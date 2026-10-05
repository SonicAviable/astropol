using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using StellarisClone.Core;

namespace StellarisClone.Rendering
{
    public class TechTreePanel : MonoBehaviour
    {
        public static TechTreePanel Instance { get; private set; }
        public bool IsOpen => _isOpen;

        private static class ST
        {
            public static readonly Color DimmerBg         = new Color(0.005f, 0.015f, 0.020f, 0.88f);
            public static readonly Color CanvasBg         = UIManager.DS.BgDeep;
            public static readonly Color TopBarBg         = UIManager.DS.BgHeader;

            public static readonly Color TabBtnNormal     = UIManager.DS.BtnNeutral;
            public static readonly Color TabBtnActive     = UIManager.DS.BtnPrimary;
            public static readonly Color TabTextActive    = UIManager.DS.NeonCyan;
            public static readonly Color TabTextNormal    = UIManager.DS.TextMuted;

            public static readonly Color CardBg           = UIManager.DS.BgSlot;
            public static readonly Color CardHeaderBg     = UIManager.DS.BgHeader;
            public static readonly Color CardBorderNormal = UIManager.DS.NeonTeal;
            public static readonly Color CardBorderHover  = UIManager.DS.NeonCyan;

            public static readonly Color RareHeaderBg     = new Color(0.380f, 0.120f, 0.160f, 1.00f);
            public static readonly Color RareBorder       = UIManager.DS.Red;

            public static readonly Color DoneHeaderBg     = new Color(0.060f, 0.220f, 0.160f, 1.00f);
            public static readonly Color DoneBorder       = UIManager.DS.Green;

            public static readonly Color ActiveHeaderBg   = new Color(0.080f, 0.280f, 0.320f, 1.00f);
            public static readonly Color ActiveBorder     = UIManager.DS.NeonCyan;

            public static readonly Color WireNormal       = new Color(0.120f, 0.380f, 0.420f, 0.75f);
            public static readonly Color WireActive       = UIManager.DS.NeonCyan;

            public static readonly Color TextWhite        = UIManager.DS.TextPrimary;
            public static readonly Color TextSub          = UIManager.DS.TextMuted;
            public static readonly Color Gold             = UIManager.DS.Gold;
            public static readonly Color GreenCheck       = UIManager.DS.Green;
            public static readonly Color Cyan             = UIManager.DS.NeonCyan;
            public static readonly Color Outline          = UIManager.DS.TextShadow;
        }

        private const float WinWidth  = 1640f;
        private const float WinHeight = 940f;
        private const float TopBarH   = 52f;

        private const float CardW     = 290f;
        private const float CardH     = 78f;
        private const float ColStep   = 360f;
        private const float RowStep   = 108f;
        private const float BasePadX  = 50f;
        private const float BasePadY  = 40f;

        private Canvas _modalCanvas;
        private Font _font;
        private GameObject _dimmer;
        private GameObject _windowRoot;
        private CanvasGroup _windowGroup;
        private RectTransform _scrollContent;
        private ScrollRect _scrollRect;
        private InputField _searchInput;
        private Transform _tabsBar;
        private Image _backgroundWatermark;
        private TechTreeVideoBackground _videoBg;

        private enum FilterBranch { All, Physics, Society, Engineering }
        private FilterBranch _currentBranch = FilterBranch.Physics;
        private string _searchFilter = "";
        private bool _isBuilt;
        private bool _isOpen;

        private readonly Dictionary<string, TechCardView> _spawnedCards = new Dictionary<string, TechCardView>();
        private readonly List<GameObject> _spawnedWires = new List<GameObject>();
        private readonly Dictionary<FilterBranch, TabButtonRef> _tabRefs = new Dictionary<FilterBranch, TabButtonRef>();

        public class TabButtonRef
        {
            public Image Bg;
            public Text Label;
            public LiquidGlassEffect Fx;
        }

        public class TechCardView
        {
            public Technology Data;
            public GameObject Root;
            public RectTransform Rt;
            public Image Bg;
            public Image HeaderBg;
            public Outline BorderOutline;
            public Text Title;
            public Text Subtitle;
            public Text Cost;
            public Image CheckBox;
            public Text CheckMark;
            public RectTransform ProgressBar;
            public GameObject ActiveBadge;
            public Button Btn;
            public TechCardFX FX;
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        public void Initialize(Canvas canvas)
        {
            Instance = this;
            _modalCanvas = canvas;
            CleanupGhostObjects();
            BuildUI();
        }

        private void CleanupGhostObjects()
        {
            string[] ghostNames = { "StellarisTechTree", "StellarisTechWindow", "TechTreePanel", "TechTreeDimmer" };
            foreach (var gName in ghostNames)
            {
                var objs = GameObject.FindObjectsByType<GameObject>(FindObjectsInactive.Include, FindObjectsSortMode.None);
                foreach (var obj in objs)
                {
                    if (obj != null && obj != gameObject && obj.name == gName)
                        Destroy(obj);
                }
            }
        }

        public void Open()
        {
            if (!_isBuilt) return;
            _isOpen = true;

            MapModeController.HideGlobal();

            ToggleWorldNameplates(false);

            LG.Show(_dimmer);
            LG.Show(_windowRoot);
            _dimmer.transform.SetAsLastSibling();
            _windowRoot.transform.SetAsLastSibling();

            RefreshTabsUI();
            RebuildTree();
            UpdateWatermarkArt();
            Subscribe();

            _videoBg?.OnOpen(GetBranchKey(_currentBranch));

            if (_scrollContent != null)
                _scrollContent.anchoredPosition = Vector2.zero;
        }

