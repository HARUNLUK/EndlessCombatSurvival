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
        public EnemyState State => currentState;

        /// <summary>Enemies currently enabled in the scene (used by survival systems: toilet safety, stress).</summary>
        public static readonly System.Collections.Generic.List<EnemyController> Active = new System.Collections.Generic.List<EnemyController>();

        private void OnEnable()
        {
            if (!Active.Contains(this)) Active.Add(this);
        }

        private void OnDisable()
        {
            Active.Remove(this);
        }

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
        [Tooltip("Yakın mesafede atış sapması (derece)")]
        public float aimSpreadClose = 0.5f;
        [Tooltip("attackRange sınırında atış sapması (derece)")]
        public float aimSpreadFar = 2.5f;
        [Tooltip("Hedef görüşten çıksa bile bu süre boyunca nişan almaya devam eder")]
        public float lostSightGrace = 1f;
        private float _lastSeenTime = -999f;

        [Header("Pathfinding")]
        [Tooltip("Seconds between path recalculations while moving")]
        public float repathInterval = 0.3f;
        [Tooltip("Recalculate early when the destination moves more than this (m)")]
        public float repathDestinationDelta = 1.5f;
        [Tooltip("A path corner counts as reached within this horizontal distance (m)")]
        public float cornerReachDistance = 0.6f;
        [Tooltip("How far from the NavMesh the enemy or destination may be and still get a path (m)")]
        public float navMeshSampleDistance = 4f;
        private NavMeshPath _path;
        private readonly Vector3[] _pathCorners = new Vector3[64];
        private int _pathCornerCount;
        private int _pathCornerIndex;
        private float _lastRepathTime = -999f;
        private Vector3 _pathDestination;
        private bool _hasPath;

        [Header("Loot")]
        [Tooltip("Loot rolled and dropped as pickups when this enemy dies")]
        public LootTable dropTable;

        [Tooltip("Item version of the weapon this enemy carries; dropped as a pickup on death (needs Drop Table for the pickup prefab)")]
        public ItemDefinition heldWeaponItem;

        [Tooltip("On spawn, replace the weapon in the hand with Held Weapon Item's weapon prefab (pistol / melee variants)")]
        public bool equipHeldWeaponOnSpawn = false;

        [Header("Melee (melee weapon or no weapon)")]
        [Tooltip("Distance at which a melee fighter stops running and swings (m)")]
        public float meleeRange = 1.5f;
        [Tooltip("Run speed multiplier while a melee fighter charges the player")]
        public float meleeChargeSpeedMultiplier = 1.6f;
        private float _pendingMeleeHitTime = -1f;
        private MeleeStats _meleeStats;

        // Hit reaction (melee hits)
        private float _staggerUntil = -1f;
        private Vector3 _knockbackVelocity;
        private CombatAnimator _combatAnim;
        private WeaponType _weaponType = WeaponType.Rifle;

        public WeaponType WeaponKind => _weaponType;
        private bool IsMeleeFighter => _weaponType == WeaponType.Melee || _weaponType == WeaponType.Unarmed;

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
        private static bool s_missingPlayerLogged;
        private float verticalVelocity;
        private float gravity = -15f;

        private void Start()
        {
            currentState = initialState;
            currentHealth = maxHealth;
            agent = GetComponent<NavMeshAgent>();
            animator = GetComponentInChildren<Animator>();
            if (equipHeldWeaponOnSpawn) EquipHeldWeaponItem();
            weapon = GetComponentInChildren<WeaponController>();
            SetupWeaponType();
            if (animator != null && animator.GetComponent<FootstepEmitter>() == null)
            {
                animator.gameObject.AddComponent<FootstepEmitter>();
            }

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

            // Movement stays on the CharacterController (gravity, collisions); the NavMesh only supplies the route
            if (agent != null) agent.enabled = false;
            _path = new NavMeshPath();

            if (target == null)
            {
                var player = GameObject.FindGameObjectWithTag("Player");
                if (player != null)
                {
                    target = player.transform;
                }
                else if (!s_missingPlayerLogged)
                {
                    // Once per session: every enemy of every camp would log it otherwise (slow, with stack traces)
                    s_missingPlayerLogged = true;
                    Debug.LogError("Düşman oyuncuyu bulamadı! Oyuncunun tag'i 'Player' değil veya Target atanmamış.");
                }
            }

            SetupRagdoll();
            DisableRagdoll();
            SetupStealthIndicator();
        }

        /// <summary>Swaps the weapon in the hand for heldWeaponItem's prefab (keeps the hand socket).</summary>
        private void EquipHeldWeaponItem()
        {
            if (heldWeaponItem == null) return;

            WeaponController old = GetComponentInChildren<WeaponController>(true);
            Transform socket = old != null ? old.transform.parent : null;
            if (socket == null)
            {
                foreach (Transform t in GetComponentsInChildren<Transform>(true))
                {
                    if (t.name.Trim().Equals("WeaponSocket", System.StringComparison.OrdinalIgnoreCase)) { socket = t; break; }
                }
            }
            if (socket == null && animator != null) socket = animator.GetBoneTransform(HumanBodyBones.RightHand);

            if (old != null)
            {
                old.gameObject.SetActive(false);
                Destroy(old.gameObject);
            }

            if (heldWeaponItem.weaponPrefab != null && socket != null)
            {
                WeaponController spawned = Instantiate(heldWeaponItem.weaponPrefab, socket);
                spawned.MakeInfiniteAmmo();
            }
        }

        private void SetupWeaponType()
        {
            _weaponType = weapon != null ? weapon.Type : WeaponType.Unarmed;

            var melee = weapon != null ? weapon.GetComponent<MeleeWeapon>() : null;
            _meleeStats = melee != null ? melee.stats : CombatContent.Instance.fists;

            // Rifles are held twisted 45 degrees; pistols are held straight in front
            if (_weaponType == WeaponType.Pistol) aimRotationOffset = -weapon.AimYawOffset;

            _combatAnim = CombatAnimator.GetOrAdd(animator);
            if (_combatAnim != null)
            {
                _combatAnim.SetWeaponType(_weaponType);
                _combatAnim.SnapWeights();
            }
        }

        /// <summary>Melee hit reaction: short push back, no attacking or walking for the given time.</summary>
        public void Stagger(Vector3 push, float duration)
        {
            if (IsDead || duration <= 0f) return;
            _staggerUntil = Time.time + duration;
            _knockbackVelocity = push;
            _pendingMeleeHitTime = -1f;
            if (_combatAnim != null) _combatAnim.PlayAction(CombatAnimator.HitReact, duration + 0.15f);
        }

        private void Update()
        {
            if (IsDead) return;

            // Staggered by a melee hit: only the push back moves the enemy
            if (Time.time < _staggerUntil && controller != null)
            {
                verticalVelocity = controller.isGrounded ? -2f : verticalVelocity + gravity * Time.deltaTime;
                Vector3 v = _knockbackVelocity;
                v.y = verticalVelocity;
                controller.Move(v * Time.deltaTime);
                _knockbackVelocity = Vector3.MoveTowards(_knockbackVelocity, Vector3.zero, 8f * Time.deltaTime);
                animator.SetBool("IsAiming", false);
                animator.SetFloat("Speed", 0f);
                UpdateStealthIndicator();
                return;
            }

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
            float animSpeed = moveSpeed;
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

            if (currentState == EnemyState.Combat && IsMeleeFighter)
            {
                // Yakın dövüşçü: koşarak gelir, menzile girince vurur
                suspicionMeter = 100f;
                animator.SetBool("IsAiming", false);
                UpdateMeleeCombat(distanceToTarget, direction, ref moveVelocity, ref isMoving, ref animSpeed);
            }
            else if (currentState == EnemyState.Combat)
            {
                // Savaş Durumu
                suspicionMeter = 100f;

                if (canSee) _lastSeenTime = Time.time;
                // Kısa süreli görüş kaybı (animasyon/kemik titremesi) silahı indirmesin
                bool hasSight = canSee || Time.time - _lastSeenTime < lostSightGrace;

                if (distanceToTarget <= attackRange && hasSight)
                {
                    animator.SetBool("IsAiming", true);
                    transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(direction), Time.deltaTime * 10f);

                    if (canSee && Time.time >= lastAttackTime + attackCooldown)
                    {
                        Attack();
                    }

                    if (distanceToTarget > stopDist)
                    {
                        moveVelocity = GetPathDirection(target.position) * (moveSpeed * 0.7f);
                        isMoving = true;
                    }
                }
                else if (distanceToTarget <= detectionRange * 1.3f)
                {
                    animator.SetBool("IsAiming", false);

                    if (distanceToTarget > stopDist)
                    {
                        // Face the walking direction so going around an obstacle looks natural
                        Vector3 pathDir = GetPathDirection(target.position);
                        transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(pathDir), Time.deltaTime * 10f);
                        moveVelocity = pathDir * moveSpeed;
                        isMoving = true;
                    }
                    else
                    {
                        transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(direction), Time.deltaTime * 10f);
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
                    suspicionMeter += suspicionBuildRate * sightClarity * EndlessCombat.Combat.CombatHooks.EnemySuspicionMultiplier() * Time.deltaTime;
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
                        Vector3 pathDir = GetPathDirection(_investigatePoint);
                        transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(pathDir), Time.deltaTime * 6f);
                        moveVelocity = pathDir * (moveSpeed * 0.5f);
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
                    suspicionMeter += suspicionBuildRate * sightClarity * EndlessCombat.Combat.CombatHooks.EnemySuspicionMultiplier() * Time.deltaTime;
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

            animator.SetFloat("Speed", isMoving ? animSpeed : 0f);

            UpdateStealthIndicator();
        }

        private void UpdateMeleeCombat(float distanceToTarget, Vector3 direction, ref Vector3 moveVelocity, ref bool isMoving, ref float animSpeed)
        {
            // Swing in progress: the hit lands after hitDelay if the player is still in reach and in front
            if (_pendingMeleeHitTime > 0f && Time.time >= _pendingMeleeHitTime)
            {
                _pendingMeleeHitTime = -1f;
                Vector3 to = target.position - transform.position;
                to.y = 0f;
                bool inReach = to.magnitude <= _meleeStats.range + 0.6f;
                bool inFront = to.sqrMagnitude < 0.01f || Vector3.Angle(transform.forward, to) < 75f;
                if (inReach && inFront)
                {
                    var victim = target.GetComponentInParent<IDamageable>();
                    if (victim != null && !victim.IsDead)
                    {
                        victim.TakeDamage(_meleeStats.damage, target.position + Vector3.up * 1.1f, -transform.forward);
                    }
                }
            }

            if (distanceToTarget > detectionRange * 1.3f)
            {
                // Lost the player
                SetState(EnemyState.Suspicious);
                _investigatePoint = target.position;
                _investigateTimer = investigateDuration;
                return;
            }

            bool swinging = _pendingMeleeHitTime > 0f || Time.time < lastAttackTime + _meleeStats.hitDelay + 0.25f;

            if (distanceToTarget > meleeRange && !swinging)
            {
                Vector3 pathDir = GetPathDirection(target.position);
                transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(pathDir), Time.deltaTime * 10f);
                animSpeed = moveSpeed * meleeChargeSpeedMultiplier;
                moveVelocity = pathDir * animSpeed;
                isMoving = true;
                return;
            }

            if (direction.sqrMagnitude > 0.001f)
            {
                transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(direction), Time.deltaTime * 12f);
            }

            float cooldown = Mathf.Max(attackCooldown, _meleeStats.attackInterval);
            if (!swinging && distanceToTarget <= meleeRange + 0.2f && Time.time >= lastAttackTime + cooldown)
            {
                lastAttackTime = Time.time;
                _pendingMeleeHitTime = Time.time + _meleeStats.hitDelay;

                string trigger;
                if (_weaponType == WeaponType.Unarmed) trigger = Random.value < 0.5f ? CombatAnimator.PunchJab : CombatAnimator.PunchCross;
                else trigger = _meleeStats.stab ? CombatAnimator.MeleeStab : CombatAnimator.MeleeSwing;
                if (_combatAnim != null) _combatAnim.PlayAction(trigger, Mathf.Max(0.5f, _meleeStats.attackInterval));
            }
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

        /// <summary>
        /// Horizontal unit direction to walk in to reach the destination, following the NavMesh route
        /// around walls and rocks. Falls back to a straight line when there is no NavMesh or no path.
        /// </summary>
        private Vector3 GetPathDirection(Vector3 destination)
        {
            Vector3 straight = destination - transform.position;
            straight.y = 0f;
            straight = straight.sqrMagnitude > 0.0001f ? straight.normalized : transform.forward;

            bool destinationMoved = (destination - _pathDestination).sqrMagnitude > repathDestinationDelta * repathDestinationDelta;
            if (Time.time - _lastRepathTime >= repathInterval || (destinationMoved && Time.time - _lastRepathTime >= 0.1f))
            {
                RecalculatePath(destination);
            }

            if (!_hasPath) return straight;

            while (_pathCornerIndex < _pathCornerCount)
            {
                Vector3 toCorner = _pathCorners[_pathCornerIndex] - transform.position;
                toCorner.y = 0f;
                if (toCorner.magnitude > cornerReachDistance) return toCorner.normalized;
                _pathCornerIndex++;
            }

            return straight;
        }

        private void RecalculatePath(Vector3 destination)
        {
            _lastRepathTime = Time.time;
            _pathDestination = destination;
            _hasPath = false;

            if (LocalNavMeshBuilder.Instance == null || !LocalNavMeshBuilder.Instance.HasNavMesh) return;
            if (!NavMesh.SamplePosition(transform.position, out NavMeshHit fromHit, navMeshSampleDistance, NavMesh.AllAreas)) return;
            if (!NavMesh.SamplePosition(destination, out NavMeshHit toHit, navMeshSampleDistance, NavMesh.AllAreas)) return;
            if (!NavMesh.CalculatePath(fromHit.position, toHit.position, NavMesh.AllAreas, _path)) return;
            if (_path.status == NavMeshPathStatus.PathInvalid) return;

            _pathCornerCount = _path.GetCornersNonAlloc(_pathCorners);
            // Corner 0 is the start point itself
            _pathCornerIndex = _pathCornerCount > 1 ? 1 : 0;
            _hasPath = _pathCornerCount > 0;
        }

        private void Attack()
        {
            lastAttackTime = Time.time;
            
            if (weapon != null)
            {
                // Gövde ortasına nişan al (kafa değil) ki küçük sapmalar ıskalamaya yol açmasın
                Vector3 aimPoint = target.position + Vector3.up * 1.1f;
                LayerMask hitMask = ~0;

                // Mermi animasyondan bağımsız olarak namludan doğrudan hedefe gider; mesafeye göre hafif sapma.
                float dist = Vector3.Distance(transform.position, target.position);
                float spread = Mathf.Lerp(aimSpreadClose, aimSpreadFar, Mathf.Clamp01(dist / attackRange));
                weapon.TryFire(aimPoint, hitMask, out RaycastHit hit, true, spread, transform);
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

            // The user's Mixamo death clip (Rifle Death) when the controller has it, ragdoll otherwise
            if (!PlayDeathAnimation()) EnableRagdoll();

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

        private bool PlayDeathAnimation()
        {
            if (animator == null) return false;
            var layer = EndlessSurvival.Survival.SurvivalAnimLayer.GetOrAdd(animator);
            if (layer == null || !layer.Available || layer.ClipLength(EndlessSurvival.Survival.SurvivalAnimClips.NpcDeath) <= 0f) return false;
            animator.SetFloat("Speed", 0f);
            animator.SetBool("IsAiming", false);
            layer.SetInstant(EndlessSurvival.Survival.SurvAnim.Death);
            var mainCol = GetComponent<Collider>();
            if (mainCol != null) mainCol.enabled = false;
            return true;
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
            // Kendi collider'larımızı (hitbox, silah) ve trigger'ları yok say; aksi halde silah kalkınca
            // kendi kolumuz/silahımız ışını keser, görüş kaybolur, silah iner, tekrar görür... (titreme)
            var hits = Physics.RaycastAll(eyePos, dirToTarget.normalized, dist, mask, QueryTriggerInteraction.Ignore);
            float nearest = float.MaxValue;
            Collider nearestCol = null;
            foreach (var h in hits)
            {
                if (h.collider.transform.IsChildOf(transform)) continue;
                if (h.distance < nearest)
                {
                    nearest = h.distance;
                    nearestCol = h.collider;
                }
            }

            if (nearestCol != null)
            {
                bool isPlayer = nearestCol.CompareTag("Player") || nearestCol.transform.IsChildOf(target);
                if (!isPlayer) return false;
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
