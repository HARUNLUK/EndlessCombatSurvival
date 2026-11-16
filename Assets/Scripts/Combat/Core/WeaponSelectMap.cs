using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif
using EndlessSurvival.Inventory;

namespace EndlessCombat.Combat
{
    /// <summary>
    /// LB on the gamepad (Combat_Action_Plan.md 1, 3; 2026-11-16):
    ///   tap (shorter than HoldTime)  the weapon in the hand goes to its holster / the last one comes out
    ///   held                         the selection map is open while LB is held; one button picks:
    ///                                Y long gun, X pistol, RB melee, A shield (arm / back), LT torch, B bow, RT rock / grenade
    ///                                (RT again switches between them). The pick comes to the hand at once.
    /// While the map is open the other gamepad presses are not seen by the game (GameInput.SelectMapOpen).
    /// The look of the map is a placeholder: its design is being talked over separately (Combat_Action_Plan.md 3).
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

        private bool _lbDown;
        private float _lbDownAt;
        private int _lastPick = -1;

        public bool IsOpen => GameInput.SelectMapOpen && _lbDown;

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

#if ENABLE_INPUT_SYSTEM
            Gamepad pad = GameInput.TuningPanelOpen ? null : Gamepad.current;
            if (pad == null || _state == null || _state.InputBlocked || EndlessSurvival.Survival.HandsBusyHolster.HandsBusy)
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
                _lastPick = -1;
            }
            else if (!lb && _lbDown)
            {
                _lbDown = false;
                bool wasOpen = GameInput.SelectMapOpen;
                Close();
                if (!wasOpen && Time.unscaledTime - _lbDownAt < HoldTime) _state.Press(CombatIntent.Holster);
                return;
            }

            if (!_lbDown) return;
            if (!GameInput.SelectMapOpen && Time.unscaledTime - _lbDownAt >= HoldTime) GameInput.SelectMapOpen = true;
            if (!GameInput.SelectMapOpen) return;

            int pick = -1;
            if (pad.buttonNorth.wasPressedThisFrame) pick = PlayerLoadout.PickLongGun;
            else if (pad.buttonWest.wasPressedThisFrame) pick = PlayerLoadout.PickPistol;
            else if (pad.rightShoulder.wasPressedThisFrame) pick = PlayerLoadout.PickMelee;
            else if (pad.buttonSouth.wasPressedThisFrame) pick = PlayerLoadout.PickShield;
            else if (pad.buttonEast.wasPressedThisFrame) pick = PlayerLoadout.PickBow;
            else if (pad.rightTrigger.wasPressedThisFrame) pick = PlayerLoadout.PickThrowable;
            else if (pad.leftTrigger.wasPressedThisFrame) pick = PlayerLoadout.PickTorch; // placeholder button until the 3b map
            if (pick >= 0)
            {
                _lastPick = pick;
                _state.Press(CombatIntent.Swap, pick);
            }
#endif
        }

        private void Close()
        {
            GameInput.SelectMapOpen = false;
        }

        // ------------------------------------------------------------------ Placeholder look

        private GUIStyle _box, _label;

        private void OnGUI()
        {
            if (!GameInput.SelectMapOpen || _loadout == null) return;
            if (_box == null)
            {
                _box = new GUIStyle(GUI.skin.box) { fontSize = 15, alignment = TextAnchor.MiddleCenter, wordWrap = true };
                _label = new GUIStyle(GUI.skin.label) { fontSize = 13, alignment = TextAnchor.MiddleCenter };
            }

            float w = 190f, h = 58f, gap = 10f;
            Vector2 c = new Vector2(Screen.width * 0.5f, Screen.height * 0.55f);
            Cell(c + new Vector2(0f, -(h + gap)), w, h, "Y", SlotText(PlayerLoadout.Slot.LongGun, "Uzun namlu"), PlayerLoadout.PickLongGun);
            Cell(c + new Vector2(-(w + gap), 0f), w, h, "X", SlotText(PlayerLoadout.Slot.Pistol, "Tabanca"), PlayerLoadout.PickPistol);
            Cell(c + new Vector2(w + gap, 0f), w, h, "B", "Yay (yok)", PlayerLoadout.PickBow);
            Cell(c + new Vector2(0f, h + gap), w, h, "A", ShieldText(), PlayerLoadout.PickShield);
            Cell(c + new Vector2(w + gap, -(h + gap)), w, h, "RB", SlotText(PlayerLoadout.Slot.Melee, "Yakın dövüş"), PlayerLoadout.PickMelee);
            Cell(c + new Vector2(w + gap, h + gap), w, h, "RT", ThrowText(), PlayerLoadout.PickThrowable);
            Cell(c + new Vector2(-(w + gap), h + gap), w, h, "LT", TorchText(), PlayerLoadout.PickTorch);

            string warn = _loadout.RightDrawn || (_thrower != null && _thrower.HasItem)
                ? "Elindeki kınına konmadı: başka seçim onu yere düşürür (LB'ye kısa bas: kınına koy)"
                : "Sağ el boş: seçim düşürmeden ele gelir";
            GUI.Label(new Rect(c.x - 320f, c.y + 2f * (h + gap) + 4f, 640f, 24f), warn, _label);
        }

        private void Cell(Vector2 center, float w, float h, string button, string text, int pick)
        {
            Color old = GUI.color;
            if (pick == _lastPick) GUI.color = new Color(1f, 0.9f, 0.5f);
            GUI.Box(new Rect(center.x - w * 0.5f, center.y - h * 0.5f, w, h), $"{button}\n{text}", _box);
            GUI.color = old;
        }

        private string SlotText(PlayerLoadout.Slot slot, string empty)
        {
            ItemDefinition item = _loadout.Item(slot);
            string name = item != null ? item.DisplayName : slot == PlayerLoadout.Slot.Melee ? "Yumruk" : empty + " (boş)";
            bool inHand = _loadout.Active == slot && _loadout.RightDrawn;
            return inHand ? name + " (elde)" : name;
        }

        private string ShieldText()
        {
            if (_shield == null || !_shield.HasShield) return "Kalkan (yok)";
            string name = _shield.Item != null ? _shield.Item.DisplayName : "Kalkan";
            return _loadout.ShieldOnArm ? name + " (kolda)" : name + " (sırtta)";
        }

        private TorchHolder _torch;

        private string TorchText()
        {
            if (_torch == null) _torch = GetComponent<TorchHolder>();
            if (_torch != null && _torch.Held) return $"Meşale (elde, %{Mathf.RoundToInt(_torch.Fuel01 * 100f)})";
            return _torch != null && _torch.TorchInBag() != null ? "Meşale" : "Meşale (yok)";
        }

        private string ThrowText()
        {
            if (_thrower == null) return "Fırlatılabilir";
            int rocks = _thrower.Count(PlayerThrower.Kind.Rock), nades = _thrower.Count(PlayerThrower.Kind.Grenade);
            string held = _thrower.HasItem ? (_thrower.Held == PlayerThrower.Kind.Rock ? " - elde taş" : " - elde el bombası") : "";
            return $"Taş {rocks} / El bombası {nades}{held}";
        }
    }
}
