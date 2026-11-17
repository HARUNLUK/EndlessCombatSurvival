using System.Collections.Generic;
using UnityEngine;
using EndlessSurvival.Survival;

namespace EndlessCombat.AI
{
    /// <summary>
    /// Enemies far from the player (Enemy_AI_Deep_Plan.md 19). 1 Hz:
    ///   abstraction   a squad whose living members are all 130 m+ away and not fighting is switched off (members
    ///                 kept, SetActive false) and switched on again under 110 m. Off squads wait where they are.
    ///   trail         the player's position every 100 m (on foot or driving), last 30 points.
    ///   witness       an enemy who saw the player in a fight and got 150 m away (or left with its chunk) alive.
    ///                 Habits are written (AIMemory) and on Normal+ a pursuit group forms 90-240 s later.
    ///   pursuit       walks the trail at 3 m/s as numbers; under 110 m from a player on foot / a parked vehicle it
    ///                 spawns as a Pursuit squad in the Searching phase (they search, they do not know where you are).
    ///   camp threat   replaces the old sleep dice: who is near, how visible the camp is (19.4).
    /// </summary>
    public static class AbstractGroups
    {
        public static float AbstractAt = 130f, ConcreteAt = 110f;
        public static float TrailStep = 100f;
        public static int TrailMax = 30;
        public static float PursuitSpeed = 3f;
        public static float PursuitDropDistance = 2500f;
        public static float PursuitMaxSeconds = 1200f;
        public static float WitnessDistance = 150f;
        public static float FireNight = 1f, FireDay = 0.5f, NoFire = 0.15f;

        public class Pursuit
        {
            public Vector3 position;
            public int count;
            public float created;
            public int trailIndex;
            public float rainSince = -1f;
            public bool rainRolled;
        }

        public static readonly List<Vector3> Trail = new List<Vector3>();
        public static Pursuit Active { get; private set; }
        private static float _pendingAt = -1f;
        private static Vector3 _pendingFrom;
        private static float _nextTick;

        private static Vector3 PlayerPos => AITarget.Player != null ? AITarget.Player.Position : SurvivalDirector.PlayerPosition;

        public static void Tick()
        {
            if (Time.time < _nextTick) return;
            _nextTick = Time.time + 1f;
            if (AITarget.Player == null) return;
            Vector3 pp = PlayerPos;

            // Trail
            if (Trail.Count == 0 || Flat(Trail[Trail.Count - 1] - pp).magnitude >= TrailStep)
            {
                Trail.Add(pp);
                if (Trail.Count > TrailMax)
                {
                    Trail.RemoveAt(0);
                    if (Active != null) Active.trailIndex = Mathf.Max(0, Active.trailIndex - 1);
                }
            }

            TickAbstraction(pp);
            TickWitnessDistance(pp);

            // Pending pursuit
            if (_pendingAt > 0f && Time.time >= _pendingAt)
            {
                _pendingAt = -1f;
                StartPursuit(_pendingFrom);
            }
            if (Active != null) TickPursuit(pp);
        }

        // ------------------------------------------------------------------ 19.1 abstraction

        private static void TickAbstraction(Vector3 pp)
        {
            float a2 = AbstractAt * AbstractAt, c2 = ConcreteAt * ConcreteAt;
            var squads = EnemySquad.Squads;
            for (int i = squads.Count - 1; i >= 0; i--)
            {
                var s = i < squads.Count ? squads[i] : null;
                if (s == null) continue;
                if (!s.IsAbstract)
                {
                    if (s.Phase == AlertPhase.Combat || s.Phase == AlertPhase.Lost || s.Alive == 0) continue;
                    bool allFar = true;
                    foreach (var m in s.Members)
                        if (m != null && !m.IsDead && Flat(m.transform.position - pp).sqrMagnitude < a2) { allFar = false; break; }
                    if (!allFar) continue;
                    s.IsAbstract = true;
                    foreach (var m in s.Members)
                        if (m != null && !m.IsDead) m.gameObject.SetActive(false);
                }
                else
                {
                    bool near = false;
                    foreach (var m in s.Members)
                        if (m != null && Flat(m.transform.position - pp).sqrMagnitude < c2) { near = true; break; }
                    if (!near) continue;
                    s.IsAbstract = false;
                    foreach (var m in s.Members)
                        if (m != null && !m.IsDead) m.gameObject.SetActive(true);
                }
            }
        }

