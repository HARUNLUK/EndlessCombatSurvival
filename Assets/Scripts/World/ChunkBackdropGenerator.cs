using System.Collections.Generic;
using UnityEngine;

namespace EndlessSurvival.World
{
    /// <summary>
    /// Biyoma özel arka plan dağ ayarları.
    /// </summary>
    [System.Serializable]
    public class BiomeBackdropSettings
    {
        public ChunkBiomeType biome = ChunkBiomeType.Forest;

        [Header("Boyut")]
        [Tooltip("Kenardan ilk dağ yamacına kadar yükselme mesafesi")]
        public float riseDistance = 140f;
        [Tooltip("Dağların kenar yüksekliğinin (uçurum tepesi) en az ne kadar üstüne çıkacağı. Düzlükte 0.")]
        public float minAboveEdge = 12f;

        [Header("Yükseklik (baseline 20m üstü)")]
        [Tooltip("Dağların minimum taban yüksekliği")]
        public float baseHeight = 70f;
        [Tooltip("Sırt (ridge) zirvelerinin ek yüksekliği")]
        public float peakAmplitude = 260f;
        [Tooltip("Küçük ölçekli engebe")]
        public float detailAmplitude = 35f;
        [Tooltip("Sırtların sıklığı (küçük = daha geniş dağlar)")]
        public float ridgeFrequency = 0.0028f;

        [Header("Renk Bantları")]
        public float rockLine = 120f;
        public float snowLine = 210f;
        [Range(0f, 1f)]
        [Tooltip("Normal.y bu değerin altındaysa (dik yüz) kaya rengi kullanılır")]
        public float rockSlope = 0.72f;
        public Color forestColor = new Color(0.15f, 0.25f, 0.13f);
        public Color rockColor = new Color(0.37f, 0.35f, 0.32f);
        public Color snowColor = new Color(0.90f, 0.92f, 0.95f);

        /// <summary>Built-in settings for biomes that need a different horizon than the first list entry.</summary>
        public static BiomeBackdropSettings DefaultFor(ChunkBiomeType biome)
        {
            if (biome == ChunkBiomeType.Mountain)
            {
                // Snowy high mountains: tall ridges starting right at the edge, mostly snow-covered
                return new BiomeBackdropSettings
                {
                    biome = ChunkBiomeType.Mountain,
                    riseDistance = 55f,   // steep faces: in mountain chunks the strip starts right beside the road
                    minAboveEdge = 20f,
                    baseHeight = 130f,
                    peakAmplitude = 380f,
                    detailAmplitude = 45f,
                    ridgeFrequency = 0.0032f,
                    rockLine = 40f,
                    snowLine = 70f,
                    rockSlope = 0.62f,
                    forestColor = new Color(0.86f, 0.89f, 0.93f),
                    rockColor = new Color(0.40f, 0.40f, 0.43f),
                    snowColor = new Color(0.95f, 0.96f, 0.98f)
                };
            }

            if (biome != ChunkBiomeType.Field) return null;
            return FieldDefault();
        }

        /// <summary>Alpine (snowless) mountains: green lower slopes, rock faces, snow only on the high peaks.</summary>
        public static BiomeBackdropSettings AlpineMountain()
        {
            return new BiomeBackdropSettings
            {
                biome = ChunkBiomeType.Mountain,
                riseDistance = 55f,
                minAboveEdge = 20f,
                baseHeight = 120f,
                peakAmplitude = 340f,
                detailAmplitude = 40f,
                ridgeFrequency = 0.0032f,
                rockLine = 110f,
                snowLine = 240f,
                rockSlope = 0.62f,
                forestColor = new Color(0.20f, 0.33f, 0.17f),
                rockColor = new Color(0.40f, 0.39f, 0.37f),
                snowColor = new Color(0.94f, 0.95f, 0.97f)
            };
        }

