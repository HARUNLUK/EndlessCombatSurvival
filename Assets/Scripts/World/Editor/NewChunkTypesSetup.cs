using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using EndlessSurvival.World.Road;

namespace EndlessSurvival.World.Editor
{
    /// <summary>
    /// Çorak Arazi, Harabe Kasaba and Mola Yeri chunk types (2026-11-15): profile assets derived from Orman (same
    /// terrain engine), set up here and added to the Game scene's ChunkManager type list. Running it again resets the
    /// values written here and keeps the list entries. Also "start with X" test toggles.
    /// </summary>
    public static class NewChunkTypesSetup
    {
        public const string DesertPath = "Assets/Data/ChunkTypes/ChunkType_Desert.asset";
        public const string TownPath = "Assets/Data/ChunkTypes/ChunkType_Town.asset";
        public const string RestStopPath = "Assets/Data/ChunkTypes/ChunkType_RestStop.asset";
        private const string ForestPath = "Assets/Data/ChunkTypes/ChunkType_Forest.asset";
        private const string ScenePath = "Assets/Scenes/Game.unity";
        private const string PrevStartKey = "EndlessSurvival.NewChunkTypes.PrevStartType";

        [MenuItem("Endless Survival/Yeni Chunk Tiplerini Oluştur (Çorak, Kasaba, Mola Yeri)", false, 5)]
        public static void CreateTypes()
        {
            var forest = AssetDatabase.LoadAssetAtPath<ChunkTypeProfile>(ForestPath);
            if (forest == null)
            {
                Debug.LogError($"[NewChunkTypes] {ForestPath} bulunamadı; yeni tipler ondan türetilir.");
                return;
            }

            var desert = Profile(DesertPath, forest);
            SetupDesert(desert, forest);
            var town = Profile(TownPath, forest);
            SetupTown(town, forest);
            var stop = Profile(RestStopPath, forest);
            SetupRestStop(stop, forest);
            AssetDatabase.SaveAssets();

            var mgr = OpenSceneManager();
            if (mgr == null) return;
            Undo.RecordObject(mgr, "Yeni chunk tipleri");
            bool changed = false;
            changed |= AddEntry(mgr, desert, 25, 3, 5);
            changed |= AddEntry(mgr, town, 22, 1, 2);
            changed |= AddEntry(mgr, stop, 30, 1, 1);
            if (changed)
            {
                EditorUtility.SetDirty(mgr);
                EditorSceneManager.MarkSceneDirty(mgr.gameObject.scene);
                EditorSceneManager.SaveScene(mgr.gameObject.scene);
            }
            Debug.Log("[NewChunkTypes] Çorak Arazi (ağırlık 25, 3-5 chunk), Harabe Kasaba (22, 1-2), Mola Yeri (30, 1) hazır ve ChunkManager listesinde.");
            ApplyTransitionRules();
        }

        // ---------------------------------------------------------------- neighbours, insets, run lengths

        private const string Dir = "Assets/Data/ChunkTypes/";

