using UnityEngine;
using EndlessSurvival.Story;

namespace EndlessCombat.AI
{
    public class EnemyDialogue : MonoBehaviour
    {
        public SpeechBubble speechBubblePrefab;
        private SpeechBubble currentBubble;
        private EnemyController controller;

        private void Start()
        {
            controller = GetComponent<EnemyController>();
        }

        /// <summary>Camp chatter: not while dead or alerted.</summary>
        public void Speak(string text, float duration)
        {
            if (controller != null && controller.IsDead) return;
            if (controller != null && controller.IsAlerted) return;
            Show(text, Color.white, duration);
        }

        /// <summary>Shouts (EnemyBarks): also in combat, with a colour.</summary>
        public void Shout(string text, Color color, float duration)
        {
            if (controller != null && controller.IsDead) return;
            Show(text, color, duration);
        }

        private void Show(string text, Color color, float duration)
        {
            if (speechBubblePrefab == null)
            {
                speechBubblePrefab = Resources.Load<SpeechBubble>("UI/SpeechBubble");
            }

            if (currentBubble == null && speechBubblePrefab != null)
            {
                currentBubble = Instantiate(speechBubblePrefab);
                currentBubble.target = transform;
            }

            if (currentBubble != null)
            {
                currentBubble.SetColor(color);
                currentBubble.ShowText(text, duration);
            }
            else
            {
                _fallbackText = text;
                _fallbackColor = color;
                _fallbackUntil = Time.time + duration;
            }
        }

        // No bubble prefab in Resources: the overlay draws the text instead (AISystem)
        private string _fallbackText;
        private Color _fallbackColor;
        private float _fallbackUntil;

        public bool FallbackVisible(out string text, out Color color)
        {
            text = _fallbackText;
            color = _fallbackColor;
            return currentBubble == null && Time.time < _fallbackUntil && !string.IsNullOrEmpty(text);
        }

        public void StopSpeaking()
        {
            _fallbackUntil = 0f;
            if (currentBubble != null)
            {
                currentBubble.Hide();
            }
        }

        private void OnDestroy()
        {
            if (currentBubble != null)
            {
                Destroy(currentBubble.gameObject);
            }
        }
    }
}
