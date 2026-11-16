using System.Collections.Generic;
using UnityEngine;
using EndlessSurvival.World.Road;
using EndlessSurvival.World.POI;

namespace EndlessSurvival.World
{
    /// <summary>Shape of a type's side roads.</summary>
    public enum SideRoadStyle
    {
        /// <summary>Town street: straight, at right angles to the main road, houses along it (Settlements).</summary>
        Street,
        /// <summary>Dirt track: leaves at an angle and winds into the forest / desert.</summary>
        Track,
        /// <summary>Field road: almost straight, out to a farm.</summary>
        FarmRoad
    }

    /// <summary>Side roads of a chunk type. The main road always runs on; these branch off it and end inside the chunk.</summary>
    [System.Serializable]
    public class SideRoadSettings
    {
        public bool enabled;
        public SideRoadStyle style = SideRoadStyle.Track;
        [Tooltip("Bir chunk'ta yan yol olma ihtimali")]
        [Range(0f, 1f)] public float chance = 0.5f;
        [Range(1, 6)] public int minCount = 1;
        [Range(1, 6)] public int maxCount = 2;
        [Tooltip("Yolun yarı genişliği (m)")]
        public float halfWidth = 2.2f;
        public float minLength = 120f;
        public float maxLength = 260f;
        [Tooltip("Sokak: aynı yerden karşı tarafa da sokak açılma (dört yol ağzı) ihtimali")]
        [Range(0f, 1f)] public float crossChance = 0.35f;
        [Tooltip("Yolun sonunda bir şey (bina...) olma ihtimali; gerisi boş biter")]
        [Range(0f, 1f)] public float endContentChance = 0.25f;
        [Tooltip("Toprak / çakıl yolun rengi")]
        public Color surfaceColor = new Color(0.36f, 0.30f, 0.23f);
        [Tooltip("Yan yolun asfalt olma ihtimali (sokaklar hep asfalt)")]
        [Range(0f, 1f)] public float pavedChance = 0.3f;
        public Color pavedColor = new Color(0.22f, 0.22f, 0.23f);
    }

    /// <summary>One side road: a chunk-local path (x, z) with its ground height, from the main road's edge outward.</summary>
    public class SideRoad
    {
        public SideRoadStyle style;
        public float side;               // -1 left, +1 right of the main road
        public float junctionZ;          // chunk-local z where it leaves the main road
        public float halfWidth;
        public bool paved;               // asphalt (else dirt / gravel)
        public bool endContent;          // something stands at its end (Settlements builds it)
        public int seed;
        public readonly List<Vector3> points = new List<Vector3>(); // chunk-local; y = road surface ground height
        private Rect _bounds;

        public Vector3 End => points[points.Count - 1];
        public Vector3 EndDirection
        {
            get
            {
                Vector3 d = points[points.Count - 1] - points[Mathf.Max(0, points.Count - 3)];
                d.y = 0f;
                return d.sqrMagnitude > 0.001f ? d.normalized : Vector3.forward;
            }
        }

        public float Length
        {
            get
            {
                float l = 0f;
                for (int i = 1; i < points.Count; i++) l += Flat(points[i] - points[i - 1]).magnitude;
                return l;
            }
        }

        public void UpdateBounds()
        {
            float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
            foreach (var p in points)
            {
                minX = Mathf.Min(minX, p.x); maxX = Mathf.Max(maxX, p.x);
                minZ = Mathf.Min(minZ, p.z); maxZ = Mathf.Max(maxZ, p.z);
            }
            _bounds = Rect.MinMaxRect(minX, minZ, maxX, maxZ);
        }

        /// <summary>Horizontal distance from a chunk-local point to the path.</summary>
        public float Distance(float x, float z) => Distance(x, z, out _, out _);

