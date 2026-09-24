using UnityEngine;
using UnityEngine.UI;
#if TMPro
using TMPro;
#endif

namespace EndlessCombat.Combat
{
    public class CombatUI : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private PlayerShooter playerShooter;
        [SerializeField] private WeaponController weaponController;

        [Header("Crosshair Elements")]
        [SerializeField] private GameObject crosshairRoot;
        [SerializeField] private bool showCrosshairOnlyWhenAiming = false;

        [Header("Ammo & Status UI")]
        [SerializeField] private Text ammoText;
#if TMPro
        [SerializeField] private TextMeshProUGUI ammoTextTMP;
#endif
        [SerializeField] private GameObject reloadingPrompt;

        private void Start()
        {
            if (playerShooter == null)
            {
                playerShooter = FindFirstObjectByType<PlayerShooter>();
            }

            if (weaponController == null && playerShooter != null)
            {
                weaponController = playerShooter.CurrentWeapon;
            }

            if (weaponController != null)
            {
                weaponController.onAmmoChanged.AddListener(UpdateAmmoDisplay);
                weaponController.onReloadStart.AddListener(ShowReloading);
                weaponController.onReloadComplete.AddListener(HideReloading);
                UpdateAmmoDisplay(weaponController.CurrentAmmo, weaponController.ReserveAmmo);
            }

            if (reloadingPrompt != null)
            {
                reloadingPrompt.SetActive(false);
            }
        }

        private bool wasHolstered = false;

        private void Update()
        {
            if (playerShooter == null) return;

            if (crosshairRoot != null)
            {
                if (playerShooter.IsHolstered)
                {
                    crosshairRoot.SetActive(false);
                }
                else if (showCrosshairOnlyWhenAiming)
                {
                    crosshairRoot.SetActive(playerShooter.IsAiming);
                }
                else
                {
                    crosshairRoot.SetActive(true);
                }
            }

            if (playerShooter.IsHolstered)
            {
                if (ammoText != null) ammoText.text = "[Sırtta]";
#if TMPro
                if (ammoTextTMP != null) ammoTextTMP.text = "[Sırtta]";
#endif
            }
            else if (wasHolstered)
            {
                if (weaponController != null)
                {
                    UpdateAmmoDisplay(weaponController.CurrentAmmo, weaponController.ReserveAmmo);
                }
            }

            wasHolstered = playerShooter.IsHolstered;
        }

        private void UpdateAmmoDisplay(int current, int reserve)
        {
            if (playerShooter != null && playerShooter.IsHolstered) return;

            string displayString = (weaponController != null && weaponController.InfiniteAmmo) ? "∞" : $"{current} / {reserve}";

            if (ammoText != null)
            {
                ammoText.text = displayString;
            }

#if TMPro
            if (ammoTextTMP != null)
            {
                ammoTextTMP.text = displayString;
            }
#endif
        }

        private void ShowReloading()
        {
            if (reloadingPrompt != null)
            {
                reloadingPrompt.SetActive(true);
            }
        }

        private void HideReloading()
        {
            if (reloadingPrompt != null)
            {
                reloadingPrompt.SetActive(false);
            }
        }
    }
}
