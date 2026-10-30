using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using StellarisClone.Core;

namespace StellarisClone.Rendering
{
    /// <summary>
    /// Новость для главного меню. Файлы Resources/Menu/News/&lt;id&gt;_ru.txt и &lt;id&gt;_en.txt:
    /// строки «@поле значение» — шапка, дальше текст: «# » — раздел, «- » — пункт, остальное — абзац.
    /// Поля: @order (больше — новее), @tag, @chip (метка на полосе в меню), @title, @headline, @cover (картинка в Resources),
    /// @art cx,cy,w — кадр большой картинки справа, @thumb cx,cy,w — кадр миниатюр,
    /// @tile путь | cx,cy,w | подпись — плитки с картинками под заголовком (cx, cy — центр кадра от левого верхнего угла, w — доля ширины).
    /// </summary>
    public sealed class MenuNews
    {
        public int Order;
        public string Id, Tag = "", Chip, Title = "", Headline = "", Cover;
        public Vector3 Art = new Vector3(0.75f, 0.5f, 0.45f), Thumb = new Vector3(0.75f, 0.45f, 0.42f);
        public readonly List<(string path, Vector3 crop, string label)> Tiles = new List<(string, Vector3, string)>();
        public readonly List<string> Body = new List<string>();

        public bool IsUpdate => Tag == "ОБНОВЛЕНИЕ" || Tag == "UPDATE";
        public Texture2D CoverTex => string.IsNullOrEmpty(Cover) ? null : Resources.Load<Texture2D>(Cover);

        /// <summary>Все новости на языке игры (если перевода нет — русская), новые первыми.</summary>
        public static List<MenuNews> All()
        {
            var byId = new Dictionary<string, MenuNews>();
            foreach (var a in Resources.LoadAll<TextAsset>("Menu/News"))
            {
                int us = a.name.LastIndexOf('_');
                if (us <= 0) continue;
                string id = a.name.Substring(0, us), lang = a.name.Substring(us + 1);
                if (lang != Loc.Current && (lang != "ru" || byId.ContainsKey(id))) continue;
                byId[id] = Parse(id, a.text);
            }
            var list = new List<MenuNews>(byId.Values);
            list.Sort((x, y) => y.Order.CompareTo(x.Order));
            return list;
        }

        private static MenuNews Parse(string id, string text)
        {
            var n = new MenuNews { Id = id };
            foreach (var raw in text.Replace("\r", "").Split('\n'))
            {
                string line = raw.TrimEnd();
                if (line.Length == 0) continue;
                if (!line.StartsWith("@")) { n.Body.Add(line); continue; }
                int sp = line.IndexOf(' ');
                if (sp < 0) continue;
                string key = line.Substring(1, sp - 1), val = line.Substring(sp + 1).Trim();
                switch (key)
                {
                    case "order": int.TryParse(val, out n.Order); break;
                    case "tag": n.Tag = val; break;
                    case "chip": n.Chip = val; break;
                    case "title": n.Title = val; break;
                    case "headline": n.Headline = val; break;
                    case "cover": n.Cover = val; break;
                    case "art": n.Art = Vec(val, n.Art); break;
                    case "thumb": n.Thumb = Vec(val, n.Thumb); break;
                    case "tile":
                        var p = val.Split('|');
                        if (p.Length >= 3) n.Tiles.Add((p[0].Trim(), Vec(p[1], new Vector3(0.5f, 0.5f, 1f)), p[2].Trim()));
                        break;
                }
            }
            if (string.IsNullOrEmpty(n.Chip)) n.Chip = n.Tag;
            return n;
        }

        private static Vector3 Vec(string s, Vector3 def)
        {
            var p = s.Split(',');
            if (p.Length < 3) return def;
            float.TryParse(p[0].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out def.x);
            float.TryParse(p[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out def.y);
            float.TryParse(p[2].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out def.z);
            return def;
        }

        /// <summary>
        /// Кадр картинки под окно с пропорциями frameAspect (ширина / высота) без растяжения:
        /// crop.x, crop.y — центр кадра (от левого верхнего угла), crop.z — доля ширины картинки.
        /// </summary>
        public static Rect Crop(Texture tex, Vector3 crop, float frameAspect)
        {
            if (tex == null || tex.height == 0) return new Rect(0, 0, 1, 1);
            float texAspect = tex.width / (float)tex.height;
            float w = Mathf.Clamp(crop.z, 0.02f, 1f);
            float h = w * texAspect / Mathf.Max(0.05f, frameAspect);
            if (h > 1f) { w /= h; h = 1f; }
            float x = Mathf.Clamp(crop.x - w * 0.5f, 0f, 1f - w);
            float yTop = Mathf.Clamp(crop.y - h * 0.5f, 0f, 1f - h);
            return new Rect(x, 1f - yTop - h, w, h);
        }
    }

