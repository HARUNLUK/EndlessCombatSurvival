using UnityEngine;

namespace EndlessCombat.Character
{
    /// <summary>
    /// The main character's body: third person movement on a CharacterController, written for this game
    /// (2026-11-13, replaces the Starter Assets ThirdPersonController).
    ///
    /// Two ways of moving:
    ///   free run   nothing aimed: the body turns to where it runs, relative to the camera (exploring, melee)
    ///   strafe     aiming, binoculars (IsAiming / FaceCamera): the body keeps facing where the camera looks and
    ///              steps forward, back and sideways. The animator gets the sideways and forward speed
    ///              separately (MoveX, Speed), so the legs really step sideways and backwards.
    /// Sprint only in free run. Jump, gravity and the ground check are here too.
    /// Other systems steer it through the public flags (ActionLocked, MoveSpeedMultiplier, ...).
    /// The camera follows CameraTarget; this script turns that target with the look input.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    [DisallowMultipleComponent]
    public class PlayerMotor : MonoBehaviour
    {
        [Header("Speeds (m/s)")]
        [Tooltip("Run speed at full stick")]
        public float MoveSpeed = 4.0f;
        public float SprintSpeed = 6.2f;
        [Tooltip("Speed while aiming (strafe)")]
        public float AimMoveSpeed = 2.4f;
        public float Acceleration = 24f;
        public float Deceleration = 30f;
        [Tooltip("Seconds to turn towards the run direction")]
        [Range(0f, 0.3f)] public float RotationSmoothTime = 0.1f;
        [Tooltip("Degrees per second the body turns to the camera while aiming")]
        public float AimTurnSpeed = 720f;

        [Header("Jump")]
        public float JumpHeight = 1.1f;
        public float Gravity = -18f;
        public float JumpTimeout = 0.35f;
        public float FallTimeout = 0.15f;

        [Header("Ground")]
        public bool Grounded = true;
        public float GroundedOffset = -0.14f;
        public float GroundedRadius = 0.28f;
        public LayerMask GroundLayers = ~0;

        [Header("Camera target (the camera follows and copies its rotation)")]
        public GameObject CinemachineCameraTarget;
        public float TopClamp = 70f;
        public float BottomClamp = -35f;
        public bool LockCameraPosition;

        // ---- written by other systems ----
        [HideInInspector] public bool IsAiming;
        [HideInInspector] public float AimYawOffset;
        /// <summary>Attack, dodge, hit reaction: the combat code moves the body, no walking, turning or jumping.</summary>
        [HideInInspector] public bool ActionLocked;
        /// <summary>Written every frame by the combat code (raised guard is slow).</summary>
        [HideInInspector] public float MoveSpeedMultiplier = 1f;
        /// <summary>Binoculars and similar (slow walk, no sprint).</summary>
        [HideInInspector] public float ExtraSpeedMultiplier = 1f;
        [HideInInspector] public float LookSensitivityMultiplier = 1f;
        /// <summary>Face the camera and strafe without aiming a weapon (binoculars).</summary>
        [HideInInspector] public bool FaceCamera;

        /// <summary>Horizontal velocity (m/s, world space).</summary>
        public Vector3 PlanarVelocity => _planar;
        public bool IsStrafing => IsAiming || FaceCamera;
        public bool IsSprinting { get; private set; }

        private CharacterController _controller;
        private PlayerInputState _input;
        private Animator _animator;
        private Transform _camera;
        private Vector3 _planar;
        private float _vertical;
        private float _turnVelocity;
        private float _jumpTimer, _fallTimer;
        private float _yaw, _pitch;
        private bool _hasMoveX;

        private static readonly int SpeedId = Animator.StringToHash("Speed");
        private static readonly int MoveXId = Animator.StringToHash("MoveX");
        private static readonly int MotionSpeedId = Animator.StringToHash("MotionSpeed");
        private static readonly int GroundedId = Animator.StringToHash("Grounded");
        private static readonly int JumpId = Animator.StringToHash("Jump");
        private static readonly int FreeFallId = Animator.StringToHash("FreeFall");

