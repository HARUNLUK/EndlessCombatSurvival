using UnityEngine;
using EndlessSurvival.Inventory;

namespace EndlessCombat.Combat
{
    /// <summary>
    /// The bow (Combat_Action_Plan.md 2, 8; first version 2026-11-17). Its own slot on the back (EquipSlot.Bow, with a
    /// quiver), held with both hands. Picked on the LB map; LB tap puts it back on the back.
    ///   LT        aim (the character turns with the camera, a small crosshair)
    ///   RT held   the string is pulled: power and accuracy grow to full in DrawSeconds; held longer than
    ///             SteadySeconds the aim starts to shake and stamina drains
    ///   RT up     the arrow flies (ArrowProjectile: falls with gravity, silent, head hits count more)
    /// Arrows are a bag item (Item_Arrow), not an ammo pool: one arrow per shot. Some can be picked up again.
    /// No animation: the bow is held upright in the left hand after the animation, like the torch.
    /// </summary>
    [DisallowMultipleComponent]
    public class PlayerBow : MonoBehaviour
    {
        public static float DrawSeconds = 0.8f, SteadySeconds = 3f, StaminaPerSecond = 6f;
        /// <summary>Seconds the bow takes from the back to the hands (shots wait for it).</summary>
        public static float DrawTime = 0.6f;
        public static float SpeedMin = 16f, SpeedMax = 58f, DamageMin = 18f, DamageMax = 75f, HeadMultiplier = 2.5f;
        public static float SpreadFull = 0.4f, SpreadWeak = 5f, ShakeDegrees = 3f, RecoverChance = 0.5f;
        private static bool _tuningRegistered;

        private PlayerInventory _inventory;
        private PlayerCombatState _state;
        private Animator _animator;
        private Camera _camera;
        private GameObject _visual;     // the bow
        private GameObject _quiver;     // on the back with arrows
        private GameObject _nocked;     // arrow on the string while pulling
        private ItemDefinition _item;
        private float _pull;            // 0..1
        private float _pullHeld;        // seconds at full pull
        private bool _rtWas;
        private GUIStyle _style;

        public bool HasBow => _item != null;
        public ItemDefinition Item => _item;
        /// <summary>The bow is in both hands (not on the back).</summary>
        public bool InHands { get; private set; }
        public float Pull01 => _pull;

        public static PlayerBow GetOrAdd(GameObject owner)
        {
            var b = owner.GetComponent<PlayerBow>();
            return b != null ? b : owner.AddComponent<PlayerBow>();
        }

        private void Awake()
        {
            _inventory = GetComponent<PlayerInventory>();
            _state = PlayerCombatState.GetOrAdd(gameObject);
            if (_tuningRegistered) return;
            _tuningRegistered = true;
            const string P = "Dövüş";
            Tuning.TuningPanel.Register(P, "Yay: germe süresi (sn)", () => DrawSeconds, v => DrawSeconds = v, 0.2f, 2f, 0.05f);
            Tuning.TuningPanel.Register(P, "Yay: titremeden tutma (sn)", () => SteadySeconds, v => SteadySeconds = v, 0.5f, 8f, 0.25f);
            Tuning.TuningPanel.Register(P, "Yay: hasar (tam gerili)", () => DamageMax, v => DamageMax = v, 10f, 200f, 5f);
            Tuning.TuningPanel.Register(P, "Yay: ok hızı (tam gerili)", () => SpeedMax, v => SpeedMax = v, 20f, 120f, 2f);
            Tuning.TuningPanel.Register(P, "Yay: kafa çarpanı", () => HeadMultiplier, v => HeadMultiplier = v, 1f, 5f, 0.1f);
            Tuning.TuningPanel.Register(P, "Yay: oku geri alma şansı", () => RecoverChance, v => RecoverChance = v, 0f, 1f, 0.05f);
        }

        // ------------------------------------------------------------------ arrows

        public static ItemDefinition ArrowItem()
        {
            var s = EndlessSurvival.Survival.SurvivalSettings.Instance;
            return s != null ? s.Item("Item_Arrow") : null;
        }

