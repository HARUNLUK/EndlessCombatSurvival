using System.Collections.Generic;
using UnityEngine;
using EndlessCombat.AI;
using EndlessSurvival.Camping;
using EndlessSurvival.Inventory;
using EndlessSurvival.World;

namespace EndlessSurvival.Survival
{
    /// <summary>Temperature (2.5), poison (2.6), infection (2.15), clothing (2.16), wetness (2.17), stress and anxiety (2.20).</summary>
    public partial class PlayerVitals
    {
        // temperature
        public float BodyHeat { get; private set; } = 100f;
        public float FeltTemperature { get; private set; } = 15f;
        public float AmbientTemperature { get; private set; } = 15f;
        public float ColdSeverity => Mathf.Clamp01((S.coldThreshold - BodyHeat) / Mathf.Max(1f, S.coldThreshold));
        public bool Freezing => BodyHeat < S.freezingThreshold;
        public bool NearFire { get; private set; }
        private float _foodWarmth, _foodWarmthTimer;
        private float _coldDamageAccum;

        // wetness
        public float Wetness { get; private set; }
        public bool InWater { get; private set; }

        // poison / infection
        private float _poisonTimer;
        public bool IsPoisoned => _poisonTimer > 0f;
        public bool WoundDirty { get; private set; }
        public bool IsInfected { get; private set; }
        private float _woundTimer;
        private float _sickDamageAccum;

        // clothing
        private readonly Dictionary<string, float> _durability = new Dictionary<string, float>();
        public float ClothingInsulation { get; private set; }
        public float ClothingWaterproof { get; private set; }
        public float ClothingNoise { get; private set; }

        // stress / anxiety
        public float Stress { get; private set; }
        private float _anxiety;
        private bool _anxietyWarned;
        private float _aboveThreshold;
        private float _panicTimer;
        private float _panicCooldown;
        private int _lastKills = -1;
        private float _calmTime;
        private float _falseAlarmTimer = 120f;
        public bool Panicking => _panicTimer > 0f;
        public float AimSwayDegrees => Panicking ? S.panicAimSway * (0.6f + 0.4f * Mathf.Abs(Mathf.Sin(Time.time * 3.1f))) : 0f;
        /// <summary>Adrenaline during panic: hits hurt a bit less.</summary>
        public float PainMultiplier => Panicking ? 0.85f : 1f;

        /// <summary>0 none, 1 mild, 2 moderate, 3 severe. The raw value stays hidden.</summary>
        public int AnxietyLevel => _anxiety >= S.anxietySevere ? 3 : _anxiety >= S.anxietyModerate ? 2 : _anxiety >= S.anxietyMild ? 1 : 0;
        public bool AnxietyHint => _anxiety >= S.anxietyWarning;

        private float _fireScan;
        private PlayerCampfire _nearFire;

        private void UpdateConditions(float dt)
        {
            UpdateClothing();
            UpdateFireAndTemperature(dt);
            UpdateWetness(dt);
            UpdateSickness(dt);
            UpdateStress(dt);
        }

        // ---------------- clothing (2.16) ----------------

        private static readonly EquipSlot[] ApparelSlots = { EquipSlot.Head, EquipSlot.Torso, EquipSlot.Arms, EquipSlot.Legs, EquipSlot.Feet };

        private void UpdateClothing()
        {
            float insulation = 0f, waterproof = 0f, noise = 0f;
            int pieces = 0;
            if (_equipment != null)
            {
                foreach (var slot in ApparelSlots)
                {
                    var item = _equipment.Get(slot);
                    if (item == null) continue;
                    float dur = Durability(item) / 100f;
                    float quality = Mathf.Lerp(0.3f, 1f, dur);
                    insulation += item.insulation * quality;
                    waterproof += item.waterproof * quality;
                    noise += item.noise;
                    pieces++;
                }
            }
            ClothingInsulation = insulation;
            ClothingWaterproof = pieces > 0 ? Mathf.Clamp01(waterproof / 3f) : 0f; // 3 waterproof pieces = fully dry
            ClothingNoise = noise;

            // slow wear while awake
            if (GameClock.AwakeDeltaHours > 0f) WearClothing(GameClock.AwakeDeltaHours * 0.35f);
        }

