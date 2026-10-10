using UnityEngine;
using EndlessCombat.Combat;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace EndlessSurvival.Vehicle
{
    /// <summary>
    /// Raycast vehicle with its own suspension and tire model (the WheelColliders are only used as wheel
    /// anchors: position + radius). Engine/gearbox -> per-wheel drive and brake torque -> slip-angle based
    /// tire forces limited by a friction circle. Tire forces can be applied above the contact patch to
    /// control how much the body rolls, so the truck leans in corners without tripping over itself.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class VehicleController : MonoBehaviour, IDamageable
    {
        public enum DriveType
        {
            RearWheelDrive,
            FrontWheelDrive,
            AllWheelDrive
        }

        [Header("Engine")]
        [Tooltip("Peak engine (crankshaft) torque in Nm. Engine upgrades add to this.")]
        public float maxEngineTorque = 380f;

        [Tooltip("Torque multiplier over normalized RPM (0 = 0 rpm, 1 = maxRpm)")]
        public AnimationCurve torqueCurve = new AnimationCurve(
            new Keyframe(0f, 0.6f),
            new Keyframe(0.25f, 0.85f),
            new Keyframe(0.55f, 1f),
            new Keyframe(0.85f, 0.92f),
            new Keyframe(1f, 0.75f));

        public float idleRpm = 850f;
        [Tooltip("Rev limiter")]
        public float maxRpm = 6000f;
        [Tooltip("RPM the clutch lets the engine rev to when pulling away from a standstill")]
        public float launchRpm = 2800f;
        [Tooltip("How fast the engine RPM follows its target")]
        public float rpmResponse = 12f;
        [Tooltip("Engine braking torque at the crankshaft when off throttle (Nm, scaled by RPM)")]
        public float engineBrakeTorque = 80f;

        [Header("Gearbox & Drivetrain")]
        public DriveType driveType = DriveType.AllWheelDrive;
        public float[] gearRatios = { 4.0f, 2.4f, 1.6f, 1.15f, 0.9f };
        public float reverseRatio = 3.6f;
        public float finalDriveRatio = 4.1f;
        [Range(0.5f, 1f)] public float drivetrainEfficiency = 0.9f;
        [Tooltip("AWD only: share of torque sent to the front axle")]
        [Range(0f, 1f)] public float frontTorqueShare = 0.4f;
        [Tooltip("Time the clutch is open during a gear change (no drive torque)")]
        public float shiftTime = 0.2f;
        [Tooltip("Upshift RPM at light throttle / full throttle")]
        public Vector2 upshiftRpm = new Vector2(3000f, 5600f);
        [Tooltip("Downshift RPM at light throttle / full throttle")]
        public Vector2 downshiftRpm = new Vector2(1500f, 3200f);
        public float topSpeedKmh = 160f;
        public float topReverseSpeedKmh = 30f;

        [Header("Brakes")]
        [Tooltip("Total brake torque for the whole car at full pedal (Nm)")]
        public float maxBrakeTorque = 10000f;
        [Tooltip("Share of brake torque on the front axle")]
        [Range(0f, 1f)] public float brakeBias = 0.62f;
        [Tooltip("Handbrake torque per rear wheel (Nm)")]
        public float handbrakeTorque = 3000f;
        [Tooltip("Rear lateral grip multiplier with the handbrake pulled (lower = easier slides)")]
        [Range(0.1f, 1f)] public float handbrakeGripMultiplier = 0.45f;
        [Tooltip("Space / gamepad B: false = full brake on all wheels, true = rear handbrake that slides the truck")]
        public bool spaceIsHandbrake = false;
        [Tooltip("Anti-lock brakes: keep steering while braking hard")]
        public bool abs = true;

        [Header("Steering")]
        [Tooltip("Steering lock at low speed (degrees, outer wheel)")]
        public float maxSteerAngle = 35f;
        [Tooltip("Limit the lock at speed to the angle that gives maximum cornering grip (like racing games' steering assist)")]
        public bool speedSensitiveSteering = true;
        [Tooltip("Extra lock allowed past the grip-optimal angle, as a multiple of the tire's peak slip angle")]
        public float steerSlipAllowance = 1.0f;
        [Tooltip("How fast the steering winds in (full lock per second)")]
        public float steerSpeed = 5f;
        [Tooltip("How fast the steering returns to centre (full lock per second)")]
        public float steerReturnSpeed = 7f;

        [Header("Pedals")]
        public float throttleRiseSpeed = 6f;
        public float throttleFallSpeed = 8f;
        public float brakeRiseSpeed = 6f;
        public float brakeFallSpeed = 10f;

        [Header("Wheels (WheelColliders are used as anchors: position + radius)")]
        public WheelCollider frontLeftCollider;
        public WheelCollider frontRightCollider;
        public WheelCollider rearLeftCollider;
        public WheelCollider rearRightCollider;

        [Header("Wheel Visual Meshes")]
        public Transform frontLeftMesh;
        public Transform frontRightMesh;
        public Transform rearLeftMesh;
        public Transform rearRightMesh;

        [Header("Suspension")]
        [Tooltip("Suspension travel in metres")]
        public float suspensionDistance = 0.3f;
        [Tooltip("Spring rate per wheel (N/m)")]
        public float suspensionSpring = 38000f;
        [Tooltip("Damper rate per wheel (N per m/s)")]
        public float suspensionDamper = 3400f;
        [Tooltip("Anti-roll bar stiffness per axle (N per metre of left/right compression difference)")]
        public float antiRollForce = 10000f;
        [Tooltip("Layers the wheels can stand on")]
        public LayerMask groundLayers = ~0;
        [Tooltip("Surfaces steeper than this (relative to the truck) are walls, not ground: wheels won't climb them")]
        [Range(10f, 80f)] public float maxGroundAngle = 50f;

        [Header("Tires")]
        [Tooltip("Longitudinal friction coefficient (acceleration / braking grip)")]
        [Range(0.5f, 3f)] public float forwardGrip = 1.3f;
        [Tooltip("Lateral friction coefficient (cornering grip)")]
        [Range(0.5f, 3f)] public float sidewaysGrip = 1.7f;
        [Tooltip("Slip angle (degrees) at which the tire produces peak cornering force")]
        public float peakSlipAngle = 5f;
        [Tooltip("Grip left once the tire slides well past its peak (fraction of peak)")]
        [Range(0.3f, 1f)] public float slideGrip = 0.75f;
        [Tooltip("Where tire forces act: 0 = contact patch (lots of body roll, can flip), 1 = centre of mass (no roll)")]
        [Range(0f, 1f)] public float tireForceHeight = 0.55f;

        [Header("Body & Stability")]
        [Tooltip("Centre of mass in local space")]
        public Vector3 centerOfMassOffset = new Vector3(0f, 0.1f, 0.1f);
        [Tooltip("Scales the collider-derived inertia (x = pitch, y = yaw, z = roll)")]
        public Vector3 inertiaScale = new Vector3(1f, 1f, 1f);
        [Tooltip("Gentle yaw correction against spinning out (0 = off). Not applied while using the handbrake.")]
        [Range(0f, 1f)] public float stabilityAssist = 0.25f;
        [Tooltip("Helps the nose follow the steering when the truck understeers (0 = pure physics). Never exceeds the tires' grip limit.")]
        [Range(0f, 1f)] public float turnAssist = 0.7f;
        [Tooltip("Adds a collider under the body down to this height above the ground, so kerbs and barriers hit the body instead of sliding under it")]
        public float groundClearance = 0.25f;
        [Tooltip("Friction of the body against walls/barriers (low = slides along them instead of grabbing and flipping)")]
        [Range(0f, 1f)] public float bodyFriction = 0.1f;
        [Tooltip("Automatically put the truck back on its wheels after it has been stuck on its side/roof")]
        public bool autoFlipRecovery = true;
        public float flipRecoveryDelay = 2.5f;

        [Header("Resistance & Aero")]
        [Tooltip("Aerodynamic drag: force = coefficient * speed^2")]
        public float airDragCoefficient = 0.6f;
        [Tooltip("Rolling resistance coefficient (fraction of weight)")]
        public float rollingResistance = 0.015f;
        [Tooltip("Downforce: force = coefficient * speed^2")]
        public float downforceCoefficient = 0.4f;

        [Header("Air Physics")]
        [Tooltip("Gravity multiplier applied only while every wheel is off the ground. Large vehicles look floaty at normal gravity.")]
        public float airGravityMultiplier = 2.5f;

        [Header("Status & GDD Stats")]
        [Tooltip("Whether the vehicle is currently driven by the player")]
        public bool isDriven = false;

        [Tooltip("Current fuel amount")]
        public float currentFuel = 100f;
        public float maxFuel = 100f;
        [Tooltip("Fuel consumed per second at full throttle")]
        public float fuelConsumptionRate = 0.1f;

        [Tooltip("Vehicle body health")]
        public float currentHealth = 100f;
        public float maxHealth = 100f;

        [Header("Damage & Breakdown")]
        [Tooltip("Impact speed (m/s) below which collisions do no damage")]
        public float collisionDamageThreshold = 14f;

        [Tooltip("Damage per m/s of impact speed above the threshold")]
        public float collisionDamageScale = 0.5f;

        [Tooltip("Multiplier applied to bullet damage received by the vehicle body")]
        [Range(0f, 1f)]
        public float bulletDamageMultiplier = 0.3f;

        [Tooltip("Below this health ratio the engine loses power")]
        [Range(0f, 1f)]
        public float weakEngineHealthRatio = 0.3f;

        private const float ReverseSwitchSpeed = 0.8f; // m/s

        private class Wheel
        {
            public WheelCollider anchor;
            public Transform mesh;
            public bool front;
            public bool driven;
            public float radius;

            public bool grounded;
            public RaycastHit hit;
            public float springLength;
            public float compression;
            public float lastCompression;
            public float load;
            public float steerAngle;
            public float driveTorque;
            public float brakeTorque;
            public float spin;          // rad/s
            public float meshSpinAngle; // degrees
            public bool slipping;
        }

        // Raw input
        private float _throttleRaw;
        private float _brakeRaw;
        private float _steerRaw;
        private bool _handbrakeRaw;
        private bool _hardBrakeRaw;

        // Smoothed controls
        private float _throttle;
        private float _brake;
        private float _steer;
        private float _handbrake;
        private float _currentSteerAngle;

        // Powertrain state
        private Rigidbody _rb;
        private int _gear = 1;          // -1 = reverse, 1..n = forward gears
        private int _pendingGear = 1;
        private float _shiftTimer;
        private float _shiftCooldown;
        private float _engineRpm;
        private bool _drivenWheelsSlipping;

        private Wheel[] _wheels = new Wheel[0];
        private readonly RaycastHit[] _hitBuffer = new RaycastHit[8];
        private float _wheelbase = 2.5f;
        private float _trackWidth = 1.5f;
        private float _wheelRadius = 0.45f;
        private float _flipTimer;

        public float CurrentSpeedKmh => _rb != null ? _rb.linearVelocity.magnitude * 3.6f : 0f;
        public float ForwardSpeed => _rb != null ? Vector3.Dot(_rb.linearVelocity, transform.forward) : 0f;
        public Rigidbody Rigidbody => _rb;
        public float EngineRpm => _engineRpm;
        public float Throttle => _throttle;
        public bool IsShifting => _shiftTimer > 0f;
        public int CurrentGear => _gear;
        public string GearLabel => IsShifting ? "-" : (_gear < 0 ? "R" : _gear.ToString());
        public int GroundedWheelCount { get; private set; }

        public bool IsBroken => currentHealth <= 0f;
        public bool IsDead => IsBroken;

        public float HealthRatio => maxHealth > 0f ? Mathf.Clamp01(currentHealth / maxHealth) : 0f;

        private bool EngineRunning => isDriven && !IsBroken && currentFuel > 0f;

        public void TakeDamage(float damage, Vector3 hitPoint, Vector3 hitNormal)
        {
            ApplyDamage(damage * bulletDamageMultiplier);
        }

        public void ApplyDamage(float amount)
        {
            if (amount <= 0f || IsBroken) return;
            currentHealth = Mathf.Max(0f, currentHealth - amount);
        }

        /// <summary>Adds fuel up to the tank capacity and returns the amount actually added.</summary>
        public float Refuel(float amount)
        {
            float before = currentFuel;
            currentFuel = Mathf.Min(maxFuel, currentFuel + Mathf.Max(0f, amount));
            return currentFuel - before;
        }

        /// <summary>Restores health up to the maximum and returns the amount actually restored.</summary>
        public float Repair(float amount)
        {
            float before = currentHealth;
            currentHealth = Mathf.Min(maxHealth, currentHealth + Mathf.Max(0f, amount));
            return currentHealth - before;
        }

        private void OnCollisionEnter(Collision collision)
        {
            float impact = collision.relativeVelocity.magnitude;
            if (impact <= collisionDamageThreshold) return;
            ApplyDamage((impact - collisionDamageThreshold) * collisionDamageScale);
        }

        private float GetPowerFactor()
        {
            if (IsBroken) return 0f;
            float ratio = HealthRatio;
            if (weakEngineHealthRatio <= 0f || ratio >= weakEngineHealthRatio) return 1f;
            return Mathf.Lerp(0.5f, 1f, ratio / weakEngineHealthRatio);
        }

        private void Awake()
        {
            _rb = GetComponent<Rigidbody>();
            SetupPhysicsProperties();

            if (GetComponent<VehicleModifications>() == null)
            {
                gameObject.AddComponent<VehicleModifications>();
            }
        }

        private void SetupPhysicsProperties()
        {
            if (_rb == null) return;

            BuildWheels();
            SetupBodyColliders();

            _rb.interpolation = RigidbodyInterpolation.Interpolate;
            _rb.collisionDetectionMode = CollisionDetectionMode.Continuous;
            _rb.linearDamping = 0f;
            _rb.angularDamping = 0.05f;
            _rb.maxAngularVelocity = 10f;

            _rb.ResetCenterOfMass();
            _rb.ResetInertiaTensor();
            _rb.centerOfMass = centerOfMassOffset;
            _rb.inertiaTensor = Vector3.Scale(_rb.inertiaTensor, inertiaScale);
        }

        private void BuildWheels()
        {
            var list = new System.Collections.Generic.List<Wheel>(4);
            AddWheel(list, frontLeftCollider, frontLeftMesh, true);
            AddWheel(list, frontRightCollider, frontRightMesh, true);
            AddWheel(list, rearLeftCollider, rearLeftMesh, false);
            AddWheel(list, rearRightCollider, rearRightMesh, false);
            _wheels = list.ToArray();

            if (frontLeftCollider != null && frontRightCollider != null && rearLeftCollider != null && rearRightCollider != null)
            {
                Vector3 front = (frontLeftCollider.transform.position + frontRightCollider.transform.position) * 0.5f;
                Vector3 rear = (rearLeftCollider.transform.position + rearRightCollider.transform.position) * 0.5f;
                _wheelbase = Mathf.Max(0.5f, Vector3.Distance(front, rear));
                _trackWidth = Mathf.Max(0.5f, Vector3.Distance(frontLeftCollider.transform.position, frontRightCollider.transform.position));
            }

            if (_wheels.Length > 0) _wheelRadius = _wheels[0].radius;
        }

        private void AddWheel(System.Collections.Generic.List<Wheel> list, WheelCollider wc, Transform mesh, bool front)
        {
            if (wc == null) return;

            // The WheelCollider stays in the hierarchy as a wheel anchor, but PhysX must not simulate it.
            wc.enabled = false;

            list.Add(new Wheel
            {
                anchor = wc,
                mesh = mesh,
                front = front,
                driven = driveType == DriveType.AllWheelDrive
                         || (front ? driveType == DriveType.FrontWheelDrive : driveType == DriveType.RearWheelDrive),
                radius = wc.radius * wc.transform.lossyScale.y,
                springLength = suspensionDistance
            });
        }

        private void SetupBodyColliders()
        {
            BoxCollider body = GetComponent<BoxCollider>();
            if (body == null) return;

            PhysicsMaterial material = new PhysicsMaterial("VehicleBody")
            {
                dynamicFriction = bodyFriction,
                staticFriction = bodyFriction,
                bounciness = 0f,
                frictionCombine = PhysicsMaterialCombine.Minimum,
                bounceCombine = PhysicsMaterialCombine.Minimum
            };
            if (body.sharedMaterial == null) body.sharedMaterial = material;

            if (groundClearance <= 0f || _wheels.Length == 0 || transform.Find("LowerBodyCollider") != null) return;

            // Ground height under the truck when it sits on its springs.
            float staticCompression = Mathf.Min(suspensionDistance, _rb.mass * Physics.gravity.magnitude / (_wheels.Length * suspensionSpring));
            Wheel w = _wheels[0];
            Vector3 groundWorld = w.anchor.transform.position - transform.up * (suspensionDistance - staticCompression + w.radius);
            float scaleY = Mathf.Max(0.0001f, transform.lossyScale.y);
            float groundLocalY = transform.InverseTransformPoint(groundWorld).y;
            float bottom = groundLocalY + groundClearance / scaleY;
            float top = body.center.y - body.size.y * 0.5f + 0.05f;
            if (top - bottom < 0.05f) return;

            GameObject skirt = new GameObject("LowerBodyCollider");
            skirt.layer = gameObject.layer;
            skirt.transform.SetParent(transform, false);
            BoxCollider lower = skirt.AddComponent<BoxCollider>();
            lower.center = new Vector3(body.center.x, (top + bottom) * 0.5f, body.center.z);
            lower.size = new Vector3(body.size.x * 0.98f, top - bottom, body.size.z * 0.96f);
            lower.sharedMaterial = body.sharedMaterial;
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
                _throttleRaw = 0f;
                _brakeRaw = 0f;
                _steerRaw = 0f;
                _handbrakeRaw = true; // Parking brake when nobody is driving
                _hardBrakeRaw = false;
            }

            UpdateWheelVisuals();
        }

        private void FixedUpdate()
        {
            if (_rb == null || _wheels.Length == 0) return;

            float dt = Time.fixedDeltaTime;
            float forwardSpeed = ForwardSpeed;

            UpdateControls(dt, forwardSpeed);
            UpdateGearbox(dt, forwardSpeed);
            UpdateEngine(dt, forwardSpeed);
            UpdateWheelTorques(forwardSpeed);
            UpdateSteering(forwardSpeed);
            UpdateSuspension(dt);
            ApplyAntiRollBars();
            ApplyTireForces(dt);
            ApplyResistance();
            ApplyStabilityAssist(forwardSpeed);
            ApplyAirGravity();
            UpdateFlipRecovery(dt);
        }

        // ---------------------------------------------------------------- Input

        private void HandleInput()
        {
            float throttle = 0f, brake = 0f, steer = 0f;
            bool hardBrake = false;

#if ENABLE_INPUT_SYSTEM
            var keyboard = Keyboard.current;
            if (keyboard != null)
            {
                if (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed) throttle = 1f;
                if (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed) brake = 1f;
                if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed) steer -= 1f;
                if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed) steer += 1f;
                hardBrake = keyboard.spaceKey.isPressed;
            }

            var gamepad = Gamepad.current;
            if (gamepad != null)
            {
                throttle = Mathf.Max(throttle, gamepad.rightTrigger.ReadValue());
                brake = Mathf.Max(brake, gamepad.leftTrigger.ReadValue());
                float stick = gamepad.leftStick.ReadValue().x;
                if (Mathf.Abs(stick) > Mathf.Abs(steer)) steer = stick;
                hardBrake |= gamepad.buttonEast.isPressed;
            }
