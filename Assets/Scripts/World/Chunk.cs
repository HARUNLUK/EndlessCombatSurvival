using System;
using UnityEngine;
using EndlessSurvival.World.Road;

namespace EndlessSurvival.World
{
    public enum ChunkBiomeType
    {
        Forest,
        Desert,
        RuinedCity,
        Wasteland,
        Snow
    }

    public enum ChunkRoadType
    {
        Straight,
        CurvedLeft,
        CurvedRight,
        Intersection,
        HazardZone
    }

    /// <summary>
    /// Represents an individual 500x500 pre-authored parcel/chunk in the infinite road world.
    /// Classified by biome, road topology, and spawn weight parameters.
    /// </summary>
    public class Chunk : MonoBehaviour
    {
        [Header("Classification & Parameters")]
        [Tooltip("Biome classification for this chunk")]
        public ChunkBiomeType biomeType = ChunkBiomeType.Forest;

        [Tooltip("Road layout type for this chunk (Straight, Curved, Chicane, etc.)")]
        public ChunkRoadType roadType = ChunkRoadType.Straight;

        [Tooltip("Road cross-section style (Kaldırımlı, Bariyerli, Otoyol, Açık Kırsal, vb.)")]
        public RoadCrossSectionPreset crossSectionType = RoadCrossSectionPreset.FullHighway;

        [Tooltip("Relative probability weight when randomly selecting chunks (higher = more frequent)")]
        [Range(1, 100)]
        public int spawnWeight = 10;

        [Tooltip("Difficulty tier of this chunk (for enemy/loot scaling in later phases)")]
        [Range(1, 5)]
        public int difficultyTier = 1;

        [Header("Dimensions & Sockets")]
        [Tooltip("Length of the chunk along the forward Z axis (standard 500 units)")]
        public float chunkLength = 500f;

        [Tooltip("Baseline road & ground elevation above sea level in meters (Y=0 is sea level, Y=20 is road level)")]
        public float baseElevation = 20f;

        [Tooltip("Socket where the road enters this chunk (usually local Y = 20, Z = 0)")]
        public Transform entrySocket;

        [Tooltip("Socket where the road exits this chunk (usually local Y = 20, Z = 500)")]
        public Transform exitSocket;

        [Header("Triggers & Blockades")]
        [Tooltip("Trigger near the chunk end that requests spawning the next chunk ahead")]
        public ChunkTrigger spawnNextTrigger;

        [Tooltip("Trigger or barrier that blocks backward passage after moving forward")]
        public GameObject backBlockade;

        [Header("Object Pooling / Spawn Points")]
        [Tooltip("Predefined spawn points inside this chunk for randomized props/hazards/loot")]
        public Transform[] propSpawnPoints;

        // Runtime state
        private ChunkManager _manager;
        private int _chunkIndex;

        public int ChunkIndex => _chunkIndex;
        public ChunkManager Manager => _manager;

        public void Initialize(ChunkManager manager, int index)
        {
            _manager = manager;
            _chunkIndex = index;

            if (backBlockade != null)
            {
                backBlockade.SetActive(false);
            }
        }

        public void NotifySpawnNextRequested()
        {
            if (_manager != null)
            {
                _manager.OnChunkSpawnNextRequested(this);
            }
        }

        public void NotifySealPassageRequested()
        {
            SealBackwardPassage();

            if (_manager != null)
            {
                _manager.OnChunkSealed(this);
            }
        }

        public void SealBackwardPassage()
        {
            if (backBlockade != null)
            {
                backBlockade.SetActive(true);
            }
        }

        public void UnloadChunk()
        {
            Destroy(gameObject);
        }

        private void OnDrawGizmos()
        {
            Gizmos.color = Color.green;
            if (entrySocket != null)
            {
                Gizmos.DrawWireCube(entrySocket.position, new Vector3(8f, 1f, 2f));
            }

            Gizmos.color = Color.red;
            if (exitSocket != null)
            {
                Gizmos.DrawWireCube(exitSocket.position, new Vector3(8f, 1f, 2f));
            }

            // Draw bounding box (500x500)
            Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.25f);
            Vector3 center = transform.position + new Vector3(0f, 0f, chunkLength * 0.5f);
            Gizmos.DrawWireCube(center, new Vector3(500f, 20f, chunkLength));
        }
    }
}
