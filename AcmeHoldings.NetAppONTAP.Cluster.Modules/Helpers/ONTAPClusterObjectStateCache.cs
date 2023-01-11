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
using NetApp.Filer.Cluster.Volume;
using NetApp;
using System.Collections;
using System.Collections.Concurrent;
using System.Reflection;

namespace AcmeHoldings.NetApp.ONTAPCluster
{
  public delegate object ONTAPClusterObjectStateCacheReader<T>(IList<T> volumeCollection) where T: NaSerializable;

  public class LockableCacheList<T> : IList<T> where T: NaSerializable
  {
    private static readonly string[] nameSuffixes = { "Info", "", "Attributes", "Config" };
    private List<T> proxy;
    public DateTime UpdateTimestamp { get; set; }
    public double CacheAgeSeconds { get { return DateTime.Now.Subtract(UpdateTimestamp).TotalSeconds; } }
    public object LockObject = new object();

    protected Type getterType, getterResultType;

    public LockableCacheList()
    {
      UpdateTimestamp = DateTime.MinValue;
      proxy = new List<T>();
      Assembly rootAssembly = Assembly.GetAssembly(typeof(T));
      string dataTypeName = typeof(T).FullName;
      // there are three models: 
      // CoreNameInfo / CoreNameGetIter / CoreNameGetIterResult OR 
      // CoreName / CoreNameGetIter / CoreNameGetIterResult OR 
      // CoreNameAttributes / CoreNameGetIter / CoreNameGetIterResult OR
      // *Config !
      foreach (string suffix in nameSuffixes)
      {
        string coreName = dataTypeName.Substring(0, dataTypeName.Length - suffix.Length);
        getterType = rootAssembly.GetType(coreName + "GetIter", false);
        getterResultType = rootAssembly.GetType(coreName + "GetIterResult", false);
        if (getterType != null && getterResultType != null)
          break; // fund
      }
      if (getterType == null || getterResultType == null)
      {
        // if none of these works, try all types from that namespace, which have AttributesList property of the target type
        string targetNamespace = typeof(T).Namespace;
        foreach (var type in rootAssembly.GetTypes())
          if (type.Namespace == targetNamespace && type.Name.EndsWith("GetIterResult"))
          {
            Type attributesType = type.GetProperty("AttributesList")?.PropertyType;
            if (attributesType != null && attributesType.IsArray && attributesType.Name == (typeof(T).Name + "[]"))
            {
              getterResultType = type;
              getterType = rootAssembly.GetType(getterResultType.FullName.Substring(0, getterResultType.FullName.Length - "GetIterResult".Length) + "GetIter", false);
              break;
            }
          }
      }
      if (getterType == null || getterResultType == null)
        throw new ArgumentOutOfRangeException("Cannot find corresponding GetIter and GetIterResult types for " + dataTypeName);
    }

    public void ReadFromAppliance(NaFiler appliance)
    {
      object getter = Activator.CreateInstance(getterType);
      object getterResults;
      if (proxy == null)
        proxy = new List<T>();
      proxy.Clear();
      string nextTag = null;
      do
      {
        getterResults = getterType.InvokeMember("Invoke", BindingFlags.InvokeMethod, null, getter, new object[] { appliance });
        int errno = (int)getterResultType.GetProperty("Errno").GetValue(getterResults, null);
        if (errno != 0)
          throw new NaApiFailedException("Cannot query.", errno);
        long? numRecords = (long?)getterResultType.GetProperty("NumRecords").GetValue(getterResults, null);
        T[] itemList = (T[])getterResultType.GetProperty("AttributesList").GetValue(getterResults, null);
        if (numRecords > 0 && itemList != null)
          proxy.AddRange(itemList);
        nextTag = (string)getterResultType.GetProperty("NextTag").GetValue(getterResults, null);
        getterType.GetProperty("Tag").SetValue(getter, nextTag, null);
      } while (!string.IsNullOrEmpty(nextTag));
      UpdateTimestamp = DateTime.Now;
    }

    #region Proxy Interface
    public T this[int index] { get => ((IList<T>)proxy)[index]; set => ((IList<T>)proxy)[index] = value; }

