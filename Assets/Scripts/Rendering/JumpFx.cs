using System.Collections.Generic;
using UnityEngine;

namespace StellarisClone.Rendering
{
    /// <summary>
    /// Эффекты гиперпрыжка на карте галактики (всё лежит в плоскости карты, аддитивно):
    ///   • уход в прыжок — вспышка у корабля, расходящееся кольцо и световой «росчерк» вперёд по курсу;
    ///   • выход из прыжка — сходящееся кольцо, «разрыв» пространства поперёк курса (раскрывается и схлопывается)
    ///     и вспышка в конце.
    /// Флоты одного строя прыгают одновременно — рядом стоящие вспышки за доли секунды не дублируются.
    /// </summary>
    public sealed class JumpFx : MonoBehaviour
    {
        private struct Part
        {
            public SpriteRenderer R;
            public Vector3 S0, S1;          // масштаб в начале и в конце окна
            public Vector3 P0, P1;          // позиция (мир)
            public float T0, T1;            // окно жизни детали, доля от общей длительности
            public float Peak;              // яркость
            public float Rise;              // доля окна на разгорание
        }

        private readonly List<Part> _parts = new List<Part>();
        private float _t, _life;

        private static Material s_mat;
        private static Sprite s_glow, s_ring, s_streak, s_lens;
        private static readonly List<(Vector3 pos, float time)> s_recent = new List<(Vector3, float)>();

        public static void Departure(Vector3 pos, Vector3 dir, Color tint)
        {
            if (!Allow(pos)) return;
            var fx = Create("JumpOut", 0.8f);
            Color c = Color.Lerp(tint, new Color(0.65f, 0.9f, 1f), 0.45f);
            Quaternion rot = Flat(dir);
            Vector3 fwd = Fwd(dir);
            fx.Add(Glow(), c, rot, pos, pos, new Vector3(0.6f, 0.6f, 1f), new Vector3(3.6f, 3.6f, 1f), 0f, 0.35f, 1.2f, 0.25f);
            fx.Add(Ring(), c, rot, pos, pos, new Vector3(0.8f, 0.8f, 1f), new Vector3(7.5f, 7.5f, 1f), 0f, 0.85f, 0.9f, 0.1f);
            fx.Add(Streak(), Color.Lerp(c, Color.white, 0.4f), rot, pos, pos + fwd * 5.5f,
                   new Vector3(0.8f, 0.6f, 1f), new Vector3(0.45f, 11f, 1f), 0.05f, 0.6f, 1.1f, 0.15f);
        }

        public static void Arrival(Vector3 pos, Vector3 dir, Color tint)
        {
            if (!Allow(pos)) return;
            var fx = Create("JumpIn", 0.95f);
            Color c = Color.Lerp(tint, new Color(0.65f, 0.9f, 1f), 0.45f);
            Quaternion rot = Flat(dir);
            fx.Add(Ring(), c, rot, pos, pos, new Vector3(7f, 7f, 1f), new Vector3(0.9f, 0.9f, 1f), 0f, 0.55f, 0.8f, 0.35f);
            // Разрыв: узкая «линза» поперёк курса — раскрывается и схлопывается
            fx.Add(Lens(), Color.Lerp(c, Color.white, 0.35f), rot, pos, pos, new Vector3(0.6f, 0.05f, 1f), new Vector3(6.5f, 1.3f, 1f), 0.1f, 0.75f, 1.3f, 0.45f);
            fx.Add(Glow(), c, rot, pos, pos, new Vector3(0.5f, 0.5f, 1f), new Vector3(3.2f, 3.2f, 1f), 0.45f, 1f, 1.1f, 0.2f);
        }

        // ---------------------------------------------------------------- Сборка

        private static bool Allow(Vector3 pos)
        {
            float now = Time.unscaledTime;
            for (int i = s_recent.Count - 1; i >= 0; i--)
            {
                if (now - s_recent[i].time > 0.25f) { s_recent.RemoveAt(i); continue; }
                if ((s_recent[i].pos - pos).sqrMagnitude < 4f) return false;
            }
            s_recent.Add((pos, now));
            return true;
        }

        private static JumpFx Create(string name, float life)
        {
            var go = new GameObject(name);
            var fx = go.AddComponent<JumpFx>();
            fx._life = life;
            return fx;
        }

        /// <summary>Лечь в плоскость карты: локальная «вверх» спрайта — по курсу.</summary>
        private static Quaternion Flat(Vector3 dir)
        {
            Vector3 f = Fwd(dir);
            float yaw = Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg;
            return Quaternion.Euler(90f, yaw, 0f);
        }

