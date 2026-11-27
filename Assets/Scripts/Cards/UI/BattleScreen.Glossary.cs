using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using StellarisClone.Core;
using StellarisClone.Rendering;

namespace StellarisClone.Cards
{
    /// <summary>
    /// Подсказки к увеличенной карте, как в больших карточных играх: рядом с превью — столбик стеклянных
    /// плашек «свойство — что оно делает» (тип карты, ключевые слова, укрытие, залп, ветеранство, состояние юнита).
    /// </summary>
    public sealed partial class BattleScreen
    {
        private readonly List<GameObject> _glossary = new List<GameObject>();

        private static List<(string title, string text)> GlossaryFor(BCard card, BUnit u)
        {
            var d = card.Def;
            var l = new List<(string, string)>();
            switch (d.Kind)
            {
                case CardKind.Order: l.Add((Loc.T("Приказ"), Loc.T("Кладётся рубашкой вверх, срабатывает при раскрытии в конце хода и сгорает."))); break;
                case CardKind.Trap: l.Add((Loc.T("Ловушка"), Loc.T("Скрыта от врага. Срабатывает на первом раскрытом враге в секторе."))); break;
                case CardKind.Structure: l.Add((Loc.T("Сооружение"), Loc.T("Стоит на столе с начала боя, держит сектор и стреляет."))); break;
                case CardKind.Hero: l.Add((Loc.T("Герой"), Loc.T("Лидер стороны: всегда в стартовой руке, усиливает своих."))); break;
            }
            void K(Kw k, string title, string text) { if (d.Has(k)) l.Add((title, text)); }
            K(Kw.Pierce, Loc.T("Бронебой"), Loc.T("По технике и сооружениям — двойной урон."));
            K(Kw.Indirect, Loc.T("Навесной огонь"), Loc.T("Ещё бьёт по соседнему сектору половиной силы."));
            K(Kw.Crush, Loc.T("Давит"), Loc.T("При раскрытии уничтожает до двух пехотинцев врага с бронёй до 2."));
            K(Kw.Swarm, Loc.T("Рой"), Loc.T("Сила не больше оставшейся брони: теряя дронов, рой слабеет."));
            K(Kw.Sniper, Loc.T("Снайпер"), Loc.T("Бьёт самого хрупкого врага; по пехоте — двойной урон."));
            K(Kw.Flame, Loc.T("Огонь"), Loc.T("Игнорирует укрытие; по сооружениям — двойной урон."));
            K(Kw.Fragile, Loc.T("Одноразовый"), Loc.T("Погибает в конце хода."));
            K(Kw.NeedsPower, Loc.T("От электростанции"), Loc.T("Работает, пока электростанция у защитника."));
            K(Kw.Merc, Loc.T("Наёмники"), Loc.T("Покупаются прямо в бою за гелий-3 казны."));
            K(Kw.Militia, Loc.T("Ополчение"), Loc.T("Жители колонии с оружием: погибшее ополчение — минус житель."));
            if (d.Cover > 0) l.Add((Loc.F("Укрытие {0}", d.Cover), Loc.F("Первые {0} урона за ход не проходят.", d.Cover)));
            if (d.Salvo > 0) l.Add((Loc.F("Залп {0}", d.Salvo), Loc.F("При раскрытии сразу наносит {0} урона врагу в секторе.", d.Salvo)));
            if (card.Stars > 0)
                l.Add((Loc.T("Ветеран ") + new string('★', Mathf.Min(3, card.Stars)), Loc.T("Опыт прошлых боёв: ★ — +1 броня, ★★ — ещё +1 сила, ★★★ — ещё +1 к силе и броне.")));
            if (u != null)
            {
                if (u.Suppressed > 0) l.Add((Loc.T("Подавлен"), Loc.F("Не стреляет ещё {0} ход.", u.Suppressed)));
                if (u.Disabled > 0) l.Add((Loc.T("Отключён"), Loc.F("Не работает ещё {0} ход.", u.Disabled)));
                if (u.Immobile > 0) l.Add((Loc.T("Скован"), Loc.F("Не стреляет и не двигается ещё {0} ход.", u.Immobile)));
            }
            if (d.IsBoardUnit && l.Count < 4)
                l.Add((Loc.T("Сила и броня"), Loc.T("Сила — урон за ход и вклад в контроль сектора. Броня — сколько урона карта выдержит.")));
            return l;
        }

