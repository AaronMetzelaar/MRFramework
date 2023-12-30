// using UnityEngine;
// using OpenCvSharp;
// using System.Linq;
// using System;
// using UnityEngine.UI;

// public class ObjectInitiator : MonoBehaviour
// {
//     private Calibrator calibrator;
//     private CalibratorData calibratorData;
//     [NonSerialized]
//     public WebCamTexture webCamTexture;
//     public RawImage fullCanvas;

//     public Canvas canvas;

//     public class InitiatedObject
//     {
//         public Color32 Color;
//         public Point[] Contour;

//         // public ObjectType objectType;
//     }

//     [NonSerialized]
//     public bool isInitiating = false;

//     public void Initialize()
//     {
//         calibrator = GetComponent<Calibrator>() ?? throw new Exception("Calibrator not found in the scene.");
//         calibratorData = calibrator.CurrentCalibratorData ?? throw new Exception("Calibrator data not found. Please calibrate first.");
//         RectTransform rectTransform = fullCanvas.GetComponent<RectTransform>();

//         if (webCamTexture == null)
//         {
//             Debug.LogError("Webcam texture not found.");
//         }
//     }

//     void Update()
//     {
//         if (!isInitiating || !Input.GetKeyDown(KeyCode.Space))
//         {
//             return;
//         }
//         CaptureAndInitiateObject();
//     }

//     public void CaptureAndInitiateObject()
//     {
//         if (calibrator.CurrentCalibratorData == null)
//         {
//             Debug.LogError("Calibrator data is not available. Please calibrate first.");
//             return;
//         }

//         fullCanvas.gameObject.SetActive(true);


//         Mat image = OpenCvSharp.Unity.TextureToMat(webCamTexture);
//         Mat croppedImage = calibrator.CropImage(image, calibratorData.Corners);

//         InitiatedObject initiatedObject = DetectObject(croppedImage);

//         if (initiatedObject != null)
//         {
//             InitializeObject(initiatedObject, croppedImage);
//         }
//         else
//         {
//             Debug.LogError("Object not detected.");
//         }
//     }

//     InitiatedObject DetectObject(Mat image)
//     {
//         // Converting to grayscale and applying Gaussian blur to reduce noise
//         Mat grayImage = new Mat();
//         Cv2.CvtColor(image, grayImage, ColorConversionCodes.BGR2GRAY);
//         Cv2.GaussianBlur(grayImage, grayImage, new Size(5, 5), 0);

//         // Thresholding to get binary image
//         Mat thresholdImage = new Mat();

//         // Otsu's thresholding can be used here, since we know the background is a uniform color
//         Cv2.Threshold(grayImage, thresholdImage, 0, 255, ThresholdTypes.Otsu | ThresholdTypes.BinaryInv);

//         Point[][] contours;
//         HierarchyIndex[] hierarchyIndexes;
//         Cv2.FindContours(thresholdImage, out contours, out hierarchyIndexes, RetrievalModes.Tree, ContourApproximationModes.ApproxSimple);

//         // Assuming that the biggest contour is the object that is not the entire playing field (can check using calibrator.IsContourWithinImage)
//         foreach (Point[] contour in contours.OrderByDescending(contour => Cv2.ContourArea(contour)))
//         {
//             if (calibrator.IsContourWithinImage(contour, image))
//             {

//                 return new InitiatedObject
//                 {
//                     Color = GetObjectColor(image, contour),
//                     Contour = contour
//                 };
//             }
//         }

//         Debug.LogError("Object not detected.");
//         return null;
//     }
//     Color32 GetObjectColor(Mat image, Point[] contour)
//     {
//         Moments moments = Cv2.Moments(contour);
//         int centerX = (int)(moments.M10 / moments.M00);
//         int centerY = (int)(moments.M01 / moments.M00);

//         Vec3b color = image.At<Vec3b>(centerY, centerX);
//         return new Color32(color.Item2, color.Item1, color.Item0, 255);
//     }

//     private void InitializeObject(InitiatedObject initiatedObject, Mat image)
//     {
//         GameObject detectedObject = new GameObject("TestObject");

//         // Calculate pixels per unit and centroid
//         float pixelsPerUnit = CalculatePixelsPerUnit(fullCanvas.rectTransform, image);
//         Vector2 centroidInImageSpace = CalculateCentroid(initiatedObject.Contour);

//         // Convert centroid from image space to canvas local space
//         Vector2 centerInCanvasSpace = ConvertToCanvasLocalSpace(centroidInImageSpace, image, canvas.GetComponent<RectTransform>());

