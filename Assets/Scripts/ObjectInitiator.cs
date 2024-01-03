using UnityEngine;
using OpenCvSharp;
using System.Linq;
using System;
using UnityEngine.UI;
using System.Collections.Generic;
using System.Collections;
using TMPro;
public class ObjectInitiator : MonoBehaviour
{
    private Calibrator calibrator;
    private CalibratorData calibratorData;
    [NonSerialized] public WebCamTexture webCamTexture;
    [NonSerialized] public GameObject currentVisualizedObject;
    [SerializeField] public RawImage fullImage;
    [SerializeField] private Transform canvasPos;
    [SerializeField] private GameObject prefabMaterialEmpty;
    [SerializeField] private ObjectData objectData;
    [SerializeField] private TextMeshProUGUI instructionText;

    InitiatedObject initiatedObject = null;
    Mat differenceImage;
    Mat grayImage;
    Mat bilateralFilterImage;
    Mat cannyImage;
    Mat kernel;

    /// <summary>
    /// Initializes the object.
    /// </summary>
    public void Initialize()
    {

        if (!TryGetComponent(out calibrator))
        {
            Debug.LogError("Calibrator not found.");
        }

        calibratorData = calibrator.CurrentCalibratorData ?? throw new Exception("Calibrator data not found. Please calibrate first.");

        if (webCamTexture == null)
        {
            Debug.LogError("Webcam texture not found.");
        }
    }

    public IEnumerator DelayedInitiate()
    {
        instructionText.gameObject.SetActive(false);
        yield return new WaitForSeconds(0.2f);
        CaptureAndInitiateObject();
        yield return new WaitForSeconds(0.2f);
        instructionText.gameObject.SetActive(true);
    }

    public void Reinitiate()
    {
        if (currentVisualizedObject != null)
        {
            Destroy(currentVisualizedObject);
        }
        if (initiatedObject != null)
        {
            initiatedObject.Contour = null;
            initiatedObject.Hue = 0f;
        }

        fullImage.texture = null;
        differenceImage?.Dispose();
        grayImage?.Dispose();
        bilateralFilterImage?.Dispose();
        cannyImage?.Dispose();
        kernel?.Dispose();

        StartCoroutine(DelayedInitiate());
    }

    /// <summary>
    /// Captures an image, initiates an object, and visualizes it.
    /// </summary>
    public void CaptureAndInitiateObject()
    {
        if (calibrator.CurrentCalibratorData == null)
        {
            Debug.LogError("Calibrator data is not available. Please calibrate first.");
            return;
        }

        if
            (fullImage.gameObject.activeSelf == false) fullImage.gameObject.SetActive(true);

        Mat image = OpenCvSharp.Unity.TextureToMat(webCamTexture);
        Mat croppedImage = calibrator.CropImage(image, calibratorData.Corners);

        initiatedObject = DetectObject(croppedImage);

        if (initiatedObject == null)
        {
            Debug.LogError("Object not detected.");
        }

    }

    /// <summary>
    /// Detects an object in the given image.
    /// </summary>
    /// <param name="image">The image in which to detect the object.</param>
    /// <returns>The detected object.</returns>
    InitiatedObject DetectObject(Mat image)
    {
        differenceImage = SubtractImages(calibratorData.CalibratedImage, image);

        grayImage = new Mat();
        Cv2.CvtColor(differenceImage, grayImage, ColorConversionCodes.BGR2GRAY);

        // Apply bilateral filter to reduce noise while keeping edges sharp
        bilateralFilterImage = new Mat();
        Cv2.BilateralFilter(grayImage, bilateralFilterImage, 9, 50, 50);

        // Use Canny edge detection as a thresholding step
        cannyImage = new Mat();
        Cv2.Canny(bilateralFilterImage, cannyImage, 1, 75);

        kernel = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(3, 3));
        Cv2.MorphologyEx(cannyImage, cannyImage, MorphTypes.Close, kernel);

        // Find the largest contour that represents the object
        InitiatedObject initiatedObject = FindContour(cannyImage, image);

        if (initiatedObject != null)
        {
            return initiatedObject;
        }

