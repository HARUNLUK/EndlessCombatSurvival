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

        [Header("Baseline Elevation")]
        [Tooltip("Baseline road elevation in meters above sea level (Y=0 is sea level, Y=20 is road level)")]
        public float baseElevation = 20f;

        private void Reset()
        {
            SetStraightPreset();
        }

        [ContextMenu("Curve Preset: Straight (Düz Yol)")]
        public void SetStraightPreset()
        {
            waypoints = new List<Vector3>
            {
                new Vector3(0f, baseElevation, 0f),
                new Vector3(0f, baseElevation, 60f),
                new Vector3(0f, baseElevation, 250f),
                new Vector3(0f, baseElevation, 440f),
                new Vector3(0f, baseElevation, 500f)
            };
        }

        [ContextMenu("Curve Preset: S-Curve Right (Sağ Viraj)")]
        public void SetSCurveRightPreset() => SetSCurvePreset(45f);

        [ContextMenu("Curve Preset: S-Curve Left (Sol Viraj)")]
        public void SetSCurveLeftPreset() => SetSCurvePreset(-45f);

        public void SetSCurvePreset(float lateralShift = 45f)
        {
            waypoints = new List<Vector3>
            {
                new Vector3(0f, baseElevation, 0f),
                new Vector3(0f, baseElevation, 60f),
                new Vector3(lateralShift * 0.7f, baseElevation, 130f),
                new Vector3(lateralShift, baseElevation, 200f),
                new Vector3(0f, baseElevation, 250f),
                new Vector3(-lateralShift, baseElevation, 300f),
                new Vector3(-lateralShift * 0.7f, baseElevation, 370f),
                new Vector3(0f, baseElevation, 440f),
                new Vector3(0f, baseElevation, 500f)
            };
        }

        [ContextMenu("Curve Preset: Elevation Hill (Tümsek / Eğim)")]
        public void SetElevationHillPreset() => SetElevationHillPreset(14f);

        public void SetElevationHillPreset(float hillHeight = 14f)
        {
            waypoints = new List<Vector3>
            {
                new Vector3(0f, baseElevation, 0f),
                new Vector3(0f, baseElevation, 50f),
                new Vector3(0f, baseElevation + hillHeight * 0.40f, 130f),
                new Vector3(0f, baseElevation + hillHeight * 0.85f, 190f),
                new Vector3(0f, baseElevation + hillHeight, 250f),
                new Vector3(0f, baseElevation + hillHeight * 0.85f, 310f),
                new Vector3(0f, baseElevation + hillHeight * 0.40f, 370f),
                new Vector3(0f, baseElevation, 450f),
                new Vector3(0f, baseElevation, 500f)
            };
        }

        [ContextMenu("Curve Preset: Valley Dip (Vadi İnişi)")]
        public void SetDipValleyPreset() => SetDipValleyPreset(14f);

        public void SetDipValleyPreset(float dipDepth = 14f)
        {
            waypoints = new List<Vector3>
            {
                new Vector3(0f, baseElevation, 0f),
                new Vector3(0f, baseElevation, 50f),
                new Vector3(0f, baseElevation - dipDepth * 0.40f, 130f),
                new Vector3(0f, baseElevation - dipDepth * 0.85f, 190f),
                new Vector3(0f, baseElevation - dipDepth, 250f),
                new Vector3(0f, baseElevation - dipDepth * 0.85f, 310f),
                new Vector3(0f, baseElevation - dipDepth * 0.40f, 370f),
                new Vector3(0f, baseElevation, 450f),
                new Vector3(0f, baseElevation, 500f)
            };
        }

        [ContextMenu("Curve Preset: Rolling Hills (Dalgalı Tepeler)")]
        public void SetRollingHillsPreset() => SetRollingHillsPreset(14f, 10f);

        public void SetRollingHillsPreset(float hillHeight = 14f, float dipDepth = 10f)
        {
            waypoints = new List<Vector3>
            {
                new Vector3(0f, baseElevation, 0f),
                new Vector3(0f, baseElevation, 50f),
                new Vector3(0f, baseElevation + hillHeight * 0.60f, 110f),
                new Vector3(0f, baseElevation + hillHeight, 160f),
                new Vector3(0f, baseElevation + hillHeight * 0.50f, 210f),
                new Vector3(0f, baseElevation, 250f),
                new Vector3(0f, baseElevation - dipDepth * 0.50f, 300f),
                new Vector3(0f, baseElevation - dipDepth, 350f),
                new Vector3(0f, baseElevation - dipDepth * 0.60f, 400f),
                new Vector3(0f, baseElevation, 450f),
                new Vector3(0f, baseElevation, 500f)
            };
        }

        [ContextMenu("Curve Preset: Mountain Pass (Dağ Geçidi Viraj & Zirve)")]
        public void SetMountainPassPreset() => SetMountainPassPreset(45f, 16f);

        public void SetMountainPassPreset(float lateralShift = 45f, float hillHeight = 16f)
        {
            waypoints = new List<Vector3>
            {
                new Vector3(0f, baseElevation, 0f),
                new Vector3(0f, baseElevation, 60f),
                new Vector3(lateralShift * 0.7f, baseElevation + hillHeight * 0.5f, 130f),
                new Vector3(lateralShift, baseElevation + hillHeight, 200f),
                new Vector3(0f, baseElevation + hillHeight * 0.85f, 250f),
                new Vector3(-lateralShift, baseElevation + hillHeight * 0.6f, 320f),
                new Vector3(-lateralShift * 0.5f, baseElevation + hillHeight * 0.25f, 390f),
                new Vector3(0f, baseElevation, 440f),
                new Vector3(0f, baseElevation, 500f)
            };
        }

        [ContextMenu("Curve Preset: Chicane (Şikan / Tehlikeli Hat)")]
        public void SetChicanePreset() => SetChicanePreset(35f);

        public void SetChicanePreset(float shift = 35f)
        {
            waypoints = new List<Vector3>
            {
                new Vector3(0f, baseElevation, 0f),
                new Vector3(0f, baseElevation, 60f),
                new Vector3(shift, baseElevation, 150f),
                new Vector3(0f, baseElevation, 200f),
                new Vector3(-shift, baseElevation, 260f),
                new Vector3(0f, baseElevation, 320f),
                new Vector3(shift * 0.6f, baseElevation, 380f),
                new Vector3(0f, baseElevation, 440f),
                new Vector3(0f, baseElevation, 500f)
            };
        }

        [ContextMenu("Curve Preset: Elevated Chicane (Tepeli Şikan)")]
        public void SetElevatedChicanePreset() => SetElevatedChicanePreset(35f, 10f);

        public void SetElevatedChicanePreset(float shift = 35f, float hillHeight = 10f)
        {
            waypoints = new List<Vector3>
            {
                new Vector3(0f, baseElevation, 0f),
                new Vector3(0f, baseElevation, 60f),
                new Vector3(shift, baseElevation + hillHeight * 0.5f, 150f),
                new Vector3(0f, baseElevation + hillHeight, 200f),
                new Vector3(-shift, baseElevation + hillHeight * 0.75f, 260f),
                new Vector3(0f, baseElevation + hillHeight * 0.35f, 320f),
                new Vector3(shift * 0.6f, baseElevation, 380f),
                new Vector3(0f, baseElevation, 440f),
                new Vector3(0f, baseElevation, 500f)
            };
        }

        public Vector3 GetPoint(float t)
        {
            if (waypoints == null || waypoints.Count == 0)
                return Vector3.zero;
            if (waypoints.Count == 1)
                return waypoints[0];

            t = Mathf.Clamp01(t);
            float minZ = waypoints[0].z;
            float maxZ = waypoints[waypoints.Count - 1].z;
            float targetZ = Mathf.Lerp(minZ, maxZ, t);
            return GetPointAtZ(targetZ);
        }

        public Vector3 GetPointAtZ(float targetZ)
        {
            if (waypoints == null || waypoints.Count == 0)
                return Vector3.zero;
            if (waypoints.Count == 1)
                return waypoints[0];

            float minZ = waypoints[0].z;
            float maxZ = waypoints[waypoints.Count - 1].z;
            if (maxZ <= minZ) return waypoints[0];

            targetZ = Mathf.Clamp(targetZ, minZ, maxZ);

            if (targetZ <= minZ + 0.0001f) return waypoints[0];
            if (targetZ >= maxZ - 0.0001f) return waypoints[waypoints.Count - 1];

            // Find segment [currPt, currPt + 1] containing targetZ
            int currPt = 0;
            for (int i = 0; i < waypoints.Count - 1; i++)
            {
                if (targetZ <= waypoints[i + 1].z)
                {
                    currPt = i;
                    break;
                }
            }

            Vector3 p0 = currPt > 0 ? waypoints[currPt - 1] : waypoints[currPt] + (waypoints[currPt] - waypoints[currPt + 1]);
            Vector3 p1 = waypoints[currPt];
            Vector3 p2 = waypoints[currPt + 1];
            Vector3 p3 = (currPt + 2 < waypoints.Count) ? waypoints[currPt + 2] : waypoints[currPt + 1] + (waypoints[currPt + 1] - waypoints[currPt]);

            // Binary search u in [0, 1] such that CatmullRom1D(p0.z, p1.z, p2.z, p3.z, u) == targetZ
            float low = 0f;
            float high = 1f;
            float mid = 0.5f;

            for (int iter = 0; iter < 12; iter++)
            {
                mid = (low + high) * 0.5f;
                float zMid = CatmullRom1D(p0.z, p1.z, p2.z, p3.z, mid);
                if (zMid < targetZ)
                {
                    low = mid;
                }
                else
                {
                    high = mid;
                }
            }

            return CatmullRom(p0, p1, p2, p3, mid);
        }

        private static float CatmullRom1D(float p0, float p1, float p2, float p3, float t)
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
