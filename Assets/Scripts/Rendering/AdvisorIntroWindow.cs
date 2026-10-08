using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using StellarisClone.Core;
using Sfx = StellarisClone.Core.Audio.Sfx;

namespace StellarisClone.Rendering
{
    /// <summary>
    /// Первое окно партии — «входящая передача» от правителя выбранной фракции.
    ///   • открытие как захват сигнала: вспышка тонкой линии, окно раскрывается по высоте, короткие помехи;
    ///   • слева живой портрет правителя (моргает, дышит, светится) под бегущими строками развёртки,
    ///     метка «ПРЯМАЯ СВЯЗЬ» с мигающей точкой, имя и титул;
    ///   • справа — название фракции, приветствие и доктрина печатаются по буквам, цитата правителя,
    ///     бонусы фракции чипами с иконками, первая задача и кнопка «Вступить в должность»;
    ///   • фоном — медленно всплывающие частицы данных и проходящая полоса сканирования, кромка «дышит».
    /// Клик по окну, Пробел или Enter — сразу допечатать текст; после этого Enter — вступить в должность.
    /// Всё на unscaled time: до старта партии время стоит.
    /// </summary>
    public sealed class AdvisorIntroWindow : MonoBehaviour
    {
        private const float W = 960f, H = 540f, PortraitW = 340f;
        private const float LineTime = 0.18f, OpenTime = 0.38f, CharsPerSecond = 62f;

        private RectTransform _rt, _content, _sweep, _flashLine, _bonusRow, _scan;
        private CanvasGroup _group, _buttonGroup, _objectiveGroup;
        private LiquidGlassEffect _fx;
        private Text _faction, _title, _body, _leaderName, _leaderTitle, _signalText;
        private Image _recDot, _sepGlow;
        private RawImage _scanlines;
        private RectTransform _portraitHost;
        private LeaderPortraitView _portrait;
        private Button _startBtn;
        private readonly List<Image> _bars = new List<Image>();
        private readonly List<(RectTransform rt, CanvasGroup g)> _chips = new List<(RectTransform, CanvasGroup)>();
        private readonly List<Mote> _motes = new List<Mote>();

        private System.Action _onStart;
        private Color _accent = UIManager.DS.NeonCyan;
        private string _fullText = "";
        private int _visibleTotal, _shown = -1;
        private float _t, _typeStart, _typed, _doneAt = -1f, _nextSweep, _sweepStart = -1f, _nextTick;
        private bool _closing;

        private class Mote
        {
            public RectTransform Rt;
            public Image Img;
            public float Speed, Phase, X;
        }

        // ================================================================ Создание

        public static AdvisorIntroWindow Create(Transform modalCanvas, System.Action onStart)
        {
            var rt = LGBuild.Rect(modalCanvas, "AdvisorIntroModal");
            rt.At(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(W, H));
            var w = rt.gameObject.AddComponent<AdvisorIntroWindow>();
            w._onStart = onStart;
            w.Build(rt);
            rt.gameObject.SetActive(false);
            return w;
        }

        private void Build(RectTransform rt)
        {
            _rt = rt;
            _group = rt.gameObject.AddComponent<CanvasGroup>();
            var bg = rt.gameObject.AddComponent<Image>();
            bg.color = new Color(0.008f, 0.022f, 0.032f, 0.97f);
            bg.raycastTarget = true;
            _fx = LG.Glass(rt.gameObject, 22f);

            // Клик по окну — допечатать текст
            var skip = rt.gameObject.AddComponent<Button>();
            skip.transition = Selectable.Transition.None;
            skip.onClick.AddListener(CompleteTyping);

            _content = LGBuild.Rect(rt, "Content");
            _content.Stretch();

            BuildMotes();
            BuildPortrait();
            BuildRight();

            // Полоса сканирования поверх всего окна
            var mask = LG.RoundedMask(rt, 22f, 1f);
            LG.Ignore(mask.gameObject);
            var sweep = LGBuild.Panel(mask, "Sweep", Color.white);
            sweep.sprite = MenuArt.VerticalFade;
            _sweep = sweep.rectTransform;
            _sweep.anchorMin = new Vector2(0, 1);
            _sweep.anchorMax = new Vector2(1, 1);
            _sweep.pivot = new Vector2(0.5f, 0f);
            _sweep.sizeDelta = new Vector2(0, 70);            // яркий край снизу ведёт полосу, хвост тянется вверх
            sweep.gameObject.SetActive(false);

            // Вспышка захвата сигнала — тонкая линия по центру
            var line = LGBuild.Panel(rt.parent, "SignalLine", Color.white);
            _flashLine = line.rectTransform;
            _flashLine.At(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), _rt.anchoredPosition, new Vector2(W, 2f));
            LG.Ignore(line.gameObject);
            line.raycastTarget = false;
            line.gameObject.SetActive(false);
        }

