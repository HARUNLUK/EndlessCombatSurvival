using System;
using UnityEngine;

namespace EndlessCombat.Combat
{
    /// <summary>
    /// Small static seam between the combat code and the survival systems (stamina, character stats, stress).
    /// Combat code only reads these values and raises these events; the survival code sets the delegates.
    /// Every delegate has a neutral default so combat works on its own.
    /// </summary>
    public static class CombatHooks
    {
        /// <summary>Extra aim spread in degrees for the player's firearm (panic, marksmanship, cold).</summary>
        public static Func<float> PlayerSpreadDegrees = () => 0f;

        /// <summary>Multiplier for the player's melee damage (melee and strength stats).</summary>
        public static Func<float> PlayerMeleeDamageMultiplier = () => 1f;

        /// <summary>Multiplier for the player's melee attack interval (lower = faster).</summary>
        public static Func<float> PlayerMeleeIntervalMultiplier = () => 1f;

        /// <summary>Asked before a player melee attack: return false to refuse (not enough stamina).</summary>
        public static Func<float, bool> TrySpendStamina = cost => true;

        /// <summary>Multiplier for how fast enemies build suspicion on the player (stealth stat, clothing).</summary>
        public static Func<float> EnemySuspicionMultiplier = () => 1f;

        /// <summary>Multiplier for the player's movement noise radius (stealth stat, clothing).</summary>
        public static Func<float> PlayerNoiseMultiplier = () => 1f;

        /// <summary>The player's firearm hit a damageable target (target, damage).</summary>
        public static event Action<IDamageable, float> PlayerFirearmHit;

        /// <summary>The player fired a firearm.</summary>
        public static event Action PlayerFired;

        /// <summary>A player melee attack landed on this many targets.</summary>
        public static event Action<int> PlayerMeleeLanded;

        /// <summary>A noise was made in the world (origin, radius). Animals and the stress system listen.</summary>
        public static event Action<Vector3, float> NoiseMade;

        public static void RaisePlayerFirearmHit(IDamageable target, float damage) => PlayerFirearmHit?.Invoke(target, damage);
        public static void RaisePlayerFired() => PlayerFired?.Invoke();
        /// <summary>Any firearm shot (player or enemy), for sounds.</summary>
        public static event Action<Vector3, bool> WeaponFired;
        public static void RaiseWeaponFired(Vector3 position, bool byPlayer) => WeaponFired?.Invoke(position, byPlayer);
        public static void RaisePlayerMeleeLanded(int hits) => PlayerMeleeLanded?.Invoke(hits);
        public static void RaiseNoise(Vector3 origin, float radius) => NoiseMade?.Invoke(origin, radius);
    }

    /// <summary>Things that react to melee hits without being damageable (trees give wood).</summary>
    public interface IMeleeReactive
    {
        void OnMeleeHit(Transform attacker, MeleeStats stats, Vector3 point);
    }
}
