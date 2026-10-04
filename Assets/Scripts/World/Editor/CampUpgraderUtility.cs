using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using EndlessSurvival.World.POI;
using EndlessSurvival.Story;

public class CampUpgraderUtility
{
    [InitializeOnLoadMethod]
    private static void AutoUpgradeCampsOnce()
    {
        if (!EditorPrefs.GetBool("Camp_Upgrade_Outpost_v2", false))
        {
            EditorPrefs.SetBool("Camp_Upgrade_Outpost_v2", true);
            EditorApplication.delayCall += () =>
            {
                UpgradeCamps();
            };
        }
    }

    [MenuItem("Tools/Upgrade Camps")]
    [MenuItem("Endless Survival/Setup Camps & Animations")]
    public static void UpgradeCamps()
    {
        Debug.Log("<color=cyan>[CampUpgrader] Kamp Tipleri ve Animasyon Kurulumu Başlatılıyor...</color>");

        // 1. Otomatik Sitting Animasyonu Entegrasyonu
        AnimationIntegrator.IntegrateSittingAnimation();

        // 2. Klasörleri Doğrula
        EnsureFolder("Assets/Materials");
        EnsureFolder("Assets/Prefabs");
        EnsureFolder("Assets/Prefabs/PointsOfInterests");
        EnsureFolder("Assets/Resources");
        EnsureFolder("Assets/Resources/Dialogues");

        // 3. Kalıcı Materyalleri Hazırla
        Material fireWoodMat = GetOrCreateMaterial("Mat_Camp_Wood", new Color(0.35f, 0.20f, 0.08f), null, 0.15f);
        Material emberMat = GetOrCreateMaterial("Mat_Camp_Embers", new Color(1.0f, 0.25f, 0.05f), new Color(1.5f, 0.45f, 0.05f) * 3f, 0.3f);
        Material stoneMat = GetOrCreateMaterial("Mat_Camp_Stone", new Color(0.32f, 0.32f, 0.34f), null, 0.1f);
        Material ashMat = GetOrCreateMaterial("Mat_Camp_Ash", new Color(0.12f, 0.12f, 0.12f), null, 0.05f);
        Material ironMat = GetOrCreateMaterial("Mat_Camp_Iron", new Color(0.2f, 0.2f, 0.22f), null, 0.6f);
        Material wallMat = GetOrCreateMaterial("Mat_Outpost_Wall", new Color(0.28f, 0.18f, 0.10f), null, 0.1f);
        Material gateMat = GetOrCreateMaterial("Mat_Outpost_Gate", new Color(0.22f, 0.14f, 0.07f), null, 0.15f);
        Material hutWoodMat = GetOrCreateMaterial("Mat_Outpost_HutWood", new Color(0.42f, 0.26f, 0.14f), null, 0.2f);
        Material roofMat = GetOrCreateMaterial("Mat_Outpost_Roof", new Color(0.18f, 0.12f, 0.08f), null, 0.1f);
        Material clothMat = GetOrCreateMaterial("Mat_Camp_Cloth", new Color(0.55f, 0.48f, 0.38f), null, 0.05f);
        Material crateMat = GetOrCreateMaterial("Mat_Camp_Crate", new Color(0.48f, 0.34f, 0.18f), null, 0.2f);

        // 4. Diyalog Setlerini Hazırla
        DialogueSet diagComplaining = AssetDatabase.LoadAssetAtPath<DialogueSet>("Assets/Resources/Dialogues/Dialogue_Complaining.asset");
        DialogueSet diagStory = AssetDatabase.LoadAssetAtPath<DialogueSet>("Assets/Resources/Dialogues/Dialogue_Story.asset");
        DialogueSet diagOutpost = GetOrCreateOutpostDialogue();

        // 5. Düşman Prefab'ini Bul
        GameObject enemyPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Enemies/Enemy.prefab");
        GameObject[] enemyList = enemyPrefab != null ? new GameObject[] { enemyPrefab } : new GameObject[0];

        // 6. PREFAB 1: OTURAK ALANLI, ORTASI ATEŞLİ KAMP (Campfire with Seating Area)
        string campfirePrefabPath = "Assets/Prefabs/PointsOfInterests/EnemyCamp_Campfire.prefab";
        string mediumPrefabPath = "Assets/Prefabs/PointsOfInterests/EnemyCamp_Medium.prefab";
        GameObject campfireCamp = BuildCampfireCamp(fireWoodMat, emberMat, stoneMat, ashMat, ironMat, clothMat, crateMat, enemyList, new DialogueSet[] { diagComplaining, diagStory });
        
        PrefabUtility.SaveAsPrefabAsset(campfireCamp, campfirePrefabPath);
        PrefabUtility.SaveAsPrefabAsset(campfireCamp, mediumPrefabPath); // Eski Medium prefabını da güncelle
        GameObject.DestroyImmediate(campfireCamp);
        Debug.Log($"[CampUpgrader] Campfire prefabi oluşturuldu: {campfirePrefabPath}");

        // 7. PREFAB 2: ETRAFI DUVARLI, İÇİNDE KULÜBESİ OLAN KARAKOL (Walled Outpost with Hut)
        string outpostPrefabPath = "Assets/Prefabs/PointsOfInterests/EnemyCamp_Outpost.prefab";
        GameObject outpostCamp = BuildOutpostCamp(wallMat, gateMat, hutWoodMat, roofMat, emberMat, ironMat, crateMat, enemyList, new DialogueSet[] { diagOutpost, diagStory, diagComplaining });

        PrefabUtility.SaveAsPrefabAsset(outpostCamp, outpostPrefabPath);
        GameObject.DestroyImmediate(outpostCamp);
        Debug.Log($"[CampUpgrader] Outpost prefabi oluşturuldu: {outpostPrefabPath}");

        // 8. Chunk Prefab'larını Güncelle (Chunk_Forest_Curve)
        UpdateChunkPrefabsWithCamps(campfirePrefabPath, outpostPrefabPath);

        // 9. Oyuncu Kamp ve Hayatta Kalma Sistemini Kur (PlayerCampfire, Yemek Pişirme, Kanama, İhtiyaç)
        EndlessSurvival.Camping.Editor.PlayerCampSetupUtility.SetupPlayerCamping();

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("<color=green>TÜM DÜŞMAN KAMPLARI, OYUNCU KAMP SİSTEMİ VE ANİMASYON ENTEGRASYONU BAŞARIYLA TAMAMLANDI!</color>");
    }

