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

        private EndlessCombat.AI.AITarget _trippedBy;

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
                _trippedBy = EndlessCombat.AI.AITarget.Player;
                TriggerExplosion();
                return;
            }

            // Enemy_AI_Deep_Plan.md U14: anyone with an AITarget trips it. The camp's own people know where their
            // traps are, unless they are running away or panicking.
            var t = other.GetComponentInParent<EndlessCombat.AI.AITarget>();
            if (t == null || t.kind == EndlessCombat.AI.AITargetKind.Vehicle) return;
            var enemy = t.GetComponent<EndlessCombat.AI.EnemyController>();
            if (enemy != null)
            {
                if (enemy.IsDead) return;
                var squad = enemy.Squad;
                bool ownCamp = squad != null && (squad.kind == EndlessCombat.AI.SquadKind.Camp || squad.kind == EndlessCombat.AI.SquadKind.Occupants)
                               && (squad.home - transform.position).sqrMagnitude < 45f * 45f;
                bool lostHead = enemy.Fleeing || (enemy.Nerve != null && enemy.Nerve.Level >= EndlessCombat.AI.NerveLevel.Panic);
                if (ownCamp && !lostHead) return;
            }
            _trippedBy = t;
            TriggerExplosion();
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

            // Hasar verme: everyone in the blast (U14), once each
            Collider[] hits = Physics.OverlapSphere(transform.position, explosionRadius);
            var done = new System.Collections.Generic.HashSet<IDamageable>();
            foreach (var hit in hits)
            {
                var target = hit.GetComponentInParent<IDamageable>();
                if (target == null || target.IsDead || !done.Add(target)) continue;
                if (target is PlayerHealth) GameStatsTracker.Instance?.SetCauseOfDeath("Tuzak Patlaması (Tripwire)");
                EndlessCombat.Combat.Damage.Deal(target, new EndlessCombat.Combat.DamageInfo(damage, EndlessCombat.Combat.DamageType.Trap,
                    hit.transform.position, Vector3.up) { Source = gameObject });
            }

            // Etraftaki düşmanları uyar: the blast is heard; when the player tripped it, they know someone is there
            bool byPlayer = _trippedBy != null && _trippedBy.kind == EndlessCombat.AI.AITargetKind.Player;
            EndlessCombat.AI.AIStimuli.Raise(new EndlessCombat.AI.Stimulus
            {
                kind = EndlessCombat.AI.StimulusKind.Explosion,
                position = transform.position,
                radius = alertRadius,
                source = byPlayer ? _trippedBy : null,
                certainty = byPlayer ? 0.6f : 0f
            });
        }

        public string GetPrompt()
        {
            if (isDisarmed || isTriggered) return "";
            return "Tuzağı imha et";
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
