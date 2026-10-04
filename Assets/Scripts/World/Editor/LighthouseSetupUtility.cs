using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using EndlessSurvival.Story;
using EndlessSurvival.World;
using EndlessSurvival.World.POI;
using EndlessSurvival.Inventory;

namespace EndlessSurvival.World.Editor
{
    public static class LighthouseSetupUtility
    {
        [InitializeOnLoadMethod]
        private static void AutoRunSetupOnce()
        {
            if (!EditorPrefs.GetBool("Lighthouse_Setup_Completed_v2", false))
            {
                EditorPrefs.SetBool("Lighthouse_Setup_Completed_v2", true);
                EditorApplication.delayCall += () =>
                {
                    SetupLighthouseEvent();
                };
            }
        }

        [MenuItem("Endless Survival/Setup Lighthouse Event")]
        [MenuItem("Tools/Setup Lighthouse Event")]
        public static void SetupLighthouseEvent()
        {
            Debug.Log("<color=cyan>[LighthouseSetup] Deniz Feneri Hikaye Eventi Kurulumu Başlatılıyor...</color>");

            EnsureFolder("Assets/Materials");
            EnsureFolder("Assets/Resources");
            EnsureFolder("Assets/Resources/Dialogues");
            EnsureFolder("Assets/Resources/Notes");
            EnsureFolder("Assets/Resources/Story");
            EnsureFolder("Assets/Prefabs");
            EnsureFolder("Assets/Prefabs/PointsOfInterests");
            EnsureFolder("Assets/Prefabs/Story");
            EnsureFolder("Assets/Data");
            EnsureFolder("Assets/Data/Events");

            // 1. Not ve Diyalog Asset'lerini Oluştur
            var noteAsset = CreateKeeperNoteAsset();
            var dialogueAsset = CreateKeeperDialogueAsset();

            // 2. Materyalleri Oluştur
            Material whiteMat = GetOrCreateMaterial("Assets/Materials/Mat_Lighthouse_White.mat", new Color(0.85f, 0.85f, 0.85f));
            Material redMat = GetOrCreateMaterial("Assets/Materials/Mat_Lighthouse_Red.mat", new Color(0.72f, 0.18f, 0.15f));
            Material lanternMat = GetOrCreateMaterial("Assets/Materials/Mat_Lighthouse_Lantern.mat", new Color(1f, 0.85f, 0.4f), true, Color.yellow * 4f);
            Material glassMat = GetOrCreateMaterial("Assets/Materials/Mat_Lighthouse_Glass.mat", new Color(0.2f, 0.3f, 0.4f, 0.6f));
            Material woodMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Mat_Camp_Wood.mat") ?? whiteMat;
            Material ironMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Mat_Camp_Iron.mat") ?? redMat;
            Material keeperMat = GetOrCreateMaterial("Assets/Materials/Mat_Keeper_Suit.mat", new Color(0.12f, 0.22f, 0.38f));

            // Thomas NPC Prefab'ını İnsansı Karakter Modeli ile Oluştur
            GameObject thomasPrefab = CreateOrGetThomasPrefab(keeperMat, dialogueAsset);

            // 3. Deniz Feneri Prefab'ını İnşa Et
            string prefabPath = "Assets/Prefabs/PointsOfInterests/LighthousePOI.prefab";
            GameObject lighthouseRoot = BuildLighthouseHierarchy(whiteMat, redMat, lanternMat, glassMat, woodMat, ironMat, noteAsset, dialogueAsset, thomasPrefab);

            GameObject savedPrefab = PrefabUtility.SaveAsPrefabAsset(lighthouseRoot, prefabPath);
            GameObject.DestroyImmediate(lighthouseRoot);
            Debug.Log($"<color=green>[LighthouseSetup] Prefab kaydedildi: {prefabPath}</color>");

            // 4. StoryEventDefinition ScriptableObject Oluştur
            string eventAssetPath = "Assets/Data/Events/Event_Lighthouse.asset";
            var eventDef = AssetDatabase.LoadAssetAtPath<StoryEventDefinition>(eventAssetPath);
            if (eventDef == null)
            {
                eventDef = ScriptableObject.CreateInstance<StoryEventDefinition>();
                AssetDatabase.CreateAsset(eventDef, eventAssetPath);
            }

            eventDef.eventId = "Event_Lighthouse";
            eventDef.eventName = "Yalnız Deniz Feneri";
            eventDef.eventPrefab = savedPrefab;
            eventDef.spawnChance = 0.38f;
            eventDef.minChunkInterval = 3;
            eventDef.lateralDistance = 55f;
            eventDef.placementZone = POIPlacementZone.OffRoad;
            eventDef.isAllowedOnRoad = false;
            eventDef.orientAwayFromRoad = false;
            eventDef.minRoadDistance = 48f;
            EditorUtility.SetDirty(eventDef);

            // 5. Chunk Prefab'ına StoryEventSpawner Entegre Et
            AttachSpawnerToChunkPrefab(eventDef);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("<color=green>[LighthouseSetup] KURULUM TAMAMLANDI! Deniz feneri eventi, bekçi Thomas, günlüğü ve dönen ışığıyla dünyada belirmeye hazır.</color>");
        }

