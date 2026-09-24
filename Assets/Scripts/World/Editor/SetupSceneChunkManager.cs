#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace EndlessSurvival.World.Editor
{
    public static class SetupSceneChunkManager
    {
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

            // Load all chunk prefabs found in Assets/Prefabs/Chunks
            string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Prefabs/Chunks" });
            List<Chunk> prefabs = new List<Chunk>();

            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                GameObject pgo = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (pgo != null)
                {
                    Chunk c = pgo.GetComponent<Chunk>();
                    if (c != null && !prefabs.Contains(c))
                    {
                        prefabs.Add(c);
                    }
                }
            }

            mgr.chunkPrefabs = prefabs;
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
