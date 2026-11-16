using UnityEngine;
using UnityEngine.AI;
using EndlessCombat.Combat;
using System.Collections.Generic;
using EndlessSurvival.Inventory;

namespace EndlessCombat.AI
{
    /// <summary>
    /// An enemy soldier. Perception (sight, hearing, suspicion) and the non combat states live here. In combat the
    /// decisions come from EnemyBrain (roles, distance bands, cover, attack tokens: Core_Feel_Plan stage 4), the
    /// walking from EnemyMover (route, steering, stuck handling), sight checks from EnemyPerception, the shared memory
    /// and tokens from EnemySquad, the strikes / hit reactions / shield from CombatBody (the player's simple melee,
    /// stage 2: one strike per press, no combos, rolls or heavy attacks).
    /// </summary>
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

        private bool _started;

        private void OnEnable()
        {
            if (!Active.Contains(this)) Active.Add(this);
            if (_started && !IsDead && _squad == null) _squad = EnemySquad.Join(this);
        }

        private void OnDisable()
        {
            Active.Remove(this);
            LeaveSquad();
        }

        private void LeaveSquad()
        {
            if (_squad != null) _squad.Leave(this);
            else EnemySquad.ReleaseToken(this);
            _squad = null;
            _brain?.Reset();
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
        [Tooltip("How many enemies walk to a noise; the others only turn to look and stay alert")]
        public int maxNoiseInvestigators = 2;
        [Tooltip("After seeing or hearing the player itself, noises elsewhere (a thrown rock) are ignored for this long (s)")]
        public float distractionIgnoreSeconds = 10f;
        private bool _investigateLookOnly;
        private float _playerEvidenceTime = -999f;
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
        [Tooltip("How far from the NavMesh the enemy or destination may be and still get a path (m)")]
        public float navMeshSampleDistance = 4f;

        [Header("Loot")]
        [Tooltip("Loot rolled and dropped as pickups when this enemy dies")]
        public LootTable dropTable;

        [Tooltip("Item version of the weapon this enemy carries; dropped as a pickup on death (needs Drop Table for the pickup prefab)")]
        public ItemDefinition heldWeaponItem;

        [Tooltip("On spawn, replace the weapon in the hand with Held Weapon Item's weapon prefab (pistol / melee variants)")]
        public bool equipHeldWeaponOnSpawn = false;

        [Header("Ammo (firearms)")]
        [Tooltip("Total rounds a rifle enemy carries (random between x and y). When they are gone it fights hand to hand")]
        public Vector2Int rifleRounds = new Vector2Int(14, 24);
        [Tooltip("Total rounds a pistol enemy carries (random between x and y)")]
        public Vector2Int pistolRounds = new Vector2Int(7, 13);
        [Tooltip("Rounds per magazine: rifle / pistol. The enemy stops to reload between magazines")]
        public Vector2Int magazineRounds = new Vector2Int(8, 6);
        [Tooltip("Chance that an enemy out of ammo draws a knife instead of using its fists")]
        [Range(0f, 1f)] public float backupKnifeChance = 0.5f;

        [Header("Damage dealt (the same weapons are much deadlier in the player's hands)")]
        [Tooltip("Multiplier for this enemy's bullets")]
        public float firearmDamageMultiplier = 0.55f;
        [Tooltip("Multiplier for this enemy's melee hits")]
        public float meleeDamageMultiplier = 0.6f;
        [Tooltip("A melee enemy closer than this steps back to keep some room (m)")]
        public float meleeMinDistance = 1.05f;

        private bool _outOfAmmo;
        private WeaponController _stowedGun;

        /// <summary>The gun is empty and put away: this enemy now fights hand to hand.</summary>
        public bool OutOfAmmo => _outOfAmmo;

        [Header("Melee (melee weapon or no weapon)")]
        [Tooltip("Distance at which a melee fighter stops running and strikes (m)")]
        public float meleeRange = 1.5f;
        [Tooltip("Run speed multiplier while a melee fighter charges the player (with an attack token)")]
        public float meleeChargeSpeedMultiplier = 1.6f;
        [Tooltip("Shield carried on the left arm (on the back with a rifle or two handed weapon). Dropped on death, breaks when worn out")]
        public ItemDefinition heldShieldItem;

        private CombatBody _body;
        private MeleeCategory _meleeCategory = MeleeCategory.Fists;
        private float _nextMeleeTime;
        private MeleeStats _meleeStats;
        private CombatAnimator _combatAnim;
        private WeaponType _weaponType = WeaponType.Rifle;
        private EnemyMover _mover;
        private EnemyBrain _brain;
        private EnemySquad _squad;

        public WeaponType WeaponKind => _weaponType;
        private bool IsMeleeFighter => _weaponType == WeaponType.Melee || _weaponType == WeaponType.Unarmed;
        /// <summary>Fights hand to hand (melee weapon, fists, or out of ammo).</summary>
        public bool IsMeleeRole => IsMeleeFighter;
        /// <summary>A shield on the arm (not on the back), not broken.</summary>
        public bool HasUsableShield => _body != null && _body.UsingShield;
        /// <summary>Shield on the arm and raised: counts as cover for the squad's gunners.</summary>
        public bool HasRaisedShield => !IsDead && _body != null && _body.IsGuarding;
        public float Health01 => maxHealth > 0f ? Mathf.Clamp01(currentHealth / maxHealth) : 1f;
        public EnemySquad Squad => _squad;
        public bool IsReloading => weapon != null && weapon.IsFirearm && weapon.IsReloading;
        public EnemyMover Mover => _mover;
        public EnemyBrain Brain => _brain;

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
            SetupAmmo();
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

            _mover = new EnemyMover(this);
            _brain = new EnemyBrain(this);
            SetupRagdoll();
            DisableRagdoll();
            SetupStealthIndicator();
            SetupCombatBody();
            _squad = EnemySquad.Join(this);
            _started = true;
            RegisterTuning();
        }

