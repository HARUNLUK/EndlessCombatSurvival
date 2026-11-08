using System.Collections.Generic;
using System.Text;
using UnityEngine;
using EndlessSurvival.Camping;
using EndlessSurvival.Inventory;
using EndlessSurvival.World;

namespace EndlessSurvival.Survival
{
    /// <summary>
    /// 2.1 craft logic: station detection (hand, burning campfire, vehicle, distiller), material checks
    /// (backpack + vehicle trunk when close), timed jobs and recipe effects (wound cleaning, repairs, placing
    /// the distiller / rabbit trap).
    /// </summary>
    public class CraftingSystem : MonoBehaviour
    {
        public static CraftingSystem Instance { get; private set; }

        public RecipeDefinition CurrentJob { get; private set; }
        public float JobProgress { get; private set; }
        private float _jobDuration;

        private void Awake() => Instance = this;

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public IReadOnlyList<RecipeDefinition> Recipes => SurvivalSettings.Instance.recipes;

        // ---------------- stations ----------------

        public static PlayerCampfire NearestBurningFire(Vector3 pos, float radius)
        {
            PlayerCampfire best = null;
            float bestD = radius * radius;
            foreach (var fire in FindObjectsByType<PlayerCampfire>())
            {
                if (fire == null || !fire.IsBurning) continue;
                float d = (fire.transform.position - pos).sqrMagnitude;
                if (d < bestD)
                {
                    bestD = d;
                    best = fire;
                }
            }
            return best;
        }

        public bool StationAvailable(CraftStation station)
        {
            Vector3 pos = SurvivalDirector.PlayerPosition;
            bool inVehicle = SurvivalDirector.PlayerInVehicle;
            switch (station)
            {
                case CraftStation.Hand:
                    return true;
                case CraftStation.Campfire:
                    return !inVehicle && NearestBurningFire(pos, 3.5f) != null;
                case CraftStation.Vehicle:
                    var v = SurvivalDirector.Vehicle;
                    return v != null && (inVehicle || Vector3.Distance(v.transform.position, pos) < 6f);
                case CraftStation.Distiller:
                    if (inVehicle) return false;
                    foreach (var d in Distiller.All)
                        if (d != null && d.IsHeated && Vector3.Distance(d.transform.position, pos) < 3.5f) return true;
                    return false;
            }
            return false;
        }

        /// <summary>Test mode: every recipe needs and uses no materials or tools.</summary>
        public static bool IsFree(RecipeDefinition r) => r != null && SurvivalTest.FreeResources;

        public bool CanCraft(RecipeDefinition r, out string reason)
        {
            reason = null;
            if (r == null) { reason = "Tarif yok"; return false; }
            if (CurrentJob != null) { reason = "Başka bir iş sürüyor"; return false; }
            if (!StationAvailable(r.station))
            {
                reason = r.station == CraftStation.Campfire ? "Yanan bir kamp ateşi yanında olmalısın"
                       : r.station == CraftStation.Vehicle ? "Aracın yanında olmalısın"
                       : r.station == CraftStation.Distiller ? "Ateşin üstündeki damıtıcının yanında olmalısın" : "İstasyon yok";
                return false;
            }
            bool free = IsFree(r);
            foreach (var t in r.tools)
            {
                if (!free && t != null && SurvivalDirector.CountItem(t) <= 0)
                {
                    reason = $"Gerekli alet: {t.DisplayName}";
                    return false;
                }
            }
            foreach (var i in r.inputs)
            {
                if (free || i?.item == null) continue;
                int have = SurvivalDirector.CountItem(i.item);
                if (have < i.amount)
                {
                    reason = $"Eksik: {i.item.DisplayName} ({have}/{i.amount})";
                    return false;
                }
            }
            if (free) goto NeedsChecked; // test mode: no "nothing to clean / repair" refusals
            if (r.effect == RecipeEffect.CleanWound && PlayerVitals.Instance != null && !PlayerVitals.Instance.WoundDirty && !PlayerVitals.Instance.IsInfected)
            {
                reason = "Temizlenecek yara yok";
                return false;
            }
            if (r.effect == RecipeEffect.RepairVehicle)
            {
                var v = SurvivalDirector.Vehicle;
                if (v == null || v.currentHealth >= v.maxHealth - 0.5f) { reason = "Araç hasarsız"; return false; }
            }
            if (r.effect == RecipeEffect.RepairClothing && PlayerVitals.Instance != null && PlayerVitals.Instance.WornAverageDurability > 95f)
            {
                reason = "Kıyafetler sağlam";
                return false;
            }
            NeedsChecked:
            if (r.effect == RecipeEffect.PlaceRabbitTrap)
            {
                if (SurvivalDirector.PlayerInVehicle) { reason = "Araçtan in"; return false; }
                if (NearestBurningFire(SurvivalDirector.PlayerPosition, 25f) == null) { reason = "Tuzak kamp kurduğun yere kurulur (yanan ateş yakında olmalı)"; return false; }
            }
            if (r.effect == RecipeEffect.PlaceDistiller)
            {
                var fire = NearestBurningFire(SurvivalDirector.PlayerPosition, 3.5f);
                if (fire != null)
                {
                    foreach (var d in Distiller.All)
                        if (d != null && Vector3.Distance(d.transform.position, fire.transform.position) < 4f) { reason = "Bu ateşte zaten damıtıcı var"; return false; }
                }
            }
            return true;
        }