        public int Arrows
        {
            get
            {
                ItemDefinition a = ArrowItem();
                if (_inventory == null) _inventory = GetComponent<PlayerInventory>();
                return a != null && _inventory != null ? _inventory.Storage.GetCount(a) : 0;
            }
        }

        // ------------------------------------------------------------------ slot (PlayerEquipment), hands (PlayerLoadout)

        /// <summary>The bow slot changed (null: empty). Builds the model on the back.</summary>
        public void SetItem(ItemDefinition item)
        {
            if (_item == item && (item == null || _visual != null)) return;
            _item = item;
            InHands = false;
            _pull = 0f;
            DestroyVisuals();
            if (item == null) return;
            _visual = item.heldModel != null ? Instantiate(item.heldModel) : Placeholder(new Vector3(0.03f, 1.1f, 0.05f));
            _visual.name = "Bow_" + item.DisplayName;
            Clean(_visual);
            _visual.transform.SetParent(transform, false);
            ItemDefinition arrow = ArrowItem();
            GameObject quiverModel = arrow != null && arrow.heldModel != null ? arrow.heldModel.transform.Find("QuiverModel")?.gameObject : null;
            _quiver = quiverModel != null ? Instantiate(quiverModel) : Placeholder(new Vector3(0.1f, 0.5f, 0.1f));
            _quiver.name = "Quiver";
            _quiver.SetActive(true);
            Clean(_quiver);
            _quiver.transform.SetParent(transform, false);
        }

        /// <summary>The bow comes to both hands (the caller has emptied them: drop rule in PlayerLoadout).</summary>
        public void Draw()
        {
            if (_item == null) return;
            InHands = true;
            _pull = 0f;
            _rtWas = GameInput.FireHeld; // a held RT from before does not start pulling
        }

        /// <summary>Back on the back.</summary>
        public void Stow()
        {
            InHands = false;
            _pull = 0f;
            ShowNocked(false);
        }

        /// <summary>The bow falls to the ground: its slot empties (the caller spawns the pickup).</summary>
        public void Dropped()
        {
            InHands = false;
            _pull = 0f;
            ShowNocked(false);
        }

        public void Hide(bool hidden)
        {
            if (_visual != null) _visual.SetActive(!hidden);
            if (_quiver != null) _quiver.SetActive(!hidden);
        }

        // ------------------------------------------------------------------ aim, pull, release

        private void Update()
        {
            if (_camera == null) _camera = Camera.main;
            if (!InHands || _item == null) { ShowNocked(false); return; }
            if (_state != null && _state.InputBlocked)
            {
                _pull = 0f;
                _rtWas = false;
                ShowNocked(false);
                return;
            }
            // RT on the bow is never a fist strike / throw: clear the buffered press
            if (_state != null) { _state.TryConsume(CombatIntent.Throw); _state.TryConsume(CombatIntent.Fire); }

            bool busy = _state != null && (_state.Drawing || _state.Current == CombatAction.Stagger);
            bool aiming = GameInput.AimHeld && !busy;
            bool rt = GameInput.FireHeld && !busy;
            if (aiming || _pull > 0f) FaceCamera();

            if (rt && Arrows > 0)
            {
                if (!_rtWas || _pull > 0f)
                {
                    _pull = Mathf.MoveTowards(_pull, 1f, Time.deltaTime / Mathf.Max(0.05f, DrawSeconds));
                    if (_pull >= 1f)
                    {
                        _pullHeld += Time.deltaTime;
                        if (_pullHeld > SteadySeconds) CombatHooks.TrySpendStamina(StaminaPerSecond * Time.deltaTime);
                    }
                }
            }
            else if (_pull > 0f)
            {
                if (!busy && _pull >= 0.12f) Release(aiming);
                _pull = 0f;
                _pullHeld = 0f;
            }
            else if (rt && _rtWas == false && Arrows <= 0)
                EndlessSurvival.Survival.SurvivalNotify.Show("Ok yok (Üretim: odun + taş = 3 ok)", 1.5f);
            _rtWas = GameInput.FireHeld;
            ShowNocked(_pull > 0f);
        }

        private void FaceCamera()
        {
            if (_camera == null) return;
            Vector3 f = _camera.transform.forward;
            f.y = 0f;
            if (f.sqrMagnitude < 0.001f) return;
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(f.normalized), Time.deltaTime * 14f);
        }

