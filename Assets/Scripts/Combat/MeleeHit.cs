using System.Collections.Generic;
using UnityEngine;
using EndlessCombat.AI;

namespace EndlessCombat.Combat
{
    /// <summary>Overlap sphere hit check shared by the player's and the enemies' melee attacks.</summary>
    public static class MeleeHit
    {
        private static readonly Collider[] Buffer = new Collider[64];
        private static readonly HashSet<IDamageable> Hit = new HashSet<IDamageable>();

        /// <summary>
        /// Damages every IDamageable whose collider is inside a sphere in front of the attacker
        /// (each target once). Colliders under the attacker are skipped. Returns the number of targets hit.
        /// </summary>
        public static int Strike(Transform attacker, Vector3 forward, MeleeStats stats, float heightOffset = 1.1f)
        {
            if (attacker == null) return 0;

            forward.y = 0f;
            if (forward.sqrMagnitude < 0.0001f) forward = attacker.forward;
            forward.Normalize();

            Vector3 center = attacker.position + Vector3.up * heightOffset + forward * Mathf.Max(0.1f, stats.range - stats.radius * 0.5f);
            int count = Physics.OverlapSphereNonAlloc(center, stats.radius, Buffer, ~0, QueryTriggerInteraction.Ignore);

            Hit.Clear();
            int hits = 0;
            for (int i = 0; i < count; i++)
            {
                Collider col = Buffer[i];
                if (col == null || col.transform.IsChildOf(attacker)) continue;

                // Non-damageable things that react to hits (trees give wood)
                var reactive = col.GetComponentInParent<IMeleeReactive>();
                if (reactive != null)
                {
                    reactive.OnMeleeHit(attacker, stats, col.ClosestPoint(center));
                    hits++;
                    continue;
                }

                IDamageable target = col.GetComponentInParent<IDamageable>();
                if (target == null || target.IsDead || !Hit.Add(target)) continue;

                Vector3 point = col.ClosestPoint(center);
                target.TakeDamage(stats.damage, point, -forward);
                hits++;

                var enemy = target as EnemyController;
                if (enemy != null && !enemy.IsDead)
                {
                    enemy.Stagger(forward * stats.knockback, stats.staggerTime);
                }
            }
            return hits;
        }
    }
}
