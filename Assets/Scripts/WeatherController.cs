using UnityEngine;

public enum WeatherPreset
{
    Clear,
    Cloudy,
    Foggy,
    LightRain,
    HeavyRain,
    LightSnow,
    HeavySnow,
    Custom
}

public enum ThunderStrength
{
    Weak,
    Medium,
    Heavy
}

[ExecuteAlways]
public class WeatherController : MonoBehaviour
{
    [Header("Hava Durumu Modu")]
    public WeatherPreset weatherPreset = WeatherPreset.Clear;

    [Header("Hava Şiddeti Ayarları (0 - 1)")]
    [Range(0f, 1f)] public float rainIntensity = 0f;
    [Range(0f, 1f)] public float snowIntensity = 0f;
    [Range(0f, 1f)] public float fogIntensity = 0.05f;
    [Range(0f, 1f)] public float cloudiness = 0.05f;
    [Range(0f, 1f)] public float windStrength = 0.15f;
    [Range(0f, 360f)] public float windDirection = 35f;

    [Header("Fırtına, Şimşek & Geçiş Ayarları")]
    public bool enableLightning = true;
    [Range(2f, 25f)] public float minThunderInterval = 5f;
    [Range(5f, 40f)] public float maxThunderInterval = 14f;
    [Range(0.1f, 6f)] public float transitionSpeed = 1.8f;
    [Tooltip("Edit Mode'da Scene View içinde yağmur ve karı canlı oynatır.")]
    public bool editModePreview = true;

    [Header("Ses Ayarları")]
    [Range(0f, 1f)] public float masterVolume = 0.85f;
    [Range(0f, 1.5f)] public float thunderVolume = 1.0f;
    public bool muteAudio = false;
    [Tooltip("Ev veya garaj çatısının altındayken yağmur ve rüzgar sesini boğuklaştırır.")]
    public bool enableIndoorMuffling = true;
    [Range(400f, 3500f)] public float indoorCutoffFreq = 900f;
    public LayerMask indoorRoofMask = ~0;

    [Header("Takip & Atmosfer Referansları")]
    [Tooltip("Boş bırakılırsa otomatik olarak aktif kamerayı (Play Mode'da Main/Aktif Kamera, Edit Mode'da Scene View) takip eder.")]
    public Transform followTarget;
    public Light sunLight;
    public Material skyboxMaterial;
    public TimeOfDayController timeOfDay;

    [Header("3 Katmanlı Yağış Sistemleri (Otomatik Bağlanır)")]
    public ParticleSystem rainNear;
    public ParticleSystem rainMid;
    public ParticleSystem rainFar;
    public ParticleSystem snowNear;
    public ParticleSystem snowMid;
    public ParticleSystem snowFar;

    [Header("Ses Kaynakları & Klipler (Otomatik Bağlanır)")]
    public AudioSource rainLightSource;
    public AudioSource rainHeavySource;
    public AudioSource windSource;
    public AudioSource thunderSource;

    public AudioClip rainLightClip;
    public AudioClip rainHeavyClip;
    public AudioClip windClip;
    public AudioClip[] weakThunderClips;
    public AudioClip[] mediumThunderClips;
    public AudioClip[] heavyThunderClips;
    public AudioClip[] thunderClips;

    [Header("Kapsama & Hız Öngörüsü")]
    [Range(0f, 1.5f)] public float velocityLeadTime = 0.55f;
    public float nearHeightOffset = 18f;
    public float midHeightOffset = 34f;
    public float farHeightOffset = 58f;

    public float rainNearMaxRate = 2400f;
    public float rainMidMaxRate = 3600f;
    public float rainFarMaxRate = 2200f;
    public float snowNearMaxRate = 900f;
    public float snowMidMaxRate = 1800f;
    public float snowFarMaxRate = 1100f;

    // Runtime smoothed state
    private float curRain;
    private float curSnow;
    private float curFog;
    private float curCloud;
    private float curWind;

    // Tiered Lightning & Thunder state
    private float lightningTimer = 4.5f;
    private ThunderStrength activeFlashStrength = ThunderStrength.Medium;
    private float flashElapsed = 99f;
    private float flashDuration = 0.4f;
    private float flashPeakSun = 0f;
    private float flashPeakExposure = 0f;
    private float flashPeakAmbient = 0f;
    private float curFlashSun = 0f;
    private float curFlashExposure = 0f;
    private float curFlashAmbient = 0f;
    private float curFlashColorBlend = 0f;

    private float pendingThunderDelay = -1f;
    private ThunderStrength pendingThunderStrength = ThunderStrength.Medium;
    private float pendingThunderRelVol = 0.9f;
    private float pendingThunderPitch = 1.0f;

    public ThunderStrength LastThunderStrength { get; private set; } = ThunderStrength.Medium;

