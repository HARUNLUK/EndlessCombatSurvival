using System.Collections.Generic;
using UnityEngine;
using EndlessCombat.Combat;

namespace EndlessSurvival.Survival
{
    /// <summary>
    /// Gamepad focus for the IMGUI screens (menus, Tab screen tabs, trader, game over).
    /// Every button drawn through PadGUI takes a number in draw order. The D-pad / left stick moves the focus
    /// (up / left = previous, down / right = next), A presses the focused button. The focused button gets a frame
    /// and is scrolled into view. The mouse keeps working as before.
    ///
    /// How a press is delivered: IMGUI runs OnGUI once per event. A gamepad press is not an IMGUI event, so the
    /// focused button reports "clicked" during the Layout event. The screen may then change what it draws, which
    /// would not match the layout any more, so the rest of that frame's events are skipped for that screen
    /// (Begin returns false): one frame without the screen instead of "Mismatched LayoutGroup" errors.
    ///
    /// Usage in OnGUI:
    ///   if (!PadGUI.Begin(this)) return;      // before any GUILayout call
    ///   if (PadGUI.Button("Devam")) ...
    ///   PadGUI.End();
    /// </summary>
    public static class PadGUI
    {
        private class State
        {
            public int selected;
            public int count;        // controls seen in the last full pass
            public int counter;      // running number in the current pass
            public int skipFrame = -1;
            public int lastFrame = -1;
            public bool scrollPending;
            public readonly List<bool> enabled = new List<bool>();
            public HashSet<int> sliders = new HashSet<int>(), slidersNext = new HashSet<int>();
        }

        private static readonly Dictionary<object, State> States = new Dictionary<object, State>();
        private static State _cur;
        private static bool _curActive;

        // Input, polled once per frame
        private static int _pollFrame = -1;
        private static int _move;      // -1 previous, +1 next
        private static int _side;      // -1 left, +1 right (sliders)
        private static bool _submit;
        private static float _repeatAt;
        private static Vector2Int _heldDir;

        private static void Poll()
        {
            if (_pollFrame == Time.frameCount) return;
            _pollFrame = Time.frameCount;
            _move = 0;
            _side = 0;
            _submit = GameInput.PadPressed(0);

            Vector2Int dir = GameInput.PadDpadPressed;
            int dx = dir.x, dy = -dir.y; // dy: +1 = down
            Vector2 stick = GameInput.PadLeftStick;
            Vector2Int held = Vector2Int.zero;
            if (stick.magnitude > 0.6f)
            {
                if (Mathf.Abs(stick.x) > Mathf.Abs(stick.y)) held.x = stick.x > 0 ? 1 : -1;
                else held.y = stick.y > 0 ? -1 : 1;
            }
            if (held != _heldDir)
            {
                _heldDir = held;
                _repeatAt = Time.unscaledTime + 0.38f;
                if (dx == 0 && dy == 0) { dx = held.x; dy = held.y; }
            }
            else if (held != Vector2Int.zero && Time.unscaledTime >= _repeatAt)
            {
                _repeatAt = Time.unscaledTime + 0.13f;
                if (dx == 0 && dy == 0) { dx = held.x; dy = held.y; }
            }

            if (dy != 0) _move = dy;
            _side = dx;
        }

        /// <summary>True when the last input came from a gamepad (the focus frame is drawn).</summary>
        public static bool Show => GameInput.UsingPad;

        /// <summary>
        /// Starts a screen. Returns false when this event must be skipped (a gamepad press changed the screen
        /// during this frame's Layout). active = false: the screen is drawn but does not take gamepad input.
        /// </summary>
        public static bool Begin(object owner, bool active = true)
        {
            if (!States.TryGetValue(owner, out State st))
            {
                st = new State();
                States[owner] = st;
            }
            _cur = st;
            _curActive = active;
            if (st.skipFrame == Time.frameCount && Event.current.type != EventType.Layout)
            {
                _cur = null;
                return false;
            }
            st.counter = 0;

            if (Event.current.type == EventType.Layout && st.lastFrame != Time.frameCount)
            {
                // Opened again after a pause: start from the top
                if (Time.frameCount - st.lastFrame > 2) st.selected = 0;
                st.lastFrame = Time.frameCount;
                if (active)
                {
                    Poll();
                    // Left / right also moves the focus (rows of buttons), except on a slider where it changes the value
                    int step = _move != 0 ? _move : (st.sliders.Contains(st.selected) ? 0 : _side);
                    if (step != 0 && st.count > 0)
                    {
                        int next = st.selected;
                        for (int i = 0; i < st.count; i++)
                        {
                            next = (next + step + st.count) % st.count;
                            if (next >= st.enabled.Count || st.enabled[next]) break;
                        }
                        if (next != st.selected) EndlessCombat.Audio.GameSound.PlayUi(EndlessCombat.Audio.SoundId.UiMove);
                        st.selected = next;
                        st.scrollPending = true;
                    }
                }
            }
            return true;
        }

