using UnityEngine;
using EndlessSurvival.Inventory;
using EndlessSurvival.World.Road;

namespace EndlessSurvival.World
{
    /// <summary>
    /// Scatters loose pickups and loot chests along the road of a chunk when it spawns.
    /// Placed items are parented to the chunk so they unload with it.
    /// </summary>
    public class RoadsideLootSpawner : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("Road spline of this chunk (found in children if empty)")]
        public RoadSpline spline;

        [Header("Loose Pickups")]
        public LootTable pickupTable;
        public int minPickups = 6;
        public int maxPickups = 14;

        [Header("Chests")]
        public LootChest chestPrefab;
        public int minChests = 1;
        public int maxChests = 3;

        [Header("Placement")]
        [Tooltip("Minimum distance from the road centerline")]
        public float minLateralDistance = 7f;

        [Tooltip("Maximum distance from the road centerline")]
        public float maxLateralDistance = 45f;

        [Tooltip("Ground probe layers")]
        public LayerMask groundMask = ~0;

        private void Start()
        {
            if (spline == null) spline = GetComponentInChildren<RoadSpline>();
            if (spline == null) return;

            if (pickupTable != null && pickupTable.pickupPrefab != null)
            {
                int count = Random.Range(minPickups, maxPickups + 1);
                for (int i = 0; i < count; i++)
                {
                    if (!TryGetGroundPoint(out Vector3 point)) continue;

                    ItemStack stack = pickupTable.RollSingle();
                    WorldPickup.Spawn(pickupTable.pickupPrefab, stack.item, stack.amount, point + Vector3.up * 0.4f, transform);
                }
            }

            if (chestPrefab != null)
            {
                int count = Random.Range(minChests, maxChests + 1);
                for (int i = 0; i < count; i++)
                {
                    if (!TryGetGroundPoint(out Vector3 point)) continue;

                    Instantiate(chestPrefab, point + Vector3.up * 0.3f, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f), transform);
                }
            }
        }

        private bool TryGetGroundPoint(out Vector3 point)
        {
            point = default;

            float t = Random.Range(0.04f, 0.96f);
            Transform st = spline.transform;
            Vector3 center = st.TransformPoint(spline.GetPoint(t));
            Vector3 right = st.TransformDirection(spline.GetRight(t));

            float side = Random.value < 0.5f ? -1f : 1f;
            Vector3 probe = center + right * side * Random.Range(minLateralDistance, maxLateralDistance);

            Vector3 origin = probe + Vector3.up * 150f;
            if (!Physics.Raycast(origin, Vector3.down, out RaycastHit hit, 400f, groundMask, QueryTriggerInteraction.Ignore))
                return false;

            point = hit.point;
            return true;
        }
    }
}
