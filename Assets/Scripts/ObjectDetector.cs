using System;
using System.Collections.Generic;
using System.Linq;
using OpenCvSharp;
using UnityEngine;
using UnityEngine.UI;

public class ObjectDetector : MonoBehaviour
{
    private Calibrator calibrator;
    private CalibratorData calibratorData;
    private ObjectInitializer objectInitializer;
    [NonSerialized] public WebCamTexture webCamTexture;
    [SerializeField] private RawImage fullImage;
    [SerializeField] private ObjectData objectData;
    [Tooltip("The threshold for the shape matching algorithm. The higher the value, the less sensitive the algorithm is to shape differences.")]
    [Range(0.0f, 1.0f)] public float objectMatchThreshold = 0.2f;
    [Tooltip("The interval in seconds between each object detection.")]
    [Range(0.0f, 1.0f)] public float detectionInterval = 0.1f;
    [Tooltip("The margin in pixels between two detected objects. Increase this value if objects are detected multiple times or recreated too often.")]
    [Range(0.0f, 100.0f)] public float positionMargin = 10f;

    private bool isDetecting = false;
    private InitializedObject[] initializedObjects;
    private Dictionary<DetectedValues, GameObject> activeObjects;
    private int objectIdCount = 0;

    public void Initialize()
    {
        if (!TryGetComponent(out calibrator))
        {
            Debug.LogError("Calibrator not found.");
        }

        calibratorData = calibrator.CurrentCalibratorData ?? throw new Exception("Calibrator data not found. Please calibrate first.");

        if (!TryGetComponent(out objectInitializer))
        {
            Debug.LogError("ObjectInitializer not found in the scene.");
        }
        if (webCamTexture == null)
        {
            Debug.LogError("Webcam texture not found.");
        }
    }

    /// <summary>
    /// Starts the object detection process.
    /// </summary>
    public void StartDetecting()
    {
        isDetecting = true;
        initializedObjects = objectData.objectDataList.ToArray();
        activeObjects = new Dictionary<DetectedValues, GameObject>();

        if (calibrator.CurrentCalibratorData == null)
        {
            Debug.LogError("Calibrator data is not available. Please calibrate first.");
            return;
        }

        InvokeRepeating(nameof(UpdateObjects), 0f, detectionInterval);
    }

    public void StopDetecting()
    {
        isDetecting = false;
        CancelInvoke(nameof(UpdateObjects));

        foreach (var activeObject in activeObjects.Values)
        {
            if (activeObject != null)
                Destroy(activeObject);
        }
        activeObjects.Clear();
    }

    /// <summary>
    /// Updates the detected objects based on the current frame.
    /// </summary>
    private void UpdateObjects()
    {
        if (!isDetecting) return;

        using Mat image = OpenCvSharp.Unity.TextureToMat(webCamTexture);
        using Mat croppedImage = calibrator.CropImage(image, calibratorData.Corners);
        using Mat transformedImage = objectInitializer.TransformImage(croppedImage);
        fullImage.texture = OpenCvSharp.Unity.MatToTexture(transformedImage);
        List<DetectedObject> detectedObjects = FindObjects(transformedImage);
        ProcessDetectedObjects(detectedObjects, transformedImage);
    }


    /// <summary>
    /// Finds objects in the given image using contour detection and shape matching.
    /// </summary>
    /// <param name="image">The image to search for objects in.</param>
    /// <returns>An array of DetectedObject instances representing the detected objects.</returns>
    List<DetectedObject> FindObjects(Mat image)
    {
        Cv2.FindContours(image, out Point[][] contours, out HierarchyIndex[] hierarchyIndexes, RetrievalModes.Tree, ContourApproximationModes.ApproxSimple);
        double smallestContourArea = GetSmallestContourArea(initializedObjects);
        List<DetectedObject> detectedObjects = new();

        foreach (Point[] contour in contours.OrderByDescending(contour => Cv2.ContourArea(contour)))
        {
            if (Cv2.ContourArea(contour) < smallestContourArea * 0.5)
            {
                break; // No more objects to detect
            }

            float hue = objectInitializer.GetObjectHue(image, contour, image);

            foreach (InitializedObject initializedObject in initializedObjects)
            {
                double matchShapeScore = Cv2.MatchShapes(contour, initializedObject.Contour, ShapeMatchModes.I1);
                Debug.Log("Match score: " + matchShapeScore);
                if (matchShapeScore < objectMatchThreshold)
                {
                    Vector2 centroidInCanvasSpace = objectInitializer.CalculateAndConvertCentroid(contour, image, fullImage.rectTransform);
                    Point centroidPoint = new((int)centroidInCanvasSpace.x, (int)centroidInCanvasSpace.y);
                    RotatedRect minAreaRect = Cv2.MinAreaRect(contour);
                    float rotationAngle = minAreaRect.Angle;

                    DetectedObject detectedObject = gameObject.AddComponent<DetectedObject>();
                    detectedObject.initializedObject = initializedObject;
                    detectedObject.centroidInCanvasSpace = centroidInCanvasSpace;
                    detectedObject.rotationAngle = rotationAngle;
                    detectedObjects.Add(detectedObject);
                    Debug.Log("Object detected! Centroid: " + centroidInCanvasSpace.x + ", " + centroidInCanvasSpace.y + " Rotation angle: " + rotationAngle + " Hue: " + hue + " Match score: " + matchShapeScore);
                }
            }
        }

        return detectedObjects;
    }

