using BalloonFlow;
using System;
using System.IO;
using UnityEditor;
using UnityEngine;

public static class DartTrailPrefabSetup
{
    private const string DartPrefabPath = "Assets/Resources/Prefabs/Dart.prefab";
    private const string PreferredTrailMaterialPath = "Assets/3.Material/DartTailShared.mat";
    private const string FallbackTrailMaterialPath = "Assets/3.Material/Trail.mat";
    private const string TrailChildName = "FlightTrail";

    [MenuItem("BalloonFlow/Setup Dart Flight Trail")]
    public static void SetupDartFlightTrail()
    {
        SetupDartFlightTrailInternal(true);
    }

    private static void SetupDartFlightTrailInternal(bool showDialog)
    {
        GameObject root = PrefabUtility.LoadPrefabContents(DartPrefabPath);
        if (root == null)
        {
            Debug.LogError($"Dart prefab not found: {DartPrefabPath}");
            return;
        }

        try
        {
            DartIdentifier identifier = root.GetComponent<DartIdentifier>();
            if (identifier == null)
                identifier = root.AddComponent<DartIdentifier>();

            Transform trailTransform = root.transform.Find(TrailChildName);
            if (trailTransform == null)
            {
                var trailObject = new GameObject(TrailChildName);
                trailTransform = trailObject.transform;
                trailTransform.SetParent(root.transform, false);
                trailTransform.localPosition = Vector3.zero;
                trailTransform.localRotation = Quaternion.identity;
                trailTransform.localScale = Vector3.one;
            }

            TrailRenderer trail = trailTransform.GetComponent<TrailRenderer>();
            if (trail == null)
                trail = trailTransform.gameObject.AddComponent<TrailRenderer>();

            Material material = AssetDatabase.LoadAssetAtPath<Material>(PreferredTrailMaterialPath);
            if (material == null)
                material = AssetDatabase.LoadAssetAtPath<Material>(FallbackTrailMaterialPath);

            trail.emitting = false;
            trail.time = 0.08f;
            trail.startWidth = 0.055f;
            trail.endWidth = 0f;
            trail.minVertexDistance = 0.025f;
            trail.autodestruct = false;
            trail.startColor = Color.white;
            trail.endColor = new Color(1f, 1f, 1f, 0f);
            trail.generateLightingData = false;
            trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            trail.receiveShadows = false;
            trail.sharedMaterial = material;
            trail.Clear();

            var serialized = new SerializedObject(identifier);
            serialized.FindProperty("_flightTrail").objectReferenceValue = trail;
            serialized.FindProperty("_flightTrailMaterial").objectReferenceValue = material;
            serialized.FindProperty("_flightTrailTime").floatValue = 0.08f;
            serialized.FindProperty("_flightTrailStartWidth").floatValue = 0.055f;
            serialized.FindProperty("_flightTrailEndWidth").floatValue = 0f;
            serialized.FindProperty("_flightTrailMinVertexDistance").floatValue = 0.025f;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            PrefabUtility.SaveAsPrefabAsset(root, DartPrefabPath);
            string message = $"Dart flight trail setup complete. Material: {(material != null ? material.name : "none")}";
            Debug.Log(message);
            WriteSetupLog(message);
            if (showDialog && !Application.isBatchMode)
                EditorUtility.DisplayDialog("Dart Flight Trail", message, "OK");
        }
        catch (Exception ex)
        {
            WriteSetupLog(ex.ToString());
            throw;
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static void WriteSetupLog(string message)
    {
        try
        {
            Directory.CreateDirectory("Temp");
            File.WriteAllText("Temp/DartTrailPrefabSetup.log", $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}");
        }
        catch
        {
            // Best-effort editor confirmation only.
        }
    }
}
