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
    /// Debug: plays Game.unity and checks the survival systems (menu, world content, craft, spoilage, fatigue,
    /// save extensions), writes PoseShots/survival_log.txt and screenshots. Then quits.
    /// Run the editor (not batchmode) with: -executeMethod EndlessSurvival.Survival.Editor.SurvivalSmokeTest.Run
    /// </summary>
    [InitializeOnLoad]
    public static class SurvivalSmokeTest
    {
        private const string Flag = "EndlessSurvival.SurvivalSmokeTest.Active";
        private static double _t0 = -1, _stepAt;
        private static int _step;
        private static readonly StringWriter Log = new StringWriter();
        private static int _errors;
        private static int _bandagesBefore;

        static SurvivalSmokeTest()
        {
            if (!EditorPrefs.GetBool(Flag, false)) return;
            EditorApplication.update += Tick;
            Application.logMessageReceived += OnLog;
        }

        [MenuItem("Endless Survival/Debug/Survival Smoke Test")]
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
            File.WriteAllText(Path.Combine(OutDir, "survival_log.txt"), Log.ToString());
            if (System.Environment.GetCommandLineArgs().Contains("-executeMethod")) EditorApplication.Exit(0);
            else EditorApplication.ExitPlaymode();
        }

        private static void Shot(string name)
        {
            Directory.CreateDirectory(OutDir);
            ScreenCapture.CaptureScreenshot(Path.Combine(OutDir, $"survival_{name}.png"));
        }

        private static void Next(double wait)
        {
            _step++;
            _stepAt = EditorApplication.timeSinceStartup + wait;
        }

        private static void HubShot(HubTab tab)
        {
            bool ok = SurvivalPanel.OpenTab(tab);
            var inv = InventoryUI.Instance;
            bool invPanel = inv != null && inv.inventoryPanel != null && inv.inventoryPanel.activeSelf;
            Log.WriteLine($"hub tab {tab}: ok={ok} open={SurvivalPanel.IsHubOpen} showing={SurvivalPanel.Showing(tab)} inventoryPanelVisible={invPanel} campfireIsOpen={EndlessSurvival.Camping.CampfireUI.IsOpen}");
            Shot("hub_" + tab);
            Next(1.0);
        }

        private static ItemDefinition It(string n) => SurvivalSettings.Instance.Item(n);

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
            if (now - _t0 > 240) { Log.WriteLine("timeout"); Finish(); return; }

            try
            {
                var director = SurvivalDirector.Instance;
                var inv = SurvivalDirector.Inventory;
                switch (_step)
                {
                    case 0:
                        Log.WriteLine($"director: {(director != null)}  player: {(SurvivalDirector.Player != null)}  menuOpen: {SurvivalUIState.MenuOpen}  timeScale: {Time.timeScale}");
                        Shot("0_mainmenu");
                        Next(1.0);
                        break;
                    case 1:
                        var menus = Object.FindAnyObjectByType<GameMenus>();
                        if (menus != null) menus.CloseMenu();
                        Log.WriteLine($"menu closed: menuOpen={SurvivalUIState.MenuOpen} timeScale={Time.timeScale}");
                        Next(10.0);
                        break;
                    case 2:
                    {
                        var chunks = ChunkManager.Instance != null ? ChunkManager.Instance.ActiveChunks.Count : -1;
                        Log.WriteLine($"chunks: {chunks}");
                        Log.WriteLine($"resource nodes: {Object.FindObjectsByType<ResourceNode>().Length}  " +
                                      $"(trees {Object.FindObjectsByType<ResourceNode>().Count(n => n.kind == ResourceKind.DeadTree)})");
                        Log.WriteLine($"animals: {AnimalController.All.Count} [{string.Join(",", AnimalController.All.Select(a => a.kind.ToString()).Distinct())}]");
                        Log.WriteLine($"wreck salvage: {Object.FindObjectsByType<WreckSalvage>().Length}  pumps: {Object.FindObjectsByType<FuelPump>().Length}  traders: {Object.FindObjectsByType<Trader>().Length}");
                        Log.WriteLine($"buildings: {GameObject.FindObjectsByType<Transform>().Count(t => t.name.StartsWith("POI_GasStation") || t.name.StartsWith("POI_AbandonedHouse"))}");
                        Log.WriteLine($"recipes: {SurvivalSettings.Instance.recipes.Length}  settings items: {SurvivalSettings.Instance.items.Length}");
                        var v = PlayerVitals.Instance;
                        if (v != null)
                            Log.WriteLine($"vitals: stamina {v.Stamina:0}/{v.MaxStamina:0} fatigue {v.Fatigue:0.0} body {v.BodyHeat:0} felt {v.FeltTemperature:0.0}C ambient {v.AmbientTemperature:0.0}C stress {v.Stress:0.0} carry comfort {v.ComfortWeight:0.0}");
                        var w = WeatherSystem.Instance;
                        Log.WriteLine($"weather: {(w != null ? w.Current + " " + w.Intensity.ToString("0.00") : "none")}");
                        var stats = CharacterStats.Instance;
                        if (stats != null) Log.WriteLine("stats: " + string.Join(" | ", System.Enum.GetValues(typeof(StatType)).Cast<StatType>().Select(s => stats.Describe(s))));
                        Shot("1_hud");

                        // craft: bandage from cloth
                        inv.AddItem(It("Item_Cloth"), 4);
                        _bandagesBefore = inv.Storage.GetCount(It("Item_Bandage"));
                        var bandage = SurvivalSettings.Instance.recipes.First(r => r.name == "Recipe_Bandage");
                        bool can = CraftingSystem.Instance.CanCraft(bandage, out string reason);
                        Log.WriteLine($"craft bandage can={can} reason={reason}");
                        CraftingSystem.Instance.StartCraft(bandage);
                        Next(3.0);
                        break;
                    }
                    case 3:
                    {
                        int after = inv.Storage.GetCount(It("Item_Bandage"));
                        Log.WriteLine($"bandages {_bandagesBefore} -> {after}, cloth left {inv.Storage.GetCount(It("Item_Cloth"))}");
                        var campfire = SurvivalSettings.Instance.recipes.First(r => r.name == "Recipe_CookMeat");
                        CraftingSystem.Instance.CanCraft(campfire, out string reason);
                        Log.WriteLine($"cook meat away from fire: {reason}");

                        // spoilage: raw meat ages 36h -> rotten
                        inv.AddItem(It("Item_RawMeat"), 2);
                        Log.WriteLine($"raw meat added: {inv.Storage.GetCount(It("Item_RawMeat"))}, fatigue {PlayerVitals.Instance.Fatigue:0.0}");
                        Next(1.0);
                        break;
                    }
                    case 4:
                    case 5:
                    case 6:
                    case 7:
                    case 8:
                    case 9:
                    case 10:
                        DayNightCycle.Instance.AdvanceHours(6f);
                        Next(0.8);
                        break;
                    case 11:
                    {
                        Log.WriteLine($"after 42h: raw {inv.Storage.GetCount(It("Item_RawMeat"))} rotten {inv.Storage.GetCount(It("Item_RottenFood"))} fatigue {PlayerVitals.Instance.Fatigue:0.0} totalHours {GameClock.TotalHours:0.0}");
                        Log.WriteLine($"vitals: body {PlayerVitals.Instance.BodyHeat:0} felt {PlayerVitals.Instance.FeltTemperature:0.0} stamina cap {PlayerVitals.Instance.MaxStamina:0} weather {WeatherSystem.Instance.Current}");
                        // fill bottle near water? report water under player
                        var p = SurvivalDirector.Player.transform.position;
                        Log.WriteLine($"water near player: {WaterBodies.Near(p, 2.2f)}");

                        // save: extensions
                        var save = EndlessSurvival.Save.SaveManager.Instance;
                        bool saved = save != null && save.SaveGame();
                        string file = Path.Combine(Application.persistentDataPath, "savegame.json");
                        var files = Directory.GetFiles(Application.persistentDataPath, "*.json");
                        string text = files.Length > 0 ? File.ReadAllText(files.OrderByDescending(File.GetLastWriteTime).First()) : "";
                        Log.WriteLine($"save ok={saved} survival keys: {string.Join(",", new[] { "survival.clock", "survival.stats", "survival.vitals", "survival.spoilage", "survival.vehiclehome" }.Where(k => text.Contains(k)))}");

                        // spawn a deer and a wolf next to the player and watch them
                        var chunk = SurvivalDirector.ChunkAt(p);
                        AnimalController.Spawn(AnimalKind.Deer, p + SurvivalDirector.Player.transform.forward * 12f, chunk != null ? chunk.transform : null, 0, 0f);
                        AnimalController.Spawn(AnimalKind.Wolf, p - SurvivalDirector.Player.transform.forward * 15f, chunk != null ? chunk.transform : null, 77, 0f);
                        Next(3.0);
                        break;
                    }
                    case 12:
                    {
                        var p = SurvivalDirector.Player.transform.position;
                        foreach (var a in AnimalController.All.Where(a => (a.transform.position - p).sqrMagnitude < 40f * 40f))
                            Log.WriteLine($"near animal {a.kind} dist {Vector3.Distance(a.transform.position, p):0.0} hunting {a.IsHunting}");
                        var health = SurvivalDirector.Player.GetComponent<PlayerHealth>();
                        Log.WriteLine($"player health {health.CurrentHealth:0} stress {PlayerVitals.Instance.Stress:0.0}");
                        Shot("2_animals");
                        Next(2.0);
                        break;
                    }
                    case 13: HubShot(HubTab.Inventory); break;
                    case 14: HubShot(HubTab.Craft); break;
                    case 15: HubShot(HubTab.Camp); break;
                    case 16: HubShot(HubTab.Vehicle); break;
                    case 17: HubShot(HubTab.Status); break;
                    case 18:
                        SurvivalPanel.CloseHub();
                        Log.WriteLine($"hub closed: open={SurvivalPanel.IsHubOpen} inventoryOpen={InventoryUI.IsOpen}");
                        Log.WriteLine($"notifications: {string.Join(" / ", SurvivalNotify.Entries.Select(e => e.text))}");
                        Finish();
                        break;
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
