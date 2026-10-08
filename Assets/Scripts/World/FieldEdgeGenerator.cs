using System.Collections.Generic;
using UnityEngine;
using EndlessSurvival.World.POI;

namespace EndlessSurvival.World
{
    public enum FieldEdgeStyle
    {
        FenceTrees,     // Tel çit + arkasında ağaç sırası
        FenceFarmland,  // Tel çit + arkasında sürülmüş tarla (saman balyalı)
        FenceCorn,      // Tel çit + arkasında uzun mısır şeridi
        Stream          // Kenar boyunca akan dere
    }

    /// <summary>
    /// Field (open plains) chunks: keeps the land flat and closes each side with a random,
    /// seed-based edge style instead of cliffs. Everything that continues into the next chunk
    /// (fence line, stream, corn strip) is positioned so that neighboring Field chunks line up.
    /// Fences, trees, corn and hay bales are combined into one mesh per side (simple boxes).
    /// </summary>
    [DisallowMultipleComponent]
    public class FieldEdgeGenerator : MonoBehaviour
    {
        public const string CONTAINER_NAME = "Field_Edges";

        public bool fieldEdgesEnabled = true;

        [Header("Stil Ağırlıkları (her taraf ayrı seçilir)")]
        [Range(0, 100)] public int fenceTreesWeight = 30;
        [Range(0, 100)] public int fenceFarmlandWeight = 25;
        [Range(0, 100)] public int fenceCornWeight = 25;
        [Range(0, 100)] public int streamWeight = 20;

        [Header("Düzlük")]
        [Range(0f, 1f)]
        [Tooltip("Arazinin doğal dalgalanmasından düzlükte kalan oran (0 = tamamen düz)")]
        public float ambientScale = 0.12f;
        [Tooltip("Oyuncunun alan dışına yürümemesi için kenarlardaki görünmez duvarın yeri")]
        public float edgeWallDistance = 248f;

        [Header("Tel Çit")]
        [Tooltip("Çitin chunk merkez çizgisine uzaklığı (tüm düzlük chunk'larında aynı, böylece çit kesintisiz devam eder)")]
        public float fenceLine = 150f;
        public float postSpacing = 2.8f;
        public float fenceHeight = 1.4f;
        [Range(0f, 0.5f)] [Tooltip("Bir çit aralığının kopuk/eksik olma ihtimali")]
        public float fenceGapChance = 0.06f;
        [Range(0f, 0.5f)] [Tooltip("Bir direğin yamuk/eğik olma ihtimali")]
        public float leaningPostChance = 0.1f;
        public Color postColor = new Color(0.33f, 0.25f, 0.17f);
        public Color wireColor = new Color(0.20f, 0.20f, 0.21f);

        [Header("Ağaç Sırası")]
        public float treeRowOffset = 9f;
        public float minTreeSpacing = 6.5f;
        public float maxTreeSpacing = 9.5f;
        [Range(0f, 0.5f)] public float treeGapChance = 0.12f;
        [Tooltip("Ön sıranın arkasındaki ağaç kuşağının derinliği (yol boyunca bu aralıkta değişir)")]
        public float minTreeBeltDepth = 14f;
        public float maxTreeBeltDepth = 40f;
        [Tooltip("Kuşaktaki sıralar arası mesafe")]
        public float beltRowSpacing = 6f;
        [Range(0f, 0.6f)] [Tooltip("Arka sıralarda boşluk ihtimali")]
        public float beltGapChance = 0.2f;
        [Range(0f, 1f)] [Tooltip("Arka sıralarda geniş tepeli (meşe) ağaç oranı")]
        public float beltBroadChance = 0.4f;
        public float minTreeHeight = 11f;
        public float maxTreeHeight = 16f;
        public Color crownColor = new Color(0.17f, 0.31f, 0.14f);
        public Color trunkColor = new Color(0.30f, 0.22f, 0.15f);

        [Header("Tarla")]
        [Tooltip("Sürülmüş tarla şeritlerinin aralığı")]
        public float furrowSpacing = 3f;
        public int minHayBales = 4;
        public int maxHayBales = 12;
        public Color hayColor = new Color(0.78f, 0.66f, 0.32f);
        public Color soilColor = new Color(0.38f, 0.30f, 0.22f);

        [Header("Mısır Şeridi")]
        public float minCornDepth = 22f;
        public float maxCornDepth = 45f;
        public float cornRowSpacing = 2.0f;
        public float cornPlantSpacing = 1.4f;
        public float minCornHeight = 2.3f;
        public float maxCornHeight = 2.9f;
        public Color cornColor = new Color(0.42f, 0.55f, 0.18f);
        public Color tasselColor = new Color(0.80f, 0.70f, 0.35f);

        [Header("Dere")]
        [Tooltip("Dere orta çizgisinin chunk merkezine uzaklığı (chunk birleşimlerinde sabit)")]
        public float streamCenter = 175f;
        public float streamMeander = 18f;
        public float streamBedHalfWidth = 3f;
        public float streamBankWidth = 6f;
        public float streamDepth = 2.2f;
        [Tooltip("Komşu chunk'ta aynı tarafta dere yoksa derenin başlayıp/bittiği mesafe")]
        public float streamTaper = 70f;
        public Color waterColor = new Color(0.12f, 0.30f, 0.36f);

        [Header("Nehir ve Köprü (yolu dik kesen)")]
        [Range(0f, 1f)]
        [Tooltip("Bir düzlük chunk'ında yolu kesen nehir ve üstünde köprü olma ihtimali")]
        public float bridgeChance = 0.3f;
        [Tooltip("Nehrin yolu kestiği yer (chunk içi Z aralığı). 250 = chunk'ın tam ortası.")]
        public float minBridgeZ = 180f;
        public float maxBridgeZ = 320f;
        [Tooltip("Nehrin yola göre en fazla ne kadar çapraz geçebileceği (derece)")]
        [Range(0f, 25f)] public float maxRiverAngle = 18f;
        public float riverMeander = 12f;
        [Tooltip("Su yüzeyi yarı genişliği")]
        public float riverHalfWidth = 9f;
        [Tooltip("Alçak köprülerde kıyı eğimi genişliği. Derin yarıklarda derinliğe göre otomatik genişler.")]
        public float riverBankWidth = 6f;
        [Tooltip("Kıyı eğimi genişliği / yarık derinliği oranı (1.2 ≈ 40°, oyuncu tırmanabilir)")]
        public float gorgeBankRatio = 1.2f;
        [Tooltip("Su derinliği (su yüzeyinden yatağa)")]
        public float riverWaterDepth = 2.5f;

