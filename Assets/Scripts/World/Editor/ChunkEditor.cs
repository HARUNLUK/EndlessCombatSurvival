#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using EndlessSurvival.World.Road;

namespace EndlessSurvival.World.Editor
{
    /// <summary>
    /// Lone chunk inspector. The preview seed and the options are linked both ways (ChunkPreviewSeed):
    /// typing a seed rolls every option (type, sea side, mountain layout, road...); changing an option by hand
    /// pins it into the seed as "|key=value", so the same seed always gives the same chunk.
    /// Chunks spawned by ChunkManager keep its plan (only the old road / cross-section refresh applies).
    /// </summary>
    [CustomEditor(typeof(Chunk))]
    public class ChunkEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            Chunk chunk = (Chunk)target;
            bool seeded = ChunkPreviewSeed.Supports(chunk);
            serializedObject.Update();

            Dictionary<string, string> before = ChunkPreviewSeed.Capture(chunk);
            string newSeed = null;

            if (seeded) newSeed = DrawSeedSection(chunk);

            DrawPropertiesExcluding(serializedObject, "editorPreviewSeed");
            serializedObject.ApplyModifiedProperties();

            Dictionary<string, string> after = ChunkPreviewSeed.Capture(chunk);

            if (seeded)
            {
                if (newSeed != null)
                {
                    // Seed typed / rolled: every option follows it
                    Undo.RecordObject(chunk, "Preview Seed");
                    chunk.editorPreviewSeed = newSeed;
                    ChunkPreviewSeed.Apply(chunk);
                }
                else if (Changed(before, after))
                {
                    // Option changed by hand: rewrite its letter in the seed, then rebuild from the seed
                    Undo.RecordObject(chunk, "Preview Option");
                    chunk.editorPreviewSeed = ChunkPreviewSeed.WriteChanges(chunk, chunk.editorPreviewSeed, before, after);
                    ChunkPreviewSeed.Apply(chunk);
                }
            }
            else if (!Application.isPlaying)
            {
                // Spawned by ChunkManager (or a prefab asset): just reflect road edits
                if (Differs(before, after, ChunkPreviewSeed.Slot.Road, ChunkPreviewSeed.Slot.Elevation, ChunkPreviewSeed.Slot.Param,
                        ChunkPreviewSeed.Slot.EntryX, ChunkPreviewSeed.Slot.ExitX, ChunkPreviewSeed.Slot.EntryY, ChunkPreviewSeed.Slot.ExitY))
                    Regenerate(chunk);
                else if (Differs(before, after, ChunkPreviewSeed.Slot.Cross))
                {
                    var gen = chunk.GetComponentInChildren<RoadGenerator>();
                    if (gen != null) gen.ApplyCrossSectionPreset(chunk.crossSectionType);
                }
            }

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Editor Preview & Vegetation Controls", EditorStyles.boldLabel);

            GUI.backgroundColor = new Color(0.2f, 0.7f, 1.0f);
            if (GUILayout.Button("Refresh All Preview (Ağaçlar + Kamp + Loot + Kenarlar)", GUILayout.Height(28)))
            {
                chunk.GenerateEditorPreview(chunk.GetEditorPreviewSeed());
            }

            GUI.backgroundColor = new Color(0.4f, 0.85f, 0.5f);
            if (GUILayout.Button("Regenerate Trees & Vegetation", GUILayout.Height(26)))
            {
                var spawner = chunk.GetComponentInChildren<ChunkVegetationSpawner>();
                if (spawner != null) spawner.GenerateVegetation();
            }

            GUI.backgroundColor = new Color(1.0f, 0.4f, 0.4f);
            if (GUILayout.Button("Clear Preview", GUILayout.Height(22)))
            {
                Transform preview = chunk.transform.Find("EditorPreview");
                if (preview != null) DestroyImmediate(preview.gameObject);
                var spawner = chunk.GetComponentInChildren<ChunkVegetationSpawner>();
                if (spawner != null) spawner.ClearVegetation();
            }

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Prefab Kayıt ve Onarım (Overrides)", EditorStyles.boldLabel);

            GUI.backgroundColor = new Color(0.1f, 0.85f, 0.45f);
            if (GUILayout.Button("💾 Geçersiz Scriptleri Onar & Prefab'a Kaydet", GUILayout.Height(32)))
            {
                PrefabMissingScriptFixer.CleanAndApplyToPrefab(chunk);
            }

