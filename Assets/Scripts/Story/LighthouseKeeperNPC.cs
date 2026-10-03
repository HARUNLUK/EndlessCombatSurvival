using UnityEngine;
using EndlessSurvival.Inventory;

namespace EndlessSurvival.Story
{
    /// <summary>
    /// Friendly lighthouse keeper NPC that interacts with the player using speech bubbles and IInteractable.
    /// </summary>
    public class LighthouseKeeperNPC : MonoBehaviour, IInteractable
    {
        [Header("NPC Profile")]
        public string npcName = "Fener Bekçisi Thomas";
        public float ambientTriggerDistance = 12f;

        [Header("Dialogue Content")]
        public DialogueSet dialogueSet;
        public SpeechBubble speechBubblePrefab;

        private SpeechBubble _currentBubble;
        private int _currentLineIndex = 0;
        private bool _hasGreeted = false;
        private float _lastAmbientCheckTime = 0f;

        private readonly string[] _fallbackLines = new string[]
        {
            "Işık hiç sönmemeli... Yıllardır bu yoldan kimse geçmedi sanıyordum.",
            "Dünya sustuğunda deniz de çekildi; geriye sadece bu sonsuz sis ve asfalt kaldı.",
            "Arabana iyi bak yolcu. Bu yolda durmak ölüm demektir.",
            "Masanın üstünde eski seyir defterimi bıraktım. Neler olduğunu merak ediyorsan oku.",
            "Kilerde biraz yakıt ve temiz su var. Yolculuğunda işine yarar, çekinmeden al."
        };

        // IInteractable Implementation
        public Vector3 InteractPosition => transform.position;
        public bool CanInteract => true;

        public string GetPrompt()
        {
            return $"Konuş: {npcName}";
        }

        public string Interact(PlayerInventory user)
        {
            SpeakNextLine();
            return null;
        }

        private void Start()
        {
            if (speechBubblePrefab == null)
            {
                speechBubblePrefab = Resources.Load<SpeechBubble>("UI/SpeechBubble");
            }
        }

        private void Update()
        {
            if (!_hasGreeted && Time.time >= _lastAmbientCheckTime + 1f)
            {
                _lastAmbientCheckTime = Time.time;
                var player = GameObject.FindGameObjectWithTag("Player");
                if (player != null && Vector3.Distance(transform.position, player.transform.position) <= ambientTriggerDistance)
                {
                    _hasGreeted = true;
                    SpeakLine(0);
                }
            }
        }

        public void SpeakNextLine()
        {
            string line;
            float duration = 4.5f;

            if (dialogueSet != null && dialogueSet.lines != null && dialogueSet.lines.Length > 0)
            {
                line = dialogueSet.lines[_currentLineIndex % dialogueSet.lines.Length].text;
                duration = dialogueSet.lines[_currentLineIndex % dialogueSet.lines.Length].duration;
                _currentLineIndex++;
            }
            else
            {
                line = _fallbackLines[_currentLineIndex % _fallbackLines.Length];
                _currentLineIndex++;
            }

            DisplayBubble(line, duration);
        }

        public void SpeakLine(int index)
        {
            string line = (dialogueSet != null && dialogueSet.lines != null && index < dialogueSet.lines.Length)
                ? dialogueSet.lines[index].text
                : _fallbackLines[Mathf.Clamp(index, 0, _fallbackLines.Length - 1)];

            DisplayBubble(line, 4.5f);
        }

        private void DisplayBubble(string text, float duration)
        {
            if (speechBubblePrefab == null)
            {
                speechBubblePrefab = Resources.Load<SpeechBubble>("UI/SpeechBubble");
            }

            if (_currentBubble == null && speechBubblePrefab != null)
            {
                _currentBubble = Instantiate(speechBubblePrefab);
                _currentBubble.target = transform;
                _currentBubble.offset = new Vector3(0, 2.1f, 0);
            }

            if (_currentBubble != null)
            {
                _currentBubble.ShowText(text, duration);
            }

            Debug.Log($"<color=yellow>[{npcName}]: {text}</color>");
        }

        private void OnDestroy()
        {
            if (_currentBubble != null)
            {
                Destroy(_currentBubble.gameObject);
            }
        }
    }
}
