#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using System.Text;

namespace EndlessSurvival.World.Editor
{
    [InitializeOnLoad]
    public static class PrefabMissingScriptFixer
    {
        static PrefabMissingScriptFixer()
        {
            EditorApplication.delayCall += () =>
            {
                RunScanAndReport(silentIfClean: true);
            };
        }

        [MenuItem("Endless Survival/Fix Missing Scripts on Scene Chunks & Prefabs", false, 30)]
        [MenuItem("Tools/Endless Survival/Fix Missing Scripts on Scene Chunks & Prefabs", false, 30)]
        public static void FixMissingScriptsMenu()
        {
            RunScanAndReport(silentIfClean: false);
        }

        public static void RunScanAndReport(bool silentIfClean)
        {
            StringBuilder sb = new StringBuilder();
            int totalRemovedInScene = 0;
            int totalRemovedInPrefabs = 0;

            // 1. Scan all GameObjects in the current scene
            GameObject[] allSceneObjects = Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var go in allSceneObjects)
            {
                int count = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(go);
                if (count > 0)
                {
                    Undo.RegisterCompleteObjectUndo(go, "Remove Missing Scripts");
                    int removed = GameObjectUtility.RemoveMonoBehavioursWithMissingScript(go);
                    totalRemovedInScene += removed;
                    string path = GetGameObjectPath(go);
                    sb.AppendLine($"[Sahne] '{path}' objesinden {removed} adet eksik/geçersiz script temizlendi.");
                    Debug.LogWarning($"<color=orange>[MissingScriptFixer] Sahnedeki '{path}' üzerinde {removed} adet eksik script bulundu ve temizlendi!</color>", go);
                    EditorUtility.SetDirty(go);
                }
            }

            // 2. Scan Chunk prefabs in Assets/Prefabs/
            string[] prefabGuids = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Prefabs" });
            foreach (var guid in prefabGuids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                GameObject prefabContents = PrefabUtility.LoadPrefabContents(path);
                if (prefabContents != null)
                {
                    bool modified = false;
                    foreach (var t in prefabContents.GetComponentsInChildren<Transform>(true))
                    {
                        int count = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject);
                        if (count > 0)
                        {
                            int removed = GameObjectUtility.RemoveMonoBehavioursWithMissingScript(t.gameObject);
                            totalRemovedInPrefabs += removed;
                            modified = true;
                            sb.AppendLine($"[Prefab: {path}] '{GetGameObjectPath(t.gameObject)}' objesinden {removed} adet eksik script temizlendi.");
                            Debug.LogWarning($"<color=orange>[MissingScriptFixer] '{path}' içindeki '{t.gameObject.name}' üzerinde {removed} eksik script temizlendi.</color>");
                        }
                    }

                    if (modified)
                    {
                        PrefabUtility.SaveAsPrefabAsset(prefabContents, path);
                        Debug.Log($"<color=green>[MissingScriptFixer] Prefab asset '{path}' güncellendi ve temizlendi.</color>");
                    }
                    PrefabUtility.UnloadPrefabContents(prefabContents);
                }
            }

