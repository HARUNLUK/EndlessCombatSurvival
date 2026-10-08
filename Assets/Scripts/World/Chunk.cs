using System;
using UnityEngine;
using EndlessSurvival.World.Road;
using EndlessSurvival.World.POI;

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
    [ExecuteAlways]
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

        [Header("Editor Preview")]
        [Tooltip("Type a seed here to preview deterministic generation in the editor. Leave empty for random.")]
        public string editorPreviewSeed = "";

        [Tooltip("Trigger or barrier that blocks backward passage after moving forward")]
        public GameObject backBlockade;

        [Header("Object Pooling / Spawn Points")]
        [Tooltip("Predefined spawn points inside this chunk for randomized props/hazards/loot")]
        public Transform[] propSpawnPoints;

        [Header("On-Road Spawn Sockets (Yol Üstü Özel Noktalar)")]
        [Tooltip("Yol üzerinde özel olarak tanımlanmış spawn noktaları (barikatlar, terkedilmiş araçlar, pusu tuzakları vb.). Yalnızca OnRoad olarak işaretlenmiş nesneler burada oluşabilir. Yol dışı nesneler (kamp, mağara, bina) bu noktalarda ve yol koridorunda ASLA oluşamaz.")]
        public Transform[] onRoadSpawnPoints;

        /// <summary>
        /// Yol üzerinde tanımlanmış özel noktayı döner. Eğer manuel socket atanmamışsa spline üzerindeki uygun bir noktayı dinamik olarak hesaplar.
        /// </summary>
        public bool TryGetOnRoadPosition(SeededRandom rng, out Vector3 position, out Quaternion rotation)
        {
            position = Vector3.zero;
            rotation = Quaternion.identity;

            if (onRoadSpawnPoints != null && onRoadSpawnPoints.Length > 0)
            {
                var validPoints = new System.Collections.Generic.List<Transform>();
                for (int i = 0; i < onRoadSpawnPoints.Length; i++)
                {
                    if (onRoadSpawnPoints[i] != null) validPoints.Add(onRoadSpawnPoints[i]);
                }
                if (validPoints.Count > 0)
                {
                    Transform chosen = rng.Pick(validPoints.ToArray());
                    position = chosen.position;
                    rotation = chosen.rotation;
                    return true;
                }
            }

            // Fallback: Spline üzerinden yolun tam orta çizgisinde bir nokta al
            var spline = GetComponentInChildren<Road.RoadSpline>();
            if (spline != null)
            {
                float t = rng.Range(0.25f, 0.75f);
                position = spline.transform.TransformPoint(spline.GetPoint(t));
                Vector3 forward = spline.transform.TransformDirection(spline.GetTangent(t));
                rotation = Quaternion.LookRotation(forward);
                return true;
            }

            return false;
        }

        // Runtime state
        private ChunkManager _manager;
        private int _chunkIndex;
        private int _seed;

        // Sub-stream random generators for each system.
        // Adding a new sub-stream never alters the output of existing ones.
        private SeededRandom _roadRandom;
        private SeededRandom _lootRandom;
        private SeededRandom _poiRandom;
        private SeededRandom _campRandom;
        private SeededRandom _eventsRandom;
        private SeededRandom _notesRandom;

        public int ChunkIndex => _chunkIndex;
        public ChunkManager Manager => _manager;
        public int ChunkSeed => _seed;
        
        public Terrain ChunkTerrain => GetComponentInChildren<Terrain>();

        /// <summary>Per-chunk deterministic random for road generation.</summary>
        public SeededRandom RoadRandom => _roadRandom;
        /// <summary>Per-chunk deterministic random for loot placement and rolls.</summary>
        public SeededRandom LootRandom => _lootRandom;
        /// <summary>Per-chunk deterministic random for POI spawn chance.</summary>
        public SeededRandom PoiRandom => _poiRandom;
        /// <summary>Per-chunk deterministic random for enemy camp details.</summary>
        public SeededRandom CampRandom => _campRandom;
        /// <summary>Per-chunk deterministic random for story events (future).</summary>
        public SeededRandom EventsRandom => _eventsRandom;
        /// <summary>Per-chunk deterministic random for readable notes (future).</summary>
        public SeededRandom NotesRandom => _notesRandom;

        private void Awake()
        {
            if (Application.isPlaying)
            {
                Transform preview = transform.Find("EditorPreview");
                if (preview != null)
                {
                    // Destroy() is deferred to end of frame; rename + deactivate so nothing
                    // (e.g. ChunkVegetationSpawner) can still find it and parent runtime objects under it.
                    preview.name = "EditorPreview_Destroying";
                    preview.gameObject.SetActive(false);
                    Destroy(preview.gameObject);
                }
            }
        }

