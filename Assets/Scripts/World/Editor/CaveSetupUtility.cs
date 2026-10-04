using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using EndlessSurvival.Story;
using EndlessSurvival.World;
using EndlessSurvival.World.POI;
using EndlessSurvival.World.Traps;
using EndlessSurvival.Inventory;
using EndlessCombat.AI;

namespace EndlessSurvival.World.Editor
{
    public static class CaveSetupUtility
    {
        [InitializeOnLoadMethod]
        private static void AutoRunSetupOnce()
        {
            if (!EditorPrefs.GetBool("Cave_Setup_Subterranean_v5", false))
            {
                EditorPrefs.SetBool("Cave_Setup_Subterranean_v5", true);
                EditorApplication.delayCall += () =>
                {
                    SetupCaveEvent();
                };
            }
        }

        [MenuItem("Endless Survival/Setup Cave Event")]
        [MenuItem("Tools/Setup Cave Event")]
        public static void SetupCaveEvent()
        {
            Debug.Log("<color=cyan>[CaveSetup] Gizemli Mağara Hikaye Eventi Kurulumu Başlatılıyor...</color>");

            EnsureFolder("Assets/Materials");
            EnsureFolder("Assets/Resources");
            EnsureFolder("Assets/Resources/Notes");
            EnsureFolder("Assets/Prefabs");
            EnsureFolder("Assets/Prefabs/PointsOfInterests");
            EnsureFolder("Assets/Data");
            EnsureFolder("Assets/Data/Events");

            // 1. Not Asset'ini Oluştur
            var noteAsset = CreateMinerNoteAsset();

            // 2. Materyalleri Oluştur
            Material rockMat = GetOrCreateMaterial("Assets/Materials/Mat_Cave_Rock.mat", new Color(0.20f, 0.20f, 0.22f));
            Material dirtMat = GetOrCreateMaterial("Assets/Materials/Mat_Cave_Dirt.mat", new Color(0.26f, 0.18f, 0.12f));
            Material woodMat = GetOrCreateMaterial("Assets/Materials/Mat_Cave_Wood.mat", new Color(0.35f, 0.22f, 0.14f));
            Material ironMat = GetOrCreateMaterial("Assets/Materials/Mat_Cave_Iron.mat", new Color(0.18f, 0.18f, 0.19f));
            Material fireMat = GetOrCreateMaterial("Assets/Materials/Mat_Cave_Fire.mat", new Color(1f, 0.45f, 0.1f), true, Color.yellow * 4f);
            Material beamMat = GetOrCreateMaterial("Assets/Materials/Mat_Cave_LightShaft.mat", new Color(1f, 0.95f, 0.7f, 0.25f));

            // 3. Mağara POI Prefab'ını İnşa Et
            string prefabPath = "Assets/Prefabs/PointsOfInterests/CavePOI.prefab";
            GameObject caveRoot = BuildCaveHierarchy(rockMat, dirtMat, woodMat, ironMat, fireMat, beamMat, noteAsset);

            GameObject savedPrefab = PrefabUtility.SaveAsPrefabAsset(caveRoot, prefabPath);
            GameObject.DestroyImmediate(caveRoot);
            Debug.Log($"<color=green>[CaveSetup] Prefab kaydedildi: {prefabPath}</color>");

            // 4. StoryEventDefinition ScriptableObject Oluştur
            string eventAssetPath = "Assets/Data/Events/Event_Cave.asset";
            var eventDef = AssetDatabase.LoadAssetAtPath<StoryEventDefinition>(eventAssetPath);
            if (eventDef == null)
            {
                eventDef = ScriptableObject.CreateInstance<StoryEventDefinition>();
                AssetDatabase.CreateAsset(eventDef, eventAssetPath);
            }

            eventDef.eventId = "Event_Cave";
            eventDef.eventName = "Terk Edilmiş Maden Mağarası";
            eventDef.eventPrefab = savedPrefab;
            eventDef.spawnChance = 0.35f;
            eventDef.minChunkInterval = 3;
            eventDef.minChunkIndex = 1;
            eventDef.lateralDistance = 42f;
            eventDef.placementZone = POIPlacementZone.Roadside;
            eventDef.isAllowedOnRoad = false;
            eventDef.orientAwayFromRoad = true;
            eventDef.minRoadDistance = 36f;
            EditorUtility.SetDirty(eventDef);

            // 5. Chunk Prefab'ına StoryEventSpawner ile Entegre Et
            AttachSpawnerToChunkPrefab(eventDef);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("<color=green>[CaveSetup] KURULUM TAMAMLANDI! Gizemli Mağara eventi; girişi çöken tüneli, gergin tuzak teli, nöbetçi düşmanları, ganimet sandığı ve çıkış tüneliyle oyuna eklendi.</color>");

            if (!Application.isBatchMode)
            {
                EditorUtility.DisplayDialog("Mağara Eventi Hazır", 
                    "Gizemli Mağara Eventi (The Cave POI) başarıyla kuruldu!\n\nÖzellikler:\n- Çöken kaya girişi (içeride mahsur kalma)\n- Çömelerek altından geçilebilen veya [E] ile imha edilen tuzak teli\n- İçeride ateş başında bekleyen 2 düşman (Gizlilik AI aktif)\n- Değerli ganimet sandığı ve hikaye günlüğü\n- Dağın yamacından dışarı çıkan gizli kurtuluş tüneli", 
                    "Harika");
            }
        }

