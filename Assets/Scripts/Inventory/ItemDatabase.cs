using UnityEngine;

namespace EndlessSurvival.Inventory
{
    /// <summary>
    /// Ordered list of every item in the game. UI rows are matched to this order.
    /// </summary>
    [CreateAssetMenu(fileName = "ItemDatabase", menuName = "Endless Survival/Item Database")]
    public class ItemDatabase : ScriptableObject
    {
        public ItemDefinition[] items = new ItemDefinition[0];

        public int Count => items != null ? items.Length : 0;

        public ItemDefinition Get(int index)
        {
            return items != null && index >= 0 && index < items.Length ? items[index] : null;
        }
    }
}
