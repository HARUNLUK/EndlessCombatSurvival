using UnityEngine;
using UnityEditor;
using EndlessSurvival.World;

namespace EndlessSurvival.World.Editor
{
    public static class GameOverSetupUtility
    {
        [MenuItem("Endless Survival/Setup Game Over & Stats Tracker")]
        [MenuItem("Tools/Setup Game Over & Stats Tracker")]
        public static void SetupGameOverAndStats()
        {
            Debug.Log("<color=cyan>[GameOverSetup] Game Over ve İstatistik Takip Sistemi Kuruluyor...</color>");

            // 1. GameStatsTracker
            var tracker = Object.FindFirstObjectByType<GameStatsTracker>();
            if (tracker == null)
            {
                GameObject trackerObj = new GameObject("GameStatsTracker");
                tracker = trackerObj.AddComponent<GameStatsTracker>();
                Undo.RegisterCreatedObjectUndo(trackerObj, "Create GameStatsTracker");
                Debug.Log("[GameOverSetup] GameStatsTracker nesnesi oluşturuldu.");
            }

            Debug.Log("<color=green>[GameOverSetup] KURULUM TAMAMLANDI! Oyuncu öldüğünde veya yolculuk bittiğinde istatistikler ve Game Over ekranı görüntülenecek.</color>");
        }

        [MenuItem("Endless Survival/Test Game Over Screen (Play Mode Only)")]
        public static void TestGameOverScreen()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("[GameOverSetup] Game Over ekranını test etmek için önce oyunu başlatmalısınız (Play Mode).");
                return;
            }

            if (GameStatsTracker.Instance != null)
            {
                GameStatsTracker.Instance.totalDistanceMeters = 3450f;
                GameStatsTracker.Instance.enemiesKilled = 7;
                GameStatsTracker.Instance.itemsLooted = 23;
                GameStatsTracker.Instance.realTimePlayed = 425f;
                GameStatsTracker.Instance.SetCauseOfDeath("Test Simülasyonu (Düşman Ateşi)");
            }

            GameOverUI.Show("Test Simülasyonu (Düşman Ateşi)");
        }
    }
}
