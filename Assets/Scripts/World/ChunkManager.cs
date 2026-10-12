using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using EndlessSurvival.World.Road;

namespace EndlessSurvival.World
{
    /// <summary>
    /// Values evaluated at chunk seams by BOTH neighbors, so they must be world-wide (not per chunk type),
    /// otherwise two different types would disagree at their shared edge.
    /// </summary>
    [System.Serializable]
    public class WorldSeamSettings
    {
        [Header("Kenar Uçurumları (seam)")]
        [Tooltip("Z=0 ve Z=500'de uçurumun başladığı sabit mesafe (chunk merkez çizgisinden)")]
        public float seamInnerEdge = 175f;
        public float seamRampWidth = 28f;
        [Range(0f, 1f)]
        [Tooltip("Bir birleşim noktasında uçurum olma ihtimali (0 ise orada kayalar boşluğu doldurur)")]
        public float seamCliffChance = 0.6f;
        public float seamMinHeight = 30f;
        public float seamMaxHeight = 55f;

        [Header("Arka Plan Dağ Şeridi")]
        [Tooltip("Chunk kenarından (X=±250) dışarıya doğru şeridin genişliği")]
        public float backdropStripDepth = 2600f;
        [Tooltip("Farklı tipli komşu chunk ile arka planın harmanlandığı mesafe")]
        public float backdropBlendDistance = 250f;
        [Range(10, 80)] public int backdropRowsAlongRoad = 40;
        [Range(8, 48)] public int backdropColumnsOutward = 32;

        public static readonly WorldSeamSettings Default = new WorldSeamSettings();

        /// <summary>The settings of the chunk's manager; defaults for a lone chunk previewed in the editor.</summary>
        public static WorldSeamSettings For(Chunk chunk)
        {
            if (chunk != null && chunk.Manager != null && chunk.Manager.seamSettings != null) return chunk.Manager.seamSettings;
            if (ChunkManager.Instance != null && ChunkManager.Instance.seamSettings != null) return ChunkManager.Instance.seamSettings;
            return Default;
        }
    }

    /// <summary>
    /// Endless chunk streaming. Holds only world-wide settings (seed, streaming, build budget, seams) and the weighted
    /// list of chunk TYPE prefabs. Picks types in runs from the seed, aligns each chunk's entry socket to the previous
    /// chunk's exit and builds it; every type-specific setting lives on the type prefab itself.
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

        /// <summary>One chunk type in the world: its prefab and how often / how long it appears.</summary>
        [System.Serializable]
        public class ChunkTypeEntry
        {
            [Tooltip("Chunk tipi prefab'ı (Chunk_Forest, Chunk_Mountain, kendi tasarladığın tip...)")]
            public Chunk prefab;
            [Tooltip("Bu tipin bir sonraki bölüm olarak seçilme ağırlığı (0 = hiç gelmez)")]
            [Range(0, 100)] public int weight = 50;
            [Tooltip("Bölümün kaç chunk süreceği (min-max). Dağ tiplerinde yol tırmanıp inebilsin diye en az 4 önerilir.")]
            [Range(1, 20)] public int minLength = 2;
            [Range(1, 20)] public int maxLength = 4;
        }

        [Header("Chunk Tipleri")]
        [Tooltip("Dünyada kullanılacak chunk tipleri. Tip seçilir, o tipten min-max uzunlukta bir bölüm gelir; aynı tip art arda iki bölüm olmaz. " +
                 "Yeni bir tip eklemek için prefab'ını bu listeye eklemen yeterli.")]
        public List<ChunkTypeEntry> chunkTypes = new List<ChunkTypeEntry>();

        [Tooltip("Oyunun başladığı tip (boşsa listedeki ilk tip)")]
        public Chunk startChunkType;

        [Tooltip("Başlangıçta garanti başlangıç tipi chunk sayısı (en az o tipin listedeki Min Length'i kadar olur)")]
        [Range(1, 10), UnityEngine.Serialization.FormerlySerializedAs("initialForestChunks")]
        public int startChunkCount = 2;

        [Header("Dünya Geneli Seam Ayarları")]
        [Tooltip("Farklı tiplerin birleştiği yerlerde iki tarafın da aynı değeri kullanması gereken ayarlar")]
        public WorldSeamSettings seamSettings = new WorldSeamSettings();

