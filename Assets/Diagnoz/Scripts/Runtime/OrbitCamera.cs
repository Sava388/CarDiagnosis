using UnityEngine;
using UnityEngine.InputSystem;
namespace Diagnoz
{
    [RequireComponent(typeof(Camera))]
    public sealed class OrbitCamera : MonoBehaviour
    {
        [SerializeField] private Transform target;
        [SerializeField] private UiInputBlocker uiBlocker;
        [SerializeField] private float minDistance = 2.5f, maxDistance = 7f;
        [SerializeField] private float minPitch = 12f, maxPitch = 65f;
        [SerializeField] private float sensitivity = .18f, zoomStep = .35f;
        [SerializeField] private float startDistance = 5.2f, startYaw = 36f, startPitch = 32f;
        private float distance, yaw, pitch;
        private bool dragging;
        public void Configure(Transform orbitTarget, UiInputBlocker blocker)
        { target = orbitTarget; uiBlocker = blocker; ResetView(); }
        // Ограничения орбиты для компактной мастерской (камера не уходит за стены).
        public void SetLimits(float min, float max, float start, float yawStart, float pitchStart)
        { minDistance = min; maxDistance = max; startDistance = start; startYaw = yawStart; startPitch = pitchStart; ResetView(); }
        private void Awake() { ResetView(); }
        public void ResetView()
        { distance = startDistance; yaw = startYaw; pitch = startPitch; dragging = false; ApplyPose(); }
        private void LateUpdate()
        {
            Mouse mouse = Mouse.current;
            if (!Application.isFocused || mouse == null || target == null || uiBlocker == null)
            { dragging = false; return; }
            Vector2 pos = mouse.position.ReadValue();
            if (pos.x < 0 || pos.y < 0 || pos.x >= Screen.width || pos.y >= Screen.height)
            { dragging = false; return; }
            bool blocked = uiBlocker.BlocksWorldInput(pos);
            if (!mouse.rightButton.isPressed || blocked) dragging = false;
            if (mouse.rightButton.wasPressedThisFrame && !blocked) dragging = true;
            if (dragging)
            {
                Vector2 delta = mouse.delta.ReadValue();
                yaw = Mathf.Repeat(yaw + delta.x * sensitivity, 360f);
                pitch = Mathf.Clamp(pitch - delta.y * sensitivity, minPitch, maxPitch);
            }
            float scroll = mouse.scroll.ReadValue().y;
            if (!blocked && Mathf.Abs(scroll) > .01f)
                distance = Mathf.Clamp(distance - Mathf.Sign(scroll) * zoomStep, minDistance, maxDistance);
            ApplyPose();
        }
        private void ApplyPose()
        {
            if (target == null) return;
            distance = Mathf.Clamp(distance, minDistance, maxDistance);
            pitch = Mathf.Clamp(pitch, minPitch, maxPitch);
            Quaternion rotation = Quaternion.Euler(pitch, yaw, 0);
            transform.SetPositionAndRotation(target.position + rotation * (Vector3.back * distance), rotation);
        }
        private void OnApplicationFocus(bool focused) { if (!focused) dragging = false; }
        private void OnDisable() { dragging = false; }
    }
}
