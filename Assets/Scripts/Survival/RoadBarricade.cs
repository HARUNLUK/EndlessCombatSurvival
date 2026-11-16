using System.Collections.Generic;
using UnityEngine;
using EndlessSurvival.Inventory;
using EndlessSurvival.World;
using EndlessSurvival.World.Road;

namespace EndlessSurvival.Survival
{
    /// <summary>
    /// A roadblock: a fence line across the whole chunk with a blocked gate on the road. The vehicle cannot pass
    /// until the player gets out and clears the gate by hand (timed, noisy work; sometimes guarded). This is what
    /// makes the player stop: the road can no longer be driven from start to end without leaving the car.
    /// Which chunks have one comes from the world seed (the first one 16-20 chunks after the start, then every
    /// 12-24 chunks). A fast vehicle can smash the gate (<see cref="BreakByVehicle"/>). Opened gates are remembered in the journey save (WorldPopulator).
    /// </summary>
    public class RoadBarricade : MonoBehaviour, IInteractable
    {
        /// <summary>Chunks from the start to the first roadblock (min / max, 500 m each).</summary>
        public const int FirstMinChunks = 16, FirstMaxChunks = 20;
        /// <summary>Chunks from a roadblock to the next one (min / max).</summary>
        public const int NextMinChunks = 12, NextMaxChunks = 24;

        /// <summary>Speed (km/h) at which a vehicle smashes the gate.</summary>
        public const float BreakSpeedKmh = 40f;

        public const float GateWidth = 20f;
        private const float SegmentWidth = 6f;
        private const float FenceHeight = 4.5f;

        /// <summary>Chunk-local z of the roadblock line of each chunk that has one (other content keeps clear of it).</summary>
        private static readonly Dictionary<Chunk, float> Lines = new Dictionary<Chunk, float>();
        /// <summary>Chunk indices whose gate the player has opened on this journey.</summary>
        public static readonly HashSet<int> Opened = new HashSet<int>();
        public static readonly List<RoadBarricade> All = new List<RoadBarricade>();

        private static Material _fenceMat, _postMat, _gateMat, _stripeMat;

        public int ChunkIndex { get; private set; }
        public bool IsOpen { get; private set; }

        private GameObject _gateBody;
        private Vector3 _gateCenter;
        private bool _working;
        private bool _announced;
        private float _nextCheck;

        internal static void ResetAll()
        {
            Lines.Clear();
            Sequences.Clear();
            Opened.Clear();
            All.Clear();
        }

        /// <summary>Seeded roadblock sequence of one journey: relative chunk indices, generated as far as asked.</summary>
        private class Sequence
        {
            public readonly HashSet<int> Rels = new HashSet<int>();
            public int Last;
            public int Count;
        }

        private static readonly Dictionary<(int seed, int start), Sequence> Sequences = new Dictionary<(int, int), Sequence>();

        /// <summary>Seed rule: does this chunk carry a roadblock? First one 16-20 chunks after the start, then every 12-24 chunks.</summary>
        public static bool ChunkHasBarricade(int worldSeed, int chunkIndex, int startChunkIndex)
        {
            int rel = chunkIndex - startChunkIndex;
            if (rel < FirstMinChunks) return false;
            if (!Sequences.TryGetValue((worldSeed, startChunkIndex), out var seq))
            {
                seq = new Sequence();
                Sequences[(worldSeed, startChunkIndex)] = seq;
            }
            int baseHash = SeededRandom.Combine(worldSeed, "barricade");
            while (seq.Last < rel)
            {
                uint hash = (uint)SeededRandom.Combine(baseHash, seq.Count);
                int gap = seq.Count == 0
                    ? FirstMinChunks + (int)(hash % (uint)(FirstMaxChunks - FirstMinChunks + 1))
                    : NextMinChunks + (int)(hash % (uint)(NextMaxChunks - NextMinChunks + 1));
                seq.Last += gap;
                seq.Count++;
                seq.Rels.Add(seq.Last);
            }
            return seq.Rels.Contains(rel);
        }

        /// <summary>True when the point is close to the chunk's roadblock line (nothing else is placed there).</summary>
        public static bool NearLine(Chunk chunk, Vector3 worldPoint, float margin)
        {
            if (chunk == null || !Lines.TryGetValue(chunk, out float z)) return false;
            return Mathf.Abs(chunk.transform.InverseTransformPoint(worldPoint).z - z) < margin;
        }

