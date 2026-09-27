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
        Drink
    }

    public enum EquipSlot
    {
        Head,
        Torso,
        Arms,
        Legs,
        Feet,
        Weapon
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

        [Header("World Visual (placeholder cube)")]
        public Color worldColor = Color.white;
        public Vector3 worldScale = new Vector3(0.35f, 0.35f, 0.35f);

        [Tooltip("Optional model shown in the world instead of the placeholder cube (e.g. the weapon model)")]
        public GameObject worldModel;

        [Tooltip("Uniform scale applied when a world model is used")]
        public float worldModelScale = 1f;

        public string DisplayName => string.IsNullOrEmpty(displayName) ? name : displayName;
        public bool IsDirectPickup => category == ItemCategory.Ammo || category == ItemCategory.Currency;
        public bool IsEquippable => category == ItemCategory.Weapon || category == ItemCategory.Apparel;
        public bool IsConsumable => category == ItemCategory.Consumable || category == ItemCategory.Food || category == ItemCategory.Drink;
    }
}
