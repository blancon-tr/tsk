using UnityEngine;

[ExecuteAlways]
public class WindTurbineController : MonoBehaviour
{
    [Header("Referanslar")]
    public Transform rotorPivot;
    public Light aviationBeaconLight;
    public Renderer aviationBeaconRenderer;

    [Header("Dönüş Ayarları")]
    [Tooltip("Sakin güneşli havada minimum dönüş hızı (derece/sn)")]
    public float minSpinSpeed = 14f;
    [Tooltip("Rüzgarlı havada maksimum temel hız (derece/sn)")]
    public float maxWindSpinSpeed = 72f;
    [Tooltip("Yağmurlu ve fırtınalı havada eklenen ekstra dönüş hızı (derece/sn)")]
    public float rainSpinBoost = 62f;
    [Tooltip("Her türbinin hafifçe farklı hızda dönmesi için çarpan")]
    public float speedMultiplier = 1f;
    public bool syncWithWeather = true;

    [Header("Uyarı Işığı")]
    public float beaconBlinkInterval = 1.8f;

    private WeatherController cachedWeather;
    private float currentSpinSpeed;
    private float blinkTimer;
    private float gustPhase;

    private void OnEnable()
    {
        if (rotorPivot == null)
        {
            Transform t = transform.Find("Nacelle/Rotor_Pivot");
            if (t != null) rotorPivot = t;
        }
        cachedWeather = FindAnyObjectByType<WeatherController>();
        currentSpinSpeed = minSpinSpeed * speedMultiplier;
        gustPhase = (transform.position.x + transform.position.z) * 0.01f;
    }

    private void Update()
    {
        if (!Application.isPlaying)
            return;

        float dt = Time.deltaTime;
        float wind = 0.25f;
        float rain = 0f;
        float snow = 0f;

        if (syncWithWeather)
        {
            if (cachedWeather == null)
                cachedWeather = FindAnyObjectByType<WeatherController>();
            if (cachedWeather != null)
            {
                wind = cachedWeather.windStrength;
                rain = cachedWeather.rainIntensity;
                snow = cachedWeather.snowIntensity;
            }
        }

        // Smooth wind speed + strong extra boost during rain/storms + subtle storm gusts
        float baseSpeed = Mathf.Lerp(minSpinSpeed, maxWindSpinSpeed, wind);
        float stormBoost = rain * rainSpinBoost + snow * (rainSpinBoost * 0.45f);
        float gust = (rain > 0.2f || wind > 0.5f)
            ? Mathf.Sin(Time.time * 0.85f + gustPhase) * (10f * Mathf.Max(rain, wind))
            : 0f;

        float targetSpeed = Mathf.Max(minSpinSpeed, (baseSpeed + stormBoost + gust) * speedMultiplier);
        currentSpinSpeed = Mathf.Lerp(currentSpinSpeed, targetSpeed, Mathf.Clamp01(dt * 1.4f));

        if (rotorPivot != null)
            rotorPivot.Rotate(0f, 0f, -currentSpinSpeed * dt, Space.Self);

        // Aviation red warning light blink on top of nacelle
        if (aviationBeaconLight != null)
        {
            blinkTimer += dt;
            bool on = (blinkTimer % beaconBlinkInterval) < 0.35f;
            aviationBeaconLight.intensity = on ? 6.5f : 0.4f;
        }
    }
}
