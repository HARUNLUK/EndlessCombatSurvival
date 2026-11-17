using System.Collections.Generic;
using UnityEngine;
using EndlessSurvival.Survival;

namespace EndlessCombat.AI
{
    /// <summary>
    /// Run memory of the enemies (Enemy_AI_Deep_Plan.md 20.2, 20.3): six faction habits (0..1, one key "all" until
    /// factions exist), the difficulty tier and custom profile, the pursuit group and the trail. Written only when
    /// a witness gets away (19.3). Fades by 0.1 at every safe zone. Nothing is read or written in the test arena.
    /// </summary>
    public static class AIMemory
    {
        public enum HabitKind { VehicleEscape, LongRange, Shield, Stealth, Explosives, NightRaid }
        public const int HabitCount = 6;

        public static readonly string[] HabitNames = { "araçla kaçma", "uzaktan vurma", "kalkan", "gizlilik", "patlayıcı", "gece baskını" };
        private static readonly string[] HabitLines =
        {
            "Artık araçla kaçtığını biliyorlar.",
            "Artık uzaktan vurduğunu biliyorlar.",
            "Artık kalkanını biliyorlar.",
            "Artık sessizce geldiğini biliyorlar.",
            "Artık bomba kullandığını biliyorlar.",
            "Artık gece bastığını biliyorlar."
        };

        private static readonly Dictionary<string, float[]> Habits = new Dictionary<string, float[]>();
        public static float Gain = 0.34f, BrutalGain = 0.5f, SafeZoneFade = 0.1f;

        /// <summary>Player shield raised during fights (s), and the last player grenade (for the habit flags).</summary>
        public static float ShieldSeconds;
        public static float LastPlayerGrenade = -999f;

        private static bool TestScene => EndlessSurvival.Testing.TestArena.Active;

        private static float[] Of(string faction)
        {
            if (string.IsNullOrEmpty(faction)) faction = "all";
            if (!Habits.TryGetValue(faction, out var a))
            {
                a = new float[HabitCount];
                Habits[faction] = a;
            }
            return a;
        }

        /// <summary>How much the (only) faction expects this from the player; 0 when the lock is off.</summary>
        public static float Habit(HabitKind k, string faction = "all")
        {
            if (AIDifficulty.P.factionMemory <= 0f) return 0f;
            return Of(faction)[(int)k];
        }

        /// <summary>A witness got away from this squad: every habit seen in the fight grows (20.2).</summary>
        public static void RecordEncounter(EnemySquad s)
        {
            if (s == null || TestScene) return;
            var p = AIDifficulty.P;
            if (p.factionMemory <= 0f) return;
            float gain = (AIDifficulty.Current == AIDifficulty.Tier.Brutal ? BrutalGain : Gain) * Mathf.Max(0.5f, p.factionMemory * 0.5f + 0.5f);
            var a = Of("all");
            var cycle = EndlessSurvival.World.DayNightCycle.Instance;
            bool[] seen = new bool[HabitCount];
            seen[(int)HabitKind.VehicleEscape] = s.vehicleEscape > 0;
            seen[(int)HabitKind.LongRange] = s.longRange >= 2;
            seen[(int)HabitKind.Shield] = ShieldSeconds >= 15f;
            seen[(int)HabitKind.Stealth] = s.stealthFound > 0 || s.SilentKills > 0;
            seen[(int)HabitKind.Explosives] = Time.time - LastPlayerGrenade < 300f;
            seen[(int)HabitKind.NightRaid] = cycle != null && cycle.IsNight && s.PlayerStarted;
            for (int i = 0; i < HabitCount; i++)
            {
                if (!seen[i]) continue;
                float before = a[i];
                a[i] = Mathf.Clamp01(a[i] + gain);
                if (before < 0.5f && a[i] >= 0.5f) SurvivalNotify.Warn(HabitLines[i]);
                AIDebug.Log(s, $"alışkanlık: {HabitNames[i]} {a[i]:0.00}");
            }
            ShieldSeconds = 0f;
        }

        public static void OnSafeZone()
        {
            foreach (var a in Habits.Values)
                for (int i = 0; i < a.Length; i++) a[i] = Mathf.Max(0f, a[i] - SafeZoneFade);
        }

        public static void ResetAll()
        {
            Habits.Clear();
            ShieldSeconds = 0f;
        }

        public static string HabitsText()
        {
            var a = Of("all");
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < HabitCount; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(HabitNames[i]).Append(' ').Append(a[i].ToString("0.00"));
            }
            return sb.ToString();
        }

        // ------------------------------------------------------------------ save (20.3)

        [System.Serializable]
        private class HabitRow
        {
            public string faction;
            public float[] values;
        }

        [System.Serializable]
        private class Data
        {
            public int tier = (int)AIDifficulty.Tier.Normal;
            public AIDifficulty.Profile custom;
            public List<HabitRow> habits = new List<HabitRow>();
            public string groups;
        }

        public class Saver : ISurvivalSave
        {
            public string SaveKey => "ai";

            public string CaptureJson()
            {
                var d = new Data { tier = (int)AIDifficulty.Current };
                if (AIDifficulty.Current == AIDifficulty.Tier.Custom) d.custom = AIDifficulty.P.Clone();
                if (!TestScene)
                {
                    foreach (var kv in Habits) d.habits.Add(new HabitRow { faction = kv.Key, values = (float[])kv.Value.Clone() });
                    d.groups = AbstractGroups.CaptureJson();
                }
                return JsonUtility.ToJson(d);
            }

            public void RestoreJson(string json)
            {
                if (string.IsNullOrEmpty(json)) return;
                var d = JsonUtility.FromJson<Data>(json);
                if (d == null) return;
                if (d.tier == (int)AIDifficulty.Tier.Custom && d.custom != null) AIDifficulty.SetCustom(d.custom);
                else AIDifficulty.Set((AIDifficulty.Tier)Mathf.Clamp(d.tier, 0, 3));
                if (TestScene) return;
                Habits.Clear();
                if (d.habits != null)
                    foreach (var r in d.habits)
                        if (r != null && r.values != null && r.values.Length == HabitCount) Habits[r.faction ?? "all"] = r.values;
                AbstractGroups.RestoreJson(d.groups);
            }
        }

        public static readonly Saver Save = new Saver();
    }
}