    /// <summary>
    /// Окно новостей главного меню: сверху лента новостей (карточки с обложками, листаются Q/E или стрелками),
    /// ниже — выбранная новость: слева заголовок, вступление, плитки с картинками и разделы, справа — большая обложка,
    /// медленно наезжающая. Смена новости — плавная: текст выезжает, обложки перетекают одна в другую.
    /// </summary>
    public sealed class NewsWindow : MonoBehaviour
    {
        private const float PanelTop = 186f, PanelBottom = 86f, Side = 100f, LeftShare = 0.58f;

        private List<MenuNews> _items;
        private int _sel = -1;
        private CanvasGroup _cg;
        private RectTransform _panel, _tabsRow, _textHost, _content;
        private CanvasGroup _textCg;
        private RawImage _artA, _artB;
        private Rect _uvA, _uvB;
        private Vector2 _panelPos, _textPos;   // места покоя растянутых панелей (их anchoredPosition не ноль)
        private float _k, _kv, _textT = 1f, _artMix = 1f, _t, _openT;
        private bool _shown;
        private System.Action _onClose;
        private readonly List<NewsTab> _tabs = new List<NewsTab>();

        public bool IsShown => _shown;

        public static NewsWindow Create(RectTransform root, System.Action onClose)
        {
            var rt = LGBuild.Rect(root, "News");
            rt.Stretch();
            var w = rt.gameObject.AddComponent<NewsWindow>();
            w._onClose = onClose;
            w._items = MenuNews.All();
            w.Build(rt);
            rt.gameObject.SetActive(false);
            return w;
        }

        private void Build(RectTransform rt)
        {
            _cg = rt.gameObject.AddComponent<CanvasGroup>();
            var dim = LGBuild.Panel(rt, "Dim", new Color(0.005f, 0.012f, 0.022f, 0.72f), raycast: true);
            dim.rectTransform.Stretch();
            LG.Ignore(dim.gameObject);

            // Лента новостей
            _tabsRow = LGBuild.Rect(rt, "Tabs");
            _tabsRow.At(new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -32), new Vector2(1640, 124));
            int n = Mathf.Min(_items.Count, 4);
            const float tw = 396f, gap = 18f;
            float x0 = -(n * tw + (n - 1) * gap) * 0.5f + tw * 0.5f;
            for (int i = 0; i < n; i++)
            {
                int idx = i;
                var tab = NewsTab.Create(_tabsRow, _items[i], new Vector2(tw, 116), new Vector2(x0 + i * (tw + gap), 0), () => Select(idx));
                tab.Index = i;
                _tabs.Add(tab);
            }
            if (_items.Count > 1)
            {
                float edge = (n * tw + (n - 1) * gap) * 0.5f + 42f;
                var prev = LGControls.IconButton(_tabsRow, LGIcon.Back, 44, UIManager.DS.BgDeep, new Color(1f, 1f, 1f, 0.3f), () => Step(-1));
                ((RectTransform)prev.transform).At(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-edge, 14), new Vector2(44, 44));
                KeyCap(_tabsRow, "Q", new Vector2(-edge, -32));
                TooltipHelper.Attach(prev.gameObject, Loc.T("Предыдущая  <color=#8AA2A8>(Q)</color>"));
                var next = LGControls.IconButton(_tabsRow, LGIcon.Back, 44, UIManager.DS.BgDeep, new Color(1f, 1f, 1f, 0.3f), () => Step(1), 180f);
                ((RectTransform)next.transform).At(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(edge, 14), new Vector2(44, 44));
                KeyCap(_tabsRow, "E", new Vector2(edge, -32));
                TooltipHelper.Attach(next.gameObject, Loc.T("Следующая  <color=#8AA2A8>(E)</color>"));
            }

