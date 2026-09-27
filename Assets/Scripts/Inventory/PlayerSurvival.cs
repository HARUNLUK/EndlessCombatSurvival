using UnityEngine;
using UnityEngine.UI;
using StarterAssets;

namespace EndlessSurvival.Inventory
{
    /// <summary>
    /// Hunger and thirst. Both drain over time (faster while sprinting) and keep draining while the player
    /// is inside the vehicle. When either reaches zero the player takes starvation damage that ignores armor.
    /// </summary>
    public class PlayerSurvival : MonoBehaviour
    {
        [Header("Limits")]
        public float maxHunger = 100f;
        public float maxThirst = 100f;

        [Header("Drain (units per second)")]
        [Tooltip("Default: about 15 minutes from full to empty")]
        public float hungerDrainPerSecond = 0.11f;

        [Tooltip("Default: about 10 minutes from full to empty")]
        public float thirstDrainPerSecond = 0.17f;

        [Tooltip("Drain multiplier while sprinting")]
        public float sprintMultiplier = 1.5f;

        [Header("Starvation")]
        [Tooltip("Health lost per second for each stat that is empty")]
        public float starvationDamagePerSecond = 1.5f;

        [Tooltip("Ratio below which the HUD text turns orange")]
        [Range(0f, 1f)]
        public float lowThreshold = 0.2f;

        [Header("References")]
        public PlayerHealth health;
        public StarterAssetsInputs inputs;

        [Header("Hierarchy UI")]
        public Text statusText;

        private float _hunger;
        private float _thirst;
        private float _lastTime;
        private float _nextRefresh;
        private bool _initialized;

        public float Hunger
        {
            get
            {
                EnsureInitialized();
                return _hunger;
            }
        }

        public float Thirst
        {
            get
            {
                EnsureInitialized();
                return _thirst;
            }
        }

        private void EnsureInitialized()
        {
            if (_initialized) return;
            _initialized = true;
            _hunger = maxHunger;
            _thirst = maxThirst;
        }

        private void Awake()
        {
            if (health == null) health = GetComponent<PlayerHealth>();
            if (inputs == null) inputs = GetComponent<StarterAssetsInputs>();
            EnsureInitialized();
        }

        private void Start()
        {
            _lastTime = Time.time;
            RefreshUI();
        }

        private void Update()
        {
            EnsureInitialized();

            // The player object is inactive while driving, so the elapsed time since the last frame
            // can be long. That time is still applied so hunger keeps draining in the vehicle.
            float dt = Time.time - _lastTime;
            _lastTime = Time.time;
            if (dt <= 0f) return;
            if (health != null && health.IsDead) return;

            float multiplier = (dt < 1f && inputs != null && inputs.sprint) ? sprintMultiplier : 1f;
            _hunger = Mathf.Max(0f, _hunger - hungerDrainPerSecond * multiplier * dt);
            _thirst = Mathf.Max(0f, _thirst - thirstDrainPerSecond * multiplier * dt);

            if (health != null)
            {
                int empty = (_hunger <= 0f ? 1 : 0) + (_thirst <= 0f ? 1 : 0);
                if (empty > 0) health.DamageIgnoringArmor(starvationDamagePerSecond * empty * dt);
            }

            if (Time.unscaledTime >= _nextRefresh)
            {
                _nextRefresh = Time.unscaledTime + 0.25f;
                RefreshUI();
            }
        }

        /// <summary>Restores hunger and returns the amount actually restored.</summary>
        public float Eat(float amount)
        {
            EnsureInitialized();
            float before = _hunger;
            _hunger = Mathf.Min(maxHunger, _hunger + Mathf.Max(0f, amount));
            RefreshUI();
            return _hunger - before;
        }

        /// <summary>Restores thirst and returns the amount actually restored.</summary>
        public float Drink(float amount)
        {
            EnsureInitialized();
            float before = _thirst;
            _thirst = Mathf.Min(maxThirst, _thirst + Mathf.Max(0f, amount));
            RefreshUI();
            return _thirst - before;
        }

        public void RefreshUI()
        {
            if (statusText == null) return;

            EnsureInitialized();
            float hungerRatio = maxHunger > 0f ? _hunger / maxHunger : 0f;
            float thirstRatio = maxThirst > 0f ? _thirst / maxThirst : 0f;

            statusText.text = $"HUNGER: {Mathf.CeilToInt(_hunger)}    THIRST: {Mathf.CeilToInt(_thirst)}";

            float lowest = Mathf.Min(hungerRatio, thirstRatio);
            if (lowest <= 0f) statusText.color = new Color(1f, 0.2f, 0.2f);
            else if (lowest <= lowThreshold) statusText.color = new Color(1f, 0.65f, 0.2f);
            else statusText.color = Color.white;
        }
    }
}
