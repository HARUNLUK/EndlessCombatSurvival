using UnityEngine;

namespace EndlessCombat.Combat
{
    /// <summary>
    /// Melee stats of a weapon. Lives on the same object as a WeaponController whose type is Melee.
    /// The player's fists use the same numbers from CombatContent (no object in the hand).
    /// </summary>
    [RequireComponent(typeof(WeaponController))]
    public class MeleeWeapon : MonoBehaviour
    {
        [Tooltip("Overwritten by the weapon's WeaponDefinition (melee numbers) when it has one")]
        public MeleeStats stats = MeleeStats.Default;

        private void Awake()
        {
            var weapon = GetComponent<WeaponController>();
            if (weapon != null && weapon.Definition != null && weapon.Definition.type == WeaponType.Melee)
                stats = weapon.Definition.melee;
        }
    }

    [System.Serializable]
    public struct MeleeStats
    {
        [Tooltip("Damage per hit")]
        public float damage;
        [Tooltip("Reach in front of the character (m)")]
        public float range;
        [Tooltip("Radius of the hit sphere at the end of the reach (m)")]
        public float radius;
        [Tooltip("Old timing (s). Now only used by Auto category; the real timing comes from the clip sockets in MeleeAnimationSet")]
        public float attackInterval;
        [Tooltip("Old timing (s), not used by the souls-like combat (hit window is in MeleeAnimationSet)")]
        public float hitDelay;
        [Tooltip("Stamina used per swing (used once stamina exists, Package 2.2)")]
        public float staminaCost;
        [Tooltip("Noise radius of a swing / hit for enemy hearing (m)")]
        public float noiseRadius;
        [Tooltip("Push applied to a hit enemy (m/s)")]
        public float knockback;
        [Tooltip("Seconds a hit enemy staggers (cannot attack or move)")]
        public float staggerTime;
        [Tooltip("Animation: true = stab (fast jab), false = swing")]
        public bool stab;
        [Tooltip("Animation set (MeleeAnimationSet). Auto: stab = Short, attack interval 0.85 s or more = TwoHand, else OneHand")]
        public MeleeCategory category;

        /// <summary>Category with Auto resolved. Fists are passed in by the caller (no weapon object).</summary>
        public MeleeCategory ResolveCategory(bool isFists)
        {
            if (isFists) return MeleeCategory.Fists;
            if (category != MeleeCategory.Auto) return category;
            if (stab) return MeleeCategory.Short;
            return attackInterval >= 0.85f ? MeleeCategory.TwoHand : MeleeCategory.OneHand;
        }

        public static MeleeStats Default => new MeleeStats
        {
            damage = 20f, range = 1.4f, radius = 0.6f, attackInterval = 0.7f, hitDelay = 0.3f,
            staminaCost = 10f, noiseRadius = 6f, knockback = 2.5f, staggerTime = 0.5f, stab = false
        };

        public static MeleeStats Fists => new MeleeStats
        {
            damage = 10f, range = 1.1f, radius = 0.5f, attackInterval = 0.5f, hitDelay = 0.2f,
            staminaCost = 6f, noiseRadius = 4f, knockback = 1.5f, staggerTime = 0.35f, stab = true
        };
    }
}
