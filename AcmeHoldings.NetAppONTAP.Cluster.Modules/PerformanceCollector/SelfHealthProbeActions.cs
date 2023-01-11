using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Xml;
using Library.Common;
using Microsoft.EnterpriseManagement.HealthService;
using Microsoft.EnterpriseManagement.Mom.Modules.DataItems;
using AcmeHoldings.Library.Common;
using AcmeHoldings.NetApp.ONTAPCluster;

namespace AcmeHoldings.NetAppONTAP.Cluster.Modules
{
  [MonitoringModule(ModuleType.ReadAction)]
  [ModuleOutput(true)]
  class PerformanceCollectorHealthProbeAction : ModuleBaseSimpleAction<PropertyBagDataItem>
  {
    // configuration
    private string ConnectionFQDN, username, password;
    private bool? ForceUseUnsecure = null;
    private double MaximumPerfDataAge = 300;

    public PerformanceCollectorHealthProbeAction(ModuleHost<PropertyBagDataItem> moduleHost, XmlReader configuration, byte[] previousState) : base(moduleHost, configuration, previousState)
    {
      ONTAPClusterPerformanceCollector.GetPerformanceCollector(ConnectionFQDN, new NetworkCredential(username, password), ForceUseUnsecure);
    }

    protected override void PreInitialize(ModuleHost<PropertyBagDataItem> moduleHost, XmlReader configuration, byte[] previousState)
    {
      LibraryInit.SetEventSource();
      Global.AddLoggingSource(GetType(), LibraryInit.evtid_PerformanceCollectorHealthProbeAction);
      base.PreInitialize(moduleHost, configuration, previousState);
    }

    protected override PropertyBagDataItem[] GetOutputData(DataItemBase[] inputDataItems)
    {
      try
      {
        Dictionary<string, object> bagItem = new Dictionary<string, object>();
        ResultWrapper<ONTAPClusterPerformanceCollector> getPCResult = ONTAPClusterPerformanceCollector.GetPerformanceCollector(ConnectionFQDN);
        if (getPCResult.IsOK)
        {
          if (getPCResult.Result.PerformanceCollectionInitialized)
          {
            if (ONTAPClusterPerformanceCollector.GetPerformanceCollector(ConnectionFQDN).Result.PerformanceDataAgeSeconds < MaximumPerfDataAge)
              bagItem.Add("PerfCollectorState", "UPTODATE");
            else
              bagItem.Add("PerfCollectorState", "STALE");
          }
          else
            bagItem.Add("PerfCollectorState", "UPTODATE");
        }
        else
          bagItem.Add("PerfCollectorState", "FAILURE");

        return new PropertyBagDataItem[] { CreatePropertyBag(bagItem) };
      }
      catch (Exception e)
      {
        Global.logWriteException("Performance Collector Health Probe Action failed at {0}.", e, this, ConnectionFQDN);
        return null;
      }
    }

    protected override void LoadConfiguration(XmlReader configuration)
    {
      /*
       * <xsd:element minOccurs="1" name="ConnectionFQDN" type="xsd:string" />
       * <xsd:element minOccurs="1" name="MaximumPerfDataAge" type="xsd:integer" />
       * <xsd:element minOccurs="1" name="username" type="xsd:string" />
       * <xsd:element minOccurs="1" name="password" type="xsd:string" />
       */
      try
      {
        int MaximumPerfDataAge_Int;
        // load
        XmlDocument xmlDoc = new XmlDocument();
        xmlDoc.Load(configuration);
        LoadConfigurationElement(xmlDoc, "ConnectionFQDN", out ConnectionFQDN);
        LoadConfigurationElement(xmlDoc, "MaximumPerfDataAge", out MaximumPerfDataAge_Int);
        LoadConfigurationElement(xmlDoc, "username", out username);
        LoadConfigurationElement(xmlDoc, "password", out password);
        // parse
        MaximumPerfDataAge = MaximumPerfDataAge_Int;
        username = Helpers.TransformUsername(username);
      }
      catch (Exception xe)
      {
        Global.logWriteException("Error parsing configuration XML. This error is unrecoverable.", xe, this);
        throw new ModuleException("Error parsing configuration XML", xe);
      }
    }
  }

  [MonitoringModule(ModuleType.ReadAction)]
  [ModuleOutput(true)]
  class ObjectCacheHealthProbeAction : ModuleBaseSimpleAction<PropertyBagDataItem>
  {
    // configuration
    private string ConnectionFQDN, username, password;
    private bool? ForceUseUnsecure = null;
    private double MaximumCacheAge = 300;

    public ObjectCacheHealthProbeAction(ModuleHost<PropertyBagDataItem> moduleHost, XmlReader configuration, byte[] previousState) : base(moduleHost, configuration, previousState)
    {
      ONTAPClusterObjectStateCache.GetObjectCache(ConnectionFQDN, new NetworkCredential(username, password), ForceUseUnsecure);
    }

    protected override void PreInitialize(ModuleHost<PropertyBagDataItem> moduleHost, XmlReader configuration, byte[] previousState)
    {
      LibraryInit.SetEventSource();
      Global.AddLoggingSource(GetType(), LibraryInit.evtid_ObjectCacheHealthProbeAction);
      base.PreInitialize(moduleHost, configuration, previousState);
    }

    protected override PropertyBagDataItem[] GetOutputData(DataItemBase[] inputDataItems)
    {
      try
      {
        Dictionary<string, object> bagItem = new Dictionary<string, object>();
        if (ONTAPClusterObjectStateCache.GetObjectCache(ConnectionFQDN).CacheAgeSeconds < MaximumCacheAge)
          bagItem.Add("ObjectCacheState", "UPTODATE");
        else
          bagItem.Add("ObjectCacheState", "STALE");
        return new PropertyBagDataItem[] { CreatePropertyBag(bagItem) };
      }
      catch (Exception e)
      {
        Global.logWriteException("Object Cache Health Probe Action failed at {0}.", e, this, ConnectionFQDN);
        return null;
      }
    }

    protected override void LoadConfiguration(XmlReader configuration)
    {
      /*
       * <xsd:element minOccurs="1" name="ConnectionFQDN" type="xsd:string" />
       * <xsd:element minOccurs="1" name="MaximumCacheAge" type="xsd:integer" />
       * <xsd:element minOccurs="1" name="username" type="xsd:string" />
       * <xsd:element minOccurs="1" name="password" type="xsd:string" />
       */
      try
      {
        int MaximumCacheAge_Int;
        // load
        XmlDocument xmlDoc = new XmlDocument();
        xmlDoc.Load(configuration);
        LoadConfigurationElement(xmlDoc, "ConnectionFQDN", out ConnectionFQDN);
        LoadConfigurationElement(xmlDoc, "MaximumCacheAge", out MaximumCacheAge_Int);
        LoadConfigurationElement(xmlDoc, "username", out username);
        LoadConfigurationElement(xmlDoc, "password", out password);
        // parse
        MaximumCacheAge = MaximumCacheAge_Int;
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
