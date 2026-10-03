using UnityEngine;
using UnityEditor;
using EndlessSurvival.Story;
using EndlessSurvival.World.POI;
using TMPro;
using UnityEngine.UI;

namespace EndlessSurvival.World.Editor
{
    public class CampSetupUtility : EditorWindow
    {
        [MenuItem("Endless Survival/Setup Camp \u0026 Dialogue")]
        public static void ShowWindow()
        {
            GetWindow<CampSetupUtility>("Camp \u0026 Dialogue Setup");
        }

        private void OnGUI()
        {
            GUILayout.Label("Camp and Dialogue Generator", EditorStyles.boldLabel);

            if (GUILayout.Button("1. Create Dialogue Sets"))
            {
                CreateDialogueSets();
            }

            if (GUILayout.Button("2. Create Speech Bubble Prefab"))
            {
                CreateSpeechBubblePrefab();
            }

            if (GUILayout.Button("3. Update POI Prefabs (Campfire)"))
            {
                UpdateCampPrefabs();
            }
        }

        public static void RunSetupBatch()
        {
            Debug.Log("Starting RunSetupBatch...");
            CreateDialogueSets();
            CreateSpeechBubblePrefab();
            UpdateCampPrefabs();
            Debug.Log("Finished RunSetupBatch.");
        }

        private static void CreateDialogueSets()
        {
            if (!AssetDatabase.IsValidFolder("Assets/Resources")) AssetDatabase.CreateFolder("Assets", "Resources");
            if (!AssetDatabase.IsValidFolder("Assets/Resources/Dialogues")) AssetDatabase.CreateFolder("Assets/Resources", "Dialogues");

            CreateDialogue("Assets/Resources/Dialogues/Dialogue_Complaining.asset", new DialogueLine[]
            {
                new DialogueLine { speakerIndex = 0, text = "Bu soğukta nöbet tutmak hiç adil değil.", duration = 3f },
                new DialogueLine { speakerIndex = 1, text = "Sızlanmayı kes. Ateşin var en azından.", duration = 3f },
                new DialogueLine { speakerIndex = 0, text = "Yakacak bitiyor ama, ormana girmeye korkuyorum.", duration = 4f }
            });

            CreateDialogue("Assets/Resources/Dialogues/Dialogue_Story.asset", new DialogueLine[]
            {
                new DialogueLine { speakerIndex = 1, text = "Dün gece o garip sesleri duydun mu?", duration = 3.5f },
                new DialogueLine { speakerIndex = 0, text = "Rüzgardır o. Ya da eski madenden geliyordur.", duration = 4f },
                new DialogueLine { speakerIndex = 1, text = "Umarım rüzgardır... Sadece rüzgar.", duration = 3f }
            });

            AssetDatabase.SaveAssets();
            Debug.Log("Dialogue Sets created in Assets/Resources/Dialogues.");
        }

        private static void CreateDialogue(string path, DialogueLine[] lines)
        {
            var ds = AssetDatabase.LoadAssetAtPath<DialogueSet>(path);
            if (ds == null)
            {
                ds = ScriptableObject.CreateInstance<DialogueSet>();
                AssetDatabase.CreateAsset(ds, path);
            }
            ds.lines = lines;
            ds.pauseBetweenLines = 1.0f;
            EditorUtility.SetDirty(ds);
        }

        private static void CreateSpeechBubblePrefab()
        {
            if (!AssetDatabase.IsValidFolder("Assets/Prefabs")) AssetDatabase.CreateFolder("Assets", "Prefabs");
            if (!AssetDatabase.IsValidFolder("Assets/Prefabs/UI")) AssetDatabase.CreateFolder("Assets/Prefabs", "UI");

            string prefabPath = "Assets/Prefabs/UI/SpeechBubble.prefab";
            if (AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) != null)
            {
                Debug.Log("SpeechBubble prefab already exists.");
                return;
            }

            GameObject canvasObj = new GameObject("SpeechBubble");
            var canvas = canvasObj.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            var rect = canvas.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(3, 1);
            rect.localScale = new Vector3(0.01f, 0.01f, 0.01f);
            
            var cg = canvasObj.AddComponent<CanvasGroup>();
            var speechBubble = canvasObj.AddComponent<SpeechBubble>();
            speechBubble.canvasGroup = cg;

            // Background panel
            GameObject bgObj = new GameObject("Background");
            bgObj.transform.SetParent(canvasObj.transform, false);
            var bgImage = bgObj.AddComponent<Image>();
            bgImage.color = new Color(0, 0, 0, 0.7f);
            var bgRect = bgObj.GetComponent<RectTransform>();
            bgRect.anchorMin = Vector2.zero;
            bgRect.anchorMax = Vector2.one;
            bgRect.offsetMin = Vector2.zero;
            bgRect.offsetMax = Vector2.zero;

