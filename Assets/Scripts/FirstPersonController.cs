using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

[RequireComponent(typeof(CharacterController))]
public class FirstPersonController : MonoBehaviour
{
    [Header("Kamera & Bakış")]
    public Transform cameraRoot;
    [Range(0.2f, 8f)] public float mouseSensitivity = 2.0f;
    public float maxLookAngle = 86f;
    public bool lockCursorOnStart = true;

    [Header("Hareket Hızları (m/sn)")]
    public float walkSpeed = 4.8f;
    public float sprintSpeed = 8.4f;
    public float crouchSpeed = 2.5f;
    public float airControl = 0.35f;
    public float acceleration = 14f;

    [Header("Zıplama & Yerçekimi")]
    public float jumpHeight = 1.35f;
    public float gravity = -20.5f;
    public float coyoteTime = 0.12f;
    public float jumpBufferTime = 0.12f;

    [Header("Eğilme (Left Ctrl basılı tut / C aç-kapa)")]
    public float standingHeight = 1.82f;
    public float crouchHeight = 1.05f;
    public float standingCamY = 1.64f;
    public float crouchCamY = 0.90f;
    public float crouchTransitionSpeed = 12f;

    [Header("Kamera Efektleri")]
    public bool enableHeadBob = true;
    public float walkBobFrequency = 9.5f;
    public float walkBobAmplitude = 0.045f;
    public float sprintBobFrequency = 13.5f;
    public float sprintBobAmplitude = 0.075f;
    public float normalFov = 65f;
    public float sprintFov = 72f;

    [Header("Nişangah (Crosshair) & Etkileşim")]
    public bool showCrosshair = true;
    public float crosshairSize = 5f;
    public float crosshairHoverSize = 9f;
    public float interactDistance = 3.2f;

    [Header("Sürüş Modu (Enter: Bin/İn | Q veya Sağ Tık: İleri Uzan)")]
    public bool isInDrivingMode = false;

    private CharacterController cc;
    private Camera playerCam;
    private Rigidbody rb;
    private Collider[] extraColliders;

    private float pitch = 0f;
    private float seatedYaw = 0f;
    private Vector3 horizontalVelocity;
    private float verticalVelocity;
    private float coyoteCounter;
    private float jumpBufferCounter;

    private bool crouchToggleState = false;
    private bool isCrouching = false;
    private float currentCamY;
    private float bobTimer = 0f;
    private bool cursorLocked = true;

    // My Summer Car drag-door & vehicle seat state
    private HingeInteractable hoveredHinge;
    private HingeInteractable grabbedHinge;
    private Vector3 hoveredHitPoint;
    private VehicleSeat hoveredSeat;
    private VehicleSeat activeSeat;
    private Vector3 seatedBaseCamLocalPos;

    // HUD crosshair & status toast
    private Texture2D dotTexture;
    private float curCrosshairSize;
    private string statusMessage;
    private float statusMessageTimer;
    private GUIStyle hintStyle;

    private void Awake()
    {
        cc = GetComponent<CharacterController>();
        rb = GetComponent<Rigidbody>();
        extraColliders = GetComponents<Collider>();

        if (cameraRoot == null)
        {
            Transform t = transform.Find("CameraRoot");
            if (t != null) cameraRoot = t;
        }
        playerCam = cameraRoot != null ? cameraRoot.GetComponentInChildren<Camera>() : GetComponentInChildren<Camera>();

        currentCamY = standingCamY;
        curCrosshairSize = crosshairSize;
        BuildDotTexture();
    }

    private void OnDestroy()
    {
        if (dotTexture != null) Destroy(dotTexture);
    }

    private void Start()
    {
        if (lockCursorOnStart)
            SetCursorLock(true);
    }