        private static NoteDefinition CreateMinerNoteAsset()
        {
            string path = "Assets/Resources/Notes/Note_CaveSurvivor.asset";
            var note = AssetDatabase.LoadAssetAtPath<NoteDefinition>(path);
            if (note == null)
            {
                note = ScriptableObject.CreateInstance<NoteDefinition>();
                AssetDatabase.CreateAsset(note, path);
            }

            note.title = "Terk Edilmiş Madenin Son Günlüğü";
            note.author = "Kıdemli Madenci Harun";
            note.bodyText = "Giriş çöktükten sonra dışarıyla tüm bağımız koptu. Askeri konvoyların sesi de sustu. Günlerdir yerin altında kazıyoruz ama dışarıdan sadece uğultular geliyor.\n\n" +
                            "Yiyeceğimiz azaldıkça içeridekiler deliriyor. Giriş koridoruna patlayıcı tuzak teli gerdik; artık kimseye güvenimiz kalmadı. Eğer buraya kadar gelebildiysen, çömelerek tuzakları geçmeyi akıl etmişsin demektir.\n\n" +
                            "Kilerdeki cephaneyi ve konserve yiyecekleri al. Kuzeybatı galerisindeki dar çatlaktan tırman; dışarıdaki dağ yamacına, yola çıkan bir patika var.\n\n" +
                            "Yukarıda hala yaşayan birileri kaldı mı, bilmiyorum...";
            note.noteId = "NOTE_CAVE_MINERS";
            EditorUtility.SetDirty(note);
            return note;
        }

