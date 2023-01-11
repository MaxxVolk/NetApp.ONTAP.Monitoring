using DataONTAP.C.Types.Ems;
using Microsoft.EnterpriseManagement.HealthService;
using Microsoft.EnterpriseManagement.Mom.Modules.DataItems;
using NetApp.Ontapi.Filer;
using NetApp.Ontapi.Filer.C.Ems840;
using AcmeHoldings.Library.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml;

namespace AcmeHoldings.NetAppONTAP.Cluster.PS.Modules
{
  [Serializable]
  internal class ClusterEventsProbeState
  {
    internal long LastUnixTimeStamp;
    internal DateTime LastQueryEventTimeStamp;
  }

  [MonitoringModule(ModuleType.ReadAction)]
  [ModuleOutput(true)]
  [StatelessModule(false)]
  public class ClusterEventsProbeAction : ModuleBaseSimpleAction<PropertyBagDataItem>
  {
    // configuration
    private string ConnectionFQDN, username, password;
    private int ReadIntervalSeconds;
    private bool? ForceUseUnsecure = null;
    private string FilterName;

    private Dictionary<string, Tuple<int, int>> severityTranslation = new Dictionary<string, Tuple<int, int>>(StringComparer.InvariantCultureIgnoreCase)
    {
      // severity, priority
      { "ALERT", new Tuple<int, int>(1, 2) },
      { "EMERGENCY", new Tuple<int, int>(2, 2) },
      { "NOTICE", new Tuple<int, int>(1, 1) },
      { "INFORMATIONAL", new Tuple<int, int>(1, 0) },
      { "ERROR", new Tuple<int, int>(2, 1) },
      { "DEBUG", new Tuple<int, int>(0, 2) },
      { "DEFAULT", new Tuple<int, int>(1, 1) }
    };

    public ClusterEventsProbeAction(ModuleHost<PropertyBagDataItem> moduleHost, XmlReader configuration, byte[] previousState) : base(moduleHost, configuration, previousState)
    {
    }

    protected override void PreInitialize(ModuleHost<PropertyBagDataItem> moduleHost, XmlReader configuration, byte[] previousState)
    {
      LibInit.SetEventSource();
      Global.AddLoggingSource(GetType(), LibInit.evtid_ClusterEventsProbeAction);
      base.PreInitialize(moduleHost, configuration, previousState);
    }

    private ClusterEventsProbeState TypedModuleState
    {
      get { return (ClusterEventsProbeState)ModuleState; }
      set { ModuleState = value; }
    }

    protected override PropertyBagDataItem[] GetOutputData(DataItemBase[] inputDataItems)
    {
      try
      {
        if (TypedModuleState == null)
          TypedModuleState = new ClusterEventsProbeState
          {
            LastUnixTimeStamp = -1,
            LastQueryEventTimeStamp = DateTime.UtcNow.AddSeconds(-ReadIntervalSeconds)
          };
        NaController Controller = ConnectToCluster(ConnectionFQDN, username, password, ForceUseUnsecure);
        EmsMessageGetIter getMessages = new EmsMessageGetIter
        {
          Query = string.IsNullOrWhiteSpace(FilterName) ? null : new List<EmsMessageInfo>() { new EmsMessageInfo() { FilterName = FilterName } }.ToArray(),
          MaxRecords = 50,
          MaxRecordsSpecified = true
        };
        long maxRead = 1000;
        bool escalateBreak = false;
        List<EmsMessageInfo> messagesToProcess = new List<EmsMessageInfo>();
        EmsMessageGetIterResult getMessagesResult;
        DateTime emsQuerySubmissionTimestamp = DateTime.UtcNow;
        do
        {
          getMessagesResult = getMessages.Invoke(Controller);
          if (getMessagesResult.NumRecords > 0)
            foreach (var msg in getMessagesResult.AttributesList)
              if (TypedModuleState.LastUnixTimeStamp > 0) // read as: we have valid previous state, so will compare by the last logged message timestamp
              {
                if ((long)msg.Time > TypedModuleState.LastUnixTimeStamp)
                  messagesToProcess.Add(msg);
                else
                {
                  escalateBreak = true;
                  break; // messages returned in chronological order from the most recent one
                }
              }
              else // read as: we don't have valid SeqNum saved, so use timestamp
              {
                if (new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds((long?)msg.Time ?? 0) > TypedModuleState.LastQueryEventTimeStamp)
                  messagesToProcess.Add(msg);
                else
                {
                  escalateBreak = true;
                  break;
                }
              }
          else
            escalateBreak = true;
          Global.logWriteInformation("{0} events read from NetApp API at {3}, {1} of them selected to post. The cycle will {2} now.", this, getMessagesResult.NumRecords, messagesToProcess.Count, escalateBreak ? "stop" : "continue", ConnectionFQDN);
          if (escalateBreak)
            break;
          maxRead -= getMessagesResult.NumRecords ?? 0;
          if (maxRead < 0)
          {
            Global.logWriteWarning("Emergency exit from event read cycle due to maxEvents reaches its limit.", this);
            break;
          }
          getMessages.Tag = getMessagesResult.NextTag;
        } while (!string.IsNullOrWhiteSpace(getMessagesResult.NextTag));
        // save state
        if (TypedModuleState == null)
          TypedModuleState = new ClusterEventsProbeState() { LastUnixTimeStamp = -1, LastQueryEventTimeStamp = emsQuerySubmissionTimestamp };
        if (messagesToProcess.Any())
          TypedModuleState.LastUnixTimeStamp = messagesToProcess.First().Time == null ? -1 : (long)messagesToProcess.First().Time;
        // else keep the value unchanged
        TypedModuleState.LastQueryEventTimeStamp = emsQuerySubmissionTimestamp;
        if (!SavePreviousState())
          Global.logWriteWarning("Failed to save event bookmark.", this);
        if (messagesToProcess.Count != 0) // we have some data to update the bookmark
        {
          Global.logWriteInformation("ClusterEventsProbeAction captured {0} events to publish in this cycle for {1}.", this, messagesToProcess.Count, ConnectionFQDN);
          // create output
          List<PropertyBagDataItem> result = new List<PropertyBagDataItem>(messagesToProcess.Count);
          foreach (EmsMessageInfo eventMsg in messagesToProcess)
            result.Add(CreatePropertyBag(new Dictionary<string, object>
            {
              { "EventDescription", eventMsg.Event?.ToString() ?? "" },
              { "EventName", "~{Summary}#[SCOM: " + (eventMsg.MessageName?.ToString() ?? "No Name Specified") + "]#" },
              { "Severity", severityTranslation.ContainsKey(eventMsg.Severity?.ToString() ?? "DEFAULT") ? severityTranslation[eventMsg.Severity?.ToString() ?? "DEFAULT"].Item1 : severityTranslation["DEFAULT"].Item1 },
              { "Priority", severityTranslation.ContainsKey(eventMsg.Severity?.ToString() ?? "DEFAULT") ? severityTranslation[eventMsg.Severity?.ToString() ?? "DEFAULT"].Item2 : severityTranslation["DEFAULT"].Item2 },
              { "Source", eventMsg.Source?.ToString() ?? "Not Specified" },
              { "Node", eventMsg.Node?.ToString() ?? "" },
              { "SuppressionCount", eventMsg.NumSuppressedSinceLast?.ToString() ?? "0" },
              { "Parameters", CreateNameValueString(eventMsg.Parameters) },
              { "TimeUTC", ConvertUnixTime((long?)eventMsg.Time).ToString("yyyy-MM-dd HH:mm") },
              { "TimeLocal", eventMsg.TimeDT?.ToString("yyyy-MM-dd HH:mm") ?? "No time specified" }
            }));
          return result.ToArray();
        }
        else
          return null;
      }
      catch (Exception e)
      {
        Global.logWriteException("Generic error in ClusterEventsProbeAction.", e, this);
        return null;
      }
    }

