using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NetApp.Filer.Cluster.Perf;
using NetApp.Filer;
using System.Net;
using System.Threading;
using System.Runtime.Serialization;
using AcmeHoldings.Library.Common;
using System.Xml;
using Library.Common;

namespace AcmeHoldings.NetApp.ONTAPCluster
{
  [Serializable]
  public class ONTAPClusterAPIException : Exception
  {
    public ONTAPClusterAPIException(string inAPIStatus, int inErrno, string inFailureReason) : base()
    {
      APIStatus = inAPIStatus;
      Errno = inErrno;
      FailureReason = inFailureReason;
    }

    public ONTAPClusterAPIException(string inAPIStatus, int inErrno, string inFailureReason, string Message) : base(Message)
    {
      APIStatus = inAPIStatus;
      Errno = inErrno;
      FailureReason = inFailureReason;
    }

    public ONTAPClusterAPIException(string inAPIStatus, int inErrno, string inFailureReason, string message, Exception innerException)
      : base(message, innerException)
    {
      APIStatus = inAPIStatus;
      Errno = inErrno;
      FailureReason = inFailureReason;
    }

    protected ONTAPClusterAPIException(SerializationInfo info, StreamingContext context) : base(info, context)
    {
      APIStatus = info.GetString("APIStatus");
      Errno = info.GetInt32("Errno");
      FailureReason = info.GetString("FailureReason");
    }

    public override void GetObjectData(SerializationInfo info, StreamingContext context)
    {
      base.GetObjectData(info, context);
      info.AddValue("APIStatus", APIStatus);
      info.AddValue("Errno", Errno);
      info.AddValue("FailureReason", FailureReason);
    }

    public string APIStatus { get; private set; }
    public int Errno { get; private set; }
    public string FailureReason { get; private set; }
  }

  public static class UnixDateTime
  {
    public static DateTime From1970TimeStamp(string Timestamp)
    {
      return From1970TimeStamp(Convert.ToDouble(Timestamp));
    }

    public static DateTime From1970TimeStamp(double Timestamp)
    {
      return (new DateTime(1970, 1, 1)).AddSeconds(Timestamp);
    }
  }

  public struct CounterNameDataPair
  {
    public string name;
    public double value;
  }

  public class ONTAPClusterPerformanceCollector
  {
    // Static variables to support singleton instances
    private static Dictionary<string, ONTAPClusterPerformanceCollector> MyselfSingularInstance = new Dictionary<string, ONTAPClusterPerformanceCollector>();
    private static readonly object MyselfSingularLock = new object();

    // per-instance variables
    private NaFiler ONTAPCluster;
    private int idl;
    protected DateTime lastReadTimestamp = DateTime.MinValue;

    // types and counters
    private Dictionary<string, string[]> typesAndCounters = new Dictionary<string, string[]>();
    private object typesAndCountersLock = new object();
    // raw data collection storage
    private Dictionary<string, HistoricalQueue<InstanceData[]>> rawData = new Dictionary<string, HistoricalQueue<InstanceData[]>>();
    private object rawDataLock = new object();

    // Health model
    private List<ObjectInfo> ObjectTypes = new List<ObjectInfo>();
    private Dictionary<string, List<CounterInfo>> ObjectCounters = new Dictionary<string, List<CounterInfo>>();
    private int InstanceBatchingSize = 200; // reserved for future use
    private const int MinimumTimeSpan = 100; // 100 seconds
    private const int MaxCollectionDepth = 36; // 1 hour (36 * 100)

    public double PerformanceDataAgeSeconds { get { return DateTime.Now.Subtract(lastReadTimestamp).TotalSeconds; } }
    public bool PerformanceCollectionInitialized { get { lock (typesAndCountersLock) { return typesAndCounters.Count > 0; } } }

    private ONTAPClusterPerformanceCollector (string clusterFQDN, NetworkCredential Credentials, bool? ForceUseUnsecure, int id)
    {
      idl = id;
      ONTAPCluster = new NaFiler(clusterFQDN)
      {
        Credentials = Credentials
      };
      if (ForceUseUnsecure != null)
      {
        if (ForceUseUnsecure == true)
          ONTAPCluster.ForceUseUnsecure = true;
        else
          ONTAPCluster.ForceUseUnsecure = false;
      }
      // create Performance Model
      PerfObjectListInfo perfObjectInvocation = new PerfObjectListInfo();
      PerfObjectListInfoResult perfObjects = perfObjectInvocation.Invoke(ONTAPCluster);
      if (perfObjects.Objects != null && perfObjects.Objects.Length > 0)
        ObjectTypes.AddRange(perfObjects.Objects);
      foreach (ObjectInfo perfObjectType in ObjectTypes)
      {
        PerfObjectCounterListInfo perfCounterInvocation = new PerfObjectCounterListInfo
        {
          Objectname = perfObjectType.Name
        };
        PerfObjectCounterListInfoResult perfCounters = perfCounterInvocation.Invoke(ONTAPCluster);
        ObjectCounters.Add(perfObjectType.Name, perfCounters.Counters.ToList());
      }
      // Start data collection
      TimedPerformanceCollector(); // synchronous mode
      // collectionTimer = new Timer(new TimerCallback(TimedPerformanceCollector), null, MinimumTimeSpan * 1000, MinimumTimeSpan * 1000); // 100 seconds hard-coded
    }

