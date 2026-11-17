using System.Collections;
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
    /// Harabe Kasaba and Mola Yeri chunk types: the settlement beside the road. There is no fixed prefab. Every
    /// chunk rolls its own layout from its seed: which buildings, how many, their size, order, colour, damage and
    /// side of the road. Two towns (or two rest stops) never look alike.
    ///
    /// <see cref="Build"/> plans every lot at once (synchronously, inside StartCoroutine) and registers the
    /// footprints, so the other roadside spawners (<see cref="WorldPopulator.TryRoadsidePoint"/>) keep clear of
    /// them. The geometry is then built a few lots per frame. Everything is primitives under the chunk.
    /// </summary>
    public static class Settlements
    {
        // ------------------------------------------------------------------ footprints

        private struct Footprint
        {
            public Vector3 center, axisX, axisZ;
            public float halfW, halfD;
        }

        private static readonly Dictionary<Chunk, List<Footprint>> s_footprints = new Dictionary<Chunk, List<Footprint>>();

        /// <summary>A world point is inside (or within margin of) a planned settlement building / yard.</summary>
        public static bool Blocked(Chunk chunk, Vector3 world, float margin)
        {
            if (chunk == null || !s_footprints.TryGetValue(chunk, out var list)) return false;
            foreach (var f in list)
            {
                Vector3 d = world - f.center;
                if (Mathf.Abs(Vector3.Dot(d, f.axisX)) < f.halfW + margin && Mathf.Abs(Vector3.Dot(d, f.axisZ)) < f.halfD + margin)
                    return true;
            }
            return false;
        }

        private static void Register(Chunk chunk, Lot lot)
        {
            if (!s_footprints.TryGetValue(chunk, out var list)) s_footprints[chunk] = list = new List<Footprint>();
            list.Add(new Footprint
            {
                center = lot.pos,
                axisX = lot.rot * Vector3.right,
                axisZ = lot.rot * Vector3.forward,
                halfW = lot.w * 0.5f,
                halfD = lot.d * 0.5f
            });
        }

        private static void PurgeDestroyed()
        {
            List<Chunk> dead = null;
            foreach (var key in s_footprints.Keys)
                if (key == null) (dead ??= new List<Chunk>()).Add(key);
            if (dead != null) foreach (var key in dead) s_footprints.Remove(key);
        }

        // ------------------------------------------------------------------ lots

        private enum Kind
        {
            House, TwoStory, Shop, Apartment, Rubble, Burned, Garage, FencedLot, LotWreck, GasStation, WaterTower,
            TownSign, StreetLamp,
            Diner, Market, Motel, Toilets, Picnic, Truck, Billboard, Apron
        }

        private class Lot
        {
            public Kind kind;
            public Vector3 pos;          // ground point, building centre (local +Z faces the road)
            public Quaternion rot;
            public float w, d;           // along the road / away from the road
            public float lateral;        // centre distance from the road axis
            public float damage;
            public bool burned;
            public int rooms;
            public int seed;
            public bool flatten = true;
            public bool register = true;
            public float roadClear = 8f;  // the leveled pad keeps this far from the axis it faces (main road / street)
        }

        private class Ctx
        {
            public Chunk chunk;
            public RoadSpline spline;
            public Terrain terrain;
            public SeededRandom rng;
            public readonly List<Lot> lots = new List<Lot>();
            public readonly List<Vector3> wrecks = new List<Vector3>();
            public readonly List<Vector3> pois = new List<Vector3>();
            public readonly List<Transform> vegetation = new List<Transform>();
            public bool traderPlaced;
            public readonly int[] rejects = new int[10]; // range, ground, slope, river, barricade, safe, poi, wreck, lot, side road
        }

        /// <summary>Some side road of the chunk has a building at its end (built here even without a settlement).</summary>
        public static bool HasSideRoadEnds(Chunk chunk)
        {
            if (chunk == null) return false;
            foreach (var road in chunk.sideRoads) if (road.endContent) return true;
            return false;
        }

        // ------------------------------------------------------------------ entry

        /// <summary>
        /// Plans (synchronously, before the first yield) and then builds the chunk's settlement. Call with
        /// StartCoroutine before the other roadside content is spawned.
        /// </summary>
        public static IEnumerator Build(Chunk chunk, RoadSpline spline, Transform root)
        {
            if (chunk == null || spline == null || (!chunk.HasSettlement && !HasSideRoadEnds(chunk))) yield break;
            PurgeDestroyed();

            var ctx = new Ctx
            {
                chunk = chunk,
                spline = spline,
                terrain = chunk.ChunkTerrain,
                rng = new SeededRandom(chunk.ChunkSeed).SubStream("settlement")
            };
            foreach (var t in chunk.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == "WreckCollider") ctx.wrecks.Add(t.position);
                else if (t.name == ChunkVegetationSpawner.CONTAINER_NAME) ctx.vegetation.Add(t);
            }
            foreach (var poi in chunk.GetComponentsInChildren<PointOfInterest>(true))
                if (poi.gameObject.activeInHierarchy) ctx.pois.Add(poi.transform.position);

            if (chunk.Settlement == SettlementKind.Town) PlanTown(ctx);
            else if (chunk.Settlement == SettlementKind.RestStop) PlanRestStop(ctx);
            PlanSideRoadEnds(ctx);
            Debug.Log($"[Settlement] {chunk.Settlement} chunk {chunk.ChunkIndex}: {ctx.lots.Count} lots; rejected range/ground/slope/river/barricade/safe/poi/wreck/lot/sideroad = {string.Join("/", ctx.rejects)}");

            foreach (var lot in ctx.lots)
            {
                if (!lot.register) continue;
                Register(chunk, lot);
                ClearVegetation(ctx, lot, 2.5f);
            }

            var settlementRoot = new GameObject(chunk.Settlement == SettlementKind.Town ? "Settlement_Town"
                : chunk.Settlement == SettlementKind.RestStop ? "Settlement_RestStop" : "Settlement_SideRoadEnds").transform;
            settlementRoot.SetParent(root, true);

            // Terrain first (one frame), then the buildings a few per frame
            foreach (var lot in ctx.lots) if (lot.flatten) Flatten(ctx, lot);
            yield return null;

            int built = 0;
            foreach (var lot in ctx.lots)
            {
                if (chunk == null || settlementRoot == null) yield break;
                try { BuildLot(ctx, lot, settlementRoot); }
                catch (System.Exception e) { Debug.LogWarning($"[Settlement] {lot.kind}: {e.Message}"); }
                if (++built % 3 == 0) yield return null;
            }

            if (chunk != null && settlementRoot != null && chunk.Settlement == SettlementKind.Town)
            {
                try { SpawnOccupants(ctx, settlementRoot); }
                catch (System.Exception e) { Debug.LogWarning("[Settlement] occupants: " + e.Message); }
            }
        }

        // ------------------------------------------------------------------ town plan

        private static void PlanTown(Ctx ctx)
        {
            var s = ctx.chunk.settlementSettings;
            var rng = ctx.rng;
            float len = ctx.chunk.chunkLength;
            float zStart = ctx.chunk.SettlementContinuesBefore ? 4f : rng.Range(30f, 55f);
            float zEnd = ctx.chunk.SettlementContinuesAfter ? len - 4f : len - rng.Range(30f, 55f);
            bool gasPlaced = !rng.Chance(0.35f);
            bool towerPlaced = !rng.Chance(0.3f);

            for (int sideIndex = 0; sideIndex < 2; sideIndex++)
            {
                float side = sideIndex == 0 ? -1f : 1f;
                float z = zStart + rng.Range(0f, 6f);
                while (z < zEnd - 6f)
                {
                    Kind kind;
                    float w, d, front = rng.Range(s.townSetbackMin, s.townSetbackMax);
                    if (!gasPlaced && rng.Chance(0.18f))
                    {
                        kind = Kind.GasStation; w = 12f; d = 16f; front += 2f; gasPlaced = true;
                    }
                    else if (rng.Chance(s.townDensity))
                    {
                        kind = PickTownBuilding(rng);
                        TownSize(kind, rng, out w, out d);
                    }
                    else
                    {
                        float r = rng.Value;
                        kind = r < 0.45f ? Kind.FencedLot : r < 0.65f ? Kind.LotWreck : Kind.Rubble;
                        w = rng.Range(8f, 14f); d = rng.Range(7f, 11f);
                        if (r >= 0.65f && r < 0.8f) { z += rng.Range(6f, 14f); continue; } // empty gap
                    }
                    if (z + w > zEnd) break;

                    var lot = TrySite(ctx, z + w * 0.5f, side, front + d * 0.5f, w, d, kind);
                    if (lot != null)
                    {
                        TownDamage(lot, s, rng);
                        ctx.lots.Add(lot);

                        // Back row behind it: a smaller house, a ruin, a garage, now and then the water tower
                        if (IsBuilding(kind) && rng.Chance(s.townBackRowChance))
                        {
                            Kind back;
                            float bw, bd;
                            if (!towerPlaced) { back = Kind.WaterTower; bw = 7f; bd = 7f; towerPlaced = true; }
                            else
                            {
                                float r = rng.Value;
                                back = r < 0.45f ? Kind.House : r < 0.7f ? Kind.Rubble : r < 0.85f ? Kind.Garage : Kind.Burned;
                                TownSize(back, rng, out bw, out bd);
                            }
                            float backFront = front + d + rng.Range(5f, 11f);
                            var backLot = TrySite(ctx, z + w * 0.5f + rng.Range(-2f, 2f), side, backFront + bd * 0.5f, bw, bd, back);
                            if (backLot != null)
                            {
                                TownDamage(backLot, s, rng);
                                ctx.lots.Add(backLot);
                            }
                        }
                    }
                    z += w + rng.Range(2f, 9f);
                }
            }

            // Houses along the side streets
            PlanStreetLots(ctx, s, ref towerPlaced);

            // Street lamps along the sidewalks, some knocked over
            float lampZ = zStart + rng.Range(0f, 15f);
            float lampSide = rng.Chance(0.5f) ? -1f : 1f;
            while (lampZ < zEnd)
            {
                if (RoadAt(ctx, lampZ, out Vector3 c, out Vector3 right, out Vector3 fwd))
                {
                    Vector3 p = SurvivalPrimitives.Ground(c + right * lampSide * 7.6f + Vector3.up * 2f);
                    ctx.lots.Add(new Lot
                    {
                        kind = Kind.StreetLamp, pos = p, rot = Quaternion.LookRotation(-right * lampSide),
                        w = 0.5f, d = 0.5f, lateral = 7.6f, seed = rng.Range(int.MinValue, int.MaxValue),
                        damage = rng.Value, flatten = false, register = false
                    });
                }
                lampZ += rng.Range(26f, 40f);
                lampSide = -lampSide;
            }

            // Town sign at the entrance (right-hand side, facing the oncoming driver)
            if (!ctx.chunk.SettlementContinuesBefore && RoadAt(ctx, rng.Range(12f, 20f), out Vector3 sc, out Vector3 sr, out Vector3 sf))
            {
                Vector3 p = SurvivalPrimitives.Ground(sc + sr * 9.5f + Vector3.up * 2f);
                ctx.lots.Add(new Lot { kind = Kind.TownSign, pos = p, rot = Quaternion.LookRotation(-sf), w = 3f, d = 0.5f, flatten = false, register = false, seed = rng.Range(int.MinValue, int.MaxValue) });
            }
        }

        // ------------------------------------------------------------------ side roads

        /// <summary>World point, direction and across-vector of a side road at distance <paramref name="along"/> from the junction.</summary>
        private static bool SideRoadAt(Ctx ctx, SideRoad road, float along, out Vector3 center, out Vector3 fwd, out Vector3 right)
        {
            center = fwd = right = default;
            var pts = road.points;
            if (pts.Count < 2) return false;
            float acc = 0f;
            Transform ct = ctx.chunk.transform;
            float terrainY = ctx.terrain != null ? ctx.terrain.transform.localPosition.y : 0f;
            for (int i = 0; i < pts.Count - 1; i++)
            {
                Vector3 a = pts[i], b = pts[i + 1];
                Vector3 ab = b - a; ab.y = 0f;
                float l = ab.magnitude;
                if (acc + l >= along || i == pts.Count - 2)
                {
                    float t = l > 0.001f ? Mathf.Clamp01((along - acc) / l) : 0f;
                    Vector3 local = Vector3.Lerp(a, b, t);
                    local.y += terrainY;
                    center = ct.TransformPoint(local);
                    fwd = ct.TransformDirection(ab.normalized);
                    fwd.y = 0f;
                    fwd.Normalize();
                    right = Vector3.Cross(Vector3.up, fwd).normalized;
                    return along <= acc + l + 0.01f;
                }
                acc += l;
            }
            return false;
        }

        /// <summary>Town: rows of houses on both sides of every side street, facing it.</summary>
        private static void PlanStreetLots(Ctx ctx, SettlementSettings s, ref bool towerPlaced)
        {
            var rng = ctx.rng;
            foreach (var road in ctx.chunk.sideRoads)
            {
                if (road.style != SideRoadStyle.Street) continue;
                float length = road.Length;
                float clear = road.halfWidth + 1.5f;
                for (int sideIndex = 0; sideIndex < 2; sideIndex++)
                {
                    float side = sideIndex == 0 ? -1f : 1f;
                    float a = rng.Range(20f, 28f); // past the main road's own rows (the overlap test sorts out the corner)
                    while (a < length - 8f)
                    {
                        Kind kind;
                        float w, d;
                        if (rng.Chance(s.townDensity)) { kind = PickTownBuilding(rng); TownSize(kind, rng, out w, out d); }
                        else
                        {
                            float r = rng.Value;
                            if (r > 0.75f) { a += rng.Range(6f, 12f); continue; } // empty gap
                            kind = r < 0.45f ? Kind.FencedLot : r < 0.6f ? Kind.LotWreck : Kind.Rubble;
                            w = rng.Range(8f, 13f); d = rng.Range(7f, 10f);
                        }
                        if (a + w > length - 4f) break;
                        if (!SideRoadAt(ctx, road, a + w * 0.5f, out Vector3 c, out _, out Vector3 right)) break;
                        float front = clear + rng.Range(3.5f, 6.5f);
                        var lot = TrySiteAt(ctx, c, right, side, front + d * 0.5f, w, d, kind, false, clear);
                        if (lot != null)
                        {
                            TownDamage(lot, s, rng);
                            ctx.lots.Add(lot);
                            if (!towerPlaced && IsBuilding(kind) && rng.Chance(0.25f))
                            {
                                var tower = TrySiteAt(ctx, c, right, side, front + d + 9f, 7f, 7f, Kind.WaterTower, false, clear);
                                if (tower != null) { ctx.lots.Add(tower); towerPlaced = true; }
                            }
                        }
                        a += w + rng.Range(2f, 8f);
                    }
                }
            }
        }

        /// <summary>Side roads that end at something: one building (or yard) standing past the end, facing back down the road.</summary>
        private static void PlanSideRoadEnds(Ctx ctx)
        {
            var settings = ctx.chunk.settlementSettings ?? new SettlementSettings();
            foreach (var road in ctx.chunk.sideRoads)
            {
                if (!road.endContent || road.points.Count < 2) continue;
                var rng = new SeededRandom(road.seed).SubStream("end");
                Kind kind;
                float r = rng.Value;
                switch (road.style)
                {
                    case SideRoadStyle.FarmRoad:
                        kind = r < 0.45f ? Kind.House : r < 0.7f ? Kind.TwoStory : r < 0.9f ? Kind.Garage : Kind.Burned; break;
                    case SideRoadStyle.Street:
                        kind = r < 0.5f ? Kind.House : r < 0.75f ? Kind.Shop : Kind.Garage; break;
                    default: // forest / desert track: a lone cabin, a shed, a burned-out shell, a ruin
                        kind = r < 0.45f ? Kind.House : r < 0.65f ? Kind.Garage : r < 0.85f ? Kind.Burned : Kind.Rubble; break;
                }
                TownSize(kind, rng, out float w, out float d);
                SideRoadAt(ctx, road, road.Length, out Vector3 end, out Vector3 fwd, out _);
                // "right" here is the road direction: the lot stands past the end and faces back along the road
                var lot = TrySiteAt(ctx, end, fwd, 1f, d * 0.5f + road.halfWidth + 3.5f, w, d, kind, false, 1f)
                          ?? TrySiteAt(ctx, end, fwd, 1f, d * 0.5f + road.halfWidth + 8f, w, d, kind, false, 1f);
                if (lot == null) continue;
                TownDamage(lot, settings, rng);
                if (kind == Kind.Burned) lot.burned = true;
                ctx.lots.Add(lot);
            }
        }

        private static Kind PickTownBuilding(SeededRandom rng)
        {
            float r = rng.Range(0f, 90f);
            if ((r -= 30f) < 0f) return Kind.House;
            if ((r -= 18f) < 0f) return Kind.TwoStory;
            if ((r -= 12f) < 0f) return Kind.Shop;
            if ((r -= 6f) < 0f) return Kind.Apartment;
            if ((r -= 10f) < 0f) return Kind.Rubble;
            if ((r -= 8f) < 0f) return Kind.Burned;
            return Kind.Garage;
        }

        private static void TownSize(Kind kind, SeededRandom rng, out float w, out float d)
        {
            switch (kind)
            {
                case Kind.TwoStory: w = rng.Range(8f, 11f); d = rng.Range(7f, 9f); break;
                case Kind.Shop: w = rng.Range(10f, 14f); d = rng.Range(8f, 10f); break;
                case Kind.Apartment: w = rng.Range(13f, 17f); d = rng.Range(10f, 12f); break;
                case Kind.Garage: w = rng.Range(7f, 9f); d = rng.Range(7f, 9f); break;
                default: w = rng.Range(7f, 10f); d = rng.Range(6f, 8f); break;
            }
        }

        private static bool IsBuilding(Kind k) =>
            k == Kind.House || k == Kind.TwoStory || k == Kind.Shop || k == Kind.Apartment || k == Kind.Burned || k == Kind.Garage;

        private static void TownDamage(Lot lot, SettlementSettings s, SeededRandom rng)
        {
            switch (lot.kind)
            {
                case Kind.Rubble: lot.damage = 1f; break;
                case Kind.Burned: lot.damage = rng.Range(0.35f, 0.7f); lot.burned = true; break;
                case Kind.Apartment: lot.damage = rng.Chance(Mathf.Clamp01(s.townRuinShare + 0.25f)) ? rng.Range(0.5f, 0.95f) : rng.Range(0f, 0.2f); break;
                case Kind.House:
                case Kind.TwoStory:
                case Kind.Shop:
                case Kind.Garage:
                    lot.damage = rng.Chance(s.townRuinShare) ? rng.Range(0.45f, 0.95f) : rng.Range(0f, 0.25f); break;
                default: lot.damage = 0f; break;
            }
        }

        // ------------------------------------------------------------------ rest stop plan

        private static void PlanRestStop(Ctx ctx)
        {
            var s = ctx.chunk.settlementSettings;
            var rng = ctx.rng;
            float len = ctx.chunk.chunkLength;
            float side = rng.Chance(0.5f) ? -1f : 1f;

            var parts = new List<Kind>();
            if (rng.Chance(s.stopFuelChance)) parts.Add(Kind.GasStation);
            bool diner = rng.Chance(0.8f), market = rng.Chance(0.5f);
            if (!diner && !market) { if (rng.Chance(0.5f)) diner = true; else market = true; }
            if (diner) parts.Add(Kind.Diner);
            if (market) parts.Add(Kind.Market);
            if (rng.Chance(0.55f)) parts.Add(Kind.Motel);
            if (rng.Chance(0.5f)) parts.Add(Kind.Toilets);
            if (rng.Chance(0.6f)) parts.Add(Kind.Picnic);
            if (rng.Chance(0.45f)) parts.Add(Kind.Truck);
            for (int i = parts.Count - 1; i > 0; i--)
            {
                int j = rng.Range(0, i + 1);
                (parts[i], parts[j]) = (parts[j], parts[i]);
            }

            // Sizes and the total length along the road
            var sizes = new List<Vector3>(); // w, d, front setback
            float total = 0f;
            foreach (var k in parts)
            {
                Vector3 size;
                switch (k)
                {
                    case Kind.GasStation: size = new Vector3(12f, 16f, rng.Range(15f, 17f)); break;
                    case Kind.Diner: size = new Vector3(rng.Range(12f, 15f), rng.Range(8f, 10f), rng.Range(17f, 21f)); break;
                    case Kind.Market: size = new Vector3(rng.Range(9f, 11f), rng.Range(7f, 8f), rng.Range(16f, 20f)); break;
                    case Kind.Motel:
                        int rooms = rng.Range(3, 7);
                        size = new Vector3(rooms * 4.5f, 6f, rng.Range(18f, 22f));
                        break;
                    case Kind.Toilets: size = new Vector3(4.6f, 3.4f, rng.Range(16f, 20f)); break;
                    case Kind.Picnic: size = new Vector3(rng.Range(10f, 14f), rng.Range(7f, 10f), rng.Range(12f, 15f)); break;
                    default: size = new Vector3(15f, 4f, 10f); break; // truck parked along the road
                }
                sizes.Add(size);
                total += size.x;
            }
            var gaps = new float[parts.Count];
            for (int i = 0; i < gaps.Length; i++) { gaps[i] = rng.Range(5f, 12f); total += gaps[i]; }

            float half = total * 0.5f;
            float zCenter = half * 2f < len - 220f ? rng.Range(110f + half, len - 110f - half) : len * 0.5f;
            float z = zCenter - half;
            float zFirst = z;

            for (int i = 0; i < parts.Count; i++)
            {
                Vector3 size = sizes[i];
                var lot = TrySite(ctx, z + size.x * 0.5f, side, size.z + size.y * 0.5f, size.x, size.y, parts[i]);
                if (lot != null)
                {
                    if (parts[i] == Kind.Motel) lot.rooms = Mathf.RoundToInt(size.x / 4.5f);
                    lot.damage = rng.Chance(0.3f) ? rng.Range(0.3f, 0.6f) : rng.Range(0f, 0.15f);
                    ctx.lots.Add(lot);

                    // Paved forecourt / parking between the road and the building
                    if (parts[i] == Kind.Diner || parts[i] == Kind.Market || parts[i] == Kind.Motel || parts[i] == Kind.Toilets)
                    {
                        float apronIn = 8.6f, apronOut = size.z;
                        if (apronOut - apronIn > 2f)
                        {
                            float aw = size.x + 4f, ad = apronOut - apronIn;
                            var apron = TrySite(ctx, z + size.x * 0.5f, side, apronIn + ad * 0.5f, aw, ad, Kind.Apron, true);
                            if (apron != null)
                            {
                                apron.flatten = false;
                                apron.rooms = parts[i] == Kind.Motel ? 0 : 1; // 1 = parking lines
                                ctx.lots.Add(apron);
                            }
                        }
                    }
                }
                z += size.x + gaps[i];
            }

            // Billboard before the stop (seen while driving toward it)
            if (rng.Chance(0.75f))
            {
                float bz = Mathf.Max(20f, zFirst - rng.Range(25f, 50f));
                var board = TrySite(ctx, bz, side, 14f, 7.5f, 1.2f, Kind.Billboard, true);
                if (board != null)
                {
                    // Turned toward the oncoming driver
                    RoadAt(ctx, bz, out _, out _, out Vector3 bf);
                    board.rot = Quaternion.LookRotation((board.rot * Vector3.forward - bf * 1.5f).normalized);
                    board.flatten = false;
                    ctx.lots.Add(board);
                }
            }

            // Sometimes something on the other side too
            if (rng.Chance(s.stopOppositeSideChance))
            {
                float r = rng.Value;
                Kind k = r < 0.45f ? Kind.Picnic : r < 0.8f ? Kind.Truck : Kind.Billboard;
                float w = k == Kind.Picnic ? rng.Range(9f, 12f) : k == Kind.Truck ? 15f : 7.5f;
                float d = k == Kind.Picnic ? rng.Range(6f, 9f) : k == Kind.Truck ? 4f : 1.2f;
                float front = k == Kind.Picnic ? rng.Range(12f, 15f) : k == Kind.Truck ? 10f : 14f;
                var lot = TrySite(ctx, rng.Range(zFirst, zFirst + total), -side, front + d * 0.5f, w, d, k);
                if (lot != null) { if (k == Kind.Billboard) lot.flatten = false; ctx.lots.Add(lot); }
            }
        }

        // ------------------------------------------------------------------ site check

        private static bool RoadAt(Ctx ctx, float localZ, out Vector3 center, out Vector3 right, out Vector3 fwd)
        {
            Transform ct = ctx.chunk.transform, st = ctx.spline.transform;
            float z0 = Mathf.Clamp(localZ, 0f, ctx.chunk.chunkLength - 2f);
            Vector3 w0 = ct.TransformPoint(new Vector3(0f, 0f, z0));
            Vector3 w1 = ct.TransformPoint(new Vector3(0f, 0f, z0 + 2f));
            center = st.TransformPoint(ctx.spline.GetPointAtZ(st.InverseTransformPoint(w0).z));
            Vector3 c1 = st.TransformPoint(ctx.spline.GetPointAtZ(st.InverseTransformPoint(w1).z));
            fwd = c1 - center;
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 0.0001f) fwd = ct.forward;
            fwd.Normalize();
            right = Vector3.Cross(Vector3.up, fwd).normalized;
            return true;
        }

        /// <summary>A lot beside the road (null if the ground is bad or something is in the way).</summary>
        private static Lot TrySite(Ctx ctx, float localZ, float side, float lateral, float w, float d, Kind kind, bool ignoreLots = false)
        {
            if (localZ < 6f || localZ > ctx.chunk.chunkLength - 6f) { ctx.rejects[0]++; return null; }
            RoadAt(ctx, localZ, out Vector3 center, out Vector3 right, out Vector3 fwd);
            return TrySiteAt(ctx, center, right, side, lateral, w, d, kind, ignoreLots, 8f);
        }

        /// <summary>
        /// A lot beside any axis (main road point, side street point, road end): <paramref name="center"/> on the axis,
        /// <paramref name="right"/> across it; the lot faces the axis and keeps <paramref name="roadClear"/> from it.
        /// </summary>
        private static Lot TrySiteAt(Ctx ctx, Vector3 center, Vector3 right, float side, float lateral, float w, float d, Kind kind,
            bool ignoreLots, float roadClear)
        {
            Vector3 p = center + right * side * lateral;
            Vector3 cl = ctx.chunk.transform.InverseTransformPoint(p);
            if (cl.z < 6f || cl.z > ctx.chunk.chunkLength - 6f || Mathf.Abs(cl.x) > Chunk.HalfWidth - 20f) { ctx.rejects[0]++; return null; }
            if (!WorldPopulator.Ground(ctx.chunk, p, center, out Vector3 g)) { ctx.rejects[1]++; return null; }

            Vector3 toRoad = -right * side;
            Quaternion rot = Quaternion.LookRotation(toRoad);
            Vector3 ax = rot * Vector3.right, az = rot * Vector3.forward;

            // Corners on reasonably level ground (the pad gets flattened, but not a hillside)
            if (ctx.terrain != null)
            {
                float lo = float.MaxValue, hi = float.MinValue;
                for (int i = 0; i < 4; i++)
                {
                    Vector3 c = g + ax * ((i & 1) == 0 ? -0.5f : 0.5f) * w + az * ((i & 2) == 0 ? -0.5f : 0.5f) * d;
                    float y = ctx.terrain.SampleHeight(c) + ctx.terrain.transform.position.y;
                    lo = Mathf.Min(lo, y);
                    hi = Mathf.Max(hi, y);
                    if (ctx.chunk.IsInRiver(c, 1f)) { ctx.rejects[3]++; return null; }
                }
                if (hi - lo > 4f) { ctx.rejects[2]++; return null; }
            }

            float reach = Mathf.Max(w, d) * 0.5f;
            if (RoadBarricade.NearLine(ctx.chunk, g, reach + 10f)) { ctx.rejects[4]++; return null; }
            if (SafeZone.Near(g, 45f + reach)) { ctx.rejects[5]++; return null; }
            foreach (var poi in ctx.pois)
                if (Flat(poi - g).sqrMagnitude < (reach + 24f) * (reach + 24f)) { ctx.rejects[6]++; return null; }
            foreach (var wreck in ctx.wrecks)
                if (Inside(g, ax, az, w * 0.5f + 2.5f, d * 0.5f + 2.5f, wreck)) { ctx.rejects[7]++; return null; }
            if (!ignoreLots)
            {
                foreach (var other in ctx.lots)
                {
                    if (!other.register) continue;
                    if (Overlap(g, ax, az, w * 0.5f + 1f, d * 0.5f + 1f, other)) { ctx.rejects[8]++; return null; }
                }
            }
            // Side roads (streets, tracks) stay open: centre, corners and edge midpoints
            if (ctx.chunk.sideRoads.Count > 0)
            {
                for (int i = 0; i < 9; i++)
                {
                    float fx = (i % 3 - 1) * 0.5f, fz = (i / 3 - 1) * 0.5f;
                    if (ctx.chunk.NearSideRoad(g + ax * fx * w + az * fz * d, 1.5f)) { ctx.rejects[9]++; return null; }
                }
            }

            // Pads sit at about road height: no deep cut or tall fill next to the asphalt
            g.y = Mathf.Clamp(g.y, center.y - 2f, center.y + 1.2f);
            return new Lot
            {
                kind = kind, pos = g, rot = rot, w = w, d = d, lateral = lateral, roadClear = roadClear,
                seed = ctx.rng.Range(int.MinValue, int.MaxValue)
            };
        }

        private static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }

        private static bool Inside(Vector3 c, Vector3 ax, Vector3 az, float hw, float hd, Vector3 p)
        {
            Vector3 d = p - c;
            return Mathf.Abs(Vector3.Dot(d, ax)) < hw && Mathf.Abs(Vector3.Dot(d, az)) < hd;
        }

        /// <summary>Two oriented rectangles overlap (separating axis test on the four axes).</summary>
        private static bool Overlap(Vector3 c, Vector3 ax, Vector3 az, float hw, float hd, Lot o)
        {
            Vector3 bx = o.rot * Vector3.right, bz = o.rot * Vector3.forward;
            float ow = o.w * 0.5f, od = o.d * 0.5f;
            Vector3 t = Flat(o.pos - c);
            foreach (var axis in new[] { ax, az, bx, bz })
            {
                float ra = hw * Mathf.Abs(Vector3.Dot(ax, axis)) + hd * Mathf.Abs(Vector3.Dot(az, axis));
                float rb = ow * Mathf.Abs(Vector3.Dot(bx, axis)) + od * Mathf.Abs(Vector3.Dot(bz, axis));
                if (Mathf.Abs(Vector3.Dot(t, axis)) > ra + rb) return false;
            }
            return true;
        }

        // ------------------------------------------------------------------ terrain / vegetation

        private static void Flatten(Ctx ctx, Lot lot)
        {
            if (ctx.terrain == null) return;
            // Keep the circles off the road (the asphalt is a mesh lying on the conformed terrain)
            float maxR = lot.lateral - lot.roadClear;
            if (maxR < 1.5f) return;
            float r = Mathf.Min(lot.d * 0.5f + 1.5f, maxR);
            float blend = Mathf.Clamp(lot.lateral - r - (lot.roadClear - 0.5f), 0.5f, 4f);
            int count = Mathf.Max(1, Mathf.CeilToInt(lot.w / (1.5f * r)));
            Vector3 ax = lot.rot * Vector3.right;
            for (int i = 0; i < count; i++)
            {
                float t = count == 1 ? 0.5f : i / (count - 1f);
                float x = Mathf.Lerp(-lot.w * 0.5f + r * 0.7f, lot.w * 0.5f - r * 0.7f, t);
                if (count == 1) x = 0f;
                RoadTerrainAdapter.FlattenTerrainArea(ctx.terrain, lot.pos + ax * x, r, blend, lot.pos.y);
            }
        }

        /// <summary>Removes trees / bushes (vegetation objects and terrain trees) inside a lot.</summary>
        private static void ClearVegetation(Ctx ctx, Lot lot, float margin)
        {
            Chunk chunk = ctx.chunk;
            Vector3 ax = lot.rot * Vector3.right, az = lot.rot * Vector3.forward;
            float hw = lot.w * 0.5f + margin, hd = lot.d * 0.5f + margin;
            foreach (var t in ctx.vegetation)
            {
                if (t == null) continue;
                for (int i = t.childCount - 1; i >= 0; i--)
                {
                    Transform child = t.GetChild(i);
                    if (Inside(lot.pos, ax, az, hw, hd, child.position)) Object.Destroy(child.gameObject);
                }
            }

            var terrain = chunk.ChunkTerrain;
            if (terrain == null || terrain.terrainData == null || terrain.terrainData.treeInstanceCount == 0) return;
            var data = terrain.terrainData;
            var trees = data.treeInstances;
            var kept = new List<TreeInstance>(trees.Length);
            Vector3 size = data.size, origin = terrain.transform.position;
            foreach (var tree in trees)
                if (!Inside(lot.pos, ax, az, hw, hd, Vector3.Scale(tree.position, size) + origin)) kept.Add(tree);
            if (kept.Count != trees.Length) data.SetTreeInstances(kept.ToArray(), true);
        }

        // ------------------------------------------------------------------ build dispatch

        private static void BuildLot(Ctx ctx, Lot lot, Transform parent)
        {
            var rng = new SeededRandom(lot.seed);
            var s = SurvivalSettings.Instance;
            switch (lot.kind)
            {
                case Kind.House:
                case Kind.Burned:
                {
                    var b = Shell(parent, lot, rng, "House", 1, rng.Range(2.7f, 3.0f), rng.Range(1.1f, 1.4f), rng.Chance(0.55f));
                    Furniture(b, lot, rng, 2);
                    LootIn(b, lot, rng, s.houseLoot, lot.burned ? 0.15f : lot.damage > 0.4f ? 0.2f : 0.5f);
                    if (lot.damage < 0.4f && rng.Chance(0.12f)) Plan(b, lot, rng, Plans.Kind.Recipe);
                    break;
                }
                case Kind.TwoStory:
                {
                    var b = Shell(parent, lot, rng, "House_TwoStory", 2, 2.9f, 1.2f, rng.Chance(0.5f));
                    Furniture(b, lot, rng, 3);
                    LootIn(b, lot, rng, s.houseLoot, lot.damage > 0.4f ? 0.25f : 0.55f);
                    if (lot.damage < 0.4f && rng.Chance(0.12f)) Plan(b, lot, rng, Plans.Kind.Recipe);
                    break;
                }
                case Kind.Shop:
                case Kind.Market:
                {
                    var b = Shell(parent, lot, rng, lot.kind == Kind.Market ? "Market" : "Shop", 1, 3.6f, rng.Range(2.8f, 4f), false);
                    Shelves(b, lot, rng);
                    LootIn(b, lot, rng, s.gasStationLoot != null ? s.gasStationLoot : s.houseLoot, lot.kind == Kind.Market ? 0.85f : 0.6f);
                    if (lot.kind == Kind.Market) LootIn(b, lot, rng, s.houseLoot, 0.4f);
                    if (rng.Chance(0.15f)) Plan(b, lot, rng, Plans.Kind.Recipe);
                    if (lot.kind == Kind.Market) RoofSign(b, lot, rng);
                    break;
                }
                case Kind.Apartment:
                {
                    var b = Shell(parent, lot, rng, "Apartment", 3, 3f, 1.6f, false);
                    Furniture(b, lot, rng, 3);
                    LootIn(b, lot, rng, s.houseLoot, 0.5f);
                    LootIn(b, lot, rng, s.houseLoot, 0.3f);
                    break;
                }
                case Kind.Garage:
                {
                    var b = Shell(parent, lot, rng, "Garage", 1, 4f, rng.Range(3.8f, 4.6f), false);
                    if (rng.Chance(0.6f)) Car(b, new Vector3(0f, 0f, -0.3f), Quaternion.identity, rng, lot.seed);
                    Block(b, new Vector3(-lot.w * 0.5f + 0.7f, 0.45f, -lot.d * 0.5f + 1.4f), new Vector3(0.9f, 0.9f, 2.2f), new Color(0.35f, 0.3f, 0.25f));
                    LootIn(b, lot, rng, s.wreckLoot != null ? s.wreckLoot : s.houseLoot, 0.45f);
                    if (rng.Chance(0.25f)) Plan(b, lot, rng, Plans.Kind.VehicleMod);
                    break;
                }
                case Kind.Rubble: RubbleLot(parent, lot, rng, s); break;
                case Kind.FencedLot: FencedLot(parent, lot, rng); break;
                case Kind.LotWreck:
                {
                    var root = Root(parent, lot, "Lot_Wreck");
                    Car(root, new Vector3(rng.Range(-2f, 2f), 0f, rng.Range(-1.5f, 1.5f)), Quaternion.Euler(0f, rng.Range(0f, 360f), 0f), rng, lot.seed);
                    break;
                }
                case Kind.GasStation:
                {
                    // Shop + canopy + pumps (Buildings.GasStation); its root centre is the canopy, shop at -6 m
                    Vector3 p = lot.pos + lot.rot * new Vector3(0f, 0f, 1.5f);
                    var gas = Buildings.GasStation(p, lot.rot, parent, rng);
                    gas.name = "POI_GasStation";
                    if (rng.Chance(0.6f))
                        PlanPickup.Create(gas.transform, gas.transform.TransformPoint(new Vector3(0f, 0.16f, 5f)), Plans.Kind.VehicleMod, rng.Range(int.MinValue, int.MaxValue));
                    break;
                }
                case Kind.WaterTower: WaterTower(parent, lot, rng); break;
                case Kind.StreetLamp: StreetLamp(parent, lot, rng); break;
                case Kind.TownSign: Sign(parent, lot, rng, new Color(0.9f, 0.9f, 0.86f), 2.2f, 1.1f, 2f); break;
                case Kind.Diner:
                {
                    var b = Shell(parent, lot, rng, "Diner", 1, 3.4f, 1.8f, false);
                    DinerTables(b, lot, rng);
                    LootIn(b, lot, rng, s.houseLoot, 0.8f);
                    LootIn(b, lot, rng, s.houseLoot, 0.35f);
                    RoofSign(b, lot, rng);
                    break;
                }
                case Kind.Motel: Motel(parent, lot, rng, s); break;
                case Kind.Toilets:
                {
                    var b = Shell(parent, lot, rng, "Toilets", 1, 2.6f, 1f, false);
                    Block(b, new Vector3(0f, 1.3f, -0.2f), new Vector3(0.12f, 2.6f, lot.d - 0.8f), new Color(0.75f, 0.75f, 0.72f));
                    break;
                }
                case Kind.Picnic: Picnic(parent, lot, rng); break;
                case Kind.Truck: Truck(parent, lot, rng); break;
                case Kind.Billboard: Sign(parent, lot, rng, RandomPaint(rng), 7f, 3.2f, 4.2f); break;
                case Kind.Apron: Apron(parent, lot, rng, ctx); break;
            }
        }

        // ------------------------------------------------------------------ building shell

        private static readonly Color[] WallPaints =
        {
            new Color(0.78f, 0.74f, 0.64f), new Color(0.82f, 0.80f, 0.74f), new Color(0.80f, 0.72f, 0.50f),
            new Color(0.62f, 0.34f, 0.26f), new Color(0.55f, 0.62f, 0.66f), new Color(0.66f, 0.70f, 0.58f),
            new Color(0.70f, 0.60f, 0.52f)
        };
        private static readonly Color[] RoofPaints =
        {
            new Color(0.55f, 0.24f, 0.16f), new Color(0.30f, 0.30f, 0.32f), new Color(0.40f, 0.28f, 0.22f),
            new Color(0.25f, 0.30f, 0.33f)
        };
        private static readonly Color Foundation = new Color(0.45f, 0.43f, 0.40f);
        private static readonly Color FloorColor = new Color(0.40f, 0.36f, 0.31f);
        private static readonly Color WindowColor = new Color(0.08f, 0.10f, 0.12f);

        private static Color RandomPaint(SeededRandom rng)
        {
            Color c = WallPaints[rng.Range(0, WallPaints.Length)];
            return Color.Lerp(c, new Color(0.45f, 0.42f, 0.38f), rng.Range(0f, 0.35f)); // weathered
        }

        private static Transform Root(Transform parent, Lot lot, string name)
        {
            var root = new GameObject(name).transform;
            root.SetParent(parent, true);
            root.position = lot.pos;
            root.rotation = lot.rot;
            return root;
        }

        private static GameObject Block(Transform root, Vector3 local, Vector3 size, Color color, bool collider = true)
        {
            return SurvivalPrimitives.Part(PrimitiveType.Cube, root, local, size, color, collider);
        }

        /// <summary>
        /// Walls (door gap in the front +Z wall), floors, windows, roof. damage 0..1: lower / missing wall pieces,
        /// holes in upper floors, no roof above ~0.45, rubble inside.
        /// </summary>
        private static Transform Shell(Transform parent, Lot lot, SeededRandom rng, string name, int floors, float storyH, float doorW, bool gable)
        {
            var root = Root(parent, lot, name);
            float w = lot.w, d = lot.d, hw = w * 0.5f, hd = d * 0.5f, damage = lot.damage;
            Color wall = lot.burned ? new Color(0.16f, 0.15f, 0.14f) : RandomPaint(rng);
            Color roof = lot.burned ? new Color(0.08f, 0.08f, 0.08f) : RoofPaints[rng.Range(0, RoofPaints.Length)];
            Color trim = Color.Lerp(wall, Color.black, 0.25f);
            const float t = 0.22f;

            Block(root, new Vector3(0f, -1.2f, 0f), new Vector3(w + 0.3f, 2.5f, d + 0.3f), Foundation);
            Block(root, new Vector3(0f, 0.05f, 0f), new Vector3(w, 0.1f, d), FloorColor);

            float doorRoom = Mathf.Max(0f, hw - doorW * 0.5f - 0.8f);
            float doorX = doorRoom > 0f ? rng.Range(-doorRoom, doorRoom) : 0f;
            if (lot.kind == Kind.Shop || lot.kind == Kind.Market || lot.kind == Kind.Garage || lot.kind == Kind.Diner) doorX = rng.Range(-0.5f, 0.5f);

            for (int f = 0; f < floors; f++)
            {
                float y0 = f * storyH;
                float dmg = Mathf.Clamp01(damage * (1f + 0.45f * f));
                if (f > 0 && damage > 0.85f && rng.Chance(0.6f)) break; // upper floors gone
                var gaps = f == 0 ? new[] { new Vector2(doorX, doorW) } : null;
                WallRun(root, new Vector3(-hw, y0, hd), new Vector3(hw, y0, hd), storyH, t, gaps, dmg, rng, wall, true);    // front
                WallRun(root, new Vector3(-hw, y0, -hd), new Vector3(hw, y0, -hd), storyH, t, null, dmg, rng, wall, true);  // back
                WallRun(root, new Vector3(-hw, y0, -hd), new Vector3(-hw, y0, hd), storyH, t, null, dmg, rng, wall, false); // left
                WallRun(root, new Vector3(hw, y0, -hd), new Vector3(hw, y0, hd), storyH, t, null, dmg, rng, wall, false);   // right

                // Lintel above the door
                if (f == 0 && storyH > 2.4f && dmg < 0.6f)
                    Block(root, new Vector3(doorX, (2.2f + storyH) * 0.5f, hd), new Vector3(doorW, storyH - 2.2f, t), wall);

                // Upper floor slab
                if (f > 0)
                {
                    if (dmg < 0.5f) Block(root, new Vector3(0f, y0, 0f), new Vector3(w - 0.1f, 0.2f, d - 0.1f), FloorColor);
                    else Block(root, new Vector3(0f, y0, -hd * 0.5f), new Vector3(w - 0.1f, 0.2f, hd), FloorColor);
                }

                // Windows (dark panes on the outside of the wall)
                if (dmg < 0.55f)
                {
                    float wy = y0 + Mathf.Min(1.55f, storyH * 0.55f);
                    for (float x = -hw + 1.4f; x <= hw - 1.4f; x += 2.8f)
                    {
                        if (f == 0 && Mathf.Abs(x - doorX) < doorW * 0.5f + 0.9f) continue;
                        Block(root, new Vector3(x, wy, hd + t * 0.5f + 0.02f), new Vector3(1.1f, 1.2f, 0.04f), WindowColor, false);
                        if (rng.Chance(0.8f)) Block(root, new Vector3(x, wy, -hd - t * 0.5f - 0.02f), new Vector3(1.1f, 1.2f, 0.04f), WindowColor, false);
                    }
                    if (d >= 6f)
                    {
                        Block(root, new Vector3(-hw - t * 0.5f - 0.02f, wy, 0f), new Vector3(0.04f, 1.2f, 1.1f), WindowColor, false);
                        Block(root, new Vector3(hw + t * 0.5f + 0.02f, wy, 0f), new Vector3(0.04f, 1.2f, 1.1f), WindowColor, false);
                    }
                }
            }

            // Roof
            float top = floors * storyH;
            if (damage < 0.3f)
            {
                if (gable)
                {
                    float a = 26f, run = hd + 0.35f;
                    float slab = run / Mathf.Cos(a * Mathf.Deg2Rad);
                    float rise = run * Mathf.Tan(a * Mathf.Deg2Rad);
                    var front = Block(root, new Vector3(0f, top + rise * 0.5f + 0.1f, run * 0.5f - 0.17f), new Vector3(w + 0.6f, 0.16f, slab), roof);
                    front.transform.localRotation = Quaternion.Euler(a, 0f, 0f);
                    var back = Block(root, new Vector3(0f, top + rise * 0.5f + 0.1f, -run * 0.5f + 0.17f), new Vector3(w + 0.6f, 0.16f, slab), roof);
                    back.transform.localRotation = Quaternion.Euler(-a, 0f, 0f);
                    // gable ends, roughly
                    Block(root, new Vector3(-hw, top + rise * 0.3f, 0f), new Vector3(t, rise * 0.6f, d * 0.6f), wall);
                    Block(root, new Vector3(hw, top + rise * 0.3f, 0f), new Vector3(t, rise * 0.6f, d * 0.6f), wall);
                }
                else
                {
                    Block(root, new Vector3(0f, top + 0.12f, 0f), new Vector3(w + 0.5f, 0.24f, d + 0.5f), roof);
                    Block(root, new Vector3(0f, top + 0.45f, hd + 0.2f), new Vector3(w + 0.5f, 0.45f, 0.15f), trim);
                }
            }
            else if (damage < 0.45f)
            {
                // Partly caved in
                float keep = rng.Range(0.45f, 0.7f);
                Block(root, new Vector3(0f, top + 0.12f, -hd + d * keep * 0.5f), new Vector3(w + 0.5f, 0.24f, d * keep), roof);
            }

            // Rubble inside ruined buildings
            if (damage > 0.3f)
            {
                int pieces = rng.Range(2, 3 + Mathf.RoundToInt(damage * 6f));
                Color rubble = Color.Lerp(wall, new Color(0.35f, 0.33f, 0.3f), 0.5f);
                for (int i = 0; i < pieces; i++)
                {
                    float sz = rng.Range(0.4f, 1.4f);
                    var r = Block(root, new Vector3(rng.Range(-hw + 0.8f, hw - 0.8f), sz * 0.3f, rng.Range(-hd + 0.8f, hd - 1.6f)),
                        new Vector3(sz * rng.Range(0.8f, 2f), sz * 0.6f, sz), rubble, sz > 0.8f);
                    r.transform.localRotation = Quaternion.Euler(rng.Range(-15f, 15f), rng.Range(0f, 360f), rng.Range(-15f, 15f));
                }
            }
            return root;
        }

        /// <summary>
        /// One wall from a to b (local, at floor level). gaps: (centre along the wall measured on local X for front /
        /// back walls, width). Damaged walls are cut into pieces of different height, some missing.
        /// </summary>
        private static void WallRun(Transform root, Vector3 a, Vector3 b, float h, float t, Vector2[] gaps, float damage, SeededRandom rng, Color color, bool alongX)
        {
            float from = alongX ? a.x : a.z, to = alongX ? b.x : b.z;
            var spans = new List<Vector2> { new Vector2(from, to) };
            if (gaps != null)
            {
                foreach (var g in gaps)
                {
                    float g0 = g.x - g.y * 0.5f, g1 = g.x + g.y * 0.5f;
                    var next = new List<Vector2>();
                    foreach (var s in spans)
                    {
                        if (g1 <= s.x || g0 >= s.y) { next.Add(s); continue; }
                        if (g0 > s.x) next.Add(new Vector2(s.x, g0));
                        if (g1 < s.y) next.Add(new Vector2(g1, s.y));
                    }
                    spans = next;
                }
            }

            foreach (var s in spans)
            {
                float len = s.y - s.x;
                if (len < 0.05f) continue;
                int pieces = damage < 0.05f ? 1 : Mathf.Max(1, Mathf.RoundToInt(len / rng.Range(1.6f, 3f)));
                float step = len / pieces;
                for (int i = 0; i < pieces; i++)
                {
                    if (pieces > 1 && rng.Chance(damage * 0.3f)) continue; // missing piece
                    float ph = rng.Chance(damage) ? h * rng.Range(0.2f, 0.85f) : h;
                    float mid = s.x + step * (i + 0.5f);
                    Vector3 c = alongX ? new Vector3(mid, a.y + ph * 0.5f, a.z) : new Vector3(a.x, a.y + ph * 0.5f, mid);
                    Vector3 size = alongX ? new Vector3(step + t, ph, t) : new Vector3(t, ph, step);
                    Block(root, c, size, color);
                }
            }
        }

        // ------------------------------------------------------------------ contents

        private static void Furniture(Transform b, Lot lot, SeededRandom rng, int max)
        {
            float hw = lot.w * 0.5f, hd = lot.d * 0.5f;
            int count = rng.Range(1, max + 1);
            for (int i = 0; i < count; i++)
            {
                Vector3 size = new Vector3(rng.Range(0.8f, 2f), rng.Range(0.6f, 1f), rng.Range(0.6f, 1.2f));
                Vector3 p = new Vector3(rng.Range(-hw + 1f, hw - 1f), size.y * 0.5f + 0.1f, rng.Range(-hd + 0.8f, -0.4f));
                Block(b, p, size, Color.Lerp(new Color(0.42f, 0.3f, 0.2f), new Color(0.3f, 0.3f, 0.33f), rng.Value));
            }
            if (lot.w > 7.5f && lot.damage < 0.7f && rng.Chance(0.6f))
            {
                // inner wall splitting two rooms (leaves a passage)
                float x = rng.Range(-hw * 0.4f, hw * 0.4f);
                Block(b, new Vector3(x, 1.4f, -hd * 0.4f), new Vector3(0.15f, 2.8f, lot.d * 0.55f), Color.Lerp(new Color(0.7f, 0.68f, 0.62f), Color.gray, 0.3f));
            }
        }

        private static void Shelves(Transform b, Lot lot, SeededRandom rng)
        {
            float hw = lot.w * 0.5f, hd = lot.d * 0.5f;
            Color shelf = new Color(0.5f, 0.5f, 0.52f);
            int rows = Mathf.Max(1, Mathf.FloorToInt((lot.w - 3f) / 2.6f));
            for (int i = 0; i < rows; i++)
            {
                if (rng.Chance(lot.damage * 0.5f)) continue;
                float x = -hw + 1.8f + i * 2.6f;
                var s = Block(b, new Vector3(x, 0.8f, -hd * 0.15f), new Vector3(0.6f, 1.6f, lot.d * 0.45f), shelf);
                if (rng.Chance(lot.damage)) s.transform.localRotation = Quaternion.Euler(0f, 0f, rng.Range(-70f, 70f)); // knocked over
            }
            Block(b, new Vector3(hw - 1.6f, 0.5f, hd - 1.6f), new Vector3(2.2f, 1f, 0.7f), new Color(0.45f, 0.38f, 0.3f)); // counter
        }

        private static void DinerTables(Transform b, Lot lot, SeededRandom rng)
        {
            float hw = lot.w * 0.5f, hd = lot.d * 0.5f;
            Color table = new Color(0.55f, 0.18f, 0.15f), seat = new Color(0.35f, 0.12f, 0.1f);
            int count = rng.Range(3, 6);
            for (int i = 0; i < count; i++)
            {
                float x = Mathf.Lerp(-hw + 1.5f, hw - 1.5f, count == 1 ? 0.5f : i / (count - 1f));
                float z = hd - 1.6f;
                if (Mathf.Abs(x) < 1.4f) continue; // the door
                Block(b, new Vector3(x, 0.75f, z), new Vector3(1.2f, 0.08f, 0.8f), table);
                Block(b, new Vector3(x, 0.38f, z), new Vector3(0.15f, 0.75f, 0.15f), table);
                Block(b, new Vector3(x, 0.45f, z + 0.75f), new Vector3(1.2f, 0.5f, 0.45f), seat);
                Block(b, new Vector3(x, 0.45f, z - 0.75f), new Vector3(1.2f, 0.5f, 0.45f), seat);
            }
            Block(b, new Vector3(0f, 0.55f, -hd + 1.5f), new Vector3(lot.w * 0.6f, 1.1f, 0.7f), new Color(0.5f, 0.45f, 0.4f)); // counter
        }

        private static void LootIn(Transform b, Lot lot, SeededRandom rng, LootTable table, float chance)
        {
            if (table == null || !rng.Chance(chance)) return;
            float hw = lot.w * 0.5f, hd = lot.d * 0.5f;
            var local = new Vector3(rng.Chance(0.5f) ? -hw + 0.9f : hw - 0.9f, 0.3f, -hd + 0.7f);
            Buildings.Chest(b, local, table, rng, lot.pos);
        }

        private static void Plan(Transform b, Lot lot, SeededRandom rng, Plans.Kind kind)
        {
            Vector3 p = b.TransformPoint(new Vector3(rng.Range(-lot.w * 0.3f, lot.w * 0.3f), 0.08f, rng.Range(-lot.d * 0.3f, 0f)));
            PlanPickup.Create(b, p, kind, rng.Range(int.MinValue, int.MaxValue));
        }

        private static void RoofSign(Transform b, Lot lot, SeededRandom rng)
        {
            if (lot.damage > 0.45f) return;
            float top = 3.6f;
            foreach (var x in new[] { -1.4f, 1.4f })
                Block(b, new Vector3(x, top + 0.7f, lot.d * 0.5f - 0.6f), new Vector3(0.12f, 1.4f, 0.12f), new Color(0.3f, 0.3f, 0.3f), false);
            var board = Block(b, new Vector3(0f, top + 1.5f, lot.d * 0.5f - 0.6f), new Vector3(4.2f, 1.1f, 0.15f), RandomPaint(rng), false);
            if (rng.Chance(0.3f)) board.transform.localRotation = Quaternion.Euler(0f, 0f, rng.Range(-14f, 14f)); // hanging loose
        }

        // ------------------------------------------------------------------ lots without a building

        private static void RubbleLot(Transform parent, Lot lot, SeededRandom rng, SurvivalSettings s)
        {
            var root = Root(parent, lot, "Ruin_Collapsed");
            float hw = lot.w * 0.5f, hd = lot.d * 0.5f;
            Color wall = Color.Lerp(RandomPaint(rng), new Color(0.35f, 0.33f, 0.3f), 0.4f);
            Block(root, new Vector3(0f, -1.1f, 0f), new Vector3(lot.w, 2.4f, lot.d), Foundation);
            // A corner still standing
            float cx = rng.Chance(0.5f) ? -hw : hw, cz = rng.Chance(0.5f) ? -hd : hd;
            float h1 = rng.Range(1.2f, 3f), h2 = rng.Range(0.8f, 2.6f);
            Block(root, new Vector3(cx * 0.6f, h1 * 0.5f, cz), new Vector3(lot.w * 0.4f, h1, 0.22f), wall);
            Block(root, new Vector3(cx, h2 * 0.5f, cz * 0.6f), new Vector3(0.22f, h2, lot.d * 0.4f), wall);
            int pieces = rng.Range(6, 12);
            for (int i = 0; i < pieces; i++)
            {
                float sz = rng.Range(0.5f, 1.8f);
                var r = Block(root, new Vector3(rng.Range(-hw, hw), sz * 0.25f, rng.Range(-hd, hd)), new Vector3(sz * rng.Range(1f, 2.2f), sz * 0.5f, sz), wall, sz > 0.9f);
                r.transform.localRotation = Quaternion.Euler(rng.Range(-20f, 20f), rng.Range(0f, 360f), rng.Range(-20f, 20f));
            }
            if (s.houseLoot != null && rng.Chance(0.12f))
                Buildings.Chest(root, new Vector3(cx * 0.7f, 0.3f, cz * 0.7f), s.houseLoot, rng, lot.pos);
        }

        private static void FencedLot(Transform parent, Lot lot, SeededRandom rng)
        {
            var root = Root(parent, lot, "Lot_Fenced");
            float hw = lot.w * 0.5f, hd = lot.d * 0.5f;
            Color wood = Color.Lerp(new Color(0.4f, 0.32f, 0.22f), new Color(0.35f, 0.35f, 0.35f), rng.Value);
            float gate = rng.Range(-hw * 0.5f, hw * 0.5f);
            for (int side = 0; side < 4; side++)
            {
                bool alongX = side < 2;
                float fixedC = side == 0 ? hd : side == 1 ? -hd : side == 2 ? -hw : hw;
                float from = alongX ? -hw : -hd, to = alongX ? hw : hd;
                for (float p = from; p <= to + 0.01f; p += 2.4f)
                {
                    if (side == 0 && Mathf.Abs(p - gate) < 1.6f) continue;
                    if (rng.Chance(0.15f)) continue;
                    Vector3 pos = alongX ? new Vector3(p, 0.55f, fixedC) : new Vector3(fixedC, 0.55f, p);
                    var post = Block(root, pos, new Vector3(0.12f, 1.1f, 0.12f), wood);
                    if (rng.Chance(0.2f)) post.transform.localRotation = Quaternion.Euler(rng.Range(-25f, 25f), 0f, rng.Range(-25f, 25f));
                    if (p + 2.4f <= to + 0.01f && rng.Chance(0.75f) && !(side == 0 && Mathf.Abs(p + 1.2f - gate) < 1.6f))
                    {
                        Vector3 rail = alongX ? new Vector3(p + 1.2f, 0.8f, fixedC) : new Vector3(fixedC, 0.8f, p + 1.2f);
                        Block(root, rail, alongX ? new Vector3(2.4f, 0.08f, 0.05f) : new Vector3(0.05f, 0.08f, 2.4f), wood, false);
                    }
                }
            }
            // Junk inside
            int junk = rng.Range(0, 4);
            for (int i = 0; i < junk; i++)
            {
                float sz = rng.Range(0.4f, 1.1f);
                Block(root, new Vector3(rng.Range(-hw + 1f, hw - 1f), sz * 0.5f, rng.Range(-hd + 1f, hd - 1f)), Vector3.one * sz, new Color(0.32f, 0.28f, 0.24f));
            }
        }

        private static void WaterTower(Transform parent, Lot lot, SeededRandom rng)
        {
            var root = Root(parent, lot, "WaterTower");
            Color steel = Color.Lerp(new Color(0.5f, 0.52f, 0.55f), new Color(0.45f, 0.3f, 0.2f), rng.Range(0.1f, 0.6f));
            float h = rng.Range(9f, 13f);
            foreach (var x in new[] { -2f, 2f })
                foreach (var z in new[] { -2f, 2f })
                    Block(root, new Vector3(x, h * 0.5f, z), new Vector3(0.3f, h, 0.3f), steel);
            Block(root, new Vector3(0f, h * 0.45f, 0f), new Vector3(4.2f, 0.15f, 0.15f), steel, false);
            Block(root, new Vector3(0f, h * 0.45f, 0f), new Vector3(0.15f, 0.15f, 4.2f), steel, false);
            SurvivalPrimitives.Part(PrimitiveType.Cylinder, root, new Vector3(0f, h + 1.8f, 0f), new Vector3(5.5f, 1.8f, 5.5f), steel);
            SurvivalPrimitives.Part(PrimitiveType.Cylinder, root, new Vector3(0f, h + 3.7f, 0f), new Vector3(4.5f, 0.15f, 4.5f), Color.Lerp(steel, Color.black, 0.3f), false);
            Block(root, new Vector3(0f, h * 0.5f + 0.9f, 2.25f), new Vector3(0.5f, h + 1.8f, 0.06f), Color.Lerp(steel, Color.black, 0.35f), false); // ladder
        }

        private static void StreetLamp(Transform parent, Lot lot, SeededRandom rng)
        {
            var root = Root(parent, lot, "StreetLamp");
            Color pole = new Color(0.32f, 0.33f, 0.35f);
            float h = 6.5f;
            var stand = new GameObject("Pole").transform;
            stand.SetParent(root, false);
            SurvivalPrimitives.Part(PrimitiveType.Cylinder, stand, new Vector3(0f, h * 0.5f, 0f), new Vector3(0.18f, h * 0.5f, 0.18f), pole);
            Block(stand, new Vector3(0f, h, 0.9f), new Vector3(0.12f, 0.12f, 1.8f), pole, false);
            Block(stand, new Vector3(0f, h - 0.15f, 1.7f), new Vector3(0.4f, 0.18f, 0.6f), new Color(0.6f, 0.6f, 0.55f), false);
            float roll = lot.damage;
            if (roll < 0.12f) stand.localRotation = Quaternion.Euler(rng.Range(60f, 85f) * (rng.Chance(0.5f) ? -1f : 1f), 0f, rng.Range(-20f, 20f)); // fallen
            else if (roll < 0.3f) stand.localRotation = Quaternion.Euler(rng.Range(-12f, 12f), 0f, rng.Range(-12f, 12f));      // leaning
        }

        /// <summary>Board on two posts facing the road (town sign, billboard).</summary>
        private static void Sign(Transform parent, Lot lot, SeededRandom rng, Color board, float width, float height, float postH)
        {
            var root = Root(parent, lot, lot.kind == Kind.Billboard ? "Billboard" : "TownSign");
            Color post = new Color(0.3f, 0.3f, 0.3f);
            foreach (var x in new[] { -width * 0.35f, width * 0.35f })
                Block(root, new Vector3(x, (postH + height) * 0.5f, 0f), new Vector3(0.18f, postH + height, 0.18f), post);
            var b = Block(root, new Vector3(0f, postH + height * 0.5f, 0.1f), new Vector3(width, height, 0.12f), board);
            Block(root, new Vector3(0f, postH + height * 0.5f, 0.17f), new Vector3(width * 0.7f, height * 0.25f, 0.02f), Color.Lerp(board, Color.black, 0.6f), false);
            if (rng.Chance(0.25f)) b.transform.localRotation = Quaternion.Euler(0f, 0f, rng.Range(-8f, 8f));
        }

        // ------------------------------------------------------------------ rest stop pieces

        private static void Motel(Transform parent, Lot lot, SeededRandom rng, SurvivalSettings s)
        {
            var root = Root(parent, lot, "Motel");
            int rooms = Mathf.Max(2, lot.rooms);
            float w = lot.w, d = lot.d, hw = w * 0.5f, hd = d * 0.5f, h = 2.8f, room = w / rooms;
            Color wall = RandomPaint(rng), roof = RoofPaints[rng.Range(0, RoofPaints.Length)];
            Color door = Color.Lerp(RandomPaint(rng), Color.black, 0.3f);
            Block(root, new Vector3(0f, -1.2f, 0f), new Vector3(w + 0.3f, 2.5f, d + 0.3f), Foundation);
            Block(root, new Vector3(0f, 0.05f, 0f), new Vector3(w, 0.1f, d), FloorColor);

            var gaps = new Vector2[rooms];
            for (int i = 0; i < rooms; i++) gaps[i] = new Vector2(-hw + room * (i + 0.5f) + room * 0.2f, 1f);
            float dmg = lot.damage;
            WallRun(root, new Vector3(-hw, 0f, hd), new Vector3(hw, 0f, hd), h, 0.22f, gaps, dmg, rng, wall, true);
            WallRun(root, new Vector3(-hw, 0f, -hd), new Vector3(hw, 0f, -hd), h, 0.22f, null, dmg, rng, wall, true);
            WallRun(root, new Vector3(-hw, 0f, -hd), new Vector3(-hw, 0f, hd), h, 0.22f, null, dmg, rng, wall, false);
            WallRun(root, new Vector3(hw, 0f, -hd), new Vector3(hw, 0f, hd), h, 0.22f, null, dmg, rng, wall, false);
            for (int i = 0; i < rooms; i++)
            {
                float cx = -hw + room * (i + 0.5f);
                if (i > 0) Block(root, new Vector3(-hw + room * i, h * 0.5f, 0f), new Vector3(0.15f, h, d), wall);
                Block(root, new Vector3(gaps[i].x, 2.5f, hd), new Vector3(1f, 0.6f, 0.22f), wall);                       // above the door
                Block(root, new Vector3(cx - room * 0.2f, 1.5f, hd + 0.13f), new Vector3(1f, 1f, 0.04f), WindowColor, false);
                if (!rng.Chance(dmg)) Block(root, new Vector3(gaps[i].x + 0.45f, 1.05f, hd + 0.35f), new Vector3(0.06f, 2.1f, 0.9f), door).transform.localRotation = Quaternion.Euler(0f, rng.Range(-60f, 0f), 0f); // door ajar
                Block(root, new Vector3(cx + room * 0.15f, 0.35f, -hd + 1.2f), new Vector3(1.6f, 0.5f, 2f), new Color(0.6f, 0.58f, 0.55f)); // bed
                if (s.houseLoot != null && rng.Chance(0.35f))
                    Buildings.Chest(root, new Vector3(cx - room * 0.3f, 0.3f, -hd + 0.6f), s.houseLoot, rng, lot.pos);
            }
            if (dmg < 0.45f)
            {
                Block(root, new Vector3(0f, h + 0.12f, 0.6f), new Vector3(w + 0.5f, 0.24f, d + 1.7f), roof); // overhang over the walkway
                foreach (var x in new[] { -hw, hw })
                    Block(root, new Vector3(x, h * 0.5f, hd + 1.2f), new Vector3(0.15f, h, 0.15f), new Color(0.35f, 0.35f, 0.35f));
            }
        }

        private static void Picnic(Transform parent, Lot lot, SeededRandom rng)
        {
            var root = Root(parent, lot, "Picnic");
            float hw = lot.w * 0.5f, hd = lot.d * 0.5f;
            Color wood = new Color(0.45f, 0.33f, 0.22f);
            int tables = rng.Range(2, 5);
            for (int i = 0; i < tables; i++)
            {
                var t = new GameObject("Table").transform;
                t.SetParent(root, false);
                t.localPosition = new Vector3(rng.Range(-hw + 1.5f, hw - 1.5f), 0f, rng.Range(-hd + 1.5f, hd - 1.5f));
                t.localRotation = Quaternion.Euler(0f, rng.Range(-25f, 25f) + (rng.Chance(0.3f) ? 90f : 0f), 0f);
                Block(t, new Vector3(0f, 0.75f, 0f), new Vector3(1.8f, 0.07f, 0.8f), wood);
                Block(t, new Vector3(0f, 0.45f, 0.65f), new Vector3(1.8f, 0.06f, 0.3f), wood);
                Block(t, new Vector3(0f, 0.45f, -0.65f), new Vector3(1.8f, 0.06f, 0.3f), wood);
                foreach (var x in new[] { -0.75f, 0.75f }) Block(t, new Vector3(x, 0.37f, 0f), new Vector3(0.08f, 0.74f, 1.5f), wood, false);
                if (rng.Chance(0.2f)) t.localRotation *= Quaternion.Euler(0f, 0f, 80f); // tipped over
            }
            SurvivalPrimitives.Part(PrimitiveType.Cylinder, root, new Vector3(hw - 0.8f, 0.45f, hd - 0.8f), new Vector3(0.6f, 0.45f, 0.6f), new Color(0.25f, 0.32f, 0.25f));
            if (rng.Chance(0.5f))
            {
                // shelter roof over one table
                foreach (var x in new[] { -2f, 2f })
                    foreach (var z in new[] { -1.6f, 1.6f })
                        Block(root, new Vector3(x, 1.25f, z), new Vector3(0.15f, 2.5f, 0.15f), wood);
                Block(root, new Vector3(0f, 2.6f, 0f), new Vector3(5f, 0.15f, 4f), RoofPaints[rng.Range(0, RoofPaints.Length)]);
            }
        }

        private static void Truck(Transform parent, Lot lot, SeededRandom rng)
        {
            // Lot +Z faces the road; the truck is parked along it (its length on local X)
            var root = Root(parent, lot, "Truck");
            float dir = rng.Chance(0.5f) ? 1f : -1f;
            Color trailer = Color.Lerp(RandomPaint(rng), new Color(0.75f, 0.75f, 0.75f), 0.4f);
            Color cabColor = RandomPaint(rng);
            Block(root, new Vector3(-dir * 1.5f, 2.3f, 0f), new Vector3(11f, 2.9f, 2.5f), trailer);
            var cab = Block(root, new Vector3(dir * 5.4f, 1.7f, 0f), new Vector3(2.4f, 2.4f, 2.4f), cabColor);
            cab.name = "WreckCollider";
            cab.AddComponent<WreckSalvage>().seed = SeededRandom.Combine(lot.seed, "truck");
            Block(root, new Vector3(dir * 6.62f, 2.2f, 0f), new Vector3(0.04f, 1f, 2f), WindowColor, false);
            foreach (var x in new[] { -5f, -3.6f, 2.5f, 5.4f })
                foreach (var z in new[] { -1.1f, 1.1f })
                {
                    var wheel = SurvivalPrimitives.Part(PrimitiveType.Cylinder, root, new Vector3(dir * x, 0.5f, z), new Vector3(1f, 0.18f, 1f), new Color(0.07f, 0.07f, 0.07f), false);
                    wheel.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                }
            if (rng.Chance(0.35f)) root.localRotation *= Quaternion.Euler(0f, 0f, rng.Range(-6f, 6f)); // sagging tyres
        }

        private static void Apron(Transform parent, Lot lot, SeededRandom rng, Ctx ctx)
        {
            var root = Root(parent, lot, "Parking");
            Color asphalt = Color.Lerp(new Color(0.2f, 0.2f, 0.21f), new Color(0.32f, 0.31f, 0.29f), rng.Value);
            // Thick slab so slopes do not show its edge in the air
            Block(root, new Vector3(0f, -0.45f, 0f), new Vector3(lot.w, 1f, lot.d), asphalt);
            if (lot.rooms == 1)
            {
                Color line = new Color(0.8f, 0.8f, 0.75f);
                for (float x = -lot.w * 0.5f + 1.5f; x < lot.w * 0.5f - 1f; x += 2.8f)
                    Block(root, new Vector3(x, 0.06f, -lot.d * 0.5f + 2.6f), new Vector3(0.12f, 0.02f, 4.8f), line, false);
            }
            int cars = rng.Range(0, 3);
            for (int i = 0; i < cars; i++)
            {
                float x = rng.Range(-lot.w * 0.5f + 2.5f, lot.w * 0.5f - 2.5f);
                Car(root, new Vector3(x, 0.05f, -lot.d * 0.5f + 2.6f), Quaternion.Euler(0f, rng.Range(-8f, 8f), 0f), rng, SeededRandom.Combine(lot.seed, i));
            }

            // A trader now and then waits at the stop
            var s = ctx.chunk.settlementSettings;
            if (s != null && rng.Chance(s.stopTraderChance) && !TraderPlaced(ctx))
            {
                Vector3 p = root.TransformPoint(new Vector3(rng.Range(-lot.w * 0.3f, lot.w * 0.3f), 0.05f, lot.d * 0.5f - 1.2f));
                Trader.Create(p, lot.rot, root, rng.Range(int.MinValue, int.MaxValue));
                ctx.traderPlaced = true;
            }
        }

        private static bool TraderPlaced(Ctx ctx) => ctx.traderPlaced;

        /// <summary>Simple rusty car (salvageable like the road wrecks).</summary>
        private static void Car(Transform parent, Vector3 local, Quaternion rot, SeededRandom rng, int seed)
        {
            var car = new GameObject("Car").transform;
            car.SetParent(parent, false);
            car.localPosition = local;
            car.localRotation = rot;
            Color paint = Color.Lerp(RandomPaint(rng), new Color(0.42f, 0.23f, 0.11f), rng.Range(0.2f, 0.7f));
            var body = Block(car, new Vector3(0f, 0.62f, 0f), new Vector3(1.8f, 0.7f, 4.2f), paint);
            body.name = "WreckCollider";
            body.AddComponent<WreckSalvage>().seed = SeededRandom.Combine(seed, "settlement_car");
            Block(car, new Vector3(0f, 1.22f, -0.25f), new Vector3(1.6f, 0.55f, 2.1f), Color.Lerp(paint, Color.black, 0.2f), false);
            Block(car, new Vector3(0f, 1.22f, 0.83f), new Vector3(1.5f, 0.45f, 0.04f), WindowColor, false);
            bool flat = rng.Chance(0.5f);
            foreach (var x in new[] { -0.85f, 0.85f })
                foreach (var z in new[] { -1.35f, 1.35f })
                {
                    if (flat && rng.Chance(0.3f)) continue; // wheel gone
                    var wheel = SurvivalPrimitives.Part(PrimitiveType.Cylinder, car, new Vector3(x, 0.33f, z), new Vector3(0.65f, 0.12f, 0.65f), new Color(0.06f, 0.06f, 0.06f), false);
                    wheel.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
                }
        }

        // ------------------------------------------------------------------ people

        /// <summary>Towns are where people are: now and then a group holds one of the buildings.</summary>
        private static void SpawnOccupants(Ctx ctx, Transform root)
        {
            var manager = ctx.chunk.Manager;
            if (manager != null && ctx.chunk.ChunkIndex == manager.StartChunkIndex) return; // nobody in the start chunk
            var rng = new SeededRandom(ctx.chunk.ChunkSeed).SubStream("settlement_people");
            if (!rng.Chance(0.45f)) return;
            var prefabs = SurvivalSettings.Instance.humanEnemyPrefabs;
            if (prefabs == null || prefabs.Length == 0) return;
            var candidates = new List<Lot>();
            foreach (var lot in ctx.lots) if (IsBuilding(lot.kind)) candidates.Add(lot);
            if (candidates.Count == 0) return;
            var home = candidates[rng.Range(0, candidates.Count)];

            int baseCount = DifficultyCurve.EarlyGame ? rng.Range(1, 3) : rng.Range(2, 4);
            int count = DifficultyCurve.CapHumans(DifficultyCurve.ScaleEnemyCount(baseCount, home.pos));
            Vector3 front = home.rot * Vector3.forward;
            var squad = EnemySquad.Create(SquadKind.Occupants, home.pos); // Enemy_AI_Deep_Plan.md 12.1
            for (int i = 0; i < count; i++)
            {
                var prefab = DifficultyCurve.PickEnemyPrefab(rng, prefabs, home.pos);
                if (prefab == null) continue;
                // One inside, the rest in front of the building
                Vector3 p = i == 0
                    ? home.pos + home.rot * new Vector3(rng.Range(-home.w * 0.25f, home.w * 0.25f), 0f, rng.Range(-home.d * 0.2f, home.d * 0.2f))
                    : home.pos + front * (home.d * 0.5f + rng.Range(2f, 5f)) + home.rot * Vector3.right * rng.Range(-home.w * 0.5f, home.w * 0.5f);
                p = SurvivalPrimitives.Ground(p + Vector3.up * 1.5f);
                var go = Object.Instantiate(prefab, p + Vector3.up * 0.1f, Quaternion.LookRotation(front), root);
                var enemy = go.GetComponent<EnemyController>();
                if (enemy == null) continue;
                squad.Add(enemy);
                DifficultyCurve.ApplyToEnemy(enemy, home.pos);
                enemy.initialState = i == 0 ? EnemyController.EnemyState.Idle : EnemyController.EnemyState.Patrolling;
            }
        }

        // ------------------------------------------------------------------ desert scatter

        /// <summary>Çorak Arazi: dry shrubs and rocks instead of the forest's vegetation.</summary>
        public static void DesertScatter(Chunk chunk, RoadSpline spline, Transform root)
        {
            var rng = new SeededRandom(chunk.ChunkSeed).SubStream("desert_scatter");
            var container = new GameObject("DesertScatter").transform;
            container.SetParent(root, false);
            Color shrubA = new Color(0.46f, 0.42f, 0.25f), shrubB = new Color(0.38f, 0.32f, 0.2f);
            Color rockA = new Color(0.55f, 0.42f, 0.32f), rockB = new Color(0.45f, 0.4f, 0.36f);

            int shrubs = rng.Range(60, 90);
            for (int i = 0; i < shrubs; i++)
            {
                if (!WorldPopulator.TryRoadsidePoint(chunk, spline, rng, 9f, 300f, out Vector3 p, out _)) continue;
                if (rng.Value > chunk.DesertScatterWeight(p)) continue; // thins out across a transition chunk
                int parts = rng.Range(1, 4);
                Color c = Color.Lerp(shrubA, shrubB, rng.Value);
                for (int k = 0; k < parts; k++)
                {
                    float sz = rng.Range(0.5f, 1.3f);
                    var go = SurvivalPrimitives.Part(PrimitiveType.Sphere, container, Vector3.zero, new Vector3(sz, sz * rng.Range(0.45f, 0.7f), sz), c, false);
                    go.transform.position = p + new Vector3(rng.Range(-0.6f, 0.6f), sz * 0.15f, rng.Range(-0.6f, 0.6f));
                }
            }

            int rocks = rng.Range(28, 44);
            for (int i = 0; i < rocks; i++)
            {
                if (!WorldPopulator.TryRoadsidePoint(chunk, spline, rng, 10f, 320f, out Vector3 p, out _)) continue;
                if (rng.Value > chunk.DesertScatterWeight(p)) continue;
                Vector3 size = new Vector3(rng.Range(0.5f, 2.6f), rng.Range(0.4f, 1.6f), rng.Range(0.5f, 2.4f));
                var go = SurvivalPrimitives.Part(PrimitiveType.Cube, container, Vector3.zero, size, Color.Lerp(rockA, rockB, rng.Value), Mathf.Max(size.x, size.z) > 1.2f);
                go.transform.position = p + Vector3.up * size.y * 0.2f;
                go.transform.rotation = Quaternion.Euler(rng.Range(-12f, 12f), rng.Range(0f, 360f), rng.Range(-12f, 12f));
            }
        }
    }
}
