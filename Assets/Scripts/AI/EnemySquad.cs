using System.Collections.Generic;
using UnityEngine;
using EndlessSurvival.Survival;

namespace EndlessCombat.AI
{
    public enum SquadKind { Camp, Patrol, Occupants, Raid, Pursuit, Ambush, Loose }
    /// <summary>Calm, Suspicious, Searching, Combat, Lost, Guarded (Sakin, Şüphe, Arama, Çatışma, Kayıp, Tetikte).</summary>
    public enum AlertPhase { Calm, Suspicious, Searching, Combat, Lost, Guarded }
    public enum Stance { Dominant, Even, Weak, Broken }
    public enum SquadTactic { Routine, Investigate, Search, Hold, FixAndFlank, Encircle, Push, FallBack, BreakContact, Scatter, DefendObjective, Rout }
    public enum SquadRole { None, Suppressor, Flanker, Pointman, Assault, Grenadier, Guard, Runner }

    /// <summary>
    /// A group of enemies that fight together (Enemy_AI_Deep_Plan.md 12): who is in it (the spawner sets it up),
    /// the alert phase, the power balance, the tactic, the roles, the attack tokens (per squad, plus a global cap),
    /// the leader, the shared belief (only updated by shouts), the search, the encounter memory.
    /// </summary>
    public class EnemySquad
    {
        // ---- tuning (panel page "Düşman") ----
        public static float HoldTime = 5f;
        public static float RequestCooldown = 1.2f;
        public static float JoinRadius = 40f;
        public static float SuspiciousCalmAfter = 20f;
        public static float LostAfter = 4f;
        public static float MinTacticTime = 6f;
        public static float MinStanceTime = 4f;
        public static float DominantAt = 3f, EvenAt = 1.5f, WeakAt = 0.7f;
        /// <summary>Legacy global setting (old panel): the global cap is maxAttackers + 1.</summary>
        public static int MaxAttackers = 2;

        public static readonly List<EnemySquad> Squads = new List<EnemySquad>();
        private static int _nextId = 1;

        public readonly int id;
        public SquadKind kind;
        public Vector3 home;
        public Transform objective;
        public EnemyController leader;
        public readonly List<EnemyController> Members = new List<EnemyController>();
        public AlertPhase Phase { get; private set; } = AlertPhase.Calm;
        public float PhaseSince { get; private set; }
        public Stance Stance { get; private set; } = Stance.Even;
        public float Power { get; private set; } = 1f;
        public SquadTactic Tactic { get; private set; } = SquadTactic.Routine;
        public float TacticSince { get; private set; }
        public readonly Belief Shared = new Belief();
        public int TierPenalty;
        public bool everFought, everSearched;
        public bool signatureUsed;
        public bool KnownVehicle;
        public float lastGrenadeTime = -999f;
        public int grenadesGiven;
        public float flankLockUntil;
        public float scatterUntil;
        public EnemySearch Search;
        /// <summary>Squad is abstract (members switched off far from the player, 19.1).</summary>
        public bool IsAbstract;
        public bool startGuarded;
        public int startCount;
        public int killedByPlayer, timesLost, fled;
        public readonly List<string> TacticsTried = new List<string>();
        public readonly List<float> RecentKills = new List<float>();
        public readonly Dictionary<EnemyController, SquadRole> Roles = new Dictionary<EnemyController, SquadRole>();
        public readonly Dictionary<EnemyController, float> LastSeenByMate = new Dictionary<EnemyController, float>();
        public readonly Dictionary<EnemyController, Vector3> LastSeenAt = new Dictionary<EnemyController, Vector3>();
        public readonly List<EnemyController> Allied = new List<EnemyController>();
        public string summaryEnd = "";
        public float lastInfoTime = -999f;
        public bool witnessDone;
        public float pushUntil;
        /// <summary>Robbery / ambush: aiming, not firing (21.4, 21.5).</summary>
        public bool HoldFire;
        /// <summary>Shout ids already relayed into this squad (11.2: another squad's news is passed on once).</summary>
        public readonly HashSet<int> HeardShouts = new HashSet<int>();
        public int SilentKills;
        /// <summary>The player attacked first (night raid habit, 20.2).</summary>
        public bool PlayerStarted;
        /// <summary>Signature move in progress (AISignatures).</summary>
        public int signature;
        public float signatureSince;
        public int signatureFlags;
        public EnemyController signatureMember;

