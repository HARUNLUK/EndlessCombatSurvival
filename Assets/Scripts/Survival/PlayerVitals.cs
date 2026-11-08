using System;
using UnityEngine;
using UnityEngine.InputSystem;
using StarterAssets;
using EndlessCombat.Combat;
using EndlessSurvival.Inventory;
using EndlessSurvival.World;

namespace EndlessSurvival.Survival
{
    /// <summary>
    /// Player body systems that are not in PlayerSurvival: stamina (2.2), toilet action (2.3), carry weight (2.7),
    /// fatigue (2.8) and the movement speed they produce. The second half (PlayerVitals.Conditions.cs) holds
    /// temperature, wetness, poison, infection, clothing, stress and anxiety.
    /// Lives on the SurvivalSystems object so it keeps running while the player sits in the vehicle.
    /// </summary>
    [DefaultExecutionOrder(-80)]
    public partial class PlayerVitals : MonoBehaviour, ISurvivalSave
    {
        public static PlayerVitals Instance { get; private set; }

        private GameObject _player;
        private ThirdPersonController _tpc;
        private StarterAssetsInputs _inputs;
        private PlayerSurvival _survival;
        private PlayerHealth _health;
        private PlayerInventory _inventory;
        private PlayerEquipment _equipment;
        private PlayerStealthController _stealth;
        private float _baseMove = 2f, _baseSprint = 5.335f;
        private bool _speedsCaptured;

        // stamina
        public float Stamina { get; private set; } = 100f;
        private float _lastStaminaUse = -10f;
        private bool _exhausted;

        // fatigue
        public float Fatigue { get; private set; }
        private bool _fatigueWarned;

        // toilet
        public bool ToiletActive { get; private set; }
        public float ToiletProgress { get; private set; }
        private float _toiletNeglect;
        private float _toiletCheck;

        // carry
        public bool Overloaded { get; private set; }
        public float OverloadRatio { get; private set; }
        public float ComfortWeight { get; private set; } = 30f;

        public string SaveKey => "vitals";

        private SurvivalSettings S => SurvivalSettings.Instance;
        private CharacterStats Stats => CharacterStats.Instance;

        private void Awake()
        {
            Instance = this;
        }

        private void OnEnable()
        {
            DayNightCycle.Slept += OnSlept;
            PlayerInventory.ItemConsumed += OnItemConsumed;
            CombatHooks.PlayerFired += OnPlayerFired;
            SurvivalDirector.Instance?.Register(this);
        }

        private void OnDisable()
        {
            DayNightCycle.Slept -= OnSlept;
            PlayerInventory.ItemConsumed -= OnItemConsumed;
            CombatHooks.PlayerFired -= OnPlayerFired;
            if (_health != null)
            {
                _health.Damaged -= OnDamaged;
                _health.BleedingStarted -= OnBleedingStarted;
            }
            SurvivalDirector.Instance?.Unregister(this);
            if (Instance == this) Instance = null;
        }

        private void Start()
        {
            _player = SurvivalDirector.Player;
            if (_player == null) return;
            _tpc = _player.GetComponent<ThirdPersonController>();
            _inputs = _player.GetComponent<StarterAssetsInputs>();
            _survival = _player.GetComponent<PlayerSurvival>();
            _health = _player.GetComponent<PlayerHealth>();
            _inventory = _player.GetComponent<PlayerInventory>();
            _equipment = _player.GetComponent<PlayerEquipment>();
            _stealth = _player.GetComponent<PlayerStealthController>();
            if (_health != null)
            {
                _health.Damaged += OnDamaged;
                _health.BleedingStarted += OnBleedingStarted;
            }
            Stamina = S.maxStamina;
        }

        public float MaxStamina
        {
            get
            {
                float endurance = 1f + StatSettings.Instance.enduranceStaminaBonus * CharacterStats.Frac(StatType.Endurance);
                float fatigueCap = Mathf.Lerp(1f, S.staminaCapAtFullFatigue, Mathf.InverseLerp(40f, 100f, Fatigue));
                return S.maxStamina * endurance * fatigueCap;
            }
        }

        private bool PlayerActive => _player != null && _player.activeInHierarchy;

