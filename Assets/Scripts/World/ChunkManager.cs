using System.Collections;
using System.Collections.Generic;
using System.Text;
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

        [Header("Seed")]
        [Tooltip("Master seed text. Leave empty for a random seed each run. Same seed always produces the same world.")]
        public string masterSeed = "";

        /// <summary>Resolved integer seed used for all generation this session.</summary>
        public int ResolvedSeed { get; private set; }

        /// <summary>Ordered list of chunk seeds generated so far (index = chunk index).</summary>
        private readonly List<int> _chunkSeeds = new List<int>();

        [Header("Chunk Prefab")]
        [Tooltip("The single base 500x500 chunk prefab used for endless generation")]
        public Chunk chunkPrefab;

        [Tooltip("Optional list fallback")]
        public List<Chunk> chunkPrefabs = new List<Chunk>();

        [System.Serializable]
        public class BiomeRunSettings
        {
            public ChunkBiomeType biome = ChunkBiomeType.Forest;
            [Tooltip("Bu biyomun bir sonraki bölüm olarak seçilme ağırlığı")]
            [Range(0, 100)] public int weight = 50;
            [Tooltip("Bölümün kaç chunk süreceği (min-max)")]
            [Range(1, 20)] public int minLength = 2;
            [Range(1, 20)] public int maxLength = 4;
        }

        [Header("Biome Sequence (Biyom Sırası)")]
        [Tooltip("Kapalıysa her chunk prefab'ın kendi biyomunu kullanır")]
        public bool enableBiomeVariation = true;

        [Tooltip("Başlangıçta garanti orman chunk sayısı")]
        [Range(1, 10)]
        public int initialForestChunks = 2;

        [Tooltip("Biyom bölümleri. Aynı biyom art arda iki bölüm olarak gelmez.")]
        public List<BiomeRunSettings> biomeRuns = new List<BiomeRunSettings>
        {
            new BiomeRunSettings { biome = ChunkBiomeType.Forest, weight = 45, minLength = 2, maxLength = 5 },
            new BiomeRunSettings { biome = ChunkBiomeType.Coast, weight = 30, minLength = 2, maxLength = 4 },
            new BiomeRunSettings { biome = ChunkBiomeType.Field, weight = 35, minLength = 2, maxLength = 4 },
            new BiomeRunSettings { biome = ChunkBiomeType.Mountain, weight = 30, minLength = 4, maxLength = 7 }
        };

        // Scenes saved with older defaults are upgraded once (see EnsureSettingsDefaults)
        [SerializeField, HideInInspector, UnityEngine.Serialization.FormerlySerializedAs("_biomeRunsVersion")]
        private int _settingsVersion;
        private const int SettingsVersion = 4;

        private void OnValidate()
        {
            EnsureSettingsDefaults();
        }

        private void EnsureSettingsDefaults()
        {
            if (_settingsVersion >= SettingsVersion) return;

            // v1: Field biome
            if (_settingsVersion < 1 && biomeRuns != null && !biomeRuns.Exists(r => r != null && r.biome == ChunkBiomeType.Field))
            {
                biomeRuns.Add(new BiomeRunSettings { biome = ChunkBiomeType.Field, weight = 35, minLength = 2, maxLength = 4 });
            }

            // v2: time-sliced builds -> keep two chunks ready ahead (new chunks appear ~1km away)
            if (_settingsVersion < 2)
            {
                chunksAhead = Mathf.Max(chunksAhead, 2);
                maxActiveChunks = Mathf.Max(maxActiveChunks, maxChunksBehind + 1 + chunksAhead);
            }

            // v3: Snow (karlı dağ) biome
            if (_settingsVersion < 3 && biomeRuns != null && !biomeRuns.Exists(r => r != null && r.biome == ChunkBiomeType.Mountain))
            {
                biomeRuns.Add(new BiomeRunSettings { biome = ChunkBiomeType.Mountain, weight = 30, minLength = 4, maxLength = 7 });
            }

            // v4: the mountain road climbs ~40m per chunk to 100m+, which needs longer mountain runs
            if (_settingsVersion < 4 && biomeRuns != null)
            {
                foreach (var run in biomeRuns)
                {
                    if (run == null || run.biome != ChunkBiomeType.Mountain) continue;
                    run.minLength = Mathf.Max(run.minLength, 4);
                    run.maxLength = Mathf.Max(run.maxLength, 7);
                }
            }

            _settingsVersion = SettingsVersion;
        }

        [Tooltip("Sonsuz deniz yüzeyi materyali (boşsa otomatik üretilir)")]
        public Material oceanMaterial;

        private struct BiomePlanEntry
        {
            public ChunkBiomeType biome;
            public CoastSide seaSide;           // coast: sea side; mountain: side of the mountain a ledge road follows
            public MountainVariant variant;     // mountain sub-type, same for the whole run
        }

        private readonly List<BiomePlanEntry> _biomePlan = new List<BiomePlanEntry>();
        private SeededRandom _biomeRng;
        private ChunkBiomeType _lastRunBiome;
        private OceanSurface _ocean;

        [Header("Streaming Settings")]
        [Tooltip("Number of chunks to spawn when the game starts")]
        [Range(1, 5)]
        public int initialChunkCount = 2;

        [Tooltip("Number of ready chunks kept ahead of the chunk the player is in. 2 = entering chunk N spawns chunk N+2, so new chunks appear ~1km away.")]
        [Range(1, 3)]
        public int chunksAhead = 2;

        [Tooltip("Maximum chunks to keep behind the player before unloading")]
        [Range(0, 3)]
        public int maxChunksBehind = 1;

        [Tooltip("Maximum total chunks allowed simultaneously in the scene (behind + current + ahead)")]
        [Range(2, 6)]
        public int maxActiveChunks = 4;

        [Header("Time-Sliced Build (Takılmasız Yükleme)")]
        [Tooltip("Yeni chunk'lar birkaç kareye yayılarak kurulur. Bir karede kuruluma harcanabilecek süre (ms). Ağır tek adımlar bunu aşabilir ama kare başına en az bir adım yapılır.")]
        [Range(1f, 16f)]
        public float buildBudgetMs = 4f;

        [Tooltip("Henüz yüklenmemiş kaç chunk ilerisinin arka plan dağ silüeti önceden gösterilsin")]
        [Range(0, 6)]
        public int backdropProxyChunks = 3;

        [Tooltip("Bir chunk'ın herhangi bir yapım adımı Slow Step Log Ms'i aşarsa, adım sürelerini tek satır olarak log'a yazar (takılma teşhisi için)")]
        public bool logSlowBuilds = true;
        [Range(1f, 50f)]
        public float slowStepLogMs = 8f;

        private class PendingBuild
        {
            public Chunk chunk;
            public IEnumerator steps;
            public int index;
            public double spawnMs;
            private readonly StringBuilder _timings = new StringBuilder();
            private double _total, _max;
            private string _maxLabel;

            public void RecordStep(string label, double ms)
            {
                _total += ms;
                if (ms > _max) { _max = ms; _maxLabel = label; }
                _timings.Append(label).Append(' ').Append(ms.ToString("0.0")).Append("ms, ");
            }

            public void LogIfSlow(float thresholdMs)
            {
                if (_max < thresholdMs && spawnMs < thresholdMs) return;
                string biome = chunk != null ? chunk.biomeType.ToString() : "?";
                Debug.Log($"[ChunkBuild] Chunk {index} ({biome}): instantiate {spawnMs:0.0}ms, adımlar toplam {_total:0.0}ms, " +
                          $"en ağır '{_maxLabel}' {_max:0.0}ms | {_timings}");
            }
        }

        private readonly Queue<PendingBuild> _buildQueue = new Queue<PendingBuild>();
        private PendingBuild _currentBuild;
        private readonly System.Diagnostics.Stopwatch _buildTimer = new System.Diagnostics.Stopwatch();

        private readonly Dictionary<int, GameObject> _backdropProxies = new Dictionary<int, GameObject>();
        private ChunkBackdropGenerator _proxyBackdrop;
        private Transform _proxyRoot;

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
            EnsureSettingsDefaults();

            // Resolve the master seed
            if (string.IsNullOrEmpty(masterSeed))
            {
                masterSeed = GenerateRandomSeedText(12);
                Debug.Log($"[ChunkManager] No seed provided. Generated random seed: {masterSeed}");
            }
            ResolvedSeed = SeededRandom.HashString(masterSeed);
            Debug.Log($"[ChunkManager] Master seed: \"{masterSeed}\" -> hash: {ResolvedSeed}");

            ClearChildObjects();
        }

        private void Start()
        {
            if (chunkPrefab != null || (chunkPrefabs != null && chunkPrefabs.Count > 0))
            {
                // Endless sea at sea level: visible on coasts, hidden under land elsewhere
                if (_ocean == null)
                {
                    _ocean = OceanSurface.Create(null, initialSpawnPosition.y + WorldConstants.SeaLevel, oceanMaterial);
                }
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

            // Keep exactly `chunksAhead` chunks in front of the chunk the player is currently in
            Chunk current = GetChunkContainingTarget();
            if (current != null)
            {
                EnsureChunksAhead(current);

                // The chunk the player is in and the next one must never be half built (only at extreme speed)
                int idx = _activeChunks.IndexOf(current);
                if (!current.IsBuilt) CompleteBuildsUpTo(current);
                if (idx + 1 < _activeChunks.Count && !_activeChunks[idx + 1].IsBuilt) CompleteBuildsUpTo(_activeChunks[idx + 1]);

                // Same cleanup the seal trigger does, in case that trigger is missed
                UnloadPassedChunks(current);
            }

            ProcessBuildQueue();
        }

        public void SpawnInitialChunks()
        {
            // The player starts on these, so they are built right away
            for (int i = 0; i < initialChunkCount; i++)
            {
                SpawnNextChunk(immediate: true);
            }
        }

        // ---------------------------------------------------------------
        // Time-sliced chunk build
        // ---------------------------------------------------------------

        /// <summary>Runs queued build steps until this frame's time budget is used up.</summary>
        private void ProcessBuildQueue()
        {
            _buildTimer.Restart();
            while (_buildTimer.Elapsed.TotalMilliseconds < buildBudgetMs)
            {
                if (_currentBuild == null)
                {
                    if (_buildQueue.Count == 0) return;
                    _currentBuild = _buildQueue.Dequeue();
                }
                if (!StepBuild(_currentBuild, false)) _currentBuild = null;
            }
        }

        /// <summary>Finishes queued builds synchronously until the given chunk is built (builds run in spawn order).</summary>
        private void CompleteBuildsUpTo(Chunk chunk)
        {
            while (chunk != null && !chunk.IsBuilt && (_currentBuild != null || _buildQueue.Count > 0))
            {
                if (_currentBuild == null) _currentBuild = _buildQueue.Dequeue();
                if (!StepBuild(_currentBuild, true)) _currentBuild = null;
            }
        }

        /// <summary>
        /// Runs one build step and, with <see cref="logSlowBuilds"/>, records how long it took. Step names come
        /// from the labels BuildChunkSteps yields. A chunk with any step over <see cref="slowStepLogMs"/> logs
        /// one summary line when it finishes.
        /// </summary>
        private bool StepBuild(PendingBuild build, bool forced)
        {
            if (build.chunk == null) return false;

            long start = System.Diagnostics.Stopwatch.GetTimestamp();
            bool more = build.steps.MoveNext();
            double ms = (System.Diagnostics.Stopwatch.GetTimestamp() - start) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;

            if (logSlowBuilds)
            {
                string label = more ? (build.steps.Current as string ?? "step") : "finish";
                build.RecordStep(forced ? label + "(senkron)" : label, ms);
                if (!more) build.LogIfSlow(slowStepLogMs);
            }
            return more;
        }

        private static void RunToEnd(IEnumerator steps)
        {
            while (steps.MoveNext()) { }
        }

        /// <summary>
        /// Builds a chunk in steps (one heavy job per frame). Order matters: POIs before terrain (keepouts),
        /// terrain before rocks/edges/vegetation (they sample its height). Loot and notes wait for <see cref="Chunk.Built"/>.
        /// </summary>
        private IEnumerator BuildChunkSteps(Chunk chunk, int index, int seed)
        {
            // 1) Camp (with its enemies), POIs, story event: one per frame
            IEnumerator init = chunk.InitializeSteps(this, index, seed);
            while (init.MoveNext()) yield return init.Current;

            var roadGen = chunk.GetComponentInChildren<RoadGenerator>();
            var spline = chunk.GetComponentInChildren<RoadSpline>();
            var terrain = chunk.GetComponentInChildren<Terrain>();

            if (roadGen != null && spline != null)
            {
                // Keep the half-built terrain hidden; the backdrop proxy stands in for it meanwhile
                if (terrain != null && Application.isPlaying)
                {
                    terrain.drawHeightmap = false;
                    terrain.drawTreesAndFoliage = false;
                }

                // 2) Road shape and cross section
                if (elevationMode == ElevationSelectionMode.DynamicWeightedRandom)
                    ApplyProceduralElevation(chunk, roadGen, spline);
                else
                    ApplySequentialElevationPreset(chunk, roadGen, spline);
                AlignRoadObjects(chunk, spline);
                yield return "road";

                // Side boundaries (cliffs / giant rocks) so the world edge is never visible
                var boundary = chunk.GetComponent<ChunkBoundaryGenerator>();
                if (boundary == null) boundary = chunk.gameObject.AddComponent<ChunkBoundaryGenerator>();

                // Field chunks: fences / tree rows / corn / streams instead of cliffs (must exist before terrain conform)
                var fieldEdges = chunk.GetComponent<FieldEdgeGenerator>();
                if (fieldEdges == null && chunk.IsField) fieldEdges = chunk.gameObject.AddComponent<FieldEdgeGenerator>();

                // 3) Terrain under and around the road (also cliffs, coast, field hills, river channel)
                if (terrain != null)
                {
                    RoadTerrainAdapter.ConformTerrainToRoad(terrain, spline, roadGen, true, paintLayers: false);
                    chunk.SnapPointsOfInterestToTerrain();
                    yield return "terrain";

                    // 4) Sand / soil painting
                    RoadTerrainAdapter.PaintTerrainLayers(terrain);
                    yield return "paint";
                }

                // 5) Boundary rocks
                boundary.SpawnBoundaryRocks(chunk.transform, terrain);
                yield return "rocks";

                // 6) Background mountains outside the boundary (seamless across chunks via global Z noise)
                var backdrop = chunk.GetComponent<ChunkBackdropGenerator>();
                if (backdrop == null) backdrop = chunk.gameObject.AddComponent<ChunkBackdropGenerator>();
                backdrop.Generate(chunk.transform, terrain);
                yield return "backdrop";

                // 7) Field edges: one side per frame, then river/bridge
                if (fieldEdges != null)
                {
                    IEnumerator fieldSteps = fieldEdges.GenerateSteps(chunk.transform, terrain);
                    while (fieldSteps.MoveNext()) yield return "fieldEdges";
                }

                // 8) Abandoned vehicle wrecks on and beside the road (before vegetation: trees keep clear of them)
                var wrecks = chunk.GetComponent<AbandonedVehicleSpawner>();
                if (wrecks == null) wrecks = chunk.gameObject.AddComponent<AbandonedVehicleSpawner>();
                wrecks.Generate(chunk.transform, terrain, chunk.WrecksRandom);
                yield return "wrecks";

                // 9) Vegetation, conforming to the final terrain heights & road curve
                var vegSpawner = chunk.GetComponentInChildren<ChunkVegetationSpawner>();
                if (vegSpawner != null)
                {
                    if (overrideChunkVegetationDensity)
                        vegSpawner.densityMultiplier = globalVegetationDensity;
                    vegSpawner.GenerateVegetation();
                    yield return "vegetation";
                }

                if (terrain != null)
                {
                    terrain.drawHeightmap = true;
                    terrain.drawTreesAndFoliage = true;
                }
            }

            chunk.name = $"Chunk_{index}_{chunk.biomeType}_{chunk.roadType}_{chunk.crossSectionType}";
            chunk.MarkBuilt();
            RemoveBackdropProxy(index);
        }

        // ---------------------------------------------------------------
        // Backdrop silhouettes of chunks that are not loaded yet
        // ---------------------------------------------------------------

        /// <summary>
        /// Keeps mountain silhouettes (plus flat ground) for the next <see cref="backdropProxyChunks"/> unspawned
        /// chunks, so the horizon is already there before the chunk itself loads.
        /// </summary>
        private void UpdateBackdropProxies()
        {
            if (!Application.isPlaying || backdropProxyChunks <= 0 || _activeChunks.Count == 0) return;
            Chunk last = _activeChunks[_activeChunks.Count - 1];
            if (last == null) return;

            if (_proxyBackdrop == null)
            {
                _proxyRoot = new GameObject("BackdropProxies").transform;
                _proxyRoot.SetParent(transform, false);
                _proxyBackdrop = _proxyRoot.gameObject.AddComponent<ChunkBackdropGenerator>();

                // Same mountain settings as the chunks themselves
                Chunk source = chunkPrefab != null ? chunkPrefab : last;
                var sourceBackdrop = source.GetComponent<ChunkBackdropGenerator>();
                if (sourceBackdrop != null) JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(sourceBackdrop), _proxyBackdrop);
            }

            int lastIndex = _totalSpawnedCount - 1;
            // Same value as Chunk.WorldSeed (the last chunk may not be initialized yet while its build is queued)
            int worldSeed = ResolvedSeed != 0 ? ResolvedSeed : SeededRandom.HashString(masterSeed);
            bool planned = enableBiomeVariation && biomeRuns != null && biomeRuns.Count > 0;
            var fixedEntry = new BiomePlanEntry { biome = last.biomeType, seaSide = last.seaSide };

            for (int i = _totalSpawnedCount; i < _totalSpawnedCount + backdropProxyChunks; i++)
            {
                if (_backdropProxies.ContainsKey(i)) continue;

                BiomePlanEntry entry = planned ? GetBiomePlan(i) : fixedEntry;
                BiomePlanEntry prevEntry = planned ? GetBiomePlan(i - 1) : fixedEntry;
                BiomePlanEntry nextEntry = planned ? GetBiomePlan(i + 1) : fixedEntry;
                Vector3 position = last.transform.position + last.transform.forward * (last.chunkLength * (i - lastIndex));

                _backdropProxies[i] = _proxyBackdrop.GenerateProxy(_proxyRoot, position, last.transform.rotation, i,
                    entry.biome, prevEntry.biome, nextEntry.biome, entry.variant, prevEntry.variant, nextEntry.variant, entry.seaSide,
                    IsSameCoast(prevEntry, entry), IsSameCoast(nextEntry, entry),
                    worldSeed, last.chunkLength, last.baseElevation, 250f, 200f, SeamRoadOffset(i), SeamRoadOffset(i + 1),
                    SeamRoadHeight(i, last.baseElevation), SeamRoadHeight(i + 1, last.baseElevation));
            }
        }

        private void RemoveBackdropProxy(int index)
        {
            if (!_backdropProxies.TryGetValue(index, out GameObject proxy)) return;
            if (proxy != null) Destroy(proxy);
            _backdropProxies.Remove(index);
        }

        /// <summary>
        /// Instantiates the next chunk, aligns its entry socket to the previous chunk's exit socket and queues
        /// its build. <paramref name="immediate"/> (and edit mode) builds it completely in this call.
        /// </summary>
        public Chunk SpawnNextChunk(bool immediate = false)
        {
            // Derive a deterministic seed for this chunk index
            int chunkSeed = SeededRandom.Combine(ResolvedSeed, _totalSpawnedCount);
            _chunkSeeds.Add(chunkSeed);

            // Use a prefab-selection random derived from this chunk's seed
            var prefabRng = new SeededRandom(SeededRandom.Combine(chunkSeed, "prefab"));
            Chunk prefab = SelectNextPrefab(prefabRng);
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

            int index = _totalSpawnedCount;
            long spawnStart = System.Diagnostics.Stopwatch.GetTimestamp();
            Chunk newChunk = Instantiate(prefab, spawnPos, spawnRot, transform);
            double spawnMs = (System.Diagnostics.Stopwatch.GetTimestamp() - spawnStart) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            // Biome (and snow layout) must be set before Initialize so camps/events know which side is sea / precipice
            ApplyBiomePlan(newChunk, index);
            if (newChunk.IsMountain) SetupMountainChunk(newChunk, index, chunkSeed);
            newChunk.name = $"Chunk_{index}_{newChunk.biomeType}_Building";
            newChunk.BeginBuild();

            _activeChunks.Add(newChunk);
            _totalSpawnedCount++;

            IEnumerator steps = BuildChunkSteps(newChunk, index, chunkSeed);
            if (immediate || !Application.isPlaying)
            {
                // Builds queued earlier come first, so neighbors are always finished in order
                CompleteBuildsUpTo(_activeChunks.Count > 1 ? _activeChunks[_activeChunks.Count - 2] : null);
                RunToEnd(steps);
            }
            else
            {
                _buildQueue.Enqueue(new PendingBuild { chunk = newChunk, steps = steps, index = index, spawnMs = spawnMs });
            }

            UpdateBackdropProxies();
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

        [Tooltip("Random curve generation (bend count, positions, lengths, offsets)")]
        public RoadCurveSettings curveSettings = new RoadCurveSettings();

        [Header("Mountain (Dağ)")]
        [Tooltip("Bir dağ bölümünün karlı olma ihtimali (kalanı alpin: çim + kaya, kar sadece zirvelerde)")]
        [Range(0f, 1f)]
        public float snowyMountainChance = 0.6f;
        [Tooltip("İki dağ chunk'ının birleştiği noktada yolun ortada değil kenarda (bir dağın yamacında) olma ihtimali. " +
                 "Kenarda giden yolun diğer yanı uçurum + kanyon + karşı dağ olur.")]
        [Range(0f, 1f)]
        public float mountainLedgeChance = 0.7f;
        [Tooltip("Kenardan giden yolun chunk merkezine uzaklığı")]
        public float ledgeRoadMinOffset = 80f;
        public float ledgeRoadMaxOffset = 100f;
        [Tooltip("Dağ bölümünün ortasında yolun ulaştığı yükseklik (ormanda yol 20m'de)")]
        public float mountainRoadMinHeight = 100f;
        public float mountainRoadMaxHeight = 140f;
        [Tooltip("Yolun bir chunk (500m) boyunca en fazla tırmanıp inebileceği yükseklik. Dağ bölümü başında 20m'den bu hızla tırmanır.")]
        public float mountainRoadClimbPerChunk = 40f;
        [Tooltip("Yol ortadan giden dağ chunk'ında yolun dağ yamacından (aşağısı yumuşak vadi) gitme ihtimali. Kalanında iki dağ arasındaki geçitten gider.")]
        [Range(0f, 1f)]
        public float mountainHillsideChance = 0.4f;

        [Header("Field (Düzlük) Eğimleri")]
        [Tooltip("Düzlük chunk'ının eğimli (yumuşak tepe/çukur/dalgalı) olma ihtimali. Kalanı tamamen düz.")]
        [Range(0f, 1f)]
        public float fieldElevationChance = 0.2f;
        [Tooltip("Düzlükteki tepe yüksekliği aralığı (m). Ormandakinden alçak.")]
        public float fieldMinHillHeight = 4f;
        public float fieldMaxHillHeight = 9f;
        [Tooltip("Düzlükteki çukur derinliği aralığı (m)")]
        public float fieldMinDipDepth = 3f;
        public float fieldMaxDipDepth = 7f;

        [Header("Vegetation & Environment Settings (Bitki Örtüsü Ayarları)")]
        [Tooltip("Global vegetation density multiplier applied to spawned chunks (1.0 = normal, 1.5 = denser, 0.5 = sparser)")]
        [Range(0.2f, 3.0f)]
        public float globalVegetationDensity = 1.0f;

        [Tooltip("Kapalıysa her chunk prefab'ının kendi ChunkVegetationSpawner.densityMultiplier değeri kullanılır. Açıksa global değer prefab değerini ezer.")]
        public bool overrideChunkVegetationDensity = false;

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

        private void ApplyProceduralElevation(Chunk chunk, RoadGenerator roadGen, RoadSpline spline)
        {
            SeededRandom rng = chunk.RoadRandom;

            ChunkElevationConfig config = (chunk != null && chunk.overrideElevationSettings)
                ? chunk.elevationConfig
                : globalElevationConfig;

            if (config == null) config = new ChunkElevationConfig();

            // First N chunks are kept flat (no hills/dips) as runway, but may still have gentle curves
            bool isRunway = chunk.ChunkIndex < initialFlatChunks;
            // Field (open plains): mostly flat; with a low chance gentle hills/dips/rolling only, at lower amplitudes
            bool isField = chunk.IsField;
            bool forceFlat = isRunway || !enableElevationVariation;
            bool rollSuccess = !forceFlat && (rng.Value <= (isField ? fieldElevationChance : config.elevationChance));

            float minHill = isField ? fieldMinHillHeight : config.minHillHeight;
            float maxHill = isField ? fieldMaxHillHeight : config.maxHillHeight;
            float minDip = isField ? fieldMinDipDepth : config.minDipDepth;
            float maxDip = isField ? fieldMaxDipDepth : config.maxDipDepth;

            RoadElevationType elevationType = RoadElevationType.Flat;

            if (rollSuccess)
            {
                int w0 = config.hillWeight;
                int w1 = w0 + config.valleyWeight;
                int w2 = w1 + config.rollingHillsWeight;
                int w3 = w2 + (isField ? 0 : config.mountainPassWeight);
                int w4 = w3 + (isField ? 0 : config.elevatedChicaneWeight);
                int totalWeight = Mathf.Max(1, w4 + (isField ? 0 : config.flatWeight));
                int roll = rng.Range(0, totalWeight);

                if (roll < w0) elevationType = RoadElevationType.HillCrest;
                else if (roll < w1) elevationType = RoadElevationType.ValleyDip;
                else if (roll < w2) elevationType = RoadElevationType.RollingHills;
                else if (roll < w3) elevationType = RoadElevationType.MountainPass;
                else if (roll < w4) elevationType = RoadElevationType.ElevatedChicane;
            }

            float hill = 0f, dip = 0f;
            bool forceCurve = false, sharp = false;
            RoadCrossSectionPreset cross = RoadCrossSectionPreset.SidewalkOnly;

            switch (elevationType)
            {
                case RoadElevationType.HillCrest:
                    hill = rng.Range(minHill, maxHill);
                    cross = RoadCrossSectionPreset.GuardrailOnly;
                    break;
                case RoadElevationType.ValleyDip:
                    dip = rng.Range(minDip, maxDip);
                    cross = RoadCrossSectionPreset.OpenRoad;
                    break;
                case RoadElevationType.RollingHills:
                    hill = rng.Range(minHill, maxHill);
                    dip = rng.Range(minDip, maxDip);
                    cross = RoadCrossSectionPreset.GuardrailOnly;
                    break;
                case RoadElevationType.MountainPass:
                    hill = rng.Range(config.minHillHeight, config.maxHillHeight);
                    forceCurve = true;
                    cross = RoadCrossSectionPreset.FullHighway;
                    break;
                case RoadElevationType.ElevatedChicane:
                    hill = rng.Range(config.minHillHeight * 0.7f, config.maxHillHeight * 0.7f);
                    forceCurve = sharp = true;
                    cross = RoadCrossSectionPreset.HazardFortified;
                    break;
            }

            // Curve layout (bend count, positions, lengths, offsets) is fully random
            // Fields: fewer and never sharp curves
            bool curvy = forceCurve || rng.Chance(isField ? curveSettings.curveChance * 0.6f : curveSettings.curveChance);
            // Mountain roads always wind (and the mountain meshes follow the road)
            if (chunk.IsMountain) curvy = true;
            if (curvy && !forceCurve)
            {
                // No hazard chicanes on the runway chunks
                sharp = !isRunway && !isField && rng.Chance(0.15f);
                if (elevationType == RoadElevationType.Flat)
                    cross = sharp ? RoadCrossSectionPreset.HazardFortified : RoadCrossSectionPreset.FullHighway;
            }
            // Country road through the fields: no sidewalks or guardrails
            if (isField) cross = RoadCrossSectionPreset.OpenRoad;

            // Snow: guardrails toward the drop on mountainside and cliff roads (layout rolled in RollSnowLayout)
            if (chunk.IsMountain && chunk.mountainLayout != MountainLayout.Valley) cross = RoadCrossSectionPreset.GuardrailOnly;

            float peak = spline.SetProceduralPreset(curveSettings, curvy, sharp, elevationType, hill, dip, rng,
                chunk.roadEntryX, chunk.roadExitX, chunk.roadEntryY, chunk.roadExitY);

            chunk.roadType = !curvy ? ChunkRoadType.Straight
                : sharp ? ChunkRoadType.HazardZone
                : peak > 0f ? ChunkRoadType.CurvedRight : ChunkRoadType.CurvedLeft;
            chunk.crossSectionType = cross;
            chunk.currentElevationType = elevationType;
            chunk.currentElevationParam = hill > 0f ? hill : dip;

            roadGen.ApplyCrossSectionPreset(cross);
        }

        private void ApplySequentialElevationPreset(Chunk chunk, RoadGenerator roadGen, RoadSpline spline)
        {
            RoadVariationDefinition variation = RoadVariations[chunk.ChunkIndex % RoadVariations.Length];
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
        /// Sets biome + coast data on a freshly instantiated chunk from the deterministic biome plan.
        /// </summary>
        private void ApplyBiomePlan(Chunk chunk, int index)
        {
            if (!enableBiomeVariation || biomeRuns == null || biomeRuns.Count == 0) return;

            BiomePlanEntry entry = GetBiomePlan(index);
            chunk.biomeType = entry.biome;
            chunk.seaSide = entry.seaSide;
            chunk.hasNeighborInfo = true;
            chunk.prevBiome = index > 0 ? GetBiomePlan(index - 1).biome : entry.biome;
            chunk.nextBiome = GetBiomePlan(index + 1).biome;
            chunk.prevMountainVariant = index > 0 ? GetBiomePlan(index - 1).variant : entry.variant;
            chunk.nextMountainVariant = GetBiomePlan(index + 1).variant;

            if (entry.biome == ChunkBiomeType.Coast)
            {
                // A coast continues into a neighbor only if that neighbor is coast on the same side
                chunk.coastContinuesAtStart = index > 0 && IsSameCoast(GetBiomePlan(index - 1), entry);
                chunk.coastContinuesAtEnd = IsSameCoast(GetBiomePlan(index + 1), entry);
            }
        }

        /// <summary>
        /// Mountain sub-type, road entry/exit offsets and layout, before Initialize (camps/events need to know
        /// the precipice side). A road that is off-center at either end runs as a ledge along the mountain on
        /// that side; a centered one goes through a pass (valley) or along a gentle mountainside (hillside).
        /// </summary>
        private void SetupMountainChunk(Chunk chunk, int index, int chunkSeed)
        {
            chunk.mountainVariant = GetBiomePlan(index).variant;
            chunk.roadEntryX = SeamRoadOffset(index);
            chunk.roadExitX = SeamRoadOffset(index + 1);
            chunk.roadEntryY = SeamRoadHeight(index, chunk.baseElevation);
            chunk.roadExitY = SeamRoadHeight(index + 1, chunk.baseElevation);

            var rng = new SeededRandom(SeededRandom.Combine(chunkSeed, "mountain_layout"));
            float entry = chunk.roadEntryX, exit = chunk.roadExitX;
            bool offEntry = Mathf.Abs(entry) > 1f, offExit = Mathf.Abs(exit) > 1f;

            if ((offEntry || offExit) && entry * exit >= 0f)
            {
                chunk.mountainLayout = MountainLayout.Ledge;
                chunk.uphillOnRight = (offEntry ? entry : exit) > 0f;
            }
            else
            {
                // Centered road (or crossing from one side to the other): pass or gentle mountainside
                chunk.mountainLayout = rng.Chance(mountainHillsideChance) ? MountainLayout.Hillside : MountainLayout.Valley;
                chunk.uphillOnRight = rng.Chance(0.5f);
            }
        }

        /// <summary>
        /// The prefab's triggers and back blockade are authored for a centered road; when the road runs
        /// off-center (mountain ledges) they are moved sideways onto the road at their Z.
        /// </summary>
        private static void AlignRoadObjects(Chunk chunk, RoadSpline spline)
        {
            bool centered = Mathf.Abs(chunk.roadEntryX) < 0.01f && Mathf.Abs(chunk.roadExitX) < 0.01f;
            bool baseHeight = Mathf.Abs(chunk.roadEntryY - chunk.baseElevation) < 0.01f && Mathf.Abs(chunk.roadExitY - chunk.baseElevation) < 0.01f;
            if (centered && baseHeight) return;

            var triggers = chunk.GetComponentsInChildren<ChunkTrigger>(true);
            for (int i = 0; i < triggers.Length; i++) MoveOntoRoad(chunk, spline, triggers[i].transform);
            if (chunk.backBlockade != null) MoveOntoRoad(chunk, spline, chunk.backBlockade.transform);
        }

        private static void MoveOntoRoad(Chunk chunk, RoadSpline spline, Transform target)
        {
            Vector3 local = chunk.transform.InverseTransformPoint(target.position);
            // Spline points are chunk-local (the spline sits at the chunk origin, as RoadTerrainAdapter assumes)
            Vector3 road = spline.GetPointAtZ(Mathf.Clamp(local.z, 0f, chunk.chunkLength));
            local.x = road.x;
            // Authored relative to a road at baseElevation; keep the same height above the actual road
            local.y = road.y + (local.y - chunk.baseElevation);
            target.position = chunk.transform.TransformPoint(local);
        }

        /// <summary>
        /// Road height at seam i, shared by both chunks. Outside mountains (and at a mountain run's ends) it is
        /// the baseline (20m). Inside a run the road climbs at most mountainRoadClimbPerChunk per chunk toward a
        /// seeded mountainRoadMinHeight-mountainRoadMaxHeight, and descends again before the run ends.
        /// </summary>
        public float SeamRoadHeight(int seam, float baseline)
        {
            if (seam <= 0 || elevationMode != ElevationSelectionMode.DynamicWeightedRandom) return baseline;
            if (!enableBiomeVariation || biomeRuns == null || biomeRuns.Count == 0) return baseline;
            if (GetBiomePlan(seam - 1).biome != ChunkBiomeType.Mountain || GetBiomePlan(seam).biome != ChunkBiomeType.Mountain)
                return baseline;

            // Position of this seam inside its mountain run: chunks before it / after it
            int before = 0;
            for (int i = seam - 1; i >= 0 && before < 32 && GetBiomePlan(i).biome == ChunkBiomeType.Mountain; i--) before++;
            int after = 0;
            for (int i = seam; after < 32 && GetBiomePlan(i).biome == ChunkBiomeType.Mountain; i++) after++;

            int worldSeed = ResolvedSeed != 0 ? ResolvedSeed : SeededRandom.HashString(masterSeed);
            var rng = new SeededRandom(SeededRandom.Combine(SeededRandom.Combine(worldSeed, "mtn_road_height"), seam));
            float target = rng.Range(mountainRoadMinHeight, mountainRoadMaxHeight);

            // Climb from the valley at the run's start, descend toward its end, at most mountainRoadClimbPerChunk per chunk
            float climb = mountainRoadClimbPerChunk;
            return Mathf.Min(target, baseline + climb * before, baseline + climb * after);
        }

        /// <summary>
        /// Lateral road position at seam i (between chunk i-1 and chunk i), shared by both chunks.
        /// Only seams between two mountain chunks can be off-center; the side follows the mountain run's side,
        /// so consecutive off-center seams form a long ledge along one mountain.
        /// </summary>
        public float SeamRoadOffset(int seam)
        {
            if (seam <= 0 || elevationMode != ElevationSelectionMode.DynamicWeightedRandom) return 0f;
            if (!enableBiomeVariation || biomeRuns == null || biomeRuns.Count == 0) return 0f;

            BiomePlanEntry before = GetBiomePlan(seam - 1), after = GetBiomePlan(seam);
            if (before.biome != ChunkBiomeType.Mountain || after.biome != ChunkBiomeType.Mountain) return 0f;

            int worldSeed = ResolvedSeed != 0 ? ResolvedSeed : SeededRandom.HashString(masterSeed);
            var rng = new SeededRandom(SeededRandom.Combine(SeededRandom.Combine(worldSeed, "mtn_road_offset"), seam));
            if (!rng.Chance(mountainLedgeChance)) return 0f;

            float side = after.seaSide == CoastSide.Right ? 1f : -1f;
            return side * rng.Range(ledgeRoadMinOffset, ledgeRoadMaxOffset);
        }

        private static bool IsSameCoast(BiomePlanEntry a, BiomePlanEntry b)
        {
            return a.biome == ChunkBiomeType.Coast && b.biome == ChunkBiomeType.Coast && a.seaSide == b.seaSide;
        }

        /// <summary>
        /// Biome for any chunk index. Generated sequentially in runs from the master seed and cached,
        /// so neighbors can be queried before they are spawned and the same seed always gives the same sequence.
        /// </summary>
        private BiomePlanEntry GetBiomePlan(int index)
        {
            if (_biomeRng == null)
            {
                // Edit mode: Awake has not resolved the seed yet, fall back to the seed text
                int seed = ResolvedSeed != 0 ? ResolvedSeed : SeededRandom.HashString(masterSeed);
                _biomeRng = new SeededRandom(SeededRandom.Combine(seed, "biome_runs"));
                _biomePlan.Clear();
                _lastRunBiome = ChunkBiomeType.Forest;
            }

            while (_biomePlan.Count <= index)
            {
                if (_biomePlan.Count < initialForestChunks)
                {
                    _biomePlan.Add(new BiomePlanEntry { biome = ChunkBiomeType.Forest });
                    _lastRunBiome = ChunkBiomeType.Forest;
                    continue;
                }

                // Never repeat the same biome as two consecutive runs (when there is an alternative)
                var candidates = new List<BiomeRunSettings>();
                for (int i = 0; i < biomeRuns.Count; i++)
                {
                    var run = biomeRuns[i];
                    if (run != null && run.weight > 0 && run.biome != _lastRunBiome) candidates.Add(run);
                }
                if (candidates.Count == 0)
                {
                    for (int i = 0; i < biomeRuns.Count; i++)
                        if (biomeRuns[i] != null && biomeRuns[i].weight > 0) candidates.Add(biomeRuns[i]);
                }
                if (candidates.Count == 0)
                {
                    _biomePlan.Add(new BiomePlanEntry { biome = ChunkBiomeType.Forest });
                    continue;
                }

                BiomeRunSettings chosen = _biomeRng.WeightedPick(candidates, r => r.weight);
                int length = _biomeRng.Range(chosen.minLength, Mathf.Max(chosen.minLength, chosen.maxLength) + 1);
                CoastSide side = _biomeRng.Chance(0.5f) ? CoastSide.Left : CoastSide.Right;
                // Mountain runs: one sub-type (snowy / alpine) for the whole run; `side` is the mountain the road follows
                MountainVariant variant = chosen.biome == ChunkBiomeType.Mountain && !_biomeRng.Chance(snowyMountainChance)
                    ? MountainVariant.Alpine
                    : MountainVariant.Snowy;

                for (int i = 0; i < length; i++)
                {
                    _biomePlan.Add(new BiomePlanEntry { biome = chosen.biome, seaSide = side, variant = variant });
                }
                _lastRunBiome = chosen.biome;
            }

            return _biomePlan[index];
        }

        /// <summary>Planned biome of a chunk index (spawned or not).</summary>
        public ChunkBiomeType GetPlannedBiome(int index)
        {
            return index >= 0 ? GetBiomePlan(index).biome : ChunkBiomeType.Forest;
        }

        /// <summary>
        /// Selects the next chunk prefab from the pool, ensuring consecutive chunks have different road types.
        /// </summary>
        private Chunk SelectNextPrefab(SeededRandom rng)
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

            int roll = rng.Range(0, totalWeight);
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
            // Same rule as the per-frame check, so the trigger never spawns earlier than intended
            EnsureChunksAhead(chunk);
        }

        /// <summary>
        /// Spawns chunks until there are `chunksAhead` chunks after the given chunk.
        /// Start: chunk 1 + 2 exist. Entering chunk 2 (end of chunk 1) spawns chunk 3, entering chunk 3 spawns chunk 4...
        /// </summary>
        private void EnsureChunksAhead(Chunk current)
        {
            int index = _activeChunks.IndexOf(current);
            if (index < 0) return;

            int ahead = _activeChunks.Count - 1 - index;
            for (int i = ahead; i < chunksAhead; i++)
            {
                if (SpawnNextChunk() == null) break;
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

            // Enforce maxActiveChunks limit, but never unload the chunk the player is in (or anything ahead of it)
            while (_activeChunks.Count > maxActiveChunks && _activeChunks[0] != currentChunk)
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

        /// <summary>Returns the active chunk whose 0..chunkLength forward span contains the player/vehicle.</summary>
        private Chunk GetChunkContainingTarget()
        {
            Transform target = GetTargetTransform();
            if (target == null) return null;

            for (int i = _activeChunks.Count - 1; i >= 0; i--)
            {
                Chunk chunk = _activeChunks[i];
                if (chunk == null) continue;
                float localZ = chunk.transform.InverseTransformPoint(target.position).z;
                if (localZ >= 0f && localZ < chunk.chunkLength) return chunk;
            }
            return null;
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

#if UNITY_EDITOR
        [ContextMenu("Spawn Editor Preview Chunks")]
        public void SpawnEditorPreviewChunks(int count = 2)
        {
            ClearChildObjects();
            _activeChunks.Clear();
            _chunkSeeds.Clear();
            _totalSpawnedCount = 0;
            _biomeRng = null; // re-plan biomes from the (possibly changed) seed

            for (int i = 0; i < count; i++)
            {
                SpawnNextChunk();
            }

            foreach (var chunk in _activeChunks)
            {
                if (chunk != null)
                {
                    chunk.GenerateEditorPreview(chunk.ChunkSeed);
                }
            }
        }
#endif

        /// <summary>
        /// Generates a random alphanumeric seed text using System.Random (non-deterministic, for first-run only).
        /// </summary>
        private static string GenerateRandomSeedText(int length)
        {
            const string chars = "abcdefghijklmnopqrstuvwxyz0123456789";
            var sysRng = new System.Random();
            var sb = new StringBuilder(length);
            for (int i = 0; i < length; i++)
                sb.Append(chars[sysRng.Next(chars.Length)]);
            return sb.ToString();
        }

        /// <summary>Returns the seed used for a given chunk index, or 0 if not yet generated.</summary>
        public int GetChunkSeed(int chunkIndex)
        {
            if (chunkIndex >= 0 && chunkIndex < _chunkSeeds.Count)
                return _chunkSeeds[chunkIndex];
            return 0;
        }

        /// <summary>Total number of chunks spawned so far.</summary>
        public int TotalSpawnedCount => _totalSpawnedCount;
    }
}
