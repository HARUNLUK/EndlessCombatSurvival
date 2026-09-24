#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;
using EndlessSurvival.World.Road;

namespace EndlessSurvival.World.Editor
{
    /// <summary>
    /// Editor utility for generating and saving categorized 500x500 chunk prefabs
    /// with procedural terrains, textures, materials, and baked spline roads.
    /// Kept completely isolated from runtime gameplay code for modularity.
    /// </summary>
    public static class ChunkPrefabGenerator
    {
        [MenuItem("Endless Survival/Generate All Chunk Prefabs")]
        public static void MenuItemGenerateAllChunkPrefabs()
        {
            GenerateAndSaveChunkPrefabs();
        }

        public static void GenerateAndSaveChunkPrefabs()
        {
            Debug.Log("[ChunkPrefabGenerator] Generating categorized URP 500x500 chunk prefabs with Spline roads...");

            string chunkFolderPath = "Assets/Prefabs/Chunks";
            string texFolderPath = "Assets/Prefabs/Chunks/Textures";
            string tlFolderPath = "Assets/Prefabs/Chunks/TerrainLayers";
            string tdFolderPath = "Assets/Prefabs/Chunks/TerrainData";
            string matFolderPath = "Assets/Prefabs/Chunks/Materials";
            string meshFolderPath = "Assets/Prefabs/Chunks/Meshes";
            string profileFolderPath = "Assets/Settings/RoadProfiles";

            EnsureFolder("Assets/Prefabs");
            EnsureFolder(chunkFolderPath);
            EnsureFolder(texFolderPath);
            EnsureFolder(tlFolderPath);
            EnsureFolder(tdFolderPath);
            EnsureFolder(matFolderPath);
            EnsureFolder(meshFolderPath);
            EnsureFolder("Assets/Settings");
            EnsureFolder(profileFolderPath);

            // 1. Prepare persistent solid ground textures on disk
            Texture2D texForest = GetOrCreateSolidTexture(texFolderPath, "Tex_Terrain_Forest", new Color(0.24f, 0.40f, 0.20f));
            Texture2D texDesert = GetOrCreateSolidTexture(texFolderPath, "Tex_Terrain_Desert", new Color(0.72f, 0.62f, 0.42f));
            Texture2D texCity = GetOrCreateSolidTexture(texFolderPath, "Tex_Terrain_RuinedCity", new Color(0.34f, 0.34f, 0.36f));
            Texture2D texWasteland = GetOrCreateSolidTexture(texFolderPath, "Tex_Terrain_Wasteland", new Color(0.38f, 0.30f, 0.22f));

            // 2. Prepare persistent TerrainLayer assets
            TerrainLayer layerForest = GetOrCreateTerrainLayer(tlFolderPath, "Layer_Forest", texForest);
            TerrainLayer layerDesert = GetOrCreateTerrainLayer(tlFolderPath, "Layer_Desert", texDesert);
            TerrainLayer layerCity = GetOrCreateTerrainLayer(tlFolderPath, "Layer_RuinedCity", texCity);
            TerrainLayer layerWasteland = GetOrCreateTerrainLayer(tlFolderPath, "Layer_Wasteland", texWasteland);

            // 3. Prepare road and blockade materials
            Material roadMat = GetOrCreateURPMaterial(matFolderPath, "Mat_Road_Dark", new Color(0.18f, 0.18f, 0.18f));
            Material forestMat = GetOrCreateURPMaterial(matFolderPath, "Mat_Chunk_Forest", new Color(0.24f, 0.42f, 0.22f));
            Material desertMat = GetOrCreateURPMaterial(matFolderPath, "Mat_Chunk_Desert", new Color(0.62f, 0.52f, 0.32f));
            Material cityMat = GetOrCreateURPMaterial(matFolderPath, "Mat_Chunk_RuinedCity", new Color(0.32f, 0.32f, 0.36f));
            Material wastelandMat = GetOrCreateURPMaterial(matFolderPath, "Mat_Chunk_Wasteland", new Color(0.42f, 0.35f, 0.26f));
            Material blockadeMat = GetOrCreateURPMaterial(matFolderPath, "Mat_Chunk_Blockade", new Color(0.7f, 0.18f, 0.18f));

            // 4. Prepare Parametric Road Profiles per Biome
            RoadProfile profileForest = GetOrCreateRoadProfile(profileFolderPath, "RoadProfile_Forest", ChunkBiomeType.Forest, 12f, 2.5f, roadMat, forestMat, false);
            RoadProfile profileDesert = GetOrCreateRoadProfile(profileFolderPath, "RoadProfile_Desert", ChunkBiomeType.Desert, 14f, 3.5f, roadMat, desertMat, false);
            RoadProfile profileCity = GetOrCreateRoadProfile(profileFolderPath, "RoadProfile_RuinedCity", ChunkBiomeType.RuinedCity, 14f, 2.0f, roadMat, cityMat, false);
            RoadProfile profileWasteland = GetOrCreateRoadProfile(profileFolderPath, "RoadProfile_Wasteland", ChunkBiomeType.Wasteland, 10f, 4.0f, roadMat, wastelandMat, false);

            // 5. Generate Chunk Prefabs for each distinct Road Type
            Chunk forestStraight = CreateChunkGameObject("Chunk_Forest_Straight", ChunkBiomeType.Forest, ChunkRoadType.Straight, layerForest, profileForest, blockadeMat, 10);
            SaveChunkAsPrefab(forestStraight, chunkFolderPath);

            Chunk forestCurveRight = CreateChunkGameObject("Chunk_Forest_CurveRight", ChunkBiomeType.Forest, ChunkRoadType.CurvedRight, layerForest, profileForest, blockadeMat, 10);
            SaveChunkAsPrefab(forestCurveRight, chunkFolderPath);

            Chunk forestCurveLeft = CreateChunkGameObject("Chunk_Forest_CurveLeft", ChunkBiomeType.Forest, ChunkRoadType.CurvedLeft, layerForest, profileForest, blockadeMat, 10);
            SaveChunkAsPrefab(forestCurveLeft, chunkFolderPath);

            Chunk forestHazard = CreateChunkGameObject("Chunk_Forest_Hazard", ChunkBiomeType.Forest, ChunkRoadType.HazardZone, layerForest, profileForest, blockadeMat, 10);
            SaveChunkAsPrefab(forestHazard, chunkFolderPath);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log("[ChunkPrefabGenerator] Successfully created 4 categorized chunk prefabs with distinct road types!");
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

        private static Texture2D GetOrCreateSolidTexture(string folder, string name, Color color)
        {
            string path = $"{folder}/{name}.png";
            Texture2D tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (tex != null) return tex;

            tex = new Texture2D(64, 64, TextureFormat.RGBA32, true);
            Color[] pixels = new Color[64 * 64];
            for (int y = 0; y < 64; y++)
            {
                for (int x = 0; x < 64; x++)
                {
                    float noise = Mathf.PerlinNoise(x * 0.15f, y * 0.15f) * 0.12f - 0.06f;
                    pixels[y * 64 + x] = new Color(
                        Mathf.Clamp01(color.r + noise),
                        Mathf.Clamp01(color.g + noise),
                        Mathf.Clamp01(color.b + noise),
                        1f
                    );
                }
            }
            tex.SetPixels(pixels);
            tex.Apply();

            byte[] bytes = tex.EncodeToPNG();
            File.WriteAllBytes(path, bytes);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);

            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer != null)
            {
                importer.wrapMode = TextureWrapMode.Repeat;
                importer.filterMode = FilterMode.Bilinear;
                importer.SaveAndReimport();
            }

            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        private static TerrainLayer GetOrCreateTerrainLayer(string folder, string name, Texture2D diffuseTex)
        {
            string path = $"{folder}/{name}.terrainlayer";
            TerrainLayer layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(path);
            if (layer == null)
            {
                layer = new TerrainLayer();
                layer.diffuseTexture = diffuseTex;
                layer.tileSize = new Vector2(12f, 12f);
                layer.smoothness = 0.05f;
                layer.specular = Color.black;
                AssetDatabase.CreateAsset(layer, path);
            }
            else
            {
                layer.diffuseTexture = diffuseTex;
                layer.tileSize = new Vector2(12f, 12f);
                EditorUtility.SetDirty(layer);
            }
            return layer;
        }

