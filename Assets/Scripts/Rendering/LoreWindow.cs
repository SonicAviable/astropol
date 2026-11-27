using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using StellarisClone.Core;
using Sfx = StellarisClone.Core.Audio.Sfx;

namespace StellarisClone.Rendering
{
    /// <summary>
    /// «Архив» — лор галактики (клавиша Y, пункт левого меню): летопись Строителей, народы, их легенды,
    /// тайны и находки, узлы силы, развязки. Слева — разделы и записи (закрытые — «???» с подсказкой, как открыть,
    /// непрочитанные — с золотой точкой), справа — текст выбранной записи. Сверху — семь осколков летописи.
    /// Содержание и состояние — Core/Lore.
    /// </summary>
    public class LoreWindow : MonoBehaviour
    {
        public static LoreWindow Instance { get; private set; }
        public bool IsOpen => _root != null && LG.IsVisible(_root);
        public static bool IsOpenNow => Instance != null && Instance.IsOpen;

        private GameObject _root;
        private RectTransform _list, _textContent;
        private Text _subtitle, _eyebrow, _title, _body;
        private readonly List<Image> _shardMarks = new List<Image>();
        private string _selected = "intro_precursors";

        private static Color Muted => UIManager.DS.TextMuted;
        private static Color Gold => UIManager.DS.Gold;
        private static readonly Color CPanel = new Color(0.055f, 0.094f, 0.157f, 0.90f);
        private static readonly Color CRow = new Color(0.09f, 0.14f, 0.22f, 0.85f);
        private static readonly Color CRowSel = new Color(0.20f, 0.17f, 0.09f, 0.95f);
        private static readonly Color CViolet = new Color(0.89f, 0.72f, 1f);

        public static void Toggle()
        {
            if (Instance == null) new GameObject("[UI] LoreWindow").AddComponent<LoreWindow>();
            if (Instance.IsOpen) Instance.Close(); else Instance.Open();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        private void Update()
        {
            if (!IsOpen) return;
            if (Input.GetKeyDown(KeyCode.Escape)) { UIEsc.Consume(); Close(); }
        }

        public void Open()
        {
            if (_root == null) Build();
            if (_root == null) return;
            if (!Lore.IsUnlocked(_selected)) _selected = "intro_precursors";
            Refresh();
            LG.Show(_root);
            _root.transform.SetAsLastSibling();
            UIManager.Instance?.ShowModalDimPublic();
        }

        public void Close()
        {
            if (_root == null) return;
            LG.Hide(_root);
            UIManager.Instance?.HideModalDimPublic();
            TooltipSystem.Hide();
        }

        /// <summary>Открыть Архив сразу на нужной записи.</summary>
        public static void Show(string entryId)
        {
            if (Instance == null) new GameObject("[UI] LoreWindow").AddComponent<LoreWindow>();
            if (!string.IsNullOrEmpty(entryId)) Instance._selected = entryId;
            if (Instance.IsOpen) Instance.Refresh(); else Instance.Open();
        }

        // ================================================================ Построение

        private void Build()
        {
            var host = GameObject.Find("ModalCanvas");
            if (host == null) return;
            var rt = LGBuild.Rect(host.transform, "LoreArchive");
            rt.Stretch(60, 40, 60, 40);
            _root = rt.gameObject;
            _root.AddComponent<CanvasGroup>();
            var bg = _root.AddComponent<Image>();
            bg.color = new Color(0.055f, 0.094f, 0.157f, 0.97f);
            bg.raycastTarget = true;
            LG.Glass(_root).SetRim(new Color(Gold.r, Gold.g, Gold.b, 0.40f));
            LG.Motion(_root, LGAppear.Kind.Pop).fromScale = 0.97f;

            // Шапка
            var icon = LGIcons.Create(rt, LGIcon.Star, 26, Gold);
            icon.rectTransform.At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(28, -24), new Vector2(26, 26));
            var title = LGBuild.Label(rt, Loc.T("АРХИВ"), 28, Color.white, TextAnchor.UpperLeft, bold: true);
            title.rectTransform.At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(64, -18), new Vector2(600, 36));
            _subtitle = LGBuild.Label(rt, "", 12, Muted, TextAnchor.UpperLeft);
            _subtitle.rectTransform.At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(66, -56), new Vector2(900, 18));

            var close = LGBuild.Button(rt, "Close", UIManager.DS.BtnNeutral, new Color(1f, 1f, 1f, 0.25f), Close, LGIcon.Close, null, 12);
            ((RectTransform)close.transform).At(new Vector2(1, 1), new Vector2(1, 1), new Vector2(-20, -18), new Vector2(38, 38));
            TooltipHelper.Attach(close.gameObject, Loc.T("Закрыть  <color=#A9A392>(Esc / Y)</color>"));

