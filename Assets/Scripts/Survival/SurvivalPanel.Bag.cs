using System.Collections.Generic;
using UnityEngine;
using EndlessCombat.Combat;
using EndlessSurvival.Inventory;

namespace EndlessSurvival.Survival
{
    /// <summary>
    /// The "Çanta" (backpack) tab, drawn like the other tabs (it replaced the old scene UI window with its rows of
    /// small buttons, which could not be used with a gamepad).
    ///   Left  : the backpack.            Right : the open chest, or the vehicle trunk when the car is in reach.
    ///   Gamepad: D-pad / left stick moves, left / right changes the column, A opens the actions of the item
    ///            (use, equip, put in the trunk, drop...), X moves the whole stack to the other side at once,
    ///            Y takes everything from the chest / trunk, B goes back.
    ///   Mouse  : click an item for its actions, click an action.
    /// Fuel, repair and upgrades of the vehicle moved to the Araç tab.
    /// </summary>
    public partial class SurvivalPanel
    {
        private struct BagAction
        {
            public string label;
            public System.Action run;
        }

        private readonly List<ItemDefinition> _bagItems = new List<ItemDefinition>();
        private readonly List<ItemDefinition> _boxItems = new List<ItemDefinition>();
        private readonly List<BagAction> _bagActions = new List<BagAction>();
        private int _bagCol;                 // 0 backpack, 1 container
        private readonly int[] _bagSel = new int[2];
        private readonly int[] _bagTop = new int[2];
        private bool _bagMenu;
        private int _bagMenuSel;
        private ItemDefinition _bagMenuItem;
        private bool _bagMenuFromBox;
        private float _bagNavAt;
        private Vector2Int _bagHeld;
        private GUIStyle _bRow, _bRowSel, _bTag;

        private static PlayerInventory BagInventory => SurvivalDirector.Inventory;

        private static string CategoryName(ItemDefinition item)
        {
            if (item.IsShield) return "Kalkan";
            switch (item.category)
            {
                case ItemCategory.Weapon: return "Silah";
                case ItemCategory.Apparel: return "Giysi";
                case ItemCategory.Food: return "Yiyecek";
                case ItemCategory.Drink: return "İçecek";
                case ItemCategory.Consumable: return "İlaç";
                case ItemCategory.Ammo: return "Mermi";
                default: return "Malzeme";
            }
        }

        private static int CategoryOrder(ItemDefinition item)
        {
            switch (item.category)
            {
                case ItemCategory.Weapon: return 0;
                case ItemCategory.Apparel: return 1;
                case ItemCategory.Consumable: return 2;
                case ItemCategory.Food: return 3;
                case ItemCategory.Drink: return 4;
                default: return 5;
            }
        }

        private static void FillSorted(List<ItemDefinition> list, ItemStorage storage)
        {
            list.Clear();
            if (storage == null) return;
            foreach (var pair in storage.Items)
                if (pair.Key != null && pair.Value > 0) list.Add(pair.Key);
            list.Sort((a, b) =>
            {
                int c = CategoryOrder(a).CompareTo(CategoryOrder(b));
                return c != 0 ? c : string.CompareOrdinal(a.DisplayName, b.DisplayName);
            });
        }

        private ItemStorage BagContainer(out string title, out bool chest)
        {
            var ui = InventoryUI.Instance;
            chest = ui != null && ui.HasChest;
            ItemStorage box = ui != null ? ui.ContainerStorage : null;
            title = ui != null ? ui.ContainerTitle : "ARAÇ BAGAJI";
            return box;
        }

        private void RefreshBag()
        {
            var inv = BagInventory;
            FillSorted(_bagItems, inv != null ? inv.Storage : null);
            FillSorted(_boxItems, BagContainer(out _, out _));
            _bagSel[0] = Mathf.Clamp(_bagSel[0], 0, Mathf.Max(0, _bagItems.Count - 1));
            _bagSel[1] = Mathf.Clamp(_bagSel[1], 0, Mathf.Max(0, _boxItems.Count - 1));
            if (_bagCol == 1 && _boxItems.Count == 0 && BagContainer(out _, out _) == null) _bagCol = 0;
        }

        private ItemDefinition BagSelected(int col)
        {
            var list = col == 0 ? _bagItems : _boxItems;
            int i = _bagSel[col];
            return i >= 0 && i < list.Count ? list[i] : null;
        }