        public void Close()
        {
            _isOpen = false;
            Unsubscribe();
            LG.Hide(_windowRoot);
            LG.Hide(_dimmer);
            ToggleWorldNameplates(true);
            UIManager.Instance?.HideModalDimPublic();

            _videoBg?.OnClose();
            MapModeController.ShowGlobal();
        }

        private void ToggleWorldNameplates(bool visible)
        {
            var nameplateCanvas = GameObject.Find("NameplateCanvas");
            if (nameplateCanvas != null)
            {
                var cg = nameplateCanvas.GetComponent<CanvasGroup>();
                if (cg == null) cg = nameplateCanvas.AddComponent<CanvasGroup>();
                cg.alpha = visible ? 1f : 0f;
                cg.blocksRaycasts = visible;
            }
        }

        private static string GetBranchKey(FilterBranch branch) => branch switch
        {
            FilterBranch.All         => "all",
            FilterBranch.Physics     => "physics",
            FilterBranch.Society     => "society",
            FilterBranch.Engineering => "engineering",
            _                        => "all"
        };

        // ==================== ПОСТРОЕНИЕ ====================

        private void BuildUI()
        {
            _font = GameFont.Regular;

            BuildDimmer();
            BuildWindow();
            BuildTopBar();
            BuildTreeArea();

            _dimmer.SetActive(false);
            _windowRoot.SetActive(false);
            _isBuilt = true;
        }

        private void BuildDimmer()
        {
            _dimmer = new GameObject("TechTreeDimmer");
            _dimmer.transform.SetParent(_modalCanvas.transform, false);

            var rt = _dimmer.AddComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.sizeDelta = Vector2.zero;

            var img = _dimmer.AddComponent<Image>();
            img.color = ST.DimmerBg;
            img.raycastTarget = true;
            LG.Scrim(_dimmer);
            LG.Motion(_dimmer, LGAppear.Kind.Fade).outDuration = 0.26f;

            var dimBtn = _dimmer.AddComponent<Button>();
            dimBtn.transition = Selectable.Transition.None;
            dimBtn.onClick.AddListener(Close);
        }

        private void BuildWindow()
        {
            _windowRoot = new GameObject("StellarisTechTree");
            _windowRoot.transform.SetParent(_modalCanvas.transform, false);

            var rt = _windowRoot.AddComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(WinWidth, WinHeight);

            _windowGroup = _windowRoot.AddComponent<CanvasGroup>();

            var bg = _windowRoot.AddComponent<Image>();
            bg.color = ST.CanvasBg;
            bg.raycastTarget = true;
            LG.Glass(_windowRoot, 28f).SetRim(new Color(0.45f, 0.95f, 0.90f, 0.42f));
            var motion = LG.Motion(_windowRoot, LGAppear.Kind.Pop);
            motion.fromScale = 0.95f;
            motion.inDuration = 0.45f;

            // Видео-фон обрезается по скруглению стеклянного окна
            var videoMask = LG.RoundedMask(_windowRoot.transform, 27f, 1f);
            videoMask.SetAsFirstSibling();

            var videoHost = new GameObject("VideoHost");
            videoHost.transform.SetParent(videoMask, false);
            var vhRt = videoHost.AddComponent<RectTransform>();
            vhRt.anchorMin = Vector2.zero;
            vhRt.anchorMax = Vector2.one;
            vhRt.offsetMin = Vector2.zero;
            vhRt.offsetMax = Vector2.zero;

            _videoBg = videoHost.AddComponent<TechTreeVideoBackground>();
            _videoBg.Initialize(vhRt);
        }

        private void BuildTopBar()
        {
            var topBar = new GameObject("TopBar");
            topBar.transform.SetParent(_windowRoot.transform, false);
            var rt = topBar.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0, 1);
            rt.anchorMax = new Vector2(1, 1);
            rt.pivot = new Vector2(0.5f, 1);
            rt.sizeDelta = new Vector2(0, TopBarH);
            rt.anchoredPosition = Vector2.zero;

            var tbImg = topBar.AddComponent<Image>();
            tbImg.color = new Color(ST.TopBarBg.r, ST.TopBarBg.g, ST.TopBarBg.b, 0.95f);
            LG.Header(topBar);
            _tabsBar = topBar.transform;

            var sep = new GameObject("BottomLine");
            sep.transform.SetParent(topBar.transform, false);
            var sRt = sep.AddComponent<RectTransform>();
            sRt.anchorMin = new Vector2(0, 0);
            sRt.anchorMax = new Vector2(1, 0);
            sRt.pivot = new Vector2(0.5f, 0.5f);
            sRt.sizeDelta = new Vector2(-80, 1.5f);
            sRt.anchoredPosition = Vector2.zero;
            sep.AddComponent<Image>().color = new Color(ST.Cyan.r, ST.Cyan.g, ST.Cyan.b, 0.5f);
            LG.Line(sep, hairline: true);

            float startX = 14f;
            CreateBranchTab("ВСЕ", LGIcon.Menu, FilterBranch.All, ref startX, 86f);
            CreateBranchTab("ФИЗИКА", LGIcon.Research, FilterBranch.Physics, ref startX, 122f);
            CreateBranchTab("ОБЩЕСТВО", LGIcon.Society, FilterBranch.Society, ref startX, 136f);
            CreateBranchTab("ИНЖЕНЕРИЯ", LGIcon.Gear, FilterBranch.Engineering, ref startX, 142f);

