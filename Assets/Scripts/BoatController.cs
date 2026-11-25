using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Realistic Karadeniz Wooden Fishing Boat controller:
/// - Smooth weighty hull yaw & banking roll inertia (no instant snap-back when releasing A/D)
/// - Concentric propeller spin & outboard motor swivel
/// - Dynamic V-shaped white foam water wake & propeller wash synced to boat speed
/// - Shallow-shore & pier collision protection
/// </summary>
public class BoatController : MonoBehaviour
{
    [Header("Sürüş & Referanslar")]
    public VehicleSeat driverSeat;
    public Transform outboardMotor;
    public Transform propeller;
    public GameObject mooringRopeObject;

    [Header("Su İzi (Wake) Parçacıkları")]
    public ParticleSystem wakePropWash;
    public ParticleSystem wakeLeft;
    public ParticleSystem wakeRight;

    [Header("Su & Motor Fiziği")]
    public float waterLevel = 100.15f;
    public float maxForwardSpeed = 11.5f;
    public float maxReverseSpeed = 4.0f;
    public float acceleration = 4.0f;
    public float waterDrag = 1.25f;
    public float turnSpeed = 48f;
    public float minWaterDepth = 0.55f;

    [Header("Dalga & Ağırlık Ataleti")]
    public float bobAmplitude = 0.06f;
    public float bobSpeed = 1.35f;
    public float idleRollAmp = 2.2f;
    public float idlePitchAmp = 1.2f;
    public float maxMotorTurnAngle = 32f;
    public float maxBankRoll = 8.5f;
    public float maxPlaningPitch = -5.5f;

    private float currentSpeed = 0f;
    private float currentTurn = 0f;
    private float currentYaw;
    private float motorAngle = 0f;
    private float smoothedBankRoll = 0f;
    private float smoothedPlaningPitch = 0f;
    private Vector3 dockPos;

    private Terrain terrain;
    private WeatherController weather;

    private void Start()
    {
        currentYaw = transform.eulerAngles.y;
        dockPos = transform.position;
        terrain = Terrain.activeTerrain ?? FindAnyObjectByType<Terrain>();
        weather = FindAnyObjectByType<WeatherController>();

        if (driverSeat == null) driverSeat = GetComponentInChildren<VehicleSeat>();
        if (outboardMotor == null) outboardMotor = transform.Find("Boat_OutboardMotor");
        if (propeller == null && outboardMotor != null) propeller = outboardMotor.Find("Motor_Propeller");

        if (wakePropWash == null) { var t = transform.Find("Boat_Wake_PropWash"); if (t != null) wakePropWash = t.GetComponent<ParticleSystem>(); }
        if (wakeLeft == null)     { var t = transform.Find("Boat_Wake_Left");     if (t != null) wakeLeft     = t.GetComponent<ParticleSystem>(); }
        if (wakeRight == null)    { var t = transform.Find("Boat_Wake_Right");    if (t != null) wakeRight    = t.GetComponent<ParticleSystem>(); }

        var oldBob = GetComponent<BoatBobbing>();
        if (oldBob != null) oldBob.enabled = false;
    }

