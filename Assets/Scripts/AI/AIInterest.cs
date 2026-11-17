using UnityEngine;
using EndlessCombat.Combat;
using EndlessSurvival.Inventory;

namespace EndlessCombat.AI
{
    /// <summary>
    /// Objects an enemy may walk to (Enemy_AI_Deep_Plan.md 13.10): weapons lying on the ground (WorldPickup.All).
    /// The score weighs the gain (weapon class, own ammo, disarming the player) against the risk and the walk.
    /// </summary>
    public static class AIInterest
    {
        public static float SearchRadius = 15f;
        public static float MinScore = 3f;

        public static float WeaponValue(WeaponType t, bool knife)
        {
            switch (t)
            {
                case WeaponType.Rifle: return 4f;
                case WeaponType.Pistol: return 3f;
                case WeaponType.Melee: return knife ? 1f : 1.5f;
                default: return 0f;
            }
        }

        public static float ValueOf(WorldPickup p)
        {
            if (p == null || p.item == null || p.item.weaponPrefab == null) return -1f;
            var w = p.item.weaponPrefab;
            var mw = w.GetComponent<MeleeWeapon>();
            bool knife = mw != null && mw.stats.ResolveCategory(false) == MeleeCategory.Short;
            return WeaponValue(w.Type, knife);
        }

        /// <summary>Best pickup worth the walk, or null.</summary>
        public static WorldPickup Find(EnemyController e, Belief threat, out float score)
        {
            score = 0f;
            var p = AIDifficulty.For(e.Squad);
            bool lowAmmo = e.TotalAmmo < 6;
            bool out_ = !e.HasAnyAmmo;
            if (p.scavenge == 0 && !out_ && !(e.IsMeleeRole && e.OutOfAmmo)) return null;
            if (p.scavenge == 1 && !lowAmmo && !out_) return null;
            if (e.UnderFire(3f) && threat != null && threat.SeenNow) return null;

            WorldPickup best = null;
            Vector3 me = e.transform.position;
            float mine = e.CurrentWeaponValue;
            var read = threat != null ? threat.read : PlayerRead.Current;
            foreach (var w in WorldPickup.All)
            {
                if (w == null || w.item == null || w.item.weaponPrefab == null) continue;
                if (w.claimedBy != null && w.claimedBy != e) continue;
                Vector3 pos = w.transform.position;
                float dist = (pos - me).magnitude;
                if (dist > SearchRadius) continue;
                float gain = ValueOf(w) - mine;
                if (out_ || lowAmmo) gain += 2f;
                bool playersDrop = w.fromHand && Time.time - w.SpawnTime < 30f;
                bool disarm = playersDrop && (p.scavenge >= 3 || (p.exploitDrop && e.Squad != null && e.Squad.weaponRecovered > 0));
                if (disarm) gain += 2f;
                if (gain <= 0f) continue;

                float safety = 1f;
                if (threat != null && threat.Valid && threat.confidence > 0.3f)
                {
                    bool covered = CoverMap.Blocked(threat.position + Vector3.up * 1.5f, pos + Vector3.up * 1f);
                    if (!covered) safety *= 0.5f;
                    if (read.aiming)
                    {
                        Vector3 toP = (pos - threat.position).normalized;
                        if (Vector3.Angle(read.aimDirection, toP) < 8f) safety *= 0.3f;
                    }
                }
                if (e.UnderFire(3f)) safety = 0f;
                float time = dist / Mathf.Max(0.5f, e.moveSpeed) + 1f;
                float s = (gain * 2f - time * 0.3f) * safety;
                if (s > score)
                {
                    score = s;
                    best = w;
                }
            }
            if (score < MinScore) return null;
            return best;
        }
    }
}
