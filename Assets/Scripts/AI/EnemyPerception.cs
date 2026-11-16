using UnityEngine;

namespace EndlessCombat.AI
{
    /// <summary>
    /// What an enemy sees (Core_Feel_Plan stage 4: moved out of EnemyController, behaviour unchanged).
    /// Sight: range (crouch and night shorten it), field of view (very close behind is noticed too), and a ray that
    /// must reach the player without hitting cover. Hearing, suspicion and alerting stay in EnemyController.
    /// </summary>
    public static class EnemyPerception
    {
        private static int _mask = -1;

        public static bool CanSee(EnemyController self, Transform target, out float clarity)
        {
            clarity = 0f;
            if (target == null) return false;
            Transform t = self.transform;

            Vector3 eye = t.position + Vector3.up * self.eyeHeight;
            Vector3 targetCenter = target.position + Vector3.up * 1.0f;
            Vector3 toTarget = targetCenter - eye;
            float dist = toTarget.magnitude;

            float range = self.detectionRange;
            var stealth = Combat.PlayerStealthController.Instance;
            if (stealth != null && stealth.IsCrouching) range *= stealth.crouchVisibilityMultiplier;
            var cycle = EndlessSurvival.World.DayNightCycle.Instance;
            if (cycle != null && cycle.IsNight) range *= 0.55f;
            if (dist > range) return false;

            float angle = Vector3.Angle(t.forward, toTarget);
            if (angle > self.fieldOfViewAngle * 0.5f && dist > 1.8f) return false;

            if (!ClearLine(eye, targetCenter, t, target)) return false;

            float distFactor = 1f - Mathf.Clamp01(dist / range);
            clarity = Mathf.Clamp01(distFactor * 1.6f);
            return true;
        }

        /// <summary>
        /// True when the nearest solid thing on the line from a to b is the target (or nothing). Own colliders and
        /// triggers are skipped (the raised arm / weapon would block the ray and make the sight flicker).
        /// </summary>
        public static bool ClearLine(Vector3 a, Vector3 b, Transform self, Transform target)
        {
            if (_mask == -1) _mask = LayerMask.GetMask("Default", "Environment");
            Vector3 d = b - a;
            float dist = d.magnitude;
            if (dist < 0.01f) return true;
            var hits = Physics.RaycastAll(a, d / dist, dist, _mask, QueryTriggerInteraction.Ignore);
            float nearest = float.MaxValue;
            Collider nearestCol = null;
            foreach (var h in hits)
            {
                if (self != null && h.collider.transform.IsChildOf(self)) continue;
                if (h.distance < nearest)
                {
                    nearest = h.distance;
                    nearestCol = h.collider;
                }
            }
            if (nearestCol == null) return true;
            return target != null && (nearestCol.CompareTag("Player") || nearestCol.transform.IsChildOf(target));
        }

        /// <summary>Is the line from the player's eyes to a point blocked by something solid (a cover check).</summary>
        public static bool Covered(Vector3 point, Transform player, Transform self)
        {
            if (_mask == -1) _mask = LayerMask.GetMask("Default", "Environment");
            Vector3 from = player.position + Vector3.up * 1.5f;
            Vector3 d = point - from;
            float dist = d.magnitude;
            if (dist < 0.5f) return false;
            var hits = Physics.RaycastAll(from, d / dist, dist - 0.3f, _mask, QueryTriggerInteraction.Ignore);
            foreach (var h in hits)
            {
                if (h.collider.transform.IsChildOf(player)) continue;
                if (self != null && h.collider.transform.IsChildOf(self)) continue;
                if (h.collider.GetComponentInParent<EnemyController>() != null && h.collider.GetComponentInParent<EnemyController>().HasRaisedShield) return true;
                if (h.collider.GetComponentInParent<Combat.CombatBody>() != null) continue; // people are no cover
                if (h.collider.attachedRigidbody != null && !h.collider.attachedRigidbody.isKinematic) continue;
                return true;
            }
            return false;
        }
    }
}