    // ==========================================
    // PREFAB 1 BUILDER: KAMP ATEŞİ & OTURAK ALANI
    // ==========================================
    private static GameObject BuildCampfireCamp(Material woodMat, Material fireMat, Material stoneMat, Material ashMat, Material metalMat, Material clothMat, Material crateMat, GameObject[] enemyList, DialogueSet[] dialogues)
    {
        GameObject camp = new GameObject("EnemyCamp_Campfire");
        var poi = camp.AddComponent<EnemyCampPOI>();
        poi.campType = EnemyCampPOI.CampType.Campfire;
        poi.minEnemies = 3;
        poi.maxEnemies = 4;
        poi.spawnRadius = 7f;
        poi.dialogueTriggerDistance = 35f;
        poi.randomizeCampLocation = true;
        poi.enemyPrefabs = enemyList;
        poi.possibleDialogues = dialogues;

        // A) Ortadaki Kamp Ateşi
        GameObject fireGroup = new GameObject("CampfireGroup");
        fireGroup.transform.SetParent(camp.transform, false);

        // Kül yatağı
        GameObject ash = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        ash.name = "AshBed";
        ash.transform.SetParent(fireGroup.transform, false);
        ash.transform.localPosition = new Vector3(0, 0.02f, 0);
        ash.transform.localScale = new Vector3(1.6f, 0.04f, 1.6f);
        ash.GetComponent<Renderer>().sharedMaterial = ashMat;
        GameObject.DestroyImmediate(ash.GetComponent<Collider>());

        // Taş halkası
        int stoneCount = 10;
        float stoneRadius = 0.95f;
        for (int i = 0; i < stoneCount; i++)
        {
            float angle = i * Mathf.PI * 2f / stoneCount;
            Vector3 pos = new Vector3(Mathf.Cos(angle) * stoneRadius, 0.15f, Mathf.Sin(angle) * stoneRadius);
            GameObject stone = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            stone.name = $"Stone_{i}";
            stone.transform.SetParent(fireGroup.transform, false);
            stone.transform.localPosition = pos;
            stone.transform.localScale = new Vector3(0.38f, 0.28f, 0.38f);
            stone.GetComponent<Renderer>().sharedMaterial = stoneMat;
        }

        // Çapraz odun kütükleri
        for (int i = 0; i < 6; i++)
        {
            float rotY = i * 60f;
            GameObject log = GameObject.CreatePrimitive(PrimitiveType.Cube);
            log.name = $"Log_{i}";
            log.transform.SetParent(fireGroup.transform, false);
            log.transform.localPosition = new Vector3(0, 0.15f, 0);
            log.transform.localRotation = Quaternion.Euler(18f, rotY, 0);
            log.transform.localScale = new Vector3(0.18f, 0.16f, 1.1f);
            log.GetComponent<Renderer>().sharedMaterial = woodMat;
        }

        // Parlayan köz çekirdeği
        GameObject ember = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        ember.name = "EmberCore";
        ember.transform.SetParent(fireGroup.transform, false);
        ember.transform.localPosition = new Vector3(0, 0.22f, 0);
        ember.transform.localScale = new Vector3(0.55f, 0.35f, 0.55f);
        ember.GetComponent<Renderer>().sharedMaterial = fireMat;
        GameObject.DestroyImmediate(ember.GetComponent<Collider>());

        // Ateş Işığı
        GameObject lightObj = new GameObject("FireLight");
        lightObj.transform.SetParent(fireGroup.transform, false);
        lightObj.transform.localPosition = new Vector3(0, 0.8f, 0);
        Light lightComp = lightObj.AddComponent<Light>();
        lightComp.type = LightType.Point;
        lightComp.color = new Color(1f, 0.55f, 0.15f);
        lightComp.range = 16f;
        lightComp.intensity = 2.5f;

        // Asılı Yemek Kazanı (Tripod)
        GameObject tripod = new GameObject("CookingTripod");
        tripod.transform.SetParent(fireGroup.transform, false);
        CreatePrimitiveChild(tripod.transform, PrimitiveType.Cube, "Leg_L", new Vector3(-0.75f, 0.6f, 0), new Vector3(0, 0, -12), new Vector3(0.08f, 1.3f, 0.08f), woodMat);
        CreatePrimitiveChild(tripod.transform, PrimitiveType.Cube, "Leg_R", new Vector3(0.75f, 0.6f, 0), new Vector3(0, 0, 12), new Vector3(0.08f, 1.3f, 0.08f), woodMat);
        CreatePrimitiveChild(tripod.transform, PrimitiveType.Cube, "Bar", new Vector3(0, 1.15f, 0), new Vector3(0, 0, 90), new Vector3(0.07f, 1.7f, 0.07f), woodMat);
        CreatePrimitiveChild(tripod.transform, PrimitiveType.Sphere, "Pot", new Vector3(0, 0.7f, 0), Vector3.zero, new Vector3(0.35f, 0.32f, 0.35f), metalMat);

        // B) Oturak Alanları (Seating Area & Benches)
        GameObject seatingArea = new GameObject("SeatingArea");
        seatingArea.transform.SetParent(camp.transform, false);

        // 4 Yönde kütük banklar (North, South, East, West)
        CreateBenchWithSeat(seatingArea.transform, "Seat_0", new Vector3(0, 0, 2.3f), 180f, woodMat);
        CreateBenchWithSeat(seatingArea.transform, "Seat_1", new Vector3(0, 0, -2.3f), 0f, woodMat);
        CreateBenchWithSeat(seatingArea.transform, "Seat_2", new Vector3(2.3f, 0, 0), 270f, woodMat);
        CreateBenchWithSeat(seatingArea.transform, "Seat_3", new Vector3(-2.3f, 0, 0), 90f, woodMat);

        // C) Kamp Çevre Detayları (Props & Shelter)
        GameObject props = new GameObject("CampProps");
        props.transform.SetParent(camp.transform, false);

        // İstiflenmiş yakacak odunlar
        GameObject woodpile = new GameObject("WoodPile");
        woodpile.transform.SetParent(props.transform, false);
        woodpile.transform.localPosition = new Vector3(-3.8f, 0, 2.2f);
        for (int y = 0; y < 3; y++)
        {
            for (int x = 0; x < 3 - y; x++)
            {
                CreatePrimitiveChild(woodpile.transform, PrimitiveType.Cylinder, $"Log_{y}_{x}", 
                    new Vector3((x - (2 - y) * 0.5f) * 0.3f, 0.15f + y * 0.22f, 0), 
                    new Vector3(90, 0, 0), 
                    new Vector3(0.24f, 0.7f, 0.24f), woodMat);
            }
        }

        // Erzak sandığı
        CreatePrimitiveChild(props.transform, PrimitiveType.Cube, "SupplyCrate_Large", new Vector3(3.6f, 0.4f, 2.0f), new Vector3(0, 15, 0), new Vector3(0.9f, 0.8f, 0.9f), crateMat);
        CreatePrimitiveChild(props.transform, PrimitiveType.Cube, "SupplyCrate_Small", new Vector3(3.5f, 0.25f, 3.0f), new Vector3(0, -10, 0), new Vector3(0.6f, 0.5f, 0.6f), crateMat);

        // Tente / Küçük Barınak (Lean-to Tarp)
        GameObject shelter = new GameObject("LeanToShelter");
        shelter.transform.SetParent(props.transform, false);
        shelter.transform.localPosition = new Vector3(0, 0, -4.6f);
        // Tente kumaşı
        CreatePrimitiveChild(shelter.transform, PrimitiveType.Cube, "Tarp", new Vector3(0, 1.4f, 0), new Vector3(-30, 0, 0), new Vector3(3.8f, 0.05f, 2.6f), clothMat);
        // Destek direkleri
        CreatePrimitiveChild(shelter.transform, PrimitiveType.Cube, "Pole_L", new Vector3(-1.8f, 1.0f, 1.0f), Vector3.zero, new Vector3(0.1f, 2.0f, 0.1f), woodMat);
        CreatePrimitiveChild(shelter.transform, PrimitiveType.Cube, "Pole_R", new Vector3(1.8f, 1.0f, 1.0f), Vector3.zero, new Vector3(0.1f, 2.0f, 0.1f), woodMat);
        // Yatak matları
        CreatePrimitiveChild(shelter.transform, PrimitiveType.Cube, "Bedroll_1", new Vector3(-0.9f, 0.06f, 0.3f), new Vector3(0, 5, 0), new Vector3(0.8f, 0.12f, 1.8f), clothMat);
        CreatePrimitiveChild(shelter.transform, PrimitiveType.Cube, "Bedroll_2", new Vector3(0.9f, 0.06f, 0.2f), new Vector3(0, -5, 0), new Vector3(0.8f, 0.12f, 1.8f), clothMat);

        return camp;
    }

