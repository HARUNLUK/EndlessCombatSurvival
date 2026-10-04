using UnityEngine;
using System.Collections.Generic;
using EndlessSurvival.World;
using EndlessCombat.AI;
using EndlessSurvival.Story;

namespace EndlessSurvival.World.POI
{
    public class EnemyCampPOI : PointOfInterest
    {
        public enum CampType { Default, Campfire, Watchtower, Barricade, Outpost }

        [Header("Camp Settings")]
        public CampType campType = CampType.Default;

        [Header("Dialogue")]
        public DialogueSet[] possibleDialogues;
        [Tooltip("Oyuncu bu mesafeye geldiginde diyalog baslar")]
        public float dialogueTriggerDistance = 35f;
        private DialogueSet currentDialogue;
        private int currentLineIndex = 0;
        private float nextLineTime = 0f;
        private bool isDialogueActive = false;
        private bool hasDialogueStarted = false;
        private Vector3 actualCampCenter;
        private List<EnemyDialogue> spawnedSpeakers = new List<EnemyDialogue>();
        private List<EnemyDialogue> shuffledSpeakers = new List<EnemyDialogue>();
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

        private void Awake()
        {
            placementZone = POIPlacementZone.OffRoad;
            minRoadClearance = (campType == CampType.Outpost) ? 55f : 45f;
            actualCampCenter = transform.position;
        }

        private void Update()
        {
            if (currentDialogue != null)
            {
                if (!isDialogueActive && !hasDialogueStarted)
                {
                    var player = GameObject.FindGameObjectWithTag("Player");
                    if (player != null && Vector3.Distance(actualCampCenter, player.transform.position) < dialogueTriggerDistance)
                    {
                        isDialogueActive = true;
                        hasDialogueStarted = true;
                        nextLineTime = Time.time + 0.5f;
                    }
                }

                if (isDialogueActive && Time.time >= nextLineTime)
                {
                    var player = GameObject.FindGameObjectWithTag("Player");
                    if (player != null && Vector3.Distance(actualCampCenter, player.transform.position) <= dialogueTriggerDistance + 10f)
                    {
                        PlayNextDialogueLine();
                    }
                    else
                    {
                        // Player went too far, push the timer forward so they don't miss the lines
                        nextLineTime = Time.time + 1f;
                    }
                }
            }
        }