        // ------------------------------------------------------------------ building

        /// <summary>Builds the roadblock of a chunk (null when the chunk has no terrain or no free place for the line).</summary>
        public static RoadBarricade Create(Chunk chunk, RoadSpline spline, Transform parent, SeededRandom rng)
        {
            Terrain terrain = chunk.ChunkTerrain;
            if (terrain == null || terrain.terrainData == null || spline == null) return null;

            // A line that does not run through a camp, a lighthouse or another point of interest
            float z = -1f;
            var pois = chunk.GetComponentsInChildren<EndlessSurvival.World.POI.PointOfInterest>(true);
            for (int attempt = 0; attempt < 8 && z < 0f; attempt++)
            {
                float candidate = chunk.chunkLength * rng.Range(0.3f, 0.7f);
                bool free = true;
                foreach (var poi in pois)
                    if (Mathf.Abs(chunk.transform.InverseTransformPoint(poi.transform.position).z - candidate) < 40f) { free = false; break; }
                if (free) z = candidate;
            }
            if (z < 0f) return null;

            Materials();
            var root = new GameObject("RoadBarricade");
            root.transform.SetParent(parent, false);
            root.transform.position = chunk.transform.position;
            root.transform.rotation = chunk.transform.rotation;

            // Where the line crosses the road
            Transform st = spline.transform;
            float splineZ = st.InverseTransformPoint(chunk.transform.TransformPoint(new Vector3(0f, 0f, z))).z;
            Vector3 roadWorld = st.TransformPoint(spline.GetPointAtZ(splineZ));
            Vector3 roadLocal = chunk.transform.InverseTransformPoint(roadWorld);
            roadLocal.z = z;

            // Fence across the terrain width, following the ground, open at the road
            Vector3 size = terrain.terrainData.size;
            Vector3 tPos = terrain.GetPosition();
            float x0 = chunk.transform.InverseTransformPoint(tPos).x;
            float x1 = x0 + size.x;
            float seaFloor = chunk.HasSea ? chunk.transform.position.y + WorldConstants.SeaLevel - 1f : float.MinValue;
            var fence = new GameObject("Fence").transform;
            fence.SetParent(root.transform, false);
            // Panels start right at the gate posts and go outwards on both sides, so no gap is left beside the gate
            foreach (float side in new[] { -1f, 1f })
            {
                for (float x = roadLocal.x + side * (GateWidth * 0.5f + SegmentWidth * 0.5f); x > x0 - SegmentWidth && x < x1 + SegmentWidth; x += side * SegmentWidth)
                {
                    Vector3 world = chunk.transform.TransformPoint(new Vector3(x, 0f, z));
                    float ground = Mathf.Max(terrain.SampleHeight(world) + tPos.y, seaFloor);
                    // Sunk into the ground so that slopes leave no gap under the panel
                    Block(fence, "Panel", new Vector3(x, ground - chunk.transform.position.y + FenceHeight * 0.5f - 2.5f, z),
                        new Vector3(SegmentWidth + 0.3f, FenceHeight + 5f, 0.5f), _fenceMat);
                }
            }

            // A wreck that happens to stand in the gate would keep the road shut after the gate is cleared
            foreach (var t in chunk.GetComponentsInChildren<Transform>(true))
            {
                if (t.name != "WreckCollider") continue;
                Vector3 local = chunk.transform.InverseTransformPoint(t.position);
                if (Mathf.Abs(local.z - z) > 8f || Mathf.Abs(local.x - roadLocal.x) > GateWidth * 0.5f + 2f) continue;
                Transform wreck = t.parent != null && t.parent.GetComponent<Chunk>() == null ? t.parent : t;
                wreck.gameObject.SetActive(false);
            }

            // Gate: two posts and the blocked opening between them
            float roadY = roadLocal.y;
            foreach (float side in new[] { -1f, 1f })
                Block(root.transform, "GatePost", new Vector3(roadLocal.x + side * (GateWidth * 0.5f + 0.4f), roadY + 2.6f, z), new Vector3(1.2f, 7.2f, 1.2f), _postMat);

            var gate = new GameObject("Gate");
            gate.transform.SetParent(root.transform, false);
            gate.transform.localPosition = new Vector3(roadLocal.x, roadY, z);
            var blocker = gate.AddComponent<BoxCollider>();
            blocker.center = new Vector3(0f, 1.6f, 0f);
            blocker.size = new Vector3(GateWidth, 5.2f, 1.6f);

            var body = new GameObject("Body").transform;
            body.SetParent(gate.transform, false);
            // Concrete blocks, a striped bar and junk piled across the road
            int blocks = Mathf.RoundToInt(GateWidth / 3.4f);
            for (int i = 0; i < blocks; i++)
            {
                float bx = -GateWidth * 0.5f + 1.7f + i * (GateWidth - 3.4f) / Mathf.Max(1, blocks - 1);
                var b = Visual(body, "Block", new Vector3(bx, 0.55f, rng.Range(-0.25f, 0.25f)), new Vector3(3f, 1.1f, 1.1f), i % 2 == 0 ? _gateMat : _postMat);
                b.transform.localRotation = Quaternion.Euler(0f, rng.Range(-9f, 9f), 0f);
            }
            Visual(body, "Bar", new Vector3(0f, 1.65f, 0f), new Vector3(GateWidth, 0.28f, 0.28f), _stripeMat);
            for (int i = 0; i < 4; i++)
                Visual(body, "Junk", new Vector3(rng.Range(-GateWidth * 0.42f, GateWidth * 0.42f), 1.45f, rng.Range(-0.3f, 0.3f)), new Vector3(rng.Range(0.8f, 1.6f), rng.Range(0.5f, 0.9f), rng.Range(0.7f, 1.1f)), _fenceMat)
                    .transform.localRotation = Quaternion.Euler(rng.Range(-8f, 8f), rng.Range(0f, 180f), rng.Range(-8f, 8f));

            var barricade = gate.AddComponent<RoadBarricade>();
            barricade.ChunkIndex = chunk.ChunkIndex;
            barricade._gateBody = body.gameObject;
            barricade._gateCenter = gate.transform.position + Vector3.up;
            Lines[chunk] = z;
            All.Add(barricade);
            if (Opened.Contains(chunk.ChunkIndex)) barricade.Open(false);
            return barricade;
        }

