using System.Collections.Generic;
using UnityEngine;

namespace EndlessCombat.AI
{
    /// <summary>
    /// Line of sight helpers (no allocation: RaycastNonAlloc and a collider to owner cache, Enemy_AI_Deep_Plan.md 23).
    /// The sight formula itself is in EnemySenses.
    /// </summary>
    public static class EnemyPerception
    {
        private static int _mask = -1;
        private static readonly RaycastHit[] Hits = new RaycastHit[24];
        private static readonly Dictionary<int, EnemyController> OwnerCache = new Dictionary<int, EnemyController>();
        private static readonly Dictionary<int, bool> CharacterCache = new Dictionary<int, bool>();

        public static int Mask
        {
            get
            {
                if (_mask == -1) _mask = LayerMask.GetMask("Default", "Environment");
                return _mask;
            }
        }

        /// <summary>Old entry point (kept for callers outside the AI): sight of the player with the full formula.</summary>
        public static bool CanSee(EnemyController self, Transform target, out float clarity)
        {
            clarity = 0f;
            if (self == null || self.Senses == null || AITarget.Player == null) return false;
            return self.Senses.CanSee(AITarget.Player, out clarity);
        }

        public static EnemyController OwnerOf(Collider c)
        {
            if (c == null) return null;
            int id = c.GetHashCode();
            if (!OwnerCache.TryGetValue(id, out var e))
            {
                e = c.GetComponentInParent<EnemyController>();
                OwnerCache[id] = e;
            }
            return e;
        }

        /// <summary>A person (player or NPC): people are no cover.</summary>
        public static bool IsCharacter(Collider c)
        {
            if (c == null) return false;
            int id = c.GetHashCode();
            if (!CharacterCache.TryGetValue(id, out bool v))
            {
                v = c.GetComponentInParent<Combat.CombatBody>() != null || c.GetComponentInParent<EnemyController>() != null
                    || c.CompareTag("Player") || c.GetComponentInParent<CharacterController>() != null;
                CharacterCache[id] = v;
            }
            return v;
        }

        /// <summary>
        /// True when the nearest solid thing on the line from a to b is the target (or nothing). Own colliders and
        /// triggers are skipped (the raised arm / weapon would block the ray and make the sight flicker).
        /// </summary>
        public static bool ClearLine(Vector3 a, Vector3 b, Transform self, Transform target)
        {
            Vector3 d = b - a;
            float dist = d.magnitude;
            if (dist < 0.01f) return true;
            int n = Physics.RaycastNonAlloc(a, d / dist, Hits, dist, Mask, QueryTriggerInteraction.Ignore);
            float nearest = float.MaxValue;
            Collider nearestCol = null;
            for (int i = 0; i < n; i++)
            {
                var h = Hits[i];
                if (self != null && h.collider.transform.IsChildOf(self)) continue;
                if (h.distance < nearest)
                {
                    nearest = h.distance;
                    nearestCol = h.collider;
                }
            }
            if (nearestCol == null) return true;
            if (target == null) return false;
            if (nearestCol.transform.IsChildOf(target)) return true;
            // A collider moved by the target's rigidbody (vehicle parts) counts as the target
            var rb = nearestCol.attachedRigidbody;
            if (rb != null && (rb.transform == target || rb.transform.IsChildOf(target) || target.IsChildOf(rb.transform))) return true;
            return target.CompareTag("Player") && nearestCol.CompareTag("Player");
        }

        /// <summary>Is the line from the player's eyes to a point blocked by something solid (a cover check).</summary>
        public static bool Covered(Vector3 point, Transform player, Transform self)
        {
            if (player == null) return false;
            Vector3 from = player.position + Vector3.up * 1.5f;
            Vector3 d = point - from;
            float dist = d.magnitude;
            if (dist < 0.5f) return false;
            int n = Physics.RaycastNonAlloc(from, d / dist, Hits, dist - 0.3f, Mask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var c = Hits[i].collider;
                if (c.transform.IsChildOf(player)) continue;
                if (self != null && c.transform.IsChildOf(self)) continue;
                var owner = OwnerOf(c);
                if (owner != null && owner.HasRaisedShield) return true;
                if (IsCharacter(c)) continue; // people are no cover
                if (c.attachedRigidbody != null && !c.attachedRigidbody.isKinematic) continue;
                return true;
            }
            return false;
        }

        /// <summary>Same as Covered, from any eye point.</summary>
        public static bool CoveredFrom(Vector3 point, Vector3 eye, Transform self)
        {
            Vector3 d = point - eye;
            float dist = d.magnitude;
            if (dist < 0.5f) return false;
            int n = Physics.RaycastNonAlloc(eye, d / dist, Hits, dist - 0.3f, Mask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var c = Hits[i].collider;
                if (self != null && c.transform.IsChildOf(self)) continue;
                var owner = OwnerOf(c);
                if (owner != null && owner.HasRaisedShield) return true;
                if (IsCharacter(c)) continue;
                if (c.attachedRigidbody != null && !c.attachedRigidbody.isKinematic) continue;
                return true;
            }
            return false;
        }

        public static void ClearCaches()
        {
            OwnerCache.Clear();
            CharacterCache.Clear();
        }
    }
}