    private void Update()
    {
        if (statusMessageTimer > 0f)
            statusMessageTimer -= Time.deltaTime;

        HandleCursorLock();

        if (cursorLocked)
        {
            HandleInteractRaycastAndDrag();
            HandleVehicleEnterExit();

            if (grabbedHinge == null)
                HandleMouseLook();
        }
        else if (grabbedHinge != null)
        {
            grabbedHinge.EndGrab();
            grabbedHinge = null;
        }

        if (isInDrivingMode && activeSeat != null)
        {
            HandleSeatedCameraAndLean();
        }
        else
        {
            HandleCrouch();
            HandleMovementAndJump();
            HandleHeadBobAndFov();
        }

        bool isHovering = (grabbedHinge != null || hoveredHinge != null || hoveredSeat != null);
        float targetDot = isHovering ? crosshairHoverSize : crosshairSize;
        curCrosshairSize = Mathf.Lerp(curCrosshairSize, targetDot, Mathf.Clamp01(Time.deltaTime * 16f));
    }

    private void HandleCursorLock()
    {
        if (GetKeyDownEsc())
            SetCursorLock(false);
        else if (!cursorLocked && GetMouseLeftDown())
            SetCursorLock(true);
    }

    private void SetCursorLock(bool locked)
    {
        cursorLocked = locked;
        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !locked;
    }

    private void HandleMouseLook()
    {
        Vector2 delta = GetMouseDelta();
        float mx = delta.x * mouseSensitivity;
        float my = delta.y * mouseSensitivity;

        pitch = Mathf.Clamp(pitch - my, -maxLookAngle, maxLookAngle);

        if (isInDrivingMode)
        {
            // Unrestricted free 360-degree head rotation inside vehicle while body stays locked to seat!
            seatedYaw += mx;
            if (cameraRoot != null)
                cameraRoot.localRotation = Quaternion.Euler(pitch, seatedYaw, 0f);
        }
        else
        {
            transform.Rotate(Vector3.up * mx);
            if (cameraRoot != null)
                cameraRoot.localRotation = Quaternion.Euler(pitch, 0f, 0f);
        }
    }

    private void HandleInteractRaycastAndDrag()
    {
        if (playerCam == null) return;

        // Active door drag (works both on foot AND while seated inside a vehicle!)
        if (grabbedHinge != null)
        {
            float dist = Vector3.Distance(playerCam.transform.position, grabbedHinge.transform.position);
            if (!GetMouseLeftHeld() || dist > interactDistance + 1.5f)
            {
                grabbedHinge.EndGrab();
                grabbedHinge = null;
            }
            else
            {
                grabbedHinge.ApplyDrag(GetMouseDelta(), playerCam.transform);
            }
            return;
        }

        hoveredHinge = null;
        hoveredSeat  = null;

        Ray ray = new Ray(playerCam.transform.position, playerCam.transform.forward);
        if (Physics.Raycast(ray, out RaycastHit hit, interactDistance, ~0, QueryTriggerInteraction.Collide))
        {
            if (hit.collider.transform.IsChildOf(transform)) return;

            HingeInteractable hi = hit.collider.GetComponentInParent<HingeInteractable>();
            if (hi != null)
            {
                hoveredHinge = hi;
                hoveredHitPoint = hit.point;
                if (GetMouseLeftDown())
                {
                    grabbedHinge = hoveredHinge;
                    grabbedHinge.BeginGrab(hoveredHitPoint);
                }
                return;
            }

            if (!isInDrivingMode)
            {
                VehicleSeat seat = hit.collider.GetComponentInParent<VehicleSeat>();
                if (seat != null && !seat.IsOccupied)
                    hoveredSeat = seat;
            }
        }
    }

    // =========================================================================
    // MY SUMMER CAR DRIVING MODE: ENTER, SEAT ANCHOR, LEAN/REACH, SAFE EXIT
    // =========================================================================
    private void HandleVehicleEnterExit()
    {
        if (!GetEnterVehicleDown()) return;

        if (!isInDrivingMode)
        {
            if (hoveredSeat != null && !hoveredSeat.IsOccupied)
                EnterDrivingMode(hoveredSeat);
        }
        else if (activeSeat != null)
        {
            if (activeSeat.CanExit(out string reason))
            {
                ExitDrivingMode();
            }
            else if (!string.IsNullOrEmpty(reason))
            {
                ShowStatus(reason, 2.2f);
            }
        }
    }

