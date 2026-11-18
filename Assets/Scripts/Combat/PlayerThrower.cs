using System.Collections;
using UnityEngine;
using EndlessSurvival.Inventory;

namespace EndlessCombat.Combat
{
    /// <summary>
    /// Rocks and grenades from the backpack, held in the right hand like a weapon (Combat_Action_Plan.md 7):
    ///   pick       the selection map's throwable part (LB held + RT; keyboard T / G). It comes to the hand
    ///              (short draw time); a weapon still in the right hand falls to the ground (PlayerLoadout.Pick).
    ///   aim        LT held: the arc is drawn
    ///   throw      RT (left click). Without LT it goes straight ahead. The next one of the same kind comes to the
    ///              hand by itself while there are more; else the hand is empty.
    ///   put away   LB tap (R hold) puts it back in the bag. Dropped (another right hand pick): it lies on the ground,
    ///              a dropped grenade does not go off.
    /// The item leaves the bag only when it is thrown or dropped. A rock makes a noise where it lands (enemies go
    /// to look); a grenade explodes after its fuse.
    /// Each scene start gives a few of each for testing (CombatContent.startingRocks / startingGrenades; set to 0 to turn off).
    /// </summary>
    [DisallowMultipleComponent]
    public class PlayerThrower : MonoBehaviour
    {
        public enum Kind { None, Rock, Grenade }

        private const int ArcPoints = 40;
        private const float ArcStep = 0.05f;

        private PlayerInventory _inventory;
        private Camera _camera;
        private LineRenderer _arc;
        private GameObject _arcEnd;
        private GameObject _handVisual;
        private ItemDefinition _handModelOf;
        private Transform _hand;
        private Kind _held = Kind.None;
        private float _nextThrowTime;
        private bool _throwing;
        private GUIStyle _style;
        private PlayerCombatState _state;

        /// <summary>True during the throw motion (PlayerCombatState: CombatAction.Throw).</summary>
        public bool IsBusy => _throwing;

        /// <summary>A rock or grenade is in the right hand (or being thrown with another one coming).</summary>
        public bool HasItem => _held != Kind.None;

        public Kind Held => _held;

        /// <summary>The throw motion starts (seconds the arms are busy). The animation driver listens.</summary>
        public event System.Action<float> ThrowStarted;

        private void Awake()
        {
            _inventory = GetComponent<PlayerInventory>();
            _state = PlayerCombatState.GetOrAdd(gameObject);
        }

        private void Start()
        {
            // Test kit at scene start. Loading a save (F9) replaces the backpack, so it does not pile up in saves.
            GiveStartingKit();
        }

        private void GiveStartingKit()
        {
            var content = CombatContent.Instance;
            if (_inventory == null || content == null) return;

            // Only in test mode (free resources) or while the scene loadout is kept for the automatic play test
            bool testKit = EndlessSurvival.Survival.SurvivalTest.FreeResources
                || PlayerPrefs.GetInt(PlayerEquipment.KeepSceneLoadoutKey, 0) == 1;
            if (!testKit) return;

            if (content.rockItem != null && content.startingRocks > 0 && _inventory.Storage.GetCount(content.rockItem) == 0)
                _inventory.Storage.Add(content.rockItem, content.startingRocks);
            if (content.grenadeItem != null && content.startingGrenades > 0 && _inventory.Storage.GetCount(content.grenadeItem) == 0)
                _inventory.Storage.Add(content.grenadeItem, content.startingGrenades);

            if (content.testItems != null)
            {
                foreach (var item in content.testItems)
                {
                    if (item == null || _inventory.Storage.GetCount(item) != 0) continue;
                    var equipment = GetComponent<PlayerEquipment>();
                    if (equipment != null && item.IsEquippable && equipment.Get(item.CarrySlot) == item) continue; // already worn
                    _inventory.AddItem(item, 1);
                }
            }
        }

        // ------------------------------------------------------------------ hand

