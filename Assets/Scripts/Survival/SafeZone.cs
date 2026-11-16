using System.Collections.Generic;
using UnityEngine;
using EndlessSurvival.Inventory;
using EndlessSurvival.World;
using EndlessSurvival.World.Road;

namespace EndlessSurvival.Survival
{
    /// <summary>
    /// A Safe Zone: the stop that comes back every SurvivalSettings.safeZoneEveryKm on the road. A real place, not
    /// only a screen: an arch over the road, a trader, a fuel pump and a mechanic that take money, and no enemies,
    /// wolves or camp raids around it. Reaching it is the milestone Journey counts (summary screen, save).
    /// Built by WorldPopulator in the chunk that contains the stop's kilometre.
    /// </summary>
    public class SafeZone : MonoBehaviour
    {
        public const float Radius = 70f;
        public static readonly List<SafeZone> All = new List<SafeZone>();

        public int Number { get; private set; }
        public Vector3 Center { get; private set; }

        private static readonly Color Green = new Color(0.25f, 0.55f, 0.3f);
        private static readonly Color Concrete = new Color(0.6f, 0.6f, 0.56f);

        internal static void ResetAll() => All.Clear();

        public static bool Near(Vector3 position, float radius = Radius)
        {
            foreach (var zone in All)
            {
                if (zone == null) continue;
                Vector3 d = zone.Center - position;
                d.y = 0f;
                if (d.sqrMagnitude < radius * radius) return true;
            }
            return false;
        }

        /// <summary>Does a stop fall inside this chunk? number = which stop, localZ = where along the chunk.</summary>
        public static bool TryGetStop(Chunk chunk, out int number, out float localZ)
        {
            number = 0;
            localZ = 0f;
            float every = Mathf.Max(0.05f, SurvivalSettings.Instance.safeZoneEveryKm);
            Vector3 a = chunk.transform.position;
            Vector3 b = a + chunk.transform.forward * chunk.chunkLength;
            float ka = DifficultyCurve.Km(a), kb = DifficultyCurve.Km(b);
            if (kb <= ka) return false;
            number = Mathf.FloorToInt(kb / every);
            if (number < 1 || number * every < ka) return false;
            localZ = Mathf.Clamp((number * every - ka) / (kb - ka) * chunk.chunkLength, 45f, chunk.chunkLength - 45f);
            return true;
        }

        public static SafeZone Create(Chunk chunk, RoadSpline spline, Transform parent, int number, float localZ, SeededRandom rng)
        {
            Transform st = spline.transform;
            float splineZ = st.InverseTransformPoint(chunk.transform.TransformPoint(new Vector3(0f, 0f, localZ))).z;
            Vector3 road = st.TransformPoint(spline.GetPointAtZ(splineZ));
            Vector3 fwd = chunk.transform.forward, right = chunk.transform.right;

            var root = new GameObject("SafeZone_" + number);
            root.transform.SetParent(parent, true);
            root.transform.SetPositionAndRotation(road, Quaternion.LookRotation(fwd));
            var zone = root.AddComponent<SafeZone>();
            zone.Number = number;
            zone.Center = road;
            All.Add(zone);

            // Arch over the road
            foreach (float side in new[] { -1f, 1f })
                SurvivalPrimitives.Part(PrimitiveType.Cube, root.transform, new Vector3(side * 10.5f, 3.2f, 0f), new Vector3(1f, 8.4f, 1f), Concrete);
            SurvivalPrimitives.Part(PrimitiveType.Cube, root.transform, new Vector3(0f, 7f, 0f), new Vector3(22f, 1.1f, 0.6f), Green, false);

            // Services beside the road, each on the ground of its own spot
            Vector3 Spot(float along, float side)
            {
                Vector3 p = road + fwd * along + right * side;
                return WorldPopulator.Ground(chunk, p, road, out Vector3 g) ? g : p;
            }

            SafeZoneService.Create(root.transform, Spot(-10f, 10f), Quaternion.LookRotation(-right), SafeZoneService.Kind.Fuel);
            SafeZoneService.Create(root.transform, Spot(-20f, 10f), Quaternion.LookRotation(-right), SafeZoneService.Kind.Repair);
            Trader.Create(Spot(8f, 8f), Quaternion.LookRotation(-right), root.transform, rng.Range(int.MinValue, int.MaxValue));
            // R4: one plan for sale at every stop
            PlanPickup.Create(root.transform, Spot(16f, 8f), Plans.Kind.Any, rng.Range(int.MinValue, int.MaxValue), SurvivalSettings.Instance.safeZonePlanPrice);
            // R6: whoever arrives on foot can have the vehicle they left behind brought here
            var tow = SafeZoneService.Create(root.transform, Spot(-30f, 10f), Quaternion.LookRotation(-right), SafeZoneService.Kind.Tow);
            tow.parkPosition = road - fwd * 15f + Vector3.up * 1.2f;
            tow.parkRotation = Quaternion.LookRotation(fwd);

            // A place to sit: a low wall and a barrel fire shape on the other side (plain decoration)
            SurvivalPrimitives.Part(PrimitiveType.Cube, root.transform, root.transform.InverseTransformPoint(Spot(4f, -10f)) + Vector3.up * 0.45f, new Vector3(5f, 0.9f, 0.6f), Concrete);
            SurvivalPrimitives.Part(PrimitiveType.Cylinder, root.transform, root.transform.InverseTransformPoint(Spot(4f, -12.5f)) + Vector3.up * 0.5f, new Vector3(0.8f, 0.5f, 0.8f), new Color(0.3f, 0.2f, 0.15f));

            // Nobody hostile lives here
            foreach (var enemy in chunk.GetComponentsInChildren<EndlessCombat.AI.EnemyController>(true))
                Destroy(enemy.gameObject);
            return zone;
        }

