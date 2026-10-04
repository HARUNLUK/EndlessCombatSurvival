using UnityEngine;
using EndlessSurvival.Story;
using EndlessSurvival.World;

namespace EndlessSurvival.World.POI
{
    /// <summary>
    /// Narrative POI representing an old seaside lighthouse on the cliff edge with a rotating searchlight and NPC keeper.
    /// </summary>
    public class LighthousePOI : PointOfInterest
    {
        [Header("Lantern & Light Settings")]
        [Tooltip("Dönen fener kafası transformu")]
        public Transform rotatingLanternHead;

        [Tooltip("Uzaklara ve gökyüzüne vuran ana spot ışığı demeti")]
        public Light spotLightBeam;

        [Tooltip("Fener odasını ve çevreyi aydınlatan sıcak ortam ışığı")]
        public Light ambientLanternLight;

        [Tooltip("Fenerin saniyedeki dönüş hızı (derece)")]
        public float rotationSpeed = 32f;

        [Header("Narrative References")]
        public LighthouseKeeperNPC keeper;
        public InteractableNote keeperNote;

        [Header("Ambient Sound / Audio")]
        public AudioSource fogHornAudio;

        private float _baseBeamIntensity = 6.5f;

        private void Awake()
        {
            if (rotatingLanternHead == null)
            {
                BuildProceduralVisuals();
            }
        }

        protected override void Start()
        {
            base.Start();
            if (spotLightBeam != null) _baseBeamIntensity = spotLightBeam.intensity;
        }

        private void Update()
        {
            // Fener ışığını döndür
            if (rotatingLanternHead != null)
            {
                rotatingLanternHead.Rotate(Vector3.up, rotationSpeed * Time.deltaTime, Space.World);
            }

            // Gece olunca fener ışığını daha belirgin ve güçlü yap
            if (spotLightBeam != null)
            {
                var cycle = DayNightCycle.Instance;
                bool isNight = cycle != null ? cycle.IsNight : false;
                spotLightBeam.intensity = isNight ? (_baseBeamIntensity * 1.6f) : _baseBeamIntensity;
                spotLightBeam.range = isNight ? 130f : 90f;
            }
        }

        protected override void OnSpawn(SeededRandom rng, Transform container)
        {
            // Seed sistemine göre fenerin başlangıç dönüş açısını belirle
            if (rotatingLanternHead != null)
            {
                rotatingLanternHead.rotation = Quaternion.Euler(14f, rng.Range(0f, 360f), 0f);
            }

            Chunk chunk = GetComponentInParent<Chunk>();
            Terrain terrain = chunk != null ? chunk.ChunkTerrain : null;
            if (terrain != null)
            {
                EndlessSurvival.World.Road.RoadTerrainAdapter.FlattenTerrainArea(terrain, transform.position, 14f, 10f, transform.position.y);
            }

            Debug.Log("<color=cyan>[LighthousePOI] Yalnız Deniz Feneri dünyada belirdi!</color>");
        }

        public void BuildProceduralVisuals()
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");

            Material whiteMat = new Material(shader) { color = new Color(0.85f, 0.85f, 0.85f) };
            Material redMat = new Material(shader) { color = new Color(0.75f, 0.18f, 0.15f) };
            Material ironMat = new Material(shader) { color = new Color(0.18f, 0.19f, 0.22f) };
            Material woodMat = new Material(shader) { color = new Color(0.35f, 0.22f, 0.12f) };

            Material lanternMat = new Material(shader) { color = new Color(1f, 0.85f, 0.4f) };
            lanternMat.EnableKeyword("_EMISSION");
            lanternMat.SetColor("_EmissionColor", Color.yellow * 3.5f);

            Material glassMat = new Material(shader) { color = new Color(0.2f, 0.35f, 0.5f, 0.55f) };

            // 1. Zemin Kulübesi (Keeper's Cabin) - Açık Kapılı ve İçine Girilebilir
            GameObject cabin = new GameObject("KeepersCabin");
            cabin.transform.SetParent(transform, false);

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
            Destroy(doorFrameLeft.GetComponent<Collider>());

