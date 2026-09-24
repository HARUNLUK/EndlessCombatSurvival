using System.Collections.Generic;
using UnityEngine;

namespace EndlessSurvival.World.Road
{
    /// <summary>
    /// Procedurally generates a solid 3D road mesh and collision surface along a RoadSpline.
    /// Features customizable elevation above terrain and 3D curb thickness side skirts.
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(RoadSpline))]
    [RequireComponent(typeof(MeshFilter))]
    [RequireComponent(typeof(MeshRenderer))]
    public class RoadGenerator : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("Spline defining the path of this road")]
        public RoadSpline spline;

        [Tooltip("Active profile used for road width and materials")]
        public RoadProfile activeProfile;

        [Tooltip("Biome profiles pool to automatically select the matching style")]
        public List<RoadProfile> biomeProfiles = new List<RoadProfile>();

        [Header("3D Thickness & Elevation")]
        [Tooltip("Height of road surface above base terrain to eliminate clipping and Z-fighting")]
        [Range(0.1f, 1.2f)]
        public float roadElevation = 0.45f;

        [Tooltip("Vertical thickness of the side curbs/skirts extending down into the terrain")]
        [Range(0.2f, 1.5f)]
        public float roadThickness = 0.80f;

        [Header("Cross-Section Preset & Style")]
        [Tooltip("Road cross-section style: Kaldırımlı, Bariyerli, Otoyol, Açık Kırsal, vb.")]
        public RoadCrossSectionPreset crossSectionPreset = RoadCrossSectionPreset.ProfileDefault;

        [Tooltip("Whether raised concrete curbs (bordür) are enabled")]
        public bool hasCurbs = true;

        [Tooltip("Whether pedestrian sidewalks / extended shoulders are enabled")]
        public bool hasSidewalks = true;

        [Tooltip("Whether roadside metal guardrails & posts are enabled")]
        public bool hasGuardrails = true;

        [Header("Generation Settings")]
        [Tooltip("Automatically generate mesh on Awake/Start")]
        public bool generateOnStart = true;

        [Tooltip("Automatically update parent Chunk exit socket to match spline endpoint")]
        public bool syncExitSocket = true;

        private MeshFilter _meshFilter;
        private MeshRenderer _meshRenderer;
        private MeshCollider _meshCollider;
        private Chunk _parentChunk;

        private void Awake()
        {
            EnsureComponents();
        }

        private void OnEnable()
        {
            EnsureComponents();
            bool missingGuardrails = (activeProfile == null || activeProfile.hasGuardrails) && transform.Find("Guardrails") == null;
            if (_meshFilter != null && (_meshFilter.sharedMesh == null || missingGuardrails))
            {
                ApplyBiomeProfile();
                BuildRoadMesh();
            }
        }

        private void Start()
        {
            EnsureComponents();
            if (generateOnStart || (_meshFilter != null && _meshFilter.sharedMesh == null))
            {
                ApplyBiomeProfile();
                BuildRoadMesh();
            }
        }

        private void EnsureComponents()
        {
            if (_meshFilter == null) _meshFilter = GetComponent<MeshFilter>();
            if (_meshRenderer == null) _meshRenderer = GetComponent<MeshRenderer>();
            if (_meshCollider == null)
            {
                _meshCollider = GetComponent<MeshCollider>();
                if (_meshCollider == null) _meshCollider = gameObject.AddComponent<MeshCollider>();
            }

            if (spline == null) spline = GetComponent<RoadSpline>();
            if (_parentChunk == null) _parentChunk = GetComponentInParent<Chunk>();
        }

        public void ApplyBiomeProfile()
        {
            if (_parentChunk == null) _parentChunk = GetComponentInParent<Chunk>();
            if (_parentChunk == null || biomeProfiles == null || biomeProfiles.Count == 0) return;

            foreach (var profile in biomeProfiles)
            {
                if (profile != null && profile.targetBiome == _parentChunk.biomeType)
                {
                    activeProfile = profile;
                    break;
                }
            }
        }

        public void ApplyCrossSectionPreset(RoadCrossSectionPreset preset)
        {
            crossSectionPreset = preset;
            switch (preset)
            {
                case RoadCrossSectionPreset.FullHighway:
                    hasCurbs = true;
                    hasSidewalks = true;
                    hasGuardrails = true;
                    break;
                case RoadCrossSectionPreset.SidewalkOnly:
                    hasCurbs = true;
                    hasSidewalks = true;
                    hasGuardrails = false;
                    break;
                case RoadCrossSectionPreset.GuardrailOnly:
                    hasCurbs = false;
                    hasSidewalks = false;
                    hasGuardrails = true;
                    break;
                case RoadCrossSectionPreset.OpenRoad:
                    hasCurbs = false;
                    hasSidewalks = false;
                    hasGuardrails = false;
                    break;
                case RoadCrossSectionPreset.HazardFortified:
                    hasCurbs = true;
                    hasSidewalks = true;
                    hasGuardrails = true;
                    break;
                case RoadCrossSectionPreset.ProfileDefault:
                default:
                    if (activeProfile != null)
                    {
                        hasCurbs = activeProfile.hasCurbs;
                        hasSidewalks = activeProfile.hasSidewalks;
                        hasGuardrails = activeProfile.hasGuardrails;
                    }
                    break;
            }

            BuildRoadMesh();
        }

        [ContextMenu("Preset: Full Highway (Kaldırım + Bariyer)")]
        public void SetPresetFullHighway() => ApplyCrossSectionPreset(RoadCrossSectionPreset.FullHighway);

        [ContextMenu("Preset: Sidewalk Only (Kaldırımlı Düz)")]
        public void SetPresetSidewalkOnly() => ApplyCrossSectionPreset(RoadCrossSectionPreset.SidewalkOnly);

        [ContextMenu("Preset: Guardrail Only (Bariyerli Düz)")]
        public void SetPresetGuardrailOnly() => ApplyCrossSectionPreset(RoadCrossSectionPreset.GuardrailOnly);

        [ContextMenu("Preset: Open Road (Sade Açık Kırsal)")]
        public void SetPresetOpenRoad() => ApplyCrossSectionPreset(RoadCrossSectionPreset.OpenRoad);

        [ContextMenu("Preset: Hazard Fortified (Güçlendirilmiş)")]
        public void SetPresetHazardFortified() => ApplyCrossSectionPreset(RoadCrossSectionPreset.HazardFortified);

        [ContextMenu("Rebuild Guardrails")]
        public void RebuildGuardrails()
        {
            EnsureComponents();
            if (activeProfile == null) ApplyBiomeProfile();
            GenerateGuardrails();
        }

        public float GetTotalHalfWidth()
        {
            float roadW = activeProfile != null ? activeProfile.roadWidth : 11f;
            float curbW = hasCurbs && activeProfile != null ? activeProfile.curbWidth : 0f;
            float sideW = hasSidewalks && activeProfile != null ? activeProfile.shoulderWidth : 0f;
            return (roadW * 0.5f) + curbW + sideW;
        }

        [ContextMenu("Build Road Mesh")]
        public Mesh BuildRoadMesh()
        {
            EnsureComponents();

            if (activeProfile == null)
            {
                ApplyBiomeProfile();
            }

            float baseRoadWidth = activeProfile != null ? activeProfile.roadWidth : 11.0f;
            float roadWidth = baseRoadWidth;
            float curbWidth = 0.30f;
            float curbHeight = 0.18f;
            float sidewalkWidth = 2.2f;
            bool enableGuardrails = hasGuardrails;

            switch (crossSectionPreset)
            {
                case RoadCrossSectionPreset.FullHighway:
                    curbWidth = activeProfile != null ? activeProfile.curbWidth : 0.30f;
                    curbHeight = activeProfile != null ? activeProfile.curbHeight : 0.18f;
                    sidewalkWidth = activeProfile != null ? activeProfile.shoulderWidth : 2.2f;
                    enableGuardrails = true;
                    break;
                case RoadCrossSectionPreset.SidewalkOnly:
                    curbWidth = activeProfile != null ? activeProfile.curbWidth : 0.30f;
                    curbHeight = activeProfile != null ? activeProfile.curbHeight : 0.18f;
                    sidewalkWidth = activeProfile != null ? activeProfile.shoulderWidth : 2.4f;
                    enableGuardrails = false;
                    break;
                case RoadCrossSectionPreset.GuardrailOnly:
                    curbWidth = 0.15f;
                    curbHeight = 0.05f;
                    sidewalkWidth = 0.85f;
                    enableGuardrails = true;
                    break;
                case RoadCrossSectionPreset.OpenRoad:
                    curbWidth = 0.05f;
                    curbHeight = 0.0f;
                    sidewalkWidth = 1.0f;
                    enableGuardrails = false;
                    break;
                case RoadCrossSectionPreset.HazardFortified:
                    roadWidth = Mathf.Min(baseRoadWidth, 9.5f);
                    curbWidth = 0.35f;
                    curbHeight = 0.22f;
                    sidewalkWidth = 1.2f;
                    enableGuardrails = true;
                    break;
                case RoadCrossSectionPreset.ProfileDefault:
                default:
                    curbWidth = hasCurbs ? (activeProfile != null ? activeProfile.curbWidth : 0.30f) : 0.05f;
                    curbHeight = hasCurbs ? (activeProfile != null ? activeProfile.curbHeight : 0.18f) : 0.0f;
                    sidewalkWidth = hasSidewalks ? (activeProfile != null ? activeProfile.shoulderWidth : 2.2f) : 0.85f;
                    enableGuardrails = hasGuardrails;
                    break;
            }

            float skirtDepth = 0.80f;
            float tileRate = activeProfile != null ? activeProfile.textureTileRate : 0.15f;

            int steps = Mathf.Max(10, spline.resolution);
            // 15 vertices per ring:
            // 0..4: Road surface (Left edge, Left lane, Center, Right lane, Right edge)
            // 5..7: Left Curb (Bottom at road level, Top inner, Top outer)
            // 8..10: Right Curb (Bottom at road level, Top inner, Top outer)
            // 11..12: Left Sidewalk (Top outer, Skirt bottom)
            // 13..14: Right Sidewalk (Top outer, Skirt bottom)
            int numVerticesPerStep = 15;
            int totalVertices = (steps + 1) * numVerticesPerStep;

            Vector3[] vertices = new Vector3[totalVertices];
            Vector3[] normals = new Vector3[totalVertices];
            Vector2[] uvs = new Vector2[totalVertices];

            List<int> roadTriangles = new List<int>();
            List<int> curbTriangles = new List<int>();
            List<int> sidewalkTriangles = new List<int>();

            float halfRoad = roadWidth * 0.5f;
            float totalLength = 0f;
            Vector3 prevCenter = spline.GetPoint(0f);

            for (int i = 0; i <= steps; i++)
            {
                float t = i / (float)steps;
                Vector3 center = spline.GetPoint(t);
                Vector3 right = spline.GetRight(t);
                Vector3 normal = spline.GetNormal(t);

                if (i == 0)
                {
                    center = new Vector3(0f, center.y, 0f);
                    right = Vector3.right;
                    normal = Vector3.up;
                }
                else if (i == steps)
                {
                    center = new Vector3(0f, center.y, 500f);
                    right = Vector3.right;
                    normal = Vector3.up;
                }

                if (i > 0)
                {
                    totalLength += Vector3.Distance(prevCenter, center);
                    prevCenter = center;
                }

                float vRoad = totalLength * tileRate;
                float vCurb = totalLength * 1.0f;
                float vSidewalk = totalLength * 0.4f;

                int baseIndex = i * numVerticesPerStep;

                Vector3 roadElevationOffset = Vector3.up * roadElevation;
                Vector3 curbElevationOffset = Vector3.up * (roadElevation + curbHeight);
                Vector3 skirtElevationOffset = Vector3.up * (roadElevation - skirtDepth);

                float leftRoadX = halfRoad;
                float leftCurbOuterX = halfRoad + curbWidth;
                float leftSidewalkOuterX = halfRoad + curbWidth + sidewalkWidth;

                float rightRoadX = halfRoad;
                float rightCurbOuterX = halfRoad + curbWidth;
                float rightSidewalkOuterX = halfRoad + curbWidth + sidewalkWidth;

                // --- 0..4: Road Surface (Asphalt + Markings) ---
                vertices[baseIndex + 0] = center - right * leftRoadX + roadElevationOffset;
                normals[baseIndex + 0] = normal;
                uvs[baseIndex + 0] = new Vector2(0.0f, vRoad);

                vertices[baseIndex + 1] = center - right * (leftRoadX * 0.5f) + roadElevationOffset;
                normals[baseIndex + 1] = normal;
                uvs[baseIndex + 1] = new Vector2(0.25f, vRoad);

                vertices[baseIndex + 2] = center + roadElevationOffset;
                normals[baseIndex + 2] = normal;
                uvs[baseIndex + 2] = new Vector2(0.50f, vRoad);

                vertices[baseIndex + 3] = center + right * (rightRoadX * 0.5f) + roadElevationOffset;
                normals[baseIndex + 3] = normal;
                uvs[baseIndex + 3] = new Vector2(0.75f, vRoad);

                vertices[baseIndex + 4] = center + right * rightRoadX + roadElevationOffset;
                normals[baseIndex + 4] = normal;
                uvs[baseIndex + 4] = new Vector2(1.0f, vRoad);

                // --- 5..7: Left Curb (Vertical face + Curb top) ---
                vertices[baseIndex + 5] = center - right * leftRoadX + roadElevationOffset;
                normals[baseIndex + 5] = right;
                uvs[baseIndex + 5] = new Vector2(0.0f, vCurb);

                vertices[baseIndex + 6] = center - right * leftRoadX + curbElevationOffset;
                normals[baseIndex + 6] = (right + Vector3.up).normalized;
                uvs[baseIndex + 6] = new Vector2(0.35f, vCurb);

                vertices[baseIndex + 7] = center - right * leftCurbOuterX + curbElevationOffset;
                normals[baseIndex + 7] = normal;
                uvs[baseIndex + 7] = new Vector2(1.0f, vCurb);

                // --- 8..10: Right Curb (Vertical face + Curb top) ---
                vertices[baseIndex + 8] = center + right * rightRoadX + roadElevationOffset;
                normals[baseIndex + 8] = -right;
                uvs[baseIndex + 8] = new Vector2(0.0f, vCurb);

                vertices[baseIndex + 9] = center + right * rightRoadX + curbElevationOffset;
                normals[baseIndex + 9] = (-right + Vector3.up).normalized;
                uvs[baseIndex + 9] = new Vector2(0.35f, vCurb);

                vertices[baseIndex + 10] = center + right * rightCurbOuterX + curbElevationOffset;
                normals[baseIndex + 10] = normal;
                uvs[baseIndex + 10] = new Vector2(1.0f, vCurb);

                // --- 11..12: Left Sidewalk & Skirt ---
                vertices[baseIndex + 11] = center - right * leftSidewalkOuterX + curbElevationOffset;
                normals[baseIndex + 11] = normal;
                uvs[baseIndex + 11] = new Vector2(1.0f, vSidewalk);

                vertices[baseIndex + 12] = center - right * leftSidewalkOuterX + skirtElevationOffset;
                normals[baseIndex + 12] = -right;
                uvs[baseIndex + 12] = new Vector2(0.0f, vSidewalk);

                // --- 13..14: Right Sidewalk & Skirt ---
                vertices[baseIndex + 13] = center + right * rightSidewalkOuterX + curbElevationOffset;
                normals[baseIndex + 13] = normal;
                uvs[baseIndex + 13] = new Vector2(1.0f, vSidewalk);

                vertices[baseIndex + 14] = center + right * rightSidewalkOuterX + skirtElevationOffset;
                normals[baseIndex + 14] = right;
                uvs[baseIndex + 14] = new Vector2(0.0f, vSidewalk);

                // Strictly clamp boundary vertices to Z=0 and Z=500
                if (i == 0)
                {
                    for (int v = 0; v < numVerticesPerStep; v++)
                    {
                        Vector3 pt = vertices[baseIndex + v];
                        pt.z = 0f;
                        vertices[baseIndex + v] = pt;
                    }
                }
                else if (i == steps)
                {
                    for (int v = 0; v < numVerticesPerStep; v++)
                    {
                        Vector3 pt = vertices[baseIndex + v];
                        pt.z = 500f;
                        vertices[baseIndex + v] = pt;
                    }
                }

                // Connect rings with quads
                if (i < steps)
                {
                    int c = baseIndex;
                    int n = baseIndex + numVerticesPerStep;

                    // 1. Road Surface (Submesh 0)
                    AddQuad(roadTriangles, c + 0, n + 0, n + 1, c + 1);
                    AddQuad(roadTriangles, c + 1, n + 1, n + 2, c + 2);
                    AddQuad(roadTriangles, c + 2, n + 2, n + 3, c + 3);
                    AddQuad(roadTriangles, c + 3, n + 3, n + 4, c + 4);

                    // 2. Curb Stones (Submesh 1)
                    // Left vertical curb face (facing road)
                    AddQuad(curbTriangles, c + 6, n + 6, n + 5, c + 5);
                    // Left curb top surface
                    AddQuad(curbTriangles, c + 7, n + 7, n + 6, c + 6);

                    // Right vertical curb face (facing road)
                    AddQuad(curbTriangles, c + 8, n + 8, n + 9, c + 9);
                    // Right curb top surface
                    AddQuad(curbTriangles, c + 9, n + 9, n + 10, c + 10);

                    // 3. Sidewalks & Skirts (Submesh 2)
                    // Left sidewalk top surface
                    AddQuad(sidewalkTriangles, c + 11, n + 11, n + 7, c + 7);
                    // Left outer skirt into ground
                    AddQuad(sidewalkTriangles, c + 12, n + 12, n + 11, c + 11);

                    // Right sidewalk top surface
                    AddQuad(sidewalkTriangles, c + 10, n + 10, n + 13, c + 13);
                    // Right outer skirt into ground
                    AddQuad(sidewalkTriangles, c + 13, n + 13, n + 14, c + 14);
                }
            }

            Mesh mesh = new Mesh();
            mesh.name = "Generated_Road_Mesh";
            mesh.subMeshCount = 3;

            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.uv = uvs;

            mesh.SetTriangles(roadTriangles, 0);
            mesh.SetTriangles(curbTriangles, 1);
            mesh.SetTriangles(sidewalkTriangles, 2);
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();

            _meshFilter.sharedMesh = mesh;
            _meshCollider.sharedMesh = mesh;

            // Apply 3 distinct materials: Road (Asphalt), Curb (Bordür), Sidewalk (Kaldırım)
            Material rMat = activeProfile != null && activeProfile.roadMaterial != null 
                ? activeProfile.roadMaterial 
                : CreateDefaultURPMaterial("Mat_Road_Dark", new Color(0.18f, 0.18f, 0.19f, 1f), 0.10f);
            Material cMat = activeProfile != null && activeProfile.curbMaterial != null 
                ? activeProfile.curbMaterial 
                : CreateDefaultURPMaterial("Mat_Road_Curb", new Color(0.68f, 0.67f, 0.65f, 1f), 0.20f);
            Material sMat = activeProfile != null && activeProfile.shoulderMaterial != null 
                ? activeProfile.shoulderMaterial 
                : CreateDefaultURPMaterial("Mat_Road_Sidewalk", new Color(0.55f, 0.54f, 0.52f, 1f), 0.20f);

            EnsureMaterialTexture(rMat, GetOrCreateAsphaltTexture());
            EnsureMaterialTexture(cMat, GetOrCreateCurbTexture());
            EnsureMaterialTexture(sMat, GetOrCreateSidewalkTexture());

            _meshRenderer.sharedMaterials = new Material[3] { rMat, cMat, sMat };

            if (syncExitSocket)
            {
                SyncSocketToEnd();
            }

            GenerateGuardrails(enableGuardrails, halfRoad + curbWidth + sidewalkWidth);

            return mesh;
        }

        private static Texture2D _cachedAsphaltTex;
        private static Texture2D _cachedCurbTex;
        private static Texture2D _cachedSidewalkTex;

        public static Texture2D GetOrCreateAsphaltTexture()
        {
            if (_cachedAsphaltTex != null) return _cachedAsphaltTex;

#if UNITY_EDITOR
            Texture2D diskTex = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Prefabs/Chunks/Textures/Tex_Road_Asphalt.png");
            if (diskTex != null)
            {
                _cachedAsphaltTex = diskTex;
                return _cachedAsphaltTex;
            }
#endif

            int size = 512;
            _cachedAsphaltTex = new Texture2D(size, size, TextureFormat.RGBA32, true);
            _cachedAsphaltTex.hideFlags = HideFlags.DontUnloadUnusedAsset;
            Color[] pixels = new Color[size * size];

            Color asphaltColor = new Color(0.17f, 0.175f, 0.18f);
            Color whiteLineColor = new Color(0.92f, 0.92f, 0.90f);
            Color dashedLineColor = new Color(0.92f, 0.90f, 0.85f);

            for (int y = 0; y < size; y++)
            {
                bool isDashActive = (y % 256) < 160;

                for (int x = 0; x < size; x++)
                {
                    float noise = Mathf.PerlinNoise(x * 0.12f, y * 0.12f) * 0.04f - 0.02f;
                    Color c = new Color(
                        Mathf.Clamp01(asphaltColor.r + noise),
                        Mathf.Clamp01(asphaltColor.g + noise),
                        Mathf.Clamp01(asphaltColor.b + noise),
                        1f
                    );

                    // Left solid white edge line
                    if (x >= 18 && x <= 34)
                    {
                        c = whiteLineColor;
                    }
                    // Right solid white edge line
                    else if (x >= 478 && x <= 494)
                    {
                        c = whiteLineColor;
                    }
                    // Center dashed white line
                    else if (x >= 248 && x <= 264 && isDashActive)
                    {
                        c = dashedLineColor;
                    }

                    pixels[y * size + x] = c;
                }
            }

            _cachedAsphaltTex.SetPixels(pixels);
            _cachedAsphaltTex.wrapMode = TextureWrapMode.Repeat;
            _cachedAsphaltTex.filterMode = FilterMode.Bilinear;
            _cachedAsphaltTex.Apply();
            return _cachedAsphaltTex;
        }

        public static Texture2D GetOrCreateCurbTexture()
        {
            if (_cachedCurbTex != null) return _cachedCurbTex;

#if UNITY_EDITOR
            Texture2D diskTex = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Prefabs/Chunks/Textures/Tex_Road_Curb.png");
            if (diskTex != null)
            {
                _cachedCurbTex = diskTex;
                return _cachedCurbTex;
            }
#endif

            int width = 128;
            int height = 256;
            _cachedCurbTex = new Texture2D(width, height, TextureFormat.RGBA32, true);
            _cachedCurbTex.hideFlags = HideFlags.DontUnloadUnusedAsset;
            Color[] pixels = new Color[width * height];

            Color curbColor = new Color(0.68f, 0.67f, 0.65f);
            Color jointColor = new Color(0.35f, 0.34f, 0.33f);
            Color bevelColor = new Color(0.78f, 0.77f, 0.75f);

            for (int y = 0; y < height; y++)
            {
                bool isJoint = (y % 64) < 3;

                for (int x = 0; x < width; x++)
                {
                    if (isJoint)
                    {
                        pixels[y * width + x] = jointColor;
                    }
                    else if (x < 8)
                    {
                        pixels[y * width + x] = bevelColor;
                    }
                    else
                    {
                        float noise = Mathf.PerlinNoise(x * 0.2f, y * 0.2f) * 0.05f - 0.025f;
                        pixels[y * width + x] = new Color(
                            Mathf.Clamp01(curbColor.r + noise),
                            Mathf.Clamp01(curbColor.g + noise),
                            Mathf.Clamp01(curbColor.b + noise),
                            1f
                        );
                    }
                }
            }

            _cachedCurbTex.SetPixels(pixels);
            _cachedCurbTex.wrapMode = TextureWrapMode.Repeat;
            _cachedCurbTex.filterMode = FilterMode.Bilinear;
            _cachedCurbTex.Apply();
            return _cachedCurbTex;
        }

        public static Texture2D GetOrCreateSidewalkTexture()
        {
            if (_cachedSidewalkTex != null) return _cachedSidewalkTex;

#if UNITY_EDITOR
            Texture2D diskTex = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Prefabs/Chunks/Textures/Tex_Road_Sidewalk.png");
            if (diskTex != null)
            {
                _cachedSidewalkTex = diskTex;
                return _cachedSidewalkTex;
            }
#endif

            int size = 256;
            _cachedSidewalkTex = new Texture2D(size, size, TextureFormat.RGBA32, true);
            _cachedSidewalkTex.hideFlags = HideFlags.DontUnloadUnusedAsset;
            Color[] pixels = new Color[size * size];

            Color baseTileColor = new Color(0.55f, 0.54f, 0.52f);
            Color mortarColor = new Color(0.30f, 0.29f, 0.28f);

            for (int y = 0; y < size; y++)
            {
                int ty = y / 64;
                bool isMortarY = (y % 64) < 3;

                for (int x = 0; x < size; x++)
                {
                    int tx = x / 64;
                    bool isMortarX = (x % 64) < 3;

                    if (isMortarX || isMortarY)
                    {
                        pixels[y * size + x] = mortarColor;
                    }
                    else
                    {
                        float perTileTint = ((tx * 7 + ty * 13) % 7 - 3) * 0.015f;
                        float noise = Mathf.PerlinNoise(x * 0.15f, y * 0.15f) * 0.04f - 0.02f;
                        pixels[y * size + x] = new Color(
                            Mathf.Clamp01(baseTileColor.r + perTileTint + noise),
                            Mathf.Clamp01(baseTileColor.g + perTileTint + noise),
                            Mathf.Clamp01(baseTileColor.b + perTileTint + noise),
                            1f
                        );
                    }
                }
            }

            _cachedSidewalkTex.SetPixels(pixels);
            _cachedSidewalkTex.wrapMode = TextureWrapMode.Repeat;
            _cachedSidewalkTex.filterMode = FilterMode.Bilinear;
            _cachedSidewalkTex.Apply();
            return _cachedSidewalkTex;
        }

        private static void EnsureMaterialTexture(Material mat, Texture2D tex)
        {
            if (mat == null || tex == null) return;
            if (mat.HasProperty("_BaseMap") && mat.GetTexture("_BaseMap") == null)
            {
                mat.SetTexture("_BaseMap", tex);
            }
            if (mat.HasProperty("_MainTex") && mat.GetTexture("_MainTex") == null)
            {
                mat.SetTexture("_MainTex", tex);
            }
            if (mat.mainTexture == null)
            {
                mat.mainTexture = tex;
            }
        }

        private static Material CreateDefaultURPMaterial(string name, Color color, float smoothness = 0.10f)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            Material mat = new Material(shader);
            mat.name = name;
            mat.color = color;
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", smoothness);
            return mat;
        }

        private static void AddQuad(List<int> triangles, int bl, int tl, int tr, int br)
        {
            triangles.Add(bl);
            triangles.Add(tl);
            triangles.Add(tr);

            triangles.Add(bl);
            triangles.Add(tr);
            triangles.Add(br);
        }

        public void SyncSocketToEnd()
        {
            if (_parentChunk == null) _parentChunk = GetComponentInParent<Chunk>();
            if (_parentChunk == null || _parentChunk.exitSocket == null) return;

            float baseY = spline != null ? spline.baseElevation : 20f;
            _parentChunk.exitSocket.localPosition = new Vector3(0f, baseY, 500f);
            _parentChunk.exitSocket.localRotation = Quaternion.identity;
            if (_parentChunk.entrySocket != null)
            {
                _parentChunk.entrySocket.localPosition = new Vector3(0f, baseY, 0f);
                _parentChunk.entrySocket.localRotation = Quaternion.identity;
            }
        }

        public void GenerateGuardrails()
        {
            float baseRoadWidth = activeProfile != null ? activeProfile.roadWidth : 11.0f;
            float roadWidth = (crossSectionPreset == RoadCrossSectionPreset.HazardFortified) ? Mathf.Min(baseRoadWidth, 9.5f) : baseRoadWidth;
            float curbW = hasCurbs ? (activeProfile != null ? activeProfile.curbWidth : 0.30f) : 0.05f;
            float sidewalkW = hasSidewalks ? (activeProfile != null ? activeProfile.shoulderWidth : 2.2f) : 0.85f;
            float computedOffset = (roadWidth * 0.5f) + curbW + sidewalkW;
            GenerateGuardrails(hasGuardrails, computedOffset);
        }

        public void GenerateGuardrails(bool enabled, float computedOffset)
        {
            Transform existingHolder = transform.Find("Guardrails");
            if (existingHolder != null)
            {
                if (Application.isPlaying) Destroy(existingHolder.gameObject);
                else DestroyImmediate(existingHolder.gameObject);
            }

            Transform oldHolder = transform.Find("Guardrails_Holder");
            if (oldHolder != null)
            {
                if (Application.isPlaying) Destroy(oldHolder.gameObject);
                else DestroyImmediate(oldHolder.gameObject);
            }

            if (!enabled) return;

            GameObject guardrailsGo = new GameObject("Guardrails");
            guardrailsGo.transform.SetParent(transform, false);

            MeshFilter filter = guardrailsGo.AddComponent<MeshFilter>();
            MeshRenderer renderer = guardrailsGo.AddComponent<MeshRenderer>();
            MeshCollider collider = guardrailsGo.AddComponent<MeshCollider>();

            Mesh mesh = BuildGuardrailMesh(computedOffset);
            filter.sharedMesh = mesh;
            collider.sharedMesh = mesh;

            Material gMat = activeProfile != null && activeProfile.guardrailMaterial != null
                ? activeProfile.guardrailMaterial
                : GetOrCreateFallbackGuardrailMaterial();

            renderer.sharedMaterial = gMat;
        }

        private Mesh BuildGuardrailMesh(float offset)
        {
            float curbH = hasCurbs ? (activeProfile != null ? activeProfile.curbHeight : 0.18f) : 0.0f;
            float sidewalkY = roadElevation + curbH;

            int steps = Mathf.Max(20, spline.resolution);
            float postSpacing = activeProfile != null ? Mathf.Max(2f, activeProfile.guardrailSpacing) : 3.5f;

            List<Vector3> verts = new List<Vector3>();
            List<Vector3> norms = new List<Vector3>();
            List<Vector2> uvs = new List<Vector2>();
            List<int> tris = new List<int>();

            float totalLength = 0f;
            Vector3 prevCenter = spline.GetPoint(0f);

            // 1. Build continuous extruded 3D solid Guardrails (Left and Right sides with 18cm thickness)
            float beamThickness = 0.18f;

            for (int i = 0; i <= steps; i++)
            {
                float t = i / (float)steps;
                Vector3 center = spline.GetPoint(t);
                Vector3 right = spline.GetRight(t);

                if (i == 0) { center = new Vector3(0f, center.y, 0f); right = Vector3.right; }
                else if (i == steps) { center = new Vector3(0f, center.y, 500f); right = Vector3.right; }

                if (i > 0)
                {
                    totalLength += Vector3.Distance(prevCenter, center);
                    prevCenter = center;
                }

                float vCoord = totalLength * 0.8f;
                Vector3 leftAnchor = center - right * offset;
                Vector3 leftBack = leftAnchor - right * beamThickness;

                Vector3 rightAnchor = center + right * offset;
                Vector3 rightBack = rightAnchor + right * beamThickness;

                int baseLeft = verts.Count;
                // --- Left Guardrail: 7 vertices per step ---
                // 0..4: Front W-beam face (facing road = +right)
                verts.Add(leftAnchor + Vector3.up * (sidewalkY + 0.74f));
                verts.Add(leftAnchor + right * 0.08f + Vector3.up * (sidewalkY + 0.60f));
                verts.Add(leftAnchor - right * 0.02f + Vector3.up * (sidewalkY + 0.46f));
                verts.Add(leftAnchor + right * 0.08f + Vector3.up * (sidewalkY + 0.32f));
                verts.Add(leftAnchor + Vector3.up * (sidewalkY + 0.18f));
                // 5..6: Back face (facing outside = -right)
                verts.Add(leftBack + Vector3.up * (sidewalkY + 0.74f));
                verts.Add(leftBack + Vector3.up * (sidewalkY + 0.18f));

                for (int v = 0; v < 5; v++) norms.Add(right);
                norms.Add(-right);
                norms.Add(-right);

                for (int v = 0; v < 5; v++) uvs.Add(new Vector2(v * 0.25f, vCoord));
                uvs.Add(new Vector2(0f, vCoord));
                uvs.Add(new Vector2(1f, vCoord));

                int baseRight = verts.Count;
                // --- Right Guardrail: 7 vertices per step ---
                // 0..4: Front W-beam face (facing road = -right)
                verts.Add(rightAnchor + Vector3.up * (sidewalkY + 0.74f));
                verts.Add(rightAnchor - right * 0.08f + Vector3.up * (sidewalkY + 0.60f));
                verts.Add(rightAnchor + right * 0.02f + Vector3.up * (sidewalkY + 0.46f));
                verts.Add(rightAnchor - right * 0.08f + Vector3.up * (sidewalkY + 0.32f));
                verts.Add(rightAnchor + Vector3.up * (sidewalkY + 0.18f));
                // 5..6: Back face (facing outside = +right)
                verts.Add(rightBack + Vector3.up * (sidewalkY + 0.74f));
                verts.Add(rightBack + Vector3.up * (sidewalkY + 0.18f));

                for (int v = 0; v < 5; v++) norms.Add(-right);
                norms.Add(right);
                norms.Add(right);

                for (int v = 0; v < 5; v++) uvs.Add(new Vector2(v * 0.25f, vCoord));
                uvs.Add(new Vector2(0f, vCoord));
                uvs.Add(new Vector2(1f, vCoord));

                if (i < steps)
                {
                    int nextLeft = baseLeft + 14;
                    int nextRight = baseRight + 14;

                    // === Left Guardrail Faces (Solid 3D Closed Box Volume) ===
                    // 1. Front W-beam face (Strictly facing road = +right)
                    for (int q = 0; q < 4; q++)
                    {
                        tris.Add(baseLeft + q + 1);
                        tris.Add(baseLeft + q);
                        tris.Add(nextLeft + q);

                        tris.Add(baseLeft + q + 1);
                        tris.Add(nextLeft + q);
                        tris.Add(nextLeft + q + 1);
                    }

                    // 2. Back face (Strictly facing outside = -right)
                    tris.Add(baseLeft + 6);
                    tris.Add(nextLeft + 5);
                    tris.Add(baseLeft + 5);

                    tris.Add(baseLeft + 6);
                    tris.Add(nextLeft + 6);
                    tris.Add(nextLeft + 5);

                    // 3. Top cap (Strictly facing +up)
                    tris.Add(baseLeft + 5);
                    tris.Add(nextLeft + 5);
                    tris.Add(nextLeft + 0);

                    tris.Add(baseLeft + 5);
                    tris.Add(nextLeft + 0);
                    tris.Add(baseLeft + 0);

                    // 4. Bottom cap (Strictly facing -up)
                    tris.Add(baseLeft + 4);
                    tris.Add(nextLeft + 4);
                    tris.Add(nextLeft + 6);

                    tris.Add(baseLeft + 4);
                    tris.Add(nextLeft + 6);
                    tris.Add(baseLeft + 6);

                    // === Right Guardrail Faces (Solid 3D Closed Box Volume) ===
                    // 1. Front W-beam face (Strictly facing road = -right)
                    for (int q = 0; q < 4; q++)
                    {
                        tris.Add(baseRight + q + 1);
                        tris.Add(nextRight + q);
                        tris.Add(baseRight + q);

                        tris.Add(baseRight + q + 1);
                        tris.Add(nextRight + q + 1);
                        tris.Add(nextRight + q);
                    }

                    // 2. Back face (Strictly facing outside = +right)
                    tris.Add(baseRight + 6);
                    tris.Add(baseRight + 5);
                    tris.Add(nextRight + 5);

                    tris.Add(baseRight + 6);
                    tris.Add(nextRight + 5);
                    tris.Add(nextRight + 6);

                    // 3. Top cap (Strictly facing +up)
                    tris.Add(baseRight + 0);
                    tris.Add(nextRight + 0);
                    tris.Add(nextRight + 5);

                    tris.Add(baseRight + 0);
                    tris.Add(nextRight + 5);
                    tris.Add(baseRight + 5);

                    // 4. Bottom cap (Strictly facing -up)
                    tris.Add(baseRight + 6);
                    tris.Add(nextRight + 6);
                    tris.Add(nextRight + 4);

                    tris.Add(baseRight + 6);
                    tris.Add(nextRight + 4);
                    tris.Add(baseRight + 4);
                }
            }

            // 2. Add vertical support posts along the guardrails
            int postCount = Mathf.FloorToInt(500f / postSpacing);
            float postHalfW = 0.06f;
            float postHeight = 0.95f;
            float postBaseY = sidewalkY - 0.20f;

            for (int p = 0; p <= postCount; p++)
            {
                float t = p / (float)postCount;
                Vector3 center = spline.GetPoint(t);
                Vector3 right = spline.GetRight(t);
                Vector3 forward = spline.GetTangent(t);

                if (p == 0) { center = new Vector3(0f, center.y, 0f); right = Vector3.right; forward = Vector3.forward; }
                else if (p == postCount) { center = new Vector3(0f, center.y, 500f); right = Vector3.right; forward = Vector3.forward; }

                Vector3 leftPostCenter = center - right * (offset + beamThickness + postHalfW) + Vector3.up * (postBaseY + postHeight * 0.5f);
                Vector3 rightPostCenter = center + right * (offset + beamThickness + postHalfW) + Vector3.up * (postBaseY + postHeight * 0.5f);
                Vector3 postSize = new Vector3(postHalfW * 2f, postHeight, postHalfW * 2f);

                AddPostBox(verts, norms, uvs, tris, leftPostCenter, right, forward, postSize);
                AddPostBox(verts, norms, uvs, tris, rightPostCenter, right, forward, postSize);
            }

            Mesh mesh = new Mesh();
            mesh.name = "Generated_Guardrails_Mesh";
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.SetUVs(0, uvs);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            return mesh;
        }

        private static void AddPostBox(List<Vector3> verts, List<Vector3> norms, List<Vector2> uvs, List<int> tris, Vector3 center, Vector3 right, Vector3 forward, Vector3 size)
        {
            Vector3 hX = right * (size.x * 0.5f);
            Vector3 hY = Vector3.up * (size.y * 0.5f);
            Vector3 hZ = forward * (size.z * 0.5f);

            AddFace(verts, norms, uvs, tris, center + hZ, right, Vector3.up, size.x, size.y, forward);
            AddFace(verts, norms, uvs, tris, center - hZ, -right, Vector3.up, size.x, size.y, -forward);
            AddFace(verts, norms, uvs, tris, center - hX, forward, Vector3.up, size.z, size.y, -right);
            AddFace(verts, norms, uvs, tris, center + hX, -forward, Vector3.up, size.z, size.y, right);
            AddFace(verts, norms, uvs, tris, center + hY, right, forward, size.x, size.z, Vector3.up);
        }

        private static void AddFace(List<Vector3> verts, List<Vector3> norms, List<Vector2> uvs, List<int> tris, Vector3 center, Vector3 rightAxis, Vector3 upAxis, float width, float height, Vector3 normal)
        {
            int baseIdx = verts.Count;
            Vector3 r = rightAxis * (width * 0.5f);
            Vector3 u = upAxis * (height * 0.5f);

            verts.Add(center - r - u);
            verts.Add(center - r + u);
            verts.Add(center + r + u);
            verts.Add(center + r - u);

            for (int k = 0; k < 4; k++) norms.Add(normal);

            uvs.Add(new Vector2(0f, 0f));
            uvs.Add(new Vector2(0f, 1f));
            uvs.Add(new Vector2(1f, 1f));
            uvs.Add(new Vector2(1f, 0f));

            AddQuad(tris, baseIdx + 0, baseIdx + 3, baseIdx + 2, baseIdx + 1);
        }

        private static Texture2D _cachedGuardrailTex;

        public static Texture2D GetOrCreateGuardrailTexture()
        {
            if (_cachedGuardrailTex != null) return _cachedGuardrailTex;

#if UNITY_EDITOR
            Texture2D diskTex = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Prefabs/Chunks/Textures/Tex_Road_Guardrail.png");
            if (diskTex != null)
            {
                _cachedGuardrailTex = diskTex;
                return _cachedGuardrailTex;
            }
#endif

            int size = 256;
            _cachedGuardrailTex = new Texture2D(size, size, TextureFormat.RGBA32, true);
            _cachedGuardrailTex.hideFlags = HideFlags.DontUnloadUnusedAsset;
            Color[] pixels = new Color[size * size];

            Color steelColor = new Color(0.82f, 0.83f, 0.85f);

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float noise = Mathf.PerlinNoise(x * 0.15f, y * 0.15f) * 0.05f - 0.025f;
                    pixels[y * size + x] = new Color(
                        Mathf.Clamp01(steelColor.r + noise),
                        Mathf.Clamp01(steelColor.g + noise),
                        Mathf.Clamp01(steelColor.b + noise),
                        1f
                    );
                }
            }

            _cachedGuardrailTex.SetPixels(pixels);
            _cachedGuardrailTex.wrapMode = TextureWrapMode.Repeat;
            _cachedGuardrailTex.filterMode = FilterMode.Bilinear;
            _cachedGuardrailTex.Apply();
            return _cachedGuardrailTex;
        }

        private static Material GetOrCreateFallbackGuardrailMaterial()
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            Material mat = new Material(shader);
            mat.name = "Mat_Guardrail_Metal";
            Color col = new Color(0.85f, 0.86f, 0.88f);
            mat.color = col;
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", col);
            if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", 0.85f);
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.55f);

            Texture2D tex = GetOrCreateGuardrailTexture();
            if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", tex);
            if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", tex);
            mat.mainTexture = tex;

            return mat;
        }
    }
}
