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
    private ObjectStateManager objectStateManager;
    [NonSerialized] public WebCamTexture webCamTexture;
    [SerializeField] private RawImage fullImage;
    [SerializeField] private ObjectData objectData;
    [Tooltip("The maximum number of objects of the same type that can be detected.")]
    [Range(1, 10)] public int maxObjectCount = 1;
    [Tooltip("The threshold for the shape matching algorithm. The higher the value, the less sensitive the algorithm is to shape differences.")]
    [Range(0.0f, 1.0f)] public float objectMatchThreshold = 0.2f;
    [Tooltip("The interval in seconds between each object detection.")]
    [Range(0.0f, 1.0f)] public float detectionInterval = 0.1f;
    [Tooltip("The margin in pixels between two detected objects. Increase this value if objects are detected multiple times or recreated too often.")]
    [Range(0.0f, 100.0f)] public float positionMargin = 50f;
    [Tooltip("The margin in pixels between contours to be merged. Increase this value if objects are detected as multiple contours.")]
    [Range(0.0f, 100.0f)] public float contourMergeMargin = 50f;
    [Tooltip("The margin between the hues of the detected object and the initialized object. Increase this value if objects are detected multiple times or recreated too often.")]
    [Range(0.0f, 1.0f)] public float hueMargin = 0.2f;

    private bool isDetecting = false;
    private InitializedObject[] initializedObjects;
    private Dictionary<DetectedValues, GameObject> activeObjects;
    private HashSet<DetectedValues> currentObjectIds;

    /// <summary>
    /// Initializes the object detector by checking for required components and resources.
    /// </summary>
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

    /// <summary>
    /// Stops the object detection process.
    /// </summary>
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
        List<DetectedInitializer> detectedInitializers = FindObjects(transformedImage, undistortedCroppedImage);
        ProcessDetectedObjects(detectedInitializers, transformedImage);

        foreach (var detectedObject in detectedInitializers)
        {
            DetectedValues objectId = GetObjectId(detectedObject);
            currentObjectIds.Add(GetObjectId(detectedObject));
        }

        RemoveAllInactiveObjects();
    }

    /// <summary>
    /// Removes all inactive objects from the activeObjects dictionary.
    /// </summary>
    private void RemoveAllInactiveObjects()
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

    /// <summary>
    /// Retrieves a list of detected objects.
    /// </summary>
    /// <returns>A list of DetectedObject instances representing the detected objects.</returns>
    public List<DetectedObject> GetDetectedObjects()
    {
        if (!isDetecting || activeObjects.Count == 0) return new List<DetectedObject>();
        return activeObjects.Values.Select(go => go.GetComponent<DetectedObject>()).ToList();
    }

    /// <summary>
    /// Finds objects in the given image using contour detection and shape matching.
    /// </summary>
    /// <param name="image">The image to search for objects in.</param>
    /// <param name="undistortedCroppedImage">The undistorted and cropped image (with colors).</param>
    /// <returns>An array of DetectedObject instances representing the detected objects.</returns>
    List<DetectedInitializer> FindObjects(Mat image, Mat undistortedCroppedImage)
    {
        Cv2.FindContours(image, out Point[][] contours, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);
        double smallestContourArea = GetSmallestContourArea(initializedObjects);
        List<DetectedInitializer> detectedInitializers = new();
        Dictionary<InitializedObject, int> objectCounter = new Dictionary<InitializedObject, int>();

        contours = MergeCloseContours(contours);

        foreach (Point[] contour in contours.OrderByDescending(contour => Cv2.ContourArea(contour)))
        {
            if (Cv2.ContourArea(contour) < smallestContourArea * 0.5f)
            {
                break;
            }

            float hue = objectInitializer.GetObjectHue(undistortedCroppedImage, contour);

            if (hue == 0)
            {
                continue;
            }

            foreach (InitializedObject initializedObject in initializedObjects)
            {
                if (!objectCounter.ContainsKey(initializedObject))
                {
                    objectCounter[initializedObject] = 0;
                }

                if (objectCounter[initializedObject] >= maxObjectCount)
                {
                    continue; // Reached maxObjectCount for this initializedObject
                }

                double matchShapeScore = Cv2.MatchShapes(contour, initializedObject.Contour, ShapeMatchModes.I1);

                if (matchShapeScore > objectMatchThreshold)
                    continue;

                if (initializedObject.CheckColor && !AreHuesSimilar(hue, initializedObject.ColorHue) && !AreHuesSimilar(hue, initializedObject.WhiteHue))
                    continue;

                Vector2 centroidInCanvasSpace = objectInitializer.CalculateAndConvertCentroid(contour, image, fullImage.rectTransform);
                Point centroidPoint = new((int)centroidInCanvasSpace.x, (int)centroidInCanvasSpace.y);
                float rotationAngle = objectInitializer.GetRotationAngle(contour);

                DetectedInitializer detectedObject = new()
                {
                    initializedObject = initializedObject,
                    centroidInCanvasSpace = centroidInCanvasSpace,
                    contour = contour,
                    rotationAngle = rotationAngle
                };

                detectedInitializers.Add(detectedObject);

                objectCounter[initializedObject]++;
            }
        }

        return detectedInitializers;
    }

    /// <summary>
    /// Merges close contours together to form a single contour.
    /// </summary>
    /// <param name="contours">The array of contours to merge.</param>
    /// <returns>The merged contours as an array of points.</returns>
    public Point[][] MergeCloseContours(Point[][] contours)
    {
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

    /// <summary>
    /// Processes the detected objects by updating existing game objects or creating new ones.
    /// </summary>
    /// <param name="detectedObjects">The array of detected objects.</param>
    /// <param name="image">The input image.</param>
    public void ProcessDetectedObjects(List<DetectedInitializer> detectedInitializers, Mat image)
    {
        foreach (DetectedInitializer detectedInitializer in detectedInitializers)
        {
            var objectId = GetObjectId(detectedInitializer);

            if (activeObjects.ContainsKey(objectId))
            {
                UpdateGameObject(activeObjects[objectId], detectedInitializer);
            }
            else
            {
                float rotationAngle = objectInitializer.GetRotationAngle(detectedInitializer.contour);
                if (float.IsNaN(rotationAngle))
                {
                    continue;
                }

                Point[] normalizedContour = objectInitializer.NormalizeContour(detectedInitializer.contour, detectedInitializer.centroidInCanvasSpace, image.Width, image.Height, rotationAngle);
                GameObject gameObject = objectInitializer.VisualizeObject(normalizedContour, detectedInitializer.centroidInCanvasSpace, detectedInitializer.rotationAngle, detectedInitializer.initializedObject.Color, detectedInitializer.initializedObject.Name);
                gameObject.GetComponent<DetectedObject>().initializedObject = detectedInitializer.initializedObject;
                gameObject.name = detectedInitializer.initializedObject.Name;
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
    private DetectedValues GetObjectId(DetectedInitializer detectedInitializer)
    {
        int positionHashX = Mathf.RoundToInt(detectedInitializer.centroidInCanvasSpace.x * (1f / positionMargin));
        int positionHashY = Mathf.RoundToInt(detectedInitializer.centroidInCanvasSpace.y * (1f / positionMargin));
        float sizeHash = Mathf.RoundToInt(detectedInitializer.initializedObject.Contour.Length * (1f / positionMargin));
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
    private void UpdateGameObject(GameObject gameObject, DetectedInitializer detectedInitializer)
    {
        // if gameObject is null or centroid is NaN, return
        if (gameObject == null || float.IsNaN(detectedInitializer.centroidInCanvasSpace.x) || float.IsNaN(detectedInitializer.centroidInCanvasSpace.y))
        {
            return;
        }

        gameObject.GetComponent<DetectedObject>().UpdatePosition(detectedInitializer.centroidInCanvasSpace);
        gameObject.GetComponent<DetectedObject>().UpdateRotation(detectedInitializer.rotationAngle);
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
    /// Determines whether two contours are close to each other based on a specified margin.
    /// </summary>
    /// <param name="contour1">The first contour.</param>
    /// <param name="contour2">The second contour.</param>
    /// <returns>True if the contours are close to each other; otherwise, false.</returns>
    private bool AreContoursClose(Point[] contour1, Point[] contour2)
    {
        foreach (Point point1 in contour1)
        {
            foreach (Point point2 in contour2)
            {
                float distance = Mathf.Sqrt(Mathf.Pow(point1.X - point2.X, 2) + Mathf.Pow(point1.Y - point2.Y, 2));
                if (distance < contourMergeMargin)
                {
                    return true;
                }
            }
        }
        return false;
    }

    /// <summary>
    /// Checks if two hues are similar based on a specified margin.
    /// </summary>
    /// <param name="hue1">The first hue value.</param>
    /// <param name="hue2">The second hue value.</param>
    /// <returns>True if the hues are similar, false otherwise.</returns>
    private bool AreHuesSimilar(float hue1, float hue2)
    {
        float hueDifference = Mathf.Abs(hue1 - hue2);
        if (hueDifference > 0.5f)
        {
            hueDifference = 1f - hueDifference;
        }
        return hueDifference < hueMargin;
    }
}