        // ------------------------------------------------------------------ actions

        private void BuildBagActions(ItemDefinition item, bool fromBox)
        {
            _bagActions.Clear();
            var inv = BagInventory;
            if (item == null || inv == null) return;
            ItemStorage box = BagContainer(out _, out bool chest);
            string boxWord = chest ? "Sandığa" : "Bagaja";

            if (fromBox)
            {
                int n = box != null ? box.GetCount(item) : 0;
                _bagActions.Add(new BagAction { label = "Al: 1", run = () => BagTake(item, 1) });
                if (n > 1) _bagActions.Add(new BagAction { label = $"Al: hepsi ({n})", run = () => BagTake(item, int.MaxValue) });
                return;
            }

            int count = inv.Storage.GetCount(item);
            var eq = DollEquipment;
            if (item.IsEquippable)
                _bagActions.Add(new BagAction { label = "Kuşan", run = () => _message = eq != null ? eq.Equip(item) : null });
            else if (item.category == ItemCategory.Food)
                _bagActions.Add(new BagAction { label = "Ye", run = () => _message = inv.UseItem(item) });
            else if (item.category == ItemCategory.Drink)
                _bagActions.Add(new BagAction { label = "İç", run = () => _message = inv.UseItem(item) });
            else if (item.IsConsumable || PlayerInventory.IsCustomUsable(item))
                _bagActions.Add(new BagAction { label = "Kullan", run = () => _message = inv.UseItem(item) });

            if (item.IsConsumable)
                _bagActions.Add(new BagAction
                {
                    label = "Hızlı eşya yap (yön ↓ / 4)",
                    run = () =>
                    {
                        var l = DollLoadout;
                        if (l != null) l.QuickItem = item;
                        _message = $"Hızlı eşya: {item.DisplayName}";
                    }
                });

            if (box != null)
            {
                _bagActions.Add(new BagAction { label = $"{boxWord} koy: 1", run = () => BagStore(item, 1) });
                if (count > 1) _bagActions.Add(new BagAction { label = $"{boxWord} koy: hepsi ({count})", run = () => BagStore(item, int.MaxValue) });
            }
            _bagActions.Add(new BagAction { label = "Yere bırak: 1", run = () => BagDrop(item, 1) });
            if (count > 1) _bagActions.Add(new BagAction { label = $"Yere bırak: hepsi ({count})", run = () => BagDrop(item, int.MaxValue) });
        }

        private void BagStore(ItemDefinition item, int amount)
        {
            var inv = BagInventory;
            ItemStorage box = BagContainer(out _, out _);
            if (inv == null || box == null) { _message = "Yakında eşya koyacak yer yok"; return; }
            int moved = Mathf.Min(amount, inv.Storage.GetCount(item));
            moved = box.Add(item, moved);
            if (moved > 0) inv.Storage.Remove(item, moved);
            _message = moved > 0 ? $"Kondu: {item.DisplayName} x{moved}" : "Sığmadı";
        }

        private void BagTake(ItemDefinition item, int amount)
        {
            var inv = BagInventory;
            ItemStorage box = BagContainer(out _, out _);
            if (inv == null || box == null) { _message = "Yakında bir şey yok"; return; }
            int wanted = Mathf.Min(amount, box.GetCount(item));
            int accepted = inv.AddItem(item, wanted);
            if (accepted <= 0) { _message = "Çanta çok ağır"; return; }
            box.Remove(item, accepted);
            _message = $"Alındı: {item.DisplayName} x{accepted}" + (accepted < wanted ? " (kalanı sığmadı)" : "");
        }

        private void BagTakeAll()
        {
            var inv = BagInventory;
            ItemStorage box = BagContainer(out _, out _);
            if (inv == null || box == null) return;
            int stacks = 0, left = 0;
            foreach (var item in new List<ItemDefinition>(_boxItems))
            {
                int n = box.GetCount(item);
                int got = inv.AddItem(item, n);
                if (got > 0) { box.Remove(item, got); stacks++; }
                if (got < n) left++;
            }
            _message = stacks == 0 ? (left > 0 ? "Çanta çok ağır" : "Alınacak bir şey yok") : left > 0 ? "Alındı (bazıları sığmadı: çanta ağır)" : "Hepsi alındı";
        }

