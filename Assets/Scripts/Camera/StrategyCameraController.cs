using UnityEngine;
using UnityEngine.EventSystems;

namespace StellarisClone.Cam
{
    public class StrategyCameraController : MonoBehaviour
    {
        [Header("Панорама")]
        [SerializeField] private float panSpeed = 60f;
        [SerializeField] private float fastPanMultiplier = 2.5f;
        [SerializeField] private float panZoomFactor = 0.12f;
        [SerializeField] private bool edgeScrolling = false;
        [SerializeField] private float edgeScrollThreshold = 8f;
        [SerializeField] private float middleMouseDragSensitivity = 1.2f;

        [Header("Зум")]
        [SerializeField] private float zoomSpeed = 25f;
        [SerializeField] private float zoomSmooth = 12f;
        [SerializeField] private float minHeight = 18f;
        [SerializeField] private float maxHeight = 240f;
        [SerializeField] private float zoomToCursorStrength = 0.85f;

        [Header("Вращение")]
        [SerializeField] private float rotationSpeed = 90f;

        [Header("Границы")]
        [SerializeField] private bool limitToBounds = true;
        [SerializeField] private float galaxyBoundsRadius = 180f;

        [Header("Фокус камеры")]
        [SerializeField] private float focusDuration = 0.5f;

        [Header("Режим системы")]
        [SerializeField] private float systemZoomSpeed = 35f;
        [SerializeField] private float minSystemDistance = 20f;
        [SerializeField] private float maxSystemDistance = 140f;
        [SerializeField] private float systemTransitionSpeed = 6f;
        [SerializeField] private float returnFromSystemDuration = 0.45f;

        public bool IsSystemMode { get; private set; }
        public bool IsTransitioning { get; private set; }
        public bool IsReturningFromSystem { get; private set; }

        // ---- Системный режим ----
        private Vector3 _systemCenter;
        private float _systemDistance = 65f;
        private const float FixedSystemPitch = 55f;
        private const float FixedSystemYaw = 0f;

        private Vector3 _preSystemPosition;
        private Quaternion _preSystemRotation;

        // ---- Возврат из системы ----
        private Vector3 _returnTargetPos;
        private Quaternion _returnTargetRot;
        private float _returnTimer;

        // ---- Плавный зум ----
        private float _targetHeight;
        private bool _isDraggingMiddle;
        private Vector3 _dragOrigin;

        // ---- Фокус ----
        private Vector3 _focusStartPos;
        private Vector3 _focusTargetPos;
        private float _focusTimer;
        private bool _isFocusing;

        /// <summary>Настройки игрока (меню «Настройки»).</summary>
        public static bool EdgeScrollingSetting = false;
        public static float PanSpeedSetting = 1f;

        private void Awake()
        {
            _targetHeight = transform.position.y;
        }

        /// <summary>Положение камеры в режиме галактики (для сохранения).</summary>
        public bool TryGetGalaxyPose(out Vector3 pos, out Quaternion rot)
        {
            if (IsSystemMode || IsReturningFromSystem)
            {
                pos = IsReturningFromSystem ? _returnTargetPos : _preSystemPosition;
                rot = IsReturningFromSystem ? _returnTargetRot : _preSystemRotation;
            }
            else
            {
                pos = _isFocusing ? _focusTargetPos : transform.position;
                rot = transform.rotation;
            }
            return pos.y > 1f;
        }

        public void Teleport(Vector3 pos, Quaternion rot)
        {
            IsSystemMode = false;
            IsReturningFromSystem = false;
            IsTransitioning = false;
            _isFocusing = false;
            transform.SetPositionAndRotation(ClampToBounds(pos), rot);
            _targetHeight = Mathf.Clamp(pos.y, minHeight, maxHeight);
        }

        private void Update()
        {
            // 1. Возврат из системы (высший приоритет, блокирует всё)
            if (IsReturningFromSystem)
            {
                UpdateReturnToGalaxy();
                return;
            }

            // 2. Режим системы
            if (IsSystemMode)
            {
                HandleSystemZoomOnly();
                return;
            }

            // 3. Фокус (плавный полёт к точке)
            if (_isFocusing)
            {
                UpdateFocus();
                return;
            }

            if (IsTransitioning) return;

            // 4. Обычный режим галактики
            HandleGalaxyMovement();
            HandleGalaxyZoomToCursor();
            HandleGalaxyRotation();
            HandleDrag();
            HandleShortcuts();
        }

        // ==================== РЕЖИМ СИСТЕМЫ ====================

