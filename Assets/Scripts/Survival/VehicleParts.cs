using System;
using UnityEngine;
using UnityEngine.InputSystem;
using EndlessCombat.AI;
using EndlessCombat.Combat;
using EndlessSurvival.Camping;
using EndlessSurvival.Inventory;
using EndlessSurvival.Vehicle;
using EndlessSurvival.World;

namespace EndlessSurvival.Survival
{
    public enum WindowSlot { Front, Rear, Left, Right, Roof }

    /// <summary>State of one window opening. Glass is never repaired: it is covered, or swapped with glass from a wreck.
    /// The jeep is open: only the windshield has glass, the sides, back and roof start Open and can be closed with covers.</summary>
    public enum WindowState { Intact, Broken, Plastic, Plank, Metal, Open }

    /// <summary>
    /// Paket 6: the vehicle as something that has to survive too. Parts that wear and break on their own
    /// (tyres, battery, radiator, headlights, windows), cabin warmth, starting in the cold, and timed work
    /// (changing parts, covering windows, stripping glass from wrecks) that makes noise enemies hear.
    /// Everything comes from wrecks; there is no factory left, so glass is covered (nylon, planks, sheet metal)
    /// or replaced with intact glass taken out of a wreck.
    /// </summary>
    public class VehicleParts : MonoBehaviour, ISurvivalSave
    {
        public static VehicleParts Instance { get; private set; }

        public static readonly string[] TireNames = { "Sol ön", "Sağ ön", "Sol arka", "Sağ arka" };
        public static readonly string[] WindowNames = { "Ön cam", "Arka açıklık", "Sol yan", "Sağ yan", "Tavan" };

        // ---------------- state ----------------
        public readonly float[] Tires = { 100f, 100f, 100f, 100f };
        public bool SpareFilled { get; private set; }
        public float BatteryCharge { get; private set; } = 85f;
        public float BatteryTemp { get; private set; } = 12f;
        public float Radiator { get; private set; } = 100f;
        public float EngineHeat { get; private set; } = 20f;
        public float Headlights { get; private set; } = 100f;
        public bool HeadlightsOn { get; private set; }
        public readonly WindowState[] Windows = { WindowState.Intact, WindowState.Open, WindowState.Open, WindowState.Open, WindowState.Open };
        public readonly float[] PlasticWear = { 100f, 100f, 100f, 100f, 100f };

        /// <summary>Only the windshield has a glass frame; the other openings are closed with covers only.</summary>
        public static bool HasGlassFrame(int slot) => slot == (int)WindowSlot.Front;
        /// <summary>Degrees the cabin is warmer than outside.</summary>
        public float CabinBonus { get; private set; }

        public const float OverheatAt = 100f;
        public const float RestartBelow = 75f;
        public const float FlatAt = 0.5f;

        private VehicleController _vehicle;
        private VehicleModifications _mods;
        private bool _wasDriven;
        private bool _overheated;
        private Light[] _lamps;
        private bool _spareMountKnown;
        private float _fireScan;
        private bool _fireNear;
        private Transform _spareVisual;

        public string SaveKey => "vehicleparts";

        private void Awake() => Instance = this;
        private void OnEnable() => SurvivalDirector.Instance?.Register(this);

        private void OnDisable()
        {
            SurvivalDirector.Instance?.Unregister(this);
            if (Instance == this) Instance = null;
            if (_vehicle != null) _vehicle.Damaged -= OnDamaged;
        }

        private static bool Has(VehicleModType type) => VehicleHome.Has(type);
        private static SurvivalSettings S => SurvivalSettings.Instance;

        public VehicleController Vehicle => _vehicle;
        public bool EngineRunning => _vehicle != null && _vehicle.IsEngineRunning;
        public bool IsFlat(int i) => Tires[i] <= FlatAt;
        public bool Overheated => _overheated;

        /// <summary>Rain gets into the cabin through a broken windshield or an open roof.</summary>
        public bool RainEnters
        {
            get
            {
                foreach (var w in Windows) if (w == WindowState.Broken) return true;
                return Windows[(int)WindowSlot.Roof] == WindowState.Open;
            }
        }

        /// <summary>1 = closed cabin, lower with every broken or badly covered window.</summary>
        public float ShelterFactor
        {
            get
            {
                float loss = 0f;
                for (int i = 0; i < Windows.Length; i++) loss += WindowLoss(Windows[i], i);
                return Mathf.Clamp01(1f - loss);
            }
        }

        private static float WindowLoss(WindowState s, int slot)
        {
            float big = slot == (int)WindowSlot.Front ? 1.2f : slot == (int)WindowSlot.Roof ? 1.3f : 1f; // windshield and roof are larger
            switch (s)
            {
                case WindowState.Broken: return 0.25f * big;
                case WindowState.Open: return 0.15f * big;
                case WindowState.Plastic: return 0.08f * big;
                case WindowState.Plank: return 0.04f * big;
                case WindowState.Metal: return 0.04f * big;
                default: return 0f;
            }
        }

        public static string WindowLabel(WindowState s)
        {
            switch (s)
            {
                case WindowState.Broken: return "KIRIK";
                case WindowState.Plastic: return "naylonla kapalı";
                case WindowState.Plank: return "tahtayla kapalı";
                case WindowState.Metal: return "sacla kapalı";
                case WindowState.Open: return "açık";
                default: return "sağlam";
            }
        }

        // ================================================================== update

        private void Update()
        {
            if (_vehicle == null)
            {
                _vehicle = SurvivalDirector.Vehicle;
                if (_vehicle == null) return;
                _vehicle.Damaged += OnDamaged;
                _vehicle.engineOn = false;
                _mods = _vehicle.GetComponent<VehicleModifications>();
                if (_mods != null) _mods.OnModificationsChanged += OnModsChanged;
                OnModsChanged();
                BuildLamps();
            }

            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            HandleEnterExit();
            HandleKeys();
            UpdateBattery(dt);
            UpdateEngineHeat(dt);
            UpdateTires(dt);
            UpdateWindows(dt);
            UpdateCabin(dt);
            UpdateLamps();
            UpdateCoverVisuals();
            ApplyToController();
            UpdateJob(dt);
        }

