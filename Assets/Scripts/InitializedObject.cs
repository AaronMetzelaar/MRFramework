using System;
using OpenCvSharp;
using UnityEngine;

[Serializable]
public class InitializedObject
{
    // Original hue of the object
    public float ObjectHue;
    // Color to project on the object
    public Color Color;
    // Contour of the object
    public Point[] Contour;
}
