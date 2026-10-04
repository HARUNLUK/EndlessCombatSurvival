using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using EndlessSurvival.Inventory;
using EndlessSurvival.Camping;
using EndlessSurvival.World.Editor;

namespace EndlessSurvival.Camping.Editor
{
    public static class PlayerCampSetupUtility
    {
        [MenuItem("Endless Survival/Setup Player Camping")]
        [MenuItem("Tools/Setup Player Camping")]
        public static void SetupPlayerCamping()
        {
            Debug.Log("<color=cyan>[CampingSetup] Oyuncu Kamp ve Hayatta Kalma Sistemi Kurulumu Başlatılıyor...</color>");

            EnsureFolder("Assets/Prefabs");
            EnsureFolder("Assets/Prefabs/Camping");
            EnsureFolder("Assets/Data");
            EnsureFolder("Assets/Data/Items");
            EnsureFolder("Assets/Materials");

            // 0. Oturma Animasyonunu Entegre Et (Camping Sitting Idle)
            AnimationIntegrator.IntegrateSittingAnimation();

            // 1. Yeni Pişmiş Yemek / İçecek Asset'lerini Oluştur
            CreateCookedItems();

            // 2. Oyuncu Kamp Ateşi Prefab'ını Oluştur / Güncelle
            GameObject campfirePrefab = CreatePlayerCampfirePrefab();

            // 3. Açık Sahnedeki Oyuncuyu Yapılandır (PlayerCampPlacer, PlayerNeedReliever)
            SetupScenePlayer(campfirePrefab);

            // 4. Gece/Gündüz Güneş Döngüsü ve TimeHUD Kurulumu
            DayNightSetupUtility.SetupDayNightCycle();

            // 5. Hikayeli Deniz Feneri Eventi Kurulumu
            LighthouseSetupUtility.SetupLighthouseEvent();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("<color=green>[CampingSetup] KURULUM TAMAMLANDI! Oyuncu artık 'B' ile kamp kurabilir, ateşte pişirebilir, dinlenebilir ve 'P' ile ihtiyacını giderebilir.</color>");
        }

        private static void CreateCookedItems()
        {
            CreateItemAsset("Item_CookedMeat", "Pişmiş Et", ItemCategory.Food, 45, 0.4f, 
                "Ateşte taze pişirilmiş, açlığı hızla gideren ve can yenileyen lezzetli et.", new Color(0.6f, 0.25f, 0.1f));

            CreateItemAsset("Item_WarmStew", "Sıcak Güveç", ItemCategory.Food, 40, 0.5f, 
                "Konserve fasulye ve sebzelerle ateşte ısıtılmış doyurucu sıcak güveç.", new Color(0.7f, 0.4f, 0.15f));

            CreateItemAsset("Item_BoiledWater", "Kaynamış Temiz Su", ItemCategory.Drink, 50, 0.5f, 
                "Ateşte kaynatılarak tüm mikroplardan arındırılmış temiz içme suyu.", new Color(0.2f, 0.7f, 0.9f));
        }

        private static void CreateItemAsset(string assetName, string displayName, ItemCategory cat, int useAmount, float weight, string desc, Color color)
        {
            string path = $"Assets/Data/Items/{assetName}.asset";
            var item = AssetDatabase.LoadAssetAtPath<ItemDefinition>(path);
            if (item == null)
            {
                item = ScriptableObject.CreateInstance<ItemDefinition>();
                item.displayName = displayName;
                item.category = cat;
                item.useAmount = useAmount;
                item.weight = weight;
                item.description = desc;
                item.worldColor = color;
                AssetDatabase.CreateAsset(item, path);
                Debug.Log($"[CampingSetup] Eşya oluşturuldu: {displayName} ({path})");
            }
        }

