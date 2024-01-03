using OpenCvSharp;
using System;

[Serializable]
public class CalibratorData
{
    public Point[] Corners;
    public Mat TransformationMatrix;
    public Mat CalibratedImage;
    public Calibrator.CameraRotationOption CameraRotation;

    public CalibratorData(Point[] corners, Mat transformationMatrix, Mat calibratedImage, Calibrator.CameraRotationOption cameraRotation)
    {
        Corners = corners ?? throw new ArgumentNullException(nameof(corners));
        TransformationMatrix = transformationMatrix ?? throw new ArgumentNullException(nameof(transformationMatrix));
        CalibratedImage = calibratedImage ?? throw new ArgumentNullException(nameof(calibratedImage));
        CameraRotation = cameraRotation;
    }
}
