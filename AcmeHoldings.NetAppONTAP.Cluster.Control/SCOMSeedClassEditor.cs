using Microsoft.EnterpriseManagement;
using System;
using System.Collections.Generic;
using System.Dynamic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Collections;
using System.ComponentModel;
using Microsoft.EnterpriseManagement.ConnectorFramework;
using Microsoft.EnterpriseManagement.Configuration;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Microsoft.EnterpriseManagement.Common;
using Microsoft.EnterpriseManagement.Monitoring;

namespace AcmeHoldings.Common.Library
{
  public enum SCOMDiscoveryType { Insert, Update, Delete, Snapshot }
  internal enum SCOMCommitStatus { New, Committed, Modified }

  public class SCOMSeedClassEditor : IList<ExpandoObject>, ICollection<ExpandoObject>, IEnumerable<ExpandoObject>, IList, ICollection, IEnumerable, INotifyCollectionChanged, INotifyPropertyChanged
  {
    protected ObservableCollection<ExpandoObject> seedClassInstances = new ObservableCollection<ExpandoObject>();
    protected ManagementPackClass mySeedClass, myActionPointClass = null;
    ManagementPackRelationship relMAPShouldManageEntity = null, relMAPManageEntity = null;
    protected MonitoringConnector myConnector;
    private List<MonitoringObject> myActionPoints;
    protected ManagementGroup myMG;
    private bool ignorePropertyUpdates = true;
    const string SCOMCommitStatusFieldName = "CommitStatus";
    const string ActionPointFieldName = "ManagingActionPoint";

    public ManagementGroup SCOMManagementGroup { get { return myMG; } }
    public IList<MonitoringObject> ActionPoints { get { return myActionPoints; } }

    #region ProxyImplementation
    IEnumerator<ExpandoObject> IEnumerable<ExpandoObject>.GetEnumerator()
    {
      return ((IEnumerable<ExpandoObject>)seedClassInstances).GetEnumerator();
    }

    public void Add(ExpandoObject item)
    {
      ((ICollection<ExpandoObject>)seedClassInstances).Add(MakeEmptyExpandoObject(item));
    }

    public bool Contains(ExpandoObject item)
    {
      return ((ICollection<ExpandoObject>)seedClassInstances).Contains(item);
    }

    public void CopyTo(ExpandoObject[] array, int arrayIndex)
    {
      ((ICollection<ExpandoObject>)seedClassInstances).CopyTo(array, arrayIndex);
    }

    public bool Remove(ExpandoObject item)
    {
      return ((ICollection<ExpandoObject>)seedClassInstances).Remove(item);
    }

    public int IndexOf(ExpandoObject item)
    {
      return ((IList<ExpandoObject>)seedClassInstances).IndexOf(item);
    }

    public void Insert(int index, ExpandoObject item)
    {
      ((IList<ExpandoObject>)seedClassInstances).Insert(index, MakeEmptyExpandoObject(item));
    }

    public IEnumerator GetEnumerator()
    {
      return ((IEnumerable)seedClassInstances).GetEnumerator();
    }

    public int Add(object value)
    {
      // new row in DataGrid lands here
      int Result = ((IList)seedClassInstances).Add(MakeEmptyExpandoObject((ExpandoObject)value));
      return Result;
    }

    public bool Contains(object value)
    {
      return ((IList)seedClassInstances).Contains(value);
    }

    public void Clear()
    {
      ((IList)seedClassInstances).Clear();
    }

    public int IndexOf(object value)
    {
      return ((IList)seedClassInstances).IndexOf(value);
    }

    public void Insert(int index, object value)
    {
      ((IList)seedClassInstances).Insert(index, MakeEmptyExpandoObject((ExpandoObject)value));
    }

    public void Remove(object value)
    {
      ((IList)seedClassInstances).Remove(value);
    }

    public void RemoveAt(int index)
    {
      ((IList)seedClassInstances).RemoveAt(index);
    }

    public void CopyTo(Array array, int index)
    {
      ((IList)seedClassInstances).CopyTo(array, index);
    }