    // Tracking & Indoor muffling
    private Vector3 lastAnchorPos;
    private bool hasLastAnchor;
    private Vector3 smoothedVelocity;
    private float indoorFactor = 0f;
    public bool IsIndoors => indoorFactor > 0.5f;

    // Cached LowPass filters
    private AudioLowPassFilter rainLightLPF;
    private AudioLowPassFilter rainHeavyLPF;
    private AudioLowPassFilter windLPF;

    private bool rainWasActive;
    private bool snowWasActive;

#if UNITY_EDITOR
    private double lastEditorTime;
    private bool editorNeedsRefresh;
#endif

    private void OnEnable()
    {
        ResolveReferences();
        curRain = rainIntensity;
        curSnow = snowIntensity;
        curFog = fogIntensity;
        curCloud = cloudiness;
        curWind = windStrength;
        hasLastAnchor = false;
        rainWasActive = false;
        snowWasActive = false;
        flashElapsed = 99f;
        curFlashSun = curFlashExposure = curFlashAmbient = curFlashColorBlend = 0f;

        FollowActiveCamera(0f);

        if (Application.isPlaying)
        {
            ApplyWeatherImmediate(forcePrewarmIfStarting: true);
            UpdateAudio(1f);
        }
        else
        {
            PauseAllParticlesForEditorSim();
            ApplyAtmosphere();
#if UNITY_EDITOR
            editorNeedsRefresh = true;
#endif
        }

#if UNITY_EDITOR
        lastEditorTime = UnityEditor.EditorApplication.timeSinceStartup;
        UnityEditor.EditorApplication.update -= OnEditorUpdate;
        UnityEditor.EditorApplication.update += OnEditorUpdate;
#endif
    }

