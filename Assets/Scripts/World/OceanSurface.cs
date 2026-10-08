using UnityEngine;

namespace EndlessSurvival.World
{
    /// <summary>
    /// Endless sea: one large water plane at sea level that follows the camera on XZ.
    /// Land (terrain, backdrop mountains) covers it everywhere except on coasts, so the
    /// sea reaches the horizon without per-chunk water meshes.
    /// </summary>
    public class OceanSurface : MonoBehaviour
    {
        public const string PREVIEW_NAME = "Preview_Sea";

        [Tooltip("Su düzleminin kenar uzunluğu (kamera far clip'inin iki katı kadar olmalı)")]
        public float size = 7000f;
        [Tooltip("Takip adımı; küçük titreşimleri engeller")]
        public float followSnap = 50f;
        [Tooltip("Boşsa renkten URP Lit su materyali üretilir. WaterWorks'ün SSR_Water materyali de atanabilir (URP ayarları gerekir).")]
        public Material material;
        public Color waterColor = new Color(0.06f, 0.25f, 0.34f);

        private Transform _target;
        private static Material s_defaultMaterial;

        public static OceanSurface Create(Transform parent, float worldY, Material material)
        {
            var go = new GameObject("Ocean");
            go.transform.SetParent(parent, false);
            go.transform.position = new Vector3(0f, worldY, 0f);

            var ocean = go.AddComponent<OceanSurface>();
            ocean.material = material;
            ocean.Build();
            return ocean;
        }

        private void Build()
        {
            float h = size * 0.5f;
            var mesh = new Mesh { name = "Ocean_Plane" };
            mesh.vertices = new[]
            {
                new Vector3(-h, 0f, -h), new Vector3(-h, 0f, h),
                new Vector3(h, 0f, h), new Vector3(h, 0f, -h)
            };
            mesh.uv = new[] { Vector2.zero, Vector2.up, Vector2.one, Vector2.right };
            mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = gameObject.AddComponent<MeshRenderer>();
            mr.sharedMaterial = material != null ? material : GetDefaultMaterial(waterColor);
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        private void LateUpdate()
        {
            if (_target == null || !_target.gameObject.activeInHierarchy)
            {
                Camera cam = Camera.main;
                _target = cam != null ? cam.transform : null;
                if (_target == null) return;
            }

            Vector3 p = _target.position;
            float snap = Mathf.Max(1f, followSnap);
            transform.position = new Vector3(Mathf.Round(p.x / snap) * snap, transform.position.y, Mathf.Round(p.z / snap) * snap);
        }

        private void OnDestroy()
        {
            var mf = GetComponent<MeshFilter>();
            if (mf != null && mf.sharedMesh != null) Destroy(mf.sharedMesh);
        }

        /// <summary>
        /// Edit-mode stand-in for the endless sea: a water quad covering a coast chunk and far out to sea.
        /// </summary>
        public static GameObject CreatePreviewQuad(Transform parent, Chunk chunk, float seaReach = 3000f)
        {
            float sign = chunk.SeaSign;
            float landX = -sign * 250f;
            float seaX = sign * seaReach;
            float y = WorldConstants.SeaLevel;
            float len = chunk.chunkLength;

            var mesh = new Mesh { name = "Preview_Sea_Mesh" };
            mesh.vertices = new[]
            {
                new Vector3(Mathf.Min(landX, seaX), y, 0f), new Vector3(Mathf.Min(landX, seaX), y, len),
                new Vector3(Mathf.Max(landX, seaX), y, len), new Vector3(Mathf.Max(landX, seaX), y, 0f)
            };
            mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            var go = new GameObject(PREVIEW_NAME);
            go.transform.SetPositionAndRotation(chunk.transform.position, chunk.transform.rotation);
            go.transform.SetParent(parent, true);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = GetDefaultMaterial(new Color(0.06f, 0.25f, 0.34f));
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go;
        }

        private static Material GetDefaultMaterial(Color color)
        {
            if (s_defaultMaterial != null) return s_defaultMaterial;

            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            s_defaultMaterial = new Material(shader) { name = "Mat_Ocean_Runtime", color = color };
            if (s_defaultMaterial.HasProperty("_BaseColor")) s_defaultMaterial.SetColor("_BaseColor", color);
            if (s_defaultMaterial.HasProperty("_Smoothness")) s_defaultMaterial.SetFloat("_Smoothness", 0.9f);
            if (s_defaultMaterial.HasProperty("_Metallic")) s_defaultMaterial.SetFloat("_Metallic", 0.05f);
            return s_defaultMaterial;
        }
    }
}
