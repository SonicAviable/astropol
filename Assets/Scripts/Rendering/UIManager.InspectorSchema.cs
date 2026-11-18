using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using StellarisClone.Core;

namespace StellarisClone.Rendering
{
    /// <summary>
    /// Схема системы в шапке панели («Атлас», вариант «Схема»): орбиты тел — тонкие эллипсы, тела — точки своего цвета
    /// (колонии — в кольце морской зелени), граница системы — пунктир, выходы гиперкоридоров — золотые шевроны у края
    /// кадра в сторону соседних систем. Рисуется одним мешем, без текстур.
    /// </summary>
    public sealed class SystemSchemaGraphic : MaskableGraphic
    {
        public struct Body { public float Orbit, Angle, Radius; public Color Color; public bool Colony; }

        public Vector2 Center01 = new Vector2(0.66f, 0.58f);
        public float Flatten = 0.42f;
        public bool Unknown;
        public readonly List<float> Orbits = new List<float>();
        public readonly List<Body> Bodies = new List<Body>();
        public readonly List<float> Corridors = new List<float>();

        private static readonly Color Cream = new Color(0.925f, 0.898f, 0.827f);
        private static readonly Color Gold = new Color(0.906f, 0.757f, 0.439f);
        private static readonly Color Sea = new Color(0.475f, 0.816f, 0.749f);

        public Vector2 Center(Rect r) => new Vector2(r.xMin + r.width * Center01.x, r.yMin + r.height * Center01.y);
        public float OuterR(Rect r) => r.width * 0.5f;
        public Vector2 OnOrbit(Rect r, float rx, float ang) => Center(r) + new Vector2(Mathf.Cos(ang) * rx, Mathf.Sin(ang) * rx * Flatten);

        /// <summary>Точка выхода коридора: луч из звезды до края кадра (с полем), там стоит шеврон.</summary>
        public Vector2 CorridorPoint(Rect r, float ang, float inset = 14f)
        {
            var c = Center(r);
            var d = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
            float tx = d.x > 0.001f ? (r.xMax - inset - c.x) / d.x : d.x < -0.001f ? (r.xMin + inset - c.x) / d.x : float.MaxValue;
            float ty = d.y > 0.001f ? (r.yMax - inset - c.y) / d.y : d.y < -0.001f ? (r.yMin + inset - c.y) / d.y : float.MaxValue;
            return c + d * Mathf.Min(tx, ty);
        }

        public void Rebuild() => SetVerticesDirty();

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            var r = GetPixelAdjustedRect();
            var c = Center(r);
            float outer = OuterR(r);

            // Визир через звезду — едва заметные оси, как на штурманской карте
            Seg(vh, new Vector2(r.xMin + 10f, c.y), new Vector2(r.xMax - 10f, c.y), 1f, A(Cream, 0.05f));
            Seg(vh, new Vector2(c.x, r.yMin + 8f), new Vector2(c.x, r.yMax - 8f), 1f, A(Cream, 0.05f));

            // Орбиты: ближние ярче, дальние тише
            for (int i = 0; i < Orbits.Count; i++)
            {
                float t = Orbits.Count <= 1 ? 0f : i / (float)(Orbits.Count - 1);
                Ellipse(vh, c, Orbits[i], Orbits[i] * Flatten, 1f, A(Cream, Mathf.Lerp(0.24f, 0.12f, t)), false);
            }
            // Граница системы — пунктир
            Ellipse(vh, c, outer, outer * Flatten, 1f, A(Cream, Unknown ? 0.16f : 0.09f), true);

            // Выходы коридоров: пунктир от границы до шеврона, сам шеврон смотрит наружу
            foreach (float ang in Corridors)
            {
                var d = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
                var n = new Vector2(-d.y, d.x);
                var p = CorridorPoint(r, ang);
                var from = c + d * Mathf.Min(Vector2.Distance(c, p) - 10f, outer * 0.75f);
                Dashed(vh, from, p - d * 6f, 1f, A(Gold, 0.28f), 3f, 4f);
                Seg(vh, p - d * 3.5f + n * 5f, p + d * 2.5f, 1.4f, A(Gold, 0.9f));
                Seg(vh, p - d * 3.5f - n * 5f, p + d * 2.5f, 1.4f, A(Gold, 0.9f));
            }