        /// <summary>Combat body: the same simple melee rules as the player (one strike, short hit reaction, shield).</summary>
        private void SetupCombatBody()
        {
            if (heldShieldItem != null)
            {
                var holder = ShieldHolder.GetOrAdd(gameObject);
                holder.Set(heldShieldItem);
                holder.SetOnBack(_weaponType == WeaponType.Rifle || _meleeCategory == MeleeCategory.TwoHand);
                holder.Broken -= OnShieldBroken;
                holder.Broken += OnShieldBroken;
            }

            _body = CombatBody.GetOrAdd(gameObject, CombatTeam.Enemy);
            _body.team = CombatTeam.Enemy;
            _body.RefreshShield();
            _body.AimDirection = () =>
            {
                if (target == null) return Vector3.zero;
                Vector3 d = target.position - transform.position;
                d.y = 0f;
                return d;
            };
            // Stealth kill (knife from behind) only works on an enemy that is not fighting
            _body.Unaware = () => currentState != EnemyState.Combat;
            _body.HitReceived -= OnHitReceived;
            _body.HitReceived += OnHitReceived;
            _body.SwingFinished -= OnSwingFinished;
            _body.SwingFinished += OnSwingFinished;
        }

        private void OnHitReceived(HitResult result, CombatBody attacker)
        {
            if (IsDead) return;
            suspicionMeter = 100f;
            if (currentState != EnemyState.Combat)
            {
                SetState(EnemyState.Combat);
                AlertNearbyTeammates(transform.position, 25f);
            }
        }

        /// <summary>One strike is over (hit, missed or cut by a hit reaction): the token goes to the next one, a short wait.</summary>
        private void OnSwingFinished(int hits)
        {
            _brain?.StrikeDone();
            _nextMeleeTime = Time.time + attackCooldown * Random.Range(0.7f, 1.3f);
        }

