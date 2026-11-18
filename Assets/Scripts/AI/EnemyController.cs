using UnityEngine;
using UnityEngine.AI;
using EndlessCombat.Combat;
using System.Collections.Generic;
using EndlessSurvival.Inventory;

namespace EndlessCombat.AI
{
    /// <summary>
    /// An enemy soldier (Enemy_AI_Deep_Plan.md 3.3). Health, damage, death, ragdoll, loot, weapon handling, and the
    /// frame order: senses (EnemySenses) -> belief (EnemyBelief) -> nerve (EnemyNerve) -> squad (EnemySquad: phase,
    /// power, tactic, roles, tokens) -> brain (EnemyBrain: scored options) -> actor (EnemyActor: one timed action)
    /// -> movement (EnemyMover), CombatBody (strikes, shield) and WeaponController (shots). Information between
    /// enemies travels only by shouts (EnemyBarks). The brain never reads the player's real position.
    /// </summary>
    [RequireComponent(typeof(NavMeshAgent))]
    public partial class EnemyController : MonoBehaviour, IDamageable
    {
        public enum EnemyState { Idle, Sitting, Patrolling, Suspicious, Searching, Combat }

        [Header("Behavior")]
        public EnemyState initialState = EnemyState.Idle;
        private EnemyState currentState;
        public bool IsAlerted => currentState == EnemyState.Combat;
        public EnemyState State => currentState;
        [Tooltip("Name used in shouts ({ad}); empty = picked from a fixed list")]
        public string displayName = "";

        /// <summary>Enemies currently enabled in the scene (used by survival systems: toilet safety, stress).</summary>
        public static readonly List<EnemyController> Active = new List<EnemyController>();

        private bool _started;

        private void OnEnable()
        {
            if (!Active.Contains(this)) Active.Add(this);
            if (_started && !IsDead && _squad == null) EnemySquad.Join(this);
        }

        private void OnDisable()
        {
            Active.Remove(this);
            // An abstract squad switches its members off far from the player (19.1): they stay members
            if (_squad != null && _squad.IsAbstract && !IsDead) return;
            LeaveSquad();
        }

        private void LeaveSquad()
        {
            _brain?.Reset();
            if (_squad != null) _squad.Leave(this);
            else EnemySquad.ReleaseToken(this);
            _squad = null;
        }

        [Header("Stealth & Perception")]
        public float fieldOfViewAngle = 110f;
        public float eyeHeight = 1.6f;
        [Tooltip("Suspicion (0-100)")]
        public float suspicionMeter = 0f;
        public float suspicionBuildRate = 50f;
        public float suspicionDecayRate = 20f;
        public float investigateDuration = 5f;
        private Vector3 _investigatePoint;
        private float _investigateTimer = 0f;
        public int maxNoiseInvestigators = 2;
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
        [Tooltip("Set from the weapon at spawn: pistol 20 m, rifle 45 m (13.4)")]
        public float attackRange = 15f;
        public float attackCooldown = 1.5f;
        public float damage = 10f;
        private float lastAttackTime;
        public float aimSpreadClose = 0.5f;
        public float aimSpreadFar = 2.5f;
        public float lostSightGrace = 1f;

        [Header("Pathfinding")]
        public float repathInterval = 0.3f;
        public float repathDestinationDelta = 1.5f;
        public float navMeshSampleDistance = 4f;

        [Header("Loot")]
        public LootTable dropTable;
        public ItemDefinition heldWeaponItem;
        public bool equipHeldWeaponOnSpawn = false;

        [Header("Ammo (firearms)")]
        public Vector2Int rifleRounds = new Vector2Int(14, 24);
        public Vector2Int pistolRounds = new Vector2Int(7, 13);
        public Vector2Int magazineRounds = new Vector2Int(8, 6);
        [Range(0f, 1f)] public float backupKnifeChance = 0.5f;

        [Header("Damage dealt (the same weapons are much deadlier in the player's hands)")]
        public float firearmDamageMultiplier = 0.55f;
        public float meleeDamageMultiplier = 0.6f;
        public float meleeMinDistance = 1.05f;

        [Header("Carried (set by the squad at spawn)")]
        public int grenades;
        public int rocks;
        public int bandages;

        private bool _outOfAmmo;
        private WeaponController _stowedGun;
        public bool OutOfAmmo => _outOfAmmo;

        [Header("Melee (melee weapon or no weapon)")]
        public float meleeRange = 1.5f;
        public float meleeChargeSpeedMultiplier = 1.6f;
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

        // ---- deep AI parts (Enemy_AI_Deep_Plan.md) ----
        public EnemySenses Senses { get; private set; }
        public EnemyBelief Belief { get; private set; }
        public EnemyNerve Nerve { get; private set; }
        public EnemyActor Actor { get; private set; }
        public EnemyBarks Barks { get; private set; }
        public AITarget SelfTarget { get; private set; }

        public WeaponType WeaponKind => _weaponType;
        private bool IsMeleeFighter => _weaponType == WeaponType.Melee || _weaponType == WeaponType.Unarmed;
        public bool IsMeleeRole => IsMeleeFighter;
        public bool IsGunner => !IsMeleeFighter && weapon != null && weapon.IsFirearm && !_outOfAmmo && !HasUsableShield;
        public bool HasUsableShield => _body != null && _body.UsingShield;
        public bool HasRaisedShield => !IsDead && _body != null && _body.IsGuarding;
        public float Health01 => maxHealth > 0f ? Mathf.Clamp01(currentHealth / maxHealth) : 1f;
        public EnemySquad Squad => _squad;
        public bool IsReloading => weapon != null && weapon.IsFirearm && weapon.IsReloading;
        public EnemyMover Mover => _mover;
        public EnemyBrain Brain => _brain;
        public bool IsLeader => _squad != null && _squad.leader == this;
        public string DisplayName => string.IsNullOrEmpty(displayName) ? "Biri" : displayName;
        public string LastLostMateName { get; set; }
        public bool Surrendered { get; private set; }
        public bool Fleeing { get; private set; }
        public bool KilledSilently { get; private set; }

        /// <summary>Firearm or a firearm on the way (before Start: the prefab's weapon or heldWeaponItem).</summary>
        public bool IsGunnerKind
        {
            get
            {
                if (weapon != null) return weapon.IsFirearm;
                if (equipHeldWeaponOnSpawn && heldWeaponItem != null && heldWeaponItem.weaponPrefab != null) return heldWeaponItem.weaponPrefab.IsFirearm;
                var w = GetComponentInChildren<WeaponController>(true);
                return w != null && w.IsFirearm;
            }
        }

        // ammo helpers for the brain
        public int MagazineRounds => weapon != null && weapon.IsFirearm ? weapon.CurrentAmmo : 0;
        public float MagazineFraction => weapon != null && weapon.IsFirearm ? weapon.CurrentAmmo / (float)Mathf.Max(1, weapon.MagazineCapacity) : 1f;
        public bool MagazineEmpty => weapon != null && weapon.IsFirearm && weapon.CurrentAmmo <= 0;
        public bool CanReload => weapon != null && weapon.IsFirearm && !weapon.IsReloading && weapon.ReserveAmmo > 0 && weapon.CurrentAmmo < weapon.MagazineCapacity;
        public int TotalAmmo => weapon != null && weapon.IsFirearm ? weapon.CurrentAmmo + weapon.ReserveAmmo : 0;
        public bool HasAnyAmmo => TotalAmmo > 0;
        public int MagazineGeneration { get; private set; }
        public bool ShieldWorn
        {
            get
            {
                var h = GetComponent<ShieldHolder>();
                return h != null && h.Usable && h.Durability01 < 0.25f;
            }
        }
        public float CurrentWeaponValue
        {
            get
            {
                if (weapon == null || _outOfAmmo) return weapon != null && weapon.Type == WeaponType.Melee ? 1f : 0f;
                return AIInterest.WeaponValue(weapon.Type, _meleeCategory == MeleeCategory.Short);
            }
        }

        // sleeping at night in a calm camp (16.2)
        public bool Sleeping { get; set; }
        public float SightFactor => Sleeping ? 0.3f : 1f;
        public float HearingFactor => Sleeping ? 0.5f : 1f;

        [Header("References")]
        public Transform target;
        private NavMeshAgent agent;
        private Animator animator;
        private WeaponController weapon;
        private CharacterController controller;

        private List<Rigidbody> ragdollRigidbodies = new List<Rigidbody>();
        private List<Collider> ragdollColliders = new List<Collider>();

        public bool IsDead { get; private set; }
        private static bool s_missingPlayerLogged;
        private static int s_nameCounter;
        private float verticalVelocity;
        private float gravity = -15f;

        private void Awake()
        {
            Senses = new EnemySenses(this);
            Belief = new EnemyBelief(this);
            Nerve = new EnemyNerve(this);
            Actor = new EnemyActor();
            Barks = new EnemyBarks(this);
        }

        private void Start()
        {
            currentState = initialState;
            currentHealth = maxHealth;
            _homePos = transform.position;
            _homeYaw = transform.eulerAngles.y;
            _idleNextAt = Time.time + Random.Range(5f, 15f);
            agent = GetComponent<NavMeshAgent>();
            animator = GetComponentInChildren<Animator>();
            if (equipHeldWeaponOnSpawn) EquipHeldWeaponItem();
            weapon = GetComponentInChildren<WeaponController>();
            SetupWeaponType();
            SetupAmmo();
            if (animator != null && animator.GetComponent<FootstepEmitter>() == null)
                animator.gameObject.AddComponent<FootstepEmitter>();

            controller = GetComponent<CharacterController>();
            if (controller == null)
            {
                controller = gameObject.AddComponent<CharacterController>();
                controller.height = 1.8f;
                controller.radius = 0.28f;
                controller.center = new Vector3(0, 0.9f, 0);
                controller.skinWidth = 0.01f;
            }
            if (agent != null) agent.enabled = false;

            if (target == null)
            {
                // The director's reference first: in the car the player object is inactive and the tag search
                // finds nothing (every enemy spawned while driving logged an error, 17 Nov AI playtest)
                var player = EndlessSurvival.Survival.SurvivalDirector.Player;
                if (player == null) player = GameObject.FindGameObjectWithTag("Player");
                if (player != null) target = player.transform;
                else if (!s_missingPlayerLogged)
                {
                    s_missingPlayerLogged = true;
                    Debug.LogError("Düşman oyuncuyu bulamadı! Oyuncunun tag'i 'Player' değil veya Target atanmamış.");
                }
            }

            SelfTarget = AITarget.GetOrAdd(gameObject, AITargetKind.Human, CombatTeam.Enemy);
            if (string.IsNullOrEmpty(displayName)) displayName = EnemyBarks.Names[(s_nameCounter++) % EnemyBarks.Names.Length];
            _mover = new EnemyMover(this);
            _brain = new EnemyBrain(this);
            SetupRagdoll();
            DisableRagdoll();
            SetupStealthIndicator();
            SetupStatusLabel();
            SetupCombatBody();
            CharacterBodyColliders.GetOrAdd(gameObject).CapsulePassesVehicles = true; // KeepOutOfVehicles instead
            if (_squad == null) EnemySquad.Join(this);
            _squad?.EnsureLeader();
            if (bandages <= 0 && Random.value < (AIDifficulty.Current == AIDifficulty.Tier.Easy ? 0.2f : AIDifficulty.Current >= AIDifficulty.Tier.Hard ? 0.7f : 0.45f)) bandages = 1;
            if (IsMeleeFighter && rocks <= 0) rocks = Random.Range(0, 3);
            _started = true;
            if (_squad != null) OnSquadPhase(_squad.Phase);
            RegisterTuning();
        }

        private void OnDestroy()
        {
            // Alive and leaving with the chunk after seeing the player in a fight: a witness (19.3)
            if (!IsDead && AISystem.Running && _squad != null && _squad.everFought && Belief != null)
            {
                var b = Belief.Of(AITarget.Player);
                if (b != null && b.everSeen) AbstractGroups.Witness(this);
            }
            if (_bodyStimulus > 0) AIStimuli.RemovePersistent(_bodyStimulus);
            CoverMap.Release(this);
        }

        /// <summary>Called by EnemySquad.Add.</summary>
        public void SetSquad(EnemySquad s) => _squad = s;

        public void MakeLeader()
        {
            maxHealth *= 1.3f;
            if (_started) currentHealth = Mathf.Min(maxHealth, currentHealth * 1.3f);
        }