        private static GameObject BuildCaveHierarchy(Material rockMat, Material dirtMat, Material woodMat, Material ironMat, Material fireMat, Material beamMat, NoteDefinition noteAsset)
        {
            GameObject root = new GameObject("CavePOI");
            var poi = root.AddComponent<CavePOI>();
            poi.spawnChance = 1f;
            poi.placementZone = POIPlacementZone.Roadside;
            poi.minRoadClearance = 36f;

            // ==========================================
            // 1. DAĞ VE DIŞ KAYALIK GÖVDE (Mountain Shell)
            // ==========================================
            GameObject mountainShell = new GameObject("MountainShell");
            mountainShell.transform.SetParent(root.transform, false);

            // Dış dağ kütleleri (Mağaranın üstünü örten devasa kayalık sırt)
            Vector3[] mountainRockPositions = new Vector3[]
            {
                new Vector3(-7f, 4f, 4f), new Vector3(7f, 4f, 4f),
                new Vector3(-11f, 5f, 16f), new Vector3(11f, 5f, 16f),
                new Vector3(-12f, 7f, 28f), new Vector3(12f, 7f, 28f),
                new Vector3(0f, 8f, 26f), new Vector3(0f, 6.5f, 12f),
                new Vector3(9f, 5f, 42f), new Vector3(-6f, 6f, 40f),
                new Vector3(0f, 6f, 48f), new Vector3(-8f, 4f, 52f)
            };

            for (int i = 0; i < mountainRockPositions.Length; i++)
            {
                GameObject rock = GameObject.CreatePrimitive(PrimitiveType.Cube);
                rock.name = $"MountainRock_{i}";
                rock.transform.SetParent(mountainShell.transform, false);
                rock.transform.localPosition = mountainRockPositions[i];
                rock.transform.localRotation = Quaternion.Euler(15f * (i % 3), 35f * i, 10f * (i % 2));
                rock.transform.localScale = new Vector3(11f, 9f, 12f);
                rock.GetComponent<Renderer>().sharedMaterial = rockMat;
            }

            // ==========================================
            // 2. GİRİŞ VE İNİŞ TÜNELİ BÖLÜMÜ (Descent Tunnel)
            // ==========================================
            GameObject entranceTunnel = new GameObject("EntranceTunnel");
            entranceTunnel.transform.SetParent(root.transform, false);

            // 2a. Giriş Eşiği (Z: 0 to 4m, Y = 0 - Yol Seviyesi)
            GameObject mouthFloor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            mouthFloor.name = "MouthFloor";
            mouthFloor.transform.SetParent(entranceTunnel.transform, false);
            mouthFloor.transform.localPosition = new Vector3(0, -0.1f, 2f);
            mouthFloor.transform.localScale = new Vector3(5.2f, 0.2f, 4f);
            mouthFloor.GetComponent<Renderer>().sharedMaterial = dirtMat;

            GameObject mouthWallL = GameObject.CreatePrimitive(PrimitiveType.Cube);
            mouthWallL.name = "MouthWall_L";
            mouthWallL.transform.SetParent(entranceTunnel.transform, false);
            mouthWallL.transform.localPosition = new Vector3(-2.6f, 1.8f, 2f);
            mouthWallL.transform.localScale = new Vector3(0.5f, 3.8f, 4f);
            mouthWallL.GetComponent<Renderer>().sharedMaterial = rockMat;

            GameObject mouthWallR = GameObject.CreatePrimitive(PrimitiveType.Cube);
            mouthWallR.name = "MouthWall_R";
            mouthWallR.transform.SetParent(entranceTunnel.transform, false);
            mouthWallR.transform.localPosition = new Vector3(2.6f, 1.8f, 2f);
            mouthWallR.transform.localScale = new Vector3(0.5f, 3.8f, 4f);
            mouthWallR.GetComponent<Renderer>().sharedMaterial = rockMat;

            GameObject mouthCeiling = GameObject.CreatePrimitive(PrimitiveType.Cube);
            mouthCeiling.name = "MouthCeiling";
            mouthCeiling.transform.SetParent(entranceTunnel.transform, false);
            mouthCeiling.transform.localPosition = new Vector3(0, 3.8f, 2f);
            mouthCeiling.transform.localScale = new Vector3(5.6f, 0.4f, 4f);
            mouthCeiling.GetComponent<Renderer>().sharedMaterial = rockMat;

            // Girişteki zemin güvenlik tetikleyicisi (Yeraltına inince TerrainCollider'ı devre dışı bırakır)
            GameObject enterTriggerObj = new GameObject("EntranceZoneTrigger");
            enterTriggerObj.transform.SetParent(entranceTunnel.transform, false);
            enterTriggerObj.transform.localPosition = new Vector3(0, 1.5f, 1.5f);
            var enterTriggerCol = enterTriggerObj.AddComponent<BoxCollider>();
            enterTriggerCol.isTrigger = true;
            enterTriggerCol.size = new Vector3(4.8f, 3.5f, 2.5f);
            var enterZone = enterTriggerObj.AddComponent<CaveZoneTrigger>();
            enterZone.isExit = false;

            // 2b. Eğimli İniş Şaftı (Z: 4m to 18m, Y: 0 to -6.5m, Slope ~25 derece)
            Quaternion descentRot = Quaternion.Euler(24.9f, 0, 0);

            GameObject descentFloor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            descentFloor.name = "DescentFloor";
            descentFloor.transform.SetParent(entranceTunnel.transform, false);
            descentFloor.transform.localPosition = new Vector3(0, -3.25f, 11f);
            descentFloor.transform.localRotation = descentRot;
            descentFloor.transform.localScale = new Vector3(5.2f, 0.25f, 15.5f);
            descentFloor.GetComponent<Renderer>().sharedMaterial = dirtMat;

            GameObject descentWallL = GameObject.CreatePrimitive(PrimitiveType.Cube);
            descentWallL.name = "DescentWall_L";
            descentWallL.transform.SetParent(entranceTunnel.transform, false);
            descentWallL.transform.localPosition = new Vector3(-2.6f, -1.45f, 11f);
            descentWallL.transform.localRotation = descentRot;
            descentWallL.transform.localScale = new Vector3(0.5f, 3.9f, 15.5f);
            descentWallL.GetComponent<Renderer>().sharedMaterial = rockMat;

            GameObject descentWallR = GameObject.CreatePrimitive(PrimitiveType.Cube);
            descentWallR.name = "DescentWall_R";
            descentWallR.transform.SetParent(entranceTunnel.transform, false);
            descentWallR.transform.localPosition = new Vector3(2.6f, -1.45f, 11f);
            descentWallR.transform.localRotation = descentRot;
            descentWallR.transform.localScale = new Vector3(0.5f, 3.9f, 15.5f);
            descentWallR.GetComponent<Renderer>().sharedMaterial = rockMat;

            GameObject descentCeiling = GameObject.CreatePrimitive(PrimitiveType.Cube);
            descentCeiling.name = "DescentCeiling";
            descentCeiling.transform.SetParent(entranceTunnel.transform, false);
            descentCeiling.transform.localPosition = new Vector3(0, 0.5f, 11f);
            descentCeiling.transform.localRotation = descentRot;
            descentCeiling.transform.localScale = new Vector3(5.6f, 0.4f, 15.5f);
            descentCeiling.GetComponent<Renderer>().sharedMaterial = rockMat;

            // Ahşap Maden Destek Kirişleri (İniş boyunca yerleştirilir)
            CreateMineSupportStrut(entranceTunnel.transform, new Vector3(0, -1.4f, 7f), descentRot, woodMat);
            CreateMineSupportStrut(entranceTunnel.transform, new Vector3(0, -3.25f, 11f), descentRot, woodMat);
            CreateMineSupportStrut(entranceTunnel.transform, new Vector3(0, -5.1f, 15f), descentRot, woodMat);

            // Girişteki Paslı Maden Tabelası / Fener
            GameObject lanternObj = new GameObject("EntranceLantern");
            lanternObj.transform.SetParent(entranceTunnel.transform, false);
            lanternObj.transform.localPosition = new Vector3(2.0f, 2.2f, 0.8f);
            var lanternLight = lanternObj.AddComponent<Light>();
            lanternLight.type = LightType.Point;
            lanternLight.range = 8f;
            lanternLight.intensity = 2f;
            lanternLight.color = new Color(1f, 0.7f, 0.35f);

            // ==========================================
            // 3. GİRİŞ ÇÖKME TETİKLEYİCİSİ (CaveCollapseTrigger)
            // ==========================================
            GameObject collapseTriggerObj = new GameObject("CollapseTriggerZone");
            collapseTriggerObj.transform.SetParent(entranceTunnel.transform, false);
            collapseTriggerObj.transform.localPosition = new Vector3(0, -0.6f, 5.5f);

            var triggerCol = collapseTriggerObj.AddComponent<BoxCollider>();
            triggerCol.isTrigger = true;
            triggerCol.size = new Vector3(4.8f, 3.2f, 2.5f);

            var collapseComp = collapseTriggerObj.AddComponent<CaveCollapseTrigger>();

            // Çökünce girişi kapatacak kayalar (BouldersBlocker)
            GameObject bouldersBlocker = new GameObject("Boulders_Blocker");
            bouldersBlocker.transform.SetParent(entranceTunnel.transform, false);
            bouldersBlocker.transform.localPosition = new Vector3(0, 0, 1.2f);

            Vector3[] boulderOffsets = new Vector3[]
            {
                new Vector3(-1.2f, 0.9f, 0f), new Vector3(0f, 1.0f, 0.2f), new Vector3(1.3f, 0.8f, -0.1f),
                new Vector3(-0.6f, 2.0f, 0.1f), new Vector3(0.7f, 2.1f, -0.1f), new Vector3(0.1f, 2.8f, 0f)
            };

            for (int b = 0; b < boulderOffsets.Length; b++)
            {
                GameObject bObj = GameObject.CreatePrimitive(PrimitiveType.Cube);
                bObj.name = $"Boulder_{b}";
                bObj.transform.SetParent(bouldersBlocker.transform, false);
                bObj.transform.localPosition = boulderOffsets[b];
                bObj.transform.localRotation = Quaternion.Euler(20f * b, 45f * b, 15f * b);
                bObj.transform.localScale = new Vector3(1.9f, 1.4f, 1.6f);
                bObj.GetComponent<Renderer>().sharedMaterial = rockMat;
            }

            bouldersBlocker.SetActive(false);
            collapseComp.bouldersBlocker = bouldersBlocker;
            poi.collapseTrigger = collapseComp;

            // ==========================================
            // 4. TUZAK TELİ BÖLÜMÜ (TripwireTrap)
            // ==========================================
            GameObject tripwireObj = new GameObject("TripwireTrap_Zone");
            tripwireObj.transform.SetParent(entranceTunnel.transform, false);
            tripwireObj.transform.localPosition = new Vector3(0, -4.5f, 14.5f);
            tripwireObj.transform.localRotation = descentRot;

            var tripwireComp = tripwireObj.AddComponent<TripwireTrap>();
            tripwireComp.damage = 35f;

            var tripCol = tripwireObj.AddComponent<BoxCollider>();
            tripCol.isTrigger = true;
            tripCol.size = new Vector3(4.8f, 0.8f, 0.8f);

            // Tel Görseli
            GameObject wireVisual = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            wireVisual.name = "WireLine";
            wireVisual.transform.SetParent(tripwireObj.transform, false);
            wireVisual.transform.localPosition = Vector3.zero;
            wireVisual.transform.localRotation = Quaternion.Euler(0, 0, 90f);
            wireVisual.transform.localScale = new Vector3(0.015f, 2.3f, 0.015f);
            wireVisual.GetComponent<Renderer>().sharedMaterial = ironMat;
            var wireCol = wireVisual.GetComponent<Collider>();
            if (wireCol != null) Object.DestroyImmediate(wireCol);

            // Yan Duvar İmhâ Pini
            GameObject pinVisual = GameObject.CreatePrimitive(PrimitiveType.Cube);
            pinVisual.name = "DisarmPin";
            pinVisual.transform.SetParent(tripwireObj.transform, false);
            pinVisual.transform.localPosition = new Vector3(-2.25f, 0f, 0f);
            pinVisual.transform.localScale = new Vector3(0.2f, 0.25f, 0.25f);
            pinVisual.GetComponent<Renderer>().sharedMaterial = ironMat;

            // Yanıp Sönen Kırmızı Işık
            GameObject warnLightObj = new GameObject("WarningLight");
            warnLightObj.transform.SetParent(pinVisual.transform, false);
            warnLightObj.transform.localPosition = new Vector3(0.15f, 0, 0);
            var warnLight = warnLightObj.AddComponent<Light>();
            warnLight.type = LightType.Point;
            warnLight.range = 2.5f;
            warnLight.color = Color.red;

            tripwireComp.wireVisual = wireVisual;
            tripwireComp.pinVisual = pinVisual;
            tripwireComp.warningLight = warnLight;
            poi.tripwire = tripwireComp;

            // ==========================================
            // 5. ANA YERALTI MAĞARA ODASI (Subterranean Cavern Chamber at Y = -6.5m)
            // ==========================================
            GameObject cavernChamber = new GameObject("MainCavernChamber");
            cavernChamber.transform.SetParent(root.transform, false);

            // Yeraltı Mağara Zemini (Y = -6.6m, Z: 18 to 38m, X: -9 to +9)
            GameObject cavernFloor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cavernFloor.name = "CavernFloor";
            cavernFloor.transform.SetParent(cavernChamber.transform, false);
            cavernFloor.transform.localPosition = new Vector3(0, -6.6f, 28f);
            cavernFloor.transform.localScale = new Vector3(18f, 0.25f, 21f);
            cavernFloor.GetComponent<Renderer>().sharedMaterial = dirtMat;

            // Çevreleyen Yüksek Mağara Duvarları (Yükseklik 7.5m)
            GameObject cavWallL = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cavWallL.name = "CavernWall_Left";
            cavWallL.transform.SetParent(cavernChamber.transform, false);
            cavWallL.transform.localPosition = new Vector3(-9.1f, -3.0f, 28f);
            cavWallL.transform.localScale = new Vector3(0.6f, 7.5f, 21f);
            cavWallL.GetComponent<Renderer>().sharedMaterial = rockMat;

            GameObject cavWallR = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cavWallR.name = "CavernWall_Right";
            cavWallR.transform.SetParent(cavernChamber.transform, false);
            cavWallR.transform.localPosition = new Vector3(9.1f, -3.0f, 28f);
            cavWallR.transform.localScale = new Vector3(0.6f, 7.5f, 21f);
            cavWallR.GetComponent<Renderer>().sharedMaterial = rockMat;

            GameObject cavWallBack = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cavWallBack.name = "CavernWall_Back";
            cavWallBack.transform.SetParent(cavernChamber.transform, false);
            cavWallBack.transform.localPosition = new Vector3(-2f, -3.0f, 38.6f);
            cavWallBack.transform.localScale = new Vector3(14.5f, 7.5f, 0.6f);
            cavWallBack.GetComponent<Renderer>().sharedMaterial = rockMat;

            GameObject cavWallFrontL = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cavWallFrontL.name = "CavernWall_FrontL";
            cavWallFrontL.transform.SetParent(cavernChamber.transform, false);
            cavWallFrontL.transform.localPosition = new Vector3(-5.8f, -3.0f, 17.5f);
            cavWallFrontL.transform.localScale = new Vector3(6.8f, 7.5f, 0.6f);
            cavWallFrontL.GetComponent<Renderer>().sharedMaterial = rockMat;

            GameObject cavWallFrontR = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cavWallFrontR.name = "CavernWall_FrontR";
            cavWallFrontR.transform.SetParent(cavernChamber.transform, false);
            cavWallFrontR.transform.localPosition = new Vector3(5.8f, -3.0f, 17.5f);
            cavWallFrontR.transform.localScale = new Vector3(6.8f, 7.5f, 0.6f);
            cavWallFrontR.GetComponent<Renderer>().sharedMaterial = rockMat;

            // Yeraltı Mağara Tavanı (Y = 0.6f)
            GameObject cavCeiling = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cavCeiling.name = "CavernCeiling";
            cavCeiling.transform.SetParent(cavernChamber.transform, false);
            cavCeiling.transform.localPosition = new Vector3(0, 0.6f, 28f);
            cavCeiling.transform.localScale = new Vector3(19f, 0.5f, 22f);
            cavCeiling.GetComponent<Renderer>().sharedMaterial = rockMat;

            // Tavandan Tabana Uzanan Doğal Kaya Sütunları
            GameObject pillar1 = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            pillar1.name = "RockPillar_1";
            pillar1.transform.SetParent(cavernChamber.transform, false);
            pillar1.transform.localPosition = new Vector3(-4f, -3.0f, 24f);
            pillar1.transform.localScale = new Vector3(2.5f, 3.8f, 2.5f);
            pillar1.GetComponent<Renderer>().sharedMaterial = rockMat;

            GameObject pillar2 = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            pillar2.name = "RockPillar_2";
            pillar2.transform.SetParent(cavernChamber.transform, false);
            pillar2.transform.localPosition = new Vector3(4f, -3.0f, 27f);
            pillar2.transform.localScale = new Vector3(2.8f, 3.8f, 2.8f);
            pillar2.GetComponent<Renderer>().sharedMaterial = rockMat;

            // Ortadaki Ateş / Varil Ateşi
            GameObject firePit = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            firePit.name = "FireBarrel";
            firePit.transform.SetParent(cavernChamber.transform, false);
            firePit.transform.localPosition = new Vector3(0, -6.05f, 26f);
            firePit.transform.localScale = new Vector3(1.1f, 0.45f, 1.1f);
            firePit.GetComponent<Renderer>().sharedMaterial = ironMat;

            GameObject fireEmber = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            fireEmber.name = "Embers";
            fireEmber.transform.SetParent(firePit.transform, false);
            fireEmber.transform.localPosition = new Vector3(0, 0.6f, 0);
            fireEmber.transform.localScale = new Vector3(0.85f, 0.4f, 0.85f);
            fireEmber.GetComponent<Renderer>().sharedMaterial = fireMat;

            GameObject fireLightObj = new GameObject("CavernFireLight");
            fireLightObj.transform.SetParent(cavernChamber.transform, false);
            fireLightObj.transform.localPosition = new Vector3(0, -4.8f, 26f);
            var fireLight = fireLightObj.AddComponent<Light>();
            fireLight.type = LightType.Point;
            fireLight.range = 18f;
            fireLight.intensity = 3.5f;
            fireLight.color = new Color(1f, 0.58f, 0.22f);
            poi.cavernFireLight = fireLight;

            // ==========================================
            // 6. DÜŞMANLAR (2 Cave Scavenger)
            // ==========================================
            var enemyPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Enemies/Enemy.prefab")
                           ?? AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Enemy.prefab");
            poi.enemyPrefab = enemyPrefab;

            // Muhafız Noktaları (Yeraltı Galerisi Seviyesi Y = -6.5m)
            GameObject spotSitting = new GameObject("SittingGuardSpot");
            spotSitting.transform.SetParent(cavernChamber.transform, false);
            spotSitting.transform.localPosition = new Vector3(-1.6f, -6.5f, 25f);
            spotSitting.transform.localRotation = Quaternion.Euler(0, 70f, 0);
            poi.sittingGuardSpot = spotSitting.transform;

            GameObject spotStanding = new GameObject("StandingGuardSpot");
            spotStanding.transform.SetParent(cavernChamber.transform, false);
            spotStanding.transform.localPosition = new Vector3(3.2f, -6.5f, 29f);
            spotStanding.transform.localRotation = Quaternion.Euler(0, 200f, 0);
            poi.standingGuardSpot = spotStanding.transform;

            List<EnemyController> guards = new List<EnemyController>();
            if (enemyPrefab != null)
            {
                GameObject guard1 = PrefabUtility.InstantiatePrefab(enemyPrefab, cavernChamber.transform) as GameObject;
                if (guard1 != null)
                {
                    guard1.name = "CaveScavenger_Sitting";
                    guard1.transform.position = spotSitting.transform.position;
                    guard1.transform.rotation = spotSitting.transform.rotation;
                    var ec1 = guard1.GetComponent<EnemyController>();
                    if (ec1 != null)
                    {
                        ec1.initialState = EnemyController.EnemyState.Sitting;
                        guards.Add(ec1);
                    }
                }

                GameObject guard2 = PrefabUtility.InstantiatePrefab(enemyPrefab, cavernChamber.transform) as GameObject;
                if (guard2 != null)
                {
                    guard2.name = "CaveScavenger_Guard";
                    guard2.transform.position = spotStanding.transform.position;
                    guard2.transform.rotation = spotStanding.transform.rotation;
                    var ec2 = guard2.GetComponent<EnemyController>();
                    if (ec2 != null)
                    {
                        ec2.initialState = EnemyController.EnemyState.Idle;
                        guards.Add(ec2);
                    }
                }
            }
            poi.caveGuards = guards.ToArray();

            // ==========================================
            // 7. GANİMET KASASI VE HİKAYE NOTU (Loot & Note)
            // ==========================================
            GameObject lootCache = new GameObject("LootCacheZone");
            lootCache.transform.SetParent(cavernChamber.transform, false);
            lootCache.transform.localPosition = new Vector3(-6.2f, -6.5f, 32f);

            GameObject table = GameObject.CreatePrimitive(PrimitiveType.Cube);
            table.name = "SupplyTable";
            table.transform.SetParent(lootCache.transform, false);
            table.transform.localPosition = new Vector3(0, 0.45f, 0);
            table.transform.localScale = new Vector3(2.2f, 0.9f, 1.4f);
            table.GetComponent<Renderer>().sharedMaterial = woodMat;

            var chestPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Loot/Chest_Cube.prefab")
                           ?? AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Loot/DefaultChest.prefab");
            if (chestPrefab != null)
            {
                GameObject chestInstance = PrefabUtility.InstantiatePrefab(chestPrefab, lootCache.transform) as GameObject;
                if (chestInstance != null)
                {
                    chestInstance.transform.localPosition = new Vector3(0f, 0.92f, 0f);
                    chestInstance.transform.localRotation = Quaternion.Euler(0, 180f, 0);
                }
            }

            var notePrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Story/InteractableNote.prefab");
            if (notePrefab != null)
            {
                GameObject noteInstance = PrefabUtility.InstantiatePrefab(notePrefab, lootCache.transform) as GameObject;
                if (noteInstance != null)
                {
                    noteInstance.name = "Note_CaveMiners";
                    noteInstance.transform.localPosition = new Vector3(1.2f, 0.92f, 0f);
                    var noteComp = noteInstance.GetComponent<InteractableNote>();
                    if (noteComp != null)
                    {
                        noteComp.noteDefinition = noteAsset;
                        poi.caveNote = noteComp;
                    }
                }
            }

            // ==========================================
            // 8. YERYÜZÜNE ÇIKAN KURTULUŞ TÜNELİ (Ascent Exit Tunnel To Surface)
            // ==========================================
            GameObject exitTunnel = new GameObject("ExitPassage");
            exitTunnel.transform.SetParent(root.transform, false);

            Quaternion exitSlopeRot = Quaternion.Euler(-25.5f, 8f, 0f);

            // Tırmanış Rampası Zemini (Y: -6.5m'den Y: +0.2m'ye tırmanır)
            GameObject exitRamp = GameObject.CreatePrimitive(PrimitiveType.Cube);
            exitRamp.name = "ExitRamp";
            exitRamp.transform.SetParent(exitTunnel.transform, false);
            exitRamp.transform.localPosition = new Vector3(6.5f, -3.15f, 45f);
            exitRamp.transform.localRotation = exitSlopeRot;
            exitRamp.transform.localScale = new Vector3(4.2f, 0.25f, 15.6f);
            exitRamp.GetComponent<Renderer>().sharedMaterial = dirtMat;

            // Çıkış Tüneli Duvarları
            GameObject exitWallL = GameObject.CreatePrimitive(PrimitiveType.Cube);
            exitWallL.name = "ExitWall_Left";
            exitWallL.transform.SetParent(exitTunnel.transform, false);
            exitWallL.transform.localPosition = new Vector3(4.4f, -1.35f, 45f);
            exitWallL.transform.localRotation = exitSlopeRot;
            exitWallL.transform.localScale = new Vector3(0.5f, 3.8f, 15.6f);
            exitWallL.GetComponent<Renderer>().sharedMaterial = rockMat;

            GameObject exitWallR = GameObject.CreatePrimitive(PrimitiveType.Cube);
            exitWallR.name = "ExitWall_Right";
            exitWallR.transform.SetParent(exitTunnel.transform, false);
            exitWallR.transform.localPosition = new Vector3(8.6f, -1.35f, 45f);
            exitWallR.transform.localRotation = exitSlopeRot;
            exitWallR.transform.localScale = new Vector3(0.5f, 3.8f, 15.6f);
            exitWallR.GetComponent<Renderer>().sharedMaterial = rockMat;

            // Çıkış Tüneli Tavanı
            GameObject exitCeiling = GameObject.CreatePrimitive(PrimitiveType.Cube);
            exitCeiling.name = "ExitCeiling";
            exitCeiling.transform.SetParent(exitTunnel.transform, false);
            exitCeiling.transform.localPosition = new Vector3(6.5f, 0.5f, 45f);
            exitCeiling.transform.localRotation = exitSlopeRot;
            exitCeiling.transform.localScale = new Vector3(4.8f, 0.4f, 15.6f);
            exitCeiling.GetComponent<Renderer>().sharedMaterial = rockMat;

            // Çıkıştaki zemin güvenlik tetikleyicisi (Yeryüzüne çıkınca TerrainCollider'ı tekrar normale döndürür)
            GameObject exitTriggerObj = new GameObject("ExitZoneTrigger");
            exitTriggerObj.transform.SetParent(exitTunnel.transform, false);
            exitTriggerObj.transform.localPosition = new Vector3(7.5f, 0.2f, 51.5f);
            var exitTriggerCol = exitTriggerObj.AddComponent<BoxCollider>();
            exitTriggerCol.isTrigger = true;
            exitTriggerCol.size = new Vector3(4.5f, 3.5f, 2.5f);
            var exitZone = exitTriggerObj.AddComponent<CaveZoneTrigger>();
            exitZone.isExit = true;

            // Dışarıdan vuran gün ışığı demeti (Sunlight Shaft)
            GameObject sunBeamObj = new GameObject("ExitSunbeam");
            sunBeamObj.transform.SetParent(exitTunnel.transform, false);
            sunBeamObj.transform.localPosition = new Vector3(8.0f, 4.5f, 52.5f);
            sunBeamObj.transform.localRotation = Quaternion.Euler(55f, -150f, 0);

            var sunLight = sunBeamObj.AddComponent<Light>();
            sunLight.type = LightType.Spot;
            sunLight.range = 24f;
            sunLight.spotAngle = 48f;
            sunLight.intensity = 5.0f;
            sunLight.color = new Color(1f, 0.95f, 0.8f);

            poi.entrancePoint = entranceTunnel.transform;
            poi.exitPoint = exitTunnel.transform;

            return root;
        }

