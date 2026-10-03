using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using EndlessSurvival.Inventory;

namespace EndlessSurvival.Camping
{
    public class PlayerNeedReliever : MonoBehaviour
    {
        [Header("Settings")]
        [Tooltip("İhtiyaç giderme tuşu")]
        public KeyCode reliefKey = KeyCode.P;

        private PlayerSurvival _survival;
        private Image _fadeOverlay;

        private void Start()
        {
            _survival = GetComponent<PlayerSurvival>();
        }

        private void Update()
        {
            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            if (keyboard != null && keyboard.pKey.wasPressedThisFrame)
            {
                if (CampfireUI.IsOpen) return;
                TryRelieveNeed();
            }
        }

        public void TryRelieveNeed()
        {
            if (!gameObject.activeInHierarchy) return;

            if (_survival == null) _survival = GetComponent<PlayerSurvival>();
            if (_survival == null) return;

            if (_survival.Bladder < 15f)
            {
                Debug.Log("<color=yellow>[İhtiyaç] Şu an bir ihtiyacın yok.</color>");
                return;
            }

            StartCoroutine(ReliefRoutine());
        }

        private IEnumerator ReliefRoutine()
        {
            EnsureFadeOverlay();

            // Ekran kararır (Fade to black)
            if (_fadeOverlay != null)
            {
                for (float t = 0; t <= 1f; t += Time.deltaTime * 2.5f)
                {
                    _fadeOverlay.color = new Color(0, 0, 0, t);
                    yield return null;
                }
            }

            yield return new WaitForSeconds(1.2f);

            // İhtiyacı sıfırla
            if (_survival != null)
            {
                _survival.RelieveNeed();
            }

            // Ekran açılır (Fade from black)
            if (_fadeOverlay != null)
            {
                for (float t = 1f; t >= 0f; t -= Time.deltaTime * 2.5f)
                {
                    _fadeOverlay.color = new Color(0, 0, 0, t);
                    yield return null;
                }
                _fadeOverlay.color = new Color(0, 0, 0, 0);
            }

            Debug.Log("<color=green>[İhtiyaç] İhtiyacını giderdin ve rahatladın.</color>");
        }

        private void EnsureFadeOverlay()
        {
            if (_fadeOverlay != null) return;

            var canvas = FindObjectOfType<Canvas>();
            if (canvas == null)
            {
                var canvasObj = new GameObject("ReliefCanvas");
                canvas = canvasObj.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = 99;
                canvasObj.AddComponent<CanvasScaler>();
            }

            var fadeObj = new GameObject("NeedFadeOverlay");
            fadeObj.transform.SetParent(canvas.transform, false);
            var rect = fadeObj.AddComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            _fadeOverlay = fadeObj.AddComponent<Image>();
            _fadeOverlay.color = new Color(0, 0, 0, 0);
            _fadeOverlay.raycastTarget = false;
        }
    }
}
