using System;
using OpenCvSharp;
using UnityEngine;

/// <summary>
/// Represents an initialized object in the scene.
/// </summary>
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
    // Name of the object, defaults to "Object"
    public string Name = "Object";
    // Whether to check for the color of the object
    public bool CheckColor = true;
}
