using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using EndlessSurvival.Inventory;
using EndlessSurvival.Camping;
using EndlessCombat.Combat;
using EndlessCombat.AI;
using EndlessSurvival.World;
using EndlessSurvival.World.POI;

namespace EndlessSurvival.Survival
{
    /// <summary>
    /// Binoculars (Dürbün). Hold [Z] to look, release to put them down; "Use" in the inventory toggles them.
    /// First person from the character's eyes, black two-circle mask, mouse wheel zoom (4x-8x, FOV), light hand sway.
    /// Looking ~1.5 s at an enemy, an enemy camp, a loot chest, a wreck or a lighthouse in the centre marks it:
    /// the mark stays 30 s after the binoculars close and shows the distance (enemies also their suspicion state).
    /// While looking: no firing or hitting, slow walk, the weapon goes on the back (HandsBusyHolster).
    /// Not in a vehicle, while swimming, sitting, sleeping, crafting, smoking or on the toilet.
    /// Lens glint: by day, facing the sun, enemies within 60 m you look at slowly grow suspicious.
    /// No animation clip yet (see Docs: Mixamo list). Values: SurvivalSettings > Binoculars.
    /// </summary>
    [DefaultExecutionOrder(-50)] // before the EndlessCombat.Character.PlayerMotor Update, so jump can be cancelled
    public class BinocularsController : MonoBehaviour
    {
        public const string BinocularsItem = "Item_Binoculars";

        public static BinocularsController Instance { get; private set; }
        public static bool Active => Instance != null && Instance._active;
        public static float CurrentZoom => Instance != null ? Instance._zoom : 1f;

        private static SurvivalSettings S => SurvivalSettings.Instance;
        private static ItemDefinition Binoculars => S != null ? S.Item(BinocularsItem) : null;

        private enum MarkKind { Enemy, Camp, Chest, Wreck, Lighthouse }

        private class Target
        {
            public Transform transform;
            public MarkKind kind;
            public string label;
            public float radius;        // rough size, for the aim test
            public Vector3 localCenter; // point to aim at, in the target's space
            public EnemyController enemy;
            public Vector3 Point => transform.TransformPoint(localCenter);
        }

        private class Mark
        {
            public Target target;
            public Vector3 lastPoint;
            public float expiresAt;
        }

        private bool _active;
        private bool _toggled;       // opened from the inventory: stays open until Z or Use
        private float _zoom = 6f;
        private float _zoomTarget = 6f;
        private GameObject _player;
        private EndlessCombat.Character.PlayerMotor _tpc;
        private EndlessCombat.Character.PlayerInputState _inputs;
        private Animator _animator;
        private PlayerHealth _health;
        private float _savedNear = -1f;
        private Camera _cam;

        private readonly List<Target> _targets = new List<Target>();
        private readonly Dictionary<Transform, Target> _targetByTransform = new Dictionary<Transform, Target>();
        private float _refreshTimer;
        private readonly List<Mark> _marks = new List<Mark>();
        private Target _focus;
        private float _focusTime;
        private float _focusLostAt = -1f;
        private bool _testItemGiven;
        private float _glintTimer;

        // ------------------------------------------------------------------ lifecycle

        private void Awake()
        {
            Instance = this;
            PlayerInventory.CustomUse += OnInventoryUse;
            PlayerInventory.CustomUsable += IsBinoculars;
            RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
            Camera.onPreCull += OnPreCullCamera;
        }

        private void OnDestroy()
        {
            Close(null);
            PlayerInventory.CustomUse -= OnInventoryUse;
            PlayerInventory.CustomUsable -= IsBinoculars;
            RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
            Camera.onPreCull -= OnPreCullCamera;
            if (_maskTex != null) Destroy(_maskTex);
            if (Instance == this) Instance = null;
        }

        private static bool IsBinoculars(ItemDefinition item) => item != null && item == Binoculars;

        private string OnInventoryUse(ItemDefinition item, PlayerInventory user)
        {
            if (!IsBinoculars(item)) return null;
            if (_active) { Close(null); return "Dürbünü indirdin"; }
            var msg = TryOpen(true);
            return msg ?? "Dürbün açık (envanteri kapat; Z ile indir)";
        }

