using UnityEngine;
using UnityEngine.InputSystem;
using WargameRTS.Map;

namespace WargameRTS.CameraRig
{
    /// <summary>
    /// Wargame-style camera: WASD/arrows/screen edges to pan, wheel to zoom,
    /// MMB drag or Q/E to rotate. Tilts towards top-down when zoomed out.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public sealed class RtsCameraController : MonoBehaviour
    {
        [Header("Pan")]
        [SerializeField] float panSpeedPerDistance = 1.1f;
        [SerializeField] bool edgePan = true;
        [SerializeField] float edgeSizePixels = 6f;

        [Header("Zoom")]
        [SerializeField] float minDistance = 30f;
        [SerializeField] float maxDistance = 1800f;
        [SerializeField] float zoomStep = 0.18f;
        [SerializeField] float zoomSmoothing = 10f;

        [Header("Rotation")]
        [SerializeField] float minPitch = 28f;
        [SerializeField] float maxPitch = 72f;
        [SerializeField] float mouseRotateSpeed = 0.25f;
        [SerializeField] float keyRotateSpeed = 90f;

        Vector3 focus;
        float yaw;
        float pitchOffset;
        float distance = 350f;
        float targetDistance = 350f;

        public static RtsCameraController Instance { get; private set; }

        void Awake() => Instance = this;

        void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        public void FocusOn(Vector3 point) => focus = point;

        public void SetView(Vector3 point, float yawDegrees, float zoomDistance)
        {
            focus = point;
            yaw = yawDegrees;
            distance = targetDistance = Mathf.Clamp(zoomDistance, minDistance, maxDistance);
            Apply();
        }

        void LateUpdate()
        {
            var keyboard = Keyboard.current;
            var mouse = Mouse.current;
            float deltaTime = Time.unscaledDeltaTime;

            if (keyboard != null && mouse != null)
            {
                HandlePan(keyboard, mouse, deltaTime);
                HandleRotation(keyboard, mouse, deltaTime);
                HandleZoom(mouse);
            }

            distance = Mathf.Lerp(distance, targetDistance, 1f - Mathf.Exp(-zoomSmoothing * deltaTime));
            Apply();
        }

        void HandlePan(Keyboard keyboard, Mouse mouse, float deltaTime)
        {
            Vector2 input = Vector2.zero;
            if (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed) input.y += 1f;
            if (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed) input.y -= 1f;
            if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed) input.x += 1f;
            if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed) input.x -= 1f;

            if (edgePan && Application.isFocused && !mouse.middleButton.isPressed)
            {
                Vector2 pointer = mouse.position.ReadValue();
                bool inside = pointer.x >= 0f && pointer.y >= 0f && pointer.x <= Screen.width && pointer.y <= Screen.height;
                if (inside)
                {
                    if (pointer.x <= edgeSizePixels) input.x -= 1f;
                    if (pointer.x >= Screen.width - edgeSizePixels) input.x += 1f;
                    if (pointer.y <= edgeSizePixels) input.y -= 1f;
                    if (pointer.y >= Screen.height - edgeSizePixels) input.y += 1f;
                }
            }

            if (input.sqrMagnitude < 0.01f)
                return;

            input = Vector2.ClampMagnitude(input, 1f);
            float speed = distance * panSpeedPerDistance * (keyboard.shiftKey.isPressed ? 2f : 1f);
            var rotation = Quaternion.Euler(0f, yaw, 0f);
            focus += rotation * new Vector3(input.x, 0f, input.y) * speed * deltaTime;
        }

        void HandleRotation(Keyboard keyboard, Mouse mouse, float deltaTime)
        {
            if (keyboard.qKey.isPressed) yaw += keyRotateSpeed * deltaTime;
            if (keyboard.eKey.isPressed) yaw -= keyRotateSpeed * deltaTime;

            if (mouse.middleButton.isPressed)
            {
                Vector2 delta = mouse.delta.ReadValue();
                yaw += delta.x * mouseRotateSpeed;
                pitchOffset = Mathf.Clamp(pitchOffset - delta.y * mouseRotateSpeed, -20f, 20f);
            }
        }

        void HandleZoom(Mouse mouse)
        {
            float scroll = mouse.scroll.ReadValue().y;
            if (Mathf.Abs(scroll) < 0.01f)
                return;

            // Scroll units differ between platforms; only the direction matters.
            float factor = scroll > 0f ? 1f - zoomStep : 1f + zoomStep;
            targetDistance = Mathf.Clamp(targetDistance * factor, minDistance, maxDistance);
        }

        void Apply()
        {
            var map = GameMap.Instance;
            if (map != null)
            {
                focus = map.Clamp(focus, 0f);
                focus.y = map.HeightAt(focus.x, focus.z);
            }

            float zoom01 = Mathf.InverseLerp(minDistance, maxDistance, distance);
            float pitch = Mathf.Clamp(Mathf.Lerp(minPitch, maxPitch, Mathf.Sqrt(zoom01)) + pitchOffset, 10f, 89f);
            var rotation = Quaternion.Euler(pitch, yaw, 0f);
            Vector3 position = focus - rotation * Vector3.forward * distance;

            if (map != null)
            {
                float minHeight = map.HeightAt(position.x, position.z) + 4f;
                if (position.y < minHeight)
                    position.y = minHeight;
            }

            transform.SetPositionAndRotation(position, rotation);
        }
    }
}