        private static BiomeBackdropSettings FieldDefault()
        {
            // Open plains: meadow continues flat past the edge, low soft hills only far away on the horizon
            return new BiomeBackdropSettings
            {
                biome = ChunkBiomeType.Field,
                riseDistance = 1500f,
                minAboveEdge = 0f,
                baseHeight = 12f,
                peakAmplitude = 75f,
                detailAmplitude = 6f,
                ridgeFrequency = 0.0011f,
                rockLine = 400f,
                snowLine = 2000f,
                rockSlope = 0.35f,
                forestColor = new Color(0.24f, 0.40f, 0.20f),
                rockColor = new Color(0.30f, 0.40f, 0.24f),
                snowColor = new Color(0.90f, 0.92f, 0.95f)
            };
        }
    }

    /// <summary>
    /// Chunk'ın sağ ve sol dışına low-poly arka plan dağları üretir. Dağ yüksekliği
    /// "global Z" (chunkIndex * 500 + localZ) üzerinden sürekli gürültüyle hesaplandığından
    /// komşu chunk'ların dağları birleşim noktasında aynı vertex'lerde buluşur.
    /// İç kenar, terrain'in X=±250 kenar yüksekliğine (uçurum tepesi dahil) oturur.
    /// </summary>
    [DisallowMultipleComponent]
    public class ChunkBackdropGenerator : MonoBehaviour
    {
        public const string CONTAINER_NAME = "Backdrop_Mountains";

        public bool backdropEnabled = true;

        [Tooltip("Biyoma göre ayarlar. Chunk'ın biyomu listede yoksa ilk eleman kullanılır.")]
        public List<BiomeBackdropSettings> biomeSettings = new List<BiomeBackdropSettings>
        {
            new BiomeBackdropSettings(),
            BiomeBackdropSettings.DefaultFor(ChunkBiomeType.Field),
            BiomeBackdropSettings.DefaultFor(ChunkBiomeType.Mountain)
        };

        [Header("Mesh")]
        [Tooltip("Chunk kenarından (X=±250) dışarıya doğru şeridin genişliği. Tüm biyomlarda aynı olmalı ki komşu chunk'ların vertex'leri buluşsun.")]
        public float stripDepth = 2600f;
        [Tooltip("Farklı biyomlu komşu chunk ile arka planın harmanlandığı mesafe")]
        public float biomeBlendDistance = 250f;
        [Range(10, 80)] public int rowsAlongRoad = 40;
        [Range(8, 48)] public int columnsOutward = 32;

        [Header("Performans / Görünürlük")]
        [Tooltip("Oyun sırasında oyun kameralarının far clip değeri bundan küçükse yükseltilir (dağların kesilmemesi için). Sonradan açılan kameralar (araç kamerası vb.) da dahil.")]
        public float requiredCameraFarClip = 3000f;
        [Tooltip("Oyuncu uçurum/kaya arasından sızarsa düşmesin diye dağ yüzeyine collider ekler")]
        public bool addCollider = true;

        private readonly List<Mesh> _ownedMeshes = new List<Mesh>();

