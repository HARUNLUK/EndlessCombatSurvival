using System;
using UnityEngine;
using EndlessSurvival.World;

namespace EndlessSurvival.Survival
{
    public enum WeatherType
    {
        Clear,
        Rain,
        Snow,
        Fog
    }

    /// <summary>
    /// 4.2: weather per time segment, picked from the world seed + segment index (same seed, same weather),
    /// adjusted by the biome the player is in (snow on mountains, no rain in the desert).
    /// Drives particles around the camera, light/fog (DayNightCycle statics), temperature, wetness and grip.
    /// </summary>
    public class WeatherSystem : MonoBehaviour
    {
        public static WeatherSystem Instance { get; private set; }

        public WeatherType Current { get; private set; } = WeatherType.Clear;
        /// <summary>0..1 blend of the current weather (fades in/out).</summary>
        public float Intensity { get; private set; }
        public float RainIntensity => Current == WeatherType.Rain ? Intensity : 0f;
        public float SnowIntensity => Current == WeatherType.Snow ? Intensity : 0f;

        public float TemperatureOffset
        {
            get
            {
                var s = SurvivalSettings.Instance;
                switch (Current)
                {
                    case WeatherType.Rain: return s.rainOffset * Intensity;
                    case WeatherType.Snow: return s.snowOffset * Intensity;
                    case WeatherType.Fog: return s.fogOffset * Intensity;
                }
                return 0f;
            }
        }

        public float GripMultiplier
        {
            get
            {
                var s = SurvivalSettings.Instance;
                switch (Current)
                {
                    case WeatherType.Rain: return Mathf.Lerp(1f, s.rainGripMultiplier, Intensity);
                    case WeatherType.Snow: return Mathf.Lerp(1f, s.snowGripMultiplier, Intensity);
                }
                return 1f;
            }
        }

        public string DisplayName
        {
            get
            {
                switch (Current)
                {
                    case WeatherType.Rain: return "Yağmur";
                    case WeatherType.Snow: return "Kar";
                    case WeatherType.Fog: return "Sis";
                }
                return "Açık";
            }
        }

        private WeatherType _target = WeatherType.Clear;
        private long _segment = long.MinValue;
        private float _check;
        private ParticleSystem _rain, _snow;
        private Transform _fx;

        private void Awake() => Instance = this;

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            DayNightCycle.WeatherLightMultiplier = 1f;
            DayNightCycle.WeatherFogDensityAdd = 0f;
        }

        private void Update()
        {
            _check -= Time.deltaTime;
            if (_check <= 0f)
            {
                _check = 2f;
                PickTarget();
            }

            // Fade out the old weather, then fade in the new one
            if (Current != _target)
            {
                Intensity = Mathf.MoveTowards(Intensity, 0f, Time.deltaTime / 8f);
                if (Intensity <= 0.001f) Current = _target;
            }
            else
            {
                Intensity = Mathf.MoveTowards(Intensity, Current == WeatherType.Clear ? 0f : 1f, Time.deltaTime / 12f);
            }

            ApplyVisuals();
        }

