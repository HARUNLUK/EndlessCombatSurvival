using System.Collections.Generic;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif
using EndlessSurvival.Inventory;
using EndlessSurvival.Survival;

namespace EndlessCombat.Combat
{
    /// <summary>
    /// LB on the gamepad (Combat_Action_Plan.md 1, 3, 3b; 2026-11-17):
    ///   tap (shorter than HoldTime)  the weapon in the hand goes to its holster / the last one comes out
    ///   held                         the selection map: cards grouped by hand (left hand on the left, two hands on top,
    ///                                right hand on the right), an info panel in the middle. The right stick moves the
    ///                                cursor; releasing LB takes the card under the cursor. No button is bound to a card.
    /// Released on the card already in the right hand, or on an empty card: nothing changes. Released on the shield /
    /// torch already in the left hand: the shield goes to the back / the torch is put out (the only way to free the left hand).
    /// While the map is open the other gamepad presses are not seen by the game (GameInput.SelectMapOpen), the camera
    /// does not turn (PlayerInputState), the left stick still walks. The game does not pause.
    /// </summary>
    [DefaultExecutionOrder(-70)]
    [DisallowMultipleComponent]
    public class WeaponSelectMap : MonoBehaviour
    {
        /// <summary>LB held longer than this opens the map; shorter is a tap.</summary>
        public static float HoldTime = 0.25f;
        private static bool _tuningRegistered;

        private PlayerCombatState _state;
        private PlayerLoadout _loadout;
        private PlayerThrower _thrower;
        private ShieldHolder _shield;
        private TorchHolder _torch;

        private bool _lbDown;
        private float _lbDownAt;
        private int _cursor = -1;
        private Vector2Int _stickDir;
        private float _repeatAt;

        public bool IsOpen => GameInput.SelectMapOpen && _lbDown;

        // ------------------------------------------------------------------ Cards

        private enum Group { Left, Both, Right }

        private struct Card
        {
            public int pick;          // PlayerLoadout.Pick code
            public Group group;
            public Vector2 pos;       // top left in the 1600 x 900 layout (Combat_Action_Plan.md 3b, taslak 2)
            public int icon;
        }

        private const float LayoutW = 1600f, LayoutH = 900f, CardW = 190f, CardH = 124f;
        private const float PanelX = 590f, PanelY = 300f, PanelW = 420f, PanelH = 270f;

        // One card per slot. The melee card holds every melee weapon the player carries and the throwable card holds
        // rock / grenade: on those cards the right stick left / right changes the active one (user, 2026-11-17).
        private static readonly Card[] Cards =
        {
            new Card { pick = PlayerLoadout.PickLongGun,   group = Group.Both,  pos = new Vector2(604f, 120f),  icon = IconRifle },
            new Card { pick = PlayerLoadout.PickBow,       group = Group.Both,  pos = new Vector2(806f, 120f),  icon = IconBow },
            new Card { pick = PlayerLoadout.PickShield,    group = Group.Left,  pos = new Vector2(360f, 300f),  icon = IconShield },
            new Card { pick = PlayerLoadout.PickTorch,     group = Group.Left,  pos = new Vector2(360f, 436f),  icon = IconTorch },
            new Card { pick = PlayerLoadout.PickPistol,    group = Group.Right, pos = new Vector2(1050f, 232f), icon = IconPistol },
            new Card { pick = PlayerLoadout.PickMelee,     group = Group.Right, pos = new Vector2(1050f, 368f), icon = IconBat },
            new Card { pick = PlayerLoadout.PickThrowable, group = Group.Right, pos = new Vector2(1050f, 504f), icon = IconRock },
        };

        // ------------------------------------------------------------------ Card variants (melee weapons, throwables)

        private PlayerEquipment _equipment;
        private PlayerInventory _inventory;
        private readonly List<ItemDefinition> _melee = new List<ItemDefinition>(); // empty: the fists
        private int _meleeIx;
        private PlayerThrower.Kind _throwKind = PlayerThrower.Kind.Rock;

        private ItemDefinition MeleeEquipped => _loadout.Item(PlayerLoadout.Slot.Melee);
        private ItemDefinition MeleeChoice => _melee.Count > 0 ? _melee[Mathf.Clamp(_meleeIx, 0, _melee.Count - 1)] : null;

        /// <summary>The melee weapons on the body and in the bag, in a fixed order (by name).</summary>
        private void BuildMeleeList()
        {
            _melee.Clear();
            ItemDefinition worn = MeleeEquipped;
            if (worn != null) _melee.Add(worn);
            if (_inventory != null)
                foreach (var kv in _inventory.Storage.Items)
                {
                    ItemDefinition it = kv.Key;
                    if (it == null || kv.Value <= 0 || _melee.Contains(it)) continue;
                    if (it.category == ItemCategory.Weapon && it.CarrySlot == EquipSlot.Melee && it.weaponPrefab != null) _melee.Add(it);
                }
            _melee.Sort((a, b) => string.CompareOrdinal(a.DisplayName, b.DisplayName));
            _meleeIx = worn != null ? _melee.IndexOf(worn) : 0;
        }

        private List<PlayerThrower.Kind> ThrowKinds()
        {
            var list = new List<PlayerThrower.Kind>(2);
            if (_thrower == null) return list;
            foreach (var k in new[] { PlayerThrower.Kind.Rock, PlayerThrower.Kind.Grenade })
                if (_thrower.Count(k) > 0 || _thrower.Held == k) list.Add(k);
            return list;
        }

        private int VariantCount(int pick) =>
            pick == PlayerLoadout.PickMelee ? _melee.Count : pick == PlayerLoadout.PickThrowable ? ThrowKinds().Count : 1;

