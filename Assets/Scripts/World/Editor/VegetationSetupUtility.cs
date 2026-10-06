#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using EndlessSurvival.World;

namespace EndlessSurvival.World.Editor
{
    public static class VegetationSetupUtility
    {
        private const string PREF_KEY = "Forest_Vegetation_Setup_V17_FixedCapsuleProportions";

        [InitializeOnLoadMethod]
        private static void AutoRunSetupOnce()
        {
            if (!EditorPrefs.GetBool(PREF_KEY, false))
            {
                EditorPrefs.SetBool(PREF_KEY, true);
                EditorApplication.delayCall += () =>
                {
                    PurgeAllVegetationContainers();
                    StripAllVegetationColliders();
                    SetupForestVegetation();
                };
            }
        }

        [MenuItem("Endless Survival/Remove All Vegetation Colliders (Trees, Rocks, Bushes)", false, 15)]
        [MenuItem("Tools/Endless Survival/Remove All Vegetation Colliders (Trees, Rocks, Bushes)", false, 15)]
        public static void StripAllVegetationColliders()
        {
            string[] prefabs = new string[]
            {
                "Assets/Prefabs/Environment/Trees/Tree_Pine_Blocky.prefab",
                "Assets/Prefabs/Environment/Trees/Tree_Oak_Blocky.prefab",
                "Assets/Prefabs/Environment/Trees/Tree_Dead_Blocky.prefab",
                "Assets/Prefabs/Environment/Trees/Rock_Forest_Blocky.prefab",
                "Assets/Prefabs/Environment/Trees/Bush_Forest_Blocky.prefab"
            };

            foreach (var path in prefabs)
            {
                GameObject root = PrefabUtility.LoadPrefabContents(path);
                if (root != null)
                {
                    var cols = root.GetComponentsInChildren<Collider>(true);
                    foreach (var col in cols)
                    {
                        col.isTrigger = true;
                    }
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                    PrefabUtility.UnloadPrefabContents(root);
                }
            }
            AssetDatabase.SaveAssets();
            Debug.Log("<color=green>[VegetationSetup] Tüm ağaç, kaya ve çalı prefablarının collider'ları Trigger (çarpışmasız) yapıldı! Bounds korundu, fiziksel çarpışma sıfırlandı.</color>");
        }

        [MenuItem("Endless Survival/Purge All Vegetation_Containers (Terrain Only)", false, 14)]
        [MenuItem("Tools/Endless Survival/Purge All Vegetation_Containers (Terrain Only)", false, 14)]
        public static void PurgeAllVegetationContainers()
        {
            // 1. Purge from Chunk_Forest_Curve.prefab
            string chunkPrefabPath = "Assets/Prefabs/Chunks/Chunk_Forest_Curve.prefab";
            GameObject root = PrefabUtility.LoadPrefabContents(chunkPrefabPath);
            if (root != null)
            {
                for (int i = root.transform.childCount - 1; i >= 0; i--)
                {
                    Transform c = root.transform.GetChild(i);
                    if (c.name.StartsWith("Vegetation_Container"))
                    {
                        Object.DestroyImmediate(c.gameObject);
                    }
                }
                var spawner = root.GetComponent<ChunkVegetationSpawner>();
                if (spawner != null)
                {
                    spawner.renderMode = ChunkVegetationSpawner.VegetationRenderMode.TerrainTrees;
                }
                PrefabUtility.SaveAsPrefabAsset(root, chunkPrefabPath);
                PrefabUtility.UnloadPrefabContents(root);
                Debug.Log("<color=green>[Purge] 'Chunk_Forest_Curve.prefab' içindeki tüm Vegetation_Container nesneleri silindi!</color>");
            }

            // 2. Purge from current scene chunks
            Chunk[] chunks = Object.FindObjectsByType<Chunk>(FindObjectsSortMode.None);
            foreach (var chunk in chunks)
            {
                for (int i = chunk.transform.childCount - 1; i >= 0; i--)
                {
                    Transform c = chunk.transform.GetChild(i);
                    if (c.name.StartsWith("Vegetation_Container"))
                    {
                        Object.DestroyImmediate(c.gameObject);
                    }
                }
                var spawner = chunk.GetComponent<ChunkVegetationSpawner>();
                if (spawner != null)
                {
                    spawner.renderMode = ChunkVegetationSpawner.VegetationRenderMode.TerrainTrees;
                    spawner.GenerateVegetation();
                }
                EditorUtility.SetDirty(chunk.gameObject);
            }
            AssetDatabase.SaveAssets();
            Debug.Log("<color=green>[Purge] Sahnedeki tüm chunk'lardan Vegetation_Container nesneleri tamamen temizlendi!</color>");
        }

