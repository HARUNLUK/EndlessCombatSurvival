using UnityEngine;
using UnityEngine.AI;

namespace EndlessCombat.AI
{
    /// <summary>
    /// Moving an enemy (Core_Feel_Plan stage 4, 2026-11-14). Knows only "go to this point" and "look this way".
    ///   route      NavMesh.CalculatePath on the runtime NavMesh (LocalNavMeshBuilder), corners followed with the
    ///              CharacterController. A partial route (does not reach the goal) is used up to its end and reported.
    ///   steering   a short sphere cast ahead; when blocked, the first free direction 35 / 70 / 105 degrees to a side.
    ///   stuck      under 0.3 m of progress in 1 s while trying to move: the route is rebuilt; still stuck: a detour
    ///              point to the side for a moment, then the goal again.
    /// The state of the route is kept for the tuning panel's enemy page (why does an enemy rub along a wall?).
    /// </summary>
    public class EnemyMover
    {
        /// <summary>NoNavMesh / NoPath: the enemy walks a straight line (with steering) as a fallback.</summary>
        public enum RouteState { None, Full, Partial, NoNavMesh, NoPath }

        // ---- tuning (panel page "Düşman") ----
        public static float CornerReach = 0.8f;
        public static float StuckDistance = 0.3f;
        public static float StuckWindow = 1f;

        private readonly EnemyController _owner;
        private readonly Transform _t;
        private NavMeshPath _path = new NavMeshPath();
        private readonly Vector3[] _corners = new Vector3[64];
        private int _cornerCount, _cornerIndex;
        private float _lastRepath = -999f;
        private Vector3 _pathGoal;
        private bool _hasPath;

        private Vector3 _stuckCheckPos;
        private float _stuckCheckAt;
        private int _stuckStrikes;
        private Vector3 _detour;
        private float _detourUntil = -1f;

        private int _steerSide;
        private float _steerSideUntil = -1f;
        private static readonly RaycastHit[] SteerHits = new RaycastHit[12];
        private static readonly float[] SteerAngles = { 35f, 70f, 105f };

        public RouteState Route { get; private set; } = RouteState.None;
        public int StuckCount { get; private set; }
        public Vector3 Goal => _pathGoal;
        public bool Detouring => Time.time < _detourUntil;
        public int CornerCount => _hasPath ? _cornerCount : 0;
        public Vector3 Corner(int i) => _corners[Mathf.Clamp(i, 0, Mathf.Max(0, _cornerCount - 1))];

        public EnemyMover(EnemyController owner)
        {
            _owner = owner;
            _t = owner.transform;
        }

        /// <summary>Unit direction (horizontal) to walk in to reach the goal. Call every frame while moving.</summary>
        public Vector3 DirectionTo(Vector3 goal)
        {
            Vector3 aim = Detouring ? _detour : goal;
            Vector3 straight = aim - _t.position;
            straight.y = 0f;
            straight = straight.sqrMagnitude > 0.0001f ? straight.normalized : _t.forward;
            if (Detouring) return Steer(straight);

            float delta = _owner.repathDestinationDelta;
            bool moved = (goal - _pathGoal).sqrMagnitude > delta * delta;
            if (Time.time - _lastRepath >= _owner.repathInterval || (moved && Time.time - _lastRepath >= 0.1f))
                Recalculate(goal);

            if (!_hasPath) return Steer(straight);

            while (_cornerIndex < _cornerCount)
            {
                Vector3 toCorner = _corners[_cornerIndex] - _t.position;
                toCorner.y = 0f;
                if (toCorner.magnitude > CornerReach) return Steer(toCorner.normalized);
                _cornerIndex++;
            }
            return Steer(straight);
        }

        /// <summary>Called by the owner each frame it tries to move: detects being stuck.</summary>
        public void TrackProgress(bool tryingToMove)
        {
            if (!tryingToMove)
            {
                _stuckCheckAt = Time.time + StuckWindow;
                _stuckCheckPos = _t.position;
                return;
            }
            if (Time.time < _stuckCheckAt) return;
            Vector3 d = _t.position - _stuckCheckPos;
            d.y = 0f;
            _stuckCheckPos = _t.position;
            _stuckCheckAt = Time.time + StuckWindow;
            if (d.magnitude >= StuckDistance) { _stuckStrikes = 0; return; }

            StuckCount++;
            _stuckStrikes++;
            if (_stuckStrikes == 1)
            {
                _lastRepath = -999f; // rebuild the route at once
                _steerSide = -_steerSide;
                return;
            }
            // Still stuck: a detour to the side, then try the goal again
            Vector3 side = Vector3.Cross(Vector3.up, _t.forward).normalized * (Random.value < 0.5f ? -1f : 1f);
            Vector3 p = _t.position + side * 3.5f - _t.forward * 1.5f;
            if (NavMesh.SamplePosition(p, out NavMeshHit hit, 3f, NavMesh.AllAreas)) p = hit.position;
            _detour = p;
            _detourUntil = Time.time + 1.4f;
            _stuckStrikes = 0;
            _lastRepath = -999f;
        }

        // ------------------------------------------------------------------ route queries (Enemy_AI_Deep_Plan 16.5)

        private static NavMeshPath _queryPath;
        private static readonly Vector3[] QueryCorners = new Vector3[48];
        private int _queryCount;