        private static Material GetOrCreateTerrainMaterial(string folder, string name)
        {
            string path = $"{folder}/{name}.mat";
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            Shader terrainShader = Shader.Find("Universal Render Pipeline/Terrain/Lit");
            if (terrainShader == null) terrainShader = Shader.Find("Universal Render Pipeline/Lit");
            if (terrainShader == null) terrainShader = Shader.Find("Standard");

            if (mat == null)
            {
                mat = new Material(terrainShader);
                AssetDatabase.CreateAsset(mat, path);
            }
            else
            {
                mat.shader = terrainShader;
                EditorUtility.SetDirty(mat);
            }
            return mat;
        }

        private static RoadProfile GetOrCreateRoadProfile(string folderPath, string profileName, ChunkBiomeType biome, float roadW, float shoulderW, Material roadMat, Material shoulderMat, bool guardrails)
        {
            string path = $"{folderPath}/{profileName}.asset";
            RoadProfile profile = AssetDatabase.LoadAssetAtPath<RoadProfile>(path);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<RoadProfile>();
                profile.targetBiome = biome;
                profile.roadWidth = roadW;
                profile.shoulderWidth = shoulderW;
                profile.roadMaterial = roadMat;
                profile.shoulderMaterial = shoulderMat;
                profile.hasGuardrails = guardrails;
                AssetDatabase.CreateAsset(profile, path);
            }
            else
            {
                profile.targetBiome = biome;
                profile.roadWidth = roadW;
                profile.shoulderWidth = shoulderW;
                profile.roadMaterial = roadMat;
                profile.shoulderMaterial = shoulderMat;
                profile.hasGuardrails = guardrails;
                EditorUtility.SetDirty(profile);
            }
            return profile;
        }

