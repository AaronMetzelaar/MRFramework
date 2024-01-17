using UnityEngine;
using Point = OpenCvSharp.Point;

public class DetectedObject : MonoBehaviour
{
    public InitializedObject initializedObject;
    public Vector2 centroidInCanvasSpace;
    public Point[] contour;
    public float rotationAngle;

    public void UpdatePosition(Vector2 centroidInCanvasSpace)
    {
        transform.localPosition = new(centroidInCanvasSpace.x, centroidInCanvasSpace.y, -0.01f);
    }

    public void UpdateRotation(float rotationAngle)
    {
        transform.localRotation = Quaternion.Euler(0, 0, rotationAngle);
    }
}
