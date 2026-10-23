using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using StellarisClone.Core;
using StellarisClone.Generation;
using Sfx = StellarisClone.Core.Audio.Sfx;

namespace StellarisClone.Rendering
{
    /// <summary>
    /// «Оформление империи» — второй шаг после выбора фракции: своё название, строй и титул, имя правителя,
    /// цвет империи на карте (границы и корабли) и герб. Бонусы, корпуса кораблей, вид и история остаются от фракции.
    /// Слева — живое превью герба и подписи; у каждого текстового поля — кубик «случайно», внизу — «Случайно всё».
    /// Результат — копия фракции (общие экземпляры выбирает и ИИ).
    /// </summary>
    public class EmpireCustomizePanel : MonoBehaviour
    {
        private const float W = 1080f, H = 680f, PreviewW = 330f;

        /// <summary>Цвета империи на карте: первый — привычная бирюза игрока.</summary>
        public static readonly Color[] Palette =
        {
            new Color(0.22f, 0.92f, 0.86f), new Color(0.25f, 0.62f, 1f), new Color(0.62f, 0.45f, 1f), new Color(0.95f, 0.42f, 0.85f),
            new Color(0.95f, 0.30f, 0.32f), new Color(1f, 0.56f, 0.22f), new Color(1f, 0.82f, 0.30f), new Color(0.58f, 0.95f, 0.32f),
            new Color(0.22f, 0.85f, 0.52f), new Color(0.84f, 0.88f, 0.94f)
        };

        private static readonly string[] EmblemKeys = { "astrea", "xarn", "aquila", "iridia", "terraan", "nol" };

        private static string[] Forms => new[]
        {
            Loc.T("Республика"), Loc.T("Империя"), Loc.T("Доминион"), Loc.T("Федерация"), Loc.T("Синдикат"), Loc.T("Конклав"),
            Loc.T("Союз"), Loc.T("Гегемония"), Loc.T("Содружество"), Loc.T("Протекторат"), Loc.T("Директорат"), Loc.T("Лига")
        };

        private static string[] Titles => new[]
        {
            Loc.T("Свободная Федерация"), Loc.T("Военная Иерархия"), Loc.T("Корпоративный Альянс"), Loc.T("Теократия Провидцев"),
            Loc.T("Звёздная Монархия"), Loc.T("Технократия"), Loc.T("Совет Старейшин"), Loc.T("Народная Коммуна"),
            Loc.T("Торговая Лига"), Loc.T("Орден Хранителей"), Loc.T("Живой Конклав Корней"), Loc.T("Пограничный Протекторат")
        };

        private FactionInfo _base, _f;
        private Action<FactionInfo> _done;
        private Action _back;

        private InputField _name, _title, _ruler;
        private Image _prevEmblem, _prevGlow, _prevStrip;
        private Text _prevName, _prevTitle, _prevRuler, _baseText;
        private readonly List<(Image ring, int idx)> _swatches = new List<(Image, int)>();
        private readonly List<(Image bg, LiquidGlassEffect fx, Image emblem, string key)> _emblems = new List<(Image, LiquidGlassEffect, Image, string)>();
        private int _colorIdx;
        private bool _syncing;

        private static Color Muted => UIManager.DS.TextMuted;
        private static Color Primary => UIManager.DS.TextPrimary;
        private static Color Cyan => UIManager.DS.NeonCyan;

        public static EmpireCustomizePanel Create(RectTransform host)
        {
            var rt = LGBuild.Rect(host, "EmpireCustomize");
            rt.Stretch();
            var p = rt.gameObject.AddComponent<EmpireCustomizePanel>();
            p.Build(rt);
            rt.gameObject.SetActive(false);
            return p;
        }

        public bool IsOpen => gameObject.activeSelf;

        public void Open(FactionInfo baseFaction, Action<FactionInfo> done, Action back)
        {
            _base = baseFaction;
            _done = done;
            _back = back;
            _f = baseFaction.Clone();
            _f.RulerName = LeaderPortraits.ForFaction(baseFaction)?.Name;
            _f.EmblemKey = null;
            _colorIdx = 0;
            _f.MapColor = Palette[0];
            _baseText.text = Loc.F("Основа — <b>{0}</b>: бонусы, корабли, вид и история фракции сохраняются. Остальное — на ваш вкус.", baseFaction.Name);
            _syncing = true;
            _name.text = _f.Name;
            _title.text = _f.Title;
            _ruler.text = _f.RulerName ?? "";
            _syncing = false;
            RefreshAll();
            gameObject.SetActive(true);
            transform.SetAsLastSibling();
            LG.Show(gameObject);
        }