            // Тела
            foreach (var b in Bodies)
            {
                var p = c + new Vector2(Mathf.Cos(b.Angle) * b.Orbit, Mathf.Sin(b.Angle) * b.Orbit * Flatten);
                Disc(vh, p, b.Radius + 2.5f, A(new Color(0.043f, 0.071f, 0.125f), 0.9f));   // тёмный зазор поверх орбиты
                Disc(vh, p, b.Radius, b.Color);
                if (b.Colony) Ring(vh, p, b.Radius + 3.5f, 1.2f, A(Sea, 0.9f));
            }
        }

        private static Color A(Color c, float a) => new Color(c.r, c.g, c.b, a);

        private static void Seg(VertexHelper vh, Vector2 a, Vector2 b, float w, Color col)
        {
            var d = b - a;
            if (d.sqrMagnitude < 0.0001f) return;
            var n = new Vector2(-d.y, d.x).normalized * (w * 0.5f);
            int i = vh.currentVertCount;
            vh.AddVert(a - n, col, Vector2.zero);
            vh.AddVert(a + n, col, Vector2.zero);
            vh.AddVert(b + n, col, Vector2.zero);
            vh.AddVert(b - n, col, Vector2.zero);
            vh.AddTriangle(i, i + 1, i + 2);
            vh.AddTriangle(i, i + 2, i + 3);
        }

        private static void Dashed(VertexHelper vh, Vector2 a, Vector2 b, float w, Color col, float dash, float gap)
        {
            float len = Vector2.Distance(a, b);
            if (len < 1f) return;
            var d = (b - a) / len;
            for (float s = 0f; s < len; s += dash + gap)
                Seg(vh, a + d * s, a + d * Mathf.Min(len, s + dash), w, col);
        }

        private static void Ellipse(VertexHelper vh, Vector2 c, float rx, float ry, float w, Color col, bool dashed)
        {
            int n = Mathf.Clamp(Mathf.RoundToInt(rx * 0.6f), 48, 144);
            for (int i = 0; i < n; i++)
            {
                if (dashed && (i % 3) == 2) continue;
                float a0 = i / (float)n * Mathf.PI * 2f, a1 = (i + 1) / (float)n * Mathf.PI * 2f;
                Seg(vh, c + new Vector2(Mathf.Cos(a0) * rx, Mathf.Sin(a0) * ry), c + new Vector2(Mathf.Cos(a1) * rx, Mathf.Sin(a1) * ry), w, col);
            }
        }

        private static void Disc(VertexHelper vh, Vector2 c, float rad, Color col)
        {
            const int n = 20;
            int i0 = vh.currentVertCount;
            vh.AddVert(c, col, Vector2.zero);
            for (int i = 0; i <= n; i++)
            {
                float a = i / (float)n * Mathf.PI * 2f;
                vh.AddVert(c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * rad, col, Vector2.zero);
            }
            for (int i = 0; i < n; i++) vh.AddTriangle(i0, i0 + 1 + i, i0 + 2 + i);
        }

