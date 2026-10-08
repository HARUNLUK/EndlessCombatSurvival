using System.Collections.Generic;
using UnityEngine;
using EndlessSurvival.World.Road;
using EndlessSurvival.World.POI;

namespace EndlessSurvival.World
{
    /// <summary>
    /// Spawns procedural environment vegetation (trees, rocks, bushes) across a chunk.
    /// Supports both:
    ///  1) Unity Terrain TreeInstance System (Maximum optimization, GPU instancing, zero GameObject overhead, Box+Capsule bounds)
    ///  2) Independent GameObjects (for future physical tree chopping, falling, and survival crafting mechanics)
    /// </summary>
    [ExecuteAlways]
    public class ChunkVegetationSpawner : MonoBehaviour
    {
        public enum VegetationRenderMode
        {
            [Tooltip("Unity Terrain TreeInstance sistemi (En yüksek optimizasyon, GPU instancing, otomatik billboard LOD, sıfır GameObject yükü)")]
            TerrainTrees,

            [Tooltip("Bağımsız Prefab objeleri (İleride balta ile kesme, devrilme, parçalanma ve survival mekanikleri için)")]
            GameObjects
        }

        [System.Serializable]
        public struct PoiExclusionZone
        {
            public Vector3 center;
            public float radius;
            public float sqrRadius;

            public PoiExclusionZone(Vector3 pos, float r)
            {
                center = pos;
                radius = r;
                sqrRadius = r * r;
            }
        }

        [Header("Render & Optimization Mode (Performans & Çizim Modu)")]
        [Tooltip("TerrainTrees = Unity Terrain GPU instanced ağaç motoru (Maksimum FPS). GameObjects = Fiziksel bağımsız objeler.")]
        public VegetationRenderMode renderMode = VegetationRenderMode.GameObjects;

        [Header("Biome Filtering")]
        [Tooltip("Allowed biomes for this vegetation spawner")]
        public ChunkBiomeType targetBiome = ChunkBiomeType.Forest;

        [Header("Vegetation Pools")]
        [Tooltip("Tree prefabs (Pine, Oak, Dead trees, etc.)")]
        public GameObject[] treePrefabs;

        [Tooltip("Detail/Ground prop prefabs (Rocks, Bushes, etc.)")]
        public GameObject[] detailPrefabs;

        [Header("Density & Counts (Yoğunluk & Sayı)")]
        [Tooltip("Minimum number of trees to spawn per chunk")]
        [Range(20, 800)]
        public int minTrees = 180;

        [Tooltip("Maximum number of trees to spawn per chunk")]
        [Range(20, 800)]
        public int maxTrees = 320;

        [Tooltip("Minimum number of detail props (bushes, rocks) to spawn")]
        [Range(10, 300)]
        public int minDetails = 60;

        [Tooltip("Maximum number of detail props (bushes, rocks) to spawn")]
        [Range(10, 300)]
        public int maxDetails = 120;

        [Tooltip("Genel yoğunluk çarpanı (1.0 = normal, 1.5 = %50 daha sık, 0.5 = daha seyrek)")]
        [Range(0.2f, 3.0f)]
        public float densityMultiplier = 1.0f;

        [Header("Placement Boundaries")]
        [Tooltip("Minimum distance from road centerline. Must clear the asphalt, curbs, and shoulders.")]
        public float minRoadDistance = 15f;

        [Tooltip("Maximum distance from road centerline.")]
        public float maxRoadDistance = 238f;

        [Tooltip("Minimum spacing between spawned trees to prevent overlap")]
        public float minTreeSpacing = 3.6f;

        [Header("POI Clearings (Orman Açıklığı)")]
        [Tooltip("Minimum clearing radius around enemy camps so trees do not overlap tents/fire")]
        public float campClearingRadius = 32f;

        [Tooltip("Minimum clearing radius around caves and story structures")]
        public float caveClearingRadius = 38f;

