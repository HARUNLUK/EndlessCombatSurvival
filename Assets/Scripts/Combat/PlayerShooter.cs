using UnityEngine;

namespace EndlessCombat.Combat
{
    /// <summary>
    /// The player's firearm hands (Combat_Architecture.md). Split into parts by job, one file each:
    ///   PlayerShooter.cs          fields, set up, the frame order
    ///   PlayerShooter.Aim.cs      LT aim stance, the aim / shot point under the crosshair, the rifle pose bend
    ///   PlayerShooter.Fire.cs     RT firing
    ///   PlayerShooter.Reload.cs   X / R reload, the automatic reload of an empty gun
    ///   PlayerShooter.Weapons.cs  weapon in the hand, holster and body spots (used by the loadout)
    /// Whether a press may start is not decided here but by PlayerCombatState (rules table + input buffer).
    /// The animator is not touched here: PlayerCombatAnimation reads this state and the weapon events.
    /// </summary>
    public partial class PlayerShooter : MonoBehaviour
    {
        [Header("Weapon Setup")]
        [SerializeField] private WeaponController currentWeapon;
        [SerializeField] private LayerMask aimColliderMask = ~0; // Everything by default

        [Header("Aiming Settings")]
        [SerializeField] private float aimRotateSpeed = 20f;
        [SerializeField] private bool allowHipFire = false;
        [Tooltip("Adjust this yaw angle so the character's rifle points straight forward with the camera crosshair")]
        [Range(-90f, 90f)]
        [SerializeField] private float aimBodyYawOffset = -45f;

        [Header("Dynamic Weapon Aiming")]
        [SerializeField] private bool dynamicAiming = true;
        [Range(0f, 1f)]
        [SerializeField] private float aimWeight = 1f;
        [SerializeField] private float maxAimAngle = 85f;

        [Header("Animation Setup (used by PlayerCombatAnimation)")]
        [SerializeField] private Animator animator;
        [SerializeField] private string aimingParam = "IsAiming";
        [SerializeField] private string fireTriggerParam = "Fire";
        [SerializeField] private string reloadTriggerParam = "Reload";

        [Header("Holster Settings")]
        [SerializeField] private bool isHolstered = false;
        [Tooltip("Pre-defined HolsterSocket GameObject in character hierarchy (e.g. under Chest/Spine). Position and rotate the socket directly in the scene.")]
        [SerializeField] private Transform holsterSocket;

        [Tooltip("Turn the right wrist so the barrel points exactly at the crosshair. Off (2026-11-13): it bent the wrist; the gun is seated in the hands instead (HandGripIK) and the chest does the aiming")]
        public bool wristAimCorrection = false;

        private Camera mainCamera;
        private Character.PlayerMotor tpController;
        private PlayerCombatState _state;
        private bool isAiming;
        private bool isShooting;

        public bool IsAiming => isAiming;
        public bool IsShooting => isShooting;
        public float AimBodyYawOffset => aimBodyYawOffset;
        public WeaponController CurrentWeapon => currentWeapon;
        public bool IsHolstered => isHolstered;
        public Transform HolsterSocket => holsterSocket;
        public Animator Animator => animator;
        public string AimingParam => aimingParam;
        public string FireTriggerParam => fireTriggerParam;
        public string ReloadTriggerParam => reloadTriggerParam;

        /// <summary>What the character holds right now: Unarmed when there is no weapon or it is holstered.</summary>
        public WeaponType HandType => (isHolstered || currentWeapon == null) ? WeaponType.Unarmed : EffectiveType;

        /// <summary>
        /// Weapon type as it is held: a long gun gripped with one hand (shield on the other arm) is handled like a pistol
        /// (pistol pose, no rifle aim, more spread).
        /// </summary>
        private WeaponType EffectiveType =>
            currentWeapon == null ? WeaponType.Unarmed
            : (currentWeapon.Type == WeaponType.Rifle && OneHandLongGun ? WeaponType.Pistol : currentWeapon.Type);

        /// <summary>Set by the loadout: the long gun is held with one hand.</summary>
        public bool OneHandLongGun { get; set; }