    public static ResultWrapper<ONTAPClusterPerformanceCollector> GetPerformanceCollector(string clusterFQDN, NetworkCredential Credentials = null, bool? ForceUseUnsecure = null)
    {
      // http://csharpindepth.com/Articles/General/Singleton.aspx
      lock (MyselfSingularLock)
      {
        if (MyselfSingularInstance.ContainsKey(clusterFQDN.ToUpper()))
          return new ResultWrapper<ONTAPClusterPerformanceCollector>(MyselfSingularInstance[clusterFQDN.ToUpper()]);
        else
        {
          try
          {
            ONTAPClusterPerformanceCollector newInstance = new ONTAPClusterPerformanceCollector(clusterFQDN, Credentials, ForceUseUnsecure, (new Random(DateTime.Now.Millisecond)).Next(100));
            MyselfSingularInstance.Add(clusterFQDN.ToUpper(), newInstance);
            return new ResultWrapper<ONTAPClusterPerformanceCollector>(newInstance);
          }
          catch (Exception e)
          {
            return new ResultWrapper<ONTAPClusterPerformanceCollector>(e);
          }
        }
      }
    }

    public static int ShutdownPerformanceCollector(string clusterFQDN)
    {
      lock (MyselfSingularLock)
      {
        int retval = -1;
        if (MyselfSingularInstance.ContainsKey(clusterFQDN.ToUpper()))
        {
          retval = MyselfSingularInstance[clusterFQDN.ToUpper()].idl;
          MyselfSingularInstance.Remove(clusterFQDN.ToUpper());
          return retval;
        }
        else
          return -1;
      }
    }

    public void AddOrUpdateType(string instanceType, string[] counters)
    {
      // Test input
      if (!ObjectTypes.Exists(x => x.Name == instanceType))
        throw new KeyNotFoundException(instanceType + " is not a supported performance object type.");
      if (counters != null)
        foreach (string propertyName in counters)
        {
          if (!ObjectCounters[instanceType].Exists(x => x.Name == propertyName))
            throw new KeyNotFoundException($"{propertyName} is not a valid performance counter for the {instanceType} performance object type.");
        }
      // add valid type and counters to start collection
      lock (typesAndCountersLock)
      {
        if (!typesAndCounters.Keys.Contains(instanceType))
          typesAndCounters.Add(instanceType, ResolveBaseCounters(instanceType, counters));
        else
          typesAndCounters[instanceType] = ResolveBaseCounters(instanceType, typesAndCounters[instanceType].Union(counters).ToArray());
      }
    }

    public void AddOrUpdateType(string instanceType, Func<CounterInfo, bool> countersLookup)
    {
      // Test input
      if (!ObjectTypes.Exists(x => x.Name == instanceType))
        throw new KeyNotFoundException(instanceType + " is not a supported performance object type.");
      List<string> counters = new List<string>(ObjectCounters[instanceType].Where(countersLookup).Select(x => x.Name));
      AddOrUpdateType(instanceType, counters.ToArray());
    }

    private string[] ResolveBaseCounters(string instanceType, string[] counters)
    {
      if (counters == null)
        return null;
      List<string> Result = new List<string>(counters);
      List<string> ToAdd = new List<string>();
      do
      {
        ToAdd.Clear();
        foreach (string propertyName in Result)
        {
          var counterDefinition = ObjectCounters[instanceType].Find(x => x.Name == propertyName);
          if (!string.IsNullOrEmpty(counterDefinition.BaseCounter))
          {
            // base counter is required, so if it's not here, then add
            if (Result.IndexOf(counterDefinition.BaseCounter) < 0)
              ToAdd.Add(counterDefinition.BaseCounter);
          }
        }
        if (ToAdd.Count > 0)
          Result.AddRange(ToAdd);
      } while (ToAdd.Count > 0); // re-evaluate if new counters were added
      
      return Result.ToArray();
    }

