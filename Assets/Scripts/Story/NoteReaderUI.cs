using UnityEngine;
using TMPro;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace EndlessSurvival.Story
{
    /// <summary>
    /// Singleton UI controller for displaying readable notes.
    /// Manages cursor state and pauses the game if needed.
    /// </summary>
    public class NoteReaderUI : MonoBehaviour
    {
        public static NoteReaderUI Instance { get; private set; }

        [Header("UI References")]
        public GameObject notePanel;
        public TextMeshProUGUI titleText;
        public TextMeshProUGUI authorText;
        public TextMeshProUGUI bodyText;

        [Header("Player Input (To disable look)")]
        [Tooltip("Optional reference to StarterAssetsInputs to disable mouse look while reading")]
        public MonoBehaviour playerInputs;

        private bool _isOpen;
        private bool _wasCursorLocked;
        private float _previousTimeScale;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            if (notePanel != null) notePanel.SetActive(false);
        }

        private void Update()
        {
            if (_isOpen && ClosePressed())
            {
                Close();
            }
        }

        private static bool ClosePressed()
        {
#if ENABLE_INPUT_SYSTEM
            return Keyboard.current != null && (Keyboard.current.escapeKey.wasPressedThisFrame || Keyboard.current.eKey.wasPressedThisFrame);
#else
            return Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.E);
#endif
        }

        public void OpenNote(NoteDefinition note)
        {
            if (note == null || notePanel == null) return;

            if (titleText != null) titleText.text = note.title;
            if (authorText != null) authorText.text = note.author;
            if (bodyText != null) bodyText.text = note.bodyText;

            // Track read note for Save system and story progression
            EndlessSurvival.Save.SaveManager.Instance?.MarkNoteAsRead(note.noteId);

            _isOpen = true;
            notePanel.SetActive(true);

            // Save state and lock
            _wasCursorLocked = Cursor.lockState == CursorLockMode.Locked;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            _previousTimeScale = Time.timeScale;
            Time.timeScale = 0f; // Pause the game while reading

            // Optional: Disable player look if using StarterAssetsInputs
            if (playerInputs != null)
            {
                var cursorProp = playerInputs.GetType().GetField("cursorInputForLook");
                var lookProp = playerInputs.GetType().GetField("look");
                
                if (cursorProp != null) cursorProp.SetValue(playerInputs, false);
                if (lookProp != null) lookProp.SetValue(playerInputs, Vector2.zero);
            }
        }

        public void Close()
        {
            if (!_isOpen) return;

            _isOpen = false;
            if (notePanel != null) notePanel.SetActive(false);

            Time.timeScale = _previousTimeScale;

            if (_wasCursorLocked)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }

            if (playerInputs != null)
            {
                var cursorProp = playerInputs.GetType().GetField("cursorInputForLook");
                if (cursorProp != null) cursorProp.SetValue(playerInputs, true);
            }
        }
    }
}