        [Header("Köprü Yüksekliği (köprü altı boşluk = yol ile su yüzeyi arası)")]
        [Range(0f, 1f)]
        [Tooltip("Köprünün yüksek (derin yarık üstünde) olma ihtimali")]
        public float highBridgeChance = 0.35f;
        public float minLowClearance = 3f;
        public float maxLowClearance = 6f;
        public float minHighClearance = 10f;
        public float maxHighClearance = 18f;
        [Tooltip("Köprü boyu bundan uzunsa ayak konur")]
        public float pierMinSpan = 18f;
        [Tooltip("Uzun köprülerde ayaklar arası mesafe")]
        public float pierSpacing = 16f;

        [Header("Düzlük Tepeleri (yol eğimliyse arazi de birlikte yükselir/alçalır)")]
        [Tooltip("Yolun iki yanında yolla aynı yükseklikte kalan düzlük yarı genişliği")]
        public float hillPlateauHalfWidth = 30f;
        [Tooltip("Tepenin yoldan uzaklaştıkça çayır seviyesine inme mesafesi")]
        public float hillFalloff = 140f;
        public Color concreteColor = new Color(0.56f, 0.56f, 0.54f);
        public Color deckColor = new Color(0.40f, 0.40f, 0.39f);

        [Header("POI Açıklığı")]
        [Tooltip("Kamp/mağara/fener çevresinde çit, mısır ve dere olmaz")]
        public float poiClearance = 35f;

        private class SidePlan
        {
            public FieldEdgeStyle style;
            public float[] streamX;      // stream center distance from chunk center line per row
            public float[] streamScale;  // 0..1 stream depth/width (tapers where the stream starts/ends)
        }

        private SidePlan[] _sides;
        private int _rows;
        private float _length;
        private bool _active;

        // River crossing the road (z of its center line as a function of chunk-local x)
        private bool _hasRiver;
        private float _riverZ, _riverTan, _riverCos, _riverPhase;
        private float _riverWaterY, _riverBedY, _riverBank, _riverDeckY;
        private float _halfWidth = 250f;
        private float _baseline = 20f;
        private readonly List<Vector2> _keepouts = new List<Vector2>();
        private readonly List<Mesh> _ownedMeshes = new List<Mesh>();

        private static TerrainLayer s_runtimeSoilLayer;
        private static Material s_waterMaterial;

        // Submesh indices of the combined decoration mesh
        private const int SubWood = 0, SubWire = 1, SubCrown = 2, SubCorn = 3, SubTassel = 4, SubHay = 5, SubCount = 6;

        /// <summary>True after a successful BuildPlan on a Field chunk.</summary>
        public bool IsActive => _active;

        // ---------------------------------------------------------------
        // Plan
        // ---------------------------------------------------------------

        public bool BuildPlan(int rows, float length, Road.RoadSpline spline)
        {
            _active = false;
            _sides = null;
            Chunk chunk = GetChunk();
            if (!fieldEdgesEnabled || chunk == null || !chunk.IsField || rows < 2) return false;

            _rows = rows;
            _length = length;
            _baseline = chunk.baseElevation;
            int worldSeed = chunk.WorldSeed;
            int index = chunk.ChunkIndex;
            CollectKeepouts(chunk);

            _sides = new SidePlan[2];
            for (int s = 0; s < 2; s++)
            {
                var side = new SidePlan { style = StyleFor(worldSeed, index, s) };
                _sides[s] = side;
                if (side.style != FieldEdgeStyle.Stream) continue;

                // Stream continues into a neighbor only if that neighbor is a Field with a stream on the same side.
                // A lone chunk previewed in the editor is treated as continuous.
                bool prevStream = !chunk.hasNeighborInfo
                    || (index > 0 && chunk.PrevBiome == ChunkBiomeType.Field && StyleFor(worldSeed, index - 1, s) == FieldEdgeStyle.Stream);
                bool nextStream = !chunk.hasNeighborInfo
                    || (chunk.NextBiome == ChunkBiomeType.Field && StyleFor(worldSeed, index + 1, s) == FieldEdgeStyle.Stream);

                float sign = s == 0 ? -1f : 1f;
                float noise = new SeededRandom(SeededRandom.Combine(SeededRandom.Combine(chunk.ChunkSeed != 0 ? chunk.ChunkSeed : worldSeed, "stream"), s)).Range(0f, 1000f);
                side.streamX = new float[rows];
                side.streamScale = new float[rows];

                for (int r = 0; r < rows; r++)
                {
                    float z = r / (rows - 1f) * length;
                    float w = Mathf.Clamp01(Mathf.Sin(z / length * Mathf.PI)); // 0 at seams -> same X as the neighbor
                    float x = streamCenter + streamMeander * w * (Mathf.PerlinNoise(noise, z * 0.008f) - 0.5f) * 2f;

                    float scale = Mathf.Min(
                        prevStream ? 1f : Mathf.SmoothStep(0f, 1f, z / streamTaper),
                        nextStream ? 1f : Mathf.SmoothStep(0f, 1f, (length - z) / streamTaper));

                    // Keep camps/caves dry
                    for (int k = 0; k < _keepouts.Count; k++)
                    {
                        float dx = (_keepouts[k].x * sign - x) / (poiClearance + 15f);
                        float dz = (_keepouts[k].y - z) / (poiClearance + 15f);
                        scale *= 1f - Mathf.Exp(-(dx * dx + dz * dz) * 2f);
                    }

                    side.streamX[r] = x;
                    side.streamScale[r] = scale;
                }
            }

            PlanRiver(chunk, spline);

            _active = true;
            return true;
        }

        /// <summary>
        /// Rolls whether a river crosses this chunk (under a bridge), where, and how high the bridge is.
        /// Stays clear of camps/caves.
        /// </summary>
        private void PlanRiver(Chunk chunk, Road.RoadSpline spline)
        {
            _hasRiver = false;
            var rng = new SeededRandom(SeededRandom.Combine(chunk.ChunkSeed != 0 ? chunk.ChunkSeed : chunk.WorldSeed, "field_river"));
            if (!rng.Chance(bridgeChance)) return;

            float angle = rng.Range(-maxRiverAngle, maxRiverAngle) * Mathf.Deg2Rad;
            _riverTan = Mathf.Tan(angle);
            _riverCos = Mathf.Cos(angle);
            _riverPhase = rng.Range(0f, 10f);

            // Clearance under the bridge: mostly low, sometimes a high bridge over a deep gorge
            float clearance = rng.Chance(highBridgeChance)
                ? rng.Range(minHighClearance, maxHighClearance)
                : rng.Range(minLowClearance, maxLowClearance);

            // Try a few crossing points; skip any that would run the river through a POI
            for (int attempt = 0; attempt < 6; attempt++)
            {
                _riverZ = rng.Range(minBridgeZ, maxBridgeZ);
                PlanRiverDepth(spline, clearance);

                bool clear = true;
                for (int k = 0; k < _keepouts.Count && clear; k++)
                {
                    if (RiverDistance(_keepouts[k].x, _keepouts[k].y) < riverHalfWidth + _riverBank + poiClearance) clear = false;
                }
                if (clear)
                {
                    _hasRiver = true;
                    return;
                }
            }
        }