            BuildRightControls(topBar.transform);
        }

        private void CreateBranchTab(string label, LGIcon icon, FilterBranch branch, ref float currentX, float width)
        {
            var tab = new GameObject($"Tab_{branch}");
            tab.transform.SetParent(_tabsBar, false);
            var rt = tab.AddComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0, 0.5f);
            rt.pivot = new Vector2(0, 0.5f);
            rt.sizeDelta = new Vector2(width, 34f);
            rt.anchoredPosition = new Vector2(currentX, 0);

            var bg = tab.AddComponent<Image>();
            bg.color = ST.TabBtnNormal;

            var txt = LGIcons.IconLabel(tab.transform, icon, label, 11, ST.Cyan, ST.TabTextNormal, 14f);

            var captured = branch;
            var tabBtn = tab.AddComponent<Button>();
            var tabFx = LG.Button(tab, new Color(ST.Cyan.r, ST.Cyan.g, ST.Cyan.b, 0.25f));
            tabBtn.onClick.AddListener(() =>
            {
                _currentBranch = captured;
                RefreshTabsUI();
                RebuildTree();
                UpdateWatermarkArt();
                _videoBg?.SetBranch(GetBranchKey(_currentBranch));
                if (_scrollContent != null) _scrollContent.anchoredPosition = Vector2.zero;
                SFXManager.Play("ui_click", 1f, 1.05f);
            });

            _tabRefs[branch] = new TabButtonRef { Bg = bg, Label = txt, Fx = tabFx };
            currentX += width + 6f;
        }

        private void BuildRightControls(Transform parent)
        {
            var searchObj = new GameObject("SearchBox");
            searchObj.transform.SetParent(parent, false);
            var sRt = searchObj.AddComponent<RectTransform>();
            sRt.anchorMin = sRt.anchorMax = new Vector2(1, 0.5f);
            sRt.pivot = new Vector2(1, 0.5f);
            sRt.sizeDelta = new Vector2(240f, 32f);
            sRt.anchoredPosition = new Vector2(-150f, 0);

            searchObj.AddComponent<Image>().color = UIManager.DS.BgVisor;
            LG.Apply(searchObj, LiquidGlassEffect.Role.Field)
              .SetRim(new Color(ST.Cyan.r, ST.Cyan.g, ST.Cyan.b, 0.35f));

            var placeHolder = MakeLabel(searchObj.transform, "Поиск по названию...", 12, FontStyle.Italic, ST.TextSub, TextAnchor.MiddleLeft);
            placeHolder.rectTransform.anchorMin = Vector2.zero;
            placeHolder.rectTransform.anchorMax = Vector2.one;
            placeHolder.rectTransform.offsetMin = new Vector2(12, 0);
            placeHolder.rectTransform.offsetMax = new Vector2(-12, 0);

            var inputTxt = MakeLabel(searchObj.transform, "", 12, FontStyle.Normal, ST.TextWhite, TextAnchor.MiddleLeft);
            inputTxt.rectTransform.anchorMin = Vector2.zero;
            inputTxt.rectTransform.anchorMax = Vector2.one;
            inputTxt.rectTransform.offsetMin = new Vector2(12, 0);
            inputTxt.rectTransform.offsetMax = new Vector2(-12, 0);

            _searchInput = searchObj.AddComponent<InputField>();
            _searchInput.textComponent = inputTxt;
            _searchInput.placeholder = placeHolder;
            _searchInput.onValueChanged.AddListener((val) =>
            {
                _searchFilter = val.Trim().ToLower();
                RebuildTree();
            });

            var closeBtn = new GameObject("CloseBtn");
            closeBtn.transform.SetParent(parent, false);
            var cRt = closeBtn.AddComponent<RectTransform>();
            cRt.anchorMin = cRt.anchorMax = new Vector2(1, 0.5f);
            cRt.pivot = new Vector2(1, 0.5f);
            cRt.sizeDelta = new Vector2(115f, 32f);
            cRt.anchoredPosition = new Vector2(-14f, 0);

            closeBtn.AddComponent<Image>().color = ST.TabBtnActive;
            closeBtn.AddComponent<Button>().onClick.AddListener(Close);
            LG.Button(closeBtn, new Color(ST.Cyan.r, ST.Cyan.g, ST.Cyan.b, 0.6f));
            var cTxt = MakeLabel(closeBtn.transform, "◀  ЗАКРЫТЬ", 12, FontStyle.Bold, ST.TextWhite, TextAnchor.MiddleCenter);
            cTxt.rectTransform.anchorMin = Vector2.zero;
            cTxt.rectTransform.anchorMax = Vector2.one;
        }

