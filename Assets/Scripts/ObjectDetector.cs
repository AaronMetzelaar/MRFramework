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
    [Range(0.0f, 100.0f)] public float positionMargin = 50f;
    [Tooltip("The margin in pixels between contours to be merged. Increase this value if objects are detected as multiple contours.")]
    [Range(0.0f, 100.0f)] public float contourMargin = 50f;
    [Tooltip("The margin between the hues of the detected object and the initialized object. Increase this value if objects are detected multiple times or recreated too often.")]
    [Range(0.0f, 1.0f)] public float hueMargin = 0.2f;

    private bool isDetecting = false;
    private InitializedObject[] initializedObjects;
    private Dictionary<DetectedValues, GameObject> activeObjects;
    private HashSet<DetectedValues> currentObjectIds;

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
        if (!isDetecting) return;

        currentObjectIds = new HashSet<DetectedValues>();

        using Mat image = OpenCvSharp.Unity.TextureToMat(webCamTexture);
        using Mat undistortedCroppedImage = calibrator.GetUndistortedCroppedImage(image, calibratorData.TransformationMatrix, calibratorData.CameraMatrix, calibratorData.DistortionCoefficients);
        using Mat subtractedImage = objectInitializer.SubtractImages(undistortedCroppedImage, calibratorData.BaseImage);
        using Mat transformedImage = objectInitializer.TransformImage(subtractedImage);
        List<DetectedObject> detectedObjects = FindObjects(transformedImage, undistortedCroppedImage);
        ProcessDetectedObjects(detectedObjects, transformedImage);

        foreach (var detectedObject in detectedObjects)
        {
            DetectedValues objectId = GetObjectId(detectedObject);
            currentObjectIds.Add(GetObjectId(detectedObject));
        }

        RemoveAllActiveObjects();
    }

    private void RemoveAllActiveObjects()
    {
        List<DetectedValues> inactiveObjectIds = new();

        foreach (var activeObject in activeObjects)
        {
            if (!currentObjectIds.Contains(activeObject.Key))
            {
                inactiveObjectIds.Add(activeObject.Key);
            }
        }

        foreach (var inactiveObjectId in inactiveObjectIds)
        {
            GameObject inactiveObject = activeObjects[inactiveObjectId];
            if (inactiveObject != null)
            {
                objectStateManager.UnregisterObject(inactiveObject.GetComponent<DetectedObject>());
                Destroy(inactiveObject);
            }
            activeObjects.Remove(inactiveObjectId);
        }
    }

    public List<DetectedObject> GetDetectedObjects()
    {
        if (!isDetecting || activeObjects.Count == 0) return new List<DetectedObject>();
        return activeObjects.Values.Select(go => go.GetComponent<DetectedObject>()).ToList();
    }

    /// <summary>
    /// Finds objects in the given image using contour detection and shape matching.
    /// </summary>
    /// <param name="image">The image to search for objects in.</param>
    /// <returns>An array of DetectedObject instances representing the detected objects.</returns>
    List<DetectedObject> FindObjects(Mat image, Mat undistortedCroppedImage)
    {
        Cv2.FindContours(image, out Point[][] contours, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);
        double smallestContourArea = GetSmallestContourArea(initializedObjects);
        List<DetectedObject> detectedObjects = new();

        contours = MergeCloseContours(contours);

        foreach (Point[] contour in contours.OrderByDescending(contour => Cv2.ContourArea(contour)))
        {
            if (Cv2.ContourArea(contour) < smallestContourArea * 0.2)
            {
                break; // No more objects to detect
            }

            float hue = objectInitializer.GetObjectHue(undistortedCroppedImage, contour);

            if (hue == 0)
            {
                continue;
            }

            foreach (InitializedObject initializedObject in initializedObjects)
            {
                double matchShapeScore = Cv2.MatchShapes(contour, initializedObject.Contour, ShapeMatchModes.I1);

                if (matchShapeScore > objectMatchThreshold || !AreHuesSimilar(hue, initializedObject.ColorHue))
                    continue;

                Vector2 centroidInCanvasSpace = objectInitializer.CalculateAndConvertCentroid(contour, image, fullImage.rectTransform);
                Point centroidPoint = new((int)centroidInCanvasSpace.x, (int)centroidInCanvasSpace.y);
                float rotationAngle = objectInitializer.GetRotationAngle(contour);

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

    public Point[][] MergeCloseContours(Point[][] contours)
    {
        // Find contours that are close to each other, do convex hull on them to merge them
        List<Point[]> mergedContours = new();
        bool[] merged = new bool[contours.Length];

        for (int i = 0; i < contours.Length; i++)
        {
            if (merged[i]) continue;

            List<Point> currentContour = new(contours[i]);

            for (int j = 0; j < contours.Length; j++)
            {
                if (i == j || merged[j]) continue;

                if (AreContoursClose(contours[i], contours[j]))
                {
                    currentContour.AddRange(contours[j]);
                    merged[j] = true;
                }
            }

            Point[] mergedContour = Cv2.ConvexHull(currentContour.ToArray());
            mergedContours.Add(mergedContour);
            merged[i] = true;
        }

        return mergedContours.ToArray();
    }

    private bool AreContoursClose(Point[] contour1, Point[] contour2)
    {
        foreach (Point point1 in contour1)
        {
            foreach (Point point2 in contour2)
            {
                float distance = Mathf.Sqrt(Mathf.Pow(point1.X - point2.X, 2) + Mathf.Pow(point1.Y - point2.Y, 2));
                if (distance < contourMargin)
                {
                    return true;
                }
            }
        }
        return false;
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
                float rotationAngle = objectInitializer.GetRotationAngle(detectedObject.contour);
                Point[] normalizedContour = objectInitializer.NormalizeContour(detectedObject.contour, detectedObject.centroidInCanvasSpace, image.Width, image.Height, rotationAngle);
                GameObject gameObject = objectInitializer.VisualizeObject(normalizedContour, image, detectedObject.centroidInCanvasSpace, detectedObject.rotationAngle, detectedObject.initializedObject.Color);
                gameObject.name = detectedObject.initializedObject.Name;
                activeObjects.Add(objectId, gameObject);
                objectStateManager.RegisterObject(gameObject.GetComponent<DetectedObject>());
            }
        }
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
    // private void RemoveInactiveObjects()
    // {
    //     List<DetectedValues> inactiveObjectIds = new();

    //     foreach (KeyValuePair<DetectedValues, GameObject> activeObject in activeObjects)
    //     {
    //         if (!activeObject.Value.activeSelf)
    //         {
    //             inactiveObjectIds.Add(activeObject.Key);
    //         }
    //     }

    //     foreach (var inactiveObjectId in inactiveObjectIds)
    //     {
    //         GameObject inactiveObject = activeObjects[inactiveObjectId];
    //         if (inactiveObject != null)
    //         {
    //             Debug.Log($"Removing inactive object with id {inactiveObjectId}");
    //             objectStateManager.UnregisterObject(inactiveObject.GetComponent<DetectedObject>());
    //             Destroy(inactiveObject);
    //         }
    //         activeObjects.Remove(inactiveObjectId);
    //     }
    // }

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

    bool AreHuesSimilar(float hue1, float hue2)
    {
        float hueDifference = Mathf.Abs(hue1 - hue2);
        return hueDifference < hueMargin;
    }
}