        public string Requirements(RecipeDefinition r)
        {
            var sb = new StringBuilder();
            foreach (var i in r.inputs)
            {
                if (i?.item == null) continue;
                if (sb.Length > 0) sb.Append(", ");
                sb.Append(i.item.DisplayName).Append(" x").Append(i.amount);
            }
            foreach (var t in r.tools)
            {
                if (t == null) continue;
                if (sb.Length > 0) sb.Append(", ");
                sb.Append("[alet] ").Append(t.DisplayName);
            }
            if (IsFree(r)) sb.Append("  (TEST: bedava)");
            return sb.ToString();
        }

        public string Results(RecipeDefinition r)
        {
            var sb = new StringBuilder();
            foreach (var o in r.outputs)
            {
                if (o?.item == null) continue;
                if (sb.Length > 0) sb.Append(", ");
                sb.Append(o.item.DisplayName).Append(" x").Append(o.amount);
            }
            switch (r.effect)
            {
                case RecipeEffect.CleanWound: Append(sb, "yarayı temizler"); break;
                case RecipeEffect.RepairClothing: Append(sb, "giyilen kıyafetleri onarır"); break;
                case RecipeEffect.RepairVehicle: Append(sb, "araca +40 can"); break;
                case RecipeEffect.PlaceDistiller: Append(sb, "ateşe damıtıcı kurar"); break;
                case RecipeEffect.PlaceRabbitTrap: Append(sb, "tavşan tuzağı kurar"); break;
            }
            return sb.ToString();
        }

        private static void Append(StringBuilder sb, string text)
        {
            if (sb.Length > 0) sb.Append(", ");
            sb.Append(text);
        }

        public bool StartCraft(RecipeDefinition r)
        {
            if (!CanCraft(r, out string reason))
            {
                SurvivalNotify.Warn(reason);
                return false;
            }
            float speed = 1f - StatSettings.Instance.mechanicsCraftTimeReduction * CharacterStats.Frac(StatType.Mechanics);
            _jobDuration = Mathf.Max(0f, r.craftSeconds * speed);
            CurrentJob = r;
            JobProgress = 0f;
            if (_jobDuration <= 0.05f) Complete();
            return true;
        }

        public void CancelJob()
        {
            if (CurrentJob != null) SurvivalNotify.Warn($"{CurrentJob.DisplayName} yarıda kaldı");
            CurrentJob = null;
            JobProgress = 0f;
        }

        private void Update()
        {
            if (CurrentJob == null) return;
            if (!StationAvailable(CurrentJob.station))
            {
                CancelJob();
                return;
            }
            JobProgress += Time.deltaTime / Mathf.Max(0.05f, _jobDuration);
            if (JobProgress >= 1f) Complete();
        }

        private void Complete()
        {
            var r = CurrentJob;
            CurrentJob = null;
            JobProgress = 0f;
            if (r == null) return;

            bool free = IsFree(r);
            // Check again: something may have been dropped while waiting
            foreach (var i in r.inputs)
            {
                if (!free && i?.item != null && SurvivalDirector.CountItem(i.item) < i.amount)
                {
                    SurvivalNotify.Warn($"Malzeme eksik: {i.item.DisplayName}");
                    return;
                }
            }

            float saveChance = r.effect == RecipeEffect.None && r.trainsStat == StatType.Mechanics
                ? StatSettings.Instance.mechanicsSaveMaterialChance * CharacterStats.Frac(StatType.Mechanics) : 0f;
            bool saved = false;
            foreach (var i in r.inputs)
            {
                if (free || i?.item == null) continue;
                int amount = i.amount;
                if (!saved && amount > 1 && Random.value < saveChance)
                {
                    amount -= 1;
                    saved = true;
                }
                SurvivalDirector.RemoveItem(i.item, amount);
            }

            foreach (var o in r.outputs)
            {
                if (o?.item == null) continue;
                int amount = o.amount;
                if (r.natureBonusOutput && o.item.IsConsumable &&
                    Random.value < StatSettings.Instance.natureExtraYieldChance * CharacterStats.Frac(StatType.Nature) * 0.5f)
                    amount += 1;
                SurvivalDirector.GiveItem(o.item, amount);
            }

            ApplyEffect(r.effect);

            var stats = CharacterStats.Instance;
            if (stats != null)
            {
                var set = StatSettings.Instance;
                float gain = r.trainsStat == StatType.Nature ? set.naturePerGather : set.mechanicsPerCraft;
                stats.Train(r.trainsStat, gain);
            }

            SurvivalNotify.Show($"Yapıldı: {r.DisplayName}{(saved ? " (malzeme artırdın)" : "")}", new Color(0.7f, 1f, 0.7f), 3f);
        }

