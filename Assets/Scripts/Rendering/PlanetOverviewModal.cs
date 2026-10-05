using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using StellarisClone.Core;

namespace StellarisClone.Rendering
{
    public class PlanetOverviewModal : MonoBehaviour
    {
        public static PlanetOverviewModal Instance { get; private set; }
        public bool IsOpen => _root != null && LG.IsVisible(_root);

        private Canvas _host;
        private GameObject _root;
        private CanvasGroup _group;
        private Font _font;
        private RawImage _holo;
        private Text _title;
        private Text _subtitle;
        private Text _body;
        private Text _grid;
        private Button _btnMine;
        private Button _btnColony;
        private Button _btnTerra;
        private Button _btnExpand;
        private Text _btnMineTxt;
        private Text _btnColonyTxt;
        private Text _btnTerraTxt;
        private PlanetData _planet;
        private StarSystem _system;

        private void Awake()
        {
            Instance = this;
            _font = GameFont.Regular;
        }

        public void BindHost(Canvas modalCanvas)
        {
            _host = modalCanvas;
            if (_root == null) Build();
        }

        public void Open(PlanetData planet, StarSystem system)
        {
            if (_host == null)
            {
                var modal = GameObject.Find("ModalCanvas");
                if (modal != null) _host = modal.GetComponent<Canvas>();
            }
            if (_root == null) Build();
            if (_root == null) return;

            _planet = planet;
            _system = system;

            BindHolo();
            Refresh();

            UIManager.Instance?.ShowModalDimPublic();
            LG.Show(_root);
            _root.transform.SetAsLastSibling();
        }

        public void Close()
        {
            PlanetHoloStudio.Instance?.Hide();
            if (_root != null) LG.Hide(_root);
            UIManager.Instance?.HideModalDimPublic();
        }

        private void BindHolo()
        {
            var studio = PlanetHoloStudio.Instance;
            if (studio == null || _planet == null) return;

            Material src = null;
            var inst = SystemViewManager.Instance?.GetPlanetInstance(_planet);
            if (inst?.Renderer != null) src = inst.Renderer.sharedMaterial;

            studio.ShowPlanet(_planet, src);
            if (_holo != null) _holo.texture = studio.Target;
        }

        private void Refresh()
        {
            if (_planet == null) return;

            bool surveyed = _system != null && _system.IsSurveyed;
            string sov = _system == null || _system.OwnerId < 0
                ? "Нейтральный фронтир"
                : _system.OwnerId == 0 ? "Суверенитет империи"
                : "Оккупация соперника";

            _title.text = _planet.Name.ToUpper();
            _subtitle.text = $"{_planet.ClassDisplayName}  ·  {sov}";

            float dist = _planet.OrbitRadius;
            float rot = Mathf.Max(4f, 28f - _planet.Size * 6f);
            _grid.text = $"ORBITAL DISTANCE  {dist:0.0} AU\nROTATION PERIOD   {rot:0.0} h";

            string station = _planet.HasMiningStation
                ? "Орбитальный комплекс: <color=#5F5>активен</color>"
                : "Орбитальный комплекс: <color=#AAA>отсутствует</color>";

            _body.text =
                $"<color=#F2C747><b>КЛИМАТ И БИОСФЕРА</b></color>\n" +
                $"Пригодность: Habitability {_planet.HabitabilityPercent}%\n" +
                $"Размер мира: Planet Size {_planet.PlanetSize}\n\n" +
                $"<color=#F2C747><b>ЗАЛЕЖИ И РАЗРАБОТКА</b></color>\n" +
                $"<color=#33E6CC>◆ Титан</color>  {_planet.MineralDeposit}\n" +
                $"<color=#F2C747>⚡ Гелий-3</color>  {_planet.EnergyDeposit}\n" +
                $"{station}\n" +
                (surveyed ? "Сенсорный архив: полный" : "<color=#FFAA88>Требуется разведка системы</color>");

            ConfigureActions(surveyed);
        }

        private void ConfigureActions(bool surveyed)
        {
            bool owned = _system != null && _system.OwnerId == 0;

            bool canMine = surveyed && owned && !_planet.HasMiningStation;
            _btnMine.interactable = canMine;
            _btnMineTxt.text = _planet.HasMiningStation
                ? "✓  ДОБЫВАЮЩИЙ КОМПЛЕКС АКТИВЕН"
                : "▶  ПОСТРОИТЬ ДОБЫВАЮЩИЙ КОМПЛЕКС";

            bool canCol = surveyed && owned && _planet.CanColonize;
            _btnColony.interactable = canCol;
            _btnColonyTxt.text = _planet.Population > 0
                ? "✓  КОЛОНИЯ ОСНОВАНА"
                : "▶  ОСНОВАТЬ КОЛОНИЮ";

            bool canTerra = surveyed && owned && _planet.CanTerraform && _planet.HabitabilityPercent < 70;
            _btnTerra.interactable = canTerra;
            _btnTerraTxt.text = "▶  НАЧАТЬ ТЕРРАФОРМИРОВАНИЕ";

            // Кнопка «Осмотреть» — всегда активна
            if (_btnExpand != null) _btnExpand.interactable = true;
        }

