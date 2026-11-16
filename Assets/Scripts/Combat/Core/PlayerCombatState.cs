using System.Collections.Generic;
using UnityEngine;

namespace EndlessCombat.Combat
{
    /// <summary>
    /// The player's combat brain (Combat_Architecture.md). One place that knows:
    ///   what the arms are doing now   Current (None, Fire, Reload, Draw, Strike, Throw, Stagger, Dead), read from
    ///                                 the systems that do the work (CombatBody, the weapon, PlayerThrower)
    ///   who owns the input            InputBlocked (menus, panels, survival actions: CombatInputGate)
    ///   which press may start         CombatRules table; presses wait in a short input buffer (BufferTime)
    ///   what LT means                 WantsAim (firearm in the hand) / WantsGuard (usable shield, no long gun)
    /// PlayerShooter, PlayerMelee, PlayerThrower and PlayerLoadout ask this instead of checking each other.
    /// Runs before them (execution order -60) so the buffer is filled before anyone reads it.
    /// </summary>
    [DefaultExecutionOrder(-60)]
    [DisallowMultipleComponent]
    public class PlayerCombatState : MonoBehaviour
    {
        // ---- tuning (panel page "Dövüş") ----
        /// <summary>Seconds a press waits in the buffer (a strike pressed before the last one ends still comes out).</summary>
        public static float BufferTime = 0.2f;
        /// <summary>Longest "just fired" window (a reload pressed during it waits for it).</summary>
        public static float FireWindowMax = 0.12f;
        private static bool _tuningRegistered;

        public static PlayerCombatState Player { get; private set; }

        /// <summary>Test / debug hook: every press pushed, taken, queued, denied or dropped (null = off).</summary>
        public static System.Action<string> DebugLog;

        public static PlayerCombatState Of(GameObject go)
        {
            if (go == null) return null;
            if (Player != null && Player.gameObject == go) return Player;
            return go.GetComponent<PlayerCombatState>();
        }

        public static PlayerCombatState GetOrAdd(GameObject go)
        {
            var s = Of(go);
            return s != null ? s : go.AddComponent<PlayerCombatState>();
        }

        private struct BufferedPress
        {
            public CombatIntent intent;
            public int slot;
            public float time;
        }

        private readonly List<BufferedPress> _buffer = new List<BufferedPress>(8);
        private PlayerShooter _shooter;
        private PlayerThrower _thrower;
        private CombatBody _body;
        private ShieldHolder _shield;
        private IDamageable _health;
        private float _fireUntil = -1f, _drawUntil = -1f;
        private int _blockedFrame = -1;
        private bool _blocked;

        /// <summary>What the arms are doing now.</summary>
        public CombatAction Current { get; private set; }
        /// <summary>The action of the last frame (to see changes).</summary>
        public CombatAction Previous { get; private set; }
        /// <summary>Raised when Current changes (old, new).</summary>
        public event System.Action<CombatAction, CombatAction> Changed;

        /// <summary>A menu, panel or survival action owns the input: no combat press is taken.</summary>
        public bool InputBlocked
        {
            get
            {
                if (_blockedFrame != Time.frameCount)
                {
                    _blockedFrame = Time.frameCount;
                    _blocked = CombatInputGate.Blocked(gameObject);
                }
                return _blocked;
            }
        }

        /// <summary>LT held with a firearm in the hand, and aiming is possible now.</summary>
        public bool WantsAim
        {
            get
            {
                Refresh();
                return !InputBlocked && CombatRules.AimAllowedDuring(Current) && GameInput.AimHeld
                       && _shooter != null && _shooter.HoldsFirearm;
            }
        }

        /// <summary>
        /// LT held with a usable shield on the arm (fists, one hand melee, pistol; never with a long gun in the hand),
        /// and guarding is possible now. With a pistol LT means both: aim and shield up.
        /// </summary>
        public bool WantsGuard
        {
            get
            {
                Refresh();
                if (_shield == null) _shield = GetComponent<ShieldHolder>();
                return !InputBlocked && CombatRules.GuardAllowedDuring(Current) && GameInput.AimHeld
                       && _shield != null && _shield.Usable && (_shooter == null || _shooter.HandType != WeaponType.Rifle);
            }
        }

        private void Awake()
        {
            Player = this;
            CacheRefs();
            RegisterTuning();
            Damage.RegisterTuning();
            const string P = "Dövüş";
            Tuning.TuningPanel.RegisterReadout(P, "Durum", () => CombatRules.Name(Current) + (WantsAim ? " + nişan" : "") + (WantsGuard ? " + kalkan" : ""), this);
            Tuning.TuningPanel.RegisterReadout(P, "Bekleyen tuşlar", BufferText, this);
        }