        private void OnModsChanged()
        {
            bool mount = Has(VehicleModType.SpareTireMount);
            // Taking the mount off gives its tyre back
            if (!mount && _spareMountKnown && SpareFilled)
            {
                SpareFilled = false;
                SurvivalDirector.GiveItem(S.Item("Item_CarTire"), 1);
            }
            _spareMountKnown = mount;
        }

        private void HandleEnterExit()
        {
            bool driven = _vehicle.isDriven;
            if (driven && !_wasDriven)
            {
                _vehicle.engineOn = false;
                TryStart(true);
            }
            else if (!driven && _wasDriven)
            {
                _vehicle.engineOn = false;
                if (HeadlightsOn) SurvivalNotify.Warn("Farlar açık kaldı: aküyü bitirir");
            }
            _wasDriven = driven;
        }

        private void HandleKeys()
        {
            if (!_vehicle.isDriven || SurvivalUIState.AnyOpen) return;
            if (SKeys.Down(Key.R) && !_vehicle.engineOn) TryStart(false);
            if (SKeys.Down(Key.H))
            {
                HeadlightsOn = !HeadlightsOn;
                if (HeadlightsOn && Headlights < 20f) SurvivalNotify.Warn("Farlar kırık, yanmıyor");
            }
            if (SKeys.Down(Key.T)) UseTowRope();
        }

        // ================================================================== starting

        /// <summary>Chance that the starter turns the engine over (0..1), with the reason it might not.</summary>
        public float StartChance(out string why)
        {
            why = null;
            if (BatteryCharge < 15f) { why = "Akü bitik"; return 0f; }
            float charge = Mathf.InverseLerp(15f, 60f, BatteryCharge);
            float cold = Mathf.InverseLerp(-15f, 5f, BatteryTemp + (Has(VehicleModType.BatteryInsulation) ? 6f : 0f));
            float chance = Mathf.Lerp(0.15f, 1f, charge) * Mathf.Lerp(0.2f, 1f, cold);
            if (chance < 0.95f) why = BatteryTemp < 2f ? "akü soğuk" : "akü zayıf";
            return Mathf.Clamp01(chance);
        }

        /// <summary>Tries the starter. A moving vehicle (pushed / rolling downhill) starts without the battery.</summary>
        public bool TryStart(bool automatic)
        {
            if (_vehicle == null || _vehicle.engineOn) return true;
            if (_vehicle.IsBroken) { SurvivalNotify.Warn("Motor parçalanmış, önce tamir et"); return false; }
            if (_vehicle.currentFuel <= 0f) { SurvivalNotify.Warn("Yakıt yok"); return false; }
            if (_overheated && EngineHeat > RestartBelow) { SurvivalNotify.Warn($"Motor hâlâ çok sıcak ({Mathf.RoundToInt(EngineHeat)}), soğumasını bekle"); return false; }

            bool rolling = _vehicle.CurrentSpeedKmh > 6f;
            float chance = StartChance(out string why);
            bool ok = rolling || UnityEngine.Random.value < chance;
            if (!rolling) BatteryCharge = Mathf.Max(0f, BatteryCharge - 3f);
            if (ok)
            {
                _vehicle.engineOn = true;
                _overheated = false;
                if (rolling && chance < 0.5f) SurvivalNotify.Show("Araç yuvarlanırken çalıştı");
                else if (!automatic || chance < 0.95f) SurvivalNotify.Show("Motor çalıştı");
                return true;
            }
            SurvivalNotify.Warn(BatteryCharge < 15f
                ? "Marş basmıyor: akü bitik. Yokuş aşağı it ya da enkazdan akü bul"
                : $"Marş tutmadı ({why}). [R] tekrar dene. Aracın yanında ateş yakmak aküyü ısıtır");
            return false;
        }

        // ================================================================== battery

        private void UpdateBattery(float dt)
        {
            float ambient = PlayerVitals.ComputeAmbient(_vehicle.transform.position);
            _fireScan -= dt;
            if (_fireScan <= 0f)
            {
                _fireScan = 1f;
                _fireNear = false;
                foreach (var fire in FindObjectsByType<PlayerCampfire>())
                {
                    if (fire != null && fire.IsBurning && (fire.transform.position - _vehicle.transform.position).sqrMagnitude < 7f * 7f)
                    {
                        _fireNear = true;
                        break;
                    }
                }
            }

            float target = ambient, rate = Has(VehicleModType.BatteryInsulation) ? 0.0015f : 0.004f;
            if (EngineRunning)
            {
                target = Mathf.Max(ambient, 30f);
                rate = 0.01f;
                BatteryCharge = Mathf.Min(100f, BatteryCharge + 0.04f * dt);
            }
            else if (_fireNear)
            {
                // A fire next to the vehicle warms the battery up (the old trick on frozen mornings)
                target = Mathf.Max(ambient, 25f);
                rate = 0.02f;
            }
            BatteryTemp = Mathf.Lerp(BatteryTemp, target, 1f - Mathf.Exp(-rate * dt));

            if (!EngineRunning && LampsLit) BatteryCharge = Mathf.Max(0f, BatteryCharge - 0.12f * dt);
        }

        // ================================================================== radiator and heat

