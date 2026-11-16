using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using EndlessSurvival.Inventory;
using EndlessSurvival.Survival;

namespace EndlessSurvival.Camping.Editor
{
    /// <summary>
    /// Debug: campfire → sit → stand → Tab screen. Logs what the mouse would hit (uGUI raycast) and IMGUI state
    /// to PoseShots/campfire_log.txt, to catch the "Tab screen stops taking clicks after the campfire" bug.
    /// Run the editor (not batchmode) with: -executeMethod EndlessSurvival.Camping.Editor.CampfireUiTest.Run
    /// </summary>
    [InitializeOnLoad]
    public static class CampfireUiTest
    {
        private const string Flag = "EndlessSurvival.CampfireUiTest.Active";
        private static double _t0 = -1, _stepAt;
        private static int _step;
        private static readonly StringWriter Log = new StringWriter();
        private static int _errors;
        private static PlayerCampfire _fire;

        static CampfireUiTest()
        {
            if (!EditorPrefs.GetBool(Flag, false)) return;
            EditorApplication.update += Tick;
            Application.logMessageReceived += OnLog;
        }

        [MenuItem("Endless Survival/Debug/Kamp Ateşi UI Testi")]
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
            File.WriteAllText(Path.Combine(OutDir, "campfire_log.txt"), Log.ToString());
            if (System.Environment.GetCommandLineArgs().Contains("-executeMethod")) EditorApplication.Exit(0);
            else EditorApplication.ExitPlaymode();
        }

        private static void Shot(string name)
        {
            Directory.CreateDirectory(OutDir);
            ScreenCapture.CaptureScreenshot(Path.Combine(OutDir, $"campfire_{name}.png"));
        }

        private static void Next(double wait)
        {
            _step++;
            _stepAt = EditorApplication.timeSinceStartup + wait;
        }

        private static void State(string tag)
        {
            var sit = SurvivalDirector.Player.GetComponent<PlayerSittingController>();
            Log.WriteLine($"[{tag}] hub={SurvivalPanel.IsHubOpen} invOpen={InventoryUI.IsOpen} campOpen={CampfireUI.IsOpen} resting={CampfireUI.IsResting} " +
                          $"sitting={(sit != null && sit.IsSitting)} anyOpen={SurvivalUIState.AnyOpen} cursor={Cursor.lockState}/{Cursor.visible} " +
                          $"hotControl={GUIUtility.hotControl} keyboardControl={GUIUtility.keyboardControl} timeScale={Time.timeScale}");
            var es = EventSystem.current;
            if (es == null) { Log.WriteLine("   no EventSystem"); return; }
            foreach (var p in new[] { new Vector2(Screen.width * 0.5f, Screen.height * 0.5f), new Vector2(Screen.width * 0.3f, Screen.height * 0.4f), new Vector2(Screen.width * 0.7f, Screen.height * 0.3f) })
            {
                var hits = new List<RaycastResult>();
                es.RaycastAll(new PointerEventData(es) { position = p }, hits);
                string list = string.Join(", ", hits.Take(4).Select(h => $"{h.gameObject.name}({h.gameObject.GetComponentInParent<Canvas>()?.rootCanvas.name})"));
                Log.WriteLine($"   ray {p}: {(hits.Count == 0 ? "nothing" : list)}");
            }
        }

        private static void Tick()
        {
            if (!EditorApplication.isPlaying) return;
            double now = EditorApplication.timeSinceStartup;
            if (_t0 < 0) { _t0 = now; _stepAt = now + 6.0; return; }
            if (now < _stepAt) return;
            if (now - _t0 > 200) { Log.WriteLine("timeout"); Finish(); return; }

            try
            {
                var player = SurvivalDirector.Player;
                var inv = SurvivalDirector.Inventory;
                switch (_step)
                {
                    case 0:
                    {
                        Object.FindAnyObjectByType<GameMenus>().CloseMenu();
                        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Camping/PlayerCampfire.prefab");
                        var go = Object.Instantiate(prefab, player.transform.position + player.transform.forward * 1.8f, Quaternion.identity);
                        _fire = go.GetComponent<PlayerCampfire>();
                        Next(1.0);
                        break;
                    }
                    case 1: State("before campfire"); _fire.Interact(inv); Next(1.0); break;
                    case 2: State("kamp tab open"); Shot("0_tab"); CampfireUI.Do(CampfireUI.CampAction.AddFuel, _fire, inv); Next(1.0); break;
                    case 3: CampfireUI.Do(CampfireUI.CampAction.Sit, _fire, inv); Next(2.0); break;
                    case 4: State("sat down"); Shot("1_sit"); Next(0.3); break;
                    case 5: _fire.Interact(inv); Next(1.0); break;
                    case 6: State("reopened while sitting"); SurvivalPanel.CloseHub(); Next(0.5); break;
                    case 7: player.GetComponent<PlayerSittingController>().StandUp(); Next(1.5); break;
                    case 8: State("stood up"); SurvivalPanel.OpenTab(HubTab.Inventory); Next(1.0); break;
                    case 9: State("inventory after campfire"); Shot("2_inventory"); SurvivalPanel.CloseHub(); Next(0.5); break;
                    case 10: _fire.Interact(inv); Next(0.6); break;
                    case 11: CampfireUI.Do(CampfireUI.CampAction.Rest, _fire, inv); Next(9.0); break;
                    case 12: State("after rest"); SurvivalPanel.OpenTab(HubTab.Inventory); Next(1.0); break;
                    case 13: State("inventory after rest"); Shot("3_inventory_rest"); Next(0.3); break;
                    case 14: Finish(); break;
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
