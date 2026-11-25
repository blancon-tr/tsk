using UnityEngine;

/// <summary>
/// My Summer Car / Amnesia style hold-Left-Click & drag-mouse hinged object controller.
/// Works on house doors, balcony doors, garage doors, turbine doors, and future car doors/hoods/trunks.
/// </summary>
public class HingeInteractable : MonoBehaviour
{
    [Header("Menteşe Eksen & Açı Sınırları")]
    [Tooltip("Kapılar için (0, 1, 0), kaput/bagaj için (1, 0, 0)")]
    public Vector3 hingeAxis = Vector3.up;
    public float minAngle = -105f;
    public float maxAngle = 0f;
    public float closedAngle = 0f;

    [Header("Sürükleme & Fizik Hissi")]
    [Range(0.1f, 2.5f)] public float dragSensitivity = 0.55f;
    [Tooltip("Bırakıldığında kapının yumuşakça durma sönümü")]
    public float angularDamping = 9f;
    [Tooltip("Kapalı açıya bu kadar yaklaşıp bırakınca çıt diye kapanır")]
    public float latchSnapAngle = 3.5f;
    public bool invertDrag = false;

    private Quaternion baseLocalRotation;
    private float currentAngle;
    private float angularVelocity;
    private bool isGrabbed;
    private Vector3 grabLocalOffset;

    public float CurrentAngle => currentAngle;
    public bool IsGrabbed => isGrabbed;

    private void Awake()
    {
        // Measure starting local angle around hingeAxis so pre-opened doors (like garage doors) start at their exact angle!
        Vector3 euler = transform.localEulerAngles;
        float startAng = 0f;
        if (Mathf.Abs(hingeAxis.y) > 0.5f) startAng = NormalizeAngle(euler.y);
        else if (Mathf.Abs(hingeAxis.x) > 0.5f) startAng = NormalizeAngle(euler.x);
        else if (Mathf.Abs(hingeAxis.z) > 0.5f) startAng = NormalizeAngle(euler.z);

        // Compute zero-angle base rotation
        Quaternion currentRot = transform.localRotation;
        baseLocalRotation = currentRot * Quaternion.Inverse(Quaternion.AngleAxis(startAng, hingeAxis.normalized));
        currentAngle = Mathf.Clamp(startAng, minAngle, maxAngle);
        ApplyRotation();
    }

    public void BeginGrab(Vector3 worldHitPoint)
    {
        isGrabbed = true;
        angularVelocity = 0f;
        grabLocalOffset = transform.InverseTransformPoint(worldHitPoint);
    }

    public void ApplyDrag(Vector2 mouseDelta, Transform cameraTransform)
    {
        if (!isGrabbed || cameraTransform == null) return;

        // World-space hinge axis and tangent direction of the grabbed point when angle increases (+1 deg)
        Vector3 worldAxis = transform.TransformDirection(hingeAxis.normalized);
        Vector3 worldGrabPos = transform.TransformPoint(grabLocalOffset);
        Vector3 radial = worldGrabPos - transform.position;
        if (radial.sqrMagnitude < 0.01f)
            radial = transform.right * 0.5f;

        Vector3 swingDir = Vector3.Cross(worldAxis, radial).normalized;

        // Project swing direction onto camera Right (mouse X) and camera Forward (mouse Y)
        // So pushing mouse forward pushes the door away, pulling pulls it toward you, left/right swings naturally!
        Vector3 camRight = cameraTransform.right;
        Vector3 camFwd = cameraTransform.forward;
        camFwd.y = 0f;
        if (camFwd.sqrMagnitude > 0.001f) camFwd.Normalize();
        else camFwd = cameraTransform.forward;

        float dotRight = Vector3.Dot(swingDir, camRight);
        float dotFwd   = Vector3.Dot(swingDir, camFwd);

        float dirSign = invertDrag ? -1f : 1f;
        float deltaDeg = (mouseDelta.x * dotRight + mouseDelta.y * dotFwd) * dragSensitivity * 3.2f * dirSign;

        float prevAngle = currentAngle;
        currentAngle = Mathf.Clamp(currentAngle + deltaDeg, minAngle, maxAngle);

        float dt = Mathf.Max(Time.deltaTime, 0.001f);
        angularVelocity = Mathf.Clamp((currentAngle - prevAngle) / dt, -260f, 260f);

        ApplyRotation();
    }

    public void EndGrab()
    {
        isGrabbed = false;
        // Soft latch when released very close to closed
        if (Mathf.Abs(currentAngle - closedAngle) <= latchSnapAngle)
        {
            currentAngle = closedAngle;
            angularVelocity = 0f;
            ApplyRotation();
        }
    }

    private void Update()
    {
        if (isGrabbed) return;

        if (Mathf.Abs(angularVelocity) > 0.25f)
        {
            currentAngle += angularVelocity * Time.deltaTime;
            if (currentAngle <= minAngle || currentAngle >= maxAngle)
            {
                currentAngle = Mathf.Clamp(currentAngle, minAngle, maxAngle);
                angularVelocity = 0f;
            }
            else
            {
                angularVelocity = Mathf.Lerp(angularVelocity, 0f, Mathf.Clamp01(Time.deltaTime * angularDamping));
            }
            ApplyRotation();
        }
    }

    private void ApplyRotation()
    {
        transform.localRotation = baseLocalRotation * Quaternion.AngleAxis(currentAngle, hingeAxis.normalized);
    }

    private static float NormalizeAngle(float deg)
    {
        deg %= 360f;
        if (deg > 180f) deg -= 360f;
        if (deg < -180f) deg += 360f;
        return deg;
    }
}
