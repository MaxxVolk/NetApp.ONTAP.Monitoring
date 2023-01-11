using Library.Common;
using Microsoft.EnterpriseManagement.HealthService;
using Microsoft.EnterpriseManagement.Mom.Modules.DataItems;
using NetApp.Filer.Cluster.Perf;
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
  class StorePoolExhaustProbeAction : ModuleBaseSimpleAction<PropertyBagDataItem>
  {
    // configuration
    private string ConnectionFQDN, username, password, ClusterName;
    // private int CollectionInterval;
    private bool? ForceUseUnsecure = null;
    const string ObjectType = "nfsv4_diag";
    // working data
    private bool PerformanceCollectorInitialized = false;

    public StorePoolExhaustProbeAction(ModuleHost<PropertyBagDataItem> moduleHost, XmlReader configuration, byte[] previousState) : base(moduleHost, configuration, previousState)
    {
      InitializePerformanceCollector();
    }

    protected void InitializePerformanceCollector()
    {
      ResultWrapper<ONTAPClusterPerformanceCollector> PerfCollectorInstance = ONTAPClusterPerformanceCollector.GetPerformanceCollector(ConnectionFQDN, new NetworkCredential(username, password), ForceUseUnsecure);
      PerformanceCollectorInitialized = PerfCollectorInstance.IsOK;
      if (PerfCollectorInstance.IsOK)
        PerfCollectorInstance.Result.AddOrUpdateType("nfsv4_diag", x => x.Name.IndexOf("storePool_") == 0 || x.Name.IndexOf("storepool_") == 0);
      else
      {
        Global.logWriteException("Failed to initialize performance data collector for the {0} instance at {1}.", PerfCollectorInstance.Exception ?? new Exception("Unknown error."), this, ObjectType, ConnectionFQDN);
      }
    }

    protected override void PreInitialize(ModuleHost<PropertyBagDataItem> moduleHost, XmlReader configuration, byte[] previousState)
    {
      LibraryInit.SetEventSource();
      Global.AddLoggingSource(GetType(), LibraryInit.evtid_StorePoolExhaustProbeAction);
      base.PreInitialize(moduleHost, configuration, previousState);
    }

    protected override PropertyBagDataItem[] GetOutputData(DataItemBase[] inputDataItems)
    {
      try
      {
        if (!PerformanceCollectorInitialized)
          InitializePerformanceCollector();
        if (PerformanceCollectorInitialized)
        {
          ResultWrapper<ONTAPClusterPerformanceCollector> perfMan = ONTAPClusterPerformanceCollector.GetPerformanceCollector(ConnectionFQDN, null, ForceUseUnsecure);
          if (perfMan.IsOK)
          {
            DateTime startStamp = perfMan.Result.GetCurrentTimestamp(ObjectType);
            InstanceData rawData = perfMan?.Result.GetMostRecentPerformanceValuesRaw("nfsv4_diag")?.Where(x => x.Uuid == $"{ClusterName}:kernel:nfs4_diag").FirstOrDefault();
            if (rawData == null)
            {
              Global.logWriteWarning("No Store Pool performance counters found for the {0} cluster at {1}", this, ClusterName, ConnectionFQDN);
              return null;
            }
            string countersDump = "";
            string maxingCounterName = "";
            double maxUsage = 0;
            foreach (CounterData baseCounter in rawData.Counters.Where(x => x.Name.EndsWith("Alloc", StringComparison.InvariantCultureIgnoreCase)))
            {
              string maxCounterName = baseCounter.Name.ToUpperInvariant().Replace("ALLOC", "MAX");
              CounterData maxCounter = rawData.Counters.Where(x => x.Name.ToUpperInvariant() == maxCounterName).FirstOrDefault();
              if (maxCounter == null)
              {
                Global.logWriteWarning("Missing corresponding MAX counter to the {0} ALLOC counter.", this, baseCounter.Name);
                continue;
              }
              double counterUsage = Convert.ToDouble(baseCounter.Value) / Convert.ToDouble(maxCounter.Value) * 100d;
              countersDump += $"{baseCounter.Name}: {baseCounter.Value} out of {maxCounter.Value}\r\n";
              if (counterUsage > maxUsage)
              {
                maxUsage = counterUsage;
                maxingCounterName = baseCounter.Name;
              }
            }
            return new PropertyBagDataItem[1] { CreatePropertyBag(new Dictionary<string, object>
            {
              { "AllCounters", countersDump },
              { "MaxUsage", Math.Round(maxUsage, 2) },
              { "MaxingOutCounter", maxingCounterName }
            }) };
          }
          else
            return null;
        }
        else
        {
          return null;
        }
      }
      catch (Exception e)
      {
        Global.logWriteException("Failed to collect Store Pool performance data for the {0} cluster at {1}.", e, this, ClusterName, ConnectionFQDN);
        return null;
      }
    }

    protected override void LoadConfiguration(XmlReader configuration)
    {
      /*
       * <xsd:element minOccurs="1" name="ConnectionFQDN" type="xsd:string" />
       * <xsd:element minOccurs="1" name="ClusterName" type="xsd:string" />
       * <xsd:element minOccurs="1" name="username" type="xsd:string" />
       * <xsd:element minOccurs="1" name="password" type="xsd:string" />
       */
      try
      {
        // load
        XmlDocument xmlDoc = new XmlDocument();
        xmlDoc.Load(configuration);
        LoadConfigurationElement(xmlDoc, "ConnectionFQDN", out ConnectionFQDN);
        LoadConfigurationElement(xmlDoc, "ClusterName", out ClusterName);
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
