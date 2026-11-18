using UnityEngine;
using UnityEngine.InputSystem;

namespace EndlessCombat.Character
{
    /// <summary>
    /// Movement input of the main character, read straight from the devices every frame (gamepad first, keyboard
    /// and mouse second). No PlayerInput component, no input action asset, no SendMessage.
    ///   move    left stick / WASD            (x = right, y = forward, length 0..1)
    ///   look    right stick / mouse          (degrees to turn this frame: x = yaw, y = pitch, + is down)
    ///   jump    A / Space                    (set on press, the motor clears it)
    ///   sprint  L3 click (latched until the stick is let go) / Left Shift held
    /// The combat and survival keys are in Combat/GameInput.cs.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    [DisallowMultipleComponent]
    public class PlayerInputState : MonoBehaviour
    {
        [Header("State (read by the motor and the game systems)")]
        public Vector2 move;
        public Vector2 look;
        public bool jump;
        public bool sprint;
        [Tooltip("Stick tilt scales the speed (half tilt = walk)")]
        public bool analogMovement = true;

        [Header("Mouse cursor")]
        public bool cursorLocked = true;
        [Tooltip("Off while a menu owns the mouse: the camera does not turn")]
        public bool cursorInputForLook = true;

        [Header("Look speed")]
        [Tooltip("Gamepad: degrees per second at full tilt (yaw, pitch)")]
        public Vector2 padLookSpeed = new Vector2(220f, 140f);
        [Tooltip("Mouse: degrees per pixel")]
        public float mouseLookSpeed = 0.06f;
        [Range(0f, 0.5f)] public float stickDeadZone = 0.15f;

        [Header("Gamepad look feel (Core_Feel_Plan stage 1)")]
        [Tooltip("Stick held at the rim: the turn speed grows to this multiple (fast 180)")]
        public float padBoost = 1.6f;
        [Tooltip("Seconds at the rim until the full boost")]
        public float padBoostTime = 0.35f;
        [Tooltip("Gamepad look speed multiplier while aiming a weapon (fine aim)")]
        public float padAimScale = 0.55f;
        [Tooltip("Aim assist: look speed multiplier while the crosshair is over an enemy (aiming, gamepad only)")]
        public float aimSlowdown = 0.6f;
        public bool aimAssist = true;

        /// <summary>Tuning panel: sprint only while L3 is held (default off: a click latches it).</summary>
        public static bool SprintHoldMode;

        private float _rimTime;
        private PlayerMotor _motor;
        private int _assistFrame = -1;
        private bool _assistHit;

        private void Awake()
        {
            _motor = GetComponent<PlayerMotor>();
            // The scene still holds the old 170 / 110: move it to the stage 1 base (an inspector change is kept)
            if (padLookSpeed == new Vector2(170f, 110f)) padLookSpeed = new Vector2(220f, 140f);
            const string P = "Kamera";
            EndlessCombat.Tuning.TuningPanel.Register(P, "Gamepad yatay hız", () => padLookSpeed.x, v => padLookSpeed.x = v, 60f, 600f, 5f, this);
            EndlessCombat.Tuning.TuningPanel.Register(P, "Gamepad dikey hız", () => padLookSpeed.y, v => padLookSpeed.y = v, 40f, 400f, 5f, this);
            EndlessCombat.Tuning.TuningPanel.Register(P, "Hızlı dönüş çarpanı", () => padBoost, v => padBoost = v, 1f, 3f, 0.05f, this);
            EndlessCombat.Tuning.TuningPanel.Register(P, "Hızlı dönüş süresi (sn)", () => padBoostTime, v => padBoostTime = v, 0.05f, 2f, 0.05f, this);
            EndlessCombat.Tuning.TuningPanel.Register(P, "Nişanda bakış çarpanı", () => padAimScale, v => padAimScale = v, 0.1f, 1f, 0.05f, this);
            EndlessCombat.Tuning.TuningPanel.RegisterToggle(P, "Nişan yardımı", () => aimAssist, v => aimAssist = v, this);
            EndlessCombat.Tuning.TuningPanel.Register(P, "Nişan yardımı yavaşlatma", () => aimSlowdown, v => aimSlowdown = v, 0.1f, 1f, 0.05f, this);
            EndlessCombat.Tuning.TuningPanel.Register(P, "Fare hızı", () => mouseLookSpeed, v => mouseLookSpeed = v, 0.01f, 0.3f, 0.005f, this);
            EndlessCombat.Tuning.TuningPanel.RegisterToggle("Karakter", "Depar: basılı tut", () => SprintHoldMode, v => SprintHoldMode = v, this);
            EndlessCombat.Tuning.TuningPanel.Register(P, "Çubuk ölü bölge", () => stickDeadZone, v => stickDeadZone = v, 0f, 0.5f, 0.01f, this);
        }

