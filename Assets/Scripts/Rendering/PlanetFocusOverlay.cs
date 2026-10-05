using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using StellarisClone.Core;

namespace StellarisClone.Rendering
{
    /// <summary>
    /// Полноэкранный осмотр планеты. Один из ключевых экранов игры.
    ///   • Слева — поверхность, климат, залежи
    ///   • В центре — вращающаяся планета
    ///   • Справа — население, районы, производство
    ///   • Снизу — панель действий
    /// </summary>
    public class PlanetFocusOverlay : MonoBehaviour
    {
        public static PlanetFocusOverlay Instance { get; private set; }
        public bool IsOpen => _isOpen;

        private Canvas _host;
        private GameObject _root;
        private CanvasGroup _group;
        private Font _font;

        private RawImage _planetImage;
        private Text _titleText;
        private Text _subtitleText;
        private Text _leftStats;
        private Text _rightStats;
        private Text _hintText;

        private Button _btnMine;
        private Text _btnMineTxt;
        private Button _btnColony;
        private Text _btnColonyTxt;
        private Button _btnTerra;
        private Text _btnTerraTxt;

        private PlanetData _planet;
        private StarSystem _system;
        private bool _isOpen;

        private const float PlanetSize = 500f;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        public void BindHost(Canvas modalCanvas)
        {
            _host = modalCanvas;
            _font = GameFont.Regular;
        }

        public void Open(PlanetData planet, StarSystem system)
        {
            if (planet == null) return;

            if (_host == null)
            {
                var modal = GameObject.Find("ModalCanvas");
                if (modal != null) _host = modal.GetComponent<Canvas>();
            }
            if (_host == null) return;

            if (_font == null) _font = GameFont.Regular;
            if (_root == null) Build();

            _planet = planet;
            _system = system;

            // Прячем маленькую модалку, чтобы не мешала
            PlanetOverviewModal.Instance?.Close();

            RefreshContent();
            BindHolo();

            _isOpen = true;
            LG.Show(_root);
            _root.transform.SetAsLastSibling();

            UIManager.Instance?.ShowModalDimPublic();
            MapModeController.HideGlobal();
            GalaxyMinimap.HideGlobal();
        }

        public void Close()
        {
            if (_root == null) return;
            _isOpen = false;
            LG.Hide(_root);

            PlanetHoloStudio.Instance?.Hide();
            MapModeController.ShowGlobal();
            UIManager.Instance?.HideModalDimPublic();
            GalaxyMinimap.ShowGlobal();
        }

        private void BindHolo()
        {
            var studio = PlanetHoloStudio.Instance;
            if (studio == null || _planet == null) return;

            Material src = null;
            var inst = SystemViewManager.Instance?.GetPlanetInstance(_planet);
            if (inst?.Renderer != null) src = inst.Renderer.sharedMaterial;

            studio.ShowPlanet(_planet, src);
            if (_planetImage != null) _planetImage.texture = studio.Target;
        }

        // ==================== ОБНОВЛЕНИЕ ДАННЫХ ====================

