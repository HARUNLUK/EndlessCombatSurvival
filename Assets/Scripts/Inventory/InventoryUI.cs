using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using StarterAssets;
using EndlessSurvival.Vehicle;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace EndlessSurvival.Inventory
{
    /// <summary>
    /// Hierarchy-driven inventory screen. Every element is defined in the scene Canvas;
    /// this script only fills in text, toggles rows and forwards button clicks.
    /// Tab opens/closes the screen. The right column shows the vehicle stash (near or inside the vehicle)
    /// or, when opened with E on a chest, the chest contents.
    /// </summary>
    public class InventoryUI : MonoBehaviour
    {
        /// <summary>Bumped whenever the hierarchy layout produced by the setup tool changes.</summary>
        public const int CurrentUiVersion = 3;

        public static bool IsOpen { get; private set; }
        public static InventoryUI Instance { get; private set; }

        [HideInInspector] public int uiVersion;

        [Header("Data")]
        public ItemDatabase database;
        public PlayerInventory playerInventory;
        public VehicleStorage vehicleStorage;
        public VehicleMaintenance vehicleMaintenance;
        public VehicleInteraction vehicleInteraction;

        [Header("Input Lock")]
        [Tooltip("Starter Assets input component; look input is disabled while the screen is open")]
        public StarterAssetsInputs playerInputs;

        [Tooltip("Vehicle speed (km/h) above which the screen cannot be opened")]
        public float maxOpenSpeedKmh = 3f;

        [Tooltip("A chest screen closes automatically when the player walks farther than this from the chest")]
        public float chestCloseDistance = 5f;

        [Header("HUD (always visible)")]
        public Text moneyText;
        public Text weightText;

        [Header("Panel")]
        public GameObject inventoryPanel;
        public Text playerTitleText;
        public Text vehicleTitleText;
        public Text vehicleInfoText;
        public Text statusText;

        [Tooltip("Backpack rows. Buttons: 0 Equip/Use, 1 Store 1, 2 Store All, 3 Drop 1, 4 Drop All")]
        public ItemRowUI[] playerRows;

        [Tooltip("Right column rows (vehicle stash or chest). Buttons: 0 Take 1, 1 Take All")]
        public ItemRowUI[] vehicleRows;

        [Header("Equipment Panel (one row per EquipSlot, in enum order)")]
        public PlayerEquipment equipment;
        public Text equipTitleText;
        [Tooltip("Buttons: 0 Unequip")]
        public ItemRowUI[] equipRows;

        [Header("Vehicle Actions")]
        public Button storeAllButton;
        public Button refuelButton;
        public Text refuelLabel;
        public Button repairButton;
        public Text repairLabel;
        public Button[] upgradeButtons;
        public Text[] upgradeLabels;

        private const int AllUnits = int.MaxValue;

        private float _nextRefresh;
        private bool _wasCursorLocked;
        private bool _warnedRowShortage;
        private LootChest _chest;

        private void Awake()
        {
            IsOpen = false;
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            IsOpen = false;
        }

        private void Start()
        {
            if (uiVersion != CurrentUiVersion)
            {
                Debug.LogError("[InventoryUI] The inventory UI in the scene is outdated. Run 'Endless Survival/Setup Inventory System' and save the scene.");
                if (statusText != null) statusText.text = "UI is outdated: run Endless Survival > Setup Inventory System";
                return;
            }

            WireRows();
            WireActions();

            if (playerInventory != null)
            {
                playerInventory.Storage.Changed += Refresh;
                playerInventory.MoneyChanged += OnMoneyChanged;
            }

            if (equipment != null) equipment.Changed += Refresh;
            if (vehicleStorage != null) vehicleStorage.Storage.Changed += Refresh;
            if (vehicleMaintenance != null) vehicleMaintenance.Changed += Refresh;

            if (inventoryPanel != null) inventoryPanel.SetActive(false);
            Refresh();
        }

        private static void Wire(ItemRowUI row, int buttonIndex, UnityAction action)
        {
            if (row == null) return;
            row.EnsureButtons();
            if (row.buttons == null || buttonIndex >= row.buttons.Length || row.buttons[buttonIndex] == null) return;
            row.buttons[buttonIndex].onClick.AddListener(action);
        }

        private void WireRows()
        {
            for (int i = 0; playerRows != null && i < playerRows.Length; i++)
            {
                int index = i;
                Wire(playerRows[i], 0, () => OnUseClicked(index));
                Wire(playerRows[i], 1, () => OnStoreClicked(index, 1));
                Wire(playerRows[i], 2, () => OnStoreClicked(index, AllUnits));
                Wire(playerRows[i], 3, () => OnDropClicked(index, 1));
                Wire(playerRows[i], 4, () => OnDropClicked(index, AllUnits));
            }

            for (int i = 0; vehicleRows != null && i < vehicleRows.Length; i++)
            {
                int index = i;
                Wire(vehicleRows[i], 0, () => OnTakeClicked(index, 1));
                Wire(vehicleRows[i], 1, () => OnTakeClicked(index, AllUnits));
            }

            for (int i = 0; equipRows != null && i < equipRows.Length; i++)
            {
                int index = i;
                Wire(equipRows[i], 0, () => OnUnequipClicked(index));
            }
        }

        private void WireActions()
        {
            if (storeAllButton != null) storeAllButton.onClick.AddListener(OnStoreEverythingClicked);
            if (refuelButton != null) refuelButton.onClick.AddListener(() => ShowStatus(vehicleMaintenance != null ? vehicleMaintenance.Refuel() : null));
            if (repairButton != null) repairButton.onClick.AddListener(() => ShowStatus(vehicleMaintenance != null ? vehicleMaintenance.Repair() : null));

            for (int i = 0; upgradeButtons != null && i < upgradeButtons.Length; i++)
            {
                int index = i;
                if (upgradeButtons[i] != null)
                    upgradeButtons[i].onClick.AddListener(() => ShowStatus(vehicleMaintenance != null ? vehicleMaintenance.Upgrade(index) : null));
            }
        }

        private void Update()
        {
            if (TogglePressed())
            {
                if (IsOpen) Close();
                else TryOpen();
            }
            else if (IsOpen && ClosePressed())
            {
                Close();
            }

            if (IsOpen && _chest != null && !ChestInReach()) Close();

            if (IsOpen && Time.unscaledTime >= _nextRefresh)
            {
                _nextRefresh = Time.unscaledTime + 0.25f;
                Refresh();
            }
        }

        private bool ChestInReach()
        {
            if (_chest == null || playerInventory == null) return false;
            return Vector3.Distance(playerInventory.transform.position, _chest.transform.position) <= chestCloseDistance;
        }

        private static bool TogglePressed()
        {
#if ENABLE_INPUT_SYSTEM
            return Keyboard.current != null && Keyboard.current.tabKey.wasPressedThisFrame;
#else
            return Input.GetKeyDown(KeyCode.Tab);
#endif
        }

        private static bool ClosePressed()
        {
#if ENABLE_INPUT_SYSTEM
            return Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame;
#else
            return Input.GetKeyDown(KeyCode.Escape);
#endif
        }

        private void TryOpen()
        {
            if (vehicleMaintenance != null && vehicleMaintenance.vehicle != null &&
                vehicleInteraction != null && vehicleInteraction.IsPlayerInside &&
                vehicleMaintenance.vehicle.CurrentSpeedKmh > maxOpenSpeedKmh)
            {
                return;
            }

            Open();
        }

        /// <summary>Opens the screen with the chest contents shown in the right column.</summary>
        public void OpenChest(LootChest chest)
        {
            _chest = chest;
            if (IsOpen) Refresh();
            else Open();
        }

        public void Open()
        {
            IsOpen = true;

            _wasCursorLocked = Cursor.lockState == CursorLockMode.Locked;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            if (playerInputs != null)
            {
                playerInputs.cursorInputForLook = false;
                playerInputs.look = Vector2.zero;
            }

            if (inventoryPanel != null) inventoryPanel.SetActive(true);
            if (statusText != null) statusText.text = string.Empty;
            Refresh();
        }

        public void Close()
        {
            IsOpen = false;
            _chest = null;

            if (inventoryPanel != null) inventoryPanel.SetActive(false);

            if (playerInputs != null) playerInputs.cursorInputForLook = true;

            if (_wasCursorLocked)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
        }

        private bool VehicleAccessible()
        {
            if (vehicleStorage == null) return false;
            if (vehicleInteraction != null && vehicleInteraction.IsPlayerInside) return true;
            return playerInventory != null && vehicleStorage.IsInRange(playerInventory.transform.position);
        }

        /// <summary>The storage shown in the right column: the open chest, or the vehicle stash when reachable.</summary>
        private ItemStorage GetContainer()
        {
            if (_chest != null) return _chest.Storage;
            if (VehicleAccessible()) return vehicleStorage.Storage;
            return null;
        }

        private void OnMoneyChanged(int money)
        {
            UpdateHud();
        }

        private void UpdateHud()
        {
            if (moneyText != null && playerInventory != null)
                moneyText.text = $"MONEY: {playerInventory.Money}";

            if (weightText != null && playerInventory != null)
                weightText.text = $"BACKPACK: {playerInventory.Storage.TotalWeight:0.0} / {playerInventory.Storage.MaxWeight:0.#} kg";
        }

        public void Refresh()
        {
            UpdateHud();
            if (!IsOpen || database == null || playerInventory == null) return;

            ItemStorage container = GetContainer();
            bool hasContainer = container != null;
            bool chestMode = _chest != null;
            ItemStorage backpack = playerInventory.Storage;

            if (playerTitleText != null)
                playerTitleText.text = $"BACKPACK  {backpack.TotalWeight:0.0} / {backpack.MaxWeight:0.#} kg   Money: {playerInventory.Money}";

            CheckRowCount();

            for (int i = 0; playerRows != null && i < playerRows.Length; i++)
            {
                if (playerRows[i] == null) continue;

                ItemDefinition item = database.Get(i);
                int count = backpack.GetCount(item);
                if (item == null || count <= 0)
                {
                    playerRows[i].Hide();
                    continue;
                }

                string useText = null;
                if (item.IsEquippable) useText = "Equip";
                else if (item.category == ItemCategory.Food) useText = "Eat";
                else if (item.category == ItemCategory.Drink) useText = "Drink";
                else if (item.IsConsumable) useText = "Use";
                bool many = count > 1;
                string putWord = chestMode ? "Put" : "Store";

                playerRows[i].Show($"{item.DisplayName} x{count}  ({item.weight * count:0.0} kg)",
                    new ItemRowUI.RowButton(useText, true),
                    new ItemRowUI.RowButton($"{putWord} 1", hasContainer),
                    new ItemRowUI.RowButton(many ? $"{putWord} All" : null, hasContainer),
                    new ItemRowUI.RowButton("Drop 1", true),
                    new ItemRowUI.RowButton(many ? "Drop All" : null, true));
            }

            RefreshEquipment();

            if (vehicleTitleText != null)
            {
                if (chestMode) vehicleTitleText.text = container.IsEmpty ? "CHEST (empty)" : "CHEST";
                else vehicleTitleText.text = hasContainer ? "VEHICLE STORAGE (unlimited)" : "VEHICLE STORAGE (out of reach)";
            }

            for (int i = 0; vehicleRows != null && i < vehicleRows.Length; i++)
            {
                if (vehicleRows[i] == null) continue;

                ItemDefinition item = database.Get(i);
                int count = hasContainer ? container.GetCount(item) : 0;
                if (item == null || count <= 0)
                {
                    vehicleRows[i].Hide();
                    continue;
                }

                vehicleRows[i].Show($"{item.DisplayName} x{count}",
                    new ItemRowUI.RowButton("Take 1", true),
                    new ItemRowUI.RowButton(count > 1 ? "Take All" : null, true));
            }

            RefreshVehicleInfo(VehicleAccessible(), chestMode);
        }

        private void RefreshEquipment()
        {
            if (equipment == null) return;

            if (equipTitleText != null)
                equipTitleText.text = $"EQUIPPED   Armor {equipment.TotalArmor:0}  (-{equipment.DamageReduction * 100f:0}% damage)";

            for (int i = 0; equipRows != null && i < equipRows.Length; i++)
            {
                if (equipRows[i] == null) continue;

                var slot = (EquipSlot)i;
                ItemDefinition worn = equipment.Get(slot);
                if (worn == null)
                    equipRows[i].Show($"{slot}: (empty)", new ItemRowUI.RowButton(null, false));
                else if (slot == EquipSlot.Weapon)
                    equipRows[i].Show($"{slot}: {worn.DisplayName}", new ItemRowUI.RowButton("Unequip", true));
                else
                    equipRows[i].Show($"{slot}: {worn.DisplayName} (+{worn.armor:0})", new ItemRowUI.RowButton("Unequip", true));
            }
        }

        private void RefreshVehicleInfo(bool vehicleAccess, bool chestMode)
        {
            VehicleController vehicle = vehicleMaintenance != null ? vehicleMaintenance.vehicle : null;

            if (vehicleInfoText != null)
            {
                if (chestMode || vehicle == null)
                {
                    vehicleInfoText.text = string.Empty;
                }
                else
                {
                    string state = vehicle.IsBroken ? "  [BROKEN]" : (vehicle.currentFuel <= 0f ? "  [NO FUEL]" : string.Empty);
                    vehicleInfoText.text = $"Fuel {vehicle.currentFuel:0} / {vehicle.maxFuel:0}    Hull {vehicle.currentHealth:0} / {vehicle.maxHealth:0}{state}";
                }
            }

            SetActionButton(storeAllButton, !chestMode, vehicleAccess);
            SetActionButton(refuelButton, !chestMode, vehicleAccess);
            SetActionButton(repairButton, !chestMode, vehicleAccess);
            if (refuelLabel != null && vehicleMaintenance != null) refuelLabel.text = vehicleMaintenance.GetRefuelLabel();
            if (repairLabel != null && vehicleMaintenance != null) repairLabel.text = vehicleMaintenance.GetRepairLabel();

            for (int i = 0; upgradeButtons != null && i < upgradeButtons.Length; i++)
            {
                bool valid = vehicleMaintenance != null && i < vehicleMaintenance.upgrades.Count;
                SetActionButton(upgradeButtons[i], valid && !chestMode, vehicleAccess);
                if (valid && upgradeLabels != null && i < upgradeLabels.Length && upgradeLabels[i] != null)
                    upgradeLabels[i].text = vehicleMaintenance.GetUpgradeLabel(i);
            }
        }

        private static void SetActionButton(Button button, bool visible, bool interactable)
        {
            if (button == null) return;
            button.gameObject.SetActive(visible);
            button.interactable = interactable;
        }

        private void CheckRowCount()
        {
            if (_warnedRowShortage || database == null) return;

            int rows = playerRows != null ? playerRows.Length : 0;
            if (rows < database.Count)
            {
                _warnedRowShortage = true;
                Debug.LogWarning($"[InventoryUI] Item database has {database.Count} items but only {rows} rows exist in the panel. Re-run 'Endless Survival/Setup Inventory System'.");
            }
        }

        // ------------------------------------------------------------------ Button handlers

        private void OnUseClicked(int index)
        {
            ItemDefinition item = database.Get(index);
            if (item == null) return;

            if (item.IsEquippable)
                ShowStatus(equipment != null ? equipment.Equip(item) : "Equipment is not set up");
            else if (item.IsConsumable)
                ShowStatus(playerInventory.UseItem(item));
        }

        private void OnUnequipClicked(int slotIndex)
        {
            if (equipment == null) return;
            ShowStatus(equipment.Unequip((EquipSlot)slotIndex));
        }

        private void OnStoreClicked(int index, int amount)
        {
            ItemStorage container = GetContainer();
            if (container == null) { ShowStatus("Nothing in reach to store items in"); return; }

            ItemDefinition item = database.Get(index);
            if (item == null) return;

            int moved = Mathf.Min(amount, playerInventory.Storage.GetCount(item));
            moved = container.Add(item, moved);
            if (moved > 0) playerInventory.Storage.Remove(item, moved);
        }

        private void OnTakeClicked(int index, int amount)
        {
            ItemStorage container = GetContainer();
            if (container == null) { ShowStatus("Nothing in reach"); return; }

            ItemDefinition item = database.Get(index);
            if (item == null) return;

            int wanted = Mathf.Min(amount, container.GetCount(item));
            int accepted = playerInventory.AddItem(item, wanted);
            if (accepted <= 0)
            {
                ShowStatus("Backpack is too heavy");
                return;
            }

            container.Remove(item, accepted);
            ShowStatus($"Took {item.DisplayName} x{accepted}");
        }

        private void OnDropClicked(int index, int amount)
        {
            ItemDefinition item = database.Get(index);
            if (item == null) return;

            int dropped = playerInventory.DropItem(item, Mathf.Min(amount, playerInventory.Storage.GetCount(item)));
            if (dropped > 0) ShowStatus($"Dropped {item.DisplayName} x{dropped}");
        }

        private void OnStoreEverythingClicked()
        {
            ItemStorage container = GetContainer();
            if (container == null) { ShowStatus("Vehicle is out of reach"); return; }

            int stacks = 0;
            for (int i = 0; i < database.Count; i++)
            {
                ItemDefinition item = database.Get(i);
                if (item == null) continue;

                int count = playerInventory.Storage.GetCount(item);
                if (count <= 0) continue;

                int accepted = container.Add(item, count);
                if (accepted <= 0) continue;

                playerInventory.Storage.Remove(item, accepted);
                stacks++;
            }
            ShowStatus(stacks > 0 ? "Stored everything" : "Nothing to store");
        }

        private void ShowStatus(string message)
        {
            if (statusText != null && !string.IsNullOrEmpty(message)) statusText.text = message;
            Refresh();
        }
    }
}
