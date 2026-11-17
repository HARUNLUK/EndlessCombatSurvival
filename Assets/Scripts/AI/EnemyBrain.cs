using UnityEngine;
using EndlessCombat.Combat;

namespace EndlessCombat.AI
{
    /// <summary>
    /// Decisions of one enemy in combat and in a search (Enemy_AI_Deep_Plan.md 13). Every decideMin..decideMax
    /// seconds the valid options are scored (13.2); the best wins (the running one gets a +1.5 steadiness bonus),
    /// with mistakeChance the second best. The brain reads only the belief (never the real target position), the
    /// squad (tactic, role, tokens), the nerve, the actor and PlayerRead as last seen. EnemyController carries the
    /// result out every frame (move, face, aim, fire, strike).
    /// </summary>
    public class EnemyBrain
    {
        public enum Role { Rifle, Pistol, Melee, Shield }
        public enum Option
        {
            Wait, Flee, Surrender, EvadeGrenade, EvadeVehicle, Heal, Reload, ToCover, PeekFire, OpenFire, Suppress,
            Advance, Retreat, Flank, HoldAngle, ThrowGrenade, ThrowRock, Pickup, ShieldAdvance, ShieldBash,
            MeleeApproach, MeleeCharge, GuardSpot, SearchPoint, CarAssault, Count
        }

        // ---- tuning (panel page "Düşman") ----
        public static Vector2 RifleBand = new Vector2(12f, 20f);
        public static Vector2 PistolBand = new Vector2(7f, 12f);
        public static Vector2 ShieldPistolBand = new Vector2(4f, 9f);
        public static float CoverRadius = 25f;
        public static Vector2 PeekTime = new Vector2(1f, 2f);
        public static Vector2 HideTime = new Vector2(1.5f, 2.5f);
        public static Vector2 CircleRadius = new Vector2(4f, 6f);
        public static float ShieldTurnSpeed = 70f;
        public static float WoundedFraction = 0.25f;
        public static bool UseCover = true;
        public static float Steadiness = 1.5f;
        public static readonly float[] OptionMul = NewMuls();

        private static float[] NewMuls()
        {
            var a = new float[(int)Option.Count];
            for (int i = 0; i < a.Length; i++) a[i] = 1f;
            return a;
        }

        private readonly EnemyController _c;
        private float _nextDecide;
        private readonly float[] _score = new float[(int)Option.Count];

        // cover state
        private CoverPoint _cover;
        private Vector3 _peekSpot;
        private int _lastPeekSide;
        private float _nextCoverSearch;
        private float _peekUntil, _hideUntil;
        private bool _peekAllowed = true;
        private float _outOfBandSince = -1f;
        // movement state
        private float _orbitAngle, _orbitDir = 1f, _circleRadius;
        private CoverPoint _flankSpot;
        private int _flankSide;
        private bool _flankBarked;
        private float _flankDropUntil;
        private CoverPoint _boundSpot;
        private float _boundPauseUntil;
        private float _zigAt;
        private float _zigSign = 1f;
        private float _waitedForCharge;
        private EnemySearch.Point _searchPoint;
        private float _searchLookUntil;
        private float _breakContactSince = -1f;
        private float _suppressMag = -1;
        private int _suppressShots;
        private Vector3 _sideStep;
        private float _sideStepUntil;

        // ---- decision (read by EnemyController every frame) ----
        public Role CurrentRole { get; private set; }
        public Option Choice { get; private set; } = Option.Wait;
        public float ChoiceScore { get; private set; }
        public Option Second { get; private set; } = Option.Wait;
        public float SecondScore { get; private set; }
        public Vector3 Goal { get; private set; }
        public bool Moving { get; private set; }
        public float Speed { get; private set; }
        /// <summary>Face this point (null: face the walking direction).</summary>
        public Vector3? FacePoint { get; private set; }
        public bool WantsFire { get; private set; }
        public Vector3 FireAt { get; private set; }
        public float FireSpreadMul { get; private set; } = 1f;
        public bool Suppressing { get; private set; }
        public bool Guard { get; private set; }
        public bool HasToken { get; private set; }
        public bool InCover { get; private set; }
        public CoverPoint Cover => _cover;
        public bool ChargeNow { get; private set; }
        public bool Bash { get; private set; }
        public bool Heavy { get; private set; }

        public EnemyBrain(EnemyController c)
        {
            _c = c;
            _orbitDir = Random.value < 0.5f ? -1f : 1f;
            _orbitAngle = Random.Range(0f, 360f);
            _circleRadius = Random.Range(CircleRadius.x, CircleRadius.y);
            _nextDecide = Time.time + Random.Range(0f, 0.3f); // staggered first decision (23)
        }

        public Role ResolveRole()
        {
            if (_c.HasUsableShield) return Role.Shield;
            switch (_c.WeaponKind)
            {
                case WeaponType.Rifle: return _c.OutOfAmmo ? Role.Melee : Role.Rifle;
                case WeaponType.Pistol: return _c.OutOfAmmo ? Role.Melee : Role.Pistol;
                default: return Role.Melee;
            }
        }

        public void Reset()
        {
            ReleaseToken();
            ReleaseCover();
            Moving = false;
            Guard = false;
            WantsFire = false;
            _flankSpot = null;
            _searchPoint = null;
            Choice = Option.Wait;
        }

        public void StrikeDone()
        {
            ReleaseToken();
            _waitedForCharge = 0f;
            _nextDecide = 0f;
        }

        public void DecideNow() => _nextDecide = 0f;

        public float TurnSpeed => CurrentRole == Role.Shield && Guard ? ShieldTurnSpeed : 540f;

        // ------------------------------------------------------------------ the loop (13.1)

        public void Tick()
        {
            if (Time.time < _nextDecide) return;
            var p = AIDifficulty.For(_c.Squad);
            _nextDecide = Time.time + Random.Range(p.decideMin, p.decideMax);
            CurrentRole = ResolveRole();

            var s = _c.Squad;
            Belief b = s != null ? s.BestBelief(_c) : _c.Belief.Primary;
            Ctx x = BuildContext(b, p);

            UpdateToken(x);
            Score(x, p);

            // best and second (steadiness bonus for the running option)
            int best = -1, second = -1;
            float bestS = 0f, secondS = 0f;
            for (int i = 0; i < _score.Length; i++)
            {
                float v = _score[i];
                if (v <= 0f) continue;
                v *= OptionMul[i];
                if (i == (int)Choice) v += Steadiness;
                if (v > bestS)
                {
                    second = best; secondS = bestS;
                    best = i; bestS = v;
                }
                else if (v > secondS)
                {
                    second = i; secondS = v;
                }
            }
            if (best < 0) { best = (int)Option.Wait; bestS = 1f; }
            Option pick = (Option)best;
            bool urgent = pick == Option.Flee || pick == Option.Surrender || pick == Option.EvadeGrenade || pick == Option.EvadeVehicle;
            if (!urgent && second >= 0 && Random.value < p.mistakeChance) pick = (Option)second;
            // A running action (throw, heal, pickup) keeps its option; only movement may change
            if (_c.Actor.Busy && (Choice == Option.ThrowGrenade || Choice == Option.Heal || Choice == Option.Pickup || Choice == Option.ThrowRock)) pick = Choice;
            Second = second >= 0 ? (Option)second : Option.Wait;
            SecondScore = secondS;
            ChoiceScore = bestS;
            if (pick != Choice) OnLeave(Choice, pick);
            Choice = pick;
            Execute(pick, x, p);
        }

        private void OnLeave(Option from, Option to)
        {
            if (from == Option.SearchPoint && _searchPoint != null && to != Option.SearchPoint) _searchPoint.claimedBy = null;
        }

        // ------------------------------------------------------------------ context

        private struct Ctx
        {
            public Belief b;
            public bool has;
            public Vector3 threat;      // estimated (movement)
            public Vector3 chase;       // where to walk to follow the target: its seen path while unseen
            public float unseenFor;     // seconds since this enemy itself saw the target
            public Vector3 seenAt;      // last known (fire)
            public float conf;
            public bool seen;
            public float dist;
            public Vector2 band;
            public bool inCover;
            public bool underFire;
            public PlayerRead.Snapshot read;
            public bool lookingAway;
            public NerveLevel nerve;
            public SquadTactic tactic;
            public SquadRole role;
            public bool gunner, melee;
            public bool friend15;
        }