        private void RefreshContent()
        {
            if (_planet == null) return;

            bool surveyed = _system != null && _system.IsSurveyed;
            string sov = _system == null || _system.OwnerId < 0
                ? "Нейтральный фронтир"
                : _system.OwnerId == 0 ? "Суверенитет империи"
                : "Оккупация соперника";

            _titleText.text = _planet.Name.ToUpper();
            _subtitleText.text = $"{_planet.ClassDisplayName}   ·   {sov}";

            if (!surveyed)
            {
                _leftStats.text = "<color=#FFAA88>Информация засекречена.\n\nОтправьте научный корабль\nдля разведки системы.</color>";
                _rightStats.text = "<color=#8AA2A8>Данные появятся\nпосле разведки.</color>";
                ConfigureActions(surveyed);
                return;
            }

            // === ЛЕВАЯ ПАНЕЛЬ: поверхность + залежи ===
            string habColor = _planet.HabitabilityPercent >= 70 ? StatFormat.GreenHex
                            : _planet.HabitabilityPercent >= 40 ? StatFormat.GoldHex
                            : StatFormat.RedHex;

            // Детерминированные атмосферные показатели (по имени планеты)
            var rng = new System.Random(_planet.Name.GetHashCode());
            float gravity = 0.6f + (float)rng.NextDouble() * 0.9f;      // 0.6 - 1.5 g
            string atmoType = gravity < 0.85f ? "Разрежённая"
                             : gravity > 1.25f ? "Плотная"
                             : "Стандартная";
            int temperature = -40 + rng.Next(0, 90);                     // -40..+50 C
            int axialTilt = rng.Next(0, 45);

            _leftStats.text =
                $"<color=#F2C747><b>ПОВЕРХНОСТЬ</b></color>\n" +
                $"Класс: {_planet.ClassDisplayName}\n" +
                $"Пригодность: <color={habColor}>{_planet.HabitabilityPercent}%</color>\n" +
                $"Размер: {_planet.PlanetSize} районов\n" +
                $"Гравитация: {gravity:0.00} g\n" +
                $"Атмосфера: {atmoType}\n" +
                $"Ср. температура: {temperature:+#;-#;0}°C\n" +
                $"Наклон оси: {axialTilt}°\n\n" +
                $"<color=#F2C747><b>ОРБИТА</b></color>\n" +
                $"Дистанция: {_planet.OrbitRadius:0.0} AU\n" +
                $"Период: {(_planet.OrbitSpeed * 1.4f):0.0} ч\n\n" +
                $"<color=#F2C747><b>ПРИРОДНЫЕ ЗАЛЕЖИ</b></color>\n" +
                $"<color=#33E6CC>◆ Титан</color>   <b>{_planet.MineralDeposit}</b>\n" +
                $"<color=#F2C747>⚡ Гелий-3</color>   <b>{_planet.EnergyDeposit}</b>";

            // === ПРАВАЯ ПАНЕЛЬ: население + районы + производство ===
            int pop = _planet.Population;
            int housing = _planet.HousingCapacity;
            int idle = _planet.IdlePops;
            int employed = pop - idle;

            string popColor = housing >= pop ? StatFormat.GreenHex : StatFormat.RedHex;

            int urban = _planet.GetDistrictCount(DistrictType.Urban);
            int mining = _planet.GetDistrictCount(DistrictType.Mining);
            int generator = _planet.GetDistrictCount(DistrictType.Generator);
            int industrial = _planet.GetDistrictCount(DistrictType.Industrial);

            float growthSpeed = _planet.PopGrowthBaseSpeedPctPerMonth;

            _rightStats.text =
                $"<color=#F2C747><b>НАСЕЛЕНИЕ</b></color>\n" +
                $"Жители: <color={popColor}>{pop}</color> / {housing}\n" +
                $"Занятые: {employed}   Безработные: " +
                (idle > 0 ? $"<color={StatFormat.RedHex}>{idle}</color>" : "0") + "\n" +
                $"Рост: {StatFormat.Colored(growthSpeed, "%/мес")}\n\n" +
                $"<color=#F2C747><b>РАЙОНЫ</b> ({_planet.BuiltDistricts}/{_planet.MaxDistricts})</color>\n" +
                $"● Городские:  {urban}\n" +
                $"● Горнодобыча:  {mining}\n" +
                $"● Энергетика:  {generator}\n" +
                $"● Промышленные:  {industrial}\n\n" +
                $"<color=#F2C747><b>ПРОИЗВОДСТВО / МЕСЯЦ</b></color>\n" +
                $"<color=#33E6CC>◆ Титан</color>   {StatFormat.Positive("+" + _planet.ProducedMineralsPerMonth)}\n" +
                $"<color=#F2C747>⚡ Гелий-3</color>   {StatFormat.Positive("+" + _planet.ProducedEnergyPerMonth)}\n" +
                $"<color=#20EBB0>⬢ Сплавы</color>   {StatFormat.Positive("+" + _planet.ProducedAlloysPerMonth)}";

            ConfigureActions(surveyed);
        }