        private static Material GetOrCreateURPMaterial(string folderPath, string matName, Color color)
        {
            string matPath = $"{folderPath}/{matName}.mat";
            Material existingMat = AssetDatabase.LoadAssetAtPath<Material>(matPath);

            Shader urpShader = Shader.Find("Universal Render Pipeline/Lit");
            if (urpShader == null)
            {
                Material sampleURP = AssetDatabase.LoadAssetAtPath<Material>("Assets/Pack_Adventure/Materials/Ground.mat");
                if (sampleURP != null) urpShader = sampleURP.shader;
            }
            if (urpShader == null) urpShader = Shader.Find("Standard");

            if (existingMat == null)
            {
                existingMat = new Material(urpShader);
                if (existingMat.HasProperty("_BaseColor"))
                    existingMat.SetColor("_BaseColor", color);
                existingMat.color = color;

                AssetDatabase.CreateAsset(existingMat, matPath);
            }
            else
            {
                existingMat.shader = urpShader;
                if (existingMat.HasProperty("_BaseColor"))
                    existingMat.SetColor("_BaseColor", color);
                existingMat.color = color;
                EditorUtility.SetDirty(existingMat);
            }

            return existingMat;
        }

        private static GameObject CreateChunkTerrain(string chunkName, Transform parent, TerrainLayer terrainLayer)
        {
            string tdPath = $"Assets/Prefabs/Chunks/TerrainData/TerrainData_{chunkName}.asset";
            if (AssetDatabase.LoadAssetAtPath<TerrainData>(tdPath) != null)
            {
                AssetDatabase.DeleteAsset(tdPath);
            }

            TerrainData td = new TerrainData();
            td.heightmapResolution = 129;
            td.baseMapResolution = 256;
            td.size = new Vector3(500f, 60f, 500f);

            td.terrainLayers = new TerrainLayer[] { terrainLayer };

            int alphamapWidth = td.alphamapWidth;
            int alphamapHeight = td.alphamapHeight;
            float[,,] alphas = new float[alphamapHeight, alphamapWidth, 1];
            for (int y = 0; y < alphamapHeight; y++)
            {
                for (int x = 0; x < alphamapWidth; x++)
                {
                    alphas[y, x, 0] = 1f;
                }
            }
            td.SetAlphamaps(0, 0, alphas);

            AssetDatabase.CreateAsset(td, tdPath);
            AssetDatabase.SaveAssets();

            GameObject terrainGo = Terrain.CreateTerrainGameObject(td);
            terrainGo.name = "Terrain_500x500";
            terrainGo.transform.SetParent(parent, false);
            terrainGo.transform.localPosition = new Vector3(-250f, 0f, 0f);

            Terrain terrain = terrainGo.GetComponent<Terrain>();
            if (terrain != null)
            {
                Material savedTerrainMat = GetOrCreateTerrainMaterial("Assets/Prefabs/Chunks/Materials", $"Mat_Terrain_{chunkName}");
                terrain.materialTemplate = savedTerrainMat;
                terrain.drawHeightmap = true;
                terrain.heightmapPixelError = 1;
            }

            return terrainGo;
        }

