using UnityEngine;
using OpenCvSharp;
using System.Linq;
using System;
using UnityEngine.UI;

public class ObjectInitiator : MonoBehaviour
{
    private Calibrator calibrator;
    private CalibratorData calibratorData;
    [NonSerialized]
    public WebCamTexture webCamTexture;
    public RawImage fullImage;
    [SerializeField] private Transform canvasPos;
    [SerializeField] private GameObject prefabMaterialEmpty;

    public class InitiatedObject
    {
        public Color32 Color;
        public Point[] Contour;
    }

    [NonSerialized]
    public bool isInitiating = false;

    public void Initialize()
    {
        calibrator = GetComponent<Calibrator>() ?? throw new Exception("Calibrator not found in the scene.");
        calibratorData = calibrator.CurrentCalibratorData ?? throw new Exception("Calibrator data not found. Please calibrate first.");

        if (webCamTexture == null)
        {
            Debug.LogError("Webcam texture not found.");
        }
    }

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

    InitiatedObject DetectObject(Mat image)
    {
        // Converting to grayscale and applying Gaussian blur to reduce noise
        Mat grayImage = new();
        Cv2.CvtColor(image, grayImage, ColorConversionCodes.BGR2GRAY);
        Cv2.GaussianBlur(grayImage, grayImage, new Size(5, 5), 0);

        // Thresholding to get binary image
        Mat thresholdImage = new();

        // Otsu's thresholding can be used here, since we know the background is a uniform color
        Cv2.Threshold(grayImage, thresholdImage, 0, 255, ThresholdTypes.Otsu | ThresholdTypes.BinaryInv);

        // Finding contours
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

        Cv2.Polylines(image, new Point[][] { initiatedObject.Contour }, true, new Scalar(0, 255, 0), 2);
        fullImage.texture = OpenCvSharp.Unity.MatToTexture(image);

        Vector2 centroidInImageSpace = CalculateCentroidImageSpace(initiatedObject.Contour);
        Vector2 centroidInCanvasSpace = ConvertToCanvasLocalSpace(centroidInImageSpace, image, fullImage.rectTransform);

        detectedObject.transform.localPosition = new Vector3(centroidInCanvasSpace.x, centroidInCanvasSpace.y, -0.01f); // negative z to render in front of the image

        if (detectedObject.TryGetComponent(out MeshFilter meshFilter))
            meshFilter.mesh = CreateMeshFromContour(initiatedObject.Contour, centroidInImageSpace, centroidInCanvasSpace);
        else
            Debug.LogError("Material not found.");
    }

    Mesh CreateMeshFromContour(Point[] contour, Vector2 imageCentroid, Vector3 canvasCentroid)
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

    private Vector2 CalculateCentroidImageSpace(Point[] contour)
    {
        Moments moments = Cv2.Moments(contour);
        return new Vector2((float)(moments.M10 / moments.M00), (float)(moments.M01 / moments.M00));
    }

    private Vector2 ConvertToCanvasLocalSpace(Vector2 centerInImageSpace, Mat image, RectTransform canvasRect)
    {
        Vector2 canvasSize = new(canvasRect.rect.width, canvasRect.rect.height);
        return new Vector2(
            centerInImageSpace.x / image.Width * canvasSize.x - canvasSize.x / 2f,
            (image.Height - centerInImageSpace.y) / image.Height * canvasSize.y - canvasSize.y / 2f
        );
    }
}