    public void EnterDrivingMode(VehicleSeat seat)
    {
        if (seat == null) return;

        activeSeat = seat;
        isInDrivingMode = true;
        hoveredSeat = null;

        // 1. Disable CharacterController, Rigidbody, and capsule colliders
        cc.enabled = false;
        if (rb != null)
        {
            rb.isKinematic = true;
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }
        for (int i = 0; i < extraColliders.Length; i++)
        {
            if (extraColliders[i] != null && extraColliders[i] != cc)
                extraColliders[i].enabled = false;
        }

        horizontalVelocity = Vector3.zero;
        verticalVelocity = 0f;

        if (seat.lockAtCurrentPosition)
        {
            // MY SUMMER CAR STYLE: Keep player's EXACT current position, height & head orientation relative to vehicle!
            Transform attachParent = seat.GetAttachParent();
            transform.SetParent(attachParent, true);

            // Preserve current relative yaw without any jarring camera snap
            seatedYaw = 0f;
            seatedBaseCamLocalPos = cameraRoot != null ? cameraRoot.localPosition : new Vector3(0f, currentCamY, 0f);
        }
        else if (seat.seatAnchor != null)
        {
            transform.SetParent(seat.seatAnchor, false);
            transform.localPosition = Vector3.zero;
            transform.localRotation = Quaternion.identity;
            seatedYaw = 0f;
            pitch = 4f;
            seatedBaseCamLocalPos = Vector3.zero;
            if (cameraRoot != null)
            {
                cameraRoot.localPosition = seatedBaseCamLocalPos;
                cameraRoot.localRotation = Quaternion.Euler(pitch, seatedYaw, 0f);
            }
        }

        seat.OnDriverEnter(this);
        ShowStatus(seat.vehicleName + " — Sürüş Modu (WASD: Sür | Q / Sağ Tık: Uzan | Enter: Çık)", 3.5f);
    }

    private void HandleSeatedCameraAndLean()
    {
        if (cameraRoot == null || activeSeat == null) return;

        // Lean / Reach mechanic: hold Q or Right Click to smoothly lean forward from your exact seated spot!
        bool leaning = GetLeanHeld();
        Vector3 targetPos = seatedBaseCamLocalPos;

        if (leaning)
        {
            Vector3 lookFlat = Quaternion.Euler(0f, seatedYaw, 0f) * Vector3.forward;
            targetPos = seatedBaseCamLocalPos + new Vector3(0f, activeSeat.leanForwardOffset.y, 0f) + lookFlat * activeSeat.leanForwardOffset.z;
        }

        cameraRoot.localPosition = Vector3.Lerp(cameraRoot.localPosition, targetPos, Mathf.Clamp01(Time.deltaTime * activeSeat.leanSmoothSpeed));

        if (playerCam != null)
            playerCam.fieldOfView = Mathf.Lerp(playerCam.fieldOfView, leaning ? normalFov - 6f : normalFov, Mathf.Clamp01(Time.deltaTime * 8f));
    }

