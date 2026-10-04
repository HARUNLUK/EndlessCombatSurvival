using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using EndlessCombat.AI;
using EndlessSurvival.Inventory;
using EndlessSurvival.World;

namespace EndlessSurvival.Camping
{
    /// <summary>
    /// Manages the Camp Ambush event (Adım 7.3 / 4.7-4).
    /// When the player rests or maintains a camp, enemies can track the campfire smoke/light and assault the campsite.
    /// Defeating the ambush rewards the player with high-value military supplies and survival stats.
    /// </summary>
    public class CampAmbushManager : MonoBehaviour
    {
        public static CampAmbushManager Instance { get; private set; }

        [Header("Ambush Chance Settings")]
        [Tooltip("Gündüz dinlenildiğinde baskın çıkma olasılığı (0-1)")]
        [Range(0f, 1f)]
        public float dayAmbushChance = 0.35f;

        [Tooltip("Gece dinlenildiğinde ateşin ışığı ve dumanı nedeniyle baskın çıkma olasılığı (0-1)")]
        [Range(0f, 1f)]
        public float nightAmbushChance = 0.65f;

        [Header("Squad Settings")]
        [Tooltip("Baskın yapacak minimum düşman sayısı")]
        public int minEnemies = 2;

        [Tooltip("Baskın yapacak maksimum düşman sayısı")]
        public int maxEnemies = 4;

        [Tooltip("Düşmanların kampa olan spawn mesafesi (metre)")]
        public float spawnDistance = 28f;

        [Header("Prefabs & Rewards")]
        public GameObject enemyPrefab;
        public GameObject rewardChestPrefab;
        public LootTable rewardLootTable;

        [Header("Status")]
        public bool IsAmbushActive { get; private set; }
        private readonly List<EnemyController> _activeAmbushEnemies = new List<EnemyController>();
        private Vector3 _currentCampPosition;

