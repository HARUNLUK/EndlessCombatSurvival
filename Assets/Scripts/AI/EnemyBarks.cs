using System.Collections.Generic;
using UnityEngine;

namespace EndlessCombat.AI
{
    public enum BarkKind
    {
        Contact, ShotsHeard, Lost, Evidence, Missing, Flanking, Covering, Reloading, Grenade, GrenadeSeen, Charging,
        PlayerReloading, ToVehicle, ManDown, LeaderDown, FallBack, Push, Searching, AllClear, Surrender, Fleeing, Demand,
        Custom
    }

    /// <summary>
    /// Shouts and speech bubbles (Enemy_AI_Deep_Plan.md 11). Information spreads only by shouting: a shout has a
    /// delay (a faint "..." meanwhile), a range and a visible bubble. A speaker who dies or is staggered before the
    /// delay is over says nothing and the news does not travel. No sounds: bubbles and a screen edge marker.
    /// Lines were written for this game.
    /// </summary>
    public class EnemyBarks
    {
        public enum Tone { White, Yellow, Red }

        private struct Meta
        {
            public Tone tone;
            public int priority;
            public float cooldown;
            public bool info;
            public string[] lines;
            public Meta(Tone t, int p, float c, bool i, params string[] l) { tone = t; priority = p; cooldown = c; info = i; lines = l; }
        }

        private static readonly Dictionary<BarkKind, Meta> Table = new Dictionary<BarkKind, Meta>
        {
            { BarkKind.Contact, new Meta(Tone.Yellow, 8, 6f, true, "Orada!", "Gördüm!", "Biri var!") },
            { BarkKind.ShotsHeard, new Meta(Tone.Yellow, 6, 8f, true, "Silah sesi!", "Ateş ediyorlar!") },
            { BarkKind.Lost, new Meta(Tone.White, 5, 5f, true, "Burada yok!", "Kaybettim!") },
            { BarkKind.Evidence, new Meta(Tone.Yellow, 6, 10f, true, "Ceset var!", "Araba! Biri var buralarda.", "Sandık açılmış!") },
            { BarkKind.Missing, new Meta(Tone.White, 4, 30f, true, "{ad} nerede?", "Biri eksik, nerede kaldı?") },
            { BarkKind.Flanking, new Meta(Tone.White, 4, 10f, false, "Soldan dolanıyorum!", "Arkasına geçiyorum!", "Sağdan dolanıyorum!") },
            { BarkKind.Covering, new Meta(Tone.White, 3, 10f, false, "Ben tutuyorum, git!") },
            { BarkKind.Reloading, new Meta(Tone.Yellow, 5, 4f, false, "Şarjör!", "Dolduruyorum!") },
            { BarkKind.Grenade, new Meta(Tone.Red, 10, 0f, false, "Bomba!") },
            { BarkKind.GrenadeSeen, new Meta(Tone.Yellow, 9, 2f, true, "Bomba! Kaç!") },
            { BarkKind.Charging, new Meta(Tone.Red, 9, 3f, false, "Geliyorum!", "Şimdi!") },
            { BarkKind.PlayerReloading, new Meta(Tone.White, 7, 6f, true, "Dolduruyor, yüklen!", "Mermisi bitti!") },
            { BarkKind.ToVehicle, new Meta(Tone.White, 7, 8f, true, "Arabaya kaçıyor!", "Arabayı tut!") },
            { BarkKind.ManDown, new Meta(Tone.Yellow, 6, 3f, false, "{ad}'ı vurdular!", "Biri düştü!") },
            { BarkKind.LeaderDown, new Meta(Tone.Yellow, 8, 0f, true, "Reis düştü!") },
            { BarkKind.FallBack, new Meta(Tone.White, 6, 8f, false, "Geri çekil!", "Sipere!") },
            { BarkKind.Push, new Meta(Tone.Red, 7, 8f, false, "Üstüne!", "Yüklenin!") },
            { BarkKind.Searching, new Meta(Tone.White, 2, 12f, false, "Şuraya bakıyorum.", "Çık dışarı.") },
            { BarkKind.AllClear, new Meta(Tone.White, 2, 0f, true, "Kimse yok. Gözünüz açık olsun.") },
            { BarkKind.Surrender, new Meta(Tone.Yellow, 9, 0f, false, "Ateş etme! Bırakıyorum!") },
            { BarkKind.Fleeing, new Meta(Tone.Yellow, 5, 0f, false, "Ben yokum!", "Kaçın!") },
            { BarkKind.Demand, new Meta(Tone.Yellow, 7, 0f, false, "Elini silahtan çek. Çantayı bırak.") },
            { BarkKind.Custom, new Meta(Tone.White, 5, 0f, false, "...") },
        };

        public static readonly string[] Names =
        {
            "Cemal", "Rıza", "Haydar", "Sabri", "Kenan", "Nuri", "Bayram", "Ferit", "Hamdi", "Selim",
            "Kadir", "Yaşar", "Tahsin", "Remzi", "Orhan", "Zeki", "Şükrü", "Vedat", "Lütfi", "Cevdet"
        };

        // ---- tuning ----
        public static float BubbleSeconds = 1.6f;
        public static int MaxBubbles = 2;

        public static Color ColorOf(Tone t) => t == Tone.Red ? new Color(1f, 0.3f, 0.25f) : t == Tone.Yellow ? new Color(1f, 0.85f, 0.3f) : Color.white;

