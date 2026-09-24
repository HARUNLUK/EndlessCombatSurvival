#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace EndlessSurvival.World.Editor
{
    [CustomEditor(typeof(ChunkManager))]
    public class ChunkManagerEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            EditorGUILayout.Space(12);
            EditorGUILayout.LabelField("Chunk & World Actions", EditorStyles.boldLabel);

            GUI.backgroundColor = new Color(0.2f, 0.7f, 1.0f);
            if (GUILayout.Button("Generate Base Chunk Prefab", GUILayout.Height(32)))
            {
                ChunkPrefabGenerator.GenerateAndSaveChunkPrefabs();
            }

            GUI.backgroundColor = new Color(0.3f, 0.85f, 0.4f);
            if (GUILayout.Button("Setup ChunkManager in Scene", GUILayout.Height(28)))
            {
                SetupSceneChunkManager.SetupInActiveScene();
            }

            GUI.backgroundColor = new Color(1.0f, 0.5f, 0.3f);
            if (GUILayout.Button("Clear All Child Chunks", GUILayout.Height(24)))
            {
                var mgr = (ChunkManager)target;
                if (mgr != null)
                {
                    mgr.ContextClearChildChunks();
                }
            }

            GUI.backgroundColor = Color.white;
        }
    }
}
#endif
