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
                    TerrainLayer[] oldLayers = terrain.terrainData.terrainLayers;
                    Material oldMat = terrain.materialTemplate;
                    
                    terrain.terrainData = Object.Instantiate(terrain.terrainData);
                    terrain.terrainData.terrainLayers = oldLayers;
                    terrain.materialTemplate = oldMat;
                    
                    terrain.gameObject.name += " (RuntimeClone)";
                    var tCol = terrain.GetComponent<TerrainCollider>();
                    if (tCol != null) tCol.terrainData = terrain.terrainData;
                    
                    terrain.allowAutoConnect = false;
                }
            }

            TerrainData td = terrain.terrainData;
            if (td == null) return;

            int hRes = td.heightmapResolution;
            Vector3 terrainSize = td.size; // 500 x 100 x 500
            float[,] heights = td.GetHeights(0, 0, hRes, hRes);

            float roadHalfWidth = roadGen != null ? roadGen.GetTotalHalfWidth() : 8.5f;
            float terrainXOffset = terrain.transform.localPosition.x; // Typically -250f

            // Side boundaries (cliffs) are added on top of the natural landscape, outside the road clearance
            var boundary = terrain.GetComponentInParent<ChunkBoundaryGenerator>();
            bool useBoundary = boundary != null && boundary.BuildPlan(spline, hRes, terrainSize.z, terrainSize.x * 0.5f);

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
                    if (useBoundary) naturalLandscapeY += boundary.GetHeightOffset(localX, z);

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

        /// <summary>
        /// Flattens a circular area of terrain around worldCenter into a level platform and blends smoothly into surrounding terrain.
        /// </summary>
        public static void FlattenTerrainArea(Terrain terrain, Vector3 worldCenter, float flatRadius, float blendRadius, float? targetWorldY = null)
        {
            if (terrain == null) return;
            EnsureRuntimeTerrainClone(terrain);

            TerrainData td = terrain.terrainData;
            if (td == null) return;

            int hRes = td.heightmapResolution;
            Vector3 tSize = td.size; // 500 x 100 x 500
            Vector3 tPos = terrain.transform.position;

            float targetLocalY = targetWorldY.HasValue ? (targetWorldY.Value - tPos.y) : terrain.SampleHeight(worldCenter);

            float totalRadius = flatRadius + blendRadius;

            // Convert worldCenter to heightmap sample coordinates
            float normCenterX = (worldCenter.x - tPos.x) / tSize.x;
            float normCenterZ = (worldCenter.z - tPos.z) / tSize.z;

            int centerSampleX = Mathf.RoundToInt(normCenterX * (hRes - 1));
            int centerSampleZ = Mathf.RoundToInt(normCenterZ * (hRes - 1));

            int radiusSamplesX = Mathf.CeilToInt((totalRadius / tSize.x) * (hRes - 1));
            int radiusSamplesZ = Mathf.CeilToInt((totalRadius / tSize.z) * (hRes - 1));

            int minX = Mathf.Clamp(centerSampleX - radiusSamplesX, 0, hRes - 1);
            int maxX = Mathf.Clamp(centerSampleX + radiusSamplesX, 0, hRes - 1);
            int minZ = Mathf.Clamp(centerSampleZ - radiusSamplesZ, 0, hRes - 1);
            int maxZ = Mathf.Clamp(centerSampleZ + radiusSamplesZ, 0, hRes - 1);

            int width = maxX - minX + 1;
            int height = maxZ - minZ + 1;
            if (width <= 0 || height <= 0) return;

            float[,] heights = td.GetHeights(minX, minZ, width, height);

            for (int z = 0; z < height; z++)
            {
                int sampleZ = minZ + z;
                float normZ = (float)sampleZ / (hRes - 1);
                float worldZ = tPos.z + normZ * tSize.z;

                for (int x = 0; x < width; x++)
                {
                    int sampleX = minX + x;
                    float normX = (float)sampleX / (hRes - 1);
                    float worldX = tPos.x + normX * tSize.x;

                    float dist = Vector2.Distance(new Vector2(worldX, worldZ), new Vector2(worldCenter.x, worldCenter.z));
                    if (dist > totalRadius) continue;

                    float currentHeightMeters = heights[z, x] * tSize.y;
                    float finalHeightMeters;

                    if (dist <= flatRadius)
                    {
                        finalHeightMeters = targetLocalY;
                    }
                    else
                    {
                        float blendFactor = (dist - flatRadius) / blendRadius;
                        float smooth = Mathf.SmoothStep(0f, 1f, blendFactor);
                        finalHeightMeters = Mathf.Lerp(targetLocalY, currentHeightMeters, smooth);
                    }

                    heights[z, x] = Mathf.Clamp01(finalHeightMeters / tSize.y);
                }
            }

            td.SetHeights(minX, minZ, heights);
            terrain.Flush();

#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                UnityEditor.EditorUtility.SetDirty(td);
            }
#endif
        }

        public static void EnsureRuntimeTerrainClone(Terrain terrain)
        {
            if (terrain == null) return;
            if (Application.isPlaying && !terrain.gameObject.name.Contains("(RuntimeClone)"))
            {
                TerrainLayer[] oldLayers = terrain.terrainData.terrainLayers;
                Material oldMat = terrain.materialTemplate;

                terrain.terrainData = Object.Instantiate(terrain.terrainData);
                terrain.terrainData.terrainLayers = oldLayers;
                terrain.materialTemplate = oldMat;

                terrain.gameObject.name += " (RuntimeClone)";
                var tCol = terrain.GetComponent<TerrainCollider>();
                if (tCol != null) tCol.terrainData = terrain.terrainData;

                terrain.allowAutoConnect = false;
            }
        }
    }
}
