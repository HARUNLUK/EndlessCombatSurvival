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
        public MeleeStats stats = MeleeStats.Default;
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
        [Tooltip("Seconds between two attacks")]
        public float attackInterval;
        [Tooltip("Seconds from the start of the swing to the moment it hits")]
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