        private float Shake => _pullHeld > SteadySeconds ? Mathf.Clamp01((_pullHeld - SteadySeconds) / 3f) : 0f;

        private void Release(bool aimed)
        {
            ItemDefinition arrow = ArrowItem();
            if (arrow == null || _inventory == null || _inventory.Storage.Remove(arrow, 1) <= 0) return;

            Vector3 origin = transform.position + Vector3.up * 1.45f + transform.forward * 0.55f;
            Vector3 target = AimPoint(origin);
            Vector3 dir = (target - origin).normalized;
            float spread = Mathf.Lerp(SpreadWeak, SpreadFull, _pull) * (aimed ? 1f : 2.5f) + ShakeDegrees * Shake;
            dir = Quaternion.Euler(Random.Range(-spread, spread) * 0.5f, Random.Range(-spread, spread) * 0.5f, 0f) * dir;
            float speed = Mathf.Lerp(SpeedMin, SpeedMax, _pull);
            float damage = Mathf.Lerp(DamageMin, DamageMax, _pull * _pull);
            ArrowProjectile.Launch(origin, dir * speed, damage, HeadMultiplier, RecoverChance, gameObject, arrow);
            EndlessCombat.Audio.GameSound.Play(EndlessCombat.Audio.SoundId.BowRelease, origin, Mathf.Clamp01(_pull), null, true);
            if (_state != null) _state.NoteFired(0.25f);
        }

        /// <summary>Where the camera centre looks (the player is skipped).</summary>
        private Vector3 AimPoint(Vector3 origin)
        {
            if (_camera == null) return origin + transform.forward * 60f;
            Ray ray = new Ray(_camera.transform.position, _camera.transform.forward);
            foreach (var hit in Physics.RaycastAll(ray, 300f, ~0, QueryTriggerInteraction.Ignore))
            {
                if (hit.collider.transform.IsChildOf(transform)) continue;
                if (Vector3.Dot(hit.point - origin, ray.direction) < 0.5f) continue;
                return hit.point;
            }
            return ray.origin + ray.direction * 300f;
        }

        // ------------------------------------------------------------------ models

        private void LateUpdate()
        {
            if (_visual == null || !_visual.activeSelf) return;
            if (_animator == null) _animator = GetComponentInChildren<Animator>();
            bool human = _animator != null && _animator.isHuman;
            Vector3 up = Vector3.up;
            Vector3 fwd = Vector3.ProjectOnPlane(transform.forward, up).normalized;
            Vector3 right = Vector3.Cross(up, fwd);

            // Quiver: on the back, right shoulder
            Transform chest = human ? (_animator.GetBoneTransform(HumanBodyBones.Chest) ?? _animator.GetBoneTransform(HumanBodyBones.Spine)) : null;
            Vector3 back = chest != null ? chest.position : transform.position + up * 1.3f;
            if (_quiver != null)
                _quiver.transform.SetPositionAndRotation(back - fwd * 0.17f + right * 0.13f + up * 0.02f,
                    Quaternion.LookRotation(fwd, up) * Quaternion.Euler(0f, 0f, -18f));

            if (!InHands)
            {
                // Across the back, string toward the body
                _visual.transform.SetPositionAndRotation(back - fwd * 0.2f - right * 0.02f,
                    Quaternion.LookRotation(-fwd, up) * Quaternion.Euler(0f, 0f, 32f));
                return;
            }

            // In the left hand, upright; aimed: held out toward the aim, the string drawn back
            Transform hand = human ? _animator.GetBoneTransform(HumanBodyBones.LeftHand) : null;
            Transform middle = human ? _animator.GetBoneTransform(HumanBodyBones.LeftMiddleProximal) : null;
            Vector3 palm = hand != null && middle != null ? Vector3.Lerp(hand.position, middle.position, 0.75f)
                         : transform.position + up * 1.1f - right * 0.3f + fwd * 0.3f;
            Vector3 aimDir = _camera != null ? _camera.transform.forward : fwd;
            bool raised = GameInput.AimHeld || _pull > 0f;
            Vector3 pos = raised ? transform.position + up * 1.45f + fwd * 0.55f - right * 0.02f : palm;
            Vector3 face = raised ? aimDir : fwd;
            _visual.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(face, up) * Quaternion.Euler(0f, 0f, raised ? -8f : 0f));

