using System;
using UnityEngine;

namespace EndlessCombat.Combat
{
    /// <summary>Which animation set a melee weapon uses. Auto picks one from the old stats (stab = Short, slow = TwoHand).</summary>
    public enum MeleeCategory
    {
        Auto = 0,
        Fists = 1,
        Short = 2,     // knife, dagger
        OneHand = 3,   // club, machete, short sword
        TwoHand = 4    // baseball bat, great sword
    }

    /// <summary>
    /// One attack animation "socket": the clip plus the timing that belongs to that clip.
    /// All times are normalized (0 = clip start, 1 = clip end) so they stay right when the speed changes.
    /// </summary>
    [Serializable]
    public class AttackSlot
    {
        [Tooltip("Drop the Mixamo clip here. Empty = this move is skipped (the light attack is used instead)")]
        public AnimationClip clip;
        [Tooltip("Playback speed of the clip")]
        public float speed = 1f;
        [Tooltip("Hit window start (normalized). Before this: wind-up, the target can still roll away")]
        [Range(0f, 1f)] public float hitStart = 0.35f;
        [Tooltip("Hit window end (normalized)")]
        [Range(0f, 1f)] public float hitEnd = 0.55f;
        [Tooltip("The character is free again from this point (normalized). Until then: no moving, no other action")]
        [Range(0.3f, 1f)] public float recoverAt = 0.9f;
        [Tooltip("Forward step during the wind-up and the hit (m)")]
        public float lunge = 0.35f;
        [Tooltip("Damage multiplier on top of the weapon's damage")]
        public float damageMultiplier = 1f;
        [Tooltip("Stamina multiplier on top of the weapon's stamina cost")]
        public float staminaMultiplier = 1f;
        [Tooltip("Stagger multiplier on top of the weapon's stagger time")]
        public float staggerMultiplier = 1f;
        [Tooltip("Hits taken during this attack do not interrupt it (damage still lands)")]
        public bool hyperArmor;

        public AttackSlot() { }

        public AttackSlot(float hitStart, float hitEnd, float recoverAt, float lunge, float damage = 1f, float stamina = 1f, float stagger = 1f, bool hyperArmor = false)
        {
            this.hitStart = hitStart;
            this.hitEnd = hitEnd;
            this.recoverAt = recoverAt;
            this.lunge = lunge;
            damageMultiplier = damage;
            staminaMultiplier = stamina;
            staggerMultiplier = stagger;
            this.hyperArmor = hyperArmor;
        }
    }

    /// <summary>The four attacks of one weapon category: light combo 1-2-3 and a heavy attack.</summary>
    [Serializable]
    public class CategorySlots
    {
        public AttackSlot light1 = new AttackSlot(0.35f, 0.55f, 0.85f, 0.35f);
        public AttackSlot light2 = new AttackSlot(0.35f, 0.55f, 0.85f, 0.35f);
        public AttackSlot light3 = new AttackSlot(0.35f, 0.58f, 0.9f, 0.45f, 1.15f, 1.1f, 1.2f);
        public AttackSlot heavy = new AttackSlot(0.45f, 0.65f, 0.92f, 0.5f, 1.7f, 1.8f, 2f, true);

        public AttackSlot Light(int step)
        {
            switch (step)
            {
                case 1: return light2;
                case 2: return light3;
                default: return light1;
            }
        }
    }

    /// <summary>A movement action (side step, dash, backstep): clip, travel and the invulnerable window.</summary>
    [Serializable]
    public class DodgeSlot
    {
        public AnimationClip clip;
        public float speed = 1f;
        [Tooltip("Total travel (m)")]
        public float distance = 3.2f;
        [Tooltip("Invulnerable from (normalized)")]
        [Range(0f, 1f)] public float iFrameStart = 0.08f;
        [Tooltip("Invulnerable until (normalized)")]
        [Range(0f, 1f)] public float iFrameEnd = 0.45f;
        [Tooltip("Free again from (normalized)")]
        [Range(0.3f, 1f)] public float recoverAt = 0.85f;
        [Tooltip("Stamina cost")]
        public float staminaCost = 18f;
    }

