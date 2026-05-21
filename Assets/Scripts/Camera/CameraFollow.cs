using UnityEngine;

public class CameraFollow : MonoBehaviour
{
    public Transform target;
    public Vector3 offset = new Vector3(0, 0, -10);
    public float smoothSpeed = 5f;
    public float lookAheadDistance = 3f;
    public float lookAheadSmoothTime = 0.5f;
    public float minSpeedForLookAhead = 1f;

    [Header("Pixel Perfect")]
    public bool pixelSnap = true;
    public int pixelsPerUnit = 16;

    private float currentLookAhead;
    private float lookAheadVelocity;
    private Vector3 smoothPosition;

    void Start()
    {
        smoothPosition = transform.position;
    }

    void LateUpdate()
    {
        if (target == null)
        {
            if (GameManager.instance != null && GameManager.instance.player != null)
                target = GameManager.instance.player.transform;
            return;
        }

        // While the game is paused (e.g. the level-complete popup sets
        // Time.timeScale = 0), Time.deltaTime is 0. SmoothDamp and Lerp with a
        // zero deltaTime produce NaN, which permanently corrupts the camera
        // position. Nothing needs to move while paused, so hold still.
        if (Time.deltaTime == 0f)
            return;

        float xVel = target.GetComponent<Rigidbody2D>().linearVelocity.x;
        float targetLookAhead = Mathf.Abs(xVel) < minSpeedForLookAhead
            ? currentLookAhead
            : Mathf.Sign(xVel) * lookAheadDistance;

        currentLookAhead = Mathf.SmoothDamp(currentLookAhead, targetLookAhead, ref lookAheadVelocity, lookAheadSmoothTime);

        Vector3 desiredPosition = target.position + offset + new Vector3(currentLookAhead, 0, 0);
        smoothPosition = Vector3.Lerp(smoothPosition, desiredPosition, smoothSpeed * Time.deltaTime);

        Vector3 finalPosition = smoothPosition;
        if (pixelSnap && pixelsPerUnit > 0)
        {
            float unitsPerPixel = 1f / pixelsPerUnit;
            finalPosition.x = Mathf.Round(finalPosition.x / unitsPerPixel) * unitsPerPixel;
            finalPosition.y = Mathf.Round(finalPosition.y / unitsPerPixel) * unitsPerPixel;
        }
        transform.position = finalPosition;
    }
}