        private Ctx BuildContext(Belief b, AIDifficulty.Profile p)
        {
            var x = new Ctx { b = b };
            Vector3 me = _c.transform.position;
            x.has = b != null && b.Valid;
            x.threat = x.has ? b.EstimatedWith(p.predictSeconds) : me + _c.transform.forward * 10f;
            x.seenAt = x.has ? b.position : x.threat;
            x.conf = x.has ? b.confidence : 0f;
            x.seen = x.has && b.SeenNow;
            x.dist = Flat(x.threat - me).magnitude;
            // Unseen: follow the seen path, unless newer news (heard, told) put him somewhere else
            bool newerNews = x.has && b.time > b.lastSeenTime + 0.5f;
            x.chase = x.has && !x.seen && !newerNews ? PursuitPoint(b, x.threat) : x.threat;
            var own = x.has ? _c.Belief.Of(b.target) : null;
            x.unseenFor = own != null && own.everSeen ? own.SinceSeen : (x.has ? b.Age + 3f : 0f);
            x.read = x.has ? b.read : PlayerRead.Current;
            if (x.has)
            {
                Vector3 toMe = Flat(me - x.seenAt);
                x.lookingAway = toMe.sqrMagnitude > 0.01f && Vector3.Angle(Flat(x.read.facing), toMe) > 70f;
            }
            x.nerve = _c.Nerve.Level;
            var s = _c.Squad;
            x.tactic = s != null ? s.Tactic : SquadTactic.Hold;
            x.role = s != null ? s.RoleOf(_c) : SquadRole.None;
            x.gunner = _c.IsGunner;
            x.melee = !x.gunner && CurrentRole != Role.Shield;
            x.underFire = _c.UnderFire(2f);
            x.friend15 = s != null && s.FriendWithin(_c, 15f);
            x.band = Band(x, p);
            x.inCover = _cover != null && Flat(_cover.position - me).magnitude < 0.9f && CoverMap.Protects(_cover, x.threat);
            InCover = x.inCover;
            return x;
        }

        /// <summary>
        /// Follow the target's seen path (Belief.trail): walk to the furthest trail point in a straight clear line
        /// from here; when no point is in sight, join the trail at the nearest point (NavMesh route). At the last seen
        /// point the straight guess (last speed) takes over.
        /// </summary>
        private Vector3 PursuitPoint(Belief b, Vector3 guess)
        {
            var t = b.trail;
            if (t == null || t.Count == 0) return guess;
            Vector3 me = _c.transform.position;
            Vector3 last = t[t.Count - 1];
            if (Flat(last - me).magnitude < 1.5f) return guess;
            Vector3 eye = me + Vector3.up;
            int nearest = 0;
            float nearestD = float.MaxValue;
            for (int i = t.Count - 1; i >= 0; i--)
            {
                float d = Flat(t[i] - me).magnitude;
                if (d < nearestD) { nearestD = d; nearest = i; }
                if (d > 35f) continue;
                if (!CoverMap.Blocked(eye, t[i] + Vector3.up)) return i == t.Count - 1 ? last : t[i];
            }
            return t[nearest];
        }

        private Vector2 Band(Ctx x, AIDifficulty.Profile p)
        {
            Vector2 band = CurrentRole == Role.Rifle ? RifleBand : CurrentRole == Role.Pistol ? PistolBand : ShieldPistolBand;
            var s = _c.Squad;
            if (x.tactic == SquadTactic.Push) band.x *= 0.5f;
            if (s != null && s.leader == _c) band.y += 5f;
            var cycle = EndlessSurvival.World.DayNightCycle.Instance;
            var w = EndlessSurvival.Survival.WeatherSystem.Instance;
            if ((cycle != null && cycle.IsNight) || (w != null && w.Current == EndlessSurvival.Survival.WeatherType.Fog && w.Intensity > 0.4f)) band *= 0.7f;
            if (s != null && s.longRange >= 2) band *= 0.7f;
            if (AIMemory.Habit(AIMemory.HabitKind.LongRange) >= 0.5f) band *= 0.8f;
            return band;
        }

        private void UpdateToken(Ctx x)
        {
            bool pinned = x.nerve >= NerveLevel.Pinned && _c.Nerve.Panic != EnemyNerve.PanicKind.Charge;
            bool want;
            if (pinned || !x.has || _c.Surrendered) want = false;
            else if (x.gunner) want = x.conf > EnemyBelief.Guess && x.dist <= _c.attackRange * 1.2f;
            else want = x.dist < 14f;
            if (_c.Squad != null && _c.Squad.RoleOf(_c) == SquadRole.Flanker && _flankSpot != null && !AtSpot(_flankSpot.position, 1.2f)) want = false;
            if (want) HasToken = EnemySquad.RequestToken(_c);
            else ReleaseToken();
        }

        // ------------------------------------------------------------------ scoring (13.2)

        private void Score(Ctx x, AIDifficulty.Profile p)
        {
            System.Array.Clear(_score, 0, _score.Length);
            var s = _c.Squad;
            var nerve = _c.Nerve;
            bool pinned = x.nerve >= NerveLevel.Pinned;
            bool push = x.tactic == SquadTactic.Push;
            bool searching = _c.State == EnemyController.EnemyState.Searching;

            _score[(int)Option.Wait] = 1f;

            // Flee / surrender
            // "Seen" here includes the last second: the sight flag drops for a frame between sense ticks, and a man
            // 5 m from the player then chose to flee instead, turned his back, and never saw him again to give up
            // (17 Nov playtest, surrender scenario failed now and then).
            bool surrender = x.nerve == NerveLevel.Panic && _c.Health01 < 0.5f && (x.seen || x.unseenFor < 1f) && x.dist < 12f && s?.leader != _c
                             && (s == null || s.AlliesFighting(_c, 60f) == 0);
            if (surrender) _score[(int)Option.Surrender] = 9.5f;
            _fightingRetreat = false;
            if (!surrender && (x.tactic == SquadTactic.Rout || nerve.Panic == EnemyNerve.PanicKind.Flee
                || (_c.Health01 < WoundedFraction && !x.friend15)))
            {
                // A man with a loaded gun does not turn his back and run (user bug report 16 Nov): he backs off to
                // cover and keeps shooting. He runs only when badly hurt AND the player is armed with a gun.
                bool armed = x.gunner && _c.HasAnyAmmo;
                bool playerHasGun = x.read.hand == PlayerHand.Pistol || x.read.hand == PlayerHand.Rifle;
                if (armed && (!playerHasGun || _c.Health01 >= WoundedFraction))
                {
                    _fightingRetreat = true;
                    _score[(int)Option.Retreat] = 7f;
                }
                else _score[(int)Option.Flee] = 9f;
            }

            if (_c.GrenadeThreat.HasValue && Flat(_c.GrenadeThreat.Value - _c.transform.position).magnitude < EnemyThrower.BlastRadius + 2f)
                _score[(int)Option.EvadeGrenade] = 10f;
            if (VehicleThreat.Charging(_c, out _)) _score[(int)Option.EvadeVehicle] = 10f;

            if (searching && !x.seen)
            {
                if (s != null && s.RoleOf(_c) == SquadRole.Guard) _score[(int)Option.GuardSpot] = 7f;
                else _score[(int)Option.SearchPoint] = 6f;
                // Just heard him (footsteps behind a wall, the engine): go there now, round the wall, not the next search point
                if (x.has && (x.b.source == BeliefSource.Heard || x.b.source == BeliefSource.Told) && x.b.Age < 4f && x.conf >= 0.25f
                    && (s == null || s.RoleOf(_c) != SquadRole.Guard))
                    _score[(int)Option.Advance] = 7f;
                ScorePickup(x);
                return;
            }

            // Heal
            if (_c.Health01 < 0.5f && _c.bandages > 0 && !_c.UnderFire(5f) && (!x.has || CoverMap.IsPositionCovered(_c.transform.position, x.seenAt)))
                _score[(int)Option.Heal] = 6f + (0.5f - _c.Health01) * 4f - (HasToken ? 2f : 0f);

            // Reload
            if (x.gunner && _c.CanReload)
            {
                if (_c.MagazineEmpty) _score[(int)Option.Reload] = 8f + (x.inCover ? 1f : 0f);
                else if (_c.MagazineFraction <= 0.25f && x.inCover && !HasToken)
                {
                    float v = 5f + 1f;
                    if (p.staggerReload && s != null && s.SomeoneReloading(_c)) v -= 4f;
                    _score[(int)Option.Reload] = v;
                }
            }

            // Cover
            if (UseCover && !x.inCover && CurrentRole != Role.Melee && x.has && EnsureCover(x))
            {
                float v = 6f + (x.underFire ? 2f : 0f) + (_c.MagazineFraction <= 0.25f ? 2f : 0f) - (push ? 3f : 0f) - (CurrentRole == Role.Shield ? 4f : 0f);
                if (_c.ShieldWorn) v += 3f;
                _score[(int)Option.ToCover] = v;
            }

            if (CurrentRole == Role.Shield) ScoreShield(x, p);
            else if (x.melee) ScoreMelee(x, p);
            else ScoreGunner(x, p, pinned, push);

            // Roles
            if (s != null)
            {
                var role = s.RoleOf(_c);
                if (role == SquadRole.Flanker && !pinned && Time.time >= _flankDropUntil && x.has) _score[(int)Option.Flank] = 8f;
                if (role == SquadRole.Guard) _score[(int)Option.GuardSpot] = 7f;
            }
            if ((x.tactic == SquadTactic.FallBack || x.tactic == SquadTactic.Encircle || x.tactic == SquadTactic.BreakContact) && x.has)
                _score[(int)Option.HoldAngle] = 6f;

            ScoreGrenade(x, p, pinned);
            ScorePickup(x);

            // Panic kinds: charge runs in, freeze waits
            if (nerve.Panic == EnemyNerve.PanicKind.Charge)
            {
                _score[(int)Option.Advance] = 9f;
                _score[(int)Option.MeleeCharge] = x.melee ? 9f : 0f;
            }
            if (nerve.Frozen)
            {
                System.Array.Clear(_score, 0, _score.Length);
                _score[(int)Option.Wait] = 10f;
            }
        }