            if (_nocked != null && _nocked.activeSelf)
            {
                // The arrow lies on the bow, its back at the string (pulled back by _pull)
                Vector3 bowFwd = _visual.transform.forward;
                _nocked.transform.SetPositionAndRotation(pos + bowFwd * (0.185f - 0.45f * _pull), Quaternion.LookRotation(bowFwd, up));
                Transform str = _visual.transform.Find("String");
                if (str != null) str.localPosition = new Vector3(0f, 0f, -0.19f - 0.45f * _pull);
            }
            else
            {
                Transform str = _visual.transform.Find("String");
                if (str != null) str.localPosition = new Vector3(0f, 0f, -0.19f);
            }
        }

        private void ShowNocked(bool show)
        {
            if (show && _nocked == null)
            {
                ItemDefinition a = ArrowItem();
                Transform model = a != null && a.heldModel != null ? a.heldModel.transform.Find("ArrowModel") : null;
                _nocked = model != null ? Instantiate(model.gameObject) : Placeholder(new Vector3(0.01f, 0.01f, 0.75f));
                _nocked.name = "NockedArrow";
                _nocked.SetActive(true);
                Clean(_nocked);
                _nocked.transform.SetParent(transform, false);
            }
            if (_nocked != null && _nocked.activeSelf != show) _nocked.SetActive(show);
        }

        private static GameObject Placeholder(Vector3 size)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.transform.localScale = size;
            return go;
        }

        private static void Clean(GameObject go)
        {
            foreach (var c in go.GetComponentsInChildren<Collider>(true)) Destroy(c);
            foreach (var r in go.GetComponentsInChildren<Renderer>(true)) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        private void DestroyVisuals()
        {
            if (_visual != null) Destroy(_visual);
            if (_quiver != null) Destroy(_quiver);
            if (_nocked != null) Destroy(_nocked);
            _visual = _quiver = _nocked = null;
        }

        private void OnDestroy() => DestroyVisuals();

        // ------------------------------------------------------------------ small HUD (placeholder)

        private void OnGUI()
        {
            if (!InHands || _item == null || (_state != null && _state.InputBlocked)) return;
            if (_style == null)
            {
                _style = new GUIStyle(GUI.skin.label) { fontSize = 16, alignment = TextAnchor.LowerRight };
                _style.normal.textColor = new Color(1f, 1f, 1f, 0.85f);
            }
            float cx = Screen.width * 0.5f, cy = Screen.height * 0.5f;
            if (GameInput.AimHeld || _pull > 0f)
            {
                // Crosshair: closes as the string is pulled
                float gap = Mathf.Lerp(18f, 3f, _pull) + 6f * Shake;
                Color c = _pull >= 1f ? new Color(1f, 0.85f, 0.4f, 0.95f) : new Color(1f, 1f, 1f, 0.85f);
                Tex(new Rect(cx - 1f, cy - 1f, 2f, 2f), c);
                Tex(new Rect(cx - gap - 8f, cy - 1f, 8f, 2f), c);
                Tex(new Rect(cx + gap, cy - 1f, 8f, 2f), c);
                Tex(new Rect(cx - 1f, cy + gap, 2f, 8f), c);
            }
            if (_pull > 0f)
            {
                Tex(new Rect(cx - 40f, cy + 34f, 80f, 5f), new Color(0f, 0f, 0f, 0.5f));
                Tex(new Rect(cx - 40f, cy + 34f, 80f * _pull, 5f), new Color(1f, 0.85f, 0.4f, 0.9f));
            }
            string hint = GameInput.Hint("Sağ tık: nişan   Sol tık basılı: ger, bırak: at", "LT: nişan   RT basılı: ger, bırak: at   LB: sırta");
            GUI.Label(new Rect(Screen.width - 620, Screen.height - 165, 600, 44), $"Elde: {_item.DisplayName}   Ok x{Arrows}\n{hint}", _style);
        }

        private static void Tex(Rect r, Color c)
        {
            Color old = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = old;
        }
    }
}