    private static void CreateBenchWithSeat(Transform parent, string seatName, Vector3 pos, float rotY, Material woodMat)
    {
        GameObject bench = new GameObject($"Bench_{seatName}");
        bench.transform.SetParent(parent, false);
        bench.transform.localPosition = pos;
        bench.transform.localRotation = Quaternion.Euler(0, rotY, 0);

        // Oturma tahtası
        CreatePrimitiveChild(bench.transform, PrimitiveType.Cube, "Plank", new Vector3(0, 0.38f, 0), Vector3.zero, new Vector3(1.6f, 0.16f, 0.52f), woodMat);
        // Sol ayak
        CreatePrimitiveChild(bench.transform, PrimitiveType.Cube, "Leg_L", new Vector3(-0.55f, 0.19f, 0), Vector3.zero, new Vector3(0.22f, 0.38f, 0.42f), woodMat);
        // Sağ ayak
        CreatePrimitiveChild(bench.transform, PrimitiveType.Cube, "Leg_R", new Vector3(0.55f, 0.19f, 0), Vector3.zero, new Vector3(0.22f, 0.38f, 0.42f), woodMat);

        // Seat Spot Transform (düşmanın oturacağı nokta)
        GameObject seat = new GameObject(seatName);
        seat.transform.SetParent(bench.transform, false);
        seat.transform.localPosition = Vector3.zero;
        seat.transform.localRotation = Quaternion.identity;
    }