        private void UpdateEngineHeat(float dt)
        {
            float ambient = PlayerVitals.ComputeAmbient(_vehicle.transform.position);
            if (EngineRunning)
            {
                float eff = Radiator / 100f;
                float throttle = _vehicle.Throttle;
                float target = 60f + (1f - eff) * 70f + throttle * 10f * (1f + (1f - eff) * 3f);
                float rate = target > EngineHeat ? 0.6f : 0.4f;
                EngineHeat = Mathf.MoveTowards(EngineHeat, target, rate * dt);

                // Idle fuel (and the heater runs on the engine's heat)
                if (throttle < 0.05f) _vehicle.currentFuel = Mathf.Max(0f, _vehicle.currentFuel - 0.006f * dt);

                if (EngineHeat >= OverheatAt)
                {
                    _overheated = true;
                    _vehicle.engineOn = false;
                    Radiator = Mathf.Max(0f, Radiator - 4f);
                    _vehicle.ApplyDamage(3f);
                    SurvivalNotify.Warn("Motor hararet yaptı ve durdu! Soğumasını bekle, radyatörü değiştir");
                }
            }
            else
            {
                EngineHeat = Mathf.MoveTowards(EngineHeat, ambient, 0.35f * dt);
                if (_overheated && EngineHeat <= RestartBelow) _overheated = false;
            }
        }

        // ================================================================== tyres

        private bool OnSnow
        {
            get
            {
                var w = WeatherSystem.Instance;
                if (w != null && w.SnowIntensity > 0.2f) return true;
                var chunk = SurvivalDirector.ChunkAt(_vehicle.transform.position);
                return chunk != null && chunk.biomeType == ChunkBiomeType.Mountain && !chunk.IsFoothill && chunk.mountainVariant == MountainVariant.Snowy;
            }
        }

        private void UpdateTires(float dt)
        {
            if (!_vehicle.isDriven) return;
            float kmh = _vehicle.CurrentSpeedKmh;
            if (kmh < 5f) return;
            for (int i = 0; i < 4 && i < _vehicle.WheelCount; i++)
            {
                if (IsFlat(i) || !_vehicle.WheelOffRoad(i)) continue;
                // Off the road: rocks and roots wear the tyres, sometimes one is punctured
                Tires[i] = Mathf.Max(0f, Tires[i] - 0.02f * kmh / 50f * dt);
                if (UnityEngine.Random.value < 0.00035f * kmh / 50f * dt) Puncture(i, "sivri bir şeye çarptı");
            }
        }

        private void Puncture(int i, string cause)
        {
            if (IsFlat(i)) return;
            Tires[i] = 0f;
            SurvivalNotify.Warn($"{TireNames[i]} lastik patladı ({cause})!");
        }

        // ================================================================== windows

        private void UpdateWindows(float dt)
        {
            if (!_vehicle.isDriven) return;
            float kmh = _vehicle.CurrentSpeedKmh;
            for (int i = 0; i < Windows.Length; i++)
            {
                if (Windows[i] != WindowState.Plastic) continue;
                // Wind tears nylon apart over time
                PlasticWear[i] -= (0.002f + kmh / 50f * 0.01f) * dt;
                if (PlasticWear[i] <= 0f)
                {
                    Windows[i] = HasGlassFrame(i) ? WindowState.Broken : WindowState.Open;
                    SurvivalNotify.Warn($"{WindowNames[i]}: naylon yırtıldı");
                }
            }
        }

        private int SlotForLocalPoint(Vector3 local)
        {
            if (local.y > 1.45f && local.z < 0.3f) return (int)WindowSlot.Roof;
            if (local.z > 0.3f && Mathf.Abs(local.x) < 0.7f) return (int)WindowSlot.Front;
            if (local.z < -1.0f && Mathf.Abs(local.x) < 0.7f) return (int)WindowSlot.Rear;
            return local.x < 0f ? (int)WindowSlot.Left : (int)WindowSlot.Right;
        }

        private void HitWindow(int slot, float chance, bool bullet)
        {
            if (Has(VehicleModType.WindowCage)) return;
            if (bullet && Has(VehicleModType.DoorPlates) && slot >= (int)WindowSlot.Left) return;
            var s = Windows[slot];
            if (s == WindowState.Metal || s == WindowState.Broken || s == WindowState.Open) return;
            if (s == WindowState.Plank) chance *= 0.35f;
            if (UnityEngine.Random.value > chance) return;
            Windows[slot] = HasGlassFrame(slot) || s == WindowState.Intact ? WindowState.Broken : WindowState.Open;
            SurvivalNotify.Warn(s == WindowState.Intact ? $"{WindowNames[slot]} kırıldı!" : $"{WindowNames[slot]}: kaplama parçalandı!");
        }

        // ================================================================== damage

        private void OnDamaged(float amount, bool bullet, Vector3 point, float impact)
        {
            if (_vehicle == null) return;
            Vector3 local = _vehicle.transform.InverseTransformPoint(point);
            bool front = local.z > 0.9f;

            if (bullet)
            {
                for (int i = 0; i < 4 && i < _vehicle.WheelCount; i++)
                {
                    if ((_vehicle.WheelPosition(i) - point).sqrMagnitude < 0.8f * 0.8f && UnityEngine.Random.value < 0.6f)
                    {
                        Puncture(i, "kurşun");
                        return;
                    }
                }
                if (local.y > 1.05f) HitWindow(SlotForLocalPoint(local), 0.4f, true);
                if (front && local.y < 1.05f)
                {
                    if (UnityEngine.Random.value < 0.3f) Radiator = Mathf.Max(0f, Radiator - 15f);
                    else if (UnityEngine.Random.value < 0.3f) Headlights = Mathf.Max(0f, Headlights - 35f);
                }
                return;
            }

            // Collision
            if (front)
            {
                float rad = amount * (Has(VehicleModType.RadiatorGrille) ? 0.3f : 1.5f);
                Radiator = Mathf.Max(0f, Radiator - rad);
                Headlights = Mathf.Max(0f, Headlights - amount * (Has(VehicleModType.Bullbar) ? 0.4f : 1.2f));
                if (impact > 18f) HitWindow((int)WindowSlot.Front, 0.5f, false);
            }
            else if (impact > 18f)
            {
                HitWindow(SlotForLocalPoint(local), 0.4f, false);
            }
        }

        // ================================================================== cabin

        /// <summary>Warmth the player feels inside the vehicle (added to the outside temperature).</summary>
        public float CabinWarmth => CabinBonus;

