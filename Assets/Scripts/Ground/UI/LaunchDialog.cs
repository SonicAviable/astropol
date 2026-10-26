using System;
using UnityEngine;
using UnityEngine.UI;
using StellarisClone.Core;
using StellarisClone.Rendering;

namespace StellarisClone.Ground
{
    /// <summary>
    /// Окно перед штурмом: силы сторон, шанс автобоя и выбор —
    /// «Командовать лично / Автобой / Ждать осады» (игрок атакует) или «Защищаться лично / Автобой» (враг высаживается).
    /// Пока окно открыто, стратегическое время стоит.
    /// </summary>
    public static class LaunchDialog
    {
        private static GameObject s_root;
        private static int s_speedBefore = -1;

        public static bool IsOpen => s_root != null;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() { s_root = null; s_speedBefore = -1; }

        public static void ShowAttack(GroundBattleSetup setup, float chance, Action personal, Action auto)
            => Build(setup, chance, false, personal, auto);

        public static void ShowDefense(GroundBattleSetup setup, float chance, Action personal, Action auto)
            => Build(setup, chance, true, personal, auto);

        private static void Build(GroundBattleSetup s, float chance, bool defense, Action personal, Action auto)
        {
            Close();
            var tm = TimeManager.Instance;
            if (tm != null) { s_speedBefore = tm.CurrentSpeed; tm.SetSpeed(0); }
            SFXManager.Play(defense ? Core.Audio.Sfx.RedAlert : Core.Audio.Sfx.PanelSlide, 0.8f);

            var canvas = ResultScreen.NewCanvas("[Ground] Launch", 390);
            s_root = canvas.gameObject;
            Color accent = defense ? new Color(1f, 0.42f, 0.38f) : UIManager.DS.Gold;
            var w = ResultScreen.Window(canvas, new Vector2(760f, 540f), new Color(accent.r, accent.g, accent.b, 0.55f));

            string head = defense ? Loc.F("ВРАГ ВЫСАЖИВАЕТСЯ НА {0}!", s.PlanetName.ToUpper()) : Loc.F("ВЫСАДКА НА {0}", s.PlanetName.ToUpper());
            var title = LGBuild.Label(w, head, 26, accent, TextAnchor.UpperCenter, bold: true);
            title.rectTransform.Stretch(20, 0, 20, 24);
            title.Track(3f);
            var sub = LGBuild.Label(w, Loc.F("{0} · население {1} · {2}", GroundBiome.Name(s.Planet), s.Population, Fortifications.Name(s.Fortification)),
                13, UIManager.DS.TextMuted, TextAnchor.UpperCenter, bold: true);
            sub.rectTransform.Stretch(20, 0, 20, 64);

            // ---- Две колонки сил ----
            SideColumn(w, 0f, 0.5f, s.Attacker, Loc.T("ДЕСАНТ"), new[]
            {
                Loc.F("Мест под отряды: {0}", s.Attacker.Slots),
                Loc.F("Мощь флота на орбите: {0:N0}", s.Attacker.FleetPower),
                s.Attacker.GeneralLeaderId >= 0 ? Loc.F("Генерал: {0} (ур. {1})", s.Attacker.GeneralName, s.Attacker.GeneralLevel) : Loc.T("Без генерала"),
                s.Attacker.StartVeterancy > 0 ? Loc.F("Ветераны: {0}", new string('★', s.Attacker.StartVeterancy)) : Loc.T("Новобранцы"),
            });
            SideColumn(w, 0.5f, 1f, s.Defender, Loc.T("ГАРНИЗОН"), new[]
            {
                Loc.F("Отрядов на старте: {0}", s.GarrisonSquads),
                Loc.F("Ополчение из городских районов: {0}", s.Urban),
                s.Defender.GeneralLeaderId >= 0 ? Loc.F("Губернатор: {0} (ур. {1})", s.Defender.GeneralName, s.Defender.GeneralLevel) : Loc.T("Без губернатора"),
                s.Stability < 40f ? Loc.T("<color=#FFB070>Колония неспокойна: ополчение может перейти на сторону врага</color>") : Loc.F("Стабильность: {0:0}", s.Stability),
            });

            // ---- Шанс автобоя ----
            var odds = LGBuild.Label(w, Loc.F("Шанс автобоя: {0:0}%", Mathf.Clamp01(chance) * 100f), 15, Color.white, TextAnchor.MiddleCenter, bold: true);
            odds.rectTransform.At(new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 136f), new Vector2(600f, 24f));
            var barHost = LGBuild.Rect(w, "Odds");
            barHost.At(new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 118f), new Vector2(520f, 10f));
            LGBuild.Bar(barHost, chance >= 0.5f ? new Color(0.45f, 1f, 0.6f) : new Color(1f, 0.55f, 0.4f), Mathf.Clamp01(chance), 10f);