    private void OnDisable()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.update -= OnEditorUpdate;
#endif
        StopLoopAudio();
    }

    private void OnValidate()
    {
        if (!Application.isPlaying)
        {
            curRain = rainIntensity;
            curSnow = snowIntensity;
            curFog = fogIntensity;
            curCloud = cloudiness;
            curWind = windStrength;
#if UNITY_EDITOR
            editorNeedsRefresh = true;
#endif
        }
    }

    public void ApplyPreset(WeatherPreset preset)
    {
        weatherPreset = preset;
        switch (preset)
        {
            case WeatherPreset.Clear:
                rainIntensity = 0f; snowIntensity = 0f; fogIntensity = 0.04f; cloudiness = 0.05f; windStrength = 0.15f;
                break;
            case WeatherPreset.Cloudy:
                rainIntensity = 0f; snowIntensity = 0f; fogIntensity = 0.25f; cloudiness = 0.65f; windStrength = 0.35f;
                break;
            case WeatherPreset.Foggy:
                rainIntensity = 0f; snowIntensity = 0f; fogIntensity = 0.90f; cloudiness = 0.72f; windStrength = 0.20f;
                break;
            case WeatherPreset.LightRain:
                rainIntensity = 0.42f; snowIntensity = 0f; fogIntensity = 0.45f; cloudiness = 0.78f; windStrength = 0.42f;
                break;
            case WeatherPreset.HeavyRain:
                rainIntensity = 1.00f; snowIntensity = 0f; fogIntensity = 0.72f; cloudiness = 1.00f; windStrength = 0.88f;
                break;
            case WeatherPreset.LightSnow:
                rainIntensity = 0f; snowIntensity = 0.42f; fogIntensity = 0.40f; cloudiness = 0.70f; windStrength = 0.28f;
                break;
            case WeatherPreset.HeavySnow:
                rainIntensity = 0f; snowIntensity = 1.00f; fogIntensity = 0.76f; cloudiness = 0.95f; windStrength = 0.78f;
                break;
            case WeatherPreset.Custom:
                break;
        }

        if (!Application.isPlaying)
        {
            curRain = rainIntensity;
            curSnow = snowIntensity;
            curFog = fogIntensity;
            curCloud = cloudiness;
            curWind = windStrength;
            FollowActiveCamera(0f);
            ApplyWeatherImmediate(forcePrewarmIfStarting: true);
#if UNITY_EDITOR
            UnityEditor.SceneView.RepaintAll();
#endif
        }
    }

    /// <summary>
    /// Picks a random thunder strength weighted by current rain intensity and triggers synced flash + sound.
    /// </summary>
    public void TriggerRandomThunder()
    {
        float r = Random.value;
        ThunderStrength strength;
        if (curRain >= 0.70f)
        {
            // Heavy storm: 25% Weak, 40% Medium, 35% Heavy
            if (r < 0.25f) strength = ThunderStrength.Weak;
            else if (r < 0.65f) strength = ThunderStrength.Medium;
            else strength = ThunderStrength.Heavy;
        }
        else
        {
            // Light rain / distant storm: 68% Weak, 27% Medium, 5% Heavy
            if (r < 0.68f) strength = ThunderStrength.Weak;
            else if (r < 0.95f) strength = ThunderStrength.Medium;
            else strength = ThunderStrength.Heavy;
        }
        TriggerThunder(strength);
    }

    /// <summary>
    /// Triggers a lightning flash and matching thunder sound scaled by strength (Weak / Medium / Heavy).
    /// </summary>
    public AudioClip TriggerThunder(ThunderStrength strength, float overrideDelay = -1f)
    {
        LastThunderStrength = strength;
        activeFlashStrength = strength;
        flashElapsed = 0f;

        float autoDelay;
        switch (strength)
        {
            case ThunderStrength.Weak:
                // Distant soft glow + late soft rumble
                flashDuration     = 0.30f;
                flashPeakSun      = Random.Range(0.75f, 1.35f);
                flashPeakExposure = Random.Range(0.22f, 0.38f);
                flashPeakAmbient  = 0.14f;
                autoDelay         = Random.Range(1.50f, 2.65f);
                pendingThunderRelVol = Random.Range(0.45f, 0.62f);
                pendingThunderPitch  = Random.Range(0.84f, 0.96f);
                break;

            case ThunderStrength.Medium:
                // Noticeable double-flicker flash + medium delay rumble
                flashDuration     = 0.44f;
                flashPeakSun      = Random.Range(2.3f, 3.6f);
                flashPeakExposure = Random.Range(0.65f, 1.00f);
                flashPeakAmbient  = 0.38f;
                autoDelay         = Random.Range(0.55f, 1.25f);
                pendingThunderRelVol = Random.Range(0.72f, 0.88f);
                pendingThunderPitch  = Random.Range(0.92f, 1.04f);
                break;

            default: // Heavy
                // Blinding close triple-flicker lightning strike + near-instant thunder crack & boom
                flashDuration     = 0.60f;
                flashPeakSun      = Random.Range(5.2f, 7.6f);
                flashPeakExposure = Random.Range(1.45f, 2.15f);
                flashPeakAmbient  = 0.78f;
                autoDelay         = Random.Range(0.05f, 0.24f);
                pendingThunderRelVol = Random.Range(0.95f, 1.00f);
                pendingThunderPitch  = Random.Range(0.96f, 1.08f);
                break;
        }

        pendingThunderStrength = strength;
        pendingThunderDelay = overrideDelay >= 0f ? overrideDelay : autoDelay;

        // Immediately evaluate frame 0 of flash so light spikes right away
        EvaluateFlashEnvelope();
        ApplyAtmosphere();

        AudioClip chosenClip = PickThunderClip(strength);
        if (!Application.isPlaying)
        {
            pendingThunderDelay = -1f;
#if UNITY_EDITOR
            UnityEditor.SceneView.RepaintAll();
#endif
        }
        return chosenClip;
    }

    private void Update()
    {
        if (!Application.isPlaying)
            return;

        float dt = Time.deltaTime;
        Vector3 anchorPos = FollowActiveCamera(dt);
        float step = Mathf.Clamp01(dt * transitionSpeed);

        curRain  = Mathf.Lerp(curRain,  rainIntensity, step);
        curSnow  = Mathf.Lerp(curSnow,  snowIntensity, step);
        curFog   = Mathf.Lerp(curFog,   fogIntensity,  step);
        curCloud = Mathf.Lerp(curCloud, cloudiness,    step);
        curWind  = Mathf.Lerp(curWind,  windStrength,  step);

        // Automatic Storm Lightning + Tiered Thunder (active from LightRain onward)
        if (enableLightning && curRain > 0.30f)
        {
            lightningTimer -= dt;
            if (lightningTimer <= 0f)
            {
                TriggerRandomThunder();
                float stormFactor = Mathf.InverseLerp(0.30f, 1.0f, curRain);
                float minInt = Mathf.Lerp(minThunderInterval * 1.7f, minThunderInterval, stormFactor);
                float maxInt = Mathf.Lerp(maxThunderInterval * 1.8f, maxThunderInterval, stormFactor);
                lightningTimer = Random.Range(Mathf.Min(minInt, maxInt), Mathf.Max(minInt, maxInt));
            }
        }

        // Countdown delayed thunder sound (simulates speed of sound from strike distance)
        if (pendingThunderDelay >= 0f)
        {
            pendingThunderDelay -= dt;
            if (pendingThunderDelay <= 0f)
            {
                PlayThunderNow(pendingThunderStrength, pendingThunderRelVol, pendingThunderPitch);
                pendingThunderDelay = -1f;
            }
        }

        // Advance lightning multi-flicker envelope
        if (flashElapsed < flashDuration)
        {
            flashElapsed += dt;
            EvaluateFlashEnvelope();
        }
        else
        {
            curFlashSun = curFlashExposure = curFlashAmbient = curFlashColorBlend = 0f;
        }

        UpdateIndoorDetection(anchorPos, dt);
        UpdateAudio(dt);
        ApplyWeatherImmediate(forcePrewarmIfStarting: false);
    }

    private void EvaluateFlashEnvelope()
    {
        if (flashElapsed >= flashDuration || flashDuration <= 0.001f)
        {
            curFlashSun = curFlashExposure = curFlashAmbient = curFlashColorBlend = 0f;
            return;
        }

        float u = Mathf.Clamp01(flashElapsed / flashDuration);
        float shape;

        switch (activeFlashStrength)
        {
            case ThunderStrength.Weak:
                // Single soft distant sky pulse
                shape = Mathf.Sin(u * Mathf.PI);
                shape *= shape;
                break;

            case ThunderStrength.Medium:
                // Sharp initial flash + 1 quick secondary flicker
                float m1 = Mathf.Exp(-Mathf.Pow((u - 0.14f) / 0.11f, 2f));
                float m2 = 0.65f * Mathf.Exp(-Mathf.Pow((u - 0.48f) / 0.14f, 2f));
                shape = Mathf.Clamp01(Mathf.Max(m1, m2));
                break;

            default: // Heavy
                // Dramatic triple-strike lightning bolt flicker!
                float h1 = 1.00f * Mathf.Exp(-Mathf.Pow((u - 0.08f) / 0.065f, 2f));
                float h2 = 0.88f * Mathf.Exp(-Mathf.Pow((u - 0.28f) / 0.075f, 2f));
                float h3 = 0.55f * Mathf.Exp(-Mathf.Pow((u - 0.54f) / 0.14f, 2f));
                shape = Mathf.Clamp01(Mathf.Max(h1, Mathf.Max(h2, h3)));
                break;
        }

        curFlashSun        = flashPeakSun      * shape;
        curFlashExposure   = flashPeakExposure * shape;
        curFlashAmbient    = flashPeakAmbient  * shape;
        curFlashColorBlend = Mathf.Clamp01(shape * (activeFlashStrength == ThunderStrength.Heavy ? 1.0f : activeFlashStrength == ThunderStrength.Medium ? 0.65f : 0.30f));
    }

