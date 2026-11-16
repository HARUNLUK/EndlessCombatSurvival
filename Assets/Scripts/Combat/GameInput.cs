using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace EndlessCombat.Combat
{
    /// <summary>
    /// One place for the combat and loadout buttons. The game is designed for a gamepad; the keyboard and mouse
    /// are a fallback with the same meaning.
    /// Weapon buttons since 2026-11-16 (Combat_Action_Plan.md, the dominant decision):
    ///   Gamepad                     Keyboard / mouse
    ///   RT   fire / heavy strike / throw (by what is in the hand)   left click (fire / quick strike / throw)
    ///   LT   aim (draws the holstered weapon first) / shield up     right click
    ///   RB   quick strike with what is in the hand                   V
    ///   LB   tap: draw / holster, hold: selection map (WeaponSelectMap reads LB itself)   R hold: draw / holster
    ///   X    reload (tap)           R (tap)
    ///   B, Y not used by the weapons. The D-pad is not used by the weapons (no slots, no quick item).
    ///   Keyboard picks (no map yet): 1 long gun, 2 pistol, 3 melee, T rock, G grenade. 4 = quick item.
    /// Combat presses go into the PlayerCombatState input buffer (Combat_Architecture.md); only that state and
    /// the menus read the combat buttons here.
    ///   View (select)               Tab: inventory
    ///   LB / RB in menus            Q / E: change tab
    ///   L3 sprint, R3 crouch (crouch keeps C / Left Ctrl on the keyboard)
    /// </summary>
    public static class GameInput
    {
        public const float HoldTime = 0.4f;

        private static int _yFrame = -1;
        private static float _yDownAt = -1f;
        private static bool _yHoldFired;
        private static bool _yTap, _yHold;

        /// <summary>
        /// Set by the tuning panel (Debug/TuningPanel.cs): while it is open its D-pad / LB / RB / X / A presses
        /// must not switch weapons, attack or interact, so every button read here goes dead. The sticks
        /// (PadLeftStick / PadRightStick) and the movement / look / driving input keep working.
        /// </summary>
        public static bool TuningPanelOpen;

        /// <summary>
        /// Set by WeaponSelectMap while LB is held and the selection map is up: its face buttons / RB / RT pick a
        /// weapon there, so every gamepad button read here goes dead (no jump, no reload, no strike). Sticks keep working.
        /// </summary>
        public static bool SelectMapOpen;

#if ENABLE_INPUT_SYSTEM
        private static Gamepad Pad => TuningPanelOpen || SelectMapOpen ? null : Gamepad.current;
        private static Keyboard Kb => TuningPanelOpen ? null : Keyboard.current;
        private static Mouse Ms => TuningPanelOpen ? null : Mouse.current;
        private static bool KeyDown(Key k) => Kb != null && Kb[k].wasPressedThisFrame;
        private static bool KeyHeld(Key k) => Kb != null && Kb[k].isPressed;
#endif

        /// <summary>True when the last used device is a gamepad (for button hints).</summary>
        public static bool UsingPad
        {
            get
            {
#if ENABLE_INPUT_SYSTEM
                var gp = Gamepad.current;
                if (gp == null) return false;
                double pad = gp.lastUpdateTime;
                double kb = Keyboard.current != null ? Keyboard.current.lastUpdateTime : 0;
                double ms = Mouse.current != null ? Mouse.current.lastUpdateTime : 0;
                return pad >= kb && pad >= ms;
#else
                return false;
#endif
            }
        }

        // ------------------------------------------------------------------ Firearms

        public static bool FireHeld
        {
            get
            {
#if ENABLE_INPUT_SYSTEM
                return (Pad != null && Pad.rightTrigger.isPressed) || (Ms != null && Ms.leftButton.isPressed);
#else
                return Input.GetMouseButton(0);
#endif
            }
        }

        public static bool FirePressed => TriggerPressed || MouseLeftPressed;

        /// <summary>RT pressed this frame: fire, heavy strike or throw, by what is in the hand (PlayerCombatState).</summary>
        public static bool TriggerPressed
        {
            get
            {
#if ENABLE_INPUT_SYSTEM
                return Pad != null && Pad.rightTrigger.wasPressedThisFrame;
#else
                return false;
#endif
            }
        }

        /// <summary>Left click this frame: fire, quick strike or throw, by what is in the hand.</summary>
        public static bool MouseLeftPressed
        {
            get
            {
#if ENABLE_INPUT_SYSTEM
                return Ms != null && Ms.leftButton.wasPressedThisFrame;
#else
                return Input.GetMouseButtonDown(0);
#endif
            }
        }

        /// <summary>LT / right mouse: aim with a firearm, guard with a melee weapon or a shield.</summary>
        public static bool AimHeld
        {
            get
            {
#if ENABLE_INPUT_SYSTEM
                return (Pad != null && Pad.leftTrigger.isPressed) || (Ms != null && Ms.rightButton.isPressed);
#else
                return Input.GetMouseButton(1);
#endif
            }
        }

        /// <summary>X tapped (gamepad) / R tapped (keyboard): reload.</summary>
        public static bool ReloadPressed
        {
            get
            {
                UpdateR();
#if ENABLE_INPUT_SYSTEM
                if (_rTap) return true;
                // X is "interact" while something is in reach (pick up, open, get in); the reload waits
                if (InteractAvailable) return false;
                return Pad != null && Pad.buttonWest.wasPressedThisFrame;
#else
                return false;
#endif
            }
        }

        // Keyboard R: tap = reload, hold = stow everything / draw again (same as gamepad X / Y hold)
        private static int _rFrame = -1;
        private static float _rDownAt = -1f;
        private static bool _rHoldFired, _rTap, _rHold;

        private static void UpdateR()
        {
            if (_rFrame == Time.frameCount) return;
            _rFrame = Time.frameCount;
            _rTap = false;
            _rHold = false;
#if ENABLE_INPUT_SYSTEM
            if (Kb == null) { _rDownAt = -1f; return; }
            var r = Kb.rKey;
            if (r.wasPressedThisFrame)
            {
                _rDownAt = Time.unscaledTime;
                _rHoldFired = false;
            }
            if (r.isPressed && _rDownAt >= 0f && !_rHoldFired && Time.unscaledTime - _rDownAt >= HoldTime)
            {
                _rHoldFired = true;
                _rHold = true;
            }
            if (r.wasReleasedThisFrame)
            {
                if (!_rHoldFired && _rDownAt >= 0f) _rTap = true;
                _rDownAt = -1f;
            }
#endif
        }

        /// <summary>Button name for on-screen hints: the gamepad one when a gamepad was used last.</summary>
        public static string Hint(string keyboard, string pad) => UsingPad ? pad : keyboard;

        // ------------------------------------------------------------------ World and menus

        private static float _gameplayBlockedUntil = -1f;

        /// <summary>
        /// A menu just closed with a gamepad button that also means something in the game (B = dodge, A = jump):
        /// combat input and jumping are ignored for a moment.
        /// </summary>
        public static void BlockGameplayBriefly(float seconds = 0.2f) => _gameplayBlockedUntil = Time.unscaledTime + seconds;

        public static bool GameplayBlocked => Time.unscaledTime < _gameplayBlockedUntil;

        private static int _interactFrame = -10;

        /// <summary>
        /// Something to interact with is in reach this frame (set by PlayerInteractor and the vehicle). While true the
        /// gamepad X button interacts instead of reloading.
        /// </summary>
        public static bool InteractAvailable => InteractTargetPresent || Time.frameCount - _vehicleFrame <= 1;

        /// <summary>The interactor has a target (pickup, chest...): it wins over the vehicle for the X button.</summary>
        public static bool InteractTargetPresent => Time.frameCount - _interactFrame <= 1;

        private static int _vehicleFrame = -10;

        /// <summary>The player stands next to the vehicle and can get in.</summary>
        public static void MarkVehicleNear() => _vehicleFrame = Time.frameCount;

        public static void MarkInteractAvailable() => _interactFrame = Time.frameCount;

        /// <summary>E / gamepad X: pick up, open, gather, talk.</summary>
        public static bool InteractPressed
        {
            get
            {
#if ENABLE_INPUT_SYSTEM
                return KeyDown(Key.E) || (Pad != null && Pad.buttonWest.wasPressedThisFrame);
#else
                return Input.GetKeyDown(KeyCode.E);
#endif
            }
        }

        /// <summary>F / gamepad X next to the vehicle: get in.</summary>
        public static bool VehicleEnterPressed
        {
            get
            {
#if ENABLE_INPUT_SYSTEM
                return KeyDown(Key.F) || (Pad != null && Pad.buttonWest.wasPressedThisFrame && !InteractTargetPresent);
#else
                return Input.GetKeyDown(KeyCode.F);
#endif
            }
        }

        /// <summary>F / gamepad Y in the vehicle: get out.</summary>
        public static bool VehicleExitPressed
        {
            get
            {
#if ENABLE_INPUT_SYSTEM
                return KeyDown(Key.F) || (Pad != null && Pad.buttonNorth.wasPressedThisFrame);
#else
                return Input.GetKeyDown(KeyCode.F);
#endif
            }
        }

        /// <summary>Esc / gamepad Start: pause menu.</summary>
        public static bool PausePressed
        {
            get
            {
#if ENABLE_INPUT_SYSTEM
                return KeyDown(Key.Escape) || (Pad != null && Pad.startButton.wasPressedThisFrame);
#else
                return Input.GetKeyDown(KeyCode.Escape);
#endif
            }
        }

        /// <summary>Esc / gamepad B: back, close.</summary>
        public static bool BackPressed
        {
            get
            {
#if ENABLE_INPUT_SYSTEM
                return KeyDown(Key.Escape) || (Pad != null && Pad.buttonEast.wasPressedThisFrame);
#else
                return Input.GetKeyDown(KeyCode.Escape);
#endif
            }
        }

        /// <summary>Gamepad A / Enter: confirm in menus and placement.</summary>
        public static bool SubmitPressed
        {
            get
            {
#if ENABLE_INPUT_SYSTEM
                return (Pad != null && Pad.buttonSouth.wasPressedThisFrame) || KeyDown(Key.Enter) || KeyDown(Key.NumpadEnter);
#else
                return Input.GetKeyDown(KeyCode.Return);
#endif
            }
        }

        /// <summary>Gamepad button this frame (menus): 0 A, 1 B, 2 X, 3 Y, 4 LB, 5 RB, 6 Start.</summary>
        public static bool PadPressed(int button)
        {
#if ENABLE_INPUT_SYSTEM
            if (Pad == null) return false;
            switch (button)
            {
                case 0: return Pad.buttonSouth.wasPressedThisFrame;
                case 1: return Pad.buttonEast.wasPressedThisFrame;
                case 2: return Pad.buttonWest.wasPressedThisFrame;
                case 3: return Pad.buttonNorth.wasPressedThisFrame;
                case 4: return Pad.leftShoulder.wasPressedThisFrame;
                case 5: return Pad.rightShoulder.wasPressedThisFrame;
                case 6: return Pad.startButton.wasPressedThisFrame;
            }
#endif
            return false;
        }

        /// <summary>Gamepad right stick (camera in the vehicle, zoom in the binoculars).</summary>
        public static Vector2 PadRightStick
        {
            get
            {
#if ENABLE_INPUT_SYSTEM
                return Gamepad.current != null ? Gamepad.current.rightStick.ReadValue() : Vector2.zero;
#else
                return Vector2.zero;
#endif
            }
        }

        public static Vector2 PadLeftStick
        {
            get
            {
#if ENABLE_INPUT_SYSTEM
                return Gamepad.current != null ? Gamepad.current.leftStick.ReadValue() : Vector2.zero;
#else
                return Vector2.zero;
#endif
            }
        }

        /// <summary>Gamepad D-pad buttons pressed this frame as a direction (x right, y up).</summary>
        public static Vector2Int PadDpadPressed
        {
            get
            {
#if ENABLE_INPUT_SYSTEM
                if (Pad == null) return Vector2Int.zero;
                int x = Pad.dpad.right.wasPressedThisFrame ? 1 : Pad.dpad.left.wasPressedThisFrame ? -1 : 0;
                int y = Pad.dpad.up.wasPressedThisFrame ? 1 : Pad.dpad.down.wasPressedThisFrame ? -1 : 0;
                return new Vector2Int(x, y);
#else
                return Vector2Int.zero;
#endif
            }
        }

        // D-pad down hold = action menu (toilet, smoke, binoculars, fishing, camp). The tap did the quick item; the
        // D-pad left the weapon system on 2026-11-16 (Combat_Action_Plan.md), so the tap does nothing now.
        private static int _downFrame = -1;
        private static float _downAt = -1f;
        private static bool _downHoldFired, _downTap, _downHold;

        private static void UpdateDown()
        {
            if (_downFrame == Time.frameCount) return;
            _downFrame = Time.frameCount;
            _downTap = false;
            _downHold = false;
#if ENABLE_INPUT_SYSTEM
            if (Pad == null) { _downAt = -1f; return; }
            var d = Pad.dpad.down;
            if (d.wasPressedThisFrame)
            {
                _downAt = Time.unscaledTime;
                _downHoldFired = false;
            }
            if (d.isPressed && _downAt >= 0f && !_downHoldFired && Time.unscaledTime - _downAt >= HoldTime)
            {
                _downHoldFired = true;
                _downHold = true;
            }
            if (d.wasReleasedThisFrame)
            {
                if (!_downHoldFired && _downAt >= 0f) _downTap = true;
                _downAt = -1f;
            }
#endif
        }

        /// <summary>Key 4: use the quick item (not on the gamepad any more; quick items get their own design later).</summary>
        public static bool QuickItemPressed
        {
            get
            {
#if ENABLE_INPUT_SYSTEM
                return KeyDown(Key.Digit4);
#else
                return false;
#endif
            }
        }

        /// <summary>D-pad down held / Q: the action menu.</summary>
        public static bool ActionMenuPressed
        {
            get
            {
                UpdateDown();
#if ENABLE_INPUT_SYSTEM
                return _downHold || KeyDown(Key.Q);
#else
                return _downHold;
#endif
            }
        }

        // ------------------------------------------------------------------ Melee

        /// <summary>
        /// RB / V: the quick strike with what is in the hand (fists, melee weapon, gun butt / pistol whip with a
        /// firearm). The heavy strike is RT with a melee weapon or the fists (Combat_Action_Plan.md 6).
        /// </summary>
        public static bool LightAttackPressed
        {
            get
            {
#if ENABLE_INPUT_SYSTEM
                return (Pad != null && Pad.rightShoulder.wasPressedThisFrame) || KeyDown(Key.V);
#else
                return Input.GetKeyDown(KeyCode.V);
#endif
            }
        }

        public static bool CrouchPressed
        {
            get
            {
#if ENABLE_INPUT_SYSTEM
                return Pad != null && Pad.rightStickButton.wasPressedThisFrame;
#else
                return false;
#endif
            }
        }

        // ------------------------------------------------------------------ Loadout

        /// <summary>
        /// Keyboard picks (the gamepad picks on the LB selection map): PlayerLoadout.Pick codes 0 long gun (1),
        /// 1 pistol (2), 2 melee (3), 5 rock (T), 6 grenade (G), 8 torch (5); -1 when none was pressed this frame.
        /// </summary>
        public static int SlotPressed
        {
            get
            {
#if ENABLE_INPUT_SYSTEM
                if (KeyDown(Key.Digit1)) return 0;
                if (KeyDown(Key.Digit2)) return 1;
                if (KeyDown(Key.Digit3)) return 2;
                if (KeyDown(Key.T)) return 5;
                if (KeyDown(Key.G)) return 6;
                if (KeyDown(Key.Digit5)) return 8; // torch (left hand)
#endif
                return -1;
            }
        }

        /// <summary>R held (keyboard): draw the last weapon / put the one in the hand back (gamepad: LB tap).</summary>
        public static bool HolsterKeyPressed
        {
            get
            {
                UpdateR();
                return _rHold;
            }
        }

        /// <summary>View / select: open the inventory.</summary>
        public static bool InventoryPressed
        {
            get
            {
#if ENABLE_INPUT_SYSTEM
                return Pad != null && Pad.selectButton.wasPressedThisFrame;
#else
                return false;
#endif
            }
        }

        private static void UpdateY()
        {
            if (_yFrame == Time.frameCount) return;
            _yFrame = Time.frameCount;
            _yTap = false;
            _yHold = false;
#if ENABLE_INPUT_SYSTEM
            if (Pad == null) { _yDownAt = -1f; return; }
            var y = Pad.buttonNorth;
            if (y.wasPressedThisFrame)
            {
                _yDownAt = Time.unscaledTime;
                _yHoldFired = false;
            }
            if (y.isPressed && _yDownAt >= 0f && !_yHoldFired && Time.unscaledTime - _yDownAt >= HoldTime)
            {
                _yHoldFired = true;
                _yHold = true;
            }
            if (y.wasReleasedThisFrame)
            {
                if (!_yHoldFired && _yDownAt >= 0f) _yTap = true;
                _yDownAt = -1f;
            }
#endif
        }
    }
}