        private void UpdateCabin(float dt)
        {
            float shelter = ShelterFactor;
            bool insulated = Has(VehicleModType.CabinInsulation);
            // A closed cabin keeps the wind off; the heater runs on engine heat; never warmer than a campfire
            float target = 4f * shelter;
            if (EngineRunning) target += Mathf.InverseLerp(30f, 70f, EngineHeat) * 15f * shelter;
            if (insulated) target += 3f * shelter;
            target = Mathf.Min(target, S.campfireWarmth);

            float rate = target > CabinBonus ? 0.05f : (insulated ? 0.004f : 0.01f);
            CabinBonus = Mathf.MoveTowards(CabinBonus, target, rate * dt * Mathf.Max(1f, Mathf.Abs(target - CabinBonus) * 0.15f));
        }

        // ================================================================== headlights

        private bool LampsLit => HeadlightsOn && Headlights >= 20f && (EngineRunning || BatteryCharge > 3f);

        private const float LampIntensity = 120f; // URP spot falloff is inverse-square: needs a high value to reach the road

        private void BuildLamps()
        {
            var t = _vehicle.transform;
            var existing = t.Find("Package6_Headlights");
            if (existing != null) Destroy(existing.gameObject);
            var root = new GameObject("Package6_Headlights").transform;
            root.SetParent(t, false);

            // Front of the body from the renderers' bounds
            var b = new Bounds(t.position, Vector3.zero);
            bool any = false;
            foreach (var r in _vehicle.GetComponentsInChildren<MeshRenderer>())
            {
                if (r.transform.IsChildOf(root) || r.GetComponentInParent<VehicleModifications>() == null) continue;
                if (r.name.StartsWith("Mod_") || r.transform.parent != null && r.transform.parent.name.StartsWith("Mod_")) continue;
                if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
            }
            Vector3 size = any ? t.InverseTransformVector(b.size) : new Vector3(1.7f, 1.6f, 3f);
            Vector3 center = any ? t.InverseTransformPoint(b.center) : new Vector3(0f, 0.8f, 0f);
            float halfW = Mathf.Abs(size.x) * 0.5f, halfL = Mathf.Abs(size.z) * 0.5f;
            float y = Mathf.Max(0.85f, center.y - Mathf.Abs(size.y) * 0.15f); // headlight height: a low lamp only grazes the road

            _lamps = new Light[2];
            for (int i = 0; i < 2; i++)
            {
                var go = new GameObject(i == 0 ? "Headlight_L" : "Headlight_R");
                go.transform.SetParent(root, false);
                go.transform.localPosition = new Vector3((i == 0 ? -1f : 1f) * halfW * 0.6f, y, center.z + halfL + 0.35f); // ahead of the bullbar
                go.transform.localRotation = Quaternion.Euler(7f, 0f, 0f);
                var l = go.AddComponent<Light>();
                l.type = LightType.Spot;
                l.range = 60f;
                l.spotAngle = 60f;
                l.innerSpotAngle = 30f;
                l.intensity = LampIntensity;
                l.color = new Color(1f, 0.93f, 0.78f);
                l.shadows = LightShadows.None;
                l.enabled = false;
                _lamps[i] = l;
            }
        }

        private void UpdateLamps()
        {
            if (_lamps == null) return;
            bool lit = LampsLit;
            float strength = Mathf.InverseLerp(20f, 100f, Headlights);
            float battery = EngineRunning ? 1f : Mathf.InverseLerp(3f, 30f, BatteryCharge);
            foreach (var l in _lamps)
            {
                if (l == null) continue;
                l.enabled = lit;
                l.intensity = LampIntensity * Mathf.Lerp(0.5f, 1f, strength) * Mathf.Lerp(0.3f, 1f, battery);
            }
        }

        // ================================================================== window cover visuals

        private GameObject[,] _coverVisuals; // [slot, cover kind 0=plastic 1=plank 2=metal]
        private WindowState[] _shownWindows;

        /// <summary>Openings in vehicle space: center, euler, size. Fitted to the open jeep: the sides run from the door tops
        /// to the roll cage, the roof sits on the cage under the roof rack. The windshield is measured from the glass submesh.</summary>
        private static readonly (Vector3 pos, Vector3 euler, Vector2 size)[] DefaultPanes =
        {
            (new Vector3(0f, 1.25f, 0.55f), new Vector3(-25f, 0f, 0f), new Vector2(1.3f, 0.45f)),
            (new Vector3(0f, 1.30f, -1.40f), new Vector3(0f, 180f, 0f), new Vector2(1.55f, 0.40f)),
            (new Vector3(-0.82f, 1.30f, -0.45f), new Vector3(0f, -90f, 0f), new Vector2(1.80f, 0.40f)),
            (new Vector3(0.82f, 1.30f, -0.45f), new Vector3(0f, 90f, 0f), new Vector2(1.80f, 0.40f)),
            (new Vector3(0f, 1.52f, -0.50f), new Vector3(90f, 0f, 0f), new Vector2(1.62f, 1.80f)),
        };