        public static ItemDefinition ItemOf(Kind kind)
        {
            var content = CombatContent.Instance;
            if (content == null) return null;
            return kind == Kind.Rock ? content.rockItem : kind == Kind.Grenade ? content.grenadeItem : null;
        }

        public int Count(Kind kind)
        {
            ItemDefinition item = ItemOf(kind);
            return item != null && _inventory != null ? _inventory.Storage.GetCount(item) : 0;
        }

        /// <summary>The next kind for the map's throwable button: the other one when one is in the hand, else the first there is.</summary>
        public Kind NextKind()
        {
            Kind first = _held == Kind.Rock ? Kind.Grenade : Kind.Rock;
            Kind second = first == Kind.Rock ? Kind.Grenade : Kind.Rock;
            if (Count(first) > 0) return first;
            if (Count(second) > 0) return second;
            return Kind.None;
        }

        /// <summary>
        /// Takes one rock / grenade into the right hand (the loadout has already freed the hand). False when the bag
        /// has none. Another throwable in the hand goes back to the bag (switching between the two is not a drop).
        /// </summary>
        public bool Hold(Kind kind, bool draw = true)
        {
            if (kind == Kind.None) return false;
            if (Count(kind) <= 0)
            {
                EndlessSurvival.Survival.SurvivalNotify.Show(kind == Kind.Rock ? "Taş yok" : "El bombası yok", 1.5f);
                return false;
            }
            _held = kind;
            ShowHandVisual();
            if (draw && _state != null) _state.NoteDraw(WeaponDefinition.DrawThrowable);
            return true;
        }

        /// <summary>LB tap: the rock / grenade goes back to the bag.</summary>
        public void Stow()
        {
            if (_held == Kind.None) return;
            _held = Kind.None;
            ShowArc(false);
            HideHandVisual();
            if (_state != null) _state.NoteDraw(WeaponDefinition.DrawThrowable * WeaponDefinition.HolsterFactor);
        }

        /// <summary>Another right hand pick without putting it away: it falls to the ground (Combat_Action_Plan.md 5).</summary>
        public void DropHeld()
        {
            if (_held == Kind.None) return;
            ItemDefinition item = ItemOf(_held);
            _held = Kind.None;
            ShowArc(false);
            Vector3 at = _handVisual != null ? _handVisual.transform.position : transform.position + Vector3.up * 1.1f + transform.forward * 0.3f;
            HideHandVisual();
            if (item == null || _inventory == null || _inventory.dropPickupPrefab == null) return; // no pickup prefab: it stays in the bag
            if (_inventory.Storage.Remove(item, 1) <= 0) return;
            WorldPickup.Spawn(_inventory.dropPickupPrefab, item, 1, at, null);
        }

        /// <summary>Old name kept for callers: nothing is aimed by a held button any more.</summary>
        public void CancelHold() => ShowArc(false);