    // ==========================================
    // PREFAB 2 BUILDER: DUVARLI KARAKOL & KULÜBE
    // ==========================================
    private static GameObject BuildOutpostCamp(Material wallMat, Material gateMat, Material hutWoodMat, Material roofMat, Material emberMat, Material metalMat, Material crateMat, GameObject[] enemyList, DialogueSet[] dialogues)
    {
        GameObject camp = new GameObject("EnemyCamp_Outpost");
        var poi = camp.AddComponent<EnemyCampPOI>();
        poi.campType = EnemyCampPOI.CampType.Outpost;
        poi.minEnemies = 3;
        poi.maxEnemies = 5;
        poi.spawnRadius = 10f;
        poi.dialogueTriggerDistance = 38f;
        poi.randomizeCampLocation = true;
        poi.enemyPrefabs = enemyList;
        poi.possibleDialogues = dialogues;

        // A) Savunma Duvarları (Perimeter Walls, 22x22m)
        GameObject walls = new GameObject("PerimeterWalls");
        walls.transform.SetParent(camp.transform, false);

        // Arka Duvar
        CreatePrimitiveChild(walls.transform, PrimitiveType.Cube, "Wall_Back", new Vector3(0, 1.6f, 11f), Vector3.zero, new Vector3(22f, 3.2f, 0.5f), wallMat);
        // Sol Duvar
        CreatePrimitiveChild(walls.transform, PrimitiveType.Cube, "Wall_Left", new Vector3(-11f, 1.6f, 0), Vector3.zero, new Vector3(0.5f, 3.2f, 22f), wallMat);
        // Sağ Duvar
        CreatePrimitiveChild(walls.transform, PrimitiveType.Cube, "Wall_Right", new Vector3(11f, 1.6f, 0), Vector3.zero, new Vector3(0.5f, 3.2f, 22f), wallMat);
        
        // Ön Duvar - Sol Kanat
        CreatePrimitiveChild(walls.transform, PrimitiveType.Cube, "Wall_Front_L", new Vector3(-7.2f, 1.6f, -11f), Vector3.zero, new Vector3(7.6f, 3.2f, 0.5f), wallMat);
        // Ön Duvar - Sağ Kanat
        CreatePrimitiveChild(walls.transform, PrimitiveType.Cube, "Wall_Front_R", new Vector3(7.2f, 1.6f, -11f), Vector3.zero, new Vector3(7.6f, 3.2f, 0.5f), wallMat);
        // Kapı Boşluğu: 6.8m genişlik X = -3.4 ile +3.4 arası

        // Kapı Direkleri ve Üst Kiriş
        CreatePrimitiveChild(walls.transform, PrimitiveType.Cube, "Gate_Pillar_L", new Vector3(-3.4f, 2.2f, -11f), Vector3.zero, new Vector3(0.9f, 4.4f, 0.9f), gateMat);
        CreatePrimitiveChild(walls.transform, PrimitiveType.Cube, "Gate_Pillar_R", new Vector3(3.4f, 2.2f, -11f), Vector3.zero, new Vector3(0.9f, 4.4f, 0.9f), gateMat);
        CreatePrimitiveChild(walls.transform, PrimitiveType.Cube, "Gate_Crossbeam", new Vector3(0, 4.1f, -11f), Vector3.zero, new Vector3(7.8f, 0.5f, 0.8f), gateMat);

        // 4 Köşe Nöbetçi Direkleri
        CreatePrimitiveChild(walls.transform, PrimitiveType.Cube, "Corner_Post_BL", new Vector3(-11f, 2.4f, 11f), Vector3.zero, new Vector3(1.1f, 4.8f, 1.1f), gateMat);
        CreatePrimitiveChild(walls.transform, PrimitiveType.Cube, "Corner_Post_BR", new Vector3(11f, 2.4f, 11f), Vector3.zero, new Vector3(1.1f, 4.8f, 1.1f), gateMat);
        CreatePrimitiveChild(walls.transform, PrimitiveType.Cube, "Corner_Post_FL", new Vector3(-11f, 2.4f, -11f), Vector3.zero, new Vector3(1.1f, 4.8f, 1.1f), gateMat);
        CreatePrimitiveChild(walls.transform, PrimitiveType.Cube, "Corner_Post_FR", new Vector3(11f, 2.4f, -11f), Vector3.zero, new Vector3(1.1f, 4.8f, 1.1f), gateMat);

        // Kapı Meşaleleri
        CreateTorch(walls.transform, "Torch_Gate_L", new Vector3(-3.4f, 2.8f, -11.5f), emberMat);
        CreateTorch(walls.transform, "Torch_Gate_R", new Vector3(3.4f, 2.8f, -11.5f), emberMat);

        // B) Ahşap Kulübe (Central Hut)
        GameObject hut = new GameObject("CentralHut");
        hut.transform.SetParent(camp.transform, false);
        hut.transform.localPosition = new Vector3(0, 0, 3.5f);

        // Zemin / Platform
        CreatePrimitiveChild(hut.transform, PrimitiveType.Cube, "Floor", new Vector3(0, 0.15f, 0), Vector3.zero, new Vector3(8.4f, 0.3f, 7.0f), hutWoodMat);
        // Arka Duvar
        CreatePrimitiveChild(hut.transform, PrimitiveType.Cube, "BackWall", new Vector3(0, 1.95f, 3.35f), Vector3.zero, new Vector3(8.0f, 3.6f, 0.35f), hutWoodMat);
        // Sol Duvar
        CreatePrimitiveChild(hut.transform, PrimitiveType.Cube, "LeftWall", new Vector3(-3.85f, 1.95f, 0), Vector3.zero, new Vector3(0.35f, 3.6f, 6.7f), hutWoodMat);
        // Sağ Duvar
        CreatePrimitiveChild(hut.transform, PrimitiveType.Cube, "RightWall", new Vector3(3.85f, 1.95f, 0), Vector3.zero, new Vector3(0.35f, 3.6f, 6.7f), hutWoodMat);
        // Ön Duvar - Kapı Açıklığı ile
        CreatePrimitiveChild(hut.transform, PrimitiveType.Cube, "FrontWall_L", new Vector3(-2.7f, 1.95f, -3.35f), Vector3.zero, new Vector3(2.6f, 3.6f, 0.35f), hutWoodMat);
        CreatePrimitiveChild(hut.transform, PrimitiveType.Cube, "FrontWall_R", new Vector3(2.7f, 1.95f, -3.35f), Vector3.zero, new Vector3(2.6f, 3.6f, 0.35f), hutWoodMat);
        CreatePrimitiveChild(hut.transform, PrimitiveType.Cube, "Door_Lintel", new Vector3(0, 3.25f, -3.35f), Vector3.zero, new Vector3(2.8f, 1.0f, 0.35f), hutWoodMat);

        // Çatı (Beşik Çatı / Gable Roof)
        CreatePrimitiveChild(hut.transform, PrimitiveType.Cube, "Roof_Left", new Vector3(-2.1f, 4.25f, 0), new Vector3(0, 0, 24f), new Vector3(5.3f, 0.22f, 7.8f), roofMat);
        CreatePrimitiveChild(hut.transform, PrimitiveType.Cube, "Roof_Right", new Vector3(2.1f, 4.25f, 0), new Vector3(0, 0, -24f), new Vector3(5.3f, 0.22f, 7.8f), roofMat);
        CreatePrimitiveChild(hut.transform, PrimitiveType.Cube, "Roof_Ridge", new Vector3(0, 4.95f, 0), Vector3.zero, new Vector3(0.45f, 0.45f, 8.0f), roofMat);

        // Kulübe İçi Lamba & Eşyalar
        GameObject hutLightObj = new GameObject("HutLight");
        hutLightObj.transform.SetParent(hut.transform, false);
        hutLightObj.transform.localPosition = new Vector3(0, 3.0f, 0);
        Light hutLight = hutLightObj.AddComponent<Light>();
        hutLight.type = LightType.Point;
        hutLight.color = new Color(1.0f, 0.8f, 0.5f);
        hutLight.range = 9f;
        hutLight.intensity = 1.8f;

        CreatePrimitiveChild(hut.transform, PrimitiveType.Cube, "Table", new Vector3(-1.8f, 0.5f, 1.5f), Vector3.zero, new Vector3(1.6f, 0.7f, 0.9f), hutWoodMat);
        CreatePrimitiveChild(hut.transform, PrimitiveType.Cube, "Stool_1", new Vector3(-1.8f, 0.25f, 0.7f), Vector3.zero, new Vector3(0.4f, 0.45f, 0.4f), hutWoodMat);
        CreatePrimitiveChild(hut.transform, PrimitiveType.Cube, "Stool_2", new Vector3(-1.8f, 0.25f, 2.3f), Vector3.zero, new Vector3(0.4f, 0.45f, 0.4f), hutWoodMat);
        CreatePrimitiveChild(hut.transform, PrimitiveType.Cube, "Chest", new Vector3(2.2f, 0.4f, 1.5f), new Vector3(0, -30, 0), new Vector3(1.2f, 0.8f, 0.8f), crateMat);

        // C) Avlu Detayları (Courtyard Props)
        GameObject courtyard = new GameObject("CourtyardProps");
        courtyard.transform.SetParent(camp.transform, false);

        // Sandık ve fıçı istifleri
        CreatePrimitiveChild(courtyard.transform, PrimitiveType.Cube, "CrateStack_1", new Vector3(6.5f, 0.5f, -2.0f), new Vector3(0, 20, 0), new Vector3(1.1f, 1.0f, 1.1f), crateMat);
        CreatePrimitiveChild(courtyard.transform, PrimitiveType.Cube, "CrateStack_2", new Vector3(6.3f, 1.4f, -2.0f), new Vector3(0, -10, 0), new Vector3(0.8f, 0.8f, 0.8f), crateMat);
        CreatePrimitiveChild(courtyard.transform, PrimitiveType.Cube, "CrateStack_3", new Vector3(7.4f, 0.4f, -1.2f), new Vector3(0, 45, 0), new Vector3(0.8f, 0.7f, 0.8f), crateMat);

        // Giriş Siper Kazıkları (Barricade Spikes)
        CreateBarricadeSpikes(courtyard.transform, "Spikes_L", new Vector3(-4.8f, 0.4f, -8.0f), 25f, gateMat);
        CreateBarricadeSpikes(courtyard.transform, "Spikes_R", new Vector3(4.8f, 0.4f, -8.0f), -25f, gateMat);

        // Avlu Mangalı
        GameObject brazier = new GameObject("CourtyardBrazier");
        brazier.transform.SetParent(courtyard.transform, false);
        brazier.transform.localPosition = new Vector3(0, 0, -3.5f);
        CreatePrimitiveChild(brazier.transform, PrimitiveType.Cylinder, "Bowl", new Vector3(0, 0.5f, 0), Vector3.zero, new Vector3(0.8f, 0.3f, 0.8f), metalMat);
        CreatePrimitiveChild(brazier.transform, PrimitiveType.Sphere, "Fire", new Vector3(0, 0.65f, 0), Vector3.zero, new Vector3(0.5f, 0.3f, 0.5f), emberMat);
        GameObject bLight = new GameObject("BrazierLight");
        bLight.transform.SetParent(brazier.transform, false);
        bLight.transform.localPosition = new Vector3(0, 1.0f, 0);
        Light blComp = bLight.AddComponent<Light>();
        blComp.type = LightType.Point;
        blComp.color = new Color(1.0f, 0.6f, 0.2f);
        blComp.range = 14f;
        blComp.intensity = 2.0f;

        // D) Nöbetçi Noktaları (GuardSpots)
        GameObject guardSpots = new GameObject("GuardSpots");
        guardSpots.transform.SetParent(camp.transform, false);

        CreateGuardSpot(guardSpots.transform, "Guard_0", new Vector3(-2.2f, 0, -9.5f), 180f); // Sol kapı nöbetçisi
        CreateGuardSpot(guardSpots.transform, "Guard_1", new Vector3(2.2f, 0, -9.5f), 180f);  // Sağ kapı nöbetçisi
        CreateGuardSpot(guardSpots.transform, "Guard_2", new Vector3(-4.5f, 0, -1.0f), 90f);   // Avlu devriyesi
        CreateGuardSpot(guardSpots.transform, "Guard_3", new Vector3(0, 0, -0.6f), 180f);      // Kulübe kapısı nöbetçisi

        return camp;
    }

