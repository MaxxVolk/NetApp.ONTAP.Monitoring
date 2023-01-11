using Microsoft.EnterpriseManagement.HealthService;
using Microsoft.EnterpriseManagement.Modules.DataItems.Discovery;
using NetApp.Filer;
using NetApp.Filer.Cluster.Cifs;
using NetApp.Filer.Cluster.Lun;
using NetApp.Filer.Cluster.Volume;
using NetApp.Filer.Cluster.Vserver;
using AcmeHoldings.Library.Common;
using AcmeHoldings.Library.SCOM;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net;
using System.Xml;
using SCOMId.System;

namespace AcmeHoldings.NetAppONTAP.Cluster.Modules
{
  [MonitoringModule(ModuleType.ReadAction)]
  [ModuleOutput(true)]
  class SVMAndVolumeDiscovery : ModuleBaseSimpleAction<DiscoveryDataItem>
  {
    // configuration
    private string ConnectionFQDN, username, password, ClusterUuid, SerialNumber;
    private Guid sourceId, managedEntityId;

    // AcmeHoldings.NetAppONTAP.Cluster.Monitoring.SVM.Class class property GUIDs
    Guid SVMUuidProperty = naID.SVMClassProperties.UuidId;

    // AcmeHoldings.NetAppONTAP.Cluster.Monitoring.SVM.Class is hosted on AcmeHoldings.NetAppONTAP.Cluster.Monitoring.Cluster.Class class
    Guid ClusterUuidProperty = naID.ClusterClassProperties.ClusterUuidId; // hosting ClusterUuid@AcmeHoldings.NetAppONTAP.Cluster.Monitoring.Cluster.Class
    // AcmeHoldings.NetAppONTAP.Cluster.Monitoring.Cluster.Class is hosted on AcmeHoldings.NetAppONTAP.Cluster.Monitoring.Seed.Class class
    Guid SerialNumberProperty = naID.SeedClassProperties.SerialNumberId; // hosting SerialNumber@AcmeHoldings.NetAppONTAP.Cluster.Monitoring.Seed.Class


    public SVMAndVolumeDiscovery(ModuleHost<DiscoveryDataItem> moduleHost, XmlReader configuration, byte[] previousState) : base(moduleHost, configuration, previousState)
    {
    }