    public void ExitDrivingMode()
    {
        if (activeSeat == null) return;

        // Keep exact current world yaw & position (My Summer Car style) unless teleportToExitPoint is enabled
        float exitYaw = cameraRoot != null ? cameraRoot.eulerAngles.y : transform.eulerAngles.y;
        Vector3 exitPos = transform.position;

        if (activeSeat.teleportToExitPoint && activeSeat.exitPoint != null)
        {
            exitPos = activeSeat.exitPoint.position;
            exitYaw = activeSeat.exitPoint.eulerAngles.y;
        }

        // 1. Detach from vehicle completely (zero vehicle velocity transferred!)
        transform.SetParent(null, true);
        transform.position = exitPos;
        transform.rotation = Quaternion.Euler(0f, exitYaw, 0f);

        horizontalVelocity = Vector3.zero;
        verticalVelocity = 0f;
        seatedYaw = 0f;

        // 2. Keep smooth standing/crouching posture cleanly
        if (cameraRoot != null)
        {
            cameraRoot.localPosition = new Vector3(0f, currentCamY, 0f);
            cameraRoot.localRotation = Quaternion.Euler(pitch, 0f, 0f);
        }

        // 3. Re-enable walking physics & colliders
        cc.enabled = true;
        if (rb != null) rb.isKinematic = false;
        for (int i = 0; i < extraColliders.Length; i++)
        {
            if (extraColliders[i] != null && extraColliders[i] != cc)
                extraColliders[i].enabled = true;
        }

        activeSeat.OnDriverExit();
        activeSeat = null;
        isInDrivingMode = false;
    }

    private void ShowStatus(string msg, float duration)
    {
        statusMessage = msg;
        statusMessageTimer = duration;
    }

    // =========================================================================
    // WALKING, CROUCHING, JUMPING, HEAD BOB
    // =========================================================================
    private void HandleCrouch()
    {
        if (GetCrouchToggleDown())
            crouchToggleState = !crouchToggleState;

        bool wantsCrouch = GetCrouchHeld() || crouchToggleState;
        if (!wantsCrouch && isCrouching && HasOverheadObstruction())
            wantsCrouch = true;

        isCrouching = wantsCrouch;

        float targetH = isCrouching ? crouchHeight : standingHeight;
        float newH = Mathf.Lerp(cc.height, targetH, Mathf.Clamp01(Time.deltaTime * crouchTransitionSpeed));
        cc.height = newH;
        cc.center = new Vector3(0f, newH * 0.5f, 0f);

        float targetCamY = isCrouching ? crouchCamY : standingCamY;
        currentCamY = Mathf.Lerp(currentCamY, targetCamY, Mathf.Clamp01(Time.deltaTime * crouchTransitionSpeed));
    }

    private bool HasOverheadObstruction()
    {
        float checkDist = (standingHeight - cc.height) + 0.12f;
        if (checkDist <= 0.02f) return false;
        Vector3 origin = transform.position + Vector3.up * (cc.height - cc.radius);
        return Physics.SphereCast(origin, cc.radius * 0.85f, Vector3.up, out _, checkDist, ~0, QueryTriggerInteraction.Ignore);
    }

    private void HandleMovementAndJump()
    {
        float dt = Time.deltaTime;
        bool grounded = cc.isGrounded;

        if (grounded)
        {
            coyoteCounter = coyoteTime;
            if (verticalVelocity < 0f) verticalVelocity = -2.5f;
        }
        else coyoteCounter -= dt;

        if (GetJumpDown()) jumpBufferCounter = jumpBufferTime;
        else jumpBufferCounter -= dt;

        Vector2 moveInput = GetMoveInput();
        bool wantsSprint = GetSprintHeld() && !isCrouching && moveInput.y > 0.1f && grabbedHinge == null;

        float targetSpeed = isCrouching ? crouchSpeed : (wantsSprint ? sprintSpeed : walkSpeed);
        Vector3 wishDir = transform.right * moveInput.x + transform.forward * moveInput.y;
        if (wishDir.sqrMagnitude > 1f) wishDir.Normalize();

        float accel = grounded ? acceleration : acceleration * airControl;
        horizontalVelocity = Vector3.Lerp(horizontalVelocity, wishDir * targetSpeed, Mathf.Clamp01(dt * accel));

        if (jumpBufferCounter > 0f && coyoteCounter > 0f)
        {
            if (isCrouching && !HasOverheadObstruction())
            {
                isCrouching = false;
                crouchToggleState = false;
            }
            verticalVelocity = Mathf.Sqrt(jumpHeight * -2f * gravity);
            jumpBufferCounter = 0f;
            coyoteCounter = 0f;
        }

        verticalVelocity += gravity * dt;
        cc.Move((horizontalVelocity + Vector3.up * verticalVelocity) * dt);
    }

