using UnityEngine;
using UnityEngine.Events;
using Point = OpenCvSharp.Point;

public class DetectedObject : MonoBehaviour
{
    public InitializedObject initializedObject;
    public Vector2 centroidInCanvasSpace;
    public Point[] contour;
    public float rotationAngle;

    public UnityEvent<DetectedObject> onRotationChange;
    private float previousRotationAngle;

    private void Start()
    {
        previousRotationAngle = rotationAngle;
        onRotationChange ??= new UnityEvent<DetectedObject>();
    }

    void Update()
    {
        if (Mathf.Abs(rotationAngle - previousRotationAngle) > 1.0f)
        {
            onRotationChange.Invoke(this);
            previousRotationAngle = rotationAngle;
        }
    }

    public void UpdatePosition(Vector2 centroidInCanvasSpace)
    {
        transform.localPosition = new(centroidInCanvasSpace.x, centroidInCanvasSpace.y, -0.01f);
    }

    public void UpdateRotation(float rotationAngle)
    {
        this.rotationAngle = rotationAngle;
        transform.localRotation = Quaternion.Euler(0, 0, rotationAngle);
    }
}
