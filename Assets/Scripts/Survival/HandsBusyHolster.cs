using UnityEngine;
using EndlessCombat.Combat;
using EndlessSurvival.Camping;

namespace EndlessSurvival.Survival
{
    /// <summary>
    /// When the character needs both hands (smoking, toilet, sitting or sleeping at the fire, crafting, fishing,
    /// vehicle repairs, gathering, drinking, kneeling at the fire...) the weapon in the hand goes on the back.
    /// When the action ends it comes back to the hand, but only if this put it away and the weapon did not change.
    /// If the player takes the weapon out (hold R) during the action, it is left alone until the action ends.
    /// </summary>
    public class HandsBusyHolster : MonoBehaviour
    {
        [Tooltip("Seconds to wait after the action before the weapon comes back (lets the animation fade out)")]
        public float drawDelay = 0.35f;

        private PlayerShooter _shooter;
        private GameObject _shooterOwner;
        private bool _autoHolstered;
        private bool _playerOverride;
        private WeaponController _weapon;
        private float _freeSince = -1f;

        /// <summary>True while an action keeps the character's hands busy.</summary>
        public static bool HandsBusy
        {
            get
            {
                if (SmokingAction.Active) return true;
                var vitals = PlayerVitals.Instance;
                if (vitals != null && vitals.ToiletActive) return true;
                if (CampfireUI.IsResting) return true;
                var craft = CraftingSystem.Instance;
                if (craft != null && craft.CurrentJob != null) return true;
                var parts = VehicleParts.Instance;
                if (parts != null && parts.CurrentJob != null) return true;
                var fishing = FishingController.Instance;
                if (fishing != null && fishing.State != FishingController.FishState.Idle) return true;
                var a = PlayerSurvivalAnimator.CurrentAction;
                if (a != SurvAnim.None && a != SurvAnim.Breath && a != SurvAnim.HeavyWalk && a != SurvAnim.Death) return true;
                var player = SurvivalDirector.Player;
                if (player != null)
                {
                    var sitting = player.GetComponent<PlayerSittingController>();
                    if (sitting != null && sitting.IsSitting) return true;
                }
                return false;
            }
        }

        private void Update()
        {
            var player = SurvivalDirector.Player;
            if (player == null || !player.activeInHierarchy) return; // in the vehicle: keep the state as it is
            if (_shooterOwner != player)
            {
                _shooterOwner = player;
                _shooter = player.GetComponentInChildren<PlayerShooter>(true);
                _autoHolstered = _playerOverride = false;
            }
            if (_shooter == null) return;

            if (HandsBusy)
            {
                _freeSince = -1f;
                var weapon = _shooter.CurrentWeapon;
                if (_autoHolstered && !_shooter.IsHolstered)
                {
                    // The player drew the weapon on purpose during the action
                    _autoHolstered = false;
                    _playerOverride = true;
                }
                if (!_autoHolstered && !_playerOverride && weapon != null && !_shooter.IsHolstered)
                {
                    _shooter.SetHolstered(true);
                    _autoHolstered = true;
                    _weapon = weapon;
                }
                return;
            }

            _playerOverride = false;
            if (!_autoHolstered) return;
            if (_freeSince < 0f) _freeSince = Time.time;
            if (Time.time - _freeSince < drawDelay) return;

            _autoHolstered = false;
            if (_shooter.IsHolstered && _shooter.CurrentWeapon != null && _shooter.CurrentWeapon == _weapon)
                _shooter.SetHolstered(false);
            _weapon = null;
        }
    }
}
