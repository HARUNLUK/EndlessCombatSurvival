using System.Collections.Generic;
using UnityEngine;
using EndlessSurvival.World.Road;
using EndlessSurvival.World.POI;

namespace EndlessSurvival.World
{
    public enum ChunkBoundaryStyle
    {
        Cliff,          // Arazi kenarda dik şekilde yukarı kıvrılır
        Rocks,          // Kenar dev kayalarla kapanır
        CliffAndRocks   // Uçurum + eteğinde dev kayalar
    }

    /// <summary>
    /// Biyoma özel kenar sınırlayıcı ayarları.
    /// </summary>
    [System.Serializable]
    public class BiomeBoundarySettings
    {
        public ChunkBiomeType biome = ChunkBiomeType.Forest;

        [Header("Stil Ağırlıkları (her taraf için ayrı seçilir)")]
        [Range(0, 100)] public int cliffWeight = 40;
        [Range(0, 100)] public int rocksWeight = 25;
        [Range(0, 100)] public int cliffAndRocksWeight = 35;

        [Header("Uçurum")]
        [Tooltip("Uçurumun baseline (20m) üstündeki yüksekliği")]
        public float minCliffHeight = 35f;
        public float maxCliffHeight = 65f;

        [Tooltip("Uçurum yüzünün yatay genişliği. Küçük = daha dik")]
        public float minRampWidth = 18f;
        public float maxRampWidth = 38f;

        [Tooltip("Uçurumun başladığı mesafe (chunk merkez çizgisinden)")]
        public float minInnerEdge = 140f;
        public float maxInnerEdge = 190f;

        [Header("Kayalar (basit küp kümeleri)")]
        public float minRockSize = 14f;
        public float maxRockSize = 30f;
        [Tooltip("Ardışık kaya kümeleri arası ortalama mesafe")]
        public float rockSpacing = 22f;
        public Color rockColor = new Color(0.36f, 0.36f, 0.34f);
        public Color mossColor = new Color(0.26f, 0.33f, 0.21f);
    }

    /// <summary>
    /// Chunk'ın sağ ve sol kenarlarını kapatarak dünyanın sonunun görünmesini engeller.
    /// Her taraf için seed'e bağlı olarak uçurum, dev kayalar veya ikisi birden seçilir.
    /// Uçurum yüksekliği chunk birleşim noktalarında (seam) komşu chunk ile aynı değeri
    /// kullanır, böylece chunk'lar arasında basamak veya boşluk oluşmaz.
    /// </summary>
    [DisallowMultipleComponent]
    public class ChunkBoundaryGenerator : MonoBehaviour
    {
        public const string ROCKS_CONTAINER_NAME = "Boundary_Rocks";

        [Tooltip("Kapalıysa uçurum ve kaya üretilmez")]
        public bool boundaryEnabled = true;

        [Tooltip("Biyoma göre ayarlar. Chunk'ın biyomu listede yoksa ilk eleman kullanılır.")]
        public List<BiomeBoundarySettings> biomeSettings = new List<BiomeBoundarySettings>
        {
            new BiomeBoundarySettings(),
            // Coast: land side only (the sea side uses the coast profile). Lighter, weathered rocks.
            new BiomeBoundarySettings
            {
                biome = ChunkBiomeType.Coast,
                cliffWeight = 45,
                rocksWeight = 30,
                cliffAndRocksWeight = 25,
                rockColor = new Color(0.45f, 0.44f, 0.41f),
                mossColor = new Color(0.34f, 0.37f, 0.27f)
            }
        };

        [Header("Chunk Birleşim Noktaları (Seam)")]
        [Tooltip("Z=0 ve Z=500'de uçurumun başladığı sabit mesafe (komşu chunk'larla eşleşmesi için sabit)")]
        public float seamInnerEdge = 175f;
        public float seamRampWidth = 28f;
        [Range(0f, 1f)]
        [Tooltip("Bir birleşim noktasında uçurum olma ihtimali (0 ise orada kayalar boşluğu doldurur)")]
        public float seamCliffChance = 0.6f;
        public float seamMinHeight = 30f;
        public float seamMaxHeight = 55f;

        [Header("Güvenlik Mesafeleri")]
        [Tooltip("Yol merkezinden uçurum başlangıcına minimum mesafe")]
        public float roadClearance = 70f;
        [Tooltip("Kamp/mağara/fener gibi POI'lerden uçurum başlangıcına minimum mesafe")]
        public float poiClearance = 55f;
        [Tooltip("Uçurum bu yükseklikten alçaksa (stil Cliff olsa bile) boşluk kalmasın diye kaya konur")]
        public float rockFillHeight = 18f;

        [Header("Materyal (boşsa renkten otomatik üretilir)")]
        public Material rockMaterialOverride;
        public Material mossMaterialOverride;

        [Header("Deniz Kıyısı (Coast biyomu, deniz tarafı)")]
        [Tooltip("Kıyı olmayan komşu chunk'a geçişte karadan denize dönüşüm (burun) uzunluğu")]
        public float coastTransitionLength = 200f;
        [Tooltip("Yol merkezi ile kıyı çizgisi arası minimum mesafe")]
        public float shoreRoadGap = 18f;
        [Range(0f, 1f)]
        [Tooltip("Chunk ortasında yolun deniz kenarından gitme ihtimali (kalanında yol karanın ortasından gider)")]
        public float seasideRoadChance = 0.5f;
        [Tooltip("Yol deniz kenarındayken kıyı çizgisinin yol merkezine uzaklığı")]
        public float shoreNearMin = 22f;
        public float shoreNearMax = 40f;
        [Tooltip("Yol ortadan giderken kıyı çizgisinin chunk merkezine uzaklığı")]
        public float shoreFarMin = 120f;
        public float shoreFarMax = 210f;
        [Tooltip("Karadan kıyıya iniş genişliği. Küçük = dik kayalık kıyı, büyük = yumuşak kumsal")]
        public float minShoreSlope = 10f;
        public float maxShoreSlope = 70f;
        [Tooltip("Kıyı çizgisinden deniz tabanına (Y=0) iniş mesafesi")]
        public float seabedDropDistance = 55f;
        [Tooltip("Kıyı inişi bundan dar ise kayalık kıyı sayılır ve kıyıya kayalar dizilir")]
        public float rockyShoreSlope = 28f;
        public Color sandColor = new Color(0.80f, 0.72f, 0.52f);

        [Header("Deniz Kenarı Yol Uçurumu")]
        [Tooltip("Kıyı çizgisi yola bu mesafeden yakınsa (yol merkezinden) yolun deniz tarafı tam uçurum olur")]
        public float seaCliffFullGap = 55f;
        [Tooltip("Kıyı çizgisi bu mesafeden uzaksa uçurum yok, normal kıyı inişi")]
        public float seaCliffNoneGap = 90f;
        [Tooltip("Yol kenarı ile uçurum ağzı arasındaki dar omuz")]
        public float seaCliffShoulder = 1.0f;
        [Tooltip("Uçurum ağzından su seviyesine iniş genişliği (küçük = daha dik)")]
        public float seaCliffDrop = 6f;

        private class SideProfile
        {
            public ChunkBoundaryStyle style;
            public float[] inner;
            public float[] ramp;
            public float[] height;
            public float[] fade;
            public float noiseOffset;

            // Coast (only on the sea side of a Coast chunk)
            public bool isSea;
            public bool rockyShore;
            public float[] coast;       // 0 = normal boundary, 1 = full coast
            public float[] shore;       // shoreline distance from chunk center line
            public float[] shoreSlope;  // width of the land -> shore descent
            public float[] seaCliff;    // 0..1: road runs right along the sea -> its sea side is a cliff
        }

        [Header("Dağ (Mountain biyomu)")]
        [Tooltip("Dağ olmayan komşu chunk'tan dağlara geçiş mesafesi (dağlar ve kar bu mesafede başlar)")]
        public float mountainTransitionLength = 220f;
        [Tooltip("İki dağ chunk'ının birleştiği noktadaki dağ yüksekliği aralığı (baseline 20m üstü)")]
        public float mountainSeamMinHeight = 35f;
        public float mountainSeamMaxHeight = 55f;
        [Tooltip("Chunk ortasındaki dağ duvarı yüksekliği (baseline üstü). Terrain en fazla 100m olduğu için ~75 üstü düzleşir.")]
        public float mountainWallMinHeight = 45f;
        public float mountainWallMaxHeight = 72f;
        [Tooltip("Geçitte dağın yükselmeye başladığı yol merkezine uzaklık")]
        public float mountainWallMinFoot = 14f;
        public float mountainWallMaxFoot = 35f;
        [Tooltip("Dağ eteğinden zirveye yatay mesafe (küçük = daha dik)")]
        public float mountainWallMinRamp = 55f;
        public float mountainWallMaxRamp = 110f;
        [Tooltip("Dağ yamacı yerleşiminde vadi tarafının yol kotundan ne kadar aşağı indiği")]
        public float hillsideMinDrop = 10f;
        public float hillsideMaxDrop = 14f;
        [Tooltip("Dağ yamacı yerleşiminde vadinin karşı yamacının başladığı mesafe ve yüksekliği")]
        public float hillsideFarStartMin = 140f;
        public float hillsideFarStartMax = 190f;
        public float hillsideFarRise = 30f;

