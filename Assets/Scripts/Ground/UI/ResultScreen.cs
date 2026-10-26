using System;
using UnityEngine;
using UnityEngine.UI;
using StellarisClone.Core;
using StellarisClone.Rendering;

namespace StellarisClone.Ground
{
    /// <summary>
    /// Экран итогов штурма (и личного боя, и автобоя) плюс модальное подтверждение «Да / Нет».
    /// Свой холст поверх всего (sortingOrder 400), строится кодом в стиле LiquidGlass.
    /// </summary>
    public static class ResultScreen
    {
        private static GameObject s_root;
        private static GameObject s_confirm;
        private static bool s_pausedByConfirm;

        public static bool IsOpen => s_root != null || s_confirm != null;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() { s_root = null; s_confirm = null; s_pausedByConfirm = false; }

        internal static RectTransform NewCanvas(string name, int order)
        {
            var go = new GameObject(name);
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = order;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            go.AddComponent<GraphicRaycaster>();
            var rt = (RectTransform)go.transform;
            var dim = LGBuild.Panel(rt, "Dim", new Color(0f, 0.01f, 0.02f, 0.72f), raycast: true);
            dim.rectTransform.Stretch();
            return rt;
        }

        internal static RectTransform Window(RectTransform canvas, Vector2 size, Color rim)
        {
            var img = LGBuild.Panel(canvas, "Window", new Color(0.02f, 0.05f, 0.07f, 0.96f), raycast: true);
            img.rectTransform.At(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, size);
            LG.Glass(img.gameObject).SetRim(rim);
            return img.rectTransform;
        }

        // ================================================================ Итоги

        /// <summary>Итоги личного боя: победа/поражение с точки зрения игрока, причина, потери. «Продолжить» — назад в галактику.</summary>
        public static void Show(GroundBattleManager gm, GroundBattleResult r, string reason, Action onContinue)
            => Show(gm != null ? gm.Setup : null, r, reason, onContinue);

