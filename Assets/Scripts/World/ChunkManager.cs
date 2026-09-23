using System.Collections.Generic;
using UnityEngine;
using EndlessSurvival.World.Road;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace EndlessSurvival.World
{
    public enum ChunkSelectionMode
    {
        WeightedRandom,
        BiomeSequence,
        SingleBiome
    }

    /// <summary>
    /// Coordinates infinite forward chunk streaming from a pool of categorized chunk prefabs.
    /// Supports weighted random selection and biome transition sequences with valid URP materials.
    /// </summary>
    public class ChunkManager : MonoBehaviour
    {
        public static ChunkManager Instance { get; private set; }

        [Header("Chunk Prefab Pool")]
        [Tooltip("Pool of pre-authored 500x500 chunk prefabs")]
        public List<Chunk> chunkPrefabs = new List<Chunk>();

        [Header("Selection & Biome Parameters")]
        [Tooltip("Selection strategy: WeightedRandom, BiomeSequence, or SingleBiome")]
        public ChunkSelectionMode selectionMode = ChunkSelectionMode.BiomeSequence;

        [Tooltip("Fixed biome when SingleBiome selection mode is active")]
        public ChunkBiomeType targetBiome = ChunkBiomeType.Forest;

        [Tooltip("Minimum number of chunks to spawn in the same biome before transitioning")]
        public int minChunksPerBiome = 3;

        [Tooltip("Maximum number of chunks to spawn in the same biome before transitioning")]
        public int maxChunksPerBiome = 6;

        [Header("Streaming Settings")]
        [Tooltip("Number of chunks to spawn at start (usually 2: current and next)")]
        public int initialChunkCount = 2;

        [Tooltip("Maximum chunks to keep behind the player before unloading (Default: 1)")]
        public int maxChunksBehind = 1;

        [Tooltip("Maximum number of active chunks maintained in the scene (Default: 3)")]
        public int maxActiveChunks = 3;

        [Tooltip("World start position for the very first chunk")]
        public Vector3 initialSpawnPosition = Vector3.zero;

        // Runtime state
        private readonly List<Chunk> _activeChunks = new List<Chunk>();
        private int _totalSpawnedCount = 0;
        private ChunkBiomeType _currentBiome;
        private int _remainingChunksInCurrentBiome = 0;

        public IReadOnlyList<Chunk> ActiveChunks => _activeChunks;
        public ChunkBiomeType CurrentBiome => _currentBiome;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            _currentBiome = targetBiome;
            ResetBiomeStreak();

            // Clear any editor preview chunks placed under ChunkManager
            ClearPreviewChildren();
        }

        private void ClearPreviewChildren()
        {
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                Transform child = transform.GetChild(i);
                if (Application.isPlaying)
                {
                    Destroy(child.gameObject);
                }
                else
                {
                    DestroyImmediate(child.gameObject);
                }
            }
        }

        [ContextMenu("Clear Preview Chunks")]
        public void ManualClearPreviewChunks()
        {
            ClearPreviewChildren();
            Debug.Log("[ChunkManager] Cleaned all children under ChunkManager.");
        }

        private void Start()
        {
            if (chunkPrefabs != null && chunkPrefabs.Count > 0)
            {
                SpawnInitialChunks();
            }
            else
            {
                Debug.LogWarning("[ChunkManager] No chunk prefabs assigned. Generate prototype prefabs via context menu.");
            }
        }

        private void ResetBiomeStreak()
        {
            _remainingChunksInCurrentBiome = Random.Range(minChunksPerBiome, maxChunksPerBiome + 1);
        }

        public void SpawnInitialChunks()
        {
            for (int i = 0; i < initialChunkCount; i++)
            {
                SpawnNextChunk();
            }
        }

        public Chunk SpawnNextChunk()
        {
            if (chunkPrefabs == null || chunkPrefabs.Count == 0)
            {
                Debug.LogError("[ChunkManager] Cannot spawn chunk: Chunk prefabs pool is empty!");
                return null;
            }

            ChunkBiomeType activeBiomeForPick = DetermineNextBiome();

            Chunk prefabToSpawn = SelectWeightedChunkPrefab(activeBiomeForPick);
            if (prefabToSpawn == null)
            {
                prefabToSpawn = SelectWeightedChunkPrefabFromList(chunkPrefabs);
            }

            Vector3 spawnPosition = initialSpawnPosition;
            Quaternion spawnRotation = Quaternion.identity;

            if (_activeChunks.Count > 0)
            {
                Chunk lastChunk = _activeChunks[_activeChunks.Count - 1];
                if (lastChunk.exitSocket != null)
                {
                    spawnPosition = lastChunk.exitSocket.position;
                    spawnRotation = lastChunk.exitSocket.rotation;

                    if (prefabToSpawn.entrySocket != null)
                    {
                        Vector3 entryLocalOffset = prefabToSpawn.entrySocket.localPosition;
                        spawnPosition -= spawnRotation * entryLocalOffset;
                    }
                }
            }

            Chunk newChunk = Instantiate(prefabToSpawn, spawnPosition, spawnRotation);
            newChunk.name = $"Chunk_{_totalSpawnedCount}_{prefabToSpawn.biomeType}_{prefabToSpawn.roadType}";
            newChunk.transform.SetParent(transform, true); // Keep Hierarchy neat and tidy
            newChunk.Initialize(this, _totalSpawnedCount);

            _activeChunks.Add(newChunk);
            _totalSpawnedCount++;

            Debug.Log($"[ChunkManager] Spawned {newChunk.name} (Biome: {newChunk.biomeType}) at {spawnPosition}. Active count: {_activeChunks.Count}");
            return newChunk;
        }

        private ChunkBiomeType DetermineNextBiome()
        {
            if (selectionMode == ChunkSelectionMode.SingleBiome)
            {
                return targetBiome;
            }

            if (selectionMode == ChunkSelectionMode.BiomeSequence)
            {
                _remainingChunksInCurrentBiome--;
                if (_remainingChunksInCurrentBiome <= 0)
                {
                    _currentBiome = PickRandomDifferentBiome(_currentBiome);
                    ResetBiomeStreak();
                    Debug.Log($"[ChunkManager] Transitioning to new Biome: {_currentBiome} for next {_remainingChunksInCurrentBiome} chunks.");
                }
                return _currentBiome;
            }

            return _currentBiome;
        }

        private ChunkBiomeType PickRandomDifferentBiome(ChunkBiomeType current)
        {
            var values = (ChunkBiomeType[])System.Enum.GetValues(typeof(ChunkBiomeType));
            if (values.Length <= 1) return current;

            List<ChunkBiomeType> candidates = new List<ChunkBiomeType>();
            foreach (var b in values)
            {
                if (b != current) candidates.Add(b);
            }
            return candidates[Random.Range(0, candidates.Count)];
        }

        private Chunk SelectWeightedChunkPrefab(ChunkBiomeType biome)
        {
            List<Chunk> matching = new List<Chunk>();
            foreach (var p in chunkPrefabs)
            {
                if (p != null && p.biomeType == biome)
                    matching.Add(p);
            }

            if (matching.Count == 0) return null;
            return SelectWeightedChunkPrefabFromList(matching);
        }

        private Chunk SelectWeightedChunkPrefabFromList(List<Chunk> pool)
        {
            int totalWeight = 0;
            foreach (var chunk in pool)
            {
                if (chunk != null) totalWeight += Mathf.Max(1, chunk.spawnWeight);
            }

            if (totalWeight <= 0) return pool[0];

            int roll = Random.Range(0, totalWeight);
            int cumulative = 0;

            foreach (var chunk in pool)
            {
                if (chunk == null) continue;
                cumulative += Mathf.Max(1, chunk.spawnWeight);
                if (roll < cumulative)
                {
                    return chunk;
                }
            }

            return pool[pool.Count - 1];
        }

        private Transform _playerTransform;

        private void Update()
        {
            if (!Application.isPlaying) return;

            FindPlayerIfNeeded();
            if (_playerTransform == null || _activeChunks.Count == 0) return;

            Vector3 playerPos = _playerTransform.position;

            // Distance-based cleanup of chunks left behind
            while (_activeChunks.Count > 1)
            {
                Chunk oldestChunk = _activeChunks[0];
                if (oldestChunk == null)
                {
                    _activeChunks.RemoveAt(0);
                    continue;
                }

                Vector3 exitPos = oldestChunk.exitSocket != null ? oldestChunk.exitSocket.position : oldestChunk.transform.position + oldestChunk.transform.forward * oldestChunk.chunkLength;
                Vector3 exitForward = oldestChunk.exitSocket != null ? oldestChunk.exitSocket.forward : oldestChunk.transform.forward;

                float forwardDistPastExit = Vector3.Dot(playerPos - exitPos, exitForward);
                float thresholdDistance = oldestChunk.chunkLength * Mathf.Max(1, maxChunksBehind);

                if (forwardDistPastExit > thresholdDistance)
                {
                    Debug.Log($"[ChunkManager] Distance-based cleanup of passed chunk: {oldestChunk.name} ({forwardDistPastExit:F1}m past exit)");
                    _activeChunks.RemoveAt(0);
                    oldestChunk.UnloadChunk();
                }
                else
                {
                    break;
                }
            }

            // Distance-based auto-spawn if approaching the end of the road
            if (_activeChunks.Count > 0)
            {
                Chunk lastChunk = _activeChunks[_activeChunks.Count - 1];
                if (lastChunk != null)
                {
                    Vector3 exitPos = lastChunk.exitSocket != null ? lastChunk.exitSocket.position : lastChunk.transform.position + lastChunk.transform.forward * lastChunk.chunkLength;
                    float distToEnd = Vector3.Distance(playerPos, exitPos);
                    if (distToEnd < 600f)
                    {
                        int currentChunkIndex = FindClosestActiveChunkIndex(playerPos);
                        int chunksAhead = (_activeChunks.Count - 1) - currentChunkIndex;
                        if (chunksAhead < 2)
                        {
                            SpawnNextChunk();
                        }
                    }
                }
            }
        }

        private void FindPlayerIfNeeded()
        {
            if (_playerTransform != null && _playerTransform.gameObject.activeInHierarchy) return;

            var vehicle = FindAnyObjectByType<EndlessSurvival.Vehicle.VehicleController>();
            if (vehicle != null && vehicle.gameObject.activeInHierarchy)
            {
                _playerTransform = vehicle.transform;
                return;
            }

            var player = GameObject.FindGameObjectWithTag("Player");
            if (player != null && player.activeInHierarchy)
            {
                _playerTransform = player.transform;
                return;
            }

            var armature = GameObject.Find("PlayerArmature");
            if (armature != null && armature.activeInHierarchy)
            {
                _playerTransform = armature.transform;
            }
        }

        private int FindClosestActiveChunkIndex(Vector3 pos)
        {
            int closest = 0;
            float minSqrDist = float.MaxValue;
            for (int i = 0; i < _activeChunks.Count; i++)
            {
                Chunk c = _activeChunks[i];
                if (c == null) continue;
                Vector3 center = c.transform.position + c.transform.forward * (c.chunkLength * 0.5f);
                float sqrDist = (pos - center).sqrMagnitude;
                if (sqrDist < minSqrDist)
                {
                    minSqrDist = sqrDist;
                    closest = i;
                }
            }
            return closest;
        }

        public void OnChunkSpawnNextRequested(Chunk triggeringChunk)
        {
            int indexInActive = _activeChunks.IndexOf(triggeringChunk);
            if (indexInActive >= 0 && indexInActive >= _activeChunks.Count - 2)
            {
                SpawnNextChunk();
            }
        }

        public void OnChunkSealed(Chunk enteredChunk)
        {
            int enteredIndex = _activeChunks.IndexOf(enteredChunk);
            if (enteredIndex < 0) return;

            // Immediately unload all chunks left further behind than maxChunksBehind
            while (_activeChunks.Count > 0)
            {
                int currentEnteredIndex = _activeChunks.IndexOf(enteredChunk);
                if (currentEnteredIndex > maxChunksBehind)
                {
                    Chunk oldestChunk = _activeChunks[0];
                    _activeChunks.RemoveAt(0);

                    Debug.Log($"[ChunkManager] Unloading chunk left behind: {oldestChunk.name} (Active remaining: {_activeChunks.Count})");
                    oldestChunk.UnloadChunk();
                }
                else
                {
                    break;
                }
            }

            // Also respect maxActiveChunks cap
            while (_activeChunks.Count > maxActiveChunks)
            {
                Chunk oldestChunk = _activeChunks[0];
                _activeChunks.RemoveAt(0);

                Debug.Log($"[ChunkManager] Unloading oldest chunk exceeding max cap: {oldestChunk.name}");
                oldestChunk.UnloadChunk();
            }
        }

