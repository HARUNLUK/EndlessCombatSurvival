using System.Collections.Generic;
using UnityEngine;

namespace EndlessCombat.AI
{
    /// <summary>
    /// Squad tactic choice (Enemy_AI_Deep_Plan.md 12.5) and role assignment (12.7). Choose runs every 2 s while in
    /// combat; the first row that holds wins. A tactic lasts at least MinTacticTime (Scatter and Rout cut in).
    /// </summary>
    public static class SquadTactics
    {
        private static readonly List<EnemyController> Pool = new List<EnemyController>();

        public static string Name(SquadTactic t)
        {
            switch (t)
            {
                case SquadTactic.Routine: return "rutin";
                case SquadTactic.Investigate: return "bakmaya gitme";
                case SquadTactic.Search: return "arama";
                case SquadTactic.Hold: return "mevzi tutma";
                case SquadTactic.FixAndFlank: return "tut ve dolan";
                case SquadTactic.Encircle: return "sarma";
                case SquadTactic.Push: return "bas";
                case SquadTactic.FallBack: return "geri çekil";
                case SquadTactic.BreakContact: return "temas kes";
                case SquadTactic.Scatter: return "dağıl";
                case SquadTactic.DefendObjective: return "ödülü koru";
                case SquadTactic.Rout: return "bozgun";
                default: return t.ToString();
            }
        }

        public static string RoleName(SquadRole r)
        {
            switch (r)
            {
                case SquadRole.Suppressor: return "baskıcı";
                case SquadRole.Flanker: return "kanatçı";
                case SquadRole.Pointman: return "öncü";
                case SquadRole.Assault: return "hücumcu";
                case SquadRole.Grenadier: return "bombacı";
                case SquadRole.Guard: return "nöbetçi";
                case SquadRole.Runner: return "kaçan";
                default: return "-";
            }
        }

        public static void Choose(EnemySquad s)
        {
            switch (s.Phase)
            {
                case AlertPhase.Calm: case AlertPhase.Guarded: s.SetTactic(SquadTactic.Routine); return;
                case AlertPhase.Suspicious: s.SetTactic(SquadTactic.Investigate); return;
                case AlertPhase.Searching: case AlertPhase.Lost: s.SetTactic(SquadTactic.Search); return;
            }

            var p = AIDifficulty.For(s);
            SquadTactic want = Pick(s, p);
            bool urgent = want == SquadTactic.Scatter || want == SquadTactic.Rout;
            if (!urgent && s.TacticAge < EnemySquad.MinTacticTime && s.Tactic != SquadTactic.Routine
                && s.Tactic != SquadTactic.Investigate && s.Tactic != SquadTactic.Search) return;
            s.SetTactic(want);
        }

        private static SquadTactic Pick(EnemySquad s, AIDifficulty.Profile p)
        {
            int alive = s.Alive;
            float conf = s.BestConfidence;
            Belief b = s.Shared.Valid ? s.Shared : null;
            foreach (var m in s.Members) if (m != null && !m.IsDead && m.Belief.Primary != null && (b == null || m.Belief.Primary.confidence > b.confidence)) b = m.Belief.Primary;
            var read = b != null ? b.read : PlayerRead.Current;

            // 1 grenade seen / vehicle coming
            if (Time.time < s.scatterUntil || VehicleThreat.ChargingAt(s)) return SquadTactic.Scatter;
            // 2 broken
            if (s.Stance == Stance.Broken) return SquadTactic.Rout;
            // 3 / 4 weak
            if (s.Stance == Stance.Weak && alive >= 2) return SquadTactic.FallBack;
            if (s.Stance == Stance.Weak && alive == 1) return SquadTactic.BreakContact;
            if (alive == 1 && s.startCount > 1 && s.Members.Count > 0 && s.Stance != Stance.Dominant) return SquadTactic.BreakContact;
            // 5 objective
            if (s.objective != null && b != null && (b.position - s.objective.position).magnitude < 12f) return SquadTactic.DefendObjective;
            // 6 push
            bool reloadSeen = p.exploitReload && (read.action == Combat.CombatAction.Reload || Time.time < s.pushUntil);
            bool unarmed = p.exploitReload && read.hand == PlayerHand.None && b != null && b.SinceSeen < 3f;
            bool sheathed = p.exploitDraw && read.hand == PlayerHand.None && b != null && NearestDist(s, b.position) < 15f;
            bool intentPush = PlayerRead.Intent == PlayerIntent.Looting || PlayerRead.Intent == PlayerIntent.Healing;
            if (s.Stance == Stance.Dominant || reloadSeen || unarmed || sheathed || (intentPush && conf > 0.7f)) return SquadTactic.Push;
            // 7 fix and flank
            bool flankOk = p.flank && Time.time >= s.flankLockUntil;
            if (conf > 0.7f && s.AliveGunners >= 2 && flankOk) return SquadTactic.FixAndFlank;
            if (PlayerRead.Intent == PlayerIntent.Camping && s.AliveGunners >= 2 && flankOk) return SquadTactic.FixAndFlank;
            // 8 encircle
            if (conf >= 0.3f && conf <= 0.7f && alive >= 3 && Time.time >= s.flankLockUntil) return SquadTactic.Encircle;
            return SquadTactic.Hold;
        }

