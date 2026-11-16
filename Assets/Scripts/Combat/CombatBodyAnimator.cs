using System.Collections.Generic;
using UnityEngine;

namespace EndlessCombat.Combat
{
    /// <summary>
    /// Plays the souls-like combat states on the three layers built by Tools > Combat > Melee > Setup:
    ///   CombatShieldArm  (left arm mask)   ShieldCarry / ShieldGuard
    ///   CombatGuard      (upper body mask) WeaponGuard
    ///   CombatFullBody   (no mask, top)    attacks, roll, backstep, hit reactions, guard hit / break
    /// States are entered with CrossFade from code (CombatActor is the state machine), the layer weights are faded here.
    /// Missing states or layers are skipped silently, so the combat logic works even before the animator is built.
    /// </summary>
    [DisallowMultipleComponent]
    public class CombatBodyAnimator : MonoBehaviour
    {
        [Tooltip("Full body layer blend in speed (weight per second)")]
        public float fullBodyInSpeed = 14f;
        [Tooltip("Full body layer blend out speed (weight per second)")]
        public float fullBodyOutSpeed = 7f;
        public float poseBlendSpeed = 9f;

        private Animator _animator;
        private int _fullLayer = -1, _guardLayer = -1, _shieldLayer = -1, _attackLayer = -1;
        private float _attackWeight;
        private bool _attackActive;
        private bool _hasSpeedParam;
        private float _fullWeight, _guardWeight, _shieldWeight;
        private bool _fullActive;
        private bool _weaponGuard;
        private int _shieldMode; // 0 none, 1 carry, 2 guard
        private int _shownShieldMode = -1;
        private readonly HashSet<int> _knownStates = new HashSet<int>();
        private bool _initialized;

        public Animator Animator => _animator;
        public bool HasFullBodyLayer => _fullLayer >= 0;

        public static CombatBodyAnimator GetOrAdd(Animator animator)
        {
            if (animator == null) return null;
            var b = animator.GetComponent<CombatBodyAnimator>();
            if (b == null) b = animator.gameObject.AddComponent<CombatBodyAnimator>();
            b.Init();
            return b;
        }

        private void Awake() => Init();

        private void Init()
        {
            if (_initialized) return;
            _animator = GetComponent<Animator>();
            if (_animator == null || _animator.runtimeAnimatorController == null) return;
            _initialized = true;
            _fullLayer = _animator.GetLayerIndex(MeleeAnimationSet.LayerFullBody);
            _attackLayer = _animator.GetLayerIndex(MeleeAnimationSet.LayerAttack);
            _guardLayer = _animator.GetLayerIndex(MeleeAnimationSet.LayerGuard);
            _shieldLayer = _animator.GetLayerIndex(MeleeAnimationSet.LayerShield);
            foreach (var p in _animator.parameters)
                if (p.name == MeleeAnimationSet.SpeedParam && p.type == AnimatorControllerParameterType.Float) _hasSpeedParam = true;
        }

        private bool HasState(int layer, string state)
        {
            if (layer < 0) return false;
            int hash = Animator.StringToHash(_animator.GetLayerName(layer) + "." + state);
            if (_knownStates.Contains(hash)) return true;
            if (_animator.HasState(layer, hash)) { _knownStates.Add(hash); return true; }
            return false;
        }

        /// <summary>Starts a full body one shot state. Returns false when the state does not exist.</summary>
        public bool PlayFullBody(string state, float speed, float fade = 0.08f)
        {
            Init();
            if (_animator == null || !_animator.isActiveAndEnabled) return false;

            // Attacks live on the upper body layer (legs keep their stance); everything else is full body
            if (HasState(_attackLayer, state))
            {
                if (_hasSpeedParam) _animator.SetFloat(MeleeAnimationSet.SpeedParam, Mathf.Max(0.05f, speed));
                if (_attackWeight > 0.01f && _animator.GetCurrentAnimatorStateInfo(_attackLayer).IsName(state) && !_animator.IsInTransition(_attackLayer))
                    _animator.Play(state, _attackLayer, 0f);
                else
                    _animator.CrossFadeInFixedTime(state, _attackWeight > 0.01f ? fade : 0f, _attackLayer, 0f);
                _attackActive = true;
                _fullActive = false;
                return true;
            }

            if (!HasState(_fullLayer, state)) return false;
            _attackActive = false;
            if (_hasSpeedParam) _animator.SetFloat(MeleeAnimationSet.SpeedParam, Mathf.Max(0.05f, speed));
            // Same state again (hit while already in the hit reaction): restart it, a cross fade to itself is ignored
            if (_fullWeight > 0.01f && _animator.GetCurrentAnimatorStateInfo(_fullLayer).IsName(state) && !_animator.IsInTransition(_fullLayer))
                _animator.Play(state, _fullLayer, 0f);
            else
                _animator.CrossFadeInFixedTime(state, _fullWeight > 0.01f ? fade : 0f, _fullLayer, 0f);
            _fullActive = true;
            return true;
        }

