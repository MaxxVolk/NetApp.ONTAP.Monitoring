using Microsoft.EnterpriseManagement.HealthService;
using Microsoft.EnterpriseManagement.Mom.Modules.DataItems;
using NetApp.Filer;
using NetApp.Filer.Cluster.Lun;
using NetApp.Filer.Cluster.Volume;
using AcmeHoldings.Library.Common;
using AcmeHoldings.NetApp.ONTAPCluster;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Xml;

namespace AcmeHoldings.NetAppONTAP.Cluster.Modules
{
  [MonitoringModule(ModuleType.ReadAction)]
  [ModuleOutput(true)]
  class VolumeSpaceProbeAction : ModuleBaseSimpleAction<PropertyBagDataItem>
  {
    // configuration
    private string ConnectionFQDN, username, password, Uuid;
    private bool? ForceUseUnsecure = null;
    private double CacheTimeout;

    public VolumeSpaceProbeAction(ModuleHost<PropertyBagDataItem> moduleHost, XmlReader configuration, byte[] previousState) : base(moduleHost, configuration, previousState)
    {
      ONTAPClusterObjectStateCache.GetObjectCache(ConnectionFQDN, new NetworkCredential(username, password), ForceUseUnsecure);
    }

    protected override void PreInitialize(ModuleHost<PropertyBagDataItem> moduleHost, XmlReader configuration, byte[] previousState)
    {
      LibraryInit.SetEventSource();
      Global.AddLoggingSource(GetType(), LibraryInit.evtid_VolumeSpaceProbeAction);
      base.PreInitialize(moduleHost, configuration, previousState);
    }

