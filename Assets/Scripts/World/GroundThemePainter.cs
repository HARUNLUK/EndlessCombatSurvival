using UnityEngine;
using EndlessSurvival.World.Road;

namespace EndlessSurvival.World
{
    /// <summary>
    /// Ground colour of the non-green chunk types (Forest terrain engine, different paint):
    ///  - Çorak Arazi (Desert): sand with patches of dry cracked soil, red-brown rock on steep faces (cliffs too).
    ///  - Harabe Kasaba (RuinedCity): dry yellow grass, bare dirt patches, gravel / rubble along the road.
    /// Toward a neighbour of another biome the paint fades back to the plain grass layer over <see cref="SeamFade"/>,
    /// so the seam matches the neighbour's grass. Noise uses world coordinates, so patches continue across seams.
    /// Layers: [grass (the terrain's own first layer), A, B, C].
    /// </summary>
    public static class GroundThemePainter
    {
        public const float SeamFade = 90f;

        private static readonly Color DesertSand = new Color(0.74f, 0.63f, 0.43f);
        private static readonly Color DesertSoil = new Color(0.55f, 0.41f, 0.29f);
        private static readonly Color DesertRock = new Color(0.53f, 0.37f, 0.27f);
        private static readonly Color TownDryGrass = new Color(0.52f, 0.49f, 0.29f);
        private static readonly Color TownDirt = new Color(0.43f, 0.36f, 0.27f);
        private static readonly Color TownGravel = new Color(0.44f, 0.43f, 0.41f);

