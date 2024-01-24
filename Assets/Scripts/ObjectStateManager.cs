using System;
using System.Collections.Generic;
using UnityEngine;

public class ObjectStateManager : MonoBehaviour
{
    private Dictionary<DetectedObject, float> objectRotations = new();
    public Action<DetectedObject> OnRotationChangedCallback;

    /// <summary>
    /// Updates the rotation of a detected object and invokes the rotation changed callback.
    /// </summary>
    /// <param name="changedObject">The detected object whose rotation has changed.</param>
    public void OnRotationChange(DetectedObject changedObject)
    {
        objectRotations[changedObject] = changedObject.rotationAngle;
        OnRotationChangedCallback?.Invoke(changedObject);
    }

    /// <summary>
    /// Registers a detected object and adds it to the object rotation dictionary.
    /// </summary>
    /// <param name="detectedObject">The detected object to register.</param>
    public void RegisterObject(DetectedObject detectedObject)
    {
        detectedObject.onRotationChange.AddListener(OnRotationChange);
        objectRotations[detectedObject] = detectedObject.rotationAngle;
    }

    /// <summary>
    /// Unregisters the specified detected object and removes it from the object rotation dictionary.
    /// </summary>
    /// <param name="detectedObject">The detected object to unregister.</param>
    public void UnregisterObject(DetectedObject detectedObject)
    {
        detectedObject.onRotationChange.RemoveListener(OnRotationChange);
        objectRotations.Remove(detectedObject);
    }
}