        private void BagDrop(ItemDefinition item, int amount)
        {
            var inv = BagInventory;
            if (inv == null) return;
            int dropped = inv.DropItem(item, Mathf.Min(amount, inv.Storage.GetCount(item)));
            if (dropped > 0) _message = $"Yere bırakıldı: {item.DisplayName} x{dropped}";
        }

        private void OpenBagMenu(int col)
        {
            ItemDefinition item = BagSelected(col);
            if (item == null) return;
            BuildBagActions(item, col == 1);
            if (_bagActions.Count == 0) return;
            _bagMenu = true;
            _bagMenuSel = 0;
            _bagMenuItem = item;
            _bagMenuFromBox = col == 1;
        }

        private void RunBagAction(int index)
        {
            if (index < 0 || index >= _bagActions.Count) return;
            var action = _bagActions[index];
            _bagMenu = false;
            action.run();
            RefreshBag();
        }

        // ------------------------------------------------------------------ update (gamepad)

        private void UpdateBagTab()
        {
            RefreshBag();
            bool hasBox = BagContainer(out _, out _) != null;

            Vector2Int d = GameInput.PadDpadPressed;
            int dx = d.x, dy = -d.y;
            Vector2 stick = GameInput.PadLeftStick;
            Vector2Int held = Vector2Int.zero;
            if (stick.magnitude > 0.6f)
            {
                if (Mathf.Abs(stick.x) > Mathf.Abs(stick.y)) held.x = stick.x > 0 ? 1 : -1;
                else held.y = stick.y > 0 ? -1 : 1;
            }
            if (held != _bagHeld)
            {
                _bagHeld = held;
                _bagNavAt = Time.unscaledTime + 0.35f;
                if (dx == 0 && dy == 0) { dx = held.x; dy = held.y; }
            }
            else if (held != Vector2Int.zero && Time.unscaledTime >= _bagNavAt)
            {
                _bagNavAt = Time.unscaledTime + 0.1f;
                if (dx == 0 && dy == 0) { dx = held.x; dy = held.y; }
            }

            if (_bagMenu)
            {
                if (dy != 0 && _bagActions.Count > 0) _bagMenuSel = (_bagMenuSel + dy + _bagActions.Count) % _bagActions.Count;
                if (GameInput.PadPressed(0)) RunBagAction(_bagMenuSel);
                else if (GameInput.PadPressed(1))
                {
                    _bagMenu = false;
                    _backConsumedFrame = Time.frameCount;
                }
                return;
            }

            if (dx != 0)
            {
                int col = Mathf.Clamp(_bagCol + dx, 0, hasBox ? 1 : 0);
                if (col != _bagCol) { _bagCol = col; _message = null; }
            }
            var list = _bagCol == 0 ? _bagItems : _boxItems;
            if (dy != 0 && list.Count > 0) _bagSel[_bagCol] = Mathf.Clamp(_bagSel[_bagCol] + dy, 0, list.Count - 1);

            if (GameInput.PadPressed(0)) OpenBagMenu(_bagCol);
            else if (GameInput.PadPressed(2))
            {
                // X: the whole stack to the other side
                ItemDefinition item = BagSelected(_bagCol);
                if (item != null && hasBox)
                {
                    if (_bagCol == 0) BagStore(item, int.MaxValue); else BagTake(item, int.MaxValue);
                    RefreshBag();
                }
                else if (item != null) _message = "Yakında sandık ya da araç yok";
            }
            else if (GameInput.PadPressed(3) && hasBox)
            {
                BagTakeAll();
                RefreshBag();
            }
        }

        // ------------------------------------------------------------------ drawing

        private void BagStyles()
        {
            if (_bRow != null) return;
            _bRow = new GUIStyle(GUI.skin.label) { fontSize = 14, alignment = TextAnchor.MiddleLeft, clipping = TextClipping.Clip, normal = { textColor = Color.white } };
            _bRowSel = new GUIStyle(_bRow) { fontStyle = FontStyle.Bold };
            _bTag = new GUIStyle(GUI.skin.label) { fontSize = 11, alignment = TextAnchor.MiddleRight, normal = { textColor = new Color(0.65f, 0.7f, 0.78f) } };
        }