        private void Update()
        {
            if (_player == null)
            {
                Start();
                if (_player == null) return;
            }
            if (_health != null && _health.IsDead) return;

            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            UpdateCarry();
            UpdateStamina(dt);
            UpdateFatigue();
            UpdateToilet(dt);
            UpdateConditions(dt);
            ReportStatConditions();
            ApplyMovement();
        }

        // ---------------- stamina (2.2) ----------------

        private bool WantsSprint =>
            PlayerActive && _inputs != null && _inputs.sprint && _inputs.move.sqrMagnitude > 0.01f &&
            (_stealth == null || !_stealth.IsCrouching) && !ToiletActive;

        public bool Exhausted => _exhausted;
        public bool CanSprint => !_exhausted && !Overloaded && !ToiletActive;

        private void UpdateStamina(float dt)
        {
            var statSet = StatSettings.Instance;
            bool grounded = _tpc == null || _tpc.Grounded;

            if (WantsSprint && CanSprint && grounded)
            {
                float drain = S.sprintDrainPerSecond * (1f - statSet.enduranceDrainReduction * CharacterStats.Frac(StatType.Endurance));
                if (Panicking) drain *= 0.7f; // adrenaline
                Spend(drain * dt);
                bool hard = Fatigue > 60f || OverloadRatio > 0f;
                Stats?.Train(StatType.Endurance, statSet.endurancePerSprintSecond * dt, hard);
            }

            // Jump costs stamina; blocked when empty
            if (PlayerActive && _inputs != null && _inputs.jump && grounded)
            {
                if (ToiletActive || Stamina < S.jumpCost * 0.5f) _inputs.jump = false;
                else Spend(S.jumpCost);
            }

            if (Stamina <= 0.5f) _exhausted = true;
            if (_exhausted && Stamina >= S.exhaustedRecoverAt) _exhausted = false;

            if (Time.time - _lastStaminaUse > S.regenDelay)
            {
                float regen = S.regenPerSecond;
                if (_survival != null && (_survival.Hunger < _survival.maxHunger * 0.2f || _survival.Thirst < _survival.maxThirst * 0.2f))
                    regen *= S.lowNeedRegenMultiplier;
                if (ToiletNeglected) regen *= S.toiletNeglectStaminaRegen;
                regen *= Mathf.Lerp(1f, 0.5f, ColdSeverity);
                if (IsPoisoned || IsInfected) regen *= 0.7f;
                regen *= SmokingAction.StaminaRegenMultiplier;
                Stamina = Mathf.Min(MaxStamina, Stamina + regen * dt);
            }
            if (Stamina > MaxStamina) Stamina = MaxStamina;
        }

        private void Spend(float amount)
        {
            if (amount <= 0f) return;
            Stamina = Mathf.Max(0f, Stamina - amount);
            _lastStaminaUse = Time.time;
            Fatigue = Mathf.Min(100f, Fatigue + amount * S.fatiguePerStamina);
        }

        /// <summary>Used by melee attacks (CombatHooks.TrySpendStamina).</summary>
        public bool TrySpendStamina(float cost)
        {
            if (cost <= 0f) return true;
            if (Stamina < cost * 0.5f)
            {
                SurvivalNotify.Warn("Çok yorgunsun");
                return false;
            }
            Spend(cost);
            return true;
        }

        public void RestoreStamina(float amount) => Stamina = Mathf.Min(MaxStamina, Stamina + amount);

        // ---------------- fatigue (2.8) ----------------

        private void UpdateFatigue()
        {
            Fatigue = Mathf.Clamp(Fatigue + GameClock.AwakeDeltaHours * S.fatiguePerGameHour, 0f, 100f);
            if (Fatigue >= S.fatigueWarning && !_fatigueWarned)
            {
                _fatigueWarned = true;
                SurvivalNotify.Warn("Çok yorgunsun: uyuman lazım");
            }
            if (Fatigue < S.fatigueWarning - 10f) _fatigueWarned = false;
        }

        private void OnSlept(float hours, bool inVehicle)
        {
            bool nightmare = RollNightmare();
            float efficiency = (inVehicle ? S.vehicleSleepEfficiency : 1f) * (nightmare ? 0.5f : 1f);
            Fatigue = Mathf.Max(0f, Fatigue - hours * S.sleepRecoveryPerHour * efficiency);
            Stamina = MaxStamina;
            OnSleptConditions(hours, inVehicle, nightmare, efficiency);
            if (nightmare) SurvivalNotify.Warn("Kabus gördün, uykun bölündü");
            else if (inVehicle) SurvivalNotify.Show("Araçta uyudun (daha az dinlendirdi)");
        }