        private void ScoreGunner(Ctx x, AIDifficulty.Profile p, bool pinned, bool push)
        {
            if (!x.has) return;
            bool reloading = _c.IsReloading;
            if (!reloading && HasToken && !pinned)
            {
                // Peek from cover
                if (x.inCover && x.conf > EnemyBelief.Knows && Time.time >= _hideUntil)
                {
                    if (CoverMap.PeekSpot(_cover, x.seenAt, out Vector3 spot, AIDifficulty.Current >= AIDifficulty.Tier.Hard ? _lastPeekSide : 0))
                    {
                        _peekSpot = spot;
                        bool aimedAt = x.read.aiming && Vector3.Angle(x.read.aimDirection, spot - x.seenAt) < 8f;
                        if (aimedAt && Time.time >= _peekUntil) _peekAllowed = Random.value < p.peekUnderAimChance;
                        else if (!aimedAt) _peekAllowed = true;
                        if (_peekAllowed)
                        {
                            float v = 7f;
                            if (x.read.action == CombatAction.Reload || x.lookingAway) v += 2f;
                            _score[(int)Option.PeekFire] = v;
                        }
                    }
                }
                // Open fire without cover
                if (!x.inCover && x.conf > EnemyBelief.Knows && x.seen && x.dist <= _c.attackRange)
                    _score[(int)Option.OpenFire] = 5f - (x.underFire ? 2f : 0f) + (_cover == null ? 1f : 0f);
                // Suppressive fire at a guessed position
                if (p.suppressFire && x.conf >= EnemyBelief.Guess && (x.conf <= EnemyBelief.Knows || !x.seen)
                    && _c.TotalAmmo >= 8 && _c.MagazineRounds > 2 && x.dist <= _c.attackRange && SuppressBudget())
                {
                    float v = 5f + (x.role == SquadRole.Suppressor ? 2f : 0f);
                    _score[(int)Option.Suppress] = v;
                }
            }
            // Shield raised in front: do not shoot into it, move around it
            if (x.read.shieldRaised && x.seen && Vector3.Angle(Flat(x.read.facing), Flat(_c.transform.position - x.seenAt)) < 50f)
            {
                _score[(int)Option.OpenFire] *= 0.3f;
                _score[(int)Option.PeekFire] *= 0.5f;
                _score[(int)Option.MeleeApproach] = 5.5f; // re-used: circle to the side
            }

            bool routeOk = _c.Mover.Route == EnemyMover.RouteState.Full || _c.Mover.Route == EnemyMover.RouteState.None
                           || _c.Mover.Route == EnemyMover.RouteState.NoNavMesh || !_c.Mover.Unreachable;
            if (!pinned && routeOk && (x.dist > x.band.y || x.conf < EnemyBelief.Knows)
                && !(x.tactic == SquadTactic.FixAndFlank && x.role == SquadRole.Suppressor)
                && x.tactic != SquadTactic.FallBack && x.tactic != SquadTactic.BreakContact)
            {
                float v = 4f + (push ? 3f : 0f);
                if (x.conf < EnemyBelief.Guess) v -= 1f;
                if (_c.Squad != null && _c.Squad.longRange >= 2 && _cover == null) v -= 2f;
                _score[(int)Option.Advance] = v;
            }
            // Keep the rifle's distance only from a target that is really there (17 Nov AI playtest: at a place just
            // found empty, conf 0.2, he backed away from nothing, then came back, ten times over a minute)
            if (!push && x.dist < x.band.x - 0.5f && (x.seen || x.conf >= EnemyBelief.Knows))
                _score[(int)Option.Retreat] = 6f + (x.tactic == SquadTactic.FallBack ? 3f : 0f);
            if (pinned && _cover != null) _score[(int)Option.HoldAngle] = 6f;

            // Lost sight of him for a while: do not sit behind a wall forever, go round it and find him (user bug
            // report 16 Nov). Grows with the time unseen; the route goes along his seen path / to the heard place.
            if (HuntsUnseen(x, pinned))
                _score[(int)Option.Advance] = Mathf.Max(_score[(int)Option.Advance], 7f + Mathf.Min(3f, (x.unseenFor - HuntAfter) * 0.4f));
        }

        public static float HuntAfter = 3f;

        /// <summary>A fixed offset per squad member (golden-angle ring), so men sent to one point stand apart.</summary>
        private Vector3 SpreadOffset(float radius)
        {
            var s = _c.Squad;
            int idx = s != null ? Mathf.Max(0, s.Members.IndexOf(_c)) : (_c.GetHashCode() & 7);
            if (idx == 0) return Vector3.zero;
            float a = idx * 137.5f * Mathf.Deg2Rad;
            float r = radius * Mathf.Sqrt(idx);
            return new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
        }

        private bool HuntsUnseen(Ctx x, bool pinned)
        {
            // 0.25: a looked-and-empty belief (0.2) no longer pulls him there
            if (!x.has || x.seen || pinned || x.unseenFor < HuntAfter || x.conf < 0.25f) return false;
            if (x.tactic == SquadTactic.FallBack || x.tactic == SquadTactic.BreakContact || x.tactic == SquadTactic.Rout) return false;
            if (_c.IsReloading || x.read.inVehicle && x.dist > 40f) return false;
            // Guards of the squad (car / objective) stay
            var s = _c.Squad;
            return s == null || s.RoleOf(_c) != SquadRole.Guard;
        }

        private void ScoreShield(Ctx x, AIDifficulty.Profile p)
        {
            if (!x.has) return;
            _score[(int)Option.ShieldAdvance] = 7f;
            if (p.shieldBash && HasToken && x.dist < 1.8f) _score[(int)Option.ShieldBash] = 8f;
            if (_c.WeaponKind == WeaponType.Pistol && !_c.OutOfAmmo && HasToken && x.seen && x.dist <= _c.attackRange && !_c.IsReloading)
                _score[(int)Option.OpenFire] = 6f;
            if (_c.WeaponKind == WeaponType.Melee || _c.WeaponKind == WeaponType.Unarmed || _c.OutOfAmmo)
            {
                if (HasToken && x.dist < 3f) _score[(int)Option.MeleeCharge] = 8f;
                if (WantsCarAssault(x)) _score[(int)Option.CarAssault] = 8f;
            }
        }

        // ------------------------------------------------------------------ car assault

        private float _atDoorSince = -1f;

