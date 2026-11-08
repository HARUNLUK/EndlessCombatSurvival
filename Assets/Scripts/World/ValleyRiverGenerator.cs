using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using EndlessSurvival.World.Road;

namespace EndlessSurvival.World
{
    /// <summary>A river whose valley continues through the backdrop mountains (field crossing river, river valley).</summary>
    public interface IBackdropRiver
    {
        /// <summary>Lowers a backdrop vertex (chunk-local x/z beyond the terrain edge, u = distance from the strip start) into the river valley.</summary>
        float ApplyBackdropRiver(float localX, float localZ, float u, float y);
    }

    /// <summary>
    /// Nehir Vadisi tipinin ayarları. Bir tip profilinde <see cref="enabled"/> açıksa o tip Orman arazi motorunu kullanır ve
    /// yolun yanında, yolla paralel akan geniş bir nehir alır (<see cref="ValleyRiverGenerator"/>).
    /// </summary>
    [System.Serializable]
    public class RiverValleySettings
    {
        [Tooltip("Açıksa bu tip Nehir Vadisi'dir: yolun yanında yolla birlikte akan geniş bir nehir")]
        public bool enabled = false;

        [Header("Karşıya Geçiş (köprü)")]
        [Tooltip("Bölümün ortadaki chunk'larında nehrin yolun altından (köprüyle) öbür tarafa geçme ihtimali. İlk ve son chunk'ta geçiş olmaz.")]
        [Range(0f, 1f)] public float crossingChance = 0.35f;
        [Tooltip("Geçişin chunk içindeki Z aralığı (nehrin yolu kestiği yer)")]
        public float crossingMinZ = 210f;
        public float crossingMaxZ = 290f;
        [Tooltip("Nehrin bir yandan öbür yana geçtiği S kıvrımının Z uzunluğu (küçük = daha dik açıyla, daha kısa köprü)")]
        public float crossingLength = 150f;
        [Tooltip("Köprü altında nehrin daraldığı su genişliği")]
        public float crossingWidth = 34f;
        [Tooltip("Köprü altında yol ile su yüzeyi arasındaki en az mesafe")]
        public float crossingMinDrop = 5f;

        [Header("Nehir")]
        [Tooltip("Su yüzeyinin genişliği (chunk birleşimlerinde ortak değer, chunk ortasında gürültüyle değişir)")]
        public float minWidth = 40f;
        public float maxWidth = 80f;
        [Range(0f, 0.5f)] [Tooltip("Chunk ortasında genişliğin daralıp genişleme oranı")]
        public float widthNoise = 0.18f;
        [Tooltip("Yol kenarından su kıyısına mesafe")]
        public float minGap = 6f;
        public float maxGap = 25f;
        [Tooltip("Yol kotundan (20m) su yüzeyine iniş")]
        public float minDrop = 3f;
        public float maxDrop = 7f;
        [Tooltip("Nehrin ortasındaki su derinliği. Yüzme yok: oyuncu suyun içinde yürür, bu yüzden sığ tutulur.")]
        public float maxDepth = 1.3f;
        [Tooltip("Kıyı şevinin yatay genişliği (yol tarafında yol ile kıyı arası mesafeyle sınırlanır)")]
        public float minBank = 4f;
        public float maxBank = 16f;

        [Header("Bölüm Başı / Sonu")]
        [Tooltip("Bölümün ilk chunk'ında nehir yandaki dağların arasından gelir: kenardan girdiği Z ve yola katıldığı Z. Son chunk'ta tersi (500 - değer).")]
        public float bendSideZ = 120f;
        public float bendJoinZ = 300f;
        [Tooltip("Nehrin dağların arasından geçtiği yerde kıyı şevi (uçurumu yaran vadi)")]
        public float bendBank = 24f;

        [Header("Görünüm")]
        [Tooltip("Su rengi (alfa = saydamlık)")]
        public Color waterColor = new Color(0.16f, 0.34f, 0.40f, 0.84f);
        [Tooltip("Akıntı dokusunun akış hızı (m/sn, sadece görsel)")]
        public float flowSpeed = 1.1f;
        [Tooltip("Akıntı dokusunun bir tekrarının metre boyu")]
        public float flowTile = 14f;
        [Range(0f, 1f)] [Tooltip("Arazinin doğal dalgalanmasından vadide kalan oran (vadi tabanı daha düz)")]
        public float ambientScale = 0.45f;
    }

    /// <summary>
    /// Nehir Vadisi: a wide river running beside the road. Its course is a polyline in chunk-local space (river side,
    /// width, distance to the road and water level come from seam values shared with the neighbor, so the river runs on
    /// across chunks); a middle chunk may take it under the road to the other side (bridge), the first and last chunk of
    /// a run bring it in from / take it out to the mountains at the side. Shapes the terrain (channel and banks, after the
    /// road blend), pushes the side cliffs beyond the far bank, paints muddy banks, builds the water surface (with a
    /// scrolling flow texture) and the bridge. No water physics: the river is shallow, the player and vehicles wade.
    /// </summary>
    [DisallowMultipleComponent]
    public class ValleyRiverGenerator : MonoBehaviour, IBackdropRiver
    {
        public const string CONTAINER_NAME = "River_Valley";
        /// <summary>Distance from the road center line the river layout is measured from (largest road half width + margin).</summary>
        private const float RoadMargin = 9f;
        private const float FarReach = 2600f;

