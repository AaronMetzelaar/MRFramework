using UnityEngine;

public class DetectedObject : MonoBehaviour
{
    public InitializedObject initializedObject;
    public Vector2 centroidInCanvasSpace;
    public float rotationAngle;

    public DetectedObject(InitializedObject initializedObject, Vector2 centroidInCanvasSpace, float rotationAngle)
    {
        this.initializedObject = initializedObject;
        this.centroidInCanvasSpace = centroidInCanvasSpace;
        this.rotationAngle = rotationAngle;
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