        private void BuildTreeArea()
        {
            var area = new GameObject("TreeViewportArea");
            area.transform.SetParent(_windowRoot.transform, false);
            var rt = area.AddComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(14f, 16f);
            rt.offsetMax = new Vector2(-14f, -(TopBarH + 8f));

            var watermarkObj = new GameObject("BranchWatermark");
            watermarkObj.transform.SetParent(area.transform, false);
            var wRt = watermarkObj.AddComponent<RectTransform>();
            wRt.anchorMin = new Vector2(0.5f, 0.5f);
            wRt.anchorMax = new Vector2(0.5f, 0.5f);
            wRt.sizeDelta = new Vector2(700f, 700f);
            _backgroundWatermark = watermarkObj.AddComponent<Image>();
            _backgroundWatermark.raycastTarget = false;
            LG.Ignore(watermarkObj);   // спрайт-водяной знак, не стекло

            _scrollRect = area.AddComponent<ScrollRect>();
            _scrollRect.horizontal = true;
            _scrollRect.vertical = true;
            _scrollRect.movementType = ScrollRect.MovementType.Clamped;
            _scrollRect.scrollSensitivity = 50f;

            var viewport = new GameObject("Viewport");
            viewport.transform.SetParent(area.transform, false);
            var vRt = viewport.AddComponent<RectTransform>();
            vRt.anchorMin = Vector2.zero;
            vRt.anchorMax = Vector2.one;
            vRt.offsetMin = Vector2.zero;
            vRt.offsetMax = Vector2.zero;

            var vpImg = viewport.AddComponent<Image>();
            vpImg.color = new Color(0, 0, 0, 0.01f);
            vpImg.raycastTarget = true;

            var mask = viewport.AddComponent<Mask>();
            mask.showMaskGraphic = false;

            var content = new GameObject("Content");
            content.transform.SetParent(viewport.transform, false);
            var cRt = content.AddComponent<RectTransform>();
            cRt.anchorMin = new Vector2(0, 1);
            cRt.anchorMax = new Vector2(0, 1);
            cRt.pivot = new Vector2(0, 1);

            _scrollRect.viewport = vRt;
            _scrollRect.content = cRt;
            _scrollContent = cRt;
        }

        // ==================== ВОДЯНОЙ ЗНАК ====================

        private void UpdateWatermarkArt()
        {
            if (_backgroundWatermark == null) return;

            Texture2D tex = new Texture2D(128, 128, TextureFormat.RGBA32, false);
            Color clear = new Color(0, 0, 0, 0);
            for (int y = 0; y < 128; y++)
                for (int x = 0; x < 128; x++)
                    tex.SetPixel(x, y, clear);

            Color artColor = _currentBranch switch
            {
                FilterBranch.Physics     => new Color(0.20f, 0.92f, 0.82f, 0.025f),
                FilterBranch.Society     => new Color(0.30f, 0.92f, 0.55f, 0.025f),
                FilterBranch.Engineering => new Color(0.95f, 0.78f, 0.28f, 0.025f),
                _                        => new Color(0.18f, 0.65f, 0.60f, 0.018f)
            };

            if (_currentBranch == FilterBranch.Engineering || _currentBranch == FilterBranch.All)
            {
                for (int y = 16; y < 112; y++)
                {
                    int span = (int)((112 - y) * 0.45f);
                    for (int x = 64 - span; x <= 64 + span; x++)
                        tex.SetPixel(x, y, artColor);
                }
                for (int x = 20; x < 108; x++)
                    for (int y = 30; y < 45; y++)
                        tex.SetPixel(x, y, artColor);
            }
            else if (_currentBranch == FilterBranch.Physics)
            {
                Vector2 center = new Vector2(64, 64);
                for (int y = 0; y < 128; y++)
                {
                    for (int x = 0; x < 128; x++)
                    {
                        float d = Vector2.Distance(new Vector2(x, y), center);
                        if (Mathf.Abs(d - 48f) < 3.5f || Mathf.Abs(d - 28f) < 3f || d < 12f)
                            tex.SetPixel(x, y, artColor);
                    }
                }
            }
            else if (_currentBranch == FilterBranch.Society)
            {
                for (int y = 20; y < 40; y++)
                    for (int x = 20; x < 108; x++) tex.SetPixel(x, y, artColor);

                for (int col = 26; col <= 102; col += 15)
                    for (int y = 40; y < 85; y++)
                        for (int x = col; x < col + 8; x++) tex.SetPixel(x, y, artColor);

                for (int y = 85; y < 115; y++)
                {
                    int span = (int)((115 - y) * 1.5f);
                    for (int x = 64 - span; x <= 64 + span; x++)
                        tex.SetPixel(x, y, artColor);
                }
            }

            tex.Apply();
            _backgroundWatermark.sprite = Sprite.Create(tex, new Rect(0, 0, 128, 128), new Vector2(0.5f, 0.5f));
        }

        // ==================== РАСКЛАДКА ====================

        private void RebuildTree()
        {
            ClearTree();
            var tm = TechnologyManager.Instance;
            if (tm == null) return;

            var filteredTechs = GetFilteredTechs(tm);
            if (filteredTechs.Count == 0) return;

            var depthMap = new Dictionary<string, int>();
            foreach (var t in filteredTechs)
                depthMap[t.Id] = GetTechDepth(t, tm);

            int maxDepth = 0;
            foreach (var d in depthMap.Values)
                if (d > maxDepth) maxDepth = d;

            var cardPositions = new Dictionary<string, Vector2>();
            int currentRow = 0;

            var rootTechs = new List<Technology>();
            foreach (var t in filteredTechs)
            {
                if (t.RequiredTechIds == null || t.RequiredTechIds.Length == 0)
                    rootTechs.Add(t);
            }

            rootTechs.Sort((a, b) => a.Category.CompareTo(b.Category));

            var visited = new HashSet<string>();

            foreach (var root in rootTechs)
            {
                AssignBranchPositions(root, depthMap, cardPositions, ref currentRow, visited, filteredTechs, tm);
            }

            foreach (var remaining in filteredTechs)
            {
                if (!visited.Contains(remaining.Id))
                {
                    float x = BasePadX + depthMap[remaining.Id] * ColStep;
                    float y = -(BasePadY + currentRow * RowStep);
                    cardPositions[remaining.Id] = new Vector2(x, y);
                    currentRow++;
                    visited.Add(remaining.Id);
                }
            }

            float contentWidth  = Mathf.Max(1650f, BasePadX * 2f + (maxDepth + 1) * ColStep + CardW);
            float contentHeight = Mathf.Max(950f, BasePadY * 2f + currentRow * RowStep + CardH + 60f);

            _scrollContent.sizeDelta = new Vector2(contentWidth, contentHeight);

            foreach (var tech in filteredTechs)
            {
                var card = CreateStellarisCard(tech, cardPositions[tech.Id]);
                _spawnedCards[tech.Id] = card;
            }

            foreach (var tech in filteredTechs)
            {
                if (tech.RequiredTechIds == null) continue;
                foreach (var reqId in tech.RequiredTechIds)
                {
                    if (_spawnedCards.TryGetValue(reqId, out var fromCard) &&
                        _spawnedCards.TryGetValue(tech.Id, out var toCard))
                    {
                        var reqTech = tm.FindTech(reqId);
                        bool isDone = reqTech != null && reqTech.IsResearched;
                        DrawTreeConnection(fromCard.Rt.anchoredPosition, toCard.Rt.anchoredPosition, isDone);
                    }
                }
            }

            RefreshActiveTechProgress();
            LG.Skin(_scrollContent);
        }

