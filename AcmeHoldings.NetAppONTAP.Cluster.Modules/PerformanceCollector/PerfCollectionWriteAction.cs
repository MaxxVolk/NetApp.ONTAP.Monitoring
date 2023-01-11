using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Xml;
using Library.Common;
using Microsoft.EnterpriseManagement.HealthService;
using AcmeHoldings.Library.Common;
using AcmeHoldings.NetApp.ONTAPCluster;

namespace AcmeHoldings.NetAppONTAP.Cluster.Modules
{
  [MonitoringModule(ModuleType.WriteAction)]
  [ModuleOutput(false)]
  class PerfCollectionWriteAction : ModuleBaseSimpleAction<DataItemBase>
  {
    // configuration
    private string ConnectionFQDN, username, password;
    private bool? ForceUseUnsecure = null;

    // private data
    ONTAPClusterPerformanceCollector myPerfInstance;

    public PerfCollectionWriteAction(ModuleHost<DataItemBase> moduleHost, XmlReader configuration, byte[] previousState) : base(moduleHost, configuration, previousState)
    {
      ResultWrapper<ONTAPClusterPerformanceCollector> initResult = ONTAPClusterPerformanceCollector.GetPerformanceCollector(ConnectionFQDN, new NetworkCredential(username, password), ForceUseUnsecure);
      myPerfInstance = initResult.IsOK ? initResult.Result : null;
    }

    protected override void PreInitialize(ModuleHost<DataItemBase> moduleHost, XmlReader configuration, byte[] previousState)
    {
      LibraryInit.SetEventSource();
      Global.AddLoggingSource(GetType(), LibraryInit.evtid_ONTAPClusterPerformanceRefresher);
      base.PreInitialize(moduleHost, configuration, previousState);
    }

    protected override DataItemBase[] GetOutputData(DataItemBase[] inputDataItems)
    {
      try
      {
        if (myPerfInstance == null)
        {
          ResultWrapper<ONTAPClusterPerformanceCollector> initResult = ONTAPClusterPerformanceCollector.GetPerformanceCollector(ConnectionFQDN, new NetworkCredential(username, password), ForceUseUnsecure);
          myPerfInstance = initResult.IsOK ? initResult.Result : null;
        }
        myPerfInstance?.TimedPerformanceCollector();
      }
      catch (Exception e)
      {
        Global.logWriteException(e, this);
      }
      return null;
    }

    protected override void LoadConfiguration(XmlReader configuration)
    {
      /*
       * <xsd:element minOccurs="1" name="ConnectionFQDN" type="xsd:string" />
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
