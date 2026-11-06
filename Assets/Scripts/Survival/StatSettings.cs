using System;
using UnityEngine;

namespace EndlessSurvival.Survival
{
    public enum StatType
    {
        Marksmanship,
        Melee,
        Toughness,
        Endurance,
        Strength,
        Stealth,
        Nature,
        Mechanics,
        FirstAid,
        Driving
    }

    /// <summary>Temporary conditions that lower the effective stat values (4.4 table).</summary>
    public enum StatCondition
    {
        Hunger,
        Thirst,
        Sleepless,
        ToiletNeglect,
        Cold,
        Sick,
        Wounded,
        Overloaded,
        Panic,
        Anxiety
    }

    [Serializable]
    public class ConditionPenalty
    {
        public StatCondition condition;
        public StatType[] stats = new StatType[0];
        [Tooltip("Fraction of the base value lost at full severity")]
        [Range(0f, 1f)] public float maxDrop = 0.4f;
    }

    /// <summary>4.4: all character development numbers. Resources/StatSettings.asset, code defaults otherwise.</summary>
    [CreateAssetMenu(fileName = "StatSettings", menuName = "Endless Survival/Stat Settings")]
    public class StatSettings : ScriptableObject
    {
        private static StatSettings _instance;

        public static StatSettings Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = Resources.Load<StatSettings>("StatSettings");
                    if (_instance == null)
                    {
                        _instance = CreateInstance<StatSettings>();
                        _instance.hideFlags = HideFlags.DontSave;
                        _instance.penalties = DefaultPenalties();
                    }
                }
                return _instance;
            }
        }

        [Header("Growth")]
        public float startValue = 5f;
        public float maxValue = 100f;
        [Tooltip("Global growth speed")]
        public float growthMultiplier = 1f;
        [Tooltip("Diminishing returns power: gain * (1 - value/max)^power")]
        public float diminishingPower = 1.6f;
        [Tooltip("Gain multiplier when training under hard conditions (heavy load, low health, strong enemy)")]
        public float hardshipMultiplier = 2f;

        [Header("Gain per action")]
        public float marksmanshipPerHit = 0.6f;
        public float meleePerHit = 0.8f;
        public float strengthPerMeleeHit = 0.25f;
        public float toughnessPerDamage = 0.05f;
        public float endurancePerSprintSecond = 0.025f;
        public float strengthPerOverloadedSecond = 0.03f;
        public float stealthPerSneakSecond = 0.04f;
        public float naturePerGather = 0.5f;
        public float mechanicsPerCraft = 0.9f;
        public float firstAidPerUse = 1.5f;
        public float drivingPerSecond = 0.008f;

        [Header("Effects at 100 (scaled linearly by value / 100)")]
        public float marksmanshipSpreadAtZero = 1.2f;
        public float meleeDamageBonus = 0.6f;
        public float meleeSpeedBonus = 0.25f;
        public float strengthMeleeBonus = 0.25f;
        public float toughnessDamageReduction = 0.3f;
        public float toughnessBleedReduction = 0.5f;
        public float enduranceStaminaBonus = 0.8f;
        public float enduranceSprintSpeedBonus = 0.12f;
        public float enduranceDrainReduction = 0.3f;
        public float stealthNoiseReduction = 0.35f;
        public float stealthSuspicionReduction = 0.4f;
        public float naturePoisonReduction = 0.6f;
        public float natureExtraYieldChance = 0.6f;
        public float mechanicsCraftTimeReduction = 0.5f;
        public float mechanicsSaveMaterialChance = 0.3f;
        public float firstAidHealBonus = 0.8f;
        public float drivingGripBonus = 0.15f;

        [Header("Temporary drops")]
        [Tooltip("Effective value never goes below this fraction of the base value")]
        [Range(0f, 1f)] public float effectiveFloor = 0.4f;
        public ConditionPenalty[] penalties = new ConditionPenalty[0];

        public static ConditionPenalty[] DefaultPenalties()
        {
            ConditionPenalty P(StatCondition c, float drop, params StatType[] stats) =>
                new ConditionPenalty { condition = c, maxDrop = drop, stats = stats };

            return new[]
            {
                P(StatCondition.Hunger, 0.4f, StatType.Strength, StatType.Endurance, StatType.Melee),
                P(StatCondition.Thirst, 0.4f, StatType.Endurance, StatType.Marksmanship),
                P(StatCondition.Sleepless, 0.35f, StatType.Marksmanship, StatType.Stealth, StatType.Mechanics, StatType.Driving),
                P(StatCondition.ToiletNeglect, 0.2f, StatType.Stealth, StatType.Endurance),
                P(StatCondition.Cold, 0.3f, StatType.Marksmanship, StatType.Mechanics),
                P(StatCondition.Sick, 0.35f, StatType.Endurance, StatType.Strength, StatType.Stealth),
                P(StatCondition.Wounded, 0.35f, StatType.Strength, StatType.Endurance, StatType.Melee),
                P(StatCondition.Overloaded, 0.3f, StatType.Endurance, StatType.Stealth),
                P(StatCondition.Panic, 0.5f, StatType.Marksmanship),
                P(StatCondition.Anxiety, 0.3f, StatType.Marksmanship, StatType.Stealth, StatType.Mechanics)
            };
        }

        public static string StatName(StatType s)
        {
            switch (s)
            {
                case StatType.Marksmanship: return "Nişancılık";
                case StatType.Melee: return "Yakın Dövüş";
                case StatType.Toughness: return "Sağlamlık";
                case StatType.Endurance: return "Dayanıklılık";
                case StatType.Strength: return "Güç";
                case StatType.Stealth: return "Gizlilik";
                case StatType.Nature: return "Doğa";
                case StatType.Mechanics: return "Mekanik";
                case StatType.FirstAid: return "İlk Yardım";
                case StatType.Driving: return "Sürüş";
            }
            return s.ToString();
        }

        public static string ConditionName(StatCondition c)
        {
            switch (c)
            {
                case StatCondition.Hunger: return "Açlık";
                case StatCondition.Thirst: return "Susuzluk";
                case StatCondition.Sleepless: return "Uykusuzluk";
                case StatCondition.ToiletNeglect: return "Tuvalet";
                case StatCondition.Cold: return "Üşüme";
                case StatCondition.Sick: return "Hastalık";
                case StatCondition.Wounded: return "Ağır yara";
                case StatCondition.Overloaded: return "Aşırı yük";
                case StatCondition.Panic: return "Panik";
                case StatCondition.Anxiety: return "Anksiyete";
            }
            return c.ToString();
        }
    }
}
