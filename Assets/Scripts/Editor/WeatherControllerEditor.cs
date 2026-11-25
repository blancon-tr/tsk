using System;
using System.Reflection;
using UnityEngine;
using UnityEditor;

[CustomEditor(typeof(WeatherController))]
public class WeatherControllerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        var wc = (WeatherController)target;
        serializedObject.Update();

        EditorGUILayout.LabelField("Hızlı Hava Durumu Seçimi", EditorStyles.boldLabel);
        EditorGUILayout.BeginHorizontal();
        PresetButton(wc, "☀ Güneşli", WeatherPreset.Clear);
        PresetButton(wc, "☁ Bulutlu", WeatherPreset.Cloudy);
        PresetButton(wc, "🌫 Sisli",   WeatherPreset.Foggy);
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();
        PresetButton(wc, "🌧 Hafif Yağmur",    WeatherPreset.LightRain);
        PresetButton(wc, "⛈ Şiddetli Yağmur", WeatherPreset.HeavyRain);
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();
        PresetButton(wc, "❄ Hafif Kar", WeatherPreset.LightSnow);
        PresetButton(wc, "🌨 Yoğun Kar", WeatherPreset.HeavySnow);
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("⚡ Şimşek & Gök Gürültüsü Testi (Işık + Ses Senkron)", EditorStyles.boldLabel);
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("⚡ Hafif (Uzak)"))
            TriggerAndPreviewThunder(wc, ThunderStrength.Weak);
        if (GUILayout.Button("⚡ Orta Şiddet"))
            TriggerAndPreviewThunder(wc, ThunderStrength.Medium);
        if (GUILayout.Button("⚡ Şiddetli Yıldırım!"))
            TriggerAndPreviewThunder(wc, ThunderStrength.Heavy);
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("Ses Kontrolleri", EditorStyles.boldLabel);
        EditorGUILayout.BeginHorizontal();
        string muteLabel = wc.muteAudio ? "🔇 Ses Kapalı (Aç)" : "🔊 Ses Açık (Kapat)";
        if (GUILayout.Button(muteLabel))
        {
            Undo.RecordObject(wc, "Toggle Weather Mute");
            wc.muteAudio = !wc.muteAudio;
            EditorUtility.SetDirty(wc);
        }
        if (!Application.isPlaying)
        {
            if (GUILayout.Button("▶ Hafif Yağmur")) PreviewEditorClip(wc.rainLightClip);
            if (GUILayout.Button("▶ Sağanak"))      PreviewEditorClip(wc.rainHeavyClip);
            if (GUILayout.Button("▶ Rüzgar"))       PreviewEditorClip(wc.windClip);
            if (GUILayout.Button("■ Durdur"))       StopAllEditorClips();
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(4);
        if (Application.isPlaying)
        {
            string roofState = wc.IsIndoors ? "İç Mekân (Çatı Altı Boğuk Ses Aktif)" : "Açık Alan (Tam Ses)";
            EditorGUILayout.HelpBox("Play Mode Aktif — " + roofState + " | Son Gök Gürültüsü: " + wc.LastThunderStrength, MessageType.Info);
        }
        else
        {
            EditorGUILayout.HelpBox(
                "Edit Mode: Şimşek butonları hem ışık şiddetini gökyüzünde canlı çaktırır hem de seçilen şiddette gök gürültüsü sesini çalar. " +
                "Play Mode'da yağış şiddetine göre hafif, orta ve şiddetli yıldırımlar otomatik karışık çakar.",
                MessageType.None);
        }

        EditorGUILayout.Space(6);

        float prevRain  = wc.rainIntensity;
        float prevSnow  = wc.snowIntensity;
        float prevFog   = wc.fogIntensity;
        float prevCloud = wc.cloudiness;
        float prevWind  = wc.windStrength;
        WeatherPreset prevPreset = wc.weatherPreset;

        EditorGUI.BeginChangeCheck();
        DrawDefaultInspector();
        if (EditorGUI.EndChangeCheck())
        {
            if (wc.weatherPreset != prevPreset && wc.weatherPreset != WeatherPreset.Custom)
            {
                wc.ApplyPreset(wc.weatherPreset);
            }
            else if (!Mathf.Approximately(wc.rainIntensity, prevRain) ||
                     !Mathf.Approximately(wc.snowIntensity, prevSnow) ||
                     !Mathf.Approximately(wc.fogIntensity,  prevFog)  ||
                     !Mathf.Approximately(wc.cloudiness,    prevCloud)||
                     !Mathf.Approximately(wc.windStrength,  prevWind))
            {
                wc.weatherPreset = WeatherPreset.Custom;
                wc.ApplyWeatherImmediate(forcePrewarmIfStarting: true);
            }
            else
            {
                wc.ApplyWeatherImmediate(forcePrewarmIfStarting: false);
            }
            EditorUtility.SetDirty(wc);
            SceneView.RepaintAll();
        }
    }

    private void TriggerAndPreviewThunder(WeatherController wc, ThunderStrength strength)
    {
        AudioClip clip = wc.TriggerThunder(strength);
        if (!Application.isPlaying && clip != null)
        {
            if (!PreviewEditorClip(clip) && wc.thunderSource != null)
            {
                wc.thunderSource.volume = 1f;
                wc.thunderSource.spatialBlend = 0f;
                wc.thunderSource.PlayOneShot(clip, Mathf.Clamp01(wc.masterVolume * wc.thunderVolume));
            }
        }
    }

    private void PresetButton(WeatherController wc, string label, WeatherPreset preset)
    {
        Color oldBg = GUI.backgroundColor;
        if (wc.weatherPreset == preset)
            GUI.backgroundColor = new Color(0.55f, 0.85f, 1.0f);

        if (GUILayout.Button(label))
        {
            Undo.RecordObject(wc, "Weather Preset " + preset);
            wc.ApplyPreset(preset);
            EditorUtility.SetDirty(wc);
        }
        GUI.backgroundColor = oldBg;
    }

    private static bool PreviewEditorClip(AudioClip clip)
    {
        if (clip == null) return false;
        StopAllEditorClips();
        Type audioUtilType = typeof(AudioImporter).Assembly.GetType("UnityEditor.AudioUtil");
        if (audioUtilType == null) return false;

        BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        MethodInfo method = audioUtilType.GetMethod("PlayPreviewClip", flags, null,
            new Type[] { typeof(AudioClip), typeof(int), typeof(bool) }, null);
        if (method != null)
        {
            method.Invoke(null, new object[] { clip, 0, false });
            return true;
        }

        MethodInfo legacy = audioUtilType.GetMethod("PlayClip", flags, null,
            new Type[] { typeof(AudioClip), typeof(int), typeof(bool) }, null);
        if (legacy != null)
        {
            legacy.Invoke(null, new object[] { clip, 0, false });
            return true;
        }

        return false;
    }

    private static void StopAllEditorClips()
    {
        Type audioUtilType = typeof(AudioImporter).Assembly.GetType("UnityEditor.AudioUtil");
        if (audioUtilType == null) return;

        BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        MethodInfo method = audioUtilType.GetMethod("StopAllPreviewClips", flags, null, Type.EmptyTypes, null)
                         ?? audioUtilType.GetMethod("StopAllClips", flags, null, Type.EmptyTypes, null);
        if (method != null)
        {
            method.Invoke(null, null);
        }
    }
}