        [Header("Scale & Rotation Variation (Ağaç Boyutları)")]
        [Tooltip("Minimum random scale multiplier for trees")]
        public float minScale = 1.8f;

        [Tooltip("Maximum random scale multiplier for trees")]
        public float maxScale = 3.2f;

        [Header("Ground Detection")]
        public LayerMask groundMask = ~0;

        private const string CONTAINER_NAME = "Vegetation_Container";
        private ChunkBoundaryGenerator _boundary;

        private void OnEnable()
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                UnityEditor.EditorApplication.delayCall += AutoCheckPreview;
            }
#endif
        }

#if UNITY_EDITOR
        [UnityEditor.Callbacks.DidReloadScripts]
        private static void OnScriptsReloaded()
        {
            // When Unity recompiles scripts or domain reloads, flush Terrain tree batch so trees never vanish
            UnityEditor.EditorApplication.delayCall += () =>
            {
                var spawners = Object.FindObjectsByType<ChunkVegetationSpawner>(FindObjectsInactive.Include, FindObjectsSortMode.None);
                foreach (var spawner in spawners)
                {
                    if (spawner != null)
                    {
                        spawner.AutoCheckPreview();
                    }
                }
            };
        }

        private static void DisableTreeColliders(Terrain t)
        {
            if (t == null) return;
            TerrainCollider tCol = t.GetComponent<TerrainCollider>();
            if (tCol == null) return;
            UnityEditor.SerializedObject so = new UnityEditor.SerializedObject(tCol);
            UnityEditor.SerializedProperty prop = so.FindProperty("m_EnableTreeColliders");
            if (prop != null && prop.boolValue)
            {
                prop.boolValue = false;
                so.ApplyModifiedProperties();
            }
        }

        private void OnValidate()
        {
            if (!Application.isPlaying && gameObject.scene.name != null)
            {
                UnityEditor.EditorApplication.delayCall += () =>
                {
                    if (this != null && gameObject != null && !Application.isPlaying)
                    {
                        AutoCheckPreview();
                    }
                };
            }
        }