        /// <summary>A Camp / Raid squad whose first member was not a gunner: the first gunner later leads.</summary>
        public void EnsureLeader()
        {
            if (LeaderAlive || (kind != SquadKind.Camp && kind != SquadKind.Raid)) return;
            foreach (var m in Members)
                if (m != null && !m.IsDead && m.IsGunnerKind) { SetLeader(m); return; }
        }

        public static void NoteWeaponRecovered()
        {
            foreach (var s in Squads)
                if (s.Phase == AlertPhase.Combat && AIDifficulty.For(s).encounterMemory) s.weaponRecovered++;
        }

        // encounter memory (20.1)
        public int sameSpot, vehicleEscape, longRange, stealthFound, weaponRecovered;
        private Vector3 _lastSpot;
        private float _spotSince;
        private readonly List<Vector3> _spotsVisited = new List<Vector3>();

        // tokens (12.8)
        private readonly List<EnemyController> _holders = new List<EnemyController>();
        private readonly Dictionary<EnemyController, float> _heldSince = new Dictionary<EnemyController, float>();
        private static readonly Dictionary<EnemyController, float> NextRequest = new Dictionary<EnemyController, float>();

        private int _tickFrame = -1;
        private float _nextPower, _nextTactic, _nextMissing, _lostSince = -1f, _stanceSince;
        private float _allBrokenSince = -1f;

        private readonly float _created;
        private bool _grenadesHanded;

        /// <summary>
        /// Grenades of the squad (4.2): a flat count plus grenadesPerGunner per gunner (rounded up), +1 when the
        /// faction expects a shield (20.2). Given one by one to gunners, the leader first. Loose squads get none.
        /// </summary>
        private void HandOutGrenades(AIDifficulty.Profile p)
        {
            _grenadesHanded = true;
            if (kind == SquadKind.Loose) return;
            int gunners = 0;
            foreach (var m in Members) if (m != null && !m.IsDead && m.IsGunnerKind) gunners++;
            if (gunners == 0) return;
            int n = p.grenadesFlat + Mathf.CeilToInt(p.grenadesPerGunner * gunners);
            if (AIMemory.Habit(AIMemory.HabitKind.Shield) >= 0.5f) n++;
            int guard = 0;
            while (n > 0 && guard++ < 32)
            {
                foreach (var m in Members)
                {
                    if (n <= 0) break;
                    if (m == null || m.IsDead || !m.IsGunnerKind) continue;
                    if (m != leader && LeaderAlive && guard == 1 && leader.grenades == 0) continue;
                    m.grenades++;
                    n--;
                    grenadesGiven++;
                }
            }
        }

        public EnemySquad(SquadKind kind, Vector3 home)
        {
            _created = Time.time;
            id = _nextId++;
            this.kind = kind;
            this.home = home;
            PhaseSince = Time.time;
            TacticSince = Time.time;
        }

        // ------------------------------------------------------------------ membership (12.1)

        public static EnemySquad Create(SquadKind kind, Vector3 home)
        {
            var s = new EnemySquad(kind, home);
            Squads.Add(s);
            if (AIMemory.Habit(AIMemory.HabitKind.Stealth) >= 0.5f && kind == SquadKind.Camp && Random.value < 0.5f) s.startGuarded = true;
            return s;
        }

        /// <summary>Adds an enemy (call right after spawning, before or after its Start).</summary>
        public void Add(EnemyController e)
        {
            if (e == null) return;
            if (e.Squad != null && e.Squad != this) e.Squad.Leave(e);
            if (!Members.Contains(e)) Members.Add(e);
            e.SetSquad(this);
            startCount = Mathf.Max(startCount, Members.Count);
            if (leader == null && (kind == SquadKind.Camp || kind == SquadKind.Raid))
            {
                // First gunner of the camp / raid leads (12.9)
                if (e.IsGunnerKind) SetLeader(e);
            }
            if (startGuarded && Phase == AlertPhase.Calm) SetPhase(AlertPhase.Guarded);
        }

        public void SetLeader(EnemyController e)
        {
            if (leader == e || e == null) return;
            leader = e;
            e.MakeLeader();
        }