        [Header("Dağ: Kenardan Giden Yol (Ledge) - uçurum, kanyon, karşı dağ")]
        [Tooltip("Yolun dibindeki dağın yolun hemen kenarından yükselmeye başladığı mesafe")]
        public float ledgeWallMinFoot = 12f;
        public float ledgeWallMaxFoot = 20f;
        [Tooltip("Yol kenarı ile uçurum ağzı arasındaki dar omuz")]
        public float ledgeCliffShoulder = 1.2f;
        [Tooltip("Uçurum ağzından kanyon tabanına yatay iniş (küçük = daha dik)")]
        public float ledgeCliffDrop = 6f;
        [Tooltip("Kanyon tabanının yol kotunun altındaki derinliği (zemin 3m'nin altına inmez)")]
        public float canyonDepth = 60f;
        [Tooltip("Kanyonun karşısındaki dağın başladığı yer: chunk merkez çizgisinden, yolun karşı tarafında")]
        public float ledgeFarWallMinX = 110f;
        public float ledgeFarWallMaxX = 160f;
        [Tooltip("Karşı dağın yüksekliği (baseline üstü)")]
        public float ledgeFarWallMinHeight = 40f;
        public float ledgeFarWallMaxHeight = 65f;
        [Range(0f, 1f)]
        [Tooltip("Kanyon tabanından nehir akma ihtimali")]
        public float canyonRiverChance = 0.5f;
        public float canyonRiverHalfWidth = 8f;
        public float canyonRiverDepth = 2f;

        [Header("Dağ: Boyama")]
        [Tooltip("Bu eğimden (tan) dik yamaçlar kaya rengine boyanır (0.7 ≈ 35°)")]
        public float mountainRockSlope = 0.7f;
        [Tooltip("Alpin (karsız) dağlarda karın başladığı yükseklik (baseline üstü)")]
        public float alpineSnowLine = 45f;
        public Color snowGroundColor = new Color(0.92f, 0.94f, 0.97f);
        public Color mountainRockColor = new Color(0.42f, 0.42f, 0.44f);
        public Color canyonRiverColor = new Color(0.16f, 0.32f, 0.38f);

        /// <summary>
        /// Mountain shape of one side of the road, as the sum of two smooth steps of the distance d from the road:
        /// a1 * S((d - f1) / r1) + a2 * S((d - f2) / r2). Rising wall: a1 > 0. Valley / precipice side: a1 < 0
        /// (drop) and a2 > 0 (the mountain across). cliff/river: ledge precipice and canyon river strength.
        /// </summary>
        private class MountainSide
        {
            public float[] a1, f1, r1, a2, f2, r2, cliff, river;
            public float[] innerD; // distance from the road where the mountain mesh (ChunkBackdropGenerator) starts
            public float noiseOffset;
        }

        /// <summary>Shape parameters of one side at one point (a seam or the chunk middle).</summary>
        private struct SideShape
        {
            public float a1, f1, r1, a2, f2, r2, cliff, river;
        }

        private MountainSide[] _mtnSides;  // 0 = left of the road, 1 = right of the road
        private float[] _mtnWeight;        // 0..1 per row: 0 = normal boundary (non-mountain neighbor), 1 = full mountain
        private bool _hasLedgeCliff;
        private MountainVariant _mtnVariant;
        private float[] _roadY;          // road center height per heightmap row
        private float _baselineY = 20f;  // chunk.baseElevation, cached for per-sample use
        private List<Vector2> _poiKeepouts = new List<Vector2>(); // chunk-local (x, z) of POIs

        private float[] _roadX;          // road center X per heightmap row
        private float _roadHalfWidth = 9f;
        private int _seaIndex = -1;      // index into _sides of the sea side, -1 = no sea
        private float _seaSign;

        private SideProfile[] _sides; // 0 = sol (x<0), 1 = sağ (x>0)
        private int _rows;
        private float _length;
        private float _halfWidth;
        private readonly List<Vector3> _rockFootprints = new List<Vector3>(); // x, z, radius (chunk local)
        private Mesh _rockMesh;
        private readonly List<Mesh> _riverMeshes = new List<Mesh>();

        private static readonly Dictionary<Color, Material> _materialCache = new Dictionary<Color, Material>();

        private bool _hasPreviewSeed;
        private int _previewSeed;

        /// <summary>
        /// Edit mode: seed used when the chunk was not initialized by ChunkManager,
        /// so terrain cliffs and rocks follow Chunk.editorPreviewSeed.
        /// </summary>
        public void SetPreviewSeed(int seed)
        {
            _previewSeed = seed;
            _hasPreviewSeed = true;
        }

        public bool HasPlan => _sides != null;
        public ChunkBoundaryStyle LeftStyle => _sides != null ? _sides[0].style : ChunkBoundaryStyle.Cliff;
        public ChunkBoundaryStyle RightStyle => _sides != null ? _sides[1].style : ChunkBoundaryStyle.Cliff;

        // ---------------------------------------------------------------
        // Plan
        // ---------------------------------------------------------------

        /// <summary>
        /// Her iki taraf için uçurum profilini hesaplar. rows = terrain heightmap satır sayısı.
        /// </summary>
        public bool BuildPlan(RoadSpline spline, int rows, float length, float halfWidth, float roadHalfWidth = 9f)
        {
            _sides = null;
            _seaIndex = -1;
            _mtnSides = null;
            _hasLedgeCliff = false;
            if (!boundaryEnabled || rows < 2) return false;

            Chunk chunk = GetChunk();
            BiomeBoundarySettings settings = GetSettings(chunk);
            if (settings == null) return false;

            _rows = rows;
            _length = length;
            _halfWidth = halfWidth;
            _roadHalfWidth = roadHalfWidth;

            int planSeed = GetPlanSeed(chunk);
            int worldSeed = chunk != null && chunk.Manager != null ? chunk.Manager.ResolvedSeed : planSeed;
            int startSeam = chunk != null ? chunk.ChunkIndex : 0;

            var rng = new SeededRandom(SeededRandom.Combine(planSeed, "boundary"));
            List<Vector2> keepouts = CollectPoiKeepouts(chunk);
            _poiKeepouts = keepouts;

            _baselineY = chunk != null ? chunk.baseElevation : 20f;
            float[] roadX = new float[rows];
            _roadY = new float[rows];
            for (int r = 0; r < rows; r++)
            {
                float z = r / (rows - 1f) * length;
                Vector3 p = spline != null ? spline.GetPointAtZ(z) : new Vector3(0f, chunk != null ? chunk.baseElevation : 20f, z);
                roadX[r] = p.x;
                _roadY[r] = p.y;
            }
            _roadX = roadX;

            _sides = new SideProfile[2];
            for (int s = 0; s < 2; s++)
            {
                float sign = s == 0 ? -1f : 1f;
                var side = new SideProfile
                {
                    style = PickStyle(rng, settings),
                    inner = new float[rows],
                    ramp = new float[rows],
                    height = new float[rows],
                    fade = new float[rows],
                    noiseOffset = rng.Range(0f, 1000f)
                };

                float peak = rng.Range(settings.minCliffHeight, settings.maxCliffHeight);
                float innerTarget = rng.Range(settings.minInnerEdge, settings.maxInnerEdge);
                float rampTarget = rng.Range(settings.minRampWidth, settings.maxRampWidth);
                // Any seam touching a Field chunk is flat, so cliffs fade out toward open plains.
                // Both chunks at a seam see the same biomes, so they agree on the value.
                bool isField = chunk != null && chunk.IsField;
                bool prevField = chunk != null && chunk.PrevBiome == ChunkBiomeType.Field;
                bool nextField = chunk != null && chunk.NextBiome == ChunkBiomeType.Field;
                float seamA = SeamHeight(worldSeed, startSeam, s);
                float seamB = SeamHeight(worldSeed, startSeam + 1, s);
                if (isField || prevField) seamA = 0f;
                if (isField || nextField) seamB = 0f;

                for (int r = 0; r < rows; r++)
                {
                    float z = r / (rows - 1f) * length;
                    float t = z / length;
                    // w = 0 at both seams, 1 in the middle -> everything chunk-specific fades out at seams
                    float w = Mathf.Clamp01(Mathf.Sin(t * Mathf.PI));
                    float wSoft = Mathf.Sqrt(w);

                    float seamH = Mathf.Lerp(seamA, seamB, Mathf.SmoothStep(0f, 1f, t));
                    float h;
                    switch (side.style)
                    {
                        case ChunkBoundaryStyle.Rocks:
                            h = seamH * (1f - wSoft);
                            break;
                        case ChunkBoundaryStyle.CliffAndRocks:
                            h = Mathf.Lerp(seamH, peak * 0.7f, wSoft);
                            break;
                        default:
                            h = Mathf.Lerp(seamH, peak, wSoft);
                            break;
                    }
                    h *= 1f + (Mathf.PerlinNoise(side.noiseOffset, z * 0.012f) - 0.5f) * 0.35f * w;
                    if (isField) h = 0f; // open plains: edges come from FieldEdgeGenerator, not cliffs

                    float ramp = Mathf.Lerp(seamRampWidth, rampTarget, wSoft);
                    float edgeNoise = (Mathf.PerlinNoise(side.noiseOffset + 50f, z * 0.015f) - 0.5f) * 2f * 12f * w;
                    float baseInner = Mathf.Lerp(seamInnerEdge, innerTarget, wSoft) + edgeNoise;

                    float inner = Mathf.Max(baseInner, roadX[r] * sign + roadClearance);
                    for (int k = 0; k < keepouts.Count; k++)
                    {
                        float poiDist = keepouts[k].x * sign;
                        if (poiDist < 10f) continue; // POI is on the other side of the road
                        float dz = (z - keepouts[k].y) / 70f;
                        float f = Mathf.Exp(-dz * dz) * w;
                        inner = Mathf.Max(inner, Mathf.Lerp(baseInner, poiDist + poiClearance, f));
                    }
                    inner = Mathf.Min(inner, halfWidth - ramp - 8f);

                    side.inner[r] = inner;
                    side.ramp[r] = ramp;
                    side.height[r] = Mathf.Max(0f, h);
                    side.fade[r] = w;
                }

                _sides[s] = side;
            }

            // Coast: the sea side gets a shoreline profile on top of (and blending away from) the normal boundary.
            // Uses its own random stream so the land side stays identical to a forest chunk with the same seed.
            if (chunk != null && chunk.IsCoast)
            {
                _seaSign = chunk.SeaSign;
                _seaIndex = _seaSign < 0f ? 0 : 1;
                BuildCoastProfile(chunk, _sides[_seaIndex], _seaSign, roadX, planSeed, worldSeed, startSeam);
            }

            if (chunk != null && chunk.IsMountain)
            {
                BuildMountainProfile(chunk, planSeed, worldSeed, startSeam);
            }

            return true;
        }

