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

            if (mgr.chunkPrefab == null)
            {
                var shared = AssetDatabase.LoadAssetAtPath<GameObject>(ChunkProfileMigration.DefaultPrefabPath);
                if (shared != null) mgr.chunkPrefab = shared.GetComponent<Chunk>();
            }

            // Every chunk type profile in the profile folder that is not in the list yet is added
            List<ChunkTypeProfile> profiles = FindChunkTypeProfiles();
            if (mgr.chunkTypes == null) mgr.chunkTypes = new List<ChunkManager.ChunkTypeEntry>();
            int added = 0;
            foreach (ChunkTypeProfile profile in profiles)
            {
                if (mgr.chunkTypes.Exists(e => e != null && e.profile == profile)) continue;
                bool mountain = profile.biome == ChunkBiomeType.Mountain;
                mgr.chunkTypes.Add(new ChunkManager.ChunkTypeEntry
                {
                    profile = profile,
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

        /// <summary>All chunk type profiles in the project.</summary>
        public static List<ChunkTypeProfile> FindChunkTypeProfiles()
        {
            var profiles = new List<ChunkTypeProfile>();
            foreach (string guid in AssetDatabase.FindAssets("t:ChunkTypeProfile"))
            {
                var p = AssetDatabase.LoadAssetAtPath<ChunkTypeProfile>(AssetDatabase.GUIDToAssetPath(guid));
                if (p != null && !profiles.Contains(p)) profiles.Add(p);
            }
            return profiles;
        }
    }
}
#endif