#if UNITY_EDITOR
        private void OnEnable()
        {
            if (!Application.isPlaying)
            {
                UnityEditor.EditorApplication.delayCall += AutoCheckPreview;
            }
        }

        private void AutoCheckPreview()
        {
            if (this == null || gameObject == null || Application.isPlaying) return;
            Transform preview = transform.Find("EditorPreview");
            if (preview == null || preview.childCount == 0)
            {
                GenerateEditorPreview(GetEditorPreviewSeed());
            }
        }

        /// <summary>Seed used by editor previews: ChunkManager seed if spawned by it, else editorPreviewSeed.</summary>
        public int GetEditorPreviewSeed()
        {
            if (_seed != 0) return _seed;
            return string.IsNullOrEmpty(editorPreviewSeed)
                ? GetHashCode()
                : SeededRandom.HashString(editorPreviewSeed);
        }
#endif

        public void Initialize(ChunkManager manager, int index, int seed)
        {
            _manager = manager;
            _chunkIndex = index;
            _seed = seed;

            // Derive independent sub-stream seeds
            var chunkRng = new SeededRandom(seed);
            _roadRandom = chunkRng.SubStream("road");
            _lootRandom = chunkRng.SubStream("loot");
            _poiRandom = chunkRng.SubStream("poi");
            _campRandom = chunkRng.SubStream("camp");
            _eventsRandom = chunkRng.SubStream("events");
            _notesRandom = chunkRng.SubStream("notes");

            // Initialize or spawn enemy camps (Campfire, Outpost vb.)
            var campSpawner = GetComponentInChildren<EndlessSurvival.World.POI.EnemyCampSpawner>();
            if (campSpawner == null)
            {
                campSpawner = gameObject.AddComponent<EndlessSurvival.World.POI.EnemyCampSpawner>();
            }
            campSpawner.SpawnCampIfEligible(this);

            // Initialize all POIs deterministically instead of relying on Unity's Start() order
            var pois = GetComponentsInChildren<PointOfInterest>(true);
            for (int i = 0; i < pois.Length; i++)
            {
                // Each POI gets its own sub-stream so POI order doesn't matter
                SeededRandom poiSubRng;
                if (pois[i] is EnemyCampPOI)
                    poiSubRng = new SeededRandom(SeededRandom.Combine(_campRandom.Seed, i));
                else
                    poiSubRng = new SeededRandom(SeededRandom.Combine(_poiRandom.Seed, i));

                pois[i].InitializeFromChunk(poiSubRng);
            }

            // Initialize or spawn roadside story events (Deniz Feneri vb.)
            var eventSpawner = GetComponentInChildren<StoryEventSpawner>();
            if (eventSpawner == null)
            {
                eventSpawner = gameObject.AddComponent<StoryEventSpawner>();
            }
            eventSpawner.SpawnEventIfEligible(this);

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

#if UNITY_EDITOR
        public void GenerateEditorPreview(int seed)
        {
            Transform oldPreview = transform.Find("EditorPreview");
            if (oldPreview != null) DestroyImmediate(oldPreview.gameObject);

            GameObject previewContainer = new GameObject("EditorPreview");
            previewContainer.transform.SetParent(transform, false);

            var chunkRng = new SeededRandom(seed);
            var lootRng = chunkRng.SubStream("loot");
            var poiRng = chunkRng.SubStream("poi");
            var campRng = chunkRng.SubStream("camp");
            var eventsRng = chunkRng.SubStream("events");
            var notesRng = chunkRng.SubStream("notes");

            var lootSpawner = GetComponentInChildren<RoadsideLootSpawner>();
            if (lootSpawner != null) lootSpawner.GenerateLoot(lootRng, previewContainer.transform);

            var noteSpawner = GetComponentInChildren<EndlessSurvival.World.POI.NoteSpawner>();
            if (noteSpawner != null) noteSpawner.GenerateNote(notesRng, previewContainer.transform);

            var campSpawner = GetComponentInChildren<EndlessSurvival.World.POI.EnemyCampSpawner>();
            if (campSpawner != null) campSpawner.GenerateCampPreview(campRng, previewContainer.transform);

            var eventSpawner = GetComponentInChildren<StoryEventSpawner>();
            if (eventSpawner != null) eventSpawner.GenerateEventPreview(eventsRng, previewContainer.transform);

            var pois = GetComponentsInChildren<EndlessSurvival.World.POI.PointOfInterest>(true);
            for (int i = 0; i < pois.Length; i++)
            {
                SeededRandom poiSubRng;
                if (pois[i] is EndlessSurvival.World.POI.EnemyCampPOI)
                    poiSubRng = new SeededRandom(SeededRandom.Combine(campRng.Seed, i));
                else
                    poiSubRng = new SeededRandom(SeededRandom.Combine(poiRng.Seed, i));

                pois[i].InitializeFromChunk(poiSubRng, previewContainer.transform);
            }

            // Side boundaries: re-shape terrain (cliffs) and place rocks for this preview seed
            var boundary = GetComponent<ChunkBoundaryGenerator>();
            if (boundary == null) boundary = gameObject.AddComponent<ChunkBoundaryGenerator>();
            boundary.SetPreviewSeed(seed);

            Terrain previewTerrain = ChunkTerrain;
            var previewSpline = GetComponentInChildren<Road.RoadSpline>();
            if (previewTerrain != null && previewSpline != null)
            {
                Road.RoadTerrainAdapter.ConformTerrainToRoad(previewTerrain, previewSpline, GetComponentInChildren<Road.RoadGenerator>(), true);
            }
            boundary.SpawnBoundaryRocks(previewContainer.transform, previewTerrain);

            var backdrop = GetComponent<ChunkBackdropGenerator>();
            if (backdrop == null) backdrop = gameObject.AddComponent<ChunkBackdropGenerator>();
            backdrop.Generate(previewContainer.transform, previewTerrain);

            var vegRng = chunkRng.SubStream("vegetation");
            var vegSpawner = GetComponentInChildren<ChunkVegetationSpawner>();
            if (vegSpawner != null) vegSpawner.GenerateVegetation(previewContainer.transform, vegRng);
        }
#endif

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

            if (onRoadSpawnPoints != null)
            {
                Gizmos.color = Color.yellow;
                for (int i = 0; i < onRoadSpawnPoints.Length; i++)
                {
                    if (onRoadSpawnPoints[i] != null)
                    {
                        Gizmos.DrawWireSphere(onRoadSpawnPoints[i].position, 2f);
                        Gizmos.DrawRay(onRoadSpawnPoints[i].position, onRoadSpawnPoints[i].forward * 4f);
                    }
                }
            }

            // Draw bounding box (500x500)
            Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.25f);
            Vector3 center = transform.position + new Vector3(0f, 0f, chunkLength * 0.5f);
            Gizmos.DrawWireCube(center, new Vector3(500f, 20f, chunkLength));
        }
    }
}