    public bool IsReadOnly
    {
      get
      {
        return ((IList)seedClassInstances).IsReadOnly;
      }
    }

    public bool IsFixedSize
    {
      get
      {
        return ((IList)seedClassInstances).IsFixedSize;
      }
    }

    public int Count
    {
      get
      {
        return ((IList)seedClassInstances).Count;
      }
    }

    public object SyncRoot
    {
      get
      {
        return ((IList)seedClassInstances).SyncRoot;
      }
    }

    public bool IsSynchronized
    {
      get
      {
        return ((IList)seedClassInstances).IsSynchronized;
      }
    }

    ExpandoObject IList<ExpandoObject>.this[int index]
    {
      get
      {
        return ((IList<ExpandoObject>)seedClassInstances)[index];
      }

      set
      {
        ((IList<ExpandoObject>)seedClassInstances)[index] = value;
      }
    }

    public object this[int index]
    {
      get
      {
        return ((IList)seedClassInstances)[index];
      }

      set
      {
        ((IList)seedClassInstances)[index] = value;
      }
    }

    public event NotifyCollectionChangedEventHandler CollectionChanged;
    public event PropertyChangedEventHandler PropertyChanged;

    #endregion

    public SCOMSeedClassEditor(ManagementGroup managementGroup, string seedClassName, MonitoringConnector insertConnector)
    {
      DefaultConstructor(managementGroup, seedClassName, insertConnector);
    }

    private void DefaultConstructor(ManagementGroup managementGroup, string seedClassName, MonitoringConnector insertConnector)
    {
      // load class meta-data
      ManagementPackClassCriteria findClass = new ManagementPackClassCriteria("Name='" + seedClassName + "'");
      var seedClasses = managementGroup.EntityTypes.GetClasses(findClass);
      if (seedClasses.Count != 1)
        throw new KeyNotFoundException("Cannot find class " + seedClassName + ", none or more than one classes returned.");
      mySeedClass = seedClasses[0];
      myConnector = insertConnector;
      myMG = managementGroup;
      // start monitoring collection change
      seedClassInstances.CollectionChanged += SyncCollectionChangesToSCOM;
      // load initial data
      RefreshFromSCOM();
      // turn on property change notifications
      ignorePropertyUpdates = false;
    }

    public SCOMSeedClassEditor(ManagementGroup managementGroup, string seedClassName, string actionPointClassName, MonitoringConnector insertConnector)
    {
      ManagementPackClassCriteria findClass = new ManagementPackClassCriteria("Name='" + actionPointClassName + "'");
      var actionPointClasses = managementGroup.EntityTypes.GetClasses(findClass);
      if (actionPointClasses.Count != 1)
        throw new KeyNotFoundException("Cannot find class " + actionPointClassName + ", none or more than one classes returned.");
      myActionPointClass = actionPointClasses[0];
      relMAPShouldManageEntity = managementGroup.EntityTypes.GetRelationshipClass(Guid.Parse("cdb09107-2411-d9e2-d718-e574983d304d")); // Microsoft.SystemCenter.ManagementActionPointShouldManageEntity
      relMAPManageEntity = managementGroup.EntityTypes.GetRelationshipClass(Guid.Parse("cb72a458-d56e-3be8-950b-955b16f2f6a2")); // Microsoft.SystemCenter.ManagementActionPointManagesEntity
      myActionPoints = new List<MonitoringObject>();
      myActionPoints.AddRange(managementGroup.EntityObjects.GetObjectReader<MonitoringObject>(myActionPointClass, ObjectQueryOptions.Default));
      DefaultConstructor(managementGroup, seedClassName, insertConnector);
    }