        /// <summary>Set by the loadout: the pistol is gripped with both hands (default is one hand).</summary>
        public bool PistolTwoHand { get; set; }

        /// <summary>Set by the loadout: extra bullet spread in degrees from the grip (one hand, shield on the arm).</summary>
        public float ExtraSpreadDegrees { get; set; }

        public bool HoldsFirearm => !isHolstered && currentWeapon != null && currentWeapon.IsFirearm;

        /// <summary>Body yaw offset while aiming: rifles use this shooter's value, other weapons their own.</summary>
        public float CurrentAimYawOffset =>
            (currentWeapon != null && EffectiveType != WeaponType.Rifle) ? currentWeapon.AimYawOffset : aimBodyYawOffset;

        /// <summary>Debug / capture tools: acts as if the aim button is held.</summary>
        public bool ForceAim { get; set; }

        private void Awake()
        {
            mainCamera = Camera.main;
            tpController = GetComponent<Character.PlayerMotor>();

            if (animator == null)
            {
                animator = GetComponent<Animator>();
            }

            if (currentWeapon == null)
            {
                currentWeapon = GetComponentInChildren<WeaponController>();
            }

            // Exclude the player's own layer from the aim ray, but only when the player has a layer of its own.
            // The project has no Player layer (the character is on Default): stripping Default made the aim ray
            // and every bullet pass through the enemies, the terrain and the buildings. The shooter's own colliders
            // are skipped one by one instead (GetShotPoint, WeaponController.TryFire ignoreRoot).
            // The player's own layer is NOT stripped either: the player and the enemies share layer 8 (the
            // Starter Assets armature), so stripping it hid the enemies' CharacterController and the rifle aim ray
            // went through the gaps between their bone colliders (12 Nov night fix).
            aimColliderMask &= ~(1 << 2); // Ignore Raycast

            if (currentWeapon != null)
            {
                handSocket = currentWeapon.transform.parent;
                handLocalPos = currentWeapon.transform.localPosition;
                handLocalRot = currentWeapon.transform.localRotation;
            }

            FindHolsterSocket();

            if (isHolstered)
            {
                SetHolstered(true);
            }

            // The combat parts are added at runtime so the scene does not need editing
            _state = PlayerCombatState.GetOrAdd(gameObject);
            if (GetComponent<PlayerMelee>() == null) gameObject.AddComponent<PlayerMelee>();
            if (GetComponent<PlayerThrower>() == null) gameObject.AddComponent<PlayerThrower>();
            if (GetComponent<WeaponSelectMap>() == null) gameObject.AddComponent<WeaponSelectMap>(); // LB tap / held map
            if (GetComponent<PlayerCombatAnimation>() == null) gameObject.AddComponent<PlayerCombatAnimation>();
            if (GetComponent<HandGripIK>() == null) gameObject.AddComponent<HandGripIK>(); // left hand on the weapon
            if (GetComponent<PlayerCameraRig>() == null) gameObject.AddComponent<PlayerCameraRig>(); // aim / melee / crouch camera
            if (animator != null && animator.GetComponent<FootstepEmitter>() == null)
            {
                var steps = animator.gameObject.AddComponent<FootstepEmitter>();
                steps.isPlayer = true;
            }
        }

        private void Update()
        {
            if (mainCamera == null)
            {
                mainCamera = Camera.main;
                if (mainCamera == null) return;
            }
            if (_state == null) _state = PlayerCombatState.GetOrAdd(gameObject);

            HandleReloadInput();
            HandleAimInput();
            HandleShootInput();
            AutoReloadEmptyWeapon();
            HandleStowWithoutLoadout();
        }

        /// <summary>Old scenes without the loadout: LB tap / R hold puts the weapon away and takes it out again.</summary>
        private void HandleStowWithoutLoadout()
        {
            if (_loadout == null) _loadout = GetComponent<EndlessSurvival.Inventory.PlayerLoadout>();
            if (_loadout != null) return; // the loadout takes the press
            if (_state.TryConsume(CombatIntent.Holster)) ToggleHolster();
        }

        private EndlessSurvival.Inventory.PlayerLoadout _loadout;
    }
}
