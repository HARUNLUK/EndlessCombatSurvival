using System.Collections.Generic;
using UnityEngine;
using EndlessSurvival.World;

namespace EndlessSurvival.World.Road
{
    /// <summary>Tuning for randomly generated road curves.</summary>
    [System.Serializable]
    public class RoadCurveSettings
    {
        [Tooltip("Chance a (non-runway) chunk has curves at all")]
        [Range(0f, 1f)] public float curveChance = 0.7f;
        [Tooltip("Min/max number of bends in a curvy chunk")]
        [Range(1, 8)] public int minBends = 2;
        [Range(1, 8)] public int maxBends = 5;
        [Tooltip("Lateral offset range of each bend apex in meters")]
        public float minShift = 15f;
        public float maxShift = 50f;
        [Tooltip("Absolute lateral limit from chunk center")]
        public float maxLateral = 70f;
        [Tooltip("Max sideways meters per forward meter (lower = gentler curves)")]
        [Range(0.2f, 1.2f)] public float maxSlope = 0.55f;
        [Tooltip("Tightest allowed turn radius in meters. Keeps road edges and guardrails from folding on the inside of bends")]
        [Range(15f, 120f)] public float minTurnRadius = 35f;
        [Tooltip("Bend length randomness: gaps vary between 1x and this multiple")]
        [Range(1f, 4f)] public float lengthVariance = 2.5f;
    }

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

