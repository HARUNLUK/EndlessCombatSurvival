using UnityEngine;
using UnityEngine.AI;
using EndlessCombat.Combat;
using System.Collections.Generic;
using EndlessSurvival.Inventory;

namespace EndlessCombat.AI
{
    [RequireComponent(typeof(NavMeshAgent))]
    public class EnemyController : MonoBehaviour, IDamageable
    {
        public enum EnemyState { Idle, Sitting, Patrolling, Suspicious, Combat }
        
        [Header("Behavior")]
        public EnemyState initialState = EnemyState.Idle;
        private EnemyState currentState;
        public bool IsAlerted => currentState == EnemyState.Combat;

        [Header("Stealth & Perception")]
        [Tooltip("Düşmanın görüş açısı genişliği (derece)")]
        public float fieldOfViewAngle = 110f;
        [Tooltip("Göz yüksekliği (raycast çıkış noktası)")]
        public float eyeHeight = 1.6f;
        [Tooltip("Şüphelenme sayacı (0-100)")]
        public float suspicionMeter = 0f;
        public float suspicionBuildRate = 50f; // Saniyede dolan şüphe miktarı
        public float suspicionDecayRate = 20f;  // Saniyede azalan şüphe miktarı
        [Tooltip("Şüpheli ses veya hareket algılandığında araştırma süresi")]
        public float investigateDuration = 5f;
        private Vector3 _investigatePoint;
        private float _investigateTimer = 0f;
        private TextMesh _stealthIndicator;

        [Header("Stats")]
        public float maxHealth = 100f;
        private float currentHealth;

        [Header("Combat")]
        public float moveSpeed = 3.5f;
        public float aimRotationOffset = 45f;
        public float detectionRange = 30f;
        public float attackRange = 15f;
        public float attackCooldown = 1.5f;
        public float damage = 10f;
        private float lastAttackTime;

        [Header("Loot")]
        [Tooltip("Loot rolled and dropped as pickups when this enemy dies")]
        public LootTable dropTable;

        [Tooltip("Item version of the weapon this enemy carries; dropped as a pickup on death (needs Drop Table for the pickup prefab)")]
        public ItemDefinition heldWeaponItem;

        [Header("References")]
        public Transform target;
        private NavMeshAgent agent;
        private Animator animator;
        private WeaponController weapon;
        private CharacterController controller; // Yerçekimi ve çarpışma için

        [Header("Ragdoll (Auto-populated at Start)")]
        private List<Rigidbody> ragdollRigidbodies = new List<Rigidbody>();
        private List<Collider> ragdollColliders = new List<Collider>();

        public bool IsDead { get; private set; }
        private float verticalVelocity;
        private float gravity = -15f;

        private void Start()
        {
            currentState = initialState;
            currentHealth = maxHealth;
            agent = GetComponent<NavMeshAgent>();
            animator = GetComponentInChildren<Animator>();
            weapon = GetComponentInChildren<WeaponController>();
            
            // Yerçekimi ve duvarlardan geçmemesi için CharacterController ekle/bul
            controller = GetComponent<CharacterController>();
            if (controller == null)
            {
                controller = gameObject.AddComponent<CharacterController>();
                // Ayakların tam zemine değmesi için ince ayarlar (StarterAssets boyutlarına uygun)
                controller.height = 1.8f;
                controller.radius = 0.28f;
                controller.center = new Vector3(0, 0.9f, 0);
                controller.skinWidth = 0.01f; // Zeminle aradaki tampon boşluğu minimuma indiriyoruz ki havada süzülmesin
            }

            if (agent != null) agent.enabled = false;

            if (target == null)
            {
                var player = GameObject.FindGameObjectWithTag("Player");
                if (player != null)
                {
                    target = player.transform;
                }
                else
                {
                    Debug.LogError("Düşman oyuncuyu bulamadı! Oyuncunun tag'i 'Player' değil veya Target atanmamış.");
                }
            }

            SetupRagdoll();
            DisableRagdoll();
            SetupStealthIndicator();
        }

