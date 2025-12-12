using UnityEngine;

public class PuppetRootMover : MonoBehaviour
{
    public PoseReceiver receiver;
    public int personIndex = 0;

    [Header("Tracking")]
    public float lostTimeout = 0.5f; // giây

    [Header("Movement")]
    public float sceneMinX = -6f;
    public float sceneMaxX = 6f;
    public float moveSmooth = 5f;

    [Header("Disappear")]
    public float offscreenLeft = -10f;
    public float offscreenRight = 10f;
    public float disappearSpeed = 2f;

    private Vector3 targetPos;
    private bool hasPerson = false;

    void Update()
    {
        if (receiver == null)
            return;

        bool trackingAlive = (Time.time - receiver.lastReceiveTime) < lostTimeout;

        var person = trackingAlive ? receiver.GetPerson(personIndex) : null;

        if (person != null)
        {
            hasPerson = true;

            float x = Mathf.Lerp(sceneMinX, sceneMaxX, person.center_x);
            targetPos = new Vector3(x, transform.position.y, transform.position.z);
        }
        else
        {
            hasPerson = false;

            float outX = (personIndex == 0) ? offscreenLeft : offscreenRight;
            targetPos = new Vector3(outX, transform.position.y, transform.position.z);
        }

        transform.position = Vector3.Lerp(
            transform.position,
            targetPos,
            Time.deltaTime * (hasPerson ? moveSmooth : disappearSpeed)
        );
    }
}
