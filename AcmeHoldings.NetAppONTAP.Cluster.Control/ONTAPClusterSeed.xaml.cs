using Microsoft.EnterpriseManagement;
using Microsoft.EnterpriseManagement.Common;
using Microsoft.EnterpriseManagement.ConnectorFramework;
using AcmeHoldings.Common.Library;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.Composition;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace AcmeHoldings.NetAppONTAP.Cluster.Control
{
  /// <summary>
  /// Interaction logic for ONTAPClusterSeed.xaml
  /// </summary>

  [Export]
  [PartCreationPolicy(CreationPolicy.NonShared)]
  public partial class ONTAPClusterSeed : UserControl, INotifyPropertyChanged
  {
    private SCOMSeedClassEditor ClusterSeeds;

    public ONTAPClusterSeed()
    {
      InitializeComponent();
      // Connect to MG
      ManagementGroup mg = new ManagementGroup("localhost");
      // Get connector
      var connectorFramework = mg.ConnectorFramework;
      MonitoringConnector myConnnector;
      Guid connectorID = new Guid("5C36CFDC-9C81-4D27-9845-E581500032A9");
      try
      {
        myConnnector = connectorFramework.GetConnector(connectorID);
        if (!myConnnector.Initialized)
          myConnnector.Initialize();
      }
      catch (ObjectNotFoundException)
      {
        myConnnector = connectorFramework.Setup(new ConnectorInfo()
        {
          Name = "AcmeHoldings Generic Connector",
          DisplayName = "AcmeHoldings Generic Connector",
          Description = "Connector for AcmeHoldings Management Packs",
          DiscoveryDataIsManaged = true
        }, connectorID);
        if (!myConnnector.Initialized)
          myConnnector.Initialize();
      }
      ClusterSeeds = new SCOMSeedClassEditor(mg, "AcmeHoldings.NetAppONTAP.Cluster.Monitoring.Seed.Class", "Microsoft.SystemCenter.ManagementServicePool", myConnnector);

      dgDeploymentSeeds.DataContext = ClusterSeeds;
      dgcbcManagingPool.ItemsSource = ClusterSeeds.ActionPoints;
    }

    public event PropertyChangedEventHandler PropertyChanged;

    protected void OnPropertyChanged(string propertyName)
    {
      PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
  }
}