        private void Update()
        {
            if (!IsOpen) return;
            if (Input.GetKeyDown(KeyCode.Escape)) { Back(); return; }
            if ((Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) && !Typing()) Confirm();
        }

        private static bool Typing()
        {
            var es = UnityEngine.EventSystems.EventSystem.current;
            var go = es != null ? es.currentSelectedGameObject : null;
            var input = go != null ? go.GetComponent<InputField>() : null;
            return input != null && input.isFocused;
        }

        // ================================================================ Каркас

        private void Build(RectTransform rt)
        {
            var scrim = LGBuild.Panel(rt, "Scrim", new Color(0.004f, 0.008f, 0.014f, 0.82f), raycast: true);
            scrim.rectTransform.Stretch();
            LG.Ignore(scrim.gameObject, includeChildren: false);

            var card = LGBuild.Panel(rt, "Card", new Color(0.014f, 0.030f, 0.044f, 0.97f), raycast: true);
            card.rectTransform.At(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(W, H));
            LG.Glass(card.gameObject).SetRim(new Color(0.45f, 0.95f, 0.90f, 0.40f));
            LG.Motion(card.gameObject, LGAppear.Kind.Pop).fromScale = 0.97f;
            var c = card.rectTransform;

            var title = LGBuild.Label(c, Loc.T("ОФОРМЛЕНИЕ ИМПЕРИИ"), 26, Color.white, TextAnchor.UpperLeft, bold: true);
            title.rectTransform.At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(32, -22), new Vector2(700, 36));
            title.Track(1.6f);
            _baseText = LGBuild.Label(c, "", 13, Muted, TextAnchor.UpperLeft, wrap: true);
            _baseText.rectTransform.At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(33, -60), new Vector2(W - 70, 20));