        /// <summary>The target is in the car, the car is slow or stopped and near enough.</summary>
        private bool WantsCarAssault(Ctx x)
        {
            // Seen just now through the squad's picture counts too (the shared belief is not "SeenNow")
            bool fresh = x.seen || (x.b != null && x.b.source == BeliefSource.Seen && x.b.Age < 1.5f);
            if (!x.has || !AITargetRegistry.VehicleOccupied || !(x.read.inVehicle || fresh)) return false;
            if (x.b == null || x.b.target != AITarget.Player) return false;
            return VehicleThreat.CanAssault() && x.dist < VehicleThreat.AssaultRange;
        }

        /// <summary>This man's place in the line of attackers (driver door first).</summary>
        private int AssaultRank()
        {
            var s = _c.Squad;
            if (s == null) return 0;
            int rank = 0;
            foreach (var m in s.Members)
            {
                if (m == _c) break;
                if (m != null && !m.IsDead && m.Brain != null && m.Brain.Choice == Option.CarAssault) rank++;
            }
            return rank;
        }

        private void DoCarAssault(Ctx x, float speed)
        {
            var vt = AITarget.PlayerVehicle;
            Vector3 me = _c.transform.position;
            if (vt == null) { Set(me, 0f); return; }
            ReleaseCover();
            Vector3 spot = VehicleThreat.AssaultSpot(AssaultRank(), out int slot);
            FacePoint = vt.transform.position + Vector3.up;
            if (Flat(spot - me).magnitude > 1.3f)
            {
                _atDoorSince = -1f;
                Set(Snap(spot), speed * 1.15f);
                return;
            }
            Set(me, 0f);
            _c.StrikeCar(slot);
            // At the open driver door of a stopped car: grab him and drag him out
            if (slot == (int)EndlessSurvival.Survival.WindowSlot.Left && VehicleThreat.SlotOpen(slot))
            {
                if (_atDoorSince < 0f) _atDoorSince = Time.time;
                else if (Time.time - _atDoorSince >= VehicleThreat.PullOutSeconds && VehicleThreat.PullOut(_c))
                {
                    _atDoorSince = -1f;
                    _c.Barks?.Say(BarkKind.Custom, x.b, "İndirdim onu!");
                }
            }
            else _atDoorSince = -1f;
        }

        private void ScoreMelee(Ctx x, AIDifficulty.Profile p)
        {
            if (!x.has) return;
            var s = _c.Squad;
            // He sits in a slow or stopped car: go and beat on it, break in, drag him out (no standing around)
            if (WantsCarAssault(x))
            {
                _score[(int)Option.CarAssault] = 8f;
                return;
            }
            bool reachable = !_c.Mover.Unreachable && !x.read.inVehicle;
            // Melee too: lost sight -> go find him along his path
            if (reachable && HuntsUnseen(x, false))
            {
                _score[(int)Option.Advance] = 7.5f;
                return;
            }
            if (!reachable)
            {
                if (_c.rocks > 0 && EnemyThrower.CanThrowRock(_c, x.seenAt, out _)) _score[(int)Option.ThrowRock] = 6f;
                _score[(int)Option.HoldAngle] = 4f;
                return;
            }
            _score[(int)Option.MeleeApproach] = 5f;
            // Already at arm's length, or the player runs away: no circling, go (user bug report 16 Nov:
            // club men stood in front of the player without hitting, and could not keep up with a running player)
            float playerSpeed = x.b != null ? Flat(x.b.velocity).magnitude : 0f;
            if (x.seen && (x.dist < 3.5f || (playerSpeed > 3f && x.dist < 25f)))
            {
                if (!HasToken) HasToken = EnemySquad.RequestToken(_c, ignoreSquadLimit: true);
                _score[(int)Option.MeleeCharge] = 8.5f;
                if (x.dist >= 3.5f) _score[(int)Option.MeleeApproach] = 0f;
            }
            bool sheathed = p.exploitDraw && x.read.hand == PlayerHand.None && x.dist < 15f;
            if (!HasToken && sheathed) HasToken = EnemySquad.RequestToken(_c, ignoreSquadLimit: true);
            if (HasToken)
            {
                _waitedForCharge += Mathf.Max(0.05f, p.decideMax);
                bool moment = _waitedForCharge >= 1.5f;
                if (AIDifficulty.Current != AIDifficulty.Tier.Easy)
                    moment |= x.read.action == CombatAction.Reload || x.read.action == CombatAction.Draw || x.read.action == CombatAction.Stagger
                              || x.lookingAway || sheathed;
                // Double dive: wait for a 100 degree angle from the other diver (max 2 s)
                if (moment && p.maxAttackers >= 3 && s != null)
                {
                    foreach (var m in s.Members)
                    {
                        if (m == null || m == _c || m.IsDead || !m.IsMeleeRole || m.Brain == null || m.Brain.Choice != Option.MeleeCharge) continue;
                        float a = Vector3.Angle(Flat(m.transform.position - x.seenAt), Flat(_c.transform.position - x.seenAt));
                        if (a < 100f && _waitedForCharge < 5f) moment = false;
                    }
                }
                if (moment) _score[(int)Option.MeleeCharge] = Mathf.Max(_score[(int)Option.MeleeCharge], 8f);
            }
            if (_c.rocks > 0 && x.dist > 6f && x.read.inVehicle && EnemyThrower.CanThrowRock(_c, x.seenAt, out _)) _score[(int)Option.ThrowRock] = 6f;
        }

        private void ScoreGrenade(Ctx x, AIDifficulty.Profile p, bool pinned)
        {
            if (_c.grenades <= 0 || !x.has || _c.Actor.Busy) return;
            var s = _c.Squad;
            Vector3 target = x.seenAt;
            bool reason = false;
            float stillFor = s != null && s.sameSpot > 0 ? 3f : 8f;
            if (p.grenadeFlush && x.conf > 0.5f && x.read.stationary >= stillFor && CoverMap.IsPositionCovered(x.seenAt, _c.transform.position)) reason = true;
            if (p.grenadeFlush && _c.Mover.Unreachable && x.role == SquadRole.Grenadier) reason = true;
            if (p.grenadeCoverRetreat && (x.tactic == SquadTactic.FallBack || x.tactic == SquadTactic.BreakContact || (pinned && x.dist < 15f)))
            {
                Vector3 me = _c.transform.position;
                Vector3 dir = Flat(x.seenAt - me).normalized;
                target = me + dir * Mathf.Min(11f, x.dist);
                reason = x.dist < 18f;
            }
            if (p.vehicleDeny >= 2 && PlayerRead.Intent == PlayerIntent.ToVehicle && AITarget.PlayerVehicle != null
                && (AITarget.PlayerVehicle.Position - x.seenAt).magnitude < 8f)
            {
                target = Vector3.Lerp(AITarget.PlayerVehicle.Position, x.seenAt, 0.5f);
                reason = true;
            }
            if (_c.Health01 < 0.2f && !x.friend15 && _c.Nerve.aggression > 0.6f) reason = true;
            if (PlayerRead.Intent == PlayerIntent.Camping && x.conf > 0.5f && p.grenadeFlush) reason = true;
            if (!reason) return;
            if (!EnemyThrower.CanThrowGrenade(_c, target, out Vector3 v)) return;
            _grenadeTarget = target;
            _grenadeVelocity = v;
            _score[(int)Option.ThrowGrenade] = 8f;
        }

        private Vector3 _grenadeTarget, _grenadeVelocity;
        private bool _fightingRetreat;
        private EndlessSurvival.Inventory.WorldPickup _pickup;

        private void ScorePickup(Ctx x)
        {
            if (_c.Actor.Busy) return;
            _pickup = AIInterest.Find(_c, x.b, out float score);
            if (_pickup != null) _score[(int)Option.Pickup] = Mathf.Clamp(score, 3f, 8f);
        }

        private bool SuppressBudget()
        {
            if (_suppressMag != _c.MagazineGeneration)
            {
                _suppressMag = _c.MagazineGeneration;
                _suppressShots = 0;
            }
            return _suppressShots < 2;
        }

        public void NoteShotFired()
        {
            if (Suppressing) _suppressShots++;
            if (Choice == Option.PeekFire && Time.time > _peekUntil) EndPeek();
        }

        // ------------------------------------------------------------------ execution