        public Color concreteColor = new Color(0.56f, 0.56f, 0.54f);
        public Color deckColor = new Color(0.40f, 0.40f, 0.39f);
        [Tooltip("Köprü boyu bundan uzunsa ayak konur")]
        public float pierMinSpan = 18f;
        public float pierSpacing = 16f;

        private struct RiverPoint
        {
            public Vector2 p;      // chunk-local x, z
            public float half;     // half width of the water surface
            public float bank;     // bank slope width
            public float waterY;   // chunk-local water surface height
            public float v;        // along-river distance (flow texture), continuous across chunks
            public bool parallel;  // beside the road (not a bend through the side mountains)
        }

        private readonly List<RiverPoint> _points = new List<RiverPoint>();
        private readonly List<Mesh> _ownedMeshes = new List<Mesh>();
        private bool _active;
        private RiverValleySettings _st;
        private int _rows;
        private float _length = 500f, _xOffset = -250f, _width = 500f, _halfWidth = 250f;
        private float[] _dist, _half, _bank, _water;   // per heightmap sample (row * rows + col)
        private float[] _outerL, _outerR;              // per row: how far out the river's far bank reaches on each side (0 = none)
        private float _roadHalf = 8f;
        private bool _hasCrossing;

        private static Material s_waterMaterial;
        private static Texture2D s_flowTexture;
        private static TerrainLayer s_runtimeMudLayer;

        public bool IsActive => _active;
        public bool HasCrossing => _active && _hasCrossing;

        // ---------------------------------------------------------------
        // Seam values (shared by both chunks at a seam)
        // ---------------------------------------------------------------

        /// <summary>Water half width, road edge -> shore gap and road -> water drop at seam i (world seed only).</summary>
        public static void SeamValues(int worldSeed, int seam, RiverValleySettings st, out float half, out float gap, out float drop)
        {
            var rng = new SeededRandom(SeededRandom.Combine(SeededRandom.Combine(worldSeed, "river_valley_seam"), seam));
            half = rng.Range(st.minWidth, st.maxWidth) * 0.5f;
            gap = rng.Range(st.minGap, st.maxGap);
            drop = rng.Range(st.minDrop, st.maxDrop);
        }

        /// <summary>Whether a middle chunk of a river valley run takes the river under the road (world seed only).</summary>
        public static bool RollsCrossing(int worldSeed, int chunkIndex, float chance)
        {
            return new SeededRandom(SeededRandom.Combine(SeededRandom.Combine(worldSeed, "river_cross"), chunkIndex)).Chance(chance);
        }

        public static float RollCrossingZ(int worldSeed, int chunkIndex, RiverValleySettings st)
        {
            return new SeededRandom(SeededRandom.Combine(SeededRandom.Combine(worldSeed, "river_cross_z"), chunkIndex))
                .Range(st.crossingMinZ, st.crossingMaxZ);
        }

        // ---------------------------------------------------------------
        // Plan
        // ---------------------------------------------------------------

        /// <summary>
        /// Lays out the river course for this chunk and rasterizes its distance field onto the heightmap grid.
        /// Called by <see cref="RoadTerrainAdapter"/> before the boundary plan (the side cliffs move out beyond the river).
        /// </summary>
        public bool BuildPlan(int rows, float length, float terrainXOffset, float terrainWidth, RoadSpline spline, float roadHalfWidth)
        {
            _active = false;
            _points.Clear();
            Chunk chunk = GetChunk();
            if (chunk == null || !chunk.IsRiverValley || rows < 2 || spline == null) return false;

            _st = chunk.riverValleySettings;
            _rows = rows;
            _length = length;
            _xOffset = terrainXOffset;
            _width = terrainWidth;
            _halfWidth = terrainWidth * 0.5f;
            _roadHalf = roadHalfWidth;

            BuildCourse(chunk, spline);
            Rasterize();
            BuildOuterExtents();
            _active = _points.Count > 1;
            return _active;
        }