        public float Durability(ItemDefinition item)
        {
            if (item == null) return 100f;
            return _durability.TryGetValue(item.name, out float d) ? d : 100f;
        }

        private void WearClothing(float amount)
        {
            if (_equipment == null || amount <= 0f) return;
            foreach (var slot in ApparelSlots)
            {
                var item = _equipment.Get(slot);
                if (item == null) continue;
                float before = Durability(item);
                float after = Mathf.Max(0f, before - amount);
                _durability[item.name] = after;
                if (before > 20f && after <= 20f) SurvivalNotify.Warn($"{item.DisplayName} yıpranıyor (kumaşla onar)");
            }
        }

        /// <summary>Recipe effect: repairs all worn clothes.</summary>
        public void RepairClothing()
        {
            if (_equipment == null) return;
            foreach (var slot in ApparelSlots)
            {
                var item = _equipment.Get(slot);
                if (item != null) _durability[item.name] = 100f;
            }
        }

        public float WornAverageDurability
        {
            get
            {
                if (_equipment == null) return 100f;
                float sum = 0f;
                int n = 0;
                foreach (var slot in ApparelSlots)
                {
                    var item = _equipment.Get(slot);
                    if (item == null) continue;
                    sum += Durability(item);
                    n++;
                }
                return n > 0 ? sum / n : 100f;
            }
        }

        private void CaptureClothing(out string[] names, out float[] values)
        {
            names = new string[_durability.Count];
            values = new float[_durability.Count];
            int i = 0;
            foreach (var pair in _durability)
            {
                names[i] = pair.Key;
                values[i] = pair.Value;
                i++;
            }
        }

        private void RestoreClothing(string[] names, float[] values)
        {
            _durability.Clear();
            if (names == null || values == null) return;
            for (int i = 0; i < names.Length && i < values.Length; i++)
                if (!string.IsNullOrEmpty(names[i])) _durability[names[i]] = values[i];
        }

        // ---------------- temperature (2.5) ----------------

        private void UpdateFireAndTemperature(float dt)
        {
            Vector3 pos = SurvivalDirector.PlayerPosition;
            bool inVehicle = SurvivalDirector.PlayerInVehicle;

            _fireScan -= dt;
            if (_fireScan <= 0f)
            {
                _fireScan = 1f;
                _nearFire = null;
                float best = S.campfireRadius * S.campfireRadius;
                foreach (var fire in FindObjectsByType<PlayerCampfire>())
                {
                    if (fire == null || !fire.IsBurning) continue;
                    float d = (fire.transform.position - pos).sqrMagnitude;
                    if (d < best)
                    {
                        best = d;
                        _nearFire = fire;
                    }
                }
            }
            NearFire = _nearFire != null && !inVehicle;

            AmbientTemperature = ComputeAmbient(pos);
            float felt = AmbientTemperature + ClothingInsulation;
            if (NearFire)
            {
                float d = Vector3.Distance(_nearFire.transform.position, pos);
                felt += S.campfireWarmth * Mathf.Clamp01(1.2f - d / S.campfireRadius);
            }
            if (inVehicle) felt += S.vehicleWarmth;
            felt -= Wetness / 100f * S.wetChill;
            if (IsInfected) felt -= S.feverChill;
            if (_foodWarmthTimer > 0f)
            {
                _foodWarmthTimer -= dt;
                felt += _foodWarmth;
            }
            FeltTemperature = felt;

            if (felt < S.comfortTemperature)
                BodyHeat -= (S.comfortTemperature - felt) * S.coolingPerDegree * dt;
            else
                BodyHeat += S.warmingPerSecond * Mathf.Clamp((felt - S.comfortTemperature) / 5f, 0.3f, 1.5f) * dt;
            BodyHeat = Mathf.Clamp(BodyHeat, 0f, 100f);

            if (Freezing && _health != null)
            {
                _coldDamageAccum += S.freezingDamagePerSecond * dt;
                if (_coldDamageAccum >= 1f)
                {
                    _health.DamageIgnoringArmor(_coldDamageAccum, "Donarak öldü");
                    _coldDamageAccum = 0f;
                }
                Stamina = Mathf.Max(0f, Stamina - 2f * dt);
            }
        }