        private static GameObject Block(Transform parent, string name, Vector3 localPos, Vector3 scale, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = scale;
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
            return go;
        }

        /// <summary>A cube without its own collider (the gate has one collider for the whole opening).</summary>
        private static GameObject Visual(Transform parent, string name, Vector3 localPos, Vector3 scale, Material mat)
        {
            var go = Block(parent, name, localPos, scale, mat);
            Destroy(go.GetComponent<Collider>());
            return go;
        }

        private static void Materials()
        {
            if (_fenceMat != null) return;
            _fenceMat = Make("Barricade_Fence", new Color(0.33f, 0.31f, 0.28f));
            _postMat = Make("Barricade_Concrete", new Color(0.55f, 0.54f, 0.5f));
            _gateMat = Make("Barricade_Rust", new Color(0.5f, 0.27f, 0.14f));
            _stripeMat = Make("Barricade_Stripe", new Color(0.9f, 0.72f, 0.1f));
        }

        private static Material Make(string name, Color color)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            var mat = new Material(shader) { name = name, color = color };
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.1f);
            return mat;
        }

        private void OnDestroy() => All.Remove(this);

        // ------------------------------------------------------------------ opening

        public Vector3 InteractPosition => _gateCenter;
        public bool CanInteract => !IsOpen && !_working;

        public string GetPrompt()
        {
            var s = SurvivalSettings.Instance;
            return HasTools(SurvivalDirector.Inventory)
                ? $"Barikat: yolu aç (alet çantasıyla, ~{Mathf.RoundToInt(s.barricadeSeconds * s.barricadeToolMultiplier)} sn, gürültülü)"
                : $"Barikat: yolu elle aç (~{Mathf.RoundToInt(s.barricadeSeconds)} sn, gürültülü)";
        }

        private static bool HasTools(PlayerInventory user)
        {
            var toolkit = SurvivalSettings.Instance.Item("Item_ToolKit");
            return user != null && toolkit != null && user.Storage.GetCount(toolkit) > 0;
        }

        public string Interact(PlayerInventory user)
        {
            if (IsOpen) return null;
            var parts = VehicleParts.Instance;
            if (parts == null) return null;
            if (SurvivalDirector.DangerNearby(_gateCenter, 25f)) return "Önce etraftaki tehlikeyi hallet";
            var s = SurvivalSettings.Instance;
            float seconds = s.barricadeSeconds * (HasTools(user) ? s.barricadeToolMultiplier : 1f);
            // The work stays where the player stands (anywhere along the gate)
            Vector3 anchor = SurvivalDirector.Player != null ? SurvivalDirector.Player.transform.position : _gateCenter;
            _working = true;
            string msg = parts.StartJob("Barikat sökülüyor", seconds, anchor, s.barricadeNoiseRadius, () =>
            {
                _working = false;
                Open(true);
                CharacterStats.Instance?.Train(StatType.Strength, 0.6f);
                return "Yol açıldı. Araca dön.";
            });
            if (parts.CurrentJob == null) _working = false;
            else PlayerSurvivalAnimator.PlayOneShot(SurvAnim.Work, 2.5f);
            return msg;
        }

        /// <summary>Clears the gate: the junk goes away and the collider with it; the posts and the fence stay.</summary>
        public void Open(bool byPlayer)
        {
            if (IsOpen) return;
            IsOpen = true;
            if (_gateBody != null) _gateBody.SetActive(false);
            var blocker = GetComponent<BoxCollider>();
            if (blocker != null) blocker.enabled = false;
            if (byPlayer) Opened.Add(ChunkIndex);
        }

        /// <summary>A fast vehicle smashes the gate: opened for good (remembered), junk flies off.</summary>
        public void BreakByVehicle(Vector3 direction = default)
        {
            if (IsOpen) return;
            Vector3 center = _gateCenter;
            Open(true);
            SurvivalNotify.Show("Barikat kırıldı");
            if (direction.sqrMagnitude < 0.01f) direction = transform.forward;
            direction = new Vector3(direction.x, 0f, direction.z).normalized;
            StartCoroutine(Scatter(center, direction));
        }

        private System.Collections.IEnumerator Scatter(Vector3 center, Vector3 direction)
        {
            Materials();
            const int count = 9;
            const float life = 3f;
            var pieces = new Transform[count];
            var velocity = new Vector3[count];
            var spin = new Vector3[count];
            Transform parent = transform.parent;
            for (int i = 0; i < count; i++)
            {
                float lateral = Random.Range(-GateWidth * 0.4f, GateWidth * 0.4f);
                Vector3 pos = center + transform.right * lateral + Vector3.up * Random.Range(-0.3f, 0.8f);
                var piece = Visual(parent, "BarricadeDebris", Vector3.zero, new Vector3(Random.Range(0.6f, 1.4f), Random.Range(0.4f, 0.9f), Random.Range(0.6f, 1.2f)), i % 2 == 0 ? _gateMat : _postMat);
                piece.transform.position = pos;
                piece.transform.rotation = Random.rotation;
                pieces[i] = piece.transform;
                velocity[i] = direction * Random.Range(4f, 10f) + Vector3.up * Random.Range(3f, 7f) + transform.right * Random.Range(-3f, 3f);
                spin[i] = Random.insideUnitSphere * 360f;
            }
            for (float t = 0f; t < life; t += Time.deltaTime)
            {
                for (int i = 0; i < count; i++)
                {
                    if (pieces[i] == null) continue;
                    velocity[i].y -= 9.81f * Time.deltaTime;
                    pieces[i].position += velocity[i] * Time.deltaTime;
                    pieces[i].Rotate(spin[i] * Time.deltaTime, Space.Self);
                }
                yield return null;
            }
            for (int i = 0; i < count; i++)
                if (pieces[i] != null) Destroy(pieces[i].gameObject);
        }

        private void Update()
        {
            // A cancelled job can be started again
            if (_working && (VehicleParts.Instance == null || VehicleParts.Instance.CurrentJob == null)) _working = false;

            if (IsOpen || _announced || Time.time < _nextCheck) return;
            _nextCheck = Time.time + 0.5f;
            Vector3 p = SurvivalDirector.PlayerPosition;
            if (SurvivalDirector.Player == null || (p - _gateCenter).sqrMagnitude > 90f * 90f) return;
            _announced = true;
            SurvivalNotify.Warn(SurvivalDirector.PlayerInVehicle
                ? "Kapı kapalı: elle aç ya da hızla vurup kır (araç hasar alır)"
                : "Yol barikatla kapalı. Kapıdaki yığını sökersen araç geçer.", 7f);
        }
    }
}