        private static void CreateMineSupportStrut(Transform parent, Vector3 localPos, Quaternion localRot, Material woodMat)
        {
            GameObject strutRoot = new GameObject($"MineStrut_{localPos.z}");
            strutRoot.transform.SetParent(parent, false);
            strutRoot.transform.localPosition = localPos;
            strutRoot.transform.localRotation = localRot;

            // Sol Direk
            GameObject postL = GameObject.CreatePrimitive(PrimitiveType.Cube);
            postL.name = "Post_L";
            postL.transform.SetParent(strutRoot.transform, false);
            postL.transform.localPosition = new Vector3(-2.2f, 1.8f, 0f);
            postL.transform.localScale = new Vector3(0.25f, 3.6f, 0.25f);
            postL.GetComponent<Renderer>().sharedMaterial = woodMat;

            // Sağ Direk
            GameObject postR = GameObject.CreatePrimitive(PrimitiveType.Cube);
            postR.name = "Post_R";
            postR.transform.SetParent(strutRoot.transform, false);
            postR.transform.localPosition = new Vector3(2.2f, 1.8f, 0f);
            postR.transform.localScale = new Vector3(0.25f, 3.6f, 0.25f);
            postR.GetComponent<Renderer>().sharedMaterial = woodMat;

            // Üst Kiriş
            GameObject beam = GameObject.CreatePrimitive(PrimitiveType.Cube);
            beam.name = "TopBeam";
            beam.transform.SetParent(strutRoot.transform, false);
            beam.transform.localPosition = new Vector3(0f, 3.55f, 0f);
            beam.transform.localScale = new Vector3(4.65f, 0.25f, 0.25f);
            beam.GetComponent<Renderer>().sharedMaterial = woodMat;
        }

