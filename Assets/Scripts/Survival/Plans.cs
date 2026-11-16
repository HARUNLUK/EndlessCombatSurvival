using System.Collections.Generic;
using UnityEngine;
using EndlessSurvival.Inventory;
using EndlessSurvival.Save;
using EndlessSurvival.Vehicle;

namespace EndlessSurvival.Survival
{
    /// <summary>
    /// R4: knowledge the character keeps for good. The advanced recipes and most vehicle mods cannot be made until
    /// their plan has been learned; a learned plan stays on the character profile through death (the materials
    /// still have to be found on every journey). Plans lie in particular places: recipe plans in abandoned houses,
    /// vehicle plans in gas stations and enemy camps, and one of either kind is sold at every Safe Zone.
    /// Without a selected character (automated tests) and in test mode (FreeResources) everything counts as known.
    /// </summary>
    public static class Plans
    {
        public enum Kind
        {
            Recipe,
            VehicleMod,
            Any
        }

        /// <summary>Recipe asset names that need a plan. Everything else is known from the start.</summary>
        public static readonly string[] RecipePlans =
        {
            "Recipe_FishingRod", "Recipe_RabbitTrap", "Recipe_SmokeMeat", "Recipe_Ointment",
            "Recipe_RepairKit", "Recipe_LeatherJacket", "Recipe_Distiller",
        };

        /// <summary>Vehicle mods that need a plan. The simple ones (rope, spare tire mount, blanket, battery wrap, drying line) do not.</summary>
        public static readonly VehicleModType[] ModPlans =
        {
            VehicleModType.RoofLightbar, VehicleModType.RadiatorGrille, VehicleModType.WaterTank, VehicleModType.RainTarp, VehicleModType.DewCollector,
            VehicleModType.SnowChains, VehicleModType.ExternalFuelTanks, VehicleModType.OffroadTires, VehicleModType.Bullbar,
            VehicleModType.WindowCage, VehicleModType.DoorPlates, VehicleModType.RoofRack,
        };

        public static int Total => RecipePlans.Length + ModPlans.Length;

        private static bool EverythingKnown => CharacterProfiles.Current == null || SurvivalTest.FreeResources;

        private static string Id(RecipeDefinition r) => "recipe:" + r.name;
        private static string Id(VehicleModType m) => "mod:" + m;

        public static bool NeedsPlan(RecipeDefinition r) => r != null && System.Array.IndexOf(RecipePlans, r.name) >= 0;
        public static bool NeedsPlan(VehicleModType m) => System.Array.IndexOf(ModPlans, m) >= 0;

        public static bool Knows(RecipeDefinition r) => r == null || EverythingKnown || !NeedsPlan(r) || CharacterProfiles.Current.knownPlans.Contains(Id(r));
        public static bool Knows(VehicleModType m) => EverythingKnown || !NeedsPlan(m) || CharacterProfiles.Current.knownPlans.Contains(Id(m));

        /// <summary>Plans the active character has learned (0 without a character).</summary>
        public static int KnownCount => CharacterProfiles.Current != null ? CharacterProfiles.Current.knownPlans.Count : 0;

        /// <summary>Plans of a kind the active character does not know yet, as (id, shown name).</summary>
        public static List<(string id, string name)> Unknown(Kind kind)
        {
            var list = new List<(string, string)>();
            var p = CharacterProfiles.Current;
            if (p == null) return list;
            if (kind != Kind.VehicleMod)
            {
                foreach (var r in SurvivalSettings.Instance.recipes)
                    if (r != null && NeedsPlan(r) && !p.knownPlans.Contains(Id(r))) list.Add((Id(r), r.DisplayName));
            }
            if (kind != Kind.Recipe)
            {
                var mods = VehicleModifications.Instance;
                foreach (var m in ModPlans)
                {
                    if (p.knownPlans.Contains(Id(m))) continue;
                    var item = mods != null ? mods.GetModItem(m) : null;
                    list.Add((Id(m), item != null && !string.IsNullOrEmpty(item.displayName) ? item.displayName : m.ToString()));
                }
            }
            return list;
        }

        /// <summary>Which plan a place holds: the same place offers the same plan until it is learned.</summary>
        public static bool Pick(Kind kind, int seed, out string id, out string name)
        {
            id = name = null;
            var open = Unknown(kind);
            if (open.Count == 0) return false;
            var pick = open[(int)((uint)seed % (uint)open.Count)];
            id = pick.id;
            name = pick.name;
            return true;
        }

        public static bool Learn(string id, string name)
        {
            var p = CharacterProfiles.Current;
            if (p == null || string.IsNullOrEmpty(id) || p.knownPlans.Contains(id)) return false;
            p.knownPlans.Add(id);
            CharacterProfiles.SaveCurrent();
            SurvivalNotify.Show($"PLAN ÖĞRENİLDİ: {name}  ({p.knownPlans.Count} / {Total})", new Color(0.55f, 0.85f, 1f), 8f);
            SurvivalNotify.Show("Öğrendiğin planlar karakterinde kalır; ölsen de unutulmaz.", new Color(0.55f, 0.85f, 1f), 8f);
            return true;
        }
    }

    /// <summary>
    /// A plan lying in a place (a blue folder on a small crate) or sold at a Safe Zone (price above 0).
    /// Learning it is the place's own reward. Hidden for a character that already knows every plan of its kind.
    /// </summary>
    public class PlanPickup : MonoBehaviour, IInteractable
    {
        public Plans.Kind kind = Plans.Kind.Any;
        public int seed;
        public int price;

        private bool _taken;

        public static PlanPickup Create(Transform parent, Vector3 position, Plans.Kind kind, int seed, int price = 0)
        {
            var root = new GameObject(price > 0 ? "PlanSeller" : "PlanPickup");
            root.transform.SetParent(parent, true);
            root.transform.position = position;
            SurvivalPrimitives.Part(PrimitiveType.Cube, root.transform, new Vector3(0f, 0.3f, 0f), new Vector3(0.7f, 0.6f, 0.5f), new Color(0.35f, 0.28f, 0.2f));
            var folder = SurvivalPrimitives.Part(PrimitiveType.Cube, root.transform, new Vector3(0f, 0.65f, 0f), new Vector3(0.45f, 0.06f, 0.32f), new Color(0.2f, 0.5f, 0.9f));
            folder.transform.localRotation = Quaternion.Euler(0f, 20f, 0f);
            var pickup = root.AddComponent<PlanPickup>();
            pickup.kind = kind;
            pickup.seed = seed;
            pickup.price = price;
            return pickup;
        }

        public Vector3 InteractPosition => transform.position + Vector3.up * 0.6f;
        public bool CanInteract => !_taken && CharacterProfiles.Current != null && Plans.Unknown(kind).Count > 0;

        public string GetPrompt()
        {
            if (!Plans.Pick(kind, seed, out _, out string name)) return "Plan: hepsini biliyorsun";
            return price > 0 ? $"Plan satın al: {name} ({price} para)" : $"Planı incele ve öğren: {name}";
        }

        public string Interact(PlayerInventory user)
        {
            if (_taken || !Plans.Pick(kind, seed, out string id, out string name)) return null;
            if (price > 0 && !SurvivalTest.FreeResources && !user.SpendMoney(price)) return $"Bu plan {price} para (tüccara eşya satabilirsin)";
            if (!Plans.Learn(id, name)) return null;
            _taken = true;
            foreach (var r in GetComponentsInChildren<Renderer>()) if (r.transform.localPosition.y > 0.5f) r.enabled = false; // the folder is gone
            return $"Öğrenildi: {name}";
        }
    }
}
