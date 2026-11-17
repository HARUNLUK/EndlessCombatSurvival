using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using EndlessSurvival.World;

namespace EndlessSurvival.Testing.Editor
{
    /// <summary>
    /// Builds the three test scenes as copies of Assets/Scenes/Game.unity (same player, UI and managers), with the
    /// endless world switched off and a TestArena object that builds the arena at runtime. Run it again after the
    /// game scene changes: the copies are made fresh. The game scene itself is not changed.
    /// </summary>
    public static class TestSceneBuilder
    {
        private const string GameScene = "Assets/Scenes/Game.unity";
        private const string Folder = "Assets/Scenes/Test";

        private static readonly (string file, TestArena.Mode mode)[] Scenes =
        {
            ("Test_Savas", TestArena.Mode.Combat),
            ("Test_Gizlilik", TestArena.Mode.Stealth),
            ("Test_Surus", TestArena.Mode.Driving),
        };

        private static string PathOf(string file) => $"{Folder}/{file}.unity";

        [MenuItem("Endless Survival/Test Sahneleri/Kur - Yenile (Game.unity kopyası)", priority = 1)]
        public static void BuildAll() => Build(false);

        /// <summary>Path of the combat (action) test scene.</summary>
        public static string CombatScenePath => PathOf("Test_Savas");

