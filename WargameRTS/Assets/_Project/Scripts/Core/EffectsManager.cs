using System.Collections.Generic;
using UnityEngine;

namespace WargameRTS.Core
{
    /// <summary>Cheap pooled visual effects: shot tracers and explosion flashes.</summary>
    public sealed class EffectsManager : MonoBehaviour
    {
        const float TracerLifetime = 0.12f;
        const float ExplosionLifetime = 0.45f;

        struct ActiveTracer
        {
            public LineRenderer Line;
            public float ExpiresAt;
        }

        struct ActiveExplosion
        {
            public Transform Transform;
            public float StartedAt;
            public float Size;
        }

        static EffectsManager instance;

        readonly Stack<LineRenderer> tracerPool = new Stack<LineRenderer>();
        readonly List<ActiveTracer> tracers = new List<ActiveTracer>();
        readonly List<ActiveExplosion> explosions = new List<ActiveExplosion>();
        Camera mainCamera;

        void Awake() => instance = this;

        void OnDestroy()
        {
            if (instance == this)
                instance = null;
        }

        public static void Shot(Vector3 from, Vector3 to, Color color, float width)
        {
            if (instance != null)
                instance.SpawnTracer(from, to, color, width);
        }

        public static void Explosion(Vector3 position, float size)
        {
            if (instance != null)
                instance.SpawnExplosion(position, size);
        }

        void SpawnTracer(Vector3 from, Vector3 to, Color color, float width)
        {
            var line = tracerPool.Count > 0 ? tracerPool.Pop() : CreateLine();
            line.gameObject.SetActive(true);
            line.sharedMaterial = GameMaterials.Unlit(color);

            // Keep tracers readable when the camera is zoomed far out.
            if (mainCamera == null)
                mainCamera = Camera.main;
            float cameraDistance = mainCamera != null ? Vector3.Distance(mainCamera.transform.position, from) : 100f;
            line.widthMultiplier = Mathf.Max(width, cameraDistance * 0.002f);
            line.SetPosition(0, from);
            line.SetPosition(1, to);

            tracers.Add(new ActiveTracer { Line = line, ExpiresAt = Time.time + TracerLifetime });
        }

        LineRenderer CreateLine()
        {
            var go = new GameObject("Tracer");
            go.transform.SetParent(transform, false);
            var line = go.AddComponent<LineRenderer>();
            line.positionCount = 2;
            line.useWorldSpace = true;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            return line;
        }

        void SpawnExplosion(Vector3 position, float size)
        {
            var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(sphere.GetComponent<Collider>());
            sphere.name = "Explosion";
            sphere.transform.SetParent(transform, false);
            sphere.transform.position = position;
            sphere.transform.localScale = Vector3.zero;
            var sphereRenderer = sphere.GetComponent<MeshRenderer>();
            sphereRenderer.sharedMaterial = GameMaterials.Unlit(new Color(1f, 0.55f, 0.15f));
            sphereRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            explosions.Add(new ActiveExplosion { Transform = sphere.transform, StartedAt = Time.time, Size = size });
        }

        void Update()
        {
            float now = Time.time;

            for (int i = tracers.Count - 1; i >= 0; i--)
            {
                if (now < tracers[i].ExpiresAt)
                    continue;
                tracers[i].Line.gameObject.SetActive(false);
                tracerPool.Push(tracers[i].Line);
                tracers.RemoveAt(i);
            }

            for (int i = explosions.Count - 1; i >= 0; i--)
            {
                var explosion = explosions[i];
                float t = (now - explosion.StartedAt) / ExplosionLifetime;
                if (t >= 1f)
                {
                    Destroy(explosion.Transform.gameObject);
                    explosions.RemoveAt(i);
                    continue;
                }

                // Grow fast, then shrink away.
                float scale = explosion.Size * Mathf.Sin(t * Mathf.PI) * (1f - t * 0.3f);
                explosion.Transform.localScale = Vector3.one * scale;
            }
        }
    }
}