        private int GetTechDepth(Technology t, TechnologyManager tm)
        {
            if (t.RequiredTechIds == null || t.RequiredTechIds.Length == 0) return 0;
            int maxParentDepth = 0;
            foreach (var reqId in t.RequiredTechIds)
            {
                var parent = tm.FindTech(reqId);
                if (parent != null)
                {
                    int pd = GetTechDepth(parent, tm);
                    if (pd > maxParentDepth) maxParentDepth = pd;
                }
            }
            return maxParentDepth + 1;
        }

        private void AssignBranchPositions(
            Technology current,
            Dictionary<string, int> depthMap,
            Dictionary<string, Vector2> cardPositions,
            ref int currentRow,
            HashSet<string> visited,
            List<Technology> scope,
            TechnologyManager tm)
        {
            if (visited.Contains(current.Id)) return;
            visited.Add(current.Id);

            float x = BasePadX + depthMap[current.Id] * ColStep;
            float y = -(BasePadY + currentRow * RowStep);
            cardPositions[current.Id] = new Vector2(x, y);

            var children = new List<Technology>();
            foreach (var t in scope)
            {
                if (t.RequiredTechIds != null && Array.IndexOf(t.RequiredTechIds, current.Id) >= 0)
                    children.Add(t);
            }

            if (children.Count == 0)
            {
                currentRow++;
            }
            else
            {
                for (int i = 0; i < children.Count; i++)
                {
                    if (i > 0) currentRow++;
                    AssignBranchPositions(children[i], depthMap, cardPositions, ref currentRow, visited, scope, tm);
                }
            }
        }

        private List<Technology> GetFilteredTechs(TechnologyManager tm)
        {
            var result = new List<Technology>();
            foreach (var t in tm.AllTechs)
            {
                if (!BelongsToBranch(t, _currentBranch)) continue;
                if (!string.IsNullOrEmpty(_searchFilter) && !t.Name.ToLower().Contains(_searchFilter))
                    continue;
                result.Add(t);
            }
            return result;
        }

        private bool BelongsToBranch(Technology t, FilterBranch branch)
        {
            if (branch == FilterBranch.All) return true;

            switch (branch)
            {
                case FilterBranch.Physics:
                    return t.Category == TechCategory.Sensors ||
                           t.Category == TechCategory.Reactor ||
                           t.Category == TechCategory.Propulsion ||
                           t.Id.StartsWith("wpn_las") ||
                           t.Id.StartsWith("def_shl");

                case FilterBranch.Society:
                    return t.Category == TechCategory.Society;

                case FilterBranch.Engineering:
                    return t.Category == TechCategory.Industry ||
                           t.Category == TechCategory.Construction ||
                           t.Id.StartsWith("wpn_kin") ||
                           t.Id.StartsWith("wpn_pls") ||
                           t.Id.StartsWith("def_arm");

                default:
                    return true;
            }
        }

