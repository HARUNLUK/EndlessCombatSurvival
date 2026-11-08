using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using StarterAssets;
using EndlessSurvival.Inventory;
using EndlessSurvival.World;
using EndlessSurvival.Save;

namespace EndlessSurvival.Survival
{
    /// <summary>Keyboard helper (Input System).</summary>
    public static class SKeys
    {
        public static bool Down(Key key) => Keyboard.current != null && Keyboard.current[key].wasPressedThisFrame;
        public static bool Held(Key key) => Keyboard.current != null && Keyboard.current[key].isPressed;
        public static bool Up(Key key) => Keyboard.current != null && Keyboard.current[key].wasReleasedThisFrame;
    }

    /// <summary>Open state of the survival panels and the shared cursor handling.</summary>
    public static class SurvivalUIState
    {
        public static bool PanelOpen, TradeOpen, PauseOpen, MenuOpen;
        public static bool AnyOwnOpen => PanelOpen || TradeOpen || PauseOpen || MenuOpen;

        /// <summary>Panels made by other systems (inventory, campfire, notes...).</summary>
        public static bool OtherOpen
        {
            get
            {
                if (InventoryUI.IsOpen) return true;
                if (EndlessSurvival.Camping.CampfireUI.IsOpen) return true;
                if (EndlessSurvival.Vehicle.VehicleModificationUI.IsOpen) return true;
                if (GameOverUI.IsGameOver) return true;
                var note = EndlessSurvival.Story.NoteReaderUI.Instance;
                return note != null && note.IsOpen;
            }
        }

        public static bool AnyOpen => AnyOwnOpen || OtherOpen;

        private static int _cursorUsers;
        private static bool _wasLocked;

        public static void AcquireCursor()
        {
            if (_cursorUsers++ == 0)
            {
                _wasLocked = Cursor.lockState == CursorLockMode.Locked;
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
                SetLook(false);
            }
        }

        public static void ReleaseCursor()
        {
            if (_cursorUsers <= 0) return;
            if (--_cursorUsers == 0)
            {
                if (_wasLocked)
                {
                    Cursor.lockState = CursorLockMode.Locked;
                    Cursor.visible = false;
                }
                SetLook(true);
            }
        }

        internal static void ResetAll()
        {
            PanelOpen = TradeOpen = PauseOpen = MenuOpen = false;
            _cursorUsers = 0;
            _enforcedOpen = false;
        }

        private static bool _enforcedOpen;

        /// <summary>
        /// Runs every LateUpdate. Each panel locks/unlocks the cursor on its own, so when two panels overlap
        /// (or the window regains focus) the cursor could end up locked while a panel is still open and the
        /// UI stopped taking clicks. While any panel is open the cursor is kept free; when the last one closes
        /// it is locked again once.
        /// </summary>
        internal static void EnforceCursor()
        {
            bool open = AnyOpen;
            if (open)
            {
                if (Cursor.lockState != CursorLockMode.None) Cursor.lockState = CursorLockMode.None;
                if (!Cursor.visible) Cursor.visible = true;
                SetLook(false);
            }
            else if (_enforcedOpen)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
                SetLook(true);
            }
            _enforcedOpen = open;
            SyncStarterAssets(!open);
        }

        private static void SyncStarterAssets(bool locked)
        {
            // StarterAssetsInputs re-locks the cursor on window focus using this flag
            var player = SurvivalDirector.Player;
            var inputs = player != null ? player.GetComponent<StarterAssetsInputs>() : null;
            if (inputs != null) inputs.cursorLocked = locked;
        }