        /// <summary>Right stick left / right on the melee / throwable card: the next one in that card.</summary>
        private void CycleVariant(int pick, int dir)
        {
            if (pick == PlayerLoadout.PickMelee && _melee.Count > 1)
                _meleeIx = (_meleeIx + dir + _melee.Count) % _melee.Count;
            else if (pick == PlayerLoadout.PickThrowable)
            {
                var kinds = ThrowKinds();
                if (kinds.Count < 2) return;
                int i = Mathf.Max(0, kinds.IndexOf(_throwKind));
                _throwKind = kinds[(i + dir + kinds.Count) % kinds.Count];
            }
        }

        /// <summary>The Pick code the card stands for now (the throwable card: rock or grenade).</summary>
        private int Resolve(int pick) =>
            pick != PlayerLoadout.PickThrowable ? pick : _throwKind == PlayerThrower.Kind.Grenade ? PlayerLoadout.PickGrenade : PlayerLoadout.PickRock;

        // ------------------------------------------------------------------ Update: LB, cursor, pick

        private void Awake()
        {
            _state = PlayerCombatState.GetOrAdd(gameObject);
            if (!_tuningRegistered)
            {
                _tuningRegistered = true;
                Tuning.TuningPanel.Register("Dövüş", "LB basılı tutma (sn)", () => HoldTime, v => HoldTime = v, 0.1f, 0.6f, 0.05f);
            }
        }

        private void OnDisable() => Close();

        private void Update()
        {
            if (_loadout == null) _loadout = GetComponent<PlayerLoadout>();
            if (_thrower == null) _thrower = GetComponent<PlayerThrower>();
            if (_shield == null) _shield = GetComponent<ShieldHolder>();
            if (_torch == null) _torch = GetComponent<TorchHolder>();
            if (_equipment == null) _equipment = GetComponent<PlayerEquipment>();
            if (_inventory == null) _inventory = GetComponent<PlayerInventory>();

#if ENABLE_INPUT_SYSTEM
            Gamepad pad = GameInput.TuningPanelOpen ? null : Gamepad.current;
            if (pad == null || _state == null || _loadout == null || _state.InputBlocked || HandsBusyHolster.HandsBusy)
            {
                Close();
                _lbDown = pad != null && pad.leftShoulder.isPressed; // a held LB does not count when the block ends
                _lbDownAt = -999f;
                return;
            }

            bool lb = pad.leftShoulder.isPressed;
            if (lb && !_lbDown)
            {
                _lbDown = true;
                _lbDownAt = Time.unscaledTime;
            }
            else if (!lb && _lbDown)
            {
                _lbDown = false;
                bool wasOpen = GameInput.SelectMapOpen;
                int chosen = _cursor;
                Close();
                if (wasOpen) TakeCard(chosen);
                else if (Time.unscaledTime - _lbDownAt < HoldTime) _state.Press(CombatIntent.Holster);
                return;
            }

            if (!_lbDown) return;
            if (!GameInput.SelectMapOpen && Time.unscaledTime - _lbDownAt >= HoldTime) Open();
            if (!GameInput.SelectMapOpen) return;

            MoveCursor(pad.rightStick.ReadValue());
#endif
        }

        private void Open()
        {
            GameInput.SelectMapOpen = true;
            _stickDir = Vector2Int.zero;
            BuildMeleeList();
            // The throwable card shows the one in the hand, else the last chosen if there is still some, else the first there is
            var kinds = ThrowKinds();
            if (_thrower != null && _thrower.HasItem) _throwKind = _thrower.Held;
            else if (!kinds.Contains(_throwKind) && kinds.Count > 0) _throwKind = kinds[0];
            // The cursor starts on what the right hand holds, else on the last used weapon
            _cursor = -1;
            for (int i = 0; i < Cards.Length; i++)
                if (Cards[i].group == Group.Right && InHand(Cards[i].pick)) { _cursor = i; break; }
            if (_cursor < 0)
            {
                int last = _loadout.LastActive == PlayerLoadout.Slot.None ? PlayerLoadout.PickPistol : (int)_loadout.LastActive;
                _cursor = IndexOf(last);
            }
            if (_cursor < 0) _cursor = IndexOf(PlayerLoadout.PickPistol);
        }

        private void Close()
        {
            GameInput.SelectMapOpen = false;
        }

        private static int IndexOf(int pick)
        {
            for (int i = 0; i < Cards.Length; i++) if (Cards[i].pick == pick) return i;
            return -1;
        }

        /// <summary>Right stick: a push steps to the nearest card in that direction; held, it repeats.</summary>
        private void MoveCursor(Vector2 stick)
        {
            Vector2Int dir = Vector2Int.zero;
            if (stick.magnitude > 0.6f)
            {
                if (Mathf.Abs(stick.x) > Mathf.Abs(stick.y)) dir.x = stick.x > 0f ? 1 : -1;
                else dir.y = stick.y > 0f ? -1 : 1; // layout y goes down
            }
            bool step = false;
            if (dir != _stickDir)
            {
                _stickDir = dir;
                _repeatAt = Time.unscaledTime + 0.35f;
                step = dir != Vector2Int.zero;
            }
            else if (dir != Vector2Int.zero && Time.unscaledTime >= _repeatAt)
            {
                _repeatAt = Time.unscaledTime + 0.15f;
                step = true;
            }
            if (!step || _cursor < 0) return;

            // Left / right on a card with more than one weapon in it changes the active one instead of moving
            int here = Cards[_cursor].pick;
            if (dir.x != 0 && VariantCount(here) > 1)
            {
                CycleVariant(here, dir.x);
                return;
            }

            Vector2 from = Centre(Cards[_cursor]);
            Vector2 d = dir;
            int best = -1;
            float bestScore = float.MaxValue;
            for (int i = 0; i < Cards.Length; i++)
            {
                if (i == _cursor) continue;
                Vector2 off = Centre(Cards[i]) - from;
                float along = Vector2.Dot(off, d);
                if (along < 20f) continue;
                float across = Mathf.Abs(off.x * d.y - off.y * d.x);
                float score = along + across * 2f;
                if (score < bestScore) { bestScore = score; best = i; }
            }
            if (best >= 0) _cursor = best;
        }