            GUI.backgroundColor = Color.white;
        }

        private const string SeedControl = "ChunkPreviewSeedField";
        // Text being typed into the seed field (applied on Enter, the Uygula button or leaving the field)
        private string _typedSeed;
        private string _lastSeed;
        private bool _seedFocused;

        /// <summary>Seed field and roll buttons. Returns a new seed to apply, or null.</summary>
        private string DrawSeedSection(Chunk chunk)
        {
            string result = null;
            EditorGUILayout.LabelField("Önizleme Seed'i (tüm seçenekleri belirler)", EditorStyles.boldLabel);

            string current = chunk.editorPreviewSeed ?? "";
            // Seed changed elsewhere (pins, buttons, undo): show it, unless the user is typing
            if (_typedSeed == null || (_lastSeed != current && !_seedFocused)) _typedSeed = current;
            _lastSeed = current;

            Event e = Event.current;
            bool enter = e.type == EventType.KeyDown && (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter)
                         && GUI.GetNameOfFocusedControl() == SeedControl;

            EditorGUILayout.BeginHorizontal();
            GUI.SetNextControlName(SeedControl);
            _typedSeed = EditorGUILayout.TextField(
                new GUIContent("Preview Seed", "Herhangi bir metin yaz + Enter (veya Uygula): tip, deniz tarafı, dağ yerleşimi, yol vb. hepsi bu seed'den seçilir. " +
                                               "Aşağıdan elle değiştirdiğin seçenekler seed'e |anahtar=değer olarak eklenir."),
                _typedSeed);
            bool pending = _typedSeed != current;
            GUI.backgroundColor = pending ? new Color(1f, 0.85f, 0.3f) : Color.white;
            using (new EditorGUI.DisabledScope(!pending))
            {
                if (GUILayout.Button("Uygula", GUILayout.Width(60))) result = _typedSeed;
            }
            GUI.backgroundColor = Color.white;
            EditorGUILayout.EndHorizontal();

            bool focused = GUI.GetNameOfFocusedControl() == SeedControl;
            if (e.type == EventType.Repaint || e.type == EventType.Layout)
            {
                // Left the field with unapplied text: apply it
                if (_seedFocused && !focused && pending) result = _typedSeed;
                _seedFocused = focused;
            }
            if (enter && pending)
            {
                result = _typedSeed;
                e.Use();
            }
            if (pending && result == null)
                EditorGUILayout.HelpBox("Enter'a bas veya Uygula'ya tıkla.", MessageType.None);

            EditorGUILayout.BeginHorizontal();
            GUI.backgroundColor = new Color(0.3f, 0.8f, 0.4f);
            if (GUILayout.Button(new GUIContent("🎲 Yeni Seed", "Her şey yeni: seçenekler + yol kıvrımı / kamp / ağaçlar"), GUILayout.Height(24)))
                result = ChunkPreviewSeed.NewSeed();
            if (GUILayout.Button(new GUIContent("🎲 Seçenekler", "Sadece seçenek harfleri yeni ('-' öncesi); kıvrım / kamp / ağaçlar aynı"), GUILayout.Height(24)))
                result = ChunkPreviewSeed.ShuffleOptions(chunk.editorPreviewSeed);
            if (GUILayout.Button(new GUIContent("🎲 Şekil", "Seçenekler aynı; yol kıvrımı / kamp / ağaçlar yeni ('-' sonrası)"), GUILayout.Height(24)))
                result = ChunkPreviewSeed.ShuffleShape(chunk.editorPreviewSeed);
            GUI.backgroundColor = Color.white;
            EditorGUILayout.EndHorizontal();

            if (string.IsNullOrEmpty(chunk.editorPreviewSeed))
                EditorGUILayout.HelpBox("Seed boş: bir şey yaz ya da bir seçeneği değiştir, seed oluşur.", MessageType.None);

            _showLegend = EditorGUILayout.Foldout(_showLegend, "Seed harfleri (her harf bir seçenek, '-' sonrası rastgelelik)", true);
            if (_showLegend) DrawLegend(chunk);

            EditorGUILayout.Space(4);
            return result;
        }

        private bool _showLegend;

        /// <summary>Position, current letter and meaning of each option letter.</summary>
        private static void DrawLegend(Chunk chunk)
        {
            int[] sym = ChunkPreviewSeed.Symbols(chunk.editorPreviewSeed);
            var mini = new GUIStyle(EditorStyles.miniLabel) { wordWrap = true, richText = true };
            EditorGUI.indentLevel++;
            foreach (ChunkPreviewSeed.Slot slot in System.Enum.GetValues(typeof(ChunkPreviewSeed.Slot)))
            {
                int i = (int)slot;
                string letter = sym != null ? ChunkPreviewSeed.Alphabet[sym[i]].ToString() : "-";
                EditorGUILayout.LabelField((i + 1) + ". <b>" + letter + "</b>  " + ChunkPreviewSeed.SlotLabel(slot) + ":  "
                                           + ChunkPreviewSeed.SlotValues(chunk, slot), mini);
            }
            EditorGUILayout.LabelField("Harf sayısı seçenekten fazlaysa başa döner (Tip 5 taneyse f = a). Eksik harfler metinden türetilir.", mini);
            EditorGUI.indentLevel--;
        }

        private static bool Changed(Dictionary<string, string> before, Dictionary<string, string> after)
        {
            foreach (var kv in after)
                if (!before.TryGetValue(kv.Key, out string old) || old != kv.Value) return true;
            return false;
        }

        private static bool Differs(Dictionary<string, string> before, Dictionary<string, string> after, params ChunkPreviewSeed.Slot[] slots)
        {
            for (int i = 0; i < slots.Length; i++)
            {
                string key = slots[i].ToString();
                if (before[key] != after[key]) return true;
            }
            return false;
        }

        /// <summary>Rebuilds the road from the chunk's road type / elevation fields, then terrain, trees and preview.</summary>
        public static void Regenerate(Chunk chunk)
        {
            var gen = chunk.GetComponentInChildren<RoadGenerator>();
            var spline = chunk.GetComponentInChildren<RoadSpline>();
            if (gen == null || spline == null) return;

            // Lone chunks: same options, new bends (the part after '-')
            if (ChunkPreviewSeed.Supports(chunk) && !string.IsNullOrEmpty(chunk.editorPreviewSeed))
            {
                Undo.RecordObject(chunk, "Randomize Road");
                chunk.editorPreviewSeed = ChunkPreviewSeed.ShuffleShape(chunk.editorPreviewSeed);
                ChunkPreviewSeed.Apply(chunk);
                return;
            }

            Undo.RecordObject(spline, "Randomize Road");
            ChunkPreviewSeed.BuildManualRoad(chunk, spline, new SeededRandom(new System.Random().Next()));
            EditorUtility.SetDirty(spline);
            gen.ApplyCrossSectionPreset(chunk.crossSectionType);
            ChunkPreviewSeed.RebuildPreview(chunk, spline, gen);
        }
    }
}
#endif
