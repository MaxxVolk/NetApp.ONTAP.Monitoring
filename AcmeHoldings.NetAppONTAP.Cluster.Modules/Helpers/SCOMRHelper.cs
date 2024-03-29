using System;
using System.Collections.Generic;
using System.Text;

using System.Security.Cryptography;

namespace AcmeHoldings.Library.SCOM
{
  public static partial class SCOMRHelper
  {
    public static string NullToEmptyString(object obj)
    {
      try { return obj.ToString(); } catch { return string.Empty; }
    }

    public static string NullToEmptyString(string str)
    {
      if (str == null)
        return string.Empty;
      else
        return str;
    }

    /// <summary>
    /// Convert SCOM boolean string to genuine boolean value.
    /// </summary>
    /// <param name="BoolString">SCOM boolean value</param>
    /// <returns>c# boolean equivalent of SCOM boolean string</returns>
    public static bool ConvertSCOMBoolean(string BoolString)
    {
      bool _Result;
      _Result = false;
      if (!string.IsNullOrEmpty(BoolString))
      { _Result = string.Equals(BoolString, "true", StringComparison.OrdinalIgnoreCase) | string.Equals(BoolString, "1", StringComparison.OrdinalIgnoreCase); }
      return _Result;
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
      foreach(var pair in valueDictionary)
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

    /// <summary>
    /// Used to store Windows account in a basic auth SCOM run-as user account. Transform user@domain into DOMAIN\User.
    /// </summary>
    /// <param name="SplittedUserName"></param>
    /// <returns></returns>
    public static string TransformUsername(string SplittedUserName)
    {
      string domainSplitName = "", userSplitName = "";
      char[] Separators = { '@', '\\' };
      var RResult = SplittedUserName.Split(Separators);
      userSplitName = RResult[0];
      if (RResult.Length >= 2)
        domainSplitName = RResult[1];
      else
        domainSplitName = ".";
      return domainSplitName + "\\" + userSplitName;
    }

    // https://github.com/LogosBible/Logos.Utility/blob/master/src/Logos.Utility/GuidUtility.cs
    // http://www.ietf.org/rfc/rfc4122.txt
    public static Guid CreateElementId(Guid namespaceId, string name)
    {
      if (name == null)
        throw new ArgumentNullException("name");

      // convert the name to a sequence of octets (as defined by the standard or conventions of its namespace) (step 3)
      // ASSUME: UTF-8 encoding is always appropriate
      byte[] nameBytes = Encoding.UTF8.GetBytes(name);

      // convert the namespace UUID to network order (step 3)
      byte[] namespaceBytes = namespaceId.ToByteArray();
      SwapByteOrder(namespaceBytes);

      // compute the hash of the name space ID concatenated with the name (step 4)
      byte[] hash;
      using (HashAlgorithm algorithm = SHA1.Create())
      {
        algorithm.TransformBlock(namespaceBytes, 0, namespaceBytes.Length, null, 0);
        algorithm.TransformFinalBlock(nameBytes, 0, nameBytes.Length);
        hash = algorithm.Hash;
      }

      // most bytes from the hash are copied straight to the bytes of the new GUID (steps 5-7, 9, 11-12)
      byte[] newGuid = new byte[16];
      Array.Copy(hash, 0, newGuid, 0, 16);

      // set the four most significant bits (bits 12 through 15) of the time_hi_and_version field to the appropriate 4-bit version number from Section 4.1.3 (step 8)
      newGuid[6] = (byte)((newGuid[6] & 0x0F) | (5 << 4));

      // set the two most significant bits (bits 6 and 7) of the clock_seq_hi_and_reserved to zero and one, respectively (step 10)
      newGuid[8] = (byte)((newGuid[8] & 0x3F) | 0x80);

      // convert the resulting UUID to local byte order (step 13)
      SwapByteOrder(newGuid);
      return new Guid(newGuid);
    }

    /// <summary>
    /// The namespace for fully-qualified domain names (from RFC 4122, Appendix C).
    /// </summary>
    public static readonly Guid DnsNamespace = new Guid("6ba7b810-9dad-11d1-80b4-00c04fd430c8");

    /// <summary>
    /// The namespace for URLs (from RFC 4122, Appendix C).
    /// </summary>
    public static readonly Guid UrlNamespace = new Guid("6ba7b811-9dad-11d1-80b4-00c04fd430c8");

    /// <summary>
    /// The namespace for ISO OIDs (from RFC 4122, Appendix C).
    /// </summary>
    public static readonly Guid IsoOidNamespace = new Guid("6ba7b812-9dad-11d1-80b4-00c04fd430c8");

    // Converts a GUID (expressed as a byte array) to/from network order (MSB-first).
    internal static void SwapByteOrder(byte[] guid)
    {
      SwapBytes(guid, 0, 3);
      SwapBytes(guid, 1, 2);
      SwapBytes(guid, 4, 5);
      SwapBytes(guid, 6, 7);
    }

    private static void SwapBytes(byte[] guid, int left, int right)
    {
      byte temp = guid[left];
      guid[left] = guid[right];
      guid[right] = temp;
    }
  }
}