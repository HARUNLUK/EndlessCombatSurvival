using UnityEngine;
using EndlessCombat.Combat;

namespace EndlessSurvival.Inventory
{
    public enum ItemCategory
    {
        Ammo,
        Fuel,
        Material,
        Gear,
        Junk,
        Currency,
        Weapon,
        Apparel,
        Consumable,
        Food,
        Drink,
        Throwable
    }

    /// <summary>Extra effects of consumables, handled by the survival systems.</summary>
    [System.Flags]
    public enum ConsumableEffect
    {
        None = 0,
        CleansWound = 1,     // alcohol, clean water on a wound: stops infection from starting
        CuresInfection = 2,  // herbal ointment
        CuresPoison = 4,     // herbal tea, charcoal
        RestoresStamina = 8,
        ReducesFatigue = 16  // coffee-like
    }

    public enum EquipSlot
    {
        Head,
        Torso,
        Arms,
        Legs,
        Feet,
        Weapon,  // old single weapon slot: kept so saved assets keep their numbers; weapons now go to the three slots below
        Shield,  // left arm (category Apparel, equipSlot Shield); numbers in ItemDefinition.shield
        LongGun, // back: one long, two handed firearm
        Pistol,  // right hip
        Melee,   // left hip (heavy ones crossed on the back)
        Bow      // back with the quiver, two hands (category Apparel, equipSlot Bow; PlayerBow, 2026-11-17)
    }

    /// <summary>Ammunition kinds. Each firearm uses one; the player keeps one pool per kind.</summary>
    public enum AmmoType
    {
        None = 0,
        Pistol = 1,   // pistol, smg
        Rifle = 2,    // assault rifle
        Shotgun = 3   // hunting shotgun shells
    }

    /// <summary>
    /// Data asset describing one kind of item. Ammo and Currency are "direct" items:
    /// they are converted into weapon reserve ammo / money on pickup and never occupy backpack weight.
    /// </summary>
    [CreateAssetMenu(fileName = "Item_New", menuName = "Endless Survival/Item Definition")]
    public class ItemDefinition : ScriptableObject
    {
        [Header("Identity")]
        public string displayName = "New Item";
        public ItemCategory category = ItemCategory.Junk;
        [TextArea] public string description;

        [Header("Stats")]
        [Tooltip("Weight of a single unit in kilograms (backpack limit is weight based)")]
        public float weight = 0.5f;

        [Tooltip("Base trade value of a single unit (reserved for future vendors)")]
        public int value = 1;

        [Tooltip("Ammo: rounds per unit. Fuel: fuel per unit. Currency: money per unit. Consumable: health restored. Food: hunger restored. Drink: thirst restored.")]
        public int useAmount = 1;

        [Header("Equipment (Weapon and Apparel only)")]
        [Tooltip("Body slot this item is worn in. Weapons use the Weapon slot.")]
        public EquipSlot equipSlot = EquipSlot.Torso;

        [Tooltip("Armor points of an apparel item. Each point reduces incoming damage (see PlayerEquipment).")]
        public float armor = 0f;

        [Tooltip("Weapon prefab spawned in the hand when this weapon is equipped")]
        public WeaponController weaponPrefab;

        [Tooltip("Shield numbers (only used when Equip Slot is Shield). worldScale is the shield size: width, height, thickness")]
        public EndlessCombat.Combat.ShieldStats shield;
        public bool IsShield => equipSlot == EquipSlot.Shield && category == ItemCategory.Apparel;
        public bool IsBow => equipSlot == EquipSlot.Bow && category == ItemCategory.Apparel;

        [Tooltip("Ammo items: which pool the rounds go to. Weapons: leave None, the kind comes from the weapon prefab.")]
        public AmmoType ammoType = AmmoType.None;

        /// <summary>Ammo kind of an ammo item (old assets without a kind count as rifle rounds).</summary>
        public AmmoType AmmoKind => ammoType != AmmoType.None ? ammoType : (category == ItemCategory.Ammo ? AmmoType.Rifle : WeaponAmmo);

