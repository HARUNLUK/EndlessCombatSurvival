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

        public void Speak(string text, float duration)
        {
            // Don't speak if dead or in combat
            if (controller != null && controller.IsDead) return;
            if (controller != null && controller.IsAlerted) return;

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
                currentBubble.ShowText(text, duration);
            }
        }

        public void StopSpeaking()
        {
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