        private static void AttachSpawnerToChunkPrefab(StoryEventDefinition eventDef)
        {
            string chunkPrefabPath = "Assets/Prefabs/Chunks/Chunk_Forest_Curve.prefab";
            var chunkPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(chunkPrefabPath);
            if (chunkPrefab == null)
            {
                Debug.LogWarning($"[CaveSetup] {chunkPrefabPath} bulunamadı!");
                return;
            }

            var rootObj = PrefabUtility.LoadPrefabContents(chunkPrefabPath);
            var spawner = rootObj.GetComponentInChildren<StoryEventSpawner>();
            if (spawner == null)
            {
                GameObject spawnerObj = new GameObject("StoryEventSpawner");
                spawnerObj.transform.SetParent(rootObj.transform, false);
                spawner = spawnerObj.AddComponent<StoryEventSpawner>();
            }

            spawner.roadSpline = rootObj.GetComponentInChildren<EndlessSurvival.World.Road.RoadSpline>();

            // Event listesini güncelle
            var list = new List<StoryEventDefinition>();
            if (spawner.availableEvents != null)
            {
                list.AddRange(spawner.availableEvents);
            }
            if (!list.Contains(eventDef))
            {
                list.Add(eventDef);
            }
            spawner.availableEvents = list.ToArray();

            PrefabUtility.SaveAsPrefabAsset(rootObj, chunkPrefabPath);
            PrefabUtility.UnloadPrefabContents(rootObj);
            Debug.Log($"<color=cyan>[CaveSetup] StoryEventSpawner başarıyla '{chunkPrefabPath}' içine güncellendi (Mağara eventi eklendi).</color>");
        }

        private static Material GetOrCreateMaterial(string path, Color col, bool emissive = false, Color emissiveColor = default)
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                mat = new Material(shader) { color = col };
                if (emissive)
                {
                    mat.EnableKeyword("_EMISSION");
                    mat.SetColor("_EmissionColor", emissiveColor);
                }
                AssetDatabase.CreateAsset(mat, path);
            }
            return mat;
        }

        private static void EnsureFolder(string path)
        {
            if (!AssetDatabase.IsValidFolder(path))
            {
                int idx = path.LastIndexOf('/');
                string parent = path.Substring(0, idx);
                string folder = path.Substring(idx + 1);
                AssetDatabase.CreateFolder(parent, folder);
            }
        }
    }
}
