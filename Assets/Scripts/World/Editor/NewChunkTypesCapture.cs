using System.Collections.Generic;
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
    /// Debug: runs the type setup (NewChunkTypesSetup.CreateTypes + transition rules), then plays Game.unity a few times
    /// and saves pictures to PoseShots/newtypes_*.png (+ newtypes_log.txt):
    ///  - desert_in: Düzlük run of 2 chunks, then Çorak Arazi (chunk 2 is the transition chunk)
    ///  - desert_out: Çorak Arazi run of 2 chunks, then its neighbour (chunk 2 is the transition chunk)
    ///  - town: a town set into a desert run (start type Harabe Kasaba)
    ///  - stop: a rest stop set into a forest run (start type Mola Yeri)
    /// Start type, seed and the type list are changed in memory only (the scene is not saved). Then quits.
    /// Run the editor (not batchmode) with: -executeMethod EndlessSurvival.World.Editor.NewChunkTypesCapture.Run
    /// </summary>
    [InitializeOnLoad]
    public static class NewChunkTypesCapture
    {
        private const string Flag = "EndlessSurvival.NewTypesCapture.Active";
        private const string IndexKey = Flag + ".index";
        private const string ScenePath = "Assets/Scenes/Game.unity";
        private const int W = 1280, H = 720;

        private class Session
        {
            public string tag, start, seed;
            public System.Action<ChunkManager> tweak;
            public bool transition; // shots around the chunk 1 / chunk 2 seam
            public bool wide;       // 1000 m chunk + side roads: wide views and one view per side road
        }

        /// <summary>In memory (restored by ApplySideRoadRules at the end): every chunk of the type gets its side roads.</summary>
        private static void AlwaysSideRoads(string type, bool paved = false)
        {
            var p = AssetDatabase.LoadAssetAtPath<ChunkTypeProfile>("Assets/Data/ChunkTypes/ChunkType_" + type + ".asset");
            if (p != null && p.sideRoads != null) { p.sideRoads.chance = 1f; if (paved) p.sideRoads.pavedChance = 1f; }
        }

        private static readonly Session[] Sessions =
        {
            new Session { tag = "desert_in", start = "Field", seed = "SHOTDIN2", transition = true, tweak = m => { Only(m, "Field", 2); Only(m, "Desert", 0, keepOthers: false, onlyNext: "Desert"); } },
            new Session { tag = "desert_out", start = "Desert", seed = "SHOTDOUT4", transition = true, tweak = m => Only(m, "Desert", 2) },
            new Session { tag = "town", start = "Town", seed = "SHOTTOWN7" },
            new Session { tag = "stop", start = "RestStop", seed = "SHOTSTOP3" },
            new Session { tag = "wide_forest", start = "Forest", seed = "SHOTWFOR1", wide = true, tweak = m => { Only(m, "Forest", 6); AlwaysSideRoads("Forest"); } },
            new Session { tag = "wide_field", start = "Field", seed = "SHOTWFLD2", wide = true, tweak = m => { Only(m, "Field", 5); AlwaysSideRoads("Field"); } },
            new Session { tag = "wide_desert", start = "Desert", seed = "SHOTWDES3", wide = true, tweak = m => { Only(m, "Desert", 6); AlwaysSideRoads("Desert"); } },
            new Session { tag = "wide_town", start = "Town", seed = "SHOTWTWN4", wide = true, tweak = m => AlwaysSideRoads("Town") },
            new Session { tag = "wide_paved", start = "Forest", seed = "SHOTWPAV6", wide = true, tweak = m => { Only(m, "Forest", 6); AlwaysSideRoads("Forest", true); } },
            new Session { tag = "wide_mountain", start = "Mountain", seed = "SHOTWMTN5", wide = true },
        };

        private static double _t0 = -1, _stepAt, _builtAt = -1;
        private static int _step;
        private static Camera _cam;
        private static RenderTexture _rt;
        private static int _errors;

        static NewChunkTypesCapture()
        {
            if (!EditorPrefs.GetBool(Flag, false)) return;
            EditorApplication.update += Tick;
            EditorApplication.playModeStateChanged += OnPlayMode;
            Application.logMessageReceived += OnLog;
        }

        /// <summary>
        /// In memory: the given type's run is exactly <paramref name="length"/> chunks (0 = unchanged); with
        /// onlyNext every other run type gets weight 0 so that type must come next.
        /// </summary>
        private static void Only(ChunkManager m, string type, int length, bool keepOthers = true, string onlyNext = null)
        {
            foreach (var e in m.chunkTypes)
            {
                if (e == null || e.profile == null) continue;
                if (length > 0 && e.profile.name == "ChunkType_" + type) { e.minLength = length; e.maxLength = length; }
                if (onlyNext != null && !e.profile.IsInset && e.profile.name != "ChunkType_" + onlyNext) e.weight = 0;
            }
        }

        [MenuItem("Endless Survival/Debug/Yeni Chunk Tipleri Ekran Görüntüleri")]
        public static void Run()
        {
            NewChunkTypesSetup.CreateTypes();
            NewChunkTypesSetup.ApplySideRoadRules();
            Directory.CreateDirectory(OutDir);
            // -captureOnly town,stop: only those sessions (their old pictures are replaced, the others kept)
            var args = System.Environment.GetCommandLineArgs();
            int only = System.Array.IndexOf(args, "-captureOnly");
            string filter = only >= 0 && only + 1 < args.Length ? args[only + 1] : "";
            EditorPrefs.SetString(OnlyKey, filter);
            foreach (var f in Directory.GetFiles(OutDir, "newtypes_*.png"))
                if (filter == "" || Sessions.Any(x => Wanted(x) && Path.GetFileName(f).StartsWith("newtypes_" + x.tag + "_"))) File.Delete(f);
            File.WriteAllText(LogPath, "");
            EditorPrefs.SetBool(Flag, true);
            int first = NextWanted(0);
            EditorPrefs.SetInt(IndexKey, first);
            if (first >= Sessions.Length) { Finish(); return; }
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
            EditorApplication.playModeStateChanged -= OnPlayMode;
            EditorApplication.playModeStateChanged += OnPlayMode;
            Application.logMessageReceived -= OnLog;
            Application.logMessageReceived += OnLog;
            Begin(first);
        }

        private const string OnlyKey = Flag + ".only";
        private static bool Wanted(Session s)
        {
            string filter = EditorPrefs.GetString(OnlyKey, "");
            return filter == "" || filter.Split(',').Contains(s.tag);
        }
        private static int NextWanted(int from)
        {
            int i = from;
            while (i < Sessions.Length && !Wanted(Sessions[i])) i++;
            return i;
        }

        private static void Begin(int index)
        {
            var s = Sessions[index];
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var mgr = Object.FindAnyObjectByType<ChunkManager>();
            var profile = AssetDatabase.LoadAssetAtPath<ChunkTypeProfile>("Assets/Data/ChunkTypes/ChunkType_" + s.start + ".asset");
            if (mgr == null || profile == null) { Write("no ChunkManager / profile " + s.start); Finish(); return; }
            mgr.startType = profile;
            mgr.masterSeed = s.seed;
            s.tweak?.Invoke(mgr);
            mgr.ResetTypePlan();
            Write($"=== {s.tag}: start {profile.DisplayName}, seed {s.seed}");
            EditorApplication.EnterPlaymode();
        }

        private static void OnPlayMode(PlayModeStateChange state)
        {
            if (!EditorPrefs.GetBool(Flag, false)) return;
            if (state == PlayModeStateChange.EnteredPlayMode)
            {
                _t0 = -1; _builtAt = -1; _step = 0; _errors = 0; _cam = null;
            }
            else if (state == PlayModeStateChange.EnteredEditMode)
            {
                int next = NextWanted(EditorPrefs.GetInt(IndexKey, 0) + 1);
                EditorPrefs.SetInt(IndexKey, next);
                if (next < Sessions.Length) EditorApplication.delayCall += () => Begin(next);
                else Finish();
            }
        }

        private static void OnLog(string msg, string stack, LogType type)
        {
            if (msg.StartsWith("[Settlement]") || msg.StartsWith("[SideRoads]")) { Write(msg); return; }
            if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
            if (msg.Contains("ProjectAuditor") || stack.Contains("ProjectAuditor")) return; // editor package noise
            _errors++;
            Write($"[{type}] {msg}\n{stack}");
        }

        private static string OutDir => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "PoseShots"));
        private static string LogPath => Path.Combine(OutDir, "newtypes_log.txt");
        private static void Write(string line) { Directory.CreateDirectory(OutDir); File.AppendAllText(LogPath, line + "\n"); }

        private static void Finish()
        {
            Write("done");
            NewChunkTypesSetup.ApplySideRoadRules(); // undo the in-memory "always side roads"
            EditorPrefs.DeleteKey(Flag);
            EditorPrefs.DeleteKey(IndexKey);
            EditorApplication.update -= Tick;
            EditorApplication.playModeStateChanged -= OnPlayMode;
            Application.logMessageReceived -= OnLog;
            if (Application.isBatchMode || System.Environment.GetCommandLineArgs().Contains("-executeMethod"))
                EditorApplication.Exit(0);
        }

        private static void EndSession()
        {
            Write($"errors this session: {_errors}");
            EditorApplication.ExitPlaymode();
        }

        private static Session Current => Sessions[Mathf.Clamp(EditorPrefs.GetInt(IndexKey, 0), 0, Sessions.Length - 1)];

        private static void Tick()
        {
            if (!EditorApplication.isPlaying) return;
            double now = EditorApplication.timeSinceStartup;
            if (_t0 < 0) { _t0 = now; _stepAt = now + 3.0; return; }
            if (now < _stepAt) return;
            if (now - _t0 > 180) { Write("timeout"); EndSession(); _t0 = double.MaxValue; return; }
            try
            {
                double wait = DoStep(_step, now);
                if (wait < 0) { EndSession(); _t0 = double.MaxValue; return; }
                _stepAt = now + wait;
            }
            catch (System.Exception e)
            {
                Write("step " + _step + " failed: " + e);
                EndSession();
                _t0 = double.MaxValue;
                return;
            }
            _step++;
        }

        private static Chunk ChunkAt(int index) =>
            ChunkManager.Instance != null ? ChunkManager.Instance.ActiveChunks.FirstOrDefault(c => c != null && c.ChunkIndex == index) : null;

        private static Vector3 RoadAt(Chunk c, float z)
        {
            var spline = c.GetComponentInChildren<RoadSpline>();
            Vector3 w = c.transform.TransformPoint(new Vector3(0f, 0f, z));
            return spline.transform.TransformPoint(spline.GetPointAtZ(spline.transform.InverseTransformPoint(w).z));
        }

        private static double DoStep(int step, double now)
        {
            Session s = Current;
            if (step == 0)
            {
                var menus = Object.FindAnyObjectByType<GameMenus>();
                if (menus != null) menus.CloseMenu();
                if (DayNightCycle.Instance != null) DayNightCycle.Instance.SetTime(12f);
                if (WeatherSystem.Instance != null) WeatherSystem.Instance.DebugForce(WeatherType.Clear);
                return 1.0;
            }
            if (step == 1)
            {
                int need = s.transition ? 2 : 4; // the inset is on the start chunk (index 3 here)
                for (int i = 0; i <= need; i++)
                {
                    Chunk c = ChunkAt(i);
                    if (c == null || !c.IsBuilt) { _step--; return 0.5; }
                }
                if (_builtAt < 0) { _builtAt = now; _step--; return 4.0; } // settlement coroutine
                if (DayNightCycle.Instance != null) DayNightCycle.Instance.SetTime(12f);
                if (WeatherSystem.Instance != null) WeatherSystem.Instance.DebugForce(WeatherType.Clear);
                SetupCamera();
                for (int i = 0; i <= 4; i++)
                {
                    var c = ChunkAt(i);
                    if (c == null) continue;
                    var settlement = c.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name.StartsWith("Settlement_"));
                    string parts = settlement != null
                        ? string.Join(", ", Enumerable.Range(0, settlement.childCount).Select(k => settlement.GetChild(k).name).GroupBy(n => n).Select(g => $"{g.Key} x{g.Count()}"))
                        : "-";
                    Write($"chunk {i}: {c.TypeName} biome={c.biomeType} inset={(c.inset != null ? c.inset.DisplayName : "-")} transition={c.IsTransitionChunk} prev={c.prevBiome} next={c.nextBiome} | {parts}");
                }
                return 0.3;
            }

            string tag = s.tag;
            if (s.transition)
            {
                Chunk c1 = ChunkAt(1), c2 = ChunkAt(2);
                switch (step)
                {
                    case 2:
                    {
                        Vector3 seam = RoadAt(c1, c1.chunkLength - 1f);
                        Vector3 mid2 = RoadAt(c2, 220f);
                        Shot($"newtypes_{tag}_01_aerial", seam + new Vector3(-170f, 140f, -260f), mid2);
                        Shot($"newtypes_{tag}_02_aerial_side", RoadAt(c2, 60f) + new Vector3(260f, 170f, -60f), RoadAt(c2, 120f));
                        Shot($"newtypes_{tag}_03_top", RoadAt(c2, 80f) + new Vector3(0f, 520f, -40f), RoadAt(c2, 80f));
                        return 0.2;
                    }
                    case 3:
                    {
                        Shot($"newtypes_{tag}_04_road_before", RoadAt(c1, c1.chunkLength - 120f) + Vector3.up * 1.8f, RoadAt(c1, c1.chunkLength - 20f) + Vector3.up * 1.2f);
                        Shot($"newtypes_{tag}_05_road_seam", RoadAt(c1, c1.chunkLength - 10f) + Vector3.up * 1.8f, RoadAt(c2, 90f) + Vector3.up * 1.2f);
                        Shot($"newtypes_{tag}_06_road_mid", RoadAt(c2, 180f) + Vector3.up * 1.8f, RoadAt(c2, 280f) + Vector3.up * 1.2f);
                        return 0.2;
                    }
                    default:
                        return -1;
                }
            }

            if (s.wide) return WideStep(step, tag);

            // The town / rest stop sits on the player's start chunk (not always index 0)
            Chunk c0 =Enumerable.Range(0, 8).Select(ChunkAt).FirstOrDefault(c => c != null && c.inset != null) ?? ChunkAt(0);
            switch (step)
            {
                case 2:
                {
                    Vector3 mid = RoadAt(c0, 250f);
                    Shot($"newtypes_{tag}_01_aerial", mid + new Vector3(-190f, 150f, -210f), mid);
                    Shot($"newtypes_{tag}_02_top", mid + new Vector3(0f, 430f, -40f), mid);
                    return 0.2;
                }
                case 3:
                {
                    Shot($"newtypes_{tag}_03_road_entry", RoadAt(c0, 40f) + Vector3.up * 1.8f, RoadAt(c0, 140f) + Vector3.up * 1.2f);
                    Shot($"newtypes_{tag}_04_road_mid", RoadAt(c0, 230f) + Vector3.up * 1.8f, RoadAt(c0, 330f) + Vector3.up * 1.2f);
                    Vector3 p = RoadAt(c0, 250f);
                    Vector3 right = c0.transform.right;
                    Shot($"newtypes_{tag}_05_side_left", p - right * 4f + Vector3.up * 1.7f, p - right * 40f + Vector3.up * 3f);
                    Shot($"newtypes_{tag}_06_side_right", p + right * 4f + Vector3.up * 1.7f, p + right * 40f + Vector3.up * 3f);
                    return 0.2;
                }
                case 4:
                {
                    // Close-ups of a few different settlement pieces
                    var settlement = c0.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name.StartsWith("Settlement_"));
                    if (settlement == null) return 0.2;
                    var seen = new HashSet<string>();
                    int n = 0;
                    for (int i = 0; i < settlement.childCount && n < 6; i++)
                    {
                        Transform b = settlement.GetChild(i);
                        if (b.name == "StreetLamp" || !seen.Add(b.name)) continue;
                        var rends = b.GetComponentsInChildren<Renderer>();
                        if (rends.Length == 0) continue;
                        Bounds bounds = rends[0].bounds;
                        foreach (var r in rends) bounds.Encapsulate(r.bounds);
                        float size = Mathf.Max(bounds.extents.x, bounds.extents.z, 3f);
                        Vector3 look = bounds.center;
                        Vector3 from = look + b.forward * (size * 2.2f + 6f) + b.right * size * 0.8f + Vector3.up * (size * 0.7f + 2f);
                        Shot($"newtypes_{tag}_1{n}_{b.name}", from, look);
                        n++;
                    }
                    return 0.2;
                }
                default:
                    return -1;
            }
        }

        /// <summary>Wide chunk views (from high above, from the road looking out) and one junction + one end view per side road.</summary>
        private static double WideStep(int step, string tag)
        {
            // The start chunk (index 3), or the nearest built chunk with side roads
            Chunk c0 = ChunkAt(3);
            if (c0 == null || c0.sideRoads.Count == 0)
                c0 = Enumerable.Range(0, 6).Select(ChunkAt).FirstOrDefault(c => c != null && c.sideRoads.Count > 0) ?? c0 ?? ChunkAt(0);
            switch (step)
            {
                case 2:
                {
                    Write($"wide target: chunk {c0.ChunkIndex} {c0.TypeName}, terrain {c0.ChunkTerrain.terrainData.size}, side roads {c0.sideRoads.Count}");
                    Vector3 mid = RoadAt(c0, 250f);
                    Shot($"newtypes_{tag}_01_top", mid + new Vector3(0f, 1100f, -60f), mid);
                    Shot($"newtypes_{tag}_02_aerial", mid + new Vector3(-420f, 260f, -380f), mid + new Vector3(80f, 0f, 60f));
                    return 0.2;
                }
                case 3:
                {
                    Vector3 p = RoadAt(c0, 200f);
                    Vector3 right = c0.transform.right, fwd = c0.transform.forward;
                    Shot($"newtypes_{tag}_03_road", RoadAt(c0, 120f) + Vector3.up * 1.8f, RoadAt(c0, 240f) + Vector3.up * 1.2f);
                    Shot($"newtypes_{tag}_04_look_left", p - right * 4f + Vector3.up * 1.8f, p - right * 200f + fwd * 40f + Vector3.up * 4f);
                    Shot($"newtypes_{tag}_05_look_right", p + right * 4f + Vector3.up * 1.8f, p + right * 200f + fwd * 40f + Vector3.up * 4f);
                    return 0.2;
                }
                case 4:
                {
                    Transform ct = c0.transform;
                    float ty = c0.ChunkTerrain.transform.localPosition.y;
                    int n = 0;
                    foreach (var road in c0.sideRoads)
                    {
                        if (n >= 4) break;
                        Vector3 junction = RoadAt(c0, road.junctionZ);
                        Vector3 into = ct.TransformPoint(road.points[Mathf.Min(road.points.Count - 1, 6)] + Vector3.up * ty);
                        Vector3 back = ct.forward * -22f;
                        Shot($"newtypes_{tag}_1{n}_junction", junction + back - ct.right * road.side * 3f + Vector3.up * 2.2f, into + Vector3.up * 1f);
                        Vector3 end = ct.TransformPoint(road.End + Vector3.up * ty);
                        Vector3 dir = ct.TransformDirection(road.EndDirection);
                        Shot($"newtypes_{tag}_2{n}_end", end - dir * 45f + Vector3.up * 28f, end + dir * 6f);
                        Write($"side road {n}: style {road.style}, paved {road.paved}, z {road.junctionZ:0}, side {road.side}, length {road.Length:0}, end content {road.endContent}");
                        n++;
                    }
                    return 0.2;
                }
                default:
                    return -1;
            }
        }

        private static void SetupCamera()
        {
            var go = new GameObject("NewTypesCaptureCam");
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
            Write($"shot {name}");
        }
    }
}
