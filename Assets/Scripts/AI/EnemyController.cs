using UnityEngine;
using UnityEngine.AI;
using EndlessCombat.Combat;
using System.Collections.Generic;

namespace EndlessCombat.AI
{
    [RequireComponent(typeof(NavMeshAgent))]
    public class EnemyController : MonoBehaviour, IDamageable
    {
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
        }

        private void Update()
        {
            if (IsDead || target == null) return;

            float distanceToTarget = Vector3.Distance(transform.position, target.position);
            animator.SetFloat("MotionSpeed", 1f);

            bool isMoving = false;
            float stopDist = 3f;
            Vector3 direction = (target.position - transform.position).normalized;
            direction.y = 0;

            // Yerçekimi (Gravity) Hesaplaması
            if (controller.isGrounded)
            {
                verticalVelocity = -2f; // Yere yapışık kalması için küçük bir eksi değer
            }
            else
            {
                verticalVelocity += gravity * Time.deltaTime; // Havadaysa düş
            }

            Vector3 moveVelocity = Vector3.zero;

            if (distanceToTarget <= attackRange)
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
            else if (distanceToTarget <= detectionRange)
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
                animator.SetBool("IsAiming", false);
            }

            // Hareketi ve yerçekimini uygula
            moveVelocity.y = verticalVelocity;
            controller.Move(moveVelocity * Time.deltaTime);

            animator.SetFloat("Speed", isMoving ? moveSpeed : 0f);
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

            if (currentHealth <= 0)
            {
                Die();
            }
        }

        private void Die()
        {
            IsDead = true;
            if (agent != null) agent.enabled = false;
            
            EnableRagdoll();
            
            Destroy(gameObject, 15f); // 15 saniye sonra cesedi sil
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
    }
}