        private int QueryRoute(Vector3 goal)
        {
            _queryCount = 0;
            if (LocalNavMeshBuilder.Instance == null || !LocalNavMeshBuilder.Instance.HasNavMesh) return 0;
            if (_queryPath == null) _queryPath = new NavMeshPath();
            float sample = _owner.navMeshSampleDistance;
            if (!NavMesh.SamplePosition(_t.position, out NavMeshHit from, sample, NavMesh.AllAreas)) return 0;
            if (!NavMesh.SamplePosition(goal, out NavMeshHit to, sample, NavMesh.AllAreas)) return 0;
            if (!NavMesh.CalculatePath(from.position, to.position, NavMesh.AllAreas, _queryPath) || _queryPath.status != NavMeshPathStatus.PathComplete) return 0;
            _queryCount = _queryPath.GetCornersNonAlloc(QueryCorners);
            return _queryCount;
        }

        /// <summary>Route length to a goal (sum of corners); without a route the straight distance × 1.4.</summary>
        public float PathLength(Vector3 goal) => PathLength(goal, out _);

        /// <summary>Same, and whether a complete NavMesh route exists (false also when there is no NavMesh).</summary>
        public float PathLength(Vector3 goal, out bool routed)
        {
            int n = QueryRoute(goal);
            routed = n >= 2;
            if (n < 2)
            {
                Vector3 d = goal - _t.position;
                d.y = 0f;
                return d.magnitude * 1.4f;
            }
            float len = 0f;
            for (int i = 1; i < n; i++) len += Vector3.Distance(QueryCorners[i - 1], QueryCorners[i]);
            return len;
        }

        /// <summary>Fraction (0..1) of the route corners not seen from the threat (flank route choice).</summary>
        public float RouteHiddenFraction(Vector3 goal, Vector3 threat)
        {
            int n = QueryRoute(goal);
            if (n < 2) return 0.5f;
            int hidden = 0;
            Vector3 eye = threat + Vector3.up * 1.5f;
            for (int i = 0; i < n; i++)
                if (CoverMap.Blocked(eye, QueryCorners[i] + Vector3.up * 1.2f)) hidden++;
            return hidden / (float)n;
        }

        /// <summary>The goal can not be reached (partial / no route and more than 2 m away) for 2 s (16.4).</summary>
        public bool Unreachable => Time.time - _reachableAt > 2f;
        private float _reachableAt;

        public void NoteReachability(Vector3 goal)
        {
            Vector3 d = goal - _t.position;
            d.y = 0f;
            if (Route == RouteState.Full || Route == RouteState.None || Route == RouteState.NoNavMesh || d.magnitude < 2f) _reachableAt = Time.time;
        }

        public static bool OnNavMesh(Vector3 p, float radius, out Vector3 snapped)
        {
            snapped = p;
            if (LocalNavMeshBuilder.Instance == null || !LocalNavMeshBuilder.Instance.HasNavMesh) return true; // no mesh: accept
            if (!NavMesh.SamplePosition(p, out NavMeshHit hit, radius, NavMesh.AllAreas)) return false;
            snapped = hit.position;
            return true;
        }

        private void Recalculate(Vector3 goal)
        {
            _lastRepath = Time.time;
            _pathGoal = goal;
            _hasPath = false;

            if (LocalNavMeshBuilder.Instance == null || !LocalNavMeshBuilder.Instance.HasNavMesh) { Route = RouteState.NoNavMesh; return; }
            float sample = _owner.navMeshSampleDistance;
            if (!NavMesh.SamplePosition(_t.position, out NavMeshHit from, sample, NavMesh.AllAreas)) { Route = RouteState.NoPath; return; }
            if (!NavMesh.SamplePosition(goal, out NavMeshHit to, sample, NavMesh.AllAreas)) { Route = RouteState.NoPath; return; }
            if (!NavMesh.CalculatePath(from.position, to.position, NavMesh.AllAreas, _path) || _path.status == NavMeshPathStatus.PathInvalid)
            {
                Route = RouteState.NoPath;
                return;
            }
            _cornerCount = _path.GetCornersNonAlloc(_corners);
            _cornerIndex = _cornerCount > 1 ? 1 : 0;
            _hasPath = _cornerCount > 0;
            Route = !_hasPath ? RouteState.NoPath : _path.status == NavMeshPathStatus.PathPartial ? RouteState.Partial : RouteState.Full;
        }

        /// <summary>True when something solid (not a character, not a low step) is right ahead.</summary>
        private bool WayBlocked(Vector3 dir, float distance)
        {
            Vector3 origin = _t.position + Vector3.up * 0.95f;
            int n = Physics.SphereCastNonAlloc(origin, 0.27f, dir, SteerHits, distance, ~0, QueryTriggerInteraction.Ignore);
            Transform target = _owner.target;
            for (int i = 0; i < n; i++)
            {
                Collider c = SteerHits[i].collider;
                if (c == null || c.transform.IsChildOf(_t)) continue;
                if (c.GetComponentInParent<Combat.CombatBody>() != null) continue;
                if (c.GetComponentInParent<EnemyController>() != null) continue;
                if (target != null && c.transform.IsChildOf(target)) continue;
                if (SteerHits[i].distance <= 0f && SteerHits[i].point == Vector3.zero) continue;
                return true;
            }
            return false;
        }

        private Vector3 Steer(Vector3 dir)
        {
            const float look = 1.3f;
            if (!WayBlocked(dir, look))
            {
                if (Time.time > _steerSideUntil) _steerSide = 0;
                return dir;
            }
            int first = _steerSide != 0 && Time.time <= _steerSideUntil ? _steerSide : (Random.value < 0.5f ? -1 : 1);
            for (int pass = 0; pass < 2; pass++)
            {
                int side = pass == 0 ? first : -first;
                foreach (float angle in SteerAngles)
                {
                    Vector3 candidate = Quaternion.AngleAxis(angle * side, Vector3.up) * dir;
                    if (WayBlocked(candidate, look)) continue;
                    _steerSide = side;
                    _steerSideUntil = Time.time + 1.4f;
                    return candidate;
                }
            }
            return dir;
        }
    }
}
