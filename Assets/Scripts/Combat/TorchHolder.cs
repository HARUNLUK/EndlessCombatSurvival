using UnityEngine;
using EndlessSurvival.Inventory;

namespace EndlessCombat.Combat
{
    /// <summary>
    /// A lit torch in the left hand (Combat_Action_Plan.md 2, 8; first version 2026-11-16).
    /// The torch item stays in the bag while it burns (a save keeps it); when the fuel runs out one torch is used up.
    /// Picked on the LB map (PlayerLoadout.PickTorch); a shield on the arm and the torch share the left hand
    /// (drop rule, PlayerLoadout). The model is kept upright in the left hand after the animation, like the shield.
    /// Not yet: keeping animals away, setting the hit target on fire (plan 8).
    /// </summary>
    [DisallowMultipleComponent]
    public class TorchHolder : MonoBehaviour
    {
        [Tooltip("Seconds one torch burns")]
        public static float BurnSeconds = 300f;
        private static bool _tuningRegistered;

        [Tooltip("Hand-space offset of the torch grip from the left palm (character space: right, up, forward)")]
        public Vector3 gripOffset = new Vector3(0f, -0.02f, 0.04f);
        [Tooltip("Forward / outward tilt of the stick (degrees)")]
        public float forwardTilt = 18f;
        public float outwardTilt = 10f;

        private PlayerInventory _inventory;
        private Animator _animator;
        private GameObject _visual;
        private Light _light;
        private Transform _flame;
        private ItemDefinition _item;
        private float _fuel = -1f; // seconds left of the torch being burnt; < 0 = a fresh one
        private bool _hidden;

        public bool Held => _item != null;
        public ItemDefinition Item => _item;
        /// <summary>Fuel left of the torch in the hand, 0..1.</summary>
        public float Fuel01 => _item != null ? Mathf.Clamp01(_fuel / Mathf.Max(1f, BurnSeconds)) : 0f;
        /// <summary>The model in the hand (null when none).</summary>
        public GameObject Visual => _visual;

        public static TorchHolder GetOrAdd(GameObject owner)
        {
            var t = owner.GetComponent<TorchHolder>();
            return t != null ? t : owner.AddComponent<TorchHolder>();
        }

        private void Awake()
        {
            _inventory = GetComponent<PlayerInventory>();
            if (!_tuningRegistered)
            {
                _tuningRegistered = true;
                Tuning.TuningPanel.Register("Dövüş", "Meşale yanma süresi (sn)", () => BurnSeconds, v => BurnSeconds = v, 30f, 900f, 10f);
            }
        }

        /// <summary>The torch item the player can light (the first one in the bag), or null.</summary>
        public ItemDefinition TorchInBag()
        {
            if (_inventory == null) _inventory = GetComponent<PlayerInventory>();
            if (_inventory == null) return null;
            var settings = EndlessSurvival.Survival.SurvivalSettings.Instance;
            ItemDefinition torch = settings != null ? settings.Item("Item_Torch") : null;
            return torch != null && _inventory.Storage.GetCount(torch) > 0 ? torch : null;
        }

        /// <summary>Lights a torch from the bag into the left hand. False when there is none.</summary>
        public bool Light()
        {
            ItemDefinition torch = TorchInBag();
            if (torch == null) return false;
            _item = torch;
            if (_fuel <= 0f) _fuel = BurnSeconds;
            BuildVisual();
            return true;
        }

        /// <summary>Puts the torch out; it stays in the bag with the fuel it has left.</summary>
        public void PutOut()
        {
            _item = null;
            if (_visual != null) Destroy(_visual);
            _visual = null;
            _light = null;
            _flame = null;
        }

        /// <summary>The torch falls to the ground (another pick needs the left hand). Its fuel starts fresh when picked up.</summary>
        public void Drop(Vector3 at, WorldPickup pickupPrefab)
        {
            if (_item == null) return;
            ItemDefinition item = _item;
            PutOut();
            _fuel = -1f;
            if (_inventory == null || pickupPrefab == null) return;
            if (_inventory.Storage.Remove(item, 1) <= 0) return;
            WorldPickup p = WorldPickup.Spawn(pickupPrefab, item, 1, at, null);
            if (p != null) p.fromHand = true;
        }

        /// <summary>Vehicles, hands busy: the torch is hidden (and does not burn).</summary>
        public void Hide(bool hidden)
        {
            _hidden = hidden;
            if (_visual != null) _visual.SetActive(!hidden);
        }