            // Основная панель
            _panel = LGBuild.Rect(rt, "Panel");
            _panel.Stretch(Side, PanelBottom, Side, PanelTop);
            _panelPos = _panel.anchoredPosition;
            var bg = LGBuild.Panel(_panel, "Bg", new Color(0.02f, 0.04f, 0.07f, 0.96f), raycast: true);
            bg.rectTransform.Stretch();
            LG.Glass(bg.gameObject, 24f).SetRim(new Color(1f, 0.82f, 0.48f, 0.35f));

            // Справа — обложка (две для перетекания)
            var artHost = LGBuild.Rect(_panel, "ArtHost");
            artHost.anchorMin = new Vector2(LeftShare, 0);
            artHost.anchorMax = Vector2.one;
            artHost.offsetMin = new Vector2(0, 14);
            artHost.offsetMax = new Vector2(-14, -14);
            var mask = LG.RoundedMask(artHost, 16f);
            _artA = Art(mask, "ArtA");
            _artB = Art(mask, "ArtB");
            var edgeFade = LGBuild.Panel(mask, "Fade", new Color(0.02f, 0.04f, 0.07f, 1f));
            edgeFade.sprite = MenuArt.HorizontalFade;
            edgeFade.rectTransform.anchorMin = Vector2.zero;
            edgeFade.rectTransform.anchorMax = new Vector2(0.28f, 1);
            edgeFade.rectTransform.offsetMin = edgeFade.rectTransform.offsetMax = Vector2.zero;
            LG.Ignore(edgeFade.gameObject);
            var bottomFade = LGBuild.Panel(mask, "FadeBottom", new Color(0.02f, 0.04f, 0.07f, 0.8f));
            bottomFade.sprite = MenuArt.VerticalFade;
            bottomFade.rectTransform.anchorMin = Vector2.zero;
            bottomFade.rectTransform.anchorMax = new Vector2(1, 0.3f);
            bottomFade.rectTransform.offsetMin = bottomFade.rectTransform.offsetMax = Vector2.zero;
            LG.Ignore(bottomFade.gameObject);

            // Слева — текст
            _textHost = LGBuild.Rect(_panel, "Text");
            _textHost.anchorMin = Vector2.zero;
            _textHost.anchorMax = new Vector2(LeftShare, 1);
            _textHost.offsetMin = new Vector2(46, 22);
            _textHost.offsetMax = new Vector2(-30, -30);
            _textCg = _textHost.gameObject.AddComponent<CanvasGroup>();
            _textPos = _textHost.anchoredPosition;