#if UNITY_EDITOR
    private void OnEditorUpdate()
    {
        if (this == null || !isActiveAndEnabled || Application.isPlaying)
            return;

        if (editorNeedsRefresh)
        {
            editorNeedsRefresh = false;
            ResolveReferences();
            FollowActiveCamera(0f);
            ApplyWeatherImmediate(forcePrewarmIfStarting: true);
            UnityEditor.SceneView.RepaintAll();
            return;
        }

        double now = UnityEditor.EditorApplication.timeSinceStartup;
        float dt = (float)(now - lastEditorTime);
        if (dt < 0.012f)
            return;
        lastEditorTime = now;
        dt = Mathf.Clamp(dt, 0.001f, 0.05f);

        FollowActiveCamera(0f);

        bool anyActive = false;

        if (flashElapsed < flashDuration)
        {
            flashElapsed += dt;
            EvaluateFlashEnvelope();
            ApplyAtmosphere();
            anyActive = true;
        }
        else if (curFlashSun > 0.0001f)
        {
            curFlashSun = curFlashExposure = curFlashAmbient = curFlashColorBlend = 0f;
            ApplyAtmosphere();
            anyActive = true;
        }

        if (editModePreview)
        {
            anyActive |= StepEditorParticle(rainNear, curRain > 0.01f, dt);
            anyActive |= StepEditorParticle(rainMid,  curRain > 0.01f, dt);
            anyActive |= StepEditorParticle(rainFar,  curRain > 0.01f, dt);
            anyActive |= StepEditorParticle(snowNear, curSnow > 0.01f, dt);
            anyActive |= StepEditorParticle(snowMid,  curSnow > 0.01f, dt);
            anyActive |= StepEditorParticle(snowFar,  curSnow > 0.01f, dt);
        }

        if (anyActive)
            UnityEditor.SceneView.RepaintAll();
    }

    private bool StepEditorParticle(ParticleSystem ps, bool shouldEmit, float dt)
    {
        if (ps == null) return false;
        if (!shouldEmit && ps.particleCount == 0) return false;
        ps.Simulate(dt, false, false, false);
        return true;
    }