    /// <summary>
    /// Usage:
    /// Step 1. Get the most recent timestamp.
    /// Step 2. Call either GetPerformanceValueRaw or GetPerformanceValue one or more times using the same timestamp
    /// received in the step 1, to preserve data alignment.
    /// </summary>
    /// <param name="perObjectType"></param>
    /// <returns>Returns date and time of the most recent collection interval for the given object type. 
    /// If no data collected, then returns <code>DateTime.MinValue</code> </returns>
    public DateTime GetCurrentTimestamp(string perObjectType)
    {
      lock(rawDataLock)
      {
        if (!rawData.Keys.Contains(perObjectType))
          return DateTime.MinValue; // haven't collected enough performance data
        if (rawData[perObjectType].Count == 0)
          return DateTime.MinValue; // haven't collected enough performance data
        return rawData[perObjectType].MostRecentTimestamp;
      }
    }

    public InstanceData[] GetMostRecentPerformanceValuesRaw(string instanceType)
    {
      lock (rawDataLock)
      {
        if (!rawData.Keys.Contains(instanceType))
          return null;
        return (InstanceData[])rawData[instanceType].MostRecent.Clone();
      }
    }

    public CounterNameDataPair[] GetPerformanceValueRaw(string instanceType, string instanceUuid, int meanInterval,
      DateTime startTimestamp)
    {
      return GetPerformanceValueRaw(instanceType, instanceUuid, meanInterval, startTimestamp, out DateTime skipValue);
    }

    public CounterNameDataPair[] GetPerformanceValueRaw(string instanceType, string instanceUuid, int meanInterval, 
      DateTime startTimestamp, out DateTime resultTimeStamp)
    {
      lock (rawDataLock)
      {
        if (!rawData.Keys.Contains(instanceType))
        {
          resultTimeStamp = DateTime.MinValue;
          return null;
        }
        // find offset to the startTimestamp
        int offset = 0;
        do
        {
          if (rawData[instanceType].TimestampAt(offset) == startTimestamp)
            break; // timestamp offset bound
          offset++;
          if (offset >= rawData[instanceType].Count)
          {
            resultTimeStamp = DateTime.MinValue;
            return null; // timestamp offset is out of boundaries
          }
        } while (true); // will exit either when found or reach the end
        // calculate index
        int queueDepth = meanInterval / MinimumTimeSpan + offset;
        if (queueDepth > MaxCollectionDepth)
          throw new ArgumentOutOfRangeException("meanInterval parameter is too big, maximum value is " + (MinimumTimeSpan * MaxCollectionDepth).ToString());
        HistoricalQueue<InstanceData[]> typeData = rawData[instanceType];
        if (typeData.Count < queueDepth + 1)
        {
          resultTimeStamp = DateTime.MinValue;
          return null; // haven't collected enough performance data
        }
        CounterData[] AllCounters = typeData[queueDepth].Where(x => x.Uuid == instanceUuid).FirstOrDefault()?.Counters;
        if (AllCounters == null || AllCounters.Length == 0)
        {
          resultTimeStamp = DateTime.MinValue;
          return null; // timestamp offset is out of boundaries
        }
        CounterNameDataPair[] Result = new CounterNameDataPair[AllCounters.Length];
        int idx = 0;
        foreach(var counter in AllCounters)
        {
          Result[idx].name = counter.Name;
          Result[idx].value = Convert.ToDouble(counter.Value);
          idx++;
        }
        resultTimeStamp = typeData.TimestampAt(queueDepth);
        return Result;
      }
    }

    public bool GetPerformanceValue(string instanceType, string instanceUuid, string counterName, int meanInterval,
      DateTime startTimestamp, out double Result)
    {
      double mainValueRecent = 0;
      double baseValueRecent = 0;
      double mainValueOld = 0;
      double baseValueOld = 0;
      double totalSeconds = 0;
      Result = -1;
      // find counter info
      CounterInfo currentCounter = ObjectCounters[instanceType].Find(x => x.Name == counterName);
      if (meanInterval < MinimumTimeSpan)
        throw new ArgumentOutOfRangeException("meanInterval parameter is too small, minimum value is " + MinimumTimeSpan.ToString());
      DateTime startStamp = GetCurrentTimestamp(instanceType);
      CounterNameDataPair[] ValueRecent = GetPerformanceValueRaw(instanceType, instanceUuid, 0, startStamp);
      if (ValueRecent == null)
        return false;
      mainValueRecent = ValueRecent.Where(x => x.name == currentCounter.Name).First().value;
      if (!string.IsNullOrEmpty(currentCounter.BaseCounter))
        baseValueRecent = ValueRecent.Where(x => x.name == currentCounter.BaseCounter).First().value;
      CounterNameDataPair[] ValueOld = null;
      if (currentCounter.Properties != "raw" && currentCounter.Properties != "string")
      {
        ValueOld = GetPerformanceValueRaw(instanceType, instanceUuid, meanInterval, startStamp, out DateTime oldValueTimestamp);
        if (ValueOld == null)
          return false;
        mainValueOld = ValueOld.Where(x => x.name == currentCounter.Name).First().value;
        if (!string.IsNullOrEmpty(currentCounter.BaseCounter))
          baseValueOld = ValueOld.Where(x => x.name == currentCounter.BaseCounter).First().value;
        totalSeconds = startStamp.Subtract(oldValueTimestamp).TotalSeconds;
      }
      Result = -1;
      switch (currentCounter.Properties)
      {
        case "percent":
        case "average":
          if (mainValueOld == mainValueRecent)
          {
            Result = 0;
            break;
          }
          if (baseValueOld == baseValueRecent)
          {
            Result = 0; // to avoid NaN
            break;
          }
          Result = (mainValueRecent - mainValueOld) / (baseValueRecent - baseValueOld);
          break;
        case "rate":
          Result = (mainValueRecent - mainValueOld) / totalSeconds;
          break;
        case "raw":
          Result = mainValueRecent;
          break;
        case "delta":
          Result = mainValueRecent - mainValueOld;
          break;
      }
      if (currentCounter.Properties == "percent")
        Result = Result * 100;
      return true;
    }

