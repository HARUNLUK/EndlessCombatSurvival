using UnityEngine;
using EndlessSurvival.Inventory;

namespace EndlessSurvival.Survival
{
    /// <summary>
    /// All survival tuning plus references to the survival content (items, recipes, prefabs).
    /// Lives in Resources/SurvivalSettings.asset (made by the Package 2-5 setup); code defaults are used when missing.
    /// </summary>
    [CreateAssetMenu(fileName = "SurvivalSettings", menuName = "Endless Survival/Survival Settings")]
    public class SurvivalSettings : ScriptableObject
    {
        private static SurvivalSettings _instance;

        public static SurvivalSettings Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = Resources.Load<SurvivalSettings>("SurvivalSettings");
                    if (_instance == null)
                    {
                        _instance = CreateInstance<SurvivalSettings>();
                        _instance.hideFlags = HideFlags.DontSave;
                    }
                }
                return _instance;
            }
        }

        [Header("Content (filled by the setup menu)")]
        public ItemDefinition[] items = new ItemDefinition[0];
        public RecipeDefinition[] recipes = new RecipeDefinition[0];
        public WorldPickup pickupPrefab;
        public LootChest chestPrefab;
        [Tooltip("Human enemy prefabs used by roadside patrols")]
        public GameObject[] humanEnemyPrefabs = new GameObject[0];
        public LootTable houseLoot;
        public LootTable gasStationLoot;
        public LootTable wreckLoot;
        [Tooltip("Goods a trader can offer")]
        public ItemDefinition[] traderStock = new ItemDefinition[0];

        [Header("Stamina (2.2)")]
        public float maxStamina = 100f;
        public float sprintDrainPerSecond = 11f;
        public float jumpCost = 9f;
        public float regenPerSecond = 16f;
        public float regenDelay = 1.1f;
        [Tooltip("Regen multiplier when hungry or thirsty (below 20%)")]
        public float lowNeedRegenMultiplier = 0.4f;
        [Tooltip("Stamina needed again after running dry before sprinting is allowed")]
        public float exhaustedRecoverAt = 25f;

        [Header("Fatigue (2.8)")]
        [Tooltip("Fatigue per awake game hour (100 = exhausted)")]
        public float fatiguePerGameHour = 4.5f;
        [Tooltip("Extra fatigue per stamina point spent")]
        public float fatiguePerStamina = 0.012f;
        public float sleepRecoveryPerHour = 13f;
        [Tooltip("Rest efficiency when sleeping in the vehicle")]
        public float vehicleSleepEfficiency = 0.6f;
        [Tooltip("Stamina cap at 100 fatigue (fraction of max)")]
        public float staminaCapAtFullFatigue = 0.5f;
        public float fatigueWarning = 70f;

        [Header("Toilet (2.3)")]
        public float toiletDuration = 4f;
        public float toiletDangerRadius = 18f;
        [Tooltip("Seconds with a full bladder before the neglect penalty starts")]
        public float toiletNeglectGrace = 90f;
        public float toiletNeglectSpeed = 0.88f;
        public float toiletNeglectStaminaRegen = 0.6f;

        [Header("Carry weight (2.7)")]
        [Tooltip("Above the backpack's comfortable weight the player slows down; the hard limit is this many times more")]
        public float hardLimitMultiplier = 1.5f;
        public float overloadSpeedAtHardLimit = 0.6f;
        [Tooltip("Extra comfortable kg per Strength point")]
        public float carryPerStrength = 0.25f;

        [Header("Temperature (2.5)")]
        public float dayTemperature = 19f;
        public float nightTemperature = 8f;
        public float mountainOffset = -11f;
        public float desertDayOffset = 9f;
        public float desertNightOffset = -5f;
        public float coastOffset = -1f;
        public float rainOffset = -4f;
        public float snowOffset = -9f;
        public float fogOffset = -2f;
        public float campfireWarmth = 17f;
        public float campfireRadius = 6f;
        public float vehicleWarmth = 11f;
        [Tooltip("Felt temperature below this cools the body")]
        public float comfortTemperature = 12f;
        [Tooltip("Body heat lost per second for each degree below comfort")]
        public float coolingPerDegree = 0.06f;
        public float warmingPerSecond = 1.4f;
        public float coldThreshold = 40f;
        public float freezingThreshold = 15f;
        public float freezingDamagePerSecond = 0.45f;

        [Header("Wetness (2.17)")]
        public float rainWetPerSecond = 1.2f;
        public float waterWetPerSecond = 22f;
        public float dryPerSecond = 0.35f;
        public float dryNearFirePerSecond = 3f;
        public float dryInVehiclePerSecond = 1.5f;
        [Tooltip("Felt temperature lost when fully wet")]
        public float wetChill = 8f;

        [Header("Poison and infection (2.6, 2.15)")]
        public float poisonSeconds = 150f;
        public float poisonDamagePerSecond = 0.3f;
        [Tooltip("Chance to get poisoned by spoiled food")]
        public float spoiledPoisonChance = 0.85f;
        [Tooltip("Seconds after a bleeding wound until it gets infected (unless cleaned)")]
        public float infectionDelay = 240f;
        public float infectionDamagePerSecond = 0.12f;
        [Tooltip("Fever: felt temperature drop while infected")]
        public float feverChill = 6f;

        [Header("Stress (2.20)")]
        public float stressInCombatPerSecond = 2.2f;
        public float stressNoticedPerSecond = 1.2f;
        public float stressPerHit = 4f;
        public float stressLowHealthPerSecond = 0.8f;
        public float stressKill = 6f;
        public float stressDarkNightPerSecond = 0.15f;
        public float stressNeedsPerSecond = 0.12f;
        public float stressPredatorAttack = 8f;
        public float stressDecayPerSecond = 0.35f;
        [Tooltip("Without rest the bar does not go below this")]
        public float stressRestFloor = 25f;
        public float stressCampfirePerSecond = 0.8f;
        public float stressSleepPerHour = 9f;
        public float panicThreshold = 80f;
        public float panicTriggerSeconds = 12f;
        public float panicDuration = 25f;
        public float panicCooldown = 120f;
        public float panicAimSway = 4.5f;

        [Header("Anxiety (2.20)")]
        [Tooltip("Hidden anxiety gained per game hour while stress is above anxietyStressLevel")]
        public float anxietyPerGameHour = 0.76f;
        public float anxietyStressLevel = 60f;
        public float anxietyPerPanic = 2f;
        public float anxietyWarning = 70f;
        public float anxietyMild = 100f;
        public float anxietyModerate = 160f;
        public float anxietySevere = 240f;
        public float anxietyCalmSleepPerHour = 1.6f;
        public float anxietyCampfirePerGameHour = 0.8f;
        public float nightmareChanceMild = 0.25f;
        public float nightmareChanceSevere = 0.6f;
        public float anxietyStressGainMultiplier = 1.4f;

        [Header("Spoilage (2.11)")]
        [Tooltip("Spoil speed in the vehicle trunk (shade)")]
        public float vehicleSpoilMultiplier = 0.85f;
        [Tooltip("Game hours raw meat needs on the drying line while driving")]
        public float dryingLineHours = 3f;

        [Header("Fishing (2.12)")]
        public float fishWaitMin = 4f;
        public float fishWaitMax = 12f;
        public float fishBiteWindow = 1.1f;

        [Header("Rabbit trap (2.14)")]
        public float trapCatchChance = 0.55f;

        [Header("Weather (4.2)")]
        public float weatherSegmentHours = 5f;
        public float rainChance = 0.25f;
        public float snowChanceMountain = 0.45f;
        public float fogChance = 0.15f;
        public float rainGripMultiplier = 0.82f;
        public float snowGripMultiplier = 0.68f;

        [Header("Difficulty (4.3)")]
        public float kmForMaxDifficulty = 15f;
        public float enemyCountBonusAtMax = 1f;
        public float enemyHealthBonusAtMax = 0.6f;
        public float enemyDamageBonusAtMax = 0.4f;
        public int lootExtraRollsAtMax = 2;
        [Tooltip("Fuel found is multiplied by 1 - this at max distance")]
        public float fuelScarcityAtMax = 0.55f;

        [Header("Animals and encounters (3.x)")]
        public float encounterChance = 0.55f;
        public float nightWolfCheckSeconds = 90f;
        public float nightWolfChance = 0.18f;

        [Header("Trader (4.5)")]
        public float traderChance = 0.12f;
        [Tooltip("Trader sells for value * this, buys for value * sellBack")]
        public float traderMarkup = 1.6f;
        public float traderSellBack = 0.5f;

        // ---------------- content lookup ----------------

        public ItemDefinition Item(string assetName)
        {
            if (string.IsNullOrEmpty(assetName)) return null;
            if (items != null)
            {
                foreach (var it in items)
                    if (it != null && it.name == assetName) return it;
            }
            return ItemLookup.Find(assetName);
        }
    }

    /// <summary>Finds existing (non survival) item assets by asset name.</summary>
    public static class ItemLookup
    {
        private static ItemDatabase _db;

        public static ItemDefinition Find(string assetName)
        {
            if (string.IsNullOrEmpty(assetName)) return null;
            if (_db == null)
            {
                foreach (var db in Resources.FindObjectsOfTypeAll<ItemDatabase>())
                {
                    _db = db;
                    break;
                }
            }
            if (_db != null && _db.items != null)
            {
                foreach (var it in _db.items)
                    if (it != null && it.name == assetName) return it;
            }
            foreach (var it in Resources.FindObjectsOfTypeAll<ItemDefinition>())
                if (it != null && it.name == assetName) return it;
            return null;
        }
    }
}