        private static Chunk CreateChunkGameObject(string name, ChunkBiomeType biome, ChunkRoadType roadType, TerrainLayer terrainLayer, RoadProfile roadProfile, Material blockadeMat, int weight)
        {
            GameObject chunkGo = new GameObject(name);
            Chunk chunk = chunkGo.AddComponent<Chunk>();
            chunk.chunkLength = 500f;
            chunk.biomeType = biome;
            chunk.roadType = roadType;
            chunk.spawnWeight = weight;

            // 500x500 Unity Terrain
            CreateChunkTerrain(name, chunkGo.transform, terrainLayer);

            // Sockets
            GameObject entry = new GameObject("EntrySocket");
            entry.transform.SetParent(chunkGo.transform);
            entry.transform.localPosition = Vector3.zero;
            chunk.entrySocket = entry.transform;

            GameObject exit = new GameObject("ExitSocket");
            exit.transform.SetParent(chunkGo.transform);
            exit.transform.localPosition = new Vector3(0f, 0f, 500f);
            chunk.exitSocket = exit.transform;

            // Procedural Dynamic Spline Road
            GameObject roadGo = new GameObject("Spline_Road");
            roadGo.transform.SetParent(chunkGo.transform, false);

            RoadSpline spline = roadGo.AddComponent<RoadSpline>();
            spline.resolution = 100;
            if (roadType == ChunkRoadType.CurvedRight)
            {
                spline.SetSCurvePreset(45f);
            }
            else if (roadType == ChunkRoadType.CurvedLeft)
            {
                spline.SetSCurvePreset(-45f);
            }
            else if (roadType == ChunkRoadType.HazardZone)
            {
                spline.SetChicanePreset(35f);
            }
            else
            {
                spline.SetStraightPreset();
            }

            RoadGenerator roadGen = roadGo.AddComponent<RoadGenerator>();
            roadGen.spline = spline;
            roadGen.activeProfile = roadProfile;
            roadGen.roadElevation = 0.45f;
            roadGen.roadThickness = 0.80f;
            roadGen.syncExitSocket = true;
            roadGen.generateOnStart = true;
            Mesh generatedMesh = roadGen.BuildRoadMesh();

            string meshFolder = "Assets/Prefabs/Chunks/Meshes";
            string meshPath = $"{meshFolder}/Mesh_{name}.asset";
            if (AssetDatabase.LoadAssetAtPath<Mesh>(meshPath) != null)
            {
                AssetDatabase.DeleteAsset(meshPath);
            }
            AssetDatabase.CreateAsset(generatedMesh, meshPath);
            AssetDatabase.SaveAssets();
            Mesh savedMeshAsset = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
            roadGo.GetComponent<MeshFilter>().sharedMesh = savedMeshAsset;
            roadGo.GetComponent<MeshCollider>().sharedMesh = savedMeshAsset;

            // Boundary Walls
            CreateBoundary(chunkGo.transform, "LeftBoundary", new Vector3(-250f, 10f, 250f), new Vector3(2f, 20f, 500f));
            CreateBoundary(chunkGo.transform, "RightBoundary", new Vector3(250f, 10f, 250f), new Vector3(2f, 20f, 500f));

            // Spawn Next Trigger (Z = 380m)
            GameObject triggerNextGo = new GameObject("Trigger_SpawnNext");
            triggerNextGo.transform.SetParent(chunkGo.transform);
            triggerNextGo.transform.localPosition = new Vector3(0f, 5f, 380f);

            BoxCollider triggerNextCol = triggerNextGo.AddComponent<BoxCollider>();
            triggerNextCol.isTrigger = true;
            triggerNextCol.size = new Vector3(140f, 15f, 15f);

            ChunkTrigger triggerNext = triggerNextGo.AddComponent<ChunkTrigger>();
            triggerNext.triggerType = ChunkTrigger.TriggerType.SpawnNextChunk;
            triggerNext.parentChunk = chunk;
            chunk.spawnNextTrigger = triggerNext;

            // Seal Passage Trigger (Z = 40m)
            GameObject triggerSealGo = new GameObject("Trigger_SealPassage");
            triggerSealGo.transform.SetParent(chunkGo.transform);
            triggerSealGo.transform.localPosition = new Vector3(0f, 5f, 40f);

            BoxCollider triggerSealCol = triggerSealGo.AddComponent<BoxCollider>();
            triggerSealCol.isTrigger = true;
            triggerSealCol.size = new Vector3(140f, 15f, 15f);

            ChunkTrigger triggerSeal = triggerSealGo.AddComponent<ChunkTrigger>();
            triggerSeal.triggerType = ChunkTrigger.TriggerType.SealBackwardPassage;
            triggerSeal.parentChunk = chunk;

            // Back Blockade Wall (Z = 2m)
            GameObject blockadeGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
            blockadeGo.name = "BackBlockade_Wall";
            blockadeGo.transform.SetParent(chunkGo.transform);
            blockadeGo.transform.localPosition = new Vector3(0f, 5f, 2f);
            blockadeGo.transform.localScale = new Vector3(40f, 10f, 2f);

            var blockadeRenderer = blockadeGo.GetComponent<Renderer>();
            if (blockadeRenderer != null && blockadeMat != null)
            {
                blockadeRenderer.sharedMaterial = blockadeMat;
            }

            blockadeGo.SetActive(false);
            chunk.backBlockade = blockadeGo;

            return chunk;
        }

        private static void SaveChunkAsPrefab(Chunk chunkInstance, string folderPath)
        {
            string prefabPath = $"{folderPath}/{chunkInstance.gameObject.name}.prefab";
            PrefabUtility.SaveAsPrefabAssetAndConnect(chunkInstance.gameObject, prefabPath, InteractionMode.AutomatedAction);
            Object.DestroyImmediate(chunkInstance.gameObject);
        }

        private static void CreateBoundary(Transform parent, string name, Vector3 localPos, Vector3 scale)
        {
            GameObject wall = new GameObject(name);
            wall.transform.SetParent(parent);
            wall.transform.localPosition = localPos;
            BoxCollider col = wall.AddComponent<BoxCollider>();
            col.size = scale;
        }
    }
}
#endif
