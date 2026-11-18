using System;
using System.Collections.Generic;
using UnityEngine;

namespace EndlessCombat.Combat
{
    public enum CombatTeam { Player = 0, Enemy = 1, Neutral = 2 }

    public enum ActorState { Free, Attacking, Staggered, Dead }

    public enum HitResult { Ignored, Blocked, Hit }

    /// <summary>
    /// The small fighting body of the player and the enemies (Core_Feel_Plan stage 2, 2026-11-14; replaces the
    /// Souls-like CombatActor). It only knows:
    ///   team                 who may hit whom
    ///   stagger              short hit reaction with a push (the only state that stops walking)
    ///   shield               raised or not; raised stops hits / bullets from the front and wears the shield
    ///   damage filter        bullets and other damage through the owner's TakeDamage go through the shield
    ///   one quick strike     start, hit window, end. No combo, no heavy attack, no dodge, no guard break, no
    ///                        hyper armor, no poise. The owner keeps walking during the strike (at StrikeMoveScale)
    /// Who decides (gamepad or AI) is outside: PlayerMelee and EnemyController call TryAttack / SetGuard / Stagger.
    /// </summary>
    [DisallowMultipleComponent]
    public class CombatBody : MonoBehaviour
    {
        // ---- shared tuning (panel page "Karakter") ----
        /// <summary>Whole strike length per category (s): fists, knife, one hand, two hand.</summary>
        public static float StrikeTimeFists = 0.45f, StrikeTimeShort = 0.45f, StrikeTimeOneHand = 0.55f, StrikeTimeTwoHand = 0.6f;
        /// <summary>Walk speed multiplier while striking.</summary>
        public static float StrikeMoveScale = 0.5f;
        /// <summary>Tired (no stamina): the strike is this much slower, but it still happens.</summary>
        public static float TiredStrikeSlow = 0.65f;
        /// <summary>Walk speed multiplier with the shield raised (multiplied with the shield's own guardMoveSpeed).</summary>
        public static float ShieldMoveScale = 1f;
        /// <summary>Heavy strike (RT, Combat_Action_Plan.md 6): length, damage, stagger, push and stamina multipliers.</summary>
        public static float HeavyTime = 1.6f, HeavyDamage = 1.8f, HeavyStagger = 1.6f, HeavyKnockback = 1.5f, HeavyStamina = 2f;
        /// <summary>
        /// Stealth kill: the attacker must stand at more than this angle from the target's facing (100 = a 160 degree
        /// cone behind it; was 110 = 140 degrees until 2026-11-17, the user's back stabs from slightly aside failed).
        /// </summary>
        public static float StealthKillAngle = 100f;
        /// <summary>Stealth kill on a fighting enemy that has not seen the player for this many seconds (it lost you).</summary>
        public static float StealthKillLostSight = 2.5f;
        /// <summary>Raised on a stealth kill (attacker, victim): the place for a special kill animation later.</summary>
        public static event Action<CombatBody, CombatBody> StealthKilled;

        /// <summary>Would a knife strike from this attacker position kill this body at once (same test as ReceiveMelee)?</summary>
        public bool StealthKillPossibleFrom(Vector3 attackerPos)
        {
            if (State == ActorState.Dead || Unaware == null || !Unaware()) return false;
            Vector3 toAttacker = attackerPos - transform.position;
            toAttacker.y = 0f;
            return toAttacker.sqrMagnitude > 0.0001f && Vector3.Angle(transform.forward, toAttacker) > StealthKillAngle;
        }
        private static bool _tuningRegistered;