    private void HandleHeadBobAndFov()
    {
        if (cameraRoot == null) return;

        float speed = new Vector2(horizontalVelocity.x, horizontalVelocity.z).magnitude;
        bool movingOnGround = cc.isGrounded && speed > 0.25f;
        bool sprinting = !isCrouching && speed > walkSpeed + 0.8f;

        float bobY = 0f, bobX = 0f;
        if (enableHeadBob && movingOnGround)
        {
            float freq = sprinting ? sprintBobFrequency : (isCrouching ? walkBobFrequency * 0.72f : walkBobFrequency);
            float amp  = sprinting ? sprintBobAmplitude : (isCrouching ? walkBobAmplitude * 0.55f : walkBobAmplitude);
            bobTimer += Time.deltaTime * freq;
            bobY = Mathf.Sin(bobTimer) * amp;
            bobX = Mathf.Cos(bobTimer * 0.5f) * amp * 0.55f;
        }
        else bobTimer = 0f;

        cameraRoot.localPosition = Vector3.Lerp(cameraRoot.localPosition, new Vector3(bobX, currentCamY + bobY, 0f), Mathf.Clamp01(Time.deltaTime * 16f));

        if (playerCam != null)
        {
            float targetFov = sprinting ? sprintFov : normalFov;
            playerCam.fieldOfView = Mathf.Lerp(playerCam.fieldOfView, targetFov, Mathf.Clamp01(Time.deltaTime * 8f));
        }
    }

    // =========================================================================
    // CROSSHAIR & SUBTLE CONTEXT PROMPT
    // =========================================================================
    private void OnGUI()
    {
        if (!cursorLocked || dotTexture == null) return;

        float cx = Screen.width * 0.5f;
        float cy = Screen.height * 0.5f;

        if (showCrosshair)
        {
            float s = curCrosshairSize;
            Color prev = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.55f);
            GUI.DrawTexture(new Rect(cx - (s + 2f) * 0.5f, cy - (s + 2f) * 0.5f, s + 2f, s + 2f), dotTexture);

            if (grabbedHinge != null)
                GUI.color = new Color(1.00f, 0.72f, 0.22f, 0.98f);
            else if (hoveredHinge != null || hoveredSeat != null)
                GUI.color = new Color(1.00f, 0.92f, 0.55f, 0.95f);
            else
                GUI.color = new Color(1f, 1f, 1f, 0.88f);

            GUI.DrawTexture(new Rect(cx - s * 0.5f, cy - s * 0.5f, s, s), dotTexture);
            GUI.color = prev;
        }

        if (hintStyle == null)
        {
            hintStyle = new GUIStyle(GUI.skin.label);
            hintStyle.alignment = TextAnchor.MiddleCenter;
            hintStyle.fontSize = 15;
            hintStyle.fontStyle = FontStyle.Bold;
            hintStyle.normal.textColor = Color.white;
        }

