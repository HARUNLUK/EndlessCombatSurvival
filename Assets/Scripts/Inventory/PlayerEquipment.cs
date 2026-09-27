using System;
using System.Collections.Generic;
using UnityEngine;
using EndlessCombat.Combat;

namespace EndlessSurvival.Inventory
{
    /// <summary>
    /// Worn apparel (head, torso, arms, legs, feet) and the equipped weapon.
    /// Apparel is shown as simple placeholder boxes attached to the character bones.
    /// </summary>
    public class PlayerEquipment : MonoBehaviour
    {
        [Header("References")]
        public PlayerInventory inventory;
        public PlayerShooter shooter;
        [Tooltip("Humanoid animator of the character (used to find bones)")]
        public Animator animator;

        [Header("Placeholder Visuals")]
        [Tooltip("Material used by the placeholder boxes (tinted per item)")]
        public Material placeholderMaterial;

        [Header("Starting Loadout")]
        [Tooltip("Item that describes the weapon the character already holds at start (the weapon is not replaced)")]
        public ItemDefinition startingWeapon;

        [Header("Armor")]
        [Tooltip("Damage reduction per armor point (0.02 = 2 percent)")]
        public float reductionPerArmorPoint = 0.02f;

        [Tooltip("Upper limit of total damage reduction")]
        [Range(0f, 0.95f)]
        public float maxReduction = 0.75f;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int SlotCount = Enum.GetValues(typeof(EquipSlot)).Length;

        private readonly ItemDefinition[] _slots = new ItemDefinition[SlotCount];
        private readonly List<GameObject>[] _visuals = new List<GameObject>[SlotCount];

        public event Action Changed;

        public float TotalArmor
        {
            get
            {
                float total = 0f;
                foreach (var item in _slots)
                    if (item != null) total += item.armor;
                return total;
            }
        }

        public float DamageReduction => Mathf.Clamp(TotalArmor * reductionPerArmorPoint, 0f, maxReduction);

        public ItemDefinition Get(EquipSlot slot)
        {
            return _slots[(int)slot];
        }

        private void Awake()
        {
            if (inventory == null) inventory = GetComponent<PlayerInventory>();
            if (shooter == null) shooter = GetComponent<PlayerShooter>();
            if (animator == null) animator = GetComponent<Animator>();

            for (int i = 0; i < SlotCount; i++) _visuals[i] = new List<GameObject>();
        }

        private void Start()
        {
            if (startingWeapon != null && shooter != null && shooter.CurrentWeapon != null)
            {
                _slots[(int)EquipSlot.Weapon] = startingWeapon;
                Changed?.Invoke();
            }
        }

        /// <summary>Equips one unit of the item from the backpack. Returns a feedback message.</summary>
        public string Equip(ItemDefinition item)
        {
            if (item == null || !item.IsEquippable || inventory == null) return null;
            if (inventory.Storage.GetCount(item) <= 0) return null;

            EquipSlot slot = item.category == ItemCategory.Weapon ? EquipSlot.Weapon : item.equipSlot;

            if (slot == EquipSlot.Weapon && item.weaponPrefab == null)
                return $"{item.DisplayName} has no weapon model";

            inventory.Storage.Remove(item, 1);

            ItemDefinition previous = _slots[(int)slot];
            if (previous != null && inventory.Storage.Add(previous, 1) <= 0)
            {
                inventory.Storage.Add(item, 1);
                return "Backpack is too heavy to swap";
            }

            _slots[(int)slot] = item;
            ApplySlot(slot);
            Changed?.Invoke();
            return $"Equipped {item.DisplayName}";
        }

        /// <summary>Moves the item worn in the slot back to the backpack.</summary>
        public string Unequip(EquipSlot slot)
        {
            ItemDefinition item = _slots[(int)slot];
            if (item == null) return null;

            if (inventory.Storage.Add(item, 1) <= 0) return "Backpack is too heavy";

            _slots[(int)slot] = null;
            ApplySlot(slot);
            Changed?.Invoke();
            return $"Unequipped {item.DisplayName}";
        }

        private void ApplySlot(EquipSlot slot)
        {
            if (slot == EquipSlot.Weapon) ApplyWeapon(_slots[(int)slot]);
            else ApplyApparelVisuals(slot, _slots[(int)slot]);
        }

        // ------------------------------------------------------------------ Weapon

        private void ApplyWeapon(ItemDefinition item)
        {
            if (shooter == null) return;

            // Rounds stay with the player: they go to the backpack side pool and are handed to the next weapon.
            WeaponController old = shooter.CurrentWeapon;
            if (old != null) inventory.AddLooseAmmo(old.TakeAllAmmo());

            WeaponController weapon = shooter.ReplaceWeapon(item != null ? item.weaponPrefab : null);
            if (weapon != null) weapon.AddReserveAmmo(inventory.TakeLooseAmmo());
        }