    public void RefreshFromSCOM()
    {
      // stop monitoring collection change
      seedClassInstances.CollectionChanged -= SyncCollectionChangesToSCOM;
      // load existing class instances
      var allSCOMClassInstances = myMG.EntityObjects.GetObjectReader<EnterpriseManagementObject>(mySeedClass, ObjectQueryOptions.Default);
      seedClassInstances.Clear();
      foreach(EnterpriseManagementObject classInstance in allSCOMClassInstances)
      {
        var itemToAdd = MakeEmptyExpandoObject();
        foreach (ManagementPackProperty classProperty in mySeedClass.PropertyCollection)
        {
          dynamic typedValue = Convert.ChangeType(classInstance[classProperty].Value, classProperty.SystemType);
          ((IDictionary<string, object>)itemToAdd)[classProperty.Name] = typedValue;
        }
        ((IDictionary<string, object>)itemToAdd)[SCOMCommitStatusFieldName] = SCOMCommitStatus.Committed; // existing field
        if (myActionPointClass != null)
          ((IDictionary<string, object>)itemToAdd)[ActionPointFieldName] = FindActionPoint(classInstance);
        seedClassInstances.Add(itemToAdd);
      }
      // start monitoring collection change
      seedClassInstances.CollectionChanged += SyncCollectionChangesToSCOM;
    }

    private string NullableToString(object input)
    {
      if (input == null)
        return null;
      else
        return input.ToString();
    }

    private MonitoringObject FindActionPoint(EnterpriseManagementObject servedInstance)
    {
      var allMAPs = servedInstance.ManagementGroup.EntityObjects.GetRelationshipObjectsWhereTarget<MonitoringObject>(servedInstance.Id,
        relMAPManageEntity, DerivedClassTraversalDepth.Recursive, TraversalDepth.OneLevel, ObjectQueryOptions.Default).Where(x => !x.IsDeleted);
      if (allMAPs.Count() == 1)
        return allMAPs.First().SourceObject;
      else if (allMAPs.Count() == 0)
        return null;
      else
        throw new Exception("Unexpected result from GetRelationshipObjectsWhereTarget");
    }

    private void SyncCollectionChangesToSCOM(object sender, NotifyCollectionChangedEventArgs e)
    {
      // proxy event
      CollectionChanged?.Invoke(sender, e);

      switch (e.Action)
      {
        case NotifyCollectionChangedAction.Add:
          foreach (ExpandoObject newItem in e.NewItems)
          {
            // test if all key fields have values
            bool AllKeys = true;
            bool AllRequired = true;
            foreach (var classProperty in mySeedClass.PropertyCollection)
            {
              if (classProperty.Key)
                if (string.IsNullOrEmpty(NullableToString(((IDictionary<string, object>)newItem)[classProperty.Name])))
                  AllKeys = false;
              if (classProperty.Required)
                if (string.IsNullOrEmpty(NullableToString(((IDictionary<string, object>)newItem)[classProperty.Name])))
                  AllRequired = false;
            }
            if (AllKeys & AllRequired)
            {
              PerformSCOMDiscovery(SCOMDiscoveryType.Insert, new List<ExpandoObject>() { newItem });
              ((IDictionary<string, object>)newItem)[SCOMCommitStatusFieldName] = SCOMCommitStatus.Committed;
            }
          }
          break;
        case NotifyCollectionChangedAction.Remove:
          PerformSCOMDiscovery(SCOMDiscoveryType.Delete, e.OldItems);
          break;
        case NotifyCollectionChangedAction.Replace:
          PerformSCOMDiscovery(SCOMDiscoveryType.Update, e.NewItems);
          foreach (ExpandoObject newItem in e.NewItems)
          {
            ((IDictionary<string, object>)newItem)[SCOMCommitStatusFieldName] = SCOMCommitStatus.Committed;
          }
          break;
        case NotifyCollectionChangedAction.Reset:
          break;
      }
    }

