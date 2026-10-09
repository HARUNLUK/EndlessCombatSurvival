using UnityEngine;
using EndlessSurvival.Inventory;
using EndlessSurvival.World.Road;

namespace EndlessSurvival.World
{
    /// <summary>
    /// Scatters loose pickups and loot chests along the road of a chunk when it spawns.
    /// Placed items are parented to the chunk so they unload with it.
    /// Uses the chunk's LootRandom sub-stream for deterministic placement.
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
        public float minLateralDistance = 15f;

        [Tooltip("Maximum distance from the road centerline")]
        public float maxLateralDistance = 230f;

        [Tooltip("Ground probe layers")]
        public LayerMask groundMask = ~0;

        private void Start()
        {
            if (spline == null) spline = GetComponentInChildren<RoadSpline>();
            if (spline == null) return;

            Chunk chunk = GetComponentInParent<Chunk>();
            SeededRandom rng = chunk != null ? chunk.LootRandom : new SeededRandom(0);

            // Wait for the terrain to be shaped when the chunk is built over several frames
            if (chunk != null) chunk.WhenBuilt(() => GenerateLoot(rng, transform));
            else GenerateLoot(rng, transform);
        }

        public void GenerateLoot(SeededRandom rng, Transform container)
        {
            if (spline == null) spline = GetComponentInChildren<RoadSpline>();
            if (spline == null) return;

            if (pickupTable != null && pickupTable.pickupPrefab != null)
            {
                int count = rng.Range(minPickups, maxPickups + 1);
                for (int i = 0; i < count; i++)
                {
                    if (!TryGetGroundPoint(rng, out Vector3 point)) continue;
                    ItemStack stack = pickupTable.RollSingle(rng);
#if UNITY_EDITOR
                    if (!Application.isPlaying)
                    {
                        GameObject p = UnityEditor.PrefabUtility.InstantiatePrefab(pickupTable.pickupPrefab.gameObject, container) as GameObject;
                        if (p != null)
                        {
                            p.transform.position = point + Vector3.up * 0.4f;
                            // Basic setup for preview
                            WorldPickup pickup = p.GetComponent<WorldPickup>();
                            if (pickup != null) pickup.item = stack.item;
                        }
                    }
                    else
#endif
                    {
                        WorldPickup.Spawn(pickupTable.pickupPrefab, stack.item, stack.amount, point + Vector3.up * 0.4f, container);
                    }
                }
            }
            else
            {
                Debug.LogWarning("RoadsideLootSpawner: pickupTable or prefab is NULL!");
            }

            if (chestPrefab != null)
            {
                int count = rng.Range(minChests, maxChests + 1);
                for (int i = 0; i < count; i++)
                {
                    if (!TryGetGroundPoint(rng, out Vector3 point)) continue;

#if UNITY_EDITOR
                    if (!Application.isPlaying)
                    {
                        GameObject c = UnityEditor.PrefabUtility.InstantiatePrefab(chestPrefab.gameObject, container) as GameObject;
                        if (c != null)
                        {
                            c.transform.position = point + Vector3.up * 0.3f;
                            c.transform.rotation = Quaternion.Euler(0f, rng.Range(0f, 360f), 0f);
                        }
                    }
                    else
#endif
                    {
                        Instantiate(chestPrefab, point + Vector3.up * 0.3f, Quaternion.Euler(0f, rng.Range(0f, 360f), 0f), container);
                    }
                }
            }
        }

        private bool TryGetGroundPoint(SeededRandom rng, out Vector3 point)
        {
            point = default;

            float t = rng.Range(0.04f, 0.96f);
            Transform st = spline.transform;
            Vector3 center = st.TransformPoint(spline.GetPoint(t));
            Vector3 right = st.TransformDirection(spline.GetRight(t));

            float side = rng.Value < 0.5f ? -1f : 1f;
            float lateral = rng.Range(minLateralDistance, maxLateralDistance);
            Vector3 probe = center + right * side * lateral;

            point = probe;
            Chunk chunk = spline.GetComponentInParent<Chunk>();
            Terrain tComponent = chunk != null ? chunk.ChunkTerrain : null;
            if (tComponent != null)
            {
                point.y = tComponent.SampleHeight(probe) + tComponent.transform.position.y;

                // Coast: loot that would land in the sea goes to the other side of the road
                float seaY = chunk.transform.position.y + WorldConstants.SeaLevel + 1f;
                if (chunk.IsCoast && point.y < seaY)
                {
                    probe = center - right * side * lateral;
                    point = probe;
                    point.y = tComponent.SampleHeight(probe) + tComponent.transform.position.y;
                    if (point.y < seaY) return false;
                }

                // Field river crossing: skip points that fall into the channel
                if (chunk.IsField && point.y < chunk.transform.position.y + chunk.baseElevation - 1.5f) return false;

                // Mountains: nothing down in the canyon below the road (the player can't climb back up),
                // nor inside a mountain mesh beside the road
                if (chunk.IsMountain)
                {
                    if (point.y < center.y - 3f) return false;
                    var boundary = chunk.GetComponent<ChunkBoundaryGenerator>();
                    if (boundary != null && boundary.IsInsideMountain(chunk.transform.InverseTransformPoint(point))) return false;
                }
            }
            else
            {
                Vector3 origin = probe + Vector3.up * 150f;
                if (!Physics.Raycast(origin, Vector3.down, out RaycastHit hit, 400f, groundMask, QueryTriggerInteraction.Ignore))
                    return false;
                point.y = hit.point.y;
            }

            return true;
        }
    }
}
