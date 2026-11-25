using UnityEngine;
using UnityEditor;

[CustomEditor(typeof(TimeOfDayController))]
public class TimeOfDayControllerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        var tod = (TimeOfDayController)target;

        EditorGUILayout.LabelField("Gün & Saat Hızlı Kontrol", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            tod.currentDay.ToString() + " (" + tod.dayCount + ". Gün)  —  Saat " + tod.GetFormattedTime() +
            "  |  " + tod.GetWeatherLabelTurkish(),
            MessageType.Info);

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("🌅 Sabah (07:00)")) { Undo.RecordObject(tod, "Time Morning"); tod.SetTime(7.0f);  EditorUtility.SetDirty(tod); }
        if (GUILayout.Button("☀ Öğle (13:00)"))  { Undo.RecordObject(tod, "Time Noon");    tod.SetTime(13.0f); EditorUtility.SetDirty(tod); }
        if (GUILayout.Button("🌇 Akşam (18:30)")) { Undo.RecordObject(tod, "Time Sunset");  tod.SetTime(18.5f); EditorUtility.SetDirty(tod); }
        if (GUILayout.Button("🌙 Gece (23:00)"))  { Undo.RecordObject(tod, "Time Night");   tod.SetTime(23.0f); EditorUtility.SetDirty(tod); }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("⏭ +1 Saat"))
        {
            Undo.RecordObject(tod, "Advance 1 Hour");
            tod.AdvanceHours(1f);
            tod.UpdateSunAndLightingValues();
            if (tod.weatherController != null) tod.weatherController.ApplyAtmosphereOnly();
            EditorUtility.SetDirty(tod);
            SceneView.RepaintAll();
        }
        if (GUILayout.Button("📅 Sonraki Gün"))
        {
            Undo.RecordObject(tod, "Next Day");
            tod.NextDay();
            tod.UpdateSunAndLightingValues();
            if (tod.weatherController != null) tod.weatherController.ApplyAtmosphereOnly();
            EditorUtility.SetDirty(tod);
            SceneView.RepaintAll();
        }
        if (GUILayout.Button("🌦 Havayı Değiştir"))
        {
            tod.PickWeatherForCurrentHour();
            SceneView.RepaintAll();
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(6);
        EditorGUI.BeginChangeCheck();
        DrawDefaultInspector();
        if (EditorGUI.EndChangeCheck())
        {
            tod.UpdateSunAndLightingValues();
            if (tod.weatherController != null) tod.weatherController.ApplyAtmosphereOnly();
            EditorUtility.SetDirty(tod);
            SceneView.RepaintAll();
        }
    }
}