        /// <summary>The shield broke (the pieces fall off in ShieldHolder): nothing is dropped on death any more.</summary>
        private void OnShieldBroken(ItemDefinition item)
        {
            heldShieldItem = null;
            if (_body != null)
            {
                _body.SetGuard(false);
                _body.RefreshShield();
            }
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
                Instantiate(heldWeaponItem.weaponPrefab, socket);
            }
        }

        private void SetupWeaponType()
        {
            _weaponType = weapon != null ? weapon.Type : WeaponType.Unarmed;

            var melee = weapon != null ? weapon.GetComponent<MeleeWeapon>() : null;
            _meleeStats = melee != null ? melee.stats : CombatContent.Instance.fists;
            _meleeStats.damage *= Mathf.Max(0f, meleeDamageMultiplier);
            _meleeCategory = melee != null ? melee.stats.ResolveCategory(false) : MeleeCategory.Fists;

            // Rifles are held twisted 45 degrees; pistols are held straight in front
            if (_weaponType == WeaponType.Pistol) aimRotationOffset = -weapon.AimYawOffset;

            _combatAnim = CombatAnimator.GetOrAdd(animator);
            if (_combatAnim != null)
            {
                _combatAnim.SetWeaponType(_weaponType);
                _combatAnim.SnapWeights();
            }
        }

        /// <summary>Firearm enemies carry only a few rounds (one or two short magazines).</summary>
        private void SetupAmmo()
        {
            if (weapon == null || !weapon.IsFirearm) return;
            bool pistol = weapon.Type == WeaponType.Pistol;
            Vector2Int range = pistol ? pistolRounds : rifleRounds;
            int total = Random.Range(Mathf.Min(range.x, range.y), Mathf.Max(range.x, range.y) + 1);
            int mag = Mathf.Clamp(pistol ? magazineRounds.y : magazineRounds.x, 1, Mathf.Max(1, total));
            weapon.MakeLimitedAmmo(mag, Mathf.Max(0, total - mag));
            weapon.DamageMultiplier = firearmDamageMultiplier;
        }

        /// <summary>
        /// Out of rounds: the gun goes on the back and the enemy fights with a knife or its fists. From here on it
        /// has the melee role (circles, charges with an attack token, one strike).
        /// </summary>
        private void SwitchToMelee()
        {
            if (_outOfAmmo) return;
            _outOfAmmo = true;
            animator.SetBool("IsAiming", false);

            // Gun on the back (it still drops on death)
            _stowedGun = weapon;
            Transform socket = weapon != null ? weapon.transform.parent : null;
            if (weapon != null)
            {
                weapon.CancelReload();
                Transform chest = animator != null ? animator.GetBoneTransform(HumanBodyBones.Chest) : null;
                if (chest == null && animator != null) chest = animator.GetBoneTransform(HumanBodyBones.Spine);
                if (chest != null)
                {
                    weapon.transform.SetParent(chest, false);
                    weapon.transform.localPosition = new Vector3(0.18f, 0.02f, -0.15f);
                    weapon.transform.localRotation = Quaternion.Euler(25f, 155f, -35f);
                }
                else weapon.gameObject.SetActive(false);
            }

            // Backup knife for some, fists for the rest
            WeaponController melee = null;
            if (socket != null && Random.value < backupKnifeChance)
            {
                var settings = EndlessSurvival.Survival.SurvivalSettings.Instance;
                var knife = settings != null ? settings.Item("Item_Knife") : null;
                if (knife != null && knife.weaponPrefab != null) melee = Instantiate(knife.weaponPrefab, socket);
            }
            weapon = melee;
            SetupWeaponType();

            var holder = GetComponent<ShieldHolder>();
            if (holder != null && holder.HasShield) holder.SetOnBack(false);
            _body?.RefreshShield();
            _nextMeleeTime = Time.time + 0.4f;
        }

        /// <summary>Melee hit reaction: short push back, no attacking or walking for the given time.</summary>
        public void Stagger(Vector3 push, float duration)
        {
            if (IsDead || duration <= 0f) return;
            _body?.Stagger(duration, push);
        }

        private void Update()
        {
            if (IsDead) return;
            UpdateDebug();

            // Own strike or hit reaction: the CombatBody moves the body
            if (_body != null && _body.IsLocked && controller != null)
            {
                verticalVelocity = controller.isGrounded ? -2f : verticalVelocity + gravity * Time.deltaTime;
                controller.Move(new Vector3(0f, verticalVelocity, 0f) * Time.deltaTime);
                animator.SetBool("IsAiming", false);
                animator.SetFloat("Speed", 0f);
                _mover.TrackProgress(false);
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
            if (canSee) _playerEvidenceTime = Time.time;

            // Ayak sesi / gürültü duyma kontrolü
            CheckPlayerNoise();

            if (currentState == EnemyState.Combat)
            {
                suspicionMeter = 100f;
                UpdateCombat(canSee, distanceToTarget, direction, ref moveVelocity, ref isMoving, ref animSpeed);
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
                    if (Time.time >= _glintHoldUntil) suspicionMeter = Mathf.MoveTowards(suspicionMeter, 0f, suspicionDecayRate * Time.deltaTime);

                    Vector3 toInvestigate = (_investigatePoint - transform.position);
                    toInvestigate.y = 0;
                    if (_investigateLookOnly)
                    {
                        // Others went to check: this one only turns toward the noise and watches
                        if (toInvestigate.sqrMagnitude > 0.05f)
                            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(toInvestigate.normalized), Time.deltaTime * 4f);
                    }
                    else if (toInvestigate.magnitude > 1.5f && _investigateTimer > 0f)
                    {
                        Vector3 pathDir = _mover.DirectionTo(_investigatePoint);
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
                        _investigateLookOnly = false;
                        _investigateTimer = investigateDuration;
                        SetState(EnemyState.Suspicious);
                    }
                }
                else
                {
                    if (Time.time >= _glintHoldUntil) suspicionMeter = Mathf.MoveTowards(suspicionMeter, 0f, suspicionDecayRate * Time.deltaTime);
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

            // A raised shield slows the walk
            if (_body != null)
            {
                if (currentState != EnemyState.Combat) _body.SetGuard(false);
                moveVelocity.x *= _body.MoveSpeedMultiplier;
                moveVelocity.z *= _body.MoveSpeedMultiplier;
                if (_body.MoveSpeedMultiplier < 0.99f) animSpeed *= _body.MoveSpeedMultiplier;
            }

            // Stuck check (route rebuild, then a short detour); the steering is inside the mover
            _mover.TrackProgress(isMoving);

            // Hareketi ve yerçekimini uygula
            moveVelocity.y = verticalVelocity;
            controller.Move(moveVelocity * Time.deltaTime);

            animator.SetFloat("Speed", isMoving ? animSpeed : 0f);

            UpdateStealthIndicator();
        }

        /// <summary>
        /// Carries out EnemyBrain's decision this frame: walk to its goal, face the player or the way, aim and shoot
        /// (gunners with an attack token), strike (melee with a token), shield up (shield role).
        /// </summary>
        private void UpdateCombat(bool canSee, float dist, Vector3 toPlayer, ref Vector3 moveVelocity, ref bool isMoving, ref float animSpeed)
        {
            if (canSee) _lastSeenTime = Time.time;
            // Kısa süreli görüş kaybı (animasyon/kemik titremesi) silahı indirmesin
            bool hasSight = canSee || Time.time - _lastSeenTime < lostSightGrace;
            float sinceSeen = Time.time - _lastSeenTime;

            if (dist > detectionRange * 1.3f && !canSee)
            {
                // Oyuncu çok uzaklaştı veya tamamen kaçtı
                animator.SetBool("IsAiming", false);
                SetState(EnemyState.Suspicious);
                _investigatePoint = target.position;
                _investigateLookOnly = false;
                _investigateTimer = investigateDuration;
                return;
            }

            // Limited ammo: reload between magazines (a pause the player can use), then hand to hand
            bool reloading = false;
            if (weapon != null && weapon.IsFirearm)
            {
                if (weapon.OutOfAmmo)
                {
                    SwitchToMelee();
                    return;
                }
                if (weapon.NeedsReload && !weapon.IsReloading) weapon.TryReload();
                reloading = weapon.IsReloading;
            }

            _brain.Tick(target, canSee, sinceSeen);
            EnemyBrain.Act act = _brain.Action;
            bool melee = IsMeleeFighter;
            bool gunner = !melee && weapon != null && weapon.IsFirearm;
            if (_body != null) _body.SetGuard(_brain.Guard && _body.UsingShield);

            // Walk to the brain's goal (a charge stops at striking distance)
            bool charge = act == EnemyBrain.Act.Charge;
            Vector3 toGoal = _brain.Goal - transform.position;
            toGoal.y = 0f;
            float goalDist = charge ? dist : toGoal.magnitude;
            float stopAt = charge ? meleeRange + 0.3f : 0.5f;
            Vector3 moveDir = Vector3.zero;
            if (_brain.Moving && goalDist > stopAt)
            {
                moveDir = _mover.DirectionTo(charge ? target.position : _brain.Goal);
                float speed = _brain.Speed;
                // The last steps of a charge are walked (spacing), the run is from further away
                if (charge && dist < 4f) speed = Mathf.Min(speed, moveSpeed * 0.8f);
                moveVelocity = moveDir * speed;
                animSpeed = speed;
                isMoving = true;
            }

            // Too close (pushed in by the strikes): step back so the two do not stand inside each other
            if (melee && dist < meleeMinDistance)
            {
                float back = moveSpeed * 0.55f;
                moveDir = Vector3.zero;
                moveVelocity = -toPlayer * back;
                animSpeed = back;
                isMoving = true;
            }

            // Facing: the player while shooting / backing off / close, else the walking direction
            bool shootPose = gunner && !reloading && _brain.HasToken && hasSight && dist <= attackRange
                             && act != EnemyBrain.Act.Flee && act != EnemyBrain.Act.ToCover && act != EnemyBrain.Act.Flank;
            bool facePlayer = shootPose || _brain.FacePlayer || (melee && dist <= meleeRange + 1f) || (!isMoving && hasSight)
                              || moveDir.sqrMagnitude < 0.01f;
            Turn(facePlayer ? toPlayer : moveDir, _brain.TurnSpeed);

            // Only token holders shoot; the gun is raised once it points roughly at the player
            bool facing = Vector3.Angle(transform.forward, toPlayer) < 35f;
            animator.SetBool("IsAiming", shootPose);
            if (shootPose && isMoving)
            {
                moveVelocity *= 0.7f;
                animSpeed *= 0.7f;
            }
            if (shootPose && canSee && facing && Time.time >= lastAttackTime + attackCooldown) Attack();

            // Only token holders strike
            if (melee && _body != null && _brain.HasToken && dist <= meleeRange + 0.3f && Time.time >= _nextMeleeTime
                && Vector3.Angle(transform.forward, toPlayer) < 45f)
            {
                if (_body.TryAttack(_meleeStats, _meleeCategory)) lastAttackTime = Time.time;
            }
        }

        /// <summary>Turns toward dir; slow turners (shield) use a fixed rate so the player can get around them.</summary>
        private void Turn(Vector3 dir, float degreesPerSecond)
        {
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.0001f) return;
            Quaternion want = Quaternion.LookRotation(dir.normalized);
            if (degreesPerSecond < 300f)
                transform.rotation = Quaternion.RotateTowards(transform.rotation, want, degreesPerSecond * Time.deltaTime);
            else
                transform.rotation = Quaternion.Slerp(transform.rotation, want, Time.deltaTime * 10f);
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

        // ------------------------------------------------------------------ tuning panel, page "Düşman"

        /// <summary>Draws the route of the enemy nearest to the player (panel toggle).</summary>
        public static bool DebugDraw;
        private static bool s_tuningRegistered;
        private static EnemyController s_debugNearest;
        private static int s_debugFrame = -1;
        private static LineRenderer s_debugLine;

        private static void RegisterTuning()
        {
            if (s_tuningRegistered) return;
            s_tuningRegistered = true;
            const string P = "Düşman";
            Tuning.TuningPanel.Register(P, "Aynı anda saldıran (jeton)", () => EnemySquad.MaxAttackers, v => EnemySquad.MaxAttackers = Mathf.RoundToInt(v), 1f, 6f, 1f);
            Tuning.TuningPanel.Register(P, "Jeton tutma süresi (sn)", () => EnemySquad.HoldTime, v => EnemySquad.HoldTime = v, 1f, 15f, 0.5f);
            Tuning.TuningPanel.Register(P, "Jeton bekleme (sn)", () => EnemySquad.RequestCooldown, v => EnemySquad.RequestCooldown = v, 0f, 5f, 0.1f);
            Tuning.TuningPanel.Register(P, "Tüfek mesafe alt (m)", () => EnemyBrain.RifleBand.x, v => EnemyBrain.RifleBand.x = v, 3f, 30f, 0.5f);
            Tuning.TuningPanel.Register(P, "Tüfek mesafe üst (m)", () => EnemyBrain.RifleBand.y, v => EnemyBrain.RifleBand.y = v, 5f, 40f, 0.5f);
            Tuning.TuningPanel.Register(P, "Tabanca mesafe alt (m)", () => EnemyBrain.PistolBand.x, v => EnemyBrain.PistolBand.x = v, 2f, 20f, 0.5f);
            Tuning.TuningPanel.Register(P, "Tabanca mesafe üst (m)", () => EnemyBrain.PistolBand.y, v => EnemyBrain.PistolBand.y = v, 3f, 30f, 0.5f);
            Tuning.TuningPanel.RegisterToggle(P, "Siper kullan", () => EnemyBrain.UseCover, v => EnemyBrain.UseCover = v);
            Tuning.TuningPanel.Register(P, "Siper arama yarıçapı (m)", () => EnemyBrain.CoverRadius, v => EnemyBrain.CoverRadius = v, 3f, 30f, 0.5f);
            Tuning.TuningPanel.Register(P, "Siperden ateş en az (sn)", () => EnemyBrain.PeekTime.x, v => EnemyBrain.PeekTime.x = v, 0.3f, 5f, 0.1f);
            Tuning.TuningPanel.Register(P, "Siperden ateş en çok (sn)", () => EnemyBrain.PeekTime.y, v => EnemyBrain.PeekTime.y = v, 0.3f, 6f, 0.1f);
            Tuning.TuningPanel.Register(P, "Siperde bekleme (sn)", () => EnemyBrain.HideTime.x, v => { EnemyBrain.HideTime.x = v; EnemyBrain.HideTime.y = v + 1f; }, 0.3f, 6f, 0.1f);
            Tuning.TuningPanel.Register(P, "Kanattan dolanma gecikmesi (sn)", () => EnemyBrain.FlankDelay.x, v => { EnemyBrain.FlankDelay.x = v; EnemyBrain.FlankDelay.y = v + 2f; }, 1f, 20f, 0.5f);
            Tuning.TuningPanel.Register(P, "Yakın dövüş çember (m)", () => EnemyBrain.CircleRadius.x, v => { EnemyBrain.CircleRadius.x = v; EnemyBrain.CircleRadius.y = v + 2f; }, 2f, 12f, 0.5f);
            Tuning.TuningPanel.Register(P, "Kalkanlı dönüş hızı (°/sn)", () => EnemyBrain.ShieldTurnSpeed, v => EnemyBrain.ShieldTurnSpeed = v, 20f, 300f, 5f);
            Tuning.TuningPanel.Register(P, "Kaçma canı (oran)", () => EnemyBrain.WoundedFraction, v => EnemyBrain.WoundedFraction = v, 0f, 0.8f, 0.05f);
            Tuning.TuningPanel.Register(P, "Karar aralığı (sn)", () => EnemyBrain.DecideInterval.x, v => { EnemyBrain.DecideInterval.x = v; EnemyBrain.DecideInterval.y = v * 2f; }, 0.05f, 1.5f, 0.05f);
            Tuning.TuningPanel.Register(P, "Takılma: ilerleme (m)", () => EnemyMover.StuckDistance, v => EnemyMover.StuckDistance = v, 0.05f, 1.5f, 0.05f);
            Tuning.TuningPanel.RegisterToggle(P, "Yakın düşmanın rotasını çiz", () => DebugDraw, v =>
            {
                DebugDraw = v;
                if (!v && s_debugLine != null) s_debugLine.enabled = false;
            });
            Tuning.TuningPanel.RegisterReadout(P, "NavMesh kurulu", () =>
                LocalNavMeshBuilder.Instance != null && LocalNavMeshBuilder.Instance.HasNavMesh ? "evet" : "hayır");
            Tuning.TuningPanel.RegisterReadout(P, "Saldırı jetonu dolu", () => EnemySquad.TokenCount + " / " + EnemySquad.MaxAttackers);
            Tuning.TuningPanel.RegisterReadout(P, "Yakın düşman: rol", () =>
            {
                var e = DebugNearest();
                return e == null || e._brain == null ? "-" : RoleText(e._brain.CurrentRole) + (e._brain.HasToken ? " (jetonlu)" : "");
            });
            Tuning.TuningPanel.RegisterReadout(P, "Yakın düşman: davranış", () =>
            {
                var e = DebugNearest();
                if (e == null || e._brain == null) return "-";
                return e.currentState != EnemyState.Combat ? StateText(e.currentState) : ActText(e._brain.Action);
            });
            Tuning.TuningPanel.RegisterReadout(P, "Yakın düşman: rota", () =>
            {
                var e = DebugNearest();
                if (e == null || e._mover == null) return "-";
                return RouteText(e._mover.Route) + ", köşe " + e._mover.CornerCount + (e._mover.Detouring ? ", dolanıyor" : "");
            });
            Tuning.TuningPanel.RegisterReadout(P, "Yakın düşman: takılma", () =>
            {
                var e = DebugNearest();
                return e == null || e._mover == null ? "-" : e._mover.StuckCount.ToString();
            });
            Tuning.TuningPanel.RegisterReadout(P, "Yakın düşman: can", () =>
            {
                var e = DebugNearest();
                return e == null ? "-" : Mathf.RoundToInt(e.Health01 * 100f) + "%";
            });
        }

        private static EnemyController DebugNearest()
        {
            if (s_debugFrame == Time.frameCount) return s_debugNearest;
            s_debugFrame = Time.frameCount;
            s_debugNearest = null;
            Transform p = CombatBody.Player != null ? CombatBody.Player.transform : null;
            if (p == null) return null;
            float best = float.MaxValue;
            foreach (var e in Active)
            {
                if (e == null || e.IsDead) continue;
                float d = (e.transform.position - p.position).sqrMagnitude;
                if (d < best)
                {
                    best = d;
                    s_debugNearest = e;
                }
            }
            return s_debugNearest;
        }

        private void UpdateDebug()
        {
            if (!DebugDraw || DebugNearest() != this || _mover == null || _brain == null) return;
            if (s_debugLine == null)
            {
                var go = new GameObject("EnemyDebugPath");
                DontDestroyOnLoad(go);
                s_debugLine = go.AddComponent<LineRenderer>();
                s_debugLine.widthMultiplier = 0.06f;
                Shader sh = Shader.Find("Sprites/Default");
                if (sh == null) sh = Shader.Find("Universal Render Pipeline/Unlit");
                if (sh != null) s_debugLine.material = new Material(sh);
                s_debugLine.startColor = s_debugLine.endColor = Color.cyan;
                s_debugLine.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                s_debugLine.receiveShadows = false;
            }
            s_debugLine.enabled = true;
            Vector3 up = Vector3.up * 0.25f;
            int n = _mover.CornerCount;
            s_debugLine.positionCount = n + 2;
            s_debugLine.SetPosition(0, transform.position + up);
            for (int i = 0; i < n; i++) s_debugLine.SetPosition(i + 1, _mover.Corner(i) + up);
            s_debugLine.SetPosition(n + 1, (currentState == EnemyState.Combat ? _brain.Goal : _mover.Goal) + up);
        }

        private static string RoleText(EnemyBrain.Role r)
        {
            switch (r)
            {
                case EnemyBrain.Role.Rifle: return "tüfekli";
                case EnemyBrain.Role.Pistol: return "tabancalı";
                case EnemyBrain.Role.Shield: return "kalkanlı";
                default: return "yakın dövüş";
            }
        }

        private static string ActText(EnemyBrain.Act a)
        {
            switch (a)
            {
                case EnemyBrain.Act.Advance: return "yaklaşıyor";
                case EnemyBrain.Act.Retreat: return "geri çekiliyor";
                case EnemyBrain.Act.Flank: return "kanattan dolanıyor";
                case EnemyBrain.Act.ToCover: return "sipere gidiyor";
                case EnemyBrain.Act.InCover: return "siperde";
                case EnemyBrain.Act.Peek: return "siperden ateş";
                case EnemyBrain.Act.Circle: return "çevrede dolanıyor";
                case EnemyBrain.Act.Charge: return "saldırıyor";
                case EnemyBrain.Act.Flee: return "kaçıyor";
                default: return "bekliyor";
            }
        }

        private static string StateText(EnemyState s)
        {
            switch (s)
            {
                case EnemyState.Suspicious: return "şüpheli";
                case EnemyState.Sitting: return "oturuyor";
                case EnemyState.Patrolling: return "devriye";
                default: return "boşta";
            }
        }

        private static string RouteText(EnemyMover.RouteState r)
        {
            switch (r)
            {
                case EnemyMover.RouteState.Full: return "tam";
                case EnemyMover.RouteState.Partial: return "yarım (hedefe varmıyor)";
                case EnemyMover.RouteState.NoNavMesh: return "NavMesh yok (düz yürüyor)";
                case EnemyMover.RouteState.NoPath: return "rota yok (düz yürüyor)";
                default: return "-";
            }
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

        public void TakeDamage(float damage, Vector3 hitPoint, Vector3 hitNormal) =>
            TakeDamage(new DamageInfo(damage, DamageType.Other, hitPoint, hitNormal));

        public void TakeDamage(DamageInfo info)
        {
            if (IsDead) return;
            float damage = info.Amount;

            // A raised shield stops the player's bullets from the front (and wears)
            if (_body != null)
            {
                damage = _body.FilterDamage(info);
                if (damage <= 0f)
                {
                    suspicionMeter = 100f;
                    SetState(EnemyState.Combat);
                    return;
                }
            }

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

        /// <summary>
        /// Hit by a vehicle (VehicleRam): skips shield / roll i-frames. A lethal hit goes straight to the ragdoll
        /// and throws it along the impulse (m/s); otherwise a heavy stagger and the usual alert reaction.
        /// </summary>
        public void HitByVehicle(float damage, Vector3 impulse, bool lethal)
        {
            if (IsDead) return;
            currentHealth -= lethal ? Mathf.Max(damage, currentHealth) : damage;
            suspicionMeter = 100f;
            SetState(EnemyState.Combat);
            AlertNearbyTeammates(transform.position, 25f);

            if (currentHealth <= 0f)
            {
                Die(true, impulse);
                return;
            }

            Vector3 push = new Vector3(impulse.x, 0f, impulse.z);
            push = Vector3.ClampMagnitude(push * 0.25f, 6f);
            if (_body != null) _body.Stagger(1.2f, push);
        }

        private System.Collections.IEnumerator PushRagdollNextFixedUpdate(Vector3 velocityChange)
        {
            // The bodies only turn non-kinematic at the next physics step
            yield return new WaitForFixedUpdate();
            foreach (var rb in ragdollRigidbodies)
                if (rb != null && !rb.isKinematic) rb.AddForce(velocityChange, ForceMode.VelocityChange);
        }

        private void Die() => Die(false, Vector3.zero);

        private void Die(bool vehicleHit, Vector3 vehicleImpulse)
        {
            IsDead = true;
            if (agent != null) agent.enabled = false;
            if (_body != null) _body.Kill();
            LeaveSquad();
            DropShield();

            EndlessSurvival.World.GameStatsTracker.Instance?.RecordEnemyKilled();

            // The user's Mixamo death clip (Rifle Death) when the controller has it, ragdoll otherwise
            if (vehicleHit)
            {
                // Run over: no death clip, the body is thrown
                EnableRagdoll();
                StartCoroutine(PushRagdollNextFixedUpdate(vehicleImpulse));
            }
            else if (!PlayDeathAnimation()) EnableRagdoll();

            LootChest corpseChest = CreateCorpseLoot();
            DropHeldWeapon();

            StartCoroutine(RemoveCorpseLater(corpseChest));
        }

        private const float CorpseLifetime = 180f;

        /// <summary>Searchable loot on the body: a small trigger child under the hips so it follows a thrown ragdoll.</summary>
        private LootChest CreateCorpseLoot()
        {
            if (dropTable == null) return null;

            Transform hips = animator != null && animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.Hips) : null;
            if (hips == null && ragdollRigidbodies != null && ragdollRigidbodies.Count > 0 && ragdollRigidbodies[0] != null)
                hips = ragdollRigidbodies[0].transform;
            Transform parent = hips != null ? hips : transform;

            var go = new GameObject("CorpseLoot");
            go.transform.SetParent(parent, false);
            var sphere = go.AddComponent<SphereCollider>();
            sphere.isTrigger = true;
            sphere.radius = 0.35f;

            var chest = go.AddComponent<LootChest>();
            chest.openPrompt = "Cesedi ara";
            chest.emptyPrompt = "Ceset (boş)";
            chest.containerTitle = "Ceset";
            chest.FillFrom(dropTable, new EndlessSurvival.World.SeededRandom(Random.Range(1, int.MaxValue)));
            return chest;
        }

        private System.Collections.IEnumerator RemoveCorpseLater(LootChest chest)
        {
            yield return new WaitForSeconds(CorpseLifetime);

            // Do not pull the body out from under an open search window
            while (chest != null && InventoryUI.Instance != null && InventoryUI.Instance.IsShowing(chest))
                yield return new WaitForSeconds(1f);

            Destroy(gameObject);
        }

        private void DropShield()
        {
            if (heldShieldItem == null) return;
            var holder = GetComponent<ShieldHolder>();
            if (holder != null) holder.Hide(true);
            if (dropTable == null || dropTable.pickupPrefab == null) return;
            WorldPickup.Spawn(dropTable.pickupPrefab, heldShieldItem, 1, transform.position + Vector3.up * 0.8f + transform.forward * 0.4f, null);
        }

        private void DropHeldWeapon()
        {
            if (heldWeaponItem == null || dropTable == null || dropTable.pickupPrefab == null) return;

            WeaponController held = _stowedGun != null ? _stowedGun : weapon;
            Vector3 position = held != null ? held.transform.position : transform.position + Vector3.up;
            WorldPickup.Spawn(dropTable.pickupPrefab, heldWeaponItem, 1, position, null);

            if (held != null) held.gameObject.SetActive(false);
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
            if (newState != EnemyState.Combat) _brain?.Reset();
            
            if (newState == EnemyState.Combat)
            {
                // Stop dialogue if alerted
                var dialogue = GetComponent<EnemyDialogue>();
                if (dialogue != null) dialogue.StopSpeaking();
            }
        }

        #region Stealth & Perception Helpers

        private bool CanSeePlayer(out float sightClarity) => EnemyPerception.CanSee(this, target, out sightClarity);

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

        private float _glintHoldUntil = -1f;

        /// <summary>
        /// Binocular lens glint: slowly raises suspicion (not above maxSuspicion) without the enemy seeing the player.
        /// While it lasts the meter does not decay. Above 25 the enemy turns suspicious and walks towards the glint.
        /// </summary>
        public void AddGlintSuspicion(float amount, float maxSuspicion, Vector3 source)
        {
            if (IsDead || currentState == EnemyState.Combat || amount <= 0f) return;
            _glintHoldUntil = Time.time + 0.3f;
            if (suspicionMeter < maxSuspicion) suspicionMeter = Mathf.Min(maxSuspicion, suspicionMeter + amount);
            if (suspicionMeter > 25f && currentState != EnemyState.Suspicious)
            {
                _investigatePoint = source;
                _investigateTimer = investigateDuration;
                _investigateLookOnly = false;
                SetState(EnemyState.Suspicious);
            }
        }

        public void HearSound(Vector3 soundOrigin, float radius)
        {
            if (IsDead || currentState == EnemyState.Combat) return;

            // A noise the player makes where he stands (steps, a shot, a swing) tells where he is. A noise somewhere
            // else (a thrown rock) is a distraction: it does not work on someone who has just seen or heard the player.
            bool atPlayer = target != null && (soundOrigin - target.position).sqrMagnitude < 3f * 3f;
            if (atPlayer) _playerEvidenceTime = Time.time;
            else if (Time.time - _playerEvidenceTime < distractionIgnoreSeconds) return;

            // Only the nearest few walk over; the rest turn toward the noise and keep watch
            float myDist = (transform.position - soundOrigin).sqrMagnitude;
            int closer = 0;
            foreach (var other in Active)
            {
                if (other == null || other == this || other.IsDead || other.currentState == EnemyState.Combat) continue;
                float d = (other.transform.position - soundOrigin).sqrMagnitude;
                if (d > (radius + 2f) * (radius + 2f)) continue; // did not hear it
                if (d < myDist || (Mathf.Approximately(d, myDist) && Active.IndexOf(other) < Active.IndexOf(this))) closer++;
            }

            _investigateLookOnly = closer >= Mathf.Max(1, maxNoiseInvestigators);
            Vector3 point = soundOrigin;
            if (!_investigateLookOnly && closer > 0)
            {
                // Not the same square metre for everyone: the next one stops a few steps to the side
                Vector3 away = transform.position - soundOrigin;
                away.y = 0f;
                if (away.sqrMagnitude < 0.01f) away = transform.forward;
                point = soundOrigin + Quaternion.AngleAxis(70f * closer, Vector3.up) * away.normalized * 2.5f;
            }
            _investigatePoint = point;
            _investigateTimer = investigateDuration;
            suspicionMeter = Mathf.Max(suspicionMeter, 50f);
            SetState(EnemyState.Suspicious);
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
                // Reloading: a short opening for the player
                bool reloading = weapon != null && weapon.IsFirearm && weapon.IsReloading;
                _stealthIndicator.color = reloading ? new Color(1f, 0.6f, 0.2f) : Color.red;
                _stealthIndicator.text = reloading ? "..." : "!";
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
