using UnityEngine;
using System.Collections.Generic;

[CreateAssetMenu(fileName = "ObjectData", menuName = "ScriptableObjects/ObjectData", order = 1)]
public class ObjectData : ScriptableObject
{
    /// <summary>
    /// Represents a collection of object data.
    /// </summary>
    public List<InitializedObject> objectDataList;

    /// <summary>
    /// Clears the object data list.
    /// </summary>
    public void ClearData()
    {
        objectDataList.Clear();
    }
}