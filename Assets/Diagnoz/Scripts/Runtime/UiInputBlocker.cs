using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
namespace Diagnoz
{
    public sealed class UiInputBlocker : MonoBehaviour
    {
        [SerializeField] private GraphicRaycaster raycaster;
        [SerializeField] private EventSystem eventSystem;
        private readonly List<RaycastResult> hits = new List<RaycastResult>(32);
        private PointerEventData pointer;
        private int blockedThroughFrame = -1;
        public bool ModalOpen { get; private set; }
        public void Configure(GraphicRaycaster value, EventSystem events)
        { raycaster = value; eventSystem = events; pointer = null; }
        public void SetModal(bool value) { ModalOpen = value; BlockClosingClick(); }
        public void BlockClosingClick() { blockedThroughFrame = Time.frameCount + 1; }
        public bool BlocksWorldInput(Vector2 position)
        {
            if (ModalOpen || Time.frameCount <= blockedThroughFrame) return true;
            if (raycaster == null || eventSystem == null) return true;
            if (pointer == null) pointer = new PointerEventData(eventSystem);
            pointer.Reset(); pointer.position = position; hits.Clear();
            raycaster.Raycast(pointer, hits);
            return hits.Count > 0;
        }
    }
}