        /// <summary>Итоги (в том числе автобоя, без поля боя).</summary>
        public static void Show(GroundBattleSetup setup, GroundBattleResult r, string reason, Action onContinue)
        {
            CloseConfirm();
            if (s_root != null) UnityEngine.Object.Destroy(s_root);

            // Чей это бой: если игрок защищался — победа = атака отбита
            bool playerDefends = setup != null && setup.Defender.IsPlayer && !setup.Attacker.IsPlayer;
            bool playerInvolved = setup != null && (setup.Attacker.IsPlayer || setup.Defender.IsPlayer);
            bool win = playerDefends ? !r.AttackerWon : r.AttackerWon;
            Color accent = !playerInvolved ? UIManager.DS.Gold : win ? new Color(0.45f, 1f, 0.6f) : new Color(1f, 0.4f, 0.38f);

            var canvas = NewCanvas("[Ground] Result", 400);
            s_root = canvas.gameObject;
            var w = Window(canvas, new Vector2(720f, 560f), new Color(accent.r, accent.g, accent.b, 0.55f));

            string head = !playerInvolved ? (r.AttackerWon ? Loc.T("КОЛОНИЯ ПАЛА") : Loc.T("ШТУРМ ОТБИТ"))
                        : win ? Loc.T("ПОБЕДА") : r.Retreated ? Loc.T("ДЕСАНТ ОТОЗВАН") : Loc.T("ПОРАЖЕНИЕ");
            var title = LGBuild.Label(w, head, 40, accent, TextAnchor.UpperCenter, bold: true);
            title.rectTransform.Stretch(0, 0, 0, 26);
            title.Track(6f);

            string sub = setup != null ? Loc.F("Штурм планеты {0}", setup.PlanetName) : "";
            if (r.AutoResolved) sub += "  ·  " + Loc.T("автобой");
            else sub += "  ·  " + Loc.F("{0}:{1:00}", (int)(r.Duration / 60f), (int)(r.Duration % 60f));
            var st = LGBuild.Label(w, sub, 13, UIManager.DS.TextMuted, TextAnchor.UpperCenter, bold: true);
            st.rectTransform.Stretch(0, 0, 0, 82);
            if (!string.IsNullOrEmpty(reason))
            {
                var rs = LGBuild.Label(w, reason, 15, Color.white, TextAnchor.UpperCenter);
                rs.rectTransform.Stretch(30, 0, 30, 108);
            }

            // ---- Потери сторон ----
            float y = -150f;
            Color ca = setup != null ? setup.Attacker.Color : new Color(0.5f, 0.8f, 1f);
            Color cd = setup != null ? setup.Defender.Color : new Color(1f, 0.6f, 0.4f);
            Header(w, y, Loc.T("ПОТЕРИ"));
            y -= 30f;
            Column(w, y, 0f, 0.5f, ca, Loc.T("Атакующие"), r.AttackerLostMen, r.AttackerLostVehicles);
            Column(w, y, 0.5f, 1f, cd, Loc.T("Защитники"), r.DefenderLostMen, r.DefenderLostVehicles);
            y -= 96f;

            // ---- Колония ----
            Header(w, y, Loc.T("КОЛОНИЯ"));
            y -= 30f;
            string districts;
            if (r.DestroyedDistricts.Count == 0) districts = Loc.T("Районы уцелели");
            else
            {
                var counts = new int[4];
                foreach (var d in r.DestroyedDistricts) counts[(int)d]++;
                var sb = new System.Text.StringBuilder();
                for (int i = 0; i < counts.Length; i++)
                {
                    if (counts[i] == 0) continue;
                    if (sb.Length > 0) sb.Append(", ");
                    sb.Append(DistrictInfo.Name((DistrictType)i).ToLower()).Append(" ×").Append(counts[i]);
                }
                districts = Loc.F("Разрушено районов: {0}", sb.ToString());
            }
            Row(w, y, LGIcon.Planet, districts, r.DestroyedDistricts.Count > 0 ? new Color(1f, 0.7f, 0.5f) : Color.white);
            y -= 28f;
            Row(w, y, LGIcon.Population, r.CiviliansKilled > 0 ? Loc.F("Погибло жителей: {0}", r.CiviliansKilled) : Loc.T("Мирные жители не пострадали"),
                r.CiviliansKilled > 0 ? new Color(1f, 0.55f, 0.5f) : Color.white);
            y -= 28f;
            if (r.AttackerWon && r.VeteranSquads > 0)
            {
                Row(w, y, LGIcon.Leader, Loc.F("Ветеранских отрядов: {0} — флот получает опыт штурма", r.VeteranSquads), UIManager.DS.Gold);
                y -= 28f;
            }

            var btn = LGBuild.Button(w, "Continue", UIManager.DS.BtnPrimary, new Color(accent.r, accent.g, accent.b, 0.8f), () =>
            {
                SFXManager.Play(Core.Audio.Sfx.UiConfirm, 0.7f);
                if (s_root != null) UnityEngine.Object.Destroy(s_root);
                s_root = null;
                onContinue?.Invoke();
            }, LGIcon.Back, Loc.T("ПРОДОЛЖИТЬ"), 14);
            ((RectTransform)btn.transform).At(new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 26f), new Vector2(260f, 46f));
        }

        private static void Header(RectTransform w, float y, string text)
        {
            var t = LGBuild.Label(w, text, 11, UIManager.DS.Gold, TextAnchor.MiddleLeft, bold: true);
            t.rectTransform.At(new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(40f, y), new Vector2(640f, 20f));
            t.Track(2f);
            var line = LGBuild.Panel(w, "Rule", new Color(1f, 1f, 1f, 0.12f));
            line.rectTransform.At(new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(40f, y - 22f), new Vector2(640f, 1f));
        }

