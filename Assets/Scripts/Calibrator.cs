using UnityEngine;
using OpenCvSharp;
using UnityEngine.UI;
using System.Linq;
using System.Collections;
using System;
using TMPro;

/// <summary>
/// The Calibrator class is responsible for calibrating the camera and detecting corners in the camera feed.
/// It uses OpenCV for corner detection and perspective transformation.
/// </summary>
public class Calibrator : MonoBehaviour
{
    public static Calibrator Instance { get; private set; }
    public CalibratorData CurrentCalibratorData { get; private set; }
    // Used to show the camera feed with the detected rectangle
    public RawImage canvasPreviewImage;
    // The camera texture
    [NonSerialized] public WebCamTexture webcamTexture;
    // Public property for the camera rotation
    [SerializeField] private CameraRotationOption cameraRotation;

    [SerializeField] public RawImage fullImage;
    [SerializeField] public TextMeshProUGUI instructionText;

    // Boolean to check if the camera is flipped
    [NonSerialized] private bool isFlipped = false;

    [NonSerialized] public bool isCalibrating = true;

    // Enum for camera rotation options
    public enum CameraRotationOption
    {
        None,
        MirroredVertically,
        MirroredHorizontally,
        MirroredBoth
    }


    public CameraRotationOption CameraRotation
    {
        get
        {
            return cameraRotation;
        }
        set
        {
            cameraRotation = value;
            StartCoroutine(Recalibrate());
        }
    }

    /// <summary>
    /// This method is called when the script instance is being loaded.
    /// It initializes the camera and starts a coroutine for delayed initial detection.
    /// </summary>
    private void Start()
    {
        webcamTexture = new WebCamTexture();
        webcamTexture.Play();

        StartCoroutine(DelayedInitialDetection());
    }

    /// <summary>
    /// Represents a coroutine that delays the initial detection for a specified amount of time.
    /// </summary>
    /// <returns>An IEnumerator object.</returns>
    private IEnumerator DelayedInitialDetection()
    {
        instructionText.gameObject.SetActive(false);
        yield return new WaitForSeconds(3.0f);
        RunDetection();
        instructionText.gameObject.SetActive(true);
    }

    /// <summary>
    /// Runs the corner detection algorithm on the color image texture obtained from the camera data.
    /// </summary>
    private void RunDetection()
    {
        // Debug the camera size
        Debug.Log("Camera size: " + webcamTexture.width + "x" + webcamTexture.height);
        if (webcamTexture != null && webcamTexture.isPlaying)
        {
            Point[] corners = DetectCorners(webcamTexture);

            if (corners == null)
            {
                return;
            }

            Mat transformationMatrix = GetTransformationMatrix(corners);

            // Convert the points to the game's coordinate system
            Point[] gameCorners = corners.Select(corner => MapCameraPointToGame(corner, transformationMatrix)).ToArray();

            // Swap points if camera rotation option is different
            if (cameraRotation != CameraRotationOption.None)
            {
                gameCorners = SwapPoints(gameCorners);
            }

            // Draw the rectangle on the screen
            Mat image = OpenCvSharp.Unity.TextureToMat(TextureToTexture2D(webcamTexture));
            Mat croppedImage = CropImage(image, corners);
            Cv2.Polylines(image, new[] { gameCorners }, true, Scalar.Green, 5);

            // Rotate the webcam texture
            canvasPreviewImage = RotateRawImage(canvasPreviewImage, cameraRotation);

            // Save the calibration data
            CurrentCalibratorData = new CalibratorData(corners, transformationMatrix, croppedImage, cameraRotation);
        }
    }

    /// <summary>
    /// Coroutine that recalibrates the canvas corner detection.
    /// </summary>
    /// <returns>An IEnumerator used for coroutine execution.</returns>
    public IEnumerator Recalibrate()
    {
        if (canvasPreviewImage != null)
        {
            // Set texture to white
            canvasPreviewImage.texture = Texture2D.whiteTexture;
        }

        instructionText.gameObject.SetActive(false);
        // Give the camera some time to take a picture
        yield return new WaitForSeconds(0.2f);
        RunDetection();
        instructionText.gameObject.SetActive(true);

    }