            // Осколки летописи: семь ромбов
            var shards = LGBuild.Rect(rt, "Shards");
            shards.At(new Vector2(1, 1), new Vector2(1, 1), new Vector2(-74, -22), new Vector2(330, 34));
            var hit = shards.gameObject.AddComponent<Image>();
            hit.color = new Color(0, 0, 0, 0);
            var cap = LGBuild.Label(shards, Loc.T("ЛЕТОПИСЬ СТРОИТЕЛЕЙ"), 10, Muted, TextAnchor.UpperRight);
            cap.rectTransform.TopBand(0, 14);
            for (int i = 0; i < Lore.ShardTotal; i++)
            {
                var m = LGBuild.Panel(shards, "Shard" + i, new Color(1f, 1f, 1f, 0.12f));
                m.rectTransform.At(new Vector2(1, 0), new Vector2(0.5f, 0.5f), new Vector2(-8 - (Lore.ShardTotal - 1 - i) * 22, 8), new Vector2(11, 11));
                m.rectTransform.localRotation = Quaternion.Euler(0, 0, 45f);
                LG.Ignore(m.gameObject);
                _shardMarks.Add(m);
            }
            TooltipHelper.Attach(shards.gameObject, Loc.T("<b>Осколки летописи Строителей</b>\nИх находят при разведке древних мест, в ядре Предтеч, в ответе Сердца Тишины и внутри павших левиафанов. Соберите все семь, чтобы узнать, почему замолчали Строители, — и выбрать, что делать с этим знанием."));

            // Слева — список записей
            var left = LGBuild.Panel(rt, "ListPanel", CPanel);
            left.rectTransform.anchorMin = new Vector2(0, 0);
            left.rectTransform.anchorMax = new Vector2(0, 1);
            left.rectTransform.pivot = new Vector2(0, 0.5f);
            left.rectTransform.offsetMin = new Vector2(20, 20);
            left.rectTransform.offsetMax = new Vector2(360, -90);
            LG.Glass(left.gameObject, 10f).SetRim(new Color(Gold.r, Gold.g, Gold.b, 0.25f));
            _list = LGBuild.ScrollList(left.rectTransform, 3f, 8);

            // Справа — текст записи
            var right = LGBuild.Panel(rt, "TextPanel", CPanel);
            right.rectTransform.Stretch(374, 20, 20, 90);
            LG.Glass(right.gameObject, 10f).SetRim(new Color(Gold.r, Gold.g, Gold.b, 0.25f));
            var mo = LG.Motion(right.gameObject, LGAppear.Kind.SlideUp);
            mo.delay = 0.05f;
            mo.distance = 20f;
            var accent = LGBuild.Panel(right.rectTransform, "Accent", Gold);
            accent.rectTransform.TopBand(0, 3, 18, 18);
            LG.Ignore(accent.gameObject);

            var scrollHost = LGBuild.Rect(right.rectTransform, "Body");
            scrollHost.Stretch(28, 16, 28, 18);
            _textContent = LGBuild.ScrollList(scrollHost, 10f, 4);
            _eyebrow = LGBuild.Label(_textContent, "", 12, Gold, TextAnchor.UpperLeft).Eyebrow();
            _title = LGBuild.Label(_textContent, "", 26, Color.white, TextAnchor.UpperLeft, bold: true, wrap: true);
            _body = LGBuild.Label(_textContent, "", 15, new Color(0.925f, 0.898f, 0.827f), TextAnchor.UpperLeft, wrap: true);
            _body.lineSpacing = 1.15f;

