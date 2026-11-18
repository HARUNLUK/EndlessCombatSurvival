using System.Collections.Generic;
using UnityEngine;

namespace EndlessCombat.Pooling
{
    /// <summary>
    /// Bullet tracer lines. Before: every pellet made a new GameObject, a LineRenderer and a new Material
    /// (Shader.Find each time) and destroyed them 0.04 s later; a shotgun shot made eight. Now a fixed set of line
    /// renderers with one shared material is reused.
    /// </summary>
    public class BulletTracers : MonoBehaviour
    {
        private const int Capacity = 64;
        private static BulletTracers _instance;

        private struct Line
        {
            public LineRenderer renderer;
            public float hideAt;
        }

        private readonly List<Line> _lines = new List<Line>();
        private Material _material;
        private int _next;

        /// <summary>Lines shown so far (tuning panel performance page).</summary>
        public static int Shown { get; private set; }

        public static void Show(Vector3 start, Vector3 end, float seconds, Color color)
        {
            if (_instance == null)
            {
                var go = new GameObject("BulletTracers");
                go.hideFlags = HideFlags.HideInHierarchy;
                _instance = go.AddComponent<BulletTracers>();
            }
            _instance.ShowLine(start, end, seconds, color);
        }

        private void ShowLine(Vector3 start, Vector3 end, float seconds, Color color)
        {
            if (_material == null)
            {
                Shader unlit = Shader.Find("Universal Render Pipeline/Unlit");
                if (unlit == null) unlit = Shader.Find("Sprites/Default");
                // URP Unlit ignores vertex colours, so the tracer colour lives on the shared material
                _material = new Material(unlit) { color = color };
            }

            Line line;
            if (_lines.Count < Capacity)
            {
                var go = new GameObject("Tracer");
                go.transform.SetParent(transform, false);
                var lr = go.AddComponent<LineRenderer>();
                lr.positionCount = 2;
                lr.startWidth = 0.04f;
                lr.endWidth = 0.02f;
                lr.useWorldSpace = true;
                lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                lr.receiveShadows = false;
                lr.sharedMaterial = _material;
                line = new Line { renderer = lr };
                _lines.Add(line);
                _next = _lines.Count - 1;
            }
            else
            {
                _next = (_next + 1) % _lines.Count; // the oldest line is reused
                line = _lines[_next];
            }

            var r = line.renderer;
            r.SetPosition(0, start);
            r.SetPosition(1, end);
            r.startColor = color;
            r.endColor = color;
            r.enabled = true;
            line.hideAt = Time.time + seconds;
            _lines[_next] = line;
            Shown++;
        }

        private void Update()
        {
            float now = Time.time;
            for (int i = 0; i < _lines.Count; i++)
            {
                var l = _lines[i];
                if (l.renderer != null && l.renderer.enabled && now >= l.hideAt) l.renderer.enabled = false;
            }
        }

        private void OnDestroy()
        {
            if (_material != null) Destroy(_material);
            if (_instance == this) _instance = null;
        }
    }
}
