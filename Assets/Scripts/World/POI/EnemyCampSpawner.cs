using System.Collections.Generic;
using UnityEngine;
using EndlessSurvival.World;

namespace EndlessSurvival.World.POI
{
    /// <summary>
    /// Chunk'ın CampRandom seed alt akışını kullanarak havuzdaki kamp prefab'larından (Campfire, Outpost vb.)
    /// deterministik seçim yapıp dünyada oluşturan spawner.
    /// </summary>
    public class EnemyCampSpawner : MonoBehaviour
    {
        [Header("Camp Pool")]
        [Tooltip("Spawn edilebilecek düşman kampı prefab'ları (Campfire, Outpost vb.)")]
        public GameObject[] campPrefabs;

        [Header("Spawn Settings")]
        [Tooltip("Chunk başına kamp çıkma olasılığı")]
        [Range(0f, 1f)]
        public float spawnChance = 1.0f;

        private bool _hasSpawned = false;

        private void Start()
        {
            Chunk chunk = GetComponentInParent<Chunk>();
            if (chunk != null)
            {
                SpawnCampIfEligible(chunk);
            }
        }

        public void SpawnCampIfEligible(Chunk chunk)
        {
            if (_hasSpawned || chunk == null) return;
            _hasSpawned = true;

            EnsureCampPrefabs();
            if (campPrefabs == null || campPrefabs.Length == 0) return;

            var rng = chunk.CampRandom;
            if (rng == null) rng = new SeededRandom(0);

            if (!rng.Chance(spawnChance)) return;

            // Eğer chunk içinde önceden kalmış eski statik Enemy Camp varsa temizle
            Transform oldCamp = chunk.transform.Find("Enemy Camp");
            if (oldCamp != null && oldCamp.GetComponent<EnemyCampPOI>() != null)
            {
                Destroy(oldCamp.gameObject);
            }

            // Seed alt akışından kamp prefab'ını seç
            GameObject selectedPrefab = rng.Pick(campPrefabs);
            if (selectedPrefab == null) return;

            GameObject campObj = Instantiate(selectedPrefab, chunk.transform);
            campObj.name = selectedPrefab.name;
            campObj.transform.localPosition = new Vector3(0, 20f, 250f);

            var campPOI = campObj.GetComponent<EnemyCampPOI>();
            if (campPOI != null)
            {
                campPOI.InitializeFromChunk(rng);
            }

            Debug.Log($"<color=cyan>[EnemyCampSpawner] Chunk #{chunk.ChunkIndex} için kamp oluşturuldu: {selectedPrefab.name}</color>");
        }

#if UNITY_EDITOR
        public void GenerateCampPreview(SeededRandom rng, Transform container)
        {
            EnsureCampPrefabs();
            if (campPrefabs == null || campPrefabs.Length == 0) return;
            if (!rng.Chance(spawnChance)) return;

            GameObject selectedPrefab = rng.Pick(campPrefabs);
            if (selectedPrefab == null) return;

            GameObject campObj = UnityEditor.PrefabUtility.InstantiatePrefab(selectedPrefab, container) as GameObject;
            if (campObj != null)
            {
                campObj.name = selectedPrefab.name;
                campObj.transform.localPosition = new Vector3(0, 20f, 250f);

                var campPOI = campObj.GetComponent<EnemyCampPOI>();
                if (campPOI != null)
                {
                    campPOI.InitializeFromChunk(rng, container);
                }
            }
        }
#endif

        private void EnsureCampPrefabs()
        {
            if (campPrefabs == null || campPrefabs.Length == 0)
            {
#if UNITY_EDITOR
                var campfire = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/PointsOfInterests/EnemyCamp_Campfire.prefab");
                var outpost = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/PointsOfInterests/EnemyCamp_Outpost.prefab");
                var list = new List<GameObject>();
                if (campfire != null) list.Add(campfire);
                if (outpost != null) list.Add(outpost);
                campPrefabs = list.ToArray();
#endif
            }
        }
    }
}
