using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using EndlessSurvival.World.Road;

namespace EndlessSurvival.World.Editor
{
    /// <summary>
    /// Nehir Vadisi tipi: profil asset'ini Orman'dan türetip ayarlar ve Game sahnesindeki ChunkManager'ın tip listesine
    /// ekler. Ayrıca test için oyunu Nehir Vadisi ile başlatma anahtarı.
    /// </summary>
    public static class RiverValleySetup
    {
        public const string ProfilePath = "Assets/Data/ChunkTypes/ChunkType_RiverValley.asset";
        private const string ForestPath = "Assets/Data/ChunkTypes/ChunkType_Forest.asset";
        private const string ScenePath = "Assets/Scenes/Game.unity";
        private const string PrevStartKey = "EndlessSurvival.RiverValley.PrevStartType";

        [MenuItem("Endless Survival/Nehir Vadisi Tipini Oluştur", false, 4)]
        public static void CreateType()
        {
            ChunkTypeProfile profile = CreateOrUpdateProfile();
            if (profile == null) return;
            AddToScene(profile);
        }

        /// <summary>Batch mode entry point (-executeMethod).</summary>
        public static void CreateTypeBatch()
        {
            CreateType();
            EditorApplication.Exit(0);
        }

        public static ChunkTypeProfile CreateOrUpdateProfile()
        {
            var profile = AssetDatabase.LoadAssetAtPath<ChunkTypeProfile>(ProfilePath);
            if (profile == null)
            {
                var forest = AssetDatabase.LoadAssetAtPath<ChunkTypeProfile>(ForestPath);
                if (forest == null)
                {
                    Debug.LogError($"[RiverValley] {ForestPath} bulunamadı, Nehir Vadisi profili ondan türetilir.");
                    return null;
                }
                profile = Object.Instantiate(forest);
                AssetDatabase.CreateAsset(profile, ProfilePath);
            }

            profile.displayName = "Nehir Vadisi";
            profile.biome = ChunkBiomeType.Forest;
            profile.flatSeamEdges = false;
            profile.allowSpecialChunks = false; // a handcrafted chunk would cut the river

            // Road: mostly flat (the water stays below it), gentle bends that the river follows, guardrails
            var e = profile.elevationConfig ?? new ChunkElevationConfig();
            e.elevationChance = 0.5f;
            e.minHillHeight = 5f;
            e.maxHillHeight = 10f;
            e.minDipDepth = 1f;
            e.maxDipDepth = 5f;
            e.hillWeight = 35;
            e.valleyWeight = 0;        // a dip would take the road down to the water
            e.rollingHillsWeight = 0;
            e.mountainPassWeight = 15;
            e.elevatedChicaneWeight = 0;
            e.flatWeight = 50;
            profile.elevationConfig = e;

            var c = profile.curveSettings ?? new RoadCurveSettings();
            c.curveChance = 0.6f;
            c.minBends = 1;
            c.maxBends = 3;
            c.minShift = 10f;
            c.maxShift = 30f;
            c.maxLateral = 35f;
            profile.curveSettings = c;

            var s = profile.roadStyle ?? new ChunkRoadStyle();
            s.curveChanceMultiplier = 1f;
            s.sharpCurveChance = 0f;
            s.alwaysCurvy = false;
            s.forceCrossSection = true;
            s.forcedCrossSection = RoadCrossSectionPreset.GuardrailOnly;
            profile.roadStyle = s;

            if (profile.riverValley == null) profile.riverValley = new RiverValleySettings();
            profile.riverValley.enabled = true;

            // Valley sides: the cliffs stand further out (beyond the far bank) and a little higher
            var b = profile.boundary ?? new BiomeBoundarySettings();
            b.biome = ChunkBiomeType.Forest;
            b.minInnerEdge = 185f;
            b.maxInnerEdge = 210f;
            b.minCliffHeight = 40f;
            b.maxCliffHeight = 70f;
            profile.boundary = b;

            // Between two mountain ranges: taller backdrop than the forest's
            var forestProfile = AssetDatabase.LoadAssetAtPath<ChunkTypeProfile>(ForestPath);
            if (forestProfile != null && forestProfile.backdrop != null)
            {
                var bd = JsonUtility.FromJson<BiomeBackdropSettings>(JsonUtility.ToJson(forestProfile.backdrop));
                bd.biome = ChunkBiomeType.Forest;
                bd.baseHeight *= 1.35f;
                bd.peakAmplitude *= 1.35f;
                profile.backdrop = bd;
            }

            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
            Debug.Log($"[RiverValley] Profil hazır: {ProfilePath}");
            return profile;
        }

        private static ChunkManager OpenSceneManager()
        {
            var scene = EditorSceneManager.GetActiveScene();
            if (scene.path != ScenePath) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var mgr = Object.FindAnyObjectByType<ChunkManager>();
            if (mgr == null) Debug.LogError("[RiverValley] Game sahnesinde ChunkManager yok.");
            return mgr;
        }

        private static void AddToScene(ChunkTypeProfile profile)
        {
            var mgr = OpenSceneManager();
            if (mgr == null) return;

            bool found = false;
            foreach (var entry in mgr.chunkTypes)
            {
                if (entry != null && entry.profile == profile) { found = true; break; }
            }
            if (!found)
            {
                Undo.RecordObject(mgr, "Nehir Vadisi tipi");
                mgr.chunkTypes.Add(new ChunkManager.ChunkTypeEntry { profile = profile, weight = 25, minLength = 3, maxLength = 5 });
                EditorUtility.SetDirty(mgr);
                EditorSceneManager.MarkSceneDirty(mgr.gameObject.scene);
                EditorSceneManager.SaveScene(mgr.gameObject.scene);
                Debug.Log("[RiverValley] ChunkManager tip listesine eklendi (ağırlık 25, 3-5 chunk).");
            }
            else
            {
                Debug.Log("[RiverValley] Tip zaten ChunkManager listesinde.");
            }
        }

        /// <summary>Test: the game starts in a river valley (again = back to the previous start type).</summary>
        [MenuItem("Endless Survival/Debug/Nehir Vadisi ile Başla (aç-kapa)", false, 60)]
        public static void ToggleStartInRiverValley()
        {
            var profile = AssetDatabase.LoadAssetAtPath<ChunkTypeProfile>(ProfilePath);
            if (profile == null) profile = CreateOrUpdateProfile();
            if (profile == null) return;
            var mgr = OpenSceneManager();
            if (mgr == null) return;

            Undo.RecordObject(mgr, "Nehir Vadisi başlangıç");
            if (mgr.startType == profile)
            {
                string prevPath = AssetDatabase.GUIDToAssetPath(EditorPrefs.GetString(PrevStartKey, ""));
                mgr.startType = !string.IsNullOrEmpty(prevPath) ? AssetDatabase.LoadAssetAtPath<ChunkTypeProfile>(prevPath) : null;
                Debug.Log($"[RiverValley] Başlangıç tipi geri alındı: {(mgr.startType != null ? mgr.startType.DisplayName : "listedeki ilk tip")}");
            }
            else
            {
                if (mgr.startType != null)
                    EditorPrefs.SetString(PrevStartKey, AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(mgr.startType)));
                mgr.startType = profile;
                Debug.Log("[RiverValley] Oyun artık Nehir Vadisi ile başlıyor (tekrar seçince eski haline döner).");
            }
            EditorUtility.SetDirty(mgr);
            EditorSceneManager.MarkSceneDirty(mgr.gameObject.scene);
            EditorSceneManager.SaveScene(mgr.gameObject.scene);
        }
    }
}
