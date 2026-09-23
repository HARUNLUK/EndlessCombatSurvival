using System.Collections.Generic;
using UnityEngine;

namespace EndlessSurvival.World.Road
{
    /// <summary>
    /// Self-contained Catmull-Rom spline representation for dynamic road pathing.
    /// Provides smooth interpolation, tangents, and presets within a 500m chunk.
    /// </summary>
    public class RoadSpline : MonoBehaviour
    {
        [Header("Spline Waypoints")]
        [Tooltip("Local waypoints defining the path from EntrySocket (Z=0) to ExitSocket (Z=500)")]
        public List<Vector3> waypoints = new List<Vector3>();

        [Header("Sampling Resolution")]
        [Tooltip("Number of segments sampled along the spline when generating mesh")]
        [Range(20, 200)]
        public int resolution = 100;

        private void Reset()
        {
            SetStraightPreset();
        }

        public void SetStraightPreset()
        {
            waypoints = new List<Vector3>
            {
                new Vector3(0f, 0f, 0f),
                new Vector3(0f, 0f, 60f),
                new Vector3(0f, 0f, 250f),
                new Vector3(0f, 0f, 440f),
                new Vector3(0f, 0f, 500f)
            };
        }

        public void SetSCurvePreset(float lateralShift = 45f)
        {
            waypoints = new List<Vector3>
            {
                new Vector3(0f, 0f, 0f),
                new Vector3(0f, 0f, 60f),
                new Vector3(lateralShift * 0.7f, 0f, 130f),
                new Vector3(lateralShift, 0f, 200f),
                new Vector3(0f, 0f, 250f),
                new Vector3(-lateralShift, 0f, 300f),
                new Vector3(-lateralShift * 0.7f, 0f, 370f),
                new Vector3(0f, 0f, 440f),
                new Vector3(0f, 0f, 500f)
            };
        }

        public void SetElevationHillPreset(float hillHeight = 12f)
        {
            waypoints = new List<Vector3>
            {
                new Vector3(0f, 0f, 0f),
                new Vector3(0f, 0f, 60f),
                new Vector3(0f, hillHeight, 180f),
                new Vector3(0f, hillHeight, 320f),
                new Vector3(0f, 0f, 440f),
                new Vector3(0f, 0f, 500f)
            };
        }

        public void SetChicanePreset(float shift = 35f)
        {
            waypoints = new List<Vector3>
            {
                new Vector3(0f, 0f, 0f),
                new Vector3(0f, 0f, 60f),
                new Vector3(shift, 0f, 150f),
                new Vector3(0f, 0f, 200f),
                new Vector3(-shift, 0f, 260f),
                new Vector3(0f, 0f, 320f),
                new Vector3(shift * 0.6f, 0f, 380f),
                new Vector3(0f, 0f, 440f),
                new Vector3(0f, 0f, 500f)
            };
        }

        public Vector3 GetPoint(float t)
        {
            if (waypoints == null || waypoints.Count < 2)
                return Vector3.zero;

            t = Mathf.Clamp01(t);
            if (t <= 0.0001f) return waypoints[0];
            if (t >= 0.9999f) return waypoints[waypoints.Count - 1];

            int numSections = waypoints.Count - 1;
            int currPt = Mathf.Min(Mathf.FloorToInt(t * numSections), numSections - 1);
            float u = (t * numSections) - currPt;

            Vector3 p0 = currPt > 0 ? waypoints[currPt - 1] : waypoints[currPt] + (waypoints[currPt] - waypoints[currPt + 1]);
            Vector3 p1 = waypoints[currPt];
            Vector3 p2 = waypoints[currPt + 1];
            Vector3 p3 = (currPt + 2 < waypoints.Count) ? waypoints[currPt + 2] : waypoints[currPt + 1] + (waypoints[currPt + 1] - waypoints[currPt]);

            return CatmullRom(p0, p1, p2, p3, u);
        }

        public Vector3 GetTangent(float t)
        {
            if (t <= 0.001f || t >= 0.999f)
            {
                return Vector3.forward;
            }

            float delta = 0.002f;
            float t1 = Mathf.Max(0f, t - delta);
            float t2 = Mathf.Min(1f, t + delta);

            Vector3 p1 = GetPoint(t1);
            Vector3 p2 = GetPoint(t2);
            Vector3 tangent = (p2 - p1).normalized;

            return tangent != Vector3.zero ? tangent : Vector3.forward;
        }

        public Vector3 GetRight(float t)
        {
            Vector3 tangent = GetTangent(t);
            return Vector3.Cross(Vector3.up, tangent).normalized;
        }

        public Vector3 GetNormal(float t)
        {
            Vector3 tangent = GetTangent(t);
            Vector3 right = GetRight(t);
            return Vector3.Cross(tangent, right).normalized;
        }

        private static Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
        {
            float t2 = t * t;
            float t3 = t2 * t;

            return 0.5f * (
                (2.0f * p1) +
                (-p0 + p2) * t +
                (2.0f * p0 - 5.0f * p1 + 4.0f * p2 - p3) * t2 +
                (-p0 + 3.0f * p1 - 3.0f * p2 + p3) * t3
            );
        }

        public float ApproximateLength(int samples = 50)
        {
            float length = 0f;
            Vector3 prev = GetPoint(0f);
            for (int i = 1; i <= samples; i++)
            {
                float t = i / (float)samples;
                Vector3 current = GetPoint(t);
                length += Vector3.Distance(prev, current);
                prev = current;
            }
            return length;
        }

        private void OnDrawGizmos()
        {
            if (waypoints == null || waypoints.Count < 2) return;

            // Draw spline path
            Gizmos.color = new Color(0.2f, 0.9f, 0.2f, 0.9f);
            Vector3 prev = transform.TransformPoint(GetPoint(0f));
            int steps = 100;
            for (int i = 1; i <= steps; i++)
            {
                float t = i / (float)steps;
                Vector3 curr = transform.TransformPoint(GetPoint(t));
                Gizmos.DrawLine(prev, curr);
                prev = curr;
            }

            // Draw waypoint spheres
            Gizmos.color = Color.yellow;
            for (int i = 0; i < waypoints.Count; i++)
            {
                Vector3 worldPt = transform.TransformPoint(waypoints[i]);
                Gizmos.DrawSphere(worldPt, 1.2f);
            }
        }
    }
}
