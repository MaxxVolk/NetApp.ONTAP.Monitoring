using Microsoft.EnterpriseManagement.HealthService;
using Microsoft.EnterpriseManagement.Mom.Modules.DataItems;
using NetApp.Filer.Cluster.Aggr;
using AcmeHoldings.Library.Common;
using AcmeHoldings.NetApp.ONTAPCluster;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Xml;

namespace AcmeHoldings.NetAppONTAP.Cluster.Modules
{
  [MonitoringModule(ModuleType.ReadAction)]
  [ModuleOutput(true)]
  class AggrSpaceProbeAction : ModuleBaseSimpleAction<PropertyBagDataItem>
  {
    // configuration
    private string ConnectionFQDN, username, password, Uuid;
    private bool? ForceUseUnsecure = null;
    private double CacheTimeout;

    public AggrSpaceProbeAction(ModuleHost<PropertyBagDataItem> moduleHost, XmlReader configuration, byte[] previousState) : base(moduleHost, configuration, previousState)
    {
      ONTAPClusterObjectStateCache.GetObjectCache(ConnectionFQDN, new NetworkCredential(username, password), ForceUseUnsecure);
    }

    protected override void PreInitialize(ModuleHost<PropertyBagDataItem> moduleHost, XmlReader configuration, byte[] previousState)
    {
      LibraryInit.SetEventSource();
      Global.AddLoggingSource(GetType(), LibraryInit.evtid_AggrSpaceProbeAction);
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
          // Global.logWriteWarning("Explicitly read aggregate usage for the " + Uuid + " aggregate from " + ConnectionFQDN + ".", this);
          // use 20 seconds timeout to 1) force cache refresh for on-demand query 2) have a period to initialize all monitors
          return (PropertyBagDataItem[])ONTAPClusterObjectStateCache.GetObjectCache(ConnectionFQDN).ReadCachedInventory<AggrAttributes>(CacheReader, 20, out bool CacheRefreshed);
        }
        else
        {
          var retval = (PropertyBagDataItem[])ONTAPClusterObjectStateCache.GetObjectCache(ConnectionFQDN).ReadCachedInventory<AggrAttributes>(CacheReader, CacheTimeout, out bool CacheRefreshed);
          return retval;
        }
      }
      catch (Exception e)
      {
        Global.logWriteException("AggrSpaceProbeAction failed for the {0} appliance.", e, this, ConnectionFQDN);
        return null;
      }
    }

    private PropertyBagDataItem[] CacheReader(IList<AggrAttributes> aggrCollection)
    {
      try
      {
        if (aggrCollection == null)
          return null;
        AggrAttributes myAggr = aggrCollection.Where(x => x?.AggregateUuid == Uuid)?.FirstOrDefault();
        if (myAggr == null)
        {
          Global.logWriteWarning("Cannot find aggregate {0} in cache for the {1} appliance. The aggregate must be removed from appliance. This error should disappear with next aggregate discovery.", this, Uuid, ConnectionFQDN);
          return null;
        }
        Dictionary<string, object> bagItem = new Dictionary<string, object>
        {
          { "PerfObject", "aggr" },
          { "PerfInstance", "" },
          { "PerfValue_PercentageSizeFree", 100.0d - ((myAggr.AggrSpaceAttributes?.SizeTotal ?? 0) == 0 ? 0 : ((double)(myAggr.AggrSpaceAttributes?.SizeUsed ?? 0) / (double)(myAggr.AggrSpaceAttributes?.SizeTotal ?? 0) * 100)) }, // if Size == 0, then count it 100% free => there won't be division by 0 in the coalesce operator
          { "PerfValue_SizeMB", Global.ConvertBytes1024(myAggr.AggrSpaceAttributes?.SizeTotal ?? 0, BinaryPrefix.Byte, BinaryPrefix.MegaByte) },
          { "PerfValue_FreeMB", Global.ConvertBytes1024(myAggr.AggrSpaceAttributes?.SizeAvailable ?? 0, BinaryPrefix.Byte, BinaryPrefix.MegaByte) },
          { "PerfValue_FilesTotal", myAggr.AggrInodeAttributes?.FilesTotal ?? -1 },
          { "PerfValue_FlexvolCount", myAggr.AggrVolumeCountAttributes?.FlexvolCount ?? -1 },
          { "PerfValue_DiskCount", myAggr.AggrRaidAttributes?.DiskCount ?? -1 },
          { "Format_Size", Global.FormatBytes1024(myAggr.AggrSpaceAttributes?.SizeTotal ?? -1) },
          { "Format_Free", Global.FormatBytes1024(myAggr.AggrSpaceAttributes?.SizeAvailable ?? 0) },
          { "Format_PercentageSizeFree", (myAggr.AggrSpaceAttributes?.SizeTotal ?? 0) == 0 ? "<Not Available>" : (100.0d -  ((double)(myAggr.AggrSpaceAttributes?.SizeUsed ?? 0) / (double)(myAggr.AggrSpaceAttributes?.SizeTotal ?? 0) * 100)).ToString("N2") },
          { "IsRoot", myAggr.AggrRaidAttributes?.IsRootAggregate ?? false }
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