        private void Awake()
        {
            _controller = GetComponent<CharacterController>();
            _input = GetComponent<PlayerInputState>();
            if (_input == null) _input = gameObject.AddComponent<PlayerInputState>();
            _animator = GetComponent<Animator>();

            const string P = "Karakter";
            EndlessCombat.Tuning.TuningPanel.Register(P, "Koşu hızı (m/sn)", () => MoveSpeed, v => MoveSpeed = v, 1f, 10f, 0.1f, this);
            EndlessCombat.Tuning.TuningPanel.Register(P, "Depar hızı (m/sn)", () => SprintSpeed, v => SprintSpeed = v, 2f, 14f, 0.1f, this);
            EndlessCombat.Tuning.TuningPanel.Register(P, "Nişanda hız (m/sn)", () => AimMoveSpeed, v => AimMoveSpeed = v, 0.5f, 6f, 0.1f, this);
            EndlessCombat.Tuning.TuningPanel.Register(P, "İvme", () => Acceleration, v => Acceleration = v, 2f, 80f, 1f, this);
            EndlessCombat.Tuning.TuningPanel.Register(P, "Yavaşlama", () => Deceleration, v => Deceleration = v, 2f, 80f, 1f, this);
            EndlessCombat.Tuning.TuningPanel.Register(P, "Dönüş yumuşatma (sn)", () => RotationSmoothTime, v => RotationSmoothTime = v, 0f, 0.3f, 0.01f, this);
            EndlessCombat.Tuning.TuningPanel.Register(P, "Nişanda dönüş (derece/sn)", () => AimTurnSpeed, v => AimTurnSpeed = v, 90f, 2000f, 10f, this);
            EndlessCombat.Tuning.TuningPanel.Register(P, "Zıplama yüksekliği", () => JumpHeight, v => JumpHeight = v, 0.2f, 3f, 0.05f, this);
            EndlessCombat.Tuning.TuningPanel.RegisterReadout(P, "Hız (m/sn)", () => _planar.magnitude.ToString("0.00"), this);
        }

        /// <summary>
        /// Turns the camera target to a heading (degrees) with a level pitch, e.g. when getting out of the vehicle the
        /// camera looks where the vehicle looked instead of where it was before getting in.
        /// </summary>
        public void SetLookYaw(float yaw, float pitch = 8f)
        {
            _yaw = Mathf.Repeat(yaw + 180f, 360f) - 180f;
            _pitch = Mathf.Clamp(pitch, BottomClamp, TopClamp);
            if (CinemachineCameraTarget != null)
                CinemachineCameraTarget.transform.rotation = Quaternion.Euler(_pitch, _yaw, 0f);
        }

        public float LookYaw => _yaw;

        private void Start()
        {
            if (Camera.main != null) _camera = Camera.main.transform;
            if (CinemachineCameraTarget != null)
            {
                Vector3 e = CinemachineCameraTarget.transform.rotation.eulerAngles;
                _yaw = e.y;
                _pitch = e.x > 180f ? e.x - 360f : e.x;
            }
            if (_animator != null)
                foreach (var p in _animator.parameters)
                    if (p.nameHash == MoveXId) _hasMoveX = true;
            _jumpTimer = JumpTimeout;
            _fallTimer = FallTimeout;
        }

        private void Update()
        {
            if (_camera == null && Camera.main != null) _camera = Camera.main.transform;
            float dt = Time.deltaTime;
            GroundCheck();
            JumpAndGravity(dt);
            Move(dt);
        }

        private void LateUpdate()
        {
            if (CinemachineCameraTarget == null) return;
            if (!LockCameraPosition && _input.look.sqrMagnitude > 0.000001f)
            {
                _yaw += _input.look.x * LookSensitivityMultiplier;
                _pitch += _input.look.y * LookSensitivityMultiplier;
            }
            _yaw = Mathf.Repeat(_yaw + 180f, 360f) - 180f;
            _pitch = Mathf.Clamp(_pitch, BottomClamp, TopClamp);
            CinemachineCameraTarget.transform.rotation = Quaternion.Euler(_pitch, _yaw, 0f);
        }

        private void GroundCheck()
        {
            Vector3 p = transform.position;
            p.y -= GroundedOffset;
            Grounded = Physics.CheckSphere(p, GroundedRadius, GroundLayers, QueryTriggerInteraction.Ignore);
            if (_animator != null) _animator.SetBoolSafe(GroundedId, Grounded);
        }