        if (hoveredSeat != null && !isInDrivingMode)
        {
            DrawShadowLabel(new Rect(cx - 220f, cy + 18f, 440f, 28f), "[Enter] " + hoveredSeat.vehicleName + " Sürüş Moduna Geç");
        }
        else if (statusMessageTimer > 0f && !string.IsNullOrEmpty(statusMessage))
        {
            DrawShadowLabel(new Rect(cx - 340f, Screen.height - 62f, 680f, 30f), statusMessage);
        }
    }

    private void DrawShadowLabel(Rect r, string text)
    {
        Color prev = GUI.color;
        GUI.color = new Color(0f, 0f, 0f, 0.75f);
        GUI.Label(new Rect(r.x + 1f, r.y + 1f, r.width, r.height), text, hintStyle);
        GUI.color = Color.white;
        GUI.Label(r, text, hintStyle);
        GUI.color = prev;
    }

    private void BuildDotTexture()
    {
        const int sz = 32;
        dotTexture = new Texture2D(sz, sz, TextureFormat.RGBA32, false);
        float c = (sz - 1) * 0.5f, r = sz * 0.44f;
        for (int y = 0; y < sz; y++)
        for (int x = 0; x < sz; x++)
        {
            float d = Vector2.Distance(new Vector2(x, y), new Vector2(c, c));
            dotTexture.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01((r - d) * 0.85f + 0.5f)));
        }
        dotTexture.Apply();
    }

    // =========================================================================
    // DUAL INPUT SUPPORT (New Input System & Legacy Input Manager)
    // =========================================================================
    private Vector2 GetMoveInput()
    {
#if ENABLE_INPUT_SYSTEM
        var kb = Keyboard.current;
        if (kb != null)
        {
            float x = (kb.dKey.isPressed || kb.rightArrowKey.isPressed ? 1f : 0f)
                    - (kb.aKey.isPressed || kb.leftArrowKey.isPressed  ? 1f : 0f);
            float y = (kb.wKey.isPressed || kb.upArrowKey.isPressed    ? 1f : 0f)
                    - (kb.sKey.isPressed || kb.downArrowKey.isPressed  ? 1f : 0f);
            return new Vector2(x, y);
        }
        return Vector2.zero;
#else
        return new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
#endif
    }

    private Vector2 GetMouseDelta()
    {
#if ENABLE_INPUT_SYSTEM
        var m = Mouse.current;
        return m != null ? m.delta.ReadValue() * 0.12f : Vector2.zero;
#else
        return new Vector2(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y"));
#endif
    }

    private bool GetSprintHeld()
    {
#if ENABLE_INPUT_SYSTEM
        return Keyboard.current != null && Keyboard.current.leftShiftKey.isPressed;
#else
        return Input.GetKey(KeyCode.LeftShift);
#endif
    }

    private bool GetJumpDown()
    {
#if ENABLE_INPUT_SYSTEM
        return Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame;
#else
        return Input.GetKeyDown(KeyCode.Space);
#endif
    }

    private bool GetCrouchHeld()
    {
#if ENABLE_INPUT_SYSTEM
        return Keyboard.current != null && Keyboard.current.leftCtrlKey.isPressed;
#else
        return Input.GetKey(KeyCode.LeftControl);
#endif
    }

    private bool GetCrouchToggleDown()
    {
#if ENABLE_INPUT_SYSTEM
        return Keyboard.current != null && Keyboard.current.cKey.wasPressedThisFrame;
#else
        return Input.GetKeyDown(KeyCode.C);
#endif
    }

    private bool GetEnterVehicleDown()
    {
#if ENABLE_INPUT_SYSTEM
        var kb = Keyboard.current;
        return kb != null && (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame);
#else
        return Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter);
#endif
    }

    private bool GetLeanHeld()
    {
#if ENABLE_INPUT_SYSTEM
        bool q = Keyboard.current != null && Keyboard.current.qKey.isPressed;
        bool rmb = Mouse.current != null && Mouse.current.rightButton.isPressed;
        return q || rmb;
#else
        return Input.GetKey(KeyCode.Q) || Input.GetMouseButton(1);
#endif
    }

    private bool GetKeyDownEsc()
    {
#if ENABLE_INPUT_SYSTEM
        return Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame;
#else
        return Input.GetKeyDown(KeyCode.Escape);
#endif
    }

    private bool GetMouseLeftDown()
    {
#if ENABLE_INPUT_SYSTEM
        return Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
#else
        return Input.GetMouseButtonDown(0);
#endif
    }

    private bool GetMouseLeftHeld()
    {
#if ENABLE_INPUT_SYSTEM
        return Mouse.current != null && Mouse.current.leftButton.isPressed;
#else
        return Input.GetMouseButton(0);
#endif
    }
}
