using System;
using System.Collections;
using System.Collections.Generic;
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
        Mountain,
        Coast,
        Field
    }

    public enum CoastSide
    {
        Left,
        Right
    }

    public enum MountainLayout
    {
        Valley,     // Yol ortada: iki dağ arasındaki geçit
        Hillside,   // Dağ yamacı: bir yanda yükselen dağ, diğer yanda aşağıdaki yumuşak vadi
        Ledge,      // Yol kenarda: bir dağın yamacını takip eder, diğer yanı dik uçurum + kanyon (bazen nehir) + karşı dağ
        Foothill    // Dağ Eteği: yol yamaçta, bir yanda dağ, diğer yanda aşağıya inen yamaç ve geniş alçak arazi
    }

    /// <summary>Dağ Eteği tipinde dağın karşısındaki alçak arazi (bir bölüm boyunca aynı).</summary>
    public enum FoothillLowland
    {
        Forest,         // Uzayıp giden orman
        Field,          // Uzayıp giden düzlük (çit, tarla, mısır, dere)
        LandThenCoast,  // Biraz orman/düzlük, sonra kumsal ve deniz
        Coast           // Yamacın dibinde doğrudan kumsal ve deniz
    }

    public enum MountainVariant
    {
        Snowy,      // Karlı dağlar
        Alpine      // Karsız: çim + kaya, kar sadece zirvelerde
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

    public enum ChunkGenerationMode
    {
        Procedural,  // Yol, arazi, kenarlar çalışma anında seed'e göre üretilir
        Handcrafted  // Yol ve arazi prefab'da elle tasarlanmıştır; sadece prefab'da bulunan üretici bileşenler çalışır
    }

    /// <summary>
    /// Bu chunk tipinin yol karakteri (viraj sıklığı, keskin şikanlar, zorunlu kesit).
    /// </summary>
    [System.Serializable]
    public class ChunkRoadStyle
    {
        [Tooltip("RoadCurveSettings.curveChance ile çarpılır (düzlükte 0.6: daha seyrek viraj)")]
        [Range(0f, 2f)] public float curveChanceMultiplier = 1f;
        [Tooltip("Virajlı bir chunk'ın keskin şikan (HazardZone) olma ihtimali. 0 = hiç şikan yok.")]
        [Range(0f, 1f)] public float sharpCurveChance = 0.15f;
        [Tooltip("Yol her zaman virajlı (dağlarda dağ eteği yolla birlikte kıvrılır)")]
        public bool alwaysCurvy = false;
        [Tooltip("Açıksa yol kesiti eğime göre seçilmez, her zaman aşağıdaki kesit kullanılır")]
        public bool forceCrossSection = false;
        public RoadCrossSectionPreset forcedCrossSection = RoadCrossSectionPreset.OpenRoad;
    }

    /// <summary>
    /// Dağ motorlu (biomeType = Mountain) chunk tiplerinin alt tip ve yol ayarları.
    /// Bir dağ bölümü boyunca aynı tip kullanıldığı için seam değerleri tutarlıdır.
    /// </summary>
    [System.Serializable]
    public class MountainTypeSettings
    {
        [Tooltip("Bir dağ bölümünün karlı olma ihtimali (kalanı alpin: çim + kaya, kar sadece zirvelerde)")]
        [Range(0f, 1f)] public float snowyChance = 0.6f;
        [Tooltip("İki dağ chunk'ının birleştiği noktada yolun kenarda (bir dağın yamacında) olma ihtimali. " +
                 "Kenarda giden yolun diğer yanı uçurum + kanyon + karşı dağ olur.")]
        [Range(0f, 1f)] public float ledgeChance = 0.7f;
        [Tooltip("Kenardan giden yolun chunk merkezine uzaklığı")]
        public float ledgeRoadMinOffset = 80f;
        public float ledgeRoadMaxOffset = 100f;
        [Tooltip("Dağ bölümünün ortasında yolun ulaştığı yükseklik (ormanda yol 20m'de)")]
        public float roadMinHeight = 100f;
        public float roadMaxHeight = 140f;
        [Tooltip("Yolun bir chunk (500m) boyunca en fazla tırmanıp inebileceği yükseklik")]
        public float roadClimbPerChunk = 40f;
        [Tooltip("Yol ortadan giden dağ chunk'ında dağ yamacından (aşağısı yumuşak vadi) gitme ihtimali. Kalanında geçitten gider.")]
        [Range(0f, 1f)] public float hillsideChance = 0.4f;

        [Header("Dağ Eteği (tek taraf dağ)")]
        [Tooltip("Açıksa bu tip Dağ Eteği'dir: yol yamaçta, bir yanda dağ, diğer yanda aşağıya inen yamaç ve alçak arazi (orman / düzlük / sahil)")]
        public bool foothills = false;
        [Tooltip("Alçak tarafın türü her bölüm için bu ağırlıklarla seçilir")]
        [Range(0, 100)] public int lowlandForestWeight = 30;
        [Range(0, 100)] public int lowlandFieldWeight = 30;
        [Range(0, 100)] public int lowlandLandThenCoastWeight = 20;
        [Range(0, 100)] public int lowlandCoastWeight = 20;
        [Tooltip("Yol kenarından alçak araziye inişin yatay genişliği. Küçük = dimdik uçurum (kenar yolu gibi), büyük (70+) = yumuşak yamaç")]
        public float lowlandSlopeMin = 8f;
        public float lowlandSlopeMax = 16f;
        [Tooltip("Uçurum ağzının yol kenarından uzaklığı (dar omuz)")]
        public float lowlandSlopeStart = 2.5f;
        [Tooltip("LandThenCoast: kıyı çizgisinin yamaç dibinden uzaklığı (arada orman/düzlük şeridi)")]
        public float landStripMin = 50f;
        public float landStripMax = 100f;
        [Tooltip("Coast: kıyı çizgisinin yamaç dibinden uzaklığı (dar kumsal)")]
        public float coastStripMin = 10f;
        public float coastStripMax = 30f;

        public FoothillLowland RollLowland(SeededRandom rng)
        {
            int w0 = Mathf.Max(0, lowlandForestWeight);
            int w1 = w0 + Mathf.Max(0, lowlandFieldWeight);
            int w2 = w1 + Mathf.Max(0, lowlandLandThenCoastWeight);
            int total = w2 + Mathf.Max(0, lowlandCoastWeight);
            if (total <= 0) return FoothillLowland.Forest;
            int roll = rng.Range(0, total);
            if (roll < w0) return FoothillLowland.Forest;
            if (roll < w1) return FoothillLowland.Field;
            if (roll < w2) return FoothillLowland.LandThenCoast;
            return FoothillLowland.Coast;
        }

        /// <summary>Distance from the road center to the foot of the lowland slope (where the lowland begins).</summary>
        public float LowlandFootDistance(float roadHalfWidth) => roadHalfWidth + lowlandSlopeStart + lowlandSlopeMax;
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
        [Range(1f, 14f)]
        public float minHillHeight = 8f;

        [Tooltip("Maximum depth a valley or pit can dip down into the ground in meters (baseline is 20m, sea level is 0m)")]
        [Range(5f, 18f)]
        public float maxDipDepth = 15f;

        [Tooltip("Minimum depth for a generated valley dip in meters")]
        [Range(1f, 12f)]
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
    /// One 500x500 parcel of the endless road. Each chunk TYPE is its own prefab (Chunk_Forest, Chunk_Mountain, ...)
    /// that carries all of its type-specific settings (road style, elevation, sub-types, generator components).
    /// ChunkManager only holds world-wide settings and the weighted list of type prefabs it picks from.
    /// </summary>
    [ExecuteAlways]
    public class Chunk : MonoBehaviour
    {
        [Header("Chunk Tipi")]
        [Tooltip("Bu tipin görünen adı (log ve chunk isimlerinde). Boşsa prefab adı kullanılır.")]
        public string typeName = "";

        [Tooltip("Procedural: yol/arazi seed'e göre üretilir. Handcrafted: yol ve arazi prefab'da elle tasarlanmıştır " +
                 "(giriş ve çıkış yol merkezinde X=0, baseElevation yüksekliğinde olmalı, arazinin Z=0 ve Z=500 kenarları baseElevation'da kalmalı).")]
        public ChunkGenerationMode generationMode = ChunkGenerationMode.Procedural;

        [Tooltip("Kenarları düz (uçurumsuz) bir tip mi? Komşu chunk'ların uçurumları bu tipe değen seam'de sıfıra iner. Düzlük ve elle tasarlanmış tipler için açık olmalı.")]
        public bool flatSeamEdges = false;

        [Header("Classification & Parameters")]
        [Tooltip("Arazi motoru: kenar/kıyı/dağ/düzlük üreticilerinin bu chunk'ta hangi davranışı kullanacağı")]
        public ChunkBiomeType biomeType = ChunkBiomeType.Forest;

        [Tooltip("Road layout type for this chunk (Straight, Curved, Chicane, etc.)")]
        public ChunkRoadType roadType = ChunkRoadType.Straight;

        [Tooltip("Road cross-section style (Kaldırımlı, Bariyerli, Otoyol, Açık Kırsal, vb.)")]
        public RoadCrossSectionPreset crossSectionType = RoadCrossSectionPreset.FullHighway;

        [Tooltip("Difficulty tier of this chunk (for enemy/loot scaling in later phases)")]
        [Range(1, 5)]
        public int difficultyTier = 1;

        [Header("Coast (Deniz Kıyısı) - sadece biomeType = Coast iken")]
        [Tooltip("Denizin olduğu taraf. Oyunda ChunkManager biyom sırasına göre ayarlar.")]
        public CoastSide seaSide = CoastSide.Right;

        [Tooltip("Önceki chunk da aynı taraflı kıyı mı? Değilse chunk başında karadan denize geçiş (burun) oluşur.")]
        public bool coastContinuesAtStart = true;

        [Tooltip("Sonraki chunk da aynı taraflı kıyı mı? Değilse chunk sonunda denizden karaya geçiş oluşur.")]
        public bool coastContinuesAtEnd = true;

        [Header("Mountain (Dağ) - sadece biomeType = Mountain iken")]
        [Tooltip("Karlı veya alpin (karsız). Oyunda bir dağ bölümü boyunca aynıdır.")]
        public MountainVariant mountainVariant = MountainVariant.Snowy;
        [Tooltip("Yol ortada (geçit), dağ yamacında, ya da kenarda (uçurum + kanyon). Oyunda yolun konumuna göre ChunkManager seçer.")]
        public MountainLayout mountainLayout = MountainLayout.Valley;
        [Tooltip("Yamaç / kenar yerleşiminde dağın yükseldiği taraf (diğer taraf vadi veya uçurum)")]
        public bool uphillOnRight = true;
        [Tooltip("Dağ Eteği tipinde dağın karşısındaki alçak arazi. Oyunda ChunkManager bölüm başına seçer.")]
        public FoothillLowland foothillLowland = FoothillLowland.Forest;

        [Header("Yolun Yanal Konumu (chunk merkezine göre)")]
        [Tooltip("Yolun chunk'a girdiği X (0 = orta). Sadece dağ-dağ birleşimlerinde 0 olmaz; komşu chunk'ın çıkışıyla aynıdır.")]
        public float roadEntryX = 0f;
        [Tooltip("Yolun chunk'tan çıktığı X (0 = orta). Komşu chunk'ın girişiyle aynıdır.")]
        public float roadExitX = 0f;

        [Header("Yol Yüksekliği (chunk sınırlarında)")]
        [Tooltip("Yolun chunk'a girdiği yükseklik. Dağ dışındaki biyomlarda hep baseElevation (20m); dağlarda 100m+ olabilir. Komşu chunk'ın çıkışıyla aynıdır.")]
        public float roadEntryY = 20f;
        [Tooltip("Yolun chunk'tan çıktığı yükseklik. Komşu chunk'ın girişiyle aynıdır.")]
        public float roadExitY = 20f;

        public bool IsCoast => biomeType == ChunkBiomeType.Coast;
        public bool IsField => biomeType == ChunkBiomeType.Field;
        public bool IsMountain => biomeType == ChunkBiomeType.Mountain;

        /// <summary>Dağ Eteği: mountain on one side only, lowland (forest/field/coast) on the other.</summary>
        public bool IsFoothill => IsMountain && mountainSettings != null && mountainSettings.foothills;
        /// <summary>Chunk-local X side (-1 / +1) of the lowland on a foothill chunk (0 otherwise).</summary>
        public float LowlandSign => IsFoothill ? (uphillOnRight ? -1f : 1f) : 0f;
        public bool HasFoothillSea => IsFoothill && (foothillLowland == FoothillLowland.LandThenCoast || foothillLowland == FoothillLowland.Coast);
        /// <summary>The chunk has a sea on its <see cref="seaSide"/>: coast chunks and foothills with a coast lowland.</summary>
        public bool HasSea => IsCoast || HasFoothillSea;
        /// <summary>Field edge decorations (fences, corn, streams): field chunks, and the lowland of field foothills.</summary>
        public bool UsesFieldEdges => IsField || (IsFoothill && foothillLowland == FoothillLowland.Field);

        // Type identity and neighbor types, set by ChunkManager from the type plan. Without that info (a lone chunk
        // previewed in the editor) both neighbors are assumed to be the same type, i.e. seamless.
        /// <summary>The type prefab this chunk was spawned from (null for a chunk placed in the scene by hand).</summary>
        [System.NonSerialized] public Chunk sourceType;
        [System.NonSerialized] public Chunk prevType;
        [System.NonSerialized] public Chunk nextType;
        // Mountain side of the neighbors' runs (foothill neighbors show their lowland horizon on the other side)
        [System.NonSerialized] public bool prevUphillOnRight;
        [System.NonSerialized] public bool nextUphillOnRight;

        public string TypeName => !string.IsNullOrEmpty(typeName) ? typeName
            : sourceType != null ? sourceType.name : name;

        /// <summary>The previous chunk is the same type (a type's own seam rules apply: coast, mountain road, streams...).</summary>
        public bool PrevSameType => !hasNeighborInfo || (prevType != null && prevType == sourceType);
        public bool NextSameType => !hasNeighborInfo || (nextType != null && nextType == sourceType);

        /// <summary>The neighbor has flat (cliff-less) edges, so boundary cliffs must come down to 0 at that seam.</summary>
        public bool PrevFlatSeam => hasNeighborInfo && prevType != null ? prevType.flatSeamEdges : flatSeamEdges;
        public bool NextFlatSeam => hasNeighborInfo && nextType != null ? nextType.flatSeamEdges : flatSeamEdges;

        [System.NonSerialized] public bool hasNeighborInfo;
        [System.NonSerialized] public ChunkBiomeType prevBiome;
        [System.NonSerialized] public ChunkBiomeType nextBiome;
        public ChunkBiomeType PrevBiome => hasNeighborInfo ? prevBiome : biomeType;
        public ChunkBiomeType NextBiome => hasNeighborInfo ? nextBiome : biomeType;
        [System.NonSerialized] public MountainVariant prevMountainVariant;
        [System.NonSerialized] public MountainVariant nextMountainVariant;
        public MountainVariant PrevMountainVariant => hasNeighborInfo ? prevMountainVariant : mountainVariant;
        public MountainVariant NextMountainVariant => hasNeighborInfo ? nextMountainVariant : mountainVariant;
        /// <summary>-1 = sea on the left (-X), +1 = sea on the right (+X). Foothills: always the lowland side.</summary>
        public float SeaSign => IsFoothill ? LowlandSign : seaSide == CoastSide.Left ? -1f : 1f;

        /// <summary>
        /// Land-only objects (camps, caves, lighthouses) must never be placed on the sea side of a coast chunk.
        /// Returns the rolled side unchanged for non-coast chunks.
        /// </summary>
        public float GetLandSide(float rolledSide)
        {
            // Foothills: down in the lowland (forest/field), or up on the mountain side when the lowland is a coast
            if (IsFoothill) return HasSea ? -SeaSign : LowlandSign;
            if (IsCoast) return -SeaSign;
            // Cliff road: the other side of the road is a precipice, so land objects go on the mountain side
            if (IsMountain && mountainLayout == MountainLayout.Ledge) return uphillOnRight ? 1f : -1f;
            return rolledSide;
        }

        [Header("Bu Tipin Yol Ayarları (Procedural)")]
        [Tooltip("Bu tipin eğim (tepe/vadi) ihtimalleri ve yükseklik sınırları")]
        public ChunkElevationConfig elevationConfig = new ChunkElevationConfig();

        [Tooltip("Bu tipin viraj üretimi (viraj sayısı, sapma, dönüş yarıçapı)")]
        public RoadCurveSettings curveSettings = new RoadCurveSettings();

        [Tooltip("Bu tipin yol karakteri (viraj sıklığı, şikan, zorunlu kesit)")]
        public ChunkRoadStyle roadStyle = new ChunkRoadStyle();

        [Tooltip("Sadece biomeType = Mountain iken: alt tip (karlı/alpin) ve dağ yolu ayarları")]
        public MountainTypeSettings mountainSettings = new MountainTypeSettings();

        // Old single-prefab setup: "use my own elevationConfig instead of ChunkManager's global one".
        // Only read by the chunk type migration tool (ChunkTypeSetupUtility).
        [HideInInspector, UnityEngine.Serialization.FormerlySerializedAs("overrideElevationSettings")]
        public bool legacyOverrideElevationSettings = false;

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
        private SeededRandom _wrecksRandom;

        /// <summary>
        /// Seed shared by every chunk of the same world (for features that must line up across chunks:
        /// backdrop mountains, streams, corn strips). Edit-mode lone chunks use their preview seed.
        /// </summary>
        public int WorldSeed
        {
            get
            {
                if (_manager != null)
                {
                    // Edit mode: Awake has not resolved the seed yet, fall back to the seed text
                    return _manager.ResolvedSeed != 0 ? _manager.ResolvedSeed : SeededRandom.HashString(_manager.masterSeed);
                }
#if UNITY_EDITOR
                return GetEditorPreviewSeed();
#else
                return _seed;
#endif
            }
        }

        /// <summary>
        /// False while ChunkManager is still building this chunk over several frames (road, terrain, edges...).
        /// Components that place things on the terrain in Start must wait for <see cref="Built"/>.
        /// Chunks not built by ChunkManager (scene/editor) count as built.
        /// </summary>
        public bool IsBuilt { get; private set; } = true;

        /// <summary>Raised once when a time-sliced build finishes.</summary>
        public event Action Built;

        public void BeginBuild()
        {
            IsBuilt = false;
        }

        public void MarkBuilt()
        {
            if (IsBuilt) return;
            IsBuilt = true;
            Action handlers = Built;
            Built = null;
            handlers?.Invoke();
        }

        /// <summary>Runs <paramref name="action"/> now if the chunk is built, otherwise when its build finishes.</summary>
        public void WhenBuilt(Action action)
        {
            if (IsBuilt) action();
            else Built += action;
        }

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
        /// <summary>Per-chunk deterministic random for abandoned vehicle wrecks.</summary>
        public SeededRandom WrecksRandom => _wrecksRandom;

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
            IEnumerator steps = InitializeSteps(manager, index, seed);
            while (steps.MoveNext()) { }
        }

        /// <summary>
        /// Same as <see cref="Initialize"/>, split so a time-sliced chunk build can spawn the camp (with its enemies),
        /// the POIs and the story event in separate frames.
        /// </summary>
        public IEnumerator InitializeSteps(ChunkManager manager, int index, int seed)
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
            _wrecksRandom = chunkRng.SubStream("wrecks");

            // Initialize or spawn enemy camps (Campfire, Outpost vb.)
            var campSpawner = GetComponentInChildren<EndlessSurvival.World.POI.EnemyCampSpawner>();
            if (campSpawner == null)
            {
                campSpawner = gameObject.AddComponent<EndlessSurvival.World.POI.EnemyCampSpawner>();
            }
            campSpawner.SpawnCampIfEligible(this);
            yield return "camp";

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
            yield return "pois";

            // Initialize or spawn roadside story events (Deniz Feneri vb.)
            var eventSpawner = GetComponentInChildren<StoryEventSpawner>();
            if (eventSpawner == null)
            {
                eventSpawner = gameObject.AddComponent<StoryEventSpawner>();
            }
            eventSpawner.SpawnEventIfEligible(this);
            yield return "event";

            if (backBlockade != null)
            {
                backBlockade.SetActive(false);
            }
        }

        private class PoiGroundInfo
        {
            public PointOfInterest poi;
            public readonly List<Transform> children = new List<Transform>();
            public readonly List<float> offsets = new List<float>();
            public readonly List<bool> onPlatform = new List<bool>();
        }

        private readonly List<PoiGroundInfo> _poiGrounding = new List<PoiGroundInfo>();

        /// <summary>
        /// Camps and story structures get their position in Initialize, before the terrain is shaped. Call this
        /// right before shaping it: remembers how high each POI's direct children (props, enemies) stand -
        /// relative to the POI for those on its platform, relative to the ground for those further out.
        /// </summary>
        public void CapturePoiGrounding()
        {
            _poiGrounding.Clear();
            Terrain terrain = ChunkTerrain;

            var pois = GetComponentsInChildren<PointOfInterest>(false);
            for (int i = 0; i < pois.Length; i++)
            {
                Transform t = pois[i].transform;
                // Nested POIs move with their parent POI
                if (t.parent != null && t.parent.GetComponentInParent<PointOfInterest>() != null) continue;

                var info = new PoiGroundInfo { poi = pois[i] };
                float platform = pois[i].GroundFlatRadius + 1f;
                for (int c = 0; c < t.childCount; c++)
                {
                    Transform child = t.GetChild(c);
                    Vector3 d = child.position - t.position;
                    d.y = 0f;
                    bool near = d.magnitude <= platform || terrain == null;
                    info.children.Add(child);
                    info.onPlatform.Add(near);
                    info.offsets.Add(near ? child.position.y - t.position.y : child.position.y - GroundY(terrain, child.position));
                }
                _poiGrounding.Add(info);
            }
        }

        /// <summary>
        /// After the terrain is shaped: every POI drops to the natural ground height at its own position (it never
        /// pulls the terrain to the road's height), the ground under it is leveled at that height, and its children
        /// follow (platform props keep their height above the POI, outer ones stand on the new ground).
        /// </summary>
        public void GroundPointsOfInterest()
        {
            Terrain terrain = ChunkTerrain;
            if (terrain == null) { _poiGrounding.Clear(); return; }
            if (_poiGrounding.Count == 0) CapturePoiGrounding();

            for (int i = 0; i < _poiGrounding.Count; i++)
            {
                PoiGroundInfo info = _poiGrounding[i];
                if (info.poi == null || !info.poi.gameObject.activeInHierarchy) continue;

                Transform t = info.poi.transform;
                Vector3 p = t.position;
                float y = GroundY(terrain, p);
                Road.RoadTerrainAdapter.FlattenTerrainArea(terrain, p, info.poi.GroundFlatRadius, info.poi.GroundBlendRadius, y);
                t.position = new Vector3(p.x, y, p.z);

                for (int c = 0; c < info.children.Count; c++)
                {
                    Transform child = info.children[c];
                    if (child == null) continue;
                    Vector3 cp = child.position;
                    cp.y = info.onPlatform[c] ? y + info.offsets[c] : GroundY(terrain, cp) + info.offsets[c];
                    child.position = cp;
                }
            }
            _poiGrounding.Clear();
        }

        private static float GroundY(Terrain terrain, Vector3 world)
        {
            return terrain.SampleHeight(world) + terrain.transform.position.y;
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
            // The runtime TerrainData clone (RoadTerrainAdapter.EnsureRuntimeTerrainClone) is an asset object that
            // is not freed with the GameObject; without this every passed chunk leaked heights, splat maps and trees
            Terrain terrain = ChunkTerrain;
            if (terrain != null && terrain.terrainData != null && terrain.gameObject.name.Contains("(RuntimeClone)"))
            {
                Destroy(terrain.terrainData);
            }
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

            // Procedural types get the standard generators; handcrafted ones only run what their prefab has
            bool procedural = generationMode == ChunkGenerationMode.Procedural;

            // Side boundaries: re-shape terrain (cliffs) and place rocks for this preview seed
            var boundary = GetComponent<ChunkBoundaryGenerator>();
            if (boundary == null && procedural) boundary = gameObject.AddComponent<ChunkBoundaryGenerator>();
            if (boundary != null) boundary.SetPreviewSeed(seed);

            var fieldEdges = GetComponent<FieldEdgeGenerator>();
            if (fieldEdges == null && UsesFieldEdges && procedural) fieldEdges = gameObject.AddComponent<FieldEdgeGenerator>();

            Terrain previewTerrain = ChunkTerrain;
            var previewSpline = GetComponentInChildren<Road.RoadSpline>();
            CapturePoiGrounding();
            if (procedural && previewTerrain != null && previewSpline != null)
            {
                Road.RoadTerrainAdapter.ConformTerrainToRoad(previewTerrain, previewSpline, GetComponentInChildren<Road.RoadGenerator>(), true);
            }
            GroundPointsOfInterest();
            if (boundary != null) boundary.SpawnBoundaryRocks(previewContainer.transform, previewTerrain);

            var backdrop = GetComponent<ChunkBackdropGenerator>();
            if (backdrop == null && procedural) backdrop = gameObject.AddComponent<ChunkBackdropGenerator>();
            if (backdrop != null) backdrop.Generate(previewContainer.transform, previewTerrain);

            if (fieldEdges != null) fieldEdges.Generate(previewContainer.transform, previewTerrain);

            // In play mode the endless OceanSurface provides the sea; the preview gets its own water quad
            if (HasSea) OceanSurface.CreatePreviewQuad(previewContainer.transform, this);

            // Same order as play mode: loot and notes are placed after the terrain is shaped (never in the sea)
            var lootSpawner = GetComponentInChildren<RoadsideLootSpawner>();
            if (lootSpawner != null) lootSpawner.GenerateLoot(lootRng, previewContainer.transform);

            var noteSpawner = GetComponentInChildren<EndlessSurvival.World.POI.NoteSpawner>();
            if (noteSpawner != null) noteSpawner.GenerateNote(notesRng, previewContainer.transform);

            var wrecks = GetComponent<AbandonedVehicleSpawner>();
            if (wrecks == null && procedural) wrecks = gameObject.AddComponent<AbandonedVehicleSpawner>();
            if (wrecks != null) wrecks.Generate(previewContainer.transform, previewTerrain, chunkRng.SubStream("wrecks"));

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
