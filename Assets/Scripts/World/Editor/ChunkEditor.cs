#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using EndlessSurvival.World.Road;

namespace EndlessSurvival.World.Editor
{
    /// <summary>
    /// Regenerates the chunk's road with random curves whenever Road Type / Cross Section /
    /// Elevation Type is changed in the Inspector, and offers a manual re-roll button.
    /// </summary>
    [CustomEditor(typeof(Chunk))]
    public class ChunkEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            Chunk chunk = (Chunk)target;
            var oldRoad = chunk.roadType;
            var oldCross = chunk.crossSectionType;
            var oldElevation = chunk.currentElevationType;
            float oldParam = chunk.currentElevationParam;
            string oldSeed = chunk.editorPreviewSeed;

            DrawDefaultInspector();

            bool roadChanged = chunk.roadType != oldRoad
                || chunk.currentElevationType != oldElevation
                || chunk.editorPreviewSeed != oldSeed
                || !Mathf.Approximately(chunk.currentElevationParam, oldParam);
            bool crossChanged = chunk.crossSectionType != oldCross;

            if (roadChanged)
            {
                Regenerate(chunk);
            }
            else if (crossChanged)
            {
                var gen = chunk.GetComponentInChildren<RoadGenerator>();
                if (gen != null) gen.ApplyCrossSectionPreset(chunk.crossSectionType);
            }

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Editor Preview & Vegetation Controls", EditorStyles.boldLabel);

            GUI.backgroundColor = new Color(0.3f, 0.8f, 0.4f);
            if (GUILayout.Button("Randomize Road (Yeniden Rastgele)", GUILayout.Height(28)))
            {
                Regenerate(chunk);
            }

            GUI.backgroundColor = new Color(0.2f, 0.7f, 1.0f);
            if (GUILayout.Button("Refresh All Preview (Ağaçlar + Kamp + Loot + Kenarlar)", GUILayout.Height(28)))
            {
                chunk.GenerateEditorPreview(chunk.GetEditorPreviewSeed());
            }

            GUI.backgroundColor = new Color(0.4f, 0.85f, 0.5f);
            if (GUILayout.Button("Regenerate Trees & Vegetation", GUILayout.Height(26)))
            {
                var spawner = chunk.GetComponentInChildren<ChunkVegetationSpawner>();
                if (spawner != null) spawner.GenerateVegetation();
            }

            GUI.backgroundColor = new Color(1.0f, 0.4f, 0.4f);
            if (GUILayout.Button("Clear Preview", GUILayout.Height(22)))
            {
                Transform preview = chunk.transform.Find("EditorPreview");
                if (preview != null) DestroyImmediate(preview.gameObject);
                var spawner = chunk.GetComponentInChildren<ChunkVegetationSpawner>();
                if (spawner != null) spawner.ClearVegetation();
            }

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Prefab Kayıt ve Onarım (Overrides)", EditorStyles.boldLabel);

            GUI.backgroundColor = new Color(0.1f, 0.85f, 0.45f);
            if (GUILayout.Button("💾 Geçersiz Scriptleri Onar & Prefab'a Kaydet", GUILayout.Height(32)))
            {
                PrefabMissingScriptFixer.CleanAndApplyToPrefab(chunk);
            }

            GUI.backgroundColor = Color.white;
        }

        /// <summary>Rolls a new random road matching the chunk's road type and rebuilds mesh + terrain.</summary>
        public static void Regenerate(Chunk chunk)
        {
            var gen = chunk.GetComponentInChildren<RoadGenerator>();
            var spline = chunk.GetComponentInChildren<RoadSpline>();
            if (gen == null || spline == null) return;

            var manager = Object.FindAnyObjectByType<ChunkManager>();
            RoadCurveSettings settings = manager != null ? manager.curveSettings : new RoadCurveSettings();

            bool curvy = chunk.roadType == ChunkRoadType.CurvedLeft
                || chunk.roadType == ChunkRoadType.CurvedRight
                || chunk.roadType == ChunkRoadType.HazardZone;
            bool sharp = chunk.roadType == ChunkRoadType.HazardZone;

            float param = chunk.currentElevationParam;
            if (chunk.currentElevationType != RoadElevationType.Flat && param <= 0f) param = 12f;

            Undo.RecordObject(spline, "Randomize Road");
            
            int seed = string.IsNullOrEmpty(chunk.editorPreviewSeed) 
                ? new System.Random().Next() 
                : SeededRandom.HashString(chunk.editorPreviewSeed);
            var editorRng = new SeededRandom(seed);
            
            float peak = spline.SetProceduralPreset(settings, curvy, sharp, chunk.currentElevationType,
                param, chunk.currentElevationType == RoadElevationType.RollingHills ? param * 0.7f : param, editorRng);

            // Honor requested side: CurvedRight = biggest bend to +X, CurvedLeft = -X
            bool wantRight = chunk.roadType == ChunkRoadType.CurvedRight;
            bool wantLeft = chunk.roadType == ChunkRoadType.CurvedLeft;
            if ((wantRight && peak < 0f) || (wantLeft && peak > 0f))
            {
                for (int i = 0; i < spline.waypoints.Count; i++)
                {
                    Vector3 p = spline.waypoints[i];
                    p.x = -p.x;
                    spline.waypoints[i] = p;
                }
            }
            EditorUtility.SetDirty(spline);

            gen.ApplyCrossSectionPreset(chunk.crossSectionType);

            Terrain terrain = chunk.GetComponentInChildren<Terrain>();
            if (terrain != null)
            {
                RoadTerrainAdapter.ConformTerrainToRoad(terrain, spline, gen, true);
            }

            var vegSpawner = chunk.GetComponentInChildren<ChunkVegetationSpawner>();
            if (vegSpawner != null)
            {
                vegSpawner.GenerateVegetation();
            }

            chunk.GenerateEditorPreview(seed);
        }
    }
}
#endif
