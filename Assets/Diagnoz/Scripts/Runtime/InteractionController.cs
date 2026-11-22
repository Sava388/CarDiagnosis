using System;
using UnityEngine;
using UnityEngine.InputSystem;
namespace Diagnoz
{
    public sealed class InteractionController : MonoBehaviour
    {
        [SerializeField] private Camera sceneCamera;
        [SerializeField] private UiInputBlocker uiBlocker;
        [SerializeField] private LayerMask interactiveLayers;
        [SerializeField] private LayerMask occlusionLayers = Physics.DefaultRaycastLayers;
        public DiagnosticNode Selected { get; private set; }
        public event Action<DiagnosticNode> SelectionChanged;
        public void Configure(Camera cam, UiInputBlocker blocker, LayerMask nodes, LayerMask obstacles)
        { sceneCamera = cam; uiBlocker = blocker; interactiveLayers = nodes; occlusionLayers = obstacles; }
        private void Update()
        {
            Mouse mouse = Mouse.current;
            if (!Application.isFocused || mouse == null || sceneCamera == null || uiBlocker == null) return;
            if (!mouse.leftButton.wasPressedThisFrame || mouse.rightButton.isPressed) return;
            Vector2 pos = mouse.position.ReadValue();
            if (pos.x < 0 || pos.y < 0 || pos.x >= Screen.width || pos.y >= Screen.height) return;
            if (uiBlocker.BlocksWorldInput(pos)) return;
            Ray ray = sceneCamera.ScreenPointToRay(pos);
            int mask = interactiveLayers.value | occlusionLayers.value;
            if (Physics.Raycast(ray, out RaycastHit hit, 50f, mask, QueryTriggerInteraction.Ignore))
            {
                if ((interactiveLayers.value & (1 << hit.collider.gameObject.layer)) != 0)
                {
                    DiagnosticNode node = hit.collider.GetComponentInParent<DiagnosticNode>();
                    if (node != null && node.isActiveAndEnabled) { Select(node); return; }
                }
            }
            ClearSelection();
        }
        private void Select(DiagnosticNode node)
        {
            if (Selected == node) return;
            if (Selected != null) Selected.SetSelected(false);
            Selected = node;
            if (Selected != null) Selected.SetSelected(true);
            SelectionChanged?.Invoke(Selected);
        }
        public void ClearSelection() { Select(null); }
        private void OnDisable() { ClearSelection(); }
    }
}
