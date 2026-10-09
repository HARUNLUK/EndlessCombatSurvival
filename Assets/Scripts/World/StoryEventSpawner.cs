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
            // ChunkManager chunks spawn their event in Chunk.Initialize (possibly a few frames after this Start,
            // once the seed streams exist); only stand-alone chunks spawn it here
            if (chunk != null && chunk.Manager == null && chunk.IsBuilt)
            {
                SpawnEventIfEligible(chunk);
            }
        }

        private void EnsureEventsConfigured()
        {
            if (availableEvents == null || availableEvents.Length == 0)
            {
                var list = new List<StoryEventDefinition>();
#if UNITY_EDITOR
                var lightEvt = UnityEditor.AssetDatabase.LoadAssetAtPath<StoryEventDefinition>("Assets/Data/Events/Event_Lighthouse.asset");
                var caveEvt = UnityEditor.AssetDatabase.LoadAssetAtPath<StoryEventDefinition>("Assets/Data/Events/Event_Cave.asset");
                if (lightEvt != null) list.Add(lightEvt);
                if (caveEvt != null) list.Add(caveEvt);
#endif
                if (list.Count == 0)
                {
                    var lightRes = Resources.Load<StoryEventDefinition>("Events/Event_Lighthouse");
                    var caveRes = Resources.Load<StoryEventDefinition>("Events/Event_Cave");
                    if (lightRes != null) list.Add(lightRes);
                    if (caveRes != null) list.Add(caveRes);
                }

                if (list.Count > 0)
                {
                    availableEvents = list.ToArray();
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
                    if (TryGetEventSpawnTransform(chunk, evt, rng, out Vector3 spawnPos, out Quaternion spawnRot))
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

                        Debug.Log($"<color=green>[StoryEventSpawner] '{evt.eventName}' Chunk #{chunkIndex} üzerinde yerleştirildi! (Zone: {evt.placementZone})</color>");
                        break; // Bu chunk için bir event yeterli
                    }
                }
            }
        }

#if UNITY_EDITOR
        public void GenerateEventPreview(SeededRandom rng, Transform container)
        {
            EnsureEventsConfigured();
            if (availableEvents == null || availableEvents.Length == 0) return;
            Chunk chunk = GetComponentInParent<Chunk>();
            if (roadSpline == null) roadSpline = GetComponentInChildren<RoadSpline>();
            if (roadSpline == null) return;

            // Editör önizlemesinde test amacıyla seed'e göre şans kontrolü yap
            List<StoryEventDefinition> candidates = new List<StoryEventDefinition>();
            foreach (var evt in availableEvents)
            {
                if (evt != null && rng.Chance(evt.spawnChance))
                {
                    candidates.Add(evt);
                }
            }

            // Editör önizlemesinde test amacıyla hiçbir aday şanstan çıkmadıysa bile
            // sahnede boş kalmaması için ilk uygun eventi (örneğin Mağara) garantili seç
            if (candidates.Count == 0 && availableEvents.Length > 0)
            {
                for (int i = 0; i < availableEvents.Length; i++)
                {
                    if (availableEvents[i] != null && availableEvents[i].eventPrefab != null)
                    {
                        candidates.Add(availableEvents[i]);
                        break;
                    }
                }
            }

            if (candidates.Count == 0) return;

            StoryEventDefinition selectedEvt = rng.Pick(candidates.ToArray());
            if (selectedEvt == null || selectedEvt.eventPrefab == null) return;

            if (TryGetEventSpawnTransform(chunk, selectedEvt, rng, out Vector3 spawnPos, out Quaternion spawnRot))
            {
                GameObject spawned = UnityEditor.PrefabUtility.InstantiatePrefab(selectedEvt.eventPrefab, container) as GameObject;
                if (spawned != null)
                {
                    spawned.transform.position = spawnPos;
                    spawned.transform.rotation = spawnRot;
                    spawned.name = $"{selectedEvt.eventName}_Preview";

                    var poi = spawned.GetComponent<PointOfInterest>();
                    if (poi != null)
                    {
                        poi.InitializeFromChunk(rng.SubStream("poi_preview"), container);
                    }

                    Debug.Log($"<color=green>[StoryEventSpawner Preview] '{selectedEvt.eventName}' önizlemede yerleştirildi! (Zone: {selectedEvt.placementZone})</color>");
                }
            }
        }