        /// <summary>
        /// Water and bed height from the road (deck) height where it crosses the river. The water always stays
        /// below the surrounding meadow, so a crossing on a hill top automatically becomes a higher bridge.
        /// </summary>
        private void PlanRiverDepth(Road.RoadSpline spline, float clearance)
        {
            float deckY = _baseline;
            if (spline != null)
            {
                // Road/river intersection: the river is close to perpendicular, so this converges in a few steps
                float z = _riverZ;
                for (int i = 0; i < 4; i++) z = Mathf.Clamp(RiverCenterZ(spline.GetPointAtZ(z).x), 0f, _length);
                deckY = spline.GetPointAtZ(z).y;
            }
            _riverDeckY = deckY;

            _riverWaterY = Mathf.Max(riverWaterDepth + 0.3f, Mathf.Min(deckY - clearance, _baseline - 1.5f));
            _riverBedY = Mathf.Max(0.3f, _riverWaterY - riverWaterDepth);
            // Deeper gorge -> wider banks, so the slopes stay walkable (~40°)
            _riverBank = Mathf.Max(riverBankWidth, (Mathf.Max(deckY, _baseline) - _riverBedY) * gorgeBankRatio);
        }

        /// <summary>Height of the bridge clearance actually built (road surface to water), for the inspector.</summary>
        public float BridgeClearance => _riverDeckY - _riverWaterY;

        /// <summary>River center line Z at a chunk-local X. Beyond the terrain edge it runs straight out to the horizon.</summary>
        private float RiverCenterZ(float localX)
        {
            float x = Mathf.Clamp(localX, -_halfWidth, _halfWidth);
            return _riverZ + _riverTan * x + riverMeander * Mathf.Sin(x * 0.012f + _riverPhase);
        }

        /// <summary>Approximate perpendicular distance from the river center line.</summary>
        private float RiverDistance(float localX, float localZ)
        {
            return Mathf.Abs(localZ - RiverCenterZ(localX)) * _riverCos;
        }

        private bool InRiverBand(float localX, float localZ, float margin)
        {
            return _hasRiver && RiverDistance(localX, localZ) < riverHalfWidth + _riverBank + margin;
        }

        public bool HasRiver => _active && _hasRiver;

        /// <summary>
        /// Applied after the road bed is blended in: the river channel is cut under the road too,
        /// so the road crosses it as a bridge (the road mesh has its own collider).
        /// </summary>
        public float ApplyAfterRoad(float localX, int row, float height)
        {
            if (!_active || !_hasRiver) return height;
            float z = Mathf.Clamp(row, 0, _rows - 1) / (_rows - 1f) * _length;
            float d = RiverDistance(localX, z);
            float outer = riverHalfWidth + _riverBank;
            if (d >= outer) return height;

            float t = d <= riverHalfWidth * 0.6f ? 0f : Mathf.SmoothStep(0f, 1f, (d - riverHalfWidth * 0.6f) / (outer - riverHalfWidth * 0.6f));
            return Mathf.Min(height, Mathf.Lerp(_riverBedY, height, t));
        }

        /// <summary>
        /// Continues the river valley through the backdrop hills, widening toward the horizon.
        /// localX is chunk-local (beyond ±250), u the distance outward from the terrain edge.
        /// </summary>
        public float ApplyBackdropRiver(float localX, float localZ, float u, float y)
        {
            if (!_active || !_hasRiver) return y;
            float d = RiverDistance(localX, localZ);
            float inner = riverHalfWidth;
            float outer = riverHalfWidth + _riverBank + 10f + u * 0.12f;
            if (d >= outer) return y;

            float bed = _riverWaterY - 1.5f;
            float t = d <= inner ? 0f : Mathf.SmoothStep(0f, 1f, (d - inner) / (outer - inner));
            return Mathf.Min(y, Mathf.Lerp(bed, y, t));
        }

        /// <summary>Deterministic edge style for any chunk index/side, so neighbors can be queried before they exist.</summary>
        private FieldEdgeStyle StyleFor(int worldSeed, int chunkIndex, int side)
        {
            var rng = new SeededRandom(SeededRandom.Combine(SeededRandom.Combine(SeededRandom.Combine(worldSeed, "field_edge"), chunkIndex), side));
            int w0 = Mathf.Max(0, fenceTreesWeight);
            int w1 = w0 + Mathf.Max(0, fenceFarmlandWeight);
            int w2 = w1 + Mathf.Max(0, fenceCornWeight);
            int total = w2 + Mathf.Max(0, streamWeight);
            if (total <= 0) return FieldEdgeStyle.FenceTrees;

            int roll = rng.Range(0, total);
            if (roll < w0) return FieldEdgeStyle.FenceTrees;
            if (roll < w1) return FieldEdgeStyle.FenceFarmland;
            if (roll < w2) return FieldEdgeStyle.FenceCorn;
            return FieldEdgeStyle.Stream;
        }

        public FieldEdgeStyle LeftStyle => _sides != null ? _sides[0].style : FieldEdgeStyle.FenceTrees;
        public FieldEdgeStyle RightStyle => _sides != null ? _sides[1].style : FieldEdgeStyle.FenceTrees;

        // ---------------------------------------------------------------
        // Terrain
        // ---------------------------------------------------------------

        /// <summary>
        /// Field landscape: where the road climbs a hill (or dips) the meadow rises/falls with it and fades back to
        /// the plain away from the road, so it reads as a hill rather than an embankment. Then carves side streams.
        /// </summary>
        public float ApplyTerrain(float localX, int row, float naturalY, float roadX, float roadY)
        {
            if (!_active) return naturalY;

            float delta = roadY - _baseline;
            if (Mathf.Abs(delta) > 0.01f)
            {
                float fromRoad = Mathf.Abs(localX - roadX);
                float follow = 1f - Mathf.SmoothStep(0f, 1f, (fromRoad - hillPlateauHalfWidth) / Mathf.Max(1f, hillFalloff));
                // Always back to plain level at the chunk sides, so the backdrop meadow lines up
                float edgeFade = 1f - Mathf.SmoothStep(0f, 1f, (Mathf.Abs(localX) - (_halfWidth - 60f)) / 50f);
                naturalY += delta * follow * edgeFade;
            }

            SidePlan side = _sides[localX < 0f ? 0 : 1];
            if (side.style != FieldEdgeStyle.Stream) return naturalY;

            row = Mathf.Clamp(row, 0, _rows - 1);
            float profile = StreamProfile(Mathf.Abs(Mathf.Abs(localX) - side.streamX[row]));
            return naturalY - streamDepth * profile * side.streamScale[row];
        }