        public void Generate(Transform parent, Terrain terrain)
        {
            ClearContainer(parent);
            DestroyOwnedMeshes();
            if (!backdropEnabled || parent == null) return;

            Chunk chunk = GetComponent<Chunk>();
            if (chunk == null) chunk = GetComponentInParent<Chunk>();
            if (chunk == null) return;

            BiomeBackdropSettings settings = GetSettings(chunk.biomeType, chunk.mountainVariant);
            if (settings == null) return;

            int worldSeed = chunk.WorldSeed;
            float length = chunk.chunkLength;
            float halfWidth = terrain != null && terrain.terrainData != null ? terrain.terrainData.size.x * 0.5f : 250f;

            var container = new GameObject(CONTAINER_NAME);
            container.transform.SetPositionAndRotation(chunk.transform.position, chunk.transform.rotation);
            container.transform.SetParent(parent, true);

            Material[] materials =
            {
                ChunkBoundaryGenerator.GetSharedMaterial(settings.forestColor),
                ChunkBoundaryGenerator.GetSharedMaterial(settings.rockColor),
                ChunkBoundaryGenerator.GetSharedMaterial(settings.snowColor)
            };

            // On a coast's sea side the mountains sink under the water; field river crossings continue as a valley
            var boundary = chunk.GetComponent<ChunkBoundaryGenerator>();
            var fieldEdges = chunk.GetComponent<FieldEdgeGenerator>();

            for (int s = 0; s < 2; s++)
            {
                float sign = s == 0 ? -1f : 1f;
                var ctx = new SideContext
                {
                    index = chunk.ChunkIndex,
                    biome = chunk.biomeType,
                    prev = chunk.PrevBiome,
                    next = chunk.NextBiome,
                    variant = chunk.mountainVariant,
                    prevVariant = chunk.PrevMountainVariant,
                    nextVariant = chunk.NextMountainVariant,
                    baseline = chunk.baseElevation,
                    length = length,
                    halfWidth = halfWidth,
                    innerX = z => boundary != null && boundary.TryGetMountainInnerX(sign, z, out float ix) ? ix : sign * halfWidth,
                    edgeY = (x, z) => SampleEdgeHeight(chunk, terrain, x - sign * 0.1f, z),
                    groundWeight = z => boundary != null ? boundary.GetMountainWeight(z) : 0f,
                    seaWeight = z => boundary != null ? boundary.GetSeaWeight(sign, z) : 0f,
                    river = fieldEdges != null && fieldEdges.HasRiver ? fieldEdges : null
                };
                Mesh mesh = BuildSideMesh(ctx, settings, worldSeed, s, sign);
                mesh.name = $"Backdrop_{(s == 0 ? "Left" : "Right")}_Chunk{chunk.ChunkIndex}";
                _ownedMeshes.Add(mesh);

                var go = new GameObject(s == 0 ? "Mountains_Left" : "Mountains_Right");
                go.transform.SetParent(container.transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterials = materials;
                // Mountains standing right beside the road cast shadows; far backdrops do not (cost)
                mr.shadowCastingMode = chunk.IsMountain ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;

                if (addCollider)
                {
                    go.AddComponent<MeshCollider>().sharedMesh = mesh;
                }
            }

            if (Application.isPlaying) EnsureCameraFarClip();
        }

        /// <summary>
        /// Silhouette of a chunk that is not loaded yet: both mountain strips (from the biome plan and world seed,
        /// so they match what the real chunk will show) plus a flat ground stand-in at road level.
        /// No colliders. Cliffs/coast detail are approximated; the real chunk replaces it far away in the haze.
        /// </summary>
        public GameObject GenerateProxy(Transform parent, Vector3 position, Quaternion rotation, int index,
            ChunkBiomeType biome, ChunkBiomeType prev, ChunkBiomeType next, MountainVariant variant, MountainVariant prevVariant,
            MountainVariant nextVariant, CoastSide seaSide, bool coastStart, bool coastEnd,
            int worldSeed, float length, float baseline, float halfWidth, float coastTransition,
            float roadEntryX = 0f, float roadExitX = 0f, float roadEntryY = float.NaN, float roadExitY = float.NaN)
        {
            BiomeBackdropSettings settings = GetSettings(biome, variant);
            if (!backdropEnabled || settings == null) return null;

            var root = new GameObject($"BackdropProxy_{index}");
            root.transform.SetPositionAndRotation(position, rotation);
            root.transform.SetParent(parent, true);
            var owner = root.AddComponent<OwnedMeshes>();

            Material[] materials =
            {
                ChunkBoundaryGenerator.GetSharedMaterial(settings.forestColor),
                ChunkBoundaryGenerator.GetSharedMaterial(settings.rockColor),
                ChunkBoundaryGenerator.GetSharedMaterial(settings.snowColor)
            };

            bool coast = biome == ChunkBiomeType.Coast;
            float seaSign = seaSide == CoastSide.Left ? -1f : 1f;
            float transition = Mathf.Max(1f, coastTransition);

            for (int s = 0; s < 2; s++)
            {
                float sign = s == 0 ? -1f : 1f;
                bool seaHere = coast && Mathf.Approximately(sign, seaSign);
                // Same fade as ChunkBoundaryGenerator's coast profile at headlands
                System.Func<float, float> sea = z => !seaHere ? 0f : Mathf.Min(
                    coastStart ? 1f : Mathf.SmoothStep(0f, 1f, z / transition),
                    coastEnd ? 1f : Mathf.SmoothStep(0f, 1f, (length - z) / transition));

                var ctx = new SideContext
                {
                    index = index,
                    biome = biome,
                    prev = prev,
                    next = next,
                    variant = variant,
                    prevVariant = prevVariant,
                    nextVariant = nextVariant,
                    baseline = baseline,
                    length = length,
                    halfWidth = halfWidth,
                    innerX = z => ProxyMountainInnerX(biome, prev, next, sign, z, length, halfWidth, roadEntryX, roadExitX),
                    edgeY = (x, z) => biome == ChunkBiomeType.Mountain ? ProxyRoadY(z, length, roadEntryY, roadExitY, baseline) : Mathf.Lerp(baseline, 0f, sea(z)),
                    groundWeight = z => biome == ChunkBiomeType.Mountain ? ProxyMountainWeight(prev, next, z, length) : 0f,
                    seaWeight = sea,
                    river = null
                };

                Mesh mesh = BuildSideMesh(ctx, settings, worldSeed, s, sign);
                mesh.name = $"BackdropProxy_{(s == 0 ? "L" : "R")}_{index}";
                owner.meshes.Add(mesh);

                var go = new GameObject(s == 0 ? "Mountains_Left" : "Mountains_Right");
                go.transform.SetParent(root.transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterials = materials;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }

            // Flat ground at road level where the terrain will be (only the land half on coasts; the ocean shows on the other)
            float x0 = coast ? (seaSign > 0f ? -halfWidth : 0f) : -halfWidth;
            float x1 = coast ? (seaSign > 0f ? 0f : halfWidth) : halfWidth;
            // Mountain chunks: the ground climbs with the road between its entry and exit heights
            bool mountain = biome == ChunkBiomeType.Mountain;
            float y0 = (mountain ? ProxyRoadY(0f, length, roadEntryY, roadExitY, baseline) : baseline) - 0.3f;
            float y1 = (mountain ? ProxyRoadY(length, length, roadEntryY, roadExitY, baseline) : baseline) - 0.3f;
            var ground = new Mesh { name = $"BackdropProxy_Ground_{index}" };
            ground.vertices = new[] { new Vector3(x0, y0, 0f), new Vector3(x0, y1, length), new Vector3(x1, y1, length), new Vector3(x1, y0, 0f) };
            ground.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            ground.RecalculateNormals();
            ground.RecalculateBounds();
            owner.meshes.Add(ground);

            var groundGo = new GameObject("Ground");
            groundGo.transform.SetParent(root.transform, false);
            groundGo.AddComponent<MeshFilter>().sharedMesh = ground;
            var gmr = groundGo.AddComponent<MeshRenderer>();
            gmr.sharedMaterial = ChunkBoundaryGenerator.GetSharedMaterial(biome == ChunkBiomeType.Mountain && variant == MountainVariant.Snowy ? ProxySnowGroundColor : ProxyGroundColor);
            gmr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            if (Application.isPlaying) EnsureCameraFarClip();
            return root;
        }

        /// <summary>
        /// Rough mountain foot for a not-yet-loaded mountain chunk: ~35m beside the road (whose entry/exit offsets
        /// are known from the plan), pulled back to the terrain edge toward non-mountain neighbors.
        /// The real chunk replaces it far away in the haze.
        /// </summary>
        private static float ProxyMountainInnerX(ChunkBiomeType biome, ChunkBiomeType prev, ChunkBiomeType next, float sign,
            float z, float length, float halfWidth, float entryX, float exitX)
        {
            if (biome != ChunkBiomeType.Mountain) return sign * halfWidth;

            const float foot = 35f;
            float roadX = Mathf.Lerp(entryX, exitX, ProxyRoadT(z, length));
            float x = roadX + sign * foot;
            return Mathf.Lerp(sign * halfWidth, x, ProxyMountainWeight(prev, next, z, length));
        }

        /// <summary>Same entry -> exit interpolation as RoadSpline's base line (level 60m at both ends).</summary>
        private static float ProxyRoadT(float z, float length)
        {
            const float edge = 60f;
            return Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((z - edge) / (length - 2f * edge)));
        }

        private static float ProxyRoadY(float z, float length, float entryY, float exitY, float baseline)
        {
            if (float.IsNaN(entryY)) entryY = baseline;
            if (float.IsNaN(exitY)) exitY = baseline;
            return Mathf.Lerp(entryY, exitY, ProxyRoadT(z, length));
        }

        /// <summary>Same fade as ChunkBoundaryGenerator's mountain weight toward non-mountain neighbors.</summary>
        private static float ProxyMountainWeight(ChunkBiomeType prev, ChunkBiomeType next, float z, float length)
        {
            const float transition = 220f;
            return Mathf.Min(
                prev == ChunkBiomeType.Mountain ? 1f : Mathf.SmoothStep(0f, 1f, z / transition),
                next == ChunkBiomeType.Mountain ? 1f : Mathf.SmoothStep(0f, 1f, (length - z) / transition));
        }

        /// <summary>Close to the terrain's grass layer color.</summary>
        private static readonly Color ProxyGroundColor = new Color(0.24f, 0.40f, 0.20f);
        private static readonly Color ProxySnowGroundColor = new Color(0.90f, 0.92f, 0.95f);

        /// <summary>
        /// Everything one side strip needs to know about its chunk. Built from a real chunk, or from the
        /// biome plan alone for a proxy (a chunk that is not loaded yet).
        /// </summary>
        private struct SideContext
        {
            public int index;
            public ChunkBiomeType biome, prev, next;
            public MountainVariant variant, prevVariant, nextVariant;
            public float baseline, length, halfWidth;
            public System.Func<float, float> innerX;     // localZ -> signed chunk-local X where the strip starts
                                                         // (±halfWidth = terrain edge; closer to the road inside mountain chunks)
            public System.Func<float, float, float> edgeY; // (x, localZ) -> chunk-local terrain height where the strip starts
            public System.Func<float, float> groundWeight; // localZ -> 0..1: mountain heights measured from the strip's foot instead of the baseline
            public System.Func<float, float> seaWeight;  // localZ -> 0..1 (coast sea side)
            public FieldEdgeGenerator river;             // field river valley, null if none
        }

        private Mesh BuildSideMesh(SideContext ctx, BiomeBackdropSettings st, int worldSeed, int side, float sign)
        {
            int rows = Mathf.Max(2, rowsAlongRoad + 1);
            int cols = Mathf.Max(2, columnsOutward + 1);

            // Noise offsets depend only on the world seed + side, never on the chunk -> seamless across chunks
            var rng = new SeededRandom(SeededRandom.Combine(SeededRandom.Combine(worldSeed, "backdrop"), side));
            float ox = rng.Range(0f, 5000f), oy = rng.Range(0f, 5000f);
            float dx = rng.Range(0f, 5000f), dy = rng.Range(0f, 5000f);
            float cx = rng.Range(0f, 5000f);

            const float seabedY = -2f;

            // Different biome next door (e.g. forest mountains -> field hills): both chunks use a 50/50 mix
            // at the shared seam and fade to their own look over biomeBlendDistance
            bool prevDiffers = ctx.prev != ctx.biome || (ctx.prev == ChunkBiomeType.Mountain && ctx.prevVariant != ctx.variant);
            bool nextDiffers = ctx.next != ctx.biome || (ctx.next == ChunkBiomeType.Mountain && ctx.nextVariant != ctx.variant);
            BiomeBackdropSettings prevSt = prevDiffers ? GetSettings(ctx.prev, ctx.prevVariant) : null;
            BiomeBackdropSettings nextSt = nextDiffers ? GetSettings(ctx.next, ctx.nextVariant) : null;
            float blend = Mathf.Max(1f, biomeBlendDistance);
            float length = ctx.length, halfWidth = ctx.halfWidth, baseline = ctx.baseline;

            // Grid of shared positions (chunk-local)
            var grid = new Vector3[rows, cols];
            var rowBase = new float[rows]; // height the mountains of each row are measured from
            for (int r = 0; r < rows; r++)
            {
                float localZ = r / (rows - 1f) * length;
                float globalZ = ctx.index * length + localZ;
                // Mountain chunks: the strip starts at the mountain foot beside the road, not at the terrain edge
                float innerX = ctx.innerX(localZ);
                float outerX = sign * (halfWidth + stripDepth);
                float edgeY = ctx.edgeY(innerX, localZ);
                // Mountain chunks: the ground is at road level (100m+), mountains rise from there
                float rowBaseline = Mathf.Lerp(baseline, edgeY, ctx.groundWeight(localZ));
                rowBase[r] = rowBaseline;
                float seaWeight = ctx.seaWeight(localZ);
                float ownFromPrev = prevSt != null ? 0.5f + 0.5f * Mathf.SmoothStep(0f, 1f, localZ / blend) : 1f;
                float ownFromNext = nextSt != null ? 0.5f + 0.5f * Mathf.SmoothStep(0f, 1f, (length - localZ) / blend) : 1f;

                for (int c = 0; c < cols; c++)
                {
                    // Denser columns near the start, sparser far away. With the default start (terrain edge) this is
                    // the same layout for every biome, so neighbors' vertices meet at the seams.
                    float t = c / (cols - 1f);
                    float x = Mathf.Lerp(innerX, outerX, Mathf.Pow(t, 1.6f));
                    float u = Mathf.Abs(x - innerX);

                    float y;
                    if (c == 0)
                    {
                        y = edgeY - 0.4f; // tuck slightly under the terrain edge to hide any crack
                    }
                    else
                    {
                        y = HeightAt(st, rowBaseline, edgeY, u, t, globalZ, side, ox, oy, dx, dy, cx);
                        if (ownFromPrev < 1f)
                            y = Mathf.Lerp(HeightAt(prevSt, rowBaseline, edgeY, u, t, globalZ, side, ox, oy, dx, dy, cx), y, ownFromPrev);
                        if (ownFromNext < 1f)
                            y = Mathf.Lerp(HeightAt(nextSt, rowBaseline, edgeY, u, t, globalZ, side, ox, oy, dx, dy, cx), y, ownFromNext);
                        y = Mathf.Lerp(y, Mathf.Min(edgeY, seabedY), seaWeight);
                        if (ctx.river != null) y = ctx.river.ApplyBackdropRiver(x, localZ, u, y);
                    }

                    grid[r, c] = new Vector3(x, y, localZ);
                }
            }

            // Flat-shaded low-poly: every triangle has its own vertices, colored by submesh band
            var vertices = new List<Vector3>((rows - 1) * (cols - 1) * 6);
            var bands = new[] { new List<int>(), new List<int>(), new List<int>() };

            for (int r = 0; r < rows - 1; r++)
            {
                for (int c = 0; c < cols - 1; c++)
                {
                    Vector3 a = grid[r, c], b = grid[r, c + 1], d = grid[r + 1, c], e = grid[r + 1, c + 1];
                    // Winding so faces point up on both sides of the road
                    if (sign > 0f)
                    {
                        AddTriangle(vertices, bands, st, (rowBase[r] + rowBase[r + 1]) * 0.5f, a, d, b);
                        AddTriangle(vertices, bands, st, (rowBase[r] + rowBase[r + 1]) * 0.5f, b, d, e);
                    }
                    else
                    {
                        AddTriangle(vertices, bands, st, (rowBase[r] + rowBase[r + 1]) * 0.5f, a, b, d);
                        AddTriangle(vertices, bands, st, (rowBase[r] + rowBase[r + 1]) * 0.5f, b, e, d);
                    }
                }
            }

            var mesh = new Mesh();
            mesh.SetVertices(vertices);
            mesh.subMeshCount = 3;
            for (int i = 0; i < 3; i++) mesh.SetTriangles(bands[i], i);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static void AddTriangle(List<Vector3> vertices, List<int>[] bands, BiomeBackdropSettings st, float baseline,
            Vector3 p0, Vector3 p1, Vector3 p2)
        {
            Vector3 normal = Vector3.Cross(p1 - p0, p2 - p0).normalized;
            float h = (p0.y + p1.y + p2.y) / 3f - baseline;

            int band = 0;
            if (h > st.snowLine && normal.y > 0.45f) band = 2;
            else if (h > st.rockLine || normal.y < st.rockSlope) band = 1;

            int start = vertices.Count;
            vertices.Add(p0);
            vertices.Add(p1);
            vertices.Add(p2);
            bands[band].Add(start);
            bands[band].Add(start + 1);
            bands[band].Add(start + 2);
        }

        /// <summary>
        /// Backdrop height (chunk-local Y) at distance u outward from the terrain edge for one biome's settings.
        /// Noise offsets come from the world seed only, so every chunk evaluates the same landscape.
        /// </summary>
        private static float HeightAt(BiomeBackdropSettings st, float baseline, float edgeY, float u, float t, float globalZ,
            int side, float ox, float oy, float dx, float dy, float cx)
        {
            float ridge = Ridge(ox + globalZ * st.ridgeFrequency, oy + u * st.ridgeFrequency);
            float detail = Mathf.PerlinNoise(dx + globalZ * 0.009f, dy + u * 0.009f);
            // Occasional valleys between massifs so the skyline is not a flat wall
            float massif = Mathf.Lerp(0.45f, 1f, Mathf.PerlinNoise(cx + globalZ * 0.0011f, 3.7f + side));
            float mountain = (st.baseHeight + ridge * ridge * st.peakAmplitude * massif + detail * st.detailAmplitude)
                             * Mathf.Lerp(0.6f, 1f, t);

            float target = Mathf.Max(baseline + mountain, edgeY + st.minAboveEdge);
            float rise = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(u / Mathf.Max(1f, st.riseDistance)));
            return Mathf.Lerp(edgeY, target, rise);
        }

        private static float Ridge(float x, float y)
        {
            float n = Mathf.PerlinNoise(x, y);
            return 1f - Mathf.Abs(n * 2f - 1f);
        }

        private static float SampleEdgeHeight(Chunk chunk, Terrain terrain, float localX, float localZ)
        {
            if (terrain == null) return chunk.baseElevation;
            Vector3 world = chunk.transform.TransformPoint(new Vector3(localX, 0f, localZ));
            float worldY = terrain.SampleHeight(world) + terrain.transform.position.y;
            return chunk.transform.InverseTransformPoint(new Vector3(world.x, worldY, world.z)).y;
        }

        [Tooltip("Alpin (karsız) dağ alt tipinin arka plan ayarları (Mountain listedeki ayar karlı alt tip içindir)")]
        public BiomeBackdropSettings alpineMountainSettings = BiomeBackdropSettings.AlpineMountain();

        private BiomeBackdropSettings GetSettings(ChunkBiomeType biome, MountainVariant variant)
        {
            if (biome == ChunkBiomeType.Mountain && variant == MountainVariant.Alpine && alpineMountainSettings != null)
                return alpineMountainSettings;

            if (biomeSettings != null)
            {
                for (int i = 0; i < biomeSettings.Count; i++)
                {
                    if (biomeSettings[i] != null && biomeSettings[i].biome == biome)
                        return biomeSettings[i];
                }
            }

            // Not in the (possibly older, serialized) list: built-in default for that biome, else the first entry
            BiomeBackdropSettings builtIn = BiomeBackdropSettings.DefaultFor(biome);
            if (builtIn != null) return builtIn;
            return biomeSettings != null && biomeSettings.Count > 0 ? biomeSettings[0] : null;
        }

        private static float s_requiredFarClip;
        private static bool s_cameraHookRegistered;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetCameraHook()
        {
            s_requiredFarClip = 0f;
        }

        private void EnsureCameraFarClip()
        {
            s_requiredFarClip = Mathf.Max(s_requiredFarClip, requiredCameraFarClip);
            if (!s_cameraHookRegistered)
            {
                // Runs before culling of every camera, so cameras enabled later (vehicle cam, etc.) are covered too
                UnityEngine.Rendering.RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
                s_cameraHookRegistered = true;
            }
        }

        private static void OnBeginCameraRendering(UnityEngine.Rendering.ScriptableRenderContext context, Camera cam)
        {
            if (!Application.isPlaying || cam == null || cam.cameraType != CameraType.Game) return;
            if (cam.farClipPlane < s_requiredFarClip) cam.farClipPlane = s_requiredFarClip;
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
}
