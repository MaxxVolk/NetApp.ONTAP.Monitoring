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
  public class LUNStateProbeAction : ModuleBaseSimpleAction<PropertyBagDataItem>
  {
    // configuration
    private string ConnectionFQDN, username, password, Uuid;
    private bool? ForceUseUnsecure = null;
    private double CacheTimeout;

    public LUNStateProbeAction(ModuleHost<PropertyBagDataItem> moduleHost, XmlReader configuration, byte[] previousState) : base(moduleHost, configuration, previousState)
    {
      ONTAPClusterObjectStateCache.GetObjectCache(ConnectionFQDN, new NetworkCredential(username, password), ForceUseUnsecure);
    }

    protected override void PreInitialize(ModuleHost<PropertyBagDataItem> moduleHost, XmlReader configuration, byte[] previousState)
    {
      LibraryInit.SetEventSource();
      Global.AddLoggingSource(GetType(), LibraryInit.evtid_LUNSpaceProbeAction);
      base.PreInitialize(moduleHost, configuration, previousState);
    }

    protected override PropertyBagDataItem[] GetOutputData(DataItemBase[] inputDataItems)
    {
      try
      {
        // if input data item is from System.PassThroughProbe, then data item type is System.Health.StateData
        // if input data item is from System.SimpleScheduler, then data item type is System.TriggerData
        bool forceUpdate = false;
        if (inputDataItems.Length >= 1 && inputDataItems[0].DataItemTypeName == "System.Health.StateData")
          forceUpdate = true;
        if (forceUpdate)
        {
          // Global.logWriteWarning("Explicitly read volume usage for the " + Uuid + " volume from " + ConnectionFQDN + ".", this);
          // use 20 seconds timeout to 1) force cache refresh for on-demand query 2) have a period to initialize all monitors
          return (PropertyBagDataItem[])ONTAPClusterObjectStateCache.GetObjectCache(ConnectionFQDN).ReadCachedInventory<LunInfo>(CacheReader, 20, out bool CacheRefreshed);
        }
        else
        {
          PropertyBagDataItem[] retval = (PropertyBagDataItem[])ONTAPClusterObjectStateCache.GetObjectCache(ConnectionFQDN).ReadCachedInventory<LunInfo>(CacheReader, CacheTimeout, out bool CacheRefreshed);
          if (CacheRefreshed)
            Global.logWriteInformation("Cache refreshed.", this);
          return retval;
        }
      }
      catch (Exception e)
      {
        Global.logWriteException("LUN State Probe Action failed at {0}.", e, this, ConnectionFQDN);
        return null;
      }
    }

    private object CacheReader(IList<LunInfo> lunCollection)
    {
      try
      {
        if (lunCollection == null)
          return null;
        LunInfo myLUN = lunCollection.Where(x => x.Uuid == Uuid).FirstOrDefault();
        if (myLUN == null)
        {
          Global.logWriteWarning("Cannot find LUN " + Uuid + " in cache. The LUN must be removed from appliance. This error should disappear with next LUN discovery.", this);
          return null;
        }
        decimal sizeMB = (myLUN.Size ?? 0) / 1024 / 1024;
        decimal sizeUsedMB = (myLUN.SizeUsed ?? 0) / 1024 / 1024;
        double percentSizeFree = 100d;
        if (myLUN.Size != 0 && myLUN.Size != null)
          percentSizeFree = (1.0d - (double)((myLUN.SizeUsed ?? 0) / (myLUN.Size ?? 1))) * 100d; // myLUN.Size ?? 1 => will never coalesce
        Dictionary<string, object> bagItem = new Dictionary<string, object>
        {
          { "Online", myLUN.Online ?? false },
          { "State", myLUN.State ?? "" },
          { "IsSpaceReservationEnabled", myLUN.IsSpaceReservationEnabled ?? false },
          { "IsSpaceAllocEnabled", myLUN.IsSpaceAllocEnabled ?? false },
          { "IsRestoreInaccessible", myLUN.IsRestoreInaccessible ?? false },
          { "IsInconsistentImport", myLUN.IsInconsistentImport ?? false }
        };
        return new PropertyBagDataItem[] { CreatePropertyBag(bagItem) };
      }
      catch (Exception e)
      {
        Global.logWriteException(e, this);
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
        int CacheTimeout_Int;
        // load
        XmlDocument xmlDoc = new XmlDocument();
        xmlDoc.Load(configuration);
        LoadConfigurationElement(xmlDoc, "ConnectionFQDN", out ConnectionFQDN);
        LoadConfigurationElement(xmlDoc, "Uuid", out Uuid);
        LoadConfigurationElement(xmlDoc, "CacheTimeout", out CacheTimeout_Int);
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
