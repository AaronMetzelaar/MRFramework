using System;
using System.Collections.Generic;
using System.Linq;
using OpenCvSharp;
using UnityEngine;
using UnityEngine.UI;

public class ObjectDetector : MonoBehaviour
{
    private readonly Calibrator calibrator;
    private readonly CalibratorData calibratorData;
    private ObjectInitiator objectInitiator;
    private ObjectDetector objectDetector;
    [NonSerialized] public WebCamTexture webCamTexture;
    [SerializeField] private RawImage fullImage;
    [SerializeField] private ObjectData objectData;

    [Range(0.0f, 1.0f)] public float objectMatchThreshold = 0.9f;

    private bool isDetecting = false;
    private InitiatedObject[] initiatedObjects;
    private Dictionary<int, GameObject> activeObjects;
    private int objectIdCount = 0;

    public void Start()
    {
        if (!TryGetComponent(out objectInitiator))
        {
            Debug.LogError("ObjectInitiator not found in the scene.");
        }
        initiatedObjects = objectData.objectDataList.ToArray();
        activeObjects = new Dictionary<int, GameObject>();

        // Start invoking the UpdateObjects method every second
        InvokeRepeating(nameof(UpdateObjects), 1f, 1f);

    }

    private void UpdateObjects()
    {
        if (isDetecting)
        {
            Mat image = OpenCvSharp.Unity.TextureToMat(webCamTexture);
            Mat croppedImage = calibrator.CropImage(image, calibratorData.Corners);

            DetectedObject[] detectedObjects = FindObjects(croppedImage);
            ProcessDetectedObjects(detectedObjects, croppedImage);
        }
    }

    public void StartDetecting()
    {
        isDetecting = !isDetecting;
    }

    // Image should already be cropped, thresholded etc before calling this method
    // so that we only need to do this once
    DetectedObject[] FindObjects(Mat image)
    {
        Cv2.FindContours(image, out Point[][] contours, out HierarchyIndex[] hierarchyIndexes, RetrievalModes.Tree, ContourApproximationModes.ApproxSimple);
        double smallestContourArea = GetSmallestContourArea(initiatedObjects);

        DetectedObject[] detectedObjects = new DetectedObject[initiatedObjects.Length];

        foreach (Point[] contour in contours.OrderByDescending(contour => Cv2.ContourArea(contour)))
        {
            if (Cv2.ContourArea(contour) < smallestContourArea * 0.9)
            {
                break; // No more objects to detect
            }

            Vector2 centroidInCanvasSpace = objectInitiator.CalculateAndConvertCentroid(contour, image, fullImage.rectTransform);
            Point centroidPoint = new((int)centroidInCanvasSpace.x, (int)centroidInCanvasSpace.y);
            RotatedRect minAreaRect = Cv2.MinAreaRect(contour);
            float rotationAngle = minAreaRect.Angle;
            Point[] normalizedContour = objectInitiator.NormalizeContour(contour, centroidPoint, rotationAngle);
            float hue = objectInitiator.GetObjectHue(image, contour);

            foreach (InitiatedObject initiatedObject in initiatedObjects)
            {
                double matchShapeScore = Cv2.MatchShapes(normalizedContour, initiatedObject.Contour, ShapeMatchModes.I1);
                if (matchShapeScore > objectMatchThreshold)
                {
                    detectedObjects[Array.IndexOf(initiatedObjects, initiatedObject)] = gameObject.AddComponent<DetectedObject>();
                    detectedObjects[Array.IndexOf(initiatedObjects, initiatedObject)].initiatedObject = initiatedObject;
                    detectedObjects[Array.IndexOf(initiatedObjects, initiatedObject)].centroidInCanvasSpace = centroidInCanvasSpace;
                    detectedObjects[Array.IndexOf(initiatedObjects, initiatedObject)].rotationAngle = rotationAngle;
                }
            }
        }

        return detectedObjects;
    }

    public void ProcessDetectedObjects(DetectedObject[] detectedObjects, Mat image)
    {
        foreach (DetectedObject detectedObject in detectedObjects)
        {
            if (detectedObject == null)
            {
                continue;
            }
            int objectId = GetObjectId(detectedObject);

            if (activeObjects.ContainsKey(objectId))
            {
                UpdateGameObject(activeObjects[objectId], detectedObject);
            }
            else
            {
                GameObject gameObject = objectInitiator.VisualizeObject(detectedObject.initiatedObject.Contour, image, detectedObject.centroidInCanvasSpace, detectedObject.rotationAngle);
                activeObjects.Add(objectId, gameObject);
            }
        }

        RemoveInactiveObjects();
    }

    private int GetObjectId(DetectedObject detectedObject)
    {
        int positionHash = detectedObject.centroidInCanvasSpace.GetHashCode();
        int sizeHash = detectedObject.initiatedObject.Contour.Length.GetHashCode();

        return positionHash + sizeHash;
    }

    private void UpdateGameObject(GameObject gameObject, DetectedObject detectedObject)
    {
        gameObject.GetComponent<DetectedObject>().UpdatePosition(detectedObject.centroidInCanvasSpace);
        gameObject.GetComponent<DetectedObject>().UpdateRotation(detectedObject.rotationAngle);
    }

    private void RemoveInactiveObjects()
    {
        List<int> inactiveObjectIds = new List<int>();
        foreach (KeyValuePair<int, GameObject> activeObject in activeObjects)
        {
            if (!activeObject.Value.activeSelf)
            {
                inactiveObjectIds.Add(activeObject.Key);
            }
        }

        foreach (int inactiveObjectId in inactiveObjectIds)
        {
            activeObjects.Remove(inactiveObjectId);
        }
    }

    /// <summary>
    /// Calculates and returns the smallest contour area from the given array of initiated objects.
    /// </summary>
    /// <param name="initiatedObjects">The array of initiated objects.</param>
    /// <returns>The smallest contour area.</returns>
    double GetSmallestContourArea(InitiatedObject[] initiatedObjects)
    {
        double smallestArea = float.MaxValue;
        foreach (InitiatedObject initiatedObject in initiatedObjects)
        {
            double area = Cv2.ContourArea(initiatedObject.Contour);
            if (area < smallestArea)
            {
                smallestArea = area;
            }
        }

        return smallestArea;
    }
}