    protected virtual void PrepareSCOMDiscoveryData(SCOMDiscoveryType direction, IEnumerable objects, out IncrementalDiscoveryData discoveryDataIncremental,
      out SnapshotDiscoveryData discoveryDataSnapshot)
    {
      IncrementalDiscoveryData IncrementalDD = new IncrementalDiscoveryData();
      SnapshotDiscoveryData SnapshotDD = new SnapshotDiscoveryData();
      // create object for both incremental and full discovery
      foreach (ExpandoObject newObject in objects)
      {
        CreatableEnterpriseManagementObject newSeedInstance = new CreatableEnterpriseManagementObject(myMG, mySeedClass);
        CreatableEnterpriseManagementRelationshipObject newMAPShouldManageInstance = null;
        foreach (var classProperty in mySeedClass.PropertyCollection)
        {
          newSeedInstance[classProperty].Value = ((IDictionary<string, object>)newObject)[classProperty.Name];
        }
        // relationship if configured
        if (myActionPointClass != null && ((IDictionary<string, object>)newObject).ContainsKey(ActionPointFieldName) &&
          (((IDictionary<string, object>)newObject)[ActionPointFieldName]) != null)
        {
          newMAPShouldManageInstance = new CreatableEnterpriseManagementRelationshipObject(myMG, relMAPShouldManageEntity);
          newMAPShouldManageInstance.SetTarget(newSeedInstance);
          newMAPShouldManageInstance.SetSource((MonitoringObject)(((IDictionary<string, object>)newObject)[ActionPointFieldName]));
        }
        switch (direction)
        {
          case SCOMDiscoveryType.Insert:
          case SCOMDiscoveryType.Update:
            IncrementalDD.Add(newSeedInstance);
            if (newMAPShouldManageInstance != null)
              IncrementalDD.Add(newMAPShouldManageInstance);
            if (newMAPShouldManageInstance != null && direction == SCOMDiscoveryType.Update)
              PerformRelationshipCleanup(mySeedClass, newSeedInstance, (MonitoringObject)(((IDictionary<string, object>)newObject)[ActionPointFieldName]));
            break;
          case SCOMDiscoveryType.Delete:
            IncrementalDD.Remove(newSeedInstance);
            if (newMAPShouldManageInstance != null)
              IncrementalDD.Remove(newMAPShouldManageInstance);
            break;
          case SCOMDiscoveryType.Snapshot:
            SnapshotDD.Include(newSeedInstance);
            if (newMAPShouldManageInstance != null)
              SnapshotDD.Include(newMAPShouldManageInstance);
            break;
        }
      }
      discoveryDataIncremental = IncrementalDD;
      discoveryDataSnapshot = SnapshotDD;
    }

    private void PerformRelationshipCleanup(ManagementPackClass mySeedClass, CreatableEnterpriseManagementObject newSeedInstance, MonitoringObject monitoringObject)
    {
      // it's so complex, because the new instance may not exist
      EnterpriseManagementObject realSeedInstance;
      string criteriaString = "";
      foreach (var property in newSeedInstance.GetProperties())
        if (property.Key)
          if (criteriaString == "")
            criteriaString = property.Name + "='" + newSeedInstance[property] + "'";
          else
            criteriaString += " AND " + property.Name + "='" + newSeedInstance[property] + "'";
      EnterpriseManagementObjectCriteria searchQuery = new EnterpriseManagementObjectCriteria(criteriaString, mySeedClass);
      var realSeedInstanceList = myMG.EntityObjects.GetObjectReader<EnterpriseManagementObject>(searchQuery, ObjectQueryOptions.Default);
      if (realSeedInstanceList.Count == 0)
        return;
      else
        realSeedInstance = realSeedInstanceList.First();

      var RemovalDiscovery = new IncrementalDiscoveryData();
      // Management Point
      bool commitOverwrite = false;
      var allMAPRelations = myMG.EntityObjects.GetRelationshipObjectsWhereTarget<EnterpriseManagementObject>(realSeedInstance.Id, relMAPShouldManageEntity, DerivedClassTraversalDepth.Recursive, TraversalDepth.OneLevel, ObjectQueryOptions.Default);
      foreach (var rel in allMAPRelations)
      {
        if (rel.SourceObject.Id != monitoringObject.Id)
        {
          // remove this relationship
          RemovalDiscovery.Remove(rel);
          commitOverwrite = true;
        }
      }
      if (commitOverwrite)
        RemovalDiscovery.Overwrite(myConnector);
    }