        private static void SetLook(bool on)
        {
            var player = SurvivalDirector.Player;
            var inputs = player != null ? player.GetComponent<StarterAssetsInputs>() : null;
            if (inputs == null) return;
            inputs.cursorInputForLook = on;
            if (!on) inputs.look = Vector2.zero;
        }
    }

    /// <summary>Total game hours since the run started (wrap-aware), used by spoilage, fatigue and anxiety.</summary>
    public static class GameClock
    {
        public static double TotalHours;
        /// <summary>Game hours passed this frame (including sleep jumps).</summary>
        public static float DeltaHours { get; private set; }
        /// <summary>Game hours passed this frame while awake (sleep jumps removed).</summary>
        public static float AwakeDeltaHours { get; private set; }

        private static float _lastHour = -1f;
        private static float _pendingSleepHours;

        internal static void Reset()
        {
            _lastHour = -1f;
            _pendingSleepHours = 0f;
            DeltaHours = 0f;
            AwakeDeltaHours = 0f;
        }

        internal static void NoteSleep(float hours) => _pendingSleepHours += Mathf.Max(0f, hours);

        internal static void Tick()
        {
            var cycle = DayNightCycle.Instance;
            if (cycle == null)
            {
                DeltaHours = AwakeDeltaHours = 0f;
                return;
            }

            float hour = cycle.CurrentHour;
            if (_lastHour < 0f)
            {
                _lastHour = hour;
                DeltaHours = AwakeDeltaHours = 0f;
                return;
            }

            float delta = hour - _lastHour;
            if (delta < 0f) delta += 24f; // the clock only moves forward (wrap past midnight); loads call Resync
            _lastHour = hour;

            DeltaHours = delta;
            float sleep = Mathf.Min(_pendingSleepHours, delta);
            _pendingSleepHours -= sleep;
            AwakeDeltaHours = delta - sleep;
            TotalHours += delta;
        }

        /// <summary>Called after a load: the hour jump of the load must not count as time passed.</summary>
        internal static void Resync()
        {
            _lastHour = DayNightCycle.Instance != null ? DayNightCycle.Instance.CurrentHour : -1f;
            _pendingSleepHours = 0f;
        }
    }

    /// <summary>Short on-screen messages ("Nişancılık 12 -> 13", warnings).</summary>
    public static class SurvivalNotify
    {
        public struct Entry
        {
            public string text;
            public float until;
            public Color color;
        }

        public static readonly List<Entry> Entries = new List<Entry>();

        public static void Show(string text, float seconds = 3.5f) => Show(text, Color.white, seconds);

        public static void Warn(string text, float seconds = 4f) => Show(text, new Color(1f, 0.75f, 0.35f), seconds);

        public static void Show(string text, Color color, float seconds)
        {
            if (string.IsNullOrEmpty(text)) return;
            // Same text twice in a row only refreshes the timer
            for (int i = 0; i < Entries.Count; i++)
            {
                if (Entries[i].text == text)
                {
                    var e = Entries[i];
                    e.until = Time.unscaledTime + seconds;
                    Entries[i] = e;
                    return;
                }
            }
            Entries.Add(new Entry { text = text, until = Time.unscaledTime + seconds, color = color });
            if (Entries.Count > 6) Entries.RemoveAt(0);
        }

        internal static void Prune()
        {
            float now = Time.unscaledTime;
            Entries.RemoveAll(e => e.until < now);
        }
    }

    /// <summary>A survival system that stores its state in the save file.</summary>
    public interface ISurvivalSave
    {
        string SaveKey { get; }
        string CaptureJson();
        void RestoreJson(string json);
    }

    /// <summary>
    /// Creates the survival systems at runtime (no scene edits): one "SurvivalSystems" object with the
    /// player-related systems (they keep running while the player object is disabled inside the vehicle)
    /// plus world systems (weather, spawners, menus, sounds).
    /// </summary>
    public static class SurvivalBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Init()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            Spawn();
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => Spawn();

        private static void Spawn()
        {
            if (SurvivalDirector.Instance != null) return;
            if (UnityEngine.Object.FindAnyObjectByType<ChunkManager>() == null) return; // only the game scene
            GameClock.Reset();
            SurvivalUIState.ResetAll();
            var root = new GameObject("SurvivalSystems");
            root.AddComponent<SurvivalDirector>();
        }
    }

    [DefaultExecutionOrder(-90)]
    public class SurvivalDirector : MonoBehaviour
    {
        public static SurvivalDirector Instance { get; private set; }
        public static GameObject Player { get; private set; }

        private readonly List<ISurvivalSave> _saveables = new List<ISurvivalSave>();
        private readonly Dictionary<string, string> _pendingLoad = new Dictionary<string, string>();
        private bool _playerReady;

        private void Awake()
        {
            Instance = this;
            SaveManager.CollectingSave += OnCollectingSave;
            SaveManager.ApplyingLoad += OnApplyingLoad;
            DayNightCycle.Slept += OnSlept;

            // World systems (do not need the player)
            gameObject.AddComponent<WeatherSystem>();
            gameObject.AddComponent<WorldPopulator>();
            gameObject.AddComponent<FoodSpoilage>();
            gameObject.AddComponent<GameMenus>();
            gameObject.AddComponent<SurvivalHUD>();
        }

        private void OnDestroy()
        {
            SaveManager.CollectingSave -= OnCollectingSave;
            SaveManager.ApplyingLoad -= OnApplyingLoad;
            DayNightCycle.Slept -= OnSlept;
            if (Instance == this)
            {
                Instance = null;
                Player = null;
            }
        }

        private static void OnSlept(float hours, bool inVehicle) => GameClock.NoteSleep(hours);

        private void Update()
        {
            GameClock.Tick();
            SurvivalNotify.Prune();

            if (!_playerReady) TrySetupPlayer();
        }

        private void LateUpdate() => SurvivalUIState.EnforceCursor();

        private void TrySetupPlayer()
        {
            var player = GameObject.FindGameObjectWithTag("Player");
            if (player == null) return;
            Player = player;
            _playerReady = true;

            // Old fade-out toilet is replaced by the PlayerVitals toilet action
            var reliever = player.GetComponent<EndlessSurvival.Camping.PlayerNeedReliever>();
            if (reliever != null) reliever.enabled = false;

            gameObject.AddComponent<CharacterStats>();
            gameObject.AddComponent<PlayerVitals>();
            gameObject.AddComponent<CraftingSystem>();
            gameObject.AddComponent<SurvivalPanel>();
            gameObject.AddComponent<FishingController>();
            gameObject.AddComponent<VehicleHome>();
            if (player.GetComponent<WaterFillInteractable>() == null) player.AddComponent<WaterFillInteractable>();
        }

        // ---------------- save bridge ----------------

        public void Register(ISurvivalSave saveable)
        {
            if (saveable == null || _saveables.Contains(saveable)) return;
            _saveables.Add(saveable);
            if (_pendingLoad.TryGetValue(saveable.SaveKey, out string json))
            {
                _pendingLoad.Remove(saveable.SaveKey);
                SafeRestore(saveable, json);
            }
        }

        public void Unregister(ISurvivalSave saveable) => _saveables.Remove(saveable);

        private void OnCollectingSave(SaveData data)
        {
            if (data.extensions == null) data.extensions = new List<SavedExtension>();
            data.extensions.RemoveAll(e => e != null && e.key != null && e.key.StartsWith("survival."));
            data.extensions.Add(new SavedExtension("survival.clock", GameClock.TotalHours.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            foreach (var s in _saveables)
            {
                try
                {
                    data.extensions.Add(new SavedExtension("survival." + s.SaveKey, s.CaptureJson()));
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[Survival] Save of {s.SaveKey} failed: {ex.Message}");
                }
            }
        }

        private void OnApplyingLoad(SaveData data)
        {
            if (data.extensions == null) return;
            foreach (var ext in data.extensions)
            {
                if (ext == null || string.IsNullOrEmpty(ext.key) || !ext.key.StartsWith("survival.")) continue;
                string key = ext.key.Substring("survival.".Length);
                if (key == "clock")
                {
                    double.TryParse(ext.json, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out GameClock.TotalHours);
                    continue;
                }
                ISurvivalSave target = _saveables.Find(s => s.SaveKey == key);
                if (target != null) SafeRestore(target, ext.json);
                else _pendingLoad[key] = ext.json;
            }
            GameClock.Resync();
        }

        private static void SafeRestore(ISurvivalSave s, string json)
        {
            try
            {
                s.RestoreJson(json);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Survival] Load of {s.SaveKey} failed: {ex.Message}");
            }
        }

        // ---------------- shared helpers ----------------

        public static PlayerInventory Inventory => Player != null ? Player.GetComponent<PlayerInventory>() : null;

        public static EndlessSurvival.Vehicle.VehicleController Vehicle
        {
            get
            {
                if (_vehicle == null) _vehicle = FindAnyObjectByType<EndlessSurvival.Vehicle.VehicleController>();
                return _vehicle;
            }
        }
        private static EndlessSurvival.Vehicle.VehicleController _vehicle;

        public static bool PlayerInVehicle
        {
            get
            {
                var v = Vehicle;
                return v != null && v.isDriven;
            }
        }

        /// <summary>World position of the player, or of the vehicle while driving.</summary>
        public static Vector3 PlayerPosition
        {
            get
            {
                if (PlayerInVehicle) return Vehicle.transform.position;
                return Player != null ? Player.transform.position : Vector3.zero;
            }
        }

        /// <summary>Active chunk whose forward span contains the position (null when none).</summary>
        public static Chunk ChunkAt(Vector3 position)
        {
            var manager = ChunkManager.Instance;
            if (manager == null) return null;
            var chunks = manager.ActiveChunks;
            for (int i = chunks.Count - 1; i >= 0; i--)
            {
                Chunk chunk = chunks[i];
                if (chunk == null) continue;
                float localZ = chunk.transform.InverseTransformPoint(position).z;
                if (localZ >= 0f && localZ < chunk.chunkLength) return chunk;
            }
            return null;
        }

        /// <summary>Counts an item in the backpack and (when close) the vehicle trunk.</summary>
        public static int CountItem(ItemDefinition item, bool includeVehicle = true)
        {
            if (item == null) return 0;
            int count = 0;
            var inv = Inventory;
            if (inv != null) count += inv.Storage.GetCount(item);
            if (includeVehicle)
            {
                var stash = NearVehicleStorage();
                if (stash != null) count += stash.Storage.GetCount(item);
            }
            return count;
        }

        /// <summary>Removes from the backpack first, then the vehicle trunk. Returns the number removed.</summary>
        public static int RemoveItem(ItemDefinition item, int amount, bool includeVehicle = true)
        {
            if (item == null || amount <= 0) return 0;
            int removed = 0;
            var inv = Inventory;
            if (inv != null) removed += inv.Storage.Remove(item, amount);
            if (removed < amount && includeVehicle)
            {
                var stash = NearVehicleStorage();
                if (stash != null) removed += stash.Storage.Remove(item, amount - removed);
            }
            return removed;
        }

        /// <summary>Adds to the backpack; whatever does not fit goes to the vehicle trunk or is dropped.</summary>
        public static void GiveItem(ItemDefinition item, int amount)
        {
            if (item == null || amount <= 0) return;
            var inv = Inventory;
            int added = inv != null ? inv.AddItem(item, amount) : 0;
            int rest = amount - added;
            if (rest <= 0) return;
            var stash = NearVehicleStorage();
            if (stash != null) rest -= stash.Storage.Add(item, rest);
            if (rest > 0)
            {
                var prefab = SurvivalSettings.Instance.pickupPrefab;
                if (prefab != null)
                    WorldPickup.Spawn(prefab, item, rest, PlayerPosition + Vector3.up * 0.6f + (Player != null ? Player.transform.forward : Vector3.forward), null);
                SurvivalNotify.Warn($"{item.DisplayName} x{rest} yere bırakıldı (yer yok)");
            }
        }

        public static VehicleStorage NearVehicleStorage()
        {
            var v = Vehicle;
            if (v == null) return null;
            var stash = v.GetComponent<VehicleStorage>();
            if (stash == null) return null;
            if (PlayerInVehicle) return stash;
            return Player != null && stash.IsInRange(Player.transform.position) ? stash : null;
        }

        /// <summary>True when an alerted enemy or a hostile animal is within the radius.</summary>
        public static bool DangerNearby(Vector3 position, float radius)
        {
            float r2 = radius * radius;
            foreach (var e in EndlessCombat.AI.EnemyController.Active)
            {
                if (e == null || e.IsDead || !e.IsAlerted) continue;
                if ((e.transform.position - position).sqrMagnitude < r2) return true;
            }
            foreach (var a in AnimalController.All)
            {
                if (a == null || a.IsDead || !a.IsHunting) continue;
                if ((a.transform.position - position).sqrMagnitude < r2) return true;
            }
            return false;
        }
    }
}
