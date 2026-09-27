using System;
using UnityEngine;
using EndlessCombat.Combat;

namespace EndlessSurvival.Inventory
{
    /// <summary>
    /// Player carried inventory: a weight limited backpack, a money wallet and a link to the weapon reserve ammo.
    /// Ammo and Currency items never enter the backpack; they are converted on pickup.
    /// </summary>
    public class PlayerInventory : MonoBehaviour
    {
        [Header("Backpack")]
        [Tooltip("Maximum backpack weight in kilograms")]
        public float maxCarryWeight = 30f;

        [Header("Wallet")]
        public int startingMoney = 0;

        [Header("Dropping")]
        [Tooltip("Pickup prefab spawned when the player drops an item (items are discarded if empty)")]
        public WorldPickup dropPickupPrefab;

        [Tooltip("Distance in front of the player where dropped items appear")]
        public float dropDistance = 1.5f;

        private ItemStorage _storage;
        private int _money;
        private bool _moneyInitialized;

        public event Action<int> MoneyChanged;

        public ItemStorage Storage
        {
            get
            {
                if (_storage == null) _storage = new ItemStorage(maxCarryWeight);
                return _storage;
            }
        }

        public int Money
        {
            get
            {
                EnsureMoney();
                return _money;
            }
        }

        private void EnsureMoney()
        {
            if (_moneyInitialized) return;
            _moneyInitialized = true;
            _money = startingMoney;
        }

        /// <summary>Rounds carried while no weapon is equipped. Handed to the next equipped weapon.</summary>
        public int LooseAmmo { get; private set; }

        public void AddLooseAmmo(int rounds)
        {
            if (rounds > 0) LooseAmmo += rounds;
        }

        public int TakeLooseAmmo()
        {
            int rounds = LooseAmmo;
            LooseAmmo = 0;
            return rounds;
        }

        private WeaponController FindWeapon()
        {
            var shooter = GetComponentInChildren<PlayerShooter>(true);
            return shooter != null ? shooter.CurrentWeapon : null;
        }

        private string UseSurvivalItem(ItemDefinition item)
        {
            var survival = GetComponent<PlayerSurvival>();
            if (survival == null) return "Cannot use this right now";

            bool isFood = item.category == ItemCategory.Food;
            float missing = isFood ? survival.maxHunger - survival.Hunger : survival.maxThirst - survival.Thirst;
            if (missing < 0.5f) return isFood ? "Not hungry" : "Not thirsty";

            Storage.Remove(item, 1);
            float restored = isFood ? survival.Eat(item.useAmount) : survival.Drink(item.useAmount);
            return $"{(isFood ? "Ate" : "Drank")} {item.DisplayName} (+{Mathf.RoundToInt(restored)} {(isFood ? "hunger" : "thirst")})";
        }

        /// <summary>Consumes one consumable from the backpack. Returns a feedback message.</summary>
        public string UseItem(ItemDefinition item)
        {
            if (item == null || !item.IsConsumable || Storage.GetCount(item) <= 0) return null;

            if (item.category == ItemCategory.Food || item.category == ItemCategory.Drink)
                return UseSurvivalItem(item);

            var health = GetComponent<PlayerHealth>();
            if (health == null) return "Cannot use this right now";
            if (health.IsDead) return null;
            if (health.CurrentHealth >= health.maxHealth - 0.5f) return "Health is already full";

            Storage.Remove(item, 1);
            float healed = health.Heal(item.useAmount);
            return $"Used {item.DisplayName} (+{Mathf.RoundToInt(healed)} health)";
        }

        /// <summary>Adds items and returns how many units were accepted.</summary>
        public int AddItem(ItemDefinition item, int amount)
        {
            if (item == null || amount <= 0) return 0;

            if (item.category == ItemCategory.Currency)
            {
                AddMoney(amount * Mathf.Max(1, item.useAmount));
                return amount;
            }

            if (item.category == ItemCategory.Ammo)
            {
                int rounds = amount * Mathf.Max(1, item.useAmount);
                WeaponController weapon = FindWeapon();
                if (weapon != null) weapon.AddReserveAmmo(rounds);
                else AddLooseAmmo(rounds);
                return amount;
            }

            return Storage.Add(item, amount);
        }

        public void AddMoney(int amount)
        {
            EnsureMoney();
            _money += amount;
            MoneyChanged?.Invoke(_money);
        }

        public bool SpendMoney(int amount)
        {
            EnsureMoney();
            if (amount > _money) return false;
            _money -= amount;
            MoneyChanged?.Invoke(_money);
            return true;
        }

        /// <summary>Removes items from the backpack and drops them in front of the player.</summary>
        public int DropItem(ItemDefinition item, int amount)
        {
            int removed = Storage.Remove(item, amount);
            if (removed <= 0) return 0;

            if (dropPickupPrefab != null)
            {
                Vector3 position = transform.position + transform.forward * dropDistance + Vector3.up * 0.5f;
                WorldPickup.Spawn(dropPickupPrefab, item, removed, position, null);
            }
            return removed;
        }
    }
}
