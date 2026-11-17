using System.Collections.Generic;
using UnityEngine;
using EndlessSurvival.Survival;

namespace EndlessCombat.AI
{
    /// <summary>
    /// AI debugging and the fight summary (Enemy_AI_Deep_Plan.md 24): an event log per squad (source of the
    /// summary written when a squad is gone), in-game lines (beliefs, cover points, squads, stimulus rings) drawn
    /// by AISystem with GL, and the switches of the tuning page "Düşman AI".
    /// </summary>
    public static class AIDebug
    {
        public static bool DrawBeliefs, DrawCover, DrawSquads, DrawStimuli, ForceGhost, LogToConsole;
        public static bool ShowSummary = true;

        public struct Line
        {
            public Vector3 a, b;
            public Color color;
        }

        public static readonly List<Line> Lines = new List<Line>();

        private struct Ring
        {
            public Vector3 center;
            public float radius;
            public Color color;
            public float until;
        }

        private static readonly List<Ring> Rings = new List<Ring>();
        private static readonly Dictionary<int, List<string>> SquadLog = new Dictionary<int, List<string>>();
        public static readonly List<string> Recent = new List<string>();

        public static void Add(Vector3 a, Vector3 b, Color c)
        {
            if (Lines.Count < 4000) Lines.Add(new Line { a = a, b = b, color = c });
        }

        public static void Circle(Vector3 center, float radius, Color c, int segments = 24)
        {
            Vector3 prev = center + new Vector3(radius, 0f, 0f);
            for (int i = 1; i <= segments; i++)
            {
                float t = i / (float)segments * Mathf.PI * 2f;
                Vector3 p = center + new Vector3(Mathf.Cos(t) * radius, 0f, Mathf.Sin(t) * radius);
                Add(prev, p, c);
                prev = p;
            }
        }

        // ------------------------------------------------------------------ log

        public static void Log(EnemySquad s, string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            string line = $"[{Time.time:0.0}] " + (s != null ? $"#{s.id} " : "") + text;
            Recent.Add(line);
            if (Recent.Count > 30) Recent.RemoveAt(0);
            if (s != null)
            {
                if (!SquadLog.TryGetValue(s.id, out var list))
                {
                    list = new List<string>();
                    SquadLog[s.id] = list;
                }
                if (list.Count < 200) list.Add(text);
            }
            if (LogToConsole) Debug.Log("[AI] " + line);
        }

        public static void NoteStimulus(Stimulus s)
        {
            if (!DrawStimuli) return;
            Color c;
            switch (s.kind)
            {
                case StimulusKind.Gunshot: c = Color.red; break;
                case StimulusKind.BulletImpact: c = new Color(1f, 0.5f, 0f); break;
                case StimulusKind.Footstep: c = Color.white; break;
                case StimulusKind.Explosion: c = Color.magenta; break;
                case StimulusKind.Shout: c = Color.yellow; break;
                case StimulusKind.VehicleEngine: c = Color.cyan; break;
                default: c = Color.grey; break;
            }
            if (Rings.Count < 60) Rings.Add(new Ring { center = s.position + Vector3.up * 0.1f, radius = Mathf.Min(s.radius, 120f), color = c, until = Time.time + 2f });
        }

        /// <summary>The squad is gone: 2-4 lines on what happened (24.1).</summary>
        public static void Summary(EnemySquad s)
        {
            if (s == null) return;
            SquadLog.Remove(s.id);
            var sb = new System.Text.StringBuilder();
            int n = Mathf.Max(s.startCount, 1);
            sb.Append(n == 1 ? "Tek kişiydi." : $"{NumberWord(n)} kişiydiler.");
            if (s.timesLost > 0) sb.Append(s.timesLost == 1 ? " Seni bir kez kaybettiler." : $" Seni {s.timesLost} kez kaybettiler.");
            if (s.TacticsTried.Count > 0)
            {
                sb.Append(' ');
                for (int i = 0; i < s.TacticsTried.Count && i < 3; i++)
                {
                    if (i > 0) sb.Append(i == s.TacticsTried.Count - 1 || i == 2 ? " ve " : ", ");
                    sb.Append(s.TacticsTried[i]);
                }
                sb.Append(" denediler.");
            }
            string end = s.summaryEnd;
            if (string.IsNullOrEmpty(end))
            {
                if (s.fled > 0 && s.killedByPlayer > 0) end = s.fled == 1 ? "Biri kaçtı." : $"{s.fled} kişi kaçtı.";
                else if (s.fled > 0) end = "Dağılıp kaçtılar.";
                else if (s.killedByPlayer >= n) end = "Hepsi öldü.";
                else end = "Geri çekildiler.";
            }
            sb.Append(' ').Append(end);
            Log(null, sb.ToString());
            if (ShowSummary && !EndlessSurvival.Testing.TestArena.Active) SurvivalNotify.Show(sb.ToString());
            else if (ShowSummary) Debug.Log("[AI özet] " + sb);
        }