#endif

        private void AutoCheckPreview()
        {
#if UNITY_EDITOR
            if (this == null || gameObject == null || Application.isPlaying) return;
            
            Chunk chunk = GetComponent<Chunk>();
            if (chunk == null) chunk = GetComponentInParent<Chunk>();
            Terrain t = chunk != null ? chunk.ChunkTerrain : GetComponentInChildren<Terrain>();
            if (t == null && chunk != null) t = chunk.GetComponentInChildren<Terrain>();
            if (t == null) t = GetComponent<Terrain>();
            if (t == null) t = GetComponentInParent<Terrain>();

            if (renderMode == VegetationRenderMode.TerrainTrees)
            {
                if (t != null && t.terrainData != null)
                {
                    DisableTreeColliders(t);

                    if (t.terrainData.treeInstanceCount > 0)
                    {
                        // On domain reload / Unity script refresh, Unity's Terrain engine unloads its GPU draw batch.
                        // Force redraw buffer flush so trees, rocks, and bushes NEVER vanish on Unity refresh!
                        t.drawTreesAndFoliage = true;
                        t.treeDistance = 2000f;
                        t.treeBillboardDistance = 2000f;
                        t.treeCrossFadeLength = 0f;
                        t.treeMaximumFullLODCount = 2000;
                        t.Flush();
                        UnityEditor.SceneView.RepaintAll();
                        return;
                    }
                }
            }
            else
            {
                Transform container = transform.Find(CONTAINER_NAME);
                if (container == null) container = transform.Find("EditorPreview/" + CONTAINER_NAME);
                if (container != null && container.childCount > 0)
                {
                    return;
                }
            }

            GenerateVegetation();
#endif
        }

        private void Start()
        {
            if (Application.isPlaying)
            {
                // ChunkManager already generates vegetation after terrain/road conforming; only fill in if nothing exists yet.
                Transform existing = transform.Find(CONTAINER_NAME);
                Terrain t = GetComponentInChildren<Terrain>();
                bool hasContainer = existing != null && existing.childCount > 0;
                bool hasTerrainTrees = t != null && t.terrainData != null && t.terrainData.treeInstanceCount > 0;
                if (!hasContainer && !hasTerrainTrees)
                {
                    GenerateVegetation();
                }
            }
        }

        /// <summary>
        /// Clears existing spawned vegetation and deterministically generates new trees & props.
        /// Respects POI exclusion zones (camps, caves, loot) so they are never hidden by trees.
        /// </summary>
        [ContextMenu("Generate Vegetation Now")]
        public void GenerateVegetation(Transform customContainerParent = null, SeededRandom customRng = null)
        {
            Chunk chunk = GetComponent<Chunk>();
            if (chunk == null) chunk = GetComponentInParent<Chunk>();
            _boundary = chunk != null ? chunk.GetComponent<ChunkBoundaryGenerator>() : null;

            // In TerrainTrees mode, completely eradicate any Vegetation_Container! Everything is on the Terrain!
            if (renderMode == VegetationRenderMode.TerrainTrees)
            {
                DestroyAllVegetationContainers();
            }

            Transform containerParent = customContainerParent;
            if (containerParent == null)
            {
                Transform ep = null;
                if (!Application.isPlaying)
                {
                    ep = transform.Find("EditorPreview");
                    if (ep == null && chunk != null) ep = chunk.transform.Find("EditorPreview");
                }
                containerParent = ep != null ? ep : transform;
            }

            Transform vegContainer = null;
            if (renderMode == VegetationRenderMode.GameObjects)
            {
                vegContainer = containerParent.Find(CONTAINER_NAME);
                if (vegContainer == null)
                {
                    GameObject go = new GameObject(CONTAINER_NAME);
                    go.transform.SetParent(containerParent, false);
                    vegContainer = go.transform;
                }
                ClearContainer(vegContainer);
            }

            // Biome check: Only spawn forest trees on Forest chunks (or if no chunk component)
            if (chunk != null && chunk.biomeType != targetBiome)
            {
                return;
            }

            RoadSpline spline = GetComponentInChildren<RoadSpline>();
            if (spline == null && chunk != null)
            {
                spline = chunk.GetComponentInChildren<RoadSpline>();
            }

            if (spline == null)
            {
                return;
            }

            // Derive deterministic random stream from chunk seed
            SeededRandom rng = customRng;
            if (rng == null)
            {
                int seed = chunk != null ? chunk.ChunkSeed : GetHashCode();
                rng = new SeededRandom(SeededRandom.Combine(seed, "vegetation"));
            }

            Terrain terrainComp = chunk != null ? chunk.ChunkTerrain : GetComponentInChildren<Terrain>();
            if (terrainComp == null && chunk != null)
            {
                terrainComp = chunk.GetComponentInChildren<Terrain>();
            }

            // Collect all POI exclusion clearings (camps, caves, lighthouses, etc.)
            List<PoiExclusionZone> exclusionZones = CollectPoiExclusionZones(chunk, containerParent);
            List<Vector3> placedTreePositions = new List<Vector3>();

            // 1. TERRAIN TREES MODE: All trees and detail props managed by Unity Terrain Engine
            if (renderMode == VegetationRenderMode.TerrainTrees && terrainComp != null && terrainComp.terrainData != null)
            {
                EnsureTerrainTreePrototypes(terrainComp.terrainData);
                List<TreeInstance> allInstances = new List<TreeInstance>();
                Vector3 terrainSize = terrainComp.terrainData.size;

                // A) TREES ON TERRAIN
                if (treePrefabs != null && treePrefabs.Length > 0)
                {
                    int treeCount = Mathf.RoundToInt(rng.Range(minTrees, maxTrees + 1) * Mathf.Max(0.1f, densityMultiplier));
                    int maxAttempts = treeCount * 5;
                    int spawned = 0;

                    for (int i = 0; i < maxAttempts && spawned < treeCount; i++)
                    {
                        if (TryFindValidPosition(rng, spline, terrainComp, exclusionZones, placedTreePositions, minTreeSpacing, out Vector3 worldPos))
                        {
                            int protoIndex = rng.Range(0, treePrefabs.Length);
                            float scale = rng.Range(minScale, maxScale);

                            // Convert world coordinates to normalized Terrain coordinates (0 to 1)
                            Vector3 localTerrainPos = terrainComp.transform.InverseTransformPoint(worldPos);
                            float normX = Mathf.Clamp01(localTerrainPos.x / terrainSize.x);
                            float normY = Mathf.Clamp01(localTerrainPos.y / terrainSize.y);
                            float normZ = Mathf.Clamp01(localTerrainPos.z / terrainSize.z);

                            TreeInstance ti = new TreeInstance();
                            ti.position = new Vector3(normX, normY, normZ);
                            ti.widthScale = scale;
                            ti.heightScale = scale;
                            ti.rotation = rng.Range(0f, Mathf.PI * 2f);
                            ti.prototypeIndex = protoIndex;
                            ti.color = Color.white;
                            ti.lightmapColor = Color.white;

                            allInstances.Add(ti);
                            placedTreePositions.Add(worldPos);
                            spawned++;
                        }
                    }
                }

                // B) DETAIL PROPS (ROCKS & BUSHES) ON TERRAIN
                if (detailPrefabs != null && detailPrefabs.Length > 0)
                {
                    int detailCount = Mathf.RoundToInt(rng.Range(minDetails, maxDetails + 1) * Mathf.Max(0.1f, densityMultiplier));
                    int maxAttempts = detailCount * 4;
                    int spawned = 0;
                    int detailProtoOffset = treePrefabs != null ? treePrefabs.Length : 0;

                    for (int i = 0; i < maxAttempts && spawned < detailCount; i++)
                    {
                        if (TryFindValidPosition(rng, spline, terrainComp, exclusionZones, null, 2.5f, out Vector3 worldPos, minRoadDistOverride: 12f))
                        {
                            int protoIndex = detailProtoOffset + rng.Range(0, detailPrefabs.Length);
                            float scale = rng.Range(0.8f, 1.8f);

                            Vector3 localTerrainPos = terrainComp.transform.InverseTransformPoint(worldPos);
                            float normX = Mathf.Clamp01(localTerrainPos.x / terrainSize.x);
                            float normY = Mathf.Clamp01(localTerrainPos.y / terrainSize.y);
                            float normZ = Mathf.Clamp01(localTerrainPos.z / terrainSize.z);

                            TreeInstance ti = new TreeInstance();
                            ti.position = new Vector3(normX, normY, normZ);
                            ti.widthScale = scale;
                            ti.heightScale = scale;
                            ti.rotation = rng.Range(0f, Mathf.PI * 2f);
                            ti.prototypeIndex = protoIndex;
                            ti.color = Color.white;
                            ti.lightmapColor = Color.white;

                            allInstances.Add(ti);
                            spawned++;
                        }
                    }
                }

                terrainComp.terrainData.SetTreeInstances(allInstances.ToArray(), false);
                terrainComp.drawTreesAndFoliage = true;
                terrainComp.treeDistance = 2000f;
                terrainComp.treeBillboardDistance = 2000f;
                terrainComp.treeCrossFadeLength = 0f;
                terrainComp.Flush();

#if UNITY_EDITOR
                DisableTreeColliders(terrainComp);

                if (!Application.isPlaying)
                {
                    UnityEditor.EditorUtility.SetDirty(terrainComp);
                    UnityEditor.EditorUtility.SetDirty(terrainComp.terrainData);
                    UnityEditor.AssetDatabase.SaveAssetIfDirty(terrainComp.terrainData);
                    UnityEditor.SceneView.RepaintAll();
                }
#endif
                Debug.Log($"<color=cyan>[ChunkVegetationSpawner] Başarılı: {allInstances.Count} adet Terrain ağacı ve bitki örtüsü eklendi! (Chunk: {gameObject.name})</color>");
            }
            else
            {
                // 2. GAMEOBJECTS MODE: Spawn physical GameObjects in Vegetation_Container
                if (terrainComp != null && terrainComp.terrainData != null && terrainComp.terrainData.treeInstanceCount > 0)
                {
                    terrainComp.terrainData.SetTreeInstances(new TreeInstance[0], false);
#if UNITY_EDITOR
                    if (!Application.isPlaying)
                    {
                        UnityEditor.EditorUtility.SetDirty(terrainComp);
                        UnityEditor.EditorUtility.SetDirty(terrainComp.terrainData);
                    }
#endif
                }

                if (treePrefabs != null && treePrefabs.Length > 0)
                {
                    int treeCount = Mathf.RoundToInt(rng.Range(minTrees, maxTrees + 1) * Mathf.Max(0.1f, densityMultiplier));
                    int maxAttempts = treeCount * 5;
                    int spawned = 0;

                    for (int i = 0; i < maxAttempts && spawned < treeCount; i++)
                    {
                        if (TryFindValidPosition(rng, spline, terrainComp, exclusionZones, placedTreePositions, minTreeSpacing, out Vector3 worldPos))
                        {
                            GameObject prefab = rng.Pick(treePrefabs);
                            if (prefab == null) continue;

                            SpawnInstance(prefab, worldPos, rng, vegContainer);
                            placedTreePositions.Add(worldPos);
                            spawned++;
                        }
                    }
                }

                if (detailPrefabs != null && detailPrefabs.Length > 0)
                {
                    int detailCount = Mathf.RoundToInt(rng.Range(minDetails, maxDetails + 1) * Mathf.Max(0.1f, densityMultiplier));
                    int maxAttempts = detailCount * 4;
                    int spawned = 0;

                    for (int i = 0; i < maxAttempts && spawned < detailCount; i++)
                    {
                        if (TryFindValidPosition(rng, spline, terrainComp, exclusionZones, null, 2f, out Vector3 worldPos, minRoadDistOverride: 12f))
                        {
                            GameObject prefab = rng.Pick(detailPrefabs);
                            if (prefab == null) continue;

                            SpawnInstance(prefab, worldPos, rng, vegContainer, uniformScaleVariance: 0.25f);
                            spawned++;
                        }
                    }
                }

                Debug.Log($"<color=cyan>[ChunkVegetationSpawner] Başarılı: {vegContainer.childCount} adet fiziksel GameObject bitkisi oluşturuldu! (Chunk: {gameObject.name})</color>");
            }
        }

        private void EnsureTerrainTreePrototypes(TerrainData td)
        {
            if (td == null) return;

            var list = new List<TreePrototype>();

            // 1. Add tree prefabs
            if (treePrefabs != null)
            {
                for (int i = 0; i < treePrefabs.Length; i++)
                {
                    if (treePrefabs[i] == null) continue;
                    TreePrototype tp = new TreePrototype();
                    tp.prefab = treePrefabs[i];
                    list.Add(tp);
                }
            }

            // 2. Add detail prefabs (rocks, bushes) so they can also be managed by Terrain
            if (detailPrefabs != null)
            {
                for (int i = 0; i < detailPrefabs.Length; i++)
                {
                    if (detailPrefabs[i] == null) continue;
                    TreePrototype tp = new TreePrototype();
                    tp.prefab = detailPrefabs[i];
                    list.Add(tp);
                }
            }

            td.treePrototypes = list.ToArray();
            td.RefreshPrototypes();
        }

        private List<PoiExclusionZone> CollectPoiExclusionZones(Chunk chunk, Transform activeContainer)
        {
            var zones = new List<PoiExclusionZone>();

            Transform root = chunk != null ? chunk.transform : transform;

            // Find all PointOfInterest components anywhere in the chunk or container
            var pois = root.GetComponentsInChildren<PointOfInterest>(true);
            foreach (var poi in pois)
            {
                if (poi == null) continue;
                float r = 25f;
                if (poi is EnemyCampPOI camp)
                {
                    r = camp.campType == EnemyCampPOI.CampType.Outpost ? campClearingRadius * 1.3f : campClearingRadius;
                }
                else if (poi is CavePOI)
                {
                    r = caveClearingRadius;
                }
                else if (poi is LighthousePOI)
                {
                    r = caveClearingRadius;
                }

                zones.Add(new PoiExclusionZone(poi.transform.position, r));
            }

            // Also search explicitly for common POI child names if component lookup missed them
            string[] poiNames = new string[] { "Enemy Camp", "CavePOI", "LighthousePOI", "Campfire", "Outpost" };
            foreach (var name in poiNames)
            {
                Transform found = root.Find(name);
                if (found != null && !HasExistingZone(zones, found.position))
                {
                    zones.Add(new PoiExclusionZone(found.position, campClearingRadius));
                }
            }

            if (activeContainer != null && activeContainer != root)
            {
                foreach (var name in poiNames)
                {
                    Transform found = activeContainer.Find(name);
                    if (found != null && !HasExistingZone(zones, found.position))
                    {
                        zones.Add(new PoiExclusionZone(found.position, campClearingRadius));
                    }
                }
            }

            return zones;
        }

        private bool HasExistingZone(List<PoiExclusionZone> zones, Vector3 pos)
        {
            for (int i = 0; i < zones.Count; i++)
            {
                Vector3 d = zones[i].center - pos;
                d.y = 0;
                if (d.sqrMagnitude < 100f) return true;
            }
            return false;
        }

        private void SpawnInstance(GameObject prefab, Vector3 worldPos, SeededRandom rng, Transform container, float uniformScaleVariance = 0f)
        {
            Quaternion rot = Quaternion.Euler(0f, rng.Range(0f, 360f), 0f);
            float scaleMultiplier = rng.Range(minScale, maxScale);

#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                GameObject obj = UnityEditor.PrefabUtility.InstantiatePrefab(prefab, container) as GameObject;
                if (obj != null)
                {
                    obj.transform.position = worldPos;
                    obj.transform.rotation = rot;
                    obj.transform.localScale = prefab.transform.localScale * scaleMultiplier;
                }
                return;
            }