        private void OnEnable() => Player = this;

        private void OnDisable()
        {
            _buffer.Clear();
            if (Player == this) Player = null;
        }

        private static void RegisterTuning()
        {
            if (_tuningRegistered) return;
            _tuningRegistered = true;
            const string P = "Dövüş";
            Tuning.TuningPanel.Register(P, "Tuş hafızası (sn)", () => BufferTime, v => BufferTime = v, 0f, 0.6f, 0.02f);
            WeaponDefinition.RegisterTuning();
            Tuning.TuningPanel.Register(P, "Ateş penceresi en çok (sn)", () => FireWindowMax, v => FireWindowMax = v, 0f, 0.5f, 0.01f);
        }

        private void CacheRefs()
        {
            if (_shooter == null) _shooter = GetComponent<PlayerShooter>();
            if (_thrower == null) _thrower = GetComponent<PlayerThrower>();
            if (_body == null) _body = GetComponent<CombatBody>();
            if (_shield == null) _shield = GetComponent<ShieldHolder>();
            if (_health == null)
                foreach (var mb in GetComponents<MonoBehaviour>())
                    if (mb is IDamageable d) { _health = d; break; }
        }

        private void Update()
        {
            CacheRefs();
            Refresh();
            DropOld();

            if (InputBlocked)
            {
                _buffer.Clear();
                return;
            }

            // Presses of this frame into the buffer (Combat_Action_Plan.md 1). What RT / left click mean depends on the
            // hand: a firearm fires, a rock / grenade is thrown, a melee weapon or the fists make the heavy strike (RT)
            // or the quick strike (left click). RB is always the quick strike with what is in the hand.
            // LB (tap: holster, hold: selection map) is read by WeaponSelectMap, which calls Press().
            bool firearm = _shooter != null && _shooter.HoldsFirearm;
            bool throwable = _thrower != null && _thrower.HasItem;
            if (GameInput.LightAttackPressed) Push(CombatIntent.Strike);
            // LT held with the weapon in its holster: LT is drawing the gun, so RT is a shot (it waits for the draw), not a fist strike
            bool gunIntent = firearm || (_shooter != null && _shooter.IsHolstered && GameInput.AimHeld);
            if (GameInput.TriggerPressed) Push(throwable ? CombatIntent.Throw : gunIntent ? CombatIntent.Fire : CombatIntent.Heavy);
            if (GameInput.MouseLeftPressed) Push(firearm ? CombatIntent.Fire : throwable ? CombatIntent.Throw : CombatIntent.Strike);
            if (GameInput.ReloadPressed) Push(CombatIntent.Reload);
            int slot = GameInput.SlotPressed;
            if (slot >= 0) Push(CombatIntent.Swap, slot);
            if (GameInput.HolsterKeyPressed) Push(CombatIntent.Holster);
        }

        /// <summary>Recomputes Current from the systems doing the work. Cheap; called by every query.</summary>
        public void Refresh()
        {
            CombatAction now = Compute();
            if (now == Current) return;
            Previous = Current;
            Current = now;
            if (now == CombatAction.Stagger || now == CombatAction.Dead)
            {
                // A hit reaction cancels a reload and forgets the waiting presses. A weapon coming to the hand keeps
                // coming (Combat_Action_Plan.md 4), a rock / grenade stays in the hand.
                if (_shooter != null && _shooter.CurrentWeapon != null) _shooter.CurrentWeapon.CancelReload();
                _buffer.Clear();
            }
            Changed?.Invoke(Previous, now);
        }

        private CombatAction Compute()
        {
            if ((_body != null && _body.IsDead) || (_health != null && _health.IsDead)) return CombatAction.Dead;
            if (_body != null && _body.isActiveAndEnabled)
            {
                if (_body.IsStaggered) return CombatAction.Stagger;
                if (_body.State == ActorState.Attacking) return CombatAction.Strike;
            }
            if (_thrower != null && _thrower.IsBusy) return CombatAction.Throw;
            float t = Time.time;
            if (t < _drawUntil) return CombatAction.Draw;
            if (_shooter != null && !_shooter.IsHolstered && _shooter.CurrentWeapon != null && _shooter.CurrentWeapon.IsReloading)
                return CombatAction.Reload;
            if (t < _fireUntil) return CombatAction.Fire;
            return CombatAction.None;
        }

        // ============================================================ presses

        /// <summary>A press from outside the keyboard / trigger poll (the LB selection map, tests).</summary>
        public void Press(CombatIntent intent, int slot = -1) => Push(intent, slot);

