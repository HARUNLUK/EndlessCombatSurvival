#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace EndlessSurvival.World.Editor
{
    public static class SetupSceneChunkManager
    {
        [MenuItem("Endless Survival/Setup ChunkManager in Scene", false, 2)]
        [MenuItem("Tools/Endless Survival/Setup ChunkManager in Scene", false, 2)]
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

            // Every chunk type prefab in Assets/Prefabs/Chunks that is not in the list yet is added
            // (the old single base prefab is skipped once real type prefabs exist)
            List<Chunk> prefabs = FindChunkTypePrefabs();
            if (mgr.chunkTypes == null) mgr.chunkTypes = new List<ChunkManager.ChunkTypeEntry>();
            int added = 0;
            foreach (Chunk prefab in prefabs)
            {
                if (mgr.chunkTypes.Exists(e => e != null && e.prefab == prefab)) continue;
                bool mountain = prefab.biomeType == ChunkBiomeType.Mountain;
                mgr.chunkTypes.Add(new ChunkManager.ChunkTypeEntry
                {
                    prefab = prefab,
                    weight = 30,
                    minLength = mountain ? 4 : 2,
                    maxLength = mountain ? 7 : 4
                });
                added++;
            }

            mgr.initialChunkCount = 2;
            mgr.maxChunksBehind = 1;
            mgr.maxActiveChunks = Mathf.Max(mgr.maxActiveChunks, mgr.maxChunksBehind + 1 + mgr.chunksAhead);
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

            Debug.Log($"[SetupSceneChunkManager] ChunkManager: {added} yeni chunk tipi eklendi (toplam {mgr.chunkTypes.Count}). Sahne kaydedildi.");
        }

        /// <summary>All chunk prefabs in Assets/Prefabs/Chunks, without the legacy base prefab when type prefabs exist.</summary>
        public static List<Chunk> FindChunkTypePrefabs()
        {
            string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Prefabs/Chunks" });
            var prefabs = new List<Chunk>();
            Chunk legacy = null;

            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                GameObject pgo = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                Chunk c = pgo != null ? pgo.GetComponent<Chunk>() : null;
                if (c == null || prefabs.Contains(c)) continue;
                if (path == ChunkTypeSetupUtility.LegacyPrefabPath) { legacy = c; continue; }
                prefabs.Add(c);
            }

            if (prefabs.Count == 0 && legacy != null) prefabs.Add(legacy);
            return prefabs;
        }
    }
}
#endif
