using System.Collections.Generic;
using UnityEngine;
using EndlessCombat.AI;
using EndlessSurvival.Inventory;
using EndlessSurvival.World;
using EndlessSurvival.World.POI;
using EndlessSurvival.World.Road;

namespace EndlessSurvival.Survival
{
    /// <summary>
    /// Fills every built chunk (ChunkManager.ChunkBuilt) from its own seed sub-streams:
    /// resource nodes (2.4/2.13), wreck salvage (2.18), buildings (4.1), encounters (3.4) and traders (4.5).
    /// Everything is parented under the chunk, so it disappears with it. Also spawns night wolves (3.3).
    /// </summary>
    public class WorldPopulator : MonoBehaviour
    {
        private readonly HashSet<Chunk> _done = new HashSet<Chunk>();
        private float _wolfTimer;
        private int _nextPack = 1000;

        private void OnEnable() => ChunkManager.ChunkBuilt += OnChunkBuilt;
        private void OnDisable() => ChunkManager.ChunkBuilt -= OnChunkBuilt;

        private void Start()
        {
            // Chunks that were built before this object existed
            var manager = ChunkManager.Instance;
            if (manager == null) return;
            foreach (var c in manager.ActiveChunks)
            {
                if (c == null) continue;
                Chunk chunk = c;
                chunk.WhenBuilt(() => OnChunkBuilt(chunk));
            }
        }

        private void OnChunkBuilt(Chunk chunk)
        {
            if (chunk == null || !_done.Add(chunk)) return;
            var spline = chunk.GetComponentInChildren<RoadSpline>();
            if (spline == null) return;

            var root = new GameObject("SurvivalContent").transform;
            root.SetParent(chunk.transform, false);

            try { SpawnResources(chunk, spline, root); } catch (System.Exception e) { Debug.LogWarning("[Survival] resources: " + e.Message); }
            try { HookWrecks(chunk); } catch (System.Exception e) { Debug.LogWarning("[Survival] wrecks: " + e.Message); }
            try { SpawnBuilding(chunk, spline, root); } catch (System.Exception e) { Debug.LogWarning("[Survival] building: " + e.Message); }
            try { SpawnEncounter(chunk, spline, root); } catch (System.Exception e) { Debug.LogWarning("[Survival] encounter: " + e.Message); }
            try { SpawnTrader(chunk, spline, root); } catch (System.Exception e) { Debug.LogWarning("[Survival] trader: " + e.Message); }
        }

        // ---------------- ground points ----------------

        /// <summary>Point beside the road, on the ground, not in the sea / river channel / mountain.</summary>
        public static bool TryRoadsidePoint(Chunk chunk, RoadSpline spline, SeededRandom rng, float minLat, float maxLat, out Vector3 point, out Vector3 roadPoint)
        {
            point = roadPoint = default;
            float t = rng.Range(0.06f, 0.94f);
            Transform st = spline.transform;
            Vector3 center = st.TransformPoint(spline.GetPoint(t));
            Vector3 right = st.TransformDirection(spline.GetRight(t));
            float side = rng.Value < 0.5f ? -1f : 1f;
            float lateral = rng.Range(minLat, maxLat);
            roadPoint = center;
            return Ground(chunk, center + right * side * lateral, center, out point);
        }