        /// <summary>Fallback for enemies spawned without a squad: the old 40 m rule.</summary>
        public static EnemySquad Join(EnemyController enemy)
        {
            Cleanup();
            Vector3 p = enemy.transform.position;
            EnemySquad best = null;
            float bestD = JoinRadius * JoinRadius;
            foreach (var s in Squads)
            {
                if (s.kind != SquadKind.Loose) continue;
                foreach (var m in s.Members)
                {
                    if (m == null || m.IsDead) continue;
                    float d = (m.transform.position - p).sqrMagnitude;
                    if (d < bestD) { bestD = d; best = s; }
                }
            }
            if (best == null) best = Create(SquadKind.Loose, p);
            best.Add(enemy);
            return best;
        }

        public void Leave(EnemyController enemy)
        {
            Members.Remove(enemy);
            ReleaseToken(enemy);
            Roles.Remove(enemy);
            // A dead (silently killed) mate stays in the "last seen" list: the others may miss him (7.3)
            if (enemy == null || !enemy.IsDead)
            {
                LastSeenByMate.Remove(enemy);
                LastSeenAt.Remove(enemy);
            }
            if (enemy == leader && enemy != null && !enemy.IsDead) leader = null;
            if (Members.Count == 0) Dissolve();
        }

        private void Dissolve()
        {
            if (!Squads.Contains(this)) return;
            Squads.Remove(this);
            if (everFought) AIDebug.Summary(this);
        }

        public int Alive
        {
            get
            {
                int n = 0;
                foreach (var m in Members) if (m != null && !m.IsDead) n++;
                return n;
            }
        }

        public int AliveGunners
        {
            get
            {
                int n = 0;
                foreach (var m in Members) if (m != null && !m.IsDead && m.IsGunner) n++;
                return n;
            }
        }

        public bool LeaderAlive => leader != null && !leader.IsDead;

        // ------------------------------------------------------------------ queries used by members

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

        public bool FriendWithin(EnemyController asker, float radius)
        {
            float r2 = radius * radius;
            foreach (var m in EnemyController.Active)
            {
                if (m == null || m == asker || m.IsDead) continue;
                if ((m.transform.position - asker.transform.position).sqrMagnitude <= r2) return true;
            }
            return false;
        }

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

        public EnemyController NearestReloadingMate(EnemyController asker, float radius)
        {
            float r2 = radius * radius;
            foreach (var m in Members)
            {
                if (m == null || m == asker || m.IsDead || !m.IsReloading) continue;
                if ((m.transform.position - asker.transform.position).sqrMagnitude <= r2) return m;
            }
            return null;
        }

        public bool SomeoneReloading(EnemyController except)
        {
            foreach (var m in Members) if (m != null && m != except && !m.IsDead && m.IsReloading) return true;
            return false;
        }

        public SquadRole RoleOf(EnemyController e) => Roles.TryGetValue(e, out var r) ? r : SquadRole.None;

        public Vector3 SuppressorCentroid(Vector3 fallback)
        {
            Vector3 sum = Vector3.zero;
            int n = 0;
            foreach (var kv in Roles)
            {
                if (kv.Value != SquadRole.Suppressor || kv.Key == null || kv.Key.IsDead) continue;
                sum += kv.Key.transform.position;
                n++;
            }
            return n > 0 ? sum / n : fallback;
        }

        /// <summary>Best belief: the member's own or the shared one, whichever is better (8.6).</summary>
        public Belief BestBelief(EnemyController asker)
        {
            var own = asker.Belief.Primary;
            if (own != null && (!Shared.Valid || own.confidence >= Shared.confidence)) return own;
            return Shared.Valid ? Shared : own;
        }

        public float BestConfidence
        {
            get
            {
                float c = Shared.Valid ? Shared.confidence : 0f;
                foreach (var m in Members)
                {
                    if (m == null || m.IsDead || m.Belief?.Primary == null) continue;
                    c = Mathf.Max(c, m.Belief.Primary.confidence);
                }
                return c;
            }
        }

        public bool AnyMemberSees
        {
            get
            {
                foreach (var m in Members) if (m != null && !m.IsDead && m.Belief?.Primary != null && m.Belief.Primary.SeenNow) return true;
                return false;
            }
        }

        public static bool AnyKnowsVehicle
        {
            get
            {
                foreach (var s in Squads) if (s.KnownVehicle) return true;
                return AIMemory.Habit(AIMemory.HabitKind.VehicleEscape) >= 0.5f;
            }
        }