    /// <summary>
    /// Processes the detected objects by updating existing game objects or creating new ones.
    /// </summary>
    /// <param name="detectedObjects">The array of detected objects.</param>
    /// <param name="image">The input image.</param>
    public void ProcessDetectedObjects(List<DetectedObject> detectedObjects, Mat image)
    {
        foreach (DetectedObject detectedObject in detectedObjects)
        {
            if (detectedObject == null)
            {
                continue;
            }
            var objectId = GetObjectId(detectedObject);
            Debug.Log("Object ID: " + objectId);

            if (activeObjects.ContainsKey(objectId))
            {
                // UpdateGameObject(activeObjects[objectId], detectedObject);
            }
            else
            {
                GameObject gameObject = objectInitializer.VisualizeObject(detectedObject.initializedObject.Contour, image, detectedObject.centroidInCanvasSpace, detectedObject.rotationAngle, detectedObject.initializedObject.Color);
                activeObjects.Add(objectId, gameObject);
            }
        }

        RemoveInactiveObjects();
    }

    /// <summary>
    /// Calculates the unique identifier for a detected object based on its position and size.
    /// </summary>
    /// <param name="detectedObject">The detected object.</param>
    /// <returns>The unique identifier for the detected object.</returns>
    private DetectedValues GetObjectId(DetectedObject detectedObject)
    {
        int positionHashX = Mathf.RoundToInt(detectedObject.centroidInCanvasSpace.x * (1f / (positionMargin)));
        int positionHashY = Mathf.RoundToInt(detectedObject.centroidInCanvasSpace.y * (1f / (positionMargin)));
        float sizeHash = Mathf.RoundToInt(detectedObject.initializedObject.Contour.Length * (1f / (positionMargin)));
        Debug.Log("Position hashX: " + positionHashX + " Position hashY: " + positionHashY + " Size hash: " + sizeHash);
        return new DetectedValues { centroidInCanvasSpace = new Vector2(positionHashX, positionHashY), sizeHash = sizeHash };
    }

    struct DetectedValues
    {
        public Vector2 centroidInCanvasSpace;
        public float sizeHash;
    }

    /// <summary>
    /// Updates the position and rotation of a game object based on the detected object information.
    /// </summary>
    /// <param name="gameObject">The game object to update.</param>
    /// <param name="detectedObject">The detected object information.</param>
    private void UpdateGameObject(GameObject gameObject, DetectedObject detectedObject)
    {
        gameObject.GetComponent<DetectedObject>().UpdatePosition(detectedObject.centroidInCanvasSpace);
        gameObject.GetComponent<DetectedObject>().UpdateRotation(detectedObject.rotationAngle);
    }

    /// <summary>
    /// Removes inactive objects from the activeObjects dictionary.
    /// </summary>
    private void RemoveInactiveObjects()
    {
        List<DetectedValues> inactiveObjectIds = new();
        foreach (KeyValuePair<DetectedValues, GameObject> activeObject in activeObjects)
        {
            if (!activeObject.Value.activeSelf)
            {
                inactiveObjectIds.Add(activeObject.Key);
            }
        }

        foreach (var inactiveObjectId in inactiveObjectIds)
        {
            activeObjects.Remove(inactiveObjectId);
        }
    }

    /// <summary>
    /// Calculates and returns the smallest contour area from the given array of initiated objects.
    /// </summary>
    /// <param name="initializedObjects">The array of initiated objects.</param>
    /// <returns>The smallest contour area.</returns>
    double GetSmallestContourArea(InitializedObject[] initializedObjects)
    {
        double smallestArea = float.MaxValue;
        foreach (InitializedObject initializedObject in initializedObjects)
        {
            double area = Cv2.ContourArea(initializedObject.Contour);
            if (area < smallestArea)
            {
                smallestArea = area;
            }
        }

        return smallestArea;
    }
}