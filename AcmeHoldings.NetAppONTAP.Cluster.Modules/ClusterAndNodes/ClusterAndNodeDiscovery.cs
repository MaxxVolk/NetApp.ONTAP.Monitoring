using Microsoft.EnterpriseManagement.HealthService;
using Microsoft.EnterpriseManagement.Modules.DataItems.Discovery;
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
using SCOMId.System;

namespace AcmeHoldings.NetAppONTAP.Cluster.Modules
{
  [MonitoringModule(ModuleType.ReadAction)]
  [ModuleOutput(true)]
  class ClusterAndNodeDiscovery : ModuleBaseSimpleAction<DiscoveryDataItem>
  {
    // configuration
    private string ConnectionFQDN, username, password;
    private Guid sourceId, managedEntityId;
    private bool? ForceUseUnsecure = null;

    // AcmeHoldings.NetAppONTAP.Cluster.Monitoring.Cluster.Class class property GUIDs
    Guid ClusterUuidProperty = naID.ClusterClassProperties.ClusterUuidId;
    Guid ClusterNameProperty = naID.ClusterClassProperties.NameId;
    Guid SerialNumberProperty = naID.ClusterClassProperties.SerialNumberId;
    Guid ContactProperty = naID.ClusterClassProperties.ContactId;
    Guid LocationProperty = naID.ClusterClassProperties.LocationId;
    // AcmeHoldings.NetAppONTAP.Cluster.Monitoring.Cluster.Class is hosted on AcmeHoldings.NetAppONTAP.Cluster.Monitoring.Seed.Class class
    Guid SeedSerialNumberProperty = naID.SeedClassProperties.SerialNumberId; // hosting SerialNumber@AcmeHoldings.NetAppONTAP.Cluster.Monitoring.Seed.Class

    // AcmeHoldings.NetAppONTAP.Cluster.Monitoring.ClusterNode.Class class property GUIDs
    Guid NodeUuidProperty = naID.ClusterNodeClassProperties.NodeUuidId;
    Guid ClusterNodeNameProperty = naID.ClusterNodeClassProperties.NameId;
    Guid IsNodeEligibleProperty = naID.ClusterNodeClassProperties.IsNodeEligibleId;
    Guid IsNodeEpsilonProperty = naID.ClusterNodeClassProperties.IsNodeEpsilonId;
    Guid FirmwareVersionProperty = naID.ClusterNodeClassProperties.FirmwareVersionId;
    Guid FirmwareInstallDateProperty = naID.ClusterNodeClassProperties.FirmwareInstallDateId;

    public ClusterAndNodeDiscovery(ModuleHost<DiscoveryDataItem> moduleHost, XmlReader configuration, byte[] previousState) : base(moduleHost, configuration, previousState)
    {
    }

    protected override void PreInitialize(ModuleHost<DiscoveryDataItem> moduleHost, XmlReader configuration, byte[] previousState)
    {
      LibraryInit.SetEventSource();
      Global.AddLoggingSource(GetType(), LibraryInit.evtid_ClusterAndNodeDiscovery);
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
        // get Cluster properties
        ClusterIdentityGet getCluster = new ClusterIdentityGet();
        ClusterIdentityGetResult Cluster = getCluster.Invoke(server);
        // fill SCOM data item
        List<Property> ClusterProperties = new List<Property>
        {
          NewProperty(ClusterUuidProperty, Cluster.Attributes.ClusterUuid), // key
          NewProperty(SeedSerialNumberProperty, Cluster.Attributes.ClusterSerialNumber), // hosting -- if not entered correctly -- won't work
          NewProperty(ClusterNameProperty, Cluster.Attributes.ClusterName),
          NewProperty(SystemId.EntityProperties.DisplayNameId, Cluster.Attributes.ClusterName),
          NewProperty(SerialNumberProperty, Cluster.Attributes.ClusterSerialNumber),
          NewProperty(ContactProperty, Cluster.Attributes.ClusterContact),
          NewProperty(LocationProperty, Cluster.Attributes.ClusterLocation)
        };
        DiscoveredObjects.Add(new ClassInstance(naID.ClusterClassClassId, ClusterProperties.AsReadOnly()));
        // get Cluster Nodes
        ClusterNodeGetIter getClusterNodes = new ClusterNodeGetIter();
        List<ClusterNodeInfo> AllClusterNodes = new List<ClusterNodeInfo>();
        ClusterNodeGetIterResult getClusterNodeResult;
        do
        {
          getClusterNodeResult = getClusterNodes.Invoke(server);
          AllClusterNodes.AddRange(getClusterNodeResult.AttributesList);
        } while (!string.IsNullOrEmpty(getClusterNodeResult.NextTag));

        foreach (var clusterNode in AllClusterNodes)
        {
          // get cluster firmware data on the top of already know details
          ClusterImageGet getClusterFirware = new ClusterImageGet
          {
            NodeId = clusterNode.NodeName
          };
          var ClusterNodeFirmware = getClusterFirware.Invoke(server);
          DateTime installDate = (new DateTime(1970, 1, 1)).AddSeconds(ClusterNodeFirmware.Attributes.DateInstalled);
          // fill SCOM data item
          List<Property> ClusterNodeProperties = new List<Property>
          {
            NewProperty(NodeUuidProperty, clusterNode.NodeUuid), // key
            NewProperty(naID.ClusterClassProperties.ClusterUuidId, Cluster.Attributes.ClusterUuid), // hosting
            NewProperty(SeedSerialNumberProperty, Cluster.Attributes.ClusterSerialNumber), // hosting
            NewProperty(ClusterNodeNameProperty, clusterNode.NodeName),
            NewProperty(SystemId.EntityProperties.DisplayNameId, clusterNode.NodeName),
            NewProperty(IsNodeEligibleProperty, (clusterNode.IsNodeEligible ?? false).ToString().ToLowerInvariant()),
            NewProperty(IsNodeEpsilonProperty, (clusterNode.IsNodeEpsilon ?? false).ToString().ToLowerInvariant()),
            NewProperty(FirmwareVersionProperty, ClusterNodeFirmware.Attributes.CurrentVersion),
            NewProperty(FirmwareInstallDateProperty, installDate.ToString("yyyy-MM-dd HH:mm:ss"))
          };
          DiscoveredObjects.Add(new ClassInstance(naID.ClusterNodeClassClassId, ClusterNodeProperties.AsReadOnly()));
        }

        // create output
        Global.logWriteInformation("Submitting discovery data from ClusterAndNodeDiscovery.", this);
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
