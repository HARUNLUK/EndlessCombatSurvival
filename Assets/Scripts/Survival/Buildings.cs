using UnityEngine;
using EndlessSurvival.Inventory;
using EndlessSurvival.World;

namespace EndlessSurvival.Survival
{
    /// <summary>4.1: primitive lootable buildings (abandoned house, gas station). The door faces the road (+Z).</summary>
    public static class Buildings
    {
        private static readonly Color Wall = new Color(0.62f, 0.58f, 0.52f);
        private static readonly Color Roof = new Color(0.35f, 0.22f, 0.18f);
        private static readonly Color Floor = new Color(0.4f, 0.36f, 0.3f);
        private static readonly Color Metal = new Color(0.55f, 0.57f, 0.6f);
        private static readonly Color Red = new Color(0.65f, 0.15f, 0.12f);

        public static GameObject House(Vector3 position, Quaternion rotation, Transform parent, SeededRandom rng)
        {
            var root = Root(position, rotation, parent);
            Box(root, 8f, 6f, 2.8f, 1.2f, Wall, Roof, true);
            // inner wall splitting two rooms
            Part(root, new Vector3(-1.5f, 1.4f, -0.8f), new Vector3(0.15f, 2.8f, 4.4f), Wall);
            // furniture blocks
            Part(root, new Vector3(2.6f, 0.4f, -2.2f), new Vector3(1.8f, 0.8f, 0.9f), new Color(0.4f, 0.3f, 0.2f));
            Part(root, new Vector3(-3.1f, 0.45f, 1.6f), new Vector3(1.2f, 0.9f, 2f), new Color(0.3f, 0.3f, 0.35f));

            var s = SurvivalSettings.Instance;
            Chest(root, new Vector3(-3f, 0.3f, -2f), s.houseLoot, rng, position);
            if (rng.Chance(0.5f)) Chest(root, new Vector3(2.8f, 0.3f, 1.8f), s.houseLoot, rng, position);
            return root.gameObject;
        }

        public static GameObject GasStation(Vector3 position, Quaternion rotation, Transform parent, SeededRandom rng)
        {
            var root = Root(position, rotation, parent);

            // Shop at the back
            var shop = new GameObject("Shop").transform;
            shop.SetParent(root, false);
            shop.localPosition = new Vector3(0f, 0f, -6f);
            Box(shop, 7f, 5f, 3f, 1.4f, Wall, Metal, true);
            Part(shop, new Vector3(0f, 0.5f, -1.2f), new Vector3(4f, 1f, 0.8f), new Color(0.5f, 0.45f, 0.4f)); // counter

            // Canopy over the pumps
            foreach (var x in new[] { -4f, 4f })
            foreach (var z in new[] { 0.5f, 5.5f })
                Part(root, new Vector3(x, 2.25f, z), new Vector3(0.3f, 4.5f, 0.3f), Metal);
            Part(root, new Vector3(0f, 4.6f, 3f), new Vector3(10f, 0.3f, 7f), Red);
            Part(root, new Vector3(0f, 0.05f, 3f), new Vector3(10f, 0.1f, 7f), new Color(0.3f, 0.3f, 0.3f));

            float fuelMult = DifficultyCurve.FuelMultiplier(position);
            int fuel1 = Mathf.RoundToInt(rng.Range(2, 6) * fuelMult);
            int fuel2 = rng.Chance(0.5f) ? Mathf.RoundToInt(rng.Range(1, 4) * fuelMult) : 0;
            FuelPump.Create(root, new Vector3(-1.8f, 0f, 3f), fuel1);
            FuelPump.Create(root, new Vector3(1.8f, 0f, 3f), fuel2);

            var s = SurvivalSettings.Instance;
            Chest(shop, new Vector3(2.4f, 0.3f, -1.5f), s.gasStationLoot != null ? s.gasStationLoot : s.houseLoot, rng, position);
            return root.gameObject;
        }

        // ---------------- helpers ----------------

        private static Transform Root(Vector3 position, Quaternion rotation, Transform parent)
        {
            var root = new GameObject("Building").transform;
            root.SetParent(parent, true);
            root.position = position;
            root.rotation = rotation;
            return root;
        }

        /// <summary>Four walls with a door gap in the front (+Z) wall, a floor and a flat roof.</summary>
        private static void Box(Transform root, float width, float depth, float height, float door, Color wall, Color roof, bool withRoof)
        {
            const float t = 0.2f;
            float hw = width * 0.5f, hd = depth * 0.5f;
            Part(root, new Vector3(0f, 0.05f, 0f), new Vector3(width, 0.1f, depth), Floor);
            // foundation down into the ground for slopes
            Part(root, new Vector3(0f, -1.0f, 0f), new Vector3(width + 0.2f, 2f, depth + 0.2f), new Color(0.45f, 0.43f, 0.4f));
            Part(root, new Vector3(0f, height * 0.5f, -hd), new Vector3(width, height, t), wall);           // back
            Part(root, new Vector3(-hw, height * 0.5f, 0f), new Vector3(t, height, depth), wall);           // left
            Part(root, new Vector3(hw, height * 0.5f, 0f), new Vector3(t, height, depth), wall);            // right
            float side = (width - door) * 0.5f;
            Part(root, new Vector3(-(door * 0.5f + side * 0.5f), height * 0.5f, hd), new Vector3(side, height, t), wall);
            Part(root, new Vector3(door * 0.5f + side * 0.5f, height * 0.5f, hd), new Vector3(side, height, t), wall);
            Part(root, new Vector3(0f, height - 0.3f, hd), new Vector3(door, 0.6f, t), wall);              // above the door
            if (withRoof) Part(root, new Vector3(0f, height + 0.1f, 0f), new Vector3(width + 0.6f, 0.2f, depth + 0.6f), roof);
        }

