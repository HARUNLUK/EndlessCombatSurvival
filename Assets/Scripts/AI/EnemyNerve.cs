using UnityEngine;

namespace EndlessCombat.AI
{
    public enum NerveLevel { Calm, Tense, Pinned, Panic }

    /// <summary>
    /// Morale, suppression, panic and discipline as one number (Enemy_AI_Deep_Plan.md 10): 0 calm, 100 broken.
    /// Personality (courage, discipline, aggression) is rolled once per enemy.
    /// </summary>
    public class EnemyNerve
    {
        // ---- tuning (panel page "Düşman") ----
        public static float NearMissGain = 8f, HitGain = 15f, FriendDeathGain = 20f, LeaderDeathGain = 30f, ExplosionGain = 25f;
        public static float FlankedGain = 15f, AloneGainPerSec = 3f, DarkGainPerSec = 0.5f, PredatorGain = 10f;
        public static float HitPlayerRelief = 10f, PlayerReloadRelief = 5f, CalmDownPerSec = 4f;
        public static float TenseBase = 40f, PinnedBase = 70f, PanicBase = 90f;

        private readonly EnemyController _owner;
        public float courage, discipline, aggression;
        public float Value { get; private set; }
        private float _lastRise = -999f;
        public bool PanicChosen { get; private set; }
        public enum PanicKind { None, Flee, Charge, Freeze }
        public PanicKind Panic { get; private set; }
        private float _freezeUntil;

        public EnemyNerve(EnemyController owner)
        {
            _owner = owner;
            courage = Random.value;
            discipline = Random.value;
            aggression = Random.value;
        }

        private float Shift => (courage - 0.5f) * 30f + AIDifficulty.For(_owner.Squad).nerveShift;
        public float TenseAt => TenseBase + Shift;
        public float PinnedAt => PinnedBase + Shift;
        public float PanicAt => PanicBase + Shift;

        public NerveLevel Level
        {
            get
            {
                if (Value >= PanicAt) return NerveLevel.Panic;
                if (Value >= PinnedAt) return NerveLevel.Pinned;
                if (Value >= TenseAt) return NerveLevel.Tense;
                return NerveLevel.Calm;
            }
        }

        public bool Frozen => Time.time < _freezeUntil;

        public void Add(float amount)
        {
            if (amount <= 0f)
            {
                Value = Mathf.Max(0f, Value + amount);
                return;
            }
            if (ArmedVsUnarmed) amount *= 0.4f;
            Value = Mathf.Min(100f, Value + amount);
            _lastRise = Time.time;
        }

        public void Set(float v)
        {
            Value = Mathf.Clamp(v, 0f, 100f);
            if (Value < PanicAt) { PanicChosen = false; Panic = PanicKind.None; }
        }

        public void NearMiss() => Add(NearMissGain);

        public void Tick(float dt, bool inCover, bool leaderNear, int friendsFighting, bool friendWithin15, bool darkAlone)
        {
            if (!friendWithin15 && !ArmedVsUnarmed) Add(AloneGainPerSec * dt);
            if (darkAlone) Add(DarkGainPerSec * dt);
            if (Time.time - _lastRise > 2f)
            {
                float down = CalmDownPerSec * (inCover ? 2f : 1f) * (leaderNear ? 1.5f : 1f);
                if (friendsFighting >= 3) down += 2f;
                Value = Mathf.Max(0f, Value - down * dt);
            }

            if (Level == NerveLevel.Panic && !PanicChosen)
            {
                PanicChosen = true;
                if (courage < 0.4f) Panic = PanicKind.Flee;
                else if (aggression > 0.8f) Panic = PanicKind.Charge;
                else
                {
                    Panic = PanicKind.Freeze;
                    _freezeUntil = Time.time + Random.Range(2f, 4f);
                }
            }
            else if (Level != NerveLevel.Panic && PanicChosen && Value < PinnedAt)
            {
                PanicChosen = false;
                Panic = PanicKind.None;
            }
            // After the freeze: the timid run, the others pull themselves together and fight on (before, every
            // frozen man ran: too many fled, user bug report 16 Nov)
            if (Panic == PanicKind.Freeze && !Frozen)
            {
                if (courage < 0.4f) Panic = PanicKind.Flee;
                else Set(PinnedAt - 5f);
            }
        }

        /// <summary>An armed man facing a player with no gun (bare hands / melee) is much harder to scare.</summary>
        private bool ArmedVsUnarmed
        {
            get
            {
                if (_owner == null || !_owner.IsGunner || !_owner.HasAnyAmmo) return false;
                var h = PlayerRead.Current.hand;
                return h == PlayerHand.None || h == PlayerHand.Melee || h == PlayerHand.Throwable;
            }
        }

        /// <summary>Spread multiplier from the nerve (Tense 1.3, Pinned 1.8).</summary>
        public float SpreadMul => Level >= NerveLevel.Pinned ? 1.8f : Level == NerveLevel.Tense ? 1.3f : 1f;
        public float ReactionMul => Level >= NerveLevel.Tense ? 0.85f : 1f;

        public string LevelText
        {
            get
            {
                switch (Level)
                {
                    case NerveLevel.Tense: return "gergin";
                    case NerveLevel.Pinned: return "sinmiş";
                    case NerveLevel.Panic: return "panik";
                    default: return "sakin";
                }
            }
        }
    }
}
