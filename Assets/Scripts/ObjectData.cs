using UnityEngine;
using System.Collections.Generic;

[CreateAssetMenu(fileName = "ObjectData", menuName = "ScriptableObjects/ObjectData", order = 1)]
public class ObjectData : ScriptableObject
{
    public List<InitiatedObject> objectDataList;

    public void ClearData()
    {
        objectDataList.Clear();
    }

}