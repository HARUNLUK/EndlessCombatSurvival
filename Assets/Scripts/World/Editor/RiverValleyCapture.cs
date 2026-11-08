using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using EndlessSurvival.Survival;
using EndlessSurvival.World.Road;

namespace EndlessSurvival.World.Editor
{
    /// <summary>
    /// Debug: starts Game.unity in a river valley with a seed whose second chunk has a bridge crossing, and saves
    /// pictures of the river to PoseShots/river_*.png (plus river_log.txt with errors). Then quits.
    /// The scene is changed in memory only (start type + seed), it is not saved.
    /// Run the editor (not batchmode) with: -executeMethod EndlessSurvival.World.Editor.RiverValleyCapture.Run
    /// </summary>
    [InitializeOnLoad]
    public static class RiverValleyCapture
    {
        private const string Flag = "EndlessSurvival.RiverValleyCapture.Active";
        private const string ScenePath = "Assets/Scenes/Game.unity";
        private const int W = 1280, H = 720;

        private static double _t0 = -1;
        private static double _stepAt;
        private static int _step;
        private static Camera _cam;
        private static RenderTexture _rt;
        private static readonly StringWriter Log = new StringWriter();
        private static int _errors;

        static RiverValleyCapture()
        {
            if (!EditorPrefs.GetBool(Flag, false)) return;
            EditorApplication.update += Tick;
            Application.logMessageReceived += OnLog;
        }

        [MenuItem("Endless Survival/Debug/Nehir Vadisi Ekran Görüntüleri")]
        public static void Run()
        {
            var profile = AssetDatabase.LoadAssetAtPath<ChunkTypeProfile>(RiverValleySetup.ProfilePath);
            if (profile == null) profile = RiverValleySetup.CreateOrUpdateProfile();
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var mgr = Object.FindAnyObjectByType<ChunkManager>();
            if (profile == null || mgr == null) { Debug.LogError("[RiverCapture] profil veya ChunkManager yok"); return; }

            // A seed whose chunk 1 (middle of the 3-chunk start run) crosses the road
            string seed = null;
            for (int i = 0; i < 5000 && seed == null; i++)
            {
                string s = "RIVER" + i;
                if (ValleyRiverGenerator.RollsCrossing(SeededRandom.HashString(s), 1, profile.riverValley.crossingChance)) seed = s;
            }
            mgr.startType = profile;
            mgr.masterSeed = seed ?? "RIVER0";
            EditorPrefs.SetString(Flag + ".seed", mgr.masterSeed);

            EditorPrefs.SetBool(Flag, true);
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
            Application.logMessageReceived -= OnLog;
            Application.logMessageReceived += OnLog;
            EditorApplication.EnterPlaymode();
        }

        private static void OnLog(string msg, string stack, LogType type)
        {
            if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
            _errors++;
            Log.WriteLine($"[{type}] {msg}\n{stack}");
        }

