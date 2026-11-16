using System.Collections.Generic;
using UnityEngine;

namespace EndlessCombat.Combat
{
    /// <summary>What kind of damage it is. Shields, armor and the death cause read it.</summary>
    public enum DamageType
    {
        Other = 0,
        Bullet = 1,
        Melee = 2,
        Explosion = 3,
        Animal = 4,
        Vehicle = 5,
        Trap = 6
    }

    /// <summary>Where on a body a hit landed (from the hit point and the humanoid bones).</summary>
    public enum BodyRegion
    {
        Body = 0,
        Head = 1,
        Limb = 2
    }

    /// <summary>
    /// One hit, carried from whoever deals it to whatever takes it (Combat_Architecture.md, "Hasar").
    /// Replaces the old (damage, point, normal) arguments and the static DamageContext.Bullet flag.
    /// </summary>
    public struct DamageInfo
    {
        public float Amount;
        public DamageType Type;
        /// <summary>World point of the hit.</summary>
        public Vector3 Point;
        /// <summary>Surface normal at the hit (points away from the target, toward the source for most hits).</summary>
        public Vector3 Normal;
        /// <summary>Direction from the target toward the source (zero when unknown).</summary>
        public Vector3 SourceDirection;
        /// <summary>Who dealt it (the shooter, the striker, the grenade thrower). Can be null.</summary>
        public GameObject Source;
        public BodyRegion Region;
        /// <summary>Dealt by the player (stats, hit markers).</summary>
        public bool ByPlayer;

        public DamageInfo(float amount, DamageType type, Vector3 point, Vector3 normal)
        {
            Amount = amount;
            Type = type;
            Point = point;
            Normal = normal;
            SourceDirection = Vector3.zero;
            Source = null;
            Region = BodyRegion.Body;
            ByPlayer = false;
        }

        /// <summary>Direction toward the source: SourceDirection when set, else the normal.</summary>
        public Vector3 TowardSource => SourceDirection.sqrMagnitude > 0.0001f ? SourceDirection : Normal;

        public DamageInfo WithAmount(float amount)
        {
            DamageInfo copy = this;
            copy.Amount = amount;
            return copy;
        }
    }

    /// <summary>
    /// The one place damage is dealt from: region multiplier, then the target, then the event.
    /// Firearms, melee, grenades, animals and traps all call Deal.
    /// </summary>
    public static class Damage
    {
        /// <summary>Bullet damage multiplier on a head hit (panel "Dövüş"). 1 = same as the body (the old feel).</summary>
        public static float HeadMultiplier = 1f;
        /// <summary>Bullet damage multiplier on an arm or leg hit.</summary>
        public static float LimbMultiplier = 1f;
        /// <summary>Distance from the head bone that still counts as a head hit (m).</summary>
        public const float HeadRadius = 0.2f;

        /// <summary>A hit reached a target (target, the final hit). The HUD, stats and noise listen.</summary>
        public static event System.Action<IDamageable, DamageInfo> Dealt;

        private static bool _tuningRegistered;

        public static void RegisterTuning()
        {
            if (_tuningRegistered) return;
            _tuningRegistered = true;
            const string P = "Dövüş";
            Tuning.TuningPanel.Register(P, "Kafa vuruşu çarpanı", () => HeadMultiplier, v => HeadMultiplier = v, 0.5f, 4f, 0.05f);
            Tuning.TuningPanel.Register(P, "Kol / bacak vuruşu çarpanı", () => LimbMultiplier, v => LimbMultiplier = v, 0.25f, 2f, 0.05f);
        }

        /// <summary>Deals the hit. Returns the amount sent to the target (after the region multiplier), 0 when nothing was hit.</summary>
        public static float Deal(IDamageable target, DamageInfo info)
        {
            if (target == null || target.IsDead || info.Amount <= 0f) return 0f;
            if (info.Type == DamageType.Bullet)
            {
                if (info.Region == BodyRegion.Head) info.Amount *= Mathf.Max(0f, HeadMultiplier);
                else if (info.Region == BodyRegion.Limb) info.Amount *= Mathf.Max(0f, LimbMultiplier);
            }
            target.TakeDamage(info);
            Dealt?.Invoke(target, info);
            return info.Amount;
        }

        /// <summary>Head, limb or body from the hit point and the humanoid bones of the target (Body without a humanoid).</summary>
        public static BodyRegion RegionOf(Collider collider, Vector3 point)
        {
            if (collider == null) return BodyRegion.Body;
            Animator anim = collider.GetComponentInParent<Animator>();
            if (anim == null)
            {
                var root = collider.GetComponentInParent<IDamageable>() as Component;
                if (root != null) anim = root.GetComponentInChildren<Animator>();
            }
            if (anim == null || !anim.isHuman) return BodyRegion.Body;

            Transform head = anim.GetBoneTransform(HumanBodyBones.Head);
            if (head != null && (point - head.position).sqrMagnitude <= HeadRadius * HeadRadius) return BodyRegion.Head;

            // A hit on an arm or leg bone collider (ragdoll bones)
            Transform t = collider.transform;
            if (LimbBones(anim).Contains(t)) return BodyRegion.Limb;
            return BodyRegion.Body;
        }

        private static readonly Dictionary<Animator, HashSet<Transform>> LimbCache = new Dictionary<Animator, HashSet<Transform>>();
        private static readonly HumanBodyBones[] Limbs =
        {
            HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand,
            HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand,
            HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot,
            HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot
        };

        private static HashSet<Transform> LimbBones(Animator anim)
        {
            if (LimbCache.TryGetValue(anim, out var set)) return set;
            if (LimbCache.Count > 256) LimbCache.Clear(); // dead enemies leave their animators behind
            set = new HashSet<Transform>();
            foreach (var b in Limbs)
            {
                Transform t = anim.GetBoneTransform(b);
                if (t != null) set.Add(t);
            }
            LimbCache[anim] = set;
            return set;
        }
    }

    /// <summary>Old call shape (amount, point, normal) for code that does not know the damage type.</summary>
    public static class DamageableExtensions
    {
        public static void TakeDamage(this IDamageable target, float damage, Vector3 hitPoint, Vector3 hitNormal)
        {
            if (target == null) return;
            target.TakeDamage(new DamageInfo(damage, DamageType.Other, hitPoint, hitNormal));
        }
    }
}