        // ---------------------------------------------------------------
        // Mountains
        // ---------------------------------------------------------------

        /// <summary>
        /// Builds both sides' mountain shape per row. Seam shapes come only from the world seed, seam index and the
        /// road's lateral offset at that seam (Chunk.roadEntryX / roadExitX), so both chunks at a seam agree.
        /// A seam where the road runs off-center is a ledge seam: mountain right at the road on the outer side,
        /// precipice + canyon + far mountain toward the other side.
        /// </summary>
        private void BuildMountainProfile(Chunk chunk, int planSeed, int worldSeed, int startSeam)
        {
            var rng = new SeededRandom(SeededRandom.Combine(planSeed, "mountain"));
            _mtnVariant = chunk.mountainVariant;
            bool prevMtn = chunk.PrevBiome == ChunkBiomeType.Mountain;
            bool nextMtn = chunk.NextBiome == ChunkBiomeType.Mountain;

            _mtnWeight = new float[_rows];
            for (int r = 0; r < _rows; r++)
            {
                float z = r / (_rows - 1f) * _length;
                // Fades in from a non-mountain neighbor (its boundary cliffs/rocks meet the seam unchanged)
                _mtnWeight[r] = Mathf.Min(
                    prevMtn ? 1f : Mathf.SmoothStep(0f, 1f, z / mountainTransitionLength),
                    nextMtn ? 1f : Mathf.SmoothStep(0f, 1f, (_length - z) / mountainTransitionLength));
            }

            MountainLayout layout = chunk.mountainLayout;
            int uphill = chunk.uphillOnRight ? 1 : 0;
            float entryX = chunk.roadEntryX, exitX = chunk.roadExitX;

            _mtnSides = new MountainSide[2];
            for (int s = 0; s < 2; s++)
            {
                // Middle of the chunk: this chunk's own shape
                bool precipice = layout == MountainLayout.Ledge && s != uphill;
                float midFarX = (s == 0 ? -1f : 1f) * rng.Range(ledgeFarWallMinX, ledgeFarWallMaxX);
                SideShape mid = ChunkShape(rng, layout, s == uphill, precipice);
                float midRiver = precipice && rng.Chance(canyonRiverChance) ? 1f : 0f;

                // Seams: shared with the neighbors
                SideShape seamA = SeamShape(worldSeed, startSeam, s, entryX, out float farA);
                SideShape seamB = SeamShape(worldSeed, startSeam + 1, s, exitX, out float farB);
                seamA.f2 = farA > 0f ? Mathf.Abs(farA * (s == 0 ? -1f : 1f) - entryX) : 400f;
                seamB.f2 = farB > 0f ? Mathf.Abs(farB * (s == 0 ? -1f : 1f) - exitX) : 400f;

                var side = new MountainSide
                {
                    a1 = new float[_rows], f1 = new float[_rows], r1 = new float[_rows],
                    a2 = new float[_rows], f2 = new float[_rows], r2 = new float[_rows],
                    cliff = new float[_rows], river = new float[_rows], innerD = new float[_rows],
                    noiseOffset = rng.Range(0f, 1000f)
                };

                for (int r = 0; r < _rows; r++)
                {
                    float t = r / (_rows - 1f);
                    float wSoft = Mathf.Sqrt(Mathf.Clamp01(Mathf.Sin(t * Mathf.PI)));
                    float st = Mathf.SmoothStep(0f, 1f, t);

                    // The far wall stays at a fixed position across the chunk while the road moves under it
                    float midF2 = precipice ? Mathf.Abs(midFarX - _roadX[r]) : mid.f2;

                    side.a1[r] = Mathf.Lerp(Mathf.Lerp(seamA.a1, seamB.a1, st), mid.a1, wSoft);
                    side.f1[r] = Mathf.Lerp(Mathf.Lerp(seamA.f1, seamB.f1, st), mid.f1, wSoft);
                    side.r1[r] = Mathf.Lerp(Mathf.Lerp(seamA.r1, seamB.r1, st), mid.r1, wSoft);
                    side.a2[r] = Mathf.Lerp(Mathf.Lerp(seamA.a2, seamB.a2, st), mid.a2, wSoft);
                    side.f2[r] = Mathf.Lerp(Mathf.Lerp(seamA.f2, seamB.f2, st), midF2, wSoft);
                    side.r2[r] = Mathf.Lerp(Mathf.Lerp(seamA.r2, seamB.r2, st), mid.r2, wSoft);
                    side.cliff[r] = Mathf.Lerp(Mathf.Lerp(seamA.cliff, seamB.cliff, st), mid.cliff, wSoft) * _mtnWeight[r];
                    side.river[r] = Mathf.Lerp(Mathf.Lerp(seamA.river, seamB.river, st), midRiver, wSoft) * _mtnWeight[r];
                    if (side.cliff[r] > 0.01f) _hasLedgeCliff = true;

                    // Where the mountain mesh starts: the wall foot (rising side) or the far mountain across the
                    // valley/canyon (dropping side). Blends continuously while a1 passes through zero.
                    float wallness = Mathf.Clamp01(side.a1[r] / 10f);
                    float innerD = Mathf.Lerp(side.f2[r], side.f1[r], wallness);

                    // Camps/caves/lighthouses on this side: the mountain steps back to leave a clearing
                    float z = t * _length;
                    float fade = Mathf.Clamp01(Mathf.Sin(t * Mathf.PI));
                    float sideSign = s == 0 ? -1f : 1f;
                    for (int k = 0; k < _poiKeepouts.Count; k++)
                    {
                        float poiD = (_poiKeepouts[k].x - _roadX[r]) * sideSign;
                        if (poiD < 5f) continue; // other side of the road
                        float dz = (z - _poiKeepouts[k].y) / 60f;
                        float f = Mathf.Exp(-dz * dz) * fade;
                        innerD = Mathf.Lerp(innerD, Mathf.Max(innerD, poiD + 35f), f);
                    }
                    side.innerD[r] = innerD;
                }
                _mtnSides[s] = side;
            }
        }

