using UnityEngine;

public class BoatBobbing : MonoBehaviour
{
    public float bobAmplitude = 0.065f;
    public float bobSpeed = 1.35f;
    public float rollAmplitude = 2.4f;
    public float pitchAmplitude = 1.3f;

    private Vector3 basePos;
    private Quaternion baseRot;
    private WeatherController weather;

    private void Start()
    {
        basePos = transform.position;
        baseRot = transform.rotation;
        weather = FindAnyObjectByType<WeatherController>();
    }

    private void Update()
    {
        float windBoost = weather != null ? Mathf.Lerp(1f, 2.2f, weather.windStrength) : 1f;
        float t = Time.time * bobSpeed * Mathf.Lerp(1f, 1.35f, windBoost - 1f);

        float dy = Mathf.Sin(t) * bobAmplitude * windBoost;
        float roll  = Mathf.Sin(t * 0.87f + 0.6f) * rollAmplitude * windBoost;
        float pitch = Mathf.Cos(t * 0.65f + 1.2f) * pitchAmplitude * windBoost;

        transform.position = basePos + Vector3.up * dy;
        transform.rotation = baseRot * Quaternion.Euler(pitch, 0f, roll);
    }
}
