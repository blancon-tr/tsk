using UnityEngine;

public enum TurkishDayOfWeek
{
    Pazartesi = 0,
    Salı = 1,
    Çarşamba = 2,
    Perşembe = 3,
    Cuma = 4,
    Cumartesi = 5,
    Pazar = 6
}

[ExecuteAlways]
public class TimeOfDayController : MonoBehaviour
{
    [Header("Gün & Saat")]
    public TurkishDayOfWeek currentDay = TurkishDayOfWeek.Pazartesi;
    public int dayCount = 1;
    [Range(0f, 23.99f)] public float currentHour = 9.5f;

    [Header("Zaman Akışı")]
    [Tooltip("Oyun içi 24 saatin gerçek kaç dakikada geçeceği (örn: 24 = 1 gerçek dk -> 1 oyun saati)")]
    [Range(0.5f, 120f)] public float dayLengthMinutes = 24f;
    public bool pauseTime = false;

    [Header("Saate Göre Otomatik Hava Değişimi")]
    public bool enableAutoWeather = true;
    [Tooltip("Kaç oyun saatinde bir havanın doğal olarak değişeceği")]
    [Range(1f, 12f)] public float weatherChangeIntervalHours = 3.5f;
    [Tooltip("Kış/kar havalarının otomatik gelmesine izin ver")]
    public bool allowAutoSnow = false;

    [Header("Ekran Saati (HUD)")]
    public bool showClockHUD = true;

    [Header("Referanslar (Otomatik Bulunur)")]
    public WeatherController weatherController;
    public Light sunLight;

    // Computed lighting state consumed by WeatherController.ApplyAtmosphere()
    public float DaylightFactor { get; private set; } = 1f;     // 0 = deep night, 1 = full day
    public float TwilightFactor { get; private set; } = 0f;     // 1 at sunrise/sunset golden hour
    public Color TimeSunColor { get; private set; } = Color.white;
    public Color TimeSkyTint { get; private set; } = new Color(0.46f, 0.56f, 0.68f);
    public Color TimeGroundColor { get; private set; } = new Color(0.36f, 0.38f, 0.36f);
    public Color TimeFogColor { get; private set; } = new Color(0.68f, 0.80f, 0.92f);
    public float TimeSunIntensity { get; private set; } = 2.2f;
    public float TimeSkyExposure { get; private set; } = 1.25f;
    public float TimeAmbientIntensity { get; private set; } = 1.0f;

    private float hoursUntilNextWeather = 3.0f;
    private GUIStyle hudBoxStyle;
    private GUIStyle hudDayStyle;
    private GUIStyle hudSubStyle;

    private void OnEnable()
    {
        ResolveReferences();
        UpdateSunAndLightingValues();
        if (weatherController != null)
            weatherController.ApplyWeatherImmediate(false);
    }

    private void OnValidate()
    {
        ResolveReferences();
        UpdateSunAndLightingValues();
        if (weatherController != null && !Application.isPlaying)
            weatherController.ApplyAtmosphereOnly();
    }

    private void ResolveReferences()
    {
        if (weatherController == null)
            weatherController = GetComponent<WeatherController>() ?? FindAnyObjectByType<WeatherController>();
        if (sunLight == null)
        {
            if (weatherController != null && weatherController.sunLight != null)
                sunLight = weatherController.sunLight;
            else
            {
                var lights = FindObjectsByType<Light>();
                for (int i = 0; i < lights.Length; i++)
                {
                    if (lights[i].type == LightType.Directional) { sunLight = lights[i]; break; }
                }
            }
        }
        if (weatherController != null && weatherController.timeOfDay == null)
            weatherController.timeOfDay = this;
    }

    private void Update()
    {
        if (Application.isPlaying && !pauseTime)
        {
            float hoursPerSecond = 24f / Mathf.Max(30f, dayLengthMinutes * 60f);
            float deltaHours = Time.deltaTime * hoursPerSecond;
            AdvanceHours(deltaHours);

            if (enableAutoWeather && weatherController != null)
            {
                hoursUntilNextWeather -= deltaHours;
                if (hoursUntilNextWeather <= 0f)
                {
                    PickWeatherForCurrentHour();
                    hoursUntilNextWeather = weatherChangeIntervalHours * Random.Range(0.75f, 1.30f);
                }
            }
        }

        UpdateSunAndLightingValues();
        if (weatherController != null && !Application.isPlaying)
            weatherController.ApplyAtmosphereOnly();
    }