        // ------------------------------------------------------------------ open / close

        private bool HasBinoculars()
        {
            if (SurvivalTest.FreeResources) return true;
            var item = Binoculars;
            return item != null && SurvivalDirector.CountItem(item, false) > 0;
        }

        /// <summary>Why the binoculars cannot be used right now (null = they can).</summary>
        private string BlockReason()
        {
            var player = SurvivalDirector.Player;
            if (player == null || !player.activeInHierarchy || SurvivalDirector.PlayerInVehicle) return "Araçtayken dürbün kullanılmaz";
            var health = player.GetComponent<PlayerHealth>();
            if (health != null && health.IsDead) return "";
            if (IsSwimming(player)) return "Yüzerken dürbün kullanılmaz";
            var sitting = player.GetComponent<PlayerSittingController>();
            if (sitting != null && sitting.IsSitting) return "Otururken dürbün kullanılmaz";
            if (CampfireUI.IsResting) return "Dinlenirken dürbün kullanılmaz";
            var placer = player.GetComponent<PlayerCampPlacer>();
            if (placer != null && placer.IsPlacing) return "Önce kampı yerleştir";
            var craft = CraftingSystem.Instance;
            if (craft != null && craft.CurrentJob != null) return "Üretim sürerken dürbün kullanılmaz";
            var parts = VehicleParts.Instance;
            if (parts != null && parts.CurrentJob != null) return "Araç işi sürerken dürbün kullanılmaz";
            var fishing = FishingController.Instance;
            if (fishing != null && fishing.State != FishingController.FishState.Idle) return "Balık tutarken dürbün kullanılmaz";
            if (SmokingAction.Active) return "Sigara içerken dürbün kullanılmaz";
            var vitals = PlayerVitals.Instance;
            if (vitals != null && vitals.ToiletActive) return "Önce işini bitir";
            if (vitals != null && vitals.Panicking) return "Ellerin titriyor";
            if (CombatBody.PlayerLocked) return "";
            return null;
        }

        private static bool IsSwimming(GameObject player)
        {
            Vector3 p = player.transform.position;
            return WaterBodies.Query(p, out float surface, out _) && surface > p.y + 0.9f;
        }

        /// <summary>Opens the binoculars. Returns an error message, or null when they opened.</summary>
        private float _openedAt;

        public string TryOpen(bool toggled)
        {
            if (_active) return null;
            if (!HasBinoculars()) return "Dürbünün yok";
            string reason = BlockReason();
            if (reason != null) return reason.Length > 0 ? reason : "Şimdi olmaz";

            _player = SurvivalDirector.Player;
            _tpc = _player.GetComponent<EndlessCombat.Character.PlayerMotor>();
            _inputs = _player.GetComponent<EndlessCombat.Character.PlayerInputState>();
            _animator = _player.GetComponentInChildren<Animator>();
            _health = _player.GetComponent<PlayerHealth>();
            _active = true;
            _toggled = toggled;
            _openedAt = Time.time;
            _zoomTarget = Mathf.Clamp(_zoomTarget, S.binoZoomMin, S.binoZoomMax);
            _zoom = Mathf.Max(1.5f, _zoomTarget * 0.6f); // eases in
            _focus = null;
            _focusTime = 0f;
            _refreshTimer = 0f;
            ApplyMovement(true);
            return null;
        }

        /// <summary>Puts the binoculars down. Marks stay for binoMarkLifetime seconds.</summary>
        public void Close(string message)
        {
            if (!_active) return;
            _active = false;
            _toggled = false;
            _focus = null;
            _focusTime = 0f;
            ApplyMovement(false);
            if (_cam != null && _savedNear > 0f) _cam.nearClipPlane = _savedNear;
            _savedNear = -1f;
            _cam = null;
            float until = Time.time + S.binoMarkLifetime;
            foreach (var m in _marks) m.expiresAt = until;
            if (!string.IsNullOrEmpty(message)) SurvivalNotify.Warn(message);
        }

        private void ApplyMovement(bool on)
        {
            if (_tpc == null) return;
            _tpc.ExtraSpeedMultiplier = on ? S.binoMoveSpeed : 1f;
            _tpc.FaceCamera = on;
            _tpc.LookSensitivityMultiplier = on ? LookSensitivity() : 1f;
        }

