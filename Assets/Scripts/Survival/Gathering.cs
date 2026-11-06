using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using EndlessCombat.Combat;
using EndlessSurvival.Inventory;
using EndlessSurvival.World;

namespace EndlessSurvival.Survival
{
    public enum WaterKind
    {
        None,
        Fresh,
        Sea
    }

    /// <summary>
    /// 2.9: finds water around a point: the sea on coast chunks (terrain below sea level) and the
    /// River_Water / Stream_Water meshes of field chunks (point-in-triangle on the mesh).
    /// </summary>
    public static class WaterBodies
    {
        private class MeshCache
        {
            public Transform transform;
            public Bounds bounds;
            public Vector3[] world;
            public int[] triangles;
        }

        private static readonly Dictionary<Chunk, List<MeshCache>> Cache = new Dictionary<Chunk, List<MeshCache>>();

        public static bool Query(Vector3 pos, out float surfaceY, out WaterKind kind)
        {
            surfaceY = 0f;
            kind = WaterKind.None;
            var chunk = SurvivalDirector.ChunkAt(pos);
            if (chunk == null) return false;

            if (chunk.HasSea && chunk.ChunkTerrain != null)
            {
                float seaY = chunk.transform.position.y + WorldConstants.SeaLevel;
                Terrain t = chunk.ChunkTerrain;
                float ground = t.SampleHeight(pos) + t.transform.position.y;
                if (ground < seaY - 0.05f)
                {
                    surfaceY = seaY;
                    kind = WaterKind.Sea;
                    return true;
                }
            }

            foreach (var m in Meshes(chunk))
            {
                if (m.transform == null) continue;
                if (pos.x < m.bounds.min.x || pos.x > m.bounds.max.x || pos.z < m.bounds.min.z || pos.z > m.bounds.max.z) continue;
                var v = m.world;
                var tri = m.triangles;
                for (int i = 0; i + 2 < tri.Length; i += 3)
                {
                    if (InTriangle(pos, v[tri[i]], v[tri[i + 1]], v[tri[i + 2]], out float y))
                    {
                        surfaceY = y;
                        kind = WaterKind.Fresh;
                        return true;
                    }
                }
            }
            return false;
        }

        /// <summary>Water within reach (center and a ring around the point).</summary>
        public static WaterKind Near(Vector3 pos, float radius)
        {
            if (Query(pos, out _, out WaterKind k)) return k;
            for (int i = 0; i < 8; i++)
            {
                float a = i * Mathf.PI * 0.25f;
                Vector3 p = pos + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * radius;
                if (Query(p, out _, out k)) return k;
            }
            return WaterKind.None;
        }

        /// <summary>Water point in front of the player for the fishing bobber.</summary>
        public static bool FindWaterInFront(Vector3 origin, Vector3 forward, out Vector3 point, out WaterKind kind)
        {
            forward.y = 0f;
            forward.Normalize();
            for (float d = 1.5f; d <= 9f; d += 0.75f)
            {
                Vector3 p = origin + forward * d;
                if (Query(p, out float y, out kind))
                {
                    point = new Vector3(p.x, y, p.z);
                    return true;
                }
            }
            point = default;
            kind = WaterKind.None;
            return false;
        }

        private static List<MeshCache> Meshes(Chunk chunk)
        {
            Chunk id = chunk;
            if (Cache.TryGetValue(id, out var list))
            {
                if (list.Count == 0 || list[0].transform != null) return list;
                Cache.Remove(id);
            }
            list = new List<MeshCache>();
            foreach (var mf in chunk.GetComponentsInChildren<MeshFilter>(true))
            {
                string n = mf.gameObject.name;
                if (!n.Contains("River_Water") && !n.Contains("Stream_Water") && !n.Contains("Lake_Water")) continue;
                var mesh = mf.sharedMesh;
                if (mesh == null || !mesh.isReadable) continue;
                var local = mesh.vertices;
                var world = new Vector3[local.Length];
                var b = new Bounds();
                for (int i = 0; i < local.Length; i++)
                {
                    world[i] = mf.transform.TransformPoint(local[i]);
                    if (i == 0) b = new Bounds(world[i], Vector3.zero);
                    else b.Encapsulate(world[i]);
                }
                list.Add(new MeshCache { transform = mf.transform, bounds = b, world = world, triangles = mesh.triangles });
            }
            // Only cache once the chunk finished building (water meshes appear during the build)
            if (chunk.IsBuilt) Cache[id] = list;
            return list;
        }