    public void AdvanceHours(float deltaHours)
    {
        currentHour += deltaHours;
        while (currentHour >= 24f)
        {
            currentHour -= 24f;
            currentDay = (TurkishDayOfWeek)(((int)currentDay + 1) % 7);
            dayCount++;
        }
        while (currentHour < 0f)
        {
            currentHour += 24f;
            currentDay = (TurkishDayOfWeek)(((int)currentDay + 6) % 7);
            dayCount = Mathf.Max(1, dayCount - 1);
        }
    }

    public void SetTime(float hour)
    {
        currentHour = Mathf.Repeat(hour, 24f);
        UpdateSunAndLightingValues();
        if (weatherController != null)
            weatherController.ApplyAtmosphereOnly();
#if UNITY_EDITOR
        UnityEditor.SceneView.RepaintAll();
#endif
    }

    public void NextDay()
    {
        currentDay = (TurkishDayOfWeek)(((int)currentDay + 1) % 7);
        dayCount++;
        if (enableAutoWeather && weatherController != null)
            PickWeatherForCurrentHour();
    }

    /// <summary>
    /// Chooses natural Karadeniz weather weighted by the current time of day!
    /// </summary>
    public void PickWeatherForCurrentHour()
    {
        if (weatherController == null) return;

        float r = Random.value;
        WeatherPreset next;

        if (currentHour >= 5f && currentHour < 9.5f)
        {
            // Karadeniz Sabahı: sisli, parçalı bulutlu veya açık
            if (r < 0.34f)      next = WeatherPreset.Foggy;
            else if (r < 0.64f) next = WeatherPreset.Cloudy;
            else if (r < 0.86f) next = WeatherPreset.Clear;
            else                next = WeatherPreset.LightRain;
        }
        else if (currentHour >= 9.5f && currentHour < 15.5f)
        {
            // Öğle: çoğunlukla güneşli veya bulutlu, ara sıra hafif yağmur
            if (r < 0.48f)      next = WeatherPreset.Clear;
            else if (r < 0.78f) next = WeatherPreset.Cloudy;
            else if (r < 0.92f) next = WeatherPreset.LightRain;
            else                next = allowAutoSnow ? WeatherPreset.LightSnow : WeatherPreset.HeavyRain;
        }
        else if (currentHour >= 15.5f && currentHour < 21.5f)
        {
            // İkindi & Akşam: Karadeniz yağmur / fırtına olasılığı yüksek!
            if (r < 0.24f)      next = WeatherPreset.Clear;
            else if (r < 0.48f) next = WeatherPreset.Cloudy;
            else if (r < 0.74f) next = WeatherPreset.LightRain;
            else if (r < 0.92f) next = WeatherPreset.HeavyRain;
            else                next = allowAutoSnow ? WeatherPreset.HeavySnow : WeatherPreset.Foggy;
        }
        else
        {
            // Gece: açık yıldızlı, sisli veya yağmurlu
            if (r < 0.36f)      next = WeatherPreset.Clear;
            else if (r < 0.58f) next = WeatherPreset.Foggy;
            else if (r < 0.76f) next = WeatherPreset.Cloudy;
            else if (r < 0.90f) next = WeatherPreset.LightRain;
            else                next = allowAutoSnow ? WeatherPreset.LightSnow : WeatherPreset.HeavyRain;
        }

        weatherController.ApplyPreset(next);
    }

