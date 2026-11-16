using UnityEngine;

namespace EndlessCombat.AI
{
    /// <summary>
    /// Combat decisions of an enemy (Core_Feel_Plan stage 4, 2026-11-14). Decides every 0.2-0.4 s, not every frame;
    /// EnemyController carries the decision out each frame (move, face, aim, fire, strike).
    /// Roles (from the weapon):
    ///   Rifle / Pistol   keeps a distance band (rifle 12-20 m, pistol 7-12 m): backs off when the player comes
    ///                    closer, moves up when he is further. Looks for cover (the far side of nearby solid things,
    ///                    or behind a shield bearer), peeks out to shoot for 1-2 s, hides again, reloads in cover.
    ///                    Player unseen for 4-6 s: goes to the side of where he was last seen (flank).
    ///   Melee            circles the player at 4-6 m without an attack token, charges in with one, one strike,
    ///                    then back out. Keeps out of the line between the player and the nearest gunner.
    ///   Shield           shield up, slow advance, turns slowly (the player can get around it). Pistol behind the
    ///                    shield (fires with a token) or a melee weapon (strikes with a token).
    ///   Wounded          under 25 % health and no friend fighting nearby: runs away / to cover.
    /// Only token holders (EnemySquad, at most 2) shoot or strike.
    /// </summary>
    public class EnemyBrain
    {
        public enum Role { Rifle, Pistol, Melee, Shield }
        public enum Act { Hold, Advance, Retreat, Flank, ToCover, InCover, Peek, Circle, Charge, Flee }

        // ---- tuning (panel page "Düşman") ----
        public static Vector2 RifleBand = new Vector2(12f, 20f);
        public static Vector2 PistolBand = new Vector2(7f, 12f);
        public static Vector2 ShieldPistolBand = new Vector2(4f, 9f);
        public static float CoverRadius = 12f;
        public static Vector2 PeekTime = new Vector2(1f, 2f);
        public static Vector2 HideTime = new Vector2(1.5f, 2.5f);
        public static Vector2 DecideInterval = new Vector2(0.2f, 0.4f);
        public static Vector2 FlankDelay = new Vector2(4f, 6f);
        public static Vector2 CircleRadius = new Vector2(4f, 6f);
        public static float ShieldTurnSpeed = 70f;
        public static float WoundedFraction = 0.25f;
        public static bool UseCover = true;

        private readonly EnemyController _c;
        private float _nextDecide;
        private Vector3? _cover;
        private Vector3 _peek;
        private float _nextCoverSearch;
        private float _phaseUntil;
        private bool _peeking;
        private float _orbitAngle;
        private float _orbitDir = 1f;
        private float _circleRadius;
        private float _flankDelay;
        private float _flankSide = 1f;
        private static readonly Collider[] Buffer = new Collider[48];

        // ---- decision (read by EnemyController every frame) ----
        public Role CurrentRole { get; private set; }
        public Act Action { get; private set; }
        public Vector3 Goal { get; private set; }
        public bool Moving { get; private set; }
        public float Speed { get; private set; }
        /// <summary>Face the player while moving (backing off, strafing, shield).</summary>
        public bool FacePlayer { get; private set; }
        public bool Guard { get; private set; }
        public bool HasToken { get; private set; }

        public EnemyBrain(EnemyController c)
        {
            _c = c;
            _orbitDir = Random.value < 0.5f ? -1f : 1f;
            _orbitAngle = Random.Range(0f, 360f);
            _circleRadius = Random.Range(CircleRadius.x, CircleRadius.y);
            _flankDelay = Random.Range(FlankDelay.x, FlankDelay.y);
            _flankSide = Random.value < 0.5f ? -1f : 1f;
        }

        public Role ResolveRole()
        {
            if (_c.HasUsableShield) return Role.Shield;
            switch (_c.WeaponKind)
            {
                case Combat.WeaponType.Rifle: return _c.OutOfAmmo ? Role.Melee : Role.Rifle;
                case Combat.WeaponType.Pistol: return _c.OutOfAmmo ? Role.Melee : Role.Pistol;
                default: return Role.Melee;
            }
        }

        /// <summary>Leaving combat: tokens back, cover forgotten.</summary>
        public void Reset()
        {
            if (HasToken) EnemySquad.ReleaseToken(_c);
            HasToken = false;
            _cover = null;
            Moving = false;
            Guard = false;
        }

        /// <summary>A strike is done: give the token to someone else and back out.</summary>
        public void StrikeDone()
        {
            EnemySquad.ReleaseToken(_c);
            HasToken = false;
            _nextDecide = 0f;
        }

