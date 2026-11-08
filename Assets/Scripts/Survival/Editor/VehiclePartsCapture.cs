using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using EndlessSurvival.Vehicle;
using EndlessSurvival.World;

namespace EndlessSurvival.Survival.Editor
{
    /// <summary>
    /// Debug: Paket 6 check in play mode. Installs the new mods for free, covers the windows, changes a flat tyre,
    /// tries a cold start and a warm start, turns the headlights on at night, and saves pictures to
    /// PoseShots/vehicle_*.png with a log (vehicle_log.txt). Then quits.
    /// Run the editor (not batchmode) with: -executeMethod EndlessSurvival.Survival.Editor.VehiclePartsCapture.Run
    /// </summary>
    [InitializeOnLoad]
    public static class VehiclePartsCapture
    {
        private const string Flag = "EndlessSurvival.VehiclePartsCapture.Active";
        private const int W = 1280, H = 720;

        private static double _t0 = -1, _stepAt;
        private static int _step;
        private static Camera _cam;
        private static RenderTexture _rt;
        private static readonly StringWriter Log = new StringWriter();
        private static int _errors;

        static VehiclePartsCapture()
        {
            if (!EditorPrefs.GetBool(Flag, false)) return;
            EditorApplication.update += Tick;
            Application.logMessageReceived += OnLog;
        }

        [MenuItem("Endless Survival/Debug/Araç Paketi 6 Ekran Görüntüleri")]
        public static void Run()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/Game.unity", OpenSceneMode.Single);
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
            File.WriteAllText(Path.Combine(OutDir, "vehicle_log.txt"), Log.ToString());
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
                _stepAt = now + 5.0;
                Directory.CreateDirectory(OutDir);
                foreach (var f in Directory.GetFiles(OutDir, "vehicle_*.png")) File.Delete(f);
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

        private static VehicleController Car => SurvivalDirector.Vehicle;
        private static VehicleParts Parts => VehicleParts.Instance;
        private static GameObject PlayerGo => SurvivalDirector.Player;

        private static void PutPlayerBeside(float side = -2.6f)
        {
            var p = PlayerGo;
            if (p == null || Car == null) return;
            var cc = p.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;
            p.transform.position = Car.transform.position + Car.transform.right * side + Vector3.up * 0.3f;
            Physics.SyncTransforms();
            if (cc != null) cc.enabled = true;
        }

        private static void Give(string item, int n) => SurvivalDirector.GiveItem(SurvivalSettings.Instance.Item(item), n);

        private static string State()
        {
            var p = Parts;
            return $"engineOn={Car.engineOn} running={p.EngineRunning} battery={p.BatteryCharge:0}/{p.BatteryTemp:0}C heat={p.EngineHeat:0} rad={p.Radiator:0} " +
                   $"tires=[{string.Join(",", p.Tires.Select(t => t.ToString("0")))}] spare={p.SpareFilled} cabin=+{p.CabinBonus:0.0} shelter={p.ShelterFactor:0.00} " +
                   $"windows=[{string.Join(",", p.Windows)}] lamps={p.HeadlightsOn} grip=[{string.Join(",", Car.wheelGripFactor.Select(g => g.ToString("0.00")))}] cap={Car.partsTopSpeedKmh}";
        }

