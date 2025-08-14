using UnityEngine;

[RequireComponent(typeof(CharacterController))]
public class PlayerController : MonoBehaviour
{
    // Camera reference (shows in Inspector). Auto-fills if left empty.
    public Transform cam;

    // Look
    public float look = 150f;

    // Move feel (LC-style weight)
    public float maxSpeed = 4.5f;
    public float accel = 12f;
    public float decel = 14f;
    public float airControl = 0.3f;

    // Jump / gravity
    public float jump = 5f;
    public float gravity = -20f;

    // Head-bob
    public float bobFreq = 9f;
    public float bobAmp = 0.04f;

    CharacterController cc;
    float yaw, pitch, vy;
    Vector3 planarVel;
    float bobT;
    Vector3 camRestLocalPos;

    void Awake()
    {
        cc = GetComponent<CharacterController>();

        // Auto-find camera if not assigned
        if (!cam)
        {
            var camComp = GetComponentInChildren<Camera>(true);
            if (camComp) cam = camComp.transform;
        }

        camRestLocalPos = cam ? cam.localPosition : Vector3.zero;
        Cursor.lockState = CursorLockMode.Locked;
    }

    void Update()
    {
        // Look
        float mx = Input.GetAxis("Mouse X") * look * Time.deltaTime;
        float my = Input.GetAxis("Mouse Y") * look * Time.deltaTime;
        yaw += mx;
        pitch = Mathf.Clamp(pitch - my, -80f, 80f);
        transform.eulerAngles = new Vector3(0, yaw, 0);
        if (cam) cam.localEulerAngles = new Vector3(pitch, 0, 0);

        // Input
        Vector2 in2 = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical")).normalized;
        Vector3 wishDir = (transform.right * in2.x + transform.forward * in2.y);
        Vector3 targetPlanar = wishDir * maxSpeed;

        // Accel/decel
        float rate = (targetPlanar.sqrMagnitude > planarVel.sqrMagnitude) ? accel : decel;
        if (cc.isGrounded)
            planarVel = Vector3.MoveTowards(planarVel, targetPlanar, rate * Time.deltaTime);
        else
            planarVel = Vector3.Lerp(planarVel, targetPlanar, airControl * Time.deltaTime);

        // Gravity / jump
        if (cc.isGrounded)
        {
            vy = -2f;
            if (Input.GetButtonDown("Jump")) vy = jump;
        }
        else vy += gravity * Time.deltaTime;

        // Move
        cc.Move((planarVel + Vector3.up * vy) * Time.deltaTime);

        // Head-bob
        bool moving = cc.isGrounded && planarVel.magnitude > 0.2f;
        if (cam)
        {
            if (moving)
            {
                bobT += bobFreq * Time.deltaTime * (planarVel.magnitude / maxSpeed);
                cam.localPosition = camRestLocalPos + new Vector3(0, Mathf.Sin(bobT) * bobAmp, 0);
            }
            else
            {
                bobT = Mathf.Lerp(bobT, 0f, 10f * Time.deltaTime);
                cam.localPosition = Vector3.Lerp(cam.localPosition, camRestLocalPos, 10f * Time.deltaTime);
            }
        }
    }
}
