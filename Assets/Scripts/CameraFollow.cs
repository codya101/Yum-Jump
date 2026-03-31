using UnityEngine;

public class CameraFollow : MonoBehaviour
{
    public Transform target;
    public Vector3 offset = new Vector3(0, 0, -10);
    public float smoothSpeed = 5f;
    public float lookAheadDistance = 3f;

    void LateUpdate()
    {
        if (target == null)
        {
            if (GameManager.instance != null && GameManager.instance.player != null)
                target = GameManager.instance.player.transform;
            return;
        }

        float direction = Mathf.Sign(target.GetComponent<Rigidbody2D>().linearVelocity.x);
        Vector3 lookAhead = new(direction * lookAheadDistance, 0, 0);

        Vector3 desiredPosition = target.position + offset + lookAhead;
        transform.position = Vector3.Lerp(transform.position, desiredPosition, smoothSpeed * Time.deltaTime);
    }
}
