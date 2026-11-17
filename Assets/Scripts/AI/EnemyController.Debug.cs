using UnityEngine;
using EndlessCombat.Combat;

namespace EndlessCombat.AI
{
    /// <summary>Tuning panel page "Düşman" and the route line of the nearest enemy (Enemy_AI_Deep_Plan.md 24).</summary>
    public partial class EnemyController
    {
        public static bool DebugDraw;
        private static bool s_tuningRegistered;
        private static EnemyController s_debugNearest;
        private static int s_debugFrame = -1;
        private static LineRenderer s_debugLine;

        private static void RegisterTuning()
        {
            if (s_tuningRegistered) return;
            s_tuningRegistered = true;
            const string P = "Düşman";
            Tuning.TuningPanel.Register(P, "Aynı anda saldıran (jeton)", () => EnemySquad.MaxAttackers, v => EnemySquad.MaxAttackers = Mathf.RoundToInt(v), 1f, 6f, 1f);
            Tuning.TuningPanel.Register(P, "Jeton tutma süresi (sn)", () => EnemySquad.HoldTime, v => EnemySquad.HoldTime = v, 1f, 15f, 0.5f);
            Tuning.TuningPanel.Register(P, "Jeton bekleme (sn)", () => EnemySquad.RequestCooldown, v => EnemySquad.RequestCooldown = v, 0f, 5f, 0.1f);
            Tuning.TuningPanel.Register(P, "Tüfek menzili (m)", () => RifleRange, v => RifleRange = v, 15f, 80f, 1f);
            Tuning.TuningPanel.Register(P, "Tabanca menzili (m)", () => PistolRange, v => PistolRange = v, 8f, 40f, 1f);
            Tuning.TuningPanel.Register(P, "Tüfek mesafe alt (m)", () => EnemyBrain.RifleBand.x, v => EnemyBrain.RifleBand.x = v, 3f, 30f, 0.5f);
            Tuning.TuningPanel.Register(P, "Tüfek mesafe üst (m)", () => EnemyBrain.RifleBand.y, v => EnemyBrain.RifleBand.y = v, 5f, 40f, 0.5f);
            Tuning.TuningPanel.Register(P, "Tabanca mesafe alt (m)", () => EnemyBrain.PistolBand.x, v => EnemyBrain.PistolBand.x = v, 2f, 20f, 0.5f);
            Tuning.TuningPanel.Register(P, "Tabanca mesafe üst (m)", () => EnemyBrain.PistolBand.y, v => EnemyBrain.PistolBand.y = v, 3f, 30f, 0.5f);
            Tuning.TuningPanel.RegisterToggle(P, "Siper kullan", () => EnemyBrain.UseCover, v => EnemyBrain.UseCover = v);
            Tuning.TuningPanel.Register(P, "Siper arama yarıçapı (m)", () => EnemyBrain.CoverRadius, v => EnemyBrain.CoverRadius = v, 3f, 40f, 0.5f);
            Tuning.TuningPanel.Register(P, "Siperden ateş en az (sn)", () => EnemyBrain.PeekTime.x, v => EnemyBrain.PeekTime.x = v, 0.3f, 5f, 0.1f);
            Tuning.TuningPanel.Register(P, "Siperden ateş en çok (sn)", () => EnemyBrain.PeekTime.y, v => EnemyBrain.PeekTime.y = v, 0.3f, 6f, 0.1f);
            Tuning.TuningPanel.Register(P, "Siperde bekleme (sn)", () => EnemyBrain.HideTime.x, v => { EnemyBrain.HideTime.x = v; EnemyBrain.HideTime.y = v + 1f; }, 0.3f, 6f, 0.1f);
            Tuning.TuningPanel.Register(P, "Yakın dövüş çember (m)", () => EnemyBrain.CircleRadius.x, v => { EnemyBrain.CircleRadius.x = v; EnemyBrain.CircleRadius.y = v + 2f; }, 2f, 12f, 0.5f);
            Tuning.TuningPanel.Register(P, "Kalkanlı dönüş hızı (°/sn)", () => EnemyBrain.ShieldTurnSpeed, v => EnemyBrain.ShieldTurnSpeed = v, 20f, 300f, 5f);
            Tuning.TuningPanel.Register(P, "Yaralı sayılma canı (oran)", () => EnemyBrain.WoundedFraction, v => EnemyBrain.WoundedFraction = v, 0f, 0.8f, 0.05f);
            Tuning.TuningPanel.Register(P, "Karar kararlılığı (puan)", () => EnemyBrain.Steadiness, v => EnemyBrain.Steadiness = v, 0f, 5f, 0.1f);
            Tuning.TuningPanel.Register(P, "Dost ateşi kontrol yarıçapı (m)", () => FriendlyFireRadius, v => FriendlyFireRadius = v, 0.3f, 3f, 0.1f);
            Tuning.TuningPanel.Register(P, "Takılma: ilerleme (m)", () => EnemyMover.StuckDistance, v => EnemyMover.StuckDistance = v, 0.05f, 1.5f, 0.05f);
            Tuning.TuningPanel.RegisterToggle(P, "Yakın düşmanın rotasını çiz", () => DebugDraw, v =>
            {
                DebugDraw = v;
                if (!v && s_debugLine != null) s_debugLine.enabled = false;
            });
            Tuning.TuningPanel.RegisterReadout(P, "NavMesh kurulu", () =>
                LocalNavMeshBuilder.Instance != null && LocalNavMeshBuilder.Instance.HasNavMesh ? "evet" : "hayır");
            Tuning.TuningPanel.RegisterReadout(P, "Saldırı jetonu dolu", () => EnemySquad.TokenCount + " / " + EnemySquad.MaxAttackers);
            Tuning.TuningPanel.RegisterReadout(P, "Yakın düşman: rol", () =>
            {
                var e = DebugNearest();
                if (e == null || e._brain == null) return "-";
                string squadRole = e._squad != null ? ", " + SquadTactics.RoleName(e._squad.RoleOf(e)) : "";
                return RoleText(e._brain.CurrentRole) + squadRole + (e._brain.HasToken ? " (jetonlu)" : "") + (e.IsLeader ? " [lider]" : "");
            });
            Tuning.TuningPanel.RegisterReadout(P, "Yakın düşman: davranış", () =>
            {
                var e = DebugNearest();
                if (e == null || e._brain == null) return "-";
                if (e.Surrendered) return "teslim";
                if (e.Fleeing) return "kaçıyor";
                if (e.currentState != EnemyState.Combat && e.currentState != EnemyState.Searching) return StateText(e.currentState);
                return EnemyBrain.OptionText(e._brain.Choice) + " " + e._brain.ChoiceScore.ToString("0.0")
                       + " / " + EnemyBrain.OptionText(e._brain.Second) + " " + e._brain.SecondScore.ToString("0.0");
            });
            Tuning.TuningPanel.RegisterReadout(P, "Yakın düşman: eylem", () =>
            {
                var e = DebugNearest();
                return e == null || e.Actor == null ? "-" : e.Actor.Text;
            });
            Tuning.TuningPanel.RegisterReadout(P, "Yakın düşman: sinir", () =>
            {
                var e = DebugNearest();
                return e == null || e.Nerve == null ? "-" : e.Nerve.LevelText;
            });
            Tuning.TuningPanel.RegisterReadout(P, "Yakın düşman: inanç", () =>
            {
                var e = DebugNearest();
                if (e == null || e.Belief == null) return "-";
                var b = e.Belief.Primary;
                if (b == null || !b.Valid) return "yok";
                float err = AITarget.Player != null && b.target == AITarget.Player ? Vector3.Distance(b.position, AITarget.Player.Position) : 0f;
                return $"{b.source} %{Mathf.RoundToInt(b.confidence * 100f)}, hata {err:0.0} m" + (b.SeenNow ? ", görüyor" : "");
            });
            Tuning.TuningPanel.RegisterReadout(P, "Yakın düşman: takım", () =>
            {
                var e = DebugNearest();
                if (e == null || e._squad == null) return "-";
                var s = e._squad;
                return $"#{s.id} {s.kind}, {EnemySquad.PhaseName(s.Phase)}, {SquadTactics.Name(s.Tactic)}, güç {s.Power:0.0}, {s.Alive} canlı";
            });
            Tuning.TuningPanel.RegisterReadout(P, "Yakın düşman: rota", () =>
            {
                var e = DebugNearest();
                if (e == null || e._mover == null) return "-";
                return RouteText(e._mover.Route) + ", köşe " + e._mover.CornerCount + (e._mover.Detouring ? ", dolanıyor" : "");
            });
            Tuning.TuningPanel.RegisterReadout(P, "Yakın düşman: takılma", () =>
            {
                var e = DebugNearest();
                return e == null || e._mover == null ? "-" : e._mover.StuckCount.ToString();
            });
            Tuning.TuningPanel.RegisterReadout(P, "Yakın düşman: can / mermi", () =>
            {
                var e = DebugNearest();
                return e == null ? "-" : Mathf.RoundToInt(e.Health01 * 100f) + "%, mermi " + e.MagazineRounds + "+" + Mathf.Max(0, e.TotalAmmo - e.MagazineRounds)
                                         + ", el bombası " + e.grenades + ", sargı " + e.bandages;
            });
        }

