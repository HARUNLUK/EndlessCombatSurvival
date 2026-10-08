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
            if (biome != ChunkBiomeType.Field) return null;

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
            BiomeBackdropSettings.DefaultFor(ChunkBiomeType.Field)
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

            BiomeBackdropSettings settings = GetSettings(chunk.biomeType);
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

            for (int s = 0; s < 2; s++)
            {
                float sign = s == 0 ? -1f : 1f;
                Mesh mesh = BuildSideMesh(chunk, terrain, settings, worldSeed, s, sign, halfWidth, length);
                mesh.name = $"Backdrop_{(s == 0 ? "Left" : "Right")}_Chunk{chunk.ChunkIndex}";
                _ownedMeshes.Add(mesh);

                var go = new GameObject(s == 0 ? "Mountains_Left" : "Mountains_Right");
                go.transform.SetParent(container.transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterials = materials;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

                if (addCollider)
                {
                    go.AddComponent<MeshCollider>().sharedMesh = mesh;
                }
            }

            if (Application.isPlaying) EnsureCameraFarClip();
        }

        private Mesh BuildSideMesh(Chunk chunk, Terrain terrain, BiomeBackdropSettings st, int worldSeed, int side, float sign,
            float halfWidth, float length)
        {
            int rows = Mathf.Max(2, rowsAlongRoad + 1);
            int cols = Mathf.Max(2, columnsOutward + 1);

            // Noise offsets depend only on the world seed + side, never on the chunk -> seamless across chunks
            var rng = new SeededRandom(SeededRandom.Combine(SeededRandom.Combine(worldSeed, "backdrop"), side));
            float ox = rng.Range(0f, 5000f), oy = rng.Range(0f, 5000f);
            float dx = rng.Range(0f, 5000f), dy = rng.Range(0f, 5000f);
            float cx = rng.Range(0f, 5000f);

            // On a coast's sea side the mountains sink under the water (headlands where the coast fades in/out)
            var boundary = chunk.GetComponent<ChunkBoundaryGenerator>();
            const float seabedY = -2f;

            // Field river crossings continue as a valley through the backdrop hills
            var fieldEdges = chunk.GetComponent<FieldEdgeGenerator>();
            bool fieldRiver = fieldEdges != null && fieldEdges.HasRiver;

            // Different biome next door (e.g. forest mountains -> field hills): both chunks use a 50/50 mix
            // at the shared seam and fade to their own look over biomeBlendDistance
            BiomeBackdropSettings prevSt = chunk.PrevBiome != chunk.biomeType ? GetSettings(chunk.PrevBiome) : null;
            BiomeBackdropSettings nextSt = chunk.NextBiome != chunk.biomeType ? GetSettings(chunk.NextBiome) : null;
            float blend = Mathf.Max(1f, biomeBlendDistance);

            // Grid of shared positions (chunk-local)
            var grid = new Vector3[rows, cols];
            for (int r = 0; r < rows; r++)
            {
                float localZ = r / (rows - 1f) * length;
                float globalZ = chunk.ChunkIndex * length + localZ;
                float edgeY = SampleEdgeHeight(chunk, terrain, sign * (halfWidth - 0.1f), localZ);
                float seaWeight = boundary != null ? boundary.GetSeaWeight(sign, localZ) : 0f;
                float ownFromPrev = prevSt != null ? 0.5f + 0.5f * Mathf.SmoothStep(0f, 1f, localZ / blend) : 1f;
                float ownFromNext = nextSt != null ? 0.5f + 0.5f * Mathf.SmoothStep(0f, 1f, (length - localZ) / blend) : 1f;

                for (int c = 0; c < cols; c++)
                {
                    // Denser columns near the edge, sparser far away (same layout for every biome)
                    float t = c / (cols - 1f);
                    float u = stripDepth * Mathf.Pow(t, 1.6f);
                    float x = sign * (halfWidth + u);

                    float y;
                    if (c == 0)
                    {
                        y = edgeY - 0.4f; // tuck slightly under the terrain edge to hide any crack
                    }
                    else
                    {
                        y = HeightAt(st, chunk.baseElevation, edgeY, u, t, globalZ, side, ox, oy, dx, dy, cx);
                        if (ownFromPrev < 1f)
                            y = Mathf.Lerp(HeightAt(prevSt, chunk.baseElevation, edgeY, u, t, globalZ, side, ox, oy, dx, dy, cx), y, ownFromPrev);
                        if (ownFromNext < 1f)
                            y = Mathf.Lerp(HeightAt(nextSt, chunk.baseElevation, edgeY, u, t, globalZ, side, ox, oy, dx, dy, cx), y, ownFromNext);
                        y = Mathf.Lerp(y, Mathf.Min(edgeY, seabedY), seaWeight);
                        if (fieldRiver) y = fieldEdges.ApplyBackdropRiver(x, localZ, u, y);
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
                        AddTriangle(vertices, bands, st, chunk.baseElevation, a, d, b);
                        AddTriangle(vertices, bands, st, chunk.baseElevation, b, d, e);
                    }
                    else
                    {
                        AddTriangle(vertices, bands, st, chunk.baseElevation, a, b, d);
                        AddTriangle(vertices, bands, st, chunk.baseElevation, b, e, d);
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

        private BiomeBackdropSettings GetSettings(ChunkBiomeType biome)
        {
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