        /// <summary>Puts the cursor on a card while the map is open (automatic tests; the player uses the right stick).</summary>
        public void PointAt(int pick)
        {
            if (!GameInput.SelectMapOpen) return;
            if (pick == PlayerLoadout.PickRock || pick == PlayerLoadout.PickGrenade)
            {
                _throwKind = pick == PlayerLoadout.PickRock ? PlayerThrower.Kind.Rock : PlayerThrower.Kind.Grenade;
                pick = PlayerLoadout.PickThrowable;
            }
            int i = IndexOf(pick);
            if (i >= 0) _cursor = i;
        }

        /// <summary>Tests: on the melee card, the melee weapon with this item name becomes the active one.</summary>
        public void PointAtMelee(string itemName)
        {
            PointAt(PlayerLoadout.PickMelee);
            int i = _melee.FindIndex(it => it != null && it.name == itemName);
            if (i >= 0) _meleeIx = i;
        }

        private static Vector2 Centre(Card c) => c.pos + new Vector2(CardW * 0.5f, CardH * 0.5f);

        private void TakeCard(int index)
        {
            if (index < 0 || index >= Cards.Length) return;
            int pick = Cards[index].pick;
            if (Empty(pick)) return;
            if (InHand(pick) && Cards[index].group != Group.Left) return; // already in the right hand: nothing changes
            if (pick == PlayerLoadout.PickMelee)
            {
                // Another melee weapon from the card: it goes into the melee slot, the one there goes to the bag
                ItemDefinition want = MeleeChoice;
                if (want != null && want != MeleeEquipped && _equipment != null)
                {
                    string msg = _equipment.Equip(want);
                    if (MeleeEquipped != want)
                    {
                        if (!string.IsNullOrEmpty(msg)) SurvivalNotify.Show(msg, 1.5f);
                        return;
                    }
                }
            }
            _state.Press(CombatIntent.Swap, Resolve(pick));
        }

        // ------------------------------------------------------------------ What each card shows

        private WeaponController Weapon(int pick) =>
            pick == PlayerLoadout.PickLongGun ? _loadout.Weapon(PlayerLoadout.Slot.LongGun)
            : pick == PlayerLoadout.PickPistol ? _loadout.Weapon(PlayerLoadout.Slot.Pistol)
            : pick == PlayerLoadout.PickMelee ? _loadout.Weapon(PlayerLoadout.Slot.Melee) : null;

        private bool ThrowableHeld => _thrower != null && _thrower.HasItem;

        private static MeleeWeapon MeleeOf(ItemDefinition item) =>
            item != null && item.weaponPrefab != null ? item.weaponPrefab.GetComponent<MeleeWeapon>() : null;

        /// <summary>The melee weapon chosen on the card needs both hands (bat).</summary>
        private bool TwoHandMelee()
        {
            MeleeWeapon melee = MeleeOf(MeleeChoice);
            return melee != null && melee.stats.ResolveCategory(false) == MeleeCategory.TwoHand;
        }

        /// <summary>The melee slot weapon (or the fists) is in the right hand.</summary>
        private bool MeleeSlotInHand => !ThrowableHeld && !_loadout.IsStowed && _loadout.Active == PlayerLoadout.Slot.Melee;

        /// <summary>The card, as it stands now (active melee weapon / throwable), is what the hand holds.</summary>
        private bool InHand(int pick)
        {
            switch (pick)
            {
                case PlayerLoadout.PickShield: return _loadout.ShieldOnArm;
                case PlayerLoadout.PickTorch: return _loadout.TorchHeld;
                case PlayerLoadout.PickThrowable: return ThrowableHeld && _thrower.Held == _throwKind;
                case PlayerLoadout.PickBow: return _loadout.BowInHands;
                case PlayerLoadout.PickMelee: return MeleeSlotInHand && MeleeChoice == MeleeEquipped;
                default: return !ThrowableHeld && _loadout.RightDrawn && (int)_loadout.Active == pick;
            }
        }

        /// <summary>Something of this card is in the hand, whichever one the card shows (for the "Elde" tag).</summary>
        private bool CardHeld(int pick) =>
            pick == PlayerLoadout.PickMelee ? MeleeSlotInHand : pick == PlayerLoadout.PickThrowable ? ThrowableHeld : InHand(pick);

        private bool Empty(int pick)
        {
            switch (pick)
            {
                case PlayerLoadout.PickLongGun:
                case PlayerLoadout.PickPistol: return Weapon(pick) == null;
                case PlayerLoadout.PickMelee: return false; // nothing carried = the fists
                case PlayerLoadout.PickBow: return !_loadout.HasBow;
                case PlayerLoadout.PickShield: return _shield == null || !_shield.HasShield;
                case PlayerLoadout.PickTorch: return _torch == null || (!_torch.Held && _torch.TorchInBag() == null);
                default: return ThrowKinds().Count == 0;
            }
        }

        private ItemDefinition PlayerBowItem() { var b = GetComponent<PlayerBow>(); return b != null ? b.Item : null; }

        private string ThrowName(PlayerThrower.Kind k) => k == PlayerThrower.Kind.Grenade ? "El bombası" : "Taş";

