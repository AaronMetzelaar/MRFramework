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
    [Tooltip("The amount of degrees the offset is rotated by to have 0 degrees when the knob is in the middle.")]
    [SerializeField] private int rotationOffset = 50;
    private Dictionary<Color, Action<DetectedObject>> objectRoleMappings;
    private Dictionary<Color, float> colorValues = new()
    {
        { Color.red, 0.0f },
        { Color.green, 0.0f },
        { Color.blue, 0.0f }
    };
    public List<(string, Color, bool)> objectsToInitialize;

    public Color overrideColor = Color.white;

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

        if (objectStateManager != null)
        {
            objectStateManager.OnRotationChangedCallback += HandleRotationChange;
        }

        objectsToInitialize = new List<(string, Color, bool)>
        {
            ("redKnob", Color.red, true),
            ("greenKnob", Color.green, true),
            ("blueKnob", Color.blue, true),
            ("colorDisplay", Color.yellow, false)
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

    private void HandleRotationChange(DetectedObject changedObject)
    {
        Color objectColor = changedObject.initializedObject.Color;
        if (objectRoleMappings.TryGetValue(objectColor, out Action<DetectedObject> action))
        {
            action(changedObject);
        }
    }

    private void InitializeObjectRoleMapping()
    {
        objectRoleMappings = new Dictionary<Color, Action<DetectedObject>>
    {
        { Color.red, HandleRedKnob },
        { Color.green, HandleGreenKnob },
        { Color.blue, HandleBlueKnob },
    };
    }

    private void HandleRedKnob(DetectedObject redKnob)
    {
        colorValues[Color.red] = NormalizeRotationAngle(redKnob.rotationAngle);
        UpdateColor();
    }

    private void HandleGreenKnob(DetectedObject greenKnob)
    {
        colorValues[Color.green] = NormalizeRotationAngle(greenKnob.rotationAngle);
        UpdateColor();
    }

    private void HandleBlueKnob(DetectedObject blueKnob)
    {
        colorValues[Color.blue] = NormalizeRotationAngle(blueKnob.rotationAngle);
        UpdateColor();
    }

    public void OnRotationChange(DetectedObject changedObject)
    {
        Color objectColor = changedObject.initializedObject.Color;
        if (objectRoleMappings.TryGetValue(objectColor, out Action<DetectedObject> action))
        {
            action(changedObject);
        }
    }

    // Subtracts the rotation offset from the rotation angle while keeping the angle in the range [0, 360)
    // Also makes sure that the change is less close to 0 and 360
    public float NormalizeRotationAngle(float rotationAngle)
    {
        float normalizedRotationAngle = rotationAngle - rotationOffset;
        if (normalizedRotationAngle < 0.0f)
        {
            normalizedRotationAngle += 360.0f;
        }
        return normalizedRotationAngle;
    }

    private void UpdateColor()
    {
        float red = colorValues[Color.red] / 360.0f;
        float green = colorValues[Color.green] / 360.0f;
        float blue = colorValues[Color.blue] / 360.0f;

        if (objectsToInitialize != null)
        {
            foreach (var (name, _, _) in objectsToInitialize)
            {
                if (name == "colorDisplay")
                {
                    GameObject colorDisplay = GameObject.Find(name);

                    if (colorDisplay == null)
                    {
                        break; // no color display found, so we can't update the color
                    }

                    Material mat = colorDisplay.GetComponent<Renderer>().material;
                    mat.SetColor("_Color", new Color(red, green, blue));
                    // Set the initialised object's color to the color of the color display
                    colorDisplay.GetComponent<DetectedObject>().initializedObject.Color = new Color(red, green, blue);
                }
            }
        }
    }
}
