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

        private float[] _roadX;          // road center X per heightmap row
        private float _roadHalfWidth = 9f;
        private int _seaIndex = -1;      // index into _sides of the sea side, -1 = no sea
        private float _seaSign;

        private SideProfile[] _sides; // 0 = sol (x<0), 1 = sağ (x>0)
        private int _rows;
        private float _length;
        private float _halfWidth;
        private readonly List<Vector3> _rockFootprints = new List<Vector3>(); // x, z, radius (chunk local)

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

            float[] roadX = new float[rows];
            for (int r = 0; r < rows; r++)
            {
                float z = r / (rows - 1f) * length;
                roadX[r] = spline != null ? spline.GetPointAtZ(z).x : 0f;
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
                float seamA = SeamHeight(worldSeed, startSeam, s);
                float seamB = SeamHeight(worldSeed, startSeam + 1, s);

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

            return true;
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
            return y;
        }

        /// <summary>
        /// Applied after the road bed has been blended in: where the road runs right along the sea,
        /// the terrain drops as a cliff straight from the road shoulder into the water (overrides the
        /// adapter's wide road blend on the sea side). Only ever lowers the terrain.
        /// </summary>
        public float ApplyAfterRoad(float localX, int row, float height, float roadBedY)
        {
            if (_sides == null || _seaIndex < 0) return height;
            row = Mathf.Clamp(row, 0, _rows - 1);
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
            _rockFootprints.Clear();
            if (!boundaryEnabled || parent == null) return;

            Chunk chunk = GetChunk();
            if (chunk == null) return;

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

            Transform container = new GameObject(ROCKS_CONTAINER_NAME).transform;
            container.SetParent(parent, false);

            Material rockMat = rockMaterialOverride != null ? rockMaterialOverride : GetSharedMaterial(settings.rockColor);
            Material mossMat = mossMaterialOverride != null ? mossMaterialOverride : GetSharedMaterial(settings.mossColor);
            float avgSize = (settings.minRockSize + settings.maxRockSize) * 0.5f;

            for (int s = 0; s < 2; s++)
            {
                SideProfile side = _sides[s];
                float sign = s == 0 ? -1f : 1f;
                float z = rng.Range(0f, settings.rockSpacing * 0.5f);

                while (z < _length)
                {
                    // No boundary wall on the open sea
                    if (side.isSea && GetSeaWeight(sign, z) > 0.3f)
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
                    SpawnCluster(container, chunk, terrain, sign * d, z, size, rng, rockMat, mossMat);

                    if (side.style == ChunkBoundaryStyle.Rocks && rng.Chance(0.6f))
                    {
                        float backSize = size * rng.Range(0.8f, 1.2f);
                        float backD = Mathf.Min(d + rng.Range(size * 0.9f, size * 1.6f), _halfWidth - backSize * 0.3f);
                        float backZ = z + rng.Range(-size * 0.4f, size * 0.4f);
                        SpawnCluster(container, chunk, terrain, sign * backD, backZ, backSize, rng, rockMat, mossMat);
                    }

                    z += settings.rockSpacing * rng.Range(0.7f, 1.15f) * Mathf.Max(1f, size / avgSize);
                }
            }

            SpawnShoreRocks(container, chunk, terrain, rockMat, mossMat);
        }

        /// <summary>
        /// Rocky coasts: smaller rock clusters scattered along the waterline.
        /// </summary>
        private void SpawnShoreRocks(Transform container, Chunk chunk, Terrain terrain, Material rockMat, Material mossMat)
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
                            SpawnCluster(container, chunk, terrain, sign * d, z, size, rng, rockMat, mossMat);
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

        private void SpawnCluster(Transform container, Chunk chunk, Terrain terrain, float localX, float localZ, float size,
            SeededRandom rng, Material rockMat, Material mossMat)
        {
            Vector3 world = chunk.transform.TransformPoint(new Vector3(localX, 0f, localZ));
            float groundY = terrain != null
                ? terrain.SampleHeight(world) + terrain.transform.position.y
                : chunk.transform.position.y + chunk.baseElevation;

            GameObject cluster = new GameObject("BoundaryRock");
            cluster.transform.SetParent(container, false);
            cluster.transform.position = new Vector3(world.x, groundY, world.z);
            cluster.transform.rotation = chunk.transform.rotation * Quaternion.Euler(0f, rng.Range(0f, 360f), 0f);

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

                GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                cube.name = $"RockPiece_{i}";
                cube.transform.SetParent(cluster.transform, false);
                cube.transform.localPosition = offset;
                cube.transform.localRotation = Quaternion.Euler(rng.Range(-14f, 14f), rng.Range(0f, 360f), rng.Range(-14f, 14f));
                cube.transform.localScale = scale;
                cube.GetComponent<MeshRenderer>().sharedMaterial = (i > 0 && rng.Chance(0.4f)) ? mossMat : rockMat;
            }

            _rockFootprints.Add(new Vector3(localX, localZ, size * 0.9f));
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