            // ---- Кнопки ----
            float bw = defense ? 260f : 220f;
            float gap = 14f;
            int n = defense ? 2 : 3;
            float x0 = -(n * bw + (n - 1) * gap) * 0.5f + bw * 0.5f;
            Button(w, x0, bw, defense ? Loc.T("ЗАЩИЩАТЬСЯ ЛИЧНО") : Loc.T("КОМАНДОВАТЬ ЛИЧНО"), LGIcon.Swords, UIManager.DS.BtnPrimary, accent,
                Loc.T("Бой в реальном времени. Перед боем — автосохранение"), personal);
            Button(w, x0 + bw + gap, bw, Loc.T("АВТОБОЙ"), LGIcon.Target, UIManager.DS.BtnNeutral, new Color(1f, 1f, 1f, 0.4f),
                Loc.T("Мгновенный итог по соотношению сил"), auto);
            if (!defense)
                Button(w, x0 + 2f * (bw + gap), bw, Loc.T("ЖДАТЬ ОСАДЫ"), LGIcon.Siege, UIManager.DS.BtnNeutral, new Color(1f, 1f, 1f, 0.25f),
                    Loc.T("Осада идёт своим ходом и возьмёт колонию по таймеру"), null);
        }

        private static void SideColumn(RectTransform w, float x0, float x1, GroundSideSetup side, string title, string[] lines)
        {
            var host = LGBuild.Panel(w, title, new Color(side.Color.r * 0.15f, side.Color.g * 0.15f, side.Color.b * 0.15f, 0.55f));
            var rt = host.rectTransform;
            rt.anchorMin = new Vector2(x0, 1f); rt.anchorMax = new Vector2(x1, 1f); rt.pivot = new Vector2(0.5f, 1f);
            rt.offsetMin = new Vector2(x0 == 0f ? 30f : 10f, -330f); rt.offsetMax = new Vector2(x1 == 1f ? -30f : -10f, -100f);
            LG.Platter(host.gameObject, 12f).SetRim(new Color(side.Color.r, side.Color.g, side.Color.b, 0.45f));
            string who = side.Owner == 0 ? Loc.T("Мы") : AIEmpireManager.NameOf(side.Owner);
            var t = LGBuild.Label(rt, $"{title}  ·  {who}", 13, Color.Lerp(side.Color, Color.white, 0.35f), TextAnchor.UpperLeft, bold: true);
            t.rectTransform.Stretch(16, 0, 16, 14);
            float y = -46f;
            foreach (var line in lines)
            {
                var l = LGBuild.Label(rt, line, 13, Color.white, TextAnchor.UpperLeft, wrap: true);
                l.rectTransform.anchorMin = new Vector2(0f, 1f); l.rectTransform.anchorMax = new Vector2(1f, 1f); l.rectTransform.pivot = new Vector2(0.5f, 1f);
                l.rectTransform.offsetMin = new Vector2(16f, y - 40f); l.rectTransform.offsetMax = new Vector2(-16f, y);
                y -= 42f;
            }
        }

        private static void Button(RectTransform w, float x, float width, string label, LGIcon icon, Color tint, Color rim, string tip, Action act)
        {
            var b = LGBuild.Button(w, "Choice", tint, new Color(rim.r, rim.g, rim.b, 0.8f), () =>
            {
                SFXManager.Play(Core.Audio.Sfx.UiConfirm, 0.7f);
                Close();
                act?.Invoke();
            }, icon, label, 12);
            ((RectTransform)b.transform).At(new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(x, 34f), new Vector2(width, 50f));
            TooltipHelper.Attach(b.gameObject, tip);
        }

        public static void Close()
        {
            if (s_root != null) UnityEngine.Object.Destroy(s_root);
            s_root = null;
            if (s_speedBefore >= 0)
            {
                TimeManager.Instance?.SetSpeed(s_speedBefore);
                s_speedBefore = -1;
            }
        }
    }
}
