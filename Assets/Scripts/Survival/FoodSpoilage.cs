using System;
using System.Collections.Generic;
using UnityEngine;
using EndlessSurvival.Inventory;
using EndlessSurvival.Vehicle;

namespace EndlessSurvival.Survival
{
    /// <summary>
    /// 2.11: perishable items age in the backpack and the vehicle trunk (FIFO batches per item).
    /// A spoiled batch turns into its spoilsInto item (rotten food). Moving food between storages keeps its age.
    /// 2.19: with the drying line mod, raw meat in the trunk dries while driving.
    /// </summary>
    public class FoodSpoilage : MonoBehaviour, ISurvivalSave
    {
        public static FoodSpoilage Instance { get; private set; }

        [Serializable]
        private class Batch
        {
            public string item;
            public float age;
            public int count;
        }

        private class Tracked
        {
            public string key;
            public ItemStorage storage;
            public float spoilMultiplier = 1f;
            public readonly Dictionary<ItemDefinition, List<Batch>> batches = new Dictionary<ItemDefinition, List<Batch>>();
        }

        private readonly List<Tracked> _tracked = new List<Tracked>();
        // Removed batches wait here briefly so a move to the other storage keeps the age
        private readonly List<(ItemDefinition item, Batch batch, float until)> _transit = new List<(ItemDefinition, Batch, float)>();
        private List<Batch> _pendingPlayer, _pendingVehicle;
        private float _dryProgressHours;
        private float _tick;

        public string SaveKey => "spoilage";

        private void Awake() => Instance = this;

        private void OnEnable() => SurvivalDirector.Instance?.Register(this);

        private void OnDisable()
        {
            SurvivalDirector.Instance?.Unregister(this);
            if (Instance == this) Instance = null;
        }

        private void Update()
        {
            EnsureTracked();
            _tick += Time.deltaTime;
            if (_tick < 0.5f && GameClock.DeltaHours < 0.5f) return;
            _tick = 0f;

            _transit.RemoveAll(t => t.until < Time.unscaledTime);
            float hours = _accumHours + GameClock.DeltaHours;
            _accumHours = 0f;
            foreach (var t in _tracked) Process(t, hours);
            UpdateDryingLine(hours);
        }

        private float _accumHours;

        private void LateUpdate()
        {
            // Hours of frames skipped by the half-second tick are kept for the next tick
            if (_tick > 0f) _accumHours += GameClock.DeltaHours;
        }

        private void EnsureTracked()
        {
            var inv = SurvivalDirector.Inventory;
            if (inv != null && _tracked.Find(t => t.key == "player") == null)
            {
                var t = new Tracked { key = "player", storage = inv.Storage };
                _tracked.Add(t);
                if (_pendingPlayer != null) Apply(t, _pendingPlayer);
                _pendingPlayer = null;
            }
            var v = SurvivalDirector.Vehicle;
            var stash = v != null ? v.GetComponent<VehicleStorage>() : null;
            if (stash != null && _tracked.Find(t => t.key == "vehicle") == null)
            {
                var t = new Tracked { key = "vehicle", storage = stash.Storage, spoilMultiplier = SurvivalSettings.Instance.vehicleSpoilMultiplier };
                _tracked.Add(t);
                if (_pendingVehicle != null) Apply(t, _pendingVehicle);
                _pendingVehicle = null;
            }
        }

        private void Process(Tracked t, float hours)
        {
            // 1. sync with the storage contents
            foreach (var pair in t.storage.Items)
            {
                var item = pair.Key;
                if (item == null || !item.IsPerishable) continue;
                if (!t.batches.TryGetValue(item, out var list))
                {
                    list = new List<Batch>();
                    t.batches[item] = list;
                }
            }
            var spoiled = new List<(ItemDefinition, int)>();
            foreach (var pair in t.batches)
            {
                var item = pair.Key;
                var list = pair.Value;
                int have = t.storage.GetCount(item);
                int tracked = 0;
                foreach (var b in list) tracked += b.count;

                if (have > tracked) AddBatches(item, list, have - tracked);
                else if (have < tracked) RemoveOldest(item, list, tracked - have);

                // 2. age
                foreach (var b in list) b.age += hours * t.spoilMultiplier;
                for (int i = list.Count - 1; i >= 0; i--)
                {
                    if (list[i].age >= item.spoilHours)
                    {
                        spoiled.Add((item, list[i].count));
                        list.RemoveAt(i);
                    }
                }
            }

            // 3. replace spoiled food
            foreach (var (item, count) in spoiled)
            {
                int removed = t.storage.Remove(item, count);
                if (removed <= 0) continue;
                if (item.spoilsInto != null) t.storage.Add(item.spoilsInto, removed);
                SurvivalNotify.Warn($"{item.DisplayName} x{removed} bozuldu{(t.key == "vehicle" ? " (bagajda)" : "")}");
            }
        }

