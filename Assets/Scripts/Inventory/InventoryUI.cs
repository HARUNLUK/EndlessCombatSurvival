using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using EndlessSurvival.Vehicle;
using EndlessSurvival.Survival;
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
        public EndlessCombat.Character.PlayerInputState playerInputs;

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

        /// <summary>
        /// The panel was built by the setup tool with English labels that are saved in the scene; swap them for
        /// Turkish at runtime. The duplicate close hint goes away (the Tab screen has its own).
        /// </summary>
        private void TranslateStaticTexts()
        {
            Canvas canvas = GetComponentInParent<Canvas>();
            Transform root = canvas != null ? canvas.rootCanvas.transform : (inventoryPanel != null ? inventoryPanel.transform : transform);
            foreach (Text t in root.GetComponentsInChildren<Text>(true))
            {
                switch (t.text)
                {
                    case "Store All": t.text = "Hepsini bagaja koy"; break;
                    case "Refuel": t.text = "Yakıt doldur"; break;
                    case "Repair": t.text = "Tamir et"; break;
                    case "Upgrade": t.text = "Yükselt"; break;
                    case "YOU DIED": t.text = "ÖLDÜN"; break;
                    case "BACKPACK": t.text = "ÇANTA"; break;
                    case "EQUIPPED": t.text = "KUŞANILAN"; break;
                    case "VEHICLE STORAGE": t.text = "ARAÇ BAGAJI"; break;
                    case "TAB / ESC: close": t.gameObject.SetActive(false); break;
                }
            }
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

            // One row per database item: items added after the panel was built get copies of the last row
            playerRows = ExtendRows(playerRows, "PlayerRow");
            vehicleRows = ExtendRows(vehicleRows, "VehicleRow");
            equipRows = ExtendEquipRows(equipRows);

            WireRows();
            WireActions();
            TranslateStaticTexts();

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

        private ItemRowUI[] ExtendRows(ItemRowUI[] rows, string prefix)
        {
            if (database == null || rows == null || rows.Length == 0) return rows;
            ItemRowUI template = rows[rows.Length - 1];
            if (template == null || rows.Length >= database.Count) return rows;

            var list = new System.Collections.Generic.List<ItemRowUI>(rows);
            while (list.Count < database.Count)
            {
                // Rows sit in a VerticalLayoutGroup, so a copy under the same parent stacks below the others
                ItemRowUI copy = Instantiate(template, template.transform.parent);
                copy.name = $"{prefix}_{list.Count}";
                list.Add(copy);
            }
            return list.ToArray();
        }

        /// <summary>
        /// Equip slots added after the panel was built (Shield): a copy of the last row one line below it.
        /// </summary>
        private static ItemRowUI[] ExtendEquipRows(ItemRowUI[] rows)
        {
            int slotCount = System.Enum.GetValues(typeof(EquipSlot)).Length;
            if (rows == null || rows.Length == 0 || rows.Length >= slotCount) return rows;
            ItemRowUI template = rows[rows.Length - 1];
            if (template == null) return rows;

            var list = new System.Collections.Generic.List<ItemRowUI>(rows);
            var templateRect = template.GetComponent<RectTransform>();
            while (list.Count < slotCount)
            {
                ItemRowUI copy = Instantiate(template, template.transform.parent);
                copy.name = $"EquipRow_{(EquipSlot)list.Count}";
                var rect = copy.GetComponent<RectTransform>();
                if (rect != null && templateRect != null)
                    rect.anchoredPosition = templateRect.anchoredPosition + new Vector2(0f, -34f * (list.Count - rows.Length + 1));
                list.Add(copy);
            }
            return list.ToArray();
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
            if (IsOpen) UpdatePadFocus();

            if (IsOpen && Time.unscaledTime >= _nextRefresh)
            {
                _nextRefresh = Time.unscaledTime + 0.25f;
                Refresh();
            }
        }

        // ------------------------------------------------------------------ Gamepad focus (backpack tab)

        private bool _padColorsSet;
        private GameObject _lastSelected;

        /// <summary>
        /// Backpack tab with a gamepad: one button always has the focus (orange), the D-pad / left stick moves it
        /// (Unity UI navigation), A presses it. A hidden row loses the focus, so it is given back to the nearest
        /// visible button. The list scrolls to keep the focused button in view.
        /// </summary>
        private void UpdatePadFocus()
        {
            var es = UnityEngine.EventSystems.EventSystem.current;
            if (es == null || inventoryPanel == null) return;
            if (!inventoryPanel.activeInHierarchy)
            {
                // Another tab is shown: nothing of the hidden window may keep the focus
                if (es.currentSelectedGameObject != null && es.currentSelectedGameObject.transform.IsChildOf(inventoryPanel.transform))
                    es.SetSelectedGameObject(null);
                return;
            }

            if (!_padColorsSet)
            {
                _padColorsSet = true;
                foreach (var sel in inventoryPanel.GetComponentsInChildren<Selectable>(true))
                {
                    var c = sel.colors;
                    c.selectedColor = new Color(1f, 0.72f, 0.25f, 1f);
                    c.highlightedColor = new Color(1f, 0.9f, 0.7f, 1f);
                    sel.colors = c;
                    var nav = sel.navigation;
                    nav.mode = Navigation.Mode.Automatic;
                    sel.navigation = nav;
                }
            }

            if (!EndlessCombat.Combat.GameInput.UsingPad) return;

            GameObject cur = es.currentSelectedGameObject;
            bool valid = cur != null && cur.activeInHierarchy && cur.transform.IsChildOf(inventoryPanel.transform);
            if (valid)
            {
                var s = cur.GetComponent<Selectable>();
                valid = s != null && s.IsInteractable();
            }
            if (!valid)
            {
                Selectable best = null;
                float bestDist = float.MaxValue;
                Vector3 from = _lastSelected != null ? _lastSelected.transform.position : new Vector3(float.MinValue, float.MaxValue, 0f);
                foreach (var sel in inventoryPanel.GetComponentsInChildren<Selectable>(false))
                {
                    if (!sel.IsInteractable() || !sel.gameObject.activeInHierarchy) continue;
                    float d;
                    if (_lastSelected != null) d = (sel.transform.position - from).sqrMagnitude;
                    else d = -sel.transform.position.y * 10000f + sel.transform.position.x; // top left first
                    if (d < bestDist) { bestDist = d; best = sel; }
                }
                if (best != null) es.SetSelectedGameObject(best.gameObject);
                cur = best != null ? best.gameObject : null;
            }
            if (cur != null && cur != _lastSelected) ScrollIntoView(cur.transform as RectTransform);
            if (cur != null) _lastSelected = cur;
        }

        private static readonly Vector3[] CornersA = new Vector3[4], CornersB = new Vector3[4];

        private static void ScrollIntoView(RectTransform target)
        {
            if (target == null) return;
            var scroll = target.GetComponentInParent<ScrollRect>();
            if (scroll == null || scroll.content == null) return;
            RectTransform view = scroll.viewport != null ? scroll.viewport : scroll.transform as RectTransform;
            target.GetWorldCorners(CornersA);
            view.GetWorldCorners(CornersB);
            float above = CornersA[1].y - CornersB[1].y; // target top above the view top
            float below = CornersB[0].y - CornersA[0].y; // target bottom under the view bottom
            float scale = Mathf.Max(0.0001f, view.lossyScale.y);
            Vector2 pos = scroll.content.anchoredPosition;
            if (above > 0f) pos.y -= above / scale + 6f;
            else if (below > 0f) pos.y += below / scale + 6f;
            else return;
            scroll.content.anchoredPosition = pos;
        }

        private bool ChestInReach()
        {
            if (_chest == null || playerInventory == null) return false;
            return Vector3.Distance(playerInventory.transform.position, _chest.transform.position) <= chestCloseDistance;
        }

        private static bool TogglePressed()
        {
#if ENABLE_INPUT_SYSTEM
            return (Keyboard.current != null && Keyboard.current.tabKey.wasPressedThisFrame) || EndlessCombat.Combat.GameInput.InventoryPressed; // Tab / gamepad View
#else
            return Input.GetKeyDown(KeyCode.Tab);
#endif
        }

        private static bool ClosePressed()
        {
#if ENABLE_INPUT_SYSTEM
            return (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
                || (Gamepad.current != null && Gamepad.current.buttonEast.wasPressedThisFrame && !EndlessSurvival.Survival.SurvivalPanel.PadBackConsumed); // Esc / gamepad B
#else
            return Input.GetKeyDown(KeyCode.Escape);
#endif
        }

        private void TryOpen()
        {
            // The screen opens while driving too (the user asked for it); maxOpenSpeedKmh is no longer used.
            Open();
        }

        /// <summary>Opens the screen with the chest contents shown in the right column.</summary>
        /// <summary>True while the screen shows a chest (it opens on the backpack tab).</summary>
        public bool HasChest => _chest != null;

        /// <summary>True while the screen is open on exactly this chest.</summary>
        public bool IsShowing(LootChest chest) => IsOpen && _chest != null && _chest == chest;

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

            // The window itself stays hidden when the Tab screen draws the backpack (it only lends its rectangle)
            if (inventoryPanel != null) inventoryPanel.SetActive(EndlessSurvival.Survival.SurvivalPanel.Instance == null);
            if (statusText != null) statusText.text = string.Empty;
            Refresh();
        }

        public void Close()
        {
            IsOpen = false;
            _chest = null;

            if (inventoryPanel != null) inventoryPanel.SetActive(false);
            var es = UnityEngine.EventSystems.EventSystem.current;
            if (es != null) es.SetSelectedGameObject(null);
            _lastSelected = null;
            EndlessCombat.Combat.GameInput.BlockGameplayBriefly();

            if (playerInputs != null) playerInputs.cursorInputForLook = true;

            if (_wasCursorLocked)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
        }

        /// <summary>The chest that is open, or the vehicle trunk when it is in reach (null when neither).</summary>
        public ItemStorage ContainerStorage => GetContainer();

        /// <summary>Title of the right column (chest name or the trunk).</summary>
        public string ContainerTitle => _chest != null ? LootChest.Tr(_chest.containerTitle) : "ARAÇ BAGAJI";

        /// <summary>The inventory window; the Tab panel's other tabs are drawn in the same rectangle.</summary>
        public RectTransform PanelRect => inventoryPanel != null ? inventoryPanel.transform as RectTransform : null;

        /// <summary>Shows/hides the inventory window while the screen stays open (other tab selected).</summary>
        public void SetPanelVisible(bool visible)
        {
            if (!IsOpen || inventoryPanel == null || inventoryPanel.activeSelf == visible) return;
            inventoryPanel.SetActive(visible);
            if (visible) Refresh();
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
                moneyText.text = $"Para: {playerInventory.Money}";

            if (weightText != null && playerInventory != null)
                weightText.text = $"Çanta: {playerInventory.Storage.TotalWeight:0.0} / {(playerInventory.Storage.IsUnlimited ? "sınırsız" : playerInventory.Storage.MaxWeight.ToString("0.#") + " kg")}";
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
                playerTitleText.text = $"ÇANTA  {backpack.TotalWeight:0.0} / {(backpack.IsUnlimited ? "sınırsız" : backpack.MaxWeight.ToString("0.#") + " kg")}   Para: {playerInventory.Money}";

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
                if (item.IsEquippable) useText = "Kuşan";
                else if (item.category == ItemCategory.Food) useText = "Ye";
                else if (item.category == ItemCategory.Drink) useText = "İç";
                else if (item.IsConsumable || PlayerInventory.IsCustomUsable(item)) useText = "Kullan";
                bool many = count > 1;
                string putWord = chestMode ? "Koy" : "Araca";

                playerRows[i].Show($"{item.DisplayName} x{count}  ({item.weight * count:0.0} kg)",
                    new ItemRowUI.RowButton(useText, true),
                    new ItemRowUI.RowButton($"{putWord} 1", hasContainer),
                    new ItemRowUI.RowButton(many ? "Tümünü koy" : null, hasContainer),
                    new ItemRowUI.RowButton("At 1", true),
                    new ItemRowUI.RowButton(many ? "Hepsini at" : null, true));
            }

            RefreshEquipment();

            if (vehicleTitleText != null)
            {
                if (chestMode) vehicleTitleText.text = container.IsEmpty ? LootChest.Tr(_chest.containerTitle) + " (boş)" : LootChest.Tr(_chest.containerTitle);
                else vehicleTitleText.text = hasContainer ? "ARAÇ BAGAJI (sınırsız)" : "ARAÇ BAGAJI (uzakta)";
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
                    new ItemRowUI.RowButton("Al 1", true),
                    new ItemRowUI.RowButton(count > 1 ? "Hepsini al" : null, true));
            }

            RefreshVehicleInfo(VehicleAccessible(), chestMode);
        }

        private void RefreshEquipment()
        {
            if (equipment == null) return;

            if (equipTitleText != null)
                equipTitleText.text = $"KUŞANILAN   Zırh {equipment.TotalArmor:0}  (hasar -%{equipment.DamageReduction * 100f:0})";

            for (int i = 0; equipRows != null && i < equipRows.Length; i++)
            {
                if (equipRows[i] == null) continue;

                var slot = (EquipSlot)i;
                if (slot == EquipSlot.Weapon)
                {
                    // Old single weapon row: the three weapon slots below replace it (details in the Karakter tab)
                    equipRows[i].Hide();
                    continue;
                }
                ItemDefinition worn = equipment.Get(slot);
                if (worn == null)
                    equipRows[i].Show($"{TurkishText.Slot(slot)}: (boş)", new ItemRowUI.RowButton(null, false));
                else if (slot == EquipSlot.Shield || slot == EquipSlot.Bow || slot == EquipSlot.LongGun || slot == EquipSlot.Pistol || slot == EquipSlot.Melee)
                    equipRows[i].Show($"{TurkishText.Slot(slot)}: {worn.DisplayName}", new ItemRowUI.RowButton("Çıkar", true));
                else
                    equipRows[i].Show($"{TurkishText.Slot(slot)}: {worn.DisplayName} (+{worn.armor:0})", new ItemRowUI.RowButton("Çıkar", true));
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
                    string state = vehicle.IsBroken ? "  [BOZUK]" : (vehicle.currentFuel <= 0f ? "  [YAKIT YOK]" : string.Empty);
                    vehicleInfoText.text = $"Yakıt {vehicle.currentFuel:0} / {vehicle.maxFuel:0}    Gövde {vehicle.currentHealth:0} / {vehicle.maxHealth:0}{state}";
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
                ShowStatus(equipment != null ? equipment.Equip(item) : "Ekipman sistemi kurulmamış");
            else if (item.IsConsumable || PlayerInventory.IsCustomUsable(item))
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
            if (container == null) { ShowStatus("Yakında eşya koyacak yer yok"); return; }

            ItemDefinition item = database.Get(index);
            if (item == null) return;

            int moved = Mathf.Min(amount, playerInventory.Storage.GetCount(item));
            moved = container.Add(item, moved);
            if (moved > 0) playerInventory.Storage.Remove(item, moved);
        }

        private void OnTakeClicked(int index, int amount)
        {
            ItemStorage container = GetContainer();
            if (container == null) { ShowStatus("Yakında bir şey yok"); return; }

            ItemDefinition item = database.Get(index);
            if (item == null) return;

            int wanted = Mathf.Min(amount, container.GetCount(item));
            int accepted = playerInventory.AddItem(item, wanted);
            if (accepted <= 0)
            {
                ShowStatus("Çanta çok ağır");
                return;
            }

            container.Remove(item, accepted);
            ShowStatus($"Alındı: {item.DisplayName} x{accepted}");
        }

        private void OnDropClicked(int index, int amount)
        {
            ItemDefinition item = database.Get(index);
            if (item == null) return;

            int dropped = playerInventory.DropItem(item, Mathf.Min(amount, playerInventory.Storage.GetCount(item)));
            if (dropped > 0) ShowStatus($"Atıldı: {item.DisplayName} x{dropped}");
        }

        private void OnStoreEverythingClicked()
        {
            ItemStorage container = GetContainer();
            if (container == null) { ShowStatus("Araç uzakta"); return; }

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
            ShowStatus(stacks > 0 ? "Hepsi bagaja kondu" : "Konacak bir şey yok");
        }

        private void ShowStatus(string message)
        {
            if (statusText != null && !string.IsNullOrEmpty(message)) statusText.text = message;
            Refresh();
        }
    }
}