        public static void RegisterTuning()
        {
            if (_tuningRegistered) return;
            _tuningRegistered = true;
            const string P = "Karakter";
            Tuning.TuningPanel.Register(P, "Vuruş süresi: yumruk", () => StrikeTimeFists, v => StrikeTimeFists = v, 0.2f, 1.5f, 0.05f);
            Tuning.TuningPanel.Register(P, "Vuruş süresi: bıçak", () => StrikeTimeShort, v => StrikeTimeShort = v, 0.2f, 1.5f, 0.05f);
            Tuning.TuningPanel.Register(P, "Vuruş süresi: tek el", () => StrikeTimeOneHand, v => StrikeTimeOneHand = v, 0.2f, 1.5f, 0.05f);
            Tuning.TuningPanel.Register(P, "Vuruş süresi: çift el", () => StrikeTimeTwoHand, v => StrikeTimeTwoHand = v, 0.2f, 1.5f, 0.05f);
            Tuning.TuningPanel.Register(P, "Vururken yürüme çarpanı", () => StrikeMoveScale, v => StrikeMoveScale = v, 0f, 1f, 0.05f);
            Tuning.TuningPanel.Register(P, "Kalkan kalkıkken hız çarpanı", () => ShieldMoveScale, v => ShieldMoveScale = v, 0.1f, 2f, 0.05f);
            Tuning.TuningPanel.Register(P, "Ağır vuruş: süre çarpanı", () => HeavyTime, v => HeavyTime = v, 1f, 3f, 0.05f);
            Tuning.TuningPanel.Register(P, "Ağır vuruş: hasar çarpanı", () => HeavyDamage, v => HeavyDamage = v, 1f, 4f, 0.05f);
            Tuning.TuningPanel.Register(P, "Ağır vuruş: sendeletme çarpanı", () => HeavyStagger, v => HeavyStagger = v, 1f, 4f, 0.05f);
            Tuning.TuningPanel.Register(P, "Ağır vuruş: kondisyon çarpanı", () => HeavyStamina, v => HeavyStamina = v, 1f, 4f, 0.05f);
            Tuning.TuningPanel.Register(P, "Sessiz öldürme: arka açı (büyük = dar)", () => StealthKillAngle, v => StealthKillAngle = v, 60f, 150f, 5f);
            Tuning.TuningPanel.Register(P, "Sessiz öldürme: seni kaybettiyse (sn)", () => StealthKillLostSight, v => StealthKillLostSight = v, 0.5f, 10f, 0.5f);
        }

        [Header("Identity")]
        public CombatTeam team = CombatTeam.Enemy;

        [Tooltip("Strike speed multiplier (stats)")]
        public float attackSpeed = 1f;

        /// <summary>Player stamina: spend, false when too tired (the strike is then slower, never refused).</summary>
        public Func<float, bool> ExternalSpend;
        /// <summary>Facing at the start of a strike (player: move input or camera; enemy: the target). Zero = keep.</summary>
        public Func<Vector3> AimDirection;
        /// <summary>Enemy only: true while it has not noticed the player (a knife from behind kills at once).</summary>
        public Func<bool> Unaware;
        /// <summary>Hit count of a finished strike (noise, stat training).</summary>
        public event Action<int> SwingFinished;
        /// <summary>Something hit us (result, attacker): the AI reacts.</summary>
        public event Action<HitResult, CombatBody> HitReceived;

        public ActorState State { get; private set; } = ActorState.Free;
        public int AttackSerial { get; private set; }
        public MeleeCategory CurrentCategory { get; private set; }
        /// <summary>The current strike is a gun butt push (firearm in the hand).</summary>
        public bool CurrentIsButt { get; private set; }
        /// <summary>The strike in progress is the heavy one (RT).</summary>
        public bool CurrentIsHeavy { get; private set; }

        private MeleeAnimationSet _set;
        private CharacterController _cc;
        private CombatBodyAnimator _body;
        private Character.PlayerMotor _motor;
        private ShieldHolder _shield;
        private IDamageable _damageable;

        private float _stateTime, _duration;
        private float _hitStart, _hitEnd;
        private MeleeStats _stats;
        private readonly HashSet<object> _hitSet = new HashSet<object>();
        private int _swingHits;
        private bool _swingReported;
        private Vector3 _push;
        private bool _guardWanted;
        private bool _resolving;
        private bool _initialized;

        public float NormalizedTime => _duration > 0f ? _stateTime / _duration : 1f;
        /// <summary>Striking or staggered: other actions (interact, get in the vehicle, smoke) wait.</summary>
        public bool IsLocked => State == ActorState.Attacking || State == ActorState.Staggered;
        public bool IsStaggered => State == ActorState.Staggered;
        public bool IsDead => State == ActorState.Dead;
        public bool InHitWindow => State == ActorState.Attacking && NormalizedTime >= _hitStart && NormalizedTime <= _hitEnd;
        public bool CanGuard => _shield != null && _shield.Usable;
        public bool IsGuarding => _guardWanted && CanGuard && State != ActorState.Dead && State != ActorState.Staggered;
        public bool UsingShield => _shield != null && _shield.Usable;
        public ShieldStats GuardStats => UsingShield ? _shield.Stats : default;

        /// <summary>Walk speed multiplier for the owner (raised shield is slow, a strike halves the speed).</summary>
        public float MoveSpeedMultiplier
        {
            get
            {
                float m = 1f;
                if (IsGuarding) m *= Mathf.Clamp(GuardStats.guardMoveSpeed * ShieldMoveScale, 0.1f, 1f);
                if (State == ActorState.Attacking) m *= Mathf.Clamp(StrikeMoveScale, 0.05f, 1f);
                return m;
            }
        }

