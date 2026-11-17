using System.Collections.Generic;
using UnityEngine;
using EndlessCombat.Combat;
using EndlessSurvival.Inventory;
using EndlessSurvival.Survival;

namespace EndlessCombat.AI
{
    /// <summary>
    /// Rare prepared moves (Enemy_AI_Deep_Plan.md 21), at most one per encounter (EnemySquad.signatureUsed), off on
    /// Easy (signatureMoves). Each has a clue the player can see and an answer:
    ///   FakeUnaware  21.1  a camp guard pretends not to have seen you; one member walks around behind you.
    ///   LastHider    21.2  the last one hides near the loot and strikes when you turn away or loot.
    ///   PrizeRunner  21.3  a weak squad sends one to grab the chest and run.
    ///   Robbery      21.4  "drop the bag" instead of shooting (panel; test mode takes nothing).
    ///   Ambush       21.5  a roadside squad holds fire until you stop, get out or come close.
    ///   Siege        21.6  you are inside: hold the exits, call out, grenade at 60 s, push at 90 s.
    /// </summary>
    public static class AISignatures
    {
        public const int None = 0, FakeUnaware = 1, LastHider = 2, PrizeRunner = 3, Robbery = 4, Ambush = 5, Siege = 6;
        public static readonly string[] Names = { "-", "fark etmemiş gibi", "saklanan son kişi", "ödülü kaçırma", "soygun", "pusu", "kuşatma" };

        public static float RobberyChance = 0.4f, FakeUnawareChance = 0.25f;
        public static float RobberySeconds = 8f;

        private static bool On(EnemySquad s) => s != null && AIDifficulty.For(s).signatureMoves && !s.signatureUsed;

        private static void Begin(EnemySquad s, int kind, EnemyController member)
        {
            s.signatureUsed = true;
            s.signature = kind;
            s.signatureFlags = 0;
            s.signatureSince = Time.time;
            s.signatureMember = member;
            AIDebug.Log(s, "imza an: " + Names[kind]);
        }

        private static void End(EnemySquad s)
        {
            if (s.signatureMember != null) s.signatureMember.SignatureLocked = false;
            s.signature = None;
            s.signatureMember = null;
        }

        private static Vector3 PlayerPos => AITarget.Player != null ? AITarget.Player.Position : Vector3.zero;

        // ------------------------------------------------------------------ 21.1 fake unaware

        public static bool TryFakeUnaware(EnemyController e, AITarget t)
        {
            var s = e.Squad;
            if (!On(s) || s.kind != SquadKind.Camp || s.Phase != AlertPhase.Calm || s.Alive < 3) return false;
            if (t == null || t.kind != AITargetKind.Player) return false;
            if ((t.Position - e.transform.position).sqrMagnitude < 400f) return false;
            s.signatureUsed = true; // rolled once per squad
            if (Random.value >= FakeUnawareChance) return false;

            // The flanker: the member furthest from the player's believed place
            var b = e.Belief.Of(t);
            Vector3 believed = b != null ? b.position : t.Position;
            EnemyController flanker = null;
            float best = -1f;
            foreach (var m in s.Members)
            {
                if (m == null || m == e || m.IsDead) continue;
                float d = (m.transform.position - believed).sqrMagnitude;
                if (d > best) { best = d; flanker = m; }
            }
            if (flanker == null) return false;
            Begin(s, FakeUnaware, flanker);
            e.suspicionMeter = 20f;
            e.ShowBubble("...", new Color(1f, 1f, 1f, 0.6f), 1.5f);
            foreach (var m in s.Members)
                if (m != null && !m.IsDead) m.Actor.SetReadiness(Readiness.Wary, 0.4f, 0.3f, false);
            Vector3 away = believed - s.home;
            away.y = 0f;
            Vector3 behind = believed + (away.sqrMagnitude > 0.01f ? away.normalized : Vector3.forward) * 25f;
            flanker.Belief.Note(t, believed, Vector3.zero, 0.5f, BeliefSource.Told);
            flanker.GoLookAt(behind);
            return true;
        }

        // ------------------------------------------------------------------ 21.4 robbery