        private void DrawBagTab(Rect panel)
        {
            BagStyles();
            SurvivalGUI.Fill(panel, new Color(0.05f, 0.06f, 0.08f, 0.96f));
            var inv = BagInventory;
            if (inv == null)
            {
                GUI.Label(new Rect(panel.x + 20, panel.y + 20, 400, 30), "Çanta bulunamadı", SurvivalGUI.Header);
                return;
            }
            if (Event.current.type == EventType.Layout) RefreshBag();

            ItemStorage box = BagContainer(out string boxTitle, out bool chest);
            bool pad = GameInput.UsingPad;
            const float m = 18f, infoH = 96f, headH = 30f, rowH = 26f;
            float colW = (panel.width - m * 3f) * 0.5f;
            float top = panel.y + 14f;
            float listH = panel.height - 14f - headH - infoH - 44f;
            int rows = Mathf.Max(3, Mathf.FloorToInt(listH / rowH));

            for (int col = 0; col < 2; col++)
            {
                var list = col == 0 ? _bagItems : _boxItems;
                float x = panel.x + m + col * (colW + m);
                var head = new Rect(x, top, colW, headH);
                bool active = _bagCol == col && !_bagMenu;
                SurvivalGUI.Fill(head, active && pad ? new Color(0.95f, 0.71f, 0.19f, 0.28f) : new Color(1f, 1f, 1f, 0.06f));
                string title = col == 0
                    ? $"ÇANTA   {inv.Storage.TotalWeight:0.0} / {(inv.Storage.IsUnlimited ? "sınırsız" : inv.Storage.MaxWeight.ToString("0.#") + " kg")}     Para {inv.Money}"
                    : box != null ? (chest ? boxTitle : "ARAÇ BAGAJI (sınırsız)") : "SANDIK / ARAÇ BAGAJI: yakında yok";
                GUI.Label(new Rect(head.x + 8f, head.y, head.width - 16f, headH), title, SurvivalGUI.Header);

                var area = new Rect(x, top + headH + 4f, colW, rows * rowH);
                SurvivalGUI.Fill(area, new Color(0f, 0f, 0f, 0.25f));
                if (list.Count == 0)
                {
                    GUI.Label(new Rect(area.x + 10f, area.y + 6f, area.width - 20f, 40f),
                        col == 0 ? "Çanta boş" : box != null ? "Boş" : "Sandık açınca ya da aracın yanındayken burada görünür.", SurvivalGUI.Small);
                    continue;
                }

                // keep the selection in view
                int sel = _bagSel[col];
                if (sel < _bagTop[col]) _bagTop[col] = sel;
                if (sel >= _bagTop[col] + rows) _bagTop[col] = sel - rows + 1;
                _bagTop[col] = Mathf.Clamp(_bagTop[col], 0, Mathf.Max(0, list.Count - rows));
                if (area.Contains(Event.current.mousePosition) && Event.current.type == EventType.ScrollWheel)
                {
                    _bagTop[col] = Mathf.Clamp(_bagTop[col] + (Event.current.delta.y > 0 ? 2 : -2), 0, Mathf.Max(0, list.Count - rows));
                    Event.current.Use();
                }

                ItemStorage storage = col == 0 ? inv.Storage : box;
                for (int r = 0; r < rows && _bagTop[col] + r < list.Count; r++)
                {
                    int i = _bagTop[col] + r;
                    ItemDefinition item = list[i];
                    var rr = new Rect(area.x, area.y + r * rowH, area.width, rowH - 2f);
                    bool selected = i == sel && _bagCol == col;
                    if (selected) SurvivalGUI.Fill(rr, pad ? new Color(0.95f, 0.71f, 0.19f, 0.35f) : new Color(1f, 1f, 1f, 0.12f));
                    else if (r % 2 == 1) SurvivalGUI.Fill(rr, new Color(1f, 1f, 1f, 0.03f));
                    int count = storage != null ? storage.GetCount(item) : 0;
                    string label = count > 1 ? $"{item.DisplayName}  x{count}" : item.DisplayName;
                    GUI.Label(new Rect(rr.x + 10f, rr.y, rr.width - 150f, rr.height), label, selected ? _bRowSel : _bRow);
                    GUI.Label(new Rect(rr.xMax - 142f, rr.y, 134f, rr.height), $"{CategoryName(item)} · {item.weight * count:0.0} kg", _bTag);
                    if (!_bagMenu && GUI.Button(rr, GUIContent.none, GUIStyle.none))
                    {
                        _bagCol = col;
                        _bagSel[col] = i;
                        OpenBagMenu(col);
                    }
                }
                if (list.Count > rows)
                    GUI.Label(new Rect(area.x, area.yMax + 1f, area.width, 16f), $"{_bagTop[col] + 1}-{Mathf.Min(list.Count, _bagTop[col] + rows)} / {list.Count}", _bTag);
            }

            // Selected item: description and numbers
            ItemDefinition shown = _bagMenu ? _bagMenuItem : BagSelected(_bagCol);
            var info = new Rect(panel.x + m, panel.yMax - infoH - 34f, panel.width - m * 2f, infoH);
            SurvivalGUI.Fill(info, new Color(1f, 1f, 1f, 0.05f));
            if (shown != null)
            {
                GUI.Label(new Rect(info.x + 10f, info.y + 4f, info.width - 20f, 22f), shown.DisplayName, SurvivalGUI.Header);
                string facts = $"{CategoryName(shown)}   {shown.weight:0.##} kg   değer {shown.value}";
                if (shown.category == ItemCategory.Apparel && !shown.IsShield) facts += $"   zırh +{shown.armor:0}";
                if (shown.category == ItemCategory.Food) facts += $"   tokluk +{shown.useAmount}";
                if (shown.category == ItemCategory.Drink) facts += $"   su +{shown.useAmount}";
                if (shown.category == ItemCategory.Consumable && shown.useAmount > 0) facts += $"   can +{shown.useAmount}";
                GUI.Label(new Rect(info.x + 10f, info.y + 26f, info.width - 20f, 18f), facts, SurvivalGUI.Small);
                GUI.Label(new Rect(info.x + 10f, info.y + 44f, info.width - 20f, infoH - 46f), shown.description ?? "", SurvivalGUI.Small);
            }

            string hint = pad
                ? (_bagMenu ? "Yön: seç    A: yap    B: geri" : "Yön: seç    ← →: çanta / " + (chest ? "sandık" : "bagaj") + "    A: eylemler    X: tümünü karşıya    Y: hepsini al    B: kapat")
                : "Eşyaya tıkla: eylemler    Tekerlek: kaydır";
            GUI.Label(new Rect(panel.x + m, panel.yMax - 30f, panel.width - m * 2f, 22f), string.IsNullOrEmpty(_message) ? hint : _message + "      " + hint, SurvivalGUI.Small);

            if (_bagMenu) DrawBagMenu(panel);
        }

