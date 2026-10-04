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

        [MenuItem("Endless Survival/Vehicle/Open Modification Workshop (U)", false, 140)]
        public static void OpenVehicleWorkshop()
        {
            if (!Application.isPlaying)
            {
                EditorUtility.DisplayDialog("Vehicle Workshop", "Atölyeyi açmak için oyunun Play Mode'da olması gerekir.", "Tamam");
                return;
            }

            Vehicle.VehicleModificationUI.Open();
        }

        [MenuItem("Endless Survival/Vehicle/Install All Modifications (Test)", false, 141)]
        public static void InstallAllMods()
        {
            if (!Application.isPlaying)
            {
                EditorUtility.DisplayDialog("Vehicle Workshop", "Modifiye takmak için oyunun Play Mode'da olması gerekir.", "Tamam");
                return;
            }

            var vehicle = Object.FindFirstObjectByType<Vehicle.VehicleController>();
            if (vehicle != null)
            {
                var mods = vehicle.GetComponent<Vehicle.VehicleModifications>() ?? vehicle.gameObject.AddComponent<Vehicle.VehicleModifications>();
                mods.InstallModification(Vehicle.VehicleModType.Bullbar, true);
                mods.InstallModification(Vehicle.VehicleModType.OffroadTires, true);
                mods.InstallModification(Vehicle.VehicleModType.RoofRack, true);
                mods.InstallModification(Vehicle.VehicleModType.RoofLightbar, true);
                mods.InstallModification(Vehicle.VehicleModType.ExternalFuelTanks, true);
                Debug.Log("<color=green>[VehicleModifications] Tüm modifikasyonlar başarıyla araca takıldı!</color>");
            }
        }

        [MenuItem("Endless Survival/Vehicle/Uninstall All Modifications", false, 142)]
        public static void UninstallAllMods()
        {
            if (!Application.isPlaying) return;

            var vehicle = Object.FindFirstObjectByType<Vehicle.VehicleController>();
            if (vehicle != null)
            {
                var mods = vehicle.GetComponent<Vehicle.VehicleModifications>();
                if (mods != null)
                {
                    mods.UninstallModification(Vehicle.VehicleModType.Bullbar);
                    mods.UninstallModification(Vehicle.VehicleModType.OffroadTires);
                    mods.UninstallModification(Vehicle.VehicleModType.RoofRack);
                    mods.UninstallModification(Vehicle.VehicleModType.RoofLightbar);
                    mods.UninstallModification(Vehicle.VehicleModType.ExternalFuelTanks);
                    Debug.Log("<color=yellow>[VehicleModifications] Tüm modifikasyonlar araçtan söküldü.</color>");
                }
            }
        }
    }
}
#endif
