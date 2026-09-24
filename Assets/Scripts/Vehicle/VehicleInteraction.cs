using System;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace EndlessSurvival.Vehicle
{
    public class VehicleInteraction : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("Vehicle controller reference (automatically retrieved if left empty)")]
        public VehicleController vehicleController;

        [Tooltip("Spawn point where the character appears upon exiting the vehicle")]
        public Transform exitPoint;

        [Tooltip("Vehicle camera GameObject activated during driving")]
        public GameObject vehicleCamera;

        [Tooltip("Player walking camera GameObject (automatically found if left empty)")]
        public GameObject playerCamera;

        [Header("Interaction Settings")]
        [Tooltip("Maximum distance to enter the vehicle")]
        public float interactionDistance = 3.5f;

        [Tooltip("Interact key (Default: F)")]
        public KeyCode legacyInteractKey = KeyCode.F;

        // Events for UI
        public event Action<bool> OnPlayerNearVehicle;
        public event Action<bool> OnDriveStateChanged;

        private GameObject _currentPlayer;
        private bool _isPlayerNear;

        public bool IsPlayerInside => vehicleController != null && vehicleController.isDriven;

        private void Awake()
        {
            if (vehicleController == null)
                vehicleController = GetComponent<VehicleController>();

            if (exitPoint == null)
            {
                Transform foundExit = transform.Find("ExitPoint");
                if (foundExit != null) exitPoint = foundExit;
            }

            FindCamerasIfNeeded();

            if (vehicleController != null)
                vehicleController.SetDriving(false);

            if (vehicleCamera != null)
                vehicleCamera.SetActive(false);
        }

        private void FindCamerasIfNeeded()
        {
            if (playerCamera == null)
            {
                playerCamera = GameObject.Find("PlayerFollowCamera");
            }

            if (vehicleCamera == null)
            {
                vehicleCamera = GameObject.Find("VehicleFollowCamera");
                if (vehicleCamera == null)
                {
                    var found = transform.Find("VehicleFollowCamera");
                    if (found != null) vehicleCamera = found.gameObject;
                }
            }
        }

        private void Update()
        {
            if (IsPlayerInside)
            {
                if (CheckInteractPressed())
                {
                    ExitVehicle();
                }
            }
            else
            {
                CheckPlayerProximity();

                if (_isPlayerNear && CheckInteractPressed())
                {
                    EnterVehicle();
                }
            }
        }

        private bool CheckInteractPressed()
        {
#if ENABLE_INPUT_SYSTEM
            var keyboard = Keyboard.current;
            return keyboard != null && keyboard.fKey.wasPressedThisFrame;
#else
            return Input.GetKeyDown(legacyInteractKey);
#endif
        }

        private void CheckPlayerProximity()
        {
            if (_currentPlayer == null)
            {
                _currentPlayer = GameObject.FindGameObjectWithTag("Player");
                if (_currentPlayer == null)
                {
                    var armature = GameObject.Find("PlayerArmature");
                    if (armature != null) _currentPlayer = armature;
                }
            }

            if (_currentPlayer == null) return;

            Collider col = GetComponent<Collider>();
            float dist;
            if (col != null)
            {
                Vector3 closestPoint = col.ClosestPoint(_currentPlayer.transform.position);
                dist = Vector3.Distance(closestPoint, _currentPlayer.transform.position);
            }
            else
            {
                dist = Vector3.Distance(transform.position, _currentPlayer.transform.position);
            }

            bool wasNear = _isPlayerNear;
            _isPlayerNear = (dist <= interactionDistance);

            if (wasNear != _isPlayerNear)
            {
                OnPlayerNearVehicle?.Invoke(_isPlayerNear);
            }
        }

        public void EnterVehicle()
        {
            if (_currentPlayer == null || vehicleController == null) return;

            FindCamerasIfNeeded();

            Debug.Log("[VehicleInteraction] Player entered vehicle.");

            // 1. Disable character controller and hide player inside vehicle
            var characterController = _currentPlayer.GetComponent<CharacterController>();
            if (characterController != null) characterController.enabled = false;

            _currentPlayer.transform.SetParent(transform);
            _currentPlayer.SetActive(false);

            // 2. Enable vehicle drive controls
            vehicleController.SetDriving(true);

            // 3. Switch camera from player to vehicle
            if (playerCamera != null)
            {
                playerCamera.SetActive(false);
            }

            if (vehicleCamera != null)
            {
                vehicleCamera.SetActive(true);

                var customCam = vehicleCamera.GetComponent<VehicleCamera>();
                if (customCam != null)
                {
                    customCam.SetTarget(transform);
                }
            }

            _isPlayerNear = false;
            OnPlayerNearVehicle?.Invoke(false);
            OnDriveStateChanged?.Invoke(true);
        }

        public void ExitVehicle()
        {
            if (_currentPlayer == null || vehicleController == null) return;

            if (vehicleController.CurrentSpeedKmh > 30f)
            {
                Debug.LogWarning("[VehicleInteraction] Vehicle is moving too fast to safely exit.");
                return;
            }

            Debug.Log("[VehicleInteraction] Player exited vehicle.");

            // 1. Disable driving
            vehicleController.SetDriving(false);

            // 2. Reposition player safely at exit point and re-enable
            _currentPlayer.transform.SetParent(null);
            Vector3 spawnPos = exitPoint != null ? exitPoint.position : transform.position - transform.right * 2.4f;
            Quaternion spawnRot = exitPoint != null ? exitPoint.rotation : transform.rotation;

            var characterController = _currentPlayer.GetComponent<CharacterController>();
            if (characterController != null) characterController.enabled = false;

            _currentPlayer.transform.position = spawnPos;
            _currentPlayer.transform.rotation = spawnRot;
            Physics.SyncTransforms();

            _currentPlayer.SetActive(true);

            if (characterController != null) characterController.enabled = true;

            // 3. Restore camera back to player
            if (vehicleCamera != null)
            {
                vehicleCamera.SetActive(false);
            }

            if (playerCamera != null)
            {
                playerCamera.SetActive(true);
            }

            OnDriveStateChanged?.Invoke(false);
        }


        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(transform.position, interactionDistance);

            if (exitPoint != null)
            {
                Gizmos.color = Color.green;
                Gizmos.DrawSphere(exitPoint.position, 0.25f);
            }
        }
    }
}