        private static string NumberWord(int n)
        {
            switch (n)
            {
                case 2: return "İki";
                case 3: return "Üç";
                case 4: return "Dört";
                case 5: return "Beş";
                case 6: return "Altı";
                case 7: return "Yedi";
                case 8: return "Sekiz";
                default: return n.ToString();
            }
        }

        // ------------------------------------------------------------------ drawing (filled once a frame by AISystem)

        public static void DrawCoverPoints(List<CoverPoint> points)
        {
            if (!DrawCover || points == null || AITarget.Player == null) return;
            Vector3 pp = AITarget.Player.Position;
            foreach (var p in points)
            {
                if ((p.position - pp).sqrMagnitude > 45f * 45f) continue;
                Color c = p.reservedBy != null ? Color.red : p.height == CoverHeight.Low ? Color.yellow : Color.green;
                Add(p.position, p.position + Vector3.up * (p.height == CoverHeight.Low ? 0.9f : 1.5f), c);
                Add(p.position + Vector3.up * 0.2f, p.position + Vector3.up * 0.2f + p.normal * 0.5f, c);
            }
        }

        public static void Collect()
        {
            if (DrawCover && !CoverMap.DebugDraw)
            {
                // CoverMap only draws when its own flag is on; list the points directly
                var pts = CoverMap.All;
                if (AITarget.Player != null)
                {
                    Vector3 pp = AITarget.Player.Position;
                    for (int i = 0; i < pts.Count; i++)
                    {
                        var p = pts[i];
                        if ((p.position - pp).sqrMagnitude > 45f * 45f) continue;
                        Color c = p.reservedBy != null ? Color.red : p.height == CoverHeight.Low ? Color.yellow : Color.green;
                        Add(p.position, p.position + Vector3.up * (p.height == CoverHeight.Low ? 0.9f : 1.5f), c);
                        Add(p.position + Vector3.up * 0.2f, p.position + Vector3.up * 0.2f + p.normal * 0.5f, c);
                    }
                }
            }
            if (DrawBeliefs)
            {
                foreach (var e in EnemyController.Active)
                {
                    if (e == null || e.IsDead || e.Belief == null) continue;
                    var b = e.Belief.Primary;
                    if (b == null || !b.Valid) continue;
                    Color c = b.SeenNow ? Color.red : Color.Lerp(new Color(0.3f, 0.3f, 1f), Color.yellow, b.confidence);
                    Add(e.transform.position + Vector3.up * 1.6f, b.position + Vector3.up * 1f, c);
                    Circle(b.position + Vector3.up * 0.05f, 0.3f + (1f - b.confidence) * 2f, c, 12);
                }
            }
            if (DrawSquads)
            {
                foreach (var s in EnemySquad.Squads)
                {
                    EnemyController first = null;
                    foreach (var m in s.Members)
                    {
                        if (m == null || m.IsDead || !m.isActiveAndEnabled) continue;
                        if (first != null) Add(first.transform.position + Vector3.up * 2.1f, m.transform.position + Vector3.up * 2.1f, new Color(1f, 1f, 1f, 0.4f));
                        else first = m;
                        Color rc = RoleColor(s.RoleOf(m));
                        Add(m.transform.position + Vector3.up * 2.1f, m.transform.position + Vector3.up * 2.5f, rc);
                    }
                    if (s.Shared.Valid) Circle(s.Shared.position + Vector3.up * 0.05f, 1f, Color.magenta, 16);
                }
            }
            for (int i = Rings.Count - 1; i >= 0; i--)
            {
                if (Time.time > Rings[i].until) { Rings.RemoveAt(i); continue; }
                Circle(Rings[i].center, Rings[i].radius, Rings[i].color, 32);
            }
        }

