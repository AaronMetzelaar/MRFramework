using System;
using OpenCvSharp;
using UnityEngine;

[Serializable]
public class InitializedObject
{
    // Hue of the object with white projected on it
    public float WhiteHue;
    // Hue of the object with the Color projected on it
    public float ColorHue;
    // Color to project on the object
    public Color Color;
    // Contour of the object
    public Point[] Contour;
}
