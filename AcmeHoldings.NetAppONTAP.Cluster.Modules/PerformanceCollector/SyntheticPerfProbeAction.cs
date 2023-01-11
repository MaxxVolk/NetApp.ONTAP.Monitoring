using Library.Common;
using Microsoft.EnterpriseManagement.HealthService;
using Microsoft.EnterpriseManagement.Mom.Modules.DataItems;
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
  class SyntheticPerfReadWriteRatioProbeAction : ModuleBaseSimpleAction<PropertyBagDataItem>
  {
    // configuration
    private string ConnectionFQDN, username, password, InstanceUuid, ObjectType;
    private int CollectionInterval;
    private bool? ForceUseUnsecure = null;
    // working data
    private bool PerformanceCollectorInitialized = false;

    public SyntheticPerfReadWriteRatioProbeAction(ModuleHost<PropertyBagDataItem> moduleHost, XmlReader configuration, byte[] previousState) : base(moduleHost, configuration, previousState)
    {
      InitializePerformanceCollector();
    }

    protected void InitializePerformanceCollector()
    {
      ResultWrapper<ONTAPClusterPerformanceCollector> PerfCollectorInstance = ONTAPClusterPerformanceCollector.GetPerformanceCollector(ConnectionFQDN, new NetworkCredential(username, password), ForceUseUnsecure);
      PerformanceCollectorInitialized = PerfCollectorInstance.IsOK;
      if (PerfCollectorInstance.IsOK)
        PerfCollectorInstance.Result.AddOrUpdateType(ObjectType, new string[] { "read_ops", "write_ops" });
      else
      {
        Global.logWriteException("Failed to initialize performance data collector for the {0} {1} instance at {2}.", PerfCollectorInstance.Exception ?? new Exception("Unknown error."), this, InstanceUuid, ObjectType, ConnectionFQDN);
      }
    }

    protected override void PreInitialize(ModuleHost<PropertyBagDataItem> moduleHost, XmlReader configuration, byte[] previousState)
    {
      LibraryInit.SetEventSource();
      Global.AddLoggingSource(GetType(), 4);
      base.PreInitialize(moduleHost, configuration, previousState);
    }

    protected override PropertyBagDataItem[] GetOutputData(DataItemBase[] inputDataItems)
    {
      try
      {
        if (!PerformanceCollectorInitialized)
          InitializePerformanceCollector();
        ONTAPClusterPerformanceCollector perfMan = ONTAPClusterPerformanceCollector.GetPerformanceCollector(ConnectionFQDN, null, ForceUseUnsecure).Result;
        DateTime startStamp = perfMan.GetCurrentTimestamp(ObjectType);
        CounterNameDataPair[] recentData = perfMan.GetPerformanceValueRaw(ObjectType, InstanceUuid, 0, startStamp);
        CounterNameDataPair[] oldData = perfMan.GetPerformanceValueRaw(ObjectType, InstanceUuid, CollectionInterval, startStamp);
        if (recentData == null || oldData == null)
          return null;
         
        double totalReadOps = recentData.Where(x => x.name == "read_ops").First().value - oldData.Where(x => x.name == "read_ops").First().value;
        double totalWriteOps = recentData.Where(x => x.name == "write_ops").First().value - oldData.Where(x => x.name == "write_ops").First().value;
        double RWRatio = 100; // default to read if no rw activity
        if (totalReadOps + totalWriteOps != 0)
          RWRatio = totalReadOps / (totalReadOps + totalWriteOps) * 100; // %

        if (!double.IsNaN(RWRatio) && !double.IsInfinity(RWRatio))
        {
          Dictionary<string, object> bagItem = new Dictionary<string, object>
          {
            { "PerfObject", ObjectType },
            { "PerfCounter", "rw_ops_ratio" },
            { "PerfInstance", "" },
            { "PerfValue", RWRatio }
          };
          return new PropertyBagDataItem[] { CreatePropertyBag(bagItem) };
        }
        else
          return null;
      }
      catch (Exception e)
      {
        Global.logWriteException("Failed to collect performance data for the {0} {1} instance at {2}.", e, this, InstanceUuid, ObjectType, ConnectionFQDN);
        return null;
      }
    }

    protected override void LoadConfiguration(XmlReader configuration)
    {
      /*
       * <xsd:element minOccurs="1" name="ConnectionFQDN" type="xsd:string" />
       * <xsd:element minOccurs="1" name="ObjectType" type="xsd:string" />
       * <xsd:element minOccurs="1" name="InstanceUuid" type="xsd:string" />
       * <xsd:element minOccurs="1" name="CollectionInterval" type="xsd:int" />
       * <xsd:element minOccurs="1" name="username" type="xsd:string" />
       * <xsd:element minOccurs="1" name="password" type="xsd:string" />
       */
      try
      {
        // load
        XmlDocument xmlDoc = new XmlDocument();
        xmlDoc.Load(configuration);
        LoadConfigurationElement(xmlDoc, "ConnectionFQDN", out ConnectionFQDN);
        LoadConfigurationElement(xmlDoc, "ObjectType", out ObjectType);
        LoadConfigurationElement(xmlDoc, "InstanceUuid", out InstanceUuid);
        LoadConfigurationElement(xmlDoc, "CollectionInterval", out CollectionInterval);
        LoadConfigurationElement(xmlDoc, "username", out username);
        LoadConfigurationElement(xmlDoc, "password", out password);
        // parse
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
  class SyntheticPerfThroughputProbeAction : ModuleBaseSimpleAction<PropertyBagDataItem>
  {
    // configuration
    private string ConnectionFQDN, username, password, InstanceUuid, ObjectType;
    private int CollectionInterval;
    private double VolumeSizeMB;
    private bool? ForceUseUnsecure = null;
    // working data
    private bool PerformanceCollectorInitialized = false;

    public SyntheticPerfThroughputProbeAction(ModuleHost<PropertyBagDataItem> moduleHost, XmlReader configuration, byte[] previousState) : base(moduleHost, configuration, previousState)
    {
      InitializePerformanceCollector();
    }

    protected void InitializePerformanceCollector()
    {
      ResultWrapper<ONTAPClusterPerformanceCollector> PerfCollectorInstance = ONTAPClusterPerformanceCollector.GetPerformanceCollector(ConnectionFQDN, new NetworkCredential(username, password), ForceUseUnsecure);
      PerformanceCollectorInitialized = PerfCollectorInstance.IsOK;
      if (PerfCollectorInstance.IsOK)
        PerfCollectorInstance.Result.AddOrUpdateType(ObjectType, new string[] { "total_ops" });
      else
      {
        Global.logWriteException("Failed to initialize performance data collector for the {0} {1} instance at {2}.", PerfCollectorInstance.Exception ?? new Exception("Unknown error."), this, InstanceUuid, ObjectType, ConnectionFQDN);
      }
    }

    protected override void PreInitialize(ModuleHost<PropertyBagDataItem> moduleHost, XmlReader configuration, byte[] previousState)
    {
      LibraryInit.SetEventSource();
      Global.AddLoggingSource(GetType(), 5);
      base.PreInitialize(moduleHost, configuration, previousState);
    }

    protected override PropertyBagDataItem[] GetOutputData(DataItemBase[] inputDataItems)
    {
      try
      {
        if (!PerformanceCollectorInitialized)
          InitializePerformanceCollector();
        double totalOps = -1;
        ONTAPClusterPerformanceCollector perfMan = ONTAPClusterPerformanceCollector.GetPerformanceCollector(ConnectionFQDN, null, ForceUseUnsecure).Result;
        DateTime startStamp = perfMan.GetCurrentTimestamp(ObjectType);
        if (perfMan.GetPerformanceValue(ObjectType, InstanceUuid, "total_ops", CollectionInterval, startStamp, out totalOps))
        {
          double IOPSperTB = totalOps / (VolumeSizeMB / 1024.0 / 1024.0);
          Dictionary<string, object> bagItem = new Dictionary<string, object>
          {
            { "PerfObject", ObjectType },
            { "PerfCounter", "IOPS_per_TB" },
            { "PerfInstance", "" },
            { "PerfValue", IOPSperTB }
          };
          return new PropertyBagDataItem[] { CreatePropertyBag(bagItem) };
        }
        else
          return null;
      }
      catch (Exception e)
      {
        Global.logWriteException("Failed to collect performance data for the {0} {1} instance at {2}.", e, this, InstanceUuid, ObjectType, ConnectionFQDN);
        return null;
      }
    }

    protected override void LoadConfiguration(XmlReader configuration)
    {
      /*
       * <xsd:element minOccurs="1" name="ConnectionFQDN" type="xsd:string" />
       * <xsd:element minOccurs="1" name="ObjectType" type="xsd:string" />
       * <xsd:element minOccurs="1" name="InstanceUuid" type="xsd:string" />
       * <xsd:element minOccurs="1" name="VolumeSizeMB" type="xsd:double" />
       * <xsd:element minOccurs="1" name="CollectionInterval" type="xsd:int" />
       * <xsd:element minOccurs="1" name="username" type="xsd:string" />
       * <xsd:element minOccurs="1" name="password" type="xsd:string" />
       */
      try
      {
        // load
        XmlDocument xmlDoc = new XmlDocument();
        xmlDoc.Load(configuration);
        LoadConfigurationElement(xmlDoc, "ConnectionFQDN", out ConnectionFQDN);
        LoadConfigurationElement(xmlDoc, "ObjectType", out ObjectType);
        LoadConfigurationElement(xmlDoc, "InstanceUuid", out InstanceUuid);
        LoadConfigurationElement(xmlDoc, "VolumeSizeMB", out VolumeSizeMB);
        LoadConfigurationElement(xmlDoc, "CollectionInterval", out CollectionInterval);
        LoadConfigurationElement(xmlDoc, "username", out username);
        LoadConfigurationElement(xmlDoc, "password", out password);
        // parse
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
