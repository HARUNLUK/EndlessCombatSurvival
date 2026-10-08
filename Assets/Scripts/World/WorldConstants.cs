namespace EndlessSurvival.World
{
    /// <summary>
    /// World-wide constants shared by terrain, coast, ocean and spawners.
    /// </summary>
    public static class WorldConstants
    {
        /// <summary>
        /// Sea surface height in chunk-local meters. Terrain cannot go below 0 (its origin),
        /// so 0 is the seabed and the water sits a little above it. Lowest road point (valley dip) is ~5m.
        /// </summary>
        public const float SeaLevel = 2.5f;
    }
}
