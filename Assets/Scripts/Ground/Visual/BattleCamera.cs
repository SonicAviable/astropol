using UnityEngine;
using StellarisClone.Core;

namespace StellarisClone.Ground
{
    /// <summary>
    /// RTS-камера боя (как в Dawn of War / Company of Heroes): наклон зависит от зума (близко ~40°, далеко ~65°),
    /// зум колесом 18–90 м, WASD и края экрана, поворот средней кнопкой или Q/E. Тряска от взрывов — перлин-шум
    /// со спадом по расстоянию и потолком (чтобы не тошнило). Вход в бой — падение с орбиты сквозь облака.
    /// Всё на unscaledDeltaTime: камера работает и на тактической паузе, и в замедлении добиваний.
    /// </summary>
    public sealed class BattleCamera : MonoBehaviour
    {
        public static BattleCamera Instance { get; private set; }

        public Camera Cam { get; private set; }
        private GroundMap _map;
        private Vector3 _focus;
        private float _yaw, _zoom = 48f, _zoomTarget = 48f;
        private float _trauma;
        private float _intro;
        private Vector3 _punch;
        private const float MinZoom = 18f, MaxZoom = 90f;
        private UnityEngine.Rendering.VolumeProfile _grade;
        private UnityEngine.Rendering.Universal.ChromaticAberration _chroma;

        public static BattleCamera Create(Transform root, GroundMap map, Vector3 focus)
        {
            var go = new GameObject("BattleCamera");
            go.transform.SetParent(root, false);
            var cam = go.AddComponent<Camera>();
            cam.fieldOfView = 45f;
            cam.nearClipPlane = 0.3f;
            cam.farClipPlane = 700f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = map.Biome.Sky;
            cam.allowHDR = true;
            cam.depth = 10;
            go.AddComponent<AudioListener>();
            var data = UnityEngine.Rendering.Universal.CameraExtensions.GetUniversalAdditionalCameraData(cam);
            if (data != null)
            {
                data.renderPostProcessing = GraphicsQuality.PostFx;
                data.antialiasing = UnityEngine.Rendering.Universal.AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            }
            var bc = go.AddComponent<BattleCamera>();
            bc.Cam = cam;
            bc._map = map;
            bc._focus = focus;
            bc.BuildGrade(root);
            Instance = bc;
            return bc;
        }

        /// <summary>Цветокор боя: свой глобальный Volume поверх галактического — контраст, виньетка, хроматизм при сильной тряске.</summary>
        private void BuildGrade(Transform root)
        {
            var go = new GameObject("BattleGrade");
            go.transform.SetParent(root, false);
            var vol = go.AddComponent<UnityEngine.Rendering.Volume>();
            vol.isGlobal = true;
            vol.priority = 50f;
            _grade = ScriptableObject.CreateInstance<UnityEngine.Rendering.VolumeProfile>();
            _grade.hideFlags = HideFlags.DontSave;
            var ca = _grade.Add<UnityEngine.Rendering.Universal.ColorAdjustments>(true);
            ca.contrast.Override(18f);
            ca.saturation.Override(-8f);
            var vg = _grade.Add<UnityEngine.Rendering.Universal.Vignette>(true);
            vg.intensity.Override(0.3f);
            vg.smoothness.Override(0.45f);
            _chroma = _grade.Add<UnityEngine.Rendering.Universal.ChromaticAberration>(true);
            _chroma.intensity.Override(0f);
            vol.sharedProfile = _grade;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (_grade != null) Destroy(_grade);
        }

        public Vector3 Focus => _focus;

        public void LookAt(Vector3 p) => _focus = _map.Clamp(p, 10f);

        /// <summary>Кинематографичный вход: камера падает с высоты, пыль и удар (можно пропустить кликом).</summary>
        public static void PlayIntro()
        {
            if (Instance == null) return;
            Instance._intro = 3.2f;
        }

        /// <summary>Тряска от события в точке: сила гаснет с расстоянием до фокуса камеры.</summary>
        public static void Shake(Vector3 at, float power)
        {
            var c = Instance;
            if (c == null) return;
            float d = Vector3.Distance(at, c._focus);
            float k = Mathf.Clamp01(1f - d / (60f + power * 40f));
            c._trauma = Mathf.Min(1f, c._trauma + power * k * 0.45f);
        }

        /// <summary>Резкий наезд (добивания).</summary>
        public static void Punch(Vector3 at, float amount)
        {
            var c = Instance;
            if (c == null) return;
            c._punch = (at - c._focus) * 0.15f;
            c._zoomTarget = Mathf.Max(MinZoom, c._zoomTarget - amount);
        }

        private void LateUpdate()
        {
            float dt = Time.unscaledDeltaTime;
            var gm = GroundBattleManager.Instance;
            bool ui = GroundHud.PointerOverUi;

            if (_intro > 0f)
            {
                if (Input.GetMouseButtonDown(0) || Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.Space)) _intro = 0f;
                _intro -= dt;
                float k = Mathf.Clamp01(_intro / 3.2f);
                float h = Mathf.Lerp(_zoom, 420f, k * k);
                Place(h, Mathf.Lerp(62f, 88f, k), dt);
                if (_intro <= 0.05f && _intro > -1f) { _intro = -2f; Shake(_focus, 1.2f); GoreFx.Dust(_map.Snap(_focus), 6f); }
                return;
            }

