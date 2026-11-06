using System;
using UnityEngine;
using EndlessSurvival.Camping;
using EndlessSurvival.Inventory;
using EndlessSurvival.Vehicle;
using EndlessSurvival.World;

namespace EndlessSurvival.Survival
{
    /// <summary>
    /// 2.19 vehicle as home: water tank (rain tarp fills it), sleeping in the vehicle (less rest, safer),
    /// plus the grip changes from weather (4.2) and the Driving stat (4.4).
    /// </summary>
    public class VehicleHome : MonoBehaviour, ISurvivalSave
    {
        public static VehicleHome Instance { get; private set; }

        public const float TankCapacity = 40f;
        public float TankWater { get; private set; }

        private VehicleController _vehicle;
        private float _baseForwardGrip = -1f, _baseSidewaysGrip = -1f;

        public string SaveKey => "vehiclehome";

        private void Awake() => Instance = this;

        private void OnEnable() => SurvivalDirector.Instance?.Register(this);

        private void OnDisable()
        {
            SurvivalDirector.Instance?.Unregister(this);
            if (Instance == this) Instance = null;
        }

        public static bool Has(VehicleModType type)
        {
            var mods = VehicleModifications.Instance;
            return mods != null && mods.IsInstalled(type);
        }

        private void Update()
        {
            if (_vehicle == null)
            {
                _vehicle = SurvivalDirector.Vehicle;
                if (_vehicle == null) return;
                _baseForwardGrip = _vehicle.forwardGrip;
                _baseSidewaysGrip = _vehicle.sidewaysGrip;
            }

            // Rain tarp fills the tank (needs the water tank)
            var w = WeatherSystem.Instance;
            if (w != null && w.RainIntensity > 0.05f && Has(VehicleModType.RainTarp) && Has(VehicleModType.WaterTank))
                TankWater = Mathf.Min(TankCapacity, TankWater + w.RainIntensity * 4f * GameClock.DeltaHours);

            ApplyGrip(w);
        }

        private void ApplyGrip(WeatherSystem w)
        {
            if (_baseForwardGrip < 0f) return;
            // Other systems (off-road tyres) may change the grip: follow their changes as the new base
            float expectedF = _baseForwardGrip * _lastGripMult;
            float expectedS = _baseSidewaysGrip * _lastGripMult;
            if (Mathf.Abs(_vehicle.forwardGrip - expectedF) > 0.001f) _baseForwardGrip = _vehicle.forwardGrip / Mathf.Max(0.01f, _lastGripMult);
            if (Mathf.Abs(_vehicle.sidewaysGrip - expectedS) > 0.001f) _baseSidewaysGrip = _vehicle.sidewaysGrip / Mathf.Max(0.01f, _lastGripMult);

            float mult = w != null ? w.GripMultiplier : 1f;
            mult *= 1f + StatSettings.Instance.drivingGripBonus * CharacterStats.Frac(StatType.Driving);
            _lastGripMult = mult;
            _vehicle.forwardGrip = _baseForwardGrip * mult;
            _vehicle.sidewaysGrip = _baseSidewaysGrip * mult;
        }

        private float _lastGripMult = 1f;

        // ---------------- water tank ----------------

        public bool CanUseVehicle => SurvivalDirector.NearVehicleStorage() != null;

        /// <summary>Pours clean water bottles from the backpack into the tank.</summary>
        public string PourIntoTank()
        {
            if (!Has(VehicleModType.WaterTank)) return "Su deposu modu takılı değil";
            var inv = SurvivalDirector.Inventory;
            if (inv == null) return null;
            var s = SurvivalSettings.Instance;
            var bottle = s.Item("Item_EmptyBottle");
            int poured = 0;
            foreach (var name in new[] { "Item_BoiledWater", "Item_WaterBottle" })
            {
                var water = s.Item(name);
                if (water == null) continue;
                while (inv.Storage.GetCount(water) > 0 && TankWater + 1f <= TankCapacity)
                {
                    inv.Storage.Remove(water, 1);
                    if (bottle != null) inv.Storage.Add(bottle, 1);
                    TankWater += 1f;
                    poured++;
                }
            }
            return poured > 0 ? $"Depoya {poured} şişe su boşaltıldı" : "Boşaltılacak temiz su yok (ya da depo dolu)";
        }

        /// <summary>Fills empty bottles from the tank.</summary>
        public string FillFromTank()
        {
            if (!Has(VehicleModType.WaterTank)) return "Su deposu modu takılı değil";
            var inv = SurvivalDirector.Inventory;
            var s = SurvivalSettings.Instance;
            var bottle = s.Item("Item_EmptyBottle");
            var water = s.Item("Item_WaterBottle");
            if (inv == null || bottle == null || water == null) return null;
            int filled = 0;
            while (TankWater >= 1f && inv.Storage.GetCount(bottle) > 0)
            {
                inv.Storage.Remove(bottle, 1);
                if (inv.Storage.Add(water, 1) <= 0)
                {
                    inv.Storage.Add(bottle, 1);
                    break;
                }
                TankWater -= 1f;
                filled++;
            }
            return filled > 0 ? $"{filled} şişe depodan dolduruldu" : "Boş şişe yok ya da depo boş";
        }

        // ---------------- sleeping in the vehicle ----------------

