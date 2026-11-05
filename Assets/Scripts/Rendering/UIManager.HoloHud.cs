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
        private const float DiskD = 148f;          // диаметр диска времени
        private const float DiskCenterY = 62f;     // центр диска от верха экрана
        private const float WingH = 64f;
        private const float WingGap = 18f;          // зазор между диском и крылом
        private const float CellStep = 148f;        // ширина ячейки ресурса (144) + зазор
        private const float RulerD = RulerBadge.Width;
        private static readonly Vector2 RulerCenter = new Vector2(20f + RulerD * 0.5f, -(18f + RulerD * 0.5f));

        /// <summary>Нижний край верха HUD (портрет с кольцами) — от него ставится левое меню.</summary>
        public const float HoloTopBottom = 18f + RulerBadge.Height + 16f;

        private Text _diskMonth, _diskDay, _diskYear, _speedCaption;
        private Image _monthRing, _stabRing, _stabTicks;
        private Text _empireName, _empireRuler;
        private readonly Image[] _arcChips = new Image[4];
        private readonly Image[] _arcIcons = new Image[4];
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
        }

        // ---------- Правитель ----------

        private void BuildRulerCluster()
        {
            var root = _canvas.transform;
            // Шкала и кольцо стабильности — под портретом (портрет поверх, он ловит клики)
            _stabTicks = HoloImage(root, "RulerTicks", HudArt.Ticks(60, 5, 0.95f, 0.90f, 1f), new Color(HoloLine.r, HoloLine.g, HoloLine.b, 0.35f));
            _stabTicks.rectTransform.At(new Vector2(0, 1), new Vector2(0.5f, 0.5f), RulerCenter, Vector2.one * (RulerD + 32f));
            var track = HoloImage(root, "StabTrack", HudArt.Ring(0.92f, 1f), new Color(1f, 1f, 1f, 0.07f));
            track.rectTransform.At(new Vector2(0, 1), new Vector2(0.5f, 0.5f), RulerCenter, Vector2.one * (RulerD + 18f));
            _stabRing = HoloImage(root, "StabRing", HudArt.Ring(0.92f, 1f), new Color(0.94f, 0.56f, 0.86f, 1f));
            _stabRing.rectTransform.At(new Vector2(0, 1), new Vector2(0.5f, 0.5f), RulerCenter, Vector2.one * (RulerD + 18f));
            RadialFill(_stabRing, 0f);
            track.raycastTarget = true;
            TooltipHelper.Attach(track.gameObject, Loc.T("<b>Кольцо стабильности</b>\nСредняя стабильность колоний (по числу жителей)."));

            RulerBadge.Create(root, new Vector2(20f, -18f), OpenEmpireOverviewModal);

            float x = 20f + RulerD + 26f;
            _empireName = CreateText(root, "", 17, FontStyle.Normal, DS.NeonCyan, TextAnchor.LowerLeft);
            _empireName.font = GameFont.Display;
            _empireName.rectTransform.At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(x, -24f), new Vector2(230f, 24f));
            _empireName.resizeTextForBestFit = true;
            _empireName.resizeTextMinSize = 11;
            _empireName.resizeTextMaxSize = 17;
            _empireName.horizontalOverflow = HorizontalWrapMode.Wrap;
            _empireName.Track(1.0f);

            _empireRuler = CreateText(root, "", 12, FontStyle.Normal, DS.TextMuted, TextAnchor.UpperLeft);
            _empireRuler.rectTransform.At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(x, -52f), new Vector2(230f, 34f));
            _empireRuler.horizontalOverflow = HorizontalWrapMode.Wrap;
        }

        // ---------- Крылья ресурсов ----------

        private void BuildResourceWings()
        {
            float half = DiskD * 0.5f + WingGap;

            var left = Wing("WingLeft", true, -half);
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

            var glow = HoloImage(disk, "Glow", FactionSelectScreen.RadialSprite(), new Color(HoloLine.r, HoloLine.g, HoloLine.b, 0.14f));
            glow.rectTransform.At(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.one * (DiskD + 120f));

            var ticks = HoloImage(disk, "Ticks", HudArt.Ticks(72, 6, 0.95f, 0.89f, 1f), new Color(HoloLine.r, HoloLine.g, HoloLine.b, 0.5f));
            ticks.rectTransform.At(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.one * (DiskD + 34f));

            var track = HoloImage(disk, "MonthTrack", HudArt.Ring(0.96f, 1f), new Color(1f, 1f, 1f, 0.08f));
            track.rectTransform.At(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.one * (DiskD + 18f));
            _monthRing = HoloImage(disk, "MonthRing", HudArt.Ring(0.96f, 1f), HoloLine);
            _monthRing.rectTransform.At(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.one * (DiskD + 18f));
            RadialFill(_monthRing, 0f);

            // Сам диск — кнопка паузы
            var face = HoloImage(disk, "Face", HudArt.Disc(), new Color(0.028f, 0.072f, 0.098f, 0.97f));
            face.rectTransform.Stretch();
            face.raycastTarget = true;
            var faceBtn = face.gameObject.AddComponent<Button>();
            faceBtn.transition = Selectable.Transition.None;
            faceBtn.onClick.AddListener(TogglePauseFromDisk);
            TooltipHelper.Attach(face.gameObject, Loc.T("<b>Дата</b>\nКольцо — ход текущего месяца.\nЩелчок — пауза / продолжить  <color=#8AA2A8>(ПРОБЕЛ)</color>"));
            var edge = HoloImage(disk, "Edge", HudArt.Ring(0.975f, 1f), new Color(HoloLine.r, HoloLine.g, HoloLine.b, 0.45f));
            edge.rectTransform.Stretch();

            _diskMonth = CreateText(disk, "", 10, FontStyle.Bold, HoloLine, TextAnchor.MiddleCenter);
            _diskMonth.rectTransform.At(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 34f), new Vector2(DiskD, 16f));
            _diskMonth.Track(2.6f);
            _diskDay = CreateText(disk, "", 40, FontStyle.Normal, DS.TextPrimary, TextAnchor.MiddleCenter);
            _diskDay.font = GameFont.Display;
            _diskDay.rectTransform.At(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 4f), new Vector2(DiskD, 46f));
            var dayGlow = _diskDay.gameObject.AddComponent<Outline>();
            dayGlow.effectColor = new Color(HoloLine.r, HoloLine.g, HoloLine.b, 0.18f);
            dayGlow.effectDistance = new Vector2(1.5f, -1.5f);
            _diskYear = CreateText(disk, "", 13, FontStyle.Bold, DS.TextMuted, TextAnchor.MiddleCenter);
            _diskYear.rectTransform.At(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -28f), new Vector2(DiskD, 18f));
            _diskYear.Track(3.4f);

            BuildSpeedArc(disk);

            _speedCaption = CreateText(disk, "", 9, FontStyle.Bold, DS.TextMuted, TextAnchor.MiddleCenter);
            _speedCaption.rectTransform.At(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -(DiskD * 0.5f + 62f)), new Vector2(200f, 14f));
            _speedCaption.Track(1.8f);

            // Музыка — кнопка справа под диском, панель плеера — под ней
            var music = LGBuild.Rect(disk, "MusicHolder");
            music.At(new Vector2(0.5f, 0.5f), new Vector2(0f, 0.5f), new Vector2(DiskD * 0.5f + 34f, -(DiskD * 0.5f + 20f)), new Vector2(30f, 30f));
            MusicPlayerPanel.CreateToggle(music, 0f, _canvas, -(DiskCenterY + DiskD * 0.5f + 70f));
        }

        private void BuildSpeedArc(RectTransform disk)
        {
            var arc = LGBuild.Rect(disk, "SpeedArc");
            arc.At(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -(DiskD * 0.5f + 28f)), new Vector2(170f, 44f));
            UIAnchors.Register(UIAnchors.Speed, arc);

            LGIcon[] icons = { LGIcon.Pause, LGIcon.Play, LGIcon.Fast, LGIcon.Fastest };
            string[] tips =
            {
                Loc.T("<b>Пауза</b>\nКлавиша: ПРОБЕЛ"),
                Loc.T("<b>Нормальная скорость</b>\nКлавиша: 1"),
                Loc.T("<b>Быстро</b>\nКлавиша: 2"),
                Loc.T("<b>Очень быстро</b>\nКлавиша: 3")
            };
            int[] speeds = { 0, 1, 2, 4 };      // как клавиши: 3 — самая быстрая скорость ×4
            float[] deg = { 124f, 102f, 78f, 56f }; // углы от +X по часовой (экранные: вниз — 90°)
            const float r = 104f;
            for (int i = 0; i < 4; i++)
            {
                int captured = speeds[i];
                float a = deg[i] * Mathf.Deg2Rad;
                var pos = new Vector2(Mathf.Cos(a) * r, -Mathf.Sin(a) * r) - arc.anchoredPosition;
                var chip = HoloImage(arc, $"Speed_{i}", HudArt.CutPanel(6), new Color(0.05f, 0.11f, 0.15f, 0.96f));
                chip.type = Image.Type.Sliced;
                chip.raycastTarget = true;
                chip.rectTransform.At(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), pos, new Vector2(36f, 22f));
                chip.rectTransform.localRotation = Quaternion.Euler(0, 0, 90f - deg[i]);
                var b = chip.gameObject.AddComponent<Button>();
                b.transition = Selectable.Transition.None;
                b.onClick.AddListener(() => SetSpeed(captured));
                TooltipHelper.Attach(chip.gameObject, tips[i]);
                _arcChips[i] = chip;
                _arcIcons[i] = LGIcons.Create(chip.transform, icons[i], i == 3 ? 13f : 11f, DS.TextMuted);
                _arcIcons[i].raycastTarget = false;
            }
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

                int speed = tm.CurrentSpeed;
                int idx = Mathf.Clamp(speed >= 3 ? 3 : speed, 0, 3);
                if (idx != _arcShown)
                {
                    _arcShown = idx;
                    for (int i = 0; i < 4; i++)
                    {
                        bool on = i == idx;
                        _arcChips[i].color = on ? (i == 0 ? DS.Gold : HoloLine) : new Color(0.05f, 0.11f, 0.15f, 0.96f);
                        _arcIcons[i].color = on ? new Color(0.01f, 0.06f, 0.08f, 1f) : DS.TextMuted;
                    }
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
                if (_avgStability >= 0f)
                    _stabRing.color = PlanetStability.TierColor(PlanetStability.TierOf(_avgStability));
            }

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