        private void Update()
        {
            if (IsDead) return;

            if (target == null)
            {
                var player = GameObject.FindGameObjectWithTag("Player");
                if (player != null)
                {
                    target = player.transform;
                }
                else return;
            }

            float distanceToTarget = Vector3.Distance(transform.position, target.position);
            animator.SetFloat("MotionSpeed", 1f);

            bool isMoving = false;
            float stopDist = 3f;
            Vector3 direction = (target.position - transform.position).normalized;
            direction.y = 0;

            // Yerçekimi (Gravity) Hesaplaması
            if (controller.isGrounded)
            {
                verticalVelocity = -2f;
            }
            else
            {
                verticalVelocity += gravity * Time.deltaTime;
            }

            Vector3 moveVelocity = Vector3.zero;

            // 1. Algılama ve Görüş Kontrolü
            bool canSee = CanSeePlayer(out float sightClarity);

            // Ayak sesi / gürültü duyma kontrolü
            CheckPlayerNoise();

            if (currentState == EnemyState.Combat)
            {
                // Savaş Durumu
                suspicionMeter = 100f;

                if (distanceToTarget <= attackRange && canSee)
                {
                    animator.SetBool("IsAiming", true);
                    transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(direction), Time.deltaTime * 10f);

                    if (Time.time >= lastAttackTime + attackCooldown)
                    {
                        Attack();
                    }

                    if (distanceToTarget > stopDist)
                    {
                        moveVelocity = direction * (moveSpeed * 0.7f);
                        isMoving = true;
                    }
                }
                else if (distanceToTarget <= detectionRange * 1.3f)
                {
                    animator.SetBool("IsAiming", false);
                    transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(direction), Time.deltaTime * 10f);

                    if (distanceToTarget > stopDist)
                    {
                        moveVelocity = direction * moveSpeed;
                        isMoving = true;
                    }
                }
                else
                {
                    // Oyuncu çok uzaklaştı veya tamamen kaçtı
                    animator.SetBool("IsAiming", false);
                    SetState(EnemyState.Suspicious);
                    _investigatePoint = target.position;
                    _investigateTimer = investigateDuration;
                }
            }
            else if (currentState == EnemyState.Suspicious)
            {
                // Şüphe Durumu: Şüpheli noktaya doğru bak / ilerle
                animator.SetBool("IsAiming", false);

                if (canSee)
                {
                    // Oyuncuyu doğrudan görmeye devam ediyorsa şüphe barı hızla dolar
                    suspicionMeter += suspicionBuildRate * sightClarity * Time.deltaTime;
                    transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(direction), Time.deltaTime * 8f);

                    if (suspicionMeter >= 100f)
                    {
                        SetState(EnemyState.Combat);
                        AlertNearbyTeammates(transform.position, 25f);
                    }
                }
                else
                {
                    // Görüş alanı dışındaysa şüphe barı yavaşça söner
                    suspicionMeter = Mathf.MoveTowards(suspicionMeter, 0f, suspicionDecayRate * Time.deltaTime);

                    Vector3 toInvestigate = (_investigatePoint - transform.position);
                    toInvestigate.y = 0;
                    if (toInvestigate.magnitude > 1.5f && _investigateTimer > 0f)
                    {
                        transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(toInvestigate.normalized), Time.deltaTime * 6f);
                        moveVelocity = toInvestigate.normalized * (moveSpeed * 0.5f);
                        isMoving = true;
                    }

