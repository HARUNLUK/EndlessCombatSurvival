using UnityEngine;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace EndlessSurvival.Inventory
{
    /// <summary>
    /// Finds the closest interactable near the player, shows a prompt and uses it when the interact key (E) is pressed.
    /// </summary>
    public class PlayerInteractor : MonoBehaviour
    {
        [Header("References")]
        public PlayerInventory inventory;

        [Header("Settings")]
        public float interactRadius = 2.5f;
        public float scanInterval = 0.1f;
        public float feedbackSeconds = 2.5f;

        [Header("Hierarchy UI")]
        [Tooltip("Root object of the interact prompt (shown only when something is in reach)")]
        public GameObject promptRoot;
        public Text promptText;
        public Text feedbackText;

        private readonly Collider[] _buffer = new Collider[48];
        private IInteractable _current;
        private float _nextScan;
        private float _feedbackTimer;

        private void Awake()
        {
            if (inventory == null) inventory = GetComponent<PlayerInventory>();
        }

        private void OnDisable()
        {
            _current = null;
            SetPrompt(null);
        }

        private void Update()
        {
            if (_feedbackTimer > 0f)
            {
                _feedbackTimer -= Time.deltaTime;
                if (_feedbackTimer <= 0f && feedbackText != null) feedbackText.text = string.Empty;
            }

            if (InventoryUI.IsOpen)
            {
                SetPrompt(null);
                return;
            }

            if (Time.time >= _nextScan)
            {
                _nextScan = Time.time + scanInterval;
                _current = FindClosest();
                SetPrompt(_current != null ? $"[E] {_current.GetPrompt()}" : null);
            }

            if (_current != null && InteractPressed())
            {
                string message = _current.Interact(inventory);
                ShowFeedback(message);
                _nextScan = 0f;
            }
        }

        private IInteractable FindClosest()
        {
            int count = Physics.OverlapSphereNonAlloc(transform.position, interactRadius, _buffer, ~0, QueryTriggerInteraction.Collide);

            IInteractable best = null;
            float bestDistance = float.MaxValue;

            for (int i = 0; i < count; i++)
            {
                IInteractable candidate = _buffer[i].GetComponentInParent<IInteractable>();
                if (candidate == null || !candidate.CanInteract) continue;

                float distance = (candidate.InteractPosition - transform.position).sqrMagnitude;
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = candidate;
                }
            }

            return best;
        }

        private static bool InteractPressed()
        {
#if ENABLE_INPUT_SYSTEM
            return Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame;
#else
            return Input.GetKeyDown(KeyCode.E);
#endif
        }

        private void SetPrompt(string text)
        {
            if (promptRoot != null) promptRoot.SetActive(!string.IsNullOrEmpty(text));
            if (promptText != null) promptText.text = text ?? string.Empty;
        }

        public void ShowFeedback(string message)
        {
            if (feedbackText == null || string.IsNullOrEmpty(message)) return;
            feedbackText.text = message;
            _feedbackTimer = feedbackSeconds;
        }
    }
}
