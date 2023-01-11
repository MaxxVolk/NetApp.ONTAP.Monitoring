using System.Net;
using AcmeHoldings.Library.Common;

namespace AcmeHoldings.NetAppONTAP.Cluster.Modules
{
  internal static class LibraryInit
  {
    static LibraryInit()
    {
      ServicePointManager.CheckCertificateRevocationList = false;
      ServicePointManager.ServerCertificateValidationCallback += (sender, cert, chain, sslPolicyErrors) => true; // ignore certificates
      ServicePointManager.SecurityProtocol = SecurityProtocolType.Ssl3 | SecurityProtocolType.Tls | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls12;
      SetEventSource();
    }

    internal static void SetEventSource()
    {
      Global.eventSourceName = "Acme Holdings ONTAP Clustered Monitoring";
      Global.eventBaseID = 12710;
      Global.eventLoggingLevel = EventLoggingLevel.Informational;
    }

    internal const int evtid_ClusterAndNodeDiscovery = 1;
    internal const int evtid_PerfCollectionProbeAction = 3;
    internal const int evtid_VolumeSpaceProbeAction = 6;
    internal const int evtid_VolumeStateProbeAction = 7;
    internal const int evtid_ONTAPClusterPerformanceRefresher = 8;
    internal const int evtid_PerformanceCollectorHealthProbeAction = 9;
    internal const int evtid_ObjectCacheHealthProbeAction = 10;
    internal const int evtid_ClusterEventsProbeAction = 11;
    internal const int evtid_AggregateDiscovery = 12;
    internal const int evtid_AggrSpaceProbeAction = 13;
    internal const int evtid_LUNSpaceProbeAction = 14;
    internal const int evtid_StorePoolExhaustProbeAction = 15;
  }

  internal static class Helpers
  {
    internal static string TransformUsername(string SplittedUserName)
    {
      string domainSplitName = "", userSplitName = "";
      char[] Separators = { '@', '\\' };
      var RResult = SplittedUserName.Split(Separators);
      userSplitName = RResult[0];
      if (RResult.Length >= 2)
        domainSplitName = RResult[1];
      else
        domainSplitName = string.Empty;
      return domainSplitName + "\\" + userSplitName;
    }

    internal static string EnumStringArray(string [] valueList, string separator = "; ")
    {
      if (valueList == null)
        return string.Empty;
      if (valueList.Length == 0)
        return string.Empty;
      string Result = "";
      foreach (var value in valueList)
        Result += (value ?? "NULL") + separator;
      return Result.Substring(0, Result.Length - separator.Length);
    }
  }
}