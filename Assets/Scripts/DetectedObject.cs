using UnityEngine;
using UnityEngine.Events;
using Point = OpenCvSharp.Point;

/// <summary>
/// Represents a detected object in the scene.
/// </summary>
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

    /// <summary>
    /// Updates the position of the detected object in canvas space.
    /// </summary>
    /// <param name="centroidInCanvasSpace">The centroid position of the object in canvas space.</param>
    public void UpdatePosition(Vector2 centroidInCanvasSpace)
    {
        transform.localPosition = new(centroidInCanvasSpace.x, centroidInCanvasSpace.y, -0.01f);
    }

    /// <summary>
    /// Updates the rotation of the object.
    /// </summary>
    /// <param name="rotationAngle">The new rotation angle in degrees.</param>
    public void UpdateRotation(float rotationAngle)
    {
        this.rotationAngle = rotationAngle;
        transform.localRotation = Quaternion.Euler(0, 0, rotationAngle);
    }
}
