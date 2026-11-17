using System.Collections.Generic;
using UnityEngine;

namespace EndlessCombat.AI
{
    /// <summary>
    /// A squad search (Enemy_AI_Deep_Plan.md 17): hiding places near the centre (cover points not seen from the
    /// centre, the ones along the last movement first) and rings at 8 / 16 / 24 m. Members claim the nearest free
    /// point, stop there 2 s and look around; the point is then clear for everybody.
    /// </summary>
    public class EnemySearch
    {
        public static float[] Rings = { 8f, 16f, 24f };
        public static int MaxPoints = 24;
        public static float LookTime = 2f;

        public class Point
        {
            public Vector3 position;
            public bool cleared;
            public EnemyController claimedBy;
            public float priority;
        }

        public readonly EnemySquad squad;
        public Vector3 center;
        public readonly List<Point> Points = new List<Point>();
        private readonly Dictionary<EnemyController, EnemyController> _pairs = new Dictionary<EnemyController, EnemyController>();
        private static readonly List<CoverPoint> Near = new List<CoverPoint>();

        public EnemySearch(EnemySquad squad, Vector3 center)
        {
            this.squad = squad;
            this.center = center;
            Build();
        }

        public void Recenter(Vector3 c)
        {
            if ((c - center).sqrMagnitude < 9f) return;
            center = c;
            Build();
        }

        private void Build()
        {
            Points.Clear();
            Vector3 heading = squad.Shared.Valid ? squad.Shared.velocity : Vector3.zero;
            heading.y = 0f;
            CoverMap.Near(center, 25f, Near);
            foreach (var c in Near)
            {
                if (Points.Count >= MaxPoints / 2) break;
                // A hiding place: the centre can not see it
                if (!CoverMap.Blocked(center + Vector3.up * 1.5f, c.position + Vector3.up * 1.2f)) continue;
                float pr = 1f;
                if (heading.sqrMagnitude > 0.1f) pr += Mathf.Max(0f, Vector3.Dot(heading.normalized, (c.position - center).normalized)) * 2f;
                Points.Add(new Point { position = c.position, priority = pr });
            }
            float start = Random.Range(0f, 360f);
            foreach (float r in Rings)
            {
                for (int i = 0; i < 6 && Points.Count < MaxPoints; i++)
                {
                    Vector3 p = center + Quaternion.AngleAxis(start + i * 60f + r, Vector3.up) * Vector3.forward * r;
                    if (!EnemyMover.OnNavMesh(p, 3f, out Vector3 s)) continue;
                    Points.Add(new Point { position = s, priority = 1f - r / 40f });
                }
            }
            Points.Sort((a, b) => b.priority.CompareTo(a.priority));
            if (Points.Count == 0) Points.Add(new Point { position = center, priority = 1f });
        }

        public void Tick()
        {
            for (int i = 0; i < Points.Count; i++)
            {
                var c = Points[i].claimedBy;
                if (c != null && (c.IsDead || c.Squad != squad || c.State != EnemyController.EnemyState.Searching)) Points[i].claimedBy = null;
            }
        }

        /// <summary>The nearest free, not yet cleared point for this member (pairSearch: the partner follows 4 m behind).</summary>
        public Point Claim(EnemyController e)
        {
            foreach (var p in Points) if (p.claimedBy == e && !p.cleared) return p;
            Point best = null;
            float bestD = float.MaxValue;
            Vector3 me = e.transform.position;
            foreach (var p in Points)
            {
                if (p.cleared || (p.claimedBy != null && p.claimedBy != e)) continue;
                float d = (p.position - me).sqrMagnitude / Mathf.Max(0.2f, p.priority);
                if (d < bestD) { bestD = d; best = p; }
            }
            if (best != null) best.claimedBy = e;
            return best;
        }

        public void Clear(Point p)
        {
            if (p == null) return;
            p.cleared = true;
            p.claimedBy = null;
        }

        public bool Exhausted
        {
            get
            {
                foreach (var p in Points) if (!p.cleared) return false;
                return true;
            }
        }

        /// <summary>pairSearch: the follower of a leader member (or null).</summary>
        public EnemyController PartnerOf(EnemyController e)
        {
            bool pair = AIDifficulty.For(squad).pairSearch || squad.stealthFound > 0;
            if (!pair) return null;
            if (_pairs.TryGetValue(e, out var lead) && lead != null && !lead.IsDead && lead.State == EnemyController.EnemyState.Searching) return lead;
            _pairs.Remove(e);
            // Odd members follow the previous even one
            int idx = 0;
            EnemyController prev = null;
            foreach (var m in squad.Members)
            {
                if (m == null || m.IsDead || m.State != EnemyController.EnemyState.Searching || squad.RoleOf(m) == SquadRole.Guard) continue;
                if (m == e)
                {
                    if (idx % 2 == 1 && prev != null)
                    {
                        _pairs[e] = prev;
                        return prev;
                    }
                    return null;
                }
                prev = m;
                idx++;
            }
            return null;
        }
    }
}