    protected override PropertyBagDataItem[] GetOutputData(DataItemBase[] inputDataItems)
    {
      // if input data item is from System.PassThroughProbe, then data item type is System.Health.StateData
      // if input data item is from System.SimpleScheduler, then data item type is System.TriggerData
      try
      {
        Dictionary<string, object> bagItem = null;
        VolumeAttributes myVolume = null;
        bool CacheRefreshed;
        bool forceUpdate = inputDataItems.Length >= 1 && inputDataItems[0].DataItemTypeName == "System.Health.StateData";
        if (forceUpdate)
        {
          // use 20 seconds timeout to 1) force cache refresh for on-demand query 2) have a period to initialize all monitors
          ONTAPClusterObjectStateCache.GetObjectCache(ConnectionFQDN).ReadCachedInventory<VolumeAttributes>(VolumeReader, 20, out CacheRefreshed);
          ONTAPClusterObjectStateCache.GetObjectCache(ConnectionFQDN).ReadCachedInventory<LunInfo>(LunReader, 20, out CacheRefreshed);
        }
        else
        {
          ONTAPClusterObjectStateCache.GetObjectCache(ConnectionFQDN).ReadCachedInventory<VolumeAttributes>(VolumeReader, CacheTimeout, out CacheRefreshed);
          ONTAPClusterObjectStateCache.GetObjectCache(ConnectionFQDN).ReadCachedInventory<LunInfo>(LunReader, CacheTimeout, out CacheRefreshed);
        }
        #region Data handling callbacks
        object VolumeReader(IList<VolumeAttributes> volumeCollection)
        {
          try
          {
            if (volumeCollection == null)
              return null;
            myVolume = volumeCollection.Where(x => x.VolumeIdAttributes?.Uuid == Uuid).FirstOrDefault();
            if (myVolume == null)
            {
              Global.logWriteWarning("Cannot find volume {0} in cache for the {1} appliance. The volume must be removed from appliance. This error should disappear with next volume discovery.", this, Uuid, ConnectionFQDN);
              return null;
            }
            bagItem = new Dictionary<string, object>
            {
              { "PerfObject", "volume" },
              { "PerfInstance", "" },
              { "PerfValue_PercentageSizeFree", 100 - (myVolume.VolumeSpaceAttributes?.PercentageSizeUsed ?? 0) },
              { "PerfValue_PercentageSnapshotReserveFree", 100 - (myVolume.VolumeSpaceAttributes?.PercentageSnapshotReserveUsed ?? 0) },
              { "PerfValue_PhysicalFreePercent", 100 - (myVolume.VolumeSpaceAttributes?.PhysicalUsedPercent ?? 0) },
              { "PerfValue_SizeMB", Global.ConvertBytes1024(myVolume.VolumeSpaceAttributes?.Size ?? 0, BinaryPrefix.Byte, BinaryPrefix.MegaByte) },
              { "PerfValue_FreeMB", Global.ConvertBytes1024(myVolume.VolumeSpaceAttributes?.SizeAvailable ?? 0, BinaryPrefix.Byte, BinaryPrefix.MegaByte) },
              { "PerfValue_TotalSizeMB", Global.ConvertBytes1024(myVolume.VolumeSpaceAttributes?.SizeTotal ?? 0, BinaryPrefix.Byte, BinaryPrefix.MegaByte) },
              { "PerfValue_MaxSizeMB", Global.ConvertBytes1024(myVolume.VolumeAutosizeAttributes?.MaximumSize ?? 0, BinaryPrefix.Byte, BinaryPrefix.MegaByte) },
              { "PerfValue_PercentageTotalSpaceSaved", myVolume.VolumeSisAttributes?.PercentageTotalSpaceSaved ?? 0 },
              { "IsAnyRoot", (myVolume.VolumeStateAttributes?.IsNodeRoot ?? false) | (myVolume.VolumeStateAttributes?.IsVserverRoot ?? false) },
              { "IsAnyMirror", (myVolume.VolumeMirrorAttributes?.IsDataProtectionMirror ?? false) | (myVolume.VolumeMirrorAttributes?.IsMoveMirror ?? false) },
              { "IsAutosizeEnabled", (myVolume.VolumeAutosizeAttributes?.IsEnabled ?? false).ToString().ToLowerInvariant() },
              { "Format_Size", Global.FormatBytes1024(myVolume.VolumeSpaceAttributes?.Size ?? 0) },
              { "Format_Free", Global.FormatBytes1024(myVolume.VolumeSpaceAttributes?.SizeAvailable ?? 0) },
              { "Format_MaxGrowSize", Global.FormatBytes1024(myVolume.VolumeAutosizeAttributes?.MaximumSize ?? 0) }
            };
            if (myVolume.VolumeAutosizeAttributes?.IsEnabled ?? false)
              bagItem.Add("PerfValue_MaxSizeReachedPercent", (((double)(myVolume.VolumeSpaceAttributes?.SizeTotal ?? 0)) / ((double)(myVolume.VolumeAutosizeAttributes?.MaximumSize ?? 1))) * 100);
            else
              bagItem.Add("PerfValue_MaxSizeReachedPercent", 0);
            return null; // no value required
          }
          catch (Exception e)
          {
            Global.logWriteException(e, this);
            return null;
          }
        }
        object LunReader(IList<LunInfo> lunCollection)
        {
          if (bagItem != null) // effectively means myVolume != null
          {
            IEnumerable<LunInfo> myLUNs = lunCollection.Where(x => x.Volume == myVolume.VolumeIdAttributes?.Name && x.Vserver == myVolume.VolumeIdAttributes?.OwningVserverName);
            if (myLUNs?.Any() ?? false)
            {
              decimal totalLUNSizse = myLUNs.Sum(x => x.Size ?? 0);
              bagItem.Add("PerfValue_TotalLUNSizeMB", Math.Round(Global.ConvertBytes1024(totalLUNSizse, BinaryPrefix.Byte, BinaryPrefix.MegaByte), 2));
              bagItem.Add("PerfValue_TotalLUNSizePercent", Math.Round(totalLUNSizse / (myVolume.VolumeSpaceAttributes?.Size ?? 1) * 100, 2));
            }
            else
            {
              bagItem.Add("PerfValue_TotalLUNSizeMB", 0);
              bagItem.Add("PerfValue_TotalLUNSizePercent", 0);
            }
          }
          return null;
        }
        #endregion
        if (bagItem != null)
          return new PropertyBagDataItem[] { CreatePropertyBag(bagItem) };
        else
          return null;
      }
      catch (Exception e)
      {
        Global.logWriteException("Volume Space Probe Action running for {0} volume failed at {1}.", e, this, Uuid, ConnectionFQDN);
        return null;
      }
    }

    protected override void LoadConfiguration(XmlReader configuration)
    {
      /*
       * <xsd:element minOccurs="1" name="ConnectionFQDN" type="xsd:string" />
       * <xsd:element minOccurs="1" name="Uuid" type="xsd:string" />
       * <xsd:element minOccurs="1" name="CacheTimeout" type="xsd:integer" />
       * <xsd:element minOccurs="1" name="username" type="xsd:string" />
       * <xsd:element minOccurs="1" name="password" type="xsd:string" />
       */
      try
      {
        // load
        XmlDocument xmlDoc = new XmlDocument();
        xmlDoc.Load(configuration);
        LoadConfigurationElement(xmlDoc, "ConnectionFQDN", out ConnectionFQDN);
        LoadConfigurationElement(xmlDoc, "Uuid", out Uuid);
        LoadConfigurationElement(xmlDoc, "CacheTimeout", out int CacheTimeout_Int);
        LoadConfigurationElement(xmlDoc, "username", out username);
        LoadConfigurationElement(xmlDoc, "password", out password);
        // parse
        CacheTimeout = CacheTimeout_Int;
        username = Helpers.TransformUsername(username);
      }
      catch (Exception xe)
      {
        Global.logWriteException("Error parsing configuration XML. This error is unrecoverable.", xe, this);
        throw new ModuleException("Error parsing configuration XML", xe);
      }
    }
  }
}
