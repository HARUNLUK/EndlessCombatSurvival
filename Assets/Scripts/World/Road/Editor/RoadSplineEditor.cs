using UnityEditor;
using UnityEngine;

namespace EndlessSurvival.World.Road.Editor
{
    [CustomEditor(typeof(RoadGenerator))]
    public class RoadSplineEditor : UnityEditor.Editor
    {
        private RoadGenerator _generator;
        private RoadSpline _spline;

        private void OnEnable()
        {
            _generator = (RoadGenerator)target;
            if (_generator != null)
            {
                _spline = _generator.GetComponent<RoadSpline>();
            }
        }

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            EditorGUILayout.Space(12);
            EditorGUILayout.LabelField("Road Curve Presets", EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Straight"))
            {
                Undo.RecordObject(_spline, "Set Straight Preset");
                _spline.SetStraightPreset();
                _generator.BuildRoadMesh();
            }

            if (GUILayout.Button("Gentle S-Curve"))
            {
                Undo.RecordObject(_spline, "Set S-Curve Preset");
                _spline.SetSCurvePreset(45f);
                _generator.BuildRoadMesh();
            }

            if (GUILayout.Button("Elevation Hill"))
            {
                Undo.RecordObject(_spline, "Set Hill Preset");
                _spline.SetElevationHillPreset(12f);
                _generator.BuildRoadMesh();
            }

            if (GUILayout.Button("Chicane"))
            {
                Undo.RecordObject(_spline, "Set Chicane Preset");
                _spline.SetChicanePreset(35f);
                _generator.BuildRoadMesh();
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(8);
            GUI.backgroundColor = new Color(0.3f, 0.8f, 0.4f);
            if (GUILayout.Button("Rebuild Road Mesh Now", GUILayout.Height(32)))
            {
                _generator.BuildRoadMesh();
            }
            GUI.backgroundColor = Color.white;
        }

        private void OnSceneGUI()
        {
            if (_spline == null || _spline.waypoints == null) return;

            Transform tr = _spline.transform;

            for (int i = 0; i < _spline.waypoints.Count; i++)
            {
                Vector3 worldPt = tr.TransformPoint(_spline.waypoints[i]);

                EditorGUI.BeginChangeCheck();
                Vector3 newWorldPt = Handles.PositionHandle(worldPt, tr.rotation);

                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(_spline, "Move Road Waypoint");
                    _spline.waypoints[i] = tr.InverseTransformPoint(newWorldPt);
                    EditorUtility.SetDirty(_spline);
                    _generator.BuildRoadMesh();
                }

                Handles.Label(worldPt + Vector3.up * 1.5f, $"P{i}", EditorStyles.boldLabel);
            }
        }
    }
}