        /// <summary>Windshield pane from the body mesh's glass submesh, so covers sit on the real glass.</summary>
        private bool TryMeasureWindshield(out Vector3 pos, out Vector3 euler, out Vector2 size)
        {
            pos = default; euler = default; size = default;
            foreach (var mr in _vehicle.GetComponentsInChildren<MeshRenderer>())
            {
                var mf = mr.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null || mf.sharedMesh.subMeshCount < 2) continue;
                var mats = mr.sharedMaterials;
                for (int i = 0; i < mats.Length && i < mf.sharedMesh.subMeshCount; i++)
                {
                    if (mats[i] == null || mats[i].name.IndexOf("glass", StringComparison.OrdinalIgnoreCase) < 0) continue;
                    var sb = mf.sharedMesh.GetSubMesh(i).bounds;
                    // corners into vehicle space
                    var vb = new Bounds();
                    bool first = true;
                    for (int k = 0; k < 8; k++)
                    {
                        var corner = sb.center + Vector3.Scale(sb.extents, new Vector3((k & 1) == 0 ? -1 : 1, (k & 2) == 0 ? -1 : 1, (k & 4) == 0 ? -1 : 1));
                        var v = _vehicle.transform.InverseTransformPoint(mr.transform.TransformPoint(corner));
                        if (first) { vb = new Bounds(v, Vector3.zero); first = false; } else vb.Encapsulate(v);
                    }
                    if (vb.size.x < 0.6f || vb.center.z < 0f) continue; // not a windshield
                    float tilt = Mathf.Atan2(vb.size.z, Mathf.Max(0.05f, vb.size.y)) * Mathf.Rad2Deg;
                    pos = vb.center + new Vector3(0f, 0f, 0.04f); // a hair in front of the glass
                    euler = new Vector3(-tilt, 0f, 0f);
                    size = new Vector2(vb.size.x * 1.02f, Mathf.Sqrt(vb.size.y * vb.size.y + vb.size.z * vb.size.z) * 1.02f);
                    WindshieldInfo = $"glass bounds center={vb.center} size={vb.size} tilt={tilt:0}";
                    return true;
                }
            }
            WindshieldInfo = "glass submesh not found, default pane";
            return false;
        }

        /// <summary>Debug: how the windshield pane was placed.</summary>
        public string WindshieldInfo { get; private set; } = "";

        private static Material CoverMat(string name, Color c, bool transparent)
        {
            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard")) { name = name, color = c };
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", transparent ? 0.7f : 0.15f);
            if (transparent)
            {
                mat.SetFloat("_Surface", 1f);
                mat.SetFloat("_Blend", 0f);
                mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                mat.SetInt("_ZWrite", 0);
                mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                mat.renderQueue = 3000;
            }
            return mat;
        }

