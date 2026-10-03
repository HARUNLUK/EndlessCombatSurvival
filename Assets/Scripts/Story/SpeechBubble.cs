using UnityEngine;
using TMPro;

namespace EndlessSurvival.Story
{
    public class SpeechBubble : MonoBehaviour
    {
        public TextMeshProUGUI textUI;
        public CanvasGroup canvasGroup;
        public Transform target;
        public Vector3 offset = new Vector3(0, 2.2f, 0);

        private float displayDuration;
        private float displayTimer;
        private bool isVisible;

        private void Awake()
        {
            if (canvasGroup != null) canvasGroup.alpha = 0;
            isVisible = false;
            
            // Fix prefab scale/size which was generated too small in batchmode
            var rect = GetComponent<RectTransform>();
            if (rect != null)
            {
                rect.sizeDelta = new Vector2(300, 100);
                rect.localScale = new Vector3(0.015f, 0.015f, 0.015f);
            }
        }

        private void LateUpdate()
        {
            if (!isVisible) return;

            // Billboarding and positioning
            if (target != null)
            {
                transform.position = target.position + offset;
            }
            
            if (Camera.main != null)
            {
                transform.rotation = Camera.main.transform.rotation;
            }

            // Timer
            if (displayTimer > 0)
            {
                displayTimer -= Time.deltaTime;
                if (displayTimer <= 0)
                {
                    Hide();
                }
            }
        }

        public void ShowText(string text, float duration)
        {
            if (textUI != null) textUI.text = text;
            displayDuration = duration;
            displayTimer = duration;
            isVisible = true;

            if (canvasGroup != null) canvasGroup.alpha = 1;
            gameObject.SetActive(true);
        }

        public void Hide()
        {
            isVisible = false;
            if (canvasGroup != null) canvasGroup.alpha = 0;
            gameObject.SetActive(false);
        }
    }
}
