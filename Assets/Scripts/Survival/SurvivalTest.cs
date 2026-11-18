namespace EndlessSurvival.Survival
{
    /// <summary>
    /// Test switches while the game is being built. FreeResources: nothing needs materials, tools, money or a need
    /// (crafting, campfire actions, placing a fire, filling bottles, fishing, vehicle parts and mods, trader, toilet
    /// without a full bladder, cleaning a wound / repairing clothes or the vehicle when nothing is wrong) and nothing
    /// is used up; the UI still shows what it would cost. Danger checks and stations (fire, vehicle nearby) still apply.
    /// Since 2026-11-15 it also puts every item of the game in the backpack and keeps it topped up (SurvivalTestKit),
    /// keeps 999 rounds per ammo kind and 99999 money, and lifts the carry limit (PlayerVitals.UpdateCarry).
    /// OFF by default since 2026-11-17 (user: normal play starts without it). The test scenes (TestArena) still turn
    /// it on for their session. Tab > Araç and Tab > Üretim have a toggle; it is remembered between sessions.
    /// </summary>
    public static class SurvivalTest
    {
        // "_v4": the default became OFF on 2026-11-17; the new key makes that apply once on machines where "_v3" had
        // remembered ON. (v3: default ON on 2026-11-15. v2: default OFF on 2026-11-13.)
        private const string Key = "SurvivalTest_FreeResources_v4";
        private static bool? _free;

        public static bool FreeResources
        {
            get { if (!_free.HasValue) _free = UnityEngine.PlayerPrefs.GetInt(Key, 0) == 1; return _free.Value; }
            set { _free = value; UnityEngine.PlayerPrefs.SetInt(Key, value ? 1 : 0); }
        }

        /// <summary>On or off for this run only, nothing is remembered (the automatic play test runs with it on).</summary>
        public static void ForceForSession(bool free) => _free = free;
    }
}