        private void AddBatches(ItemDefinition item, List<Batch> list, int amount)
        {
            // Prefer batches that just left another storage
            for (int i = 0; i < _transit.Count && amount > 0; i++)
            {
                var tr = _transit[i];
                if (tr.item != item) continue;
                int take = Mathf.Min(amount, tr.batch.count);
                list.Add(new Batch { item = item.name, age = tr.batch.age, count = take });
                tr.batch.count -= take;
                amount -= take;
                if (tr.batch.count <= 0)
                {
                    _transit.RemoveAt(i);
                    i--;
                }
            }
            if (amount > 0) list.Add(new Batch { item = item.name, age = 0f, count = amount });
            list.Sort((a, b) => b.age.CompareTo(a.age)); // oldest first
        }

        private void RemoveOldest(ItemDefinition item, List<Batch> list, int amount)
        {
            while (amount > 0 && list.Count > 0)
            {
                var b = list[0];
                int take = Mathf.Min(amount, b.count);
                b.count -= take;
                amount -= take;
                _transit.Add((item, new Batch { item = b.item, age = b.age, count = take }, Time.unscaledTime + 3f));
                if (b.count <= 0) list.RemoveAt(0);
            }
        }

        /// <summary>Hours until the oldest unit of the item spoils in the backpack (for the UI), -1 if none.</summary>
        public float HoursLeft(ItemDefinition item)
        {
            if (item == null || !item.IsPerishable) return -1f;
            var t = _tracked.Find(x => x.key == "player");
            if (t == null || !t.batches.TryGetValue(item, out var list) || list.Count == 0) return item.spoilHours;
            return Mathf.Max(0f, item.spoilHours - list[0].age);
        }

        private void UpdateDryingLine(float hours)
        {
            var mods = VehicleModifications.Instance;
            var v = SurvivalDirector.Vehicle;
            if (mods == null || v == null || !mods.IsInstalled(VehicleModType.DryingLine)) return;
            if (v.CurrentSpeedKmh < 15f) return;
            var stash = v.GetComponent<VehicleStorage>();
            if (stash == null) return;

            var s = SurvivalSettings.Instance;
            var raw = s.Item("Item_RawMeat");
            var dried = s.Item("Item_DriedMeat");
            if (raw == null || dried == null || stash.Storage.GetCount(raw) <= 0)
            {
                _dryProgressHours = 0f;
                return;
            }
            _dryProgressHours += hours;
            if (_dryProgressHours < s.dryingLineHours) return;
            _dryProgressHours = 0f;
            if (stash.Storage.Remove(raw, 1) > 0)
            {
                stash.Storage.Add(dried, 1);
                SurvivalNotify.Show("Kurutma ipinde bir et kurudu (bagaj)");
            }
        }

        // ---------------- save ----------------

        [Serializable]
        private class Data
        {
            public List<Batch> player = new List<Batch>();
            public List<Batch> vehicle = new List<Batch>();
        }

        public string CaptureJson()
        {
            var d = new Data();
            foreach (var t in _tracked)
            {
                var target = t.key == "player" ? d.player : d.vehicle;
                foreach (var list in t.batches.Values) target.AddRange(list);
            }
            return JsonUtility.ToJson(d);
        }

        public void RestoreJson(string json)
        {
            var d = JsonUtility.FromJson<Data>(json);
            if (d == null) return;
            _pendingPlayer = d.player;
            _pendingVehicle = d.vehicle;
            foreach (var t in _tracked)
            {
                t.batches.Clear();
                if (t.key == "player" && _pendingPlayer != null) { Apply(t, _pendingPlayer); _pendingPlayer = null; }
                if (t.key == "vehicle" && _pendingVehicle != null) { Apply(t, _pendingVehicle); _pendingVehicle = null; }
            }
        }

        private static void Apply(Tracked t, List<Batch> batches)
        {
            var s = SurvivalSettings.Instance;
            foreach (var b in batches)
            {
                var item = s.Item(b.item);
                if (item == null || b.count <= 0) continue;
                if (!t.batches.TryGetValue(item, out var list))
                {
                    list = new List<Batch>();
                    t.batches[item] = list;
                }
                list.Add(new Batch { item = b.item, age = b.age, count = b.count });
            }
            foreach (var list in t.batches.Values) list.Sort((a, b) => b.age.CompareTo(a.age));
        }
    }
}
