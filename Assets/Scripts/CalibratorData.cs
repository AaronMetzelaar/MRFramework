using OpenCvSharp;
using System;

/// <summary>
/// Represents the data that is necessary to calibrate the camera.
/// </summary>
[Serializable]
public class CalibratorData
{
    public Point[] Corners;
    public Mat TransformationMatrix;
    public Mat CameraMatrix;
    public Mat DistortionCoefficients;
    public Mat BaseImage;
    public Calibrator.CameraRotationOption CameraRotation;

    public CalibratorData(Point[] corners, Mat transformationMatrix, Mat cameraMatrix, Mat distortionCoefficients, Mat baseImage, Calibrator.CameraRotationOption cameraRotation)
    {
        Corners = corners ?? throw new ArgumentNullException(nameof(corners));
        CameraMatrix = cameraMatrix ?? throw new ArgumentNullException(nameof(cameraMatrix));
        DistortionCoefficients = distortionCoefficients ?? throw new ArgumentNullException(nameof(distortionCoefficients));
        TransformationMatrix = transformationMatrix ?? throw new ArgumentNullException(nameof(transformationMatrix));
        BaseImage = baseImage ?? throw new ArgumentNullException(nameof(baseImage));
        CameraRotation = cameraRotation;
    }
}