        private static NoteDefinition CreateKeeperNoteAsset()
        {
            string path = "Assets/Resources/Notes/Note_LighthouseKeeper.asset";
            var note = AssetDatabase.LoadAssetAtPath<NoteDefinition>(path);
            if (note == null)
            {
                note = ScriptableObject.CreateInstance<NoteDefinition>();
                AssetDatabase.CreateAsset(note, path);
            }

            note.title = "Fener Bekçisinin Son Seyir Defteri";
            note.author = "Fener Bekçisi Thomas";
            note.bodyText = "14 Ekim - Sis Günü 412\n\n" +
                            "Deniz çekileli aylar oldu. Artık dalga sesleri yerine sadece asfaltın uğultusu ve rüzgarın fısıltısı var. Kuzeye giden araç konvoyları da bitti. Kalan son askeri devriyeler de kamplarına çekilip bir daha görünmediler.\n\n" +
                            "Her gece bu feneri yakmaya devam ediyorum. Belki karanlıkta yönünü kaybetmiş bir yolcu, benim gibi hayatta kalmaya çalışan bir ruh görür de buraya sığınır diye.\n\n" +
                            "Eğer bu satırları okuyorsan yabancı; kulübedeki yakıtı ve suyu al. İlerideki dağ geçitleri düşman kamplarıyla dolu. Ateşini yakmadan ve arabanı kontrol etmeden sakın yola çıkma. Işık yolunu aydınlatsın.";
            note.noteId = "NOTE_LIGHTHOUSE_KEEPER";
            EditorUtility.SetDirty(note);
            return note;
        }

        private static DialogueSet CreateKeeperDialogueAsset()
        {
            string path = "Assets/Resources/Dialogues/Dialogue_LighthouseKeeper.asset";
            var set = AssetDatabase.LoadAssetAtPath<DialogueSet>(path);
            if (set == null)
            {
                set = ScriptableObject.CreateInstance<DialogueSet>();
                AssetDatabase.CreateAsset(set, path);
            }

            set.setName = "Fener Bekçisi Thomas Diyalogları";
            set.pauseBetweenLines = 1.5f;
            set.lines = new DialogueLine[]
            {
                new DialogueLine { text = "Işık hiç sönmemeli... Yıllardır bu yoldan kimse geçmedi sanıyordum.", duration = 4.5f, speakerIndex = 0 },
                new DialogueLine { text = "Dünya sustuğunda deniz de çekildi; geriye sadece bu sonsuz sis ve asfalt kaldı.", duration = 5f, speakerIndex = 0 },
                new DialogueLine { text = "Arabana iyi bak yolcu. Bu yolda durmak ölüm demektir.", duration = 4.5f, speakerIndex = 0 },
                new DialogueLine { text = "Masanın üstünde eski seyir defterimi bıraktım. Neler olduğunu merak ediyorsan oku.", duration = 5f, speakerIndex = 0 },
                new DialogueLine { text = "Kulübede biraz yakıt ve temiz su var. Yolculuğunda işine yarar, çekinmeden al.", duration = 5f, speakerIndex = 0 }
            };
            EditorUtility.SetDirty(set);
            return set;
        }

