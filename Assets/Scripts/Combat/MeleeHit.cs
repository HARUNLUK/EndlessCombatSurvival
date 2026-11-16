using System.Collections.Generic;
using UnityEngine;
using EndlessCombat.AI;

namespace EndlessCombat.Combat
{
    /// <summary>Overlap sphere hit check shared by the player's and the enemies' melee strikes.</summary>
    public static class MeleeHit
    {
        private static readonly Collider[] Buffer = new Collider[64];
        private static readonly HashSet<object> OneShot = new HashSet<object>();

        /// <summary>One-off strike without an attacker body (old callers). Each target is hit once.</summary>
        public static int Strike(Transform attacker, Vector3 forward, MeleeStats stats, float heightOffset = 1.1f)
        {
            OneShot.Clear();
            return StrikeInternal(attacker, null, forward, stats, stats.staggerTime, OneShot, heightOffset);
        }

        /// <summary>
        /// Strike of a CombatBody during its hit window; called every frame of the window. alreadyHit keeps each
        /// target to one hit per strike. Targets with a CombatBody go through ReceiveMelee (shield, stagger,
        /// stealth kill); same team is skipped; enemies only hit bodies of the other team.
        /// </summary>
        public static int Strike(CombatBody attacker, Vector3 forward, MeleeStats stats, float staggerTime, HashSet<object> alreadyHit, float heightOffset = 1.1f)
        {
            if (attacker == null) return 0;
            return StrikeInternal(attacker.transform, attacker, forward, stats, staggerTime, alreadyHit, heightOffset);
        }

        private static int StrikeInternal(Transform attacker, CombatBody attackerBody, Vector3 forward, MeleeStats stats, float staggerTime,
            HashSet<object> alreadyHit, float heightOffset)
        {
            if (attacker == null) return 0;

            forward.y = 0f;
            if (forward.sqrMagnitude < 0.0001f) forward = attacker.forward;
            forward.Normalize();

            Vector3 center = attacker.position + Vector3.up * heightOffset + forward * Mathf.Max(0.1f, stats.range - stats.radius * 0.5f);
            int count = Physics.OverlapSphereNonAlloc(center, stats.radius, Buffer, ~0, QueryTriggerInteraction.Ignore);
            bool playerSide = attackerBody == null || attackerBody.team == CombatTeam.Player;

            int hits = 0;
            for (int i = 0; i < count; i++)
            {
                Collider col = Buffer[i];
                if (col == null || col.transform.IsChildOf(attacker)) continue;

                Vector3 point = col.ClosestPoint(center);
                var target = col.GetComponentInParent<CombatBody>();

                if (target != null)
                {
                    if (attackerBody != null && target.team == attackerBody.team) continue;
                    if (target.IsDead || !alreadyHit.Add(target)) continue;
                    var result = target.ReceiveMelee(attackerBody, attacker.position, stats, staggerTime, point, forward);
                    if (result != HitResult.Ignored) hits++;
                    continue;
                }

                // Only the player's strikes touch the rest of the world (trees, dummies, animals)
                if (!playerSide) continue;

                var reactive = col.GetComponentInParent<IMeleeReactive>();
                if (reactive != null)
                {
                    if (!alreadyHit.Add(reactive)) continue;
                    reactive.OnMeleeHit(attacker, stats, point);
                    // Not counted: hits train Melee / Strength, and punching a tree over and over must not farm them.
                    // The tree gives its own small training per piece of firewood (ResourceNode.OnMeleeHit).
                    continue;
                }

                IDamageable damageable = col.GetComponentInParent<IDamageable>();
                if (damageable == null || damageable.IsDead || !alreadyHit.Add(damageable)) continue;

                Damage.Deal(damageable, new DamageInfo(stats.damage, DamageType.Melee, point, -forward)
                {
                    SourceDirection = -forward,
                    Source = attacker.gameObject,
                    ByPlayer = playerSide
                });
                hits++;

                var enemy = damageable as EnemyController;
                if (enemy != null && !enemy.IsDead) enemy.Stagger(forward * stats.knockback, staggerTime);
            }
            return hits;
        }
    }
}