        private void ApplyEffect(RecipeEffect effect)
        {
            var vitals = PlayerVitals.Instance;
            switch (effect)
            {
                case RecipeEffect.CleanWound:
                    if (vitals != null) vitals.CleanWound();
                    break;
                case RecipeEffect.RepairClothing:
                    if (vitals != null) vitals.RepairClothing();
                    break;
                case RecipeEffect.RepairVehicle:
                    var v = SurvivalDirector.Vehicle;
                    if (v != null) v.currentHealth = Mathf.Min(v.maxHealth, v.currentHealth + 40f);
                    break;
                case RecipeEffect.PlaceDistiller:
                    var fire = NearestBurningFire(SurvivalDirector.PlayerPosition, 3.5f);
                    if (fire != null) Distiller.Create(fire.transform.position + fire.transform.right * 1.1f, fire.transform.parent);
                    break;
                case RecipeEffect.PlaceRabbitTrap:
                    var player = SurvivalDirector.Player;
                    if (player != null)
                    {
                        Vector3 p = player.transform.position + player.transform.forward * 1.6f;
                        var chunk = SurvivalDirector.ChunkAt(p);
                        RabbitTrap.Create(p, chunk != null ? chunk.transform : null, chunk != null ? chunk.ChunkSeed : 0);
                    }
                    break;
            }
        }
    }

    /// <summary>2.10: distiller placed on a campfire. Works only while that fire burns.</summary>
    public class Distiller : MonoBehaviour
    {
        public static readonly List<Distiller> All = new List<Distiller>();

        private PlayerCampfire _fire;
        private float _scan;

        public bool IsHeated
        {
            get
            {
                if (_fire == null || Time.time > _scan)
                {
                    _scan = Time.time + 1f;
                    _fire = CraftingSystem.NearestBurningFire(transform.position, 4f);
                }
                return _fire != null && _fire.IsBurning;
            }
        }

        private void OnEnable() => All.Add(this);
        private void OnDisable() => All.Remove(this);

        public static Distiller Create(Vector3 position, Transform parent)
        {
            var root = new GameObject("Distiller");
            root.transform.SetParent(parent, true);
            root.transform.position = SurvivalPrimitives.Ground(position);

            var pot = SurvivalPrimitives.Part(PrimitiveType.Cylinder, root.transform, new Vector3(0f, 0.3f, 0f), new Vector3(0.45f, 0.3f, 0.45f), new Color(0.45f, 0.45f, 0.48f));
            SurvivalPrimitives.Part(PrimitiveType.Cylinder, root.transform, new Vector3(0f, 0.65f, 0f), new Vector3(0.25f, 0.08f, 0.25f), new Color(0.5f, 0.5f, 0.52f));
            var pipe = SurvivalPrimitives.Part(PrimitiveType.Cylinder, root.transform, new Vector3(0.35f, 0.7f, 0f), new Vector3(0.05f, 0.35f, 0.05f), new Color(0.7f, 0.45f, 0.25f));
            pipe.transform.localRotation = Quaternion.Euler(0f, 0f, 60f);
            SurvivalPrimitives.Part(PrimitiveType.Cylinder, root.transform, new Vector3(0.7f, 0.15f, 0f), new Vector3(0.18f, 0.15f, 0.18f), new Color(0.35f, 0.5f, 0.6f));
            pot.name = "Pot";
            return root.AddComponent<Distiller>();
        }
    }

    /// <summary>2.14: rabbit trap set at the camp. A seeded catch roll happens when the player sleeps.</summary>
    public class RabbitTrap : MonoBehaviour, IInteractable
    {
        private int _seed;
        private bool _caught;
        private bool _rolled;
        private GameObject _catchVisual;

        public Vector3 InteractPosition => transform.position;
        public bool CanInteract => true;

