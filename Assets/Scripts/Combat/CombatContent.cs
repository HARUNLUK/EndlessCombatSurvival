using UnityEngine;
using EndlessSurvival.Inventory;

namespace EndlessCombat.Combat
{
    /// <summary>
    /// Asset references and tuning values for Package 1 (melee, throwables, footsteps, hearing focus) that
    /// runtime-added components need. Loaded from Resources/CombatContent (built by Package1Setup).
    /// Everything here can be tuned in the Inspector.
    /// </summary>
    [CreateAssetMenu(fileName = "CombatContent", menuName = "Endless Survival/Combat Content")]
    public class CombatContent : ScriptableObject
    {
        [Header("Fists (unarmed)")]
        public MeleeStats fists = MeleeStats.Fists;

        [Header("Throwables")]
        public ItemDefinition rockItem;
        public ItemDefinition grenadeItem;
        [Tooltip("Throw speed (m/s)")]
        public float throwSpeed = 15f;
        [Tooltip("Extra upward angle added to the camera direction (degrees)")]
        public float throwUpAngle = 12f;
        [Tooltip("Seconds between two throws")]
        public float throwCooldown = 0.9f;
        [Tooltip("Delay from the throw animation start to the release (s)")]
        public float releaseDelay = 0.35f;
        [Tooltip("Rock landing noise radius: enemies inside go to look (m)")]
        public float rockNoiseRadius = 14f;
        [Tooltip("Grenade fuse after the throw (s)")]
        public float grenadeFuse = 3f;
        [Tooltip("Grenade blast radius (m)")]
        public float grenadeRadius = 6f;
        [Tooltip("Damage at the center of the blast; falls to 0 at the edge")]
        public float grenadeDamage = 120f;
        [Tooltip("Explosion noise radius (m)")]
        public float grenadeNoiseRadius = 45f;
        [Tooltip("Free items for a fresh game (no save loaded) so the mechanic can be tested")]
        public int startingRocks = 5;
        public int startingGrenades = 2;
        [Tooltip("Test kit: one of each is put in the backpack at scene start (empty the list to turn off)")]
        public ItemDefinition[] testItems = new ItemDefinition[0];

        [Header("Footsteps")]
        public AudioClip[] footstepClips;
        public AudioClip landingClip;
        [Tooltip("Base volume of a footstep (walking). Kept low so focus mode has room to boost it.")]
        [Range(0f, 1f)] public float footstepVolume = 0.18f;
        [Tooltip("Meters per step: crouch / walk / run")]
        public Vector3 strideLength = new Vector3(0.6f, 0.85f, 1.25f);
        [Tooltip("Volume multiplier: crouch / walk / run")]
        public Vector3 gaitVolume = new Vector3(0.4f, 0.75f, 1f);
        [Tooltip("Enemy footsteps are heard up to this distance (m)")]
        public float footstepMaxDistance = 25f;

        [Header("Hearing focus (crouch)")]
        [Tooltip("Footsteps and nearby creature sounds when crouched (dB)")]
        public float focusBoostDb = 8f;
        [Tooltip("Everything else (ambience, engine, music, gunfire) when crouched (dB)")]
        public float ambientDuckDb = -8f;
        [Tooltip("Blend time in and out of focus (s)")]
        public float focusBlendTime = 0.4f;

        private static CombatContent _instance;
        private static bool _loaded;

        public static CombatContent Instance
        {
            get
            {
                if (!_loaded)
                {
                    _loaded = true;
                    _instance = Resources.Load<CombatContent>("CombatContent");
                    if (_instance == null)
                    {
                        Debug.LogWarning("[CombatContent] Resources/CombatContent not found. Run Tools > Combat > Setup Package 1. Using defaults.");
                        _instance = CreateInstance<CombatContent>();
                    }
                }
                return _instance;
            }
        }
    }
}