        // ---------------- toilet (2.3) ----------------

        public bool ToiletNeglected => _toiletNeglect > S.toiletNeglectGrace;

        private void UpdateToilet(float dt)
        {
            if (_survival == null) return;

            if (_survival.IsBladderFull) _toiletNeglect += dt;
            else _toiletNeglect = Mathf.Max(0f, _toiletNeglect - dt * 4f);

            if (ToiletActive)
            {
                if (!PlayerActive) { CancelToilet(null); return; }
                ToiletProgress += dt / Mathf.Max(0.5f, S.toiletDuration);
                _toiletCheck -= dt;
                if (_toiletCheck <= 0f)
                {
                    _toiletCheck = 0.4f;
                    if (SurvivalDirector.DangerNearby(_player.transform.position, S.toiletDangerRadius))
                    {
                        CancelToilet("Tehlike! Tuvalet yarıda kaldı");
                        return;
                    }
                }
                if (ToiletProgress >= 1f)
                {
                    _survival.RelieveNeed();
                    _toiletNeglect = 0f;
                    EndToilet();
                    SurvivalNotify.Show("Rahatladın");
                }
                return;
            }

            if (!PlayerActive || SurvivalUIState.AnyOpen || !SKeys.Down(Key.P)) return;
            if (!SurvivalTest.FreeResources && _survival.Bladder < _survival.maxBladder * 0.2f) // test mode: any time
            {
                SurvivalNotify.Show("Şu an tuvalet ihtiyacın yok");
                return;
            }
            if (SurvivalDirector.DangerNearby(_player.transform.position, S.toiletDangerRadius))
            {
                SurvivalNotify.Warn("Yakında tehlike var, şimdi olmaz");
                return;
            }
            ToiletActive = true;
            ToiletProgress = 0f;
            _toiletCheck = 0f;
            if (_stealth != null) _stealth.SetCrouching(true);
            SurvivalNotify.Show("Tuvalet... (savunmasızsın)");
        }

        private void CancelToilet(string message)
        {
            EndToilet();
            if (message != null) SurvivalNotify.Warn(message);
        }

        private void EndToilet()
        {
            ToiletActive = false;
            ToiletProgress = 0f;
            if (_stealth != null && PlayerActive) _stealth.SetCrouching(false);
        }

        // ---------------- carry weight (2.7) ----------------

        private void UpdateCarry()
        {
            if (_inventory == null) return;
            ComfortWeight = _inventory.maxCarryWeight +
                            CharacterStats.Frac(StatType.Strength) * StatSettings.Instance.maxValue * S.carryPerStrength;
            float hard = ComfortWeight * Mathf.Max(1f, S.hardLimitMultiplier);
            var storage = _inventory.Storage;
            if (Mathf.Abs(storage.MaxWeight - hard) > 0.01f) storage.MaxWeight = hard;

            float weight = storage.TotalWeight;
            Overloaded = weight > ComfortWeight + 0.01f;
            OverloadRatio = Overloaded ? Mathf.Clamp01((weight - ComfortWeight) / Mathf.Max(0.1f, hard - ComfortWeight)) : 0f;

            if (Overloaded && PlayerActive && _inputs != null && _inputs.move.sqrMagnitude > 0.01f)
                Stats?.Train(StatType.Strength, StatSettings.Instance.strengthPerOverloadedSecond * Time.deltaTime, OverloadRatio > 0.5f);
        }

        // ---------------- movement ----------------

