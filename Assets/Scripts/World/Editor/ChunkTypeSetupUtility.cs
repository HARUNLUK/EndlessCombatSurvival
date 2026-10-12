#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using EndlessSurvival.World.Road;

namespace EndlessSurvival.World.Editor
{
    /// <summary>
    /// Migrates the old single-prefab setup (Chunk_Forest_Curve + biome enum + biome settings on ChunkManager) to one
    /// prefab per chunk type: Chunk_Forest, Chunk_Coast, Chunk_Field, Chunk_Mountain. Each new prefab is a copy of the
    /// old one that keeps only its own type's settings; ChunkManager gets the weighted type list and world seam settings.
    /// The old prefab is left untouched.
    /// </summary>
    public static class ChunkTypeSetupUtility
    {
        public const string LegacyPrefabPath = "Assets/Prefabs/Chunks/Chunk_Forest_Curve.prefab";
        private const string Folder = "Assets/Prefabs/Chunks";

        private struct TypeDef
        {
            public string prefabName, displayName;
            public ChunkBiomeType biome;
            public TypeDef(string prefabName, string displayName, ChunkBiomeType biome)
            {
                this.prefabName = prefabName;
                this.displayName = displayName;
                this.biome = biome;
            }
        }

        private static readonly TypeDef[] Types =
        {
            new TypeDef("Chunk_Forest", "Orman", ChunkBiomeType.Forest),
            new TypeDef("Chunk_Coast", "Kıyı", ChunkBiomeType.Coast),
            new TypeDef("Chunk_Field", "Düzlük", ChunkBiomeType.Field),
            new TypeDef("Chunk_Mountain", "Dağ", ChunkBiomeType.Mountain)
        };

        [MenuItem("Endless Survival/Chunk Tiplerini Oluştur (Orman, Kıyı, Düzlük, Dağ)", false, 3)]
        [MenuItem("Tools/Endless Survival/Chunk Tiplerini Oluştur (Orman, Kıyı, Düzlük, Dağ)", false, 3)]
        public static void CreateChunkTypes()
        {
            GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(LegacyPrefabPath);
            if (source == null || source.GetComponent<Chunk>() == null)
            {
                EditorUtility.DisplayDialog("Chunk Tipleri", $"Kaynak prefab bulunamadı:\n{LegacyPrefabPath}", "Tamam");
                return;
            }

            ChunkManager mgr = Object.FindAnyObjectByType<ChunkManager>();

            bool anyExists = false;
            foreach (TypeDef t in Types)
                if (AssetDatabase.LoadAssetAtPath<GameObject>(PathOf(t)) != null) anyExists = true;

            bool overwrite = false;
            if (anyExists)
            {
                int choice = EditorUtility.DisplayDialogComplex("Chunk Tipleri",
                    "Bazı chunk tipi prefab'ları zaten var. Üzerine yazılırsa o prefab'larda yaptığın değişiklikler kaybolur.",
                    "Var olanları koru", "İptal", "Üzerine yaz");
                if (choice == 1) return;
                overwrite = choice == 2;
            }

            var created = new Dictionary<ChunkBiomeType, Chunk>();
            foreach (TypeDef t in Types)
            {
                string path = PathOf(t);
                bool exists = AssetDatabase.LoadAssetAtPath<GameObject>(path) != null;
                if (exists && !overwrite)
                {
                    created[t.biome] = AssetDatabase.LoadAssetAtPath<GameObject>(path).GetComponent<Chunk>();
                    continue;
                }

                if (exists) AssetDatabase.DeleteAsset(path);
                if (!AssetDatabase.CopyAsset(LegacyPrefabPath, path))
                {
                    Debug.LogError($"[ChunkTypeSetup] Kopyalanamadı: {LegacyPrefabPath} -> {path}");
                    continue;
                }

                GameObject root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    ConfigureType(root, t, mgr);
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }

                created[t.biome] = AssetDatabase.LoadAssetAtPath<GameObject>(path).GetComponent<Chunk>();
                Debug.Log($"[ChunkTypeSetup] Oluşturuldu: {path}");
            }

