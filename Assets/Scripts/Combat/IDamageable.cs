namespace EndlessCombat.Combat
{
    /// <summary>
    /// Anything that can be hurt. Hits arrive as one DamageInfo (type, point, source direction, region).
    /// Deal damage through Damage.Deal; the old (amount, point, normal) call still works through
    /// DamageableExtensions and arrives as DamageType.Other.
    /// </summary>
    public interface IDamageable
    {
        void TakeDamage(DamageInfo info);
        bool IsDead { get; }
    }
}