#else
            float vertical = Input.GetAxisRaw("Vertical");
            throttle = Mathf.Max(0f, vertical);
            brake = Mathf.Max(0f, -vertical);
            steer = Input.GetAxisRaw("Horizontal");
            hardBrake = Input.GetKey(KeyCode.Space);
#endif

            _throttleRaw = throttle;
            _brakeRaw = brake;
            _steerRaw = Mathf.Clamp(steer, -1f, 1f);
            // Space / B: full brake on all four wheels (ABS), or a drifting rear handbrake if enabled.
            _hardBrakeRaw = hardBrake && !spaceIsHandbrake;
            _handbrakeRaw = hardBrake && spaceIsHandbrake;
        }

        private void UpdateControls(float dt, float forwardSpeed)
        {
            // Automatic-style pedal mapping: "brake" reverses once the car has stopped, and vice versa.
            float accelPedal, brakePedal;
            if (_gear < 0)
            {
                accelPedal = _brakeRaw;
                brakePedal = _throttleRaw;
                if (_throttleRaw > 0.1f && _brakeRaw < 0.1f && forwardSpeed > -ReverseSwitchSpeed)
                    RequestShift(1);
            }
            else
            {
                accelPedal = _throttleRaw;
                brakePedal = _brakeRaw;
                if (_brakeRaw > 0.1f && _throttleRaw < 0.1f && forwardSpeed < ReverseSwitchSpeed && isDriven)
                    RequestShift(-1);
            }

            if (_hardBrakeRaw) brakePedal = 1f;
            if (!EngineRunning) accelPedal = 0f;

            _throttle = MoveTowardsRate(_throttle, accelPedal, throttleRiseSpeed, throttleFallSpeed, dt);
            _brake = MoveTowardsRate(_brake, brakePedal, brakeRiseSpeed, brakeFallSpeed, dt);
            _handbrake = Mathf.MoveTowards(_handbrake, _handbrakeRaw ? 1f : 0f, dt * 8f);

            bool returning = Mathf.Abs(_steerRaw) < Mathf.Abs(_steer) || Mathf.Sign(_steerRaw) != Mathf.Sign(_steer);
            _steer = Mathf.MoveTowards(_steer, _steerRaw, (returning ? steerReturnSpeed : steerSpeed) * dt);
        }

        private static float MoveTowardsRate(float current, float target, float rise, float fall, float dt)
        {
            return Mathf.MoveTowards(current, target, (target > current ? rise : fall) * dt);
        }

        private void ConsumeFuel()
        {
            if (currentFuel > 0f && !IsBroken && _throttle > 0.05f)
            {
                currentFuel -= fuelConsumptionRate * Mathf.Lerp(0.4f, 1f, _throttle) * Time.deltaTime;
                if (currentFuel < 0f) currentFuel = 0f;
            }
        }

        // ---------------------------------------------------------------- Powertrain

        private float GearRatio(int gear)
        {
            if (gear < 0) return -reverseRatio;
            if (gearRatios == null || gearRatios.Length == 0) return 1f;
            return gearRatios[Mathf.Clamp(gear - 1, 0, gearRatios.Length - 1)];
        }

        private int ForwardGearCount => gearRatios != null ? Mathf.Max(1, gearRatios.Length) : 1;

        /// <summary>Engine RPM the given gear would have at the current road speed.</summary>
        private float RpmAtSpeed(int gear, float forwardSpeed)
        {
            float wheelRpm = Mathf.Abs(forwardSpeed) / (2f * Mathf.PI * Mathf.Max(0.1f, _wheelRadius)) * 60f;
            return wheelRpm * Mathf.Abs(GearRatio(gear)) * finalDriveRatio;
        }

        private void RequestShift(int gear)
        {
            if (_shiftTimer > 0f || gear == _gear) return;
            _pendingGear = gear;
            _shiftTimer = shiftTime;
            _shiftCooldown = shiftTime + 0.5f;
        }

        private void UpdateGearbox(float dt, float forwardSpeed)
        {
            if (_shiftTimer > 0f)
            {
                _shiftTimer -= dt;
                if (_shiftTimer <= 0f) _gear = _pendingGear;
                return;
            }

            if (_shiftCooldown > 0f)
            {
                _shiftCooldown -= dt;
                return;
            }

            if (_gear < 1 || GroundedWheelCount == 0) return;

            float rpm = RpmAtSpeed(_gear, forwardSpeed);
            float up = Mathf.Lerp(upshiftRpm.x, upshiftRpm.y, _throttle);
            float down = Mathf.Lerp(downshiftRpm.x, downshiftRpm.y, _throttle);

            if (_gear < ForwardGearCount && rpm > up)
            {
                RequestShift(_gear + 1);
            }
            else if (_gear > 1 && rpm < down && RpmAtSpeed(_gear - 1, forwardSpeed) < maxRpm * 0.9f)
            {
                RequestShift(_gear - 1);
            }
        }

        private void UpdateEngine(float dt, float forwardSpeed)
        {
            float target = 0f;
            if (EngineRunning)
            {
                int gear = IsShifting ? _pendingGear : _gear;
                // Clutch slip: below launch RPM the engine is allowed to rev up independently of the wheels.
                float clutchRpm = idleRpm + _throttle * (launchRpm - idleRpm);
                target = Mathf.Max(RpmAtSpeed(gear, forwardSpeed), clutchRpm);

                // Wheelspin / airborne wheels let the engine flare.
                if ((_drivenWheelsSlipping || GroundedWheelCount == 0) && _throttle > 0.1f)
                    target = Mathf.Max(target, Mathf.Lerp(target, maxRpm * 0.95f, _throttle * 0.6f));
            }

            _engineRpm = Mathf.Lerp(_engineRpm, Mathf.Min(target, maxRpm * 1.02f), dt * rpmResponse);
        }

        private void UpdateWheelTorques(float forwardSpeed)
        {
            float speed = Mathf.Abs(forwardSpeed);
            float wheelTorque = 0f;

            if (EngineRunning && !IsShifting && _throttle > 0f)
            {
                float engineTorque = torqueCurve.Evaluate(Mathf.Clamp01(_engineRpm / maxRpm)) * maxEngineTorque * _throttle * GetPowerFactor();
                if (_engineRpm >= maxRpm) engineTorque = 0f; // rev limiter

                float limitKmh = _gear < 0 ? topReverseSpeedKmh : topSpeedKmh;
                float excess = speed * 3.6f - limitKmh;
                if (excess > 0f) engineTorque *= Mathf.Clamp01(1f - excess / 5f);

                wheelTorque = engineTorque * GearRatio(_gear) * finalDriveRatio * drivetrainEfficiency;
            }

            float engineBrake = 0f;
            if (EngineRunning && !IsShifting && _throttle < 0.05f && speed > 1f)
            {
                float rpmFactor = Mathf.Clamp01((_engineRpm - idleRpm) / (maxRpm - idleRpm));
                engineBrake = engineBrakeTorque * rpmFactor * Mathf.Abs(GearRatio(_gear)) * finalDriveRatio;
            }

            int frontDriven = 0, rearDriven = 0;
            foreach (Wheel w in _wheels)
            {
                if (!w.driven) continue;
                if (w.front) frontDriven++; else rearDriven++;
            }

            float frontShare = driveType == DriveType.FrontWheelDrive ? 1f : driveType == DriveType.RearWheelDrive ? 0f : frontTorqueShare;
            int drivenCount = Mathf.Max(1, frontDriven + rearDriven);

            foreach (Wheel w in _wheels)
            {
                float axleShare = w.front ? frontShare : 1f - frontShare;
                int axleCount = w.front ? frontDriven : rearDriven;
                w.driveTorque = w.driven && axleCount > 0 ? wheelTorque * axleShare / axleCount : 0f;

                float service = _brake * maxBrakeTorque * (w.front ? brakeBias : 1f - brakeBias) * 0.5f;
                float hand = w.front ? 0f : _handbrake * handbrakeTorque;
                float engine = w.driven ? engineBrake / drivenCount : 0f;
                float hold = (!isDriven || (speed < 0.3f && _throttle < 0.05f)) ? 1500f : 0f;
                w.brakeTorque = service + hand + engine + hold;
            }
        }

        // ---------------------------------------------------------------- Steering

        private void UpdateSteering(float forwardSpeed)
        {
            float lockAngle = maxSteerAngle;
            float speed = Mathf.Abs(forwardSpeed);
            if (speedSensitiveSteering && speed > 1f)
            {
                // Angle that produces the maximum lateral acceleration the tires can deliver, plus some slip.
                float maxLateralAccel = sidewaysGrip * Physics.gravity.magnitude * 0.95f;
                float geometric = Mathf.Atan(_wheelbase * maxLateralAccel / (speed * speed)) * Mathf.Rad2Deg;
                lockAngle = Mathf.Clamp(geometric + peakSlipAngle * steerSlipAllowance, 4f, maxSteerAngle);
            }

            _currentSteerAngle = _steer * lockAngle;

            // Ackermann geometry: the inside wheel turns tighter than the outside one.
            float outerAngle = _currentSteerAngle;
            float innerAngle = _currentSteerAngle;
            if (Mathf.Abs(_currentSteerAngle) > 0.1f)
            {
                float radius = _wheelbase / Mathf.Tan(Mathf.Abs(_currentSteerAngle) * Mathf.Deg2Rad);
                innerAngle = Mathf.Sign(_currentSteerAngle) * Mathf.Min(Mathf.Atan(_wheelbase / Mathf.Max(0.1f, radius - _trackWidth)) * Mathf.Rad2Deg, Mathf.Abs(_currentSteerAngle) * 1.25f);
            }

            foreach (Wheel w in _wheels)
            {
                if (!w.front)
                {
                    w.steerAngle = 0f;
                    continue;
                }

                float side = transform.InverseTransformPoint(w.anchor.transform.position).x;
                bool isInside = side * _currentSteerAngle > 0f; // turning right: the +x wheel is inside
                w.steerAngle = isInside ? innerAngle : outerAngle;
            }
        }

        // ---------------------------------------------------------------- Suspension & tires

        private void UpdateSuspension(float dt)
        {
            Vector3 up = transform.up;
            float maxLoad = _rb.mass * Physics.gravity.magnitude * 1.5f;
            int grounded = 0;

            foreach (Wheel w in _wheels)
            {
                Vector3 origin = w.anchor.transform.position;
                w.grounded = CastGround(origin, -up, suspensionDistance + w.radius, out w.hit);

                if (w.grounded)
                {
                    w.springLength = Mathf.Clamp(w.hit.distance - w.radius, 0f, suspensionDistance);
                    w.compression = suspensionDistance - w.springLength;
                    float compressionVelocity = (w.compression - w.lastCompression) / dt;
                    float force = w.compression * suspensionSpring + compressionVelocity * suspensionDamper;
                    w.load = Mathf.Clamp(force, 0f, maxLoad);
                    _rb.AddForceAtPosition(up * w.load, origin);
                    grounded++;
                }
                else
                {
                    w.springLength = suspensionDistance;
                    w.compression = 0f;
                    w.load = 0f;
                }

                w.lastCompression = w.compression;
            }

            GroundedWheelCount = grounded;
        }

        private bool CastGround(Vector3 origin, Vector3 direction, float length, out RaycastHit best)
        {
            best = default;
            int count = Physics.RaycastNonAlloc(origin, direction, _hitBuffer, length, groundLayers, QueryTriggerInteraction.Ignore);
            float minCos = Mathf.Cos(maxGroundAngle * Mathf.Deg2Rad);
            bool found = false;

            for (int i = 0; i < count; i++)
            {
                RaycastHit h = _hitBuffer[i];
                if (h.collider.attachedRigidbody == _rb) continue;
                if (Vector3.Dot(h.normal, -direction) < minCos) continue; // a wall, not ground
                if (!found || h.distance < best.distance)
                {
                    best = h;
                    found = true;
                }
            }

            return found;
        }

        private void ApplyAntiRollBars()
        {
            ApplyAntiRoll(FindWheel(frontLeftCollider), FindWheel(frontRightCollider));
            ApplyAntiRoll(FindWheel(rearLeftCollider), FindWheel(rearRightCollider));
        }

        private void ApplyAntiRoll(Wheel left, Wheel right)
        {
            if (left == null || right == null || (!left.grounded && !right.grounded)) return;

            float force = (left.compression - right.compression) * antiRollForce;
            Vector3 up = transform.up;

            if (left.grounded)
            {
                _rb.AddForceAtPosition(up * force, left.anchor.transform.position);
                left.load = Mathf.Max(0f, left.load + force);
            }

            if (right.grounded)
            {
                _rb.AddForceAtPosition(up * -force, right.anchor.transform.position);
                right.load = Mathf.Max(0f, right.load - force);
            }
        }

        private Wheel FindWheel(WheelCollider wc)
        {
            if (wc == null) return null;
            foreach (Wheel w in _wheels)
                if (w.anchor == wc) return w;
            return null;
        }

        private void ApplyTireForces(float dt)
        {
            Vector3 up = transform.up;
            Vector3 com = _rb.worldCenterOfMass;
            float massPerWheel = _rb.mass / _wheels.Length;
            bool drivenSlipping = false;

            foreach (Wheel w in _wheels)
            {
                if (!w.grounded || w.load <= 0f)
                {
                    // Free wheel: spins up under throttle, slowly stops otherwise.
                    float freeSpin = w.driven && _throttle > 0.1f ? 25f * _throttle : 0f;
                    w.spin = Mathf.Lerp(w.spin, freeSpin, dt * 2f);
                    w.slipping = false;
                    continue;
                }

                Vector3 normal = w.hit.normal;
                Vector3 forward = Vector3.ProjectOnPlane(Quaternion.AngleAxis(w.steerAngle, up) * transform.forward, normal).normalized;
                Vector3 side = Vector3.Cross(normal, forward);
                Vector3 contact = w.hit.point;
                Vector3 velocity = _rb.GetPointVelocity(contact);
                if (w.hit.rigidbody != null) velocity -= w.hit.rigidbody.GetPointVelocity(contact);

                float vLong = Vector3.Dot(velocity, forward);
                float vLat = Vector3.Dot(velocity, side);

                float muLong = forwardGrip;
                float muLat = sidewaysGrip * (w.front ? 1f : Mathf.Lerp(1f, handbrakeGripMultiplier, _handbrake));
                float maxLong = muLong * w.load;
                float maxLat = muLat * w.load;

                // Longitudinal: drive force plus brakes that never push the wheel backwards past a stop.
                float fx = w.driveTorque / w.radius;
                float brakeForce = w.brakeTorque / w.radius;
                if (brakeForce > 0f)
                {
                    float stopForce = Mathf.Abs(vLong) * massPerWheel / dt;
                    fx -= Mathf.Sign(vLong) * Mathf.Min(brakeForce, stopForce);
                }

                // Lateral: slip-angle tire curve, rising to a peak and falling to slideGrip beyond it.
                float slipAngle = Mathf.Atan2(Mathf.Abs(vLat), Mathf.Max(Mathf.Abs(vLong), 0.5f)) * Mathf.Rad2Deg;
                float x = slipAngle / Mathf.Max(0.5f, peakSlipAngle);
                float curve = x < 1f ? x * (2f - x) : Mathf.Lerp(1f, slideGrip, Mathf.Clamp01((x - 1f) * 0.5f));
                float fy = Mathf.Clamp(-vLat * massPerWheel / dt, -maxLat * curve, maxLat * curve);

                // Friction circle: total tire force can't exceed what the load allows.
                bool braking = brakeForce > 0f && fx * vLong < 0f;
                bool handbraked = !w.front && _handbrake > 0.1f;
                float nx = maxLong > 0f ? fx / maxLong : 0f;
                float ny = maxLat > 0f ? fy / maxLat : 0f;
                float usage = Mathf.Sqrt(nx * nx + ny * ny);
                w.slipping = false;

                if (usage > 1f)
                {
                    w.slipping = true;
                    if (braking && (handbraked || !abs))
                    {
                        // Locked wheel: slides, and loses most of its side grip.
                        fx = -Mathf.Sign(vLong) * Mathf.Min(Mathf.Abs(fx), maxLong * slideGrip);
                        fy *= 0.35f;
                    }
                    else if (braking || w.front)
                    {
                        // ABS / front traction control: keep the side force (steering), use whatever grip is left
                        // for braking or drive, so holding the throttle mid-corner doesn't wash the nose wide.
                        if (!braking && w.driven && _throttle > 0.1f) drivenSlipping = true;
                        // ABS: keep the side force, use whatever grip is left for braking.
                        float lateralUse = Mathf.Min(Mathf.Abs(ny), 0.95f);
                        fy = Mathf.Sign(fy) * lateralUse * maxLat;
                        fx = Mathf.Sign(fx) * Mathf.Min(Mathf.Abs(fx), maxLong * Mathf.Sqrt(1f - lateralUse * lateralUse));
                    }
                    else
                    {
                        fx /= usage;
                        fy /= usage;
                        if (w.driven && _throttle > 0.1f) drivenSlipping = true;
                    }
                }

                // Raise the application point toward the centre of mass to control body roll.
                float comHeight = Vector3.Dot(com - contact, up);
                Vector3 applyPoint = contact + up * (comHeight * tireForceHeight);
                _rb.AddForceAtPosition(forward * fx + side * fy, applyPoint);
                // Visual wheel spin (rad/s); spinning faster than the road when the tire is spinning.
                float roll = vLong / w.radius;
                if (braking && w.slipping && (handbraked || !abs)) roll = 0f;
                else if (w.slipping && w.driven && _throttle > 0.1f) roll += Mathf.Sign(GearRatio(_gear)) * 15f * _throttle;
                w.spin = roll;
            }

            _drivenWheelsSlipping = drivenSlipping;
        }

        private void ApplyResistance()
        {
            Vector3 velocity = _rb.linearVelocity;
            float speed = velocity.magnitude;
            if (speed < 0.01f) return;

            // Aerodynamic drag grows with the square of speed.
            _rb.AddForce(-velocity * (speed * airDragCoefficient), ForceMode.Force);

            if (GroundedWheelCount > 0)
            {
                float rolling = rollingResistance * _rb.mass * Physics.gravity.magnitude * Mathf.Clamp01(speed);
                Vector3 planar = Vector3.ProjectOnPlane(velocity, transform.up);
                if (planar.sqrMagnitude > 0.0001f)
                    _rb.AddForce(-planar.normalized * rolling, ForceMode.Force);

                _rb.AddForce(-transform.up * (downforceCoefficient * speed * speed), ForceMode.Force);
            }
        }

        private void ApplyStabilityAssist(float forwardSpeed)
        {
            if (_handbrake > 0.05f) return;
            float speed = Mathf.Abs(forwardSpeed);
            if (speed < 5f || GroundedWheelCount < 3) return;

            // Yaw rate the steering asks for, capped at what the tires can actually hold at this speed.
            float maxYaw = sidewaysGrip * Physics.gravity.magnitude / speed;
            float targetYaw = Mathf.Clamp(forwardSpeed * Mathf.Tan(_currentSteerAngle * Mathf.Deg2Rad) / _wheelbase, -maxYaw, maxYaw);
            float actualYaw = Vector3.Dot(_rb.angularVelocity, transform.up);
            float error = targetYaw - actualYaw;

            bool oversteer = Mathf.Abs(actualYaw) > Mathf.Abs(targetYaw) + 0.05f && Mathf.Sign(error) != Mathf.Sign(targetYaw);
            float gain = oversteer ? stabilityAssist : (Mathf.Abs(_steer) > 0.1f ? turnAssist : 0f);
            if (gain <= 0f) return;

            _rb.AddTorque(transform.up * (error * gain * 4f), ForceMode.Acceleration);
        }

        private void ApplyAirGravity()
        {
            if (airGravityMultiplier <= 1f || GroundedWheelCount > 0) return;
            _rb.AddForce(Physics.gravity * (airGravityMultiplier - 1f), ForceMode.Acceleration);
        }

        private void UpdateFlipRecovery(float dt)
        {
            bool stuck = autoFlipRecovery && isDriven
                         && Vector3.Dot(transform.up, Vector3.up) < 0.3f
                         && _rb.linearVelocity.magnitude < 2f;
            _flipTimer = stuck ? _flipTimer + dt : 0f;
            if (_flipTimer < flipRecoveryDelay) return;

            _flipTimer = 0f;
            Vector3 heading = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
            if (heading.sqrMagnitude < 0.01f) heading = Vector3.ProjectOnPlane(transform.up, Vector3.up);
            if (heading.sqrMagnitude < 0.01f) heading = Vector3.forward;

            _rb.linearVelocity = Vector3.zero;
            _rb.angularVelocity = Vector3.zero;
            _rb.position += Vector3.up * 1.5f;
            _rb.rotation = Quaternion.LookRotation(heading.normalized, Vector3.up);
        }

        // ---------------------------------------------------------------- Visuals

        private void UpdateWheelVisuals()
        {
            float dt = Time.deltaTime;
            foreach (Wheel w in _wheels)
            {
                if (w.mesh == null) continue;

                Transform anchor = w.anchor.transform;
                w.meshSpinAngle += w.spin * Mathf.Rad2Deg * dt;
                w.meshSpinAngle %= 360f;

                w.mesh.position = anchor.position - anchor.up * w.springLength;
                w.mesh.rotation = anchor.rotation * Quaternion.Euler(0f, w.steerAngle, 0f) * Quaternion.Euler(w.meshSpinAngle, 0f, 0f);
            }
        }

        public void SetDriving(bool driving)
        {
            isDriven = driving;
            _throttleRaw = 0f;
            _brakeRaw = 0f;
            _steerRaw = 0f;
            _throttle = 0f;
            _brake = 0f;
            _handbrakeRaw = !isDriven;
            _hardBrakeRaw = false;
            _handbrake = isDriven ? 0f : 1f;

            if (isDriven)
            {
                _gear = 1;
                _pendingGear = 1;
                _shiftTimer = 0f;
                _engineRpm = idleRpm;
            }
        }

        [ContextMenu("Auto Setup Vehicle")]
        public void AutoSetupVehicle()
        {
            Debug.Log("[VehicleController] Starting auto vehicle setup...");

            // 1. Rigidbody configuration
            _rb = GetComponent<Rigidbody>();
            if (_rb == null) _rb = gameObject.AddComponent<Rigidbody>();
            _rb.mass = 1800f;
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

            // 4. Create or fetch wheel anchors
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

            // 7. Camera Follow Target with Mouse Orbit
            Transform camTarget = transform.Find("VehicleCameraTarget");
            if (camTarget == null)
            {
                GameObject camTargetGo = new GameObject("VehicleCameraTarget");
                camTargetGo.transform.SetParent(transform, false);
                camTargetGo.transform.localPosition = new Vector3(0f, 1.4f, -0.5f);
                camTarget = camTargetGo.transform;
            }

            if (camTarget.GetComponent<VehicleCameraOrbit>() == null)
            {
                var orbit = camTarget.gameObject.AddComponent<VehicleCameraOrbit>();
                orbit.parentVehicle = this;
            }

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

            if (wc != null) wc.radius = 0.38f;
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