        public static void End()
        {
            if (_cur == null) return;
            if (Event.current.type == EventType.Layout)
            {
                _cur.count = _cur.counter;
                var swap = _cur.sliders;
                _cur.sliders = _cur.slidersNext;
                _cur.slidersNext = swap;
                _cur.slidersNext.Clear();
                if (_cur.enabled.Count > _cur.count) _cur.enabled.RemoveRange(_cur.count, _cur.enabled.Count - _cur.count);
                if (_cur.selected >= _cur.count) _cur.selected = Mathf.Max(0, _cur.count - 1);
            }
            _cur = null;
        }

        /// <summary>Moves the focus of a screen to a control (by draw order).</summary>
        public static void Focus(object owner, int index)
        {
            if (States.TryGetValue(owner, out State st)) st.selected = Mathf.Max(0, index);
        }

        /// <summary>Number the next control will get (to focus it later).</summary>
        public static int NextIndex => _cur != null ? _cur.counter : 0;

        // ------------------------------------------------------------------ controls

        private static int Register()
        {
            if (_cur == null) return -1;
            int idx = _cur.counter++;
            if (Event.current.type == EventType.Layout)
            {
                while (_cur.enabled.Count <= idx) _cur.enabled.Add(true);
                _cur.enabled[idx] = GUI.enabled;
            }
            return idx;
        }

        private static bool Focused(int idx) => _cur != null && _curActive && idx == _cur.selected;

        /// <summary>The gamepad press for this control (only during Layout, once).</summary>
        private static bool TakeSubmit(int idx)
        {
            if (!Focused(idx) || !_submit || !GUI.enabled) return false;
            if (Event.current.type != EventType.Layout) return false;
            _submit = false;
            _cur.skipFrame = Time.frameCount;
            EndlessCombat.Audio.GameSound.PlayUi(EndlessCombat.Audio.SoundId.UiConfirm);
            return true;
        }

        private static void Frame(int idx, Rect r)
        {
            if (!Focused(idx) || !Show || Event.current.type != EventType.Repaint) return;
            var c = new Color(1f, 0.8f, 0.3f, 1f);
            const float t = 2f;
            // Drawn inside the control: a frame around it would be clipped at the edge of a layout area
            SurvivalGUI.Fill(new Rect(r.x, r.y, r.width, t), c);
            SurvivalGUI.Fill(new Rect(r.x, r.yMax - t, r.width, t), c);
            SurvivalGUI.Fill(new Rect(r.x, r.y, t, r.height), c);
            SurvivalGUI.Fill(new Rect(r.xMax - t, r.y, t, r.height), c);
            SurvivalGUI.Fill(r, new Color(1f, 0.8f, 0.3f, 0.12f));
            if (_cur.scrollPending)
            {
                _cur.scrollPending = false;
                GUI.ScrollTo(new Rect(r.x, r.y - 40f, r.width, r.height + 80f));
            }
        }

        public static bool Button(string text, params GUILayoutOption[] options) => Button(new GUIContent(text), options);

        public static bool Button(GUIContent content, params GUILayoutOption[] options)
        {
            int idx = Register();
            bool clicked = GUILayout.Button(content, options);
            if (Event.current.type == EventType.Repaint) Frame(idx, GUILayoutUtility.GetLastRect());
            return clicked || TakeSubmit(idx);
        }

        public static bool Button(Rect rect, string text)
        {
            int idx = Register();
            bool clicked = GUI.Button(rect, text);
            Frame(idx, rect);
            return clicked || TakeSubmit(idx);
        }

        public static bool Toggle(bool value, string text, params GUILayoutOption[] options)
        {
            int idx = Register();
            bool result = GUILayout.Toggle(value, text, options);
            if (Event.current.type == EventType.Repaint) Frame(idx, GUILayoutUtility.GetLastRect());
            if (TakeSubmit(idx)) result = !value;
            return result;
        }

        /// <summary>Slider: left / right changes the value while focused.</summary>
        public static float Slider(float value, float min, float max, params GUILayoutOption[] options)
        {
            int idx = Register();
            if (Event.current.type == EventType.Layout && _cur != null) _cur.slidersNext.Add(idx);
            float result = GUILayout.HorizontalSlider(value, min, max, options);
            if (Event.current.type == EventType.Repaint) Frame(idx, GUILayoutUtility.GetLastRect());
            if (Focused(idx) && Event.current.type == EventType.Layout && _side != 0)
            {
                result = Mathf.Clamp(value + _side * (max - min) * 0.05f, min, max);
                _side = 0;
            }
            return result;
        }

        /// <summary>A text row that can take the focus (so long read-only lists scroll with the gamepad).</summary>
        public static void Row(Rect rect)
        {
            int idx = Register();
            Frame(idx, rect);
        }
    }
}