    private void Update()
    {
        float dt = Time.deltaTime;
        bool isDriving = driverSeat != null && driverSeat.IsOccupied;

        float throttleInput = 0f;
        float steerInput = 0f;

        if (isDriving)
        {
            Vector2 input = GetBoatInput();
            throttleInput = input.y;
            steerInput = input.x;
        }

        // 1. Smooth marine throttle & water drag
        if (Mathf.Abs(throttleInput) > 0.01f)
        {
            float targetSpd = throttleInput > 0f ? throttleInput * maxForwardSpeed : throttleInput * maxReverseSpeed;
            currentSpeed = Mathf.MoveTowards(currentSpeed, targetSpd, acceleration * dt);
        }
        else
        {
            currentSpeed = Mathf.MoveTowards(currentSpeed, 0f, waterDrag * dt);
        }

        // 2. Weighty hull steering & outboard motor angle (slow natural settle when releasing A/D!)
        float targetMotorAng = -steerInput * maxMotorTurnAngle;
        float motorLerpRate = Mathf.Abs(steerInput) > 0.05f ? 3.8f : 1.7f;
        motorAngle = Mathf.Lerp(motorAngle, targetMotorAng, Mathf.Clamp01(dt * motorLerpRate));

        float steerNormalized = -motorAngle / Mathf.Max(1f, maxMotorTurnAngle); // [-1..+1] smoothly lags input
        float steerAuthority = Mathf.Clamp01(Mathf.Abs(currentSpeed) / 2.2f + (Mathf.Abs(throttleInput) > 0.05f ? 0.50f : 0.12f));
        float reverseSign = currentSpeed < -0.25f ? -1f : 1f;
        float targetTurn = steerNormalized * turnSpeed * steerAuthority * reverseSign;

        float turnLerpRate = Mathf.Abs(steerInput) > 0.05f ? 2.6f : 1.4f;
        currentTurn = Mathf.Lerp(currentTurn, targetTurn, Mathf.Clamp01(dt * turnLerpRate));
        currentYaw += currentTurn * dt;

        // 3. Horizontal movement with shallow-shore & pier protection
        Vector3 flatFwd = Quaternion.Euler(0f, currentYaw, 0f) * Vector3.forward;
        Vector3 moveDelta = flatFwd * (currentSpeed * dt);
        Vector3 nextPos = transform.position + moveDelta;

        if (IsDeepEnough(nextPos + flatFwd * (currentSpeed >= 0f ? 2.6f : -2.4f)) && !HitsPier(transform.position, moveDelta))
        {
            transform.position = new Vector3(nextPos.x, transform.position.y, nextPos.z);
        }
        else
        {
            currentSpeed *= 0.35f;
        }

        // 4. Wave bobbing + heavily damped banking roll & planing pitch (NO instant snap-back!)
        float windBoost = weather != null ? Mathf.Lerp(1f, 2.1f, weather.windStrength) : 1f;
        float t = Time.time * bobSpeed * Mathf.Lerp(1f, 1.3f, windBoost - 1f);
        float waveY     = Mathf.Sin(t) * bobAmplitude * windBoost;
        float waveRoll  = Mathf.Sin(t * 0.87f + 0.6f) * idleRollAmp * windBoost;
        float wavePitch = Mathf.Cos(t * 0.65f + 1.2f) * idlePitchAmp * windBoost;

        float speedRatio = Mathf.Clamp01(Mathf.Abs(currentSpeed) / maxForwardSpeed);
        float targetPlaning = Mathf.Clamp01(currentSpeed / maxForwardSpeed) * maxPlaningPitch;
        float targetBank    = -(currentTurn / Mathf.Max(1f, turnSpeed)) * Mathf.Lerp(0.35f, 1.0f, speedRatio) * maxBankRoll;

        smoothedPlaningPitch = Mathf.Lerp(smoothedPlaningPitch, targetPlaning, Mathf.Clamp01(dt * 1.8f));
        smoothedBankRoll     = Mathf.Lerp(smoothedBankRoll,     targetBank,    Mathf.Clamp01(dt * 1.9f));

        Vector3 pos = transform.position;
        pos.y = waterLevel + waveY;
        transform.position = pos;
        transform.rotation = Quaternion.Euler(wavePitch + smoothedPlaningPitch, currentYaw, waveRoll + smoothedBankRoll);

        // 5. Animate outboard motor swivel & concentric propeller spin around its own hub!
        if (outboardMotor != null)
            outboardMotor.localRotation = Quaternion.Euler(0f, motorAngle, 0f);

        if (propeller != null)
        {
            float propRpm = Mathf.Abs(currentSpeed) * 210f + (isDriving ? 140f : 0f);
            float dir = currentSpeed < -0.1f ? -1f : 1f;
            propeller.Rotate(0f, 0f, propRpm * dir * dt, Space.Self);
        }

        // 6. Update dynamic water wake foam behind boat!
        UpdateWaterWake(speedRatio, isDriving);

        // 7. Auto-hide mooring rope once boat leaves dock
        if (mooringRopeObject != null)
        {
            float distFromDock = Vector2.Distance(new Vector2(pos.x, pos.z), new Vector2(dockPos.x, dockPos.z));
            bool shouldShowRope = distFromDock < 1.35f && !isDriving;
            if (mooringRopeObject.activeSelf != shouldShowRope)
                mooringRopeObject.SetActive(shouldShowRope);
        }
    }

    private void UpdateWaterWake(float speedRatio, bool isDriving)
    {
        float moving = Mathf.Clamp01((Mathf.Abs(currentSpeed) - 0.35f) / (maxForwardSpeed - 0.35f));

        // Center bubbly propeller wash (gentle idle bubbles when driving, strong foam trail when moving)
        if (wakePropWash != null)
        {
            var em = wakePropWash.emission;
            float rate = moving > 0.01f ? Mathf.Lerp(12f, 55f, moving) : (isDriving ? 6f : 0f);
            em.rateOverTime = rate;
            if (rate > 0f && !wakePropWash.isPlaying) wakePropWash.Play();
            else if (rate <= 0f && wakePropWash.isPlaying) wakePropWash.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }

        // Diverging V-shaped side wake ribbons
        UpdateSideWake(wakeLeft,  moving, -1f);
        UpdateSideWake(wakeRight, moving,  1f);
    }

    private void UpdateSideWake(ParticleSystem ps, float moving, float sideSign)
    {
        if (ps == null) return;
        var em = ps.emission;
        float rate = moving > 0.04f ? Mathf.Lerp(8f, 38f, moving) : 0f;
        em.rateOverTime = rate;

        // Fan foam slightly outward from hull sides in world space
        var vel = ps.velocityOverLifetime;
        vel.enabled = true;
        vel.space = ParticleSystemSimulationSpace.World;
        Vector3 flatRight = transform.right; flatRight.y = 0f; flatRight.Normalize();
        Vector3 flatBack = -transform.forward; flatBack.y = 0f; flatBack.Normalize();
        Vector3 spread = (flatRight * sideSign * 1.45f + flatBack * 0.65f) * Mathf.Lerp(0.4f, 1.35f, moving);
        vel.x = new ParticleSystem.MinMaxCurve(spread.x);
        vel.y = new ParticleSystem.MinMaxCurve(0f);
        vel.z = new ParticleSystem.MinMaxCurve(spread.z);

        if (rate > 0f && !ps.isPlaying) ps.Play();
        else if (rate <= 0f && ps.isPlaying) ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
    }

    private bool IsDeepEnough(Vector3 probeWorldPos)
    {
        if (terrain == null) return true;
        float groundY = terrain.SampleHeight(probeWorldPos) + terrain.transform.position.y;
        return groundY <= (waterLevel - minWaterDepth);
    }

    private bool HitsPier(Vector3 curPos, Vector3 moveDelta)
    {
        float dist = moveDelta.magnitude;
        if (dist < 0.0005f) return false;
        Vector3 dir = moveDelta / dist;
        if (Physics.SphereCast(curPos + Vector3.up * 0.45f, 1.05f, dir, out RaycastHit hit, dist + 0.6f, ~0, QueryTriggerInteraction.Ignore))
        {
            if (hit.transform.IsChildOf(transform)) return false;
            if (hit.collider.GetComponentInParent<FirstPersonController>() != null) return false;
            return true;
        }
        return false;
    }

    private Vector2 GetBoatInput()
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
}