        [MenuItem("Endless Survival/Setup Forest Vegetation (Trees & Environment)", false, 15)]
        [MenuItem("Tools/Endless Survival/Setup Forest Vegetation (Trees & Environment)", false, 15)]
        public static void SetupForestVegetation()
        {
            Debug.Log("<color=green>[VegetationSetup] Başlatılıyor: Tek Mesh Tabanlı Kübik Orman Varlıkları ve Terrain Entegrasyonu...</color>");

            string envFolder = "Assets/Prefabs/Environment";
            string treeFolder = "Assets/Prefabs/Environment/Trees";
            string meshFolder = "Assets/Prefabs/Environment/Meshes";
            string matFolder = "Assets/Materials/Environment";

            EnsureFolder("Assets/Prefabs");
            EnsureFolder(envFolder);
            EnsureFolder(treeFolder);
            EnsureFolder(meshFolder);
            EnsureFolder("Assets/Materials");
            EnsureFolder(matFolder);

            // 1. Materyalleri Hazırla (Universal Render Pipeline Lit + GPU Instancing)
            Material matWood = GetOrCreateURPMaterial($"{matFolder}/Mat_Tree_Wood.mat", new Color(0.28f, 0.18f, 0.11f), smoothness: 0.15f);
            Material matPineDark = GetOrCreateURPMaterial($"{matFolder}/Mat_Tree_Pine_Dark.mat", new Color(0.12f, 0.28f, 0.15f), smoothness: 0.20f);
            Material matPineLight = GetOrCreateURPMaterial($"{matFolder}/Mat_Tree_Pine_Light.mat", new Color(0.18f, 0.38f, 0.20f), smoothness: 0.20f);
            Material matOakFoliage = GetOrCreateURPMaterial($"{matFolder}/Mat_Tree_Oak_Foliage.mat", new Color(0.22f, 0.45f, 0.18f), smoothness: 0.20f);
            Material matOakAccent = GetOrCreateURPMaterial($"{matFolder}/Mat_Tree_Oak_Accent.mat", new Color(0.30f, 0.52f, 0.22f), smoothness: 0.20f);
            Material matDeadTree = GetOrCreateURPMaterial($"{matFolder}/Mat_Tree_Dead.mat", new Color(0.22f, 0.20f, 0.19f), smoothness: 0.10f);
            Material matRock = GetOrCreateURPMaterial($"{matFolder}/Mat_Forest_Rock.mat", new Color(0.35f, 0.36f, 0.38f), smoothness: 0.15f);
            Material matBush = GetOrCreateURPMaterial($"{matFolder}/Mat_Forest_Bush.mat", new Color(0.25f, 0.42f, 0.16f), smoothness: 0.15f);

            // 2. Prefabları Tek Mesh Halinde İnşa Et (Unity Terrain TreePrototype uyumlu!)
            GameObject pinePrefab = CreateOrUpdatePrefab($"{treeFolder}/Tree_Pine_Blocky.prefab", () =>
            {
                GameObject temp = BuildPineTreeRaw(matWood, matPineDark, matPineLight);
                CollapseToSingleMesh(temp, $"{meshFolder}/Mesh_Tree_Pine.asset", hasCollider: true);
                return temp;
            });

            GameObject oakPrefab = CreateOrUpdatePrefab($"{treeFolder}/Tree_Oak_Blocky.prefab", () =>
            {
                GameObject temp = BuildOakTreeRaw(matWood, matOakFoliage, matOakAccent);
                CollapseToSingleMesh(temp, $"{meshFolder}/Mesh_Tree_Oak.asset", hasCollider: true);
                return temp;
            });

            GameObject deadPrefab = CreateOrUpdatePrefab($"{treeFolder}/Tree_Dead_Blocky.prefab", () =>
            {
                GameObject temp = BuildDeadTreeRaw(matDeadTree);
                CollapseToSingleMesh(temp, $"{meshFolder}/Mesh_Tree_Dead.asset", hasCollider: true);
                return temp;
            });

            GameObject rockPrefab = CreateOrUpdatePrefab($"{treeFolder}/Rock_Forest_Blocky.prefab", () =>
            {
                GameObject temp = BuildForestRockRaw(matRock);
                CollapseToSingleMesh(temp, $"{meshFolder}/Mesh_Rock_Forest.asset", hasCollider: true);
                return temp;
            });

            GameObject bushPrefab = CreateOrUpdatePrefab($"{treeFolder}/Bush_Forest_Blocky.prefab", () =>
            {
                GameObject temp = BuildForestBushRaw(matBush);
                CollapseToSingleMesh(temp, $"{meshFolder}/Mesh_Bush_Forest.asset", hasCollider: true);
                return temp;
            });

            // 3. Chunk_Forest_Curve Prefabını Güncelle
            string chunkPrefabPath = "Assets/Prefabs/Chunks/Chunk_Forest_Curve.prefab";
            GameObject chunkPrefabContents = PrefabUtility.LoadPrefabContents(chunkPrefabPath);
            if (chunkPrefabContents != null)
            {
                var spawner = chunkPrefabContents.GetComponent<ChunkVegetationSpawner>();
                if (spawner == null) spawner = chunkPrefabContents.AddComponent<ChunkVegetationSpawner>();

                spawner.targetBiome = ChunkBiomeType.Forest;
                spawner.renderMode = ChunkVegetationSpawner.VegetationRenderMode.TerrainTrees;
                spawner.treePrefabs = new GameObject[] { pinePrefab, oakPrefab, deadPrefab };
                spawner.detailPrefabs = new GameObject[] { bushPrefab, rockPrefab };
                spawner.minTrees = 200;
                spawner.maxTrees = 340;
                spawner.minDetails = 60;
                spawner.maxDetails = 120;
                spawner.minRoadDistance = 15f;
                spawner.maxRoadDistance = 238f;
                spawner.minTreeSpacing = 3.6f;
                spawner.minScale = 1.8f;
                spawner.maxScale = 3.2f;

                // Terrain bileşenini hazırla
                Terrain terrain = chunkPrefabContents.GetComponentInChildren<Terrain>();
                if (terrain != null && terrain.terrainData != null)
                {
                    terrain.drawTreesAndFoliage = true;
                    terrain.treeDistance = 2000f;
                    terrain.treeBillboardDistance = 2000f;
                    terrain.treeCrossFadeLength = 0f;
                    terrain.treeMaximumFullLODCount = 2000;
                    terrain.Flush();
                }

                // Completely destroy ANY and ALL Vegetation_Container GameObjects from prefab
                for (int i = chunkPrefabContents.transform.childCount - 1; i >= 0; i--)
                {
                    Transform child = chunkPrefabContents.transform.GetChild(i);
                    if (child.name.StartsWith("Vegetation_Container"))
                    {
                        Object.DestroyImmediate(child.gameObject);
                    }
                }

                spawner.GenerateVegetation();

                // Re-verify that no container was left behind
                for (int i = chunkPrefabContents.transform.childCount - 1; i >= 0; i--)
                {
                    Transform child = chunkPrefabContents.transform.GetChild(i);
                    if (child.name.StartsWith("Vegetation_Container"))
                    {
                        Object.DestroyImmediate(child.gameObject);
                    }
                }

                PrefabUtility.SaveAsPrefabAsset(chunkPrefabContents, chunkPrefabPath);
                PrefabUtility.UnloadPrefabContents(chunkPrefabContents);
                Debug.Log($"<color=green>[VegetationSetup] '{chunkPrefabPath}' Vegetation_Container tamamen silindi, her şey Terrain motoruna taşındı!</color>");
            }

            // 4. Sahnede aktif chunk veya ChunkManager varsa güncelle
            UpdateSceneChunks(pinePrefab, oakPrefab, deadPrefab, bushPrefab, rockPrefab);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log("<color=green>SUCCESS: [VegetationSetup] Tüm ağaçlar ve Terrain sistemi başarıyla kuruldu!</color>");
        }