        // UI Canvas & Elements
        private GameObject _uiCanvasObj;
        private GameObject _bannerPanel;
        private Text _bannerTitleText;
        private Text _bannerSubtitleText;
        private Image _bannerBg;
        private Coroutine _hideBannerRoutine;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoInitialize()
        {
            if (Instance == null)
            {
                var existing = FindFirstObjectByType<CampAmbushManager>();
                if (existing == null)
                {
                    GameObject go = new GameObject("CampAmbushManager");
                    go.AddComponent<CampAmbushManager>();
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
            LoadDefaultAssets();
            CreateUI();
        }

        private void LoadDefaultAssets()
        {
#if UNITY_EDITOR
            if (enemyPrefab == null)
                enemyPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Enemies/Enemy.prefab");

            if (rewardChestPrefab == null)
                rewardChestPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Loot/Chest_Cube.prefab") ??
                                    UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Loot/DefaultChest.prefab");

            if (rewardLootTable == null)
                rewardLootTable = UnityEditor.AssetDatabase.LoadAssetAtPath<LootTable>("Assets/Data/Loot/Loot_Chest.asset") ??
                                  UnityEditor.AssetDatabase.LoadAssetAtPath<LootTable>("Assets/Prefabs/Loot/DefaultLootTable.asset");
#endif
            if (enemyPrefab == null)
                enemyPrefab = Resources.Load<GameObject>("Enemy");
            if (rewardChestPrefab == null)
                rewardChestPrefab = Resources.Load<GameObject>("Chest_Cube");
        }

        private void Update()
        {
            if (IsAmbushActive)
            {
                UpdateAmbushState();
            }

            // Test / Debug Kısayolu: Klavyeden F8 veya Numpad 9 tuşu ile anında kamp baskını simülasyonu
            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            if (keyboard != null && (keyboard.f8Key.wasPressedThisFrame || keyboard.numpad9Key.wasPressedThisFrame))
            {
                var player = GameObject.FindGameObjectWithTag("Player");
                if (player != null)
                {
                    Debug.Log("<color=yellow>[CampAmbushManager] Manuel test baskını tetiklendi!</color>");
                    TriggerAmbush(player.transform.position);
                }
            }
        }

        /// <summary>
        /// Kamp ateşinde uyunurken baskın ihtimalini kontrol eder.
        /// </summary>
        public bool RollAmbushOnSleep(Vector3 campPosition)
        {
            var cycle = DayNightCycle.Instance;
            bool isNight = cycle != null && cycle.IsNight;
            float chance = isNight ? nightAmbushChance : dayAmbushChance;

            if (Random.value <= chance)
            {
                TriggerAmbush(campPosition);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Belirtilen kamp koordinatında düşman baskın dalgası başlatır.
        /// </summary>
        public void TriggerAmbush(Vector3 campPosition)
        {
            if (IsAmbushActive) return;

            _currentCampPosition = campPosition;
            IsAmbushActive = true;
            _activeAmbushEnemies.Clear();

            var player = GameObject.FindGameObjectWithTag("Player");
            Vector3 playerPos = player != null ? player.transform.position : campPosition;

            int count = Random.Range(minEnemies, maxEnemies + 1);
            float baseAngle = Random.Range(0f, 360f);

            for (int i = 0; i < count; i++)
            {
                // Kamp etrafında 90 derecelik bir yay açısı boyunca düşmanları grupla
                float angle = baseAngle + (i - (count - 1) * 0.5f) * (80f / Mathf.Max(1, count));
                float rad = angle * Mathf.Deg2Rad;
                Vector3 offset = new Vector3(Mathf.Cos(rad), 0f, Mathf.Sin(rad)) * (spawnDistance + Random.Range(-3f, 4f));
                Vector3 spawnPos = campPosition + offset;

                // Arazi yüksekliğini raycast veya Terrain ile belirle
                if (Physics.Raycast(spawnPos + Vector3.up * 100f, Vector3.down, out RaycastHit hit, 200f))
                {
                    spawnPos.y = hit.point.y + 0.1f;
                }

                GameObject enemyObj = null;
                if (enemyPrefab != null)
                {
                    enemyObj = Instantiate(enemyPrefab, spawnPos, Quaternion.identity);
                }
                else
                {
                    enemyObj = CreateFallbackEnemy(spawnPos);
                }

                if (enemyObj != null)
                {
                    var enemyCtrl = enemyObj.GetComponent<EnemyController>();
                    if (enemyCtrl != null)
                    {
                        if (player != null) enemyCtrl.target = player.transform;
                        enemyCtrl.SetState(EnemyController.EnemyState.Combat);
                        enemyCtrl.suspicionMeter = 100f;
                        enemyObj.transform.LookAt(new Vector3(playerPos.x, enemyObj.transform.position.y, playerPos.z));
                        _activeAmbushEnemies.Add(enemyCtrl);
                    }
                }
            }

            ShowBanner("⚠️ KAMP BASKINI!", $"Düşmanlar kamp ateşini bastı! ({_activeAmbushEnemies.Count} Düşman)", new Color(0.85f, 0.15f, 0.15f, 0.92f), 8f);
            Debug.Log($"<color=red>[CampAmbushManager] Kamp Baskını Başladı! {_activeAmbushEnemies.Count} düşman kampa saldırıyor.</color>");
        }

        private void UpdateAmbushState()
        {
            // Ölen veya yok edilen düşmanları listeden çıkar
            _activeAmbushEnemies.RemoveAll(e => e == null || e.IsDead || !e.gameObject.activeInHierarchy);

            if (_activeAmbushEnemies.Count > 0)
            {
                if (_bannerSubtitleText != null && _bannerPanel != null && _bannerPanel.activeSelf)
                {
                    _bannerSubtitleText.text = $"Kalan Düşman: {_activeAmbushEnemies.Count}";
                }
            }
            else
            {
                // Bütün baskıncılar öldürüldü: Baskın püskürtüldü!
                ResolveAmbush();
            }
        }

        private void ResolveAmbush()
        {
            IsAmbushActive = false;
            _activeAmbushEnemies.Clear();

            // İstatistik ve skor kaydı
            GameStatsTracker.Instance?.RecordAmbushSurvived();

            // Ödül Sandığını Kamp Alanına Bırak
            SpawnRewardChest(_currentCampPosition);

            ShowBanner("🎉 BASKIN PÜSKÜRTÜLDÜ!", "Düşmanlar temizlendi! Askeri ganimet sandığı kampa bırakıldı.", new Color(0.12f, 0.65f, 0.28f, 0.95f), 7f);
            Debug.Log("<color=green>[CampAmbushManager] Baskın başarıyla püskürtüldü ve ganimet sandığı oluşturuldu!</color>");
        }

        private void SpawnRewardChest(Vector3 campPos)
        {
            Vector3 chestPos = campPos + Vector3.right * 2.2f + Vector3.forward * 1.0f;
            if (Physics.Raycast(chestPos + Vector3.up * 50f, Vector3.down, out RaycastHit hit, 100f))
            {
                chestPos.y = hit.point.y + 0.05f;
            }

            GameObject chestObj = null;
            if (rewardChestPrefab != null)
            {
                chestObj = Instantiate(rewardChestPrefab, chestPos, Quaternion.identity);
            }
            else
            {
                chestObj = GameObject.CreatePrimitive(PrimitiveType.Cube);
                chestObj.name = "AmbushRewardChest";
                chestObj.transform.position = chestPos + Vector3.up * 0.4f;
                chestObj.transform.localScale = new Vector3(0.9f, 0.7f, 0.9f);
                var rend = chestObj.GetComponent<Renderer>();
                if (rend != null) rend.material.color = new Color(0.35f, 0.45f, 0.2f);
            }

            var chest = chestObj.GetComponent<LootChest>();
            if (chest == null) chest = chestObj.AddComponent<LootChest>();

            if (rewardLootTable != null)
            {
                chest.lootTable = rewardLootTable;
            }

            // Garanti zengin içerik takviyesi
            chest.Storage.Add(FindItem("Bandage") ?? FindItem("Medkit"), 2);
            chest.Storage.Add(FindItem("CookedMeat") ?? FindItem("DriedMeat"), 2);
            chest.Storage.Add(FindItem("Fuel") ?? FindItem("Scrap"), 1);

            chestObj.name = "AmbushReward_MilitaryChest";
        }

        private ItemDefinition FindItem(string nameKeyword)
        {
            var allItems = Resources.FindObjectsOfTypeAll<ItemDefinition>();
            foreach (var it in allItems)
            {
                if (it.name.Contains(nameKeyword) || it.displayName.Contains(nameKeyword))
                    return it;
            }
            return null;
        }

        private GameObject CreateFallbackEnemy(Vector3 pos)
        {
            GameObject e = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            e.name = "Ambush_Raider";
            e.transform.position = pos;
            e.tag = "Enemy";

            var rend = e.GetComponent<Renderer>();
            if (rend != null) rend.material.color = new Color(0.8f, 0.2f, 0.2f);

            var ctrl = e.AddComponent<EnemyController>();
            return e;
        }

        // ==========================================
        // DİNAMİK HUD BANNER OLUŞTURUCU
        // ==========================================

        private void CreateUI()
        {
            if (_uiCanvasObj != null) return;

            _uiCanvasObj = new GameObject("AmbushHUD_Canvas");
            var canvas = _uiCanvasObj.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 95;

            var scaler = _uiCanvasObj.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);

            _uiCanvasObj.AddComponent<GraphicRaycaster>();
            DontDestroyOnLoad(_uiCanvasObj);

            // Banner Container (Ekranın üst-orta kısmı)
            _bannerPanel = new GameObject("BannerPanel");
            _bannerPanel.transform.SetParent(_uiCanvasObj.transform, false);
            var rect = _bannerPanel.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 1f);
            rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(0f, -40f);
            rect.sizeDelta = new Vector2(620f, 90f);

            _bannerBg = _bannerPanel.AddComponent<Image>();
            _bannerBg.color = new Color(0.1f, 0.12f, 0.16f, 0.92f);

            // Başlık Metni
            GameObject titleObj = new GameObject("TitleText");
            titleObj.transform.SetParent(_bannerPanel.transform, false);
            var titleRect = titleObj.AddComponent<RectTransform>();
            titleRect.anchorMin = new Vector2(0f, 0.45f);
            titleRect.anchorMax = new Vector2(1f, 1f);
            titleRect.offsetMin = new Vector2(10, 0);
            titleRect.offsetMax = new Vector2(-10, -5);

            _bannerTitleText = titleObj.AddComponent<Text>();
            _bannerTitleText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf") ?? Resources.GetBuiltinResource<Font>("Arial.ttf");
            _bannerTitleText.fontSize = 24;
            _bannerTitleText.fontStyle = FontStyle.Bold;
            _bannerTitleText.alignment = TextAnchor.MiddleCenter;
            _bannerTitleText.color = Color.white;
            _bannerTitleText.text = "⚠️ KAMP BASKINI!";

            // Alt Açıklama Metni
            GameObject subObj = new GameObject("SubtitleText");
            subObj.transform.SetParent(_bannerPanel.transform, false);
            var subRect = subObj.AddComponent<RectTransform>();
            subRect.anchorMin = new Vector2(0f, 0f);
            subRect.anchorMax = new Vector2(1f, 0.45f);
            subRect.offsetMin = new Vector2(10, 5);
            subRect.offsetMax = new Vector2(-10, 0);

            _bannerSubtitleText = subObj.AddComponent<Text>();
            _bannerSubtitleText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf") ?? Resources.GetBuiltinResource<Font>("Arial.ttf");
            _bannerSubtitleText.fontSize = 17;
            _bannerSubtitleText.fontStyle = FontStyle.Normal;
            _bannerSubtitleText.alignment = TextAnchor.MiddleCenter;
            _bannerSubtitleText.color = new Color(0.9f, 0.9f, 0.9f);
            _bannerSubtitleText.text = "Düşmanları etkisiz hale getir!";

            _bannerPanel.SetActive(false);
        }

        public void ShowBanner(string title, string subtitle, Color bgColor, float duration)
        {
            if (_bannerPanel == null) CreateUI();

            _bannerTitleText.text = title;
            _bannerSubtitleText.text = subtitle;
            _bannerBg.color = bgColor;
            _bannerPanel.SetActive(true);

            if (_hideBannerRoutine != null) StopCoroutine(_hideBannerRoutine);
            _hideBannerRoutine = StartCoroutine(HideBannerAfterDelay(duration));
        }

        private IEnumerator HideBannerAfterDelay(float delay)
        {
            yield return new WaitForSeconds(delay);
            if (!IsAmbushActive && _bannerPanel != null)
            {
                _bannerPanel.SetActive(false);
            }
        }
    }
}
