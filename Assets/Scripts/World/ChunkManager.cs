using System.Collections.Generic;
using UnityEngine;
using EndlessSurvival.World.Road;

namespace EndlessSurvival.World
{
    /// <summary>
    /// Lightweight, modular manager for infinite road chunk streaming.
    /// Handles chunk spawning, socket-based road alignment, and unloading chunks left behind.
    /// </summary>
    public class ChunkManager : MonoBehaviour
    {
        public static ChunkManager Instance { get; private set; }

        [Header("Chunk Prefab")]
        [Tooltip("The single base 500x500 chunk prefab used for endless generation")]
        public Chunk chunkPrefab;

        [Tooltip("Optional list fallback")]
        public List<Chunk> chunkPrefabs = new List<Chunk>();

        [Header("Streaming Settings")]
        [Tooltip("Number of chunks to spawn when the game starts")]
        [Range(1, 5)]
        public int initialChunkCount = 2;

        [Tooltip("Maximum chunks to keep behind the player before unloading")]
        [Range(0, 3)]
        public int maxChunksBehind = 1;

        [Tooltip("Maximum total chunks allowed simultaneously in the scene")]
        [Range(2, 6)]
        public int maxActiveChunks = 3;

        [Tooltip("World start position for the very first chunk")]
        public Vector3 initialSpawnPosition = Vector3.zero;

        // Runtime chunk tracking
        private readonly List<Chunk> _activeChunks = new List<Chunk>();
        private int _totalSpawnedCount = 0;
        private Transform _targetTransform;

        public IReadOnlyList<Chunk> ActiveChunks => _activeChunks;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            ClearChildObjects();
        }

        private void Start()
        {
            if (chunkPrefab != null || (chunkPrefabs != null && chunkPrefabs.Count > 0))
            {
                SpawnInitialChunks();
            }
            else
            {
                Debug.LogWarning("[ChunkManager] No chunk prefab assigned.");
            }
        }

        private void Update()
        {
            if (!Application.isPlaying || _activeChunks.Count == 0) return;

            // Fallback safety check: ensure the road ahead is always populated if triggers were missed
            CheckRoadAheadDistance();
        }

        public void SpawnInitialChunks()
        {
            for (int i = 0; i < initialChunkCount; i++)
            {
                SpawnNextChunk();
            }
        }

        /// <summary>
        /// Instantiates the next chunk and aligns its entry socket to the previous chunk's exit socket.
        /// </summary>
        public Chunk SpawnNextChunk()
        {
            Chunk prefab = SelectNextPrefab();
            if (prefab == null)
            {
                Debug.LogError("[ChunkManager] Cannot spawn: No valid chunk prefab found.");
                return null;
            }

            Vector3 spawnPos = initialSpawnPosition;
            Quaternion spawnRot = Quaternion.identity;

            if (_activeChunks.Count > 0)
            {
                Chunk lastChunk = _activeChunks[_activeChunks.Count - 1];
                GetSocketAlignment(lastChunk, prefab, out spawnPos, out spawnRot);
            }

            Chunk newChunk = Instantiate(prefab, spawnPos, spawnRot, transform);
            ApplyDynamicRoadVariation(newChunk);

            newChunk.name = $"Chunk_{_totalSpawnedCount}_{newChunk.biomeType}_{newChunk.roadType}_{newChunk.crossSectionType}";
            newChunk.Initialize(this, _totalSpawnedCount);

            _activeChunks.Add(newChunk);
            _totalSpawnedCount++;

            return newChunk;
        }

        [Header("Procedural Elevation & Slope Generation (Eğim ve Yükseklik Ayarları)")]
        [Tooltip("Whether dynamic procedural elevation variation is enabled")]
        public bool enableElevationVariation = true;

        [Tooltip("Method used to determine elevation for each spawned chunk")]
        public ElevationSelectionMode elevationMode = ElevationSelectionMode.DynamicWeightedRandom;

        [Tooltip("Number of initial chunks guaranteed to be completely flat for smooth game start")]
        [Range(1, 5)]
        public int initialFlatChunks = 2;

        [Tooltip("Global elevation and slope distribution settings (used if chunk doesn't override)")]
        public ChunkElevationConfig globalElevationConfig = new ChunkElevationConfig();

        public enum ElevationSelectionMode
        {
            DynamicWeightedRandom, // Procedurally rolls based on probability, weights, and min/max limits
            SequentialPresetList   // Sequentially cycles through the predefined RoadVariations list
        }

        private struct RoadVariationDefinition
        {
            public ChunkRoadType roadType;
            public RoadElevationType elevationType;
            public RoadCrossSectionPreset crossSection;
            public float param1; // lateral shift / curve offset
            public float param2; // elevation height / depth