        public static bool AnyVehicleEscapeMemory
        {
            get
            {
                foreach (var s in Squads) if (s.vehicleEscape > 0) return true;
                return false;
            }
        }

        // ------------------------------------------------------------------ phases (12.2)

        public void SetPhase(AlertPhase p, Vector3? center = null)
        {
            if (center.HasValue && (p == AlertPhase.Searching || p == AlertPhase.Lost || p == AlertPhase.Suspicious))
            {
                if (!Shared.Valid || Shared.confidence < 0.35f)
                {
                    Shared.position = center.Value;
                    Shared.confidence = Mathf.Max(Shared.confidence, 0.35f);
                    Shared.time = Time.time;
                    if (Shared.target == null) Shared.target = AITarget.Player;
                }
            }
            if (Phase == p) return;
            var old = Phase;
            Phase = p;
            PhaseSince = Time.time;
            lastInfoTime = Time.time;
            if (p == AlertPhase.Combat) everFought = true;
            if (p == AlertPhase.Searching || p == AlertPhase.Lost)
            {
                everSearched = true;
                Search = new EnemySearch(this, center ?? (Shared.Valid ? Shared.EstimatedWith(AIDifficulty.For(this).predictSeconds) : home));
                if (p == AlertPhase.Lost) timesLost++;
            }
            else Search = null;
            AIDebug.Log(this, $"kademe {PhaseName(old)} -> {PhaseName(p)}");
            foreach (var m in Members) if (m != null && !m.IsDead) m.OnSquadPhase(p);
        }

        /// <summary>
        /// A member hears the driven vehicle while the squad is after him: the shared belief moves to the sound, but
        /// never over fresher sight news.
        /// </summary>
        public void HearVehicle(Vector3 pos, float conf)
        {
            if (Phase == AlertPhase.Calm || Phase == AlertPhase.Guarded) return;
            if (AITarget.Player == null) return;
            if (Shared.Valid && Shared.source == BeliefSource.Seen && Time.time - Shared.time < 1.5f) return;
            lastInfoTime = Time.time;
            Shared.target = AITarget.Player;
            Shared.position = pos;
            Shared.velocity = Vector3.zero;
            Shared.confidence = Mathf.Clamp01(Mathf.Max(conf, Shared.Valid ? Mathf.Min(Shared.confidence, 0.7f) : 0f));
            Shared.time = Time.time;
            Shared.source = BeliefSource.Heard;
            if (Phase == AlertPhase.Searching || Phase == AlertPhase.Lost) Search?.Recenter(pos);
        }

        /// <summary>
        /// A member sees the driven vehicle: the shared belief moves to it (user bug report 16 Nov: after he got in they
        /// kept searching the boarding spot, because the shared belief still pointed there).
        /// </summary>
        public void SeeVehicle(Vector3 pos)
        {
            if (AITarget.Player == null) return;
            lastInfoTime = Time.time;
            Shared.target = AITarget.Player;
            Shared.position = pos;
            Shared.velocity = AITarget.Player.Velocity;
            Shared.confidence = 1f;
            Shared.time = Time.time;
            Shared.lastSeenTime = Time.time;
            Shared.everSeen = true;
            Shared.source = BeliefSource.Seen;
            // The picture of him goes with it: "in the car" (17 Nov AI playtest: a stale read said "on foot", so men
            // whose best belief was the shared one stopped the car assault every other frame)
            Shared.read = PlayerRead.Current;
            Shared.AddTrail(pos);
            if (Phase == AlertPhase.Searching || Phase == AlertPhase.Lost) Search?.Recenter(pos);
        }

        /// <summary>A member asks for at least this phase (never lowers combat by itself).</summary>
        public void Raise(AlertPhase p, Vector3? center = null)
        {
            if (Rank(p) <= Rank(Phase) && !(p == AlertPhase.Searching && Phase == AlertPhase.Lost)) { lastInfoTime = Time.time; return; }
            SetPhase(p, center);
        }

        private static int Rank(AlertPhase p)
        {
            switch (p)
            {
                case AlertPhase.Calm: return 0;
                case AlertPhase.Guarded: return 1;
                case AlertPhase.Suspicious: return 2;
                case AlertPhase.Searching: return 3;
                case AlertPhase.Lost: return 4;
                default: return 5;
            }
        }