        private float StreamProfile(float distFromCenter)
        {
            if (distFromCenter <= streamBedHalfWidth) return 1f;
            float t = (distFromCenter - streamBedHalfWidth) / Mathf.Max(0.5f, streamBankWidth);
            return t >= 1f ? 0f : 1f - Mathf.SmoothStep(0f, 1f, t);
        }

        /// <summary>
        /// Paints plowed soil behind farmland fences and muddy stream banks. Restores the single grass layer otherwise.
        /// </summary>
        public void PaintTerrainLayers(TerrainData td, float terrainXOffset)
        {
            if (td == null || !_active) return;
            TerrainLayer[] layers = td.terrainLayers;
            if (layers == null || layers.Length == 0 || layers[0] == null) return;

            int res = td.alphamapResolution;
            bool needsSoil = _sides[0].style == FieldEdgeStyle.FenceFarmland || _sides[1].style == FieldEdgeStyle.FenceFarmland
                             || _sides[0].style == FieldEdgeStyle.Stream || _sides[1].style == FieldEdgeStyle.Stream
                             || _hasRiver;

            if (!needsSoil)
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

            TerrainLayer soil = GetSoilLayer();
            if (soil == null) return;
            if (layers.Length != 2 || layers[1] != soil) td.terrainLayers = new[] { layers[0], soil };

            Vector3 size = td.size;
            var alphas = new float[res, res, 2];
            for (int y = 0; y < res; y++)
            {
                float localZ = y / (res - 1f) * size.z;
                int row = Mathf.Clamp(Mathf.RoundToInt(localZ / _length * (_rows - 1)), 0, _rows - 1);
                for (int x = 0; x < res; x++)
                {
                    float localX = terrainXOffset + x / (res - 1f) * size.x;
                    float absX = Mathf.Abs(localX);
                    SidePlan side = _sides[localX < 0f ? 0 : 1];

                    float soilW = 0f;
                    if (side.style == FieldEdgeStyle.FenceFarmland && absX > fenceLine + 4f && !IsBlocked(localX, localZ))
                    {
                        // Furrows: alternating bare soil and soil with a little green
                        float f = Mathf.Repeat(absX - fenceLine, furrowSpacing) / furrowSpacing;
                        soilW = f < 0.6f ? 1f : 0.5f;
                    }
                    else if (side.style == FieldEdgeStyle.Stream)
                    {
                        float p = StreamProfile(Mathf.Abs(absX - side.streamX[row]) - 1.5f);
                        soilW = Mathf.Clamp01(p * 1.4f) * 0.75f * side.streamScale[row];
                    }

                    // Muddy river banks
                    if (_hasRiver)
                    {
                        float rd = RiverDistance(localX, localZ);
                        float mud = 1f - Mathf.SmoothStep(0f, 1f, (rd - riverHalfWidth) / (riverBankWidth + 3f));
                        soilW = Mathf.Max(soilW, mud * 0.8f);
                    }

                    alphas[y, x, 0] = 1f - soilW;
                    alphas[y, x, 1] = soilW;
                }
            }
            td.SetAlphamaps(0, 0, alphas);
        }

        // ---------------------------------------------------------------
        // Decorations
        // ---------------------------------------------------------------

        /// <summary>
        /// Builds fences, tree rows, corn, hay bales, stream water and edge walls. Call after the terrain is shaped.
        /// </summary>
        public void Generate(Transform parent, Terrain terrain)
        {
            ClearContainer(parent);
            DestroyOwnedMeshes();
            if (parent == null) return;

            Chunk chunk = GetChunk();
            if (chunk == null || !chunk.IsField || !fieldEdgesEnabled) return;

            if (!_active)
            {
                int rows = terrain != null && terrain.terrainData != null ? terrain.terrainData.heightmapResolution : 129;
                if (!BuildPlan(rows, chunk.chunkLength, chunk.GetComponentInChildren<Road.RoadSpline>())) return;
            }

            var container = new GameObject(CONTAINER_NAME);
            container.transform.SetPositionAndRotation(chunk.transform.position, chunk.transform.rotation);
            container.transform.SetParent(parent, true);

            Material[] materials = new Material[SubCount];
            materials[SubWood] = ChunkBoundaryGenerator.GetSharedMaterial(postColor);
            materials[SubWire] = ChunkBoundaryGenerator.GetSharedMaterial(wireColor);
            materials[SubCrown] = ChunkBoundaryGenerator.GetSharedMaterial(crownColor);
            materials[SubCorn] = ChunkBoundaryGenerator.GetSharedMaterial(cornColor);
            materials[SubTassel] = ChunkBoundaryGenerator.GetSharedMaterial(tasselColor);
            materials[SubHay] = ChunkBoundaryGenerator.GetSharedMaterial(hayColor);
            Material trunkMat = ChunkBoundaryGenerator.GetSharedMaterial(trunkColor);

            int seedBase = SeededRandom.Combine(chunk.ChunkSeed != 0 ? chunk.ChunkSeed : chunk.WorldSeed, "field_deco");

            for (int s = 0; s < 2; s++)
            {
                float sign = s == 0 ? -1f : 1f;
                SidePlan side = _sides[s];
                var rng = new SeededRandom(SeededRandom.Combine(seedBase, s));
                var mb = new MeshBuilder(SubCount + 1); // last submesh = tree trunks
                var sideGo = new GameObject(s == 0 ? "Edge_Left_" + side.style : "Edge_Right_" + side.style);
                sideGo.transform.SetParent(container.transform, false);

                switch (side.style)
                {
                    case FieldEdgeStyle.FenceTrees:
                        BuildFence(mb, sideGo, chunk, terrain, sign, rng);
                        BuildTreeRow(mb, chunk, terrain, sign, rng);
                        break;
                    case FieldEdgeStyle.FenceFarmland:
                        BuildFence(mb, sideGo, chunk, terrain, sign, rng);
                        BuildHayBales(mb, chunk, terrain, sign, rng);
                        break;
                    case FieldEdgeStyle.FenceCorn:
                        BuildFence(mb, sideGo, chunk, terrain, sign, rng);
                        BuildCorn(mb, chunk, terrain, sign, rng);
                        break;
                    case FieldEdgeStyle.Stream:
                        BuildStreamWater(sideGo, chunk, terrain, sign, side);
                        break;
                }

                if (mb.VertexCount > 0)
                {
                    Mesh mesh = mb.ToMesh($"FieldEdge_{(s == 0 ? "L" : "R")}_Chunk{chunk.ChunkIndex}");
                    _ownedMeshes.Add(mesh);
                    sideGo.AddComponent<MeshFilter>().sharedMesh = mesh;
                    var mr = sideGo.AddComponent<MeshRenderer>();
                    var mats = new Material[SubCount + 1];
                    materials.CopyTo(mats, 0);
                    mats[SubCount] = trunkMat;
                    mr.sharedMaterials = mats;
                }
            }

            if (_hasRiver) BuildRiverAndBridge(container.transform, chunk, terrain);

            BuildEdgeWalls(container.transform, chunk);
        }

