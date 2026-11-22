using CarDiagnosis.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Diagnoz
{
    // Режимы управления: ходьба (курсор захвачен) и осмотр узлов (орбита + курсор).
    // Луч взаимодействия из центра экрана, одна клавиша E, Esc — выход из осмотра или меню.
    public sealed class WorkshopDirector : MonoBehaviour
    {
        public enum Mode { Walk, Inspect }
        [SerializeField] private BatteryOrderSession session;
        [SerializeField] private BatteryOrderUI ui;
        [SerializeField] private PlayerController player;
        [SerializeField] private OrbitCamera orbit;
        [SerializeField] private InteractionController interaction;
        [SerializeField] private UiInputBlocker blocker;
        [SerializeField] private float reach = 2.4f;
        private readonly RaycastHit[] hits = new RaycastHit[16];
        public Mode Current { get; private set; } = Mode.Walk;
        public WorkshopInteractable Focus { get; private set; }

        public void Configure(BatteryOrderSession s, BatteryOrderUI u, PlayerController p, OrbitCamera o,
            InteractionController i, UiInputBlocker b)
        { session = s; ui = u; player = p; orbit = o; interaction = i; blocker = b; }

        private void Start() { ApplyMode(Mode.Walk); }

        private void Update()
        {
            OrderFlow flow = session != null ? session.Flow : null;
            Keyboard keys = Keyboard.current;
            bool modal = blocker.ModalOpen;
            if (keys != null && keys.escapeKey.wasPressedThisFrame && flow != null)
            {
                if (modal) ui.OnEscape();
                else if (Current == Mode.Inspect) ExitInspect();
                else if (flow.Phase == FlowPhase.Order) ui.ShowPause();
                modal = blocker.ModalOpen;
            }
            bool walking = flow != null && Current == Mode.Walk && !modal && flow.Phase == FlowPhase.Order;
            player.SetControl(walking);
            ApplyCursor(walking);
            Focus = walking ? FindFocus() : null;
            ui.SetFocus(Focus);
            if (walking && Focus != null && keys != null && keys.eKey.wasPressedThisFrame) ui.Interact(Focus.Point);
        }

        private WorkshopInteractable FindFocus()
        {
            Camera cam = player.ViewCamera;
            if (cam == null) return null;
            Ray ray = cam.ViewportPointToRay(new Vector3(.5f, .5f, 0));
            int count = Physics.RaycastNonAlloc(ray, hits, reach, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide);
            System.Array.Sort(hits, 0, count, HitComparer.Instance);
            for (int i = 0; i < count; i++)
            {
                WorkshopInteractable item = hits[i].collider.GetComponent<WorkshopInteractable>();
                if (item != null) return item;
                if (!hits[i].collider.isTrigger) return null;   // стена или кузов закрывают точку
            }
            return null;
        }

        private sealed class HitComparer : System.Collections.Generic.IComparer<RaycastHit>
        {
            public static readonly HitComparer Instance = new HitComparer();
            public int Compare(RaycastHit a, RaycastHit b) => a.distance.CompareTo(b.distance);
        }

        public void EnterInspect() { ApplyMode(Mode.Inspect); }
        public void ExitInspect() { ApplyMode(Mode.Walk); }

        public void ResetForNewShift()
        {
            ApplyMode(Mode.Walk);
            player.ResetToSpawn();
        }

        private void ApplyMode(Mode mode)
        {
            Current = mode;
            bool inspect = mode == Mode.Inspect;
            if (inspect) orbit.ResetView();
            orbit.enabled = inspect;
            player.CameraAttached = !inspect;
            if (!inspect) interaction.ClearSelection();
            interaction.enabled = inspect;
            ui.SetInspectMode(inspect);
        }

        private static void ApplyCursor(bool locked)
        {
            if (!Application.isFocused) return;
            CursorLockMode want = locked ? CursorLockMode.Locked : CursorLockMode.None;
            if (Cursor.lockState != want) Cursor.lockState = want;
            Cursor.visible = !locked;
        }

        private void OnDisable() { Cursor.lockState = CursorLockMode.None; Cursor.visible = true; }
    }
}
