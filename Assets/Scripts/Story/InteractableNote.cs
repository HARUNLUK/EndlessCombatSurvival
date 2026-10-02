using UnityEngine;
using EndlessSurvival.Inventory;

namespace EndlessSurvival.Story
{
    /// <summary>
    /// Placed in the world (e.g., on a table, wall). When interacted with, opens the NoteReaderUI.
    /// </summary>
    public class InteractableNote : MonoBehaviour, IInteractable
    {
        [Tooltip("The note content to display when interacted with.")]
        public NoteDefinition noteDefinition;

        public Vector3 InteractPosition => transform.position;

        public bool CanInteract => noteDefinition != null;

        public string GetPrompt()
        {
            if (noteDefinition == null) return "Bos Kagit";
            return $"Oku: {noteDefinition.title}";
        }

        public string Interact(PlayerInventory user)
        {
            if (noteDefinition == null) return "Not okunamiyor.";

            if (NoteReaderUI.Instance != null)
            {
                NoteReaderUI.Instance.OpenNote(noteDefinition);
                return null; // Basarili, mesaj gostermeye gerek yok
            }
            return "UI bulunamadi!";
        }
    }
}
