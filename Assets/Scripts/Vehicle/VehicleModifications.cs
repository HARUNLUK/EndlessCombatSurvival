using System;
using System.Collections.Generic;
using UnityEngine;
using EndlessSurvival.Inventory;
using EndlessCombat.AI;
using EndlessCombat.Combat;

namespace EndlessSurvival.Vehicle
{
    public enum VehicleModType
    {
        Bullbar,            // Ön Koruma Demiri / Ram Bumper
        OffroadTires,       // Arazi Tekerleri & Zırhlı Jantlar
        RoofRack,           // Tavan Zırh Kafesi & Portbagaj
        RoofLightbar,       // Tavan Projektörü / Sis Spot Barı
        ExternalFuelTanks,  // Harici Yedek Yakıt Bidonları
        WaterTank,          // Su deposu (Paket 2.19)
        RainTarp,           // Yağmur brandası: yağmurda depoya su toplar
        DryingLine,         // Kurutma ipi: sürerken et kurur
        // Paket 6 (araç hayatta kalma)
        RadiatorGrille,     // Radyatör ızgarası: ön darbede radyatör daha az hasar alır
        WindowCage,         // Cam kafesi: camlar kırılmaz
        DoorPlates,         // Kapı saçları: yandan mermi koruması, biraz ağırlık
        SpareTireMount,     // Yedek lastik askısı: yanında 1 yedek lastik
        TowRope,            // Çekme halatı: saplanınca kurtarma, enkaz/barikat çekme
        SnowChains,         // Kar zinciri: karda tutuş, asfaltta yavaşlatır
        BatteryInsulation,  // Akü yalıtımı: soğukta marş tutar
        CabinInsulation     // Battaniye ve yalıtım: kabin ısısını tutar
    }

    [Serializable]
    public class VehicleModItem
    {
        public VehicleModType type;
        public string displayName;
        public string description;
        public string statBenefit;
        public int scrapCost = 4;
        public string secondaryItemKeyword = "";
        public int secondaryItemCost = 1;
        public bool isInstalled = false;
        public GameObject visualRoot;
    }

    /// <summary>
    /// Controls modular survival upgrades for the vehicle.
    /// Manages 3D procedural visual attachments, gameplay benefits, material costs, and save state.
    /// </summary>
    public class VehicleModifications : MonoBehaviour
    {
        public static VehicleModifications Instance { get; private set; }

        [Header("References")]
        public VehicleController vehicleController;

        [Header("Modifications")]
        public List<VehicleModItem> modifications = new List<VehicleModItem>();

        public event Action OnModificationsChanged;

        private Light _roofLightComponent;
        private bool _lightbarActive = true;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }
            Instance = this;

            if (vehicleController == null)
                vehicleController = GetComponent<VehicleController>();

