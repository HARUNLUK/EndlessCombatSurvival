using UnityEngine;
using System.Collections.Generic;
using EndlessSurvival.Story;
using EndlessSurvival.World;
using EndlessSurvival.World.Traps;
using EndlessCombat.AI;

namespace EndlessSurvival.World.POI
{
    /// <summary>
    /// Yol kenarında beliren, içi tuzaklı, düşman korumalı ve girişi çöken gizemli mağara eventi.
    /// </summary>
    public class CavePOI : PointOfInterest
    {
        [Header("Cave Narrative & Traps")]
        public CaveCollapseTrigger collapseTrigger;
        public TripwireTrap tripwire;
        public InteractableNote caveNote;

        [Header("Cavern Enemies & Lights")]
        public EnemyController[] caveGuards;
        public GameObject enemyPrefab;
        public Transform sittingGuardSpot;
        public Transform standingGuardSpot;
        public Light cavernFireLight;

        [Header("Cave Structure")]
        public Transform entrancePoint;
        public Transform exitPoint;

        private float _fireLightBaseIntensity = 2.5f;

        private void Awake()
        {
            if (cavernFireLight != null)
            {
                _fireLightBaseIntensity = cavernFireLight.intensity;
            }
        }

        private void Update()
        {
            // Mağara ateşi titreşim efekti (flicker)
            if (cavernFireLight != null)
            {
                float noise = Mathf.PerlinNoise(Time.time * 6f, 0f);
                cavernFireLight.intensity = _fireLightBaseIntensity * (0.8f + noise * 0.4f);
            }
        }

        protected override void OnSpawn(SeededRandom rng, Transform container)
        {
            List<EnemyController> activeGuards = new List<EnemyController>();
            if (caveGuards != null && caveGuards.Length > 0)
            {
                foreach (var g in caveGuards)
                {
                    if (g != null) activeGuards.Add(g);
                }
            }

            // Eğer prefab içerisinde guard referansı boşsa, dinamik olarak oluştur
            if (activeGuards.Count == 0 && enemyPrefab != null)
            {
                if (sittingGuardSpot != null)
                {
                    var g1 = Instantiate(enemyPrefab, sittingGuardSpot.position, sittingGuardSpot.rotation, transform);
                    g1.name = "CaveScavenger_Sitting";
                    var ec1 = g1.GetComponent<EnemyController>();
                    if (ec1 != null)
                    {
                        ec1.initialState = EnemyController.EnemyState.Sitting;
                        activeGuards.Add(ec1);
                    }
                }

                if (standingGuardSpot != null)
                {
                    var g2 = Instantiate(enemyPrefab, standingGuardSpot.position, standingGuardSpot.rotation, transform);
                    g2.name = "CaveScavenger_Guard";
                    var ec2 = g2.GetComponent<EnemyController>();
                    if (ec2 != null)
                    {
                        ec2.initialState = EnemyController.EnemyState.Idle;
                        activeGuards.Add(ec2);
                    }
                }
            }

            foreach (var guard in activeGuards)
            {
                if (guard != null)
                {
                    // Düşmanların görüş açısını ve uyanıklığını mağara karanlığına göre ayarla
                    guard.detectionRange = 22f;
                }
            }

            caveGuards = activeGuards.ToArray();

            // Girişin önünü yol kenarı seviyesinde düzleştir
            Chunk chunk = GetComponentInParent<Chunk>();
            Terrain terrain = chunk != null ? chunk.ChunkTerrain : null;
            if (terrain != null)
            {
                EndlessSurvival.World.Road.RoadTerrainAdapter.FlattenTerrainArea(terrain, transform.position, 12f, 10f, transform.position.y);
            }

            Debug.Log("<color=yellow>[CavePOI] Gizemli Maden Mağarası dünyada belirdi!</color>");
        }

        private CharacterController _playerCC;
        private TerrainCollider _terrainCol;

        public void OnPlayerEnterCave(Collider other)
        {
            if (!other.CompareTag("Player")) return;
            _playerCC = other.GetComponent<CharacterController>();
            if (_terrainCol == null)
            {
                var chunk = GetComponentInParent<Chunk>();
                if (chunk != null && chunk.ChunkTerrain != null)
                {
                    _terrainCol = chunk.ChunkTerrain.GetComponent<TerrainCollider>();
                }
            }

            if (_playerCC != null && _terrainCol != null)
            {
                Physics.IgnoreCollision(_playerCC, _terrainCol, true);
                Debug.Log("<color=cyan>[CavePOI] Oyuncu yer altı madenine indi. Yeraltı galerisi aktif.</color>");
            }
        }

        public void OnPlayerExitCave(Collider other)
        {
            if (!other.CompareTag("Player")) return;
            if (_playerCC != null && _terrainCol != null)
            {
                Physics.IgnoreCollision(_playerCC, _terrainCol, false);
                Debug.Log("<color=green>[CavePOI] Oyuncu yer üstüne çıktı. Zemin çarpışması normale döndü.</color>");
            }
        }

        private void OnDisable()
        {
            if (_playerCC != null && _terrainCol != null)
            {
                Physics.IgnoreCollision(_playerCC, _terrainCol, false);
            }
        }

        private void OnDestroy()
        {
            if (_playerCC != null && _terrainCol != null)
            {
                Physics.IgnoreCollision(_playerCC, _terrainCol, false);
            }
        }
    }

    /// <summary>
    /// Mağara giriş ve çıkış kapılarına yerleştirilen çift yönlü zemin geçiş tetikleyicisi.
    /// </summary>
    public class CaveZoneTrigger : MonoBehaviour
    {
        public bool isExit = false;
        private CavePOI _cave;

        private void Awake()
        {
            _cave = GetComponentInParent<CavePOI>();
        }

        private void OnTriggerEnter(Collider other)
        {
            if (_cave == null || !other.CompareTag("Player")) return;
            if (isExit)
                _cave.OnPlayerExitCave(other);
            else
                _cave.OnPlayerEnterCave(other);
        }

        private void OnTriggerExit(Collider other)
        {
            if (_cave == null || !other.CompareTag("Player")) return;
            // Eğer giriş kapısından dışarı doğru (dünyaya/yola doğru) çıktıysa çarpışmayı normale döndür
            if (!isExit && other.transform.position.z < transform.position.z)
            {
                _cave.OnPlayerExitCave(other);
            }
            // Eğer çıkış kapısından içeri doğru (mağaraya doğru) girdiyse çarpışmayı devre dışı bırak
            else if (isExit && other.transform.position.z < transform.position.z)
            {
                _cave.OnPlayerEnterCave(other);
            }
        }
    }
}
