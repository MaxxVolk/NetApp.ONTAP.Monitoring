using Microsoft.EnterpriseManagement.HealthService;
using Microsoft.EnterpriseManagement.Mom.Modules.DataItems;
using AcmeHoldings.Library.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml;

namespace AcmeHoldings.NetAppONTAP.Cluster.Modules
{
  [MonitoringModule(ModuleType.Condition)]
  [ModuleOutput(true)]
  class DynamicDiskThresholdCD : ModuleBaseSimpleAction<PropertyBagDataItem>
  {
    private string TotalSizePropertyPath, FreeSpacePropertyPath, UsedSpacePropertyPath;
    private decimal ByteMultiplicator;
    private ThresholdBrackets thresholdBrackets;

    public DynamicDiskThresholdCD(ModuleHost<PropertyBagDataItem> moduleHost, XmlReader configuration, byte[] previousState) : base(moduleHost, configuration, previousState)
    {
    }

    protected override PropertyBagDataItem[] GetOutputData(DataItemBase[] inputDataItems)
    {
      if (inputDataItems == null || inputDataItems.Length != 1 || inputDataItems[0].GetType() != typeof(PropertyBagDataItem))
        throw new ModuleException("Invalid input data packet in AcmeHoldings.NetAppONTAP.Cluster.Monitoring.StorageDynamicThreshold condition detection module.");
      PropertyBagDataItem inputData = (PropertyBagDataItem)inputDataItems[0];
      decimal TotalSizeByte = Convert.ToDecimal(inputData.QueryItem(TotalSizePropertyPath).ToDouble()) * ByteMultiplicator;
      decimal FreeSpaceByte = 0;
      if (!string.IsNullOrWhiteSpace(FreeSpacePropertyPath))
        FreeSpaceByte = Convert.ToDecimal(inputData.QueryItem(FreeSpacePropertyPath).ToDouble()) * ByteMultiplicator;
      else
        FreeSpaceByte = TotalSizeByte - Convert.ToDecimal(inputData.QueryItem(UsedSpacePropertyPath).ToDouble()) * ByteMultiplicator;
      if (thresholdBrackets.IsInRange(TotalSizeByte, FreeSpaceByte))
        return new PropertyBagDataItem[] { inputData };
      else
        return null;
    }

    protected override void LoadConfiguration(XmlReader configuration)
    {
      /*
       * <xsd:element minOccurs="1" name="TotalSizePropertyPath" type="xsd:string" />
       * <xsd:element minOccurs="0" name="FreeSpacePropertyPath" type="xsd:string" />
       * <xsd:element minOccurs="0" name="UsedSpacePropertyPath" type="xsd:string" />
       * <xsd:element minOccurs="1" name="ByteMultiplicator" type="xsd:double" />
       * <xsd:element minOccurs="0" name="BottomPercentThreshold" type="xsd:double" />
       * <xsd:element minOccurs="0" name="BottomAbsoluteThreshold" type="xsd:double" />
       * <xsd:element minOccurs="0" name="TopPercentThreshold" type="xsd:double" />
       * <xsd:element minOccurs="0" name="TopAbsoluteThreshold" type="xsd:double" />
       */
      try
      {
        // load
        XmlDocument xmlDoc = new XmlDocument();
        xmlDoc.Load(configuration);
        LoadConfigurationElement(xmlDoc, "TotalSizePropertyPath", out TotalSizePropertyPath);
        LoadConfigurationElement(xmlDoc, "FreeSpacePropertyPath", out FreeSpacePropertyPath, null, false);
        LoadConfigurationElement(xmlDoc, "UsedSpacePropertyPath", out UsedSpacePropertyPath, null, false);
        if ((string.IsNullOrWhiteSpace(FreeSpacePropertyPath) && string.IsNullOrWhiteSpace(UsedSpacePropertyPath)) ||
            (!string.IsNullOrWhiteSpace(FreeSpacePropertyPath) && !string.IsNullOrWhiteSpace(UsedSpacePropertyPath)))
          throw new ModuleException("Invalid configuration for AcmeHoldings.NetAppONTAP.Cluster.Monitoring.StorageDynamicThreshold. Either FreeSpacePropertyName or UsedSpacePropertyName must have a value, but not the both, which will define free space mode, or used space mode.");

        LoadConfigurationElement(xmlDoc, "ByteMultiplicator", out double localByteMultiplicator);
        ByteMultiplicator = Convert.ToDecimal(localByteMultiplicator);

        LoadConfigurationElement(xmlDoc, "BottomPercentThreshold", out double localBottomPercentThreshold, -1, false);
        LoadConfigurationElement(xmlDoc, "BottomAbsoluteThreshold", out double localBottomAbsoluteThreshold, -1, false);
        LoadConfigurationElement(xmlDoc, "TopPercentThreshold", out double localTopPercentThreshold, -1, false);
        LoadConfigurationElement(xmlDoc, "TopAbsoluteThreshold", out double localTopAbsoluteThreshold, -1, false);
          thresholdBrackets = new ThresholdBrackets(Convert.ToDecimal(localTopPercentThreshold), Convert.ToDecimal(localTopAbsoluteThreshold),
            Convert.ToDecimal(localBottomPercentThreshold), Convert.ToDecimal(localBottomAbsoluteThreshold));
      }
      catch (Exception xe)
      {
        Global.logWriteException("Error parsing configuration XML. This error is unrecoverable.", xe, this);
        throw new ModuleException("Error parsing configuration XML", xe);
      }
    }
  }

