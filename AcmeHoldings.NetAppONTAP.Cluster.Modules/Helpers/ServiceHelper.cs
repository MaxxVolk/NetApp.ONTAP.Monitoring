using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.IO;
using System.Management;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace Library.ServiceManagement
{
  [Flags]
  public enum DomainJoinFlags : int
  {
    JOIN_DOMAIN = 1,
    ACCT_CREATE = 2,
    ACCT_DELETE = 4,
    WIN9X_UPGRADE = 16,
    DOMAIN_JOIN_IF_JOINED = 32,
    JOIN_UNSECURE = 64,
    MACHINE_PASSWORD_PASSED = 128,
    DEFERRED_SPN_SET = 256,
    INSTALL_INVOCATION = 262144,
  }

  public static class ServiceHelper
  {
    /// <summary>
    /// Translate a wildcard sting to a regular expression one.
    /// </summary>
    /// <param name="pattern">Wildcard expression to translate</param>
    /// <returns>Regular expression equivalent of the given wildcard one</returns>
    public static string WildcardToRegex(string pattern)
    {
      return "^" + Regex.Escape(pattern).Replace("\\*", ".*").Replace("\\?", ".") + "$";
    }

    public static bool IsRegexMatchAny(string input, IEnumerable<string> patterns)
    {
      foreach (var pattern in patterns)
        if (Regex.IsMatch(input, pattern))
          return true;
      return false;
    }

    public static string ObjectArrayToSeparatedString(IEnumerable<object> inputObjects, string separator = "; ")
    {
      string Result = "";
      foreach (object obj in inputObjects)
        Result += obj.ToString() + separator;
      return Result.Substring(0, Result.LastIndexOf(separator));
    }

    public static bool IsCluster(string computerName)
    {
      RegistryKey remoteRegistry = RegistryKey.OpenRemoteBaseKey(RegistryHive.LocalMachine, ComputerHelper.LocalizeName(computerName));
      RegistryKey clusterKey = remoteRegistry.OpenSubKey("Cluster");
      return clusterKey != null;
    }

    public static bool Is64Bit()
    {
      string processorArchitecture = null;
      try
      {
        processorArchitecture = Environment.GetEnvironmentVariable("PROCESSOR_ARCHITECTURE");
      }
      catch
      {
        return false;
      }
      if (string.IsNullOrEmpty(processorArchitecture))
        return false;
      if (processorArchitecture.Contains("AMD64"))
        return true;
      return false;
    }

    public static string GetTimeBasedTempFileName(bool useDate, bool useTime, bool useSeconds, bool useMilliseconds, bool useRandom, string prefix = null, string extension = "log", bool useLocalTime = true)
    {
      string fileName = "";
      DateTime now = DateTime.UtcNow;
      if (useLocalTime)
        now = DateTime.Now;
      if (useDate)
        fileName += now.ToString("yyyy-MM-dd");
      if (useTime)
        fileName += (useDate ? "-" : "") + now.ToString("HH-mm");
      if (useSeconds)
        fileName += (useDate || useTime ? "-" : "") + now.ToString("ss");
      if (useMilliseconds)
        fileName += (useDate || useTime || useSeconds ? "-" : "") + now.Millisecond.ToString();
      if (useRandom)
      {
        Random random = new Random();
        fileName += (useDate || useTime || useSeconds || useMilliseconds ? "-" : "") + random.Next().ToString("D4").Substring(0, 3);
      }
      fileName = (prefix ?? "") + fileName + "." + (extension ?? "").TrimStart(new char[] { ' ', '.' });
      return fileName;
    }

    public static DateTime FromUnixTime(uint secondsSince1970)
    {
      DateTime Result = new DateTime(1970, 1, 1, 0, 0, 0, 0);
      return Result.AddSeconds(secondsSince1970);
    }
  }

  public static class WMIHelper
  {
    public static ManagementScope LocalManagementScope => new ManagementScope(ManagementPath.DefaultPath);
    public static ManagementBaseObject InvokeWMIMethod(this ManagementObject managementObject, string methodName, Dictionary<string, object> parameters, InvokeMethodOptions methodOptions = null)
    {
      ManagementBaseObject inParams = managementObject.GetMethodParameters(methodName);
      foreach(KeyValuePair<string, object> param in parameters)
        inParams[param.Key] = param.Value;
      return managementObject.InvokeMethod(methodName, inParams, methodOptions);
    }

    public static DataTable GetWMIQuery(string computerName, string WQLquery, string WMInamespace = @"\root\cimv2")
    {
      ManagementScope scope = new ManagementScope("\\\\" + ComputerHelper.LocalizeName(computerName, ".") + WMInamespace);
      scope.Connect();
      return GetWMIQuery(scope, WQLquery, WMInamespace);
    }

    public static DataTable GetWMIQuery(string WQLquery, string WMInamespace = @"\root\cimv2")
    {
      ManagementScope scope = new ManagementScope(WMInamespace);
      scope.Connect();
      return GetWMIQuery(scope, WQLquery, WMInamespace);
    }

    public static DataTable GetWMIQuery(ManagementScope scope, string WQLquery, string WMInamespace = @"\root\cimv2")
    {
      if (!scope.IsConnected)
        scope.Connect();
      if (scope.IsConnected)
      {
        ObjectQuery query = new ObjectQuery(WQLquery);
        ManagementObjectSearcher objectList = null;
        ManagementObjectCollection allResources = null;
        DataTable Result = null;
        try
        {
          objectList = new ManagementObjectSearcher(scope, query);
          allResources = objectList.Get();
          foreach (ManagementBaseObject x in allResources)
          {
            // create output table and use the first row to initialize columns
            if (Result == null)
            {

              Result = new DataTable(x.ClassPath.ClassName);
              foreach (var col in x.Properties)
                Result.Columns.Add(col.Name, col.GetManagedType());
            }
            // Add data row
            DataRow newRow = Result.NewRow();
            foreach (var dataCol in x.Properties)
            {
              if (x[dataCol.Name] == null)
              {
                newRow[dataCol.Name] = DBNull.Value;
                continue;
              }
              if (dataCol.Type == CimType.DateTime)
                newRow[dataCol.Name] = ManagementDateTimeConverter.ToDateTime(x[dataCol.Name].ToString());
              else
                newRow[dataCol.Name] = x[dataCol.Name];
            }
            Result.Rows.Add(newRow);
            x.Dispose();
          }
        }
        finally
        {
          if (allResources != null)
            allResources.Dispose();
          if (objectList != null)
            objectList.Dispose();
        }
        return Result;
      }
      return null;
    }
  }

  public class WMIQuery : IDisposable
  {
    protected ManagementScope queryScope;
    protected ObjectQuery query;
    protected ManagementObjectSearcher moSearcher = null;
    protected ManagementObjectCollection moCollection = null;

    public WMIQuery(string wqlQuery)
    {
      DefaultConstructor(WMIHelper.LocalManagementScope, wqlQuery);
    }

    public WMIQuery(ManagementScope managementScope, string wqlQuery)
    {
      DefaultConstructor(managementScope, wqlQuery);
    }

    protected void DefaultConstructor(ManagementScope managementScope, string wqlQuery)
    {
      queryScope = managementScope;
      query = new ObjectQuery(wqlQuery);
    }

    public ManagementObjectCollection Select()
    {
      if (!ManagementScope.IsConnected)
        ManagementScope.Connect();
      if (!disposedValue) // for repetitive query
        Dispose(false);
      try
      {
        moSearcher = new ManagementObjectSearcher(ManagementScope, Query);
        moCollection = moSearcher.Get();
      }
      finally
      {
        disposedValue = false;
      }
      return moCollection;
    }

    public ManagementScope ManagementScope => queryScope;
    public ObjectQuery Query => query;

    #region IDisposable Support
    private bool disposedValue = true; // no unmanaged resources allocated by default

    protected virtual void Dispose(bool disposing)
    {
      if (!disposedValue)
      {
        if (moCollection != null)
          moCollection.Dispose();
        if (moSearcher != null)
          moSearcher.Dispose();

        disposedValue = true;
      }
    }

    ~WMIQuery()
    {
      Dispose(false);
    }

    public void Dispose()
    {
      Dispose(true);
    }
    #endregion
  }

  public static class ComputerHelper
  {
    /// <summary>
    /// Simplify computer name if it is the local computer. Make the string empty (default) or replace with the specified value.
    /// </summary>
    /// <param name="remoteName">Input: computer name to match with the local.</param>
    /// <param name="localReplacement">If the input name is local, then replace with this string, default to empty string.</param>
    /// <returns></returns>
    public static string LocalizeName(string remoteName, string localReplacement = "")
    {
      if (string.IsNullOrEmpty(remoteName) || remoteName == "." || remoteName == string.Empty || remoteName.ToUpperInvariant() == "LOCALHOST")
        return localReplacement;
      if (remoteName.ToUpperInvariant() == Environment.MachineName.ToUpperInvariant())
        return localReplacement;
      if (remoteName.ToUpperInvariant() == Dns.GetHostName().ToUpperInvariant())
        return localReplacement;
      if (remoteName.ToUpperInvariant() == Dns.GetHostName().ToUpperInvariant() + "." + IPGlobalProperties.GetIPGlobalProperties().DomainName.ToUpperInvariant())
        return localReplacement;
      return remoteName;
    }

    public static string GetMachineDNSName()
    {
      string computerName = "";
      string dnsName = Dns.GetHostName();
      if (string.IsNullOrEmpty(dnsName))
        computerName = Environment.MachineName;
      else
      {
        if (dnsName.IndexOf(".") >= 0)
          computerName = dnsName;
        else
        {
          string dnsSuffix = IPGlobalProperties.GetIPGlobalProperties().DomainName;
          computerName = dnsName + (string.IsNullOrEmpty(dnsSuffix) ? "" : $".{dnsSuffix}");
        }
      }
      return computerName;
    }

    public static List<IPAddress> GetMachineIPAddresses(NetworkInterfaceType _type)
    {
      List<IPAddress> ipAddrList = new List<IPAddress>();
      foreach (NetworkInterface item in NetworkInterface.GetAllNetworkInterfaces())
      {
        if (item.NetworkInterfaceType == _type && item.OperationalStatus == OperationalStatus.Up)
        {
          foreach (UnicastIPAddressInformation ip in item.GetIPProperties().UnicastAddresses)
          {
            if (ip.Address.AddressFamily == AddressFamily.InterNetwork)
            {
              ipAddrList.Add(ip.Address);
            }
          }
        }
      }
      return ipAddrList;
    }

    public static void JoinDomainOrWorkgroup(string domainName, string AccountOU = null, string username = null, string password = null, DomainJoinFlags JoinOptions = DomainJoinFlags.ACCT_CREATE | DomainJoinFlags.JOIN_DOMAIN)
    {
      using (WMIQuery localComputer = new WMIQuery("SELECT * FROM Win32_ComputerSystem"))
        foreach (ManagementBaseObject computer in localComputer.Select())
        {
          ManagementObject computerSystem = (ManagementObject)computer;
          computerSystem.Scope.Options.Authentication = AuthenticationLevel.PacketPrivacy;
          computerSystem.Scope.Options.Impersonation = ImpersonationLevel.Impersonate;
          computerSystem.Scope.Options.EnablePrivileges = true;
          // Execute the method and obtain the return values.
          ManagementBaseObject outParams = computerSystem.InvokeWMIMethod("JoinDomainOrWorkgroup", new Dictionary<string, object>
          {
            { "Name", domainName },
            { "AccountOU", AccountOU },
            { "Password", password },
            { "UserName", username },
            { "FJoinOptions", (int)JoinOptions }
          });
          // Did it work?
          int hResult = Convert.ToInt32((uint)outParams.Properties["ReturnValue"].Value);
          if (hResult != 0)
          {
            switch (hResult)
            {
              case 5:
                throw new Win32Exception(hResult, "Access is denied");
              case 87:
                throw new Win32Exception(hResult, "The parameter is incorrect");
              case 110:
                throw new Win32Exception(hResult, "The system cannot open the specified object");
              case 1323:
                throw new Win32Exception(hResult, "Unable to update the password");
              case 1326:
                throw new Win32Exception(hResult, "Logon failure: unknown username or bad password");
              case 1355:
                throw new Win32Exception(hResult, "The specified domain either does not exist or could not be contacted");
              case 2224:
                throw new Win32Exception(hResult, "The account already exists");
              case 2691:
                throw new Win32Exception(hResult, "The machine is already joined to the domain");
              case 2692:
                throw new Win32Exception(hResult, "The machine is not currently joined to a domain");
              default:
                throw new Win32Exception(hResult);
            }
          }
          break; // there must be only one object always
        }
    }

    public static void SetDNSServerSearchOrder(string[] dnsSearchOrder, string SettingID)
    {
      using (WMIQuery localComputer = new WMIQuery($"SELECT * FROM Win32_NetworkAdapterConfiguration where SettingID = '{SettingID}'"))
        foreach (ManagementBaseObject nic in localComputer.Select())
        {
          ManagementObject networkAdapter = (ManagementObject)nic;
          networkAdapter.Scope.Options.EnablePrivileges = true;
          ManagementBaseObject outParams = networkAdapter.InvokeWMIMethod("SetDNSServerSearchOrder", new Dictionary<string, object>
          {
            { "DNSServerSearchOrder", dnsSearchOrder }
          });
          int hResult = Convert.ToInt32((uint)outParams.Properties["ReturnValue"].Value);
          if (hResult != 0)
          {
            switch (hResult)
            {
              case 1:
                throw new Win32Exception(hResult, "Reboot is required.");
              case 91:
                throw new Win32Exception(hResult, "Access is denied");
              default:
                throw new Win32Exception(hResult);
            }
          }
        }
    }

    public static string GetMachinePrincipalName()
    {
      string Result = ".";
      try
      {
        ManagementScope localWMI = new ManagementScope(ManagementPath.DefaultPath);
        localWMI.Connect();
        ObjectQuery query = new ObjectQuery("SELECT * FROM Win32_ComputerSystem");
        ManagementObjectSearcher computerSystemSearcher = null;
        ManagementObjectCollection computerSystemList = null;
        ManagementObjectCollection.ManagementObjectEnumerator computerSystemEnum = null;
        try
        {
          computerSystemSearcher = new ManagementObjectSearcher(localWMI, query);
          computerSystemList = computerSystemSearcher.Get();
          computerSystemEnum = computerSystemList.GetEnumerator();
          if (computerSystemEnum.MoveNext())
          {
            ManagementBaseObject computerSystem = computerSystemEnum.Current;
            if ((bool)computerSystem["PartOfDomain"])
              Result = computerSystem["DNSHostName"].ToString() + "." + computerSystem["Domain"].ToString();
            else
              Result = computerSystem["DNSHostName"].ToString();
          }
          else
            throw new ArgumentNullException("No instances of Win32_ComputerSystem WMI class returned.");
        }
        finally
        {
          if (computerSystemEnum != null)
            computerSystemEnum.Dispose();
          if (computerSystemList != null)
            computerSystemList.Dispose();
          if (computerSystemSearcher != null)
            computerSystemSearcher.Dispose();
        }
      }
      catch
      {
        Result = Environment.MachineName;
      }
      return Result;
    }

    public static long GetUptime(string computerName)
    {
      ManagementObject mo = new ManagementObject(@"\\" + LocalizeName(computerName, ".") + @"\root\cimv2:Win32_OperatingSystem=@");
      DateTime lastBootUp = ManagementDateTimeConverter.ToDateTime(mo["LastBootUpTime"].ToString());
      return Convert.ToInt64((DateTime.Now.ToUniversalTime() - lastBootUp.ToUniversalTime()).TotalSeconds);
    }

    public static DataTable GetQueryWMI(string computerName, string WQLquery, string WMInamespace = @"\root\cimv2")
    {
      ManagementScope scope = new ManagementScope("\\\\" + LocalizeName(computerName, ".") + WMInamespace);
      scope.Connect();
      if (scope.IsConnected)
      {
        ObjectQuery query = new ObjectQuery(WQLquery);
        ManagementObjectSearcher objectList = null;
        ManagementObjectCollection allResources = null;
        DataTable Result = null;
        try
        {
          objectList = new ManagementObjectSearcher(scope, query);
          allResources = objectList.Get();
          foreach (ManagementBaseObject x in allResources)
          {
            // create output table and use the first row to initialize columns
            if (Result == null)
            {
              
              Result = new DataTable(x.ClassPath.ClassName);
              foreach (var col in x.Properties)
                Result.Columns.Add(col.Name, col.GetManagedType());
            }
            // Add data row
            DataRow newRow = Result.NewRow();
            foreach (var dataCol in x.Properties)
            {
              if (x[dataCol.Name] == null)
              {
                newRow[dataCol.Name] = DBNull.Value;
                continue;
              }
              if (dataCol.Type == CimType.DateTime)
                newRow[dataCol.Name] = ManagementDateTimeConverter.ToDateTime(x[dataCol.Name].ToString());
              else
                newRow[dataCol.Name] = x[dataCol.Name];
            }
            Result.Rows.Add(newRow);
            x.Dispose();
          }
        }
        finally
        {
          if (allResources != null)
            allResources.Dispose();
          if (objectList != null)
            objectList.Dispose();
        }
        return Result;
      }
      return null;
    }
  }

  public static class RegistryHelper
  {
    public static string ReadRegistryString(string computerName, string fullPath, string defaultValue = null)
    {
      return ReadRegistryValue(computerName, fullPath, defaultValue).ToString();
    }

    public static DateTime ReadRegistryUnixTime(string computerName, string fullPath)
    {
      int rawResult = (int)ReadRegistryValue(computerName, fullPath, null);
      uint unixTime = unchecked((uint)rawResult);
      return ServiceHelper.FromUnixTime(unixTime);
    }

    public static uint ReadRegistryUInt(string computerName, string fullPath, uint? defaultValue = null)
    {
      int rawResult = (int)ReadRegistryValue(computerName, fullPath, defaultValue);
      return unchecked((uint)rawResult);
    }

    public static bool ReadRegistryBoolean(string computerName, string fullPath, bool? defaultValue = null)
    {
      int? defaultInt = null;
      if (defaultValue != null)
        defaultInt = defaultValue == true ? 1 : 0;
      int RawResult = (int)ReadRegistryValue(computerName, fullPath, defaultInt);
      return RawResult != 0;
    }

    public static ulong ReadRegistryULong(string computerName, string fullPath, ulong? defaultValue = null)
    {
      return unchecked((ulong)ReadRegistryValue(computerName, fullPath, defaultValue));
    }

    /// <summary>
    /// Read registry parameter value from full path: HKLM:\Key\Parameter. The PowerShell-style key prefix can be replaced with its
    /// full name: HKEY_LOCAL_MACHINE
    /// </summary>
    /// <param name="computerName">Remote computer name. Leave empty, null, '.' or 'localhost' to access local Registry.</param>
    /// <param name="fullPath"></param>
    /// <returns></returns>
    public static object ReadRegistryValue(string computerName, string fullPath, object defaultValue = null)
    {
      try
      {
        string pathName = ParseValuePath(fullPath, out string valueName);
        object returnValue = null;
        using (RegistryKey valueKey = GetRegistryKey(computerName, pathName))
        {
          if (valueKey == null)
            throw new IOException(pathName + " registry path not found.");
          returnValue = valueKey.GetValue(valueName);
        }
        if (returnValue == null)
          throw new IOException(valueName + " registry value not found.");
        return returnValue;
      }
      catch (IOException)
      {
        if (defaultValue != null)
          return defaultValue;
        else
          throw;
      }
    }

    public static bool RegistryKeyExists(string computerName, string keyPath)
    {
      if (GetRegistryKey(computerName, keyPath) == null)
        return false;
      else
        return true;
    }

    public static bool RegistryValueExists(string computerName, string fullPath)
    {
      try
      {
        ReadRegistryValue(computerName, fullPath);
        return true;
      }
      catch (IOException)
      {
        return false;
      }
    }

    public static RegistryKey CreateRegistryKey(string computerName, string keyPath, bool writable = false)
    {
      RegistryKey draftResult = GetRegistryKey(computerName, keyPath, writable);
      if (draftResult != null)
        return draftResult;
      RegistryHive hKey = ParseKeyPath(keyPath, out string pathName);
      RegistryKey remoteRegistry = RegistryKey.OpenRemoteBaseKey(hKey, ComputerHelper.LocalizeName(computerName));
      if (!string.IsNullOrEmpty(pathName))
      {
        string[] pathParts = pathName.Split(new char[] { '\\' }, StringSplitOptions.RemoveEmptyEntries);
        RegistryKey cursor = remoteRegistry;
        foreach (string part in pathParts)
        {
          RegistryKey nested = cursor.OpenSubKey(part, true);
          if (nested == null)
            nested = cursor.CreateSubKey(part);
          cursor = nested;
        }
        return cursor;
      }
      else
        return remoteRegistry;
    }

    public static RegistryKey GetRegistryKey(string computerName, string keyPath, bool writable = false)
    {
      RegistryHive hKey = ParseKeyPath(keyPath, out string pathName);
      RegistryKey remoteRegistry = RegistryKey.OpenRemoteBaseKey(hKey, ComputerHelper.LocalizeName(computerName));
      if (string.IsNullOrEmpty(pathName))
        return remoteRegistry;
      else
        return remoteRegistry.OpenSubKey(pathName, writable);
    }

    private static RegistryHive ParseKeyPath(string keyPath, out string relativePath)
    {
      int firstPeriodPos = keyPath.IndexOf("\\");
      string hKeyName = keyPath.Substring(0, firstPeriodPos);
      RegistryHive hKey = RegistryHive.LocalMachine;
      switch (hKeyName.ToUpperInvariant())
      {
        case "HKLM:":
        case "HKLM":
        case "HKEY_LOCAL_MACHINE":
          hKey = RegistryHive.LocalMachine;
          break;
        case "HKCR:":
        case "HKCR":
        case "HKEY_CLASSES_ROOT":
          hKey = RegistryHive.ClassesRoot;
          break;
        case "HKCC:":
        case "HKCC":
        case "HKEY_CURRENT_CONFIG":
          hKey = RegistryHive.CurrentConfig;
          break;
        case "HKCU:":
        case "HKCU":
        case "HKEY_CURRENT_USER":
          hKey = RegistryHive.CurrentUser;
          break;
        case "HKDD:":
        case "HKDD":
        case "HKEY_DYN_DATA":
          hKey = RegistryHive.DynData;
          break;
        case "HKPD:":
        case "HKPD":
        case "HKEY_PERFROMANCE_DATA":
          hKey = RegistryHive.PerformanceData;
          break;
        case "HKU:":
        case "HKU":
        case "HKEY_USERS":
          hKey = RegistryHive.Users;
          break;
      }
      relativePath = keyPath.Substring(firstPeriodPos + 1);
      return hKey;
    }

    private static string ParseValuePath(string fullPath, out string valueName)
    {
      int lastPeriodPos = fullPath.LastIndexOf("\\");
      valueName = fullPath.Substring(lastPeriodPos + 1);
      return fullPath.Substring(0, lastPeriodPos);
    }
  }

  public delegate T RemediationAction<T>();

  public delegate Exception ExceptionAction();

  public delegate void UsingAction<T>(T input);

  public static class AssertationHelper
  {
    public static T AssertNotNull<T>(T input)
    {
      if (input != null)
        return input;
      throw new ArgumentNullException();
    }

    public static T AssertNotNull<T>(T input, Action action)
    {
      if (input != null)
        return input;
      action?.Invoke();
      return default(T);
    }

    public static T AssertNotNull<T>(T input, ExceptionAction action)
    {
      if (input != null)
        return input;
      throw action?.Invoke();
    }

    public static T AssertNotNull<T>(T input, RemediationAction<T> action)
    {
      if (input != null)
        return input;
      return action();
    }

    public static void UseIfNotNull<T>(T input, UsingAction<T> usingAction)
    {
      if (input != null)
        usingAction?.Invoke(input);
    }
  }

  /// <summary>
  /// Compare machine names taking domain suffix in account. If both names are FQDN or NetBIOS names, then literal comparison
  /// applied, otherwise either name reduced to NetBIOS format.
  /// </summary>
  public class MachineNameComparer : IComparer<string>
  {
    public int Compare(string x, string y)
    {
      bool isXfull = (x.IndexOf(".") > 0); // cannot be 0, as ".dnssuffix.name" is invalid.
      bool isYfull = (y.IndexOf(".") > 0);
      if (isXfull && isYfull)
        return StringComparer.OrdinalIgnoreCase.Compare(x, y);
      if (!isXfull && !isYfull)
        return StringComparer.OrdinalIgnoreCase.Compare(x, y);
      if (isXfull)
        return StringComparer.OrdinalIgnoreCase.Compare(x.Substring(0, x.IndexOf(".")), y);
      if (isYfull)
        return StringComparer.OrdinalIgnoreCase.Compare(x, y.Substring(0, y.IndexOf(".")));
      throw new Exception("It's impossible to get here. Suppressing compiler error.");
    }

    public static MachineNameComparer OrdinalIgnoreCase
    {
      get { return new MachineNameComparer(); }
    }
  }

  public static class PropertyDataExtension
  {
    public static Type GetManagedType(this PropertyData property)
    {
      if (property.IsArray)
      {
        switch (property.Type)
        {
          case CimType.Boolean: return typeof(bool[]);
          case CimType.Char16: return typeof(char[]);
          case CimType.DateTime: return typeof(DateTime[]);
          case CimType.Real32: return typeof(float[]);
          case CimType.Real64: return typeof(double[]);
          case CimType.SInt16: return typeof(short[]);
          case CimType.SInt32: return typeof(int[]);
          case CimType.SInt64: return typeof(long[]);
          case CimType.SInt8: return typeof(sbyte[]);
          case CimType.String: return typeof(string[]);
          case CimType.UInt16: return typeof(ushort[]);
          case CimType.UInt32: return typeof(uint[]);
          case CimType.UInt64: return typeof(ulong[]);
          case CimType.UInt8: return typeof(byte[]);
          default: return typeof(object[]);
        }
      }
      else
      {
        switch (property.Type)
        {
          case CimType.Boolean: return typeof(bool);
          case CimType.Char16: return typeof(char);
          case CimType.DateTime: return typeof(DateTime);
          case CimType.Real32: return typeof(float);
          case CimType.Real64: return typeof(double);
          case CimType.SInt16: return typeof(short);
          case CimType.SInt32: return typeof(int);
          case CimType.SInt64: return typeof(long);
          case CimType.SInt8: return typeof(sbyte);
          case CimType.String: return typeof(string);
          case CimType.UInt16: return typeof(ushort);
          case CimType.UInt32: return typeof(uint);
          case CimType.UInt64: return typeof(ulong);
          case CimType.UInt8: return typeof(byte);
          default: return typeof(object);
        }
      }
    }
  }

  public enum BinaryPrefix { Byte = 0, KiloByte = 1, MegaByte = 2, GigaByte = 3, TeraByte = 4, PetaByte = 5, ExaByte = 6, ZettaByte = 7, YottaByte = 8 }

  public static class FormattingHelper
  {
    private static string[] decimalPrefixNames = { "B", "KB", "MB", "GB", "TB", "PB", "EB", "ZB", "YB" };
    private static string[] binaryPrefixNames = { "B", "KiB", "MiB", "GiB", "TiB", "PiB", "EiB", "ZiB", "YiB" };

    /// <summary>
    /// Convert byte count for human readable display value, by auto-choosing the best unit.
    /// </summary>
    /// <param name="bytes">Original number by bytes.</param>
    /// <param name="prcisionDigits">Number of decimal fraction digits in output.</param>
    /// <param name="useDecimalPrefixes">Chose between decimal (kilo/KB) or binary (kibi/KiB) prefixes. The actual divider is ALWAYS 1024 regardless the prefix.</param>
    /// <returns>Formatted string.</returns>
    public static string FormatBytes1024(long bytesCount, int prcisionDigits = 2, bool useDecimalPrefixes = true)
    {
      decimal bytes = bytesCount;
      return FormatBytes1024(bytes, prcisionDigits, useDecimalPrefixes);
    }

    /// <summary>
    /// Convert byte count for human readable display value, by auto-choosing the best unit.
    /// </summary>
    /// <param name="bytes">Original number by bytes.</param>
    /// <param name="prcisionDigits">Number of decimal fraction digits in output.</param>
    /// <param name="useDecimalPrefixes">Chose between decimal (kilo/KB) or binary (kibi/KiB) prefixes. The actual divider is ALWAYS 1024 regardless the prefix.</param>
    /// <returns>Formatted string.</returns>
    public static string FormatBytes1024(decimal bytes, int prcisionDigits = 2, bool useDecimalPrefixes = true)
    {
      int magnifierCounter = 0;
      do
      {
        if (bytes < 1024)
          return bytes.ToString($"N{prcisionDigits}") + " " + (useDecimalPrefixes ? decimalPrefixNames[magnifierCounter] : binaryPrefixNames[magnifierCounter]);
        bytes = bytes / 1024;
        magnifierCounter++;
      } while (magnifierCounter < decimalPrefixNames.Length);
      return (bytes * 1024).ToString($"N{prcisionDigits}") + " " + (useDecimalPrefixes ? decimalPrefixNames[magnifierCounter - 1] : binaryPrefixNames[magnifierCounter - 1]);
    }

    public static double ConvertBytes1024(decimal fromBytes, BinaryPrefix fromUnit, BinaryPrefix toUnit)
    {
      int magnifierCounter = fromUnit - toUnit;
      if (magnifierCounter == 0)
        return Convert.ToDouble(fromBytes);
      if (magnifierCounter > 0)
      {
        for (int idx = 0; idx < magnifierCounter; idx++)
          fromBytes = fromBytes * 1024;
        return Convert.ToDouble(fromBytes);
      }
      if (magnifierCounter < 0)
      {
        for (int idx = 0; idx < -magnifierCounter; idx++)
          fromBytes = fromBytes / 1024;
        return Convert.ToDouble(fromBytes);
      }
      throw new OverflowException("You just did the impossible!");
    }

    public static string EnumElements(this object[] valueList, string separator = "; ")
    {
      if (valueList == null)
        return string.Empty;
      if (valueList.Length == 0)
        return string.Empty;
      string Result = "";
      foreach (var value in valueList)
        Result += value.ToString() + separator;
      return Result.Substring(0, Result.Length - separator.Length);
    }

    public static string EnumElements(this Dictionary<string, object> valueDictionary, string itemSeparator = "; ", string namevalueSeparator = ": ")
    {
      if (valueDictionary == null)
        return string.Empty;
      if (valueDictionary.Keys.Count == 0)
        return string.Empty;
      string Result = "";
      foreach (var pair in valueDictionary)
        Result += pair.Key.ToString() + namevalueSeparator + pair.Value.ToString() + itemSeparator;
      return Result.Substring(0, Result.Length - itemSeparator.Length);
    }

    public static string EnumElements(this Dictionary<string, double> valueDictionary, string itemSeparator = "; ", string namevalueSeparator = ": ", string doubleFormat = "N2")
    {
      if (valueDictionary == null)
        return string.Empty;
      if (valueDictionary.Keys.Count == 0)
        return string.Empty;
      string Result = "";
      foreach (var pair in valueDictionary)
        Result += pair.Key.ToString() + namevalueSeparator + pair.Value.ToString(doubleFormat) + itemSeparator;
      return Result.Substring(0, Result.Length - itemSeparator.Length);
    }
  }
}