        private static void UpdateSceneChunks(GameObject pine, GameObject oak, GameObject dead, GameObject bush, GameObject rock)
        {
            Chunk[] sceneChunks = Object.FindObjectsByType<Chunk>(FindObjectsSortMode.None);
            if (sceneChunks.Length == 0)
            {
                var mgr = Object.FindAnyObjectByType<ChunkManager>();
                if (mgr != null)
                {
                    mgr.SpawnEditorPreviewChunks(2);
                    sceneChunks = Object.FindObjectsByType<Chunk>(FindObjectsSortMode.None);
                }
            }

            foreach (var chunk in sceneChunks)
            {
                if (chunk.biomeType == ChunkBiomeType.Forest)
                {
                    var spawner = chunk.GetComponent<ChunkVegetationSpawner>();
                    if (spawner == null) spawner = chunk.gameObject.AddComponent<ChunkVegetationSpawner>();

                    spawner.targetBiome = ChunkBiomeType.Forest;
                    spawner.renderMode = ChunkVegetationSpawner.VegetationRenderMode.TerrainTrees;
                    spawner.treePrefabs = new GameObject[] { pine, oak, dead };
                    spawner.detailPrefabs = new GameObject[] { bush, rock };

                    Terrain terrain = chunk.GetComponentInChildren<Terrain>();
                    if (terrain != null)
                    {
                        terrain.drawTreesAndFoliage = true;
                        terrain.treeDistance = 2000f;
                        terrain.treeBillboardDistance = 2000f;
                        terrain.treeCrossFadeLength = 0f;
                        terrain.treeMaximumFullLODCount = 2000;
                        terrain.Flush();
                    }
                    // Completely destroy all Vegetation_Container GameObjects on scene chunk
                    for (int i = chunk.transform.childCount - 1; i >= 0; i--)
                    {
                        Transform c = chunk.transform.GetChild(i);
                        if (c.name.StartsWith("Vegetation_Container"))
                        {
                            Object.DestroyImmediate(c.gameObject);
                        }
                    }

                    spawner.GenerateVegetation();

                    for (int i = chunk.transform.childCount - 1; i >= 0; i--)
                    {
                        Transform c = chunk.transform.GetChild(i);
                        if (c.name.StartsWith("Vegetation_Container"))
                        {
                            Object.DestroyImmediate(c.gameObject);
                        }
                    }
                    chunk.GenerateEditorPreview(chunk.ChunkSeed);
                    EditorUtility.SetDirty(chunk.gameObject);
                    if (chunk.gameObject.scene.IsValid())
                    {
                        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(chunk.gameObject.scene);
                    }
                }
            }
        }

