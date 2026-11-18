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
    /// One journey file per character slot (CharacterProfiles), written automatically: when sleeping, at a new zone,
    /// every few minutes while it is calm, when leaving to the menu and when the game closes. There is no manual
    /// quick save or quick load. Dying ends the journey: the file is removed, the character's profile (stats,
    /// read notes, records) is kept.
    /// </summary>
    public class SaveManager : MonoBehaviour
    {
        public static SaveManager Instance { get; private set; }

        /// <summary>Raised while a save is built: add <see cref="SavedExtension"/> entries to data.extensions.</summary>
        public static event Action<SaveData> CollectingSave;
        /// <summary>Raised while a save is applied, after the player and vehicle state.</summary>
        public static event Action<SaveData> ApplyingLoad;

        // Journey file of the active character slot (a separate test folder while no character is selected)
        private string SaveFilePath => CharacterProfiles.ActiveRunPath;

        [Header("Auto save")]
        [Tooltip("Seconds between the silent automatic saves while no enemy is near")]
        public float autoSaveSeconds = 300f;

        private float _autoTimer;
        private bool _runEnded;

        /// <summary>False on the main menu, after death and once the journey has been closed.</summary>
        // Test scenes (Assets/Scenes/Test) never save: they would overwrite the real journey
        public bool CanSave => !_runEnded && !GameOverUI.IsGameOver && !EndlessSurvival.Survival.SurvivalUIState.MenuOpen && !EndlessSurvival.Testing.TestArena.Active;

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
            CharacterProfiles.Changed += OnCharacterChanged;
            UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private void OnDestroy()
        {
            if (Instance != this) return;
            CharacterProfiles.Changed -= OnCharacterChanged;
            UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        private void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode)
        {
            _runEnded = false;
            _autoTimer = 0f;
        }

        /// <summary>The read notes belong to the character, not to one journey.</summary>
        private void OnCharacterChanged()
        {
            _readNoteIds.Clear();
            var p = CharacterProfiles.Current;
            if (p?.readNoteIds == null) return;
            foreach (var id in p.readNoteIds) _readNoteIds.Add(id);
        }

        private void OnApplicationQuit()
        {
            // Closing the game is "save and quit" (also when play mode stops in the editor)
            if (CharacterProfiles.Active >= 0 && CanSave && EndlessSurvival.Survival.SurvivalDirector.Player != null)
                SaveGame(isAutoSave: true, silent: true);
        }

        private void Update()
        {
            // Silent safety save every few minutes, only for a selected character and only while it is calm
            if (CharacterProfiles.Active < 0 || !CanSave || Time.timeScale < 0.01f) return;
            if (EndlessSurvival.Survival.SurvivalDirector.Player == null) return;
            _autoTimer += Time.unscaledDeltaTime;
            if (_autoTimer < autoSaveSeconds) return;
            if (EndlessSurvival.Survival.SurvivalDirector.DangerNearby(EndlessSurvival.Survival.SurvivalDirector.PlayerPosition, 60f))
            {
                _autoTimer = autoSaveSeconds - 20f; // try again shortly
                return;
            }
            SaveGame(isAutoSave: true, silent: true);
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
                var profile = CharacterProfiles.Current;
                if (profile != null && !profile.readNoteIds.Contains(noteId))
                {
                    profile.readNoteIds.Add(noteId);
                    CharacterProfiles.SaveCurrent();
                }
                Debug.Log($"<color=cyan>[SaveManager] Not okundu olarak işaretlendi: {noteId}</color>");
            }
        }

        public bool IsNoteRead(string noteId)
        {
            return !string.IsNullOrEmpty(noteId) && _readNoteIds.Contains(noteId);
        }

        /// <summary>World seed stored in the save file (null when there is no readable save). Used by the main menu.</summary>
        public string PeekSavedSeed()
        {
            try
            {
                if (!HasSaveFile()) return null;
                var data = SaveMigration.ReadSafe<SaveData>(SaveFilePath);
                return data != null ? data.masterSeed : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        public bool HasSaveFile()
        {
            return SaveMigration.ExistsWithBackup(SaveFilePath);
        }

        public void DeleteSave()
        {
            if (EndlessSurvival.Testing.TestArena.Active) return;
            if (SaveMigration.ExistsWithBackup(SaveFilePath))
            {
                SaveMigration.DeleteWithBackup(SaveFilePath);
                Debug.Log("[SaveManager] Kayıt dosyası silindi.");
            }
        }

        /// <summary>
        /// Closes the journey: the character keeps what it learned (stats, read notes, records), the journey file
        /// is removed. death = the character died (counted on the profile); false = the journey was given up or
        /// finished and a new one starts.
        /// </summary>
        public void EndRun(bool death)
        {
            if (_runEnded) return;
            BankProfile(death);
            _runEnded = true;
            CharacterProfiles.DeleteRun(CharacterProfiles.Active);
            Debug.Log($"[SaveManager] Journey closed (death={death}); the character profile is kept.");
        }

        /// <summary>Writes what the character keeps for good into its profile.</summary>
        private void BankProfile(bool death)
        {
            var p = CharacterProfiles.Current;
            if (p == null) return;

            var stats = EndlessSurvival.Survival.CharacterStats.Instance;
            if (stats != null) p.stats = stats.BaseValues();
            p.readNoteIds = new List<string>(_readNoteIds);

            var journey = EndlessSurvival.Survival.Journey.Instance;
            if (journey != null)
            {
                p.bestKm = Mathf.Max(p.bestKm, journey.MaxKm);
                p.runKm = journey.MaxKm;
                p.runDay = EndlessSurvival.Survival.Journey.Day;
            }
            var tracker = GameStatsTracker.Instance;
            if (tracker != null) p.bestScore = Mathf.Max(p.bestScore, tracker.CalculateSurvivalScore());
            if (death) p.deaths++;
            CharacterProfiles.SaveCurrent();
        }

        // ==========================================
        // OYUN KAYDETME (SAVE)
        // ==========================================

        public bool SaveGame(bool isAutoSave = false, bool silent = false)
        {
            if (!CanSave)
            {
                Debug.LogWarning("[SaveManager] Save skipped: no journey is running (menu, death or journey closed).");
                return false;
            }
            _autoTimer = 0f;
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
                        data.playerPistolAmmo = inv.Count(EndlessCombat.Combat.AmmoKind.Pistol);
                        data.playerRifleAmmo = inv.Count(EndlessCombat.Combat.AmmoKind.Rifle);
                        data.playerShotgunAmmo = inv.Count(EndlessCombat.Combat.AmmoKind.Shotgun);

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

                    // Worn clothes, weapons on the body, shield
                    var equipment = player.GetComponent<PlayerEquipment>();
                    if (equipment != null) equipment.CaptureSave(data);
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

                    var vehicleMods = vehicle.GetComponent<VehicleModifications>();
                    if (vehicleMods != null)
                    {
                        data.installedVehicleMods = vehicleMods.GetInstalledModNames();
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

                // 7. Hayatta kalma sistemleri kendi verilerini ekler
                CollectingSave?.Invoke(data);

                // JSON'a Çevir ve Kaydet
                data.saveVersion = SaveMigration.RunVersion;
                string json = JsonUtility.ToJson(data, true);
                SaveMigration.WriteSafe(SaveFilePath, json);
                BankProfile(false);

                if (!silent) ShowNotification("OYUN KAYDEDİLDİ", new Color(0.15f, 0.55f, 0.25f, 0.95f));
                Debug.Log($"<color=green>[SaveManager] Oyun başarıyla kaydedildi: {SaveFilePath}</color>");
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[SaveManager] Kayıt başarısız oldu: {ex.Message}");
                ShowNotification("KAYIT HATASI!", new Color(0.75f, 0.15f, 0.15f, 0.95f));
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
                ShowNotification("KAYIT BULUNAMADI!", new Color(0.7f, 0.4f, 0.1f, 0.95f));
                return false;
            }

            try
            {
                SaveData data = SaveMigration.ReadSafe<SaveData>(SaveFilePath, out bool fromBackup);
                if (data == null)
                {
                    Debug.LogError("[SaveManager] Kayıt dosyası çözümlenemedi.");
                    ShowNotification("KAYIT OKUNAMADI", new Color(0.75f, 0.15f, 0.15f, 0.95f));
                    return false;
                }
                SaveMigration.MigrateRun(data);
                if (fromBackup) ShowNotification("KAYIT BOZUKTU, YEDEK YÜKLENDİ", new Color(0.7f, 0.4f, 0.1f, 0.95f));

                StartCoroutine(ApplyLoadRoutine(data));
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[SaveManager] Yükleme sırasında hata oluştu: {ex.Message}");
                ShowNotification("YÜKLEME HATASI!", new Color(0.75f, 0.15f, 0.15f, 0.95f));
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
            // Notes the character read on earlier journeys stay read
            var charProfile = CharacterProfiles.Current;
            if (charProfile?.readNoteIds != null)
            {
                foreach (var id in charProfile.readNoteIds) _readNoteIds.Add(id);
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

                // Araç Modifikasyonlarını Geri Yükle
                var vehicleMods = vehicle.GetComponent<VehicleModifications>();
                if (vehicleMods != null)
                {
                    vehicleMods.RestoreFromSave(data.installedVehicleMods);
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

                    // Equipment first (swapping a weapon returns its magazine to the pool), then money and the ammo pools
                    var equipment = player.GetComponent<PlayerEquipment>();
                    if (equipment != null) equipment.RestoreSave(data, FindItemDefinition);

                    inv.SetMoney(data.playerMoney);
                    int pistol = data.playerPistolAmmo, rifle = data.playerRifleAmmo, shells = data.playerShotgunAmmo;
                    if (pistol + rifle + shells == 0 && data.playerLooseAmmo > 0) rifle = data.playerLooseAmmo; // save from before the ammo kinds
                    inv.SetAmmo(EndlessCombat.Combat.AmmoKind.Pistol, pistol);
                    inv.SetAmmo(EndlessCombat.Combat.AmmoKind.Rifle, rifle);
                    inv.SetAmmo(EndlessCombat.Combat.AmmoKind.Shotgun, shells);
                }
            }

            // Hayatta kalma sistemlerinin verileri
            ApplyingLoad?.Invoke(data);

            // Eğer oyuncu araç içindeyken kaydedilmişse araca bindir
            if (data.isPlayerInsideVehicle && vehicleInteract != null && !vehicleInteract.IsPlayerInside)
            {
                vehicleInteract.EnterVehicle();
            }

            yield return null;

            ShowNotification("YOLCULUK YÜKLENDİ", new Color(0.12f, 0.45f, 0.75f, 0.95f));
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

        public void ShowNotification(string message, Color bgColor)
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
