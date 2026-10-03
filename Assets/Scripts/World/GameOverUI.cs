using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

namespace EndlessSurvival.World
{
    /// <summary>
    /// Displays a comprehensive game over modal with distance traveled, survival statistics, cause of death, and restart action.
    /// </summary>
    public class GameOverUI : MonoBehaviour
    {
        private static GameOverUI _instance;
        public static bool IsGameOver { get; private set; }

        private GameObject _panelRoot;
        private CanvasGroup _canvasGroup;

        private Text _causeText;
        private Text _distanceText;
        private Text _timeText;
        private Text _enemiesText;
        private Text _lootText;
        private Text _scoreText;

        public static void Show(string customCause = null)
        {
            if (IsGameOver) return;
            IsGameOver = true;

            if (_instance == null)
            {
                CreateUI();
            }

            if (!string.IsNullOrEmpty(customCause))
            {
                GameStatsTracker.Instance?.SetCauseOfDeath(customCause);
            }

            _instance.PopulateAndDisplay();
        }

        private static void CreateUI()
        {
            GameObject uiObj = new GameObject("GameOver_Canvas");
            var canvas = uiObj.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100; // En üstte görünsün

            var scaler = uiObj.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);

            uiObj.AddComponent<GraphicRaycaster>();
            _instance = uiObj.AddComponent<GameOverUI>();

            // Fullscreen Backdrop (Koyu atmosferik karartma)
            GameObject backdrop = new GameObject("Backdrop");
            backdrop.transform.SetParent(uiObj.transform, false);
            var backRect = backdrop.AddComponent<RectTransform>();
            backRect.anchorMin = Vector2.zero;
            backRect.anchorMax = Vector2.one;
            backRect.offsetMin = Vector2.zero;
            backRect.offsetMax = Vector2.zero;

            var backImg = backdrop.AddComponent<Image>();
            backImg.color = new Color(0.06f, 0.02f, 0.02f, 0.95f);

            var cg = backdrop.AddComponent<CanvasGroup>();
            _instance._canvasGroup = cg;
            _instance._panelRoot = backdrop;

            // Modal Card Panel
            GameObject card = new GameObject("StatsCard");
            card.transform.SetParent(backdrop.transform, false);
            var cardRect = card.AddComponent<RectTransform>();
            cardRect.sizeDelta = new Vector2(650, 680);
            cardRect.anchoredPosition = Vector2.zero;

            var cardImg = card.AddComponent<Image>();
            cardImg.color = new Color(0.10f, 0.11f, 0.14f, 0.96f);

            // Başlık: YOLCULUK SONA ERDİ
            CreateText(card.transform, "Title", "YOLCULUK SONA ERDİ", 32, FontStyle.Bold, new Vector2(0, 290), new Vector2(600, 50), new Color(0.92f, 0.28f, 0.22f));

            // Ölüm Sebebi
            _instance._causeText = CreateText(card.transform, "Cause", "Ölüm Sebebi: Bilinmeyen Tehlike", 17, FontStyle.Italic, new Vector2(0, 245), new Vector2(580, 35), new Color(1f, 0.75f, 0.4f));

            // Çizgi
            CreateDivider(card.transform, new Vector2(0, 215), 560);

            // İstatistik Kartı İçeriği
            float startY = 175f;
            float rowSpacing = 50f;

            _instance._distanceText = CreateStatRow(card.transform, "Row_Dist", "Katedilen Mesafe:", "0 m", new Vector2(0, startY));
            _instance._timeText = CreateStatRow(card.transform, "Row_Time", "Hayatta Kalınan Süre:", "0 dk", new Vector2(0, startY - rowSpacing));
            _instance._enemiesText = CreateStatRow(card.transform, "Row_Enemies", "Etkisiz Düşman:", "0", new Vector2(0, startY - rowSpacing * 2));
            _instance._lootText = CreateStatRow(card.transform, "Row_Loot", "Toplanan Ganimet:", "0", new Vector2(0, startY - rowSpacing * 3));

            CreateDivider(card.transform, new Vector2(0, startY - rowSpacing * 3.8f), 560);

            // Toplam Skor
            _instance._scoreText = CreateText(card.transform, "TotalScore", "TOPLAM HAYATTA KALMA SKORU: 0", 21, FontStyle.Bold, new Vector2(0, -90), new Vector2(580, 45), new Color(0.35f, 1f, 0.5f));

            // Butonlar
            CreateButton(card.transform, "Btn_Restart", "Yeniden Başla [R]", new Vector2(0, -170), new Vector2(480, 50), new Color(0.22f, 0.55f, 0.28f), () => _instance.RestartGame());
            CreateButton(card.transform, "Btn_Quit", "Oyundan Çık", new Vector2(0, -235), new Vector2(480, 45), new Color(0.3f, 0.32f, 0.38f), () => Application.Quit());

            backdrop.SetActive(false);
            DontDestroyOnLoad(uiObj);
        }