        // ------------------------------------------------------------------ 19.3 witnesses

        private static void TickWitnessDistance(Vector3 pp)
        {
            float w2 = WitnessDistance * WitnessDistance;
            foreach (var s in EnemySquad.Squads)
            {
                if (s.witnessDone || !s.everFought) continue;
                foreach (var m in s.Members)
                {
                    if (m == null || m.IsDead || m.Surrendered) continue;
                    var b = m.Belief != null ? m.Belief.Of(AITarget.Player) : null;
                    if (b == null || !b.everSeen) continue;
                    if (Flat(m.transform.position - pp).sqrMagnitude < w2) continue;
                    Witness(m);
                    break;
                }
            }
        }

        /// <summary>An enemy who saw the player fight is getting away (or leaving with its chunk).</summary>
        public static void Witness(EnemyController e)
        {
            var s = e != null ? e.Squad : null;
            if (s == null || s.witnessDone) return;
            if (EndlessSurvival.Testing.TestArena.Active) { s.witnessDone = true; return; }
            s.witnessDone = true;
            SurvivalNotify.Warn("Biri kaçtı. Haber götürecek.");
            AIDebug.Log(s, $"{e.DisplayName} tanık olarak kaçtı");
            AIMemory.RecordEncounter(s);
            if (AIDifficulty.Current == AIDifficulty.Tier.Easy) return;
            if (DifficultyCurve.EarlyGame || Active != null || _pendingAt > 0f) return;
            _pendingAt = Time.time + Random.Range(90f, 240f);
            _pendingFrom = e.transform.position;
        }

        // ------------------------------------------------------------------ pursuit

        public static void StartPursuit(Vector3 from)
        {
            int count = 2 + Mathf.RoundToInt(2f * DifficultyCurve.CharacterLevel);
            count = DifficultyCurve.CapHumans(count);
            Active = new Pursuit { position = from, count = Mathf.Max(1, count), created = Time.time, trailIndex = NearestTrailIndex(from) };
            SurvivalNotify.Warn("Peşine düştüler.");
        }

        private static int NearestTrailIndex(Vector3 p)
        {
            int best = Mathf.Max(0, Trail.Count - 1);
            float bd = float.MaxValue;
            for (int i = 0; i < Trail.Count; i++)
            {
                float d = (Trail[i] - p).sqrMagnitude;
                if (d < bd) { bd = d; best = i; }
            }
            return best;
        }

        public static void DropPursuit(string why)
        {
            if (Active == null) return;
            Active = null;
            if (!string.IsNullOrEmpty(why)) AIDebug.Log(null, "takip bitti: " + why);
        }

        private static void TickPursuit(Vector3 pp)
        {
            var g = Active;
            float dist = Flat(g.position - pp).magnitude;
            // Drop rules (19.3)
            if (dist > PursuitDropDistance) { DropPursuit("çok uzaklaştın"); return; }
            if (SafeZone.Near(pp)) { DropPursuit("güvenli bölge"); return; }
            if (Time.time - g.created > PursuitMaxSeconds) { DropPursuit("vazgeçtiler"); return; }
            var w = WeatherSystem.Instance;
            if (w != null && w.RainIntensity > 0.5f && dist > 400f)
            {
                if (g.rainSince < 0f) g.rainSince = Time.time;
                if (!g.rainRolled && Time.time - g.rainSince > 60f)
                {
                    g.rainRolled = true;
                    if (Random.value < 0.5f) { DropPursuit("yağmur izi sildi"); return; }
                }
            }
            else g.rainSince = -1f;

            // Walk the trail (1 s step)
            float step = PursuitSpeed;
            while (step > 0f)
            {
                Vector3 next = g.trailIndex < Trail.Count ? Trail[g.trailIndex] : pp;
                Vector3 to = Flat(next - g.position);
                float d = to.magnitude;
                if (d <= step)
                {
                    g.position = new Vector3(next.x, g.position.y, next.z);
                    step -= d;
                    if (g.trailIndex < Trail.Count) g.trailIndex++;
                    else break;
                }
                else
                {
                    g.position += to / d * step;
                    step = 0f;
                }
            }

            // Catch up: player on foot or the vehicle stopped (19.3)
            var v = SurvivalDirector.Vehicle;
            bool driving = SurvivalDirector.PlayerInVehicle && v != null && v.CurrentSpeedKmh > 20f;
            if (dist < ConcreteAt && !driving) Materialize(g);
        }