            BuildPreview(c);
            BuildFields(c);
            BuildButtons(c);
        }

        private void BuildPreview(RectTransform c)
        {
            var pv = LGBuild.Panel(c, "Preview", new Color(0.020f, 0.045f, 0.065f, 0.9f));
            pv.rectTransform.anchorMin = new Vector2(0, 0); pv.rectTransform.anchorMax = new Vector2(0, 1);
            pv.rectTransform.pivot = new Vector2(0, 0.5f);
            pv.rectTransform.offsetMin = new Vector2(24, 92); pv.rectTransform.offsetMax = new Vector2(24 + PreviewW, -96);
            LG.Platter(pv.gameObject, 10f);
            var p = pv.rectTransform;

            _prevGlow = LGBuild.Panel(p, "Glow", Color.white);
            _prevGlow.sprite = FactionSelectScreen.RadialSprite();
            _prevGlow.raycastTarget = false;
            _prevGlow.rectTransform.At(new Vector2(0.5f, 1), new Vector2(0.5f, 0.5f), new Vector2(0, -110), new Vector2(260, 260));
            LG.Ignore(_prevGlow.gameObject);
            _prevEmblem = FactionEmblems.Create(p, FactionEmblems.ForKey(EmblemKeys[0]), 150f, Color.white, "PreviewEmblem");
            if (_prevEmblem != null) _prevEmblem.rectTransform.At(new Vector2(0.5f, 1), new Vector2(0.5f, 0.5f), new Vector2(0, -110), new Vector2(150, 150));

            _prevName = LGBuild.Label(p, "", 20, Color.white, TextAnchor.UpperCenter, bold: true, wrap: true);
            _prevName.rectTransform.TopBand(206, 54, 18, 18);
            _prevTitle = LGBuild.Label(p, "", 13, Muted, TextAnchor.UpperCenter, wrap: true);
            _prevTitle.rectTransform.TopBand(262, 20, 18, 18);
            var line = LGBuild.Panel(p, "Line", new Color(1f, 1f, 1f, 0.10f));
            line.rectTransform.TopBand(294, 1, 30, 30);
            var rl = LGBuild.Label(p, Loc.T("ПРАВИТЕЛЬ"), 10, Muted, TextAnchor.UpperCenter, bold: true);
            rl.rectTransform.TopBand(306, 14, 18, 18);
            rl.Track(1.6f);
            _prevRuler = LGBuild.Label(p, "", 16, Primary, TextAnchor.UpperCenter, bold: true, wrap: true);
            _prevRuler.rectTransform.TopBand(324, 40, 18, 18);

            _prevStrip = LGBuild.Panel(p, "ColorStrip", Cyan);
            _prevStrip.rectTransform.anchorMin = new Vector2(0, 0); _prevStrip.rectTransform.anchorMax = new Vector2(1, 0);
            _prevStrip.rectTransform.offsetMin = new Vector2(30, 22); _prevStrip.rectTransform.offsetMax = new Vector2(-30, 25);
            var sc = LGBuild.Label(p, Loc.T("цвет на карте: границы и корабли"), 10, Muted, TextAnchor.LowerCenter);
            sc.rectTransform.anchorMin = new Vector2(0, 0); sc.rectTransform.anchorMax = new Vector2(1, 0);
            sc.rectTransform.offsetMin = new Vector2(18, 30); sc.rectTransform.offsetMax = new Vector2(-18, 46);
        }

        private void BuildFields(RectTransform c)
        {
            float x0 = 24f + PreviewW + 28f;
            float y = 96f;
            _name = Field(c, x0, ref y, Loc.T("НАЗВАНИЕ ИМПЕРИИ"), Loc.T("Например: Республика Астрея"), 34, RandomName,
                v => { if (!_syncing) { _f.Name = Clean(v, _base.Name); RefreshPreview(); } });
            _title = Field(c, x0, ref y, Loc.T("СТРОЙ И ТИТУЛ"), Loc.T("Например: Свободная Федерация"), 34, RandomTitle,
                v => { if (!_syncing) { _f.Title = Clean(v, _base.Title); RefreshPreview(); } });
            _ruler = Field(c, x0, ref y, Loc.T("ИМЯ ПРАВИТЕЛЯ"), Loc.T("Как зовут главу государства"), 28, RandomRuler,
                v => { if (!_syncing) { _f.RulerName = Clean(v, LeaderPortraits.ForFaction(_base)?.Name); RefreshPreview(); } });

            // Цвет
            var cl = LGBuild.Label(c, Loc.T("ЦВЕТ ИМПЕРИИ НА КАРТЕ"), 11, Muted, TextAnchor.UpperLeft, bold: true);
            cl.rectTransform.At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(x0, -y), new Vector2(400, 16));
            cl.Track(1.4f);
            y += 22f;
            for (int i = 0; i < Palette.Length; i++)
            {
                int idx = i;
                float sx = x0 + i * 52f;
                var sw = LGBuild.Panel(c, "Swatch", Palette[i], raycast: true);
                sw.sprite = Disc();
                sw.rectTransform.At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(sx + 5f, -y - 5f), new Vector2(36, 36));
                var btn = sw.gameObject.AddComponent<Button>();
                btn.transition = Selectable.Transition.None;
                btn.onClick.AddListener(() => PickColor(idx));
                var ring = LGBuild.Panel(c, "Ring", Color.white);
                ring.sprite = Ring();
                ring.raycastTarget = false;
                ring.rectTransform.At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(sx, -y), new Vector2(46, 46));
                _swatches.Add((ring, idx));
            }
            y += 64f;

            // Герб
            var el = LGBuild.Label(c, Loc.T("ГЕРБ"), 11, Muted, TextAnchor.UpperLeft, bold: true);
            el.rectTransform.At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(x0, -y), new Vector2(400, 16));
            el.Track(1.4f);
            y += 22f;
            for (int i = 0; i < EmblemKeys.Length; i++)
            {
                string key = EmblemKeys[i];
                var b = LGBuild.Button(c, "Emblem", new Color(0.03f, 0.06f, 0.08f, 0.95f), new Color(1f, 1f, 1f, 0.12f), () => PickEmblem(key));
                ((RectTransform)b.transform).At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(x0 + i * 92f, -y), new Vector2(80, 80));
                var em = FactionEmblems.Create(b.transform, FactionEmblems.ForKey(key), 56f, Color.white);
                if (em != null) { em.raycastTarget = false; em.rectTransform.At(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(56, 56)); }
                _emblems.Add((b.GetComponent<Image>(), b.GetComponent<LiquidGlassEffect>(), em, key));
            }
        }

        private InputField Field(RectTransform c, float x0, ref float y, string label, string placeholder, int max, Action dice, Action<string> onChange)
        {
            var l = LGBuild.Label(c, label, 11, Muted, TextAnchor.UpperLeft, bold: true);
            l.rectTransform.At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(x0, -y), new Vector2(400, 16));
            l.Track(1.4f);
            var host = LGBuild.Rect(c, "Field");
            host.anchorMin = new Vector2(0, 1); host.anchorMax = new Vector2(1, 1); host.pivot = new Vector2(0, 1);
            host.offsetMin = new Vector2(x0, -y - 20f - 44f); host.offsetMax = new Vector2(-24f - 56f, -y - 20f);
            var input = LGControls.Input(host, "", placeholder, max);
            input.onValueChanged.AddListener(v => onChange(v));
            var d = LGBuild.Button(c, "Dice", new Color(0.05f, 0.10f, 0.13f), new Color(0.74f, 0.56f, 1f, 0.5f), () => { dice(); SFXManager.Play(Sfx.UiTab, 0.5f); }, LGIcon.Dice, null, 14);
            ((RectTransform)d.transform).At(new Vector2(1, 1), new Vector2(1, 1), new Vector2(-24f, -y - 20f), new Vector2(44, 44));
            TooltipHelper.Attach(d.gameObject, Loc.T("Случайный вариант"));
            y += 20f + 44f + 18f;
            return input;
        }

        private void BuildButtons(RectTransform c)
        {
            var back = LGBuild.Button(c, "Back", new Color(0.05f, 0.10f, 0.13f), new Color(1f, 1f, 1f, 0.25f), Back, LGIcon.Back, Loc.T("НАЗАД"), 13);
            ((RectTransform)back.transform).At(new Vector2(0, 0), new Vector2(0, 0), new Vector2(24, 24), new Vector2(180, 48));
            TooltipHelper.Attach(back.gameObject, Loc.T("К выбору фракции  <color=#8AA2A8>(Esc)</color>"));
            var rnd = LGBuild.Button(c, "RandomAll", new Color(0.05f, 0.10f, 0.13f), new Color(0.74f, 0.56f, 1f, 0.5f), RandomAll, LGIcon.Dice, Loc.T("СЛУЧАЙНО ВСЁ"), 13);
            ((RectTransform)rnd.transform).At(new Vector2(1, 0), new Vector2(1, 0), new Vector2(-24 - 300 - 14, 24), new Vector2(220, 48));
            var go = LGBuild.Button(c, "Start", new Color(0.05f, 0.20f, 0.15f), new Color(0.36f, 0.96f, 0.68f, 0.85f), Confirm, LGIcon.Play, Loc.T("НАЧАТЬ ПРАВЛЕНИЕ"), 14);
            ((RectTransform)go.transform).At(new Vector2(1, 0), new Vector2(1, 0), new Vector2(-24, 24), new Vector2(300, 48));
            TooltipHelper.Attach(go.gameObject, Loc.T("Принять оформление и начать  <color=#8AA2A8>(Enter)</color>"));
        }

        // ================================================================ Действия

        private static string Clean(string v, string fallback)
        {
            v = v != null ? v.Trim() : "";
            return v.Length > 0 ? v : fallback;
        }

        private void PickColor(int idx)
        {
            _colorIdx = Mathf.Clamp(idx, 0, Palette.Length - 1);
            _f.MapColor = Palette[_colorIdx];
            SFXManager.Play(Sfx.UiClick, 0.5f);
            RefreshAll();
        }

        private void PickEmblem(string key)
        {
            _f.EmblemKey = key == FactionRegistry.KeyOf(_base) ? null : key;
            SFXManager.Play(Sfx.UiClick, 0.5f);
            RefreshAll();
        }

        private static bool FemaleRuler(string key) => key == "astrea" || key == "iridia" || key == "terraan";

        private void RandomName()
        {
            var forms = Forms;
            string root = new StarNameGenerator().Next();
            _name.text = forms[UnityEngine.Random.Range(0, forms.Length)] + " " + root;
        }

        private void RandomTitle()
        {
            var t = Titles;
            _title.text = t[UnityEngine.Random.Range(0, t.Length)];
        }

        private void RandomRuler()
            => _ruler.text = FactionRegistry.IsMachine(_base) ? MachineRulerName() : LeaderManager.RandomRulerName(FemaleRuler(FactionRegistry.KeyOf(_base)));

        /// <summary>У машин нет имён — только обозначения узлов: «Узел Сигма-7».</summary>
        private static string MachineRulerName()
        {
            string[] heads = { Loc.T("Голос"), Loc.T("Узел"), Loc.T("Индекс"), Loc.T("Ядро") };
            string[] marks = { "Альфа", "Сигма", "Тета", "Омега", "Дельта", "Каппа" };
            string mark = Loc.English ? new[] { "Alpha", "Sigma", "Theta", "Omega", "Delta", "Kappa" }[UnityEngine.Random.Range(0, 6)]
                                      : marks[UnityEngine.Random.Range(0, marks.Length)];
            return $"{heads[UnityEngine.Random.Range(0, heads.Length)]} {mark}-{UnityEngine.Random.Range(0, 100)}";
        }

        private void RandomAll()
        {
            RandomName();
            RandomTitle();
            RandomRuler();
            PickColor(UnityEngine.Random.Range(0, Palette.Length));
            PickEmblem(EmblemKeys[UnityEngine.Random.Range(0, EmblemKeys.Length)]);
        }

        private void Back()
        {
            SFXManager.Play(Sfx.UiClick, 0.5f);
            gameObject.SetActive(false);
            _back?.Invoke();
        }

        private void Confirm()
        {
            SFXManager.Play(Sfx.UiConfirm);
            var f = _f;
            f.EmpireColor = f.MapColor;   // интерфейс империи (обзор, карточка правителя) — в её цвете
            if (f.RulerName == LeaderPortraits.ForFaction(_base)?.Name) f.RulerName = null;
            gameObject.SetActive(false);
            _done?.Invoke(f);
        }

        // ================================================================ Превью

        private void RefreshAll()
        {
            for (int i = 0; i < _swatches.Count; i++)
                _swatches[i].ring.color = _swatches[i].idx == _colorIdx ? Color.white : new Color(1f, 1f, 1f, 0f);
            Color col = Palette[_colorIdx];
            string emKey = _f.EmblemKey ?? FactionRegistry.KeyOf(_base);
            foreach (var e in _emblems)
            {
                bool on = e.key == emKey;
                if (e.fx != null) e.fx.SetRim(on ? new Color(col.r, col.g, col.b, 0.9f) : new Color(1f, 1f, 1f, 0.12f));
                if (e.emblem != null) e.emblem.color = on ? Color.Lerp(col, Color.white, 0.25f) : new Color(1f, 1f, 1f, 0.45f);
            }
            RefreshPreview();
        }

        private void RefreshPreview()
        {
            Color col = Palette[_colorIdx];
            if (_prevEmblem != null)
            {
                _prevEmblem.sprite = FactionEmblems.ForKey(_f.EmblemKey ?? FactionRegistry.KeyOf(_base));
                _prevEmblem.enabled = _prevEmblem.sprite != null;
                _prevEmblem.color = Color.Lerp(col, Color.white, 0.2f);
            }
            _prevGlow.color = new Color(col.r, col.g, col.b, 0.30f);
            _prevStrip.color = col;
            _prevName.text = _f.Name;
            _prevName.color = Color.Lerp(col, Color.white, 0.55f);
            _prevTitle.text = _f.Title;
            _prevRuler.text = string.IsNullOrEmpty(_f.RulerName) ? "—" : _f.RulerName;
        }

        // ================================================================ Спрайты

        private static Sprite s_disc, s_ring;

        private static Sprite Disc() => s_disc != null ? s_disc : (s_disc = Circle(64, 0f, 1f));
        private static Sprite Ring() => s_ring != null ? s_ring : (s_ring = Circle(64, 0.84f, 0.96f));

        /// <summary>Круг (inner = 0) или кольцо с мягкими краями; радиусы — в долях половины стороны.</summary>
        private static Sprite Circle(int size, float inner, float outer)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color32[size * size];
            float c = (size - 1) * 0.5f, aa = 1.5f / c;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c)) / c;
                    float a = Mathf.Clamp01((outer - d) / aa);
                    if (inner > 0f) a *= Mathf.Clamp01((d - inner) / aa);
                    px[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
                }
            tex.SetPixels32(px);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        }
    }
}
