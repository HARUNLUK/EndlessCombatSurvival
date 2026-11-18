using System;
using UnityEngine;

namespace EndlessCombat.Audio
{
    /// <summary>
    /// Every moment the game will one day make a sound. Nothing is played today (no sounds in the game, user rule
    /// 2026-11-06): this is only the list of places, so adding sound later is a data job (a SoundBank asset that
    /// maps an id to clips plus one listener on GameSound.Played) instead of searching 260 files.
    /// Add new ids at the end of their group; ids are never stored in save files, so reordering is safe.
    /// </summary>
    public enum SoundId
    {
        None = 0,

        // Firearms and bows
        WeaponFire = 100, WeaponDryFire, WeaponReloadStart, WeaponReloadEnd, WeaponDraw, WeaponHolster,
        BulletImpact, Explosion, GrenadeBounce, ThrowRelease, BowDraw, BowRelease, ArrowImpact,

        // Melee and shield
        MeleeSwing = 200, MeleeHit, ShieldBlock, ShieldBreak,

        // People
        Footstep = 300, PlayerHurt, PlayerDeath, EnemyHurt, EnemyDeath, EnemyShout,

        // Vehicle
        VehicleEnter = 400, VehicleExit, VehicleEngineStart, VehicleEngineStop, VehicleImpact, VehicleEngineLoop,

        // World and survival
        ItemPickup = 500, ContainerOpen, CraftDone, Eat, Drink,

        // Interface
        UiMove = 600, UiConfirm, UiBack, UiOpen, UiClose, Notification
    }

    /// <summary>One sound moment. position is ignored when positional is false (interface sounds).</summary>
    public struct SoundEvent
    {
        public SoundId id;
        public Vector3 position;
        public bool positional;
        /// <summary>0..1, how loud relative to the id's normal level (a crouched step is quieter).</summary>
        public float volume;
        /// <summary>Optional sub kind: weapon name, surface, item name. Lets one id pick different clips.</summary>
        public string variant;
        /// <summary>True when the player made it (some sounds are only heard for the player).</summary>
        public bool byPlayer;
        /// <summary>For looped sounds: the object the sound follows. Null for one shot sounds.</summary>
        public Transform follow;
        public float time;
    }

    public static class GameSound
    {
        /// <summary>Raised for every sound moment. The future sound player listens here; nothing does today.</summary>
        public static event Action<SoundEvent> Played;

        /// <summary>Raised when a looped sound starts (true) or stops (false), e.g. the engine.</summary>
        public static event Action<SoundEvent, bool> LoopChanged;

        /// <summary>Counts per id since start (shown on the tuning panel performance page).</summary>
        public static int TotalEvents { get; private set; }
        private static readonly int[] Counts = new int[700];

        public static int CountOf(SoundId id)
        {
            int i = (int)id;
            return i >= 0 && i < Counts.Length ? Counts[i] : 0;
        }

        /// <summary>A sound at a point in the world.</summary>
        public static void Play(SoundId id, Vector3 position, float volume = 1f, string variant = null, bool byPlayer = false)
        {
            Raise(new SoundEvent
            {
                id = id, position = position, positional = true, volume = volume, variant = variant,
                byPlayer = byPlayer, time = Time.time
            });
        }

        /// <summary>An interface sound (not in the world).</summary>
        public static void PlayUi(SoundId id, string variant = null)
        {
            Raise(new SoundEvent { id = id, positional = false, volume = 1f, variant = variant, byPlayer = true, time = Time.time });
        }

        /// <summary>Starts or stops a looped sound that follows an object (engine, fire).</summary>
        public static void SetLoop(SoundId id, Transform follow, bool on, string variant = null)
        {
            if (follow == null) return;
            var e = new SoundEvent
            {
                id = id, position = follow.position, positional = true, volume = 1f, variant = variant,
                follow = follow, time = Time.time
            };
            Count(id);
            LoopChanged?.Invoke(e, on);
        }

        private static void Raise(SoundEvent e)
        {
            if (e.id == SoundId.None) return;
            Count(e.id);
            Played?.Invoke(e);
        }

        private static void Count(SoundId id)
        {
            TotalEvents++;
            int i = (int)id;
            if (i >= 0 && i < Counts.Length) Counts[i]++;
        }
    }
}