        // ==========================================
        // MESH COMBINER (UNITY TERRAIN UYUMLULUK)
        // ==========================================

        private static void CollapseToSingleMesh(GameObject root, string meshAssetPath, bool hasCollider)
        {
            MeshFilter[] filters = root.GetComponentsInChildren<MeshFilter>();
            if (filters.Length == 0) return;

            // Group combine instances by material
            Dictionary<Material, List<CombineInstance>> materialGroups = new Dictionary<Material, List<CombineInstance>>();

            foreach (var mf in filters)
            {
                if (mf.sharedMesh == null) continue;
                var mr = mf.GetComponent<MeshRenderer>();
                if (mr == null || mr.sharedMaterial == null) continue;

                Material mat = mr.sharedMaterial;
                if (!materialGroups.ContainsKey(mat))
                {
                    materialGroups[mat] = new List<CombineInstance>();
                }

                CombineInstance ci = new CombineInstance();
                ci.mesh = mf.sharedMesh;
                ci.transform = root.transform.worldToLocalMatrix * mf.transform.localToWorldMatrix;
                materialGroups[mat].Add(ci);
            }

            List<Material> finalMaterials = new List<Material>();
            List<CombineInstance> submeshCombines = new List<CombineInstance>();

            foreach (var kvp in materialGroups)
            {
                Mesh subMesh = new Mesh();
                subMesh.CombineMeshes(kvp.Value.ToArray(), true, true);

                CombineInstance ci = new CombineInstance();
                ci.mesh = subMesh;
                ci.transform = Matrix4x4.identity;
                submeshCombines.Add(ci);
                finalMaterials.Add(kvp.Key);
            }

            Mesh combinedMesh = new Mesh();
            combinedMesh.name = Path.GetFileNameWithoutExtension(meshAssetPath);
            combinedMesh.CombineMeshes(submeshCombines.ToArray(), false, false);
            combinedMesh.RecalculateBounds();
            combinedMesh.RecalculateNormals();

            if (AssetDatabase.LoadAssetAtPath<Mesh>(meshAssetPath) != null)
            {
                AssetDatabase.DeleteAsset(meshAssetPath);
            }
            AssetDatabase.CreateAsset(combinedMesh, meshAssetPath);
            AssetDatabase.SaveAssets();

            Mesh savedMesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshAssetPath);