            // Подсказка снизу
            var hint = MenuLink.Create(rt, Loc.T("ESC  ·  ЗАКРЫТЬ"), new Color(0.80f, 0.88f, 0.92f), () => _onClose?.Invoke());
            ((RectTransform)hint.transform).At(new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 30), new Vector2(200, 30));
            var ht = hint.GetComponentInChildren<Text>();
            if (ht != null) ht.alignment = TextAnchor.MiddleCenter;
            if (_items.Count > 1)
            {
                var keys = LGBuild.Label(rt, Loc.T("Q / E — листать новости"), 12, UIManager.DS.TextMuted, TextAnchor.LowerRight);
                keys.rectTransform.At(new Vector2(1, 0), new Vector2(1, 0), new Vector2(-Side, 36), new Vector2(400, 20));
            }
        }

        /// <summary>Клавиша-подсказка: светлая «клавиша» с буквой.</summary>
        private static void KeyCap(RectTransform parent, string key, Vector2 pos)
        {
            var cap = LGBuild.Panel(parent, "Key_" + key, new Color(0.80f, 0.86f, 0.90f, 0.92f));
            cap.rectTransform.At(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), pos, new Vector2(26, 26));
            LG.Chip(cap.gameObject, new Color(1f, 1f, 1f, 0.5f), 5f);
            var t = LGBuild.Label(cap.transform, key, 14, new Color(0.04f, 0.07f, 0.10f), TextAnchor.MiddleCenter, bold: true);
            t.rectTransform.Stretch();
        }

        private static RawImage Art(RectTransform parent, string name)
        {
            var img = LGBuild.Rect(parent, name).gameObject.AddComponent<RawImage>();
            img.rectTransform.Stretch();
            img.raycastTarget = false;
            img.color = new Color(1, 1, 1, 0);
            return img;
        }

        public void Show(int index = 0)
        {
            if (_items.Count == 0) return;
            gameObject.SetActive(true);
            transform.SetAsLastSibling();
            _shown = true;
            _openT = 0f;
            foreach (var t in _tabs) t.Restart();
            _sel = -1;
            Select(Mathf.Clamp(index, 0, _items.Count - 1), instant: true);
        }

        public void Hide() => _shown = false;

        private void Step(int d)
        {
            if (_items.Count < 2) return;
            Select(((_sel + d) % _items.Count + _items.Count) % _items.Count);
        }

        private void Select(int i, bool instant = false)
        {
            if (i == _sel || i < 0 || i >= _items.Count) return;
            if (!instant) SFXManager.Play("ui_click", 0.6f, 1.1f);
            _sel = i;
            foreach (var t in _tabs) t.Selected = t.Index == i;
            var item = _items[i];

            // Обложка: новая встаёт поверх и проявляется
            var tex = item.CoverTex;
            var tmp = _artA; _artA = _artB; _artB = tmp;
            var tuv = _uvA; _uvA = _uvB; _uvB = tuv;
            _artA.transform.SetAsLastSibling();
            _artA.texture = tex;
            // Пропорции кадра: по готовой разметке, а в первый кадр (разметки ещё нет) — по окну 1920×1080
            var r = _artA.rectTransform.rect;
            float aspect = r.height > 1f ? r.width / r.height
                         : ((1920f - 2f * Side) * (1f - LeftShare) - 14f) / (1080f - PanelTop - PanelBottom - 28f);
            _uvA = MenuNews.Crop(tex, item.Art, aspect);
            _artMix = instant ? 1f : 0f;
            // Затемнения обложки — поверх обеих картинок
            var mask = _artA.transform.parent;
            mask.Find("Fade")?.SetAsLastSibling();
            mask.Find("FadeBottom")?.SetAsLastSibling();

            BuildText(item);
            _textT = 0f;
        }

        private void BuildText(MenuNews item)
        {
            LGBuild.Clear(_textHost);
            _content = LGBuild.ScrollList(_textHost, 10f, 2);
            var gold = new Color(1f, 0.82f, 0.48f);

            LGBuild.Label(_content, item.Tag, 13, item.IsUpdate ? gold : UIManager.DS.NeonCyan, TextAnchor.UpperLeft, bold: true);
            var title = LGBuild.Label(_content, item.Title.ToUpperInvariant(), 44, Color.white, TextAnchor.UpperLeft, bold: true, wrap: true);
            title.lineSpacing = 0.95f;
            if (!string.IsNullOrEmpty(item.Headline))
                LGBuild.Label(_content, item.Headline, 20, Color.Lerp(gold, Color.white, 0.25f), TextAnchor.UpperLeft, bold: true, wrap: true);

            int body = 0;
            // Вступление — абзацы до первого раздела
            for (; body < item.Body.Count && !item.Body[body].StartsWith("# "); body++)
                Para(item.Body[body], 17, new Color(0.84f, 0.90f, 0.93f));

            if (item.Tiles.Count > 0) BuildTiles(item);

            for (; body < item.Body.Count; body++)
            {
                string line = item.Body[body];
                if (line.StartsWith("# "))
                {
                    var gap = LGBuild.Rect(_content, "Gap");
                    LGBuild.Height(gap.gameObject, 4f);
                    LGBuild.Label(_content, line.Substring(2), 18, gold, TextAnchor.UpperLeft, bold: true, wrap: true);
                }
                else if (line.StartsWith("- ")) Para("<color=#6FE6DA>•</color>   " + line.Substring(2), 15, new Color(0.80f, 0.87f, 0.90f));
                else Para(line, 15, new Color(0.74f, 0.82f, 0.86f));
            }
        }

        private void Para(string text, int size, Color c)
        {
            var t = LGBuild.Label(_content, text, size, c, TextAnchor.UpperLeft, wrap: true);
            t.supportRichText = true;
            t.lineSpacing = 1.12f;
        }

        /// <summary>Плитки с картинками в ряд: номер в углу и подпись под картинкой; при наведении — подсветка.</summary>
        private void BuildTiles(MenuNews item)
        {
            var row = LGBuild.Rect(_content, "Tiles");
            LGBuild.Height(row.gameObject, 196f);
            var h = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 14f;
            h.childControlWidth = h.childControlHeight = true;
            h.childForceExpandWidth = h.childForceExpandHeight = true;
            h.padding = new RectOffset(0, 0, 10, 4);

            // Ширина плитки — по ширине колонки текста (окно 1920 по ссылке): для кадра без растяжения
            float colW = (1920f - 2f * Side) * LeftShare - 76f;
            int n = item.Tiles.Count;
            float tileW = (colW - 14f * (n - 1)) / n;
            for (int i = 0; i < n; i++)
            {
                var (path, crop, label) = item.Tiles[i];
                var tile = LGBuild.Rect(row, "Tile");
                var frame = LGBuild.Panel(tile, "Frame", new Color(0.03f, 0.06f, 0.10f, 1f));
                frame.rectTransform.anchorMin = new Vector2(0, 0);
                frame.rectTransform.anchorMax = new Vector2(1, 1);
                frame.rectTransform.offsetMin = new Vector2(0, 30);
                frame.rectTransform.offsetMax = Vector2.zero;
                var fx = LG.Platter(frame.gameObject, 10f);
                fx.SetRim(new Color(1f, 0.82f, 0.48f, 0.45f));
                var m = LG.RoundedMask(frame.transform, 10f, 2f);
                var tex = Resources.Load<Texture2D>(path);
                if (tex != null)
                {
                    var img = LGBuild.Rect(m, "Img").gameObject.AddComponent<RawImage>();
                    img.rectTransform.Stretch();
                    img.texture = tex;
                    img.raycastTarget = false;
                    img.uvRect = MenuNews.Crop(tex, crop, tileW / 140f);
                }
                var badge = LGBuild.Panel(frame.transform, "No", new Color(0.95f, 0.72f, 0.30f));
                badge.rectTransform.At(new Vector2(1, 0), new Vector2(1, 0), new Vector2(-6, 6), new Vector2(24, 24));
                LG.Ignore(badge.gameObject);
                var no = LGBuild.Label(badge.transform, (i + 1).ToString(), 13, new Color(0.08f, 0.05f, 0.01f), TextAnchor.MiddleCenter, bold: true);
                no.rectTransform.Stretch();
                var cap = LGBuild.Label(tile, label, 13, new Color(0.84f, 0.90f, 0.93f), TextAnchor.LowerLeft, bold: true);
                cap.rectTransform.anchorMin = Vector2.zero;
                cap.rectTransform.anchorMax = new Vector2(1, 0);
                cap.rectTransform.pivot = new Vector2(0.5f, 0);
                cap.rectTransform.offsetMin = new Vector2(2, 2);
                cap.rectTransform.offsetMax = new Vector2(0, 24);
                frame.raycastTarget = true;
                frame.gameObject.AddComponent<NewsTileHover>().Init(fx);
            }
        }

        private void Update()
        {
            float dt = Mathf.Min(Time.unscaledDeltaTime, 1f / 20f);
            _t += dt;
            _openT += dt;
            _k = LGEase.Spring(_k, _shown ? 1f : 0f, ref _kv, 4.5f, 0.9f, dt);
            float k = Mathf.Clamp01(_k);
            _cg.alpha = k;
            _cg.blocksRaycasts = _shown;
            float s = Mathf.Lerp(0.97f, 1f, k);
            _panel.localScale = new Vector3(s, s, 1f);
            _panel.anchoredPosition = _panelPos + new Vector2(0f, -24f * (1f - k));
            if (!_shown && k < 0.01f) { gameObject.SetActive(false); return; }

            if (_shown)
            {
                if (Input.GetKeyDown(KeyCode.Q) || Input.GetKeyDown(KeyCode.LeftArrow)) Step(-1);
                if (Input.GetKeyDown(KeyCode.E) || Input.GetKeyDown(KeyCode.RightArrow)) Step(1);
            }

            // Текст новости выезжает слева
            _textT = Mathf.Min(1f, _textT + dt / 0.38f);
            float te = LGEase.OutCubic(_textT);
            _textCg.alpha = te;
            _textHost.anchoredPosition = _textPos + new Vector2(-26f * (1f - te), 0f);

            // Обложки: новая проявляется, обе медленно наезжают и дрейфуют
            _artMix = Mathf.Min(1f, _artMix + dt / 0.55f);
            float drift = 0.5f + 0.5f * Mathf.Sin(_t * 0.18f);
            _artA.color = new Color(1, 1, 1, LGEase.OutCubic(_artMix));
            _artB.color = new Color(1, 1, 1, 1f - LGEase.OutCubic(_artMix));
            _artA.uvRect = Zoom(_uvA, 0.96f - 0.03f * drift + 0.04f * (1f - _artMix), _t);
            _artB.uvRect = Zoom(_uvB, 0.96f - 0.03f * drift, _t);
        }

        private static Rect Zoom(Rect uv, float z, float t)
        {
            var c = uv.center + new Vector2(Mathf.Sin(t * 0.06f), Mathf.Cos(t * 0.045f)) * 0.008f;
            var size = uv.size * z;
            return new Rect(c - size * 0.5f, size);
        }
    }

    /// <summary>Карточка ленты новостей: обложка справа, метка, заголовок, подзаголовок. Выбранная — яркая и крупнее.</summary>
    public sealed class NewsTab : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
    {
        public int Index;
        public bool Selected;
        private RectTransform _rt;
        private CanvasGroup _cg;
        private LiquidGlassEffect _fx;
        private Color _accent;
        private System.Action _onClick;
        private Vector2 _rest;
        private bool _over;
        private float _h, _hv, _s, _sv, _t;

        public static NewsTab Create(RectTransform parent, MenuNews n, Vector2 size, Vector2 pos, System.Action onClick)
        {
            var root = LGBuild.Rect(parent, "Tab_" + n.Id);
            root.At(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), pos, size);
            var t = root.gameObject.AddComponent<NewsTab>();
            t._rt = root;
            t._rest = pos;
            t._onClick = onClick;
            t._cg = root.gameObject.AddComponent<CanvasGroup>();
            t._accent = n.IsUpdate ? new Color(1f, 0.82f, 0.48f) : UIManager.DS.NeonCyan;

            var body = LGBuild.Panel(root, "Body", new Color(0.03f, 0.05f, 0.09f, 0.96f), raycast: true);
            body.rectTransform.Stretch();
            t._fx = LG.Glass(body.gameObject, 16f);
            var m = LG.RoundedMask(body.transform, 15f, 2f);
            // Обложка — правая половина; слева под текстом сплошная подложка, переходящая в картинку
            var tex = n.CoverTex;
            if (tex != null)
            {
                var host = LGBuild.Rect(m, "Thumb");
                host.anchorMin = new Vector2(0.45f, 0);
                host.anchorMax = Vector2.one;
                host.offsetMin = host.offsetMax = Vector2.zero;
                var img = host.gameObject.AddComponent<RawImage>();
                img.texture = tex;
                img.raycastTarget = false;
                img.uvRect = MenuNews.Crop(tex, n.Thumb, size.x * 0.55f / size.y);
            }
            var ink = new Color(0.03f, 0.05f, 0.09f, 1f);
            var solid = LGBuild.Panel(m, "Solid", ink);
            solid.rectTransform.anchorMin = Vector2.zero;
            solid.rectTransform.anchorMax = new Vector2(0.45f, 1);
            solid.rectTransform.offsetMin = solid.rectTransform.offsetMax = Vector2.zero;
            LG.Ignore(solid.gameObject);
            var fade = LGBuild.Panel(m, "Fade", ink);
            fade.sprite = MenuArt.HorizontalFade;
            fade.rectTransform.anchorMin = new Vector2(0.45f, 0);
            fade.rectTransform.anchorMax = new Vector2(0.85f, 1);
            fade.rectTransform.offsetMin = fade.rectTransform.offsetMax = Vector2.zero;
            LG.Ignore(fade.gameObject);

            var chip = LGBuild.Panel(body.transform, "Chip", new Color(t._accent.r * 0.22f, t._accent.g * 0.22f, t._accent.b * 0.22f, 0.95f));
            chip.rectTransform.At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(18, -14), new Vector2(118, 22));
            LG.Chip(chip.gameObject, new Color(t._accent.r, t._accent.g, t._accent.b, 0.6f));
            var ct = LGBuild.Label(chip.transform, n.Tag, 11, t._accent, TextAnchor.MiddleCenter, bold: true);
            ct.rectTransform.Stretch();
            var title = LGBuild.Label(body.transform, n.Title, 20, Color.white, TextAnchor.UpperLeft, bold: true);
            title.rectTransform.Stretch(18, 38, size.x * 0.4f, 44);
            title.resizeTextForBestFit = true; title.resizeTextMinSize = 14; title.resizeTextMaxSize = 20;
            var head = LGBuild.Label(body.transform, n.Headline, 12, new Color(0.78f, 0.86f, 0.90f), TextAnchor.UpperLeft, wrap: true);
            head.rectTransform.Stretch(18, 8, size.x * 0.4f, 76);
            head.verticalOverflow = VerticalWrapMode.Truncate;
            head.lineSpacing = 0.95f;
            var sh = title.gameObject.AddComponent<Shadow>();
            sh.effectColor = new Color(0f, 0f, 0f, 0.8f);
            sh.effectDistance = new Vector2(1f, -1f);
            return t;
        }

        public void Restart() { _t = 0f; }

        public void OnPointerEnter(PointerEventData e)
        {
            _over = true;
            if (SFXManager.Has("ui_hover")) SFXManager.Play("ui_hover", 0.3f, 1.15f);
        }

        public void OnPointerExit(PointerEventData e) => _over = false;

        public void OnPointerClick(PointerEventData e)
        {
            if (e.button == PointerEventData.InputButton.Left) _onClick?.Invoke();
        }

        private void Update()
        {
            float dt = Mathf.Min(Time.unscaledDeltaTime, 1f / 20f);
            _t += dt;
            float k = LGEase.OutCubic(Mathf.Clamp01((_t - 0.08f * Index) / 0.5f));   // лента выезжает сверху по очереди
            _h = LGEase.Spring(_h, _over ? 1f : 0f, ref _hv, 4f, 0.7f, dt);
            _s = LGEase.Spring(_s, Selected ? 1f : 0f, ref _sv, 4f, 0.8f, dt);
            float h = Mathf.Clamp01(_h), s = Mathf.Clamp01(_s);
            _cg.alpha = k * Mathf.Lerp(0.62f, 1f, Mathf.Max(s, h * 0.8f));
            float sc = Mathf.Lerp(0.95f, 1f, s) + 0.015f * h;
            _rt.localScale = new Vector3(sc, sc, 1f);
            _rt.anchoredPosition = _rest + new Vector2(0f, 30f * (1f - k) + 4f * h);
            float pulse = s * (0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 2.2f));
            _fx.SetRim(new Color(_accent.r, _accent.g, _accent.b, Mathf.Lerp(0.18f, 0.85f, Mathf.Max(s, h * 0.6f)) - 0.1f * pulse));
            _fx.SetHover(Mathf.Max(h * 0.6f, s * 0.35f));
            _fx.SetPulse(0.2f * pulse);
        }
    }

    /// <summary>Подсветка плитки новости при наведении.</summary>
    public sealed class NewsTileHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        private LiquidGlassEffect _fx;
        private bool _over;
        private float _h, _hv;

        public void Init(LiquidGlassEffect fx) => _fx = fx;
        public void OnPointerEnter(PointerEventData e) => _over = true;
        public void OnPointerExit(PointerEventData e) => _over = false;

        private void Update()
        {
            float dt = Mathf.Min(Time.unscaledDeltaTime, 1f / 20f);
            _h = LGEase.Spring(_h, _over ? 1f : 0f, ref _hv, 4f, 0.7f, dt);
            float h = Mathf.Clamp01(_h);
            if (_fx != null)
            {
                _fx.SetHover(h * 0.7f);
                _fx.SetRim(new Color(1f, 0.82f, 0.48f, Mathf.Lerp(0.45f, 0.95f, h)));
            }
            float s = 1f + 0.03f * h;
            transform.localScale = new Vector3(s, s, 1f);
        }
    }
}