        private void DrawBagMenu(Rect panel)
        {
            const float w = 320f, rowH = 34f;
            float h = 46f + _bagActions.Count * rowH + 40f;
            var rect = new Rect(panel.center.x - w * 0.5f, panel.center.y - h * 0.5f, w, h);
            SurvivalGUI.Fill(new Rect(panel.x, panel.y, panel.width, panel.height), new Color(0f, 0f, 0f, 0.45f));
            GUI.Box(rect, GUIContent.none, SurvivalGUI.Panel);
            GUI.Label(new Rect(rect.x + 14f, rect.y + 8f, w - 28f, 26f), _bagMenuItem != null ? _bagMenuItem.DisplayName : "", SurvivalGUI.Title);
            int run = -1;
            for (int i = 0; i < _bagActions.Count; i++)
            {
                var r = new Rect(rect.x + 14f, rect.y + 44f + i * rowH, w - 28f, rowH - 4f);
                if (i == _bagMenuSel && GameInput.UsingPad) SurvivalGUI.Fill(new Rect(r.x - 3f, r.y - 3f, r.width + 6f, r.height + 6f), new Color(1f, 0.8f, 0.3f, 1f));
                if (GUI.Button(r, _bagActions[i].label)) run = i;
            }
            var back = new Rect(rect.x + 14f, rect.yMax - 36f, w - 28f, 28f);
            if (GUI.Button(back, GameInput.UsingPad ? "Geri (B)" : "Geri")) _bagMenu = false;
            if (run >= 0) RunBagAction(run);
        }
    }
}