        private string Name(int pick)
        {
            switch (pick)
            {
                case PlayerLoadout.PickLongGun: return ItemName(PlayerLoadout.Slot.LongGun, "Uzun namlu");
                case PlayerLoadout.PickPistol: return ItemName(PlayerLoadout.Slot.Pistol, "Tabanca");
                case PlayerLoadout.PickMelee: return MeleeChoice != null ? MeleeChoice.DisplayName : "Yumruk";
                case PlayerLoadout.PickBow: return _loadout.HasBow && PlayerBowItem() != null ? PlayerBowItem().DisplayName : "Yay";
                case PlayerLoadout.PickShield: return _shield != null && _shield.Item != null ? _shield.Item.DisplayName : "Kalkan";
                case PlayerLoadout.PickTorch: return "Meşale";
                default: return Empty(pick) ? "Fırlatılabilir" : ThrowName(_throwKind);
            }
        }

        /// <summary>The name of what the hand holds now for a card (drop warnings name the held one, not the card's choice).</summary>
        private string HeldName(int pick)
        {
            if (pick == PlayerLoadout.PickMelee) return MeleeEquipped != null ? MeleeEquipped.DisplayName : "Yumruk";
            if (pick == PlayerLoadout.PickThrowable) return ThrowableHeld ? ThrowName(_thrower.Held) : "Fırlatılabilir";
            return Name(pick);
        }

        private string ItemName(PlayerLoadout.Slot slot, string fallback)
        {
            ItemDefinition item = _loadout.Item(slot);
            return item != null ? item.DisplayName : fallback;
        }

        private string Kind(int pick)
        {
            switch (pick)
            {
                case PlayerLoadout.PickLongGun: return "Uzun namlu  ·  iki el";
                case PlayerLoadout.PickPistol: return "Tabanca  ·  sağ el";
                case PlayerLoadout.PickMelee: return TwoHandMelee() ? "Yakın dövüş  ·  iki el" : "Yakın dövüş  ·  sağ el";
                case PlayerLoadout.PickBow: return "Yay  ·  iki el";
                case PlayerLoadout.PickShield: return "Kalkan  ·  sol el";
                case PlayerLoadout.PickTorch: return "Meşale  ·  sol el";
                default: return "Fırlatılabilir  ·  sağ el";
            }
        }

        /// <summary>Small text at the bottom right of a card: rounds, wear, fuel, count.</summary>
        private string Info(int pick)
        {
            if (Empty(pick)) return "Boş";
            WeaponController w = Weapon(pick);
            switch (pick)
            {
                case PlayerLoadout.PickLongGun:
                case PlayerLoadout.PickPistol:
                    return w != null && w.IsFirearm ? $"{w.CurrentAmmo} / {w.ReserveAmmo}" : "";
                case PlayerLoadout.PickShield: return $"%{Mathf.RoundToInt(_shield.Durability01 * 100f)}";
                case PlayerLoadout.PickTorch: return _torch.Held ? $"%{Mathf.RoundToInt(_torch.Fuel01 * 100f)}" : "Çantada";
                case PlayerLoadout.PickThrowable: return $"x{_thrower.Count(_throwKind)}";
                case PlayerLoadout.PickBow: return $"Ok x{_loadout.ArrowCount}";
                default: return "";
            }
        }

        private readonly List<string> _drops = new List<string>();

        /// <summary>
        /// What releasing LB on this card would cost, mirroring PlayerLoadout.Pick (Combat_Action_Plan.md 5).
        /// Returns the panel line (empty: nothing happens) and fills dropCards with the cards that get "Düşecek".
        /// </summary>
        private string Outcome(int pick, List<int> dropCards)
        {
            dropCards.Clear();
            _drops.Clear();
            if (Empty(pick)) return "Bu yuva boş";
            bool inHand = InHand(pick);
            if (inHand && pick == PlayerLoadout.PickShield) return "Bırakınca: kalkan sırta takılır";
            if (inHand && pick == PlayerLoadout.PickTorch) return "Bırakınca: meşale söner";
            if (inHand && pick == PlayerLoadout.PickBow) return "";
            if (inHand) return "";
            if (_loadout.BowInHands) AddDrop(PlayerLoadout.PickBow, dropCards); // the bow holds both hands

            bool twoHand = pick == PlayerLoadout.PickLongGun || (pick == PlayerLoadout.PickMelee && TwoHandMelee());
            bool rightTwoHand = _loadout.RightDrawn && !ThrowableHeld && _loadout.TwoHanded(_loadout.Active)
                                && _loadout.Active != PlayerLoadout.Slot.Pistol;
            string swapBack = null; // same card, another one: the old one goes back to the bag (not a drop)

            switch (pick)
            {
                case PlayerLoadout.PickMelee:
                    if (MeleeSlotInHand) swapBack = HeldName(PlayerLoadout.PickMelee);
                    else DropRightHand(dropCards);
                    if (twoHand && _loadout.ShieldOnArm) AddDrop(PlayerLoadout.PickShield, dropCards);
                    if (twoHand && _loadout.TorchHeld) AddDrop(PlayerLoadout.PickTorch, dropCards);
                    if (swapBack == "Yumruk") swapBack = null;
                    break;
                case PlayerLoadout.PickLongGun:
                case PlayerLoadout.PickPistol:
                    DropRightHand(dropCards);
                    if (twoHand && _loadout.ShieldOnArm) AddDrop(PlayerLoadout.PickShield, dropCards);
                    if (twoHand && _loadout.TorchHeld) AddDrop(PlayerLoadout.PickTorch, dropCards);
                    break;
                case PlayerLoadout.PickThrowable:
                    if (ThrowableHeld) swapBack = ThrowName(_thrower.Held);
                    else if (_loadout.RightDrawn) DropRightHand(dropCards);
                    break;
                case PlayerLoadout.PickShield:
                    if (rightTwoHand) DropRightHand(dropCards);
                    if (_loadout.TorchHeld) AddDrop(PlayerLoadout.PickTorch, dropCards);
                    break;
                case PlayerLoadout.PickTorch:
                    if (_loadout.ShieldOnArm) AddDrop(PlayerLoadout.PickShield, dropCards);
                    if (rightTwoHand) DropRightHand(dropCards);
                    break;
                case PlayerLoadout.PickBow:
                    DropRightHand(dropCards);
                    if (_loadout.ShieldOnArm) AddDrop(PlayerLoadout.PickShield, dropCards);
                    if (_loadout.TorchHeld) AddDrop(PlayerLoadout.PickTorch, dropCards);
                    break;
            }
            string line = _drops.Count > 0 ? "Bırakınca: " + string.Join(" ve ", _drops) + " yere düşer" : "";
            if (swapBack != null)
                line = line.Length > 0 ? line + ", " + swapBack + " çantaya döner" : "Bırakınca: " + swapBack + " çantaya döner";
            return line;
        }