            GameObject doorFrameRight = GameObject.CreatePrimitive(PrimitiveType.Cube);
            doorFrameRight.name = "DoorFrame_Right";
            doorFrameRight.transform.SetParent(cabin.transform, false);
            doorFrameRight.transform.localPosition = new Vector3(-2.35f, 1.2f, 2.52f);
            doorFrameRight.transform.localScale = new Vector3(0.12f, 2.4f, 0.25f);
            doorFrameRight.GetComponent<Renderer>().sharedMaterial = woodMat;
            Destroy(doorFrameRight.GetComponent<Collider>());

            GameObject doorFrameTop = GameObject.CreatePrimitive(PrimitiveType.Cube);
            doorFrameTop.name = "DoorFrame_Top";
            doorFrameTop.transform.SetParent(cabin.transform, false);
            doorFrameTop.transform.localPosition = new Vector3(-3.25f, 2.4f, 2.52f);
            doorFrameTop.transform.localScale = new Vector3(1.92f, 0.14f, 0.25f);
            doorFrameTop.GetComponent<Renderer>().sharedMaterial = woodMat;
            Destroy(doorFrameTop.GetComponent<Collider>());

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

            // 2. Kule Gövdesi (16 Metre)
            GameObject towerRoot = new GameObject("Tower");
            towerRoot.transform.SetParent(transform, false);
            towerRoot.transform.localPosition = new Vector3(2.5f, 0f, 0f);

            GameObject towerBase = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            towerBase.name = "TowerBase";
            towerBase.transform.SetParent(towerRoot.transform, false);
            towerBase.transform.localPosition = new Vector3(0, 1.5f, 0);
            towerBase.transform.localScale = new Vector3(5.5f, 1.5f, 5.5f);
            towerBase.GetComponent<Renderer>().sharedMaterial = ironMat;

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

            // 3. Balkon & Korkuluk
            GameObject balcony = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            balcony.name = "Balcony";
            balcony.transform.SetParent(towerRoot.transform, false);
            balcony.transform.localPosition = new Vector3(0, currentHeight + 0.2f, 0);
            balcony.transform.localScale = new Vector3(5.2f, 0.2f, 5.2f);
            balcony.GetComponent<Renderer>().sharedMaterial = ironMat;

            currentHeight += 0.5f;

            // 4. Fener Odası (Cam Muhafaza)
            GameObject lanternRoom = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            lanternRoom.name = "LanternRoom_Glass";
            lanternRoom.transform.SetParent(towerRoot.transform, false);
            lanternRoom.transform.localPosition = new Vector3(0, currentHeight + 1.25f, 0);
            lanternRoom.transform.localScale = new Vector3(3.2f, 1.25f, 3.2f);
            lanternRoom.GetComponent<Renderer>().sharedMaterial = glassMat;

            GameObject dome = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            dome.name = "LanternDome";
            dome.transform.SetParent(towerRoot.transform, false);
            dome.transform.localPosition = new Vector3(0, currentHeight + 2.7f, 0);
            dome.transform.localScale = new Vector3(3.4f, 1.5f, 3.4f);
            dome.GetComponent<Renderer>().sharedMaterial = redMat;

            // 5. DÖNEN FENER IŞIĞI
            GameObject rotHead = new GameObject("RotatingLanternHead");
            rotHead.transform.SetParent(towerRoot.transform, false);
            rotHead.transform.localPosition = new Vector3(0, currentHeight + 1.25f, 0);

            GameObject bulb = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            bulb.name = "LanternCore";
            bulb.transform.SetParent(rotHead.transform, false);
            bulb.transform.localScale = new Vector3(1.2f, 1.2f, 1.2f);
            bulb.GetComponent<Renderer>().sharedMaterial = lanternMat;
            Destroy(bulb.GetComponent<Collider>());

            GameObject beamObj = new GameObject("SearchlightBeam");
            beamObj.transform.SetParent(rotHead.transform, false);
            beamObj.transform.localPosition = new Vector3(0, 0, 0.4f);
            beamObj.transform.localRotation = Quaternion.Euler(14f, 0f, 0f);
            var spot = beamObj.AddComponent<Light>();
            spot.type = LightType.Spot;
            spot.spotAngle = 38f;
            spot.innerSpotAngle = 22f;
            spot.range = 100f;
            spot.intensity = 6.5f;
            spot.color = new Color(1f, 0.94f, 0.78f);
            spot.shadows = LightShadows.Soft;