            // Перемещение
            Vector3 fwd = Quaternion.Euler(0f, _yaw, 0f) * Vector3.forward;
            Vector3 right = Quaternion.Euler(0f, _yaw, 0f) * Vector3.right;
            Vector3 move = Vector3.zero;
            if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow)) move += fwd;
            if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow)) move -= fwd;
            if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) move += right;
            if (Input.GetKey(KeyCode.A) && !GroundInput.AttackModifier || Input.GetKey(KeyCode.LeftArrow)) move -= right;
            if (GameSettings.EdgeScroll && Application.isFocused)
            {
                var mp = Input.mousePosition;
                if (mp.x < 6f) move -= right;
                if (mp.x > Screen.width - 6f) move += right;
                if (mp.y < 6f) move -= fwd;
                if (mp.y > Screen.height - 6f) move += fwd;
            }
            float speed = Mathf.Lerp(28f, 95f, (_zoom - MinZoom) / (MaxZoom - MinZoom)) * GameSettings.CameraSpeed * (Input.GetKey(KeyCode.LeftShift) ? 2f : 1f);
            _focus += move.normalized * speed * dt;
            _focus = _map.Clamp(_focus, 8f);

            // Поворот
            if (Input.GetKey(KeyCode.Q)) _yaw -= 70f * dt;
            if (Input.GetKey(KeyCode.E)) _yaw += 70f * dt;
            if (Input.GetMouseButton(2)) _yaw += Input.GetAxis("Mouse X") * 3f;

            // Зум
            if (!ui) _zoomTarget = Mathf.Clamp(_zoomTarget * Mathf.Pow(0.88f, Input.GetAxis("Mouse ScrollWheel") * 10f), MinZoom, MaxZoom);
            _zoom = Mathf.Lerp(_zoom, _zoomTarget, 1f - Mathf.Exp(-dt * 8f));
            _punch = Vector3.Lerp(_punch, Vector3.zero, 1f - Mathf.Exp(-dt * 3f));

            float pitch = Mathf.Lerp(40f, 65f, (_zoom - MinZoom) / (MaxZoom - MinZoom));
            Place(_zoom, pitch, dt);
        }

        private void Place(float dist, float pitch, float dt)
        {
            var ground = _map.Snap(_focus) + _punch;
            var rot = Quaternion.Euler(pitch, _yaw, 0f);
            Vector3 pos = ground - rot * Vector3.forward * dist;
            // Тряска: перлин со спадом, потолок по силе
            if (_trauma > 0f)
            {
                float t = Time.unscaledTime * 22f;
                float s = _trauma * _trauma;
                pos += new Vector3(Mathf.PerlinNoise(t, 0.1f) - 0.5f, Mathf.PerlinNoise(0.2f, t) - 0.5f, Mathf.PerlinNoise(t, t) - 0.5f) * s * 1.6f;
                rot *= Quaternion.Euler((Mathf.PerlinNoise(t * 0.7f, 3f) - 0.5f) * s * 4f, (Mathf.PerlinNoise(5f, t * 0.7f) - 0.5f) * s * 4f, (Mathf.PerlinNoise(t, 7f) - 0.5f) * s * 3f);
                _trauma = Mathf.Max(0f, _trauma - dt * 1.4f);
                if (_chroma != null) _chroma.intensity.value = Mathf.Clamp01((_trauma - 0.35f) * 1.6f);
            }
            // Не уходить под землю
            var l = _map.ToLocal(pos);
            float minY = _map.Origin.y + _map.Height(Mathf.Clamp(l.x, -GroundMap.Half, GroundMap.Half), Mathf.Clamp(l.z, -GroundMap.Half, GroundMap.Half)) + 3f;
            if (pos.y < minY) pos.y = minY;
            transform.SetPositionAndRotation(pos, rot);
        }

        /// <summary>Луч из курсора на землю (по коллайдерам корня боя).</summary>
        public bool GroundUnderMouse(out Vector3 hit)
        {
            hit = default;
            var ray = Cam.ScreenPointToRay(Input.mousePosition);
            // Плоскость на высоте рельефа: сначала физика, иначе — пересечение с плоскостью фокуса
            if (Physics.Raycast(ray, out var h, 1500f, ~(1 << 2), QueryTriggerInteraction.Ignore) && h.point.y < _map.Origin.y + 200f)
            {
                hit = _map.Snap(h.point);
                return _map.Inside(hit, 2f);
            }
            var plane = new Plane(Vector3.up, _map.Snap(_focus));
            if (plane.Raycast(ray, out float e))
            {
                hit = _map.Snap(ray.GetPoint(e));
                return _map.Inside(hit, 2f);
            }
            return false;
        }

        public Vector3 WorldToScreen(Vector3 p) => Cam.WorldToScreenPoint(p);
    }
}
