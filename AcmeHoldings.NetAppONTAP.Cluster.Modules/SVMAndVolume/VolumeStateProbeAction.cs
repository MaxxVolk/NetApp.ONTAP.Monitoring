using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Xml;
using Microsoft.EnterpriseManagement.HealthService;
using Microsoft.EnterpriseManagement.Mom.Modules.DataItems;
using NetApp.Filer.Cluster.Volume;
using AcmeHoldings.Library.Common;
using AcmeHoldings.NetApp.ONTAPCluster;

namespace AcmeHoldings.NetAppONTAP.Cluster.Modules
{
  [MonitoringModule(ModuleType.ReadAction)]
  [ModuleOutput(true)]
  class VolumeStateProbeAction : ModuleBaseSimpleAction<PropertyBagDataItem>
  {
    // configuration
    private string ConnectionFQDN, username, password, Uuid;
    private bool? ForceUseUnsecure = null;
    private double CacheTimeout = 300;

    public VolumeStateProbeAction(ModuleHost<PropertyBagDataItem> moduleHost, XmlReader configuration, byte[] previousState) : base(moduleHost, configuration, previousState)
    {
      ONTAPClusterObjectStateCache.GetObjectCache(ConnectionFQDN, new NetworkCredential(username, password), ForceUseUnsecure);
    }

    protected override void PreInitialize(ModuleHost<PropertyBagDataItem> moduleHost, XmlReader configuration, byte[] previousState)
    {
      LibraryInit.SetEventSource();
      Global.AddLoggingSource(GetType(), LibraryInit.evtid_VolumeStateProbeAction);
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
          // Global.logWriteWarning("Explicitly read volume state for the " + Uuid + " volume from " + ConnectionFQDN + ".", this);
          // use 20 seconds timeout to 1) force cache refresh for on-demand query 2) have a period to initialize all monitors
          return (PropertyBagDataItem[])ONTAPClusterObjectStateCache.GetObjectCache(ConnectionFQDN).ReadCachedInventory<VolumeAttributes>(CacheReader, 20, out bool CacheRefreshed);
        }
        else
        {
          return (PropertyBagDataItem[])ONTAPClusterObjectStateCache.GetObjectCache(ConnectionFQDN).ReadCachedInventory<VolumeAttributes>(CacheReader, CacheTimeout, out bool CacheRefreshed);
        }
      }
      catch (Exception e)
      {
        Global.logWriteException("Volume State Probe Action running for {0} volume failed at {1}.", e, this, Uuid, ConnectionFQDN);
        return null;
      }
    }

    private object CacheReader(IList<VolumeAttributes> volumeCollection)
    {
      try
      {
        if (volumeCollection == null)
          return null;
        VolumeAttributes myVolume = volumeCollection.Where(x => x.VolumeIdAttributes.Uuid == Uuid).FirstOrDefault();
        if (myVolume == null)
        {
          Global.logWriteWarning("Cannot find volume {0} in cache for the {1} appliance. The volume must be removed from appliance. This error should disappear with next volume discovery.", this, Uuid, ConnectionFQDN);
          return null;
        }
        VolumeStateAttributes volumeState = myVolume.VolumeStateAttributes;
        if (volumeState == null)
          return null;
        Dictionary<string, object> bagItem = new Dictionary<string, object>
        {
          { "NvfailedState", volumeState.InNvfailedState ?? false },
          { "Inconsistent", volumeState.IsInconsistent ?? false },
          { "Invalid", volumeState.IsInvalid ?? false },
          { "Unrecoverable", volumeState.IsUnrecoverable ?? false },
          { "VolumeInCutover", volumeState.IsVolumeInCutover ?? false },
          { "State", volumeState.State ?? "" }
        };

        return new PropertyBagDataItem[] { CreatePropertyBag(bagItem) };
      }
      catch (Exception e)
      {
        Global.logWriteException("Volume State Probe Action running for {0} volume failed at {1}.", e, this, Uuid, ConnectionFQDN);
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
