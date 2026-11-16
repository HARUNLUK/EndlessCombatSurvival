using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using EndlessSurvival.Inventory;
using EndlessSurvival.World;

namespace EndlessSurvival.Survival.Editor
{
    /// <summary>
    /// Debug: plays Game.unity and saves screenshots of the menus and HUD to PoseShots/ui_*.png (main menu, controls,
    /// HUD, tip box, hub tabs, pause, settings), plus PoseShots/ui_log.txt with errors. Then quits.
    /// Run the editor (not batchmode) with: -executeMethod EndlessSurvival.Survival.Editor.UiCapture.Run
    /// </summary>
    [InitializeOnLoad]
    public static class UiCapture
    {
        private const string Flag = "EndlessSurvival.UiCapture.Active";
        private static double _t0 = -1, _stepAt;
        private static int _step;
        private static readonly StringWriter Log = new StringWriter();
        private static int _errors;
        private static float _hpAtBleed;

        static UiCapture()
        {
            if (!EditorPrefs.GetBool(Flag, false)) return;
            EditorApplication.update += Tick;
            Application.logMessageReceived += OnLog;
        }

        [MenuItem("Endless Survival/Debug/UI Ekran Görüntüleri")]
        public static void Run()
        {
            EditorPrefs.SetBool(Flag, true);
            EditorSceneManager.OpenScene("Assets/Scenes/Game.unity", OpenSceneMode.Single);
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
            Application.logMessageReceived -= OnLog;
            Application.logMessageReceived += OnLog;
            EditorApplication.EnterPlaymode();
        }

        private static void OnLog(string condition, string stack, LogType type)
        {
            if (type != LogType.Exception && type != LogType.Error) return;
            _errors++;
            if (_errors <= 25) Log.WriteLine($"[{type}] {condition}\n    {stack?.Split('\n').FirstOrDefault()}");
        }