        /// <summary>This chunk's own (middle) shape for one side.</summary>
        private SideShape ChunkShape(SeededRandom rng, MountainLayout layout, bool isUphill, bool precipice)
        {
            var shape = new SideShape { f2 = 400f, r2 = 50f };
            if (precipice)
            {
                PrecipiceShape(ref shape, rng.Range(ledgeFarWallMinHeight, ledgeFarWallMaxHeight), rng.Range(30f, 55f));
            }
            else if (layout == MountainLayout.Hillside && !isUphill)
            {
                // Valley side: drops below the road, then the far slope rises across the valley
                shape.a1 = -rng.Range(hillsideMinDrop, hillsideMaxDrop);
                shape.f1 = rng.Range(14f, 24f);
                shape.r1 = rng.Range(40f, 70f);
                shape.a2 = -shape.a1 + hillsideFarRise * rng.Range(0.7f, 1.3f);
                shape.f2 = rng.Range(hillsideFarStartMin, hillsideFarStartMax);
                shape.r2 = rng.Range(50f, 80f);
            }
            else
            {
                shape.a1 = rng.Range(mountainWallMinHeight, mountainWallMaxHeight);
                // Hillside and ledge: the mountain starts right at the road edge
                shape.f1 = layout == MountainLayout.Valley
                    ? rng.Range(mountainWallMinFoot, mountainWallMaxFoot)
                    : rng.Range(ledgeWallMinFoot, ledgeWallMaxFoot);
                shape.r1 = rng.Range(mountainWallMinRamp, mountainWallMaxRamp);
            }
            return shape;
        }

        /// <summary>Precipice side of a ledge road: down to the canyon floor, mountain across (f2 set by the caller).</summary>
        private void PrecipiceShape(ref SideShape shape, float farWallHeight, float farRamp)
        {
            float drop = canyonDepth; // relative to the road level
            shape.a1 = -drop;
            shape.f1 = 10f;
            shape.r1 = 18f;
            shape.a2 = drop + farWallHeight;
            shape.r2 = farRamp;
            shape.cliff = 1f;
        }

        /// <summary>
        /// Shape at seam i for one side (relative to the road). farX: distance of the far wall from the chunk
        /// center line (0 = none). Depends only on world seed, seam index, side and the road offset at the seam.
        /// </summary>
        private SideShape SeamShape(int worldSeed, int seamIndex, int side, float roadOffset, out float farX)
        {
            int seed = SeededRandom.Combine(SeededRandom.Combine(SeededRandom.Combine(worldSeed, "mtn_seam"), seamIndex), side);
            var rng = new SeededRandom(seed);
            var shape = new SideShape { a1 = rng.Range(mountainSeamMinHeight, mountainSeamMaxHeight), f1 = 30f, r1 = 90f, f2 = 400f, r2 = 50f };
            farX = 0f;

            if (Mathf.Abs(roadOffset) > 1f)
            {
                // Ledge seam: the road runs along the mountain on the side it is offset to
                int uphill = roadOffset > 0f ? 1 : 0;
                if (side == uphill)
                {
                    shape.f1 = rng.Range(ledgeWallMinFoot, ledgeWallMaxFoot);
                }
                else
                {
                    PrecipiceShape(ref shape, rng.Range(ledgeFarWallMinHeight, ledgeFarWallMaxHeight), 40f);
                    farX = rng.Range(ledgeFarWallMinX, ledgeFarWallMaxX);
                    // River: decided per seam so it continues into the neighbor
                    var riverRng = new SeededRandom(SeededRandom.Combine(SeededRandom.Combine(worldSeed, "mtn_river"), seamIndex));
                    shape.river = riverRng.Chance(canyonRiverChance) ? 1f : 0f;
                }
            }
            return shape;
        }

        private static float Step01(float x)
        {
            x = Mathf.Clamp01(x);
            return x * x * (3f - 2f * x);
        }

        /// <summary>0 near a POI (flat clearing so camps/caves are not buried or dropped into the canyon), 1 elsewhere.</summary>
        private float PoiKeepFactor(float localX, float localZ)
        {
            float keep = 1f;
            for (int k = 0; k < _poiKeepouts.Count; k++)
            {
                float dx = localX - _poiKeepouts[k].x;
                float dz = localZ - _poiKeepouts[k].y;
                keep *= Step01((Mathf.Sqrt(dx * dx + dz * dz) - 25f) / 40f);
            }
            return keep;
        }

        /// <summary>
        /// Mountain terrain height (meters) at a chunk-local X for a heightmap row. The mountains themselves are
        /// low-poly meshes (ChunkBackdropGenerator, starting at <see cref="GetMountainInnerX"/>); the terrain only
        /// does what goes down: the hillside valley and (via MountainCliff) the ledge canyon. It stays near road
        /// level elsewhere so it never pokes through the mountain meshes.
        /// </summary>
        private float MountainHeight(float localX, int row, float naturalY)
        {
            float fromRoad = localX - _roadX[row];
            MountainSide side = _mtnSides[fromRoad >= 0f ? 1 : 0];
            float d = Mathf.Abs(fromRoad);
            float z = row / (_rows - 1f) * _length;

            // Only the dropping part of the side shape (valley / canyon floor)
            float drop = 0f;
            if (side.a1[row] < 0f)
            {
                drop = side.a1[row] * Step01((d - side.f1[row]) / Mathf.Max(1f, side.r1[row]));
                drop *= PoiKeepFactor(localX, z);
            }

            // The ground is at road level across the chunk (mountain roads run at 100m+), so the meshes stand on
            // it and the valley/canyon drop is measured from the road. Calmer ambient undulation keeps it under
            // the mountain meshes' foot.
            float ambient = (naturalY - _baselineY) * 0.35f;
            return _roadY[row] + ambient + drop;
        }

        /// <summary>Canyon floor height at a row: canyonDepth below the road, never below 3m.</summary>
        private float CanyonFloorY(int row)
        {
            return Mathf.Max(3f, _roadY[row] - canyonDepth);
        }

        /// <summary>
        /// Signed chunk-local X where the mountain mesh on one side of the road starts (sideSign -1 = left of
        /// the road, +1 = right). Pulled back to the terrain edge where the chunk fades to a non-mountain neighbor,
        /// so it meets that neighbor's ordinary backdrop strip. Returns false on non-mountain chunks.
        /// </summary>
        public bool TryGetMountainInnerX(float sideSign, float localZ, out float innerX)
        {
            innerX = sideSign * _halfWidth;
            if (_mtnSides == null) return false;

            float f = Mathf.Clamp01(localZ / _length) * (_rows - 1);
            int i0 = Mathf.FloorToInt(f);
            int i1 = Mathf.Min(i0 + 1, _rows - 1);
            float t = f - i0;

            MountainSide side = _mtnSides[sideSign < 0f ? 0 : 1];
            float roadX = Mathf.Lerp(_roadX[i0], _roadX[i1], t);
            float d = Mathf.Lerp(side.innerD[i0], side.innerD[i1], t);
            float w = Mathf.Lerp(_mtnWeight[i0], _mtnWeight[i1], t);

            // Never beyond the terrain edge on that side
            float x = roadX + sideSign * d;
            if (x * sideSign > _halfWidth) x = sideSign * _halfWidth;
            innerX = Mathf.Lerp(sideSign * _halfWidth, x, w);
            return true;
        }

        /// <summary>True if a chunk-local position is inside a mountain mesh (no loot / trees there).</summary>
        public bool IsInsideMountain(Vector3 chunkLocal)
        {
            if (_mtnSides == null) return false;
            int row = Mathf.Clamp(Mathf.RoundToInt(chunkLocal.z / _length * (_rows - 1)), 0, _rows - 1);
            float sideSign = chunkLocal.x - _roadX[row] >= 0f ? 1f : -1f;
            if (!TryGetMountainInnerX(sideSign, chunkLocal.z, out float innerX)) return false;
            // A couple of meters of margin: the mesh foot rises steeply from its first vertex
            return (chunkLocal.x - innerX) * sideSign > -2f;
        }

        /// <summary>
        /// Ledge roads: right after the road shoulder the terrain falls as a sheer cliff to the canyon floor, up to
        /// where the mountain across starts; an optional river runs in the middle of the canyon floor.
        /// Applied after the road blend (only lowers terrain).
        /// </summary>
        private float MountainCliff(float localX, int row, float height, float roadBedY)
        {
            float fromRoad = localX - _roadX[row];
            MountainSide side = _mtnSides[fromRoad >= 0f ? 1 : 0];
            float w = side.cliff[row];
            if (w <= 0f) return height;

            float lip = _roadHalfWidth + ledgeCliffShoulder;
            float d = Mathf.Abs(fromRoad) - lip;
            if (d <= 0f) return height;
            if (d + lip >= side.f2[row]) return height; // the far mountain takes over

            float z = row / (_rows - 1f) * _length;
            float floorY = CanyonFloorY(row);

            // River in the middle of the canyon floor
            float riverW = side.river[row];
            if (riverW > 0f)
            {
                float center = RiverCenterDistance(side, row);
                float rd = Mathf.Abs(Mathf.Abs(fromRoad) - center);
                float half = canyonRiverHalfWidth * riverW;
                float bed = rd <= half ? 1f : 1f - Step01((rd - half) / 5f);
                floorY -= canyonRiverDepth * bed * riverW;
            }

            float cliffY = Mathf.Lerp(roadBedY, floorY, Mathf.Clamp01(d / Mathf.Max(0.5f, ledgeCliffDrop)));
            if (d > ledgeCliffDrop) cliffY = floorY;
            return Mathf.Lerp(height, Mathf.Min(height, cliffY), w * PoiKeepFactor(localX, z));
        }