        private static GameObject Part(Transform root, Vector3 local, Vector3 size, Color color)
        {
            return SurvivalPrimitives.Part(PrimitiveType.Cube, root, local, size, color);
        }

        private static void Chest(Transform root, Vector3 local, LootTable table, SeededRandom rng, Vector3 worldPos)
        {
            var s = SurvivalSettings.Instance;
            LootChest chest;
            if (s.chestPrefab != null)
            {
                chest = Object.Instantiate(s.chestPrefab, root);
                chest.transform.localPosition = local;
                chest.transform.localRotation = Quaternion.identity;
            }
            else
            {
                var go = SurvivalPrimitives.Part(PrimitiveType.Cube, root, local, new Vector3(0.9f, 0.6f, 0.6f), new Color(0.45f, 0.3f, 0.15f));
                go.name = "Chest";
                chest = go.AddComponent<LootChest>();
            }
            chest.containerTitle = "DOLAP";
            if (table == null) return;
            chest.FillFrom(table, rng);
            // 4.3: more loot further away
            int extra = DifficultyCurve.ExtraLootRolls(worldPos);
            for (int i = 0; i < extra; i++)
            {
                var stack = table.RollSingle(rng);
                if (stack.item != null && stack.amount > 0) chest.Storage.Add(stack.item, stack.amount);
            }
        }
    }

    /// <summary>4.1: gas station pump. Fills empty fuel cans, or the vehicle directly when it is parked next to it.</summary>
    public class FuelPump : MonoBehaviour, IInteractable
    {
        public int fuelUnits;

        public Vector3 InteractPosition => transform.position + Vector3.up;
        public bool CanInteract => true;

        public static FuelPump Create(Transform parent, Vector3 local, int units)
        {
            var root = new GameObject("FuelPump").transform;
            root.SetParent(parent, false);
            root.localPosition = local;
            SurvivalPrimitives.Part(PrimitiveType.Cube, root, new Vector3(0f, 0.8f, 0f), new Vector3(0.8f, 1.6f, 0.5f), new Color(0.75f, 0.2f, 0.15f));
            SurvivalPrimitives.Part(PrimitiveType.Cube, root, new Vector3(0f, 1.2f, 0.26f), new Vector3(0.5f, 0.3f, 0.02f), new Color(0.1f, 0.1f, 0.1f), false);
            SurvivalPrimitives.Part(PrimitiveType.Cylinder, root, new Vector3(0.45f, 0.9f, 0f), new Vector3(0.06f, 0.4f, 0.06f), new Color(0.1f, 0.1f, 0.1f), false);
            var pump = root.gameObject.AddComponent<FuelPump>();
            pump.fuelUnits = Mathf.Max(0, units);
            return pump;
        }

        public string GetPrompt()
        {
            if (fuelUnits <= 0) return "Pompa (boş)";
            var v = SurvivalDirector.Vehicle;
            if (v != null && Vector3.Distance(v.transform.position, transform.position) < 9f) return $"Aracın deposunu doldur ({fuelUnits} birim kaldı)";
            return $"Boş bidonu doldur ({fuelUnits} birim kaldı)";
        }

        public string Interact(PlayerInventory user)
        {
            if (fuelUnits <= 0) return "Pompada yakıt kalmamış";
            var s = SurvivalSettings.Instance;
            var v = SurvivalDirector.Vehicle;
            if (v != null && Vector3.Distance(v.transform.position, transform.position) < 9f)
            {
                float before = v.currentFuel;
                while (fuelUnits > 0 && v.currentFuel < v.maxFuel - 0.5f)
                {
                    v.currentFuel = Mathf.Min(v.maxFuel, v.currentFuel + 20f);
                    fuelUnits--;
                }
                return v.currentFuel > before ? $"Araca {Mathf.RoundToInt(v.currentFuel - before)} litre yakıt kondu" : "Aracın deposu dolu";
            }

            var empty = s.Item("Item_EmptyFuelCan");
            var full = s.Item("Item_FuelCan");
            if (empty == null || full == null || user.Storage.GetCount(empty) <= 0) return "Boş bidon yok (aracı pompaya yanaştırabilirsin)";
            user.Storage.Remove(empty, 1);
            if (user.Storage.Add(full, 1) <= 0)
            {
                user.Storage.Add(empty, 1);
                return "Çantada yer yok";
            }
            fuelUnits--;
            return "Bidon dolduruldu";
        }
    }
}
