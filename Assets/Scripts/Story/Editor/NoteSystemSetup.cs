#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using UnityEngine.UI;
using TMPro;
using EndlessSurvival.Story;
using EndlessSurvival.World;

namespace EndlessSurvival.Story.Editor
{
    public class NoteSystemSetup : UnityEditor.EditorWindow
    {
        [MenuItem("Endless Survival/Automated Setup/Setup Note System")]
        public static void SetupNoteSystem()
        {
            // 1. Örnek Not İçeriği (Scriptable Object) Oluşturma
            string folderPath = "Assets/Prefabs/Story";
            if (!AssetDatabase.IsValidFolder(folderPath))
                AssetDatabase.CreateFolder("Assets/Prefabs", "Story");

            string notePath = folderPath + "/SampleNote.asset";
            NoteDefinition sampleNote = AssetDatabase.LoadAssetAtPath<NoteDefinition>(notePath);
            if (sampleNote == null)
            {
                sampleNote = ScriptableObject.CreateInstance<NoteDefinition>();
                sampleNote.title = "Kayıp Günlük";
                sampleNote.author = "Bilinmeyen Asker";
                sampleNote.bodyText = "Eğer bunu okuyorsan, ben artık buralarda değilim.\n\nYol giderek tehlikeli bir hal alıyor. Yakıtım bitmek üzere ve hava kararıyor. İlerideki benzin istasyonunda bir şeyler bulmayı umuyorum.";
                AssetDatabase.CreateAsset(sampleNote, notePath);
                AssetDatabase.SaveAssets();
            }

            // 2. UI (NoteReaderUI) Oluşturma
            Canvas canvas = FindAnyObjectByType<Canvas>();
            if (canvas == null)
            {
                GameObject canvasObj = new GameObject("Canvas");
                canvas = canvasObj.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvasObj.AddComponent<CanvasScaler>();
                canvasObj.AddComponent<GraphicRaycaster>();
            }

            NoteReaderUI existingUI = FindAnyObjectByType<NoteReaderUI>();
            if (existingUI == null)
            {
                GameObject uiObj = new GameObject("NoteReaderUI");
                uiObj.transform.SetParent(canvas.transform, false);
                
                RectTransform uiRect = uiObj.AddComponent<RectTransform>();
                uiRect.anchorMin = Vector2.zero;
                uiRect.anchorMax = Vector2.one;
                uiRect.sizeDelta = Vector2.zero;

                NoteReaderUI noteUI = uiObj.AddComponent<NoteReaderUI>();

                // Arka plan paneli (Kağıt/Tablet görünümü)
                GameObject panelObj = new GameObject("Panel");
                panelObj.transform.SetParent(uiObj.transform, false);
                Image panelImage = panelObj.AddComponent<Image>();
                panelImage.color = new Color(0.9f, 0.9f, 0.85f, 0.95f); // Kirli beyaz/sarımtırak kağıt rengi
                RectTransform panelRect = panelObj.GetComponent<RectTransform>();
                panelRect.anchorMin = new Vector2(0.2f, 0.1f);
                panelRect.anchorMax = new Vector2(0.8f, 0.9f);
                panelRect.sizeDelta = Vector2.zero;
                noteUI.notePanel = panelObj;

                // Title Text
                GameObject titleObj = new GameObject("TitleText");
                titleObj.transform.SetParent(panelObj.transform, false);
                TextMeshProUGUI titleText = titleObj.AddComponent<TextMeshProUGUI>();
                titleText.text = "Başlık";
                titleText.fontSize = 36;
                titleText.color = Color.black;
                titleText.alignment = TextAlignmentOptions.Center;
                titleText.fontStyle = FontStyles.Bold;
                RectTransform titleRect = titleObj.GetComponent<RectTransform>();
                titleRect.anchorMin = new Vector2(0, 0.85f);
                titleRect.anchorMax = new Vector2(1, 1);
                titleRect.sizeDelta = Vector2.zero;
                noteUI.titleText = titleText;

                // Author Text
                GameObject authorObj = new GameObject("AuthorText");
                authorObj.transform.SetParent(panelObj.transform, false);
                TextMeshProUGUI authorText = authorObj.AddComponent<TextMeshProUGUI>();
                authorText.text = "Yazar";
                authorText.fontSize = 24;
                authorText.color = new Color(0.2f, 0.2f, 0.2f);
                authorText.alignment = TextAlignmentOptions.Center;
                authorText.fontStyle = FontStyles.Italic;
                RectTransform authorRect = authorObj.GetComponent<RectTransform>();
                authorRect.anchorMin = new Vector2(0, 0.75f);
                authorRect.anchorMax = new Vector2(1, 0.85f);
                authorRect.sizeDelta = Vector2.zero;
                noteUI.authorText = authorText;

                // Body Text
                GameObject bodyObj = new GameObject("BodyText");
                bodyObj.transform.SetParent(panelObj.transform, false);
                TextMeshProUGUI bodyText = bodyObj.AddComponent<TextMeshProUGUI>();
                bodyText.text = "İçerik...";
                bodyText.fontSize = 28;
                bodyText.color = Color.black;
                bodyText.alignment = TextAlignmentOptions.TopLeft;
                bodyText.margin = new Vector4(20, 20, 20, 20);
                RectTransform bodyRect = bodyObj.GetComponent<RectTransform>();
                bodyRect.anchorMin = new Vector2(0, 0);
                bodyRect.anchorMax = new Vector2(1, 0.75f);
                bodyRect.sizeDelta = Vector2.zero;
                noteUI.bodyText = bodyText;

                uiObj.SetActive(false); // Başlangıçta gizli olsun
                Undo.RegisterCreatedObjectUndo(uiObj, "Create NoteReaderUI");
            }

            // 3. Dünyada duran Interactable Note objesi oluşturma
            GameObject worldNote = new GameObject("InteractableNote_Sample");
            worldNote.transform.position = new Vector3(0, 1, 2); // Oyuncunun önüne doğru
            
            // Görsel için basit bir küp/kağıt temsili
            GameObject visuals = GameObject.CreatePrimitive(PrimitiveType.Cube);
            visuals.transform.SetParent(worldNote.transform, false);
            visuals.transform.localScale = new Vector3(0.5f, 0.05f, 0.7f);
            
            BoxCollider col = worldNote.AddComponent<BoxCollider>();
            col.size = new Vector3(1f, 0.2f, 1f); // Tıklanabilir alan biraz büyük olsun

            InteractableNote interactable = worldNote.AddComponent<InteractableNote>();
            interactable.noteDefinition = sampleNote;

            Undo.RegisterCreatedObjectUndo(worldNote, "Create World Note");

            // 4. Bütün Chunk Prefab'larına NoteSpawner ekle (Yollarda otomatik çıkması için)
            string notePrefabPath = "Assets/Prefabs/Story/InteractableNote.prefab";
            GameObject savedNotePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(notePrefabPath);
            if (savedNotePrefab == null)
            {
                savedNotePrefab = PrefabUtility.SaveAsPrefabAsset(worldNote, notePrefabPath);
            }

            string[] chunkGuids = AssetDatabase.FindAssets("t:GameObject", new[] { "Assets/Prefabs/Chunks" });
            foreach (string guid in chunkGuids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                GameObject prefabContents = PrefabUtility.LoadPrefabContents(path);

                if (prefabContents != null && prefabContents.GetComponent<Chunk>() != null)
                {
                    EndlessSurvival.World.POI.NoteSpawner spawner = prefabContents.GetComponentInChildren<EndlessSurvival.World.POI.NoteSpawner>();
                    if (spawner == null)
                    {
                        GameObject spawnerObj = new GameObject("NoteSpawner");
                        spawnerObj.transform.SetParent(prefabContents.transform, false);
                        spawnerObj.transform.localPosition = new Vector3(8f, 0f, 250f); // Yolun kenarında, chunk ortası
                        spawner = spawnerObj.AddComponent<EndlessSurvival.World.POI.NoteSpawner>();

                        spawner.notePrefab = savedNotePrefab.GetComponent<InteractableNote>();
                        spawner.possibleNotes = new NoteDefinition[] { sampleNote };
                        spawner.spawnChance = 0.5f;

                        PrefabUtility.SaveAsPrefabAsset(prefabContents, path);
                    }
                }
                PrefabUtility.UnloadPrefabContents(prefabContents);
            }
            AssetDatabase.SaveAssets();

            Debug.Log("<color=green>Note System UI, Sample Note, NoteSpawner in Chunks, and Scene Object successfully created!</color>");
        }
    }
}
#endif