        public static CombatBody Player { get; private set; }
        public static bool PlayerLocked => Player != null && Player.isActiveAndEnabled && Player.IsLocked;

        public static CombatBody GetOrAdd(GameObject owner, CombatTeam team)
        {
            var a = owner.GetComponent<CombatBody>();
            if (a == null)
            {
                a = owner.AddComponent<CombatBody>();
                a.team = team;
            }
            a.Init();
            return a;
        }

        private void Awake() => Init();

        private void Init()
        {
            if (_initialized) return;
            _initialized = true;
            RegisterTuning();
            _set = MeleeAnimationSet.Instance;
            _cc = GetComponent<CharacterController>();
            _motor = GetComponent<Character.PlayerMotor>();
            _shield = GetComponent<ShieldHolder>();
            FindDamageable();
            var animator = GetComponent<Animator>();
            if (animator == null) animator = GetComponentInChildren<Animator>();
            if (animator != null && _cc != null) animator.applyRootMotion = false;
            _body = CombatBodyAnimator.GetOrAdd(animator);
            if (team == CombatTeam.Player) Player = this;
            CharacterBodyColliders.GetOrAdd(gameObject);
        }

        private void OnEnable()
        {
            if (team == CombatTeam.Player) Player = this;
        }

        private void OnDisable()
        {
            if (State != ActorState.Dead) EnterFree();
            _guardWanted = false;
            _body?.ResetAll();
            if (_motor != null)
            {
                _motor.ActionLocked = false;
                _motor.MoveSpeedMultiplier = 1f;
            }
        }

        // ============================================================ owner API

        public void RefreshShield() => _shield = GetComponent<ShieldHolder>();

        /// <summary>Shield button state (LT). Only does something with a usable shield on the arm.</summary>
        public void SetGuard(bool wanted) => _guardWanted = wanted;

        /// <summary>
        /// One quick strike with the given (already multiplied) stats. butt = gun butt push (firearm in the hand):
        /// the fists clip, the stats come from the caller. False while striking / staggered.
        /// </summary>
        public bool TryAttack(MeleeStats stats, MeleeCategory category, bool butt = false, bool heavy = false)
        {
            if (State != ActorState.Free) return false;
            if (category == MeleeCategory.Auto) category = MeleeCategory.Fists;
            bool rested = ExternalSpend == null || ExternalSpend(stats.staminaCost);

            CategorySlots cat = _set.Category(butt ? MeleeCategory.Fists : category);
            // Heavy: the category's heavy clip when it has one (the animator still has the old Heavy states), else the light one slower
            bool heavyClip = heavy && !butt && cat.heavy != null && cat.heavy.clip != null;
            AttackSlot slot = heavyClip ? cat.heavy : cat.light1;
            if (heavy)
            {
                stats.damage *= HeavyDamage;
                stats.staggerTime *= HeavyStagger;
                stats.knockback *= HeavyKnockback;
            }
            _stats = stats;
            CurrentCategory = category;
            CurrentIsButt = butt;
            CurrentIsHeavy = heavy;
            AttackSerial++;

            Vector3 aim = AimDirection != null ? AimDirection() : Vector3.zero;
            aim.y = 0f;
            if (aim.sqrMagnitude > 0.001f) transform.rotation = Quaternion.LookRotation(aim.normalized);

            float length = StrikeTime(butt ? MeleeCategory.Fists : category) / Mathf.Max(0.1f, attackSpeed);
            if (heavy) length *= Mathf.Max(1f, HeavyTime);
            if (!rested) length /= Mathf.Clamp(TiredStrikeSlow, 0.2f, 1f);
            _hitStart = slot != null ? slot.hitStart : 0.35f;
            _hitEnd = slot != null ? slot.hitEnd : 0.55f;
            BeginState(ActorState.Attacking, length);
            _hitSet.Clear();
            _swingHits = 0;
            _swingReported = false;

            float clipLen = slot != null && slot.clip != null ? slot.clip.length : length;
            float speed = Mathf.Clamp(clipLen / length, 0.3f, 4f);
            MeleeCategory stateCat = butt ? MeleeCategory.Fists : category;
            _body?.PlayFullBody(MeleeAnimationSet.AttackState(stateCat, heavyClip ? MeleeAnimationSet.MoveHeavy : MeleeAnimationSet.MoveLight1), speed);
            EndlessCombat.Audio.GameSound.Play(EndlessCombat.Audio.SoundId.MeleeSwing, transform.position, heavy ? 1f : 0.7f,
                butt ? "butt" : category.ToString(), team == CombatTeam.Player);
            return true;
        }