            AssetDatabase.SaveAssets();

            // Copies of one prefab share its TerrainData: give each type its own
            SeparateAndCleanTerrainData();

            if (mgr != null)
            {
                ConfigureManager(mgr, source, created);
                EditorSceneManager.MarkSceneDirty(mgr.gameObject.scene);
                EditorSceneManager.SaveScene(mgr.gameObject.scene);
                Selection.activeObject = mgr;
                Debug.Log($"[ChunkTypeSetup] ChunkManager güncellendi: {mgr.chunkTypes.Count} chunk tipi, başlangıç tipi {mgr.startChunkType?.name}. Sahne kaydedildi.");
            }
            else
            {
                Debug.LogWarning("[ChunkTypeSetup] Sahnede ChunkManager yok. Prefab'lar oluşturuldu; ChunkManager'ın Chunk Types listesine elle ekle " +
                                 "(veya ChunkManager olan sahneyi açıp bu menüyü tekrar çalıştır).");
            }

            EditorUtility.DisplayDialog("Chunk Tipleri",
                "Chunk tipi prefab'ları hazır:\n" + string.Join("\n", System.Array.ConvertAll(Types, PathOf)) +
                (mgr != null ? "\n\nChunkManager'ın Chunk Types listesi dolduruldu." : "\n\nSahnede ChunkManager bulunamadı, listeyi elle doldur.") +
                $"\n\nEski {System.IO.Path.GetFileName(LegacyPrefabPath)} artık kullanılmıyor (silinmedi).", "Tamam");
        }

        private static string PathOf(TypeDef t) => $"{Folder}/{t.prefabName}.prefab";

        private const string FoothillsPath = Folder + "/Chunk_Foothills.prefab";
        private const string MountainPath = Folder + "/Chunk_Mountain.prefab";

