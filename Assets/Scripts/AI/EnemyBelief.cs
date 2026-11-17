using System.Collections.Generic;
using UnityEngine;

namespace EndlessCombat.AI
{
    public enum BeliefSource { None, Seen, Heard, Told, Inferred }

    /// <summary>What one enemy thinks about one target (Enemy_AI_Deep_Plan.md 8). Never the real position unless seen.</summary>
    public class Belief
    {
        public AITarget target;
        public Vector3 position;
        public Vector3 velocity;
        public float time = -999f;
        public float confidence;
        public BeliefSource source;
        public bool everSeen;
        public bool SeenNow;
        public float lastSeenTime = -999f;
        /// <summary>Copy of the player read the last time this target was seen (14.1).</summary>
        public PlayerRead.Snapshot read;

        /// <summary>
        /// Where the target was seen walking, 1 m apart, oldest first (max 40). A chaser follows this path instead of
        /// a straight guess (user bug report 16 Nov: "sees which way I walk but does not follow it").
        /// </summary>
        public readonly List<Vector3> trail = new List<Vector3>();

        public void AddTrail(Vector3 p)
        {
            if (trail.Count > 0)
            {
                Vector3 last = trail[trail.Count - 1];
                float d = (new Vector3(p.x - last.x, 0f, p.z - last.z)).magnitude;
                if (d < 1f) return;
                if (d > 15f) trail.Clear(); // a jump (vehicle, teleport): a new path
            }
            trail.Add(p);
            if (trail.Count > 40) trail.RemoveAt(0);
        }

        public float SinceSeen => Time.time - lastSeenTime;
        public float Age => Time.time - time;
        public bool Valid => target != null && confidence > 0.001f;

        public Vector3 EstimatedWith(float predictSeconds) => position + velocity * Mathf.Min(SinceSeen, predictSeconds);

        public void CopyFrom(Belief o)
        {
            target = o.target;
            position = o.position;
            velocity = o.velocity;
            time = o.time;
            confidence = o.confidence;
            source = o.source;
            everSeen = o.everSeen;
            lastSeenTime = o.lastSeenTime;
            read = o.read;
            trail.Clear();
            trail.AddRange(o.trail);
        }
    }

    /// <summary>All beliefs of one enemy, with decay and the primary target choice (5.4).</summary>
    public class EnemyBelief
    {
        public const float Knows = 0.7f, Guess = 0.3f;

        private readonly EnemyController _owner;
        private readonly List<Belief> _list = new List<Belief>();
        public Belief Primary { get; private set; }
        public IReadOnlyList<Belief> All => _list;

        public EnemyBelief(EnemyController owner) => _owner = owner;

        public Belief Of(AITarget t)
        {
            if (t == null) return null;
            for (int i = 0; i < _list.Count; i++) if (_list[i].target == t) return _list[i];
            return null;
        }

        private Belief GetOrCreate(AITarget t)
        {
            var b = Of(t);
            if (b != null) return b;
            b = new Belief { target = t, position = t.Position };
            _list.Add(b);
            return b;
        }

        /// <summary>New information (8.2). Seen always wins; weaker news only moves the position when it is newer and not much weaker.</summary>
        public void Note(AITarget t, Vector3 pos, Vector3 vel, float confidence, BeliefSource src)
        {
            if (t == null) return;
            var b = GetOrCreate(t);
            if (src == BeliefSource.Seen)
            {
                b.position = pos;
                b.velocity = vel;
                b.confidence = 1f;
                b.source = src;
                b.time = Time.time;
                b.everSeen = true;
                b.lastSeenTime = Time.time;
                b.AddTrail(pos);
                if (t.kind == AITargetKind.Player || t.kind == AITargetKind.Vehicle) b.read = PlayerRead.Current;
                return;
            }
            if (confidence > b.confidence * 0.5f)
            {
                b.position = pos;
                b.velocity = src == BeliefSource.Told ? vel : Vector3.zero;
                b.confidence = Mathf.Max(b.confidence, confidence);
                b.source = src;
                b.time = Time.time;
            }
            else
            {
                b.confidence = Mathf.Min(1f, b.confidence + 0.1f);
            }
        }

        /// <summary>"Looked there, nobody": confidence drops to 0.2 at once (8.3).</summary>
        public void LookedAndEmpty(Belief b)
        {
            if (b == null) return;
            b.confidence = Mathf.Min(b.confidence, 0.2f);
            b.time = Time.time;
        }

        public void Forget(AITarget t)
        {
            var b = Of(t);
            if (b != null) _list.Remove(b);
            if (Primary == b) Primary = null;
        }

        public void Tick(float dt, AIDifficulty.Profile p)
        {
            for (int i = _list.Count - 1; i >= 0; i--)
            {
                var b = _list[i];
                if (b.target == null || !b.target.IsAlive)
                {
                    _list.RemoveAt(i);
                    continue;
                }
                if (b.SeenNow) continue;
                if (Time.time - b.time > 0.5f) b.confidence = Mathf.Max(0f, b.confidence - p.beliefDecay * dt);
            }
            ChoosePrimary();
        }

        private void ChoosePrimary()
        {
            Belief best = null;
            float bestScore = 0f;
            Vector3 me = _owner.transform.position;
            foreach (var b in _list)
            {
                if (b.confidence <= 0.01f) continue;
                float d = Vector3.Distance(me, b.position);
                float kind = 1f;
                if (b.target.kind == AITargetKind.Vehicle)
                {
                    // The empty car is never the target to fight; the player's own belief (left at the car) is
                    if (!AITargetRegistry.VehicleOccupied) continue;
                }
                else if (b.target.kind == AITargetKind.Animal) kind = 0.8f;
                float score = b.confidence * (1f / (1f + d / 20f)) * kind;
                if (score > bestScore)
                {
                    bestScore = score;
                    best = b;
                }
            }
            Primary = best;
        }

        public void ClearSeenFlags()
        {
            foreach (var b in _list) b.SeenNow = false;
        }
    }
}