        private static string OutDir => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "PoseShots"));

        private static void Finish()
        {
            Log.WriteLine($"errors/exceptions logged: {_errors}");
            EditorPrefs.DeleteKey(Flag);
            EditorApplication.update -= Tick;
            Application.logMessageReceived -= OnLog;
            Directory.CreateDirectory(OutDir);
            File.WriteAllText(Path.Combine(OutDir, "ui_log.txt"), Log.ToString());
            if (System.Environment.GetCommandLineArgs().Contains("-executeMethod")) EditorApplication.Exit(0);
            else EditorApplication.ExitPlaymode();
        }

        private static void Shot(string name)
        {
            Directory.CreateDirectory(OutDir);
            ScreenCapture.CaptureScreenshot(Path.Combine(OutDir, $"ui_{name}.png"));
            Log.WriteLine("shot " + name);
        }

        private static void Next(double wait)
        {
            _step++;
            _stepAt = EditorApplication.timeSinceStartup + wait;
        }

        private static GameMenus Menus => Object.FindAnyObjectByType<GameMenus>();

        private static void Tick()
        {
            if (!EditorApplication.isPlaying) return;
            double now = EditorApplication.timeSinceStartup;
            if (_t0 < 0)
            {
                _t0 = now;
                _stepAt = now + 6.0;
                return;
            }
            if (now < _stepAt) return;
            if (now - _t0 > 300) { Log.WriteLine("timeout"); Finish(); return; }

            try
            {
                switch (_step)
                {
                    case 0: Shot("0_mainmenu"); Next(0.6); break;
                    case 1: Menus.DebugOpenControls(); Next(0.6); break;
                    case 2: Shot("1_controls"); Next(0.3); break;
                    case 3: Menus.CloseMenu(); Next(4.0); break;
                    case 4: Shot("2_hud_tip"); Next(0.3); break;
                    case 5:
                    {
                        // Push vitals so the warning colours and more tips show
                        var p = SurvivalDirector.Player;
                        var s = p.GetComponent<PlayerSurvival>();
                        s.SetValues(15f, 30f, 95f);
                        var h = p.GetComponent<PlayerHealth>();
                        h.SetHealthAndBleed(22f, true);
                        Next(2.5);
                        break;
                    }
                    case 6: Shot("3_hud_low"); Next(0.3); break;
                    case 7: SurvivalPanel.OpenTab(HubTab.Inventory); Next(1.0); break;
                    case 8: Shot("4_inventory"); Next(0.3); break;
                    case 9: SurvivalPanel.OpenTab(HubTab.Vehicle); Next(1.0); break;
                    case 10: Shot("5_vehicle"); Next(0.3); break;
                    case 11: SurvivalPanel.OpenTab(HubTab.Status); Next(1.0); break;
                    case 12: Shot("6_status"); Next(0.3); break;
                    case 13: SurvivalPanel.CloseHub(); Menus.DebugOpenPause(); Next(0.6); break;
                    case 14: Shot("7_pause"); Next(0.3); break;
                    case 15: Menus.DebugOpenSettings(); Next(0.6); break;
                    case 16: Shot("8_settings"); Next(0.3); break;
                    case 17:
                    {
                        Menus.CloseMenu();
                        var p = SurvivalDirector.Player;
                        var inv = SurvivalDirector.Inventory;
                        var s = SurvivalSettings.Instance;
                        Log.WriteLine($"starter kit: bandage {inv.Storage.GetCount(s.Item("Item_Bandage"))} water {inv.Storage.GetCount(s.Item("Item_WaterBottle"))} beans {inv.Storage.GetCount(s.Item("Item_CannedBeans"))}");
                        Log.WriteLine($"names: {s.Item("Item_Bandage").DisplayName} / {s.Item("Item_Pistol").DisplayName} / {s.Item("Item_ScrapMetal").DisplayName}");
                        var h = p.GetComponent<PlayerHealth>();
                        h.SetHealthAndBleed(100f, false);
                        h.StartBleeding();
                        _hpAtBleed = h.CurrentHealth;
                        WeatherSystem.Instance.DebugForce(WeatherType.Rain);
                        Next(4.0);
                        break;
                    }
                    case 18:
                    {
                        Shot("9_rain");
                        foreach (var ps in Object.FindObjectsByType<ParticleSystem>())
                        {
                            if (ps.name != "Rain") continue;
                            var r = ps.GetComponent<ParticleSystemRenderer>();
                            var cam = Camera.main;
                            Log.WriteLine($"rain ps: playing={ps.isPlaying} count={ps.particleCount} rate={ps.emission.rateOverTime.constant} pos={ps.transform.position} fwd={ps.transform.forward} layer={LayerMask.LayerToName(ps.gameObject.layer)} shader={(r.sharedMaterial != null ? r.sharedMaterial.shader.name : "none")} camPos={cam.transform.position} camMask={cam.cullingMask}");
                        }
                        Next(36.0);
                        break;
                    }
                    case 19:
                    {
                        var h = SurvivalDirector.Player.GetComponent<PlayerHealth>();
                        Log.WriteLine($"bleed after ~40s: bleeding={h.isBleeding} hp {_hpAtBleed:0} -> {h.CurrentHealth:0}");
                        h.DamageIgnoringArmor(9999f, "Test");
                        Next(2.0);
                        break;
                    }
                    case 20: Shot("10_gameover"); Log.WriteLine($"gameover: {GameOverUI.IsGameOver}"); Next(0.3); break;
                    case 21:
                        Object.FindAnyObjectByType<GameOverUI>().RestartGame();
                        Next(8.0);
                        break;
                    case 22:
                        Log.WriteLine($"after new run: menuOpen={SurvivalUIState.MenuOpen} gameOver={GameOverUI.IsGameOver} timeScale={Time.timeScale} seed={EndlessSurvival.World.ChunkManager.Instance?.masterSeed}");
                        Shot("11_newrun");
                        Next(0.3);
                        break;
                    case 23:
                        Object.FindAnyObjectByType<EndlessSurvival.Vehicle.VehicleInteraction>().EnterVehicle();
                        WeatherSystem.Instance.DebugForce(WeatherType.Rain);
                        Next(4.0);
                        break;
                    case 24:
                    {
                        Shot("12_driving_rain");
                        foreach (var ps in Object.FindObjectsByType<ParticleSystem>())
                            if (ps.name == "Rain") Log.WriteLine($"rain particles while driving: {ps.particleCount}");
                        Next(0.3);
                        break;
                    }
                    case 25: WeatherSystem.Instance.DebugForce(WeatherType.Snow); Next(6.0); break;
                    case 26: Shot("13_snow"); Next(0.3); break;
                    case 27: Finish(); break;
                }
            }
            catch (System.Exception e)
            {
                Log.WriteLine($"step {_step} exception: {e}");
                Finish();
            }
        }
    }
}
