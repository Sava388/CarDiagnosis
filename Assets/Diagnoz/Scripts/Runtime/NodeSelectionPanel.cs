using TMPro;
using UnityEngine;
using UnityEngine.UI;
namespace Diagnoz
{
    public sealed class NodeSelectionPanel : MonoBehaviour
    {
        [SerializeField] private InteractionController interaction;
        [SerializeField] private UiInputBlocker blocker;
        [SerializeField] private TMP_Text nameLabel;
        [SerializeField] private Button clearButton;
        public void Configure(InteractionController controller, UiInputBlocker inputBlocker, TMP_Text label, Button button)
        { interaction = controller; blocker = inputBlocker; nameLabel = label; clearButton = button; }
        private void OnEnable()
        {
            if (interaction == null || clearButton == null) return;
            interaction.SelectionChanged += Refresh;
            clearButton.onClick.AddListener(Clear); Refresh(interaction.Selected);
        }
        private void OnDisable()
        {
            if (interaction != null) interaction.SelectionChanged -= Refresh;
            if (clearButton != null) clearButton.onClick.RemoveListener(Clear);
        }
        private void Refresh(DiagnosticNode node)
        { nameLabel.text = node != null ? node.DisplayName : "Узел не выбран"; clearButton.interactable = node != null; }
        private void Clear() { blocker.BlockClosingClick(); interaction.ClearSelection(); }
    }
}
