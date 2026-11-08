using UnityEngine;
using UnityEngine.InputSystem;
using EndlessSurvival.Inventory;

namespace EndlessSurvival.Survival
{
    /// <summary>
    /// Smoking (Docs/Smoking_System.md): a fast but costly way to lower stress. The character always has a lighter,
    /// so a cigarette is all it takes. [J] or "Use" on the cigarette in the inventory starts it: the Smoking clip
    /// plays, the player stands still and cannot fight. Moving, taking damage or an alerted enemy nearby stops it
    /// (within the first seconds the cigarette is given back, later it is spent). When it ends: stress -20 at once,
    /// -10 more over two game hours, stamina regenerates a little slower for a while. Not during panic.
    /// Anxiety is not touched (it goes away with rest). No addiction. Values: SurvivalSettings > Smoking.
    /// </summary>
    public class SmokingAction : MonoBehaviour
    {
        public const string CigaretteItem = "Item_Cigarette";

        public static SmokingAction Instance { get; private set; }

        /// <summary>True while the player is smoking.</summary>
        public static bool Active => Instance != null && Instance._active;
        public static float Progress => Instance != null && Instance._active ? Mathf.Clamp01(Instance._elapsed / Mathf.Max(0.1f, Instance._duration)) : 0f;

        /// <summary>Multiplier for stamina regeneration (lower for a while after smoking).</summary>
        public static float StaminaRegenMultiplier =>
            Instance != null && GameClock.TotalHours < Instance._penaltyUntil ? 1f - S.smokeStaminaRegenPenalty : 1f;

        private static SurvivalSettings S => SurvivalSettings.Instance;

        private bool _active;
        private float _elapsed;
        private float _duration;
        private bool _taken;          // a cigarette was taken from the backpack
        private float _afterLeft;     // stress still to remove slowly
        private double _penaltyUntil; // game hours
        private float _dangerCheck;
        private GameObject _handModel;
        private PlayerHealth _health;

        private void Awake()
        {
            Instance = this;
            PlayerInventory.CustomUse += OnInventoryUse;
        }

        private void OnDestroy()
        {
            PlayerInventory.CustomUse -= OnInventoryUse;
            if (_health != null) _health.Damaged -= OnDamaged;
            HideHandModel();
            if (Instance == this) Instance = null;
        }

        // ---------------- start ----------------

        private static ItemDefinition Cigarette => S != null ? S.Item(CigaretteItem) : null;

        private string OnInventoryUse(ItemDefinition item, PlayerInventory user)
        {
            if (item == null || item != Cigarette) return null;
            var msg = TryStart();
            return msg ?? "Sigara yakıldı";
        }

        /// <summary>Starts smoking. Returns an error message, or null when it started.</summary>
        public string TryStart()
        {
            if (_active) return "Zaten sigara içiyorsun";
            var player = SurvivalDirector.Player;
            if (player == null || !player.activeInHierarchy) return "Araçtan in";
            if (SurvivalDirector.PlayerInVehicle) return "Araçtan in";
            var vitals = PlayerVitals.Instance;
            if (vitals != null && vitals.Panicking) return "Ellerin titriyor, şimdi yakamazsın";
            if (vitals != null && vitals.ToiletActive) return "Önce işini bitir";
            var health = player.GetComponent<PlayerHealth>();
            if (health != null && health.IsDead) return null;
            if (SurvivalDirector.DangerNearby(player.transform.position, S.smokeDangerRadius)) return "Yakında tehlike var, şimdi olmaz";

            var cig = Cigarette;
            bool free = SurvivalTest.FreeResources;
            if (!free && (cig == null || SurvivalDirector.CountItem(cig) <= 0)) return "Sigaran yok";
            _taken = !free && SurvivalDirector.RemoveItem(cig, 1) > 0;

            _active = true;
            _elapsed = 0f;
            _dangerCheck = 0f;
            var anim = PlayerSurvivalAnimator.Instance;
            float clip = anim != null ? anim.ClipLength(SurvivalAnimClips.Smoke) : 0f;
            _duration = clip > 0.5f ? Mathf.Min(clip, S.smokeMaxSeconds) : Mathf.Min(8f, S.smokeMaxSeconds);

            if (_health != health)
            {
                if (_health != null) _health.Damaged -= OnDamaged;
                _health = health;
                if (_health != null) _health.Damaged += OnDamaged;
            }
            ShowHandModel(player);
            SurvivalNotify.Show("Sigara yakıldı (hareket edersen söner)");
            return null;
        }

        // ---------------- running ----------------

