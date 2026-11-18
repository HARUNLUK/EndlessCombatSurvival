using UnityEngine;
using EndlessSurvival.Survival;
using EndlessSurvival.World;
using EndlessSurvival.World.Road;

namespace EndlessSurvival.Camping
{
    /// <summary>
    /// What can happen while the player sleeps (2026-11-17, user: "nights feel empty, animals never come to the camp,
    /// camping or not makes no difference"). The choice of where and how to sleep matters:
    ///   - Raiders: before, a raid needed a known squad nearby (AbstractGroups.CampThreat), which almost never held.
    ///     Now wandering raiders can also find the camp: chance = WanderRaidChance x camp visibility (fire at night is
    ///     the brightest) x road factor (a camp next to the road is found far more often).
    ///   - Animals: sleeping with no burning fire nearby lets a pack come (wolves, now and then a bear). A fire keeps
    ///     them away. In the vehicle you are safe from them but they are around when you wake.
    /// Sleeping in the vehicle already rests less (PlayerVitals) and halves the raid roll (VehicleHome).
    /// Tuning: panel page "Olaylar".
    /// </summary>
    public static class NightThreats
    {
        public const string Page = "Olaylar";

        public static float WanderRaidChance = 0.3f;
        public static float RoadNear = 40f;
        public static float RoadCampFactor = 1.8f;
        public static float AwayFromRoadFactor = 0.5f;
        public static float EarlyGameFactor = 0.5f;
        public static float AnimalVisitChance = 0.35f;
        public static float FireKeepsAnimals = 12f;

        private static bool _registered;

        public static void RegisterTuning()
        {
            if (_registered) return;
            _registered = true;
            EndlessCombat.Tuning.TuningPanel.Register(Page, "Uyku baskını: gezgin baskıncı olasılığı", () => WanderRaidChance, v => WanderRaidChance = v, 0f, 1f, 0.05f);
            EndlessCombat.Tuning.TuningPanel.Register(Page, "Uyku baskını: yola yakın sayılır (m)", () => RoadNear, v => RoadNear = v, 10f, 150f, 5f);
            EndlessCombat.Tuning.TuningPanel.Register(Page, "Uyku baskını: yol kenarı çarpanı", () => RoadCampFactor, v => RoadCampFactor = v, 0.5f, 4f, 0.1f);
            EndlessCombat.Tuning.TuningPanel.Register(Page, "Uyku baskını: yoldan uzak çarpanı", () => AwayFromRoadFactor, v => AwayFromRoadFactor = v, 0f, 2f, 0.1f);
            EndlessCombat.Tuning.TuningPanel.Register(Page, "Uyku baskını: yeni karakter çarpanı", () => EarlyGameFactor, v => EarlyGameFactor = v, 0f, 1f, 0.1f);
            EndlessCombat.Tuning.TuningPanel.Register(Page, "Ateşsiz uykuda hayvan gelme olasılığı", () => AnimalVisitChance, v => AnimalVisitChance = v, 0f, 1f, 0.05f);
            EndlessCombat.Tuning.TuningPanel.RegisterReadout(Page, "Burada uyursan baskın olasılığı", () =>
            {
                Vector3 p = SurvivalDirector.PlayerPosition;
                return $"%{Mathf.RoundToInt(WanderRaid(p) * 100f)} (yol {RoadDistanceText(p)})";
            });
        }

        /// <summary>Horizontal distance from a point to the main road of its chunk (-1 when unknown).</summary>
        public static float RoadDistance(Vector3 pos)
        {
            var chunk = SurvivalDirector.ChunkAt(pos);
            if (chunk == null) return -1f;
            var spline = chunk.GetComponentInChildren<RoadSpline>();
            return spline != null ? spline.GetDistanceToSpline(pos) : -1f;
        }

        private static string RoadDistanceText(Vector3 p)
        {
            float d = RoadDistance(p);
            return d < 0f ? "?" : Mathf.RoundToInt(d) + " m";
        }

        /// <summary>Chance (0..1) that wandering raiders find a camp here tonight.</summary>
        public static float WanderRaid(Vector3 camp)
        {
            if (SafeZone.Near(camp)) return 0f;
            float vis = EndlessCombat.AI.AbstractGroups.CampVisibility(camp);
            float road = RoadDistance(camp);
            float roadFactor = road >= 0f && road < RoadNear ? RoadCampFactor : AwayFromRoadFactor;
            float early = DifficultyCurve.EarlyGame ? EarlyGameFactor : 1f;
            return Mathf.Clamp01(WanderRaidChance * vis * roadFactor * early);
        }

        /// <summary>
        /// After a sleep with no burning fire nearby: maybe a pack is around the camp. Returns true when one came.
        /// </summary>
        public static bool RollAnimals(Vector3 pos, bool inVehicle)
        {
            if (SafeZone.Near(pos, 120f)) return false;
            if (CraftingSystem.NearestBurningFire(pos, FireKeepsAnimals) != null) return false;
            if (Random.value >= AnimalVisitChance) return false;
            var chunk = SurvivalDirector.ChunkAt(pos);
            if (chunk == null) return false;

            bool bear = Random.value < 0.15f;
            AnimalKind kind = bear ? AnimalKind.Bear : AnimalKind.Wolf;
            int count = bear ? 1 : Random.Range(2, 4);
            float yaw0 = Random.Range(0f, 360f);
            Vector3 dir = Quaternion.Euler(0f, yaw0, 0f) * Vector3.forward;
            Vector3 center = pos + dir * Random.Range(22f, 32f);
            int pack = Random.Range(100000, 999999);
            int made = 0;
            for (int i = 0; i < count; i++)
            {
                Vector3 p = center + new Vector3(Random.Range(-3f, 3f), 0f, Random.Range(-3f, 3f));
                if (!Physics.Raycast(p + Vector3.up * 60f, Vector3.down, out RaycastHit hit, 150f, ~0, QueryTriggerInteraction.Ignore)) continue;
                Vector3 face = pos - hit.point;
                face.y = 0f;
                float yaw = face.sqrMagnitude > 0.01f ? Quaternion.LookRotation(face).eulerAngles.y : 0f;
                if (AnimalController.Spawn(kind, hit.point + Vector3.up * 0.1f, chunk.transform, pack, yaw) != null) made++;
            }
            if (made == 0) return false;
            string what = bear ? "bir ayı" : "kurtlar";
            SurvivalNotify.Warn(inVehicle
                ? $"Gece aracın etrafında {what} dolaştı. Hâlâ yakındalar."
                : $"Ateş yoktu: {what} kampa geldi! Uyandın.");
            return true;
        }
    }
}