        public static float ComputeAmbient(Vector3 pos)
        {
            var s = SurvivalSettings.Instance;
            float hour = DayNightCycle.Instance != null ? DayNightCycle.Instance.CurrentHour : 12f;
            float day = 0.5f + 0.5f * Mathf.Cos((hour - 14f) / 24f * Mathf.PI * 2f); // warmest at 14:00
            float t = Mathf.Lerp(s.nightTemperature, s.dayTemperature, day);

            var chunk = SurvivalDirector.ChunkAt(pos);
            if (chunk != null)
            {
                switch (chunk.biomeType)
                {
                    case ChunkBiomeType.Mountain:
                        t += chunk.IsFoothill ? s.mountainOffset * 0.5f : s.mountainOffset;
                        break;
                    case ChunkBiomeType.Desert:
                        t += Mathf.Lerp(s.desertNightOffset, s.desertDayOffset, day);
                        break;
                    case ChunkBiomeType.Coast:
                        t += s.coastOffset;
                        break;
                }
            }
            var w = WeatherSystem.Instance;
            if (w != null) t += w.TemperatureOffset;
            return t;
        }

        // ---------------- wetness (2.17) ----------------

        private void UpdateWetness(float dt)
        {
            bool inVehicle = SurvivalDirector.PlayerInVehicle;
            InWater = false;
            if (!inVehicle && PlayerActive && WaterBodies.Query(_player.transform.position, out float surface, out _))
                InWater = surface > _player.transform.position.y + 0.25f;

            float gain = 0f;
            if (InWater) gain += S.waterWetPerSecond;
            var w = WeatherSystem.Instance;
            if (!inVehicle && w != null && w.RainIntensity > 0.01f) gain += S.rainWetPerSecond * w.RainIntensity * (1f - ClothingWaterproof);

            if (gain > 0f) Wetness = Mathf.Min(100f, Wetness + gain * dt);
            else
            {
                float dry = S.dryPerSecond;
                if (NearFire) dry += S.dryNearFirePerSecond;
                if (inVehicle) dry += S.dryInVehiclePerSecond;
                Wetness = Mathf.Max(0f, Wetness - dry * dt);
            }
        }

        // ---------------- poison and infection (2.6, 2.15) ----------------

        public void Poison(float seconds)
        {
            bool was = IsPoisoned;
            _poisonTimer = Mathf.Max(_poisonTimer, seconds);
            if (!was) SurvivalNotify.Warn("Zehirlendin! (bitki çayı iyi gelir)");
            AddStress(5f);
        }

        public void CurePoison()
        {
            if (IsPoisoned) SurvivalNotify.Show("Zehirlenme geçti");
            _poisonTimer = 0f;
        }

        public void CleanWound()
        {
            if (WoundDirty) SurvivalNotify.Show("Yara temizlendi");
            WoundDirty = false;
            _woundTimer = 0f;
        }

        public void CureInfection()
        {
            if (IsInfected) SurvivalNotify.Show("Enfeksiyon geçiyor");
            IsInfected = false;
            WoundDirty = false;
            _woundTimer = 0f;
        }

        private void OnBleedingStarted()
        {
            if (IsInfected) return;
            if (!WoundDirty) _woundTimer = 0f;
            WoundDirty = true;
        }

