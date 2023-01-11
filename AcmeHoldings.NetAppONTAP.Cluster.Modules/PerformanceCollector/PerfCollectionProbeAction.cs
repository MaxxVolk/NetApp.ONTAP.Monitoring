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
  class PerfCollectionProbeAction : ModuleBaseSimpleAction<PropertyBagDataItem>
  {
    // configuration
    private string ConnectionFQDN, username, password, InstanceUuid, ObjectType, Counter;
    private int CollectionInterval;
    private bool? ForceUseUnsecure = null;
    // working data
    private bool PerformanceCollectorInitialized = false;

    public PerfCollectionProbeAction(ModuleHost<PropertyBagDataItem> moduleHost, XmlReader configuration, byte[] previousState) : base(moduleHost, configuration, previousState)
    {
      InitializePerformanceCollector();
    }

    protected void InitializePerformanceCollector()
    {
      ResultWrapper<ONTAPClusterPerformanceCollector> PerfCollectorInstance = ONTAPClusterPerformanceCollector.GetPerformanceCollector(ConnectionFQDN, new NetworkCredential(username, password), ForceUseUnsecure);
      PerformanceCollectorInitialized = PerfCollectorInstance.IsOK;
      if (PerfCollectorInstance.IsOK)
        PerfCollectorInstance.Result.AddOrUpdateType(ObjectType, new string[] { Counter });
      else
      {
        Global.logWriteException("Failed to initialize performance data collector for the {0} {1} instance at {2}.", PerfCollectorInstance.Exception ?? new Exception("Unknown error."), this, InstanceUuid, ObjectType, ConnectionFQDN);
      }
    }

    protected override void PreInitialize(ModuleHost<PropertyBagDataItem> moduleHost, XmlReader configuration, byte[] previousState)
    {
      LibraryInit.SetEventSource();
      Global.AddLoggingSource(GetType(), LibraryInit.evtid_PerfCollectionProbeAction);
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
            if (perfMan.Result.GetPerformanceValue(ObjectType, InstanceUuid, Counter, CollectionInterval, startStamp, out double Result))
            {
              Dictionary<string, object> bagItem = new Dictionary<string, object>
            {
              { "PerfObject", ObjectType },
              { "PerfCounter", Counter },
              { "PerfInstance", "" },
              { "PerfValue", Result }
            };
              return new PropertyBagDataItem[] { CreatePropertyBag(bagItem) };
            }
            else
              return null;
          }
          else
          {
            Global.logWriteException("Failed to collect performance data for the {0} {1} instance at {2}.", perfMan.Exception ?? new Exception("Unknown error."), this, InstanceUuid, ObjectType, ConnectionFQDN);
            return null;
          }
        }
        else
        {
          return null;
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
       * <xsd:element minOccurs="1" name="ObjectType" type="xsd:string" />
       * <xsd:element minOccurs="1" name="InstanceUuid" type="xsd:string" />
       * <xsd:element minOccurs="1" name="Counter" type="xsd:string" />
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
        LoadConfigurationElement(xmlDoc, "Counter", out Counter);
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
