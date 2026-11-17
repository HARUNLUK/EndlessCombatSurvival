using UnityEngine;
using EndlessSurvival.Survival;
using EndlessSurvival.World;

namespace EndlessCombat.AI
{
    /// <summary>
    /// Sight of one enemy (Enemy_AI_Deep_Plan.md 7.1). The only place where real target positions are read; the
    /// brain works from EnemyBelief. Runs at 10 Hz in combat, 5 / 2 / 1 Hz by distance outside combat (23).
    /// </summary>
    public class EnemySenses
    {
        // ---- tuning (panel page "Düşman") ----
        public static float NightSight = 0.55f;
        public static float FireBlindness = 0.6f;
        public static float LitTarget = 1.2f;
        public static float PredatorNotice = 12f;

        private readonly EnemyController _e;
        private float _next;
        private float _glanceAt, _glanceYaw;
        private float _nearFireUntil = -999f;

        public bool SeesAny { get; private set; }
        public float LastClarity { get; private set; }

        public EnemySenses(EnemyController e)
        {
            _e = e;
            _next = Time.time + Random.Range(0f, 0.3f);
        }

        private float Interval
        {
            get
            {
                if (_e.State == EnemyController.EnemyState.Combat) return 0.1f;
                Vector3 p = SurvivalDirector.PlayerPosition;
                float d = (p - _e.transform.position).magnitude;
                if (d < 40f) return 0.2f;
                if (d < 80f) return 0.5f;
                return 1f;
            }
        }

        /// <summary>Returns true when a look happened this frame (seen flags are fresh).</summary>
        public bool Tick()
        {
            if (Time.time < _next) return false;
            float dt = Interval;
            _next = Time.time + dt;
            UpdateGlance();
            UpdateFireBlindness();
            _e.Belief.ClearSeenFlags();
            SeesAny = false;
            var self = _e.SelfTarget;
            foreach (var t in AITarget.All)
            {
                if (t == null || t == self || !t.IsAlive) continue;
                if (!AITargetRegistry.IsHostile(self, t)) continue;
                if (t.kind == AITargetKind.Player && t.InVehicle) continue; // seen through the vehicle target
                if (t.kind == AITargetKind.Animal && (t.Position - _e.transform.position).sqrMagnitude > PredatorNotice * PredatorNotice) continue;
                if (!CanSee(t, out float clarity)) continue;
                AITarget about = t;
                // The vehicle with the player inside: the belief is about the player
                if (t.kind == AITargetKind.Vehicle && AITarget.Player != null && AITarget.Player.InVehicle) about = AITarget.Player;
                _e.Belief.Note(about, about.Position, about.Velocity, 1f, BeliefSource.Seen);
                var b = _e.Belief.Of(about);
                if (b != null) b.SeenNow = true;
                if (about != t) _e.Belief.Note(t, t.Position, t.Velocity, 1f, BeliefSource.Seen);
                SeesAny = true;
                LastClarity = clarity;
                _e.OnSeen(about, clarity, dt);
            }
            return true;
        }

        private void UpdateGlance()
        {
            if (_e.Actor == null || _e.Actor.Readiness == Readiness.Relaxed) { _glanceYaw = 0f; return; }
            if (Time.time < _glanceAt) return;
            float freq = _e.Squad != null && _e.Squad.Phase == AlertPhase.Guarded ? 0.5f : 1f;
            _glanceAt = Time.time + Random.Range(3f, 5f) * freq;
            _glanceYaw = _glanceYaw == 0f ? Random.Range(-60f, 60f) : 0f;
        }

        private void UpdateFireBlindness()
        {
            var cycle = DayNightCycle.Instance;
            if (cycle == null || !cycle.IsNight) return;
            if (CraftingSystem.NearestBurningFire(_e.transform.position, 5f) != null) _nearFireUntil = Time.time + 20f;
        }

        public Vector3 LookForward => Quaternion.AngleAxis(_glanceYaw, Vector3.up) * _e.transform.forward;

