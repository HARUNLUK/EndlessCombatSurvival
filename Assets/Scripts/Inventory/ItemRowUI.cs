using UnityEngine;
using UnityEngine.UI;

namespace EndlessSurvival.Inventory
{
    /// <summary>
    /// One pre-built row in the inventory panel: a label and a fixed set of buttons.
    /// Rows are defined in the scene hierarchy; InventoryUI only fills them in.
    /// A button is hidden when its text is null or empty.
    /// </summary>
    public class ItemRowUI : MonoBehaviour
    {
        public struct RowButton
        {
            public string text;
            public bool enabled;

            public RowButton(string text, bool enabled)
            {
                this.text = text;
                this.enabled = enabled;
            }
        }

        public Text label;
        public Button[] buttons;
        public Text[] buttonLabels;

        public void Hide()
        {
            gameObject.SetActive(false);
        }

        /// <summary>
        /// Rows built by an older setup have no button list. In that case the existing child buttons are
        /// collected in hierarchy order, so the row still works without rebuilding the UI.
        /// </summary>
        public void EnsureButtons()
        {
            if (buttons != null && buttons.Length > 0) return;

            buttons = GetComponentsInChildren<Button>(true);
            buttonLabels = new Text[buttons.Length];
            for (int i = 0; i < buttons.Length; i++)
                buttonLabels[i] = buttons[i].GetComponentInChildren<Text>(true);

            if (label == null)
            {
                foreach (var text in GetComponentsInChildren<Text>(true))
                {
                    bool isButtonLabel = System.Array.IndexOf(buttonLabels, text) >= 0;
                    if (!isButtonLabel) { label = text; break; }
                }
            }
        }

        public void Show(string text, params RowButton[] specs)
        {
            gameObject.SetActive(true);
            EnsureButtons();

            if (label != null) label.text = text;
            if (buttons == null) return;

            for (int i = 0; i < buttons.Length; i++)
            {
                if (buttons[i] == null) continue;

                bool has = specs != null && i < specs.Length && !string.IsNullOrEmpty(specs[i].text);
                buttons[i].gameObject.SetActive(has);
                if (!has) continue;

                buttons[i].interactable = specs[i].enabled;
                if (buttonLabels != null && i < buttonLabels.Length && buttonLabels[i] != null)
                    buttonLabels[i].text = specs[i].text;
            }
        }
    }
}