        // ------------------------------------------------------------------ setup

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
                var b = Belief?.Primary;
                Vector3 to = b != null ? b.position : (target != null ? target.position : transform.position + transform.forward);
                Vector3 d = to - transform.position;
                d.y = 0f;
                return d;
            };
            // Stealth kill works on an enemy that is not fighting, on one that gave up, and (2026-11-17) on a fighting
            // one that has lost sight of the player for a while (it does not know you are behind it)
            _body.Unaware = () =>
            {
                if (currentState != EnemyState.Combat || Surrendered) return true;
                var pb = Belief != null && AITarget.Player != null ? Belief.Of(AITarget.Player) : null;
                return pb == null || (!pb.SeenNow && pb.SinceSeen > CombatBody.StealthKillLostSight);
            };
            _body.HitReceived -= OnHitReceived;
            _body.HitReceived += OnHitReceived;
            _body.SwingFinished -= OnSwingFinished;
            _body.SwingFinished += OnSwingFinished;
        }

        private void OnHitReceived(HitResult result, CombatBody attacker)
        {
            if (IsDead) return;
            if (attacker != null && attacker.team == CombatTeam.Player && AITarget.Player != null)
                Belief.Note(AITarget.Player, attacker.transform.position, Vector3.zero, 1f, BeliefSource.Seen);
            EnterCombat();
        }

        private void OnSwingFinished(int hits)
        {
            _brain?.StrikeDone();
            _nextMeleeTime = Time.time + attackCooldown * Random.Range(0.7f, 1.3f);
            if (hits > 0) Nerve.Add(-EnemyNerve.HitPlayerRelief);
        }

        private void OnShieldBroken(ItemDefinition item)
        {
            heldShieldItem = null;
            if (_body != null)
            {
                _body.SetGuard(false);
                _body.RefreshShield();
            }
            // 13.6: a timid one backs off when the shield breaks
            if (Nerve.courage < 0.5f) Nerve.Add(25f);
        }

        private void EquipHeldWeaponItem()
        {
            if (heldWeaponItem == null) return;
            WeaponController old = GetComponentInChildren<WeaponController>(true);
            Transform socket = old != null ? old.transform.parent : null;
            if (socket == null)
            {
                foreach (Transform t in GetComponentsInChildren<Transform>(true))
                    if (t.name.Trim().Equals("WeaponSocket", System.StringComparison.OrdinalIgnoreCase)) { socket = t; break; }
            }
            if (socket == null && animator != null) socket = animator.GetBoneTransform(HumanBodyBones.RightHand);
            if (old != null)
            {
                old.gameObject.SetActive(false);
                Destroy(old.gameObject);
            }
            if (heldWeaponItem.weaponPrefab != null && socket != null) Instantiate(heldWeaponItem.weaponPrefab, socket);
        }

        private void SetupWeaponType()
        {
            _weaponType = weapon != null ? weapon.Type : WeaponType.Unarmed;
            var melee = weapon != null ? weapon.GetComponent<MeleeWeapon>() : null;
            _meleeStats = melee != null ? melee.stats : CombatContent.Instance.fists;
            _meleeStats.damage *= Mathf.Max(0f, meleeDamageMultiplier);
            _meleeCategory = melee != null ? melee.stats.ResolveCategory(false) : MeleeCategory.Fists;
            if (_weaponType == WeaponType.Pistol) aimRotationOffset = -weapon.AimYawOffset;
            else if (_weaponType == WeaponType.Rifle) aimRotationOffset = 45f;
            // Range from the weapon (13.4)
            attackRange = _weaponType == WeaponType.Pistol ? PistolRange : _weaponType == WeaponType.Rifle ? RifleRange : meleeRange;

            _combatAnim = CombatAnimator.GetOrAdd(animator);
            if (_combatAnim != null)
            {
                _combatAnim.SetWeaponType(_weaponType);
                _combatAnim.SnapWeights();
            }
        }

        public static float PistolRange = 20f, RifleRange = 45f;

        private void SetupAmmo()
        {
            if (weapon == null || !weapon.IsFirearm) return;
            bool pistol = weapon.Type == WeaponType.Pistol;
            Vector2Int range = pistol ? pistolRounds : rifleRounds;
            int total = Random.Range(Mathf.Min(range.x, range.y), Mathf.Max(range.x, range.y) + 1);
            int mag = Mathf.Clamp(pistol ? magazineRounds.y : magazineRounds.x, 1, Mathf.Max(1, total));
            weapon.MakeLimitedAmmo(mag, Mathf.Max(0, total - mag));
            weapon.DamageMultiplier = firearmDamageMultiplier;
            HookWeapon();
        }

        private void HookWeapon()
        {
            if (weapon == null) return;
            weapon.ReloadStarted -= OnWeaponReloadStarted;
            weapon.ReloadStarted += OnWeaponReloadStarted;
        }

        private void OnWeaponReloadStarted()
        {
            MagazineGeneration++;
            Barks.Say(BarkKind.Reloading);
        }

        private void SwitchToMelee()
        {
            if (_outOfAmmo) return;
            _outOfAmmo = true;
            animator.SetBoolSafe("IsAiming", false);
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
            if (_squad != null) SquadTactics.Assign(_squad);
        }

        /// <summary>Takes a weapon from the ground (13.10): the one in the hand drops, the new one comes with its magazine.</summary>
        public void EquipFromPickup(WorldPickup p)
        {
            if (p == null || p.item == null || p.item.weaponPrefab == null || IsDead) return;
            DropCurrentWeapon();
            heldWeaponItem = p.item;
            EquipHeldWeaponItem();
            weapon = GetComponentInChildren<WeaponController>();
            _outOfAmmo = false;
            _stowedGun = null;
            SetupWeaponType();
            if (weapon != null && weapon.IsFirearm)
            {
                int rounds = p.magazineRounds >= 0 ? p.magazineRounds : weapon.MagazineCapacity;
                weapon.MakeLimitedAmmo(Mathf.Max(1, rounds), 0);
                if (rounds <= 0) weapon.TakeAllAmmo();
                weapon.DamageMultiplier = firearmDamageMultiplier;
                HookWeapon();
            }
            var holder = GetComponent<ShieldHolder>();
            if (holder != null && holder.HasShield) holder.SetOnBack(_weaponType == WeaponType.Rifle || _meleeCategory == MeleeCategory.TwoHand);
            _body?.RefreshShield();
            AIDebug.Log(_squad, $"{DisplayName} yerden {p.item.DisplayName} aldı");
            Destroy(p.gameObject);
            if (_squad != null) SquadTactics.Assign(_squad);
        }

        private void DropCurrentWeapon()
        {
            WeaponController held = _stowedGun != null ? _stowedGun : weapon;
            if (held != null && heldWeaponItem != null && dropTable != null && dropTable.pickupPrefab != null)
            {
                var p = WorldPickup.Spawn(dropTable.pickupPrefab, heldWeaponItem, 1, held.transform.position, null);
                if (p != null && held.IsFirearm) p.magazineRounds = held.CurrentAmmo;
            }
            if (held != null) Destroy(held.gameObject);
            if (weapon != null && weapon != held) Destroy(weapon.gameObject);
            weapon = null;
            _stowedGun = null;
        }

        public void Stagger(Vector3 push, float duration)
        {
            if (IsDead || duration <= 0f) return;
            _body?.Stagger(duration, push);
        }

        // ------------------------------------------------------------------ frame (3.3)

        private Vector3? _lookAt;
        private float _lookAtUntil;
        private float _lastContactBark = -999f;
        private float _sightStart = -999f, _lastSightEnd = -999f;
        private bool _wasSeeing;
        private int _burstLeft;
        private float _nextShotAt;
        private float _lastUnderFire = -999f;
        private Vector3 _lastHitDir;
        private float _lastHitTime = -999f;
        public bool HitFromTwoSides { get; private set; }
        public Vector3? GrenadeThreat { get; private set; }
        public ThrownProjectile GrenadeObject { get; private set; }
        private float _grenadeThreatUntil;

        public bool UnderFire(float seconds) => Time.time - _lastUnderFire < seconds;

        private void Update()
        {
            if (IsDead) return;
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            UpdateDebug();

            if (_body != null && _body.IsLocked && controller != null)
            {
                if (_body.IsStaggered)
                {
                    InterruptAction();
                    Barks.Cancel();
                }
                verticalVelocity = controller.isGrounded ? -2f : verticalVelocity + gravity * dt;
                controller.Move(new Vector3(0f, verticalVelocity, 0f) * dt);
                animator.SetBoolSafe("IsAiming", false);
                animator.SetFloatSafe("Speed", 0f);
                _mover.TrackProgress(false);
                UpdateStealthIndicator();
                return;
            }

            if (target == null)
            {
                // The director's reference first: in the car the player object is inactive and the tag search
                // finds nothing (every enemy spawned while driving logged an error, 17 Nov AI playtest)
                var player = EndlessSurvival.Survival.SurvivalDirector.Player;
                if (player == null) player = GameObject.FindGameObjectWithTag("Player");
                if (player != null) target = player.transform;
            }

            if (Time.time > _grenadeThreatUntil || (GrenadeObject == null && GrenadeThreat.HasValue && Time.time > _grenadeThreatUntil - 2f)) { GrenadeThreat = null; GrenadeObject = null; }

            Barks.Tick();
            Actor.Tick(dt);
            if (Actor.Current == EnemyAction.Reload && !IsReloading) Actor.Cancel();
            Senses.Tick();
            var p = AIDifficulty.For(_squad);
            Belief.Tick(dt, p);
            UpdateSeeingEdges();
            _squad?.Tick();
            TickNerve(dt);
            if (Belief.Primary != null && Belief.Primary.SeenNow && Belief.Primary.target != null) target = Belief.Primary.target.SightTransform;

            animator.SetFloatSafe("MotionSpeed", 1f);
            bool isMoving = false;
            float animSpeed = moveSpeed;
            verticalVelocity = controller.isGrounded ? -2f : verticalVelocity + gravity * dt;
            Vector3 moveVelocity = Vector3.zero;

            if (Surrendered) UpdateSurrendered();
            else if (Fleeing && currentState != EnemyState.Combat) UpdateFleeing(ref moveVelocity, ref isMoving, ref animSpeed);
            else if (currentState == EnemyState.Combat || currentState == EnemyState.Searching)
            {
                if (currentState == EnemyState.Combat) suspicionMeter = 100f;
                UpdateCombat(ref moveVelocity, ref isMoving, ref animSpeed);
            }
            else if (currentState == EnemyState.Suspicious) UpdateSuspicious(ref moveVelocity, ref isMoving, ref animSpeed);
            else
            {
                animator.SetBoolSafe("IsAiming", false);
                if (!Senses.SeesAny && Time.time >= _glintHoldUntil && Time.time - _lastSawSomething > SuspicionHold) suspicionMeter = Mathf.MoveTowards(suspicionMeter, 0f, suspicionDecayRate * dt);
                if (_routineGoal.HasValue) UpdateRoutineWalk(ref moveVelocity, ref isMoving, ref animSpeed);
                else if (currentState == EnemyState.Patrolling) UpdatePatrol(ref moveVelocity, ref isMoving, ref animSpeed);
                else if (currentState == EnemyState.Idle) UpdateIdleLife();
                if (_lookAt.HasValue && Time.time < _lookAtUntil && !isMoving) Turn(_lookAt.Value - transform.position, 240f);
            }

            bool sitting = currentState == EnemyState.Sitting;
            if (sitting)
            {
                isMoving = false;
                moveVelocity = Vector3.zero;
            }
            transform.localScale = Vector3.one;
            animator.SetBoolSafe("IsSitting", sitting);

            if (_body != null)
            {
                if (currentState != EnemyState.Combat && currentState != EnemyState.Searching) _body.SetGuard(false);
                moveVelocity.x *= _body.MoveSpeedMultiplier;
                moveVelocity.z *= _body.MoveSpeedMultiplier;
                if (_body.MoveSpeedMultiplier < 0.99f) animSpeed *= _body.MoveSpeedMultiplier;
            }
            if (Actor.BlocksMove)
            {
                moveVelocity.x = 0f;
                moveVelocity.z = 0f;
                isMoving = false;
            }
            else if (Actor.Current == EnemyAction.Reload)
            {
                moveVelocity.x *= 0.8f;
                moveVelocity.z *= 0.8f;
            }

            if (!Actor.BlocksMove && currentState != EnemyState.Sitting) moveVelocity += Separation();

            _mover.TrackProgress(isMoving);
            moveVelocity.y = verticalVelocity;
            controller.Move(moveVelocity * dt);
            KeepOutOfVehicles();
            animator.SetFloatSafe("Speed", isMoving ? animSpeed : 0f);
            UpdateStealthIndicator();
        }

        /// <summary>
        /// The capsule does not collide with vehicles (CharacterBodyColliders.CapsulePassesVehicles: an immovable
        /// capsule threw the truck around, 17 Nov). Instead he is moved out of the body box here, on the side he is
        /// least deep in. A truck rolling slowly into him shoves him aside; nothing pushes the truck.
        /// </summary>
        private void KeepOutOfVehicles()
        {
            var all = EndlessSurvival.Vehicle.VehicleController.All;
            if (all.Count == 0 || controller == null || !controller.enabled) return;
            Vector3 me = transform.position;
            for (int i = 0; i < all.Count; i++)
            {
                var v = all[i];
                if (v == null) continue;
                Vector3 vp = v.transform.position;
                if ((vp - me).sqrMagnitude > 64f) continue;
                var body = v.GetComponent<BoxCollider>();
                if (body == null) continue;
                Vector3 s = v.transform.lossyScale;
                Vector3 c = Vector3.Scale(body.center, s);
                float hx = body.size.x * 0.5f * Mathf.Abs(s.x) + controller.radius + 0.05f;
                float hz = body.size.z * 0.5f * Mathf.Abs(s.z) + controller.radius + 0.05f;
                float top = c.y + body.size.y * 0.5f * Mathf.Abs(s.y);
                Vector3 local = Quaternion.Inverse(v.transform.rotation) * (me - vp) - new Vector3(c.x, 0f, c.z);
                if (local.y > top || local.y + controller.height < -0.5f) continue; // above the roof / far below
                float px = hx - Mathf.Abs(local.x), pz = hz - Mathf.Abs(local.z);
                if (px <= 0f || pz <= 0f) continue;
                Vector3 push = px < pz ? new Vector3(Mathf.Sign(local.x == 0f ? 1f : local.x) * px, 0f, 0f)
                                       : new Vector3(0f, 0f, Mathf.Sign(local.z == 0f ? 1f : local.z) * pz);
                controller.Move(v.transform.rotation * push);
                me = transform.position;
            }
        }

        /// <summary>Personal space: a soft push away from other living enemies closer than this (no more men inside each other).</summary>
        public static float PersonalSpace = 1.1f;

        private Vector3 Separation()
        {
            Vector3 me = transform.position;
            Vector3 push = Vector3.zero;
            float r2 = PersonalSpace * PersonalSpace;
            for (int i = 0; i < Active.Count; i++)
            {
                var o = Active[i];
                if (o == null || o == this || o.IsDead) continue;
                Vector3 d = me - o.transform.position;
                d.y = 0f;
                float sq = d.sqrMagnitude;
                if (sq >= r2) continue;
                if (sq < 0.0001f) d = new Vector3((GetHashCode() & 1) == 0 ? 1f : -1f, 0f, 0.3f); // exactly on top: pick a side
                float k = 1f - Mathf.Sqrt(sq) / PersonalSpace;
                push += d.normalized * k * 2.5f;
            }
            return push;
        }

        private void UpdateSeeingEdges()
        {
            var b = Belief.Primary;
            bool seeing = b != null && b.SeenNow;
            if (seeing && !_wasSeeing)
            {
                _sightStart = Time.time;
                // A new sight after 3 s: the reaction delay and the deliberate first miss (13.4)
                if (Time.time - _lastSightEnd > 3f)
                {
                    Actor.React(AIDifficulty.Reaction(AIDifficulty.For(_squad)) * Nerve.ReactionMul);
                    _firstShotPending = true;
                }
            }
            if (!seeing && _wasSeeing) _lastSightEnd = Time.time;
            _wasSeeing = seeing;
        }

        private bool _firstShotPending;
        private float _nerveFriendCheck;
        private bool _friendNear = true, _leaderNear, _darkAlone;
        private int _friendsFighting;

        private void TickNerve(float dt)
        {
            if (Time.time >= _nerveFriendCheck)
            {
                _nerveFriendCheck = Time.time + 0.5f;
                _friendNear = _squad != null && _squad.FriendWithin(this, 15f);
                _leaderNear = _squad != null && _squad.LeaderAlive && _squad.leader != this
                              && (_squad.leader.transform.position - transform.position).sqrMagnitude < 400f;
                _friendsFighting = _squad != null ? _squad.AlliesFighting(this, 30f) : 0;
                var cycle = EndlessSurvival.World.DayNightCycle.Instance;
                _darkAlone = cycle != null && cycle.IsNight && _squad != null && _squad.Phase != AlertPhase.Calm
                             && EndlessSurvival.Survival.CraftingSystem.NearestBurningFire(transform.position, 8f) == null;
            }
            bool fighting = currentState == EnemyState.Combat || currentState == EnemyState.Searching;
            Nerve.Tick(dt, _brain != null && _brain.InCover, _leaderNear, _friendsFighting, _friendNear || !fighting, _darkAlone && fighting);
            if (Nerve.Level >= NerveLevel.Pinned && _brain != null && _brain.HasToken) EnemySquad.ReleaseToken(this);
        }

        // ------------------------------------------------------------------ perception results

        /// <summary>EnemySenses saw a hostile target this tick.</summary>
        // ---- signature moves (AISignatures) ----
        /// <summary>A signature move drives this enemy (hider, runner): squad phases and sights do not take over.</summary>
        public bool SignatureLocked { get; set; }
        /// <summary>After a robbery / bluff the squad walks away and ignores the player for a while.</summary>
        public float IgnorePlayerUntil { get; set; }
        public Transform Carried { get; set; }

        /// <summary>21.3: the chest is tied to the back; the runner then runs from the player.</summary>
        public void StartCarry(Transform chest)
        {
            if (chest == null || IsDead) return;
            Carried = chest;
            foreach (var c in chest.GetComponentsInChildren<Collider>()) c.enabled = false;
            Transform back = animator != null ? animator.GetBoneTransform(HumanBodyBones.Chest) : null;
            chest.SetParent(back != null ? back : transform, true);
            chest.localPosition = new Vector3(0f, 0.1f, -0.35f);
            moveSpeed *= 0.8f;
            BeginFlee();
        }

        public void DropCarried()
        {
            if (Carried == null) return;
            Transform c = Carried;
            Carried = null;
            c.SetParent(null, true);
            Vector3 p = transform.position + transform.forward * 0.6f;
            if (Physics.Raycast(p + Vector3.up * 2f, Vector3.down, out RaycastHit hit, 6f, ~0, QueryTriggerInteraction.Ignore)) p = hit.point;
            c.position = p;
            c.rotation = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);
            foreach (var col in c.GetComponentsInChildren<Collider>()) col.enabled = true;
        }

        /// <summary>The meter does not drop for this long after the last sight (a glance away is not forgetting).</summary>
        public static float SuspicionHold = 3f;
        private float _lastSawSomething = -999f;

        public void OnSeen(AITarget t, float clarity, float dt)
        {
            if (Surrendered) return;
            if (SignatureLocked && t.kind != AITargetKind.Animal) return;
            if (Time.time < IgnorePlayerUntil && t.kind != AITargetKind.Animal) return;
            if (t.kind == AITargetKind.Player || (t.kind == AITargetKind.Vehicle && t.InVehicle)) _playerEvidenceTime = Time.time;
            if (t.kind == AITargetKind.Animal)
            {
                if (currentState != EnemyState.Combat) Nerve.Add(EnemyNerve.PredatorGain);
                EnterCombat(t);
                return;
            }
            if (t.InVehicle)
            {
                // The driven car seen = he is in it (user decision 16 Nov: seeing the car is enough, no meter, no
                // sound). The squad's shared picture moves to the car so nobody keeps searching where he got in.
                _squad?.SeeVehicle(t.Position);
                if (currentState != EnemyState.Combat && (_squad == null || !_squad.HoldFire))
                {
                    if (Time.time - _lastVehicleBark > 15f)
                    {
                        _lastVehicleBark = Time.time;
                        Barks.Say(BarkKind.Custom, Belief.Of(t), "Arabada!");
                    }
                    EnterCombat(t);
                    return;
                }
            }
            if (currentState == EnemyState.Combat)
            {
                if (Time.time - _lastContactBark > 6f && Belief.Primary != null && Belief.Primary.target == t
                    && (_squad == null || Time.time - _squad.Shared.time > 4f))
                {
                    _lastContactBark = Time.time;
                    Barks.Say(BarkKind.Contact, Belief.Of(t));
                }
                // Opportunities seen (14.2)
                var read = PlayerRead.Current;
                var p = AIDifficulty.For(_squad);
                if (p.exploitReload && read.action == CombatAction.Reload) { Barks.Say(BarkKind.PlayerReloading, Belief.Of(t)); Nerve.Add(-EnemyNerve.PlayerReloadRelief); }
                if (PlayerRead.Intent == PlayerIntent.ToVehicle) Barks.Say(BarkKind.ToVehicle, Belief.Of(t));
                return;
            }
            if (_squad != null && _squad.HoldFire) return; // robbery / ambush: watching
            if (currentState == EnemyState.Searching)
            {
                // A searcher who finds the target: Contact after the reaction time
                suspicionMeter = 100f;
                EnterCombat(t);
                return;
            }
            float guarded = _squad != null && _squad.Phase == AlertPhase.Guarded ? 1.5f : 1f;
            // Close and in plain view fills the meter fast (user bug report 16 Nov: standing in front of a man, his
            // meter never filled). Crouching and darkness shorten the "close" distance.
            float d = Flat(t.Position - transform.position).magnitude;
            var cycle = EndlessSurvival.World.DayNightCycle.Instance;
            float near = 8f * (PlayerRead.Current.crouching ? 0.55f : 1f) * (cycle != null && cycle.IsNight ? 0.7f : 1f) * SightFactor;
            float closeMul = d < near ? Mathf.Lerp(6f, 1.5f, d / near) : 1f;
            if (currentState == EnemyState.Suspicious) closeMul *= 1.5f; // already looking for something
            _lastSawSomething = Time.time;
            suspicionMeter += suspicionBuildRate * Mathf.Max(clarity, 0.15f) * closeMul * CombatHooks.EnemySuspicionMultiplier() * AIDifficulty.For(_squad).noticeMul * guarded * dt;
            if (suspicionMeter >= 100f)
            {
                if (AISignatures.TryRobbery(this)) return;
                EnterCombat(t);
                return;
            }
            if (suspicionMeter > 25f && currentState != EnemyState.Suspicious)
            {
                if (AISignatures.TryFakeUnaware(this, t)) return;
                _investigatePoint = Belief.Of(t) != null ? Belief.Of(t).position : t.Position;
                _investigateLookOnly = false;
                _investigateTimer = investigateDuration;
                SetState(EnemyState.Suspicious);
                _squad?.Raise(AlertPhase.Suspicious, _investigatePoint);
            }
        }

        /// <summary>Own senses say fight (seen with full suspicion, hit, predator close).</summary>
        public void EnterCombat(AITarget about = null)
        {
            if (IsDead || Surrendered || Carried != null) return;
            SignatureLocked = false;
            IgnorePlayerUntil = 0f;
            bool wasCombat = currentState == EnemyState.Combat;
            suspicionMeter = 100f;
            Actor.SetReadiness(Readiness.Ready, AIDifficulty.Reaction(AIDifficulty.For(_squad)) * Nerve.ReactionMul,
                weapon != null ? WeaponDefinition.DrawTimeOf(weapon) : 0.3f, currentState == EnemyState.Sitting);
            SetState(EnemyState.Combat);
            if (_squad != null) _squad.HoldFire = false;
            _squad?.Raise(AlertPhase.Combat);
            if (!wasCombat)
            {
                var b = about != null ? Belief.Of(about) : Belief.Primary;
                if (b != null && b.SeenNow) { _lastContactBark = Time.time; Barks.Say(BarkKind.Contact, b); }
            }
        }

        /// <summary>The squad's alert phase changed (12.2): member state and readiness.</summary>
        public void OnSquadPhase(AlertPhase phase)
        {
            if (IsDead || Surrendered || SignatureLocked) return;
            var p = AIDifficulty.For(_squad);
            float reaction = AIDifficulty.Reaction(p) * Nerve.ReactionMul;
            float draw = weapon != null ? WeaponDefinition.DrawTimeOf(weapon) : 0.3f;
            bool sitting = currentState == EnemyState.Sitting;
            switch (phase)
            {
                case AlertPhase.Calm:
                    Actor.SetReadiness(Readiness.Relaxed, 0f, 0f, false);
                    if (currentState != initialState && currentState != EnemyState.Patrolling) SetState(initialState);
                    break;
                case AlertPhase.Guarded:
                    Actor.SetReadiness(Readiness.Wary, reaction, draw, sitting);
                    Sleeping = false;
                    if (currentState == EnemyState.Combat || currentState == EnemyState.Searching || currentState == EnemyState.Suspicious || currentState == EnemyState.Sitting)
                        SetState(initialState == EnemyState.Sitting ? EnemyState.Idle : initialState);
                    if (_squad != null && (transform.position - _squad.home).sqrMagnitude > 400f && _squad.kind != SquadKind.Patrol) _routineGoal = _squad.home + Random.insideUnitSphere.Flat() * 5f;
                    break;
                case AlertPhase.Suspicious:
                    Actor.SetReadiness(Readiness.Wary, reaction, draw, sitting);
                    Sleeping = false;
                    if (currentState == EnemyState.Sitting) SetState(EnemyState.Idle);
                    if (currentState != EnemyState.Suspicious && currentState != EnemyState.Combat && _squad != null && _squad.Shared.Valid)
                        InvestigateAsSquad(_squad.Shared.position);
                    break;
                case AlertPhase.Searching:
                case AlertPhase.Lost:
                    Actor.SetReadiness(Readiness.Wary, reaction, draw, sitting);
                    Sleeping = false;
                    SetState(EnemyState.Searching);
                    break;
                case AlertPhase.Combat:
                    Actor.SetReadiness(Readiness.Ready, reaction, draw, sitting);
                    Sleeping = false;
                    SetState(EnemyState.Combat);
                    break;
            }
        }

        /// <summary>The nearest two walk to look, the rest turn and watch (12.2 Suspicious).</summary>
        private void InvestigateAsSquad(Vector3 point)
        {
            int closer = 0;
            float my = (transform.position - point).sqrMagnitude;
            if (_squad != null)
                foreach (var m in _squad.Members)
                {
                    if (m == null || m == this || m.IsDead) continue;
                    if ((m.transform.position - point).sqrMagnitude < my) closer++;
                }
            _investigateLookOnly = closer >= Mathf.Max(1, maxNoiseInvestigators);
            _investigatePoint = closer > 0 && !_investigateLookOnly ? point + Random.insideUnitSphere.Flat() * 2.5f : point;
            _investigateTimer = investigateDuration * 2f;
            suspicionMeter = Mathf.Max(suspicionMeter, 30f);
            SetState(EnemyState.Suspicious);
        }

        /// <summary>Walk to a place and look (missing mate, signature checks).</summary>
        public void GoLookAt(Vector3 at)
        {
            if (IsDead || currentState == EnemyState.Combat) return;
            _investigatePoint = at;
            _investigateLookOnly = false;
            _investigateTimer = investigateDuration * 3f;
            suspicionMeter = Mathf.Max(suspicionMeter, 30f);
            SetState(EnemyState.Suspicious);
        }

        // ------------------------------------------------------------------ stimuli (6, 7.2, 7.3)

        private readonly HashSet<int> _knownEvidence = new HashSet<int>();
        private float _lastVehicleBark = -999f;
        /// <summary>A driven car's engine closer than this makes a calm enemy suspicious (farther: he only turns).</summary>
        public static float VehicleNoticeRange = 30f;
        private int _rocksHeard;
        private float _lastRockTime = -999f;
        private static readonly RaycastHit[] StimHits = new RaycastHit[8];

        public void OnStimulus(Stimulus s)
        {
            if (IsDead || Surrendered) return;
            if (s.needsSight)
            {
                if (s.id > 0 && _knownEvidence.Contains(s.id)) return;
                // A light shows itself in the dark; everything else is seen as far as the night and weather allow
                float sightRange = s.kind == StimulusKind.Light ? s.radius : s.radius * Senses.EvidenceFactor(s.position);
                if (!SeesPoint(s.position, sightRange)) return;
                if (s.id > 0) _knownEvidence.Add(s.id);
                OnEvidence(s);
                return;
            }
            Vector3 me = transform.position;
            float d = Vector3.Distance(me, s.position);
            float r = Mathf.Max(0.1f, s.radius * AIDifficulty.For(_squad).hearMul * HearingFactor);
            var p = AIDifficulty.For(_squad);

            if (s.kind == StimulusKind.Explosion && d < 10f) Nerve.Add(EnemyNerve.ExplosionGain);

            bool hostile = s.source != null && SelfTarget != null && AITargetRegistry.IsHostile(SelfTarget, s.source);
            if (hostile && s.certainty > 0f)
            {
                // The occupied car's noise is news about the player himself (user bug report 16 Nov: they kept
                // searching the spot where he got in while he drove away)
                if (s.source.kind == AITargetKind.Vehicle && AITargetRegistry.VehicleOccupied && AITarget.Player != null) s.source = AITarget.Player;
                if (s.source.kind == AITargetKind.Player || s.source.InVehicle) _playerEvidenceTime = Time.time;
                if (s.kind == StimulusKind.Explosion && s.sourceHint.sqrMagnitude > 0.01f)
                {
                    // A thrown grenade: he is somewhere back along the throw, not at the blast
                    float back = Mathf.Clamp(s.sourceHint.magnitude * Random.Range(0.6f, 1.1f), 6f, 22f);
                    Vector3 guess = s.position + s.sourceHint.Flat().normalized * back + Random.insideUnitSphere.Flat() * 4f;
                    var ob = Belief.Of(s.source);
                    Belief.Note(s.source, guess, Vector3.zero, Mathf.Max(ob != null ? ob.confidence : 0f, 0.35f), BeliefSource.Inferred);
                }
                else if (s.kind == StimulusKind.BulletImpact)
                {
                    Vector3 guess = s.position + s.sourceHint.normalized * 20f;
                    var ob = Belief.Of(s.source);
                    Belief.Note(s.source, guess, Vector3.zero, Mathf.Max(ob != null ? ob.confidence : 0f, 0.35f), BeliefSource.Inferred);
                    _lastUnderFire = Time.time;
                }
                else
                {
                    Vector3 pos = s.position + Random.insideUnitSphere.Flat() * (r * 0.1f);
                    float conf = s.certainty * (1f - 0.5f * Mathf.Clamp01(d / r));
                    var ob = Belief.Of(s.source);
                    Belief.Note(s.source, pos, Vector3.zero, Mathf.Max(ob != null ? ob.confidence : 0f, conf), BeliefSource.Heard);
                }
                // The driven car heard while the squad is after him: the squad's picture follows the sound
                if (s.kind == StimulusKind.VehicleEngine && s.source.InVehicle && _squad != null)
                {
                    var hb = Belief.Of(s.source);
                    _squad.HearVehicle(s.position, hb != null ? hb.confidence : s.certainty);
                }
                if (currentState == EnemyState.Combat) return;
                if (_squad != null && _squad.HoldFire) return;
                if (s.kind == StimulusKind.Gunshot || s.kind == StimulusKind.BulletImpact || s.kind == StimulusKind.Explosion)
                {
                    // Shots: up to suspicion at least, combat when they land close
                    if (s.kind == StimulusKind.BulletImpact && d < 6f) { EnterCombat(s.source); return; }
                    Barks.Say(BarkKind.ShotsHeard, Belief.Of(s.source));
                    _squad?.Raise(AlertPhase.Suspicious, s.position);
                    if (currentState != EnemyState.Searching) InvestigateAsSquad(Belief.Of(s.source) != null ? Belief.Of(s.source).position : s.position);
                    suspicionMeter = Mathf.Max(suspicionMeter, 60f);
                    return;
                }
                if (s.kind == StimulusKind.VehicleEngine)
                {
                    // Looking for him and a car drives: the search follows the engine
                    if (currentState == EnemyState.Searching || currentState == EnemyState.Suspicious)
                    {
                        if (Time.time - _lastVehicleBark > 15f)
                        {
                            _lastVehicleBark = Time.time;
                            Barks.Say(BarkKind.Custom, Belief.Of(s.source), "Araba sesi!");
                        }
                        if (currentState == EnemyState.Searching) _squad?.Search?.Recenter(s.position);
                        else
                        {
                            _investigatePoint = s.position;
                            _investigateLookOnly = false;
                            _investigateTimer = investigateDuration;
                        }
                        return;
                    }
                    // Calm: a car close by makes them suspicious and they go to look (in a forest they may hear it
                    // without seeing it); farther away they only turn toward the sound (18.4)
                    if (s.source.InVehicle && d < VehicleNoticeRange)
                    {
                        _lookAt = s.position;
                        _lookAtUntil = Time.time + 6f;
                        suspicionMeter = Mathf.Max(suspicionMeter, Mathf.Lerp(60f, 30f, d / VehicleNoticeRange));
                        _lastSawSomething = Time.time;
                        if (currentState != EnemyState.Suspicious)
                        {
                            _investigatePoint = s.position;
                            _investigateLookOnly = false;
                            _investigateTimer = investigateDuration;
                            SetState(EnemyState.Suspicious);
                            _squad?.Raise(AlertPhase.Suspicious, s.position);
                        }
                        return;
                    }
                    _lookAt = s.position;
                    _lookAtUntil = Time.time + 4f;
                    return;
                }
                if (currentState == EnemyState.Searching) { _squad?.Search?.Recenter(s.position); return; }
                // Footsteps and the like: the old hearing rule
                HearSoundInternal(s.position, s.radius, atPlayer: true);
                return;
            }

            // A friend's gunshot: somebody is fighting there
            if (s.kind == StimulusKind.Gunshot && s.source != null && !hostile && s.source != SelfTarget)
            {
                if (currentState == EnemyState.Combat || currentState == EnemyState.Searching) return;
                _squad?.Raise(AlertPhase.Suspicious, s.position);
                return;
            }

            // Distraction (rock, unknown noise)
            if (currentState == EnemyState.Combat) return;
            if (s.kind == StimulusKind.RockLand)
            {
                if (Time.time - _lastRockTime > 60f) _rocksHeard = 0;
                _rocksHeard++;
                _lastRockTime = Time.time;
                bool backtrace = s.sourceHint.sqrMagnitude > 0.01f && (p.rockBacktrace == 2 || (p.rockBacktrace == 1 && _rocksHeard >= 2));
                if (backtrace)
                {
                    GoLookAt(s.position + s.sourceHint.normalized * 15f);
                    suspicionMeter = Mathf.Max(suspicionMeter, 50f);
                    AIDebug.Log(_squad, $"{DisplayName} taşın geldiği yöne bakıyor");
                    return;
                }
            }
            if (currentState == EnemyState.Searching) return;
            HearSoundInternal(s.position, s.radius, atPlayer: false);
        }

        private bool SeesPoint(Vector3 pos, float range)
        {
            Vector3 eye = transform.position + Vector3.up * eyeHeight;
            Vector3 to = pos + Vector3.up * 0.4f - eye;
            float dist = to.magnitude;
            if (dist > range * AIDifficulty.For(_squad).sightMul * SightFactor) return false;
            if (Vector3.Angle(Senses.LookForward, to) > fieldOfViewAngle * 0.5f && dist > 3f) return false;
            int n = Physics.RaycastNonAlloc(eye, to / Mathf.Max(0.01f, dist), StimHits, dist - 0.5f, EnemyPerception.Mask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var c = StimHits[i].collider;
                if (c.transform.IsChildOf(transform) || EnemyPerception.IsCharacter(c)) continue;
                if (c.attachedRigidbody != null && !c.attachedRigidbody.isKinematic) continue;
                return false;
            }
            return true;
        }

        /// <summary>A persistent thing seen: body, parked vehicle, disturbed object, light, grenade (7.3).</summary>
        private void OnEvidence(Stimulus s)
        {
            switch (s.kind)
            {
                case StimulusKind.GrenadeLanded:
                    GrenadeThreat = s.position;
                    GrenadeObject = s.owner as ThrownProjectile;
                    // Saw it land: it came from that way
                    if (s.source != null && SelfTarget != null && AITargetRegistry.IsHostile(SelfTarget, s.source) && s.sourceHint.sqrMagnitude > 0.01f)
                    {
                        Vector3 guess = s.position + s.sourceHint.Flat().normalized * Mathf.Clamp(s.sourceHint.magnitude * 0.9f, 6f, 22f);
                        var ob = Belief.Of(s.source);
                        Belief.Note(s.source, guess, Vector3.zero, Mathf.Max(ob != null ? ob.confidence : 0f, 0.4f), BeliefSource.Inferred);
                    }
                    _grenadeThreatUntil = Time.time + 4f;
                    Barks.Say(BarkKind.GrenadeSeen);
                    if (_squad != null) _squad.scatterUntil = Time.time + 3f;
                    _brain?.DecideNow();
                    if (currentState != EnemyState.Combat) EnterCombat();
                    return;
                case StimulusKind.Body:
                {
                    var dead = s.owner as EnemyController;
                    if (dead == this) return;
                    if (_squad != null && dead != null)
                    {
                        _squad.LastSeenByMate.Remove(dead);
                        _squad.LastSeenAt.Remove(dead);
                        if (dead.KilledSilently && AIDifficulty.For(_squad).encounterMemory) _squad.stealthFound = 1;
                    }
                    if (currentState == EnemyState.Combat) return;
                    if (dead != null) LastLostMateName = dead.DisplayName;
                    Barks.Say(BarkKind.Evidence, null, "Ceset var!");
                    _squad?.Raise(AlertPhase.Searching, s.position);
                    AIDebug.Log(_squad, "ceset bulundu");
                    return;
                }
                case StimulusKind.ParkedVehicle:
                    if (_squad != null) _squad.KnownVehicle = true;
                    if (currentState == EnemyState.Combat) return;
                    Barks.Say(BarkKind.Evidence, null, "Araba! Biri var buralarda.");
                    _squad?.Raise(AlertPhase.Searching, s.position);
                    AIDebug.Log(_squad, "park etmiş araç görüldü");
                    return;
                case StimulusKind.Disturbed:
                    if (currentState == EnemyState.Combat) return;
                    Barks.Say(BarkKind.Evidence, null, "Sandık açılmış!");
                    _squad?.Raise(AlertPhase.Searching, s.position);
                    return;
                case StimulusKind.Light:
                {
                    if (currentState == EnemyState.Combat) return;
                    // Lit torch in the player's hand: a strong clue; a fire or headlights: something is there
                    if (s.source != null && s.source.kind == AITargetKind.Player)
                    {
                        Belief.Note(s.source, s.position, Vector3.zero, s.certainty * 0.9f, BeliefSource.Heard);
                        _playerEvidenceTime = Time.time;
                    }
                    _knownEvidence.Remove(s.id); // lights move: look again next time
                    if (currentState == EnemyState.Suspicious || currentState == EnemyState.Searching) return;
                    if (_lightCheckedAt.TryGetValue(s.id, out float t) && Time.time - t < 60f) return;
                    _lightCheckedAt[s.id] = Time.time;
                    _squad?.Raise(AlertPhase.Suspicious, s.position);
                    GoLookAt(s.position);
                    return;
                }
            }
        }

        private readonly Dictionary<int, float> _lightCheckedAt = new Dictionary<int, float>();

        /// <summary>A shout reached this enemy (11.2): the news goes into the belief; another squad of the same side relays once.</summary>
        public void OnShout(BarkKind kind, Belief info, EnemyController speaker, int shoutId)
        {
            if (IsDead || Surrendered || speaker == null) return;
            if (info != null && info.target != null)
            {
                var ob = Belief.Of(info.target);
                float conf = info.confidence * 0.8f;
                Belief.Note(info.target, info.position, info.velocity, Mathf.Max(conf, ob != null ? Mathf.Min(ob.confidence, conf) : 0f), BeliefSource.Told);
            }
            if (kind == BarkKind.GrenadeSeen && info == null && speaker != null) { }
            if (_squad == null || _squad == speaker.Squad) return;
            if (_squad.HeardShouts.Contains(shoutId)) return;
            _squad.HeardShouts.Add(shoutId);
            _squad.ApplyShout(kind, info, speaker);
            // Relay once to the own squad (another squad's news)
            Barks.Say(kind, info, null, shoutId);
        }

        // ------------------------------------------------------------------ non combat states

        private void UpdateSuspicious(ref Vector3 moveVelocity, ref bool isMoving, ref float animSpeed)
        {
            animator.SetBoolSafe("IsAiming", false);
            float dt = Time.deltaTime;
            var b = Belief.Primary;
            if (Senses.SeesAny && b != null)
            {
                Turn(b.position - transform.position, 480f);
                return;
            }
            if (Time.time >= _glintHoldUntil && Time.time - _lastSawSomething > SuspicionHold) suspicionMeter = Mathf.MoveTowards(suspicionMeter, 0f, suspicionDecayRate * dt);
            Vector3 to = _investigatePoint - transform.position;
            to.y = 0f;
            if (_investigateLookOnly)
            {
                if (to.sqrMagnitude > 0.05f) Turn(to, 240f);
            }
            else if (to.magnitude > 1.5f && _investigateTimer > 0f)
            {
                Vector3 dir = _mover.DirectionTo(_investigatePoint);
                Turn(dir, 360f);
                moveVelocity = dir * (moveSpeed * 0.5f);
                animSpeed = moveSpeed * 0.5f;
                isMoving = true;
            }
            else if (to.magnitude <= 1.5f)
            {
                // Looked there: if the belief pointed here and nobody is here, say so (8.3)
                if (b != null && b.Valid && (b.position - transform.position).sqrMagnitude < 9f && b.confidence > 0.25f && !b.SeenNow)
                {
                    Belief.LookedAndEmpty(b);
                    Barks.Say(BarkKind.Lost, b);
                }
                float k = Mathf.Sin(Time.time * 1.8f) * 60f;
                Turn(Quaternion.AngleAxis(k, Vector3.up) * transform.forward, 120f);
            }
            _investigateTimer -= dt;
            if (suspicionMeter <= 0f && _investigateTimer <= 0f)
            {
                var ph = _squad != null ? _squad.Phase : AlertPhase.Calm;
                // A sitter away from his seat stands; idle life walks him back and sits him down
                bool awayFromPost = Flat(transform.position - _homePos).magnitude > 2f;
                SetState(initialState == EnemyState.Sitting && (ph != AlertPhase.Calm || awayFromPost) ? EnemyState.Idle : initialState);
                if (currentState == EnemyState.Idle && awayFromPost) _idleNextAt = Time.time + Random.Range(1f, 3f);
            }
        }

        private void UpdateSurrendered()
        {
            animator.SetBoolSafe("IsAiming", false);
            _brain?.Reset();
            if (AITarget.Player != null && (AITarget.Player.Position - transform.position).sqrMagnitude > 40f * 40f)
            {
                Surrendered = false;
                BeginFlee();
            }
        }

        private Vector3 _fleeGoal;
        private float _fleeGoalUntil = -1f;
        private static readonly float[] FleeAngles = { 0f, 35f, -35f, 70f, -70f, 105f, -105f };
        private static readonly float[] FleeFallbackAngles = { 90f, -90f, 130f, -130f };

        /// <summary>
        /// A place to run to, away from the threat, that has a real route (user bug report 16 Nov: fleeing men ran into
        /// walls because the goal was a straight line, re-picked every frame). Kept for 5 s or until reached.
        /// </summary>
        public Vector3 FleeGoalFrom(Vector3 threat)
        {
            Vector3 me = transform.position;
            if (Time.time < _fleeGoalUntil && Flat(_fleeGoal - me).magnitude > 2.5f) return _fleeGoal;
            Vector3 away = Flat(me - threat);
            if (away.sqrMagnitude < 0.01f) away = -transform.forward;
            away.Normalize();
            Vector3 best = me + away * 4f;
            float bestScore = float.MinValue;
            float nowDist = Flat(me - threat).magnitude;
            bool navMesh = LocalNavMeshBuilder.Instance != null && LocalNavMeshBuilder.Instance.HasNavMesh;
            foreach (float a in FleeAngles)
            {
                Vector3 dir = Quaternion.AngleAxis(a, Vector3.up) * away;
                Vector3 c = me + dir * 18f;
                if (!EnemyMover.OnNavMesh(c, 5f, out c)) continue;
                float straight = Flat(c - me).magnitude;
                if (straight < 4f) continue;
                float len = _mover.PathLength(c, out bool routed);
                // 17 Nov AI playtest: a point behind a wall has NavMesh but no route; PathLength then guessed
                // "straight × 1.4", the point won, and he ran into the wall for seconds. A real route is required.
                if (navMesh && !routed) continue;
                if (len > straight * 2f + 4f) continue; // the route goes back round: not a way out
                float score = Flat(c - threat).magnitude - nowDist - len * 0.3f - Mathf.Abs(a) * 0.03f;
                // A wall right in front: bad even with a route (no NavMesh case)
                if (Physics.Raycast(me + Vector3.up, dir, 3f, EnemyPerception.Mask, QueryTriggerInteraction.Ignore)) score -= 15f;
                if (score > bestScore) { bestScore = score; best = c; }
            }
            // Cornered (nothing away from him has a route): along the wall, then back past him at an angle
            if (bestScore == float.MinValue && navMesh)
            {
                foreach (float a in FleeFallbackAngles)
                {
                    Vector3 c = me + Quaternion.AngleAxis(a, Vector3.up) * away * 12f;
                    if (!EnemyMover.OnNavMesh(c, 5f, out c)) continue;
                    _mover.PathLength(c, out bool routed);
                    if (!routed) continue;
                    best = c;
                    break;
                }
            }
            _fleeGoal = best;
            _fleeGoalUntil = Time.time + 5f;
            return best;
        }

        private void UpdateFleeing(ref Vector3 moveVelocity, ref bool isMoving, ref float animSpeed)
        {
            Vector3 from = Belief.Primary != null ? Belief.Primary.position : (AITarget.Player != null ? AITarget.Player.Position : transform.position - transform.forward);
            Vector3 goal = FleeGoalFrom(from);
            Vector3 dir = _mover.DirectionTo(goal);
            Turn(dir, 540f);
            float speed = moveSpeed * 1.3f;
            moveVelocity = dir * speed;
            animSpeed = speed;
            isMoving = true;
        }

        // ------------------------------------------------------------------ combat / search execution

        private void UpdateCombat(ref Vector3 moveVelocity, ref bool isMoving, ref float animSpeed)
        {
            if (weapon != null && weapon.IsFirearm && weapon.OutOfAmmo && !IsReloading)
            {
                // 13.5: ground weapon first (brain Pickup), else hands
                if (AIInterest.Find(this, Belief.Primary, out _) == null) SwitchToMelee();
            }
            if (_squad != null && _squad.HoldFire && currentState == EnemyState.Combat)
            {
                // Robbery / ambush: aim, do not fire
                var hb = _squad.BestBelief(this);
                if (hb != null) Turn(hb.position - transform.position, 360f);
                animator.SetBoolSafe("IsAiming", IsGunner);
                _brain.Reset();
                return;
            }

            _brain.Tick();
            Vector3 me = transform.position;
            bool melee = IsMeleeFighter;
            bool gunner = !melee && weapon != null && weapon.IsFirearm;
            var b = Belief.Primary;
            if (_body != null) _body.SetGuard(_brain.Guard && _body.UsingShield);
            var holder = GetComponent<ShieldHolder>();
            if (holder != null && holder.Usable) holder.GunSide = AIDifficulty.For(_squad).shieldSideFire && _weaponType == WeaponType.Pistol && _brain.WantsFire;

            // Walk
            Vector3 goal = _brain.Goal;
            Vector3 toGoal = goal - me;
            toGoal.y = 0f;
            bool charge = _brain.ChargeNow || _brain.Bash;
            float distToTarget = b != null ? Flat(b.position - me).magnitude : 99f;
            float stopAt = charge ? meleeRange + 0.3f : 0.5f;
            float goalDist = charge ? distToTarget : toGoal.magnitude;
            Vector3 moveDir = Vector3.zero;
            if (_brain.Moving && goalDist > stopAt)
            {
                // Charging a target in sight: straight at it; out of sight: the brain's goal (its seen path)
                bool chargeSeen = charge && b != null && b.SeenNow;
                moveDir = _mover.DirectionTo(chargeSeen ? b.position : goal);
                float speed = _brain.Speed;
                // Slow down for the swing only when the target is not running away
                if (charge && distToTarget < 4f && (b == null || Flat(b.velocity).magnitude < 2.5f)) speed = Mathf.Min(speed, moveSpeed * 0.8f);
                moveVelocity = moveDir * speed;
                animSpeed = speed;
                isMoving = true;
                _mover.NoteReachability(chargeSeen ? b.position : goal);
            }
            else _mover.NoteReachability(me);

            if (melee && b != null && b.SeenNow && distToTarget < meleeMinDistance)
            {
                float back = moveSpeed * 0.55f;
                Vector3 away = Flat(me - b.position).normalized;
                moveDir = Vector3.zero;
                moveVelocity = away * back;
                animSpeed = back;
                isMoving = true;
            }

            // Face
            Vector3? face = _brain.FacePoint;
            bool shootPose = gunner && !IsReloading && _brain.WantsFire;
            if (face.HasValue && (shootPose || !isMoving || _brain.Guard || (melee && distToTarget < meleeRange + 1f) || moveDir.sqrMagnitude < 0.01f || toGoal.magnitude < 3f))
                Turn(face.Value - me, _brain.TurnSpeed);
            else if (moveDir.sqrMagnitude > 0.01f) Turn(moveDir, _brain.TurnSpeed);

            animator.SetBoolSafe("IsAiming", shootPose || (gunner && Actor.Readiness == Readiness.Ready && b != null && b.SeenNow && !isMoving));
            if (shootPose && isMoving)
            {
                moveVelocity *= 0.7f;
                animSpeed *= 0.7f;
            }
            if (shootPose) TryShoot(isMoving);

            // Strikes (token holders): quick, heavy against a raised shield, bash with a shield
            // A melee fighter at arm's length always strikes (the token only limits who comes in, not who hits)
            if ((melee || _brain.Bash) && _body != null && (_brain.HasToken || melee) && b != null && b.SeenNow && distToTarget <= meleeRange + 0.3f
                && Time.time >= _nextMeleeTime && Actor.CanShoot)
            {
                Vector3 toT = Flat(b.position - me);
                if (Vector3.Angle(transform.forward, toT) < 45f)
                {
                    bool ok = _brain.Bash ? _body.TryAttack(_meleeStats, _meleeCategory, true, false)
                                          : _body.TryAttack(_meleeStats, _meleeCategory, false, _brain.Heavy);
                    if (ok) lastAttackTime = Time.time;
                }
            }
        }

        /// <summary>A blow on the player's car at a window (car assault): the usual swing, the damage through VehicleThreat.</summary>
        public bool StrikeCar(int slot)
        {
            if (IsDead || _body == null || Time.time < _nextMeleeTime || !Actor.CanShoot) return false;
            if (!_body.TryAttack(_meleeStats, _meleeCategory, false, false)) return false;
            lastAttackTime = Time.time;
            _nextMeleeTime = Time.time + attackCooldown * Random.Range(0.8f, 1.2f);
            VehicleThreat.MeleeHitCar(this, slot, _meleeStats.damage);
            return true;
        }

        // 64, not 12: each man has ~14 colliders (11 bones, capsule, weapon, shield). The shooter's own (overlapping
        // at the start of the sweep) and the ground filled 12 slots, so a friend in the line of fire was often not
        // reported and got shot in the back (17 Nov playtest: the shield man in front of the player was hit by his
        // own riflemen, panicked and ran).
        private static readonly RaycastHit[] FireHits = new RaycastHit[64];
        public static float FriendlyFireRadius = 1.2f;

        private void TryShoot(bool moving)
        {
            if (weapon == null || !weapon.IsFirearm || !Actor.CanShoot || IsReloading) return;
            if (Time.time < _nextShotAt) return;
            Vector3 aim = _brain.FireAt;
            Vector3 to = aim - transform.position;
            to.y = 0f;
            if (Vector3.Angle(transform.forward, to) > 35f) return;
            if (weapon.CurrentAmmo <= 0) return;

            var p = AIDifficulty.For(_squad);
            // Friendly fire check (13.4): a sphere sweep along the shot
            Vector3 muzzle = transform.position + Vector3.up * 1.4f;
            Vector3 d = aim - muzzle;
            float dist = d.magnitude;
            int n = Physics.SphereCastNonAlloc(muzzle, FriendlyFireRadius * (Nerve.Level >= NerveLevel.Tense && Nerve.discipline < 0.4f ? 0.4f : 1f),
                d / Mathf.Max(0.01f, dist), FireHits, Mathf.Max(0f, dist - 1f), ~0, QueryTriggerInteraction.Ignore);
            float nearest = float.MaxValue;
            EnemyController blocker = null;
            for (int i = 0; i < n; i++)
            {
                var c = FireHits[i].collider;
                if (c == null || c.transform.IsChildOf(transform)) continue;
                if (FireHits[i].distance < 0.3f) continue;
                var o = EnemyPerception.OwnerOf(c);
                if (o != null && o != this && !o.IsDead && FireHits[i].distance < nearest) { nearest = FireHits[i].distance; blocker = o; }
            }
            if (blocker != null)
            {
                _brain.SideStep();
                _nextShotAt = Time.time + 0.5f;
                return;
            }

            // First shot of a new sight misses on purpose (by firstShotMissChance)
            if (_firstShotPending)
            {
                _firstShotPending = false;
                if (Random.value < p.firstShotMissChance) aim += transform.right * (Random.value < 0.5f ? -1.2f : 1.2f);
            }
            var b = Belief.Primary;
            float range = Mathf.Max(1f, attackRange);
            float spread = Mathf.Lerp(aimSpreadClose, aimSpreadFar, Mathf.Clamp01(dist / range)) * p.spreadMul * Nerve.SpreadMul * _brain.FireSpreadMul;
            if (b != null && b.velocity.magnitude > 4f) spread *= 1.3f;
            if (moving) spread *= 1.4f;

            if (weapon.TryFire(aim, ~0, out RaycastHit hit, true, spread, transform))
            {
                lastAttackTime = Time.time;
                _brain.NoteShotFired();
                if (hit.collider != null && hit.collider.GetComponentInParent<Combat.PlayerShooter>() != null) Nerve.Add(-EnemyNerve.HitPlayerRelief);
                if (_burstLeft <= 0) _burstLeft = _weaponType == WeaponType.Rifle ? Random.Range(2, 5) : Random.Range(1, 3);
                if (TotalAmmo < 8) _burstLeft = Mathf.Min(_burstLeft, 2);
                _burstLeft--;
                if (_burstLeft > 0) _nextShotAt = Time.time + weapon.ShotInterval;
                else _nextShotAt = Time.time + (_weaponType == WeaponType.Rifle ? Random.Range(0.6f, 1.2f) : Random.Range(0.5f, 1f)) * (attackCooldown / 1.5f);
            }
        }

        // ------------------------------------------------------------------ actions (9.2)

        public void StartReload()
        {
            if (weapon == null || !weapon.IsFirearm || IsReloading) return;
            if (Actor.Busy) return;
            if (weapon.TryReload()) Actor.TryStart(EnemyAction.Reload, 10f, null, () => weapon?.CancelReload());
        }

        public void StartHeal()
        {
            if (bandages <= 0 || Actor.Busy) return;
            if (Actor.TryStart(EnemyAction.Heal, 4f, () =>
            {
                if (IsDead) return;
                bandages--;
                currentHealth = Mathf.Min(maxHealth, currentHealth + maxHealth * 0.35f);
                AIDebug.Log(_squad, $"{DisplayName} sargı yaptı");
            })) { }
        }

        private Vector3 _throwTarget;
        public Vector3? PlannedThrowTarget => Actor.Current == EnemyAction.Throw ? _throwTarget : (Vector3?)null;

        public void StartGrenadeThrow(Vector3 targetPoint, Vector3 velocity)
        {
            if (grenades <= 0 || Actor.Busy) return;
            _throwTarget = targetPoint;
            if (_squad != null) _squad.lastGrenadeTime = Time.time; // claim the squad slot now
            Barks.Say(BarkKind.Grenade);
            Actor.TryStart(EnemyAction.Throw, AIDifficulty.For(_squad).throwWindup,
                () => { if (!IsDead) EnemyThrower.LaunchGrenade(this, velocity); },
                () => EnemyThrower.DropGrenade(this));
        }

        public void StartRockThrow(Vector3 velocity)
        {
            if (rocks <= 0 || Actor.Busy) return;
            Actor.TryStart(EnemyAction.Throw, 0.5f, () => { if (!IsDead) EnemyThrower.LaunchRock(this, velocity); });
        }

        public void StartPickup(WorldPickup p)
        {
            if (p == null || Actor.Busy) return;
            p.claimedBy = this;
            Actor.TryStart(EnemyAction.Pickup, 1f, () => { if (p != null) EquipFromPickup(p); }, () => { if (p != null) p.claimedBy = null; });
        }

        /// <summary>Brutal: picks up the player's grenade and throws it back (13.8).</summary>
        public bool TryThrowBack(Vector3 at)
        {
            var g = GrenadeObject;
            if (g == null || Actor.Busy) return false;
            if (!EnemyThrower.Solve(EnemyThrower.Hand(this), at, out Vector3 v)) return false;
            Actor.TryStart(EnemyAction.Pickup, 0.5f, () =>
            {
                if (g == null || IsDead) return;
                Destroy(g.gameObject);
                grenades++;
                Barks.Say(BarkKind.Custom, null, "Al geri!");
                Actor.TryStart(EnemyAction.Throw, 0.4f, () => { if (!IsDead) EnemyThrower.LaunchGrenade(this, v); }, () => EnemyThrower.DropGrenade(this));
            });
            GrenadeThreat = null;
            GrenadeObject = null;
            return true;
        }

        private void InterruptAction()
        {
            if (Actor.Current == EnemyAction.Reload) weapon?.CancelReload();
            Actor.Interrupt();
        }

        public void BeginFlee()
        {
            if (Fleeing || IsDead) return;
            Fleeing = true;
            Barks.Say(BarkKind.Fleeing);
            if (_squad != null) _squad.fled++;
            AIDebug.Log(_squad, $"{DisplayName} kaçtı");
        }

        public void DoSurrender()
        {
            if (Surrendered || IsDead) return;
            Surrendered = true;
            Barks.Say(BarkKind.Surrender);
            EnemySquad.ReleaseToken(this);
            CoverMap.Release(this);
            DropCurrentWeapon();
            _outOfAmmo = true;
            SetupWeaponType();
            if (_body != null) { _body.SetGuard(false); }
            AIDebug.Log(_squad, $"{DisplayName} teslim oldu");
        }

        // ------------------------------------------------------------------ routine and patrol (16)

        public List<Vector3> patrolPoints = new List<Vector3>();
        public Vector2 patrolWait = new Vector2(3f, 7f);
        public bool patrolLoop = true;
        public static float PatrolRingRadius = 9f;
        public static float PatrolSpeedFactor = 0.45f;
        private int _patrolIndex = -1;
        private int _patrolStep = 1;
        private float _patrolWaitUntil;
        private Vector3? _routineGoal;
        private Vector3 _routineLook;
        private System.Action _routineArrived;

        /// <summary>Walk somewhere for a routine event (break, rain shelter, guard change).</summary>
        public void RoutineWalk(Vector3 goal, Vector3 lookDir, System.Action arrived = null)
        {
            if (currentState != EnemyState.Idle && currentState != EnemyState.Patrolling && currentState != EnemyState.Sitting) return;
            if (currentState == EnemyState.Sitting) SetState(EnemyState.Idle);
            _routineGoal = EnemyMover.OnNavMesh(goal, 3f, out Vector3 s) ? s : goal;
            _routineLook = lookDir;
            _routineArrived = arrived;
        }

        public bool InRoutine => _routineGoal.HasValue;

        // ---- idle life (user bug report 16 Nov: after a search they stood still side by side forever) ----
        private Vector3 _homePos;
        private float _homeYaw;
        private float _idleNextAt;

        /// <summary>
        /// An idle man is never a statue: away from his post he walks back (and sits down again if he was sitting);
        /// on alert (Guarded) he walks the camp edge and looks outward; calm, now and then he strolls a few metres or
        /// looks around.
        /// </summary>
        private void UpdateIdleLife()
        {
            if (Carried != null || SignatureLocked || Time.time < _idleNextAt) return;
            if (_squad != null && _squad.HoldFire) return;
            var phase = _squad != null ? _squad.Phase : AlertPhase.Calm;
            Vector3 me = transform.position;
            Vector3 homeFace = Quaternion.Euler(0f, _homeYaw, 0f) * Vector3.forward;
            float fromHome = Flat(me - _homePos).magnitude;

            if (fromHome > 2f && (phase == AlertPhase.Calm || Random.value < 0.5f))
            {
                _idleNextAt = Time.time + Random.Range(2f, 4f);
                bool sitBack = initialState == EnemyState.Sitting && phase == AlertPhase.Calm;
                RoutineWalk(_homePos, homeFace, sitBack ? (System.Action)(() =>
                {
                    if (currentState == EnemyState.Idle && (_squad == null || _squad.Phase == AlertPhase.Calm)) SetState(EnemyState.Sitting);
                }) : null);
                return;
            }
            if (initialState == EnemyState.Sitting && phase == AlertPhase.Calm)
            {
                SetState(EnemyState.Sitting);
                return;
            }
            if (phase == AlertPhase.Guarded || phase == AlertPhase.Suspicious)
            {
                _idleNextAt = Time.time + Random.Range(6f, 12f);
                Vector3 c = _squad != null ? _squad.home : _homePos;
                Vector2 r = Random.insideUnitCircle.normalized * Random.Range(5f, 10f);
                Vector3 goal = c + new Vector3(r.x, 0f, r.y);
                RoutineWalk(goal, goal - c);
                return;
            }
            _idleNextAt = Time.time + Random.Range(8f, 20f);
            if (Random.value < 0.5f)
            {
                Vector2 r = Random.insideUnitCircle * 4f;
                RoutineWalk(_homePos + new Vector3(r.x, 0f, r.y), Random.insideUnitSphere.Flat());
            }
            else
            {
                _lookAt = me + Random.insideUnitSphere.Flat().normalized * 6f;
                _lookAtUntil = Time.time + 3f;
            }
        }

        private void UpdateRoutineWalk(ref Vector3 moveVelocity, ref bool isMoving, ref float animSpeed)
        {
            Vector3 g = _routineGoal.Value;
            Vector3 to = g - transform.position;
            to.y = 0f;
            if (to.magnitude < 1f)
            {
                if (_routineLook.sqrMagnitude > 0.01f) Turn(_routineLook, 180f);
                var cb = _routineArrived;
                _routineArrived = null;
                _routineGoal = null;
                cb?.Invoke();
                return;
            }
            if (LocalNavMeshBuilder.Instance == null || !LocalNavMeshBuilder.Instance.HasNavMesh) return;
            Vector3 dir = _mover.DirectionTo(g);
            Turn(dir, 360f);
            float speed = moveSpeed * PatrolSpeedFactor;
            moveVelocity = dir * speed;
            animSpeed = speed;
            isMoving = true;
        }

        private void UpdatePatrol(ref Vector3 moveVelocity, ref bool isMoving, ref float animSpeed)
        {
            if (patrolPoints.Count == 0)
            {
                Vector3 home = _squad != null && _squad.kind == SquadKind.Camp ? _squad.home : transform.position;
                float radius = _squad != null && _squad.kind == SquadKind.Camp ? Random.Range(14f, 18f) : PatrolRingRadius;
                float start = Random.Range(0f, 360f);
                for (int i = 0; i < 6; i++)
                {
                    Vector3 pt = home + Quaternion.AngleAxis(start + i * 60f, Vector3.up) * Vector3.forward * radius;
                    if (EnemyMover.OnNavMesh(pt, 3f, out Vector3 s)) patrolPoints.Add(s);
                }
                if (patrolPoints.Count == 0) { patrolPoints.Add(transform.position); return; }
            }
            if (_patrolIndex < 0) _patrolIndex = patrolLoop ? Random.Range(0, patrolPoints.Count) : 0;
            if (LocalNavMeshBuilder.Instance == null || !LocalNavMeshBuilder.Instance.HasNavMesh) return;
            // Rain: outer guards shelter at home (16.2)
            var w = EndlessSurvival.Survival.WeatherSystem.Instance;
            if (w != null && w.RainIntensity > 0.5f && _squad != null && _squad.kind == SquadKind.Camp && _squad.Phase == AlertPhase.Calm)
            {
                if ((transform.position - _squad.home).sqrMagnitude > 25f) RoutineWalk(_squad.home + Random.insideUnitSphere.Flat() * 3f, Vector3.zero);
                return;
            }
            if (Time.time < _patrolWaitUntil) return;

            // Patrol members walk in single file 2 m apart: the followers keep behind the one ahead
            Vector3 goal = patrolPoints[_patrolIndex];
            if (_squad != null && _squad.kind == SquadKind.Patrol)
            {
                EnemyController ahead = null;
                foreach (var m in _squad.Members)
                {
                    if (m == this) break;
                    if (m != null && !m.IsDead && m.currentState == EnemyState.Patrolling) ahead = m;
                }
                if (ahead != null)
                {
                    Vector3 fol = ahead.transform.position - ahead.transform.forward * 2f;
                    if ((fol - transform.position).sqrMagnitude < 1f) return;
                    goal = fol;
                }
            }
            Vector3 to = goal - transform.position;
            to.y = 0f;
            if (to.magnitude < 1.2f)
            {
                if (patrolLoop) _patrolIndex = (_patrolIndex + 1) % patrolPoints.Count;
                else
                {
                    if (_patrolIndex + _patrolStep >= patrolPoints.Count || _patrolIndex + _patrolStep < 0) _patrolStep = -_patrolStep;
                    _patrolIndex = Mathf.Clamp(_patrolIndex + _patrolStep, 0, patrolPoints.Count - 1);
                }
                _patrolWaitUntil = Time.time + Random.Range(patrolWait.x, patrolWait.y);
                return;
            }
            Vector3 dir = _mover.DirectionTo(goal);
            Turn(dir, 360f);
            float speed = moveSpeed * PatrolSpeedFactor;
            moveVelocity = dir * speed;
            animSpeed = speed;
            isMoving = true;
        }

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
            if (IsDead || animator == null || !animator.GetBoolSafe("IsAiming")) return;
            Transform spine = animator.GetBoneTransform(HumanBodyBones.Spine);
            if (spine == null) spine = animator.GetBoneTransform(HumanBodyBones.Chest);
            if (spine != null) spine.rotation = Quaternion.AngleAxis(aimRotationOffset, transform.up) * spine.rotation;
        }

        private static Vector3 Flat(Vector3 v)
        {
            v.y = 0f;
            return v;
        }

        // ------------------------------------------------------------------ bubbles

        private EnemyDialogue _dialogue;

        public void ShowBubble(string text, Color color, float seconds)
        {
            if (_dialogue == null) _dialogue = GetComponent<EnemyDialogue>();
            if (_dialogue == null) _dialogue = gameObject.AddComponent<EnemyDialogue>();
            _dialogue.Shout(text, color, seconds);
        }

        public void HideBubble()
        {
            if (_dialogue == null) _dialogue = GetComponent<EnemyDialogue>();
            _dialogue?.StopSpeaking();
        }

        public bool BubbleFallback(out string text, out Color color)
        {
            text = null;
            color = Color.white;
            if (_dialogue == null) return false;
            return _dialogue.FallbackVisible(out text, out color);
        }

        // ------------------------------------------------------------------ damage and death

        public void TakeDamage(float damage, Vector3 hitPoint, Vector3 hitNormal) =>
            TakeDamage(new DamageInfo(damage, DamageType.Other, hitPoint, hitNormal));

        /// <summary>Debug only (AI playtest): every hit an enemy takes, before filtering.</summary>
        public static System.Action<EnemyController, DamageInfo> DebugDamaged;

        public void TakeDamage(DamageInfo info)
        {
            if (IsDead) return;
            DebugDamaged?.Invoke(this, info);
            float dmg = info.Amount;
            bool unaware = currentState != EnemyState.Combat || Surrendered;
            if (unaware && info.ByPlayer && _squad != null && _squad.Phase != AlertPhase.Combat) _squad.PlayerStarted = true;
            NoteHit(info);
            if (_body != null)
            {
                dmg = _body.FilterDamage(info);
                if (dmg <= 0f)
                {
                    EnterCombat();
                    return;
                }
            }
            currentHealth -= dmg;
            Nerve.Add(EnemyNerve.HitGain);
            if (Actor.Current == EnemyAction.Heal) InterruptAction();
            if (currentHealth <= 0f)
            {
                KilledSilently = unaware && info.Type == DamageType.Melee;
                if (KilledSilently && _squad != null) _squad.SilentKills++;
                Die(info.ByPlayer, false, Vector3.zero);
                return;
            }
            EndlessCombat.Audio.GameSound.Play(EndlessCombat.Audio.SoundId.EnemyHurt, transform.position,
                Mathf.Clamp01(dmg / 40f), info.Type.ToString(), info.ByPlayer);
            if (!Surrendered) EnterCombat();
        }

        /// <summary>Where the hit came from goes into the belief (8.2 "hit"); unexpected direction raises the nerve.</summary>
        private void NoteHit(DamageInfo info)
        {
            _lastUnderFire = Time.time;
            Vector3 dir = info.SourceDirection;
            dir.y = 0f;
            AITarget src = info.Source != null ? info.Source.GetComponentInParent<AITarget>() : null;
            if (src == null && info.ByPlayer) src = AITarget.Player;
            if (dir.sqrMagnitude > 0.01f)
            {
                dir.Normalize();
                if (Time.time - _lastHitTime < 3f && Vector3.Angle(dir, _lastHitDir) > 90f) HitFromTwoSides = true;
                else if (Time.time - _lastHitTime > 3f) HitFromTwoSides = false;
                var pb = Belief.Primary;
                if (pb != null && pb.Valid)
                {
                    Vector3 toBelief = Flat(pb.position - transform.position);
                    if (toBelief.sqrMagnitude > 0.01f && Vector3.Angle(toBelief, dir) > 90f) Nerve.Add(EnemyNerve.FlankedGain);
                }
                _lastHitDir = dir;
                _lastHitTime = Time.time;
            }
            if (src != null && SelfTarget != null && AITargetRegistry.IsHostile(SelfTarget, src))
            {
                var ob = Belief.Of(src);
                Vector3 guess = info.Source != null ? info.Source.transform.position : transform.position + dir * 20f;
                if (info.Source == null || (src.kind == AITargetKind.Player && src.InVehicle)) guess = src.InVehicle ? src.Position : transform.position + dir * 20f;
                // Hearing the shooter is not seeing him: the place is a guess along the shot
                if (info.Type == DamageType.Bullet && info.Source != null)
                {
                    float dist = Vector3.Distance(transform.position, info.Source.transform.position);
                    guess = transform.position + dir * dist + Random.insideUnitSphere.Flat() * Mathf.Min(4f, dist * 0.1f);
                    if (info.ByPlayer && dist > 35f && _squad != null && AIDifficulty.For(_squad).encounterMemory) _squad.longRange++;
                }
                Belief.Note(src, guess, Vector3.zero, Mathf.Max(ob != null ? ob.confidence : 0f, 0.5f), BeliefSource.Inferred);
            }
        }

        public void NearMiss()
        {
            if (IsDead) return;
            Nerve.NearMiss();
            _lastUnderFire = Time.time;
            if (_brain != null && _brain.Choice == EnemyBrain.Option.PeekFire) _brain.DecideNow();
            if (currentState != EnemyState.Combat && !Surrendered) EnterCombat();
        }

        public void HitByVehicle(float damage, Vector3 impulse, bool lethal)
        {
            if (IsDead) return;
            currentHealth -= lethal ? Mathf.Max(damage, currentHealth) : damage;
            if (currentHealth <= 0f)
            {
                Die(true, true, impulse);
                return;
            }
            EnterCombat();
            Vector3 push = new Vector3(impulse.x, 0f, impulse.z);
            push = Vector3.ClampMagnitude(push * 0.25f, 6f);
            if (_body != null) _body.Stagger(1.2f, push);
        }

        private System.Collections.IEnumerator PushRagdollNextFixedUpdate(Vector3 velocityChange)
        {
            yield return new WaitForFixedUpdate();
            foreach (var rb in ragdollRigidbodies)
                if (rb != null && !rb.isKinematic) rb.AddForce(velocityChange, ForceMode.VelocityChange);
        }

        private int _bodyStimulus;

        private void Die(bool byPlayer, bool vehicleHit, Vector3 vehicleImpulse)
        {
            IsDead = true;
            EndlessCombat.Audio.GameSound.Play(EndlessCombat.Audio.SoundId.EnemyDeath, transform.position, 1f,
                vehicleHit ? "vehicle" : null, byPlayer);
            Barks.Cancel();
            if (_statusLabel != null) _statusLabel.gameObject.SetActive(false);
            if (_stealthIndicator != null) _stealthIndicator.text = "";
            if (agent != null) agent.enabled = false;
            if (_body != null) _body.Kill();
            if (_squad != null) _squad.MemberDied(this, byPlayer);
            AISignatures.OnMemberDied(this);
            WitnessDeath();
            LeaveSquad();
            CoverMap.Release(this);
            DropShield();

            EndlessSurvival.World.GameStatsTracker.Instance?.RecordEnemyKilled();
            if (vehicleHit)
            {
                EnableRagdoll();
                StartCoroutine(PushRagdollNextFixedUpdate(vehicleImpulse));
            }
            else if (!PlayDeathAnimation()) EnableRagdoll();

            LootChest corpseChest = CreateCorpseLoot();
            DropHeldWeapon();
            DropGrenades();
            _bodyStimulus = AIStimuli.RegisterPersistent(new Stimulus
            {
                kind = StimulusKind.Body, position = transform.position, radius = 20f, needsSight = true, certainty = 0.2f, owner = this
            });
            StartCoroutine(RemoveCorpseLater(corpseChest));
        }

        /// <summary>Mates within 20 m who see the death: nerve and a "man down" shout (10.2, 11.3).</summary>
        private void WitnessDeath()
        {
            foreach (var o in Active)
            {
                if (o == null || o == this || o.IsDead || o.SelfTarget == null || o.SelfTarget.team != CombatTeam.Enemy) continue;
                if ((o.transform.position - transform.position).sqrMagnitude > 400f) continue;
                if (!o.Senses.SeesMate(this)) continue;
                o.Nerve.Add(EnemyNerve.FriendDeathGain);
                o.LastLostMateName = DisplayName;
                o.Barks.Say(BarkKind.ManDown);
                if (o.currentState != EnemyState.Combat && !o.Surrendered) o.EnterCombat();
            }
        }

        private const float CorpseLifetime = 180f;

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

        /// <summary>The gun drops with the rounds left in its magazine (13.10).</summary>
        private void DropHeldWeapon()
        {
            if (heldWeaponItem == null || dropTable == null || dropTable.pickupPrefab == null) return;
            WeaponController held = _stowedGun != null ? _stowedGun : weapon;
            if (Surrendered && held == null) return; // already on the ground
            Vector3 position = held != null ? held.transform.position : transform.position + Vector3.up;
            var p = WorldPickup.Spawn(dropTable.pickupPrefab, heldWeaponItem, 1, position, null);
            if (p != null && held != null && held.IsFirearm) p.magazineRounds = held.CurrentAmmo;
            if (held != null) held.gameObject.SetActive(false);
        }

        private void DropGrenades()
        {
            if (grenades <= 0 || dropTable == null || dropTable.pickupPrefab == null) return;
            var s = EndlessSurvival.Survival.SurvivalSettings.Instance;
            var item = s != null ? s.Item("Item_Grenade") : null;
            if (item != null) WorldPickup.Spawn(dropTable.pickupPrefab, item, grenades, transform.position + Vector3.up * 0.6f, null);
            grenades = 0;
        }

        private void SetupRagdoll()
        {
            ragdollRigidbodies = new List<Rigidbody>(GetComponentsInChildren<Rigidbody>());
            ragdollColliders = new List<Collider>(GetComponentsInChildren<Collider>());
            var mainRb = GetComponent<Rigidbody>();
            var mainCol = GetComponent<Collider>();
            if (mainRb != null) ragdollRigidbodies.Remove(mainRb);
            if (mainCol != null) ragdollColliders.Remove(mainCol);
        }

        private void DisableRagdoll()
        {
            foreach (var rb in ragdollRigidbodies) rb.isKinematic = true;
            if (animator != null) animator.enabled = true;
        }

        private bool PlayDeathAnimation()
        {
            if (animator == null) return false;
            var layer = EndlessSurvival.Survival.SurvivalAnimLayer.GetOrAdd(animator);
            if (layer == null || !layer.Available || layer.ClipLength(EndlessSurvival.Survival.SurvivalAnimClips.NpcDeath) <= 0f) return false;
            animator.SetFloatSafe("Speed", 0f);
            animator.SetBoolSafe("IsAiming", false);
            layer.SetInstant(EndlessSurvival.Survival.SurvAnim.Death);
            var mainCol = GetComponent<Collider>();
            if (mainCol != null) mainCol.enabled = false;
            return true;
        }

        private void EnableRagdoll()
        {
            if (animator != null) animator.enabled = false;
            GetComponent<CharacterBodyColliders>()?.ReleaseVehicles();
            foreach (var rb in ragdollRigidbodies)
            {
                rb.isKinematic = false;
                rb.useGravity = true;
                rb.AddForce(Vector3.up * 2f, ForceMode.Impulse);
            }
            var mainCol = GetComponent<Collider>();
            if (mainCol != null) mainCol.enabled = false;
        }

        public void SetState(EnemyState newState)
        {
            if (currentState == newState) return;
            var old = currentState;
            currentState = newState;
            if (newState != EnemyState.Combat && newState != EnemyState.Searching) _brain?.Reset();
            if (newState == EnemyState.Combat || newState == EnemyState.Searching)
            {
                _routineGoal = null;
                Sleeping = false;
                if (old == EnemyState.Searching || old == EnemyState.Combat) return;
                var dialogue = GetComponent<EnemyDialogue>();
                if (dialogue != null && newState == EnemyState.Combat) dialogue.StopSpeaking();
            }
        }

        // ------------------------------------------------------------------ old entry points

        private float _glintHoldUntil = -1f;

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
                _squad?.Raise(AlertPhase.Suspicious, source);
            }
        }

        /// <summary>Old noise entry (kept for callers): a noise somewhere, treated by the hearing rules.</summary>
        public void HearSound(Vector3 soundOrigin, float radius)
        {
            bool atPlayer = AITarget.Player != null && (soundOrigin - AITarget.Player.Position).sqrMagnitude < 9f;
            OnStimulus(new Stimulus
            {
                kind = atPlayer ? StimulusKind.Footstep : StimulusKind.RockLand,
                position = soundOrigin,
                radius = radius,
                source = atPlayer ? AITarget.Player : null,
                certainty = atPlayer ? 0.6f : 0f,
                time = Time.time
            });
        }

        private void HearSoundInternal(Vector3 soundOrigin, float radius, bool atPlayer)
        {
            if (IsDead || currentState == EnemyState.Combat) return;
            if (atPlayer) _playerEvidenceTime = Time.time;
            else if (Time.time - _playerEvidenceTime < distractionIgnoreSeconds) return;

            float myDist = (transform.position - soundOrigin).sqrMagnitude;
            int closer = 0;
            foreach (var other in Active)
            {
                if (other == null || other == this || other.IsDead || other.currentState == EnemyState.Combat) continue;
                float d = (other.transform.position - soundOrigin).sqrMagnitude;
                if (d > (radius + 2f) * (radius + 2f)) continue;
                if (d < myDist || (Mathf.Approximately(d, myDist) && Active.IndexOf(other) < Active.IndexOf(this))) closer++;
            }
            _investigateLookOnly = closer >= Mathf.Max(1, maxNoiseInvestigators);
            Vector3 point = soundOrigin;
            if (!_investigateLookOnly && closer > 0)
            {
                Vector3 away = transform.position - soundOrigin;
                away.y = 0f;
                if (away.sqrMagnitude < 0.01f) away = transform.forward;
                point = soundOrigin + Quaternion.AngleAxis(70f * closer, Vector3.up) * away.normalized * 2.5f;
            }
            _investigatePoint = point;
            _investigateTimer = investigateDuration;
            suspicionMeter = Mathf.Max(suspicionMeter, 50f);
            if (currentState == EnemyState.Sitting) SetState(EnemyState.Idle);
            SetState(EnemyState.Suspicious);
            _squad?.Raise(AlertPhase.Suspicious, soundOrigin);
        }

        // ------------------------------------------------------------------ head marker (24.1)

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
            var cam = Camera.main;
            if (cam != null) _stealthIndicator.transform.rotation = Quaternion.LookRotation(_stealthIndicator.transform.position - cam.transform.position);

            string text = "";
            Color color = Color.white;
            if (Surrendered) { text = "o"; color = Color.white; }
            else if (Actor.Current == EnemyAction.Heal) { text = "+"; color = new Color(0.4f, 1f, 0.4f); }
            else if (currentState == EnemyState.Combat)
            {
                bool busy = IsReloading || Actor.Drawing;
                color = busy ? new Color(1f, 0.6f, 0.2f) : Color.red;
                text = busy ? "..." : "!";
            }
            else if (currentState == EnemyState.Suspicious || currentState == EnemyState.Searching || suspicionMeter > 10f)
            {
                color = Color.yellow;
                text = "?";
            }
            else if (Nerve.Level >= NerveLevel.Tense) { color = new Color(1f, 1f, 1f, 0.6f); text = "..."; }
            if (IsLeader && text.Length > 0) text = "[" + text + "]";
            if (IsLeader && text.Length > 0 && !Surrendered && currentState != EnemyState.Combat) color = new Color(1f, 0.6f, 0.15f);
            _stealthIndicator.color = color;
            _stealthIndicator.text = text;

            // Second line: what he is doing, in words (user request 16 Nov)
            if (_statusLabel != null)
            {
                bool show = ShowStatusText && !IsDead;
                if (show && cam != null) show = (cam.transform.position - transform.position).sqrMagnitude < StatusTextRange * StatusTextRange;
                _statusLabel.gameObject.SetActive(show);
                if (show)
                {
                    if (cam != null) _statusLabel.transform.rotation = Quaternion.LookRotation(_statusLabel.transform.position - cam.transform.position);
                    _statusLabel.text = (IsLeader ? "[lider] " : "") + StatusText;
                    _statusLabel.color = text.Length > 0 ? new Color(color.r, color.g, color.b, 1f) : new Color(0.85f, 0.85f, 0.85f);
                }
            }
        }

        /// <summary>Panel "Düşman AI" > "Kafada durum yazısı".</summary>
        public static bool ShowStatusText = true;
        public static float StatusTextRange = 45f;
        private TextMesh _statusLabel;

        private void SetupStatusLabel()
        {
            var go = new GameObject("StatusLabel");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0f, eyeHeight + 0.8f, 0f); // above the ! / ? mark
            _statusLabel = go.AddComponent<TextMesh>();
            if (_statusLabel.font == null)
            {
                var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                if (font != null)
                {
                    _statusLabel.font = font;
                    go.GetComponent<MeshRenderer>().sharedMaterial = font.material;
                }
            }
            _statusLabel.alignment = TextAlignment.Center;
            _statusLabel.anchor = TextAnchor.MiddleCenter;
            _statusLabel.characterSize = 0.045f;
            _statusLabel.fontSize = 48;
            _statusLabel.text = "";
        }

        /// <summary>What he is doing right now, in plain Turkish (head label).</summary>
        public string StatusText
        {
            get
            {
                if (IsDead) return "";
                if (Surrendered) return "teslim oldu";
                // In a fight the brain may pick something else after he once fled: say what he does now
                if (Fleeing && (currentState != EnemyState.Combat || _brain == null || _brain.Choice == EnemyBrain.Option.Flee)) return "korktu, kaçıyor";
                if (Carried != null) return "ganimeti kaçırıyor";
                if (Actor.Current == EnemyAction.Heal) return "yarasını sarıyor";
                if (IsReloading) return "şarjör değiştiriyor";
                if (_squad != null && _squad.HoldFire && currentState == EnemyState.Combat) return "nişan almış, bekliyor";
                string nerve = Nerve.Level == NerveLevel.Panic ? " (panikte)" : Nerve.Level == NerveLevel.Pinned ? " (sinmiş)" : "";
                switch (currentState)
                {
                    case EnemyState.Combat: return CombatStatus() + nerve;
                    case EnemyState.Searching:
                        if (_brain != null && _brain.Choice == EnemyBrain.Option.Advance) return "sesin geldiği yere gidiyor";
                        if (_brain != null && _brain.Choice == EnemyBrain.Option.GuardSpot) return "nöbet tutuyor";
                        return "etrafı arıyor";
                    case EnemyState.Suspicious:
                        return suspicionMeter > 25f ? $"şüphelendi %{Mathf.RoundToInt(suspicionMeter)}" : "bakmaya gidiyor";
                    case EnemyState.Patrolling: return suspicionMeter > 5f ? $"devriye, şüphe %{Mathf.RoundToInt(suspicionMeter)}" : "devriye geziyor";
                    case EnemyState.Sitting: return Sleeping ? "uyuyor" : "oturuyor";
                    default:
                        if (suspicionMeter > 5f) return $"bir şey sezdi %{Mathf.RoundToInt(suspicionMeter)}";
                        if (InRoutine) return _squad != null && _squad.Phase != AlertPhase.Calm ? "tetikte, çevreyi kolluyor" : "dolaşıyor";
                        return _squad != null && _squad.Phase != AlertPhase.Calm ? "tetikte bekliyor" : "bekliyor";
                }
            }
        }

        private string CombatStatus()
        {
            if (_brain == null) return "çatışmada";
            if (Actor.Drawing) return "silahını çekiyor";
            var b = Belief.Primary;
            bool sees = b != null && b.SeenNow;
            switch (_brain.Choice)
            {
                case EnemyBrain.Option.OpenFire: return "sana ateş ediyor";
                case EnemyBrain.Option.PeekFire: return "siperden ateş ediyor";
                case EnemyBrain.Option.Suppress: return "baskı ateşi açıyor";
                case EnemyBrain.Option.Advance: return sees ? "üstüne geliyor" : "seni arıyor, izini sürüyor";
                case EnemyBrain.Option.MeleeCharge: return "saldırıyor";
                case EnemyBrain.Option.CarAssault: return "arabaya saldırıyor";
                case EnemyBrain.Option.MeleeApproach: return "fırsat kolluyor";
                case EnemyBrain.Option.Flank: return "yandan dolanıyor";
                case EnemyBrain.Option.ToCover: return "sipere koşuyor";
                case EnemyBrain.Option.Retreat: return _brain.WantsFire ? "ateş ederek geri çekiliyor" : "geri çekiliyor";
                case EnemyBrain.Option.HoldAngle: return "pusuda bekliyor";
                case EnemyBrain.Option.ThrowGrenade: return "bomba atıyor";
                case EnemyBrain.Option.ThrowRock: return "taş atıyor";
                case EnemyBrain.Option.Pickup: return "yerden silah alıyor";
                case EnemyBrain.Option.ShieldAdvance: return "kalkanla ilerliyor";
                case EnemyBrain.Option.ShieldBash: return "kalkanla vuruyor";
                case EnemyBrain.Option.GuardSpot: return "nöbette";
                case EnemyBrain.Option.Flee: return "korktu, kaçıyor";
                case EnemyBrain.Option.Wait: return sees ? "seni izliyor" : "bekliyor";
                default: return EnemyBrain.OptionText(_brain.Choice);
            }
        }
    }

    internal static class AIVectorExt
    {
        public static Vector3 Flat(this Vector3 v)
        {
            v.y = 0f;
            return v;
        }
    }
}
