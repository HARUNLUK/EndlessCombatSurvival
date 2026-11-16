namespace EndlessCombat.Combat
{
    /// <summary>
    /// What the character's arms are doing right now (one at a time). Aiming and the raised shield are stances on
    /// top of these (PlayerCombatState.WantsAim / WantsGuard), the legs are PlayerMotor's business.
    /// </summary>
    public enum CombatAction
    {
        None = 0,
        /// <summary>A shot just left the barrel (short window, the weapon's shot interval, capped).</summary>
        Fire = 1,
        Reload = 2,
        /// <summary>
        /// A weapon is coming to the hand or going back to its holster (Combat_Action_Plan.md 4). Not a lock: the
        /// character moves freely and a hit does not cancel it; only shots and strikes wait for it.
        /// </summary>
        Draw = 3,
        /// <summary>Melee strike or gun butt (CombatBody is attacking).</summary>
        Strike = 4,
        /// <summary>The throw motion (PlayerThrower is throwing).</summary>
        Throw = 5,
        /// <summary>Hit reaction (CombatBody is staggered).</summary>
        Stagger = 6,
        Dead = 7
    }

    /// <summary>What the player asks for (a press). Held buttons (aim, fire held) are read directly.</summary>
    public enum CombatIntent
    {
        /// <summary>RT / left click with a firearm in the hand.</summary>
        Fire = 0,
        /// <summary>RB: quick melee strike with what is in the hand (gun butt / pistol whip with a firearm).</summary>
        Strike = 1,
        Reload = 2,
        /// <summary>A pick from the selection map (LB held) or a keyboard slot key. Slot = PlayerLoadout.Pick code.</summary>
        Swap = 3,
        /// <summary>RT / left click with a rock or grenade in the hand.</summary>
        Throw = 4,
        /// <summary>LB tap: draw the last weapon / put the one in the hand back in its holster.</summary>
        Holster = 5,
        /// <summary>RT with a melee weapon or the fists in the hand: one heavy strike.</summary>
        Heavy = 6
    }

    public enum CombatRule
    {
        /// <summary>Starts now.</summary>
        Allow,
        /// <summary>Not now and forgotten.</summary>
        Deny,
        /// <summary>Not now, but the press waits in the input buffer and starts as soon as it is allowed.</summary>
        Queue,
        /// <summary>The current action is cancelled and this one starts now.</summary>
        Interrupt
    }

    /// <summary>
    /// The one table of which press may start during which action (Combat_Architecture.md, "Kural tablosu").
    /// Rows: the press. Columns: the current action. Change behaviour here, nowhere else.
    /// </summary>
    public static class CombatRules
    {
        private const CombatRule A = CombatRule.Allow, D = CombatRule.Deny, Q = CombatRule.Queue, I = CombatRule.Interrupt;

        //                                   None Fire Reload Draw Strike Throw Stagger Dead
        private static readonly CombatRule[,] Table =
        {
            /* Fire    */ { A,   A,   Q,     Q,   Q,     D,    D,      D },
            /* Strike  */ { A,   A,   I,     Q,   Q,     D,    D,      D },
            /* Reload  */ { A,   Q,   D,     Q,   Q,     D,    D,      D },
            /* Swap    */ { A,   A,   I,     A,   Q,     Q,    D,      D },
            /* Throw   */ { A,   A,   D,     Q,   Q,     D,    D,      D },
            /* Holster */ { A,   A,   I,     A,   Q,     Q,    D,      D },
            /* Heavy   */ { A,   A,   I,     Q,   Q,     D,    D,      D },
        };

        public static CombatRule Get(CombatIntent press, CombatAction current) => Table[(int)press, (int)current];

        /// <summary>Aiming a firearm is possible during these actions (not while a weapon comes to the hand, staggered, dead).</summary>
        public static bool AimAllowedDuring(CombatAction current) =>
            current == CombatAction.None || current == CombatAction.Fire || current == CombatAction.Reload || current == CombatAction.Strike
            || current == CombatAction.Throw;

        /// <summary>The shield can be raised during these actions.</summary>
        public static bool GuardAllowedDuring(CombatAction current) =>
            current != CombatAction.Throw && current != CombatAction.Stagger && current != CombatAction.Dead;

        /// <summary>Turkish name for the test page readout.</summary>
        public static string Name(CombatAction a)
        {
            switch (a)
            {
                case CombatAction.Fire: return "Ateş";
                case CombatAction.Reload: return "Şarjör";
                case CombatAction.Draw: return "Silah ele geliyor / kınına";
                case CombatAction.Strike: return "Vuruş";
                case CombatAction.Throw: return "Fırlatma";
                case CombatAction.Stagger: return "Sersemledi";
                case CombatAction.Dead: return "Öldü";
                default: return "Serbest";
            }
        }
    }
}
