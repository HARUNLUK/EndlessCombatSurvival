using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using EndlessSurvival.Inventory;

namespace EndlessSurvival.Camping
{
    public class CampfireUI : MonoBehaviour
    {
        private static CampfireUI _instance;
        private static float _openTime;
        private static float _lastCloseTime;

        private PlayerCampfire _activeFire;
        private PlayerInventory _activeUser;

        private GameObject _panelRoot;
        private Text _titleText;
        private Text _statusText;
        private Text _feedbackText;
        private Image _fadeOverlay;
        private Text _fadeText;

        public static bool IsOpen => _instance != null && _instance._panelRoot != null && _instance._panelRoot.activeSelf;
        public static float LastCloseTime => _lastCloseTime;

        public static void Open(PlayerCampfire fire, PlayerInventory user)
        {
            if (_instance == null)
            {
                CreateUI();
            }

            _instance._activeFire = fire;
            _instance._activeUser = user;
            _instance._panelRoot.SetActive(true);
            _instance.RefreshUI();
            _openTime = Time.unscaledTime;

            // Cursor aç
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            // Kamera dönüşünü durdur (menü etkileşimi için)
            var inputs = Object.FindAnyObjectByType<StarterAssets.StarterAssetsInputs>();
            if (inputs != null)
            {
                inputs.cursorInputForLook = false;
                inputs.look = Vector2.zero;
            }
        }

        public static void Close()
        {
            if (_instance != null && _instance._panelRoot != null)
            {
                _instance._panelRoot.SetActive(false);
                _lastCloseTime = Time.unscaledTime;
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;

                var inputs = Object.FindAnyObjectByType<StarterAssets.StarterAssetsInputs>();
                if (inputs != null)
                {
                    inputs.cursorInputForLook = true;
                }
            }
        }

        private static void CreateUI()
        {
            GameObject uiObj = new GameObject("Campfire_Canvas");
            var canvas = uiObj.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 50;

            var scaler = uiObj.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);

            uiObj.AddComponent<GraphicRaycaster>();
            _instance = uiObj.AddComponent<CampfireUI>();

            // Fullscreen Fade Overlay (Dinlenme / Uyku için)
            GameObject fadeObj = new GameObject("FadeOverlay");
            fadeObj.transform.SetParent(uiObj.transform, false);
            var fadeRect = fadeObj.AddComponent<RectTransform>();
            fadeRect.anchorMin = Vector2.zero;
            fadeRect.anchorMax = Vector2.one;
            fadeRect.offsetMin = Vector2.zero;
            fadeRect.offsetMax = Vector2.zero;
            _instance._fadeOverlay = fadeObj.AddComponent<Image>();
            _instance._fadeOverlay.color = new Color(0, 0, 0, 0);
            _instance._fadeOverlay.raycastTarget = false;

            // Fade Metni (Uyunurken geçen zaman bilgisi)
            GameObject fadeTextObj = new GameObject("FadeText");
            fadeTextObj.transform.SetParent(fadeObj.transform, false);
            var fadeTextRect = fadeTextObj.AddComponent<RectTransform>();
            fadeTextRect.anchorMin = Vector2.zero;
            fadeTextRect.anchorMax = Vector2.one;
            fadeTextRect.offsetMin = new Vector2(40f, 0f);
            fadeTextRect.offsetMax = new Vector2(-40f, 0f);
            _instance._fadeText = fadeTextObj.AddComponent<Text>();
            _instance._fadeText.alignment = TextAnchor.MiddleCenter;
            _instance._fadeText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf") ?? Resources.GetBuiltinResource<Font>("Arial.ttf");
            _instance._fadeText.fontSize = 26;
            _instance._fadeText.fontStyle = FontStyle.Bold;
            _instance._fadeText.color = new Color(1f, 0.92f, 0.75f, 1f);
            _instance._fadeText.text = "";

            // Main Modal Window
            GameObject panel = new GameObject("ModalPanel");
            panel.transform.SetParent(uiObj.transform, false);
            var panelRect = panel.AddComponent<RectTransform>();
            panelRect.sizeDelta = new Vector2(560, 720);
            panelRect.anchoredPosition = Vector2.zero;

