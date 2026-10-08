using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace StellarisClone.Rendering
{
    public enum LGIcon
    {
        // Ресурсы
        Energy, Minerals, Alloys, Influence, Research,
        // Империя
        Population, Planet, Star, Starbase, Shipyard, Fleet, Ship, Leader, Globe, Calendar, Clock, Housing,
        // Категории технологий
        Weapons, Defense, Propulsion, Sensors, Industry, Reactor, Construction, Society,
        // Управление
        Pause, Play, Fast, Fastest, Close, Check, Lock, Warning, Trade, Diplomacy, Gear, Back, Menu, Trophy, Target,
        // Режимы карты
        MapSimple, MapPolitical, MapResources, MapLanes, MapExplored,
        // Меню
        Save, Load, Speaker, Monitor, Dice, Info, Power, Trash,
        // Дипломатия
        Swords, Peace, Handshake, Gift, Border, Betrayal, Siege, Wrench,
        // Модули и корпуса кораблей
        Laser, Cannon, Missile, ShieldDome, ArmorPlate, Thruster, Corvette, Frigate, Destroyer
    }

    /// <summary>
    /// Векторные иконки интерфейса, нарисованные процедурно (SDF → текстура с мипмапами).
    /// Белые силуэты — цвет задаётся Image.color. Никаких внешних ассетов и "квадратиков"
    /// на месте отсутствующих в шрифте символов.
    /// </summary>
    public static class LGIcons
    {
        private const int Res = 96;
        private static readonly Dictionary<LGIcon, Sprite> s_Cache = new Dictionary<LGIcon, Sprite>();

        // ------------------------------------------------------------------ API

        public static Sprite Get(LGIcon icon)
        {
            if (s_Cache.TryGetValue(icon, out var s) && s != null) return s;
            s = Build(Shape(icon));
            s.name = "LGIcon_" + icon;
            s_Cache[icon] = s;
            return s;
        }

        /// <summary>Создаёт Image-иконку заданного размера (по центру родителя, если не перемещать).</summary>
        public static Image Create(Transform parent, LGIcon icon, float size, Color color, string name = null)
        {
            var go = new GameObject(name ?? ("Icon_" + icon));
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.sizeDelta = new Vector2(size, size);
            var img = go.AddComponent<Image>();
            img.sprite = Get(icon);
            img.color = color;
            img.raycastTarget = false;
            img.preserveAspect = true;
            var sh = go.AddComponent<Shadow>();
            sh.effectColor = new Color(0f, 0.02f, 0.04f, 0.55f);
            sh.effectDistance = new Vector2(0f, -1f);
            return img;
        }

        /// <summary>
        /// Иконка + подпись, отцентрированные внутри родителя (кнопки, чипы).
        /// Возвращает Text, чтобы его можно было обновлять.
        /// </summary>
        public static Text IconLabel(Transform parent, LGIcon icon, string text, int fontSize, Color iconColor,
                                     Color textColor, float iconSize = 14f, bool bold = true, float spacing = 6f)
        {
            var row = new GameObject("IconLabel");
            row.transform.SetParent(parent, false);
            var rt = row.AddComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(6, 0);
            rt.offsetMax = new Vector2(-6, 0);

            var hlg = row.AddComponent<HorizontalLayoutGroup>();
            hlg.childAlignment = TextAnchor.MiddleCenter;
            hlg.spacing = spacing;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = false;

            var img = Create(row.transform, icon, iconSize, iconColor);
            var le = img.gameObject.AddComponent<LayoutElement>();
            le.preferredWidth = le.minWidth = iconSize;
            le.preferredHeight = le.minHeight = iconSize;

            var tGo = new GameObject("Label");
            tGo.transform.SetParent(row.transform, false);
            var t = tGo.AddComponent<Text>();
            t.font = bold ? GameFont.Bold : GameFont.Regular;
            t.fontSize = fontSize;
            t.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
            t.color = textColor;
            t.text = text;
            t.alignment = TextAnchor.MiddleLeft;
            t.raycastTarget = false;
            t.supportRichText = true;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            return t;
        }

        public static LGIcon ForTechCategory(StellarisClone.Core.TechCategory c)
        {
            switch (c)
            {
                case StellarisClone.Core.TechCategory.Weapons:      return LGIcon.Weapons;
                case StellarisClone.Core.TechCategory.Defense:      return LGIcon.Defense;
                case StellarisClone.Core.TechCategory.Propulsion:   return LGIcon.Propulsion;
                case StellarisClone.Core.TechCategory.Sensors:      return LGIcon.Sensors;
                case StellarisClone.Core.TechCategory.Industry:     return LGIcon.Industry;
                case StellarisClone.Core.TechCategory.Reactor:      return LGIcon.Reactor;
                case StellarisClone.Core.TechCategory.Construction: return LGIcon.Construction;
                case StellarisClone.Core.TechCategory.Society:      return LGIcon.Society;
                case StellarisClone.Core.TechCategory.Colonization: return LGIcon.Planet;
                case StellarisClone.Core.TechCategory.Doctrine:     return LGIcon.Target;
            }
            return LGIcon.Research;
        }

        // ------------------------------------------------------------------ Растеризация

        private static Sprite Build(Func<Vector2, float> sdf)
        {
            var tex = new Texture2D(Res, Res, TextureFormat.RGBA32, true)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Trilinear,
                anisoLevel = 2,
                hideFlags = HideFlags.DontSave
            };
            var px = new Color32[Res * Res];
            float pxPerUnit = Res * 0.5f;       // область иконки — [-1..1]
            for (int y = 0; y < Res; y++)
            for (int x = 0; x < Res; x++)
            {
                var p = new Vector2((x + 0.5f) / Res * 2f - 1f, (y + 0.5f) / Res * 2f - 1f);
                p *= 1.08f; // небольшой отступ от края
                float d = sdf(p) * pxPerUnit / 1.08f;
                byte a = (byte)Mathf.RoundToInt(Mathf.Clamp01(0.5f - d) * 255f);
                px[y * Res + x] = new Color32(255, 255, 255, a);
            }
            tex.SetPixels32(px);
            tex.Apply(true, true);
            var sprite = Sprite.Create(tex, new Rect(0, 0, Res, Res), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
            sprite.hideFlags = HideFlags.DontSave;
            return sprite;
        }

        // ------------------------------------------------------------------ SDF-примитивы

        private static float Circle(Vector2 p, Vector2 c, float r) => (p - c).magnitude - r;
        private static float Ring(Vector2 p, Vector2 c, float r, float w) => Mathf.Abs((p - c).magnitude - r) - w;

        private static float Segment(Vector2 p, Vector2 a, Vector2 b, float r)
        {
            Vector2 pa = p - a, ba = b - a;
            float h = Mathf.Clamp01(Vector2.Dot(pa, ba) / Vector2.Dot(ba, ba));
            return (pa - ba * h).magnitude - r;
        }

        private static float Box(Vector2 p, Vector2 c, Vector2 half, float round = 0f)
        {
            Vector2 q = new Vector2(Mathf.Abs(p.x - c.x), Mathf.Abs(p.y - c.y)) - half + new Vector2(round, round);
            return new Vector2(Mathf.Max(q.x, 0f), Mathf.Max(q.y, 0f)).magnitude + Mathf.Min(Mathf.Max(q.x, q.y), 0f) - round;
        }

        /// <summary>Выпуклый многоугольник (вершины против часовой стрелки).</summary>
        private static float Poly(Vector2 p, params Vector2[] v)
        {
            // Направление обхода определяем сами — порядок вершин не важен
            float area = 0f;
            for (int i = 0; i < v.Length; i++)
            {
                Vector2 a = v[i], b = v[(i + 1) % v.Length];
                area += a.x * b.y - b.x * a.y;
            }
            float flip = area >= 0f ? 1f : -1f;

            float d = float.MinValue;
            for (int i = 0; i < v.Length; i++)
            {
                Vector2 a = v[i], b = v[(i + 1) % v.Length];
                Vector2 e = (b - a).normalized;
                Vector2 n = new Vector2(e.y, -e.x) * flip; // внешняя нормаль
                d = Mathf.Max(d, Vector2.Dot(p - a, n));
            }
            return d;
        }

        private static float Hexagon(Vector2 p, float r)
        {
            const float kx = -0.866025404f, ky = 0.5f, kz = 0.577350269f;
            p = new Vector2(Mathf.Abs(p.x), Mathf.Abs(p.y));
            float dot = Mathf.Min(kx * p.x + ky * p.y, 0f);
            p -= 2f * dot * new Vector2(kx, ky);
            p -= new Vector2(Mathf.Clamp(p.x, -kz * r, kz * r), r);
            return p.magnitude * Mathf.Sign(p.y);
        }

        /// <summary>Пятиконечная звезда (Inigo Quilez). rf — отношение внутреннего радиуса.</summary>
        private static float Star(Vector2 p, float r, float rf)
        {
            Vector2 k1 = new Vector2(0.809016994375f, -0.587785252292f);
            Vector2 k2 = new Vector2(-k1.x, k1.y);
            p.x = Mathf.Abs(p.x);
            p -= 2f * Mathf.Max(Vector2.Dot(k1, p), 0f) * k1;
            p -= 2f * Mathf.Max(Vector2.Dot(k2, p), 0f) * k2;
            p.x = Mathf.Abs(p.x);
            p.y -= r;
            Vector2 ba = rf * new Vector2(-k1.y, k1.x) - new Vector2(0f, 1f);
            float h = Mathf.Clamp(Vector2.Dot(p, ba) / Vector2.Dot(ba, ba), 0f, r);
            return (p - ba * h).magnitude * Mathf.Sign(p.y * ba.x - p.x * ba.y);
        }

        private static float EllipseRing(Vector2 p, float a, float b, float rotDeg, float w)
        {
            float r = rotDeg * Mathf.Deg2Rad;
            float c = Mathf.Cos(r), s = Mathf.Sin(r);
            Vector2 q = new Vector2(c * p.x + s * p.y, -s * p.x + c * p.y);
            float k = new Vector2(q.x / a, q.y / b).magnitude;
            return Mathf.Abs((k - 1f) * Mathf.Min(a, b)) - w;
        }

        private static float U(float a, float b) => Mathf.Min(a, b);
        private static float U(params float[] v) { float m = v[0]; for (int i = 1; i < v.Length; i++) m = Mathf.Min(m, v[i]); return m; }
        private static float Sub(float a, float b) => Mathf.Max(a, -b);
        private static Vector2 V(float x, float y) => new Vector2(x, y);
        private static Vector2 Rot(Vector2 p, float deg)
        {
            float r = deg * Mathf.Deg2Rad, c = Mathf.Cos(r), s = Mathf.Sin(r);
            return new Vector2(c * p.x - s * p.y, s * p.x + c * p.y);
        }

        private static float Gear(Vector2 p, float r, float hole, int teeth)
        {
            float d = Circle(p, Vector2.zero, r);
            for (int i = 0; i < teeth; i++)
            {
                Vector2 q = Rot(p, -360f / teeth * i);
                d = U(d, Box(q, V(0, r + 0.12f), V(0.12f, 0.16f), 0.04f));
            }
            return Sub(d, Circle(p, Vector2.zero, hole));
        }

        private static float Triangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c) => Poly(p, a, b, c);

        // ------------------------------------------------------------------ Формы

        private static Func<Vector2, float> Shape(LGIcon icon)
        {
            switch (icon)
            {
                case LGIcon.Energy:
                    return p => U(
                        Poly(p, V(0.34f, 0.98f), V(-0.52f, -0.08f), V(0.14f, -0.08f)),
                        Poly(p, V(-0.34f, -0.98f), V(0.52f, 0.08f), V(-0.14f, 0.08f)));

                case LGIcon.Minerals:
                    return p =>
                    {
                        float d = Poly(p, V(0f, -0.95f), V(0.72f, 0.22f), V(0.42f, 0.78f), V(-0.42f, 0.78f), V(-0.72f, 0.22f));
                        d = Sub(d, Segment(p, V(-0.72f, 0.22f), V(0.72f, 0.22f), 0.045f));
                        d = Sub(d, Segment(p, V(-0.2f, 0.22f), V(0f, -0.95f), 0.04f));
                        d = Sub(d, Segment(p, V(0.2f, 0.22f), V(0f, -0.95f), 0.04f));
                        return d;
                    };

                case LGIcon.Alloys:
                    return p => U(Sub(Hexagon(p, 0.86f), Hexagon(p, 0.58f)), Hexagon(p, 0.32f));

                case LGIcon.Influence:
                    return p => Star(p - V(0, -0.04f), 0.98f, 0.42f) - 0.02f;

                case LGIcon.Research:
                    return p => U(
                        EllipseRing(p, 0.92f, 0.34f, 0f, 0.055f),
                        EllipseRing(p, 0.92f, 0.34f, 60f, 0.055f),
                        EllipseRing(p, 0.92f, 0.34f, 120f, 0.055f),
                        Circle(p, Vector2.zero, 0.17f));

                case LGIcon.Population:
                    return p => U(Circle(p, V(0, 0.42f), 0.3f),
                                  Sub(Box(p, V(0, -0.52f), V(0.62f, 0.4f), 0.36f), Box(p, V(0, -1.05f), V(1f, 0.1f))));

                case LGIcon.Society:
                    return p => U(
                        Circle(p, V(0f, 0.38f), 0.22f),
                        Sub(Box(p, V(0f, -0.28f), V(0.38f, 0.32f), 0.28f), Box(p, V(0, -0.7f), V(1f, 0.1f))),
                        Circle(p, V(-0.6f, 0.22f), 0.16f),
                        Sub(Box(p, V(-0.6f, -0.32f), V(0.28f, 0.26f), 0.22f), Box(p, V(0, -0.68f), V(1f, 0.1f))),
                        Circle(p, V(0.6f, 0.22f), 0.16f),
                        Sub(Box(p, V(0.6f, -0.32f), V(0.28f, 0.26f), 0.22f), Box(p, V(0, -0.68f), V(1f, 0.1f))));

                case LGIcon.Planet:
                    return p =>
                    {
                        float planet = Circle(p, Vector2.zero, 0.5f);
                        float ring = EllipseRing(p, 0.98f, 0.26f, -18f, 0.06f);
                        float qy = Rot(p, 18f).y;
                        float ringFront = Mathf.Max(ring, qy);                                   // нижняя дуга — поверх планеты
                        float ringBack = Mathf.Max(Mathf.Max(ring, -qy), -Circle(p, Vector2.zero, 0.58f)); // верхняя — за планетой
                        float body = Sub(planet, Mathf.Max(EllipseRing(p, 0.98f, 0.26f, -18f, 0.14f), qy)); // зазор у кольца
                        return U(body, ringFront, ringBack);
                    };

                case LGIcon.Star:
                    return p =>
                    {
                        float d = Circle(p, Vector2.zero, 0.38f);
                        for (int i = 0; i < 8; i++)
                        {
                            Vector2 dir = Rot(V(0, 1), 45f * i);
                            float len = i % 2 == 0 ? 0.95f : 0.72f;
                            d = U(d, Segment(p, dir * 0.52f, dir * len, 0.07f));
                        }
                        return d;
                    };

                case LGIcon.Starbase:
                    return p => U(Sub(Hexagon(p, 0.9f), Hexagon(p, 0.72f)), Circle(p, Vector2.zero, 0.24f),
                                  Segment(p, V(0, 0.24f), V(0, 0.72f), 0.06f), Segment(p, V(0, -0.24f), V(0, -0.72f), 0.06f));

                case LGIcon.Shipyard:
                case LGIcon.Gear:
                case LGIcon.Industry:
                    return p => Gear(p, 0.6f, 0.24f, 8);

                case LGIcon.Fleet:
                    return p => U(
                        U(Triangle(p, V(0f, 0.95f), V(-0.32f, 0.05f), V(0.32f, 0.05f)), Box(p, V(0, 0.12f), V(0.14f, 0.2f))),
                        U(Triangle(p - V(-0.55f, -0.45f), V(0f, 0.55f), V(-0.22f, -0.1f), V(0.22f, -0.1f))),
                        U(Triangle(p - V(0.55f, -0.45f), V(0f, 0.55f), V(-0.22f, -0.1f), V(0.22f, -0.1f))));

                case LGIcon.Ship:
                    return p => U(Triangle(p, V(0f, 0.95f), V(-0.62f, -0.75f), V(0f, -0.38f)),
                                  Triangle(p, V(0f, 0.95f), V(0f, -0.38f), V(0.62f, -0.75f)));

                case LGIcon.Leader:
                    return p => U(
                        Box(p, V(0, -0.55f), V(0.72f, 0.14f), 0.04f),
                        Triangle(p, V(-0.72f, -0.4f), V(-0.72f, 0.55f), V(-0.2f, -0.4f)),
                        Triangle(p, V(-0.36f, -0.4f), V(0f, 0.72f), V(0.36f, -0.4f)),
                        Triangle(p, V(0.2f, -0.4f), V(0.72f, 0.55f), V(0.72f, -0.4f)),
                        Circle(p, V(-0.72f, 0.62f), 0.1f), Circle(p, V(0f, 0.8f), 0.1f), Circle(p, V(0.72f, 0.62f), 0.1f));

                case LGIcon.Globe:
                    return p => U(Ring(p, Vector2.zero, 0.86f, 0.07f),
                                  EllipseRing(p, 0.38f, 0.86f, 0f, 0.06f),
                                  Mathf.Max(Box(p, Vector2.zero, V(0.86f, 0.05f)), Circle(p, Vector2.zero, 0.86f)),
                                  Mathf.Max(Box(p, V(0, 0.42f), V(0.8f, 0.045f)), Circle(p, Vector2.zero, 0.86f)),
                                  Mathf.Max(Box(p, V(0, -0.42f), V(0.8f, 0.045f)), Circle(p, Vector2.zero, 0.86f)));

                case LGIcon.Calendar:
                    return p => U(Sub(Box(p, V(0, -0.08f), V(0.82f, 0.72f), 0.14f), Box(p, V(0, -0.2f), V(0.66f, 0.46f), 0.06f)),
                                  Box(p, V(-0.42f, 0.72f), V(0.08f, 0.2f), 0.06f), Box(p, V(0.42f, 0.72f), V(0.08f, 0.2f), 0.06f),
                                  Box(p, V(-0.3f, -0.08f), V(0.12f, 0.1f)), Box(p, V(0.05f, -0.08f), V(0.12f, 0.1f)),
                                  Box(p, V(-0.3f, -0.4f), V(0.12f, 0.1f)));

                case LGIcon.Clock:
                    return p => U(Ring(p, Vector2.zero, 0.82f, 0.09f),
                                  Segment(p, Vector2.zero, V(0, 0.52f), 0.08f),
                                  Segment(p, Vector2.zero, V(0.4f, -0.18f), 0.08f));

                case LGIcon.Housing:
                    return p => U(Triangle(p, V(-0.9f, 0.05f), V(0f, 0.9f), V(0.9f, 0.05f)),
                                  Sub(Box(p, V(0, -0.42f), V(0.62f, 0.48f)), Box(p, V(0, -0.6f), V(0.18f, 0.32f))));

                case LGIcon.Weapons:
                case LGIcon.Target:
                    return p => U(Ring(p, Vector2.zero, 0.58f, 0.08f),
                                  Segment(p, V(0, 0.3f), V(0, 0.98f), 0.07f), Segment(p, V(0, -0.3f), V(0, -0.98f), 0.07f),
                                  Segment(p, V(0.3f, 0), V(0.98f, 0), 0.07f), Segment(p, V(-0.3f, 0), V(-0.98f, 0), 0.07f),
                                  Circle(p, Vector2.zero, 0.12f));

                case LGIcon.Defense:
                    return p =>
                    {
                        float outer = Poly(p, V(-0.72f, 0.82f), V(-0.72f, 0.05f), V(0f, -0.95f), V(0.72f, 0.05f), V(0.72f, 0.82f)) - 0.03f;
                        float inner = Poly(p, V(-0.52f, 0.64f), V(-0.52f, 0.1f), V(0f, -0.66f), V(0.52f, 0.1f), V(0.52f, 0.64f));
                        float core = Poly(p, V(-0.3f, 0.46f), V(-0.3f, 0.12f), V(0f, -0.34f), V(0.3f, 0.12f), V(0.3f, 0.46f));
                        return U(Sub(outer, inner), core);
                    };

                case LGIcon.Propulsion:
                    return p => U(
                        Segment(p, V(-0.66f, 0.02f), V(0f, 0.62f), 0.12f), Segment(p, V(0f, 0.62f), V(0.66f, 0.02f), 0.12f),
                        Segment(p, V(-0.66f, -0.52f), V(0f, 0.08f), 0.12f), Segment(p, V(0f, 0.08f), V(0.66f, -0.52f), 0.12f));

                case LGIcon.Sensors:
                    return p =>
                    {
                        Vector2 o = V(-0.62f, -0.62f);
                        float quad = Mathf.Max(-(p.x - o.x), -(p.y - o.y));
                        return U(Circle(p, o, 0.16f),
                                 Mathf.Max(Ring(p, o, 0.62f, 0.085f), quad),
                                 Mathf.Max(Ring(p, o, 1.05f, 0.085f), quad),
                                 Mathf.Max(Ring(p, o, 1.48f, 0.085f), Mathf.Max(quad, Circle(p, Vector2.zero, 0.98f))));
                    };

                case LGIcon.Reactor:
                    return p =>
                    {
                        float d = U(Circle(p, Vector2.zero, 0.24f), Ring(p, Vector2.zero, 0.52f, 0.07f));
                        for (int i = 0; i < 3; i++)
                        {
                            Vector2 dir = Rot(V(0, 1), 120f * i);
                            d = U(d, Circle(p, dir * 0.84f, 0.13f), Segment(p, dir * 0.6f, dir * 0.72f, 0.05f));
                        }
                        return d;
                    };

                case LGIcon.Construction:
                    return p =>
                    {
                        float handle = Segment(p, V(-0.62f, -0.62f), V(0.25f, 0.25f), 0.13f);
                        float head = Sub(Ring(p, V(0.42f, 0.42f), 0.3f, 0.13f), Box(Rot(p - V(0.6f, 0.6f), -45f), Vector2.zero, V(0.13f, 0.4f)));
                        return U(handle, head);
                    };

                case LGIcon.Pause:
                    return p => U(Box(p, V(-0.32f, 0), V(0.16f, 0.66f), 0.06f), Box(p, V(0.32f, 0), V(0.16f, 0.66f), 0.06f));

                case LGIcon.Play:
                    return p => Triangle(p, V(-0.5f, -0.7f), V(0.72f, 0f), V(-0.5f, 0.7f)) - 0.04f;

                case LGIcon.Fast:
                    return p => U(Triangle(p, V(-0.9f, -0.6f), V(0.05f, 0f), V(-0.9f, 0.6f)),
                                  Triangle(p, V(-0.05f, -0.6f), V(0.9f, 0f), V(-0.05f, 0.6f))) - 0.03f;

                case LGIcon.Fastest:
                    return p => U(Triangle(p, V(-0.98f, -0.5f), V(-0.3f, 0f), V(-0.98f, 0.5f)),
                                  Triangle(p, V(-0.36f, -0.5f), V(0.32f, 0f), V(-0.36f, 0.5f)),
                                  Triangle(p, V(0.26f, -0.5f), V(0.94f, 0f), V(0.26f, 0.5f))) - 0.03f;

                case LGIcon.Close:
                    return p => U(Segment(p, V(-0.6f, -0.6f), V(0.6f, 0.6f), 0.12f), Segment(p, V(-0.6f, 0.6f), V(0.6f, -0.6f), 0.12f));

                case LGIcon.Check:
                    return p => U(Segment(p, V(-0.7f, 0.0f), V(-0.2f, -0.52f), 0.13f), Segment(p, V(-0.2f, -0.52f), V(0.72f, 0.56f), 0.13f));

                case LGIcon.Lock:
                    return p => U(Sub(Box(p, V(0, -0.32f), V(0.66f, 0.5f), 0.12f), Circle(p, V(0, -0.26f), 0.12f)),
                                  Mathf.Max(Ring(p, V(0, 0.28f), 0.4f, 0.1f), -(p.y - 0.28f)),
                                  Box(p, V(-0.4f, 0.2f), V(0.1f, 0.1f)), Box(p, V(0.4f, 0.2f), V(0.1f, 0.1f)));

                case LGIcon.Warning:
                    return p => U(Sub(Triangle(p, V(-0.95f, -0.8f), V(0.95f, -0.8f), V(0f, 0.9f)) - 0.04f,
                                      Triangle(p, V(-0.66f, -0.6f), V(0.66f, -0.6f), V(0f, 0.55f))),
                                  Segment(p, V(0, -0.05f), V(0, 0.28f), 0.08f), Circle(p, V(0, -0.36f), 0.09f));

                case LGIcon.Trade:
                    return p => U(
                        Segment(p, V(-0.75f, 0.35f), V(0.6f, 0.35f), 0.08f), Triangle(p, V(0.45f, 0.65f), V(0.45f, 0.05f), V(0.95f, 0.35f)),
                        Segment(p, V(0.75f, -0.35f), V(-0.6f, -0.35f), 0.08f), Triangle(p, V(-0.45f, -0.05f), V(-0.45f, -0.65f), V(-0.95f, -0.35f)));

                case LGIcon.Diplomacy:
                    return p => U(Ring(p, V(-0.32f, 0f), 0.5f, 0.1f), Ring(p, V(0.32f, 0f), 0.5f, 0.1f));

                case LGIcon.Back:
                    return p => U(Segment(p, V(0.45f, 0.7f), V(-0.3f, 0f), 0.13f), Segment(p, V(-0.3f, 0f), V(0.45f, -0.7f), 0.13f));

                case LGIcon.Menu:
                    return p => U(Box(p, V(0, 0.55f), V(0.8f, 0.1f), 0.08f), Box(p, V(0, 0f), V(0.8f, 0.1f), 0.08f), Box(p, V(0, -0.55f), V(0.8f, 0.1f), 0.08f));

                case LGIcon.Trophy:
                    return p => U(Sub(Box(p, V(0, 0.35f), V(0.5f, 0.52f), 0.4f), Box(p, V(0, 1.0f), V(1f, 0.15f))),
                                  Mathf.Max(Ring(p, V(-0.55f, 0.42f), 0.22f, 0.07f), p.x + 0.45f),
                                  Mathf.Max(Ring(p, V(0.55f, 0.42f), 0.22f, 0.07f), -(p.x - 0.45f)),
                                  Box(p, V(0, -0.38f), V(0.1f, 0.25f)), Box(p, V(0, -0.72f), V(0.45f, 0.1f), 0.04f));

                case LGIcon.MapSimple:
                    return p => Star(p - V(0, -0.04f), 0.95f, 0.4f) - 0.02f;

                case LGIcon.MapPolitical:
                    return p => U(Segment(p, V(-0.6f, -0.9f), V(-0.6f, 0.85f), 0.08f),
                                  Poly(p, V(-0.55f, 0.85f), V(-0.55f, 0.05f), V(0.8f, 0.2f), V(0.8f, 0.85f)));

                case LGIcon.MapResources:
                    return p => U(Sub(Hexagon(p, 0.86f), Hexagon(p, 0.6f)), Hexagon(p, 0.34f));

                case LGIcon.MapLanes:
                    return p => U(Circle(p, V(-0.65f, -0.5f), 0.2f), Circle(p, V(0.0f, 0.6f), 0.2f), Circle(p, V(0.68f, -0.32f), 0.2f),
                                  Segment(p, V(-0.65f, -0.5f), V(0.0f, 0.6f), 0.06f), Segment(p, V(0.0f, 0.6f), V(0.68f, -0.32f), 0.06f),
                                  Segment(p, V(-0.65f, -0.5f), V(0.68f, -0.32f), 0.06f));

                case LGIcon.MapExplored:
                    return p =>
                    {
                        float lens = Mathf.Max(Circle(p, V(0, -0.62f), 1.0f), Circle(p, V(0, 0.62f), 1.0f));
                        float inner = Mathf.Max(Circle(p, V(0, -0.62f), 0.86f), Circle(p, V(0, 0.62f), 0.86f));
                        return U(Sub(lens, inner), Circle(p, Vector2.zero, 0.26f));
                    };

                case LGIcon.Save:
                    return p =>
                    {
                        float body = Poly(p, V(-0.8f, -0.8f), V(0.8f, -0.8f), V(0.8f, 0.48f), V(0.48f, 0.8f), V(-0.8f, 0.8f)) - 0.04f;
                        body = Sub(body, Box(p, V(0f, -0.34f), V(0.5f, 0.3f), 0.05f));
                        body = Sub(body, Box(p, V(-0.08f, 0.5f), V(0.38f, 0.2f), 0.04f));
                        return U(body, Box(p, V(0.12f, 0.5f), V(0.08f, 0.13f)));
                    };

                case LGIcon.Load:
                    return p =>
                    {
                        float back = U(Box(p, V(0f, -0.12f), V(0.86f, 0.6f), 0.1f), Box(p, V(-0.46f, 0.5f), V(0.36f, 0.16f), 0.08f));
                        return Sub(back, Box(p, V(0.06f, 0.28f), V(0.8f, 0.045f)));
                    };

                case LGIcon.Speaker:
                    return p => U(
                        Box(p, V(-0.62f, 0f), V(0.18f, 0.26f), 0.05f),
                        Poly(p, V(-0.46f, 0.26f), V(0.0f, 0.68f), V(0.0f, -0.68f), V(-0.46f, -0.26f)),
                        Mathf.Max(Ring(p, V(0.0f, 0f), 0.42f, 0.07f), -(p.x - 0.24f)),
                        Mathf.Max(Ring(p, V(0.0f, 0f), 0.74f, 0.07f), -(p.x - 0.38f)));

                case LGIcon.Monitor:
                    return p => U(
                        Sub(Box(p, V(0f, 0.18f), V(0.9f, 0.6f), 0.1f), Box(p, V(0f, 0.2f), V(0.74f, 0.44f), 0.04f)),
                        Box(p, V(0f, -0.6f), V(0.1f, 0.16f)),
                        Box(p, V(0f, -0.78f), V(0.42f, 0.07f), 0.05f));

                case LGIcon.Dice:
                    return p => Sub(Box(p, Vector2.zero, V(0.8f, 0.8f), 0.2f),
                                    U(Circle(p, Vector2.zero, 0.13f),
                                      Circle(p, V(-0.4f, -0.4f), 0.13f), Circle(p, V(0.4f, 0.4f), 0.13f),
                                      Circle(p, V(-0.4f, 0.4f), 0.13f), Circle(p, V(0.4f, -0.4f), 0.13f)));

                case LGIcon.Info:
                    return p => Sub(Circle(p, Vector2.zero, 0.9f),
                                    U(Box(p, V(0f, -0.18f), V(0.1f, 0.36f), 0.05f), Circle(p, V(0f, 0.42f), 0.12f)));

                case LGIcon.Power:
                    return p => U(Sub(Ring(p, V(0f, -0.08f), 0.66f, 0.1f), Box(p, V(0f, 0.6f), V(0.24f, 0.42f))),
                                  Segment(p, V(0f, 0.1f), V(0f, 0.88f), 0.1f));

                case LGIcon.Trash:
                    return p => U(
                        Sub(Poly(p, V(-0.56f, 0.42f), V(0.56f, 0.42f), V(0.42f, -0.88f), V(-0.42f, -0.88f)),
                            U(Box(p, V(-0.2f, -0.22f), V(0.05f, 0.42f)), Box(p, V(0.2f, -0.22f), V(0.05f, 0.42f)))),
                        Box(p, V(0f, 0.6f), V(0.76f, 0.08f), 0.04f),
                        Box(p, V(0f, 0.76f), V(0.22f, 0.09f), 0.04f));

                case LGIcon.Swords:
                    return p => U(
                        Segment(p, V(-0.72f, -0.72f), V(0.62f, 0.62f), 0.09f),
                        Segment(p, V(0.72f, -0.72f), V(-0.62f, 0.62f), 0.09f),
                        Triangle(p, V(0.52f, 0.72f), V(0.85f, 0.85f), V(0.72f, 0.52f)),
                        Triangle(p, V(-0.52f, 0.72f), V(-0.85f, 0.85f), V(-0.72f, 0.52f)),
                        Segment(p, V(-0.78f, -0.42f), V(-0.42f, -0.78f), 0.08f),
                        Segment(p, V(0.78f, -0.42f), V(0.42f, -0.78f), 0.08f));

                case LGIcon.Peace:
                    return p => U(Ring(p, Vector2.zero, 0.78f, 0.1f),
                                  Mathf.Max(Segment(p, V(0f, 0.78f), V(0f, -0.78f), 0.09f), Circle(p, Vector2.zero, 0.8f)),
                                  Segment(p, V(0f, -0.05f), V(-0.55f, -0.55f), 0.09f),
                                  Segment(p, V(0f, -0.05f), V(0.55f, -0.55f), 0.09f));

                case LGIcon.Handshake:
                    return p => U(
                        Box(Rot(p - V(-0.38f, 0.02f), 28f), Vector2.zero, V(0.46f, 0.17f), 0.12f),
                        Box(Rot(p - V(0.38f, 0.02f), -28f), Vector2.zero, V(0.46f, 0.17f), 0.12f),
                        Box(p, V(0f, -0.12f), V(0.3f, 0.2f), 0.14f),
                        Box(p, V(-0.86f, 0.3f), V(0.1f, 0.3f), 0.04f),
                        Box(p, V(0.86f, 0.3f), V(0.1f, 0.3f), 0.04f));

                case LGIcon.Gift:
                    return p => U(
                        Sub(Box(p, V(0f, -0.3f), V(0.7f, 0.52f), 0.08f), Box(p, V(0f, -0.3f), V(0.08f, 0.6f))),
                        Sub(Box(p, V(0f, 0.36f), V(0.82f, 0.16f), 0.06f), Box(p, V(0f, 0.36f), V(0.08f, 0.3f))),
                        Ring(p, V(-0.24f, 0.7f), 0.18f, 0.07f), Ring(p, V(0.24f, 0.7f), 0.18f, 0.07f));

                case LGIcon.Border:
                    return p => U(
                        Segment(p, V(-0.9f, -0.8f), V(-0.9f, 0.8f), 0.06f),
                        Segment(p, V(0.9f, -0.8f), V(0.9f, 0.8f), 0.06f),
                        Box(p, V(0f, 0.5f), V(0.1f, 0.24f), 0.04f),
                        Box(p, V(0f, 0f), V(0.1f, 0.14f), 0.04f),
                        Box(p, V(0f, -0.5f), V(0.1f, 0.24f), 0.04f),
                        Triangle(p, V(-0.75f, 0.25f), V(-0.25f, 0f), V(-0.75f, -0.25f)),
                        Triangle(p, V(0.75f, 0.25f), V(0.25f, 0f), V(0.75f, -0.25f)));

                case LGIcon.Betrayal:
                    return p =>
                    {
                        float heart = U(Circle(p, V(-0.36f, 0.3f), 0.4f), Circle(p, V(0.36f, 0.3f), 0.4f),
                                        Triangle(p, V(-0.76f, 0.2f), V(0.76f, 0.2f), V(0f, -0.85f)));
                        float crack = U(Segment(p, V(0.05f, 0.75f), V(-0.12f, 0.25f), 0.07f),
                                        Segment(p, V(-0.12f, 0.25f), V(0.14f, -0.1f), 0.07f),
                                        Segment(p, V(0.14f, -0.1f), V(-0.04f, -0.9f), 0.07f));
                        return Sub(heart, crack);
                    };

                case LGIcon.Siege:
                    return p => U(Ring(p, Vector2.zero, 0.42f, 0.08f),
                                  Triangle(p, V(0f, 0.98f), V(-0.18f, 0.6f), V(0.18f, 0.6f)),
                                  Triangle(p, V(0f, -0.98f), V(-0.18f, -0.6f), V(0.18f, -0.6f)),
                                  Triangle(p, V(0.98f, 0f), V(0.6f, -0.18f), V(0.6f, 0.18f)),
                                  Triangle(p, V(-0.98f, 0f), V(-0.6f, -0.18f), V(-0.6f, 0.18f)),
                                  Circle(p, Vector2.zero, 0.14f));

                case LGIcon.Wrench:
                    return p =>
                    {
                        float handle = Segment(p, V(-0.66f, -0.66f), V(0.2f, 0.2f), 0.13f);
                        float head = Sub(Circle(p, V(0.42f, 0.42f), 0.38f), Box(Rot(p - V(0.58f, 0.58f), -45f), Vector2.zero, V(0.12f, 0.36f)));
                        return U(handle, head);
                    };

                case LGIcon.Laser:
                    return p =>
                    {
                        Vector2 q = Rot(p, -35f);
                        return U(Box(q, V(-0.62f, 0f), V(0.26f, 0.2f), 0.07f),
                                 Segment(q, V(-0.36f, 0f), V(0.62f, 0f), 0.075f),
                                 Ring(q, V(0.72f, 0f), 0.2f, 0.055f),
                                 Circle(q, V(0.72f, 0f), 0.08f));
                    };

                case LGIcon.Cannon:
                    return p => U(
                        Box(p, V(-0.3f, -0.66f), V(0.56f, 0.16f), 0.06f),
                        Mathf.Max(Circle(p, V(-0.3f, -0.5f), 0.36f), -(p.y + 0.5f)),
                        Segment(p, V(-0.24f, -0.3f), V(0.5f, 0.42f), 0.12f),
                        Circle(p, V(0.74f, 0.68f), 0.1f),
                        Circle(p, V(0.9f, 0.32f), 0.08f));

                case LGIcon.Missile:
                    return p =>
                    {
                        Vector2 q = Rot(p, -45f);
                        return U(Segment(q, V(-0.62f, 0f), V(0.4f, 0f), 0.16f),
                                 Triangle(q, V(0.38f, 0.16f), V(0.38f, -0.16f), V(0.94f, 0f)),
                                 Triangle(q, V(-0.3f, 0.12f), V(-0.68f, 0.12f), V(-0.76f, 0.48f)),
                                 Triangle(q, V(-0.3f, -0.12f), V(-0.68f, -0.12f), V(-0.76f, -0.48f)));
                    };

                case LGIcon.ShieldDome:
                    return p => U(
                        Mathf.Max(Ring(p, V(0f, -0.55f), 1.0f, 0.08f), -(p.y + 0.55f)),
                        Mathf.Max(Ring(p, V(0f, -0.55f), 0.68f, 0.07f), -(p.y + 0.55f)),
                        Triangle(p, V(0f, 0.08f), V(-0.3f, -0.62f), V(0.3f, -0.62f)));

                case LGIcon.ArmorPlate:
                    return p => Sub(Hexagon(p, 0.86f),
                                    U(Segment(p, Vector2.zero, V(0f, 1f), 0.05f),
                                      Segment(p, Vector2.zero, Rot(V(0f, 1f), 120f), 0.05f),
                                      Segment(p, Vector2.zero, Rot(V(0f, 1f), 240f), 0.05f),
                                      Circle(p, Vector2.zero, 0.16f)));

                case LGIcon.Thruster:
                    return p => U(
                        Poly(p, V(-0.3f, 0.9f), V(0.3f, 0.9f), V(0.52f, 0.28f), V(-0.52f, 0.28f)),
                        Circle(p, V(0f, 0f), 0.26f),
                        Triangle(p, V(-0.26f, 0f), V(0.26f, 0f), V(0f, -0.96f)),
                        Triangle(p, V(-0.5f, 0.12f), V(-0.32f, 0.12f), V(-0.46f, -0.58f)),
                        Triangle(p, V(0.5f, 0.12f), V(0.32f, 0.12f), V(0.46f, -0.58f)));

                case LGIcon.Corvette:
                    return p => Sub(Triangle(p, V(0f, 0.92f), V(-0.46f, -0.66f), V(0.46f, -0.66f)),
                                    Triangle(p, V(0f, -0.26f), V(-0.22f, -0.7f), V(0.22f, -0.7f)));

                case LGIcon.Frigate:
                    return p => U(
                        Poly(p, V(0f, 0.96f), V(0.24f, 0.4f), V(0.24f, -0.72f), V(-0.24f, -0.72f), V(-0.24f, 0.4f)),
                        Box(p, V(-0.58f, -0.22f), V(0.13f, 0.42f), 0.08f),
                        Box(p, V(0.58f, -0.22f), V(0.13f, 0.42f), 0.08f),
                        Box(p, V(0f, -0.12f), V(0.5f, 0.06f)));

                case LGIcon.Destroyer:
                    return p =>
                    {
                        float hull = Poly(p, V(0f, 0.98f), V(0.3f, 0.3f), V(0.6f, -0.5f), V(0.6f, -0.78f),
                                          V(-0.6f, -0.78f), V(-0.6f, -0.5f), V(-0.3f, 0.3f));
                        hull = Sub(hull, U(Triangle(p, V(0f, -0.36f), V(-0.26f, -0.84f), V(0.26f, -0.84f)),
                                           Segment(p, V(0f, 0.55f), V(0f, -0.1f), 0.045f)));
                        return U(hull, Box(p, V(-0.76f, -0.3f), V(0.07f, 0.34f), 0.04f),
                                       Box(p, V(0.76f, -0.3f), V(0.07f, 0.34f), 0.04f));
                    };
            }
            return p => Circle(p, Vector2.zero, 0.6f);
        }
    }
}
