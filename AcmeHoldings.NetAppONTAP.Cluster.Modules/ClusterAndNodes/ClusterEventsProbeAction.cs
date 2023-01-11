using Microsoft.EnterpriseManagement.HealthService;
using Microsoft.EnterpriseManagement.Modules.DataItems.Discovery;
using Microsoft.EnterpriseManagement.Mom.Modules.DataItems;
using NetApp.Filer;
using NetApp.Filer.Cluster.Cluster;
using NetApp.Filer.Cluster.ClusterImage;
using AcmeHoldings.Library.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Xml;

namespace AcmeHoldings.NetAppONTAP.Cluster.Modules.ClusterAndNodes
{
  [MonitoringModule(ModuleType.ReadAction)]
  [ModuleOutput(true)]
  public class ClusterEventsProbeAction : ModuleBaseSimpleAction<PropertyBagDataItem>
  {
    // configuration
    private string ConnectionFQDN, username, password;
    private Guid sourceId, managedEntityId;
    private bool? ForceUseUnsecure = null;
    private string FilterName;

    public ClusterEventsProbeAction(ModuleHost<PropertyBagDataItem> moduleHost, XmlReader configuration, byte[] previousState) : base(moduleHost, configuration, previousState)
    {
    }

    protected override void PreInitialize(ModuleHost<PropertyBagDataItem> moduleHost, XmlReader configuration, byte[] previousState)
    {
      LibraryInit.SetEventSource();
      Global.AddLoggingSource(GetType(), LibraryInit.evtid_ClusterEventsProbeAction);
      base.PreInitialize(moduleHost, configuration, previousState);
    }

    protected override PropertyBagDataItem[] GetOutputData(DataItemBase[] inputDataItems)
    {
      try
      {
        return null;
      }
      catch (Exception e)
      {
        Global.logWriteException("Generic error in ClusterEventsProbeAction.", e, this);
        return null;
      }
    }

    // private IEnumerable<>

    protected override void LoadConfiguration(XmlReader configuration)
    {
      /*
       * <xsd:element minOccurs="1" name="sourceId" type="xsd:string" />
       * <xsd:element minOccurs="1" name="managedEntityId" type="xsd:string" />
       * <xsd:element minOccurs="1" name="ConnectionFQDN" type="xsd:string" />
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
        LoadConfigurationElement(xmlDoc, "FilterName", out FilterName);
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
      // not needed
    }
  }
}