        public static bool Ground(Chunk chunk, Vector3 probe, Vector3 roadCenter, out Vector3 point)
        {
            point = probe;
            Terrain terrain = chunk.ChunkTerrain;
            if (terrain == null)
            {
                if (!Physics.Raycast(probe + Vector3.up * 150f, Vector3.down, out RaycastHit hit, 400f, ~0, QueryTriggerInteraction.Ignore)) return false;
                point.y = hit.point.y;
                return true;
            }
            point.y = terrain.SampleHeight(probe) + terrain.transform.position.y;
            if (chunk.HasSea && point.y < chunk.transform.position.y + WorldConstants.SeaLevel + 1f) return false;
            if (chunk.IsField && point.y < chunk.transform.position.y + chunk.baseElevation - 1.5f) return false;
            if (chunk.IsMountain)
            {
                if (!chunk.IsFoothill && point.y < roadCenter.y - 3f) return false;
                var boundary = chunk.GetComponent<ChunkBoundaryGenerator>();
                Vector3 local = chunk.transform.InverseTransformPoint(point);
                if (boundary != null && (boundary.IsInsideMountain(local) || boundary.IsOnFoothillCliff(local))) return false;
            }
            if (Mathf.Abs(point.y - roadCenter.y) > 9f) return false; // too steep / far below the road
            return true;
        }

        private static bool NearPoi(Chunk chunk, Vector3 p, float radius)
        {
            foreach (var poi in chunk.GetComponentsInChildren<PointOfInterest>(true))
                if ((poi.transform.position - p).sqrMagnitude < radius * radius) return true;
            return false;
        }

        // ---------------- 2.4 / 2.13 resources ----------------

        private void SpawnResources(Chunk chunk, RoadSpline spline, Transform root)
        {
            var rng = new SeededRandom(chunk.ChunkSeed).SubStream("survival_resources");
            int trees, bushes, herbs, mushrooms;
            switch (chunk.biomeType)
            {
                case ChunkBiomeType.Forest: trees = 7; bushes = 4; herbs = 3; mushrooms = 4; break;
                case ChunkBiomeType.Field: trees = 3; bushes = 5; herbs = 4; mushrooms = 1; break;
                case ChunkBiomeType.Coast: trees = 3; bushes = 2; herbs = 2; mushrooms = 1; break;
                case ChunkBiomeType.Mountain: trees = 3; bushes = 1; herbs = 3; mushrooms = 1; break;
                case ChunkBiomeType.Desert: trees = 2; bushes = 0; herbs = 1; mushrooms = 0; break;
                default: trees = 3; bushes = 1; herbs = 1; mushrooms = 1; break;
            }
            Spawn(ResourceKind.DeadTree, trees, 9f, 35f);
            Spawn(ResourceKind.BerryBush, bushes, 8f, 30f);
            Spawn(ResourceKind.Herb, herbs, 7f, 28f);
            Spawn(ResourceKind.Mushroom, mushrooms, 8f, 30f);

            void Spawn(ResourceKind kind, int count, float minLat, float maxLat)
            {
                for (int i = 0; i < count; i++)
                {
                    if (!TryRoadsidePoint(chunk, spline, rng, minLat, maxLat, out Vector3 p, out _)) continue;
                    ResourceNode.Create(kind, p, root, rng);
                }
            }
        }

        // ---------------- 2.18 wrecks ----------------

        private static void HookWrecks(Chunk chunk)
        {
            int i = 0;
            foreach (var t in chunk.GetComponentsInChildren<Transform>(true))
            {
                if (t.name != "WreckCollider" || t.GetComponent<WreckSalvage>() != null) continue;
                var salvage = t.gameObject.AddComponent<WreckSalvage>();
                salvage.seed = SeededRandom.Combine(SeededRandom.Combine(chunk.ChunkSeed, "wreck_salvage"), i++);
            }
        }

        // ---------------- 4.1 buildings ----------------

