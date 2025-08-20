using UnityEngine;

[RequireComponent(typeof(CharacterController))]
public class FPController : MonoBehaviour
{
    [Header("References")]
    public Transform cam;

    // ---------------- LOOK ----------------
    [Header("Look")]
    [Range(0f, 100f)] public float mouseSensitivity = 50f; // 50 = baseline
    public float minPitch = -85f, maxPitch = 85f;
    [Header("Mouse Smoothing")]
    public float mouseSmoothTime = 0.03f;
    public float maxMouseStep = 6f;

    // ---------------- MOVE ----------------
    [Header("Move")]
    public float walkSpeed = 4f, sprintSpeed = 6.5f;
    public KeyCode sprintKey = KeyCode.LeftShift;

    [Header("Air Control (Ascending / Falling)")]
    [Range(0f, 1f)] public float airControlUp = 0.35f;
    [Range(0f, 1f)] public float airControlDown = 0.60f;
    public float airSpeedFallMultiplier = 1.10f;

    [Header("Jump Launch")]
    public float jumpLaunchForwardBoost = 1.4f;
    public float jumpLaunchDecay = 6f;

    // ---------------- JUMP WINDOWS ----------------
    [Header("Jump Windows")]
    public float coyoteTime = 0.02f;
    public float jumpBuffer = 0.10f;
    [Header("Jump Cooldown")]
    public float jumpCooldownSeconds = 0.50f;

    // ---------------- GRAVITY / JUMP ----------------
    [Header("Jump / Gravity")]
    public float gravity = -24f;          // lighter up-phase
    public float fallMultiplier = 4.0f;   // slower fall overall (raise for faster)
    public float lowJumpMultiplier = 4.2f;
    public float maxFallSpeed = -120f;

    [Header("Height-Scaled Jump (Vitruvian)")]
    public float headRatio = 0.125f;      // ~1/8 body height
    public float tapHeads = 6.5f;         // tall, dramatic tap
    public float holdExtraHeads = 0.25f;  // tiny bonus when holding
    public float maxHeadsCap = 7.0f;      // hard cap
    [Range(0.5f, 1f)] public float tapGuaranteeFraction = 0.95f;