        private void PopulateAndDisplay()
        {
            if (_panelRoot == null) return;

            var tracker = GameStatsTracker.Instance;
            if (tracker != null)
            {
                if (_causeText != null) _causeText.text = $"Ölüm Sebebi: {tracker.causeOfDeath}";
                if (_distanceText != null) _distanceText.text = tracker.GetFormattedDistance();
                if (_timeText != null)
                {
                    var cycle = DayNightCycle.Instance;
                    string cycleInfo = cycle != null ? $" ({cycle.GetTimeString()} - {cycle.GetPhaseString()})" : "";
                    _timeText.text = $"{tracker.GetFormattedTimePlayed()}{cycleInfo}";
                }
                if (_enemiesText != null) _enemiesText.text = $"{tracker.enemiesKilled} Düşman";
                if (_lootText != null) _lootText.text = $"{tracker.itemsLooted} Parça";
                if (_scoreText != null) _scoreText.text = $"TOPLAM SKOR: {tracker.CalculateSurvivalScore():N0} PUAN";
            }

            _panelRoot.SetActive(true);

            // Cursor aç ve kamerayı sabitle
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            var inputs = Object.FindAnyObjectByType<StarterAssets.StarterAssetsInputs>();
            if (inputs != null)
            {
                inputs.cursorInputForLook = false;
                inputs.look = Vector2.zero;
            }

            StartCoroutine(FadeInRoutine());
        }

        private IEnumerator FadeInRoutine()
        {
            if (_canvasGroup != null)
            {
                _canvasGroup.alpha = 0f;
                for (float t = 0; t <= 1f; t += Time.unscaledDeltaTime * 2.5f)
                {
                    _canvasGroup.alpha = t;
                    yield return null;
                }
                _canvasGroup.alpha = 1f;
            }
        }

        private void Update()
        {
            if (IsGameOver)
            {
                var keyboard = UnityEngine.InputSystem.Keyboard.current;
                if (keyboard != null && (keyboard.rKey.wasPressedThisFrame || keyboard.enterKey.wasPressedThisFrame))
                {
                    RestartGame();
                }
            }
        }

        public void RestartGame()
        {
            IsGameOver = false;
            Time.timeScale = 1f;

            var tracker = GameStatsTracker.Instance;
            if (tracker != null) tracker.ResetRunStats();

            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;

            if (_panelRoot != null) _panelRoot.SetActive(false);

            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }

        // ==========================================
        // UI YARDIMCILARI
        // ==========================================

        private static Text CreateText(Transform parent, string name, string content, int fontSize, FontStyle style, Vector2 pos, Vector2 size, Color col)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rect = go.AddComponent<RectTransform>();
            rect.anchoredPosition = pos;
            rect.sizeDelta = size;

            var txt = go.AddComponent<Text>();
            txt.text = content;
            txt.fontSize = fontSize;
            txt.fontStyle = style;
            txt.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf") ?? Resources.GetBuiltinResource<Font>("Arial.ttf");
            txt.alignment = TextAnchor.MiddleCenter;
            txt.color = col;
            return txt;
        }

        private static Text CreateStatRow(Transform parent, string name, string label, string value, Vector2 pos)
        {
            GameObject row = new GameObject(name);
            row.transform.SetParent(parent, false);
            var rowRect = row.AddComponent<RectTransform>();
            rowRect.anchoredPosition = pos;
            rowRect.sizeDelta = new Vector2(560, 40);

            // Sol Etiket
            GameObject lblObj = new GameObject("Label");
            lblObj.transform.SetParent(row.transform, false);
            var lblRect = lblObj.AddComponent<RectTransform>();
            lblRect.anchorMin = new Vector2(0f, 0f);
            lblRect.anchorMax = new Vector2(0.55f, 1f);
            lblRect.offsetMin = Vector2.zero;
            lblRect.offsetMax = Vector2.zero;

            var lblText = lblObj.AddComponent<Text>();
            lblText.text = label;
            lblText.fontSize = 17;
            lblText.fontStyle = FontStyle.Normal;
            lblText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf") ?? Resources.GetBuiltinResource<Font>("Arial.ttf");
            lblText.alignment = TextAnchor.MiddleLeft;
            lblText.color = new Color(0.8f, 0.82f, 0.85f);

            // Sağ Değer
            GameObject valObj = new GameObject("Value");
            valObj.transform.SetParent(row.transform, false);
            var valRect = valObj.AddComponent<RectTransform>();
            valRect.anchorMin = new Vector2(0.55f, 0f);
            valRect.anchorMax = new Vector2(1f, 1f);
            valRect.offsetMin = Vector2.zero;
            valRect.offsetMax = Vector2.zero;

            var valText = valObj.AddComponent<Text>();
            valText.text = value;
            valText.fontSize = 18;
            valText.fontStyle = FontStyle.Bold;
            valText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf") ?? Resources.GetBuiltinResource<Font>("Arial.ttf");
            valText.alignment = TextAnchor.MiddleRight;
            valText.color = Color.white;

            return valText;
        }

        private static void CreateDivider(Transform parent, Vector2 pos, float width)
        {
            GameObject div = new GameObject("Divider");
            div.transform.SetParent(parent, false);
            var rect = div.AddComponent<RectTransform>();
            rect.anchoredPosition = pos;
            rect.sizeDelta = new Vector2(width, 2);

            var img = div.AddComponent<Image>();
            img.color = new Color(0.25f, 0.28f, 0.35f, 0.6f);
        }

        private static Button CreateButton(Transform parent, string name, string label, Vector2 pos, Vector2 size, Color bgColor, System.Action onClick)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rect = go.AddComponent<RectTransform>();
            rect.anchoredPosition = pos;
            rect.sizeDelta = size;

            var img = go.AddComponent<Image>();
            img.color = bgColor;

            var btn = go.AddComponent<Button>();
            var colors = btn.colors;
            colors.highlightedColor = bgColor * 1.25f;
            colors.pressedColor = bgColor * 0.8f;
            btn.colors = colors;

            CreateText(go.transform, "Label", label, 16, FontStyle.Bold, Vector2.zero, size, Color.white);

            if (onClick != null)
            {
                btn.onClick.AddListener(() => onClick());
            }

            return btn;
        }
    }
}