        public static bool TryRobbery(EnemyController e)
        {
            var s = e.Squad;
            if (!On(s) || (s.kind != SquadKind.Patrol && s.kind != SquadKind.Raid)) return false;
            if (s.everFought || PlayerRead.Current.inVehicle || PlayerRead.Current.hand != PlayerHand.None) return false;
            if (s.Alive < 3) return false;
            if (s.LeaderAlive && s.leader.Nerve.aggression >= 0.7f) return false;
            s.signatureUsed = true;
            if (Random.value >= RobberyChance) return false;
            Begin(s, Robbery, s.LeaderAlive ? s.leader : e);
            s.HoldFire = true;
            e.Barks.Say(BarkKind.Demand);
            s.Raise(AlertPhase.Combat);
            return true;
        }

        private static bool _panelOpen;
        private static EnemySquad _robbers;
        private static float _aimAtLeader;
        private static readonly object PanelOwner = new object();

        public static bool PanelOpen => _panelOpen;

        private static void TickRobbery(EnemySquad s)
        {
            float age = Time.time - s.signatureSince;
            EnemyController speaker = s.signatureMember != null && !s.signatureMember.IsDead ? s.signatureMember : null;
            float nearest = float.MaxValue;
            foreach (var m in s.Members)
                if (m != null && !m.IsDead) nearest = Mathf.Min(nearest, Vector3.Distance(m.transform.position, PlayerPos));

            if (s.Alive == 0) { CloseRobbery(s, false); return; }
            if (!_panelOpen && (age > RobberySeconds || nearest > 20f || PlayerRead.Current.inVehicle)) { Fight(s, "süre doldu"); return; }

            // Weapon drawn: a bluff can make a nervous leader back down (P 3-4), otherwise a fight
            if (PlayerRead.Current.hand != PlayerHand.None && PlayerRead.Current.hand != PlayerHand.Throwable)
            {
                if (_panelOpen) _panelOpen = false;
                var leader = speaker;
                bool aimedAtLeader = leader != null && PlayerRead.Current.aiming
                                     && Vector3.Angle(PlayerRead.Current.aimDirection, leader.transform.position + Vector3.up - (PlayerPos + Vector3.up * 1.5f)) < 6f;
                if (aimedAtLeader && s.Power >= 3f && s.Power < 4f)
                {
                    _aimAtLeader += Time.deltaTime;
                    if (_aimAtLeader >= 2f)
                    {
                        leader.Nerve.Add(30f);
                        _aimAtLeader = 0f;
                        if (leader.Nerve.Level >= NerveLevel.Tense)
                        {
                            leader.Barks.Say(BarkKind.Custom, null, "Tamam, sakin ol.");
                            Withdraw(s);
                            return;
                        }
                        Fight(s, "silah çekildi");
                    }
                    return;
                }
                if (_aimAtLeader <= 0f) { Fight(s, "silah çekildi"); return; }
            }
            else _aimAtLeader = 0f;

            // Close to the speaker: X opens the panel
            if (!_panelOpen && speaker != null && Vector3.Distance(speaker.transform.position, PlayerPos) < 4f)
            {
                GameInput.MarkInteractAvailable();
                if (GameInput.InteractPressed)
                {
                    _panelOpen = true;
                    _robbers = s;
                    PadGUI.Focus(PanelOwner, 0);
                }
            }
        }

        private static void Fight(EnemySquad s, string why)
        {
            _panelOpen = false;
            _robbers = null;
            s.HoldFire = false;
            End(s);
            AIDebug.Log(s, "soygun çatışmaya döndü: " + why);
            foreach (var m in s.Members)
                if (m != null && !m.IsDead) m.EnterCombat(AITarget.Player);
        }

        private static void Withdraw(EnemySquad s)
        {
            _panelOpen = false;
            _robbers = null;
            s.HoldFire = false;
            s.witnessDone = true;
            End(s);
            s.SetPhase(AlertPhase.Guarded);
            Vector3 pp = PlayerPos;
            foreach (var m in s.Members)
            {
                if (m == null || m.IsDead) continue;
                m.IgnorePlayerUntil = Time.time + 45f;
                Vector3 away = m.transform.position - pp;
                away.y = 0f;
                if (away.sqrMagnitude < 0.01f) away = Vector3.forward;
                m.RoutineWalk(m.transform.position + away.normalized * 40f, away);
            }
        }