            // Remove all temporary children
            while (root.transform.childCount > 0)
            {
                Object.DestroyImmediate(root.transform.GetChild(0).gameObject);
            }

            // Attach single combined mesh to root
            MeshFilter rootFilter = root.GetComponent<MeshFilter>();
            if (rootFilter == null) rootFilter = root.AddComponent<MeshFilter>();
            rootFilter.sharedMesh = savedMesh;

            MeshRenderer rootRenderer = root.GetComponent<MeshRenderer>();
            if (rootRenderer == null) rootRenderer = root.AddComponent<MeshRenderer>();
            rootRenderer.sharedMaterials = finalMaterials.ToArray();

            // CRITICAL FOR UNITY URP TERRAIN ENGINE:
            // Without a root LODGroup and valid bounds, Unity rejects custom meshes in URP and tries to use the legacy
            // Built-in RP 'Nature/Soft Occlusion' shader, throwing warnings and making trees invisible!
            LODGroup lodGroup = root.GetComponent<LODGroup>();
            if (lodGroup == null) lodGroup = root.AddComponent<LODGroup>();
            LOD[] lods = new LOD[1];
            lods[0] = new LOD(0.01f, new Renderer[] { rootRenderer });
            lodGroup.SetLODs(lods);
            lodGroup.localReferencePoint = savedMesh.bounds.center;
            lodGroup.size = Mathf.Max(1f, savedMesh.bounds.size.magnitude);
            lodGroup.RecalculateBounds();

            // Configure tight physical colliders strictly on tree trunks and rock bodies
            ConfigurePrefabColliders(root, root.name, hasCollider);
        }

