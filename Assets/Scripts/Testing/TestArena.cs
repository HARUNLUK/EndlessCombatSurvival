using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using EndlessCombat.AI;
using EndlessSurvival.Survival;
using EndlessSurvival.World;
using EndlessSurvival.Inventory;

namespace EndlessSurvival.Testing
{
    /// <summary>
    /// Test scenes (Assets/Scenes/Test): a copy of Game.unity with this component on one object. The endless world
    /// (ChunkManager, WorldPopulator) is switched off, nothing is saved, the main menu is skipped, test mode
    /// (SurvivalTest.FreeResources) is on for the session, and a small arena is built at the origin:
    ///   Combat   cover boxes and walls; spawn soldiers, melee fighters, shield bearers, wolves, a bear
    ///   Stealth  a walled camp with patrols and guards at night; switch day / night, rebuild the camp
    ///   Driving  a long road, slalom cones, ramps, bumps, a crate pile to ram and a hilly off-road terrain
    /// The buttons sit on the tuning panel (L3 + R3 / F1), page "Test". Built by the menu
    /// Endless Survival > Test Sahneleri > Kur / Yenile (TestSceneBuilder).
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    public class TestArena : MonoBehaviour
    {
        public enum Mode { Combat, Stealth, Driving }

        public Mode mode = Mode.Combat;

        /// <summary>True while a test scene runs: no saving, no main menu, no endless world.</summary>
        public static bool Active { get; private set; }
        public static TestArena Instance { get; private set; }

        private Transform _root;      // built once
        private Transform _spawned;   // enemies / animals / cones, cleared by the buttons
        private Material _matGround, _matCover, _matWall, _matRoad, _matCone, _matRamp;
        private static bool s_registered;

        private static readonly Vector3 PlayerStart = new Vector3(0f, 0.2f, 0f);
        private static readonly Vector3 VehicleStart = new Vector3(4f, 0.8f, 2f);

        private void Awake()
        {
            Instance = this;
            Active = true;
            SurvivalTest.ForceForSession(true);

            var chunks = FindAnyObjectByType<ChunkManager>();
            if (chunks != null) chunks.enabled = false;
            var populator = FindAnyObjectByType<WorldPopulator>();
            if (populator != null) populator.enabled = false;
        }

        private void OnDestroy()
        {
            if (Instance != this) return;
            Instance = null;
            Active = false;
        }

        private IEnumerator Start()
        {
            // SurvivalDirector adds these after the scene loads (after Awake): roadside spawns, night wolves, zone briefings
            foreach (var p in FindObjectsByType<WorldPopulator>(FindObjectsSortMode.None)) p.enabled = false;
            foreach (var j in FindObjectsByType<Journey>(FindObjectsSortMode.None)) j.enabled = false;

            // The arena is built in the editor (TestSceneBuilder) and saved in the scene, child "Arena". An old scene
            // without it is built here at runtime as before.
            _root = transform.Find(ArenaName);
            if (_root == null) BuildWorld();
            else MakeMaterials(); // cones / loose crates spawned at runtime use them
            _spawned = new GameObject("TestArena_Spawned").transform;
            if (mode == Mode.Driving) SpawnCones();
            RegisterTuning();

            // Other systems find the player and the vehicle in their own Start; place them a few frames later
            yield return null;
            yield return null;
            var cycle = DayNightCycle.Instance;
            if (cycle != null)
            {
                cycle.timeSpeedMultiplier = 0f;
                SetHour(mode == Mode.Stealth ? 22.5f : 12f);
            }
            // No chunks here: the weather took the fixed seed with the clock stopped and was full fog for good, which
            // halved every enemy's sight (17 Nov AI playtest). Test scenes run in clear weather, held: setting the hour
            // starts a new weather segment at once, and in the stealth scene it then rained (guards sheltered at home).
            if (WeatherSystem.Instance != null) WeatherSystem.Instance.DebugForce(WeatherType.Clear, true);
            ResetPlayer();
            ResetVehicle();
            // NPCs from the spawn markers placed in the scene (hierarchy: TestArena > NPCs > squad > NPC)
            if (!SpawnFromMarkers() && mode == Mode.Stealth) SpawnCamp();
            SurvivalNotify.Show(Title + ": test düğmeleri ayar panelinde (L3 + R3 / F1), \"Test\" sayfası. Burada kayıt yapılmaz.", new Color(0.6f, 0.9f, 1f), 8f);
        }

        private string Title => mode == Mode.Combat ? "Savaş testi" : mode == Mode.Stealth ? "Gizlilik testi" : "Sürüş testi";

        public const string ArenaName = "Arena";
        public const string NpcRootName = "NPCs";

        /// <summary>Builds the arena geometry under the child "Arena" (edit mode by TestSceneBuilder, or at runtime).</summary>
        public void BuildWorld()
        {
            var old = transform.Find(ArenaName);
            if (old != null) DestroyImmediate(old.gameObject);
            _root = new GameObject(ArenaName).transform;
            _root.SetParent(transform, false);
            MakeMaterials();
            BuildGround();
            switch (mode)
            {
                case Mode.Combat: BuildCombat(); break;
                case Mode.Stealth: BuildStealth(); break;
                case Mode.Driving: BuildDriving(); break;
            }
        }

        /// <summary>
        /// Default NPC spawn markers (edit mode, TestSceneBuilder). They stay in the scene: move, add or delete them in
        /// the hierarchy; at Play each squad group becomes a real squad of the new AI.
        /// </summary>
        public void BuildNpcMarkers()
        {
            var old = transform.Find(NpcRootName);
            if (old != null) DestroyImmediate(old.gameObject);
            var root = new GameObject(NpcRootName).transform;
            root.SetParent(transform, false);
            if (mode == Mode.Combat)
            {
                var patrol = Group(root, "Takım - devriye (görmemiş)", SquadKind.Patrol, EnemyController.EnemyState.Patrolling);
                Marker(patrol, Kind.Rifle, new Vector3(-6f, 0f, 30f), 180f);
                Marker(patrol, Kind.Pistol, new Vector3(-2f, 0f, 32f), 180f);
                Marker(patrol, Kind.Bat, new Vector3(2f, 0f, 31f), 180f);
                Marker(patrol, Kind.Rifle, new Vector3(6f, 0f, 33f), 180f);

                var camp = Group(root, "Takım - kamp (oturuyor / nöbette)", SquadKind.Camp, EnemyController.EnemyState.Idle);
                Marker(camp, Kind.Rifle, new Vector3(-14f, 0f, 50f), 180f);
                Marker(camp, Kind.Knife, new Vector3(-10f, 0f, 52f), 160f, EnemyController.EnemyState.Sitting);
                Marker(camp, Kind.ShieldPistol, new Vector3(10f, 0f, 50f), 200f);
                Marker(camp, Kind.Pistol, new Vector3(14f, 0f, 53f), 200f, EnemyController.EnemyState.Sitting);
                Marker(camp, Kind.ShieldMelee, new Vector3(0f, 0f, 54f), 180f);
            }
            else if (mode == Mode.Stealth)
            {
                Vector3 c = new Vector3(0f, 0f, 45f);
                var camp = Group(root, "Takım - kamp", SquadKind.Camp, EnemyController.EnemyState.Idle);
                Marker(camp, Kind.Rifle, c + new Vector3(0f, 0f, -12f), 0f, EnemyController.EnemyState.Patrolling);
                Marker(camp, Kind.Pistol, c + new Vector3(10f, 0f, 0f), 270f, EnemyController.EnemyState.Patrolling);
                Marker(camp, Kind.Knife, c + new Vector3(-10f, 0f, 0f), 90f, EnemyController.EnemyState.Patrolling);
                Marker(camp, Kind.Rifle, c + new Vector3(-2f, 0f, -16.5f), 180f);
                Marker(camp, Kind.Bat, c + new Vector3(6f, 0f, 4f), 200f, EnemyController.EnemyState.Sitting);
                Marker(camp, Kind.Pistol, c + new Vector3(-6f, 0f, 9f), 160f);
            }
        }

        private static TestSquadSpawn Group(Transform root, string name, SquadKind kind, EnemyController.EnemyState state)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root, false);
            var g = go.AddComponent<TestSquadSpawn>();
            g.squadKind = kind;
            g.state = state;
            return g;
        }

        private static void Marker(TestSquadSpawn group, Kind kind, Vector3 position, float yaw, EnemyController.EnemyState? state = null)
        {
            var go = new GameObject("NPC - " + kind);
            go.transform.SetParent(group.transform, false);
            go.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
            var m = go.AddComponent<TestNpcSpawn>();
            m.kind = kind;
            m.useSquadState = !state.HasValue;
            if (state.HasValue) m.state = state.Value;
        }

        /// <summary>Spawns every enabled squad group of the scene's markers. False when the scene has none.</summary>
        private bool SpawnFromMarkers()
        {
            var groups = GetComponentsInChildren<TestSquadSpawn>(false);
            if (groups.Length == 0) return false;
            foreach (var g in groups)
            {
                if (!g.spawnOnStart) continue;
                var markers = g.GetComponentsInChildren<TestNpcSpawn>(false);
                if (markers.Length == 0) continue;
                Vector3 home = Vector3.zero;
                foreach (var m in markers) home += m.transform.position;
                home /= markers.Length;
                var squad = EnemySquad.Create(g.squadKind, home);
                foreach (var m in markers)
                {
                    var state = m.useSquadState ? g.state : m.state;
                    SpawnEnemy(m.kind, m.transform.position, state, m.transform.eulerAngles.y, squad);
                }
            }
            return true;
        }

        private void RespawnMarkers()
        {
            ClearSpawned();
            if (!SpawnFromMarkers()) SurvivalNotify.Warn("Bu sahnede NPC işareti yok (Endless Survival > Test Sahneleri > Kur - Yenile)");
        }

        // ================================================================== building blocks

        private void MakeMaterials()
        {
            _matGround = Mat(new Color(0.42f, 0.45f, 0.38f));
            _matCover = Mat(new Color(0.55f, 0.42f, 0.28f));
            _matWall = Mat(new Color(0.5f, 0.5f, 0.52f));
            _matRoad = Mat(new Color(0.18f, 0.18f, 0.19f));
            _matCone = Mat(new Color(0.95f, 0.45f, 0.1f));
            _matRamp = Mat(new Color(0.65f, 0.6f, 0.3f));
        }

        private static Material Mat(Color c)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            var m = new Material(shader);
            m.color = c;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            return m;
        }

        private GameObject Box(string name, Vector3 center, Vector3 size, Material mat, float yaw = 0f, float pitch = 0f, Transform parent = null)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent != null ? parent : _root, false);
            go.transform.position = center;
            go.transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
            go.transform.localScale = size;
            go.GetComponent<Renderer>().sharedMaterial = mat;
            go.isStatic = parent == null;
            return go;
        }

        private void BuildGround()
        {
            float size = mode == Mode.Driving ? 1400f : 240f;
            Box("Ground", new Vector3(0f, -0.5f, mode == Mode.Driving ? 500f : 0f), new Vector3(size, 1f, size), _matGround);
        }

        // ================================================================== combat

        private void BuildCombat()
        {
            // Arena border
            float r = 60f;
            Box("Wall_N", new Vector3(0f, 1.5f, r), new Vector3(2f * r, 3f, 0.6f), _matWall);
            Box("Wall_S", new Vector3(0f, 1.5f, -r), new Vector3(2f * r, 3f, 0.6f), _matWall);
            Box("Wall_E", new Vector3(r, 1.5f, 0f), new Vector3(0.6f, 3f, 2f * r), _matWall);
            Box("Wall_W", new Vector3(-r, 1.5f, 0f), new Vector3(0.6f, 3f, 2f * r), _matWall);

            // Cover field north of the start: crates, low walls, tall walls, pillars
            var rng = new System.Random(7);
            for (int row = 0; row < 4; row++)
            {
                for (int col = -3; col <= 3; col++)
                {
                    float z = 12f + row * 10f + (float)rng.NextDouble() * 3f;
                    float x = col * 8f + (float)rng.NextDouble() * 3f - 1.5f;
                    int kind = (row * 7 + col + 21) % 4;
                    switch (kind)
                    {
                        case 0: Box("Crate", new Vector3(x, 0.6f, z), new Vector3(1.2f, 1.2f, 1.2f), _matCover, rng.Next(0, 90)); break;
                        case 1: Box("LowWall", new Vector3(x, 0.55f, z), new Vector3(3.2f, 1.1f, 0.4f), _matCover, rng.Next(-20, 20)); break;
                        case 2: Box("TallWall", new Vector3(x, 1.5f, z), new Vector3(4f, 3f, 0.5f), _matWall, rng.Next(-30, 30)); break;
                        default: Box("Pillar", new Vector3(x, 1.5f, z), new Vector3(0.9f, 3f, 0.9f), _matWall); break;
                    }
                }
            }
            // Near cover for the player
            Box("PlayerCover_L", new Vector3(-3f, 0.55f, 5f), new Vector3(3f, 1.1f, 0.4f), _matCover);
            Box("PlayerCover_R", new Vector3(3.5f, 0.6f, 5.5f), new Vector3(1.2f, 1.2f, 1.2f), _matCover);
        }

        public enum Kind { Rifle, Pistol, Knife, Bat, ShieldMelee, ShieldPistol }

        private EnemyController SpawnEnemy(Kind kind, Vector3 position, EnemyController.EnemyState state, float yaw, EnemySquad squad = null)
        {
            GameObject prefab = PrefabFor(kind);
            if (prefab == null)
            {
                SurvivalNotify.Warn("Düşman prefab'ı bulunamadı (SurvivalSettings > humanEnemyPrefabs)");
                return null;
            }
            var go = Instantiate(prefab, position + Vector3.up * 0.1f, Quaternion.Euler(0f, yaw, 0f), _spawned);
            var enemy = go.GetComponent<EnemyController>();
            if (enemy == null) return null;
            enemy.initialState = state == EnemyController.EnemyState.Combat ? EnemyController.EnemyState.Idle : state;
            if (kind == Kind.ShieldMelee || kind == Kind.ShieldPistol)
                enemy.heldShieldItem = SurvivalSettings.Instance.Item(kind == Kind.ShieldPistol ? "Item_RiotShield" : "Item_WoodenShield");
            squad?.Add(enemy); // Enemy_AI_Deep_Plan.md U25: test squads are set up with their kind
            DifficultyCurve.ApplyToEnemy(enemy, position); // limited ammo like on the road
            if (state == EnemyController.EnemyState.Combat) StartCoroutine(AlertNextFrame(enemy));
            return enemy;
        }

        private static IEnumerator AlertNextFrame(EnemyController enemy)
        {
            yield return null;
            yield return null;
            if (enemy == null || enemy.IsDead || AITarget.Player == null) yield break;
            // The wave knows where the player is (it was sent at him), then fights by its own senses
            enemy.Belief.Note(AITarget.Player, AITarget.Player.Position, Vector3.zero, 0.8f, BeliefSource.Told);
            enemy.EnterCombat(AITarget.Player);
        }

        private static GameObject PrefabFor(Kind kind)
        {
            string want = kind == Kind.Rifle ? "Enemy" : kind == Kind.Pistol || kind == Kind.ShieldPistol ? "Enemy_Pistol"
                        : kind == Kind.Knife ? "Enemy_Melee_Knife" : "Enemy_Melee_Bat";
            var list = SurvivalSettings.Instance.humanEnemyPrefabs;
            if (list == null) return null;
            foreach (var p in list) if (p != null && p.name == want) return p;
            foreach (var p in list) if (p != null) return p;
            return null;
        }

        private Vector3 CombatSpawnPoint(int i, int count)
        {
            float t = count <= 1 ? 0.5f : i / (float)(count - 1);
            float x = Mathf.Lerp(-18f, 18f, t);
            return new Vector3(x, 0f, 34f + (i % 2) * 6f);
        }

        private void SpawnWave(params Kind[] kinds)
        {
            var squad = EnemySquad.Create(SquadKind.Raid, new Vector3(0f, 0f, 36f));
            for (int i = 0; i < kinds.Length; i++)
                SpawnEnemy(kinds[i], CombatSpawnPoint(i, kinds.Length), EnemyController.EnemyState.Combat, 180f, squad);
        }

        /// <summary>A squad that has not seen the player yet (patrol: robbery test 21.4; ambush: holds fire 21.5).</summary>
        private void SpawnQuiet(SquadKind squadKind, params Kind[] kinds)
        {
            var squad = EnemySquad.Create(squadKind, new Vector3(0f, 0f, 36f));
            var state = squadKind == SquadKind.Patrol ? EnemyController.EnemyState.Patrolling : EnemyController.EnemyState.Idle;
            for (int i = 0; i < kinds.Length; i++)
                SpawnEnemy(kinds[i], CombatSpawnPoint(i, kinds.Length), state, 180f, squad);
        }

        private void SpawnAnimals(AnimalKind kind, int count)
        {
            int pack = Random.Range(1000, 999999);
            for (int i = 0; i < count; i++)
                AnimalController.Spawn(kind, new Vector3(-10f + i * 4f, 0.1f, 40f), _spawned, pack, 180f);
        }

        // ================================================================== stealth

        private void BuildStealth()
        {
            // Walled camp 40 m north: 30 x 30, two gaps (front and side), tents, crates, a watch platform
            Vector3 c = new Vector3(0f, 0f, 45f);
            float h = 15f;
            Box("CampWall_S_L", c + new Vector3(-9f, 1.2f, -h), new Vector3(12f, 2.4f, 0.4f), _matWall);
            Box("CampWall_S_R", c + new Vector3(9f, 1.2f, -h), new Vector3(12f, 2.4f, 0.4f), _matWall);
            Box("CampWall_N", c + new Vector3(0f, 1.2f, h), new Vector3(2f * h, 2.4f, 0.4f), _matWall);
            Box("CampWall_W", c + new Vector3(-h, 1.2f, 0f), new Vector3(0.4f, 2.4f, 2f * h), _matWall);
            Box("CampWall_E_N", c + new Vector3(h, 1.2f, 8f), new Vector3(0.4f, 2.4f, 14f), _matWall);
            Box("CampWall_E_S", c + new Vector3(h, 1.2f, -10f), new Vector3(0.4f, 2.4f, 10f), _matWall);
            Box("Tent_1", c + new Vector3(-8f, 1f, 6f), new Vector3(4f, 2f, 3f), _matCover);
            Box("Tent_2", c + new Vector3(7f, 1f, 7f), new Vector3(4f, 2f, 3f), _matCover, 90f);
            Box("Crates_1", c + new Vector3(-4f, 0.6f, -6f), new Vector3(2.4f, 1.2f, 1.2f), _matCover, 15f);
            Box("Crates_2", c + new Vector3(5f, 0.6f, -3f), new Vector3(1.2f, 1.2f, 1.2f), _matCover);
            Box("Crates_3", c + new Vector3(0f, 0.9f, 2f), new Vector3(1.8f, 1.8f, 1.8f), _matCover, 40f);
            Box("Platform", c + new Vector3(-11f, 1.5f, -11f), new Vector3(3f, 3f, 3f), _matWall);

            // Approach: rows of low walls and boulders between the start and the camp
            var rng = new System.Random(11);
            for (int i = 0; i < 14; i++)
            {
                float x = (float)rng.NextDouble() * 40f - 20f;
                float z = 8f + (float)rng.NextDouble() * 18f;
                if (i % 3 == 0) Box("Boulder", new Vector3(x, 0.7f, z), new Vector3(1.6f, 1.4f, 1.8f), _matWall, rng.Next(0, 90));
                else Box("LowWall", new Vector3(x, 0.5f, z), new Vector3(3f, 1f, 0.4f), _matCover, rng.Next(-40, 40));
            }
        }

        private void SpawnCamp()
        {
            ClearSpawned();
            Vector3 c = new Vector3(0f, 0f, 45f);
            var squad = EnemySquad.Create(SquadKind.Camp, c);
            SpawnEnemy(Kind.Rifle, c + new Vector3(0f, 0f, -12f), EnemyController.EnemyState.Patrolling, 0f, squad);
            SpawnEnemy(Kind.Pistol, c + new Vector3(10f, 0f, 0f), EnemyController.EnemyState.Patrolling, 270f, squad);
            SpawnEnemy(Kind.Knife, c + new Vector3(-10f, 0f, 0f), EnemyController.EnemyState.Patrolling, 90f, squad);
            SpawnEnemy(Kind.Rifle, c + new Vector3(-2f, 0f, -16.5f), EnemyController.EnemyState.Idle, 180f, squad);   // gate guard
            SpawnEnemy(Kind.Bat, c + new Vector3(6f, 0f, 4f), EnemyController.EnemyState.Sitting, 200f, squad);
            SpawnEnemy(Kind.Pistol, c + new Vector3(-6f, 0f, 9f), EnemyController.EnemyState.Idle, 160f, squad);
        }

        // ================================================================== driving

        private void BuildDriving()
        {
            // Road: 10 m wide, 1000 m straight north, a wide turning pad at the end
            Box("Road", new Vector3(0f, 0.01f, 500f), new Vector3(10f, 0.02f, 1000f), _matRoad);
            Box("TurnPad", new Vector3(0f, 0.01f, 1020f), new Vector3(80f, 0.02f, 60f), _matRoad);

            // Ramps beside the road (x = -25)
            Box("Ramp_Small", new Vector3(-25f, 0.6f, 60f), new Vector3(6f, 0.3f, 8f), _matRamp, 0f, -10f);
            Box("Ramp_Big", new Vector3(-25f, 1.4f, 140f), new Vector3(6f, 0.3f, 14f), _matRamp, 0f, -14f);
            Box("Ramp_Landing", new Vector3(-25f, 0.8f, 175f), new Vector3(8f, 0.3f, 12f), _matRamp, 0f, 8f);

            // Speed bumps on the road
            for (int i = 0; i < 6; i++)
                Box("Bump", new Vector3(0f, 0.08f, 220f + i * 6f), new Vector3(10f, 0.16f, 0.6f), _matRamp);

            // Walls to crash into (x = 25)
            Box("CrashWall", new Vector3(28f, 1.5f, 90f), new Vector3(0.8f, 3f, 20f), _matWall);

            BuildOffroad();
        }

        /// <summary>Hilly terrain east of the road: a real TerrainCollider, so the arcade model counts it as off-road.</summary>
        private void BuildOffroad()
        {
            var data = new TerrainData { heightmapResolution = 129 };
            data.size = new Vector3(300f, 12f, 300f);
            int res = data.heightmapResolution;
            var heights = new float[res, res];
            for (int y = 0; y < res; y++)
            {
                for (int x = 0; x < res; x++)
                {
                    float u = x / (float)(res - 1), v = y / (float)(res - 1);
                    float hills = Mathf.PerlinNoise(u * 4f + 3.1f, v * 4f + 7.7f) * 0.7f + Mathf.PerlinNoise(u * 13f, v * 13f) * 0.15f;
                    float edge = Mathf.Clamp01(Mathf.Min(u, 1f - u, v, 1f - v) * 8f); // flat at the borders, meets the ground
                    heights[y, x] = hills * edge;
                }
            }
            data.SetHeights(0, 0, heights);
            var go = Terrain.CreateTerrainGameObject(data);
            go.name = "Offroad_Terrain";
            go.transform.SetParent(_root, false);
            go.transform.position = new Vector3(40f, -0.05f, 250f);
            var terrain = go.GetComponent<Terrain>();
            var terrainShader = Shader.Find("Universal Render Pipeline/Terrain/Lit");
            if (terrainShader != null) terrain.materialTemplate = new Material(terrainShader);
        }

        private void SpawnCones()
        {
            ClearSpawned();
            // Slalom on the road: 12 cones every 18 m, alternating sides
            for (int i = 0; i < 12; i++)
                Cone(new Vector3(i % 2 == 0 ? -2.5f : 2.5f, 0.4f, 300f + i * 18f));
            // Crate pile to ram
            for (int i = 0; i < 12; i++)
            {
                var crate = Box("LooseCrate", new Vector3(-1.5f + (i % 3) * 1.1f, 0.5f + (i / 6) * 1.05f, 560f + (i / 3 % 2) * 1.1f), Vector3.one, _matCover, 0f, 0f, _spawned);
                var rb = crate.AddComponent<Rigidbody>();
                rb.mass = 25f;
            }
        }

        private void Cone(Vector3 p)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = "Cone";
            go.transform.SetParent(_spawned, false);
            go.transform.position = p;
            go.transform.localScale = new Vector3(0.45f, 0.4f, 0.45f);
            go.GetComponent<Renderer>().sharedMaterial = _matCone;
            var rb = go.AddComponent<Rigidbody>();
            rb.mass = 3f;
        }

        // ================================================================== player / vehicle / world

        private void ResetPlayer()
        {
            var player = SurvivalDirector.Player;
            if (player == null || SurvivalDirector.PlayerInVehicle) return;
            var cc = player.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;
            player.transform.SetPositionAndRotation(PlayerStart, Quaternion.identity);
            if (cc != null) cc.enabled = true;
            var health = player.GetComponent<PlayerHealth>();
            if (health != null && !health.IsDead) health.Heal(9999f);
        }

        private void ResetVehicle()
        {
            var car = SurvivalDirector.Vehicle;
            if (car == null) return;
            car.currentFuel = car.maxFuel;
            car.currentHealth = car.maxHealth;
            var rb = car.Rigidbody;
            Vector3 p = mode == Mode.Driving ? VehicleStart : new Vector3(-8f, 0.8f, -6f);
            if (rb != null)
            {
                rb.isKinematic = false;
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
                rb.position = p;
                rb.rotation = Quaternion.identity;
            }
            car.transform.SetPositionAndRotation(p, Quaternion.identity);
        }

        private void PlayerToVehicle()
        {
            var car = SurvivalDirector.Vehicle;
            var player = SurvivalDirector.Player;
            if (car == null || player == null || SurvivalDirector.PlayerInVehicle) return;
            var cc = player.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;
            player.transform.position = car.transform.position - car.transform.right * 2.2f;
            if (cc != null) cc.enabled = true;
        }

        private static void SetHour(float hour)
        {
            var cycle = DayNightCycle.Instance;
            if (cycle == null) return;
            cycle.currentHour = hour;
            cycle.UpdateLighting(true);
        }

        private void ClearSpawned()
        {
            if (_spawned == null) return;
            for (int i = _spawned.childCount - 1; i >= 0; i--) Destroy(_spawned.GetChild(i).gameObject);
        }

        private static int AliveEnemies()
        {
            int n = 0;
            foreach (var e in EnemyController.Active) if (e != null && !e.IsDead) n++;
            return n;
        }

        private static EnemyController NearestEnemy()
        {
            var player = SurvivalDirector.Player;
            if (player == null) return null;
            EnemyController best = null;
            float bestD = float.MaxValue;
            foreach (var e in EnemyController.Active)
            {
                if (e == null || e.IsDead) continue;
                float d = (e.transform.position - player.transform.position).sqrMagnitude;
                if (d < bestD) { bestD = d; best = e; }
            }
            return best;
        }

        // ================================================================== tuning panel page "Test"

        private void RegisterTuning()
        {
            if (s_registered) return; // rows call through Instance, so a reloaded scene keeps working
            s_registered = true;
            const string P = "Test";
            EndlessCombat.Tuning.TuningPanel.RegisterReadout(P, "Sahne", () => Instance == null ? "-" : Instance.Title);
            EndlessCombat.Tuning.TuningPanel.RegisterReadout(P, "Canlı düşman", () => AliveEnemies().ToString());
            EndlessCombat.Tuning.TuningPanel.RegisterReadout(P, "En yakın düşman", () =>
            {
                var e = NearestEnemy();
                return e == null ? "-" : $"{EnemyController.StateText(e.State)}, şüphe {Mathf.RoundToInt(e.suspicionMeter)}";
            });
            EndlessCombat.Tuning.TuningPanel.RegisterAction(P, "Oyuncuyu başa al ve iyileştir", () => Instance?.ResetPlayer());
            EndlessCombat.Tuning.TuningPanel.RegisterAction(P, "Aracı başa al (yakıt ve can dolu)", () => Instance?.ResetVehicle());
            EndlessCombat.Tuning.TuningPanel.RegisterAction(P, "Aracın yanına ışınlan", () => Instance?.PlayerToVehicle());
            EndlessCombat.Tuning.TuningPanel.RegisterAction(P, "Hepsini temizle (düşman, hayvan, duba)", () => Instance?.ClearSpawned());
            EndlessCombat.Tuning.TuningPanel.RegisterAction(P, "Sahnedeki NPC'leri yeniden kur (işaretlerden)", () => Instance?.RespawnMarkers());

            // Combat
            EndlessCombat.Tuning.TuningPanel.RegisterAction(P, "Savaş: tüfekli asker", () => Instance?.SpawnWave(Kind.Rifle));
            EndlessCombat.Tuning.TuningPanel.RegisterAction(P, "Savaş: tabancalı asker", () => Instance?.SpawnWave(Kind.Pistol));
            EndlessCombat.Tuning.TuningPanel.RegisterAction(P, "Savaş: bıçaklı + sopalı", () => Instance?.SpawnWave(Kind.Knife, Kind.Bat));
            EndlessCombat.Tuning.TuningPanel.RegisterAction(P, "Savaş: kalkanlı (sopa) + kalkanlı (tabanca)", () => Instance?.SpawnWave(Kind.ShieldMelee, Kind.ShieldPistol));
            EndlessCombat.Tuning.TuningPanel.RegisterAction(P, "Savaş: karışık grup (6, jeton testi)", () => Instance?.SpawnWave(Kind.Rifle, Kind.Pistol, Kind.Knife, Kind.ShieldMelee, Kind.Rifle, Kind.Bat));
            EndlessCombat.Tuning.TuningPanel.RegisterAction(P, "AI: devriye (4, görmemiş; soygun denenebilir)", () => Instance?.SpawnQuiet(SquadKind.Patrol, Kind.Rifle, Kind.Pistol, Kind.Rifle, Kind.Bat));
            EndlessCombat.Tuning.TuningPanel.RegisterAction(P, "AI: pusu takımı (4, ateşi tutar)", () => Instance?.SpawnQuiet(SquadKind.Ambush, Kind.Rifle, Kind.Rifle, Kind.Pistol, Kind.Rifle));
            EndlessCombat.Tuning.TuningPanel.RegisterAction(P, "Savaş: kurt sürüsü (3)", () => Instance?.SpawnAnimals(AnimalKind.Wolf, 3));
            EndlessCombat.Tuning.TuningPanel.RegisterAction(P, "Savaş: ayı", () => Instance?.SpawnAnimals(AnimalKind.Bear, 1));

            // Stealth
            EndlessCombat.Tuning.TuningPanel.RegisterAction(P, "Gizlilik: kampı yeniden kur", () =>
            {
                if (Instance == null) return;
                Instance.ClearSpawned();
                if (!Instance.SpawnFromMarkers()) Instance.SpawnCamp();
            });
            EndlessCombat.Tuning.TuningPanel.RegisterAction(P, "Gece / gündüz", () =>
            {
                var cycle = DayNightCycle.Instance;
                if (cycle != null) SetHour(cycle.IsNight ? 12f : 22.5f);
            });

            // Driving
            EndlessCombat.Tuning.TuningPanel.RegisterAction(P, "Sürüş: dubaları ve kasaları yeniden diz", () => Instance?.SpawnCones());
        }
    }
}
