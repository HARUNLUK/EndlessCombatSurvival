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

        /// <summary>Camera blend between the character and the vehicle camera (s). Was the brain default 2 s.</summary>
        public static float CameraBlendTime = 0.45f;
        private static bool _blendRegistered;

        private static void ApplyCameraBlend()
        {
            if (!_blendRegistered)
            {
                _blendRegistered = true;
                EndlessCombat.Tuning.TuningPanel.Register("Kamera", "Biniş / iniş geçişi (sn)", () => CameraBlendTime, v => CameraBlendTime = v, 0f, 3f, 0.05f);
            }
            Camera main = Camera.main;
            if (main == null) return;
            var brain = main.GetComponent<Unity.Cinemachine.CinemachineBrain>();
            if (brain == null) return;
            brain.DefaultBlend = new Unity.Cinemachine.CinemachineBlendDefinition(
                CameraBlendTime > 0.001f ? Unity.Cinemachine.CinemachineBlendDefinition.Styles.EaseInOut : Unity.Cinemachine.CinemachineBlendDefinition.Styles.Cut,
                CameraBlendTime);
        }

        public bool IsPlayerInside => vehicleController != null && vehicleController.isDriven;
        public bool IsPlayerNear => _isPlayerNear;

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
            ApplyCameraBlend();

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
            // A menu, the Tab screen or the pause screen owns the buttons
            bool uiOpen = Time.timeScale <= 0.001f || EndlessSurvival.Survival.SurvivalUIState.AnyOpen
                          || EndlessSurvival.Survival.ActionMenu.IsOpen || EndlessCombat.Combat.GameInput.GameplayBlocked;

            if (IsPlayerInside)
            {
                // F / gamepad Y
                if (!uiOpen && EndlessCombat.Combat.GameInput.VehicleExitPressed)
                {
                    ExitVehicle();
                }
            }
            else
            {
                CheckPlayerProximity();
                bool canEnter = _isPlayerNear && _currentPlayer != null && _currentPlayer.activeInHierarchy;
                if (canEnter) EndlessCombat.Combat.GameInput.MarkVehicleNear();

                // F / gamepad X (X goes to a pickup or chest first when one is in reach)
                if (canEnter && !uiOpen && !EndlessCombat.Combat.CombatBody.PlayerLocked && EndlessCombat.Combat.GameInput.VehicleEnterPressed)
                {
                    EnterVehicle();
                }
            }
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
            ApplyCameraBlend();

            // The vehicle camera starts from where the character camera looked, then swings behind the vehicle
            var motor = _currentPlayer.GetComponent<EndlessCombat.Character.PlayerMotor>();
            var orbit = GetComponentInChildren<VehicleCameraOrbit>(true);
            if (orbit != null) orbit.ResetView(motor != null ? motor.LookYaw : float.NaN);

            Debug.Log("[VehicleInteraction] Player entered vehicle.");

            // 1. Disable character controller and hide player inside vehicle
            var characterController = _currentPlayer.GetComponent<CharacterController>();
            if (characterController != null) characterController.enabled = false;

            _currentPlayer.transform.SetParent(transform);
            _currentPlayer.SetActive(false);

            // 2. Enable vehicle drive controls
            vehicleController.SetDriving(true);
            EndlessCombat.Audio.GameSound.Play(EndlessCombat.Audio.SoundId.VehicleEnter, transform.position, 1f, null, true);
            if (vehicleController.engineOn) EndlessCombat.Audio.GameSound.SetLoop(EndlessCombat.Audio.SoundId.VehicleEngineLoop, transform, true);

            // 3. Switch camera from player to vehicle
            if (playerCamera != null)
            {
                playerCamera.SetActive(false);
            }

            if (vehicleCamera != null)
            {
                vehicleCamera.SetActive(true);
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
                EndlessSurvival.Survival.SurvivalNotify.Warn("Araç çok hızlı, önce yavaşla");
                return;
            }

            Debug.Log("[VehicleInteraction] Player exited vehicle.");

            // 1. Disable driving
            vehicleController.SetDriving(false);
            EndlessCombat.Audio.GameSound.Play(EndlessCombat.Audio.SoundId.VehicleExit, transform.position, 1f, null, true);
            EndlessCombat.Audio.GameSound.SetLoop(EndlessCombat.Audio.SoundId.VehicleEngineLoop, transform, false);

            // 2. Reposition player safely next to the vehicle and re-enable
            _currentPlayer.transform.SetParent(null);
            Vector3 spawnPos = FindExitSpot();
            // Only the heading of the vehicle: a car parked on a slope must not tilt the character
            Vector3 fwd = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
            Quaternion spawnRot = fwd.sqrMagnitude > 0.001f ? Quaternion.LookRotation(fwd.normalized, Vector3.up) : Quaternion.identity;

            var characterController = _currentPlayer.GetComponent<CharacterController>();
            if (characterController != null) characterController.enabled = false;

            _currentPlayer.transform.localScale = Vector3.one;
            _currentPlayer.transform.position = spawnPos;
            _currentPlayer.transform.rotation = spawnRot;
            Physics.SyncTransforms();

            _currentPlayer.SetActive(true);

            if (characterController != null) characterController.enabled = true;

            // The character camera looks where the vehicle camera looked: no random swing on getting out
            ApplyCameraBlend();
            var exitMotor = _currentPlayer.GetComponent<EndlessCombat.Character.PlayerMotor>();
            if (exitMotor != null)
            {
                var exitOrbit = GetComponentInChildren<VehicleCameraOrbit>(true);
                exitMotor.SetLookYaw(exitOrbit != null ? exitOrbit.CurrentYaw : spawnRot.eulerAngles.y);
            }

            // 3. Restore camera back to player
            if (vehicleCamera != null)
            {
                vehicleCamera.SetActive(false);
            }

            if (playerCamera != null)
            {
                playerCamera.SetActive(true);
            }

            EndlessCombat.Combat.GameInput.BlockGameplayBriefly(0.3f);
            OnDriveStateChanged?.Invoke(false);
        }

        /// <summary>
        /// A free spot on the ground beside the vehicle: the exit point first, then the left side, the right side,
        /// behind and in front. The ground is found with a ray from above, so the character never appears inside
        /// the terrain or in the air; a spot blocked by a wall, a rock or another car is skipped.
        /// </summary>
        private Vector3 FindExitSpot()
        {
            Vector3 right = Vector3.ProjectOnPlane(transform.right, Vector3.up).normalized;
            Vector3 fwd = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
            Vector3 center = transform.position;
            var candidates = new System.Collections.Generic.List<Vector3>();
            if (exitPoint != null) candidates.Add(exitPoint.position);
            foreach (float d in new[] { 2.4f, 3.2f })
            {
                candidates.Add(center - right * d);
                candidates.Add(center + right * d);
            }
            candidates.Add(center - fwd * 4.2f);
            candidates.Add(center + fwd * 4.2f);

            foreach (Vector3 c in candidates)
            {
                if (TryGround(c, out Vector3 ground) && SpotFree(ground)) return ground;
            }
            // Nothing free: the first spot with ground under it, else straight beside the vehicle
            foreach (Vector3 c in candidates)
                if (TryGround(c, out Vector3 ground)) return ground;
            return center - right * 2.4f + Vector3.up * 0.5f;
        }

        private bool TryGround(Vector3 around, out Vector3 ground)
        {
            ground = around;
            var hits = Physics.RaycastAll(around + Vector3.up * 3f, Vector3.down, 12f, ~0, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            bool found = false;
            foreach (var h in hits)
            {
                if (h.collider.transform.IsChildOf(transform)) continue; // the vehicle itself
                if (_currentPlayer != null && h.collider.transform.IsChildOf(_currentPlayer.transform)) continue;
                if (h.distance < best)
                {
                    best = h.distance;
                    ground = h.point + Vector3.up * 0.05f;
                    found = true;
                }
            }
            return found;
        }

        private bool SpotFree(Vector3 ground)
        {
            Vector3 a = ground + Vector3.up * 0.45f, b = ground + Vector3.up * 1.5f;
            foreach (var col in Physics.OverlapCapsule(a, b, 0.3f, ~0, QueryTriggerInteraction.Ignore))
            {
                if (col is TerrainCollider) continue;
                if (_currentPlayer != null && col.transform.IsChildOf(_currentPlayer.transform)) continue;
                return false;
            }
            return true;
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