        /// <summary>
        /// Dağ Eteği: a copy of Chunk_Mountain in foothill mode. The road climbs onto the mountainside (40-80m), the
        /// mountain rises on one side and the other side slopes down to a lowland that is, per run, a long forest,
        /// a long field, a strip of land then the sea, or directly the sea. Added to the ChunkManager's type list.
        /// </summary>
        [MenuItem("Endless Survival/Dağ Eteği Tipini Oluştur", false, 5)]
        [MenuItem("Tools/Endless Survival/Dağ Eteği Tipini Oluştur", false, 5)]
        public static void CreateFoothillsType()
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(MountainPath) == null)
            {
                EditorUtility.DisplayDialog("Dağ Eteği", $"Kaynak prefab bulunamadı:\n{MountainPath}", "Tamam");
                return;
            }
            if (AssetDatabase.LoadAssetAtPath<GameObject>(FoothillsPath) != null
                && !EditorUtility.DisplayDialog("Dağ Eteği", "Chunk_Foothills zaten var. Üzerine yazılsın mı? (Üzerinde yaptığın değişiklikler kaybolur)", "Üzerine yaz", "İptal"))
            {
                return;
            }

            AssetDatabase.DeleteAsset(FoothillsPath);
            if (!AssetDatabase.CopyAsset(MountainPath, FoothillsPath))
            {
                Debug.LogError($"[ChunkTypeSetup] Kopyalanamadı: {MountainPath} -> {FoothillsPath}");
                return;
            }

            GameObject root = PrefabUtility.LoadPrefabContents(FoothillsPath);
            try
            {
                root.name = "Chunk_Foothills";
                Chunk chunk = root.GetComponent<Chunk>();
                chunk.typeName = "Dağ Eteği";
                chunk.biomeType = ChunkBiomeType.Mountain;
                chunk.mountainLayout = MountainLayout.Foothill;
                chunk.foothillLowland = FoothillLowland.Forest;

                MountainTypeSettings ms = chunk.mountainSettings;
                ms.foothills = true;
                ms.snowyChance = 0f;           // green (alpine) mountains above a green lowland
                ms.roadMinHeight = 40f;        // up on the mountainside, with a view down to the lowland
                ms.roadMaxHeight = 80f;
                ms.roadClimbPerChunk = 30f;
                ms.ledgeChance = 0.6f;         // the road often hugs the mountain, leaving a wide lowland
                ms.ledgeRoadMinOffset = 40f;
                ms.ledgeRoadMaxOffset = 80f;
                chunk.roadStyle.alwaysCurvy = true;

                var backdrop = root.GetComponent<ChunkBackdropGenerator>();
                if (backdrop != null) backdrop.foothillLowlandSettings = BiomeBackdropSettings.Lowland();

                PrefabUtility.SaveAsPrefabAsset(root, FoothillsPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }

            // Own TerrainData (a copy would share Chunk_Mountain's) and no preview leftovers
            SeparateAndCleanTerrainData();

            Chunk prefab = AssetDatabase.LoadAssetAtPath<GameObject>(FoothillsPath).GetComponent<Chunk>();
            ChunkManager mgr = Object.FindAnyObjectByType<ChunkManager>();
            if (mgr != null)
            {
                Undo.RecordObject(mgr, "Add Foothills Type");
                if (mgr.chunkTypes == null) mgr.chunkTypes = new List<ChunkManager.ChunkTypeEntry>();
                mgr.chunkTypes.RemoveAll(e => e == null || e.prefab == null);
                if (!mgr.chunkTypes.Exists(e => e.prefab == prefab))
                {
                    mgr.chunkTypes.Add(new ChunkManager.ChunkTypeEntry { prefab = prefab, weight = 30, minLength = 3, maxLength = 6 });
                }
                EditorUtility.SetDirty(mgr);
                EditorSceneManager.MarkSceneDirty(mgr.gameObject.scene);
                EditorSceneManager.SaveScene(mgr.gameObject.scene);
            }

            Selection.activeObject = prefab;
            EditorUtility.DisplayDialog("Dağ Eteği", "Chunk_Foothills oluşturuldu" +
                (mgr != null ? " ve ChunkManager listesine eklendi (ağırlık 30, 3-6 chunk)." : ". Sahnede ChunkManager yok, listeye elle ekle."), "Tamam");
        }

        private const string TerrainDataFolder = Folder + "/TerrainData";

        /// <summary>
        /// Gives every chunk type prefab its own TerrainData asset (copies made from one prefab all shared one, so an
        /// editor preview of one type reshaped the ground of every other type: floating trees/POIs), resets it to
        /// flat ground at baseElevation without trees, and removes editor-preview leftovers saved into the prefab.
        /// </summary>
        [MenuItem("Endless Survival/Chunk Prefab Arazilerini Ayır ve Temizle", false, 4)]
        [MenuItem("Tools/Endless Survival/Chunk Prefab Arazilerini Ayır ve Temizle", false, 4)]
        public static void SeparateAndCleanTerrainData()
        {
            List<Chunk> prefabs = SetupSceneChunkManager.FindChunkTypePrefabs();
            if (!AssetDatabase.IsValidFolder(TerrainDataFolder)) AssetDatabase.CreateFolder(Folder, "TerrainData");

            int changed = 0;
            foreach (Chunk prefab in prefabs)
            {
                string prefabPath = AssetDatabase.GetAssetPath(prefab);
                string dataPath = $"{TerrainDataFolder}/TerrainData_{prefab.name}.asset";

                GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
                try
                {
                    Chunk chunk = root.GetComponent<Chunk>();
                    Terrain terrain = root.GetComponentInChildren<Terrain>(true);
                    if (terrain != null && terrain.terrainData != null)
                    {
                        TerrainData td = terrain.terrainData;
                        if (AssetDatabase.GetAssetPath(td) != dataPath)
                        {
                            TerrainData own = AssetDatabase.LoadAssetAtPath<TerrainData>(dataPath);
                            if (own == null)
                            {
                                own = Object.Instantiate(td);
                                own.name = $"TerrainData_{prefab.name}";
                                AssetDatabase.CreateAsset(own, dataPath);
                            }
                            td = own;
                            terrain.terrainData = td;
                            var collider = terrain.GetComponent<TerrainCollider>();
                            if (collider != null) collider.terrainData = td;
                        }

                        ResetTerrainData(td, chunk);
                        EditorUtility.SetDirty(td);
                    }

                    RemovePreviewLeftovers(root);
                    PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                    changed++;
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"[ChunkTypeSetup] {changed} chunk prefab'ının arazisi ayrıldı ve temizlendi ({TerrainDataFolder}).");
        }

        /// <summary>Flat ground at the chunk's base elevation, no trees; mountain types get their taller height range.</summary>
        private static void ResetTerrainData(TerrainData td, Chunk chunk)
        {
            float baseY = chunk != null ? chunk.baseElevation : 20f;
            float heightRange = chunk != null && chunk.IsMountain ? RoadTerrainAdapter.MountainTerrainHeight : 100f;
            td.size = new Vector3(td.size.x, heightRange, td.size.z);

            int res = td.heightmapResolution;
            var heights = new float[res, res];
            float h = Mathf.Clamp01(baseY / heightRange);
            for (int z = 0; z < res; z++)
                for (int x = 0; x < res; x++)
                    heights[z, x] = h;
            td.SetHeights(0, 0, heights);
            td.SetTreeInstances(new TreeInstance[0], false);
        }

        /// <summary>Generated content that edit-mode previews may have saved into the prefab.</summary>
        private static void RemovePreviewLeftovers(GameObject root)
        {
            string[] names =
            {
                "EditorPreview", ChunkVegetationSpawner.CONTAINER_NAME, AbandonedVehicleSpawner.CONTAINER_NAME,
                ChunkBoundaryGenerator.ROCKS_CONTAINER_NAME, ChunkBackdropGenerator.CONTAINER_NAME, FieldEdgeGenerator.CONTAINER_NAME
            };
            var doomed = new List<GameObject>();
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t == root.transform) continue;
                if (System.Array.IndexOf(names, t.name) >= 0) doomed.Add(t.gameObject);
            }
            foreach (GameObject go in doomed)
                if (go != null) Object.DestroyImmediate(go);
        }

        /// <summary>Turns a copy of the old base prefab into one type: identity, own settings only, own generators only.</summary>
        private static void ConfigureType(GameObject root, TypeDef t, ChunkManager mgr)
        {
            root.name = t.prefabName;
            Chunk chunk = root.GetComponent<Chunk>();
            ChunkBiomeType biome = t.biome;

            chunk.typeName = t.displayName;
            chunk.biomeType = biome;
            chunk.generationMode = ChunkGenerationMode.Procedural;
            chunk.flatSeamEdges = biome == ChunkBiomeType.Field;

            // Runtime values that may have been saved into the old prefab by previews
            chunk.roadEntryX = chunk.roadExitX = 0f;
            chunk.roadEntryY = chunk.roadExitY = chunk.baseElevation;
            chunk.coastContinuesAtStart = chunk.coastContinuesAtEnd = true;
            chunk.mountainLayout = MountainLayout.Valley;

            // Road settings: previously ChunkManager's globals (or the prefab's own elevation config if it overrode them)
            ChunkElevationConfig elevation = chunk.legacyOverrideElevationSettings || mgr == null
                ? chunk.elevationConfig
                : mgr.legacyElevationConfig;
            chunk.elevationConfig = Clone(elevation ?? new ChunkElevationConfig());
            chunk.curveSettings = Clone(mgr != null && mgr.legacyCurveSettings != null ? mgr.legacyCurveSettings : new RoadCurveSettings());
            chunk.roadStyle = new ChunkRoadStyle();
            chunk.mountainSettings = new MountainTypeSettings();
            chunk.legacyOverrideElevationSettings = false;

            if (mgr != null)
            {
                MountainTypeSettings ms = chunk.mountainSettings;
                ms.snowyChance = mgr.legacySnowyMountainChance;
                ms.ledgeChance = mgr.legacyMountainLedgeChance;
                ms.ledgeRoadMinOffset = mgr.legacyLedgeRoadMinOffset;
                ms.ledgeRoadMaxOffset = mgr.legacyLedgeRoadMaxOffset;
                ms.roadMinHeight = mgr.legacyMountainRoadMinHeight;
                ms.roadMaxHeight = mgr.legacyMountainRoadMaxHeight;
                ms.roadClimbPerChunk = mgr.legacyMountainRoadClimbPerChunk;
                ms.hillsideChance = mgr.legacyMountainHillsideChance;
            }

            switch (biome)
            {
                case ChunkBiomeType.Field:
                    // Open plains: rarely gentle hills/dips/rolling (no passes or chicanes), fewer curves, country road
                    ChunkElevationConfig f = chunk.elevationConfig;
                    f.elevationChance = mgr != null ? mgr.legacyFieldElevationChance : 0.2f;
                    f.minHillHeight = mgr != null ? mgr.legacyFieldMinHillHeight : 4f;
                    f.maxHillHeight = mgr != null ? mgr.legacyFieldMaxHillHeight : 9f;
                    f.minDipDepth = mgr != null ? mgr.legacyFieldMinDipDepth : 3f;
                    f.maxDipDepth = mgr != null ? mgr.legacyFieldMaxDipDepth : 7f;
                    f.mountainPassWeight = 0;
                    f.elevatedChicaneWeight = 0;
                    f.flatWeight = 0;
                    chunk.roadStyle.curveChanceMultiplier = 0.6f;
                    chunk.roadStyle.sharpCurveChance = 0f;
                    chunk.roadStyle.forceCrossSection = true;
                    chunk.roadStyle.forcedCrossSection = RoadCrossSectionPreset.OpenRoad;
                    break;
                case ChunkBiomeType.Mountain:
                    chunk.roadStyle.alwaysCurvy = true;
                    break;
            }

            // Boundary cliffs/rocks: only this type's entry of the old per-biome list
            var boundary = root.GetComponent<ChunkBoundaryGenerator>();
            if (boundary != null)
            {
                boundary.settings = Clone(boundary.LegacySettings(biome));
                boundary.settings.biome = biome;
                boundary.legacyBiomeSettings.Clear();
                boundary.settingsMigrated = true;
            }

            // Backdrop mountains: this type's entry (mountain: snowy; the alpine sub-type keeps its own field)
            var backdrop = root.GetComponent<ChunkBackdropGenerator>();
            if (backdrop != null)
            {
                backdrop.settings = Clone(backdrop.LegacySettings(biome));
                backdrop.settings.biome = biome;
                backdrop.legacyBiomeSettings.Clear();
                backdrop.settingsMigrated = true;
            }

            // Road profile: this type's profile instead of a per-biome pool
            var roadGen = root.GetComponentInChildren<RoadGenerator>();
            if (roadGen != null && roadGen.biomeProfiles != null && roadGen.biomeProfiles.Count > 0)
            {
                RoadProfile own = roadGen.biomeProfiles.Find(p => p != null && p.targetBiome == biome);
                if (own != null) roadGen.activeProfile = own;
                roadGen.biomeProfiles.Clear();
            }

            // Field edges belong to the field type only
            var fieldEdges = root.GetComponent<FieldEdgeGenerator>();
            if (biome == ChunkBiomeType.Field && fieldEdges == null) root.AddComponent<FieldEdgeGenerator>();
            if (biome != ChunkBiomeType.Field && fieldEdges != null) Object.DestroyImmediate(fieldEdges);

            Transform preview = root.transform.Find("EditorPreview");
            if (preview != null) Object.DestroyImmediate(preview.gameObject);
        }

        /// <summary>Type list (same weights/lengths as the old biome runs), start type and world seam settings.</summary>
        private static void ConfigureManager(ChunkManager mgr, GameObject source, Dictionary<ChunkBiomeType, Chunk> created)
        {
            Undo.RecordObject(mgr, "Setup Chunk Types");

            var runs = mgr.legacyBiomeRuns != null && mgr.legacyBiomeRuns.Count > 0
                ? mgr.legacyBiomeRuns
                : new List<ChunkManager.BiomeRunSettings>
                {
                    new ChunkManager.BiomeRunSettings { biome = ChunkBiomeType.Forest, weight = 45, minLength = 2, maxLength = 5 },
                    new ChunkManager.BiomeRunSettings { biome = ChunkBiomeType.Coast, weight = 30, minLength = 2, maxLength = 4 },
                    new ChunkManager.BiomeRunSettings { biome = ChunkBiomeType.Field, weight = 35, minLength = 2, maxLength = 4 },
                    new ChunkManager.BiomeRunSettings { biome = ChunkBiomeType.Mountain, weight = 30, minLength = 4, maxLength = 7 }
                };

            // Same order as the old runs, so a seed keeps picking the same sequence
            var list = new List<ChunkManager.ChunkTypeEntry>();
            foreach (var run in runs)
            {
                if (run == null || !created.TryGetValue(run.biome, out Chunk prefab) || prefab == null) continue;
                if (list.Exists(e => e.prefab == prefab)) continue;
                list.Add(new ChunkManager.ChunkTypeEntry { prefab = prefab, weight = run.weight, minLength = run.minLength, maxLength = run.maxLength });
            }
            foreach (var pair in created)
            {
                if (pair.Value == null || list.Exists(e => e.prefab == pair.Value)) continue;
                list.Add(new ChunkManager.ChunkTypeEntry { prefab = pair.Value, weight = 30 });
            }

            // Custom types the user already added stay in the list
            if (mgr.chunkTypes != null)
            {
                foreach (var e in mgr.chunkTypes)
                {
                    if (e == null || e.prefab == null || list.Exists(x => x.prefab == e.prefab)) continue;
                    if (AssetDatabase.GetAssetPath(e.prefab) == LegacyPrefabPath) continue;
                    list.Add(e);
                }
            }

            mgr.chunkTypes = list;
            if (created.TryGetValue(ChunkBiomeType.Forest, out Chunk forest)) mgr.startChunkType = forest;

            // Seam values used to live on the (single) prefab's generators
            var srcBoundary = source.GetComponent<ChunkBoundaryGenerator>();
            if (srcBoundary != null)
            {
                mgr.seamSettings.seamInnerEdge = srcBoundary.legacySeamInnerEdge;
                mgr.seamSettings.seamRampWidth = srcBoundary.legacySeamRampWidth;
                mgr.seamSettings.seamCliffChance = srcBoundary.legacySeamCliffChance;
                mgr.seamSettings.seamMinHeight = srcBoundary.legacySeamMinHeight;
                mgr.seamSettings.seamMaxHeight = srcBoundary.legacySeamMaxHeight;
            }
            var srcBackdrop = source.GetComponent<ChunkBackdropGenerator>();
            if (srcBackdrop != null)
            {
                mgr.seamSettings.backdropStripDepth = srcBackdrop.legacyStripDepth;
                mgr.seamSettings.backdropBlendDistance = srcBackdrop.legacyBlendDistance;
                mgr.seamSettings.backdropRowsAlongRoad = srcBackdrop.legacyRowsAlongRoad;
                mgr.seamSettings.backdropColumnsOutward = srcBackdrop.legacyColumnsOutward;
            }

            EditorUtility.SetDirty(mgr);
        }

        private static T Clone<T>(T value) where T : class
        {
            return value == null ? null : JsonUtility.FromJson<T>(JsonUtility.ToJson(value));
        }
    }
}
#endif