        public bool CanSee(AITarget t, out float clarity)
        {
            clarity = 0f;
            if (t == null) return false;
            Vector3 eye = _e.transform.position + Vector3.up * _e.eyeHeight;
            bool vehicle = t.kind == AITargetKind.Vehicle;
            Vector3 center = t.Center;
            Vector3 to = center - eye;
            float dist = to.magnitude;
            var p = AIDifficulty.For(_e.Squad);

            float range = _e.detectionRange * p.sightMul * _e.SightFactor;
            if (!vehicle && t.kind == AITargetKind.Player)
            {
                var read = PlayerRead.Current;
                if (read.crouching)
                {
                    var stealth = Combat.PlayerStealthController.Instance;
                    range *= stealth != null ? stealth.crouchVisibilityMultiplier : 0.45f;
                }
                float speed = t.Velocity.magnitude;
                range *= speed < 0.3f ? 0.85f : speed > 4.5f ? 1.15f : 1f;
            }
            range *= LightFactor(t);
            range *= WeatherFactor();
            if (_e.Actor != null)
            {
                if (_e.Actor.Readiness == Readiness.Wary) range *= 1.15f;
                else if (_e.Actor.Readiness == Readiness.Ready)
                {
                    var b = _e.Belief.Of(t);
                    range *= b != null && b.confidence > 0.5f ? 1.5f : 1.15f;
                }
            }
            if (vehicle) range *= 1.5f;
            if (dist > range) return false;

            float angle = Vector3.Angle(LookForward, to);
            if (angle > _e.fieldOfViewAngle * 0.5f && dist > 1.8f) return false;
            if (!EnemyPerception.ClearLine(eye, center, _e.transform, t.SightTransform))
            {
                // A vehicle behind a low wall or a parked thing still shows its cab and roof
                if (!vehicle || !EnemyPerception.ClearLine(eye, center + Vector3.up * 0.9f, _e.transform, t.SightTransform)) return false;
            }

            clarity = Mathf.Clamp01((1f - Mathf.Clamp01(dist / range)) * 1.6f);
            return true;
        }

        private float LightFactor(AITarget t)
        {
            var cycle = DayNightCycle.Instance;
            if (cycle == null || !cycle.IsNight) return 1f;
            bool lit = false;
            if (t.kind == AITargetKind.Player && !t.InVehicle)
            {
                lit = PlayerRead.Current.torchLit || CraftingSystem.NearestBurningFire(t.Position, 6f) != null;
            }
            else if (t.kind == AITargetKind.Vehicle) lit = VehicleThreat.LampsLit;
            if (lit) return LitTarget;
            return Time.time < _nearFireUntil ? NightSight * FireBlindness : NightSight;
        }

        /// <summary>
        /// Sight range factor for an unlit thing (parked car, body, opened crate) at a point: night and weather, as for
        /// the player. Without it an unlit car was seen from 45 m at night while the player was seen at half his day
        /// range (17 Nov stealth playtest: the whole camp went searching because of a dark car 35 m off).
        /// </summary>
        public float EvidenceFactor(Vector3 pos)
        {
            float f = WeatherFactor();
            var cycle = DayNightCycle.Instance;
            if (cycle == null || !cycle.IsNight) return f;
            if (CraftingSystem.NearestBurningFire(pos, 6f) != null) return f;
            return f * (Time.time < _nearFireUntil ? NightSight * FireBlindness : NightSight);
        }

        private static float WeatherFactor()
        {
            var w = WeatherSystem.Instance;
            if (w == null) return 1f;
            switch (w.Current)
            {
                case WeatherType.Fog: return 1f - 0.5f * w.Intensity;
                case WeatherType.Snow: return 1f - 0.3f * w.Intensity;
                case WeatherType.Rain: return 1f - 0.2f * w.Intensity;
                default: return 1f;
            }
        }

        /// <summary>Cheap "do I see my squad mate" (7.3): 25 m and not covered.</summary>
        public bool SeesMate(EnemyController o)
        {
            Vector3 a = _e.transform.position + Vector3.up * 1.5f;
            Vector3 b = o.transform.position + Vector3.up * 1.2f;
            if ((a - b).sqrMagnitude > 25f * 25f) return false;
            return !EnemyPerception.CoveredFrom(b, a, _e.transform);
        }
    }
}