        private static void ConfigurePrefabColliders(GameObject root, string name, bool hasCollider)
        {
            // Remove any old colliders
            var colliders = root.GetComponentsInChildren<Collider>(true);
            for (int i = colliders.Length - 1; i >= 0; i--)
            {
                Object.DestroyImmediate(colliders[i]);
            }

            if (!hasCollider) return;

            // CRITICAL FOR UNITY TERRAIN ENGINE:
            // Unity Terrain requires a Collider to compute bounds for custom tree prototypes.
            // By setting isTrigger = true, bounds are computed cleanly with ZERO physical collision!
            if (name.Contains("Pine"))
            {
                CapsuleCollider col = root.AddComponent<CapsuleCollider>();
                col.center = new Vector3(0f, 2.0f, 0f);
                col.radius = 0.45f;
                col.height = 4.0f;
                col.direction = 1;
                col.isTrigger = true;
            }
            else if (name.Contains("Oak"))
            {
                CapsuleCollider col = root.AddComponent<CapsuleCollider>();
                col.center = new Vector3(0f, 2.1f, 0f);
                col.radius = 0.50f;
                col.height = 4.2f;
                col.direction = 1;
                col.isTrigger = true;
            }
            else if (name.Contains("Dead"))
            {
                CapsuleCollider col = root.AddComponent<CapsuleCollider>();
                col.center = new Vector3(0f, 2.4f, 0f);
                col.radius = 0.40f;
                col.height = 4.8f;
                col.direction = 1;
                col.isTrigger = true;
            }
            else if (name.Contains("Rock"))
            {
                CapsuleCollider col = root.AddComponent<CapsuleCollider>();
                col.center = new Vector3(0.28f, 1.0f, 0f);
                col.radius = 0.75f;
                col.height = 2.2f;
                col.direction = 1;
                col.isTrigger = true;
            }
            else if (name.Contains("Bush"))
            {
                CapsuleCollider col = root.AddComponent<CapsuleCollider>();
                col.center = new Vector3(0.24f, 0.65f, 0.09f);
                col.radius = 0.5f;
                col.height = 1.4f;
                col.direction = 1;
                col.isTrigger = true;
            }
        }

        // ==========================================
        // GEOMETRİ OLUŞTURUCULARI (HAM ÇİZİMLER)
        // ==========================================

        private static GameObject BuildPineTreeRaw(Material woodMat, Material darkFoliage, Material lightFoliage)
        {
            GameObject root = new GameObject("Tree_Pine_Blocky");

            // Gövde
            GameObject trunk = GameObject.CreatePrimitive(PrimitiveType.Cube);
            trunk.name = "Trunk";
            trunk.transform.SetParent(root.transform, false);
            trunk.transform.localPosition = new Vector3(0f, 2.0f, 0f);
            trunk.transform.localScale = new Vector3(0.9f, 4.0f, 0.9f);
            trunk.GetComponent<Renderer>().sharedMaterial = woodMat;

            // Katman 1
            CreateFoliageBlock(root.transform, "Foliage_Tier1", new Vector3(0f, 3.2f, 0f), new Vector3(3.6f, 1.4f, 3.6f), Quaternion.identity, darkFoliage);

            // Katman 2
            CreateFoliageBlock(root.transform, "Foliage_Tier2", new Vector3(0f, 4.4f, 0f), new Vector3(2.7f, 1.3f, 2.7f), Quaternion.Euler(0f, 15f, 0f), darkFoliage);

            // Katman 3
            CreateFoliageBlock(root.transform, "Foliage_Tier3", new Vector3(0f, 5.5f, 0f), new Vector3(1.8f, 1.2f, 1.8f), Quaternion.Euler(0f, 30f, 0f), lightFoliage);

            // Tepe Ucu
            CreateFoliageBlock(root.transform, "Foliage_Spire", new Vector3(0f, 6.4f, 0f), new Vector3(0.9f, 1.0f, 0.9f), Quaternion.Euler(0f, 45f, 0f), lightFoliage);

            return root;
        }