            InitializeModList();
            BuildVisualsIfMissing();
            ApplyAllInstalledEffects();
        }

        private void Start()
        {
            ApplyAllInstalledEffects();
        }

        private void Update()
        {
            // Tavan projektörünü [L] tuşu ile açıp kapatabilme
            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            if (keyboard != null && keyboard.lKey.wasPressedThisFrame)
            {
                if (IsInstalled(VehicleModType.RoofLightbar) && _roofLightComponent != null)
                {
                    _lightbarActive = !_lightbarActive;
                    _roofLightComponent.enabled = _lightbarActive;
                }
            }
        }

        private void InitializeModList()
        {
            InitializeDefaultMods();
            EnsureSurvivalMods();
        }

        /// <summary>Package 2.19 mods are merged into lists saved before they existed.</summary>
        private void EnsureSurvivalMods()
        {
            AddIfMissing(VehicleModType.WaterTank, "Su Deposu", "Bagaja bağlanan 40 litrelik su bidonu.", "Temiz su taşır (Tab > Araç).", 3, "RubberHose", 1);
            AddIfMissing(VehicleModType.RainTarp, "Yağmur Brandası", "Tavan portbagajına gerilen huni şeklinde branda.", "Yağmurda, sürerken bile su deposuna su toplar.", 2, "Cloth", 3);
            AddIfMissing(VehicleModType.DryingLine, "Kurutma İpi", "Kabin arkasına gerilen et kurutma ipi.", "Sürerken bagajdaki çiğ et kurur.", 1, "Cloth", 1);

            // Paket 6
            AddIfMissing(VehicleModType.RadiatorGrille, "Radyatör Izgarası", "Panjurun önüne vidalanmış kalın tel ızgara.",
                "Önden çarpmada ve ezmede radyatör çok daha az hasar alır.", 3, "", 0);
            AddIfMissing(VehicleModType.WindowCage, "Cam Kafesi", "Bütün camların önüne kaynaklanmış tel örgü.",
                "Camlar kırılmaz (mermi ve darbe). Görüşü etkilemez.", 5, "DuctTape", 1);
            AddIfMissing(VehicleModType.DoorPlates, "Kapı Saçları", "Kapılara perçinlenmiş kalın saç plakalar.",
                "Mermi hasarı azalır, yan camlara mermi işlemez. Ağırlık: motor biraz zayıflar.", 6, "", 0);
            AddIfMissing(VehicleModType.SpareTireMount, "Yedek Lastik Askısı", "Arka kapıya bağlanan lastik askısı. Lastiği Parçalar bölümünden asarsın.",
                "Yanında 1 yedek lastik taşırsın (bagajda yer kaplamaz, 8 kg).", 2, "", 0);
            AddIfMissing(VehicleModType.TowRope, "Çekme Halatı", "Arka çekme kancasına sarılmış kalın halat.",
                "[T]: saplanınca aracı kurtar, yoldaki enkazı veya barikatı çekip kaldır.", 1, "Rope", 1);
            AddIfMissing(VehicleModType.SnowChains, "Kar Zinciri", "Tekerleklere sarılan zincirler.",
                "Karda ve buzda tutuş artar; asfaltta yavaşlatır (en fazla 70 km/s).", 1, "Chain", 2);
            AddIfMissing(VehicleModType.BatteryInsulation, "Akü Yalıtımı", "Akünün etrafına sarılmış kalın keçe.",
                "Akü geç soğur; soğukta marş daha kolay tutar.", 0, "Cloth", 3);
            AddIfMissing(VehicleModType.CabinInsulation, "Battaniye ve Yalıtım", "Kapı içlerine ve tavana gerilmiş battaniyeler.",
                "Kabin ısısı daha yüksek olur ve motor kapalıyken daha geç soğur.", 0, "Blanket", 2);
        }

        private void AddIfMissing(VehicleModType type, string name, string desc, string benefit, int scrap, string keyword, int keywordCost)
        {
            if (GetModItem(type) != null) return;
            modifications.Add(new VehicleModItem
            {
                type = type, displayName = name, description = desc, statBenefit = benefit,
                scrapCost = scrap, secondaryItemKeyword = keyword, secondaryItemCost = keywordCost
            });
        }

        private void InitializeDefaultMods()
        {
            if (modifications.Count == 0)
            {
                modifications.Add(new VehicleModItem
                {
                    type = VehicleModType.Bullbar,
                    displayName = "Ön Koruma Demiri (Ram Bumper)",
                    description = "Aracın önüne kaynaklanmış ağır çelik ızgara ve tampon desteği.",
                    statBenefit = "Çarpışma hasarını %70 azaltır, düşmanları ezer.",
                    scrapCost = 4,
                    secondaryItemKeyword = "",
                    secondaryItemCost = 0
                });

                modifications.Add(new VehicleModItem
                {
                    type = VehicleModType.OffroadTires,
                    displayName = "Zırhlı Arazi Tekerleri (Mud & Rock)",
                    description = "Derin dişli arazi lastikleri ve çelik jant muhafazaları.",
                    statBenefit = "Yol tutuşu +%45, arazide kaymayı önler, tırmanışı artırır.",
                    scrapCost = 3,
                    secondaryItemKeyword = "Material",
                    secondaryItemCost = 1
                });

                modifications.Add(new VehicleModItem
                {
                    type = VehicleModType.RoofRack,
                    displayName = "Tavan Zırhı & Portbagaj (Roof Cage)",
                    description = "Kabin üstü çelik koruma kafesi ve kargo taşıma sepeti.",
                    statBenefit = "Mermi hasarını %50 azaltır, zırh dayanıklılığı kazandırır.",
                    scrapCost = 5,
                    secondaryItemKeyword = "Scrap",
                    secondaryItemCost = 2
                });

                modifications.Add(new VehicleModItem
                {
                    type = VehicleModType.RoofLightbar,
                    displayName = "Tavan LED Projektörü (Roof Lightbar)",
                    description = "Kabin üzerine monte edilen yüksek lümenli projektör dizilimi.",
                    statBenefit = "Gece ve siste 90 metrelik geniş aydınlatma menzili sağlar.",
                    scrapCost = 3,
                    secondaryItemKeyword = "Fuel",
                    secondaryItemCost = 1
                });

                modifications.Add(new VehicleModItem
                {
                    type = VehicleModType.ExternalFuelTanks,
                    displayName = "Harici Yedek Yakıt Bidonları",
                    description = "Aracın arkasına sabitlenen çift askeri yakıt bidon yuvası.",
                    statBenefit = "Maksimum yakıt kapasitesini +80 Litre artırır.",
                    scrapCost = 3,
                    secondaryItemKeyword = "Fuel",
                    secondaryItemCost = 1
                });
            }
        }

        public VehicleModItem GetModItem(VehicleModType type)
        {
            return modifications.Find(m => m.type == type);
        }

        public bool IsInstalled(VehicleModType type)
        {
            var item = GetModItem(type);
            return item != null && item.isInstalled;
        }

        // ==========================================
        // MONTAJ VE OYNANIŞ ETKİLERİ
        // ==========================================

        public bool InstallModification(VehicleModType type, bool freeCost = false)
        {
            var item = GetModItem(type);
            if (item == null || item.isInstalled) return false;

            if (!freeCost && !EndlessSurvival.Survival.SurvivalTest.FreeResources) // test mode: mods are free
            {
                var player = GameObject.FindGameObjectWithTag("Player");
                var inv = player != null ? player.GetComponent<PlayerInventory>() : null;
                var stash = GetComponent<VehicleStorage>();

                if (!HasRequiredMaterials(item, inv, stash))
                {
                    return false;
                }

                ConsumeMaterials(item, inv, stash);
            }

            item.isInstalled = true;
            ApplyModEffect(type);

            if (item.visualRoot != null)
            {
                item.visualRoot.SetActive(true);
            }

            OnModificationsChanged?.Invoke();
            Debug.Log($"<color=green>[VehicleModifications] {item.displayName} başarıyla araca monte edildi!</color>");
            return true;
        }

        public void UninstallModification(VehicleModType type)
        {
            var item = GetModItem(type);
            if (item == null || !item.isInstalled) return;

            item.isInstalled = false;
            RevertModEffect(type);

            if (item.visualRoot != null)
            {
                item.visualRoot.SetActive(false);
            }

            OnModificationsChanged?.Invoke();
        }

        private void ApplyModEffect(VehicleModType type)
        {
            if (vehicleController == null) return;

            switch (type)
            {
                case VehicleModType.Bullbar:
                    // Çarpışma hasarını %70 azaltır
                    vehicleController.collisionDamageScale = 0.15f;
                    break;

                case VehicleModType.OffroadTires:
                    // Arazi yol tutuşunu ve çekiş kontrolünü zirveye taşır
                    vehicleController.forwardGrip = 1.5f;
                    vehicleController.sidewaysGrip = 1.9f;
                    vehicleController.stabilityAssist = 0.4f;
                    break;

                case VehicleModType.RoofRack:
                    // Kabin tavan mermi hasarı azaltması ve araç canı artışı
                    vehicleController.bulletDamageMultiplier = IsInstalled(VehicleModType.DoorPlates) ? 0.1f : 0.15f;
                    vehicleController.maxHealth = Mathf.Max(vehicleController.maxHealth, 150f);
                    vehicleController.currentHealth = Mathf.Min(vehicleController.maxHealth, vehicleController.currentHealth + 25f);
                    break;

                case VehicleModType.RoofLightbar:
                    if (_roofLightComponent != null)
                    {
                        _roofLightComponent.enabled = true;
                    }
                    break;

                case VehicleModType.DoorPlates:
                    vehicleController.bulletDamageMultiplier = IsInstalled(VehicleModType.RoofRack) ? 0.1f : 0.2f;
                    break;

                case VehicleModType.ExternalFuelTanks:
                    // +80 Litre yakıt kapasitesi
                    vehicleController.maxFuel = 180f;
                    vehicleController.currentFuel = Mathf.Min(vehicleController.maxFuel, vehicleController.currentFuel + 40f);
                    break;
            }
        }

        private void RevertModEffect(VehicleModType type)
        {
            if (vehicleController == null) return;

            switch (type)
            {
                case VehicleModType.Bullbar:
                    vehicleController.collisionDamageScale = 0.5f;
                    break;

                case VehicleModType.OffroadTires:
                    vehicleController.forwardGrip = 1.3f;
                    vehicleController.sidewaysGrip = 1.7f;
                    vehicleController.stabilityAssist = 0.25f;
                    break;

                case VehicleModType.RoofRack:
                    vehicleController.bulletDamageMultiplier = IsInstalled(VehicleModType.DoorPlates) ? 0.2f : 0.30f;
                    break;

                case VehicleModType.DoorPlates:
                    vehicleController.bulletDamageMultiplier = IsInstalled(VehicleModType.RoofRack) ? 0.15f : 0.30f;
                    break;

                case VehicleModType.RoofLightbar:
                    if (_roofLightComponent != null)
                    {
                        _roofLightComponent.enabled = false;
                    }
                    break;

                case VehicleModType.ExternalFuelTanks:
                    vehicleController.maxFuel = 100f;
                    vehicleController.currentFuel = Mathf.Min(100f, vehicleController.currentFuel);
                    break;
            }
        }

        private void ApplyAllInstalledEffects()
        {
            foreach (var mod in modifications)
            {
                if (mod.visualRoot != null)
                {
                    mod.visualRoot.SetActive(mod.isInstalled);
                }

                if (mod.isInstalled)
                {
                    ApplyModEffect(mod.type);
                }
            }
        }

        // ==========================================
        // MALZEME KONTROLÜ VE TÜKETİM
        // ==========================================

        public bool HasRequiredMaterials(VehicleModItem item, PlayerInventory inv, VehicleStorage stash)
        {
            if (EndlessSurvival.Survival.SurvivalTest.FreeResources) return true;
            int scrapCount = CountItems(inv, stash, "Scrap", "Metal");
            if (scrapCount < item.scrapCost) return false;

            if (!string.IsNullOrEmpty(item.secondaryItemKeyword) && item.secondaryItemCost > 0)
            {
                int secondaryCount = CountItems(inv, stash, item.secondaryItemKeyword);
                if (secondaryCount < item.secondaryItemCost) return false;
            }

            return true;
        }

        private void ConsumeMaterials(VehicleModItem item, PlayerInventory inv, VehicleStorage stash)
        {
            RemoveItems(inv, stash, item.scrapCost, "Scrap", "Metal");

            if (!string.IsNullOrEmpty(item.secondaryItemKeyword) && item.secondaryItemCost > 0)
            {
                RemoveItems(inv, stash, item.secondaryItemCost, item.secondaryItemKeyword);
            }
        }

        private int CountItems(PlayerInventory inv, VehicleStorage stash, params string[] keywords)
        {
            int total = 0;
            if (inv != null && inv.Storage != null)
            {
                foreach (var pair in inv.Storage.Items)
                {
                    if (MatchesAny(pair.Key, keywords)) total += pair.Value;
                }
            }
            if (stash != null && stash.Storage != null)
            {
                foreach (var pair in stash.Storage.Items)
                {
                    if (MatchesAny(pair.Key, keywords)) total += pair.Value;
                }
            }
            return total;
        }

        private void RemoveItems(PlayerInventory inv, VehicleStorage stash, int amount, params string[] keywords)
        {
            int remaining = amount;

            // Önce araç bagajından al
            if (stash != null && stash.Storage != null)
            {
                List<ItemDefinition> toRemove = new List<ItemDefinition>();
                foreach (var pair in stash.Storage.Items)
                {
                    if (MatchesAny(pair.Key, keywords)) toRemove.Add(pair.Key);
                }
                foreach (var def in toRemove)
                {
                    int taken = stash.Storage.Remove(def, remaining);
                    remaining -= taken;
                    if (remaining <= 0) return;
                }
            }

            // Kalanı oyuncu sırt çantasından al
            if (inv != null && inv.Storage != null)
            {
                List<ItemDefinition> toRemove = new List<ItemDefinition>();
                foreach (var pair in inv.Storage.Items)
                {
                    if (MatchesAny(pair.Key, keywords)) toRemove.Add(pair.Key);
                }
                foreach (var def in toRemove)
                {
                    int taken = inv.Storage.Remove(def, remaining);
                    remaining -= taken;
                    if (remaining <= 0) return;
                }
            }
        }

        private bool MatchesAny(ItemDefinition item, string[] keywords)
        {
            if (item == null) return false;
            foreach (var kw in keywords)
            {
                if (item.name.IndexOf(kw, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    item.displayName.IndexOf(kw, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            return false;
        }

        // ==========================================
        // PROSEDÜREL 3D GÖRSEL PARÇA OLUŞTURMA
        // ==========================================

        public void BuildVisualsIfMissing()
        {
            Transform modsParent = transform.Find("VisualModifications");
            if (modsParent == null)
            {
                GameObject root = new GameObject("VisualModifications");
                root.transform.SetParent(transform, false);
                modsParent = root.transform;
            }

            var steelMat = CreateMaterial("Mat_Mod_DarkSteel", new Color(0.18f, 0.20f, 0.22f), 0.8f, 0.4f);
            var rubberMat = CreateMaterial("Mat_Mod_Rubber", new Color(0.12f, 0.12f, 0.14f), 0.1f, 0.9f);
            var redCanMat = CreateMaterial("Mat_Mod_FuelCan", new Color(0.78f, 0.18f, 0.15f), 0.5f, 0.3f);
            var lightEmissiveMat = CreateMaterial("Mat_Mod_EmissiveLight", new Color(1.0f, 0.95f, 0.8f), 0.1f, 0.1f, true);

            // 1. BULLBAR (Ön Koruma Demiri)
            var bullbarMod = GetModItem(VehicleModType.Bullbar);
            Transform bullbarObj = modsParent.Find("Mod_Bullbar");
            if (bullbarObj == null)
            {
                bullbarObj = new GameObject("Mod_Bullbar").transform;
                bullbarObj.SetParent(modsParent, false);
                bullbarObj.localPosition = new Vector3(0f, 0.70f, 1.50f);

                // Ana yatay darbe çubuğu
                CreatePrimitiveBox(bullbarObj, "MainBar", new Vector3(0f, 0f, 0f), new Vector3(1.55f, 0.14f, 0.14f), steelMat);
                // Üst yatay koruma çubuğu
                CreatePrimitiveBox(bullbarObj, "TopBar", new Vector3(0f, 0.35f, 0f), new Vector3(1.35f, 0.10f, 0.10f), steelMat);
                // Sol & Sağ dikey koç sütunları
                CreatePrimitiveBox(bullbarObj, "LeftPillar", new Vector3(-0.45f, 0.16f, 0.05f), new Vector3(0.12f, 0.50f, 0.14f), steelMat);
                CreatePrimitiveBox(bullbarObj, "RightPillar", new Vector3(0.45f, 0.16f, 0.05f), new Vector3(0.12f, 0.50f, 0.14f), steelMat);
                // Far koruma halkaları
                CreatePrimitiveBox(bullbarObj, "LeftGrill", new Vector3(-0.65f, 0.20f, -0.05f), new Vector3(0.30f, 0.30f, 0.06f), steelMat);
                CreatePrimitiveBox(bullbarObj, "RightGrill", new Vector3(0.65f, 0.20f, -0.05f), new Vector3(0.30f, 0.30f, 0.06f), steelMat);
            }
            if (bullbarMod != null) bullbarMod.visualRoot = bullbarObj.gameObject;

            // 2. OFF-ROAD TIRES (Zırhlı Çamurluklar ve Jant Plakaları)
            var tiresMod = GetModItem(VehicleModType.OffroadTires);
            Transform tiresObj = modsParent.Find("Mod_OffroadTires");
            if (tiresObj == null)
            {
                tiresObj = new GameObject("Mod_OffroadTires").transform;
                tiresObj.SetParent(modsParent, false);

                // 4 Tekerlek arkasına ağır çamur paçalıkları
                CreatePrimitiveBox(tiresObj, "Mudflap_FL", new Vector3(-0.85f, 0.45f, 0.40f), new Vector3(0.12f, 0.40f, 0.25f), rubberMat);
                CreatePrimitiveBox(tiresObj, "Mudflap_FR", new Vector3(0.85f, 0.45f, 0.40f), new Vector3(0.12f, 0.40f, 0.25f), rubberMat);
                CreatePrimitiveBox(tiresObj, "Mudflap_BL", new Vector3(-0.85f, 0.45f, -1.05f), new Vector3(0.12f, 0.40f, 0.25f), rubberMat);
                CreatePrimitiveBox(tiresObj, "Mudflap_BR", new Vector3(0.85f, 0.45f, -1.05f), new Vector3(0.12f, 0.40f, 0.25f), rubberMat);

                // Zırhlı yan basamaklar / kaya koruyucuları (Rock sliders)
                CreatePrimitiveBox(tiresObj, "RockSlider_Left", new Vector3(-0.88f, 0.35f, -0.30f), new Vector3(0.10f, 0.08f, 1.40f), steelMat);
                CreatePrimitiveBox(tiresObj, "RockSlider_Right", new Vector3(0.88f, 0.35f, -0.30f), new Vector3(0.10f, 0.08f, 1.40f), steelMat);
            }
            if (tiresMod != null) tiresMod.visualRoot = tiresObj.gameObject;

            // 3. ROOF RACK (Tavan Zırh Kafesi & Portbagaj)
            var rackMod = GetModItem(VehicleModType.RoofRack);
            Transform rackObj = modsParent.Find("Mod_RoofRack");
            if (rackObj == null)
            {
                rackObj = new GameObject("Mod_RoofRack").transform;
                rackObj.SetParent(modsParent, false);
                rackObj.localPosition = new Vector3(0f, 1.68f, -0.15f);

                // Sepet taban ve çerçeve çubukları
                CreatePrimitiveBox(rackObj, "Rack_Base", new Vector3(0f, 0f, 0f), new Vector3(1.30f, 0.06f, 1.40f), steelMat);
                CreatePrimitiveBox(rackObj, "Rack_FrontRail", new Vector3(0f, 0.12f, 0.68f), new Vector3(1.30f, 0.18f, 0.06f), steelMat);
                CreatePrimitiveBox(rackObj, "Rack_BackRail", new Vector3(0f, 0.12f, -0.68f), new Vector3(1.30f, 0.18f, 0.06f), steelMat);
                CreatePrimitiveBox(rackObj, "Rack_LeftRail", new Vector3(-0.62f, 0.12f, 0f), new Vector3(0.06f, 0.18f, 1.40f), steelMat);
                CreatePrimitiveBox(rackObj, "Rack_RightRail", new Vector3(0.62f, 0.12f, 0f), new Vector3(0.06f, 0.18f, 1.40f), steelMat);

                // Portbagaj içine sabitlenmiş askeri erzak sandığı ve yedek çanta
                var crateMat = CreateMaterial("Mat_Mod_Crate", new Color(0.35f, 0.38f, 0.28f), 0.2f, 0.8f);
                CreatePrimitiveBox(rackObj, "CargoCrate", new Vector3(-0.25f, 0.16f, 0.15f), new Vector3(0.65f, 0.26f, 0.45f), crateMat);
                CreatePrimitiveBox(rackObj, "CargoTent", new Vector3(0.28f, 0.14f, -0.20f), new Vector3(0.40f, 0.22f, 0.70f), steelMat);
            }
            if (rackMod != null) rackMod.visualRoot = rackObj.gameObject;

            // 4. ROOF LIGHTBAR (Tavan LED Projektörü)
            var lightMod = GetModItem(VehicleModType.RoofLightbar);
            Transform lightObj = modsParent.Find("Mod_RoofLightbar");
            if (lightObj == null)
            {
                lightObj = new GameObject("Mod_RoofLightbar").transform;
                lightObj.SetParent(modsParent, false);
                lightObj.localPosition = new Vector3(0f, 1.74f, 0.60f);

                // LED Gövdesi
                CreatePrimitiveBox(lightObj, "LightBarHousing", new Vector3(0f, 0f, 0f), new Vector3(1.10f, 0.10f, 0.14f), steelMat);
                // Işık yayan şerit (Emissive strip)
                CreatePrimitiveBox(lightObj, "LightStrip", new Vector3(0f, 0f, 0.06f), new Vector3(0.98f, 0.06f, 0.04f), lightEmissiveMat);

                // Gerçek Unity Işık Kaynağı (Spotlight)
                GameObject spotObj = new GameObject("Lightbar_Spotlight");
                spotObj.transform.SetParent(lightObj, false);
                spotObj.transform.localPosition = new Vector3(0f, 0f, 0.12f);
                spotObj.transform.localRotation = Quaternion.Euler(6f, 0f, 0f);

                var spot = spotObj.AddComponent<Light>();
                spot.type = LightType.Spot;
                spot.range = 95f;
                spot.spotAngle = 75f;
                spot.innerSpotAngle = 45f;
                spot.intensity = 3.6f;
                spot.color = new Color(1.0f, 0.96f, 0.88f);
                spot.shadows = LightShadows.Soft;

                _roofLightComponent = spot;
            }
            else
            {
                _roofLightComponent = lightObj.GetComponentInChildren<Light>();
            }
            if (lightMod != null) lightMod.visualRoot = lightObj.gameObject;

            // 5. EXTERNAL FUEL TANKS (Harici Yedek Yakıt Bidonları)
            var fuelMod = GetModItem(VehicleModType.ExternalFuelTanks);
            Transform fuelObj = modsParent.Find("Mod_ExternalFuelTanks");
            if (fuelObj == null)
            {
                fuelObj = new GameObject("Mod_ExternalFuelTanks").transform;
                fuelObj.SetParent(modsParent, false);
                fuelObj.localPosition = new Vector3(0f, 0.95f, -1.42f);

                // Sol ve Sağ Kırmızı Yakıt Bidonları
                CreatePrimitiveBox(fuelObj, "JerryCan_Left", new Vector3(-0.55f, 0f, 0f), new Vector3(0.24f, 0.44f, 0.34f), redCanMat);
                CreatePrimitiveBox(fuelObj, "JerryCan_Right", new Vector3(0.55f, 0f, 0f), new Vector3(0.24f, 0.44f, 0.34f), redCanMat);

                // Bidon tutucu çelik braketler
                CreatePrimitiveBox(fuelObj, "Bracket_Left", new Vector3(-0.55f, -0.05f, 0.02f), new Vector3(0.28f, 0.36f, 0.38f), steelMat);
                CreatePrimitiveBox(fuelObj, "Bracket_Right", new Vector3(0.55f, -0.05f, 0.02f), new Vector3(0.28f, 0.36f, 0.38f), steelMat);
            }
            if (fuelMod != null) fuelMod.visualRoot = fuelObj.gameObject;

            BuildPackage6Visuals(modsParent, steelMat, rubberMat);
        }

        /// <summary>Paket 6 mods: simple boxes on the same layout as the older mods.</summary>
        private void BuildPackage6Visuals(Transform modsParent, Material steelMat, Material rubberMat)
        {
            var wireMat = CreateMaterial("Mat_Mod_Wire", new Color(0.30f, 0.31f, 0.32f), 0.7f, 0.5f);
            var plateMat = CreateMaterial("Mat_Mod_Plate", new Color(0.34f, 0.32f, 0.29f), 0.6f, 0.25f);
            var ropeMat = CreateMaterial("Mat_Mod_Rope", new Color(0.62f, 0.52f, 0.33f), 0f, 0.2f);
            var chainMat = CreateMaterial("Mat_Mod_Chain", new Color(0.55f, 0.56f, 0.58f), 0.9f, 0.6f);

            Transform Root(VehicleModType type, string name, Vector3 pos)
            {
                Transform t = modsParent.Find(name);
                bool built = t != null;
                if (!built)
                {
                    t = new GameObject(name).transform;
                    t.SetParent(modsParent, false);
                    t.localPosition = pos;
                }
                var mod = GetModItem(type);
                if (mod != null) mod.visualRoot = t.gameObject;
                return built ? null : t; // null = already has children
            }

            var grille = Root(VehicleModType.RadiatorGrille, "Mod_RadiatorGrille", new Vector3(0f, 0.82f, 1.47f));
            if (grille != null)
            {
                for (int i = 0; i < 5; i++)
                    CreatePrimitiveBox(grille, "Bar_" + i, new Vector3(0f, -0.16f + i * 0.08f, 0f), new Vector3(1.0f, 0.03f, 0.04f), wireMat);
                CreatePrimitiveBox(grille, "Frame_L", new Vector3(-0.5f, 0f, 0f), new Vector3(0.05f, 0.4f, 0.05f), steelMat);
                CreatePrimitiveBox(grille, "Frame_R", new Vector3(0.5f, 0f, 0f), new Vector3(0.05f, 0.4f, 0.05f), steelMat);
            }

            var cage = Root(VehicleModType.WindowCage, "Mod_WindowCage", new Vector3(0f, 1.35f, 0.62f));
            if (cage != null)
            {
                // Windshield mesh (tilted) and side window bars
                var front = new GameObject("Front").transform;
                front.SetParent(cage, false);
                front.localRotation = Quaternion.Euler(-28f, 0f, 0f);
                for (int i = 0; i < 6; i++)
                    CreatePrimitiveBox(front, "V_" + i, new Vector3(-0.6f + i * 0.24f, 0f, 0f), new Vector3(0.025f, 0.55f, 0.025f), wireMat);
                for (int i = 0; i < 3; i++)
                    CreatePrimitiveBox(front, "H_" + i, new Vector3(0f, -0.22f + i * 0.22f, 0f), new Vector3(1.25f, 0.025f, 0.025f), wireMat);
                foreach (float s in new[] { -1f, 1f })
                    for (int i = 0; i < 5; i++)
                        CreatePrimitiveBox(cage, (s < 0 ? "SideL_" : "SideR_") + i, new Vector3(s * 0.83f, 0f, -0.25f - i * 0.28f), new Vector3(0.025f, 0.45f, 0.025f), wireMat);
            }

            var plates = Root(VehicleModType.DoorPlates, "Mod_DoorPlates", new Vector3(0f, 0.95f, -0.25f));
            if (plates != null)
            {
                CreatePrimitiveBox(plates, "Plate_L", new Vector3(-0.86f, 0f, 0f), new Vector3(0.04f, 0.42f, 1.5f), plateMat);
                CreatePrimitiveBox(plates, "Plate_R", new Vector3(0.86f, 0f, 0f), new Vector3(0.04f, 0.42f, 1.5f), plateMat);
                for (int i = 0; i < 4; i++)
                {
                    float z = -0.6f + i * 0.4f;
                    CreatePrimitiveBox(plates, "Rivet_L" + i, new Vector3(-0.885f, 0.16f, z), new Vector3(0.02f, 0.04f, 0.04f), steelMat);
                    CreatePrimitiveBox(plates, "Rivet_R" + i, new Vector3(0.885f, 0.16f, z), new Vector3(0.02f, 0.04f, 0.04f), steelMat);
                }
            }

            var spare = Root(VehicleModType.SpareTireMount, "Mod_SpareTireMount", new Vector3(0f, 1.0f, -1.5f));
            if (spare != null)
            {
                CreatePrimitiveBox(spare, "Bracket", new Vector3(0f, 0f, 0.04f), new Vector3(0.3f, 0.3f, 0.06f), steelMat);
                var tyre = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                tyre.name = "SpareTire";
                tyre.transform.SetParent(spare, false);
                tyre.transform.localPosition = new Vector3(0f, 0f, -0.1f);
                tyre.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                tyre.transform.localScale = new Vector3(0.62f, 0.11f, 0.62f);
                var col = tyre.GetComponent<Collider>();
                if (col != null) Destroy(col);
                tyre.GetComponent<Renderer>().sharedMaterial = rubberMat;
            }

            var rope = Root(VehicleModType.TowRope, "Mod_TowRope", new Vector3(0.45f, 0.55f, -1.5f));
            if (rope != null)
            {
                for (int i = 0; i < 3; i++)
                    CreatePrimitiveBox(rope, "Coil_" + i, new Vector3(0f, i * 0.05f, 0f), new Vector3(0.3f, 0.04f, 0.3f), ropeMat);
                CreatePrimitiveBox(rope, "Hook", new Vector3(-0.45f, -0.05f, 0.02f), new Vector3(0.12f, 0.08f, 0.12f), steelMat);
            }

            var chains = Root(VehicleModType.SnowChains, "Mod_SnowChains", Vector3.zero);
            if (chains != null && vehicleController != null)
            {
                foreach (var wc in new[] { vehicleController.frontLeftCollider, vehicleController.frontRightCollider, vehicleController.rearLeftCollider, vehicleController.rearRightCollider })
                {
                    if (wc == null) continue;
                    Vector3 p = transform.InverseTransformPoint(wc.transform.position);
                    float r = wc.radius * wc.transform.lossyScale.y;
                    float side = Mathf.Sign(p.x);
                    for (int i = 0; i < 6; i++)
                    {
                        float a = i * 60f;
                        var link = CreatePrimitiveBox(chains, "Link", new Vector3(p.x + side * 0.02f, p.y - vehicleController.suspensionDistance * 0.5f, p.z), new Vector3(0.24f, 0.03f, 0.03f), chainMat);
                        link.transform.localRotation = Quaternion.Euler(a, 0f, 0f);
                        link.transform.localPosition += link.transform.localRotation * new Vector3(0f, r, 0f);
                    }
                }
            }

            // Insulation mods are inside the vehicle: no outer visual
            Root(VehicleModType.BatteryInsulation, "Mod_BatteryInsulation", Vector3.zero);
            Root(VehicleModType.CabinInsulation, "Mod_CabinInsulation", Vector3.zero);
        }

        private GameObject CreatePrimitiveBox(Transform parent, string name, Vector3 localPos, Vector3 localScale, Material mat)
        {
            GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = name;
            box.transform.SetParent(parent, false);
            box.transform.localPosition = localPos;
            box.transform.localScale = localScale;

            // Gereksiz collider'ı kaldır ki araç fiziğine takılmasın
            var col = box.GetComponent<Collider>();
            if (col != null) Destroy(col);

            var rend = box.GetComponent<Renderer>();
            if (rend != null && mat != null) rend.sharedMaterial = mat;

            return box;
        }

        private Material CreateMaterial(string name, Color color, float metallic = 0.5f, float smoothness = 0.5f, bool emissive = false)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            Material mat = new Material(shader);
            mat.name = name;
            mat.color = color;
            if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", metallic);
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", smoothness);

            if (emissive)
            {
                mat.EnableKeyword("_EMISSION");
                if (mat.HasProperty("_EmissionColor")) mat.SetColor("_EmissionColor", color * 2.5f);
            }

            return mat;
        }

        // ==========================================
        // ÇARPIŞMA DARBE HASARI ARTIRMA (RAM DAMAGE)
        // ==========================================

        private void OnCollisionEnter(Collision collision)
        {
            // Eğer Ön Koruma Demiri takılıysa ve araba hızlı gidiyorsa düşmanlara ezme hasarı vur
            if (!IsInstalled(VehicleModType.Bullbar)) return;

            float speed = vehicleController != null ? vehicleController.CurrentSpeedKmh : 0f;
            if (speed > 15f && collision.gameObject != null)
            {
                var damageable = collision.gameObject.GetComponentInParent<IDamageable>();
                if (damageable != null && !collision.gameObject.CompareTag("Player"))
                {
                    float ramDamage = speed * 3.5f; // Örn 50 km/h = 175 hasar (tek vuruşta öldürür)
                    damageable.TakeDamage(ramDamage, collision.contacts[0].point, collision.contacts[0].normal);
                    Debug.Log($"<color=orange>[Bullbar] Çarpma darbesi vuruldu: {ramDamage} Hasar!</color>");
                }
            }
        }

        // ==========================================
        // SAVE / LOAD SİSTEMİ VERİLERİ
        // ==========================================

        public List<string> GetInstalledModNames()
        {
            List<string> list = new List<string>();
            foreach (var mod in modifications)
            {
                if (mod.isInstalled) list.Add(mod.type.ToString());
            }
            return list;
        }

        public void RestoreFromSave(List<string> installedModNames)
        {
            if (installedModNames == null) return;

            foreach (var mod in modifications)
            {
                bool shouldBeInstalled = installedModNames.Contains(mod.type.ToString());
                if (shouldBeInstalled)
                {
                    InstallModification(mod.type, freeCost: true);
                }
                else
                {
                    UninstallModification(mod.type);
                }
            }
        }
    }
}
