using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using StellarisClone.Core;
using Sfx = StellarisClone.Core.Audio.Sfx;

namespace StellarisClone.Rendering
{
    /// <summary>
    /// Окно «Кодекс» (клавиша X, пункт в левом меню): слева разделы, в середине записи раздела
    /// (неоткрытые — «???»), справа — текст выбранной записи. Новые записи помечены, пока их не прочтут.
    /// </summary>
    public class CodexWindow : MonoBehaviour
    {
        public static CodexWindow Instance { get; private set; }
        public static bool IsOpenNow => Instance != null && Instance.IsOpen;
        public bool IsOpen => _root != null && LG.IsVisible(_root);

        private const float W = 1240f, H = 860f;
        private const float CatW = 236f, ListW = 380f;
        private GameObject _root;
        private Text _progress, _dTitle, _dSub, _dBody;
        private Image _dIcon, _dGlow;
        private RectTransform _list, _catHost, _detail;
        private Codex.Cat _cat = Codex.Cat.Factions;
        private string _selected;
        private readonly List<(Codex.Cat cat, Text count, Image badge, Image bg)> _catRows = new List<(Codex.Cat, Text, Image, Image)>();

        private static Color Muted => UIManager.DS.TextMuted;
        private static Color Primary => UIManager.DS.TextPrimary;
        private static Color Cyan => UIManager.DS.NeonCyan;

        public static void Toggle()
        {
            if (Instance == null) new GameObject("[UI] CodexWindow").AddComponent<CodexWindow>();
            if (Instance.IsOpen) Instance.Close(); else Instance.Open();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            Codex.OnChanged += OnCodexChanged;
        }

        private void OnDestroy()
        {
            Codex.OnChanged -= OnCodexChanged;
            if (Instance == this) Instance = null;
        }

        private void Update()
        {
            if (IsOpen && Input.GetKeyDown(KeyCode.Escape)) { UIEsc.Consume(); Close(); }
        }

        public void Open()
        {
            if (_root == null) Build();
            if (_root == null) return;
            Codex.ScanGame();
            LG.Show(_root);
            _root.transform.SetAsLastSibling();
            UIManager.Instance?.ShowModalDimPublic();
            SFXManager.Play(Sfx.UiTab, 0.6f);
            ShowCat(_cat);
        }

        public void Close()
        {
            if (_root == null) return;
            LG.Hide(_root);
            UIManager.Instance?.HideModalDimPublic();
            TooltipSystem.Hide();
        }

        private void OnCodexChanged()
        {
            if (IsOpen) RefreshCats();
        }

        // ==================== ПОСТРОЕНИЕ ====================

        private void Build()
        {
            var host = GameObject.Find("ModalCanvas");
            if (host == null) return;
            var rt = LGBuild.Rect(host.transform, "Codex");
            rt.At(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(W, H));
            _root = rt.gameObject;
            _root.AddComponent<CanvasGroup>();
            var bg = _root.AddComponent<Image>();
            bg.color = new Color(0.014f, 0.030f, 0.044f, 0.97f);
            bg.raycastTarget = true;
            LG.Glass(_root).SetRim(new Color(0.45f, 0.95f, 0.90f, 0.40f));
            LG.Motion(_root, LGAppear.Kind.Pop).fromScale = 0.97f;

            var title = LGBuild.Label(rt, Loc.T("КОДЕКС"), 26, Color.white, TextAnchor.UpperLeft, bold: true);
            title.rectTransform.At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(32, -20), new Vector2(600, 36));
            title.Track(1.6f);
            _progress = LGBuild.Label(rt, "", 13, Muted, TextAnchor.UpperLeft);
            _progress.rectTransform.At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(33, -58), new Vector2(900, 20));
            _progress.supportRichText = true;
            var close = LGBuild.Button(rt, "Close", UIManager.DS.BtnNeutral, new Color(1f, 1f, 1f, 0.25f), Close, LGIcon.Close, null, 12);
            ((RectTransform)close.transform).At(new Vector2(1, 1), new Vector2(1, 1), new Vector2(-20, -18), new Vector2(38, 38));
            TooltipHelper.Attach(close.gameObject, Loc.T("Закрыть  <color=#8AA2A8>(Esc / X)</color>"));

            float bodyH = H - 96f;
            _catHost = Panel(rt, "Cats", new Vector2(20, 20), new Vector2(CatW, bodyH));
            var listPanel = Panel(rt, "List", new Vector2(20 + CatW + 14, 20), new Vector2(ListW, bodyH));
            _list = LGBuild.ScrollList(listPanel, 4f, 8);
            _detail = Panel(rt, "Detail", new Vector2(20 + CatW + 14 + ListW + 14, 20), new Vector2(W - (20 + CatW + 14 + ListW + 14) - 20, bodyH));

