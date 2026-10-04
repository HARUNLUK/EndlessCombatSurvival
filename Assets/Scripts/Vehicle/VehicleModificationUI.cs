using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using EndlessSurvival.Inventory;

namespace EndlessSurvival.Vehicle
{
    /// <summary>
    /// Interactive workshop UI for installing and viewing vehicle modifications.
    /// Opens with [U] when near or inside the vehicle.
    /// </summary>
    public class VehicleModificationUI : MonoBehaviour
    {
        public static VehicleModificationUI Instance { get; private set; }
        public static bool IsOpen => Instance != null && Instance._panelRoot != null && Instance._panelRoot.activeSelf;

        private GameObject _uiCanvasObj;
        private GameObject _panelRoot;
        private Transform _itemsContainer;
        private Text _statsSummaryText;

        private bool _wasCursorLocked;
        private readonly List<GameObject> _modRowObjects = new List<GameObject>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoInitialize()
        {
            if (Instance == null)
            {
                var existing = FindFirstObjectByType<VehicleModificationUI>();
                if (existing == null)
                {
                    GameObject go = new GameObject("VehicleModificationUI");
                    go.AddComponent<VehicleModificationUI>();
                    DontDestroyOnLoad(go);
                }
            }
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            CreateUI();
        }

        private float _openTime;
        private float _lastCloseTime;

        private void Update()
        {
            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            if (keyboard == null) return;

            if (IsOpen)
            {
                // Aynı karede açılıp hemen geri kapanmasını önlemek için gecikme kontrolü
                if (Time.unscaledTime - _openTime < 0.25f) return;

                if (keyboard.escapeKey.wasPressedThisFrame || keyboard.uKey.wasPressedThisFrame)
                {
                    Close();
                }
            }
            else
            {
                if (Time.unscaledTime - _lastCloseTime < 0.25f) return;

                if (keyboard.uKey.wasPressedThisFrame)
                {
                    if (CanOpenWorkshop())
                    {
                        Open();
                    }
                }
            }
        }

        private bool CanOpenWorkshop()
        {
            var vehicle = Object.FindFirstObjectByType<VehicleController>();
            if (vehicle == null) return false;

            if (vehicle.isDriven) return true;

            var player = GameObject.FindGameObjectWithTag("Player");
            if (player == null)
            {
                player = GameObject.Find("PlayerArmature") ?? GameObject.Find("Player");
            }

            if (player != null)
            {
                float dist = Vector3.Distance(vehicle.transform.position, player.transform.position);
                if (dist <= 10f) return true;
            }

            if (Camera.main != null)
            {
                float camDist = Vector3.Distance(vehicle.transform.position, Camera.main.transform.position);
                if (camDist <= 14f) return true;
            }

            var interact = vehicle.GetComponent<VehicleInteraction>();
            if (interact != null && interact.IsPlayerInside) return true;

            return false;
        }

        public static void Toggle()
        {
            if (IsOpen) Close();
            else Open();
        }

        public static void Open()
        {
            if (Instance == null)
            {
                AutoInitialize();
            }

            if (Instance != null)
            {
                Instance.OpenInternal();
            }
        }

        public static void Close()
        {
            if (Instance != null)
            {
                Instance.CloseInternal();
            }
        }

        private void OpenInternal()
        {
            if (_panelRoot == null) CreateUI();

            _openTime = Time.unscaledTime;
            _wasCursorLocked = Cursor.lockState == CursorLockMode.Locked;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            _panelRoot.SetActive(true);
            RefreshUI();
        }

        private void CloseInternal()
        {
            _lastCloseTime = Time.unscaledTime;
            if (_panelRoot != null)
            {
                _panelRoot.SetActive(false);
            }

            if (_wasCursorLocked)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
        }