        private void Execute(Option o, Ctx x, AIDifficulty.Profile p)
        {
            Vector3 me = _c.transform.position;
            float speed = _c.moveSpeed;
            WantsFire = false;
            Suppressing = false;
            FireSpreadMul = 1f;
            ChargeNow = false;
            Bash = false;
            Heavy = x.read.shieldRaised;
            FacePoint = x.has ? x.seenAt : (Vector3?)null;
            Guard = ShieldGuard(x);
            if (Time.time < _sideStepUntil)
            {
                Set(_sideStep, speed * 0.8f);
                return;
            }

            switch (o)
            {
                case Option.Flee:
                {
                    ReleaseToken();
                    ReleaseCover();
                    _c.BeginFlee();
                    Vector3 goal = FleeGoal(x);
                    Set(goal, speed * 1.3f);
                    FacePoint = null;
                    break;
                }
                case Option.Surrender:
                    ReleaseToken();
                    _c.DoSurrender();
                    Set(me, 0f);
                    break;
                case Option.EvadeGrenade:
                {
                    Vector3 g = _c.GrenadeThreat ?? me;
                    // Throw back (Brutal): close, time left, not pinned
                    if (p.grenadeThrowBack > 0f && _c.GrenadeObject != null && Flat(g - me).magnitude < 2f && x.nerve < NerveLevel.Pinned
                        && Random.value < p.grenadeThrowBack && x.has)
                    {
                        if (_c.TryThrowBack(x.seenAt)) { Set(me, 0f); break; }
                    }
                    Vector3 away = Flat(me - g);
                    if (away.sqrMagnitude < 0.01f) away = -_c.transform.forward;
                    Vector3 goal = g + away.normalized * (EnemyThrower.BlastRadius + 3f);
                    var c = x.has ? CoverMap.FindCover(_c, x.threat, 3f, Flat(goal - x.threat).magnitude, 12f) : null;
                    if (c != null && Flat(c.position - g).magnitude > EnemyThrower.BlastRadius + 2f) goal = c.position;
                    Set(Snap(goal), speed * 1.4f);
                    FacePoint = null;
                    break;
                }
                case Option.EvadeVehicle:
                {
                    VehicleThreat.Charging(_c, out Vector3 away);
                    Set(Snap(me + away * 7f), speed * 1.5f);
                    FacePoint = null;
                    break;
                }
                case Option.Heal:
                    ReleaseToken();
                    _c.StartHeal();
                    Set(me, 0f);
                    break;
                case Option.Reload:
                {
                    _c.StartReload();
                    if (_c.MagazineEmpty && !x.inCover && _cover != null && Flat(_cover.position - me).magnitude < 8f)
                        Set(_cover.position, speed * 1.2f);
                    else if (!x.inCover && (_cover == null || Flat(_cover.position - me).magnitude >= 8f))
                    {
                        // No cover close: back behind a shield bearer or a mate
                        var bearer = _c.Squad?.NearestShieldBearer(_c, 12f);
                        if (bearer != null) Set(bearer.transform.position + Flat(bearer.transform.position - x.threat).normalized * 1.6f, speed * 0.8f);
                        else Set(me, 0f);
                    }
                    else Set(_cover != null ? _cover.position : me, speed * 0.8f);
                    break;
                }
                case Option.ToCover:
                    Set(_cover.position, speed * 1.3f);
                    FacePoint = Flat(_cover.position - me).magnitude < 3f && x.has ? x.seenAt : (Vector3?)null;
                    break;
                case Option.PeekFire:
                {
                    if (Time.time >= _peekUntil && Time.time >= _hideUntil) _peekUntil = Time.time + Random.Range(PeekTime.x, PeekTime.y);
                    bool there = Flat(_peekSpot - me).magnitude < 0.6f;
                    Set(_peekSpot, speed * 0.8f);
                    WantsFire = there;
                    FireAt = AimPoint(x, p);
                    if (Time.time > _peekUntil) EndPeek();
                    break;
                }
                case Option.OpenFire:
                {
                    // Keep the band; strafe a little while shooting
                    Vector3 goal = me;
                    if (x.dist > _c.attackRange * 0.9f) goal = Vector3.MoveTowards(me, x.threat, 2f);
                    Set(goal, goal == me ? 0f : speed * 0.6f);
                    WantsFire = x.seen;
                    FireAt = AimPoint(x, p);
                    break;
                }
                case Option.Suppress:
                {
                    Vector3? edge = CoverMap.EdgeToward(x.seenAt, me);
                    FireAt = edge ?? (x.seenAt + Vector3.up * 1.1f);
                    WantsFire = true;
                    Suppressing = true;
                    FireSpreadMul = 2f;
                    Set(x.inCover && _cover != null ? (_peekSpot != Vector3.zero ? _peekSpot : _cover.position) : me, speed * 0.6f);
                    if (_c.Squad != null && Random.value < 0.2f) _c.Barks.Say(BarkKind.Covering);
                    break;
                }
                case Option.Advance:
                    DoAdvance(x, p, speed);
                    break;
                case Option.Retreat:
                {
                    var c = UseCover ? CoverMap.FindCover(_c, x.threat, x.band.x, (x.band.x + x.band.y) * 0.5f + (x.tactic == SquadTactic.FallBack ? 6f : 0f), 16f) : null;
                    if (c != null) { TakeCover(c); Set(c.position, speed * 1.1f); }
                    else Set(Snap(me + Flat(me - x.threat).normalized * 5f), speed * 0.8f);
                    // Backing off with a gun: shoot while moving when the target is in sight
                    if (x.gunner && x.seen && x.dist <= _c.attackRange && !_c.IsReloading)
                    {
                        WantsFire = true;
                        FireAt = AimPoint(x, p);
                        FireSpreadMul = _fightingRetreat ? 1.4f : 1.2f;
                    }
                    break;
                }
                case Option.Flank:
                    DoFlank(x, p, speed);
                    break;
                case Option.HoldAngle:
                    DoHoldAngle(x, p, speed);
                    break;
                case Option.ThrowGrenade:
                    _c.StartGrenadeThrow(_grenadeTarget, _grenadeVelocity);
                    Set(me, 0f);
                    FacePoint = _grenadeTarget;
                    break;
                case Option.ThrowRock:
                    if (EnemyThrower.CanThrowRock(_c, x.seenAt, out Vector3 rv)) _c.StartRockThrow(rv);
                    Set(_cover != null ? _cover.position : me, speed);
                    break;
                case Option.Pickup:
                {
                    if (_pickup == null) { Set(me, 0f); break; }
                    _pickup.claimedBy = _c;
                    Vector3 pos = _pickup.transform.position;
                    if (Flat(pos - me).magnitude < 1.2f)
                    {
                        _c.StartPickup(_pickup);
                        Set(me, 0f);
                    }
                    else Set(pos, speed * 1.2f);
                    FacePoint = null;
                    break;
                }
                case Option.ShieldAdvance:
                    DoShield(x, p, speed);
                    break;
                case Option.ShieldBash:
                    Set(x.threat, speed * 0.6f);
                    Bash = true;
                    break;
                case Option.MeleeApproach:
                    DoMeleeApproach(x, speed);
                    break;
                case Option.MeleeCharge:
                    ChargeNow = true;
                    if (_c.Barks != null && _waitedForCharge > 0f) _c.Barks.Say(BarkKind.Charging);
                    _waitedForCharge = 0f;
                    Set(x.chase, _c.moveSpeed * _c.meleeChargeSpeedMultiplier);
                    break;
                case Option.GuardSpot:
                    DoGuard(x, speed);
                    break;
                case Option.SearchPoint:
                    DoSearch(x, speed);
                    break;
                case Option.CarAssault:
                    DoCarAssault(x, speed);
                    break;
                default:
                    Set(_cover != null ? _cover.position : me, _cover != null ? speed * 0.6f : 0f);
                    break;
            }
        }

        private void EndPeek()
        {
            _hideUntil = Time.time + Random.Range(HideTime.x, HideTime.y);
            _lastPeekSide = _peekSpot == (_cover != null ? _cover.peekLeftPos : Vector3.zero) ? -1 : 1;
        }

        /// <summary>Aim point: the last seen place (center), lead for a running target (Hard+).</summary>
        private Vector3 AimPoint(Ctx x, AIDifficulty.Profile p)
        {
            Vector3 aim = x.seenAt + Vector3.up * (x.read.inVehicle ? 1.3f : 1.1f);
            if (p.leadTarget && x.has && x.seen)
            {
                float d = Vector3.Distance(_c.transform.position, x.seenAt);
                aim += x.b.velocity * (d / 300f);
            }
            if (x.read.inVehicle && p.vehicleDeny >= 2 && AITarget.PlayerVehicle != null)
                aim = VehicleThreat.AimPoint(_c, aim);
            return aim;
        }

