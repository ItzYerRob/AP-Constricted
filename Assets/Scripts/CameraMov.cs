using UnityEngine;

public class CameraFollow : MonoBehaviour
{
    public Transform player;
    public Vector3 offset = new Vector3(0f, 2f, -4f);
    public float sensitivity = 5f;
    public float minY = -60f;
    public float maxY = 80f;
    public float smoothSpeed = 10f;
    public float collisionRadius = 0.3f;
    public float collisionBuffer = 0.2f;
    public LayerMask collisionLayers;

    private float currentRotationX = 0f;
    void LateUpdate()
    {
        float mouseY = Input.GetAxis("Mouse Y") * sensitivity;
        currentRotationX -= mouseY;
        currentRotationX = Mathf.Clamp(currentRotationX, minY, maxY);

        Quaternion rotation = Quaternion.Euler(currentRotationX, player.eulerAngles.y, 0f);
        transform.rotation = rotation;
    }
}