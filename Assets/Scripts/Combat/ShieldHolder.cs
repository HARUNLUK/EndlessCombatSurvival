using UnityEngine;
using EndlessSurvival.Inventory;

namespace EndlessCombat.Combat
{
    /// <summary>Numbers of a shield item (ItemDefinition.shield, used when equipSlot is Shield).</summary>
    [System.Serializable]
    public struct ShieldStats
    {
        [Tooltip("Share of melee / animal damage stopped by the raised shield (0..1)")]
        [Range(0f, 1f)] public float absorb;
        [Tooltip("Share of bullet damage stopped by the raised shield from the front (0 = bullets go through)")]
        [Range(0f, 1f)] public float bulletAbsorb;
        [Tooltip("Stamina lost per blocked damage point. Lower = steadier shield")]
        public float staminaPerDamage;
        [Tooltip("Half angle in front of the character that the raised shield covers (degrees)")]
        public float coverAngle;
        [Tooltip("Move speed multiplier while the shield is raised")]
        public float guardMoveSpeed;
        [Tooltip("Heavy shield (riot): more durability by default")]
        public bool heavy;
        [Tooltip("Not used since the Souls combat was removed (2026-11-14); kept for the item assets")]
        public bool bounceAttackers;
        [Tooltip("Damage the shield can stop before it breaks. 0 = default (120, heavy 300)")]
        public float durability;

        /// <summary>Durability with the default for item assets made before the field existed.</summary>
        public float MaxDurability => durability > 0f ? durability : (heavy ? 300f : 120f);

        public static ShieldStats Wooden => new ShieldStats
        {
            absorb = 0.75f, bulletAbsorb = 0.55f, staminaPerDamage = 1.1f, coverAngle = 70f, guardMoveSpeed = 0.55f,
            heavy = false, bounceAttackers = false
        };

        public static ShieldStats Riot => new ShieldStats
        {
            absorb = 1f, bulletAbsorb = 0.95f, staminaPerDamage = 0.6f, coverAngle = 80f, guardMoveSpeed = 0.4f,
            heavy = true, bounceAttackers = true
        };

        /// <summary>Guard with only the weapon (no shield).</summary>
        public static ShieldStats WeaponGuard => new ShieldStats
        {
            absorb = 0.55f, bulletAbsorb = 0f, staminaPerDamage = 1.6f, coverAngle = 65f, guardMoveSpeed = 0.6f,
            heavy = false, bounceAttackers = false
        };
    }

    /// <summary>
    /// Shows the shield item on the character: on the left forearm when it can be used, on the back when the
    /// hands are busy (rifle, two handed weapon). Placeholder box sized by the item's worldScale unless the item
    /// has a worldModel. Used by the player (PlayerEquipment) and by enemies (EnemyController.heldShieldItem).
    /// </summary>
    [DisallowMultipleComponent]
    public class ShieldHolder : MonoBehaviour
    {
        [Tooltip("Shield position on the left forearm (bone space)")]
        public Vector3 armOffset = new Vector3(0f, 0.12f, -0.09f);
        public Vector3 armEuler = new Vector3(0f, 90f, 90f);
        [Tooltip("Shield position on the back (chest bone space)")]
        public Vector3 backOffset = new Vector3(0f, 0.05f, -0.22f);
        public Vector3 backEuler = new Vector3(0f, 0f, 0f);
        [Tooltip("Material for the placeholder box (tinted with the item color)")]
        public Material placeholderMaterial;

        private Animator _animator;
        private GameObject _visual;
        private ItemDefinition _item;
        private bool _onBack;

        public ItemDefinition Item => _item;
        public bool HasShield => _item != null;

        /// <summary>Damage the shield can still stop. Full when a shield is put on (not saved: a loaded game starts full).</summary>
        public float Durability { get; private set; }
        public float Durability01 => _item != null ? Mathf.Clamp01(Durability / Mathf.Max(1f, Stats.MaxDurability)) : 0f;
        /// <summary>The shield broke (the item is gone). The owner empties its slot.</summary>
        public event System.Action<ItemDefinition> Broken;

        /// <summary>Stopped damage wears the shield; at zero it breaks and the pieces fall to the ground.</summary>
        public void Wear(float stoppedDamage)
        {
            if (_item == null || stoppedDamage <= 0f) return;
            Durability -= stoppedDamage;
            if (Durability > 0f) return;

            ItemDefinition item = _item;
            GameObject pieces = _visual;
            _visual = null;
            _item = null;
            Raised = false;
            if (pieces != null)
            {
                pieces.transform.SetParent(null, true);
                var rb = pieces.AddComponent<Rigidbody>();
                rb.mass = 3f;
                rb.AddForce(transform.forward * 1.5f + Vector3.up, ForceMode.VelocityChange);
                var box = pieces.AddComponent<BoxCollider>();
                box.size = Vector3.one;
                Destroy(pieces, 20f);
            }
            EndlessCombat.Audio.GameSound.Play(EndlessCombat.Audio.SoundId.ShieldBreak, transform.position, 1f, item != null ? item.name : null);
            Broken?.Invoke(item);
        }
        /// <summary>The shield model on the body (null without a shield).</summary>
        public GameObject Visual => _visual;
        /// <summary>True when the shield is on the arm and can be raised.</summary>
        public bool Usable => _item != null && !_onBack;
        public ShieldStats Stats => _item != null ? _item.shield : default;

        public static ShieldHolder GetOrAdd(GameObject owner)
        {
            var h = owner.GetComponent<ShieldHolder>();
            if (h == null) h = owner.AddComponent<ShieldHolder>();
            return h;
        }

        private Animator Anim
        {
            get
            {
                if (_animator == null) _animator = GetComponentInChildren<Animator>();
                return _animator;
            }
        }