        public static string PhaseName(AlertPhase p)
        {
            switch (p)
            {
                case AlertPhase.Suspicious: return "şüphe";
                case AlertPhase.Searching: return "arama";
                case AlertPhase.Combat: return "çatışma";
                case AlertPhase.Lost: return "kayıp";
                case AlertPhase.Guarded: return "tetikte";
                default: return "sakin";
            }
        }

        // ------------------------------------------------------------------ per frame (3.3 step 4)

        public void Tick()
        {
            if (_tickFrame == Time.frameCount) return;
            _tickFrame = Time.frameCount;
            Members.RemoveAll(m => m == null);
            var p = AIDifficulty.For(this);
            float now = Time.time;
            if (!_grenadesHanded && now - _created > 0.5f) HandOutGrenades(p);

            UpdatePhase(p, now);
            if (now >= _nextPower)
            {
                _nextPower = now + 1f;
                UpdatePower();
                UpdateAllied();
                UpdateEncounterMemory();
                UpdateDeadlock();
            }
            if (now >= _nextMissing)
            {
                _nextMissing = now + 1f;
                UpdateMissing(p);
            }
            if (now >= _nextTactic)
            {
                _nextTactic = now + 2f;
                SquadTactics.Choose(this);
            }
            Search?.Tick();
        }

        private void UpdatePhase(AIDifficulty.Profile p, float now)
        {
            float since = now - PhaseSince;
            float quiet = now - lastInfoTime;
            switch (Phase)
            {
                case AlertPhase.Suspicious:
                    if (quiet > SuspiciousCalmAfter) SetPhase(everFought || everSearched ? AlertPhase.Guarded : AlertPhase.Calm);
                    break;
                case AlertPhase.Combat:
                    if (BestConfidence < EnemyBelief.Guess)
                    {
                        if (_lostSince < 0f) _lostSince = now;
                        if (now - _lostSince > LostAfter)
                        {
                            _lostSince = -1f;
                            SetPhase(AlertPhase.Lost, Shared.Valid ? Shared.position : (Vector3?)null);
                            foreach (var m in Members) if (m != null && !m.IsDead) { m.Barks.Say(BarkKind.Lost, Shared); break; }
                        }
                    }
                    else _lostSince = -1f;
                    break;
                case AlertPhase.Lost:
                    if (since > p.searchTime) EndSearch();
                    break;
                case AlertPhase.Searching:
                    if (since > p.searchTime * 0.5f) EndSearch();
                    break;
            }
        }

        private void EndSearch()
        {
            // The last one hiding never says all clear (21.2)
            bool lastHider = Alive == 1 && Tactic == SquadTactic.BreakContact;
            if (!lastHider)
                foreach (var m in Members) if (m != null && !m.IsDead) { m.Barks.Say(BarkKind.AllClear); break; }
            SetPhase(AlertPhase.Guarded);
            AIDebug.Log(this, "arama bitti");
        }

        /// <summary>Power balance (12.3), 1 Hz.</summary>
        private void UpdatePower()
        {
            float power = 0f;
            foreach (var m in Members)
            {
                if (m == null || m.IsDead || m.Surrendered) continue;
                float v = m.Health01 * (m.HasAnyAmmo || m.IsMeleeRole ? 1f : 0.5f) * (m.Nerve.Level < NerveLevel.Pinned ? 1f : 0.4f);
                power += v;
            }
            RecentKills.RemoveAll(t => Time.time - t > 20f);
            float threat = 1f + RecentKills.Count * 0.8f;
            var b = Shared.Valid ? Shared : null;
            foreach (var m in Members) if (m != null && !m.IsDead && m.Belief.Primary != null) { b = m.Belief.Primary; break; }
            var read = b != null ? b.read : PlayerRead.Current;
            if (b != null && CoverMap.IsPositionCovered(b.position, home)) threat += 0.3f;
            if (read.shieldRaised) threat += 0.3f;
            // A moving car is a threat; a stopped or crawling one is a chance to drag him out (EN SON 16). 17 Nov AI
            // playtest: with +0.5 for any car a lone attacker fell to "broken" (1 / 1.5 < 0.7) and fled from a parked
            // car within 4 s instead of beating on it.
            if (read.inVehicle && !VehicleThreat.CanAssault()) threat += 0.5f;
            if (read.health01 > 0f && read.health01 < 0.35f) threat -= 0.5f;
            if (read.action == Combat.CombatAction.Reload || read.hand == PlayerHand.None) threat -= 0.3f;
            threat = Mathf.Max(threat, 0.5f);
            Power = power / threat;

            Stance want = Power >= DominantAt ? Stance.Dominant : Power >= EvenAt ? Stance.Even : Power >= WeakAt ? Stance.Weak : Stance.Broken;
            if (want == Stance) return;
            if (Time.time - _stanceSince < MinStanceTime) return;
            // Hysteresis
            if (Stance == Stance.Dominant && Power > DominantAt - 0.2f) return;
            if (Stance == Stance.Broken && Power < WeakAt + 0.2f) return;
            if (Stance == Stance.Even && Power > EvenAt - 0.2f && Power < DominantAt + 0.2f && want != Stance.Broken) return;
            if (Stance == Stance.Weak && Power > WeakAt - 0.2f && Power < EvenAt + 0.2f) return;
            AIDebug.Log(this, $"duruş {StanceName(Stance)} -> {StanceName(want)} (P {Power:0.0})");
            Stance = want;
            _stanceSince = Time.time;
        }

