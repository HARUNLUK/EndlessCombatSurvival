using System;
using UnityEngine;
using EndlessCombat.Combat;

namespace EndlessSurvival.Inventory
{
    /// <summary>
    /// Player carried inventory: a weight limited backpack, a money wallet and a link to the weapon reserve ammo.
    /// Ammo and Currency items never enter the backpack; they are converted on pickup.
    /// </summary>
    public class PlayerInventory : MonoBehaviour, IAmmoPool
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

        // ------------------------------------------------------------------ Ammo pools (one per kind)

        [Header("Ammo")]
        [Tooltip("Weight of one round per kind (pistol, rifle, shotgun shell). Carried rounds count toward the weight limit.")]
        public float pistolRoundWeight = 0.012f;
        public float rifleRoundWeight = 0.02f;
        public float shellWeight = 0.04f;

        private readonly int[] _ammo = new int[4];

        /// <summary>Raised when any ammo pool changes.</summary>
        public event Action AmmoChanged;

        public int Count(AmmoKind kind) => kind == AmmoKind.None ? 0 : _ammo[(int)kind];
        public int Count(AmmoType kind) => Count((AmmoKind)(int)kind);

        public void Add(AmmoKind kind, int amount)
        {
            if (kind == AmmoKind.None || amount <= 0) return;
            _ammo[(int)kind] += amount;
            OnAmmoChanged();
        }

        public int Take(AmmoKind kind, int amount)
        {
            if (kind == AmmoKind.None || amount <= 0) return 0;
            int taken = Mathf.Min(amount, _ammo[(int)kind]);
            _ammo[(int)kind] -= taken;
            if (taken > 0) OnAmmoChanged();
            return taken;
        }

        /// <summary>Sets a pool directly (loading a save).</summary>
        public void SetAmmo(AmmoKind kind, int amount)
        {
            if (kind == AmmoKind.None) return;
            _ammo[(int)kind] = Mathf.Max(0, amount);
            OnAmmoChanged();
        }

        private void OnAmmoChanged()
        {
            Storage.ExtraWeight = AmmoWeight + CarriedWeight;
            AmmoChanged?.Invoke();
            var weapon = FindWeapon();
            if (weapon != null) weapon.RefreshAmmo();
        }

        public float AmmoWeight =>
            _ammo[(int)AmmoKind.Pistol] * pistolRoundWeight + _ammo[(int)AmmoKind.Rifle] * rifleRoundWeight + _ammo[(int)AmmoKind.Shotgun] * shellWeight;

        private float _carriedWeight;

        /// <summary>Weight of the worn and carried equipment (weapons on the body, shield, clothes). Set by PlayerEquipment.</summary>
        public float CarriedWeight
        {
            get => _carriedWeight;
            set
            {
                _carriedWeight = Mathf.Max(0f, value);
                Storage.ExtraWeight = AmmoWeight + _carriedWeight;
            }
        }

        /// <summary>All rounds together (old save field).</summary>
        public int LooseAmmo => _ammo[1] + _ammo[2] + _ammo[3];

        /// <summary>Old single pool: rounds without a kind go to the rifle pool.</summary>
        public void AddLooseAmmo(int rounds) => Add(AmmoKind.Rifle, rounds);

        private WeaponController FindWeapon()
        {
            var shooter = GetComponentInChildren<PlayerShooter>(true);
            return shooter != null ? shooter.CurrentWeapon : null;
        }

        private string UseSurvivalItem(ItemDefinition item)
        {
            var survival = GetComponent<PlayerSurvival>();
            if (survival == null) return "Şu an kullanılamaz";

            bool isFood = item.category == ItemCategory.Food;
            float missing = isFood ? survival.maxHunger - survival.Hunger : survival.maxThirst - survival.Thirst;

            // Salt water and similar: drinking is allowed even when it makes things worse
            bool thirstHelps = isFood && item.bonusThirst > 0f && survival.maxThirst - survival.Thirst >= 0.5f;
            if (missing < 0.5f && item.thirstPenalty <= 0f && !thirstHelps && !EndlessSurvival.Survival.SurvivalTest.FreeResources) // test mode: eat/drink any time
                return isFood ? "Aç değilsin" : "Susamadın";

            Storage.Remove(item, 1);
            float restored = isFood ? survival.Eat(item.useAmount) : survival.Drink(item.useAmount);
            float quenched = 0f;
            if (isFood && item.bonusThirst > 0f) quenched = survival.Drink(item.bonusThirst);
            if (item.thirstPenalty > 0f) survival.SetValues(survival.Hunger, survival.Thirst - item.thirstPenalty, survival.Bladder);
            GiveBack(item);
            ItemConsumed?.Invoke(item, this);
            string penalty = item.thirstPenalty > 0f ? $" (susuzluk -{Mathf.RoundToInt(item.thirstPenalty)}, tuzlu!)" : "";
            string bonus = quenched >= 0.5f ? $", su +{Mathf.RoundToInt(quenched)}" : "";
            return $"{item.DisplayName} {(isFood ? "yendi" : "içildi")} ({(isFood ? "tokluk" : "su")} +{Mathf.RoundToInt(restored)}{bonus}){penalty}";
        }

