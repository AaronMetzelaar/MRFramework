using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// This file is used as an example of how the framework can be used.
/// It represents a simulation for an RGB color mixing demo.
/// This class handles the detection and manipulation of objects with different colors,
/// and updates the color display object based on the rotation of the detected objects.
/// </summary>
public class RGBDemoSimulation : MonoBehaviour
{
    [NonSerialized]
    private ObjectDetector objectDetector;

    [NonSerialized]
    private ObjectStateManager objectStateManager;

    [Tooltip(
        "The amount of degrees the offset is rotated by to have 0 degrees when the knob is in the middle."
    )]
    [SerializeField]
    private int rotationOffset = 50;
    private Dictionary<Color, Action<DetectedObject>> objectRoleMappings;
    private Dictionary<Color, float> colorValues =
        new()
        {
            { Color.red, 0.0f },
            { Color.green, 0.0f },
            { Color.blue, 0.0f }
        };
    public List<(string, Color, bool)> objectsToInitialize;

    public Color overrideColor = Color.white;

    /// <summary>
    /// Called before the first frame update.
    /// Initializes the object detector, object state manager, and sets up the objects to initialize.
    /// </summary>
    void Start()
    {
        if (!TryGetComponent(out objectDetector))
            Debug.LogError("ObjectDetector not found in the scene.");

        if (!TryGetComponent(out objectStateManager))
            Debug.LogError("ObjectStateManager not found in the scene.");

        if (objectStateManager != null)
            objectStateManager.OnRotationChangedCallback += HandleRotationChange;

        objectsToInitialize = new List<(string, Color, bool)>
        {
            ("redKnob", Color.red, true),
            ("greenKnob", Color.green, true),
            ("blueKnob", Color.blue, true),
            ("colorDisplay", Color.yellow, false)
        };
    }

    /// <summary>
    /// Initializes the RGBDemoSimulation.
    /// </summary>
    public void Initialize()
    {
        InitializeObjectRoleMapping();
        List<DetectedObject> detectedObjects = objectDetector.GetDetectedObjects();

        foreach (var detectedObject in detectedObjects)
            detectedObject.onRotationChange.AddListener(OnRotationChange);
    }

    /// <summary>
    /// Handles the rotation change of a detected object.
    /// </summary>
    /// <param name="changedObject">The detected object that has undergone rotation change.</param>
    private void HandleRotationChange(DetectedObject changedObject)
    {
        Color objectColor = changedObject.initializedObject.Color;
        if (objectRoleMappings.TryGetValue(objectColor, out Action<DetectedObject> action))
            action(changedObject);
    }

    /// <summary>
    /// Initializes the object-role mapping dictionary.
    /// </summary>
    private void InitializeObjectRoleMapping()
    {
        objectRoleMappings = new Dictionary<Color, Action<DetectedObject>>
        {
            { Color.red, HandleRedKnob },
            { Color.green, HandleGreenKnob },
            { Color.blue, HandleBlueKnob },
        };
    }

    /// <summary>
    /// Handles the red knob by updating the color values and triggering an update of the color.
    /// </summary>
    /// <param name="redKnob">The detected red knob object.</param>
    private void HandleRedKnob(DetectedObject redKnob)
    {
        colorValues[Color.red] = NormalizeRotationAngle(redKnob.rotationAngle);
        UpdateColor();
    }

    /// <summary>
    /// Handles the green knob by updating the color values and triggering an update of the color.
    /// </summary>
    /// <param name="greenKnob">The detected green knob object.</param>
    private void HandleGreenKnob(DetectedObject greenKnob)
    {
        colorValues[Color.green] = NormalizeRotationAngle(greenKnob.rotationAngle);
        UpdateColor();
    }

    /// <summary>
    /// Handles the blue knob by updating the color values and triggering an update of the color.
    /// </summary>
    /// <param name="blueKnob">The detected blue knob object.</param>
    private void HandleBlueKnob(DetectedObject blueKnob)
    {
        colorValues[Color.blue] = NormalizeRotationAngle(blueKnob.rotationAngle);
        UpdateColor();
    }

    /// <summary>
    /// Handles the rotation change event for a detected object.
    /// </summary>
    /// <param name="changedObject">The detected object that has undergone rotation change.</param>
    public void OnRotationChange(DetectedObject changedObject)
    {
        Color objectColor = changedObject.initializedObject.Color;
        if (objectRoleMappings.TryGetValue(objectColor, out Action<DetectedObject> action))
            action(changedObject);
    }

    /// <summary>
    /// Normalizes the rotation angle by subtracting the rotation offset and ensuring the angle is in the range [0, 360).
    /// </summary>
    /// <param name="rotationAngle">The rotation angle to be normalized.</param>
    /// <returns>The normalized rotation angle.</returns>
    public float NormalizeRotationAngle(float rotationAngle)
    {
        float normalizedRotationAngle = rotationAngle - rotationOffset;
        if (normalizedRotationAngle < 0.0f)
            normalizedRotationAngle += 360.0f;

        return normalizedRotationAngle;
    }

    /// <summary>
    /// Updates the color of the initialized colorDisplay object based on the color values.
    /// </summary>
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
                        break; // no color display found, so we can't update the color

                    Material mat = colorDisplay.GetComponent<Renderer>().material;
                    mat.SetColor("_Color", new Color(red, green, blue));
                    // Set the initialised object's color to the color of the color display
                    colorDisplay.GetComponent<DetectedObject>().initializedObject.Color = new Color(
                        red,
                        green,
                        blue
                    );
                }
            }
        }
    }
}