    public int Count => ((IList<T>)proxy).Count;

    public bool IsReadOnly => ((IList<T>)proxy).IsReadOnly;

    public void Add(T item)
    {
      ((IList<T>)proxy).Add(item);
    }

    public void Clear()
    {
      ((IList<T>)proxy).Clear();
    }

    public bool Contains(T item)
    {
      return ((IList<T>)proxy).Contains(item);
    }

    public void CopyTo(T[] array, int arrayIndex)
    {
      ((IList<T>)proxy).CopyTo(array, arrayIndex);
    }

    public IEnumerator<T> GetEnumerator()
    {
      return ((IList<T>)proxy).GetEnumerator();
    }

    public int IndexOf(T item)
    {
      return ((IList<T>)proxy).IndexOf(item);
    }

    public void Insert(int index, T item)
    {
      ((IList<T>)proxy).Insert(index, item);
    }

    public bool Remove(T item)
    {
      return ((IList<T>)proxy).Remove(item);
    }

    public void RemoveAt(int index)
    {
      ((IList<T>)proxy).RemoveAt(index);
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
      return ((IList<T>)proxy).GetEnumerator();
    }
    #endregion
  }

  public class ONTAPClusterObjectStateCache
  {
    // Static variables to support singleton instances
    private static Dictionary<string, ONTAPClusterObjectStateCache> MyselfSingularInstance = new Dictionary<string, ONTAPClusterObjectStateCache>();
    private static readonly object MyselfSingularLock = new object();

    // per-instance variables
    private NaFiler ONTAPCluster;
    private DateTime lastRefreshTimestamp = DateTime.MinValue;
    private object globalLock_lastRefreshTimestamp = new object();
    // inventory data
    private ConcurrentDictionary<Type, object> objectCache = new ConcurrentDictionary<Type, object>();
    //private List<VolumeAttributes> AllSCMVolumes;
    //private object inventoryDataLock_AllSCMVolumes = new object();

    public double CacheAgeSeconds { get { lock (globalLock_lastRefreshTimestamp) return DateTime.Now.Subtract(lastRefreshTimestamp).TotalSeconds; } }

    private ONTAPClusterObjectStateCache(string clusterFQDN, NetworkCredential Credentials, bool? ForceUseUnsecure)
    {
      //LibraryInit.SetEventSource();
      //Global.AddLoggingSource(GetType(), 8);
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
    }

    public static ONTAPClusterObjectStateCache GetObjectCache(string clusterFQDN, NetworkCredential Credentials = null, bool? ForceUseUnsecure = null)
    {
      // http://csharpindepth.com/Articles/General/Singleton.aspx
      lock (MyselfSingularLock)
      {
        if (MyselfSingularInstance.ContainsKey(clusterFQDN.ToUpper()))
          return MyselfSingularInstance[clusterFQDN.ToUpper()];
        else
        {
          var newInstance = new ONTAPClusterObjectStateCache(clusterFQDN, Credentials, ForceUseUnsecure);
          MyselfSingularInstance.Add(clusterFQDN.ToUpper(), newInstance);
          return newInstance;
        }
      }
    }

    public object ReadCachedInventory<T>(ONTAPClusterObjectStateCacheReader<T> reader, double refreshTTL, out bool Refreshed) where T: NaSerializable
    {
      bool genuineRefresh = false;
      LockableCacheList<T> currentCacheElement = (LockableCacheList<T>)objectCache.GetOrAdd(typeof(T), new LockableCacheList<T>());
      lock (currentCacheElement.LockObject)
      {
        if (currentCacheElement.CacheAgeSeconds >= refreshTTL)
          genuineRefresh = true;
      }
      if (genuineRefresh)
      {
        lock (currentCacheElement.LockObject)
          currentCacheElement.ReadFromAppliance(ONTAPCluster);
        Refreshed = true;
        lock (globalLock_lastRefreshTimestamp)
          lastRefreshTimestamp = DateTime.Now;
      }
      else
        Refreshed = false;
      lock (currentCacheElement.LockObject)
      {
        return reader?.Invoke(currentCacheElement);
      }
    }
  }
}