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

        [Header("Chunk Prefabs Pool")]
        [Tooltip("Pool of authored 500x500 chunk prefabs")]
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
            if (chunkPrefabs != null && chunkPrefabs.Count > 0)
            {
                SpawnInitialChunks();
            }
            else
            {
                Debug.LogWarning("[ChunkManager] No chunk prefabs assigned.");
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
            if (chunkPrefabs.Count <= 1)
            {
                ApplyDynamicRoadVariation(newChunk);
            }

            newChunk.name = $"Chunk_{_totalSpawnedCount}_{newChunk.biomeType}_{newChunk.roadType}";
            newChunk.Initialize(this, _totalSpawnedCount);

            _activeChunks.Add(newChunk);
            _totalSpawnedCount++;

            return newChunk;
        }

        private static readonly ChunkRoadType[] VariedRoadTypes = new[]
        {
            ChunkRoadType.Straight,
            ChunkRoadType.CurvedRight,
            ChunkRoadType.Straight,
            ChunkRoadType.CurvedLeft,
            ChunkRoadType.HazardZone
        };

        private void ApplyDynamicRoadVariation(Chunk chunk)
        {
            var roadGen = chunk.GetComponentInChildren<RoadGenerator>();
            var spline = chunk.GetComponentInChildren<RoadSpline>();
            if (roadGen == null || spline == null) return;

            ChunkRoadType targetType = VariedRoadTypes[_totalSpawnedCount % VariedRoadTypes.Length];
            chunk.roadType = targetType;

            switch (targetType)
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

            roadGen.BuildRoadMesh();
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
