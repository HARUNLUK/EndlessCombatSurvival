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
        private bool _hasBattery, _hasTire, _hasHeadlight, _hasRadiator;
        private int _scrap;
        private int _glass;          // intact windows left in the wreck
        private bool _glassWorking;

        public bool Towed { get; private set; }

        public Vector3 InteractPosition => transform.position;
        public bool CanInteract => !(_fuelTaken && _partsTaken && (_glass <= 0 || _glassWorking));

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
            // Paket 6: more parts, and now and then a window that survived
            _hasHeadlight = rng.Chance(0.4f);
            _hasRadiator = rng.Chance(0.3f);
            _glass = rng.Chance(0.45f) ? rng.Range(1, 3) : 0;
        }

        /// <summary>Tow rope: drags the wreck off the road, to the side away from the towing vehicle.</summary>
        public bool TowAside(Transform vehicle)
        {
            Transform wreck = transform.parent != null && transform.parent.GetComponent<Chunk>() == null ? transform.parent : transform;
            Vector3 away = wreck.position - vehicle.position;
            away.y = 0f;
            Vector3 side = Vector3.Cross(Vector3.up, vehicle.forward).normalized;
            if (Vector3.Dot(away, side) < 0f) side = -side;
            Vector3 target = wreck.position + side * 7f;
            if (Physics.Raycast(target + Vector3.up * 30f, Vector3.down, out RaycastHit hit, 80f, ~0, QueryTriggerInteraction.Ignore))
                target.y = hit.point.y + (wreck.position.y - GroundBelow(wreck.position));
            wreck.position = target;
            Physics.SyncTransforms();
            Towed = true;
            return true;
        }

        private static float GroundBelow(Vector3 p)
        {
            var terrain = Terrain.activeTerrain;
            foreach (var t in Terrain.activeTerrains)
            {
                Vector3 local = p - t.transform.position;
                if (local.x >= 0 && local.z >= 0 && local.x <= t.terrainData.size.x && local.z <= t.terrainData.size.z) { terrain = t; break; }
            }
            return terrain != null ? terrain.SampleHeight(p) + terrain.transform.position.y : p.y;
        }

        private string StartGlassJob(PlayerInventory user)
        {
            var parts = VehicleParts.Instance;
            var s = SurvivalSettings.Instance;
            var toolkit = s.Item("Item_ToolKit");
            if (!SurvivalTest.FreeResources && (toolkit == null || user.Storage.GetCount(toolkit) <= 0)) return "Camı çıkarmak için alet çantası lazım";
            if (parts == null) return null;
            _glassWorking = true;
            string msg = parts.StartJob("Sağlam cam sökülüyor (conta, klips, yavaşça)", 40f, transform.position, 12f, () =>
            {
                _glassWorking = false;
                _glass--;
                // The glass may still crack while it comes out
                float breakChance = Mathf.Lerp(0.35f, 0.15f, CharacterStats.Frac(StatType.Mechanics));
                if (UnityEngine.Random.value < breakChance) return "Cam çıkarken çatladı, işe yaramaz";
                SurvivalDirector.GiveItem(s.Item("Item_CarWindow"), 1);
                return "Sağlam cam söküldü";
            });
            if (parts.CurrentJob == null) _glassWorking = false;
            return msg;
        }

        private void Update()
        {
            // A cancelled glass job can be started again
            if (_glassWorking && (VehicleParts.Instance == null || VehicleParts.Instance.CurrentJob == null)) _glassWorking = false;
        }

        private bool CanSiphon(PlayerInventory user)
        {
            var s = SurvivalSettings.Instance;
            var hose = s.Item("Item_RubberHose");
            var empty = s.Item("Item_EmptyFuelCan");
            if (_fuelTaken || user == null) return false;
            if (SurvivalTest.FreeResources) return _fuelUnits > 0;
            return hose != null && empty != null && user.Storage.GetCount(hose) > 0 && user.Storage.GetCount(empty) > 0;
        }

        public string GetPrompt()
        {
            Init();
            if (CanSiphon(SurvivalDirector.Inventory)) return "Enkaz: hortumla yakıt çek";
            if (!_partsTaken) return "Enkaz: parça sök (akü, lastik, far, radyatör, hurda)";
            if (_glass > 0 && !_glassWorking) return $"Enkaz: sağlam camı sök (uzun sürer, {_glass} cam)";
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
                bool free = SurvivalTest.FreeResources; // test: no hose or empty can needed
                for (int i = 0; i < _fuelUnits && (free || user.Storage.GetCount(empty) > 0); i++)
                {
                    if (!free) user.Storage.Remove(empty, 1);
                    if (user.Storage.Add(full, 1) <= 0)
                    {
                        if (!free) user.Storage.Add(empty, 1);
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
                bool hasTools = SurvivalTest.FreeResources || toolkit != null && user.Storage.GetCount(toolkit) > 0;
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
                if (_hasHeadlight && hasTools)
                {
                    SurvivalDirector.GiveItem(s.Item("Item_Headlight"), 1);
                    got += ", Far";
                }
                if (_hasRadiator && hasTools)
                {
                    SurvivalDirector.GiveItem(s.Item("Item_Radiator"), 1);
                    got += ", Radyatör";
                }
                if (!hasTools && (_hasBattery || _hasTire || _hasHeadlight || _hasRadiator)) got += " (akü/lastik/far/radyatör için alet çantası lazım)";
                if (_glass > 0) got += $". İçinde {_glass} sağlam cam var";
                CharacterStats.Instance?.Train(StatType.Mechanics, 0.6f);
                // Tools on metal: enemies nearby hear it
                VehicleParts.MakeNoise(transform.position, 18f);
                return "Söküldü: " + got;
            }
            if (_glass > 0 && !_glassWorking) return StartGlassJob(user);
            if (!_fuelTaken) return "Yakıt çekmek için lastik hortum ve boş bidon lazım";
            return null;
        }
    }
}