        public static float StrikeTime(MeleeCategory c)
        {
            switch (c)
            {
                case MeleeCategory.Short: return StrikeTimeShort;
                case MeleeCategory.OneHand: return StrikeTimeOneHand;
                case MeleeCategory.TwoHand: return StrikeTimeTwoHand;
                default: return StrikeTimeFists;
            }
        }

        /// <summary>Short hit reaction (can't walk), pushed along push (m/s).</summary>
        public void Stagger(float duration, Vector3 push)
        {
            if (State == ActorState.Dead || duration <= 0.02f) return;
            ReportSwing();
            _push = push;
            _push.y = 0f;
            BeginState(ActorState.Staggered, Mathf.Max(0.15f, duration));
            var slot = _set.hitLight;
            if (slot != null && slot.clip != null)
                _body?.PlayFullBody(MeleeAnimationSet.StateHitLight, Mathf.Clamp(slot.clip.length * slot.recoverAt / _duration, 0.4f, 2.5f), 0.06f);
            else _body?.StopFullBody();
        }

        /// <summary>Old signature (heavy flag is ignored).</summary>
        public void Stagger(float duration, Vector3 push, bool heavy) => Stagger(duration, push);

        public void Kill()
        {
            State = ActorState.Dead;
            _guardWanted = false;
            _body?.ResetAll();
            if (_motor != null)
            {
                _motor.ActionLocked = false;
                _motor.MoveSpeedMultiplier = 1f;
            }
        }

        public void Revive() => State = ActorState.Free;

        // ============================================================ damage in

        /// <summary>
        /// A melee hit. The raised shield stops it from the front (the shield wears). A knife from behind into an
        /// unaware enemy kills at once (stealth kill).
        /// </summary>
        public HitResult ReceiveMelee(CombatBody attacker, Vector3 attackerPos, MeleeStats stats, float staggerTime, Vector3 point, Vector3 attackDir)
        {
            if (State == ActorState.Dead) return HitResult.Ignored;
            attackDir.y = 0f;
            if (attackDir.sqrMagnitude < 0.0001f) attackDir = -transform.forward;
            attackDir.Normalize();
            float damage = stats.damage;

            // Stealth kill: knife, from behind, the target has not noticed anyone
            if (attacker != null && attacker.team == CombatTeam.Player && !attacker.CurrentIsButt
                && attacker.CurrentCategory == MeleeCategory.Short && Unaware != null && Unaware())
            {
                Vector3 toAttacker = attackerPos - transform.position;
                toAttacker.y = 0f;
                if (toAttacker.sqrMagnitude > 0.0001f && Vector3.Angle(transform.forward, toAttacker) > StealthKillAngle)
                {
                    ApplyDamage(MeleeInfo(100000f, attacker, point, attackDir));
                    StealthKilled?.Invoke(attacker, this); // a special animation can hook here later
                    HitReceived?.Invoke(HitResult.Hit, attacker);
                    return HitResult.Hit;
                }
            }

            if (IsGuarding && FromFront(attackerPos - transform.position, GuardStats.coverAngle))
            {
                float blocked = damage * Mathf.Clamp01(GuardStats.absorb);
                float through = damage - blocked;
                _shield.Wear(blocked);
                if (through > 0.01f) ApplyDamage(MeleeInfo(through, attacker, point, attackDir));
                EndlessCombat.Audio.GameSound.Play(EndlessCombat.Audio.SoundId.ShieldBlock, point, 1f, "melee", team == CombatTeam.Player);
                HitReceived?.Invoke(HitResult.Blocked, attacker);
                return HitResult.Blocked;
            }

            ApplyDamage(MeleeInfo(damage, attacker, point, attackDir));
            EndlessCombat.Audio.GameSound.Play(EndlessCombat.Audio.SoundId.MeleeHit, point, 1f,
                attacker != null ? attacker.CurrentCategory.ToString() : null, attacker != null && attacker.team == CombatTeam.Player);
            bool dead = State == ActorState.Dead || (_damageable != null && _damageable.IsDead);
            if (!dead) Stagger(staggerTime, attackDir * stats.knockback);
            HitReceived?.Invoke(HitResult.Hit, attacker);
            return HitResult.Hit;
        }