        private TechCardView CreateStellarisCard(Technology tech, Vector2 pos)
        {
            var tm = TechnologyManager.Instance;
            var obj = new GameObject($"Card_{tech.Id}");
            obj.transform.SetParent(_scrollContent, false);

            var rt = obj.AddComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
            rt.pivot = new Vector2(0, 1);
            rt.sizeDelta = new Vector2(CardW, CardH);
            rt.anchoredPosition = pos;

            bool isResearched = tech.IsResearched;
            bool isCurrent = tm.IsBeingResearched(tech);
            bool isAvailable = tm.IsTechAvailable(tech);

            bool isRare = tech.Tier >= 2 || tech.Id.Contains("ant") || tech.Id.Contains("pls");

            Color borderColor = isResearched ? ST.DoneBorder : (isCurrent ? ST.ActiveBorder : (isRare ? ST.RareBorder : ST.CardBorderNormal));
            Color headerColor = isResearched ? ST.DoneHeaderBg : (isCurrent ? ST.ActiveHeaderBg : (isRare ? ST.RareHeaderBg : ST.CardHeaderBg));

            var bgImg = obj.AddComponent<Image>();
            bgImg.color = new Color(ST.CardBg.r, ST.CardBg.g, ST.CardBg.b, 0.85f);

            var borderOutline = obj.AddComponent<Outline>();
            borderOutline.effectColor = borderColor;
            borderOutline.effectDistance = new Vector2(1.5f, -1.5f);

            var headerObj = new GameObject("HeaderStrip");
            headerObj.transform.SetParent(obj.transform, false);
            var hRt = headerObj.AddComponent<RectTransform>();
            hRt.anchorMin = new Vector2(0, 1);
            hRt.anchorMax = new Vector2(1, 1);
            hRt.pivot = new Vector2(0.5f, 1);
            hRt.sizeDelta = new Vector2(0, 24f);
            hRt.anchoredPosition = Vector2.zero;

            var hImg = headerObj.AddComponent<Image>();
            hImg.color = new Color(headerColor.r, headerColor.g, headerColor.b, 0.95f);

            var titleTxt = MakeLabel(headerObj.transform, tech.Name, 11, FontStyle.Bold, ST.TextWhite, TextAnchor.MiddleLeft);
            titleTxt.rectTransform.anchorMin = Vector2.zero;
            titleTxt.rectTransform.anchorMax = Vector2.one;
            titleTxt.rectTransform.offsetMin = new Vector2(8, 0);
            titleTxt.rectTransform.offsetMax = new Vector2(-4, 0);

            var iconBox = new GameObject("IconBox");
            iconBox.transform.SetParent(obj.transform, false);
            var iRt = iconBox.AddComponent<RectTransform>();
            iRt.anchorMin = iRt.anchorMax = new Vector2(0, 0);
            iRt.pivot = new Vector2(0, 0);
            iRt.sizeDelta = new Vector2(44f, 44f);
            iRt.anchoredPosition = new Vector2(6f, 4f);

            iconBox.AddComponent<Image>().color = new Color(0.015f, 0.040f, 0.055f, 0.75f);
            var iOutline = iconBox.AddComponent<Outline>();
            iOutline.effectColor = new Color(borderColor.r, borderColor.g, borderColor.b, 0.5f);
            iOutline.effectDistance = new Vector2(1f, -1f);

            LGIcons.Create(iconBox.transform, LGIcons.ForTechCategory(tech.Category), 26f, TechCategoryInfo.Color(tech.Category));

            var infoBox = new GameObject("InfoBox");
            infoBox.transform.SetParent(obj.transform, false);
            var infRt = infoBox.AddComponent<RectTransform>();
            infRt.anchorMin = new Vector2(0, 0);
            infRt.anchorMax = new Vector2(1, 1);
            infRt.offsetMin = new Vector2(56f, 4f);
            infRt.offsetMax = new Vector2(-32f, -26f);

            string tierStr = tech.Tier == 0 ? "Базовый" : $"Tier {tech.Tier}";
            var subTxt = MakeLabel(infoBox.transform, $"{TechCategoryInfo.Name(tech.Category)} ({tierStr})", 10, FontStyle.Normal, ST.TextSub, TextAnchor.UpperLeft);
            subTxt.rectTransform.anchorMin = new Vector2(0, 1);
            subTxt.rectTransform.anchorMax = new Vector2(1, 1);
            subTxt.rectTransform.anchoredPosition = new Vector2(0, -2);
            subTxt.rectTransform.sizeDelta = new Vector2(0, 16);

            float penalty = tm.GetYearPenalty(tech);
            int days = Mathf.RoundToInt(tech.BaseDays * penalty);
            var clock = LGIcons.Create(infoBox.transform, LGIcon.Clock, 12f, ST.Gold);
            clock.rectTransform.anchorMin = clock.rectTransform.anchorMax = new Vector2(0, 0);
            clock.rectTransform.pivot = new Vector2(0, 0);
            clock.rectTransform.anchoredPosition = new Vector2(0, 4);

            var costTxt = MakeLabel(infoBox.transform, $"{days} дн.", 10, FontStyle.Bold, ST.Gold, TextAnchor.LowerLeft);
            costTxt.rectTransform.anchorMin = new Vector2(0, 0);
            costTxt.rectTransform.anchorMax = new Vector2(1, 0);
            costTxt.rectTransform.pivot = new Vector2(0.5f, 0);
            costTxt.rectTransform.offsetMin = new Vector2(17, 2);
            costTxt.rectTransform.offsetMax = new Vector2(0, 18);

            var checkObj = new GameObject("CheckBox");
            checkObj.transform.SetParent(obj.transform, false);
            var chkRt = checkObj.AddComponent<RectTransform>();
            chkRt.anchorMin = chkRt.anchorMax = new Vector2(1, 0.5f);
            chkRt.pivot = new Vector2(1, 0.5f);
            chkRt.sizeDelta = new Vector2(20f, 20f);
            chkRt.anchoredPosition = new Vector2(-8f, -10f);

            var chkImg = checkObj.AddComponent<Image>();
            chkImg.color = isResearched ? new Color(0.06f, 0.22f, 0.16f, 0.85f) : new Color(0.02f, 0.06f, 0.08f, 0.75f);
            LG.Platter(checkObj, 10f).SetRim(isResearched
                ? new Color(ST.GreenCheck.r, ST.GreenCheck.g, ST.GreenCheck.b, 0.85f)
                : new Color(1f, 1f, 1f, 0.18f));

            var chkMark = MakeLabel(checkObj.transform, isResearched ? "✔" : (isCurrent ? "▶" : ""), 12, FontStyle.Bold, isResearched ? ST.GreenCheck : ST.TextWhite, TextAnchor.MiddleCenter);
            chkMark.rectTransform.anchorMin = Vector2.zero;
            chkMark.rectTransform.anchorMax = Vector2.one;

            var barBg = new GameObject("CardProgressBar");
            barBg.transform.SetParent(obj.transform, false);
            var pbRt = barBg.AddComponent<RectTransform>();
            pbRt.anchorMin = new Vector2(0, 0);
            pbRt.anchorMax = new Vector2(1, 0);
            pbRt.pivot = new Vector2(0.5f, 0f);
            pbRt.sizeDelta = new Vector2(-28f, 3f);
            pbRt.anchoredPosition = new Vector2(0f, 4f);
            barBg.AddComponent<Image>().color = new Color(0, 0, 0, 0.6f);
            LG.Platter(barBg, 1.5f).FillMultiplier = 2f;

            var fill = new GameObject("Fill");
            fill.transform.SetParent(barBg.transform, false);
            var fRt = fill.AddComponent<RectTransform>();
            fRt.anchorMin = Vector2.zero;
            fRt.anchorMax = new Vector2(tech.ProgressNormalized, 1f);
            fRt.offsetMin = fRt.offsetMax = Vector2.zero;
            var fImg = fill.AddComponent<Image>();
            fImg.color = ST.Cyan;
            LG.Line(fill);

            barBg.SetActive(isCurrent);

            var btn = obj.AddComponent<Button>();
            btn.interactable = isAvailable && !isResearched && !isCurrent;
            // Карточка — стеклянная кнопка; Outline выше служит источником кромки (TechCardFX анимирует её)
            var cardFx = LG.Button(obj, null, 14f, animateScale: false);
            cardFx.FillMultiplier = 0.75f;
            var capturedTech = tech;
            btn.onClick.AddListener(() =>
            {
                var confirm = TechConfirmDialog.Instance;
                if (confirm != null)
                {
                    confirm.Open(capturedTech, () =>
                    {
                        tm.AssignTech(capturedTech);
                        RebuildTree();
                        SFXManager.Play("ui_click", 1f, 1.1f);
                    });
                }
                else
                {
                    tm.AssignTech(capturedTech);
                    RebuildTree();
                    SFXManager.Play("ui_click", 1f, 1.1f);
                }
            });

            var fx = obj.AddComponent<TechCardFX>();
            fx.Init(bgImg, borderOutline, borderColor, isCurrent);

            TooltipHelper.Attach(obj, $"<b>{tech.Name}</b>\n{tech.Description}\n\n{TechCategoryInfo.Name(tech.Category)} ({tierStr})\nБазовое время: {days} дн.");

            return new TechCardView
            {
                Data = tech,
                Root = obj,
                Rt = rt,
                Bg = bgImg,
                HeaderBg = hImg,
                BorderOutline = borderOutline,
                Title = titleTxt,
                Subtitle = subTxt,
                Cost = costTxt,
                CheckBox = chkImg,
                CheckMark = chkMark,
                ProgressBar = fRt,
                ActiveBadge = barBg,
                Btn = btn,
                FX = fx
            };
        }