        /// <summary>Distance of the canyon river's center line from the road (middle of the canyon floor).</summary>
        private float RiverCenterDistance(MountainSide side, int row)
        {
            float cliffFoot = _roadHalfWidth + ledgeCliffShoulder + ledgeCliffDrop;
            return (cliffFoot + side.f2[row]) * 0.5f;
        }

        /// <summary>0..1: how much of this Z belongs to the mountain shape (0 on non-mountain chunks).</summary>
        public float GetMountainWeight(float localZ)
        {
            if (_mtnSides == null) return 0f;
            float f = Mathf.Clamp01(localZ / _length) * (_rows - 1);
            int i0 = Mathf.FloorToInt(f);
            int i1 = Mathf.Min(i0 + 1, _rows - 1);
            return Mathf.Lerp(_mtnWeight[i0], _mtnWeight[i1], f - i0);
        }

        /// <summary>True inside a canyon river (keeps trees out of the water).</summary>
        public bool IsInCanyonRiver(Vector3 chunkLocal)
        {
            if (_mtnSides == null) return false;
            int row = Mathf.Clamp(Mathf.RoundToInt(chunkLocal.z / _length * (_rows - 1)), 0, _rows - 1);
            float fromRoad = chunkLocal.x - _roadX[row];
            MountainSide side = _mtnSides[fromRoad >= 0f ? 1 : 0];
            if (side.river[row] < 0.1f) return false;
            return Mathf.Abs(Mathf.Abs(fromRoad) - RiverCenterDistance(side, row)) < canyonRiverHalfWidth + 4f;
        }

