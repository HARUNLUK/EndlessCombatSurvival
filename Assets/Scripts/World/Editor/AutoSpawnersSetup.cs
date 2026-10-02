#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using EndlessSurvival.World;
using EndlessSurvival.World.POI;
using EndlessSurvival.Inventory;

namespace EndlessSurvival.EditorScripts
{
    [InitializeOnLoad]
    public class AutoSpawnersSetup
    {
        static AutoSpawnersSetup()
        {
            EditorApplication.delayCall += RunSetupOnce;
        }

        static void RunSetupOnce()
        {
            if (EditorPrefs.GetBool("AutoSpawnersSetupRun_V3", false)) return;
            EditorPrefs.SetBool("AutoSpawnersSetupRun_V3", true);

            Debug.Log("<color=yellow>Starting Automatic Default Setup for Spawners...</color>");

            // 1. Find User's Existing Loot Table
            string tablePath = "Assets/Data/Loot/Loot_Roadside.asset";
            LootTable lootTable = AssetDatabase.LoadAssetAtPath<LootTable>(tablePath);
            if (lootTable == null)
            {
                Debug.LogError($"[AutoSpawnersSetup] Bulunamadı: {tablePath}");
                return;
            }

            // 2. Find User's Existing Chest Prefab
            string chestPath = "Assets/Prefabs/Loot/Chest_Cube.prefab";
            LootChest chestPrefab = AssetDatabase.LoadAssetAtPath<LootChest>(chestPath);
            if (chestPrefab == null)
            {
                Debug.LogError($"[AutoSpawnersSetup] Bulunamadı: {chestPath}");
                return;
            }

            // 3. Find User's Existing Enemy Prefab
            string enemyPath = "Assets/Prefabs/Enemies/Enemy.prefab";
            GameObject enemyPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(enemyPath);
            if (enemyPrefab == null)
            {
                Debug.LogError($"[AutoSpawnersSetup] Bulunamadı: {enemyPath}");
                return;
            }

            // 5. Update All Chunks
            string[] chunkGuids = AssetDatabase.FindAssets("t:GameObject", new[] { "Assets/Prefabs/Chunks" });
            foreach (string guid in chunkGuids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                GameObject prefabContents = PrefabUtility.LoadPrefabContents(path);

                if (prefabContents != null && prefabContents.GetComponent<Chunk>() != null)
                {
                    // RoadsideLootSpawner
                    RoadsideLootSpawner lootSpawner = prefabContents.GetComponent<RoadsideLootSpawner>();
                    if (lootSpawner == null) lootSpawner = prefabContents.AddComponent<RoadsideLootSpawner>();
                    lootSpawner.pickupTable = lootTable;
                    lootSpawner.chestPrefab = chestPrefab;
                    lootSpawner.maxLateralDistance = 230f; // <--- Zorla 230 yapıyoruz ki etrafa saçılsın!
                    lootSpawner.minLateralDistance = 15f;

                    // EnemyCampPOI
                    Transform campTransform = prefabContents.transform.Find("Enemy Camp");
                    GameObject campObj;
                    if (campTransform == null)
                    {
                        campObj = new GameObject("Enemy Camp");
                        campObj.transform.SetParent(prefabContents.transform, false);
                        campObj.transform.localPosition = new Vector3(0, 0, 250); // Yolun ortası
                    }
                    else
                    {
                        campObj = campTransform.gameObject;
                    }

                    EnemyCampPOI campPOI = campObj.GetComponent<EnemyCampPOI>();
                    if (campPOI == null) campPOI = campObj.AddComponent<EnemyCampPOI>();
                    campPOI.enemyPrefabs = new GameObject[] { enemyPrefab };
                    campPOI.spawnChance = 1.0f; // Kesin çıksın diye
                    
                    PrefabUtility.SaveAsPrefabAsset(prefabContents, path);
                }
                PrefabUtility.UnloadPrefabContents(prefabContents);
            }

            AssetDatabase.SaveAssets();
            Debug.Log("<color=green>SUCCESS: All chunks have been updated with default Loot and Enemies!</color>");
        }
    }
}
#endif