            if (totalRemovedInScene > 0 || totalRemovedInPrefabs > 0)
            {
                AssetDatabase.SaveAssets();
                Debug.Log($"<color=green><b>[MissingScriptFixer] İŞLEM TAMAMLANDI!</b> Toplam {totalRemovedInScene} sahne objesi, {totalRemovedInPrefabs} prefab objesi temizlendi. Artık 'Apply All' yapılabilir!</color>\n{sb}");
                if (!silentIfClean)
                {
                    EditorUtility.DisplayDialog("Geçersiz Scriptler Temizlendi", 
                        $"Prefab kaydını engelleyen bozuk scriptler temizlendi!\n\n" +
                        $"• Sahnede temizlenen: {totalRemovedInScene}\n" +
                        $"• Prefablarda temizlenen: {totalRemovedInPrefabs}\n\n" +
                        $"Artık Prefab Overrides menüsünden 'Apply All' butonuna tıklayabilirsiniz.", "Tamam");
                }
            }
            else
            {
                if (!silentIfClean)
                {
                    Debug.Log("<color=green>[MissingScriptFixer] Herhangi bir eksik veya geçersiz script bulunamadı.</color>");
                    EditorUtility.DisplayDialog("Script Kontrolü", "Tüm sahne ve prefablar incelendi, herhangi bir eksik/bozuk script bulunamadı.", "Tamam");
                }
            }
        }

        /// <summary>
        /// Cleans any missing scripts on the target chunk and directly applies overrides to the base prefab.
        /// </summary>
        public static bool CleanAndApplyToPrefab(Chunk chunk)
        {
            if (chunk == null) return false;

            // 1. Clean missing scripts on chunk and all its children
            int countRemoved = 0;
            foreach (var t in chunk.GetComponentsInChildren<Transform>(true))
            {
                int count = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject);
                if (count > 0)
                {
                    countRemoved += GameObjectUtility.RemoveMonoBehavioursWithMissingScript(t.gameObject);
                    EditorUtility.SetDirty(t.gameObject);
                }
            }

            // 2. Clean temporary EditorPreview to prevent saving temporary spawned objects into the master prefab asset
            Transform preview = chunk.transform.Find("EditorPreview");
            if (preview != null)
            {
                Object.DestroyImmediate(preview.gameObject);
            }

            // Clean any legacy/destroying holders in children
            Transform splineRoad = chunk.transform.Find("Spline_Road");
            if (splineRoad != null)
            {
                for (int i = splineRoad.childCount - 1; i >= 0; i--)
                {
                    Transform c = splineRoad.GetChild(i);
                    if (c.name.Contains("Destroying") || c.name.Contains("Holder"))
                    {
                        Object.DestroyImmediate(c.gameObject);
                    }
                }
            }

            // Clean Vegetation_Container children when in TerrainTrees mode to avoid baking duplicate GameObjects
            var vegSpawner = chunk.GetComponent<ChunkVegetationSpawner>();
            if (vegSpawner == null || vegSpawner.renderMode == ChunkVegetationSpawner.VegetationRenderMode.TerrainTrees)
            {
                Transform vegContainer = chunk.transform.Find("Vegetation_Container");
                if (vegContainer != null)
                {
                    for (int i = vegContainer.childCount - 1; i >= 0; i--)
                    {
                        Object.DestroyImmediate(vegContainer.GetChild(i).gameObject);
                    }
                }
            }

            // 3. Also clean missing scripts and invalid children on the target base prefab asset itself
            string assetPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(chunk.gameObject);
            if (!string.IsNullOrEmpty(assetPath))
            {
                GameObject contents = PrefabUtility.LoadPrefabContents(assetPath);
                if (contents != null)
                {
                    bool prefabCleaned = false;
                    for (int i = contents.transform.childCount - 1; i >= 0; i--)
                    {
                        Transform c = contents.transform.GetChild(i);
                        if (c.name.Contains("Destroying") || c.name == "EditorPreview")
                        {
                            Object.DestroyImmediate(c.gameObject);
                            prefabCleaned = true;
                        }
                    }
                    foreach (var t in contents.GetComponentsInChildren<Transform>(true))
                    {
                        int cCount = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject);
                        if (cCount > 0)
                        {
                            GameObjectUtility.RemoveMonoBehavioursWithMissingScript(t.gameObject);
                            prefabCleaned = true;
                        }
                    }
                    if (prefabCleaned)
                    {
                        PrefabUtility.SaveAsPrefabAsset(contents, assetPath);
                    }
                    PrefabUtility.UnloadPrefabContents(contents);
                }
            }

            // 4. Safely apply overrides to prefab
            try
            {
                PrefabUtility.ApplyPrefabInstance(chunk.gameObject, InteractionMode.UserAction);
                Debug.Log($"<color=green><b>[MissingScriptFixer] BAŞARILI:</b> '{chunk.gameObject.name}' üzerindeki tüm yol, spline ve terrain değişiklikleri ana Prefab'a kaydedildi!</color>");

                // Re-generate preview so scene view remains visually complete
                int seed = string.IsNullOrEmpty(chunk.editorPreviewSeed) ? chunk.GetHashCode() : SeededRandom.HashString(chunk.editorPreviewSeed);
                chunk.GenerateEditorPreview(seed);

                EditorUtility.DisplayDialog("Prefab Başarıyla Kaydedildi", 
                    $"'{chunk.gameObject.name}' objesinde yaptığınız tüm değişiklikler ana Prefab dosyasına kaydedildi!\n\n" +
                    $"• Geçersiz scriptler temizlendi ({countRemoved} adet)\n" +
                    $"• Geçici önizleme nesneleri filtrelendi\n" +
                    $"• Yol, spline ve arazi güncellendi.", "Harika!");
                return true;
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[MissingScriptFixer] Prefab kaydedilirken hata oluştu: {ex.Message}");
                EditorUtility.DisplayDialog("Kayıt Hatası", $"Prefab kaydedilemedi:\n{ex.Message}", "Tamam");
                return false;
            }
        }

        private static string GetGameObjectPath(GameObject go)
        {
            string path = go.name;
            Transform parent = go.transform.parent;
            while (parent != null)
            {
                path = parent.name + "/" + path;
                parent = parent.parent;
            }
            return path;
        }
    }
}
#endif