#endif

    private void PauseAllParticlesForEditorSim()
    {
        PausePS(rainNear); PausePS(rainMid); PausePS(rainFar);
        PausePS(snowNear); PausePS(snowMid); PausePS(snowFar);
    }

    private void PausePS(ParticleSystem ps)
    {
        if (ps != null) ps.Pause(false);
    }

    private Vector3 FollowActiveCamera(float dt)
    {
        Vector3 anchor = transform.position;
        bool found = false;

        if (followTarget != null)
        {
            anchor = followTarget.position;
            found = true;
        }
        else if (Application.isPlaying)
        {
            Camera cam = Camera.main;
            if (cam == null || !cam.isActiveAndEnabled)
            {
                float bestDepth = float.NegativeInfinity;
                var allCams = Camera.allCameras;
                for (int i = 0; i < allCams.Length; i++)
                {
                    if (allCams[i] != null && allCams[i].isActiveAndEnabled && allCams[i].depth >= bestDepth)
                    {
                        bestDepth = allCams[i].depth;
                        cam = allCams[i];
                    }
                }
            }
            if (cam != null)
            {
                anchor = cam.transform.position;
                found = true;
            }
        }
#if UNITY_EDITOR
        else
        {
            var sv = UnityEditor.SceneView.lastActiveSceneView;
            if (sv != null && sv.camera != null)
            {
                anchor = sv.camera.transform.position;
                found = true;
            }
            else if (Camera.main != null)
            {
                anchor = Camera.main.transform.position;
                found = true;
            }
        }
#endif

        if (!found)
            return anchor;

        Vector3 lead = Vector3.zero;
        if (Application.isPlaying && dt > 0.0001f && hasLastAnchor)
        {
            Vector3 vel = (anchor - lastAnchorPos) / dt;
            vel.y = 0f;
            smoothedVelocity = Vector3.Lerp(smoothedVelocity, vel, Mathf.Clamp01(dt * 4f));
            lead = Vector3.ClampMagnitude(smoothedVelocity * velocityLeadTime, 35f);
        }
        lastAnchorPos = anchor;
        hasLastAnchor = true;

        float x = anchor.x + lead.x;
        float z = anchor.z + lead.z;

        SetPos(rainNear, new Vector3(x, anchor.y + nearHeightOffset, z));
        SetPos(rainMid,  new Vector3(x, anchor.y + midHeightOffset,  z));
        SetPos(rainFar,  new Vector3(x, anchor.y + farHeightOffset,  z));

        SetPos(snowNear, new Vector3(x, anchor.y + nearHeightOffset, z));
        SetPos(snowMid,  new Vector3(x, anchor.y + midHeightOffset,  z));
        SetPos(snowFar,  new Vector3(x, anchor.y + farHeightOffset,  z));

        return anchor;
    }

    private void SetPos(ParticleSystem ps, Vector3 pos)
    {
        if (ps != null) ps.transform.position = pos;
    }

    public void ApplyWeatherImmediate(bool forcePrewarmIfStarting = false)
    {
        float rad = windDirection * Mathf.Deg2Rad;
        Vector3 windVec = new Vector3(Mathf.Sin(rad), 0f, Mathf.Cos(rad));

        bool rainActiveNow = curRain > 0.01f;
        bool snowActiveNow = curSnow > 0.01f;

        bool prewarmRain = forcePrewarmIfStarting && rainActiveNow && !rainWasActive;
        bool prewarmSnow = forcePrewarmIfStarting && snowActiveNow && !snowWasActive;

        ApplyRainLayer(rainNear, curRain, 180f, rainNearMaxRate, windVec, 8.5f, -17f, -26f, prewarmRain, 2.2f);
        ApplyRainLayer(rainMid,  curRain, 260f, rainMidMaxRate,  windVec, 9.5f, -18f, -27f, prewarmRain, 3.0f);
        ApplyRainLayer(rainFar,  curRain, 160f, rainFarMaxRate,  windVec, 10f,  -19f, -28f, prewarmRain, 4.0f);

        ApplySnowLayer(snowNear, curSnow, 80f,  snowNearMaxRate, windVec, 4.5f, -1.6f, -3.2f, prewarmSnow, 8.5f);
        ApplySnowLayer(snowMid,  curSnow, 160f, snowMidMaxRate,  windVec, 5.2f, -1.8f, -3.5f, prewarmSnow, 11.0f);
        ApplySnowLayer(snowFar,  curSnow, 100f, snowFarMaxRate,  windVec, 5.8f, -2.1f, -3.8f, prewarmSnow, 14.0f);

        rainWasActive = rainActiveNow;
        snowWasActive = snowActiveNow;

        ApplyAtmosphere();
    }

    private void ApplyRainLayer(ParticleSystem ps, float intensity, float minRate, float maxRate,
        Vector3 windVec, float windScale, float minFallY, float maxFallY, bool prewarmNow, float prewarmSec)
    {
        if (ps == null) return;
        var em = ps.emission;
        float rate = intensity > 0.01f ? Mathf.Lerp(minRate, maxRate, intensity) : 0f;
        em.rateOverTime = rate;

        var vel = ps.velocityOverLifetime;
        vel.enabled = true;
        vel.space = ParticleSystemSimulationSpace.World;
        vel.x = new ParticleSystem.MinMaxCurve(windVec.x * curWind * windScale);
        vel.z = new ParticleSystem.MinMaxCurve(windVec.z * curWind * windScale);
        vel.y = new ParticleSystem.MinMaxCurve(Mathf.Lerp(minFallY, maxFallY, intensity));

        if (Application.isPlaying)
        {
            if (rate > 0f && !ps.isPlaying) ps.Play(false);
            else if (rate <= 0f && ps.isPlaying) ps.Stop(false, ParticleSystemStopBehavior.StopEmitting);
        }
        else
        {
            if (rate <= 0f)
            {
                ps.Clear(false);
            }
            else if (prewarmNow && editModePreview)
            {
                ps.Simulate(prewarmSec, false, true, false);
            }
        }
    }

    private void ApplySnowLayer(ParticleSystem ps, float intensity, float minRate, float maxRate,
        Vector3 windVec, float windScale, float minFallY, float maxFallY, bool prewarmNow, float prewarmSec)
    {
        if (ps == null) return;
        var em = ps.emission;
        float rate = intensity > 0.01f ? Mathf.Lerp(minRate, maxRate, intensity) : 0f;
        em.rateOverTime = rate;

        var vel = ps.velocityOverLifetime;
        vel.enabled = true;
        vel.space = ParticleSystemSimulationSpace.World;
        vel.x = new ParticleSystem.MinMaxCurve(windVec.x * curWind * windScale);
        vel.z = new ParticleSystem.MinMaxCurve(windVec.z * curWind * windScale);
        vel.y = new ParticleSystem.MinMaxCurve(Mathf.Lerp(minFallY, maxFallY, intensity));

        var noise = ps.noise;
        noise.enabled = true;
        noise.strength = Mathf.Lerp(0.75f, 2.2f, curWind);

        if (Application.isPlaying)
        {
            if (rate > 0f && !ps.isPlaying) ps.Play(false);
            else if (rate <= 0f && ps.isPlaying) ps.Stop(false, ParticleSystemStopBehavior.StopEmitting);
        }
        else
        {
            if (rate <= 0f)
            {
                ps.Clear(false);
            }
            else if (prewarmNow && editModePreview)
            {
                ps.Simulate(prewarmSec, false, true, false);
            }
        }
    }

    public void ApplyAtmosphereOnly()
    {
        ApplyAtmosphere();
    }

    private void ApplyAtmosphere()
    {
        float overcast = Mathf.Clamp01(Mathf.Max(curCloud, Mathf.Max(curRain * 0.95f, curSnow * 0.85f)));
        bool isSnowDominant = curSnow > curRain && curSnow > 0.02f;
        Color lightningTint = new Color(0.84f, 0.92f, 1.00f, 1f);

        // Read live Time-of-Day base values (if TimeOfDayController is attached)
        float dayFac = timeOfDay != null ? timeOfDay.DaylightFactor : 1f;
        Color baseClearFog   = timeOfDay != null ? timeOfDay.TimeFogColor    : new Color(0.68f, 0.80f, 0.92f, 1f);
        Color baseClearSky   = timeOfDay != null ? timeOfDay.TimeSkyTint     : new Color(0.46f, 0.56f, 0.68f, 1f);
        Color baseClearGnd   = timeOfDay != null ? timeOfDay.TimeGroundColor : new Color(0.36f, 0.38f, 0.36f, 1f);
        Color baseClearSun   = timeOfDay != null ? timeOfDay.TimeSunColor    : new Color(1.00f, 0.95f, 0.86f, 1f);
        float baseSunInt     = timeOfDay != null ? timeOfDay.TimeSunIntensity     : 2.30f;
        float baseSkyExp     = timeOfDay != null ? timeOfDay.TimeSkyExposure      : 1.25f;
        float baseAmbientInt = timeOfDay != null ? timeOfDay.TimeAmbientIntensity : 1.05f;

        // 1. Fog (darkens smoothly at night, brightens briefly during lightning)
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        RenderSettings.fogDensity = Mathf.Lerp(0.000025f, 0.0052f, curFog * curFog);

        Color stormFog = Color.Lerp(new Color(0.04f, 0.05f, 0.08f, 1f), new Color(0.32f, 0.37f, 0.43f, 1f), dayFac);
        Color snowFog  = Color.Lerp(new Color(0.10f, 0.13f, 0.19f, 1f), new Color(0.78f, 0.83f, 0.89f, 1f), dayFac);
        Color fogCol = isSnowDominant
            ? Color.Lerp(baseClearFog, snowFog, Mathf.Clamp01(curSnow + curFog * 0.45f))
            : Color.Lerp(baseClearFog, stormFog, Mathf.Clamp01(overcast * 0.85f + curRain * 0.25f));
        RenderSettings.fogColor = Color.Lerp(fogCol, lightningTint, curFlashColorBlend * 0.45f);

        // 2. Procedural Skybox Mood + Day/Night + Synced Lightning Exposure
        if (skyboxMaterial != null)
        {
            if (RenderSettings.skybox != skyboxMaterial)
                RenderSettings.skybox = skyboxMaterial;

            Color stormSkyTint = Color.Lerp(new Color(0.04f, 0.05f, 0.09f, 1f), new Color(0.24f, 0.28f, 0.34f, 1f), dayFac);
            Color snowSkyTint  = Color.Lerp(new Color(0.09f, 0.12f, 0.18f, 1f), new Color(0.60f, 0.64f, 0.70f, 1f), dayFac);
            Color skyTint = isSnowDominant
                ? Color.Lerp(baseClearSky, snowSkyTint, overcast)
                : Color.Lerp(baseClearSky, stormSkyTint, overcast);
            skyTint = Color.Lerp(skyTint, lightningTint, curFlashColorBlend * 0.55f);

            Color stormGround = Color.Lerp(new Color(0.02f, 0.03f, 0.05f, 1f), new Color(0.18f, 0.21f, 0.24f, 1f), dayFac);
            Color snowGround  = Color.Lerp(new Color(0.08f, 0.10f, 0.14f, 1f), new Color(0.66f, 0.70f, 0.75f, 1f), dayFac);
            Color groundTint = isSnowDominant
                ? Color.Lerp(baseClearGnd, snowGround, overcast)
                : Color.Lerp(baseClearGnd, stormGround, overcast);

            if (skyboxMaterial.HasProperty("_SkyTint"))
                skyboxMaterial.SetColor("_SkyTint", skyTint);
            if (skyboxMaterial.HasProperty("_GroundColor"))
                skyboxMaterial.SetColor("_GroundColor", groundTint);
            if (skyboxMaterial.HasProperty("_AtmosphereThickness"))
                skyboxMaterial.SetFloat("_AtmosphereThickness", Mathf.Lerp(0.95f, isSnowDominant ? 0.50f : 0.42f, overcast));

            float weatherExpMult = Mathf.Lerp(1.0f, isSnowDominant ? 0.85f : 0.48f, overcast);
            if (skyboxMaterial.HasProperty("_Exposure"))
                skyboxMaterial.SetFloat("_Exposure", baseSkyExp * weatherExpMult + curFlashExposure);
            if (skyboxMaterial.HasProperty("_SunSize"))
                skyboxMaterial.SetFloat("_SunSize", Mathf.Lerp(0.04f, 0.008f, overcast) * dayFac);
        }

        // 3. Sun Light & Ambient Intensity synced to Day/Night + Weather + Thunder
        float ambMult = Mathf.Lerp(1.0f, isSnowDominant ? 0.85f : 0.52f, overcast);
        RenderSettings.ambientIntensity = baseAmbientInt * ambMult + curFlashAmbient;

        if (sunLight != null)
        {
            Color stormSun = Color.Lerp(new Color(0.18f, 0.24f, 0.38f), new Color(0.56f, 0.64f, 0.76f), dayFac);
            Color snowSun  = Color.Lerp(new Color(0.26f, 0.34f, 0.48f), new Color(0.84f, 0.90f, 0.98f), dayFac);
            Color baseSun = isSnowDominant
                ? Color.Lerp(baseClearSun, snowSun, curSnow)
                : Color.Lerp(baseClearSun, stormSun, overcast);
            sunLight.color = Color.Lerp(baseSun, lightningTint, curFlashColorBlend);
            float sunMult = Mathf.Lerp(1.0f, isSnowDominant ? 0.48f : 0.26f, overcast);
            sunLight.intensity = baseSunInt * sunMult + curFlashSun;
        }
    }

    private void UpdateIndoorDetection(Vector3 anchorPos, float dt)
    {
        float target = 0f;
        if (enableIndoorMuffling)
        {
            if (Physics.Raycast(anchorPos + Vector3.up * 0.35f, Vector3.up, 16f, indoorRoofMask, QueryTriggerInteraction.Ignore))
                target = 1f;
        }
        indoorFactor = Mathf.MoveTowards(indoorFactor, target, dt * 4.5f);
    }

    private void UpdateAudio(float dt)
    {
        float volMult = muteAudio ? 0f : masterVolume;
        float indoorVolMult = Mathf.Lerp(1f, 0.50f, indoorFactor);
        float cutoff = Mathf.Lerp(22000f, indoorCutoffFreq, indoorFactor);

        float lightRainTarget = curRain > 0.01f
            ? Mathf.Clamp01(curRain * 2.2f) * Mathf.Lerp(1f, 0.45f, Mathf.Clamp01((curRain - 0.45f) / 0.55f)) * 0.72f
            : 0f;
        float heavyRainTarget = curRain > 0.22f
            ? Mathf.SmoothStep(0.22f, 1.0f, curRain) * 0.92f
            : 0f;
        float windBoost = 1f + curSnow * 0.25f + curRain * 0.15f;
        float windTarget = curWind > 0.05f
            ? Mathf.Pow(curWind, 1.2f) * 0.62f * windBoost
            : 0f;

        UpdateLoopSource(rainLightSource, rainLightClip, rainLightLPF, lightRainTarget * volMult * indoorVolMult, cutoff, Mathf.Lerp(0.95f, 1.04f, curRain), dt);
        UpdateLoopSource(rainHeavySource, rainHeavyClip, rainHeavyLPF, heavyRainTarget * volMult * indoorVolMult, cutoff, Mathf.Lerp(0.94f, 1.05f, curRain), dt);
        UpdateLoopSource(windSource,      windClip,      windLPF,      windTarget      * volMult * indoorVolMult, cutoff, Mathf.Lerp(0.88f, 1.12f, curWind), dt);
    }

    private void UpdateLoopSource(AudioSource src, AudioClip clip, AudioLowPassFilter lpf, float targetVol, float cutoff, float pitch, float dt)
    {
        if (src == null) return;
        if (src.clip == null && clip != null) src.clip = clip;
        if (src.clip == null) return;

        src.loop = true;
        src.spatialBlend = 0f;
        src.pitch = pitch;

        if (lpf != null)
            lpf.cutoffFrequency = cutoff;

        if (targetVol > 0.004f)
        {
            if (!src.isPlaying)
            {
                src.volume = 0f;
                src.Play();
            }
            src.volume = Mathf.MoveTowards(src.volume, targetVol, dt * 1.8f);
        }
        else
        {
            src.volume = Mathf.MoveTowards(src.volume, 0f, dt * 1.8f);
            if (src.volume <= 0.002f && src.isPlaying)
                src.Stop();
        }
    }

    private void StopLoopAudio()
    {
        if (rainLightSource != null && rainLightSource.isPlaying) rainLightSource.Stop();
        if (rainHeavySource != null && rainHeavySource.isPlaying) rainHeavySource.Stop();
        if (windSource != null && windSource.isPlaying) windSource.Stop();
    }

    public AudioClip PickThunderClip(ThunderStrength strength)
    {
        AudioClip[] pool = null;
        switch (strength)
        {
            case ThunderStrength.Weak:   pool = weakThunderClips;   break;
            case ThunderStrength.Medium: pool = mediumThunderClips; break;
            case ThunderStrength.Heavy:  pool = heavyThunderClips;  break;
        }
        if (pool == null || pool.Length == 0)
            pool = thunderClips;
        if (pool == null || pool.Length == 0)
            return null;

        return pool[Random.Range(0, pool.Length)];
    }

    private void PlayThunderNow(ThunderStrength strength, float relativeVolume, float pitch)
    {
        if (muteAudio || thunderSource == null)
            return;

        AudioClip clip = PickThunderClip(strength);
        if (clip == null) return;

        thunderSource.enabled = true;
        thunderSource.mute = false;
        thunderSource.volume = 1f;
        thunderSource.spatialBlend = 0f;
        thunderSource.pitch = pitch;
        float indoorDamp = Mathf.Lerp(1f, 0.72f, indoorFactor);
        thunderSource.PlayOneShot(clip, Mathf.Clamp01(relativeVolume * thunderVolume * masterVolume * indoorDamp));
    }

    private void ResolveReferences()
    {
        if (sunLight == null)
        {
            var lights = FindObjectsByType<Light>();
            for (int i = 0; i < lights.Length; i++)
            {
                if (lights[i].type == LightType.Directional) { sunLight = lights[i]; break; }
            }
        }

        rainNear = FindChildPS("Rain_Near", rainNear);
        rainMid  = FindChildPS("Rain_Mid",  rainMid);
        rainFar  = FindChildPS("Rain_Far",  rainFar);
        snowNear = FindChildPS("Snow_Near", snowNear);
        snowMid  = FindChildPS("Snow_Mid",  snowMid);
        snowFar  = FindChildPS("Snow_Far",  snowFar);

        rainLightSource = FindChildAudio("Audio_RainLight", rainLightSource);
        rainHeavySource = FindChildAudio("Audio_RainHeavy", rainHeavySource);
        windSource      = FindChildAudio("Audio_Wind",      windSource);
        thunderSource   = FindChildAudio("Audio_Thunder",   thunderSource);

        if (rainLightSource != null) rainLightLPF = rainLightSource.GetComponent<AudioLowPassFilter>();
        if (rainHeavySource != null) rainHeavyLPF = rainHeavySource.GetComponent<AudioLowPassFilter>();
        if (windSource != null)      windLPF      = windSource.GetComponent<AudioLowPassFilter>();
    }

    private ParticleSystem FindChildPS(string childName, ParticleSystem current)
    {
        if (current != null) return current;
        Transform t = transform.Find(childName);
        return t != null ? t.GetComponent<ParticleSystem>() : null;
    }

    private AudioSource FindChildAudio(string childName, AudioSource current)
    {
        if (current != null) return current;
        Transform t = transform.Find(childName);
        return t != null ? t.GetComponent<AudioSource>() : null;
    }
}
