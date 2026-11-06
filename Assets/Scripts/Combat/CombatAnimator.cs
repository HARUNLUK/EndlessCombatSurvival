using UnityEngine;

namespace EndlessCombat.Combat
{
    /// <summary>
    /// Drives the weapon related animator layers of the shared character controller
    /// (StarterAssetsThirdPerson.controller, used by both the player and the enemies):
    ///   'UpperBody '      rifle idle / aim / fire / reload
    ///   PistolUpperBody   pistol idle / aim / fire / reload
    ///   Actions           one shot actions (punch, melee swing, throw, hit reaction)
    /// The weapon type decides which upper body layer is blended in. The Actions layer is only
    /// weighted in while an action plays, so it never freezes the arms in between.
    /// Layers and parameters are created by Tools > Combat > Setup Package 1 (Package1Setup.cs).
    /// </summary>
    [DisallowMultipleComponent]
    public class CombatAnimator : MonoBehaviour
    {
        public const string RifleLayerName = "UpperBody ";
        public const string PistolLayerName = "PistolUpperBody";
        public const string MeleeLayerName = "MeleeUpperBody";
        public const string AimPitchParam = "AimPitch";
        public const string ActionLayerName = "Actions";

        // Action triggers (Actions layer)
        public const string PunchJab = "ActPunchJab";
        public const string PunchCross = "ActPunchCross";
        public const string MeleeSwing = "ActMeleeSwing";
        public const string MeleeStab = "ActMeleeStab";
        public const string Throw = "ActThrow";
        public const string HitReact = "ActHit";

        // Pistol layer triggers (separate from the rifle Fire/Reload triggers so the rifle layer never eats them)
        public const string PistolFire = "PistolFire";
        public const string PistolReload = "PistolReload";

        [Tooltip("Layer weight change per second when switching weapon type")]
        public float layerBlendSpeed = 6f;
        [Tooltip("Actions layer blend in / out speed per second")]
        public float actionBlendSpeed = 12f;

        private Animator _animator;
        private int _rifleLayer = -1;
        private int _pistolLayer = -1;
        private int _actionLayer = -1;
        private int _meleeLayer = -1;
        private bool _hasAimPitch;
        private float _rifleWeight;
        private float _pistolWeight;
        private float _meleeWeight;
        private float _actionWeight;
        private float _actionUntil = -1f;
        private WeaponType _type = WeaponType.Rifle;
        private bool _initialized;

        public Animator Animator => _animator;
        public bool HasActionLayer => _actionLayer >= 0;
        public bool IsActionPlaying => Time.time < _actionUntil;

        public static CombatAnimator GetOrAdd(Animator animator)
        {
            if (animator == null) return null;
            var ca = animator.GetComponent<CombatAnimator>();
            if (ca == null) ca = animator.gameObject.AddComponent<CombatAnimator>();
            ca.Init();
            return ca;
        }

        private void Awake()
        {
            Init();
        }

        private void Init()
        {
            if (_initialized) return;
            _animator = GetComponent<Animator>();
            if (_animator == null) return;
            _initialized = true;

            _rifleLayer = _animator.GetLayerIndex(RifleLayerName);
            if (_rifleLayer < 0) _rifleLayer = _animator.GetLayerIndex("UpperBody");
            if (_rifleLayer < 0 && _animator.layerCount > 1) _rifleLayer = 1;
            _pistolLayer = _animator.GetLayerIndex(PistolLayerName);
            _actionLayer = _animator.GetLayerIndex(ActionLayerName);
            _meleeLayer = _animator.GetLayerIndex(MeleeLayerName);
            foreach (var p in _animator.parameters)
            {
                if (p.name == AimPitchParam && p.type == AnimatorControllerParameterType.Float) _hasAimPitch = true;
            }

            _rifleWeight = _rifleLayer >= 0 ? _animator.GetLayerWeight(_rifleLayer) : 0f;
        }

        /// <summary>Selects the upper body animation set. Call every frame or whenever the weapon changes.</summary>
        public void SetWeaponType(WeaponType type)
        {
            _type = type;
        }

        /// <summary>Snaps the layer weights to the current weapon type (used right after spawning).</summary>
        public void SnapWeights()
        {
            _rifleWeight = _type == WeaponType.Rifle ? 1f : 0f;
            _pistolWeight = _type == WeaponType.Pistol ? 1f : 0f;
            _meleeWeight = _type == WeaponType.Melee ? 1f : 0f;
            Apply();
        }

        /// <summary>-1 looking down, 0 level, 1 looking up (blends the pistol aim clips).</summary>
        public void SetAimPitch(float value)
        {
            if (_hasAimPitch && _animator != null && _animator.isActiveAndEnabled)
                _animator.SetFloat(AimPitchParam, value, 0.08f, Time.deltaTime);
        }

        /// <summary>Fires an Actions layer trigger and keeps the layer weighted in for the given time.</summary>
        public void PlayAction(string trigger, float duration)
        {
            if (_animator == null || !_animator.isActiveAndEnabled) return;
            if (_actionLayer < 0) return;
            _animator.SetTrigger(trigger);
            _actionUntil = Mathf.Max(_actionUntil, Time.time + duration);
        }

        /// <summary>Sets a trigger that exists on the controller (no-op when the parameter is missing).</summary>
        public void SetTrigger(string trigger)
        {
            if (_animator == null || !_animator.isActiveAndEnabled) return;
            foreach (var p in _animator.parameters)
            {
                if (p.name == trigger && p.type == AnimatorControllerParameterType.Trigger)
                {
                    _animator.SetTrigger(trigger);
                    return;
                }
            }
        }

        private void Update()
        {
            if (_animator == null || !_animator.enabled) return;

            float dt = Time.deltaTime;
            _rifleWeight = Mathf.MoveTowards(_rifleWeight, _type == WeaponType.Rifle ? 1f : 0f, dt * layerBlendSpeed);
            _pistolWeight = Mathf.MoveTowards(_pistolWeight, _type == WeaponType.Pistol ? 1f : 0f, dt * layerBlendSpeed);
            _meleeWeight = Mathf.MoveTowards(_meleeWeight, _type == WeaponType.Melee ? 1f : 0f, dt * layerBlendSpeed);
            _actionWeight = Mathf.MoveTowards(_actionWeight, IsActionPlaying ? 1f : 0f, dt * actionBlendSpeed);
            Apply();
        }

        private void Apply()
        {
            if (_animator == null) return;
            if (_rifleLayer >= 0) _animator.SetLayerWeight(_rifleLayer, _rifleWeight);
            if (_pistolLayer >= 0) _animator.SetLayerWeight(_pistolLayer, _pistolWeight);
            if (_meleeLayer >= 0) _animator.SetLayerWeight(_meleeLayer, _meleeWeight);
            if (_actionLayer >= 0) _animator.SetLayerWeight(_actionLayer, _actionWeight);
        }
    }
}
