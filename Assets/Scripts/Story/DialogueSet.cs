using UnityEngine;

namespace EndlessSurvival.Story
{
    [System.Serializable]
    public struct DialogueLine
    {
        [TextArea(2, 4)]
        public string text;
        public float duration;
        public int speakerIndex; // Which enemy speaks this line (e.g. 0 for the first, 1 for the second)
    }

    [CreateAssetMenu(fileName = "New Dialogue Set", menuName = "Endless Survival/Story/Dialogue Set")]
    public class DialogueSet : ScriptableObject
    {
        public string setName;
        public DialogueLine[] lines;
        public float pauseBetweenLines = 1.5f;
    }
}
