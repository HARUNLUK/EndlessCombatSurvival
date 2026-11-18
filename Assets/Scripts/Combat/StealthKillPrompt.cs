using UnityEngine;

namespace EndlessCombat.Combat
{
    /// <summary>
    /// "SESSİZ ÖLDÜR" hint (2026-11-17): shown when a knife is in the hand and an enemy within reach would die at once
    /// from a strike now (behind it, it has not noticed you; same test as CombatBody.ReceiveMelee). The player knows
    /// when the stealth kill works instead of guessing. Self-installs.
    /// </summary>
    public class StealthKillPrompt : MonoBehaviour
    {
        public static float Reach = 2.2f;

        private PlayerMelee _player;
        private bool _show;
        private float _next;
        private GUIStyle _style;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (FindAnyObjectByType<StealthKillPrompt>() != null) return;
            var go = new GameObject("StealthKillPrompt");
            DontDestroyOnLoad(go);
            go.AddComponent<StealthKillPrompt>();
        }

        private void Update()
        {
            if (Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + 0.1f;
            _show = false;
            if (_player == null) _player = FindAnyObjectByType<PlayerMelee>();
            if (_player == null || !_player.isActiveAndEnabled || !_player.HoldsKnife) return;

            Vector3 me = _player.transform.position;
            var list = AI.EnemyController.Active;
            for (int i = 0; i < list.Count; i++)
            {
                var e = list[i];
                if (e == null || e.IsDead) continue;
                Vector3 d = e.transform.position - me;
                d.y = 0f;
                if (d.sqrMagnitude > Reach * Reach) continue;
                var body = e.GetComponent<CombatBody>();
                if (body != null && body.StealthKillPossibleFrom(me)) { _show = true; return; }
            }
        }

        private void OnGUI()
        {
            if (!_show) return;
            if (_style == null)
            {
                _style = new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
                _style.normal.textColor = new Color(1f, 0.85f, 0.4f);
            }
            var r = new Rect(0f, Screen.height * 0.62f, Screen.width, 36f);
            GUI.Label(r, "SESSİZ ÖLDÜR  " + GameInput.Hint("[Sol tık]", "RB"), _style);
        }
    }
}