        private void Push(CombatIntent intent, int slot = -1)
        {
            // One waiting press per kind: the newest wins (a second slot press replaces the first)
            for (int i = _buffer.Count - 1; i >= 0; i--)
                if (_buffer[i].intent == intent) _buffer.RemoveAt(i);
            _buffer.Add(new BufferedPress { intent = intent, slot = slot, time = Time.unscaledTime });
            DebugLog?.Invoke($"push {intent} {slot} during {Current}");
        }

        private void DropOld()
        {
            float limit = Time.unscaledTime - Mathf.Max(0f, BufferTime);
            for (int i = _buffer.Count - 1; i >= 0; i--)
                if (_buffer[i].time < limit)
                {
                    DebugLog?.Invoke($"drop {_buffer[i].intent} (old) during {Current}");
                    _buffer.RemoveAt(i); // a press of this frame always lives one frame
                }
        }

        private int Find(CombatIntent intent)
        {
            for (int i = _buffer.Count - 1; i >= 0; i--)
                if (_buffer[i].intent == intent) return i;
            return -1;
        }

        /// <summary>The press may start now (Allow or Interrupt), without taking it. For held buttons (auto fire, throw).</summary>
        public bool Allows(CombatIntent intent)
        {
            Refresh();
            if (InputBlocked) return false;
            var rule = CombatRules.Get(intent, Current);
            return rule == CombatRule.Allow || rule == CombatRule.Interrupt;
        }

        /// <summary>
        /// Takes a waiting press if the table allows it now. Interrupt cancels the current action first.
        /// Queue keeps the press waiting (it may start in a later frame); Deny forgets it.
        /// </summary>
        public bool TryConsume(CombatIntent intent)
        {
            return TryConsume(intent, out _);
        }

        public bool TryConsume(CombatIntent intent, out int slot)
        {
            slot = -1;
            Refresh();
            int i = Find(intent);
            if (i < 0) return false;
            if (InputBlocked)
            {
                _buffer.RemoveAt(i);
                return false;
            }
            DebugLog?.Invoke($"consume {intent} during {Current}: {CombatRules.Get(intent, Current)}");
            switch (CombatRules.Get(intent, Current))
            {
                case CombatRule.Queue:
                    return false;
                case CombatRule.Deny:
                    _buffer.RemoveAt(i);
                    return false;
                case CombatRule.Interrupt:
                    Interrupt(Current);
                    break;
            }
            slot = _buffer[i].slot;
            _buffer.RemoveAt(i);
            return true;
        }

        /// <summary>Starts an action now without a press (empty magazine reload, the loadout UI). Interrupt applies.</summary>
        public bool TryStart(CombatIntent intent)
        {
            Refresh();
            switch (CombatRules.Get(intent, Current))
            {
                case CombatRule.Allow: return true;
                case CombatRule.Interrupt: Interrupt(Current); return true;
                default: return false;
            }
        }

        private void Interrupt(CombatAction current)
        {
            if (current == CombatAction.Reload && _shooter != null && _shooter.CurrentWeapon != null)
                _shooter.CurrentWeapon.CancelReload();
            Refresh();
        }

        // ============================================================ notes from the doers

        /// <summary>The shooter fired: the arms count as firing for the shot interval (capped).</summary>
        public void NoteFired(float shotInterval)
        {
            _fireUntil = Time.time + Mathf.Clamp(shotInterval, 0f, Mathf.Max(0f, FireWindowMax));
            Refresh();
        }

        /// <summary>
        /// A weapon starts coming to the hand or going back to its holster (Combat_Action_Plan.md 4): shots and strikes
        /// wait for it. A hit does not cancel it; a later pick or holster replaces the time.
        /// </summary>
        public void NoteDraw(float seconds)
        {
            _drawUntil = Time.time + Mathf.Max(0f, seconds);
            _fireUntil = -1f;
            Refresh();
        }

        /// <summary>Ends a draw / holster window early (the holster was taken back).</summary>
        public void ClearDraw()
        {
            _drawUntil = -1f;
            Refresh();
        }

        /// <summary>True while a weapon is coming to the hand or going to its holster.</summary>
        public bool Drawing => Time.time < _drawUntil;

        private string BufferText()
        {
            if (_buffer.Count == 0) return "-";
            var sb = new System.Text.StringBuilder();
            foreach (var p in _buffer)
            {
                if (sb.Length > 0) sb.Append(", ");
                sb.Append(p.intent);
                if (p.slot >= 0) sb.Append(' ').Append(p.slot);
            }
            return sb.ToString();
        }
    }
}