        private void SpawnBuilding(Chunk chunk, RoadSpline spline, Transform root)
        {
            if (chunk.IsMountain && !chunk.IsFoothill) return;
            var rng = new SeededRandom(chunk.ChunkSeed).SubStream("survival_building");
            float chance = chunk.biomeType == ChunkBiomeType.RuinedCity ? 0.7f : 0.38f;
            if (!rng.Chance(chance)) return;
            bool gas = rng.Chance(0.4f);

            for (int attempt = 0; attempt < 6; attempt++)
            {
                if (!TryRoadsidePoint(chunk, spline, rng, gas ? 17f : 20f, gas ? 22f : 30f, out Vector3 p, out Vector3 road)) continue;
                if (NearPoi(chunk, p, 45f)) continue;
                Vector3 toRoad = road - p;
                toRoad.y = 0f;
                if (toRoad.sqrMagnitude < 0.01f) continue;
                Quaternion rot = Quaternion.LookRotation(toRoad.normalized);
                PrepareGround(chunk, p, gas ? 13f : 9f);
                var building = gas ? Buildings.GasStation(p, rot, root, rng) : Buildings.House(p, rot, root, rng);
                building.name = gas ? "POI_GasStation" : "POI_AbandonedHouse";
                return;
            }
        }

        private static void PrepareGround(Chunk chunk, Vector3 p, float radius)
        {
            var terrain = chunk.ChunkTerrain;
            if (terrain == null) return;
            RoadTerrainAdapter.FlattenTerrainArea(terrain, p, radius, radius * 0.8f, p.y);

            // Remove terrain trees inside the building area (the terrain data is a per-chunk copy)
            var data = terrain.terrainData;
            var trees = data.treeInstances;
            var kept = new List<TreeInstance>(trees.Length);
            Vector3 size = data.size, origin = terrain.transform.position;
            float r2 = (radius + 2f) * (radius + 2f);
            foreach (var tree in trees)
            {
                Vector3 w = Vector3.Scale(tree.position, size) + origin;
                if ((new Vector2(w.x - p.x, w.z - p.z)).sqrMagnitude > r2) kept.Add(tree);
            }
            if (kept.Count != trees.Length) data.SetTreeInstances(kept.ToArray(), true);
        }

        // ---------------- 3.4 encounters ----------------

        private void SpawnEncounter(Chunk chunk, RoadSpline spline, Transform root)
        {
            if (chunk.GetComponentInChildren<EnemyCampPOI>(true) != null) return; // camps already have people
            var rng = new SeededRandom(chunk.ChunkSeed).SubStream("encounter");
            if (!rng.Chance(SurvivalSettings.Instance.encounterChance)) return;

            float animalWeight;
            switch (chunk.biomeType)
            {
                case ChunkBiomeType.Field:
                case ChunkBiomeType.Forest: animalWeight = 0.75f; break;
                case ChunkBiomeType.Mountain: animalWeight = 0.7f; break;
                case ChunkBiomeType.Coast: animalWeight = 0.5f; break;
                default: animalWeight = 0.3f; break; // wasteland, ruined city, desert: people by the road
            }

            if (rng.Value < animalWeight) SpawnAnimals(chunk, spline, root, rng);
            else SpawnPatrol(chunk, spline, root, rng);
        }

        private void SpawnAnimals(Chunk chunk, RoadSpline spline, Transform root, SeededRandom rng)
        {
            float roll = rng.Value;
            AnimalKind kind;
            int count;
            bool mountain = chunk.IsMountain;
            if (roll < 0.4f) { kind = AnimalKind.Deer; count = rng.Range(2, 5); }
            else if (roll < 0.7f) { kind = AnimalKind.Rabbit; count = rng.Range(2, 4); }
            else if (roll < 0.9f || !mountain && chunk.biomeType != ChunkBiomeType.Forest) { kind = AnimalKind.Wolf; count = rng.Range(2, 4); }
            else { kind = AnimalKind.Bear; count = 1; }

            if (!TryRoadsidePoint(chunk, spline, rng, 25f, 55f, out Vector3 center, out _)) return;
            int pack = ++_nextPack;
            for (int i = 0; i < count; i++)
            {
                Vector3 offset = new Vector3(rng.Range(-4f, 4f), 0f, rng.Range(-4f, 4f));
                if (!Ground(chunk, center + offset, center, out Vector3 p)) p = center;
                AnimalController.Spawn(kind, p, root, pack, rng.Range(0f, 360f));
            }
        }