            public RoadVariationDefinition(
                ChunkRoadType roadType,
                RoadElevationType elevationType,
                RoadCrossSectionPreset crossSection,
                float param1 = 0f,
                float param2 = 0f)
            {
                this.roadType = roadType;
                this.elevationType = elevationType;
                this.crossSection = crossSection;
                this.param1 = param1;
                this.param2 = param2;
            }
        }

        private static readonly RoadVariationDefinition[] RoadVariations = new[]
        {
            // 0: Start Runway - Flat straight with sidewalk (smooth start for vehicle)
            new RoadVariationDefinition(ChunkRoadType.Straight, RoadElevationType.Flat, RoadCrossSectionPreset.SidewalkOnly),

            // 1: Gentle Hill Climb (+14m) with guardrails
            new RoadVariationDefinition(ChunkRoadType.Straight, RoadElevationType.HillCrest, RoadCrossSectionPreset.GuardrailOnly, 0f, 14f),

            // 2: Curved Right with rolling waves (+14m peak, -10m dip)
            new RoadVariationDefinition(ChunkRoadType.CurvedRight, RoadElevationType.RollingHills, RoadCrossSectionPreset.GuardrailOnly, 40f, 14f),

            // 3: Scenic Valley Dip (-14m down to Y=6m) on open country road
            new RoadVariationDefinition(ChunkRoadType.Straight, RoadElevationType.ValleyDip, RoadCrossSectionPreset.OpenRoad, 0f, 14f),

            // 4: Mountain Pass (Curved Left -45m with +16m ridge climb), Full Highway
            new RoadVariationDefinition(ChunkRoadType.CurvedLeft, RoadElevationType.MountainPass, RoadCrossSectionPreset.FullHighway, -45f, 16f),

            // 5: Winding Chicane with elevated undulating ridge (+10m) and fortified guardrails
            new RoadVariationDefinition(ChunkRoadType.HazardZone, RoadElevationType.ElevatedChicane, RoadCrossSectionPreset.HazardFortified, 35f, 10f),

            // 6: Wide Mountain Pass Right (+45m curve with +15m hill), Full Highway
            new RoadVariationDefinition(ChunkRoadType.CurvedRight, RoadElevationType.MountainPass, RoadCrossSectionPreset.FullHighway, 45f, 15f),

            // 7: Deep Valley Dip (-15m down to Y=5m) with guardrails
            new RoadVariationDefinition(ChunkRoadType.Straight, RoadElevationType.ValleyDip, RoadCrossSectionPreset.GuardrailOnly, 0f, 15f),

            // 8: Fast Flat Highway stretch
            new RoadVariationDefinition(ChunkRoadType.Straight, RoadElevationType.Flat, RoadCrossSectionPreset.FullHighway),
        };

        private void ApplyDynamicRoadVariation(Chunk chunk)
        {
            var roadGen = chunk.GetComponentInChildren<RoadGenerator>();
            var spline = chunk.GetComponentInChildren<RoadSpline>();
            var terrain = chunk.GetComponentInChildren<Terrain>();
            if (roadGen == null || spline == null) return;

            if (elevationMode == ElevationSelectionMode.DynamicWeightedRandom)
            {
                ApplyProceduralElevation(chunk, roadGen, spline);
            }
            else
            {
                ApplySequentialElevationPreset(chunk, roadGen, spline);
            }

            // Conform and deform terrain underneath and around the road seamlessly
            if (terrain != null)
            {
                RoadTerrainAdapter.ConformTerrainToRoad(terrain, spline, roadGen, true);
            }
        }