        private static void CloseRobbery(EnemySquad s, bool fight)
        {
            if (fight) Fight(s, "reddetti");
            else
            {
                _panelOpen = false;
                _robbers = null;
                End(s);
            }
        }

        private static void DropBag(EnemySquad s)
        {
            if (!SurvivalTest.FreeResources)
            {
                var inv = SurvivalDirector.Inventory;
                if (inv != null)
                {
                    int take = Mathf.FloorToInt(inv.Money * 0.3f);
                    if (take > 0) inv.SpendMoney(take);
                    var storage = inv.Storage;
                    if (storage != null)
                    {
                        for (int n = 0; n < 5; n++)
                        {
                            ItemDefinition best = null;
                            foreach (var kv in storage.Items)
                                if (kv.Key != null && kv.Value > 0 && (best == null || kv.Key.value > best.value)) best = kv.Key;
                            if (best == null) break;
                            storage.Remove(best, 1);
                        }
                    }
                }
                SurvivalNotify.Warn("Paranın bir kısmını ve değerli eşyalarını aldılar.");
            }
            else SurvivalNotify.Show("Test modu: soyguncular bir şey almadı.");
            Withdraw(s);
        }

        /// <summary>Robbery panel (gamepad: D-pad, A choose, B close = refuse).</summary>
        public static void OnGUI()
        {
            if (!_panelOpen || _robbers == null) return;
            if (GameInput.PadPressed(1)) { CloseRobbery(_robbers, true); return; }
            float w = 420f, h = 170f;
            var rect = new Rect((Screen.width - w) * 0.5f, Screen.height * 0.55f, w, h);
            GUI.Box(rect, "");
            GUILayout.BeginArea(new Rect(rect.x + 14f, rect.y + 12f, w - 28f, h - 24f));
            if (PadGUI.Begin(PanelOwner))
            {
                GUILayout.Label("\"Çantayı bırak, kimse ölmesin.\"");
                GUILayout.Space(8f);
                if (PadGUI.Button(SurvivalTest.FreeResources ? "Çantayı bırak (test modu: hiçbir şey gitmez)" : "Çantayı bırak (paranın %30'u ve en değerli 5 eşya)"))
                    DropBag(_robbers);
                else if (PadGUI.Button("Reddet")) CloseRobbery(_robbers, true);
                PadGUI.End();
            }
            GUILayout.EndArea();
        }

        // ------------------------------------------------------------------ per frame (AISystem)

        public static void Tick()
        {
            var squads = EnemySquad.Squads;
            for (int i = squads.Count - 1; i >= 0; i--)
            {
                var s = i < squads.Count ? squads[i] : null;
                if (s == null || s.IsAbstract) continue;
                switch (s.signature)
                {
                    case Robbery: TickRobbery(s); continue;
                    case FakeUnaware: TickFakeUnaware(s); continue;
                    case LastHider: TickLastHider(s); continue;
                    case PrizeRunner: TickPrizeRunner(s); continue;
                    case Siege: TickSiege(s); continue;
                }
                if (s.kind == SquadKind.Ambush) TickAmbush(s);
                if (s.signatureUsed || !AIDifficulty.For(s).signatureMoves || s.Phase != AlertPhase.Combat) continue;
                TryLastHider(s);
                TryPrizeRunner(s);
                TrySiege(s);
            }
        }

        private static void TickFakeUnaware(EnemySquad s)
        {
            var f = s.signatureMember;
            if (s.Phase == AlertPhase.Combat || f == null || f.IsDead) { End(s); return; }
            if (Time.time - s.signatureSince > 45f || (f.State != EnemyController.EnemyState.Suspicious && Time.time - s.signatureSince > 5f))
            {
                f.Barks.Say(BarkKind.Lost);
                End(s);
                s.SetPhase(AlertPhase.Guarded);
            }
        }

