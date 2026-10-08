#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace EndlessSurvival.World.Editor
{
    /// <summary>
    /// Rebuilds the chunk preview (terrain for streams + fences/trees/corn) when a field edge setting changes,
    /// and shows which style each side rolled for the current seed.
    /// </summary>
    [CustomEditor(typeof(FieldEdgeGenerator))]
    public class FieldEdgeGeneratorEditor : UnityEditor.Editor
    {
        private static bool _refreshQueued;

        public override void OnInspectorGUI()
        {
            var field = (FieldEdgeGenerator)target;

            EditorGUI.BeginChangeCheck();
            DrawDefaultInspector();
            if (EditorGUI.EndChangeCheck() && !Application.isPlaying && !_refreshQueued)
            {
                // Debounced so dragging a slider does not rebuild every frame
                _refreshQueued = true;
                EditorApplication.delayCall += () =>
                {
                    _refreshQueued = false;
                    Refresh(field);
                };
            }

            EditorGUILayout.Space(8);
            if (field.IsActive)
            {
                EditorGUILayout.HelpBox($"Sol kenar: {field.LeftStyle}    |    Sağ kenar: {field.RightStyle}\n" +
                                        $"Nehir + köprü: {(field.HasRiver ? $"var (köprü altı {field.BridgeClearance:0.#} m)" : "yok")}", MessageType.Info);
            }
            else
            {
                EditorGUILayout.HelpBox("Chunk'ın Biome Type'ı Field değil; bu bileşen sadece düzlük chunk'larında çalışır.", MessageType.None);
            }

            GUI.backgroundColor = new Color(0.75f, 0.7f, 0.35f);
            if (GUILayout.Button("Düzlük Kenarlarını Yeniden Üret", GUILayout.Height(28)))
            {
                Refresh(field);
            }
            GUI.backgroundColor = Color.white;
        }

        private static void Refresh(FieldEdgeGenerator field)
        {
            if (field == null || Application.isPlaying || EditorUtility.IsPersistent(field)) return;
            Chunk chunk = field.GetComponent<Chunk>();
            if (chunk == null) chunk = field.GetComponentInParent<Chunk>();
            if (chunk == null) return;

            chunk.GenerateEditorPreview(chunk.GetEditorPreviewSeed());
            SceneView.RepaintAll();
        }
    }
}
#endif
