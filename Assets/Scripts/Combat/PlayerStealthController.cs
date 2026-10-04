using UnityEngine;
using StarterAssets;
using EndlessCombat.AI;
using EndlessSurvival.Camping;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace EndlessCombat.Combat
{
    /// <summary>
    /// Oyuncunun gizlilik, comelme (crouch), gorunurluk profili ve cikarilan gurultuyu yonetir.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class PlayerStealthController : MonoBehaviour
    {
        public static PlayerStealthController Instance { get; private set; }

        [Header("Movement & Crouch Settings")]
        [Tooltip("Comelme tusuna basildiginda acilip kapanma (toggle) mi olsun yoksa basili tutma (hold) mi?")]
        public bool toggleCrouch = true;

        [Tooltip("Comelirken hareket hizi")]
        public float crouchSpeed = 2.0f;

        [Tooltip("Normal ayakta durma hizi (ThirdPersonController'dan okunur veya ezilir)")]
        public float walkSpeed = 4.0f;
        public float sprintSpeed = 7.0f;

        [Header("Character Controller Heights")]
        public float standingHeight = 1.8f;
        public Vector3 standingCenter = new Vector3(0, 0.93f, 0);

        public float crouchHeight = 1.15f;
        public Vector3 crouchCenter = new Vector3(0, 0.6f, 0);

        [Header("Stealth & Noise Profiles")]
        [Tooltip("Comelirken dusmanlarin gorus mesafesi carpani (0.45 = %55 daha zor fark edilir)")]
        public float crouchVisibilityMultiplier = 0.45f;

        [Tooltip("Comelerek yururken ayak sesi gurultu yaricapi (metre)")]
        public float crouchNoiseRadius = 2.0f;

        [Tooltip("Normal yururken ayak sesi gurultu yaricapi (metre)")]
        public float walkNoiseRadius = 7.0f;

        [Tooltip("Kosarken ayak sesi gurultu yaricapi (metre)")]
        public float sprintNoiseRadius = 16.0f;

        [Tooltip("Ates ederken cikan silah sesi gurultu yaricapi (metre)")]
        public float gunshotNoiseRadius = 38.0f;

        // State
        private bool _isCrouching = false;
        public bool IsCrouching => _isCrouching;

        private CharacterController _characterController;
        private Animator _animator;
        private ThirdPersonController _thirdPersonController;
        private StarterAssetsInputs _inputs;

        private float _defaultSpeed;
        private float _defaultSprintSpeed;

        private void Awake()
        {
            Instance = this;
            _characterController = GetComponent<CharacterController>();
            _animator = GetComponentInChildren<Animator>();
            _thirdPersonController = GetComponent<ThirdPersonController>();
            _inputs = GetComponent<StarterAssetsInputs>();

            if (_thirdPersonController != null)
            {
                _defaultSpeed = _thirdPersonController.MoveSpeed;
                _defaultSprintSpeed = _thirdPersonController.SprintSpeed;
            }
        }

        private void Update()
        {
            HandleCrouchInput();
            UpdateAnimation();
        }

        private void HandleCrouchInput()
        {
            // UI acikken veya aractayken comelme engellenir
            if (CampfireUI.IsOpen || !gameObject.activeInHierarchy) return;

            bool crouchPressed = false;
            bool crouchHeld = false;

            // Eğer kampta oturuyorsa (IsSitting) veya kamp UI açıkken çömelme tetiklenmez
            var sittingCtrl = GetComponent<PlayerSittingController>();
            if (sittingCtrl != null && sittingCtrl.IsSitting) return;

#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current;
            if (kb != null)
            {
                crouchPressed = kb.cKey.wasPressedThisFrame || kb.leftCtrlKey.wasPressedThisFrame;
                crouchHeld = kb.cKey.isPressed || kb.leftCtrlKey.isPressed;
            }
#else
            crouchPressed = Input.GetKeyDown(KeyCode.C) || Input.GetKeyDown(KeyCode.LeftControl);
            crouchHeld = Input.GetKey(KeyCode.C) || Input.GetKey(KeyCode.LeftControl);
#endif

            if (toggleCrouch)
            {
                if (crouchPressed)
                {
                    SetCrouching(!_isCrouching);
                }
            }
            else
            {
                if (crouchHeld && !_isCrouching) SetCrouching(true);
                else if (!crouchHeld && _isCrouching) SetCrouching(false);
            }

            // Kosmaya baslarsa otomatik comelmeden cikar
            if (_isCrouching && _inputs != null && _inputs.sprint)
            {
                SetCrouching(false);
            }
        }

        public void SetCrouching(bool crouch)
        {
            if (_isCrouching == crouch) return;

            // Ayaga kalkarken yukari carpisma kontrolu (ornek: alcak masanin/tavanin altindaysa kalkamaz)
            if (!crouch && HasObstacleAbove())
            {
                return;
            }

            _isCrouching = crouch;

            if (_characterController != null)
            {
                float targetHeight = _isCrouching ? crouchHeight : standingHeight;
                _characterController.height = targetHeight;
                _characterController.center = new Vector3(0, targetHeight * 0.5f, 0);
            }

            if (_thirdPersonController != null)
            {
                _thirdPersonController.MoveSpeed = _isCrouching ? crouchSpeed : _defaultSpeed;
                _thirdPersonController.SprintSpeed = _isCrouching ? crouchSpeed : _defaultSprintSpeed;
            }
        }

        private bool HasObstacleAbove()
        {
            Vector3 start = transform.position + Vector3.up * crouchHeight;
            float checkDist = standingHeight - crouchHeight;
            return Physics.SphereCast(start, 0.3f, Vector3.up, out _, checkDist, LayerMask.GetMask("Default", "Environment"));
        }

        private void UpdateAnimation()
        {
            if (_animator == null) _animator = GetComponentInChildren<Animator>();
            if (_animator != null)
            {
                _animator.SetBool("IsCrouching", _isCrouching);
            }
        }

        /// <summary>
        /// Karakterin su anki eylemine gore yaydigi ses dalgasi menzili (metre).
        /// </summary>
        public float GetCurrentNoiseRadius()
        {
            if (_inputs == null) return 0f;

            bool isMoving = _inputs.move != Vector2.zero;
            if (!isMoving) return 0f;

            if (_isCrouching) return crouchNoiseRadius;
            if (_inputs.sprint) return sprintNoiseRadius;
            return walkNoiseRadius;
        }

        /// <summary>
        /// Silah ateslendiginde veya bomba patladiginda etraftaki tum dusmanlari uyaran ses patlamasi.
        /// </summary>
        public void EmitGunshotNoise(Vector3 origin)
        {
            AlertNearbyEnemies(origin, gunshotNoiseRadius);
        }

        public void AlertNearbyEnemies(Vector3 soundOrigin, float radius)
        {
            Collider[] hits = Physics.OverlapSphere(soundOrigin, radius);
            foreach (var hit in hits)
            {
                var enemy = hit.GetComponent<EnemyController>();
                if (enemy != null && !enemy.IsDead)
                {
                    enemy.HearSound(soundOrigin, radius);
                }
            }
        }
    }
}