        private void BuildRiverAndBridge(Transform container, Chunk chunk, Terrain terrain)
        {
            float waterY = _riverWaterY;
            Material waterMat = GetWaterMaterial(waterColor);

            // --- Water: one strip along the river center line, from horizon to horizon (widening slightly far out)
            var verts = new List<Vector3>();
            var tris = new List<int>();
            float reach = _halfWidth + 2600f;
            for (float x = -reach; x <= reach + 0.01f; x += 10f)
            {
                float u = Mathf.Max(0f, Mathf.Abs(x) - _halfWidth);
                float half = (riverHalfWidth + 1.5f + u * 0.04f) / Mathf.Max(0.5f, _riverCos);
                float zc = RiverCenterZ(x);
                int i = verts.Count;
                verts.Add(new Vector3(x, waterY, zc - half));
                verts.Add(new Vector3(x, waterY, zc + half));
                if (i > 0)
                {
                    tris.Add(i - 2); tris.Add(i - 1); tris.Add(i + 1);
                    tris.Add(i - 2); tris.Add(i + 1); tris.Add(i);
                }
            }

            var waterMesh = new Mesh { name = "River_Water" };
            waterMesh.SetVertices(verts);
            waterMesh.SetTriangles(tris, 0);
            waterMesh.RecalculateNormals();
            waterMesh.RecalculateBounds();
            _ownedMeshes.Add(waterMesh);

            var water = new GameObject("River_Water");
            water.transform.SetParent(container, false);
            water.AddComponent<MeshFilter>().sharedMesh = waterMesh;
            var wmr = water.AddComponent<MeshRenderer>();
            wmr.sharedMaterial = waterMat;
            wmr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            // --- Bridge: along the road wherever it passes over the channel
            var spline = chunk.GetComponentInChildren<Road.RoadSpline>();
            if (spline == null) return;
            var roadGen = chunk.GetComponentInChildren<Road.RoadGenerator>();
            float roadHalf = roadGen != null ? roadGen.GetTotalHalfWidth() : 7f;
            float band = riverHalfWidth + _riverBank + 1f + roadHalf * Mathf.Abs(_riverTan);

            float zStart = -1f, zEnd = -1f, zDeepest = _riverZ, minDist = float.MaxValue;
            for (float z = Mathf.Max(1f, _riverZ - 150f); z <= Mathf.Min(_length - 1f, _riverZ + 150f); z += 0.5f)
            {
                Vector3 p = spline.GetPointAtZ(z);
                float d = RiverDistance(p.x, z);
                if (d < band)
                {
                    if (zStart < 0f) zStart = z;
                    zEnd = z;
                }
                if (d < minDist) { minDist = d; zDeepest = z; }
            }
            if (zStart < 0f) return;

            // Let the deck rest on the abutments a little past the banks
            zStart = Mathf.Max(0.5f, zStart - 3f);
            zEnd = Mathf.Min(_length - 0.5f, zEnd + 3f);

            var bridge = new GameObject("Bridge");
            bridge.transform.SetParent(container, false);
            var mb = new MeshBuilder(2); // 0 = concrete, 1 = deck

            int segments = Mathf.Max(1, Mathf.CeilToInt((zEnd - zStart) / 3f));
            float segLen = (zEnd - zStart) / segments;
            for (int i = 0; i < segments; i++)
            {
                Vector3 p0 = spline.GetPointAtZ(zStart + i * segLen);
                Vector3 p1 = spline.GetPointAtZ(zStart + (i + 1) * segLen);
                Vector3 mid = (p0 + p1) * 0.5f;
                float len = Vector3.Distance(p0, p1);
                Quaternion rot = Quaternion.LookRotation(p1 - p0, Vector3.up);
                Vector3 right = rot * Vector3.right;

                // Deck slab under the road surface (the road mesh itself is the driving surface)
                mb.Box(mid + Vector3.up * -0.95f, new Vector3(roadHalf * 2f + 1.2f, 1.3f, len + 0.05f), rot, 1);

                // Concrete parapets on both edges, with colliders so vehicles can't drive off
                for (int s = -1; s <= 1; s += 2)
                {
                    Vector3 c = mid + right * (s * (roadHalf + 0.35f)) + Vector3.up * 0.35f;
                    var size = new Vector3(0.45f, 1.1f, len + 0.05f);
                    mb.Box(c, size, rot, 0);

                    var parapet = new GameObject("Parapet");
                    parapet.transform.SetParent(bridge.transform, false);
                    parapet.transform.localPosition = c;
                    parapet.transform.localRotation = rot;
                    parapet.AddComponent<BoxCollider>().size = size;
                }
            }

            // Abutments: walls at both ends, from under the deck down into the banks
            float[] ends = { zStart + 1f, zEnd - 1f };
            for (int i = 0; i < ends.Length; i++)
            {
                Vector3 p = spline.GetPointAtZ(ends[i]);
                Vector3 ahead = spline.GetPointAtZ(Mathf.Min(_length, ends[i] + 1f));
                Quaternion rot = Quaternion.LookRotation(ahead - p, Vector3.up);
                mb.Box(p + Vector3.up * -4.6f, new Vector3(roadHalf * 2f + 1.6f, 6f, 2f), rot, 0);
            }

            // Piers on long spans: one under the river center, more every pierSpacing toward the banks
            // (high bridges over deep gorges get several tall piers), each standing on the ground below
            if (zEnd - zStart > pierMinSpan)
            {
                float spacing = Mathf.Max(6f, pierSpacing);
                for (int k = -8; k <= 8; k++)
                {
                    float z = zDeepest + k * spacing;
                    if (z < zStart + 5f || z > zEnd - 5f) continue;

                    Vector3 p = spline.GetPointAtZ(z);
                    float ground = k == 0 ? _riverBedY : Mathf.Min(GroundY(chunk, terrain, p.x, z), p.y);
                    float deckBottom = p.y - 1.6f;
                    if (deckBottom - ground < 3f) continue; // close to the bank: the deck rests on the ground anyway

                    Vector3 ahead = spline.GetPointAtZ(Mathf.Min(_length, z + 1f));
                    Quaternion rot = Quaternion.LookRotation(ahead - p, Vector3.up);
                    float bottom = ground - 0.5f;
                    float h = deckBottom - bottom;
                    mb.Box(new Vector3(p.x, bottom + h * 0.5f, p.z), new Vector3(roadHalf * 2f - 1f, h, 1.8f), rot, 0);
                }
            }

            Mesh mesh = mb.ToMesh($"Bridge_Chunk{chunk.ChunkIndex}");
            _ownedMeshes.Add(mesh);
            bridge.AddComponent<MeshFilter>().sharedMesh = mesh;
            bridge.AddComponent<MeshRenderer>().sharedMaterials = new[]
            {
                ChunkBoundaryGenerator.GetSharedMaterial(concreteColor),
                ChunkBoundaryGenerator.GetSharedMaterial(deckColor)
            };
        }

