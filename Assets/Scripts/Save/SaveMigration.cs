using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace EndlessSurvival.Save
{
    /// <summary>
    /// Version numbers and upgrade steps for the journey file (run.json) and the character file (profile.json),
    /// plus safe writing (temp file + one backup) and reading that falls back to the backup when the file is broken.
    ///
    /// Adding a field to SaveData that old files do not have needs nothing: JsonUtility keeps the field's initial
    /// value. A step is needed only when the meaning of stored data changes (a field is renamed, an item is
    /// renamed, a unit changes). To add one: raise RunVersion (or ProfileVersion) by one and add a case to
    /// MigrateRun (or MigrateProfile) that turns version N-1 into N.
    /// </summary>
    public static class SaveMigration
    {
        /// <summary>Version written into every new run.json. Files from before versioning read as 0.</summary>
        public const int RunVersion = 1;
        /// <summary>Version written into every new profile.json. Files from before versioning read as 0.</summary>
        public const int ProfileVersion = 1;

        private const string BackupExt = ".bak";
        private const string TempExt = ".tmp";

        // ---------------- upgrade steps ----------------

        /// <summary>Brings a journey read from disk up to RunVersion. Returns false when the file is newer than the game.</summary>
        public static bool MigrateRun(SaveData d)
        {
            if (d == null) return false;
            if (d.saveVersion > RunVersion)
            {
                Debug.LogWarning($"[SaveMigration] run.json version {d.saveVersion} is newer than this game ({RunVersion}); loading what is known.");
                return false;
            }
            while (d.saveVersion < RunVersion)
            {
                switch (d.saveVersion)
                {
                    case 0:
                        // Files from before versioning: lists that were never written come back null
                        if (d.playerInventory == null) d.playerInventory = new List<SavedItemSlot>();
                        if (d.playerEquipment == null) d.playerEquipment = new List<SavedEquipSlot>();
                        if (d.vehicleStorage == null) d.vehicleStorage = new List<SavedItemSlot>();
                        if (d.installedVehicleMods == null) d.installedVehicleMods = new List<string>();
                        if (d.readNoteIds == null) d.readNoteIds = new List<string>();
                        if (d.placedCampfires == null) d.placedCampfires = new List<SavedCampfire>();
                        if (d.extensions == null) d.extensions = new List<SavedExtension>();
                        break;
                }
                d.saveVersion++;
                Debug.Log($"[SaveMigration] Journey save upgraded to version {d.saveVersion}.");
            }
            return true;
        }

        /// <summary>Brings a character profile read from disk up to ProfileVersion.</summary>
        public static bool MigrateProfile(CharacterProfile p)
        {
            if (p == null) return false;
            if (p.profileVersion > ProfileVersion)
            {
                Debug.LogWarning($"[SaveMigration] profile.json version {p.profileVersion} is newer than this game ({ProfileVersion}).");
                return false;
            }
            while (p.profileVersion < ProfileVersion)
            {
                switch (p.profileVersion)
                {
                    case 0:
                        if (p.stats == null) p.stats = new float[0];
                        if (p.readNoteIds == null) p.readNoteIds = new List<string>();
                        if (p.knownPlans == null) p.knownPlans = new List<string>();
                        break;
                }
                p.profileVersion++;
            }
            return true;
        }

        // ---------------- safe file access ----------------

        /// <summary>
        /// Writes through a temp file so a crash mid-write never leaves half a file. The previous file is kept as
        /// path.bak (one generation).
        /// </summary>
        public static void WriteSafe(string path, string text)
        {
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            string tmp = path + TempExt;
            string bak = path + BackupExt;
            File.WriteAllText(tmp, text);
            if (File.Exists(path))
            {
                if (File.Exists(bak)) File.Delete(bak);
                File.Move(path, bak);
            }
            File.Move(tmp, path);
        }

        /// <summary>
        /// Reads and parses a JSON file; when it is missing, empty or broken the backup is tried. Null when neither
        /// can be read. usedBackup tells the caller to warn the player.
        /// </summary>
        public static T ReadSafe<T>(string path, out bool usedBackup) where T : class
        {
            usedBackup = false;
            T result = TryParse<T>(path);
            if (result != null) return result;
            result = TryParse<T>(path + BackupExt);
            if (result != null)
            {
                usedBackup = true;
                Debug.LogWarning($"[SaveMigration] {Path.GetFileName(path)} could not be read; the backup was used.");
            }
            return result;
        }

        public static T ReadSafe<T>(string path) where T : class => ReadSafe<T>(path, out _);

        /// <summary>Removes a save file together with its backup and any left-over temp file.</summary>
        public static void DeleteWithBackup(string path)
        {
            if (File.Exists(path)) File.Delete(path);
            if (File.Exists(path + BackupExt)) File.Delete(path + BackupExt);
            if (File.Exists(path + TempExt)) File.Delete(path + TempExt);
        }

        /// <summary>True when the file or its backup exists.</summary>
        public static bool ExistsWithBackup(string path) => File.Exists(path) || File.Exists(path + BackupExt);

        private static T TryParse<T>(string path) where T : class
        {
            try
            {
                if (!File.Exists(path)) return null;
                string json = File.ReadAllText(path);
                if (string.IsNullOrWhiteSpace(json)) return null;
                return JsonUtility.FromJson<T>(json);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[SaveMigration] {Path.GetFileName(path)}: {ex.Message}");
                return null;
            }
        }
    }
}