                    _investigateTimer -= Time.deltaTime;
                    if (suspicionMeter <= 0f && _investigateTimer <= 0f)
                    {
                        SetState(initialState);
                    }
                }
            }
            else
            {
                // Normal / Idle / Sitting / Patrolling
                animator.SetBool("IsAiming", false);

                if (canSee)
                {
                    suspicionMeter += suspicionBuildRate * sightClarity * Time.deltaTime;
                    if (suspicionMeter > 25f)
                    {
                        _investigatePoint = target.position;
                        _investigateTimer = investigateDuration;
                        SetState(EnemyState.Suspicious);
                    }
                }
                else
                {
                    suspicionMeter = Mathf.MoveTowards(suspicionMeter, 0f, suspicionDecayRate * Time.deltaTime);
                }
            }

            if (currentState == EnemyState.Sitting)
            {
                transform.localScale = Vector3.one;
                isMoving = false;
                moveVelocity = Vector3.zero;
                animator.SetBool("IsSitting", true);
            }
            else
            {
                transform.localScale = Vector3.one;
                animator.SetBool("IsSitting", false);
            }

            // Hareketi ve yerçekimini uygula
            moveVelocity.y = verticalVelocity;
            controller.Move(moveVelocity * Time.deltaTime);

            animator.SetFloat("Speed", isMoving ? moveSpeed : 0f);

            UpdateStealthIndicator();
        }

        private void LateUpdate()
        {
            if (IsDead || target == null || !animator.GetBool("IsAiming")) return;

            // Bacaklar yürüme yönüne bakarken, üst gövdeyi silahı hedefe tutacak şekilde büküyoruz.
            Transform spine = animator.GetBoneTransform(HumanBodyBones.Spine);
            if (spine == null) spine = animator.GetBoneTransform(HumanBodyBones.Chest);

            if (spine != null)
            {
                // Spine kemiğini karakterin kendi Y (yukarı) ekseni etrafında offset kadar çevir
                spine.rotation = Quaternion.AngleAxis(aimRotationOffset, transform.up) * spine.rotation;
            }
        }

        private void Attack()
        {
            lastAttackTime = Time.time;
            
            if (weapon != null)
            {
                Vector3 aimPoint = target.position + Vector3.up * 1.5f;
                LayerMask hitMask = ~0; 
                
                // (weapon.transform.LookAt kaldırıldı çünkü silahı elin içinde büküp kırık gösteriyordu)
                // Animasyon ve Root yönelmesi hedefe baktığı için mermi Muzzle.forward yönünde gidecek.
                weapon.TryFire(aimPoint, hitMask, out RaycastHit hit);
            }
            else
            {
                Debug.LogWarning("Düşmanın elinde WeaponController yok! Lütfen prefab'ına silah ekle.");
            }
        }

        public void TakeDamage(float damage, Vector3 hitPoint, Vector3 hitNormal)
        {
            if (IsDead) return;

            currentHealth -= damage;
            Debug.Log($"{gameObject.name} hasar aldi: {damage}. Kalan Can: {currentHealth}");

            // Hasar alınca anında çatışmaya geç ve etraftakileri uyar
            suspicionMeter = 100f;
            SetState(EnemyState.Combat);
            AlertNearbyTeammates(transform.position, 25f);

            if (currentHealth <= 0)
            {
                Die();
            }
        }

        private void Die()
        {
            IsDead = true;
            if (agent != null) agent.enabled = false;
            
            EndlessSurvival.World.GameStatsTracker.Instance?.RecordEnemyKilled();

            EnableRagdoll();

            LootSpawner.DropLoot(dropTable, transform.position);
            DropHeldWeapon();

            Destroy(gameObject, 15f); // 15 saniye sonra cesedi sil
        }

        private void DropHeldWeapon()
        {
            if (heldWeaponItem == null || dropTable == null || dropTable.pickupPrefab == null) return;

            Vector3 position = weapon != null ? weapon.transform.position : transform.position + Vector3.up;
            WorldPickup.Spawn(dropTable.pickupPrefab, heldWeaponItem, 1, position, null);

            if (weapon != null) weapon.gameObject.SetActive(false);
        }

        private void SetupRagdoll()
        {
            ragdollRigidbodies = new List<Rigidbody>(GetComponentsInChildren<Rigidbody>());
            ragdollColliders = new List<Collider>(GetComponentsInChildren<Collider>());

            // Kendi ana collider ve rigidbody'sini ragdoll listesinden cikar
            var mainRb = GetComponent<Rigidbody>();
            var mainCol = GetComponent<Collider>();
            
            if (mainRb != null) ragdollRigidbodies.Remove(mainRb);
            if (mainCol != null) ragdollColliders.Remove(mainCol);
        }

        private void DisableRagdoll()
        {
            foreach (var rb in ragdollRigidbodies)
            {
                rb.isKinematic = true;
            }
            foreach (var col in ragdollColliders)
            {
                // Hasar alabilmesi icin colliderlarin isTrigger veya Layer tabanli ayarlanmasi daha iyidir, 
                // ama biz hitbox bazli calisiyorsak colliderlar acik kalmali. 
                // Biz ragdoll colliderlarini Hitbox (hasar alma) icin kullanabiliriz.
                // Bu yuzden kapatmiyoruz, sadece rigidbody'leri kinematic yapiyoruz.
            }
            if (animator != null) animator.enabled = true;
        }

        private void EnableRagdoll()
        {
            if (animator != null) animator.enabled = false;
            foreach (var rb in ragdollRigidbodies)
            {
                rb.isKinematic = false;
                rb.useGravity = true;
                rb.AddForce(Vector3.up * 2f, ForceMode.Impulse); // Hafif sicrama
            }
            
            var mainCol = GetComponent<Collider>();
            if (mainCol != null) mainCol.enabled = false;
        }

        public void SetState(EnemyState newState)
        {
            if (currentState == newState) return;
            currentState = newState;
            
            if (newState == EnemyState.Combat)
            {
                // Stop dialogue if alerted
                var dialogue = GetComponent<EnemyDialogue>();
                if (dialogue != null) dialogue.StopSpeaking();
            }
        }

        #region Stealth & Perception Helpers

        private bool CanSeePlayer(out float sightClarity)
        {
            sightClarity = 0f;
            if (target == null) return false;

            Vector3 eyePos = transform.position + Vector3.up * eyeHeight;
            Vector3 targetCenter = target.position + Vector3.up * 1.0f;
            Vector3 dirToTarget = (targetCenter - eyePos);
            float dist = dirToTarget.magnitude;

            // Menzil hesabı (çömelme ve gece çarpanları)
            float effectiveRange = detectionRange;
            var stealthCtrl = EndlessCombat.Combat.PlayerStealthController.Instance;
            if (stealthCtrl != null && stealthCtrl.IsCrouching)
            {
                effectiveRange *= stealthCtrl.crouchVisibilityMultiplier;
            }

            var cycle = EndlessSurvival.World.DayNightCycle.Instance;
            if (cycle != null && cycle.IsNight)
            {
                effectiveRange *= 0.55f;
            }

            if (dist > effectiveRange) return false;

            // Görüş açısı (FOV) kontrolü
            float angle = Vector3.Angle(transform.forward, dirToTarget);
            if (angle > fieldOfViewAngle * 0.5f)
            {
                // Karakter arkadan çok yaklaştığında (1.8 metre) fark edilir
                if (dist > 1.8f) return false;
            }

            // Siper / Engel (Raycast) kontrolü
            int mask = LayerMask.GetMask("Default", "Environment");
            if (Physics.Raycast(eyePos, dirToTarget.normalized, out RaycastHit hit, dist, mask))
            {
                if (!hit.collider.CompareTag("Player"))
                {
                    return false;
                }
            }

            float distFactor = 1f - Mathf.Clamp01(dist / effectiveRange);
            sightClarity = Mathf.Clamp01(distFactor * 1.6f);
            return true;
        }

        private void CheckPlayerNoise()
        {
            if (target == null) return;

            var stealth = EndlessCombat.Combat.PlayerStealthController.Instance;
            if (stealth != null)
            {
                float noiseRadius = stealth.GetCurrentNoiseRadius();
                if (noiseRadius > 0.01f)
                {
                    float dist = Vector3.Distance(transform.position, target.position);
                    if (dist <= noiseRadius)
                    {
                        HearSound(target.position, noiseRadius);
                    }
                }
            }
        }

        public void HearSound(Vector3 soundOrigin, float radius)
        {
            if (IsDead) return;

            if (currentState != EnemyState.Combat)
            {
                _investigatePoint = soundOrigin;
                _investigateTimer = investigateDuration;
                suspicionMeter = Mathf.Max(suspicionMeter, 50f);
                SetState(EnemyState.Suspicious);
            }
        }

        public void AlertNearbyTeammates(Vector3 center, float radius)
        {
            Collider[] hits = Physics.OverlapSphere(center, radius);
            foreach (var h in hits)
            {
                var teammate = h.GetComponent<EnemyController>();
                if (teammate != null && teammate != this && !teammate.IsDead)
                {
                    if (teammate.currentState != EnemyState.Combat)
                    {
                        teammate.suspicionMeter = 100f;
                        teammate.SetState(EnemyState.Combat);
                    }
                }
            }
        }

        private void SetupStealthIndicator()
        {
            GameObject indicatorObj = new GameObject("StealthIndicator");
            indicatorObj.transform.SetParent(transform, false);
            indicatorObj.transform.localPosition = new Vector3(0, eyeHeight + 0.5f, 0);

            _stealthIndicator = indicatorObj.AddComponent<TextMesh>();
            _stealthIndicator.alignment = TextAlignment.Center;
            _stealthIndicator.anchor = TextAnchor.MiddleCenter;
            _stealthIndicator.fontSize = 24;
            _stealthIndicator.characterSize = 0.08f;
            _stealthIndicator.fontStyle = FontStyle.Bold;
            _stealthIndicator.text = "";
        }

        private void UpdateStealthIndicator()
        {
            if (_stealthIndicator == null) return;

            // Kameraya dönük tut
            var cam = Camera.main;
            if (cam != null)
            {
                _stealthIndicator.transform.rotation = Quaternion.LookRotation(_stealthIndicator.transform.position - cam.transform.position);
            }

            if (currentState == EnemyState.Combat)
            {
                _stealthIndicator.color = Color.red;
                _stealthIndicator.text = "!";
            }
            else if (currentState == EnemyState.Suspicious || suspicionMeter > 10f)
            {
                _stealthIndicator.color = Color.yellow;
                _stealthIndicator.text = "?";
            }
            else
            {
                _stealthIndicator.text = "";
            }
        }

        #endregion
    }
}
