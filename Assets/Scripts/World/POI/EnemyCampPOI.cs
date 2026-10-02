using UnityEngine;
using EndlessSurvival.World;

namespace EndlessSurvival.World.POI
{
    public class EnemyCampPOI : PointOfInterest
    {
        [Header("Enemy Spawning")]
        [Tooltip("Dusman prefab'lari")]
        public GameObject[] enemyPrefabs;
        
        [Tooltip("Olusturulacak minimum dusman sayisi")]
        public int minEnemies = 2;
        
        [Tooltip("Olusturulacak maksimum dusman sayisi")]
        public int maxEnemies = 5;
        
        [Tooltip("Kampin etrafinda dusmanlarin spawnlanacagi alanin yaricapi")]
        public float spawnRadius = 15f;

        [Tooltip("Eger aciksa, kamp sabit bir noktada degil arazide rastgele bir yerde cikar")]
        public bool randomizeCampLocation = true;

        protected override void OnSpawn(SeededRandom rng, Transform container)
        {
            if (enemyPrefabs == null || enemyPrefabs.Length == 0)
            {
                Debug.LogWarning("[EnemyCampPOI] Herhangi bir dusman prefabi atanmamis!");
                return;
            }

            Vector3 campCenter = transform.position;
            Chunk chunk = GetComponentInParent<Chunk>();
            if (randomizeCampLocation && chunk != null)
            {
                float randX = rng.Range(-220f, 220f);
                float randZ = rng.Range(20f, 480f);
                campCenter = chunk.transform.position + new Vector3(randX, 0, randZ);
            }

            int spawnCount = rng.Range(minEnemies, maxEnemies + 1);
            Terrain tComponent = chunk?.ChunkTerrain;
            
            for (int i = 0; i < spawnCount; i++)
            {
                Vector2 randomCircle = rng.InsideUnitCircle * spawnRadius;
                Vector3 spawnPos = campCenter + new Vector3(randomCircle.x, 0, randomCircle.y);
                
                // Zemini bulmak icin Terrain ustunden dogrudan veri cekiyoruz (Physics motoruna ihtiyac kalmaz)
                if (tComponent != null)
                {
                    spawnPos.y = tComponent.SampleHeight(spawnPos) + tComponent.transform.position.y;
                }
                else if (Physics.Raycast(spawnPos + Vector3.up * 100f, Vector3.down, out RaycastHit hit, 200f))
                {
                    spawnPos.y = hit.point.y;
                }
                
                GameObject prefab = rng.Pick(enemyPrefabs);
                // Dusmani spawnla
#if UNITY_EDITOR
                if (!Application.isPlaying)
                {
                    GameObject e = UnityEditor.PrefabUtility.InstantiatePrefab(prefab, container) as GameObject;
                    if (e != null)
                    {
                        e.transform.position = spawnPos;
                        e.transform.rotation = Quaternion.Euler(0, rng.Range(0f, 360f), 0);
                    }
                }
                else
#endif
                {
                    Instantiate(prefab, spawnPos, Quaternion.Euler(0, rng.Range(0f, 360f), 0), container);
                }
            }
        }
        
        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0f, 0f, 0.5f);
            Gizmos.DrawWireSphere(transform.position, spawnRadius);
        }
    }
}