        /// <summary>Raised after a consumable was used (food, drink, medicine). Survival systems apply extra effects.</summary>
        public static event Action<ItemDefinition, PlayerInventory> ItemConsumed;

        /// <summary>Survival systems with their own use action for an item (returns a message, or null when not theirs).</summary>
        public static event Func<ItemDefinition, PlayerInventory, string> CustomUse;

        /// <summary>First aid stat: multiplier for health restored by medical items.</summary>
        public static Func<float> HealMultiplier = () => 1f;

        /// <summary>
        /// Non-consumable items with their own "Use" action (binoculars: a Gear item that is not used up).
        /// Survival systems add a check; the inventory shows a Use button when any check says yes.
        /// </summary>
        public static event Func<ItemDefinition, bool> CustomUsable;

        public static bool IsCustomUsable(ItemDefinition item)
        {
            if (item == null || CustomUsable == null) return false;
            foreach (Func<ItemDefinition, bool> check in CustomUsable.GetInvocationList())
                if (check(item)) return true;
            return false;
        }

        private void GiveBack(ItemDefinition item)
        {
            if (item.leavesItem != null) Storage.Add(item.leavesItem, 1);
        }

        /// <summary>Consumes one consumable from the backpack. Returns a feedback message.</summary>
        public string UseItem(ItemDefinition item)
        {
            if (item == null || Storage.GetCount(item) <= 0) return null;
            if (!item.IsConsumable && !IsCustomUsable(item)) return null;

            // Items with their own action (cigarette: smoking). A handler returns a message, or null when not its item.
            if (CustomUse != null)
                foreach (Func<ItemDefinition, PlayerInventory, string> handler in CustomUse.GetInvocationList())
                {
                    string custom = handler(item, this);
                    if (custom != null) return custom;
                }
            if (!item.IsConsumable) return null;

            if (item.category == ItemCategory.Food || item.category == ItemCategory.Drink)
                return UseSurvivalItem(item);

            var health = GetComponent<PlayerHealth>();
            if (health == null) return "Şu an kullanılamaz";
            if (health.IsDead) return null;
            bool hasEffect = item.effects != ConsumableEffect.None;
            if (!hasEffect && health.CurrentHealth >= health.maxHealth - 0.5f && !health.isBleeding && !EndlessSurvival.Survival.SurvivalTest.FreeResources)
                return "Canın zaten dolu";

            Storage.Remove(item, 1);
            float healed = item.useAmount > 0 ? health.Heal(item.useAmount * Mathf.Max(0.1f, HealMultiplier())) : 0f;
            bool stoppedBleed = false;
            // Medical items with a heal amount stop bleeding; pure effect items (ointment, tea) do not
            if (item.useAmount > 0)
            {
                stoppedBleed = health.isBleeding;
                health.StopBleeding();
            }
            GiveBack(item);
            ItemConsumed?.Invoke(item, this);

            string bleedNote = stoppedBleed ? ", kanama durdu!" : "";
            string healNote = healed > 0f ? $" (can +{Mathf.RoundToInt(healed)}{bleedNote})" : bleedNote;
            return $"{item.DisplayName} kullanıldı{healNote}";
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
                // Rounds go to the pool of their kind; every firearm of that kind reloads from it
                Add((AmmoKind)(int)item.AmmoKind, rounds);
                return amount;
            }

            int added = Storage.Add(item, amount);
            if (added > 0)
            {
                EndlessSurvival.World.GameStatsTracker.Instance?.RecordItemLooted(added);
            }
            return added;
        }

        public void AddMoney(int amount)
        {
            EnsureMoney();
            _money += amount;
            MoneyChanged?.Invoke(_money);
        }

        /// <summary>Sets the wallet directly (loading a save).</summary>
        public void SetMoney(int amount)
        {
            EnsureMoney();
            _money = Mathf.Max(0, amount);
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