        private void OnMine()
        {
            if (_planet == null) return;
            if (FleetManager.Instance != null && FleetManager.Instance.BuildMiningStationOnPlanet(_planet))
            {
                Refresh();
            }
        }

        private void OnColony()
        {
            if (_planet != null && _planet.TryFoundColony())
                Refresh();
        }

        private void OnTerra()
        {
            if (_planet != null && _planet.TryStartTerraform())
            {
                BindHolo();
                Refresh();
            }
        }

        private void OnExpand()
        {
            if (_planet == null) return;
            PlanetFocusOverlay.Instance?.Open(_planet, _system);
        }

        private void Build()
        {
            if (_host == null) return;

            _root = new GameObject("PlanetOverviewModal");
            _root.transform.SetParent(_host.transform, false);

            var rt = _root.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 0.5f);
            rt.anchorMax = new Vector2(0f, 0.5f);
            rt.pivot = new Vector2(0f, 0.5f);
            rt.anchoredPosition = new Vector2(24, -8);
            rt.sizeDelta = new Vector2(960, 620);

            _group = _root.AddComponent<CanvasGroup>();
            _root.AddComponent<Image>().color = UIManager.DS.BgDeep;
            LG.Glass(_root).SetRim(new Color(0.45f, 0.95f, 0.90f, 0.42f));
            LG.Motion(_root, LGAppear.Kind.SlideLeft);

            // Header
            var header = new GameObject("Header");
            header.transform.SetParent(_root.transform, false);
            var hRt = header.AddComponent<RectTransform>();
            hRt.anchorMin = new Vector2(0, 1);
            hRt.anchorMax = new Vector2(1, 1);
            hRt.pivot = new Vector2(0.5f, 1);
            hRt.sizeDelta = new Vector2(0, 64);
            header.AddComponent<Image>().color = UIManager.DS.BgHeader;
            LG.Header(header);

            _title = MakeText(header.transform, "ПЛАНЕТА", 18, FontStyle.Bold,
                UIManager.DS.TextPrimary, TextAnchor.MiddleLeft);
            _title.rectTransform.anchorMin = new Vector2(0, 0.38f);
            _title.rectTransform.anchorMax = new Vector2(1, 1);
            _title.rectTransform.offsetMin = new Vector2(18, 0);
            _title.rectTransform.offsetMax = new Vector2(-48, 0);

            _subtitle = MakeText(header.transform, "", 11, FontStyle.Bold,
                UIManager.DS.NeonCyan, TextAnchor.UpperLeft);
            _subtitle.rectTransform.anchorMin = new Vector2(0, 0);
            _subtitle.rectTransform.anchorMax = new Vector2(1, 0.42f);
            _subtitle.rectTransform.offsetMin = new Vector2(18, 4);
            _subtitle.rectTransform.offsetMax = new Vector2(-48, 0);

            var close = MakeBtn(_root.transform, "✕", new Vector2(30, 30),
                new Color(0.16f, 0.20f, 0.24f), Close);
            LG.Button(close, new Color(1f, 0.45f, 0.48f, 0.55f), 15f);
            var cRt = close.GetComponent<RectTransform>();
            cRt.anchorMin = cRt.anchorMax = new Vector2(1, 1);
            cRt.pivot = new Vector2(1, 1);
            cRt.anchoredPosition = new Vector2(-16, -17);

            // Visor с голограммой
            var visor = new GameObject("Visor");
            visor.transform.SetParent(_root.transform, false);
            var vRt = visor.AddComponent<RectTransform>();
            vRt.anchorMin = new Vector2(0.01f, 0.16f);
            vRt.anchorMax = new Vector2(0.52f, 0.88f);
            vRt.offsetMin = vRt.offsetMax = Vector2.zero;
            visor.AddComponent<Image>().color = UIManager.DS.BgVisor;

            var holoGo = new GameObject("Holo");
            holoGo.transform.SetParent(visor.transform, false);
            var hr = holoGo.AddComponent<RectTransform>();
            hr.anchorMin = Vector2.zero; hr.anchorMax = Vector2.one;
            hr.offsetMin = hr.offsetMax = Vector2.zero;
            _holo = holoGo.AddComponent<RawImage>();
            _holo.color = Color.white;
            holoGo.AddComponent<HoloDragCatcher>();

            var gradGo = new GameObject("Gradient");
            gradGo.transform.SetParent(visor.transform, false);
            var gRt = gradGo.AddComponent<RectTransform>();
            gRt.anchorMin = Vector2.zero; gRt.anchorMax = Vector2.one;
            gRt.offsetMin = gRt.offsetMax = Vector2.zero;
            var gImg = gradGo.AddComponent<Image>();
            gImg.raycastTarget = false;
            gImg.sprite = MakeGradientSprite();
            gImg.type = Image.Type.Simple;
            gImg.color = Color.white;

            // Голограмма обрезается по скруглению стеклянного "визора"
            LG.Platter(visor, 16f);
            LG.WrapInRoundedMask(visor.transform, 16f, 0f, holoGo.transform, gradGo.transform);

