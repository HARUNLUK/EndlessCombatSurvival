using System;
using UnityEngine;
using EndlessSurvival.Inventory;
using EndlessSurvival.World;

namespace EndlessSurvival.Survival
{
    /// <summary>
    /// Short timed action on foot (drinking from a stream, collecting dew): the player stands still while a progress
    /// bar fills. Moving, jumping, being hit, getting locked or driving cancels it. One action at a time.
    /// </summary>
    public class FootAction : MonoBehaviour
    {
        public static FootAction Instance { get; private set; }

        public static bool Busy => Instance != null && Instance._active;

        private bool _active;
        private string _label;
        private float _seconds;
        private float _elapsed;
        private Func<string> _done;
        private PlayerHealth _health;

        /// <summary>Gets (or adds) the component on the player.</summary>
        public static FootAction Get(GameObject player)
        {
            if (player == null) return null;
            var a = player.GetComponent<FootAction>();
            return a != null ? a : player.AddComponent<FootAction>();
        }

        private void Awake() => Instance = this;

        private void OnDestroy()
        {
            if (_health != null) _health.Damaged -= OnDamaged;
            if (Instance == this) Instance = null;
        }

        /// <summary>Starts the action. Returns an error message, or null when it started.</summary>
        public string Begin(string label, float seconds, Func<string> done)
        {
            if (_active) return "Zaten bir iş yapıyorsun";
            if (SurvivalDirector.PlayerInVehicle) return "Araçtan in";
            var vitals = PlayerVitals.Instance;
            if (vitals != null && vitals.ToiletActive) return "Önce işini bitir";
            if (SmokingAction.Active) SmokingAction.Instance.Stop("Sigarayı söndürdün");

            var health = GetComponent<PlayerHealth>();
            if (_health != health)
            {
                if (_health != null) _health.Damaged -= OnDamaged;
                _health = health;
                if (_health != null) _health.Damaged += OnDamaged;
            }
            _active = true;
            _label = label;
            _seconds = Mathf.Max(0.2f, seconds);
            _elapsed = 0f;
            _done = done;
            return null;
        }

        public void Cancel(string why)
        {
            if (!_active) return;
            _active = false;
            _done = null;
            if (why != null) SurvivalNotify.Warn($"{_label} yarıda kaldı ({why})");
        }

        private void OnDamaged(float amount)
        {
            if (_active && amount > 0f) Cancel("vuruldun");
        }

        private void Update()
        {
            if (!_active) return;
            var player = SurvivalDirector.Player;
            if (player == null || !player.activeInHierarchy || SurvivalDirector.PlayerInVehicle) { Cancel("araca bindin"); return; }
            if (_health != null && _health.IsDead) { Cancel(null); return; }
            if (EndlessCombat.Combat.CombatBody.PlayerLocked) { Cancel("sersemledin"); return; }

            var inputs = GetComponent<EndlessCombat.Character.PlayerInputState>();
            if (inputs != null && !SurvivalUIState.AnyOpen && (inputs.move.sqrMagnitude > 0.04f || inputs.jump))
            {
                Cancel("hareket ettin");
                return;
            }

            _elapsed += Time.deltaTime;
            if (_elapsed < _seconds) return;
            _active = false;
            var done = _done;
            _done = null;
            string msg = done != null ? done() : null;
            if (!string.IsNullOrEmpty(msg)) SurvivalNotify.Show(msg);
        }

        private void OnGUI()
        {
            if (!_active) return;
            SurvivalGUI.Init();
            float w = 260f, x = (Screen.width - w) * 0.5f, y = Screen.height * 0.62f;
            GUI.Label(new Rect(x, y - 24, w, 22), _label + "...", SurvivalGUI.Center);
            SurvivalGUI.DrawBar(new Rect(x, y, w, 12), "", Mathf.Clamp01(_elapsed / _seconds), new Color(0.9f, 0.8f, 0.4f, 0.9f));
        }
    }

    /// <summary>
    /// On-foot dew collecting (Action menu > "Çiy topla"): a cloth wiped over the morning grass and wrung out.
    /// Morning only (05:00 - 08:30), not while it rains, a cloth in the backpack (not used up), at most twice per game day.
    /// Gives a bottle of clean water (empty bottle needed) or drinks it directly. Dew is clean: no sickness.
    /// The daily counter lasts for the session only (not saved).
    /// </summary>
    public static class DewCollecting
    {
        public const float StartHour = 5f;
        public const float EndHour = 8.5f;
        public const float MaxRain = 0.05f;
        public const int MaxPerDay = 2;
        public const float Seconds = 6f;
        public const float DirectDrinkThirst = 12f;

        private static int _day = -1;
        private static int _count;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetState()
        {
            _day = -1;
            _count = 0;
        }

        private static int Today => Mathf.FloorToInt((float)(GameClock.TotalHours / 24.0));

        private static int CountToday => _day == Today ? _count : 0;

        /// <summary>Why dew cannot be collected right now (short text), or null when it can.</summary>
        public static string BlockReason()
        {
            var cycle = DayNightCycle.Instance;
            float hour = cycle != null ? cycle.CurrentHour : 12f;
            if (hour < StartHour || hour >= EndHour) return "Sadece sabah";
            var weather = WeatherSystem.Instance;
            if (weather != null && weather.RainIntensity > MaxRain) return "Yağmur var";
            if (SurvivalTest.FreeResources) return null;

            var s = SurvivalSettings.Instance;
            var cloth = s != null ? s.Item("Item_Cloth") : null;
            if (cloth == null || SurvivalDirector.CountItem(cloth, false) <= 0) return "Bez gerekli";
            if (CountToday >= MaxPerDay) return "Bugünlük yeter";
            return null;
        }

        /// <summary>Starts collecting. Returns an error message, or null when it started.</summary>
        public static string TryStart()
        {
            var player = SurvivalDirector.Player;
            if (player == null || !player.activeInHierarchy || SurvivalDirector.PlayerInVehicle) return "Araçtan in";
            string reason = BlockReason();
            if (reason != null) return reason == "Sadece sabah" ? "Çiy sadece sabah toplanır (05:00 - 08:30)" : reason == "Bez gerekli" ? "Çiy toplamak için bez gerekli" : reason == "Yağmur var" ? "Yağmurda çiy toplanmaz" : "Bugün yeterince çiy topladın";

            var action = FootAction.Get(player);
            if (action == null) return "Şimdi olmaz";
            string error = action.Begin("Çiy toplanıyor", Seconds, Finish);
            if (error != null) return error;
            PlayerSurvivalAnimator.PlayOneShot(SurvAnim.Gather, Seconds + 0.5f);
            return null;
        }

        private static string Finish()
        {
            int today = Today;
            if (_day != today)
            {
                _day = today;
                _count = 0;
            }
            _count++;

            var s = SurvivalSettings.Instance;
            var bottle = s != null ? s.Item("Item_EmptyBottle") : null;
            var water = s != null ? s.Item("Item_WaterBottle") : null;
            if (bottle != null && water != null && SurvivalDirector.RemoveItem(bottle, 1, false) > 0)
            {
                SurvivalDirector.GiveItem(water, 1);
                return "Çiy toplandı, şişe doldu";
            }

            var player = SurvivalDirector.Player;
            var survival = player != null ? player.GetComponent<PlayerSurvival>() : null;
            if (survival != null) survival.Drink(DirectDrinkThirst);
            return "Çiyi sıkıp içtin";
        }
    }
}