        // ------------------------------------------------------------------ 21.5 ambush

        private static void TickAmbush(EnemySquad s)
        {
            if (!AIDifficulty.For(s).signatureMoves) return;
            if (s.Phase == AlertPhase.Combat && !s.HoldFire) return;
            bool sees = false;
            float nearest = float.MaxValue;
            EnemyController jumpy = null;
            foreach (var m in s.Members)
            {
                if (m == null || m.IsDead) continue;
                nearest = Mathf.Min(nearest, Vector3.Distance(m.transform.position, PlayerPos));
                var b = m.Belief.Of(AITarget.Player);
                if (b != null && b.SeenNow) sees = true;
                if (m.Nerve.discipline < 0.35f && m.Nerve.Level >= NerveLevel.Tense) jumpy = m;
            }
            if (!sees && !s.HoldFire) return;
            if (!s.HoldFire)
            {
                s.signatureUsed = true;
                s.HoldFire = true;
                s.Raise(AlertPhase.Combat);
                AIDebug.Log(s, "pusu: hedef görüldü, ateş yok");
                return;
            }
            var v = SurvivalDirector.Vehicle;
            bool open = !PlayerRead.Current.inVehicle || nearest < 12f || (v != null && v.CurrentSpeedKmh < 10f) || jumpy != null;
            if (!open) return;
            s.HoldFire = false;
            AIDebug.Log(s, jumpy != null ? $"pusu: {jumpy.DisplayName} erken ateş etti" : "pusu: ateş");
            foreach (var m in s.Members)
                if (m != null && !m.IsDead) m.EnterCombat(AITarget.Player);
        }

        // ------------------------------------------------------------------ 21.2 last hider

        private static void TryLastHider(EnemySquad s)
        {
            if (s.Alive != 1 || (s.Stance != Stance.Weak && s.Stance != Stance.Broken)) return;
            EnemyController e = null;
            foreach (var m in s.Members) if (m != null && !m.IsDead) { e = m; break; }
            if (e == null || e.Nerve.courage < 0.4f || e.Surrendered || e.Fleeing) return;
            Vector3 anchor = s.objective != null ? s.objective.position : s.home;
            var b = s.BestBelief(e);
            Vector3 threat = b != null && b.Valid ? b.position : PlayerPos;
            CoverPoint best = null;
            float bestD = float.MaxValue;
            Near.Clear();
            CoverMap.Near(anchor, 12f, Near);
            foreach (var p in Near)
            {
                if (p.reservedBy != null || !CoverMap.Protects(p, threat)) continue;
                float d = (p.position - e.transform.position).sqrMagnitude;
                if (d < bestD) { bestD = d; best = p; }
            }
            if (best == null) return;
            Begin(s, LastHider, e);
            CoverMap.Reserve(best, e);
            e.SignatureLocked = true;
            s.HoldFire = true;
            e.SetState(EnemyController.EnemyState.Idle);
            e.RoutineWalk(best.position, threat - best.position);
        }

        private static readonly List<CoverPoint> Near = new List<CoverPoint>();

        private static void TickLastHider(EnemySquad s)
        {
            var e = s.signatureMember;
            if (e == null || e.IsDead) { s.HoldFire = false; End(s); return; }
            float d = Vector3.Distance(e.transform.position, PlayerPos);
            Vector3 toMe = e.transform.position - PlayerPos;
            toMe.y = 0f;
            bool turnedAway = d < 6f && Vector3.Angle(PlayerRead.Current.facing, toMe) > 100f;
            bool looting = d < 15f && PlayerRead.Intent == PlayerIntent.Looting;
            if (turnedAway || looting || InventoryUI.IsOpen && d < 15f || d < 2.5f)
            {
                s.HoldFire = false;
                End(s);
                e.EnterCombat(AITarget.Player);
                return;
            }
            if (Time.time - s.signatureSince > 120f)
            {
                s.HoldFire = false;
                End(s);
                e.BeginFlee();
            }
        }

        // ------------------------------------------------------------------ 21.3 prize runner