        public static string StanceName(Stance s)
        {
            switch (s)
            {
                case Stance.Dominant: return "üstün";
                case Stance.Weak: return "zayıf";
                case Stance.Broken: return "çökmüş";
                default: return "dengeli";
            }
        }

        /// <summary>Two squads fighting the same target within 30 m share command (12.1).</summary>
        private void UpdateAllied()
        {
            Allied.Clear();
            if (Phase != AlertPhase.Combat) return;
            foreach (var s in Squads)
            {
                if (s == this || s.Phase != AlertPhase.Combat) continue;
                bool near = false;
                foreach (var a in s.Members)
                {
                    if (a == null || a.IsDead) continue;
                    foreach (var b in Members)
                        if (b != null && !b.IsDead && (a.transform.position - b.transform.position).sqrMagnitude < 900f) { near = true; break; }
                    if (near) break;
                }
                if (!near) continue;
                foreach (var a in s.Members) if (a != null && !a.IsDead) Allied.Add(a);
            }
        }

        private void UpdateEncounterMemory()
        {
            if (!AIDifficulty.For(this).encounterMemory || Phase != AlertPhase.Combat) return;
            var b = BestBelief(Members.Count > 0 ? Members[0] : null);
            if (b == null || b.confidence < 0.7f) return;
            if ((b.position - _lastSpot).sqrMagnitude > 9f)
            {
                _lastSpot = b.position;
                _spotSince = Time.time;
                return;
            }
            if (Time.time - _spotSince >= 5f)
            {
                bool visited = false;
                foreach (var v in _spotsVisited) if ((v - _lastSpot).sqrMagnitude < 9f) { visited = true; break; }
                if (visited && sameSpot == 0) { sameSpot = 1; AIDebug.Log(this, "aynı yere döndü"); }
                if (!visited) _spotsVisited.Add(_lastSpot);
                _spotSince = Time.time + 999f;
            }
        }

        /// <summary>The whole squad pinned for 6 s: the bravest one comes to (U23).</summary>
        private void UpdateDeadlock()
        {
            bool all = Phase == AlertPhase.Combat && Alive > 0;
            EnemyController bravest = null;
            foreach (var m in Members)
            {
                if (m == null || m.IsDead) continue;
                if (m.Nerve.Level < NerveLevel.Pinned) { all = false; break; }
                if (bravest == null || m.Nerve.courage > bravest.Nerve.courage) bravest = m;
            }
            if (!all) { _allBrokenSince = -1f; return; }
            if (_allBrokenSince < 0f) _allBrokenSince = Time.time;
            if (Time.time - _allBrokenSince > 6f && bravest != null)
            {
                bravest.Nerve.Set(bravest.Nerve.PinnedAt - 5f);
                _allBrokenSince = -1f;
            }
        }