        private float LookSensitivity() => Mathf.Clamp(1.6f / Mathf.Max(1f, _zoom), 0.12f, 1f);

        // ------------------------------------------------------------------ update

        private void Update()
        {
            GiveTestItem();
            ExpireMarks();

            bool uiOpen = SurvivalUIState.AnyOpen;
            if (!uiOpen && Time.timeScale > 0.001f)
            {
                if (SKeys.Down(Key.Z))
                {
                    if (_active && _toggled) Close(null);
                    else if (!_active)
                    {
                        var msg = TryOpen(false);
                        if (!string.IsNullOrEmpty(msg)) SurvivalNotify.Warn(msg);
                    }
                }
                else if (_active && !_toggled && !SKeys.Held(Key.Z)) Close(null);
                // Gamepad (opened from the action menu): B puts them down
                else if (_active && _toggled && Time.time - _openedAt > 0.3f && EndlessCombat.Combat.GameInput.PadPressed(1))
                {
                    Close(null);
                    EndlessCombat.Combat.GameInput.BlockGameplayBriefly();
                }
            }

            if (!_active) return;

            string reason = BlockReason();
            if (reason != null || !HasBinoculars())
            {
                Close(string.IsNullOrEmpty(reason) ? null : reason);
                return;
            }
            if (_player != SurvivalDirector.Player) { Close(null); return; }

            // No jumping with the binoculars at the eyes
            if (_inputs != null) _inputs.jump = false;

            // Zoom with the mouse wheel
            if (!uiOpen && Mouse.current != null)
            {
                float wheel = Mouse.current.scroll.ReadValue().y;
                if (Mathf.Abs(wheel) > 0.01f) _zoomTarget += Mathf.Sign(wheel) * 0.5f;
            }
            // Gamepad: D-pad up / down zooms
            if (!uiOpen) _zoomTarget += EndlessCombat.Combat.GameInput.PadDpadPressed.y * 1f;
            _zoomTarget = Mathf.Clamp(_zoomTarget, S.binoZoomMin, S.binoZoomMax);
            _zoom = Mathf.MoveTowards(_zoom, _zoomTarget, Time.deltaTime * 12f);
            if (_tpc != null) _tpc.LookSensitivityMultiplier = LookSensitivity();

            // Marks stay while looking
            float keep = Time.time + S.binoMarkLifetime;
            foreach (var m in _marks) m.expiresAt = keep;

            var cam = Camera.main;
            if (cam == null) return;
            Vector3 eye, forward;
            EyePose(cam, out eye, out forward, out _);
            if (!uiOpen) UpdateFocus(eye, forward);
            UpdateGlint(eye, forward);
        }

        private void GiveTestItem()
        {
            if (_testItemGiven || !SurvivalTest.FreeResources || Time.timeSinceLevelLoad < 3f) return;
            var player = SurvivalDirector.Player;
            var item = Binoculars;
            if (player == null || item == null) return;
            _testItemGiven = true;
            if (SurvivalDirector.CountItem(item) <= 0) SurvivalDirector.GiveItem(item, 1);
        }

        // ------------------------------------------------------------------ camera

        private Vector3 HeadPosition()
        {
            Transform head = _animator != null && _animator.isHuman ? _animator.GetBoneTransform(HumanBodyBones.Head) : null;
            if (head != null) return head.position + Vector3.up * 0.06f;
            return _player.transform.position + Vector3.up * 1.62f;
        }

