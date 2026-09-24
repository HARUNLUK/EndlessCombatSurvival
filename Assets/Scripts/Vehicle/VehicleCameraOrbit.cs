using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace EndlessSurvival.Vehicle
{
    /// <summary>
    /// Controls VehicleCameraTarget orientation with smooth rotational chase lag,
    /// speed-sensitive dynamics, and mouse orbit with smooth auto-recentering.
    /// </summary>
    public class VehicleCameraOrbit : MonoBehaviour
    {
        [Header("Follow Dynamics (Yumuşak Takip)")]
        [Tooltip("Smooth follow lag speed when the vehicle turns (lower = more cinematic lag, higher = tighter follow)")]
        [Range(1f, 15f)]
        public float followRotationSpeed = 4.5f;

        [Header("Sensitivity Settings")]
        [Tooltip("Horizontal mouse look sensitivity")]
        public float sensitivityX = 1.8f;

        [Tooltip("Vertical mouse look sensitivity")]
        public float sensitivityY = 1.4f;

        [Header("Pitch Clamping")]
        [Tooltip("Default pitch angle behind vehicle")]
        public float defaultPitch = 10f;

        [Tooltip("Minimum downward pitch angle")]
        public float minPitch = -15f;

        [Tooltip("Maximum upward pitch angle")]
        public float maxPitch = 50f;

        [Header("Recentering Dynamics")]
        [Tooltip("Automatically recenter camera behind vehicle after idle time")]
        public bool autoRecenter = true;

        [Tooltip("Time in seconds without mouse input before auto recentering begins")]
        public float recenterDelay = 1.5f;

        [Tooltip("Speed of recentering pitch to default")]
        public float recenterSpeed = 3.0f;

        [Header("References")]
        [Tooltip("Parent vehicle controller (found automatically if left empty)")]
        public VehicleController parentVehicle;

        private float _currentWorldYaw = 0f;
        private float _mouseYawOffset = 0f;
        private float _pitch = 10f;
        private float _lastInputTime = 0f;
        private bool _hasInitialized = false;

        private void Awake()
        {
            if (parentVehicle == null)
            {
                parentVehicle = GetComponentInParent<VehicleController>();
            }
        }

        private void Start()
        {
            InitializeYaw();
        }

        private void OnEnable()
        {
            InitializeYaw();
        }

        private void InitializeYaw()
        {
            if (parentVehicle != null)
            {
                _currentWorldYaw = parentVehicle.transform.eulerAngles.y;
            }
            else
            {
                _currentWorldYaw = transform.eulerAngles.y;
            }
            _mouseYawOffset = 0f;
            _pitch = defaultPitch;
            _lastInputTime = -100f; // Force immediate follow until user touches mouse
            _hasInitialized = true;
        }

        private void LateUpdate()
        {
            if (parentVehicle == null || !parentVehicle.isDriven)
            {
                return;
            }

            if (!_hasInitialized)
            {
                InitializeYaw();
            }

            HandleMouseInput();
            ApplyRotation();
        }

        private void HandleMouseInput()
        {
            float mouseDeltaX = 0f;
            float mouseDeltaY = 0f;

#if ENABLE_INPUT_SYSTEM
            var mouse = Mouse.current;
            if (mouse != null)
            {
                Vector2 delta = mouse.delta.ReadValue();
                mouseDeltaX = delta.x * sensitivityX * 0.1f;
                mouseDeltaY = delta.y * sensitivityY * 0.1f;
            }
#else
            mouseDeltaX = Input.GetAxis("Mouse X") * sensitivityX;
            mouseDeltaY = Input.GetAxis("Mouse Y") * sensitivityY;
#endif

            if (Mathf.Abs(mouseDeltaX) > 0.01f || Mathf.Abs(mouseDeltaY) > 0.01f)
            {
                _mouseYawOffset += mouseDeltaX;
                _pitch = Mathf.Clamp(_pitch - mouseDeltaY, minPitch, maxPitch);
                _lastInputTime = Time.time;
            }
            else if (autoRecenter && (Time.time - _lastInputTime > recenterDelay))
            {
                // Smoothly decay mouse offset back to zero so camera returns to vehicle forward axis
                _mouseYawOffset = Mathf.Lerp(_mouseYawOffset, 0f, Time.deltaTime * recenterSpeed);
                _pitch = Mathf.Lerp(_pitch, defaultPitch, Time.deltaTime * recenterSpeed);
            }
        }

        private void ApplyRotation()
        {
            float vehicleWorldYaw = parentVehicle.transform.eulerAngles.y;

            // Target world yaw combines vehicle forward heading with user's mouse look offset
            float targetWorldYaw = vehicleWorldYaw + _mouseYawOffset;

            // Smoothly interpolate current world yaw towards target world yaw for silky-smooth turning follow
            _currentWorldYaw = Mathf.LerpAngle(_currentWorldYaw, targetWorldYaw, Time.deltaTime * followRotationSpeed);

            // Apply world rotation to target
            transform.rotation = Quaternion.Euler(_pitch, _currentWorldYaw, 0f);
        }
    }
}