        private void DropRightHand(List<int> dropCards)
        {
            if (ThrowableHeld) { AddDrop(PlayerLoadout.PickThrowable, dropCards); return; }
            if (!_loadout.RightDrawn) return;
            AddDrop((int)_loadout.Active, dropCards);
        }

        private void AddDrop(int pick, List<int> dropCards)
        {
            if (dropCards.Contains(pick)) return;
            dropCards.Add(pick);
            _drops.Add(HeldName(pick));
        }

        /// <summary>The bars in the info panel (label, 0-1). Bars that mean nothing for the kind are left out.</summary>
        private void Stats(int pick, List<KeyValuePair<string, float>> bars)
        {
            bars.Clear();
            if (Empty(pick)) return;
            WeaponController w = Weapon(pick);
            switch (pick)
            {
                case PlayerLoadout.PickLongGun:
                case PlayerLoadout.PickPistol:
                    if (w == null) return;
                    float dmg = w.BaseDamage * Mathf.Max(1, w.Pellets);
                    bars.Add(new KeyValuePair<string, float>("Hasar", Mathf.Clamp01(dmg / 80f)));
                    bars.Add(new KeyValuePair<string, float>("Hız", Mathf.Clamp01(1f / Mathf.Max(0.05f, w.ShotInterval) / 12f)));
                    float range = w.Definition != null ? w.Definition.range : 120f;
                    bars.Add(new KeyValuePair<string, float>("Menzil", Mathf.Clamp01(range / 250f)));
                    if (w.MagazineCapacity > 0)
                        bars.Add(new KeyValuePair<string, float>("Şarjör", Mathf.Clamp01((float)w.CurrentAmmo / w.MagazineCapacity)));
                    break;
                case PlayerLoadout.PickMelee:
                    MeleeStats st = MeleeStats.Fists;
                    MeleeWeapon melee = MeleeOf(MeleeChoice);
                    if (melee != null) st = melee.stats;
                    bars.Add(new KeyValuePair<string, float>("Hasar", Mathf.Clamp01(st.damage / 45f)));
                    bars.Add(new KeyValuePair<string, float>("Hız", Mathf.Clamp01(1f - st.staminaCost / 45f)));
                    bars.Add(new KeyValuePair<string, float>("Menzil", Mathf.Clamp01(st.range / 2.5f)));
                    break;
                case PlayerLoadout.PickShield:
                    ItemDefinition item = _shield.Item;
                    if (item != null && item.IsShield)
                    {
                        bars.Add(new KeyValuePair<string, float>("Engel", Mathf.Clamp01(item.shield.absorb)));
                        bars.Add(new KeyValuePair<string, float>("Mermi engeli", Mathf.Clamp01(item.shield.bulletAbsorb)));
                    }
                    bars.Add(new KeyValuePair<string, float>("Dayanıklılık", _shield.Durability01));
                    break;
                case PlayerLoadout.PickTorch:
                    if (_torch.Held) bars.Add(new KeyValuePair<string, float>("Yakıt", _torch.Fuel01));
                    break;
                case PlayerLoadout.PickBow:
                    bars.Add(new KeyValuePair<string, float>("Hasar", Mathf.Clamp01(PlayerBow.DamageMax / 80f)));
                    bars.Add(new KeyValuePair<string, float>("Hız", Mathf.Clamp01(0.35f / Mathf.Max(0.1f, PlayerBow.DrawSeconds))));
                    bars.Add(new KeyValuePair<string, float>("Menzil", Mathf.Clamp01(PlayerBow.SpeedMax * 2.2f / 250f)));
                    bars.Add(new KeyValuePair<string, float>("Sessiz", 1f));
                    break;
            }
        }

        // ------------------------------------------------------------------ Look

        private static readonly Color Gold = new Color(0.91f, 0.77f, 0.47f, 1f);
        private static readonly Color TextWhite = new Color(0.94f, 0.93f, 0.91f, 1f);
        private static readonly Color TextDim = new Color(0.59f, 0.59f, 0.57f, 1f);
        private static readonly Color Dark = new Color(0.1f, 0.1f, 0.1f, 1f);
        private static readonly Color Red = new Color(0.82f, 0.24f, 0.18f, 1f);
        private static readonly Color CardBg = new Color(0.05f, 0.05f, 0.05f, 0.67f);
        private static readonly Color SelBg = new Color(0.93f, 0.91f, 0.87f, 0.94f);

