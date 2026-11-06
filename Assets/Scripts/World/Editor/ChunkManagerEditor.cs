#if UNITY_EDITOR
using System.Text;
using UnityEditor;
using UnityEngine;

namespace EndlessSurvival.World.Editor
{
    [CustomEditor(typeof(ChunkManager))]
    public class ChunkManagerEditor : UnityEditor.Editor
    {
        private const int PlanPreviewCount = 60;
        private string _planPreview;

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var manager = (ChunkManager)target;
            bool hasLegacy = manager.legacyStartChunkType != null
                             || (manager.chunkTypes != null && manager.chunkTypes.Exists(e => e != null && e.legacyPrefab != null && e.profile == null));
            if (hasLegacy)
            {
                EditorGUILayout.HelpBox("Chunk Types listesinde eski tip prefab'ları var. Profillere geçmek için aşağıdaki " +
                                        "'Chunk Profillerine Geçir' butonunu kullan.", MessageType.Warning);
            }
            else if (manager.chunkPrefab == null || manager.chunkTypes == null || !manager.chunkTypes.Exists(e => e != null && e.profile != null))
            {
                EditorGUILayout.HelpBox("Chunk Prefab'ı ve en az bir tip profili gerekli.", MessageType.Warning);
            }

            EditorGUILayout.Space(12);
            EditorGUILayout.LabelField("Tip Planı (seed'e göre)", EditorStyles.boldLabel);
            if (GUILayout.Button($"İlk {PlanPreviewCount} Chunk'ın Planını Göster", GUILayout.Height(24)))
            {
                _planPreview = BuildPlanPreview(manager, PlanPreviewCount);
            }
            if (!string.IsNullOrEmpty(_planPreview))
            {
                EditorGUILayout.HelpBox(_planPreview, MessageType.None);
            }

            EditorGUILayout.Space(12);
            EditorGUILayout.LabelField("Chunk & World Actions", EditorStyles.boldLabel);

            if (hasLegacy)
            {
                GUI.backgroundColor = new Color(1.0f, 0.8f, 0.2f);
                if (GUILayout.Button("Chunk Profillerine Geçir", GUILayout.Height(30)))
                {
                    ChunkProfileMigration.MigrateFromMenu();
                }
            }

            GUI.backgroundColor = new Color(0.2f, 0.7f, 1.0f);
            if (GUILayout.Button("Generate Base Chunk Prefab", GUILayout.Height(32)))
            {
                ChunkPrefabGenerator.GenerateAndSaveChunkPrefabs();
            }

            GUI.backgroundColor = new Color(0.3f, 0.85f, 0.4f);
            if (GUILayout.Button("Setup ChunkManager in Scene", GUILayout.Height(28)))
            {
                SetupSceneChunkManager.SetupInActiveScene();
            }

            GUI.backgroundColor = new Color(0.4f, 0.85f, 0.5f);
            if (GUILayout.Button("Spawn Editor Preview Chunks (Canlı Önizleme)", GUILayout.Height(30)))
            {
                manager.SpawnEditorPreviewChunks(2);
            }

            GUI.backgroundColor = new Color(1.0f, 0.5f, 0.3f);
            if (GUILayout.Button("Clear All Child Chunks", GUILayout.Height(24)))
            {
                manager.ContextClearChildChunks();
            }

            GUI.backgroundColor = Color.white;
        }

        /// <summary>One line per run ("0-2 Orman"), special chunks marked on their own line.</summary>
        private static string BuildPlanPreview(ChunkManager manager, int count)
        {
            manager.ResetTypePlan();
            string seedNote = string.IsNullOrEmpty(manager.masterSeed) ? "(Master Seed boş: oyunda her seferinde rastgele; önizleme boş seed ile)" : $"seed '{manager.masterSeed}'";
            var sb = new StringBuilder(seedNote).Append('\n');

            int runStart = 0;
            ChunkTypeRef runType = manager.GetPlannedType(0);
            for (int i = 1; i <= count; i++)
            {
                ChunkTypeRef t = i < count ? manager.GetPlannedType(i) : default;
                if (i < count && t == runType) continue;

                string range = runStart == i - 1 ? $"{runStart}" : $"{runStart}-{i - 1}";
                sb.Append(runType.IsSpecial ? $"{range}  ★ {runType.Name} (özel, {(runType.profile != null ? runType.profile.DisplayName : "?")} içinde)" : $"{range}  {runType.Name}");
                if (i < count) sb.Append('\n');
                runStart = i;
                runType = t;
            }

            // The editor plan must not leak into play mode / the next preview with a different seed
            manager.ResetTypePlan();
            return sb.ToString();
        }
    }
}
#endif