        public void EnterSystemMode(Vector3 starPosition)
        {
            if (IsSystemMode) return;
            _preSystemPosition = transform.position;
            _preSystemRotation = transform.rotation;

            IsSystemMode = true;
            IsReturningFromSystem = false;
            _isFocusing = false;
            _systemCenter = starPosition;
            _systemDistance = Mathf.Clamp(_systemDistance, minSystemDistance, maxSystemDistance);
            UpdateSystemCameraTransform();
        }

        public void ExitSystemMode()
        {
            IsSystemMode = false;
        }

        public void ReturnToGalaxyView(Vector3 targetPos, Quaternion targetRot)
        {
            IsSystemMode = false;
            _isFocusing = false;
            IsReturningFromSystem = true;
            _returnTargetPos = targetPos;
            _returnTargetRot = targetRot;
            _returnTimer = 0f;
            _targetHeight = targetPos.y;
        }

        private void UpdateReturnToGalaxy()
        {
            _returnTimer += Time.deltaTime;
            float t = Mathf.Clamp01(_returnTimer / returnFromSystemDuration);
            float smooth = Mathf.SmoothStep(0f, 1f, t);

            transform.position = Vector3.Lerp(transform.position, _returnTargetPos, smooth);
            transform.rotation = Quaternion.Slerp(transform.rotation, _returnTargetRot, smooth);

            if (t >= 1f)
            {
                transform.position = _returnTargetPos;
                transform.rotation = _returnTargetRot;
                _targetHeight = _returnTargetPos.y;
                IsReturningFromSystem = false;
            }
        }

        private void HandleSystemZoomOnly()
        {
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
                return;

            float scroll = Input.GetAxis("Mouse ScrollWheel");
            if (Mathf.Abs(scroll) > 0.01f)
                _systemDistance -= scroll * systemZoomSpeed * 10f;

            if (Input.GetKey(KeyCode.R)) _systemDistance -= systemZoomSpeed * Time.deltaTime;
            if (Input.GetKey(KeyCode.F)) _systemDistance += systemZoomSpeed * Time.deltaTime;

            _systemDistance = Mathf.Clamp(_systemDistance, minSystemDistance, maxSystemDistance);
            UpdateSystemCameraTransform();
        }

        private void UpdateSystemCameraTransform()
        {
            Quaternion rot = Quaternion.Euler(FixedSystemPitch, FixedSystemYaw, 0f);
            Vector3 offset = rot * new Vector3(0, 0, -_systemDistance);
            Vector3 target = _systemCenter + offset;

            float t = 1f - Mathf.Exp(-systemTransitionSpeed * Time.deltaTime);
            transform.position = Vector3.Lerp(transform.position, target, t);
            transform.rotation = Quaternion.Slerp(transform.rotation, rot, t);
        }

        // ==================== РЕЖИМ ГАЛАКТИКИ ====================

        private void HandleGalaxyMovement()
        {
            float speedMult = Input.GetKey(KeyCode.LeftShift) ? fastPanMultiplier : 1f;
            float zoomMult = 1f + (transform.position.y - minHeight) * panZoomFactor * 0.01f;
            float speed = panSpeed * PanSpeedSetting * speedMult * zoomMult;

            float h = Input.GetAxisRaw("Horizontal");
            float v = Input.GetAxisRaw("Vertical");

            // Граничный скролл активен только если мышь не находится над интерфейсом
            bool isOverUI = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();

            if ((edgeScrolling || EdgeScrollingSetting) && !isOverUI && Application.isFocused)
            {
                Vector3 m = Input.mousePosition;
                if (m.x < edgeScrollThreshold) h = -1f;
                else if (m.x > Screen.width - edgeScrollThreshold) h = 1f;
                if (m.y < edgeScrollThreshold) v = -1f;
                else if (m.y > Screen.height - edgeScrollThreshold) v = 1f;
            }

            Vector3 forward = transform.forward; forward.y = 0f;
            Vector3 right = transform.right; right.y = 0f;
            forward = forward.sqrMagnitude > 0.001f ? forward.normalized : Vector3.forward;
            right = right.sqrMagnitude > 0.001f ? right.normalized : Vector3.right;

            Vector3 dir = forward * v + right * h;
            if (dir.sqrMagnitude > 1f) dir.Normalize();

            Vector3 newPos = transform.position + dir * (speed * Time.deltaTime);
            newPos = ClampToBounds(newPos);
            transform.position = newPos;
        }

