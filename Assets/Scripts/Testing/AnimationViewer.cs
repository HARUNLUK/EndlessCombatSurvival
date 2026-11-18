using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.InputSystem;
using UnityEngine.Playables;
using EndlessSurvival.Survival;
using EndlessSurvival.World;

namespace EndlessSurvival.Testing
{
    /// <summary>
    /// Test_Animasyon scene (2026-11-18): look at one character's animations from every side.
    /// The camera orbits the character; the clips of its controller can be played one by one, paused or slowed.
    ///   gamepad                      keyboard / mouse
    ///   right stick: orbit           right mouse drag
    ///   RT / LT: zoom in / out       mouse wheel
    ///   D-pad up / down: aim height  W / S
    ///   D-pad left / right: clip     Q / E      (first entry = the controller itself, as in the game)
    ///   A: pause                     Space
    ///   X: slow motion (x0.25)       T
    ///   B: camera preset             C          (front, 3/4, side, back, close)
    ///   Y: reset camera              R
    /// Built by Endless Survival > Test Sahneleri > Animasyon sahnesi: Kur - Yenile.
    /// </summary>
    public class AnimationViewer : MonoBehaviour
    {
        public Animator subject;
        public float distance = 3.4f;
        public float aimHeight = 1.0f;
        public float yaw = 0f;      // 0 = in front of the character
        public float pitch = 8f;

        private static readonly (string name, float yaw, float pitch, float dist, float h)[] Presets =
        {
            ("Ön", 0f, 8f, 3.4f, 1.0f), ("3/4", 40f, 10f, 3.4f, 1.0f), ("Yan", 90f, 5f, 3.4f, 1.0f),
            ("Arka", 180f, 10f, 3.4f, 1.0f), ("Yakın (gövde)", 25f, 5f, 1.6f, 1.35f),
        };

        private readonly List<AnimationClip> _clips = new List<AnimationClip>();
        private int _clip = -1;           // -1: the controller plays (game behaviour)
        private int _preset;
        private bool _paused, _slow;
        private PlayableGraph _graph;
        private AnimationClipPlayable _playable;
        private Camera _cam;

        private IEnumerator Start()
        {
            _cam = GetComponent<Camera>();
            if (subject == null) subject = FindAnyObjectByType<Animator>();
            if (subject != null && subject.runtimeAnimatorController != null)
                _clips.AddRange(subject.runtimeAnimatorController.animationClips.Where(c => c != null).Distinct().OrderBy(c => c.name));
            ApplyPreset(1);

            // the global systems (day / night, weather) spawn after scene load: hold noon and clear sky
            yield return null;
            yield return null;
            var cycle = DayNightCycle.Instance;
            if (cycle != null)
            {
                cycle.timeSpeedMultiplier = 0f;
                cycle.SetTime(12f);
            }
            if (WeatherSystem.Instance != null) WeatherSystem.Instance.DebugForce(WeatherType.Clear, true);
        }

