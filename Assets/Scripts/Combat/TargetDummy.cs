using System.Collections;
using UnityEngine;

namespace EndlessCombat.Combat
{
    public class TargetDummy : MonoBehaviour, IDamageable
    {
        [Header("Health Settings")]
        [SerializeField] private float maxHealth = 100f;
        [SerializeField] private float currentHealth;
        [SerializeField] private bool respawnOnDeath = true;
        [SerializeField] private float respawnDelay = 3f;

        [Header("Hit Feedback")]
        [SerializeField] private Renderer targetRenderer;
        [SerializeField] private Color hitColor = Color.red;
        [SerializeField] private float flashDuration = 0.12f;

        private Color originalColor;
        private Material targetMaterial;
        private Rigidbody targetRigidbody;
        private Collider targetCollider;
        private bool isDead;

        public bool IsDead => isDead;

        private void Awake()
        {
            currentHealth = maxHealth;
            targetRigidbody = GetComponent<Rigidbody>();
            targetCollider = GetComponent<Collider>();

            if (targetRenderer == null)
            {
                targetRenderer = GetComponentInChildren<Renderer>();
            }

            if (targetRenderer != null)
            {
                targetMaterial = targetRenderer.material;
                if (targetMaterial.HasProperty("_BaseColor"))
                {
                    originalColor = targetMaterial.GetColor("_BaseColor");
                }
                else if (targetMaterial.HasProperty("_Color"))
                {
                    originalColor = targetMaterial.color;
                }
            }
        }

        public void TakeDamage(float damage, Vector3 hitPoint, Vector3 hitNormal)
        {
            if (isDead) return;

            currentHealth -= damage;

            if (targetRigidbody != null)
            {
                targetRigidbody.AddForceAtPosition(-hitNormal * (damage * 10f), hitPoint, ForceMode.Impulse);
            }

            if (targetRenderer != null)
            {
                StopAllCoroutines();
                StartCoroutine(FlashRoutine());
            }

            if (currentHealth <= 0f)
            {
                Die();
            }
        }

        private IEnumerator FlashRoutine()
        {
            SetColor(hitColor);
            yield return new WaitForSeconds(flashDuration);
            SetColor(originalColor);
        }

        private void SetColor(Color col)
        {
            if (targetMaterial == null) return;

            if (targetMaterial.HasProperty("_BaseColor"))
            {
                targetMaterial.SetColor("_BaseColor", col);
            }
            else if (targetMaterial.HasProperty("_Color"))
            {
                targetMaterial.color = col;
            }
        }

        private void Die()
        {
            isDead = true;

            if (respawnOnDeath)
            {
                StartCoroutine(RespawnRoutine());
            }
            else
            {
                Destroy(gameObject, 0.5f);
            }
        }

        private IEnumerator RespawnRoutine()
        {
            if (targetCollider != null) targetCollider.enabled = false;
            if (targetRenderer != null) targetRenderer.enabled = false;

            yield return new WaitForSeconds(respawnDelay);

            currentHealth = maxHealth;
            isDead = false;

            if (targetCollider != null) targetCollider.enabled = true;
            if (targetRenderer != null)
            {
                targetRenderer.enabled = true;
                SetColor(originalColor);
            }
        }
    }
}