    /// <summary>
    /// Detects corners in the given texture using OpenCV.
    /// </summary>
    /// <param name="texture">The input texture.</param>
    /// <returns>An array of points representing the detected corners, or null if no corners are found.</returns>
    public Point[] DetectCorners(Texture texture)
    {
        Texture2D texture2D = TextureToTexture2D(texture);
        Mat image = OpenCvSharp.Unity.TextureToMat(texture2D);

        Mat grayImage = new();
        Mat thresholdImage = new();
        Mat contoursImage = new();

        Cv2.CvtColor(image, grayImage, ColorConversionCodes.BGRA2GRAY);
        Cv2.GaussianBlur(grayImage, grayImage, new Size(5, 5), 0);

        int startThreshold = 0;
        int endThreshold = 255;
        int step = 10;
        Point[] rectangleCorners = new Point[4];
        bool foundRectangle = false;

        for (int i = startThreshold; i < endThreshold - step; i += step)
        {
            Cv2.Threshold(grayImage, thresholdImage, i, i + step, ThresholdTypes.Binary);
            Cv2.CvtColor(thresholdImage, contoursImage, ColorConversionCodes.GRAY2BGR);

            Point[][] contours;
            HierarchyIndex[] hierarchyIndexes;
            Cv2.FindContours(thresholdImage, out contours, out hierarchyIndexes, RetrievalModes.External, ContourApproximationModes.ApproxSimple);

            double maxArea = 0;
            int maxAreaIndex = -1;

            for (int j = 0; j < contours.Length; j++)
            {
                double area = Cv2.ContourArea(contours[j]);
                if (area > maxArea)
                {
                    maxArea = area;
                    maxAreaIndex = j;
                }
            }

            if (maxAreaIndex != -1)
            {
                Point[] polyApprox = Cv2.ApproxPolyDP(contours[maxAreaIndex], Cv2.ArcLength(contours[maxAreaIndex], true) * 0.02, true);

                if (polyApprox.Length == 4)
                {
                    // If the size of the rectangle is smaller than 100 pixels, it's probably just noise
                    if (!(Cv2.ContourArea(polyApprox) < 100) && IsContourWithinImage(polyApprox, image))
                    {
                        rectangleCorners = polyApprox;
                        foundRectangle = true;
                        break;
                    }
                }
            }

            if (foundRectangle)
            {
                break;
            }
        }

        if (foundRectangle)
        {
            Cv2.Polylines(image, new[] { rectangleCorners }, true, new Scalar(0, 255, 0), 5);

            // Convert back to Texture2D
            Texture2D textureWithRectangle = OpenCvSharp.Unity.MatToTexture(image);

            // // Update the RawImage texture
            if (canvasPreviewImage != null)
            {
                canvasPreviewImage.texture = textureWithRectangle;
            }
        }
        else
        {
            canvasPreviewImage.texture = OpenCvSharp.Unity.MatToTexture(image);
            Debug.LogError("No rectangle found.");
            return null;
        }

        if (rectangleCorners.Length == 4)
        {
            return rectangleCorners;
        }

        return null;
    }

    /// <summary>
    /// Checks if the contour defined by the given rectangle corners is completely within the image.
    /// </summary>
    /// <param name="rectangleCorners">The corners of the rectangle defining the contour.</param>
    /// <param name="image">The image to check against.</param>
    /// <returns>True if the contour is within the image, false otherwise.</returns>
    public bool IsContourWithinImage(Point[] rectangleCorners, Mat image)
    {
        OpenCvSharp.Rect boundingRect = Cv2.BoundingRect(rectangleCorners);
        bool isInsideImage = boundingRect.X > 1 && boundingRect.Y > 1 &&
                             boundingRect.X + boundingRect.Width < image.Width - 1 &&
                             boundingRect.Y + boundingRect.Height < image.Height - 1;

        return isInsideImage;
    }

    /// <summary>
    /// Converts a Unity Texture to a Texture2D object.
    /// Reference: https://github.com/luowensheng/hand-detection-gesture-recognition/blob/4292812712a8f9993faa73c0f818acfce67f2ccc/Assets/Scripts/TextureExtenstions.cs#L16-L24
    /// </summary>
    /// <param name="texture">The Unity Texture to convert.</param>
    /// <returns>The converted Texture2D object.</returns>
    public Texture2D TextureToTexture2D(Texture texture)
    {
        Texture2D texture2D = new(texture.width, texture.height, TextureFormat.RGBA32, false);
        RenderTexture currentRT = RenderTexture.active;
        RenderTexture renderTexture = RenderTexture.GetTemporary(texture.width, texture.height, 32);
        Graphics.Blit(texture, renderTexture);

        RenderTexture.active = renderTexture;
        texture2D.ReadPixels(new UnityEngine.Rect(0, 0, renderTexture.width, renderTexture.height), 0, 0);
        texture2D.Apply();

        RenderTexture.active = currentRT;
        RenderTexture.ReleaseTemporary(renderTexture);

        return texture2D;
    }

    /// <summary>
    /// Rotates the given RawImage based on the specified rotation option.
    /// </summary>
    /// <param name="rawImage">The RawImage to rotate.</param>
    /// <param name="rotation">The rotation option to apply.</param>
    /// <returns>The rotated RawImage.</returns>
    public RawImage RotateRawImage(RawImage rawImage, CameraRotationOption rotation)
    {
        if (rotation == CameraRotationOption.MirroredVertically && !isFlipped)
        {
            rawImage.rectTransform.Rotate(0, 0, -180);
            isFlipped = true;
        }
        else if (rotation == CameraRotationOption.None && isFlipped)
        {
            rawImage.rectTransform.Rotate(0, 0, 180);
            isFlipped = false;
        }

        return rawImage;
    }