    /// <summary>A one shot reaction (hit, guard hit, guard break). Its length is the lock time.</summary>
    [Serializable]
    public class ReactionSlot
    {
        public AnimationClip clip;
        public float speed = 1f;
        [Tooltip("Lock time when the clip is empty (s)")]
        public float fallbackDuration = 0.4f;
        [Tooltip("Free again from (normalized)")]
        [Range(0.2f, 1f)] public float recoverAt = 0.8f;
    }

    /// <summary>A held pose (guard, shield carry). A clip without Loop Time is frozen at holdAt.</summary>
    [Serializable]
    public class PoseSlot
    {
        public AnimationClip clip;
        [Tooltip("-1 = play and loop the clip. 0..1 = freeze the clip at this point (for one shot clips)")]
        [Range(-1f, 1f)] public float holdAt = -1f;
    }

    /// <summary>
    /// Every animation "socket" of the melee combat in one place (Assets/Resources/MeleeAnimationSet.asset).
    /// Drop Mixamo clips into the fields, then run Tools > Combat > Melee > Rebuild Animator.
    /// Empty fields are filled with placeholder clips from the UAL packs by the setup tool.
    /// </summary>
    [CreateAssetMenu(fileName = "MeleeAnimationSet", menuName = "Endless Survival/Melee Animation Set")]
    public class MeleeAnimationSet : ScriptableObject
    {
        [Header("Attacks per weapon category")]
        [HideInInspector] public int clipSetVersion; // which version of PackClipsSetup filled the sockets (auto update)
        public CategorySlots fists = new CategorySlots();
        public CategorySlots shortBlade = new CategorySlots();
        public CategorySlots oneHand = new CategorySlots();
        public CategorySlots twoHand = new CategorySlots();

        [Header("Dodge (no rolls: a quick step / hop, the character keeps facing forward)")]
        [Tooltip("Side step to the left (Mixamo: Standing Dodge Left)")]
        public DodgeSlot dodgeLeft = new DodgeSlot { distance = 2.0f, iFrameStart = 0.05f, iFrameEnd = 0.4f, recoverAt = 0.75f, staminaCost = 15f };
        [Tooltip("Side step to the right (Mixamo: Standing Dodge Right)")]
        public DodgeSlot dodgeRight = new DodgeSlot { distance = 2.0f, iFrameStart = 0.05f, iFrameEnd = 0.4f, recoverAt = 0.75f, staminaCost = 15f };
        [Tooltip("Quick dash forward (Mixamo: Standing Dodge Forward)")]
        public DodgeSlot dodgeForward = new DodgeSlot { distance = 2.2f, iFrameStart = 0.05f, iFrameEnd = 0.35f, recoverAt = 0.75f, staminaCost = 15f };
        [Tooltip("Back or no move input: hop backwards (Mixamo: Standing Dodge Backward)")]
        public DodgeSlot backstep = new DodgeSlot { distance = 1.8f, iFrameStart = 0.05f, iFrameEnd = 0.35f, recoverAt = 0.75f, staminaCost = 13f };
        [Tooltip("Heavy shield on the arm: dodges are this much slower and shorter")]
        [Range(0.4f, 1f)] public float heavyShieldDodge = 0.8f;
        [Tooltip("Heavy shield on the arm: stamina cost multiplier")]
        public float heavyShieldStamina = 1.5f;