        private static Vector3 Fwd(Vector3 dir)
        {
            dir.y = 0f;
            return dir.sqrMagnitude > 0.0001f ? dir.normalized : Vector3.forward;
        }

        private void Add(Sprite sp, Color c, Quaternion rot, Vector3 p0, Vector3 p1, Vector3 s0, Vector3 s1, float t0, float t1, float peak, float rise)
        {
            var go = new GameObject("Part");
            go.transform.SetParent(transform, false);
            go.transform.SetPositionAndRotation(p0, rot);
            go.transform.localScale = s0;
            var r = go.AddComponent<SpriteRenderer>();
            r.sprite = sp;
            r.sharedMaterial = Mat();
            r.color = new Color(c.r, c.g, c.b, 0f);
            r.sortingOrder = 20;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            _parts.Add(new Part { R = r, S0 = s0, S1 = s1, P0 = p0, P1 = p1, T0 = t0, T1 = t1, Peak = peak, Rise = Mathf.Max(0.01f, rise) });
        }

        private void Update()
        {
            _t += Time.unscaledDeltaTime / Mathf.Max(0.05f, _life);
            if (_t >= 1f) { Destroy(gameObject); return; }
            foreach (var p in _parts)
            {
                if (p.R == null) continue;
                float k = Mathf.InverseLerp(p.T0, p.T1, _t);
                bool live = _t >= p.T0 && _t <= p.T1;
                float e = LGEase.OutCubic(k);
                p.R.transform.position = Vector3.Lerp(p.P0, p.P1, e);
                p.R.transform.localScale = Vector3.LerpUnclamped(p.S0, p.S1, e);
                float a = !live ? 0f : k < p.Rise ? k / p.Rise : 1f - (k - p.Rise) / (1f - p.Rise);
                var c = p.R.color;
                c.a = Mathf.Clamp01(a * a * p.Peak);
                p.R.color = c;
            }
        }

        // ---------------------------------------------------------------- Материал и спрайты

        private static Material Mat()
        {
            if (s_mat != null) return s_mat;
            var sh = Resources.Load<Shader>("Shaders/CombatAdditive");
            if (sh == null) sh = Shader.Find("Sprites/Default");
            s_mat = new Material(sh) { name = "JumpFx" };
            return s_mat;
        }

        private static Sprite Glow() => s_glow ??= Make(64, (u, v) =>
        {
            float d = Mathf.Sqrt(u * u + v * v);
            return Mathf.Exp(-d * d * 5f) * Mathf.Clamp01(1f - d);
        }, "JumpGlow");

        private static Sprite Ring() => s_ring ??= Make(128, (u, v) =>
        {
            float d = Mathf.Sqrt(u * u + v * v);
            return Mathf.Exp(-Mathf.Pow((d - 0.86f) / 0.045f, 2f)) + Mathf.Exp(-Mathf.Pow((d - 0.86f) / 0.16f, 2f)) * 0.25f;
        }, "JumpRing");

        /// <summary>Росчерк: узкий по ширине, вдоль (v) — ярче в голове, сходит на нет к хвосту.</summary>
        private static Sprite Streak() => s_streak ??= Make(64, (u, v) =>
        {
            float across = Mathf.Exp(-u * u * 18f);
            float along = Mathf.Clamp01(1f - Mathf.Abs(v)) * Mathf.Lerp(0.35f, 1f, (v + 1f) * 0.5f);
            return across * along;
        }, "JumpStreak");

        /// <summary>Линза разрыва: вдоль (u) — длинная, поперёк (v) — тонкая, яркая середина.</summary>
        private static Sprite Lens() => s_lens ??= Make(128, (u, v) =>
        {
            float th = Mathf.Max(0.001f, 1f - u * u);
            float core = Mathf.Exp(-Mathf.Pow(v / (0.45f * th), 2f) * 3f);
            return core * th;
        }, "JumpLens");

        private static Sprite Make(int n, System.Func<float, float, float> alpha, string name)
        {
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, true)
            { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = name, hideFlags = HideFlags.DontSave };
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float u = (x + 0.5f) / n * 2f - 1f, v = (y + 0.5f) / n * 2f - 1f;
                px[y * n + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(Mathf.Clamp01(alpha(u, v)) * 255f));
            }
            tex.SetPixels32(px);
            tex.Apply(true);
            var sp = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), n);
            sp.hideFlags = HideFlags.DontSave;
            return sp;
        }
    }
}
