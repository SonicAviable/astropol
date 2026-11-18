using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using StellarisClone.Core;

namespace StellarisClone.Rendering
{
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
            var go = new GameObject("Schema", typeof(RectTransform), typeof(CanvasRenderer));
            go.transform.SetParent(clip, false);
            _inspSchema = go.AddComponent<SystemSchemaGraphic>();
            _inspSchema.raycastTarget = false;
            _inspSchema.color = Color.white;
            _inspSchema.rectTransform.Stretch();
            LG.Ignore(go);
            LG.Ignore(clip.gameObject);

            // Низ схемы уходит в чернила панели — название читается поверх орбит
            _inspSchemaFade = LGBuild.Panel(header, "SchemaFade", new Color(0.075f, 0.125f, 0.220f, 0.96f));   // цвет шапки (Ink2)
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