        private static GameObject BuildLighthouseHierarchy(Material whiteMat, Material redMat, Material lanternMat, Material glassMat, Material woodMat, Material ironMat, NoteDefinition note, DialogueSet dialogue, GameObject thomasPrefab)
        {
            GameObject root = new GameObject("LighthousePOI");
            var poi = root.AddComponent<LighthousePOI>();
            poi.spawnChance = 1f;
            poi.placementZone = POIPlacementZone.OffRoad;
            poi.minRoadClearance = 48f;

            // 1. Zemin Kulübesi (Keeper's Cabin) - Açık Kapılı ve İçine Girilebilir
            GameObject cabin = new GameObject("KeepersCabin");
            cabin.transform.SetParent(root.transform, false);

            // Ahşap Zemin
            GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.name = "CabinFloor";
            floor.transform.SetParent(cabin.transform, false);
            floor.transform.localPosition = new Vector3(-3.25f, 0.05f, 0f);
            floor.transform.localScale = new Vector3(6.5f, 0.1f, 5.2f);
            floor.GetComponent<Renderer>().sharedMaterial = woodMat;

            // Arka Duvar (Z = -2.5f)
            GameObject wallBack = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wallBack.name = "Wall_Back";
            wallBack.transform.SetParent(cabin.transform, false);
            wallBack.transform.localPosition = new Vector3(-3.25f, 1.75f, -2.5f);
            wallBack.transform.localScale = new Vector3(6.5f, 3.5f, 0.2f);
            wallBack.GetComponent<Renderer>().sharedMaterial = whiteMat;

            // Sol Dış Duvar (X = -6.4f)
            GameObject wallLeft = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wallLeft.name = "Wall_Left";
            wallLeft.transform.SetParent(cabin.transform, false);
            wallLeft.transform.localPosition = new Vector3(-6.4f, 1.75f, 0f);
            wallLeft.transform.localScale = new Vector3(0.2f, 3.5f, 5.0f);
            wallLeft.GetComponent<Renderer>().sharedMaterial = whiteMat;

            // Sağ Duvar (Kuleye bakan cephe, X = -0.1f)
            GameObject wallRight = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wallRight.name = "Wall_Right";
            wallRight.transform.SetParent(cabin.transform, false);
            wallRight.transform.localPosition = new Vector3(-0.1f, 1.75f, 0f);
            wallRight.transform.localScale = new Vector3(0.2f, 3.5f, 5.0f);
            wallRight.GetComponent<Renderer>().sharedMaterial = whiteMat;

            // Ön Duvar - Giriş Kapısı Boşluğu Olan Parçalı Duvar (Z = +2.5f)
            // Sol Ön Duvar Paneli
            GameObject wallFrontLeft = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wallFrontLeft.name = "Wall_Front_Left";
            wallFrontLeft.transform.SetParent(cabin.transform, false);
            wallFrontLeft.transform.localPosition = new Vector3(-5.325f, 1.75f, 2.5f);
            wallFrontLeft.transform.localScale = new Vector3(2.35f, 3.5f, 0.2f);
            wallFrontLeft.GetComponent<Renderer>().sharedMaterial = whiteMat;

            // Sağ Ön Duvar Paneli
            GameObject wallFrontRight = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wallFrontRight.name = "Wall_Front_Right";
            wallFrontRight.transform.SetParent(cabin.transform, false);
            wallFrontRight.transform.localPosition = new Vector3(-1.175f, 1.75f, 2.5f);
            wallFrontRight.transform.localScale = new Vector3(2.35f, 3.5f, 0.2f);
            wallFrontRight.GetComponent<Renderer>().sharedMaterial = whiteMat;

            // Kapı Üstü Lento Paneli (Yükseklik 2.4m - 3.5m arası)
            GameObject wallFrontLintel = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wallFrontLintel.name = "Wall_Front_Lintel";
            wallFrontLintel.transform.SetParent(cabin.transform, false);
            wallFrontLintel.transform.localPosition = new Vector3(-3.25f, 2.95f, 2.5f);
            wallFrontLintel.transform.localScale = new Vector3(1.8f, 1.1f, 0.2f);
            wallFrontLintel.GetComponent<Renderer>().sharedMaterial = whiteMat;

            // Kapı Çerçevesi Ahşap Detayı (Giriş hissini güçlendirmek için)
            GameObject doorFrameLeft = GameObject.CreatePrimitive(PrimitiveType.Cube);
            doorFrameLeft.name = "DoorFrame_Left";
            doorFrameLeft.transform.SetParent(cabin.transform, false);
            doorFrameLeft.transform.localPosition = new Vector3(-4.15f, 1.2f, 2.52f);
            doorFrameLeft.transform.localScale = new Vector3(0.12f, 2.4f, 0.25f);
            doorFrameLeft.GetComponent<Renderer>().sharedMaterial = woodMat;
            GameObject.DestroyImmediate(doorFrameLeft.GetComponent<Collider>());

            GameObject doorFrameRight = GameObject.CreatePrimitive(PrimitiveType.Cube);
            doorFrameRight.name = "DoorFrame_Right";
            doorFrameRight.transform.SetParent(cabin.transform, false);
            doorFrameRight.transform.localPosition = new Vector3(-2.35f, 1.2f, 2.52f);
            doorFrameRight.transform.localScale = new Vector3(0.12f, 2.4f, 0.25f);
            doorFrameRight.GetComponent<Renderer>().sharedMaterial = woodMat;
            GameObject.DestroyImmediate(doorFrameRight.GetComponent<Collider>());

            GameObject doorFrameTop = GameObject.CreatePrimitive(PrimitiveType.Cube);
            doorFrameTop.name = "DoorFrame_Top";
            doorFrameTop.transform.SetParent(cabin.transform, false);
            doorFrameTop.transform.localPosition = new Vector3(-3.25f, 2.4f, 2.52f);
            doorFrameTop.transform.localScale = new Vector3(1.92f, 0.14f, 0.25f);
            doorFrameTop.GetComponent<Renderer>().sharedMaterial = woodMat;
            GameObject.DestroyImmediate(doorFrameTop.GetComponent<Collider>());

            // Kulübe Çatısı
            GameObject roof = GameObject.CreatePrimitive(PrimitiveType.Cube);
            roof.name = "CabinRoof";
            roof.transform.SetParent(cabin.transform, false);
            roof.transform.localPosition = new Vector3(-3.25f, 3.6f, 0f);
            roof.transform.localScale = new Vector3(6.9f, 0.25f, 5.6f);
            roof.GetComponent<Renderer>().sharedMaterial = redMat;

            // Kulübe İçi Sıcak Tavan Işığı
            GameObject cabinWarmLight = new GameObject("CabinInteriorWarmLight");
            cabinWarmLight.transform.SetParent(cabin.transform, false);
            cabinWarmLight.transform.localPosition = new Vector3(-3.25f, 2.7f, 0f);
            var interiorLight = cabinWarmLight.AddComponent<Light>();
            interiorLight.type = LightType.Point;
            interiorLight.range = 9f;
            interiorLight.intensity = 2.2f;
            interiorLight.color = new Color(1f, 0.82f, 0.5f);

            // 2. Ana Fener Kulesi (16m Yüksekliğinde Kademeli Silindirler)
            GameObject towerRoot = new GameObject("Tower");
            towerRoot.transform.SetParent(root.transform, false);
            towerRoot.transform.localPosition = new Vector3(2.5f, 0f, 0f);

            // Alt Taban Taş Halka
            GameObject towerBase = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            towerBase.name = "TowerBase";
            towerBase.transform.SetParent(towerRoot.transform, false);
            towerBase.transform.localPosition = new Vector3(0, 1.5f, 0);
            towerBase.transform.localScale = new Vector3(5.5f, 1.5f, 5.5f);
            towerBase.GetComponent<Renderer>().sharedMaterial = ironMat;

            // Gövde Katmanları (Beyaz ve Kırmızı Çizgili Deniz Feneri Deseni)
            float currentHeight = 3f;
            int segments = 5;
            float segmentHeight = 2.6f;
            float baseRadius = 4.8f;
            float topRadius = 3.6f;

            for (int i = 0; i < segments; i++)
            {
                float t = (float)i / (segments - 1);
                float radius = Mathf.Lerp(baseRadius, topRadius, t);
                Material segMat = (i % 2 == 0) ? whiteMat : redMat;

                GameObject seg = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                seg.name = $"TowerSegment_{i}";
                seg.transform.SetParent(towerRoot.transform, false);
                seg.transform.localPosition = new Vector3(0, currentHeight + segmentHeight * 0.5f, 0);
                seg.transform.localScale = new Vector3(radius, segmentHeight * 0.5f, radius);
                seg.GetComponent<Renderer>().sharedMaterial = segMat;

                currentHeight += segmentHeight;
            }

            // 3. Balkon / Gözlem Platformu
            GameObject balcony = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            balcony.name = "Balcony";
            balcony.transform.SetParent(towerRoot.transform, false);
            balcony.transform.localPosition = new Vector3(0, currentHeight + 0.2f, 0);
            balcony.transform.localScale = new Vector3(5f, 0.2f, 5f);
            balcony.GetComponent<Renderer>().sharedMaterial = ironMat;

            // Balkon Korkuluk Halkası
            GameObject railing = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            railing.name = "Railing";
            railing.transform.SetParent(towerRoot.transform, false);
            railing.transform.localPosition = new Vector3(0, currentHeight + 0.8f, 0);
            railing.transform.localScale = new Vector3(4.9f, 0.6f, 4.9f);
            railing.GetComponent<Renderer>().sharedMaterial = ironMat;
            GameObject.DestroyImmediate(railing.GetComponent<Collider>());

            currentHeight += 0.5f;

            // 4. Fener Odası (Lantern Room) & Cam Muhafaza
            GameObject lanternRoom = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            lanternRoom.name = "LanternRoom_Glass";
            lanternRoom.transform.SetParent(towerRoot.transform, false);
            lanternRoom.transform.localPosition = new Vector3(0, currentHeight + 1.25f, 0);
            lanternRoom.transform.localScale = new Vector3(3.2f, 1.25f, 3.2f);
            lanternRoom.GetComponent<Renderer>().sharedMaterial = glassMat;

            // Fener Kubbe Tepesi (Kırmızı Çatı)
            GameObject dome = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            dome.name = "LanternDome";
            dome.transform.SetParent(towerRoot.transform, false);
            dome.transform.localPosition = new Vector3(0, currentHeight + 2.7f, 0);
            dome.transform.localScale = new Vector3(3.4f, 1.5f, 3.4f);
            dome.GetComponent<Renderer>().sharedMaterial = redMat;

            // 5. DÖNEN FENER IŞIK BAŞLIĞI
            GameObject rotHead = new GameObject("RotatingLanternHead");
            rotHead.transform.SetParent(towerRoot.transform, false);
            rotHead.transform.localPosition = new Vector3(0, currentHeight + 1.25f, 0);

            // Kor Işık Lambası
            GameObject bulb = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            bulb.name = "LanternCore";
            bulb.transform.SetParent(rotHead.transform, false);
            bulb.transform.localScale = new Vector3(1.2f, 1.2f, 1.2f);
            bulb.GetComponent<Renderer>().sharedMaterial = lanternMat;
            GameObject.DestroyImmediate(bulb.GetComponent<Collider>());

            // Spot Işığı Demeti (Spotlight Beam)
            GameObject beamObj = new GameObject("SearchlightBeam");
            beamObj.transform.SetParent(rotHead.transform, false);
            beamObj.transform.localPosition = new Vector3(0, 0, 0.4f);
            beamObj.transform.localRotation = Quaternion.Euler(14f, 0f, 0f); // Hafif aşağı doğru baksın
            var spot = beamObj.AddComponent<Light>();
            spot.type = LightType.Spot;
            spot.spotAngle = 38f;
            spot.innerSpotAngle = 22f;
            spot.range = 100f;
            spot.intensity = 6.5f;
            spot.color = new Color(1f, 0.94f, 0.78f);
            spot.shadows = LightShadows.Soft;

            // Çevreleyen Sıcak Ortam Işığı
            GameObject ambientLightObj = new GameObject("AmbientLanternLight");
            ambientLightObj.transform.SetParent(towerRoot.transform, false);
            ambientLightObj.transform.localPosition = new Vector3(0, currentHeight + 1.25f, 0);
            var pLight = ambientLightObj.AddComponent<Light>();
            pLight.type = LightType.Point;
            pLight.range = 16f;
            pLight.intensity = 2.5f;
            pLight.color = new Color(1f, 0.75f, 0.35f);

            poi.rotatingLanternHead = rotHead.transform;
            poi.spotLightBeam = spot;
            poi.ambientLanternLight = pLight;

            // 6. KULÜBE İÇİ MOBİLYA, NOT VE GANİMET
            // Masa
            GameObject table = GameObject.CreatePrimitive(PrimitiveType.Cube);
            table.name = "Desk";
            table.transform.SetParent(root.transform, false);
            table.transform.localPosition = new Vector3(-4.8f, 0.45f, -1.2f);
            table.transform.localScale = new Vector3(1.8f, 0.75f, 1.0f);
            table.GetComponent<Renderer>().sharedMaterial = woodMat;

            // Masa Üstü Gaz Lambası
            GameObject lamp = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            lamp.name = "TableOilLamp";
            lamp.transform.SetParent(root.transform, false);
            lamp.transform.localPosition = new Vector3(-5.3f, 1.05f, -1.2f);
            lamp.transform.localScale = new Vector3(0.2f, 0.35f, 0.2f);
            lamp.GetComponent<Renderer>().sharedMaterial = lanternMat;
            GameObject.DestroyImmediate(lamp.GetComponent<Collider>());

            var lampLight = lamp.AddComponent<Light>();
            lampLight.type = LightType.Point;
            lampLight.range = 5f;
            lampLight.intensity = 1.5f;
            lampLight.color = new Color(1f, 0.65f, 0.25f);

            // Sandalye
            GameObject chair = GameObject.CreatePrimitive(PrimitiveType.Cube);
            chair.name = "Chair";
            chair.transform.SetParent(root.transform, false);
            chair.transform.localPosition = new Vector3(-4.8f, 0.4f, -0.4f);
            chair.transform.localScale = new Vector3(0.5f, 0.8f, 0.5f);
            chair.GetComponent<Renderer>().sharedMaterial = woodMat;

            // Masa Üstündeki Fener Bekçisinin Günlüğü (InteractableNote)
            var notePrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Story/InteractableNote.prefab");
            if (notePrefab != null)
            {
                GameObject noteInstance = PrefabUtility.InstantiatePrefab(notePrefab, root.transform) as GameObject;
                if (noteInstance != null)
                {
                    noteInstance.transform.localPosition = new Vector3(-4.6f, 0.88f, -1.2f);
                    var interNote = noteInstance.GetComponent<InteractableNote>();
                    if (interNote != null)
                    {
                        interNote.noteDefinition = note;
                        poi.keeperNote = interNote;
                    }
                }
            }

            // Ganimet Sandığı (Yakıt, Su, Erzak)
            var chestPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Loot/Chest_Cube.prefab")
                           ?? AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Loot/DefaultChest.prefab");
            if (chestPrefab != null)
            {
                GameObject chestInstance = PrefabUtility.InstantiatePrefab(chestPrefab, root.transform) as GameObject;
                if (chestInstance != null)
                {
                    chestInstance.transform.localPosition = new Vector3(-5.2f, 0.25f, 1.0f);
                    chestInstance.transform.localRotation = Quaternion.Euler(0, 45f, 0);
                }
            }

            // 7. FENER BEKÇİSİ THOMAS NPC'Sİ (İnsansı Karakter Modeli ile)
            GameObject keeperObj = null;
            if (thomasPrefab != null)
            {
                keeperObj = PrefabUtility.InstantiatePrefab(thomasPrefab, root.transform) as GameObject;
            }
            if (keeperObj == null)
            {
                keeperObj = new GameObject("LighthouseKeeper_Thomas");
                keeperObj.transform.SetParent(root.transform, false);
            }

            keeperObj.name = "LighthouseKeeper_Thomas";
            keeperObj.transform.localPosition = new Vector3(-2.0f, 0f, 3.2f); // Açık kapı önü karşılama konumu
            keeperObj.transform.localRotation = Quaternion.Euler(0, 160f, 0);

            var keeperComp = keeperObj.GetComponent<LighthouseKeeperNPC>();
            if (keeperComp == null) keeperComp = keeperObj.AddComponent<LighthouseKeeperNPC>();
            keeperComp.npcName = "Fener Bekçisi Thomas";
            keeperComp.dialogueSet = dialogue;
            keeperComp.speechBubblePrefab = Resources.Load<SpeechBubble>("UI/SpeechBubble");

            poi.keeper = keeperComp;

            return root;
        }

