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

            DrawDefaultInspector();

            bool roadChanged = chunk.roadType != oldRoad
                || chunk.currentElevationType != oldElevation
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
            GUI.backgroundColor = new Color(0.3f, 0.8f, 0.4f);
            if (GUILayout.Button("Randomize Road (Yeniden Rastgele)", GUILayout.Height(28)))
            {
                Regenerate(chunk);
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
            float peak = spline.SetProceduralPreset(settings, curvy, sharp, chunk.currentElevationType,
                param, chunk.currentElevationType == RoadElevationType.RollingHills ? param * 0.7f : param);

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
        }
    }
}
#endif
