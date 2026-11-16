using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace EndlessSurvival.Save
{
    /// <summary>What a character keeps between journeys (dying only ends the journey).</summary>
    [Serializable]
    public class CharacterProfile
    {
        public string name;
        public string created;
        /// <summary>Base stat values in StatType order; empty until the first journey is saved.</summary>
        public float[] stats = new float[0];
        public List<string> readNoteIds = new List<string>();
        /// <summary>R4: plans the character has learned ("recipe:Recipe_X", "mod:Bullbar"); kept through death.</summary>
        public List<string> knownPlans = new List<string>();
        /// <summary>R5: preparation points earned on the road, spent on starting gear before a journey.</summary>
        public int prepPoints;
        public int journeys;
        public int deaths;
        public int arrivals;
        public float bestKm;
        public int bestScore;
        // The journey on the road right now (shown in the slot list; the journey itself is in run.json)
        public float runKm;
        public int runDay;
    }

    /// <summary>
    /// Character slots. Each slot is a folder with profile.json (kept for good: stats, read notes, records) and
    /// run.json (the journey on the road, written by SaveManager, removed when the character dies).
    /// While no slot is selected (automated tests close the menu without choosing one) nothing is kept: the
    /// journey goes to a separate test folder and there is no profile.
    /// </summary>
    public static class CharacterProfiles
    {
        public const int SlotCount = 4;
        private const string LegacySaveName = "survival_save.json";

        public static int Active { get; private set; } = -1;
        public static CharacterProfile Current { get; private set; }

        /// <summary>Raised when another character (or none) becomes the active one.</summary>
        public static event Action Changed;

        private static bool _migrated;

        private static string Root => Path.Combine(Application.persistentDataPath, "characters");
        private static string SlotDir(int slot) => Path.Combine(Root, slot < 0 ? "slot_test" : "slot_" + slot);
        private static string ProfilePath(int slot) => Path.Combine(SlotDir(slot), "profile.json");
        public static string RunPath(int slot) => Path.Combine(SlotDir(slot), "run.json");
        public static string ActiveRunPath => RunPath(Active);

        public static bool Exists(int slot) => slot >= 0 && File.Exists(ProfilePath(slot));
        public static bool HasRun(int slot) => File.Exists(RunPath(slot));

        /// <summary>Reads a slot without selecting it (null when empty or unreadable).</summary>
        public static CharacterProfile Peek(int slot)
        {
            MigrateLegacy();
            if (slot == Active && Current != null) return Current;
            if (!Exists(slot)) return null;
            try
            {
                return JsonUtility.FromJson<CharacterProfile>(File.ReadAllText(ProfilePath(slot)));
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Profiles] Slot {slot} could not be read: {ex.Message}");
                return null;
            }
        }

        public static CharacterProfile Create(int slot, string name)
        {
            if (slot < 0 || slot >= SlotCount) return null;
            var p = new CharacterProfile
            {
                name = string.IsNullOrWhiteSpace(name) ? "Yolcu " + (slot + 1) : name.Trim(),
                created = DateTime.Now.ToString("yyyy-MM-dd HH:mm")
            };
            Write(slot, p);
            return p;
        }

        public static bool Select(int slot)
        {
            var p = Peek(slot);
            if (p == null) return false;
            if (p.readNoteIds == null) p.readNoteIds = new List<string>();
            if (p.stats == null) p.stats = new float[0];
            if (p.knownPlans == null) p.knownPlans = new List<string>();
            Active = slot;
            Current = p;
            Changed?.Invoke();
            return true;
        }

        public static void Deselect()
        {
            if (Active < 0) return;
            Active = -1;
            Current = null;
            Changed?.Invoke();
        }

        /// <summary>Writes the active character's profile.</summary>
        public static void SaveCurrent()
        {
            if (Active >= 0 && Current != null) Write(Active, Current);
        }

        /// <summary>Removes the journey of a slot; the character stays.</summary>
        public static void DeleteRun(int slot)
        {
            if (EndlessSurvival.Testing.TestArena.Active) return; // dying in a test scene must not end the real journey
            try
            {
                if (File.Exists(RunPath(slot))) File.Delete(RunPath(slot));
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Profiles] Journey of slot {slot} could not be removed: {ex.Message}");
            }
            var p = Peek(slot);
            if (p != null && slot >= 0)
            {
                p.runKm = 0f;
                p.runDay = 0;
                Write(slot, p);
            }
        }

        /// <summary>Removes the character and its journey for good.</summary>
        public static void Delete(int slot)
        {
            if (slot < 0) return;
            if (slot == Active) Deselect();
            try
            {
                if (Directory.Exists(SlotDir(slot))) Directory.Delete(SlotDir(slot), true);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Profiles] Slot {slot} could not be deleted: {ex.Message}");
            }
        }

        private static void Write(int slot, CharacterProfile p)
        {
            if (EndlessSurvival.Testing.TestArena.Active) return; // test scenes never touch the character files
            try
            {
                Directory.CreateDirectory(SlotDir(slot));
                File.WriteAllText(ProfilePath(slot), JsonUtility.ToJson(p, true));
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Profiles] Slot {slot} could not be written: {ex.Message}");
            }
        }

        // ---------------- save from before the slots ----------------

        [Serializable]
        private class LegacyStats
        {
            public float[] values;
        }

        /// <summary>The single save file from before the slots becomes the first character, journey included.</summary>
        private static void MigrateLegacy()
        {
            if (_migrated) return;
            _migrated = true;
            try
            {
                string legacy = Path.Combine(Application.persistentDataPath, LegacySaveName);
                if (!File.Exists(legacy)) return;
                for (int i = 0; i < SlotCount; i++)
                    if (Exists(i)) return;

                var p = Create(0, "Yolcu");
                var data = JsonUtility.FromJson<SaveData>(File.ReadAllText(legacy));
                if (data != null)
                {
                    if (data.readNoteIds != null) p.readNoteIds = new List<string>(data.readNoteIds);
                    p.runKm = data.totalDistanceMeters / 1000f;
                    p.runDay = 1;
                    if (data.extensions != null)
                    {
                        foreach (var ext in data.extensions)
                        {
                            if (ext == null || ext.key != "survival.stats") continue;
                            var s = JsonUtility.FromJson<LegacyStats>(ext.json);
                            if (s?.values != null) p.stats = s.values;
                        }
                    }
                    p.journeys = 1;
                    Write(0, p);
                }
                File.Move(legacy, RunPath(0));
                Debug.Log("[Profiles] Old save moved into character slot 1.");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Profiles] Old save could not be moved: {ex.Message}");
            }
        }
    }
}