        // ---------------------------------------------------------------
        // Legacy single-prefab setup (Chunk_Forest_Curve + biome enum). Hidden and unused at runtime;
        // only read once by "Endless Survival/Chunk Tiplerini Oluştur" (ChunkTypeSetupUtility) to migrate.
        // ---------------------------------------------------------------
        [HideInInspector, UnityEngine.Serialization.FormerlySerializedAs("chunkPrefab")] public Chunk legacyChunkPrefab;
        [HideInInspector, UnityEngine.Serialization.FormerlySerializedAs("chunkPrefabs")] public List<Chunk> legacyChunkPrefabs = new List<Chunk>();
        [HideInInspector, UnityEngine.Serialization.FormerlySerializedAs("biomeRuns")] public List<BiomeRunSettings> legacyBiomeRuns = new List<BiomeRunSettings>();
        [HideInInspector, UnityEngine.Serialization.FormerlySerializedAs("globalElevationConfig")] public ChunkElevationConfig legacyElevationConfig = new ChunkElevationConfig();
        [HideInInspector, UnityEngine.Serialization.FormerlySerializedAs("curveSettings")] public RoadCurveSettings legacyCurveSettings = new RoadCurveSettings();
        [HideInInspector, UnityEngine.Serialization.FormerlySerializedAs("snowyMountainChance")] public float legacySnowyMountainChance = 0.6f;
        [HideInInspector, UnityEngine.Serialization.FormerlySerializedAs("mountainLedgeChance")] public float legacyMountainLedgeChance = 0.7f;
        [HideInInspector, UnityEngine.Serialization.FormerlySerializedAs("ledgeRoadMinOffset")] public float legacyLedgeRoadMinOffset = 80f;
        [HideInInspector, UnityEngine.Serialization.FormerlySerializedAs("ledgeRoadMaxOffset")] public float legacyLedgeRoadMaxOffset = 100f;
        [HideInInspector, UnityEngine.Serialization.FormerlySerializedAs("mountainRoadMinHeight")] public float legacyMountainRoadMinHeight = 100f;
        [HideInInspector, UnityEngine.Serialization.FormerlySerializedAs("mountainRoadMaxHeight")] public float legacyMountainRoadMaxHeight = 140f;
        [HideInInspector, UnityEngine.Serialization.FormerlySerializedAs("mountainRoadClimbPerChunk")] public float legacyMountainRoadClimbPerChunk = 40f;
        [HideInInspector, UnityEngine.Serialization.FormerlySerializedAs("mountainHillsideChance")] public float legacyMountainHillsideChance = 0.4f;
        [HideInInspector, UnityEngine.Serialization.FormerlySerializedAs("fieldElevationChance")] public float legacyFieldElevationChance = 0.2f;
        [HideInInspector, UnityEngine.Serialization.FormerlySerializedAs("fieldMinHillHeight")] public float legacyFieldMinHillHeight = 4f;
        [HideInInspector, UnityEngine.Serialization.FormerlySerializedAs("fieldMaxHillHeight")] public float legacyFieldMaxHillHeight = 9f;
        [HideInInspector, UnityEngine.Serialization.FormerlySerializedAs("fieldMinDipDepth")] public float legacyFieldMinDipDepth = 3f;
        [HideInInspector, UnityEngine.Serialization.FormerlySerializedAs("fieldMaxDipDepth")] public float legacyFieldMaxDipDepth = 7f;

        [System.Serializable]
        public class BiomeRunSettings
        {
            public ChunkBiomeType biome = ChunkBiomeType.Forest;
            [Range(0, 100)] public int weight = 50;
            [Range(1, 20)] public int minLength = 2;
            [Range(1, 20)] public int maxLength = 4;
        }

        // Scenes saved with older defaults are upgraded once (see EnsureSettingsDefaults)
        [SerializeField, HideInInspector, UnityEngine.Serialization.FormerlySerializedAs("_biomeRunsVersion")]
        private int _settingsVersion;
        private const int SettingsVersion = 5;

        private void OnValidate()
        {
            EnsureSettingsDefaults();
        }

        private void EnsureSettingsDefaults()
        {
            if (_settingsVersion >= SettingsVersion) return;

            // v2: time-sliced builds -> keep two chunks ready ahead (new chunks appear ~1km away)
            if (_settingsVersion < 2)
            {
                chunksAhead = Mathf.Max(chunksAhead, 2);
                maxActiveChunks = Mathf.Max(maxActiveChunks, maxChunksBehind + 1 + chunksAhead);
            }

            _settingsVersion = SettingsVersion;
        }

        [Tooltip("Sonsuz deniz yüzeyi materyali (boşsa otomatik üretilir)")]
        public Material oceanMaterial;