        /// <summary>
        /// Which type may follow which (Çorak Arazi never next to a snowy mountain or the forest...), town and rest stop
        /// as insets inside fitting runs, longer runs. Writes the profiles' Komşular / inset settings and the run
        /// lengths in the Game scene's ChunkManager list. Safe to run again.
        /// </summary>
        [MenuItem("Endless Survival/Chunk Geçiş Kurallarını Uygula (komşular, kasaba/mola yeri, bölüm uzunlukları)", false, 6)]
        public static void ApplyTransitionRules()
        {
            ChunkTypeProfile Load(string n) => AssetDatabase.LoadAssetAtPath<ChunkTypeProfile>(Dir + "ChunkType_" + n + ".asset");
            var forest = Load("Forest"); var coast = Load("Coast"); var river = Load("RiverValley"); var field = Load("Field");
            var foothills = Load("Foothills"); var mountain = Load("Mountain"); var desert = Load("Desert");
            var town = Load("Town"); var stop = Load("RestStop");
            if (forest == null || coast == null || river == null || field == null || foothills == null || mountain == null || desert == null || town == null || stop == null)
            {
                Debug.LogError("[ChunkTransitions] Assets/Data/ChunkTypes içinde tiplerden biri eksik.");
                return;
            }

            void Neighbours(ChunkTypeProfile p, params ChunkTypeProfile[] list)
            {
                p.neighbours = new List<ChunkTypeProfile>(list);
                EditorUtility.SetDirty(p);
            }
            Neighbours(forest, field, foothills, river, coast);
            Neighbours(coast, forest, field, river);
            Neighbours(river, forest, coast, field, foothills);
            Neighbours(field, forest, coast, river, foothills, desert);
            Neighbours(foothills, forest, field, river, mountain, desert);
            Neighbours(mountain, foothills);
            Neighbours(desert, field, foothills);
            Neighbours(town);
            Neighbours(stop);

            town.inset = new InsetSettings { enabled = true, hosts = new List<ChunkTypeProfile> { desert, field }, chancePerRun = 0.45f, minLength = 1, maxLength = 2 };
            stop.inset = new InsetSettings { enabled = true, hosts = new List<ChunkTypeProfile> { forest, field, desert, coast }, chancePerRun = 0.3f, minLength = 1, maxLength = 1 };
            EditorUtility.SetDirty(town);
            EditorUtility.SetDirty(stop);
            AssetDatabase.SaveAssets();

            var mgr = OpenSceneManager();
            if (mgr == null) return;
            Undo.RecordObject(mgr, "Chunk geçiş kuralları");
            void Length(ChunkTypeProfile p, int min, int max)
            {
                foreach (var e in mgr.chunkTypes)
                    if (e != null && e.profile == p) { e.minLength = min; e.maxLength = max; }
            }
            Length(forest, 3, 6);
            Length(coast, 3, 5);
            Length(river, 2, 4);
            Length(field, 2, 5);
            Length(foothills, 3, 4);
            Length(mountain, 4, 7);
            Length(desert, 4, 7);
            Length(town, 1, 2);
            Length(stop, 1, 1);
            mgr.ResetTypePlan();
            EditorUtility.SetDirty(mgr);
            EditorSceneManager.MarkSceneDirty(mgr.gameObject.scene);
            EditorSceneManager.SaveScene(mgr.gameObject.scene);
            Debug.Log("[ChunkTransitions] Komşu kuralları, kasaba/mola yeri yerleşimleri ve bölüm uzunlukları uygulandı.");
        }

        /// <summary>
        /// Side roads per type: town streets, forest / desert / coast tracks, field roads; none on mountains, foothills,
        /// river valleys and rest stops. Safe to run again.
        /// </summary>
        [MenuItem("Endless Survival/Yan Yol Ayarlarını Uygula (sokak, orman/çöl yolu, tarla yolu)", false, 7)]
        public static void ApplySideRoadRules()
        {
            ChunkTypeProfile Load(string n) => AssetDatabase.LoadAssetAtPath<ChunkTypeProfile>(Dir + "ChunkType_" + n + ".asset");
            void Set(string name, SideRoadSettings s)
            {
                var p = Load(name);
                if (p == null) { Debug.LogWarning("[SideRoads] tip yok: " + name); return; }
                p.sideRoads = s;
                EditorUtility.SetDirty(p);
            }
            Set("Town", new SideRoadSettings
            {
                enabled = true, style = SideRoadStyle.Street, chance = 0.9f, minCount = 2, maxCount = 3, halfWidth = 3.5f,
                minLength = 80f, maxLength = 200f, crossChance = 0.35f, endContentChance = 0.2f, pavedChance = 1f, surfaceColor = new Color(0.23f, 0.23f, 0.24f)
            });
            Set("Forest", new SideRoadSettings
            {
                enabled = true, style = SideRoadStyle.Track, chance = 0.55f, minCount = 1, maxCount = 2, halfWidth = 2f,
                minLength = 150f, maxLength = 320f, endContentChance = 0.25f, pavedChance = 0.3f, surfaceColor = new Color(0.33f, 0.27f, 0.20f)
            });
            Set("Field", new SideRoadSettings
            {
                enabled = true, style = SideRoadStyle.FarmRoad, chance = 0.5f, minCount = 1, maxCount = 1, halfWidth = 2.4f,
                minLength = 140f, maxLength = 270f, endContentChance = 0.4f, pavedChance = 0.35f, surfaceColor = new Color(0.45f, 0.40f, 0.32f)
            });
            Set("Desert", new SideRoadSettings
            {
                enabled = true, style = SideRoadStyle.Track, chance = 0.45f, minCount = 1, maxCount = 1, halfWidth = 2.2f,
                minLength = 150f, maxLength = 300f, endContentChance = 0.25f, pavedChance = 0.3f, surfaceColor = new Color(0.60f, 0.50f, 0.37f)
            });
            Set("Coast", new SideRoadSettings
            {
                enabled = true, style = SideRoadStyle.Track, chance = 0.35f, minCount = 1, maxCount = 1, halfWidth = 2f,
                minLength = 120f, maxLength = 240f, endContentChance = 0.25f, pavedChance = 0.35f, surfaceColor = new Color(0.36f, 0.31f, 0.24f)
            });
            foreach (var none in new[] { "Mountain", "Foothills", "RiverValley", "RestStop" })
                Set(none, new SideRoadSettings { enabled = false });
            AssetDatabase.SaveAssets();
            Debug.Log("[SideRoads] Yan yol ayarları tiplere yazıldı.");
        }