        private static GameObject Box(Transform parent, Vector3 pos, Vector3 scale, Material mat, Vector3 euler = default)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = Quaternion.Euler(euler);
            go.transform.localScale = scale;
            Destroy(go.GetComponent<Collider>());
            go.GetComponent<Renderer>().sharedMaterial = mat;
            return go;
        }

        private void BuildCoverVisuals()
        {
            var t = _vehicle.transform;
            var old = t.Find("Package6_WindowCovers");
            if (old != null) Destroy(old.gameObject);
            var root = new GameObject("Package6_WindowCovers").transform;
            root.SetParent(t, false);

            var plastic = CoverMat("Mat_Cover_Plastic", new Color(0.85f, 0.9f, 0.95f, 0.45f), true);
            var wood = CoverMat("Mat_Cover_Plank", new Color(0.55f, 0.4f, 0.25f), false);
            var wood2 = CoverMat("Mat_Cover_Plank2", new Color(0.47f, 0.34f, 0.21f), false);
            var metal = CoverMat("Mat_Cover_Metal", new Color(0.38f, 0.36f, 0.33f), false);
            var tape = CoverMat("Mat_Cover_Tape", new Color(0.6f, 0.6f, 0.62f), false);

            int count = Windows.Length;
            var panes = ((Vector3 pos, Vector3 euler, Vector2 size)[])DefaultPanes.Clone();
            if (TryMeasureWindshield(out var wp, out var we, out var ws)) panes[(int)WindowSlot.Front] = (wp, we, ws);

            _coverVisuals = new GameObject[count, 3];
            for (int s = 0; s < count; s++)
            {
                var pane = new GameObject("Pane_" + (WindowSlot)s).transform;
                pane.SetParent(root, false);
                pane.localPosition = panes[s].pos;
                pane.localRotation = Quaternion.Euler(panes[s].euler);
                Vector2 size = panes[s].size;

                var p = new GameObject("Plastic").transform;
                p.SetParent(pane, false);
                Box(p, Vector3.zero, new Vector3(size.x, size.y, 0.01f), plastic);
                Box(p, new Vector3(0f, size.y * 0.5f, 0f), new Vector3(size.x, 0.05f, 0.015f), tape);
                Box(p, new Vector3(0f, -size.y * 0.5f, 0f), new Vector3(size.x, 0.05f, 0.015f), tape);

                var w = new GameObject("Plank").transform;
                w.SetParent(pane, false);
                int boards = Mathf.Max(2, Mathf.RoundToInt(size.y / 0.16f));
                for (int b = 0; b < boards; b++)
                {
                    float y = -size.y * 0.5f + (b + 0.5f) * size.y / boards;
                    Box(w, new Vector3(0f, y, 0f), new Vector3(size.x * 1.04f, size.y / boards * 0.9f, 0.025f), b % 2 == 0 ? wood : wood2,
                        new Vector3(0f, 0f, (b % 2 == 0 ? 1.5f : -2f)));
                }

                var m = new GameObject("Metal").transform;
                m.SetParent(pane, false);
                Box(m, Vector3.zero, new Vector3(size.x * 1.02f, size.y * 1.04f, 0.02f), metal);
                Box(m, new Vector3(0f, 0f, -0.012f), new Vector3(size.x * 0.25f, 0.05f, 0.01f), tape); // weld seam

                _coverVisuals[s, 0] = p.gameObject;
                _coverVisuals[s, 1] = w.gameObject;
                _coverVisuals[s, 2] = m.gameObject;
            }
            _shownWindows = new WindowState[count];
            for (int s = 0; s < count; s++) _shownWindows[s] = (WindowState)(-1);
        }

        private void UpdateCoverVisuals()
        {
            if (_coverVisuals == null) BuildCoverVisuals();
            for (int s = 0; s < Windows.Length; s++)
            {
                if (_shownWindows[s] == Windows[s]) continue;
                _shownWindows[s] = Windows[s];
                _coverVisuals[s, 0].SetActive(Windows[s] == WindowState.Plastic);
                _coverVisuals[s, 1].SetActive(Windows[s] == WindowState.Plank);
                _coverVisuals[s, 2].SetActive(Windows[s] == WindowState.Metal);
            }
        }

        // ================================================================== controller

        private void ApplyToController()
        {
            bool snow = OnSnow;
            bool chains = Has(VehicleModType.SnowChains);
            float speedCap = 0f;
            for (int i = 0; i < 4; i++)
            {
                float grip = 1f, drag = 0f;
                if (IsFlat(i))
                {
                    grip = 0.45f;
                    drag = 380f;
                    speedCap = speedCap <= 0f ? 45f : Mathf.Min(speedCap, 45f);
                }
                else if (Tires[i] < 30f) grip = 0.85f;
                if (chains) grip *= snow ? 1.35f : 0.95f;
                if (i < _vehicle.wheelGripFactor.Length) _vehicle.wheelGripFactor[i] = grip;
                if (i < _vehicle.wheelDragTorque.Length) _vehicle.wheelDragTorque[i] = drag;
            }
            if (chains && !snow) speedCap = speedCap <= 0f ? 70f : Mathf.Min(speedCap, 70f);
            _vehicle.partsTopSpeedKmh = speedCap;

            // The spare tyre on the mount is only visible when there is one
            if (_spareVisual == null) _spareVisual = _vehicle.transform.Find("VisualModifications/Mod_SpareTireMount/SpareTire");
            if (_spareVisual != null && _spareVisual.gameObject.activeSelf != SpareFilled) _spareVisual.gameObject.SetActive(SpareFilled);

            float power = 1f;
            if (EngineHeat > 90f) power *= 0.7f;
            if (Has(VehicleModType.DoorPlates)) power *= 0.93f;
            _vehicle.partsPowerFactor = power;
        }

        // ================================================================== timed work

        public class Job
        {
            public string label;
            public float seconds;
            public float elapsed;
            public Vector3 anchor;
            public float noiseRadius;
            public float noiseTimer;
            public Func<string> done;
        }

        public Job CurrentJob { get; private set; }
        public float JobProgress => CurrentJob != null ? Mathf.Clamp01(CurrentJob.elapsed / CurrentJob.seconds) : 0f;

        /// <summary>Starts timed work at a place. Staying next to it finishes it; walking away or driving off cancels it.</summary>
        public string StartJob(string label, float seconds, Vector3 anchor, float noiseRadius, Func<string> done)
        {
            if (CurrentJob != null) return "Önce elindeki işi bitir: " + CurrentJob.label;
            if (SurvivalDirector.PlayerInVehicle) return "Araçtan in";
            float mech = CharacterStats.Frac(StatType.Mechanics);
            CurrentJob = new Job
            {
                label = label,
                seconds = seconds * Mathf.Lerp(1f, 0.7f, mech),
                anchor = anchor,
                noiseRadius = noiseRadius,
                done = done
            };
            return label + " başladı. Yanından ayrılma.";
        }

        public void CancelJob(string why)
        {
            if (CurrentJob == null) return;
            SurvivalNotify.Warn($"{CurrentJob.label} yarıda kaldı ({why})");
            CurrentJob = null;
        }

        private void UpdateJob(float dt)
        {
            var job = CurrentJob;
            if (job == null) return;
            var player = SurvivalDirector.Player;
            if (player == null || !player.activeInHierarchy || SurvivalDirector.PlayerInVehicle) { CancelJob("araca bindin"); return; }
            Vector3 p = player.transform.position;
            Vector3 a = job.anchor;
            if (new Vector2(p.x - a.x, p.z - a.z).sqrMagnitude > 4.5f * 4.5f) { CancelJob("uzaklaştın"); return; }

            job.elapsed += dt;
            job.noiseTimer -= dt;
            if (job.noiseRadius > 0f && job.noiseTimer <= 0f)
            {
                job.noiseTimer = 2f;
                MakeNoise(a, job.noiseRadius);
            }
            if (job.elapsed >= job.seconds)
            {
                CurrentJob = null;
                string msg = job.done != null ? job.done() : null;
                if (!string.IsNullOrEmpty(msg)) SurvivalNotify.Show(msg);
                CharacterStats.Instance?.Train(StatType.Mechanics, 0.5f);
            }
        }

        /// <summary>Work noise (tools, metal): nearby enemies come to look. No sound file, only their hearing.</summary>
        public static void MakeNoise(Vector3 origin, float radius)
        {
            CombatHooks.RaiseNoise(origin, radius);
            foreach (var hit in Physics.OverlapSphere(origin, radius))
            {
                var enemy = hit.GetComponent<EnemyController>();
                if (enemy != null && !enemy.IsDead) enemy.HearSound(origin, radius);
            }
        }

        // ================================================================== actions (Tab > Araç)

        private Vector3 WorkSpot => _vehicle != null ? _vehicle.transform.position : SurvivalDirector.PlayerPosition;

        private static ItemDefinition It(string name) => S.Item(name);
        private static bool HasTools => SurvivalTest.FreeResources || SurvivalDirector.CountItem(It("Item_ToolKit")) > 0;
        /// <summary>Has the item (always true in test mode).</summary>
        private static bool HasItem(string item, int n = 1) => SurvivalTest.FreeResources || SurvivalDirector.CountItem(It(item)) >= n;
        /// <summary>Uses the item up (nothing is taken in test mode).</summary>
        private static bool TakeItem(string item, int n = 1) => SurvivalTest.FreeResources || SurvivalDirector.RemoveItem(It(item), n) > 0;

        public string ChangeTire(int i)
        {
            if (_vehicle == null) return null;
            if (!HasTools) return "Lastik değiştirmek için alet çantası lazım";
            bool useSpare = SpareFilled;
            if (!useSpare && !HasItem("Item_CarTire")) return "Takılacak lastik yok (enkazlardan sökülür)";
            return StartJob($"{TireNames[i]} lastik değişiyor", 14f, WorkSpot, 18f, () =>
            {
                if (useSpare && SpareFilled) SpareFilled = false;
                else if (!TakeItem("Item_CarTire")) return "Lastik kayboldu";
                Tires[i] = 100f;
                return $"{TireNames[i]} lastik takıldı" + (useSpare ? " (yedek kullanıldı)" : "");
            });
        }

        public string FillSpare()
        {
            if (!Has(VehicleModType.SpareTireMount)) return "Yedek lastik askısı takılı değil";
            if (SpareFilled) return "Askıda zaten lastik var";
            if (!TakeItem("Item_CarTire")) return "Lastik yok";
            SpareFilled = true;
            return "Yedek lastik askıya takıldı";
        }

        public string ReplaceBattery()
        {
            if (!HasTools) return "Alet çantası lazım";
            if (!HasItem("Item_CarBattery")) return "Akü yok (enkazlardan sökülür)";
            return StartJob("Akü değişiyor", 8f, WorkSpot, 10f, () =>
            {
                if (!TakeItem("Item_CarBattery")) return "Akü kayboldu";
                // Wreck batteries are not new: half to fully charged
                BatteryCharge = UnityEngine.Random.Range(55f, 100f);
                return $"Akü takıldı (şarj %{Mathf.RoundToInt(BatteryCharge)})";
            });
        }

        public string ReplaceRadiator()
        {
            if (!HasTools) return "Alet çantası lazım";
            if (!HasItem("Item_Radiator")) return "Radyatör yok (enkazlardan sökülür)";
            return StartJob("Radyatör değişiyor", 22f, WorkSpot, 20f, () =>
            {
                if (!TakeItem("Item_Radiator")) return "Radyatör kayboldu";
                Radiator = UnityEngine.Random.Range(70f, 100f);
                return "Radyatör takıldı";
            });
        }

        public string ReplaceHeadlights()
        {
            if (!HasItem("Item_Headlight")) return "Far ünitesi yok (enkazlardan sökülür)";
            return StartJob("Farlar değişiyor", 8f, WorkSpot, 8f, () =>
            {
                if (!TakeItem("Item_Headlight")) return "Far kayboldu";
                Headlights = 100f;
                return "Farlar takıldı";
            });
        }

        /// <summary>What each part job needs, for the UI ("şu kadar gerekiyor").</summary>
        public const string RadiatorCost = "1x Radyatör + Alet Çantası (22 sn)";
        public const string BatteryCost = "1x Araba Aküsü + Alet Çantası (8 sn)";
        public const string HeadlightCost = "1x Far Ünitesi (8 sn)";
        public const string TireCost = "Askıdaki yedek ya da 1x Araba Lastiği + Alet Çantası (14 sn)";
        public const string SpareCost = "1x Araba Lastiği";
        public const string GlassCost = "1x Sağlam Araç Camı + Alet Çantası (16 sn)";

        /// <summary>Cover cost: (item, amount) pairs and seconds.</summary>
        public static (string item, int amount)[] CoverCost(WindowState cover, out float seconds, out bool needsTools)
        {
            needsTools = false;
            switch (cover)
            {
                case WindowState.Plastic: seconds = 6f; return new[] { ("Item_PlasticSheet", 1), ("Item_DuctTape", 1) };
                case WindowState.Plank: seconds = 12f; return new[] { ("Item_Plank", 2), ("Item_DuctTape", 1) };
                case WindowState.Metal: seconds = 18f; needsTools = true; return new[] { ("Item_ScrapMetal", 3) };
                default: seconds = 0f; return new (string, int)[0];
            }
        }

        public static string CostText((string item, int amount)[] cost)
        {
            var parts = new System.Collections.Generic.List<string>();
            foreach (var c in cost)
            {
                var def = S.Item(c.item);
                parts.Add($"{c.amount}x {(def != null ? def.DisplayName : c.item)}");
            }
            return string.Join(" + ", parts);
        }

        public bool CanAfford((string item, int amount)[] cost)
        {
            if (SurvivalTest.FreeResources) return true;
            foreach (var c in cost)
                if (SurvivalDirector.CountItem(It(c.item)) < c.amount) return false;
            return true;
        }

        public string CoverWindow(int slot, WindowState cover)
        {
            if (Windows[slot] == WindowState.Intact) return "Cam sağlam";
            if (Windows[slot] == cover) return "Zaten bu şekilde kapalı";
            var cost = CoverCost(cover, out float seconds, out bool tools);
            if (tools && !HasTools) return "Saç perçinlemek için alet çantası lazım";
            if (!CanAfford(cost)) return "Malzeme yetersiz: " + CostText(cost);
            string verb = cover == WindowState.Plastic ? "naylonla" : cover == WindowState.Plank ? "tahtayla" : "sacla";
            return StartJob($"{WindowNames[slot]} {verb} kapatılıyor", seconds, WorkSpot, cover == WindowState.Plastic ? 0f : 16f, () =>
            {
                if (!CanAfford(cost)) return "Malzeme yetersiz";
                foreach (var c in cost) TakeItem(c.item, c.amount);
                Windows[slot] = cover;
                PlasticWear[slot] = 100f;
                return $"{WindowNames[slot]} {verb} kapatıldı";
            });
        }

        public string InstallGlass(int slot)
        {
            if (Windows[slot] == WindowState.Intact) return "Cam zaten sağlam";
            if (!HasGlassFrame(slot)) return "Bu açıklığın cam çerçevesi yok; ancak kaplanabilir";
            if (!HasTools) return "Alet çantası lazım";
            if (!HasItem("Item_CarWindow")) return "Sağlam cam yok (enkazlardan uzun uğraşla sökülür)";
            return StartJob($"{WindowNames[slot]} takılıyor", 16f, WorkSpot, 8f, () =>
            {
                if (!TakeItem("Item_CarWindow")) return "Cam kayboldu";
                Windows[slot] = WindowState.Intact;
                return $"{WindowNames[slot]} yerine sağlam cam takıldı";
            });
        }

        // ================================================================== tow rope

        private void UseTowRope()
        {
            if (!Has(VehicleModType.TowRope))
            {
                SurvivalNotify.Warn("Çekme halatı takılı değil");
                return;
            }
            Vector3 pos = _vehicle.transform.position;

            // 1. A wreck close by: drag it off the road
            WreckSalvage best = null;
            float bestD = 14f * 14f;
            foreach (var w in FindObjectsByType<WreckSalvage>())
            {
                if (w == null || w.Towed) continue;
                float d = (w.transform.position - pos).sqrMagnitude;
                if (d < bestD) { bestD = d; best = w; }
            }
            if (best != null)
            {
                SurvivalNotify.Show(best.TowAside(_vehicle.transform) ? "Enkaz halatla yolun kenarına çekildi" : "Enkaz yerinden oynamadı");
                MakeNoise(pos, 25f);
                return;
            }

            // 2. Stuck or tipped: pull the vehicle free (rope around a tree or rock)
            var rb = _vehicle.Rigidbody;
            bool tipped = Vector3.Dot(_vehicle.transform.up, Vector3.up) < 0.6f;
            bool stuck = _vehicle.CurrentSpeedKmh < 3f && (_vehicle.GroundedWheelCount < 4 || tipped);
            if (rb == null || !stuck)
            {
                SurvivalNotify.Show("Halat: yakında çekilecek enkaz yok, araç da saplanmamış");
                return;
            }
            Vector3 fwd = Vector3.ProjectOnPlane(_vehicle.transform.forward, Vector3.up).normalized;
            if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.forward;
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.position = pos + Vector3.up * 1.2f + fwd * 1.5f;
            rb.rotation = Quaternion.LookRotation(fwd, Vector3.up);
            MakeNoise(pos, 20f);
            SurvivalNotify.Show("Araç halatla kurtarıldı");
        }

        // ================================================================== HUD (in the vehicle)

        public void DrawDrivingHud()
        {
            if (_vehicle == null || !_vehicle.isDriven) return;
            float x = 20f, y = Screen.height - 150f, w = 260f;
            var lines = new System.Collections.Generic.List<(string, Color)>();
            Color ok = new Color(0.85f, 0.85f, 0.85f), warn = new Color(1f, 0.75f, 0.35f), bad = new Color(1f, 0.4f, 0.35f);

            if (!_vehicle.engineOn) lines.Add(("Motor kapalı: [R] marş", warn));
            Color heatC = EngineHeat > 90f ? bad : EngineHeat > 80f ? warn : ok;
            lines.Add(($"Hararet {Mathf.RoundToInt(EngineHeat)}   Akü %{Mathf.RoundToInt(BatteryCharge)} ({Mathf.RoundToInt(BatteryTemp)}°C)", heatC));
            string flats = "";
            for (int i = 0; i < 4; i++) if (IsFlat(i)) flats += (flats.Length > 0 ? ", " : "") + TireNames[i];
            if (flats.Length > 0) lines.Add(("Patlak lastik: " + flats, bad));
            lines.Add(($"Kabin +{CabinBonus:0}°C   Far [H] {(LampsLit ? "açık" : "kapalı")}" + (Has(VehicleModType.TowRope) ? "   Halat [T]" : ""), ok));
            if (RainEnters) lines.Add(("Kırık camdan yağmur giriyor", warn));

            SurvivalGUI.Fill(new Rect(x - 6, y - 4, w + 12, lines.Count * 18 + 8), new Color(0f, 0f, 0f, 0.45f));
            var old = GUI.color;
            foreach (var (text, color) in lines)
            {
                GUI.color = color;
                GUI.Label(new Rect(x, y, w, 18), text, SurvivalGUI.Small);
                y += 18;
            }
            GUI.color = old;
        }

        public void SetHeadlights(bool on) => HeadlightsOn = on;

        /// <summary>Debug / tests: puts the battery into a state.</summary>
        public void DebugSetBattery(float charge, float temperature)
        {
            BatteryCharge = charge;
            BatteryTemp = temperature;
        }

        /// <summary>Debug / tests: radiator and engine heat.</summary>
        public void DebugSetEngine(float radiator, float heat)
        {
            Radiator = radiator;
            EngineHeat = heat;
        }

        // ================================================================== save

        [Serializable]
        private class Data
        {
            public float[] tires;
            public bool spare;
            public float battery, batteryTemp, radiator, heat, headlights;
            public bool lamps;
            public int[] windows;
            public float[] plastic;
            public float cabin;
        }

        public string CaptureJson()
        {
            var w = new int[Windows.Length];
            for (int i = 0; i < w.Length; i++) w[i] = (int)Windows[i];
            return JsonUtility.ToJson(new Data
            {
                tires = (float[])Tires.Clone(), spare = SpareFilled, battery = BatteryCharge, batteryTemp = BatteryTemp,
                radiator = Radiator, heat = EngineHeat, headlights = Headlights, lamps = HeadlightsOn,
                windows = w, plastic = (float[])PlasticWear.Clone(), cabin = CabinBonus
            });
        }

        public void RestoreJson(string json)
        {
            var d = JsonUtility.FromJson<Data>(json);
            if (d == null) return;
            if (d.tires != null) for (int i = 0; i < Tires.Length && i < d.tires.Length; i++) Tires[i] = d.tires[i];
            SpareFilled = d.spare;
            _spareMountKnown = Has(VehicleModType.SpareTireMount);
            BatteryCharge = d.battery;
            BatteryTemp = d.batteryTemp;
            Radiator = d.radiator;
            EngineHeat = d.heat;
            Headlights = d.headlights;
            HeadlightsOn = d.lamps;
            if (d.windows != null) for (int i = 0; i < Windows.Length && i < d.windows.Length; i++) Windows[i] = (WindowState)d.windows[i];
            if (d.plastic != null) for (int i = 0; i < PlasticWear.Length && i < d.plastic.Length; i++) PlasticWear[i] = d.plastic[i];
            CabinBonus = d.cabin;
        }
    }
}
