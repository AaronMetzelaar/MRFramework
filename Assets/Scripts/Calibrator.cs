using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using OpenCvSharp;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

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
    [NonSerialized]
    public WebCamTexture webcamTexture;

    [SerializeField]
    private CameraRotationOption cameraRotation;

    [SerializeField]
    public Image calibrationImageContainer;

    [SerializeField]
    public TextMeshProUGUI instructionText;

    [SerializeField]
    public Sprite calibrationImage;

    // Boolean to check if the camera is flipped
    [NonSerialized]
    private bool isFlipped = false;

    [NonSerialized]
    public bool isCalibrating = true;
    private Mat cameraMatrix;
    private Mat distortionCoefficients;
    private Mat transformationMatrix;

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
        get { return cameraRotation; }
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
        calibrationImageContainer.gameObject.SetActive(true);

        yield return new WaitForSeconds(3.0f);
        RunDetection();

        calibrationImageContainer.gameObject.SetActive(false);
        instructionText.gameObject.SetActive(true);
    }

    /// <summary>
    /// Coroutine that recalibrates the canvas corner detection and distortion coefficients.
    /// </summary>
    /// <returns>An IEnumerator used for coroutine execution.</returns>
    public IEnumerator Recalibrate()
    {
        if (canvasPreviewImage != null)
            canvasPreviewImage.gameObject.SetActive(false);

        cameraMatrix = null;
        distortionCoefficients = null;
        calibrationImageContainer.gameObject.SetActive(true);
        instructionText.gameObject.SetActive(false);

        // Give the camera some time to take a picture
        yield return new WaitForSeconds(0.2f);

        RunDetection();
        calibrationImageContainer.gameObject.SetActive(false);
        instructionText.gameObject.SetActive(true);
    }

    /// <summary>
    /// Runs the detection process to identify and calibrate the playing field.
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
                canvasPreviewImage.gameObject.SetActive(true);
                canvasPreviewImage.texture = webcamTexture;
                instructionText.text =
                    "No rectangle found.\n\n"
                    + "Make sure the entire playing field is visible.\n"
                    + "Also, make sure there is enough contrast between the playing field\n"
                    + "and the surface underneath.\n\n"
                    + "Press <b>Spacebar</b> to recalibrate.";
                return;
            }

            transformationMatrix = GetTransformationMatrix(corners);
            Mat croppedImage = CropImage(image, corners);
            Mat undistortedImage = ChequerboardCalibration(croppedImage);

            if (undistortedImage == null)
                return;

            Point[] undistortedCorners = UndistortPoints(
                corners,
                cameraMatrix,
                distortionCoefficients
            );

            Mat baseImage = await GetBaseImageAsync();

            instructionText.gameObject.SetActive(true);
            canvasPreviewImage.gameObject.SetActive(true);
            canvasPreviewImage = RotateRawImage(canvasPreviewImage, cameraRotation);

            // Save the calibration data
            CurrentCalibratorData = new CalibratorData(
                undistortedCorners,
                transformationMatrix,
                cameraMatrix,
                distortionCoefficients,
                baseImage,
                cameraRotation
            );
        }
    }

    /// <summary>
    /// Sets the base image, which is later used to subtract the background from the camera feed.
    /// </summary>
    public async void SetBaseImage()
    {
        canvasPreviewImage.gameObject.SetActive(false);
        Mat baseImage = await GetBaseImageAsync();

        if (baseImage == null)
        {
            Debug.LogError("Base image not found.");
            return;
        }

        CurrentCalibratorData.BaseImage = baseImage;
        canvasPreviewImage.gameObject.SetActive(true);

        canvasPreviewImage.texture = OpenCvSharp.Unity.MatToTexture(baseImage);
        instructionText.gameObject.SetActive(true);
        instructionText.text =
            "Base image set.\n\n"
            + "Press <b>Spacebar</b> to recalibrate the rectangle.\n"
            + "Press <b>B</b> to set a new base image based on the current found rectangle.\n"
            + "Press <b>Enter</b> to continue.";
    }

    /// <summary>
    /// Asynchronously retrieves the base image from the webcam and applies distortion correction.
    /// </summary>
    /// <returns>The base image with distortion correction and cropping applied.</returns>
    public async Task<Mat> GetBaseImageAsync()
    {
        if (webcamTexture == null || !webcamTexture.isPlaying)
        {
            Debug.LogError("Webcam texture not found or not playing.");
            return null;
        }

        if (transformationMatrix == null || cameraMatrix == null || distortionCoefficients == null)
        {
            Debug.LogError(
                "Transformation matrix, camera matrix, or distortion coefficients not found."
            );
            return null;
        }

        calibrationImageContainer.gameObject.SetActive(false);
        instructionText.gameObject.SetActive(false);
        await Task.Delay(500);

        Mat image = OpenCvSharp.Unity.TextureToMat(webcamTexture);
        Mat baseImage = GetUndistortedCroppedImage(
            image,
            transformationMatrix,
            cameraMatrix,
            distortionCoefficients
        );

        return baseImage;
    }

    /// <summary>
    /// Detects the corners of a projected rectangle in an image.
    /// </summary>
    /// <param name="image">The input image.</param>
    /// <returns>An array of points representing the corners of the detected rectangle, or null if no rectangle is found.</returns>
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

            Cv2.FindContours(
                thresholdImage,
                out Point[][] contours,
                out HierarchyIndex[] hierarchyIndexes,
                RetrievalModes.External,
                ContourApproximationModes.ApproxSimple
            );

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
                Point[] polyApprox = Cv2.ApproxPolyDP(
                    contours[maxAreaIndex],
                    Cv2.ArcLength(contours[maxAreaIndex], true) * 0.02,
                    true
                );

                if (polyApprox.Length == 4)
                {
                    // If the size of the rectangle is smaller than 100 pixels, it's probably just noise
                    if (
                        !(Cv2.ContourArea(polyApprox) < 100)
                        && IsContourWithinImage(polyApprox, image)
                    )
                    {
                        rectangleCorners = polyApprox;
                        foundRectangle = true;
                        break;
                    }
                }
            }

            if (foundRectangle)
                break;
        }

        if (foundRectangle)
        {
            Cv2.Polylines(image, new[] { rectangleCorners }, true, new Scalar(0, 255, 0), 5);
            Texture2D textureWithRectangle = OpenCvSharp.Unity.MatToTexture(image);

            if (canvasPreviewImage != null)
                canvasPreviewImage.texture = textureWithRectangle;
        }
        else
        {
            canvasPreviewImage.texture = webcamTexture;
            calibrationImageContainer.gameObject.SetActive(false);
            Debug.LogError("No rectangle found.");
            return null;
        }

        if (rectangleCorners.Length == 4)
        {
            instructionText.text =
                "If the border is incorrect, make sure the entire playing field is visible.\n"
                + "Also, make sure there is enough contrast between the playing field\n"
                + "and the surface underneath.\n\n"
                + "Press <b>Spacebar</b> to recalibrate.\n"
                + "Press <b>B</b> to set a base image based on the current found rectangle.\n"
                + "Press <b>Enter</b> to continue.";
            return rectangleCorners;
        }

        return null;
    }

    /// <summary>
    /// Calibrates the camera using a chequerboard pattern.
    /// </summary>
    /// <param name="image">The input image.</param>
    /// <returns>The input image, calibrated with distortion correction.</returns>
    public Mat ChequerboardCalibration(Mat image)
    {
        Size patternSize = new(9, 6);
        Cv2.FindChessboardCorners(
            image,
            patternSize,
            out Point2f[] corners,
            ChessboardFlags.AdaptiveThresh | ChessboardFlags.NormalizeImage
        );

        if (corners.Length == 54)
        {
            Mat grayImage = new();
            Cv2.CvtColor(image, grayImage, ColorConversionCodes.BGRA2GRAY);

            // Refine corner locations (needs grayscale image)
            Cv2.CornerSubPix(
                grayImage,
                corners,
                new Size(11, 11),
                new Size(-1, -1),
                new TermCriteria(CriteriaType.Eps | CriteriaType.MaxIter, 30, 0.1)
            );

            List<Point3f> objectPoints = new();
            for (int i = 0; i < patternSize.Height; i++)
            for (int j = 0; j < patternSize.Width; j++)
                objectPoints.Add(new Point3f(j, i, 0.0f));

            List<Point3f[]> objPoints = new() { objectPoints.ToArray() };
            List<Point2f[]> imgPoints = new() { corners };
            List<Mat> objPointsMat = objPoints
                .Select(p => new Mat(p.Length, 1, MatType.CV_32FC3, p))
                .ToList();
            List<Mat> imgPointsMat = imgPoints
                .Select(p => new Mat(p.Length, 1, MatType.CV_32FC2, p))
                .ToList();

            Mat camMatrix = new();
            Mat distCoeffs = new();
            Cv2.CalibrateCamera(
                objPointsMat,
                imgPointsMat,
                image.Size(),
                camMatrix,
                distCoeffs,
                out _,
                out _
            );

            // Save to calibrator data
            cameraMatrix = camMatrix;
            distortionCoefficients = distCoeffs;

            // To see the undistorted image, uncomment the following lines
            Mat undistortedImage = new();
            Cv2.Undistort(image, undistortedImage, camMatrix, distCoeffs);

            return undistortedImage;
            // canvasPreviewImage.texture = OpenCvSharp.Unity.MatToTexture(undistortedImage);
        }
        else
        {
            canvasPreviewImage.gameObject.SetActive(true);
            canvasPreviewImage.texture = OpenCvSharp.Unity.MatToTexture(image);
            instructionText.gameObject.SetActive(true);
            instructionText.text =
                "Chessboard corners not found.\n\n"
                + "Make sure the entire playing field is visible.\n"
                + "Also, make sure there is enough contrast between the playing field\n"
                + "and the surface underneath.\n\n"
                + "Press <b>Spacebar</b> to recalibrate.";
            Debug.LogError("Chessboard corners not found.");
            return null;
        }
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
        bool isInsideImage =
            boundingRect.X > 1
            && boundingRect.Y > 1
            && boundingRect.X + boundingRect.Width < image.Width - 1
            && boundingRect.Y + boundingRect.Height < image.Height - 1;

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
        texture2D.ReadPixels(
            new UnityEngine.Rect(0, 0, renderTexture.width, renderTexture.height),
            0,
            0
        );
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

    /// <summary>
    /// Undistorts an array of points using the provided camera matrix and distortion coefficients.
    /// </summary>
    /// <param name="points">The array of points to be undistorted.</param>
    /// <param name="cameraMatrix">The camera matrix.</param>
    /// <param name="distortionCoefficients">The distortion coefficients.</param>
    /// <returns>An array of undistorted points.</returns>
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
            undistortedPoints[i] = new Point(
                (int)undistortedPoints2f.X,
                (int)undistortedPoints2f.Y
            );
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
        // Source points as seen by the camera
        Point2f[] sourcePoints = OrderCorners(
            corners.Select(corner => new Point2f(corner.X, corner.Y)).ToArray()
        );

        // The corner points of the camera feed
        Point2f[] destinationPoints = new Point2f[4];

        if (cameraRotation == CameraRotationOption.None)
        {
            destinationPoints = new Point2f[]
            {
                new(0, 0), // Top left
                new(Screen.width, 0), // Top right
                new(Screen.width, Screen.height), // Bottom right
                new(0, Screen.height), // Bottom left
            };
        }
        else if (cameraRotation == CameraRotationOption.MirroredBoth)
        {
            destinationPoints = new Point2f[]
            {
                new(Screen.width, Screen.height),
                new(0, Screen.height),
                new(0, 0),
                new(Screen.width, 0),
            };
        }
        else if (cameraRotation == CameraRotationOption.MirroredHorizontally)
        {
            destinationPoints = new Point2f[]
            {
                new(0, Screen.height),
                new(Screen.width, Screen.height),
                new(Screen.width, 0),
                new(0, 0),
            };
        }
        else if (cameraRotation == CameraRotationOption.MirroredVertically)
        {
            destinationPoints = new Point2f[]
            {
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
        Cv2.WarpPerspective(
            image,
            transformedImage,
            GetTransformationMatrix(corners),
            new Size(Screen.width, Screen.height)
        );

        return transformedImage;
    }

    /// <summary>
    /// Undistorts and crops the input image based on the provided transformation matrix, camera matrix, and distortion coefficients.
    /// </summary>
    /// <param name="image">The input image to be undistorted and cropped.</param>
    /// <param name="transformationMatrix">The transformation matrix.</param>
    /// <param name="cameraMatrix">The camera matrix.</param>
    /// <param name="distortionCoefficients">The distortion coefficients.</param>
    /// <returns>The undistorted and cropped image.</returns>
    public Mat GetUndistortedCroppedImage(
        Mat image,
        Mat transformationMatrix,
        Mat cameraMatrix,
        Mat distortionCoefficients
    )
    {
        if (cameraMatrix == null || distortionCoefficients == null)
        {
            Debug.LogError("Camera matrix or distortion coefficients not found.");
            return null;
        }

        Mat undistortedImage = new();
        Cv2.Undistort(image, undistortedImage, cameraMatrix, distortionCoefficients);

        Mat croppedImage = new();
        Cv2.WarpPerspective(
            undistortedImage,
            croppedImage,
            transformationMatrix,
            new Size(Screen.width, Screen.height)
        );

        return croppedImage;
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
        Point2f[] orderedCorners = corners
            .OrderBy(point => Math.Atan2(point.Y - center.Y, point.X - center.X))
            .ToArray();

        if (orderedCorners[0].X > orderedCorners[2].X || orderedCorners[0].Y > orderedCorners[2].Y)
        {
            return new Point2f[]
            {
                orderedCorners[1],
                orderedCorners[0],
                orderedCorners[3],
                orderedCorners[2]
            };
        }

        return orderedCorners;
    }
}
