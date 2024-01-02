using UnityEngine;

public class DetectedObject : MonoBehaviour
{
    public InitiatedObject initiatedObject;
    public Vector2 centroidInCanvasSpace;
    // public Quaternion rotation;

    public DetectedObject(InitiatedObject initiatedObject, Vector2 centroidInCanvasSpace)
    {
        this.initiatedObject = initiatedObject;
        this.centroidInCanvasSpace = centroidInCanvasSpace;
    }

    public void UpdatePosition(Vector2 centroidInCanvasSpace)
    {
        transform.position = centroidInCanvasSpace;
    }

    // public void UpdateRotation(float rotation)
    // {
    //     transform.rotation = Quaternion.Euler(0, 0, rotation);
    // }
}
