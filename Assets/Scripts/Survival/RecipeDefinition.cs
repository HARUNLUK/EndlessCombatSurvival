using System;
using UnityEngine;
using EndlessSurvival.Inventory;

namespace EndlessSurvival.Survival
{
    public enum CraftStation
    {
        Hand,
        Campfire,
        Vehicle,
        Distiller
    }

    public enum RecipeEffect
    {
        None,
        CleanWound,
        RepairClothing,
        RepairVehicle,
        PlaceDistiller,
        PlaceRabbitTrap
    }

    [Serializable]
    public class RecipeIngredient
    {
        public ItemDefinition item;
        public int amount = 1;

        public RecipeIngredient() { }

        public RecipeIngredient(ItemDefinition item, int amount)
        {
            this.item = item;
            this.amount = amount;
        }
    }

    /// <summary>2.1: one craft recipe. Inputs are consumed, tools only need to be carried.</summary>
    [CreateAssetMenu(fileName = "Recipe_New", menuName = "Endless Survival/Recipe")]
    public class RecipeDefinition : ScriptableObject
    {
        public string displayName = "Yeni tarif";
        [TextArea] public string description;
        public CraftStation station = CraftStation.Hand;
        public RecipeIngredient[] inputs = new RecipeIngredient[0];
        [Tooltip("Needed but not consumed (knife, tool kit, hose)")]
        public ItemDefinition[] tools = new ItemDefinition[0];
        public RecipeIngredient[] outputs = new RecipeIngredient[0];
        [Tooltip("Real seconds (Mechanics makes it faster)")]
        public float craftSeconds = 2f;
        public RecipeEffect effect = RecipeEffect.None;
        [Tooltip("Stat trained by this recipe (Nature for food work, Mechanics for building)")]
        public StatType trainsStat = StatType.Mechanics;
        [Tooltip("Nature stat may give one extra output")]
        public bool natureBonusOutput;

        public string DisplayName => string.IsNullOrEmpty(displayName) ? name : displayName;

        public static string StationName(CraftStation s)
        {
            switch (s)
            {
                case CraftStation.Hand: return "Elde";
                case CraftStation.Campfire: return "Kamp ateşi";
                case CraftStation.Vehicle: return "Araç";
                case CraftStation.Distiller: return "Damıtıcı";
            }
            return s.ToString();
        }
    }
}