#if UNITY_EDITOR
        [MenuItem("Endless Survival/Generate All Chunk Prefabs")]
        public static void MenuItemGenerateAllChunkPrefabs()
        {
            ChunkManager mgr = FindAnyObjectByType<ChunkManager>();
            if (mgr == null)
            {
                GameObject go = new GameObject("ChunkManager");
                mgr = go.AddComponent<ChunkManager>();
            }
            mgr.GenerateAndSaveChunkPrefabs();
        }

        [ContextMenu("Generate and Save Chunk Prefabs")]
        public void GenerateAndSaveChunkPrefabs()
        {
            Debug.Log("[ChunkManager] Generating categorized URP 500x500 chunk prefabs with Spline roads...");

            string chunkFolderPath = "Assets/Prefabs/Chunks";
            string texFolderPath = "Assets/Prefabs/Chunks/Textures";
            string tlFolderPath = "Assets/Prefabs/Chunks/TerrainLayers";
            string tdFolderPath = "Assets/Prefabs/Chunks/TerrainData";
            string matFolderPath = "Assets/Prefabs/Chunks/Materials";
            string meshFolderPath = "Assets/Prefabs/Chunks/Meshes";
            string profileFolderPath = "Assets/Settings/RoadProfiles";

            EnsureFolder("Assets/Prefabs");
            EnsureFolder(chunkFolderPath);
            EnsureFolder(texFolderPath);
            EnsureFolder(tlFolderPath);
            EnsureFolder(tdFolderPath);
            EnsureFolder(matFolderPath);
            EnsureFolder(meshFolderPath);
            EnsureFolder("Assets/Settings");
            EnsureFolder(profileFolderPath);

            // 1. Prepare persistent solid ground textures on disk (eliminates magenta shader / missing texture errors)
            Texture2D texForest = GetOrCreateSolidTexture(texFolderPath, "Tex_Terrain_Forest", new Color(0.24f, 0.40f, 0.20f));
            Texture2D texDesert = GetOrCreateSolidTexture(texFolderPath, "Tex_Terrain_Desert", new Color(0.72f, 0.62f, 0.42f));
            Texture2D texCity = GetOrCreateSolidTexture(texFolderPath, "Tex_Terrain_RuinedCity", new Color(0.34f, 0.34f, 0.36f));
            Texture2D texWasteland = GetOrCreateSolidTexture(texFolderPath, "Tex_Terrain_Wasteland", new Color(0.38f, 0.30f, 0.22f));

            // 2. Prepare persistent TerrainLayer assets pointing to real imported textures
            TerrainLayer layerForest = GetOrCreateTerrainLayer(tlFolderPath, "Layer_Forest", texForest);
            TerrainLayer layerDesert = GetOrCreateTerrainLayer(tlFolderPath, "Layer_Desert", texDesert);
            TerrainLayer layerCity = GetOrCreateTerrainLayer(tlFolderPath, "Layer_RuinedCity", texCity);
            TerrainLayer layerWasteland = GetOrCreateTerrainLayer(tlFolderPath, "Layer_Wasteland", texWasteland);

            // 3. Prepare road and blockade materials
            Material roadMat = GetOrCreateURPMaterial(matFolderPath, "Mat_Road_Dark", new Color(0.18f, 0.18f, 0.18f));
            Material forestMat = GetOrCreateURPMaterial(matFolderPath, "Mat_Chunk_Forest", new Color(0.24f, 0.42f, 0.22f));
            Material desertMat = GetOrCreateURPMaterial(matFolderPath, "Mat_Chunk_Desert", new Color(0.62f, 0.52f, 0.32f));
            Material cityMat = GetOrCreateURPMaterial(matFolderPath, "Mat_Chunk_RuinedCity", new Color(0.32f, 0.32f, 0.36f));
            Material wastelandMat = GetOrCreateURPMaterial(matFolderPath, "Mat_Chunk_Wasteland", new Color(0.42f, 0.35f, 0.26f));
            Material blockadeMat = GetOrCreateURPMaterial(matFolderPath, "Mat_Chunk_Blockade", new Color(0.7f, 0.18f, 0.18f));

            // 4. Prepare Parametric Road Profiles per Biome
            RoadProfile profileForest = GetOrCreateRoadProfile(profileFolderPath, "RoadProfile_Forest", ChunkBiomeType.Forest, 12f, 2.5f, roadMat, forestMat, false);
            RoadProfile profileDesert = GetOrCreateRoadProfile(profileFolderPath, "RoadProfile_Desert", ChunkBiomeType.Desert, 14f, 3.5f, roadMat, desertMat, false);
            RoadProfile profileCity = GetOrCreateRoadProfile(profileFolderPath, "RoadProfile_RuinedCity", ChunkBiomeType.RuinedCity, 14f, 2.0f, roadMat, cityMat, false);
            RoadProfile profileWasteland = GetOrCreateRoadProfile(profileFolderPath, "RoadProfile_Wasteland", ChunkBiomeType.Wasteland, 10f, 4.0f, roadMat, wastelandMat, false);

            chunkPrefabs.Clear();

            // 5. Generate Single Master Chunk Prototype: Chunk_Forest_Curve
            Chunk forestCurve = CreateChunkGameObject("Chunk_Forest_Curve", ChunkBiomeType.Forest, ChunkRoadType.CurvedRight, layerForest, profileForest, blockadeMat, 10);
            SaveChunkAsPrefab(forestCurve, chunkFolderPath);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[ChunkManager] Successfully created single master chunk prototype: Chunk_Forest_Curve!");
        }

        private void EnsureFolder(string path)
        {
            if (!AssetDatabase.IsValidFolder(path))
            {
                int lastSlash = path.LastIndexOf('/');
                if (lastSlash > 0)
                {
                    string parent = path.Substring(0, lastSlash);
                    string child = path.Substring(lastSlash + 1);
                    EnsureFolder(parent);
                    AssetDatabase.CreateFolder(parent, child);
                }
            }
        }

        private Texture2D GetOrCreateSolidTexture(string folder, string name, Color color)
        {
            string path = $"{folder}/{name}.png";
            Texture2D tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (tex != null) return tex;

            tex = new Texture2D(64, 64, TextureFormat.RGBA32, true);
            Color[] pixels = new Color[64 * 64];
            for (int y = 0; y < 64; y++)
            {
                for (int x = 0; x < 64; x++)
                {
                    float noise = Mathf.PerlinNoise(x * 0.15f, y * 0.15f) * 0.12f - 0.06f;
                    pixels[y * 64 + x] = new Color(
                        Mathf.Clamp01(color.r + noise),
                        Mathf.Clamp01(color.g + noise),
                        Mathf.Clamp01(color.b + noise),
                        1f
                    );
                }
            }
            tex.SetPixels(pixels);
            tex.Apply();

            byte[] bytes = tex.EncodeToPNG();
            System.IO.File.WriteAllBytes(path, bytes);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);

            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer != null)
            {
                importer.wrapMode = TextureWrapMode.Repeat;
                importer.filterMode = FilterMode.Bilinear;
                importer.SaveAndReimport();
            }

            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        private TerrainLayer GetOrCreateTerrainLayer(string folder, string name, Texture2D diffuseTex)
        {
            string path = $"{folder}/{name}.terrainlayer";
            TerrainLayer layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(path);
            if (layer == null)
            {
                layer = new TerrainLayer();
                layer.diffuseTexture = diffuseTex;
                layer.tileSize = new Vector2(12f, 12f);
                layer.smoothness = 0.05f;
                layer.specular = Color.black;
                AssetDatabase.CreateAsset(layer, path);
            }
            else
            {
                layer.diffuseTexture = diffuseTex;
                layer.tileSize = new Vector2(12f, 12f);
                EditorUtility.SetDirty(layer);
            }
            return layer;
        }

        private Material GetOrCreateTerrainMaterial(string folder, string name)
        {
            string path = $"{folder}/{name}.mat";
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            Shader terrainShader = Shader.Find("Universal Render Pipeline/Terrain/Lit");
            if (terrainShader == null) terrainShader = Shader.Find("Universal Render Pipeline/Lit");
            if (terrainShader == null) terrainShader = Shader.Find("Standard");

            if (mat == null)
            {
                mat = new Material(terrainShader);
                AssetDatabase.CreateAsset(mat, path);
            }
            else
            {
                mat.shader = terrainShader;
                EditorUtility.SetDirty(mat);
            }
            return mat;
        }

        private RoadProfile GetOrCreateRoadProfile(string folderPath, string profileName, ChunkBiomeType biome, float roadW, float shoulderW, Material roadMat, Material shoulderMat, bool guardrails)
        {
            string path = $"{folderPath}/{profileName}.asset";
            RoadProfile profile = AssetDatabase.LoadAssetAtPath<RoadProfile>(path);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<RoadProfile>();
                profile.targetBiome = biome;
                profile.roadWidth = roadW;
                profile.shoulderWidth = shoulderW;
                profile.roadMaterial = roadMat;
                profile.shoulderMaterial = shoulderMat;
                profile.hasGuardrails = guardrails;
                AssetDatabase.CreateAsset(profile, path);
            }
            else
            {
                profile.targetBiome = biome;
                profile.roadWidth = roadW;
                profile.shoulderWidth = shoulderW;
                profile.roadMaterial = roadMat;
                profile.shoulderMaterial = shoulderMat;
                profile.hasGuardrails = guardrails;
                EditorUtility.SetDirty(profile);
            }
            return profile;
        }

        private Material GetOrCreateURPMaterial(string folderPath, string matName, Color color)
        {
            string matPath = $"{folderPath}/{matName}.mat";
            Material existingMat = AssetDatabase.LoadAssetAtPath<Material>(matPath);

            Shader urpShader = Shader.Find("Universal Render Pipeline/Lit");
            if (urpShader == null)
            {
                Material sampleURP = AssetDatabase.LoadAssetAtPath<Material>("Assets/Pack_Adventure/Materials/Ground.mat");
                if (sampleURP != null) urpShader = sampleURP.shader;
            }
            if (urpShader == null) urpShader = Shader.Find("Standard");

            if (existingMat == null)
            {
                existingMat = new Material(urpShader);
                if (existingMat.HasProperty("_BaseColor"))
                    existingMat.SetColor("_BaseColor", color);
                existingMat.color = color;

                AssetDatabase.CreateAsset(existingMat, matPath);
            }
            else
            {
                existingMat.shader = urpShader;
                if (existingMat.HasProperty("_BaseColor"))
                    existingMat.SetColor("_BaseColor", color);
                existingMat.color = color;
                EditorUtility.SetDirty(existingMat);
            }

            return existingMat;
        }

        private GameObject CreateChunkTerrain(string chunkName, Transform parent, TerrainLayer terrainLayer)
        {
            string tdPath = $"Assets/Prefabs/Chunks/TerrainData/TerrainData_{chunkName}.asset";
            if (AssetDatabase.LoadAssetAtPath<TerrainData>(tdPath) != null)
            {
                AssetDatabase.DeleteAsset(tdPath);
            }

            TerrainData td = new TerrainData();
            // CRITICAL: heightmapResolution must be set BEFORE size, because setting heightmapResolution resets size to (1000, 600, 1000)!
            td.heightmapResolution = 129;
            td.baseMapResolution = 256;
            td.size = new Vector3(500f, 60f, 500f);

            td.terrainLayers = new TerrainLayer[] { terrainLayer };

            // Initialize alphamap to 100% layer 0 to guarantee correct rendering
            int alphamapWidth = td.alphamapWidth;
            int alphamapHeight = td.alphamapHeight;
            float[,,] alphas = new float[alphamapHeight, alphamapWidth, 1];
            for (int y = 0; y < alphamapHeight; y++)
            {
                for (int x = 0; x < alphamapWidth; x++)
                {
                    alphas[y, x, 0] = 1f;
                }
            }
            td.SetAlphamaps(0, 0, alphas);

            AssetDatabase.CreateAsset(td, tdPath);
            AssetDatabase.SaveAssets();

            GameObject terrainGo = Terrain.CreateTerrainGameObject(td);
            terrainGo.name = "Terrain_500x500";
            terrainGo.transform.SetParent(parent, false);
            // Center the 500x500 terrain: X ranges from -250 to +250, Z from 0 to 500
            terrainGo.transform.localPosition = new Vector3(-250f, 0f, 0f);

            Terrain terrain = terrainGo.GetComponent<Terrain>();
            if (terrain != null)
            {
                Material savedTerrainMat = GetOrCreateTerrainMaterial("Assets/Prefabs/Chunks/Materials", $"Mat_Terrain_{chunkName}");
                terrain.materialTemplate = savedTerrainMat;
                terrain.drawHeightmap = true;
                terrain.heightmapPixelError = 1;
            }

            return terrainGo;
        }

        private Chunk CreateChunkGameObject(string name, ChunkBiomeType biome, ChunkRoadType roadType, TerrainLayer terrainLayer, RoadProfile roadProfile, Material blockadeMat, int weight)
        {
            GameObject chunkGo = new GameObject(name);
            Chunk chunk = chunkGo.AddComponent<Chunk>();
            chunk.chunkLength = 500f;
            chunk.biomeType = biome;
            chunk.roadType = roadType;
            chunk.spawnWeight = weight;

            // 500x500 Unity Terrain
            CreateChunkTerrain(name, chunkGo.transform, terrainLayer);

            // Sockets
            GameObject entry = new GameObject("EntrySocket");
            entry.transform.SetParent(chunkGo.transform);
            entry.transform.localPosition = Vector3.zero;
            chunk.entrySocket = entry.transform;

            GameObject exit = new GameObject("ExitSocket");
            exit.transform.SetParent(chunkGo.transform);
            exit.transform.localPosition = new Vector3(0f, 0f, 500f);
            chunk.exitSocket = exit.transform;

            // Procedural Dynamic Spline Road
            GameObject roadGo = new GameObject("Spline_Road");
            roadGo.transform.SetParent(chunkGo.transform, false);

            RoadSpline spline = roadGo.AddComponent<RoadSpline>();
            spline.resolution = 100;
            if (roadType == ChunkRoadType.CurvedRight)
            {
                spline.SetSCurvePreset(45f);
            }
            else if (roadType == ChunkRoadType.CurvedLeft)
            {
                spline.SetSCurvePreset(-45f);
            }
            else if (roadType == ChunkRoadType.HazardZone)
            {
                spline.SetChicanePreset(35f);
            }
            else
            {
                spline.SetStraightPreset();
            }

            RoadGenerator roadGen = roadGo.AddComponent<RoadGenerator>();
            roadGen.spline = spline;
            roadGen.activeProfile = roadProfile;
            roadGen.roadElevation = 0.45f;
            roadGen.roadThickness = 0.80f;
            roadGen.syncExitSocket = true;
            roadGen.generateOnStart = false; // Baked into mesh asset
            Mesh generatedMesh = roadGen.BuildRoadMesh();

            // Bake mesh directly to disk so it is 100% visible in the Scene view without running the game
            string meshFolder = "Assets/Prefabs/Chunks/Meshes";
            string meshPath = $"{meshFolder}/Mesh_{name}.asset";
            if (AssetDatabase.LoadAssetAtPath<Mesh>(meshPath) != null)
            {
                AssetDatabase.DeleteAsset(meshPath);
            }
            AssetDatabase.CreateAsset(generatedMesh, meshPath);
            roadGo.GetComponent<MeshFilter>().sharedMesh = generatedMesh;
            roadGo.GetComponent<MeshCollider>().sharedMesh = generatedMesh;

            // Boundary Walls
            CreateBoundary(chunkGo.transform, "LeftBoundary", new Vector3(-250f, 10f, 250f), new Vector3(2f, 20f, 500f));
            CreateBoundary(chunkGo.transform, "RightBoundary", new Vector3(250f, 10f, 250f), new Vector3(2f, 20f, 500f));

            // Spawn Next Trigger (Z = 380m)
            GameObject triggerNextGo = new GameObject("Trigger_SpawnNext");
            triggerNextGo.transform.SetParent(chunkGo.transform);
            triggerNextGo.transform.localPosition = new Vector3(0f, 5f, 380f);

            BoxCollider triggerNextCol = triggerNextGo.AddComponent<BoxCollider>();
            triggerNextCol.isTrigger = true;
            triggerNextCol.size = new Vector3(140f, 15f, 15f);

            ChunkTrigger triggerNext = triggerNextGo.AddComponent<ChunkTrigger>();
            triggerNext.triggerType = ChunkTrigger.TriggerType.SpawnNextChunk;
            triggerNext.parentChunk = chunk;
            chunk.spawnNextTrigger = triggerNext;

            // Seal Passage Trigger (Z = 40m)
            GameObject triggerSealGo = new GameObject("Trigger_SealPassage");
            triggerSealGo.transform.SetParent(chunkGo.transform);
            triggerSealGo.transform.localPosition = new Vector3(0f, 5f, 40f);

            BoxCollider triggerSealCol = triggerSealGo.AddComponent<BoxCollider>();
            triggerSealCol.isTrigger = true;
            triggerSealCol.size = new Vector3(140f, 15f, 15f);

            ChunkTrigger triggerSeal = triggerSealGo.AddComponent<ChunkTrigger>();
            triggerSeal.triggerType = ChunkTrigger.TriggerType.SealBackwardPassage;
            triggerSeal.parentChunk = chunk;

            // Back Blockade Wall (Z = 2m)
            GameObject blockadeGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
            blockadeGo.name = "BackBlockade_Wall";
            blockadeGo.transform.SetParent(chunkGo.transform);
            blockadeGo.transform.localPosition = new Vector3(0f, 5f, 2f);
            blockadeGo.transform.localScale = new Vector3(40f, 10f, 2f);

            var blockadeRenderer = blockadeGo.GetComponent<Renderer>();
            if (blockadeRenderer != null && blockadeMat != null)
            {
                blockadeRenderer.sharedMaterial = blockadeMat;
            }

            blockadeGo.SetActive(false);
            chunk.backBlockade = blockadeGo;

            return chunk;
        }

        private void SaveChunkAsPrefab(Chunk chunkInstance, string folderPath)
        {
            string prefabPath = $"{folderPath}/{chunkInstance.gameObject.name}.prefab";
            GameObject savedPrefab = PrefabUtility.SaveAsPrefabAssetAndConnect(chunkInstance.gameObject, prefabPath, InteractionMode.AutomatedAction);
            if (savedPrefab != null)
            {
                Chunk savedChunkComponent = savedPrefab.GetComponent<Chunk>();
                if (savedChunkComponent != null)
                {
                    chunkPrefabs.Add(savedChunkComponent);
                }
            }
            DestroyImmediate(chunkInstance.gameObject);
        }

        private void CreateBoundary(Transform parent, string name, Vector3 localPos, Vector3 scale)
        {
            GameObject wall = new GameObject(name);
            wall.transform.SetParent(parent);
            wall.transform.localPosition = localPos;
            BoxCollider col = wall.AddComponent<BoxCollider>();
            col.size = scale;
        }
#endif
    }
}