        // ------------------------------------------------------------------ Apparel visuals

        private void ApplyApparelVisuals(EquipSlot slot, ItemDefinition item)
        {
            List<GameObject> list = _visuals[(int)slot];
            foreach (var go in list)
                if (go != null) Destroy(go);
            list.Clear();

            if (item == null || animator == null || !animator.isHuman) return;

            switch (slot)
            {
                case EquipSlot.Head:
                    Transform head = Bone(HumanBodyBones.Head);
                    if (head != null)
                        AddBox(list, item, head, head.position + Vector3.up * 0.10f, new Vector3(0.27f, 0.27f, 0.29f));
                    break;

                case EquipSlot.Torso:
                    Transform neck = Bone(HumanBodyBones.Neck) != null ? Bone(HumanBodyBones.Neck) : Bone(HumanBodyBones.Head);
                    Transform hips = Bone(HumanBodyBones.Spine) != null ? Bone(HumanBodyBones.Spine) : Bone(HumanBodyBones.Hips);
                    AddSegment(list, item, hips, hips, neck, 0.40f, 0.26f, 1.0f);
                    break;

                case EquipSlot.Arms:
                    AddLimb(list, item, HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand, 0.115f, 0.10f);
                    AddLimb(list, item, HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand, 0.115f, 0.10f);
                    break;

                case EquipSlot.Legs:
                    AddLimb(list, item, HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot, 0.17f, 0.13f);
                    AddLimb(list, item, HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot, 0.17f, 0.13f);
                    break;

                case EquipSlot.Feet:
                    AddFoot(list, item, HumanBodyBones.LeftFoot);
                    AddFoot(list, item, HumanBodyBones.RightFoot);
                    break;
            }
        }

        private Transform Bone(HumanBodyBones bone)
        {
            return animator.GetBoneTransform(bone);
        }

        private void AddLimb(List<GameObject> list, ItemDefinition item, HumanBodyBones upper, HumanBodyBones lower, HumanBodyBones end,
            float upperThickness, float lowerThickness)
        {
            Transform a = Bone(upper);
            Transform b = Bone(lower);
            Transform c = Bone(end);
            if (a == null || b == null || c == null) return;

            AddSegment(list, item, a, a, b, upperThickness, upperThickness, 0.95f);
            AddSegment(list, item, b, b, c, lowerThickness, lowerThickness, 0.95f);
        }

        private void AddFoot(List<GameObject> list, ItemDefinition item, HumanBodyBones footBone)
        {
            Transform foot = Bone(footBone);
            if (foot == null) return;

            Vector3 forward = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
            Vector3 position = foot.position + forward * 0.07f + Vector3.down * 0.03f;
            AddBox(list, item, foot, position, new Vector3(0.12f, 0.10f, 0.28f), Quaternion.LookRotation(forward, Vector3.up));
        }

        /// <summary>Adds a box stretched between two bones and attached to a parent bone.</summary>
        private void AddSegment(List<GameObject> list, ItemDefinition item, Transform parent, Transform from, Transform to,
            float width, float depth, float lengthScale)
        {
            if (parent == null || from == null || to == null) return;

            Vector3 direction = to.position - from.position;
            float length = direction.magnitude * lengthScale;
            if (length < 0.02f) return;

            Vector3 forward = Vector3.ProjectOnPlane(transform.forward, direction.normalized);
            if (forward.sqrMagnitude < 0.0001f) forward = Vector3.ProjectOnPlane(transform.right, direction.normalized);
            Quaternion rotation = Quaternion.LookRotation(forward.normalized, direction.normalized);

            Vector3 center = (from.position + to.position) * 0.5f;
            AddBox(list, item, parent, center, new Vector3(width, length, depth), rotation);
        }

        private void AddBox(List<GameObject> list, ItemDefinition item, Transform parent, Vector3 worldPosition, Vector3 size)
        {
            AddBox(list, item, parent, worldPosition, size, parent.rotation);
        }

        private void AddBox(List<GameObject> list, ItemDefinition item, Transform parent, Vector3 worldPosition, Vector3 size, Quaternion worldRotation)
        {
            if (parent == null) return;

            GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = $"Worn_{item.DisplayName}";

            var collider = box.GetComponent<Collider>();
            if (collider != null) Destroy(collider);

            var rendererComponent = box.GetComponent<MeshRenderer>();
            if (placeholderMaterial != null) rendererComponent.sharedMaterial = placeholderMaterial;

            var block = new MaterialPropertyBlock();
            block.SetColor(BaseColorId, item.worldColor);
            block.SetColor(ColorId, item.worldColor);
            rendererComponent.SetPropertyBlock(block);

            box.transform.SetPositionAndRotation(worldPosition, worldRotation);
            box.transform.localScale = size;
            box.transform.SetParent(parent, true);

            list.Add(box);
        }
    }
}
