using UnityEngine;
using UnityEngine.UI;
using TMPro;
using EndlessCombat.AI;
using EndlessCombat.Combat;

namespace EndlessSurvival.World
{
    /// <summary>
    /// Oyuncunun comelme durumunu, etraftaki dusmanlarin suphe/fark edilme durumunu gosteren HUD.
    /// </summary>
    public class StealthHUD : MonoBehaviour
    {
        public static StealthHUD Instance { get; private set; }

        private Canvas _canvas;
        private GameObject _indicatorRoot;
        private TextMeshProUGUI _stealthText;
        private Image _stealthIcon;

        private float _fadeTimer = 0f;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            BuildProceduralUI();
        }

        private void BuildProceduralUI()
        {
            _canvas = GetComponentInParent<Canvas>();
            if (_canvas == null)
            {
                GameObject canvasObj = new GameObject("StealthHUD_Canvas");
                _canvas = canvasObj.AddComponent<Canvas>();
                _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                _canvas.sortingOrder = 45;

                var scaler = canvasObj.AddComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920, 1080);

                canvasObj.AddComponent<GraphicRaycaster>();
                transform.SetParent(canvasObj.transform, false);
            }

            _indicatorRoot = new GameObject("StealthRoot");
            _indicatorRoot.transform.SetParent(_canvas.transform, false);

            var rootRect = _indicatorRoot.AddComponent<RectTransform>();
            rootRect.anchorMin = new Vector2(0.5f, 0.90f);
            rootRect.anchorMax = new Vector2(0.5f, 0.90f);
            rootRect.pivot = new Vector2(0.5f, 0.5f);
            rootRect.sizeDelta = new Vector2(240, 44);

            // Arka plan paneli
            var bgObj = new GameObject("Background");
            bgObj.transform.SetParent(_indicatorRoot.transform, false);
            var bgRect = bgObj.AddComponent<RectTransform>();
            bgRect.sizeDelta = new Vector2(240, 44);
            var bgImg = bgObj.AddComponent<Image>();
            bgImg.color = new Color(0.05f, 0.05f, 0.08f, 0.75f);

            // Durum Metni
            var textObj = new GameObject("StatusText");
            textObj.transform.SetParent(_indicatorRoot.transform, false);
            var textRect = textObj.AddComponent<RectTransform>();
            textRect.sizeDelta = new Vector2(230, 40);

            _stealthText = textObj.AddComponent<TextMeshProUGUI>();
            _stealthText.fontSize = 20;
            _stealthText.alignment = TextAlignmentOptions.Center;
            _stealthText.fontStyle = FontStyles.Bold;
            _stealthText.text = "GİZLİ";
            _stealthText.color = new Color(0.4f, 0.9f, 0.4f, 0.9f);

            _indicatorRoot.SetActive(false);
        }

        private void Update()
        {
            var stealth = PlayerStealthController.Instance;
            if (stealth == null) return;

            // Yakındaki en yüksek düşman alarm durumunu bul
            EnemyController.EnemyState highestState = EnemyController.EnemyState.Idle;
            float maxSuspicion = 0f;

            var enemies = FindObjectsByType<EnemyController>(FindObjectsSortMode.None);
            foreach (var e in enemies)
            {
                if (e.IsDead) continue;
                float d = Vector3.Distance(e.transform.position, stealth.transform.position);
                if (d < 45f)
                {
                    if (e.IsAlerted)
                    {
                        highestState = EnemyController.EnemyState.Combat;
                        break;
                    }
                    if (e.suspicionMeter > maxSuspicion)
                    {
                        maxSuspicion = e.suspicionMeter;
                    }
                    if (e.initialState == EnemyController.EnemyState.Suspicious || maxSuspicion > 25f)
                    {
                        highestState = EnemyController.EnemyState.Suspicious;
                    }
                }
            }

            // UI Durumu
            if (highestState == EnemyController.EnemyState.Combat)
            {
                _indicatorRoot.SetActive(true);
                _stealthText.text = "! FARK EDİLDİ !";
                _stealthText.color = new Color(1.0f, 0.25f, 0.25f, 1f);
            }
            else if (highestState == EnemyController.EnemyState.Suspicious || maxSuspicion > 20f)
            {
                _indicatorRoot.SetActive(true);
                _stealthText.text = $"? ŞÜPHELİ ({(int)maxSuspicion}%)";
                _stealthText.color = new Color(1.0f, 0.85f, 0.2f, 1f);
            }
            else if (stealth.IsCrouching)
            {
                _indicatorRoot.SetActive(true);
                _stealthText.text = "GİZLİ";
                _stealthText.color = new Color(0.45f, 0.95f, 0.45f, 0.85f);
            }
            else
            {
                _indicatorRoot.SetActive(false);
            }
        }
    }
}