    protected override void PreInitialize(ModuleHost<DiscoveryDataItem> moduleHost, XmlReader configuration, byte[] previousState)
    {
      LibraryInit.SetEventSource();
      Global.AddLoggingSource(GetType(), 2);
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
        NaFiler server = new NaFiler(ConnectionFQDN)
        {
          Credentials = new NetworkCredential(username, password)
        };
        // list all Storage Virtual Machines (SVMs) aka Vserver
        VserverGetIter getSVMs = new VserverGetIter(); // Getter
        List<VserverInfo> AllSVMs = new List<VserverInfo>(); // List of items of the actual object type
        VserverGetIterResult getSVMsResult; // Getter invocation results
        do
        {
          getSVMsResult = getSVMs.Invoke(server);
          if (getSVMsResult.NumRecords > 0)
            AllSVMs.AddRange(getSVMsResult.AttributesList);
          getSVMs.Tag = getSVMsResult.NextTag;
        } while (!string.IsNullOrWhiteSpace(getSVMsResult.NextTag));

        CifsShareGetIter getShares = new CifsShareGetIter();
        List<CifsShare> allShares = new List<CifsShare>();
        CifsShareGetIterResult getSharesResult;
        do
        {
          getSharesResult = getShares.Invoke(server);
          if (getSharesResult.NumRecords > 0)
            allShares.AddRange(getSharesResult.AttributesList);
          getShares.Tag = getSharesResult.NextTag;
        } while (!string.IsNullOrWhiteSpace(getSharesResult.NextTag));

        VolumeGetIter getVolume = new VolumeGetIter();
        List<VolumeAttributes> allSCMVolumes = new List<VolumeAttributes>();
        VolumeGetIterResult getVolumeResult;
        do
        {
          getVolumeResult = getVolume.Invoke(server);
          if (getVolumeResult.NumRecords > 0)
            allSCMVolumes.AddRange(getVolumeResult.AttributesList);
          getVolume.Tag = getVolumeResult.NextTag;
        } while (!string.IsNullOrWhiteSpace(getVolumeResult.NextTag));

        LunGetIter getLUN = new LunGetIter();
        List<LunInfo> allLUNs = new List<LunInfo>();
        LunGetIterResult getLUNResult;
        do
        {
          getLUNResult = getLUN.Invoke(server);
          if (getLUNResult.NumRecords > 0)
            allLUNs.AddRange(getLUNResult.AttributesList);
          getLUN.Tag = getLUNResult.NextTag;
        } while (!string.IsNullOrWhiteSpace(getLUNResult.NextTag));

        CifsServerGetIter getCifsServers = new CifsServerGetIter();
        List<CifsServerConfig> allCifsServers = new List<CifsServerConfig>();
        CifsServerGetIterResult getCifsServersResult;
        do
        {
          getCifsServersResult = getCifsServers.Invoke(server);
          if (getCifsServersResult.NumRecords > 0)
            allCifsServers.AddRange(getCifsServersResult.AttributesList);
          getCifsServers.Tag = getCifsServersResult.NextTag;
        } while (!string.IsNullOrWhiteSpace(getCifsServersResult.NextTag));

        Dictionary<string, ReadOnlyCollection<Property>> discoveredVolumes = new Dictionary<string, ReadOnlyCollection<Property>>(allSCMVolumes.Count);

        foreach (VserverInfo SVMInstance in AllSVMs)
        {
          // submit SVM instance
          List<Property> SVMProperties = new List<Property>
          {
            NewProperty(SVMUuidProperty, SVMInstance.Uuid), // key
            NewProperty(SerialNumberProperty, SerialNumber ?? "N/A"), // hosting
            NewProperty(ClusterUuidProperty, ClusterUuid), // hosting
            NewProperty(naID.SVMClassProperties.NameId, SVMInstance.VserverName),
            NewProperty(naID.SVMClassProperties.AllowedProtocolsId, Helpers.EnumStringArray(SVMInstance.AllowedProtocols)),
            NewProperty(naID.SVMClassProperties.AntivirusOnAccessPolicyId, SVMInstance.AntivirusOnAccessPolicy),
            NewProperty(naID.SVMClassProperties.CachingPolicyId, SVMInstance.CachingPolicy),
            NewProperty(naID.SVMClassProperties.CommentId, SVMInstance.Comment),
            NewProperty(naID.SVMClassProperties.DisallowedProtocolsId, Helpers.EnumStringArray(SVMInstance.DisallowedProtocols)),
            NewProperty(naID.SVMClassProperties.IpspaceId, SVMInstance.Ipspace),
            NewProperty(naID.SVMClassProperties.IsConfigLockedForChangesId, (SVMInstance.IsConfigLockedForChanges ?? false).ToString().ToLowerInvariant()),
            NewProperty(naID.SVMClassProperties.IsRepositoryVserverId, (SVMInstance.IsRepositoryVserver ?? false).ToString().ToLowerInvariant()),
            NewProperty(naID.SVMClassProperties.LdapDomainId, SVMInstance.LdapDomain),
            NewProperty(naID.SVMClassProperties.MaxVolumesId, SVMInstance.MaxVolumes),
            NewProperty(naID.SVMClassProperties.NisDomainId, SVMInstance.NisDomain),
            NewProperty(naID.SVMClassProperties.QosPolicyGroupId, SVMInstance.QosPolicyGroup),
            NewProperty(naID.SVMClassProperties.QuotaPolicyId, SVMInstance.QuotaPolicy),
            NewProperty(naID.SVMClassProperties.RootVolumeId, SVMInstance.RootVolume),
            NewProperty(naID.SVMClassProperties.RootVolumeAggregateId, SVMInstance.RootVolumeAggregate),
            NewProperty(naID.SVMClassProperties.RootVolumeSecurityStyleId, SVMInstance.RootVolumeSecurityStyle),
            NewProperty(naID.SVMClassProperties.SnapshotPolicyId, SVMInstance.SnapshotPolicy),
            NewProperty(naID.SVMClassProperties.VserverSubtypeId, SVMInstance.VserverSubtype),
            NewProperty(naID.SVMClassProperties.VserverTypeId, SVMInstance.VserverType),
            NewProperty(SystemId.EntityProperties.DisplayNameId, SVMInstance.VserverName)
          };
          DiscoveredObjects.Add(new ClassInstance(naID.SVMClassClassId, SVMProperties.AsReadOnly()));

          // discover volumes for data SVMs only
          if (SVMInstance.VserverType.ToLowerInvariant() != "data")
            continue;

          foreach (VolumeAttributes volumeInstance in allSCMVolumes.Where(x => x.VolumeIdAttributes.OwningVserverUuid == SVMInstance.Uuid))
          {
            // find volume shares
            string sharesList = "";
            bool IsShared = false;
            int sharesCount = 0;
            IEnumerable<CifsShare> allVolumeShares = allShares.Where(x => x.Volume == volumeInstance.VolumeIdAttributes.Name && x.Vserver == volumeInstance.VolumeIdAttributes.OwningVserverName);
            if (allVolumeShares != null)
              foreach (CifsShare share in allVolumeShares)
              {
                sharesCount++;
                IsShared = true;
                sharesList += string.Format("\\\\{0}\\{1}{2}\r\n",
                  SCOMRHelper.NullToEmptyString(share.CifsServer),
                  SCOMRHelper.NullToEmptyString(share.ShareName),
                  string.IsNullOrWhiteSpace(share.Comment) ? "" : " (" + share.Comment + ")");
              }
            if (IsShared)
              sharesList = sharesList.Substring(0, sharesList.Length - 2); // remove tail "\r\n "
            // submit SVM's volumes
            List<Property> VolumeProperties = new List<Property>
            {
              NewProperty(SerialNumberProperty, SerialNumber), // hosting
              NewProperty(ClusterUuidProperty, ClusterUuid), // hosting
              NewProperty(SVMUuidProperty, SVMInstance.Uuid), // hosting
              NewProperty(naID.VolumeClassProperties.UuidId, volumeInstance.VolumeIdAttributes.Uuid), // key
              NewProperty(naID.VolumeClassProperties.NameId, volumeInstance.VolumeIdAttributes.Name),
              NewProperty(naID.VolumeClassProperties.InstanceUuidId, volumeInstance.VolumeIdAttributes.InstanceUuid),
              NewProperty(naID.VolumeClassProperties.CommentId, volumeInstance.VolumeIdAttributes.Comment),
              NewProperty(naID.VolumeClassProperties.ProvenanceUuidId, volumeInstance.VolumeIdAttributes.ProvenanceUuid),
              NewProperty(SystemId.EntityProperties.DisplayNameId, volumeInstance.VolumeIdAttributes.Name),
              NewProperty(naID.VolumeClassProperties.SizeMBId, Global.ConvertBytes1024(volumeInstance.VolumeSpaceAttributes?.Size ?? 0, BinaryPrefix.Byte, BinaryPrefix.MegaByte).ToString("N2")),
              NewProperty(naID.VolumeClassProperties.SnapshotReserveSizeId, Global.ConvertBytes1024(volumeInstance.VolumeSpaceAttributes?.SnapshotReserveSize ?? 0, BinaryPrefix.Byte, BinaryPrefix.MegaByte).ToString("N2")),
              NewProperty(naID.VolumeClassProperties.IsSisVolumeId, (volumeInstance.VolumeSisAttributes?.IsSisVolume ?? false).ToString().ToLowerInvariant()),
              NewProperty(naID.VolumeClassProperties.IsNodeRootId, (volumeInstance.VolumeStateAttributes?.IsNodeRoot ?? false).ToString().ToLowerInvariant()),
              NewProperty(naID.VolumeClassProperties.IsVserverRootId, (volumeInstance.VolumeStateAttributes?.IsVserverRoot ?? false).ToString().ToLowerInvariant()),
              NewProperty(naID.VolumeClassProperties.IsAutosizeEnabledId, (volumeInstance.VolumeAutosizeAttributes?.IsEnabled ?? false).ToString().ToLowerInvariant()),
              NewProperty(naID.VolumeClassProperties.AutosizeModeId, volumeInstance.VolumeAutosizeAttributes?.Mode ?? "None"),
              NewProperty(naID.VolumeClassProperties.IsSharedId, IsShared.ToString().ToLowerInvariant()),
              NewProperty(naID.VolumeClassProperties.ShareListId, string.IsNullOrWhiteSpace(sharesList) ? "N/A" : sharesList),
              NewProperty(naID.VolumeClassProperties.ShareCountId, sharesCount.ToString()),
              NewProperty(naID.VolumeClassProperties.CIFSServerId, allVolumeShares.Any() ? allVolumeShares.First().CifsServer : "N/A"),
              NewProperty(naID.VolumeClassProperties.IsLUNVolumeId, allLUNs?.Any(x => x?.Volume == volumeInstance.VolumeIdAttributes?.Name && x?.Vserver == SVMInstance?.VserverName).ToString().ToLowerInvariant() ?? "false")
            };
            DiscoveredObjects.Add(new ClassInstance(naID.VolumeClassClassId, VolumeProperties.AsReadOnly()));
            // save property collections for further reference in share containment relationship
            discoveredVolumes.Add(SVMInstance.VserverName + volumeInstance.VolumeIdAttributes.Name, VolumeProperties.AsReadOnly());

            // LUNs at this volume/SVM
            foreach(var lunInstance in allLUNs.Where(x=>x.Volume == volumeInstance.VolumeIdAttributes.Name && x.Vserver == SVMInstance.VserverName))
            {
              string lunName = lunInstance.Path.Substring(lunInstance.Path.LastIndexOf('/') + 1);
              List<Property> lunProperties = new List<Property>
              {
                NewProperty(naID.SeedClassProperties.SerialNumberId, SerialNumber), // hosting
                NewProperty(naID.ClusterClassProperties.ClusterUuidId, ClusterUuid), // hosting
                NewProperty(naID.SVMClassProperties.UuidId, SVMInstance.Uuid), // hosting
                NewProperty(naID.VolumeClassProperties.UuidId, volumeInstance.VolumeIdAttributes.Uuid), // hosting
                NewProperty(naID.LUNClassProperties.UuidId, lunInstance.Uuid), // key
                NewProperty(naID.LUNClassProperties.AlignmentId, lunInstance.Alignment),
                NewProperty(naID.LUNClassProperties.ClassId, lunInstance.Class),
                NewProperty(naID.LUNClassProperties.MultiprotocolTypeId, lunInstance.MultiprotocolType),
                NewProperty(naID.LUNClassProperties.NameId, lunName),
                NewProperty(naID.LUNClassProperties.PathId, lunInstance.Path),
                NewProperty(naID.LUNClassProperties.SizeMBId, Global.ConvertBytes1024(lunInstance.Size ?? 0, BinaryPrefix.Byte, BinaryPrefix.MegaByte).ToString()),
                NewProperty(SystemId.EntityProperties.DisplayNameId, lunName)
              };
              DiscoveredObjects.Add(new ClassInstance(naID.LUNClassClassId, lunProperties.AsReadOnly()));
            }
          }

          foreach (CifsServerConfig cifsServerInstance in allCifsServers.Where(x => x.Vserver == SVMInstance.VserverName))
          {
            List<Property> cifsServerProperties = new List<Property>()
            {
              NewProperty(naID.SeedClassProperties.SerialNumberId, SerialNumber), // hosting
              NewProperty(naID.ClusterClassProperties.ClusterUuidId, ClusterUuid), // hosting
              NewProperty(naID.SVMClassProperties.UuidId, SVMInstance.Uuid), // hosting
              NewProperty(naID.CifsServerClassProperties.CifsServerId, cifsServerInstance.CifsServer), // key
              NewProperty(naID.CifsServerClassProperties.AuthStyleId, SCOMRHelper.NullToEmptyString(cifsServerInstance.AuthStyle)),
              NewProperty(naID.CifsServerClassProperties.CommentId, SCOMRHelper.NullToEmptyString(cifsServerInstance.Comment)),
              NewProperty(naID.CifsServerClassProperties.DomainId, SCOMRHelper.NullToEmptyString(cifsServerInstance.Domain)),
              NewProperty(naID.CifsServerClassProperties.DomainWorkgroupId, SCOMRHelper.NullToEmptyString(cifsServerInstance.DomainWorkgroup)),
              NewProperty(SystemId.EntityProperties.DisplayNameId, cifsServerInstance.CifsServer),
            };
            DiscoveredObjects.Add(new ClassInstance(naID.CifsServerClassClassId, cifsServerProperties.AsReadOnly()));

            foreach (var cifsShareInstance in allShares.Where(x => x.Vserver == SVMInstance.VserverName && x.CifsServer == cifsServerInstance.CifsServer))
            {
              List<Property> cifsShareProperties = new List<Property>()
              {
                NewProperty(naID.SeedClassProperties.SerialNumberId, SerialNumber), // hosting
                NewProperty(naID.ClusterClassProperties.ClusterUuidId, ClusterUuid), // hosting
                NewProperty(naID.SVMClassProperties.UuidId, SVMInstance.Uuid), // hosting
                NewProperty(naID.CifsServerClassProperties.CifsServerId, cifsServerInstance.CifsServer), // hosting
                NewProperty(naID.CifsShareClassProperties.ShareNameId, cifsShareInstance.ShareName), // key
                NewProperty(naID.CifsShareClassProperties.PathId, SCOMRHelper.NullToEmptyString(cifsShareInstance.Path)),
                NewProperty(naID.CifsShareClassProperties.CommentId, SCOMRHelper.NullToEmptyString(cifsShareInstance.Comment)),
                NewProperty(SystemId.EntityProperties.DisplayNameId, cifsShareInstance.ShareName + " @ " + cifsServerInstance.CifsServer),
              };
              DiscoveredObjects.Add(new ClassInstance(naID.CifsShareClassClassId, cifsShareProperties.AsReadOnly()));

              if (discoveredVolumes.ContainsKey(cifsShareInstance.Vserver + cifsShareInstance.Volume))
              DiscoveredRelationships.Add(new RelationshipInstance(naID.Volume_Contain_CifsShareRelationshipRelationshipId,
                naID.VolumeClassClassId, naID.CifsShareClassClassId,
                discoveredVolumes[cifsShareInstance.Vserver + cifsShareInstance.Volume], cifsShareProperties.AsReadOnly(),
                new List<Property>().AsReadOnly()));
              else
              {
                Global.logWriteWarning("Share [{0}] refers to the [{1}] volume at the [{2}] SVM, which doesn't exist.", this, SCOMRHelper.NullToEmptyString(cifsShareInstance.ShareName), SCOMRHelper.NullToEmptyString(cifsShareInstance.Volume), SCOMRHelper.NullToEmptyString(cifsShareInstance.Vserver));
              }
            }
          }
        }

        // create output
        Global.logWriteInformation("Submitting discovery data from SVMAndVolumeDiscovery.", this);
        return new List<DiscoveryDataItem>()
          {
            new DiscoveryDataItem(DateTime.UtcNow, DiscoveryType.Snapshot, DiscoverySourceType.Rule,
            sourceId, managedEntityId, DiscoveredObjects.AsReadOnly(), DiscoveredRelationships.AsReadOnly())
          }.ToArray(); // DiscoveredRelationships = empty list
      }
      catch (Exception e)
      {
        Global.logWriteException("Generic error in SVM, Volume, and LUN Discovery running for {0}.", e, this, ConnectionFQDN);
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