        /// <summary>Planned type of one chunk index, plus the per-run rolls (sea side / mountain side, mountain sub-type).</summary>
        private struct TypePlanEntry
        {
            public Chunk type;
            public CoastSide seaSide;           // coast: sea side; mountain: side of the mountain a ledge road follows
            public MountainVariant variant;     // mountain sub-type, same for the whole run
            public FoothillLowland lowland;     // foothills: the land opposite the mountain, same for the whole run
            public ChunkBiomeType Biome => type != null ? type.biomeType : ChunkBiomeType.Forest;
            public bool IsFoothill => type != null && type.biomeType == ChunkBiomeType.Mountain && type.mountainSettings != null && type.mountainSettings.foothills;
            public bool FoothillSea => IsFoothill && (lowland == FoothillLowland.LandThenCoast || lowland == FoothillLowland.Coast);
        }

        private readonly List<TypePlanEntry> _typePlan = new List<TypePlanEntry>();
        private SeededRandom _typeRng;
        private Chunk _lastRunType;
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
                string type = chunk != null ? chunk.TypeName : "?";
                Debug.Log($"[ChunkBuild] Chunk {index} ({type}): instantiate {spawnMs:0.0}ms, adımlar toplam {_total:0.0}ms, " +
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
            if (HasChunkTypes)
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
                Debug.LogError("[ChunkManager] Chunk Types listesi boş. Menüden 'Endless Survival/Chunk Tiplerini Oluştur' çalıştır " +
                               "veya chunk tipi prefab'larını listeye ekle.");
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

            // Procedural types get the standard generators (added if the prefab lacks them) and a seeded road + terrain.
            // Handcrafted types keep their authored road and terrain; only the generators on their prefab run.
            bool procedural = chunk.generationMode == ChunkGenerationMode.Procedural;

            if (!procedural || (roadGen != null && spline != null))
            {
                // Keep the half-built terrain hidden; the backdrop proxy stands in for it meanwhile
                if (terrain != null && Application.isPlaying)
                {
                    terrain.drawHeightmap = false;
                    terrain.drawTreesAndFoliage = false;
                }

                // 2) Road shape and cross section
                if (procedural)
                {
                    if (elevationMode == ElevationSelectionMode.DynamicWeightedRandom)
                        ApplyProceduralElevation(chunk, roadGen, spline);
                    else
                        ApplySequentialElevationPreset(chunk, roadGen, spline);
                    AlignRoadObjects(chunk, spline);
                    yield return "road";
                }

                // Side boundaries (cliffs / giant rocks) so the world edge is never visible
                var boundary = chunk.GetComponent<ChunkBoundaryGenerator>();
                if (boundary == null && procedural) boundary = chunk.gameObject.AddComponent<ChunkBoundaryGenerator>();

                // Field chunks: fences / tree rows / corn / streams instead of cliffs (must exist before terrain conform)
                var fieldEdges = chunk.GetComponent<FieldEdgeGenerator>();
                if (fieldEdges == null && chunk.UsesFieldEdges && procedural) fieldEdges = chunk.gameObject.AddComponent<FieldEdgeGenerator>();

                // 3) Terrain under and around the road (also cliffs, coast, field hills, river channel)
                // POIs then drop to the natural ground at their spot and the ground under them is leveled there
                chunk.CapturePoiGrounding();
                if (terrain != null && procedural)
                {
                    RoadTerrainAdapter.ConformTerrainToRoad(terrain, spline, roadGen, true, paintLayers: false);
                    chunk.GroundPointsOfInterest();
                    yield return "terrain";

                    // 4) Sand / soil painting
                    RoadTerrainAdapter.PaintTerrainLayers(terrain);
                    yield return "paint";
                }
                else
                {
                    chunk.GroundPointsOfInterest();
                }

                // 5) Boundary rocks
                if (boundary != null)
                {
                    boundary.SpawnBoundaryRocks(chunk.transform, terrain);
                    yield return "rocks";
                }

                // 6) Background mountains outside the boundary (seamless across chunks via global Z noise)
                var backdrop = chunk.GetComponent<ChunkBackdropGenerator>();
                if (backdrop == null && procedural) backdrop = chunk.gameObject.AddComponent<ChunkBackdropGenerator>();
                if (backdrop != null)
                {
                    backdrop.Generate(chunk.transform, terrain);
                    yield return "backdrop";
                }

                // 7) Field edges: one side per frame, then river/bridge
                if (fieldEdges != null)
                {
                    IEnumerator fieldSteps = fieldEdges.GenerateSteps(chunk.transform, terrain);
                    while (fieldSteps.MoveNext()) yield return "fieldEdges";
                }

                // 8) Abandoned vehicle wrecks on and beside the road (before vegetation: trees keep clear of them)
                var wrecks = chunk.GetComponent<AbandonedVehicleSpawner>();
                if (wrecks == null && procedural) wrecks = chunk.gameObject.AddComponent<AbandonedVehicleSpawner>();
                if (wrecks != null)
                {
                    wrecks.Generate(chunk.transform, terrain, chunk.WrecksRandom);
                    yield return "wrecks";
                }

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

            chunk.name = $"Chunk_{index}_{chunk.TypeName}_{chunk.roadType}_{chunk.crossSectionType}";
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
                // Only builds the meshes; the look of each silhouette comes from its planned type's backdrop settings
                _proxyRoot = new GameObject("BackdropProxies").transform;
                _proxyRoot.SetParent(transform, false);
                _proxyBackdrop = _proxyRoot.gameObject.AddComponent<ChunkBackdropGenerator>();
            }

            int lastIndex = _totalSpawnedCount - 1;
            // Same value as Chunk.WorldSeed (the last chunk may not be initialized yet while its build is queued)
            int worldSeed = ResolvedSeed != 0 ? ResolvedSeed : SeededRandom.HashString(masterSeed);

            for (int i = _totalSpawnedCount; i < _totalSpawnedCount + backdropProxyChunks; i++)
            {
                if (_backdropProxies.ContainsKey(i)) continue;

                TypePlanEntry entry = GetTypePlan(i);
                TypePlanEntry prevEntry = GetTypePlan(i - 1);
                TypePlanEntry nextEntry = GetTypePlan(i + 1);
                if (entry.type == null) continue;

                var typeBackdrop = entry.type.GetComponent<ChunkBackdropGenerator>();
                if (typeBackdrop == null || !typeBackdrop.backdropEnabled) continue;

                // Different type next door: blend toward its look at the seam (same as the real chunk will)
                var ownSettings = typeBackdrop.ResolveSettings(entry.Biome, entry.variant);
                var prevSettings = prevEntry.type != entry.type ? NeighborBackdrop(prevEntry) : null;
                var nextSettings = nextEntry.type != entry.type ? NeighborBackdrop(nextEntry) : null;
                var typeBoundary = entry.type.GetComponent<ChunkBoundaryGenerator>();
                float coastTransition = typeBoundary != null ? typeBoundary.coastTransitionLength : 200f;

                Vector3 position = last.transform.position + last.transform.forward * (last.chunkLength * (i - lastIndex));

                // Foothills: the run's side is the mountain; lowland (and sea, if any) on the other side
                float runSign = entry.seaSide == CoastSide.Right ? 1f : -1f;
                float lowlandSign = entry.IsFoothill ? -runSign : 0f;
                bool hasSea = entry.Biome == ChunkBiomeType.Coast || entry.FoothillSea;
                float seaSign = entry.IsFoothill ? lowlandSign : runSign;

                _backdropProxies[i] = _proxyBackdrop.GenerateProxy(_proxyRoot, position, last.transform.rotation, i,
                    entry.Biome, entry.variant, ownSettings, prevSettings, nextSettings,
                    IsSameMountainRun(prevEntry, entry), IsSameMountainRun(entry, nextEntry), hasSea, seaSign,
                    IsSameCoast(prevEntry, entry), IsSameCoast(nextEntry, entry),
                    worldSeed, seamSettings, last.chunkLength, entry.type.baseElevation, 250f, coastTransition,
                    SeamRoadOffset(i), SeamRoadOffset(i + 1),
                    SeamRoadHeight(i, entry.type.baseElevation), SeamRoadHeight(i + 1, entry.type.baseElevation),
                    lowlandSign, typeBackdrop.foothillLowlandSettings);
            }
        }