        // ==================== ЛИНИИ ====================

        private void DrawTreeConnection(Vector2 fromPos, Vector2 toPos, bool isComplete)
        {
            Vector2 start = fromPos + new Vector2(CardW, -CardH * 0.5f);
            Vector2 end   = toPos + new Vector2(0, -CardH * 0.5f);

            float midX = (start.x + end.x) * 0.5f;
            Color wireColor = isComplete ? ST.WireActive : ST.WireNormal;

            AddWireSegment(start, new Vector2(midX, start.y), wireColor, isComplete);
            AddWireSegment(new Vector2(midX, start.y), new Vector2(midX, end.y), wireColor, isComplete);
            AddWireSegment(new Vector2(midX, end.y), end, wireColor, isComplete);
        }

        private void AddWireSegment(Vector2 a, Vector2 b, Color col, bool emitParticles)
        {
            var wire = new GameObject("Wire");
            wire.transform.SetParent(_scrollContent, false);
            wire.transform.SetAsFirstSibling();

            var rt = wire.AddComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
            rt.pivot = new Vector2(0, 0.5f);

            Vector2 diff = b - a;
            float dist = diff.magnitude;
            rt.sizeDelta = new Vector2(dist, 2.2f);
            rt.anchoredPosition = a;
            rt.localRotation = Quaternion.Euler(0, 0, Mathf.Atan2(diff.y, diff.x) * Mathf.Rad2Deg);

            var img = wire.AddComponent<Image>();
            img.color = col;
            img.raycastTarget = false;
            LG.Line(wire).Intensity = emitParticles ? 0.9f : 0.3f;
            _spawnedWires.Add(wire);

            if (emitParticles && dist > 15f)
            {
                var pulse = new GameObject("EnergyPulse");
                pulse.transform.SetParent(wire.transform, false);
                var pRt = pulse.AddComponent<RectTransform>();
                pRt.anchorMin = pRt.anchorMax = new Vector2(0, 0.5f);
                pRt.pivot = new Vector2(0.5f, 0.5f);
                pRt.sizeDelta = new Vector2(10f, 6f);

                var pImg = pulse.AddComponent<Image>();
                pImg.color = Color.white;
                pImg.raycastTarget = false;
                LG.Dot(pulse);

                var anim = pulse.AddComponent<WirePulseAnimator>();
                anim.Init(pRt, dist, col);
            }
        }