        public void Set(ItemDefinition item)
        {
            if (_item == item && _visual != null) return;
            _item = item;
            if (_visual != null) Destroy(_visual);
            _visual = null;
            Durability = item != null ? item.shield.MaxDurability : 0f;
            if (item == null) return;

            if (item.heldModel != null)
            {
                _visual = Instantiate(item.heldModel); // simple model made for the arm (front faces +Z)
            }
            else if (item.worldModel != null)
            {
                _visual = Instantiate(item.worldModel);
                _visual.transform.localScale = Vector3.one * Mathf.Max(0.01f, item.worldModelScale);
            }
            else
            {
                _visual = GameObject.CreatePrimitive(PrimitiveType.Cube);
                _visual.transform.localScale = item.worldScale;
                var r = _visual.GetComponent<MeshRenderer>();
                if (placeholderMaterial != null) r.sharedMaterial = placeholderMaterial;
                var block = new MaterialPropertyBlock();
                block.SetColor("_BaseColor", item.worldColor);
                block.SetColor("_Color", item.worldColor);
                r.SetPropertyBlock(block);
            }
            _visual.name = "Shield_" + item.DisplayName;
            foreach (var c in _visual.GetComponentsInChildren<Collider>()) Destroy(c);
            foreach (var rb in _visual.GetComponentsInChildren<Rigidbody>()) Destroy(rb);

            _onBack = !_onBack; // force re-parent
            SetOnBack(!_onBack);
        }

        /// <summary>Moves the shield between the arm and the back.</summary>
        public void SetOnBack(bool onBack)
        {
            if (_visual == null) { _onBack = onBack; return; }
            if (_onBack == onBack && _visual.transform.parent != null) return;
            _onBack = onBack;

            var anim = Anim;
            Transform bone = null;
            if (anim != null && anim.isHuman)
            {
                if (onBack)
                {
                    bone = anim.GetBoneTransform(HumanBodyBones.Chest);
                    if (bone == null) bone = anim.GetBoneTransform(HumanBodyBones.Spine);
                }
                else bone = anim.GetBoneTransform(HumanBodyBones.LeftLowerArm);
            }
            if (bone == null) bone = transform;

            _visual.transform.SetParent(bone, false);
            _visual.transform.localPosition = onBack ? backOffset : armOffset;
            _visual.transform.localRotation = Quaternion.Euler(onBack ? backEuler : armEuler);
        }

        public void Hide(bool hidden)
        {
            if (_visual != null) _visual.SetActive(!hidden);
        }

        [Header("On the arm (kept upright after the animation)")]
        [Tooltip("Distance in front of the forearm when raised (m)")]
        public float raisedForward = 0.16f;
        [Tooltip("Distance outside the forearm when carried at the side (m)")]
        public float carriedOutward = 0.1f;
        [Tooltip("Backward tilt of the raised shield (degrees)")]
        public float raisedTilt = 8f;
        [Tooltip("Pistol behind the shield: the raised shield moves this far to the left (m) so the gun arm passes its right edge")]
        public float gunSideShift = 0.26f;
        [Tooltip("Pistol behind the shield: the shield turns this much toward the left (degrees)")]
        public float gunSideYaw = 22f;

        /// <summary>Set every frame by the owner: shooting the pistol past the shield instead of through it.</summary>
        public bool GunSide { get; set; }
        private float _gunBlend;
        /// <summary>Set by the owner's CombatBody: the shield is raised (guarding).</summary>
        public bool Raised { get; set; }
        private float _raisedBlend;

        /// <summary>
        /// A shield glued to the forearm bone turns with every clip and ends up diagonal. On the arm it is placed
        /// after the animation instead: upright and facing forward when the forearm is across the chest (guard),
        /// upright on the outside of the arm when the arm hangs (carry), blended in between.
        /// </summary>
        private void LateUpdate()
        {
            if (_visual == null || _onBack) return;
            var anim = Anim;
            if (anim == null || !anim.isHuman) return;
            Transform lower = anim.GetBoneTransform(HumanBodyBones.LeftLowerArm);
            Transform hand = anim.GetBoneTransform(HumanBodyBones.LeftHand);
            if (lower == null || hand == null) return;

            Vector3 up = Vector3.up;
            Vector3 fwd = Vector3.ProjectOnPlane(transform.forward, up).normalized;
            Vector3 right = Vector3.Cross(up, fwd);
            Vector3 forearm = (hand.position - lower.position).normalized;
            Vector3 anchor = Vector3.Lerp(lower.position, hand.position, 0.55f);

            // 1 = raised (guard), 0 = carried at the side. Driven by the guard state, not by the forearm angle
            // (2026-11-12): the arm swings while running and swinging, and the shield used to flip with it.
            _raisedBlend = Mathf.MoveTowards(_raisedBlend, Raised ? 1f : 0f, Time.deltaTime * 7f);
            float raised = Mathf.SmoothStep(0f, 1f, _raisedBlend);

            _gunBlend = Mathf.MoveTowards(_gunBlend, GunSide ? 1f : 0f, Time.deltaTime * 6f);
            Quaternion guardRot = Quaternion.LookRotation(fwd, up) * Quaternion.Euler(-raisedTilt, -gunSideYaw * _gunBlend, 0f);
            Quaternion carryRot = Quaternion.LookRotation(-right, up);
            Vector3 guardPos = anchor + fwd * raisedForward - right * (gunSideShift * _gunBlend);
            Vector3 carryPos = anchor - right * carriedOutward;

            _visual.transform.SetPositionAndRotation(
                Vector3.Lerp(carryPos, guardPos, raised),
                Quaternion.Slerp(carryRot, guardRot, raised));
        }
    }
}
