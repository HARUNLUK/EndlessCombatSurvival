using UnityEngine;
using EndlessSurvival.Inventory;
using EndlessCombat.Combat;

namespace EndlessSurvival.World.Traps
{
    /// <summary>
    /// Gergin tuzak teli. Oyuncu comelerek altindan gecebilir veya [E] ile imha edebilir.
    /// Ayakta yururken takilirsa patlar ve etraftaki tum dusmanlari alarma gecirir.
    /// </summary>
    public class TripwireTrap : MonoBehaviour, IInteractable
    {
        [Header("Trap Settings")]
        [Tooltip("Patlama hasarı")]
        public float damage = 35f;

        [Tooltip("Patlama yarıçapı")]
        public float explosionRadius = 4.5f;

        [Tooltip("Patlama gürültüsünün düşmanları uyarma mesafesi")]
        public float alertRadius = 40f;

        [Header("State")]
        public bool isDisarmed = false;
        public bool isTriggered = false;

        [Header("Visuals")]
        public GameObject wireVisual;
        public GameObject pinVisual;
        public Light warningLight;

        public Vector3 InteractPosition => transform.position;
        public bool CanInteract => !isDisarmed && !isTriggered;

        private void Start()
        {
            if (warningLight != null)
            {
                warningLight.color = Color.red;
                warningLight.intensity = 1.5f;
            }
        }

        private void Update()
        {
            // Hafif kırmızı yanıp sönme efekti
            if (warningLight != null && !isDisarmed && !isTriggered)
            {
                warningLight.intensity = 1.0f + Mathf.PingPong(Time.time * 2.5f, 1.2f);
            }
        }

        private void OnTriggerEnter(Collider other)
        {
            if (isDisarmed || isTriggered) return;

            if (other.CompareTag("Player"))
            {
                // Çömeliyorsa telin altından güvenle geçer
                var stealth = PlayerStealthController.Instance;
                if (stealth != null && stealth.IsCrouching)
                {
                    Debug.Log("<color=cyan>[Tripwire] Oyuncu çömelerek tuzağın altından güvenle sızdı.</color>");
                    return;
                }

                // Ayaktaysa tuzağa takılır!
                TriggerExplosion();
            }
        }

        public void TriggerExplosion()
        {
            if (isTriggered || isDisarmed) return;
            isTriggered = true;

            Debug.LogWarning("<color=red>[Tripwire] TUZAK TETİKLENDİ! Patlama gerçekleşiyor...</color>");

            // Tel görselini yok et
            if (wireVisual != null) wireVisual.SetActive(false);
            if (warningLight != null) warningLight.enabled = false;

            // Patlama Görseli & Işığı
            GameObject explosionEffect = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            explosionEffect.name = "Tripwire_Explosion";
            explosionEffect.transform.position = transform.position;
            explosionEffect.transform.localScale = Vector3.one * 2.5f;
            
            var ren = explosionEffect.GetComponent<Renderer>();
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            Material expMat = new Material(shader) { color = new Color(1f, 0.4f, 0.1f) };
            expMat.EnableKeyword("_EMISSION");
            expMat.SetColor("_EmissionColor", Color.yellow * 6f);
            ren.sharedMaterial = expMat;

            var col = explosionEffect.GetComponent<Collider>();
            if (col != null) Destroy(col);

            Destroy(explosionEffect, 0.35f);

            // Hasar verme
            Collider[] hits = Physics.OverlapSphere(transform.position, explosionRadius);
            foreach (var hit in hits)
            {
                var health = hit.GetComponent<PlayerHealth>();
                if (health != null)
                {
                    GameStatsTracker.Instance?.SetCauseOfDeath("Tuzak Patlaması (Tripwire)");
                    health.TakeDamage(damage, hit.transform.position, Vector3.up);
                }
            }

            // Etraftaki düşmanları uyar
            var stealth = PlayerStealthController.Instance;
            if (stealth != null)
            {
                stealth.AlertNearbyEnemies(transform.position, alertRadius);
            }
        }

        public string GetPrompt()
        {
            if (isDisarmed || isTriggered) return "";
            return "[E] Tuzağı İmhâ Et";
        }

        public string Interact(PlayerInventory user)
        {
            if (isDisarmed || isTriggered) return null;

            isDisarmed = true;
            if (wireVisual != null) wireVisual.SetActive(false);
            if (warningLight != null) warningLight.enabled = false;

            Debug.Log("<color=green>[Tripwire] Tuzak başarıyla imhâ edildi.</color>");
            return "Tuzak güvenle imhâ edildi. (Hurdalar toplandı)";
        }
    }
}