        private static bool InTriangle(Vector3 p, Vector3 a, Vector3 b, Vector3 c, out float y)
        {
            y = 0f;
            float d = (b.z - c.z) * (a.x - c.x) + (c.x - b.x) * (a.z - c.z);
            if (Mathf.Abs(d) < 1e-6f) return false;
            float w1 = ((b.z - c.z) * (p.x - c.x) + (c.x - b.x) * (p.z - c.z)) / d;
            float w2 = ((c.z - a.z) * (p.x - c.x) + (a.x - c.x) * (p.z - c.z)) / d;
            float w3 = 1f - w1 - w2;
            if (w1 < 0f || w2 < 0f || w3 < 0f) return false;
            y = w1 * a.y + w2 * b.y + w3 * c.y;
            return true;
        }

        public static void ClearCache() => Cache.Clear();
    }

    /// <summary>
    /// 2.9: [E] fill an empty bottle at water. Sits on the player object; its interact position is pushed
    /// forward so real objects near the player still win the prompt.
    /// </summary>
    public class WaterFillInteractable : MonoBehaviour, IInteractable
    {
        private WaterKind _kind;
        private float _nextScan;

        public Vector3 InteractPosition => transform.position + transform.forward * 2.3f;

        public bool CanInteract
        {
            get
            {
                if (Time.time >= _nextScan)
                {
                    _nextScan = Time.time + 0.4f;
                    var bottle = SurvivalSettings.Instance.Item("Item_EmptyBottle");
                    var inv = GetComponent<PlayerInventory>();
                    _kind = bottle != null && inv != null && inv.Storage.GetCount(bottle) > 0 ? WaterBodies.Near(transform.position, 2.2f) : WaterKind.None;
                }
                return _kind != WaterKind.None;
            }
        }

        public string GetPrompt() => _kind == WaterKind.Sea ? "Şişeye deniz suyu doldur (tuzlu)" : "Şişeye dereden su doldur (kirli)";

        public string Interact(PlayerInventory user)
        {
            var s = SurvivalSettings.Instance;
            var bottle = s.Item("Item_EmptyBottle");
            var water = s.Item(_kind == WaterKind.Sea ? "Item_SaltWater" : "Item_DirtyWater");
            if (bottle == null || water == null || user.Storage.GetCount(bottle) <= 0) return "Boş şişe yok";
            user.Storage.Remove(bottle, 1);
            if (user.Storage.Add(water, 1) <= 0)
            {
                user.Storage.Add(bottle, 1);
                return "Çantada yer yok";
            }
            _nextScan = 0f;
            return $"{water.DisplayName} dolduruldu";
        }
    }

    public enum ResourceKind
    {
        DeadTree,
        BerryBush,
        Herb,
        Mushroom
    }

    /// <summary>
    /// 2.4 / 2.13: gatherable nature objects. Dead trees give firewood when hit (melee), plants are picked with [E].
    /// Yield is fixed by the chunk seed; Nature may add one more.
    /// </summary>
    public class ResourceNode : MonoBehaviour, IInteractable, IMeleeReactive
    {
        public ResourceKind kind;
        public int remaining = 3;
        public bool poisonous;

        private float _chopProgress;

        public Vector3 InteractPosition => transform.position;
        public bool CanInteract => remaining > 0;

        public string GetPrompt()
        {
            switch (kind)
            {
                case ResourceKind.DeadTree: return "Kuru ağaç: vurarak odun topla (sol tık)";
                case ResourceKind.BerryBush: return "Böğürtlen topla";
                case ResourceKind.Herb: return "Şifalı bitki topla";
                default:
                    bool known = CharacterStats.Instance != null && CharacterStats.Instance.Effective(StatType.Nature) >= 20f;
                    return known ? (poisonous ? "Mantar topla (ZEHİRLİ)" : "Mantar topla (yenilebilir)") : "Mantar topla";
            }
        }

        public string Interact(PlayerInventory user)
        {
            if (kind == ResourceKind.DeadTree) return "Odun için ağaca vur (sol tık, sopa/bıçak daha hızlı)";
            var s = SurvivalSettings.Instance;
            string itemName = kind == ResourceKind.BerryBush ? "Item_Berries"
                            : kind == ResourceKind.Herb ? "Item_Herb"
                            : poisonous ? "Item_PoisonMushroom" : "Item_Mushroom";
            var item = s.Item(itemName);
            if (item == null) return "Eşya tanımı yok (setup çalıştırılmalı)";
            int amount = remaining;
            if (Random.value < StatSettings.Instance.natureExtraYieldChance * CharacterStats.Frac(StatType.Nature)) amount += 1;
            int added = user.AddItem(item, amount);
            if (added <= 0) return "Çantada yer yok";
            remaining = 0;
            CharacterStats.Instance?.Train(StatType.Nature, StatSettings.Instance.naturePerGather);
            Deplete();
            return $"{item.DisplayName} x{added} toplandı";
        }