            var panelBg = panel.AddComponent<Image>();
            panelBg.color = new Color(0.08f, 0.08f, 0.1f, 0.95f); // Koyu şık panel

            _instance._panelRoot = panel;

            // Header Title
            _instance._titleText = CreateText(panel.transform, "Title", "KAMP ATEŞİ & PİŞİRME", 22, FontStyle.Bold, new Vector2(0, 320), new Vector2(500, 40), Color.white);

            // Subtitle / Status
            _instance._statusText = CreateText(panel.transform, "Status", "Kalan Süre: 05:00", 16, FontStyle.Normal, new Vector2(0, 280), new Vector2(500, 35), new Color(1f, 0.8f, 0.4f));

            // Feedback Message
            _instance._feedbackText = CreateText(panel.transform, "Feedback", "", 14, FontStyle.Italic, new Vector2(0, 245), new Vector2(500, 30), Color.green);

            // 1. Yakacak Ekle Butonu
            CreateButton(panel.transform, "Btn_Fuel", "🪵 Yakacak Ekle (+3 Dakika)", new Vector2(0, 195), new Vector2(460, 42), () => _instance.OnAddFuelClicked());

            // 2. Pişirme Butonları Bölümü
            CreateText(panel.transform, "SectionCooking", "--- YEMEK PİŞİRME ---", 15, FontStyle.Bold, new Vector2(0, 145), new Vector2(500, 30), new Color(0.8f, 0.8f, 0.8f));

            CreateButton(panel.transform, "Btn_CookMeat", "🥩 Çiğ Et ➔ Pişmiş Et (+45 Açlık, +15 Can)", new Vector2(0, 95), new Vector2(460, 40), () => _instance.OnCookMeatClicked());
            CreateButton(panel.transform, "Btn_CookStew", "🍲 Fasulye ➔ Sıcak Güveç (+40 Açlık, +10 Su)", new Vector2(0, 45), new Vector2(460, 40), () => _instance.OnCookStewClicked());
            CreateButton(panel.transform, "Btn_BoilWater", "💧 Boş Şişe ➔ Kaynamış Temiz Su (+50 Su)", new Vector2(0, -5), new Vector2(460, 40), () => _instance.OnBoilWaterClicked());

            // 3. Dinlenme & Sıhhat Bölümü
            CreateText(panel.transform, "SectionRest", "--- DİNLENME & SIHHAT ---", 15, FontStyle.Bold, new Vector2(0, -55), new Vector2(500, 30), new Color(0.8f, 0.8f, 0.8f));

            CreateButton(panel.transform, "Btn_Sit", "🔥 Ateş Başında Yere Otur [C]", new Vector2(0, -100), new Vector2(460, 40), () => _instance.OnSitClicked());
            CreateButton(panel.transform, "Btn_Rest", "🏕️ Ateş Başında Uyu / Zamanı İlerlet (+40 Can)", new Vector2(0, -150), new Vector2(460, 40), () => _instance.OnRestClicked());
            CreateButton(panel.transform, "Btn_StopBleed", "🩹 Yara Sar & Bandaj Yap", new Vector2(0, -200), new Vector2(460, 40), () => _instance.OnBandageClicked());

            // 4. Kapat Butonu
            CreateButton(panel.transform, "Btn_Close", "Kapat [ESC]", new Vector2(0, -280), new Vector2(460, 45), () => Close());

            panel.SetActive(false);
            DontDestroyOnLoad(uiObj);
        }