        /// <summary>Batch mode entry point (-executeMethod).</summary>
        public static void CreateTypesBatch()
        {
            CreateTypes();
            EditorApplication.Exit(0);
        }

        private static ChunkTypeProfile Profile(string path, ChunkTypeProfile source)
        {
            var profile = AssetDatabase.LoadAssetAtPath<ChunkTypeProfile>(path);
            if (profile != null) return profile;
            profile = Object.Instantiate(source);
            AssetDatabase.CreateAsset(profile, path);
            return profile;
        }

        private static T Copy<T>(T source) where T : class, new()
        {
            return source == null ? new T() : JsonUtility.FromJson<T>(JsonUtility.ToJson(source));
        }

        // ---------------------------------------------------------------- Çorak Arazi

        private static void SetupDesert(ChunkTypeProfile p, ChunkTypeProfile forest)
        {
            p.displayName = "Çorak Arazi";
            p.biome = ChunkBiomeType.Desert;
            p.flatSeamEdges = false;
            p.allowSpecialChunks = false;
            p.riverValley = new RiverValleySettings { enabled = false };
            p.settlement = new SettlementSettings { kind = SettlementKind.None };

            // Long open road: gentle rises, few bends, no barriers
            var e = Copy(forest.elevationConfig);
            e.elevationChance = 0.55f;
            e.minHillHeight = 4f; e.maxHillHeight = 11f;
            e.minDipDepth = 3f; e.maxDipDepth = 8f;
            e.hillWeight = 30; e.valleyWeight = 20; e.rollingHillsWeight = 35;
            e.mountainPassWeight = 0; e.elevatedChicaneWeight = 0; e.flatWeight = 15;
            p.elevationConfig = e;

            var c = Copy(forest.curveSettings);
            c.curveChance = 0.35f; c.minBends = 1; c.maxBends = 2;
            c.minShift = 8f; c.maxShift = 30f; c.maxLateral = 40f;
            p.curveSettings = c;

            var s = Copy(forest.roadStyle);
            s.curveChanceMultiplier = 0.6f; s.sharpCurveChance = 0f; s.alwaysCurvy = false;
            s.forceCrossSection = true; s.forcedCrossSection = RoadCrossSectionPreset.OpenRoad;
            p.roadStyle = s;

            // Edges: rock piles and low red cliffs (mesa walls), further out
            var b = Copy(forest.boundary);
            b.biome = ChunkBiomeType.Desert;
            b.cliffWeight = 30; b.rocksWeight = 40; b.cliffAndRocksWeight = 30;
            b.minCliffHeight = 25f; b.maxCliffHeight = 50f;
            b.minInnerEdge = 360f; b.maxInnerEdge = 440f;
            b.rockColor = new Color(0.56f, 0.40f, 0.29f);
            b.mossColor = new Color(0.62f, 0.52f, 0.36f);
            p.boundary = b;

            // Horizon: flat-topped, rocky, sand coloured; no snow
            var bd = Copy(forest.backdrop);
            bd.biome = ChunkBiomeType.Desert;
            bd.baseHeight = 45f; bd.peakAmplitude = 170f; bd.detailAmplitude = 25f; bd.ridgeFrequency = 0.0022f;
            bd.rockLine = 55f; bd.snowLine = 5000f; bd.rockSlope = 0.8f;
            bd.forestColor = new Color(0.66f, 0.54f, 0.37f);
            bd.rockColor = new Color(0.56f, 0.37f, 0.26f);
            p.backdrop = bd;
            EditorUtility.SetDirty(p);
        }

