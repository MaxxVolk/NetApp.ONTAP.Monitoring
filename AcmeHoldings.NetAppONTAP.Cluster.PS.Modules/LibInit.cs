using AcmeHoldings.Library.Common;
using System.Net;

namespace AcmeHoldings.NetAppONTAP.Cluster.PS.Modules
{
  public class LibInit
  {
    static LibInit()
    {
      ServicePointManager.CheckCertificateRevocationList = false;
      ServicePointManager.ServerCertificateValidationCallback += (sender, cert, chain, sslPolicyErrors) => true; // ignore certificates
      SetEventSource();
    }

    internal static void SetEventSource()
    {
      Global.eventSourceName = "AcmeHoldings ONTAP Clustered Monitoring";
      Global.eventBaseID = 12810;
      Global.eventLoggingLevel = EventLoggingLevel.Informational;
    }

    internal const int evtid_ClusterEventsProbeAction = 1;
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

    internal static string EnumStringArray(string[] valueList, string separator = "; ")
    {
      if (valueList == null)
        return string.Empty;
      if (valueList.Length == 0)
        return string.Empty;
      string Result = "";
      foreach (var value in valueList)
        Result += value + separator;
      return Result.Substring(0, Result.Length - separator.Length);
    }
  }
}