        private void ApplyProceduralElevation(Chunk chunk, RoadGenerator roadGen, RoadSpline spline)
        {
            ChunkElevationConfig config = (chunk != null && chunk.overrideElevationSettings)
                ? chunk.elevationConfig
                : globalElevationConfig;

            if (config == null) config = new ChunkElevationConfig();

            // First N chunks are kept completely flat as runway
            bool forceFlat = _totalSpawnedCount < initialFlatChunks || !enableElevationVariation;
            bool rollSuccess = !forceFlat && (Random.value <= config.elevationChance);

            RoadElevationType elevationType = RoadElevationType.Flat;

            if (rollSuccess)
            {
                int totalWeight = Mathf.Max(1, config.hillWeight + config.valleyWeight + config.rollingHillsWeight + config.mountainPassWeight + config.elevatedChicaneWeight + config.flatWeight);
                int roll = Random.Range(0, totalWeight);

                if (roll < config.hillWeight)
                {
                    elevationType = RoadElevationType.HillCrest;
                }
                else if (roll < config.hillWeight + config.valleyWeight)
                {
                    elevationType = RoadElevationType.ValleyDip;
                }
                else if (roll < config.hillWeight + config.valleyWeight + config.rollingHillsWeight)
                {
                    elevationType = RoadElevationType.RollingHills;
                }
                else if (roll < config.hillWeight + config.valleyWeight + config.rollingHillsWeight + config.mountainPassWeight)
                {
                    elevationType = RoadElevationType.MountainPass;
                }
                else if (roll < config.hillWeight + config.valleyWeight + config.rollingHillsWeight + config.mountainPassWeight + config.elevatedChicaneWeight)
                {
                    elevationType = RoadElevationType.ElevatedChicane;
                }
                else
                {
                    elevationType = RoadElevationType.Flat;
                }
            }

            switch (elevationType)
            {
                case RoadElevationType.HillCrest:
                    float hillHeight = Random.Range(config.minHillHeight, config.maxHillHeight);
                    spline.SetElevationHillPreset(hillHeight);
                    chunk.roadType = ChunkRoadType.Straight;
                    chunk.crossSectionType = RoadCrossSectionPreset.GuardrailOnly;
                    chunk.currentElevationType = RoadElevationType.HillCrest;
                    chunk.currentElevationParam = hillHeight;
                    roadGen.ApplyCrossSectionPreset(RoadCrossSectionPreset.GuardrailOnly);
                    break;

                case RoadElevationType.ValleyDip:
                    float dipDepth = Random.Range(config.minDipDepth, config.maxDipDepth);
                    spline.SetDipValleyPreset(dipDepth);
                    chunk.roadType = ChunkRoadType.Straight;
                    chunk.crossSectionType = RoadCrossSectionPreset.OpenRoad;
                    chunk.currentElevationType = RoadElevationType.ValleyDip;
                    chunk.currentElevationParam = dipDepth;
                    roadGen.ApplyCrossSectionPreset(RoadCrossSectionPreset.OpenRoad);
                    break;

                case RoadElevationType.RollingHills:
                    float rHill = Random.Range(config.minHillHeight, config.maxHillHeight);
                    float rDip = Random.Range(config.minDipDepth, config.maxDipDepth);
                    spline.SetRollingHillsPreset(rHill, rDip);
                    chunk.roadType = ChunkRoadType.CurvedRight;
                    chunk.crossSectionType = RoadCrossSectionPreset.GuardrailOnly;
                    chunk.currentElevationType = RoadElevationType.RollingHills;
                    chunk.currentElevationParam = rHill;
                    roadGen.ApplyCrossSectionPreset(RoadCrossSectionPreset.GuardrailOnly);
                    break;

                case RoadElevationType.MountainPass:
                    float mHill = Random.Range(config.minHillHeight, config.maxHillHeight);
                    float shift = (Random.value > 0.5f ? 1f : -1f) * Random.Range(35f, 45f);
                    spline.SetMountainPassPreset(shift, mHill);
                    chunk.roadType = shift > 0f ? ChunkRoadType.CurvedRight : ChunkRoadType.CurvedLeft;
                    chunk.crossSectionType = RoadCrossSectionPreset.FullHighway;
                    chunk.currentElevationType = RoadElevationType.MountainPass;
                    chunk.currentElevationParam = mHill;
                    roadGen.ApplyCrossSectionPreset(RoadCrossSectionPreset.FullHighway);
                    break;

                case RoadElevationType.ElevatedChicane:
                    float cHill = Random.Range(config.minHillHeight * 0.7f, config.maxHillHeight * 0.7f);
                    float cShift = (Random.value > 0.5f ? 1f : -1f) * 35f;
                    spline.SetElevatedChicanePreset(cShift, cHill);
                    chunk.roadType = ChunkRoadType.HazardZone;
                    chunk.crossSectionType = RoadCrossSectionPreset.HazardFortified;
                    chunk.currentElevationType = RoadElevationType.ElevatedChicane;
                    chunk.currentElevationParam = cHill;
                    roadGen.ApplyCrossSectionPreset(RoadCrossSectionPreset.HazardFortified);
                    break;

                case RoadElevationType.Flat:
                default:
                    float curveRoll = Random.value;
                    if (curveRoll < 0.45f)
                    {
                        spline.SetStraightPreset();
                        chunk.roadType = ChunkRoadType.Straight;
                        chunk.crossSectionType = RoadCrossSectionPreset.SidewalkOnly;
                        roadGen.ApplyCrossSectionPreset(RoadCrossSectionPreset.SidewalkOnly);
                    }
                    else if (curveRoll < 0.70f)
                    {
                        spline.SetSCurvePreset(45f);
                        chunk.roadType = ChunkRoadType.CurvedRight;
                        chunk.crossSectionType = RoadCrossSectionPreset.FullHighway;
                        roadGen.ApplyCrossSectionPreset(RoadCrossSectionPreset.FullHighway);
                    }
                    else if (curveRoll < 0.90f)
                    {
                        spline.SetSCurvePreset(-45f);
                        chunk.roadType = ChunkRoadType.CurvedLeft;
                        chunk.crossSectionType = RoadCrossSectionPreset.FullHighway;
                        roadGen.ApplyCrossSectionPreset(RoadCrossSectionPreset.FullHighway);
                    }
                    else
                    {
                        spline.SetChicanePreset(35f);
                        chunk.roadType = ChunkRoadType.HazardZone;
                        chunk.crossSectionType = RoadCrossSectionPreset.HazardFortified;
                        roadGen.ApplyCrossSectionPreset(RoadCrossSectionPreset.HazardFortified);
                    }
                    chunk.currentElevationType = RoadElevationType.Flat;
                    chunk.currentElevationParam = 0f;
                    break;
            }
        }