        private void SpawnPatrol(Chunk chunk, RoadSpline spline, Transform root, SeededRandom rng)
        {
            var prefabs = SurvivalSettings.Instance.humanEnemyPrefabs;
            if (prefabs == null || prefabs.Length == 0) return;
            if (!TryRoadsidePoint(chunk, spline, rng, 6f, 12f, out Vector3 center, out Vector3 road)) return;
            int count = DifficultyCurve.ScaleEnemyCount(rng.Range(2, 4), center);
            for (int i = 0; i < count; i++)
            {
                var prefab = DifficultyCurve.PickEnemyPrefab(rng, prefabs, center);
                if (prefab == null) continue;
                Vector3 offset = new Vector3(rng.Range(-3f, 3f), 0f, rng.Range(-3f, 3f));
                if (!Ground(chunk, center + offset, road, out Vector3 p)) p = center;
                var go = Instantiate(prefab, p + Vector3.up * 0.1f, Quaternion.Euler(0f, rng.Range(0f, 360f), 0f), root);
                var enemy = go.GetComponent<EnemyController>();
                if (enemy != null)
                {
                    DifficultyCurve.ApplyToEnemy(enemy, center);
                    enemy.initialState = EnemyController.EnemyState.Patrolling;
                }
            }
        }

        // ---------------- 4.5 trader ----------------

        private void SpawnTrader(Chunk chunk, RoadSpline spline, Transform root)
        {
            var rng = new SeededRandom(chunk.ChunkSeed).SubStream("trader");
            if (!rng.Chance(SurvivalSettings.Instance.traderChance)) return;
            if (chunk.GetComponentInChildren<EnemyCampPOI>(true) != null) return;
            if (!TryRoadsidePoint(chunk, spline, rng, 5f, 8f, out Vector3 p, out Vector3 road)) return;
            Vector3 toRoad = road - p;
            toRoad.y = 0f;
            Trader.Create(p, toRoad.sqrMagnitude > 0.01f ? Quaternion.LookRotation(toRoad) : Quaternion.identity, root, rng.Range(int.MinValue, int.MaxValue));
        }

        // ---------------- 3.3 night wolves ----------------

        private void Update()
        {
            var cycle = DayNightCycle.Instance;
            var player = SurvivalDirector.Player;
            if (cycle == null || player == null || !cycle.IsNight || !player.activeInHierarchy) return;
            _wolfTimer += Time.deltaTime;
            var s = SurvivalSettings.Instance;
            if (_wolfTimer < s.nightWolfCheckSeconds) return;
            _wolfTimer = 0f;

            Vector3 pos = player.transform.position;
            if (CraftingSystem.NearestBurningFire(pos, 12f) != null) return; // fire keeps them away
            foreach (var a in AnimalController.All)
                if (a != null && a.kind == AnimalKind.Wolf && (a.transform.position - pos).sqrMagnitude < 80f * 80f) return;
            if (Random.value > s.nightWolfChance) return;

            var chunk = SurvivalDirector.ChunkAt(pos);
            if (chunk == null) return;
            Vector3 dir = -player.transform.forward;
            dir = Quaternion.Euler(0f, Random.Range(-60f, 60f), 0f) * dir;
            Vector3 center = pos + dir * Random.Range(35f, 45f);
            if (!Ground(chunk, center, pos, out Vector3 c)) return;
            int pack = ++_nextPack;
            int count = Random.Range(2, 4);
            for (int i = 0; i < count; i++)
            {
                Vector3 p = c + new Vector3(Random.Range(-3f, 3f), 0f, Random.Range(-3f, 3f));
                if (!Ground(chunk, p, pos, out p)) p = c;
                AnimalController.Spawn(AnimalKind.Wolf, p, chunk.transform, pack, Random.Range(0f, 360f));
            }
            SurvivalNotify.Warn("Uzaktan kurt ulumaları geliyor...");
        }
    }
}