        private static GameObject BuildOakTreeRaw(Material woodMat, Material foliageMat, Material accentMat)
        {
            GameObject root = new GameObject("Tree_Oak_Blocky");

            // Gövde
            GameObject trunk = GameObject.CreatePrimitive(PrimitiveType.Cube);
            trunk.name = "Trunk";
            trunk.transform.SetParent(root.transform, false);
            trunk.transform.localPosition = new Vector3(0f, 2.2f, 0f);
            trunk.transform.localScale = new Vector3(1.1f, 4.4f, 1.1f);
            trunk.GetComponent<Renderer>().sharedMaterial = woodMat;

            // Merkez Yaprak Tacı
            CreateFoliageBlock(root.transform, "Canopy_Center", new Vector3(0f, 5.2f, 0f), new Vector3(3.8f, 2.6f, 3.8f), Quaternion.identity, foliageMat);

            // Sol Asimetrik Çıkıntı Yaprak
            CreateFoliageBlock(root.transform, "Canopy_Left", new Vector3(-1.3f, 4.8f, 0.6f), new Vector3(2.4f, 2.1f, 2.4f), Quaternion.Euler(0f, 12f, 0f), accentMat);

            // Sağ Asimetrik Çıkıntı Yaprak
            CreateFoliageBlock(root.transform, "Canopy_Right", new Vector3(1.3f, 5.0f, -0.7f), new Vector3(2.3f, 2.2f, 2.3f), Quaternion.Euler(0f, -15f, 0f), foliageMat);

            // Üst Yaprak Kubbesi
            CreateFoliageBlock(root.transform, "Canopy_Top", new Vector3(0.1f, 6.5f, 0.1f), new Vector3(2.5f, 1.6f, 2.5f), Quaternion.Euler(0f, 25f, 0f), accentMat);

            return root;
        }

        private static GameObject BuildDeadTreeRaw(Material deadMat)
        {
            GameObject root = new GameObject("Tree_Dead_Blocky");

            GameObject trunk = GameObject.CreatePrimitive(PrimitiveType.Cube);
            trunk.name = "Trunk";
            trunk.transform.SetParent(root.transform, false);
            trunk.transform.localPosition = new Vector3(0f, 2.4f, 0f);
            trunk.transform.localRotation = Quaternion.Euler(3f, 10f, -2f);
            trunk.transform.localScale = new Vector3(0.8f, 4.8f, 0.8f);
            trunk.GetComponent<Renderer>().sharedMaterial = deadMat;

            GameObject branch1 = GameObject.CreatePrimitive(PrimitiveType.Cube);
            branch1.name = "Branch_Left";
            branch1.transform.SetParent(root.transform, false);
            branch1.transform.localPosition = new Vector3(1.0f, 3.8f, 0.2f);
            branch1.transform.localRotation = Quaternion.Euler(15f, 25f, 35f);
            branch1.transform.localScale = new Vector3(1.8f, 0.4f, 0.4f);
            branch1.GetComponent<Renderer>().sharedMaterial = deadMat;

            GameObject branch2 = GameObject.CreatePrimitive(PrimitiveType.Cube);
            branch2.name = "Branch_Right";
            branch2.transform.SetParent(root.transform, false);
            branch2.transform.localPosition = new Vector3(-0.8f, 4.4f, -0.5f);
            branch2.transform.localRotation = Quaternion.Euler(-20f, -30f, -40f);
            branch2.transform.localScale = new Vector3(1.5f, 0.35f, 0.35f);
            branch2.GetComponent<Renderer>().sharedMaterial = deadMat;

            return root;
        }

        private static GameObject BuildForestRockRaw(Material rockMat)
        {
            GameObject root = new GameObject("Rock_Forest_Blocky");

            GameObject mainRock = GameObject.CreatePrimitive(PrimitiveType.Cube);
            mainRock.name = "Rock_Base";
            mainRock.transform.SetParent(root.transform, false);
            mainRock.transform.localPosition = new Vector3(0f, 0.8f, 0f);
            mainRock.transform.localRotation = Quaternion.Euler(8f, 25f, -6f);
            mainRock.transform.localScale = new Vector3(2.2f, 1.6f, 1.9f);
            mainRock.GetComponent<Renderer>().sharedMaterial = rockMat;

            GameObject subRock = GameObject.CreatePrimitive(PrimitiveType.Cube);
            subRock.name = "Rock_Side";
            subRock.transform.SetParent(root.transform, false);
            subRock.transform.localPosition = new Vector3(1.0f, 0.55f, -0.4f);
            subRock.transform.localRotation = Quaternion.Euler(-12f, 50f, 15f);
            subRock.transform.localScale = new Vector3(1.3f, 1.1f, 1.4f);
            subRock.GetComponent<Renderer>().sharedMaterial = rockMat;

            return root;
        }