        /// <summary>Distance to the path, the nearest segment and the position along it (0..1).</summary>
        public float Distance(float x, float z, out int segment, out float t)
        {
            segment = 0; t = 0f;
            if (points.Count < 2) return float.MaxValue;
            const float pad = 40f;
            if (x < _bounds.xMin - pad || x > _bounds.xMax + pad || z < _bounds.yMin - pad || z > _bounds.yMax + pad) return float.MaxValue;
            float best = float.MaxValue;
            for (int i = 0; i < points.Count - 1; i++)
            {
                Vector2 a = new Vector2(points[i].x, points[i].z), b = new Vector2(points[i + 1].x, points[i + 1].z);
                Vector2 ab = b - a;
                float len2 = ab.sqrMagnitude;
                float u = len2 > 0.0001f ? Mathf.Clamp01(Vector2.Dot(new Vector2(x, z) - a, ab) / len2) : 0f;
                float d = (a + ab * u - new Vector2(x, z)).sqrMagnitude;
                if (d < best) { best = d; segment = i; t = u; }
            }
            return Mathf.Sqrt(best);
        }

        /// <summary>Ground height of the path at a segment position.</summary>
        public float HeightAt(int segment, float t) => Mathf.Lerp(points[segment].y, points[Mathf.Min(segment + 1, points.Count - 1)].y, t);