        protected override void OnSpawn(SeededRandom rng, Transform container)
        {
            if (enemyPrefabs == null || enemyPrefabs.Length == 0)
            {
#if UNITY_EDITOR
                var defaultEnemy = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Enemies/Enemy.prefab");
                if (defaultEnemy != null) enemyPrefabs = new GameObject[] { defaultEnemy };
#endif
                if (enemyPrefabs == null || enemyPrefabs.Length == 0)
                {
                    Debug.LogWarning("[EnemyCampPOI] Herhangi bir dusman prefabi atanmamis!");
                    return;
                }
            }

            Vector3 campCenter = transform.position;
            Chunk chunk = GetComponentInParent<Chunk>();
            Terrain tComponent = chunk?.ChunkTerrain;
            Road.RoadSpline spline = chunk?.GetComponentInChildren<Road.RoadSpline>();

            float flatR = (campType == CampType.Outpost) ? 19f : 12f;
            float blendR = (campType == CampType.Outpost) ? 12f : 9f;
            // Yolun şevini ve kampın düzleştirme platformunu korumak için gereken minimum güvenli mesafe
            float requiredClearance = flatR + blendR + 20f;

            if (randomizeCampLocation && chunk != null)
            {
                float randZ = rng.Range(70f, 430f);
                float roadLocalX = 0f;
                if (spline != null)
                {
                    Vector3 roadPt = spline.SampleAtZ(randZ);
                    roadLocalX = roadPt.x;
                }

                // Yolun sağına veya soluna, yoldan en az requiredClearance kadar uzağa yerleştir
                float side = rng.Value < 0.5f ? -1f : 1f;
                float offset = rng.Range(requiredClearance, requiredClearance + 55f);
                float campLocalX = roadLocalX + side * offset;

                // Chunk sınırları (-215 ile +215) içinde kalmasını sağla
                if (campLocalX < -215f || campLocalX > 215f)
                {
                    side = -side;
                    campLocalX = roadLocalX + side * offset;
                    campLocalX = Mathf.Clamp(campLocalX, -215f, 215f);
                }

                Vector3 localCampPos = new Vector3(campLocalX, 0f, randZ);
                campCenter = chunk.transform.TransformPoint(localCampPos);

                if (tComponent != null)
                {
                    campCenter.y = tComponent.SampleHeight(campCenter) + tComponent.transform.position.y;
                }
                transform.position = campCenter;
            }
            else
            {
                // Statik/manuel yerleşimde dahi yolun üstüne veya yakınına denk gelmişse yoldan uzağa ötele
                if (spline != null && chunk != null)
                {
                    float distToRoad = spline.GetDistanceToSpline(campCenter);
                    if (distToRoad < requiredClearance)
                    {
                        Vector3 localPos = chunk.transform.InverseTransformPoint(campCenter);
                        Vector3 roadPt = spline.SampleAtZ(localPos.z);
                        float side = (localPos.x >= roadPt.x) ? 1f : -1f;
                        localPos.x = roadPt.x + side * requiredClearance;
                        localPos.x = Mathf.Clamp(localPos.x, -215f, 215f);
                        campCenter = chunk.transform.TransformPoint(localPos);
                    }
                }

                if (tComponent != null)
                {
                    campCenter.y = tComponent.SampleHeight(campCenter) + tComponent.transform.position.y;
                    transform.position = campCenter;
                }
            }

            if (tComponent != null)
            {
                EndlessSurvival.World.Road.RoadTerrainAdapter.FlattenTerrainArea(tComponent, campCenter, flatR, blendR, campCenter.y);
            }

            actualCampCenter = transform.position;

            // Find any authored seat or guard spots in children
            List<Transform> seatSpots = new List<Transform>();
            List<Transform> guardSpots = new List<Transform>();

            Transform seatsGroup = transform.Find("SeatingArea") ?? transform.Find("Seats");
            if (seatsGroup != null)
            {
                for (int s = 0; s < seatsGroup.childCount; s++)
                {
                    var ch = seatsGroup.GetChild(s);
                    if (ch.name.StartsWith("Seat")) seatSpots.Add(ch);
                }
            }

            Transform guardsGroup = transform.Find("GuardSpots") ?? transform.Find("Guards");
            if (guardsGroup != null)
            {
                for (int g = 0; g < guardsGroup.childCount; g++)
                {
                    var ch = guardsGroup.GetChild(g);
                    if (ch.name.StartsWith("Guard")) guardSpots.Add(ch);
                }
            }

            int spawnCount = rng.Range(minEnemies, maxEnemies + 1);
            spawnedSpeakers.Clear();

            int seatIndex = 0;
            int guardIndex = 0;

            for (int i = 0; i < spawnCount; i++)
            {
                Vector3 spawnPos = campCenter;
                Quaternion spawnRot = Quaternion.Euler(0, rng.Range(0f, 360f), 0);
                bool shouldSit = false;

                if (seatIndex < seatSpots.Count)
                {
                    spawnPos = seatSpots[seatIndex].position;
                    spawnRot = seatSpots[seatIndex].rotation;
                    shouldSit = true;
                    seatIndex++;
                }
                else if (guardIndex < guardSpots.Count)
                {
                    spawnPos = guardSpots[guardIndex].position;
                    spawnRot = guardSpots[guardIndex].rotation;
                    shouldSit = false;
                    guardIndex++;
                }
                else
                {
                    Vector2 randomCircle = rng.InsideUnitCircle * spawnRadius;
                    spawnPos = campCenter + new Vector3(randomCircle.x, 0, randomCircle.y);
                    
                    if (tComponent != null)
                    {
                        spawnPos.y = tComponent.SampleHeight(spawnPos) + tComponent.transform.position.y;
                    }
                    else if (Physics.Raycast(spawnPos + Vector3.up * 100f, Vector3.down, out RaycastHit hit, 200f))
                    {
                        spawnPos.y = hit.point.y;
                    }

                    Vector3 dirToCenter = (campCenter - spawnPos).normalized;
                    dirToCenter.y = 0;
                    if (dirToCenter != Vector3.zero)
                    {
                        spawnRot = Quaternion.LookRotation(dirToCenter);
                    }

                    if (campType == CampType.Campfire && i < 2)
                    {
                        shouldSit = true;
                    }
                }
                
                GameObject prefab = rng.Pick(enemyPrefabs);
                GameObject spawnedEnemy = null;
#if UNITY_EDITOR
                if (!Application.isPlaying)
                {
                    spawnedEnemy = UnityEditor.PrefabUtility.InstantiatePrefab(prefab, container) as GameObject;
                    if (spawnedEnemy != null)
                    {
                        spawnedEnemy.transform.position = spawnPos;
                        spawnedEnemy.transform.rotation = spawnRot;
                    }
                }
                else
#endif
                {
                    spawnedEnemy = Instantiate(prefab, spawnPos, spawnRot, container);
                }

                if (spawnedEnemy != null)
                {
                    var controller = spawnedEnemy.GetComponent<EnemyController>();
                    if (controller != null)
                    {
                        if (shouldSit)
                        {
                            controller.initialState = EnemyController.EnemyState.Sitting;
                            controller.SetState(EnemyController.EnemyState.Sitting);
                        }
                    }

                    var dialogue = spawnedEnemy.GetComponent<EnemyDialogue>();
                    if (dialogue == null) dialogue = spawnedEnemy.AddComponent<EnemyDialogue>();
                    spawnedSpeakers.Add(dialogue);
                }
            }

            // Setup Dialogue Fallback
            if (possibleDialogues == null || possibleDialogues.Length == 0)
            {
                possibleDialogues = Resources.LoadAll<DialogueSet>("Dialogues");
            }

            if (Application.isPlaying && possibleDialogues != null && possibleDialogues.Length > 0 && spawnedSpeakers.Count > 0)
            {
                PickNewDialogue(rng);
            }
        }

