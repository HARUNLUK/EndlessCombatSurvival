using UnityEngine;

namespace EndlessSurvival.World.POI
{
    public class EnemyCampPOI : PointOfInterest
    {
        [Header("Enemy Spawning")]
        [Tooltip("Düşman prefab'ları")]
        public GameObject[] enemyPrefabs;
        
        [Tooltip("Oluşturulacak minimum düşman sayısı")]
        public int minEnemies = 2;
        
        [Tooltip("Oluşturulacak maksimum düşman sayısı")]
        public int maxEnemies = 5;
        
        [Tooltip("Kampın etrafında düşmanların spawnlanacağı alanın yarıçapı")]
        public float spawnRadius = 15f;

        protected override void OnSpawn()
        {
            if (enemyPrefabs == null || enemyPrefabs.Length == 0)
            {
                Debug.LogWarning("[EnemyCampPOI] Herhangi bir düşman prefabı atanmamış!");
                return;
            }

            int spawnCount = Random.Range(minEnemies, maxEnemies + 1);
            
            for (int i = 0; i < spawnCount; i++)
            {
                Vector2 randomCircle = Random.insideUnitCircle * spawnRadius;
                Vector3 spawnPos = transform.position + new Vector3(randomCircle.x, 0, randomCircle.y);
                
                // Zemini bulmak için yukarıdan aşağıya raycast atıyoruz
                // Terrain layerını baz alıyoruz, duruma göre Default da kontrol edilebilir
                if (Physics.Raycast(spawnPos + Vector3.up * 100f, Vector3.down, out RaycastHit hit, 200f))
                {
                    spawnPos.y = hit.point.y;
                }
                
                GameObject prefab = enemyPrefabs[Random.Range(0, enemyPrefabs.Length)];
                // Düşmanı spawnla
                Instantiate(prefab, spawnPos, Quaternion.Euler(0, Random.Range(0f, 360f), 0), transform);
            }
        }
        
        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0f, 0f, 0.5f);
            Gizmos.DrawWireSphere(transform.position, spawnRadius);
        }
    }
}