        private bool ShieldGuard(Ctx x)
        {
            if (CurrentRole != Role.Shield) return false;
            if (_c.UnderFire(2f)) return true;
            if (x.has && x.dist < 3f) return true;
            if (x.seen && x.read.hand != PlayerHand.None) return true;
            if (x.conf < EnemyBelief.Knows && x.dist > 12f) return false;
            return x.has;
        }

        private void DoAdvance(Ctx x, AIDifficulty.Profile p, float speed)
        {
            Vector3 me = _c.transform.position;
            var s = _c.Squad;
            // Hunting an unseen target: no cover-to-cover hops (they stalled behind walls), walk his path / to the news
            if (!x.seen && x.unseenFor >= HuntAfter)
            {
                _boundSpot = null;
                ReleaseCover();
                // At the end of the trail / the heard place and nobody: say so; the confidence drops and the squad
                // spreads out to search instead of everybody standing on one spot (user bug report 16 Nov)
                bool finalSpot = Flat(x.chase - x.threat).magnitude < 1f;
                if (finalSpot && AtSpot(x.chase, 2.5f))
                {
                    _c.Belief.LookedAndEmpty(x.b);
                    var own = _c.Belief.Of(x.b.target);
                    if (own != null && own != x.b) _c.Belief.LookedAndEmpty(own);
                    _c.Barks.Say(BarkKind.Lost, x.b);
                    Set(me, 0f);
                    return;
                }
                // Each man aims a little apart from the others (no pile on one point)
                Vector3 huntGoal = x.chase;
                if (finalSpot) huntGoal += SpreadOffset(2.5f);
                Set(Snap(huntGoal), speed * (x.melee ? 1.1f : 0.9f));
                FacePoint = null;
                if (Random.value < 0.03f) _c.Barks.Say(BarkKind.Searching);
                return;
            }
            // Bounding: at most half of the squad moves at once; a moving one without a token gets cover fire
            if (p.bounding && s != null && Time.time < _boundPauseUntil)
            {
                Set(_boundSpot != null ? _boundSpot.position : me, 0f);
                return;
            }
            if (p.bounding && s != null)
            {
                int moving = 0;
                foreach (var m in s.Members)
                    if (m != null && m != _c && !m.IsDead && m.Brain != null && m.Brain.Choice == Option.Advance && m.Brain.Moving) moving++;
                if (moving >= Mathf.Max(1, s.Alive / 2)) { Set(_cover != null ? _cover.position : me, 0f); return; }
                if (!HasToken && Random.value < 0.15f)
                    foreach (var m in s.Members)
                        if (m != null && m != _c && !m.IsDead && m.Brain != null && m.Brain.HasToken) { m.Barks.Say(BarkKind.Covering); break; }
            }
            Vector3 dir = Flat(x.threat - me);
            if (dir.sqrMagnitude < 0.01f) { Set(me, 0f); return; }
            dir.Normalize();
            if (UseCover && (_boundSpot == null || AtSpot(_boundSpot.position, 0.8f)))
            {
                if (_boundSpot != null && AtSpot(_boundSpot.position, 0.8f))
                {
                    _boundPauseUntil = Time.time + Random.Range(1f, 2f);
                    TakeCover(_boundSpot);
                    _boundSpot = null;
                    Set(me, 0f);
                    return;
                }
                _boundSpot = CoverMap.FindCover(_c, x.threat, 2f, Mathf.Max(2f, x.dist - 10f), 16f, dir);
            }
            if (_boundSpot != null)
            {
                Set(_boundSpot.position, speed * 1.3f);
                FacePoint = null;
                return;
            }
            // No cover: along the target's path (straight when seen), zigzag under fire
            Vector3 goal = x.chase;
            if (x.underFire)
            {
                if (Time.time > _zigAt)
                {
                    _zigAt = Time.time + 1.2f;
                    _zigSign = -_zigSign;
                }
                goal = me + Quaternion.AngleAxis(25f * _zigSign, Vector3.up) * dir * 6f;
            }
            Set(goal, speed);
            FacePoint = x.seen && x.dist < _c.attackRange ? x.seenAt : (Vector3?)null;
            if (HasToken && x.seen && x.dist < _c.attackRange && x.gunner)
            {
                WantsFire = true;
                FireAt = AimPoint(x, p);
            }
        }

        private void DoFlank(Ctx x, AIDifficulty.Profile p, float speed)
        {
            var s = _c.Squad;
            Vector3 me = _c.transform.position;
            if (_c.UnderFire(1f) && _flankSpot != null && !AtSpot(_flankSpot.position, 1.2f))
            {
                // Seen on the way: into the nearest cover, the tactic is looked at again later
                _flankDropUntil = Time.time + 6f;
                var c = CoverMap.FindCover(_c, x.threat, 3f, x.dist, 12f);
                if (c != null) { TakeCover(c); Set(c.position, speed * 1.3f); }
                _flankSpot = null;
                return;
            }
            if (_flankSpot == null || (_flankSpot.reservedBy != null && _flankSpot.reservedBy != _c))
            {
                Vector3 centroid = s != null ? s.SuppressorCentroid(me) : me;
                _flankSpot = CoverMap.FindFlankSpot(_c, x.threat, centroid, out _flankSide);
                _flankBarked = false;
                if (_flankSpot != null) CoverMap.Reserve(_flankSpot, _c);
            }
            if (_flankSpot == null)
            {
                // No map: the old way, to the side of the belief
                Vector3 toK = Flat(x.threat - me);
                Vector3 side = Vector3.Cross(Vector3.up, toK.sqrMagnitude > 0.01f ? toK.normalized : _c.transform.forward) * (_flankSide == 0 ? 1f : _flankSide);
                Set(Snap(x.threat + side * 9f), speed);
                FacePoint = null;
                return;
            }
            if (!_flankBarked && AIDifficulty.For(s).flankerBark)
            {
                _c.Barks.Say(BarkKind.Flanking, null, _flankSide > 0 ? "Sağdan dolanıyorum!" : "Soldan dolanıyorum!");
                _flankBarked = true;
            }
            if (!AtSpot(_flankSpot.position, 1.2f))
            {
                Set(_flankSpot.position, speed * 1.2f);
                FacePoint = null;
                return;
            }
            _cover = _flankSpot;
            if (HasToken && x.conf > EnemyBelief.Guess && CoverMap.PeekSpot(_flankSpot, x.seenAt, out Vector3 spot))
            {
                Set(spot, speed * 0.7f);
                WantsFire = AtSpot(spot, 0.7f) && x.seen;
                FireAt = AimPoint(x, p);
            }
            else Set(_flankSpot.position, 0f);
        }

        private void DoHoldAngle(Ctx x, AIDifficulty.Profile p, float speed)
        {
            Vector3 me = _c.transform.position;
            var s = _c.Squad;
            CoverPoint want = _cover;
            switch (x.tactic)
            {
                case SquadTactic.FallBack:
                    if (want == null || Flat(want.position - x.threat).magnitude < x.band.y || Time.time > _nextCoverSearch)
                    {
                        _nextCoverSearch = Time.time + 2f;
                        want = CoverMap.FindCover(_c, x.threat, x.band.y, x.band.y + 6f, 25f) ?? want;
                    }
                    break;
                case SquadTactic.Encircle:
                    if (want == null || Time.time > _nextCoverSearch)
                    {
                        _nextCoverSearch = Time.time + 3f;
                        int idx = s != null ? Mathf.Max(0, s.Members.IndexOf(_c)) : 0;
                        Vector3 baseDir = Flat(me - x.threat).normalized;
                        Vector3 sector = x.threat + Quaternion.AngleAxis(idx * 120f, Vector3.up) * baseDir * 14f;
                        var c = CoverMap.FindCover(_c, x.threat, 8f, 14f, 30f);
                        if (c != null && Flat(c.position - sector).magnitude < 15f) want = c;
                        else if (want == null) { Set(Snap(sector), speed); return; }
                    }
                    break;
                case SquadTactic.BreakContact:
                {
                    if (_breakContactSince < 0f) _breakContactSince = Time.time;
                    Vector3 anchor = s != null && s.objective != null ? s.objective.position : s != null ? s.home : me;
                    if (want == null || Time.time > _nextCoverSearch)
                    {
                        _nextCoverSearch = Time.time + 4f;
                        var c = CoverMap.FindCover(_c, x.threat, 6f, 12f, 25f);
                        if (c != null && Flat(c.position - anchor).magnitude < 14f) want = c;
                        else if (c != null && want == null) want = c;
                    }
                    // The last one hiding: strikes only when the player comes close and looks away / loots (21.2)
                    bool chance = x.seen && x.dist < 6f && (x.lookingAway || PlayerRead.Intent == PlayerIntent.Looting);
                    if (chance && HasToken)
                    {
                        WantsFire = x.gunner;
                        FireAt = AimPoint(x, p);
                        if (x.melee) { ChargeNow = true; Set(x.threat, _c.moveSpeed * _c.meleeChargeSpeedMultiplier); return; }
                    }
                    if (Time.time - _breakContactSince > 120f) _c.BeginFlee();
                    break;
                }
            }
            if (want != null)
            {
                TakeCover(want);
                Set(want.position, AtSpot(want.position, 0.8f) ? 0f : speed * 1.1f);
            }
            else Set(me, 0f);
            if (x.tactic != SquadTactic.BreakContact && HasToken && x.seen && x.conf > EnemyBelief.Knows && x.gunner && _cover != null
                && CoverMap.PeekSpot(_cover, x.seenAt, out Vector3 spot) && AtSpot(want != null ? want.position : me, 1f))
            {
                Set(spot, speed * 0.6f);
                WantsFire = AtSpot(spot, 0.7f);
                FireAt = AimPoint(x, p);
            }
        }

