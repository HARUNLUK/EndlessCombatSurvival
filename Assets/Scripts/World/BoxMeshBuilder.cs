using System.Collections.Generic;
using UnityEngine;

namespace EndlessSurvival.World
{
    /// <summary>
    /// Collects many boxes into one mesh with several submeshes (one draw call per material).
    /// </summary>
    internal sealed class BoxMeshBuilder
    {
        private readonly List<Vector3> _vertices = new List<Vector3>();
        private readonly List<Vector3> _normals = new List<Vector3>();
        private readonly List<int>[] _submeshes;

        private static readonly Vector3[] FaceNormals = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
        private static readonly Vector3[] FaceU = { Vector3.forward, Vector3.forward, Vector3.right, Vector3.right, Vector3.up, Vector3.up };

        public BoxMeshBuilder(int submeshCount)
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