    private void PerformSCOMDiscovery(SCOMDiscoveryType direction, IEnumerable objects)
    {
      IncrementalDiscoveryData discoveryDataIncremental;
      SnapshotDiscoveryData discoveryDataSnapshot;
      PrepareSCOMDiscoveryData(direction, objects, out discoveryDataIncremental, out discoveryDataSnapshot);
      switch (direction)
      {
        case SCOMDiscoveryType.Insert:
          discoveryDataIncremental.Commit(myConnector);
          break;
        case SCOMDiscoveryType.Update:
          discoveryDataIncremental.Overwrite(myConnector);
          break;
        case SCOMDiscoveryType.Delete:
          discoveryDataIncremental.Commit(myConnector);
          break;
        case SCOMDiscoveryType.Snapshot:
          discoveryDataSnapshot.Commit(myConnector);
          break;
      }
      // after successful operation set all items to committed state
      foreach (var newObject in objects)
      {
        ((IDictionary<string, object>)newObject)[SCOMCommitStatusFieldName] = SCOMCommitStatus.Committed;
      }
    }

    public void Add(Dictionary<string, object> newInstanceData)
    {
      var itemToAdd = MakeEmptyExpandoObject();
      foreach (var dataItem in newInstanceData)
      {
        dynamic typedValue = Convert.ChangeType(dataItem.Value, mySeedClass.PropertyCollection[dataItem.Key].SystemType);
        ((IDictionary<string, object>)itemToAdd)[dataItem.Key] = typedValue;
      }
      seedClassInstances.Add(itemToAdd);
    }

    private ExpandoObject MakeEmptyExpandoObject()
    {
      ExpandoObject result = new ExpandoObject();
      return MakeEmptyExpandoObject(result);
    }

    private ExpandoObject MakeEmptyExpandoObject(ExpandoObject obj)
    {
      foreach (var classProperty in mySeedClass.PropertyCollection)
      {
        dynamic emptyTypedValue = Convert.ChangeType(classProperty.DefaultValue, classProperty.SystemType);
        ((IDictionary<string, object>)obj).Add(classProperty.Name, emptyTypedValue);
      }
      // special field to trace commit status
      ((IDictionary<string, object>)obj).Add(SCOMCommitStatusFieldName, SCOMCommitStatus.New);
      // must be the last statement to avoid creation notifications
      ((INotifyPropertyChanged)obj).PropertyChanged += SyncPropertiesChangesToSCOM;
      return obj;
    }

    private void SyncPropertiesChangesToSCOM(object sender, PropertyChangedEventArgs e)
    {
      // proxy the event
      PropertyChanged?.Invoke(sender, e);

      if (ignorePropertyUpdates)
        return;
      if (e.PropertyName == SCOMCommitStatusFieldName)
        return;

      // mark existing records as modified if they were committed
      if ((SCOMCommitStatus)(((IDictionary<string, object>)sender)[SCOMCommitStatusFieldName]) == SCOMCommitStatus.Committed)
        ((IDictionary<string, object>)sender)[SCOMCommitStatusFieldName] = SCOMCommitStatus.Modified;

      if (e.PropertyName != ActionPointFieldName)
        if (mySeedClass.PropertyCollection[e.PropertyName].Key && (SCOMCommitStatus)((IDictionary<string, object>)sender)[SCOMCommitStatusFieldName] != SCOMCommitStatus.New)
          throw new KeyNotFoundException("Cannot edit key field.");
      // test if all key fields have values
      bool AllKeys = true;
      bool AllRequired = true;
      foreach(var classProperty in mySeedClass.PropertyCollection)
      {
        if (classProperty.Key)
          if (string.IsNullOrEmpty(NullableToString(((IDictionary<string, object>)sender)[classProperty.Name])))
            AllKeys = false;
        if (classProperty.Required)
          if (string.IsNullOrEmpty(NullableToString(((IDictionary<string, object>)sender)[classProperty.Name])))
            AllRequired = false;
      }
      if (AllKeys & AllRequired)
      {
        if ((SCOMCommitStatus)(((IDictionary<string, object>)sender)[SCOMCommitStatusFieldName]) == SCOMCommitStatus.Modified)
        {
          PerformSCOMDiscovery(SCOMDiscoveryType.Update, new List<ExpandoObject>() { (ExpandoObject)sender });
          ((IDictionary<string, object>)sender)[SCOMCommitStatusFieldName] = SCOMCommitStatus.Committed;
        }
        if ((SCOMCommitStatus)(((IDictionary<string, object>)sender)[SCOMCommitStatusFieldName]) == SCOMCommitStatus.New)
        {
          PerformSCOMDiscovery(SCOMDiscoveryType.Insert, new List<ExpandoObject>() { (ExpandoObject)sender });
          ((IDictionary<string, object>)sender)[SCOMCommitStatusFieldName] = SCOMCommitStatus.Committed;
        }
      }
    }
  }

