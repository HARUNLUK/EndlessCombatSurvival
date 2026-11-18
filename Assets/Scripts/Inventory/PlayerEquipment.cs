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

        /// <summary>Item in a slot. EquipSlot.Weapon (old single slot) returns the weapon in the hand.</summary>
        public ItemDefinition Get(EquipSlot slot)
        {
            if (slot == EquipSlot.Weapon) return Loadout != null ? Loadout.ActiveItem : null;
            return _slots[(int)slot];
        }

        public PlayerLoadout Loadout { get; private set; }

        /// <summary>Weight of the weapons and the shield on the body (counts toward the carry limit).</summary>
        public float CarriedWeight
        {
            get
            {
                float w = 0f;
                foreach (var s in new[] { EquipSlot.LongGun, EquipSlot.Pistol, EquipSlot.Melee, EquipSlot.Shield, EquipSlot.Bow })
                    if (_slots[(int)s] != null) w += _slots[(int)s].weight;
                return w;
            }
        }

        private void UpdateCarriedWeight()
        {
            if (inventory != null) inventory.CarriedWeight = CarriedWeight;
        }

        private void Awake()
        {
            if (inventory == null) inventory = GetComponent<PlayerInventory>();
            if (shooter == null) shooter = GetComponent<PlayerShooter>();
            if (animator == null) animator = GetComponent<Animator>();

            for (int i = 0; i < SlotCount; i++) _visuals[i] = new List<GameObject>();
            Loadout = PlayerLoadout.GetOrAdd(gameObject);
        }

        private void Start()
        {
            if (startingWeapon != null && shooter != null && shooter.CurrentWeapon != null)
            {
                EquipSlot slot = startingWeapon.CarrySlot;
                _slots[(int)slot] = startingWeapon;
                Loadout.AdoptExisting(PlayerLoadout.SlotOf(slot), startingWeapon, shooter.CurrentWeapon);
                UpdateCarriedWeight();
                Changed?.Invoke();
            }
            GiveStartingLoadout();
        }

        /// <summary>Set by the automatic play test: keep the scene's test weapons (its script was written for them).</summary>
        public const string KeepSceneLoadoutKey = "playtest.keepSceneLoadout";

        /// <summary>
        /// What a new journey really starts with (2026-11-13): no rifle, a pistol in the holster with a few rounds
        /// and nothing else on the body. The scene still holds the rifle that was used while the game was built;
        /// it is taken away here. Continuing a journey replaces all of this right after from the save file.
        /// </summary>
        private void GiveStartingLoadout()
        {
            if (PlayerPrefs.GetInt(KeepSceneLoadoutKey, 0) == 1 || inventory == null) return;
            var settings = EndlessSurvival.Survival.SurvivalSettings.Instance;
            if (settings == null) return;

            for (int i = 0; i < SlotCount; i++)
            {
                ItemDefinition worn = _slots[i];
                if (worn == null || worn.category != ItemCategory.Weapon) continue;
                if (Unequip((EquipSlot)i) != null) inventory.Storage.Remove(worn, 1);
            }
            inventory.SetAmmo(EndlessCombat.Combat.AmmoKind.Rifle, 0);
            inventory.SetAmmo(EndlessCombat.Combat.AmmoKind.Shotgun, 0);
            inventory.SetAmmo(EndlessCombat.Combat.AmmoKind.Pistol, 0);

            ItemDefinition pistol = settings.Item("Item_Pistol");
            if (pistol == null) return;
            if (inventory.Storage.GetCount(pistol) == 0) inventory.Storage.Add(pistol, 1);
            Equip(pistol);
            var gun = Loadout != null ? Loadout.Weapon(PlayerLoadout.Slot.Pistol) : null;
            if (gun != null) gun.TakeAllAmmo(); // the magazine it came with
            inventory.SetAmmo(EndlessCombat.Combat.AmmoKind.Pistol, settings.startPistolRounds);

            // A knife on the belt (melee slot)
            ItemDefinition knife = settings.Item("Item_Knife");
            if (knife != null && _slots[(int)knife.CarrySlot] != knife)
            {
                if (inventory.Storage.GetCount(knife) == 0) inventory.Storage.Add(knife, 1);
                Equip(knife);
            }
        }

        /// <summary>Equips one unit of the item from the backpack. Returns a feedback message.</summary>
        public string Equip(ItemDefinition item)
        {
            if (item == null || !item.IsEquippable || inventory == null) return null;
            if (inventory.Storage.GetCount(item) <= 0) return null;

            EquipSlot slot = item.CarrySlot; // weapons: long gun / pistol / melee slot

            if (item.category == ItemCategory.Weapon && item.weaponPrefab == null)
                return $"{item.DisplayName} için silah modeli yok";

            inventory.Storage.Remove(item, 1);

            ItemDefinition previous = _slots[(int)slot];
            _slots[(int)slot] = null;
            UpdateCarriedWeight(); // the old one leaves the body before it goes into the backpack
            if (previous != null && inventory.Storage.Add(previous, 1) <= 0)
            {
                _slots[(int)slot] = previous;
                UpdateCarriedWeight();
                inventory.Storage.Add(item, 1);
                return "Değiştirmek için çanta çok ağır";
            }

            _slots[(int)slot] = item;
            ApplySlot(slot);
            UpdateCarriedWeight();
            Changed?.Invoke();
            return $"Kuşanıldı: {item.DisplayName}";
        }

        /// <summary>Moves the item worn in the slot back to the backpack.</summary>
        public string Unequip(EquipSlot slot)
        {
            if (slot == EquipSlot.Weapon && Loadout != null) slot = ActiveWeaponSlot();
            ItemDefinition item = _slots[(int)slot];
            if (item == null) return null;

            _slots[(int)slot] = null;
            UpdateCarriedWeight();
            if (inventory.Storage.Add(item, 1) <= 0)
            {
                _slots[(int)slot] = item;
                UpdateCarriedWeight();
                return "Çanta çok ağır";
            }

            ApplySlot(slot);
            Changed?.Invoke();
            return $"Çıkarıldı: {item.DisplayName}";
        }

        /// <summary>
        /// The carried item leaves its slot without going to the bag: it fell to the ground (Combat_Action_Plan.md 5,
        /// the caller spawns the pickup). Empty the gun's magazine first, SetSlot would put it into the pool.
        /// </summary>
        public void DropCarried(EquipSlot slot)
        {
            ItemDefinition item = _slots[(int)slot];
            if (item == null) return;
            _slots[(int)slot] = null;
            ApplySlot(slot);
            UpdateCarriedWeight();
            Changed?.Invoke();
        }

        /// <summary>
        /// A weapon / shield picked up from the ground goes straight to its slot when that slot is empty (it does not
        /// go through the bag). False: the slot is taken, the caller puts it into the bag.
        /// </summary>
        public bool EquipFromGround(ItemDefinition item)
        {
            if (item == null || !item.IsEquippable) return false;
            if (item.category == ItemCategory.Weapon && item.weaponPrefab == null) return false;
            EquipSlot slot = item.CarrySlot;
            if (slot != EquipSlot.LongGun && slot != EquipSlot.Pistol && slot != EquipSlot.Melee && slot != EquipSlot.Shield && slot != EquipSlot.Bow) return false;
            if (_slots[(int)slot] != null) return false;
            _slots[(int)slot] = item;
            ApplySlot(slot);
            UpdateCarriedWeight();
            Changed?.Invoke();
            return true;
        }

        private void ApplySlot(EquipSlot slot)
        {
            if (slot == EquipSlot.LongGun || slot == EquipSlot.Pistol || slot == EquipSlot.Melee)
                Loadout.SetSlot(PlayerLoadout.SlotOf(slot), _slots[(int)slot]);
            else if (slot == EquipSlot.Weapon) { }
            else if (slot == EquipSlot.Shield) ApplyShield(_slots[(int)slot]);
            else if (slot == EquipSlot.Bow) Loadout.SetBow(_slots[(int)slot]);
            else ApplyApparelVisuals(slot, _slots[(int)slot]);
        }

        // ------------------------------------------------------------------ Save / load

        /// <summary>Writes the worn items, the weapons on the body (with their magazines and grips) and the hand state.</summary>
        public void CaptureSave(EndlessSurvival.Save.SaveData data)
        {
            data.hasPlayerEquipment = true;
            data.playerEquipment.Clear();
            for (int i = 0; i < SlotCount; i++)
            {
                if (_slots[i] == null) continue;
                var entry = new EndlessSurvival.Save.SavedEquipSlot { slot = i, itemName = _slots[i].name };
                PlayerLoadout.Slot ws = PlayerLoadout.SlotOf((EquipSlot)i);
                if (ws != PlayerLoadout.Slot.None && Loadout != null)
                {
                    WeaponController w = Loadout.Weapon(ws);
                    entry.magazine = w != null && w.IsFirearm ? w.CurrentAmmo : 0;
                    entry.twoHand = Loadout.GripFlag(ws);
                }
                data.playerEquipment.Add(entry);
            }
            data.playerActiveSlot = Loadout != null ? (int)Loadout.Active : -1;
            data.playerWeaponStowed = Loadout == null || Loadout.IsStowed;
            data.playerQuickItem = Loadout != null && Loadout.QuickItem != null ? Loadout.QuickItem.name : null;
        }

        /// <summary>
        /// Puts the saved items back on the body (the backpack is not touched). Call before the ammo pools are set:
        /// replacing a weapon returns its magazine to the pool.
        /// </summary>
        public void RestoreSave(EndlessSurvival.Save.SaveData data, Func<string, ItemDefinition> find)
        {
            if (data == null || !data.hasPlayerEquipment || find == null) return;

            var wanted = new ItemDefinition[SlotCount];
            var entries = new EndlessSurvival.Save.SavedEquipSlot[SlotCount];
            foreach (var e in data.playerEquipment)
            {
                if (e == null || e.slot < 0 || e.slot >= SlotCount) continue;
                wanted[e.slot] = find(e.itemName);
                entries[e.slot] = e;
            }

            for (int i = 0; i < SlotCount; i++)
            {
                var slot = (EquipSlot)i;
                if (slot == EquipSlot.Weapon) continue; // old single slot, not stored
                ItemDefinition item = wanted[i];
                if (item != null && item.category == ItemCategory.Weapon && item.weaponPrefab == null) item = null;
                _slots[i] = item;
                ApplySlot(slot);

                PlayerLoadout.Slot ws = PlayerLoadout.SlotOf(slot);
                if (ws == PlayerLoadout.Slot.None || Loadout == null || entries[i] == null) continue;
                WeaponController w = Loadout.Weapon(ws);
                if (w != null && w.IsFirearm)
                {
                    w.TakeAllAmmo();
                    w.RestoreMagazine(entries[i].magazine);
                }
            }

            // Grips last: putting the shield on resets them to one hand
            for (int i = 0; i < SlotCount && Loadout != null; i++)
            {
                PlayerLoadout.Slot ws = PlayerLoadout.SlotOf((EquipSlot)i);
                if (ws != PlayerLoadout.Slot.None && entries[i] != null) Loadout.SetTwoHanded(ws, entries[i].twoHand);
            }

            if (Loadout != null)
            {
                Loadout.RestoreActive((PlayerLoadout.Slot)Mathf.Clamp(data.playerActiveSlot, -1, 2), data.playerWeaponStowed);
                Loadout.QuickItem = string.IsNullOrEmpty(data.playerQuickItem) ? null : find(data.playerQuickItem);
            }
            UpdateCarriedWeight();
            Changed?.Invoke();
        }

        // ------------------------------------------------------------------ Shield (left arm)

        private bool _shieldBreakHooked;

        private void ApplyShield(ItemDefinition item)
        {
            var holder = ShieldHolder.GetOrAdd(gameObject);
            if (holder.placeholderMaterial == null) holder.placeholderMaterial = placeholderMaterial;
            if (!_shieldBreakHooked)
            {
                _shieldBreakHooked = true;
                holder.Broken += OnShieldBroken;
            }
            holder.Set(item);
            var body = GetComponent<CombatBody>();
            if (body != null) body.RefreshShield();
            if (Loadout != null) Loadout.OnShieldChanged(); // pistol goes one handed with a shield
        }

        /// <summary>The shield took too many hits and fell apart (ShieldHolder drops the pieces): the slot empties.</summary>
        private void OnShieldBroken(ItemDefinition item)
        {
            int i = (int)EquipSlot.Shield;
            if (_slots[i] == null) return;
            _slots[i] = null;
            ApplyShield(null);
            UpdateCarriedWeight();
            Changed?.Invoke();
            EndlessSurvival.Survival.SurvivalNotify.Warn($"{(item != null ? item.DisplayName : "Kalkan")} kırıldı");
        }

        // ------------------------------------------------------------------ Weapon

        private EquipSlot ActiveWeaponSlot()
        {
            switch (Loadout.Active)
            {
                case PlayerLoadout.Slot.LongGun: return EquipSlot.LongGun;
                case PlayerLoadout.Slot.Pistol: return EquipSlot.Pistol;
                default: return EquipSlot.Melee;
            }
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