        public void Tick(Transform player, bool seenNow, float timeSinceSeen)
        {
            if (Time.time < _nextDecide) return;
            _nextDecide = Time.time + Random.Range(DecideInterval.x, DecideInterval.y);
            CurrentRole = ResolveRole();

            EnemySquad squad = _c.Squad;
            if (seenNow) squad?.ReportSighting(player.position);
            Vector3 me = _c.transform.position;
            Vector3 known = seenNow || squad == null || !squad.HasMemory ? player.position : squad.LastKnownPlayerPos;
            float dist = Flat(player.position - me).magnitude;
            bool sighted = timeSinceSeen < 1.5f;

            // Wounded and alone: get away
            if (_c.Health01 < WoundedFraction && (squad == null || squad.AlliesFighting(_c, 15f) == 0))
            {
                ReleaseToken();
                Vector3 away = Flat(me - player.position).normalized;
                Vector3 goal = FindCover(player, me + away * 6f, 4f, 30f) ?? (me + away * 15f);
                Set(Act.Flee, goal, _c.moveSpeed * 1.3f, false);
                Guard = CurrentRole == Role.Shield;
                return;
            }

            switch (CurrentRole)
            {
                case Role.Melee: DecideMelee(player, dist, slow: false); break;
                case Role.Shield:
                    Guard = true;
                    if (_c.WeaponKind == Combat.WeaponType.Pistol && !_c.OutOfAmmo) DecideGunner(player, known, dist, sighted, timeSinceSeen, ShieldPistolBand, shield: true);
                    else DecideMelee(player, dist, slow: true);
                    break;
                default:
                    Guard = false;
                    DecideGunner(player, known, dist, sighted, timeSinceSeen, CurrentRole == Role.Rifle ? RifleBand : PistolBand, shield: false);
                    break;
            }
        }

        // ------------------------------------------------------------------ gunners

        private void DecideGunner(Transform player, Vector3 known, float dist, bool sighted, float sinceSeen, Vector2 band, bool shield)
        {
            Vector3 me = _c.transform.position;
            float speed = _c.moveSpeed * (shield ? 0.6f : 1f);

            // Token: only with the player in sight and in range
            if (sighted && dist <= _c.attackRange * 1.4f) HasToken = EnemySquad.RequestToken(_c);
            else if (sinceSeen > 1.5f) ReleaseToken();

            if (!shield && _c.IsReloading)
            {
                if (UseCover && EnsureCover(player, band)) { Set(Act.InCover, _cover.Value, speed * 1.2f, false); return; }
                Set(Act.Hold, me, 0f, true);
                return;
            }

            if (dist < band.x - 0.5f)
            {
                Vector3 away = Flat(me - player.position).normalized;
                Vector3 goal = me + away * 5f;
                if (EnemyMover.OnNavMesh(goal, 3f, out Vector3 snapped)) goal = snapped;
                _cover = null;
                Set(Act.Retreat, goal, speed * 0.8f, true);
                return;
            }

            if (sinceSeen > _flankDelay)
            {
                // Lost him for a while: not to the spot he was last seen, to its side
                Vector3 toKnown = Flat(known - me);
                Vector3 side = Vector3.Cross(Vector3.up, toKnown.sqrMagnitude > 0.01f ? toKnown.normalized : _c.transform.forward) * _flankSide;
                Vector3 goal = known + side * 7f;
                if (EnemyMover.OnNavMesh(goal, 4f, out Vector3 snapped)) goal = snapped;
                _cover = null;
                Set(Act.Flank, goal, speed, false);
                return;
            }

            if (!shield && UseCover && EnsureCover(player, band))
            {
                if (Time.time >= _phaseUntil)
                {
                    _peeking = HasToken && !_peeking;
                    _phaseUntil = Time.time + (_peeking ? Random.Range(PeekTime.x, PeekTime.y) : Random.Range(HideTime.x, HideTime.y));
                }
                if (!HasToken) _peeking = false;
                Vector3 spot = _peeking ? _peek : _cover.Value;
                bool there = Flat(spot - me).magnitude < 0.6f;
                Set(_peeking ? Act.Peek : (there ? Act.InCover : Act.ToCover), spot, speed * (there ? 0.5f : 1.2f), _peeking || there);
                return;
            }

            if (dist > band.y || !sighted)
            {
                Set(Act.Advance, known, speed, sighted && dist < _c.attackRange);
                return;
            }
            Set(Act.Hold, me, 0f, true);
        }

        /// <summary>Keeps a valid cover point, searching again when none or when the player can see it.</summary>
        private bool EnsureCover(Transform player, Vector2 band)
        {
            if (_cover.HasValue && !EnemyPerception.Covered(_cover.Value + Vector3.up * 1.0f, player, _c.transform)) _cover = null;
            if (_cover.HasValue) return true;
            if (Time.time < _nextCoverSearch) return false;
            _nextCoverSearch = Time.time + 2f;
            float mid = (band.x + band.y) * 0.5f;
            _cover = FindCover(player, _c.transform.position, band.x * 0.6f, mid);
            if (_cover.HasValue) _peek = FindPeek(_cover.Value, player);
            _peeking = false;
            _phaseUntil = 0f;
            return _cover.HasValue;
        }