        private static float Smooth01(float t) => Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t));

        /// <summary>River width / gap / drop / side offset at a chunk-local Z of the part beside the road.</summary>
        private void ParallelAt(Chunk chunk, RoadSpline spline, float z, float noise, float halfA, float halfB, float gapA, float gapB,
            float dropA, float dropB, out RiverPoint point)
        {
            RiverValleySettings st = _st;
            float t = Mathf.Clamp01(z / _length);
            float w = Mathf.Clamp01(Mathf.Sin(t * Mathf.PI)); // 0 at the seams: both chunks agree there
            float globalZ = chunk.ChunkIndex * _length + z;

            float half = Mathf.Lerp(halfA, halfB, Smooth01(t));
            half *= 1f + st.widthNoise * w * (Mathf.PerlinNoise(noise, globalZ * 0.006f) * 2f - 1f);
            float gap = Mathf.Lerp(gapA, gapB, Smooth01(t)) + w * 4f * (Mathf.PerlinNoise(noise + 37f, globalZ * 0.01f) * 2f - 1f);
            gap = Mathf.Max(st.minGap * 0.6f, gap);
            float drop = Mathf.Lerp(dropA, dropB, Smooth01(t));

            float side = chunk.riverEntrySide;
            if (_hasCrossing)
            {
                float zc = chunk.riverCrossingZ;
                float window = Mathf.Max(40f, st.crossingLength);
                side = Mathf.Lerp(chunk.riverEntrySide, chunk.riverExitSide, Smooth01((z - (zc - window * 0.5f)) / window));
                // Narrower and a little lower under the bridge
                float bell = Mathf.Exp(-Mathf.Pow((z - zc) / (window * 0.55f), 2f));
                half = Mathf.Lerp(half, Mathf.Min(half, st.crossingWidth * 0.5f), bell);
                drop = Mathf.Lerp(drop, Mathf.Max(drop, st.crossingMinDrop), bell);
            }

            Vector3 road = spline.GetPointAtZ(z);
            float d = RoadMargin + gap + half;
            float bankRoom = RoadMargin + gap - _roadHalf - 2.5f; // the bank must never reach the road bed
            float bank = Mathf.Lerp(st.minBank, st.maxBank, Mathf.InverseLerp(st.minGap, st.maxGap, gap));
            bank = Mathf.Clamp(bank, 2.5f, Mathf.Max(2.5f, bankRoom));
            if (_hasCrossing) bank = Mathf.Lerp(bank, 7f, Mathf.Exp(-Mathf.Pow((z - chunk.riverCrossingZ) / 60f, 2f)));

            float waterY = chunk.baseElevation - drop;
            waterY = Mathf.Min(waterY, road.y - 2.5f);

            point = new RiverPoint
            {
                p = new Vector2(road.x + side * d, z),
                half = half,
                bank = bank,
                waterY = waterY,
                v = globalZ,
                parallel = true
            };
        }

        private void BuildCourse(Chunk chunk, RoadSpline spline)
        {
            RiverValleySettings st = _st;
            int worldSeed = chunk.WorldSeed;
            int seamA = chunk.ChunkIndex, seamB = chunk.ChunkIndex + 1;
            SeamValues(worldSeed, seamA, st, out float halfA, out float gapA, out float dropA);
            SeamValues(worldSeed, seamB, st, out float halfB, out float gapB, out float dropB);
            float noise = new SeededRandom(SeededRandom.Combine(worldSeed, "river_valley_noise")).Range(0f, 1000f);

            bool enters = chunk.RiverEntersFromSide;
            bool leaves = chunk.RiverLeavesToSide;
            _hasCrossing = !enters && !leaves && !Mathf.Approximately(chunk.riverEntrySide, chunk.riverExitSide);

            // Where the river runs beside the road
            float zStart = 0f, zEnd = _length;
            float zIn = st.bendSideZ, zJoin = st.bendJoinZ;
            float zLeave = _length - st.bendJoinZ, zOut = _length - st.bendSideZ;
            if (enters && leaves)
            {
                // A one-chunk run: in from the side and straight out again
                zIn = _length * 0.22f; zJoin = _length * 0.42f; zLeave = _length * 0.58f; zOut = _length * 0.78f;
            }
            if (enters) zStart = zJoin;
            if (leaves) zEnd = zLeave;

            var parallel = new List<RiverPoint>();
            const float step = 5f;
            for (float z = zStart; z <= zEnd + 0.01f; z += step)
            {
                ParallelAt(chunk, spline, Mathf.Min(z, zEnd), noise, halfA, halfB, gapA, gapB, dropA, dropB, out RiverPoint rp);
                parallel.Add(rp);
            }
            if (parallel.Count == 0) return;

            if (enters)
            {
                List<RiverPoint> bend = Bend(parallel[0], chunk.riverEntrySide, zIn, incoming: true);
                _points.AddRange(bend);
            }
            _points.AddRange(parallel);
            if (leaves)
            {
                List<RiverPoint> bend = Bend(parallel[parallel.Count - 1], chunk.riverExitSide, zOut, incoming: false);
                _points.AddRange(bend);
            }
        }

        /// <summary>
        /// The river coming in from (or going out to) the mountains on one side: a straight run from far out on the
        /// horizon to just outside the terrain, then a Bezier curve to the point where it meets the road.
        /// </summary>
        private List<RiverPoint> Bend(RiverPoint join, float side, float sideZ, bool incoming)
        {
            var pts = new List<RiverPoint>();
            Vector2 p3 = join.p;
            Vector2 p0 = new Vector2(side * (_halfWidth + 30f), sideZ);
            float dx = Mathf.Abs(p0.x - p3.x);
            float dz = Mathf.Abs(p3.y - p0.y);
            float dirZ = incoming ? 1f : -1f;            // along-road direction from the side point toward the join
            Vector2 p1 = p0 + new Vector2(-side * dx * 0.5f, 0f);
            Vector2 p2 = p3 - new Vector2(0f, dirZ * dz * 0.6f);

            // Curve from the side point to the join point, then far reach outward
            var curve = new List<Vector2>();
            const int samples = 36;
            for (int i = 0; i < samples; i++) // excludes the join point itself (it is the first/last parallel point)
            {
                float t = i / (float)samples;
                float u = 1f - t;
                curve.Add(u * u * u * p0 + 3f * u * u * t * p1 + 3f * u * t * t * p2 + t * t * t * p3);
            }

            // Arc lengths from the join point (for the flow texture and the bank blend)
            var far = new List<Vector2>();
            for (float x = FarReach; x >= 40f; x -= x > 400f ? 200f : 50f)
                far.Add(new Vector2(side * (_halfWidth + 30f + x), sideZ));

            var chain = new List<Vector2>(far);   // outermost first
            chain.AddRange(curve);                // then in toward the join

            var lengths = new float[chain.Count];
            float acc = Vector2.Distance(chain[chain.Count - 1], p3);
            lengths[chain.Count - 1] = acc;
            for (int i = chain.Count - 2; i >= 0; i--)
            {
                acc += Vector2.Distance(chain[i], chain[i + 1]);
                lengths[i] = acc;
            }

            for (int i = 0; i < chain.Count; i++)
            {
                Vector2 p = chain[i];
                float fromJoin = lengths[i];
                float outside = Mathf.Max(0f, Mathf.Abs(p.x) - _halfWidth);
                // Bank widens away from the road (the river cuts a valley through the side mountains)
                float k = Smooth01(fromJoin / 120f);
                var rp = new RiverPoint
                {
                    p = p,
                    half = join.half + outside * 0.04f,
                    bank = Mathf.Lerp(join.bank, _st.bendBank, k) + outside * 0.05f,
                    waterY = join.waterY,
                    v = incoming ? join.v - fromJoin : join.v + fromJoin,
                    parallel = false
                };
                pts.Add(rp);
            }

            if (!incoming) pts.Reverse(); // flow order: from the join point out to the horizon
            return pts;
        }

        /// <summary>Distance field of the river course on the heightmap grid (nearest-segment distance and its attributes).</summary>
        private void Rasterize()
        {
            int n = _rows * _rows;
            if (_dist == null || _dist.Length != n)
            {
                _dist = new float[n];
                _half = new float[n];
                _bank = new float[n];
                _water = new float[n];
            }
            for (int i = 0; i < n; i++) _dist[i] = float.MaxValue;

            float cell = _width / (_rows - 1f);
            float cellZ = _length / (_rows - 1f);
            for (int s = 0; s + 1 < _points.Count; s++)
            {
                RiverPoint a = _points[s], b = _points[s + 1];
                float reach = Mathf.Max(a.half + a.bank, b.half + b.bank) + 20f; // + bank-top raise margin
                float minX = Mathf.Min(a.p.x, b.p.x) - reach, maxX = Mathf.Max(a.p.x, b.p.x) + reach;
                float minZ = Mathf.Min(a.p.y, b.p.y) - reach, maxZ = Mathf.Max(a.p.y, b.p.y) + reach;
                if (maxX < _xOffset || minX > _xOffset + _width || maxZ < 0f || minZ > _length) continue;

                int cx0 = Mathf.Clamp(Mathf.FloorToInt((minX - _xOffset) / cell), 0, _rows - 1);
                int cx1 = Mathf.Clamp(Mathf.CeilToInt((maxX - _xOffset) / cell), 0, _rows - 1);
                int cz0 = Mathf.Clamp(Mathf.FloorToInt(minZ / cellZ), 0, _rows - 1);
                int cz1 = Mathf.Clamp(Mathf.CeilToInt(maxZ / cellZ), 0, _rows - 1);

                Vector2 ab = b.p - a.p;
                float len2 = Mathf.Max(1e-4f, ab.sqrMagnitude);
                for (int r = cz0; r <= cz1; r++)
                {
                    float z = r * cellZ;
                    int row = r * _rows;
                    for (int c = cx0; c <= cx1; c++)
                    {
                        float x = _xOffset + c * cell;
                        float t = Mathf.Clamp01(((x - a.p.x) * ab.x + (z - a.p.y) * ab.y) / len2);
                        float px = a.p.x + ab.x * t - x, pz = a.p.y + ab.y * t - z;
                        float d = Mathf.Sqrt(px * px + pz * pz);
                        int idx = row + c;
                        if (d >= _dist[idx]) continue;
                        _dist[idx] = d;
                        _half[idx] = Mathf.Lerp(a.half, b.half, t);
                        _bank[idx] = Mathf.Lerp(a.bank, b.bank, t);
                        _water[idx] = Mathf.Lerp(a.waterY, b.waterY, t);
                    }
                }
            }
        }

        /// <summary>Per row and side: how far from the chunk center the far bank of the river beside the road reaches.</summary>
        private void BuildOuterExtents()
        {
            _outerL = new float[_rows];
            _outerR = new float[_rows];
            for (int i = 0; i < _points.Count; i++)
            {
                RiverPoint rp = _points[i];
                if (!rp.parallel) continue;
                float reach = rp.half + rp.bank;
                float extent = Mathf.Abs(rp.p.x) + reach;
                float[] target = rp.p.x < 0f ? _outerL : _outerR;
                int r0 = Mathf.Clamp(Mathf.FloorToInt((rp.p.y - reach) / _length * (_rows - 1)), 0, _rows - 1);
                int r1 = Mathf.Clamp(Mathf.CeilToInt((rp.p.y + reach) / _length * (_rows - 1)), 0, _rows - 1);
                for (int r = r0; r <= r1; r++) target[r] = Mathf.Max(target[r], extent);
            }
        }

        /// <summary>
        /// Side cliffs must start beyond the far bank of the river beside the road (the bends through the side
        /// mountains cut through the cliffs instead). 0 when the river is not on that side at this row.
        /// </summary>
        public float CliffClearance(float sideSign, int row)
        {
            if (!_active || _outerL == null) return 0f;
            row = Mathf.Clamp(row, 0, _rows - 1);
            float e = sideSign < 0f ? _outerL[row] : _outerR[row];
            return e > 0f ? e + 25f : 0f;
        }

        // ---------------------------------------------------------------
        // Terrain
        // ---------------------------------------------------------------

        /// <summary>Share of the ambient landscape undulation kept in the valley (flatter valley floor).</summary>
        public float AmbientScale => _active && _st != null ? _st.ambientScale : 1f;

        private int GridIndex(float localX, int row)
        {
            int c = Mathf.Clamp(Mathf.RoundToInt((localX - _xOffset) / _width * (_rows - 1)), 0, _rows - 1);
            return Mathf.Clamp(row, 0, _rows - 1) * _rows + c;
        }

        /// <summary>
        /// Applied after the road bed is blended in: the bank tops stay above the water (no water floating over a dip),
        /// then the channel and its banks are cut. Under a crossing the channel runs under the road, which spans it as
        /// a bridge (the road mesh has its own collider). Beside the road the bank never reaches the road bed.
        /// </summary>
        public float ApplyAfterRoad(float localX, int row, float height)
        {
            if (!_active) return height;
            int idx = GridIndex(localX, row);
            float d = _dist[idx];
            float half = _half[idx], bank = _bank[idx], water = _water[idx];
            const float raise = 18f;
            if (d >= half + bank + raise) return height;

            // Keep the ground around the river above the water level
            float floorY = water + 0.6f;
            if (height < floorY)
            {
                float s = Smooth01((d - (half + bank)) / raise);
                height = Mathf.Max(height, Mathf.Lerp(floorY, height, s));
            }

            if (d <= half)
            {
                float u = d / Mathf.Max(0.5f, half);
                float bed = water - Mathf.Max(0.15f, _st.maxDepth * (1f - u * u));
                return Mathf.Min(height, bed);
            }
            if (d < half + bank)
            {
                float k = Smooth01((d - half) / Mathf.Max(0.5f, bank));
                return Mathf.Min(height, Mathf.Lerp(water - 0.15f, height, k));
            }
            return height;
        }

        /// <summary>Muddy banks and riverbed on top of the grass (replaces the boundary's plain grass painting).</summary>
        public void PaintTerrainLayers(TerrainData td)
        {
            if (!_active || td == null) return;
            TerrainLayer[] layers = td.terrainLayers;
            if (layers == null || layers.Length == 0 || layers[0] == null) return;
            TerrainLayer mud = GetMudLayer();
            if (mud == null) return;
            if (layers.Length != 2 || layers[1] != mud) td.terrainLayers = new[] { layers[0], mud };
            RoadTerrainAdapter.LimitRuntimeAlphamapResolution(td);
            int res = td.alphamapResolution;

            var alphas = new float[res, res, 2];
            for (int y = 0; y < res; y++)
            {
                int row = Mathf.RoundToInt(y / (res - 1f) * (_rows - 1));
                for (int x = 0; x < res; x++)
                {
                    int col = Mathf.RoundToInt(x / (res - 1f) * (_rows - 1));
                    int idx = row * _rows + col;
                    float d = _dist[idx];
                    float m = 0f;
                    if (d < float.MaxValue)
                    {
                        float edge = _half[idx] + _bank[idx] * 0.55f;
                        m = 1f - Smooth01((d - edge) / 5f);
                        // Patchy edge
                        m *= 0.75f + 0.25f * Mathf.PerlinNoise(x * 0.11f, y * 0.11f);
                        if (d < _half[idx]) m = 1f;
                    }
                    alphas[y, x, 0] = 1f - m;
                    alphas[y, x, 1] = m;
                }
            }
            td.SetAlphamaps(0, 0, alphas);
        }

        // ---------------------------------------------------------------
        // Queries (vegetation, loot, wrecks, animals, backdrop)
        // ---------------------------------------------------------------

        /// <summary>Distance to the river center line, with the water half width and bank there (any chunk-local point).</summary>
        private float DistanceToCourse(float x, float z, out float half, out float bank, out float waterY)
        {
            half = bank = 0f;
            waterY = 0f;
            float best = float.MaxValue;
            for (int s = 0; s + 1 < _points.Count; s++)
            {
                RiverPoint a = _points[s], b = _points[s + 1];
                Vector2 ab = b.p - a.p;
                float len2 = Mathf.Max(1e-4f, ab.sqrMagnitude);
                float t = Mathf.Clamp01(((x - a.p.x) * ab.x + (z - a.p.y) * ab.y) / len2);
                float px = a.p.x + ab.x * t - x, pz = a.p.y + ab.y * t - z;
                float d2 = px * px + pz * pz;
                if (d2 >= best) continue;
                best = d2;
                half = Mathf.Lerp(a.half, b.half, t);
                bank = Mathf.Lerp(a.bank, b.bank, t);
                waterY = Mathf.Lerp(a.waterY, b.waterY, t);
            }
            return Mathf.Sqrt(best);
        }

        /// <summary>True in the water, on its banks, or within <paramref name="margin"/> meters of them (chunk-local point).</summary>
        public bool IsNearRiver(Vector3 chunkLocal, float margin)
        {
            if (!_active) return false;
            float d = DistanceToCourse(chunkLocal.x, chunkLocal.z, out float half, out float bank, out _);
            return d < half + bank + margin;
        }

        /// <summary>True in the water itself (plus <paramref name="margin"/>), banks excluded.</summary>
        public bool IsInWater(Vector3 chunkLocal, float margin)
        {
            if (!_active) return false;
            float d = DistanceToCourse(chunkLocal.x, chunkLocal.z, out float half, out _, out _);
            return d < half + margin;
        }

        public float ApplyBackdropRiver(float localX, float localZ, float u, float y)
        {
            if (!_active) return y;
            float d = DistanceToCourse(localX, localZ, out float half, out float bank, out float waterY);
            float outer = half + bank + 10f + u * 0.12f;
            if (d >= outer) return y;
            float bed = waterY - 1.5f;
            float t = d <= half ? 0f : Smooth01((d - half) / (outer - half));
            float carved = Mathf.Min(y, Mathf.Lerp(bed, y, t));
            // Never at the seam rows: the neighbor's backdrop strip must meet this one there
            float seamFade = Smooth01(Mathf.Min(localZ, _length - localZ) / 40f);
            return Mathf.Lerp(y, carved, seamFade);
        }

        // ---------------------------------------------------------------
        // Water and bridge
        // ---------------------------------------------------------------

        public void Generate(Transform parent, Terrain terrain)
        {
            IEnumerator steps = GenerateSteps(parent, terrain);
            while (steps.MoveNext()) { }
        }

        /// <summary>Water surface, then the bridge (separate steps for the time-sliced chunk build). Call after the terrain is shaped.</summary>
        public IEnumerator GenerateSteps(Transform parent, Terrain terrain)
        {
            ClearContainer(parent);
            DestroyOwnedMeshes();
            if (parent == null) yield break;
            Chunk chunk = GetChunk();
            if (chunk == null || !chunk.IsRiverValley) yield break;

            if (!_active)
            {
                var spline = chunk.GetComponentInChildren<RoadSpline>();
                var roadGen = chunk.GetComponentInChildren<RoadGenerator>();
                TerrainData td = terrain != null ? terrain.terrainData : null;
                int rows = td != null ? td.heightmapResolution : 129;
                float width = td != null ? td.size.x : 500f;
                float xOffset = terrain != null ? terrain.transform.localPosition.x : -250f;
                if (!BuildPlan(rows, chunk.chunkLength, xOffset, width, spline, roadGen != null ? roadGen.GetTotalHalfWidth() : 8f)) yield break;
            }

            var container = new GameObject(CONTAINER_NAME);
            container.transform.SetPositionAndRotation(chunk.transform.position, chunk.transform.rotation);
            container.transform.SetParent(parent, true);

            BuildWater(container.transform, chunk);
            yield return null;

            if (_hasCrossing) BuildBridge(container.transform, chunk, terrain);
        }

        private void BuildWater(Transform container, Chunk chunk)
        {
            int count = _points.Count;
            var verts = new List<Vector3>(count * 2);
            var uvs = new List<Vector2>(count * 2);
            var tris = new List<int>((count - 1) * 6);
            float tile = Mathf.Max(1f, _st.flowTile);
            // Keep UVs small far down the road: a whole number of tiles off, so neighbors still line up
            float vBase = Mathf.Floor(chunk.ChunkIndex * _length / tile) * tile;

            for (int i = 0; i < count; i++)
            {
                RiverPoint rp = _points[i];
                Vector2 prev = _points[Mathf.Max(0, i - 1)].p;
                Vector2 next = _points[Mathf.Min(count - 1, i + 1)].p;
                Vector2 dir = next - prev;
                if (dir.sqrMagnitude < 1e-6f) dir = Vector2.up;
                dir.Normalize();
                Vector2 right = new Vector2(dir.y, -dir.x); // right-hand side looking downstream
                // A little wider than the shoreline: the edge tucks under the bank instead of floating over it
                float half = rp.half + 1.2f;
                Vector2 l = rp.p - right * half, r = rp.p + right * half;
                verts.Add(new Vector3(l.x, rp.waterY, l.y));
                verts.Add(new Vector3(r.x, rp.waterY, r.y));
                uvs.Add(new Vector2(-half / tile, (rp.v - vBase) / tile));
                uvs.Add(new Vector2(half / tile, (rp.v - vBase) / tile));

                if (i > 0)
                {
                    int a = (i - 1) * 2;
                    // left(prev), left(this), right(prev) / right(prev), left(this), right(this): faces up
                    tris.Add(a); tris.Add(a + 2); tris.Add(a + 1);
                    tris.Add(a + 1); tris.Add(a + 2); tris.Add(a + 3);
                }
            }

            var mesh = new Mesh { name = $"River_Water_Chunk{chunk.ChunkIndex}" };
            mesh.indexFormat = verts.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            _ownedMeshes.Add(mesh);

            // Named River_Water: survival water queries (filling bottles, fishing, wetness) find it by name
            var water = new GameObject("River_Water");
            water.transform.SetParent(container, false);
            water.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = water.AddComponent<MeshRenderer>();
            mr.sharedMaterial = GetWaterMaterial(_st);
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = true;
            water.AddComponent<RiverFlow>().speed = _st.flowSpeed / tile;
        }

        private void BuildBridge(Transform container, Chunk chunk, Terrain terrain)
        {
            var spline = chunk.GetComponentInChildren<RoadSpline>();
            if (spline == null) return;
            var roadGen = chunk.GetComponentInChildren<RoadGenerator>();
            float roadHalf = roadGen != null ? roadGen.GetTotalHalfWidth() : 7f;

            // Where the road passes over the channel (water + banks), measured at both road edges
            float zc = chunk.riverCrossingZ;
            float zStart = -1f, zEnd = -1f, zDeepest = zc, minDist = float.MaxValue;
            float bedAtDeepest = 0f;
            for (float z = Mathf.Max(1f, zc - 200f); z <= Mathf.Min(_length - 1f, zc + 200f); z += 0.5f)
            {
                Vector3 p = spline.GetPointAtZ(z);
                bool over = false;
                for (int e = -1; e <= 1; e++)
                {
                    float d = DistanceToCourse(p.x + e * roadHalf, z, out float half, out float bank, out float wy);
                    if (d < half + bank + 1f) over = true;
                    if (e == 0 && d < minDist) { minDist = d; zDeepest = z; bedAtDeepest = wy - _st.maxDepth; }
                }
                if (!over) continue;
                if (zStart < 0f) zStart = z;
                zEnd = z;
            }
            if (zStart < 0f) return;

            zStart = Mathf.Max(0.5f, zStart - 3f);
            zEnd = Mathf.Min(_length - 0.5f, zEnd + 3f);

            var bridge = new GameObject("Bridge");
            bridge.transform.SetParent(container, false);
            var mb = new BoxMeshBuilder(2); // 0 = concrete, 1 = deck

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

                // Concrete parapets with colliders so vehicles can't drive off into the river
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

            // Abutments at both ends
            float[] ends = { zStart + 1f, zEnd - 1f };
            for (int i = 0; i < ends.Length; i++)
            {
                Vector3 p = spline.GetPointAtZ(ends[i]);
                Vector3 ahead = spline.GetPointAtZ(Mathf.Min(_length, ends[i] + 1f));
                Quaternion rot = Quaternion.LookRotation(ahead - p, Vector3.up);
                mb.Box(p + Vector3.up * -4.6f, new Vector3(roadHalf * 2f + 1.6f, 6f, 2f), rot, 0);
            }

            // Piers every pierSpacing, standing on the riverbed / banks below
            if (zEnd - zStart > pierMinSpan)
            {
                float spacing = Mathf.Max(6f, pierSpacing);
                for (int k = -10; k <= 10; k++)
                {
                    float z = zDeepest + k * spacing;
                    if (z < zStart + 5f || z > zEnd - 5f) continue;

                    Vector3 p = spline.GetPointAtZ(z);
                    float ground = k == 0 ? bedAtDeepest : Mathf.Min(GroundY(chunk, terrain, p.x, z), p.y);
                    float deckBottom = p.y - 1.6f;
                    if (deckBottom - ground < 2f) continue;

                    Vector3 ahead = spline.GetPointAtZ(Mathf.Min(_length, z + 1f));
                    Quaternion rot = Quaternion.LookRotation(ahead - p, Vector3.up);
                    float bottom = ground - 0.5f;
                    float h = deckBottom - bottom;
                    mb.Box(new Vector3(p.x, bottom + h * 0.5f, p.z), new Vector3(roadHalf * 2f - 1f, h, 1.8f), rot, 0);
                }
            }

            Mesh mesh = mb.ToMesh($"RiverBridge_Chunk{chunk.ChunkIndex}");
            _ownedMeshes.Add(mesh);
            bridge.AddComponent<MeshFilter>().sharedMesh = mesh;
            bridge.AddComponent<MeshRenderer>().sharedMaterials = new[]
            {
                ChunkBoundaryGenerator.GetSharedMaterial(concreteColor),
                ChunkBoundaryGenerator.GetSharedMaterial(deckColor)
            };
        }

        // ---------------------------------------------------------------
        // Materials
        // ---------------------------------------------------------------

        private static Material GetWaterMaterial(RiverValleySettings st)
        {
            if (s_waterMaterial != null) return s_waterMaterial;
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            var m = new Material(shader) { name = "Mat_RiverValley_Runtime" };
            Color c = st != null ? st.waterColor : new Color(0.16f, 0.34f, 0.40f, 0.84f);
            Texture2D tex = GetFlowTexture();

            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            m.color = c;
            if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", tex);
            m.mainTexture = tex;
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.88f);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0.05f);

            // URP Lit, transparent (alpha blend): the shallow bed shows through near the banks
            if (m.HasProperty("_Surface"))
            {
                m.SetFloat("_Surface", 1f);
                m.SetFloat("_Blend", 0f);
                m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                if (m.HasProperty("_SrcBlendAlpha")) m.SetFloat("_SrcBlendAlpha", (float)UnityEngine.Rendering.BlendMode.One);
                if (m.HasProperty("_DstBlendAlpha")) m.SetFloat("_DstBlendAlpha", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                m.SetFloat("_ZWrite", 0f);
                m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                m.SetOverrideTag("RenderType", "Transparent");
                m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            }
            s_waterMaterial = m;
            return m;
        }

        /// <summary>
        /// Tileable flow streaks: long, soft light lines along V (the river's length) over a near-white base, so the
        /// material color shows through. Built from integer-frequency sines, so it wraps seamlessly.
        /// </summary>
        private static Texture2D GetFlowTexture()
        {
            if (s_flowTexture != null) return s_flowTexture;
            const int size = 128;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true) { name = "Tex_RiverFlow_Runtime", wrapMode = TextureWrapMode.Repeat };
            var px = new Color[size * size];
            const float tau = Mathf.PI * 2f;
            for (int y = 0; y < size; y++)
            {
                float v = y / (float)size;
                for (int x = 0; x < size; x++)
                {
                    float u = x / (float)size;
                    // Streaks: thin across (u), long along (v); wobble so they are not straight lines
                    float wobble = 0.035f * Mathf.Sin(tau * (2f * v + 0.3f)) + 0.02f * Mathf.Sin(tau * (5f * v + 1.7f));
                    float s1 = Mathf.Pow(Mathf.Abs(Mathf.Sin(tau * (7f * (u + wobble)))), 18f) * (0.5f + 0.5f * Mathf.Sin(tau * (1f * v + u * 3f)));
                    float s2 = Mathf.Pow(Mathf.Abs(Mathf.Sin(tau * (11f * (u - wobble * 1.3f) + 0.4f))), 24f) * (0.5f + 0.5f * Mathf.Sin(tau * (2f * v + 2.1f)));
                    float ripple = 0.5f + 0.5f * Mathf.Sin(tau * (3f * u + 9f * v)) * Mathf.Sin(tau * (4f * v - 2f * u));
                    float l = 0.80f + 0.06f * ripple + 0.30f * Mathf.Clamp01(s1 + s2 * 0.8f);
                    px[y * size + x] = new Color(l, l, l, 1f);
                }
            }
            tex.SetPixels(px);
            tex.Apply(true);
            s_flowTexture = tex;
            return tex;
        }

        private static TerrainLayer GetMudLayer()
        {
#if UNITY_EDITOR
            // Edit mode writes into the prefab's TerrainData asset, so the layer must be an asset too
            var asset = UnityEditor.AssetDatabase.LoadAssetAtPath<TerrainLayer>("Assets/Prefabs/Chunks/TerrainLayers/Layer_Wasteland.terrainlayer");
            if (asset != null) return asset;
#endif
            if (s_runtimeMudLayer == null)
            {
                var tex = new Texture2D(4, 4, TextureFormat.RGBA32, false) { name = "Tex_RiverMud_Runtime" };
                var pixels = new Color[16];
                for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color(0.36f, 0.31f, 0.24f);
                tex.SetPixels(pixels);
                tex.Apply();
                s_runtimeMudLayer = new TerrainLayer { name = "Layer_RiverMud_Runtime", diffuseTexture = tex, tileSize = new Vector2(12f, 12f) };
            }
            return s_runtimeMudLayer;
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

        private static float GroundY(Chunk chunk, Terrain terrain, float localX, float localZ)
        {
            if (terrain == null) return chunk.baseElevation;
            Vector3 world = chunk.transform.TransformPoint(new Vector3(localX, 0f, localZ));
            float worldY = terrain.SampleHeight(world) + terrain.transform.position.y;
            return worldY - chunk.transform.position.y;
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
    }

    /// <summary>Scrolls the shared river material's flow texture along the river (visual only). One update per frame for all rivers.</summary>
    public class RiverFlow : MonoBehaviour
    {
        public float speed = 0.08f; // texture tiles per second
        private static int s_lastFrame = -1;
        private Renderer _renderer;

        private void Awake()
        {
            _renderer = GetComponent<Renderer>();
        }

        private void Update()
        {
            if (s_lastFrame == Time.frameCount || _renderer == null) return;
            s_lastFrame = Time.frameCount;
            Material m = _renderer.sharedMaterial;
            if (m == null) return;
            // Features move toward increasing V (downstream) as the offset decreases
            var offset = new Vector2(0f, -Mathf.Repeat(Time.time * speed, 1f));
            if (m.HasProperty("_BaseMap")) m.SetTextureOffset("_BaseMap", offset);
            else m.mainTextureOffset = offset;
        }
    }
}