        /// <summary>Water surface strips along the canyon rivers (chunk-local), built into the rocks container.</summary>
        private void SpawnCanyonRivers(Transform container)
        {
            if (_mtnSides == null) return;
            for (int s = 0; s < 2; s++)
            {
                MountainSide side = _mtnSides[s];
                float sign = s == 0 ? -1f : 1f;
                var verts = new List<Vector3>();
                var tris = new List<int>();
                int prev = -1;

                for (int r = 0; r < _rows; r++)
                {
                    float w = side.river[r] * side.cliff[r];
                    if (w < 0.05f)
                    {
                        prev = -1;
                        continue;
                    }

                    float z = r / (_rows - 1f) * _length;
                    float center = _roadX[r] + sign * RiverCenterDistance(side, r);
                    float half = (canyonRiverHalfWidth + 1f) * w;
                    float y = CanyonFloorY(r) - 0.6f;

                    int i = verts.Count;
                    verts.Add(new Vector3(center - half, y, z));
                    verts.Add(new Vector3(center + half, y, z));
                    if (prev >= 0)
                    {
                        // a = prev left, b = cur left, c = cur right, d = prev right -> faces up
                        tris.Add(prev); tris.Add(i); tris.Add(i + 1);
                        tris.Add(prev); tris.Add(i + 1); tris.Add(prev + 1);
                    }
                    prev = i;
                }

                if (tris.Count == 0) continue;

                var mesh = new Mesh { name = $"CanyonRiver_{s}" };
                mesh.SetVertices(verts);
                mesh.SetTriangles(tris, 0);
                mesh.RecalculateNormals();
                mesh.RecalculateBounds();
                _riverMeshes.Add(mesh);

                var go = new GameObject("CanyonRiver");
                go.transform.SetParent(container, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = GetSharedMaterial(canyonRiverColor);
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
        }

        private void BuildCoastProfile(Chunk chunk, SideProfile side, float sign, float[] roadX, int planSeed, int worldSeed, int startSeam)
        {
            int sideIndex = sign < 0f ? 0 : 1;
            var rng = new SeededRandom(SeededRandom.Combine(planSeed, "coast"));

            bool seasideRoad = rng.Chance(seasideRoadChance);
            float shoreMid = seasideRoad ? rng.Range(shoreNearMin, shoreNearMax) : rng.Range(shoreFarMin, shoreFarMax);
            float slopeMid = rng.Range(minShoreSlope, maxShoreSlope);
            float noiseOffset = rng.Range(0f, 1000f);

            SeamShore(worldSeed, startSeam, sideIndex, out float shoreA, out float slopeA);
            SeamShore(worldSeed, startSeam + 1, sideIndex, out float shoreB, out float slopeB);

            side.isSea = true;
            side.rockyShore = slopeMid < rockyShoreSlope;
            side.coast = new float[_rows];
            side.shore = new float[_rows];
            side.shoreSlope = new float[_rows];
            side.seaCliff = new float[_rows];

            for (int r = 0; r < _rows; r++)
            {
                float z = r / (_rows - 1f) * _length;
                float t = z / _length;
                float w = Mathf.Clamp01(Mathf.Sin(t * Mathf.PI));
                float wSoft = Mathf.Sqrt(w);

                // Coast fades in/out where the neighbor chunk is not a coast on the same side (headland)
                float cStart = chunk.coastContinuesAtStart ? 1f : Mathf.SmoothStep(0f, 1f, z / coastTransitionLength);
                float cEnd = chunk.coastContinuesAtEnd ? 1f : Mathf.SmoothStep(0f, 1f, (_length - z) / coastTransitionLength);
                side.coast[r] = Mathf.Min(cStart, cEnd);

                float seamShore = Mathf.Lerp(shoreA, shoreB, Mathf.SmoothStep(0f, 1f, t));
                float shore = Mathf.Lerp(seamShore, shoreMid, wSoft)
                              + (Mathf.PerlinNoise(noiseOffset, z * 0.01f) - 0.5f) * 2f * 15f * w;
                shore = Mathf.Max(shore, roadX[r] * sign + shoreRoadGap);
                side.shore[r] = Mathf.Min(shore, _halfWidth - 30f);

                // Shoreline close to the road -> the road's sea side becomes a cliff straight into the water
                float gap = side.shore[r] - roadX[r] * sign;
                side.seaCliff[r] = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(seaCliffFullGap, seaCliffNoneGap, gap));

                side.shoreSlope[r] = Mathf.Lerp(Mathf.Lerp(slopeA, slopeB, t), slopeMid, wSoft);
            }
        }

        /// <summary>
        /// Shoreline values at seam i, shared by both coast chunks that meet there.
        /// </summary>
        private void SeamShore(int worldSeed, int seamIndex, int side, out float shore, out float slope)
        {
            int seed = SeededRandom.Combine(SeededRandom.Combine(SeededRandom.Combine(worldSeed, "coast_seam"), seamIndex), side);
            var rng = new SeededRandom(seed);
            shore = rng.Chance(0.4f) ? rng.Range(shoreNearMin, shoreNearMax) : rng.Range(shoreFarMin, shoreFarMax);
            slope = rng.Range(minShoreSlope, maxShoreSlope);
        }

        /// <summary>
        /// Final terrain height (meters) for a heightmap sample: natural landscape + boundary cliff,
        /// replaced by the coast profile on the sea side of a Coast chunk.
        /// </summary>
        public float ApplyBoundary(float localX, int row, float naturalY)
        {
            if (_sides == null) return naturalY;
            row = Mathf.Clamp(row, 0, _rows - 1);
            SideProfile side = _sides[localX < 0f ? 0 : 1];
            float absX = Mathf.Abs(localX);

            float y = naturalY + CliffOffset(side, absX, row);

            if (_seaIndex >= 0)
            {
                // Signed distance toward the sea, evaluated across the whole row (also on the land half when the
                // shoreline comes close to the center line), so the profile has no step at X = 0
                SideProfile sea = _sides[_seaIndex];
                float c = sea.coast[row];
                float xs = localX * _seaSign;
                if (c > 0f && xs - sea.shore[row] > -sea.shoreSlope[row])
                {
                    y = Mathf.Lerp(y, CoastHeight(sea, xs, row, naturalY), c);
                }
            }

            // Mountain: walls beside the road (pass), a valley (hillside) or precipice + canyon + far mountain (ledge)
            if (_mtnSides != null && _mtnWeight[row] > 0f)
            {
                y = Mathf.Lerp(y, MountainHeight(localX, row, naturalY), _mtnWeight[row]);
            }
            return y;
        }

        /// <summary>
        /// Applied after the road bed has been blended in: where the road runs right along the sea,
        /// the terrain drops as a cliff straight from the road shoulder into the water (overrides the
        /// adapter's wide road blend on the sea side). Only ever lowers the terrain.
        /// </summary>
        public float ApplyAfterRoad(float localX, int row, float height, float roadBedY)
        {
            if (_sides == null) return height;
            row = Mathf.Clamp(row, 0, _rows - 1);

            // Mountain ledge road: sheer drop into the canyon on the precipice side
            if (_hasLedgeCliff) return MountainCliff(localX, row, height, roadBedY);

            if (_seaIndex < 0) return height;
            SideProfile sea = _sides[_seaIndex];

            float w = sea.seaCliff[row] * sea.coast[row];
            if (w <= 0f) return height;

            // Distance seaward from the cliff lip (road edge + shoulder)
            float d = (localX - _roadX[row]) * _seaSign - (_roadHalfWidth + seaCliffShoulder);
            if (d <= 0f) return height;

            float waterline = WorldConstants.SeaLevel - 1.5f;
            float y;
            if (d < seaCliffDrop)
            {
                y = Mathf.Lerp(roadBedY, waterline, d / Mathf.Max(0.5f, seaCliffDrop));
            }
            else
            {
                float u = Mathf.Clamp01((d - seaCliffDrop) / Mathf.Max(1f, seabedDropDistance));
                y = Mathf.Lerp(waterline, 0f, Mathf.SmoothStep(0f, 1f, u));
            }

            return Mathf.Lerp(height, Mathf.Min(height, y), w);
        }

        /// <summary>
        /// 0..1: how strongly the road runs directly along a sea cliff at this Z (used to thin out vegetation).
        /// </summary>
        public float GetSeasideRoadWeight(float localZ)
        {
            if (_sides == null || _seaIndex < 0) return 0f;
            SideProfile sea = _sides[_seaIndex];
            float f = Mathf.Clamp01(localZ / _length) * (_rows - 1);
            int i0 = Mathf.FloorToInt(f);
            int i1 = Mathf.Min(i0 + 1, _rows - 1);
            float t = f - i0;
            return Mathf.Lerp(sea.seaCliff[i0] * sea.coast[i0], sea.seaCliff[i1] * sea.coast[i1], t);
        }

        private float CoastHeight(SideProfile side, float seawardX, int row, float naturalY)
        {
            float d = seawardX - side.shore[row]; // > 0 = seaward of the shoreline
            float slope = Mathf.Max(1f, side.shoreSlope[row]);
            float shoreTop = WorldConstants.SeaLevel + 0.5f;

            if (d <= -slope) return naturalY;
            if (d <= 0f)
            {
                float t = (d + slope) / slope;
                return Mathf.Lerp(naturalY, shoreTop, Mathf.SmoothStep(0f, 1f, t));
            }

            float u = Mathf.Clamp01(d / Mathf.Max(1f, seabedDropDistance));
            return Mathf.Lerp(shoreTop, 0f, Mathf.SmoothStep(0f, 1f, u));
        }

        /// <summary>
        /// 0..1: how much of the given chunk-local position belongs to the sea (0 on land sides / non-coast chunks).
        /// </summary>
        public float GetSeaWeight(float localX, float localZ)
        {
            if (_sides == null) return 0f;
            SideProfile side = _sides[localX < 0f ? 0 : 1];
            if (!side.isSea) return 0f;
            float f = Mathf.Clamp01(localZ / _length) * (_rows - 1);
            int i0 = Mathf.FloorToInt(f);
            int i1 = Mathf.Min(i0 + 1, _rows - 1);
            return Mathf.Lerp(side.coast[i0], side.coast[i1], f - i0);
        }

        private float CliffOffset(SideProfile side, float absX, int row)
        {
            float d = absX - side.inner[row];
            if (d <= 0f) return 0f;

            float u = Mathf.Clamp01(d / Mathf.Max(1f, side.ramp[row]));
            float shape = u * u * (3f - 2f * u);
            float z = row / (_rows - 1f) * _length;
            float rough = (Mathf.PerlinNoise(side.noiseOffset + 100f + d * 0.04f, z * 0.023f) - 0.5f) * 2f * 4f;

            return Mathf.Max(0f, side.height[row] * shape + rough * shape * side.fade[row]);
        }

        /// <summary>
        /// Ağaç/kaya bitki örtüsünün uçurum yüzüne veya sınır kayalarının içine düşmemesi için.
        /// </summary>
        public bool IsBlockedForVegetation(Vector3 chunkLocal)
        {
            if (_sides == null) return false;
            if (IsInCanyonRiver(chunkLocal) || IsInsideMountain(chunkLocal)) return true;

            SampleSide(_sides[chunkLocal.x < 0f ? 0 : 1], chunkLocal.z, out float inner, out float ramp, out float h);
            float d = Mathf.Abs(chunkLocal.x) - inner;
            if (h > 4f && d > -4f && d < ramp + 4f) return true;

            for (int i = 0; i < _rockFootprints.Count; i++)
            {
                Vector3 fp = _rockFootprints[i];
                float dx = chunkLocal.x - fp.x;
                float dz = chunkLocal.z - fp.y;
                if (dx * dx + dz * dz < fp.z * fp.z) return true;
            }
            return false;
        }

        // ---------------------------------------------------------------
        // Rocks
        // ---------------------------------------------------------------

        /// <summary>
        /// Terrain şekillendikten sonra çağrılır. Kenarlara dev küp kaya kümeleri dizer.
        /// </summary>
        public void SpawnBoundaryRocks(Transform parent, Terrain terrain)
        {
            ClearRocks(parent);
            DestroyRockMesh();
            _rockFootprints.Clear();
            if (!boundaryEnabled || parent == null) return;

            Chunk chunk = GetChunk();
            if (chunk == null || chunk.IsField) return; // fields have fences/trees/corn/streams instead

            if (_sides == null)
            {
                int rows = terrain != null && terrain.terrainData != null ? terrain.terrainData.heightmapResolution : 129;
                float halfWidth = terrain != null && terrain.terrainData != null ? terrain.terrainData.size.x * 0.5f : 250f;
                var roadGen = chunk.GetComponentInChildren<RoadGenerator>();
                BuildPlan(chunk.GetComponentInChildren<RoadSpline>(), rows, chunk.chunkLength, halfWidth,
                    roadGen != null ? roadGen.GetTotalHalfWidth() : 9f);
                if (_sides == null) return;
            }

            BiomeBoundarySettings settings = GetSettings(chunk);
            var rng = new SeededRandom(SeededRandom.Combine(GetPlanSeed(chunk), "boundary_rocks"));

            // Container sits on the chunk origin so the combined rock mesh can be built in chunk-local space
            Transform container = new GameObject(ROCKS_CONTAINER_NAME).transform;
            container.SetPositionAndRotation(chunk.transform.position, chunk.transform.rotation);
            container.SetParent(parent, true);

            Material rockMat = rockMaterialOverride != null ? rockMaterialOverride : GetSharedMaterial(settings.rockColor);
            Material mossMat = mossMaterialOverride != null ? mossMaterialOverride : GetSharedMaterial(settings.mossColor);
            float avgSize = (settings.minRockSize + settings.maxRockSize) * 0.5f;
            var mb = new BoxMeshBuilder(2); // 0 = rock, 1 = moss

            for (int s = 0; s < 2; s++)
            {
                SideProfile side = _sides[s];
                float sign = s == 0 ? -1f : 1f;
                float z = rng.Range(0f, settings.rockSpacing * 0.5f);

                while (z < _length)
                {
                    // No boundary wall on the open sea, nor inside the snow mountains (only at their transitions)
                    if ((side.isSea && GetSeaWeight(sign, z) > 0.3f) || GetMountainWeight(z) > 0.5f)
                    {
                        z += settings.rockSpacing * 0.5f;
                        continue;
                    }

                    SampleSide(side, z, out float inner, out float ramp, out float h);
                    bool needRocks = side.style != ChunkBoundaryStyle.Cliff || h < rockFillHeight;
                    if (!needRocks)
                    {
                        z += settings.rockSpacing * 0.5f;
                        continue;
                    }

                    float size = rng.Range(settings.minRockSize, settings.maxRockSize);
                    if (side.style == ChunkBoundaryStyle.Rocks) size *= 1.25f;

                    // CliffAndRocks: rocks sit at the foot of the cliff. Rocks: rocks form the wall themselves.
                    float d = side.style == ChunkBoundaryStyle.CliffAndRocks
                        ? inner + rng.Range(-3f, ramp * 0.3f)
                        : inner + rng.Range(0f, 18f);
                    d = Mathf.Min(d, _halfWidth - size * 0.3f);
                    SpawnCluster(mb, container, chunk, terrain, sign * d, z, size, rng);

                    if (side.style == ChunkBoundaryStyle.Rocks && rng.Chance(0.6f))
                    {
                        float backSize = size * rng.Range(0.8f, 1.2f);
                        float backD = Mathf.Min(d + rng.Range(size * 0.9f, size * 1.6f), _halfWidth - backSize * 0.3f);
                        float backZ = z + rng.Range(-size * 0.4f, size * 0.4f);
                        SpawnCluster(mb, container, chunk, terrain, sign * backD, backZ, backSize, rng);
                    }

                    z += settings.rockSpacing * rng.Range(0.7f, 1.15f) * Mathf.Max(1f, size / avgSize);
                }
            }

            SpawnShoreRocks(mb, container, chunk, terrain);
            SpawnCanyonRivers(container);

            if (mb.VertexCount > 0)
            {
                _rockMesh = mb.ToMesh($"BoundaryRocks_Chunk{chunk.ChunkIndex}");
                container.gameObject.AddComponent<MeshFilter>().sharedMesh = _rockMesh;
                container.gameObject.AddComponent<MeshRenderer>().sharedMaterials = new[] { rockMat, mossMat };
            }
        }

        /// <summary>
        /// Rocky coasts: smaller rock clusters scattered along the waterline.
        /// </summary>
        private void SpawnShoreRocks(BoxMeshBuilder mb, Transform container, Chunk chunk, Terrain terrain)
        {
            for (int s = 0; s < 2; s++)
            {
                SideProfile side = _sides[s];
                if (!side.isSea) continue;

                float sign = s == 0 ? -1f : 1f;
                var rng = new SeededRandom(SeededRandom.Combine(GetPlanSeed(chunk), "shore_rocks"));
                float z = rng.Range(0f, 10f);

                while (z < _length)
                {
                    float step = rng.Range(14f, 26f);
                    float f = Mathf.Clamp01(z / _length) * (_rows - 1);
                    int i = Mathf.RoundToInt(f);
                    // Sea cliff along the road: rocks in the surf at the cliff foot. Otherwise only rocky shores get rocks.
                    bool atCliff = side.seaCliff[i] > 0.5f;

                    if ((atCliff || side.rockyShore) && GetSeaWeight(sign, z) > 0.6f && rng.Chance(atCliff ? 0.55f : 0.65f))
                    {
                        float roadEdge = _roadX[i] * sign + _roadHalfWidth;
                        float d = atCliff
                            ? roadEdge + seaCliffShoulder + seaCliffDrop + rng.Range(1f, 9f)
                            : side.shore[i] - side.shoreSlope[i] * rng.Range(0f, 0.5f) + rng.Range(-2f, 8f);
                        float size = rng.Range(5f, 12f);
                        d = Mathf.Max(d, roadEdge + size * 0.6f + 3f); // never on the road
                        if (d < _halfWidth - size)
                        {
                            SpawnCluster(mb, container, chunk, terrain, sign * d, z, size, rng);
                        }
                    }
                    z += step;
                }
            }
        }

        // ---------------------------------------------------------------
        // Terrain painting (sand on coasts)
        // ---------------------------------------------------------------

        private static TerrainLayer s_runtimeSandLayer;

        /// <summary>
        /// Coast chunks: paints sand on the beach / seabed. Other chunks: restores the single grass layer
        /// (edit-mode previews share one TerrainData asset, so a previous coast preview must be undone).
        /// heights = normalized heightmap just written by the terrain adapter.
        /// </summary>
        public void PaintTerrainLayers(TerrainData td, float[,] heights, float terrainXOffset)
        {
            if (td == null || heights == null) return;
            TerrainLayer[] layers = td.terrainLayers;
            if (layers == null || layers.Length == 0 || layers[0] == null) return;

            bool coast = _sides != null && (_sides[0].isSea || _sides[1].isSea);
            int res = td.alphamapResolution;

            if (_mtnSides != null)
            {
                PaintMountain(td, heights, layers[0]);
                return;
            }

            if (!coast)
            {
                if (layers.Length > 1)
                {
                    td.terrainLayers = new[] { layers[0] };
                    var full = new float[res, res, 1];
                    for (int y = 0; y < res; y++)
                        for (int x = 0; x < res; x++)
                            full[y, x, 0] = 1f;
                    td.SetAlphamaps(0, 0, full);
                }
                return;
            }

            TerrainLayer sand = GetSandLayer();
            if (sand == null) return;
            if (layers.Length != 2 || layers[1] != sand)
            {
                td.terrainLayers = new[] { layers[0], sand };
            }
            RoadTerrainAdapter.LimitRuntimeAlphamapResolution(td);
            res = td.alphamapResolution;

            int hRes = heights.GetLength(0);
            Vector3 size = td.size;
            var alphas = new float[res, res, 2];

            for (int y = 0; y < res; y++)
            {
                float nz = y / (res - 1f);
                float localZ = nz * size.z;
                for (int x = 0; x < res; x++)
                {
                    float nx = x / (res - 1f);
                    float h = SampleNormalizedHeight(heights, hRes, nx, nz) * size.y;
                    float localX = terrainXOffset + nx * size.x;

                    // Sand on the beach band and seabed, only on the sea side
                    float sandW = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(WorldConstants.SeaLevel + 2.5f, WorldConstants.SeaLevel + 6f, h));
                    sandW *= GetSeaWeight(localX, localZ);

                    alphas[y, x, 0] = 1f - sandW;
                    alphas[y, x, 1] = sandW;
                }
            }
            td.SetAlphamaps(0, 0, alphas);
        }