        private void ApplySequentialElevationPreset(Chunk chunk, RoadGenerator roadGen, RoadSpline spline)
        {
            RoadVariationDefinition variation = RoadVariations[_totalSpawnedCount % RoadVariations.Length];
            chunk.roadType = variation.roadType;
            chunk.crossSectionType = variation.crossSection;
            chunk.currentElevationType = variation.elevationType;
            chunk.currentElevationParam = variation.param2;

            switch (variation.elevationType)
            {
                case RoadElevationType.HillCrest:
                    spline.SetElevationHillPreset(variation.param2 > 0f ? variation.param2 : 14f);
                    break;

                case RoadElevationType.ValleyDip:
                    spline.SetDipValleyPreset(variation.param2 > 0f ? variation.param2 : 14f);
                    break;

                case RoadElevationType.RollingHills:
                    spline.SetRollingHillsPreset(variation.param2 > 0f ? variation.param2 : 14f, 10f);
                    break;

                case RoadElevationType.MountainPass:
                    spline.SetMountainPassPreset(variation.param1 != 0f ? variation.param1 : 45f, variation.param2 > 0f ? variation.param2 : 16f);
                    break;

                case RoadElevationType.ElevatedChicane:
                    spline.SetElevatedChicanePreset(variation.param1 != 0f ? variation.param1 : 35f, variation.param2 > 0f ? variation.param2 : 10f);
                    break;

                case RoadElevationType.Flat:
                default:
                    switch (variation.roadType)
                    {
                        case ChunkRoadType.CurvedRight:
                            spline.SetSCurvePreset(45f);
                            break;
                        case ChunkRoadType.CurvedLeft:
                            spline.SetSCurvePreset(-45f);
                            break;
                        case ChunkRoadType.HazardZone:
                            spline.SetChicanePreset(35f);
                            break;
                        case ChunkRoadType.Straight:
                        default:
                            spline.SetStraightPreset();
                            break;
                    }
                    break;
            }

            roadGen.ApplyCrossSectionPreset(variation.crossSection);
        }

        /// <summary>
        /// Calculates the position and rotation needed so the new chunk's entry socket aligns with the last chunk's exit socket.
        /// </summary>
        private void GetSocketAlignment(Chunk lastChunk, Chunk newPrefab, out Vector3 position, out Quaternion rotation)
        {
            Transform exit = lastChunk.exitSocket;
            if (exit != null)
            {
                position = exit.position;
                rotation = exit.rotation;

                if (newPrefab.entrySocket != null)
                {
                    Vector3 entryLocal = newPrefab.entrySocket.localPosition;
                    position -= rotation * entryLocal;
                }
            }
            else
            {
                position = lastChunk.transform.position + lastChunk.transform.forward * lastChunk.chunkLength;
                rotation = lastChunk.transform.rotation;
            }
        }