            // Text
            GameObject textObj = new GameObject("Text");
            textObj.transform.SetParent(canvasObj.transform, false);
            var tmp = textObj.AddComponent<TextMeshProUGUI>();
            tmp.text = "Hello World!";
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.fontSize = 14;
            tmp.color = Color.white;
            tmp.enableAutoSizing = true;
            tmp.fontSizeMin = 6;
            tmp.fontSizeMax = 20;

            var textRect = textObj.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(0.1f, 0.1f);
            textRect.offsetMax = new Vector2(-0.1f, -0.1f);

            speechBubble.textUI = tmp;

            PrefabUtility.SaveAsPrefabAsset(canvasObj, prefabPath);
            DestroyImmediate(canvasObj);
            Debug.Log($"Created SpeechBubble prefab at {prefabPath}");
        }

        private static void UpdateCampPrefabs()
        {
            // Try to find the existing EnemyCamp_Medium prefab to update it, or create a Campfire
            string searchPath = "Assets/Prefabs"; // Assuming they are here or inside subfolders
            string[] guids = AssetDatabase.FindAssets("EnemyCamp_Medium t:GameObject", new[] { searchPath });
            if (guids.Length == 0)
            {
                Debug.LogWarning("Could not find EnemyCamp_Medium. Please create a camp prefab manually or update its path.");
                return;
            }

            string path = AssetDatabase.GUIDToAssetPath(guids[0]);
            GameObject prefab = PrefabUtility.LoadPrefabContents(path);

            var camp = prefab.GetComponent<EnemyCampPOI>();
            if (camp != null)
            {
                camp.campType = EnemyCampPOI.CampType.Campfire;
                
                // Load dialogue sets
                var ds1 = AssetDatabase.LoadAssetAtPath<DialogueSet>("Assets/Resources/Dialogues/Dialogue_Complaining.asset");
                var ds2 = AssetDatabase.LoadAssetAtPath<DialogueSet>("Assets/Resources/Dialogues/Dialogue_Story.asset");

                if (ds1 != null && ds2 != null)
                {
                    camp.possibleDialogues = new DialogueSet[] { ds1, ds2 };
                }

                // Make sure we have a fire object to represent campfire
                Transform fireVisual = prefab.transform.Find("Campfire");
                if (fireVisual == null)
                {
                    GameObject fire = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    fire.name = "Campfire";
                    fire.transform.SetParent(prefab.transform, false);
                    fire.transform.localPosition = Vector3.zero;
                    fire.transform.localScale = new Vector3(0.8f, 0.2f, 0.8f);
                    
                    var renderer = fire.GetComponent<MeshRenderer>();
                    if (renderer != null)
                    {
                        var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                        mat.color = new Color(1f, 0.5f, 0f);
                        renderer.sharedMaterial = mat;
                    }
                    // Remove collider so it doesn't block player too much or enemies
                    DestroyImmediate(fire.GetComponent<Collider>());
                }
            }

            PrefabUtility.SaveAsPrefabAsset(prefab, path);
            PrefabUtility.UnloadPrefabContents(prefab);

            Debug.Log($"Updated camp prefab at {path}");
            
            // Note: EnemyDialogue needs a reference to SpeechBubble prefab.
            // But since EnemyDialogue is added dynamically, we need to load it from Resources or assign it somehow.
            // Better to load it from Resources dynamically in EnemyDialogue, or assign it via EnemyController.
            UpdateEnemyPrefabToLoadBubble();
        }
        
        private static void UpdateEnemyPrefabToLoadBubble()
        {
            string searchPath = "Assets/Prefabs"; 
            string[] guids = AssetDatabase.FindAssets("Enemy t:GameObject", new[] { searchPath });
            if (guids.Length > 0)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[0]);
                GameObject prefab = PrefabUtility.LoadPrefabContents(path);
                
                var dialogue = prefab.GetComponent<EndlessCombat.AI.EnemyDialogue>();
                if (dialogue == null) dialogue = prefab.AddComponent<EndlessCombat.AI.EnemyDialogue>();
                
                var bubble = AssetDatabase.LoadAssetAtPath<SpeechBubble>("Assets/Prefabs/UI/SpeechBubble.prefab");
                if (bubble != null)
                {
                    dialogue.speechBubblePrefab = bubble;
                }
                
                PrefabUtility.SaveAsPrefabAsset(prefab, path);
                PrefabUtility.UnloadPrefabContents(prefab);
                Debug.Log($"Updated Enemy prefab with SpeechBubble reference at {path}");
            }
        }
    }
}