        private void Update()
        {
            var gp = Gamepad.current;
            var kb = Keyboard.current;
            var mouse = Mouse.current;
            float dt = Time.unscaledDeltaTime;

            Vector2 look = gp != null ? gp.rightStick.ReadValue() : Vector2.zero;
            if (mouse != null && mouse.rightButton.isPressed) look += mouse.delta.ReadValue() * 0.08f;
            yaw += look.x * 120f * dt;
            pitch = Mathf.Clamp(pitch - look.y * 80f * dt, -20f, 70f);

            float zoom = gp != null ? gp.rightTrigger.ReadValue() - gp.leftTrigger.ReadValue() : 0f;
            float wheel = mouse != null ? mouse.scroll.ReadValue().y : 0f;
            distance = Mathf.Clamp(distance - zoom * 2.5f * dt - wheel * 0.002f, 0.6f, 10f);

            float h = (gp != null ? (gp.dpad.up.isPressed ? 1 : 0) - (gp.dpad.down.isPressed ? 1 : 0) : 0)
                      + (kb != null ? (kb.wKey.isPressed ? 1 : 0) - (kb.sKey.isPressed ? 1 : 0) : 0);
            aimHeight = Mathf.Clamp(aimHeight + h * 0.8f * dt, 0.05f, 2.0f);

            if (Pressed(gp?.dpad.right, kb?.eKey)) SelectClip(_clip + 1);
            if (Pressed(gp?.dpad.left, kb?.qKey)) SelectClip(_clip - 1);
            if (Pressed(gp?.buttonSouth, kb?.spaceKey)) _paused = !_paused;
            if (Pressed(gp?.buttonWest, kb?.tKey)) _slow = !_slow;
            if (Pressed(gp?.buttonEast, kb?.cKey)) ApplyPreset(_preset + 1);
            if (Pressed(gp?.buttonNorth, kb?.rKey)) ApplyPreset(_preset);

            float speed = _paused ? 0f : (_slow ? 0.25f : 1f);
            if (subject != null) subject.speed = speed;
            if (_graph.IsValid()) _playable.SetSpeed(speed);
        }

        private static bool Pressed(UnityEngine.InputSystem.Controls.ButtonControl a, UnityEngine.InputSystem.Controls.KeyControl b)
            => (a != null && a.wasPressedThisFrame) || (b != null && b.wasPressedThisFrame);

        private void LateUpdate()
        {
            if (subject == null) return;
            Transform t = subject.transform;
            Vector3 target = t.position + Vector3.up * aimHeight;
            Quaternion rot = Quaternion.Euler(pitch, t.eulerAngles.y + 180f + yaw, 0f);
            transform.position = target - rot * Vector3.forward * distance;
            transform.rotation = rot;
        }

        private void ApplyPreset(int i)
        {
            _preset = (i % Presets.Length + Presets.Length) % Presets.Length;
            var p = Presets[_preset];
            yaw = p.yaw; pitch = p.pitch; distance = p.dist; aimHeight = p.h;
        }

        private void SelectClip(int i)
        {
            int count = _clips.Count + 1;
            _clip = ((i + 1) % count + count) % count - 1;
            if (_graph.IsValid()) _graph.Destroy();
            if (_clip < 0 || subject == null) { subject?.Rebind(); return; }
            _playable = AnimationPlayableUtilities.PlayClip(subject, _clips[_clip], out _graph);
            _playable.SetApplyFootIK(true);
        }

        private void OnDestroy()
        {
            if (_graph.IsValid()) _graph.Destroy();
        }

        private void OnGUI()
        {
            string clip = _clip < 0 ? "Controller (oyundaki gibi)" : _clips[_clip].name;
            string state = _paused ? "DURDU" : (_slow ? "yavaş x0.25" : "normal");
            string time = "";
            if (_clip >= 0 && _graph.IsValid())
            {
                float len = Mathf.Max(0.01f, _clips[_clip].length);
                float tt = (float)_playable.GetTime() % len;
                time = $"   {tt:0.00} / {len:0.00} sn";
            }
            else if (subject != null && subject.runtimeAnimatorController != null)
            {
                var info = subject.GetCurrentAnimatorStateInfo(0);
                time = $"   döngü %{(info.normalizedTime % 1f) * 100f:0}";
            }
            var style = new GUIStyle(GUI.skin.box) { alignment = TextAnchor.UpperLeft, fontSize = 15 };
            GUI.Box(new Rect(10, 10, 520, 112),
                $"Klip: {clip}{time}\nHız: {state}    Kamera: {Presets[_preset].name}\n" +
                "Sağ çubuk: dön   RT / LT: yakın / uzak   D-pad yukarı / aşağı: yükseklik\n" +
                "D-pad sol / sağ: klip   A: durdur   X: yavaş   B: kamera   Y: sıfırla", style);
        }
    }
}