        // ---------------------------------------------------------------- Harabe Kasaba

        private static void SetupTown(ChunkTypeProfile p, ChunkTypeProfile forest)
        {
            p.displayName = "Harabe Kasaba";
            p.biome = ChunkBiomeType.RuinedCity;
            p.flatSeamEdges = false;
            p.allowSpecialChunks = false;
            p.riverValley = new RiverValleySettings { enabled = false };
            p.settlement = new SettlementSettings { kind = SettlementKind.Town };

            // Mostly level main street with sidewalks
            var e = Copy(forest.elevationConfig);
            e.elevationChance = 0.3f;
            e.minHillHeight = 2f; e.maxHillHeight = 5f;
            e.minDipDepth = 1f; e.maxDipDepth = 3f;
            e.hillWeight = 40; e.valleyWeight = 10; e.rollingHillsWeight = 0;
            e.mountainPassWeight = 0; e.elevatedChicaneWeight = 0; e.flatWeight = 50;
            p.elevationConfig = e;

            var c = Copy(forest.curveSettings);
            c.curveChance = 0.45f; c.minBends = 1; c.maxBends = 2;
            c.minShift = 6f; c.maxShift = 18f; c.maxLateral = 25f;
            p.curveSettings = c;

            var s = Copy(forest.roadStyle);
            s.curveChanceMultiplier = 0.7f; s.sharpCurveChance = 0f; s.alwaysCurvy = false;
            s.forceCrossSection = true; s.forcedCrossSection = RoadCrossSectionPreset.SidewalkOnly;
            p.roadStyle = s;

            var b = Copy(forest.boundary);
            b.biome = ChunkBiomeType.RuinedCity;
            b.minInnerEdge = 340f; b.maxInnerEdge = 430f;
            b.minCliffHeight = 25f; b.maxCliffHeight = 45f;
            b.mossColor = new Color(0.42f, 0.40f, 0.27f);
            p.boundary = b;

            // Dry olive hills around the town
            var bd = Copy(forest.backdrop);
            bd.biome = ChunkBiomeType.RuinedCity;
            bd.baseHeight = 50f; bd.peakAmplitude = 180f;
            bd.forestColor = new Color(0.36f, 0.36f, 0.22f);
            bd.rockColor = new Color(0.42f, 0.39f, 0.34f);
            p.backdrop = bd;
            EditorUtility.SetDirty(p);
        }

        // ---------------------------------------------------------------- Mola Yeri

        private static void SetupRestStop(ChunkTypeProfile p, ChunkTypeProfile forest)
        {
            p.displayName = "Mola Yeri";
            p.biome = ChunkBiomeType.Forest;     // forest around the clearing
            p.flatSeamEdges = false;
            p.allowSpecialChunks = false;
            p.riverValley = new RiverValleySettings { enabled = false };
            p.settlement = new SettlementSettings { kind = SettlementKind.RestStop };

            var e = Copy(forest.elevationConfig);
            e.elevationChance = 0.25f;
            e.minHillHeight = 2f; e.maxHillHeight = 5f;
            e.minDipDepth = 1f; e.maxDipDepth = 3f;
            e.hillWeight = 40; e.valleyWeight = 10; e.rollingHillsWeight = 0;
            e.mountainPassWeight = 0; e.elevatedChicaneWeight = 0; e.flatWeight = 50;
            p.elevationConfig = e;

            var c = Copy(forest.curveSettings);
            c.curveChance = 0.4f; c.minBends = 1; c.maxBends = 2;
            c.minShift = 6f; c.maxShift = 20f; c.maxLateral = 30f;
            p.curveSettings = c;

            var s = Copy(forest.roadStyle);
            s.curveChanceMultiplier = 0.7f; s.sharpCurveChance = 0f; s.alwaysCurvy = false;
            s.forceCrossSection = true; s.forcedCrossSection = RoadCrossSectionPreset.OpenRoad;
            p.roadStyle = s;

            p.boundary = Copy(forest.boundary);
            p.backdrop = Copy(forest.backdrop);
            EditorUtility.SetDirty(p);
        }