        public void OnMeleeHit(Transform attacker, MeleeStats stats, Vector3 point)
        {
            if (kind != ResourceKind.DeadTree || remaining <= 0) return;
            if (attacker == null || !attacker.CompareTag("Player")) return;
            _chopProgress += Mathf.Max(5f, stats.damage);
            if (_chopProgress < 30f) return;
            _chopProgress = 0f;
            var wood = SurvivalSettings.Instance.Item("Item_Firewood");
            if (wood == null) return;
            int amount = 1 + (Random.value < StatSettings.Instance.natureExtraYieldChance * CharacterStats.Frac(StatType.Nature) * 0.5f ? 1 : 0);
            SurvivalDirector.GiveItem(wood, amount);
            remaining--;
            CharacterStats.Instance?.Train(StatType.Nature, StatSettings.Instance.naturePerGather * 0.5f);
            CharacterStats.Instance?.Train(StatType.Strength, 0.1f);
            SurvivalNotify.Show($"Odun x{amount}" + (remaining > 0 ? "" : " (ağaç bitti)"));
            transform.localScale *= 0.85f;
            if (remaining <= 0) Deplete();
        }

        private void Deplete()
        {
            if (kind == ResourceKind.DeadTree)
            {
                // Leave a stump
                foreach (Transform child in transform)
                    if (child.name != "Stump") child.gameObject.SetActive(false);
                return;
            }
            gameObject.SetActive(false);
        }

        // ---------------- creation ----------------

        public static ResourceNode Create(ResourceKind kind, Vector3 position, Transform parent, SeededRandom rng)
        {
            var root = new GameObject("Resource_" + kind);
            root.transform.SetParent(parent, true);
            root.transform.position = position;
            root.transform.rotation = Quaternion.Euler(0f, rng.Range(0f, 360f), 0f);
            var node = root.AddComponent<ResourceNode>();
            node.kind = kind;

            switch (kind)
            {
                case ResourceKind.DeadTree:
                {
                    Color bark = new Color(0.33f, 0.27f, 0.2f);
                    var stump = SurvivalPrimitives.Part(PrimitiveType.Cylinder, root.transform, new Vector3(0f, 0.25f, 0f), new Vector3(0.45f, 0.25f, 0.45f), bark);
                    stump.name = "Stump";
                    float h = rng.Range(2.2f, 3.6f);
                    var trunk = SurvivalPrimitives.Part(PrimitiveType.Cylinder, root.transform, new Vector3(0f, 0.5f + h * 0.5f, 0f), new Vector3(0.32f, h * 0.5f, 0.32f), bark);
                    trunk.transform.localRotation = Quaternion.Euler(rng.Range(-6f, 6f), 0f, rng.Range(-6f, 6f));
                    var branch = SurvivalPrimitives.Part(PrimitiveType.Cylinder, root.transform, new Vector3(0.35f, 0.4f + h * 0.75f, 0f), new Vector3(0.12f, 0.6f, 0.12f), bark);
                    branch.transform.localRotation = Quaternion.Euler(0f, 0f, -50f);
                    node.remaining = rng.Range(2, 5);
                    break;
                }
                case ResourceKind.BerryBush:
                {
                    SurvivalPrimitives.Part(PrimitiveType.Sphere, root.transform, new Vector3(0f, 0.45f, 0f), new Vector3(1.1f, 0.8f, 1.1f), new Color(0.2f, 0.38f, 0.16f));
                    for (int i = 0; i < 5; i++)
                    {
                        Vector3 p = new Vector3(rng.Range(-0.45f, 0.45f), rng.Range(0.4f, 0.8f), rng.Range(-0.45f, 0.45f));
                        SurvivalPrimitives.Part(PrimitiveType.Sphere, root.transform, p, Vector3.one * 0.12f, new Color(0.3f, 0.05f, 0.25f), false);
                    }
                    node.remaining = rng.Range(2, 5);
                    break;
                }
                case ResourceKind.Herb:
                {
                    for (int i = 0; i < 4; i++)
                    {
                        var leaf = SurvivalPrimitives.Part(PrimitiveType.Cube, root.transform, new Vector3(rng.Range(-0.15f, 0.15f), 0.15f, rng.Range(-0.15f, 0.15f)), new Vector3(0.05f, 0.3f, 0.12f), new Color(0.35f, 0.62f, 0.3f), i == 0);
                        leaf.transform.localRotation = Quaternion.Euler(rng.Range(-25f, 25f), i * 45f, 0f);
                    }
                    node.remaining = rng.Range(1, 3);
                    break;
                }
                case ResourceKind.Mushroom:
                {
                    node.poisonous = rng.Chance(0.45f);
                    // Both look alike on purpose; only the Nature stat tells them apart
                    Color cap = node.poisonous ? new Color(0.62f, 0.42f, 0.3f) : new Color(0.6f, 0.44f, 0.3f);
                    SurvivalPrimitives.Part(PrimitiveType.Cylinder, root.transform, new Vector3(0f, 0.08f, 0f), new Vector3(0.06f, 0.08f, 0.06f), new Color(0.9f, 0.88f, 0.8f), false);
                    SurvivalPrimitives.Part(PrimitiveType.Sphere, root.transform, new Vector3(0f, 0.17f, 0f), new Vector3(0.2f, 0.09f, 0.2f), cap);
                    node.remaining = rng.Range(1, 4);
                    break;
                }
            }

            // Generous trigger so [E] picks it from a step away
            if (kind != ResourceKind.DeadTree)
            {
                var trigger = root.AddComponent<SphereCollider>();
                trigger.isTrigger = true;
                trigger.radius = 0.8f;
                trigger.center = Vector3.up * 0.3f;
            }
            return node;
        }
    }