        /// <summary>
        /// Non melee damage (bullets, animals, explosions) going through the owner's TakeDamage: the raised shield
        /// stops it from the front and wears. Returns the damage that gets through.
        /// </summary>
        public float FilterDamage(DamageInfo info)
        {
            float damage = info.Amount;
            if (_resolving || State == ActorState.Dead || damage <= 0f) return damage;
            if (!IsGuarding) return damage;

            Vector3 toSource = info.TowardSource;
            toSource.y = 0f;
            if (toSource.sqrMagnitude < 0.0001f || !FromFront(toSource, GuardStats.coverAngle)) return damage;

            var g = GuardStats;
            float absorb = info.Type == DamageType.Bullet ? g.bulletAbsorb : g.absorb;
            float blocked = damage * Mathf.Clamp01(absorb);
            if (blocked <= 0f) return damage;
            _shield.Wear(blocked);
            EndlessCombat.Audio.GameSound.Play(EndlessCombat.Audio.SoundId.ShieldBlock, info.Point, 1f,
                info.Type == DamageType.Bullet ? "bullet" : "other", team == CombatTeam.Player);
            return damage - blocked;
        }

        private static DamageInfo MeleeInfo(float amount, CombatBody attacker, Vector3 point, Vector3 attackDir)
        {
            return new DamageInfo(amount, DamageType.Melee, point, -attackDir)
            {
                SourceDirection = -attackDir,
                Source = attacker != null ? attacker.gameObject : null,
                ByPlayer = attacker != null && attacker.team == CombatTeam.Player
            };
        }

        /// <summary>Melee damage already resolved here (shield, stealth kill): the owner's TakeDamage does not filter it again.</summary>
        private void ApplyDamage(DamageInfo info)
        {
            if (_damageable == null) FindDamageable();
            if (_damageable == null || _damageable.IsDead) return;
            _resolving = true;
            try { Damage.Deal(_damageable, info); }
            finally { _resolving = false; }
        }

        private void FindDamageable()
        {
            foreach (var mb in GetComponents<MonoBehaviour>())
                if (mb is IDamageable d) { _damageable = d; return; }
            foreach (var mb in GetComponentsInChildren<MonoBehaviour>())
                if (mb is IDamageable d) { _damageable = d; return; }
        }

        private bool FromFront(Vector3 toSource, float halfAngle)
        {
            toSource.y = 0f;
            if (toSource.sqrMagnitude < 0.0001f) return true;
            return Vector3.Angle(transform.forward, toSource) <= halfAngle;
        }

        // ============================================================ state

        private void BeginState(ActorState state, float duration)
        {
            State = state;
            _stateTime = 0f;
            _duration = Mathf.Max(0.05f, duration);
        }

        private void EnterFree()
        {
            ReportSwing();
            State = ActorState.Free;
            _body?.StopFullBody();
        }

        private void ReportSwing()
        {
            if (State != ActorState.Attacking || _swingReported) return;
            _swingReported = true;
            SwingFinished?.Invoke(_swingHits);
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            if (State == ActorState.Attacking || State == ActorState.Staggered)
            {
                _stateTime += dt;
                if (State == ActorState.Attacking) UpdateAttack();
                else UpdateStagger(dt);
                if (State != ActorState.Free && State != ActorState.Dead && NormalizedTime >= 1f) EnterFree();
            }

            UpdatePoses();

            if (_motor != null)
            {
                _motor.ActionLocked = State == ActorState.Staggered;
                _motor.MoveSpeedMultiplier = MoveSpeedMultiplier;
            }
        }

        private void UpdateAttack()
        {
            float t = NormalizedTime;
            if (t >= _hitStart && t <= _hitEnd)
                _swingHits += MeleeHit.Strike(this, transform.forward, _stats, _stats.staggerTime, _hitSet);
            else if (t > _hitEnd) ReportSwing();
        }

        private void UpdateStagger(float dt)
        {
            if (_cc != null && _cc.enabled && _push.sqrMagnitude > 0.0001f)
            {
                _cc.Move(_push * dt);
                _push = Vector3.MoveTowards(_push, Vector3.zero, 9f * dt);
            }
        }

        private void UpdatePoses()
        {
            if (_body == null) return;
            _body.SetWeaponGuard(false);
            if (State == ActorState.Dead || !UsingShield)
            {
                _body.SetShieldMode(0);
                if (_shield != null) _shield.Raised = false;
                return;
            }
            bool guarding = IsGuarding;
            _body.SetShieldMode(guarding ? 2 : 1);
            _shield.Raised = guarding;
        }
    }
}
