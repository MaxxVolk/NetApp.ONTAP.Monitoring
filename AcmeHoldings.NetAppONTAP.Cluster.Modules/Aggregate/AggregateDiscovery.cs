using Microsoft.EnterpriseManagement.HealthService;
using Microsoft.EnterpriseManagement.Modules.DataItems.Discovery;
using NetApp.Filer;
using NetApp.Filer.Cluster.Aggr;
using AcmeHoldings.Library.Common;
using AcmeHoldings.Library.SCOM;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Xml;
using SCOMId.System;

namespace AcmeHoldings.NetAppONTAP.Cluster.Modules
{
  [MonitoringModule(ModuleType.ReadAction)]
  [ModuleOutput(true)]
  class AggregateDiscovery : ModuleBaseSimpleAction<DiscoveryDataItem>
  {
    // configuration
    private string ConnectionFQDN, username, password, ClusterUuid, SerialNumber;
    private Guid sourceId, managedEntityId;
    private bool? ForceUseUnsecure = null;

    public AggregateDiscovery(ModuleHost<DiscoveryDataItem> moduleHost, XmlReader configuration, byte[] previousState) : base(moduleHost, configuration, previousState)
    {
    }

    protected override void PreInitialize(ModuleHost<DiscoveryDataItem> moduleHost, XmlReader configuration, byte[] previousState)
    {
      LibraryInit.SetEventSource();
      Global.AddLoggingSource(GetType(), LibraryInit.evtid_AggregateDiscovery);
      base.PreInitialize(moduleHost, configuration, previousState);
    }

    protected override DiscoveryDataItem[] GetOutputData(DataItemBase[] inputDataItems)
    {
      try
      {
        // prepare SCOM discovery data containers
        List<ClassInstance> DiscoveredObjects = new List<ClassInstance>();
        List<RelationshipInstance> DiscoveredRelationships = new List<RelationshipInstance>();
        // setup appliance connection
        NaFiler server = new NaFiler(ConnectionFQDN);
        server.Credentials = new NetworkCredential(username, password);
        if (ForceUseUnsecure != null)
        {
          if (ForceUseUnsecure == true)
            server.ForceUseUnsecure = true;
          else
            server.ForceUseUnsecure = false;
        }

        AggrGetIter getAggrs = new AggrGetIter();
        List<AggrAttributes> allAggrs = new List<AggrAttributes>();
        AggrGetIterResult getSpaceResults;
        do
        {
          getSpaceResults = getAggrs.Invoke(server);
          if (getSpaceResults.NumRecords > 0)
            allAggrs.AddRange(getSpaceResults.AttributesList);
          getAggrs.Tag = getSpaceResults.NextTag;
        } while (!string.IsNullOrEmpty(getSpaceResults.NextTag));

        foreach(var aggr in allAggrs)
        {
          List<Property> AggregateProperties = new List<Property>
          {
            NewProperty(naID.AggregateClassProperties.UuidId, aggr.AggregateUuid), // key
            NewProperty(naID.ClusterClassProperties.ClusterUuidId, ClusterUuid), // hosting
            NewProperty(naID.SeedClassProperties.SerialNumberId, SerialNumber), // hosting
            NewProperty(naID.AggregateClassProperties.IsRootId, ConvertToSCOMBoolean(aggr.AggrRaidAttributes.IsRootAggregate??false)),
            NewProperty(naID.AggregateClassProperties.NameId, aggr.AggregateName),
            NewProperty(naID.AggregateClassProperties.SizeTotalGBId, ((double)(aggr.AggrSpaceAttributes.SizeTotal ?? 0) / 1024.0d / 1024.0d / 1024.0d).ToString()),
            NewProperty(SystemId.EntityProperties.DisplayNameId, aggr.AggregateName)
          };
          DiscoveredObjects.Add(new ClassInstance(naID.AggregateClassClassId, AggregateProperties.AsReadOnly()));
        }
        return new List<DiscoveryDataItem>()
          {
            new DiscoveryDataItem(DateTime.UtcNow, DiscoveryType.Snapshot, DiscoverySourceType.Rule,
            sourceId, managedEntityId, DiscoveredObjects.AsReadOnly(), DiscoveredRelationships.AsReadOnly())
          }.ToArray(); // DiscoveredRelationships = empty list
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
       * <xsd:element minOccurs="1" name="sourceId" type="xsd:string" />
       * <xsd:element minOccurs="1" name="managedEntityId" type="xsd:string" />
       * <xsd:element minOccurs="1" name="ConnectionFQDN" type="xsd:string" />
       * <xsd:element minOccurs="1" name="SerialNumber" type="xsd:string" />
       * <xsd:element minOccurs="1" name="ClusterUuid" type="xsd:string" />
       * <xsd:element minOccurs="1" name="username" type="xsd:string" />
       * <xsd:element minOccurs="1" name="password" type="xsd:string" />
       */
      try
      {
        // load
        XmlDocument xmlDoc = new XmlDocument();
        xmlDoc.Load(configuration);
        LoadConfigurationElement(xmlDoc, "sourceId", out sourceId);
        LoadConfigurationElement(xmlDoc, "managedEntityId", out managedEntityId);
        LoadConfigurationElement(xmlDoc, "ConnectionFQDN", out ConnectionFQDN);
        LoadConfigurationElement(xmlDoc, "SerialNumber", out SerialNumber);
        LoadConfigurationElement(xmlDoc, "ClusterUuid", out ClusterUuid);
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
      // not needed
    }
  }
}
