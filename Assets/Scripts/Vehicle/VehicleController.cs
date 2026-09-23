using System;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace EndlessSurvival.Vehicle
{
    [RequireComponent(typeof(Rigidbody))]
    public class VehicleController : MonoBehaviour
    {
        public enum DriveType
        {
            RearWheelDrive,
            FrontWheelDrive,
            AllWheelDrive
        }

        [Header("Drive Settings")]
        [Tooltip("Drivetrain type: All-Wheel Drive, Rear-Wheel Drive, or Front-Wheel Drive")]
        public DriveType driveType = DriveType.AllWheelDrive;

        [Tooltip("Maximum engine motor torque (Nm)")]
        public float motorForce = 1500f;

        [Tooltip("Braking torque (Nm)")]
        public float brakeForce = 3000f;

        [Tooltip("Maximum steering angle (degrees)")]
        public float maxSteerAngle = 35f;

        [Tooltip("Steering smoothing speed")]
        public float steerSmoothSpeed = 8f;

        [Header("Wheel Colliders")]
        public WheelCollider frontLeftCollider;
        public WheelCollider frontRightCollider;
        public WheelCollider rearLeftCollider;
        public WheelCollider rearRightCollider;

        [Header("Wheel Visual Meshes")]
        public Transform frontLeftMesh;
        public Transform frontRightMesh;
        public Transform rearLeftMesh;
        public Transform rearRightMesh;

        [Header("Stability & Physics")]
        [Tooltip("Center of mass offset (kept low to prevent rollover)")]
        public Vector3 centerOfMassOffset = new Vector3(0f, -0.4f, 0f);

        [Tooltip("Anti-roll stabilizer bar force")]
        public float antiRollForce = 5000f;

        [Header("Status & GDD Stats")]
        [Tooltip("Whether the vehicle is currently driven by the player")]
        public bool isDriven = false;

        [Tooltip("Current fuel amount")]
        public float currentFuel = 100f;
        public float maxFuel = 100f;
        [Tooltip("Fuel consumed per second during driving")]
        public float fuelConsumptionRate = 0.5f;

        [Tooltip("Vehicle body health")]
        public float currentHealth = 100f;
        public float maxHealth = 100f;

        // Runtime state
        private Rigidbody _rb;
        private float _currentSteerAngle;
        private float _verticalInput;
        private float _horizontalInput;
        private bool _isBraking;

        public float CurrentSpeedKmh => _rb != null ? _rb.linearVelocity.magnitude * 3.6f : 0f;
        public Rigidbody Rigidbody => _rb;

        private void Awake()
        {
            _rb = GetComponent<Rigidbody>();
            SetupPhysicsProperties();
        }

        private void SetupPhysicsProperties()
        {
            if (_rb == null) return;
            _rb.centerOfMass = centerOfMassOffset;
            _rb.interpolation = RigidbodyInterpolation.Interpolate;
            _rb.collisionDetectionMode = CollisionDetectionMode.Continuous;
        }

        private void Update()
        {
            if (isDriven)
            {
                HandleInput();
                ConsumeFuel();
            }
            else
            {
                _verticalInput = 0f;
                _horizontalInput = 0f;
                _isBraking = true; // Apply parking brake when not driven
            }

            UpdateWheelVisuals();
        }

        private void FixedUpdate()
        {
            ApplyMotor();
            ApplySteering();
            ApplyAntiRollBars();
        }

        private void HandleInput()
        {
#if ENABLE_INPUT_SYSTEM
            var keyboard = Keyboard.current;
            if (keyboard != null)
            {
                _horizontalInput = 0f;
                _verticalInput = 0f;

                if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed) _horizontalInput -= 1f;
                if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed) _horizontalInput += 1f;
                if (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed) _verticalInput += 1f;
                if (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed) _verticalInput -= 1f;

                _isBraking = keyboard.spaceKey.isPressed;
            }
#else
            _horizontalInput = Input.GetAxis("Horizontal");
            _verticalInput = Input.GetAxis("Vertical");
            _isBraking = Input.GetKey(KeyCode.Space);
#endif
        }

        private void ConsumeFuel()
        {
            if (currentFuel > 0f && Mathf.Abs(_verticalInput) > 0.05f)
            {
                currentFuel -= fuelConsumptionRate * Time.deltaTime;
                if (currentFuel < 0f) currentFuel = 0f;
            }
        }

        private void ApplyMotor()
        {
            float effectiveVerticalInput = (currentFuel > 0f) ? _verticalInput : 0f;
            float torque = effectiveVerticalInput * motorForce;
            float currentBrakeTorque = _isBraking ? brakeForce : 0f;

            if (driveType == DriveType.FrontWheelDrive || driveType == DriveType.AllWheelDrive)
            {
                if (frontLeftCollider != null) frontLeftCollider.motorTorque = torque;
                if (frontRightCollider != null) frontRightCollider.motorTorque = torque;
            }
            else
            {
                if (frontLeftCollider != null) frontLeftCollider.motorTorque = 0f;
                if (frontRightCollider != null) frontRightCollider.motorTorque = 0f;
            }

            if (driveType == DriveType.RearWheelDrive || driveType == DriveType.AllWheelDrive)
            {
                if (rearLeftCollider != null) rearLeftCollider.motorTorque = torque;
                if (rearRightCollider != null) rearRightCollider.motorTorque = torque;
            }
            else
            {
                if (rearLeftCollider != null) rearLeftCollider.motorTorque = 0f;
                if (rearRightCollider != null) rearRightCollider.motorTorque = 0f;
            }

            ApplyBrakeTorque(currentBrakeTorque);
        }

        private void ApplyBrakeTorque(float brakeTorqueValue)
        {
            if (frontLeftCollider != null) frontLeftCollider.brakeTorque = brakeTorqueValue;
            if (frontRightCollider != null) frontRightCollider.brakeTorque = brakeTorqueValue;
            if (rearLeftCollider != null) rearLeftCollider.brakeTorque = brakeTorqueValue;
            if (rearRightCollider != null) rearRightCollider.brakeTorque = brakeTorqueValue;
        }

        private void ApplySteering()
        {
            float targetAngle = _horizontalInput * maxSteerAngle;
            _currentSteerAngle = Mathf.Lerp(_currentSteerAngle, targetAngle, Time.fixedDeltaTime * steerSmoothSpeed);

            if (frontLeftCollider != null) frontLeftCollider.steerAngle = _currentSteerAngle;
            if (frontRightCollider != null) frontRightCollider.steerAngle = _currentSteerAngle;
        }

        private void UpdateWheelVisuals()
        {
            SyncWheelMesh(frontLeftCollider, frontLeftMesh);
            SyncWheelMesh(frontRightCollider, frontRightMesh);
            SyncWheelMesh(rearLeftCollider, rearLeftMesh);
            SyncWheelMesh(rearRightCollider, rearRightMesh);
        }

        private void SyncWheelMesh(WheelCollider col, Transform meshTransform)
        {
            if (col == null || meshTransform == null) return;
            col.GetWorldPose(out Vector3 pos, out Quaternion rot);
            meshTransform.position = pos;
            meshTransform.rotation = rot;
        }

        private void ApplyAntiRollBars()
        {
            ApplyAntiRoll(frontLeftCollider, frontRightCollider);
            ApplyAntiRoll(rearLeftCollider, rearRightCollider);
        }

        private void ApplyAntiRoll(WheelCollider leftWheel, WheelCollider rightWheel)
        {
            if (leftWheel == null || rightWheel == null || _rb == null) return;

            float travelL = 1.0f;
            float travelR = 1.0f;

            bool groundedL = leftWheel.GetGroundHit(out WheelHit hitL);
            if (groundedL)
                travelL = (-leftWheel.transform.InverseTransformPoint(hitL.point).y - leftWheel.radius) / leftWheel.suspensionDistance;

            bool groundedR = rightWheel.GetGroundHit(out WheelHit hitR);
            if (groundedR)
                travelR = (-rightWheel.transform.InverseTransformPoint(hitR.point).y - rightWheel.radius) / rightWheel.suspensionDistance;

            float antiRollForceAmount = (travelL - travelR) * antiRollForce;

            if (groundedL)
                _rb.AddForceAtPosition(leftWheel.transform.up * -antiRollForceAmount, leftWheel.transform.position);

            if (groundedR)
                _rb.AddForceAtPosition(rightWheel.transform.up * antiRollForceAmount, rightWheel.transform.position);
        }

        public void SetDriving(bool driving)
        {
            isDriven = driving;
            if (!isDriven)
            {
                _verticalInput = 0f;
                _horizontalInput = 0f;
                _isBraking = true;
                ApplyBrakeTorque(brakeForce);
            }
            else
            {
                _isBraking = false;
                ApplyBrakeTorque(0f);
            }
        }

        [ContextMenu("Auto Setup Vehicle")]
        public void AutoSetupVehicle()
        {
            Debug.Log("[VehicleController] Starting auto vehicle setup...");

            // 1. Rigidbody configuration
            _rb = GetComponent<Rigidbody>();
            if (_rb == null) _rb = gameObject.AddComponent<Rigidbody>();
            _rb.mass = 1500f;
            _rb.linearDamping = 0.05f;
            _rb.angularDamping = 1.0f;
            _rb.interpolation = RigidbodyInterpolation.Interpolate;
            _rb.collisionDetectionMode = CollisionDetectionMode.Continuous;

            // 2. Locate visual wheel transforms by name
            Transform[] allChildren = GetComponentsInChildren<Transform>(true);
            foreach (var child in allChildren)
            {
                string lower = child.name.ToLower();
                if (lower == "fl_wheel" || lower.Contains("frontleft")) frontLeftMesh = child;
                else if (lower == "fr_wheel" || lower.Contains("frontright")) frontRightMesh = child;
                else if (lower == "bl_wheel" || lower.Contains("backleft")) rearLeftMesh = child;
                else if (lower == "br_wheel" || lower.Contains("backright")) rearRightMesh = child;
            }

            // 3. WheelColliders root holder
            Transform collidersHolder = transform.Find("WheelColliders");
            if (collidersHolder == null)
            {
                GameObject holderGo = new GameObject("WheelColliders");
                holderGo.transform.SetParent(transform, false);
                collidersHolder = holderGo.transform;
            }

            // 4. Create or fetch WheelColliders
            frontLeftCollider = GetOrCreateWheelCollider(collidersHolder, "FL_Collider", frontLeftMesh);
            frontRightCollider = GetOrCreateWheelCollider(collidersHolder, "FR_Collider", frontRightMesh);
            rearLeftCollider = GetOrCreateWheelCollider(collidersHolder, "BL_Collider", rearLeftMesh);
            rearRightCollider = GetOrCreateWheelCollider(collidersHolder, "BR_Collider", rearRightMesh);

            // 5. Body Box Collider
            BoxCollider bodyCollider = GetComponent<BoxCollider>();
            if (bodyCollider == null)
            {
                bodyCollider = gameObject.AddComponent<BoxCollider>();
                bodyCollider.center = new Vector3(0f, 0.9f, 0f);
                bodyCollider.size = new Vector3(2.0f, 1.4f, 4.4f);
            }

            // 6. Driver Exit Point
            Transform exitPoint = transform.Find("ExitPoint");
            if (exitPoint == null)
            {
                GameObject exitGo = new GameObject("ExitPoint");
                exitGo.transform.SetParent(transform, false);
                exitGo.transform.localPosition = new Vector3(-1.8f, 0.2f, 0.3f);
            }

            // 7. Camera Follow Target
            Transform camTarget = transform.Find("VehicleCameraTarget");
            if (camTarget == null)
            {
                GameObject camTargetGo = new GameObject("VehicleCameraTarget");
                camTargetGo.transform.SetParent(transform, false);
                camTargetGo.transform.localPosition = new Vector3(0f, 1.4f, -0.5f);
            }

            SetupPhysicsProperties();
            Debug.Log("[VehicleController] Auto vehicle setup completed successfully.");
        }

        private WheelCollider GetOrCreateWheelCollider(Transform parent, string name, Transform referenceMesh)
        {
            Transform existing = parent.Find(name);
            WheelCollider wc;
            if (existing != null)
            {
                wc = existing.GetComponent<WheelCollider>();
            }
            else
            {
                GameObject go = new GameObject(name);
                go.transform.SetParent(parent, false);
                if (referenceMesh != null)
                {
                    go.transform.position = referenceMesh.position;
                    go.transform.rotation = transform.rotation;
                }
                wc = go.AddComponent<WheelCollider>();
            }

            if (wc != null)
            {
                wc.mass = 35f;
                wc.radius = 0.38f;
                wc.wheelDampingRate = 0.5f;
                wc.suspensionDistance = 0.2f;

                JointSpring spring = wc.suspensionSpring;
                spring.spring = 35000f;
                spring.damper = 4000f;
                spring.targetPosition = 0.5f;
                wc.suspensionSpring = spring;
            }

            return wc;
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.yellow;
            Vector3 comWorld = transform.TransformPoint(centerOfMassOffset);
            Gizmos.DrawSphere(comWorld, 0.15f);
            Gizmos.DrawWireSphere(comWorld, 0.25f);
        }
    }
}
