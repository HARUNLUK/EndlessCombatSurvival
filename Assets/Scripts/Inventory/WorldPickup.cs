using UnityEngine;

namespace EndlessSurvival.Inventory
{
    /// <summary>
    /// A loose item lying in the world. Shown as a tinted placeholder cube, or as the item's world model
    /// (weapons) when one is assigned. New pickups fall to the ground and rest there.
    /// </summary>
    public class WorldPickup : MonoBehaviour, IInteractable
    {
        public ItemDefinition item;
        [Min(1)] public int amount = 1;

        [Tooltip("Degrees per second of idle spin")]
        public float spinSpeed = 40f;

        [Header("Falling")]
        [Tooltip("Height above the ground surface where the pickup comes to rest")]
        public float restHeight = 0.12f;
        public float gravity = 20f;
        [Tooltip("Safety limit: the pickup stops falling after this many seconds")]
        public float maxFallSeconds = 6f;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        private bool _settled;
        private float _fallSpeed;
        private float _fallTime;

        public Vector3 InteractPosition => transform.position;
        public bool CanInteract => item != null && amount > 0;

        private void Start()
        {
            ApplyVisual();
        }

        private void Update()
        {
            if (!_settled) Fall();
            if (spinSpeed != 0f) transform.Rotate(0f, spinSpeed * Time.deltaTime, 0f, Space.World);
        }

        private void Fall()
        {
            _fallTime += Time.deltaTime;
            _fallSpeed += gravity * Time.deltaTime;
            float step = _fallSpeed * Time.deltaTime;

            if (_fallTime >= maxFallSeconds)
            {
                _settled = true;
                return;
            }

            Vector3 position = transform.position;
            if (Physics.Raycast(position + Vector3.up * 0.05f, Vector3.down, out RaycastHit hit, step + restHeight + 0.05f, ~0, QueryTriggerInteraction.Ignore))
            {
                transform.position = hit.point + Vector3.up * restHeight;
                _settled = true;
            }
            else
            {
                transform.position = position + Vector3.down * step;
            }
        }

        public string GetPrompt()
        {
            return item == null ? string.Empty : $"Pick up {item.DisplayName} x{amount}";
        }

        public string Interact(PlayerInventory user)
        {
            if (!CanInteract || user == null) return null;

            int taken = user.AddItem(item, amount);
            if (taken <= 0) return "Backpack is too heavy";

            string message = $"Picked up {item.DisplayName} x{taken}";
            amount -= taken;
            if (amount <= 0) Destroy(gameObject);
            return message;
        }

        private void ApplyVisual()
        {
            if (item == null) return;

            if (item.worldModel != null)
            {
                ApplyModelVisual();
                return;
            }

            transform.localScale = item.worldScale;

            var block = new MaterialPropertyBlock();
            block.SetColor(BaseColorId, item.worldColor);
            block.SetColor(ColorId, item.worldColor);
            foreach (var r in GetComponentsInChildren<Renderer>())
                r.SetPropertyBlock(block);
        }

        private void ApplyModelVisual()
        {
            transform.localScale = Vector3.one * item.worldModelScale;

            // Hide the placeholder cube of the pickup prefab root
            var cube = GetComponent<MeshRenderer>();
            if (cube != null) cube.enabled = false;

            GameObject model = Instantiate(item.worldModel, transform);
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;
            model.transform.localScale = Vector3.one;

            foreach (var col in model.GetComponentsInChildren<Collider>())
                Destroy(col);

            // Long models (rifles) should lie flat on the ground instead of standing upright
            var filter = model.GetComponentInChildren<MeshFilter>();
            if (filter != null && filter.sharedMesh != null)
            {
                Vector3 size = Vector3.Scale(filter.sharedMesh.bounds.size, filter.transform.lossyScale);
                if (size.y > Mathf.Max(size.x, size.z))
                    model.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            }
        }

        public static WorldPickup Spawn(WorldPickup prefab, ItemDefinition item, int amount, Vector3 position, Transform parent)
        {
            if (prefab == null || item == null || amount <= 0) return null;

            WorldPickup pickup = Instantiate(prefab, position, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f), parent);
            pickup.item = item;
            pickup.amount = amount;
            return pickup;
        }
    }
}
