using UnityEngine;
using UnityEngine.UI;
using StellarisClone.Core;

namespace StellarisClone.Rendering
{
    /// <summary>
    /// Окно звёздной базы — открывается отдельно (строкой «Звёздная база» в панели системы) и встаёт слева от неё.
    /// В духе больших 4X: шапка с туманностью и крупной эмблемой станции в голографических кольцах цвета владельца,
    /// мощь «набегает», залп и осада, полосы прочности, лестница укреплений (форпост → укреплённая база → крепость →
    /// цитадель) с множителями каждого уровня и стройка следующего уровня с ценой.
    /// </summary>
    public partial class UIManager
    {
        private RectTransform _sbWin, _sbBody;
        private CanvasGroup _sbGroup;
        private int _sbSystemId = -1;
        private float _sbShow, _sbShowV, _sbTarget;
        private const float SbWidth = 440f, SbHeaderH = 118f;

        public bool StarbaseWindowOpen => _sbTarget > 0.5f;

        // ==================== СТРОКА В ПАНЕЛИ СИСТЕМЫ ====================

        /// <summary>Строка-кнопка в панели системы: эмблема, уровень укреплений, мощь; клик — окно базы.</summary>
        private void InspStarbaseRow(StarSystem system, CombatManager.Starbase sb)
        {
            var cm = CombatManager.Instance;
            Color owner = FleetIndicator.OwnerColor(system.OwnerId);
            int lvl = system.Fortification;
            bool open = StarbaseWindowOpen && _sbSystemId == system.Id;

            var row = LGBuild.Panel(_inspStack, "StarbaseRow", InspCardBg);
            LGBuild.Height(row.gameObject, 62);
            LG.Platter(row.gameObject, 12f).SetRim(new Color(owner.r, owner.g, owner.b, open ? 0.7f : 0.32f));
            LGFx.HoverGlow(row.gameObject);
            var btn = row.gameObject.AddComponent<Button>();
            btn.transition = Selectable.Transition.None;
            btn.onClick.AddListener(() => { SFXManager.Play(StellarisClone.Core.Audio.Sfx.UiClick, 0.7f); ToggleStarbaseWindow(system); });

            var light = LGBuild.Panel(row.transform, "Light", new Color(owner.r, owner.g, owner.b, 0.12f));
            light.sprite = FactionSelectScreen.RadialSprite();
            light.raycastTarget = false;
            light.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(40, 0), new Vector2(200, 120));

            // Ручка у правого края: окно базы встаёт у правого края экрана, панель системы отъезжает влево —
            // стрелка смотрит туда, где появится окно («›» открыть, «‹» закрыть)
            var tab = LGBuild.Panel(row.transform, "Handle", new Color(owner.r * 0.15f, owner.g * 0.15f, owner.b * 0.15f, 0.95f));
            tab.raycastTarget = false;
            tab.rectTransform.At(new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-5, 0), new Vector2(16, 44));
            LG.Chip(tab.gameObject, new Color(owner.r, owner.g, owner.b, open ? 0.8f : 0.45f), 8f);
            var arrow = LGIcons.Create(tab.transform, LGIcon.Back, 11, open ? DS.Gold : Color.Lerp(owner, Color.white, 0.4f));
            arrow.rectTransform.At(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(11, 11));
            arrow.rectTransform.localRotation = Quaternion.Euler(0, 0, open ? 0f : 180f);

            SbEmblem(row.rectTransform, new Vector2(40, 0), 50f, owner, sb.Disabled, new Vector2(0, 0.5f));