        private static Color RoleColor(SquadRole r)
        {
            switch (r)
            {
                case SquadRole.Suppressor: return new Color(1f, 0.5f, 0f);
                case SquadRole.Flanker: return Color.cyan;
                case SquadRole.Pointman: return Color.red;
                case SquadRole.Assault: return new Color(1f, 0.2f, 0.6f);
                case SquadRole.Grenadier: return Color.green;
                case SquadRole.Guard: return Color.blue;
                case SquadRole.Runner: return Color.yellow;
                default: return Color.white;
            }
        }

        public static void Clear()
        {
            Lines.Clear();
        }

        // ------------------------------------------------------------------ panel (24.2)

        private static bool s_registered;

        public static void RegisterTuning()
        {
            if (s_registered) return;
            s_registered = true;
            const string P = "Düşman AI";
            Tuning.TuningPanel.RegisterToggle(P, "Kafada durum yazısı", () => EnemyController.ShowStatusText, v => EnemyController.ShowStatusText = v);
            Tuning.TuningPanel.RegisterToggle(P, "İnanç çiz", () => DrawBeliefs, v => DrawBeliefs = v);
            Tuning.TuningPanel.RegisterToggle(P, "Siper noktalarını çiz", () => DrawCover, v => DrawCover = v);
            Tuning.TuningPanel.RegisterToggle(P, "Takım çiz", () => DrawSquads, v => DrawSquads = v);
            Tuning.TuningPanel.RegisterToggle(P, "Uyaranları çiz", () => DrawStimuli, v => DrawStimuli = v);
            Tuning.TuningPanel.RegisterToggle(P, "Hayalet işareti zorla", () => ForceGhost, v => ForceGhost = v);
            Tuning.TuningPanel.RegisterToggle(P, "Olayları konsola yaz", () => LogToConsole, v => LogToConsole = v);
            Tuning.TuningPanel.RegisterToggle(P, "Çatışma özeti göster", () => ShowSummary, v => ShowSummary = v);

            Tuning.TuningPanel.Register(P, "Sinir: isabet", () => EnemyNerve.HitGain, v => EnemyNerve.HitGain = v, 0f, 60f, 1f);
            Tuning.TuningPanel.Register(P, "Sinir: yakın sıyırma", () => EnemyNerve.NearMissGain, v => EnemyNerve.NearMissGain = v, 0f, 30f, 1f);
            Tuning.TuningPanel.Register(P, "Sinir: arkadaş ölümü", () => EnemyNerve.FriendDeathGain, v => EnemyNerve.FriendDeathGain = v, 0f, 60f, 1f);
            Tuning.TuningPanel.Register(P, "Güç: üstün eşiği", () => EnemySquad.DominantAt, v => EnemySquad.DominantAt = v, 1f, 6f, 0.1f);
            Tuning.TuningPanel.Register(P, "Güç: denk eşiği", () => EnemySquad.EvenAt, v => EnemySquad.EvenAt = v, 0.5f, 4f, 0.1f);
            Tuning.TuningPanel.Register(P, "Güç: zayıf eşiği", () => EnemySquad.WeakAt, v => EnemySquad.WeakAt = v, 0.1f, 2f, 0.05f);
            Tuning.TuningPanel.Register(P, "Taktik en az süre (sn)", () => EnemySquad.MinTacticTime, v => EnemySquad.MinTacticTime = v, 1f, 20f, 0.5f);
            Tuning.TuningPanel.Register(P, "Balon süresi (sn)", () => EnemyBarks.BubbleSeconds, v => EnemyBarks.BubbleSeconds = v, 0.5f, 5f, 0.1f);
            Tuning.TuningPanel.Register(P, "Arama halkası 1 (m)", () => EnemySearch.Rings[0], v => EnemySearch.Rings[0] = v, 3f, 20f, 1f);
            Tuning.TuningPanel.Register(P, "Arama halkası 2 (m)", () => EnemySearch.Rings[1], v => EnemySearch.Rings[1] = v, 6f, 30f, 1f);
            Tuning.TuningPanel.Register(P, "Arama halkası 3 (m)", () => EnemySearch.Rings[2], v => EnemySearch.Rings[2] = v, 10f, 50f, 1f);
            Tuning.TuningPanel.Register(P, "Takip hızı (m/sn)", () => AbstractGroups.PursuitSpeed, v => AbstractGroups.PursuitSpeed = v, 0.5f, 10f, 0.5f);
            Tuning.TuningPanel.Register(P, "Takibi bırakma (m)", () => AbstractGroups.PursuitDropDistance, v => AbstractGroups.PursuitDropDistance = v, 300f, 5000f, 100f);
            Tuning.TuningPanel.Register(P, "Kamp görünürlüğü: gece ateş", () => AbstractGroups.FireNight, v => AbstractGroups.FireNight = v, 0f, 1f, 0.05f);
            Tuning.TuningPanel.Register(P, "Kamp görünürlüğü: gündüz ateş", () => AbstractGroups.FireDay, v => AbstractGroups.FireDay = v, 0f, 1f, 0.05f);
            Tuning.TuningPanel.Register(P, "Kamp görünürlüğü: ateş yok", () => AbstractGroups.NoFire, v => AbstractGroups.NoFire = v, 0f, 1f, 0.05f);
            Tuning.TuningPanel.Register(P, "Alışkanlık artışı", () => AIMemory.Gain, v => AIMemory.Gain = v, 0f, 1f, 0.02f);
            Tuning.TuningPanel.Register(P, "Sürücüye geçen hasar oranı", () => VehicleThreat.DriverDamageShare, v => VehicleThreat.DriverDamageShare = v, 0f, 2f, 0.05f);
            for (int i = 0; i < EnemyBrain.OptionMul.Length; i++)
            {
                int k = i;
                Tuning.TuningPanel.Register(P, "Seçenek çarpanı: " + EnemyBrain.OptionText((EnemyBrain.Option)k), () => EnemyBrain.OptionMul[k], v => EnemyBrain.OptionMul[k] = v, 0f, 3f, 0.05f);
            }

            Tuning.TuningPanel.RegisterReadout(P, "AI süresi (ms)", () => AISystem.LastMs.ToString("0.00"));
            Tuning.TuningPanel.RegisterReadout(P, "Düşman / takım", () => EnemyController.Active.Count + " / " + EnemySquad.Squads.Count);
            Tuning.TuningPanel.RegisterReadout(P, "Siper noktası sayısı", () => CoverMap.Count.ToString());
            Tuning.TuningPanel.RegisterReadout(P, "Kalıcı uyaran", () => AIStimuli.PersistentCount.ToString());
            Tuning.TuningPanel.RegisterReadout(P, "Oyuncu niyeti (tahmin)", () => PlayerRead.IntentText);
            Tuning.TuningPanel.RegisterReadout(P, "Takip grubu", AbstractGroups.PursuitText);
            Tuning.TuningPanel.RegisterReadout(P, "Alışkanlıklar", AIMemory.HabitsText);
            Tuning.TuningPanel.RegisterReadout(P, "Son olay", () => Recent.Count > 0 ? Recent[Recent.Count - 1] : "-");

            Tuning.TuningPanel.RegisterAction(P, "Yakın takımı Tetikte yap", () =>
            {
                var e = EnemyController.DebugNearest();
                e?.Squad?.SetPhase(AlertPhase.Guarded);
            });
            Tuning.TuningPanel.RegisterAction(P, "Yakın takımın liderini öldür", () =>
            {
                var e = EnemyController.DebugNearest();
                var l = e != null && e.Squad != null ? e.Squad.leader : null;
                if (l != null && !l.IsDead) l.TakeDamage(new EndlessCombat.Combat.DamageInfo(99999f, EndlessCombat.Combat.DamageType.Other, l.transform.position, Vector3.up));
            });
            Tuning.TuningPanel.RegisterAction(P, "Takip grubu başlat (300 m geride)", () =>
            {
                Vector3 back = AITarget.Player != null ? AITarget.Player.Position - (Camera.main != null ? Camera.main.transform.forward : Vector3.forward) * 300f : Vector3.zero;
                AbstractGroups.StartPursuit(back);
            });
            Tuning.TuningPanel.RegisterAction(P, "Bütün sinirleri 80 yap", () =>
            {
                foreach (var e in EnemyController.Active) if (e != null && !e.IsDead) e.Nerve.Set(80f);
            });
            Tuning.TuningPanel.RegisterAction(P, "Alışkanlıkları sıfırla", AIMemory.ResetAll);
        }
    }
}