        private void ShowHandVisual()
        {
            if (_hand == null)
            {
                var anim = GetComponentInChildren<Animator>();
                if (anim != null && anim.isHuman) _hand = anim.GetBoneTransform(HumanBodyBones.RightHand);
            }
            ItemDefinition model = ItemOf(_held);
            if (_handVisual != null && _handModelOf != model)
            {
                Destroy(_handVisual);
                _handVisual = null;
            }
            _handModelOf = model;
            if (_handVisual == null && model != null && model.heldModel != null)
            {
                // Simple model (Basit Modelleri Kur): rock / grenade, sized in metres
                _handVisual = Instantiate(model.heldModel);
                _handVisual.name = "HeldThrowable";
                foreach (var col in _handVisual.GetComponentsInChildren<Collider>()) Destroy(col);
                _handVisual.transform.SetParent(_hand != null ? _hand : transform, false);
                _handVisual.transform.localPosition = _hand != null ? new Vector3(0f, 0.08f, 0.04f) : new Vector3(0.25f, 1.1f, 0.3f);
                _handVisual.SetActive(true);
                return;
            }
            if (_handVisual != null && model != null && model.heldModel != null)
            {
                _handVisual.transform.SetParent(_hand != null ? _hand : transform, false);
                _handVisual.SetActive(true);
                return;
            }
            if (_handVisual == null)
            {
                _handVisual = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                _handVisual.name = "HeldThrowable";
                Destroy(_handVisual.GetComponent<Collider>());
                _handVisual.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            ItemDefinition item = ItemOf(_held);
            _handVisual.transform.SetParent(_hand != null ? _hand : transform, false);
            _handVisual.transform.localPosition = _hand != null ? new Vector3(0f, 0.08f, 0.04f) : new Vector3(0.25f, 1.1f, 0.3f);
            _handVisual.transform.localScale = Vector3.one * (_held == Kind.Grenade ? 0.09f : 0.1f);
            var block = new MaterialPropertyBlock();
            Color c = item != null ? item.worldColor : Color.gray;
            block.SetColor("_BaseColor", c);
            block.SetColor("_Color", c);
            _handVisual.GetComponent<Renderer>().SetPropertyBlock(block);
            _handVisual.SetActive(true);
        }

        private void HideHandVisual()
        {
            if (_handVisual != null) _handVisual.SetActive(false);
        }

        private void OnDisable()
        {
            _throwing = false;
            ShowArc(false);
        }

        // ------------------------------------------------------------------ aim and throw

        private void Update()
        {
            if (_camera == null) _camera = Camera.main;
            if (_state == null) _state = PlayerCombatState.GetOrAdd(gameObject);

            if (_held != Kind.None && Count(_held) <= 0)
            {
                // Used up elsewhere (bag, trade): the hand is empty
                _held = Kind.None;
                HideHandVisual();
            }

            if (_held == Kind.None || _state.InputBlocked || _throwing)
            {
                ShowArc(false);
                return;
            }

            // LT: the arc (not before it is in the hand, not during a hit reaction)
            bool aiming = GameInput.AimHeld && !_state.Drawing && _state.Current != CombatAction.Stagger;
            if (aiming) DrawArc();
            else ShowArc(false);

            // RT / left click: throw (waits for the draw like a shot)
            if (Time.time >= _nextThrowTime && _state.TryConsume(CombatIntent.Throw))
                StartCoroutine(ThrowRoutine(_held));
        }

        private IEnumerator ThrowRoutine(Kind kind)
        {
            var content = CombatContent.Instance;
            ItemDefinition item = ItemOf(kind);
            if (item == null || _inventory == null || _inventory.Storage.Remove(item, 1) <= 0) yield break;

            _throwing = true;
            _nextThrowTime = Time.time + content.throwCooldown;
            ShowArc(false);

            // Face the throw direction
            Vector3 flat = _camera != null ? _camera.transform.forward : transform.forward;
            flat.y = 0f;
            if (flat.sqrMagnitude > 0.001f) transform.rotation = Quaternion.LookRotation(flat.normalized);

            ThrowStarted?.Invoke(content.releaseDelay + 0.5f); // PlayerCombatAnimation plays the throw clip
            yield return new WaitForSeconds(content.releaseDelay);

            HideHandVisual();
            GetLaunch(out Vector3 origin, out Vector3 velocity);
            ThrownProjectile.Launch(kind == Kind.Rock ? ThrownProjectile.Kind.Rock : ThrownProjectile.Kind.Grenade,
                origin, velocity, transform, item.worldColor);

            yield return new WaitForSeconds(0.3f);
            _throwing = false;

            // The next one of the same kind comes to the hand; none left: the hand is empty
            if (_held == kind && Count(kind) > 0) Hold(kind);
            else if (_held == kind) _held = Kind.None;
        }

        private void GetLaunch(out Vector3 origin, out Vector3 velocity)
        {
            var content = CombatContent.Instance;
            Vector3 dir = _camera != null ? _camera.transform.forward : transform.forward;
            Vector3 right = Vector3.Cross(Vector3.up, dir);
            if (right.sqrMagnitude < 0.0001f) right = transform.right;
            dir = Quaternion.AngleAxis(-content.throwUpAngle, right.normalized) * dir;

            Vector3 flat = new Vector3(dir.x, 0f, dir.z);
            if (flat.sqrMagnitude < 0.0001f) flat = transform.forward;
            origin = transform.position + Vector3.up * 1.6f + flat.normalized * 0.5f + transform.right * 0.15f;
            velocity = dir.normalized * content.throwSpeed;
        }

        private void DrawArc()
        {
            EnsureArc();
            GetLaunch(out Vector3 pos, out Vector3 vel);

            int count = 0;
            Vector3 g = Physics.gravity;
            Vector3 end = pos;
            bool landed = false;
            for (int i = 0; i < ArcPoints; i++)
            {
                _arc.positionCount = i + 1;
                _arc.SetPosition(i, pos);
                count = i + 1;

                Vector3 next = pos + vel * ArcStep + 0.5f * g * ArcStep * ArcStep;
                vel += g * ArcStep;
                Vector3 seg = next - pos;
                if (Physics.Raycast(pos, seg.normalized, out RaycastHit hit, seg.magnitude, ~0, QueryTriggerInteraction.Ignore)
                    && !hit.collider.transform.IsChildOf(transform))
                {
                    _arc.positionCount = count + 1;
                    _arc.SetPosition(count, hit.point);
                    end = hit.point;
                    landed = true;
                    break;
                }
                pos = next;
                end = pos;
            }

            ShowArc(true);
            _arcEnd.SetActive(landed);
            if (landed) _arcEnd.transform.position = end + Vector3.up * 0.05f;
        }

        private void EnsureArc()
        {
            if (_arc != null) return;

            var go = new GameObject("ThrowArc");
            _arc = go.AddComponent<LineRenderer>();
            _arc.startWidth = 0.05f;
            _arc.endWidth = 0.03f;
            _arc.numCapVertices = 2;
            Shader unlit = Shader.Find("Universal Render Pipeline/Unlit");
            if (unlit == null) unlit = Shader.Find("Sprites/Default");
            var mat = new Material(unlit) { color = new Color(1f, 1f, 1f, 0.8f) };
            _arc.sharedMaterial = mat;
            _arc.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _arc.receiveShadows = false;

            _arcEnd = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            _arcEnd.name = "ThrowArcEnd";
            Destroy(_arcEnd.GetComponent<Collider>());
            _arcEnd.transform.localScale = Vector3.one * 0.25f;
            var r = _arcEnd.GetComponent<Renderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _arcEnd.transform.SetParent(go.transform, true);
        }

        private void ShowArc(bool show)
        {
            if (_arc != null && _arc.gameObject.activeSelf != show) _arc.gameObject.SetActive(show);
        }

        private void OnDestroy()
        {
            if (_arc != null) Destroy(_arc.gameObject);
            if (_handVisual != null) Destroy(_handVisual);
        }

        // Small counter in the bottom right corner (placeholder HUD)
        private void OnGUI()
        {
            var content = CombatContent.Instance;
            if (content == null || (_state != null && _state.InputBlocked)) return;

            int rocks = Count(Kind.Rock);
            int grenades = Count(Kind.Grenade);
            if (rocks == 0 && grenades == 0 && _held == Kind.None) return;

            if (_style == null)
            {
                _style = new GUIStyle(GUI.skin.label) { fontSize = 16, alignment = TextAnchor.LowerRight };
                _style.normal.textColor = new Color(1f, 1f, 1f, 0.85f);
            }

            string text = GameInput.UsingPad ? $"LB basılı: seçim haritası   Taş x{rocks}   El bombası x{grenades}"
                                             : $"[T] Taş x{rocks}    [G] El bombası x{grenades}";
            if (_held != Kind.None)
                text = (GameInput.UsingPad ? "Elde: " : "Elde: ") + (_held == Kind.Rock ? "Taş" : "El bombası")
                       + GameInput.Hint("   Sağ tık: nişan   Sol tık: at", "   LT: nişan   RT: at   LB: geri koy") + "\n" + text;
            GUI.Label(new Rect(Screen.width - 620, Screen.height - 120, 600, 50), text, _style);
        }
    }
}
