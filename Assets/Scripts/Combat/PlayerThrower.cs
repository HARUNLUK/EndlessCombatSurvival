using System.Collections;
using UnityEngine;
using EndlessSurvival.Inventory;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace EndlessCombat.Combat
{
    /// <summary>
    /// Throwing rocks and grenades from the backpack.
    ///   Hold T (rock) or G (grenade): the arc is drawn. Release: throw. Right click while holding: cancel.
    /// A rock makes a noise where it lands (enemies go to look). A grenade explodes after its fuse.
    /// Each scene start gives a few of each for testing (CombatContent.startingRocks / startingGrenades; set to 0 to turn off).
    /// </summary>
    [DisallowMultipleComponent]
    public class PlayerThrower : MonoBehaviour
    {
        private enum Kind { None, Rock, Grenade }

        private const int ArcPoints = 40;
        private const float ArcStep = 0.05f;

        private PlayerInventory _inventory;
        private CombatAnimator _anim;
        private Camera _camera;
        private LineRenderer _arc;
        private GameObject _arcEnd;
        private Kind _holding = Kind.None;
        private float _nextThrowTime;
        private bool _throwing;
        private GUIStyle _style;

        /// <summary>True while aiming or performing a throw (blocks melee).</summary>
        public bool IsBusy => _holding != Kind.None || _throwing;

        private void Awake()
        {
            _inventory = GetComponent<PlayerInventory>();
            var animator = GetComponent<Animator>();
            if (animator == null) animator = GetComponentInChildren<Animator>();
            _anim = CombatAnimator.GetOrAdd(animator);
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

            if (content.rockItem != null && content.startingRocks > 0 && _inventory.Storage.GetCount(content.rockItem) == 0)
                _inventory.Storage.Add(content.rockItem, content.startingRocks);
            if (content.grenadeItem != null && content.startingGrenades > 0 && _inventory.Storage.GetCount(content.grenadeItem) == 0)
                _inventory.Storage.Add(content.grenadeItem, content.startingGrenades);

            if (content.testItems != null)
            {
                foreach (var item in content.testItems)
                {
                    if (item != null && _inventory.Storage.GetCount(item) == 0) _inventory.AddItem(item, 1);
                }
            }
        }

        private void OnDisable()
        {
            _holding = Kind.None;
            _throwing = false;
            ShowArc(false);
        }

        private void Update()
        {
            if (_camera == null) _camera = Camera.main;
            var content = CombatContent.Instance;

            if (CombatInputGate.Blocked(gameObject))
            {
                _holding = Kind.None;
                ShowArc(false);
                return;
            }

            ReadKeys(out bool rockDown, out bool rockHeld, out bool grenadeDown, out bool grenadeHeld, out bool cancel);

            if (_holding == Kind.None && !_throwing && Time.time >= _nextThrowTime)
            {
                if (rockDown && Count(content.rockItem) > 0) _holding = Kind.Rock;
                else if (grenadeDown && Count(content.grenadeItem) > 0) _holding = Kind.Grenade;
                else if (rockDown || grenadeDown)
                {
                    EndlessSurvival.Save.SaveManager.Instance?.ShowNotification(
                        rockDown ? "Taş yok" : "El bombası yok", new Color(0.4f, 0.4f, 0.4f, 0.9f));
                }
            }

            if (_holding == Kind.None)
            {
                ShowArc(false);
                return;
            }

            if (cancel)
            {
                _holding = Kind.None;
                ShowArc(false);
                return;
            }

            bool held = _holding == Kind.Rock ? rockHeld : grenadeHeld;
            if (held)
            {
                DrawArc();
                return;
            }

            // Released: throw
            Kind kind = _holding;
            _holding = Kind.None;
            ShowArc(false);
            StartCoroutine(ThrowRoutine(kind));
        }

        private IEnumerator ThrowRoutine(Kind kind)
        {
            var content = CombatContent.Instance;
            ItemDefinition item = kind == Kind.Rock ? content.rockItem : content.grenadeItem;
            if (item == null || _inventory == null || _inventory.Storage.Remove(item, 1) <= 0) yield break;

            _throwing = true;
            _nextThrowTime = Time.time + content.throwCooldown;

            // Face the throw direction
            Vector3 flat = _camera != null ? _camera.transform.forward : transform.forward;
            flat.y = 0f;
            if (flat.sqrMagnitude > 0.001f) transform.rotation = Quaternion.LookRotation(flat.normalized);

            if (_anim != null) _anim.PlayAction(CombatAnimator.Throw, content.releaseDelay + 0.5f);
            yield return new WaitForSeconds(content.releaseDelay);

            GetLaunch(out Vector3 origin, out Vector3 velocity);
            ThrownProjectile.Launch(kind == Kind.Rock ? ThrownProjectile.Kind.Rock : ThrownProjectile.Kind.Grenade,
                origin, velocity, transform, item.worldColor);

            yield return new WaitForSeconds(0.3f);
            _throwing = false;
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
        }

        private int Count(ItemDefinition item)
        {
            return item != null && _inventory != null ? _inventory.Storage.GetCount(item) : 0;
        }

        private static void ReadKeys(out bool rockDown, out bool rockHeld, out bool grenadeDown, out bool grenadeHeld, out bool cancel)
        {
#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current;
            var mouse = Mouse.current;
            rockDown = kb != null && kb.tKey.wasPressedThisFrame;
            rockHeld = kb != null && kb.tKey.isPressed;
            grenadeDown = kb != null && kb.gKey.wasPressedThisFrame;
            grenadeHeld = kb != null && kb.gKey.isPressed;
            cancel = mouse != null && mouse.rightButton.wasPressedThisFrame;
#else
            rockDown = Input.GetKeyDown(KeyCode.T);
            rockHeld = Input.GetKey(KeyCode.T);
            grenadeDown = Input.GetKeyDown(KeyCode.G);
            grenadeHeld = Input.GetKey(KeyCode.G);
            cancel = Input.GetMouseButtonDown(1);
#endif
        }

        // Small counter in the bottom right corner (placeholder HUD)
        private void OnGUI()
        {
            var content = CombatContent.Instance;
            if (content == null || CombatInputGate.Blocked(gameObject)) return;

            int rocks = Count(content.rockItem);
            int grenades = Count(content.grenadeItem);
            if (rocks == 0 && grenades == 0 && _holding == Kind.None) return;

            if (_style == null)
            {
                _style = new GUIStyle(GUI.skin.label) { fontSize = 16, alignment = TextAnchor.LowerRight };
                _style.normal.textColor = new Color(1f, 1f, 1f, 0.85f);
            }

            string text = $"[T] Taş x{rocks}    [G] El bombası x{grenades}";
            if (_holding != Kind.None) text = "Bırak: at   Sağ tık: vazgeç\n" + text;
            GUI.Label(new Rect(Screen.width - 520, Screen.height - 120, 500, 50), text, _style);
        }
    }
}