    private static void CreateGuardSpot(Transform parent, string name, Vector3 pos, float rotY)
    {
        GameObject spot = new GameObject(name);
        spot.transform.SetParent(parent, false);
        spot.transform.localPosition = pos;
        spot.transform.localRotation = Quaternion.Euler(0, rotY, 0);
    }

    private static void CreateTorch(Transform parent, string name, Vector3 pos, Material emberMat)
    {
        GameObject torch = new GameObject(name);
        torch.transform.SetParent(parent, false);
        torch.transform.localPosition = pos;

        CreatePrimitiveChild(torch.transform, PrimitiveType.Cube, "Stick", new Vector3(0, 0, 0), Vector3.zero, new Vector3(0.12f, 0.45f, 0.12f), emberMat);
        
        GameObject lightObj = new GameObject("TorchLight");
        lightObj.transform.SetParent(torch.transform, false);
        lightObj.transform.localPosition = new Vector3(0, 0.25f, 0);
        Light light = lightObj.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = new Color(1.0f, 0.55f, 0.15f);
        light.range = 10f;
        light.intensity = 2.0f;
    }

    private static void CreateBarricadeSpikes(Transform parent, string name, Vector3 pos, float rotY, Material woodMat)
    {
        GameObject barricade = new GameObject(name);
        barricade.transform.SetParent(parent, false);
        barricade.transform.localPosition = pos;
        barricade.transform.localRotation = Quaternion.Euler(0, rotY, 0);

        CreatePrimitiveChild(barricade.transform, PrimitiveType.Cube, "Beam_1", new Vector3(0, 0.4f, 0), new Vector3(45, 0, 0), new Vector3(0.15f, 1.2f, 0.15f), woodMat);
        CreatePrimitiveChild(barricade.transform, PrimitiveType.Cube, "Beam_2", new Vector3(0.4f, 0.4f, 0), new Vector3(45, 0, 0), new Vector3(0.15f, 1.2f, 0.15f), woodMat);
        CreatePrimitiveChild(barricade.transform, PrimitiveType.Cube, "Beam_3", new Vector3(-0.4f, 0.4f, 0), new Vector3(45, 0, 0), new Vector3(0.15f, 1.2f, 0.15f), woodMat);
        CreatePrimitiveChild(barricade.transform, PrimitiveType.Cube, "Cross", new Vector3(0, 0.35f, 0), new Vector3(0, 0, 90), new Vector3(0.12f, 1.4f, 0.12f), woodMat);
    }