        private static void UseEditorTerrainCopy(Chunk chunk)
        {
            Terrain terrain = chunk.GetComponentInChildren<Terrain>();
            if (terrain == null || terrain.terrainData == null) return;
            TerrainData copy = Instantiate(terrain.terrainData);
            copy.hideFlags = HideFlags.DontSave;
            terrain.terrainData = copy;
            var collider = terrain.GetComponent<TerrainCollider>();
            if (collider != null) collider.terrainData = copy;
        }

        /// <summary>A planned neighbor's backdrop look per side [left, right] (foothills differ between their two sides).</summary>
        private static BiomeBackdropSettings[] NeighborBackdrop(TypePlanEntry neighbor)
        {
            bool uphillRight = neighbor.seaSide == CoastSide.Right;
            return new[]
            {
                ChunkBackdropGenerator.SettingsOf(neighbor.type, neighbor.variant, -1f, uphillRight),
                ChunkBackdropGenerator.SettingsOf(neighbor.type, neighbor.variant, 1f, uphillRight)
            };
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

            // The type comes from the seeded type plan (runs of the same type)
            Chunk prefab = GetTypePlan(_totalSpawnedCount).type;
            if (prefab == null)
            {
                _chunkSeeds.RemoveAt(_chunkSeeds.Count - 1);
                Debug.LogError("[ChunkManager] Cannot spawn: Chunk Types listesinde geçerli bir prefab yok.");
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
            // Edit-mode preview chunks of the same type would all reshape their prefab's TerrainData asset (each one
            // overwriting the ground under the previous one's trees/POIs): give each its own unsaved copy
            if (!Application.isPlaying) UseEditorTerrainCopy(newChunk);
            // Type/neighbor info (and mountain layout) must be set before Initialize so camps/events know which side is sea / precipice
            ApplyTypePlan(newChunk, index);
            if (newChunk.IsMountain && newChunk.generationMode == ChunkGenerationMode.Procedural) SetupMountainChunk(newChunk, index, chunkSeed);
            newChunk.name = $"Chunk_{index}_{newChunk.TypeName}_Building";
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

        [Header("Genel Yol Kuralları (eğim/viraj ayarları her tipin kendi prefab'ında)")]
        [Tooltip("Kapalıysa hiçbir chunk'ta tepe/vadi olmaz (test için)")]
        public bool enableElevationVariation = true;

        [Tooltip("DynamicWeightedRandom: her tip kendi ayarlarıyla rastgele yol üretir. SequentialPresetList: test için sabit 9'lu liste.")]
        public ElevationSelectionMode elevationMode = ElevationSelectionMode.DynamicWeightedRandom;

        [Tooltip("Oyun başında tamamen düz (eğimsiz, şikansız) pist olarak üretilecek chunk sayısı")]
        [Range(1, 5)]
        public int initialFlatChunks = 2;

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

        /// <summary>
        /// Rolls this chunk's road from its TYPE's settings (elevation config, curve settings, road style).
        /// </summary>
        private void ApplyProceduralElevation(Chunk chunk, RoadGenerator roadGen, RoadSpline spline)
        {
            SeededRandom rng = chunk.RoadRandom;
            ChunkElevationConfig config = chunk.elevationConfig ?? new ChunkElevationConfig();
            RoadCurveSettings curves = chunk.curveSettings ?? new RoadCurveSettings();
            ChunkRoadStyle style = chunk.roadStyle ?? new ChunkRoadStyle();

            // First N chunks are kept flat (no hills/dips) as runway, but may still have gentle curves
            bool isRunway = chunk.ChunkIndex < initialFlatChunks;
            bool forceFlat = isRunway || !enableElevationVariation;
            bool rollSuccess = !forceFlat && (rng.Value <= config.elevationChance);

            RoadElevationType elevationType = RoadElevationType.Flat;

            if (rollSuccess)
            {
                int w0 = config.hillWeight;
                int w1 = w0 + config.valleyWeight;
                int w2 = w1 + config.rollingHillsWeight;
                int w3 = w2 + config.mountainPassWeight;
                int w4 = w3 + config.elevatedChicaneWeight;
                int totalWeight = Mathf.Max(1, w4 + config.flatWeight);
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
                    hill = rng.Range(config.minHillHeight, config.maxHillHeight);
                    cross = RoadCrossSectionPreset.GuardrailOnly;
                    break;
                case RoadElevationType.ValleyDip:
                    dip = rng.Range(config.minDipDepth, config.maxDipDepth);
                    cross = RoadCrossSectionPreset.OpenRoad;
                    break;
                case RoadElevationType.RollingHills:
                    hill = rng.Range(config.minHillHeight, config.maxHillHeight);
                    dip = rng.Range(config.minDipDepth, config.maxDipDepth);
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
            bool curvy = forceCurve || rng.Chance(curves.curveChance * style.curveChanceMultiplier);
            // e.g. mountain roads always wind (and the mountain meshes follow the road)
            if (style.alwaysCurvy) curvy = true;
            if (curvy && !forceCurve)
            {
                // No hazard chicanes on the runway chunks
                sharp = !isRunway && style.sharpCurveChance > 0f && rng.Chance(style.sharpCurveChance);
                if (elevationType == RoadElevationType.Flat)
                    cross = sharp ? RoadCrossSectionPreset.HazardFortified : RoadCrossSectionPreset.FullHighway;
            }
            // e.g. country road through the fields: no sidewalks or guardrails
            if (style.forceCrossSection) cross = style.forcedCrossSection;

            // Mountains: guardrails toward the drop on mountainside and cliff roads
            if (chunk.IsMountain && chunk.mountainLayout != MountainLayout.Valley) cross = RoadCrossSectionPreset.GuardrailOnly;

            float peak = spline.SetProceduralPreset(curves, curvy, sharp, elevationType, hill, dip, rng,
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
        /// Sets type identity, neighbor types and per-run rolls (sea side, mountain sub-type) on a freshly
        /// instantiated chunk from the deterministic type plan.
        /// </summary>
        private void ApplyTypePlan(Chunk chunk, int index)
        {
            TypePlanEntry entry = GetTypePlan(index);
            TypePlanEntry prev = index > 0 ? GetTypePlan(index - 1) : entry;
            TypePlanEntry next = GetTypePlan(index + 1);

            chunk.sourceType = entry.type;
            chunk.prevType = prev.type;
            chunk.nextType = next.type;
            chunk.prevUphillOnRight = prev.seaSide == CoastSide.Right;
            chunk.nextUphillOnRight = next.seaSide == CoastSide.Right;
            chunk.hasNeighborInfo = true;
            chunk.prevBiome = prev.Biome;
            chunk.nextBiome = next.Biome;
            chunk.prevMountainVariant = prev.variant;
            chunk.nextMountainVariant = next.variant;
            chunk.seaSide = entry.seaSide;
            chunk.mountainVariant = entry.variant;
            chunk.foothillLowland = entry.lowland;
            // Foothills: the run's side is the mountain side (road ledge offsets go toward it); the lowland is opposite
            if (entry.IsFoothill) chunk.uphillOnRight = entry.seaSide == CoastSide.Right;

            if (chunk.HasSea)
            {
                // A coast continues into a neighbor only if that neighbor is the same coast type, sea on the same side
                chunk.coastContinuesAtStart = index > 0 && IsSameCoast(prev, entry);
                chunk.coastContinuesAtEnd = IsSameCoast(next, entry);
            }
        }

        /// <summary>
        /// Road entry/exit offsets and heights and the layout of a mountain chunk, before Initialize (camps/events
        /// need to know the precipice side). A road that is off-center at either end runs as a ledge along the
        /// mountain on that side; a centered one goes through a pass (valley) or along a gentle mountainside (hillside).
        /// </summary>
        private void SetupMountainChunk(Chunk chunk, int index, int chunkSeed)
        {
            chunk.roadEntryX = SeamRoadOffset(index);
            chunk.roadExitX = SeamRoadOffset(index + 1);
            chunk.roadEntryY = SeamRoadHeight(index, chunk.baseElevation);
            chunk.roadExitY = SeamRoadHeight(index + 1, chunk.baseElevation);

            var rng = new SeededRandom(SeededRandom.Combine(chunkSeed, "mountain_layout"));
            float entry = chunk.roadEntryX, exit = chunk.roadExitX;
            bool offEntry = Mathf.Abs(entry) > 1f, offExit = Mathf.Abs(exit) > 1f;

            if (chunk.IsFoothill)
            {
                // Mountain on the run's side (set in ApplyTypePlan), lowland on the other, for the whole run
                chunk.mountainLayout = MountainLayout.Foothill;
            }
            else if ((offEntry || offExit) && entry * exit >= 0f)
            {
                chunk.mountainLayout = MountainLayout.Ledge;
                chunk.uphillOnRight = (offEntry ? entry : exit) > 0f;
            }
            else
            {
                // Centered road (or crossing from one side to the other): pass or gentle mountainside
                chunk.mountainLayout = rng.Chance(chunk.mountainSettings.hillsideChance) ? MountainLayout.Hillside : MountainLayout.Valley;
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
        /// Road height at seam i, shared by both chunks. Outside mountain runs (and at a run's ends) it is the
        /// baseline (20m). Inside a run of one procedural mountain type the road climbs at most roadClimbPerChunk
        /// per chunk toward a seeded roadMinHeight-roadMaxHeight, and descends again before the run ends.
        /// </summary>
        public float SeamRoadHeight(int seam, float baseline)
        {
            if (seam <= 0 || elevationMode != ElevationSelectionMode.DynamicWeightedRandom) return baseline;
            TypePlanEntry a = GetTypePlan(seam - 1), b = GetTypePlan(seam);
            if (!IsSameMountainRun(a, b)) return baseline;

            Chunk type = b.type;
            MountainTypeSettings ms = type.mountainSettings;

            // Position of this seam inside its mountain run: chunks before it / after it
            int before = 0;
            for (int i = seam - 1; i >= 0 && before < 32 && GetTypePlan(i).type == type; i--) before++;
            int after = 0;
            for (int i = seam; after < 32 && GetTypePlan(i).type == type; i++) after++;

            int worldSeed = ResolvedSeed != 0 ? ResolvedSeed : SeededRandom.HashString(masterSeed);
            var rng = new SeededRandom(SeededRandom.Combine(SeededRandom.Combine(worldSeed, "mtn_road_height"), seam));
            float target = rng.Range(ms.roadMinHeight, ms.roadMaxHeight);

            // Climb from the valley at the run's start, descend toward its end, at most roadClimbPerChunk per chunk
            float climb = ms.roadClimbPerChunk;
            return Mathf.Min(target, baseline + climb * before, baseline + climb * after);
        }

        /// <summary>
        /// Lateral road position at seam i (between chunk i-1 and chunk i), shared by both chunks.
        /// Only seams inside a run of one procedural mountain type can be off-center; the side follows the run's
        /// side, so consecutive off-center seams form a long ledge along one mountain.
        /// </summary>
        public float SeamRoadOffset(int seam)
        {
            if (seam <= 0 || elevationMode != ElevationSelectionMode.DynamicWeightedRandom) return 0f;
            TypePlanEntry before = GetTypePlan(seam - 1), after = GetTypePlan(seam);
            if (!IsSameMountainRun(before, after)) return 0f;

            MountainTypeSettings ms = after.type.mountainSettings;
            int worldSeed = ResolvedSeed != 0 ? ResolvedSeed : SeededRandom.HashString(masterSeed);
            var rng = new SeededRandom(SeededRandom.Combine(SeededRandom.Combine(worldSeed, "mtn_road_offset"), seam));
            if (!rng.Chance(ms.ledgeChance)) return 0f;

            float side = after.seaSide == CoastSide.Right ? 1f : -1f;
            return side * rng.Range(ms.ledgeRoadMinOffset, ms.ledgeRoadMaxOffset);
        }

        /// <summary>Both chunks belong to one run of the same procedural mountain type (its seam rules apply).</summary>
        private static bool IsSameMountainRun(TypePlanEntry a, TypePlanEntry b)
        {
            return a.type != null && a.type == b.type
                && a.type.biomeType == ChunkBiomeType.Mountain
                && a.type.generationMode == ChunkGenerationMode.Procedural;
        }

        private static bool IsSameCoast(TypePlanEntry a, TypePlanEntry b)
        {
            if (a.type == null || a.type != b.type || a.seaSide != b.seaSide) return false;
            if (a.type.biomeType == ChunkBiomeType.Coast) return true;
            // Foothills: same run (same lowland), and that lowland has a sea
            return a.FoothillSea && a.lowland == b.lowland;
        }

        private bool HasChunkTypes
        {
            get
            {
                if (chunkTypes == null) return false;
                for (int i = 0; i < chunkTypes.Count; i++)
                    if (chunkTypes[i] != null && chunkTypes[i].prefab != null) return true;
                return false;
            }
        }

        private Chunk StartType
        {
            get
            {
                if (startChunkType != null) return startChunkType;
                for (int i = 0; i < chunkTypes.Count; i++)
                    if (chunkTypes[i] != null && chunkTypes[i].prefab != null) return chunkTypes[i].prefab;
                return null;
            }
        }

        /// <summary>
        /// Type of any chunk index. Generated sequentially in runs from the master seed and cached, so neighbors can
        /// be queried before they are spawned and the same seed always gives the same sequence. A run picks a type
        /// (weighted, never the same type as the previous run), its length (min-max) and its per-run rolls.
        /// </summary>
        private TypePlanEntry GetTypePlan(int index)
        {
            if (index < 0) index = 0;
            if (!HasChunkTypes) return default;

            if (_typeRng == null)
            {
                // Edit mode: Awake has not resolved the seed yet, fall back to the seed text
                int seed = ResolvedSeed != 0 ? ResolvedSeed : SeededRandom.HashString(masterSeed);
                _typeRng = new SeededRandom(SeededRandom.Combine(seed, "biome_runs"));
                _typePlan.Clear();

                // Start run: its own stream so the main sequence does not depend on the start type
                Chunk start = StartType;
                var startRng = new SeededRandom(SeededRandom.Combine(seed, "start_run"));
                var startEntry = new TypePlanEntry
                {
                    type = start,
                    seaSide = startRng.Chance(0.5f) ? CoastSide.Left : CoastSide.Right,
                    variant = RollVariant(startRng, start),
                    lowland = RollLowland(startRng, start)
                };
                // At least the start type's own minimum run length (a 1-chunk mountain/foothill run has no inner
                // seams, so its road can neither climb nor move to the ledge)
                int startLength = Mathf.Max(1, startChunkCount);
                for (int i = 0; i < chunkTypes.Count; i++)
                {
                    if (chunkTypes[i] != null && chunkTypes[i].prefab == start) startLength = Mathf.Max(startLength, chunkTypes[i].minLength);
                }
                for (int i = 0; i < startLength; i++) _typePlan.Add(startEntry);
                _lastRunType = start;
            }

            while (_typePlan.Count <= index)
            {
                // Never repeat the same type as two consecutive runs (when there is an alternative)
                var candidates = new List<ChunkTypeEntry>();
                for (int i = 0; i < chunkTypes.Count; i++)
                {
                    var t = chunkTypes[i];
                    if (t != null && t.prefab != null && t.weight > 0 && t.prefab != _lastRunType) candidates.Add(t);
                }
                if (candidates.Count == 0)
                {
                    for (int i = 0; i < chunkTypes.Count; i++)
                        if (chunkTypes[i] != null && chunkTypes[i].prefab != null && chunkTypes[i].weight > 0) candidates.Add(chunkTypes[i]);
                }
                if (candidates.Count == 0)
                {
                    _typePlan.Add(new TypePlanEntry { type = StartType });
                    continue;
                }

                ChunkTypeEntry chosen = _typeRng.WeightedPick(candidates, t => t.weight);
                int length = _typeRng.Range(chosen.minLength, Mathf.Max(chosen.minLength, chosen.maxLength) + 1);
                CoastSide side = _typeRng.Chance(0.5f) ? CoastSide.Left : CoastSide.Right;
                // Mountain runs: one sub-type (snowy / alpine) for the whole run; `side` is the mountain the road follows
                MountainVariant variant = RollVariant(_typeRng, chosen.prefab);
                // Foothills: own stream per run, so other types' sequences are not affected by this roll
                int runSeed = SeededRandom.Combine(SeededRandom.Combine(_typeRng.Seed, "foothill_lowland"), _typePlan.Count);
                FoothillLowland lowland = RollLowland(new SeededRandom(runSeed), chosen.prefab);

                for (int i = 0; i < length; i++)
                {
                    _typePlan.Add(new TypePlanEntry { type = chosen.prefab, seaSide = side, variant = variant, lowland = lowland });
                }
                _lastRunType = chosen.prefab;
            }

            return _typePlan[index];
        }

        private static FoothillLowland RollLowland(SeededRandom rng, Chunk type)
        {
            if (type == null || type.biomeType != ChunkBiomeType.Mountain || type.mountainSettings == null || !type.mountainSettings.foothills)
                return FoothillLowland.Forest;
            return type.mountainSettings.RollLowland(rng);
        }

        private static MountainVariant RollVariant(SeededRandom rng, Chunk type)
        {
            return type != null && type.biomeType == ChunkBiomeType.Mountain && !rng.Chance(type.mountainSettings.snowyChance)
                ? MountainVariant.Alpine
                : MountainVariant.Snowy;
        }

        /// <summary>Planned terrain engine (biome) of a chunk index (spawned or not).</summary>
        public ChunkBiomeType GetPlannedBiome(int index)
        {
            return GetTypePlan(index).Biome;
        }

        /// <summary>Planned type prefab of a chunk index (spawned or not).</summary>
        public Chunk GetPlannedType(int index)
        {
            return GetTypePlan(index).type;
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
            _typeRng = null; // re-plan types from the (possibly changed) seed

            for (int i = 0; i < count; i++)
            {
                SpawnNextChunk();
            }

            // The build above already made terrain, POIs, edges, wrecks and vegetation; loot and notes normally wait for
            // play mode (Start), so place them here (a full GenerateEditorPreview would duplicate everything)
            foreach (var chunk in _activeChunks)
            {
                if (chunk == null) continue;
                var loot = chunk.GetComponentInChildren<RoadsideLootSpawner>();
                if (loot != null) loot.GenerateLoot(chunk.LootRandom, loot.transform);
                var notes = chunk.GetComponentInChildren<EndlessSurvival.World.POI.NoteSpawner>();
                if (notes != null) notes.GenerateNote(chunk.NotesRandom, notes.transform);
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