        private void PickNewDialogue(SeededRandom rng)
        {
            currentDialogue = rng.Pick(possibleDialogues);
            currentLineIndex = 0;
            
            // Shuffle speakers so different people talk each time
            shuffledSpeakers = new List<EnemyDialogue>(spawnedSpeakers);
            for (int i = 0; i < shuffledSpeakers.Count; i++)
            {
                EnemyDialogue temp = shuffledSpeakers[i];
                int randomIndex = rng.Range(i, shuffledSpeakers.Count);
                shuffledSpeakers[i] = shuffledSpeakers[randomIndex];
                shuffledSpeakers[randomIndex] = temp;
            }
        }

        private void PlayNextDialogueLine()
        {
            if (currentLineIndex >= currentDialogue.lines.Length)
            {
                // Dialogue finished. Wait 4 seconds, then pick a new one and loop.
                var chunk = GetComponentInParent<Chunk>();
                SeededRandom rng = chunk != null ? chunk.LootRandom : new SeededRandom(Random.Range(0, 999999));
                PickNewDialogue(rng);
                
                isDialogueActive = true;
                hasDialogueStarted = true;
                nextLineTime = Time.time + 4f;
                return;
            }

            var line = currentDialogue.lines[currentLineIndex];
            
            // Find speaker from the shuffled list
            int speakerIdx = line.speakerIndex % shuffledSpeakers.Count;
            var speaker = shuffledSpeakers[speakerIdx];

            if (speaker != null)
            {
                speaker.Speak(line.text, line.duration);
                nextLineTime = Time.time + line.duration + currentDialogue.pauseBetweenLines;
            }
            else
            {
                // Speaker is dead or missing, skip line
                nextLineTime = Time.time + 0.5f;
            }

            currentLineIndex++;
        }
        
        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0f, 0f, 0.5f);
            Gizmos.DrawWireSphere(transform.position, spawnRadius);
        }
    }
}
