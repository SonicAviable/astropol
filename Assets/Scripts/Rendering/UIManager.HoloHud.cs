using UnityEngine;
using UnityEngine.UI;
using StellarisClone.Core;

namespace StellarisClone.Rendering
{
    /// <summary>
    /// Верх голо-HUD (вариант «Голо-диск»):
    ///   • по центру сверху — диск времени: месяц, крупный день, год; кольцо — ход месяца; шкала-деления;
    ///     под диском дугой — четыре кнопки скорости; клик по диску — пауза / продолжить;
    ///   • от диска расходятся два скошенных «крыла» с ресурсами (гелий-3, титан, сплавы | влияние, наука, стабильность);
    ///   • слева — круглый портрет правителя с кольцом средней стабильности, справа от него — название империи и правитель.
    /// </summary>
    public partial class UIManager
    {
        private const float DiskD = 118f;          // диаметр диска времени
        private const float DiskCenterY = 78f;     // центр диска от верха экрана: риски (DiskD + 34) целиком на экране
        private const float WingH = 64f;
        private const float WingGap = 18f;          // зазор между диском и крылом
        private const float CellStep = 154f;        // ширина ячейки ресурса (150) + зазор
        private const float RulerD = RulerBadge.Width;
        private static readonly Vector2 RulerCenter = new Vector2(20f + RulerD * 0.5f, -(18f + RulerD * 0.5f));

        /// <summary>Нижний край верха HUD (портрет с кольцами) — от него ставится левое меню.</summary>
        public const float HoloTopBottom = 18f + RulerBadge.Height + 16f;

        private Text _diskMonth, _diskDay, _diskYear, _speedCaption;
        private Image _monthRing, _stabRing, _stabTicks;
        private Image _moon, _moonGlow;
        private float _moonPhase = -1f;
        private Text _empireName, _empireRuler;
        private int _arcShown = -1;
        private float _avgStability = -1f;
        private string _empireKey;

        private static readonly Color HoloLine = new Color(0.37f, 0.82f, 0.75f, 1f);
        private static readonly Color WingTint = new Color(0.030f, 0.068f, 0.095f, 0.95f);

        private static string MonthName(int m)
        {
            switch (m)
            {
                case 1: return Loc.T("ЯНВАРЬ");
                case 2: return Loc.T("ФЕВРАЛЬ");
                case 3: return Loc.T("МАРТ");
                case 4: return Loc.T("АПРЕЛЬ");
                case 5: return Loc.T("МАЙ");
                case 6: return Loc.T("ИЮНЬ");
                case 7: return Loc.T("ИЮЛЬ");
                case 8: return Loc.T("АВГУСТ");
                case 9: return Loc.T("СЕНТЯБРЬ");
                case 10: return Loc.T("ОКТЯБРЬ");
                case 11: return Loc.T("НОЯБРЬ");
                default: return Loc.T("ДЕКАБРЬ");
            }
        }

        private void BuildTopBar()
        {
            BuildRulerCluster();
            BuildResourceWings();
            BuildTimeDisk();
            BuildNavMenu();
            HudBanner.Create(_canvas.transform, DiskCenterY + DiskD * 0.5f + 84f);
            BuildScreenCorners();
        }

        /// <summary>Уголки по краям экрана — рамка голо-HUD.</summary>
        private void BuildScreenCorners()
        {
            var c = new Color(HoloLine.r, HoloLine.g, HoloLine.b, 0.5f);
            for (int i = 0; i < 4; i++)
            {
                var anchor = new Vector2(i % 2, i / 2);                 // (0,0) (1,0) (0,1) (1,1)
                var dir = new Vector2(anchor.x < 0.5f ? 1f : -1f, anchor.y < 0.5f ? 1f : -1f);
                var off = new Vector2(6f * dir.x, 6f * dir.y);
                var h = HoloImage(_canvas.transform, "Corner", null, c);
                h.rectTransform.At(anchor, anchor, off, new Vector2(26f, 1f));
                var v = HoloImage(_canvas.transform, "Corner", null, c);
                v.rectTransform.At(anchor, anchor, off, new Vector2(1f, 26f));
            }
        }

        // ---------- Правитель ----------