        private void BuildFence(MeshBuilder mb, GameObject sideGo, Chunk chunk, Terrain terrain, float sign, SeededRandom rng)
        {
            float x = sign * fenceLine;
            int posts = Mathf.FloorToInt(_length / postSpacing);
            float runStart = -1f, runEnd = -1f, runY = 0f;
            int runCount = 0;
            Vector3 prevTop = Vector3.zero;
            bool hasPrev = false;

            for (int i = 0; i <= posts; i++)
            {
                float z = Mathf.Min(_length - 0.3f, 0.3f + i * postSpacing);
                bool cleared = IsBlocked(x, z);
                bool gap = cleared || rng.Chance(fenceGapChance);

                float ground = GroundY(chunk, terrain, x, z);
                if (!cleared)
                {
                    // Post (partly buried), some leaning
                    Quaternion tilt = rng.Chance(leaningPostChance)
                        ? Quaternion.Euler(rng.Range(-25f, 25f), 0f, rng.Range(-25f, 25f))
                        : Quaternion.Euler(0f, rng.Range(-8f, 8f), 0f);
                    float postH = fenceHeight + 0.4f;
                    mb.Box(new Vector3(x, ground + postH * 0.5f - 0.4f, z), new Vector3(0.14f, postH, 0.14f), tilt, SubWood);
                }

                Vector3 basePoint = new Vector3(x, ground, z);
                if (hasPrev && !gap)
                {
                    // Three barbed wires between this post and the previous one
                    Vector3 a = prevTop, b = basePoint;
                    Vector3 mid = (a + b) * 0.5f;
                    float len = Vector3.Distance(a, b);
                    Quaternion rot = Quaternion.LookRotation(b - a);
                    for (int wIdx = 0; wIdx < 3; wIdx++)
                    {
                        float h = 0.45f + wIdx * 0.4f;
                        float sag = rng.Range(-0.05f, 0.03f);
                        mb.BoxLow(mid + Vector3.up * (h + sag), new Vector3(0.035f, 0.035f, len), rot, SubWire);
                    }

                    if (runStart < 0f) { runStart = a.z; runY = 0f; runCount = 0; }
                    runEnd = b.z;
                    runY += ground;
                    runCount++;
                }
                else if (runStart >= 0f)
                {
                    AddFenceCollider(sideGo, x, runStart, runEnd, runY / Mathf.Max(1, runCount));
                    runStart = -1f;
                }

                prevTop = basePoint;
                hasPrev = !cleared;
            }

            if (runStart >= 0f) AddFenceCollider(sideGo, x, runStart, runEnd, runY / Mathf.Max(1, runCount));
        }

        private void AddFenceCollider(GameObject sideGo, float x, float z0, float z1, float groundY)
        {
            if (z1 - z0 < 1f) return;
            var col = sideGo.AddComponent<BoxCollider>();
            col.center = new Vector3(x, groundY + fenceHeight * 0.5f, (z0 + z1) * 0.5f);
            col.size = new Vector3(0.3f, fenceHeight, z1 - z0);
        }

        /// <summary>
        /// Tree belt behind the fence: a front line of poplars and several staggered rows behind it,
        /// more oaks toward the back. Belt depth comes from world-continuous noise (lines up across chunks).
        /// </summary>
        private void BuildTreeRow(MeshBuilder mb, Chunk chunk, Terrain terrain, float sign, SeededRandom rng)
        {
            int sideIdx = sign < 0f ? 0 : 1;
            float noise = new SeededRandom(SeededRandom.Combine(SeededRandom.Combine(chunk.WorldSeed, "tree_belt"), sideIdx)).Range(0f, 1000f);
            float rowSpacing = Mathf.Max(3f, beltRowSpacing);

            float z = rng.Range(1f, maxTreeSpacing);
            while (z < _length - 1f)
            {
                float globalZ = chunk.ChunkIndex * _length + z;
                float depth = Mathf.Lerp(minTreeBeltDepth, maxTreeBeltDepth, Mathf.PerlinNoise(noise, globalZ * 0.004f));

                int row = 0;
                for (float d = 0f; d <= depth; d += rowSpacing, row++)
                {
                    bool front = row == 0;
                    // Staggered rows: every other row is shifted half a spacing along the road
                    float tz = z + (row % 2 == 1 ? maxTreeSpacing * 0.5f : 0f) + rng.Range(-1.5f, 1.5f);
                    float x = sign * (fenceLine + treeRowOffset + d + rng.Range(front ? -1.5f : -2.2f, front ? 1.5f : 2.2f));

                    if (rng.Chance(front ? treeGapChance : beltGapChance)) continue;
                    if (tz < 0.5f || tz > _length - 0.5f || IsBlocked(x, tz) || Mathf.Abs(x) > edgeWallDistance - 3f) continue;

                    AddTree(mb, chunk, terrain, x, tz, rng, front ? 0.15f : beltBroadChance, front ? 1f : rng.Range(0.85f, 1.2f));
                }
                z += rng.Range(minTreeSpacing, maxTreeSpacing);
            }
        }

        private void AddTree(MeshBuilder mb, Chunk chunk, Terrain terrain, float x, float z, SeededRandom rng, float broadChance, float heightScale)
        {
            float ground = GroundY(chunk, terrain, x, z);
            float h = rng.Range(minTreeHeight, maxTreeHeight) * heightScale;
            bool broad = rng.Chance(broadChance); // oak among the poplars
            float crownW = broad ? rng.Range(5f, 7f) : rng.Range(2.4f, 3.4f);
            float crownH = broad ? h * 0.45f : h * 0.72f;
            float trunkH = h - crownH;
            Quaternion lean = Quaternion.Euler(rng.Range(-3f, 3f), rng.Range(0f, 90f), rng.Range(-3f, 3f));

            mb.Box(new Vector3(x, ground + trunkH * 0.5f, z), new Vector3(0.45f, trunkH + 0.5f, 0.45f), lean, SubCount);
            Vector3 crownCenter = new Vector3(x, ground + trunkH + crownH * 0.5f, z);
            mb.Box(crownCenter, new Vector3(crownW, crownH, crownW), lean, SubCrown);
            mb.Box(crownCenter + Vector3.up * (crownH * 0.55f), new Vector3(crownW * 0.6f, crownH * 0.25f, crownW * 0.6f), lean, SubCrown);
        }

