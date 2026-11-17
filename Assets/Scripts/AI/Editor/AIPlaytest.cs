using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using EndlessCombat.Combat;
using EndlessSurvival.Survival;
using EndlessSurvival.Testing;
using EndlessSurvival.Vehicle;

namespace EndlessCombat.AI.Editor
{
    /// <summary>
    /// Debug (Enemy_AI_Deep_Plan.md 27): plays Test_Savas and runs scripted enemy AI scenarios. The player is moved by
    /// the script (teleport, car entry), enemies are spawned through TestArena. Each scenario writes PASS / FAIL with
    /// its measurements to PoseShots/ai_log.txt, then the editor quits.
    /// Run the editor (not batchmode): -executeMethod EndlessCombat.AI.Editor.AIPlaytest.Run [-aiOnly name1,name2]
    /// or the menu Endless Survival > Debug > AI Senaryo Testi.
    /// </summary>
    [InitializeOnLoad]
    public static class AIPlaytest
    {
        private const string Flag = "EndlessCombat.AIPlaytest.Active";
        private static readonly StringWriter Log = new StringWriter();
        private static int _errors, _pass, _fail;
        private static bool _started;
        private static float _waitUntil;
        private static double _realStart;
        private static readonly Stack<IEnumerator> Stack = new Stack<IEnumerator>();
        private static Gamepad _pad;
        private static GamepadState _padState;
        private const double TimeoutSeconds = 900;

        static AIPlaytest()
        {
            if (!EditorPrefs.GetBool(Flag, false)) return;
            EditorApplication.update += Tick;
            Application.logMessageReceived += OnLog;
        }

        private static bool StealthScene
        {
            get
            {
                var args = System.Environment.GetCommandLineArgs();
                int i = System.Array.IndexOf(args, "-aiScene");
                return i >= 0 && i + 1 < args.Length && args[i + 1] == "gizlilik";
            }
        }

        [MenuItem("Endless Survival/Debug/AI Senaryo Testi")]
        public static void Run()
        {
            EditorPrefs.SetBool(Flag, true);
            EndlessSurvival.Testing.Editor.TestSceneBuilder.EnsureBuilt();
            // -aiScene gizlilik: the night camp scene (its own scenario only)
            string scene = StealthScene ? "Assets/Scenes/Test/Test_Gizlilik.unity" : EndlessSurvival.Testing.Editor.TestSceneBuilder.CombatScenePath;
            EditorSceneManager.OpenScene(scene, OpenSceneMode.Single);
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
            if (_errors <= 30) Log.WriteLine($"[{type}] {condition}\n    {string.Join(" | ", (stack ?? "").Split('\n').Take(3))}");
        }

