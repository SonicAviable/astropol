using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using StellarisClone.Core;
using StellarisClone.Generation;

namespace StellarisClone.Rendering
{
    /// <summary>
    /// Панель выделенного флота внизу экрана: имя, тип, состояние, прочность, сила, маршрут
    /// и кнопки «Стоп», «Следующая цель», «Очистить очередь», «Камера», «Снять выделение».
    /// При мультивыделении — «Выделено: N флотов», состав, суммарная сила и средняя прочность.
    /// </summary>
    public class FleetCommandHUD : MonoBehaviour
    {
        public static FleetCommandHUD Instance { get; private set; }

        private GameObject _root;
        private Image _iconBg, _icon;
        private Text _title, _subtitle, _hpText, _route, _power;
        private RectTransform _hpBar;
        private Button _btnStop, _btnNext, _btnClear;
        private float _timer;
        private GalaxyGenerator _gen;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        private void Start()
        {
            var ui = UIManager.Instance;
            if (ui == null || ui.HudCanvas == null) return;
            Build(ui.HudCanvas.transform);
            FleetManager.OnSelectionChanged += Refresh;
        }

        private void OnDestroy() => FleetManager.OnSelectionChanged -= Refresh;

        private void Build(Transform hud)
        {
            var rt = LGBuild.Rect(hud, "[UI] FleetCommandHUD");
            rt.At(new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0, 18), new Vector2(640, 112));
            _root = rt.gameObject;
            var bg = _root.AddComponent<Image>();
            bg.color = UIManager.DS.BgDeep;
            bg.raycastTarget = true;
            LG.Glass(_root, 22f).SetRim(new Color(FleetIndicator.OwnColor.r, FleetIndicator.OwnColor.g, FleetIndicator.OwnColor.b, 0.45f));
            var mo = LG.Motion(_root, LGAppear.Kind.SlideUp);
            mo.distance = 24f;

            // Иконка типа
            _iconBg = LGBuild.Panel(rt, "IconBg", new Color(0.05f, 0.2f, 0.2f));
            _iconBg.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(16, 6), new Vector2(60, 60));
            LG.Platter(_iconBg.gameObject, 30f).FillMultiplier = 2.2f;
            _icon = LGIcons.Create(_iconBg.transform, LGIcon.Ship, 32, FleetIndicator.OwnColor);

            var body = LGBuild.Rect(rt, "Body");
            body.Stretch(90, 10, 236, 10);
            _title = LGBuild.Label(body, "", 15, UIManager.DS.TextPrimary, TextAnchor.UpperLeft, bold: true);
            _subtitle = LGBuild.Label(body, "", 11, UIManager.DS.TextMuted, TextAnchor.UpperLeft);
            _subtitle.rectTransform.offsetMax = new Vector2(0, -21);
            _power = LGBuild.Label(body, "", 12, UIManager.DS.Gold, TextAnchor.UpperRight, bold: true);

            var hpHost = LGBuild.Rect(body, "Hp");
            hpHost.anchorMin = new Vector2(0, 1);
            hpHost.anchorMax = new Vector2(1, 1);
            hpHost.offsetMin = new Vector2(0, -52);
            hpHost.offsetMax = new Vector2(0, -42);
            _hpBar = LGBuild.Bar(hpHost, UIManager.DS.Green, 1f, 7f);
            _hpText = LGBuild.Label(body, "", 10, UIManager.DS.TextPrimary, TextAnchor.UpperLeft);
            _hpText.rectTransform.offsetMax = new Vector2(0, -55);

            _route = LGBuild.Label(body, "", 10, UIManager.DS.TextMuted, TextAnchor.LowerLeft, wrap: true);

            // Кнопки
            var btns = LGBuild.Rect(rt, "Buttons");
            btns.anchorMin = new Vector2(1, 0);
            btns.anchorMax = new Vector2(1, 1);
            btns.pivot = new Vector2(1, 0.5f);
            btns.sizeDelta = new Vector2(220, 0);
            btns.anchoredPosition = new Vector2(-12, 0);

            _btnStop = IconButton(btns, 0, 0, LGIcon.Pause, "Стоп", "Сбросить маршрут и очередь приказов", () => ForEach(f => FleetManager.Instance.StopFleet(f)));
            _btnNext = IconButton(btns, 1, 0, LGIcon.Fast, "Следующая цель", "Пропустить текущий пункт и лететь к следующему из очереди", () => ForEach(f => FleetManager.Instance.SkipToNext(f)));
            _btnClear = IconButton(btns, 2, 0, LGIcon.Trash, "Очистить очередь", "Убрать пункты, добавленные через Shift+ПКМ (клавиша Backspace)", () => ForEach(f => FleetManager.Instance.ClearQueue(f)));
            IconButton(btns, 0, 1, LGIcon.Target, "Камера к флоту", "Плавно навести камеру на флот (клавиша F, двойной клик по значку)",
                () => FleetSelectionController.FocusCamera(FleetManager.Instance?.SelectedFleet));
            IconButton(btns, 1, 1, LGIcon.Fleet, "Выделить только главный", "Оставить в выделении один флот",
                () => FleetManager.Instance?.SelectFleet(FleetManager.Instance.SelectedFleet));
            IconButton(btns, 2, 1, LGIcon.Close, "Снять выделение", "Щелчок ЛКМ по пустому месту делает то же самое",
                () => FleetManager.Instance?.SetSelection(null));