        private void BuildHayBales(MeshBuilder mb, Chunk chunk, Terrain terrain, float sign, SeededRandom rng)
        {
            int count = rng.Range(minHayBales, maxHayBales + 1);
            for (int i = 0; i < count; i++)
            {
                float x = sign * rng.Range(fenceLine + 8f, edgeWallDistance - 10f);
                float z = rng.Range(10f, _length - 10f);
                if (IsBlocked(x, z)) continue;

                float ground = GroundY(chunk, terrain, x, z);
                Quaternion rot = Quaternion.Euler(0f, rng.Range(0f, 180f), 0f);
                var size = new Vector3(1.3f, 1.0f, 2.3f);
                mb.Box(new Vector3(x, ground + size.y * 0.5f - 0.05f, z), size, rot, SubHay);
                if (rng.Chance(0.35f)) // small stack
                {
                    mb.Box(new Vector3(x, ground + size.y * 1.5f - 0.05f, z), size, rot * Quaternion.Euler(0f, rng.Range(-15f, 15f), 0f), SubHay);
                }
            }
        }

        private void BuildCorn(MeshBuilder mb, Chunk chunk, Terrain terrain, float sign, SeededRandom rng)
        {
            // Strip depth from a world-continuous noise, so the corn edge lines up with a neighboring corn chunk
            int sideIdx = sign < 0f ? 0 : 1;
            float noise = new SeededRandom(SeededRandom.Combine(SeededRandom.Combine(chunk.WorldSeed, "corn_depth"), sideIdx)).Range(0f, 1000f);
            float start = fenceLine + 2.5f;

            for (float z = 0.7f; z < _length; z += cornPlantSpacing)
            {
                float globalZ = chunk.ChunkIndex * _length + z;
                float depth = Mathf.Lerp(minCornDepth, maxCornDepth, Mathf.PerlinNoise(noise, globalZ * 0.004f));
                float end = Mathf.Min(start + depth, edgeWallDistance - 2f);

                for (float d = start; d < end; d += cornRowSpacing)
                {
                    float x = sign * (d + rng.Range(-0.3f, 0.3f));
                    float pz = z + rng.Range(-0.3f, 0.3f);
                    if (IsBlocked(x, pz)) continue;

                    float ground = GroundY(chunk, terrain, x, pz);
                    float h = rng.Range(minCornHeight, maxCornHeight);
                    float w = rng.Range(0.9f, 1.3f);
                    Quaternion rot = Quaternion.Euler(rng.Range(-4f, 4f), rng.Range(0f, 90f), rng.Range(-4f, 4f));
                    mb.BoxLow(new Vector3(x, ground + h * 0.5f, pz), new Vector3(w, h, w), rot, SubCorn);
                    mb.BoxLow(new Vector3(x, ground + h + 0.15f, pz), new Vector3(0.35f, 0.45f, 0.35f), rot, SubTassel);
                }
            }
        }