//         // Set GameObject's local position and scale
//         // SetGameObjectLocalPosition(detectedObject, centerInCanvasSpace);
//         // SetGameObjectLocalScale(detectedObject, Cv2.BoundingRect(initiatedObject.Contour), canvas.GetComponent<RectTransform>(), image);


//         // Add MeshRenderer and MeshFilter and set material
//         MeshRenderer meshRenderer = detectedObject.AddComponent<MeshRenderer>();
//         MeshFilter meshFilter = detectedObject.AddComponent<MeshFilter>();
//         meshRenderer.material = new Material(Shader.Find("Standard")) { color = Color.black };

//         // Create and assign mesh
//         Mesh mesh = CreateMesh(initiatedObject.Contour, centroidInImageSpace, pixelsPerUnit);
//         meshFilter.mesh = mesh;

//         // Debug information
//         if (mesh != null) Debug.Log("Mesh created and assigned to GameObject.");
//         else Debug.LogError("Mesh creation failed.");


//         // TEMPORARY
//         Cv2.Polylines(image, new Point[][] { initiatedObject.Contour }, true, new Scalar(0, 255, 0), 2);
//         fullCanvas.texture = OpenCvSharp.Unity.MatToTexture(image);
//     }

//     Mesh CreateMesh(Point[] contour, Vector2 centroid, float pixelsPerUnit)
//     {
//         // Convert contour points to Vector2 vertices and adjust by the centroid
//         Vector2 centroidOffset = new Vector2(centroid.x / pixelsPerUnit, centroid.y / pixelsPerUnit);
//         Vector2[] vertices2D = contour.Select(point => new Vector2(
//             (point.X / pixelsPerUnit) - centroidOffset.x,
//             (point.Y / pixelsPerUnit) - centroidOffset.y
//         )).ToArray();

//         // Triangulate the vertices to create a mesh
//         int[] triangles = new Triangulator(vertices2D).Triangulate();
//         Vector3[] vertices3D = vertices2D.Select(point => new Vector3(point.x, point.y, 0)).ToArray();

//         // Create and return the mesh
//         Mesh mesh = new Mesh
//         {
//             vertices = vertices3D,
//             triangles = triangles
//         };
//         mesh.RecalculateNormals();

//         return mesh;
//     }

//     private Vector2 CalculateCentroid(Point[] contour)
//     {
//         Moments moments = Cv2.Moments(contour);
//         return new Vector2((float)(moments.M10 / moments.M00), (float)(moments.M01 / moments.M00));
//     }

//     float CalculatePixelsPerUnit(RectTransform canvasRect, Mat image)
//     {
//         float canvasWidthInUnits = canvasRect.sizeDelta.x * canvasRect.localScale.x;
//         float canvasHeightInUnits = canvasRect.sizeDelta.y * canvasRect.localScale.y;

//         float pixelsPerUnitWidth = image.Width / canvasWidthInUnits;
//         float pixelsPerUnitHeight = image.Height / canvasHeightInUnits;

//         // Use the average of the two conversion factors to maintain the aspect ratio
//         return (pixelsPerUnitWidth + pixelsPerUnitHeight) / 2;
//     }

//     private Vector2 ConvertToCanvasLocalSpace(Vector2 centerInImageSpace, Mat image, RectTransform canvasRect)
//     {
//         Vector2 canvasSize = new Vector2(canvasRect.rect.width, canvasRect.rect.height);
//         return new Vector2(
//             centerInImageSpace.x / image.Width * canvasSize.x - canvasSize.x / 2f,
//             canvasSize.y / 2f - centerInImageSpace.y / image.Height * canvasSize.y // Flipping y-coordinate
//         );
//     }

//     private void SetGameObjectLocalPosition(GameObject gameObject, Vector2 centerInCanvasSpace)
//     {
//         gameObject.transform.localPosition = new Vector3(centerInCanvasSpace.x, centerInCanvasSpace.y, 0);
//     }

//     private void SetGameObjectLocalScale(GameObject gameObject, OpenCvSharp.Rect boundingBox, RectTransform canvasRect, Mat image)
//     {
//         Vector2 canvasSize = new Vector2(canvasRect.rect.width, canvasRect.rect.height);
//         float scaleFactor = canvasSize.y / image.Height; // Uniform scale factor for both axes
//         gameObject.transform.localScale = new Vector3(
//             boundingBox.Width * scaleFactor,
//             boundingBox.Height * scaleFactor,
//             1f
//         );
//         Debug.Log($"Scale factor: {scaleFactor}");
//     }
// }
