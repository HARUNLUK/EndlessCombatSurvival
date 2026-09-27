using UnityEngine;

namespace EndlessSurvival.Inventory
{
    /// <summary>
    /// Vehicle stash. Unlimited capacity for now. Only reachable when the player is close to or inside the vehicle.
    /// </summary>
    public class VehicleStorage : MonoBehaviour
    {
        [Tooltip("Maximum distance from the vehicle body at which the player can reach the stash")]
        public float accessDistance = 6f;

        private ItemStorage _storage;

        public ItemStorage Storage
        {
            get
            {
                if (_storage == null) _storage = new ItemStorage(0f);
                return _storage;
            }
        }

        public bool IsInRange(Vector3 position)
        {
            Collider col = GetComponent<Collider>();
            float distance = col != null
                ? Vector3.Distance(col.ClosestPoint(position), position)
                : Vector3.Distance(transform.position, position);
            return distance <= accessDistance;
        }
    }
}
