using System.Collections.Generic;
using UnityEngine;
using EndlessCombat.Combat;

namespace EndlessCombat.AI
{
    public enum StimulusKind
    {
        Gunshot, BulletImpact, Footstep, Explosion, RockLand, GrenadeLanded,
        VehicleEngine, Shout, Light, Body, ParkedVehicle, MissingMember, Disturbed, Glint
    }

    /// <summary>One event the AI can hear or see (Enemy_AI_Deep_Plan.md 6). "Noise" is a game event with a radius, not a sound.</summary>
    public struct Stimulus
    {
        public StimulusKind kind;
        public Vector3 position;
        /// <summary>Heard: range. Seen: the furthest distance it can be seen from.</summary>
        public float radius;
        public bool needsSight;
        /// <summary>Who made it (null when unknown).</summary>
        public AITarget source;
        /// <summary>BulletImpact: the direction the bullet came from; RockLand: toward the thrower (rockBacktrace).</summary>
        public Vector3 sourceHint;
        /// <summary>How strongly it says "a target is here" (0..1).</summary>
        public float certainty;
        public float time;
        /// <summary>Persistent id (0 for one shot events).</summary>
        public int id;
        /// <summary>Optional object (the body, the vehicle, the torch).</summary>
        public Object owner;
    }

    /// <summary>
    /// The stimulus bus. One shot events are delivered at once to every enemy in hearing range (needsSight false)
    /// or in sight (needsSight true, checked by the enemy). Persistent ones (bodies, parked vehicle, burning
    /// fires, lit torches) are scanned at 1 Hz by AISystem. CombatHooks.NoiseMade is still raised for heard kinds
    /// (animals and the stress system listen).
    /// </summary>
    public static class AIStimuli
    {
        public static event System.Action<Stimulus> Raised;

        private static readonly Dictionary<int, Stimulus> Persistent = new Dictionary<int, Stimulus>();
        private static readonly List<int> Keys = new List<int>();
        private static int _nextId = 1;

        public static int PersistentCount => Persistent.Count;

        public static void Raise(Stimulus s, bool alsoNoise = true)
        {
            s.time = Time.time;
            if (alsoNoise && !s.needsSight && s.kind != StimulusKind.Shout) CombatHooks.RaiseNoise(s.position, s.radius);
            Deliver(s);
            Raised?.Invoke(s);
            AIDebug.NoteStimulus(s);
        }

        private static void Deliver(Stimulus s)
        {
            var list = EnemyController.Active;
            for (int i = list.Count - 1; i >= 0; i--)
            {
                var e = i < list.Count ? list[i] : null;
                if (e == null || e.IsDead) continue;
                if (s.needsSight)
                {
                    e.OnStimulus(s);
                    continue;
                }
                float r = s.radius * AIDifficulty.For(e.Squad).hearMul * e.HearingFactor;
                if ((e.transform.position - s.position).sqrMagnitude > r * r) continue;
                e.OnStimulus(s);
            }
        }

        public static int RegisterPersistent(Stimulus s)
        {
            s.id = _nextId++;
            s.time = Time.time;
            Persistent[s.id] = s;
            return s.id;
        }

        public static void UpdatePersistent(int id, Stimulus s)
        {
            if (id <= 0) return;
            s.id = id;
            s.time = Time.time;
            Persistent[id] = s;
        }

        /// <summary>Moves a persistent stimulus (vehicle, carried torch) without changing its id.</summary>
        public static void MovePersistent(int id, Vector3 position)
        {
            if (id <= 0 || !Persistent.TryGetValue(id, out Stimulus s)) return;
            s.position = position;
            Persistent[id] = s;
        }

        public static void RemovePersistent(int id)
        {
            if (id > 0) Persistent.Remove(id);
        }

        public static bool TryGetPersistent(int id, out Stimulus s) => Persistent.TryGetValue(id, out s);

        /// <summary>1 Hz: every enemy looks at the persistent stimuli within their radius (sight is checked by the enemy).</summary>
        public static void ScanPersistent()
        {
            if (Persistent.Count == 0) return;
            Keys.Clear();
            Keys.AddRange(Persistent.Keys);
            var enemies = EnemyController.Active;
            foreach (int k in Keys)
            {
                if (!Persistent.TryGetValue(k, out Stimulus s)) continue;
                if (s.owner == null && s.kind != StimulusKind.Light) { } // ownerless ones stay until removed
                float r2 = s.radius * s.radius;
                for (int i = enemies.Count - 1; i >= 0; i--)
                {
                    var e = i < enemies.Count ? enemies[i] : null;
                    if (e == null || e.IsDead) continue;
                    if ((e.transform.position - s.position).sqrMagnitude > r2) continue;
                    e.OnStimulus(s);
                }
            }
        }

        /// <summary>Persistent stimuli of a kind (debug and the camp threat).</summary>
        public static void Collect(StimulusKind kind, List<Stimulus> into)
        {
            foreach (var kv in Persistent) if (kv.Value.kind == kind) into.Add(kv.Value);
        }

        public static void Clear()
        {
            Persistent.Clear();
        }
    }
}