        private void Update()
        {
            if (_panelRoot != null && _panelRoot.activeSelf)
            {
                // Açıldıktan hemen sonra (aynı karede) E tuşunu algılayıp geri kapanmasını önle
                if (Time.unscaledTime - _openTime < 0.25f)
                {
                    return;
                }

                var keyboard = UnityEngine.InputSystem.Keyboard.current;
                if (keyboard != null && (keyboard.escapeKey.wasPressedThisFrame || keyboard.eKey.wasPressedThisFrame))
                {
                    Close();
                    return;
                }

                if (_activeFire != null && _statusText != null)
                {
                    int mins = Mathf.FloorToInt(_activeFire.RemainingTime / 60f);
                    int secs = Mathf.FloorToInt(_activeFire.RemainingTime % 60f);

                    var cycle = EndlessSurvival.World.DayNightCycle.Instance;
                    string timeInfo = cycle != null ? $" | {cycle.GetTimeWithIcon()}" : "";

                    _statusText.text = _activeFire.IsBurning 
                        ? $"Ateş: {mins:00}:{secs:00}{timeInfo}"
                        : $"<color=red>Ateş Söndü!</color>{timeInfo}";
                }
            }
        }

        private void RefreshUI()
        {
            if (_feedbackText != null) _feedbackText.text = "";
        }

        // ==========================================
        // BUTON FONKSİYONLARI
        // ==========================================

        private void OnAddFuelClicked()
        {
            if (_activeFire == null || _activeUser == null) return;

            // Envanterden yakacak aranır (FuelCan, ScrapMetal veya genel yakacak)
            ItemDefinition fuelItem = FindItemWithCategory(ItemCategory.Fuel) ?? FindItemNamed("Fuel", "Scrap");
            if (fuelItem != null && _activeUser.Storage.GetCount(fuelItem) > 0)
            {
                _activeUser.Storage.Remove(fuelItem, 1);
                _activeFire.AddFuel(180f); // 3 dakika
                ShowFeedback($"Yakacak eklendi ({fuelItem.DisplayName})! Ateş canlandı.", Color.green);
            }
            else
            {
                // Test kolaylığı: Eğer yakacak yoksa da ateşe küçük dal eklemiş sayılarak 90 saniye eklenir
                _activeFire.AddFuel(90f);
                ShowFeedback("Ateşe çalı çırpı atıldı (+1.5 dakika).", Color.yellow);
            }
        }

        private void OnCookMeatClicked()
        {
            if (_activeFire == null || !_activeFire.IsBurning)
            {
                ShowFeedback("Ateş yanmıyor! Önce ateşi yakmalısın.", Color.red);
                return;
            }

            ItemDefinition rawMeat = FindItemNamed("DriedMeat", "Meat");
            if (rawMeat != null && _activeUser.Storage.GetCount(rawMeat) > 0)
            {
                _activeUser.Storage.Remove(rawMeat, 1);
                ItemDefinition cookedMeat = FindOrCreateCookedItem("Item_CookedMeat", "Pişmiş Et", ItemCategory.Food, 45, 15);
                _activeUser.AddItem(cookedMeat, 1);
                ShowFeedback("Et ateşte nar gibi kızartıldı! (Pişmiş Et envanterine eklendi)", Color.green);
            }
            else
            {
                ShowFeedback("Envanterinde pişirilecek et bulunamadı!", Color.red);
            }
        }

        private void OnCookStewClicked()
        {
            if (_activeFire == null || !_activeFire.IsBurning)
            {
                ShowFeedback("Ateş yanmıyor!", Color.red);
                return;
            }

            ItemDefinition cannedBeans = FindItemNamed("CannedBeans", "Beans");
            if (cannedBeans != null && _activeUser.Storage.GetCount(cannedBeans) > 0)
            {
                _activeUser.Storage.Remove(cannedBeans, 1);
                ItemDefinition stew = FindOrCreateCookedItem("Item_WarmStew", "Sıcak Güveç", ItemCategory.Food, 40, 10);
                _activeUser.AddItem(stew, 1);
                ShowFeedback("Konserve ateşte ısıtıldı: Sıcak Güveç hazırlandı!", Color.green);
            }
            else
            {
                ShowFeedback("Envanterinde konserve fasulye yok!", Color.red);
            }
        }