        private void BuildMotes()
        {
            var mask = LG.RoundedMask(_content, 22f, 2f);
            LG.Ignore(mask.gameObject);
            for (int i = 0; i < 22; i++)
            {
                var img = LGBuild.Panel(mask, "Mote", Color.white);
                img.sprite = MenuArt.SoftDisc;
                float s = Random.Range(3f, 7f);
                img.rectTransform.anchorMin = img.rectTransform.anchorMax = new Vector2(0, 0);
                img.rectTransform.sizeDelta = new Vector2(s, s);
                _motes.Add(new Mote
                {
                    Rt = img.rectTransform, Img = img,
                    Speed = Random.Range(8f, 22f), Phase = Random.Range(0f, 100f), X = Random.Range(0f, W)
                });
            }
        }

        private void BuildPortrait()
        {
            var frame = LGBuild.Panel(_content, "PortraitFrame", new Color(0.01f, 0.03f, 0.04f, 1f));
            frame.rectTransform.anchorMin = new Vector2(0, 0);
            frame.rectTransform.anchorMax = new Vector2(0, 1);
            frame.rectTransform.pivot = new Vector2(0, 0.5f);
            frame.rectTransform.offsetMin = new Vector2(14, 14);
            frame.rectTransform.offsetMax = new Vector2(14 + PortraitW, -14);
            LG.Platter(frame.gameObject, 16f);

            var mask = LG.RoundedMask(frame.rectTransform, 16f, 1f);
            LG.Ignore(mask.gameObject);
            _portraitHost = LGBuild.Rect(mask, "Portrait");
            _portraitHost.Stretch();

            // Бегущие строки развёртки поверх портрета
            _scanlines = LGBuild.Rect(mask, "Scanlines").gameObject.AddComponent<RawImage>();
            _scanlines.rectTransform.Stretch();
            _scanlines.texture = ScanlineTexture();
            _scanlines.color = new Color(1f, 1f, 1f, 0.5f);
            _scanlines.raycastTarget = false;

            // Снизу затемнение под имя
            var shade = LGBuild.Panel(mask, "Shade", new Color(0.005f, 0.015f, 0.022f, 1f));
            shade.sprite = MenuArt.VerticalFade;
            shade.rectTransform.anchorMin = Vector2.zero;
            shade.rectTransform.anchorMax = new Vector2(1, 0.42f);
            shade.rectTransform.offsetMin = shade.rectTransform.offsetMax = Vector2.zero;

            _leaderName = LGBuild.Label(mask, "", 20, Color.white, TextAnchor.LowerLeft, bold: true);
            _leaderName.rectTransform.Stretch(18, 40, 12, 0);
            _leaderTitle = LGBuild.Label(mask, "", 11, UIManager.DS.TextMuted, TextAnchor.LowerLeft, bold: true);
            _leaderTitle.rectTransform.Stretch(18, 20, 12, 0);

            // «● ПРЯМАЯ СВЯЗЬ» в углу
            var rec = LGBuild.Panel(mask, "Rec", new Color(0f, 0f, 0f, 0.55f));
            rec.rectTransform.At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(12, -12), new Vector2(138, 24));
            LG.Chip(rec.gameObject, new Color(1f, 0.3f, 0.3f, 0.45f), 12f);
            _recDot = LGBuild.Panel(rec.transform, "Dot", new Color(1f, 0.25f, 0.25f));
            _recDot.sprite = MenuArt.SoftDisc;
            _recDot.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(14, 0), new Vector2(10, 10));
            var recText = LGBuild.Label(rec.transform, "ПРЯМАЯ СВЯЗЬ", 9, Color.white, TextAnchor.MiddleLeft, bold: true);
            recText.rectTransform.Stretch(26, 0, 6, 0);
        }

        private void BuildRight()
        {
            var right = LGBuild.Rect(_content, "Right");
            right.Stretch(14 + PortraitW + 28, 22, 30, 24);

            // Шапка передачи и индикатор сигнала
            var head = LGBuild.Rect(right, "Head");
            head.TopBand(0, 18);
            var ic = LGIcons.Create(head, LGIcon.Sensors, 14, UIManager.DS.NeonCyan);
            ic.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), Vector2.zero, new Vector2(14, 14));
            _signalText = LGBuild.Label(head, "", 10, UIManager.DS.TextMuted, TextAnchor.MiddleLeft, bold: true);
            _signalText.rectTransform.Stretch(22, 0, 60, 0);
            for (int i = 0; i < 5; i++)
            {
                var bar = LGBuild.Panel(head, "Bar", UIManager.DS.NeonCyan);
                bar.rectTransform.anchorMin = bar.rectTransform.anchorMax = new Vector2(1, 0);
                bar.rectTransform.pivot = new Vector2(0.5f, 0);
                bar.rectTransform.anchoredPosition = new Vector2(-46 + i * 9, 2);
                bar.rectTransform.sizeDelta = new Vector2(5, 4 + i * 3);
                LG.Ignore(bar.gameObject);
                _bars.Add(bar);
            }

            _faction = LGBuild.Label(right, "", 30, Color.white, TextAnchor.UpperLeft, bold: true);
            _faction.rectTransform.TopBand(28, 40);
            _title = LGBuild.Label(right, "", 13, UIManager.DS.TextMuted, TextAnchor.UpperLeft);
            _title.rectTransform.TopBand(70, 20);

            var sep = LGBuild.Panel(right, "Sep", new Color(1f, 1f, 1f, 0.12f));
            sep.rectTransform.TopBand(100, 1);
            LG.Ignore(sep.gameObject);
            _sepGlow = LGBuild.Panel(sep.transform, "Glow", Color.white);
            _sepGlow.sprite = MenuArt.SoftDisc;
            _sepGlow.rectTransform.anchorMin = _sepGlow.rectTransform.anchorMax = new Vector2(0, 0.5f);
            _sepGlow.rectTransform.sizeDelta = new Vector2(160, 6);
            LG.Ignore(_sepGlow.gameObject);

            _body = LGBuild.Label(right, "", 13, UIManager.DS.TextPrimary, TextAnchor.UpperLeft, wrap: true);
            _body.rectTransform.Stretch(0, 146, 0, 114);
            _body.lineSpacing = 1.2f;

            _bonusRow = LGBuild.Rect(right, "Bonuses");
            _bonusRow.anchorMin = new Vector2(0, 0);
            _bonusRow.anchorMax = new Vector2(1, 0);
            _bonusRow.pivot = new Vector2(0, 0);
            _bonusRow.offsetMin = new Vector2(0, 104);
            _bonusRow.offsetMax = new Vector2(0, 136);

            var obj = LGBuild.Rect(right, "Objective");
            obj.anchorMin = new Vector2(0, 0);
            obj.anchorMax = new Vector2(1, 0);
            obj.offsetMin = new Vector2(0, 66);
            obj.offsetMax = new Vector2(0, 90);
            _objectiveGroup = obj.gameObject.AddComponent<CanvasGroup>();
            var oic = LGIcons.Create(obj, LGIcon.Target, 15, UIManager.DS.Gold);
            oic.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), Vector2.zero, new Vector2(15, 15));
            var ot = LGBuild.Label(obj, "<color=#FFCC52><b>Первая задача:</b></color>  отправьте научный корабль на разведку приграничных систем",
                                   12, UIManager.DS.TextPrimary, TextAnchor.MiddleLeft);
            ot.rectTransform.Stretch(24, 0, 0, 0);

            _startBtn = LGBuild.Button(right, "StartGameBtn", UIManager.DS.BtnSuccess, UIManager.DS.Green, Confirm, LGIcon.Play, "ВСТУПИТЬ В ДОЛЖНОСТЬ", 13);
            var brt = (RectTransform)_startBtn.transform;
            brt.At(new Vector2(1, 0), new Vector2(1, 0), Vector2.zero, new Vector2(300, 46));
            _buttonGroup = _startBtn.gameObject.AddComponent<CanvasGroup>();
        }

        private static Texture2D s_scan;

        private static Texture2D ScanlineTexture()
        {
            if (s_scan != null) return s_scan;
            s_scan = new Texture2D(1, 4, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear };
            s_scan.SetPixels(new[] { new Color(0, 0, 0, 0.55f), new Color(0, 0, 0, 0.15f), new Color(0, 0, 0, 0f), new Color(0, 0, 0, 0.1f) });
            s_scan.Apply();
            return s_scan;
        }

        // ================================================================ Открытие

        public void Open(FactionInfo faction)
        {
            var leader = LeaderPortraits.ForFaction(faction);
            _accent = leader != null ? leader.Accent : faction != null ? faction.EmpireColor : UIManager.DS.NeonCyan;

            LGBuild.Clear(_portraitHost);
            _portrait = LeaderPortraitView.Create(_portraitHost, leader, 1.25f, new Vector4(0f, 0.12f, 0f, 0f), true);
            _leaderName.text = leader != null ? leader.Name : faction?.Name ?? "";
            _leaderName.color = Color.Lerp(_accent, Color.white, 0.55f);
            _leaderTitle.text = leader != null ? leader.Title.ToUpper() : "";

            _faction.text = faction != null ? faction.Name.ToUpper() : "";
            _faction.color = Color.Lerp(_accent, Color.white, 0.25f);
            _title.text = faction != null ? faction.Title : "";
            _sepGlow.color = new Color(_accent.r, _accent.g, _accent.b, 0.9f);
            foreach (var b in _bars) b.color = Color.Lerp(_accent, Color.white, 0.2f);

            _fullText = BuildText(faction, leader);
            _visibleTotal = VisibleLength(_fullText);
            BuildBonuses(faction);

            _t = 0f;
            _typed = 0f;
            _shown = -1;
            _doneAt = -1f;
            _typeStart = LineTime + OpenTime + 0.15f;
            _sweepStart = LineTime + OpenTime * 0.6f;
            _nextSweep = 7f;
            _closing = false;
            _body.text = "";
            _buttonGroup.alpha = 0f;
            _buttonGroup.interactable = _buttonGroup.blocksRaycasts = false;
            _objectiveGroup.alpha = 0f;
            _group.alpha = 1f;
            _group.interactable = _group.blocksRaycasts = true;
            _rt.localScale = new Vector3(1f, 0.02f, 1f);
            _content.gameObject.SetActive(false);

            gameObject.SetActive(true);
            transform.SetAsLastSibling();
            _flashLine.SetAsLastSibling();
            _flashLine.gameObject.SetActive(true);
            SFXManager.Play(Sfx.NotifyInfo, 0.8f, 0.9f);
        }

        private string BuildText(FactionInfo f, LeaderPortraits.Entry leader)
        {
            string desc = f?.Description ?? "";
            int cut = desc.IndexOf("\n\n<b>", System.StringComparison.Ordinal);
            if (cut >= 0) desc = desc.Substring(0, cut);
            string hex = LGBuild.Hex(Color.Lerp(_accent, Color.white, 0.35f));
            var sb = new StringBuilder();
            sb.Append("<b>Приветствую, Командующий!</b>\n\n");
            sb.Append("Бортовой тактический сервер развёрнут, все сенсоры на связи. ");
            sb.Append($"Вы принимаете командование: <b>{f?.Name}</b>.\n\n");
            sb.Append(desc);
            if (leader != null)
                sb.Append($"\n\n<i><color={hex}>«{leader.Quote}»</color></i>");
            return sb.ToString();
        }

        private void BuildBonuses(FactionInfo f)
        {
            LGBuild.Clear(_bonusRow);
            _chips.Clear();
            if (f == null) return;
            var list = new List<(LGIcon icon, Color col, string text)>();
            void Add(float mult, LGIcon icon, Color col, string name)
            {
                int pct = Mathf.RoundToInt((mult - 1f) * 100f);
                if (pct != 0) list.Add((icon, col, $"{(pct > 0 ? "+" : "")}{pct}%  {name}"));
            }
            Add(f.InfluenceBonus, LGIcon.Influence, new Color(1.00f, 0.48f, 0.58f), "влияние");
            Add(f.EnergyBonus, LGIcon.Energy, UIManager.DS.Gold, "гелий-3");
            Add(f.MineralBonus, LGIcon.Minerals, UIManager.DS.NeonCyan, "титан");
            Add(f.AlloyBonus, LGIcon.Alloys, new Color(0.95f, 0.62f, 0.36f), "сплавы");

            float x = 0f;
            foreach (var (icon, col, text) in list)
            {
                float w = 34f + text.Length * 7.6f;
                var chip = LGBuild.Panel(_bonusRow, "Bonus", new Color(col.r * 0.12f, col.g * 0.12f, col.b * 0.12f, 0.95f));
                var crt = chip.rectTransform;
                crt.anchorMin = crt.anchorMax = new Vector2(0, 0.5f);
                crt.pivot = new Vector2(0, 0.5f);
                crt.anchoredPosition = new Vector2(x, 0);
                crt.sizeDelta = new Vector2(w, 30);
                LG.Chip(chip.gameObject, new Color(col.r, col.g, col.b, 0.6f), 15f);
                LGIcons.IconLabel(chip.transform, icon, text, 12, col, Color.white, 15f);
                var g = chip.gameObject.AddComponent<CanvasGroup>();
                g.alpha = 0f;
                _chips.Add((crt, g));
                x += w + 10f;
            }
        }

        // ================================================================ Закрытие

        private void Confirm()
        {
            if (_closing) return;
            if (_typed < _visibleTotal) { CompleteTyping(); return; }
            _closing = true;
            _group.interactable = _group.blocksRaycasts = false;
            _onStart?.Invoke();
        }

        private void CompleteTyping()
        {
            if (_closing || _t < _typeStart) return;
            _typed = _visibleTotal;
        }

        // ================================================================ Анимация

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;
            _t += dt;

            if (_closing)
            {
                _group.alpha = Mathf.MoveTowards(_group.alpha, 0f, dt * 4f);
                float s = Mathf.Lerp(1f, 1.03f, 1f - _group.alpha);
                _rt.localScale = new Vector3(s, s * Mathf.Lerp(0.85f, 1f, _group.alpha), 1f);
                if (_group.alpha <= 0f) gameObject.SetActive(false);
                return;
            }

            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) Confirm();
            else if (Input.GetKeyDown(KeyCode.Space)) CompleteTyping();

            AnimateOpen();
            AnimateTyping(dt);
            AnimateAmbient();
        }

        /// <summary>Линия-вспышка → окно раскрывается по высоте → короткие помехи.</summary>
        private void AnimateOpen()
        {
            if (_t < LineTime)
            {
                float k = Mathf.SmoothStep(0f, 1f, _t / LineTime);
                _flashLine.sizeDelta = new Vector2(W * k, 2f);
                _flashLine.GetComponent<Image>().color = new Color(_accent.r, _accent.g, _accent.b, 1f);
                _rt.localScale = new Vector3(1f, 0.02f, 1f);
                _group.alpha = 0f;
                return;
            }

            float o = Mathf.Clamp01((_t - LineTime) / OpenTime);
            if (!_content.gameObject.activeSelf) _content.gameObject.SetActive(true);
            float ease = 1f - Mathf.Pow(1f - o, 3f);
            float overshoot = Mathf.Sin(o * Mathf.PI) * 0.025f;
            _rt.localScale = new Vector3(1f + overshoot * 0.3f, Mathf.Lerp(0.02f, 1f, ease) + overshoot, 1f);

            // Помехи в первые полсекунды: окно мерцает
            float since = _t - LineTime;
            float flicker = since < 0.55f ? (Mathf.PerlinNoise(_t * 40f, 0.3f) > 0.35f ? 1f : 0.55f) : 1f;
            _group.alpha = Mathf.Clamp01(o * 1.6f) * flicker;

            var li = _flashLine.GetComponent<Image>();
            float la = 1f - Mathf.Clamp01(since / 0.3f);
            li.color = new Color(_accent.r, _accent.g, _accent.b, la);
            if (la <= 0f && _flashLine.gameObject.activeSelf) _flashLine.gameObject.SetActive(false);
        }

        /// <summary>Текст печатается по буквам (теги rich text не рвутся), за ним — бонусы, задача и кнопка.</summary>
        private void AnimateTyping(float dt)
        {
            if (_t < _typeStart) return;
            if (_typed < _visibleTotal)
            {
                _typed = Mathf.Min(_visibleTotal, _typed + dt * CharsPerSecond);
                if (_t >= _nextTick)
                {
                    _nextTick = _t + 0.07f;
                    SFXManager.Play(Sfx.UiHover, 0.12f, Random.Range(1.4f, 1.7f));
                }
            }

            int n = Mathf.FloorToInt(_typed);
            bool typing = n < _visibleTotal;
            if (n != _shown || typing)
            {
                _shown = n;
                bool caret = typing && Mathf.Repeat(_t, 0.5f) < 0.3f;
                _body.text = VisibleSubstring(_fullText, n) + (typing ? (caret ? "<color=#7FF5EA>▌</color>" : " ") : "");
            }

            if (!typing && _doneAt < 0f)
            {
                _doneAt = _t;
                SFXManager.Play(Sfx.UiConfirm, 0.5f, 1.1f);
            }
            if (_doneAt < 0f) return;

            float since = _t - _doneAt;
            for (int i = 0; i < _chips.Count; i++)
            {
                float k = Mathf.Clamp01((since - i * 0.12f) / 0.3f);
                float e = 1f - Mathf.Pow(1f - k, 3f);
                _chips[i].g.alpha = e;
                _chips[i].rt.localScale = Vector3.one * Mathf.Lerp(0.8f, 1f, e);
            }
            float ok = Mathf.Clamp01((since - 0.2f) / 0.35f);
            _objectiveGroup.alpha = ok;
            float bk = Mathf.Clamp01((since - 0.35f) / 0.35f);
            _buttonGroup.alpha = bk;
            bool ready = bk > 0.5f;
            _buttonGroup.interactable = _buttonGroup.blocksRaycasts = ready;
            if (bk >= 1f)
            {
                // Кнопка мягко «дышит», приглашая начать
                float p = 1f + 0.025f * Mathf.Sin(_t * 3.2f);
                _startBtn.transform.localScale = new Vector3(p, p, 1f);
            }
        }

        private void AnimateAmbient()
        {
            float pulse = 0.5f + 0.5f * Mathf.Sin(_t * 1.6f);
            _fx?.SetRim(new Color(_accent.r, _accent.g, _accent.b, Mathf.Lerp(0.35f, 0.8f, pulse)));
            if (_portrait != null) _portrait.Glow = Mathf.Lerp(0.25f, 0.7f, 0.5f + 0.5f * Mathf.Sin(_t * 1.1f));

            // Бегущие строки и мигающая точка «ПРЯМАЯ СВЯЗЬ»
            var r = _scanlines.rectTransform.rect;
            _scanlines.uvRect = new Rect(0f, -_t * 0.6f, 1f, Mathf.Max(1f, r.height / 3f));
            _recDot.color = new Color(1f, 0.25f, 0.25f, Mathf.Repeat(_t, 1.2f) < 0.7f ? 1f : 0.15f);

            // Индикатор сигнала и подпись канала
            for (int i = 0; i < _bars.Count; i++)
            {
                float lvl = Mathf.PerlinNoise(_t * 2.2f, i * 0.7f);
                var c = _bars[i].color;
                c.a = lvl > 0.25f + i * 0.08f ? 1f : 0.2f;
                _bars[i].color = c;
            }
            _signalText.text = $"ВХОДЯЩАЯ ПЕРЕДАЧА   ·   КАНАЛ ПРАВИТЕЛЬСТВА   ·   <color=#7FF5EA>{(int)(_t * 37.3f) % 1000:000}</color>";

            // Блик бежит по разделителю
            float sx = Mathf.Repeat(_t * 0.35f, 1.4f) - 0.2f;
            var sr = (RectTransform)_sepGlow.transform.parent;
            _sepGlow.rectTransform.anchoredPosition = new Vector2(sx * sr.rect.width, 0f);

            // Полоса сканирования: при открытии и потом изредка
            if (_sweepStart < 0f && _t >= _nextSweep) { _sweepStart = _t; _nextSweep = _t + Random.Range(6f, 9f); }
            if (_sweepStart >= 0f && _t >= _sweepStart)
            {
                float k = (_t - _sweepStart) / 1.3f;
                if (k >= 1f) { _sweepStart = -1f; _sweep.gameObject.SetActive(false); }
                else
                {
                    if (!_sweep.gameObject.activeSelf) _sweep.gameObject.SetActive(true);
                    _sweep.anchoredPosition = new Vector2(0f, -k * (H + 70f));
                    _sweep.GetComponent<Image>().color = new Color(_accent.r, _accent.g, _accent.b, 0.10f * Mathf.Sin(k * Mathf.PI));
                }
            }

            // Частицы данных медленно всплывают и мерцают
            foreach (var m in _motes)
            {
                float y = Mathf.Repeat(m.Phase * 13f + _t * m.Speed, H + 20f) - 10f;
                float x = m.X + Mathf.Sin(_t * 0.4f + m.Phase) * 14f;
                m.Rt.anchoredPosition = new Vector2(x, y);
                float tw = 0.5f + 0.5f * Mathf.Sin(_t * 2f + m.Phase * 3f);
                m.Img.color = new Color(_accent.r, _accent.g, _accent.b, 0.06f + 0.16f * tw);
            }
        }

        // ================================================================ Rich text

        /// <summary>Число видимых символов (без тегов).</summary>
        private static int VisibleLength(string s)
        {
            int n = 0;
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] == '<')
                {
                    int close = s.IndexOf('>', i);
                    if (close > i) { i = close; continue; }
                }
                n++;
            }
            return n;
        }

        /// <summary>Первые n видимых символов; открытые теги закрываются, чтобы Text не показал их как текст.</summary>
        private static string VisibleSubstring(string s, int n)
        {
            var sb = new StringBuilder();
            var open = new Stack<string>();
            int count = 0;
            for (int i = 0; i < s.Length && count < n; i++)
            {
                if (s[i] == '<')
                {
                    int close = s.IndexOf('>', i);
                    if (close > i)
                    {
                        string tag = s.Substring(i + 1, close - i - 1);
                        if (tag.StartsWith("/")) { if (open.Count > 0) open.Pop(); }
                        else
                        {
                            int eq = tag.IndexOf('=');
                            open.Push(eq >= 0 ? tag.Substring(0, eq) : tag);
                        }
                        sb.Append(s, i, close - i + 1);
                        i = close;
                        continue;
                    }
                }
                sb.Append(s[i]);
                count++;
            }
            while (open.Count > 0) sb.Append("</").Append(open.Pop()).Append('>');
            return sb.ToString();
        }
    }
}
