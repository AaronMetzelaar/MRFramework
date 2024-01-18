using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// This class is used to demonstrate the usage of the framework.
/// It is necessary to pass an array of the different kinds of objects
/// to the ObjectInitializer, so that they can be assigned a real object.
/// </summary>
public class RGBDemoSimulation : MonoBehaviour
{
    [NonSerialized] private ObjectDetector objectDetector;
    [NonSerialized] private ObjectStateManager objectStateManager;
    private Dictionary<Color, Action<DetectedObject>> objectRoleMappings;
    public List<(string, Color)> objectsToInitialize;
    // Start is called before the first frame update
    void Start()
    {
        if (!TryGetComponent(out objectDetector))
        {
            Debug.LogError("ObjectDetector not found in the scene.");
        }

        if (!TryGetComponent(out objectStateManager))
        {
            Debug.LogError("ObjectStateManager not found in the scene.");
        }

        objectsToInitialize = new List<(string, Color)>
        {
            ("redKnob", Color.red),
            ("greenKnob", Color.green),
            ("blueKnob", Color.blue)
        };
    }

    public void Initialize()
    {
        InitializeObjectRoleMapping();
        List<DetectedObject> detectedObjects = objectDetector.GetDetectedObjects();
        foreach (var detectedObject in detectedObjects)
        {
            detectedObject.onRotationChange.AddListener(OnRotationChange);
        }
    }

    // Update is called once per frame
    void Update()
    {

    }

    private void InitializeObjectRoleMapping()
    {
        Dictionary<Color, Action<DetectedObject>> objectRoleMappings = new()
            {{ Color.red, HandleRedKnob },
            { Color.green, HandleGreenKnob },
            { Color.blue, HandleBlueKnob },
        };
    }

    private void HandleRedKnob(DetectedObject redKnob)
    {
        Debug.Log("Red knob detected");
    }

    private void HandleGreenKnob(DetectedObject greenKnob)
    {
        Debug.Log("Green knob detected");
    }

    private void HandleBlueKnob(DetectedObject blueKnob)
    {
        Debug.Log("Blue knob detected");
    }

    public void OnRotationChange(DetectedObject changedObject)
    {
        Color objectColor = changedObject.initializedObject.Color;
        if (objectRoleMappings.TryGetValue(objectColor, out Action<DetectedObject> action))
        {
            action(changedObject);
        }
        Debug.Log("Rotation changed to " + changedObject.rotationAngle);
    }
}