            LG.Skin(_root.transform);
            _root.SetActive(false);
        }

        // ================================================================ Наполнение

        private void Refresh()
        {
            if (_root == null) return;
            int shards = Lore.ShardCount;
            _subtitle.text = Loc.F("Открыто записей: {0} из {1}  ·  Осколки летописи: {2} из {3}",
                Lore.UnlockedCount, Lore.All.Count, shards, Lore.ShardTotal);
            for (int i = 0; i < _shardMarks.Count; i++)
                _shardMarks[i].color = i < shards ? Gold : new Color(1f, 1f, 1f, 0.12f);

            LGBuild.Clear(_list);
            foreach (LoreCategory cat in System.Enum.GetValues(typeof(LoreCategory)))
            {
                var entries = new List<LoreEntry>();
                int open = 0;
                foreach (var e in Lore.All)
                    if (e.Cat == cat) { entries.Add(e); if (Lore.IsUnlocked(e)) open++; }
                if (entries.Count == 0) continue;
                // «Развязки» показываем, только когда до них дошло дело
                if (cat == LoreCategory.Endings && open == 0 && !Lore.ChronicleComplete) continue;

                var head = LGBuild.Rect(_list, "Head");
                LGBuild.Height(head.gameObject, 30f);
                var ht = LGBuild.Label(head, Lore.CategoryName(cat).ToUpper() + "  <color=#A9A392>" + open + "/" + entries.Count + "</color>",
                    11, Gold, TextAnchor.LowerLeft).Eyebrow();
                ht.supportRichText = true;
                ht.rectTransform.Stretch(6, 4, 4, 0);

                foreach (var e in entries) AddRow(e);
            }

            ShowEntry(Lore.Get(_selected));
        }

        private void AddRow(LoreEntry e)
        {
            bool unlocked = Lore.IsUnlocked(e);
            bool sel = e.Id == _selected;
            var img = LGBuild.Panel(_list, "Row", sel ? CRowSel : CRow, raycast: true);
            LGBuild.Height(img.gameObject, 34f);
            var btn = img.gameObject.AddComponent<Button>();
            btn.transition = Selectable.Transition.None;
            btn.targetGraphic = img;
            string id = e.Id;
            btn.onClick.AddListener(() =>
            {
                SFXManager.Play(Sfx.UiTab, 0.6f);
                _selected = id;
                Refresh();
            });
            LG.Button(img.gameObject, new Color(Gold.r, Gold.g, Gold.b, sel ? 0.7f : 0.18f), 6f, false);

            string label = unlocked ? e.Title : "???";
            if (e.Shard > 0 && !unlocked) label = Loc.F("Осколок {0}  ·  ???", e.Shard);
            var t = LGBuild.Label(img.transform, label, 12, unlocked ? Color.white : new Color(Muted.r, Muted.g, Muted.b, 0.7f), TextAnchor.MiddleLeft);
            t.rectTransform.Stretch(14, 0, 24, 0);

            if (!unlocked)
            {
                var lk = LGIcons.Create(img.transform, LGIcon.Lock, 11, new Color(Muted.r, Muted.g, Muted.b, 0.6f));
                lk.rectTransform.At(new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-10, 0), new Vector2(11, 11));
            }
            else if (!Lore.IsSeen(e))
            {
                var dot = LGBuild.Panel(img.transform, "New", Gold);
                dot.rectTransform.At(new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-12, 0), new Vector2(7, 7));
                dot.rectTransform.localRotation = Quaternion.Euler(0, 0, 45f);
                LG.Ignore(dot.gameObject);
            }
        }

        private void ShowEntry(LoreEntry e)
        {
            if (e == null) return;
            bool unlocked = Lore.IsUnlocked(e);
            string cat = Lore.CategoryName(e.Cat).ToUpper();
            if (e.Shard > 0) cat += "  ·  " + Loc.F("ОСКОЛОК {0} ИЗ {1}", e.Shard, Lore.ShardTotal);
            _eyebrow.text = cat;
            if (unlocked)
            {
                _title.text = e.Title;
                _body.text = e.Text;
                Lore.MarkSeen(e);
            }
            else
            {
                _title.text = "???";
                _body.text = "<color=#A9A392>" + Loc.T("Запись ещё не открыта.") + "</color>\n\n" +
                             (string.IsNullOrEmpty(e.Hint) ? "" : "<color=" + LGBuild.Hex(CViolet) + ">" + e.Hint + "</color>");
            }
            _textContent.anchoredPosition = Vector2.zero;
        }

        // ================================================================ Кружок на кнопке меню

        /// <summary>Счётчик непрочитанных записей на соте левого меню.</summary>
        public static void AttachBadge(Transform tile)
        {
            if (tile == null) return;
            var pill = LGBuild.Panel(tile, "LoreBadge", UIManager.DS.Gold);
            pill.sprite = HudArt.CutPanel(4);
            pill.type = Image.Type.Sliced;
            pill.rectTransform.At(new Vector2(1, 1), new Vector2(0.5f, 0.5f), new Vector2(-4, -2), new Vector2(20, 15));
            var label = LGBuild.Label(pill.transform, "", 9, new Color(0.08f, 0.05f, 0.0f), TextAnchor.MiddleCenter, bold: true);
            label.rectTransform.Stretch();
            var b = tile.gameObject.AddComponent<LoreBadge>();
            b.Pill = pill.gameObject;
            b.Label = label;
            pill.gameObject.SetActive(false);
        }
    }

    /// <summary>Кружок непрочитанных записей Архива на соте левого меню.</summary>
    internal sealed class LoreBadge : MonoBehaviour
    {
        public GameObject Pill;
        public Text Label;
        private float _t;

        private void Update()
        {
            if ((_t -= Time.unscaledDeltaTime) > 0f || Pill == null) return;
            _t = 1.5f;
            int n = LoreWindow.IsOpenNow || !UIManager.IsGameStarted ? 0 : Lore.UnreadCount;
            if (Pill.activeSelf != n > 0) Pill.SetActive(n > 0);
            if (Label != null) Label.text = n > 99 ? "99+" : n.ToString();
        }
    }
}