        private void OnBoilWaterClicked()
        {
            if (_activeFire == null || !_activeFire.IsBurning)
            {
                ShowFeedback("Ateş yanmıyor!", Color.red);
                return;
            }

            ItemDefinition bottle = FindItemNamed("EmptyBottle", "Bottle");
            if (bottle != null && _activeUser.Storage.GetCount(bottle) > 0)
            {
                _activeUser.Storage.Remove(bottle, 1);
                ItemDefinition cleanWater = FindOrCreateCookedItem("Item_BoiledWater", "Kaynamış Temiz Su", ItemCategory.Drink, 50, 5);
                _activeUser.AddItem(cleanWater, 1);
                ShowFeedback("Su ateşte kaynatıldı ve sterilize edildi!", Color.green);
            }
            else
            {
                ShowFeedback("Envanterinde boş şişe yok!", Color.red);
            }
        }

        private void OnSitClicked()
        {
            var player = _activeUser != null ? _activeUser.gameObject : GameObject.FindGameObjectWithTag("Player");
            if (player != null)
            {
                var sittingCtrl = player.GetComponent<PlayerSittingController>();
                if (sittingCtrl == null) sittingCtrl = player.AddComponent<PlayerSittingController>();

                Vector3 dirToFire = (_activeFire != null) ? (_activeFire.transform.position - player.transform.position) : player.transform.forward;
                sittingCtrl.SitDown(dirToFire);
            }
            Close();
        }

        private void OnRestClicked()
        {
            if (_activeFire == null) return;
            StartCoroutine(RestRoutine());
        }

        private IEnumerator RestRoutine()
        {
            Close();

            // Ekran karartma (Fade to black)
            if (_fadeText != null) _fadeText.text = "💤 Kamp ateşinin başında dinleniliyor...";
            if (_fadeOverlay != null)
            {
                for (float t = 0; t <= 1f; t += Time.deltaTime * 2.5f)
                {
                    _fadeOverlay.color = new Color(0, 0, 0, t);
                    yield return null;
                }
            }

            yield return new WaitForSeconds(0.6f);

            // 1. Zamanı İlerlet (Geceye veya Sabaha Geçiş)
            string sleepReport = "Dinlenildi ve can yenilendi.";
            var cycle = EndlessSurvival.World.DayNightCycle.Instance;
            if (cycle != null)
            {
                var result = cycle.SleepToNextPhase();
                sleepReport = result.summary;
            }

            if (_fadeText != null)
            {
                _fadeText.text = sleepReport;
            }

            // 2. Oyuncuyu iyileştir, kanamayı durdur, açlık/susuzluk harca
            var player = _activeUser != null ? _activeUser.gameObject : GameObject.FindGameObjectWithTag("Player");
            if (player != null)
            {
                var health = player.GetComponent<PlayerHealth>();
                if (health != null && !health.IsDead)
                {
                    health.Heal(40f);
                    health.StopBleeding();
                }

                var survival = player.GetComponent<PlayerSurvival>();
                if (survival != null)
                {
                    survival.Eat(-10f);  // Uyurken hafif açlık harcanır
                    survival.Drink(-12f);
                }
            }

            // Ateşin uyku süresince sönmemesi için yakacak desteği
            if (_activeFire != null)
            {
                _activeFire.AddFuel(240f);
            }

            yield return new WaitForSeconds(2.0f);

            // Ekranı aç (Fade from black)
            if (_fadeOverlay != null)
            {
                for (float t = 1f; t >= 0f; t -= Time.deltaTime * 2f)
                {
                    _fadeOverlay.color = new Color(0, 0, 0, t);
                    yield return null;
                }
                _fadeOverlay.color = new Color(0, 0, 0, 0);
            }

            if (_fadeText != null)
            {
                _fadeText.text = "";
            }
        }

        private void OnBandageClicked()
        {
            var player = _activeUser != null ? _activeUser.gameObject : GameObject.FindGameObjectWithTag("Player");
            if (player == null) return;

            var health = player.GetComponent<PlayerHealth>();
            ItemDefinition bandage = FindItemNamed("Bandage", "Medkit");

            if (bandage != null && _activeUser.Storage.GetCount(bandage) > 0)
            {
                _activeUser.Storage.Remove(bandage, 1);
                if (health != null)
                {
                    health.Heal(25f);
                    health.StopBleeding();
                }
                ShowFeedback("Yara sarıldı, kanama durduruldu ve can tazelendi.", Color.green);
            }
            else
            {
                ShowFeedback("Envanterinde bandaj veya medkit kalmamış!", Color.red);
            }
        }

