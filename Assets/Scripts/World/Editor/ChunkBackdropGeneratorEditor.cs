#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace EndlessSurvival.World.Editor
{
    /// <summary>
    /// Rebuilds only the background mountains of the chunk preview when a backdrop setting changes.
    /// </summary>
    [CustomEditor(typeof(ChunkBackdropGenerator))]
    public class ChunkBackdropGeneratorEditor : UnityEditor.Editor
    {
        private static bool _refreshQueued;

        public override void OnInspectorGUI()
        {
            var backdrop = (ChunkBackdropGenerator)target;

            EditorGUI.BeginChangeCheck();
            DrawDefaultInspector();
            if (EditorGUI.EndChangeCheck() && !Application.isPlaying && !_refreshQueued)
            {
                // Debounced so dragging a slider does not rebuild every frame
                _refreshQueued = true;
                EditorApplication.delayCall += () =>
                {
                    _refreshQueued = false;
                    Refresh(backdrop);
                };
            }

            EditorGUILayout.Space(8);
            GUI.backgroundColor = new Color(0.5f, 0.65f, 0.9f);
            if (GUILayout.Button("Arka Plan Dağlarını Yeniden Üret", GUILayout.Height(28)))
            {
                Refresh(backdrop);
            }
            GUI.backgroundColor = Color.white;
        }

        private static void Refresh(ChunkBackdropGenerator backdrop)
        {
            if (backdrop == null || Application.isPlaying || EditorUtility.IsPersistent(backdrop)) return;
            Chunk chunk = backdrop.GetComponent<Chunk>();
            if (chunk == null) chunk = backdrop.GetComponentInParent<Chunk>();
            if (chunk == null) return;

            // Mountains live in the preview container if there is one, otherwise directly under the chunk
            Transform parent = chunk.transform.Find("EditorPreview");
            backdrop.Generate(parent != null ? parent : chunk.transform, chunk.ChunkTerrain);
            SceneView.RepaintAll();
        }
    }
}
#endif