        private static GameObject CreatePlayerCampfirePrefab()
        {
            string prefabPath = "Assets/Prefabs/Camping/PlayerCampfire.prefab";

            // Materyalleri al veya oluştur
            Material woodMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Mat_Camp_Wood.mat");
            Material emberMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Mat_Camp_Embers.mat");
            Material stoneMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Mat_Camp_Stone.mat");
            Material ashMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Mat_Camp_Ash.mat");

            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            if (woodMat == null) woodMat = new Material(shader) { color = new Color(0.35f, 0.2f, 0.1f) };
            if (stoneMat == null) stoneMat = new Material(shader) { color = Color.gray };
            if (ashMat == null) ashMat = new Material(shader) { color = new Color(0.12f, 0.12f, 0.12f) };
            if (emberMat == null)
            {
                emberMat = new Material(shader) { color = Color.red };
                emberMat.EnableKeyword("_EMISSION");
                emberMat.SetColor("_EmissionColor", Color.red * 3f);
            }

            GameObject fireRoot = new GameObject("PlayerCampfire");
            var campComp = fireRoot.AddComponent<PlayerCampfire>();
            campComp.remainingBurnTime = 300f; // 5 dakika
            campComp.maxBurnTime = 900f;
            campComp.warmthRadius = 6.5f;
            campComp.healthRegenPerSecond = 1.0f; // Saniyede 1 HP yavaş can yenileme

            // Çarpışma kutusu (trigger)
            var col = fireRoot.AddComponent<SphereCollider>();
            col.radius = 1.1f;
            col.isTrigger = false;

            // Kül Yatağı
            GameObject ash = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            ash.name = "AshBed";
            ash.transform.SetParent(fireRoot.transform, false);
            ash.transform.localPosition = new Vector3(0, 0.02f, 0);
            ash.transform.localScale = new Vector3(1.4f, 0.04f, 1.4f);
            ash.GetComponent<Renderer>().sharedMaterial = ashMat;
            GameObject.DestroyImmediate(ash.GetComponent<Collider>());

            // Taş Halkası
            int stoneCount = 8;
            float radius = 0.8f;
            for (int i = 0; i < stoneCount; i++)
            {
                float angle = i * Mathf.PI * 2f / stoneCount;
                Vector3 pos = new Vector3(Mathf.Cos(angle) * radius, 0.15f, Mathf.Sin(angle) * radius);
                GameObject stone = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                stone.name = $"Stone_{i}";
                stone.transform.SetParent(fireRoot.transform, false);
                stone.transform.localPosition = pos;
                stone.transform.localScale = new Vector3(0.36f, 0.25f, 0.36f);
                stone.GetComponent<Renderer>().sharedMaterial = stoneMat;
                GameObject.DestroyImmediate(stone.GetComponent<Collider>());
            }

            // Çapraz Odun Kütükleri
            for (int i = 0; i < 5; i++)
            {
                float rotY = i * 72f;
                GameObject log = GameObject.CreatePrimitive(PrimitiveType.Cube);
                log.name = $"Log_{i}";
                log.transform.SetParent(fireRoot.transform, false);
                log.transform.localPosition = new Vector3(0, 0.14f, 0);
                log.transform.localRotation = Quaternion.Euler(18f, rotY, 0);
                log.transform.localScale = new Vector3(0.16f, 0.14f, 0.95f);
                log.GetComponent<Renderer>().sharedMaterial = woodMat;
                GameObject.DestroyImmediate(log.GetComponent<Collider>());
            }

            // Köz Çekirdeği
            GameObject ember = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            ember.name = "EmberCore";
            ember.transform.SetParent(fireRoot.transform, false);
            ember.transform.localPosition = new Vector3(0, 0.2f, 0);
            ember.transform.localScale = new Vector3(0.5f, 0.3f, 0.5f);
            ember.GetComponent<Renderer>().sharedMaterial = emberMat;
            GameObject.DestroyImmediate(ember.GetComponent<Collider>());
            campComp.emberVisual = ember;

            // Ateş Işığı
            GameObject lightObj = new GameObject("FireLight");
            lightObj.transform.SetParent(fireRoot.transform, false);
            lightObj.transform.localPosition = new Vector3(0, 0.8f, 0);
            Light l = lightObj.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = new Color(1.0f, 0.55f, 0.15f);
            l.range = 15f;
            l.intensity = 2.5f;
            campComp.fireLight = l;

            // Prefab olarak kaydet
            GameObject savedPrefab = PrefabUtility.SaveAsPrefabAsset(fireRoot, prefabPath);
            GameObject.DestroyImmediate(fireRoot);
            Debug.Log($"[CampingSetup] PlayerCampfire prefabi kaydedildi: {prefabPath}");
            return savedPrefab;
        }

        private static void SetupScenePlayer(GameObject campfirePrefab)
        {
            var player = GameObject.FindGameObjectWithTag("Player") ?? GameObject.Find("PlayerArmature");
            if (player == null)
            {
                Debug.LogWarning("[CampingSetup] Sahnede 'Player' tag'li obje bulunamadı. Oyuncu bileşenleri daha sonra eklenebilir.");
                return;
            }

            // PlayerCampPlacer
            var placer = player.GetComponent<PlayerCampPlacer>();
            if (placer == null) placer = player.AddComponent<PlayerCampPlacer>();
            placer.placeKey = KeyCode.B;
            placer.campfirePrefab = campfirePrefab;

            // PlayerSittingController (Yerde Oturma)
            var sittingCtrl = player.GetComponent<PlayerSittingController>();
            if (sittingCtrl == null) sittingCtrl = player.AddComponent<PlayerSittingController>();
            sittingCtrl.sitToggleKey = KeyCode.C;

            // PlayerStealthController (Çömelme & Gizlilik)
            var stealthCtrl = player.GetComponent<EndlessCombat.Combat.PlayerStealthController>();
            if (stealthCtrl == null) stealthCtrl = player.AddComponent<EndlessCombat.Combat.PlayerStealthController>();

            // StealthHUD
            var stealthHUD = Object.FindFirstObjectByType<EndlessSurvival.World.StealthHUD>();
            if (stealthHUD == null)
            {
                var hudObj = new GameObject("StealthHUD");
                hudObj.AddComponent<EndlessSurvival.World.StealthHUD>();
            }

            // PlayerNeedReliever
            var reliever = player.GetComponent<PlayerNeedReliever>();
            if (reliever == null) reliever = player.AddComponent<PlayerNeedReliever>();
            reliever.reliefKey = KeyCode.P;

            // PlayerHealth ve Survival bileşenlerini garantiye al
            var health = player.GetComponent<PlayerHealth>();
            if (health != null)
            {
                health.bleedDamagePerSecond = 2f;
                health.bleedChanceOnHit = 0.35f;
            }

            var survival = player.GetComponent<PlayerSurvival>();
            if (survival != null)
            {
                survival.maxBladder = 100f;
                survival.bladderFillPerSecond = 0.05f;
            }

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            Debug.Log("[CampingSetup] Oyuncu 'PlayerCampPlacer', 'PlayerSittingController', 'PlayerStealthController' ve 'PlayerNeedReliever' ile donatıldı.");
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
}