            // Разделы
            float y = 14f;
            _catRows.Clear();
            foreach (var c in Codex.Cats)
            {
                var cat = c;
                var row = RowButton(_catHost, "Cat", () => ShowCat(cat), out var rowBg);
                row.anchorMin = new Vector2(0, 1); row.anchorMax = new Vector2(1, 1); row.pivot = new Vector2(0.5f, 1);
                row.offsetMin = new Vector2(10, -y - 46); row.offsetMax = new Vector2(-10, -y);
                var ic = LGIcons.Create(row, Codex.CatIcon(c), 18, Cyan);
                ic.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(22, 0), new Vector2(18, 18));
                var name = LGBuild.Label(row, Codex.CatName(c).ToUpper(), 12, Color.white, TextAnchor.MiddleLeft, bold: true);
                name.rectTransform.Stretch(42, 0, 54, 0);
                name.Track(1.2f);
                var count = LGBuild.Label(row, "", 11, Muted, TextAnchor.MiddleRight);
                count.rectTransform.Stretch(0, 0, 12, 0);
                var badge = LGBuild.Panel(row, "New", UIManager.DS.Gold);
                badge.rectTransform.At(new Vector2(1, 1), new Vector2(0.5f, 0.5f), new Vector2(-6, -6), new Vector2(10, 10));
                LG.Platter(badge.gameObject, 5f);
                badge.raycastTarget = false;
                _catRows.Add((c, count, badge, rowBg));
                y += 54f;
            }

            // Запись: значок, название, подзаголовок, текст (прокрутка)
            _dGlow = LGBuild.Panel(_detail, "Glow", new Color(1, 1, 1, 0.06f));
            _dGlow.raycastTarget = false;
            _dGlow.rectTransform.At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(20, -20), new Vector2(76, 76));
            LG.Platter(_dGlow.gameObject, 38f);
            _dIcon = LGIcons.Create(_dGlow.rectTransform, LGIcon.Info, 38, Color.white);
            _dIcon.rectTransform.At(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(38, 38));
            _dTitle = LGBuild.Label(_detail, "", 22, Color.white, TextAnchor.UpperLeft, bold: true, wrap: true);
            _dTitle.rectTransform.TopBand(24, 60, 112, 20);
            _dSub = LGBuild.Label(_detail, "", 13, Cyan, TextAnchor.UpperLeft);
            _dSub.rectTransform.TopBand(76, 20, 112, 20);
            var sep = LGBuild.Panel(_detail, "Sep", new Color(0.45f, 0.95f, 0.90f, 0.18f));
            sep.raycastTarget = false;
            sep.rectTransform.TopBand(112, 1, 20, 20);
            var bodyHost = LGBuild.Rect(_detail, "BodyHost");
            bodyHost.Stretch(14, 14, 14, 124);
            var bodyList = LGBuild.ScrollList(bodyHost, 0f, 6);
            _dBody = LGBuild.Label(bodyList, "", 14, Primary, TextAnchor.UpperLeft, wrap: true);
            _dBody.supportRichText = true;
            _dBody.lineSpacing = 1.12f;
            _dBody.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            _root.SetActive(false);
        }

        // ==================== ПОКАЗ ====================

        private void ShowCat(Codex.Cat cat)
        {
            if (_cat != cat) SFXManager.Play(Sfx.UiClick, 0.6f);
            _cat = cat;
            RefreshCats();
            LGBuild.Clear(_list);
            var entries = Codex.Entries(cat);
            string first = null;
            foreach (var e in entries)
            {
                var entry = e;
                bool known = Codex.Known(e.Id);
                var row = RowButton(_list, "Entry", () => Select(entry), out var bg);
                var le = row.gameObject.AddComponent<LayoutElement>();
                le.preferredHeight = 40f;
                var ic = LGIcons.Create(row, known ? e.Icon : LGIcon.Lock, 16, known ? e.Color : new Color(1f, 1f, 1f, 0.25f));
                ic.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(22, 0), new Vector2(16, 16));
                var t = LGBuild.Label(row, known ? e.Title : "? ? ?", 13, known ? Color.white : new Color(1f, 1f, 1f, 0.3f), TextAnchor.MiddleLeft, bold: known);
                t.rectTransform.Stretch(42, 0, 70, 0);
                if (Codex.IsNew(e.Id))
                {
                    var n = LGBuild.Label(row, Loc.T("НОВОЕ"), 10, UIManager.DS.Gold, TextAnchor.MiddleRight, bold: true);
                    n.rectTransform.Stretch(0, 0, 12, 0);
                    n.Track(1.4f);
                }
                if (e.Id == _selected) bg.color = new Color(0.08f, 0.22f, 0.26f, 0.95f);
                if (first == null && known) first = e.Id;
            }
            var pick = entries.Find(x => x.Id == _selected) ?? entries.Find(x => x.Id == first) ?? (entries.Count > 0 ? entries[0] : null);
            if (pick != null) Select(pick, rebuild: false); else ShowEmpty();
        }

        private void Select(Codex.Entry e, bool rebuild = true)
        {
            bool changed = _selected != e.Id;
            _selected = e.Id;
            bool known = Codex.Known(e.Id);
            _dGlow.color = known ? new Color(e.Color.r, e.Color.g, e.Color.b, 0.16f) : new Color(1, 1, 1, 0.05f);
            _dIcon.sprite = LGIcons.Get(known ? e.Icon : LGIcon.Lock);
            _dIcon.color = known ? e.Color : new Color(1f, 1f, 1f, 0.35f);
            _dTitle.text = known ? e.Title : "? ? ?";
            _dSub.text = known ? e.Sub ?? "" : Codex.CatName(e.Cat);
            _dSub.color = known ? e.Color : Muted;
            _dBody.text = known ? e.Body : HowToFind(e.Cat);
            if (known && Codex.IsNew(e.Id)) Codex.MarkSeen(e.Id);
            if (rebuild && changed) { SFXManager.Play(Sfx.UiClick, 0.5f); ShowCat(_cat); }
        }

        private void ShowEmpty()
        {
            _dTitle.text = "";
            _dSub.text = "";
            _dBody.text = "";
        }

        private static string HowToFind(Codex.Cat c) => "<color=#8AA2A8>" + (c switch
        {
            Codex.Cat.Factions => Loc.T("Запись откроется при первой встрече с этой цивилизацией."),
            Codex.Cat.Tech => Loc.T("Запись откроется, когда вы изучите эту технологию."),
            Codex.Cat.Cards => Loc.T("Запись откроется, когда карта появится в вашем арсенале или в бою — вашем или вражеском."),
            Codex.Cat.Landmarks => Loc.T("Запись откроется, когда разведка найдёт этот узел силы на карте."),
            _ => Loc.T("Запись откроется, когда разведка найдёт это чудо природы на карте.")
        }) + "</color>";

        private void RefreshCats()
        {
            int fAll = 0, nAll = 0;
            foreach (var (cat, count, badge, bg) in _catRows)
            {
                var (f, n) = Codex.Progress(cat);
                fAll += f; nAll += n;
                count.text = f + "/" + n;
                bool anyNew = false;
                foreach (var e in Codex.Entries(cat)) if (Codex.IsNew(e.Id)) { anyNew = true; break; }
                badge.gameObject.SetActive(anyNew);
                bg.color = cat == _cat ? new Color(0.06f, 0.20f, 0.24f, 0.95f) : new Color(0.02f, 0.05f, 0.07f, 0.7f);
            }
            int pct = nAll > 0 ? Mathf.RoundToInt(100f * fAll / nAll) : 0;
            _progress.text = Loc.F("Энциклопедия галактики. Открыто <color=#7CF2D8>{0}</color> из {1} записей ({2}%). Открытия сохраняются между партиями.", fAll, nAll, pct);
        }

        // ==================== ПОМОЩНИКИ ====================

        private static RectTransform Panel(RectTransform parent, string name, Vector2 pos, Vector2 size)
        {
            var p = LGBuild.Panel(parent, name, new Color(0.020f, 0.045f, 0.065f, 0.88f));
            p.rectTransform.anchorMin = p.rectTransform.anchorMax = new Vector2(0, 0);
            p.rectTransform.pivot = new Vector2(0, 0);
            p.rectTransform.anchoredPosition = pos;
            p.rectTransform.sizeDelta = size;
            LG.Platter(p.gameObject, 10f);
            return p.rectTransform;
        }

        private static RectTransform RowButton(Transform parent, string name, System.Action onClick, out Image bg)
        {
            bg = LGBuild.Panel(parent, name, new Color(0.02f, 0.05f, 0.07f, 0.7f), raycast: true);
            var btn = bg.gameObject.AddComponent<Button>();
            btn.onClick.AddListener(() => onClick());
            LG.Chip(bg.gameObject, new Color(0.45f, 0.95f, 0.90f, 0.22f), 8f);
            return bg.rectTransform;
        }
    }
}