        private GUIStyle _name, _info, _head, _title, _sub, _bar, _warn, _tag, _hint, _btn, _arrow, _count;
        private float _styleScale = -1f;
        private Texture2D _vignette;
        private readonly List<int> _dropCards = new List<int>();
        private readonly List<KeyValuePair<string, float>> _bars = new List<KeyValuePair<string, float>>();

        private void OnGUI()
        {
            if (!GameInput.SelectMapOpen || _loadout == null) return;
            GUI.depth = -55; // above the HUD cards and prompts (the action menu is -60)
            if (Event.current.type != EventType.Repaint) return;
            SurvivalGUI.Init();
            EnsureIcons();
            float s = Mathf.Min(Screen.width / LayoutW, Screen.height / LayoutH);
            Vector2 o = new Vector2((Screen.width - LayoutW * s) * 0.5f, (Screen.height - LayoutH * s) * 0.5f);
            EnsureStyles(s);
            Rect R(float x, float y, float w, float h) => new Rect(o.x + x * s, o.y + y * s, w * s, h * s);

            // Darken the game behind (it keeps running) and a soft vignette
            var full = new Rect(0f, 0f, Screen.width, Screen.height);
            SurvivalGUI.Fill(full, new Color(0f, 0f, 0f, 0.55f));
            if (_vignette == null) _vignette = MakeVignette();
            GUI.DrawTexture(full, _vignette, ScaleMode.StretchToFill, true);

            int cur = _cursor >= 0 && _cursor < Cards.Length ? Cards[_cursor].pick : -1;
            string outcome = cur >= 0 ? Outcome(cur, _dropCards) : "";

            // Group heads
            Head(R(360f, 260f, 190f, 30f), "SOL EL");
            Head(R(604f, 80f, 392f, 30f), "İKİ EL");
            Head(R(1050f, 192f, CardW, 30f), "SAĞ EL");

            // Cards
            for (int i = 0; i < Cards.Length; i++)
            {
                Card c = Cards[i];
                DrawCard(R(c.pos.x, c.pos.y, CardW, CardH), c, i == _cursor, _dropCards.Contains(c.pick), s);
            }

            // Info panel
            Rect p = R(PanelX, PanelY, PanelW, PanelH);
            SurvivalGUI.Fill(p, new Color(0.03f, 0.03f, 0.03f, 0.75f));
            SurvivalGUI.Fill(new Rect(p.x, p.y, p.width, 3f * s), Gold);
            if (cur >= 0)
            {
                Label(R(PanelX + 22f, PanelY + 14f, PanelW - 40f, 46f), Name(cur), _title, TextWhite);
                Label(R(PanelX + 24f, PanelY + 62f, PanelW - 40f, 24f), Kind(cur), _sub, TextDim);
                Stats(cur, _bars);
                float by = PanelY + 100f;
                foreach (var b in _bars)
                {
                    Label(R(PanelX + 24f, by, 130f, 22f), b.Key, _bar, TextWhite);
                    SurvivalGUI.Fill(R(PanelX + 150f, by + 7f, 240f, 8f), new Color(1f, 1f, 1f, 0.22f));
                    SurvivalGUI.Fill(R(PanelX + 150f, by + 7f, 240f * b.Value, 8f), TextWhite);
                    by += 28f;
                }
                if (Empty(cur) && _bars.Count == 0)
                    Label(R(PanelX + 24f, PanelY + 104f, PanelW - 48f, 24f), "Bu yuvada bir şey yok", _bar, TextDim);
                if (!string.IsNullOrEmpty(outcome))
                {
                    bool drop = _dropCards.Count > 0;
                    Rect wr = R(PanelX + 16f, PanelY + 228f, PanelW - 32f, 28f);
                    SurvivalGUI.Fill(wr, drop ? new Color(0.67f, 0.16f, 0.12f, 0.92f) : new Color(1f, 1f, 1f, 0.12f));
                    Label(wr, outcome, _warn, TextWhite);
                }
            }

            // Hints
            Hint(R(470f, 800f, 34f, 34f), "RS", R(512f, 805f, 280f, 26f), "Seç   (karttaki silah: sağ / sol)");
            Hint(R(810f, 800f, 34f, 34f), "LB", R(852f, 805f, 160f, 26f), "Bırak: al");
            Label(R(1010f, 805f, 200f, 26f), "Oyun sürüyor", _hint, TextDim);
        }