    [Header("Jump Feel")]
    public float jumpBlendTime = 0.14f;   // slows takeoff a touch
    public AnimationCurve jumpBlendCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);
    public float apexGravityMultiplier = 0.80f; // gentle start of descent
    public float apexThreshold = 0.32f;

    [Header("Jump Timing")]
    public float minAscendTime = 0.18f;   // ensures brief rise
    public float apexHangTime = 0.26f;    // little float at top
    public float hangGravityMultiplier = 0.55f; // lighter during hang

    [Header("Cinematic Tap (rare)")]
    public float dramaticTapChance = 0.00f;  // set >0 if you want spice later
    public float dramaticBonusHeads = 0.20f;
    public float dramaticCooldown = 6f;

    [Header("Ceiling Safety")]
    public float headroomMargin = 0.12f;

    [Header("Jump Vertical Tuning")]
    [Tooltip("Fraction of tap velocity applied instantly at jump start (rest blends).")]
    [Range(0.3f, 1f)] public float jumpImmediateKickFraction = 0.75f; // lower = slower takeoff

    [Header("Jump Arc (Forward)")]
    [Tooltip("Extra forward boost at jump start (m/s).")]
    public float jumpForwardExtra = 2.0f;          // was jumpLaunchForwardBoost ~1.4
    [Tooltip("How much of current ground sideways speed to inherit into the jump (0..1).")]
    [Range(0f, 1f)] public float inheritPlanarFactor = 0.5f; // adds arc from your run-up
    [Tooltip("Decay rate for the extra forward boost (per second).")]
    public float jumpForwardDecay = 3.5f;          // lower = lasts longer
    [Tooltip("Decay rate for inherited ground speed (per second).")]
    public float inheritPlanarDecay = 1.2f;        // slowly bleeds off, keeps arc

    [Header("Jump Arc (Input-Gated)")]
    [Tooltip("Only add forward/inherit if move input magnitude exceeds this.")]
    [Range(0f, 1f)] public float jumpInputDeadZone = 0.15f;

    [Header("Tap vs Hold Airtime")]
    [Tooltip("While rising AND holding jump, gravity is scaled by this (<1 = longer air).")]
    [Range(0.2f, 1.0f)] public float holdAscendGravityScale = 0.65f;
    [Tooltip("Max duration (s) the hold-sustain applies after jump start).")]
    [Range(0.1f, 1.5f)] public float holdSustainTime = 0.80f;
    [Tooltip("If NOT holding (a tap), use this strong gravity multiplier while rising.")]
    [Range(1.5f, 12f)] public float tapAscendCutMultiplier = 8.0f;

    [Header("Sprint Jump Arc")]
    public float sprintArcBoost = 1.25f;     // scales the forward shove
    public float sprintInheritBoost = 1.15f; // scales carried ground speed

    // ---------------- SIZE / CAMERA ----------------
    [Header("Player Dimensions")]
    public float controllerHeight = 2.0f, controllerRadius = 0.28f, cameraHeight = 1.8f, skinWidth = 0.06f;

    // ---------------- HEAD BOB ----------------
    [Header("Head Bob")]
    public bool enableHeadBob = true;
    public float bobFreq = 9.5f, bobAmp = 0.07f, bobStartSmooth = 0.06f;
    public float bobRunFreqMultiplier = 1.25f, backBobAmpMultiplier = 0.7f;

    // ---------------- WALL SLIDE ----------------
    [Header("Wall Slide (optional)")]
    public bool enableWallSlide = true;
    public float wallCheckDistance = 0.5f;
    public LayerMask wallMask = ~0;
    [Range(0.6f, 1.0f)] public float wallProbeRadiusScale = 0.85f;

    // ---------------- GROUND CHECK ----------------
    [Header("Ground Check (auto-managed)")]
    public Transform groundCheck;
    public float groundRadius = 0.2f;
    public LayerMask groundMask = ~0;

    // ---------------- FALL CURVE (NUMERIC) ----------------
    [Header("Fall Curve (Numeric)")]
    public bool useFallCurve = true;
    [Range(0f, 1f)] public float fallStartScale = 0.30f; // scale at t=0 (gentle)
    [Range(0f, 1f)] public float fallMidT = 0.50f; // time for middle key
    [Range(0f, 1f)] public float fallMidScale = 0.65f; // scale at middle key
    public float fallCurveTime = 1.0f;                 // seconds to reach full fall

    // -------- Internals --------
    CharacterController cc;
    Vector3 velocityY; float pitch;
    bool groundedStrict, wasGroundedPrev, hasJumpedSinceGrounded;
    float lastGroundedTime = -999f, lastJumpPressedTime = -999f, lastJumpStartTime = -999f;
    float vTap, vMax, tapHeightW, maxHeightW, jumpStartY, jumpStartTime = -999f;
    bool blendingJump; float lastDramaticTime = -999f;
    Vector2 mouseVel, smoothMouse; float mouseX, mouseY;
    float bobT, moveBlend, moveBlendVel; Vector3 camRestLocalPos;
    Vector3 jumpLaunchVel = Vector3.zero;
    float fallStartTime = -999f, prevVelY = 0f;
    Vector3 _inheritVel = Vector3.zero;   // preserved portion of ground speed at jump start

    AnimationCurve fallAccelCurve;

    void Awake()
    {
        cc = GetComponent<CharacterController>();
        EnsureGroundCheck();
        ApplyControllerDimensions();
        RebuildFallCurve();
        
        // Auto-find camera if not set
        if (!cam)
        {
            // Try children first, then parent's children (for complex hierarchies)
            cam = GetComponentInChildren<Camera>()?.transform;
            if (!cam && transform.parent)
            {
                cam = transform.parent.GetComponentInChildren<Camera>()?.transform;
            }
        }
    }
    void Start()
    {
        if (cam)
        {
            var p = cam.localPosition; p.y = cameraHeight; cam.localPosition = p;
            camRestLocalPos = cam.localPosition;
        }
        Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false;
    }
    void OnValidate()
    {
        if (!cc) cc = GetComponent<CharacterController>();
        ApplyControllerDimensions();
        RebuildFallCurve();
    }

    void EnsureGroundCheck()
    {
        if (!groundCheck)
        {
            var t = new GameObject("GroundCheck").transform;
            t.SetParent(transform);
            groundCheck = t;
        }
    }
    

    
    [ContextMenu("Fix Missing References")]
    void FixMissingReferences()
    {
        if (!cc) cc = GetComponent<CharacterController>();
        if (!cam) cam = GetComponentInChildren<Camera>()?.transform;
        EnsureGroundCheck();
        ApplyControllerDimensions();
        Debug.Log("FPController: References checked and fixed");
        

    }
    

    void ApplyControllerDimensions()
    {
        if (!cc) return;
        cc.height = Mathf.Max(1.0f, controllerHeight);
        cc.radius = Mathf.Clamp(controllerRadius, 0.15f, 0.6f);
        cc.center = new Vector3(0f, cc.height * 0.5f, 0f);
        cc.skinWidth = Mathf.Clamp(skinWidth, 0.02f, 0.1f);
        if (groundCheck) groundCheck.localPosition = new Vector3(0, -cc.height * 0.5f + 0.06f, 0);
    }

    void RebuildFallCurve()
    {
        var k0 = new Keyframe(0f, Mathf.Clamp01(fallStartScale));
        var k1 = new Keyframe(Mathf.Clamp01(fallMidT), Mathf.Clamp01(fallMidScale));
        var k2 = new Keyframe(1f, 1f);
        fallAccelCurve = new AnimationCurve(k0, k1, k2);
        fallAccelCurve.SmoothTangents(0, 0f);
        fallAccelCurve.SmoothTangents(1, 0f);
        fallAccelCurve.SmoothTangents(2, 0f);
    }

    void Update()
    {
        if (!cc || !cam) return;

        // --- Mouse ---
        float sens = mouseSensitivity / 50f;
        float rawX = Input.GetAxisRaw("Mouse X") * sens;
        float rawY = Input.GetAxisRaw("Mouse Y") * sens;
        smoothMouse.x = Mathf.SmoothDamp(smoothMouse.x, rawX, ref mouseVel.x, mouseSmoothTime);
        smoothMouse.y = Mathf.SmoothDamp(smoothMouse.y, rawY, ref mouseVel.y, mouseSmoothTime);
        mouseX = Mathf.Clamp(smoothMouse.x, -maxMouseStep, maxMouseStep);
        mouseY = Mathf.Clamp(smoothMouse.y, -maxMouseStep, maxMouseStep);

        // --- Input ---
        float h = Input.GetAxisRaw("Horizontal"), v = Input.GetAxisRaw("Vertical");
        Vector3 input = new Vector3(h, 0f, v); if (input.sqrMagnitude > 1f) input.Normalize();
        float targetSpeed = Input.GetKey(sprintKey) ? sprintSpeed : walkSpeed;

        // --- Grounding (Simple & Reliable) ---
        groundedStrict = cc.isGrounded;
        if (!groundedStrict)
        {
            // Backup check with raycast
            groundedStrict = Physics.Raycast(transform.position + Vector3.up * 0.1f, Vector3.down, 0.2f, ~0, QueryTriggerInteraction.Ignore);
        }

        if (groundedStrict)
        {
            lastGroundedTime = Time.time;
            hasJumpedSinceGrounded = false; // Simple: reset every frame when grounded
            
            if (!wasGroundedPrev)
            {
                _inheritVel = Vector3.zero;
                jumpLaunchVel = Vector3.zero;
            }

            if (velocityY.y < 0f && !blendingJump) velocityY.y = -2f;
        }
        wasGroundedPrev = groundedStrict;


        // Jump input detection (supports both Jump button and Space key)
        if (Input.GetButtonDown("Jump") || Input.GetKeyDown(KeyCode.Space)) 
        {
            lastJumpPressedTime = Time.time;
        }

        // --- Height targets (heads -> meters) ---
        float headH = Mathf.Max(0.1f, controllerHeight * headRatio);
        float tapH = tapHeads * headH;
        float maxH = Mathf.Min(tapH + Mathf.Max(0f, holdExtraHeads) * headH, maxHeadsCap * headH);

        // cinematic tap (optional)
        bool canDramatic = (Time.time - lastDramaticTime) >= dramaticCooldown;
        bool rollDramatic = dramaticTapChance > 0f && canDramatic && Random.value < Mathf.Clamp01(dramaticTapChance);
        if (rollDramatic) { float bonus = Mathf.Max(0f, dramaticBonusHeads) * headH; tapH += bonus; maxH += bonus; }

        // ceiling safety
        float headroom = GetHeadroomMeters();
        if (headroom > 0f)
        {
            float safe = Mathf.Max(0f, headroom - headroomMargin);
            tapH = Mathf.Min(tapH, safe); maxH = Mathf.Min(maxH, safe);
        }

        // velocities for those heights
        float gMag = Mathf.Abs(gravity);
        vTap = Mathf.Sqrt(2f * gMag * Mathf.Max(0.01f, tapH));
        vMax = Mathf.Sqrt(2f * gMag * Mathf.Max(0.01f, maxH));

        // jump gating (cooldown + coyote + buffer)
        bool cooldownReady = (Time.time - lastJumpStartTime) >= jumpCooldownSeconds;
        bool canCoyote = (Time.time - lastGroundedTime) <= coyoteTime;
        bool hasBuffer = (Time.time - lastJumpPressedTime) <= jumpBuffer;
        bool canJumpNow = cooldownReady && canCoyote && hasBuffer && !hasJumpedSinceGrounded;
        


        if (canJumpNow)
        {
            jumpStartY = transform.position.y;

            // Vertical: solid tap velocity (rest blends up if holding)
            if (velocityY.y < vTap) velocityY.y = vTap;

            // Forward arc ONLY if there's actual input (W/A/S/D or diagonals)
            float ix = Input.GetAxisRaw("Horizontal");
            float iz = Input.GetAxisRaw("Vertical");
            Vector2 in2 = new Vector2(ix, iz);
            if (in2.sqrMagnitude > (jumpInputDeadZone * jumpInputDeadZone))
            {
                // inherit current ground momentum
                Vector3 planarPre = cc.velocity; planarPre.y = 0f;
                _inheritVel = planarPre * Mathf.Clamp01(inheritPlanarFactor);

                // plus a small directed boost using YOUR field jumpLaunchForwardBoost
                Vector3 wish = new Vector3(ix, 0f, iz).normalized;
                wish = transform.TransformDirection(wish);
                jumpLaunchVel = wish * Mathf.Max(0f, jumpLaunchForwardBoost);
            }
            else
            {
                // No input: straight up
                _inheritVel = Vector3.zero;
                jumpLaunchVel = Vector3.zero;

                // If sprinting, increase the forward arc
                if (Input.GetKey(sprintKey))
                {
                    jumpLaunchVel *= sprintArcBoost;
                    _inheritVel *= sprintInheritBoost;
                }
            }

            // bookkeeping you already had
            jumpStartTime = Time.time;
            blendingJump = true;
            tapHeightW = tapH; maxHeightW = maxH;
            hasJumpedSinceGrounded = true;
            lastJumpStartTime = Time.time;
            if (rollDramatic) lastDramaticTime = Time.time;
        }




        // --- Gravity (clear ascend vs fall + apex hang + fall curve) ---
        float g = gravity;
        float timeSinceJump = Time.time - jumpStartTime;
        bool nearApex = Mathf.Abs(velocityY.y) < apexThreshold;

        if (nearApex && timeSinceJump > 0.02f && timeSinceJump < (apexHangTime + 0.02f))
        {
            g *= hangGravityMultiplier; // gentle float
        }
        else if (nearApex)
        {
            g *= apexGravityMultiplier;
        }

        if (velocityY.y < 0f)
        {
            float scale = 1f;
            if (useFallCurve)
            {
                if (prevVelY > 0f && velocityY.y <= 0f) fallStartTime = Time.time; // just hit apex
                float t = Mathf.Clamp01((Time.time - fallStartTime) / Mathf.Max(0.01f, fallCurveTime));
                scale = fallAccelCurve.Evaluate(t); // 0.30 -> 1.0 default
            }
            g *= fallMultiplier * scale;   // falling branch ends here
        }                                   // <--- this brace is the one that was missing
        else
        {
            // --- Rising: Tap vs Hold airtime control ---
            float tSinceJump = Time.time - jumpStartTime;
            bool holding = Input.GetButton("Jump");

            if (holding && tSinceJump <= holdSustainTime)
            {
                g *= Mathf.Clamp(holdAscendGravityScale, 0.2f, 1f);   // e.g., 0.65
            }
            else
            {
                g *= tapAscendCutMultiplier;                           // e.g., 8.0
            }
        }





        // blend towards vMax while holding
        if (blendingJump)
        {
            float t = (jumpBlendTime > 0f) ? Mathf.Clamp01((Time.time - jumpStartTime) / jumpBlendTime) : 1f;
            float targetUp = vMax * jumpBlendCurve.Evaluate(t);
            if (Input.GetButton("Jump") && velocityY.y < targetUp) velocityY.y = targetUp;
            if (t >= 1f || !Input.GetButton("Jump")) blendingJump = false;
        }

        velocityY.y += g * Time.deltaTime;
        if (velocityY.y < maxFallSpeed) velocityY.y = maxFallSpeed;

        float asc = transform.position.y - jumpStartY;
        if (asc >= maxHeightW && velocityY.y > 0f) { velocityY.y = 0f; blendingJump = false; }

        prevVelY = velocityY.y;

            // Decay extra forward push (uses your jumpLaunchDecay)
            if (jumpLaunchVel.sqrMagnitude > 0f)
            {
                float kExtra = Mathf.Exp(-jumpLaunchDecay * Time.deltaTime);
                jumpLaunchVel *= kExtra;
            }

            // Decay inherited ground momentum (if you already have inheritPlanarDecay, keep using it;
            // otherwise you can reuse jumpLaunchDecay here, or add a new public float inheritPlanarDecay = 1.1f;)
            if (_inheritVel.sqrMagnitude > 0f)
            {
                float kInherit = Mathf.Exp(-(inheritPlanarDecay) * Time.deltaTime); // or jumpLaunchDecay if you prefer one knob
                _inheritVel *= kInherit;
            }




            // --- Lateral movement (up vs down control) ---
            bool ascending = velocityY.y > 0f, descending = velocityY.y <= 0f;
        float control = groundedStrict ? 1f : (ascending ? Mathf.Clamp01(airControlUp) : Mathf.Clamp01(airControlDown));
        float speedMult = (groundedStrict || ascending) ? 1f : airSpeedFallMultiplier;
        Vector3 lateral = transform.TransformDirection(input) * targetSpeed * control * speedMult;

        // wall slide
        if (enableWallSlide && lateral.sqrMagnitude > 0.0001f)
        {
            Vector3 origin = transform.position + Vector3.up * (cc.height * 0.5f);
            float probe = Mathf.Clamp01(wallProbeRadiusScale) * cc.radius;
            if (Physics.SphereCast(origin, probe, lateral.normalized, out RaycastHit hit, wallCheckDistance, wallMask, QueryTriggerInteraction.Ignore))
            {
                if (Vector3.Dot(lateral, hit.normal) < 0f) lateral = Vector3.ProjectOnPlane(lateral, hit.normal);
            }
        }

        Vector3 airborneExtra = groundedStrict ? Vector3.zero : (_inheritVel + jumpLaunchVel);
        Vector3 displacement = (lateral + airborneExtra + velocityY) * Time.deltaTime;
        cc.Move(displacement);

    }

    void LateUpdate()
    {
        if (!cam) return;

        // yaw body, pitch cam
        transform.Rotate(Vector3.up * mouseX);
        pitch = Mathf.Clamp(pitch - mouseY, minPitch, maxPitch);
        cam.localRotation = Quaternion.Euler(pitch, 0f, 0f);

        // head-bob: ground only; forward/back; instant stop; sprint -> freq only
        if (enableHeadBob)
        {
            float vAxis = Input.GetAxisRaw("Vertical");
            bool movingF = vAxis > 0.01f, movingB = vAxis < -0.01f;
            Vector3 planar = cc.velocity; planar.y = 0f;
            float maxS = Mathf.Max(walkSpeed, sprintSpeed);
            float speed01 = Mathf.Clamp01(planar.magnitude / Mathf.Max(0.01f, maxS));
            bool shouldBob = groundedStrict && (movingF || movingB) && speed01 > 0.01f;



            if (shouldBob)
            {
                moveBlend = Mathf.SmoothDamp(moveBlend, 1f, ref moveBlendVel, Mathf.Max(0.005f, bobStartSmooth));
                float freq = bobFreq * Mathf.Lerp(1f, bobRunFreqMultiplier, speed01);
                bobT += freq * Time.deltaTime;
                float ampMul = movingB ? backBobAmpMultiplier : 1f;
                float bobY = Mathf.Sin(bobT) * bobAmp * ampMul * moveBlend;
                cam.localPosition = camRestLocalPos + new Vector3(0f, bobY, 0f);
            }
            else
            {
                moveBlend = 0f; bobT = 0f; cam.localPosition = camRestLocalPos;
            }
        }
        else cam.localPosition = camRestLocalPos;
    }

    float GetHeadroomMeters()
    {
        float headY = transform.position.y + cc.height * 0.5f;
        Vector3 origin = new Vector3(transform.position.x, headY, transform.position.z);
        float radius = Mathf.Max(0.05f, cc.radius * 0.5f), maxCheck = 8f;
        if (Physics.SphereCast(origin, radius, Vector3.up, out RaycastHit hit, maxCheck, wallMask, QueryTriggerInteraction.Ignore))
            return hit.distance;
        return Mathf.Infinity;
    }
}
