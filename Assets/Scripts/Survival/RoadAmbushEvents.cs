using UnityEngine;
using EndlessCombat.AI;
using EndlessSurvival.World;
using EndlessSurvival.World.Road;

namespace EndlessSurvival.Survival
{
    /// <summary>
    /// Road ambush while driving (2026-11-17, user: "the drive is the same all the time, enemies are rare"). No new
    /// models: an ambush squad (SquadKind.Ambush, AISignatures 21.5) is placed ahead of the vehicle, one man standing
    /// on the road as bait, the rest at both sides. They hold fire until you slow down, stop, get out or pass close
    /// (then they all open up). Driving through fast or going around is an answer; stopping is the trap.
    /// Rolled once per CheckEveryKm driven, never twice within MinMinutes, not near a Safe Zone, not in test scenes.
    /// Self-installs; tuning on the panel page "Olaylar".
    /// </summary>
    public class RoadAmbushEvents : MonoBehaviour
    {
        public static float CheckEveryKm = 1f;
        public static float Chance = 0.25f;
        public static float EarlyGameFactor = 0.6f;
        public static float MinMinutes = 4f;
        public static float AheadMeters = 180f;
        public static bool Enabled = true;

        public static int Triggered { get; private set; }

        private Vector3 _last;
        private bool _hasLast;
        private float _driven;
        private float _lastTime = -9999f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (FindAnyObjectByType<RoadAmbushEvents>() != null) return;
            var go = new GameObject("RoadAmbushEvents");
            DontDestroyOnLoad(go);
            go.AddComponent<RoadAmbushEvents>();
        }

        private void Start()
        {
            const string P = Camping.NightThreats.Page;
            EndlessCombat.Tuning.TuningPanel.RegisterToggle(P, "Yol pususu açık", () => Enabled, v => Enabled = v);
            EndlessCombat.Tuning.TuningPanel.Register(P, "Yol pususu: kaç km'de bir zar", () => CheckEveryKm, v => CheckEveryKm = v, 0.2f, 5f, 0.1f);
            EndlessCombat.Tuning.TuningPanel.Register(P, "Yol pususu: olasılık", () => Chance, v => Chance = v, 0f, 1f, 0.05f);
            EndlessCombat.Tuning.TuningPanel.Register(P, "Yol pususu: yeni karakter çarpanı", () => EarlyGameFactor, v => EarlyGameFactor = v, 0f, 1f, 0.1f);
            EndlessCombat.Tuning.TuningPanel.Register(P, "Yol pususu: en az ara (dk)", () => MinMinutes, v => MinMinutes = v, 0f, 30f, 0.5f);
            EndlessCombat.Tuning.TuningPanel.RegisterReadout(P, "Yol pususu: sonraki zara kalan (km)", () => Mathf.Max(0f, CheckEveryKm - _driven / 1000f).ToString("0.00"));
            EndlessCombat.Tuning.TuningPanel.RegisterReadout(P, "Yol pususu: bu oturumda çıkan", () => Triggered.ToString());
            EndlessCombat.Tuning.TuningPanel.RegisterAction(P, "Yol pususunu şimdi kur (önde)", () => TrySpawn(true));
        }

        private void Update()
        {
            if (!Enabled || Testing.TestArena.Active || ChunkManager.Instance == null) { _hasLast = false; return; }
            var v = SurvivalDirector.Vehicle;
            if (v == null || !SurvivalDirector.PlayerInVehicle) { _hasLast = false; return; }

            Vector3 p = v.transform.position;
            if (_hasLast)
            {
                Vector3 d = p - _last;
                d.y = 0f;
                float step = d.magnitude;
                if (step < 50f && v.CurrentSpeedKmh > 20f) _driven += step; // a teleport / load is not driving
            }
            _last = p;
            _hasLast = true;

            if (_driven < CheckEveryKm * 1000f) return;
            _driven = 0f;
            if (Time.time - _lastTime < MinMinutes * 60f) return;
            float chance = Chance * (DifficultyCurve.EarlyGame ? EarlyGameFactor : 1f);
            if (Random.value >= chance) return;
            TrySpawn(false);
        }

        private bool TrySpawn(bool forced)
        {
            var v = SurvivalDirector.Vehicle;
            Vector3 from = v != null ? v.transform.position : SurvivalDirector.PlayerPosition;
            Vector3 fwd = v != null ? v.transform.forward : Vector3.forward;
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.forward;
            Vector3 ahead = from + fwd.normalized * AheadMeters;

            var chunk = SurvivalDirector.ChunkAt(ahead);
            if (chunk == null) { if (forced) SurvivalNotify.Warn("Pusu kurulamadı: önde yol yok"); return false; }
            var spline = chunk.GetComponentInChildren<RoadSpline>();
            if (spline == null) return false;
            if (SafeZone.Near(ahead, 150f)) return false;

            Transform st = spline.transform;
            float z = st.InverseTransformPoint(ahead).z;
            Vector3 center = st.TransformPoint(spline.SampleAtZ(z));
            Vector3 tangent = st.TransformPoint(spline.SampleAtZ(z + 3f)) - center;
            tangent.y = 0f;
            if (tangent.sqrMagnitude < 0.01f) tangent = fwd;
            Vector3 right = Vector3.Cross(Vector3.up, tangent.normalized);
            if (Settlements.Blocked(chunk, center, 6f)) { if (forced) SurvivalNotify.Warn("Pusu kurulamadı: önde yerleşim var"); return false; }

            var prefabs = SurvivalSettings.Instance.humanEnemyPrefabs;
            if (prefabs == null || prefabs.Length == 0) return false;
            var rng = new SeededRandom(Random.Range(int.MinValue, int.MaxValue));
            int count = DifficultyCurve.CapHumans(DifficultyCurve.ScaleEnemyCount(DifficultyCurve.EarlyGame ? 2 : rng.Range(3, 5), center));
            count = Mathf.Max(2, count);

            var squad = EnemySquad.Create(SquadKind.Ambush, center);
            int made = 0;
            for (int i = 0; i < count; i++)
            {
                Vector3 p;
                if (i == 0) p = center + tangent.normalized * rng.Range(-2f, 2f); // the bait on the road
                else
                {
                    float side = i % 2 == 1 ? 1f : -1f;
                    p = center + right * side * rng.Range(9f, 14f) + tangent.normalized * rng.Range(-10f, 10f);
                }
                if (!Physics.Raycast(p + Vector3.up * 60f, Vector3.down, out RaycastHit hit, 150f, ~0, QueryTriggerInteraction.Ignore)) continue;
                p = hit.point;
                Vector3 look = center - p;
                look.y = 0f;
                if (i == 0 || look.sqrMagnitude < 0.01f) look = from - p; // the bait faces the coming car
                look.y = 0f;
                var prefab = DifficultyCurve.PickEnemyPrefab(rng, prefabs, center);
                if (prefab == null) continue;
                var go = Instantiate(prefab, p + Vector3.up * 0.1f, look.sqrMagnitude > 0.01f ? Quaternion.LookRotation(look) : Quaternion.identity, chunk.transform);
                var enemy = go.GetComponent<EnemyController>();
                if (enemy == null) continue;
                squad.Add(enemy);
                DifficultyCurve.ApplyToEnemy(enemy, center);
                enemy.initialState = EnemyController.EnemyState.Idle;
                made++;
            }
            if (made == 0) return false;
            _lastTime = Time.time;
            Triggered++;
            Debug.Log($"[RoadAmbush] {made} men {AheadMeters:0} m ahead (forced={forced})");
            return true;
        }
    }
}
