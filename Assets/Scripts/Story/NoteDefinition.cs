using UnityEngine;

namespace EndlessSurvival.Story
{
    /// <summary>
    /// Represents a readable text note, letter, or document found in the world.
    /// </summary>
    [CreateAssetMenu(fileName = "New Note", menuName = "Endless Survival/Story/Note Definition")]
    public class NoteDefinition : ScriptableObject
    {
        [Tooltip("Title of the note displayed at the top.")]
        public string title = "Bilinmeyen Not";

        [Tooltip("Author or sender of the note.")]
        public string author = "Bilinmeyen Yazar";

        [Tooltip("The main content of the note.")]
        [TextArea(10, 30)]
        public string bodyText = "Not icerigi buraya yazilacak...";

        [Tooltip("Unique ID for tracking if this note was read (for the save system).")]
        public string noteId = System.Guid.NewGuid().ToString();
    }
}