        /// <summary>Ammo kind used by this weapon item (None for melee weapons).</summary>
        public AmmoType WeaponAmmo => weaponPrefab != null ? (AmmoType)(int)weaponPrefab.AmmoKind : AmmoType.None;

        /// <summary>Carry slot of a weapon item: long gun (back), pistol (hip) or melee. Apparel returns its equipSlot.</summary>
        public EquipSlot CarrySlot
        {
            get
            {
                if (category != ItemCategory.Weapon) return equipSlot;
                if (weaponPrefab == null) return EquipSlot.Melee;
                switch (weaponPrefab.Type)
                {
                    case WeaponType.Rifle: return EquipSlot.LongGun;
                    case WeaponType.Pistol: return EquipSlot.Pistol;
                    default: return EquipSlot.Melee;
                }
            }
        }

        [Header("World Visual (placeholder cube)")]
        public Color worldColor = Color.white;
        public Vector3 worldScale = new Vector3(0.35f, 0.35f, 0.35f);

        [Tooltip("Optional model shown in the world instead of the placeholder cube (e.g. the weapon model)")]
        public GameObject worldModel;

        [Tooltip("Uniform scale applied when a world model is used")]
        public float worldModelScale = 1f;

        [Tooltip("Optional model carried on the body / in the hand (shield on the arm, torch, rock or grenade in the hand). " +
                 "Made by the menu Survival > Silahlar > Basit Modelleri Kur. Empty: the world model or a placeholder")]
        public GameObject heldModel;

        [Header("Survival: food and drink")]
        [Tooltip("Game hours until this item spoils (0 = never)")]
        public float spoilHours = 0f;
        [Tooltip("Item this turns into when it spoils (empty = it disappears)")]
        public ItemDefinition spoilsInto;
        [Tooltip("Chance (0-1) that eating/drinking this poisons the player")]
        [Range(0f, 1f)] public float poisonChance = 0f;
        [Tooltip("Extra thirst lost when drinking this (salt water)")]
        public float thirstPenalty = 0f;
        [Tooltip("Thirst this food also quenches (berries and other juicy food)")]
        public float bonusThirst = 0f;
        [Tooltip("Stress removed when consumed (cooked food, alcohol). Negative adds stress.")]
        public float stressRelief = 0f;
        [Tooltip("Body warmth gained when consumed (hot food/tea), degrees for a short while")]
        public float warmth = 0f;
        [Tooltip("Item given back after use (empty bottle after drinking)")]
        public ItemDefinition leavesItem;
        public ConsumableEffect effects = ConsumableEffect.None;

        [Header("Survival: apparel")]
        [Tooltip("Insulation of worn apparel (degrees of cold it blocks)")]
        public float insulation = 0f;
        [Tooltip("0 = soaks through, 1 = fully waterproof")]
        [Range(0f, 1f)] public float waterproof = 0f;
        [Tooltip("Extra movement noise while worn (0.15 = +15% noise radius)")]
        public float noise = 0f;

        [Header("Survival: identification")]
        [Tooltip("Name shown once the player can tell what this really is (Nature stat). Empty = always the normal name.")]
        public string identifiedName = "";
        [Tooltip("Nature stat needed to see identifiedName")]
        public float natureToIdentify = 20f;

        /// <summary>Set by the character stats: returns true when the player can identify the item.</summary>
        public static System.Func<ItemDefinition, bool> IdentifyCheck;

        public string DisplayName
        {
            get
            {
                if (!string.IsNullOrEmpty(identifiedName) && IdentifyCheck != null && IdentifyCheck(this)) return identifiedName;
                return string.IsNullOrEmpty(displayName) ? name : displayName;
            }
        }

        public bool IsPerishable => spoilHours > 0f;
        public bool IsDirectPickup => category == ItemCategory.Ammo || category == ItemCategory.Currency;
        public bool IsEquippable => category == ItemCategory.Weapon || category == ItemCategory.Apparel;
        public bool IsConsumable => category == ItemCategory.Consumable || category == ItemCategory.Food || category == ItemCategory.Drink;
    }
}
