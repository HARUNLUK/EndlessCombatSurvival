namespace EndlessCombat.Combat
{
    /// <summary>
    /// How a held weapon is used and which upper body animation set the character plays.
    /// Unarmed means nothing in the hand (no weapon or the weapon is holstered): the character punches.
    /// </summary>
    public enum WeaponType
    {
        Unarmed = 0,
        Rifle = 1,
        Pistol = 2,
        Melee = 3
    }
}
