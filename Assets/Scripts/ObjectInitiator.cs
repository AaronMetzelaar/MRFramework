using UnityEngine;
using OpenCvSharp;
using System.Linq;
using System;
using UnityEngine.UI;

public class ObjectInitiator : MonoBehaviour
{
    private Calibrator calibrator;
    private CalibratorData calibratorData;
    [NonSerialized] public WebCamTexture webCamTexture;
    [SerializeField] public RawImage fullImage;
    [SerializeField] private Transform canvasPos;
    [SerializeField] private GameObject prefabMaterialEmpty;
    [NonSerialized] public bool isInitiating = false;

    public class InitiatedObject
    {
        public Color32 Color;
        public Point[] Contour;
    }

    /// <summary>
    /// Initializes the object.
    /// </summary>
    public void Initialize()
    {
        calibrator = GetComponent<Calibrator>() ?? throw new Exception("Calibrator not found in the scene.");
        calibratorData = calibrator.CurrentCalibratorData ?? throw new Exception("Calibrator data not found. Please calibrate first.");

        if (webCamTexture == null)
        {
            Debug.LogError("Webcam texture not found.");
        }
    }

    /// <summary>
    /// This method is called every frame and checks for user input to initiate object capture and initialization.
    /// </summary>
    void Update()
    {
        if (!isInitiating)
        {
            return;
        }
        if (Input.GetKeyDown(KeyCode.Space))
        {
            CaptureAndInitiateObject();
        }
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
        else
            fullImage.texture = null;


        Mat image = OpenCvSharp.Unity.TextureToMat(webCamTexture);
        Mat croppedImage = calibrator.CropImage(image, calibratorData.Corners);

        InitiatedObject initiatedObject = DetectObject(croppedImage);

        if (initiatedObject != null)
        {
            VisualizeObject(initiatedObject, croppedImage);
        }
        else
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
        // Converting to grayscale and applying Gaussian blur to reduce noise
        Mat grayImage = new();
        Cv2.CvtColor(image, grayImage, ColorConversionCodes.BGR2GRAY);
        Cv2.GaussianBlur(grayImage, grayImage, new Size(5, 5), 0);

        Mat thresholdImage = new();

        // Otsu's thresholding can be used here, since we know the background is a uniform color
        Cv2.Threshold(grayImage, thresholdImage, 0, 255, ThresholdTypes.Otsu | ThresholdTypes.BinaryInv);

        Point[][] contours;
        HierarchyIndex[] hierarchyIndexes;
        Cv2.FindContours(thresholdImage, out contours, out hierarchyIndexes, RetrievalModes.Tree, ContourApproximationModes.ApproxSimple);

        // Assuming that the biggest contour is the object that is not the entire playing field
        foreach (Point[] contour in contours.OrderByDescending(contour => Cv2.ContourArea(contour)))
        {
            if (calibrator.IsContourWithinImage(contour, image))
            {
                return new InitiatedObject
                {
                    Color = GetObjectColor(image, contour),
                    Contour = contour
                };
            }
        }

        Debug.LogError("Object not detected.");
        return null;
    }

    Color32 GetObjectColor(Mat image, Point[] contour)
    {
        Moments moments = Cv2.Moments(contour);
        int centerX = (int)(moments.M10 / moments.M00);
        int centerY = (int)(moments.M01 / moments.M00);

        Vec3b color = image.At<Vec3b>(centerY, centerX);
        return new Color32(color.Item2, color.Item1, color.Item0, 255);
    }

    void VisualizeObject(InitiatedObject initiatedObject, Mat image)
    {
        Debug.Log("Visualizing object:" + initiatedObject.Color + ", " + initiatedObject.Contour.Length);
        GameObject detectedObject = Instantiate(prefabMaterialEmpty, canvasPos);

        // fullImage.texture = OpenCvSharp.Unity.MatToTexture(image);
        Vector2 centroidInCanvasSpace = CalculateAndConvertCentroid(initiatedObject.Contour, image, fullImage.rectTransform);

        detectedObject.transform.localPosition = new Vector3(centroidInCanvasSpace.x, centroidInCanvasSpace.y, -0.01f); // negative z to render in front of the image

        if (detectedObject.TryGetComponent(out MeshFilter meshFilter))
            meshFilter.mesh = CreateMeshFromContour(initiatedObject.Contour, centroidInCanvasSpace);
        else
            Debug.LogError("Material not found.");
    }

    /// <summary>
    /// Represents a 3D mesh composed of vertices and triangles. Since Unity's coordinate system is
    /// left-handed with the y-axis pointing up, while OpenCV's coordinate system is left-handed
    /// with the y-axis pointing down, we need to apply vertical mirroring to the vertices.
    /// </summary>
    /// <param name="contour">The contour points.</param>
    /// <param name="canvasCentroid">The centroid of the contour in canvas space.</param>
    /// <returns>The created mesh.</returns>
    Mesh CreateMeshFromContour(Point[] contour, Vector3 canvasCentroid)
    {
        // Get the canvas height from the RectTransform associated with the fullImage RawImage UI element
        float canvasHeight = fullImage.rectTransform.rect.height;
        float canvasWidth = fullImage.rectTransform.rect.width;

        // Convert the contour points to vertices and apply the vertical mirroring
        Vector3[] vertices = contour.Select(point => new Vector3(
            point.X - canvasWidth / 2f,
            -(point.Y - canvasHeight / 2f), // Apply vertical mirroring here
            0)).ToArray();

        // Center the vertices around the centroid
        for (int i = 0; i < vertices.Length; i++)
        {
            vertices[i] -= canvasCentroid;
        }

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
    /// Calculates the centroid of the given contour in image space and converts it to canvas space.
    /// </summary>
    /// <param name="contour">The contour points.</param>
    /// <param name="image">The image on which the contour is drawn.</param>
    /// <param name="canvasRect">The RectTransform associated with the fullImage RawImage UI element.</param>
    Vector2 CalculateAndConvertCentroid(Point[] contour, Mat image, RectTransform canvasRect)
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
    private void DrawContour(Mat image, Point[] contour)
    {
        Cv2.Polylines(image, new Point[][] { contour }, true, new Scalar(0, 255, 0), 2);
    }
}