        // ---------------------------------------------------------------- scene

        private static ChunkManager OpenSceneManager()
        {
            var scene = EditorSceneManager.GetActiveScene();
            if (scene.path != ScenePath)
            {
                if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return null;
                scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            }
            var mgr = Object.FindAnyObjectByType<ChunkManager>();
            if (mgr == null) Debug.LogError("[NewChunkTypes] Game sahnesinde ChunkManager yok.");
            return mgr;
        }

        private static bool AddEntry(ChunkManager mgr, ChunkTypeProfile profile, int weight, int min, int max)
        {
            foreach (var entry in mgr.chunkTypes)
                if (entry != null && entry.profile == profile) return false;
            mgr.chunkTypes.Add(new ChunkManager.ChunkTypeEntry { profile = profile, weight = weight, minLength = min, maxLength = max });
            return true;
        }

        // ---------------------------------------------------------------- test toggles

        [MenuItem("Endless Survival/Debug/Çorak Arazi ile Başla (aç-kapa)", false, 61)]
        public static void StartInDesert() => ToggleStart(DesertPath);

        [MenuItem("Endless Survival/Debug/Harabe Kasaba ile Başla (aç-kapa)", false, 62)]
        public static void StartInTown() => ToggleStart(TownPath);

        [MenuItem("Endless Survival/Debug/Mola Yeri ile Başla (aç-kapa)", false, 63)]
        public static void StartInRestStop() => ToggleStart(RestStopPath);

        private static void ToggleStart(string path)
        {
            var profile = AssetDatabase.LoadAssetAtPath<ChunkTypeProfile>(path);
            if (profile == null)
            {
                CreateTypes();
                profile = AssetDatabase.LoadAssetAtPath<ChunkTypeProfile>(path);
                if (profile == null) return;
            }
            var mgr = OpenSceneManager();
            if (mgr == null) return;

            Undo.RecordObject(mgr, "Başlangıç tipi");
            if (mgr.startType == profile)
            {
                string prevPath = AssetDatabase.GUIDToAssetPath(EditorPrefs.GetString(PrevStartKey, ""));
                mgr.startType = !string.IsNullOrEmpty(prevPath) ? AssetDatabase.LoadAssetAtPath<ChunkTypeProfile>(prevPath) : null;
                Debug.Log($"[NewChunkTypes] Başlangıç tipi geri alındı: {(mgr.startType != null ? mgr.startType.DisplayName : "listedeki ilk tip")}");
            }
            else
            {
                // Remember the original start type only once (switching between the test types keeps it)
                bool currentIsTest = mgr.startType != null && (AssetDatabase.GetAssetPath(mgr.startType) == DesertPath
                    || AssetDatabase.GetAssetPath(mgr.startType) == TownPath || AssetDatabase.GetAssetPath(mgr.startType) == RestStopPath);
                if (mgr.startType != null && !currentIsTest)
                    EditorPrefs.SetString(PrevStartKey, AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(mgr.startType)));
                mgr.startType = profile;
                Debug.Log($"[NewChunkTypes] Oyun artık {profile.DisplayName} ile başlıyor (tekrar seçince eski haline döner).");
            }
            EditorUtility.SetDirty(mgr);
            EditorSceneManager.MarkSceneDirty(mgr.gameObject.scene);
            EditorSceneManager.SaveScene(mgr.gameObject.scene);
        }
    }
}
