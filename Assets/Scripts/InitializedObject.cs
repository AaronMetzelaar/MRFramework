using System;
using OpenCvSharp;
using UnityEngine;

/// <summary>
/// Represents an initialized object in the scene.
/// </summary>
[Serializable]
public class InitializedObject
{
    /// <summary>
    /// Hue of the object with white projected on it.
    /// </summary>
    public float WhiteHue;

    /// <summary>
    /// Hue of the object with the Color projected on it.
    /// </summary>
    public float ColorHue;

    /// <summary>
    /// Color to project on the object.
    /// </summary>
    public Color Color;

    /// <summary>
    /// Contour of the object.
    /// </summary>
    public Point[] Contour;

    /// <summary>
    /// Name of the object, defaults to "Object".
    /// </summary>
    public string Name = "Object";

    /// <summary>
    /// Whether to check for the color of the object.
    /// </summary>
    public bool CheckColor = true;
}
