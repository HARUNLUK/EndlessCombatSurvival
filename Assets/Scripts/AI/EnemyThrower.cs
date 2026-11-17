using UnityEngine;
using EndlessCombat.Combat;

namespace EndlessCombat.AI
{
    /// <summary>
    /// Grenade and rock throws of enemies (Enemy_AI_Deep_Plan.md 13.8, 13.7): the safety check (distance 8-22 m,
    /// no friend in the blast, the arc is clear, one grenade per squad every 6 s) and the ballistic velocity.
    /// </summary>
    public static class EnemyThrower
    {
        public static float MinDist = 8f, MaxDist = 22f;
        public static float SquadGap = 6f;
        public static readonly Color GrenadeColor = new Color(0.25f, 0.35f, 0.2f);
        public static readonly Color RockColor = new Color(0.45f, 0.42f, 0.38f);
        private static readonly RaycastHit[] Hits = new RaycastHit[8];

        public static float BlastRadius => CombatContent.Instance != null ? CombatContent.Instance.grenadeRadius : 5f;

        public static Vector3 Hand(EnemyController e) => e.transform.position + Vector3.up * 1.6f + e.transform.right * 0.25f;

        /// <summary>All safety rules (13.8). On success returns the launch velocity.</summary>
        public static bool CanThrowGrenade(EnemyController e, Vector3 target, out Vector3 velocity, bool ignoreMinDist = false)
        {
            velocity = Vector3.zero;
            if (e.grenades <= 0) return false;
            var s = e.Squad;
            if (s != null && Time.time - s.lastGrenadeTime < SquadGap) return false;
            Vector3 from = Hand(e);
            float d = Flat(target - e.transform.position).magnitude;
            if ((!ignoreMinDist && d < MinDist) || d > MaxDist) return false;
            float r = BlastRadius;
            if (d < r + 1f) return false;
            foreach (var o in EnemyController.Active)
            {
                if (o == null || o.IsDead) continue;
                if ((o.transform.position - target).sqrMagnitude < (r + 2f) * (r + 2f)) return false;
            }
            return Solve(from, target, out velocity) && ArcClear(from, velocity, target);
        }

        public static bool CanThrowRock(EnemyController e, Vector3 target, out Vector3 velocity)
        {
            velocity = Vector3.zero;
            if (e.rocks <= 0) return false;
            float d = Flat(target - e.transform.position).magnitude;
            if (d < 3f || d > 18f) return false;
            Vector3 from = Hand(e);
            return Solve(from, target + Vector3.up * 1.2f, out velocity, 20f) && ArcClear(from, velocity, target);
        }

        /// <summary>Launch speed for a 35 or 50 degree arc (gravity 9.81).</summary>
        public static bool Solve(Vector3 from, Vector3 to, out Vector3 velocity, float angleDeg = 35f)
        {
            velocity = Vector3.zero;
            Vector3 flat = Flat(to - from);
            float d = flat.magnitude;
            if (d < 0.5f) return false;
            float h = to.y - from.y;
            float g = -Physics.gravity.y;
            foreach (float a in new[] { angleDeg, angleDeg + 15f })
            {
                float th = a * Mathf.Deg2Rad;
                float denom = 2f * Mathf.Cos(th) * Mathf.Cos(th) * (d * Mathf.Tan(th) - h);
                if (denom <= 0.01f) continue;
                float v2 = g * d * d / denom;
                if (v2 <= 0f) continue;
                float v = Mathf.Sqrt(v2);
                if (v > 25f) continue;
                Vector3 dir = flat / d;
                velocity = dir * (v * Mathf.Cos(th)) + Vector3.up * (v * Mathf.Sin(th));
                return true;
            }
            return false;
        }

        /// <summary>Six samples along the arc are joined by linecasts that must not hit anything solid.</summary>
        public static bool ArcClear(Vector3 from, Vector3 velocity, Vector3 target)
        {
            float g = -Physics.gravity.y;
            float flatSpeed = Flat(velocity).magnitude;
            if (flatSpeed < 0.1f) return false;
            float total = Flat(target - from).magnitude / flatSpeed;
            Vector3 prev = from;
            for (int i = 1; i <= 6; i++)
            {
                float t = total * i / 6f * 0.92f;
                Vector3 p = from + velocity * t + 0.5f * Vector3.down * g * t * t;
                Vector3 d = p - prev;
                float len = d.magnitude;
                if (len > 0.01f)
                {
                    int n = Physics.RaycastNonAlloc(prev, d / len, Hits, len, EnemyPerception.Mask, QueryTriggerInteraction.Ignore);
                    for (int k = 0; k < n; k++)
                    {
                        if (EnemyPerception.IsCharacter(Hits[k].collider)) continue;
                        return false;
                    }
                }
                prev = p;
            }
            return true;
        }

        public static void LaunchGrenade(EnemyController e, Vector3 velocity)
        {
            if (e.grenades <= 0) return;
            e.grenades--;
            if (e.Squad != null) e.Squad.lastGrenadeTime = Time.time;
            ThrownProjectile.Launch(ThrownProjectile.Kind.Grenade, Hand(e), velocity, e.transform, GrenadeColor);
            AIDebug.Log(e.Squad, $"{e.DisplayName} bomba attı");
        }

        /// <summary>The thrower was staggered during the windup: the grenade drops at his feet.</summary>
        public static void DropGrenade(EnemyController e)
        {
            if (e.grenades <= 0) return;
            e.grenades--;
            ThrownProjectile.Launch(ThrownProjectile.Kind.Grenade, e.transform.position + Vector3.up * 0.5f + e.transform.forward * 0.3f,
                Vector3.zero, e.transform, GrenadeColor);
            AIDebug.Log(e.Squad, $"{e.DisplayName} bombayı düşürdü");
        }

        public static void LaunchRock(EnemyController e, Vector3 velocity)
        {
            if (e.rocks <= 0) return;
            e.rocks--;
            ThrownProjectile.Launch(ThrownProjectile.Kind.Rock, Hand(e), velocity, e.transform, RockColor);
        }

        private static Vector3 Flat(Vector3 v)
        {
            v.y = 0f;
            return v;
        }
    }
}