        private void DrawCard(Rect r, Card c, bool selected, bool willDrop, float s)
        {
            bool empty = Empty(c.pick);
            bool inHand = CardHeld(c.pick);
            int icon = c.icon;
            if (c.pick == PlayerLoadout.PickMelee && MeleeChoice == null) icon = IconFist;
            else if (c.pick == PlayerLoadout.PickMelee && MeleeOf(MeleeChoice) != null && MeleeOf(MeleeChoice).stats.ResolveCategory(false) == MeleeCategory.Short) icon = IconKnife;
            else if (c.pick == PlayerLoadout.PickThrowable && _throwKind == PlayerThrower.Kind.Grenade) icon = IconGrenade;
            Color iconCol, nameCol, infoCol;
            if (selected)
            {
                SurvivalGUI.Fill(r, SelBg);
                SurvivalGUI.Fill(new Rect(r.x, r.yMax - 4f * s, r.width, 4f * s), Gold);
                iconCol = empty ? new Color(0.1f, 0.1f, 0.1f, 0.3f) : Dark;
                nameCol = Dark;
                infoCol = new Color(0.27f, 0.27f, 0.27f, 1f);
            }
            else
            {
                SurvivalGUI.Fill(r, CardBg);
                Outline(r, new Color(1f, 1f, 1f, empty ? 0.12f : 0.18f), 1f);
                iconCol = empty ? new Color(1f, 1f, 1f, 0.2f) : new Color(0.92f, 0.91f, 0.88f, 0.9f);
                nameCol = empty ? new Color(1f, 1f, 1f, 0.43f) : TextWhite;
                infoCol = TextDim;
            }
            float isz = 74f * s;
            GUI.DrawTexture(new Rect(r.center.x - isz * 0.5f, r.y + 8f * s, isz, isz), _icons[icon], ScaleMode.ScaleToFit, true, 0f, iconCol, 0f, 0f);
            Label(new Rect(r.x + 12f * s, r.y + 90f * s, r.width - 20f * s, 26f * s), Name(c.pick), _name, nameCol);
            Label(new Rect(r.x + 10f * s, r.y + 92f * s, r.width - 22f * s, 24f * s), Info(c.pick), _info, infoCol);

            // More than one weapon in the card: arrows at the sides (right stick left / right) and "2 / 3" at the top right
            int count = VariantCount(c.pick);
            if (count > 1)
            {
                int ix = c.pick == PlayerLoadout.PickMelee ? _meleeIx : Mathf.Max(0, ThrowKinds().IndexOf(_throwKind));
                Color ac = selected ? Dark : TextWhite;
                Label(new Rect(r.x + 4f * s, r.y + 34f * s, 20f * s, 30f * s), "<", _arrow, ac);
                Label(new Rect(r.xMax - 24f * s, r.y + 34f * s, 20f * s, 30f * s), ">", _arrow, ac);
                Label(new Rect(r.xMax - 56f * s, r.y + 8f * s, 48f * s, 20f * s), $"{ix + 1} / {count}", _count, selected ? infoCol : TextDim);
            }

            if (inHand)
            {
                Rect t = new Rect(r.x + 8f * s, r.y + 8f * s, 52f * s, 20f * s);
                SurvivalGUI.Fill(t, Gold);
                Label(t, "Elde", _tag, Dark);
            }
            if (willDrop)
            {
                Outline(new Rect(r.x + 1f, r.y + 1f, r.width - 2f, r.height - 2f), Red, 3f * s);
                Rect t = new Rect(r.x + (inHand ? 66f : 8f) * s, r.y + 8f * s, 86f * s, 20f * s);
                SurvivalGUI.Fill(t, Red);
                Label(t, "Düşecek", _tag, TextWhite);
            }
        }

        private void Head(Rect r, string text)
        {
            Label(r, text, _head, Gold);
            SurvivalGUI.Fill(new Rect(r.x, r.yMax - 2f, r.width, 2f), new Color(Gold.r, Gold.g, Gold.b, 0.6f));
        }

        private void Hint(Rect circle, string button, Rect textRect, string text)
        {
            GUI.DrawTexture(circle, SurvivalGUI.White, ScaleMode.StretchToFill, true, 0f, new Color(0.92f, 0.91f, 0.88f, 0.92f), 0f, circle.width * 0.5f);
            Label(circle, button, _btn, Dark);
            Label(textRect, text, _hint, TextWhite);
        }

        private static void Outline(Rect r, Color c, float t)
        {
            SurvivalGUI.Fill(new Rect(r.x, r.y, r.width, t), c);
            SurvivalGUI.Fill(new Rect(r.x, r.yMax - t, r.width, t), c);
            SurvivalGUI.Fill(new Rect(r.x, r.y, t, r.height), c);
            SurvivalGUI.Fill(new Rect(r.xMax - t, r.y, t, r.height), c);
        }

        private static void Label(Rect r, string text, GUIStyle style, Color c)
        {
            if (string.IsNullOrEmpty(text)) return;
            style.normal.textColor = c;
            GUI.Label(r, text, style);
        }

        private void EnsureStyles(float s)
        {
            if (_name != null && Mathf.Abs(_styleScale - s) < 0.001f) return;
            _styleScale = s;
            GUIStyle Make(float size, bool bold, TextAnchor anchor) => new GUIStyle(GUI.skin.label)
            {
                fontSize = Mathf.Max(9, Mathf.RoundToInt(size * s)),
                fontStyle = bold ? FontStyle.Bold : FontStyle.Normal,
                alignment = anchor,
                clipping = TextClipping.Clip,
                wordWrap = false,
                padding = new RectOffset(0, 0, 0, 0),
            };
            _name = Make(19f, true, TextAnchor.MiddleLeft);
            _info = Make(17f, false, TextAnchor.MiddleRight);
            _head = Make(17f, true, TextAnchor.UpperCenter);
            _title = Make(36f, true, TextAnchor.MiddleLeft);
            _sub = Make(17f, false, TextAnchor.MiddleLeft);
            _bar = Make(16f, false, TextAnchor.MiddleLeft);
            _warn = Make(17f, true, TextAnchor.MiddleCenter);
            _tag = Make(14f, true, TextAnchor.MiddleCenter);
            _hint = Make(19f, false, TextAnchor.MiddleLeft);
            _btn = Make(15f, true, TextAnchor.MiddleCenter);
            _arrow = Make(24f, true, TextAnchor.MiddleCenter);
            _count = Make(13f, false, TextAnchor.MiddleRight);
        }