    // ==========================================
    // CHUNK PREFAB GÜNCELLEME
    // ==========================================
    private static void UpdateChunkPrefabsWithCamps(string campfirePath, string outpostPath)
    {
        string[] chunkGuids = AssetDatabase.FindAssets("t:GameObject", new[] { "Assets/Prefabs/Chunks" });
        GameObject campfirePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(campfirePath);
        GameObject outpostPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(outpostPath);

        foreach (string guid in chunkGuids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            GameObject chunkGo = PrefabUtility.LoadPrefabContents(path);

            if (chunkGo != null && chunkGo.GetComponent<EndlessSurvival.World.Chunk>() != null)
            {
                // Mevcut 'Enemy Camp' ya da EnemyCampPOI bileşenlerini temizle
                for (int i = chunkGo.transform.childCount - 1; i >= 0; i--)
                {
                    Transform ch = chunkGo.transform.GetChild(i);
                    if (ch.name.Contains("Enemy Camp") || ch.name.Contains("Campfire") || ch.name.Contains("Outpost") || ch.GetComponent<EnemyCampPOI>() != null)
                    {
                        GameObject.DestroyImmediate(ch.gameObject);
                    }
                }

                // Kök objedeki eski EnemyCampPOI varsa kaldır
                var rootPOI = chunkGo.GetComponent<EnemyCampPOI>();
                if (rootPOI != null) GameObject.DestroyImmediate(rootPOI);

                // Chunk üzerine EnemyCampSpawner bileşenini ekle / güncelle
                var campSpawner = chunkGo.GetComponentInChildren<EndlessSurvival.World.POI.EnemyCampSpawner>();
                if (campSpawner == null)
                {
                    GameObject spawnerObj = new GameObject("EnemyCampSpawner");
                    spawnerObj.transform.SetParent(chunkGo.transform, false);
                    campSpawner = spawnerObj.AddComponent<EndlessSurvival.World.POI.EnemyCampSpawner>();
                }

                var campList = new List<GameObject>();
                if (campfirePrefab != null) campList.Add(campfirePrefab);
                if (outpostPrefab != null) campList.Add(outpostPrefab);
                campSpawner.campPrefabs = campList.ToArray();
                campSpawner.spawnChance = 1.0f;

                PrefabUtility.SaveAsPrefabAsset(chunkGo, path);
            }
            PrefabUtility.UnloadPrefabContents(chunkGo);
        }
    }

