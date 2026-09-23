using UnityEngine;
using UnityEngine.UI;
#if TMPro
using TMPro;
#endif

namespace EndlessSurvival.Vehicle
{
    /// <summary>
    /// Hierarchy-driven UI controller for vehicle interaction prompts and dashboard gauges.
    /// References UI elements defined in the scene Canvas.
    /// </summary>
    public class VehicleUI : MonoBehaviour
    {
        [Header("Vehicle Reference")]
        [Tooltip("Reference to the vehicle interaction component")]
        public VehicleInteraction vehicleInteraction;

        [Tooltip("Reference to the vehicle controller component")]
        public VehicleController vehicleController;

        [Header("Hierarchy UI Panels")]
        [Tooltip("Panel showing the interact prompt (e.g. 'Press F to Enter')")]
        public GameObject interactionPromptPanel;

        [Tooltip("Dashboard panel containing speed, fuel, and controls information")]
        public GameObject dashboardPanel;

        [Header("Dashboard UI Elements")]
        [Tooltip("Text component displaying current speed")]
        public Text speedText;

        [Tooltip("Text component displaying fuel percentage")]
        public Text fuelText;

        [Tooltip("Slider displaying current fuel level")]
        public Slider fuelSlider;

        private void Awake()
        {
            if (vehicleInteraction == null)
                vehicleInteraction = GetComponent<VehicleInteraction>();

            if (vehicleController == null)
                vehicleController = GetComponent<VehicleController>();

            // Ensure initial visibility matches vehicle state
            UpdatePanelsVisibility(false, false);
        }

        private void OnEnable()
        {
            if (vehicleInteraction != null)
            {
                vehicleInteraction.OnPlayerNearVehicle += HandlePlayerProximity;
                vehicleInteraction.OnDriveStateChanged += HandleDriveStateChanged;
            }
        }

        private void OnDisable()
        {
            if (vehicleInteraction != null)
            {
                vehicleInteraction.OnPlayerNearVehicle -= HandlePlayerProximity;
                vehicleInteraction.OnDriveStateChanged -= HandleDriveStateChanged;
            }
        }

        private void Update()
        {
            if (vehicleInteraction == null || vehicleController == null) return;

            if (vehicleInteraction.IsPlayerInside)
            {
                UpdateDashboardValues();
            }
        }

        private void HandlePlayerProximity(bool isNear)
        {
            if (interactionPromptPanel != null)
            {
                interactionPromptPanel.SetActive(isNear && !vehicleInteraction.IsPlayerInside);
            }
        }

        private void HandleDriveStateChanged(bool isDriving)
        {
            UpdatePanelsVisibility(isDriving, !isDriving && interactionPromptPanel != null && interactionPromptPanel.activeSelf);
        }

        private void UpdatePanelsVisibility(bool isDriving, bool showPrompt)
        {
            if (dashboardPanel != null)
                dashboardPanel.SetActive(isDriving);

            if (interactionPromptPanel != null)
                interactionPromptPanel.SetActive(showPrompt);
        }

        private void UpdateDashboardValues()
        {
            if (speedText != null)
            {
                int speed = Mathf.RoundToInt(vehicleController.CurrentSpeedKmh);
                speedText.text = $"{speed} KM/H";
            }

            float fuelRatio = vehicleController.maxFuel > 0 ? (vehicleController.currentFuel / vehicleController.maxFuel) : 0f;

            if (fuelSlider != null)
            {
                fuelSlider.value = fuelRatio;
            }

            if (fuelText != null)
            {
                int fuelPercent = Mathf.RoundToInt(fuelRatio * 100f);
                fuelText.text = $"FUEL: {fuelPercent}%";
            }
        }
    }
}