        /// <summary>Missing mate (7.3, missingCheck).</summary>
        private void UpdateMissing(AIDifficulty.Profile p)
        {
            foreach (var m in Members)
            {
                if (m == null) continue;
                bool seen = false;
                foreach (var o in Members)
                {
                    if (o == null || o == m || o.IsDead) continue;
                    if (!m.IsDead && o.Senses.SeesMate(m)) { seen = true; break; }
                }
                if (seen || !LastSeenByMate.ContainsKey(m))
                {
                    LastSeenByMate[m] = Time.time;
                    LastSeenAt[m] = m.transform.position;
                }
            }
            // Mates removed (destroyed) also count: they are kept in LastSeenByMate until noticed
            if (p.missingCheck <= 0f || Phase == AlertPhase.Combat) return;
            EnemyController missing = null;
            foreach (var kv in LastSeenByMate)
            {
                if (Time.time - kv.Value < p.missingCheck) continue;
                if (kv.Key != null && !kv.Key.IsDead && kv.Key.isActiveAndEnabled) continue;
                missing = kv.Key;
                break;
            }
            if (ReferenceEquals(missing, null)) return;
            Vector3 at = LastSeenAt.TryGetValue(missing, out var pos) ? pos : home;
            string n = missing != null ? missing.DisplayName : "";
            LastSeenByMate.Remove(missing);
            LastSeenAt.Remove(missing);
            EnemyController asker = null;
            float best = float.MaxValue;
            foreach (var m in Members)
            {
                if (m == null || m.IsDead) continue;
                float d = (m.transform.position - at).sqrMagnitude;
                if (d < best) { best = d; asker = m; }
            }
            if (asker == null) return;
            asker.LastLostMateName = n;
            asker.Barks.Say(BarkKind.Missing);
            asker.GoLookAt(at);
            Raise(AlertPhase.Suspicious, at);
            AIDebug.Log(this, "eksik arkadaşı fark ettiler");
        }

        // ------------------------------------------------------------------ shouts (11)

        /// <summary>A member's shout went out: the squad phase follows (the speaker's own team).</summary>
        public void OnOwnShout(BarkKind kind, Belief info, EnemyController speaker) => ApplyShout(kind, info, speaker);

        /// <summary>The information a shout carries, applied to this squad (Told).</summary>
        public void ApplyShout(BarkKind kind, Belief info, EnemyController speaker)
        {
            lastInfoTime = Time.time;
            if (info != null && info.target != null)
            {
                float conf = info.confidence * 0.8f;
                if (!Shared.Valid || conf > Shared.confidence * 0.5f || Time.time - Shared.time > 3f)
                {
                    Shared.target = info.target;
                    Shared.position = info.position;
                    Shared.velocity = info.velocity;
                    Shared.confidence = Mathf.Max(conf, Shared.Valid ? Shared.confidence : 0f);
                    Shared.time = Time.time;
                    Shared.read = info.read;
                    Shared.source = BeliefSource.Told;
                    if (info.everSeen) Shared.lastSeenTime = info.lastSeenTime;
                }
            }
            switch (kind)
            {
                case BarkKind.Contact: Raise(AlertPhase.Combat); break;
                case BarkKind.ShotsHeard: case BarkKind.Missing: Raise(AlertPhase.Suspicious, info?.position); break;
                case BarkKind.Evidence: Raise(AlertPhase.Searching, info?.position); break;
                case BarkKind.Lost:
                    if (Phase == AlertPhase.Combat) { Shared.confidence = Mathf.Min(Shared.confidence, 0.2f); }
                    break;
                case BarkKind.PlayerReloading: pushUntil = Time.time + 4f; break;
                case BarkKind.ToVehicle: vehicleEscape = Mathf.Max(vehicleEscape, AIDifficulty.For(this).encounterMemory ? 1 : 0); KnownVehicle = true; break;
                case BarkKind.LeaderDown: break;
                case BarkKind.AllClear: if (Phase == AlertPhase.Searching || Phase == AlertPhase.Lost) SetPhase(AlertPhase.Guarded); break;
                case BarkKind.GrenadeSeen: scatterUntil = Time.time + 3f; break;
            }
        }

        // ------------------------------------------------------------------ events from members

        public void MemberDied(EnemyController e, bool byPlayer)
        {
            if (byPlayer)
            {
                killedByPlayer++;
                RecentKills.Add(Time.time);
            }
            if (e == leader)
            {
                TierPenalty = 1;
                flankLockUntil = Time.time + 10f;
                AIDebug.Log(this, "lider öldü");
                foreach (var m in Members)
                {
                    if (m == null || m.IsDead || m == e) continue;
                    m.Nerve.Add(EnemyNerve.LeaderDeathGain);
                }
                foreach (var m in Members) if (m != null && !m.IsDead && m != e) { m.Barks.Say(BarkKind.LeaderDown); break; }
            }
            SquadTactics.Assign(this);
        }