        private void Update()
        {
            if (_item == null || _hidden) return;
            if (_inventory != null && _inventory.Storage.GetCount(_item) <= 0)
            {
                // Taken out of the bag some other way (dropped from the inventory): nothing left to burn
                PutOut();
                _fuel = -1f;
                return;
            }
            _fuel -= Time.deltaTime;
            if (_fuel <= 0f)
            {
                ItemDefinition item = _item;
                PutOut();
                _fuel = -1f;
                if (_inventory != null) _inventory.Storage.Remove(item, 1);
                EndlessSurvival.Survival.SurvivalNotify.Show("Meşale söndü", 1.5f);
                GetComponent<PlayerLoadout>()?.Refresh();
                return;
            }
            if (_light != null)
            {
                float t = Time.time;
                float flicker = 0.85f + 0.1f * Mathf.Sin(t * 13f) + 0.06f * Mathf.Sin(t * 29f + 1.3f);
                _light.intensity = 1.5f * flicker * Mathf.Lerp(0.55f, 1f, Fuel01);
                if (_flame != null) _flame.localScale = new Vector3(0.07f, 0.12f * flicker, 0.07f);
            }
        }

        /// <summary>Upright in the left hand after the animation (a stick glued to the bone would swing with every clip).</summary>
        private void LateUpdate()
        {
            if (_visual == null || _hidden) return;
            if (_animator == null) _animator = GetComponentInChildren<Animator>();
            Transform hand = _animator != null && _animator.isHuman ? _animator.GetBoneTransform(HumanBodyBones.LeftHand) : null;
            Vector3 up = Vector3.up;
            Vector3 fwd = Vector3.ProjectOnPlane(transform.forward, up).normalized;
            Vector3 right = Vector3.Cross(up, fwd);
            Transform middle = _animator != null && _animator.isHuman ? _animator.GetBoneTransform(HumanBodyBones.LeftMiddleProximal) : null;
            Vector3 palm = hand != null && middle != null ? Vector3.Lerp(hand.position, middle.position, 0.75f) // the palm, not the wrist
                         : hand != null ? hand.position : transform.position + up * 1.0f - right * 0.3f + fwd * 0.2f;
            Vector3 pos = palm + right * gripOffset.x + up * gripOffset.y + fwd * gripOffset.z;
            Quaternion rot = Quaternion.LookRotation(fwd, up) * Quaternion.Euler(forwardTilt, 0f, outwardTilt);
            _visual.transform.SetPositionAndRotation(pos, rot);
        }

        private void BuildVisual()
        {
            if (_visual != null) Destroy(_visual);
            if (_item != null && _item.heldModel != null) _visual = Instantiate(_item.heldModel);
            else
            {
                _visual = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                Destroy(_visual.GetComponent<Collider>());
                _visual.transform.localScale = new Vector3(0.035f, 0.25f, 0.035f);
            }
            _visual.name = "HeldTorch";
            _visual.transform.SetParent(transform, false); // goes with the player (hidden, destroyed); placed in LateUpdate
            foreach (var c in _visual.GetComponentsInChildren<Collider>()) Destroy(c);

            // The flame and its light are made here (the pickup model is the plain stick)
            var flameGo = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            flameGo.name = "Flame";
            Destroy(flameGo.GetComponent<Collider>());
            flameGo.transform.SetParent(_visual.transform, false);
            flameGo.transform.localPosition = new Vector3(0f, 0.47f, 0f);
            flameGo.transform.localScale = new Vector3(0.07f, 0.12f, 0.07f);
            var r = flameGo.GetComponent<MeshRenderer>();
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.sharedMaterial = FlameMaterial();
            _flame = flameGo.transform;

            var lightGo = new GameObject("TorchLight");
            lightGo.transform.SetParent(_visual.transform, false);
            lightGo.transform.localPosition = new Vector3(0f, 0.55f, 0f);
            _light = lightGo.AddComponent<Light>();
            _light.type = LightType.Point;
            _light.color = new Color(1f, 0.62f, 0.3f);
            _light.range = 8f;
            _light.intensity = 1.5f;
            _light.shadows = LightShadows.None;

            _visual.SetActive(!_hidden);
        }

        private static Material _flameMat;

        private static Material FlameMaterial()
        {
            if (_flameMat != null) return _flameMat;
            Shader s = Shader.Find("Universal Render Pipeline/Unlit");
            if (s == null) s = Shader.Find("Unlit/Color");
            _flameMat = new Material(s) { name = "TorchFlame" };
            Color c = new Color(1f, 0.7f, 0.25f);
            _flameMat.SetColor("_BaseColor", c);
            _flameMat.SetColor("_Color", c);
            return _flameMat;
        }

        private void OnDestroy()
        {
            if (_visual != null) Destroy(_visual);
        }
    }
}
