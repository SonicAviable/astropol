using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace StellarisClone.Cards
{
    /// <summary>
    /// Настоящие системы частиц (Unity Particle Pack: взрывы, вспышки выстрелов, попадания, огнемёт, плазма) поверх
    /// карточного стола. Канвас боя — Overlay, поэтому частицы снимает своя ортографическая камера далеко от сцены
    /// (на свободном слое) в RenderTexture, а та выводится картинкой с премультиплицированной альфой (UIPremultiplied)
    /// в слое эффектов: свечение складывается со столом, дым его заслоняет.
    /// Координаты — как у слоя эффектов (UIFx.Local): пиксели канваса от центра.
    /// Нет пака/шейдера — Play возвращает false, и бой рисует свой спрайтовый вариант.
    /// </summary>
    public sealed class CardParticles : MonoBehaviour
    {
        public enum Fx { BigExplosion, SmallExplosion, TinyExplosion, Energy, Sparks, Electric, Muzzle, MetalHit, FleshHit, Flame, Dust, Plasma }

        private static readonly string[] Paths =
        {
            "VFX/Fire & Explosion Effects/Prefabs/BigExplosion",
            "VFX/Fire & Explosion Effects/Prefabs/SmallExplosion",
            "VFX/Fire & Explosion Effects/Prefabs/TinyExplosion",
            "VFX/Fire & Explosion Effects/Prefabs/EnergyExplosion",
            "VFX/Misc Effects/Prefabs/SparksEffect",
            "VFX/Misc Effects/Prefabs/ElectricalSparks",
            "VFX/Battle/Weapon Effects/Prefabs/MuzzleFlash",
            "VFX/Battle/Weapon Effects/Prefabs/MetalImpacts",
            "VFX/Battle/Weapon Effects/Prefabs/FleshImpacts",
            "VFX/Battle/Fire & Explosion Effects/Prefabs/FlameThrower",
            "VFX/Battle/Fire & Explosion Effects/Prefabs/DustExplosion",
            "VFX/Battle/Legacy Particles/Prefabs/PlasmaExplosionEffect",
        };

        /// <summary>Масштаб эффекта (пак сделан в метрах; 1 ед. мира = 100 пикселей канваса).</summary>
        private static readonly float[] BaseScale = { 0.42f, 0.55f, 0.7f, 0.5f, 0.7f, 0.6f, 0.55f, 0.75f, 0.75f, 0.5f, 0.38f, 0.42f };
        /// <summary>Сколько секунд эффект живёт максимум.</summary>
        private const float MaxLife = 4f;
        private const int MaxAlive = 40;
        private const float Unit = 0.01f;   // мира на пиксель канваса
        private const int Layer = 9;

        private Camera _cam;
        private RenderTexture _rt;
        private RawImage _img;
        private RectTransform _layer;
        private readonly GameObject[] _prefabs = new GameObject[Paths.Length];
        private readonly bool[] _tried = new bool[Paths.Length];
        private readonly List<(GameObject go, float until)> _alive = new List<(GameObject, float)>();
        private bool _ok;

        public float Speed = 1f;

        /// <summary>Создать камеру и картинку в слое эффектов (картинка — первым ребёнком: под числами и вспышкой).</summary>
        public static CardParticles Create(RectTransform fxLayer)
        {
            var go = new GameObject("[FX] CardParticles");
            DontDestroyOnLoad(go);
            var p = go.AddComponent<CardParticles>();
            p.Init(fxLayer);
            return p;
        }

        private void Init(RectTransform fxLayer)
        {
            _layer = fxLayer;
            transform.position = new Vector3(0f, -20000f, 0f);
            var shader = Resources.Load<Shader>("Shaders/UIPremultiplied");
            if (shader == null) { Debug.LogWarning("[CardParticles] Нет шейдера Shaders/UIPremultiplied — частицы выключены"); return; }

            var camGo = new GameObject("CardFxCamera");
            camGo.transform.SetParent(transform, false);
            camGo.transform.localPosition = new Vector3(0f, 0f, -20f);
            _cam = camGo.AddComponent<Camera>();
            _cam.orthographic = true;
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
            _cam.nearClipPlane = 0.1f;
            _cam.farClipPlane = 60f;
            _cam.cullingMask = 1 << Layer;
            _cam.allowHDR = false;
            _cam.allowMSAA = false;
            _cam.depth = -50;
            _cam.enabled = false;

            var imgGo = new GameObject("Particles", typeof(RectTransform));
            imgGo.transform.SetParent(fxLayer, false);
            imgGo.transform.SetAsFirstSibling();
            _img = imgGo.AddComponent<RawImage>();
            _img.material = new Material(shader);
            _img.raycastTarget = false;
            var r = _img.rectTransform;
            r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.offsetMin = r.offsetMax = Vector2.zero;
            _ok = true;
            Resize();
        }

        /// <summary>Подогнать текстуру и камеру под размер слоя эффектов.</summary>
        private void Resize()
        {
            if (!_ok || _layer == null) return;
            var size = _layer.rect.size;
            if (size.x < 10f || size.y < 10f) return;
            int w = Mathf.Clamp(Screen.width, 320, 3840), h = Mathf.Clamp(Screen.height, 240, 2160);
            if (_rt == null || _rt.width != w || _rt.height != h)
            {
                if (_rt != null) { _cam.targetTexture = null; _rt.Release(); Destroy(_rt); }
                _rt = new RenderTexture(w, h, 16, RenderTextureFormat.ARGB32) { name = "CardFxRT", filterMode = FilterMode.Bilinear };
                _cam.targetTexture = _rt;
                _img.texture = _rt;
                _cam.enabled = _want;
            }
            _cam.orthographicSize = size.y * 0.5f * Unit;
            _cam.aspect = size.x / size.y;
        }

        private bool _want;

        public void SetActive(bool on)
        {
            _want = on;
            if (_cam != null) _cam.enabled = on && _ok && _rt != null;   // без текстуры камера рисовала бы прямо на экран
            if (_img != null) _img.enabled = on && _ok;
            if (!on) Clear();
        }

        public void Clear()
        {
            foreach (var a in _alive) if (a.go != null) Destroy(a.go);
            _alive.Clear();
        }

        private void OnDestroy()
        {
            Clear();
            if (_rt != null) { _rt.Release(); Destroy(_rt); }
        }

        private void LateUpdate()
        {
            if (!_ok) return;
            Resize();
            float now = Time.unscaledTime;
            for (int i = _alive.Count - 1; i >= 0; i--)
                if (_alive[i].go == null || now >= _alive[i].until) { if (_alive[i].go != null) Destroy(_alive[i].go); _alive.RemoveAt(i); }
            // Скорость боя — скорость частиц
            foreach (var a in _alive)
                if (a.go != null)
                    foreach (var ps in a.go.GetComponentsInChildren<ParticleSystem>())
                    {
                        var m = ps.main; m.simulationSpeed = Speed;
                        Flatten(ps);
                    }
        }

        private static ParticleSystem.Particle[] s_buf = new ParticleSystem.Particle[512];

        /// <summary>
        /// Искры и обломки пака летят во все стороны в 3D; камера смотрит сверху, и те, что летят к ней или от неё,
        /// вытягиваются вдоль взгляда — на экране это «точки». Скорость таких частиц кладём в плоскость стола,
        /// сохраняя её величину: каждая искра — видимый штрих.
        /// </summary>
        private static void Flatten(ParticleSystem ps)
        {
            var r = ps.GetComponent<ParticleSystemRenderer>();
            if (r == null || !(r.renderMode == ParticleSystemRenderMode.Stretch || ps.trails.enabled)) return;
            int n = ps.particleCount;
            if (n == 0) return;
            if (s_buf.Length < n) s_buf = new ParticleSystem.Particle[n];
            n = ps.GetParticles(s_buf, n);
            bool local = ps.main.simulationSpace == ParticleSystemSimulationSpace.Local;
            var tr = ps.transform;
            bool changed = false;
            for (int i = 0; i < n; i++)
            {
                var v = s_buf[i].velocity;
                var w = local ? tr.TransformDirection(v) : v;
                if (Mathf.Abs(w.z) < 0.001f) continue;
                float speed = w.magnitude;
                var xy = new Vector2(w.x, w.y);
                if (xy.sqrMagnitude < 1e-6f) xy = Random.insideUnitCircle;
                xy = xy.normalized * speed;
                w = new Vector3(xy.x, xy.y, 0f);
                s_buf[i].velocity = local ? tr.InverseTransformDirection(w) : w;
                changed = true;
            }
            if (changed) ps.SetParticles(s_buf, n);
        }

        private GameObject Prefab(int i)
        {
            if (!_tried[i])
            {
                _tried[i] = true;
                _prefabs[i] = Resources.Load<GameObject>(Paths[i]);
                if (_prefabs[i] == null) Debug.LogWarning("[CardParticles] Нет эффекта Resources/" + Paths[i]);
            }
            return _prefabs[i];
        }

        /// <summary>
        /// Запустить эффект в точке слоя эффектов. dirDeg — направление (вспышка выстрела и огнемёт смотрят по нему,
        /// попадания — навстречу). Вернёт false, если эффекта нет (тогда рисуется спрайтовый вариант).
        /// </summary>
        public bool Play(Fx fx, Vector2 uiPos, float scale = 1f, float dirDeg = 90f)
        {
            if (!_ok || _cam == null || !_cam.enabled || _rt == null) return false;
            int i = (int)fx;
            var prefab = Prefab(i);
            if (prefab == null) return false;
            if (_alive.Count >= MaxAlive) { if (_alive[0].go != null) Destroy(_alive[0].go); _alive.RemoveAt(0); }

            var dir = new Vector3(Mathf.Cos(dirDeg * Mathf.Deg2Rad), Mathf.Sin(dirDeg * Mathf.Deg2Rad), 0f);
            Quaternion rot;
            switch (fx)
            {
                case Fx.Muzzle: case Fx.Flame: rot = Quaternion.LookRotation(dir, Vector3.back); break;
                case Fx.MetalHit: case Fx.FleshHit: rot = Quaternion.LookRotation(-dir, Vector3.back); break;
                default: rot = Quaternion.identity; break;   // как в демо пака: «вверх» — вверх экрана, дым поднимается
            }
            var pos = transform.position + new Vector3(uiPos.x * Unit, uiPos.y * Unit, 0f);
            var go = Instantiate(prefab, pos, rot, transform);
            go.name = prefab.name;
            go.transform.localScale = Vector3.one * BaseScale[i] * Mathf.Max(0.1f, scale);
            SetLayer(go, Layer);
            float life = Configure(go);
            _alive.Add((go, Time.unscaledTime + life));
            return true;
        }

        private static void SetLayer(GameObject go, int layer)
        {
            go.layer = layer;
            foreach (Transform c in go.transform) SetLayer(c.gameObject, layer);
        }

        /// <summary>Привести экземпляр к столу: один раз, без гравитации и столкновений, без света и искажений, по реальному времени.</summary>
        private float Configure(GameObject go)
        {
            float life = 1f;
            var systems = go.GetComponentsInChildren<ParticleSystem>(true);
            foreach (var ps in systems) ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            foreach (var ps in systems)
            {
                var main = ps.main;
                main.loop = false;
                main.playOnAwake = false;
                main.gravityModifier = main.gravityModifier.constant * 0.6f;
                main.scalingMode = ParticleSystemScalingMode.Hierarchy;
                main.useUnscaledTime = true;
                main.simulationSpeed = Speed;
                main.stopAction = ParticleSystemStopAction.None;
                var col = ps.collision; col.enabled = false;
                var lights = ps.lights; lights.enabled = false;
                var r = ps.GetComponent<ParticleSystemRenderer>();
                if (r != null)
                {
                    var m = r.sharedMaterial;
                    if (m == null || m.name.Contains("Distortion") || m.name.Contains("Decal")) r.enabled = false;
                    if (r.trailMaterial == null) { var tr = ps.trails; tr.enabled = false; }
                    r.sortingOrder = 0;
                }
                float l = main.duration + main.startDelay.constantMax + main.startLifetime.constantMax;
                life = Mathf.Max(life, l);
            }
            foreach (var light in go.GetComponentsInChildren<Light>(true)) light.enabled = false;
            // В префабах попаданий пака на корне лежит серая сфера-«мишень» из демо-сцены — на столе она
            // выглядела как шар поверх карты. Рисуем только частицы: обычные меши выключаем.
            foreach (var mr in go.GetComponentsInChildren<MeshRenderer>(true)) mr.enabled = false;
            foreach (var sr in go.GetComponentsInChildren<SkinnedMeshRenderer>(true)) sr.enabled = false;
            foreach (var mb in go.GetComponentsInChildren<MonoBehaviour>(true)) mb.enabled = false;
            if (systems.Length > 0) systems[0].Play(true);
            return Mathf.Min(life / Mathf.Max(0.2f, Speed), MaxLife);
        }
    }
}