    public void UpdateSunAndLightingValues()
    {
        // Sun arc: 06:00 sunrise (0 deg), 12:00 noon (90 deg), 18:00 sunset (180 deg), 00:00 midnight (270 deg)
        float sunAngle = (currentHour - 6f) * 15f; // degrees
        float sinElev = Mathf.Sin(sunAngle * Mathf.Deg2Rad); // -1 at midnight, 0 at 06/18, +1 at noon

        // Daylight [0..1] transitions smoothly around sunrise (05:30..07:30) and sunset (17:30..19:45)
        DaylightFactor = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-0.14f, 0.24f, sinElev));
        // Golden hour twilight peaks right around horizon (-0.12 .. +0.26)
        TwilightFactor = Mathf.Clamp01(1f - Mathf.Abs(sinElev - 0.05f) / 0.24f);
        TwilightFactor *= TwilightFactor;

        if (sunLight != null)
        {
            if (sinElev >= -0.05f)
            {
                // Daytime sun pitch & azimuth
                float pitch = Mathf.Lerp(4f, 66f, Mathf.Clamp01(sinElev));
                float yaw = Mathf.Lerp(75f, 285f, Mathf.Clamp01((currentHour - 6f) / 12f));
                sunLight.transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
            }
            else
            {
                // Subtle high moonlight angle at night
                sunLight.transform.rotation = Quaternion.Euler(38f, 210f, 0f);
            }
        }

        // Color palette for Night / Twilight (Sunrise & Sunset) / Midday
        Color nightSun    = new Color(0.32f, 0.44f, 0.68f);
        Color twilightSun = new Color(1.00f, 0.56f, 0.26f);
        Color daySun      = new Color(1.00f, 0.95f, 0.86f);
        TimeSunColor = Color.Lerp(Color.Lerp(nightSun, daySun, DaylightFactor), twilightSun, TwilightFactor * 0.85f);

        Color nightSky    = new Color(0.05f, 0.08f, 0.16f);
        Color twilightSky = new Color(0.76f, 0.44f, 0.32f);
        Color daySky      = new Color(0.46f, 0.56f, 0.68f);
        TimeSkyTint = Color.Lerp(Color.Lerp(nightSky, daySky, DaylightFactor), twilightSky, TwilightFactor * 0.72f);

        Color nightGround = new Color(0.03f, 0.04f, 0.07f);
        Color dayGround   = new Color(0.36f, 0.38f, 0.36f);
        TimeGroundColor = Color.Lerp(nightGround, dayGround, DaylightFactor);

        Color nightFog    = new Color(0.06f, 0.09f, 0.15f);
        Color twilightFog = new Color(0.76f, 0.54f, 0.42f);
        Color dayFog      = new Color(0.68f, 0.80f, 0.92f);
        TimeFogColor = Color.Lerp(Color.Lerp(nightFog, dayFog, DaylightFactor), twilightFog, TwilightFactor * 0.70f);

        TimeSunIntensity     = Mathf.Lerp(0.20f, 2.30f, DaylightFactor);
        TimeSkyExposure      = Mathf.Lerp(0.14f, 1.25f, DaylightFactor) + TwilightFactor * 0.14f;
        TimeAmbientIntensity = Mathf.Lerp(0.24f, 1.05f, DaylightFactor);
    }

    public string GetFormattedTime()
    {
        int h = Mathf.FloorToInt(currentHour) % 24;
        int m = Mathf.FloorToInt((currentHour - Mathf.Floor(currentHour)) * 60f) % 60;
        return string.Format("{0:00}:{1:00}", h, m);
    }

    public string GetWeatherLabelTurkish()
    {
        if (weatherController == null) return "";
        switch (weatherController.weatherPreset)
        {
            case WeatherPreset.Clear:     return DaylightFactor < 0.25f ? "Açık Gece" : "Güneşli";
            case WeatherPreset.Cloudy:    return "Bulutlu";
            case WeatherPreset.Foggy:     return "Sisli";
            case WeatherPreset.LightRain: return "Hafif Yağmur";
            case WeatherPreset.HeavyRain: return "Sağanak & Fırtına";
            case WeatherPreset.LightSnow: return "Hafif Kar";
            case WeatherPreset.HeavySnow: return "Yoğun Kar";
            default:                      return "Özel Hava";
        }
    }

    private void OnGUI()
    {
        if (!Application.isPlaying || !showClockHUD) return;

        if (hudDayStyle == null)
        {
            hudDayStyle = new GUIStyle(GUI.skin.label);
            hudDayStyle.alignment = TextAnchor.MiddleRight;
            hudDayStyle.fontSize = 18;
            hudDayStyle.fontStyle = FontStyle.Bold;
            hudDayStyle.normal.textColor = Color.white;

            hudSubStyle = new GUIStyle(GUI.skin.label);
            hudSubStyle.alignment = TextAnchor.MiddleRight;
            hudSubStyle.fontSize = 13;
            hudSubStyle.normal.textColor = new Color(0.88f, 0.92f, 0.96f, 0.92f);
        }

        float w = 220f, h = 52f;
        Rect bg = new Rect(Screen.width - w - 16f, 14f, w, h);

        Color prev = GUI.color;
        GUI.color = new Color(0.05f, 0.07f, 0.10f, 0.52f);
        GUI.Box(bg, GUIContent.none);
        GUI.color = Color.white;

        string topLine = currentDay.ToString().ToUpper() + "  " + GetFormattedTime();
        string subLine = dayCount + ". Gün  •  " + GetWeatherLabelTurkish();

        GUI.Label(new Rect(bg.x, bg.y + 4f,  bg.width - 10f, 24f), topLine, hudDayStyle);
        GUI.Label(new Rect(bg.x, bg.y + 27f, bg.width - 10f, 20f), subLine, hudSubStyle);
        GUI.color = prev;
    }
}
