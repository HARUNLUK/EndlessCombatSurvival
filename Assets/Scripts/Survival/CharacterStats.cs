using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using EndlessCombat.Combat;
using EndlessSurvival.Inventory;

namespace EndlessSurvival.Survival
{
    /// <summary>
    /// 4.4: ten stats that grow with use (Kenshi style). Base value only goes up; the effective value drops
    /// temporarily under bad conditions (reported by PlayerVitals) and never below a floor of the base.
    /// Also installs the combat / inventory hooks so every system reads the effective values.
    /// </summary>
    public class CharacterStats : MonoBehaviour, ISurvivalSave
    {
        public static CharacterStats Instance { get; private set; }

        private const int StatCount = 10;
        private const int ConditionCount = 10;

        private readonly float[] _base = new float[StatCount];
        private readonly float[] _effective = new float[StatCount];
        private readonly float[] _severity = new float[ConditionCount];
        private readonly List<StatCondition>[] _reasons = new List<StatCondition>[StatCount];

        private PlayerHealth _health;
        private float _lastShownSecond;

        public string SaveKey => "stats";

        private void Awake()
        {
            Instance = this;
            var s = StatSettings.Instance;
            for (int i = 0; i < StatCount; i++)
            {
                _base[i] = s.startValue;
                _effective[i] = s.startValue;
                _reasons[i] = new List<StatCondition>();
            }
        }

        private void OnEnable()
        {
            InstallHooks();
            CombatHooks.PlayerFirearmHit += OnFirearmHit;
            CombatHooks.PlayerMeleeLanded += OnMeleeLanded;
            PlayerInventory.ItemConsumed += OnItemConsumed;
            SurvivalDirector.Instance?.Register(this);
        }

        private void OnDisable()
        {
            CombatHooks.PlayerFirearmHit -= OnFirearmHit;
            CombatHooks.PlayerMeleeLanded -= OnMeleeLanded;
            PlayerInventory.ItemConsumed -= OnItemConsumed;
            if (_health != null) _health.Damaged -= OnDamaged;
            UninstallHooks();
            SurvivalDirector.Instance?.Unregister(this);
            if (Instance == this) Instance = null;
        }

        private void Start()
        {
            var player = SurvivalDirector.Player;
            _health = player != null ? player.GetComponent<PlayerHealth>() : null;
            if (_health != null) _health.Damaged += OnDamaged;
        }

        // ---------------- values ----------------

        public float Base(StatType s) => _base[(int)s];
        public float Effective(StatType s) => _effective[(int)s];
        /// <summary>Effective value as 0..1.</summary>
        public float F(StatType s) => Mathf.Clamp01(_effective[(int)s] / Mathf.Max(1f, StatSettings.Instance.maxValue));
        public IReadOnlyList<StatCondition> Reasons(StatType s) => _reasons[(int)s];
        public float Severity(StatCondition c) => _severity[(int)c];

        /// <summary>Static helper usable when the stats are not set up yet (returns 0..1, start value otherwise).</summary>
        public static float Frac(StatType s)
        {
            if (Instance != null) return Instance.F(s);
            var set = StatSettings.Instance;
            return Mathf.Clamp01(set.startValue / Mathf.Max(1f, set.maxValue));
        }

        public void SetCondition(StatCondition c, float severity) => _severity[(int)c] = Mathf.Clamp01(severity);

        /// <summary>Grows a stat. hard = trained under hard conditions (faster).</summary>
        public void Train(StatType s, float amount, bool hard = false)
        {
            if (amount <= 0f) return;
            var set = StatSettings.Instance;
            int i = (int)s;
            float before = _base[i];
            float room = Mathf.Clamp01(1f - before / set.maxValue);
            float gain = amount * set.growthMultiplier * Mathf.Pow(room, set.diminishingPower) * (hard ? set.hardshipMultiplier : 1f);
            _base[i] = Mathf.Min(set.maxValue, before + gain);
            int a = Mathf.FloorToInt(before), b = Mathf.FloorToInt(_base[i]);
            if (b > a) SurvivalNotify.Show($"{StatSettings.StatName(s)} {a} -> {b}", new Color(0.6f, 1f, 0.6f), 3.5f);
        }

        private void Update()
        {
            var set = StatSettings.Instance;
            for (int i = 0; i < StatCount; i++) _reasons[i].Clear();

            var drops = new float[StatCount];
            foreach (var p in set.penalties)
            {
                if (p == null || p.stats == null) continue;
                float sev = _severity[(int)p.condition];
                if (sev <= 0.01f) continue;
                foreach (var st in p.stats)
                {
                    drops[(int)st] += p.maxDrop * sev;
                    _reasons[(int)st].Add(p.condition);
                }
            }

            for (int i = 0; i < StatCount; i++)
            {
                float target = _base[i] * Mathf.Max(set.effectiveFloor, 1f - drops[i]);
                // Recovers within a few seconds when the condition goes away, drops right away
                _effective[i] = target < _effective[i] ? target : Mathf.MoveTowards(_effective[i], target, Time.deltaTime * Mathf.Max(1f, _base[i] * 0.5f));
            }

            // Driving training
            var vehicle = SurvivalDirector.Vehicle;
            if (vehicle != null && vehicle.isDriven && vehicle.CurrentSpeedKmh > 12f)
            {
                var chunk = SurvivalDirector.ChunkAt(vehicle.transform.position);
                bool offroad = chunk != null && OffRoad(chunk, vehicle.transform.position);
                Train(StatType.Driving, set.drivingPerSecond * Time.deltaTime, offroad);
            }
        }