        private void JumpAndGravity(float dt)
        {
            if (Grounded)
            {
                _fallTimer = FallTimeout;
                // Jump stays on while still rising: the ground check can still hit for a frame or two after launch,
                // and the Animator leaves the jump state as soon as Grounded && !Jump.
                if (_animator != null)
                {
                    if (_vertical <= 0f) _animator.SetBoolSafe(JumpId, false);
                    _animator.SetBoolSafe(FreeFallId, false);
                }
                if (_vertical < 0f) _vertical = -2f;
                if (ActionLocked) _input.jump = false;
                if (_input.jump && _jumpTimer <= 0f)
                {
                    _vertical = Mathf.Sqrt(JumpHeight * -2f * Gravity);
                    if (_animator != null) _animator.SetBoolSafe(JumpId, true);
                }
                if (_jumpTimer >= 0f) _jumpTimer -= dt;
            }
            else
            {
                _jumpTimer = JumpTimeout;
                if (_fallTimer >= 0f) _fallTimer -= dt;
                else if (_animator != null) _animator.SetBoolSafe(FreeFallId, true);
            }
            _input.jump = false;
            if (_vertical > -53f) _vertical += Gravity * dt;
        }

        private void Move(float dt)
        {
            if (ActionLocked)
            {
                // The combat code moves and turns the body; only gravity here
                _planar = Vector3.zero;
                IsSprinting = false;
                _controller.Move(new Vector3(0f, _vertical, 0f) * dt);
                SetAnimator(0f, 0f, dt);
                return;
            }

            float camYaw = _camera != null ? _camera.eulerAngles.y : transform.eulerAngles.y;
            Vector2 stick = _input.move;
            float tilt = _input.analogMovement ? Mathf.Clamp01(stick.magnitude) : (stick == Vector2.zero ? 0f : 1f);
            bool strafe = IsStrafing;
            float slow = Mathf.Clamp(MoveSpeedMultiplier, 0.05f, 1f) * Mathf.Clamp(ExtraSpeedMultiplier, 0.05f, 1f);
            IsSprinting = !strafe && _input.sprint && tilt > 0.5f && slow >= 0.99f;
            float top = (strafe ? Mathf.Min(AimMoveSpeed, MoveSpeed) : IsSprinting ? SprintSpeed : MoveSpeed) * slow;

            Vector3 wish = Vector3.zero;
            if (tilt > 0.01f)
                wish = Quaternion.Euler(0f, camYaw, 0f) * new Vector3(stick.x, 0f, stick.y).normalized * (top * (IsSprinting ? 1f : tilt));

            float rate = wish.sqrMagnitude >= _planar.sqrMagnitude ? Acceleration : Deceleration;
            _planar = Vector3.MoveTowards(_planar, wish, rate * dt);

            if (strafe)
            {
                Quaternion face = Quaternion.Euler(0f, camYaw, 0f);
                transform.rotation = Quaternion.RotateTowards(transform.rotation, face, AimTurnSpeed * dt);
            }
            else if (wish.sqrMagnitude > 0.01f)
            {
                float target = Mathf.Atan2(wish.x, wish.z) * Mathf.Rad2Deg;
                float yaw = Mathf.SmoothDampAngle(transform.eulerAngles.y, target, ref _turnVelocity, RotationSmoothTime);
                transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            }

            _controller.Move((_planar + new Vector3(0f, _vertical, 0f)) * dt);

            if (strafe)
            {
                Vector3 local = transform.InverseTransformDirection(_planar);
                SetAnimator(local.z, local.x, dt);
            }
            else SetAnimator(_planar.magnitude, 0f, dt);
        }

        // Animation events of the older clips (crouch, landing): no sound in this game, the receivers only keep
        // Unity from logging "no receiver".
        private void OnFootstep(AnimationEvent animationEvent) { }
        private void OnLand(AnimationEvent animationEvent) { }

        private void SetAnimator(float forward, float sideways, float dt)
        {
            if (_animator == null) return;
            _animator.SetFloatSafe(SpeedId, forward, 0.08f, dt);
            if (_hasMoveX) _animator.SetFloatSafe(MoveXId, sideways, 0.08f, dt);
            _animator.SetFloatSafe(MotionSpeedId, 1f);
        }
    }
}