        private void BuildRulerCluster()
        {
            var root = _canvas.transform;
            // Шкала и кольцо стабильности — под портретом (портрет поверх, он ловит клики)
            _stabTicks = HoloImage(root, "RulerTicks", HudArt.Ticks(60, 0, 0.955f, 0.955f, 1f), new Color(HoloLine.r, HoloLine.g, HoloLine.b, 0.4f));
            _stabTicks.rectTransform.At(new Vector2(0, 1), new Vector2(0.5f, 0.5f), RulerCenter, Vector2.one * (RulerD + 34f));
            var track = HoloImage(root, "StabTrack", HudArt.Ring(0.90f, 1f), new Color(1f, 1f, 1f, 0.08f));
            track.rectTransform.At(new Vector2(0, 1), new Vector2(0.5f, 0.5f), RulerCenter, Vector2.one * (RulerD + 22f));
            var stabGlow = HoloImage(root, "StabGlow", FactionSelectScreen.RadialSprite(), new Color(StabPink.r, StabPink.g, StabPink.b, 0.16f));
            stabGlow.rectTransform.At(new Vector2(0, 1), new Vector2(0.5f, 0.5f), RulerCenter, Vector2.one * (RulerD + 70f));
            _stabRing = HoloImage(root, "StabRing", HudArt.Ring(0.90f, 1f), DS.NeonCyan);
            _stabGlow = stabGlow;
            _stabRing.rectTransform.At(new Vector2(0, 1), new Vector2(0.5f, 0.5f), RulerCenter, Vector2.one * (RulerD + 22f));
            RadialFill(_stabRing, 0f);
            track.raycastTarget = true;
            TooltipHelper.Attach(track.gameObject, Loc.T("<b>Кольцо стабильности</b>\nСредняя стабильность колоний (по числу жителей)."));

            RulerBadge.Create(root, new Vector2(20f, -18f), OpenEmpireOverviewModal);

            // Значок места империи в рейтинге — кружок у правого нижнего края портрета
            var rank = HoloImage(root, "RankBadge", HudArt.Disc(), new Color(0.02f, 0.06f, 0.09f, 1f));
            rank.rectTransform.At(new Vector2(0, 1), new Vector2(0.5f, 0.5f), RulerCenter + new Vector2(34f, -34f), new Vector2(26f, 26f));
            var rankRing = HoloImage(rank.transform, "Ring", HudArt.Ring(0.86f, 1f), DS.NeonCyan);
            rankRing.rectTransform.Stretch();
            _rankText = CreateText(rank.transform, "", 12, FontStyle.Normal, DS.NeonCyan, TextAnchor.MiddleCenter);
            _rankText.font = GameFont.Display;
            _rankText.rectTransform.Stretch();
            rank.raycastTarget = true;
            TooltipHelper.Attach(rank.gameObject, Loc.T("<b>Место в галактике</b>\nПо очкам империи: системы, население, технологии, флот и чудеса."));

            float x = 20f + RulerD + 28f;
            _empireName = CreateText(root, "", 19, FontStyle.Normal, DS.NeonCyan, TextAnchor.LowerLeft);
            _empireName.font = GameFont.Display;
            _empireName.rectTransform.At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(x, -18f), new Vector2(240f, 26f));
            _empireName.resizeTextForBestFit = true;
            _empireName.resizeTextMinSize = 11;
            _empireName.resizeTextMaxSize = 19;
            _empireName.horizontalOverflow = HorizontalWrapMode.Wrap;

            _empireRuler = CreateText(root, "", 12, FontStyle.Normal, DS.TextMuted, TextAnchor.MiddleLeft);
            _empireRuler.rectTransform.At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(x, -46f), new Vector2(240f, 16f));
            _empireRuler.horizontalOverflow = HorizontalWrapMode.Wrap;
            _empireRuler.verticalOverflow = VerticalWrapMode.Truncate;
            _empireRuler.resizeTextForBestFit = true;
            _empireRuler.resizeTextMinSize = 9;
            _empireRuler.resizeTextMaxSize = 12;

