using UnityEngine;
using OpenCvSharp;
using System.Linq;
using System;
using UnityEngine.UI;
using System.Collections.Generic;
using System.Collections;
using TMPro;
using System.Threading.Tasks;

public class ObjectInitializer : MonoBehaviour
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
    [SerializeField] public Color objectColor = default;

    InitializedObject initializedObject = new();
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

    /// <summary>
    /// Coroutine that delays the initiation of an object.
    /// </summary>
    /// <returns>An IEnumerator used for coroutine execution.</returns>
    public IEnumerator DelayedIntialize()
    {
        instructionText.gameObject.SetActive(false);
        initializedObject = new InitializedObject();
        yield return new WaitForSeconds(0.2f);

        CaptureAndInitializeObject();
        yield return new WaitForSeconds(0.2f);
        instructionText.gameObject.SetActive(true);
    }

    /// <summary>
    /// Reinitializes the object by destroying the current visualized object and resetting the initialized object's properties.
    /// Disposes of various images and kernel used in the initialization process.
    /// Initiates the object after a delay.
    /// </summary>
    public void Reinitialize()
    {
        if (currentVisualizedObject != null)
        {
            Destroy(currentVisualizedObject);
        }
        // if (initializedObject != null)
        // {
        //     initializedObject.Contour = null;
        //     initializedObject.WhiteHue = 0f;
        //     initializedObject.Color = Color.clear;
        //     initializedObject.ColorHue = 0f;
        //     initializedObject.Name = "Object";
        // }

        fullImage.texture = null;
        differenceImage?.Dispose();
        grayImage?.Dispose();
        bilateralFilterImage?.Dispose();
        cannyImage?.Dispose();
        kernel?.Dispose();

        StartCoroutine(DelayedIntialize());
    }

    public void InitializeNamedObject(string objectName, Color color)
    {
        initializedObject = new()
        {
            Color = color,
            Name = objectName
        };

        objectColor = color;
    }

    /// <summary>
    /// Captures an image, initiates an object, and visualizes it.
    /// </summary>
    public async void CaptureAndInitializeObject()
    {
        if (calibrator.CurrentCalibratorData == null)
        {
            Debug.LogError("Calibrator data is not available. Please calibrate first.");
            return;
        }

        if (fullImage.gameObject.activeSelf == false)
        {
            fullImage.gameObject.SetActive(true);
            fullImage.texture = null;
        }

        Mat image = OpenCvSharp.Unity.TextureToMat(webCamTexture);
        Mat undistortedCroppedImage = calibrator.GetUndistortedCroppedImage(image, calibratorData.TransformationMatrix, calibratorData.CameraMatrix, calibratorData.DistortionCoefficients);
        differenceImage = SubtractImages(calibratorData.BaseImage, undistortedCroppedImage);
        Mat grayImage = TransformImage(differenceImage);
        Point[] contour = FindContour(grayImage, undistortedCroppedImage);
        // fullImage.texture = OpenCvSharp.Unity.MatToTexture(grayImage);

        if (contour == null)
        {
            instructionText.gameObject.SetActive(true);
            instructionText.text = "No object detected.\n\n" +
                                    "Place your object in the center of the canvas.\n" +
                                    "Make sure only the object is new in the scene and that the background\n" +
                                    "is the same as the background used for calibration.\n\n" +
                                   "Press <b>Spacebar</b> to reinitialize.";
            Debug.LogError("Contour not found.");
            return;
        }

        // This is needed to get the hue of the object with the Color projected on it
        Mat newImage = await GetImageFromWebcam();
        Mat newUndistortedCroppedImage = calibrator.GetUndistortedCroppedImage(newImage, calibratorData.TransformationMatrix, calibratorData.CameraMatrix, calibratorData.DistortionCoefficients);

        Vector2 centroidInCanvasSpace = CalculateAndConvertCentroid(contour, undistortedCroppedImage, fullImage.rectTransform);
        float rotationAngle = GetRotationAngle(contour);
        initializedObject.Contour = NormalizeContour(contour, centroidInCanvasSpace, Screen.width, Screen.height, rotationAngle);
        initializedObject.WhiteHue = GetObjectHue(undistortedCroppedImage, contour);
        initializedObject.ColorHue = GetObjectHue(newUndistortedCroppedImage, contour);
        initializedObject.Color = (objectColor == default) ? GetContrastingColor(initializedObject.WhiteHue) : objectColor;
    }

    public async Task<Mat> GetImageFromWebcam()
    {
        await Task.Delay(500);
        return OpenCvSharp.Unity.TextureToMat(webCamTexture);
    }

    /// <summary>
    /// Transforms the input image by applying various image processing techniques to be able to
    /// extract the contour of the object.
    /// </summary>
    /// <param name="image">The input image to be transformed.</param>
    /// <returns>The transformed image.</returns>
    public Mat TransformImage(Mat image)
    {
        Mat hsvImage = new();
        Cv2.CvtColor(image, hsvImage, ColorConversionCodes.RGB2HSV);

        Cv2.Split(hsvImage, out Mat[] channels);
        Mat grayImage = channels[2];

        // Apply bilateral filter to reduce noise while keeping edges sharp
        Mat bilateralFilterImage = new();
        Cv2.BilateralFilter(grayImage, bilateralFilterImage, 9, 50, 50);

        // Use Canny edge detection as a thresholding step
        Mat cannyImage = new();
        Cv2.Canny(bilateralFilterImage, cannyImage, 1, 75);

        kernel = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(3, 3));
        Cv2.MorphologyEx(cannyImage, cannyImage, MorphTypes.Close, kernel);

        return cannyImage;
    }

    /// <summary>
    /// Saves the initiated object to the object data list.
    /// </summary>
    public void SaveObjectToList()
    {
        if (initializedObject != null)
        {
            objectData.objectDataList ??= new List<InitializedObject>();
            objectData.objectDataList.Add(initializedObject);
        }
        else
        {
            Debug.LogError("Object not detected.");
        }
    }

    /// <summary>
    /// Subtracts two images and returns the result as a new Mat object.
    /// </summary>
    /// <param name="image1">The first image to subtract.</param>
    /// <param name="image2">The second image to subtract.</param>
    /// <returns>A new Mat object representing the result of the subtraction.</returns>
    public Mat SubtractImages(Mat image1, Mat image2)
    {
        Mat result = new();
        Cv2.Absdiff(image1, image2, result);
        return result;
    }

    /// <summary>
    /// Finds the contour of an object in an image using the provided threshold image.
    /// </summary>
    /// <param name="thresholdImage">The threshold image used for contour detection.</param>
    /// <param name="image">The original image.</param>
    /// <returns>The contour points of the largest object found.</returns>
    public Point[] FindContour(Mat thresholdImage, Mat image)
    {
        Cv2.FindContours(thresholdImage, out Point[][] contours, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);

        if (contours.Length == 0)
        {
            Debug.LogError("No contours found.");
            return null;
        }

        // Find the largest contour by area
        Point[] largestContour = contours.OrderByDescending(c => Cv2.ContourArea(c)).First();

        if (!calibrator.IsContourWithinImage(largestContour, image))
            largestContour = contours.OrderByDescending(c => Cv2.ContourArea(c)).Skip(1).First();

        double area = Cv2.ContourArea(largestContour);
        double canvasArea = fullImage.rectTransform.rect.width * fullImage.rectTransform.rect.height;
        double maxArea = canvasArea * 0.5;
        double minArea = canvasArea * 0.005;

        if (area < minArea || area > maxArea)
        {
            Debug.LogError("Contour area is too small or too large.");
            return null;
        }

        // If the contour consists of multiple contours, merge them into one using the convex hull algorithm
        if (largestContour.Length > 1)
            largestContour = Cv2.ConvexHull(largestContour);

        Vector2 centroidInCanvasSpace = CalculateAndConvertCentroid(largestContour, image, fullImage.rectTransform);
        float rotationAngle = GetRotationAngle(largestContour);
        Color color = objectColor == default ? GetContrastingColor(initializedObject.WhiteHue) : objectColor;
        Point[] normalizedContour = NormalizeContour(largestContour, centroidInCanvasSpace, Screen.width, Screen.height, rotationAngle);
        GameObject initializedGameObject = VisualizeObject(normalizedContour, image, centroidInCanvasSpace, rotationAngle, color);
        // initializedGameObject.transform.localRotation = Quaternion.Euler(0, 0, rotationAngle);

        return largestContour;
    }

    /// <summary>
    /// Get the hue of the object by calculating the hue of the pixel at the center of
    /// the object.
    /// </summary>
    /// <param name="image">The image in which the object is detected.</param>
    /// <param name="contour">The contour of the detected object.</param>
    /// <returns>The color of the detected object.</returns>
    public float GetObjectHue(Mat image, Point[] contour)
    {
        Moments moments = Cv2.Moments(contour);
        int centerX = (int)(moments.M10 / moments.M00);
        int centerY = (int)(moments.M01 / moments.M00);

        Vec3b pixel = image.Get<Vec3b>(centerY, centerX);
        Vector3 rgb = new(pixel.Item2, pixel.Item1, pixel.Item0);
        float hue = RgbToHue(rgb);

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
    /// Converts a hue value to its corresponding RGB color value.
    /// </summary>
    /// <param name="hue">The hue value to convert.</param>
    /// <returns>The RGB color value of the hue.</returns>
    Vector3 HueToRgb(float hue)
    {
        float r = Mathf.Abs(hue * 6 - 3) - 1;
        float g = 2 - Mathf.Abs(hue * 6 - 2);
        float b = 2 - Mathf.Abs(hue * 6 - 4);

        return new Vector3(Mathf.Clamp01(r), Mathf.Clamp01(g), Mathf.Clamp01(b));
    }

    /// <summary>
    /// Initializes the color of the initiated object based on getting the contrasting color of the object's hue.
    /// </summary>
    /// <param name="hue">The hue value of the initiated object.</param>
    /// <param name="initializedObject">The initiated object.</param>
    public Color GetContrastingColor(float hue)
    {
        float contrastingHue = (hue + 0.5f) % 1f;
        Vector3 contrastingColor = HueToRgb(contrastingHue);
        Color color = new(contrastingColor.x, contrastingColor.y, contrastingColor.z, 1f);
        return color;
    }

    /// <summary>
    /// Calculates the rotation angle of the given contour. The angle can be between 0 and 360 degrees.
    /// </summary>
    /// <param name="contour">The contour to calculate the rotation angle for.</param>
    /// <returns>The rotation angle of the contour.</returns>

    public float GetRotationAngle(Point[] contour)
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

        return rotationAngle;
    }

    /// <summary>
    /// Visualizes the initiated object by instantiating a game object and setting its position and mesh based on the initiated object's properties.
    /// </summary>
    /// <param name="contour">The contour of the initiated object. This must be normalized beforehand.</param>
    /// <param name="image">The image used for visualization.</param>
    public GameObject VisualizeObject(Point[] contour, Mat image, Vector2 centroidInCanvasSpace, float rotationAngle, Color color = default)
    {
        // Uncomment the following lines to draw the contour on the image
        // image = DrawContour(image, contour);
        // fullImage.texture = OpenCvSharp.Unity.MatToTexture(image);

        GameObject detectedObject = Instantiate(prefabMaterialEmpty, new Vector3(centroidInCanvasSpace.x, centroidInCanvasSpace.y, -0.01f), Quaternion.identity);
        detectedObject.transform.SetParent(canvasPos, false);

        if (detectedObject.TryGetComponent(out MeshFilter meshFilter))
            meshFilter.mesh = CreateMeshFromContour(contour, centroidInCanvasSpace);
        else
            Debug.LogError("Material not found.");
        detectedObject.GetComponent<MeshRenderer>().material.color = color;
        detectedObject.transform.localRotation = Quaternion.Euler(0, 0, rotationAngle);

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
    public Mesh CreateMeshFromContour(Point[] contour, Vector3 canvasCentroid)
    {
        Point canvasCentroidPoint = new((int)canvasCentroid.x, (int)canvasCentroid.y);
        Vector3[] vertices = contour.Select(point => new Vector3(point.X, point.Y, 0)).ToArray();

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
    /// Normalizes the contour points by shifting them to the center of the canvas and rotating them by the given angle.
    /// </summary>
    /// <param name="contour">The array of contour points to be normalized.</param>
    /// <param name="canvasWidth">The width of the canvas.</param>
    /// <param name="canvasHeight">The height of the canvas.</param>
    public Point[] NormalizeContour(Point[] contour, Vector2 centroid, float canvasWidth, float canvasHeight, float rotationAngle)
    {
        Point[] normalizedContour = new Point[contour.Length];

        float centerX = canvasWidth / 2f;
        float centerY = canvasHeight / 2f;
        float normalizedRotationAngle = rotationAngle * Mathf.Deg2Rad;

        float cosAngle = Mathf.Cos(normalizedRotationAngle);
        float sinAngle = Mathf.Sin(normalizedRotationAngle);

        for (int i = 0; i < contour.Length; i++)
        {
            float shiftedX = contour[i].X - centerX - centroid.x;
            float shiftedY = centerY - contour[i].Y - centroid.y;

            float rotatedX = shiftedX * cosAngle + shiftedY * sinAngle; // swapped sign
            float rotatedY = -shiftedX * sinAngle + shiftedY * cosAngle; // swapped sign

            Point normalizedPoint = new((int)rotatedX, (int)rotatedY);
            normalizedContour[i] = normalizedPoint;
        }

        return normalizedContour;
    }

    /// <summary>
    /// Translates the contour points by the given offset.
    /// </summary>
    /// <param name="contour">The array of contour points to be translated.</param>
    /// <param name="offset">The offset to be applied to the contour points.</param>
    /// <returns>The translated contour points.</returns>
    public Point[] TranslateContour(Point[] contour, Point offset)
    {
        Point[] translatedContour = new Point[contour.Length];

        for (int i = 0; i < contour.Length; i++)
        {
            Point translatedPoint = new(
                contour[i].X + offset.X,
                contour[i].Y + offset.Y
            );

            translatedContour[i] = translatedPoint;
        }

        return translatedContour;
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