        /// <summary>
        /// A point hidden from the player near "around": the far side of nearby solid objects (10-12 candidates,
        /// each checked with a ray from the player's eyes) or the back of a shield bearer of the squad.
        /// </summary>
        private Vector3? FindCover(Transform player, Vector3 around, float minPlayerDist, float idealPlayerDist)
        {
            Vector3 pp = player.position;
            int n = Physics.OverlapSphereNonAlloc(around, CoverRadius, Buffer, ~0, QueryTriggerInteraction.Ignore);
            Vector3? best = null;
            float bestScore = float.MaxValue;
            int tested = 0;
            for (int i = 0; i < n && tested < 12; i++)
            {
                Collider col = Buffer[i];
                if (col == null || col is TerrainCollider) continue;
                if (col.transform.IsChildOf(_c.transform) || col.transform.IsChildOf(player)) continue;
                if (col.attachedRigidbody != null && !col.attachedRigidbody.isKinematic) continue;
                if (col.GetComponentInParent<Combat.CombatBody>() != null) continue;
                Bounds b = col.bounds;
                if (b.size.y < 0.9f || b.size.x > 15f || b.size.z > 15f || Mathf.Max(b.size.x, b.size.z) < 0.6f) continue;

                Vector3 away = Flat(b.center - pp);
                if (away.sqrMagnitude < 0.01f) continue;
                away.Normalize();
                float ext = Mathf.Max(b.extents.x, b.extents.z);
                Vector3 p = b.center + away * (ext + 0.8f);
                p.y = around.y;
                tested++;
                if (!Score(p, pp, player, minPlayerDist, idealPlayerDist, ref best, ref bestScore)) continue;
            }

            var bearer = _c.Squad?.NearestShieldBearer(_c, 12f);
            if (bearer != null)
            {
                Vector3 bp = bearer.transform.position;
                Vector3 behind = bp + Flat(bp - pp).normalized * 1.6f;
                Score(behind, pp, player, 0f, idealPlayerDist, ref best, ref bestScore, bonus: -3f);
            }
            return best;
        }

        private bool Score(Vector3 p, Vector3 pp, Transform player, float minPlayerDist, float idealPlayerDist,
                           ref Vector3? best, ref float bestScore, float bonus = 0f)
        {
            if (!EnemyMover.OnNavMesh(p, 2f, out Vector3 snapped)) return false;
            float toPlayer = Flat(snapped - pp).magnitude;
            if (toPlayer < minPlayerDist) return false;
            if (!EnemyPerception.Covered(snapped + Vector3.up * 1.0f, player, _c.transform)) return false;
            float score = Flat(snapped - _c.transform.position).magnitude + Mathf.Abs(toPlayer - idealPlayerDist) * 0.5f + bonus;
            if (score >= bestScore) return false;
            bestScore = score;
            best = snapped;
            return true;
        }

        /// <summary>Where to stand to shoot from a cover point: the point itself if the player is visible standing up, else a step to a side.</summary>
        private Vector3 FindPeek(Vector3 cover, Transform player)
        {
            Vector3 target = player.position + Vector3.up * 1.1f;
            if (EnemyPerception.ClearLine(cover + Vector3.up * 1.5f, target, _c.transform, player)) return cover;
            Vector3 side = Vector3.Cross(Vector3.up, Flat(player.position - cover).normalized);
            foreach (float s in new[] { 1.3f, -1.3f, 2.2f, -2.2f })
            {
                Vector3 p = cover + side * s;
                if (EnemyPerception.ClearLine(p + Vector3.up * 1.5f, target, _c.transform, player)
                    && EnemyMover.OnNavMesh(p, 1.5f, out Vector3 snapped)) return snapped;
            }
            return cover;
        }

        // ------------------------------------------------------------------ melee

        private void DecideMelee(Transform player, float dist, bool slow)
        {
            Vector3 pp = player.position;
            float baseSpeed = _c.moveSpeed * (slow ? 0.6f : 1f);
            if (!slow) Guard = false;

            if (dist < 14f) HasToken = EnemySquad.RequestToken(_c);
            if (HasToken)
            {
                Set(Act.Charge, pp, slow ? baseSpeed : _c.moveSpeed * _c.meleeChargeSpeedMultiplier, slow);
                return;
            }

            // No token: circle at a distance, out of the gunners' line of fire
            float interval = (DecideInterval.x + DecideInterval.y) * 0.5f;
            _orbitAngle += _orbitDir * 25f * interval;
            Vector3 from = Flat(_c.transform.position - pp);
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
            Set(Act.Circle, goal, baseSpeed * 0.9f, dist < 8f);
        }

        // ------------------------------------------------------------------ helpers

        public float TurnSpeed => CurrentRole == Role.Shield ? ShieldTurnSpeed : 540f;

        private void ReleaseToken()
        {
            if (HasToken) EnemySquad.ReleaseToken(_c);
            HasToken = false;
        }

        private void Set(Act act, Vector3 goal, float speed, bool facePlayer)
        {
            Action = act;
            Goal = goal;
            Speed = speed;
            Moving = speed > 0.01f;
            FacePlayer = facePlayer;
        }

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
    }
}