        public string SleepInVehicle()
        {
            var cycle = DayNightCycle.Instance;
            if (cycle == null) return null;
            if (!CanUseVehicle) return "Aracın yanında olmalısın";
            if (SurvivalDirector.DangerNearby(SurvivalDirector.PlayerPosition, 40f)) return "Yakında tehlike var, uyuyamazsın";

            DayNightCycle.NextSleepInVehicle = true;
            var result = cycle.SleepToNextPhase();

            // Safer than a camp: only half the usual ambush chance
            bool ambushed = false;
            if (CampAmbushManager.Instance != null && UnityEngine.Random.value < 0.5f)
                ambushed = CampAmbushManager.Instance.RollAmbushOnSleep(SurvivalDirector.PlayerPosition);

            var player = SurvivalDirector.Player;
            if (player != null)
            {
                var health = player.GetComponent<PlayerHealth>();
                if (health != null && !health.IsDead) health.Heal(ambushed ? 10f : 20f);
                var survival = player.GetComponent<PlayerSurvival>();
                if (survival != null)
                {
                    survival.Eat(-10f);
                    survival.Drink(-12f);
                }
            }
            return ambushed ? "Uykun bölündü: araç kuşatıldı!" : $"Araçta uyudun. {result.summary}";
        }

        // ---------------- save ----------------

        [Serializable]
        private class Data
        {
            public float tank;
        }

        public string CaptureJson() => JsonUtility.ToJson(new Data { tank = TankWater });

        public void RestoreJson(string json)
        {
            var d = JsonUtility.FromJson<Data>(json);
            if (d != null) TankWater = Mathf.Clamp(d.tank, 0f, TankCapacity);
        }
    }

    /// <summary>
    /// 2.18: abandoned wrecks become interactive: siphon fuel with a hose into an empty can (sometimes dry),
    /// strip battery / tyre / scrap. Contents come from the wreck position and the chunk seed.
    /// </summary>
    public class WreckSalvage : MonoBehaviour, IInteractable
    {
        public int seed;
        private bool _fuelTaken, _partsTaken, _initialized;
        private int _fuelUnits;
        private bool _hasBattery, _hasTire;
        private int _scrap;

        public Vector3 InteractPosition => transform.position;
        public bool CanInteract => !(_fuelTaken && _partsTaken);

        private void Init()
        {
            if (_initialized) return;
            _initialized = true;
            var rng = new SeededRandom(seed);
            float fuelMult = DifficultyCurve.FuelMultiplier(transform.position);
            _fuelUnits = rng.Chance(0.55f * fuelMult) ? rng.Range(1, 3) : 0;
            _hasBattery = rng.Chance(0.3f);
            _hasTire = rng.Chance(0.45f);
            _scrap = rng.Range(1, 4);
        }

        private bool CanSiphon(PlayerInventory user)
        {
            var s = SurvivalSettings.Instance;
            var hose = s.Item("Item_RubberHose");
            var empty = s.Item("Item_EmptyFuelCan");
            return !_fuelTaken && user != null && hose != null && empty != null &&
                   user.Storage.GetCount(hose) > 0 && user.Storage.GetCount(empty) > 0;
        }

        public string GetPrompt()
        {
            Init();
            if (CanSiphon(SurvivalDirector.Inventory)) return "Enkaz: hortumla yakıt çek";
            if (!_partsTaken) return "Enkaz: parça sök (akü, lastik, hurda)";
            return "Enkaz: yakıt için hortum + boş bidon lazım";
        }

        public string Interact(PlayerInventory user)
        {
            Init();
            var s = SurvivalSettings.Instance;
            if (CanSiphon(user))
            {
                var empty = s.Item("Item_EmptyFuelCan");
                var full = s.Item("Item_FuelCan");
                _fuelTaken = true;
                if (_fuelUnits <= 0) return "Depo kupkuru, yakıt çıkmadı";
                int filled = 0;
                for (int i = 0; i < _fuelUnits && user.Storage.GetCount(empty) > 0; i++)
                {
                    user.Storage.Remove(empty, 1);
                    if (user.Storage.Add(full, 1) <= 0)
                    {
                        user.Storage.Add(empty, 1);
                        break;
                    }
                    filled++;
                }
                CharacterStats.Instance?.Train(StatType.Mechanics, 0.4f);
                return filled > 0 ? $"Yakıt çekildi: Yakıt Bidonu x{filled}" : "Çantada yer yok";
            }

            if (!_partsTaken)
            {
                _partsTaken = true;
                var toolkit = s.Item("Item_ToolKit");
                bool hasTools = toolkit != null && user.Storage.GetCount(toolkit) > 0;
                int bonus = UnityEngine.Random.value < StatSettings.Instance.mechanicsSaveMaterialChance * CharacterStats.Frac(StatType.Mechanics) ? 1 : 0;
                string got = "";
                SurvivalDirector.GiveItem(s.Item("Item_ScrapMetal"), _scrap + bonus);
                got += $"Hurda x{_scrap + bonus}";
                if (_hasBattery && hasTools)
                {
                    SurvivalDirector.GiveItem(s.Item("Item_CarBattery"), 1);
                    got += ", Akü";
                }
                if (_hasTire && hasTools)
                {
                    SurvivalDirector.GiveItem(s.Item("Item_CarTire"), 1);
                    got += ", Lastik";
                }
                if (!hasTools && (_hasBattery || _hasTire)) got += " (akü/lastik için alet çantası lazım)";
                CharacterStats.Instance?.Train(StatType.Mechanics, 0.6f);
                return "Söküldü: " + got;
            }
            if (!_fuelTaken) return "Yakıt çekmek için lastik hortum ve boş bidon lazım";
            return null;
        }
    }
}