        private void HandleDrag()
        {
            // Блокируем захват перетаскивания средней кнопкой мыши над UI
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
            {
                _isDraggingMiddle = false;
                return;
            }

            if (Input.GetMouseButtonDown(2))
            {
                _isDraggingMiddle = true;
                _dragOrigin = Input.mousePosition;
            }
            if (Input.GetMouseButtonUp(2))
            {
                _isDraggingMiddle = false;
            }

            if (_isDraggingMiddle)
            {
                Vector3 delta = Input.mousePosition - _dragOrigin;
                _dragOrigin = Input.mousePosition;

                float zoomMult = transform.position.y * 0.0025f;
                Vector3 forward = transform.forward; forward.y = 0f; forward.Normalize();
                Vector3 right = transform.right; right.y = 0f; right.Normalize();

                Vector3 move = (-right * delta.x - forward * delta.y)
                             * zoomMult * middleMouseDragSensitivity;
                Vector3 newPos = ClampToBounds(transform.position + move);
                transform.position = newPos;
            }
        }

        private void HandleGalaxyZoomToCursor()
        {
            // Блокируем скролл галактики колесиком, если курсор находится над UI
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
            {
                Vector3 p = transform.position;
                p.y = Mathf.Lerp(p.y, _targetHeight, Time.deltaTime * zoomSmooth);
                transform.position = p;
                return;
            }

            float scroll = Input.GetAxis("Mouse ScrollWheel");
            if (Mathf.Abs(scroll) < 0.01f)
            {
                Vector3 p = transform.position;
                p.y = Mathf.Lerp(p.y, _targetHeight, Time.deltaTime * zoomSmooth);
                transform.position = p;
                return;
            }

            if (Camera.main == null) return;

            Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            Plane plane = new Plane(Vector3.up, Vector3.zero);
            float enter = 0f;
            plane.Raycast(ray, out enter);
            Vector3 worldPoint = ray.GetPoint(enter);

            float oldHeight = _targetHeight;
            _targetHeight -= scroll * zoomSpeed * 10f;
            _targetHeight = Mathf.Clamp(_targetHeight, minHeight, maxHeight);

            float heightDelta = _targetHeight - oldHeight;
            Vector3 shift = worldPoint - transform.position;
            shift.y = 0f;
            Vector3 move = shift * (heightDelta / Mathf.Max(1f, oldHeight)) * zoomToCursorStrength;

            Vector3 newPos = transform.position + move;
            newPos.y = Mathf.Lerp(transform.position.y, _targetHeight, Time.deltaTime * zoomSmooth);
            newPos = ClampToBounds(newPos);
            transform.position = newPos;
        }

        private void HandleGalaxyRotation()
        {
            float rot = 0f;
            if (Input.GetKey(KeyCode.Q)) rot += rotationSpeed * Time.deltaTime;
            if (Input.GetKey(KeyCode.E)) rot -= rotationSpeed * Time.deltaTime;
            if (Mathf.Abs(rot) > 0.001f)
                transform.Rotate(Vector3.up, rot, Space.World);
        }

        private void HandleShortcuts()
        {
            if (Input.GetKeyDown(KeyCode.Home))
                FocusOnCapital();
        }

        private void FocusOnCapital()
        {
            var gen = FindFirstObjectByType<StellarisClone.Generation.GalaxyGenerator>();
            if (gen == null || gen.Systems.Count == 0) return;
            FocusOn(gen.Systems[0].Position);
        }

        // ==================== ФОКУС ====================

        public void FocusOn(Vector3 worldPos)
        {
            _focusStartPos = transform.position;
            _focusTargetPos = new Vector3(worldPos.x, transform.position.y, worldPos.z - 25f);
            _focusTargetPos = ClampToBounds(_focusTargetPos);
            _focusTimer = 0f;
            _isFocusing = true;
        }

        private void UpdateFocus()
        {
            _focusTimer += Time.deltaTime;
            float t = Mathf.Clamp01(_focusTimer / focusDuration);
            float smooth = Mathf.SmoothStep(0f, 1f, t);
            transform.position = Vector3.Lerp(_focusStartPos, _focusTargetPos, smooth);

            if (t >= 1f)
            {
                _isFocusing = false;
                _targetHeight = transform.position.y;
            }
        }

        // ==================== ВСПОМОГАТЕЛЬНОЕ ====================

        private Vector3 ClampToBounds(Vector3 pos)
        {
            if (!limitToBounds) return pos;

            Vector2 flat = new Vector2(pos.x, pos.z);
            if (flat.magnitude > galaxyBoundsRadius)
            {
                flat = flat.normalized * galaxyBoundsRadius;
                pos.x = flat.x;
                pos.z = flat.y;
            }
            return pos;
        }
    }
}