        [Header("Reactions")]
        public ReactionSlot hitLight = new ReactionSlot { fallbackDuration = 0.35f };
        [Tooltip("Long stagger (heavy attacks)")]
        public ReactionSlot hitHeavy = new ReactionSlot { fallbackDuration = 0.8f };
        [Tooltip("A hit landed on the raised guard")]
        public ReactionSlot guardHit = new ReactionSlot { fallbackDuration = 0.25f, recoverAt = 0.7f };
        [Tooltip("Out of stamina while guarding: guard is broken, open to hits")]
        public ReactionSlot guardBreak = new ReactionSlot { fallbackDuration = 1.2f, recoverAt = 0.9f };
        [Tooltip("The attacker's weapon bounced off a big shield")]
        public ReactionSlot rebound = new ReactionSlot { fallbackDuration = 0.6f };

        [Header("Guard and shield poses (held while the button is down)")]
        [Tooltip("Shield raised (left arm only, so the right hand keeps its weapon or pistol)")]
        public PoseSlot shieldGuard = new PoseSlot();
        [Tooltip("Shield carried, not raised (left arm only). Empty = arm hangs naturally")]
        public PoseSlot shieldCarry = new PoseSlot();
        [Tooltip("Guard with the weapon when there is no shield (upper body)")]
        public PoseSlot weaponGuard = new PoseSlot { holdAt = 0.5f };
        [Tooltip("Melee weapon held with both hands, at rest (upper body). Empty = the one handed stance with the left hand pulled onto the handle")]
        public PoseSlot twoHandCarry = new PoseSlot();

        public CategorySlots Category(MeleeCategory category)
        {
            switch (category)
            {
                case MeleeCategory.Short: return shortBlade;
                case MeleeCategory.OneHand: return oneHand;
                case MeleeCategory.TwoHand: return twoHand;
                default: return fists;
            }
        }

        // ------------------------------------------------------------ state names (shared with the editor builder)

        public const string LayerFullBody = "CombatFullBody";
        /// <summary>
        /// Attacks play from the waist up only (2026-11-12): the attack clips step and lunge with the legs, and on a
        /// character that stands in place the feet jumped left and right. The legs now keep the base stance; the
        /// small lunge is done by code. Dodges and hit reactions stay on the full body layer.
        /// </summary>
        public const string LayerAttack = "CombatUpperAttack";
        public const string LayerGuard = "CombatGuard";
        public const string LayerShield = "CombatShieldArm";
        public const string SpeedParam = "CombatSpeed";
        public const string EmptyState = "Empty";

        public static string AttackState(MeleeCategory category, string move) => $"{category}_{move}";
        public const string MoveLight1 = "Light1";
        public const string MoveLight2 = "Light2";
        public const string MoveLight3 = "Light3";
        public const string MoveHeavy = "Heavy";
        public const string StateDodgeLeft = "DodgeLeft";
        public const string StateDodgeRight = "DodgeRight";
        public const string StateDodgeForward = "DodgeForward";
        public const string StateBackstep = "Backstep";
        public const string StateHitLight = "HitLight";
        public const string StateHitHeavy = "HitHeavy";
        public const string StateGuardHit = "GuardHit";
        public const string StateGuardBreak = "GuardBreak";
        public const string StateRebound = "Rebound";
        public const string StateShieldGuard = "ShieldGuard";
        public const string StateShieldCarry = "ShieldCarry";
        public const string StateWeaponGuard = "WeaponGuard";
        public const string StateTwoHandCarry = "TwoHandCarry";

        public static string LightMove(int step) => step == 1 ? MoveLight2 : step == 2 ? MoveLight3 : MoveLight1;

        private static MeleeAnimationSet _instance;
        private static bool _loaded;

        public static MeleeAnimationSet Instance
        {
            get
            {
                if (!_loaded)
                {
                    _loaded = true;
                    _instance = Resources.Load<MeleeAnimationSet>("MeleeAnimationSet");
                    if (_instance == null)
                    {
                        Debug.LogWarning("[MeleeAnimationSet] Resources/MeleeAnimationSet not found. Run Tools > Combat > Melee > Setup. Using defaults.");
                        _instance = CreateInstance<MeleeAnimationSet>();
                    }
                }
                return _instance;
            }
        }
    }
}