    public void TimedPerformanceCollector()
    {
      lock(rawDataLock)
      {
        lock(typesAndCountersLock)
        {
          foreach (string perObjectType in typesAndCounters.Keys)
          {
            // re-read all instances for the type
            List<string[]> instanceUuidPrepared = new List<string[]>();
            PerfObjectInstanceListInfoIter getPerfObjectInstance = new PerfObjectInstanceListInfoIter
            {
              Objectname = perObjectType,
              MaxRecords = InstanceBatchingSize,
              MaxRecordsSpecified = true
            };
            PerfObjectInstanceListInfoIterResult PerfObjectInstanceResult;
            do
            {
              PerfObjectInstanceResult = getPerfObjectInstance.Invoke(ONTAPCluster);
              if (PerfObjectInstanceResult.Errno != 0)
                throw new ONTAPClusterAPIException(PerfObjectInstanceResult.APIStatus, PerfObjectInstanceResult.Errno, 
                  PerfObjectInstanceResult.FailureReason, "Unable to read performance targets for " + perObjectType + ".");
              if (PerfObjectInstanceResult.AttributesList != null && PerfObjectInstanceResult.AttributesList.Length > 0)
              {
                string[] UuidBatch = new string[PerfObjectInstanceResult.AttributesList.Length];
                int idx = 0;
                foreach (InstanceInfo instanceInfo in PerfObjectInstanceResult.AttributesList)
                  UuidBatch[idx++] = instanceInfo.Uuid;
                instanceUuidPrepared.Add(UuidBatch);
              }
              getPerfObjectInstance.Tag = PerfObjectInstanceResult.NextTag;
            } while (!string.IsNullOrEmpty(PerfObjectInstanceResult.NextTag));
            // read required counters for the instance collection
            if (!rawData.Keys.Contains(perObjectType))
              rawData.Add(perObjectType, new HistoricalQueue<InstanceData[]>(MaxCollectionDepth + 1)); // +1 is for possible shift when using the same timestamp
            List<InstanceData> collectedCounters = new List<InstanceData>();
            DateTime lastTimeStamp = DateTime.Now;
            foreach (var instanceBatch in instanceUuidPrepared)
            {
              PerfObjectGetInstances perfCollector = new PerfObjectGetInstances
              {
                Objectname = perObjectType,
                InstanceUuids = instanceBatch,
                Counters = typesAndCounters[perObjectType]
              };
              var perfData = perfCollector.Invoke(ONTAPCluster);
              if (perfData.Errno != 0)
                throw new ONTAPClusterAPIException(perfData.APIStatus, perfData.Errno,
                  perfData.FailureReason, "Unable to read performance data.");
              collectedCounters.AddRange(perfData.Instances);
              lastTimeStamp = UnixDateTime.From1970TimeStamp(perfData.Timestamp);
            }
            rawData[perObjectType].Enqueue(collectedCounters.ToArray(), lastTimeStamp);
            //Global.logWriteInformation("Submitting " + collectedCounters.Count.ToString() + " samples for " + perObjectType +
            //  " to Performance Collector Queue #" + idl.ToString() + " at " + lastTimeStamp.ToString(), this);
            lastReadTimestamp = DateTime.Now;
          }
        }
      }
    }
  }


  public enum ValueUnit { none, microsec, millisec, sec, per_sec, b_per_sec, kb_per_sec, mb_per_sec, mb, percent }
}