using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using StellarisClone.Core;
using StellarisClone.Rendering;
using Sfx = StellarisClone.Core.Audio.Sfx;

namespace StellarisClone.Cards
{
    /// <summary>
    /// «Начальный расклад» (муллиган), как в больших карточных играх: стартовая рука крупно по центру,
    /// щелчок по карте помечает её на замену, «ГОТОВО» — помеченные уходят в колоду, вместо них приходят новые.
    /// Один раз за бой, до первого розыгрыша. Герой не меняется.
    /// </summary>
    public sealed partial class BattleScreen
    {
        private IEnumerator MulliganPhase()
        {
            var sd = _state.Sides[_P];
            if (_state.Over || sd.Mulliganed) yield break;
            var cards = new List<BCard>();
            foreach (var c in sd.Hand) if (c.Def.Kind != CardKind.Hero) cards.Add(c);
            if (cards.Count == 0) yield break;
            _busy = true;
            HidePreview(null);

            var root = LGBuild.Rect(_overlay, "Mulligan");
            root.Stretch();
            var dim = LGBuild.Panel(root, "Dim", new Color(0.005f, 0.01f, 0.02f, 0f), raycast: true);
            dim.rectTransform.Stretch();
            var title = Caption(root, Loc.T("НАЧАЛЬНЫЙ РАСКЛАД"), 34, new Color(Gold.r, Gold.g, Gold.b, 0f), TextAnchor.MiddleCenter, 6f);
            title.rectTransform.At(Mid, Mid, new Vector2(0, 340), new Vector2(1400, 50));
            var sub = LGBuild.Label(root, Loc.T("Щёлкните по картам, которые хотите заменить. Замена — один раз за бой"), 17, new Color(Muted.r, Muted.g, Muted.b, 0f), TextAnchor.MiddleCenter);
            sub.rectTransform.At(Mid, Mid, new Vector2(0, 296), new Vector2(1400, 30));

            var size = new Vector2(HandCard.x * 1.12f, HandCard.y * 1.12f);
            float step = Mathf.Min(size.x + 26f, 1500f / cards.Count);
            var marked = new HashSet<BCard>();
            var views = new List<(CardView v, Text mark, Vector2 home)>();
            for (int i = 0; i < cards.Count; i++)
            {
                var c = cards[i];
                var v = CardView.Create(root, c, _P, FactionOf(_P), size, false);
                var home = new Vector2((i - (cards.Count - 1) * 0.5f) * step, 10f);
                v.Rt.anchoredPosition = home + new Vector2(0, -700f);
                v.SetHoverable(true);
                v.HoverLift = 14f;
                v.HoverScale = 0.05f;
                v.IdleBob = 0.6f;
                var mark = Caption(v.Rt, Loc.T("ЗАМЕНИТЬ"), 22, new Color(1f, 0.45f, 0.38f, 0f), TextAnchor.MiddleCenter, 4f);
                mark.rectTransform.At(Mid, Mid, new Vector2(0, 20), new Vector2(size.x, 40));
                mark.rectTransform.localRotation = Quaternion.Euler(0, 0, 12f);
                var mol = mark.gameObject.AddComponent<Outline>();
                mol.effectColor = new Color(0.1f, 0f, 0f, 0.9f);
                mol.effectDistance = new Vector2(2f, -2f);
                var entry = (v, mark, home);
                views.Add(entry);
                v.OnClick = x =>
                {
                    bool on = !marked.Contains(c);
                    if (on) marked.Add(c); else marked.Remove(c);
                    x.SetAlpha(on ? 0.5f : 1f);
                    mark.color = new Color(1f, 0.45f, 0.38f, on ? 1f : 0f);
                    x.Punch(0.12f);
                    SFXManager.Play(on ? Sfx.CardPick : Sfx.CardPlace, 0.8f);
                };
            }

            bool done = false;
            var ok = MenuPrimaryButton.Create(root, Loc.T("ГОТОВО"), 20, () => done = true);
            var okRt = (RectTransform)ok.transform;
            okRt.At(Mid, Mid, new Vector2(0, -270), new Vector2(280, 62));
            ok.Label.Track(3f);
            okRt.localScale = Vector3.zero;

            // Появление: затемнение, заголовок, карты одна за другой снизу
            SFXManager.Play(Sfx.Whoosh, 0.7f, 0.85f);
            float t = 0f;
            while (t < 1f)
            {
                t += Time.unscaledDeltaTime * 2.2f;
                float e = 1f - Mathf.Pow(1f - Mathf.Clamp01(t), 3f);
                dim.color = new Color(0.005f, 0.01f, 0.02f, 0.78f * e);
                title.color = new Color(Gold.r, Gold.g, Gold.b, e);
                sub.color = new Color(Muted.r, Muted.g, Muted.b, e);
                yield return null;
            }
            for (int i = 0; i < views.Count; i++)
            {
                StartCoroutine(Slide(views[i].v.Rt, views[i].home, 0.35f));
                SFXManager.Play(Sfx.CardDraw, 0.7f, 1f + i * 0.03f);
                yield return new WaitForSecondsRealtime(0.07f);
            }
            yield return new WaitForSecondsRealtime(0.3f);
            float ts = 0f;
            while (ts < 1f) { ts += Time.unscaledDeltaTime * 5f; okRt.localScale = Vector3.one * Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(ts)); yield return null; }

            while (!done)
            {
                if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) done = true;
                yield return null;
            }
            okRt.gameObject.SetActive(false);

            // Сданные карты уходят вниз в колоду, оставленные — в руку; затем приходят новые
            var back = new List<BCard>(marked);
            foreach (var (v, _, home) in views)
            {
                bool gone = marked.Contains(v.Card);
                StartCoroutine(Slide(v.Rt, home + new Vector2(gone ? -900f : 0f, gone ? -200f : -720f), 0.35f));
            }
            if (back.Count > 0) SFXManager.Play(Sfx.CardPick, 0.9f, 0.9f);
            t = 0f;
            while (t < 1f)
            {
                t += Time.unscaledDeltaTime * 2.6f;
                dim.color = new Color(0.005f, 0.01f, 0.02f, 0.78f * (1f - t));
                title.color = new Color(Gold.r, Gold.g, Gold.b, 1f - t);
                sub.color = new Color(Muted.r, Muted.g, Muted.b, 1f - t);
                yield return null;
            }
            Destroy(root.gameObject);

            if (back.Count > 0)
            {
                _state.Mulligan(_P, back);
                for (int i = _hand.Count - 1; i >= 0; i--)
                    if (_hand[i] != null && back.Contains(_hand[i].Card)) { Destroy(_hand[i].gameObject); _hand.RemoveAt(i); }
                LayoutHand(false);
                AddLog(Loc.F("Начальный расклад: заменено карт — {0}", back.Count));
                yield return PlayEvents();
            }
            else _state.Mulligan(_P, back);   // отказ от замены — тоже решение: больше не предлагать
            _busy = false;
        }

        /// <summary>Плавно сдвинуть элемент в точку (затухающая скорость).</summary>
        private IEnumerator Slide(RectTransform rt, Vector2 to, float dur)
        {
            if (rt == null) yield break;
            var from = rt.anchoredPosition;
            float t = 0f;
            while (t < 1f && rt != null)
            {
                t += Time.unscaledDeltaTime / Mathf.Max(0.05f, dur);
                float e = 1f - Mathf.Pow(1f - Mathf.Clamp01(t), 3f);
                rt.anchoredPosition = Vector2.LerpUnclamped(from, to, e);
                yield return null;
            }
        }
    }
}