  public class HostedSCOMSeedClassEditor : SCOMSeedClassEditor
  {
    private string HostNameFieldName;
    private ManagementPackRelationship relHSShouldManageEntity, relMAPShouldManageEntity;
    private ManagementPackClass classHA, classMAP;

    public HostedSCOMSeedClassEditor(ManagementGroup managementGroup, string seedClassName, MonitoringConnector insertConnector, string HostNameField) : base(managementGroup, seedClassName, insertConnector)
    {
      HostNameFieldName = HostNameField;
      relHSShouldManageEntity = managementGroup.EntityTypes.GetRelationshipClass(Guid.Parse("2f71c644-e092-b80a-040b-5c81ba1ec353")); // Microsoft.SystemCenter.HealthServiceShouldManageEntity
      relMAPShouldManageEntity = managementGroup.EntityTypes.GetRelationshipClass(Guid.Parse("cdb09107-2411-d9e2-d718-e574983d304d")); // Microsoft.SystemCenter.ManagementActionPointShouldManageEntity
      classHA = managementGroup.EntityTypes.GetClass(Guid.Parse("ab4c891f-3359-3fb6-0704-075fbfe36710")); // Microsoft.SystemCenter.HealthService
      classMAP = managementGroup.EntityTypes.GetClass(Guid.Parse("414bd649-ccf2-26a7-4171-e20694c802a4")); // Microsoft.SystemCenter.ManagementActionPoint
    }

    protected override void PrepareSCOMDiscoveryData(SCOMDiscoveryType direction, IEnumerable objects, out IncrementalDiscoveryData discoveryDataIncremental, out SnapshotDiscoveryData discoveryDataSnapshot)
    {
      IncrementalDiscoveryData IncrementalDD = new IncrementalDiscoveryData();
      SnapshotDiscoveryData SnapshotDD = new SnapshotDiscoveryData();
      // create object for both incremental and full discovery
      foreach (ExpandoObject newObject in objects)
      {
        CreatableEnterpriseManagementObject newSeedInstance = new CreatableEnterpriseManagementObject(myMG, mySeedClass);
        foreach (var classProperty in mySeedClass.PropertyCollection)
        {
          newSeedInstance[classProperty].Value = ((IDictionary<string, object>)newObject)[classProperty.Name];
        }
        switch (direction)
        {
          case SCOMDiscoveryType.Insert:
          case SCOMDiscoveryType.Update:
            // key field value
            PerformRelationshipCleanup(mySeedClass, newSeedInstance.DisplayName, ((IDictionary<string, object>)newObject)[HostNameFieldName].ToString()); // ((IDictionary<string, object>)newObject)[mySeedClass.PropertyCollection.Where(x => x.Key).First().Name].ToString());
            IncrementalDD.Add(newSeedInstance);
            // instruct SCOM what Agent monitors it
            EnterpriseManagementObject HAInstance, MAPInstance;
            var newHSManage = new CreatableEnterpriseManagementRelationshipObject(SCOMManagementGroup, relHSShouldManageEntity);
            HAInstance = GetSCOMClassInstance(myMG, classHA, ((IDictionary<string, object>)newObject)[HostNameFieldName].ToString());
            newHSManage.SetSource(HAInstance);
            newHSManage.SetTarget(newSeedInstance);
            IncrementalDD.Add(newHSManage);
            var newMAPManage = new CreatableEnterpriseManagementRelationshipObject(SCOMManagementGroup, relMAPShouldManageEntity);
            MAPInstance = GetSCOMClassInstance(myMG, classMAP, ((IDictionary<string, object>)newObject)[HostNameFieldName].ToString());
            newMAPManage.SetSource(MAPInstance);
            newMAPManage.SetTarget(newSeedInstance);
            IncrementalDD.Add(newMAPManage);
            break;
          case SCOMDiscoveryType.Delete:
            IncrementalDD.Remove(newSeedInstance);
            break;
          case SCOMDiscoveryType.Snapshot:
            SnapshotDD.Include(newSeedInstance);
            // instruct SCOM what Agent monitors it
            EnterpriseManagementObject HAInstanceS, MAPInstanceS;
            var newHSManageS = new CreatableEnterpriseManagementRelationshipObject(SCOMManagementGroup, relHSShouldManageEntity);
            HAInstanceS = GetSCOMClassInstance(myMG, classHA, ((IDictionary<string, object>)newObject)[HostNameFieldName].ToString());
            newHSManageS.SetSource(HAInstanceS);
            newHSManageS.SetTarget(newSeedInstance);
            SnapshotDD.Include(newHSManageS);
            var newMAPManageS = new CreatableEnterpriseManagementRelationshipObject(SCOMManagementGroup, relMAPShouldManageEntity);
            MAPInstanceS = GetSCOMClassInstance(myMG, classMAP, ((IDictionary<string, object>)newObject)[HostNameFieldName].ToString());
            newMAPManageS.SetSource(MAPInstanceS);
            newMAPManageS.SetTarget(newSeedInstance);
            SnapshotDD.Include(newMAPManageS);
            break;
        }
      }
      discoveryDataIncremental = IncrementalDD;
      discoveryDataSnapshot = SnapshotDD;
    }

