using System;
using System.Collections.Generic;
using UnityEngine;

public class ObjectStateManager : MonoBehaviour
{
    private Dictionary<DetectedObject, float> objectRotations = new();
    public Action<DetectedObject> OnRotationChangedCallback;

    public void OnRotationChange(DetectedObject changedObject)
    {
        objectRotations[changedObject] = changedObject.rotationAngle;
        OnRotationChangedCallback?.Invoke(changedObject);
    }
    public void RegisterObject(DetectedObject detectedObject)
    {
        detectedObject.onRotationChange.AddListener(OnRotationChange);
        objectRotations[detectedObject] = detectedObject.rotationAngle;
    }

    public void UnregisterObject(DetectedObject detectedObject)
    {
        detectedObject.onRotationChange.RemoveListener(OnRotationChange);
        objectRotations.Remove(detectedObject);
    }

    // public float GetRotation(DetectedObject detectedObject)
    // {
    //     return objectRotations.TryGetValue(detectedObject, out float rotation) ? rotation : 0.0f;
    // }
}