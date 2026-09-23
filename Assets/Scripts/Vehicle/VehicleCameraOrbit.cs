using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace EndlessSurvival.Vehicle
{
    /// <summary>
    /// Controls the rotation of VehicleCameraTarget using mouse input so Cinemachine
    /// can orbit smoothly around the vehicle, just like the TPS player camera.
    /// Includes optional auto-recentering behind the vehicle.
    /// </summary>
    public class VehicleCameraOrbit : MonoBehaviour
    {
        [Header("Sensitivity Settings")]
        [Tooltip("Horizontal mouse look sensitivity")]
        public float sensitivityX = 1.8f;

        [Tooltip("Vertical mouse look sensitivity")]
        public float sensitivityY = 1.4f;

        [Header("Pitch Clamping")]
        [Tooltip("Minimum downward pitch angle")]
        public float minPitch = -15f;

        [Tooltip("Maximum upward pitch angle")]
        public float maxPitch = 50f;

        [Header("Recentering Dynamics")]
        [Tooltip("Automatically recenter camera behind vehicle after idle time")]
        public bool autoRecenter = true;

        [Tooltip("Time in seconds without mouse input before auto recentering begins")]
        public float recenterDelay = 1.8f;

        [Tooltip("Speed of recentering")]
        public float recenterSpeed = 3.5f;

        [Header("References")]
        [Tooltip("Parent vehicle controller (found automatically if left empty)")]
        public VehicleController parentVehicle;

        private float _yawOffset = 0f;
        private float _pitch = 10f;
        private float _lastInputTime = 0f;

        private void Awake()
        {
            if (parentVehicle == null)
            {
                parentVehicle = GetComponentInParent<VehicleController>();
            }
        }

        private void OnEnable()
        {
            _yawOffset = 0f;
            _pitch = 10f;
            _lastInputTime = Time.time;
        }

        private void LateUpdate()
        {
            // Only orbit when vehicle is actively driven
            if (parentVehicle != null && !parentVehicle.isDriven)
            {
                return;
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
                _yawOffset += mouseDeltaX;
                _pitch = Mathf.Clamp(_pitch - mouseDeltaY, minPitch, maxPitch);
                _lastInputTime = Time.time;
            }
            else if (autoRecenter && (Time.time - _lastInputTime > recenterDelay))
            {
                // Smoothly recenter yaw behind the vehicle
                _yawOffset = Mathf.Lerp(_yawOffset, 0f, Time.deltaTime * recenterSpeed);
            }
        }

        private void ApplyRotation()
        {
            transform.localRotation = Quaternion.Euler(_pitch, _yawOffset, 0f);
        }
    }
}
