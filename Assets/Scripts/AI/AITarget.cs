using System.Collections.Generic;
using UnityEngine;
using EndlessCombat.Combat;
using EndlessSurvival.Survival;

namespace EndlessCombat.AI
{
    public enum AITargetKind { Player, Vehicle, Human, Animal }

    /// <summary>
    /// Something an AI can target or notice (Enemy_AI_Deep_Plan.md 5): the player, the player's vehicle, human
    /// enemies and animals. The player stays registered while the player object is off inside the vehicle; its
    /// Position then follows the vehicle and InVehicle is true (sight rays aim at the vehicle, 18.1).
    /// </summary>
    public class AITarget : MonoBehaviour
    {
        public AITargetKind kind;
        public CombatTeam team = CombatTeam.Neutral;
        /// <summary>Empty = no faction (filled when the faction system arrives).</summary>
        public string factionId = "";
        public float bodyHeight = 1f;

        public static readonly List<AITarget> All = new List<AITarget>();
        public static AITarget Player { get; private set; }
        public static AITarget PlayerVehicle { get; private set; }

        private IDamageable _damageable;
        private AnimalController _animal;
        private Vector3 _lastPos;
        private float _lastSample;

        /// <summary>Horizontal velocity, sampled at 10 Hz (AISystem).</summary>
        public Vector3 Velocity { get; private set; }

        public static AITarget GetOrAdd(GameObject go, AITargetKind kind, CombatTeam team)
        {
            var t = go.GetComponent<AITarget>();
            if (t == null)
            {
                // AddComponent runs Awake at once, before the kind is set (the default kind is Player): without this
                // guard every enemy and the vehicle took over AITarget.Player for good (user bug report 16 Nov: in the
                // car he was never seen or heard, because "is the player in the vehicle" asked the wrong object)
                s_adding = true;
                try { t = go.AddComponent<AITarget>(); }
                finally { s_adding = false; }
            }
            t.kind = kind;
            t.team = team;
            t.Register();
            return t;
        }

        private static bool s_adding;

        private void Awake()
        {
            if (!s_adding) Register();
        }

        private void Register()
        {
            if (!All.Contains(this)) All.Add(this);
            if (kind == AITargetKind.Player) Player = this;
            else if (Player == this) Player = null;
            if (kind == AITargetKind.Vehicle) PlayerVehicle = this;
            else if (PlayerVehicle == this) PlayerVehicle = null;
            if (_damageable == null)
            {
                foreach (var mb in GetComponents<MonoBehaviour>())
                    if (mb is IDamageable d) { _damageable = d; break; }
            }
            _animal = GetComponent<AnimalController>();
            _lastPos = transform.position;
        }

        private void OnDestroy()
        {
            All.Remove(this);
            if (Player == this) Player = null;
            if (PlayerVehicle == this) PlayerVehicle = null;
        }

        /// <summary>The player is inside the vehicle (the player object is switched off).</summary>
        public bool InVehicle => kind == AITargetKind.Player && SurvivalDirector.PlayerInVehicle && PlayerVehicle != null;

        public bool IsAlive
        {
            get
            {
                if (this == null) return false;
                if (kind == AITargetKind.Vehicle) return true;
                if (_animal != null) return !_animal.IsDead;
                if (_damageable != null && _damageable.IsDead) return false;
                return kind == AITargetKind.Player || gameObject.activeInHierarchy;
            }
        }

        public bool IsPredator => _animal != null && _animal.IsPredator;

        /// <summary>The transform rays hit: the vehicle while the player drives.</summary>
        public Transform SightTransform => InVehicle ? PlayerVehicle.transform : transform;

        public Vector3 Position => InVehicle ? PlayerVehicle.transform.position : transform.position;

        /// <summary>Point sight rays aim at (body centre; the vehicle body at 1 m).</summary>
        public Vector3 Center => Position + Vector3.up * bodyHeight;

        public void SampleVelocity()
        {
            float now = Time.time;
            float dt = now - _lastSample;
            if (dt < 0.09f) return;
            Vector3 p = Position;
            Vector3 v = (p - _lastPos) / Mathf.Max(0.01f, dt);
            v.y = 0f;
            Velocity = dt > 1f ? Vector3.zero : v;
            _lastPos = p;
            _lastSample = now;
        }
    }

    public static class AITargetRegistry
    {
        /// <summary>
        /// Who fights whom. Until factions are coded (Factions_And_Stories.md 1.8) the table is fixed: the Enemy team
        /// is hostile to the player and the player's vehicle; predators are hostile to everyone; prey to nobody.
        /// </summary>
        public static bool VehicleOccupied => AITarget.Player != null && AITarget.Player.InVehicle;

        public static bool IsHostile(AITarget a, AITarget b)
        {
            if (a == null || b == null || a == b) return false;
            if (a.kind == AITargetKind.Animal) return a.IsPredator;
            if (b.kind == AITargetKind.Animal) return b.IsPredator;
            // The empty vehicle is not an enemy: it is only evidence (parked vehicle stimulus, 18.4). Shooting an empty
            // car "because the player is there" looked silly (user bug report, 16 Nov).
            if (a.kind == AITargetKind.Vehicle && !VehicleOccupied) return false;
            if (b.kind == AITargetKind.Vehicle && !VehicleOccupied) return false;
            if (a.team == CombatTeam.Enemy && (b.kind == AITargetKind.Player || b.kind == AITargetKind.Vehicle)) return true;
            if (b.team == CombatTeam.Enemy && (a.kind == AITargetKind.Player || a.kind == AITargetKind.Vehicle)) return true;
            if (a.team == CombatTeam.Neutral || b.team == CombatTeam.Neutral) return false;
            return a.team != b.team;
        }
    }
}