        /// <summary>Eye position and view rotation (mouse look from the third person camera target, plus sway).</summary>
        private void EyePose(Camera cam, out Vector3 position, out Vector3 forward, out Quaternion rotation)
        {
            Quaternion look = cam.transform.rotation;
            if (_tpc != null && _tpc.CinemachineCameraTarget != null)
            {
                Vector3 e = _tpc.CinemachineCameraTarget.transform.eulerAngles;
                look = Quaternion.Euler(e.x, e.y, 0f);
            }

            // Hand sway: slow Perlin drift, stronger while walking
            float t = Time.time;
            float moving = _inputs != null && _inputs.move.sqrMagnitude > 0.04f ? 2.5f : 1f;
            var stealth = PlayerStealthController.Instance;
            if (stealth != null && stealth.IsCrouching) moving *= 0.6f;
            float sway = S.binoSwayDegrees * moving;
            float yaw = (Mathf.PerlinNoise(t * 0.55f, 0.3f) - 0.5f) * 2f * sway;
            float pitch = (Mathf.PerlinNoise(0.7f, t * 0.6f) - 0.5f) * 2f * sway;
            rotation = look * Quaternion.Euler(pitch, yaw, 0f);
            forward = rotation * Vector3.forward;
            Vector3 flat = Vector3.ProjectOnPlane(forward, Vector3.up);
            if (flat.sqrMagnitude < 0.001f) flat = _player.transform.forward;
            position = HeadPosition() + flat.normalized * 0.16f;
        }

        private void OnBeginCameraRendering(ScriptableRenderContext context, Camera camera) => PlaceCamera(camera);
        private void OnPreCullCamera(Camera camera) => PlaceCamera(camera);

        // Runs after Cinemachine has placed the camera this frame, so it simply overrides it while looking.
        private void PlaceCamera(Camera camera)
        {
            if (!_active || camera == null || camera != Camera.main || _player == null) return;
            if (_cam != camera)
            {
                if (_cam != null && _savedNear > 0f) _cam.nearClipPlane = _savedNear;
                _cam = camera;
                _savedNear = camera.nearClipPlane;
            }
            EyePose(camera, out Vector3 pos, out _, out Quaternion rot);
            camera.transform.SetPositionAndRotation(pos, rot);
            camera.fieldOfView = Mathf.Clamp(S.binoBaseFov / Mathf.Max(1f, _zoom), 2f, 60f);
            camera.nearClipPlane = 0.03f;
        }

        // ------------------------------------------------------------------ targets and marks

        private void RefreshTargets()
        {
            _targets.Clear();
            _targetByTransform.Clear();
            foreach (var e in EnemyController.Active)
            {
                if (e == null || e.IsDead) continue;
                AddTarget(e.transform, MarkKind.Enemy, EnemyLabel(e), 0.6f, Vector3.up * 1.2f, e);
            }
            foreach (var c in FindObjectsByType<EnemyCampPOI>()) AddBounded(c.transform, MarkKind.Camp, "Düşman kampı");
            foreach (var l in FindObjectsByType<LighthousePOI>()) AddBounded(l.transform, MarkKind.Lighthouse, "Fener");
            foreach (var w in FindObjectsByType<WreckSalvage>()) AddBounded(w.transform, MarkKind.Wreck, "Araç enkazı");
            foreach (var ch in FindObjectsByType<LootChest>()) AddBounded(ch.transform, MarkKind.Chest, "Sandık");
        }

        private static string EnemyLabel(EnemyController e)
        {
            switch (e.WeaponKind)
            {
                case WeaponType.Pistol: return "Asker (tabanca)";
                case WeaponType.Melee: return "Asker (yakın dövüş)";
                default: return "Asker";
            }
        }

        private void AddBounded(Transform t, MarkKind kind, string label)
        {
            if (t == null || !t.gameObject.activeInHierarchy) return;
            // Size and centre from the renderers (a lighthouse is tall, a chest is small)
            var renderers = t.GetComponentsInChildren<Renderer>();
            Bounds b = new Bounds(t.position, Vector3.one);
            bool any = false;
            foreach (var r in renderers)
            {
                if (r == null || !r.enabled || r is ParticleSystemRenderer) continue;
                if (!any) { b = r.bounds; any = true; }
                else b.Encapsulate(r.bounds);
            }
            float radius = any ? Mathf.Clamp(b.extents.magnitude * 0.6f, 0.4f, 25f) : 1f;
            if (kind == MarkKind.Camp) radius = Mathf.Max(radius, 8f);
            AddTarget(t, kind, label, radius, t.InverseTransformPoint(any ? b.center : t.position + Vector3.up), null);
        }

