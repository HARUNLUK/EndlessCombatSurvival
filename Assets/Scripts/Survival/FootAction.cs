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
}