            LG.Skin(_root.transform);
            _root.SetActive(false);
        }

        private Button IconButton(RectTransform parent, int col, int row, LGIcon icon, string title, string tip, System.Action onClick)
        {
            var b = LGBuild.Button(parent, title, UIManager.DS.BtnNeutral, new Color(0.5f, 0.95f, 0.9f, 0.55f),
                () => { onClick?.Invoke(); SFXManager.Play("ui_click", 0.9f, 1.05f); Refresh(); }, icon, null, 13, 14f);
            var rt = (RectTransform)b.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0, 0.5f);
            rt.pivot = new Vector2(0, 0.5f);
            rt.sizeDelta = new Vector2(66, 40);
            rt.anchoredPosition = new Vector2(col * 72f, row == 0 ? 22f : -22f);
            TooltipHelper.Attach(b.gameObject, $"<b>{title}</b>\n{tip}");
            return b;
        }

        private static void ForEach(System.Action<FleetView> act)
        {
            var fm = FleetManager.Instance;
            if (fm == null) return;
            foreach (var f in new List<FleetView>(fm.SelectedFleets)) act(f);
        }

        private void Update()
        {
            if (_root == null) return;
            _timer -= Time.unscaledDeltaTime;
            if (_timer > 0f) return;
            _timer = 0.25f;
            Refresh();
        }

        private void Refresh()
        {
            if (_root == null) return;
            var fm = FleetManager.Instance;
            bool inSystem = SystemViewManager.Instance != null && SystemViewManager.Instance.IsInSystemView;
            bool show = UIManager.IsGameStarted && fm != null && fm.SelectedFleets.Count > 0 && !inSystem;
            if (show != LG.IsVisible(_root))
            {
                if (show) LG.Show(_root);
                else LG.Hide(_root);
            }
            if (!show) return;
            if (_gen == null) _gen = FindAnyObjectByType<GalaxyGenerator>();

            var sel = fm.SelectedFleets;
            if (sel.Count == 1) ShowSingle(sel[0]);
            else ShowMany(sel);

            bool anyRoute = false, anyQueue = false;
            foreach (var f in sel)
            {
                if (f?.Data == null) continue;
                if (f.Data.Path.Count > 0 || f.Data.OrderQueue.Count > 0) anyRoute = true;
                if (f.Data.OrderQueue.Count > 0) anyQueue = true;
            }
            _btnStop.interactable = anyRoute;
            _btnNext.interactable = anyQueue;
            _btnClear.interactable = anyQueue;
        }

        private void ShowSingle(FleetView f)
        {
            var d = f.Data;
            if (d == null) return;
            _icon.sprite = LGIcons.Get(TypeIcon(d.Type));
            _icon.color = TypeColor(d.Type);
            _title.text = d.Name;
            // Лидер на борту: учёный или адмирал-флагман
            var leader = LeaderManager.Instance?.LeaderOfShip(d.Id);
            string lead = leader != null
                ? $"   ·   <color=#FFCC52>{LeaderManager.ClassName(leader.Class)} {leader.Name}, ур. {leader.Level}</color>"
                : "";
            _subtitle.text = $"{TypeName(d.Type)}   ·   {StateText(d)}{lead}";
            _power.text = d.Type == FleetType.Military ? $"Мощь {Mathf.RoundToInt(CombatMath.Power(d)):N0}" : "";
            SetHp(d.HullPoints + d.ArmorPoints, d.MaxHullPoints + d.MaxArmorPoints,
                  $"Корпус {d.HullPoints:0}/{d.MaxHullPoints:0} · броня {d.ArmorPoints:0} · щиты {d.ShieldPoints:0}");
            _route.text = RouteText(d);
        }

        private void ShowMany(IReadOnlyList<FleetView> sel)
        {
            int mil = 0, sci = 0, con = 0;
            float power = 0f, hp = 0f, hpMax = 0f;
            foreach (var f in sel)
            {
                var d = f?.Data;
                if (d == null) continue;
                if (d.Type == FleetType.Military) { mil++; power += CombatMath.Power(d); }
                else if (d.Type == FleetType.Science) sci++;
                else con++;
                hp += d.HullPoints + d.ArmorPoints;
                hpMax += d.MaxHullPoints + d.MaxArmorPoints;
            }
            _icon.sprite = LGIcons.Get(LGIcon.Fleet);
            _icon.color = FleetIndicator.OwnColor;
            _title.text = $"Выделено: {sel.Count} {FleetWord(sel.Count)}";
            var parts = new List<string>();
            if (mil > 0) parts.Add($"военных {mil}");
            if (sci > 0) parts.Add($"научных {sci}");
            if (con > 0) parts.Add($"строительных {con}");
            _subtitle.text = string.Join("  ·  ", parts) + "   ·   ПКМ — приказ всем";
            _power.text = mil > 0 ? $"Мощь {Mathf.RoundToInt(power):N0}" : "";
            SetHp(hp, hpMax, $"Средняя прочность {(hpMax > 0 ? hp / hpMax * 100f : 100f):0}%");

            float longest = 0f;
            foreach (var f in sel) if (f?.Data != null) longest = Mathf.Max(longest, FleetRoute.TotalDays(f.Data, _gen));
            _route.text = longest > 0f ? $"Последний прибудет через ~{FleetRoute.FormatDays(longest)}" : "Все флоты стоят на орбитах";
        }

        private void SetHp(float hp, float max, string text)
        {
            float frac = max > 0f ? Mathf.Clamp01(hp / max) : 1f;
            Color c = frac > 0.6f ? UIManager.DS.Green : frac > 0.3f ? UIManager.DS.Gold : UIManager.DS.Red;
            LGBuild.SetBar(_hpBar, frac, c);
            _hpText.text = text;
        }

        private string RouteText(FleetData d)
        {
            if (_gen == null) return "";
            var nodes = FleetRoute.Build(d, _gen);
            if (nodes.Count == 0)
            {
                var fm = FleetManager.Instance;
                if (fm != null && fm.NeedsRepair(d))
                {
                    float r = fm.RepairRateAt(d.CurrentSystemId, d.OwnerId);
                    return r > 0f ? $"Ремонт: {r * 100f:0}% в день" : "Повреждён — ремонт только на своей территории";
                }
                return $"На орбите: {_gen.Systems[d.CurrentSystemId].Name}   ·   ПКМ по системе — лететь, Shift+ПКМ — в очередь";
            }
            var sb = new StringBuilder("Маршрут: ");
            int shown = 0;
            foreach (var nd in nodes)
            {
                if (nd.OrderIndex == 0) continue;
                if (shown++ > 0) sb.Append(" → ");
                if (shown > 4) { sb.Append("…"); break; }
                sb.Append(_gen.Systems[nd.SystemId].Name);
            }
            sb.Append($"   ·   ~{FleetRoute.FormatDays(nodes[nodes.Count - 1].Days)}");
            if (d.OrderQueue.Count > 0) sb.Append($"   ·   в очереди: {d.OrderQueue.Count}");
            return sb.ToString();
        }

        private string StateText(FleetData d)
        {
            if (d.InCombat) return "<color=#FF6060>В бою</color>";
            switch (d.State)
            {
                case FleetState.InHyperlane:
                    return $"Прыжок → {(_gen != null && d.TargetSystemId >= 0 ? _gen.Systems[d.TargetSystemId].Name : "?")}";
                case FleetState.Surveying: return $"Разведка · {Mathf.Max(0, d.DaysRemainingSurvey):0} дн.";
                case FleetState.Constructing: return $"Монтаж форпоста · {Mathf.Max(0, d.DaysRemainingConstruction):0} дн.";
            }
            return d.Path.Count > 0 ? "Готовится к прыжку" : "На орбите";
        }

        private static string FleetWord(int n)
        {
            int m10 = n % 10, m100 = n % 100;
            if (m10 == 1 && m100 != 11) return "флот";
            if (m10 >= 2 && m10 <= 4 && (m100 < 12 || m100 > 14)) return "флота";
            return "флотов";
        }

        private static string TypeName(FleetType t) => t switch
        {
            FleetType.Science => "Научный корабль",
            FleetType.Constructor => "Строительный корабль",
            _ => "Военный флот"
        };

        private static LGIcon TypeIcon(FleetType t) => t switch
        {
            FleetType.Science => LGIcon.Sensors,
            FleetType.Constructor => LGIcon.Construction,
            _ => LGIcon.Ship
        };

        private static Color TypeColor(FleetType t) => t switch
        {
            FleetType.Science => new Color(0.45f, 1f, 0.62f),
            FleetType.Constructor => new Color(1f, 0.82f, 0.36f),
            _ => FleetIndicator.OwnColor
        };
    }
}