        private void AddTarget(Transform t, MarkKind kind, string label, float radius, Vector3 localCenter, EnemyController enemy)
        {
            if (t == null || _targetByTransform.ContainsKey(t)) return;
            var target = new Target { transform = t, kind = kind, label = label, radius = radius, localCenter = localCenter, enemy = enemy };
            // Keep the existing mark's target object so marks and targets agree
            foreach (var m in _marks) if (m.target.transform == t) { target = m.target; target.label = label; break; }
            _targets.Add(target);
            _targetByTransform[t] = target;
        }

        private void UpdateFocus(Vector3 eye, Vector3 forward)
        {
            _refreshTimer -= Time.deltaTime;
            if (_refreshTimer <= 0f) { _refreshTimer = 1f; RefreshTargets(); }

            float fov = S.binoBaseFov / Mathf.Max(1f, _zoom);
            float centreAngle = fov * 0.08f; // the middle of the view
            Target best = null;
            float bestScore = float.MaxValue;
            foreach (var t in _targets)
            {
                if (t.transform == null) continue;
                if (t.enemy != null && t.enemy.IsDead) continue;
                Vector3 p = t.Point;
                Vector3 to = p - eye;
                float dist = to.magnitude;
                if (dist < 1f || dist > S.binoMaxRange) continue;
                float angle = Vector3.Angle(forward, to);
                float allowed = Mathf.Max(centreAngle, Mathf.Atan(t.radius / dist) * Mathf.Rad2Deg);
                if (angle > allowed) continue;
                float score = angle / allowed + dist * 0.0005f;
                if (score >= bestScore) continue;
                if (!LineOfSight(eye, p, t.transform)) continue;
                best = t;
                bestScore = score;
            }

            if (best != _focus)
            {
                // A short grace so sway does not reset the timer
                if (best == null && _focus != null)
                {
                    if (_focusLostAt < 0f) _focusLostAt = Time.time;
                    if (Time.time - _focusLostAt < 0.25f) return;
                }
                _focus = best;
                _focusTime = 0f;
            }
            _focusLostAt = -1f;
            if (_focus == null) return;

            _focusTime += Time.deltaTime;
            if (_focusTime >= S.binoMarkSeconds) MarkTarget(_focus);
        }

        private bool LineOfSight(Vector3 from, Vector3 to, Transform target)
        {
            Vector3 dir = to - from;
            float dist = dir.magnitude;
            var hits = Physics.RaycastAll(from, dir / dist, dist, ~0, QueryTriggerInteraction.Ignore);
            foreach (var h in hits)
            {
                if (h.transform == null) continue;
                if (_player != null && h.transform.IsChildOf(_player.transform)) continue;
                if (h.transform.IsChildOf(target)) continue; // the target itself
                // Anything else in front closer than ~1.5 m to the target counts as the target itself (bounds centre inside walls)
                if (dist - h.distance < 1.5f) continue;
                return false;
            }
            return true;
        }

        private Mark FindMark(Target t)
        {
            foreach (var m in _marks) if (m.target == t || (m.target.transform != null && m.target.transform == t.transform)) return m;
            return null;
        }

        /// <summary>Number of marks still shown.</summary>
        public int MarkCount => _marks.Count;

        /// <summary>Debug/test: marks the nearest living enemy (as if looked at long enough). Returns its label or null.</summary>
        public string DebugMarkNearestEnemy()
        {
            RefreshTargets();
            Target best = null;
            float bestDist = float.MaxValue;
            Vector3 p = SurvivalDirector.PlayerPosition;
            foreach (var t in _targets)
            {
                if (t.kind != MarkKind.Enemy || t.transform == null) continue;
                float d = Vector3.Distance(p, t.Point);
                if (d < bestDist) { bestDist = d; best = t; }
            }
            if (best == null) return null;
            MarkTarget(best);
            return MarkText(FindMark(best));
        }

        public bool IsMarked(Transform t)
        {
            foreach (var m in _marks) if (m.target.transform == t) return true;
            return false;
        }

        private void MarkTarget(Target t)
        {
            var existing = FindMark(t);
            if (existing != null)
            {
                existing.expiresAt = Time.time + S.binoMarkLifetime;
                return;
            }
            var mark = new Mark { target = t, lastPoint = t.Point, expiresAt = Time.time + S.binoMarkLifetime };
            _marks.Add(mark);
            SurvivalNotify.Show($"İşaretlendi: {MarkText(mark)}", new Color(0.75f, 0.95f, 1f), 2.5f);
        }