            GameObject ambientLightObj = new GameObject("AmbientLanternLight");
            ambientLightObj.transform.SetParent(towerRoot.transform, false);
            ambientLightObj.transform.localPosition = new Vector3(0, currentHeight + 1.25f, 0);
            var pLight = ambientLightObj.AddComponent<Light>();
            pLight.type = LightType.Point;
            pLight.range = 16f;
            pLight.intensity = 2.5f;
            pLight.color = new Color(1f, 0.75f, 0.35f);

            rotatingLanternHead = rotHead.transform;
            spotLightBeam = spot;
            ambientLanternLight = pLight;

            // 6. KULÜBE İÇİ MOBİLYA, NOT VE GANİMET
            GameObject table = GameObject.CreatePrimitive(PrimitiveType.Cube);
            table.name = "Desk";
            table.transform.SetParent(transform, false);
            table.transform.localPosition = new Vector3(-4.8f, 0.45f, -1.2f);
            table.transform.localScale = new Vector3(1.8f, 0.75f, 1.0f);
            table.GetComponent<Renderer>().sharedMaterial = woodMat;

            // Masa Üstü Gaz Lambası
            GameObject lamp = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            lamp.name = "TableOilLamp";
            lamp.transform.SetParent(transform, false);
            lamp.transform.localPosition = new Vector3(-5.3f, 1.05f, -1.2f);
            lamp.transform.localScale = new Vector3(0.2f, 0.35f, 0.2f);
            lamp.GetComponent<Renderer>().sharedMaterial = lanternMat;
            Destroy(lamp.GetComponent<Collider>());

            var lampLight = lamp.AddComponent<Light>();
            lampLight.type = LightType.Point;
            lampLight.range = 5f;
            lampLight.intensity = 1.5f;
            lampLight.color = new Color(1f, 0.65f, 0.25f);

            // Sandalye
            GameObject chair = GameObject.CreatePrimitive(PrimitiveType.Cube);
            chair.name = "Chair";
            chair.transform.SetParent(transform, false);
            chair.transform.localPosition = new Vector3(-4.8f, 0.4f, -0.4f);
            chair.transform.localScale = new Vector3(0.5f, 0.8f, 0.5f);
            chair.GetComponent<Renderer>().sharedMaterial = woodMat;

            // Masa Üstündeki Fener Bekçisinin Günlüğü (InteractableNote)
            GameObject noteObj = GameObject.CreatePrimitive(PrimitiveType.Cube);
            noteObj.name = "KeepersJournal";
            noteObj.transform.SetParent(transform, false);
            noteObj.transform.localPosition = new Vector3(-4.6f, 0.88f, -1.2f);
            noteObj.transform.localScale = new Vector3(0.35f, 0.05f, 0.45f);
            noteObj.GetComponent<Renderer>().sharedMaterial = whiteMat;

            var interNote = noteObj.AddComponent<InteractableNote>();
            var noteDef = Resources.Load<NoteDefinition>("Notes/Note_LighthouseKeeper");
#if UNITY_EDITOR
            if (noteDef == null)
            {
                noteDef = UnityEditor.AssetDatabase.LoadAssetAtPath<NoteDefinition>("Assets/Resources/Notes/Note_LighthouseKeeper.asset");
            }
#endif
            if (noteDef == null)
            {
                noteDef = ScriptableObject.CreateInstance<NoteDefinition>();
                noteDef.title = "Fener Bekçisinin Son Seyir Defteri";
                noteDef.author = "Fener Bekçisi Thomas";
                noteDef.bodyText = "14 Ekim - Sis Günü 412\n\nDeniz çekileli aylar oldu. Artık dalga sesleri yerine sadece asfaltın uğultusu ve rüzgarın fısıltısı var. Kuzeye giden araç konvoyları da bitti.\n\nHer gece bu feneri yakmaya devam ediyorum. Belki karanlıkta yönünü kaybetmiş bir yolcu sığınır diye.\n\nEğer bu satırları okuyorsan yabancı; kulübedeki yakıtı ve suyu al. İlerideki dağ geçitleri düşman kamplarıyla dolu. Ateşini yakmadan ve arabanı kontrol etmeden sakın yola çıkma. Işık yolunu aydınlatsın.";
            }
            interNote.noteDefinition = noteDef;
            keeperNote = interNote;

