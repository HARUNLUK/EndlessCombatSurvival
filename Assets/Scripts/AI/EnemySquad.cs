using System.Collections.Generic;
using UnityEngine;

namespace EndlessCombat.AI
{
    /// <summary>
    /// Shared memory of enemies that fight together (Core_Feel_Plan stage 4, 2026-11-14), and the attack tokens.
    ///   Squad      enemies that spawn near each other (within JoinRadius) share where the player was last seen.
    ///   Tokens     at most MaxAttackers enemies shoot at / strike the player at the same time (all squads together).
    ///              The others move, take cover or wait. A holder gives the token back after a while (HoldTime) or
    ///              when it loses the player, so the turns rotate. This one rule ends "they all come at me at once".
    /// </summary>
    public class EnemySquad
    {
        // ---- tuning (panel page "Düşman") ----
        public static int MaxAttackers = 2;
        public static float HoldTime = 5f;
        public static float RequestCooldown = 1.2f;
        public static float JoinRadius = 40f;

        private static readonly List<EnemySquad> Squads = new List<EnemySquad>();
        private static readonly List<EnemyController> Holders = new List<EnemyController>();
        private static readonly Dictionary<EnemyController, float> HeldSince = new Dictionary<EnemyController, float>();
        private static readonly Dictionary<EnemyController, float> NextRequest = new Dictionary<EnemyController, float>();

        public readonly List<EnemyController> Members = new List<EnemyController>();
        public Vector3 LastKnownPlayerPos;
        public float LastSeenTime = -999f;
        public bool HasMemory => LastSeenTime > -900f;

        /// <summary>Joins the squad of enemies close by, or starts a new one.</summary>
        public static EnemySquad Join(EnemyController enemy)
        {
            Cleanup();
            Vector3 p = enemy.transform.position;
            EnemySquad best = null;
            float bestD = JoinRadius * JoinRadius;
            foreach (var s in Squads)
            {
                foreach (var m in s.Members)
                {
                    if (m == null || m.IsDead) continue;
                    float d = (m.transform.position - p).sqrMagnitude;
                    if (d < bestD) { bestD = d; best = s; }
                }
            }
            if (best == null)
            {
                best = new EnemySquad();
                Squads.Add(best);
            }
            if (!best.Members.Contains(enemy)) best.Members.Add(enemy);
            return best;
        }

        public void Leave(EnemyController enemy)
        {
            Members.Remove(enemy);
            ReleaseToken(enemy);
            if (Members.Count == 0) Squads.Remove(this);
        }

        public void ReportSighting(Vector3 playerPos)
        {
            LastKnownPlayerPos = playerPos;
            LastSeenTime = Time.time;
        }

        /// <summary>Alive members of this squad that are fighting, other than the asker, within radius.</summary>
        public int AlliesFighting(EnemyController asker, float radius)
        {
            int n = 0;
            float r2 = radius * radius;
            foreach (var m in Members)
            {
                if (m == null || m == asker || m.IsDead || !m.IsAlerted) continue;
                if ((m.transform.position - asker.transform.position).sqrMagnitude <= r2) n++;
            }
            return n;
        }

        /// <summary>Nearest alive member with a raised shield (gunners hide behind it).</summary>
        public EnemyController NearestShieldBearer(EnemyController asker, float radius)
        {
            EnemyController best = null;
            float bestD = radius * radius;
            foreach (var m in Members)
            {
                if (m == null || m == asker || m.IsDead || !m.HasRaisedShield) continue;
                float d = (m.transform.position - asker.transform.position).sqrMagnitude;
                if (d < bestD) { bestD = d; best = m; }
            }
            return best;
        }

        /// <summary>Nearest alive gunner of the squad (melee enemies keep out of its line of fire).</summary>
        public EnemyController NearestGunner(EnemyController asker)
        {
            EnemyController best = null;
            float bestD = float.MaxValue;
            foreach (var m in Members)
            {
                if (m == null || m == asker || m.IsDead || !m.IsAlerted || m.IsMeleeRole) continue;
                float d = (m.transform.position - asker.transform.position).sqrMagnitude;
                if (d < bestD) { bestD = d; best = m; }
            }
            return best;
        }

        // ------------------------------------------------------------------ attack tokens

        public static bool HasToken(EnemyController e) => Holders.Contains(e);

        public static int TokenCount
        {
            get { Cleanup(); return Holders.Count; }
        }

        /// <summary>Asks for the right to shoot / strike. True when this enemy holds a token now.</summary>
        public static bool RequestToken(EnemyController e)
        {
            Cleanup();
            if (Holders.Contains(e))
            {
                // Held long enough and somebody else could use it: give it back (turns rotate)
                if (!HeldSince.TryGetValue(e, out float since)) HeldSince[e] = since = Time.time;
                if (Time.time - since > HoldTime && WaitingOthers(e))
                {
                    ReleaseToken(e);
                    return false;
                }
                return true;
            }
            if (NextRequest.TryGetValue(e, out float next) && Time.time < next) return false;
            if (Holders.Count >= Mathf.Max(1, MaxAttackers)) return false;
            Holders.Add(e);
            HeldSince[e] = Time.time;
            return true;
        }

        public static void ReleaseToken(EnemyController e)
        {
            if (!Holders.Remove(e)) return;
            HeldSince.Remove(e);
            NextRequest[e] = Time.time + RequestCooldown;
        }

        private static bool WaitingOthers(EnemyController holder)
        {
            foreach (var other in EnemyController.Active)
                if (other != null && other != holder && !other.IsDead && other.IsAlerted && !Holders.Contains(other)) return true;
            return false;
        }

        private static void Cleanup()
        {
            for (int i = Holders.Count - 1; i >= 0; i--)
            {
                var h = Holders[i];
                if (h == null || h.IsDead || !h.isActiveAndEnabled || !h.IsAlerted)
                {
                    Holders.RemoveAt(i);
                    if (h != null) HeldSince.Remove(h);
                }
            }
            for (int i = Squads.Count - 1; i >= 0; i--)
            {
                Squads[i].Members.RemoveAll(m => m == null);
                if (Squads[i].Members.Count == 0) Squads.RemoveAt(i);
            }
        }
    }
}
