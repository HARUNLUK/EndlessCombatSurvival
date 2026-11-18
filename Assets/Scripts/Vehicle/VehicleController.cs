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
        [Tooltip("Fuel burnt per second at full throttle when the vehicle hardly moves (stuck, spinning wheels). On the move the distance decides: Fuel Per Km")]
        public float fuelConsumptionRate = 0.1f;
        [Tooltip("Fuel burnt per kilometre driven. A full tank lasts maxFuel / this km (100 / 28 = about 3.5 km), so the road cannot be driven without stopping for fuel")]
        public float fuelPerKm = 9f;
        [Tooltip("Fuel burnt per second while the engine idles")]
        public float idleFuelPerSecond = 0.01f;

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
        private float _stuckTimer;
        private const float GroundCastLift = 0.4f;

        /// <summary>Stuck help: full gas, engine running, not moving for StuckHelpDelay seconds -> a small hop forward.</summary>
        public static bool StuckHelp = true;
        public static float StuckHelpDelay = 1.5f;
        public static int StuckHelpCount { get; private set; }

        /// <summary>Every enabled vehicle (CharacterBodyColliders turns off body part contacts against these).</summary>
        public static readonly System.Collections.Generic.List<VehicleController> All = new System.Collections.Generic.List<VehicleController>();

        private void OnEnable()
        {
            if (!All.Contains(this)) All.Add(this);
        }

        private void OnDisable() => All.Remove(this);

        /// <summary>
        /// Living characters are never ground for the wheels and never "stuck" terrain (17 Nov: people standing at the
        /// doors lifted and tipped the truck). A capsule or an animated (kinematic) body part of a character.
        /// </summary>
        /// <summary>People are never wheel ground and never stuck terrain (off only for the AI playtest's before/after comparison).</summary>
        public static bool StuckHelpIgnoresPeople = true;

        public static bool IsLivingCharacterCollider(Collider c)
        {
            if (c == null || !StuckHelpIgnoresPeople) return false;
            if (c is CharacterController) return true;
            var rb = c.attachedRigidbody;
            return rb != null && rb.isKinematic && c.GetComponentInParent<CharacterBodyColliders>() != null;
        }

        private static readonly Collider[] s_blockBuffer = new Collider[32];

        /// <summary>A person stands against the body (within 0.8 m): the truck is held by people, not stuck on terrain.</summary>
        private bool PeopleAgainstBody()
        {
            BoxCollider body = GetComponent<BoxCollider>();
            if (body == null) return false;
            Vector3 s = transform.lossyScale;
            Vector3 half = Vector3.Scale(body.size * 0.5f, new Vector3(Mathf.Abs(s.x), Mathf.Abs(s.y), Mathf.Abs(s.z))) + Vector3.one * 0.8f;
            int n = Physics.OverlapBoxNonAlloc(transform.TransformPoint(body.center), half, s_blockBuffer, transform.rotation, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var c = s_blockBuffer[i];
                if (c == null || c.transform.IsChildOf(transform) || !(c is CharacterController) || !c.enabled) continue;
                if (c.CompareTag("Player")) continue;
                return true;
            }
            return false;
        }

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

        [Header("Parts (set by VehicleParts, Paket 6)")]
        [Tooltip("Engine ignition. Starts true so the vehicle drives without the parts system; VehicleParts turns it off on exit and asks for a start")]
        public bool engineOn = true;
        [Tooltip("Engine power multiplier from parts (overheating, damaged radiator)")]
        public float partsPowerFactor = 1f;
        [Tooltip("Speed cap from parts in km/h (flat tyre). 0 = none")]
        public float partsTopSpeedKmh = 0f;
        [Tooltip("Grip multiplier per wheel (FL, FR, RL, RR): flat tyre, snow chains")]
        public float[] wheelGripFactor = { 1f, 1f, 1f, 1f };
        [Tooltip("Rolling drag torque per wheel (FL, FR, RL, RR): a flat tyre drags the car to its side")]
        public float[] wheelDragTorque = { 0f, 0f, 0f, 0f };

        /// <summary>Damage taken (amount after multipliers, is a bullet, world point, impact speed m/s).</summary>
        public event System.Action<float, bool, Vector3, float> Damaged;

        private bool EngineRunning => isDriven && engineOn && !IsBroken && currentFuel > 0f;
        public bool IsEngineRunning => EngineRunning;

        /// <summary>Wheel i (FL, FR, RL, RR) is on the ground and on terrain (not on the road).</summary>
        public bool WheelOffRoad(int i)
        {
            if (i < 0 || i >= _wheels.Length || !_wheels[i].grounded) return false;
            return _wheels[i].hit.collider is TerrainCollider;
        }

        /// <summary>World position of wheel i (FL, FR, RL, RR), or the vehicle position.</summary>
        public Vector3 WheelPosition(int i) => i >= 0 && i < _wheels.Length ? _wheels[i].anchor.transform.position : transform.position;

        public int WheelCount => _wheels.Length;

        public void TakeDamage(DamageInfo info)
        {
            // Enemy_AI_Deep_Plan.md 18.2 / 18.3: the source is kept for the driver hit; enemy damage has a floor
            EndlessCombat.AI.VehicleThreat.LastBullet = info;
            EndlessCombat.AI.VehicleThreat.HasLastBullet = true;
            float amount = info.Amount * bulletDamageMultiplier;
            float applied = EndlessCombat.AI.VehicleThreat.ClampEnemyDamage(this, info, amount);
            ApplyDamage(applied);
            Damaged?.Invoke(amount, true, info.Point, 0f);
            EndlessCombat.AI.VehicleThreat.HasLastBullet = false;
        }

        public void TakeDamage(float damage, Vector3 hitPoint, Vector3 hitNormal)
        {
            float amount = damage * bulletDamageMultiplier;
            ApplyDamage(amount);
            Damaged?.Invoke(amount, true, hitPoint, 0f);
        }

        public void ApplyDamage(float amount)
        {
            if (amount <= 0f || IsBroken) return;
            currentHealth = Mathf.Max(0f, currentHealth - amount);
        }

        /// <summary>Non-bullet impact damage with the usual Damaged event (parts react to it): ramming people, animals, roadblocks.</summary>
        public void ApplyImpact(float amount, Vector3 point, float impactSpeedMs)
        {
            if (amount <= 0f || IsBroken) return;
            ApplyDamage(amount);
            EndlessCombat.Audio.GameSound.Play(EndlessCombat.Audio.SoundId.VehicleImpact, point, Mathf.Clamp01(impactSpeedMs / 20f), null, isDriven);
            Damaged?.Invoke(amount, false, point, impactSpeedMs);
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
            float amount = (impact - collisionDamageThreshold) * collisionDamageScale;
            ApplyDamage(amount);
            Vector3 point = collision.contactCount > 0 ? collision.GetContact(0).point : transform.position;
            Damaged?.Invoke(amount, false, point, impact);
        }

        private float GetPowerFactor()
        {
            if (IsBroken) return 0f;
            float parts = Mathf.Clamp01(partsPowerFactor);
            float ratio = HealthRatio;
            if (weakEngineHealthRatio <= 0f || ratio >= weakEngineHealthRatio) return parts;
            return Mathf.Lerp(0.5f, 1f, ratio / weakEngineHealthRatio) * parts;
        }

        private void Awake()
        {
            _rb = GetComponent<Rigidbody>();
            SetupPhysicsProperties();

            if (GetComponent<VehicleModifications>() == null)
            {
                gameObject.AddComponent<VehicleModifications>();
            }

            if (GetComponent<VehicleRam>() == null)
            {
                gameObject.AddComponent<VehicleRam>();
            }

            RegisterTuning();
        }

        private void Start() => FindTiltParts();

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

            // Body tilt first: the wheels are placed in world space right after, so they do not tilt with it
            UpdateBodyTilt();
            UpdateWheelVisuals();
        }

        private void FixedUpdate()
        {
            if (_rb == null || _wheels.Length == 0) return;

            float dt = Time.fixedDeltaTime;
            float forwardSpeed = ForwardSpeed;

            if (Model == DriveModel.Arcade)
            {
                UpdateArcadeControls(dt, forwardSpeed);
                UpdateSuspension(dt);
                ApplyAntiRollBars();
                ApplyArcadeDrive(dt, forwardSpeed);
                ApplyAirGravity();
                UpdateFlipRecovery(dt);
                UpdateStuckHelp(dt, forwardSpeed);
                MeasureBodyAcceleration(dt);
                return;
            }

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
            UpdateStuckHelp(dt, forwardSpeed);
        }

        /// <summary>
        /// A parked truck sometimes would not move after a sleep or a repair (engine running, gas held, nothing)
        /// until the player pushed it. Wake the body; after StuckHelpDelay s of full gas without motion, a small
        /// hop in the driving direction frees it, like the push did.
        /// </summary>
        private void UpdateStuckHelp(float dt, float forwardSpeed)
        {
            bool pushing = isDriven && EngineRunning && (_gear < 0 ? _brakeRaw : _throttleRaw) > 0.6f;
            if (!StuckHelp || !pushing || Mathf.Abs(forwardSpeed) > 0.4f || Vector3.Dot(transform.up, Vector3.up) < 0.5f)
            {
                _stuckTimer = 0f;
                return;
            }
            if (_rb.IsSleeping()) _rb.WakeUp();
            _stuckTimer += dt;
            if (_stuckTimer < StuckHelpDelay) return;
            // Held by people at the doors (car assault): no hop, the hop threw the truck into the air among them
            if (StuckHelpIgnoresPeople && PeopleAgainstBody())
            {
                _stuckTimer = 0f;
                return;
            }
            _stuckTimer = -1f; // one more try only after another full delay
            StuckHelpCount++;
            Vector3 dir = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized * (_gear < 0 ? -1f : 1f);
            _rb.AddForce(Vector3.up * 2.2f + dir * 1.5f, ForceMode.VelocityChange);
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
            if (currentFuel <= 0f || !EngineRunning) return;

            // Distance based: what matters is how far the tank takes you, not how long the pedal is down.
            // Idling costs a little; revving without moving (stuck) costs the old per-second rate.
            float dt = Time.deltaTime;
            float burn = idleFuelPerSecond * dt;
            if (_throttle > 0.05f)
            {
                float metres = _rb != null ? _rb.linearVelocity.magnitude * dt : 0f;
                float byDistance = fuelPerKm * metres / 1000f * Mathf.Lerp(0.85f, 1.15f, _throttle);
                float byRevving = fuelConsumptionRate * 0.5f * _throttle * dt;
                burn += Mathf.Max(byDistance, byRevving);
            }
            currentFuel = Mathf.Max(0f, currentFuel - burn);
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
                if (partsTopSpeedKmh > 0f) limitKmh = Mathf.Min(limitKmh, partsTopSpeedKmh);
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

            for (int i = 0; i < _wheels.Length; i++)
            {
                Wheel w = _wheels[i];
                float axleShare = w.front ? frontShare : 1f - frontShare;
                int axleCount = w.front ? frontDriven : rearDriven;
                w.driveTorque = w.driven && axleCount > 0 ? wheelTorque * axleShare / axleCount : 0f;

                float service = _brake * maxBrakeTorque * (w.front ? brakeBias : 1f - brakeBias) * 0.5f;
                float hand = w.front ? 0f : _handbrake * handbrakeTorque;
                float engine = w.driven ? engineBrake / drivenCount : 0f;
                float hold = (!isDriven || (speed < 0.3f && _throttle < 0.05f)) ? 1500f : 0f;
                float drag = wheelDragTorque != null && i < wheelDragTorque.Length && speed > 0.5f ? wheelDragTorque[i] : 0f;
                w.brakeTorque = service + hand + engine + hold + drag;
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
                // The ray starts a little above the wheel anchor: a truck that settled low (its anchor at or under the
                // ground, e.g. parked a long time on a soft spot) used to start the ray inside the ground, find
                // nothing, lose every wheel and sit there with the engine running until pushed.
                w.grounded = CastGround(origin + up * GroundCastLift, -up, suspensionDistance + w.radius + GroundCastLift, out w.hit);

                if (w.grounded)
                {
                    w.springLength = Mathf.Clamp(w.hit.distance - GroundCastLift - w.radius, 0f, suspensionDistance);
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
                if (IsLivingCharacterCollider(h.collider)) continue; // a wheel never stands on a person
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

            for (int i = 0; i < _wheels.Length; i++)
            {
                Wheel w = _wheels[i];
                float gripFactor = wheelGripFactor != null && i < wheelGripFactor.Length ? Mathf.Max(0.05f, wheelGripFactor[i]) : 1f;
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

                float muLong = forwardGrip * gripFactor;
                float muLat = sidewaysGrip * gripFactor * (w.front ? 1f : Mathf.Lerp(1f, handbrakeGripMultiplier, _handbrake));
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

        // ---------------------------------------------------------------- Arcade drive (Core_Feel_Plan stage 3)
        //
        // Suspension stays (raycast springs keep the truck on the ground). Everything else is direct:
        //   push      one curve: acceleration by speed, high from a standstill, zero at top speed. No gearbox.
        //   brake     one number (m/s2). Stopped + LT: reverse.
        //   steering  stick dead zone + curve, then a target turn rate by speed (sharp slow, gentle fast).
        //   grip      one number: how much of the sideways speed is killed per second. Off-road and handbrake scale it.
        //   body      squat / dive / lean are small tilts of the body visual, not physics (no flipping).
        // Parts and mods are read through the same fields (partsPowerFactor, partsTopSpeedKmh, wheelGripFactor,
        // wheelDragTorque). The old model is kept behind the panel choice "Sürüş modeli" for comparison.

        public enum DriveModel { Arcade = 0, Old = 1 }
        private const float StockEngineTorque = 380f;
        private const float StockSidewaysGrip = 1.7f;

        // ---- tuning (panel page "Araç"), shared by all vehicles ----
        public static DriveModel Model = DriveModel.Arcade;
        public static float ArcadeLaunchAccel = 6f;       // m/s2 from a standstill
        public static float ArcadeTopSpeedKmh = 130f;
        public static float ArcadeCurve = 1.6f;           // higher = keeps pulling closer to top speed
        public static float ArcadeBrake = 12f;            // m/s2
        public static float ArcadeCoast = 1.2f;           // m/s2 off the pedals (engine braking)
        public static float ArcadeTurnSlow = 110f;        // deg/s at low speed, full lock
        public static float ArcadeTurnFast = 35f;         // deg/s at ArcadeTurnFadeKmh and above
        public static float ArcadeTurnFadeKmh = 90f;
        public static float ArcadeSteerResponse = 0.12f;  // s
        public static float ArcadeGrip = 6f;              // 1/s
        public static float ArcadeOffroadGrip = 0.7f;
        public static float ArcadeOffroadAccel = 0.75f;
        public static float ArcadeHandbrakeGrip = 0.3f;
        public static float ArcadeStickDeadzone = 0.12f;
        public static float ArcadeStickCurve = 1.6f;      // 1 = linear, higher = finer around the centre
        public static float BodyTiltDegrees = 4f;

        /// <summary>The vehicle the player drives now (panel readouts).</summary>
        public static VehicleController Driven { get; private set; }

        private static bool s_tuningRegistered;
        private float _arcadeSteer;     // smoothed, after the stick curve
        private bool _arcadeSliding;
        private float _offRoadShare;
        private Vector3 _lastVelocity;
        private Vector3 _bodyAccelLocal;
        private Vector2 _tilt;          // x = pitch, y = roll (degrees, smoothed)
        private Transform[] _tiltParts = new Transform[0];
        private Vector3[] _tiltBasePos = new Vector3[0];
        private Quaternion[] _tiltBaseRot = new Quaternion[0];

        public bool IsSliding => Model == DriveModel.Arcade ? _arcadeSliding : _drivenWheelsSlipping;

        private static void RegisterTuning()
        {
            if (s_tuningRegistered) return;
            s_tuningRegistered = true;
            const string P = "Araç";
            EndlessCombat.Tuning.TuningPanel.RegisterChoice(P, "Sürüş modeli", new[] { "Arcade", "Eski" }, () => (int)Model, v => Model = (DriveModel)v);
            EndlessCombat.Tuning.TuningPanel.Register(P, "Kalkış ivmesi (m/sn²)", () => ArcadeLaunchAccel, v => ArcadeLaunchAccel = v, 1f, 20f, 0.25f);
            EndlessCombat.Tuning.TuningPanel.Register(P, "Azami hız (km/s)", () => ArcadeTopSpeedKmh, v => ArcadeTopSpeedKmh = v, 40f, 220f, 5f);
            EndlessCombat.Tuning.TuningPanel.Register(P, "Eğri sertliği", () => ArcadeCurve, v => ArcadeCurve = v, 0.5f, 5f, 0.1f);
            EndlessCombat.Tuning.TuningPanel.Register(P, "Fren (m/sn²)", () => ArcadeBrake, v => ArcadeBrake = v, 2f, 30f, 0.5f);
            EndlessCombat.Tuning.TuningPanel.Register(P, "Pedalsız yavaşlama (m/sn²)", () => ArcadeCoast, v => ArcadeCoast = v, 0f, 6f, 0.1f);
            EndlessCombat.Tuning.TuningPanel.Register(P, "Dönüş keskinliği yavaş (°/sn)", () => ArcadeTurnSlow, v => ArcadeTurnSlow = v, 20f, 250f, 5f);
            EndlessCombat.Tuning.TuningPanel.Register(P, "Dönüş keskinliği hızlı (°/sn)", () => ArcadeTurnFast, v => ArcadeTurnFast = v, 5f, 150f, 1f);
            EndlessCombat.Tuning.TuningPanel.Register(P, "Hızlı dönüş başladığı hız (km/s)", () => ArcadeTurnFadeKmh, v => ArcadeTurnFadeKmh = v, 20f, 200f, 5f);
            EndlessCombat.Tuning.TuningPanel.Register(P, "Direksiyon tepki süresi (sn)", () => ArcadeSteerResponse, v => ArcadeSteerResponse = v, 0f, 0.6f, 0.01f);
            EndlessCombat.Tuning.TuningPanel.Register(P, "Tutuş", () => ArcadeGrip, v => ArcadeGrip = v, 0.5f, 25f, 0.25f);
            EndlessCombat.Tuning.TuningPanel.Register(P, "Arazi tutuş çarpanı", () => ArcadeOffroadGrip, v => ArcadeOffroadGrip = v, 0.1f, 1.5f, 0.05f);
            EndlessCombat.Tuning.TuningPanel.Register(P, "Arazi ivme çarpanı", () => ArcadeOffroadAccel, v => ArcadeOffroadAccel = v, 0.1f, 1.5f, 0.05f);
            EndlessCombat.Tuning.TuningPanel.Register(P, "El freni tutuş çarpanı", () => ArcadeHandbrakeGrip, v => ArcadeHandbrakeGrip = v, 0.05f, 1f, 0.05f);
            EndlessCombat.Tuning.TuningPanel.Register(P, "Çubuk ölü bölge", () => ArcadeStickDeadzone, v => ArcadeStickDeadzone = v, 0f, 0.4f, 0.01f);
            EndlessCombat.Tuning.TuningPanel.Register(P, "Çubuk eğrisi", () => ArcadeStickCurve, v => ArcadeStickCurve = v, 1f, 3f, 0.05f);
            EndlessCombat.Tuning.TuningPanel.Register(P, "Gövde eğimi (°)", () => BodyTiltDegrees, v => BodyTiltDegrees = v, 0f, 12f, 0.5f);
            EndlessCombat.Tuning.TuningPanel.RegisterToggle(P, "Takılma kurtarma (tam gazda kıpırdamazsa zıplat)", () => StuckHelp, v => StuckHelp = v);
            EndlessCombat.Tuning.TuningPanel.Register(P, "Takılma bekleme (sn)", () => StuckHelpDelay, v => StuckHelpDelay = v, 0.5f, 5f, 0.1f);

            EndlessCombat.Tuning.TuningPanel.RegisterReadout(P, "Hız", () => Driven == null ? "-" : Driven.CurrentSpeedKmh.ToString("0") + " km/s");
            EndlessCombat.Tuning.TuningPanel.RegisterReadout(P, "Vites / devir", () => Driven == null ? "-" : Driven.GearLabel + " / " + Driven.EngineRpm.ToString("0"));
            EndlessCombat.Tuning.TuningPanel.RegisterReadout(P, "Gaz", () => Driven == null ? "-" : Mathf.RoundToInt(Driven.Throttle * 100f) + "%");
            EndlessCombat.Tuning.TuningPanel.RegisterReadout(P, "Güç çarpanı (parça / hasar)", () => Driven == null ? "-" : Driven.GetPowerFactor().ToString("0.00"));
            EndlessCombat.Tuning.TuningPanel.RegisterReadout(P, "Parça hız sınırı", () => Driven == null ? "-" : Driven.partsTopSpeedKmh > 0f ? Driven.partsTopSpeedKmh.ToString("0") + " km/s" : "yok");
            EndlessCombat.Tuning.TuningPanel.RegisterReadout(P, "Yere basan teker", () => Driven == null ? "-" : Driven.GroundedWheelCount + " / " + Driven.WheelCount);
            EndlessCombat.Tuning.TuningPanel.RegisterReadout(P, "Yol dışı teker oranı", () => Driven == null ? "-" : Mathf.RoundToInt(Driven._offRoadShare * 100f) + "%");
            EndlessCombat.Tuning.TuningPanel.RegisterReadout(P, "Kayıyor", () => Driven == null ? "-" : Driven.IsSliding ? "evet" : "hayır");
            EndlessCombat.Tuning.TuningPanel.RegisterReadout(P, "Eğilen gövde parçası", () => Driven == null ? "-" : Driven._tiltParts.Length.ToString());
            EndlessCombat.Tuning.TuningPanel.RegisterReadout(P, "Takılma kurtarma sayısı", () => StuckHelpCount.ToString());
            EndlessCombat.Tuning.TuningPanel.RegisterReadout(P, "Gövde uyuyor / kinematik", () => Driven == null || Driven._rb == null ? "-" : (Driven._rb.IsSleeping() ? "uyuyor" : "uyanık") + " / " + (Driven._rb.isKinematic ? "EVET" : "hayır"));
        }

        /// <summary>Stick dead zone and curve: small movements near the centre steer finely, the rim is full lock.</summary>
        private static float ShapeStick(float x)
        {
            float a = Mathf.Abs(x);
            float dz = Mathf.Clamp(ArcadeStickDeadzone, 0f, 0.9f);
            if (a <= dz) return 0f;
            float t = Mathf.Clamp01((a - dz) / (1f - dz));
            return Mathf.Sign(x) * Mathf.Pow(t, Mathf.Max(1f, ArcadeStickCurve));
        }

        private void UpdateArcadeControls(float dt, float forwardSpeed)
        {
            _shiftTimer = 0f;
            // Stopped (or nearly) + only the brake: reverse. Only the gas while rolling back slowly: forward again.
            if (_gear >= 0)
            {
                if (_brakeRaw > 0.1f && _throttleRaw < 0.1f && forwardSpeed < ReverseSwitchSpeed && isDriven) _gear = -1;
            }
            else if (_throttleRaw > 0.1f && _brakeRaw < 0.1f && forwardSpeed > -ReverseSwitchSpeed) _gear = 1;

            float accelPedal = _gear < 0 ? _brakeRaw : _throttleRaw;
            float brakePedal = _gear < 0 ? _throttleRaw : _brakeRaw;
            if (_hardBrakeRaw) brakePedal = 1f;
            if (!EngineRunning) accelPedal = 0f;

            _throttle = MoveTowardsRate(_throttle, accelPedal, throttleRiseSpeed * 2f, throttleFallSpeed * 2f, dt);
            _brake = MoveTowardsRate(_brake, brakePedal, brakeRiseSpeed * 2f, brakeFallSpeed, dt);
            _handbrake = Mathf.MoveTowards(_handbrake, _handbrakeRaw ? 1f : 0f, dt * 8f);

            float target = ShapeStick(_steerRaw);
            float k = ArcadeSteerResponse > 0.001f ? 1f - Mathf.Exp(-dt / ArcadeSteerResponse) : 1f;
            _arcadeSteer = Mathf.Lerp(_arcadeSteer, target, k);
            _steer = _arcadeSteer;
        }

        private void ApplyArcadeDrive(float dt, float forwardSpeed)
        {
            int grounded = 0, offRoad = 0;
            Vector3 normal = Vector3.zero;
            float gripFactor = 0f;
            for (int i = 0; i < _wheels.Length; i++)
            {
                if (!_wheels[i].grounded) continue;
                grounded++;
                normal += _wheels[i].hit.normal;
                if (_wheels[i].hit.collider is TerrainCollider) offRoad++;
                gripFactor += wheelGripFactor != null && i < wheelGripFactor.Length ? Mathf.Max(0.05f, wheelGripFactor[i]) : 1f;
            }
            float groundShare = _wheels.Length > 0 ? grounded / (float)_wheels.Length : 0f;
            _offRoadShare = grounded > 0 ? offRoad / (float)grounded : 0f;
            gripFactor = grounded > 0 ? gripFactor / grounded : 1f;

            float speedAbs = Mathf.Abs(forwardSpeed);
            float speedT = Mathf.Clamp01(speedAbs * 3.6f / Mathf.Max(10f, ArcadeTurnFadeKmh));
            UpdateArcadeDisplay(dt, forwardSpeed);

            // Wheel visuals: steer angle and spin from the road speed
            float visualLock = maxSteerAngle * Mathf.Lerp(1f, 0.35f, speedT);
            foreach (Wheel w in _wheels)
            {
                w.steerAngle = w.front ? _steer * visualLock : 0f;
                w.spin = forwardSpeed / Mathf.Max(0.1f, w.radius);
                w.driveTorque = 0f;
                w.brakeTorque = 0f;
            }

            if (grounded == 0)
            {
                _arcadeSliding = false;
                return;
            }

            normal.Normalize();
            Vector3 fwd = Vector3.ProjectOnPlane(transform.forward, normal).normalized;
            Vector3 right = Vector3.Cross(normal, fwd);
            Vector3 v = _rb.linearVelocity;
            float vLong = Vector3.Dot(v, fwd);
            float vLat = Vector3.Dot(v, right);
            float offAccel = Mathf.Lerp(1f, ArcadeOffroadAccel, _offRoadShare);
            float offGrip = Mathf.Lerp(1f, ArcadeOffroadGrip, _offRoadShare);

            // ---- push: one curve over speed
            float dir = _gear < 0 ? -1f : 1f;
            float topKmh = _gear < 0 ? topReverseSpeedKmh : ArcadeTopSpeedKmh;
            if (partsTopSpeedKmh > 0f) topKmh = Mathf.Min(topKmh, partsTopSpeedKmh);
            float top = Mathf.Max(1f, topKmh / 3.6f);
            float along = vLong * dir; // speed in the driving direction
            if (_throttle > 0.01f && EngineRunning)
            {
                float x = Mathf.Clamp01(along / top);
                float curve = along < 0f ? 1f : 1f - Mathf.Pow(x, Mathf.Max(0.2f, ArcadeCurve));
                // Engine upgrades (maxEngineTorque over the stock 380 Nm) scale the push
                float upgrade = Mathf.Clamp(maxEngineTorque / StockEngineTorque, 0.3f, 3f);
                float accel = ArcadeLaunchAccel * upgrade * curve * _throttle * GetPowerFactor() * offAccel * groundShare;
                _rb.AddForce(fwd * (accel * dir), ForceMode.Acceleration);
            }

            // ---- brake / coasting / parking hold
            float decel = _brake * ArcadeBrake;
            if (_throttle < 0.05f) decel += ArcadeCoast;
            if (!isDriven) decel = Mathf.Max(decel, ArcadeBrake);
            bool hold = (!isDriven || (_throttle < 0.05f && _brake < 0.05f)) && Mathf.Abs(vLong) < 0.6f;
            float dvLong = hold ? Mathf.Abs(vLong) : Mathf.Min(Mathf.Abs(vLong), decel * groundShare * dt);
            if (dvLong > 0f) _rb.AddForce(-fwd * (Mathf.Sign(vLong) * dvLong), ForceMode.VelocityChange);

            // Flat tyre drag: a force at that wheel pulls the truck to its side
            if (wheelDragTorque != null && speedAbs > 0.5f)
            {
                for (int i = 0; i < _wheels.Length && i < wheelDragTorque.Length; i++)
                {
                    if (!_wheels[i].grounded || wheelDragTorque[i] <= 0f) continue;
                    _rb.AddForceAtPosition(-fwd * (Mathf.Sign(vLong) * wheelDragTorque[i] / Mathf.Max(0.1f, _wheels[i].radius)), _wheels[i].hit.point);
                }
            }

            // ---- sideways grip: kill a share of the sideways speed each second
            // Tyre mods and the camp load (sidewaysGrip over the stock 1.7) scale the grip; a parked truck never slides
            float tyres = Mathf.Clamp(sidewaysGrip / StockSidewaysGrip, 0.3f, 3f);
            float grip = ArcadeGrip * tyres * offGrip * gripFactor * Mathf.Lerp(1f, ArcadeHandbrakeGrip, isDriven ? _handbrake : 0f);
            float dvLat = vLat * (1f - Mathf.Exp(-grip * dt)) * groundShare;
            _rb.AddForce(-right * dvLat, ForceMode.VelocityChange);
            _arcadeSliding = Mathf.Abs(vLat) > 1.5f;

            // ---- steering: aim at a turn rate (sharp slow, gentle fast), nothing when standing
            float rate = Mathf.Lerp(ArcadeTurnSlow, ArcadeTurnFast, speedT) * Mathf.Deg2Rad;
            float standScale = Mathf.Clamp01(speedAbs / 3f);
            float targetYaw = _steer * rate * standScale * Mathf.Sign(vLong == 0f ? 1f : vLong);
            Vector3 av = _rb.angularVelocity;
            float yaw = Vector3.Dot(av, normal);
            float follow = 1f - Mathf.Exp(-12f * dt);
            av += normal * ((targetYaw - yaw) * follow * groundShare);
            _rb.angularVelocity = av;

            // Downforce keeps the springs loaded at speed
            if (downforceCoefficient > 0f) _rb.AddForce(-normal * (downforceCoefficient * v.sqrMagnitude), ForceMode.Force);
        }

        /// <summary>Gauge only: a gear and an RPM derived from the speed (the arcade model has no gearbox).</summary>
        private void UpdateArcadeDisplay(float dt, float forwardSpeed)
        {
            float target = idleRpm;
            if (_gear >= 0)
            {
                int gears = ForwardGearCount;
                float frac = Mathf.Clamp01(Mathf.Abs(forwardSpeed) * 3.6f / Mathf.Max(10f, ArcadeTopSpeedKmh));
                float band = frac * gears;
                _gear = Mathf.Clamp(Mathf.FloorToInt(band) + 1, 1, gears);
                float inBand = Mathf.Clamp01(band - (_gear - 1));
                target = Mathf.Lerp(Mathf.Max(idleRpm, launchRpm * 0.6f), maxRpm * 0.85f, inBand);
            }
            else target = Mathf.Lerp(idleRpm, launchRpm, Mathf.Clamp01(Mathf.Abs(forwardSpeed) / Mathf.Max(1f, topReverseSpeedKmh / 3.6f)));
            if (!EngineRunning) target = 0f;
            else target = Mathf.Lerp(Mathf.Min(target, idleRpm + 600f), target, Mathf.Max(_throttle, 0.4f));
            _engineRpm = Mathf.Lerp(_engineRpm, target, dt * rpmResponse);
            _pendingGear = _gear;
        }

        private void MeasureBodyAcceleration(float dt)
        {
            Vector3 v = _rb.linearVelocity;
            Vector3 a = (v - _lastVelocity) / Mathf.Max(0.0001f, dt);
            _lastVelocity = v;
            if (GroundedWheelCount == 0) a = Vector3.zero;
            _bodyAccelLocal = Vector3.Lerp(_bodyAccelLocal, transform.InverseTransformDirection(a), 0.25f);
        }

        /// <summary>
        /// The tilting parts: direct children with renderers and no solid collider (so the physics shape never moves),
        /// not the wheels, not the camera target. Their rest pose is kept and the tilt is applied on top of it.
        /// </summary>
        private void FindTiltParts()
        {
            var parts = new System.Collections.Generic.List<Transform>();
            foreach (Transform child in transform)
            {
                if (child.GetComponentInChildren<Renderer>(true) == null) continue;
                if (child.GetComponentInChildren<VehicleCameraOrbit>(true) != null) continue;
                if (IsWheelMesh(child)) continue;
                bool solid = false;
                foreach (var c in child.GetComponentsInChildren<Collider>(true))
                    if (!c.isTrigger) { solid = true; break; }
                if (solid) continue;
                parts.Add(child);
            }
            _tiltParts = parts.ToArray();
            _tiltBasePos = new Vector3[_tiltParts.Length];
            _tiltBaseRot = new Quaternion[_tiltParts.Length];
            for (int i = 0; i < _tiltParts.Length; i++)
            {
                _tiltBasePos[i] = _tiltParts[i].localPosition;
                _tiltBaseRot[i] = _tiltParts[i].localRotation;
            }
        }

        private bool IsWheelMesh(Transform t)
        {
            return t == frontLeftMesh || t == frontRightMesh || t == rearLeftMesh || t == rearRightMesh;
        }

        /// <summary>Squat on the gas, dive on the brake, lean in corners: a small tilt of the body visual.</summary>
        private void UpdateBodyTilt()
        {
            if (_tiltParts.Length == 0) return;
            float g = Physics.gravity.magnitude;
            Vector2 want = Vector2.zero;
            if (Model == DriveModel.Arcade && BodyTiltDegrees > 0.01f)
            {
                want.x = Mathf.Clamp(-_bodyAccelLocal.z / g * 2f, -1f, 1f) * BodyTiltDegrees; // nose up on the gas
                want.y = Mathf.Clamp(_bodyAccelLocal.x / g * 2f, -1f, 1f) * BodyTiltDegrees;  // lean out of the turn
            }
            _tilt = Vector2.Lerp(_tilt, want, 1f - Mathf.Exp(-8f * Time.deltaTime));

            Vector3 pivot = transform.InverseTransformPoint(_rb != null ? _rb.worldCenterOfMass : transform.position);
            Quaternion q = Quaternion.Euler(_tilt.x, 0f, _tilt.y);
            for (int i = 0; i < _tiltParts.Length; i++)
            {
                Transform p = _tiltParts[i];
                if (p == null) continue;
                p.localPosition = pivot + q * (_tiltBasePos[i] - pivot);
                p.localRotation = q * _tiltBaseRot[i];
            }
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
            if (driving) Driven = this;
            else if (Driven == this) Driven = null;
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
                // A body held still (Journey parks a far truck as kinematic) or asleep must move again at once
                if (_rb != null)
                {
                    if (_rb.isKinematic) _rb.isKinematic = false;
                    _rb.WakeUp();
                }
                _stuckTimer = 0f;
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