        private void ConfigureActions(bool surveyed)
        {
            bool owned = _system != null && _system.OwnerId == 0;

            // Добывающий комплекс
            bool canMine = surveyed && owned && !_planet.HasMiningStation;
            _btnMine.gameObject.SetActive(true);
            _btnMine.interactable = canMine;
            if (_planet.HasMiningStation)
            {
                _btnMineTxt.text = "✓  ДОБЫВАЮЩИЙ КОМПЛЕКС АКТИВЕН";
            }
            else if (!surveyed)
            {
                _btnMineTxt.text = "✕  ТРЕБУЕТСЯ РАЗВЕДКА";
            }
            else if (!owned)
            {
                _btnMineTxt.text = "✕  СИСТЕМА НЕ ПРИНАДЛЕЖИТ ВАМ";
            }
            else
            {
                var eco = EconomyManager.Instance;
                bool can = eco != null && eco.Minerals >= 50f;
                _btnMine.interactable = can;
                _btnMineTxt.text = can
                    ? "▶  ДОБЫВАЮЩИЙ КОМПЛЕКС  ·  50 ◆"
                    : "✕  НЕ ХВАТАЕТ ТИТАНА (50 ◆)";
            }

            // Колония
            bool canCol = surveyed && owned && _planet.CanColonize;
            _btnColony.gameObject.SetActive(true);
            _btnColony.interactable = canCol;
            if (_planet.Population > 0)
                _btnColonyTxt.text = "✓  КОЛОНИЯ ОСНОВАНА";
            else if (!surveyed)
                _btnColonyTxt.text = "✕  ТРЕБУЕТСЯ РАЗВЕДКА";
            else if (!owned)
                _btnColonyTxt.text = "✕  СИСТЕМА НЕ ПРИНАДЛЕЖИТ ВАМ";
            else if (!_planet.CanColonize)
                _btnColonyTxt.text = "✕  ПЛАНЕТА НЕПРИГОДНА";
            else
            {
                var eco = EconomyManager.Instance;
                bool can = eco != null && eco.Minerals >= 80f && eco.Alloys >= 20f && eco.Influence >= 25f;
                _btnColony.interactable = can;
                _btnColonyTxt.text = can
                    ? "▶  ОСНОВАТЬ КОЛОНИЮ  ·  80 ◆  25 ★"
                    : "✕  НЕ ХВАТАЕТ РЕСУРСОВ";
            }

            // Терраформинг
            bool canTerra = surveyed && owned && _planet.CanTerraform && _planet.HabitabilityPercent < 70;
            _btnTerra.gameObject.SetActive(true);
            _btnTerra.interactable = canTerra;
            if (!_planet.CanTerraform)
                _btnTerraTxt.text = "✕  ТЕРРАФОРМИНГ НЕВОЗМОЖЕН";
            else if (!surveyed)
                _btnTerraTxt.text = "✕  ТРЕБУЕТСЯ РАЗВЕДКА";
            else if (!owned)
                _btnTerraTxt.text = "✕  СИСТЕМА НЕ ПРИНАДЛЕЖИТ ВАМ";
            else if (_planet.HabitabilityPercent >= 70)
                _btnTerraTxt.text = "✓  ПЛАНЕТА УЖЕ ПРИГОДНА";
            else
            {
                var eco = EconomyManager.Instance;
                bool can = eco != null && eco.EnergyCredits >= 120f && eco.Minerals >= 90f && eco.Influence >= 10f;
                _btnTerra.interactable = can;
                _btnTerraTxt.text = can
                    ? "▶  ТЕРРАФОРМИРОВАТЬ  ·  120 ⚡  90 ◆"
                    : "✕  НЕ ХВАТАЕТ РЕСУРСОВ";
            }
        }

        // ==================== ДЕЙСТВИЯ ====================

