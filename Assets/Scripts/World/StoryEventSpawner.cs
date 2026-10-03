using System.Collections.Generic;
using UnityEngine;
using EndlessSurvival.Story;
using EndlessSurvival.World.Road;
using EndlessSurvival.World.POI;

namespace EndlessSurvival.World
{
    /// <summary>
    /// Spawns narrative roadside story events (like the Lighthouse) along the road using the Chunk's EventsRandom sub-stream.
    /// </summary>
    public class StoryEventSpawner : MonoBehaviour
    {
        private static int _lastSpawnedChunkIndex = -10;

        [Header("Event Definitions")]
        public StoryEventDefinition[] availableEvents;

        [Header("Road & Placement")]
        public RoadSpline roadSpline;
        public LayerMask groundMask = ~0;

        private void Start()
        {
            if (roadSpline == null) roadSpline = GetComponentInChildren<RoadSpline>();
            Chunk chunk = GetComponentInParent<Chunk>();
            if (chunk != null)
            {
                SpawnEventIfEligible(chunk);
            }
        }

        private void EnsureEventsConfigured()
        {
            if (availableEvents == null || availableEvents.Length == 0)
            {
                var evt = Resources.Load<StoryEventDefinition>("Events/Event_Lighthouse");
#if UNITY_EDITOR
                if (evt == null)
                {
                    evt = UnityEditor.AssetDatabase.LoadAssetAtPath<StoryEventDefinition>("Assets/Data/Events/Event_Lighthouse.asset");
                }
#endif
                if (evt != null)
                {
                    availableEvents = new StoryEventDefinition[] { evt };
                }
                else
                {
                    var def = ScriptableObject.CreateInstance<StoryEventDefinition>();
                    def.eventId = "Event_Lighthouse";
                    def.eventName = "Yalnız Deniz Feneri";
                    def.spawnChance = 0.38f;
                    def.minChunkInterval = 3;
                    def.minChunkIndex = 2;
                    def.lateralDistance = 45f;
                    availableEvents = new StoryEventDefinition[] { def };
                }
            }
        }

        public void SpawnEventIfEligible(Chunk chunk)
        {
            EnsureEventsConfigured();
            if (availableEvents == null || availableEvents.Length == 0) return;
            if (roadSpline == null) roadSpline = GetComponentInChildren<RoadSpline>();
            if (roadSpline == null) return;

            SeededRandom rng = chunk.EventsRandom;
            int chunkIndex = chunk.ChunkIndex;

            foreach (var evt in availableEvents)
            {
                if (evt == null) continue;

                // Minimum chunk aralığı ve başlangıç kontrolü
                if (chunkIndex < evt.minChunkIndex) continue;
                if (chunkIndex - _lastSpawnedChunkIndex < evt.minChunkInterval) continue;

                // Şans kontrolü
                if (rng.Chance(evt.spawnChance))
                {
                    if (TryGetRoadsidePosition(rng, evt.lateralDistance, out Vector3 spawnPos, out Quaternion spawnRot))
                    {
                        GameObject spawnedEvent;
                        if (evt.eventPrefab != null)
                        {
                            spawnedEvent = Instantiate(evt.eventPrefab, spawnPos, spawnRot, transform);
                        }
                        else
                        {
                            spawnedEvent = new GameObject("LighthousePOI");
                            spawnedEvent.transform.position = spawnPos;
                            spawnedEvent.transform.rotation = spawnRot;
                            spawnedEvent.transform.SetParent(transform, true);
                            spawnedEvent.AddComponent<LighthousePOI>();
                        }

                        spawnedEvent.name = $"{evt.eventName}_Chunk{chunkIndex}";
                        _lastSpawnedChunkIndex = chunkIndex;

                        // POI başlatması
                        var poi = spawnedEvent.GetComponent<PointOfInterest>();
                        if (poi != null)
                        {
                            poi.InitializeFromChunk(rng.SubStream("poi_instance"));
                        }

                        Debug.Log($"<color=green>[StoryEventSpawner] '{evt.eventName}' Chunk #{chunkIndex} üzerinde yol kenarına yerleştirildi!</color>");
                        break; // Bu chunk için bir event yeterli
                    }
                }
            }
        }

        private bool TryGetRoadsidePosition(SeededRandom rng, float lateralDist, out Vector3 position, out Quaternion rotation)
        {
            position = Vector3.zero;
            rotation = Quaternion.identity;

            if (roadSpline == null) return false;

            // Yolun ortalarına doğru bir nokta seç (0.35 ile 0.65 arası)
            float t = rng.Range(0.35f, 0.65f);
            Transform st = roadSpline.transform;
            Vector3 center = st.TransformPoint(roadSpline.GetPoint(t));
            Vector3 forward = st.TransformDirection(roadSpline.GetTangent(t));
            Vector3 right = st.TransformDirection(roadSpline.GetRight(t));

            // Sağ mı sol mu? (Rastgele)
            float side = rng.Value < 0.5f ? -1f : 1f;
            Vector3 probePos = center + right * (side * lateralDist);

            // Zemin yüksekliğini bul
            Chunk chunk = GetComponentInParent<Chunk>();
            Terrain terrain = chunk != null ? chunk.ChunkTerrain : null;

            if (terrain != null)
            {
                probePos.y = terrain.SampleHeight(probePos) + terrain.transform.position.y;
            }
            else if (Physics.Raycast(probePos + Vector3.up * 80f, Vector3.down, out RaycastHit hit, 200f, groundMask, QueryTriggerInteraction.Ignore))
            {
                probePos.y = hit.point.y;
            }

            position = probePos;
            // Yola doğru veya hafif açıyla baksın
            Vector3 dirToRoad = (center - position).normalized;
            dirToRoad.y = 0f;
            if (dirToRoad != Vector3.zero)
            {
                rotation = Quaternion.LookRotation(dirToRoad);
            }

            return true;
        }
    }
}
