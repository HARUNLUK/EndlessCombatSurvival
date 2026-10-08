#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace EndlessSurvival.World.Editor
{
    /// <summary>
    /// Refreshes the chunk's scene preview (terrain cliffs + boundary rocks) when a boundary setting changes,
    /// and shows which style each side rolled for the current seed.
    /// </summary>
    [CustomEditor(typeof(ChunkBoundaryGenerator))]
    public class ChunkBoundaryGeneratorEditor : UnityEditor.Editor
    {
        private static bool _refreshQueued;

        public override void OnInspectorGUI()
        {
            var boundary = (ChunkBoundaryGenerator)target;

            EditorGUI.BeginChangeCheck();
            DrawDefaultInspector();
            if (EditorGUI.EndChangeCheck())
            {
                QueueRefresh(boundary);
            }

            EditorGUILayout.Space(8);
            if (boundary.HasPlan)
            {
                EditorGUILayout.HelpBox($"Sol kenar: {boundary.LeftStyle}    |    Sağ kenar: {boundary.RightStyle}", MessageType.Info);
            }

            GUI.backgroundColor = new Color(0.55f, 0.75f, 0.45f);
            if (GUILayout.Button("Kenarları Yeniden Üret (Uçurum + Kaya)", GUILayout.Height(28)))
            {
                Refresh(boundary);
            }
            GUI.backgroundColor = Color.white;
        }

        // Debounced so dragging a slider does not rebuild the whole preview every frame
        private static void QueueRefresh(ChunkBoundaryGenerator boundary)
        {
            if (Application.isPlaying || _refreshQueued) return;
            _refreshQueued = true;
            EditorApplication.delayCall += () =>
            {
                _refreshQueued = false;
                Refresh(boundary);
            };
        }

        private static void Refresh(ChunkBoundaryGenerator boundary)
        {
            if (boundary == null || Application.isPlaying || EditorUtility.IsPersistent(boundary)) return;
            Chunk chunk = boundary.GetComponent<Chunk>();
            if (chunk == null) chunk = boundary.GetComponentInParent<Chunk>();
            if (chunk == null) return;

            chunk.GenerateEditorPreview(chunk.GetEditorPreviewSeed());
            SceneView.RepaintAll();
        }
    }
}
#endif
