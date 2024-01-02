using UnityEngine;
using System.Collections.Generic;
using System.Drawing;

[CreateAssetMenu(fileName = "ObjectData", menuName = "ScriptableObjects/ObjectData", order = 1)]
public class ObjectData : ScriptableObject
{
    public List<InitiatedObject> objectDataList;
}