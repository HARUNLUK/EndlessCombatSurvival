#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace EndlessSurvival.World.Editor
{
    public static class SetupSceneChunkManager
    {
        [InitializeOnLoadMethod]
        private static void AutoUpdateChunkManager()
        {
            EditorApplication.delayCall += () =>
            {
                if (Application.isPlaying) return;
                var mgr = Object.FindAnyObjectByType<ChunkManager>();
                if (mgr != null && (mgr.maxActiveChunks > 3 || mgr.maxChunksBehind != 1))
                {
                    SetupInActiveScene();
                }
            };
        }

        [MenuItem("Endless Survival/Setup ChunkManager in Scene")]
        public static void SetupInActiveScene()
        {
            ChunkManager mgr = Object.FindAnyObjectByType<ChunkManager>();
            if (mgr == null)
            {
                GameObject go = new GameObject("ChunkManager");
                go.transform.position = Vector3.zero;
                go.transform.rotation = Quaternion.identity;
                mgr = go.AddComponent<ChunkManager>();
                Undo.RegisterCreatedObjectUndo(go, "Create ChunkManager");
            }

            // Load the single master chunk prototype: Chunk_Forest_Curve
            string chunkPath = "Assets/Prefabs/Chunks/Chunk_Forest_Curve.prefab";
            GameObject pgo = AssetDatabase.LoadAssetAtPath<GameObject>(chunkPath);
            List<Chunk> prefabs = new List<Chunk>();

            if (pgo != null)
            {
                Chunk c = pgo.GetComponent<Chunk>();
                if (c != null)
                {
                    prefabs.Add(c);
                }
            }

            mgr.chunkPrefabs = prefabs;
            mgr.targetBiome = ChunkBiomeType.Forest;
            mgr.selectionMode = ChunkSelectionMode.SingleBiome;
            mgr.initialChunkCount = 2;
            mgr.maxChunksBehind = 1;
            mgr.maxActiveChunks = 3;
            EditorUtility.SetDirty(mgr);

            // If the old 100x100 Plane exists in the scene, remove or disable it so it doesn't overlap the new 500x500 chunks
            GameObject plane = GameObject.Find("Plane");
            if (plane != null && plane.transform.parent == null)
            {
                plane.SetActive(false);
                EditorUtility.SetDirty(plane);
            }

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            EditorSceneManager.SaveOpenScenes();

            Debug.Log($"[SetupSceneChunkManager] Configured ChunkManager with {prefabs.Count} chunk prefabs in active scene and saved scene.");
        }
    }
}
#endif