        private static bool OffRoad(EndlessSurvival.World.Chunk chunk, Vector3 pos)
        {
            var spline = chunk.GetComponentInChildren<EndlessSurvival.World.Road.RoadSpline>();
            if (spline == null) return false;
            Vector3 local = spline.transform.InverseTransformPoint(pos);
            Vector3 road = spline.GetPointAtZ(local.z);
            return Mathf.Abs(local.x - road.x) > 6f;
        }

        // ---------------- training sources ----------------

        private void OnFirearmHit(IDamageable target, float damage)
        {
            bool hard = target is EndlessCombat.AI.EnemyController e && e.maxHealth > 130f;
            Train(StatType.Marksmanship, StatSettings.Instance.marksmanshipPerHit, hard || LowHealth());
        }

        private void OnMeleeLanded(int hits)
        {
            if (hits <= 0) return;
            var set = StatSettings.Instance;
            Train(StatType.Melee, set.meleePerHit * Mathf.Min(hits, 3), LowHealth());
            Train(StatType.Strength, set.strengthPerMeleeHit * Mathf.Min(hits, 3));
        }

        private void OnDamaged(float damage)
        {
            Train(StatType.Toughness, damage * StatSettings.Instance.toughnessPerDamage, LowHealth());
        }

        private void OnItemConsumed(ItemDefinition item, PlayerInventory inv)
        {
            if (item != null && item.category == ItemCategory.Consumable)
                Train(StatType.FirstAid, StatSettings.Instance.firstAidPerUse, _health != null && _health.isBleeding);
        }

        private bool LowHealth() => _health != null && _health.CurrentHealth < _health.maxHealth * 0.35f;

        // ---------------- hooks ----------------

        private void InstallHooks()
        {
            var set = StatSettings.Instance;

            CombatHooks.PlayerSpreadDegrees = () =>
            {
                float spread = set.marksmanshipSpreadAtZero * Mathf.Clamp01(1f - F(StatType.Marksmanship) / 0.6f);
                var v = PlayerVitals.Instance;
                if (v != null) spread += v.AimSwayDegrees;
                return spread;
            };
            CombatHooks.PlayerMeleeDamageMultiplier = () =>
                (1f + set.meleeDamageBonus * F(StatType.Melee) + set.strengthMeleeBonus * F(StatType.Strength)) *
                (PlayerVitals.Instance != null ? PlayerVitals.Instance.MeleeWeakness : 1f);
            CombatHooks.PlayerMeleeIntervalMultiplier = () => 1f - set.meleeSpeedBonus * F(StatType.Melee);
            CombatHooks.TrySpendStamina = cost => PlayerVitals.Instance == null || PlayerVitals.Instance.TrySpendStamina(cost);
            CombatHooks.EnemySuspicionMultiplier = () => 1f - set.stealthSuspicionReduction * F(StatType.Stealth);
            CombatHooks.PlayerNoiseMultiplier = () =>
            {
                float clothing = PlayerVitals.Instance != null ? PlayerVitals.Instance.ClothingNoise : 0f;
                return (1f - set.stealthNoiseReduction * F(StatType.Stealth)) * (1f + clothing);
            };
            PlayerHealth.DamageTakenMultiplier = () => (1f - set.toughnessDamageReduction * F(StatType.Toughness)) * (PlayerVitals.Instance != null ? PlayerVitals.Instance.PainMultiplier : 1f);
            PlayerHealth.BleedChanceMultiplier = () => 1f - set.toughnessBleedReduction * F(StatType.Toughness);
            PlayerInventory.HealMultiplier = () => 1f + set.firstAidHealBonus * F(StatType.FirstAid);
            ItemDefinition.IdentifyCheck = item => item != null && Effective(StatType.Nature) >= item.natureToIdentify;
        }

        private static void UninstallHooks()
        {
            CombatHooks.PlayerSpreadDegrees = () => 0f;
            CombatHooks.PlayerMeleeDamageMultiplier = () => 1f;
            CombatHooks.PlayerMeleeIntervalMultiplier = () => 1f;
            CombatHooks.TrySpendStamina = cost => true;
            CombatHooks.EnemySuspicionMultiplier = () => 1f;
            CombatHooks.PlayerNoiseMultiplier = () => 1f;
            PlayerHealth.DamageTakenMultiplier = () => 1f;
            PlayerHealth.BleedChanceMultiplier = () => 1f;
            PlayerInventory.HealMultiplier = () => 1f;
            ItemDefinition.IdentifyCheck = null;
        }

        // ---------------- UI text ----------------

        /// <summary>"Güç 6 / 10 (Açlık)" lines for the panel.</summary>
        public string Describe(StatType s)
        {
            int i = (int)s;
            var sb = new StringBuilder();
            sb.Append(StatSettings.StatName(s)).Append("  ");
            sb.Append(Mathf.FloorToInt(_effective[i])).Append(" / ").Append(Mathf.FloorToInt(_base[i]));
            if (_reasons[i].Count > 0)
            {
                sb.Append("  (");
                for (int r = 0; r < _reasons[i].Count; r++)
                {
                    if (r > 0) sb.Append(", ");
                    sb.Append(StatSettings.ConditionName(_reasons[i][r]));
                }
                sb.Append(")");
            }
            return sb.ToString();
        }

        // ---------------- save ----------------

        [Serializable]
        private class Data
        {
            public float[] values;
        }

        public string CaptureJson() => JsonUtility.ToJson(new Data { values = (float[])_base.Clone() });

        public void RestoreJson(string json)
        {
            var d = JsonUtility.FromJson<Data>(json);
            if (d?.values == null) return;
            for (int i = 0; i < StatCount && i < d.values.Length; i++)
            {
                _base[i] = d.values[i];
                _effective[i] = d.values[i];
            }
        }
    }
}