        private void UpdateSickness(float dt)
        {
            float damage = 0f;
            if (_poisonTimer > 0f)
            {
                _poisonTimer -= dt;
                damage += S.poisonDamagePerSecond * dt;
                if (_poisonTimer <= 0f) SurvivalNotify.Show("Zehirlenme geçti");
            }

            if (WoundDirty && !IsInfected)
            {
                _woundTimer += dt;
                if (_woundTimer >= S.infectionDelay)
                {
                    IsInfected = true;
                    WoundDirty = false;
                    SurvivalNotify.Warn("Yara enfekte oldu! Ateşin var (şifalı merhem gerekli)");
                }
            }
            if (IsInfected) damage += S.infectionDamagePerSecond * dt;

            if (damage > 0f && _health != null)
            {
                _sickDamageAccum += damage;
                if (_sickDamageAccum >= 1f)
                {
                    _health.DamageIgnoringArmor(_sickDamageAccum, IsInfected ? "Enfeksiyondan öldü" : "Zehirlenerek öldü");
                    _sickDamageAccum = 0f;
                }
            }
        }

        private void OnItemConsumed(ItemDefinition item, PlayerInventory inv)
        {
            if (item == null) return;

            if (item.poisonChance > 0f)
            {
                float chance = item.poisonChance * (1f - StatSettings.Instance.naturePoisonReduction * CharacterStats.Frac(StatType.Nature));
                if (Random.value < chance) Poison(S.poisonSeconds);
            }
            if ((item.effects & ConsumableEffect.CuresPoison) != 0) CurePoison();
            if ((item.effects & ConsumableEffect.CuresInfection) != 0) CureInfection();
            if ((item.effects & ConsumableEffect.CleansWound) != 0) CleanWound();
            if ((item.effects & ConsumableEffect.RestoresStamina) != 0) RestoreStamina(40f);
            if ((item.effects & ConsumableEffect.ReducesFatigue) != 0) Fatigue = Mathf.Max(0f, Fatigue - 15f);
            if (item.stressRelief != 0f) Stress = Mathf.Clamp(Stress - item.stressRelief, 0f, 100f);
            if (item.warmth > 0f)
            {
                _foodWarmth = item.warmth;
                _foodWarmthTimer = 120f;
                BodyHeat = Mathf.Min(100f, BodyHeat + item.warmth);
            }
        }

        // ---------------- stress and anxiety (2.20) ----------------

        public void AddStress(float amount)
        {
            if (amount <= 0f) return;
            if (AnxietyLevel > 0) amount *= S.anxietyStressGainMultiplier;
            Stress = Mathf.Clamp(Stress + amount, 0f, 100f);
            _calmTime = 0f;
        }

        public void AddStressRelief(float amount) => Stress = Mathf.Max(0f, Stress - Mathf.Max(0f, amount));

        private void OnDamaged(float damage)
        {
            AddStress(S.stressPerHit * Mathf.Clamp(damage / 10f, 0.5f, 2f));
            WearClothing(damage * 0.15f);
            if (ToiletActive) CancelToilet("Saldırı! Tuvalet yarıda kaldı");
        }

        private void OnPlayerFired() => _calmTime = 0f;

