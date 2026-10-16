using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace StellarisClone.Rendering
{
    /// <summary>
    /// Готовые эффекты частиц из Unity Particle Pack (Resources/VFX): взрывы, искры, электрические разряды.
    /// Пак сделан для «земных» сцен в метрах, поэтому каждый экземпляр приводится к космосу:
    ///   • один раз, без зацикливания; без гравитации и столкновений с полом;
    ///   • масштабируется целиком (Hierarchy + локальная симуляция) — BaseScale ниже подобран под размер кораблей карты;
    ///   • без источников света частиц (их дальность не масштабируется и заливала бы всю систему светом);
    ///   • слои без материала и искажение воздуха (нужна opaque-текстура URP) выключаются.
    /// Лимит одновременных эффектов и перезарядка по типу — чтобы частые попадания не забивали экран.
    /// Нет пака или эффекта — Play возвращает false, и CombatFx рисует свой процедурный вариант.
    /// </summary>
    public static class ParticleVfx
    {
        public enum Effect { TinyExplosion, SmallExplosion, BigExplosion, EnergyExplosion, Sparks, ElectricalSparks }

        private static readonly string[] Paths =
        {
            "VFX/Fire & Explosion Effects/Prefabs/TinyExplosion",
            "VFX/Fire & Explosion Effects/Prefabs/SmallExplosion",
            "VFX/Fire & Explosion Effects/Prefabs/BigExplosion",
            "VFX/Fire & Explosion Effects/Prefabs/EnergyExplosion",
            "VFX/Misc Effects/Prefabs/SparksEffect",
            "VFX/Misc Effects/Prefabs/ElectricalSparks",
        };

        /// <summary>Масштаб эффекта на единицу size (размер корабля в CombatFx: корвет ≈ 0.85, эсминец 1.5, база 1.9).</summary>
        private static readonly float[] BaseScale = { 0.18f, 0.34f, 0.24f, 0.45f, 0.45f, 0.45f };
        /// <summary>Минимальный интервал между эффектами одного типа (секунды реального времени).</summary>
        private static readonly float[] Cooldown = { 0.07f, 0f, 0f, 0f, 0.06f, 0.25f };

        private const int MaxAlive = 18;
        private const float MaxLife = 8f;

        private static GameObject[] s_prefabs;
        private static bool[] s_tried;
        private static float[] s_next;
        private static readonly List<float> s_aliveUntil = new List<float>();
        private static Runner s_runner;

        /// <summary>
        /// Запустить эффект в точке. size — как в CombatFx; follow — эффект едет вместе с объектом (искрящий корабль);
        /// duration &gt; 0 — остановить испускание через столько секунд (для зацикленных разрядов).
        /// </summary>
        public static bool Play(Effect e, Vector3 pos, float size = 1f, Transform follow = null, float duration = -1f, Vector3? normal = null)
            => Play(e, pos, size, follow, duration, normal, false);

        private static bool Play(Effect e, Vector3 pos, float size, Transform follow, float duration, Vector3? normal, bool force)
        {
            int i = (int)e;
            Ensure();
            float now = Time.unscaledTime;
            if (!force && now < s_next[i]) return false;
            s_aliveUntil.RemoveAll(t => t <= now);
            if (s_aliveUntil.Count >= MaxAlive) return false;

            var prefab = Prefab(i);
            if (prefab == null) return false;

            var rot = normal.HasValue && normal.Value.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(normal.Value) : Quaternion.identity;
            var go = Object.Instantiate(prefab, pos, rot);
            go.name = prefab.name;
            float scale = BaseScale[i] * Mathf.Max(0.2f, size);
            if (follow != null)
            {
                go.transform.SetParent(follow, true);
                float ls = Mathf.Max(1e-4f, follow.lossyScale.x);
                go.transform.localScale = Vector3.one * (scale / ls);
            }
            else go.transform.localScale = Vector3.one * scale;

            float life = Configure(go, duration);
            s_next[i] = now + Cooldown[i];
            s_aliveUntil.Add(now + life);
            Object.Destroy(go, life);
            if (duration > 0f) s_runner.StopLater(go, duration);
            return true;
        }

        /// <summary>То же с задержкой (снаряд ещё летит до цели).</summary>
        public static void PlayLater(float delay, Effect e, Vector3 pos, float size = 1f, Vector3? normal = null)
        {
            Ensure();
            if (delay <= 0.01f) { Play(e, pos, size, null, -1f, normal); return; }
            s_runner.Delay(delay, () => Play(e, pos, size, null, -1f, normal));
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot() => Ensure();

        /// <summary>
        /// Проверка (F7): все эффекты в ряд в центре экрана — видно, загрузились ли они и какого они размера
        /// рядом с кораблями. В консоль — какие эффекты найдены.
        /// </summary>
        public static void Showcase()
        {
            Ensure();
            var cam = Camera.main;
            if (cam == null) return;
            var ray = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            float t = Mathf.Abs(ray.direction.y) > 1e-3f ? -ray.origin.y / ray.direction.y : 40f;
            Vector3 center = ray.GetPoint(t > 0f ? t : 40f);
            var right = Vector3.ProjectOnPlane(cam.transform.right, Vector3.up).normalized;
            int n = Paths.Length, ok = 0;
            var report = new System.Text.StringBuilder("[VFX] Проверка эффектов:");
            for (int i = 0; i < n; i++)
            {
                var pos = center + right * ((i - (n - 1) * 0.5f) * 4f);
                bool played = Play((Effect)i, pos, 1.2f, null, i == (int)Effect.ElectricalSparks ? 2f : -1f, Vector3.up, true);
                if (played) ok++;
                report.Append($"\n  {(Effect)i}: {(played ? "показан" : Prefab(i) == null ? "НЕ НАЙДЕН в Resources/" + Paths[i] : "лимит")}");
            }
            report.Append($"\nПоказано {ok} из {n}. Камера на расстоянии {Vector3.Distance(cam.transform.position, center):0} ед.");
            Debug.Log(report.ToString());
        }

        private static void Ensure()
        {
            if (s_prefabs != null && s_runner != null) return;
            int n = Paths.Length;
            s_prefabs ??= new GameObject[n];
            s_tried ??= new bool[n];
            s_next ??= new float[n];
            if (s_runner == null)
            {
                var host = new GameObject("[VFX] ParticlePack");
                Object.DontDestroyOnLoad(host);
                s_runner = host.AddComponent<Runner>();
            }
        }

        private static GameObject Prefab(int i)
        {
            if (!s_tried[i])
            {
                s_tried[i] = true;
                s_prefabs[i] = Resources.Load<GameObject>(Paths[i]);
                if (s_prefabs[i] == null) Debug.LogWarning("[VFX] Нет эффекта Resources/" + Paths[i]);
            }
            return s_prefabs[i];
        }

        /// <summary>Привести экземпляр к космосу и запустить; вернуть, сколько он живёт.</summary>
        private static float Configure(GameObject go, float duration)
        {
            float life = 1.5f;
            var systems = go.GetComponentsInChildren<ParticleSystem>(true);
            foreach (var ps in systems) ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            foreach (var ps in systems)
            {
                var main = ps.main;
                main.loop = duration > 0f && main.loop;   // разряды на корабле крутятся, пока не остановим; взрывы — один раз
                main.playOnAwake = false;
                main.gravityModifier = 0f;
                main.scalingMode = ParticleSystemScalingMode.Hierarchy;
                main.simulationSpace = ParticleSystemSimulationSpace.Local;
                main.stopAction = ParticleSystemStopAction.None;

                var col = ps.collision;
                col.enabled = false;
                var lights = ps.lights;
                lights.enabled = false;

                var r = ps.GetComponent<ParticleSystemRenderer>();
                if (r != null)
                {
                    var m = r.sharedMaterial;
                    if (m == null || m.name.Contains("Distortion")) r.enabled = false;
                    if (r.trailMaterial == null) { var tr = ps.trails; tr.enabled = false; }
                }

                float l = main.duration + main.startDelay.constantMax + main.startLifetime.constantMax;
                if (main.simulationSpeed > 0.01f) l /= main.simulationSpeed;
                life = Mathf.Max(life, l);
            }
            foreach (var light in go.GetComponentsInChildren<Light>(true)) light.enabled = false;

            if (systems.Length > 0) systems[0].Play(true);
            float total = duration > 0f ? duration + 2.5f : life;
            return Mathf.Min(total, MaxLife);
        }

        /// <summary>Отложенный запуск и остановка зацикленных эффектов.</summary>
        private class Runner : MonoBehaviour
        {
            private void Update()
            {
                if (Input.GetKeyDown(KeyCode.F7)) Showcase();
            }

            public void Delay(float t, System.Action a) => StartCoroutine(After(t, a));
            public void StopLater(GameObject go, float t) => StartCoroutine(After(t, () =>
            {
                if (go == null) return;
                foreach (var ps in go.GetComponentsInChildren<ParticleSystem>()) ps.Stop(false, ParticleSystemStopBehavior.StopEmitting);
            }));

            private static IEnumerator After(float t, System.Action a)
            {
                yield return new WaitForSecondsRealtime(t);
                a?.Invoke();
            }
        }
    }
}