        private static string OutDir => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "PoseShots"));

        private static void Finish()
        {
            Log.WriteLine($"errors: {_errors}");
            EditorPrefs.DeleteKey(Flag);
            EditorApplication.update -= Tick;
            Application.logMessageReceived -= OnLog;
            Directory.CreateDirectory(OutDir);
            File.WriteAllText(Path.Combine(OutDir, "river_log.txt"), Log.ToString());
            if (Application.isBatchMode || System.Environment.GetCommandLineArgs().Contains("-executeMethod"))
                EditorApplication.Exit(0);
            else
                EditorApplication.ExitPlaymode();
        }

        private static void Tick()
        {
            if (!EditorApplication.isPlaying) return;
            double now = EditorApplication.timeSinceStartup;
            if (_t0 < 0)
            {
                _t0 = now;
                _stepAt = now + 4.0;
                Directory.CreateDirectory(OutDir);
                foreach (var f in Directory.GetFiles(OutDir, "river_*.png")) File.Delete(f);
                Log.WriteLine("seed " + EditorPrefs.GetString(Flag + ".seed", "?"));
                return;
            }
            if (now < _stepAt) return;
            if (now - _t0 > 240) { Log.WriteLine("timeout"); Finish(); return; }
            try
            {
                double wait = DoStep(_step);
                if (wait < 0) { Finish(); return; }
                _stepAt = now + wait;
            }
            catch (System.Exception e)
            {
                Log.WriteLine("step " + _step + " failed: " + e);
                Finish();
                return;
            }
            _step++;
        }

        private static Chunk ChunkAt(int index) =>
            ChunkManager.Instance != null ? ChunkManager.Instance.ActiveChunks.FirstOrDefault(c => c != null && c.ChunkIndex == index) : null;

        private static Vector3 RoadAt(Chunk c, float z)
        {
            var spline = c.GetComponentInChildren<RoadSpline>();
            return spline.transform.TransformPoint(spline.GetPointAtZ(z));
        }

        /// <summary>World X center of the water at a chunk-local Z (scan across the chunk), or NaN.</summary>
        private static float WaterCenterX(Chunk c, float z, out float surfaceY)
        {
            surfaceY = float.NaN;
            float first = float.NaN, last = float.NaN;
            for (float x = -245f; x <= 245f; x += 1f)
            {
                Vector3 p = c.transform.TransformPoint(new Vector3(x, 0f, z));
                if (c.IsInRiver(p, 0f))
                {
                    if (float.IsNaN(first)) first = p.x;
                    last = p.x;
                }
                else if (!float.IsNaN(first)) break;
            }
            if (float.IsNaN(first)) return float.NaN;
            float cx = (first + last) * 0.5f;
            Vector3 probe = new Vector3(cx, 60f, c.transform.position.z + z);
            if (WaterBodies.Query(probe + Vector3.down * 59f, out float sy, out _)) surfaceY = sy;
            Log.WriteLine($"   water at z={z:0}: x {first:0.0}..{last:0.0} width {last - first:0.0} surfaceY={surfaceY:0.00}");
            return cx;
        }

        // Each step returns how long to wait before the next one (negative = done).
        private static double DoStep(int step)
        {
            if (step == 0)
            {
                var menus = Object.FindAnyObjectByType<GameMenus>();
                if (menus != null) menus.CloseMenu();
                if (DayNightCycle.Instance != null) DayNightCycle.Instance.SetTime(12f);
                return 1.0;
            }
            if (step == 1)
            {
                // Wait for the start run to be built
                Chunk c2 = ChunkAt(2);
                if (c2 == null || !c2.IsBuilt || ChunkAt(1) == null || !ChunkAt(1).IsBuilt)
                {
                    _step--; // repeat this step
                    return 0.5;
                }
                if (DayNightCycle.Instance != null) DayNightCycle.Instance.SetTime(12f);
                SetupCamera();
                for (int i = 0; i <= 3; i++)
                {
                    Chunk c = ChunkAt(i);
                    if (c == null) continue;
                    Log.WriteLine($"chunk {i}: {c.name} river={c.IsRiverValley} entersFromSide={c.RiverEntersFromSide} leavesToSide={c.RiverLeavesToSide}"
                                  + $" entry={c.riverEntrySide} exit={c.riverExitSide} crossing={c.HasRiverCrossing} crossingZ={c.riverCrossingZ:0}"
                                  + $" water={(c.transform.Find(ValleyRiverGenerator.CONTAINER_NAME + "/River_Water") != null || c.GetComponentsInChildren<MeshFilter>().Any(m => m.name == "River_Water"))}");
                }
                return 0.5;
            }

            Chunk c0 = ChunkAt(0), c1 = ChunkAt(1), c2b = ChunkAt(2);
            switch (step)
            {
                case 2:
                {
                    // Aerial over the crossing chunk
                    Vector3 cross = RoadAt(c1, c1.riverCrossingZ);
                    Shot("river_01_aerial_crossing", cross + new Vector3(-160f, 140f, -220f), cross);
                    Shot("river_01b_aerial_top", cross + new Vector3(0f, 380f, -60f), cross);
                    return 0.2;
                }
                case 3:
                {
                    // The bridge from the river, both sides
                    Vector3 cross = RoadAt(c1, c1.riverCrossingZ);
                    float zA = c1.riverCrossingZ - 70f, zB = c1.riverCrossingZ + 70f;
                    float xA = WaterCenterX(c1, zA, out float yA);
                    WaterCenterX(c1, c1.riverCrossingZ, out _);
                    float xB = WaterCenterX(c1, zB, out float yB);
                    float baseZ = c1.transform.position.z;
                    if (!float.IsNaN(xA) && !float.IsNaN(yA))
                        Shot("river_02_bridge_side_a", new Vector3(xA, yA + 2.5f, baseZ + zA), cross + Vector3.down * 3f);
                    if (!float.IsNaN(xB) && !float.IsNaN(yB))
                        Shot("river_02b_bridge_side_b", new Vector3(xB, yB + 2.5f, baseZ + zB), cross + Vector3.down * 3f);
                    int piers = c1.GetComponentsInChildren<Transform>().Count(t => t.name.Contains("Pier"));
                    Log.WriteLine($"   bridge objects: {c1.GetComponentsInChildren<Transform>().Count(t => t.name.Contains("Bridge"))}, piers {piers}");
                    Shot("river_02c_on_bridge", RoadAt(c1, c1.riverCrossingZ - 40f) + Vector3.up * 1.8f, RoadAt(c1, c1.riverCrossingZ + 20f) + Vector3.up * 1.2f);
                    return 0.2;
                }
                case 4:
                {
                    // Road level: river beside the road (chunk 0 middle, chunk 2 start)
                    Vector3 a = RoadAt(c0, 330f);
                    Shot("river_03_road_level", a + Vector3.up * 1.8f, RoadAt(c0, 420f) + Vector3.up * 1.0f);
                    Vector3 b = RoadAt(c2b, 120f);
                    Shot("river_03b_road_level_c2", b + Vector3.up * 1.8f, RoadAt(c2b, 220f) + Vector3.up * 1.0f);
                    WaterCenterX(c0, 330f, out _);
                    WaterCenterX(c2b, 120f, out _);
                    return 0.2;
                }
                case 5:
                {
                    // Entry bend (chunk 0) and exit bend (chunk 2)
                    Vector3 e = RoadAt(c0, 150f);
                    Shot("river_04_entry_bend", e + new Vector3(0f, 160f, -200f), e + new Vector3(0f, 0f, 60f));
                    Vector3 x = RoadAt(c2b, 380f);
                    Shot("river_04b_exit_bend", x + new Vector3(0f, 170f, -240f), x + new Vector3(0f, 0f, 40f));
                    return 0.2;
                }
                case 6:
                {
                    // Wading: in the water of chunk 0, looking upstream and across
                    float z = 330f;
                    float cx = WaterCenterX(c0, z, out float surfaceY);
                    if (float.IsNaN(cx)) { Log.WriteLine("no water found for wading shot"); return 0.1; }
                    Vector3 p = new Vector3(cx, 0f, c0.transform.position.z + z);
                    Terrain t = c0.ChunkTerrain;
                    float bed = t != null ? t.SampleHeight(p) + t.transform.position.y : surfaceY - 1f;
                    Log.WriteLine($"   wading: bed {bed:0.00}, surface {surfaceY:0.00}, depth {surfaceY - bed:0.00}");
                    p.y = (float.IsNaN(surfaceY) ? bed + 1f : surfaceY) + 0.7f;
                    Vector3 road = RoadAt(c0, z);
                    Shot("river_05_wading_upstream", p, p + new Vector3(0f, -0.3f, 30f));
                    Shot("river_05b_wading_to_road", p, new Vector3(road.x, road.y + 1f, road.z + 10f));

                    // Put the player in the water: WaterBodies must see it
                    var player = GameObject.FindGameObjectWithTag("Player");
                    if (player != null)
                    {
                        var cc = player.GetComponent<CharacterController>();
                        if (cc != null) cc.enabled = false;
                        player.transform.position = new Vector3(cx, bed + 0.05f, p.z);
                        if (cc != null) cc.enabled = true;
                        bool inWater = WaterBodies.Query(player.transform.position + Vector3.up * 0.5f, out float sy, out var kind);
                        Log.WriteLine($"   player in water: {inWater} kind={kind} surface={sy:0.00}");
                    }
                    return 1.5;
                }
                case 7:
                {
                    var player = GameObject.FindGameObjectWithTag("Player");
                    if (player != null)
                    {
                        Log.WriteLine($"   player after 1.5s: y={player.transform.position.y:0.00}");
                        Shot("river_06_player_wading", player.transform.position + new Vector3(4f, 2.2f, -5f), player.transform.position + Vector3.up * 0.8f);
                    }
                    return 0.2;
                }
                default:
                    return -1;
            }
        }

        private static void SetupCamera()
        {
            var go = new GameObject("RiverCaptureCam");
            _cam = go.AddComponent<Camera>();
            var main = Camera.main;
            if (main != null) _cam.CopyFrom(main);
            _cam.fieldOfView = 60f;
            _cam.nearClipPlane = 0.1f;
            _cam.farClipPlane = 4000f;
            _cam.enabled = false;
            _rt = new RenderTexture(W, H, 24);
            _cam.targetTexture = _rt;
        }

        private static void Shot(string name, Vector3 from, Vector3 lookAt)
        {
            _cam.transform.position = from;
            _cam.transform.LookAt(lookAt);
            _cam.Render();
            RenderTexture.active = _rt;
            var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            File.WriteAllBytes(Path.Combine(OutDir, name + ".png"), tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            Log.WriteLine($"shot {name} from {from:F0} at {lookAt:F0}");
        }
    }
}