        private static Texture2D MakeVignette()
        {
            const int n = 64;
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false) { hideFlags = HideFlags.DontSave, wrapMode = TextureWrapMode.Clamp };
            var px = new Color[n * n];
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float dx = (x + 0.5f) / n * 2f - 1f, dy = (y + 0.5f) / n * 2f - 1f;
                float d = Mathf.Clamp01((Mathf.Sqrt(dx * dx + dy * dy) - 0.45f) / 0.95f);
                px[y * n + x] = new Color(0f, 0f, 0f, d * d * 0.8f);
            }
            t.SetPixels(px);
            t.Apply();
            return t;
        }

        // ------------------------------------------------------------------ Icons (flat white silhouettes, rasterized once)

        private const int IconRifle = 0, IconBow = 1, IconShield = 2, IconTorch = 3, IconPistol = 4, IconBat = 5,
                          IconRock = 6, IconGrenade = 7, IconFist = 8, IconKnife = 9;
        private static Texture2D[] _icons;

        private static void EnsureIcons()
        {
            if (_icons != null && _icons[0] != null) return;
            _icons = new Texture2D[10];
            _icons[IconRifle] = Icon(100, 50, P(0, 19, 54, 19, 54, 24, 0, 24), P(0, 26, 50, 26, 50, 29, 0, 29),
                P(50, 16, 72, 16, 72, 31, 50, 31), P(62, 31, 70, 31, 66, 40, 59, 40), P(72, 18, 100, 24, 100, 38, 92, 38, 72, 31));
            _icons[IconBow] = Icon(100, 60, Arc(40, 30, 22, 28, 100, 260), P(34, 2, 36, 2, 36, 58, 34, 58),
                P(30, 29, 88, 29, 88, 31, 30, 31), P(86, 24, 98, 30, 86, 36));
            _icons[IconShield] = Icon(60, 60, P(10, 4, 50, 4, 50, 30, 42, 44, 30, 54, 18, 44, 10, 30));
            _icons[IconTorch] = Icon(60, 64, P(27, 24, 33, 24, 32, 62, 28, 62), P(24, 20, 36, 20, 36, 25, 24, 25),
                P(30, 0, 40, 12, 38, 19, 22, 19, 20, 11, 26, 6));
            _icons[IconPistol] = Icon(90, 50, P(4, 6, 74, 6, 76, 10, 76, 20, 4, 20), P(0, 8, 4, 8, 4, 16, 0, 16),
                P(54, 20, 72, 20, 78, 46, 62, 48), P(44, 20, 54, 20, 54, 28, 46, 27));
            _icons[IconBat] = Icon(100, 50, P(0, 44, 3, 48, 58, 28, 96, 16, 100, 10, 96, 6, 55, 20));
            _icons[IconRock] = Icon(60, 44, P(6, 32, 10, 16, 22, 8, 38, 10, 50, 22, 48, 34, 32, 38, 16, 38));
            _icons[IconGrenade] = Icon(60, 60, Circle(28, 36, 15), P(22, 14, 34, 14, 34, 22, 22, 22), P(32, 12, 44, 16, 44, 30, 40, 30, 40, 18, 32, 16));
            _icons[IconFist] = Icon(60, 50, P(8, 16, 34, 10, 46, 14, 50, 22, 48, 36, 36, 42, 12, 40, 6, 28));
            _icons[IconKnife] = Icon(90, 30, P(2, 12, 30, 10, 30, 20, 2, 18), P(30, 6, 34, 6, 34, 24, 30, 24), P(34, 10, 80, 10, 90, 14, 80, 19, 34, 19));
        }

        private static Vector2[] P(params float[] xy)
        {
            var pts = new Vector2[xy.Length / 2];
            for (int i = 0; i < pts.Length; i++) pts[i] = new Vector2(xy[i * 2], xy[i * 2 + 1]);
            return pts;
        }

        private static Vector2[] Arc(float cx, float cy, float rIn, float rOut, float a0, float a1)
        {
            const int n = 16;
            var pts = new Vector2[n * 2];
            for (int i = 0; i < n; i++)
            {
                float a = Mathf.Lerp(a0, a1, i / (n - 1f)) * Mathf.Deg2Rad;
                pts[i] = new Vector2(cx + Mathf.Cos(a) * rOut, cy + Mathf.Sin(a) * rOut);
                pts[n * 2 - 1 - i] = new Vector2(cx + Mathf.Cos(a) * rIn, cy + Mathf.Sin(a) * rIn);
            }
            return pts;
        }

        private static Vector2[] Circle(float cx, float cy, float r)
        {
            const int n = 20;
            var pts = new Vector2[n];
            for (int i = 0; i < n; i++)
            {
                float a = i / (float)n * Mathf.PI * 2f;
                pts[i] = new Vector2(cx + Mathf.Cos(a) * r, cy + Mathf.Sin(a) * r);
            }
            return pts;
        }

        private static Texture2D Icon(float vw, float vh, params Vector2[][] polys)
        {
            const int size = 128;
            float scale = (size - 6f) / Mathf.Max(vw, vh);
            float ox = (size - vw * scale) * 0.5f, oy = (size - vh * scale) * 0.5f;
            var t = new Texture2D(size, size, TextureFormat.RGBA32, false) { hideFlags = HideFlags.DontSave, wrapMode = TextureWrapMode.Clamp };
            var px = new Color[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                int hits = 0;
                for (int sy = 0; sy < 4; sy++)
                for (int sx = 0; sx < 4; sx++)
                {
                    // texture y goes up, the drawing's y goes down
                    float gx = (x + (sx + 0.5f) / 4f - ox) / scale;
                    float gy = (size - (y + (sy + 0.5f) / 4f) - oy) / scale;
                    foreach (var poly in polys)
                        if (Inside(poly, gx, gy)) { hits++; break; }
                }
                px[y * size + x] = new Color(1f, 1f, 1f, hits / 16f);
            }
            t.SetPixels(px);
            t.Apply();
            return t;
        }

        private static bool Inside(Vector2[] poly, float x, float y)
        {
            bool inside = false;
            for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
            {
                if ((poly[i].y > y) != (poly[j].y > y) &&
                    x < (poly[j].x - poly[i].x) * (y - poly[i].y) / (poly[j].y - poly[i].y) + poly[i].x)
                    inside = !inside;
            }
            return inside;
        }
    }
}