        private void ShowFeedback(string msg, Color col)
        {
            if (_feedbackText != null)
            {
                _feedbackText.text = msg;
                _feedbackText.color = col;
            }
        }

        // ==========================================
        // YARDIMCI ITEM ARAMA
        // ==========================================

        private ItemDefinition FindItemWithCategory(ItemCategory cat)
        {
            if (_activeUser == null || _activeUser.Storage == null) return null;
            foreach (var pair in _activeUser.Storage.Items)
            {
                if (pair.Key != null && pair.Value > 0 && pair.Key.category == cat)
                    return pair.Key;
            }
            return null;
        }

        private ItemDefinition FindItemNamed(params string[] searchTerms)
        {
            if (_activeUser == null || _activeUser.Storage == null) return null;
            foreach (var pair in _activeUser.Storage.Items)
            {
                if (pair.Key != null && pair.Value > 0)
                {
                    foreach (var term in searchTerms)
                    {
                        if (pair.Key.name.Contains(term) || pair.Key.displayName.Contains(term))
                            return pair.Key;
                    }
                }
            }
            return null;
        }

        private ItemDefinition FindOrCreateCookedItem(string assetName, string displayName, ItemCategory cat, int amount, int healBonus)
        {
            string path = $"Assets/Data/Items/{assetName}.asset";
            var item = Resources.Load<ItemDefinition>(assetName);
#if UNITY_EDITOR
            if (item == null)
            {
                item = UnityEditor.AssetDatabase.LoadAssetAtPath<ItemDefinition>(path);
                if (item == null)
                {
                    item = ScriptableObject.CreateInstance<ItemDefinition>();
                    item.displayName = displayName;
                    item.category = cat;
                    item.useAmount = amount;
                    item.weight = 0.4f;
                    item.description = $"Ateşte taze pişirilmiş sıcacık {displayName}.";
                    item.worldColor = new Color(0.8f, 0.45f, 0.2f);
                    UnityEditor.AssetDatabase.CreateAsset(item, path);
                    UnityEditor.AssetDatabase.SaveAssets();
                }
            }
#endif
            return item;
        }

        // ==========================================
        // DİNAMİK UI ELEMANLARI OLUŞTURUCULAR
        // ==========================================

        private static Text CreateText(Transform parent, string name, string content, int fontSize, FontStyle style, Vector2 pos, Vector2 size, Color col)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rect = go.AddComponent<RectTransform>();
            rect.anchoredPosition = pos;
            rect.sizeDelta = size;

            var txt = go.AddComponent<Text>();
            txt.text = content;
            txt.fontSize = fontSize;
            txt.fontStyle = style;
            txt.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf") ?? Resources.GetBuiltinResource<Font>("Arial.ttf");
            txt.alignment = TextAnchor.MiddleCenter;
            txt.color = col;
            return txt;
        }

        private static Button CreateButton(Transform parent, string name, string label, Vector2 pos, Vector2 size, System.Action onClick)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rect = go.AddComponent<RectTransform>();
            rect.anchoredPosition = pos;
            rect.sizeDelta = size;

            var img = go.AddComponent<Image>();
            img.color = new Color(0.2f, 0.22f, 0.26f, 0.9f);

            var btn = go.AddComponent<Button>();
            var colors = btn.colors;
            colors.highlightedColor = new Color(0.35f, 0.4f, 0.48f, 1f);
            colors.pressedColor = new Color(0.15f, 0.16f, 0.2f, 1f);
            btn.colors = colors;

            CreateText(go.transform, "Label", label, 15, FontStyle.Bold, Vector2.zero, size, Color.white);

            if (onClick != null)
            {
                btn.onClick.AddListener(() => onClick());
            }

            return btn;
        }
    }
}
