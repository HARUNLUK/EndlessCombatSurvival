using UnityEngine;

namespace EndlessCombat.Combat
{
    public interface IDamageable
    {
        void TakeDamage(float damage, Vector3 hitPoint, Vector3 hitNormal);
        bool IsDead { get; }
    }
}