    /// <summary>
    /// Calculates the transformation matrix for perspective transformation.
    /// </summary>
    /// <param name="corners">The array of corner points.</param>
    /// <returns>The transformation matrix.</returns>
    public Mat GetTransformationMatrix(Point[] corners)
    {
        float aspectRatio = (float)(corners[1].X - corners[0].X) / (corners[2].Y - corners[0].Y);

        // Source points as seen by the camera
        Point2f[] sourcePoints = OrderCorners(corners.Select(corner => new Point2f(corner.X, corner.Y)).ToArray());

        // The corner points of the camera feed
        Point2f[] destinationPoints = new Point2f[4];

        if (cameraRotation == CameraRotationOption.None)
        {
            destinationPoints = new Point2f[] {
                    new(0, 0),                          // Top left
                    new(Screen.width, 0),               // Top right
                    new(Screen.width, Screen.height),   // Bottom right
                    new(0, Screen.height),              // Bottom left
                };
        }
        else if (cameraRotation == CameraRotationOption.MirroredBoth)
        {
            destinationPoints = new Point2f[] {
                    new(Screen.width, Screen.height),
                    new(0, Screen.height),
                    new(0, 0),
                    new(Screen.width, 0),
                };
        }
        else if (cameraRotation == CameraRotationOption.MirroredHorizontally)
        {
            destinationPoints = new Point2f[] {
                    new(0, Screen.height),
                    new(Screen.width, Screen.height),
                    new(Screen.width, 0),
                    new(0, 0),
                };
        }
        else if (cameraRotation == CameraRotationOption.MirroredVertically)
        {
            destinationPoints = new Point2f[] {
                    new(Screen.width, 0),
                    new(0, 0),
                    new(0, Screen.height),
                    new(Screen.width, Screen.height),
                };
        }
        Mat transformationMatrix = Cv2.GetPerspectiveTransform(sourcePoints, destinationPoints);

        return transformationMatrix;
    }

    /// <summary>
    /// Crops the input image based on the provided corners.
    /// </summary>
    /// <param name="image">The input image to be cropped.</param>
    /// <param name="corners">The array of corner points used for perspective transformation.</param>
    /// <returns>The cropped image.</returns>
    public Mat CropImage(Mat image, Point[] corners)
    {
        Mat transformationMatrix = GetTransformationMatrix(corners);
        Mat transformedImage = new();
        Cv2.WarpPerspective(image, transformedImage, transformationMatrix, new Size(Screen.width, Screen.height));

        return transformedImage;
    }

    /// <summary>
    /// Maps a camera point to a game point using a transformation matrix.
    /// </summary>
    /// <param name="cameraPoint">The camera point to be mapped.</param>
    /// <param name="transformationMatrix">The transformation matrix.</param>
    /// <returns>The mapped game point.</returns>
    public Point MapCameraPointToGame(Point cameraPoint, Mat transformationMatrix)
    {
        Point2f[] cameraPointArray = new Point2f[] { new(cameraPoint.X, cameraPoint.Y) };
        Point2f[] gamePointArray = Cv2.PerspectiveTransform(cameraPointArray, transformationMatrix);

        return new Point(gamePointArray[0].X, gamePointArray[0].Y);
    }

    /// <summary>
    /// Swaps points in a given array of points based on the camera rotation option.
    /// </summary>
    /// <param name="points">The array of points to be swapped.</param>
    /// <returns>The swapped array of points.</returns>
    private Point[] SwapPoints(Point[] points)
    {
        if (cameraRotation == CameraRotationOption.MirroredVertically)
        {
            for (int i = 0; i < points.Length; i++)
            {
                points[i] = new Point(webcamTexture.width - points[i].X, webcamTexture.height - points[i].Y);
            }
        }

        return points;
    }

    /// <summary>
    /// Orders the corners of a shape in a counter-clockwise manner starting from the top left corner.
    /// </summary>
    /// <param name="corners">The array of corners to be ordered.</param>
    /// <returns>An array of corners ordered in a counter-clockwise manner.</returns>
    public Point2f[] OrderCorners(Point2f[] corners)
    {
        Point center = new(corners.Average(point => point.X), corners.Average(point => point.Y));

        // Order of points: top left, bottom left, bottom right, top right
        Point2f[] orderedCorners = corners.OrderBy(point => Math.Atan2(point.Y - center.Y, point.X - center.X)).ToArray();

        if (orderedCorners[0].X > orderedCorners[2].X || orderedCorners[0].Y > orderedCorners[2].Y)
        {
            return new Point2f[] { orderedCorners[1], orderedCorners[0], orderedCorners[3], orderedCorners[2] };
        }

        return orderedCorners;
    }
}