        private static void Materialize(Pursuit g)
        {
            var mgr = EndlessSurvival.Camping.CampAmbushManager.Instance;
            GameObject prefab = mgr != null ? mgr.enemyPrefab : null;
            if (prefab == null) { DropPursuit("düşman örneği yok"); return; }
            Active = null;
            Vector3 center = g.position;
            Vector3 lastTrail = Trail.Count > 0 ? Trail[Trail.Count - 1] : PlayerPos;
            var squad = EnemySquad.Create(SquadKind.Pursuit, center);
            for (int i = 0; i < g.count; i++)
            {
                Vector3 p = center + Quaternion.AngleAxis(i * 360f / g.count, Vector3.up) * Vector3.forward * 2.5f;
                if (Physics.Raycast(p + Vector3.up * 100f, Vector3.down, out RaycastHit hit, 200f)) p.y = hit.point.y + 0.1f;
                if (EnemyMover.OnNavMesh(p, 6f, out Vector3 s)) p = s;
                var go = Object.Instantiate(prefab, p, Quaternion.LookRotation(Flat(lastTrail - p).sqrMagnitude > 0.01f ? Flat(lastTrail - p) : Vector3.forward));
                var e = go.GetComponent<EnemyController>();
                if (e == null) continue;
                e.initialState = EnemyController.EnemyState.Idle;
                DifficultyCurve.ApplyToEnemy(e, p);
                squad.Add(e);
            }
            squad.Shared.target = AITarget.Player;
            squad.Shared.position = lastTrail;
            squad.Shared.confidence = 0.4f;
            squad.Shared.time = Time.time;
            squad.Shared.source = BeliefSource.Told;
            squad.SetPhase(AlertPhase.Searching, lastTrail);
            AIDebug.Log(squad, $"takipçiler yetişti ({g.count} kişi)");
        }

        public static string PursuitText()
        {
            if (Active != null) return $"{Active.count} kişi, {Flat(Active.position - PlayerPos).magnitude:0} m";
            if (_pendingAt > 0f) return $"{Mathf.Max(0f, _pendingAt - Time.time):0} sn sonra oluşacak";
            return "yok";
        }

        // ------------------------------------------------------------------ 19.4 camp threat

        private static readonly Vector3[] Dirs12 = MakeDirs();

        private static Vector3[] MakeDirs()
        {
            var a = new Vector3[12];
            for (int i = 0; i < 12; i++) a[i] = Quaternion.AngleAxis(i * 30f, Vector3.up) * Vector3.forward;
            return a;
        }

        /// <summary>How visible a camp is (0..1): fire, openness, roof, sleeping in the vehicle.</summary>
        public static float CampVisibility(Vector3 camp)
        {
            var cycle = EndlessSurvival.World.DayNightCycle.Instance;
            bool night = cycle != null && cycle.IsNight;
            bool fire = CraftingSystem.NearestBurningFire(camp, 8f) != null;
            float v = fire ? (night ? FireNight : FireDay) : NoFire;
            int closed = 0;
            Vector3 eye = camp + Vector3.up * 1.2f;
            for (int i = 0; i < 12; i++)
                if (Physics.Raycast(eye, Dirs12[i], 30f, ~0, QueryTriggerInteraction.Ignore)) closed++;
            v *= 1f - 0.6f * closed / 12f;
            if (Physics.Raycast(eye, Vector3.up, 8f, ~0, QueryTriggerInteraction.Ignore)) v *= 0.5f;
            if (SurvivalDirector.PlayerInVehicle) v *= 0.6f;
            return Mathf.Clamp01(v);
        }