        private void OnMine()
        {
            if (_planet == null) return;
            if (FleetManager.Instance != null && FleetManager.Instance.BuildMiningStationOnPlanet(_planet))
            {
                SystemViewManager.Instance?.SpawnStationOnActivePlanet(_planet);
                RefreshContent();
                BindHolo();
            }
        }

        private void OnColony()
        {
            if (_planet != null && _planet.TryFoundColony())
            {
                RefreshContent();
                BindHolo();
            }
        }

        private void OnTerra()
        {
            if (_planet != null && _planet.TryStartTerraform())
            {
                BindHolo();
                RefreshContent();
            }
        }

        // ==================== ПОСТРОЕНИЕ ====================

        private void Build()
        {
            _root = new GameObject("PlanetFocusOverlay");
            _root.transform.SetParent(_host.transform, false);

            var rt = _root.AddComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            _group = _root.AddComponent<CanvasGroup>();

            // Фон — глубоко размытый и притушенный мир (скрим), а не плоская заливка
            var bg = _root.AddComponent<Image>();
            bg.color = new Color(0.006f, 0.018f, 0.028f, 1f);
            bg.raycastTarget = true;
            LG.Scrim(_root).FillMultiplier = 1.9f;
            var motion = LG.Motion(_root, LGAppear.Kind.Fade);
            motion.inDuration = 0.4f;
            motion.outDuration = 0.25f;

            // === HEADER ===
            var header = new GameObject("Header");
            header.transform.SetParent(_root.transform, false);
            var hHeadRt = header.AddComponent<RectTransform>();
            hHeadRt.anchorMin = new Vector2(0, 1);
            hHeadRt.anchorMax = new Vector2(1, 1);
            hHeadRt.pivot = new Vector2(0.5f, 1);
            hHeadRt.sizeDelta = new Vector2(0, 84);
            hHeadRt.anchoredPosition = new Vector2(0, -110);

            _titleText = MakeText(header.transform, "", 30, FontStyle.Bold,
                UIManager.DS.NeonCyan, TextAnchor.UpperCenter);
            _titleText.rectTransform.anchorMin = new Vector2(0, 0.5f);
            _titleText.rectTransform.anchorMax = new Vector2(1, 1);
            _titleText.rectTransform.offsetMin = new Vector2(220, 0);
            _titleText.rectTransform.offsetMax = new Vector2(-220, -4);

            _subtitleText = MakeText(header.transform, "", 12, FontStyle.Italic,
                UIManager.DS.TextMuted, TextAnchor.UpperCenter);
            _subtitleText.rectTransform.anchorMin = new Vector2(0, 0);
            _subtitleText.rectTransform.anchorMax = new Vector2(1, 0.5f);
            _subtitleText.rectTransform.offsetMin = new Vector2(220, 8);
            _subtitleText.rectTransform.offsetMax = new Vector2(-220, 0);

            var hSep = new GameObject("HeaderSep");
            hSep.transform.SetParent(_root.transform, false);
            var hsRt = hSep.AddComponent<RectTransform>();
            hsRt.anchorMin = new Vector2(0.2f, 1);
            hsRt.anchorMax = new Vector2(0.8f, 1);
            hsRt.pivot = new Vector2(0.5f, 1);
            hsRt.sizeDelta = new Vector2(0, 1);
            hsRt.anchoredPosition = new Vector2(0, -196);
            var hsImg = hSep.AddComponent<Image>();
            hsImg.color = new Color(0.18f, 0.65f, 0.60f, 0.45f);
            hsImg.raycastTarget = false;

            // === ПЛАНЕТА (с круглой маской) ===
            var planetMask = new GameObject("PlanetMask");
            planetMask.transform.SetParent(_root.transform, false);
            var pmRt = planetMask.AddComponent<RectTransform>();
            pmRt.anchorMin = pmRt.anchorMax = new Vector2(0.5f, 0.5f);
            pmRt.pivot = new Vector2(0.5f, 0.5f);
            pmRt.sizeDelta = new Vector2(PlanetSize, PlanetSize);
            pmRt.anchoredPosition = new Vector2(0, -30);

            var maskImg = planetMask.AddComponent<Image>();
            maskImg.sprite = MakeCircleSprite(512);
            maskImg.color = Color.white;
            maskImg.raycastTarget = false;

            var mask = planetMask.AddComponent<Mask>();
            mask.showMaskGraphic = false;

            var planetHost = new GameObject("PlanetImage");
            planetHost.transform.SetParent(planetMask.transform, false);
            var phRt = planetHost.AddComponent<RectTransform>();
            phRt.anchorMin = Vector2.zero;
            phRt.anchorMax = Vector2.one;
            phRt.offsetMin = Vector2.zero;
            phRt.offsetMax = Vector2.zero;

            _planetImage = planetHost.AddComponent<RawImage>();
            _planetImage.color = Color.white;
            _planetImage.raycastTarget = true;
            planetHost.AddComponent<HoloDragCatcher>();

            // Одно аккуратное кольцо вокруг планеты — тонкая стеклянная линза с неоновым ореолом
            var ring = new GameObject("PlanetRing");
            ring.transform.SetParent(_root.transform, false);
            var ringRt = ring.AddComponent<RectTransform>();
            ringRt.anchorMin = ringRt.anchorMax = new Vector2(0.5f, 0.5f);
            ringRt.pivot = new Vector2(0.5f, 0.5f);
            ringRt.sizeDelta = new Vector2(PlanetSize + 28f, PlanetSize + 28f);
            ringRt.anchoredPosition = new Vector2(0, -30);
            var ringImg = ring.AddComponent<Image>();
            ringImg.color = new Color(0, 0, 0, 0);
            ringImg.raycastTarget = false;
            var ringFx = LG.Border(ring, new Color(0.45f, 0.95f, 0.90f, 0.35f));
            ringFx.Radius = (PlanetSize + 28f) * 0.5f;
            ringFx.GlowMultiplier = 1.4f;


            // === ЛЕВАЯ ПАНЕЛЬ ===
            var leftPanel = new GameObject("LeftPanel");
            leftPanel.transform.SetParent(_root.transform, false);
            var lpRt = leftPanel.AddComponent<RectTransform>();
            lpRt.anchorMin = new Vector2(0, 0.5f);
            lpRt.anchorMax = new Vector2(0, 0.5f);
            lpRt.pivot = new Vector2(0, 0.5f);
            lpRt.sizeDelta = new Vector2(360, 440);
            lpRt.anchoredPosition = new Vector2(60, 20);

            var lpBg = leftPanel.AddComponent<Image>();
            lpBg.color = UIManager.DS.BgDeep;
            StylePanel(leftPanel, LGAppear.Kind.SlideLeft, 0.08f);

            _leftStats = MakeText(leftPanel.transform, "", 13, FontStyle.Normal,
                UIManager.DS.TextPrimary, TextAnchor.UpperLeft);
            _leftStats.rectTransform.anchorMin = Vector2.zero;
            _leftStats.rectTransform.anchorMax = Vector2.one;
            _leftStats.rectTransform.offsetMin = new Vector2(20, 18);
            _leftStats.rectTransform.offsetMax = new Vector2(-18, -18);
            _leftStats.lineSpacing = 1.4f;
            _leftStats.supportRichText = true;

            // === ПРАВАЯ ПАНЕЛЬ ===
            var rightPanel = new GameObject("RightPanel");
            rightPanel.transform.SetParent(_root.transform, false);
            var rpRt = rightPanel.AddComponent<RectTransform>();
            rpRt.anchorMin = new Vector2(1, 0.5f);
            rpRt.anchorMax = new Vector2(1, 0.5f);
            rpRt.pivot = new Vector2(1, 0.5f);
            rpRt.sizeDelta = new Vector2(360, 440);
            rpRt.anchoredPosition = new Vector2(-60, 20);

            var rpBg = rightPanel.AddComponent<Image>();
            rpBg.color = UIManager.DS.BgDeep;
            StylePanel(rightPanel, LGAppear.Kind.SlideRight, 0.14f);

            _rightStats = MakeText(rightPanel.transform, "", 13, FontStyle.Normal,
                UIManager.DS.TextPrimary, TextAnchor.UpperLeft);
            _rightStats.rectTransform.anchorMin = Vector2.zero;
            _rightStats.rectTransform.anchorMax = Vector2.one;
            _rightStats.rectTransform.offsetMin = new Vector2(20, 18);
            _rightStats.rectTransform.offsetMax = new Vector2(-18, -18);
            _rightStats.lineSpacing = 1.4f;
            _rightStats.supportRichText = true;

            // === НИЖНЯЯ ПАНЕЛЬ ДЕЙСТВИЙ ===
            var actionBar = new GameObject("ActionBar");
            actionBar.transform.SetParent(_root.transform, false);
            var abRt = actionBar.AddComponent<RectTransform>();
            abRt.anchorMin = new Vector2(0, 0);
            abRt.anchorMax = new Vector2(1, 0);
            abRt.pivot = new Vector2(0.5f, 0);
            abRt.sizeDelta = new Vector2(-400, 56);
            abRt.anchoredPosition = new Vector2(0, 90);

            var abBg = actionBar.AddComponent<Image>();
            abBg.color = UIManager.DS.BgDeep;
            StylePanel(actionBar, LGAppear.Kind.SlideDown, 0.2f);
            actionBar.GetComponent<LiquidGlassEffect>().Radius = 28f;

            _btnMine = MakeActionButton(actionBar.transform, UIManager.DS.BtnSuccess,
                "▶  ДОБЫВАЮЩИЙ КОМПЛЕКС  ·  50 ◆", -400, 260, OnMine, out _btnMineTxt);
            _btnColony = MakeActionButton(actionBar.transform, UIManager.DS.BtnPrimary,
                "▶  ОСНОВАТЬ КОЛОНИЮ", 0, 260, OnColony, out _btnColonyTxt);
            _btnTerra = MakeActionButton(actionBar.transform, UIManager.DS.BtnNeutral,
                "▶  ТЕРРАФОРМИРОВАТЬ", 400, 260, OnTerra, out _btnTerraTxt);

            // === ПОДСКАЗКА ===
            _hintText = MakeText(_root.transform,
                "ЗАЖМИ ЛКМ НА ПЛАНЕТЕ И ВОДИ МЫШЬЮ — ВРАЩЕНИЕ   ·   ESC — ЗАКРЫТЬ",
                11, FontStyle.Italic, UIManager.DS.TextMuted, TextAnchor.MiddleCenter);
            var hHintRt = _hintText.rectTransform;
            hHintRt.anchorMin = new Vector2(0, 0);
            hHintRt.anchorMax = new Vector2(1, 0);
            hHintRt.pivot = new Vector2(0.5f, 0);
            hHintRt.sizeDelta = new Vector2(0, 20);
            hHintRt.anchoredPosition = new Vector2(0, 44);

            // === КНОПКА ЗАКРЫТИЯ (левый верхний угол) ===
            var closeBtn = new GameObject("CloseBtn");
            closeBtn.transform.SetParent(_root.transform, false);
            var cRt = closeBtn.AddComponent<RectTransform>();
            cRt.anchorMin = cRt.anchorMax = new Vector2(0, 1);
            cRt.pivot = new Vector2(0, 1);
            cRt.sizeDelta = new Vector2(180, 40);
            cRt.anchoredPosition = new Vector2(40, -50);

            var cImg = closeBtn.AddComponent<Image>();
            cImg.color = new Color(0.10f, 0.28f, 0.32f, 1f);

            var cBtn = closeBtn.AddComponent<Button>();
            cBtn.onClick.AddListener(Close);
            LG.Button(closeBtn, new Color(UIManager.DS.NeonCyan.r, UIManager.DS.NeonCyan.g, UIManager.DS.NeonCyan.b, 0.7f));
            LG.Motion(closeBtn, LGAppear.Kind.Fade).delay = 0.12f;

            var cTxt = MakeText(closeBtn.transform, "◀  ЗАКРЫТЬ  (ESC)", 12, FontStyle.Bold,
                Color.white, TextAnchor.MiddleCenter);
            cTxt.rectTransform.anchorMin = Vector2.zero;
            cTxt.rectTransform.anchorMax = Vector2.one;
            cTxt.rectTransform.offsetMin = Vector2.zero;
            cTxt.rectTransform.offsetMax = Vector2.zero;

            LG.Skin(_root.transform);
            _root.SetActive(false);
        }

