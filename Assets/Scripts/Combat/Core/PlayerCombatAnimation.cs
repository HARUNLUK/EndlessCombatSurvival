using UnityEngine;

namespace EndlessCombat.Combat
{
    /// <summary>
    /// The one place that writes the player's weapon animation (Combat_Architecture.md, "Animasyon").
    /// It reads the combat state and listens to the doers' events; the doers never touch the animator:
    ///   weapon layers      rifle / pistol / melee upper body set by the weapon in the hand (CombatAnimator)
    ///   aim                the "IsAiming" bool and the pistol aim pitch blend
    ///   pistol arm         raised while aiming, reloading and for a moment after a shot
    ///   one shots          fire and reload triggers (WeaponController events), the throw (PlayerThrower event)
    ///   rest poses         two handed melee carry, pistol beside the raised shield
    /// Strikes and hit reactions are played by CombatBody on its own full body layers (the same code for the
    /// player and the enemies), so they are not repeated here.
    /// Runs after the shooter / melee / thrower (execution order 50), so it shows this frame's state.
    /// </summary>
    [DefaultExecutionOrder(50)]
    [DisallowMultipleComponent]
    public class PlayerCombatAnimation : MonoBehaviour
    {
        [Tooltip("Pistol: seconds the arm stays up after a shot when not aiming")]
        public float pistolRaiseAfterShot = 0.6f;

        private PlayerShooter _shooter;
        private PlayerCombatState _state;
        private PlayerThrower _thrower;
        private CombatBody _body;
        private ShieldHolder _shield;
        private EndlessSurvival.Inventory.PlayerLoadout _loadout;
        private Animator _animator;
        private CombatAnimator _combat;
        private CombatBodyAnimator _bodyAnim;
        private WeaponController _watched;
        private Camera _camera;
        private int _aimHash, _fireHash, _reloadHash;
        private bool _hasAimParam;
        private bool _shownAim;
        private float _pistolRaiseUntil = -1f;

        private void Awake()
        {
            _shooter = GetComponent<PlayerShooter>();
            _state = PlayerCombatState.GetOrAdd(gameObject);
            _thrower = GetComponent<PlayerThrower>();
            _body = GetComponent<CombatBody>();
            _shield = GetComponent<ShieldHolder>();
            FindAnimator();
        }

        private void OnEnable()
        {
            if (_thrower == null) _thrower = GetComponent<PlayerThrower>();
            if (_thrower != null) _thrower.ThrowStarted += OnThrow;
        }

        private void OnDisable()
        {
            if (_thrower != null) _thrower.ThrowStarted -= OnThrow;
            Watch(null);
        }

        private void FindAnimator()
        {
            _animator = _shooter != null ? _shooter.Animator : null;
            if (_animator == null) _animator = GetComponent<Animator>();
            if (_animator == null) _animator = GetComponentInChildren<Animator>();
            if (_animator == null) return;
            _combat = CombatAnimator.GetOrAdd(_animator);
            string aim = _shooter != null ? _shooter.AimingParam : "IsAiming";
            _aimHash = Animator.StringToHash(aim);
            _fireHash = Animator.StringToHash(_shooter != null ? _shooter.FireTriggerParam : "Fire");
            _reloadHash = Animator.StringToHash(_shooter != null ? _shooter.ReloadTriggerParam : "Reload");
            _hasAimParam = false;
            foreach (var p in _animator.parameters)
                if (p.nameHash == _aimHash && p.type == AnimatorControllerParameterType.Bool) _hasAimParam = true;
        }

        /// <summary>Listens to the weapon in the hand (fire / reload events); stops listening to the previous one.</summary>
        private void Watch(WeaponController weapon)
        {
            if (_watched == weapon) return;
            if (_watched != null)
            {
                _watched.Fired -= OnFired;
                _watched.ReloadStarted -= OnReloadStarted;
            }
            _watched = weapon;
            if (_watched != null)
            {
                _watched.Fired += OnFired;
                _watched.ReloadStarted += OnReloadStarted;
            }
        }

        private void Update()
        {
            if (_shooter == null) return;
            if (_animator == null) FindAnimator();
            if (_animator == null || !_animator.isActiveAndEnabled) return;
            if (_loadout == null) _loadout = GetComponent<EndlessSurvival.Inventory.PlayerLoadout>();
            if (_body == null) _body = GetComponent<CombatBody>();
            if (_shield == null) _shield = GetComponent<ShieldHolder>();
            if (_bodyAnim == null) _bodyAnim = CombatBodyAnimator.GetOrAdd(_animator);

            Watch(_shooter.CurrentWeapon);
            WeaponController weapon = _shooter.CurrentWeapon;
            WeaponType hand = _shooter.HandType;
            bool aiming = _shooter.IsAiming;

            // Weapon layers: the set of the weapon in the hand. Pistol: the arm hangs down with the free locomotion
            // arms, it only comes up to aim / fire / reload.
            if (_combat != null)
            {
                _combat.SetWeaponType(hand);
                _combat.SetPistolTwoHanded(_shooter.PistolTwoHand && weapon != null && weapon.Type == WeaponType.Pistol);
                bool raised = aiming || Time.time < _pistolRaiseUntil || (weapon != null && weapon.IsReloading && !_shooter.IsHolstered);
                _combat.SetPistolRaised(raised);
            }

            if (_hasAimParam && aiming != _shownAim)
            {
                _shownAim = aiming;
                _animator.SetBool(_aimHash, aiming);
            }

            // Rest poses: two handed melee carry (HandGripIK holds the left hand on it), pistol past the raised shield
            if (_bodyAnim != null)
                _bodyAnim.SetTwoHandCarry(_loadout != null && hand == WeaponType.Melee && !_shooter.IsHolstered
                                          && _loadout.TwoHanded(EndlessSurvival.Inventory.PlayerLoadout.Slot.Melee));
            if (_shield != null) _shield.GunSide = hand == WeaponType.Pistol && _body != null && _body.IsGuarding;
        }

        private void LateUpdate()
        {
            if (_combat == null || _shooter == null || _shooter.IsHolstered) return;
            if (_camera == null) _camera = Camera.main;
            if (_camera == null) return;
            // Aim pitch for the pistol aim blend (Pistol_Aim_Down / Neutral / Up clips)
            float pitch = Mathf.Asin(Mathf.Clamp(_camera.transform.forward.y, -1f, 1f)) * Mathf.Rad2Deg;
            _combat.SetAimPitch(Mathf.Clamp(pitch / 50f, -1f, 1f));
        }

        private void OnFired()
        {
            _pistolRaiseUntil = Time.time + pistolRaiseAfterShot;
            if (_animator == null || !_shooter.IsAiming) return; // recoil clips only while aiming
            if (_shooter.HandType == WeaponType.Pistol)
            {
                _combat?.SetTrigger(CombatAnimator.PistolFire);
                return;
            }
            // Rifle: not again while the firing state still plays
            AnimatorStateInfo info = _animator.layerCount > 1 ? _animator.GetCurrentAnimatorStateInfo(1) : default;
            if (!info.IsName("Firing Rifle")) _animator.SetTrigger(_fireHash);
        }

        private void OnReloadStarted()
        {
            if (_animator == null || _shooter == null) return;
            if (_shooter.HandType == WeaponType.Pistol) _combat?.SetTrigger(CombatAnimator.PistolReload);
            else _animator.SetTrigger(_reloadHash);
        }

        private void OnThrow(float seconds)
        {
            if (_combat != null) _combat.PlayAction(CombatAnimator.Throw, seconds);
        }
    }
}