        /// <summary>Paints the chunk if it has a ground theme. False = not a themed chunk (caller paints as before).</summary>
        public static bool TryPaint(Terrain terrain, float[,] heights)
        {
            if (terrain == null || terrain.terrainData == null || heights == null) return false;
            var chunk = terrain.GetComponentInParent<Chunk>();
            if (chunk == null || !chunk.HasGroundTheme) return false;

            TerrainData td = terrain.terrainData;
            TerrainLayer[] current = td.terrainLayers;
            if (current == null || current.Length == 0 || current[0] == null) return false;
            TerrainLayer grass = current[0];

            bool desert = chunk.biomeType == ChunkBiomeType.Desert;
            TerrainLayer a = desert ? TerrainLayerLibrary.Get("Layer_DesertSand", DesertSand) : TerrainLayerLibrary.Get("Layer_TownDryGrass", TownDryGrass);
            TerrainLayer b = desert ? TerrainLayerLibrary.Get("Layer_DesertSoil", DesertSoil) : TerrainLayerLibrary.Get("Layer_TownDirt", TownDirt);
            TerrainLayer c = desert ? TerrainLayerLibrary.Get("Layer_DesertRock", DesertRock) : TerrainLayerLibrary.Get("Layer_TownGravel", TownGravel);
            if (a == null || b == null || c == null) return false;

            if (current.Length != 4 || current[1] != a || current[2] != b || current[3] != c)
                td.terrainLayers = new[] { grass, a, b, c };
            RoadTerrainAdapter.LimitRuntimeAlphamapResolution(td);
            int res = td.alphamapResolution;

            int hRes = heights.GetLength(0);
            Vector3 size = td.size;
            float cell = size.x / (hRes - 1);
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

            // Road centre X (chunk-local) per splat row, for the town's gravel verge
            var spline = chunk.GetComponentInChildren<RoadSpline>();
            Transform ct = chunk.transform;
            Vector3 terrainLocal = ct.InverseTransformPoint(terrain.transform.position);
            float len = Mathf.Max(1f, chunk.chunkLength);
            bool fadeIn = !SameTheme(chunk, chunk.PrevBiome);
            bool fadeOut = !SameTheme(chunk, chunk.NextBiome);
            // First chunk after a green run: the whole chunk is the transition (grass gives way to sand gradually)
            bool longFadeIn = fadeIn && chunk.IsTransitionChunk && !Chunk.IsThemedBiome(chunk.PrevBiome);
            // Toward a green run the next chunk carries this ground over (PaintCarryOver), so no fade here
            if (fadeOut && NextCarriesOver(chunk)) fadeOut = false;

            var alphas = new float[res, res, 4];
            for (int y = 0; y < res; y++)
            {
                float nz = y / (res - 1f);
                float localZ = terrainLocal.z + nz * size.z;
                float theme = 1f;
                if (fadeIn && !longFadeIn) theme = Mathf.Min(theme, Mathf.SmoothStep(0f, 1f, localZ / SeamFade));
                if (fadeOut) theme = Mathf.Min(theme, Mathf.SmoothStep(0f, 1f, (len - localZ) / SeamFade));

                float roadX = 0f;
                if (!desert && spline != null)
                {
                    Transform st = spline.transform;
                    Vector3 w = ct.TransformPoint(new Vector3(0f, 0f, Mathf.Clamp(localZ, 0f, len)));
                    Vector3 p = st.TransformPoint(spline.GetPointAtZ(st.InverseTransformPoint(w).z));
                    roadX = ct.InverseTransformPoint(p).x;
                }

                for (int x = 0; x < res; x++)
                {
                    float nx = x / (res - 1f);
                    float localX = terrainLocal.x + nx * size.x;
                    Vector3 world = ct.TransformPoint(new Vector3(localX, 0f, localZ));
                    float slope = Sample(slopes, hRes, nx, nz);
                    float t = longFadeIn ? Mathf.Min(theme, chunk.TransitionT(world)) : theme;

                    float wa, wb, wc;
                    if (desert)
                    {
                        float patch = Mathf.PerlinNoise(world.x * 0.011f + 31.7f, world.z * 0.011f + 12.3f);
                        float fine = Mathf.PerlinNoise(world.x * 0.06f + 5.1f, world.z * 0.06f + 77.2f);
                        wc = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.5f, 1.05f, slope));
                        wb = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.48f, 0.72f, patch * 0.8f + fine * 0.2f)) * 0.9f * (1f - wc);
                        wa = Mathf.Max(0f, 1f - wb - wc);
                    }
                    else
                    {
                        float lateral = Mathf.Abs(localX - roadX);
                        float patch = Mathf.PerlinNoise(world.x * 0.02f + 51.3f, world.z * 0.02f + 8.9f);
                        float fine = Mathf.PerlinNoise(world.x * 0.09f + 13.1f, world.z * 0.09f + 41.6f);
                        wc = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(30f, 16f, lateral)) * Mathf.Lerp(0.55f, 1f, fine);
                        float dirt = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.5f, 0.75f, patch));
                        dirt = Mathf.Max(dirt, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.55f, 1.1f, slope)));
                        wb = dirt * (1f - wc);
                        wa = Mathf.Max(0f, 1f - wb - wc);
                    }

                    alphas[y, x, 0] = 1f - t;
                    alphas[y, x, 1] = wa * t;
                    alphas[y, x, 2] = wb * t;
                    alphas[y, x, 3] = wc * t;
                }
            }
            td.SetAlphamaps(0, 0, alphas);
            return true;
        }

        /// <summary>The next chunk is a procedural, unthemed run start: it paints this chunk's ground fading out (PaintCarryOver).</summary>
        private static bool NextCarriesOver(Chunk chunk) =>
            chunk.hasNeighborInfo && chunk.nextType.IsValid && !chunk.nextType.IsSpecial && !chunk.sourceType.IsSpecial
            && !Chunk.IsThemedBiome(chunk.NextBiome);

        /// <summary>
        /// First chunk of a green run after a themed run (desert): the previous run's ground is painted over this
        /// chunk's own paint and fades out along it (<see cref="Chunk.TransitionT"/>). Call after the normal painting.
        /// </summary>
        public static void PaintCarryOver(Terrain terrain, float[,] heights)
        {
            if (terrain == null || terrain.terrainData == null || heights == null) return;
            var chunk = terrain.GetComponentInParent<Chunk>();
            if (chunk == null || chunk.HasGroundTheme || !chunk.IsTransitionChunk || !Chunk.IsThemedBiome(chunk.prevBiome)) return;

            TerrainData td = terrain.terrainData;
            TerrainLayer[] current = td.terrainLayers;
            if (current == null || current.Length == 0) return;
            bool desert = chunk.prevBiome == ChunkBiomeType.Desert;
            TerrainLayer a = desert ? TerrainLayerLibrary.Get("Layer_DesertSand", DesertSand) : TerrainLayerLibrary.Get("Layer_TownDryGrass", TownDryGrass);
            TerrainLayer b = desert ? TerrainLayerLibrary.Get("Layer_DesertSoil", DesertSoil) : TerrainLayerLibrary.Get("Layer_TownDirt", TownDirt);
            TerrainLayer c = desert ? TerrainLayerLibrary.Get("Layer_DesertRock", DesertRock) : TerrainLayerLibrary.Get("Layer_TownGravel", TownGravel);
            if (a == null || b == null || c == null) return;

            // Existing paint (field soil, beach sand, grass...) is kept and scaled down where the old ground shows
            int res = td.alphamapResolution;
            float[,,] old = td.GetAlphamaps(0, 0, res, res);
            int n = current.Length;
            int ia = System.Array.IndexOf(current, a), ib = System.Array.IndexOf(current, b), ic = System.Array.IndexOf(current, c);
            var layers = new System.Collections.Generic.List<TerrainLayer>(current);
            if (ia < 0) { ia = layers.Count; layers.Add(a); }
            if (ib < 0) { ib = layers.Count; layers.Add(b); }
            if (ic < 0) { ic = layers.Count; layers.Add(c); }
            int m = layers.Count;

            int hRes = heights.GetLength(0);
            Vector3 size = td.size;
            float cell = size.x / (hRes - 1);
            Transform ct = chunk.transform;
            Vector3 terrainLocal = ct.InverseTransformPoint(terrain.transform.position);
            var alphas = new float[res, res, m];
            for (int y = 0; y < res; y++)
            {
                float nz = y / (res - 1f);
                float localZ = terrainLocal.z + nz * size.z;
                for (int x = 0; x < res; x++)
                {
                    float nx = x / (res - 1f);
                    float localX = terrainLocal.x + nx * size.x;
                    Vector3 world = ct.TransformPoint(new Vector3(localX, 0f, localZ));
                    float w = 1f - chunk.TransitionT(world);
                    for (int k = 0; k < n; k++) alphas[y, x, k] = old[y, x, k] * (1f - w);
                    if (w <= 0f) continue;

                    // Same look as the themed chunk (world-space noise continues across the seam)
                    int hx = Mathf.Clamp(Mathf.RoundToInt(nx * (hRes - 1)), 1, hRes - 2), hz = Mathf.Clamp(Mathf.RoundToInt(nz * (hRes - 1)), 1, hRes - 2);
                    float dhx = (heights[hz, hx + 1] - heights[hz, hx - 1]) * size.y / (2f * cell);
                    float dhz = (heights[hz + 1, hx] - heights[hz - 1, hx]) * size.y / (2f * cell);
                    float slope = Mathf.Sqrt(dhx * dhx + dhz * dhz);
                    float patch = Mathf.PerlinNoise(world.x * 0.011f + 31.7f, world.z * 0.011f + 12.3f);
                    float fine = Mathf.PerlinNoise(world.x * 0.06f + 5.1f, world.z * 0.06f + 77.2f);
                    float wc = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.5f, 1.05f, slope));
                    float wb = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.48f, 0.72f, patch * 0.8f + fine * 0.2f)) * 0.9f * (1f - wc);
                    float wa = Mathf.Max(0f, 1f - wb - wc);
                    alphas[y, x, ia] += wa * w;
                    alphas[y, x, ib] += wb * w;
                    alphas[y, x, ic] += wc * w;
                }
            }
            if (m != n) td.terrainLayers = layers.ToArray();
            td.SetAlphamaps(0, 0, alphas);
        }

        /// <summary>The neighbour's biome paints the same ground (no fade at that seam).</summary>
        private static bool SameTheme(Chunk chunk, ChunkBiomeType neighbour) => neighbour == chunk.biomeType;

        private static float Sample(float[,] values, int hRes, float nx, float nz)
        {
            float fx = nx * (hRes - 1), fz = nz * (hRes - 1);
            int x0 = Mathf.FloorToInt(fx), z0 = Mathf.FloorToInt(fz);
            int x1 = Mathf.Min(x0 + 1, hRes - 1), z1 = Mathf.Min(z0 + 1, hRes - 1);
            float tx = fx - x0, tz = fz - z0;
            return Mathf.Lerp(Mathf.Lerp(values[z0, x0], values[z0, x1], tx), Mathf.Lerp(values[z1, x0], values[z1, x1], tx), tz);
        }
    }
}