            _grid = MakeText(visor.transform, "", 10, FontStyle.Bold,
                UIManager.DS.NeonTeal, TextAnchor.LowerLeft);
            _grid.rectTransform.anchorMin = new Vector2(0, 0);
            _grid.rectTransform.anchorMax = new Vector2(1, 0.22f);
            _grid.rectTransform.offsetMin = new Vector2(12, 8);
            _grid.lineSpacing = 1.15f;

            // Info панель
            var info = new GameObject("Info");
            info.transform.SetParent(_root.transform, false);
            var iRt = info.AddComponent<RectTransform>();
            iRt.anchorMin = new Vector2(0.53f, 0.28f);
            iRt.anchorMax = new Vector2(0.985f, 0.88f);
            iRt.offsetMin = iRt.offsetMax = Vector2.zero;
            info.AddComponent<Image>().color = UIManager.DS.BgSlot;

            _body = MakeText(info.transform, "", 13, FontStyle.Normal,
                UIManager.DS.TextPrimary, TextAnchor.UpperLeft);
            _body.rectTransform.anchorMin = Vector2.zero;
            _body.rectTransform.anchorMax = Vector2.one;
            _body.rectTransform.offsetMin = new Vector2(14, 10);
            _body.rectTransform.offsetMax = new Vector2(-12, -10);
            _body.horizontalOverflow = HorizontalWrapMode.Wrap;
            _body.verticalOverflow = VerticalWrapMode.Overflow;
            _body.lineSpacing = 1.25f;

            // === КНОПКА «ОСМОТРЕТЬ» — самая верхняя ===
            _btnExpand = MakeBtn(_root.transform, "◇  ОСМОТРЕТЬ ПЛАНЕТУ",
                new Vector2(430, 32), UIManager.DS.BtnPrimary, OnExpand).GetComponent<Button>();
            PlaceBtn(_btnExpand.GetComponent<RectTransform>(), 96);

            // Кнопки действий
            _btnMine = MakeBtn(_root.transform, "▶  ПОСТРОИТЬ ДОБЫВАЮЩИЙ КОМПЛЕКС",
                new Vector2(430, 32), UIManager.DS.BtnSuccess, OnMine).GetComponent<Button>();
            PlaceBtn(_btnMine.GetComponent<RectTransform>(), 52);
            _btnMineTxt = _btnMine.GetComponentInChildren<Text>();

            _btnColony = MakeBtn(_root.transform, "▶  ОСНОВАТЬ КОЛОНИЮ",
                new Vector2(430, 32), UIManager.DS.BtnPrimary, OnColony).GetComponent<Button>();
            PlaceBtn(_btnColony.GetComponent<RectTransform>(), 16);
            _btnColonyTxt = _btnColony.GetComponentInChildren<Text>();

            _btnTerra = MakeBtn(_root.transform, "▶  НАЧАТЬ ТЕРРАФОРМИРОВАНИЕ",
                new Vector2(430, 32), UIManager.DS.BtnNeutral, OnTerra).GetComponent<Button>();
            PlaceBtn(_btnTerra.GetComponent<RectTransform>(), -20);
            _btnTerraTxt = _btnTerra.GetComponentInChildren<Text>();

            LG.Skin(_root.transform);
            _root.SetActive(false);
        }

        private static void PlaceBtn(RectTransform rt, float y)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0.76f, 0.12f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(0, y);
        }

        private Text MakeText(Transform parent, string val, int size, FontStyle st, Color col, TextAnchor a)
        {
            var go = new GameObject("Txt");
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<Text>();
            t.font = st == FontStyle.Bold ? GameFont.Bold : GameFont.Regular; t.text = val; t.fontSize = size; t.fontStyle = st; t.color = col;
            t.alignment = a; t.raycastTarget = false; t.supportRichText = true;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            return t;
        }

        private GameObject MakeBtn(Transform parent, string label, Vector2 size, Color bg, System.Action click)
        {
            var go = new GameObject("Btn");
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.sizeDelta = size;
            go.AddComponent<Image>().color = bg;
            var b = go.AddComponent<Button>();
            b.onClick.AddListener(() => click?.Invoke());
            LG.Button(go);
            var t = MakeText(go.transform, label, 10, FontStyle.Bold, Color.white, TextAnchor.MiddleCenter);
            t.rectTransform.anchorMin = Vector2.zero;
            t.rectTransform.anchorMax = Vector2.one;
            t.rectTransform.offsetMin = t.rectTransform.offsetMax = Vector2.zero;
            t.raycastTarget = false;
            return go;
        }

        private static Sprite MakeGradientSprite()
        {
            const int w = 64, h = 64;
            var tex = new Texture2D(w, h, TextureFormat.ARGB32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float fx = 1f - x / (float)(w - 1);
                float fy = 1f - y / (float)(h - 1);
                float a = Mathf.Clamp01(fx * 0.55f + fy * 0.62f);
                tex.SetPixel(x, y, new Color(0.012f, 0.040f, 0.052f, a * 0.92f));
            }
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f));
        }
    }
}