            // Три плашки-показателя: системы, мощь флота, место в рейтинге
            float cx = x;
            _chipSystems = StatChip(root, LGIcon.Planet, ref cx, Loc.T("<b>Системы</b>\nСколько систем у вашей империи"));
            _chipPower = StatChip(root, LGIcon.Fleet, ref cx, Loc.T("<b>Мощь флота</b>\nСуммарная боевая мощь ваших военных кораблей"));
            _chipRank = StatChip(root, LGIcon.Trophy, ref cx, Loc.T("<b>Место в галактике</b>\nПо очкам империи среди всех империй"));
        }

        private Text _chipSystems, _chipPower, _chipRank, _rankText;
        /// <summary>Заливки полосок под ячейками ресурсов: гелий, титан, сплавы, влияние, наука, стабильность.</summary>
        private readonly System.Collections.Generic.List<RectTransform> _resBarFills = new System.Collections.Generic.List<RectTransform>();

        /// <summary>
        /// Полоски под ресурсами. Титан — заполненность складов; стабильность — средняя стабильность;
        /// наука — доля занятых слотов исследований; гелий, сплавы и влияние — запас относительно
        /// двухлетнего дохода (пустая — запас на исходе, полная — с большим заделом).
        /// </summary>
        private void UpdateResourceBars()
        {
            if (_resBarFills.Count < 6) return;
            var eco = EconomyManager.Instance;
            if (eco == null) return;
            float Reserve(float stock, float income) => Mathf.Clamp01(stock / (stock + 24f * Mathf.Max(1f, income) + 1f) * 2f);
            float research = 0f;
            var tm = TechnologyManager.Instance;
            if (tm != null && tm.Slots.Count > 0)
            {
                int active = 0;
                foreach (var sl in tm.Slots) if (sl.CurrentTech != null && !sl.IsPaused) active++;
                research = active / (float)tm.Slots.Count;
            }
            float[] f =
            {
                Reserve(eco.EnergyCredits, eco.MonthlyEnergyIncome),
                eco.MineralStorage > 0f ? Mathf.Clamp01(eco.Minerals / eco.MineralStorage) : 0f,
                Reserve(eco.Alloys, eco.MonthlyAlloysIncome),
                Reserve(eco.Influence, eco.MonthlyInfluenceIncome),
                research,
                _avgStability < 0f ? 0f : Mathf.Clamp01(_avgStability / 100f)
            };
            for (int i = 0; i < 6; i++)
            {
                var rt = _resBarFills[i];
                if (rt == null) continue;
                float cur = rt.anchorMax.x;
                float v = Mathf.MoveTowards(cur, f[i], Time.unscaledDeltaTime * 0.6f);
                if (Mathf.Abs(v - cur) > 0.0005f) rt.anchorMax = new Vector2(v, 1f);
            }
        }
        private float _statsTimer;
        private static readonly Color StabPink = new Color(0.94f, 0.56f, 0.86f, 1f);
        private Image _stabGlow;
        private float _fitTimer;
        private RectTransform _leftWing;

        /// <summary>
        /// Название империи и строка правителя — не шире места до левого крыла ресурсов (иначе текст уходит под него);
        /// лишнее ужимается подбором размера шрифта.
        /// </summary>
        private void FitRulerText()
        {
            if (_empireName == null || _canvas == null || _leftWing == null) return;
            if ((_fitTimer -= Time.unscaledDeltaTime) > 0f) return;
            _fitTimer = 0.5f;                                     // размер канваса может смениться (окно, разрешение)
            float canvasW = ((RectTransform)_canvas.transform).rect.width;
            if (canvasW <= 0f) return;
            // Левый край левого крыла в координатах от левого края экрана (крыло привязано к центру, опора справа)
            float wingLeft = canvasW * 0.5f + _leftWing.anchoredPosition.x - _leftWing.rect.width;
            float x = _empireName.rectTransform.anchoredPosition.x;
            float avail = Mathf.Clamp(wingLeft - x - 14f, 90f, 320f);
            _empireName.rectTransform.sizeDelta = new Vector2(avail, _empireName.rectTransform.sizeDelta.y);
            _empireRuler.rectTransform.sizeDelta = new Vector2(avail, _empireRuler.rectTransform.sizeDelta.y);
        }

        /// <summary>Плашка-показатель под названием империи: значок и число в тёмной рамке.</summary>
        private Text StatChip(Transform root, LGIcon icon, ref float x, string tip)
        {
            var bg = HoloImage(root, "Chip_" + icon, null, new Color(0.035f, 0.08f, 0.11f, 0.92f));
            bg.rectTransform.At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(x, -62f), new Vector2(56f, 22f));
            bg.raycastTarget = true;
            var fr = HoloImage(bg.transform, "Frame", HudArt.CutFrame(2, 1f), new Color(HoloLine.r, HoloLine.g, HoloLine.b, 0.4f));
            fr.type = Image.Type.Sliced;
            fr.rectTransform.Stretch();
            var ic = LGIcons.Create(bg.transform, icon, 12f, DS.TextMuted);
            ic.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(7f, 0f), new Vector2(12f, 12f));
            ic.raycastTarget = false;
            var t = CreateText(bg.transform, "", 12, FontStyle.Bold, DS.TextPrimary, TextAnchor.MiddleLeft);
            t.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(23f, 0f), new Vector2(40f, 20f));
            TooltipHelper.Attach(bg.gameObject, tip);
            x += 56f + 6f;
            return t;
        }

        /// <summary>Раз в секунду: системы, мощь флота, место по очкам; ширина плашек — по тексту.</summary>
        private void UpdateRulerStats()
        {
            if (_chipSystems == null || (_statsTimer -= Time.unscaledDeltaTime) > 0f) return;
            _statsTimer = 1f;
            int sys = EmpireStats.SystemCount(0);
            float pw = EmpireStats.MilitaryPower(0);
            int mine = EmpireStats.Score(0).Total, place = 1;
            foreach (var ai in AIEmpireManager.Alive) if (EmpireStats.Score(ai.OwnerId).Total > mine) place++;
            _chipSystems.text = sys.ToString();
            _chipPower.text = pw >= 1000f ? $"{pw / 1000f:0.0}K" : $"{pw:0}";
            _chipRank.text = "#" + place;
            _rankText.text = place.ToString();
            float x = _chipSystems.transform.parent.GetComponent<RectTransform>().anchoredPosition.x;
            foreach (var t in new[] { _chipSystems, _chipPower, _chipRank })
            {
                var bg = (RectTransform)t.transform.parent;
                float w = 23f + t.preferredWidth + 9f;
                bg.anchoredPosition = new Vector2(x, bg.anchoredPosition.y);
                bg.sizeDelta = new Vector2(w, 22f);
                x += w + 6f;
            }
        }

        // ---------- Крылья ресурсов ----------

        private void BuildResourceWings()
        {
            float half = DiskD * 0.5f + WingGap;

            var left = Wing("WingLeft", true, -half);
            _leftWing = (RectTransform)left;
            float lx = 30f;
            _energyVal    = CreateResourceBadge(left, LGIcon.Energy, Loc.T("ГЕЛИЙ-3"), DS.Gold, ref lx, out _energyTip, false);
            _mineralsVal  = CreateResourceBadge(left, LGIcon.Minerals, Loc.T("ТИТАН"), DS.NeonCyan, ref lx, out _mineralsTip, true);
            _alloysVal    = CreateResourceBadge(left, LGIcon.Alloys, Loc.T("СПЛАВЫ"), new Color(0.95f, 0.62f, 0.36f), ref lx, out _alloysTip, true);

            var right = Wing("WingRight", false, half);
            float rx = 8f;
            _influenceVal = CreateResourceBadge(right, LGIcon.Influence, Loc.T("ВЛИЯНИЕ"), new Color(1.00f, 0.48f, 0.58f), ref rx, out _influenceTip, false);
            _researchVal  = CreateResourceBadge(right, LGIcon.Research, Loc.T("НАУКА"), DS.Green, ref rx, out _researchTip, true);
            _stabilityVal = CreateResourceBadge(right, LGIcon.Society, Loc.T("СТАБИЛЬНОСТЬ"), new Color(0.98f, 0.55f, 0.85f), ref rx, out _stabilityTip, true);
            // Клик по стабильности — управление колониями (там видно, где неспокойно)
            var stabBtn = _stabilityTip.gameObject.AddComponent<Button>();
            stabBtn.transition = Selectable.Transition.None;
            stabBtn.onClick.AddListener(ColonyManagerWindow.Toggle);

            // Соединители крыльев с диском
            for (int s = -1; s <= 1; s += 2)
            {
                var ln = HoloImage(_canvas.transform, "WingLink", null, new Color(HoloLine.r, HoloLine.g, HoloLine.b, 0.8f));
                ln.rectTransform.At(new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f), new Vector2(s * (DiskD * 0.5f + WingGap * 0.5f), -(8f + WingH * 0.5f)), new Vector2(WingGap, 1f));
            }
        }

        /// <summary>Крыло: скошенная тёмная плашка с кромкой. sign — сторона: x &lt; 0 — левое (растёт влево от диска).</summary>
        private Transform Wing(string name, bool slantLeft, float x)
        {
            float w = 3f * CellStep + 38f - 4f;
            var go = new GameObject(name);
            go.transform.SetParent(_canvas.transform, false);
            var rt = go.AddComponent<RectTransform>();
            rt.At(new Vector2(0.5f, 1f), new Vector2(slantLeft ? 1f : 0f, 1f), new Vector2(x, -8f), new Vector2(w, WingH));
            var bg = go.AddComponent<Image>();
            bg.sprite = HudArt.Wing(slantLeft);
            bg.type = Image.Type.Sliced;
            bg.color = WingTint;
            bg.raycastTarget = false;
            LG.Ignore(go, includeChildren: false);

            var rim = HoloImage(go.transform, "Rim", HudArt.WingRim(slantLeft), new Color(HoloLine.r, HoloLine.g, HoloLine.b, 0.55f));
            rim.type = Image.Type.Sliced;
            rim.rectTransform.Stretch();

            var motion = LG.Motion(go, LGAppear.Kind.SlideDown);
            motion.distance = 22f;
            motion.inDuration = 0.55f;
            return go.transform;
        }

        // ---------- Диск времени и скорость ----------

        private void BuildTimeDisk()
        {
            var disk = LGBuild.Rect(_canvas.transform, "TimeDisk");
            disk.At(new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f), new Vector2(0f, -DiskCenterY), Vector2.one * DiskD);
            LG.Ignore(disk.gameObject);
            LG.Motion(disk.gameObject, LGAppear.Kind.SlideDown).distance = 18f;

            var glow = HoloImage(disk, "Glow", FactionSelectScreen.RadialSprite(), new Color(HoloLine.r, HoloLine.g, HoloLine.b, 0.06f));
            glow.rectTransform.At(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.one * (DiskD + 120f));

            var ticks = HoloImage(disk, "Ticks", HudArt.Ticks(72, 6, 0.95f, 0.89f, 1f), new Color(HoloLine.r, HoloLine.g, HoloLine.b, 0.5f));
            ticks.rectTransform.At(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.one * (DiskD + 34f));

            var track = HoloImage(disk, "MonthTrack", HudArt.Ring(0.96f, 1f), new Color(1f, 1f, 1f, 0.08f));
            track.rectTransform.At(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.one * (DiskD + 18f));
            _monthRing = HoloImage(disk, "MonthRing", HudArt.Ring(0.96f, 1f), HoloLine);
            _monthRing.rectTransform.At(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.one * (DiskD + 18f));
            RadialFill(_monthRing, 0f);

            // Сам диск — кнопка паузы
            var face = HoloImage(disk, "Face", HudArt.Disc(), new Color(0.016f, 0.026f, 0.034f, 0.97f));
            face.rectTransform.Stretch();
            face.raycastTarget = true;
            var faceBtn = face.gameObject.AddComponent<Button>();
            faceBtn.transition = Selectable.Transition.None;
            faceBtn.onClick.AddListener(TogglePauseFromDisk);
            TooltipHelper.Attach(face.gameObject, Loc.T("<b>Дата</b>\nКольцо и фаза луны — ход текущего месяца (1-е — новолуние, середина — полнолуние).\nЩелчок — пауза / продолжить  <color=#8AA2A8>(ПРОБЕЛ)</color>"));
            var edge = HoloImage(disk, "Edge", HudArt.Ring(0.975f, 1f), new Color(HoloLine.r, HoloLine.g, HoloLine.b, 0.45f));
            edge.rectTransform.Stretch();

            // Луна за датой: фаза — ход месяца (1-е — новолуние, середина — полнолуние), приглушена под текстом
            _moonGlow = HoloImage(disk, "MoonGlow", FactionSelectScreen.RadialSprite(), new Color(0.85f, 0.95f, 1f, 0.08f));
            _moonGlow.rectTransform.At(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 3f), Vector2.one * (DiskD * 0.92f));
            _moon = HoloImage(disk, "Moon", MoonPhase.Get(0f), new Color(0.80f, 0.90f, 0.93f, 0.50f));
            _moon.rectTransform.At(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 3f), Vector2.one * (DiskD * 0.60f));

            // Дата как в референсе: месяц мелко с разрядкой (цвет HUD), крупный день, год под ним
            _diskMonth = CreateText(disk, "", 11, FontStyle.Bold, HoloLine, TextAnchor.MiddleCenter);
            _diskMonth.rectTransform.At(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 31f), new Vector2(DiskD, 16f));
            _diskMonth.Track(3f);
            _diskDay = CreateText(disk, "", 36, FontStyle.Normal, DS.TextPrimary, TextAnchor.MiddleCenter);
            _diskDay.font = GameFont.Display;
            _diskDay.rectTransform.At(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 4f), new Vector2(DiskD, 44f));
            _diskYear = CreateText(disk, "", 13, FontStyle.Bold, DS.TextMuted, TextAnchor.MiddleCenter);
            _diskYear.rectTransform.At(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -26f), new Vector2(DiskD, 18f));
            _diskYear.Track(4.5f);

            BuildSpeedArc(disk);

            _speedCaption = CreateText(disk, "", 10, FontStyle.Bold, DS.TextMuted, TextAnchor.MiddleCenter);
            _speedCaption.rectTransform.At(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -(GaugeR + 40f)), new Vector2(200f, 14f));
            _speedCaption.Track(1.8f);

            // Музыка — кнопка справа под диском, панель плеера — под ней
            var music = LGBuild.Rect(disk, "MusicHolder");
            // слева от дуги скорости — справа под крылом стоят алерты
            music.At(new Vector2(0.5f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-(GaugeR + 44f), -(DiskD * 0.5f - 12f)), new Vector2(30f, 30f));
            MusicPlayerPanel.CreateToggle(music, 0f, _canvas, -(DiskCenterY + DiskD * 0.5f + 70f));
        }

        /// <summary>
        /// Скорость — шкала-прибор под диском (вариант C): дуга-дорожка, светящаяся заливка от паузы до текущей
        /// скорости, риски и подписи «❚❚ ×1 ×2 ×4», бегунок плавно переезжает к выбранной риске.
        /// Щелчок по подписи или риске — сменить скорость.
        /// </summary>
        private void BuildSpeedArc(RectTransform disk)
        {
            var arc = LGBuild.Rect(disk, "SpeedGauge");
            arc.At(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.one * (GaugeR * 2f + GaugeW));
            LG.Ignore(arc.gameObject);
            float d = GaugeR * 2f + GaugeW;
            var band = HudArt.Ring((GaugeR - GaugeW * 0.5f) / (d * 0.5f), 1f);

            // Дорожка и заливка: части кольца от левого края шкалы (240° по часовой от верха) до правого (120°)
            var track = HoloImage(arc, "Track", band, new Color(HoloLine.r, HoloLine.g, HoloLine.b, 0.16f));
            track.rectTransform.Stretch();
            RadialFill(track, (GaugeLeft - GaugeRight) / 360f);
            track.rectTransform.localRotation = Quaternion.Euler(0, 0, -GaugeRight);
            _gaugeGlow = HoloImage(arc, "FillGlow", HudArt.Ring((GaugeR - GaugeW * 1.6f) / (d * 0.5f), 1f), new Color(HoloLine.r, HoloLine.g, HoloLine.b, 0.18f));
            _gaugeGlow.rectTransform.Stretch(-6f, -6f, -6f, -6f);
            RadialFill(_gaugeGlow, 0f);
            _gaugeGlow.fillClockwise = false;
            _gaugeGlow.rectTransform.localRotation = Quaternion.Euler(0, 0, -GaugeLeft);
            _gaugeFill = HoloImage(arc, "Fill", band, HoloLine);
            _gaugeFill.rectTransform.Stretch();
            RadialFill(_gaugeFill, 0f);
            _gaugeFill.fillClockwise = false;                         // от паузы (слева) вправо
            _gaugeFill.rectTransform.localRotation = Quaternion.Euler(0, 0, -GaugeLeft);

            string[] labels = { "", "×1", "×2", "×4" };
            string[] tips =
            {
                Loc.T("<b>Пауза</b>\nКлавиша: ПРОБЕЛ"),
                Loc.T("<b>Нормальная скорость</b>\nКлавиша: 1"),
                Loc.T("<b>Быстро</b>\nКлавиша: 2"),
                Loc.T("<b>Очень быстро</b>\nКлавиша: 3")
            };
            int[] speeds = { 0, 1, 2, 4 };
            for (int i = 0; i < 4; i++)
            {
                int captured = speeds[i];
                float phi = GaugePhi(i);
                var tick = HoloImage(arc, $"Tick_{i}", null, new Color(HoloLine.r, HoloLine.g, HoloLine.b, 0.6f));
                tick.rectTransform.At(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Polar(phi, GaugeR), new Vector2(2f, GaugeW + 12f));
                tick.rectTransform.localRotation = Quaternion.Euler(0, 0, -phi);
                _gaugeTicks[i] = tick;

                var lp = Polar(phi, GaugeR + 22f);
                Graphic label;
                if (i == 0)
                {
                    var ic = LGIcons.Create(arc, LGIcon.Pause, 12f, DS.TextMuted);
                    ic.rectTransform.At(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), lp, new Vector2(12f, 12f));
                    ic.raycastTarget = false;
                    label = ic;
                }
                else
                {
                    var t = CreateText(arc, labels[i], 13, FontStyle.Normal, DS.TextMuted, TextAnchor.MiddleCenter);
                    t.font = GameFont.Display;
                    t.rectTransform.At(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), lp, new Vector2(30f, 18f));
                    label = t;
                }
                _gaugeLabels[i] = label;

                var hit = HoloImage(arc, $"Speed_{i}", null, new Color(0f, 0f, 0f, 0f));
                hit.raycastTarget = true;
                hit.rectTransform.At(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Polar(phi, GaugeR + 10f), new Vector2(34f, 40f));
                hit.rectTransform.localRotation = Quaternion.Euler(0, 0, -phi);
                var b = hit.gameObject.AddComponent<Button>();
                b.transition = Selectable.Transition.None;
                b.onClick.AddListener(() => SetSpeed(captured));
                TooltipHelper.Attach(hit.gameObject, tips[i]);
            }

            // Бегунок: светлая точка со свечением
            var kglow = HoloImage(arc, "KnobGlow", FactionSelectScreen.RadialSprite(), new Color(HoloLine.r, HoloLine.g, HoloLine.b, 0.7f));
            kglow.rectTransform.At(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(40f, 40f));
            _knob = kglow.rectTransform;
            _knobGlow = kglow;
            var kcore = HoloImage(_knob, "Core", HudArt.Disc(), Color.white);
            kcore.rectTransform.At(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(13f, 13f));
            _knobPhi = GaugePhi(1);
            _knob.anchoredPosition = Polar(_knobPhi, GaugeR);
            _gaugeFill.fillAmount = _gaugeGlow.fillAmount = (GaugeLeft - _knobPhi) / 360f;

            UIAnchors.Register(UIAnchors.Speed, arc);
        }

        private const float GaugeR = 94f, GaugeW = 7f, GaugeLeft = 236f, GaugeRight = 124f;
        private Image _gaugeFill, _gaugeGlow, _knobGlow;
        private RectTransform _knob;
        private readonly Image[] _gaugeTicks = new Image[4];
        private readonly Graphic[] _gaugeLabels = new Graphic[4];
        private float _knobPhi, _knobV;

        /// <summary>Угол риски скорости (градусы по часовой от верха): пауза — левый край шкалы, ×4 — правый.</summary>
        private static float GaugePhi(int i) => GaugeLeft - i * (GaugeLeft - GaugeRight) / 3f;
        private static Vector2 Polar(float phiDeg, float r)
        {
            float a = phiDeg * Mathf.Deg2Rad;
            return new Vector2(Mathf.Sin(a), Mathf.Cos(a)) * r;
        }

        /// <summary>Бегунок и заливка шкалы плавно едут к текущей скорости.</summary>
        private void AnimateGauge(int idx)
        {
            if (_knob == null) return;
            float target = GaugePhi(idx);
            if (Mathf.Abs(_knobPhi - target) < 0.01f && Mathf.Abs(_knobV) < 0.01f) return;
            _knobPhi = LGEase.Spring(_knobPhi, target, ref _knobV, 3.4f, 0.75f, Time.unscaledDeltaTime);
            _knob.anchoredPosition = Polar(_knobPhi, GaugeR);
            float f = Mathf.Max(0f, GaugeLeft - _knobPhi) / 360f;
            _gaugeFill.fillAmount = f;
            _gaugeGlow.fillAmount = f;
        }

        private void SetSpeed(int s) { if (TimeManager.Instance != null) TimeManager.Instance.SetSpeedByPlayer(s); }

        private int _lastRunSpeed = 1;

        private void TogglePauseFromDisk()
        {
            var tm = TimeManager.Instance;
            if (tm == null) return;
            if (tm.CurrentSpeed > 0) { _lastRunSpeed = tm.CurrentSpeed; SetSpeed(0); }
            else SetSpeed(Mathf.Max(1, _lastRunSpeed));
        }

        // ---------- Луна ----------

        /// <summary>Фаза плавно догоняет ход месяца (на смене месяца — вперёд через новолуние), лёгкое покачивание и ореол.</summary>
        private void UpdateMoon(float frac)
        {
            if (_moon == null) return;
            float dt = Time.unscaledDeltaTime, t = Time.unscaledTime;
            if (_moonPhase < 0f) _moonPhase = frac;
            float cur = _moonPhase - Mathf.Floor(_moonPhase);
            float d = frac - cur;
            if (d < -0.5f) d += 1f;          // 30-е → 1-е: доворачиваем вперёд
            else if (d > 0.5f) d -= 1f;
            _moonPhase += d * (1f - Mathf.Exp(-dt * 5f));
            _moonPhase -= Mathf.Floor(_moonPhase);

            var sp = MoonPhase.Get(_moonPhase);
            if (_moon.sprite != sp) _moon.sprite = sp;
            _moon.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 4f * Mathf.Sin(t * 0.25f));
            float ill = MoonPhase.Illumination(_moonPhase);
            _moonGlow.color = new Color(0.85f, 0.95f, 1f, (0.03f + 0.10f * ill) * (0.85f + 0.15f * Mathf.Sin(t * 1.1f)));
        }

        // ---------- Обновление ----------

        private void UpdateHoloTop()
        {
            var tm = TimeManager.Instance;
            if (tm != null && _diskDay != null)
            {
                string day = tm.Day.ToString();
                if (_diskDay.text != day) _diskDay.text = day;
                string month = MonthName(tm.Month);
                if (_diskMonth.text != month) _diskMonth.text = month;
                string year = tm.Year.ToString();
                if (_diskYear.text != year) _diskYear.text = year;
                float frac = Mathf.Clamp01((tm.Day - 1 + tm.DayFraction) / 30f);
                if (Mathf.Abs(_monthRing.fillAmount - frac) > 0.0005f) _monthRing.fillAmount = frac;
                UpdateMoon(frac);

                int speed = tm.CurrentSpeed;
                int idx = Mathf.Clamp(speed >= 3 ? 3 : speed, 0, 3);
                AnimateGauge(idx);
                if (idx != _arcShown)
                {
                    _arcShown = idx;
                    Color acc = idx == 0 ? DS.Gold : HoloLine;
                    for (int i = 0; i < 4; i++)
                    {
                        bool on = i == idx;
                        _gaugeLabels[i].color = on ? Color.white : DS.TextMuted;
                        _gaugeTicks[i].color = on ? Color.white : new Color(HoloLine.r, HoloLine.g, HoloLine.b, i < idx ? 0.9f : 0.55f);
                    }
                    _gaugeFill.color = acc;
                    _gaugeGlow.color = new Color(acc.r, acc.g, acc.b, 0.18f);
                    _knobGlow.color = new Color(acc.r, acc.g, acc.b, 0.7f);
                    _speedCaption.text = idx == 0 ? Loc.T("ПАУЗА · ПРОБЕЛ") : Loc.F("СКОРОСТЬ ×{0}", speed);
                    _speedCaption.color = idx == 0 ? DS.Gold : DS.TextMuted;
                    if (speed > 0) _lastRunSpeed = speed;
                }
            }

            // Кольцо стабильности вокруг портрета
            if (_stabRing != null)
            {
                float target = _avgStability < 0f ? 0f : Mathf.Clamp01(_avgStability / 100f);
                _stabRing.fillAmount = Mathf.MoveTowards(_stabRing.fillAmount, target, Time.unscaledDeltaTime * 0.8f);
                UpdateRulerStats();
                UpdateResourceBars();
            }

            FitRulerText();

            // Название империи и правитель (фракция выбирается уже после постройки HUD)
            var f = SelectedFaction;
            string key = f != null ? f.Name : "";
            if (_empireName != null && key != _empireKey)
            {
                _empireKey = key;
                var leader = LeaderPortraits.ForFaction(f);
                Color accent = leader != null ? leader.Accent : f != null ? f.EmpireColor : DS.NeonCyan;
                _empireName.text = f != null ? f.Name.ToUpper() : "";
                _empireName.color = Color.Lerp(accent, Color.white, 0.15f);
                // Кольцо вокруг портрета — цвета фракции (как рамка портрета и название)
                if (_stabRing != null) _stabRing.color = Color.Lerp(accent, Color.white, 0.1f);
                if (_stabGlow != null) _stabGlow.color = new Color(accent.r, accent.g, accent.b, 0.16f);
                _empireRuler.text = leader != null ? $"{leader.Name} · {leader.Title}" : (f != null ? f.Title : "");
            }
        }

        // ---------- Хелперы ----------

        private static Image HoloImage(Transform parent, string name, Sprite sp, Color c)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<RectTransform>();
            var im = go.AddComponent<Image>();
            im.sprite = sp;
            im.color = c;
            im.raycastTarget = false;
            LG.Ignore(go);
            return im;
        }

        /// <summary>Радиальная заливка от верхней точки по часовой — для колец-прогрессов.</summary>
        private static void RadialFill(Image im, float amount)
        {
            im.type = Image.Type.Filled;
            im.fillMethod = Image.FillMethod.Radial360;
            im.fillOrigin = (int)Image.Origin360.Top;
            im.fillClockwise = true;
            im.fillAmount = amount;
        }
    }
}