        public void RefreshUI()
        {
            var vehicle = FindFirstObjectByType<VehicleController>();
            var mods = VehicleModifications.Instance;
            if (mods == null && vehicle != null)
            {
                mods = vehicle.GetComponent<VehicleModifications>() ?? vehicle.gameObject.AddComponent<VehicleModifications>();
            }

            // 1. İstatistik Özetini Güncelle
            if (_statsSummaryText != null && vehicle != null)
            {
                bool bullbar = mods != null && mods.IsInstalled(VehicleModType.Bullbar);
                bool tires = mods != null && mods.IsInstalled(VehicleModType.OffroadTires);
                bool cage = mods != null && mods.IsInstalled(VehicleModType.RoofRack);
                bool light = mods != null && mods.IsInstalled(VehicleModType.RoofLightbar);
                bool fuel = mods != null && mods.IsInstalled(VehicleModType.ExternalFuelTanks);

                string bullbarStr = bullbar ? "<color=#00ff88>GÜÇLENDİRİLMİŞ (%70 Az Hasar)</color>" : "Standart Tampon";
                string tiresStr = tires ? "<color=#00ff88>4.2 Grip (Arazi Tırmanış)</color>" : "2.8 Grip (Standart)";
                string cageStr = cage ? "<color=#00ff88>ZIRHLI (%50 Mermi Koruması)</color>" : "Standart Tavan";
                string lightStr = light ? "<color=#00ff88>90m Geniş Projektör [L]</color>" : "Standart Farlar";
                string fuelStr = fuel ? "<color=#00ff88>180 Litre (Genişletilmiş)</color>" : "100 Litre";

                _statsSummaryText.text = $"<b>ARAÇ DURUMU:</b>  Gövde: {Mathf.RoundToInt(vehicle.currentHealth)}/{Mathf.RoundToInt(vehicle.maxHealth)} HP  |  " +
                                        $"Ön Zırh: {bullbarStr}  |  Lastik: {tiresStr}  |  Tavan: {cageStr}  |  Projektör: {lightStr}  |  Depo: {fuelStr}";
            }

            // 2. Modifiye Kartlarını Yenile
            foreach (var go in _modRowObjects)
            {
                Destroy(go);
            }
            _modRowObjects.Clear();

            if (mods == null) return;

            var player = GameObject.FindGameObjectWithTag("Player");
            var inv = player != null ? player.GetComponent<PlayerInventory>() : null;
            var stash = vehicle != null ? vehicle.GetComponent<VehicleStorage>() : null;

            foreach (var mod in mods.modifications)
            {
                CreateModRow(mod, mods, inv, stash);
            }
        }

