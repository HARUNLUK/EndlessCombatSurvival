using UnityEngine;
using EndlessCombat.Combat;
using EndlessSurvival.Inventory;
using EndlessSurvival.Survival;

namespace EndlessCombat.AI
{
    public enum PlayerHand { None, Melee, Pistol, Rifle, Throwable }
    public enum PlayerIntent { None, ToVehicle, Fleeing, Looting, Healing, Camping }

    /// <summary>
    /// The player's visible state (Enemy_AI_Deep_Plan.md 14). Updated at 10 Hz. An enemy reads it fresh only while
    /// it sees the player; otherwise it keeps the copy from its last sight (Belief.read).
    /// </summary>
    public static class PlayerRead
    {
        public struct Snapshot
        {
            public CombatAction action;
            public PlayerHand hand;
            public bool aiming;
            public Vector3 aimDirection;
            public bool shieldRaised;
            public Vector3 facing;
            public float health01;
            public bool crouching;
            public bool torchLit;
            public bool inVehicle;
            public float stationary;
            public float time;
        }

        public static Snapshot Current;
        /// <summary>Shots heard since the last reload (counted from the gunshot, no sight needed).</summary>
        public static int ShotsSinceReload;
        public static PlayerIntent Intent { get; private set; }
        public static WorldPickup DroppedWeapon { get; private set; }
        public static Vector3 DroppedAt { get; private set; }

        private static Vector3 _stillCenter;
        private static float _stillSince;
        private static float _lastHealth = -1f, _healedAt = -999f;
        private static float _nextIntent;
        private static float _approachTime, _fleeTime, _lastDist = -1f;
        private static CombatAction _lastAction;
        private static bool _hooked;

        public static void Hook()
        {
            if (_hooked) return;
            _hooked = true;
            CombatHooks.PlayerFired += () => ShotsSinceReload++;
        }

        /// <summary>Typical magazine of the player's gun class (countShots, 14.2).</summary>
        public static int TypicalMagazine => Current.hand == PlayerHand.Pistol ? 6 : 8;

        public static void Tick()
        {
            Hook();
            var player = SurvivalDirector.Player;
            if (player == null) return;
            var s = new Snapshot { time = Time.time, inVehicle = SurvivalDirector.PlayerInVehicle };

            var state = PlayerCombatState.Player;
            s.action = state != null ? state.Current : CombatAction.None;
            if (s.action == CombatAction.Reload && _lastAction != CombatAction.Reload) ShotsSinceReload = 0;
            _lastAction = s.action;

            var shooter = player.GetComponent<PlayerShooter>();
            if (shooter != null)
            {
                switch (shooter.HandType)
                {
                    case WeaponType.Rifle: s.hand = PlayerHand.Rifle; break;
                    case WeaponType.Pistol: s.hand = PlayerHand.Pistol; break;
                    case WeaponType.Melee: s.hand = PlayerHand.Melee; break;
                    default: s.hand = PlayerHand.None; break;
                }
                s.aiming = shooter.IsAiming;
            }
            var thrower = player.GetComponent<PlayerThrower>();
            if (thrower != null && thrower.isActiveAndEnabled && s.action == CombatAction.Throw) s.hand = PlayerHand.Throwable;

            var cam = Camera.main;
            s.aimDirection = cam != null ? cam.transform.forward : player.transform.forward;
            s.facing = player.transform.forward;
            s.shieldRaised = CombatBody.Player != null && CombatBody.Player.IsGuarding;
            var health = player.GetComponent<PlayerHealth>();
            if (health != null)
            {
                s.health01 = Mathf.Clamp01(health.CurrentHealth / Mathf.Max(1f, health.maxHealth));
                if (_lastHealth >= 0f && health.CurrentHealth > _lastHealth + 0.5f) _healedAt = Time.time;
                _lastHealth = health.CurrentHealth;
            }
            var stealth = PlayerStealthController.Instance;
            s.crouching = stealth != null && stealth.IsCrouching;
            var torch = player.GetComponent<TorchHolder>();
            s.torchLit = torch != null && torch.Held;

            Vector3 pos = SurvivalDirector.PlayerPosition;
            if ((pos - _stillCenter).sqrMagnitude > 9f)
            {
                _stillCenter = pos;
                _stillSince = Time.time;
            }
            s.stationary = Time.time - _stillSince;
            Current = s;

            // A weapon that just fell from the hand (14.1 DroppedWeapon)
            DroppedWeapon = null;
            foreach (var p in WorldPickup.All)
            {
                if (p == null || !p.fromHand || Time.time - p.SpawnTime > 10f) continue;
                if (p.item == null || p.item.weaponPrefab == null) continue;
                DroppedWeapon = p;
                DroppedAt = p.transform.position;
                break;
            }
        }

        /// <summary>2 Hz intent guess (14.3); only while at least one enemy sees the player.</summary>
        public static void TickIntent(bool anySeen)
        {
            if (Time.time < _nextIntent) return;
            _nextIntent = Time.time + 0.5f;
            if (!anySeen)
            {
                Intent = PlayerIntent.None;
                _lastDist = -1f;
                return;
            }
            Vector3 p = SurvivalDirector.PlayerPosition;
            var v = AITarget.PlayerVehicle;
            Vector3 vel = AITarget.Player != null ? AITarget.Player.Velocity : Vector3.zero;

            if (InventoryUI.IsOpen) { Intent = PlayerIntent.Looting; return; }
            if (Time.time - _healedAt < 2f) { Intent = PlayerIntent.Healing; return; }

            if (v != null && !Current.inVehicle && EnemySquad.AnyKnowsVehicle)
            {
                Vector3 toCar = v.transform.position - p;
                toCar.y = 0f;
                float d = toCar.magnitude;
                float limit = EnemySquad.AnyVehicleEscapeMemory ? 90f : 60f;
                bool toward = vel.magnitude > 3f && Vector3.Angle(vel, toCar) < 25f && d < limit;
                if (toward && _lastDist > 0f && d < _lastDist) _approachTime += 0.5f;
                else _approachTime = 0f;
                _lastDist = d;
                if (_approachTime >= 1.5f) { Intent = PlayerIntent.ToVehicle; return; }
            }
            if (vel.magnitude > 4f) _fleeTime += 0.5f;
            else _fleeTime = 0f;
            if (_fleeTime >= 2f) { Intent = PlayerIntent.Fleeing; return; }
            if (Current.stationary >= 8f) { Intent = PlayerIntent.Camping; return; }
            Intent = PlayerIntent.None;
        }

        public static string IntentText
        {
            get
            {
                switch (Intent)
                {
                    case PlayerIntent.ToVehicle: return "araca kaçıyor";
                    case PlayerIntent.Fleeing: return "kaçıyor";
                    case PlayerIntent.Looting: return "yağmalıyor";
                    case PlayerIntent.Healing: return "iyileşiyor";
                    case PlayerIntent.Camping: return "yerinde duruyor";
                    default: return "-";
                }
            }
        }
    }
}
