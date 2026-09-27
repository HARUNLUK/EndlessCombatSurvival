using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using StarterAssets;
using EndlessCombat.Combat;

namespace EndlessSurvival.Inventory
{
    /// <summary>
    /// Player health. Incoming damage is reduced by worn armor. On death the scene restarts after a delay.
    /// </summary>
    public class PlayerHealth : MonoBehaviour, IDamageable
    {
        [Header("Stats")]
        public float maxHealth = 100f;

        [Header("References")]
        public PlayerEquipment equipment;

        [Header("Hierarchy UI")]
        public Text healthText;
        [Tooltip("Shown when the player dies")]
        public GameObject deathPanel;

        [Header("Death")]
        public float restartDelay = 4f;

        private float _current;
        private bool _initialized;

        public bool IsDead { get; private set; }
        public float CurrentHealth
        {
            get
            {
                EnsureInitialized();
                return _current;
            }
        }

        private void EnsureInitialized()
        {
            if (_initialized) return;
            _initialized = true;
            _current = maxHealth;
        }

        private void Awake()
        {
            if (equipment == null) equipment = GetComponent<PlayerEquipment>();
            EnsureInitialized();
            if (deathPanel != null) deathPanel.SetActive(false);
        }

        private void Start()
        {
            if (equipment != null) equipment.Changed += RefreshUI;
            RefreshUI();
        }

        private void OnDestroy()
        {
            if (equipment != null) equipment.Changed -= RefreshUI;
        }

        public void TakeDamage(float damage, Vector3 hitPoint, Vector3 hitNormal)
        {
            if (IsDead) return;
            EnsureInitialized();

            float reduction = equipment != null ? equipment.DamageReduction : 0f;
            _current = Mathf.Max(0f, _current - damage * (1f - reduction));
            RefreshUI();

            if (_current <= 0f) Die();
        }

        /// <summary>Damage from starvation and similar sources that armor cannot absorb.</summary>
        public void DamageIgnoringArmor(float damage)
        {
            if (IsDead || damage <= 0f) return;
            EnsureInitialized();

            _current = Mathf.Max(0f, _current - damage);
            RefreshUI();

            if (_current <= 0f) Die();
        }

        public float Heal(float amount)
        {
            if (IsDead) return 0f;
            EnsureInitialized();

            float before = _current;
            _current = Mathf.Min(maxHealth, _current + Mathf.Max(0f, amount));
            RefreshUI();
            return _current - before;
        }

        public void RefreshUI()
        {
            if (healthText == null) return;

            EnsureInitialized();
            float armor = equipment != null ? equipment.TotalArmor : 0f;
            healthText.text = $"HEALTH: {Mathf.CeilToInt(_current)} / {Mathf.CeilToInt(maxHealth)}    ARMOR: {armor:0}";
        }

        private void Die()
        {
            IsDead = true;

            if (deathPanel != null) deathPanel.SetActive(true);

            var controller = GetComponent<ThirdPersonController>();
            if (controller != null) controller.enabled = false;

            var shooter = GetComponent<PlayerShooter>();
            if (shooter != null) shooter.enabled = false;

            Invoke(nameof(Restart), restartDelay);
        }

        private void Restart()
        {
            Time.timeScale = 1f;
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }
    }
}
