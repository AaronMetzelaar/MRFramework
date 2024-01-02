using OpenCvSharp;
using UnityEngine;

public class InitiatedObject : MonoBehaviour
{
    public float Hue;
    public Point[] Contour;

    public bool IsDetected { get; set; } = false;
}
