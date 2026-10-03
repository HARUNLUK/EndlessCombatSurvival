using UnityEngine;
using UnityEngine.UI;

namespace EndlessSurvival.World
{
    /// <summary>
    /// Displays a minimal in-game clock and sun/moon indicator on the screen.
    /// </summary>
    public class TimeHUD : MonoBehaviour
    {
        private static TimeHUD _instance;

        [Header("UI References")]
        public Text timeText;

        [Header("Settings")]
        public bool showPhaseName = true;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoCreate()
        {
            if (_instance == null)
            {
                var existing = FindFirstObjectByType<TimeHUD>();
                if (existing != null)
                {
                    _instance = existing;
                    return;
                }

                CreateHUD();
            }
        }

        private static void CreateHUD()
        {
            GameObject hudObj = new GameObject("TimeHUD_Canvas");
            var canvas = hudObj.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 15; // Envanterin altında, oyunun üstünde

            var scaler = hudObj.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);

            hudObj.AddComponent<GraphicRaycaster>();
            _instance = hudObj.AddComponent<TimeHUD>();

            // Arka plan paneli (Sağ üst köşe)
            GameObject badge = new GameObject("TimeBadge");
            badge.transform.SetParent(hudObj.transform, false);
            var rect = badge.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(1f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(1f, 1f);
            rect.anchoredPosition = new Vector2(-25f, -25f);
            rect.sizeDelta = new Vector2(200f, 40f);

            var bgImg = badge.AddComponent<Image>();
            bgImg.color = new Color(0.08f, 0.10f, 0.14f, 0.75f);

            // Saat Metni
            GameObject textObj = new GameObject("Text_Time");
            textObj.transform.SetParent(badge.transform, false);
            var textRect = textObj.AddComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(10f, 0f);
            textRect.offsetMax = new Vector2(-10f, 0f);

            var txt = textObj.AddComponent<Text>();
            txt.alignment = TextAnchor.MiddleCenter;
            txt.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf") ?? Resources.GetBuiltinResource<Font>("Arial.ttf");
            txt.fontSize = 17;
            txt.fontStyle = FontStyle.Bold;
            txt.color = new Color(0.95f, 0.95f, 0.95f, 0.95f);

            _instance.timeText = txt;
            DontDestroyOnLoad(hudObj);
        }

        private void Awake()
        {
            _instance = this;
        }

        private void Update()
        {
            if (timeText == null) return;

            var cycle = DayNightCycle.Instance;
            if (cycle != null)
            {
                string icon;
                Color textColor;

                if (cycle.CurrentHour >= 5.5f && cycle.CurrentHour < 7.5f)
                {
                    icon = "🌅";
                    textColor = new Color(1.0f, 0.8f, 0.5f);
                }
                else if (cycle.CurrentHour >= 7.5f && cycle.CurrentHour < 17.5f)
                {
                    icon = "☀️";
                    textColor = new Color(1.0f, 0.95f, 0.75f);
                }
                else if (cycle.CurrentHour >= 17.5f && cycle.CurrentHour < 19.5f)
                {
                    icon = "🌇";
                    textColor = new Color(1.0f, 0.65f, 0.45f);
                }
                else
                {
                    icon = "🌙";
                    textColor = new Color(0.65f, 0.80f, 1.0f);
                }

                string phase = showPhaseName ? $" {cycle.GetPhaseString()}" : "";
                timeText.text = $"{icon} {cycle.GetTimeString()}{phase}";
                timeText.color = textColor;
            }
        }
    }
}