        // ==================== ХЕЛПЕРЫ ====================

        private Button MakeActionButton(Transform parent, Color bg, string label,
            float xPos, float width, System.Action onClick, out Text labelOut)
        {
            var go = new GameObject("ActionBtn");
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(width, 40);
            rt.anchoredPosition = new Vector2(xPos, 0);

            var img = go.AddComponent<Image>();
            img.color = bg;
            img.raycastTarget = true;

            var btn = go.AddComponent<Button>();
            btn.onClick.AddListener(() => onClick?.Invoke());
            LG.Button(go);

            labelOut = MakeText(go.transform, label, 11, FontStyle.Bold,
                Color.white, TextAnchor.MiddleCenter);
            labelOut.rectTransform.anchorMin = Vector2.zero;
            labelOut.rectTransform.anchorMax = Vector2.one;
            labelOut.rectTransform.offsetMin = new Vector2(6, 0);
            labelOut.rectTransform.offsetMax = new Vector2(-6, 0);

            return btn;
        }

        /// <summary>Парящая стеклянная панель с каскадным появлением.</summary>
        private static void StylePanel(GameObject panel, LGAppear.Kind kind, float delay)
        {
            LG.Glass(panel, 22f).SetRim(new Color(0.45f, 0.95f, 0.90f, 0.40f));
            var m = LG.Motion(panel, kind);
            m.delay = delay;
            m.distance = 36f;
            m.inDuration = 0.5f;
        }

