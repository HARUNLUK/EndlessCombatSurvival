using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UI;
using EndlessSurvival.World;
using EndlessSurvival.Inventory;
using EndlessSurvival.Vehicle;
using EndlessSurvival.Camping;

namespace EndlessSurvival.Save
{
    /// <summary>
    /// Core Save and Load Manager for Endless Survival.
    /// Handles deterministic seed restoration, world progress, player health/survival/inventory,
    /// vehicle status, stash/trunk contents, read lore notes, and camp placements.
    /// Supports Quick Save (F5), Quick Load (F9), and Auto-save during camping.
    /// </summary>
    public class SaveManager : MonoBehaviour
    {
        public static SaveManager Instance { get; private set; }

        private const string SaveFileName = "survival_save.json";
        private string SaveFilePath => Path.Combine(Application.persistentDataPath, SaveFileName);

        [Header("Read Notes Lore Tracking")]
        private readonly HashSet<string> _readNoteIds = new HashSet<string>();

        // Dynamic Notification UI
        private GameObject _uiCanvasObj;
        private GameObject _bannerObj;
        private Text _bannerText;
        private Image _bannerBg;
        private Coroutine _bannerCoroutine;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoInitialize()
        {
            if (Instance == null)
            {
                var existing = FindFirstObjectByType<SaveManager>();
                if (existing == null)
                {
                    GameObject go = new GameObject("SaveManager");
                    go.AddComponent<SaveManager>();
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
            CreateNotificationUI();
        }

        private void Update()
        {
            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            if (keyboard != null)
            {
                // F5: Hızlı Kayıt (Quick Save)
                if (keyboard.f5Key.wasPressedThisFrame)
                {
                    SaveGame(isAutoSave: false);
                }
                // F9: Hızlı Yükleme (Quick Load)
                else if (keyboard.f9Key.wasPressedThisFrame)
                {
                    LoadGame();
                }
            }
        }

        // ==========================================
        // NOT VE HİKAYE TAKİBİ
        // ==========================================

        public void MarkNoteAsRead(string noteId)
        {
            if (string.IsNullOrEmpty(noteId)) return;
            if (!_readNoteIds.Contains(noteId))
            {
                _readNoteIds.Add(noteId);
                Debug.Log($"<color=cyan>[SaveManager] Not okundu olarak işaretlendi: {noteId}</color>");
            }
        }

        public bool IsNoteRead(string noteId)
        {
            return !string.IsNullOrEmpty(noteId) && _readNoteIds.Contains(noteId);
        }

        public bool HasSaveFile()
        {
            return File.Exists(SaveFilePath);
        }

        public void DeleteSave()
        {
            if (File.Exists(SaveFilePath))
            {
                File.Delete(SaveFilePath);
                Debug.Log("[SaveManager] Kayıt dosyası silindi.");
            }
        }

        // ==========================================
        // OYUN KAYDETME (SAVE)
        // ==========================================

        public bool SaveGame(bool isAutoSave = false)
        {
            try
            {
                SaveData data = new SaveData();
                data.saveTimestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

                // 1. Dünya ve Seed
                var chunkMgr = ChunkManager.Instance;
                if (chunkMgr != null)
                {
                    data.masterSeed = chunkMgr.masterSeed;
                    data.resolvedSeed = chunkMgr.ResolvedSeed;
                }

                // Gün/Gece Saati
                var cycle = DayNightCycle.Instance;
                if (cycle != null)
                {
                    data.currentHour = cycle.CurrentHour;
                }

                // 2. İstatistikler
                var stats = GameStatsTracker.Instance;
                if (stats != null)
                {
                    data.totalDistanceMeters = stats.totalDistanceMeters;
                    data.enemiesKilled = stats.enemiesKilled;
                    data.itemsLooted = stats.itemsLooted;
                    data.campsDiscovered = stats.campsDiscovered;
                    data.ambushesSurvived = stats.ambushesSurvived;
                    data.realTimePlayed = stats.realTimePlayed;
                }

                // 3. Oyuncu Durumu
                var player = GameObject.FindGameObjectWithTag("Player");
                if (player != null)
                {
                    data.playerPosition = player.transform.position;
                    data.playerRotation = player.transform.rotation;

                    var health = player.GetComponent<PlayerHealth>();
                    if (health != null)
                    {
                        data.playerHealth = health.CurrentHealth;
                        data.playerIsBleeding = health.isBleeding;
                    }

                    var survival = player.GetComponent<PlayerSurvival>();
                    if (survival != null)
                    {
                        data.playerHunger = survival.Hunger;
                        data.playerThirst = survival.Thirst;
                        data.playerBladder = survival.Bladder;
                    }

                    var inv = player.GetComponent<PlayerInventory>();
                    if (inv != null)
                    {
                        data.playerMoney = inv.Money;
                        data.playerLooseAmmo = inv.LooseAmmo;

                        if (inv.Storage != null)
                        {
                            foreach (var pair in inv.Storage.Items)
                            {
                                if (pair.Key != null && pair.Value > 0)
                                {
                                    data.playerInventory.Add(new SavedItemSlot(pair.Key.name, pair.Value));
                                }
                            }
                        }
                    }
                }

                // 4. Araç Durumu
                var vehicle = FindFirstObjectByType<VehicleController>();
                var vehicleInteract = FindFirstObjectByType<VehicleInteraction>();
                if (vehicle != null)
                {
                    data.hasVehicle = true;
                    data.isPlayerInsideVehicle = vehicleInteract != null && vehicleInteract.IsPlayerInside;
                    data.vehiclePosition = vehicle.transform.position;
                    data.vehicleRotation = vehicle.transform.rotation;
                    data.vehicleFuel = vehicle.currentFuel;
                    data.vehicleHealth = vehicle.currentHealth;

                    var vehicleStash = vehicle.GetComponent<VehicleStorage>();
                    if (vehicleStash != null && vehicleStash.Storage != null)
                    {
                        foreach (var pair in vehicleStash.Storage.Items)
                        {
                            if (pair.Key != null && pair.Value > 0)
                            {
                                data.vehicleStorage.Add(new SavedItemSlot(pair.Key.name, pair.Value));
                            }
                        }
                    }
                }

                // 5. Okunan Notlar
                data.readNoteIds = new List<string>(_readNoteIds);

                // 6. Oyuncunun Kurduğu Kamp Ateşleri
                var campfires = FindObjectsByType<PlayerCampfire>(FindObjectsSortMode.None);
                foreach (var fire in campfires)
                {
                    if (fire != null && fire.IsBurning)
                    {
                        data.placedCampfires.Add(new SavedCampfire(fire.transform.position, fire.RemainingTime));
                    }
                }

                // JSON'a Çevir ve Kaydet
                string json = JsonUtility.ToJson(data, true);
                File.WriteAllText(SaveFilePath, json);

                string msg = isAutoSave ? "💾 OTOMATİK KAYIT ALINDI (Kamp)" : "💾 OYUN KAYDEDİLDİ [F5]";
                ShowNotification(msg, new Color(0.15f, 0.55f, 0.25f, 0.95f));
                Debug.Log($"<color=green>[SaveManager] Oyun başarıyla kaydedildi: {SaveFilePath}</color>");
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[SaveManager] Kayıt başarısız oldu: {ex.Message}");
                ShowNotification("⚠️ KAYIT HATASI!", new Color(0.75f, 0.15f, 0.15f, 0.95f));
                return false;
            }
        }

        // ==========================================
        // OYUN YÜKLEME (LOAD)
        // ==========================================

        public bool LoadGame()
        {
            if (!HasSaveFile())
            {
                Debug.LogWarning("[SaveManager] Yüklenecek kayıt dosyası bulunamadı.");
                ShowNotification("⚠️ KAYIT BULUNAMADI!", new Color(0.7f, 0.4f, 0.1f, 0.95f));
                return false;
            }

            try
            {
                string json = File.ReadAllText(SaveFilePath);
                SaveData data = JsonUtility.FromJson<SaveData>(json);
                if (data == null)
                {
                    Debug.LogError("[SaveManager] Kayıt dosyası çözümlenemedi.");
                    return false;
                }

                StartCoroutine(ApplyLoadRoutine(data));
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[SaveManager] Yükleme sırasında hata oluştu: {ex.Message}");
                ShowNotification("⚠️ YÜKLEME HATASI!", new Color(0.75f, 0.15f, 0.15f, 0.95f));
                return false;
            }
        }

        private IEnumerator ApplyLoadRoutine(SaveData data)
        {
            // 1. Gün/Gece Saatini Geri Yükle
            var cycle = DayNightCycle.Instance;
            if (cycle != null)
            {
                cycle.SetTime(data.currentHour);
            }

            // 2. İstatistikleri Geri Yükle
            var stats = GameStatsTracker.Instance;
            if (stats != null)
            {
                stats.SetStats(data.totalDistanceMeters, data.enemiesKilled, data.itemsLooted, data.campsDiscovered, data.ambushesSurvived, data.realTimePlayed);
            }

            // 3. Okunan Notları Geri Yükle
            _readNoteIds.Clear();
            if (data.readNoteIds != null)
            {
                foreach (var id in data.readNoteIds) _readNoteIds.Add(id);
            }

            // 4. Araç ve Oyuncu Konumları
            var vehicle = FindFirstObjectByType<VehicleController>();
            var vehicleInteract = FindFirstObjectByType<VehicleInteraction>();
            var player = GameObject.FindGameObjectWithTag("Player");

            if (vehicle != null && data.hasVehicle)
            {
                var rb = vehicle.GetComponent<Rigidbody>();
                if (rb != null)
                {
                    rb.linearVelocity = Vector3.zero;
                    rb.angularVelocity = Vector3.zero;
                }
                vehicle.transform.position = data.vehiclePosition;
                vehicle.transform.rotation = data.vehicleRotation;
                vehicle.currentFuel = data.vehicleFuel;
                vehicle.currentHealth = data.vehicleHealth;

                // Araç Bagajını Yükle
                var vehicleStash = vehicle.GetComponent<VehicleStorage>();
                if (vehicleStash != null)
                {
                    vehicleStash.Storage.Clear();
                    if (data.vehicleStorage != null)
                    {
                        foreach (var slot in data.vehicleStorage)
                        {
                            ItemDefinition def = FindItemDefinition(slot.itemName);
                            if (def != null) vehicleStash.Storage.Add(def, slot.amount);
                        }
                    }
                }
            }

            if (player != null)
            {
                var cc = player.GetComponent<CharacterController>();
                if (cc != null) cc.enabled = false;

                player.transform.position = data.playerPosition;
                player.transform.rotation = data.playerRotation;

                if (cc != null) cc.enabled = true;

                // Can ve Kanama
                var health = player.GetComponent<PlayerHealth>();
                if (health != null)
                {
                    health.SetHealthAndBleed(data.playerHealth, data.playerIsBleeding);
                }

                // Hayatta Kalma Değerleri
                var survival = player.GetComponent<PlayerSurvival>();
                if (survival != null)
                {
                    survival.SetValues(data.playerHunger, data.playerThirst, data.playerBladder);
                }

                // Oyuncu Envanteri
                var inv = player.GetComponent<PlayerInventory>();
                if (inv != null)
                {
                    inv.Storage.Clear();
                    if (data.playerInventory != null)
                    {
                        foreach (var slot in data.playerInventory)
                        {
                            ItemDefinition def = FindItemDefinition(slot.itemName);
                            if (def != null) inv.Storage.Add(def, slot.amount);
                        }
                    }
                }
            }

            // Eğer oyuncu araç içindeyken kaydedilmişse araca bindir
            if (data.isPlayerInsideVehicle && vehicleInteract != null && !vehicleInteract.IsPlayerInside)
            {
                vehicleInteract.EnterVehicle();
            }

            yield return null;

            ShowNotification("📂 OYUN BAŞARIYLA YÜKLENDİ [F9]", new Color(0.12f, 0.45f, 0.75f, 0.95f));
            Debug.Log("<color=green>[SaveManager] Kayıt başarıyla yüklendi ve sahne güncellendi!</color>");
        }

        private ItemDefinition FindItemDefinition(string itemName)
        {
            if (string.IsNullOrEmpty(itemName)) return null;

            // 1. Önce Resources ve Sahnedeki tüm ItemDefinition'lar
            var allItems = Resources.FindObjectsOfTypeAll<ItemDefinition>();
            foreach (var item in allItems)
            {
                if (item.name == itemName || item.displayName == itemName)
                    return item;
            }

            // 2. ItemDatabase üzerinden ara
            var db = Resources.Load<ItemDatabase>("ItemDatabase");
#if UNITY_EDITOR
            if (db == null)
            {
                db = UnityEditor.AssetDatabase.LoadAssetAtPath<ItemDatabase>("Assets/Data/ItemDatabase.asset");
            }
#endif
            if (db != null && db.items != null)
            {
                foreach (var item in db.items)
                {
                    if (item != null && (item.name == itemName || item.displayName == itemName))
                        return item;
                }
            }

            return null;
        }

        // ==========================================
        // DİNAMİK BİLDİRİM (NOTIFICATION) UI
        // ==========================================

        private void CreateNotificationUI()
        {
            if (_uiCanvasObj != null) return;

            _uiCanvasObj = new GameObject("SaveManager_Canvas");
            var canvas = _uiCanvasObj.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 99;

            var scaler = _uiCanvasObj.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);

            _uiCanvasObj.AddComponent<GraphicRaycaster>();
            DontDestroyOnLoad(_uiCanvasObj);

            // Bildirim Paneli (Sağ alt köşe)
            _bannerObj = new GameObject("SaveNotificationBanner");
            _bannerObj.transform.SetParent(_uiCanvasObj.transform, false);

            var rect = _bannerObj.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(1f, 0f);
            rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot = new Vector2(1f, 0f);
            rect.anchoredPosition = new Vector2(-30f, 30f);
            rect.sizeDelta = new Vector2(360f, 50f);

            _bannerBg = _bannerObj.AddComponent<Image>();
            _bannerBg.color = new Color(0.12f, 0.45f, 0.75f, 0.95f);

            // Metin
            GameObject textObj = new GameObject("Text");
            textObj.transform.SetParent(_bannerObj.transform, false);
            var textRect = textObj.AddComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(15, 0);
            textRect.offsetMax = new Vector2(-15, 0);

            _bannerText = textObj.AddComponent<Text>();
            _bannerText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf") ?? Resources.GetBuiltinResource<Font>("Arial.ttf");
            _bannerText.fontSize = 16;
            _bannerText.fontStyle = FontStyle.Bold;
            _bannerText.alignment = TextAnchor.MiddleCenter;
            _bannerText.color = Color.white;

            _bannerObj.SetActive(false);
        }

        private void ShowNotification(string message, Color bgColor)
        {
            if (_bannerObj == null) CreateNotificationUI();

            _bannerText.text = message;
            _bannerBg.color = bgColor;
            _bannerObj.SetActive(true);

            if (_bannerCoroutine != null) StopCoroutine(_bannerCoroutine);
            _bannerCoroutine = StartCoroutine(HideNotificationAfterDelay(3.5f));
        }

        private IEnumerator HideNotificationAfterDelay(float delay)
        {
            yield return new WaitForSeconds(delay);
            if (_bannerObj != null) _bannerObj.SetActive(false);
        }
    }
}
