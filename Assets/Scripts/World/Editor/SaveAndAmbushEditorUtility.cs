#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using EndlessSurvival.Save;
using EndlessSurvival.Camping;

namespace EndlessSurvival.Editor
{
    public static class SaveAndAmbushEditorUtility
    {
        [MenuItem("Endless Survival/Save/Quick Save Game (F5)", false, 100)]
        public static void QuickSave()
        {
            if (!Application.isPlaying)
            {
                EditorUtility.DisplayDialog("Save System", "Kayıt almak için oyunun Play Mode'da çalışıyor olması gerekir.", "Tamam");
                return;
            }

            if (SaveManager.Instance != null)
            {
                SaveManager.Instance.SaveGame(isAutoSave: false);
            }
        }

        [MenuItem("Endless Survival/Save/Quick Load Game (F9)", false, 101)]
        public static void QuickLoad()
        {
            if (!Application.isPlaying)
            {
                EditorUtility.DisplayDialog("Save System", "Kayıt yüklemek için oyunun Play Mode'da çalışıyor olması gerekir.", "Tamam");
                return;
            }

            if (SaveManager.Instance != null)
            {
                SaveManager.Instance.LoadGame();
            }
        }

        [MenuItem("Endless Survival/Save/Delete Save File", false, 102)]
        public static void DeleteSave()
        {
            if (SaveManager.Instance != null)
            {
                SaveManager.Instance.DeleteSave();
                EditorUtility.DisplayDialog("Save System", "Kayıt dosyası başarıyla silindi.", "Tamam");
            }
        }

        [MenuItem("Endless Survival/Events/Trigger Camp Ambush (F8)", false, 120)]
        public static void TriggerCampAmbush()
        {
            if (!Application.isPlaying)
            {
                EditorUtility.DisplayDialog("Camp Ambush", "Kamp baskınını tetiklemek için oyunun Play Mode'da olması gerekir.", "Tamam");
                return;
            }

            var player = GameObject.FindGameObjectWithTag("Player");
            if (player != null && CampAmbushManager.Instance != null)
            {
                CampAmbushManager.Instance.TriggerAmbush(player.transform.position);
            }
            else
            {
                EditorUtility.DisplayDialog("Camp Ambush", "Oyuncu veya CampAmbushManager bulunamadı.", "Tamam");
            }
        }
    }
}
#endif