        /// <summary>Releases the full body layer (fades out, then returns to Empty).</summary>
        public void StopFullBody()
        {
            _fullActive = false;
            _attackActive = false;
        }

        /// <summary>Weapon guard pose on the upper body (no shield).</summary>
        public void SetWeaponGuard(bool on) => _weaponGuard = on;

        /// <summary>Rest stance of a melee weapon gripped with both hands (upper body, under the attacks).</summary>
        public void SetTwoHandCarry(bool on) => _twoHandCarry = on;
        private bool _twoHandCarry;
        private int _shownGuardMode = -1;

        /// <summary>0 none, 1 carry, 2 raised.</summary>
        public void SetShieldMode(int mode) => _shieldMode = mode;

        /// <summary>Everything off at once (death, vehicle, disable).</summary>
        public void ResetAll()
        {
            _fullActive = false;
            _attackActive = false;
            _weaponGuard = false;
            _twoHandCarry = false;
            _shownGuardMode = -1;
            _shieldMode = 0;
            _fullWeight = _guardWeight = _shieldWeight = _attackWeight = 0f;
            _shownShieldMode = -1;
            Apply();
        }

        private void Update()
        {
            if (_animator == null || !_animator.isActiveAndEnabled) return;
            float dt = Time.deltaTime;

            _fullWeight = Mathf.MoveTowards(_fullWeight, _fullActive ? 1f : 0f, dt * (_fullActive ? fullBodyInSpeed : fullBodyOutSpeed));
            if (!_fullActive && _fullWeight <= 0f && _fullLayer >= 0)
            {
                var info = _animator.GetCurrentAnimatorStateInfo(_fullLayer);
                if (!info.IsName(MeleeAnimationSet.EmptyState) && HasState(_fullLayer, MeleeAnimationSet.EmptyState))
                    _animator.Play(MeleeAnimationSet.EmptyState, _fullLayer, 0f);
            }

            if (_attackLayer >= 0)
            {
                _attackWeight = Mathf.MoveTowards(_attackWeight, _attackActive ? 1f : 0f, dt * (_attackActive ? fullBodyInSpeed : fullBodyOutSpeed));
                if (!_attackActive && _attackWeight <= 0f)
                {
                    var info = _animator.GetCurrentAnimatorStateInfo(_attackLayer);
                    if (!info.IsName(MeleeAnimationSet.EmptyState) && HasState(_attackLayer, MeleeAnimationSet.EmptyState))
                        _animator.Play(MeleeAnimationSet.EmptyState, _attackLayer, 0f);
                }
            }

            // Guard layer: 2 = guard with the weapon, 1 = two handed rest stance
            int guardMode = _weaponGuard && HasState(_guardLayer, MeleeAnimationSet.StateWeaponGuard) ? 2
                : (_twoHandCarry && HasState(_guardLayer, MeleeAnimationSet.StateTwoHandCarry) ? 1 : 0);
            if (guardMode != 0 && guardMode != _shownGuardMode)
            {
                _animator.CrossFadeInFixedTime(guardMode == 2 ? MeleeAnimationSet.StateWeaponGuard : MeleeAnimationSet.StateTwoHandCarry, 0.12f, _guardLayer);
                _shownGuardMode = guardMode;
            }
            _guardWeight = Mathf.MoveTowards(_guardWeight, guardMode != 0 ? 1f : 0f, dt * poseBlendSpeed);
            if (_guardWeight <= 0f) _shownGuardMode = -1;

            int mode = _shieldMode;
            if (mode == 1 && !HasState(_shieldLayer, MeleeAnimationSet.StateShieldCarry)) mode = 0;
            if (mode == 2 && !HasState(_shieldLayer, MeleeAnimationSet.StateShieldGuard)) mode = 0;
            if (mode != 0 && mode != _shownShieldMode)
            {
                _animator.CrossFadeInFixedTime(mode == 2 ? MeleeAnimationSet.StateShieldGuard : MeleeAnimationSet.StateShieldCarry, 0.12f, _shieldLayer);
                _shownShieldMode = mode;
            }
            _shieldWeight = Mathf.MoveTowards(_shieldWeight, mode != 0 ? 1f : 0f, dt * poseBlendSpeed);
            if (_shieldWeight <= 0f) _shownShieldMode = -1;

            Apply();
        }

        private void Apply()
        {
            if (_animator == null) return;
            if (_fullLayer >= 0) _animator.SetLayerWeight(_fullLayer, _fullWeight);
            if (_attackLayer >= 0) _animator.SetLayerWeight(_attackLayer, _attackWeight);
            if (_guardLayer >= 0) _animator.SetLayerWeight(_guardLayer, _guardWeight);
            if (_shieldLayer >= 0) _animator.SetLayerWeight(_shieldLayer, _shieldWeight);
        }
    }
}
