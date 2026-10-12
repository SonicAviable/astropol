using UnityEngine;
using UnityEngine.UI;
using StellarisClone.Core;

namespace StellarisClone.Rendering
{
    /// <summary>
    /// Карточка планеты при наведении на её шарик на карте галактики — чтобы оценить мир, не заходя в систему.
    ///   • шапка: шарик планеты, название, класс и хозяин; у колонии — жители / жильё;
    ///   • характеристики короткими сегментными шкалами: размер, пригодность, титан, гелий-3;
    ///   • состояние одной-двумя строками: районы и стройка, комплекс, можно ли колонизировать;
    ///   • подсказка, что откроет клик.
    /// Держится рядом с планетой и не вылезает за край экрана.
    /// </summary>
    public class PlanetHoverCard : MonoBehaviour
    {
        private static PlanetHoverCard s_instance;

        private const float W = 300f, Segs = 5;
        private RectTransform _canvasRt, _card;
        private CanvasGroup _group;
        private Image _sphere, _popIcon;
        private Text _name, _sub, _pop, _state, _hint;
        private Image _popBg;
        private Row[] _rows;
        private PlanetData _planet;
        private int _seed;
        private float _alpha, _targetAlpha, _refresh;
        private Vector2 _screen;

        private class Row
        {
            public Text Value;
            public Image[] Segs;
        }

        private static readonly Color CMinerals = new Color(0.20f, 0.90f, 0.80f);
        private static readonly Color CEnergy = new Color(0.95f, 0.78f, 0.28f);
        private static readonly Color CSegOff = new Color(1f, 1f, 1f, 0.10f);

        public static void Show(PlanetData planet, int spriteSeed, Vector2 screenPos)
        {
            if (planet == null) return;
            if (s_instance == null) s_instance = new GameObject("[UI] PlanetHoverCard").AddComponent<PlanetHoverCard>();
            s_instance.Bind(planet, spriteSeed, screenPos);
        }

        public static void Hide()
        {
            if (s_instance != null) s_instance._targetAlpha = 0f;
        }

        private void Awake()
        {
            DontDestroyOnLoad(gameObject);
            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.overrideSorting = true;
            canvas.sortingOrder = 3400;   // под обычными подсказками интерфейса
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            gameObject.AddComponent<UIScaleTarget>();
            _canvasRt = (RectTransform)transform;
            Build();
        }

        private void Build()
        {
            var card = LGBuild.Panel(transform, "Card", UIManager.DS.BgDeep);
            _card = card.rectTransform;
            _card.anchorMin = _card.anchorMax = Vector2.zero;
            _card.pivot = new Vector2(0f, 0.5f);
            _card.sizeDelta = new Vector2(W, 262f);
            LG.Glass(card.gameObject, 16f).SetRim(new Color(0.45f, 0.95f, 0.90f, 0.40f));
            _group = card.gameObject.AddComponent<CanvasGroup>();
            _group.blocksRaycasts = false;
            _group.interactable = false;
            _group.alpha = 0f;

            // Шапка
            var sph = new GameObject("Sphere").AddComponent<Image>();
            sph.transform.SetParent(_card, false);
            sph.raycastTarget = false;
            sph.preserveAspect = true;
            sph.rectTransform.At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(14, -12), new Vector2(46, 46));
            LG.Ignore(sph.gameObject);
            _sphere = sph;