        /// <summary>
        /// Who (if anyone) notices the camp while the player sleeps (19.4). No candidates: no raid. The roll is
        /// seeded by the camp position and the game hour (same situation, same result).
        /// </summary>
        public static bool CampThreat(Vector3 camp, out int count)
        {
            count = 0;
            float vis = CampVisibility(camp);
            int seed = Mathf.RoundToInt(camp.x) * 73856093 ^ Mathf.RoundToInt(camp.z) * 19349663 ^ (int)System.Math.Floor(GameClock.TotalHours) * 83492791;
            var rng = new System.Random(seed);
            bool found = false;
            foreach (var s in EnemySquad.Squads)
            {
                if (s.Alive == 0) continue;
                float range = s.kind == SquadKind.Patrol || s.kind == SquadKind.Raid ? 250f : s.kind == SquadKind.Camp ? 150f : 0f;
                if (range <= 0f) continue;
                Vector3 at = s.home;
                foreach (var m in s.Members) if (m != null && !m.IsDead) { at = m.transform.position; break; }
                float d = Flat(at - camp).magnitude;
                if (d > range) continue;
                float chance = vis * (1f - d / range);
                if (rng.NextDouble() < chance)
                {
                    found = true;
                    count = Mathf.Max(count, s.kind == SquadKind.Camp ? 2 : Mathf.Min(s.Alive, 4));
                    AIDebug.Log(s, "kampını fark ettiler");
                }
            }
            if (Active != null)
            {
                float d = Flat(Active.position - camp).magnitude;
                if (d < 600f && rng.NextDouble() < vis * (1f - d / 600f) + 0.2f)
                {
                    found = true;
                    count = Mathf.Max(count, Active.count);
                    DropPursuit(null);
                }
            }
            if (found) count = DifficultyCurve.CapHumans(Mathf.Max(2, count));
            return found;
        }

        // ------------------------------------------------------------------ save (through AIMemory)

        [System.Serializable]
        private class Data
        {
            public bool active;
            public Vector3 position;
            public int count;
            public float age;
            public List<Vector3> trail = new List<Vector3>();
            public float pendingIn = -1f;
            public Vector3 pendingFrom;
        }

        public static string CaptureJson()
        {
            var d = new Data();
            if (Active != null)
            {
                d.active = true;
                d.position = Active.position;
                d.count = Active.count;
                d.age = Time.time - Active.created;
            }
            d.trail.AddRange(Trail);
            if (_pendingAt > 0f)
            {
                d.pendingIn = Mathf.Max(1f, _pendingAt - Time.time);
                d.pendingFrom = _pendingFrom;
            }
            return JsonUtility.ToJson(d);
        }

        public static void RestoreJson(string json)
        {
            Trail.Clear();
            Active = null;
            _pendingAt = -1f;
            if (string.IsNullOrEmpty(json)) return;
            var d = JsonUtility.FromJson<Data>(json);
            if (d == null) return;
            if (d.trail != null) Trail.AddRange(d.trail);
            if (d.active)
                Active = new Pursuit { position = d.position, count = d.count, created = Time.time - d.age, trailIndex = NearestTrailIndex(d.position) };
            if (d.pendingIn > 0f)
            {
                _pendingAt = Time.time + d.pendingIn;
                _pendingFrom = d.pendingFrom;
            }
        }

        public static void ResetAll()
        {
            Trail.Clear();
            Active = null;
            _pendingAt = -1f;
        }

        private static Vector3 Flat(Vector3 v)
        {
            v.y = 0f;
            return v;
        }
    }
}