#endif

            GameObject spawnedObj = Instantiate(prefab, worldPos, rot, container);
            spawnedObj.transform.localScale = prefab.transform.localScale * scaleMultiplier;
        }

        private bool TryFindValidPosition(
            SeededRandom rng,
            RoadSpline spline,
            Terrain terrainComp,
            List<PoiExclusionZone> exclusionZones,
            List<Vector3> existingPositions,
            float minSpacing,
            out Vector3 worldPos,
            float minRoadDistOverride = -1f)
        {
            worldPos = Vector3.zero;

            float effectiveMinRoadDist = minRoadDistOverride > 0f ? minRoadDistOverride : minRoadDistance;

            // Pick a random progress along the 500m road
            float t = rng.Range(0.02f, 0.98f);
            Transform splineTransform = spline.transform;
            Vector3 roadCenter = splineTransform.TransformPoint(spline.GetPoint(t));
            Vector3 roadRight = splineTransform.TransformDirection(spline.GetRight(t));

            // Choose Left or Right side of the road
            float side = rng.Value < 0.5f ? -1f : 1f;

            // Lateral offset from road centerline
            float lateralDist = rng.Range(effectiveMinRoadDist, maxRoadDistance);
            Vector3 candidatePos = roadCenter + roadRight * side * lateralDist;

            // Bounds check inside the 500x500 chunk area (-245 to +245 in local coords)
            Vector3 localToChunk = transform.InverseTransformPoint(candidatePos);
            if (Mathf.Abs(localToChunk.x) > 245f || localToChunk.z < 5f || localToChunk.z > 495f)
            {
                return false;
            }

            // Keep cliff faces and boundary rocks free of trees (cliff tops stay forested)
            if (_boundary != null && _boundary.IsBlockedForVegetation(_boundary.transform.InverseTransformPoint(candidatePos)))
            {
                return false;
            }

            // POI CLEARING CHECK: Do not spawn trees inside enemy camps, caves, or other POIs!
            if (exclusionZones != null && exclusionZones.Count > 0)
            {
                for (int i = 0; i < exclusionZones.Count; i++)
                {
                    Vector3 diff = candidatePos - exclusionZones[i].center;
                    diff.y = 0f;
                    if (diff.sqrMagnitude < exclusionZones[i].sqrRadius)
                    {
                        return false; // Point is inside a camp or cave clearing!
                    }
                }
            }

            // Height adaptation (Terrain SampleHeight or Raycast)
            if (terrainComp != null)
            {
                candidatePos.y = terrainComp.SampleHeight(candidatePos) + terrainComp.transform.position.y;
            }
            else
            {
                Vector3 rayOrigin = candidatePos + Vector3.up * 100f;
                if (Physics.Raycast(rayOrigin, Vector3.down, out RaycastHit hit, 250f, groundMask, QueryTriggerInteraction.Ignore))
                {
                    candidatePos.y = hit.point.y;
                }
                else
                {
                    candidatePos.y = transform.position.y + 20f; // Baseline fallback
                }
            }

            // Verify minimum spacing from existing trees
            if (existingPositions != null && existingPositions.Count > 0)
            {
                float sqrMinSpacing = minSpacing * minSpacing;
                for (int i = 0; i < existingPositions.Count; i++)
                {
                    Vector3 diff = existingPositions[i] - candidatePos;
                    diff.y = 0f;
                    if (diff.sqrMagnitude < sqrMinSpacing)
                    {
                        return false;
                    }
                }
            }

            worldPos = candidatePos;
            return true;
        }

        /// <summary>
        /// Clears all spawned vegetation instances from both the container and the Terrain.
        /// </summary>
        [ContextMenu("Clear Vegetation")]
        public void ClearVegetation()
        {
            if (renderMode == VegetationRenderMode.TerrainTrees)
            {
                DestroyAllVegetationContainers();
            }
            else
            {
                Transform container = transform.Find(CONTAINER_NAME);
                if (container != null) ClearContainer(container);
            }

            Terrain terrainComp = GetComponentInChildren<Terrain>();
            if (terrainComp != null && terrainComp.terrainData != null)
            {
                terrainComp.terrainData.SetTreeInstances(new TreeInstance[0], false);
            }
        }

        public void DestroyAllVegetationContainers()
        {
            Transform searchRoot = transform;
            Chunk chunk = GetComponent<Chunk>();
            if (chunk == null) chunk = GetComponentInParent<Chunk>();
            if (chunk != null) searchRoot = chunk.transform;

            var allTransforms = searchRoot.GetComponentsInChildren<Transform>(true);
            for (int i = allTransforms.Length - 1; i >= 0; i--)
            {
                if (allTransforms[i] != null && allTransforms[i].name.StartsWith(CONTAINER_NAME))
                {
#if UNITY_EDITOR
                    if (!Application.isPlaying)
                        DestroyImmediate(allTransforms[i].gameObject);
                    else
#endif
                        Destroy(allTransforms[i].gameObject);
                }
            }
        }

        private void ClearContainer(Transform container)
        {
            if (container == null) return;
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                while (container.childCount > 0)
                {
                    DestroyImmediate(container.GetChild(0).gameObject);
                }
                return;
            }
#endif
            foreach (Transform child in container)
            {
                Destroy(child.gameObject);
            }
        }
    }
}
