using UnityEngine;

namespace EndlessSurvival.World
{
    /// <summary>
    /// Trigger sensor placed inside a chunk to detect player or vehicle passage.
    /// Notifies the parent chunk when the threshold is crossed.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class ChunkTrigger : MonoBehaviour
    {
        public enum TriggerType
        {
            SpawnNextChunk,
            SealBackwardPassage
        }

        [Header("Settings")]
        [Tooltip("Type of event this trigger notifies")]
        public TriggerType triggerType = TriggerType.SpawnNextChunk;

        [Tooltip("Parent chunk reference")]
        public Chunk parentChunk;

        private bool _hasTriggered = false;

        private void Awake()
        {
            var col = GetComponent<Collider>();
            if (col != null) col.isTrigger = true;

            if (parentChunk == null)
                parentChunk = GetComponentInParent<Chunk>();
        }

        private void OnTriggerEnter(Collider other)
        {
            if (_hasTriggered) return;

            if (IsPlayerOrVehicle(other))
            {
                _hasTriggered = true;

                if (parentChunk != null)
                {
                    if (triggerType == TriggerType.SpawnNextChunk)
                    {
                        parentChunk.NotifySpawnNextRequested();
                    }
                    else if (triggerType == TriggerType.SealBackwardPassage)
                    {
                        parentChunk.NotifySealPassageRequested();
                    }
                }
            }
        }

        private bool IsPlayerOrVehicle(Collider other)
        {
            if (other.CompareTag("Player")) return true;
            if (other.GetComponentInParent<EndlessSurvival.Vehicle.VehicleController>() != null) return true;
            if (other.name.Contains("Player") || other.name.Contains("pickup")) return true;
            return false;
        }

        public void ResetTrigger()
        {
            _hasTriggered = false;
        }
    }
}
