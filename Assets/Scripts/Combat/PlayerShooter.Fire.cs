using UnityEngine;

namespace EndlessCombat.Combat
{
    /// <summary>
    /// Firing: RT / left click while aiming. Automatic guns fire while the trigger is held and the rules allow it;
    /// single shot guns take one buffered press per shot (a press during a weapon change or a strike comes out
    /// right after it). The bullet goes to the point under the crosshair.
    /// </summary>
    public partial class PlayerShooter
    {
        private bool _fireWhenReady;

        private void HandleShootInput()
        {
            if (currentWeapon == null || isHolstered || !currentWeapon.IsFirearm)
            {
                isShooting = false;
                return;
            }

            // The weapon is still coming to the hand (Combat_Action_Plan.md 4): no shot yet. RT held through the draw
            // fires once as soon as it is ready (and aimed).
            if (_state.Drawing)
            {
                isShooting = false;
                if (GameInput.FireHeld) _fireWhenReady = true;
                return;
            }
            if (!GameInput.FireHeld) _fireWhenReady = false;

            // Only allow shooting when aiming. A press without aiming is forgotten (it must not fire when LT comes later).
            if (!isAiming && !allowHipFire)
            {
                isShooting = false;
                _state.TryConsume(CombatIntent.Fire);
                return;
            }

            // RT / left mouse
            bool held = GameInput.FireHeld && _state.Allows(CombatIntent.Fire);
            bool pressed = _state.TryConsume(CombatIntent.Fire);
            if (_fireWhenReady && held)
            {
                pressed = true;
                _fireWhenReady = false;
            }
            isShooting = held;
            bool shootTriggered = currentWeapon.IsAutomatic ? (held || pressed) : pressed;

            // Trigger pulled on an empty magazine: reload by itself when there are rounds left
            if (pressed && currentWeapon.CurrentAmmo <= 0 && !currentWeapon.InfiniteAmmo && !currentWeapon.IsReloading)
            {
                if (currentWeapon.ReserveAmmo > 0) TryStartReload();
                else EndlessSurvival.Survival.SurvivalNotify.Warn("Mermi yok", 1.5f);
                return;
            }

            if (!shootTriggered) return;

            if (isAiming && EffectiveType == WeaponType.Rifle)
            {
                // Snap aim weight to full so bullet hits target with 100% accuracy immediately
                currentAimWeight = 1f;
                AlignWeaponToAim(1f);
            }

            // Every bullet goes to the point under the crosshair (the rifle pose is only bent toward it for looks:
            // shooting along the barrel missed whenever the pose lagged or the target was close).
            Vector3 aimTargetPoint = GetShotPoint(out Vector3? traceOrigin);
            float spread = Mathf.Max(0f, CombatHooks.PlayerSpreadDegrees() + ExtraSpreadDegrees);

            if (currentWeapon.TryFire(aimTargetPoint, aimColliderMask, out _, true, spread, transform, traceOrigin))
            {
                // The shot is heard around (enemies come to look); the animation driver hears WeaponController.Fired
                PlayerStealthController.Instance?.EmitGunshotNoise(transform.position);
                _state.NoteFired(currentWeapon.ShotInterval);
            }
        }
    }
}
