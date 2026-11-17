using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace EndlessCombat.AI
{
    public enum CoverHeight { Low, High }

    public class CoverPoint
    {
        public Vector3 position;
        /// <summary>Out of the obstacle (the enemy's back is toward -normal).</summary>
        public Vector3 normal;
        public CoverHeight height;
        public bool peekLeft, peekRight, peekOver;
        public Vector3 peekLeftPos, peekRightPos;
        public EnemyController reservedBy;
        public long cell;
        public bool temporary;
    }

    /// <summary>
    /// The tactical map (Enemy_AI_Deep_Plan.md 15): cover points around solid objects, made when the local NavMesh
    /// finishes baking, spread over frames (MaxCollidersPerFrame). Points sit on the NavMesh, 0.6 m clear of the
    /// obstacle, with left / right peek spots. Chunk generators are not touched.
    /// </summary>
    public static class CoverMap
    {
        // ---- tuning (panel page "Düşman") ----
        public static int MaxPoints = 600;
        public static int MaxCollidersPerFrame = 25;
        public static float Spacing = 1.5f;
        public static float Clearance = 0.6f;
        public static float CellSize = 8f;
        public static float FriendRadius = 2.5f;
        public static bool DebugDraw;

        private static readonly List<CoverPoint> Points = new List<CoverPoint>();
        private static readonly Dictionary<long, List<CoverPoint>> Grid = new Dictionary<long, List<CoverPoint>>();
        private static readonly Queue<Collider> Pending = new Queue<Collider>();
        private static readonly Collider[] Buffer = new Collider[1024];
        private static readonly RaycastHit[] Hits = new RaycastHit[16];
        private static bool _hooked;
        private static Bounds _bounds;
        private static int _mask = -1;

        public static int Count => Points.Count;
        public static bool Building => Pending.Count > 0;
        public static IReadOnlyList<CoverPoint> All => Points;

        private static int Mask
        {
            get
            {
                if (_mask == -1) _mask = LayerMask.GetMask("Default", "Environment");
                return _mask;
            }
        }

        public static void Hook()
        {
            if (_hooked) return;
            _hooked = true;
            LocalNavMeshBuilder.BakeFinished += Rebuild;
        }

        public static void Clear()
        {
            Points.Clear();
            Grid.Clear();
            Pending.Clear();
        }

        /// <summary>Starts a rebuild for the box (spread over frames by Tick).</summary>
        public static void Rebuild(Bounds bounds)
        {
            // Keep reservations of points still inside the new box
            var keep = new List<CoverPoint>();
            foreach (var p in Points) if (p.reservedBy != null && bounds.Contains(p.position)) keep.Add(p);
            Points.Clear();
            Grid.Clear();
            foreach (var p in keep) Add(p);
            Pending.Clear();
            _bounds = bounds;
            int n = Physics.OverlapBoxNonAlloc(bounds.center, bounds.extents, Buffer, Quaternion.identity, Mask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++) if (Usable(Buffer[i])) Pending.Enqueue(Buffer[i]);
        }

        private static bool Usable(Collider c)
        {
            if (c == null || c.isTrigger || c is TerrainCollider || c is CharacterController) return false;
            if (c.attachedRigidbody != null && !c.attachedRigidbody.isKinematic) return false;
            if (c.GetComponentInParent<Combat.CombatBody>() != null || c.GetComponentInParent<EnemyController>() != null) return false;
            if (c.GetComponentInParent<EndlessSurvival.Vehicle.VehicleController>() != null) return false;
            Bounds b = c.bounds;
            if (b.size.y < 0.9f) return false;
            float h = Mathf.Max(b.size.x, b.size.z);
            return h >= 0.6f && h <= 40f;
        }

        /// <summary>Called every frame by AISystem.</summary>
        public static void Tick()
        {
            int done = 0;
            while (Pending.Count > 0 && done < MaxCollidersPerFrame && Points.Count < MaxPoints)
            {
                var c = Pending.Dequeue();
                done++;
                if (c == null) continue;
                Sample(c);
            }
            if (Points.Count >= MaxPoints) Pending.Clear();
            if (DebugDraw) Draw();
        }

        private static void Sample(Collider c)
        {
            Bounds b = c.bounds;
            Vector3 center = b.center;
            float ground = b.min.y;
            // Walk the horizontal outline of the bounds, push each sample out of the collider
            float w = b.size.x, d = b.size.z;
            float perimeter = 2f * (w + d);
            int count = Mathf.Clamp(Mathf.RoundToInt(perimeter / Spacing), 4, 40);
            for (int i = 0; i < count; i++)
            {
                float t = (i + 0.5f) / count * perimeter;
                Vector3 edge;
                if (t < w) edge = new Vector3(b.min.x + t, 0f, b.min.z - 1f);
                else if (t < w + d) edge = new Vector3(b.max.x + 1f, 0f, b.min.z + (t - w));
                else if (t < 2f * w + d) edge = new Vector3(b.max.x - (t - w - d), 0f, b.max.z + 1f);
                else edge = new Vector3(b.min.x - 1f, 0f, b.max.z - (t - 2f * w - d));
                edge.y = ground + 0.5f;
                Vector3 surface = c.ClosestPoint(edge);
                Vector3 n = edge - surface;
                n.y = 0f;
                if (n.sqrMagnitude < 0.0001f) continue;
                n.Normalize();
                Vector3 p = surface + n * Clearance;
                if (!NavMesh.SamplePosition(p, out NavMeshHit hit, 0.8f, NavMesh.AllAreas)) continue;
                if (TooClose(hit.position, 1f)) continue;
                var cp = new CoverPoint { position = hit.position, normal = n };
                // Height: how tall is the obstacle right behind the point
                float top = b.max.y - hit.position.y;
                cp.height = top < 1.4f ? CoverHeight.Low : CoverHeight.High;
                cp.peekOver = cp.height == CoverHeight.Low;
                Vector3 side = Vector3.Cross(Vector3.up, n);
                cp.peekLeftPos = hit.position + side * 1.2f;
                cp.peekRightPos = hit.position - side * 1.2f;
                cp.peekLeft = OpenToward(cp.peekLeftPos, -n);
                cp.peekRight = OpenToward(cp.peekRightPos, -n);
                Add(cp);
                if (Points.Count >= MaxPoints) return;
            }
        }

        private static bool OpenToward(Vector3 from, Vector3 dir)
        {
            Vector3 o = from + Vector3.up * 1.5f;
            return Physics.RaycastNonAlloc(o, dir, Hits, 6f, Mask, QueryTriggerInteraction.Ignore) == 0;
        }

        private static long CellOf(Vector3 p)
        {
            long x = Mathf.FloorToInt(p.x / CellSize);
            long z = Mathf.FloorToInt(p.z / CellSize);
            return (x << 32) ^ (z & 0xffffffffL);
        }

        private static void Add(CoverPoint p)
        {
            p.cell = CellOf(p.position);
            Points.Add(p);
            if (!Grid.TryGetValue(p.cell, out var list)) Grid[p.cell] = list = new List<CoverPoint>();
            list.Add(p);
        }

        private static bool TooClose(Vector3 p, float r)
        {
            long c = CellOf(p);
            if (!Grid.TryGetValue(c, out var list)) return false;
            foreach (var q in list) if ((q.position - p).sqrMagnitude < r * r) return true;
            return false;
        }

        /// <summary>Points within radius of a position (no allocation beyond the given list).</summary>
        public static void Near(Vector3 pos, float radius, List<CoverPoint> into)
        {
            into.Clear();
            int r = Mathf.CeilToInt(radius / CellSize);
            long cx = Mathf.FloorToInt(pos.x / CellSize), cz = Mathf.FloorToInt(pos.z / CellSize);
            float r2 = radius * radius;
            for (long x = cx - r; x <= cx + r; x++)
                for (long z = cz - r; z <= cz + r; z++)
                {
                    long key = (x << 32) ^ (z & 0xffffffffL);
                    if (!Grid.TryGetValue(key, out var list)) continue;
                    foreach (var p in list)
                    {
                        Vector3 d = p.position - pos;
                        d.y = 0f;
                        if (d.sqrMagnitude <= r2) into.Add(p);
                    }
                }
        }

        // ------------------------------------------------------------------ queries (15.3)

        /// <summary>Ray from the threat's eye (1.5 m) to the point (Low 0.9, High 1.5): blocked = protected.</summary>
        public static bool Protects(CoverPoint p, Vector3 threat)
        {
            float h = p.height == CoverHeight.Low ? 0.9f : 1.5f;
            return Blocked(threat + Vector3.up * 1.5f, p.position + Vector3.up * h);
        }

        /// <summary>Is something solid between "from" (eyes) and pos (chest)? "Player in cover" for the power balance.</summary>
        public static bool IsPositionCovered(Vector3 pos, Vector3 from) => Blocked(from + Vector3.up * 1.5f, pos + Vector3.up * 1.1f);

        public static bool Blocked(Vector3 a, Vector3 b)
        {
            Vector3 d = b - a;
            float dist = d.magnitude;
            if (dist < 0.3f) return false;
            int n = Physics.RaycastNonAlloc(a, d / dist, Hits, dist - 0.25f, Mask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var c = Hits[i].collider;
                if (c == null) continue;
                if (c.attachedRigidbody != null && !c.attachedRigidbody.isKinematic) continue;
                if (EnemyPerception.IsCharacter(c)) continue;
                return true;
            }
            return false;
        }

        private static readonly List<CoverPoint> Candidates = new List<CoverPoint>();
        private static readonly List<float> CandidateCost = new List<float>();

        public static void Reserve(CoverPoint p, EnemyController e)
        {
            Release(e);
            if (p != null) p.reservedBy = e;
        }

        public static void Release(EnemyController e)
        {
            if (e == null) return;
            foreach (var p in Points) if (p.reservedBy == e) p.reservedBy = null;
        }

        /// <summary>Best protected point (15.3 score). Null when the map is empty or nothing fits.</summary>
        public static CoverPoint FindCover(EnemyController e, Vector3 threat, float minDist, float idealDist, float maxPath = 25f,
                                           Vector3? progressDir = null, bool hiddenFromThreatOnly = true)
        {
            if (Points.Count == 0) return null;
            Vector3 me = e.transform.position;
            Near(me, maxPath, Candidates);
            // cheap pre sort: straight distance + distance error
            CandidateCost.Clear();
            for (int i = Candidates.Count - 1; i >= 0; i--)
            {
                var p = Candidates[i];
                if (p.reservedBy != null && p.reservedBy != e && !p.reservedBy.IsDead) { Candidates.RemoveAt(i); continue; }
                float toThreat = Flat(p.position - threat).magnitude;
                if (toThreat < minDist) { Candidates.RemoveAt(i); continue; }
                if (progressDir.HasValue)
                {
                    Vector3 off = Flat(p.position - me);
                    float along = Vector3.Dot(off, progressDir.Value);
                    if (along < 4f || along > 16f) { Candidates.RemoveAt(i); continue; }
                }
            }
            Candidates.Sort((a, b) => Cheap(a, me, threat, idealDist).CompareTo(Cheap(b, me, threat, idealDist)));

            CoverPoint best = null;
            float bestScore = float.MinValue;
            int tested = 0;
            float friendR = FriendRadius * (AIMemory.Habit(AIMemory.HabitKind.Explosives) >= 0.5f ? 2f : 1f);
            foreach (var p in Candidates)
            {
                if (tested >= 12) break;
                tested++;
                if (hiddenFromThreatOnly && !Protects(p, threat)) continue;
                float path = e.Mover != null ? e.Mover.PathLength(p.position) : Flat(p.position - me).magnitude * 1.4f;
                if (path > maxPath) continue;
                float toThreat = Flat(p.position - threat).magnitude;
                float score = 10f - path * 0.25f - Mathf.Abs(toThreat - idealDist) * 0.3f;
                bool canShoot = PeekSpot(p, threat, out _);
                score += canShoot ? 2f : -2f;
                if (FriendNear(p.position, e, friendR)) score -= 4f;
                Vector3 toT = Flat(threat - p.position).normalized;
                if (Vector3.Angle(-p.normal, toT) > 60f) score -= 3f;
                if (p.height == CoverHeight.Low) score -= 1f;
                var sq = e.Squad;
                if (sq != null && sq.objective != null && sq.Tactic == SquadTactic.DefendObjective
                    && Flat(p.position - sq.objective.position).magnitude < 10f) score += 2f;
                if (score > bestScore)
                {
                    bestScore = score;
                    best = p;
                }
            }
            return best;
        }

        private static float Cheap(CoverPoint p, Vector3 me, Vector3 threat, float ideal) =>
            Flat(p.position - me).magnitude + Mathf.Abs(Flat(p.position - threat).magnitude - ideal) * 0.5f;

        private static bool FriendNear(Vector3 pos, EnemyController self, float r)
        {
            foreach (var o in EnemyController.Active)
            {
                if (o == null || o == self || o.IsDead) continue;
                if ((o.transform.position - pos).sqrMagnitude < r * r) return true;
            }
            return false;
        }

        /// <summary>Where to step out from a cover point to shoot at the threat (left / right / over / the point itself).</summary>
        public static bool PeekSpot(CoverPoint p, Vector3 threat, out Vector3 spot, int avoidSide = 0)
        {
            Vector3 t = threat + Vector3.up * 1.1f;
            spot = p.position;
            if (p.peekOver && !Blocked(p.position + Vector3.up * 1.5f, t)) return true;
            if (p.peekLeft && avoidSide != -1 && !Blocked(p.peekLeftPos + Vector3.up * 1.5f, t)) { spot = p.peekLeftPos; return true; }
            if (p.peekRight && avoidSide != 1 && !Blocked(p.peekRightPos + Vector3.up * 1.5f, t)) { spot = p.peekRightPos; return true; }
            if (!Blocked(p.position + Vector3.up * 1.5f, t)) return true;
            return false;
        }

        /// <summary>A flank spot 70-110 degrees off the suppressors' side that sees the threat and is not covered from it.</summary>
        public static CoverPoint FindFlankSpot(EnemyController e, Vector3 threat, Vector3 suppressorCentroid, out int side)
        {
            side = 0;
            if (Points.Count == 0) return null;
            Vector3 baseDir = Flat(suppressorCentroid - threat);
            if (baseDir.sqrMagnitude < 0.01f) baseDir = Flat(e.transform.position - threat);
            baseDir.Normalize();
            Near(threat, 30f, Candidates);
            CoverPoint best = null;
            float bestScore = float.MinValue;
            int tested = 0;
            foreach (var p in Candidates)
            {
                if (p.reservedBy != null && p.reservedBy != e && !p.reservedBy.IsDead) continue;
                Vector3 dir = Flat(p.position - threat);
                float dist = dir.magnitude;
                if (dist < 6f || dist > 28f) continue;
                float ang = Vector3.Angle(baseDir, dir);
                if (ang < 70f || ang > 110f) continue;
                if (tested++ >= 12) break;
                if (!PeekSpot(p, threat, out _)) continue;
                float path = e.Mover != null ? e.Mover.PathLength(p.position) : dist * 1.4f;
                float hidden = e.Mover != null ? e.Mover.RouteHiddenFraction(p.position, threat) : 0.5f;
                float score = 10f - path * 0.15f + hidden * 4f;
                if (score > bestScore)
                {
                    bestScore = score;
                    best = p;
                    side = Vector3.Cross(baseDir, dir).y > 0f ? 1 : -1;
                }
            }
            return best;
        }

        /// <summary>The edge of the threat's cover closest to the threat, seen from "from" (suppressive fire target).</summary>
        public static Vector3? EdgeToward(Vector3 threat, Vector3 from)
        {
            Vector3 a = from + Vector3.up * 1.5f;
            Vector3 b = threat + Vector3.up * 1.1f;
            Vector3 d = b - a;
            float dist = d.magnitude;
            if (dist < 1f) return null;
            int n = Physics.RaycastNonAlloc(a, d / dist, Hits, dist, Mask, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            Vector3? point = null;
            for (int i = 0; i < n; i++)
            {
                if (EnemyPerception.IsCharacter(Hits[i].collider)) continue;
                if (Hits[i].distance < best && dist - Hits[i].distance < 4f)
                {
                    best = Hits[i].distance;
                    point = Hits[i].point + Vector3.up * 0.3f;
                }
            }
            return point;
        }

        private static Vector3 Flat(Vector3 v)
        {
            v.y = 0f;
            return v;
        }

        // ------------------------------------------------------------------ debug

        private static LineRenderer[] _lines;

        private static void Draw()
        {
            foreach (var p in Points)
            {
                Color c = p.reservedBy != null ? Color.red : p.height == CoverHeight.Low ? Color.yellow : Color.green;
                Debug.DrawLine(p.position, p.position + Vector3.up * (p.height == CoverHeight.Low ? 0.9f : 1.5f), c);
                Debug.DrawLine(p.position + Vector3.up * 0.2f, p.position + Vector3.up * 0.2f + p.normal * 0.5f, c);
            }
            AIDebug.DrawCoverPoints(Points);
        }
    }
}
