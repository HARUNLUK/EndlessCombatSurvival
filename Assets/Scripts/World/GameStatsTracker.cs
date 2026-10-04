using UnityEngine;

namespace EndlessSurvival.World
{
    /// <summary>
    /// Tracks run statistics (distance traveled, enemies killed, loot gathered, time survived, and cause of death).
    /// </summary>
    public class GameStatsTracker : MonoBehaviour
    {
        public static GameStatsTracker Instance { get; private set; }

        [Header("Statistics")]
        [Tooltip("Koşulan veya araçla gidilen toplam metre")]
        public float totalDistanceMeters = 0f;

        [Tooltip("Etkisiz hale getirilen toplam düşman sayısı")]
        public int enemiesKilled = 0;

        [Tooltip("Toplanan / envantere eklenen toplam eşya sayısı")]
        public int itemsLooted = 0;

        [Tooltip("Keşfedilen düşman kampı sayısı")]
        public int campsDiscovered = 0;

        [Tooltip("Püskürtülen kamp baskını sayısı")]
        public int ambushesSurvived = 0;

        [Tooltip("Gerçek dünyada geçen oynama süresi (saniye)")]
        public float realTimePlayed = 0f;

        [Tooltip("Ölüm sebebi")]
        public string causeOfDeath = "Yolda Yaşamını Yitirdi";

        private Vector3 _lastPosition;
        private bool _hasLastPos = false;
        private Transform _trackingTarget;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoInitialize()
        {
            if (Instance == null)
            {
                var existing = Object.FindFirstObjectByType<GameStatsTracker>();
                if (existing == null)
                {
                    GameObject go = new GameObject("GameStatsTracker");
                    go.AddComponent<GameStatsTracker>();
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
            ResetRunStats();
        }

        public void ResetRunStats()
        {
            totalDistanceMeters = 0f;
            enemiesKilled = 0;
            itemsLooted = 0;
            campsDiscovered = 0;
            ambushesSurvived = 0;
            realTimePlayed = 0f;
            causeOfDeath = "Yolda Yaşamını Yitirdi";
            _hasLastPos = false;
            _trackingTarget = null;
        }

        private void Update()
        {
            if (GameOverUI.IsGameOver) return;

            realTimePlayed += Time.deltaTime;
            UpdateDistanceTracking();
        }

        private void UpdateDistanceTracking()
        {
            if (_trackingTarget == null || !_trackingTarget.gameObject.activeInHierarchy)
            {
                // Önce oyuncuyu ara, oyuncu araçtaysa (inaktifse) aracı takip et
                var player = GameObject.FindGameObjectWithTag("Player");
                if (player != null && player.activeInHierarchy)
                {
                    _trackingTarget = player.transform;
                }
                else
                {
                    var vehicle = Object.FindFirstObjectByType<EndlessSurvival.Vehicle.VehicleController>();
                    if (vehicle != null && vehicle.gameObject.activeInHierarchy)
                    {
                        _trackingTarget = vehicle.transform;
                    }
                }
            }

            if (_trackingTarget != null)
            {
                Vector3 currentPos = _trackingTarget.position;
                if (_hasLastPos)
                {
                    float delta = Vector3.Distance(currentPos, _lastPosition);
                    // Işınlanma ve sahne yükleme sapmalarını (100m üzeri) filtrele
                    if (delta > 0.02f && delta < 100f)
                    {
                        totalDistanceMeters += delta;
                    }
                }
                _lastPosition = currentPos;
                _hasLastPos = true;
            }
        }

        public void RecordEnemyKilled()
        {
            enemiesKilled++;
            Debug.Log($"<color=orange>[Stats] Düşman etkisiz hale getirildi! Toplam Leş: {enemiesKilled}</color>");
        }

        public void RecordItemLooted(int count = 1)
        {
            itemsLooted += Mathf.Max(1, count);
        }

        public void RecordCampDiscovered()
        {
            campsDiscovered++;
        }

        public void RecordAmbushSurvived()
        {
            ambushesSurvived++;
            Debug.Log($"<color=cyan>[Stats] Kamp baskını başarıyla püskürtüldü! Toplam: {ambushesSurvived}</color>");
        }

        public void SetStats(float distance, int enemies, int items, int camps, int ambushes, float time)
        {
            totalDistanceMeters = distance;
            enemiesKilled = enemies;
            itemsLooted = items;
            campsDiscovered = camps;
            ambushesSurvived = ambushes;
            realTimePlayed = time;
            _hasLastPos = false;
        }

        public void SetCauseOfDeath(string cause)
        {
            causeOfDeath = cause;
        }

        public int CalculateSurvivalScore()
        {
            int distanceScore = Mathf.FloorToInt(totalDistanceMeters / 5f);
            int enemyScore = enemiesKilled * 100;
            int lootScore = itemsLooted * 20;
            int timeScore = Mathf.FloorToInt(realTimePlayed * 2f);
            return distanceScore + enemyScore + lootScore + timeScore;
        }

        public string GetFormattedDistance()
        {
            if (totalDistanceMeters >= 1000f)
            {
                return $"{(totalDistanceMeters / 1000f):0.00} km";
            }
            return $"{Mathf.RoundToInt(totalDistanceMeters)} m";
        }

        public string GetFormattedTimePlayed()
        {
            int mins = Mathf.FloorToInt(realTimePlayed / 60f);
            int secs = Mathf.FloorToInt(realTimePlayed % 60f);
            return $"{mins} dakika {secs} saniye";
        }
    }
}
