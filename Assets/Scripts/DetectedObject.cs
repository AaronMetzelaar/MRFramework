using UnityEngine;
using Point = OpenCvSharp.Point;

public class DetectedObject : MonoBehaviour
{
    public InitializedObject initializedObject;
    public Vector2 centroidInCanvasSpace;
    public Point[] contour;

    public DetectedObject(InitializedObject initializedObject, Vector2 centroidInCanvasSpace, Point[] contour)
    {
        this.initializedObject = initializedObject;
        this.centroidInCanvasSpace = centroidInCanvasSpace;
        this.contour = contour;
    }

    public void UpdatePosition(Vector2 centroidInCanvasSpace)
    {
        transform.position = centroidInCanvasSpace;
    }

    public void UpdateRotation(float rotation)
    {
        transform.rotation = Quaternion.Euler(0, 0, rotation);
    }
}
