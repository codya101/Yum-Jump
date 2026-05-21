using UnityEngine;

public class CameraFollow : MonoBehaviour
{
    public Transform target;
    public Vector3 offset = new Vector3(0, 0, -10);
    public float smoothSpeed = 5f;
    public float lookAheadDistance = 3f;
    public float lookAheadSmoothTime = 0.5f;
    public float minSpeedForLookAhead = 1f;

    private float currentLookAhead;
    private float lookAheadVelocity;

    void LateUpdate()
    {
        if (target == null)
        {
            if (GameManager.instance != null && GameManager.instance.player != null)
                target = GameManager.instance.player.transform;
            return;
        }

        float xVel = target.GetComponent<Rigidbody2D>().linearVelocity.x;
        float targetLookAhead = Mathf.Abs(xVel) < minSpeedForLookAhead
            ? currentLookAhead
            : Mathf.Sign(xVel) * lookAheadDistance;

        currentLookAhead = Mathf.SmoothDamp(currentLookAhead, targetLookAhead, ref lookAheadVelocity, lookAheadSmoothTime);

        Vector3 desiredPosition = target.position + offset + new Vector3(currentLookAhead, 0, 0);
        transform.position = Vector3.Lerp(transform.position, desiredPosition, smoothSpeed * Time.deltaTime);
    }
}
