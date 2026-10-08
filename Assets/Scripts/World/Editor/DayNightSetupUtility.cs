using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using EndlessSurvival.World;

namespace EndlessSurvival.World.Editor
{
    public static class DayNightSetupUtility
    {
        [MenuItem("Endless Survival/Setup Day-Night Cycle")]
        [MenuItem("Tools/Setup Day-Night Cycle")]
        public static void SetupDayNightCycle()
        {
            Debug.Log("<color=cyan>[DayNightSetup] Gece/Gündüz ve Güneş Döngüsü Kurulumu Başlatılıyor...</color>");

            // 1. Sahnede Directional Light bul veya oluştur
            Light sun = RenderSettings.sun;
            if (sun == null)
            {
                var lights = Object.FindObjectsByType<Light>(FindObjectsSortMode.None);
                foreach (var l in lights)
                {
                    if (l.type == LightType.Directional)
                    {
                        sun = l;
                        break;
                    }
                }
            }

            if (sun == null)
            {
                GameObject sunObj = new GameObject("Directional Light");
                sun = sunObj.AddComponent<Light>();
                sun.type = LightType.Directional;
                sun.shadows = LightShadows.Soft;
                Undo.RegisterCreatedObjectUndo(sunObj, "Create Directional Light");
            }

            RenderSettings.sun = sun;
            sun.shadows = LightShadows.Soft;

            // 2. DayNightCycle GameObject'i
            var manager = Object.FindFirstObjectByType<DayNightCycle>();
            if (manager == null)
            {
                GameObject mgrObj = new GameObject("DayNightCycle_Manager");
                manager = mgrObj.AddComponent<DayNightCycle>();
                Undo.RegisterCreatedObjectUndo(mgrObj, "Create DayNightCycle Manager");
            }

            manager.sunLight = sun;
            manager.currentHour = 10f; // 10:00 Gündüz başlangıç
            manager.dayDurationInMinutes = 18f; // 18 gerçek dakika = 24 oyun saati
            manager.isTimeProgressing = true;
            manager.controlFog = true;
            manager.enableFog = false;
            manager.fogDensity = 0.0022f;

            EditorUtility.SetDirty(manager);

            // 3. RenderSettings Ambiyans ve Sis
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.fog = false;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogDensity = 0.0022f;

            // 4. TimeHUD Kurulumu
            var hud = Object.FindFirstObjectByType<TimeHUD>();
            if (hud == null)
            {
                GameObject hudObj = new GameObject("TimeHUD_Canvas");
                var canvas = hudObj.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = 15;

                var scaler = hudObj.AddComponent<UnityEngine.UI.CanvasScaler>();
                scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920, 1080);

                hudObj.AddComponent<UnityEngine.UI.GraphicRaycaster>();
                hud = hudObj.AddComponent<TimeHUD>();

                // Badge
                GameObject badge = new GameObject("TimeBadge");
                badge.transform.SetParent(hudObj.transform, false);
                var rect = badge.AddComponent<RectTransform>();
                rect.anchorMin = new Vector2(1f, 1f);
                rect.anchorMax = new Vector2(1f, 1f);
                rect.pivot = new Vector2(1f, 1f);
                rect.anchoredPosition = new Vector2(-25f, -25f);
                rect.sizeDelta = new Vector2(210f, 42f);

                var bgImg = badge.AddComponent<UnityEngine.UI.Image>();
                bgImg.color = new Color(0.08f, 0.10f, 0.14f, 0.78f);

                // Text
                GameObject textObj = new GameObject("Text_Time");
                textObj.transform.SetParent(badge.transform, false);
                var textRect = textObj.AddComponent<RectTransform>();
                textRect.anchorMin = Vector2.zero;
                textRect.anchorMax = Vector2.one;
                textRect.offsetMin = new Vector2(10f, 0f);
                textRect.offsetMax = new Vector2(-10f, 0f);

                var txt = textObj.AddComponent<UnityEngine.UI.Text>();
                txt.alignment = TextAnchor.MiddleCenter;
                txt.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf") ?? Resources.GetBuiltinResource<Font>("Arial.ttf");
                txt.fontSize = 17;
                txt.fontStyle = FontStyle.Bold;
                txt.color = Color.white;
                hud.timeText = txt;

                Undo.RegisterCreatedObjectUndo(hudObj, "Create TimeHUD");
            }

            // 5. Güncellemeyi çalıştır
            manager.UpdateLighting(true);

            // Sahneyi kaydet
            var activeScene = EditorSceneManager.GetActiveScene();
            if (activeScene.IsValid())
            {
                EditorSceneManager.MarkSceneDirty(activeScene);
                EditorSceneManager.SaveScene(activeScene);
            }

            Debug.Log("<color=green>[DayNightSetup] KURULUM TAMAMLANDI! 24 saatlik güneş/gece döngüsü, TimeHUD ve kamp uykusu zaman atlatma sistemi aktif.</color>");
        }
    }
}
