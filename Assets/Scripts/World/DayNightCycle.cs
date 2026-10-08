using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace EndlessSurvival.World
{
    /// <summary>
    /// Manages the in-game 24-hour day/night cycle, sun rotation, ambient lighting, and time skipping (sleeping).
    /// </summary>
    public class DayNightCycle : MonoBehaviour
    {
        public static DayNightCycle Instance { get; private set; }

        [Header("Time Settings")]
        [Tooltip("Mevcut oyun saati (0.00 - 24.00 arası). Varsayılan 10:00 (Gündüz)")]
        [Range(0f, 24f)]
        public float currentHour = 10f;

        [Tooltip("Gerçek dünyada 1 tam günün (24 oyun saati) süreceği dakika")]
        public float dayDurationInMinutes = 18f;

        [Tooltip("Zaman akış hızı çarpanı (1 = normal)")]
        public float timeSpeedMultiplier = 1f;

        [Tooltip("Zamanın kendiliğinden akıp akmayacağı")]
        public bool isTimeProgressing = true;

        [Header("Sun & Moon Light")]
        [Tooltip("Gökyüzündeki ana güneş (Directional Light)")]
        public Light sunLight;

        [Tooltip("Güneşin gökyüzündeki yatay açısı (Yaw)")]
        public float sunYawAngle = 160f;

        [Header("Lighting Curves & Colors")]
        public float maxSunIntensity = 1.8f;
        public float minNightIntensity = 0.12f;

        [Header("Atmosphere & Fog")]
        public bool controlFog = true;
        [Tooltip("Kapalıysa sahnedeki sis tamamen kapatılır (controlFog açıkken)")]
        public bool enableFog = false;
        public float fogDensity = 0.0025f;

        // Internal cached colors for smooth transitions
        private readonly Color _daySunColor = new Color(1.0f, 0.96f, 0.90f);
        private readonly Color _dawnSunColor = new Color(1.0f, 0.62f, 0.35f);
        private readonly Color _duskSunColor = new Color(1.0f, 0.45f, 0.22f);
        private readonly Color _nightSunColor = new Color(0.35f, 0.48f, 0.78f); // Ay ışığı

        private readonly Color _dayAmbientSky = new Color(0.45f, 0.55f, 0.68f);
        private readonly Color _nightAmbientSky = new Color(0.04f, 0.06f, 0.11f);

        private readonly Color _dayFogColor = new Color(0.68f, 0.76f, 0.85f);
        private readonly Color _duskFogColor = new Color(0.80f, 0.45f, 0.30f);
        private readonly Color _nightFogColor = new Color(0.02f, 0.03f, 0.07f);

        public float CurrentHour => currentHour;
        public bool IsNight => currentHour < 5.75f || currentHour >= 19.5f;
        public bool IsDay => !IsNight;

        public event Action<float> OnHourChanged;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoInitialize()
        {
            if (Instance == null)
            {
                var existing = FindFirstObjectByType<DayNightCycle>();
                if (existing == null)
                {
                    GameObject go = new GameObject("DayNightCycle_Manager");
                    go.AddComponent<DayNightCycle>();
                    DontDestroyOnLoad(go);
                }
            }
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            FindOrCreateSun();
            SetupEnvironmentSettings();
            UpdateLighting(true);
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void Update()
        {
            if (isTimeProgressing)
            {
                float hoursPerSecond = 24f / (Mathf.Max(1f, dayDurationInMinutes) * 60f);
                float previousHour = currentHour;
                currentHour = (currentHour + hoursPerSecond * Time.deltaTime * timeSpeedMultiplier) % 24f;

                if (Mathf.FloorToInt(previousHour) != Mathf.FloorToInt(currentHour))
                {
                    OnHourChanged?.Invoke(currentHour);
                }
            }

            UpdateLighting(false);
        }

        public void FindOrCreateSun()
        {
            if (sunLight != null) return;

            if (RenderSettings.sun != null)
            {
                sunLight = RenderSettings.sun;
                return;
            }

            var lights = FindObjectsByType<Light>(FindObjectsSortMode.None);
            foreach (var l in lights)
            {
                if (l.type == LightType.Directional)
                {
                    sunLight = l;
                    RenderSettings.sun = l;
                    return;
                }
            }

            // Eğer sahnede hiç directional light yoksa oluştur
            GameObject sunObj = new GameObject("Directional Light");
            sunLight = sunObj.AddComponent<Light>();
            sunLight.type = LightType.Directional;
            sunLight.shadows = LightShadows.Soft;
            RenderSettings.sun = sunLight;
        }

        private void SetupEnvironmentSettings()
        {
            RenderSettings.ambientMode = AmbientMode.Trilight;
            if (controlFog)
            {
                RenderSettings.fog = enableFog;
                RenderSettings.fogMode = FogMode.ExponentialSquared;
                RenderSettings.fogDensity = fogDensity;
            }
        }

        /// <summary>
        /// Günün saatine göre güneş açısını, ışık rengini, şiddetini ve sis/ambiyansı günceller.
        /// </summary>
        public void UpdateLighting(bool forceInstant)
        {
            if (sunLight == null) FindOrCreateSun();
            if (sunLight == null) return;

            // 1. Güneş Rotasyonu
            // 06:00 -> 0 derece (Ufuk doğu)
            // 12:00 -> 90 derece (Tepe noktası)
            // 18:00 -> 180 derece (Ufuk batı)
            // 00:00 -> 270 derece (Gece altı)
            float sunPitch = (currentHour / 24f) * 360f - 90f;
            sunLight.transform.rotation = Quaternion.Euler(sunPitch, sunYawAngle, 0f);

            // 2. Işık Şiddeti ve Renk Faktörleri
            // Gündüz zirvesi: 12:00 (factor 1.0)
            // Gece yarısı: 00:00 (factor 0.0)
            float dayFactor = Mathf.Clamp01(Mathf.Sin((currentHour - 6f) / 12f * Mathf.PI));
            bool isDusk = currentHour >= 17f && currentHour < 19.5f;
            bool isDawn = currentHour >= 5.5f && currentHour < 7.5f;

            Color targetColor;
            float targetIntensity;

            if (currentHour >= 7.5f && currentHour < 17f)
            {
                // Tam gündüz
                targetColor = _daySunColor;
                targetIntensity = Mathf.Lerp(1.2f, maxSunIntensity, dayFactor);
            }
            else if (isDawn)
            {
                // Şafak vakti (güneş doğuyor)
                float t = (currentHour - 5.5f) / 2f;
                targetColor = Color.Lerp(_dawnSunColor, _daySunColor, t);
                targetIntensity = Mathf.Lerp(minNightIntensity, 1.2f, t);
            }
            else if (isDusk)
            {
                // Gün batımı (alacakaranlık)
                float t = (currentHour - 17f) / 2.5f;
                targetColor = Color.Lerp(_duskSunColor, _nightSunColor, t);
                targetIntensity = Mathf.Lerp(1.2f, minNightIntensity, t);
            }
            else
            {
                // Gece (Ay ışığı modu)
                targetColor = _nightSunColor;
                targetIntensity = minNightIntensity;
            }

            sunLight.color = targetColor;
            sunLight.intensity = targetIntensity;
            sunLight.shadows = (currentHour >= 5.5f && currentHour <= 19.5f) ? LightShadows.Soft : LightShadows.Hard;

            // 3. Ambiyans Aydınlatması
            Color ambientSky = Color.Lerp(_nightAmbientSky, _dayAmbientSky, dayFactor);
            Color ambientEquator = ambientSky * 0.7f;
            Color ambientGround = ambientSky * 0.4f;

            RenderSettings.ambientSkyColor = ambientSky;
            RenderSettings.ambientEquatorColor = ambientEquator;
            RenderSettings.ambientGroundColor = ambientGround;
            RenderSettings.ambientIntensity = Mathf.Lerp(0.35f, 1.0f, dayFactor);

            // 4. Sis Ayarı
            if (controlFog)
            {
                RenderSettings.fog = enableFog;
                Color targetFog;
                if (isDusk)
                    targetFog = _duskFogColor;
                else if (isDawn)
                    targetFog = Color.Lerp(_duskFogColor, _dayFogColor, 0.5f);
                else
                    targetFog = Color.Lerp(_nightFogColor, _dayFogColor, dayFactor);

                RenderSettings.fogColor = targetFog;
                RenderSettings.fogDensity = Mathf.Lerp(fogDensity * 1.4f, fogDensity, dayFactor);
            }
        }

        /// <summary>
        /// Saati belirli bir miktarda ileri sarar.
        /// </summary>
        public void AdvanceHours(float hours)
        {
            currentHour = (currentHour + hours) % 24f;
            UpdateLighting(true);
            OnHourChanged?.Invoke(currentHour);
        }

        /// <summary>
        /// Saati doğrudan belirli bir değere ayarlar (Kayıt yükleme).
        /// </summary>
        public void SetTime(float hour)
        {
            currentHour = Mathf.Repeat(hour, 24f);
            UpdateLighting(true);
            OnHourChanged?.Invoke(currentHour);
        }

        /// <summary>
        /// Kamp uykusu: Eğer gündüzse geceye (21:30), geceyse sabaha (07:30) geçirir.
        /// </summary>
        public (float hoursPassed, string summary) SleepToNextPhase()
        {
            float previousHour = currentHour;
            float targetHour;
            string phaseName;

            if (currentHour >= 6.5f && currentHour < 19.0f)
            {
                // Gündüz uyundu -> Geceye geçiş
                targetHour = 21.5f; // 21:30 Gece
                phaseName = "Gece Çöktü";
            }
            else
            {
                // Gece uyundu -> Sabaha geçiş
                targetHour = 7.5f; // 07:30 Sabah
                phaseName = "Güneş Doğdu";
            }

            float hoursPassed;
            if (targetHour > currentHour)
            {
                hoursPassed = targetHour - currentHour;
            }
            else
            {
                hoursPassed = (24f - currentHour) + targetHour;
            }

            // En az 4 saat uyunsun
            if (hoursPassed < 4f)
            {
                hoursPassed += 12f;
                targetHour = (targetHour + 12f) % 24f;
            }

            currentHour = targetHour;
            UpdateLighting(true);
            OnHourChanged?.Invoke(currentHour);

            int roundedHours = Mathf.RoundToInt(hoursPassed);
            string timeStr = GetTimeString();
            string summary = $"{roundedHours} saat uyunarak dinlenildi. {phaseName} (Saat: {timeStr})";

            Debug.Log($"<color=cyan>[DayNightCycle] {summary}</color>");
            return (hoursPassed, summary);
        }

        /// <summary>
        /// "14:35" formatında saati döndürür.
        /// </summary>
        public string GetTimeString()
        {
            int hours = Mathf.FloorToInt(currentHour);
            int minutes = Mathf.FloorToInt((currentHour - hours) * 60f);
            return $"{hours:00}:{minutes:00}";
        }

        /// <summary>
        /// "Gündüz", "Gece", "Şafak", "Akşamüstü" durum metnini döndürür.
        /// </summary>
        public string GetPhaseString()
        {
            if (currentHour >= 5.5f && currentHour < 7.5f) return "Şafak";
            if (currentHour >= 7.5f && currentHour < 17.5f) return "Gündüz";
            if (currentHour >= 17.5f && currentHour < 19.5f) return "Akşamüstü";
            return "Gece";
        }

        public string GetTimeWithIcon()
        {
            string icon;
            if (currentHour >= 5.5f && currentHour < 7.5f) icon = "🌅";
            else if (currentHour >= 7.5f && currentHour < 17.5f) icon = "☀️";
            else if (currentHour >= 17.5f && currentHour < 19.5f) icon = "🌇";
            else icon = "🌙";

            return $"{icon} {GetTimeString()} ({GetPhaseString()})";
        }
    }
}