    private void PerformRelationshipCleanup(ManagementPackClass objectClass, string objectName, string newHostName)
    {
      // find the object
      EnterpriseManagementObject theURL;
      try
      {
        theURL = GetSCOMClassInstance(myMG, objectClass, objectName);
      }
      catch (ObjectNotFoundException)
      {
        // it's a new object, nothing to clean
        return;
      }
      var RemovalDiscovery = new IncrementalDiscoveryData();
      // Health Service
      var allHSRelations = myMG.EntityObjects.GetRelationshipObjectsWhereTarget<EnterpriseManagementObject>(theURL.Id, relHSShouldManageEntity, DerivedClassTraversalDepth.None, TraversalDepth.OneLevel, ObjectQueryOptions.Default);
      foreach (var rel in allHSRelations)
      {
        var HAInstance = GetSCOMClassInstance(myMG, classHA, newHostName);
        if (rel.SourceObject.Id != HAInstance.Id)
        {
          // remove this relationship
          RemovalDiscovery.Remove(rel);
        }
      }
      // Management Point
      var allMAPRelations = myMG.EntityObjects.GetRelationshipObjectsWhereTarget<EnterpriseManagementObject>(theURL.Id, relMAPShouldManageEntity, DerivedClassTraversalDepth.None, TraversalDepth.OneLevel, ObjectQueryOptions.Default);
      foreach (var rel in allMAPRelations)
      {
        var MAPInstance = GetSCOMClassInstance(myMG, classMAP, newHostName);
        if (rel.SourceObject.Id != MAPInstance.Id)
        {
          // remove this relationship
          RemovalDiscovery.Remove(rel);
        }
      }
      RemovalDiscovery.Overwrite(myConnector);
    }

    public static EnterpriseManagementObject GetSCOMClassInstance(ManagementGroup mg, ManagementPackClass instanceClass, string ObjectName)
    {
      var searchCriteria = new EnterpriseManagementObjectCriteria("DisplayName='" + ObjectName + "'", instanceClass);
      var Results = mg.EntityObjects.GetObjectReader<EnterpriseManagementObject>(searchCriteria, ObjectQueryOptions.Default);
      if (Results.Count > 0)
        return Results.First();
      else
        throw new ObjectNotFoundException();
    }
  }
}