        private void RefreshTabsUI()
        {
            foreach (var kv in _tabRefs)
            {
                bool active = kv.Key == _currentBranch;
                kv.Value.Bg.color = active ? ST.TabBtnActive : ST.TabBtnNormal;
                kv.Value.Label.color = active ? ST.TabTextActive : ST.TabTextNormal;
                if (kv.Value.Fx != null)
                {
                    kv.Value.Fx.SetRim(new Color(ST.Cyan.r, ST.Cyan.g, ST.Cyan.b, active ? 0.85f : 0.2f));
                    kv.Value.Fx.GlowMultiplier = active ? 2.2f : 0.5f;
                }
            }
        }

        private void ClearTree()
        {
            foreach (var card in _spawnedCards.Values)
                if (card.Root != null) Destroy(card.Root);
            _spawnedCards.Clear();

            foreach (var wire in _spawnedWires)
                if (wire != null) Destroy(wire);
            _spawnedWires.Clear();
        }

        private void Subscribe()
        {
            if (TechnologyManager.Instance == null) return;
            TechnologyManager.OnTechProgressUpdated += OnProgressUpdated;
            TechnologyManager.OnTechCompleted += OnTechDone;
        }

        private void Unsubscribe()
        {
            TechnologyManager.OnTechProgressUpdated -= OnProgressUpdated;
            TechnologyManager.OnTechCompleted -= OnTechDone;
        }

        private void OnProgressUpdated()
        {
            if (!_isOpen) return;
            RefreshActiveTechProgress();
        }

        private void RefreshActiveTechProgress()
        {
            var tm = TechnologyManager.Instance;
            if (tm == null) return;

            foreach (var card in _spawnedCards.Values)
            {
                if (card.Data == null) continue;
                bool isCur = tm.IsBeingResearched(card.Data);

                if (card.ActiveBadge != null)
                    card.ActiveBadge.SetActive(isCur);

                if (isCur && card.ProgressBar != null)
                {
                    card.ProgressBar.anchorMax = new Vector2(card.Data.ProgressNormalized, 1f);
                    float penalty = tm.GetYearPenalty(card.Data);
                    int remaining = Mathf.Max(0, (int)(card.Data.BaseDays * penalty - card.Data.AccumulatedDays));
                    card.Cost.text = $"<color=#20EBB0>В ПРОЦЕССЕ · {remaining} дн.</color>";
                }
            }
        }

        private void OnTechDone(Technology t)
        {
            if (_isOpen) RebuildTree();
        }

        private Text MakeLabel(Transform parent, string content, int size, FontStyle style, Color col, TextAnchor anchor)
        {
            var go = new GameObject("Txt");
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<Text>();
            t.font = (style == FontStyle.Bold) ? GameFont.Bold : GameFont.Regular;
            t.text = content;
            t.fontSize = size;
            t.fontStyle = style;
            t.color = col;
            t.alignment = anchor;
            t.raycastTarget = false;
            t.supportRichText = true;

            var outl = go.AddComponent<Outline>();
            outl.effectColor = ST.Outline;
            outl.effectDistance = new Vector2(1f, -1f);
            return t;
        }

        // ==================== ЭФФЕКТЫ ====================

        public class TechCardFX : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
        {
            private Image _bg;
            private Outline _outline;
            private Color _baseBorder;
            private bool _isCurrent;
            private bool _hovered;

            public void Init(Image bg, Outline outline, Color baseBorder, bool current)
            {
                _bg = bg;
                _outline = outline;
                _baseBorder = baseBorder;
                _isCurrent = current;
            }

            public void OnPointerEnter(PointerEventData e) => _hovered = true;
            public void OnPointerExit(PointerEventData e) => _hovered = false;

            private void Update()
            {
                if (_bg == null || _outline == null) return;

                if (_hovered)
                {
                    _outline.effectColor = Color.Lerp(_outline.effectColor, ST.CardBorderHover, Time.unscaledDeltaTime * 14f);
                    transform.localScale = Vector3.Lerp(transform.localScale, Vector3.one * 1.02f, Time.unscaledDeltaTime * 14f);
                }
                else
                {
                    transform.localScale = Vector3.Lerp(transform.localScale, Vector3.one, Time.unscaledDeltaTime * 14f);
                    if (_isCurrent)
                    {
                        float pulse = 0.65f + Mathf.Sin(Time.unscaledTime * 5f) * 0.35f;
                        _outline.effectColor = new Color(ST.Cyan.r, ST.Cyan.g, ST.Cyan.b, pulse);
                    }
                    else
                    {
                        _outline.effectColor = Color.Lerp(_outline.effectColor, _baseBorder, Time.unscaledDeltaTime * 14f);
                    }
                }
            }
        }

        public class WirePulseAnimator : MonoBehaviour
        {
            private RectTransform _rt;
            private float _length;
            private Color _color;
            private Image _img;
            private float _progress;

            public void Init(RectTransform rt, float length, Color color)
            {
                _rt = rt;
                _length = length;
                _color = color;
                _img = GetComponent<Image>();
                _progress = UnityEngine.Random.Range(0f, 1f);
            }

            private void Update()
            {
                if (_rt == null) return;
                _progress += Time.unscaledDeltaTime * 0.6f;
                if (_progress > 1f) _progress -= 1f;

                _rt.anchoredPosition = new Vector2(_progress * _length, 0f);
                float alpha = Mathf.Sin(_progress * Mathf.PI);
                if (_img != null) _img.color = new Color(_color.r, _color.g, _color.b, alpha * 0.85f);
            }
        }
    }
}