        private static float NearestDist(EnemySquad s, Vector3 pos)
        {
            float best = float.MaxValue;
            foreach (var m in s.Members) if (m != null && !m.IsDead) best = Mathf.Min(best, Vector3.Distance(m.transform.position, pos));
            return best;
        }

        /// <summary>Roles for the current tactic (12.7): on tactic change and on a death.</summary>
        public static void Assign(EnemySquad s)
        {
            s.Roles.Clear();
            Pool.Clear();
            foreach (var m in s.Members)
                if (m != null && !m.IsDead && !m.Surrendered && m.Nerve.Level < NerveLevel.Pinned) Pool.Add(m);
            if (Pool.Count == 0) return;
            var p = AIDifficulty.For(s);
            Belief b = s.Shared.Valid ? s.Shared : null;
            foreach (var m in Pool) if (m.Belief.Primary != null && (b == null || m.Belief.Primary.confidence > b.confidence)) b = m.Belief.Primary;
            Vector3 threat = b != null ? b.position : s.home;

            // Pointmen and assault: by weapon
            foreach (var m in Pool)
            {
                if (m.HasUsableShield) s.Roles[m] = SquadRole.Pointman;
                else if (m.IsMeleeRole) s.Roles[m] = SquadRole.Assault;
            }

            switch (s.Tactic)
            {
                case SquadTactic.FixAndFlank:
                {
                    // Suppressors: the 1-2 gunners with a line to the threat, nearest to cover
                    var gunners = Gunners(Pool, s);
                    gunners.Sort((x, y) => Vector3.Distance(x.transform.position, threat).CompareTo(Vector3.Distance(y.transform.position, threat)));
                    int supp = Mathf.Min(gunners.Count > 2 ? 2 : 1, gunners.Count);
                    for (int i = 0; i < supp; i++) s.Roles[gunners[i]] = SquadRole.Suppressor;
                    // Flanker: the healthiest remaining gunner (or a melee fighter)
                    EnemyController flanker = null;
                    for (int i = supp; i < gunners.Count; i++)
                        if (flanker == null || gunners[i].Health01 > flanker.Health01) flanker = gunners[i];
                    if (flanker == null) foreach (var m in Pool) if (m.IsMeleeRole) { flanker = m; break; }
                    if (flanker != null) s.Roles[flanker] = SquadRole.Flanker;
                    if (p.flankDouble && s.Alive >= 4)
                    {
                        EnemyController second = null;
                        foreach (var m in Pool)
                            if (m != flanker && s.RoleOf(m) != SquadRole.Suppressor && m.IsGunner) { second = m; break; }
                        if (second != null) s.Roles[second] = SquadRole.Flanker;
                    }
                    break;
                }
                case SquadTactic.Hold:
                case SquadTactic.Push:
                case SquadTactic.Encircle:
                case SquadTactic.DefendObjective:
                    foreach (var m in Pool) if (!s.Roles.ContainsKey(m) && m.IsGunner) s.Roles[m] = SquadRole.Suppressor;
                    break;
                case SquadTactic.FallBack:
                case SquadTactic.BreakContact:
                    foreach (var m in Pool) if (!s.Roles.ContainsKey(m) && m.IsGunner) s.Roles[m] = SquadRole.Suppressor;
                    break;
            }

            // Grenadier: one member with a grenade, 8-22 m from the threat
            EnemyController gren = null;
            foreach (var m in Pool)
            {
                if (m.grenades <= 0) continue;
                float d = Vector3.Distance(m.transform.position, threat);
                if (d < 8f || d > 22f) continue;
                if (s.RoleOf(m) == SquadRole.Flanker) continue;
                gren = m;
                break;
            }
            if (gren != null && s.RoleOf(gren) != SquadRole.Suppressor) s.Roles[gren] = SquadRole.Grenadier;

            // Guard: Hard+ keeps one at the objective / known vehicle / during the search (17.5); vehicle escape memory
            bool wantGuard = (p.vehicleDeny >= 1 && (s.vehicleEscape > 0 || PlayerRead.Intent == PlayerIntent.ToVehicle) && s.KnownVehicle)
                             || (s.Tactic == SquadTactic.Search && p.vehicleDeny >= 2)
                             || (s.Tactic == SquadTactic.DefendObjective && s.objective != null);
            if (wantGuard && Pool.Count >= 2)
            {
                EnemyController guard = null;
                foreach (var m in Pool)
                {
                    if (m == s.leader || s.RoleOf(m) == SquadRole.Flanker) continue;
                    if (guard == null || m.Health01 < guard.Health01) guard = m;
                }
                if (guard != null) s.Roles[guard] = SquadRole.Guard;
            }
        }

        private static List<EnemyController> Gunners(List<EnemyController> pool, EnemySquad s)
        {
            var list = new List<EnemyController>();
            foreach (var m in pool) if (m.IsGunner && !s.Roles.ContainsKey(m)) list.Add(m);
            return list;
        }
    }
}