        private void OnDestroy() => All.Remove(this);
    }

    /// <summary>Fuel pump and mechanic of a Safe Zone: the vehicle parked next to it is filled / repaired for money.</summary>
    public class SafeZoneService : MonoBehaviour, IInteractable
    {
        public enum Kind
        {
            Fuel,
            Repair,
            /// <summary>R6: brings the vehicle that was left behind (out of fuel, wrecked, abandoned) to the Safe Zone.</summary>
            Tow
        }

        public Kind kind;
        [HideInInspector] public Vector3 parkPosition;
        [HideInInspector] public Quaternion parkRotation = Quaternion.identity;

        /// <summary>The vehicle counts as "here" within this distance of a service.</summary>
        private const float HereRadius = 35f;

        public static SafeZoneService Create(Transform parent, Vector3 position, Quaternion rotation, Kind kind)
        {
            var root = new GameObject(kind == Kind.Fuel ? "SafeZone_FuelPump" : kind == Kind.Repair ? "SafeZone_Mechanic" : "SafeZone_Tow");
            root.transform.SetParent(parent, true);
            root.transform.SetPositionAndRotation(position, rotation);
            if (kind == Kind.Fuel)
            {
                SurvivalPrimitives.Part(PrimitiveType.Cube, root.transform, new Vector3(0f, 0.9f, 0f), new Vector3(0.9f, 1.8f, 0.6f), new Color(0.7f, 0.15f, 0.12f));
                SurvivalPrimitives.Part(PrimitiveType.Cube, root.transform, new Vector3(0f, 0.08f, 0f), new Vector3(2.4f, 0.16f, 1.6f), new Color(0.35f, 0.35f, 0.35f));
            }
            else if (kind == Kind.Tow)
            {
                // A winch post: yellow box on a pole
                SurvivalPrimitives.Part(PrimitiveType.Cube, root.transform, new Vector3(0f, 0.7f, 0f), new Vector3(0.3f, 1.4f, 0.3f), new Color(0.35f, 0.35f, 0.35f));
                SurvivalPrimitives.Part(PrimitiveType.Cube, root.transform, new Vector3(0f, 1.5f, 0f), new Vector3(0.9f, 0.6f, 0.7f), new Color(0.85f, 0.7f, 0.1f));
            }
            else
            {
                SurvivalPrimitives.Part(PrimitiveType.Cube, root.transform, new Vector3(0f, 0.45f, 0f), new Vector3(2.6f, 0.9f, 1f), new Color(0.2f, 0.3f, 0.5f));
                SurvivalPrimitives.Part(PrimitiveType.Cube, root.transform, new Vector3(0.7f, 1.05f, 0f), new Vector3(0.6f, 0.3f, 0.4f), new Color(0.75f, 0.2f, 0.15f));
            }
            var service = root.AddComponent<SafeZoneService>();
            service.kind = kind;
            return service;
        }