        private void ExpireMarks()
        {
            for (int i = _marks.Count - 1; i >= 0; i--)
            {
                var m = _marks[i];
                if (m.target.transform != null) m.lastPoint = m.target.Point;
                if (Time.time > m.expiresAt) _marks.RemoveAt(i);
            }
        }

        private string MarkText(Mark m)
        {
            Vector3 from = SurvivalDirector.PlayerPosition;
            int meters = Mathf.RoundToInt(Vector3.Distance(from, m.lastPoint));
            string text = $"{m.target.label} · {meters} m";
            var e = m.target.enemy;
            if (e != null)
            {
                if (e.IsDead) text += " · ölü";
                else text += " · " + SuspicionText(e, out _);
            }
            return text;
        }

        private static string SuspicionText(EnemyController e, out Color color)
        {
            if (e.State == EnemyController.EnemyState.Combat) { color = new Color(1f, 0.35f, 0.3f); return "alarm"; }
            if (e.State == EnemyController.EnemyState.Suspicious || e.suspicionMeter > 10f) { color = new Color(1f, 0.85f, 0.3f); return "şüpheli"; }
            color = new Color(0.6f, 1f, 0.6f);
            return "sakin";
        }

        // ------------------------------------------------------------------ lens glint

        private void UpdateGlint(Vector3 eye, Vector3 forward)
        {
            var day = DayNightCycle.Instance;
            if (day == null || !day.IsDay || day.sunLight == null || !day.sunLight.enabled) return;
            Vector3 toSun = -day.sunLight.transform.forward;
            if (toSun.y <= 0.02f) return;
            if (Vector3.Angle(forward, toSun) > S.binoGlintSunAngle) return;

            _glintTimer -= Time.deltaTime;
            float dt = Time.deltaTime;
            float halfFov = S.binoBaseFov / Mathf.Max(1f, _zoom) * 0.5f;
            Vector3 source = SurvivalDirector.PlayerPosition;
            bool checkSight = _glintTimer <= 0f;
            if (checkSight) _glintTimer = 0.25f;
            foreach (var e in EnemyController.Active)
            {
                if (e == null || e.IsDead) continue;
                Vector3 p = e.transform.position + Vector3.up * 1.4f;
                Vector3 to = p - eye;
                float dist = to.magnitude;
                if (dist > S.binoGlintRange || Vector3.Angle(forward, to) > halfFov) continue;
                if (!_glintSeen.TryGetValue(e, out bool seen) || checkSight)
                {
                    seen = LineOfSight(eye, p, e.transform);
                    _glintSeen[e] = seen;
                }
                if (seen) e.AddGlintSuspicion(S.binoGlintSuspicionPerSecond * dt, S.binoGlintMaxSuspicion, source);
            }
        }

        private readonly Dictionary<EnemyController, bool> _glintSeen = new Dictionary<EnemyController, bool>();

        // ------------------------------------------------------------------ HUD

        private Texture2D _maskTex;
        private int _maskW, _maskH;
        private GUIStyle _label, _small;