        // ------------------------------------------------------------------ tokens (12.8)

        public int TokenLimit
        {
            get
            {
                int n = AIDifficulty.For(this).maxAttackers;
                if (Tactic == SquadTactic.Push) n++;
                return Mathf.Max(1, n);
            }
        }

        public static int GlobalCap => AIDifficulty.P.maxAttackers + 1;

        public static int TokenCount
        {
            get
            {
                int n = 0;
                foreach (var s in Squads) { s.CleanupTokens(); n += s._holders.Count; }
                return n;
            }
        }

        public static bool HasToken(EnemyController e) => e != null && e.Squad != null && e.Squad._holders.Contains(e);

        private int CommandHolders()
        {
            int n = _holders.Count;
            foreach (var s in Squads)
            {
                if (s == this) continue;
                foreach (var h in s._holders) if (Allied.Contains(h)) n++;
            }
            return n;
        }

        /// <summary>Asks for the right to shoot / strike (priority: suppressor, flanker in place, assault).</summary>
        public static bool RequestToken(EnemyController e, bool ignoreSquadLimit = false)
        {
            var s = e.Squad;
            if (s == null) return false;
            s.CleanupTokens();
            if (s._holders.Contains(e))
            {
                if (!s._heldSince.TryGetValue(e, out float since)) s._heldSince[e] = since = Time.time;
                if (Time.time - since > HoldTime && s.WaitingOthers(e))
                {
                    ReleaseToken(e);
                    return false;
                }
                return true;
            }
            if (NextRequest.TryGetValue(e, out float next) && Time.time < next) return false;
            if (TokenCount >= GlobalCap) return false;
            if (!ignoreSquadLimit && s.CommandHolders() >= s.TokenLimit)
            {
                // A higher priority role may take a lower one's token
                var role = s.RoleOf(e);
                if (role != SquadRole.Suppressor) return false;
                EnemyController victim = null;
                foreach (var h in s._holders) if (s.RoleOf(h) != SquadRole.Suppressor) { victim = h; break; }
                if (victim == null) return false;
                ReleaseToken(victim);
            }
            s._holders.Add(e);
            s._heldSince[e] = Time.time;
            return true;
        }

        public static void ReleaseToken(EnemyController e)
        {
            if (e == null) return;
            var s = e.Squad;
            bool had = false;
            if (s != null) had = s._holders.Remove(e);
            else foreach (var q in Squads) had |= q._holders.Remove(e);
            if (!had) return;
            s?._heldSince.Remove(e);
            NextRequest[e] = Time.time + RequestCooldown;
        }

        private bool WaitingOthers(EnemyController holder)
        {
            foreach (var other in Members)
                if (other != null && other != holder && !other.IsDead && other.IsAlerted && !_holders.Contains(other) && !other.IsMeleeRole == !holder.IsMeleeRole) return true;
            return false;
        }

        private void CleanupTokens()
        {
            for (int i = _holders.Count - 1; i >= 0; i--)
            {
                var h = _holders[i];
                if (h == null || h.IsDead || !h.isActiveAndEnabled || !h.IsAlerted || h.Nerve.Level >= NerveLevel.Pinned)
                {
                    _holders.RemoveAt(i);
                    if (h != null) _heldSince.Remove(h);
                }
            }
        }

        public int HolderCount { get { CleanupTokens(); return _holders.Count; } }

        private static void Cleanup()
        {
            for (int i = Squads.Count - 1; i >= 0; i--)
            {
                Squads[i].Members.RemoveAll(m => m == null);
                if (Squads[i].Members.Count == 0 && !Squads[i].IsAbstract) Squads.RemoveAt(i);
            }
        }

        public static void ResetAll()
        {
            Squads.Clear();
            NextRequest.Clear();
        }

        // ------------------------------------------------------------------ tactic state (set by SquadTactics)

        public void SetTactic(SquadTactic t)
        {
            if (Tactic == t) return;
            Tactic = t;
            TacticSince = Time.time;
            string n = SquadTactics.Name(t);
            if (Phase == AlertPhase.Combat && !TacticsTried.Contains(n)) TacticsTried.Add(n);
            AIDebug.Log(this, "taktik: " + n);
            SquadTactics.Assign(this);
        }

        public float TacticAge => Time.time - TacticSince;

        public bool Pushing => Tactic == SquadTactic.Push;
    }
}