        Debug.LogError("Object not detected.");
        return null;
    }
    /// <summary>
    /// Saves the initiated object to the object data list.
    /// </summary>
    public void SaveObjectToList()
    {
        if (initiatedObject != null)
        {
            objectData.objectDataList ??= new List<InitiatedObject>();
            objectData.objectDataList.Add(initiatedObject);
        }
        else
        {
            Debug.LogError("Object not detected.");
        }
    }

    public Mat SubtractImages(Mat image1, Mat image2)
    {
        Mat result = new();
        Cv2.Absdiff(image1, image2, result);
        return result;
    }

    public InitiatedObject FindContour(Mat thresholdImage, Mat image)
    {
        Cv2.FindContours(thresholdImage, out Point[][] contours, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);

        if (contours.Length == 0)
            return null;

        // Find the largest contour by area
        Point[] largestContour = contours.OrderByDescending(c => Cv2.ContourArea(c)).First();

        if (!calibrator.IsContourWithinImage(largestContour, image))
            largestContour = contours.OrderByDescending(c => Cv2.ContourArea(c)).Skip(1).First();

        double area = Cv2.ContourArea(largestContour);
        double canvasArea = fullImage.rectTransform.rect.width * fullImage.rectTransform.rect.height;
        double maxArea = canvasArea * 0.5;
        double minArea = canvasArea * 0.01;

        if (area < minArea || area > maxArea)
            return null;

        // If the contour consists of multiple contours, merge them into one using the convex hull algorithm
        if (largestContour.Length > 1)
        {
            largestContour = Cv2.ConvexHull(largestContour);
        }

        Vector2 centroidInCanvasSpace = CalculateAndConvertCentroid(largestContour, image, fullImage.rectTransform);
        Point centroidPoint = new((int)centroidInCanvasSpace.x, (int)centroidInCanvasSpace.y);

        RotatedRect minAreaRect = Cv2.MinAreaRect(largestContour);
        float rotationAngle = minAreaRect.Angle;

        VisualizeObject(largestContour, image, centroidInCanvasSpace, rotationAngle);

        return new InitiatedObject
        {
            Hue = GetObjectHue(image, largestContour),
            Contour = NormalizeContour(largestContour, centroidPoint, rotationAngle)
        };
    }


    /// <summary>
    /// Represents a color with 32 bits per channel (RGBA).
    /// </summary>
    /// <param name="image">The image in which the object is detected.</param>
    /// <param name="contour">The contour of the detected object.</param>
    /// <returns>The color of the detected object.</returns>
    public float GetObjectHue(Mat image, Point[] contour)
    {
        Moments moments = Cv2.Moments(contour);
        int centerX = (int)(moments.M10 / moments.M00);
        int centerY = (int)(moments.M01 / moments.M00);

        Vec3b color = image.Get<Vec3b>(centerY, centerX);
        Vector3 colorVector = new(color.Item2, color.Item1, color.Item0);
        float hue = RgbToHue(colorVector);

        return hue;
    }

    /// <summary>
    /// Converts an RGB color value to its corresponding hue value.
    /// </summary>
    /// <param name="rgb">The RGB color value to convert.</param>
    /// <returns>The hue value of the RGB color.</returns>
    float RgbToHue(Vector3 rgb)
    {
        float epsilon = 0.000001f; // Small number to avoid division by zero

        Vector4 p = (rgb.y < rgb.z) ? new Vector4(rgb.z, rgb.y, -1.0f, 2.0f / 3.0f) : new Vector4(rgb.y, rgb.z, 0.0f, -1.0f / 3.0f);
        Vector4 q = (rgb.x < p.x) ? new Vector4(p.x, p.y, p.w, rgb.x) : new Vector4(rgb.x, p.y, p.z, p.x);

        float c = q.x - Mathf.Min(q.w, q.y);
        float h = Mathf.Abs((q.w - q.y) / (6 * c + epsilon) + q.z);

        return h;
    }

    /// <summary>
    /// Visualizes the initiated object by instantiating a game object and setting its position and mesh based on the initiated object's properties.
    /// </summary>
    /// <param name="initiatedObject">The initiated object to visualize.</param>
    /// <param name="image">The image used for visualization.</param>
    public GameObject VisualizeObject(Point[] contour, Mat image, Vector2 centroidInCanvasSpace, float rotationAngle)
    {
        // Uncomment the following lines to draw the contour on the image
        // image = DrawContour(image, contour);
        // fullImage.texture = OpenCvSharp.Unity.MatToTexture(image);

        GameObject detectedObject = Instantiate(prefabMaterialEmpty, canvasPos);

        if (detectedObject.TryGetComponent(out MeshFilter meshFilter))
            meshFilter.mesh = CreateMeshFromContour(contour, centroidInCanvasSpace, rotationAngle);
        else
            Debug.LogError("Material not found.");

        detectedObject.transform.localPosition = new Vector3(centroidInCanvasSpace.x, centroidInCanvasSpace.y, -0.01f); // negative z to render in front of the image
        detectedObject.transform.rotation = Quaternion.Euler(0, 0, rotationAngle);
        detectedObject.GetComponent<MeshRenderer>().material.color = Color.blue;

        currentVisualizedObject = detectedObject;

        return detectedObject;
    }

    /// <summary>
    /// Represents a 3D mesh composed of vertices and triangles. Since Unity's coordinate system is
    /// left-handed with the y-axis pointing up, while OpenCV's coordinate system is left-handed
    /// with the y-axis pointing down, we need to apply vertical mirroring to the vertices.
    /// </summary>
    /// <param name="contour">The contour points.</param>
    /// <param name="canvasCentroid">The centroid of the contour in canvas space.</param>
    /// <returns>The created mesh.</returns>
    public Mesh CreateMeshFromContour(Point[] contour, Vector3 canvasCentroid, float rotationAngle)
    {
        Point canvasCentroidPoint = new((int)canvasCentroid.x, (int)canvasCentroid.y);
        Point[] normalizedContour = NormalizeContour(contour, canvasCentroidPoint, rotationAngle);
        Vector3[] vertices = normalizedContour.Select(point => new Vector3(point.X, point.Y, 0)).ToArray();

        Triangulator triangulator = new(vertices.Select(v => (Vector2)v).ToArray());
        int[] triangles = triangulator.Triangulate();

        Mesh mesh = new()
        {
            vertices = vertices,
            triangles = triangles
        };
        mesh.RecalculateNormals();

        return mesh;
    }

    /// <summary>
    /// Normalizes the given contour points by converting them to vertices, applying vertical mirroring, and centering them around the centroid.
    /// </summary>
    /// <param name="contour">The array of contour points to be normalized.</param>
    /// <param name="canvasCentroid">The centroid point used for centering the normalized contour.</param>
    /// <returns>The normalized contour points.</returns>
    public Point[] NormalizeContour(Point[] contour, Point canvasCentroid, float rotationAngle)
    {
        float canvasHeight = fullImage.rectTransform.rect.height;
        float canvasWidth = fullImage.rectTransform.rect.width;

        // Convert the contour points to vertices and apply the vertical mirroring
        Point[] normalizedContour = contour.Select(point => new Point(
            point.X - canvasWidth / 2f,
            -(point.Y - canvasHeight / 2f) // Apply vertical mirroring here
        )).ToArray();


        // Apply rotation to the normalized contour
        for (int i = 0; i < normalizedContour.Length; i++)
        {
            Point rotatedPoint = RotatePoint(normalizedContour[i], canvasCentroid, -rotationAngle); // negative angle to normalize the rotation 
            normalizedContour[i] = rotatedPoint;
        }

        // Center the vertices around the centroid
        for (int i = 0; i < normalizedContour.Length; i++)
        {
            normalizedContour[i] -= canvasCentroid;
        }

        return normalizedContour;
    }

    private Point RotatePoint(Point point, Point center, float angle)
    {
        double radians = angle * Math.PI / 180.0;
        double cos = Math.Cos(radians);
        double sin = Math.Sin(radians);

        int rotatedX = (int)(cos * (point.X - center.X) - sin * (point.Y - center.Y) + center.X);
        int rotatedY = (int)(sin * (point.X - center.X) + cos * (point.Y - center.Y) + center.Y);

        return new Point(rotatedX, rotatedY);
    }

    /// <summary>
    /// Calculates the centroid of the given contour in image space and converts it to canvas space.
    /// </summary>
    /// <param name="contour">The contour points.</param>
    /// <param name="image">The image on which the contour is drawn.</param>
    /// <param name="canvasRect">The RectTransform associated with the fullImage RawImage UI element.</param>
    public Vector2 CalculateAndConvertCentroid(Point[] contour, Mat image, RectTransform canvasRect)
    {
        Vector2 canvasSize = new(canvasRect.rect.width, canvasRect.rect.height);

        Moments moments = Cv2.Moments(contour);
        Vector2 centerInImageSpace = new((float)(moments.M10 / moments.M00), (float)(moments.M01 / moments.M00));

        return new Vector2(
            centerInImageSpace.x / image.Width * canvasSize.x - canvasSize.x / 2f,
            (image.Height - centerInImageSpace.y) / image.Height * canvasSize.y - canvasSize.y / 2f
        );
    }

    /// <summary>
    /// Draws a contour on the given image.
    /// </summary>
    /// <param name="image">The image on which to draw the contour.</param>
    /// <param name="contour">The contour to be drawn.</param>
    private Mat DrawContour(Mat image, Point[] contour)
    {
        Cv2.Polylines(image, new Point[][] { contour }, true, new Scalar(0, 255, 0), 2);

        return image;
    }
}