        public Vector3 InteractPosition => transform.position + Vector3.up;
        // The tow post only matters while the vehicle is not here
        public bool CanInteract => kind != Kind.Tow || (SurvivalDirector.Vehicle != null && CarNear(transform.position) == null);

        private static EndlessSurvival.Vehicle.VehicleController CarNear(Vector3 p)
        {
            var car = SurvivalDirector.Vehicle;
            return car != null && (car.transform.position - p).sqrMagnitude < HereRadius * HereRadius ? car : null;
        }

        private static int TowPrice => Mathf.Max(0, SurvivalSettings.Instance.safeZoneTowPrice);

        /// <summary>
        /// The vehicle is set down at the Safe Zone with just enough fuel and body to drive to the pump and the
        /// mechanic. Its trunk, parts and mods come with it.
        /// </summary>
        private string Tow(PlayerInventory user)
        {
            var car = SurvivalDirector.Vehicle;
            if (car == null) return "Getirilecek araç yok";
            if (CarNear(transform.position) != null) return "Araç zaten burada";
            int price = TowPrice;
            if (!SurvivalTest.FreeResources && !user.SpendMoney(price)) return $"Çekici {price} para ister (tüccara eşya satabilirsin)";

            var rb = car.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.isKinematic = false;
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
                rb.position = parkPosition;
                rb.rotation = parkRotation;
            }
            car.transform.SetPositionAndRotation(parkPosition, parkRotation);
            Physics.SyncTransforms();
            var s = SurvivalSettings.Instance;
            car.currentFuel = Mathf.Max(car.currentFuel, s.safeZoneTowFuel);
            car.currentHealth = Mathf.Max(car.currentHealth, s.safeZoneTowHealth);
            return SurvivalTest.FreeResources ? "Aracın getirildi (test: bedava)" : $"Aracın getirildi (-{price} para). Depoda az yakıt var, pompaya çek.";
        }

        private float Missing(EndlessSurvival.Vehicle.VehicleController car) =>
            kind == Kind.Fuel ? car.maxFuel - car.currentFuel : car.maxHealth - car.currentHealth;

        private float UnitPrice => kind == Kind.Fuel ? SurvivalSettings.Instance.safeZoneFuelPrice : SurvivalSettings.Instance.safeZoneRepairPrice;

        public string GetPrompt()
        {
            if (kind == Kind.Tow)
            {
                var left = SurvivalDirector.Vehicle;
                float km = left != null ? Vector3.Distance(left.transform.position, transform.position) / 1000f : 0f;
                return $"Çekici: geride kalan aracını getirt ({TowPrice} para, {km:0.0} km geride)";
            }
            string what = kind == Kind.Fuel ? "Yakıt pompası" : "Tamirci";
            var car = CarNear(transform.position);
            if (car == null) return $"{what}: aracı buraya getir";
            float missing = Missing(car);
            if (missing < 1f) return kind == Kind.Fuel ? $"{what}: depo dolu" : $"{what}: araç sağlam";
            int price = Mathf.CeilToInt(missing * UnitPrice);
            return kind == Kind.Fuel ? $"{what}: depoyu doldur ({price} para)" : $"{what}: gövdeyi onar ({price} para)";
        }

        public string Interact(PlayerInventory user)
        {
            if (kind == Kind.Tow) return Tow(user);
            var car = CarNear(transform.position);
            if (car == null) return "Araç yakında değil";
            float missing = Missing(car);
            if (missing < 1f) return kind == Kind.Fuel ? "Depo zaten dolu" : "Araç zaten sağlam";

            float amount = missing;
            int price = Mathf.CeilToInt(amount * UnitPrice);
            if (!SurvivalTest.FreeResources)
            {
                if (user.Money <= 0) return "Paran yok";
                if (user.Money < price)
                {
                    // As much as the money buys
                    amount = user.Money / Mathf.Max(0.01f, UnitPrice);
                    price = user.Money;
                }
                if (!user.SpendMoney(price)) return "Paran yetmiyor";
            }

            if (kind == Kind.Fuel)
            {
                float added = car.Refuel(amount);
                return $"Depoya {Mathf.RoundToInt(added)} yakıt kondu (-{price} para)";
            }
            float restored = car.Repair(amount);
            return $"Gövde onarıldı +{Mathf.RoundToInt(restored)} (-{price} para)";
        }
    }
}