        private static GameObject BuildForestBushRaw(Material bushMat)
        {
            GameObject root = new GameObject("Bush_Forest_Blocky");

            GameObject mainBush = GameObject.CreatePrimitive(PrimitiveType.Cube);
            mainBush.name = "Bush_Core";
            mainBush.transform.SetParent(root.transform, false);
            mainBush.transform.localPosition = new Vector3(0f, 0.65f, 0f);
            mainBush.transform.localRotation = Quaternion.Euler(0f, 18f, 0f);
            mainBush.transform.localScale = new Vector3(1.5f, 1.1f, 1.5f);
            mainBush.GetComponent<Renderer>().sharedMaterial = bushMat;

            GameObject subBush = GameObject.CreatePrimitive(PrimitiveType.Cube);
            subBush.name = "Bush_Side";
            subBush.transform.SetParent(root.transform, false);
            subBush.transform.localPosition = new Vector3(0.7f, 0.45f, 0.4f);
            subBush.transform.localRotation = Quaternion.Euler(0f, -25f, 0f);
            subBush.transform.localScale = new Vector3(1.1f, 0.85f, 1.1f);
            subBush.GetComponent<Renderer>().sharedMaterial = bushMat;

            return root;
        }

        private static void CreateFoliageBlock(Transform parent, string name, Vector3 pos, Vector3 scale, Quaternion rot, Material mat)
        {
            GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = name;
            cube.transform.SetParent(parent, false);
            cube.transform.localPosition = pos;
            cube.transform.localRotation = rot;
            cube.transform.localScale = scale;
            cube.GetComponent<Renderer>().sharedMaterial = mat;
        }

        // ==========================================
        // YARDIMCI METODLAR
        // ==========================================

        private static GameObject CreateOrUpdatePrefab(string prefabPath, System.Func<GameObject> buildFunc)
        {
            GameObject root = buildFunc();
            GameObject savedPrefab = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            Object.DestroyImmediate(root);
            return savedPrefab;
        }

        private static Material GetOrCreateURPMaterial(string matPath, Color color, float smoothness = 0.2f)
        {
            Material existingMat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            Shader urpShader = Shader.Find("Universal Render Pipeline/Lit");
            if (urpShader == null) urpShader = Shader.Find("Standard");

            if (existingMat == null)
            {
                existingMat = new Material(urpShader);
                existingMat.color = color;
                existingMat.enableInstancing = true;
                if (existingMat.HasProperty("_BaseColor")) existingMat.SetColor("_BaseColor", color);
                if (existingMat.HasProperty("_Smoothness")) existingMat.SetFloat("_Smoothness", smoothness);
                AssetDatabase.CreateAsset(existingMat, matPath);
            }
            else
            {
                existingMat.shader = urpShader;
                existingMat.color = color;
                existingMat.enableInstancing = true;
                if (existingMat.HasProperty("_BaseColor")) existingMat.SetColor("_BaseColor", color);
                if (existingMat.HasProperty("_Smoothness")) existingMat.SetFloat("_Smoothness", smoothness);
                EditorUtility.SetDirty(existingMat);
            }
            return existingMat;
        }

        private static void EnsureFolder(string path)
        {
            if (!AssetDatabase.IsValidFolder(path))
            {
                int lastSlash = path.LastIndexOf('/');
                if (lastSlash > 0)
                {
                    string parent = path.Substring(0, lastSlash);
                    string child = path.Substring(lastSlash + 1);
                    EnsureFolder(parent);
                    AssetDatabase.CreateFolder(parent, child);
                }
            }
        }
    }
}
#endif