        private void ApplyMovement()
        {
            if (!PlayerActive || _tpc == null) return;
            if (!_speedsCaptured)
            {
                _speedsCaptured = true;
                bool crouched = _stealth != null && _stealth.IsCrouching;
                if (!crouched)
                {
                    _baseMove = _tpc.MoveSpeed;
                    _baseSprint = _tpc.SprintSpeed;
                }
            }

            bool crouch = _stealth != null && _stealth.IsCrouching;
            float walk = crouch ? _stealth.crouchSpeed : _baseMove;
            float sprintBonus = 1f + StatSettings.Instance.enduranceSprintSpeedBonus * CharacterStats.Frac(StatType.Endurance);
            float sprint = crouch ? _stealth.crouchSpeed : _baseSprint * sprintBonus;

            float mult = 1f;
            if (Overloaded) mult *= Mathf.Lerp(0.85f, S.overloadSpeedAtHardLimit, OverloadRatio);
            if (ToiletNeglected) mult *= S.toiletNeglectSpeed;
            mult *= Mathf.Lerp(1f, 0.85f, ColdSeverity);
            if (Fatigue > 85f) mult *= 0.9f;
            if (ToiletActive || PlayerSurvivalAnimator.LocksMovement) mult = 0f;

            float move = walk * mult;
            _tpc.MoveSpeed = move;
            _tpc.SprintSpeed = CanSprint ? sprint * mult : move;
        }

        // ---------------- stats conditions ----------------

        private void ReportStatConditions()
        {
            var stats = Stats;
            if (stats == null) return;
            if (_survival != null)
            {
                stats.SetCondition(StatCondition.Hunger, Mathf.InverseLerp(_survival.maxHunger * 0.35f, 0f, _survival.Hunger));
                stats.SetCondition(StatCondition.Thirst, Mathf.InverseLerp(_survival.maxThirst * 0.35f, 0f, _survival.Thirst));
            }
            stats.SetCondition(StatCondition.Sleepless, Mathf.InverseLerp(60f, 100f, Fatigue));
            stats.SetCondition(StatCondition.ToiletNeglect, ToiletNeglected ? 1f : 0f);
            stats.SetCondition(StatCondition.Cold, ColdSeverity);
            stats.SetCondition(StatCondition.Sick, IsPoisoned || IsInfected ? 1f : 0f);
            float wounded = 0f;
            if (_health != null)
            {
                wounded = Mathf.InverseLerp(_health.maxHealth * 0.4f, _health.maxHealth * 0.1f, _health.CurrentHealth);
                if (_health.isBleeding) wounded = Mathf.Max(wounded, 0.5f);
            }
            stats.SetCondition(StatCondition.Wounded, wounded);
            stats.SetCondition(StatCondition.Overloaded, Overloaded ? Mathf.Lerp(0.4f, 1f, OverloadRatio) : 0f);
            stats.SetCondition(StatCondition.Panic, Panicking ? 1f : 0f);
            stats.SetCondition(StatCondition.Anxiety, AnxietyLevel == 0 ? 0f : AnxietyLevel == 1 ? 0.4f : AnxietyLevel == 2 ? 0.7f : 1f);
        }

        /// <summary>Melee damage multiplier from hunger/wounds is in the stats; this covers exhaustion.</summary>
        public float MeleeWeakness => _exhausted ? 0.75f : 1f;

        // ---------------- save ----------------

        [Serializable]
        private class Data
        {
            public float stamina, fatigue, bodyHeat, wetness, poison, stress, anxiety, woundTimer;
            public bool woundDirty, infected, anxietyWarned;
            public string[] clothingNames = new string[0];
            public float[] clothingDurability = new float[0];
        }

        public string CaptureJson()
        {
            var d = new Data
            {
                stamina = Stamina, fatigue = Fatigue, bodyHeat = BodyHeat, wetness = Wetness, poison = _poisonTimer,
                stress = Stress, anxiety = _anxiety, woundTimer = _woundTimer, woundDirty = WoundDirty, infected = IsInfected,
                anxietyWarned = _anxietyWarned
            };
            CaptureClothing(out d.clothingNames, out d.clothingDurability);
            return JsonUtility.ToJson(d);
        }

        public void RestoreJson(string json)
        {
            var d = JsonUtility.FromJson<Data>(json);
            if (d == null) return;
            Stamina = d.stamina;
            Fatigue = d.fatigue;
            BodyHeat = d.bodyHeat;
            Wetness = d.wetness;
            _poisonTimer = d.poison;
            Stress = d.stress;
            _anxiety = d.anxiety;
            _woundTimer = d.woundTimer;
            WoundDirty = d.woundDirty;
            IsInfected = d.infected;
            _anxietyWarned = d.anxietyWarned;
            RestoreClothing(d.clothingNames, d.clothingDurability);
        }
    }
}