        private static GameObject CreateOrGetThomasPrefab(Material keeperMat, DialogueSet dialogue)
        {
            string prefabPath = "Assets/Prefabs/Story/NPC_Thomas.prefab";
            EnsureFolder("Assets/Prefabs/Story");

            string playerArmaturePath = "Assets/Starter Assets/Runtime/ThirdPersonController/Prefabs/PlayerArmature.prefab";
            var playerArmaturePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(playerArmaturePath);
            if (playerArmaturePrefab == null)
            {
                Debug.LogWarning("[LighthouseSetup] PlayerArmature prefab bulunamadı!");
                return null;
            }

            GameObject tempInstance = Object.Instantiate(playerArmaturePrefab);
            tempInstance.name = "NPC_Thomas";
            tempInstance.tag = "Untagged";

            // Oyuncu kontrol ve hayatta kalma scriptlerini temizle (sadece NPC davranışı kalsın)
            string[] componentsToRemove = new string[]
            {
                "ThirdPersonController",
                "StarterAssetsInputs",
                "PlayerInput",
                "CharacterController",
                "PlayerShooter",
                "PlayerHealth",
                "PlayerSurvival",
                "PlayerCampPlacer"
            };

            foreach (var compName in componentsToRemove)
            {
                var comp = tempInstance.GetComponent(compName);
                if (comp != null) Object.DestroyImmediate(comp);
            }

            var audioListener = tempInstance.GetComponentInChildren<AudioListener>();
            if (audioListener != null) Object.DestroyImmediate(audioListener);

            var camRoot = tempInstance.transform.Find("PlayerCameraRoot");
            if (camRoot != null) Object.DestroyImmediate(camRoot.gameObject);

            // Animator: StarterAssetsThirdPerson.controller varsayılan olarak Idle durumunda bekler
            var animator = tempInstance.GetComponent<Animator>();
            if (animator != null)
            {
                animator.applyRootMotion = false;
            }

            // SkinnedMeshRenderer materyallerini denizci lacivert palto materyaline dönüştür
            var renderers = tempInstance.GetComponentsInChildren<SkinnedMeshRenderer>();
            foreach (var r in renderers)
            {
                Material[] mats = new Material[r.sharedMaterials.Length];
                for (int i = 0; i < mats.Length; i++)
                {
                    mats[i] = keeperMat;
                }
                r.sharedMaterials = mats;
            }

            // Etkileşim ve gövde collider'ı
            var col = tempInstance.GetComponent<CapsuleCollider>();
            if (col == null) col = tempInstance.AddComponent<CapsuleCollider>();
            col.center = new Vector3(0, 0.9f, 0);
            col.radius = 0.4f;
            col.height = 1.8f;

            // LighthouseKeeperNPC bileşeni
            var keeperComp = tempInstance.GetComponent<LighthouseKeeperNPC>();
            if (keeperComp == null) keeperComp = tempInstance.AddComponent<LighthouseKeeperNPC>();
            keeperComp.npcName = "Fener Bekçisi Thomas";
            keeperComp.dialogueSet = dialogue;
            keeperComp.speechBubblePrefab = Resources.Load<SpeechBubble>("UI/SpeechBubble");

            // Prefab kaydet
            GameObject savedPrefab = PrefabUtility.SaveAsPrefabAsset(tempInstance, prefabPath);
            Object.DestroyImmediate(tempInstance);

            // Resources klasörüne de kopyala (Runtime dinamik yüklemeleri için)
            EnsureFolder("Assets/Resources/Story");
            string resPath = "Assets/Resources/Story/NPC_Thomas.prefab";
            if (AssetDatabase.LoadAssetAtPath<GameObject>(resPath) != null)
            {
                AssetDatabase.DeleteAsset(resPath);
            }
            AssetDatabase.CopyAsset(prefabPath, resPath);

            Debug.Log($"<color=green>[LighthouseSetup] Thomas NPC humanoid prefab kaydedildi: {prefabPath}</color>");
            return savedPrefab;
        }

        private static void AttachSpawnerToChunkPrefab(StoryEventDefinition eventDef)
        {
            string chunkPrefabPath = "Assets/Prefabs/Chunks/Chunk_Forest_Curve.prefab";
            var chunkPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(chunkPrefabPath);
            if (chunkPrefab == null)
            {
                Debug.LogWarning($"[LighthouseSetup] {chunkPrefabPath} bulunamadı!");
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
            Debug.Log($"<color=cyan>[LighthouseSetup] StoryEventSpawner başarıyla '{chunkPrefabPath}' içine entegre edildi.</color>");
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