        public static RabbitTrap Create(Vector3 position, Transform parent, int chunkSeed)
        {
            var root = new GameObject("RabbitTrap");
            root.transform.SetParent(parent, true);
            root.transform.position = SurvivalPrimitives.Ground(position);
            Color wood = new Color(0.45f, 0.32f, 0.2f);
            SurvivalPrimitives.Part(PrimitiveType.Cube, root.transform, new Vector3(-0.2f, 0.15f, 0f), new Vector3(0.05f, 0.3f, 0.05f), wood);
            SurvivalPrimitives.Part(PrimitiveType.Cube, root.transform, new Vector3(0.2f, 0.15f, 0f), new Vector3(0.05f, 0.3f, 0.05f), wood);
            SurvivalPrimitives.Part(PrimitiveType.Cube, root.transform, new Vector3(0f, 0.3f, 0f), new Vector3(0.5f, 0.04f, 0.04f), wood);
            var col = root.AddComponent<BoxCollider>();
            col.isTrigger = true;
            col.center = new Vector3(0f, 0.3f, 0f);
            col.size = new Vector3(0.8f, 0.6f, 0.8f);
            var trap = root.AddComponent<RabbitTrap>();
            Vector3 p = root.transform.position;
            trap._seed = SeededRandom.Combine(chunkSeed, Mathf.RoundToInt(p.x * 10f) ^ (Mathf.RoundToInt(p.z * 10f) << 8));
            return trap;
        }

        private void OnEnable() => DayNightCycle.Slept += OnSlept;
        private void OnDisable() => DayNightCycle.Slept -= OnSlept;

        private void OnSlept(float hours, bool inVehicle)
        {
            if (_caught || _rolled || hours < 2f) return;
            _rolled = true;
            int night = Mathf.FloorToInt((float)(GameClock.TotalHours / 24.0));
            var rng = new SeededRandom(SeededRandom.Combine(_seed, night));
            if (rng.Chance(SurvivalSettings.Instance.trapCatchChance))
            {
                _caught = true;
                _catchVisual = SurvivalPrimitives.Part(PrimitiveType.Sphere, transform, new Vector3(0f, 0.12f, 0f), new Vector3(0.3f, 0.2f, 0.4f), new Color(0.6f, 0.5f, 0.4f));
                Destroy(_catchVisual.GetComponent<Collider>());
            }
        }

        public string GetPrompt() => _caught ? "Tavşanı al" : (_rolled ? "Tuzak boş, sök" : "Tuzak kurulu (uyuyunca bakarsın) - sök");

        public string Interact(PlayerInventory user)
        {
            var s = SurvivalSettings.Instance;
            if (_caught)
            {
                SurvivalDirector.GiveItem(s.Item("Item_RawMeat"), 2);
                SurvivalDirector.GiveItem(s.Item("Item_RawHide"), 1);
                CharacterStats.Instance?.Train(StatType.Nature, StatSettings.Instance.naturePerGather);
                _caught = false;
                _rolled = false;
                if (_catchVisual != null) Destroy(_catchVisual);
                return "Tavşan yakalandı: Çiğ Et x2, Ham Deri";
            }
            SurvivalDirector.GiveItem(s.Item("Item_Firewood"), 1);
            Destroy(gameObject);
            return "Tuzak söküldü";
        }
    }

    /// <summary>Primitive placeholder visuals shared by the survival objects.</summary>
    public static class SurvivalPrimitives
    {
        private static readonly Dictionary<Color, Material> Materials = new Dictionary<Color, Material>();

        public static Material MaterialFor(Color color)
        {
            if (Materials.TryGetValue(color, out var m) && m != null) return m;
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            m = new Material(shader) { color = color };
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
            Materials[color] = m;
            return m;
        }

        public static GameObject Part(PrimitiveType type, Transform parent, Vector3 localPos, Vector3 scale, Color color, bool keepCollider = true)
        {
            var go = GameObject.CreatePrimitive(type);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = MaterialFor(color);
            if (!keepCollider) Object.Destroy(go.GetComponent<Collider>());
            return go;
        }

        /// <summary>Snaps a point to the ground below it (terrain/colliders, triggers ignored).</summary>
        public static Vector3 Ground(Vector3 p)
        {
            if (Physics.Raycast(p + Vector3.up * 3f, Vector3.down, out RaycastHit hit, 20f, ~0, QueryTriggerInteraction.Ignore))
            {
                // Do not land on the player
                if (hit.collider.CompareTag("Player") && Physics.Raycast(hit.point + Vector3.down * 0.05f, Vector3.down, out RaycastHit below, 20f, ~0, QueryTriggerInteraction.Ignore))
                    return below.point;
                return hit.point;
            }
            return p;
        }
    }
}
