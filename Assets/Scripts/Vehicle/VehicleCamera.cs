using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace EndlessSurvival.Vehicle
{
    /// <summary>
    /// Smooth chase camera controller for vehicles.
    /// Follows behind the vehicle smoothly without obstacle clipping issues.
    /// </summary>
    public class VehicleCamera : MonoBehaviour
    {
        [Header("Target")]
        [Tooltip("Vehicle transform or target point to follow")]
        public Transform target;

        [Header("Camera Offset Settings")]
        [Tooltip("Distance behind the vehicle")]
        public float distance = 6.0f;

        [Tooltip("Height above the vehicle")]
        public float height = 2.2f;

        [Tooltip("Vertical look-at target offset")]
        public float lookAtHeightOffset = 1.2f;

        [Header("Follow Dynamics")]
        [Tooltip("Position follow smoothing speed")]
        public float followSpeed = 10f;

        [Tooltip("Rotation follow smoothing speed")]
        public float rotationSpeed = 5f;

        [Header("Mouse Orbit Settings")]
        [Tooltip("Enable free mouse-look orbit")]
        public bool enableMouseLook = true;
        public float mouseSensitivity = 2.0f;
        public float autoResetDelay = 2.0f;

        private float _mouseOrbitX = 0f;
        private float _mouseOrbitY = 0f;
        private float _lastMouseMoveTime = 0f;

        private void Start()
        {
            if (target != null)
            {
                _mouseOrbitX = target.eulerAngles.y;
            }
        }

        private void LateUpdate()
        {
            if (target == null) return;

            HandleMouseInput();

            Quaternion targetRotation;
            if (enableMouseLook && (Time.time - _lastMouseMoveTime < autoResetDelay))
            {
                targetRotation = Quaternion.Euler(_mouseOrbitY, _mouseOrbitX, 0f);
            }
            else
            {
                float currentYaw = Mathf.LerpAngle(transform.eulerAngles.y, target.eulerAngles.y, rotationSpeed * Time.deltaTime);
                targetRotation = Quaternion.Euler(10f, currentYaw, 0f);
                _mouseOrbitX = currentYaw;
                _mouseOrbitY = 10f;
            }

            Vector3 targetPosition = target.position - (targetRotation * Vector3.forward * distance) + Vector3.up * height;
            transform.position = Vector3.Lerp(transform.position, targetPosition, followSpeed * Time.deltaTime);

            Vector3 lookTarget = target.position + Vector3.up * lookAtHeightOffset;
            Vector3 lookDir = lookTarget - transform.position;
            if (lookDir != Vector3.zero)
            {
                Quaternion desiredRot = Quaternion.LookRotation(lookDir, Vector3.up);
                transform.rotation = Quaternion.Slerp(transform.rotation, desiredRot, rotationSpeed * Time.deltaTime);
            }
        }

        private void HandleMouseInput()
        {
            if (!enableMouseLook) return;

            float deltaX = 0f;
            float deltaY = 0f;

#if ENABLE_INPUT_SYSTEM
            var mouse = Mouse.current;
            if (mouse != null)
            {
                Vector2 delta = mouse.delta.ReadValue();
                deltaX = delta.x * mouseSensitivity * 0.1f;
                deltaY = -delta.y * mouseSensitivity * 0.1f;
            }
#else
            deltaX = Input.GetAxis("Mouse X") * mouseSensitivity;
            deltaY = -Input.GetAxis("Mouse Y") * mouseSensitivity;
#endif

            if (Mathf.Abs(deltaX) > 0.05f || Mathf.Abs(deltaY) > 0.05f)
            {
                _mouseOrbitX += deltaX;
                _mouseOrbitY = Mathf.Clamp(_mouseOrbitY + deltaY, -10f, 60f);
                _lastMouseMoveTime = Time.time;
            }
        }

        public void SetTarget(Transform newTarget)
        {
            target = newTarget;
            if (target != null)
            {
                _mouseOrbitX = target.eulerAngles.y;
                _mouseOrbitY = 10f;
                _lastMouseMoveTime = 0f;
            }
        }
    }
}