            var cap = LGBuild.Label(row.transform, Loc.T("ЗВЁЗДНАЯ БАЗА"), 9, InspLabelCol, TextAnchor.UpperLeft, bold: true);
            Place(cap.rectTransform, 0f, 0.7f, 11f, 12f, 76f);
            cap.Track(1.6f);
            var name = LGBuild.Label(row.transform, Fortifications.Name(lvl).ToUpper(), 13, lvl > 0 ? DS.Gold : DS.TextPrimary, TextAnchor.UpperLeft, bold: true);
            Place(name.rectTransform, 0f, 0.75f, 24f, 18f, 76f);
            name.Track(1.2f);
            float power = cm != null ? cm.StarbasePower(system.Id) : 0f;
            var pw = LGBuild.Label(row.transform, sb.Disabled ? Loc.T("выведена из строя") : Loc.F("мощь {0:N0}", power), 10, sb.Disabled ? DS.Red : InspLabelCol, TextAnchor.UpperLeft);
            Place(pw.rectTransform, 0f, 0.7f, 42f, 14f, 76f);

            // Уровень укреплений — звёзды: построенные горят золотом (со свечением), недостроенные — тусклые
            for (int i = 0; i < Fortifications.MaxLevel; i++)
            {
                bool lit = i < lvl;
                var pos = new Vector2(-72 + i * 18, 0);
                if (lit)
                {
                    var g = LGBuild.Panel(row.transform, "StarGlow", new Color(DS.Gold.r, DS.Gold.g, DS.Gold.b, 0.35f));
                    g.sprite = FactionSelectScreen.RadialSprite();
                    g.raycastTarget = false;
                    g.rectTransform.At(new Vector2(1, 0.5f), new Vector2(0.5f, 0.5f), pos, new Vector2(30, 30));
                }
                var st = LGIcons.Create(row.transform, LGIcon.Star, 15, lit ? DS.Gold : new Color(0.55f, 0.62f, 0.7f, 0.25f));
                st.rectTransform.At(new Vector2(1, 0.5f), new Vector2(0.5f, 0.5f), pos, new Vector2(15, 15));
            }
            TooltipHelper.Attach(row.gameObject, open ? Loc.T("Скрыть окно звёздной базы") : Loc.T("Открыть звёздную базу: прочность, укрепления и их стройка"));
        }

        /// <summary>Эмблема станции: значок в ячейке и голографическое кольцо цвета владельца.</summary>
        private static RectTransform SbEmblem(RectTransform parent, Vector2 pos, float size, Color owner, bool disabled, Vector2 anchor)
        {
            var emb = LGBuild.Rect(parent, "Emblem");
            emb.At(anchor, new Vector2(0.5f, 0.5f), pos, new Vector2(size, size));
            var glow = LGBuild.Panel(emb, "Glow", new Color(owner.r, owner.g, owner.b, disabled ? 0.1f : 0.35f));
            glow.sprite = FactionSelectScreen.RadialSprite();
            glow.raycastTarget = false;
            glow.rectTransform.At(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(size * 1.9f, size * 1.9f));
            StellarisClone.Cards.HoloSpin.Add(glow, 0f, 0.2f, 0.04f);
            var ringB = ResSprite("UI/CardBattle/holo_ring_b");
            if (ringB != null && size >= 80f)
            {
                var rb = LGBuild.Panel(emb, "RingB", new Color(owner.r, owner.g, owner.b, disabled ? 0.2f : 0.6f));
                rb.sprite = ringB; rb.raycastTarget = false;
                rb.material = StellarisClone.Cards.UIFx.HoloMat;
                rb.rectTransform.At(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(size * 1.18f, size * 1.18f));
                StellarisClone.Cards.HoloSpin.Add(rb, disabled ? 0f : -8f, 0.15f);
            }
            var ringA = ResSprite("UI/CardBattle/holo_ring_a");
            if (ringA != null)
            {
                var ra = LGBuild.Panel(emb, "Ring", new Color(owner.r, owner.g, owner.b, disabled ? 0.25f : 0.8f));
                ra.sprite = ringA; ra.raycastTarget = false;
                ra.material = StellarisClone.Cards.UIFx.HoloMat;
                ra.rectTransform.Stretch();
                StellarisClone.Cards.HoloSpin.Add(ra, disabled ? 0f : 12f, 0.15f, 0f, 1.3f);
            }
            var cell = LGBuild.Panel(emb, "Cell", new Color(owner.r * 0.12f, owner.g * 0.12f, owner.b * 0.12f, 0.96f));
            cell.raycastTarget = false;
            cell.rectTransform.At(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(size * 0.62f, size * 0.62f));
            LG.Chip(cell.gameObject, new Color(owner.r, owner.g, owner.b, 0.6f), size * 0.31f);
            var ic = LGIcons.Create(emb, LGIcon.Starbase, size * 0.34f, disabled ? DS.Red : Color.Lerp(owner, Color.white, 0.4f));
            ic.rectTransform.At(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(size * 0.34f, size * 0.34f));
            return emb;
        }

        // ==================== ОКНО ====================

        private void ToggleStarbaseWindow(StarSystem system)
        {
            if (StarbaseWindowOpen && _sbSystemId == system.Id) { CloseStarbaseWindow(); ShowSystemPanel(system); return; }
            _sbSystemId = system.Id;
            _sbTarget = 1f;
            ShowSystemPanel(system);   // перестроит строку («открыто») и само окно (SyncStarbaseWindow)
        }

        private void CloseStarbaseWindow()
        {
            _sbTarget = 0f;
            if (_sbGroup != null) _sbGroup.blocksRaycasts = false;
        }

        /// <summary>Панель системы перестроилась: окно базы следует за ней (другая система без базы — закрыть).</summary>
        private void SyncStarbaseWindow(StarSystem system)
        {
            if (!StarbaseWindowOpen) return;
            var sb = system != null ? CombatManager.Instance?.GetStarbase(system.Id) : null;
            if (sb == null) { CloseStarbaseWindow(); return; }
            _sbSystemId = system.Id;
            BuildStarbaseWindow(system);
        }

        private void EnsureStarbaseWindow()
        {
            if (_sbWin != null) return;
            var go = new GameObject("StarbaseWindow", typeof(RectTransform));
            go.transform.SetParent(_canvas.transform, false);
            _sbWin = (RectTransform)go.transform;
            _sbWin.anchorMin = _sbWin.anchorMax = new Vector2(1, 1);
            _sbWin.pivot = new Vector2(1, 1);
            _sbWin.sizeDelta = new Vector2(SbWidth, 600);
            _sbGroup = go.AddComponent<CanvasGroup>();
            _sbGroup.alpha = 0f;
            _sbGroup.blocksRaycasts = false;
            var bg = go.AddComponent<Image>();
            bg.color = DS.BgDeep;
            bg.raycastTarget = true;
            LG.Glass(go, 22f).SetRim(new Color(0.45f, 0.95f, 0.90f, 0.34f));
            _sbBody = LGBuild.Rect(_sbWin, "Body");
            _sbBody.Stretch();
        }

        /// <summary>Окно базы стоит у правого края экрана (на месте панели системы), панель отъезжает влево.</summary>
        private Vector2 SbRestPos => InspectorRestPos;

        /// <summary>Положение панели системы: выезд справа при появлении и сдвиг влево, пока открыто окно базы.</summary>
        private Vector2 InspectorPos => InspectorRestPos + new Vector2((1f - Mathf.Clamp01(_inspShow)) * 40f - Mathf.Clamp01(_sbShow) * (SbWidth + 12f), 0f);

        private void BuildStarbaseWindow(StarSystem system)
        {
            EnsureStarbaseWindow();
            var cm = CombatManager.Instance;
            var sb = cm?.GetStarbase(system.Id);
            if (sb == null) { CloseStarbaseWindow(); return; }
            for (int i = _sbBody.childCount - 1; i >= 0; i--) { var c = _sbBody.GetChild(i); c.SetParent(null, false); Destroy(c.gameObject); }

            Color owner = FleetIndicator.OwnerColor(system.OwnerId);
            int lvl = system.Fortification;
            bool mine = system.OwnerId == 0;
            Color gold = DS.Gold;

            // ---------- шапка: туманность, свет владельца, крупная эмблема, названия
            var head = LGBuild.Rect(_sbBody, "Header");
            head.anchorMin = new Vector2(0, 1); head.anchorMax = new Vector2(1, 1); head.pivot = new Vector2(0.5f, 1);
            head.sizeDelta = new Vector2(0, SbHeaderH); head.anchoredPosition = Vector2.zero;
            var clip = LGBuild.Rect(head, "Clip");
            clip.Stretch(2, 2, 2, 0);
            clip.gameObject.AddComponent<RectMask2D>().softness = new Vector2Int(0, 10);
            var skyTex = Resources.Load<Texture2D>("Menu/menu_bg");
            if (skyTex != null)
            {
                var sky = new GameObject("Sky", typeof(RectTransform)).AddComponent<RawImage>();
                sky.transform.SetParent(clip, false);
                sky.texture = skyTex;
                sky.raycastTarget = false;
                sky.color = new Color(0.7f, 0.75f, 0.88f, 1f);
                sky.rectTransform.Stretch();
                float u = (system.Id * 37 % 55) / 100f, v = (system.Id * 61 % 50) / 100f;
                sky.uvRect = new Rect(u, v, 0.42f, 0.12f);
            }
            var olight = LGBuild.Panel(clip, "OwnerLight", new Color(owner.r, owner.g, owner.b, 0.3f));
            olight.sprite = FactionSelectScreen.RadialSprite();
            olight.raycastTarget = false;
            olight.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(70, 0), new Vector2(360, 260));
            var shade = LGBuild.Panel(clip, "Shade", new Color(0.01f, 0.02f, 0.03f, 0.55f));
            shade.sprite = FadeRightSprite;
            shade.raycastTarget = false;
            shade.rectTransform.Stretch();
            shade.rectTransform.localScale = new Vector3(-1f, 1f, 1f);   // зеркально: темнее справа, под названиями
            InspSweep.Add(head.gameObject, 7f, 0.07f);
            var stripe = LGBuild.Panel(_sbBody, "OwnerStripe", new Color(owner.r, owner.g, owner.b, 0.9f));
            stripe.raycastTarget = false;
            stripe.rectTransform.anchorMin = new Vector2(0, 1); stripe.rectTransform.anchorMax = new Vector2(1, 1);
            stripe.rectTransform.pivot = new Vector2(0.5f, 1);
            stripe.rectTransform.offsetMin = new Vector2(10, -3); stripe.rectTransform.offsetMax = new Vector2(-10, 0);
            var hline = LGBuild.Panel(head, "Line", new Color(DS.NeonCyan.r, DS.NeonCyan.g, DS.NeonCyan.b, 0.5f));
            hline.raycastTarget = false;
            hline.rectTransform.anchorMin = new Vector2(0, 0); hline.rectTransform.anchorMax = new Vector2(1, 0);
            hline.rectTransform.sizeDelta = new Vector2(-28, 1.2f);

            SbEmblem(head, new Vector2(64, -2), 92f, owner, sb.Disabled, new Vector2(0, 0.5f));
            var t1 = LGBuild.Label(head, Loc.T("ЗВЁЗДНАЯ БАЗА"), 10, InspLabelCol, TextAnchor.LowerLeft, bold: true);
            t1.rectTransform.anchorMin = new Vector2(0, 0.62f); t1.rectTransform.anchorMax = new Vector2(1, 0.8f);
            t1.rectTransform.offsetMin = new Vector2(124, 0); t1.rectTransform.offsetMax = new Vector2(-48, 0);
            t1.Track(2f);
            var t2 = LGBuild.Label(head, system.Name.ToUpper(), 22, Color.white, TextAnchor.MiddleLeft, bold: true);
            t2.rectTransform.anchorMin = new Vector2(0, 0.36f); t2.rectTransform.anchorMax = new Vector2(1, 0.64f);
            t2.rectTransform.offsetMin = new Vector2(124, 0); t2.rectTransform.offsetMax = new Vector2(-48, 0);
            t2.Track(1.8f);
            var t3 = LGBuild.Label(head, sb.Disabled ? Loc.T("ВЫВЕДЕНА ИЗ СТРОЯ") : Fortifications.Name(lvl).ToUpper(), 11, sb.Disabled ? DS.Red : (lvl > 0 ? gold : DS.NeonCyan), TextAnchor.UpperLeft, bold: true);
            t3.rectTransform.anchorMin = new Vector2(0, 0.15f); t3.rectTransform.anchorMax = new Vector2(1, 0.36f);
            t3.rectTransform.offsetMin = new Vector2(124, 0); t3.rectTransform.offsetMax = new Vector2(-48, 0);
            t3.Track(1.6f);
            var close = CreateCloseButton(head, 26f, () => { CloseStarbaseWindow(); if (_activeSystem != null) ShowSystemPanel(_activeSystem); });
            var crt = close.GetComponent<RectTransform>();
            crt.anchorMin = crt.anchorMax = new Vector2(1, 1); crt.pivot = new Vector2(1, 1);
            crt.anchoredPosition = new Vector2(-12, -12);

            // ---------- тело: карточка с мощью, прочность, лестница укреплений, стройка
            var card = LGBuild.Rect(_sbBody, "Content");
            card.anchorMin = new Vector2(0, 1); card.anchorMax = new Vector2(1, 1); card.pivot = new Vector2(0.5f, 1);
            card.anchoredPosition = new Vector2(0, -SbHeaderH - 6f);
            float y = 6f;

            // Мощь и залп — две крупные ячейки
            float power = cm.StarbasePower(system.Id);
            SbStat(card, 0f, 0.62f, y, Loc.T("МОЩЬ"), power, "N0", CombatManager.PowerInShips(power), sb.Disabled ? DS.Red : Color.white, owner);
            SbStat(card, 0.62f, 1f, y, Loc.T("ЗАЛП"), sb.Damage, "0", Loc.T("урона за раунд"), new Color(1f, 0.62f, 0.45f), new Color(1f, 0.45f, 0.35f));
            y += 74f;

            SbSection(card, ref y, Loc.T("ПРОЧНОСТЬ"), LGIcon.ShieldDome);
            DurabilityRow(card, ref y, LGIcon.ShieldDome, Loc.T("Щиты"), sb.Shields, sb.MaxShields, ShieldCol);
            DurabilityRow(card, ref y, LGIcon.ArmorPlate, Loc.T("Броня"), sb.Armor, sb.MaxArmor, ArmorCol);
            DurabilityRow(card, ref y, LGIcon.Ship, Loc.T("Корпус"), sb.Hull, sb.MaxHull, HullCol);
            if (sb.Disabled)
            {
                CardLabel(card, Loc.F("Ремонт {0:0}% из 50% — потом база вернётся в строй", sb.Hull / Mathf.Max(1f, sb.MaxHull) * 100f), 10, DS.Red,
                    TextAnchor.MiddleLeft, false, 0f, 1f, y, 16f, 14f, 14f);
                y += 20f;
            }
            y += 8f;

            SbSection(card, ref y, Loc.T("УКРЕПЛЕНИЯ"), LGIcon.Starbase);
            SbLadder(card, ref y, lvl, system);
            if (mine) FortificationAction(card, system, lvl, ref y);
            else
            {
                CardLabel(card, Loc.F("Осада системы дольше ×{0:0.##}", Fortifications.SiegeMult(lvl)), 10, InspLabelCol, TextAnchor.MiddleLeft, false, 0f, 1f, y, 16f, 14f, 14f);
                y += 22f;
            }

            card.sizeDelta = new Vector2(0, y);
            float canvasH = ((RectTransform)_canvas.transform).rect.height;
            _sbWin.sizeDelta = new Vector2(SbWidth, Mathf.Min(SbHeaderH + 12f + y + 14f, canvasH - InspectorTop - 40f));
            _sbGroup.blocksRaycasts = true;
            for (int i = 0; i < card.childCount; i++) LGFx.CascadeIn(card.GetChild(i).gameObject, i / 3, 0.05f);
        }

        /// <summary>Крупная ячейка: подпись, число «набегает», пояснение; под ней светится цвет ячейки.</summary>
        private static void SbStat(RectTransform card, float x0, float x1, float y, string label, float value, string fmt, string hint, Color col, Color glowCol)
        {
            var cell = LGBuild.Panel(card, "Stat", new Color(glowCol.r * 0.06f, glowCol.g * 0.06f, glowCol.b * 0.06f, 0.95f));
            Place(cell.rectTransform, x0, x1, y, 66f, x0 == 0f ? 14f : 4f, x1 >= 1f ? 14f : 4f);
            LG.Platter(cell.gameObject, 10f).SetRim(new Color(glowCol.r, glowCol.g, glowCol.b, 0.3f));
            var g = LGBuild.Panel(cell.transform, "Glow", new Color(glowCol.r, glowCol.g, glowCol.b, 0.14f));
            g.sprite = FactionSelectScreen.RadialSprite();
            g.raycastTarget = false;
            g.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(30, 0), new Vector2(200, 120));
            var l = LGBuild.Label(cell.transform, label, 9, InspLabelCol, TextAnchor.UpperLeft, bold: true);
            Place(l.rectTransform, 0f, 1f, 8f, 12f, 12f, 8f);
            l.Track(1.8f);
            var v = LGBuild.Label(cell.transform, value.ToString(fmt), 26, col, TextAnchor.UpperLeft, bold: true);
            Place(v.rectTransform, 0f, 1f, 19f, 32f, 12f, 8f);
            var sh = v.gameObject.AddComponent<Shadow>();
            sh.effectColor = new Color(0f, 0f, 0f, 0.5f);
            sh.effectDistance = new Vector2(0f, -1f);
            InspCountUp.Run(v, value, fmt);
            if (!string.IsNullOrEmpty(hint))
            {
                var h = LGBuild.Label(cell.transform, hint, 9, InspLabelCol, TextAnchor.UpperLeft);
                Place(h.rectTransform, 0f, 1f, 50f, 12f, 12f, 8f);
            }
        }

        /// <summary>Заголовок секции окна: ромб со свечением, надпись с разрядкой, черта, тающая вправо.</summary>
        private static void SbSection(RectTransform card, ref float y, string title, LGIcon icon)
        {
            Color g = DS.Gold;
            var row = LGBuild.Rect(card, "Section");
            Place(row, 0f, 1f, y, 26f, 14f, 14f);
            var glow = LGBuild.Panel(row, "Glow", new Color(g.r, g.g, g.b, 0.35f));
            glow.sprite = FactionSelectScreen.RadialSprite();
            glow.raycastTarget = false;
            glow.rectTransform.At(new Vector2(0, 0), new Vector2(0.5f, 0.5f), new Vector2(4, 9), new Vector2(24, 24));
            var dia = LGBuild.Panel(row, "Diamond", g);
            dia.raycastTarget = false;
            dia.rectTransform.At(new Vector2(0, 0), new Vector2(0.5f, 0.5f), new Vector2(4, 9), new Vector2(7, 7));
            dia.rectTransform.localRotation = Quaternion.Euler(0, 0, 45f);
            var ic = LGIcons.Create(row, icon, 12, new Color(g.r, g.g, g.b, 0.85f));
            ic.rectTransform.At(new Vector2(0, 0), new Vector2(0, 0), new Vector2(14, 3), new Vector2(12, 12));
            var t = LGBuild.Label(row, title, 11, g, TextAnchor.LowerLeft, bold: true);
            t.rectTransform.Stretch(32, 1, 0, 0);
            t.Track(2.2f);
            var line = LGBuild.Panel(row, "Line", new Color(g.r, g.g, g.b, 0.55f));
            line.sprite = FadeRightSprite;
            line.raycastTarget = false;
            line.rectTransform.anchorMin = new Vector2(0, 0); line.rectTransform.anchorMax = new Vector2(1, 0);
            line.rectTransform.pivot = new Vector2(0, 0);
            line.rectTransform.sizeDelta = new Vector2(0, 1.5f);
            y += 34f;
        }

        /// <summary>
        /// Лестница укреплений: четыре ступени в линию — построенные горят золотом, текущая пульсирует, следующая
        /// подсвечена; под каждой — название и множители прочности и урона.
        /// </summary>
        private void SbLadder(RectTransform card, ref float y, int lvl, StarSystem system)
        {
            Color g = DS.Gold;
            int n = Fortifications.MaxLevel + 1;
            var host = LGBuild.Rect(card, "Ladder");
            Place(host, 0f, 1f, y, 96f, 24f, 24f);
            var pending = Fortifications.PendingJob(system.Id);
            for (int i = 0; i < n; i++)
            {
                float fx = (i + 0.5f) / n;
                bool built = i <= lvl, current = i == lvl, next = i == lvl + 1;
                if (i < n - 1)
                {
                    var seg = LGBuild.Panel(host, "Seg", i < lvl ? new Color(g.r, g.g, g.b, 0.85f) : new Color(1f, 1f, 1f, 0.1f));
                    seg.raycastTarget = false;
                    seg.rectTransform.anchorMin = new Vector2(fx, 1); seg.rectTransform.anchorMax = new Vector2((i + 1.5f) / n, 1);
                    seg.rectTransform.pivot = new Vector2(0.5f, 0.5f);
                    seg.rectTransform.sizeDelta = new Vector2(-34f, i < lvl ? 3f : 2f);
                    seg.rectTransform.anchoredPosition = new Vector2(0, -18f);
                    if (i == lvl && pending != null)
                    {
                        // Идёт стройка следующего уровня — отрезок заполняется
                        seg.color = new Color(1f, 1f, 1f, 0.1f);
                        var fill = LGBuild.Panel(seg.transform, "Fill", new Color(g.r, g.g, g.b, 0.9f));
                        fill.raycastTarget = false;
                        fill.rectTransform.anchorMin = Vector2.zero; fill.rectTransform.anchorMax = new Vector2(Mathf.Clamp01(pending.Progress), 1f);
                        fill.rectTransform.offsetMin = fill.rectTransform.offsetMax = Vector2.zero;
                        LGFx.Shimmer(fill.rectTransform);
                    }
                }
                var node = LGBuild.Rect(host, "Node");
                node.anchorMin = node.anchorMax = new Vector2(fx, 1);
                node.pivot = new Vector2(0.5f, 0.5f);
                node.sizeDelta = new Vector2(34, 34);
                node.anchoredPosition = new Vector2(0, -18f);
                if (built || next)
                {
                    var glow = LGBuild.Panel(node, "Glow", new Color(g.r, g.g, g.b, current ? 0.5f : next ? 0.18f : 0.25f));
                    glow.sprite = FactionSelectScreen.RadialSprite();
                    glow.raycastTarget = false;
                    glow.rectTransform.At(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(70, 70));
                    if (current) StellarisClone.Cards.HoloSpin.Add(glow, 0f, 0.3f, 0.06f);
                }
                var disc = LGBuild.Panel(node, "Disc", built ? new Color(g.r * 0.3f, g.g * 0.24f, g.b * 0.08f, 1f) : new Color(0.03f, 0.05f, 0.07f, 1f));
                disc.raycastTarget = false;
                disc.rectTransform.Stretch();
                LG.Chip(disc.gameObject, built ? new Color(g.r, g.g, g.b, current ? 0.95f : 0.6f) : next ? new Color(g.r, g.g, g.b, 0.35f) : new Color(1f, 1f, 1f, 0.12f), 17f);
                if (i == 0)
                {
                    var ic = LGIcons.Create(node, LGIcon.Starbase, 15, built ? g : InspLabelCol);
                    ic.rectTransform.At(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(15, 15));
                }
                else
                {
                    var r = LGBuild.Label(node, i == 1 ? "I" : i == 2 ? "II" : "III", 13, built ? Color.white : next ? new Color(g.r, g.g, g.b, 0.8f) : InspLabelCol, TextAnchor.MiddleCenter, bold: true);
                    r.rectTransform.Stretch();
                }
                var nm = LGBuild.Label(host, Fortifications.Name(i), 9, built ? (current ? g : DS.TextPrimary) : InspLabelCol, TextAnchor.UpperCenter, bold: current);
                nm.rectTransform.anchorMin = new Vector2(i / (float)n, 1); nm.rectTransform.anchorMax = new Vector2((i + 1f) / n, 1);
                nm.rectTransform.pivot = new Vector2(0.5f, 1);
                nm.rectTransform.sizeDelta = new Vector2(10, 26); nm.rectTransform.anchoredPosition = new Vector2(0, -40f);
                nm.horizontalOverflow = HorizontalWrapMode.Wrap;
                var mult = LGBuild.Label(host, Loc.F("×{0:0.##} · ×{1:0.##}", Fortifications.DurabilityMult(i), Fortifications.DamageMult(i)), 9,
                    built ? new Color(g.r, g.g, g.b, 0.8f) : new Color(InspLabelCol.r, InspLabelCol.g, InspLabelCol.b, 0.7f), TextAnchor.UpperCenter);
                mult.rectTransform.anchorMin = new Vector2(i / (float)n, 1); mult.rectTransform.anchorMax = new Vector2((i + 1f) / n, 1);
                mult.rectTransform.pivot = new Vector2(0.5f, 1);
                mult.rectTransform.sizeDelta = new Vector2(0, 14); mult.rectTransform.anchoredPosition = new Vector2(0, -68f);
            }
            var hit = LGBuild.Panel(host, "Tip", new Color(0, 0, 0, 0), raycast: true);
            hit.rectTransform.Stretch();
            TooltipHelper.Attach(hit.gameObject, FortTooltip(lvl) + Loc.T("\n<color=#8AA2A8>Под ступенью: прочность × · урон ×</color>"));
            y += 100f;
        }

        /// <summary>Каждый кадр (из Update): окно выезжает справа на пружине (панель системы уступает место) и тает при закрытии.</summary>
        private void UpdateStarbaseWindow(float dt)
        {
            if (_sbWin == null) return;
            // Панель системы закрыта — окно базы тоже
            if (_inspTargetAlpha < 0.5f && _sbTarget > 0f) CloseStarbaseWindow();
            if (Mathf.Abs(_sbShow - _sbTarget) < 0.001f && Mathf.Abs(_sbShowV) < 0.001f) return;
            _sbShow = LGEase.Spring(_sbShow, _sbTarget, ref _sbShowV, 2.6f, 0.8f, dt);
            float k = Mathf.Clamp01(_sbShow);
            _sbGroup.alpha = LGEase.OutCubic(k);
            _sbWin.anchoredPosition = SbRestPos + new Vector2((1f - k) * 60f, 0f);
            _sbWin.localScale = Vector3.one * Mathf.LerpUnclamped(0.97f, 1f, _sbShow);
            if (_inspectorRect != null) _inspectorRect.anchoredPosition = InspectorPos;
        }
    }
}
