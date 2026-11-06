#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace EndlessSurvival.World.Editor
{
    /// <summary>
    /// One-time move from "one prefab per chunk type" to "one shared chunk prefab + a ChunkTypeProfile per type":
    /// reads each type prefab still referenced by the scene's ChunkManager, writes its type settings into a profile
    /// asset, creates the shared Chunk_Default prefab (a copy of the forest type with its own TerrainData) and points
    /// the ChunkManager at both. The old prefabs are not changed; delete them once the new setup is verified.
    /// </summary>
    public static class ChunkProfileMigration
    {
        public const string ProfileFolder = "Assets/Data/ChunkTypes";
        public const string ChunkFolder = "Assets/Prefabs/Chunks";
        public const string DefaultPrefabPath = ChunkFolder + "/Chunk_Default.prefab";
        private const string TerrainFolder = ChunkFolder + "/TerrainData";
        private const string ScenePath = "Assets/Scenes/Game.unity";

        [MenuItem("Endless Survival/Chunk Profillerine Geçir", false, 3)]
        [MenuItem("Tools/Endless Survival/Chunk Profillerine Geçir", false, 3)]
        public static void MigrateFromMenu()
        {
            ChunkManager mgr = Object.FindAnyObjectByType<ChunkManager>();
            if (mgr == null)
            {
                EditorUtility.DisplayDialog("Chunk Profilleri", "Sahnede ChunkManager yok. Game sahnesini açıp tekrar dene.", "Tamam");
                return;
            }
            string report = Migrate(mgr);
            EditorUtility.DisplayDialog("Chunk Profilleri", report, "Tamam");
        }

        /// <summary>Batch: Unity -batchmode -executeMethod EndlessSurvival.World.Editor.ChunkProfileMigration.MigrateBatch -quit</summary>
        public static void MigrateBatch()
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            ChunkManager mgr = Object.FindAnyObjectByType<ChunkManager>();
            if (mgr == null) { Debug.LogError("[ChunkProfileMigration] Sahnede ChunkManager yok."); EditorApplication.Exit(1); return; }
            Debug.Log(Migrate(mgr));
        }

        public static string Migrate(ChunkManager mgr)
        {
            var log = new System.Text.StringBuilder();
            EnsureFolder(ProfileFolder);

            // 1) A profile per legacy type prefab
            var byPrefab = new Dictionary<Chunk, ChunkTypeProfile>();
            Chunk forest = null, field = null;
            foreach (ChunkManager.ChunkTypeEntry entry in mgr.chunkTypes)
            {
                if (entry == null) continue;
                Chunk prefab = entry.legacyPrefab;
                if (prefab == null) continue;

                if (!byPrefab.TryGetValue(prefab, out ChunkTypeProfile profile))
                {
                    profile = CreateProfile(prefab);
                    byPrefab[prefab] = profile;
                    log.AppendLine($"Profil: {AssetDatabase.GetAssetPath(profile)}  <-  {prefab.name}");
                }
                if (entry.profile == null) entry.profile = profile;
                entry.legacyPrefab = null;

                if (prefab.biomeType == ChunkBiomeType.Forest && forest == null) forest = prefab;
                if (prefab.biomeType == ChunkBiomeType.Field && field == null) field = prefab;
            }

            if (mgr.legacyStartChunkType != null)
            {
                if (mgr.startType == null && byPrefab.TryGetValue(mgr.legacyStartChunkType, out ChunkTypeProfile start)) mgr.startType = start;
                mgr.legacyStartChunkType = null;
            }

            // 2) The shared chunk prefab
            if (mgr.chunkPrefab == null)
            {
                Chunk source = forest;
                if (source == null) foreach (Chunk p in byPrefab.Keys) { source = p; break; }
                Chunk shared = source != null ? CreateDefaultPrefab(source, field) : AssetDatabase.LoadAssetAtPath<GameObject>(DefaultPrefabPath)?.GetComponent<Chunk>();
                if (shared != null)
                {
                    mgr.chunkPrefab = shared;
                    log.AppendLine($"Ortak chunk prefab'ı: {AssetDatabase.GetAssetPath(shared)}");
                }
                else
                {
                    log.AppendLine("Ortak chunk prefab'ı oluşturulamadı (kaynak tip prefab'ı yok). Chunk Prefab alanını elle ata.");
                }
            }

            AssetDatabase.SaveAssets();
            EditorUtility.SetDirty(mgr);
            EditorSceneManager.MarkSceneDirty(mgr.gameObject.scene);
            EditorSceneManager.SaveScene(mgr.gameObject.scene);

            if (byPrefab.Count == 0) log.AppendLine("Taşınacak eski tip prefab'ı yok (zaten profillere geçilmiş).");
            log.AppendLine($"ChunkManager: {mgr.chunkTypes.Count} tip, başlangıç tipi {(mgr.startType != null ? mgr.startType.name : "-")}. Sahne kaydedildi.");
            return log.ToString();
        }

        /// <summary>A profile asset with every type-specific setting of a (legacy) type prefab.</summary>
        public static ChunkTypeProfile CreateProfile(Chunk prefab)
        {
            string baseName = prefab.name.StartsWith("Chunk_") ? prefab.name.Substring("Chunk_".Length) : prefab.name;
            string path = $"{ProfileFolder}/ChunkType_{baseName}.asset";
            var profile = AssetDatabase.LoadAssetAtPath<ChunkTypeProfile>(path);
            bool isNew = profile == null;
            if (isNew) profile = ScriptableObject.CreateInstance<ChunkTypeProfile>();

            profile.displayName = prefab.typeName;
            profile.biome = prefab.biomeType;
            profile.flatSeamEdges = prefab.flatSeamEdges;
            // Mountains: the road climbs/moves sideways between chunks of a run, a special chunk would cut it short
            profile.allowSpecialChunks = prefab.biomeType != ChunkBiomeType.Mountain;
            profile.elevationConfig = Clone(prefab.elevationConfig);
            profile.curveSettings = Clone(prefab.curveSettings);
            profile.roadStyle = Clone(prefab.roadStyle);
            profile.mountainSettings = Clone(prefab.mountainSettings);

            var boundary = prefab.GetComponent<ChunkBoundaryGenerator>();
            if (boundary != null && boundary.settings != null) profile.boundary = Clone(boundary.settings);
            profile.boundary.biome = prefab.biomeType;

            var backdrop = prefab.GetComponent<ChunkBackdropGenerator>();
            if (backdrop != null)
            {
                if (backdrop.settings != null) profile.backdrop = Clone(backdrop.settings);
                if (backdrop.alpineMountainSettings != null) profile.alpineBackdrop = Clone(backdrop.alpineMountainSettings);
                if (backdrop.foothillLowlandSettings != null) profile.foothillLowlandBackdrop = Clone(backdrop.foothillLowlandSettings);
            }
            profile.backdrop.biome = prefab.biomeType;

            if (isNew) AssetDatabase.CreateAsset(profile, path);
            else EditorUtility.SetDirty(profile);
            return profile;
        }

        /// <summary>
        /// Chunk_Default: a copy of <paramref name="source"/> with its own TerrainData, no type name, and the field edge
        /// generator of <paramref name="field"/> (it only runs on field-type chunks and field foothills).
        /// </summary>
        public static Chunk CreateDefaultPrefab(Chunk source, Chunk field)
        {
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(DefaultPrefabPath);
            if (existing != null) return existing.GetComponent<Chunk>();

            string sourcePath = AssetDatabase.GetAssetPath(source.gameObject);
            if (!AssetDatabase.CopyAsset(sourcePath, DefaultPrefabPath))
            {
                Debug.LogError($"[ChunkProfileMigration] Kopyalanamadı: {sourcePath} -> {DefaultPrefabPath}");
                return null;
            }

            GameObject root = PrefabUtility.LoadPrefabContents(DefaultPrefabPath);
            try
            {
                root.name = "Chunk_Default";
                Chunk chunk = root.GetComponent<Chunk>();
                chunk.typeName = "";

                // Own TerrainData (the copy still points at the forest type's asset)
                Terrain terrain = root.GetComponentInChildren<Terrain>(true);
                if (terrain != null && terrain.terrainData != null)
                {
                    string tdSource = AssetDatabase.GetAssetPath(terrain.terrainData);
                    string tdPath = $"{TerrainFolder}/TerrainData_Chunk_Default.asset";
                    if (AssetDatabase.LoadAssetAtPath<TerrainData>(tdPath) == null) AssetDatabase.CopyAsset(tdSource, tdPath);
                    TerrainData td = AssetDatabase.LoadAssetAtPath<TerrainData>(tdPath);
                    if (td != null)
                    {
                        terrain.terrainData = td;
                        var collider = terrain.GetComponent<TerrainCollider>();
                        if (collider != null) collider.terrainData = td;
                    }
                }

                if (root.GetComponent<FieldEdgeGenerator>() == null)
                {
                    var edges = root.AddComponent<FieldEdgeGenerator>();
                    var fieldEdges = field != null ? field.GetComponent<FieldEdgeGenerator>() : null;
                    if (fieldEdges != null) EditorUtility.CopySerialized(fieldEdges, edges);
                }

                PrefabUtility.SaveAsPrefabAsset(root, DefaultPrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }

            return AssetDatabase.LoadAssetAtPath<GameObject>(DefaultPrefabPath).GetComponent<Chunk>();
        }

        private static T Clone<T>(T source) where T : class, new()
        {
            return source == null ? new T() : JsonUtility.FromJson<T>(JsonUtility.ToJson(source));
        }

        public static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(path));
        }
    }
}
#endif