        public static EnemyController DebugNearest()
        {
            if (s_debugFrame == Time.frameCount) return s_debugNearest;
            s_debugFrame = Time.frameCount;
            s_debugNearest = null;
            Transform p = CombatBody.Player != null ? CombatBody.Player.transform : null;
            if (p == null) return null;
            float best = float.MaxValue;
            foreach (var e in Active)
            {
                if (e == null || e.IsDead) continue;
                float d = (e.transform.position - p.position).sqrMagnitude;
                if (d < best)
                {
                    best = d;
                    s_debugNearest = e;
                }
            }
            return s_debugNearest;
        }

        private void UpdateDebug()
        {
            if (!DebugDraw || DebugNearest() != this || _mover == null || _brain == null) return;
            if (s_debugLine == null)
            {
                var go = new GameObject("EnemyDebugPath");
                DontDestroyOnLoad(go);
                s_debugLine = go.AddComponent<LineRenderer>();
                s_debugLine.widthMultiplier = 0.06f;
                Shader sh = Shader.Find("Sprites/Default");
                if (sh == null) sh = Shader.Find("Universal Render Pipeline/Unlit");
                if (sh != null) s_debugLine.material = new Material(sh);
                s_debugLine.startColor = s_debugLine.endColor = Color.cyan;
                s_debugLine.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                s_debugLine.receiveShadows = false;
            }
            s_debugLine.enabled = true;
            Vector3 up = Vector3.up * 0.25f;
            int n = _mover.CornerCount;
            s_debugLine.positionCount = n + 2;
            s_debugLine.SetPosition(0, transform.position + up);
            for (int i = 0; i < n; i++) s_debugLine.SetPosition(i + 1, _mover.Corner(i) + up);
            bool fighting = currentState == EnemyState.Combat || currentState == EnemyState.Searching;
            s_debugLine.SetPosition(n + 1, (fighting ? _brain.Goal : _mover.Goal) + up);
        }

        private static string RoleText(EnemyBrain.Role r)
        {
            switch (r)
            {
                case EnemyBrain.Role.Rifle: return "tüfekli";
                case EnemyBrain.Role.Pistol: return "tabancalı";
                case EnemyBrain.Role.Shield: return "kalkanlı";
                default: return "yakın dövüş";
            }
        }

        public static string StateText(EnemyState s)
        {
            switch (s)
            {
                case EnemyState.Suspicious: return "şüpheli";
                case EnemyState.Searching: return "arıyor";
                case EnemyState.Combat: return "çatışmada";
                case EnemyState.Sitting: return "oturuyor";
                case EnemyState.Patrolling: return "devriye";
                default: return "boşta";
            }
        }

        private static string RouteText(EnemyMover.RouteState r)
        {
            switch (r)
            {
                case EnemyMover.RouteState.Full: return "tam";
                case EnemyMover.RouteState.Partial: return "yarım (hedefe varmıyor)";
                case EnemyMover.RouteState.NoNavMesh: return "NavMesh yok (düz yürüyor)";
                case EnemyMover.RouteState.NoPath: return "rota yok (düz yürüyor)";
                default: return "-";
            }
        }
    }
}
