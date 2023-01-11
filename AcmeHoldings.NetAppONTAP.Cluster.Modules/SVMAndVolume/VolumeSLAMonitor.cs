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
  class VolumeSLAProbeAction : ModuleBaseSimpleAction<PropertyBagDataItem>
  {
    // configuration
    private string ConnectionFQDN, username, password, InstanceUuid;
    private int CollectionInterval, IOPSperTBLimit, RWRatioLimit, ResponseTime_microsec;
    private double VolumeSizeMB;
    private const string ObjectType = "volume";
    private bool? ForceUseUnsecure = null;

    // internal state
    private int breachEvents = 0;
    private bool PerformanceCollectorInitialized = false;

    public VolumeSLAProbeAction(ModuleHost<PropertyBagDataItem> moduleHost, XmlReader configuration, byte[] previousState) : base(moduleHost, configuration, previousState)
    {
      ONTAPClusterObjectStateCache.GetObjectCache(ConnectionFQDN, new NetworkCredential(username, password), null);
      InitializePerformanceCollector();
    }

    protected void InitializePerformanceCollector()
    {
      ResultWrapper<ONTAPClusterPerformanceCollector> PerfCollectorInstance = ONTAPClusterPerformanceCollector.GetPerformanceCollector(ConnectionFQDN, new NetworkCredential(username, password), ForceUseUnsecure);
      PerformanceCollectorInitialized = PerfCollectorInstance.IsOK;
      if (PerfCollectorInstance.IsOK)
        PerfCollectorInstance.Result.AddOrUpdateType(ObjectType, new string[] { "total_ops", "read_ops", "write_ops" });
      else
      {
        Global.logWriteException("Failed to initialize performance data collector for the {0} {1} instance at {2}.", PerfCollectorInstance.Exception ?? new Exception("Unknown error."), this, InstanceUuid, ObjectType, ConnectionFQDN);
      }
    }

    protected override void PreInitialize(ModuleHost<PropertyBagDataItem> moduleHost, XmlReader configuration, byte[] previousState)
    {
      LibraryInit.SetEventSource();
      Global.AddLoggingSource(GetType(), 7);
      base.PreInitialize(moduleHost, configuration, previousState);
    }

    //public override void Shutdown()
    //{
    //  // ONTAPClusterObjectStateCache.DisposePerformanceCollector(ConnectionFQDN);
    //  int pcID = ONTAPClusterPerformanceCollector.ShutdownPerformanceCollector(ConnectionFQDN);
    //  if (pcID >= 0)
    //    Global.logWriteInformation("Shutting down Performance Collector instance #" + pcID.ToString(), this);
    //  base.Shutdown();
    //}

    protected override PropertyBagDataItem[] GetOutputData(DataItemBase[] inputDataItems)
    {
      try
      {
        Dictionary<string, object> bagItem = new Dictionary<string, object>();
        double totalOps = -1;
        double IOPSperTB = -1;
        double avgLatency = -1;
        double RWRatio = -1;
        // prepare Performance Collector
        if (!PerformanceCollectorInitialized)
          InitializePerformanceCollector();
        ONTAPClusterPerformanceCollector perfMan = ONTAPClusterPerformanceCollector.GetPerformanceCollector(ConnectionFQDN, null, ForceUseUnsecure).Result;
        DateTime startStamp = perfMan.GetCurrentTimestamp(ObjectType);
        var recentData = perfMan.GetPerformanceValueRaw(ObjectType, InstanceUuid, 0, startStamp);
        var oldData = perfMan.GetPerformanceValueRaw(ObjectType, InstanceUuid, CollectionInterval, startStamp);
        if (recentData == null || oldData == null)
        {
          bagItem.Add("IOPSperTB", IOPSperTB);
          bagItem.Add("RWRatio", RWRatio);
          bagItem.Add("avgLatency", avgLatency);
          bagItem.Add("breachEvents_before", breachEvents);
          bagItem.Add("Status", "NODATA");
          return new PropertyBagDataItem[] { CreatePropertyBag(bagItem) };
        }
        // calculate all components: IOPS/TB, RW ratio, latency
        if (perfMan.GetPerformanceValue(ObjectType, InstanceUuid, "total_ops", CollectionInterval, startStamp, out totalOps))
          IOPSperTB = totalOps / (VolumeSizeMB / 1024.0 / 1024.0);
        else
        {
          bagItem.Add("IOPSperTB", IOPSperTB);
          bagItem.Add("RWRatio", RWRatio);
          bagItem.Add("avgLatency", avgLatency);
          bagItem.Add("breachEvents_before", breachEvents);
          bagItem.Add("Status", "NODATA");
          return new PropertyBagDataItem[] { CreatePropertyBag(bagItem) };
        }
        double totalReadOps = recentData.Where(x => x.name == "read_ops").First().value -
          oldData.Where(x => x.name == "read_ops").First().value;
        double totalWriteOps = recentData.Where(x => x.name == "write_ops").First().value -
          oldData.Where(x => x.name == "write_ops").First().value;
        RWRatio = totalReadOps / (totalReadOps + totalWriteOps) * 100; // %
        if (!perfMan.GetPerformanceValue(ObjectType, InstanceUuid, "avg_latency", CollectionInterval, startStamp, out avgLatency))
        {
          bagItem.Add("IOPSperTB", IOPSperTB);
          bagItem.Add("RWRatio", RWRatio);
          bagItem.Add("avgLatency", avgLatency);
          bagItem.Add("breachEvents_before", breachEvents);
          bagItem.Add("Status", "NODATA");
          return new PropertyBagDataItem[] { CreatePropertyBag(bagItem) };
        }
        // prepare bag and fill "raw" values
        bagItem.Add("IOPSperTB", IOPSperTB);
        bagItem.Add("RWRatio", RWRatio);
        bagItem.Add("avgLatency", avgLatency);
        bagItem.Add("breachEvents_before", breachEvents);
        // response time is out of SLA...
        if (avgLatency > ResponseTime_microsec)
        {
          // ... but SLA is not applicable
          if ((IOPSperTB > IOPSperTBLimit) || (RWRatio < RWRatioLimit))
          {
            bagItem.Add("Status", "STRESSED");
            return new PropertyBagDataItem[] { CreatePropertyBag(bagItem) };
          }
          else
          {
            breachEvents++;
            if (breachEvents >= 2)
            {
              bagItem.Add("Status", "BREACHED");
              return new PropertyBagDataItem[] { CreatePropertyBag(bagItem) };
            }
            else
            {
              // happens just one time
              bagItem.Add("Status", "OK");
              return new PropertyBagDataItem[] { CreatePropertyBag(bagItem) };
            }
          }
        }
        else
        {
          breachEvents = 0;
          bagItem.Add("Status", "OK");
          return new PropertyBagDataItem[] { CreatePropertyBag(bagItem) };
        }
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
       * <xsd:element minOccurs="1" name="InstanceUuid" type="xsd:string" />
       * <xsd:element minOccurs="1" name="VolumeSizeMB" type="xsd:double" />
       * <xsd:element minOccurs="1" name="CollectionInterval" type="xsd:int" />
       * <xsd:element minOccurs="1" name="IOPSperTBLimit" type="xsd:int" />
       * <xsd:element minOccurs="1" name="RWRatioLimit" type="xsd:int" />
       * <xsd:element minOccurs="1" name="ResponseTime_microsec" type="xsd:int" />
       * <xsd:element minOccurs="1" name="username" type="xsd:string" />
       * <xsd:element minOccurs="1" name="password" type="xsd:string" />
       */
      try
      {
        // load
        XmlDocument xmlDoc = new XmlDocument();
        xmlDoc.Load(configuration);
        LoadConfigurationElement(xmlDoc, "ConnectionFQDN", out ConnectionFQDN);
        LoadConfigurationElement(xmlDoc, "InstanceUuid", out InstanceUuid);
        LoadConfigurationElement(xmlDoc, "VolumeSizeMB", out VolumeSizeMB);
        LoadConfigurationElement(xmlDoc, "CollectionInterval", out CollectionInterval);
        LoadConfigurationElement(xmlDoc, "IOPSperTBLimit", out IOPSperTBLimit);
        LoadConfigurationElement(xmlDoc, "RWRatioLimit", out RWRatioLimit);
        LoadConfigurationElement(xmlDoc, "ResponseTime_microsec", out ResponseTime_microsec);
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

    protected override void LoadPreviousState(byte[] previousState)
    {
      // don't need
    }
  }
}