    private static NaController ConnectToCluster(string inconnectionFQDN, string inusername, string inpassword, bool? inforceUseUnsecure)
    {
      NaController result = new NaController(inconnectionFQDN)
      {
        Credentials = new NetApp.Ontapi.OntapiCredential(inusername, inpassword)
      };
      if (inforceUseUnsecure != null)
      {
        if (inforceUseUnsecure == true)
          result.Protocol = Api.Ontapi.ServerProtocol.HTTP;
        else
          result.Protocol = Api.Ontapi.ServerProtocol.HTTPS;
      }
      return result;
    }

    private object CreateNameValueString(Parameter[] parameters)
    {
      if (parameters == null)
        return "";
      if (parameters.Length == 0)
        return "";
      string result = "";
      foreach (Parameter param in parameters)
        result += string.Format("{0}: {1}\r\n ", param.Name?.ToString() ?? "No Name", param.Value?.ToString() ?? "No Value");
      return result;
    }

    // private IEnumerable<>

    protected override void LoadConfiguration(XmlReader configuration)
    {
      /*
       * <xsd:element minOccurs="1" name="ConnectionFQDN" type="xsd:string" />
       * <xsd:element minOccurs="1" name="username" type="xsd:string" />
       * <xsd:element minOccurs="1" name="password" type="xsd:string" />
       * <xsd:element minOccurs="0" name="FilterName" type="xsd:string" />
       * <xsd:element minOccurs="1" name="ReadIntervalSeconds" type="xsd:int" />
       */
      try
      {
        // load
        XmlDocument xmlDoc = new XmlDocument();
        xmlDoc.Load(configuration);
        LoadConfigurationElement(xmlDoc, "ConnectionFQDN", out ConnectionFQDN);
        LoadConfigurationElement(xmlDoc, "username", out username);
        LoadConfigurationElement(xmlDoc, "password", out password);
        LoadConfigurationElement(xmlDoc, "FilterName", out FilterName, null, false);
        LoadConfigurationElement(xmlDoc, "ReadIntervalSeconds", out ReadIntervalSeconds);
        // parse
        username = Helpers.TransformUsername(username);
      }
      catch (Exception xe)
      {
        Global.logWriteException("Error parsing configuration XML. This error is unrecoverable.", xe, this);
        throw new ModuleException("Error parsing configuration XML", xe);
      }
    }

    private static DateTime ConvertUnixTime(long? unixTime)
    {
      if (unixTime.HasValue)
        return new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(unixTime.Value);
      else
        return DateTime.MinValue;
    }
  }
}