        /// <summary>Builds the test scenes without any dialog when the combat scene is missing (automatic play test).</summary>
        public static void EnsureBuilt()
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(CombatScenePath) == null) Build(true);
        }

        private static void Build(bool quiet)
        {
            if (EditorApplication.isPlaying)
            {
                EditorUtility.DisplayDialog("Test sahneleri", "Önce Play modundan çık.", "Tamam");
                return;
            }
            if (!quiet && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(GameScene) == null)
            {
                if (quiet) Debug.LogError("[TestSceneBuilder] " + GameScene + " bulunamadı"); else EditorUtility.DisplayDialog("Test sahneleri", GameScene + " bulunamadı.", "Tamam");
                return;
            }
            string previous = EditorSceneManager.GetActiveScene().path;

            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/Scenes", "Test");
            if (AssetDatabase.IsValidFolder(GeneratedFolder)) AssetDatabase.DeleteAsset(GeneratedFolder);
            AssetDatabase.CreateFolder(Folder, "Generated");

            foreach (var (file, mode) in Scenes)
            {
                string path = PathOf(file);
                if (AssetDatabase.LoadAssetAtPath<SceneAsset>(path) != null) AssetDatabase.DeleteAsset(path);
                if (!AssetDatabase.CopyAsset(GameScene, path))
                {
                    Debug.LogError("[TestSceneBuilder] Kopyalanamadı: " + path);
                    continue;
                }

                var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                StripToEssentials(scene, file);

                // The test content lives in the scene itself (visible and editable in the hierarchy):
                //   TestArena (mode) > Arena (ground, walls, cover...) and > NPCs (squad groups > NPC markers)
                var go = new GameObject("TestArena (" + mode + ")");
                var arena = go.AddComponent<TestArena>();
                arena.mode = mode;
                arena.BuildWorld();
                arena.BuildNpcMarkers();
                PersistGenerated(go, file);
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                Debug.Log($"[TestSceneBuilder] {path} kuruldu ({mode}).");
            }

            AddToBuildSettings();
            AssetDatabase.SaveAssets();

            if (!string.IsNullOrEmpty(previous) && AssetDatabase.LoadAssetAtPath<SceneAsset>(previous) != null)
                EditorSceneManager.OpenScene(previous, OpenSceneMode.Single);
            if (!quiet) EditorUtility.DisplayDialog("Test sahneleri",
                "Kuruldu: Assets/Scenes/Test/Test_Savas, Test_Gizlilik, Test_Surus.\n\nAçmak için: Endless Survival > Test Sahneleri menüsü. Play'e bas; test düğmeleri ayar panelinde (L3 + R3 / F1), \"Test\" sayfası. Bu sahnelerde kayıt yapılmaz.", "Tamam");
        }

        private const string GeneratedFolder = Folder + "/Generated";

        /// <summary>
        /// What a test scene keeps from Game.unity: the player, cameras, light, post volume, UI, the day / night
        /// manager and the player's vehicle. Everything else (ChunkManager and its chunks, EditorPreview with its
        /// vegetation and preview enemies, the ground plane, notes) is deleted: the test scene has no endless world.
        /// </summary>
        private static readonly HashSet<string> KeepRoots = new HashSet<string>
        {
            "PlayerArmature", "MainCamera", "PlayerFollowCamera", "VehicleFollowCamera", "Directional Light", "Global Volume",
            "EventSystem", "UI", "InventoryCanvas", "CombatUI_Canvas", "TimeHUD_Canvas", "StealthHUD", "DayNightCycle_Manager",
        };

        private static void StripToEssentials(UnityEngine.SceneManagement.Scene scene, string file)
        {
            var removed = new List<string>();
            foreach (var root in scene.GetRootGameObjects())
            {
                if (KeepRoots.Contains(root.name)) continue;
                if (root.GetComponentInChildren<EndlessSurvival.Vehicle.VehicleController>(true) != null) continue; // the player's car
                removed.Add(root.name);
                Object.DestroyImmediate(root);
            }
            Debug.Log($"[TestSceneBuilder] {file}: Game.unity'den silinenler: {string.Join(", ", removed)}");
        }

        /// <summary>
        /// Materials and terrain data made by the arena code are saved as assets next to the scenes, so the scene
        /// keeps them (an unsaved TerrainData or Material would be lost on reopen).
        /// </summary>
        private static void PersistGenerated(GameObject root, string prefix)
        {
            var saved = new Dictionary<Material, Material>();
            int n = 0;
            Material Save(Material m)
            {
                if (m == null || EditorUtility.IsPersistent(m)) return m;
                if (saved.TryGetValue(m, out var done)) return done;
                AssetDatabase.CreateAsset(m, $"{GeneratedFolder}/{prefix}_Mat{n++}.mat");
                saved[m] = m;
                return m;
            }
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++) mats[i] = Save(mats[i]);
                r.sharedMaterials = mats;
            }
            foreach (var t in root.GetComponentsInChildren<Terrain>(true))
            {
                if (t.terrainData != null && !EditorUtility.IsPersistent(t.terrainData))
                    AssetDatabase.CreateAsset(t.terrainData, $"{GeneratedFolder}/{prefix}_Terrain.asset");
                t.materialTemplate = Save(t.materialTemplate);
                var col = t.GetComponent<TerrainCollider>();
                if (col != null) col.terrainData = t.terrainData;
            }
        }

        /// <summary>Death reloads the active scene by build index, so the test scenes must be in the build list.</summary>
        private static void AddToBuildSettings()
        {
            var list = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            foreach (var (file, _) in Scenes)
            {
                string path = PathOf(file);
                if (!list.Exists(s => s.path == path)) list.Add(new EditorBuildSettingsScene(path, true));
            }
            EditorBuildSettings.scenes = list.ToArray();
        }

        [MenuItem("Endless Survival/Test Sahneleri/Savaş sahnesini aç", priority = 20)]
        private static void OpenCombat() => Open("Test_Savas");

        [MenuItem("Endless Survival/Test Sahneleri/Gizlilik sahnesini aç", priority = 21)]
        private static void OpenStealth() => Open("Test_Gizlilik");

        [MenuItem("Endless Survival/Test Sahneleri/Sürüş sahnesini aç", priority = 22)]
        private static void OpenDriving() => Open("Test_Surus");

        [MenuItem("Endless Survival/Test Sahneleri/Ana oyun sahnesini aç", priority = 40)]
        private static void OpenGame() => Open(null);

        private static void Open(string file)
        {
            if (EditorApplication.isPlaying) return;
            string path = file == null ? GameScene : PathOf(file);
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(path) == null)
            {
                EditorUtility.DisplayDialog("Test sahneleri", path + " yok. Önce \"Kur - Yenile\" çalıştır.", "Tamam");
                return;
            }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
        }
    }
}