        private void EnsureMask()
        {
            int w = Mathf.Max(64, Screen.width / 4), h = Mathf.Max(64, Screen.height / 4);
            if (_maskTex != null && _maskW == w && _maskH == h) return;
            if (_maskTex != null) Destroy(_maskTex);
            _maskW = w; _maskH = h;
            _maskTex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            float r = h * 0.47f;
            float cy = h * 0.5f;
            float cx1 = w * 0.5f - r * 0.62f, cx2 = w * 0.5f + r * 0.62f;
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float d1 = Mathf.Sqrt((x - cx1) * (x - cx1) + (y - cy) * (y - cy));
                    float d2 = Mathf.Sqrt((x - cx2) * (x - cx2) + (y - cy) * (y - cy));
                    float d = Mathf.Min(d1, d2) / r;            // 1 = lens edge
                    float a = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.97f, 1.01f, d)); // hard-ish edge
                    float vignette = 0.45f * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.7f, 0.98f, d));
                    a = Mathf.Max(a, vignette);
                    px[y * w + x] = new Color32(0, 0, 0, (byte)Mathf.RoundToInt(Mathf.Clamp01(a) * 255f));
                }
            _maskTex.SetPixels32(px);
            _maskTex.Apply();
        }

        private void EnsureStyles()
        {
            if (_label != null) return;
            _label = new GUIStyle(GUI.skin.label) { fontSize = 14, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            _small = new GUIStyle(GUI.skin.label) { fontSize = 13, alignment = TextAnchor.MiddleCenter };
        }

        private void OnGUI()
        {
            if (Event.current.type != EventType.Repaint) return;
            if (!_active && _marks.Count == 0) return;
            EnsureStyles();
            GUI.depth = 5;
            var cam = Camera.main;

            if (_active)
            {
                EnsureMask();
                GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), _maskTex, ScaleMode.StretchToFill, true);

                float cx = Screen.width * 0.5f, cy = Screen.height * 0.5f;
                // Small centre dot
                var old = GUI.color;
                GUI.color = new Color(1f, 1f, 1f, 0.55f);
                GUI.DrawTexture(new Rect(cx - 2f, cy - 2f, 4f, 4f), Texture2D.whiteTexture);
                // Focus progress
                if (_focus != null && FindMark(_focus) == null)
                {
                    float p = Mathf.Clamp01(_focusTime / Mathf.Max(0.1f, S.binoMarkSeconds));
                    GUI.color = new Color(0f, 0f, 0f, 0.6f);
                    GUI.DrawTexture(new Rect(cx - 50f, cy + 26f, 100f, 6f), Texture2D.whiteTexture);
                    GUI.color = new Color(0.75f, 0.95f, 1f, 0.95f);
                    GUI.DrawTexture(new Rect(cx - 50f, cy + 26f, 100f * p, 6f), Texture2D.whiteTexture);
                    Shadowed(new Rect(cx - 120f, cy + 34f, 240f, 22f), "İşaretleniyor...", _small, Color.white);
                }
                GUI.color = old;
                Shadowed(new Rect(cx - 200f, Screen.height - 70f, 400f, 22f), $"{_zoom:0.0}x", _label, Color.white);
                Shadowed(new Rect(cx - 300f, Screen.height - 48f, 600f, 22f),
                    _toggled ? "Fare tekerleği: yakınlaştır · Z ya da Kullan: indir · Ortadakine bakarak işaretle"
                             : "Fare tekerleği: yakınlaştır · Z bırak: indir · Ortadakine bakarak işaretle", _small, new Color(1f, 1f, 1f, 0.8f));
            }

            if (cam == null) return;
            foreach (var m in _marks)
            {
                Vector3 sp = cam.WorldToScreenPoint(m.lastPoint + Vector3.up * (m.target.kind == MarkKind.Enemy ? 0.9f : 1.5f));
                if (sp.z <= 0f) continue;
                float x = sp.x, y = Screen.height - sp.y;
                if (x < -50f || x > Screen.width + 50f || y < -50f || y > Screen.height + 50f) continue;
                Color c = new Color(0.75f, 0.95f, 1f);
                if (m.target.enemy != null && !m.target.enemy.IsDead) SuspicionText(m.target.enemy, out c);
                float fade = Mathf.Clamp01((m.expiresAt - Time.time) / 3f);
                c.a = Mathf.Lerp(0.25f, 1f, fade);
                var old = GUI.color;
                GUI.color = c;
                GUI.DrawTexture(new Rect(x - 4f, y - 4f, 8f, 8f), Texture2D.whiteTexture); // diamond-ish marker
                GUI.color = old;
                Shadowed(new Rect(x - 150f, y - 28f, 300f, 22f), MarkText(m), _label, c);
            }
        }

        private static void Shadowed(Rect r, string text, GUIStyle style, Color color)
        {
            var old = style.normal.textColor;
            style.normal.textColor = new Color(0f, 0f, 0f, color.a * 0.85f);
            GUI.Label(new Rect(r.x + 1f, r.y + 1f, r.width, r.height), text, style);
            style.normal.textColor = color;
            GUI.Label(r, text, style);
            style.normal.textColor = old;
        }
    }
}