        /// <summary>
        /// Builds a fully random road: bend count, bend positions along Z, bend lengths and lateral
        /// offsets are all rolled per call. Entry/exit sit at startX/endX (shared with the neighbors, 0 = center) and
        /// baseElevation, heading straight along Z, so chunks still join.
        /// Returns the signed lateral offset of the largest bend (0 when straight).
        /// </summary>
        /// <param name="startX">Lateral road position where it enters the chunk (0 = center). Shared with the previous chunk.</param>
        /// <param name="endX">Lateral road position where it leaves the chunk. Shared with the next chunk.</param>
        public float SetProceduralPreset(RoadCurveSettings s, bool curvy, bool sharp,
            RoadElevationType elevation, float hillHeight, float dipDepth, SeededRandom rng,
            float startX = 0f, float endX = 0f, float startY = float.NaN, float endY = float.NaN)
        {
            const float edge = 60f;
            const float length = 500f;
            // Bends never take the road further than this from the chunk center (boundaries/mountains need room)
            const float maxAbsX = 150f;
            float span = length - 2f * edge;

            // Base line from the entry to the exit position; bends are added on top of it
            float BaseX(float z) => Mathf.Lerp(startX, endX, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((z - edge) / span)));
            // Same for the height: the road climbs/descends from the entry to the exit height (mountains), level at both ends
            if (float.IsNaN(startY)) startY = baseElevation;
            if (float.IsNaN(endY)) endY = baseElevation;
            float BaseY(float z) => Mathf.Lerp(startY, endY, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((z - edge) / span)));

            int bends = curvy ? rng.Range(s.minBends, s.maxBends + 1) + (sharp ? 2 : 0) : 0;
            int interior = Mathf.Max(bends, elevation == RoadElevationType.Flat ? 1 : 5);

            // Random gap lengths between control points -> random bend lengths
            float[] gaps = new float[interior + 1];
            float total = 0f;
            for (int i = 0; i < gaps.Length; i++)
            {
                gaps[i] = rng.Range(1f, Mathf.Max(1.01f, s.lengthVariance));
                total += gaps[i];
            }

            var pts = new List<Vector3> { new Vector3(startX, startY, 0f) };
            float prevZ = edge, prevX = 0f, cum = 0f;
            float sign = rng.Value > 0.5f ? 1f : -1f;
            float peak = 0f;
            float minAmp = sharp ? Mathf.Lerp(s.minShift, s.maxShift, 0.5f) : s.minShift;

            for (int i = 0; i < interior; i++)
            {
                cum += gaps[i];
                float z = edge + span * (cum / total);
                float x = 0f; // bend offset from the base line

                if (curvy && i < bends)
                {
                    if (i > 0 && rng.Value < 0.75f) sign = -sign;
                    x = sign * rng.Range(minAmp, s.maxShift);
                    x = Mathf.Clamp(x, prevX - s.maxSlope * (z - prevZ), prevX + s.maxSlope * (z - prevZ));
                    float toExit = s.maxSlope * (length - edge - z);
                    float baseX = BaseX(z);
                    float lateral = Mathf.Min(s.maxLateral, toExit);
                    x = Mathf.Clamp(x, Mathf.Max(-lateral, -maxAbsX - baseX), Mathf.Min(lateral, maxAbsX - baseX));
                    if (Mathf.Abs(x) > Mathf.Abs(peak)) peak = x;
                }

                pts.Add(new Vector3(BaseX(z) + x, BaseY(z) + ElevationOffset(z / length, elevation, hillHeight, dipDepth), z));
                prevZ = z;
                prevX = x;
            }

            pts.Insert(1, new Vector3(startX, startY + ElevationOffset(edge / length, elevation, hillHeight, dipDepth), edge));
            pts.Add(new Vector3(endX, endY + ElevationOffset((length - edge) / length, elevation, hillHeight, dipDepth), length - edge));
            pts.Add(new Vector3(endX, endY, length));
            waypoints = pts;

            // Soften bends until no turn is tighter than minTurnRadius (only the bend part, not the base line)
            if (curvy)
            {
                for (int attempt = 0; attempt < 10 && MinTurnRadius() < s.minTurnRadius; attempt++)
                {
                    for (int i = 0; i < waypoints.Count; i++)
                    {
                        Vector3 wp = waypoints[i];
                        float baseX = BaseX(wp.z);
                        wp.x = baseX + (wp.x - baseX) * 0.85f;
                        waypoints[i] = wp;
                    }
                }
                peak = 0f;
                foreach (var wp in waypoints)
                {
                    float bend = wp.x - BaseX(wp.z);
                    if (Mathf.Abs(bend) > Mathf.Abs(peak)) peak = bend;
                }
            }
            return peak;
        }

        /// <summary>Smallest horizontal turn radius along the current spline (Menger curvature, 5m samples).</summary>
        public float MinTurnRadius()
        {
            const float h = 5f;
            float minRadius = float.MaxValue;
            for (float z = h; z <= 500f - h; z += h)
            {
                Vector3 a = GetPointAtZ(z - h), b = GetPointAtZ(z), c = GetPointAtZ(z + h);
                float cross = Mathf.Abs((b.x - a.x) * (c.z - a.z) - (b.z - a.z) * (c.x - a.x));
                if (cross < 1e-4f) continue;
                float ab = new Vector2(b.x - a.x, b.z - a.z).magnitude;
                float bc = new Vector2(c.x - b.x, c.z - b.z).magnitude;
                float ca = new Vector2(a.x - c.x, a.z - c.z).magnitude;
                float radius = (ab * bc * ca) / (2f * cross);
                if (radius < minRadius) minRadius = radius;
            }
            return minRadius;
        }

        private static float ElevationOffset(float u, RoadElevationType type, float hill, float dip)
        {
            float f;
            switch (type)
            {
                case RoadElevationType.HillCrest:
                case RoadElevationType.MountainPass:
                case RoadElevationType.ElevatedChicane:
                    f = hill * Mathf.Sin(Mathf.PI * u);
                    break;
                case RoadElevationType.ValleyDip:
                    f = -dip * Mathf.Sin(Mathf.PI * u);
                    break;
                case RoadElevationType.RollingHills:
                    float w = Mathf.Sin(2f * Mathf.PI * u);
                    f = w * (w > 0f ? hill : dip);
                    break;
                default:
                    return 0f;
            }
            // Flatten the ends so slope is zero at chunk borders
            float window = Mathf.SmoothStep(0f, 1f, u / 0.18f) * Mathf.SmoothStep(0f, 1f, (1f - u) / 0.18f);
            return f * window;
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

        /// <summary>
        /// Evaluates the road spline at local target Z coordinate (alias for GetPointAtZ).
        /// </summary>
        public Vector3 SampleAtZ(float targetZ)
        {
            return GetPointAtZ(targetZ);
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

        /// <summary>
        /// Calculates the horizontal (X/Z) distance from a given world position to the road centerline.
        /// </summary>
        public float GetDistanceToSpline(Vector3 worldPos)
        {
            Vector3 localPos = transform.InverseTransformPoint(worldPos);
            Vector3 roadLocal = SampleAtZ(localPos.z);
            Vector3 roadWorld = transform.TransformPoint(roadLocal);
            return Vector2.Distance(new Vector2(worldPos.x, worldPos.z), new Vector2(roadWorld.x, roadWorld.z));
        }

        /// <summary>
        /// Gets the world-space road centerline point at the given world Z coordinate.
        /// </summary>
        public Vector3 GetRoadCenterWorld(float worldZ)
        {
            Vector3 localZPos = transform.InverseTransformPoint(new Vector3(0, 0, worldZ));
            Vector3 roadLocal = SampleAtZ(localZPos.z);
            return transform.TransformPoint(roadLocal);
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
