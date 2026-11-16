using UnityEngine;

namespace EndlessCombat.Combat
{
    /// <summary>
    /// Reloading: X tap (gamepad, when nothing to interact with is in reach) / R tap (keyboard) from the input buffer,
    /// the trigger pulled on an empty magazine, and a gun taken into the hand empty. The rules table decides when:
    /// it waits for a shot or a strike to end, a strike or a weapon change cuts it short, a hit reaction cancels it.
    /// </summary>
    public partial class PlayerShooter
    {
        private float _autoReloadAt = -1f;

        private void HandleReloadInput()
        {
            if (_state.TryConsume(CombatIntent.Reload)) TryStartReload();
        }

        /// <summary>Starts a reload when the gun in the hand needs one and the rules allow it now. The animation follows WeaponController.ReloadStarted.</summary>
        public bool TryStartReload()
        {
            if (currentWeapon == null || isHolstered || !currentWeapon.IsFirearm) return false;
            if (currentWeapon.IsReloading) return false;
            if (_state != null && !_state.TryStart(CombatIntent.Reload)) return false;
            bool started = currentWeapon.TryReload();
            if (started && _state != null) _state.Refresh();
            return started;
        }

        /// <summary>A firearm taken into the hand with an empty magazine reloads by itself when there are rounds for it.</summary>
        private void AutoReloadEmptyWeapon()
        {
            if (_autoReloadAt < 0f || Time.time < _autoReloadAt) return;
            if (currentWeapon == null || isHolstered || !currentWeapon.IsFirearm || currentWeapon.InfiniteAmmo)
            {
                _autoReloadAt = -1f;
                return;
            }
            if (currentWeapon.CurrentAmmo > 0 || currentWeapon.ReserveAmmo <= 0 || currentWeapon.IsReloading)
            {
                _autoReloadAt = -1f;
                return;
            }
            // Waits while the rules say no (still swapping, striking), then starts
            if (TryStartReload()) _autoReloadAt = -1f;
        }
    }
}