        // ---- visible bubbles and edge markers (AIOverlay draws the markers) ----
        public struct Shown
        {
            public EnemyController speaker;
            public string text;
            public Color color;
            public int priority;
            public float until;
        }
        public static readonly List<Shown> Visible = new List<Shown>();

        // ---- per enemy ----
        private readonly EnemyController _e;
        private readonly Dictionary<BarkKind, float> _ready = new Dictionary<BarkKind, float>();
        private bool _pending;
        private BarkKind _pendingKind;
        private float _pendingAt;
        private Belief _pendingInfo;
        private string _pendingText;
        private int _pendingShoutId;
        public BarkKind LastKind { get; private set; } = BarkKind.Custom;
        public float LastTime { get; private set; } = -999f;
        public bool Pending => _pending;
        public BarkKind PendingKind => _pendingKind;

        private static int _nextShoutId = 1;

        public EnemyBarks(EnemyController e) => _e = e;

        public static bool CarriesInfo(BarkKind k) => Table.TryGetValue(k, out var m) && m.info;
        public static int Priority(BarkKind k) => Table.TryGetValue(k, out var m) ? m.priority : 0;

        /// <summary>Starts a shout. Higher priority replaces a pending lower one. Info shouts wait shoutDelay.</summary>
        public bool Say(BarkKind kind, Belief info = null, string text = null, int relayOf = 0)
        {
            if (_e == null || _e.IsDead) return false;
            if (!Table.TryGetValue(kind, out Meta m)) return false;
            if (_ready.TryGetValue(kind, out float until) && Time.time < until) return false;
            if (_pending && Priority(_pendingKind) > m.priority) return false;
            var p = AIDifficulty.For(_e.Squad);
            _ready[kind] = Time.time + m.cooldown;
            _pending = true;
            _pendingKind = kind;
            _pendingText = text;
            _pendingInfo = null;
            if (info != null)
            {
                _pendingInfo = new Belief();
                _pendingInfo.CopyFrom(info);
            }
            _pendingShoutId = relayOf != 0 ? relayOf : _nextShoutId++;
            // Urgent threats (grenade, charge) are said at once; news waits the shout delay
            float delay = m.tone == Tone.Red || kind == BarkKind.Surrender || kind == BarkKind.Fleeing ? 0.05f : p.shoutDelay;
            _pendingAt = Time.time + delay;
            EndlessCombat.Audio.GameSound.Play(EndlessCombat.Audio.SoundId.EnemyShout, _e.transform.position, 1f, kind.ToString());
            if (delay > 0.2f) _e.ShowBubble("...", new Color(1f, 1f, 1f, 0.45f), delay);
            return true;
        }

        /// <summary>The speaker was staggered or killed: the pending shout is lost (the news does not travel).</summary>
        public void Cancel()
        {
            if (!_pending) return;
            _pending = false;
            _e.HideBubble();
        }

        public void Tick()
        {
            if (!_pending || Time.time < _pendingAt) return;
            _pending = false;
            if (_e.IsDead) return;
            var m = Table[_pendingKind];
            string line = _pendingText;
            if (string.IsNullOrEmpty(line))
            {
                line = m.lines[Random.Range(0, m.lines.Length)];
                if (line.Contains("{ad}"))
                {
                    string n = _e.LastLostMateName;
                    line = string.IsNullOrEmpty(n) ? m.lines[m.lines.Length - 1].Replace("{ad}'ı", "Birini").Replace("{ad}", "O") : line.Replace("{ad}", n);
                }
            }
            LastKind = _pendingKind;
            LastTime = Time.time;
            Show(_e, line, ColorOf(m.tone), m.priority);
            Deliver(_pendingKind, _pendingInfo, _pendingShoutId);
            AIDebug.Log(_e.Squad, $"{_e.DisplayName}: \"{line}\"");
        }

        /// <summary>Bubble over the head, at most MaxBubbles at once (highest priority / nearest win).</summary>
        public static void Show(EnemyController speaker, string text, Color color, int priority)
        {
            float now = Time.time;
            Visible.RemoveAll(v => v.until < now || v.speaker == null || v.speaker == speaker);
            if (Visible.Count >= MaxBubbles)
            {
                int lowest = -1;
                for (int i = 0; i < Visible.Count; i++)
                    if (lowest < 0 || Visible[i].priority < Visible[lowest].priority) lowest = i;
                if (lowest < 0 || Visible[lowest].priority > priority) return;
                Visible[lowest].speaker?.HideBubble();
                Visible.RemoveAt(lowest);
            }
            Visible.Add(new Shown { speaker = speaker, text = text, color = color, priority = priority, until = now + BubbleSeconds });
            speaker.ShowBubble(text, color, BubbleSeconds);
        }

        private void Deliver(BarkKind kind, Belief info, int shoutId)
        {
            if (!CarriesInfo(kind)) return;
            var p = AIDifficulty.For(_e.Squad);
            float r = p.shoutRadius;
            AIDebug.NoteStimulus(new Stimulus { kind = StimulusKind.Shout, position = _e.transform.position, radius = r, time = Time.time });
            Vector3 me = _e.transform.position;
            var list = EnemyController.Active;
            for (int i = list.Count - 1; i >= 0; i--)
            {
                var o = i < list.Count ? list[i] : null;
                if (o == null || o == _e || o.IsDead) continue;
                if ((o.transform.position - me).sqrMagnitude > r * r) continue;
                o.OnShout(kind, info, _e, shoutId);
            }
            _e.Squad?.OnOwnShout(kind, info, _e);
        }
    }
}