            // Kulübe İçi Ganimet Sandığı
            var chestPrefab = Resources.Load<GameObject>("Loot/Chest_Cube");
#if UNITY_EDITOR
            if (chestPrefab == null)
            {
                chestPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Loot/Chest_Cube.prefab");
            }
#endif
            if (chestPrefab != null)
            {
                GameObject chestInstance = Instantiate(chestPrefab, transform);
                chestInstance.transform.localPosition = new Vector3(-5.2f, 0.25f, 1.0f);
                chestInstance.transform.localRotation = Quaternion.Euler(0, 45f, 0);
            }

            // 7. FENER BEKÇİSİ THOMAS NPC'Sİ (İnsansı Karakter Modeli ile)
            Material keeperMat = new Material(shader) { color = new Color(0.12f, 0.22f, 0.38f) }; // Denizci Lacivert Palto

            GameObject keeperPrefab = Resources.Load<GameObject>("Story/NPC_Thomas");
#if UNITY_EDITOR
            if (keeperPrefab == null)
            {
                keeperPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Story/NPC_Thomas.prefab");
            }
            if (keeperPrefab == null)
            {
                keeperPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Starter Assets/Runtime/ThirdPersonController/Prefabs/PlayerArmature.prefab");
            }
#endif
            GameObject keeperObj = null;
            if (keeperPrefab != null)
            {
                keeperObj = Instantiate(keeperPrefab, transform);
                keeperObj.name = "LighthouseKeeper_Thomas";

                // Eğer PlayerArmature'den geldiyse oyuncu scriptlerini temizle
                string[] removeScripts = new string[]
                {
                    "ThirdPersonController", "StarterAssetsInputs", "PlayerInput",
                    "CharacterController", "PlayerShooter", "PlayerHealth",
                    "PlayerSurvival", "PlayerCampPlacer"
                };
                foreach (var s in removeScripts)
                {
                    var c = keeperObj.GetComponent(s);
                    if (c != null) Destroy(c);
                }

                var camRoot = keeperObj.transform.Find("PlayerCameraRoot");
                if (camRoot != null) Destroy(camRoot.gameObject);

                var audioLis = keeperObj.GetComponentInChildren<AudioListener>();
                if (audioLis != null) Destroy(audioLis);

                // Lacivert denizci palto rengini SkinnedMeshRenderer'lara uygula
                var renderers = keeperObj.GetComponentsInChildren<SkinnedMeshRenderer>();
                foreach (var r in renderers)
                {
                    Material[] mats = new Material[r.sharedMaterials.Length];
                    for (int m = 0; m < mats.Length; m++) mats[m] = keeperMat;
                    r.sharedMaterials = mats;
                }
            }
            else
            {
                // Fallback basit görsel
                keeperObj = new GameObject("LighthouseKeeper_Thomas");
                keeperObj.transform.SetParent(transform, false);
                GameObject body = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                body.transform.SetParent(keeperObj.transform, false);
                body.transform.localPosition = new Vector3(0, 1.0f, 0);
                body.transform.localScale = new Vector3(0.55f, 0.65f, 0.4f);
                body.GetComponent<Renderer>().sharedMaterial = keeperMat;
            }

            // Kapı önünde bekleme pozisyonu
            keeperObj.transform.localPosition = new Vector3(-2.0f, 0f, 3.2f);
            keeperObj.transform.localRotation = Quaternion.Euler(0, 160f, 0);

            var col = keeperObj.GetComponent<CapsuleCollider>();
            if (col == null) col = keeperObj.AddComponent<CapsuleCollider>();
            col.center = new Vector3(0, 0.9f, 0);
            col.radius = 0.4f;
            col.height = 1.8f;

            var keeperComp = keeperObj.GetComponent<LighthouseKeeperNPC>();
            if (keeperComp == null) keeperComp = keeperObj.AddComponent<LighthouseKeeperNPC>();
            keeperComp.npcName = "Fener Bekçisi Thomas";
            keeper = keeperComp;
        }
    }
}
