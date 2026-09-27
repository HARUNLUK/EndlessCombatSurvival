using UnityEngine;

namespace EndlessSurvival.Inventory
{
    /// <summary>
    /// Anything the player can use with the interact key (pickups, chests).
    /// </summary>
    public interface IInteractable
    {
        Vector3 InteractPosition { get; }
        bool CanInteract { get; }
        string GetPrompt();

        /// <summary>Performs the interaction and returns a feedback message (may be null).</summary>
        string Interact(PlayerInventory user);
    }
}