    // ==========================================
    // YARDIMCI METODLAR
    // ==========================================
    private static GameObject CreatePrimitiveChild(Transform parent, PrimitiveType type, string name, Vector3 localPos, Vector3 localRot, Vector3 localScale, Material mat)
    {
        GameObject obj = GameObject.CreatePrimitive(type);
        obj.name = name;
        obj.transform.SetParent(parent, false);
        obj.transform.localPosition = localPos;
        obj.transform.localRotation = Quaternion.Euler(localRot);
        obj.transform.localScale = localScale;
        
        var renderer = obj.GetComponent<Renderer>();
        if (renderer != null && mat != null)
        {
            renderer.sharedMaterial = mat;
        }
        return obj;
    }

    private static Material GetOrCreateMaterial(string name, Color color, Color? emission = null, float smoothness = 0.2f)
    {
        string path = $"Assets/Materials/{name}.mat";
        Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            mat = new Material(shader);
            mat.color = color;
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", smoothness);
            if (emission.HasValue)
            {
                mat.EnableKeyword("_EMISSION");
                mat.SetColor("_EmissionColor", emission.Value);
            }
            AssetDatabase.CreateAsset(mat, path);
        }
        else
        {
            mat.color = color;
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", smoothness);
            if (emission.HasValue)
            {
                mat.EnableKeyword("_EMISSION");
                mat.SetColor("_EmissionColor", emission.Value);
            }
            EditorUtility.SetDirty(mat);
        }
        return mat;
    }

    private static DialogueSet GetOrCreateOutpostDialogue()
    {
        string path = "Assets/Resources/Dialogues/Dialogue_Outpost.asset";
        var ds = AssetDatabase.LoadAssetAtPath<DialogueSet>(path);
        if (ds == null)
        {
            ds = ScriptableObject.CreateInstance<DialogueSet>();
            ds.lines = new DialogueLine[]
            {
                new DialogueLine { speakerIndex = 0, text = "Barikatları sağlamlaştırın, bu gece sis çok yoğun.", duration = 3.5f },
                new DialogueLine { speakerIndex = 1, text = "Kulübedeki cephaneyi kontrol ettin mi?", duration = 3.0f },
                new DialogueLine { speakerIndex = 0, text = "Her şey yerinde. Gözünü yoldan ayırma yeter.", duration = 3.5f }
            };
            ds.pauseBetweenLines = 1.0f;
            AssetDatabase.CreateAsset(ds, path);
        }
        return ds;
    }

    private static void EnsureFolder(string path)
    {
        if (!AssetDatabase.IsValidFolder(path))
        {
            string parent = System.IO.Path.GetDirectoryName(path).Replace("\\", "/");
            string folder = System.IO.Path.GetFileName(path);
            AssetDatabase.CreateFolder(parent, folder);
        }
    }
}

