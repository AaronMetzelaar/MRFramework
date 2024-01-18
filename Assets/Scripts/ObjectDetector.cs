using System;
using System.Collections;
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
    private ObjectStateManager objectStateManager;
    [NonSerialized] public WebCamTexture webCamTexture;
    [SerializeField] private RawImage fullImage;
    [SerializeField] private ObjectData objectData;
    [Tooltip("The threshold for the shape matching algorithm. The higher the value, the less sensitive the algorithm is to shape differences.")]
    [Range(0.0f, 1.0f)] public float objectMatchThreshold = 0.2f;
    [Tooltip("The interval in seconds between each object detection.")]
    [Range(0.0f, 1.0f)] public float detectionInterval = 0.1f;
    [Tooltip("The margin in pixels between two detected objects. Increase this value if objects are detected multiple times or recreated too often.")]
    [Range(0.0f, 100.0f)] public float positionMargin = 10f;
    [Tooltip("The margin between the hues of the detected object and the initialized object. Increase this value if objects are detected multiple times or recreated too often.")]
    [Range(0.0f, 100.0f)] public float hueMargin = 50f;

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
        if (!TryGetComponent(out objectStateManager))
        {
            Debug.LogError("ObjectStateManager not found in the scene.");
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
        fullImage.gameObject.SetActive(true);
        Mat diffImage = objectInitializer.SubtractImages(OpenCvSharp.Unity.TextureToMat(webCamTexture), calibratorData.BaseImage);
        Mat transformedImage = objectInitializer.TransformImage(diffImage);
        fullImage.texture = OpenCvSharp.Unity.MatToTexture(transformedImage);

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
        // DelayedUpdateObjects();
        StartCoroutine(RemovePreviousFrame());
    }

    private void DelayedUpdateObjects()
    {
        if (!isDetecting) return;

        using Mat image = OpenCvSharp.Unity.TextureToMat(webCamTexture);
        using Mat undistortedCroppedImage = calibrator.GetUndistortedCroppedImage(image, calibratorData.TransformationMatrix, calibratorData.CameraMatrix, calibratorData.DistortionCoefficients);
        using Mat subtractedImage = objectInitializer.SubtractImages(undistortedCroppedImage, calibratorData.BaseImage);
        using Mat transformedImage = objectInitializer.TransformImage(subtractedImage);
        List<DetectedObject> detectedObjects = FindObjects(transformedImage);
        ProcessDetectedObjects(detectedObjects, transformedImage);
    }

    private void RemoveAllActiveObjects()
    {
        foreach (var activeObject in activeObjects.Values)
        {
            if (activeObject != null)
                Destroy(activeObject);
        }
        activeObjects.Clear();
    }

    IEnumerator RemovePreviousFrame()
    {
        RemoveAllActiveObjects();
        yield return new WaitForEndOfFrame();
        DelayedUpdateObjects();
    }

    public List<DetectedObject> GetDetectedObjects()
    {
        return activeObjects.Values.Select(go => go.GetComponent<DetectedObject>()).ToList();
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
            if (Cv2.ContourArea(contour) < smallestContourArea * 0.2)
            {
                break; // No more objects to detect
            }

            float hue = objectInitializer.GetObjectHue(image, contour);

            foreach (InitializedObject initializedObject in initializedObjects)
            {
                double matchShapeScore = Cv2.MatchShapes(contour, initializedObject.Contour, ShapeMatchModes.I2);
                if (matchShapeScore > objectMatchThreshold || !AreHuesSimilar(hue, initializedObject.ColorHue))
                    continue;

                Vector2 centroidInCanvasSpace = objectInitializer.CalculateAndConvertCentroid(contour, image, fullImage.rectTransform);
                Point centroidPoint = new((int)centroidInCanvasSpace.x, (int)centroidInCanvasSpace.y);
                float rotationAngle = GetRotationAngle(contour);

                DetectedObject detectedObject = gameObject.AddComponent<DetectedObject>();
                detectedObject.initializedObject = initializedObject;
                detectedObject.centroidInCanvasSpace = centroidInCanvasSpace;
                detectedObject.contour = contour;
                detectedObject.rotationAngle = rotationAngle;
                detectedObjects.Add(detectedObject);
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

            if (activeObjects.ContainsKey(objectId))
            {
                UpdateGameObject(activeObjects[objectId], detectedObject);
            }
            else
            {
                Point[] normalizedContour = objectInitializer.NormalizeContour(detectedObject.contour, detectedObject.centroidInCanvasSpace, image.Width, image.Height);
                GameObject gameObject = objectInitializer.VisualizeObject(normalizedContour, image, detectedObject.centroidInCanvasSpace, detectedObject.initializedObject.Color, true);
                activeObjects.Add(objectId, gameObject);
                objectStateManager.RegisterObject(gameObject.GetComponent<DetectedObject>());
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
        int positionHashX = Mathf.RoundToInt(detectedObject.centroidInCanvasSpace.x * (1f / positionMargin));
        int positionHashY = Mathf.RoundToInt(detectedObject.centroidInCanvasSpace.y * (1f / positionMargin));
        float sizeHash = Mathf.RoundToInt(detectedObject.initializedObject.Contour.Length * (1f / positionMargin));
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
            Debug.Log($"Removing inactive object with id {inactiveObjectId}");
            Destroy(activeObjects[inactiveObjectId]);
            activeObjects.Remove(inactiveObjectId);
            objectStateManager.UnregisterObject(activeObjects[inactiveObjectId].GetComponent<DetectedObject>());
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

    /// <summary>
    /// Calculates the rotation angle of the given contour. The angle can be between 0 and 360 degrees.
    /// </summary>
    /// <param name="contour">The contour to calculate the rotation angle for.</param>
    /// <returns>The rotation angle of the contour.</returns>

    float GetRotationAngle(Point[] contour)
    {
        if (contour == null || contour.Length == 0)
        {
            return -1;
        }

        Moments moments = Cv2.Moments(contour);
        Point2f centroid = new((float)(moments.M10 / moments.M00), (float)(moments.M01 / moments.M00));

        Point2f furthestPoint = contour.OrderByDescending(point => Math.Sqrt(Math.Pow(point.X - centroid.X, 2) + Math.Pow(point.Y - centroid.Y, 2))).First();

        float rotationAngle = Mathf.Atan2(furthestPoint.Y - centroid.Y, furthestPoint.X - centroid.X) * Mathf.Rad2Deg;

        // Normalize the angle to be between 0 and 360 degrees
        rotationAngle = (rotationAngle + 360) % 360;

        Debug.Log($"Rotation angle: {rotationAngle}");

        return rotationAngle;
    }

    bool AreHuesSimilar(float hue1, float hue2)
    {
        float hueDifference = Mathf.Abs(hue1 - hue2);
        return hueDifference < hueMargin;
    }
}