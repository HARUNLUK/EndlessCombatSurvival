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
        [Tooltip("Humanoid trader model (made by Setup Survival Animations); a capsule is used when empty")]
        public GameObject traderModelPrefab;

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

        [Header("Smoking (Smoking_System.md)")]
        [Tooltip("Stress removed when the cigarette is finished")]
        public float smokeStressRelief = 20f;
        [Tooltip("Extra stress removed slowly after smoking, spread over smokeAfterHours game hours")]
        public float smokeAfterRelief = 10f;
        public float smokeAfterHours = 2f;
        [Tooltip("Longest the smoking action lasts (the clip is cut here)")]
        public float smokeMaxSeconds = 12f;
        [Tooltip("Moving/cancelling within this many seconds gives the cigarette back (not lit yet)")]
        public float smokeLightSeconds = 1.5f;
        [Tooltip("Stamina regeneration is this much lower after smoking")]
        [Range(0f, 1f)] public float smokeStaminaRegenPenalty = 0.15f;
        public float smokeStaminaPenaltyGameMinutes = 10f;
        [Tooltip("An alerted enemy or hunting animal this close interrupts smoking")]
        public float smokeDangerRadius = 18f;

        [Header("Binoculars (Dürbün)")]
        [Tooltip("Zoom range (x). Mouse wheel changes it while looking.")]
        public float binoZoomMin = 4f;
        public float binoZoomMax = 8f;
        [Tooltip("Field of view at 1x; the binocular FOV is this divided by the zoom")]
        public float binoBaseFov = 60f;
        [Tooltip("Walking speed while looking through the binoculars (no sprint)")]
        [Range(0.1f, 1f)] public float binoMoveSpeed = 0.35f;
        [Tooltip("Hand sway in degrees at 1x (smaller at higher zoom it still looks bigger)")]
        public float binoSwayDegrees = 0.18f;
        [Tooltip("Seconds of looking at the screen centre needed to mark a target")]
        public float binoMarkSeconds = 1.5f;
        [Tooltip("Marks stay this long after the binoculars close")]
        public float binoMarkLifetime = 30f;
        [Tooltip("Farthest distance a target can be marked")]
        public float binoMaxRange = 450f;
        [Tooltip("Lens glint: by day, looking towards the sun, an enemy this close that you look at grows suspicious")]
        public float binoGlintRange = 60f;
        [Tooltip("Suspicion added per second by the lens glint (enemy meter 0-100, suspicious above 25)")]
        public float binoGlintSuspicionPerSecond = 4f;
        [Tooltip("The glint alone never raises suspicion above this")]
        public float binoGlintMaxSuspicion = 45f;
        [Tooltip("Angle between the view and the sun direction below which the lens glints")]
        public float binoGlintSunAngle = 70f;

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

        [Header("Road pressure (R2): fuel and roadblocks")]
        [Tooltip("Share of the tank a new journey starts with (fuel per km is on the vehicle prefab)")]
        [Range(0.1f, 1f)] public float startFuelFraction = 0.6f;
        [Tooltip("Pistol rounds a new journey starts with (the pistol itself is given, its magazine starts empty and is loaded from these)")]
        public int startPistolRounds = 9;
        [Tooltip("Road barricades on the road. Off since 2026-11-15 (user cancelled them); the code stays, switch on to bring them back")]
        public bool spawnRoadBarricades = false;
        [Tooltip("Seconds to clear a roadblock gate by hand (Mechanics shortens it)")]
        public float barricadeSeconds = 26f;
        [Tooltip("Time multiplier with a tool kit in the backpack")]
        [Range(0.2f, 1f)] public float barricadeToolMultiplier = 0.6f;
        [Tooltip("Enemies this close hear the work")]
        public float barricadeNoiseRadius = 35f;
        [Tooltip("Chance that a roadblock is guarded, at the start and at max difficulty")]
        [Range(0f, 1f)] public float barricadeGuardChance = 0.08f;
        [Range(0f, 1f)] public float barricadeGuardChanceAtMax = 0.6f;

        [Header("Safe Zones and long road (R3)")]
        [Tooltip("A Safe Zone (trader, fuel, mechanic, save) stands every this many km")]
        public float safeZoneEveryKm = 12f;
        [Tooltip("Money per unit of fuel at a Safe Zone pump")]
        public float safeZoneFuelPrice = 0.8f;
        [Tooltip("Money per point of vehicle body health at a Safe Zone mechanic")]
        public float safeZoneRepairPrice = 0.6f;
        [Tooltip("R4: price of the plan sold at a Safe Zone")]
        public int safeZonePlanPrice = 120;
        [Tooltip("R5: preparation points for every 2 km zone reached and for every Safe Zone reached")]
        public int prepPointsPerZone = 1;
        public int prepPointsPerSafeZone = 8;
        [Tooltip("R6: money the Safe Zone tow asks to bring the vehicle that was left behind")]
        public int safeZoneTowPrice = 60;
        [Tooltip("Fuel and body health a towed vehicle has at least when it arrives")]
        public float safeZoneTowFuel = 12f;
        public float safeZoneTowHealth = 25f;
        [Tooltip("Not used any more (difficulty comes from the character level only); kept so the asset stays valid")]
        public float difficultyGrowthPastMax = 0.6f;
        [Tooltip("Not used any more (difficulty comes from the character level only); kept so the asset stays valid")]
        public float characterDifficultyWeight = 0.6f;

        [Header("Early game and road content (2026-11-14)")]
        [Tooltip("Character level (0-1) below which the world is in the early game: few humans, lone campers")]
        [Range(0f, 1f)] public float earlyGameLevel = 0.15f;
        [Tooltip("Early game: no human group (patrol, camp, roadblock guards, ambush) is bigger than this")]
        public int earlyGameMaxHumans = 2;
        [Tooltip("Early game: at least this share of encounters is animals")]
        [Range(0f, 1f)] public float earlyGameAnimalShare = 0.85f;
        [Tooltip("Share of enemy camps with a single seated camper (early game)")]
        [Range(0f, 1f)] public float loneCamperShare = 0.6f;
        [Tooltip("Same, after the early game")]
        [Range(0f, 1f)] public float loneCamperShareLate = 0.15f;
        [Tooltip("Chance of at least one corpse beside the road per chunk (2/7 of those chunks get two, i.e. 10% of all)")]
        [Range(0f, 1f)] public float corpseChance = 0.35f;
        [Tooltip("Chance of a readable note beside the road per chunk (the start chunk always has one)")]
        [Range(0f, 1f)] public float roadNoteChance = 0.12f;
        [Tooltip("Loot of a corpse; when empty an enemy drop / roadside / wreck table is used")]
        public LootTable corpseLoot;

        [Header("Animals and encounters (3.x)")]
        public float encounterChance = 0.55f;
        public float nightWolfCheckSeconds = 90f;
        public float nightWolfChance = 0.18f;

        [Header("Trader (4.5)")]
        public float traderChance = 0.12f;
        [Tooltip("Enemy camps: share of the chunks that may hold a camp which really get one. 0.1 = about one camp every 5 km (2026-11-13: every chunk had one)")]
        [Range(0f, 1f)] public float enemyCampFrequency = 0.1f;
        [Tooltip("Of the roadside buildings, the share that is a gas station (the rest are abandoned houses). 0.15 = about one station every 9 km")]
        [Range(0f, 1f)] public float gasStationShare = 0.15f;
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