        private void Update()
        {
            if (!SurvivalUIState.AnyOpen && SKeys.Down(Key.J))
            {
                if (_active) Stop("Sigarayı söndürdün");
                else
                {
                    var msg = TryStart();
                    if (msg != null) SurvivalNotify.Warn(msg);
                }
            }

            // Slow relief after the cigarette
            if (_afterLeft > 0f && PlayerVitals.Instance != null)
            {
                float rate = S.smokeAfterRelief / Mathf.Max(0.1f, S.smokeAfterHours);
                float step = Mathf.Min(_afterLeft, rate * GameClock.DeltaHours);
                if (step > 0f)
                {
                    PlayerVitals.Instance.AddStressRelief(step);
                    _afterLeft -= step;
                }
            }

            if (!_active) return;
            var player = SurvivalDirector.Player;
            if (player == null || !player.activeInHierarchy || SurvivalDirector.PlayerInVehicle) { Stop(null); return; }
            if (_health != null && _health.IsDead) { Stop(null); return; }
            var vitals = PlayerVitals.Instance;
            if (vitals != null && vitals.Panicking) { Stop("Panik! Sigara elinden düştü"); return; }

            var inputs = player.GetComponent<StarterAssets.StarterAssetsInputs>();
            if (inputs != null && !SurvivalUIState.AnyOpen && (inputs.move.sqrMagnitude > 0.04f || inputs.jump))
            {
                Stop("Hareket ettin, sigara söndü");
                return;
            }

            _dangerCheck -= Time.deltaTime;
            if (_dangerCheck <= 0f)
            {
                _dangerCheck = 0.4f;
                if (SurvivalDirector.DangerNearby(player.transform.position, S.smokeDangerRadius))
                {
                    Stop("Tehlike! Sigarayı attın");
                    return;
                }
            }

            _elapsed += Time.deltaTime;
            if (_elapsed >= _duration) Finish();
        }

        private void OnDamaged(float amount)
        {
            if (_active && amount > 0f) Stop("Vuruldun, sigara elinden düştü");
        }

        private void Finish()
        {
            _active = false;
            HideHandModel();
            var vitals = PlayerVitals.Instance;
            if (vitals != null) vitals.AddStressRelief(S.smokeStressRelief);
            _afterLeft = S.smokeAfterRelief;
            _penaltyUntil = GameClock.TotalHours + S.smokeStaminaPenaltyGameMinutes / 60.0;
            SurvivalNotify.Show($"Sigarayı bitirdin, rahatladın (stres -{Mathf.RoundToInt(S.smokeStressRelief)})", new Color(0.8f, 0.9f, 1f), 3f);
        }

        /// <summary>Interrupts smoking. Early on (not lit yet) the cigarette goes back to the backpack.</summary>
        public void Stop(string message)
        {
            if (!_active) return;
            _active = false;
            HideHandModel();
            bool giveBack = _taken && _elapsed < S.smokeLightSeconds;
            if (giveBack && Cigarette != null) SurvivalDirector.GiveItem(Cigarette, 1);
            _taken = false;
            if (message != null) SurvivalNotify.Warn(giveBack ? "Sigarayı yakmadan bıraktın (pakete geri kondu)" : message);
        }

        // ---------------- placeholder cigarette in the right hand ----------------

        private void ShowHandModel(GameObject player)
        {
            HideHandModel();
            var animator = player.GetComponentInChildren<Animator>();
            if (animator == null || !animator.isHuman) return;
            var hand = animator.GetBoneTransform(HumanBodyBones.RightIndexProximal) ?? animator.GetBoneTransform(HumanBodyBones.RightHand);
            if (hand == null) return;

            _handModel = new GameObject("Cigarette (placeholder)");
            _handModel.transform.SetParent(hand, false);
            _handModel.transform.localPosition = new Vector3(0f, 0.02f, 0.01f);
            _handModel.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);

            var body = Primitive(PrimitiveType.Cylinder, _handModel.transform, new Vector3(0.009f, 0.035f, 0.009f), Vector3.zero, new Color(0.95f, 0.93f, 0.88f), false);
            Primitive(PrimitiveType.Sphere, _handModel.transform, new Vector3(0.011f, 0.011f, 0.011f), new Vector3(0f, 0.036f, 0f), new Color(1f, 0.45f, 0.1f), true);
            if (body == null) HideHandModel();
        }

        private static readonly System.Collections.Generic.Dictionary<Color, Material> Materials = new System.Collections.Generic.Dictionary<Color, Material>();

        private static GameObject Primitive(PrimitiveType type, Transform parent, Vector3 scale, Vector3 localPos, Color color, bool glow)
        {
            var go = GameObject.CreatePrimitive(type);
            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = scale;
            var r = go.GetComponent<Renderer>();
            if (r != null)
            {
                if (!Materials.TryGetValue(color, out var m) || m == null)
                {
                    var shader = Shader.Find(glow ? "Universal Render Pipeline/Unlit" : "Universal Render Pipeline/Lit");
                    if (shader == null) shader = Shader.Find(glow ? "Unlit/Color" : "Standard");
                    m = shader != null ? new Material(shader) : null;
                    if (m != null)
                    {
                        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
                        m.color = color;
                    }
                    Materials[color] = m;
                }
                if (m != null) r.sharedMaterial = m;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            return go;
        }

        private void HideHandModel()
        {
            if (_handModel != null) Destroy(_handModel);
            _handModel = null;
        }
    }
}