        private void CreateModRow(VehicleModItem mod, VehicleModifications modsManager, PlayerInventory inv, VehicleStorage stash)
        {
            GameObject rowObj = new GameObject($"Row_{mod.type}");
            rowObj.transform.SetParent(_itemsContainer, false);
            _modRowObjects.Add(rowObj);

            var rowRect = rowObj.AddComponent<RectTransform>();
            rowRect.sizeDelta = new Vector2(860f, 85f);

            var rowBg = rowObj.AddComponent<Image>();
            rowBg.color = mod.isInstalled ? new Color(0.12f, 0.22f, 0.16f, 0.92f) : new Color(0.16f, 0.18f, 0.22f, 0.92f);

            // Sol Kısım: Başlık ve Açıklama
            GameObject infoObj = new GameObject("Info");
            infoObj.transform.SetParent(rowObj.transform, false);
            var infoRect = infoObj.AddComponent<RectTransform>();
            infoRect.anchorMin = new Vector2(0f, 0f);
            infoRect.anchorMax = new Vector2(0.68f, 1f);
            infoRect.offsetMin = new Vector2(18f, 6f);
            infoRect.offsetMax = new Vector2(-10f, -6f);

            var infoText = infoObj.AddComponent<Text>();
            infoText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf") ?? Resources.GetBuiltinResource<Font>("Arial.ttf");
            infoText.fontSize = 15;
            infoText.alignment = TextAnchor.MiddleLeft;
            infoText.color = Color.white;

            string statusIcon = mod.isInstalled ? "<color=#00ff88>✔</color>" : "◻";
            string costStr = $"{mod.scrapCost}x Hurda Metal" + (string.IsNullOrEmpty(mod.secondaryItemKeyword) ? "" : $" + {mod.secondaryItemCost}x {mod.secondaryItemKeyword}");

            infoText.text = $"<b>{statusIcon} {mod.displayName}</b>\n" +
                            $"<size=13><color=#aaccff>Etki: {mod.statBenefit}</color></size>\n" +
                            $"<size=12><color=#ddaa66>Maliyet: {costStr}</color> — {mod.description}</size>";

            // Sağ Kısım: Butonlar
            bool canAfford = modsManager.HasRequiredMaterials(mod, inv, stash);

            if (mod.isInstalled)
            {
                // SÖK Butonu
                CreateButton(rowObj.transform, "Btn_Uninstall", "MONTE EDİLDİ [SÖK]", new Vector2(340f, 0f), new Vector2(160f, 42f),
                    new Color(0.18f, 0.48f, 0.28f), () =>
                    {
                        modsManager.UninstallModification(mod.type);
                        RefreshUI();
                    });
            }
            else
            {
                // MONTE ET Butonu
                Color btnCol = canAfford ? new Color(0.15f, 0.45f, 0.85f) : new Color(0.35f, 0.35f, 0.38f);
                string btnLabel = canAfford ? "MONTE ET" : "YETERSİZ HURDA";

                CreateButton(rowObj.transform, "Btn_Install", btnLabel, new Vector2(250f, 0f), new Vector2(150f, 42f),
                    btnCol, () =>
                    {
                        if (canAfford)
                        {
                            modsManager.InstallModification(mod.type, freeCost: false);
                            RefreshUI();
                        }
                    });

                // HIZLI TEST / ÜCRETSİZ MONTAJ Butonu
                CreateButton(rowObj.transform, "Btn_FreeTest", "TEST ET", new Vector2(375f, 0f), new Vector2(85f, 36f),
                    new Color(0.55f, 0.40f, 0.15f), () =>
                    {
                        modsManager.InstallModification(mod.type, freeCost: true);
                        RefreshUI();
                    });
            }
        }

        // ==========================================
        // DİNAMİK UI OLUŞTURUCULAR
        // ==========================================

        private void CreateUI()
        {
            if (_uiCanvasObj != null) return;

            _uiCanvasObj = new GameObject("VehicleModification_Canvas");
            var canvas = _uiCanvasObj.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 90;

            var scaler = _uiCanvasObj.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);

            _uiCanvasObj.AddComponent<GraphicRaycaster>();
            DontDestroyOnLoad(_uiCanvasObj);

            // 1. Arka Plan Karartması
            _panelRoot = new GameObject("PanelRoot");
            _panelRoot.transform.SetParent(_uiCanvasObj.transform, false);

            var rootRect = _panelRoot.AddComponent<RectTransform>();
            rootRect.anchorMin = Vector2.zero;
            rootRect.anchorMax = Vector2.one;

            var rootBg = _panelRoot.AddComponent<Image>();
            rootBg.color = new Color(0.04f, 0.05f, 0.07f, 0.75f);

            // 2. Ana Modal Penceresi
            GameObject modalObj = new GameObject("ModalWindow");
            modalObj.transform.SetParent(_panelRoot.transform, false);
            var modalRect = modalObj.AddComponent<RectTransform>();
            modalRect.anchorMin = new Vector2(0.5f, 0.5f);
            modalRect.anchorMax = new Vector2(0.5f, 0.5f);
            modalRect.sizeDelta = new Vector2(920f, 660f);

            var modalBg = modalObj.AddComponent<Image>();
            modalBg.color = new Color(0.10f, 0.12f, 0.15f, 0.97f);

            // Başlık Çubuğu
            GameObject headerObj = new GameObject("Header");
            headerObj.transform.SetParent(modalObj.transform, false);
            var headerRect = headerObj.AddComponent<RectTransform>();
            headerRect.anchorMin = new Vector2(0f, 1f);
            headerRect.anchorMax = new Vector2(1f, 1f);
            headerRect.anchoredPosition = new Vector2(0f, -32f);
            headerRect.sizeDelta = new Vector2(0f, 60f);

