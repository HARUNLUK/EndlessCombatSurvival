namespace EndlessSurvival.Survival
{
    /// <summary>Central tuning values for hunger and thirst (the scene's serialized drain values are ignored).</summary>
    public static class NeedsTuning
    {
        /// <summary>Real minutes from a full thirst bar to empty (walking, not sprinting).</summary>
        public static float ThirstMinutesToEmpty = 45f;

        /// <summary>Real minutes from a full hunger bar to empty (walking, not sprinting).</summary>
        public static float HungerMinutesToEmpty = 63f;

        /// <summary>Health lost per second for each empty need (ignores armor).</summary>
        public static float StarvationDamagePerSecond = 0.5f;

        /// <summary>Below this ratio (0-1) stamina regenerates slower.</summary>
        public static float LowNeedThreshold = 0.30f;

        /// <summary>Below this ratio (0-1) the screen blurs and the aim sways.</summary>
        public static float CriticalNeedThreshold = 0.10f;

        /// <summary>Extra aim sway in degrees while hunger or thirst is critical.</summary>
        public static float CriticalAimSwayDegrees = 2.5f;
    }
}
