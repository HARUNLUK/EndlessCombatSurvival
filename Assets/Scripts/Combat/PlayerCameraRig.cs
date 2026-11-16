using Unity.Cinemachine;
using UnityEngine;
using EndlessCombat.Tuning;

namespace EndlessCombat.Combat
{
    /// <summary>
    /// Shapes the player's over-the-shoulder camera (the scene's "PlayerFollowCamera") by what the character does.
    /// Core_Feel_Plan stage 1 (2026-11-14): the base values are no longer read from the scene, they are applied from
    /// here (the scene camera was 40 degrees, 4 m, 1 m shoulder) and every value is on the tuning panel, page "Kamera".
    ///   walking / running   base FOV, distance, shoulder, light damping
    ///   sprinting           FOV a little wider
    ///   aiming a gun        own distance and FOV per weapon kind, no damping (the crosshair must not swim)
    ///   crouching           lower shoulder
    /// The melee "+0.8 m back" was removed with the Souls combat (stage 2). Added by PlayerShooter.Awake.
    /// </summary>
    [DisallowMultipleComponent]
    public class PlayerCameraRig : MonoBehaviour
    {
        [Header("Base (walking)")]
        public float baseFov = 60f;
        public float baseDistance = 2.8f;
        public float shoulderX = 0.55f;
        [Tooltip("Height of the shoulder pivot (m). Negative = the value the scene camera has is kept")]
        public float shoulderY = -1f;
        public float verticalArm = 0.2f;
        [Tooltip("Cinemachine damping on x / y / z while not aiming (s)")]
        public float damping = 0.1f;
        public float sprintExtraFov = 5f;

        [Header("Aiming")]
        public float rifleAimDistance = 1.3f;
        public float rifleAimFov = 45f;
        public float pistolAimDistance = 1.6f;
        public float pistolAimFov = 50f;
        public float aimDamping = 0f;

        [Header("Other")]
        [Tooltip("Shoulder height change while crouching (m)")]
        public float crouchShoulderDrop = 0.35f;
        [Tooltip("Blend speed (1 / s)")]
        public float blendSpeed = 10f;

        private PlayerShooter _shooter;
        private Character.PlayerMotor _motor;
        private CinemachineCamera _camera;
        private CinemachineThirdPersonFollow _follow;
        private float _sceneShoulderY;
        private float _nextSearch;

        private void Awake()
        {
            _shooter = GetComponent<PlayerShooter>();
            _motor = GetComponent<Character.PlayerMotor>();
            const string P = "Kamera";
            TuningPanel.Register(P, "Görüş açısı", () => baseFov, v => baseFov = v, 30f, 100f, 1f, this);
            TuningPanel.Register(P, "Mesafe (m)", () => baseDistance, v => baseDistance = v, 1f, 8f, 0.1f, this);
            TuningPanel.Register(P, "Omuz kayması (m)", () => shoulderX, v => shoulderX = v, -1.5f, 1.5f, 0.05f, this);
            TuningPanel.Register(P, "Dikey kol (m)", () => verticalArm, v => verticalArm = v, -1f, 1.5f, 0.05f, this);
            TuningPanel.Register(P, "Yumuşatma (sn)", () => damping, v => damping = v, 0f, 1f, 0.02f, this);
            TuningPanel.Register(P, "Depar ek açı", () => sprintExtraFov, v => sprintExtraFov = v, 0f, 20f, 1f, this);
            TuningPanel.Register(P, "Tüfek nişan mesafe", () => rifleAimDistance, v => rifleAimDistance = v, 0.3f, 5f, 0.05f, this);
            TuningPanel.Register(P, "Tüfek nişan açı", () => rifleAimFov, v => rifleAimFov = v, 20f, 90f, 1f, this);
            TuningPanel.Register(P, "Tabanca nişan mesafe", () => pistolAimDistance, v => pistolAimDistance = v, 0.3f, 5f, 0.05f, this);
            TuningPanel.Register(P, "Tabanca nişan açı", () => pistolAimFov, v => pistolAimFov = v, 20f, 90f, 1f, this);
            TuningPanel.Register(P, "Nişan yumuşatma (sn)", () => aimDamping, v => aimDamping = v, 0f, 0.5f, 0.02f, this);
            TuningPanel.Register(P, "Çömelme omuz düşüşü", () => crouchShoulderDrop, v => crouchShoulderDrop = v, 0f, 1f, 0.05f, this);
            TuningPanel.Register(P, "Geçiş hızı (karakter)", () => blendSpeed, v => blendSpeed = v, 1f, 30f, 0.5f, this);
        }

        private bool FindCamera()
        {
            if (_follow != null) return true;
            if (Time.unscaledTime < _nextSearch) return false;
            _nextSearch = Time.unscaledTime + 1f;
            GameObject go = GameObject.Find("PlayerFollowCamera");
            if (go == null) return false;
            _camera = go.GetComponent<CinemachineCamera>();
            _follow = go.GetComponent<CinemachineThirdPersonFollow>();
            if (_camera == null || _follow == null) { _follow = null; return false; }
            _sceneShoulderY = _follow.ShoulderOffset.y;
            // Start from the new base at once (no slow blend from the old scene values)
            _follow.CameraDistance = baseDistance;
            _camera.Lens.FieldOfView = baseFov;
            return true;
        }

        private void Update()
        {
            if (_shooter == null || !FindCamera()) return;

            WeaponController weapon = _shooter.CurrentWeapon;
            WeaponType type = weapon != null && !_shooter.IsHolstered ? weapon.Type : WeaponType.Unarmed;
            bool aiming = _shooter.IsAiming && (type == WeaponType.Rifle || type == WeaponType.Pistol);
            bool pistol = type == WeaponType.Pistol;

            float distance = baseDistance, fov = baseFov;
            float sy = shoulderY >= 0f ? shoulderY : _sceneShoulderY;
            float damp = damping;
            if (aiming)
            {
                distance = pistol ? pistolAimDistance : rifleAimDistance;
                fov = pistol ? pistolAimFov : rifleAimFov;
                damp = aimDamping;
            }
            else if (_motor != null && _motor.IsSprinting) fov += sprintExtraFov;

            PlayerStealthController stealth = PlayerStealthController.Instance;
            if (stealth != null && stealth.IsCrouching) sy -= crouchShoulderDrop;

            float t = 1f - Mathf.Exp(-blendSpeed * Time.deltaTime);
            _follow.CameraDistance = Mathf.Lerp(_follow.CameraDistance, distance, t);
            _camera.Lens.FieldOfView = Mathf.Lerp(_camera.Lens.FieldOfView, fov, t);
            Vector3 shoulder = _follow.ShoulderOffset;
            shoulder.x = Mathf.Lerp(shoulder.x, shoulderX, t);
            shoulder.y = Mathf.Lerp(shoulder.y, sy, t);
            _follow.ShoulderOffset = shoulder;
            _follow.VerticalArmLength = Mathf.Lerp(_follow.VerticalArmLength, verticalArm, t);
            _follow.Damping = new Vector3(damp, damp, damp);
        }
    }
}