        /// <summary>The crosshair (screen centre) is over an enemy. Checked at most once per frame.</summary>
        private bool CrosshairOnEnemy()
        {
            if (_assistFrame == Time.frameCount) return _assistHit;
            _assistFrame = Time.frameCount;
            _assistHit = false;
            Camera cam = Camera.main;
            if (cam == null) return false;
            Ray ray = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            if (Physics.SphereCast(ray, 0.35f, out RaycastHit hit, 80f, ~0, QueryTriggerInteraction.Ignore))
            {
                var enemy = hit.collider.GetComponentInParent<EndlessCombat.AI.EnemyController>();
                _assistHit = enemy != null && !enemy.IsDead;
            }
            return _assistHit;
        }

        /// <summary>Set by the game: true while a menu or panel owns the input. Move, jump and sprint are ignored.</summary>
        public static System.Func<bool> Blocked;
        /// <summary>Look sensitivity from the settings menu.</summary>
        public static float LookSensitivity = 1f;

        private bool _padSprint;

        private void Update()
        {
            Gamepad pad = Gamepad.current;
            Keyboard kb = Keyboard.current;
            Mouse mouse = Mouse.current;
            bool blocked = (Blocked != null && Blocked()) || EndlessCombat.Combat.GameInput.GameplayBlocked;

            // Move
            Vector2 m = Vector2.zero;
            if (pad != null)
            {
                Vector2 stick = pad.leftStick.ReadValue();
                float len = stick.magnitude;
                if (len > stickDeadZone) m = stick / len * Mathf.InverseLerp(stickDeadZone, 0.95f, len);
            }
            if (kb != null)
            {
                Vector2 keys = new Vector2((kb.dKey.isPressed ? 1f : 0f) - (kb.aKey.isPressed ? 1f : 0f),
                                           (kb.wKey.isPressed ? 1f : 0f) - (kb.sKey.isPressed ? 1f : 0f));
                if (keys != Vector2.zero) m = keys.normalized;
            }
            move = blocked ? Vector2.zero : m;

            // Look
            Vector2 l = Vector2.zero;
            // The LB selection map uses the right stick to move its cursor (Combat_Action_Plan.md 3b): the camera stays
            if (cursorInputForLook && !EndlessCombat.Combat.GameInput.SelectMapOpen)
            {
                if (pad != null)
                {
                    Vector2 stick = pad.rightStick.ReadValue();
                    float len = stick.magnitude;
                    if (len > stickDeadZone)
                    {
                        // Soft start: fine aim near the centre, full speed at the rim
                        float t = Mathf.InverseLerp(stickDeadZone, 1f, len);
                        stick = stick / len * (t * t * 0.6f + t * 0.4f);
                        // Held at the rim: speed ramps up for a fast turn; any let-off resets it
                        _rimTime = len > 0.95f ? _rimTime + Time.unscaledDeltaTime : 0f;
                        float boost = Mathf.Lerp(1f, padBoost, padBoostTime > 0.001f ? Mathf.Clamp01(_rimTime / padBoostTime) : 1f);
                        float scale = boost;
                        bool aiming = _motor != null && _motor.IsAiming;
                        if (aiming)
                        {
                            scale = padAimScale;
                            if (aimAssist && CrosshairOnEnemy()) scale *= aimSlowdown;
                        }
                        l += new Vector2(stick.x * padLookSpeed.x, -stick.y * padLookSpeed.y) * (scale * Time.unscaledDeltaTime);
                    }
                    else _rimTime = 0f;
                }
                if (mouse != null && Cursor.lockState == CursorLockMode.Locked)
                {
                    Vector2 d = mouse.delta.ReadValue();
                    l += new Vector2(d.x, -d.y) * mouseLookSpeed;
                }
            }
            look = l * LookSensitivity;

            // Jump
            // No jump while the LB selection map is up (Combat_Action_Plan.md 3b)
            if (blocked || EndlessCombat.Combat.GameInput.SelectMapOpen) jump = false;
            else if ((pad != null && pad.buttonSouth.wasPressedThisFrame) || (kb != null && kb.spaceKey.wasPressedThisFrame)) jump = true;

            // Sprint: L3 click latches until the stick is let go, or (tuning panel) only while L3 is held
            if (pad != null)
            {
                if (SprintHoldMode) _padSprint = pad.leftStickButton.isPressed;
                else
                {
                    if (pad.leftStickButton.wasPressedThisFrame) _padSprint = true;
                    if (pad.leftStick.ReadValue().sqrMagnitude < 0.09f) _padSprint = false;
                }
            }
            else _padSprint = false;
            sprint = !blocked && (_padSprint || (kb != null && kb.leftShiftKey.isPressed));
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            if (hasFocus) Cursor.lockState = cursorLocked ? CursorLockMode.Locked : CursorLockMode.None;
        }
    }
}
