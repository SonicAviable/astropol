using System;
using System.Collections.Generic;
using UnityEngine;

namespace StellarisClone.Core
{
    /// <summary>
    /// Портреты лидеров (учёных, адмиралов, губернаторов) из Resources/Leaders/Pool.
    /// Портрет подбирается по классу и полу (по имени) при первом показе и запоминается в лидере —
    /// так он попадает в сохранение. Среди лидеров одной империи стараемся не повторять лица.
    /// Если подходящего портрета нет, лидер остаётся без него и интерфейс рисует иконку класса.
    /// </summary>
    public static class LeaderFaces
    {
        public sealed class Face
        {
            public string Key;
            public LeaderClass Class;
            public bool Female;
            /// <summary>Квадрат вокруг лица в пикселях исходника 1342×2000: центр и сторона.</summary>
            public Vector2 Center;
            public float Size;

            private Texture2D _tex;
            public Texture2D Texture => _tex != null ? _tex : (_tex = Resources.Load<Texture2D>("Leaders/Pool/" + Key));
        }

        private const float SrcW = 1342f, SrcH = 2000f;

        private static readonly Face[] All =
        {
            new Face { Key = "sci_1", Class = LeaderClass.Scientist, Female = false, Center = new Vector2(690, 570), Size = 700 },
            new Face { Key = "sci_2", Class = LeaderClass.Scientist, Female = true,  Center = new Vector2(620, 545), Size = 620 },
            new Face { Key = "sci_3", Class = LeaderClass.Scientist, Female = false, Center = new Vector2(680, 610), Size = 760 },
            new Face { Key = "sci_4", Class = LeaderClass.Scientist, Female = true,  Center = new Vector2(585, 590), Size = 640 },
            new Face { Key = "adm_1", Class = LeaderClass.Admiral,   Female = false, Center = new Vector2(680, 650), Size = 760 },
        };

        /// <summary>Женские имена из генератора лидеров (остальные считаются мужскими).</summary>
        private static readonly HashSet<string> FemaleNames = new HashSet<string>
        {
            "Аира", "Лира", "Тесса", "Мира", "Ния", "Рейна", "Эла", "Нова", "Селена", "Иса", "Вега"
        };

        public static bool IsFemale(Leader l)
        {
            if (l == null || string.IsNullOrEmpty(l.Name)) return false;
            int sp = l.Name.IndexOf(' ');
            return FemaleNames.Contains(sp > 0 ? l.Name.Substring(0, sp) : l.Name);
        }

        private static Face Find(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            foreach (var f in All) if (f.Key == key) return f;
            return null;
        }

        /// <summary>Портрет лидера (подбирается и запоминается при первом вызове). null — портрета нет.</summary>
        public static Face For(Leader l)
        {
            if (l == null) return null;
            var face = Find(l.Portrait);
            if (face != null && face.Class == l.Class && face.Texture != null) return face;

            face = Pick(l);
            l.Portrait = face?.Key;
            return face;
        }

        private static Face Pick(Leader l)
        {
            bool female = IsFemale(l);
            var used = new Dictionary<string, int>();
            var lm = LeaderManager.Instance;
            if (lm != null)
            {
                void Count(IReadOnlyList<Leader> list)
                {
                    foreach (var o in list)
                        if (o != l && o.Owner == l.Owner && !string.IsNullOrEmpty(o.Portrait))
                            used[o.Portrait] = used.TryGetValue(o.Portrait, out int n) ? n + 1 : 1;
                }
                Count(lm.All);
                Count(lm.Candidates);
            }

            // Наименее занятые лица подходящего класса и пола; среди равных — по зерну лидера
            var best = new List<Face>();
            int bestUse = int.MaxValue;
            foreach (var f in All)
            {
                if (f.Class != l.Class || f.Female != female || f.Texture == null) continue;
                int n = used.TryGetValue(f.Key, out int u) ? u : 0;
                if (n < bestUse) { bestUse = n; best.Clear(); }
                if (n == bestUse) best.Add(f);
            }
            if (best.Count == 0) return null;
            return best[Math.Abs(l.Seed + l.Id * 7919) % best.Count];
        }

        /// <summary>uv-прямоугольник квадратной вырезки вокруг лица (zoom &gt; 1 — крупнее).</summary>
        public static Rect FaceUv(Face f, float zoom = 1f)
        {
            float side = f.Size / Mathf.Max(0.01f, zoom);
            float w = side / SrcW, h = side / SrcH;
            float u = Mathf.Clamp(f.Center.x / SrcW - w * 0.5f, 0f, 1f - w);
            float v = Mathf.Clamp(1f - f.Center.y / SrcH - h * 0.5f, 0f, 1f - h);
            return new Rect(u, v, w, h);
        }
    }
}
