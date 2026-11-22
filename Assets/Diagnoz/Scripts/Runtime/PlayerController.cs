using UnityEngine;
using UnityEngine.InputSystem;

namespace Diagnoz
{
    // Ходьба от первого лица: WASD, обзор мышью, Shift — быстрее. Камера одна: в режиме ходьбы
    // её ставит этот компонент, в режиме осмотра — OrbitCamera (WorkshopDirector переключает).
    [RequireComponent(typeof(CharacterController))]
    public sealed class PlayerController : MonoBehaviour
    {
        [SerializeField] private Camera viewCamera;
        [SerializeField] private Transform head;
        [SerializeField] private float walkSpeed = 2.3f, runSpeed = 3.8f, lookSensitivity = .085f;
        [SerializeField] private float minPitch = -70f, maxPitch = 75f, gravity = -18f;
        private CharacterController body;
        private Vector3 spawnPosition;
        private float spawnYaw, yaw, pitch, verticalSpeed;
        private bool control;
        public bool CameraAttached { get; set; } = true;
        public Camera ViewCamera => viewCamera;

        public void Configure(Camera cam, Transform headPivot) { viewCamera = cam; head = headPivot; }

        private void Awake()
        {
            body = GetComponent<CharacterController>();
            spawnPosition = transform.position; spawnYaw = transform.eulerAngles.y;
            yaw = spawnYaw; pitch = 8f;
        }

        public void SetControl(bool value) { control = value; }

        public void ResetToSpawn()
        {
            body.enabled = false;
            transform.SetPositionAndRotation(spawnPosition, Quaternion.Euler(0, spawnYaw, 0));
            body.enabled = true;
            yaw = spawnYaw; pitch = 8f; verticalSpeed = 0;
            SnapCamera();
        }

        private void Update()
        {
            Keyboard keys = Keyboard.current;
            Mouse mouse = Mouse.current;
            Vector3 move = Vector3.zero;
            if (control && Application.isFocused && keys != null)
            {
                if (mouse != null)
                {
                    Vector2 d = mouse.delta.ReadValue();
                    yaw = Mathf.Repeat(yaw + d.x * lookSensitivity, 360f);
                    pitch = Mathf.Clamp(pitch - d.y * lookSensitivity, minPitch, maxPitch);
                }
                float x = (keys.dKey.isPressed ? 1 : 0) - (keys.aKey.isPressed ? 1 : 0);
                float z = (keys.wKey.isPressed ? 1 : 0) - (keys.sKey.isPressed ? 1 : 0);
                Vector3 input = Vector3.ClampMagnitude(new Vector3(x, 0, z), 1f);
                float speed = keys.leftShiftKey.isPressed ? runSpeed : walkSpeed;
                move = Quaternion.Euler(0, yaw, 0) * input * speed;
            }
            verticalSpeed = body.isGrounded ? -1f : verticalSpeed + gravity * Time.deltaTime;
            move.y = verticalSpeed;
            body.Move(move * Time.deltaTime);
            transform.rotation = Quaternion.Euler(0, yaw, 0);
            if (transform.position.y < -2f) ResetToSpawn();   // страховка от провала под пол
        }

        private void LateUpdate() { if (CameraAttached) SnapCamera(); }

        private void SnapCamera()
        {
            if (head == null || viewCamera == null) return;
            head.localRotation = Quaternion.Euler(pitch, 0, 0);
            viewCamera.transform.SetPositionAndRotation(head.position, head.rotation);
        }
    }
}