        private static void Ring(VertexHelper vh, Vector2 c, float rad, float w, Color col)
        {
            const int n = 28;
            for (int i = 0; i < n; i++)
            {
                float a0 = i / (float)n * Mathf.PI * 2f, a1 = (i + 1) / (float)n * Mathf.PI * 2f;
                Seg(vh, c + new Vector2(Mathf.Cos(a0), Mathf.Sin(a0)) * rad, c + new Vector2(Mathf.Cos(a1), Mathf.Sin(a1)) * rad, w, col);
            }
        }
    }

    public partial class UIManager
    {
        private SystemSchemaGraphic _inspSchema;
        private Image _inspSchemaFade;
        private readonly List<Text> _inspSchemaLabels = new List<Text>();
        private Text _inspSchemaNote;
        private RectTransform _inspHeaderRect, _inspCloseRect;
        private bool _inspSchemaMode;

        /// <summary>Высота шапки: со схемой системы — высокая, у планеты — обычная.</summary>
        private float InspHeaderH => _inspSchemaMode ? 184f : 96f;

        private static readonly string[] RomanNums = { "I", "II", "III", "IV", "V", "VI", "VII", "VIII", "IX", "X", "XI", "XII" };

        /// <summary>Слой схемы в шапке — над фоном, под звездой и надписями.</summary>
        private void BuildInspectorSchema(Transform header)
        {
            // Обрезка по шапке: дальние орбиты уходят за край кадра, как на карте
            var clip = LGBuild.Rect(header, "SchemaClip");
            clip.Stretch(1, 1, 1, 1);
            clip.gameObject.AddComponent<RectMask2D>();
            var go = new GameObject("Schema", typeof(RectTransform));
            go.transform.SetParent(clip, false);
            _inspSchema = go.AddComponent<SystemSchemaGraphic>();
            _inspSchema.raycastTarget = false;
            _inspSchema.color = Color.white;
            _inspSchema.rectTransform.Stretch();
            LG.Ignore(go);
            LG.Ignore(clip.gameObject);

            // Низ схемы уходит в чернила панели — название читается поверх орбит
            _inspSchemaFade = LGBuild.Panel(header, "SchemaFade", new Color(0.075f, 0.118f, 0.200f, 0.96f));
            _inspSchemaFade.sprite = HudArt.VFade(1f);
            _inspSchemaFade.raycastTarget = false;
            _inspSchemaFade.rectTransform.anchorMin = new Vector2(0, 0);
            _inspSchemaFade.rectTransform.anchorMax = new Vector2(1, 0);
            _inspSchemaFade.rectTransform.pivot = new Vector2(0.5f, 0);
            _inspSchemaFade.rectTransform.sizeDelta = new Vector2(0, 78);
            _inspSchemaFade.rectTransform.anchoredPosition = Vector2.zero;
            LG.Ignore(_inspSchemaFade.gameObject);

            _inspSchemaNote = LGBuild.Label(header, Loc.T("Состав не изучен"), 10, new Color(0.663f, 0.639f, 0.573f, 0.9f), TextAnchor.MiddleCenter);
            _inspSchemaNote.font = GameFont.Mono;
            _inspSchemaNote.raycastTarget = false;
            _inspSchemaNote.rectTransform.At(new Vector2(0.66f, 0.58f), new Vector2(0.5f, 0.5f), new Vector2(0, -34), new Vector2(200, 16));
            _inspSchemaNote.gameObject.SetActive(false);
            clip.gameObject.SetActive(false);
            _inspSchemaFade.gameObject.SetActive(false);
        }

        /// <summary>Схема на систему: орбиты по порядку тел, их цвета, колонии, стороны выходов коридоров.</summary>
        private void PaintInspectorSchema(StarSystem s)
        {
            if (_inspSchema == null || s == null) return;
            var sc = _inspSchema;
            sc.Orbits.Clear(); sc.Bodies.Clear(); sc.Corridors.Clear();
            var r = sc.rectTransform.rect;
            if (r.width < 10f) r = new Rect(0, 0, InspectorWidth - 2f, InspHeaderH - 2f);
            float outer = sc.OuterR(r);
            sc.Unknown = !s.IsSurveyed;

            var planets = new List<PlanetData>();
            if (s.IsSurveyed && s.Planets != null) planets.AddRange(s.Planets);
            planets.Sort((a, b) => a.OrbitRadius.CompareTo(b.OrbitRadius));
            int n = planets.Count;
            var cream = new Color(0.925f, 0.898f, 0.827f);
            for (int i = 0; i < n; i++)
            {
                var p = planets[i];
                float rx = Mathf.Lerp(46f, outer * 0.88f, n <= 1 ? 0.45f : i / (float)(n - 1));
                sc.Orbits.Add(rx);
                // Угол — стабильный для планеты; нижний левый угол кадра свободен под название
                uint h = (uint)(p.Name ?? "").GetHashCode();
                float ang = Mathf.Lerp(-35f, 195f, (h % 1000u) / 999f) * Mathf.Deg2Rad;
                Color pc = Color.Lerp(p.PlanetColor, cream, 0.22f); pc.a = 1f;
                sc.Bodies.Add(new SystemSchemaGraphic.Body
                {
                    Orbit = rx, Angle = ang, Color = pc, Colony = p.Population > 0,
                    Radius = p.Type == PlanetType.GasGiant ? 6.5f : Mathf.Clamp(2.8f + p.Size * 1.6f, 3f, 5.5f),
                });
            }

            // Коридоры — по направлениям к соседям на карте (вид сверху: X вправо, Z вверх)
            foreach (int id in s.ConnectedSystemIds)
            {
                var o = EmpireStats.GetSystem(id);
                if (o == null) continue;
                var d = o.Position - s.Position;
                if (d.x * d.x + d.z * d.z < 0.0001f) continue;
                sc.Corridors.Add(Mathf.Atan2(d.z, d.x));
            }
            sc.Rebuild();

            // Подписи орбит римскими цифрами — у каждого тела
            for (int i = 0; i < Mathf.Max(n, _inspSchemaLabels.Count); i++)
            {
                if (i >= _inspSchemaLabels.Count)
                {
                    var t = LGBuild.Label(sc.transform, "", 9, new Color(0.663f, 0.639f, 0.573f, 0.85f), TextAnchor.MiddleLeft);
                    t.font = GameFont.Mono;
                    t.raycastTarget = false;
                    _inspSchemaLabels.Add(t);
                }
                var lab = _inspSchemaLabels[i];
                bool on = i < n;
                lab.gameObject.SetActive(on);
                if (!on) continue;
                var b = sc.Bodies[i];
                var pos = sc.OnOrbit(r, b.Orbit, b.Angle) - r.min;
                lab.text = i < RomanNums.Length ? RomanNums[i] : (i + 1).ToString();
                lab.rectTransform.At(Vector2.zero, new Vector2(0, 0.5f), pos + new Vector2(b.Radius + 5f, b.Radius + 3f), new Vector2(36, 12));
            }
            if (_inspSchemaNote != null) _inspSchemaNote.gameObject.SetActive(!s.IsSurveyed);
        }

        /// <summary>
        /// Шапка в двух видах: у системы — высокая, со схемой, звезда в её центре, над названием — подпись «Звёздная система»,
        /// крестик в правом верхнем углу; у планеты — прежняя: портрет слева, название и статус справа от него.
        /// </summary>
        private void SetInspectorSchemaMode(bool on)
        {
            _inspSchemaMode = on;
            if (_inspHeaderRect != null) _inspHeaderRect.sizeDelta = new Vector2(0, InspHeaderH);
            if (_inspSchema != null) _inspSchema.transform.parent.gameObject.SetActive(on);
            if (_inspSchemaFade != null) _inspSchemaFade.gameObject.SetActive(on);
            if (!on && _inspSchemaNote != null) _inspSchemaNote.gameObject.SetActive(false);
            if (_inspBodyRect != null) _inspBodyRect.offsetMax = new Vector2(-13, -(InspHeaderH + 6f));

            if (_inspPortrait != null)
            {
                if (on)
                {
                    _inspPortrait.At(new Vector2(0.66f, 0.58f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(84, 84));
                    _inspPortrait.localScale = Vector3.one * 0.62f;
                }
                else
                {
                    _inspPortrait.At(new Vector2(0, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(52, -2), new Vector2(84, 84));
                    _inspPortrait.localScale = Vector3.one;
                }
            }
            if (_inspHeaderGlow != null)
                _inspHeaderGlow.rectTransform.At(on ? new Vector2(0.66f, 0.58f) : new Vector2(0, 0.5f), new Vector2(0.5f, 0.5f),
                    on ? Vector2.zero : new Vector2(60, 0), on ? new Vector2(300, 200) : new Vector2(460, 240));

            if (_inspTitle != null)
            {
                var t = _inspTitle.rectTransform;
                if (on)
                {
                    t.anchorMin = new Vector2(0, 0); t.anchorMax = new Vector2(1, 0);
                    t.offsetMin = new Vector2(18, 10); t.offsetMax = new Vector2(-18, 46);
                }
                else
                {
                    t.anchorMin = new Vector2(0, 0.5f); t.anchorMax = new Vector2(1, 1);
                    t.offsetMin = new Vector2(104, 2); t.offsetMax = new Vector2(-48, -8);
                }
            }
            if (_inspStatus != null)
            {
                var t = _inspStatus.rectTransform;
                if (on)
                {
                    t.anchorMin = new Vector2(0, 0); t.anchorMax = new Vector2(1, 0);
                    t.offsetMin = new Vector2(19, 46); t.offsetMax = new Vector2(-18, 62);
                    _inspStatus.font = GameFont.Mono;
                    _inspStatus.fontSize = 10;
                    _inspStatus.alignment = TextAnchor.LowerLeft;
                    _inspStatus.color = new Color(0.663f, 0.639f, 0.573f);
                    _inspStatus.Track(1.8f);
                }
                else
                {
                    t.anchorMin = new Vector2(0, 0); t.anchorMax = new Vector2(1, 0.5f);
                    t.offsetMin = new Vector2(104, 0); t.offsetMax = new Vector2(-48, -5);
                    _inspStatus.font = GameFont.Regular;
                    _inspStatus.fontSize = 12;
                    _inspStatus.alignment = TextAnchor.UpperLeft;
                    _inspStatus.color = DS.NeonCyan;
                    _inspStatus.Track(0f);
                }
            }
            if (_inspCloseRect != null)
            {
                _inspCloseRect.anchorMin = _inspCloseRect.anchorMax = on ? new Vector2(1, 1) : new Vector2(1, 0.5f);
                _inspCloseRect.pivot = on ? new Vector2(1, 1) : new Vector2(1, 0.5f);
                _inspCloseRect.anchoredPosition = on ? new Vector2(-14, -16) : new Vector2(-14, 0);
            }
        }
    }
}
