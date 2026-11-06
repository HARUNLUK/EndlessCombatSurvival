using System.Collections;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace EndlessCombat.Combat
{
    /// <summary>
    /// Left click melee for the player: fists when nothing is in the hand (no weapon or holstered),
    /// the weapon's MeleeWeapon stats when a melee weapon is held. Firearms are handled by PlayerShooter.
    /// The character turns to the camera direction on each swing; the hit is a sphere check after hitDelay.
    /// </summary>
    [DisallowMultipleComponent]
    public class PlayerMelee : MonoBehaviour
    {
        private PlayerShooter _shooter;
        private CombatAnimator _anim;
        private Camera _camera;
        private float _nextAttackTime;
        private bool _alternate;

        public bool IsAttacking { get; private set; }

        private void Awake()
        {
            _shooter = GetComponent<PlayerShooter>();
            var animator = GetComponent<Animator>();
            if (animator == null) animator = GetComponentInChildren<Animator>();
            _anim = CombatAnimator.GetOrAdd(animator);
        }

        private void Update()
        {
            if (_shooter == null) return;
            if (_camera == null) _camera = Camera.main;

            WeaponType hand = _shooter.HandType;
            if (hand != WeaponType.Unarmed && hand != WeaponType.Melee) return;
            if (CombatInputGate.Blocked(gameObject)) return;

            var thrower = GetComponent<PlayerThrower>();
            if (thrower != null && thrower.IsBusy) return;

            if (!AttackPressed() || Time.time < _nextAttackTime) return;

            MeleeStats stats = CurrentStats(out bool isWeapon);
            if (!CombatHooks.TrySpendStamina(stats.staminaCost)) return;
            stats.damage *= CombatHooks.PlayerMeleeDamageMultiplier();
            stats.attackInterval *= CombatHooks.PlayerMeleeIntervalMultiplier();
            _nextAttackTime = Time.time + Mathf.Max(0.15f, stats.attackInterval);
            StartCoroutine(AttackRoutine(stats, isWeapon));
        }

        private MeleeStats CurrentStats(out bool isWeapon)
        {
            isWeapon = false;
            if (_shooter.HandType == WeaponType.Melee && _shooter.CurrentWeapon != null)
            {
                var melee = _shooter.CurrentWeapon.GetComponent<MeleeWeapon>();
                if (melee != null)
                {
                    isWeapon = true;
                    return melee.stats;
                }
            }
            return CombatContent.Instance.fists;
        }

        private IEnumerator AttackRoutine(MeleeStats stats, bool isWeapon)
        {
            IsAttacking = true;

            // Face the camera direction so the swing hits what the player looks at
            Vector3 forward = transform.forward;
            if (_camera != null)
            {
                forward = _camera.transform.forward;
                forward.y = 0f;
                if (forward.sqrMagnitude > 0.001f)
                {
                    forward.Normalize();
                    transform.rotation = Quaternion.LookRotation(forward);
                }
            }

            string trigger;
            if (isWeapon) trigger = stats.stab ? CombatAnimator.MeleeStab : CombatAnimator.MeleeSwing;
            else trigger = _alternate ? CombatAnimator.PunchCross : CombatAnimator.PunchJab;
            _alternate = !_alternate;

            if (_anim != null) _anim.PlayAction(trigger, Mathf.Max(0.45f, stats.attackInterval));

            yield return new WaitForSeconds(stats.hitDelay);

            int hits = MeleeHit.Strike(transform, forward, stats);
            CombatHooks.RaisePlayerMeleeLanded(hits);

            // A swing is quiet; a landed hit is a bit louder
            float noise = hits > 0 ? stats.noiseRadius : stats.noiseRadius * 0.5f;
            if (noise > 0.1f) PlayerStealthController.Instance?.AlertNearbyEnemies(transform.position, noise);

            yield return new WaitForSeconds(Mathf.Max(0f, stats.attackInterval - stats.hitDelay) * 0.5f);
            IsAttacking = false;
        }

        private static bool AttackPressed()
        {
#if ENABLE_INPUT_SYSTEM
            return Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
#else
            return Input.GetMouseButtonDown(0);
#endif
        }
    }
}