        /// <summary>
        /// Selects the next chunk prefab from the pool, ensuring consecutive chunks have different road types.
        /// </summary>
        private Chunk SelectNextPrefab()
        {
            if (chunkPrefab != null) return chunkPrefab;
            if (chunkPrefabs == null || chunkPrefabs.Count == 0) return null;
            if (chunkPrefabs.Count == 1) return chunkPrefabs[0];

            ChunkRoadType? lastRoadType = null;
            if (_activeChunks.Count > 0 && _activeChunks[_activeChunks.Count - 1] != null)
            {
                lastRoadType = _activeChunks[_activeChunks.Count - 1].roadType;
            }

            // Filter for prefabs with a different road type than the last spawned chunk
            List<Chunk> candidates = new List<Chunk>();
            if (lastRoadType.HasValue)
            {
                for (int i = 0; i < chunkPrefabs.Count; i++)
                {
                    if (chunkPrefabs[i] != null && chunkPrefabs[i].roadType != lastRoadType.Value)
                    {
                        candidates.Add(chunkPrefabs[i]);
                    }
                }
            }

            // Fallback to all valid prefabs if none with a different road type exist
            if (candidates.Count == 0)
            {
                for (int i = 0; i < chunkPrefabs.Count; i++)
                {
                    if (chunkPrefabs[i] != null) candidates.Add(chunkPrefabs[i]);
                }
            }

            if (candidates.Count == 0) return null;
            if (candidates.Count == 1) return candidates[0];

            int totalWeight = 0;
            for (int i = 0; i < candidates.Count; i++)
            {
                totalWeight += Mathf.Max(1, candidates[i].spawnWeight);
            }

            int roll = Random.Range(0, totalWeight);
            int cumulative = 0;
            for (int i = 0; i < candidates.Count; i++)
            {
                cumulative += Mathf.Max(1, candidates[i].spawnWeight);
                if (roll < cumulative)
                    return candidates[i];
            }

            return candidates[candidates.Count - 1];
        }

        /// <summary>
        /// Called when the player crosses the chunk's forward spawn trigger.
        /// </summary>
        public void OnChunkSpawnNextRequested(Chunk chunk)
        {
            int index = _activeChunks.IndexOf(chunk);
            if (index >= 0 && index >= _activeChunks.Count - 2)
            {
                SpawnNextChunk();
            }
        }

        /// <summary>
        /// Called when the player enters a chunk and passes the seal trigger.
        /// Seals backward passage and unloads chunks left behind.
        /// </summary>
        public void OnChunkSealed(Chunk chunk)
        {
            UnloadPassedChunks(chunk);
        }

        private void UnloadPassedChunks(Chunk currentChunk)
        {
            int currentIndex = _activeChunks.IndexOf(currentChunk);
            if (currentIndex < 0) return;

            // Unload chunks further behind than maxChunksBehind
            while (_activeChunks.Count > 0)
            {
                int idx = _activeChunks.IndexOf(currentChunk);
                if (idx > maxChunksBehind)
                {
                    UnloadOldestChunk();
                }
                else
                {
                    break;
                }
            }

            // Enforce maxActiveChunks limit
            while (_activeChunks.Count > maxActiveChunks)
            {
                UnloadOldestChunk();
            }
        }

        private void UnloadOldestChunk()
        {
            if (_activeChunks.Count == 0) return;

            Chunk oldest = _activeChunks[0];
            _activeChunks.RemoveAt(0);

            if (oldest != null)
            {
                oldest.UnloadChunk();
            }
        }

        private void CheckRoadAheadDistance()
        {
            Transform target = GetTargetTransform();
            if (target == null || _activeChunks.Count == 0) return;

            Chunk lastChunk = _activeChunks[_activeChunks.Count - 1];
            if (lastChunk == null) return;

            Vector3 endPos = lastChunk.exitSocket != null ? lastChunk.exitSocket.position : lastChunk.transform.position + lastChunk.transform.forward * lastChunk.chunkLength;
            float distToEnd = Vector3.Distance(target.position, endPos);

            // If player is within 450m of the absolute end of all active chunks, spawn next
            if (distToEnd < 450f)
            {
                SpawnNextChunk();
            }
        }

        private Transform GetTargetTransform()
        {
            if (_targetTransform != null && _targetTransform.gameObject.activeInHierarchy)
                return _targetTransform;

            var vehicle = FindAnyObjectByType<EndlessSurvival.Vehicle.VehicleController>();
            if (vehicle != null && vehicle.gameObject.activeInHierarchy)
            {
                _targetTransform = vehicle.transform;
                return _targetTransform;
            }

            var player = GameObject.FindGameObjectWithTag("Player");
            if (player != null && player.activeInHierarchy)
            {
                _targetTransform = player.transform;
                return _targetTransform;
            }

            return null;
        }

        private void ClearChildObjects()
        {
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                Transform child = transform.GetChild(i);
                if (Application.isPlaying)
                    Destroy(child.gameObject);
                else
                    DestroyImmediate(child.gameObject);
            }
        }

        [ContextMenu("Clear All Child Chunks")]
        public void ContextClearChildChunks()
        {
            ClearChildObjects();
        }
    }
}