#endif

        private bool TryGetEventSpawnTransform(Chunk chunk, StoryEventDefinition evt, SeededRandom rng, out Vector3 position, out Quaternion rotation)
        {
            position = Vector3.zero;
            rotation = Quaternion.identity;

            if (evt == null) return false;
            if (chunk == null) chunk = GetComponentInParent<Chunk>();
            if (roadSpline == null && chunk != null) roadSpline = chunk.GetComponentInChildren<RoadSpline>();

            // 1. ÖZEL DURUM: YOL ÜSTÜ NESNELERİ (Barikat, Kaza, Kontrol Noktası vb.)
            if (evt.placementZone == POIPlacementZone.OnRoad || evt.isAllowedOnRoad)
            {
                if (chunk != null && chunk.TryGetOnRoadPosition(rng, out position, out rotation))
                {
                    return true;
                }
            }

            // 2. YOL DIŞI VE YOL KENARI NESNELERİ (Mağara, Kamp, Deniz Feneri vb.)
            // Bu nesneler ASLA yolun üstünde veya yol koridorunda oluşamaz!
            if (roadSpline == null) return false;

            float minSafeDist = Mathf.Max(evt.lateralDistance, evt.minRoadDistance, 36f);
            float t = rng.Range(0.35f, 0.65f);
            Transform st = roadSpline.transform;
            Vector3 center = st.TransformPoint(roadSpline.GetPoint(t));
            Vector3 right = st.TransformDirection(roadSpline.GetRight(t));

            float side = rng.Value < 0.5f ? -1f : 1f;
            // Kıyı chunk'ında mağara/fener gibi yapılar her zaman kara tarafında olur
            if (chunk != null) side = chunk.GetLandSide(side);
            Vector3 probePos = center + right * (side * minSafeDist);

            if (chunk != null)
            {
                Vector3 localInChunk = chunk.transform.InverseTransformPoint(probePos);
                if (localInChunk.x < -215f || localInChunk.x > 215f)
                {
                    if (!chunk.IsCoast) side = -side;
                    probePos = center + right * (side * minSafeDist);
                    localInChunk = chunk.transform.InverseTransformPoint(probePos);
                    localInChunk.x = Mathf.Clamp(localInChunk.x, -215f, 215f);
                    probePos = chunk.transform.TransformPoint(localInChunk);
                }
            }

            // Zemin yüksekliğini bul
            Terrain terrain = chunk != null ? chunk.ChunkTerrain : null;
            if (terrain != null)
            {
                probePos.y = terrain.SampleHeight(probePos) + terrain.transform.position.y;
            }
            else if (Physics.Raycast(probePos + Vector3.up * 80f, Vector3.down, out RaycastHit hit, 200f, groundMask, QueryTriggerInteraction.Ignore))
            {
                probePos.y = hit.point.y;
            }

            // Yol orta çizgisine olan mesafeyi kesin olarak doğrula ve garantile
            float actualDistToRoad = roadSpline.GetDistanceToSpline(probePos);
            if (actualDistToRoad < minSafeDist)
            {
                Vector3 awayFromRoad = (probePos - center).normalized;
                awayFromRoad.y = 0;
                probePos += awayFromRoad * (minSafeDist - actualDistToRoad);
                if (terrain != null)
                {
                    probePos.y = terrain.SampleHeight(probePos) + terrain.transform.position.y;
                }
            }

            position = probePos;

            // Yönelim Hesabı:
            // Mağara gibi yapay derinliği olan nesnelerde (orientAwayFromRoad = true), yapının tüneli (+Z) yoldan uzağa dağa doğru uzanmalıdır.
            if (evt.orientAwayFromRoad)
            {
                Vector3 dirAwayFromRoad = (position - center).normalized;
                dirAwayFromRoad.y = 0f;
                if (dirAwayFromRoad != Vector3.zero)
                {
                    rotation = Quaternion.LookRotation(dirAwayFromRoad);
                }
            }
            else
            {
                Vector3 dirToRoad = (center - position).normalized;
                dirToRoad.y = 0f;
                if (dirToRoad != Vector3.zero)
                {
                    rotation = Quaternion.LookRotation(dirToRoad);
                }
            }

            return true;
        }
    }
}