  public class ThresholdBrackets
  {
    public decimal BottomPercentThreshold { get; private set; }
    public decimal BottomAbsoluteThreshold { get; private set; }
    public decimal TopPercentThreshold { get; private set; }
    public decimal TopAbsoluteThreshold { get; private set; }

    public ThresholdBrackets(decimal topPercent, decimal topAbsolute, decimal bottomPercent, decimal bottomAbsolute)
    {
      BottomPercentThreshold = bottomPercent;
      BottomAbsoluteThreshold = bottomAbsolute;
      TopPercentThreshold = topPercent;
      TopAbsoluteThreshold = topAbsolute;
      if (!BottomThresholdDefined && !TopThresholdDefined)
        throw new Exception("Either Top or Bottom threshold, or both must be defined.");
    }

    public bool BottomThresholdDefined => BottomPercentThreshold != -1 && BottomAbsoluteThreshold != -1;
    public bool TopThresholdDefined => TopPercentThreshold != -1 && TopAbsoluteThreshold != -1;

    public bool IsInRange(decimal totalSize, decimal availableSpace)
    {
      // return !((!BottomThresholdDefined || availableSpace < EffectiveBottomThreshold(totalSize)) && (TopThresholdDefined || availableSpace > EffectiveTopThreshold(totalSize)));
      if (!TopThresholdDefined)
        return availableSpace > EffectiveBottomThreshold(totalSize);
      if (!BottomThresholdDefined)
        return availableSpace <= EffectiveTopThreshold(totalSize);
      return availableSpace > EffectiveBottomThreshold(totalSize) && availableSpace <= EffectiveTopThreshold(totalSize);
    }

    protected decimal EffectiveBottomThreshold(decimal totalSize)
    {
      if (BottomThresholdDefined)
      {
        decimal BottomPercentThresholdAbsoluteValue = totalSize * (BottomPercentThreshold / 100);
        return BottomPercentThresholdAbsoluteValue > BottomAbsoluteThreshold ? BottomAbsoluteThreshold : BottomPercentThresholdAbsoluteValue;
      }
      else
        return -1;
    }

    protected decimal EffectiveTopThreshold(decimal totalSize)
    {
      if (TopThresholdDefined)
      {
        decimal TopPercentThresholdAbsoluteValue = totalSize * (TopPercentThreshold / 100);
        return TopPercentThresholdAbsoluteValue > TopAbsoluteThreshold ? TopAbsoluteThreshold : TopPercentThresholdAbsoluteValue;
      }
      else
        return -1;
    }
  }
}
