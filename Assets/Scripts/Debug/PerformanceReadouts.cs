using System;
using System.IO;
using System.Text;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Profiling;

namespace EndlessCombat.Tuning
{
    /// <summary>
    /// "Performans" page of the tuning panel: frame rate, worst frame, memory, garbage per frame, how many enemies /
    /// animals / chunks are alive, pool and sound event counters. Measured now with placeholder cubes so the cost of
    /// real models, animations and effects can be compared later. "Raporu yaz" adds one line to
    /// persistentDataPath/perf_log.txt (the file can be sent back for reading).
    /// </summary>
    public class PerformanceReadouts : MonoBehaviour
    {
        private const string P = "Performans";
        private const int Window = 120; // frames kept for average / worst

        private readonly float[] _frames = new float[Window];
        private int _index, _filled;
        private float _worstRecent;
        private float _worstSinceReset;
        private ProfilerRecorder _gcAlloc;
        private ProfilerRecorder _drawCalls;
        private ProfilerRecorder _batches;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (FindAnyObjectByType<PerformanceReadouts>() != null) return;
            var go = new GameObject("PerformanceReadouts");
            DontDestroyOnLoad(go);
            go.AddComponent<PerformanceReadouts>();
        }

        private void OnEnable()
        {
            // Some counters only exist in the editor / development builds: a missing one reads "-"
            _gcAlloc = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame");
            _drawCalls = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count");
            _batches = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Batches Count");
        }

        private void OnDisable()
        {
            _gcAlloc.Dispose();
            _drawCalls.Dispose();
            _batches.Dispose();
        }

        private void Start()
        {
            TuningPanel.RegisterReadout(P, "FPS (ortalama)", () => Fps().ToString("0"));
            TuningPanel.RegisterReadout(P, "Kare süresi ort. / en kötü (ms)", () => $"{AverageMs():0.0} / {_worstRecent:0.0}");
            TuningPanel.RegisterReadout(P, "En kötü kare (sıfırlamadan beri, ms)", () => _worstSinceReset.ToString("0.0"));
            TuningPanel.RegisterReadout(P, "Bellek (MB, ayrılmış / rezerve)", () =>
                $"{Profiler.GetTotalAllocatedMemoryLong() / 1048576f:0} / {Profiler.GetTotalReservedMemoryLong() / 1048576f:0}");
            TuningPanel.RegisterReadout(P, "Çöp (GC) bu kare (KB)", () => Recorder(_gcAlloc, 1f / 1024f, "0.0"));
            TuningPanel.RegisterReadout(P, "Draw call / batch", () => $"{Recorder(_drawCalls, 1f, "0")} / {Recorder(_batches, 1f, "0")}");
            TuningPanel.RegisterReadout(P, "Canlı düşman / hayvan", () => $"{AliveEnemies()} / {AliveAnimals()}");
            TuningPanel.RegisterReadout(P, "Açık chunk", () =>
            {
                var cm = EndlessSurvival.World.ChunkManager.Instance;
                return cm != null && cm.ActiveChunks != null ? cm.ActiveChunks.Count.ToString() : "-";
            });
            TuningPanel.RegisterReadout(P, "Havuz: yapılan / tekrar kullanılan / dışarıda", () =>
                $"{Pooling.PrefabPool.Created} / {Pooling.PrefabPool.Reused} / {Pooling.PrefabPool.Active}");
            TuningPanel.RegisterReadout(P, "Mermi izi çizildi", () => Pooling.BulletTracers.Shown.ToString());
            TuningPanel.RegisterReadout(P, "Ses olayı (sessiz, toplam)", () => Audio.GameSound.TotalEvents.ToString());
            TuningPanel.RegisterReadout(P, "Kalıcı AI uyaranı", () => AI.AIStimuli.PersistentCount.ToString());
            TuningPanel.RegisterAction(P, "En kötü kareyi sıfırla", () => _worstSinceReset = 0f);
            TuningPanel.RegisterAction(P, "Raporu yaz (perf_log.txt)", WriteReport);
        }

        private void Update()
        {
            float ms = Time.unscaledDeltaTime * 1000f;
            _frames[_index] = ms;
            _index = (_index + 1) % Window;
            if (_filled < Window) _filled++;
            // The first seconds after loading are skipped for the all-time worst (scene load is not a hitch)
            if (Time.realtimeSinceStartup > 5f) _worstSinceReset = Mathf.Max(_worstSinceReset, ms);

            float worst = 0f;
            for (int i = 0; i < _filled; i++) worst = Mathf.Max(worst, _frames[i]);
            _worstRecent = worst;
        }

        private float AverageMs()
        {
            if (_filled == 0) return 0f;
            float sum = 0f;
            for (int i = 0; i < _filled; i++) sum += _frames[i];
            return sum / _filled;
        }

        private float Fps()
        {
            float ms = AverageMs();
            return ms > 0.001f ? 1000f / ms : 0f;
        }

        private static string Recorder(ProfilerRecorder r, float scale, string format)
        {
            return r.Valid ? (r.LastValue * scale).ToString(format) : "-";
        }

        private static int AliveEnemies()
        {
            int n = 0;
            var list = AI.EnemyController.Active;
            for (int i = 0; i < list.Count; i++)
                if (list[i] != null && !list[i].IsDead) n++;
            return n;
        }

        private static int AliveAnimals()
        {
            int n = 0;
            var list = EndlessSurvival.Survival.AnimalController.All;
            for (int i = 0; i < list.Count; i++)
                if (list[i] != null) n++;
            return n;
        }

        private void WriteReport()
        {
            try
            {
                var cm = EndlessSurvival.World.ChunkManager.Instance;
                var sb = new StringBuilder();
                sb.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                sb.Append(" scene=").Append(UnityEngine.SceneManagement.SceneManager.GetActiveScene().name);
                sb.Append(" fps=").Append(Fps().ToString("0"));
                sb.Append(" avgMs=").Append(AverageMs().ToString("0.0"));
                sb.Append(" worstMs=").Append(_worstRecent.ToString("0.0"));
                sb.Append(" worstEverMs=").Append(_worstSinceReset.ToString("0.0"));
                sb.Append(" memMB=").Append((Profiler.GetTotalAllocatedMemoryLong() / 1048576f).ToString("0"));
                sb.Append(" gcKB=").Append(Recorder(_gcAlloc, 1f / 1024f, "0.0"));
                sb.Append(" drawCalls=").Append(Recorder(_drawCalls, 1f, "0"));
                sb.Append(" enemies=").Append(AliveEnemies());
                sb.Append(" animals=").Append(AliveAnimals());
                sb.Append(" chunks=").Append(cm != null && cm.ActiveChunks != null ? cm.ActiveChunks.Count : -1);
                sb.Append(" res=").Append(Screen.width).Append('x').Append(Screen.height);
                sb.Append(" quality=").Append(QualitySettings.names[QualitySettings.GetQualityLevel()]);
                string path = Path.Combine(Application.persistentDataPath, "perf_log.txt");
                File.AppendAllText(path, sb.ToString() + Environment.NewLine);
                EndlessSurvival.Survival.SurvivalNotify.Show("Performans raporu yazıldı: perf_log.txt", 3f);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Performance] Report could not be written: {ex.Message}");
            }
        }
    }
}
