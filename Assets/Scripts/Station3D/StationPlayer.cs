using UnityEngine;

namespace StellarisClone.Station3D
{
    /// <summary>Игрок на станции от первого лица: WASD/стрелки, мышь — обзор, Shift — бег. Работает и на паузе.</summary>
    [RequireComponent(typeof(CharacterController))]
    public class StationPlayer : MonoBehaviour
    {
        public const float EyeHeight = 1.62f;
        public float WalkSpeed = 3.4f;
        public float RunSpeed = 6.2f;
        public float MouseSensitivity = 2.2f;

        public float Yaw, Pitch;
        public bool InputEnabled;

        private CharacterController _cc;
        private Vector3 _vel;
        private float _bobPhase, _bob;

        public Vector3 EyePosition => transform.position + Vector3.up * (EyeHeight + _bob);
        public Quaternion LookRotation => Quaternion.Euler(Pitch, Yaw, 0f);

        private void Awake()
        {
            _cc = GetComponent<CharacterController>();
            _cc.height = 1.8f;
            _cc.radius = 0.35f;
            _cc.center = new Vector3(0f, 0.9f, 0f);
            _cc.stepOffset = 0.4f;
            _cc.slopeLimit = 50f;
            _cc.skinWidth = 0.04f;
        }

        public void Teleport(Vector3 pos, float yaw)
        {
            if (_cc == null) _cc = GetComponent<CharacterController>();
            _cc.enabled = false;
            transform.position = pos;
            _cc.enabled = true;
            Yaw = yaw;
            Pitch = 0f;
            _vel = Vector3.zero;
        }

        private void Update()
        {
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
            Vector3 wish = Vector3.zero;
            bool run = false;
            if (InputEnabled)
            {
                Yaw += Input.GetAxisRaw("Mouse X") * MouseSensitivity;
                Pitch = Mathf.Clamp(Pitch - Input.GetAxisRaw("Mouse Y") * MouseSensitivity, -80f, 80f);
                float h = 0f, v = 0f;
                if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow)) v += 1f;
                if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow)) v -= 1f;
                if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) h += 1f;
                if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow)) h -= 1f;
                run = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
                wish = Quaternion.Euler(0f, Yaw, 0f) * new Vector3(h, 0f, v);
                if (wish.sqrMagnitude > 1f) wish.Normalize();
                wish *= run ? RunSpeed : WalkSpeed;
            }

            var flat = new Vector3(_vel.x, 0f, _vel.z);
            flat = Vector3.MoveTowards(flat, wish, (wish.sqrMagnitude > 0.01f ? 28f : 20f) * dt);
            _vel.x = flat.x; _vel.z = flat.z;
            if (_cc.isGrounded && _vel.y < 0f) _vel.y = -2f;
            else _vel.y -= 20f * dt;
            _cc.Move(_vel * dt);

            // Покачивание головы при ходьбе
            float speed = flat.magnitude;
            if (_cc.isGrounded && speed > 0.3f) _bobPhase += dt * speed * 2.1f;
            float target = speed > 0.3f ? Mathf.Sin(_bobPhase) * 0.035f * Mathf.Clamp01(speed / RunSpeed + 0.4f) : 0f;
            _bob = Mathf.Lerp(_bob, target, dt * 10f);
        }
    }
}
