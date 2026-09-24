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

    public enum RoadElevationType
    {
        Flat,
        HillCrest,
        ValleyDip,
        RollingHills,
        MountainPass,
        ElevatedChicane
    }

    /// <summary>
    /// Configuration for procedural elevation, slopes, and height limits on chunks.
    /// </summary>
    [System.Serializable]
    public class ChunkElevationConfig
    {
        [Header("Probability & Frequency (İhtimal & Sıklık)")]
        [Tooltip("Probability of this chunk spawning with vertical slope/elevation (0 = always flat, 1 = always elevated/dipped)")]
        [Range(0f, 1f)]
        public float elevationChance = 0.75f;

        [Header("Elevation Amplitude Limits (Maksimum ve Minimum Eğimler)")]
        [Tooltip("Maximum height a hill or mountain crest can reach in meters")]
        [Range(5f, 30f)]
        public float maxHillHeight = 16f;

        [Tooltip("Minimum height for a generated hill crest in meters")]
        [Range(4f, 14f)]
        public float minHillHeight = 8f;

        [Tooltip("Maximum depth a valley or pit can dip down into the ground in meters (baseline is 20m, sea level is 0m)")]
        [Range(5f, 18f)]
        public float maxDipDepth = 15f;

        [Tooltip("Minimum depth for a generated valley dip in meters")]
        [Range(4f, 12f)]
        public float minDipDepth = 7f;

        [Header("Type Distribution Weights (Eğim Tipleri Dağılım Ağırlıkları)")]
        [Tooltip("Relative weight for Hill Crest (Tümsek / Tepe)")]
        [Range(0, 100)]
        public int hillWeight = 25;

        [Tooltip("Relative weight for Valley Dip (Vadi / Çukur İnişi)")]
        [Range(0, 100)]
        public int valleyWeight = 25;

        [Tooltip("Relative weight for Rolling Hills (Dalgalı Tepeler)")]
        [Range(0, 100)]
        public int rollingHillsWeight = 20;

        [Tooltip("Relative weight for Mountain Pass (Dağ Geçidi Zirve Virajı)")]
        [Range(0, 100)]
        public int mountainPassWeight = 20;

        [Tooltip("Relative weight for Elevated Chicane (Tepeli Şikan)")]
        [Range(0, 100)]
        public int elevatedChicaneWeight = 15;

        [Tooltip("Relative weight for Flat Road (Düz Yol)")]
        [Range(0, 100)]
        public int flatWeight = 15;
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

        [Header("Elevation & Slope Settings (Eğim Ayarları)")]
        [Tooltip("Whether this chunk overrides the global elevation parameters set on ChunkManager")]
        public bool overrideElevationSettings = false;

        [Tooltip("Chunk-specific elevation distribution and amplitude limits")]
        public ChunkElevationConfig elevationConfig = new ChunkElevationConfig();

        [Header("Runtime Elevation State (Aktif Eğim Durumu)")]
        [Tooltip("Active elevation type applied to this spawned chunk")]
        public RoadElevationType currentElevationType = RoadElevationType.Flat;

        [Tooltip("Generated hill height or dip depth for this chunk in meters")]
        public float currentElevationParam = 0f;

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
