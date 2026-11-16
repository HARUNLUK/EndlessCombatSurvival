using Unity.Cinemachine;
using UnityEngine;
using EndlessCombat.Tuning;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace EndlessSurvival.Vehicle
{
    /// <summary>
    /// Turns VehicleCameraTarget (the vehicle camera follows it) and shapes the vehicle camera.
    /// Core_Feel_Plan stage 1 (2026-11-14):
    ///   - base FOV / distance / damping applied from here (scene: 40 degrees, double lag), on the tuning panel
    ///   - turn follow 4.5 > 8 per second, light Cinemachine damping: the steering shows at once
    ///   - camera centred behind the vehicle (no shoulder offset)
    ///   - own obstacle check (sphere cast that skips the vehicle's own colliders; Cinemachine's avoidance would hit
    ///     the vehicle, everything is on the Default layer): the camera does not go into terrain, trees or walls
    ///   - speed feel without sound: wider and further back with speed, a small shake at high speed, a short FOV
    ///     squeeze on hard braking
    ///   - backing up turns the camera to look behind (delay on the panel)
    /// Right stick / mouse orbit with auto recentering as before.
    /// </summary>
    public class VehicleCameraOrbit : MonoBehaviour
    {
        [Header("Follow")]
        [Tooltip("How fast the camera follows the vehicle's heading (1 / s)")]
        [Range(1f, 30f)]
        public float followRotationSpeed = 8f;

        [Header("Base camera (applied to the Cinemachine camera)")]
        public float baseFov = 60f;
        public float baseDistance = 6f;
        [Tooltip("Cinemachine damping x / y / z (s)")]
        public Vector3 damping = new Vector3(0.1f, 0.1f, 0.15f);
        public float shoulderX = 0f;

        [Header("Sensitivity Settings")]
        public float sensitivityX = 1.8f;
        public float sensitivityY = 1.4f;
        [Tooltip("Gamepad right stick (degrees per second, yaw / pitch)")]
        public Vector2 padLookSpeed = new Vector2(160f, 100f);

        [Header("Pitch Clamping")]
        public float defaultPitch = 10f;
        public float minPitch = -15f;
        public float maxPitch = 50f;

        [Header("Recentering Dynamics")]
        public bool autoRecenter = true;
        public float recenterDelay = 1.5f;
        public float recenterSpeed = 3.0f;

        [Header("Speed")]
        public float speedExtraDistance = 1.5f;
        public float speedExtraFov = 12f;
        [Tooltip("Speed at which the full extra distance / FOV is reached (km/h)")]
        public float fullEffectSpeedKmh = 110f;
        [Tooltip("Camera shake at full effect speed (degrees); 0 = off")]
        public float speedShake = 0.35f;
        [Tooltip("Speed where the shake starts (km/h)")]
        public float shakeStartKmh = 60f;
        [Tooltip("FOV squeeze on hard braking (degrees)")]
        public float brakeFovPunch = 4f;
        [Tooltip("Deceleration that counts as hard braking (m/s2)")]
        public float hardBrakeDecel = 7f;

        [Header("Obstacles")]
        public bool avoidObstacles = true;
        public float obstacleRadius = 0.25f;
        public float minDistance = 1.6f;

        [Header("Reverse")]
        public bool lookBackWhenReversing = true;
        public float reverseLookDelay = 0.7f;
        public float reverseLookSpeedKmh = 6f;

        [Header("References")]
        public VehicleController parentVehicle;

        private float _currentWorldYaw;
        private float _mouseYawOffset;
        private float _pitch = 10f;
        private float _lastInputTime;
        private bool _hasInitialized;
        private float _reverseTime;
        private float _reverseYaw;
        private CinemachineCamera _camera;
        private CinemachineThirdPersonFollow _follow;
        private float _nextCameraSearch;
        private float _obstacleDistance = float.MaxValue;
        private float _lastSpeed;
        private float _brakePunch;
        private float _smoothedDistance, _smoothedFov;
        private static readonly RaycastHit[] Hits = new RaycastHit[16];

        private void Awake()
        {
            if (parentVehicle == null) parentVehicle = GetComponentInParent<VehicleController>();
            // The scene still holds the old 4.5: move it to the stage 1 base (an inspector change is kept)
            if (Mathf.Approximately(followRotationSpeed, 4.5f)) followRotationSpeed = 8f;
            RegisterTuning();
        }

        /// <summary>Panel rows drive this vehicle. Called again when the player gets in, so with several vehicles
        /// the rows follow the driven one.</summary>
        private void RegisterTuning()
        {
            const string P = "Kamera";
            TuningPanel.Register(P, "Araç: görüş açısı", () => baseFov, v => baseFov = v, 30f, 100f, 1f, this);
            TuningPanel.Register(P, "Araç: mesafe (m)", () => baseDistance, v => baseDistance = v, 2f, 14f, 0.1f, this);
            TuningPanel.Register(P, "Araç: dönüş takibi", () => followRotationSpeed, v => followRotationSpeed = v, 1f, 30f, 0.5f, this);
            TuningPanel.Register(P, "Araç: yumuşatma yatay", () => damping.x, v => damping.x = v, 0f, 1f, 0.02f, this);
            TuningPanel.Register(P, "Araç: yumuşatma dikey", () => damping.y, v => damping.y = v, 0f, 1f, 0.02f, this);
            TuningPanel.Register(P, "Araç: yumuşatma mesafe", () => damping.z, v => damping.z = v, 0f, 1f, 0.02f, this);
            TuningPanel.Register(P, "Araç: hızla ek mesafe", () => speedExtraDistance, v => speedExtraDistance = v, 0f, 6f, 0.1f, this);
            TuningPanel.Register(P, "Araç: hızla ek açı", () => speedExtraFov, v => speedExtraFov = v, 0f, 30f, 1f, this);
            TuningPanel.Register(P, "Araç: titreme", () => speedShake, v => speedShake = v, 0f, 2f, 0.05f, this);
            TuningPanel.Register(P, "Araç: fren açı daralması", () => brakeFovPunch, v => brakeFovPunch = v, 0f, 15f, 0.5f, this);
            TuningPanel.Register(P, "Araç: geri bakma gecikmesi", () => reverseLookDelay, v => reverseLookDelay = v, 0f, 3f, 0.1f, this);
            TuningPanel.RegisterToggle(P, "Araç: geri viteste arkaya bak", () => lookBackWhenReversing, v => lookBackWhenReversing = v, this);
            TuningPanel.RegisterToggle(P, "Araç: engelden kaçınma", () => avoidObstacles, v => avoidObstacles = v, this);
            TuningPanel.Register(P, "Araç: bakış hızı", () => padLookSpeed.x, v => padLookSpeed = new Vector2(v, v * 0.62f), 40f, 400f, 5f, this);
        }

        private void Start() => InitializeYaw(float.NaN);

        private void OnEnable() => InitializeYaw(float.NaN);

        /// <summary>Called when the player gets in: start from the player camera's heading (no jump), then follow.</summary>
        public void ResetView(float startYaw)
        {
            RegisterTuning();
            InitializeYaw(startYaw);
            _obstacleDistance = float.MaxValue;
            _lastSpeed = parentVehicle != null ? parentVehicle.CurrentSpeedKmh : 0f;
            _brakePunch = 0f;
            if (FindCamera())
            {
                _smoothedDistance = baseDistance;
                _smoothedFov = baseFov;
                _follow.CameraDistance = baseDistance;
                _camera.Lens.FieldOfView = baseFov;
            }
        }

        /// <summary>The heading the camera looks along (degrees).</summary>
        public float CurrentYaw => _currentWorldYaw;

        private void InitializeYaw(float startYaw)
        {
            float vehicleYaw = parentVehicle != null ? parentVehicle.transform.eulerAngles.y : transform.eulerAngles.y;
            _currentWorldYaw = float.IsNaN(startYaw) ? vehicleYaw : startYaw;
            _mouseYawOffset = 0f;
            _pitch = defaultPitch;
            _lastInputTime = -100f;
            _reverseTime = 0f;
            _reverseYaw = 0f;
            _hasInitialized = true;
        }

        private void LateUpdate()
        {
            if (parentVehicle == null || !parentVehicle.isDriven) return;
            if (!_hasInitialized) InitializeYaw(float.NaN);

            HandleLookInput();
            UpdateReverseLook();
            ApplyRotation();
            ApplyCamera();
        }

        private void UpdateReverseLook()
        {
            bool reversing = lookBackWhenReversing && parentVehicle.ForwardSpeed * 3.6f < -reverseLookSpeedKmh;
            _reverseTime = reversing ? _reverseTime + Time.deltaTime : 0f;
            bool manual = Time.time - _lastInputTime < recenterDelay;
            float target = !manual && _reverseTime > reverseLookDelay ? 180f : 0f;
            _reverseYaw = Mathf.MoveTowards(_reverseYaw, target, Time.deltaTime * 240f);
        }

        private bool FindCamera()
        {
            if (_follow != null) return true;
            if (Time.unscaledTime < _nextCameraSearch) return false;
            _nextCameraSearch = Time.unscaledTime + 1f;
            foreach (var cam in FindObjectsByType<CinemachineCamera>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (cam.Follow != transform) continue;
                _camera = cam;
                _follow = cam.GetComponent<CinemachineThirdPersonFollow>();
                break;
            }
            if (_follow == null) return false;
            // Cinemachine's own avoidance would hit the vehicle itself (one layer): the check is done here
            var avoid = _follow.AvoidObstacles;
            avoid.Enabled = false;
            _follow.AvoidObstacles = avoid;
            _smoothedDistance = baseDistance;
            _smoothedFov = baseFov;
            return true;
        }

        /// <summary>Base values, speed feel, braking squeeze and the obstacle check.</summary>
        private void ApplyCamera()
        {
            if (!FindCamera()) return;
            float dt = Mathf.Max(Time.deltaTime, 0.0001f);
            float speed = parentVehicle.CurrentSpeedKmh;

            // Hard braking: a short FOV squeeze
            float decel = (_lastSpeed - speed) / 3.6f / dt;
            _lastSpeed = speed;
            if (decel > hardBrakeDecel && speed > 20f) _brakePunch = Mathf.Max(_brakePunch, brakeFovPunch);
            _brakePunch = Mathf.MoveTowards(_brakePunch, 0f, brakeFovPunch * 1.5f * dt);

            float k = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(15f, fullEffectSpeedKmh, speed));
            float t = 1f - Mathf.Exp(-3f * dt);
            _smoothedDistance = Mathf.Lerp(_smoothedDistance, baseDistance + speedExtraDistance * k, t);
            _smoothedFov = Mathf.Lerp(_smoothedFov, baseFov + speedExtraFov * k, t);

            float distance = _smoothedDistance;
            if (avoidObstacles)
            {
                float free = FreeDistance(distance);
                // Into an obstacle at once, back out smoothly
                _obstacleDistance = free < _obstacleDistance ? free : Mathf.Lerp(_obstacleDistance, free, 1f - Mathf.Exp(-4f * dt));
                distance = Mathf.Min(distance, Mathf.Max(minDistance, _obstacleDistance));
            }

            _follow.CameraDistance = distance;
            _camera.Lens.FieldOfView = _smoothedFov - _brakePunch;
            _follow.Damping = damping;
            Vector3 shoulder = _follow.ShoulderOffset;
            shoulder.x = shoulderX;
            _follow.ShoulderOffset = shoulder;
        }

        /// <summary>How far back the camera can go before something solid (not the vehicle, the player or a trigger).</summary>
        private float FreeDistance(float wanted)
        {
            Vector3 shoulder = _follow.ShoulderOffset;
            Vector3 pivot = transform.position + transform.rotation * new Vector3(shoulder.x, shoulder.y + _follow.VerticalArmLength, shoulder.z);
            Vector3 back = -transform.forward;
            int n = Physics.SphereCastNonAlloc(pivot, obstacleRadius, back, Hits, wanted + obstacleRadius, ~0, QueryTriggerInteraction.Ignore);
            float best = wanted;
            Transform vehicle = parentVehicle.transform;
            for (int i = 0; i < n; i++)
            {
                var h = Hits[i];
                if (h.collider == null) continue;
                if (h.collider.transform.IsChildOf(vehicle)) continue;
                if (h.collider.attachedRigidbody != null && !h.collider.attachedRigidbody.isKinematic && h.collider.attachedRigidbody.mass < 200f) continue; // loose small objects
                if (h.collider.CompareTag("Player")) continue;
                if (h.distance <= 0f) continue; // started inside: ignore (the pivot is in the cab)
                best = Mathf.Min(best, h.distance - 0.1f);
            }
            return Mathf.Max(0f, best);
        }

        private void HandleLookInput()
        {
            float dx = 0f, dy = 0f;
#if ENABLE_INPUT_SYSTEM
            var mouse = Mouse.current;
            if (mouse != null && Cursor.lockState == CursorLockMode.Locked)
            {
                Vector2 delta = mouse.delta.ReadValue();
                dx = delta.x * sensitivityX * 0.1f;
                dy = delta.y * sensitivityY * 0.1f;
            }
#else
            dx = Input.GetAxis("Mouse X") * sensitivityX;
            dy = Input.GetAxis("Mouse Y") * sensitivityY;
#endif
            Vector2 padLook = EndlessCombat.Combat.GameInput.PadRightStick;
            if (padLook.sqrMagnitude > 0.04f && !EndlessSurvival.Survival.SurvivalUIState.AnyOpen)
            {
                dx += padLook.x * padLookSpeed.x * Time.unscaledDeltaTime;
                dy += padLook.y * padLookSpeed.y * Time.unscaledDeltaTime;
            }

            if (Mathf.Abs(dx) > 0.01f || Mathf.Abs(dy) > 0.01f)
            {
                _mouseYawOffset += dx;
                _pitch = Mathf.Clamp(_pitch - dy, minPitch, maxPitch);
                _lastInputTime = Time.time;
            }
            else if (autoRecenter && Time.time - _lastInputTime > recenterDelay)
            {
                _mouseYawOffset = Mathf.Lerp(_mouseYawOffset, 0f, Time.deltaTime * recenterSpeed);
                _pitch = Mathf.Lerp(_pitch, defaultPitch, Time.deltaTime * recenterSpeed);
            }
        }

        private void ApplyRotation()
        {
            float targetWorldYaw = parentVehicle.transform.eulerAngles.y + _mouseYawOffset + _reverseYaw;
            _currentWorldYaw = Mathf.LerpAngle(_currentWorldYaw, targetWorldYaw, 1f - Mathf.Exp(-followRotationSpeed * Time.deltaTime));

            // High speed shake (visual speed cue; no sound in this game)
            float shakeK = Mathf.InverseLerp(shakeStartKmh, Mathf.Max(shakeStartKmh + 1f, fullEffectSpeedKmh + 20f), parentVehicle.CurrentSpeedKmh);
            float sx = 0f, sy = 0f;
            if (speedShake > 0f && shakeK > 0f)
            {
                float time = Time.time * 9f;
                sx = (Mathf.PerlinNoise(time, 0.3f) - 0.5f) * 2f * speedShake * shakeK;
                sy = (Mathf.PerlinNoise(0.7f, time) - 0.5f) * 2f * speedShake * shakeK;
            }
            transform.rotation = Quaternion.Euler(_pitch + sx, _currentWorldYaw + sy, 0f);
        }
    }
}
