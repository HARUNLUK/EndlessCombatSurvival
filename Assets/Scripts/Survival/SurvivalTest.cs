namespace EndlessSurvival.Survival
{
    /// <summary>
    /// Test switches while the game is being built. FreeResources: nothing needs materials, tools, money or a need
    /// (crafting, campfire actions, placing a fire, filling bottles, fishing, vehicle parts and mods, trader, toilet
    /// without a full bladder, cleaning a wound / repairing clothes or the vehicle when nothing is wrong) and nothing
    /// is used up; the UI still shows what it would cost. Danger checks and stations (fire, vehicle nearby) still apply.
    /// Turn off for real play (Tab > Araç and Tab > Üretim have a toggle; it is remembered between sessions).
    /// </summary>
    public static class SurvivalTest
    {
        private const string Key = "SurvivalTest_FreeResources";
        private static bool? _free;

        public static bool FreeResources
        {
            get { if (!_free.HasValue) _free = UnityEngine.PlayerPrefs.GetInt(Key, 1) == 1; return _free.Value; }
            set { _free = value; UnityEngine.PlayerPrefs.SetInt(Key, value ? 1 : 0); }
        }
    }
}
