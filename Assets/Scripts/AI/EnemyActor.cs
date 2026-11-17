using UnityEngine;

namespace EndlessCombat.AI
{
    public enum Readiness { Relaxed, Wary, Ready }
    public enum EnemyAction { None, Fire, Reload, Draw, Strike, Throw, Stagger, Heal, Pickup, Dead }

    /// <summary>
    /// Readiness and the one timed action of an enemy (Enemy_AI_Deep_Plan.md 9), the same logic as the player's
    /// CombatRules: drawing blocks shots, a throw or a bandage stops walking, a stagger cancels reload / throw /
    /// heal / pickup (a cancelled throw drops the grenade at the feet).
    /// </summary>
    public class EnemyActor
    {
        public Readiness Readiness { get; private set; } = Readiness.Relaxed;
        public EnemyAction Current { get; private set; }
        public float Remaining { get; private set; }
        private System.Action _onDone, _onInterrupted;

        /// <summary>Until this time the enemy cannot shoot (reaction + draw after being relaxed).</summary>
        public float ReadyAt { get; private set; }

        public bool Busy => Current != EnemyAction.None && Current != EnemyAction.Fire;
        public bool CanShoot => Time.time >= ReadyAt && (Current == EnemyAction.None || Current == EnemyAction.Fire);
        public bool BlocksMove => Current == EnemyAction.Throw || Current == EnemyAction.Heal || Current == EnemyAction.Pickup;
        public bool Drawing => Time.time < ReadyAt;

        public bool TryStart(EnemyAction a, float seconds, System.Action onDone, System.Action onInterrupted = null)
        {
            if (Busy) return false;
            Current = a;
            Remaining = Mathf.Max(0.01f, seconds);
            _onDone = onDone;
            _onInterrupted = onInterrupted;
            return true;
        }

        /// <summary>A stagger: throw / heal / pickup / reload stop (Draw keeps going).</summary>
        public void Interrupt()
        {
            if (Current == EnemyAction.None || Current == EnemyAction.Fire || Current == EnemyAction.Draw) return;
            var cb = _onInterrupted;
            Current = EnemyAction.None;
            Remaining = 0f;
            _onDone = null;
            _onInterrupted = null;
            cb?.Invoke();
        }

        public void Cancel()
        {
            Current = EnemyAction.None;
            Remaining = 0f;
            _onDone = null;
            _onInterrupted = null;
        }

        /// <summary>
        /// Readiness change. Going up from Relaxed costs reaction time + weapon draw; from Wary only the reaction.
        /// </summary>
        public void SetReadiness(Readiness r, float reaction, float drawTime, bool sitting)
        {
            if (r == Readiness) return;
            if (r > Readiness)
            {
                float delay = reaction;
                if (Readiness == Readiness.Relaxed) delay += drawTime + (sitting ? 0.8f : 0f);
                ReadyAt = Mathf.Max(ReadyAt, Time.time + delay);
            }
            Readiness = r;
        }

        /// <summary>A new sight of a target after a while: the reaction delay before the first shot.</summary>
        public void React(float reaction) => ReadyAt = Mathf.Max(ReadyAt, Time.time + reaction);

        public void Tick(float dt)
        {
            if (Current == EnemyAction.None || Current == EnemyAction.Fire) return;
            Remaining -= dt;
            if (Remaining > 0f) return;
            var cb = _onDone;
            Current = EnemyAction.None;
            _onDone = null;
            _onInterrupted = null;
            cb?.Invoke();
        }

        public string Text
        {
            get
            {
                switch (Current)
                {
                    case EnemyAction.Reload: return "şarjör";
                    case EnemyAction.Draw: return "silah çekiyor";
                    case EnemyAction.Throw: return "bomba hazırlığı";
                    case EnemyAction.Heal: return "sargı";
                    case EnemyAction.Pickup: return "eşya alıyor";
                    default: return Drawing ? "hazırlanıyor" : "-";
                }
            }
        }
    }
}
