using System;
using OpenCvSharp;

[Serializable]
public class InitiatedObject
{
    public float Hue;
    public Point[] Contour;

    public bool IsDetected { get; set; } = false;
}
