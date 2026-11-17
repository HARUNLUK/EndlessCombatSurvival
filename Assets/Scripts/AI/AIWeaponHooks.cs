using UnityEngine;
using EndlessCombat.Combat;

namespace EndlessCombat.AI
{
    /// <summary>
    /// Shots as stimuli (Enemy_AI_Deep_Plan.md 6.2): every shot is a Gunshot with its shooter (player or enemy);
    /// the player's bullets also make a BulletImpact where they land (the bullet's way back points at the shooter)
    /// and a near miss on enemies within 1.5 m of the bullet line (nerve). Called by WeaponController.
    /// </summary>
    public static class AIWeaponHooks
    {
        public static float PistolShotRadius = 45f, RifleShotRadius = 65f;
        public static float ImpactRadius = 10f, NearMissRadius = 1.5f;

        public static void OnFired(WeaponController w, Vector3 origin, Transform shooterRoot)
        {
            if (w == null) return;
            AITarget src = null;
            if (w.IsPlayerWeapon) src = AITarget.Player;
            else if (shooterRoot != null) src = shooterRoot.GetComponentInParent<AITarget>();
            if (src == null) src = w.GetComponentInParent<AITarget>();
            float r = w.Type == WeaponType.Rifle ? RifleShotRadius : PistolShotRadius;
            var stealth = PlayerStealthController.Instance;
            if (w.IsPlayerWeapon && stealth != null) r = Mathf.Max(r * 0.5f, stealth.gunshotNoiseRadius * (w.Type == WeaponType.Rifle ? 1.7f : 1.2f));
            AIStimuli.Raise(new Stimulus
            {
                kind = StimulusKind.Gunshot, position = origin, radius = r, source = src, certainty = 0.8f
            });
        }

        public static void OnPellet(WeaponController w, Vector3 from, Vector3 to, bool hit, Collider hitCollider)
        {
            if (w == null || !w.IsPlayerWeapon) return;
            Vector3 dir = to - from;
            float len = dir.magnitude;
            if (len < 0.01f) return;
            dir /= len;
            EnemyController hitEnemy = hitCollider != null ? EnemyPerception.OwnerOf(hitCollider) : null;
            var list = EnemyController.Active;
            float r2 = NearMissRadius * NearMissRadius;
            for (int i = list.Count - 1; i >= 0; i--)
            {
                var e = i < list.Count ? list[i] : null;
                if (e == null || e.IsDead || e == hitEnemy) continue;
                Vector3 c = e.transform.position + Vector3.up * 1.2f;
                float t = Mathf.Clamp(Vector3.Dot(c - from, dir), 0f, len);
                if ((from + dir * t - c).sqrMagnitude <= r2) e.NearMiss();
            }
            if (hit)
            {
                AIStimuli.Raise(new Stimulus
                {
                    kind = StimulusKind.BulletImpact, position = to, radius = ImpactRadius, source = AITarget.Player,
                    sourceHint = -dir, certainty = 0.5f
                }, false);
            }
        }
    }
}