        private static Sprite MakeCircleSprite(int res)
        {
            var tex = new Texture2D(res, res, TextureFormat.RGBA32, false)
            { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            Vector2 c = new Vector2(res * 0.5f, res * 0.5f);
            float r = res * 0.5f;
            for (int y = 0; y < res; y++)
            for (int x = 0; x < res; x++)
            {
                float d = Vector2.Distance(new Vector2(x, y), c);
                float a = Mathf.Clamp01(1f - (d - (r - 2f)));
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, res, res), new Vector2(0.5f, 0.5f), 100);
        }

        private static Sprite MakeCircleOutline(int res, float thickness, float radiusRatio)
        {
            var tex = new Texture2D(res, res, TextureFormat.RGBA32, false)
            { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            Vector2 c = new Vector2(res * 0.5f, res * 0.5f);
            float rTarget = res * 0.5f * radiusRatio;
            for (int y = 0; y < res; y++)
            for (int x = 0; x < res; x++)
            {
                float d = Vector2.Distance(new Vector2(x, y), c);
                float delta = Mathf.Abs(d - rTarget);
                float a = Mathf.Clamp01(1f - delta / thickness);
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, res, res), new Vector2(0.5f, 0.5f), 100);
        }

        private Text MakeText(Transform parent, string val, int size, FontStyle style,
            Color col, TextAnchor anchor)
        {
            var go = new GameObject("Txt");
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<Text>();
            t.font = (style == FontStyle.Bold) ? GameFont.Bold : GameFont.Regular;
            t.text = val;
            t.fontSize = size;
            t.fontStyle = style;
            t.color = col;
            t.alignment = anchor;
            t.raycastTarget = false;
            t.supportRichText = true;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            return t;
        }

        private void Update()
        {
            if (!_isOpen) return;
            if (Input.GetKeyDown(KeyCode.Escape)) Close();
        }
    }
}