        private static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }
    }

    /// <summary>
    /// Side roads branching off the main road (town streets, forest / desert tracks, field roads). Built right after the
    /// chunk's terrain is conformed to the main road: plans the paths from the chunk seed, levels the ground along them,
    /// opens the guardrail at the junctions and lays a flat road ribbon on the ground. Vegetation, wrecks, loot and
    /// settlement lots keep off them (Chunk.NearSideRoad); Settlements builds the houses along town streets and the
    /// occasional building at a road's end.
    /// </summary>
    public static class SideRoads
    {
        public const string CONTAINER_NAME = "SideRoads";
        private const float Step = 8f;           // path point spacing (m)
        private const float MaxLocalX = 290f;    // stay inside the edge cliffs / fences (chunk-local |x|)
        private const float EndMarginZ = 30f;    // stay away from the chunk seams
        private const float BlendWidth = 7f;     // ground blend beside the road surface

        /// <summary>Plans and builds the chunk's side roads (no-op if its type has none). Call after ConformTerrainToRoad.</summary>
        public static void Build(Chunk chunk, Terrain terrain, RoadSpline spline, RoadGenerator roadGen)
        {
            if (chunk == null) return;
            chunk.sideRoads.Clear();
            Transform old = chunk.transform.Find(CONTAINER_NAME);
            if (old != null) Object.Destroy(old.gameObject);
            var s = chunk.sideRoadSettings;
            if (s == null || !s.enabled || terrain == null || terrain.terrainData == null || spline == null) return;

            var rng = new SeededRandom(chunk.ChunkSeed).SubStream("side_roads");
            if (!rng.Chance(s.chance)) return;

            var ctx = new PlanCtx(chunk, terrain, spline, roadGen);
            int want = rng.Range(s.minCount, Mathf.Max(s.minCount, s.maxCount) + 1);
            int attempts = want * 6;
            for (int a = 0; a < attempts && chunk.sideRoads.Count < want; a++)
            {
                float side = rng.Chance(0.5f) ? -1f : 1f;
                float z = rng.Range(EndMarginZ + 30f, chunk.chunkLength - EndMarginZ - 30f);
                var road = TryPlan(ctx, s, rng, z, side);
                if (road == null) continue;
                chunk.sideRoads.Add(road);

                // Town: now and then the street goes on across the main road (crossroads)
                if (s.style == SideRoadStyle.Street && rng.Chance(s.crossChance) && chunk.sideRoads.Count < want + 1)
                {
                    var across = TryPlan(ctx, s, rng, z, -side);
                    if (across != null) chunk.sideRoads.Add(across);
                }
            }
            if (chunk.sideRoads.Count == 0) return;

            ApplyTerrain(chunk, terrain, ctx);
            if (roadGen != null && roadGen.hasGuardrails) roadGen.GenerateGuardrails();
            BuildMeshes(chunk, terrain, s);
            Debug.Log($"[SideRoads] chunk {chunk.ChunkIndex} ({chunk.typeName}): {chunk.sideRoads.Count} side road(s), " +
                      $"lengths {string.Join("/", chunk.sideRoads.ConvertAll(r => Mathf.RoundToInt(r.Length).ToString()))}");
        }

        // ------------------------------------------------------------------ planning

        private class PlanCtx
        {
            public readonly Chunk chunk;
            public readonly Terrain terrain;
            public readonly RoadSpline spline;
            public readonly float roadHalfWidth;
            public readonly ChunkBoundaryGenerator boundary;
            public readonly FieldEdgeGenerator field;
            public readonly List<Vector3> pois = new List<Vector3>(); // chunk-local
            public readonly float[,] heights;
            public readonly int res;
            public readonly Vector3 size;
            public readonly float offsetX;

            public PlanCtx(Chunk chunk, Terrain terrain, RoadSpline spline, RoadGenerator roadGen)
            {
                this.chunk = chunk;
                this.terrain = terrain;
                this.spline = spline;
                roadHalfWidth = roadGen != null ? roadGen.GetTotalHalfWidth() : 8.5f;
                boundary = chunk.GetComponent<ChunkBoundaryGenerator>();
                field = chunk.GetComponent<FieldEdgeGenerator>();
                foreach (var poi in chunk.GetComponentsInChildren<PointOfInterest>(true))
                    if (poi.gameObject.activeInHierarchy) pois.Add(chunk.transform.InverseTransformPoint(poi.transform.position));
                var td = terrain.terrainData;
                res = td.heightmapResolution;
                size = td.size;
                offsetX = terrain.transform.localPosition.x;
                heights = td.GetHeights(0, 0, res, res);
            }

            /// <summary>Ground height (terrain-local m) at a chunk-local point, bilinear.</summary>
            public float Ground(float x, float z)
            {
                float fx = Mathf.Clamp01((x - offsetX) / size.x) * (res - 1);
                float fz = Mathf.Clamp01(z / size.z) * (res - 1);
                int x0 = Mathf.Min((int)fx, res - 2), z0 = Mathf.Min((int)fz, res - 2);
                float tx = fx - x0, tz = fz - z0;
                float h = Mathf.Lerp(Mathf.Lerp(heights[z0, x0], heights[z0, x0 + 1], tx),
                                     Mathf.Lerp(heights[z0 + 1, x0], heights[z0 + 1, x0 + 1], tx), tz);
                return h * size.y;
            }

            /// <summary>Main road centre (chunk-local), its forward and right direction at a chunk-local z.</summary>
            public void Road(float z, out Vector3 center, out Vector3 fwd, out Vector3 right)
            {
                Transform ct = chunk.transform, st = spline.transform;
                float z0 = Mathf.Clamp(z, 0f, chunk.chunkLength - 2f);
                Vector3 a = ct.InverseTransformPoint(st.TransformPoint(spline.GetPointAtZ(st.InverseTransformPoint(ct.TransformPoint(new Vector3(0f, 0f, z0))).z)));
                Vector3 b = ct.InverseTransformPoint(st.TransformPoint(spline.GetPointAtZ(st.InverseTransformPoint(ct.TransformPoint(new Vector3(0f, 0f, z0 + 2f))).z)));
                center = a;
                fwd = b - a; fwd.y = 0f;
                fwd = fwd.sqrMagnitude > 0.0001f ? fwd.normalized : Vector3.forward;
                right = Vector3.Cross(Vector3.up, fwd).normalized;
            }

            public float RoadX(float z) { Road(z, out Vector3 c, out _, out _); return c.x; }
        }

        private static SideRoad TryPlan(PlanCtx ctx, SideRoadSettings s, SeededRandom rng, float junctionZ, float side)
        {
            Chunk chunk = ctx.chunk;
            // Coast / ledge / river valley: only toward the land
            side = chunk.GetLandSideAt(side, junctionZ);

            // Junctions apart from each other (a crossroads shares its z on the other side)
            foreach (var other in chunk.sideRoads)
            {
                float dz = Mathf.Abs(other.junctionZ - junctionZ);
                if (other.side == side && dz < 70f) return null;
                if (other.side != side && dz > 1f && dz < 30f) return null;
            }

            ctx.Road(junctionZ, out Vector3 center, out Vector3 fwd, out Vector3 right);
            Vector3 outward = right * side;

            // Heading: streets at right angles, field roads nearly so, tracks at an angle toward either end
            float angle = s.style == SideRoadStyle.Street ? rng.Range(-4f, 4f)
                : s.style == SideRoadStyle.FarmRoad ? rng.Range(-15f, 15f)
                : rng.Range(25f, 55f) * (rng.Chance(0.5f) ? 1f : -1f);
            Vector3 dir = Quaternion.AngleAxis(angle * side, Vector3.up) * outward;
            float length = rng.Range(s.minLength, Mathf.Max(s.minLength, s.maxLength));
            // Some side roads are asphalt (a bit wider, tidier edges, gentler bends), the rest dirt / gravel
            bool paved = s.style == SideRoadStyle.Street || rng.Chance(s.pavedChance);

            var road = new SideRoad
            {
                style = s.style, side = side, junctionZ = junctionZ, paved = paved,
                halfWidth = paved && s.style != SideRoadStyle.Street ? s.halfWidth + 0.8f : s.halfWidth,
                seed = rng.Range(int.MinValue, int.MaxValue)
            };
            // Starts under the main road's edge so the two surfaces overlap
            Vector3 p = center + outward * Mathf.Max(1f, ctx.roadHalfWidth - 1.5f);
            p.y = 0f;
            road.points.Add(p);

            float turn = 0f;
            float travelled = 0f;
            float wobblePhase = rng.Range(0f, 100f);
            while (travelled < length)
            {
                if (s.style == SideRoadStyle.Track)
                {
                    // Winding: a slowly changing turn rate
                    turn = Mathf.Lerp(turn, (Mathf.PerlinNoise(wobblePhase, travelled * 0.012f) - 0.5f) * (road.paved ? 12f : 22f), 0.35f);
                    dir = Quaternion.AngleAxis(turn, Vector3.up) * dir;
                }
                // Steer back inside the chunk when nearing a seam, and never back toward the main road
                float zNext = p.z + dir.z * Step;
                if (zNext < EndMarginZ + 15f || zNext > chunk.chunkLength - EndMarginZ - 15f)
                {
                    Vector3 target = outward;
                    dir = Vector3.Slerp(dir, target, 0.45f).normalized;
                }
                if (Vector3.Dot(dir, outward) < 0.25f) dir = Vector3.Slerp(dir, outward, 0.5f).normalized;

                Vector3 next = p + dir * Step;
                if (!PointOk(ctx, next, s.halfWidth, travelled + Step)) break;
                road.points.Add(next);
                p = next;
                travelled += Step;
            }

            if (travelled < Mathf.Min(s.minLength * 0.6f, 70f)) return null;

            // Ground height along the path: smoothed terrain, at the main road bed where it joins
            int n = road.points.Count;
            var raw = new float[n];
            for (int i = 0; i < n; i++) raw[i] = ctx.Ground(road.points[i].x, road.points[i].z);
            float joinY = center.y - 0.1f;
            for (int i = 0; i < n; i++)
            {
                float sum = 0f; int c = 0;
                for (int k = -3; k <= 3; k++)
                {
                    int j = Mathf.Clamp(i + k, 0, n - 1);
                    sum += raw[j]; c++;
                }
                float smooth = sum / c;
                float fromJoin = i * Step;
                float y = Mathf.Lerp(joinY, smooth, Mathf.SmoothStep(0f, 1f, fromJoin / 30f));
                Vector3 q = road.points[i];
                q.y = y;
                road.points[i] = q;
            }

            // Reject (or cut short) where the smoothed path would leave a deep cut / tall fill or get steep
            int keep = n;
            for (int i = 1; i < n; i++)
            {
                float slope = Mathf.Abs(road.points[i].y - road.points[i - 1].y) / Step;
                if (Mathf.Abs(raw[i] - road.points[i].y) > 4.5f || slope > 0.2f) { keep = i; break; }
            }
            if (keep < n) road.points.RemoveRange(keep, n - keep);
            if ((road.points.Count - 1) * Step < Mathf.Min(s.minLength * 0.6f, 70f)) return null;

            // Away from the other side roads
            road.UpdateBounds();
            foreach (var other in chunk.sideRoads)
            {
                for (int i = 4; i < road.points.Count; i++)
                    if (other.Distance(road.points[i].x, road.points[i].z) < other.halfWidth + road.halfWidth + 18f) return null;
            }

            road.endContent = rng.Chance(s.endContentChance);
            return road;
        }

        private static bool PointOk(PlanCtx ctx, Vector3 p, float halfWidth, float travelled)
        {
            Chunk chunk = ctx.chunk;
            if (Mathf.Abs(p.x) > MaxLocalX || p.z < EndMarginZ || p.z > chunk.chunkLength - EndMarginZ) return false;
            // Off the main road (past the first few meters) - a track curving back must not touch it again
            if (travelled > 24f && Mathf.Abs(p.x - ctx.RoadX(p.z)) < ctx.roadHalfWidth + 12f) return false;
            for (int k = -1; k <= 1; k++)
            {
                Vector3 q = p + Vector3.right * (k * (halfWidth + 3f));
                if (ctx.boundary != null && ctx.boundary.IsBlockedForVegetation(q)) return false;
                if (ctx.field != null && ctx.field.IsActive && ctx.field.IsNearRiver(q, 4f)) return false;
                if (chunk.IsInRiver(chunk.transform.TransformPoint(q), 4f)) return false;
            }
            // Sea / water: the ground must stay above the shore
            if (ctx.Ground(p.x, p.z) < chunk.baseElevation - 6f) return false;
            foreach (var poi in ctx.pois)
            {
                float dx = poi.x - p.x, dz = poi.z - p.z;
                if (dx * dx + dz * dz < 38f * 38f) return false;
            }
            return true;
        }

        // ------------------------------------------------------------------ terrain

        private static void ApplyTerrain(Chunk chunk, Terrain terrain, PlanCtx ctx)
        {
            var td = terrain.terrainData;
            int res = ctx.res;
            float[,] heights = ctx.heights;
            float cellX = ctx.size.x / (res - 1), cellZ = ctx.size.z / (res - 1);

            foreach (var road in chunk.sideRoads)
            {
                road.UpdateBounds();
                float reach = road.halfWidth + 1f + BlendWidth;
                float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
                foreach (var p in road.points)
                {
                    minX = Mathf.Min(minX, p.x - reach); maxX = Mathf.Max(maxX, p.x + reach);
                    minZ = Mathf.Min(minZ, p.z - reach); maxZ = Mathf.Max(maxZ, p.z + reach);
                }
                int x0 = Mathf.Clamp(Mathf.FloorToInt((minX - ctx.offsetX) / cellX), 0, res - 1);
                int x1 = Mathf.Clamp(Mathf.CeilToInt((maxX - ctx.offsetX) / cellX), 0, res - 1);
                int z0 = Mathf.Clamp(Mathf.FloorToInt(minZ / cellZ), 0, res - 1);
                int z1 = Mathf.Clamp(Mathf.CeilToInt(maxZ / cellZ), 0, res - 1);

                for (int zi = z0; zi <= z1; zi++)
                {
                    float z = zi * cellZ;
                    float roadX = ctx.RoadX(z);
                    for (int xi = x0; xi <= x1; xi++)
                    {
                        float x = ctx.offsetX + xi * cellX;
                        // Leave the main road bed alone
                        if (Mathf.Abs(x - roadX) < ctx.roadHalfWidth + 1.5f) continue;
                        float d = road.Distance(x, z, out int seg, out float t);
                        if (d > reach) continue;
                        float target = road.HeightAt(seg, t) - 0.05f;
                        float current = heights[zi, xi] * ctx.size.y;
                        float inner = road.halfWidth + 1f;
                        float y = d <= inner ? target : Mathf.Lerp(target, current, Mathf.SmoothStep(0f, 1f, (d - inner) / BlendWidth));
                        heights[zi, xi] = Mathf.Clamp01(y / ctx.size.y);
                    }
                }
            }
            td.SetHeights(0, 0, heights);
            terrain.Flush();
        }

        // ------------------------------------------------------------------ meshes

        private static readonly Dictionary<Color, Material> s_materials = new Dictionary<Color, Material>();

        private static Material SurfaceMaterial(Color color)
        {
            if (s_materials.TryGetValue(color, out var mat) && mat != null) return mat;
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            mat = new Material(shader) { name = "SideRoad_Surface", color = color };
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.05f);
            if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", 0.05f);
            s_materials[color] = mat;
            return mat;
        }

        private static void BuildMeshes(Chunk chunk, Terrain terrain, SideRoadSettings s)
        {
            var root = new GameObject(CONTAINER_NAME).transform;
            root.SetParent(chunk.transform, false);

            int index = 0;
            foreach (var road in chunk.sideRoads)
            {
                var go = new GameObject($"SideRoad_{index++}");
                go.transform.SetParent(root, false);
                Mesh mesh = Ribbon(chunk, terrain, road);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                go.AddComponent<OwnedMeshes>().meshes.Add(mesh);
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = SurfaceMaterial(road.paved ? s.pavedColor : s.surfaceColor);
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
        }

        /// <summary>A flat strip along the path, lying a few cm above the leveled ground (no collider: wheels use the terrain).</summary>
        private static Mesh Ribbon(Chunk chunk, Terrain terrain, SideRoad road)
        {
            // Resample to ~3 m for smooth curves
            var pts = new List<Vector3>();
            for (int i = 0; i < road.points.Count - 1; i++)
            {
                Vector3 a = road.points[i], b = road.points[i + 1];
                int sub = Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(a, b) / 3f));
                for (int k = 0; k < sub; k++) pts.Add(Vector3.Lerp(a, b, k / (float)sub));
            }
            pts.Add(road.points[road.points.Count - 1]);

            float terrainY = terrain.transform.localPosition.y;
            var verts = new List<Vector3>(pts.Count * 2);
            var uvs = new List<Vector2>(pts.Count * 2);
            var tris = new List<int>((pts.Count - 1) * 6);
            float along = 0f;
            for (int i = 0; i < pts.Count; i++)
            {
                Vector3 fwd = (i < pts.Count - 1 ? pts[i + 1] - pts[i] : pts[i] - pts[i - 1]);
                fwd.y = 0f;
                fwd = fwd.sqrMagnitude > 0.0001f ? fwd.normalized : Vector3.forward;
                Vector3 r = Vector3.Cross(Vector3.up, fwd);
                if (i > 0) along += Vector3.Distance(pts[i], pts[i - 1]);
                // Ragged edges: the width breathes a little
                float w = road.paved ? road.halfWidth : road.halfWidth * (0.9f + 0.2f * Mathf.PerlinNoise(road.seed * 0.0001f, along * 0.08f));
                // The last few meters fade to a narrower end
                float endFade = Mathf.Clamp01((pts.Count - 1 - i) * 3f / 8f);
                if (!road.paved) w *= Mathf.Lerp(0.6f, 1f, endFade);
                float y = pts[i].y + terrainY + 0.07f;
                verts.Add(new Vector3(pts[i].x - r.x * w, y, pts[i].z - r.z * w));
                verts.Add(new Vector3(pts[i].x + r.x * w, y, pts[i].z + r.z * w));
                uvs.Add(new Vector2(0f, along * 0.1f));
                uvs.Add(new Vector2(1f, along * 0.1f));
                if (i > 0)
                {
                    int b = (i - 1) * 2;
                    tris.Add(b); tris.Add(b + 2); tris.Add(b + 1);
                    tris.Add(b + 1); tris.Add(b + 2); tris.Add(b + 3);
                }
            }
            var mesh = new Mesh { name = "SideRoad_Mesh" };
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>Guardrail openings (chunk-local z start / end) for a side of the main road.</summary>
        public static void GuardrailOpenings(Chunk chunk, float side, List<Vector2> into)
        {
            if (chunk == null) return;
            foreach (var road in chunk.sideRoads)
            {
                if (road.side != side) continue;
                float half = road.halfWidth + 6f;
                // A track leaving at an angle is wider along the main road
                if (road.points.Count > 2)
                {
                    Vector3 d = road.points[2] - road.points[0];
                    d.y = 0f;
                    float sin = Mathf.Abs(Vector3.Dot(d.normalized, Vector3.right));
                    if (sin > 0.2f) half /= sin;
                }
                into.Add(new Vector2(road.junctionZ - half, road.junctionZ + half));
            }
        }
    }
}
