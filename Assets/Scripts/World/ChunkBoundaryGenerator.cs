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
        public List<BiomeBoundarySettings> biomeSettings = new List<BiomeBoundarySettings> { new BiomeBoundarySettings() };

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

        private class SideProfile
        {
            public ChunkBoundaryStyle style;
            public float[] inner;
            public float[] ramp;
            public float[] height;
            public float[] fade;
            public float noiseOffset;
        }

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
        public bool BuildPlan(RoadSpline spline, int rows, float length, float halfWidth)
        {
            _sides = null;
            if (!boundaryEnabled || rows < 2) return false;

            Chunk chunk = GetChunk();
            BiomeBoundarySettings settings = GetSettings(chunk);
            if (settings == null) return false;

            _rows = rows;
            _length = length;
            _halfWidth = halfWidth;

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

            return true;
        }

        /// <summary>
        /// Terrain'in doğal yüksekliğine eklenecek uçurum yüksekliği (metre).
        /// localX chunk yerel X'i, row heightmap satırıdır.
        /// </summary>
        public float GetHeightOffset(float localX, int row)
        {
            if (_sides == null) return 0f;
            row = Mathf.Clamp(row, 0, _rows - 1);
            SideProfile side = _sides[localX < 0f ? 0 : 1];

            float d = Mathf.Abs(localX) - side.inner[row];
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
                BuildPlan(chunk.GetComponentInChildren<RoadSpline>(), rows, chunk.chunkLength, halfWidth);
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
