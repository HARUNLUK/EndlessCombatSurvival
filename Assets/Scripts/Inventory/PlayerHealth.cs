using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using StarterAssets;
using EndlessCombat.Combat;
using EndlessSurvival.World;

namespace EndlessSurvival.Inventory
{
    /// <summary>
    /// Player health. Incoming damage is reduced by worn armor. On death the scene restarts after a delay.
    /// </summary>
    public class PlayerHealth : MonoBehaviour, IDamageable
    {
        [Header("Stats")]
        public float maxHealth = 100f;

        [Header("Bleeding")]
        [Tooltip("Is the player currently suffering from bleeding")]
        public bool isBleeding = false;
        [Tooltip("Health lost per second while bleeding")]
        public float bleedDamagePerSecond = 2f;
        [Tooltip("Chance that taking significant damage causes bleeding (0-1)")]
        [Range(0f, 1f)]
        public float bleedChanceOnHit = 0.35f;

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

        private void Update()
        {
            if (IsDead) return;

            if (isBleeding)
            {
                DamageIgnoringArmor(bleedDamagePerSecond * Time.deltaTime, "Ağır Kan Kaybı (Kanamadan Öldü)");
            }
        }

        private void OnDestroy()
        {
            if (equipment != null) equipment.Changed -= RefreshUI;
        }

        public void TakeDamage(float damage, Vector3 hitPoint, Vector3 hitNormal)
        {
            if (IsDead) return;
            EnsureInitialized();

            GameStatsTracker.Instance?.SetCauseOfDeath("Düşman Ateşi / Çatışma");

            float reduction = equipment != null ? equipment.DamageReduction : 0f;
            float finalDamage = damage * (1f - reduction);
            _current = Mathf.Max(0f, _current - finalDamage);

            // Chance to trigger bleeding on significant damage
            if (!isBleeding && finalDamage >= 8f && Random.value < bleedChanceOnHit)
            {
                StartBleeding();
            }

            RefreshUI();

            if (_current <= 0f) Die();
        }

        /// <summary>Damage from starvation and similar sources that armor cannot absorb.</summary>
        public void DamageIgnoringArmor(float damage, string cause = null)
        {
            if (IsDead || damage <= 0f) return;
            EnsureInitialized();

            if (!string.IsNullOrEmpty(cause))
            {
                GameStatsTracker.Instance?.SetCauseOfDeath(cause);
            }
            else if (isBleeding)
            {
                GameStatsTracker.Instance?.SetCauseOfDeath("Ağır Kan Kaybı (Kanamadan Öldü)");
            }

            _current = Mathf.Max(0f, _current - damage);
            RefreshUI();

            if (_current <= 0f) Die();
        }

        public void StartBleeding()
        {
            if (IsDead || isBleeding) return;
            isBleeding = true;
            RefreshUI();
            Debug.Log("<color=red>[PlayerHealth] KANAMA BAŞLADI! Bandaj veya medkit kullanmalısın!</color>");
        }

        public void StopBleeding()
        {
            if (!isBleeding) return;
            isBleeding = false;
            RefreshUI();
            Debug.Log("<color=green>[PlayerHealth] Kanama durduruldu.</color>");
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
            string bleedTag = isBleeding ? "  <color=red>[KANAMA!]</color>" : "";
            healthText.text = $"HEALTH: {Mathf.CeilToInt(_current)} / {Mathf.CeilToInt(maxHealth)}    ARMOR: {armor:0}{bleedTag}";
        }

        private void Die()
        {
            if (IsDead) return;
            IsDead = true;

            var controller = GetComponent<ThirdPersonController>();
            if (controller != null) controller.enabled = false;

            var shooter = GetComponent<PlayerShooter>();
            if (shooter != null) shooter.enabled = false;

            if (deathPanel != null) deathPanel.SetActive(false);

            string cause = GameStatsTracker.Instance != null ? GameStatsTracker.Instance.causeOfDeath : "Hayatını Kaybetti";
            GameOverUI.Show(cause);
        }

        private void Restart()
        {
            Time.timeScale = 1f;
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }
    }
}