        private static void Column(RectTransform w, float y, float x0, float x1, Color c, string name, int men, int vehicles)
        {
            var host = LGBuild.Rect(w, name);
            host.anchorMin = new Vector2(x0, 1f); host.anchorMax = new Vector2(x1, 1f); host.pivot = new Vector2(0.5f, 1f);
            host.offsetMin = new Vector2(40f, y - 86f); host.offsetMax = new Vector2(-20f, y);
            var n = LGBuild.Label(host, name.ToUpper(), 12, Color.Lerp(c, Color.white, 0.35f), TextAnchor.UpperLeft, bold: true);
            n.rectTransform.Stretch(0, 0, 0, 0);
            var big = LGBuild.Label(host, men.ToString(), 34, Color.white, TextAnchor.UpperLeft, bold: true);
            big.rectTransform.Stretch(0, 0, 0, 20);
            var lab = LGBuild.Label(host, Loc.T("бойцов"), 12, UIManager.DS.TextMuted, TextAnchor.UpperLeft);
            lab.rectTransform.Stretch(0, 0, 0, 62);
            if (vehicles > 0)
            {
                var v = LGBuild.Label(host, Loc.F("+ машин: {0}", vehicles), 13, Color.white, TextAnchor.UpperLeft, bold: true);
                v.rectTransform.Stretch(110, 0, 0, 36);
            }
        }

        private static void Row(RectTransform w, float y, LGIcon icon, string text, Color c)
        {
            var ic = LGIcons.Create(w, icon, 16, c);
            ic.rectTransform.At(new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(40f, y - 2f), new Vector2(16f, 16f));
            var t = LGBuild.Label(w, text, 14, c, TextAnchor.MiddleLeft);
            t.rectTransform.At(new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(66f, y), new Vector2(600f, 20f));
        }

        // ================================================================ Подтверждение

        /// <summary>Модальный вопрос «Да / Нет». В бою ставит тактическую паузу, пока открыт.</summary>
        public static void Confirm(string title, string text, Action onYes)
        {
            CloseConfirm();
            var gm = GroundBattleManager.Instance;
            s_pausedByConfirm = false;
            if (GroundBattleManager.InBattle && gm != null && !gm.Paused) { gm.TogglePause(); s_pausedByConfirm = true; }

            var canvas = NewCanvas("[Ground] Confirm", 410);
            s_confirm = canvas.gameObject;
            var w = Window(canvas, new Vector2(520f, 230f), new Color(1f, 0.5f, 0.45f, 0.5f));
            var t = LGBuild.Label(w, title, 20, Color.white, TextAnchor.UpperCenter, bold: true);
            t.rectTransform.Stretch(20, 0, 20, 22);
            var body = LGBuild.Label(w, text, 14, UIManager.DS.TextMuted, TextAnchor.UpperCenter, wrap: true);
            body.rectTransform.Stretch(34, 80, 34, 64);

            var yes = LGBuild.Button(w, "Yes", UIManager.DS.BtnDanger, new Color(1f, 0.5f, 0.45f, 0.8f), () =>
            {
                CloseConfirm();
                onYes?.Invoke();
            }, null, Loc.T("ДА"), 13);
            ((RectTransform)yes.transform).At(new Vector2(0.5f, 0f), new Vector2(1f, 0f), new Vector2(-10f, 22f), new Vector2(170f, 42f));
            var no = LGBuild.Button(w, "No", UIManager.DS.BtnNeutral, new Color(1f, 1f, 1f, 0.35f), CloseConfirm, null, Loc.T("НЕТ"), 13);
            ((RectTransform)no.transform).At(new Vector2(0.5f, 0f), new Vector2(0f, 0f), new Vector2(10f, 22f), new Vector2(170f, 42f));
        }

        public static void CloseConfirm()
        {
            if (s_confirm != null) UnityEngine.Object.Destroy(s_confirm);
            s_confirm = null;
            if (s_pausedByConfirm)
            {
                s_pausedByConfirm = false;
                var gm = GroundBattleManager.Instance;
                if (gm != null && gm.Paused) gm.TogglePause();
            }
        }
    }
}