        private static string OutDir => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "PoseShots"));

        private static void Finish()
        {
            Log.WriteLine($"\n=== TOPLAM: {_pass} GEÇTİ, {_fail} KALDI, hata/istisna {_errors}");
            EditorPrefs.DeleteKey(Flag);
            EditorApplication.update -= Tick;
            Application.logMessageReceived -= OnLog;
            if (_pad != null) InputSystem.RemoveDevice(_pad);
            Directory.CreateDirectory(OutDir);
            File.WriteAllText(Path.Combine(OutDir, "ai_log.txt"), Log.ToString());
            if (System.Environment.GetCommandLineArgs().Contains("-executeMethod")) EditorApplication.Exit(0);
            else EditorApplication.ExitPlaymode();
        }

        private static void Tick()
        {
            if (!EditorApplication.isPlaying) return;
            if (!_started)
            {
                _started = true;
                _realStart = EditorApplication.timeSinceStartup;
                Stack.Clear();
                Stack.Push(Main());
                _waitUntil = Time.time + 3f; // let the arena place everything
                return;
            }
            if (EditorApplication.timeSinceStartup - _realStart > TimeoutSeconds)
            {
                Log.WriteLine("ZAMAN AŞIMI");
                Finish();
                return;
            }
            if (Time.time < _waitUntil) return;
            try
            {
                while (Stack.Count > 0)
                {
                    var top = Stack.Peek();
                    if (!top.MoveNext())
                    {
                        Stack.Pop();
                        continue;
                    }
                    if (top.Current is IEnumerator nested) { Stack.Push(nested); continue; }
                    if (top.Current is float f) _waitUntil = Time.time + f;
                    else if (top.Current is int frames) _waitUntil = Time.time + frames * Time.deltaTime;
                    return;
                }
                Finish();
            }
            catch (System.Exception ex)
            {
                Log.WriteLine("İSTİSNA: " + ex);
                _fail++;
                Finish();
            }
        }

        // ------------------------------------------------------------------ helpers

        private static string[] _only;

        private static bool Wanted(string name)
        {
            if (_only == null)
            {
                var args = System.Environment.GetCommandLineArgs();
                int i = System.Array.IndexOf(args, "-aiOnly");
                _only = i >= 0 && i + 1 < args.Length ? args[i + 1].Split(',') : new string[0];
            }
            return _only.Length == 0 || _only.Contains(name);
        }

        private static void Result(string name, bool ok, string detail)
        {
            if (ok) _pass++; else _fail++;
            Log.WriteLine($"{(ok ? "GEÇTİ" : "KALDI")} {name}: {detail}");
        }

        private static GameObject Player => SurvivalDirector.Player;

        private static void HealPlayer()
        {
            var h = Player != null ? Player.GetComponent<EndlessSurvival.Inventory.PlayerHealth>() : null;
            if (h != null && !h.IsDead) h.Heal(9999f);
        }

        private static void TeleportPlayer(Vector3 p, float yaw = 0f)
        {
            var pl = Player;
            if (pl == null) return;
            if (SurvivalDirector.PlayerInVehicle) ExitCar();
            var cc = pl.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;
            pl.transform.SetPositionAndRotation(p, Quaternion.Euler(0f, yaw, 0f));
            if (cc != null) cc.enabled = true;
        }

        private static VehicleInteraction CarInteraction()
        {
            var v = SurvivalDirector.Vehicle;
            if (v == null) return null;
            var vi = v.GetComponentInChildren<VehicleInteraction>();
            return vi != null ? vi : v.GetComponentInParent<VehicleInteraction>();
        }

        private static void PlaceCar(Vector3 p, float yaw)
        {
            var car = SurvivalDirector.Vehicle;
            if (car == null) return;
            car.currentHealth = car.maxHealth;
            car.currentFuel = car.maxFuel;
            var rb = car.Rigidbody;
            var rot = Quaternion.Euler(0f, yaw, 0f);
            if (rb != null)
            {
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
                rb.position = p;
                rb.rotation = rot;
            }
            car.transform.SetPositionAndRotation(p, rot);
        }

        private static void EnterCar()
        {
            var vi = CarInteraction();
            if (vi == null || Player == null) return;
            typeof(VehicleInteraction).GetField("_currentPlayer", BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(vi, Player);
            vi.EnterVehicle();
            var car = SurvivalDirector.Vehicle;
            var f = typeof(VehicleController).GetField("engineOn", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (f != null && car != null) f.SetValue(car, true);
        }

        private static void ExitCar()
        {
            var vi = CarInteraction();
            if (vi != null && SurvivalDirector.PlayerInVehicle) vi.ExitVehicle();
        }

        private static void ClearEnemies()
        {
            foreach (var e in EnemyController.Active.ToArray())
                if (e != null) Object.Destroy(e.gameObject);
        }

        private static EnemyController Spawn(TestArena.Kind kind, Vector3 pos, EnemyController.EnemyState state, float yaw, EnemySquad squad)
        {
            var m = typeof(TestArena).GetMethod("SpawnEnemy", BindingFlags.NonPublic | BindingFlags.Instance);
            return (EnemyController)m.Invoke(TestArena.Instance, new object[] { kind, pos, state, yaw, squad });
        }

        private static float YawTo(Vector3 from, Vector3 to)
        {
            Vector3 d = to - from;
            d.y = 0f;
            return Quaternion.LookRotation(d.sqrMagnitude > 0.01f ? d : Vector3.forward).eulerAngles.y;
        }

        private static string Describe(EnemyController e)
        {
            if (e == null) return "(yok)";
            if (e.IsDead) return $"{e.DisplayName}: ölü";
            return $"{e.DisplayName}[{e.State}/{(e.Brain != null ? e.Brain.Choice.ToString() : "-")}] \"{e.StatusText}\" @{e.transform.position.x:0.0},{e.transform.position.z:0.0}";
        }

        private static void Gas(float v)
        {
            if (_pad == null)
            {
                _pad = InputSystem.AddDevice<Gamepad>();
                _padState = new GamepadState();
            }
            _padState.rightTrigger = v;
            InputSystem.QueueStateEvent(_pad, _padState);
            _pad.MakeCurrent();
        }

        // ------------------------------------------------------------------ scenarios

        private static IEnumerator Main()
        {
            Log.WriteLine($"AI senaryo testi {System.DateTime.Now:yyyy-MM-dd HH:mm}, sahne Test_Savas, zorluk {AIDifficulty.Current}");
            if (TestArena.Instance == null) { Result("kurulum", false, "TestArena yok"); yield break; }
            if (StealthScene)
            {
                yield return TargetCheck();
                yield return CampTest();
                yield break;
            }
            if (Wanted("target")) yield return TargetCheck();
            if (Wanted("carphysoff")) yield return CarPhysics(false);
            if (Wanted("carphys")) for (int i = 0; i < 3; i++) yield return CarPhysics(true); // the jolt was intermittent
            if (Wanted("pullout")) yield return PullOutTest();
            if (Wanted("plates")) yield return PlatesTest();
            // The empty car is evidence (they search when they see it): out of sight behind the west wall
            ExitCar();
            PlaceCar(new Vector3(-90f, 0.8f, 0f), 0f);
            yield return 1f;
            if (Wanted("separation")) yield return SeparationTest();
            if (Wanted("idle")) yield return IdleLifeTest();
            if (Wanted("notice")) yield return NoticeTest();
            if (Wanted("night")) yield return NightNoticeTest();
            if (Wanted("lost")) yield return LostTest();
            if (Wanted("grenade")) yield return GrenadeTest();
            if (Wanted("flee")) yield return FleeTest();
            if (Wanted("cover")) yield return CoverTest();
            if (Wanted("surrender")) yield return SurrenderTest();
            if (Wanted("shield")) yield return ShieldTest();
            if (Wanted("emptycar")) yield return EmptyCarTest();
            if (Wanted("seecar")) yield return SeeCarTest();
            if (Wanted("engine")) yield return EngineTest();
            if (Wanted("dodge")) yield return DodgeTest();
            if (Wanted("ammoout")) yield return AmmoOutCarTest();
            if (Wanted("target")) yield return TargetCheck();
        }

        private static List<string> _choices = new List<string>();

        private static void NoteChoice(EnemyController m)
        {
            if (m == null) return;
            string c = m.IsDead ? "ölü" : m.State + "/" + (m.Brain != null ? m.Brain.Choice.ToString() : "-");
            if (_choices.Count == 0 || _choices[_choices.Count - 1] != c) _choices.Add(c);
        }

        /// <summary>
        /// An unseen player behind a group throws a grenade 12 m: they guess the thrower's side (not the blast point)
        /// and do not pile up on one spot (EN SON 11).
        /// </summary>
        private static IEnumerator GrenadeTest()
        {
            string name = "bomba: kaynağı tahmin, yığılmama";
            Log.WriteLine($"\n--- {name}");
            ClearEnemies();
            Vector3 g = new Vector3(20f, 0f, -48f);
            Vector3 playerPos = new Vector3(20f, 0.2f, -66f); // behind the 3 m south wall (z = -60)
            TeleportPlayer(playerPos, 0f);
            var squad = EnemySquad.Create(SquadKind.Camp, g);
            var men = new List<EnemyController>();
            for (int i = 0; i < 4; i++) men.Add(Spawn(TestArena.Kind.Rifle, g + new Vector3(-3f + i * 2f, 0f, 0f), EnemyController.EnemyState.Idle, 0f, squad));
            yield return 2f;
            Vector3 origin = playerPos + Vector3.up * 1.5f;
            Vector3 blastAim = new Vector3(20f, 0f, -52f);
            float t = 1.6f; // a lob that clears the wall (apex about 3.9 m)
            Vector3 vel = new Vector3((blastAim.x - origin.x) / t, (blastAim.y - origin.y + 0.5f * 9.81f * t * t) / t, (blastAim.z - origin.z) / t);
            ThrownProjectile.Launch(ThrownProjectile.Kind.Grenade, origin, vel, Player.transform, Color.green);
            float t0 = Time.time;
            Vector3? blast = null;
            while (Time.time - t0 < 6f)
            {
                HealPlayer();
                if (blast == null && Object.FindAnyObjectByType<ThrownProjectile>() == null) blast = blastAim;
                yield return null;
            }
            // Where do they think he is: nearer his side than the blast point?
            int towardPlayer = 0, known = 0;
            var lines = new List<string>();
            foreach (var m in men)
            {
                if (m == null || m.IsDead) continue;
                var b = m.Belief.Of(AITarget.Player);
                if (b == null || !b.Valid) { lines.Add($"{m.DisplayName}: inanç yok"); continue; }
                known++;
                float dPlayer = Vector3.ProjectOnPlane(b.position - playerPos, Vector3.up).magnitude;
                float dBlast = Vector3.ProjectOnPlane(b.position - blastAim, Vector3.up).magnitude;
                if (dPlayer < dBlast) towardPlayer++;
                lines.Add($"{m.DisplayName}: inanç {b.position.x:0.0},{b.position.z:0.0} oyuncuya {dPlayer:0.0} m, patlamaya {dBlast:0.0} m, güven {b.confidence:0.00} ({b.source})");
            }
            Result(name + ": yön tahmini", known > 0 && towardPlayer * 2 >= known, $"{towardPlayer}/{known} kişi oyuncu tarafını tahmin etti; " + string.Join(" | ", lines));
            yield return 10f;
            float min = float.MaxValue;
            int alive = 0;
            for (int i = 0; i < men.Count; i++)
            {
                if (men[i] == null || men[i].IsDead) continue;
                alive++;
                for (int j = i + 1; j < men.Count; j++)
                    if (men[j] != null && !men[j].IsDead)
                        min = Mathf.Min(min, Vector3.ProjectOnPlane(men[i].transform.position - men[j].transform.position, Vector3.up).magnitude);
            }
            Result(name + ": yığılma", alive < 2 || min > 0.7f, $"16 sn sonra {alive} sağ, en yakın iki kişi {min:0.00} m; " + string.Join(" ; ", men.Select(Describe)));
            ClearEnemies();
            yield return 0.5f;
        }

        /// <summary>
        /// A timid man in panic with his back to the south wall and the player north of him: he runs along a real route
        /// (sideways), not into the wall (EN SON 7).
        /// </summary>
        private static IEnumerator FleeTest()
        {
            string name = "korkunca duvara değil yola kaçma";
            Log.WriteLine($"\n--- {name}");
            ClearEnemies();
            Vector3 e0 = new Vector3(40f, 0f, -56f);
            TeleportPlayer(new Vector3(40f, 0.2f, -46f), 180f);
            var squad = EnemySquad.Create(SquadKind.Raid, e0);
            var man = Spawn(TestArena.Kind.Bat, e0, EnemyController.EnemyState.Combat, 0f, squad);
            yield return 1f;
            if (man == null) { Result(name, false, "doğmadı"); yield break; }
            man.Nerve.courage = 0.1f;
            float t0 = Time.time, wallTime = 0f, fleeTime = 0f;
            Vector3 start = man.transform.position;
            _choices.Clear();
            while (Time.time - t0 < 8f)
            {
                HealPlayer();
                if (man == null || man.IsDead) break;
                man.Nerve.Set(100f);
                NoteChoice(man);
                if (man.StatusText.Contains("kaçıyor")) fleeTime += Time.deltaTime;
                if (man.transform.position.z < -58.6f) wallTime += Time.deltaTime; // pressed against the wall (z = -60)
                yield return null;
            }
            float moved = man != null ? Vector3.ProjectOnPlane(man.transform.position - start, Vector3.up).magnitude : 0f;
            float fromPlayer = man != null ? Vector3.ProjectOnPlane(man.transform.position - Player.transform.position, Vector3.up).magnitude : 0f;
            Result(name, fleeTime > 2f && moved > 8f && wallTime < 1.5f,
                $"kaçış süresi {fleeTime:0.0} sn, yol {moved:0.0} m, oyuncuya {fromPlayer:0.0} m, duvara yapışık {wallTime:0.0} sn; {string.Join(" > ", _choices.Take(8))}; {Describe(man)}");
            ClearEnemies();
            yield return 0.5f;
        }

        /// <summary>Four riflemen against the player standing in the open south of the cover field: cover, peek, flank.</summary>
        private static IEnumerator CoverTest()
        {
            string name = "siper, siperden ateş, yan dolanma";
            Log.WriteLine($"\n--- {name}");
            ClearEnemies();
            Vector3 p = new Vector3(0f, 0.2f, -2f);
            TeleportPlayer(p, 0f);
            var squad = EnemySquad.Create(SquadKind.Raid, new Vector3(0f, 0f, 38f));
            var men = new List<EnemyController>();
            for (int i = 0; i < 4; i++) men.Add(Spawn(TestArena.Kind.Rifle, new Vector3(-9f + i * 6f, 0f, 40f), EnemyController.EnemyState.Combat, 180f, squad));
            float t0 = Time.time;
            var everCover = new HashSet<EnemyController>();
            var seen = new HashSet<string>();
            float sharedSpot = 0f;
            while (Time.time - t0 < 25f)
            {
                HealPlayer();
                for (int i = 0; i < men.Count; i++)
                {
                    var m = men[i];
                    if (m == null || m.IsDead || m.Brain == null) continue;
                    if (m.Brain.InCover) everCover.Add(m);
                    seen.Add(m.Brain.Choice.ToString());
                    for (int j = i + 1; j < men.Count; j++)
                        if (men[j] != null && !men[j].IsDead && m.Brain.InCover && men[j].Brain.InCover
                            && Vector3.ProjectOnPlane(m.transform.position - men[j].transform.position, Vector3.up).magnitude < 0.6f)
                            sharedSpot += Time.deltaTime;
                }
                yield return null;
            }
            Result(name + ": sipere girdi", everCover.Count >= 2, $"{everCover.Count}/4 kişi sipere girdi; seçilenler: {string.Join(",", seen)}");
            Result(name + ": siperden ateş / yan dolanma", seen.Contains("PeekFire") || seen.Contains("Flank"), $"PeekFire={seen.Contains("PeekFire")}, Flank={seen.Contains("Flank")}");
            Result(name + ": aynı siper noktası", sharedSpot < 1f, $"iki kişinin aynı noktada durduğu süre {sharedSpot:0.0} sn; " + string.Join(" ; ", men.Select(Describe)));
            ClearEnemies();
            yield return 0.5f;
        }

        /// <summary>Alone, badly hurt, in panic, the player 8 m away in sight: he gives up.</summary>
        private static IEnumerator SurrenderTest()
        {
            string name = "teslim olma";
            Log.WriteLine($"\n--- {name}");
            ClearEnemies();
            Vector3 e0 = new Vector3(20f, 0f, -20f);
            TeleportPlayer(e0 + new Vector3(0f, 0.2f, 8f), 180f);
            var squad = EnemySquad.Create(SquadKind.Raid, e0);
            var man = Spawn(TestArena.Kind.Bat, e0, EnemyController.EnemyState.Combat, 0f, squad);
            yield return 1f;
            if (man == null) { Result(name, false, "doğmadı"); yield break; }
            man.TakeDamage(new DamageInfo(70f, DamageType.Bullet, man.transform.position + Vector3.up, Vector3.back) { ByPlayer = true, Source = Player });
            man.Nerve.courage = 0.1f;
            float t0 = Time.time, at = -1f;
            bool fleeNoted = false;
            _choices.Clear();
            while (Time.time - t0 < 8f)
            {
                HealPlayer();
                if (man == null || man.IsDead) break;
                man.Nerve.Set(100f);
                NoteChoice(man);
                if (man.Brain != null && man.Brain.Choice == EnemyBrain.Option.Flee && !fleeNoted)
                {
                    fleeNoted = true;
                    var own = man.Belief.Primary;
                    var best = squad.BestBelief(man);
                    Log.WriteLine($"  kaçış anı t={Time.time - t0:0.0}: sinir {man.Nerve.Level} panik {man.Nerve.Panic}, can {man.Health01:0.00}, " +
                                  $"kendi inanç {(own != null ? $"görüyor={own.SeenNow} güven {own.confidence:0.00}" : "yok")}, seçilen {(best == own ? "kendi" : "ortak")} görüyor={best?.SeenNow}, " +
                                  $"uzaklık {Vector3.Distance(man.transform.position, Player.transform.position):0.0}, savaşan dost {squad.AlliesFighting(man, 60f)}, lider {(squad.leader != null ? squad.leader.DisplayName : "-")}, üye {squad.Members.Count}");
                }
                if (man.Surrendered) { at = Time.time - t0; break; }
                yield return null;
            }
            Result(name, at >= 0f, $"{(at >= 0f ? at.ToString("0.0") + " sn" : "olmadı")}, can {(man != null ? man.Health01 : 0f):0.00}; {string.Join(" > ", _choices.Take(8))}; {Describe(man)}");
            ClearEnemies();
            yield return 0.5f;
        }

        /// <summary>EN SON 10: the shield man comes at the player and never walks away from him to front his mates.</summary>
        private static IEnumerator ShieldTest()
        {
            string name = "kalkanlı oyuncuya doğru gelir";
            Log.WriteLine($"\n--- {name}");
            ClearEnemies();
            Vector3 p = new Vector3(20f, 0.2f, -40f);
            TeleportPlayer(p, 0f);
            var squad = EnemySquad.Create(SquadKind.Raid, p + new Vector3(0f, 0f, 18f));
            var shield = Spawn(TestArena.Kind.ShieldMelee, p + new Vector3(0f, -0.2f, 16f), EnemyController.EnemyState.Combat, 180f, squad);
            Spawn(TestArena.Kind.Rifle, p + new Vector3(-4f, -0.2f, 24f), EnemyController.EnemyState.Combat, 180f, squad);
            Spawn(TestArena.Kind.Rifle, p + new Vector3(4f, -0.2f, 24f), EnemyController.EnemyState.Combat, 180f, squad);
            yield return 1f;
            float t0 = Time.time, d0 = -1f, minD = 99f, worstBack = 0f;
            bool fleeNoted = false;
            float startT = Time.time;
            int friendlyHits = 0;
            System.Action<EnemyController, DamageInfo> onHit = (e, info) =>
            {
                var shooter = !info.ByPlayer && info.Source != null ? info.Source.GetComponentInParent<EnemyController>() : null;
                if (shooter != null)
                {
                    friendlyHits++;
                    Log.WriteLine($"  dost isabeti t={Time.time - startT:0.0}: {shooter.DisplayName} -> {e.DisplayName}, {info.Amount:0} {info.Type}, uzaklık {Vector3.Distance(shooter.transform.position, e.transform.position):0.0} m, vurulan oyuncuya {Vector3.Distance(e.transform.position, Player.transform.position):0.0} m");
                }
                if (e != shield) return;
                Log.WriteLine($"  kalkanlıya vuruş t={Time.time - startT:0.0}: {info.Amount:0} {info.Type}, kaynak {(info.Source != null ? info.Source.name : "-")}, oyuncu={info.ByPlayer}, sinir {e.Nerve.Value:0}");
            };
            EnemyController.DebugDamaged += onHit;
            _choices.Clear();
            while (Time.time - t0 < 12f)
            {
                HealPlayer();
                if (shield == null || shield.IsDead) break;
                float d = Vector3.ProjectOnPlane(shield.transform.position - Player.transform.position, Vector3.up).magnitude;
                if (d0 < 0f) d0 = d;
                minD = Mathf.Min(minD, d);
                worstBack = Mathf.Max(worstBack, d - minD);
                NoteChoice(shield);
                if (shield.Brain != null && shield.Brain.Choice == EnemyBrain.Option.Flee && !fleeNoted)
                {
                    fleeNoted = true;
                    Log.WriteLine($"  kaçış anı t={Time.time - t0:0.0}: sinir {shield.Nerve.Value:0} ({shield.Nerve.Level}, panik {shield.Nerve.Panic}), can {shield.Health01:0.00}, takım güç {squad.Power:0.00} {squad.Stance} taktik {squad.Tactic}, " +
                                  string.Join(" ; ", squad.Members.Where(m => m != null).Select(Describe)));
                }
                yield return null;
            }
            EnemyController.DebugDamaged -= onHit;
            // 4 m: a melee charge ends with a step or two back before the next one (seen 3.3 m); the old bug was 48 m
            Result(name, minD < d0 - 6f && worstBack < 4f,
                $"başta {d0:0.0} m, en yakın {minD:0.0} m, en çok geri gidiş {worstBack:0.0} m; {string.Join(" > ", _choices.Take(10))}; {Describe(shield)}");
            Result("dost ateşi: tüfekçiler öndeki kalkanlıyı vurmaz", friendlyHits == 0, $"{friendlyHits} dost isabeti (12 sn)");
            ClearEnemies();
            yield return 0.5f;
        }

        /// <summary>A calm man 25 m from the driven car behind the arena wall: hears the engine, gets suspicious, goes to look.</summary>
        private static IEnumerator EngineTest()
        {
            string name = "motor sesine gelme (sakin, 25 m, duvar arkası)";
            Log.WriteLine($"\n--- {name}");
            ClearEnemies();
            Vector3 carPos = new Vector3(75f, 0.8f, 0f);
            PlaceCar(carPos, 0f);
            TeleportPlayer(carPos + new Vector3(-2.2f, -0.6f, 0f));
            yield return 1.5f;
            EnterCar();
            var squad = EnemySquad.Create(SquadKind.Camp, new Vector3(50f, 0f, 0f));
            var man = Spawn(TestArena.Kind.Rifle, new Vector3(50f, 0f, 0f), EnemyController.EnemyState.Idle, 270f, squad);
            float d0 = 25f, t0 = Time.time, susAt = -1f, minD = 99f;
            _choices.Clear();
            while (Time.time - t0 < 15f)
            {
                HealPlayer();
                if (man != null)
                {
                    NoteChoice(man);
                    minD = Mathf.Min(minD, Vector3.Distance(man.transform.position, carPos));
                    if (susAt < 0f && man.State != EnemyController.EnemyState.Idle) susAt = Time.time - t0;
                }
                yield return null;
            }
            Result(name, susAt > 0f && susAt < 6f && minD < d0 - 3f,
                $"tepki {(susAt > 0f ? susAt.ToString("0.0") + " sn" : "yok")}, araca en yakın {minD:0.0} m (başta {d0:0}); {string.Join(" > ", _choices.Take(12))}; son: {Describe(man)}");
            ExitCar();
            ClearEnemies();
            yield return 0.5f;
        }

        /// <summary>The car comes at 40 km/h straight at a fighting rifleman: he jumps aside (EvadeVehicle).</summary>
        private static IEnumerator DodgeTest()
        {
            string name = "hızlı araçtan kaçma";
            Log.WriteLine($"\n--- {name}");
            ClearEnemies();
            Vector3 carPos = new Vector3(25f, 0.8f, -50f);
            PlaceCar(carPos, 0f);
            TeleportPlayer(carPos + new Vector3(-2.2f, -0.6f, 0f));
            yield return 1.5f;
            EnterCar();
            var squad = EnemySquad.Create(SquadKind.Raid, new Vector3(25f, 0f, -20f));
            var man = Spawn(TestArena.Kind.Rifle, new Vector3(25f, 0f, -20f), EnemyController.EnemyState.Combat, 180f, squad);
            yield return 2f;
            var car = SurvivalDirector.Vehicle;
            float h0 = man != null ? man.Health01 : 0f, t0 = Time.time;
            bool evaded = false;
            _choices.Clear();
            while (Time.time - t0 < 4f)
            {
                HealPlayer();
                if (car.Rigidbody != null) car.Rigidbody.linearVelocity = new Vector3(0f, car.Rigidbody.linearVelocity.y, 11f);
                if (man != null && man.Brain != null && man.Brain.Choice == EnemyBrain.Option.EvadeVehicle) evaded = true;
                NoteChoice(man);
                yield return null;
            }
            if (car.Rigidbody != null) car.Rigidbody.linearVelocity = Vector3.zero;
            bool hit = man == null || man.IsDead || man.Health01 < h0 - 0.01f;
            Result(name, evaded && !hit, $"kaçma seçildi={evaded}, çarpıldı={hit}; {string.Join(" > ", _choices.Take(12))}; {Describe(man)}");
            ExitCar();
            ClearEnemies();
            yield return 0.5f;
        }

        /// <summary>A rifleman with no rounds left at a stopped, occupied car: goes and beats on it (no standing around).</summary>
        private static IEnumerator AmmoOutCarTest()
        {
            string name = "mermisi biten tüfekçi arabaya saldırır";
            Log.WriteLine($"\n--- {name}");
            ClearEnemies();
            Vector3 carPos = new Vector3(25f, 0.8f, -30f);
            PlaceCar(carPos, 0f);
            TeleportPlayer(carPos + new Vector3(-2.2f, -0.6f, 0f));
            yield return 1.5f;
            EnterCar();
            var squad = EnemySquad.Create(SquadKind.Raid, carPos);
            var man = Spawn(TestArena.Kind.Rifle, carPos + new Vector3(10f, -carPos.y, 4f), EnemyController.EnemyState.Combat, 240f, squad);
            yield return 1f;
            var w = man != null ? man.GetComponentInChildren<WeaponController>() : null;
            if (w != null)
            {
                typeof(WeaponController).GetField("currentAmmo", BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(w, 0);
                typeof(WeaponController).GetField("reserveAmmo", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public)?.SetValue(w, 0);
            }
            float t0 = Time.time, at = -1f;
            float savedPull = VehicleThreat.PullOutSeconds;
            VehicleThreat.PullOutSeconds = 9999f;
            _choices.Clear();
            while (Time.time - t0 < 15f)
            {
                HealPlayer();
                NoteChoice(man);
                if (at < 0f && man != null && man.Brain != null && man.Brain.Choice == EnemyBrain.Option.CarAssault) at = Time.time - t0;
                yield return null;
            }
            VehicleThreat.PullOutSeconds = savedPull;
            Result(name, at > 0f, $"silah={(w != null ? w.CurrentAmmo + "+" + w.ReserveAmmo : "yok")}, saldırı {(at > 0f ? at.ToString("0.0") + " sn" : "yok")}; {string.Join(" > ", _choices.Take(14))}; {Describe(man)}");
            ExitCar();
            ClearEnemies();
            yield return 0.5f;
        }

        private static IEnumerator TargetCheck()
        {
            yield return 0.2f;
            var t = AITarget.Player;
            bool ok = t != null && Player != null && t.gameObject == Player && t.kind == AITargetKind.Player;
            Result("AITarget.Player", ok, t == null ? "null" : $"nesne={t.gameObject.name}, tür={t.kind}, oyuncu={Player?.name}, araç={AITarget.PlayerVehicle?.gameObject.name}");
        }

        /// <summary>Task 1: four melee men beat on the stopped car with the driver in it; the car must not lift, tip or slide.</summary>
        private static IEnumerator CarPhysics(bool fixOn)
        {
            string name = fixOn ? "araç fizik (düzeltme açık)" : "araç fizik (düzeltme KAPALI, eski davranış)";
            Log.WriteLine($"\n--- {name}");
            CharacterBodyColliders.IgnoreVehicleContacts = fixOn;
            VehicleController.StuckHelpIgnoresPeople = fixOn;
            ClearEnemies();
            yield return 0.3f;
            Vector3 carPos = new Vector3(-8f, 0.8f, -12f);
            PlaceCar(carPos, 0f);
            TeleportPlayer(carPos + Vector3.left * 2.2f);
            yield return 1.5f;
            EnterCar();
            yield return 1f;
            var car = SurvivalDirector.Vehicle;
            if (car == null || !SurvivalDirector.PlayerInVehicle) { Result(name, false, "araca binilemedi"); yield break; }
            float savedPull = VehicleThreat.PullOutSeconds;
            VehicleThreat.PullOutSeconds = 9999f; // stay in the car for the whole measurement
            Vector3 c0 = car.transform.position;
            var squad = EnemySquad.Create(SquadKind.Raid, c0);
            var kinds = new[] { TestArena.Kind.Bat, TestArena.Kind.Knife, TestArena.Kind.Bat, TestArena.Kind.Knife, TestArena.Kind.Bat };
            var men = new List<EnemyController>();
            for (int i = 0; i < kinds.Length; i++)
            {
                float a = i * Mathf.PI * 2f / kinds.Length;
                Vector3 p = c0 + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 7f;
                p.y = 0f;
                men.Add(Spawn(kinds[i], p, EnemyController.EnemyState.Combat, YawTo(p, c0), squad));
            }
            if (car.GetComponent<VehicleContactProbe>() == null) car.gameObject.AddComponent<VehicleContactProbe>();
            VehicleContactProbe.Entries.Clear();
            bool dumped = false;
            float maxRise = 0f, maxTilt = 0f, maxSlide = 0f, maxSpeed = 0f, h0 = car.currentHealth;
            int assaultFrames = 0, frames = 0, maxAssault = 0;
            float t0 = Time.time;
            bool gas = false;
            while (Time.time - t0 < 26f)
            {
                HealPlayer();
                // Last 8 s: the driver floors it while they hold the car (stuck-help hop test)
                if (!gas && Time.time - t0 > 18f) { gas = true; Gas(1f); }
                if (gas) Gas(1f);
                var tr = car.transform;
                maxRise = Mathf.Max(maxRise, tr.position.y - c0.y);
                maxTilt = Mathf.Max(maxTilt, Vector3.Angle(tr.up, Vector3.up));
                if (!gas) maxSlide = Mathf.Max(maxSlide, Vector3.ProjectOnPlane(tr.position - c0, Vector3.up).magnitude);
                if (car.Rigidbody != null) maxSpeed = Mathf.Max(maxSpeed, car.Rigidbody.linearVelocity.y);
                // First jolt: who touched the car in the last half second, and where everybody stands (car space)
                if (!dumped && car.Rigidbody != null && (Mathf.Abs(car.Rigidbody.linearVelocity.y) > 1f || Vector3.Angle(tr.up, Vector3.up) > 6f))
                {
                    dumped = true;
                    Log.WriteLine($"  SARSINTI t={Time.time - t0:0.00} vy={car.Rigidbody.linearVelocity.y:0.00} açısal={car.Rigidbody.angularVelocity.magnitude:0.00} gaz={gas}");
                    foreach (var en in VehicleContactProbe.Entries.Where(en => Time.time - en.time < 0.6f).OrderByDescending(en => en.impulse).Take(12))
                        Log.WriteLine($"    temas {Time.time - en.time:0.00} sn önce: {en.other} itki {en.impulse:0} normal {en.normal} yerel nokta {en.localPoint} vy {en.velY:0.00}");
                    foreach (var m in men)
                        if (m != null)
                        {
                            var cc = m.GetComponent<CharacterController>();
                            Log.WriteLine($"    {m.DisplayName} araca göre {tr.InverseTransformPoint(m.transform.position)} ölü={m.IsDead} cc={(cc != null && cc.enabled)} {m.StatusText}");
                        }
                }
                int n = men.Count(m => m != null && !m.IsDead && m.Brain != null && m.Brain.Choice == EnemyBrain.Option.CarAssault);
                maxAssault = Mathf.Max(maxAssault, n);
                if (n > 0) assaultFrames++;
                frames++;
                if (frames % 120 == 0) Log.WriteLine($"  t={Time.time - t0:0.0} y+{tr.position.y - c0.y:0.00} eğim {Vector3.Angle(tr.up, Vector3.up):0.0} | " + string.Join(" ; ", men.Select(Describe)));
                yield return null;
            }
            Gas(0f);
            VehicleThreat.PullOutSeconds = savedPull;
            bool ok = maxRise < 0.25f && maxTilt < 8f && maxSlide < 0.3f;
            Result(name, fixOn ? ok : true,
                $"en çok yükselme {maxRise:0.00} m, en çok eğim {maxTilt:0.0}°, gaz öncesi kayma {maxSlide:0.00} m, en çok dikey hız {maxSpeed:0.00} m/s, " +
                $"aynı anda saldıran {maxAssault}, araç canı {h0:0}->{car.currentHealth:0}, saldırı kare oranı {(frames > 0 ? assaultFrames / (float)frames : 0f):0.00}" +
                (fixOn ? "" : " (yalnızca karşılaştırma için)"));
            if (fixOn && maxAssault == 0) Result("CarAssault seçimi", false, "hiç kimse arabaya saldırmadı");
            CharacterBodyColliders.IgnoreVehicleContacts = true;
            VehicleController.StuckHelpIgnoresPeople = true;
            ExitCar();
            ClearEnemies();
            yield return 0.5f;
        }

        private static IEnumerator PullOutTest()
        {
            string name = "şoför kapısından indirme";
            Log.WriteLine($"\n--- {name}");
            ClearEnemies();
            Vector3 carPos = new Vector3(-8f, 0.8f, -12f);
            PlaceCar(carPos, 0f);
            TeleportPlayer(carPos + Vector3.left * 2.2f);
            yield return 1.5f;
            EnterCar();
            yield return 1f;
            var car = SurvivalDirector.Vehicle;
            Vector3 c0 = car.transform.position;
            var squad = EnemySquad.Create(SquadKind.Raid, c0);
            var man = Spawn(TestArena.Kind.Bat, c0 + new Vector3(-8f, -c0.y, 0f), EnemyController.EnemyState.Combat, 90f, squad);
            float t0 = Time.time, outAt = -1f;
            while (Time.time - t0 < 25f)
            {
                HealPlayer();
                if (!SurvivalDirector.PlayerInVehicle) { outAt = Time.time - t0; break; }
                if (Mathf.FloorToInt((Time.time - t0) * 2f) != Mathf.FloorToInt((Time.time - t0 - Time.deltaTime) * 2f) && (int)((Time.time - t0) * 2f) % 6 == 0)
                    Log.WriteLine($"  t={Time.time - t0:0.0} {Describe(man)}");
                yield return null;
            }
            Result(name, outAt > 0f, outAt > 0f ? $"{outAt:0.0} sn'de indirildi" : "25 sn'de indiremedi: " + Describe(man));
            ExitCar();
            ClearEnemies();
            yield return 0.5f;
        }

        /// <summary>EN SON 16: sides and back plated with metal, glass windshield: no pull-out, the glass gives way.</summary>
        private static IEnumerator PlatesTest()
        {
            string name = "metal plaka: indiremez, ön cam kırılır";
            Log.WriteLine($"\n--- {name}");
            var parts = VehicleParts.Instance;
            if (parts == null) { Result(name, false, "VehicleParts yok"); yield break; }
            var saved = (WindowState[])parts.Windows.Clone();
            parts.Windows[(int)WindowSlot.Left] = WindowState.Metal;
            parts.Windows[(int)WindowSlot.Right] = WindowState.Metal;
            parts.Windows[(int)WindowSlot.Rear] = WindowState.Metal;
            parts.Windows[(int)WindowSlot.Front] = WindowState.Intact;
            ClearEnemies();
            Vector3 carPos = new Vector3(-8f, 0.8f, -12f);
            PlaceCar(carPos, 0f);
            TeleportPlayer(carPos + Vector3.left * 2.2f);
            yield return 1.5f;
            EnterCar();
            yield return 1f;
            Vector3 c0 = SurvivalDirector.Vehicle.transform.position;
            var squad = EnemySquad.Create(SquadKind.Raid, c0);
            var men = new List<EnemyController>();
            for (int i = 0; i < 3; i++)
                men.Add(Spawn(i == 1 ? TestArena.Kind.Knife : TestArena.Kind.Bat, c0 + new Vector3(-7f + i * 7f, -c0.y, 8f), EnemyController.EnemyState.Combat, 180f, squad));
            float t0 = Time.time, glassAt = -1f;
            bool pulled = false;
            while (Time.time - t0 < 25f)
            {
                HealPlayer();
                if (!SurvivalDirector.PlayerInVehicle) { pulled = true; break; }
                if (glassAt < 0f && parts.Windows[(int)WindowSlot.Front] == WindowState.Broken) glassAt = Time.time - t0;
                yield return null;
            }
            Result(name + ": indiremedi", !pulled, pulled ? $"{Time.time - t0:0.0} sn'de indirildi" : "25 sn araçta kaldı; " + string.Join(" ; ", men.Select(Describe)));
            Result(name + ": ön cam kırıldı", glassAt >= 0f, glassAt >= 0f ? $"{glassAt:0.0} sn" : $"kırılmadı, durum {parts.Windows[(int)WindowSlot.Front]}");
            for (int i = 0; i < saved.Length; i++) parts.Windows[i] = saved[i];
            ExitCar();
            ClearEnemies();
            yield return 0.5f;
        }

        /// <summary>EN SON 9: an idle man pushed 6 m off his place walks back; a patrol keeps walking.</summary>
        private static IEnumerator IdleLifeTest()
        {
            Log.WriteLine("\n--- boşta yaşam ve devriye");
            ClearEnemies();
            TeleportPlayer(new Vector3(0f, 0.2f, -45f), 180f);
            yield return 0.5f;
            Vector3 home = new Vector3(20f, 0f, 20f);
            var squad = EnemySquad.Create(SquadKind.Camp, home);
            var man = Spawn(TestArena.Kind.Rifle, home, EnemyController.EnemyState.Idle, 0f, squad);
            var squad2 = EnemySquad.Create(SquadKind.Patrol, new Vector3(-20f, 0f, 20f));
            var walker = Spawn(TestArena.Kind.Rifle, new Vector3(-20f, 0f, 20f), EnemyController.EnemyState.Patrolling, 90f, squad2);
            yield return 1.5f;
            var cc = man.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;
            man.transform.position = home + new Vector3(6f, 0f, 0f);
            if (cc != null) cc.enabled = true;
            float t0 = Time.time, backAt = -1f, walked = 0f;
            Vector3 last = walker != null ? walker.transform.position : Vector3.zero;
            while (Time.time - t0 < 20f)
            {
                if (backAt < 0f && man != null && Vector3.ProjectOnPlane(man.transform.position - home, Vector3.up).magnitude < 2.2f) backAt = Time.time - t0;
                if (walker != null)
                {
                    walked += Vector3.ProjectOnPlane(walker.transform.position - last, Vector3.up).magnitude;
                    last = walker.transform.position;
                }
                yield return null;
            }
            Result("yerinden çıkarılan geri döner", backAt >= 0f, backAt >= 0f ? $"{backAt:0.0} sn; {Describe(man)}" : "20 sn'de dönmedi: " + Describe(man));
            Result("devriye yürür", walked > 10f /* plan T0a: 30 m in 60 s */, $"20 sn'de {walked:0.0} m; {Describe(walker)}");
            ClearEnemies();
            yield return 0.5f;
        }

        /// <summary>
        /// Test_Gizlilik at 22:30 with the camp from the scene markers: 60 s with the player far off in the dark
        /// (patrols walk, nobody fights), then the player stands 5 m in front of the nearest man (noticed).
        /// </summary>
        private static IEnumerator CampTest()
        {
            Log.WriteLine("\n--- gizlilik kampı (gece)");
            var men = EnemyController.Active.Where(e => e != null && !e.IsDead).ToList();
            Log.WriteLine($"  sahnede {men.Count} düşman: " + string.Join(" ; ", men.Select(Describe)));
            Log.WriteLine($"  oyuncu @{Player.transform.position}, araç @{(SurvivalDirector.Vehicle != null ? SurvivalDirector.Vehicle.transform.position.ToString() : "-")}");
            foreach (string l in AIDebug.Recent) Log.WriteLine("  ai: " + l);
            if (men.Count == 0) { Result("kamp", false, "düşman yok"); yield break; }
            TeleportPlayer(new Vector3(0f, 0.2f, -8f), 0f);
            var walked = men.ToDictionary(m => m, m => 0f);
            var last = men.ToDictionary(m => m, m => m.transform.position);
            var startState = men.ToDictionary(m => m, m => m.State);
            bool fight = false;
            float t0 = Time.time;
            float probeAt = 0f;
            var fRoutine = typeof(EnemyController).GetField("_routineGoal", BindingFlags.NonPublic | BindingFlags.Instance);
            var fIndex = typeof(EnemyController).GetField("_patrolIndex", BindingFlags.NonPublic | BindingFlags.Instance);
            var fWait = typeof(EnemyController).GetField("_patrolWaitUntil", BindingFlags.NonPublic | BindingFlags.Instance);
            while (Time.time - t0 < 60f)
            {
                if (Time.time - t0 >= probeAt)
                {
                    probeAt += 10f;
                    foreach (var m in men.Where(m => m != null && startState[m] == EnemyController.EnemyState.Patrolling))
                        Log.WriteLine($"  t={Time.time - t0:0} {Describe(m)} faz {m.Squad?.Phase} rutin {fRoutine?.GetValue(m)} hedef#{fIndex?.GetValue(m)} bekle {((float)(fWait?.GetValue(m) ?? 0f)) - Time.time:0.0} yürüdü {walked[m]:0}");
                }
                foreach (var m in men)
                {
                    if (m == null || m.IsDead) continue;
                    walked[m] += Vector3.ProjectOnPlane(m.transform.position - last[m], Vector3.up).magnitude;
                    last[m] = m.transform.position;
                    if (m.State == EnemyController.EnemyState.Combat) fight = true;
                }
                yield return null;
            }
            var patrols = men.Where(m => m != null && startState[m] == EnemyController.EnemyState.Patrolling).ToList();
            Result("gece uzaktaki oyuncu fark edilmez", !fight, string.Join(" ; ", men.Select(Describe)));
            Result("devriyeler yürür (60 sn'de 30 m)", patrols.Count == 0 || patrols.All(m => walked[m] >= 30f),
                patrols.Count == 0 ? "devriye yok" : string.Join(" ; ", patrols.Select(m => $"{m.DisplayName} {walked[m]:0} m (yol {m.Mover.Route}, takılma {m.Mover.StuckCount}, nokta {m.patrolPoints.Count}: {string.Join(" ", m.patrolPoints.Select(p => $"{p.x:0},{p.z:0}"))})")));
            // Step in front of the nearest awake man
            var target = men.Where(m => m != null && !m.IsDead && m.State != EnemyController.EnemyState.Sitting)
                            .OrderBy(m => Vector3.Distance(m.transform.position, Player.transform.position)).FirstOrDefault();
            if (target == null) yield break;
            Vector3 front = target.transform.position + target.transform.forward * 5f;
            front.y = 0.2f;
            TeleportPlayer(front, target.transform.eulerAngles.y + 180f);
            t0 = Time.time;
            float at = -1f;
            float nextNote = 0f;
            while (Time.time - t0 < 10f)
            {
                HealPlayer();
                if (target == null) break;
                if (Time.time - t0 >= nextNote)
                {
                    nextNote += 1f;
                    Vector3 to = Player.transform.position - target.transform.position;
                    var b = AITarget.Player != null ? target.Belief.Of(AITarget.Player) : null;
                    Log.WriteLine($"  t={Time.time - t0:0} {Describe(target)} uzaklık {Vector3.ProjectOnPlane(to, Vector3.up).magnitude:0.0} m, bakış açısı {Vector3.Angle(Vector3.ProjectOnPlane(target.Senses.LookForward, Vector3.up), Vector3.ProjectOnPlane(to, Vector3.up)):0}, inanç {(b != null ? b.confidence : 0f):0.00}");
                }
                if (target.State == EnemyController.EnemyState.Combat || target.State == EnemyController.EnemyState.Suspicious) { at = Time.time - t0; break; }
                yield return null;
            }
            Result("gece 5 m önünde fark eder (şüphe ya da çatışma)", at >= 0f, at >= 0f ? $"{at:0.0} sn; {Describe(target)}" : "10 sn'de tepki yok: " + Describe(target));
        }

        private static IEnumerator SeparationTest()
        {
            string name = "birbirinin içine girme";
            Log.WriteLine($"\n--- {name}");
            ClearEnemies();
            TeleportPlayer(new Vector3(0f, 0.2f, -45f), 180f); // far away, facing away
            yield return 0.5f;
            var squad = EnemySquad.Create(SquadKind.Camp, new Vector3(20f, 0f, 20f));
            var men = new List<EnemyController>();
            for (int i = 0; i < 4; i++) men.Add(Spawn(TestArena.Kind.Rifle, new Vector3(20f + i * 0.05f, 0f, 20f), EnemyController.EnemyState.Idle, 0f, squad));
            yield return 5f;
            float min = float.MaxValue;
            for (int i = 0; i < men.Count; i++)
                for (int j = i + 1; j < men.Count; j++)
                    if (men[i] != null && men[j] != null)
                        min = Mathf.Min(min, Vector3.ProjectOnPlane(men[i].transform.position - men[j].transform.position, Vector3.up).magnitude);
            Result(name, min > 0.7f, $"5 sn sonra en yakın iki kişi arası {min:0.00} m; " + string.Join(" ; ", men.Select(Describe)));
            ClearEnemies();
            yield return 0.5f;
        }

        private static IEnumerator NoticeTest()
        {
            Log.WriteLine("\n--- fark etme");
            // a) Standing in front of him at 5 m in daylight
            ClearEnemies();
            Vector3 e0 = new Vector3(20f, 0f, -20f);
            TeleportPlayer(e0 + new Vector3(0f, 0.2f, 5f), 180f);
            yield return 0.3f;
            var squad = EnemySquad.Create(SquadKind.Camp, e0);
            var man = Spawn(TestArena.Kind.Rifle, e0, EnemyController.EnemyState.Idle, 0f, squad);
            float t0 = Time.time, at = -1f;
            while (Time.time - t0 < 8f)
            {
                if (man != null && man.State == EnemyController.EnemyState.Combat) { at = Time.time - t0; break; }
                yield return null;
            }
            Result("fark etme: önünde 5 m, gündüz, ayakta", at > 0f && at < 3.5f, at > 0f ? $"{at:0.0} sn" : "8 sn'de çatışmaya geçmedi: " + Describe(man));

            // b) Behind him at 4 m, standing still: should not be noticed by sight
            ClearEnemies();
            yield return 0.3f;
            TeleportPlayer(e0 + new Vector3(0f, 0.2f, -4f), 0f);
            squad = EnemySquad.Create(SquadKind.Camp, e0);
            man = Spawn(TestArena.Kind.Rifle, e0, EnemyController.EnemyState.Idle, 0f, squad);
            t0 = Time.time;
            float maxSus = 0f;
            bool combat = false;
            while (Time.time - t0 < 6f)
            {
                if (man == null) break;
                maxSus = Mathf.Max(maxSus, man.suspicionMeter);
                if (man.State == EnemyController.EnemyState.Combat) combat = true;
                yield return null;
            }
            Result("fark etme: arkasında 4 m, hareketsiz", !combat, $"en yüksek şüphe {maxSus:0}, çatışma={combat}; {Describe(man)}");

            // c) 20 m in front in daylight: slower than 5 m but noticed
            ClearEnemies();
            yield return 0.3f;
            TeleportPlayer(e0 + new Vector3(0f, 0.2f, 20f), 180f);
            squad = EnemySquad.Create(SquadKind.Camp, e0);
            man = Spawn(TestArena.Kind.Rifle, e0, EnemyController.EnemyState.Idle, 0f, squad);
            t0 = Time.time; at = -1f;
            float nextDiag = 0f;
            while (Time.time - t0 < 15f)
            {
                if (man != null && man.State == EnemyController.EnemyState.Combat) { at = Time.time - t0; break; }
                if (man != null && Time.time - t0 >= nextDiag)
                {
                    nextDiag += 1.5f;
                    bool can = man.Senses.CanSee(AITarget.Player, out float cl);
                    Vector3 eye = man.transform.position + Vector3.up * man.eyeHeight;
                    Vector3 ctr = AITarget.Player.Center;
                    string block = Physics.Linecast(eye, ctr, out RaycastHit lh, ~0, QueryTriggerInteraction.Ignore) ? $"{lh.collider.transform.root.name}/{lh.collider.name} @{lh.distance:0.0}" : "yok";
                    var ws = WeatherSystem.Instance;
                    var cyc = EndlessSurvival.World.DayNightCycle.Instance;
                    Log.WriteLine($"  ışın engeli={block} açıkHat={EnemyPerception.ClearLine(eye, ctr, man.transform, AITarget.Player.SightTransform)} hava={(ws != null ? ws.Current + " " + ws.Intensity.ToString("0.00") : "yok")} gece={(cyc != null && cyc.IsNight)} saat={(cyc != null ? cyc.currentHour : -1f):0.0} açı={Vector3.Angle(man.Senses.LookForward, ctr - eye):0}");
                    Log.WriteLine($"  t={Time.time - t0:0.0} görüyor={can} netlik={cl:0.00} SeesAny={man.Senses.SeesAny} şüphe={man.suspicionMeter:0.0} hazırlık={man.Actor?.Readiness} mesafe={Vector3.Distance(man.transform.position, Player.transform.position):0.0} oyuncu={Player.transform.position} hız={AITarget.Player.Velocity.magnitude:0.00}");
                }
                yield return null;
            }
            Result("fark etme: önünde 20 m, gündüz", at > 0f, at > 0f ? $"{at:0.0} sn" : "15 sn'de çatışmaya geçmedi: " + Describe(man));
            ClearEnemies();
            yield return 0.5f;
        }

        private static void SetHour(float h)
        {
            var cyc = EndlessSurvival.World.DayNightCycle.Instance;
            if (cyc == null) return;
            cyc.currentHour = h;
            cyc.UpdateLighting(true);
        }

        private static void Crouch(bool on)
        {
            var st = EndlessCombat.Combat.PlayerStealthController.Instance;
            if (st != null) st.SetCrouching(on);
        }

        /// <summary>One idle rifleman facing the player at a distance; seconds until combat (-1: not within the limit).</summary>
        private static IEnumerator TimeToNotice(float dist, float limit, float[] result, int index)
        {
            ClearEnemies();
            yield return 0.3f;
            Vector3 e0 = new Vector3(20f, 0f, -20f);
            TeleportPlayer(e0 + new Vector3(0f, 0.2f, dist), 180f);
            yield return 0.3f;
            var squad = EnemySquad.Create(SquadKind.Camp, e0);
            var man = Spawn(TestArena.Kind.Rifle, e0, EnemyController.EnemyState.Idle, 0f, squad);
            float t0 = Time.time, maxSus = 0f;
            result[index] = -1f;
            while (Time.time - t0 < limit)
            {
                if (man != null)
                {
                    maxSus = Mathf.Max(maxSus, man.suspicionMeter);
                    if (man.State == EnemyController.EnemyState.Combat) { result[index] = Time.time - t0; break; }
                }
                yield return null;
            }
            result[index + 4] = maxSus;
        }

        /// <summary>EN SON 8-9: darkness and crouching shorten sight and slow the meter.</summary>
        private static IEnumerator NightNoticeTest()
        {
            Log.WriteLine("\n--- gece ve çömelme ile fark etme");
            var r = new float[8];
            SetHour(22.5f);
            yield return 0.5f;
            yield return TimeToNotice(10f, 12f, r, 0);       // night, standing, 10 m
            Crouch(true);
            yield return 0.3f;
            yield return TimeToNotice(10f, 12f, r, 1);       // night, crouching, 10 m
            yield return TimeToNotice(4f, 12f, r, 2);        // night, crouching, 4 m
            SetHour(12f);
            yield return 0.5f;
            yield return TimeToNotice(8f, 15f, r, 3);        // day, crouching, 8 m (10 m is the edge of the crouch range)
            Crouch(false);
            string T(int i) => r[i] >= 0f ? $"{r[i]:0.0} sn" : $"yok (en çok şüphe {r[i + 4]:0})";
            Result("gece ayakta 10 m fark eder", r[0] >= 0f, T(0));
            Result("gece çömelik 10 m fark etmez", r[1] < 0f, T(1));
            Result("gece çömelik 4 m fark eder", r[2] >= 0f, T(2));
            Result("gündüz çömelik 8 m fark eder", r[3] >= 0f, T(3));
            ClearEnemies();
            yield return 0.5f;
        }

        /// <summary>Seen, then gone silently far away: hunt / search, never shooting at the new place, finally calm down.</summary>
        private static IEnumerator LostTest()
        {
            string name = "kaybetme ve arama";
            Log.WriteLine($"\n--- {name}");
            ClearEnemies();
            Vector3 e0 = new Vector3(20f, 0f, -20f);
            TeleportPlayer(e0 + new Vector3(0f, 0.2f, 10f), 180f);
            var squad = EnemySquad.Create(SquadKind.Camp, e0);
            var men = new List<EnemyController>
            {
                Spawn(TestArena.Kind.Rifle, e0, EnemyController.EnemyState.Idle, 0f, squad),
                Spawn(TestArena.Kind.Pistol, e0 + new Vector3(3f, 0f, 0f), EnemyController.EnemyState.Idle, 0f, squad),
            };
            float t0 = Time.time;
            while (Time.time - t0 < 8f && !men.Any(m => m != null && m.State == EnemyController.EnemyState.Combat)) { HealPlayer(); yield return null; }
            if (!men.Any(m => m != null && m.State == EnemyController.EnemyState.Combat)) { Result(name, false, "çatışma başlamadı"); yield break; }
            yield return 1.5f;
            HealPlayer();
            Vector3 seenAt = Player.transform.position;
            TeleportPlayer(new Vector3(-40f, 0.2f, -60f), 0f); // far, silent
            t0 = Time.time;
            var seq = new Dictionary<EnemyController, List<string>>();
            foreach (var m in men) seq[m] = new List<string>();
            float minDistToSeen = float.MaxValue;
            bool calm = false;
            float calmAt = -1f;
            while (Time.time - t0 < 120f)
            {
                HealPlayer();
                foreach (var m in men)
                {
                    if (m == null || m.IsDead) continue;
                    string s = m.State + ":" + m.StatusText;
                    var l = seq[m];
                    if (l.Count == 0 || l[l.Count - 1] != s) l.Add(s);
                    minDistToSeen = Mathf.Min(minDistToSeen, Vector3.ProjectOnPlane(m.transform.position - seenAt, Vector3.up).magnitude);
                }
                if (men.All(m => m == null || (m.State != EnemyController.EnemyState.Combat && m.State != EnemyController.EnemyState.Searching && m.State != EnemyController.EnemyState.Suspicious)))
                {
                    calm = true;
                    calmAt = Time.time - t0;
                    break;
                }
                yield return null;
            }
            foreach (var m in men) Log.WriteLine($"  {m?.DisplayName}: " + string.Join(" > ", seq[m].Take(25)));
            Result(name + ": son görülen yere gitti", minDistToSeen < 6f, $"son görülen yere en yakın {minDistToSeen:0.0} m");
            Result(name + ": arama bitti", calm, calm ? $"{calmAt:0} sn'de sakinleşti/tetikte" : "120 sn'de bitmedi: " + string.Join(" ; ", men.Select(Describe)));
            ClearEnemies();
            yield return 0.5f;
        }

        /// <summary>Empty parked car in plain sight: evidence, never a target.</summary>
        private static IEnumerator EmptyCarTest()
        {
            string name = "boş araca saldırmama";
            Log.WriteLine($"\n--- {name}");
            ClearEnemies();
            Vector3 carPos = new Vector3(20f, 0.8f, 0f);
            PlaceCar(carPos, 90f);
            TeleportPlayer(new Vector3(-40f, 0.2f, -60f), 0f);
            yield return 1f;
            var car = SurvivalDirector.Vehicle;
            float h0 = car.currentHealth;
            var squad = EnemySquad.Create(SquadKind.Camp, carPos + new Vector3(0f, 0f, 12f));
            var men = new List<EnemyController>
            {
                Spawn(TestArena.Kind.Rifle, carPos + new Vector3(-2f, -carPos.y, 12f), EnemyController.EnemyState.Idle, 180f, squad),
                Spawn(TestArena.Kind.Bat, carPos + new Vector3(2f, -carPos.y, 12f), EnemyController.EnemyState.Idle, 180f, squad),
            };
            float t0 = Time.time;
            bool combat = false, assault = false;
            while (Time.time - t0 < 20f)
            {
                foreach (var m in men)
                {
                    if (m == null) continue;
                    if (m.State == EnemyController.EnemyState.Combat) combat = true;
                    if (m.Brain != null && m.Brain.Choice == EnemyBrain.Option.CarAssault) assault = true;
                }
                yield return null;
            }
            Result(name, car.currentHealth >= h0 - 0.01f && !assault, $"araç canı {h0:0}->{car.currentHealth:0}, çatışma={combat}, arabaya saldırı={assault}; " + string.Join(" ; ", men.Select(Describe)));
            ClearEnemies();
            yield return 0.5f;
        }

        /// <summary>Driving into the sight of a camp that has not noticed anything: "Arabada!" and combat.</summary>
        private static IEnumerator SeeCarTest()
        {
            string name = "dolu aracı görünce çatışma";
            Log.WriteLine($"\n--- {name}");
            ClearEnemies();
            Vector3 carPos = new Vector3(20f, 0.8f, 0f);
            PlaceCar(carPos, 0f);
            TeleportPlayer(carPos + new Vector3(-2.2f, -0.6f, 0f));
            yield return 1.5f;
            EnterCar();
            yield return 0.5f;
            var squad = EnemySquad.Create(SquadKind.Camp, carPos + new Vector3(0f, 0f, 18f));
            var man = Spawn(TestArena.Kind.Rifle, carPos + new Vector3(0f, -carPos.y, 18f), EnemyController.EnemyState.Idle, 180f, squad);
            float t0 = Time.time, at = -1f;
            while (Time.time - t0 < 10f)
            {
                HealPlayer();
                if (man != null && man.State == EnemyController.EnemyState.Combat) { at = Time.time - t0; break; }
                yield return null;
            }
            Result(name, at > 0f && at < 4f, at > 0f ? $"{at:0.0} sn; {Describe(man)}" : "10 sn'de çatışma yok: " + Describe(man));
            ExitCar();
            ClearEnemies();
            yield return 0.5f;
        }
    }
}