        private void DoShield(Ctx x, AIDifficulty.Profile p, float speed)
        {
            Vector3 me = _c.transform.position;
            var s = _c.Squad;
            float sp = speed * (Guard ? 0.6f : 1f);
            // Cover a reloading mate within 8 m: stand between him and the threat
            var reloading = s?.NearestReloadingMate(_c, 8f);
            if (reloading != null)
            {
                Vector3 between = reloading.transform.position + Flat(x.threat - reloading.transform.position).normalized * 1.6f;
                Set(Snap(between), sp);
                return;
            }
            // Hit from two sides: back to the nearest cover, shield up
            if (_c.HitFromTwoSides)
            {
                var c = CoverMap.FindCover(_c, x.threat, 2f, Mathf.Max(3f, x.dist), 10f, null, false);
                if (c != null) { Set(c.position, sp); return; }
            }
            Vector2 band = ShieldPistolBand;
            bool pistol = _c.WeaponKind == WeaponType.Pistol && !_c.OutOfAmmo;
            // Pointman: on the line between the mates' centre and the threat, in front of them
            Vector3 goal = x.threat;
            if (s != null)
            {
                Vector3 sum = Vector3.zero;
                int n = 0;
                foreach (var m in s.Members)
                {
                    if (m == null || m == _c || m.IsDead || (m.transform.position - me).sqrMagnitude > 144f) continue;
                    sum += m.transform.position;
                    n++;
                }
                if (n > 0)
                {
                    Vector3 c = sum / n;
                    float mateDist = Flat(x.threat - c).magnitude;
                    goal = c + Flat(x.threat - c).normalized * Mathf.Max(2.5f, Mathf.Min(mateDist - band.x, 4f));
                    // Never walk back toward the mates, away from the target (user bug report 16 Nov: the shield man
                    // noticed the player and walked the other way): already in front, keep going at him
                    if (Flat(goal - x.threat).magnitude > Flat(me - x.threat).magnitude - 0.5f) goal = x.threat;
                }
            }
            if (!x.seen && x.unseenFor >= HuntAfter) goal = x.chase; // lost him: follow his path
            if (pistol && x.dist < band.x) goal = me;
            if (x.dist < 1.6f) goal = me;
            Set(Snap(goal), sp);
            if (pistol && HasToken && x.seen && x.dist <= _c.attackRange && !_c.IsReloading)
            {
                WantsFire = true;
                FireAt = AimPoint(x, p);
            }
            else if (!pistol && HasToken && x.dist < 2.5f) ChargeNow = true;
        }

        private void DoMeleeApproach(Ctx x, float speed)
        {
            Vector3 me = _c.transform.position;
            Vector3 pp = x.threat;
            // Far: cover to cover
            if (x.dist > 12f && UseCover)
            {
                if (_boundSpot == null || AtSpot(_boundSpot.position, 0.8f))
                    _boundSpot = CoverMap.FindCover(_c, pp, 4f, Mathf.Max(4f, x.dist - 9f), 16f, Flat(pp - me).normalized);
                if (_boundSpot != null) { Set(_boundSpot.position, speed * 1.3f); FacePoint = null; return; }
                Set(pp, speed);
                FacePoint = null;
                return;
            }
            // Circle at 4-6 m out of the gunners' line
            float interval = 0.3f;
            _orbitAngle += _orbitDir * 25f * interval;
            Vector3 from = Flat(me - pp);
            if (from.sqrMagnitude < 0.01f) from = Vector3.forward;
            Vector3 goal = pp + Quaternion.AngleAxis(_orbitDir * 25f, Vector3.up) * from.normalized * _circleRadius;
            var gunner = _c.Squad?.NearestGunner(_c);
            if (gunner != null)
            {
                Vector3 g = gunner.transform.position;
                Vector3 closest = ClosestOnSegment(pp, g, goal);
                Vector3 off = Flat(goal - closest);
                if (off.magnitude < 2.5f)
                {
                    Vector3 lineDir = Flat(g - pp).normalized;
                    Vector3 perp = Vector3.Cross(Vector3.up, lineDir) * (Vector3.Dot(off, Vector3.Cross(Vector3.up, lineDir)) >= 0f ? 1f : -1f);
                    goal = closest + perp * 3f;
                }
            }
            if (EnemyMover.OnNavMesh(goal, 3f, out Vector3 snapped)) goal = snapped;
            else _orbitDir = -_orbitDir;
            Set(goal, speed * 0.9f);
            FacePoint = x.dist < 8f ? x.seenAt : (Vector3?)null;
        }

        private void DoGuard(Ctx x, float speed)
        {
            var s = _c.Squad;
            Vector3 me = _c.transform.position;
            Vector3 spot = s != null ? s.home : me;
            var p = AIDifficulty.For(s);
            if (s != null && s.objective != null) spot = s.objective.position + Flat(x.threat - s.objective.position).normalized * 2f;
            else if (s != null && s.KnownVehicle && AITarget.PlayerVehicle != null)
            {
                Vector3 car = AITarget.PlayerVehicle.Position;
                if (p.vehicleDeny >= 3 && _c.State == EnemyController.EnemyState.Searching)
                {
                    // Ambush at the parked car: a hidden spot 8-15 m away that sees the car
                    var c = CoverMap.FindCover(_c, car, 8f, 12f, 30f);
                    spot = c != null ? c.position : car + Flat(me - car).normalized * 10f;
                }
                else if (x.has && !x.read.inVehicle) spot = Vector3.Lerp(car, x.threat, 0.3f) + SpreadOffset(2f); // between the car and the player
                else spot = car + Flat(me - car).normalized * 3f + SpreadOffset(1.5f);
            }
            else if (s != null && s.Search != null) spot = s.Search.center;
            Set(Snap(spot), AtSpot(spot, 1.5f) ? 0f : speed);
            FacePoint = x.has ? x.threat : (Vector3?)null;
            if (HasToken && x.seen && x.gunner && x.dist <= _c.attackRange)
            {
                WantsFire = true;
                FireAt = AimPoint(x, p);
            }
        }

