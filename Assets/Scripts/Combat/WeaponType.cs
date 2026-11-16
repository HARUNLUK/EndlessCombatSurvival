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

namespace EndlessCombat.Combat
{
    /// <summary>Ammunition used by a firearm (same numbers as EndlessSurvival.Inventory.AmmoType).</summary>
    public enum AmmoKind
    {
        None = 0,
        Pistol = 1,
        Rifle = 2,
        Shotgun = 3
    }

    /// <summary>Shared rounds of the player, one pool per ammo kind. Player firearms reload from it.</summary>
    public interface IAmmoPool
    {
        int Count(AmmoKind kind);
        int Take(AmmoKind kind, int amount);
        void Add(AmmoKind kind, int amount);
    }
}