        /// <summary>
        /// Столбик плашек рядом с картой: center/size — карта в координатах экрана боя (канвас 1920×1080 от центра),
        /// side — с какой стороны (+1 справа, −1 слева). Плашки живут в слое превью — поверх руки и стола.
        /// floor — ниже этой высоты столбик не опускается (у руки — верх соседних карт, чтобы не закрывать их имена).
        /// </summary>
        private void ShowGlossary(BCard card, BUnit u, Vector2 center, Vector2 size, float side, float floor = float.MinValue)
        {
            ClearGlossary();
            var items = GlossaryFor(card, u);
            if (items.Count == 0) return;
            const float w = 250f;
            float x = center.x + side * (size.x * 0.5f + 14f + w * 0.5f);
            // Не вылезать за экран: если места нет, плашки — по другую сторону
            if (Mathf.Abs(x) + w * 0.5f > 950f) x = center.x - side * (size.x * 0.5f + 14f + w * 0.5f);
            float total = 0f;
            for (int i = 0; i < items.Count && i < 6; i++) total += 36f + Mathf.Max(1, Mathf.CeilToInt(items[i].text.Length / 34f)) * 16f + 8f;
            float y = Mathf.Min(Mathf.Max(center.y + size.y * 0.5f, floor + total), 520f);
            var layer = _previewRoot;
            var origin = _previewRoot.anchoredPosition;
            int n = 0;
            foreach (var (title, text) in items)
            {
                int lines = Mathf.Max(1, Mathf.CeilToInt(text.Length / 34f));
                float h = 36f + lines * 16f;
                var p = GlassAt(layer, "Glossary", new Vector2(x, y - h * 0.5f) - origin, new Vector2(w, h), PanelTint, RimSoft, false, 12f);
                var acc = LGBuild.Panel(p.rectTransform, "Accent", new Color(Gold.r, Gold.g, Gold.b, 0.9f));
                acc.raycastTarget = false;
                acc.rectTransform.At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(12, -15), new Vector2(3, 12));
                var tt = Caption(p.rectTransform, title.ToUpper(), 12, Gold, TextAnchor.UpperLeft, 1.2f);
                tt.rectTransform.Stretch(22, 0, 10, 10);
                var tx = LGBuild.Label(p.rectTransform, text, 12, UIManager.DS.TextPrimary, TextAnchor.UpperLeft, wrap: true);
                tx.rectTransform.Stretch(12, 8, 10, 28);
                tx.raycastTarget = false;
                foreach (var g in p.GetComponentsInChildren<Graphic>()) g.raycastTarget = false;
                p.transform.SetAsLastSibling();
                var cg = p.gameObject.AddComponent<CanvasGroup>();
                cg.alpha = 0f;
                StartCoroutine(FadeIn(cg, 0.06f * n));
                _glossary.Add(p.gameObject);
                y -= h + 8f;
                if (++n >= 6) break;
            }
        }

        private System.Collections.IEnumerator FadeIn(CanvasGroup cg, float delay)
        {
            if (delay > 0f) yield return new WaitForSecondsRealtime(delay);
            float t = 0f;
            while (cg != null && t < 1f) { t += Time.unscaledDeltaTime * 7f; cg.alpha = Mathf.Clamp01(t); yield return null; }
        }

        private void ClearGlossary()
        {
            foreach (var g in _glossary) if (g != null) Destroy(g);
            _glossary.Clear();
        }
    }
}
