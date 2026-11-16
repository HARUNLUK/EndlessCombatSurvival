using System.IO;
using UnityEditor;
using UnityEngine;

namespace EndlessCombat.Combat.Editor
{
    /// <summary>
    /// Makes one WeaponDefinition asset per weapon prefab (Assets/Prefabs/Weapons) from the numbers the prefab
    /// holds today and plugs it into the prefab's WeaponController (Combat_Architecture.md, "Silah verisi").
    /// Run again safely: an existing asset is kept (its numbers are not overwritten), only the link is checked.
    /// Unity closed: -executeMethod EndlessCombat.Combat.Editor.WeaponDefinitionSetup.RunBatch
    /// </summary>
    public static class WeaponDefinitionSetup
    {
        private const string PrefabFolder = "Assets/Prefabs/Weapons";
        private const string DataFolder = "Assets/Data/Weapons";

        [MenuItem("Endless Survival/Dövüş/Silah Tanımlarını Oluştur")]
        public static void Run()
        {
            int made = 0, linked = 0, kept = 0;
            if (!AssetDatabase.IsValidFolder(DataFolder))
            {
                Directory.CreateDirectory(DataFolder);
                AssetDatabase.Refresh();
            }

            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { PrefabFolder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                GameObject root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    var weapon = root.GetComponent<WeaponController>();
                    if (weapon == null) continue;

                    var so = new SerializedObject(weapon);
                    SerializedProperty defProp = so.FindProperty("definition");
                    var def = defProp.objectReferenceValue as WeaponDefinition;
                    string assetPath = $"{DataFolder}/WD_{root.name.Replace("Weapon_", "")}.asset";

                    if (def == null) def = AssetDatabase.LoadAssetAtPath<WeaponDefinition>(assetPath);
                    if (def == null)
                    {
                        def = ScriptableObject.CreateInstance<WeaponDefinition>();
                        CopyFrom(so, root.GetComponent<MeleeWeapon>(), def);
                        AssetDatabase.CreateAsset(def, assetPath);
                        made++;
                    }
                    else kept++;

                    if (defProp.objectReferenceValue != def)
                    {
                        defProp.objectReferenceValue = def;
                        so.ApplyModifiedPropertiesWithoutUndo();
                        PrefabUtility.SaveAsPrefabAsset(root, path);
                        linked++;
                    }
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
            }
            AssetDatabase.SaveAssets();
            string msg = $"[WeaponDefinitionSetup] yeni {made}, bağlanan {linked}, var olan {kept} ({DataFolder})";
            Debug.Log(msg);
        }

        public static void RunBatch()
        {
            Run();
            EditorApplication.Exit(0);
        }

        private static void CopyFrom(SerializedObject w, MeleeWeapon melee, WeaponDefinition d)
        {
            d.displayName = w.FindProperty("weaponName").stringValue;
            d.type = (WeaponType)w.FindProperty("weaponType").intValue;
            d.automatic = w.FindProperty("isAutomatic").boolValue;
            d.aimYawOffset = w.FindProperty("aimYawOffset").floatValue;
            d.useLeftHandGrip = w.FindProperty("useLeftHandGrip").boolValue;
            d.damage = w.FindProperty("damage").floatValue;
            d.fireInterval = w.FindProperty("fireRate").floatValue;
            d.range = w.FindProperty("range").floatValue;
            d.magazine = w.FindProperty("magazineCapacity").intValue;
            d.reloadTime = w.FindProperty("reloadDuration").floatValue;
            d.ammo = (AmmoKind)w.FindProperty("ammoKind").intValue;
            d.pellets = 1;
            d.pelletSpread = 0f;
            if (melee != null) d.melee = melee.stats;
        }
    }
}