            _name = LGBuild.Label(_card, "", 16, UIManager.DS.TextPrimary, TextAnchor.UpperLeft, bold: true);
            _name.rectTransform.At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(70, -13), new Vector2(134, 22));
            _sub = LGBuild.Label(_card, "", 11, UIManager.DS.TextMuted, TextAnchor.UpperLeft);
            _sub.rectTransform.At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(70, -36), new Vector2(W - 84, 18));

            _popBg = LGBuild.Panel(_card, "Pop", UIManager.DS.BgSlot);
            _popBg.rectTransform.At(new Vector2(1, 1), new Vector2(1, 1), new Vector2(-12, -12), new Vector2(76, 24));
            LG.Chip(_popBg.gameObject, new Color(0.45f, 0.95f, 0.90f, 0.45f));
            _popIcon = LGIcons.Create(_popBg.transform, LGIcon.Population, 14, UIManager.DS.NeonCyan);
            _popIcon.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(8, 0), new Vector2(14, 14));
            _pop = LGBuild.Label(_popBg.transform, "", 12, UIManager.DS.TextPrimary, TextAnchor.MiddleLeft, bold: true);
            _pop.rectTransform.Stretch(26, 0, 6, 0);

            // Характеристики
            var head = LGBuild.Label(_card, "ХАРАКТЕРИСТИКИ", 10, UIManager.DS.Gold, TextAnchor.UpperLeft, bold: true);
            head.rectTransform.At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(14, -70), new Vector2(200, 14));
            _rows = new Row[4];
            string[] labels = { "Размер", "Пригодность", "Титан", "Гелий-3" };
            for (int i = 0; i < _rows.Length; i++) _rows[i] = MakeRow(labels[i], -88 - i * 22);

            var sep = LGBuild.Panel(_card, "Sep", new Color(0.45f, 0.95f, 0.90f, 0.22f));
            sep.rectTransform.At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(14, -182), new Vector2(W - 28, 1));
            LG.Ignore(sep.gameObject);

            _state = LGBuild.Label(_card, "", 11, UIManager.DS.TextPrimary, TextAnchor.UpperLeft, wrap: true);
            _state.rectTransform.At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(14, -190), new Vector2(W - 28, 40));
            _state.lineSpacing = 1.15f;

            _hint = LGBuild.Label(_card, "", 10, UIManager.DS.NeonCyan, TextAnchor.LowerLeft, bold: true);
            _hint.rectTransform.Stretch(14, 10, 14, 0);

            LG.Skin(transform);
        }

        private Row MakeRow(string label, float y)
        {
            var row = new Row { Segs = new Image[(int)Segs] };
            var l = LGBuild.Label(_card, label, 11, UIManager.DS.TextMuted, TextAnchor.MiddleLeft);
            l.rectTransform.At(new Vector2(0, 1), new Vector2(0, 0.5f), new Vector2(14, y), new Vector2(96, 18));
            row.Value = LGBuild.Label(_card, "", 11, UIManager.DS.TextPrimary, TextAnchor.MiddleRight, bold: true);
            row.Value.rectTransform.At(new Vector2(0, 1), new Vector2(1, 0.5f), new Vector2(W - 112, y), new Vector2(100, 18));
            for (int i = 0; i < row.Segs.Length; i++)
            {
                var s = LGBuild.Panel(_card, "Seg", CSegOff);
                s.rectTransform.At(new Vector2(0, 1), new Vector2(0, 0.5f), new Vector2(W - 104 + i * 18, y), new Vector2(15, 5));
                LG.Ignore(s.gameObject);
                row.Segs[i] = s;
            }
            return row;
        }

        private void Bind(PlanetData planet, int seed, Vector2 screen)
        {
            _screen = screen;
            _targetAlpha = 1f;
            if (_planet == planet && _seed == seed && _refresh > 0f) return;
            _planet = planet;
            _seed = seed;
            _refresh = 0.5f;
            _sphere.sprite = GalaxyView.PlanetIcon(planet.Type, seed);
            Fill();
        }

        private void Fill()
        {
            var p = _planet;
            var sys = p.ParentSystem;
            int owner = p.OwnerId;
            string m = LGBuild.Hex(UIManager.DS.TextMuted);
            _name.text = PlanetNames.Title(p.Name);
            string ownerName = owner == 0 ? "ваша система" : owner > 0 ? AIEmpireManager.NameOf(owner) : "ничья";
            Color oc = owner >= 0 ? FleetIndicator.OwnerColor(owner) : UIManager.DS.TextMuted;
            _sub.text = $"{p.ClassDisplayName}  ·  <color={LGBuild.Hex(oc)}>{ownerName}</color>";

            bool colony = p.Population > 0;
            _popBg.gameObject.SetActive(colony);
            if (colony) _pop.text = $"{p.Population}<color={m}>/{p.HousingCapacity}</color>";

            bool known = sys == null || sys.IsSurveyed || owner == 0;
            if (!known)
            {
                foreach (var r in _rows) SetRow(r, 0, "?", UIManager.DS.TextMuted);
                _state.text = $"<color={m}>Система не изучена — пошлите научный корабль</color>";
            }
            else
            {
                int size = p.MaxDistricts;
                int sizeSeg = size <= 5 ? 1 : size <= 6 ? 2 : size <= 7 ? 3 : size <= 9 ? 4 : 5;
                string sizeName = sizeSeg switch { 1 => "крошечная", 2 => "малая", 3 => "средняя", 4 => "большая", _ => "гигантская" };
                SetRow(_rows[0], sizeSeg, $"{sizeName} · {size}", UIManager.DS.NeonCyan);

                int hab = p.HabitabilityPercent;
                Color hc = hab >= 70 ? UIManager.DS.Green : hab >= 40 ? UIManager.DS.Gold : UIManager.DS.Red;
                SetRow(_rows[1], Mathf.Clamp(Mathf.RoundToInt(hab / 20f), hab > 0 ? 1 : 0, 5), $"{hab}%", hc);

                int mi = p.MineralDeposit, en = p.EnergyDeposit;
                SetRow(_rows[2], mi <= 0 ? 0 : Mathf.Clamp(Mathf.RoundToInt(mi * 5f / 7f), 1, 5), DepositName(mi, 7), CMinerals);
                SetRow(_rows[3], Mathf.Clamp(en, 0, 5), DepositName(en, 5), CEnergy);

                _state.text = StateText(p, m);
            }

            _hint.text = owner == 0 && colony ? "ЛКМ — управление колонией"
                       : owner == 0 ? "ЛКМ — обзор и освоение планеты"
                       : "ЛКМ — обзор планеты";
        }

        private static string DepositName(int v, int max)
        {
            if (v <= 0) return "нет";
            float k = v / (float)max;
            return (k < 0.4f ? "бедные" : k < 0.7f ? "умеренные" : "богатые") + $" · {v}";
        }

        private static string StateText(PlanetData p, string m)
        {
            var b = ConstructionManager.Instance;
            string gold = LGBuild.Hex(UIManager.DS.Gold), green = LGBuild.Hex(UIManager.DS.Green);
            string line1, line2 = "";
            if (p.Population > 0)
            {
                line1 = $"Районы  <b>{p.BuiltDistricts}</b><color={m}>/{p.MaxDistricts}</color>   ·   приоритет: {ColonyFocusInfo.Name(p.Focus).ToLower()}";
                var q = b != null ? b.QueueFor(p) : null;
                if (q != null && q.Count > 0)
                    line2 = $"<color={gold}>Стройка: {JobName(q[0])} · {Mathf.CeilToInt(q[0].DaysLeft)} дн." + (q.Count > 1 ? $" · ещё {q.Count - 1}" : "") + "</color>";
                else if (p.IdlePops > 0)
                    line2 = $"<color={gold}>Безработные: {p.IdlePops} — нужны районы</color>";
                else
                    line2 = $"<color={m}>Очередь стройки пуста</color>";
            }
            else if (p.IsColonizing) line1 = $"<color={gold}>Колонисты в пути</color>";
            else if (p.CanColonize) line1 = p.IsPlayerOwned ? $"<color={green}>Пригодна для колонии</color>" : $"<color={green}>Пригодна для жизни</color>";
            else line1 = $"<color={m}>Непригодна для жизни</color>";

            if (p.Population <= 0)
            {
                if (p.HasMiningStation) line2 = $"<color={green}>Добывающий комплекс работает</color>";
                else if (p.StationUnderConstruction) line2 = $"<color={gold}>Строится добывающий комплекс</color>";
                else if (p.MineralDeposit + p.EnergyDeposit > 0) line2 = $"<color={m}>Залежи не разрабатываются</color>";
            }
            return string.IsNullOrEmpty(line2) ? line1 : line1 + "\n" + line2;
        }

        private static string JobName(ConstructionJob j) => j.Kind switch
        {
            JobKind.District => DistrictInfo.Name(j.District).ToLower(),
            JobKind.MiningStation => "комплекс",
            JobKind.Colony => "колония",
            JobKind.Terraform => "терраформинг",
            _ => "заказ"
        };

        private static void SetRow(Row r, int lit, string value, Color c)
        {
            r.Value.text = value;
            r.Value.color = lit > 0 ? Color.Lerp(c, Color.white, 0.35f) : UIManager.DS.TextMuted;
            for (int i = 0; i < r.Segs.Length; i++) r.Segs[i].color = i < lit ? c : CSegOff;
        }

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;
            _alpha = Mathf.MoveTowards(_alpha, _targetAlpha, dt * (_targetAlpha > _alpha ? 9f : 6f));
            _group.alpha = _alpha;
            if (_alpha <= 0f) { _planet = null; return; }
            if (_planet == null) return;

            if ((_refresh -= dt) <= 0f) { _refresh = 0.5f; Fill(); }

            // Справа от планеты; у правого края — слева, по высоте — в пределах экрана
            RectTransformUtility.ScreenPointToLocalPointInRectangle(_canvasRt, _screen, null, out var local);
            Vector2 size = _canvasRt.rect.size;
            Vector2 pos = local + size * 0.5f;          // от левого нижнего угла
            float h = _card.sizeDelta.y;
            bool left = pos.x + 26f + W > size.x - 8f;
            _card.pivot = new Vector2(left ? 1f : 0f, 0.5f);
            pos.x += left ? -26f : 26f;
            pos.y = Mathf.Clamp(pos.y, h * 0.5f + 8f, size.y - h * 0.5f - 8f);
            _card.anchoredPosition = pos;
        }
    }
}