        private static void TryPrizeRunner(EnemySquad s)
        {
            if (s.objective == null || s.Alive < 2 || (s.Stance != Stance.Weak && s.Stance != Stance.Broken)) return;
            EnemyController runner = null;
            float best = float.MaxValue;
            foreach (var m in s.Members)
            {
                if (m == null || m.IsDead || m.Surrendered || m.Fleeing) continue;
                float d = (m.transform.position - s.objective.position).sqrMagnitude;
                if (d < best) { best = d; runner = m; }
            }
            if (runner == null || best > 40f * 40f) return;
            Begin(s, PrizeRunner, runner);
            runner.SignatureLocked = true;
            runner.Barks.Say(BarkKind.Custom, null, "Sandığı al, kaç!");
            runner.SetState(EnemyController.EnemyState.Idle);
            Transform chest = s.objective;
            runner.RoutineWalk(chest.position, Vector3.zero, () =>
            {
                if (runner == null || runner.IsDead || chest == null) return;
                runner.StartCarry(chest);
            });
        }

        private static void TickPrizeRunner(EnemySquad s)
        {
            var r = s.signatureMember;
            if (r == null || r.IsDead) { End(s); return; }
            if (r.Carried != null && Vector3.Distance(r.transform.position, PlayerPos) > 150f)
            {
                Object.Destroy(r.Carried.gameObject);
                r.Carried = null;
                s.objective = null;
                SurvivalNotify.Warn("Sandığı kaçırdılar.");
                End(s);
            }
        }

        // ------------------------------------------------------------------ 21.6 siege

        public static bool PlayerIndoors()
        {
            if (AITarget.Player == null || PlayerRead.Current.inVehicle) return false;
            Vector3 p = AITarget.Player.Position + Vector3.up * 1.8f;
            if (!Physics.Raycast(p, Vector3.up, 6f, ~0, QueryTriggerInteraction.Ignore)) return false;
            int walls = 0;
            for (int i = 0; i < 4; i++)
            {
                Vector3 d = Quaternion.AngleAxis(i * 90f, Vector3.up) * Vector3.forward;
                if (Physics.Raycast(p - Vector3.up * 0.8f, d, 8f, ~0, QueryTriggerInteraction.Ignore)) walls++;
            }
            return walls >= 3;
        }

        private static void TrySiege(EnemySquad s)
        {
            if (s.Tactic != SquadTactic.Encircle || s.Alive < 3 || !PlayerIndoors()) return;
            Begin(s, Siege, null);
        }

        private static void TickSiege(EnemySquad s)
        {
            float age = Time.time - s.signatureSince;
            if (s.Phase != AlertPhase.Combat || s.Alive == 0 || !PlayerIndoors()) { End(s); return; }
            if (age > 30f && (s.signatureFlags & 1) == 0)
            {
                s.signatureFlags |= 1;
                foreach (var m in s.Members)
                    if (m != null && !m.IsDead) { m.Barks.Say(BarkKind.Custom, null, "Çık dışarı!"); break; }
            }
            if (age > 60f && (s.signatureFlags & 2) == 0)
            {
                s.signatureFlags |= 2;
                foreach (var m in s.Members)
                {
                    if (m == null || m.IsDead || m.grenades <= 0) continue;
                    var b = s.BestBelief(m);
                    if (b == null) continue;
                    if (EnemyThrower.CanThrowGrenade(m, b.position, out Vector3 vel, true)) { m.StartGrenadeThrow(b.position, vel); break; }
                }
            }
            if (age > 90f && (s.signatureFlags & 4) == 0)
            {
                s.signatureFlags |= 4;
                s.pushUntil = Time.time + 25f;
                AIDebug.Log(s, "kuşatma: içeri giriyorlar");
            }
            if (age > 130f) End(s);
        }

        // ------------------------------------------------------------------ deaths

        public static void OnMemberDied(EnemyController e)
        {
            var s = e.Squad;
            if (e.Carried != null) e.DropCarried();
            if (s != null && s.signatureMember == e && s.signature == Robbery) Fight(s, "konuşan öldü");
        }
    }
}
