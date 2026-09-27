using UnityEngine;

namespace EndlessSurvival.Inventory
{
    /// <summary>
    /// Searchable container. Contents are rolled from a loot table on first use. Interacting opens the
    /// inventory screen with the chest contents listed so the player can take items one by one.
    /// </summary>
    public class LootChest : MonoBehaviour, IInteractable
    {
        public LootTable lootTable;

        [Tooltip("Tint applied to the chest once it is empty")]
        public Color emptyColor = new Color(0.25f, 0.25f, 0.25f);

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        private ItemStorage _storage;
        private bool _tinted;

        public Vector3 InteractPosition => transform.position;
        public bool CanInteract => true;

        public ItemStorage Storage
        {
            get
            {
                EnsureRolled();
                return _storage;
            }
        }

        private void Start()
        {
            EnsureRolled();
        }

        private void EnsureRolled()
        {
            if (_storage != null) return;

            _storage = new ItemStorage(0f);
            if (lootTable != null)
            {
                foreach (ItemStack stack in lootTable.Roll())
                    _storage.Add(stack.item, stack.amount);
            }

            _storage.Changed += UpdateTint;
            UpdateTint();
        }

        public string GetPrompt()
        {
            return Storage.IsEmpty ? "Open chest (empty)" : "Open chest";
        }

        public string Interact(PlayerInventory user)
        {
            InventoryUI ui = InventoryUI.Instance;
            if (ui == null) return "Inventory screen is not set up";

            ui.OpenChest(this);
            return null;
        }

        private void UpdateTint()
        {
            bool empty = _storage.IsEmpty;
            if (empty == _tinted) return;
            _tinted = empty;

            MaterialPropertyBlock block = null;
            if (empty)
            {
                block = new MaterialPropertyBlock();
                block.SetColor(BaseColorId, emptyColor);
                block.SetColor(ColorId, emptyColor);
            }

            foreach (var r in GetComponentsInChildren<Renderer>())
                r.SetPropertyBlock(block);
        }
    }
}