        private void UpdateStress(float dt)
        {
            Vector3 pos = SurvivalDirector.PlayerPosition;
            float gain = 0f;

            bool combat = false, noticed = false;
            foreach (var e in EnemyController.Active)
            {
                if (e == null || e.IsDead) continue;
                float d = (e.transform.position - pos).sqrMagnitude;
                if (d > 45f * 45f) continue;
                if (e.IsAlerted) combat = true;
                else if (e.State == EnemyController.EnemyState.Suspicious) noticed = true;
            }
            foreach (var a in AnimalController.All)
            {
                if (a != null && !a.IsDead && a.IsHunting && (a.transform.position - pos).sqrMagnitude < 40f * 40f) combat = true;
            }
            if (combat) gain += S.stressInCombatPerSecond;
            else if (noticed) gain += S.stressNoticedPerSecond;

            if (_health != null && (_health.CurrentHealth < _health.maxHealth * 0.35f || _health.isBleeding)) gain += S.stressLowHealthPerSecond;

            var cycle = DayNightCycle.Instance;
            if (cycle != null && cycle.IsNight && !NearFire && !SurvivalDirector.PlayerInVehicle) gain += S.stressDarkNightPerSecond;

            if (_survival != null && (_survival.Hunger < _survival.maxHunger * 0.2f || _survival.Thirst < _survival.maxThirst * 0.2f || Fatigue > 80f))
                gain += S.stressNeedsPerSecond;

            var tracker = GameStatsTracker.Instance;
            if (tracker != null)
            {
                if (_lastKills >= 0 && tracker.enemiesKilled > _lastKills) AddStress(S.stressKill * (tracker.enemiesKilled - _lastKills));
                _lastKills = tracker.enemiesKilled;
            }

            if (gain > 0f) AddStress(gain * dt);
            else _calmTime += dt;

            // Relief
            float decayMult = AnxietyLevel > 0 ? 0.7f : 1f;
            if (NearFire)
            {
                Stress = Mathf.Max(0f, Stress - S.stressCampfirePerSecond * decayMult * dt);
                if (Stress < 40f && GameClock.AwakeDeltaHours > 0f) _anxiety = Mathf.Max(0f, _anxiety - S.anxietyCampfirePerGameHour * GameClock.AwakeDeltaHours);
            }
            else if (_calmTime > 5f && Stress > S.stressRestFloor)
            {
                Stress = Mathf.Max(S.stressRestFloor, Stress - S.stressDecayPerSecond * decayMult * dt);
            }

            // Panic (acute stress)
            if (_panicCooldown > 0f) _panicCooldown -= dt;
            if (_panicTimer > 0f)
            {
                _panicTimer -= dt;
                if (_panicTimer <= 0f) _panicCooldown = S.panicCooldown;
            }
            else
            {
                if (Stress >= S.panicThreshold) _aboveThreshold += dt;
                else _aboveThreshold = 0f;
                if (_aboveThreshold >= S.panicTriggerSeconds && _panicCooldown <= 0f)
                {
                    _panicTimer = S.panicDuration;
                    _aboveThreshold = 0f;
                    _anxiety += S.anxietyPerPanic;
                    SurvivalNotify.Warn("Panik! Nefesini toparla");
                }
            }

            // Anxiety (slow, hidden)
            if (Stress > S.anxietyStressLevel && GameClock.AwakeDeltaHours > 0f)
                _anxiety += S.anxietyPerGameHour * GameClock.AwakeDeltaHours;
            if (_anxiety >= S.anxietyWarning && !_anxietyWarned)
            {
                _anxietyWarned = true;
                SurvivalNotify.Warn("Karakter gergin hissediyor (dinlenmeye ihtiyacı var)", 6f);
            }
            if (_anxiety < S.anxietyWarning * 0.6f) _anxietyWarned = false;

            // Severe anxiety: imagined noises now and then (shown as a message, no sound)
            if (AnxietyLevel >= 3 && !SurvivalDirector.PlayerInVehicle)
            {
                _falseAlarmTimer -= dt;
                if (_falseAlarmTimer <= 0f)
                {
                    _falseAlarmTimer = Random.Range(90f, 220f);
                    SurvivalNotify.Warn("Çalıların arasında bir şey mi kıpırdadı?", 4f);
                }
            }
        }

        private bool RollNightmare()
        {
            int level = AnxietyLevel;
            if (level == 0) return false;
            float chance = level >= 3 ? S.nightmareChanceSevere : Mathf.Lerp(S.nightmareChanceMild, S.nightmareChanceSevere, (level - 1) * 0.5f);
            return Random.value < chance;
        }

        private void OnSleptConditions(float hours, bool inVehicle, bool nightmare, float efficiency)
        {
            Stress = Mathf.Max(0f, Stress - hours * S.stressSleepPerHour * efficiency);
            if (!nightmare) _anxiety = Mathf.Max(0f, _anxiety - hours * S.anxietyCalmSleepPerHour * (inVehicle ? 0.7f : 1f));
            BodyHeat = Mathf.Max(BodyHeat, NearFire || inVehicle ? 80f : BodyHeat);
        }
    }
}