        /// <summary>
        /// Mountain chunks. Snowy: snow everywhere, rock on steep faces. Alpine: grass, rock on steep faces, snow
        /// only above <see cref="alpineSnowLine"/>. Toward a non-mountain neighbor everything fades back to grass.
        /// Layers: [grass, snow, rock].
        /// </summary>
        private void PaintMountain(TerrainData td, float[,] heights, TerrainLayer grass)
        {
            TerrainLayer snow = TerrainLayerLibrary.Get("Layer_Snow", snowGroundColor);
            TerrainLayer rock = TerrainLayerLibrary.Get("Layer_MountainRock", mountainRockColor);
            if (snow == null || rock == null) return;

            TerrainLayer[] layers = td.terrainLayers;
            if (layers.Length != 3 || layers[1] != snow || layers[2] != rock)
            {
                td.terrainLayers = new[] { grass, snow, rock };
            }
            RoadTerrainAdapter.LimitRuntimeAlphamapResolution(td);
            int res = td.alphamapResolution;

            int hRes = heights.GetLength(0);
            Vector3 size = td.size;
            float cell = size.x / (hRes - 1);          // heightmap sample spacing (m)

            // Slope (tan) once per heightmap sample, then sampled per splat texel
            var slopes = new float[hRes, hRes];
            for (int z = 0; z < hRes; z++)
            {
                int z0 = Mathf.Max(0, z - 1), z1 = Mathf.Min(hRes - 1, z + 1);
                for (int x = 0; x < hRes; x++)
                {
                    int x0 = Mathf.Max(0, x - 1), x1 = Mathf.Min(hRes - 1, x + 1);
                    float dhx = (heights[z, x1] - heights[z, x0]) * size.y / ((x1 - x0) * cell);
                    float dhz = (heights[z1, x] - heights[z0, x]) * size.y / ((z1 - z0) * cell);
                    slopes[z, x] = Mathf.Sqrt(dhx * dhx + dhz * dhz);
                }
            }

            bool snowy = _mtnVariant == MountainVariant.Snowy;
            var alphas = new float[res, res, 3];
            for (int y = 0; y < res; y++)
            {
                float nz = y / (res - 1f);
                float mtnW = GetMountainWeight(nz * size.z);
                float rowRoadY = _roadY[Mathf.Clamp(Mathf.RoundToInt(nz * (_rows - 1)), 0, _rows - 1)];
                for (int x = 0; x < res; x++)
                {
                    float nx = x / (res - 1f);
                    float slope = SampleNormalizedHeight(slopes, hRes, nx, nz);

                    float rockW = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(mountainRockSlope, mountainRockSlope + 0.45f, slope)) * mtnW;
                    float snowW = mtnW;
                    if (!snowy)
                    {
                        float aboveRoad = SampleNormalizedHeight(heights, hRes, nx, nz) * size.y - rowRoadY;
                        snowW *= Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(alpineSnowLine, alpineSnowLine + 10f, aboveRoad));
                    }
                    snowW = Mathf.Max(0f, snowW - rockW);
                    alphas[y, x, 0] = 1f - snowW - rockW;
                    alphas[y, x, 1] = snowW;
                    alphas[y, x, 2] = rockW;
                }
            }
            td.SetAlphamaps(0, 0, alphas);
        }

        private static float SampleNormalizedHeight(float[,] heights, int hRes, float nx, float nz)
        {
            float fx = nx * (hRes - 1), fz = nz * (hRes - 1);
            int x0 = Mathf.FloorToInt(fx), z0 = Mathf.FloorToInt(fz);
            int x1 = Mathf.Min(x0 + 1, hRes - 1), z1 = Mathf.Min(z0 + 1, hRes - 1);
            float tx = fx - x0, tz = fz - z0;
            float a = Mathf.Lerp(heights[z0, x0], heights[z0, x1], tx);
            float b = Mathf.Lerp(heights[z1, x0], heights[z1, x1], tx);
            return Mathf.Lerp(a, b, tz);
        }

        private TerrainLayer GetSandLayer()
        {
#if UNITY_EDITOR
            // Edit mode writes into the prefab's TerrainData asset, so the layer must be an asset too
            var asset = UnityEditor.AssetDatabase.LoadAssetAtPath<TerrainLayer>("Assets/Prefabs/Chunks/TerrainLayers/Layer_Desert.terrainlayer");
            if (asset != null) return asset;
#endif
            if (s_runtimeSandLayer == null)
            {
                var tex = new Texture2D(4, 4, TextureFormat.RGBA32, false) { name = "Tex_Sand_Runtime" };
                var pixels = new Color[16];
                for (int i = 0; i < pixels.Length; i++) pixels[i] = sandColor;
                tex.SetPixels(pixels);
                tex.Apply();
                s_runtimeSandLayer = new TerrainLayer { name = "Layer_Sand_Runtime", diffuseTexture = tex, tileSize = new Vector2(12f, 12f) };
            }
            return s_runtimeSandLayer;
        }

        /// <summary>
        /// One rock cluster (2-4 tilted boxes). All clusters of a chunk go into one combined mesh (chunk-local);
        /// only the main piece gets a collider, on its own small GameObject.
        /// </summary>
        private void SpawnCluster(BoxMeshBuilder mb, Transform colliderParent, Chunk chunk, Terrain terrain,
            float localX, float localZ, float size, SeededRandom rng)
        {
            Vector3 world = chunk.transform.TransformPoint(new Vector3(localX, 0f, localZ));
            float groundY = terrain != null
                ? terrain.SampleHeight(world) + terrain.transform.position.y - chunk.transform.position.y
                : chunk.baseElevation;

            var clusterPos = new Vector3(localX, groundY, localZ);
            Quaternion clusterRot = Quaternion.Euler(0f, rng.Range(0f, 360f), 0f);

            int pieces = rng.Range(2, 5);
            for (int i = 0; i < pieces; i++)
            {
                float pieceSize = i == 0 ? size : size * rng.Range(0.35f, 0.7f);
                Vector3 scale = new Vector3(
                    pieceSize * rng.Range(0.8f, 1.35f),
                    pieceSize * rng.Range(0.55f, 1.0f),
                    pieceSize * rng.Range(0.8f, 1.3f));

                Vector3 offset = i == 0
                    ? Vector3.zero
                    : new Vector3(rng.Range(-0.6f, 0.6f) * size, 0f, rng.Range(-0.6f, 0.6f) * size);
                // Sink into the ground so slopes never show a gap under the rock
                offset.y = scale.y * 0.5f - pieceSize * 0.25f;

                Vector3 center = clusterPos + clusterRot * offset;
                Quaternion rot = clusterRot * Quaternion.Euler(rng.Range(-14f, 14f), rng.Range(0f, 360f), rng.Range(-14f, 14f));
                bool moss = i > 0 && rng.Chance(0.4f);
                mb.Box(center, scale, rot, moss ? 1 : 0);

                if (i == 0)
                {
                    var col = new GameObject("RockCollider");
                    col.transform.SetParent(colliderParent, false);
                    col.transform.localPosition = center;
                    col.transform.localRotation = rot;
                    col.AddComponent<BoxCollider>().size = scale;
                }
            }

            _rockFootprints.Add(new Vector3(localX, localZ, size * 0.9f));
        }

        private void DestroyRockMesh()
        {
            if (_rockMesh != null)
            {
                if (Application.isPlaying) Destroy(_rockMesh);
                else DestroyImmediate(_rockMesh);
                _rockMesh = null;
            }

            for (int i = 0; i < _riverMeshes.Count; i++)
            {
                if (_riverMeshes[i] == null) continue;
                if (Application.isPlaying) Destroy(_riverMeshes[i]);
                else DestroyImmediate(_riverMeshes[i]);
            }
            _riverMeshes.Clear();
        }

        private void OnDestroy()
        {
            DestroyRockMesh();
        }

        private static void ClearRocks(Transform parent)
        {
            if (parent == null) return;
            Transform old = parent.Find(ROCKS_CONTAINER_NAME);
            if (old == null) return;

            if (!Application.isPlaying)
            {
                DestroyImmediate(old.gameObject);
            }
            else
            {
                old.name = ROCKS_CONTAINER_NAME + "_Destroying";
                old.gameObject.SetActive(false);
                Destroy(old.gameObject);
            }
        }

        // ---------------------------------------------------------------
        // Helpers
        // ---------------------------------------------------------------

        private Chunk GetChunk()
        {
            Chunk chunk = GetComponent<Chunk>();
            if (chunk == null) chunk = GetComponentInParent<Chunk>();
            return chunk;
        }

        private BiomeBoundarySettings GetSettings(Chunk chunk)
        {
            if (biomeSettings == null || biomeSettings.Count == 0) return null;
            if (chunk != null)
            {
                for (int i = 0; i < biomeSettings.Count; i++)
                {
                    if (biomeSettings[i] != null && biomeSettings[i].biome == chunk.biomeType)
                        return biomeSettings[i];
                }
            }
            return biomeSettings[0];
        }

        private int GetPlanSeed(Chunk chunk)
        {
            if (chunk == null) return 1;
            if (chunk.ChunkSeed != 0) return chunk.ChunkSeed;
            // Edit mode (chunk not initialized by ChunkManager): follow the editor preview seed
            if (_hasPreviewSeed) return _previewSeed;
            return string.IsNullOrEmpty(chunk.editorPreviewSeed) ? 12345 : SeededRandom.HashString(chunk.editorPreviewSeed);
        }

        private static ChunkBoundaryStyle PickStyle(SeededRandom rng, BiomeBoundarySettings settings)
        {
            int w0 = Mathf.Max(0, settings.cliffWeight);
            int w1 = w0 + Mathf.Max(0, settings.rocksWeight);
            int total = w1 + Mathf.Max(0, settings.cliffAndRocksWeight);
            if (total <= 0) return ChunkBoundaryStyle.Cliff;

            int roll = rng.Range(0, total);
            if (roll < w0) return ChunkBoundaryStyle.Cliff;
            if (roll < w1) return ChunkBoundaryStyle.Rocks;
            return ChunkBoundaryStyle.CliffAndRocks;
        }

        /// <summary>
        /// Seam i, chunk (i-1)'in sonu ile chunk i'nin başıdır. Her iki chunk aynı değeri hesaplar.
        /// </summary>
        private float SeamHeight(int worldSeed, int seamIndex, int side)
        {
            int seed = SeededRandom.Combine(SeededRandom.Combine(SeededRandom.Combine(worldSeed, "boundary_seam"), seamIndex), side);
            var rng = new SeededRandom(seed);
            return rng.Chance(seamCliffChance) ? rng.Range(seamMinHeight, seamMaxHeight) : 0f;
        }

        private void SampleSide(SideProfile side, float z, out float inner, out float ramp, out float height)
        {
            float f = Mathf.Clamp01(z / _length) * (_rows - 1);
            int i0 = Mathf.FloorToInt(f);
            int i1 = Mathf.Min(i0 + 1, _rows - 1);
            float t = f - i0;
            inner = Mathf.Lerp(side.inner[i0], side.inner[i1], t);
            ramp = Mathf.Lerp(side.ramp[i0], side.ramp[i1], t);
            height = Mathf.Lerp(side.height[i0], side.height[i1], t);
        }

        private static List<Vector2> CollectPoiKeepouts(Chunk chunk)
        {
            var list = new List<Vector2>();
            if (chunk == null) return list;

            var pois = chunk.GetComponentsInChildren<PointOfInterest>(true);
            for (int i = 0; i < pois.Length; i++)
            {
                if (pois[i] == null) continue;
                Vector3 local = chunk.transform.InverseTransformPoint(pois[i].transform.position);
                list.Add(new Vector2(local.x, local.z));
            }
            return list;
        }

        /// <summary>Shared runtime URP Lit material per color (also used by ChunkBackdropGenerator).</summary>
        public static Material GetSharedMaterial(Color color)
        {
            if (_materialCache.TryGetValue(color, out Material cached) && cached != null) return cached;

            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            var mat = new Material(shader) { name = "Mat_BoundaryRock_Runtime", color = color };
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.05f);

            _materialCache[color] = mat;
            return mat;
        }
    }
}
