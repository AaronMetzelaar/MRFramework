using OpenCvSharp;
using System;

[Serializable]
public class CalibratorData
{
    public Point[] Corners;
    public Mat TransformationMatrix;

    public CalibratorData(Point[] corners, Mat transformationMatrix)
    {
        Corners = corners ?? throw new ArgumentNullException(nameof(corners));
        TransformationMatrix = transformationMatrix ?? throw new ArgumentNullException(nameof(transformationMatrix));
    }
}
