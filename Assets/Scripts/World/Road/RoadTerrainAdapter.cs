using UnityEngine;

namespace EndlessSurvival.World.Road
{
    /// <summary>
    /// Deforms and conforms Unity Terrain heightmaps directly underneath and around RoadSpline roads.
    /// Creates realistic highway embankments (dolgu), cuttings (yarma), and prevents any gaps,
    /// floating roads, or road clipping inside terrain.
    /// </summary>
    public static class RoadTerrainAdapter
    {
        /// <summary>
        /// Deforms the chunk terrain so the road sits seamlessly on top of a solid earth platform.
        /// </summary>
        /// <param name="terrain">Target Unity Terrain component</param>
        /// <param name="spline">The spline defining road path and elevation</param>
        /// <param name="roadGen">The road generator (provides width and cross-section metrics)</param>
        /// <param name="addAmbientLandscape">Whether to add natural gentle rolling hills in the background</param>
        public static void ConformTerrainToRoad(Terrain terrain, RoadSpline spline, RoadGenerator roadGen, bool addAmbientLandscape = true)
        {
            if (terrain == null || spline == null) return;

            // In play mode, ensure each chunk operates on its own cloned runtime TerrainData instance
            if (Application.isPlaying)
            {
                if (!terrain.gameObject.name.Contains("(RuntimeClone)"))
                {
                    terrain.terrainData = Object.Instantiate(terrain.terrainData);
                    terrain.gameObject.name += " (RuntimeClone)";
                    var tCol = terrain.GetComponent<TerrainCollider>();
                    if (tCol != null) tCol.terrainData = terrain.terrainData;
                }
            }

            TerrainData td = terrain.terrainData;
            if (td == null) return;

            int hRes = td.heightmapResolution;
            Vector3 terrainSize = td.size; // 500 x 100 x 500
            float[,] heights = td.GetHeights(0, 0, hRes, hRes);

            float roadHalfWidth = roadGen != null ? roadGen.GetTotalHalfWidth() : 8.5f;
            float terrainXOffset = terrain.transform.localPosition.x; // Typically -250f

            // Sample row by row along the chunk forward axis (Z = 0 to 500m)
            for (int z = 0; z < hRes; z++)
            {
                float normalizedZ = (float)z / (hRes - 1);
                float localZ = normalizedZ * terrainSize.z;

                // Exact spatial evaluation at terrain localZ
                Vector3 roadPos = spline.GetPointAtZ(localZ);
                float roadX = roadPos.x;
                float roadY = roadPos.y;

                // Detect if road is recessed into the ground (cutting/trench/çukur) or elevated on a hill (embankment/dolgu)
                bool isDepression = roadY < spline.baseElevation;

                // For depressions/valleys, widen flat platform so terrain heightmap quads (~3.9m) never slope through road shoulders
                float platformHalfWidth = isDepression ? (roadHalfWidth + 5.0f) : (roadHalfWidth + 2.0f);
                float blendDistance = isDepression ? 45.0f : 35.0f;
                float roadBedElevation = isDepression ? (roadY - 0.25f) : (roadY - 0.20f);

                // Seamless chunk border edge fade: ensure boundaries at Z=0 and Z=500 cleanly lock to baseline
                float boundaryFade = Mathf.Clamp01(Mathf.Sin(normalizedZ * Mathf.PI));
                float ambientAmplitude = boundaryFade * 8.0f;

                for (int x = 0; x < hRes; x++)
                {
                    float normalizedX = (float)x / (hRes - 1);
                    float localX = terrainXOffset + (normalizedX * terrainSize.x);
                    float distToRoad = Mathf.Abs(localX - roadX);

                    // Ambient natural landscape outside the road bed
                    float ambientNoise = 0f;
                    if (addAmbientLandscape)
                    {
                        float perlin = Mathf.PerlinNoise((localX + 1000f) * 0.007f, (localZ + 1000f) * 0.007f);
                        ambientNoise = (perlin * 2f - 1f) * ambientAmplitude;
                    }
                    float naturalLandscapeY = spline.baseElevation + ambientNoise;

                    // Blend road bed into surrounding terrain
                    float finalHeightMeters;
                    if (distToRoad <= platformHalfWidth)
                    {
                        finalHeightMeters = roadBedElevation;
                    }
                    else if (distToRoad <= platformHalfWidth + blendDistance)
                    {
                        float blendFactor = (distToRoad - platformHalfWidth) / blendDistance;
                        float smooth = Mathf.SmoothStep(0f, 1f, blendFactor);
                        finalHeightMeters = Mathf.Lerp(roadBedElevation, naturalLandscapeY, smooth);
                    }
                    else
                    {
                        finalHeightMeters = naturalLandscapeY;
                    }

                    heights[z, x] = Mathf.Clamp01(finalHeightMeters / terrainSize.y);
                }
            }

            td.SetHeights(0, 0, heights);
            terrain.Flush();

#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                UnityEditor.EditorUtility.SetDirty(td);
            }
#endif
        }
    }
}
