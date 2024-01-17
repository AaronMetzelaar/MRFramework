using UnityEngine;
using OpenCvSharp;
using UnityEngine.UI;
using System.Linq;
using System.Collections;
using System;
using TMPro;
using System.Collections.Generic;
using System.Threading.Tasks;

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
    [SerializeField] public Texture2D calibrationImage;

    // Boolean to check if the camera is flipped
    [NonSerialized] private bool isFlipped = false;

    [NonSerialized] public bool isCalibrating = true;
    private Mat cameraMatrix;
    private Mat distortionCoefficients;

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
        canvasPreviewImage.gameObject.SetActive(false);
        SetCalibrationImage();
        yield return new WaitForSeconds(3.0f);
        RunDetection();
        fullImage.gameObject.SetActive(false);
    }

    /// <summary>
    /// Coroutine that recalibrates the canvas corner detection.
    /// </summary>
    /// <returns>An IEnumerator used for coroutine execution.</returns>
    public IEnumerator Recalibrate()
    {
        if (canvasPreviewImage != null)
        {
            canvasPreviewImage.gameObject.SetActive(false);
        }

        cameraMatrix = null;
        distortionCoefficients = null;
        fullImage.gameObject.SetActive(true);
        instructionText.gameObject.SetActive(false);
        SetCalibrationImage();
        // Give the camera some time to take a picture
        yield return new WaitForSeconds(0.2f);
        RunDetection();
    }

    /// <summary>
    /// Runs the corner detection algorithm on the color image texture obtained from the camera data.
    /// </summary>
    private async void RunDetection()
    {
        if (webcamTexture != null && webcamTexture.isPlaying)
        {
            Texture2D texture2D = TextureToTexture2D(webcamTexture);
            Mat image = OpenCvSharp.Unity.TextureToMat(texture2D);

            Point[] corners = DetectProjectionCorners(image); // Can't do detection of a rectangle on undistorted image because of curved edges

            if (corners == null)
            {
                instructionText.text = "No rectangle found.\n\n" +
                                       "Make sure the entire playing field is visible.\n" +
                                       "Also, make sure there is enough contrast between the playing field\n" +
                                       "and the surface underneath.\n\n" +
                                       "Press <b>Spacebar</b> to recalibrate.";
                return;
            }

            Mat transformationMatrix = GetTransformationMatrix(corners);
            Mat croppedImage = CropImage(image, corners);

            ChequerboardCalibration(croppedImage);
            // Mat undistortedImage = ChequerboardCalibration(image);

            // Convert the points to the game's coordinate system
            // Point[] gameCorners = corners.Select(corner => MapCameraPointToGame(corner, transformationMatrix)).ToArray();
            // Debug.Log("Game corners: " + gameCorners[0] + ", " + gameCorners[1] + ", " + gameCorners[2] + ", " + gameCorners[3]);
            // Point[] undistortedGameCorners = UndistortPoints(corners, cameraMatrix, distortionCoefficients);
            // undistortedGameCorners = undistortedGameCorners.Select(corner => MapCameraPointToGame(corner, transformationMatrix)).ToArray();
            // Debug.Log("Game corners: " + undistortedGameCorners[0] + ", " + undistortedGameCorners[1] + ", " + undistortedGameCorners[2] + ", " + undistortedGameCorners[3]);

            // Swap points if camera rotation option is different
            // if (cameraRotation != CameraRotationOption.None)
            // {
            //     gameCorners = SwapPoints(gameCorners);
            // }

            Mat baseImage = await GetBaseImageAsync(transformationMatrix, cameraMatrix, distortionCoefficients);

            canvasPreviewImage.gameObject.SetActive(true);
            canvasPreviewImage = RotateRawImage(canvasPreviewImage, cameraRotation);
            // canvasPreviewImage.texture = OpenCvSharp.Unity.MatToTexture(baseImage);

            // Save the calibration data
            CurrentCalibratorData = new CalibratorData(corners, transformationMatrix, cameraMatrix, distortionCoefficients, baseImage, cameraRotation);
        }
    }

    // Gets an image of the cropped and distorted board without anything projected on it
    public async Task<Mat> GetBaseImageAsync(Mat transformationMatrix, Mat cameraMatrix, Mat distortionCoefficients)
    {
        if (webcamTexture != null && webcamTexture.isPlaying)
        {
            fullImage.gameObject.SetActive(false);
            instructionText.gameObject.SetActive(false);
            await Task.Delay(500);

            Texture2D texture2D = TextureToTexture2D(webcamTexture);
            Mat image = OpenCvSharp.Unity.TextureToMat(texture2D);
            Mat baseImage = GetUndistortedCroppedImage(image, transformationMatrix, cameraMatrix, distortionCoefficients);

            instructionText.gameObject.SetActive(true);

            return baseImage;
        }

        return null;
    }

    /// Detects corners in the given texture using OpenCV.
    /// </summary>
    /// <param name="texture">The input texture.</param>
    /// <returns>An array of points representing the detected corners, or null if no corners are found.</returns>
    public Point[] DetectProjectionCorners(Mat image)
    {
        Mat grayImage = new();
        Mat thresholdImage = new();
        Mat contoursImage = new();

        Cv2.CvtColor(image, grayImage, ColorConversionCodes.RGBA2GRAY);
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

            Cv2.FindContours(thresholdImage, out Point[][] contours, out HierarchyIndex[] hierarchyIndexes, RetrievalModes.External, ContourApproximationModes.ApproxSimple);

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

            // Update the RawImage texture
            if (canvasPreviewImage != null)
            {
                canvasPreviewImage.texture = textureWithRectangle;
            }
        }
        else
        {
            canvasPreviewImage.texture = webcamTexture;
            fullImage.gameObject.SetActive(false);
            Debug.LogError("No rectangle found.");
            return null;
        }

        if (rectangleCorners.Length == 4)
        {
            return rectangleCorners;
        }

        return null;
    }

    public void ChequerboardCalibration(Mat image)
    {
        Size patternSize = new(9, 6);
        Cv2.FindChessboardCorners(image, patternSize, out Point2f[] corners,
                                  ChessboardFlags.AdaptiveThresh | ChessboardFlags.NormalizeImage);

        if (corners.Length == 54)
        {
            Mat grayImage = new();
            Cv2.CvtColor(image, grayImage, ColorConversionCodes.BGRA2GRAY);

            // Refine corner locations (needs grayscale image)
            Cv2.CornerSubPix(grayImage, corners, new Size(11, 11), new Size(-1, -1),
                             new TermCriteria(CriteriaType.Eps | CriteriaType.MaxIter, 30, 0.1));

            List<Point3f> objectPoints = new();
            for (int i = 0; i < patternSize.Height; i++)
                for (int j = 0; j < patternSize.Width; j++)
                    objectPoints.Add(new Point3f(j, i, 0.0f));

            List<Point3f[]> objPoints = new() { objectPoints.ToArray() };
            List<Point2f[]> imgPoints = new() { corners };
            List<Mat> objPointsMat = objPoints.Select(p => new Mat(p.Length, 1, MatType.CV_32FC3, p)).ToList();
            List<Mat> imgPointsMat = imgPoints.Select(p => new Mat(p.Length, 1, MatType.CV_32FC2, p)).ToList();

            Mat camMatrix = new();
            Mat distCoeffs = new();
            Cv2.CalibrateCamera(objPointsMat, imgPointsMat, image.Size(), camMatrix, distCoeffs,
                                out _, out _);

            // Save to calibrator data
            // Change this to be done all in one
            cameraMatrix = camMatrix;
            distortionCoefficients = distCoeffs;

            // To see the undistorted image, uncomment the following line
            // canvasPreviewImage.texture = OpenCvSharp.Unity.MatToTexture(undistortedImage);
        }
        else
        {
            Debug.LogError("Chessboard corners not found.");
        }
    }

    // Display the calibrationImage image on the fullImage RawImage
    public void SetCalibrationImage()
    {
        if (calibrationImage == null)
        {
            Debug.LogError("No calibration image found.");
            return;
        }

        fullImage.texture = calibrationImage;
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
        else if (rotation == CameraRotationOption.MirroredHorizontally && !isFlipped)
        {
            rawImage.rectTransform.Rotate(0, 180, 0);
            isFlipped = true;
        }
        else if (rotation == CameraRotationOption.MirroredBoth && !isFlipped)
        {
            rawImage.rectTransform.Rotate(0, 0, 180);
            isFlipped = true;
        }

        else if (rotation == CameraRotationOption.None && isFlipped)
        {
            rawImage.rectTransform.Rotate(0, 0, 180);
            isFlipped = false;
        }

        return rawImage;
    }

    // Transform points using the camera matrix and distortion coefficients
    public Point[] UndistortPoints(Point[] points, Mat cameraMatrix, Mat distortionCoefficients)
    {
        if (points == null)
        {
            Debug.LogError("Points array is null.");
            return null;
        }
        if (cameraMatrix == null || distortionCoefficients == null)
        {
            Debug.LogError("Camera matrix or distortion coefficients not found.");
            return null;
        }

        Point2f[] points2f = points.Select(point => new Point2f(point.X, point.Y)).ToArray();

        Mat pointsMat = new(points2f.Length, 1, MatType.CV_32FC2, points2f);
        Mat undistortedPointsMat = new();
        Cv2.UndistortPoints(pointsMat, undistortedPointsMat, cameraMatrix, distortionCoefficients);

        Point[] undistortedPoints = new Point[points.Length];
        for (int i = 0; i < points.Length; i++)
        {
            Point2f undistortedPoints2f = undistortedPointsMat.At<Point2f>(i);
            undistortedPoints[i] = new Point((int)undistortedPoints2f.X, (int)undistortedPoints2f.Y);
        }

        return undistortedPoints;
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
        Mat transformedImage = new();
        Cv2.WarpPerspective(image, transformedImage, GetTransformationMatrix(corners), new Size(Screen.width, Screen.height));

        return transformedImage;
    }

    public Mat GetUndistortedCroppedImage(Mat image, Mat transformationMatrix, Mat cameraMatrix, Mat distortionCoefficients)
    {
        if (cameraMatrix == null || distortionCoefficients == null)
        {
            Debug.LogError("Camera matrix or distortion coefficients not found.");
            return null;
        }

        Mat undistortedImage = new();
        Cv2.Undistort(image, undistortedImage, cameraMatrix, distortionCoefficients);

        Mat croppedImage = new();
        Cv2.WarpPerspective(undistortedImage, croppedImage, transformationMatrix, new Size(Screen.width, Screen.height));

        return croppedImage;
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