            var titleText = headerObj.AddComponent<Text>();
            titleText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf") ?? Resources.GetBuiltinResource<Font>("Arial.ttf");
            titleText.fontSize = 22;
            titleText.fontStyle = FontStyle.Bold;
            titleText.alignment = TextAnchor.MiddleCenter;
            titleText.color = new Color(1.0f, 0.85f, 0.40f);
            titleText.text = "🔧 ARAÇ MODİFİYE ATÖLYESİ (SURVIVAL MODS)";

            // Araç Durum Özeti Çubuğu
            GameObject statsObj = new GameObject("StatsBar");
            statsObj.transform.SetParent(modalObj.transform, false);
            var statsRect = statsObj.AddComponent<RectTransform>();
            statsRect.anchorMin = new Vector2(0f, 1f);
            statsRect.anchorMax = new Vector2(1f, 1f);
            statsRect.anchoredPosition = new Vector2(0f, -80f);
            statsRect.sizeDelta = new Vector2(860f, 40f);

            var statsBg = statsObj.AddComponent<Image>();
            statsBg.color = new Color(0.14f, 0.16f, 0.20f, 0.95f);

            GameObject statsTextObj = new GameObject("Text");
            statsTextObj.transform.SetParent(statsObj.transform, false);
            var statsTextRect = statsTextObj.AddComponent<RectTransform>();
            statsTextRect.anchorMin = Vector2.zero;
            statsTextRect.anchorMax = Vector2.one;
            statsTextRect.offsetMin = new Vector2(12f, 0f);
            statsTextRect.offsetMax = new Vector2(-12f, 0f);

            _statsSummaryText = statsTextObj.AddComponent<Text>();
            _statsSummaryText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf") ?? Resources.GetBuiltinResource<Font>("Arial.ttf");
            _statsSummaryText.fontSize = 13;
            _statsSummaryText.alignment = TextAnchor.MiddleLeft;
            _statsSummaryText.color = Color.white;

            // Modifiye Kartları Container (Vertical Layout)
            GameObject listObj = new GameObject("ModificationsList");
            listObj.transform.SetParent(modalObj.transform, false);
            var listRect = listObj.AddComponent<RectTransform>();
            listRect.anchorMin = new Vector2(0.5f, 0.5f);
            listRect.anchorMax = new Vector2(0.5f, 0.5f);
            listRect.anchoredPosition = new Vector2(0f, -25f);
            listRect.sizeDelta = new Vector2(860f, 460f);

            var vlg = listObj.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 8f;
            vlg.childControlHeight = false;
            vlg.childControlWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.childForceExpandWidth = true;

            _itemsContainer = listObj.transform;

            // Alt Buton: Kapat
            CreateButton(modalObj.transform, "Btn_Close", "KAPAT [ESC / U]", new Vector2(0f, -295f), new Vector2(860f, 45f),
                new Color(0.22f, 0.25f, 0.30f), () => Close());

            _panelRoot.SetActive(false);
        }

        private static Button CreateButton(Transform parent, string name, string label, Vector2 pos, Vector2 size, Color bgColor, System.Action onClick)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rect = go.AddComponent<RectTransform>();
            rect.anchoredPosition = pos;
            rect.sizeDelta = size;

            var img = go.AddComponent<Image>();
            img.color = bgColor;

            var btn = go.AddComponent<Button>();
            var colors = btn.colors;
            colors.highlightedColor = bgColor * 1.25f;
            colors.pressedColor = bgColor * 0.75f;
            btn.colors = colors;

            GameObject textObj = new GameObject("Label");
            textObj.transform.SetParent(go.transform, false);
            var textRect = textObj.AddComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;

            var txt = textObj.AddComponent<Text>();
            txt.text = label;
            txt.fontSize = 14;
            txt.fontStyle = FontStyle.Bold;
            txt.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf") ?? Resources.GetBuiltinResource<Font>("Arial.ttf");
            txt.alignment = TextAnchor.MiddleCenter;
            txt.color = Color.white;

            if (onClick != null) btn.onClick.AddListener(() => onClick());

            return btn;
        }
    }
}
