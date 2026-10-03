using UnityEngine;
using EndlessSurvival.Inventory;

namespace EndlessSurvival.Camping
{
    public class PlayerCampfire : MonoBehaviour, IInteractable
    {
        [Header("Fire Burn Settings")]
        [Tooltip("Kalan yanma süresi (saniye)")]
        public float remainingBurnTime = 300f; // 5 dakika
        public float maxBurnTime = 900f;       // 15 dakika

        [Header("Warmth & Healing")]
        [Tooltip("Ateşin ısı ve iyileştirme yaydığı mesafe")]
        public float warmthRadius = 6f;
        [Tooltip("Ateş başındayken saniyede iyileştirilen can")]
        public float healthRegenPerSecond = 0.8f;

        [Header("Visual References")]
        public Light fireLight;
        public GameObject emberVisual;
        public GameObject flameVisual;

        private bool _isBurning = true;
        private float _initialLightIntensity = 2.5f;

        public bool IsBurning => _isBurning && remainingBurnTime > 0f;
        public float RemainingTime => remainingBurnTime;

        // IInteractable Implementation
        public Vector3 InteractPosition => transform.position;
        public bool CanInteract => true;

        public string GetPrompt()
        {
            if (IsBurning)
            {
                int mins = Mathf.FloorToInt(remainingBurnTime / 60f);
                int secs = Mathf.FloorToInt(remainingBurnTime % 60f);
                return $"Kamp Ateşi: Pişir / Isın / Dinlen ({mins:00}:{secs:00})";
            }
            else
            {
                return "Sönmüş Ateş: Yakacak Ekle";
            }
        }

        private void Start()
        {
            if (fireLight != null) _initialLightIntensity = fireLight.intensity;
            _isBurning = remainingBurnTime > 0f;
            UpdateVisuals();
        }

        private void Update()
        {
            if (_isBurning)
            {
                remainingBurnTime -= Time.deltaTime;
                if (remainingBurnTime <= 0f)
                {
                    remainingBurnTime = 0f;
                    _isBurning = false;
                    UpdateVisuals();
                }

                // Ateşin yanında duran oyuncuyu ısıt ve iyileştir
                ProvideWarmthToPlayer();
            }
        }

        private void ProvideWarmthToPlayer()
        {
            var player = GameObject.FindGameObjectWithTag("Player");
            if (player == null) return;

            float dist = Vector3.Distance(transform.position, player.transform.position);
            if (dist <= warmthRadius)
            {
                var health = player.GetComponent<PlayerHealth>();
                if (health != null && !health.IsDead && !health.isBleeding)
                {
                    health.Heal(healthRegenPerSecond * Time.deltaTime);
                }
            }
        }

        public void AddFuel(float seconds)
        {
            remainingBurnTime = Mathf.Min(maxBurnTime, remainingBurnTime + seconds);
            _isBurning = true;
            UpdateVisuals();
        }

        private void UpdateVisuals()
        {
            if (fireLight != null)
            {
                fireLight.enabled = _isBurning;
                if (_isBurning)
                {
                    float factor = Mathf.Clamp01(remainingBurnTime / 60f);
                    fireLight.intensity = _initialLightIntensity * Mathf.Max(0.3f, factor);
                }
            }

            if (emberVisual != null) emberVisual.SetActive(_isBurning);
            if (flameVisual != null) flameVisual.SetActive(_isBurning);
        }

        public string Interact(PlayerInventory user)
        {
            if (user == null) return null;

            if (CampfireUI.IsOpen)
            {
                CampfireUI.Close();
                return null;
            }

            if (Time.unscaledTime - CampfireUI.LastCloseTime < 0.25f)
            {
                return null;
            }

            // UI penceresini aç
            CampfireUI.Open(this, user);
            return null;
        }
    }
}
