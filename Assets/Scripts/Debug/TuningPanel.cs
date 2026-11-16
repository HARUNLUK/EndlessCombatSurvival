using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;

namespace EndlessCombat.Tuning
{
    /// <summary>
    /// In-game tuning panel (Core_Feel_Plan stage 0). The "feel" numbers (camera distance, steering, enemy ranges...)
    /// are set by the player while playing, not guessed in code.
    ///
    /// Open / close: gamepad L3 + R3 together, keyboard F1. The game keeps running so a change is seen at once.
    /// While it is open the combat / loadout buttons of GameInput are dead (GameInput.TuningPanelOpen), walking,
    /// looking and driving keep working so the change can be tried right away.
    ///   D-pad up / down (arrows)        row
    ///   D-pad left / right (arrows)     value; held = repeats and speeds up
    ///   LB / RB (Page Up / Page Down)   page (Kamera, Karakter, Araç, Düşman)
    ///   X (Backspace)                   row back to its default
    ///   A (Enter)                       press an action row (export, reset page)
    ///
    /// Rows are registered from code: TuningPanel.Register("Kamera", "Mesafe", () => x, v => x = v, min, max, step).
    /// The same page + name registered again (a new vehicle, a reloaded scene) replaces the getter / setter, so the
    /// row always drives the live object. A saved value is written into the object when it registers.
    /// Values are saved to persistentDataPath/tuning.json. "Dışa aktar" also writes Assets/Resources/TuningDefaults.json
    /// (in the editor) so the next session can make them the code defaults.
    /// The panel is not a cost / need gate; test mode has nothing to do with it.
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    public class TuningPanel : MonoBehaviour
    {
        public static readonly string[] Pages = { "Kamera", "Karakter", "Araç", "Düşman" };

        private enum Kind { Number, Toggle, Choice, Readout, Action }

        private class Row
        {
            public string page, name, key;
            public Kind kind;
            public Func<float> get;
            public Action<float> set;
            public Func<string> text;
            public Action action;
            public string[] options;
            public float min, max, step, def;
            public UnityEngine.Object owner;
            public bool hasOwner;

            public bool Alive => !hasOwner || owner != null;
        }

        private static readonly List<Row> Rows = new List<Row>();
        private static readonly Dictionary<string, Row> ByKey = new Dictionary<string, Row>();
        private static readonly Dictionary<string, float> Saved = new Dictionary<string, float>();
        private static bool _loaded;
        private static TuningPanel _instance;

        public static bool IsOpen { get; private set; }

        private int _page, _row;
        private float _holdStart = -1f, _nextRepeat;
        private int _holdDir;
        private bool _dirty;
        private float _saveAt;
        private string _flash;
        private float _flashUntil;
        private bool _closeAtLateUpdate;
        private GUIStyle _label, _value, _title, _hint;

        // ------------------------------------------------------------------ registration

        private static string Key(string page, string name) => page + "/" + name;

        /// <summary>A number row. owner: the row hides when that object is destroyed (optional).</summary>
        public static void Register(string page, string name, Func<float> get, Action<float> set,
                                    float min, float max, float step, UnityEngine.Object owner = null)
        {
            Row r = Add(page, name, Kind.Number, owner);
            r.get = get;
            r.set = set;
            r.min = min;
            r.max = max;
            r.step = step;
            ApplySaved(r);
        }

        public static void RegisterToggle(string page, string name, Func<bool> get, Action<bool> set, UnityEngine.Object owner = null)
        {
            Row r = Add(page, name, Kind.Toggle, owner);
            r.get = () => get() ? 1f : 0f;
            r.set = v => set(v > 0.5f);
            r.min = 0f;
            r.max = 1f;
            r.step = 1f;
            ApplySaved(r);
        }

        public static void RegisterChoice(string page, string name, string[] options, Func<int> get, Action<int> set, UnityEngine.Object owner = null)
        {
            Row r = Add(page, name, Kind.Choice, owner);
            r.options = options;
            r.get = () => get();
            r.set = v => set(Mathf.Clamp(Mathf.RoundToInt(v), 0, options.Length - 1));
            r.min = 0f;
            r.max = options.Length - 1;
            r.step = 1f;
            ApplySaved(r);
        }

        /// <summary>A live read-only gauge (speed, path state...).</summary>
        public static void RegisterReadout(string page, string name, Func<string> text, UnityEngine.Object owner = null)
        {
            Row r = Add(page, name, Kind.Readout, owner);
            r.text = text;
        }

        public static void RegisterAction(string page, string name, Action action, UnityEngine.Object owner = null)
        {
            Row r = Add(page, name, Kind.Action, owner);
            r.action = action;
        }

        private static Row Add(string page, string name, Kind kind, UnityEngine.Object owner)
        {
            EnsureLoaded();
            string key = Key(page, name);
            if (!ByKey.TryGetValue(key, out Row r))
            {
                r = new Row { page = page, name = name, key = key, kind = kind, def = float.NaN };
                ByKey[key] = r;
                Rows.Add(r);
            }
            r.kind = kind;
            r.owner = owner;
            r.hasOwner = owner != null;
            return r;
        }

        private static void ApplySaved(Row r)
        {
            // The default is the code value seen at the first registration
            if (float.IsNaN(r.def)) r.def = SafeGet(r);
            if (Saved.TryGetValue(r.key, out float v)) SafeSet(r, v);
        }

        private static float SafeGet(Row r)
        {
            try { return r.get != null && r.Alive ? r.get() : 0f; }
            catch (Exception) { return 0f; }
        }

        private static void SafeSet(Row r, float v)
        {
            if (r.set == null || !r.Alive) return;
            if (r.kind == Kind.Number) v = Mathf.Clamp(v, r.min, r.max);
            try { r.set(v); }
            catch (Exception e) { Debug.LogWarning($"[Tuning] {r.key}: {e.Message}"); }
        }

        // ------------------------------------------------------------------ persistence

        [Serializable] private class Entry { public string k; public float v; }
        [Serializable] private class FileData { public List<Entry> values = new List<Entry>(); }

        private static string SavePath => Path.Combine(Application.persistentDataPath, "tuning.json");

        private static void EnsureLoaded()
        {
            if (_loaded) return;
            _loaded = true;
            try
            {
                if (!File.Exists(SavePath)) return;
                var data = JsonUtility.FromJson<FileData>(File.ReadAllText(SavePath));
                if (data?.values == null) return;
                foreach (var e in data.values)
                    if (!string.IsNullOrEmpty(e.k)) Saved[e.k] = e.v;
            }
            catch (Exception e) { Debug.LogWarning("[Tuning] could not read tuning.json: " + e.Message); }
        }

        private static string BuildJson(bool onlyChanged)
        {
            var data = new FileData();
            var seen = new HashSet<string>();
            foreach (var r in Rows)
            {
                if (r.kind == Kind.Readout || r.kind == Kind.Action) continue;
                float v = r.Alive ? SafeGet(r) : (Saved.TryGetValue(r.key, out float s) ? s : r.def);
                if (onlyChanged && Mathf.Approximately(v, r.def) && !Saved.ContainsKey(r.key)) continue;
                data.values.Add(new Entry { k = r.key, v = v });
                seen.Add(r.key);
            }
            // Saved values of rows that are not registered right now (no vehicle driven yet...) are kept
            foreach (var kv in Saved)
                if (!seen.Contains(kv.Key)) data.values.Add(new Entry { k = kv.Key, v = kv.Value });
            return JsonUtility.ToJson(data, true);
        }

        private static void WriteSave()
        {
            foreach (var r in Rows)
                if (r.kind != Kind.Readout && r.kind != Kind.Action && r.Alive)
                {
                    float v = SafeGet(r);
                    if (Mathf.Approximately(v, r.def)) Saved.Remove(r.key);
                    else Saved[r.key] = v;
                }
            try { File.WriteAllText(SavePath, BuildJson(true)); }
            catch (Exception e) { Debug.LogWarning("[Tuning] could not write tuning.json: " + e.Message); }
        }

        private void Export()
        {
            WriteSave();
            string json = BuildJson(false);
            string path;
#if UNITY_EDITOR
            path = Path.Combine(Application.dataPath, "Resources", "TuningDefaults.json");
#else
            path = Path.Combine(Application.persistentDataPath, "TuningDefaults.json");
#endif
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, json);
                Flash("Dışa aktarıldı: " + path);
            }
            catch (Exception e) { Flash("Yazılamadı: " + e.Message); }
        }

        // ------------------------------------------------------------------ lifetime

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (_instance != null) return;
            var go = new GameObject("TuningPanel");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<TuningPanel>();
        }

        private void Awake()
        {
            EnsureLoaded();
            RegisterAction("Kamera", "Dışa aktar (TuningDefaults.json)", Export);
            RegisterAction("Kamera", "Bu sayfayı varsayılana döndür", ResetPage);
            RegisterAction("Karakter", "Bu sayfayı varsayılana döndür", ResetPage);
            RegisterAction("Araç", "Bu sayfayı varsayılana döndür", ResetPage);
            RegisterAction("Düşman", "Bu sayfayı varsayılana döndür", ResetPage);
        }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
            SetOpen(false);
        }

        private void OnApplicationQuit()
        {
            if (_dirty) WriteSave();
        }

        private void SetOpen(bool open)
        {
            IsOpen = open;
            Combat.GameInput.TuningPanelOpen = open;
            if (!open && _dirty) { WriteSave(); _dirty = false; }
        }

        private void Update()
        {
            Gamepad pad = Gamepad.current;
            Keyboard kb = Keyboard.current;

            bool toggle = kb != null && kb.f1Key.wasPressedThisFrame;
            if (pad != null)
            {
                bool l3p = pad.leftStickButton.wasPressedThisFrame, r3p = pad.rightStickButton.wasPressedThisFrame;
                if ((l3p && pad.rightStickButton.isPressed) || (r3p && pad.leftStickButton.isPressed)) toggle = true;
            }
            if (toggle)
            {
                if (!IsOpen) { SetOpen(true); _row = 0; }
                // Close at the end of the frame: the same L3 / R3 press must not reach crouch / sprint this frame
                else _closeAtLateUpdate = true;
                return;
            }
            if (!IsOpen)
            {
                if (_dirty && Time.unscaledTime >= _saveAt) { WriteSave(); _dirty = false; }
                return;
            }

            List<Row> rows = PageRows(_page);

            // Page
            int pageStep = 0;
            if ((pad != null && pad.rightShoulder.wasPressedThisFrame) || (kb != null && kb.pageDownKey.wasPressedThisFrame)) pageStep = 1;
            if ((pad != null && pad.leftShoulder.wasPressedThisFrame) || (kb != null && kb.pageUpKey.wasPressedThisFrame)) pageStep = -1;
            if (pageStep != 0)
            {
                _page = (_page + pageStep + Pages.Length) % Pages.Length;
                _row = 0;
                return;
            }

            if (rows.Count == 0) return;

            // Row
            int rowStep = 0;
            if ((pad != null && pad.dpad.down.wasPressedThisFrame) || (kb != null && kb.downArrowKey.wasPressedThisFrame)) rowStep = 1;
            if ((pad != null && pad.dpad.up.wasPressedThisFrame) || (kb != null && kb.upArrowKey.wasPressedThisFrame)) rowStep = -1;
            if (rowStep != 0) _row = (_row + rowStep + rows.Count) % rows.Count;
            _row = Mathf.Clamp(_row, 0, rows.Count - 1);
            Row row = rows[_row];

            // Reset row
            if ((pad != null && pad.buttonWest.wasPressedThisFrame) || (kb != null && kb.backspaceKey.wasPressedThisFrame))
            {
                if (row.set != null && !float.IsNaN(row.def)) { SafeSet(row, row.def); MarkDirty(); }
            }

            // Press
            if ((pad != null && pad.buttonSouth.wasPressedThisFrame) || (kb != null && (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame)))
            {
                if (row.kind == Kind.Action) row.action?.Invoke();
                else if (row.kind == Kind.Toggle) { SafeSet(row, SafeGet(row) > 0.5f ? 0f : 1f); MarkDirty(); }
            }

            // Value with hold-repeat and speed-up
            int dir = 0;
            bool right = (pad != null && pad.dpad.right.isPressed) || (kb != null && kb.rightArrowKey.isPressed);
            bool left = (pad != null && pad.dpad.left.isPressed) || (kb != null && kb.leftArrowKey.isPressed);
            if (right && !left) dir = 1;
            else if (left && !right) dir = -1;

            if (dir == 0) { _holdDir = 0; _holdStart = -1f; return; }
            float now = Time.unscaledTime;
            int steps = 0;
            if (dir != _holdDir)
            {
                _holdDir = dir;
                _holdStart = now;
                _nextRepeat = now + 0.35f;
                steps = 1;
            }
            else if (now >= _nextRepeat)
            {
                float held = now - _holdStart;
                _nextRepeat = now + Mathf.Lerp(0.09f, 0.03f, Mathf.InverseLerp(0.4f, 2f, held));
                steps = held > 2.5f ? 5 : held > 1.4f ? 2 : 1;
            }
            if (steps > 0 && row.set != null && row.kind != Kind.Readout && row.kind != Kind.Action)
            {
                float v = SafeGet(row);
                if (row.kind == Kind.Toggle) v = dir > 0 ? 1f : 0f;
                else if (row.kind == Kind.Choice) v = Mathf.Repeat(Mathf.Round(v) + dir, row.options.Length);
                else v = Mathf.Clamp(Mathf.Round((v + dir * row.step * steps) / row.step) * row.step, row.min, row.max);
                SafeSet(row, v);
                MarkDirty();
            }
        }

        private void LateUpdate()
        {
            if (_closeAtLateUpdate)
            {
                _closeAtLateUpdate = false;
                SetOpen(false);
            }
        }

        private void MarkDirty()
        {
            _dirty = true;
            _saveAt = Time.unscaledTime + 1.5f;
        }

        private void Flash(string text)
        {
            _flash = text;
            _flashUntil = Time.unscaledTime + 4f;
        }

        private void ResetPage()
        {
            foreach (var r in PageRows(_page))
                if (r.set != null && !float.IsNaN(r.def)) SafeSet(r, r.def);
            MarkDirty();
            Flash("Sayfa varsayılana döndü");
        }

        private static readonly List<Row> PageBuffer = new List<Row>();

        private static List<Row> PageRows(int page)
        {
            PageBuffer.Clear();
            string p = Pages[page];
            // Value rows first (in registration order), action rows at the end
            foreach (var r in Rows)
                if (r.page == p && r.Alive && r.kind != Kind.Action) PageBuffer.Add(r);
            foreach (var r in Rows)
                if (r.page == p && r.Alive && r.kind == Kind.Action) PageBuffer.Add(r);
            return PageBuffer;
        }

        // ------------------------------------------------------------------ drawing

        private void Styles()
        {
            if (_label != null) return;
            _label = new GUIStyle(GUI.skin.label) { fontSize = 15, clipping = TextClipping.Clip };
            _label.normal.textColor = new Color(0.92f, 0.92f, 0.92f);
            _value = new GUIStyle(_label) { alignment = TextAnchor.MiddleRight };
            _title = new GUIStyle(_label) { fontSize = 17, fontStyle = FontStyle.Bold };
            _hint = new GUIStyle(_label) { fontSize = 12, wordWrap = true };
            _hint.normal.textColor = new Color(0.75f, 0.75f, 0.75f);
        }

        private static Texture2D _white;

        private static void Fill(Rect r, Color c)
        {
            if (_white == null) { _white = new Texture2D(1, 1); _white.SetPixel(0, 0, Color.white); _white.Apply(); }
            Color old = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, _white);
            GUI.color = old;
        }

        private void OnGUI()
        {
            if (!IsOpen) return;
            Styles();
            float scale = Mathf.Max(1f, Screen.height / 1080f);
            Matrix4x4 m = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));

            const float w = 520f, rowH = 26f;
            const int visible = 20;
            float x = 16f, y = 16f;
            List<Row> rows = PageRows(_page);
            float h = 70f + Mathf.Min(rows.Count, visible) * rowH + 60f;
            Fill(new Rect(x, y, w, h), new Color(0f, 0f, 0f, 0.78f));

            // Tabs
            float tx = x + 10f;
            for (int i = 0; i < Pages.Length; i++)
            {
                var tr = new Rect(tx, y + 8f, 120f, 26f);
                if (i == _page) Fill(tr, new Color(1f, 0.8f, 0.3f, 0.35f));
                GUI.Label(tr, " " + Pages[i], i == _page ? _title : _label);
                tx += 125f;
            }
            GUI.Label(new Rect(x + 10f, y + 38f, w - 20f, 22f), "Ayar paneli   LB / RB sayfa", _hint);

            int first = Mathf.Clamp(_row - visible / 2, 0, Mathf.Max(0, rows.Count - visible));
            float ry = y + 62f;
            for (int i = first; i < rows.Count && i < first + visible; i++)
            {
                Row r = rows[i];
                var rr = new Rect(x + 6f, ry, w - 12f, rowH);
                if (i == _row) Fill(rr, new Color(1f, 0.8f, 0.3f, 0.22f));
                GUI.Label(new Rect(rr.x + 6f, rr.y, 330f, rowH), r.name, _label);
                GUI.Label(new Rect(rr.x + 300f, rr.y, rr.width - 306f, rowH), ValueText(r), _value);
                ry += rowH;
            }
            if (rows.Count == 0) GUI.Label(new Rect(x + 12f, ry, w - 24f, rowH), "(bu sayfada şu an satır yok)", _hint);

            string hint = GameInputHint();
            if (_flash != null && Time.unscaledTime < _flashUntil) hint = _flash;
            GUI.Label(new Rect(x + 10f, y + h - 52f, w - 20f, 48f), hint, _hint);
            GUI.matrix = m;
        }

        private static string GameInputHint() =>
            "D-pad yukarı / aşağı satır, sol / sağ değer (basılı tut: hızlanır). X: varsayılan, A: eylem. L3 + R3 / F1: kapat.";

        private static string ValueText(Row r)
        {
            switch (r.kind)
            {
                case Kind.Readout:
                    try { return r.text != null ? r.text() : ""; } catch (Exception) { return "-"; }
                case Kind.Action: return "[A]";
                case Kind.Toggle: return SafeGet(r) > 0.5f ? "açık" : "kapalı";
                case Kind.Choice:
                    int i = Mathf.Clamp(Mathf.RoundToInt(SafeGet(r)), 0, r.options.Length - 1);
                    return "< " + r.options[i] + " >";
                default:
                    float v = SafeGet(r);
                    string f = r.step >= 1f ? "0" : r.step >= 0.1f ? "0.0" : r.step >= 0.01f ? "0.00" : "0.000";
                    string s = v.ToString(f);
                    if (!float.IsNaN(r.def) && !Mathf.Approximately(v, r.def)) s += "  (" + r.def.ToString(f) + ")";
                    return s;
            }
        }
    }
}