        private void PickTarget()
        {
            var s = SurvivalSettings.Instance;
            double hours = GameClock.TotalHours + (DayNightCycle.Instance != null ? 0 : 0);
            long segment = (long)Math.Floor(hours / Mathf.Max(1f, s.weatherSegmentHours));
            var chunk = SurvivalDirector.ChunkAt(SurvivalDirector.PlayerPosition);
            ChunkBiomeType biome = chunk != null ? chunk.biomeType : ChunkBiomeType.Forest;
            int seed = chunk != null ? chunk.WorldSeed : 12345;

            if (segment == _segment && biome == _lastBiome) return;
            _segment = segment;
            _lastBiome = biome;

            var rng = new SeededRandom(SeededRandom.Combine(SeededRandom.Combine(seed, "weather"), (int)(segment & 0x7fffffff)));
            float roll = rng.Value;
            WeatherType w = WeatherType.Clear;
            switch (biome)
            {
                case ChunkBiomeType.Desert:
                    if (roll < s.fogChance * 0.3f) w = WeatherType.Fog;
                    break;
                case ChunkBiomeType.Mountain:
                    if (roll < s.snowChanceMountain) w = WeatherType.Snow;
                    else if (roll < s.snowChanceMountain + s.fogChance) w = WeatherType.Fog;
                    break;
                case ChunkBiomeType.Coast:
                    if (roll < s.rainChance * 1.2f) w = WeatherType.Rain;
                    else if (roll < s.rainChance * 1.2f + s.fogChance * 1.5f) w = WeatherType.Fog;
                    break;
                default:
                    if (roll < s.rainChance) w = WeatherType.Rain;
                    else if (roll < s.rainChance + s.fogChance) w = WeatherType.Fog;
                    break;
            }
            if (w != _target)
            {
                _target = w;
                if (w != WeatherType.Clear) SurvivalNotify.Show($"Hava değişiyor: {NameOf(w)}");
            }
        }

        private ChunkBiomeType _lastBiome;

        private static string NameOf(WeatherType w) =>
            w == WeatherType.Rain ? "Yağmur" : w == WeatherType.Snow ? "Kar" : w == WeatherType.Fog ? "Sis" : "Açık";

        // ---------------- visuals ----------------

        private void ApplyVisuals()
        {
            float cloud = Current == WeatherType.Clear ? 0f : Intensity;
            DayNightCycle.WeatherLightMultiplier = Mathf.Lerp(1f, Current == WeatherType.Fog ? 0.75f : 0.6f, cloud);
            float fog = Current == WeatherType.Fog ? 0.02f : Current == WeatherType.Snow ? 0.008f : Current == WeatherType.Rain ? 0.004f : 0f;
            DayNightCycle.WeatherFogDensityAdd = fog * Intensity;

            var cam = Camera.main;
            if (cam == null) return;
            EnsureParticles();
            _fx.position = cam.transform.position + Vector3.up * 12f;
            SetRate(_rain, RainIntensity * 1400f);
            SetRate(_snow, SnowIntensity * 500f);
        }

        private static void SetRate(ParticleSystem ps, float rate)
        {
            if (ps == null) return;
            var em = ps.emission;
            em.rateOverTime = rate;
            if (rate > 1f && !ps.isPlaying) ps.Play();
            else if (rate <= 1f && ps.isPlaying) ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }

        private void EnsureParticles()
        {
            if (_fx != null) return;
            _fx = new GameObject("WeatherFX").transform;
            _fx.SetParent(transform, false);
            _rain = MakeSystem("Rain", new Color(0.7f, 0.75f, 0.85f, 0.55f), 0.02f, 0.5f, 22f, true);
            _snow = MakeSystem("Snow", new Color(1f, 1f, 1f, 0.9f), 0.08f, 0.08f, 2.2f, false);
        }

        private ParticleSystem MakeSystem(string name, Color color, float size, float length, float speed, bool stretch)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_fx, false);
            go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f); // emit downwards
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true;
            main.startLifetime = stretch ? 0.9f : 6f;
            main.startSpeed = speed;
            main.startSize = size;
            main.startColor = color;
            main.maxParticles = 5000;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(40f, 40f, 1f);
            var em = ps.emission;
            em.rateOverTime = 0f;
            if (!stretch)
            {
                var noise = ps.noise;
                noise.enabled = true;
                noise.strength = 0.6f;
                noise.frequency = 0.3f;
            }
            var r = go.GetComponent<ParticleSystemRenderer>();
            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader == null) shader = Shader.Find("Particles/Standard Unlit");
            if (shader != null)
            {
                var m = new Material(shader);
                if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
                m.color = color;
                r.sharedMaterial = m;
            }
            if (stretch)
            {
                r.renderMode = ParticleSystemRenderMode.Stretch;
                r.lengthScale = 1f;
                r.velocityScale = length * 0.1f;
            }
            return ps;
        }
    }
}