        private void DoSearch(Ctx x, float speed)
        {
            var s = _c.Squad;
            Vector3 me = _c.transform.position;
            if (s == null || s.Search == null)
            {
                Set(x.has ? x.chase : me, speed * 0.7f);
                return;
            }
            var partner = s.Search.PartnerOf(_c);
            if (partner != null)
            {
                // Follow 4 m behind, look the other way
                Vector3 behind = partner.transform.position - partner.transform.forward * 4f;
                Set(Snap(behind), speed * 0.7f);
                FacePoint = partner.transform.position - partner.transform.forward * 10f + partner.transform.right * (Random.value < 0.5f ? 6f : -6f);
                return;
            }
            if (_searchPoint == null || _searchPoint.cleared)
            {
                _searchPoint = s.Search.Claim(_c);
                _searchLookUntil = 0f;
                if (_searchPoint != null && Random.value < 0.3f) _c.Barks.Say(BarkKind.Searching);
            }
            if (_searchPoint == null) { Set(s.home, speed * 0.6f); return; }
            if (!AtSpot(_searchPoint.position, 1.2f))
            {
                Set(_searchPoint.position, speed * 0.7f);
                FacePoint = null;
                return;
            }
            if (_searchLookUntil <= 0f) _searchLookUntil = Time.time + EnemySearch.LookTime;
            float k = Mathf.Sin(Time.time * 2.2f) * 60f;
            FacePoint = me + Quaternion.AngleAxis(k, Vector3.up) * _c.transform.forward * 5f;
            Set(me, 0f);
            if (Time.time > _searchLookUntil)
            {
                s.Search.Clear(_searchPoint);
                _searchPoint = null;
            }
        }

        private Vector3 FleeGoal(Ctx x)
        {
            Vector3 me = _c.transform.position;
            var s = _c.Squad;
            Vector3 threat = x.has ? x.threat : me - _c.transform.forward * 10f;
            if (s != null && Flat(s.home - threat).magnitude > 25f && Flat(s.home - me).magnitude > 5f
                && Vector3.Dot(Flat(s.home - me).normalized, Flat(me - threat).normalized) > -0.2f)
                return Snap(s.home);
            return _c.FleeGoalFrom(threat);
        }

        // ------------------------------------------------------------------ cover

        private bool EnsureCover(Ctx x)
        {
            if (_cover != null)
            {
                bool bad = !CoverMap.Protects(_cover, x.threat);
                Vector3 toT = Flat(x.threat - _cover.position).normalized;
                if (Vector3.Angle(-_cover.normal, toT) > 70f) bad = true;
                if (_c.GrenadeThreat.HasValue && Flat(_c.GrenadeThreat.Value - _cover.position).magnitude < 4f) bad = true;
                float d = Flat(_cover.position - x.threat).magnitude;
                if (d < x.band.x * 0.6f || d > x.band.y * 1.6f)
                {
                    if (_outOfBandSince < 0f) _outOfBandSince = Time.time;
                    if (Time.time - _outOfBandSince > 2f) bad = true;
                }
                else _outOfBandSince = -1f;
                if (bad) ReleaseCover();
            }
            if (_cover != null) return true;
            if (Time.time < _nextCoverSearch) return false;
            _nextCoverSearch = Time.time + 2f;
            float mid = (x.band.x + x.band.y) * 0.5f;
            var c = CoverMap.FindCover(_c, x.threat, x.band.x * 0.6f, mid, CoverRadius);
            if (c != null) { TakeCover(c); return true; }
            // Fallback: the far side of nearby solid things (the old collider scan) or behind a shield bearer
            Vector3? old = OldFindCover(x.threat, _c.transform.position, x.band.x * 0.6f, mid);
            if (old.HasValue)
            {
                TakeCover(new CoverPoint { position = old.Value, normal = Flat(old.Value - x.threat).normalized, height = CoverHeight.High, temporary = true });
                return true;
            }
            return false;
        }

        private void TakeCover(CoverPoint c)
        {
            if (c == _cover) return;
            if (!c.temporary) CoverMap.Reserve(c, _c);
            _cover = c;
            _hideUntil = 0f;
            _peekUntil = 0f;
        }

        public void ReleaseCover()
        {
            CoverMap.Release(_c);
            _cover = null;
        }

        private static readonly Collider[] Buffer = new Collider[48];

        private Vector3? OldFindCover(Vector3 threat, Vector3 around, float minDist, float idealDist)
        {
            int n = Physics.OverlapSphereNonAlloc(around, 12f, Buffer, EnemyPerception.Mask, QueryTriggerInteraction.Ignore);
            Vector3? best = null;
            float bestScore = float.MaxValue;
            int tested = 0;
            for (int i = 0; i < n && tested < 12; i++)
            {
                Collider col = Buffer[i];
                if (col == null || col is TerrainCollider || EnemyPerception.IsCharacter(col)) continue;
                if (col.attachedRigidbody != null && !col.attachedRigidbody.isKinematic) continue;
                Bounds b = col.bounds;
                if (b.size.y < 0.9f || b.size.x > 15f || b.size.z > 15f || Mathf.Max(b.size.x, b.size.z) < 0.6f) continue;
                Vector3 away = Flat(b.center - threat);
                if (away.sqrMagnitude < 0.01f) continue;
                away.Normalize();
                float ext = Mathf.Max(b.extents.x, b.extents.z);
                Vector3 p = b.center + away * (ext + 0.8f);
                p.y = around.y;
                tested++;
                if (!EnemyMover.OnNavMesh(p, 2f, out Vector3 snapped)) continue;
                float toT = Flat(snapped - threat).magnitude;
                if (toT < minDist) continue;
                if (!CoverMap.Blocked(threat + Vector3.up * 1.5f, snapped + Vector3.up * 1f)) continue;
                float score = Flat(snapped - around).magnitude + Mathf.Abs(toT - idealDist) * 0.5f;
                if (score < bestScore) { bestScore = score; best = snapped; }
            }
            var bearer = _c.Squad?.NearestShieldBearer(_c, 12f);
            if (bearer != null)
            {
                Vector3 bp = bearer.transform.position;
                Vector3 behind = bp + Flat(bp - threat).normalized * 1.6f;
                float score = Flat(behind - around).magnitude - 3f;
                if (score < bestScore) best = behind;
            }
            return best;
        }

        /// <summary>A friend is in the line of fire: step 1.5 m to the side for a moment (13.4).</summary>
        public void SideStep()
        {
            Vector3 me = _c.transform.position;
            Vector3 side = _c.transform.right * (Random.value < 0.5f ? -1.5f : 1.5f);
            _sideStep = Snap(me + side);
            _sideStepUntil = Time.time + 0.5f;
        }

        // ------------------------------------------------------------------ helpers

        private void ReleaseToken()
        {
            if (HasToken) EnemySquad.ReleaseToken(_c);
            HasToken = false;
        }

        private bool AtSpot(Vector3 p, float r) => Flat(p - _c.transform.position).magnitude < r;

        private void Set(Vector3 goal, float speed)
        {
            Goal = goal;
            Speed = speed;
            Moving = speed > 0.01f && Flat(goal - _c.transform.position).magnitude > 0.4f;
        }

        private static Vector3 Snap(Vector3 p) => EnemyMover.OnNavMesh(p, 4f, out Vector3 s) ? s : p;

        private static Vector3 Flat(Vector3 v)
        {
            v.y = 0f;
            return v;
        }

        private static Vector3 ClosestOnSegment(Vector3 a, Vector3 b, Vector3 p)
        {
            Vector3 ab = b - a;
            float t = ab.sqrMagnitude > 0.0001f ? Mathf.Clamp01(Vector3.Dot(p - a, ab) / ab.sqrMagnitude) : 0f;
            return a + ab * t;
        }

        public static string OptionText(Option o)
        {
            switch (o)
            {
                case Option.Flee: return "kaçıyor";
                case Option.Surrender: return "teslim";
                case Option.EvadeGrenade: return "bombadan kaçıyor";
                case Option.EvadeVehicle: return "araçtan kaçıyor";
                case Option.Heal: return "sargı";
                case Option.Reload: return "şarjör";
                case Option.ToCover: return "sipere gidiyor";
                case Option.PeekFire: return "siperden ateş";
                case Option.OpenFire: return "açıkta ateş";
                case Option.Suppress: return "baskı ateşi";
                case Option.Advance: return "ilerliyor";
                case Option.Retreat: return "geri çekiliyor";
                case Option.Flank: return "kanattan dolanıyor";
                case Option.HoldAngle: return "açı tutuyor";
                case Option.ThrowGrenade: return "bomba";
                case Option.ThrowRock: return "taş";
                case Option.Pickup: return "eşya alıyor";
                case Option.ShieldAdvance: return "kalkanla ilerliyor";
                case Option.ShieldBash: return "kalkan darbesi";
                case Option.MeleeApproach: return "çevrede dolanıyor";
                case Option.MeleeCharge: return "dalıyor";
                case Option.GuardSpot: return "nöbet";
                case Option.SearchPoint: return "arıyor";
                case Option.CarAssault: return "arabaya saldırıyor";
                default: return "bekliyor";
            }
        }
    }
}