    /// <summary>2.12: [H] with a fishing rod at water: wait, then press [H] again when it bites.</summary>
    public class FishingController : MonoBehaviour
    {
        public enum FishState
        {
            Idle,
            Waiting,
            Bite
        }

        public FishState State { get; private set; }
        public string StatusText { get; private set; }

        private float _timer;
        private WaterKind _kind;
        private Vector3 _startPos;
        private GameObject _bobber;

        private void Update()
        {
            var player = SurvivalDirector.Player;
            if (player == null || !player.activeInHierarchy)
            {
                if (State != FishState.Idle) Stop(null);
                return;
            }

            if (State == FishState.Idle)
            {
                if (!SKeys.Down(Key.H) || SurvivalUIState.AnyOpen) return;
                TryCast(player);
                return;
            }

            // Moving away cancels
            if ((player.transform.position - _startPos).sqrMagnitude > 1.5f * 1.5f)
            {
                Stop("Olta toplandı");
                return;
            }

            _timer -= Time.deltaTime;
            if (State == FishState.Waiting)
            {
                if (_bobber != null) _bobber.transform.position += Vector3.up * Mathf.Sin(Time.time * 2f) * 0.0015f;
                if (SKeys.Down(Key.H))
                {
                    Stop("Olta erken çekildi");
                    return;
                }
                if (_timer <= 0f)
                {
                    State = FishState.Bite;
                    _timer = SurvivalSettings.Instance.fishBiteWindow * (1f + 0.5f * CharacterStats.Frac(StatType.Nature));
                    StatusText = "VURDU! [H]";
                    if (_bobber != null) _bobber.transform.position += Vector3.down * 0.12f;
                }
            }
            else if (State == FishState.Bite)
            {
                if (SKeys.Down(Key.H))
                {
                    Catch();
                    return;
                }
                if (_timer <= 0f) Stop("Balık kaçtı");
            }
        }

        private void TryCast(GameObject player)
        {
            var s = SurvivalSettings.Instance;
            var rod = s.Item("Item_FishingRod");
            var inv = player.GetComponent<PlayerInventory>();
            if (rod == null || inv == null || inv.Storage.GetCount(rod) <= 0)
            {
                SurvivalNotify.Show("Balık tutmak için olta gerekli (Üretim: Olta)");
                return;
            }
            if (!WaterBodies.FindWaterInFront(player.transform.position, player.transform.forward, out Vector3 point, out _kind))
            {
                SurvivalNotify.Show("Önünde su yok (suya dön ve [H])");
                return;
            }
            _startPos = player.transform.position;
            State = FishState.Waiting;
            _timer = Random.Range(s.fishWaitMin, s.fishWaitMax);
            StatusText = "Balık bekleniyor... (vurunca [H])";
            _bobber = SurvivalPrimitives.Part(PrimitiveType.Sphere, null, point + Vector3.up * 0.05f, Vector3.one * 0.12f, new Color(0.9f, 0.2f, 0.15f), false);
            _bobber.name = "FishingBobber";
            _bobber.transform.position = point + Vector3.up * 0.05f;
        }

        private void Catch()
        {
            var s = SurvivalSettings.Instance;
            var fish = s.Item(_kind == WaterKind.Sea ? "Item_SeaFish" : "Item_RawFish");
            if (fish != null)
            {
                SurvivalDirector.GiveItem(fish, 1);
                CharacterStats.Instance?.Train(StatType.Nature, StatSettings.Instance.naturePerGather);
                PlayerVitals.Instance?.AddStressRelief(3f);
            }
            Stop(fish != null ? $"{fish.DisplayName} yakalandı!" : "Balık tanımı yok");
        }

        private void Stop(string message)
        {
            State = FishState.Idle;
            StatusText = null;
            if (_bobber != null) Destroy(_bobber);
            _bobber = null;
            if (message != null) SurvivalNotify.Show(message);
        }
    }
}
