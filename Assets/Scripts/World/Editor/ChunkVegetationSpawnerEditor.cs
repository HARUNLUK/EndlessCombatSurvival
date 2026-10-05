#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace EndlessSurvival.World.Editor
{
    [CustomEditor(typeof(ChunkVegetationSpawner))]
    public class ChunkVegetationSpawnerEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            ChunkVegetationSpawner spawner = (ChunkVegetationSpawner)target;

            // Header Banner
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("🌲 CHUNK VEGETATION & FORESTRY SYSTEM", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                spawner.renderMode == ChunkVegetationSpawner.VegetationRenderMode.TerrainTrees
                    ? "AKTİF MOD: [Terrain Trees] Tüm ağaçlar, kayalar ve çalılar Unity Terrain GPU instanced motoru üzerinden sıfır GameObject yüküyle çizilir."
                    : "AKTİF MOD: [GameObjects] Tüm ağaçlar, kayalar ve çalılar fiziksel bağımsız GameObject olarak sahneye yerleştirilir.",
                spawner.renderMode == ChunkVegetationSpawner.VegetationRenderMode.TerrainTrees ? MessageType.Info : MessageType.Warning
            );

            // Render Mode Fast Switch Buttons
            EditorGUILayout.BeginHorizontal();
            GUI.backgroundColor = spawner.renderMode == ChunkVegetationSpawner.VegetationRenderMode.TerrainTrees ? new Color(0.2f, 0.8f, 0.4f) : Color.white;
            if (GUILayout.Button("🌿 Terrain Modu (Önerilen)", GUILayout.Height(28)))
            {
                Undo.RecordObject(spawner, "Switch to TerrainTrees Mode");
                spawner.renderMode = ChunkVegetationSpawner.VegetationRenderMode.TerrainTrees;
                spawner.GenerateVegetation();
                EditorUtility.SetDirty(spawner);
            }

            GUI.backgroundColor = spawner.renderMode == ChunkVegetationSpawner.VegetationRenderMode.GameObjects ? new Color(1.0f, 0.7f, 0.2f) : Color.white;
            if (GUILayout.Button("📦 GameObject Modu", GUILayout.Height(28)))
            {
                Undo.RecordObject(spawner, "Switch to GameObjects Mode");
                spawner.renderMode = ChunkVegetationSpawner.VegetationRenderMode.GameObjects;
                spawner.GenerateVegetation();
                EditorUtility.SetDirty(spawner);
            }
            GUI.backgroundColor = Color.white;
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(8);
            DrawDefaultInspector();

            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("Eylemler ve Yenileme", EditorStyles.boldLabel);

            // Generate Button
            GUI.backgroundColor = new Color(0.25f, 0.75f, 1.0f);
            if (GUILayout.Button("🌲 Ağaçları ve Bitki Örtüsünü Yeniden Oluştur (Generate)", GUILayout.Height(32)))
            {
                spawner.GenerateVegetation();
                EditorUtility.SetDirty(spawner.gameObject);
            }

            // Clear Button
            GUI.backgroundColor = new Color(1.0f, 0.4f, 0.4f);
            if (GUILayout.Button("Temizle (Clear Vegetation)", GUILayout.Height(24)))
            {
                spawner.ClearVegetation();
                EditorUtility.SetDirty(spawner.gameObject);
            }
            GUI.backgroundColor = Color.white;

            // Status Info
            EditorGUILayout.Space(6);
            Terrain t = spawner.GetComponentInChildren<Terrain>();
            if (t != null && t.terrainData != null)
            {
                EditorGUILayout.LabelField($"Terrain Ağaç Sayısı: {t.terrainData.treeInstanceCount} adet", EditorStyles.miniLabel);
            }
            Transform container = spawner.transform.Find("Vegetation_Container");
            if (container != null)
            {
                EditorGUILayout.LabelField($"GameObject Bitki Sayısı: {container.childCount} adet", EditorStyles.miniLabel);
            }
        }
    }
}
#endif