        private void BuildStreamWater(GameObject sideGo, Chunk chunk, Terrain terrain, float sign, SidePlan side)
        {
            const float step = 5f;
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            int prevL = -1, prevR = -1;

            for (float z = 0f; z <= _length + 0.01f; z += step)
            {
                float zc = Mathf.Min(z, _length);
                float f = zc / _length * (_rows - 1);
                int i0 = Mathf.FloorToInt(f), i1 = Mathf.Min(i0 + 1, _rows - 1);
                float t = f - i0;
                float cx = Mathf.Lerp(side.streamX[i0], side.streamX[i1], t);
                float scale = Mathf.Lerp(side.streamScale[i0], side.streamScale[i1], t);

                // Where the side stream meets the crossing river, the river's own water takes over
                if (scale < 0.05f || InRiverBand(sign * cx, zc, 0f))
                {
                    prevL = prevR = -1;
                    continue;
                }

                // Water surface a little under the lower bank top, never above the land
                float halfW = (streamBedHalfWidth + streamBankWidth * 0.45f) * scale;
                float bankL = GroundY(chunk, terrain, sign * (cx - streamBedHalfWidth - streamBankWidth - 1f), zc);
                float bankR = GroundY(chunk, terrain, sign * (cx + streamBedHalfWidth + streamBankWidth + 1f), zc);
                float y = Mathf.Min(bankL, bankR) - 0.6f * scale - 0.25f;

                // In chunk-local space; "inner" vertex is the one closer to the road
                int inner = vertices.Count;
                vertices.Add(new Vector3(sign * (cx - halfW), y, zc));
                vertices.Add(new Vector3(sign * (cx + halfW), y, zc));

                if (prevL >= 0)
                {
                    // Face up on both sides of the road (mirror flips winding)
                    if (sign > 0f)
                    {
                        triangles.Add(prevL); triangles.Add(inner); triangles.Add(prevR);
                        triangles.Add(prevR); triangles.Add(inner); triangles.Add(inner + 1);
                    }
                    else
                    {
                        triangles.Add(prevL); triangles.Add(prevR); triangles.Add(inner);
                        triangles.Add(prevR); triangles.Add(inner + 1); triangles.Add(inner);
                    }
                }
                prevL = inner;
                prevR = inner + 1;
            }

            if (triangles.Count == 0) return;

            var mesh = new Mesh { name = "Stream_Water" };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            _ownedMeshes.Add(mesh);

            var water = new GameObject("Stream_Water");
            water.transform.SetParent(sideGo.transform, false);
            water.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = water.AddComponent<MeshRenderer>();
            mr.sharedMaterial = GetWaterMaterial(waterColor);
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        private void BuildEdgeWalls(Transform container, Chunk chunk)
        {
            // Open plains have no cliffs: invisible walls stop the player from walking out onto the backdrop
            var walls = new GameObject("Field_EdgeWalls");
            walls.transform.SetParent(container, false);
            for (int s = 0; s < 2; s++)
            {
                float sign = s == 0 ? -1f : 1f;
                var col = walls.AddComponent<BoxCollider>();
                col.center = new Vector3(sign * edgeWallDistance, chunk.baseElevation + 10f, _length * 0.5f);
                col.size = new Vector3(1f, 40f, _length);
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

        private void CollectKeepouts(Chunk chunk)
        {
            _keepouts.Clear();
            var pois = chunk.GetComponentsInChildren<PointOfInterest>(true);
            for (int i = 0; i < pois.Length; i++)
            {
                if (pois[i] == null) continue;
                Vector3 local = chunk.transform.InverseTransformPoint(pois[i].transform.position);
                _keepouts.Add(new Vector2(local.x, local.z));
            }
        }

        /// <summary>No fences/trees/corn/hay/soil here: POI clearings and the river crossing.</summary>
        private bool IsBlocked(float localX, float localZ)
        {
            return NearKeepout(localX, localZ) || InRiverBand(localX, localZ, 2f);
        }

        private bool NearKeepout(float localX, float localZ)
        {
            float r2 = poiClearance * poiClearance;
            for (int i = 0; i < _keepouts.Count; i++)
            {
                float dx = localX - _keepouts[i].x;
                float dz = localZ - _keepouts[i].y;
                if (dx * dx + dz * dz < r2) return true;
            }
            return false;
        }

        /// <summary>Terrain height at a chunk-local XZ, returned in chunk-local Y.</summary>
        private static float GroundY(Chunk chunk, Terrain terrain, float localX, float localZ)
        {
            if (terrain == null) return chunk.baseElevation;
            Vector3 world = chunk.transform.TransformPoint(new Vector3(localX, 0f, localZ));
            float worldY = terrain.SampleHeight(world) + terrain.transform.position.y;
            return worldY - chunk.transform.position.y;
        }

        private TerrainLayer GetSoilLayer()
        {
#if UNITY_EDITOR
            // Edit mode writes into the prefab's TerrainData asset, so the layer must be an asset too
            var asset = UnityEditor.AssetDatabase.LoadAssetAtPath<TerrainLayer>("Assets/Prefabs/Chunks/TerrainLayers/Layer_Wasteland.terrainlayer");
            if (asset != null) return asset;
#endif
            if (s_runtimeSoilLayer == null)
            {
                var tex = new Texture2D(4, 4, TextureFormat.RGBA32, false) { name = "Tex_Soil_Runtime" };
                var pixels = new Color[16];
                for (int i = 0; i < pixels.Length; i++) pixels[i] = soilColor;
                tex.SetPixels(pixels);
                tex.Apply();
                s_runtimeSoilLayer = new TerrainLayer { name = "Layer_Soil_Runtime", diffuseTexture = tex, tileSize = new Vector2(12f, 12f) };
            }
            return s_runtimeSoilLayer;
        }

        private static Material GetWaterMaterial(Color color)
        {
            if (s_waterMaterial != null) return s_waterMaterial;
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            s_waterMaterial = new Material(shader) { name = "Mat_Stream_Runtime", color = color };
            if (s_waterMaterial.HasProperty("_BaseColor")) s_waterMaterial.SetColor("_BaseColor", color);
            if (s_waterMaterial.HasProperty("_Smoothness")) s_waterMaterial.SetFloat("_Smoothness", 0.9f);
            return s_waterMaterial;
        }

        private static void ClearContainer(Transform parent)
        {
            if (parent == null) return;
            Transform old = parent.Find(CONTAINER_NAME);
            if (old == null) return;

            if (!Application.isPlaying)
            {
                DestroyImmediate(old.gameObject);
            }
            else
            {
                old.name = CONTAINER_NAME + "_Destroying";
                old.gameObject.SetActive(false);
                Destroy(old.gameObject);
            }
        }

        private void DestroyOwnedMeshes()
        {
            for (int i = 0; i < _ownedMeshes.Count; i++)
            {
                if (_ownedMeshes[i] == null) continue;
                if (Application.isPlaying) Destroy(_ownedMeshes[i]);
                else DestroyImmediate(_ownedMeshes[i]);
            }
            _ownedMeshes.Clear();
        }

        private void OnDestroy()
        {
            DestroyOwnedMeshes();
        }

        /// <summary>
        /// Collects many boxes into one mesh with several submeshes (one draw call per material).
        /// </summary>
        private sealed class MeshBuilder
        {
            private readonly List<Vector3> _vertices = new List<Vector3>();
            private readonly List<Vector3> _normals = new List<Vector3>();
            private readonly List<int>[] _submeshes;

            private static readonly Vector3[] FaceNormals = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
            private static readonly Vector3[] FaceU = { Vector3.forward, Vector3.forward, Vector3.right, Vector3.right, Vector3.up, Vector3.up };

            public MeshBuilder(int submeshCount)
            {
                _submeshes = new List<int>[submeshCount];
                for (int i = 0; i < submeshCount; i++) _submeshes[i] = new List<int>();
            }

            public int VertexCount => _vertices.Count;

            /// <summary>Flat-shaded box (24 vertices).</summary>
            public void Box(Vector3 center, Vector3 size, Quaternion rot, int submesh)
            {
                Vector3 half = size * 0.5f;
                for (int f = 0; f < 6; f++)
                {
                    Vector3 n = FaceNormals[f];
                    Vector3 u = FaceU[f];
                    Vector3 v = Vector3.Cross(u, n); // Cross(v, u) == n -> clockwise (front) when seen from outside
                    float hn = Mathf.Abs(Vector3.Dot(n, half));
                    float hu = Mathf.Abs(Vector3.Dot(u, half));
                    float hv = Mathf.Abs(Vector3.Dot(v, half));

                    int start = _vertices.Count;
                    Vector3 worldN = rot * n;
                    AddVertex(center + rot * (n * hn - u * hu - v * hv), worldN);
                    AddVertex(center + rot * (n * hn - u * hu + v * hv), worldN);
                    AddVertex(center + rot * (n * hn + u * hu + v * hv), worldN);
                    AddVertex(center + rot * (n * hn + u * hu - v * hv), worldN);
                    AddQuad(submesh, start, start + 1, start + 2, start + 3);
                }
            }

            /// <summary>Cheap box with 8 shared corners (soft normals) for very large counts, e.g. corn.</summary>
            public void BoxLow(Vector3 center, Vector3 size, Quaternion rot, int submesh)
            {
                Vector3 half = size * 0.5f;
                int start = _vertices.Count;
                for (int i = 0; i < 8; i++)
                {
                    var corner = new Vector3((i & 1) != 0 ? 1f : -1f, (i & 2) != 0 ? 1f : -1f, (i & 4) != 0 ? 1f : -1f);
                    AddVertex(center + rot * Vector3.Scale(corner, half), (rot * corner).normalized);
                }

                for (int f = 0; f < 6; f++)
                {
                    Vector3 n = FaceNormals[f];
                    Vector3 u = FaceU[f];
                    Vector3 v = Vector3.Cross(u, n);
                    AddQuad(submesh,
                        start + CornerIndex(n - u - v),
                        start + CornerIndex(n - u + v),
                        start + CornerIndex(n + u + v),
                        start + CornerIndex(n + u - v));
                }
            }

            private static int CornerIndex(Vector3 signs)
            {
                return (signs.x > 0f ? 1 : 0) + (signs.y > 0f ? 2 : 0) + (signs.z > 0f ? 4 : 0);
            }

            private void AddVertex(Vector3 position, Vector3 normal)
            {
                _vertices.Add(position);
                _normals.Add(normal);
            }

            private void AddQuad(int submesh, int a, int b, int c, int d)
            {
                List<int> tris = _submeshes[submesh];
                tris.Add(a); tris.Add(b); tris.Add(c);
                tris.Add(a); tris.Add(c); tris.Add(d);
            }

            public Mesh ToMesh(string name)
            {
                var mesh = new Mesh { name = name };
                if (_vertices.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                mesh.SetVertices(_vertices);
                mesh.SetNormals(_normals);
                mesh.subMeshCount = _submeshes.Length;
                for (int i = 0; i < _submeshes.Length; i++) mesh.SetTriangles(_submeshes[i], i);
                mesh.RecalculateBounds();
                return mesh;
            }
        }
    }
}
