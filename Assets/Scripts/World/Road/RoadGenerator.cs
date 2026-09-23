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
            if (!Application.isPlaying)
            {
                if (_meshFilter != null && _meshFilter.sharedMesh == null)
                {
                    ApplyBiomeProfile();
                    BuildRoadMesh();
                }
            }
        }

        private void Start()
        {
            if (Application.isPlaying && generateOnStart)
            {
                if (_meshFilter.sharedMesh == null)
                {
                    ApplyBiomeProfile();
                    BuildRoadMesh();
                }
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

        [ContextMenu("Build Road Mesh")]
        public Mesh BuildRoadMesh()
        {
            EnsureComponents();

            if (activeProfile == null)
            {
                ApplyBiomeProfile();
            }

            float roadWidth = activeProfile != null ? activeProfile.roadWidth : 12f;
            float shoulderWidth = activeProfile != null ? activeProfile.shoulderWidth : 2.5f;
            float curbDrop = activeProfile != null ? activeProfile.curbDrop : 0.08f;
            float tileRate = activeProfile != null ? activeProfile.textureTileRate : 0.2f;

            int steps = Mathf.Max(10, spline.resolution);
            // 8 vertices per step:
            // 0: Outer Left Shoulder, 1: Inner Left Shoulder, 2: Left Lane, 3: Right Lane,
            // 4: Inner Right Shoulder, 5: Outer Right Shoulder, 6: Left Skirt Bottom, 7: Right Skirt Bottom
            int numVerticesPerStep = 8;
            int totalVertices = (steps + 1) * numVerticesPerStep;

            Vector3[] vertices = new Vector3[totalVertices];
            Vector3[] normals = new Vector3[totalVertices];
            Vector2[] uvs = new Vector2[totalVertices];

            List<int> roadTriangles = new List<int>();
            List<int> shoulderTriangles = new List<int>();

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

                float vCoord = totalLength * tileRate;
                int baseIndex = i * numVerticesPerStep;

                Vector3 elevationOffset = Vector3.up * roadElevation;
                Vector3 shoulderDropOffset = Vector3.up * (roadElevation - curbDrop);
                Vector3 skirtBottomOffset = Vector3.up * (roadElevation - curbDrop - roadThickness);

                // 0: Outer Left Shoulder Top
                vertices[baseIndex + 0] = center - right * (halfRoad + shoulderWidth) + shoulderDropOffset;
                normals[baseIndex + 0] = normal;
                uvs[baseIndex + 0] = new Vector2(0f, vCoord);

                // 1: Inner Left Shoulder / Left Road Edge Top
                vertices[baseIndex + 1] = center - right * halfRoad + elevationOffset;
                normals[baseIndex + 1] = normal;
                uvs[baseIndex + 1] = new Vector2(0f, vCoord);

                // 2: Left Road Lane Center
                vertices[baseIndex + 2] = center - right * (halfRoad * 0.5f) + elevationOffset;
                normals[baseIndex + 2] = normal;
                uvs[baseIndex + 2] = new Vector2(0.25f, vCoord);

                // 3: Right Road Lane Center
                vertices[baseIndex + 3] = center + right * (halfRoad * 0.5f) + elevationOffset;
                normals[baseIndex + 3] = normal;
                uvs[baseIndex + 3] = new Vector2(0.75f, vCoord);

                // 4: Inner Right Shoulder / Right Road Edge Top
                vertices[baseIndex + 4] = center + right * halfRoad + elevationOffset;
                normals[baseIndex + 4] = normal;
                uvs[baseIndex + 4] = new Vector2(1f, vCoord);

                // 5: Outer Right Shoulder Top
                vertices[baseIndex + 5] = center + right * (halfRoad + shoulderWidth) + shoulderDropOffset;
                normals[baseIndex + 5] = normal;
                uvs[baseIndex + 5] = new Vector2(1f, vCoord);

                // 6: Left Skirt Bottom (solid thickness into ground)
                vertices[baseIndex + 6] = center - right * (halfRoad + shoulderWidth) + skirtBottomOffset;
                normals[baseIndex + 6] = -right;
                uvs[baseIndex + 6] = new Vector2(0f, vCoord);

                // 7: Right Skirt Bottom (solid thickness into ground)
                vertices[baseIndex + 7] = center + right * (halfRoad + shoulderWidth) + skirtBottomOffset;
                normals[baseIndex + 7] = right;
                uvs[baseIndex + 7] = new Vector2(1f, vCoord);

                // Ensure boundary vertices strictly align with Z=0 and Z=500 planes
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

                // Triangles between consecutive rings
                if (i < steps)
                {
                    int c = baseIndex;
                    int n = baseIndex + numVerticesPerStep;

                    // Left side skirt (facing left)
                    AddQuad(shoulderTriangles, c + 6, n + 6, n + 0, c + 0);

                    // Left Shoulder top surface quad: [0, 1] to [n0, n1]
                    AddQuad(shoulderTriangles, c + 0, n + 0, n + 1, c + 1);

                    // Road surface quads: [1, 2], [2, 3], [3, 4]
                    AddQuad(roadTriangles, c + 1, n + 1, n + 2, c + 2);
                    AddQuad(roadTriangles, c + 2, n + 2, n + 3, c + 3);
                    AddQuad(roadTriangles, c + 3, n + 3, n + 4, c + 4);

                    // Right Shoulder top surface quad: [4, 5] to [n4, n5]
                    AddQuad(shoulderTriangles, c + 4, n + 4, n + 5, c + 5);

                    // Right side skirt (facing right)
                    AddQuad(shoulderTriangles, c + 5, n + 5, n + 7, c + 7);
                }
            }

            Mesh mesh = new Mesh();
            mesh.name = "Generated_Road_Mesh";
            mesh.subMeshCount = 2;

            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.uv = uvs;

            mesh.SetTriangles(roadTriangles, 0);
            mesh.SetTriangles(shoulderTriangles, 1);
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();

            _meshFilter.sharedMesh = mesh;
            _meshCollider.sharedMesh = mesh;

            // Apply materials
            if (activeProfile != null)
            {
                Material[] mats = new Material[2];
                mats[0] = activeProfile.roadMaterial;
                mats[1] = activeProfile.shoulderMaterial != null ? activeProfile.shoulderMaterial : activeProfile.roadMaterial;
                _meshRenderer.sharedMaterials = mats;
            }

            if (syncExitSocket)
            {
                SyncSocketToEnd();
            }

            GenerateGuardrails();

            return mesh;
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

            // Per GameDev.md Section 3.2, exit socket must always be at local (0, 0, 500) with Quaternion.identity
            // so chunks tile seamlessly along the infinite linear progression without angular deviation or climbing.
            _parentChunk.exitSocket.localPosition = new Vector3(0f, 0f, 500f);
            _parentChunk.exitSocket.localRotation = Quaternion.identity;
        }

        private void GenerateGuardrails()
        {
            Transform existingHolder = transform.Find("Guardrails_Holder");
            if (existingHolder != null)
            {
                if (Application.isPlaying) Destroy(existingHolder.gameObject);
                else DestroyImmediate(existingHolder.gameObject);
            }

            if (activeProfile == null || !activeProfile.hasGuardrails || activeProfile.guardrailPrefab == null)
            {
                return;
            }

            GameObject holder = new GameObject("Guardrails_Holder");
            holder.transform.SetParent(transform, false);

            float totalLength = spline.ApproximateLength();
            float spacing = Mathf.Max(2f, activeProfile.guardrailSpacing);
            int count = Mathf.FloorToInt(totalLength / spacing);
            float offset = activeProfile.guardrailOffset;

            for (int i = 0; i <= count; i++)
            {
                float t = i / (float)count;
                Vector3 center = spline.GetPoint(t);
                Vector3 right = spline.GetRight(t);
                Vector3 forward = spline.GetTangent(t);
                Quaternion rot = Quaternion.LookRotation(forward, Vector3.up);

                Vector3 leftPos = center - right * offset + Vector3.up * roadElevation;
                Instantiate(activeProfile.guardrailPrefab, leftPos, rot, holder.transform);

                Vector3 rightPos = center + right * offset + Vector3.up * roadElevation;
                Instantiate(activeProfile.guardrailPrefab, rightPos, rot, holder.transform);
            }
        }
    }
}
