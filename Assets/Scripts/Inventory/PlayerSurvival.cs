using UnityEngine;
using UnityEngine.UI;

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
        [HideInInspector] public float hungerDrainPerSecond = 0.11f; // legacy, see NeedsTuning
        [HideInInspector] public float thirstDrainPerSecond = 0.17f; // legacy, see NeedsTuning

        [Tooltip("Drain multiplier while sprinting")]
        public float sprintMultiplier = 1.5f;

        [Header("Starvation")]
        [Tooltip("Health lost per second for each stat that is empty")]
        public float starvationDamagePerSecond = 1.5f;

        [Tooltip("Ratio below which the HUD text turns orange")]
        [Range(0f, 1f)]
        public float lowThreshold = 0.2f;

        [Header("Bladder / Restroom Need")]
        public float maxBladder = 100f;
        [Tooltip("Bladder fill rate per second")]
        public float bladderFillPerSecond = 0.05f;
        private float _bladder = 0f;

        [Header("References")]
        public PlayerHealth health;
        public EndlessCombat.Character.PlayerInputState inputs;

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

        public float Bladder
        {
            get
            {
                EnsureInitialized();
                return _bladder;
            }
        }

        public float HungerRatio => maxHunger > 0f ? Hunger / maxHunger : 0f;
        public float ThirstRatio => maxThirst > 0f ? Thirst / maxThirst : 0f;
        /// <summary>Hunger or thirst below NeedsTuning.CriticalNeedThreshold.</summary>
        public bool IsCritical => Mathf.Min(HungerRatio, ThirstRatio) < EndlessSurvival.Survival.NeedsTuning.CriticalNeedThreshold;

        public bool IsBladderFull => _bladder >= maxBladder * 0.85f;

        private void EnsureInitialized()
        {
            if (_initialized) return;
            _initialized = true;
            _hunger = maxHunger;
            _thirst = maxThirst;
            _bladder = 0f;
        }

        private void Awake()
        {
            if (health == null) health = GetComponent<PlayerHealth>();
            if (inputs == null) inputs = GetComponent<EndlessCombat.Character.PlayerInputState>();
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
            // Rates come from NeedsTuning; the serialized drain fields below are legacy and ignored.
            float hungerRate = maxHunger / (EndlessSurvival.Survival.NeedsTuning.HungerMinutesToEmpty * 60f);
            float thirstRate = maxThirst / (EndlessSurvival.Survival.NeedsTuning.ThirstMinutesToEmpty * 60f);
            _hunger = Mathf.Max(0f, _hunger - hungerRate * multiplier * dt);
            _thirst = Mathf.Max(0f, _thirst - thirstRate * multiplier * dt);
            _bladder = Mathf.Min(maxBladder, _bladder + bladderFillPerSecond * dt);

            if (health != null)
            {
                int empty = (_hunger <= 0f ? 1 : 0) + (_thirst <= 0f ? 1 : 0);
                if (empty > 0)
                {
                    string cause = (_hunger <= 0f && _thirst <= 0f) ? "Açlık ve Susuzluk" : (_hunger <= 0f ? "Açlıktan Tükenme" : "Susuzluk (Dehidrasyon)");
                    health.DamageIgnoringArmor(EndlessSurvival.Survival.NeedsTuning.StarvationDamagePerSecond * empty * dt, cause);
                }
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
            // Drinking fills bladder faster
            _bladder = Mathf.Min(maxBladder, _bladder + amount * 0.35f);
            RefreshUI();
            return _thirst - before;
        }

        /// <summary>
        /// Uses up hunger and thirst (the cost of a night's sleep). Eat/Drink ignore negative values, so the
        /// sleep calls could not do this. The bladder is not touched.
        /// </summary>
        public void Spend(float hunger, float thirst)
        {
            EnsureInitialized();
            _hunger = Mathf.Max(0f, _hunger - Mathf.Max(0f, hunger));
            _thirst = Mathf.Max(0f, _thirst - Mathf.Max(0f, thirst));
            RefreshUI();
        }

        /// <summary>Relieves personal need. No animations/visuals, resets bladder.</summary>
        public void RelieveNeed()
        {
            EnsureInitialized();
            _bladder = 0f;
            RefreshUI();
        }

        public void SetValues(float hunger, float thirst, float bladder)
        {
            EnsureInitialized();
            _hunger = Mathf.Clamp(hunger, 0f, maxHunger);
            _thirst = Mathf.Clamp(thirst, 0f, maxThirst);
            _bladder = Mathf.Clamp(bladder, 0f, maxBladder);
            RefreshUI();
        }

        public void RefreshUI()
        {
            if (statusText == null) return;

            EnsureInitialized();
            float hungerRatio = maxHunger > 0f ? _hunger / maxHunger : 0f;
            float thirstRatio = maxThirst > 0f ? _thirst / maxThirst : 0f;

            float bladderRatio = maxBladder > 0f ? _bladder / maxBladder : 0f;

            // Every stat is always visible; each one gets its own color (inverted for bladder: full = bad).
            string hungerStr = Colorize($"HUNGER: {Mathf.CeilToInt(_hunger)}", hungerRatio);
            string thirstStr = Colorize($"THIRST: {Mathf.CeilToInt(_thirst)}", thirstRatio);
            string bladderStr = Colorize($"BLADDER: {Mathf.CeilToInt(_bladder)}", 1f - bladderRatio);
            string needTag = IsBladderFull ? "  <color=yellow>[Rahatla (P)]</color>" : "";

            statusText.color = Color.white;
            statusText.text = $"{hungerStr}    {thirstStr}    {bladderStr}{needTag}";
        }

        private string Colorize(string label, float goodRatio)
        {
            string hex = goodRatio <= 0f ? "#FF3333" : goodRatio <= lowThreshold ? "#FFA533" : "#FFFFFF";
            return $"<color={hex}>{label}</color>";
        }
    }
}