        private static double DoStep(int step)
        {
            switch (step)
            {
                case 0:
                {
                    var menus = Object.FindAnyObjectByType<GameMenus>();
                    if (menus != null) menus.CloseMenu();
                    DayNightCycle.Instance?.SetTime(12f);
                    return 2.0;
                }
                case 1:
                {
                    if (Car == null || Parts == null || PlayerGo == null) { Log.WriteLine("missing: car/parts/player " + (Car != null) + (Parts != null) + (PlayerGo != null)); return -1; }
                    SetupCamera();
                    foreach (var r in Car.GetComponentsInChildren<Renderer>())
                    {
                        if (r.transform.GetComponentsInParent<Transform>().Any(p => p.name == "VisualModifications" || p.name.StartsWith("Package6"))) continue;
                        var mf = r.GetComponent<MeshFilter>();
                        string sub = mf != null && mf.sharedMesh != null ? $" mesh={mf.sharedMesh.name} subs={mf.sharedMesh.subMeshCount}" : "";
                        Vector3 c = Car.transform.InverseTransformPoint(r.bounds.center);
                        Vector3 s = Car.transform.InverseTransformVector(r.bounds.size);
                        Log.WriteLine($"renderer {r.name} center={c:F2} size={s:F2}{sub} mats={string.Join("|", r.sharedMaterials.Select(m => m != null ? m.name : "null"))}");
                    }
                    var mods = Car.GetComponent<VehicleModifications>();
                    Log.WriteLine("mods in list: " + string.Join(", ", mods.modifications.Select(m => m.type)));
                    foreach (var t in new[] { VehicleModType.Bullbar, VehicleModType.RadiatorGrille, VehicleModType.DoorPlates, VehicleModType.SpareTireMount,
                                 VehicleModType.TowRope, VehicleModType.SnowChains, VehicleModType.CabinInsulation, VehicleModType.BatteryInsulation })
                        Log.WriteLine($"install {t}: {mods.InstallModification(t, freeCost: true)}");
                    Give("Item_CarTire", 2);
                    Log.WriteLine("fill spare: " + Parts.FillSpare());
                    Parts.Windows[(int)WindowSlot.Front] = WindowState.Plank;
                    Parts.Windows[(int)WindowSlot.Left] = WindowState.Plastic;
                    Parts.Windows[(int)WindowSlot.Right] = WindowState.Metal;
                    Parts.Windows[(int)WindowSlot.Rear] = WindowState.Plank;
                    Parts.Windows[(int)WindowSlot.Roof] = WindowState.Plank;
                    Log.WriteLine("state: " + State());
                    Log.WriteLine($"shelter={Parts.ShelterFactor:0.00} rainEnters={Parts.RainEnters} windshield: {Parts.WindshieldInfo}");
                    var lampRoot = Car.transform.Find("Package6_Headlights");
                    if (lampRoot != null)
                        foreach (Transform l in lampRoot) Log.WriteLine($"lamp {l.name} local={l.localPosition:F2}");
                    PutPlayerBeside(-6f);
                    return 0.5;
                }
                case 2:
                {
                    Vector3 c = Car.transform.position + Vector3.up * 0.9f;
                    Transform t = Car.transform;
                    Shot("vehicle_01_front_left", c + t.forward * 5.5f - t.right * 4f + Vector3.up * 1.5f, c);
                    Shot("vehicle_02_rear_right", c - t.forward * 5.5f + t.right * 4f + Vector3.up * 1.8f, c);
                    Shot("vehicle_03_left", c - t.right * 6f + Vector3.up * 0.8f, c);
                    Shot("vehicle_04_right", c + t.right * 6f + Vector3.up * 0.8f, c);
                    Shot("vehicle_05_top", c + Vector3.up * 8f - t.forward * 0.5f, c);
                    return 0.2;
                }
                case 3:
                {
                    // Flat tyre: grip and speed cap, then change it with the spare
                    Parts.Tires[0] = 0f;
                    Give("Item_ToolKit", 1);
                    PutPlayerBeside();
                    return 0.3;
                }
                case 4:
                {
                    Log.WriteLine("flat FL: " + State());
                    Log.WriteLine("change tyre: " + Parts.ChangeTire(0));
                    return 12.0; // job (14 s scaled by the mechanics stat)
                }
                case 5:
                {
                    Log.WriteLine($"job still running: {Parts.CurrentJob != null} progress={Parts.JobProgress:0.00}");
                    return Parts.CurrentJob != null ? 4.0 : 0.1;
                }
                case 6:
                {
                    Log.WriteLine("after change: " + State());
                    // Cold start: weak, frozen battery
                    Parts.DebugSetBattery(40f, -12f);
                    float ch = Parts.StartChance(out string why);
                    Log.WriteLine($"cold start chance {ch:0.00} ({why})");
                    PutPlayerBeside(-2.2f);
                    return 0.6;
                }
                case 7:
                {
                    var inter = Car.GetComponent<VehicleInteraction>();
                    inter.EnterVehicle();
                    Log.WriteLine("entered (auto start attempt): " + State());
                    return 0.5;
                }
                case 8:
                {
                    int tries = 0;
                    while (!Car.engineOn && tries < 6) { Parts.TryStart(false); tries++; }
                    Log.WriteLine($"cold tries {tries}: " + State());
                    Parts.DebugSetBattery(90f, 15f);
                    if (!Car.engineOn) Log.WriteLine("warm start: " + Parts.TryStart(false));
                    Log.WriteLine("warm: " + State());
                    return 6.0; // let the cabin warm a little
                }
                case 9:
                {
                    var v = PlayerVitals.Instance;
                    Log.WriteLine($"in cabin after 6 s: {State()} felt={v?.FeltTemperature:0.0} ambient={v?.AmbientTemperature:0.0}");
                    // Overheat with a broken radiator
                    Parts.DebugSetEngine(0f, 98f);
                    return 4.0;
                }
                case 10:
                {
                    Log.WriteLine($"broken radiator: overheated={Parts.Overheated} " + State());
                    DayNightCycle.Instance?.SetTime(22.5f);
                    Parts.DebugSetEngine(100f, 60f);
                    Parts.TryStart(false);
                    Parts.SetHeadlights(true);
                    return 1.0;
                }
                case 11:
                {
                    Transform t = Car.transform;
                    Vector3 c = t.position + Vector3.up * 1.2f;
                    Shot("vehicle_06_night_headlights", c - t.forward * 4.5f + t.right * 2.5f + Vector3.up * 1.6f, c + t.forward * 18f);
                    Shot("vehicle_07_night_front", c + t.forward * 12f + Vector3.up * 0.5f, c);
                    Log.WriteLine("night: " + State());
                    var lr = Car.transform.Find("Package6_Headlights");
                    if (lr != null)
                        foreach (var l in lr.GetComponentsInChildren<Light>(true))
                            Log.WriteLine($"lamp {l.name} active={l.isActiveAndEnabled} int={l.intensity:0.0} range={l.range} pos={l.transform.position:F1} fwd={l.transform.forward:F2} mask={l.cullingMask} layer={l.renderingLayerMask}");
                    // Reference: a plain point light above the road, to see whether extra lights render in this camera at all
                    var refGo = new GameObject("CaptureRefLight");
                    refGo.transform.position = t.position + t.forward * 8f + Vector3.up * 2f;
                    var rl = refGo.AddComponent<Light>();
                    rl.type = LightType.Point; rl.range = 12f; rl.intensity = 40f; rl.color = Color.red;
                    Shot("vehicle_08_night_reflight", c - t.forward * 4.5f + t.right * 2.5f + Vector3.up * 1.6f, c + t.forward * 18f);
                    Log.WriteLine("lights in scene: " + string.Join(", ", Object.FindObjectsByType<Light>(FindObjectsSortMode.None).Where(x => x.isActiveAndEnabled).Select(x => $"{x.name}:{x.type}")));
                    return 0.2;
                }
                default:
                    return -1;
            }
        }

        private static void SetupCamera()
        {
            var go = new GameObject("VehicleCaptureCam");
            _cam = go.AddComponent<Camera>();
            if (Camera.main != null) _cam.CopyFrom(Camera.main);
            _cam.fieldOfView = 50f;
            _cam.nearClipPlane = 0.05f;
            _cam.farClipPlane = 1500f;
            _cam.enabled = false;
            _rt = new RenderTexture(W, H, 24);
            _cam.targetTexture = _rt;
        }

        private static void Shot(string name, Vector3 from, Vector3 at)
        {
            _cam.transform.position = from;
            _cam.transform.LookAt(at);
            _cam.Render();
            RenderTexture.active = _rt;
            var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            File.WriteAllBytes(Path.Combine(OutDir, name + ".png"), tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            Log.WriteLine("shot " + name);
        }
    }
}
