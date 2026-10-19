using UnityEngine;
using UnityEngine.UI;
using StellarisClone.Core;
using Sfx = StellarisClone.Core.Audio.Sfx;

namespace StellarisClone.Rendering
{
    /// <summary>
    /// Справка (F1 или H, пункт «Справка» в левом меню): все горячие клавиши по группам
    /// и короткая карта интерфейса — где что находится и что нажимать. Кнопка «Пройти обучение заново».
    /// </summary>
    public class HelpOverlay : MonoBehaviour
    {
        public static HelpOverlay Instance { get; private set; }
        public static bool IsOpenNow => Instance != null && Instance.IsOpen;
        public bool IsOpen => _root != null && LG.IsVisible(_root);

        private const float W = 1240f, H = 780f;
        private GameObject _root;

        private static Color Muted => UIManager.DS.TextMuted;
        private static Color Primary => UIManager.DS.TextPrimary;
        private static Color Cyan => UIManager.DS.NeonCyan;

        public static void Toggle()
        {
            if (Instance == null) new GameObject("[UI] HelpOverlay").AddComponent<HelpOverlay>();
            if (Instance.IsOpen) Instance.Close(); else Instance.Open();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        private void Update()
        {
            if (IsOpen && Input.GetKeyDown(KeyCode.Escape)) { UIEsc.Consume(); Close(); }
        }

        public void Open()
        {
            if (_root == null) Build();
            if (_root == null) return;
            LG.Show(_root);
            _root.transform.SetAsLastSibling();
            UIManager.Instance?.ShowModalDimPublic();
            SFXManager.Play(Sfx.UiTab, 0.6f);
        }

        public void Close()
        {
            if (_root == null) return;
            LG.Hide(_root);
            UIManager.Instance?.HideModalDimPublic();
            TooltipSystem.Hide();
        }

        private void Build()
        {
            var host = GameObject.Find("ModalCanvas");
            if (host == null) return;
            var rt = LGBuild.Rect(host.transform, "Help");
            rt.At(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(W, H));
            _root = rt.gameObject;
            _root.AddComponent<CanvasGroup>();
            var bg = _root.AddComponent<Image>();
            bg.color = new Color(0.014f, 0.030f, 0.044f, 0.97f);
            bg.raycastTarget = true;
            LG.Glass(_root).SetRim(new Color(0.45f, 0.95f, 0.90f, 0.40f));
            LG.Motion(_root, LGAppear.Kind.Pop).fromScale = 0.97f;

            var title = LGBuild.Label(rt, Loc.T("СПРАВКА"), 26, Color.white, TextAnchor.UpperLeft, bold: true);
            title.rectTransform.At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(32, -20), new Vector2(600, 36));
            title.Track(1.6f);
            var sub = LGBuild.Label(rt, Loc.T("Горячие клавиши и где что находится. Открыть снова — F1 или H."), 13, Muted, TextAnchor.UpperLeft);
            sub.rectTransform.At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(33, -58), new Vector2(900, 20));
            var close = LGBuild.Button(rt, "Close", UIManager.DS.BtnNeutral, new Color(1f, 1f, 1f, 0.25f), Close, LGIcon.Close, null, 12);
            ((RectTransform)close.transform).At(new Vector2(1, 1), new Vector2(1, 1), new Vector2(-20, -18), new Vector2(38, 38));
            TooltipHelper.Attach(close.gameObject, Loc.T("Закрыть  <color=#8AA2A8>(Esc / F1)</color>"));

            // ---- Слева: клавиши ----
            var keys = Panel(rt, "Keys", new Vector2(20, 20), new Vector2(W * 0.5f - 10f, H - 96f));
            float y = 14f;
            y = Group(keys, y, Loc.T("ОКНА"),
                ("I", Loc.T("Империя: системы, флоты, лидеры")),
                ("C", Loc.T("Колонии: все планеты одной таблицей")),
                ("L", Loc.T("Законы империи")),
                ("T", Loc.T("Технологии")),
                ("P", Loc.T("Дипломатия")),
                ("K", Loc.T("Шпионаж")),
                ("N", Loc.T("Конструктор судов")),
                ("V", Loc.T("Путь к победе")),
                ("G", Loc.T("Выделить все военные флоты")),
                ("M", Loc.T("Музыка")),
                ("Esc", Loc.T("Закрыть окно, выйти из системы, меню")));
            y = Group(keys, y + 8f, Loc.T("ВРЕМЯ И ИГРА"),
                (Loc.T("Пробел"), Loc.T("Пауза / продолжить")),
                ("1 · 2 · 3", Loc.T("Скорость времени")),
                ("F5 · F9", Loc.T("Быстрое сохранение / загрузка")));
            y = Group(keys, y + 8f, Loc.T("КАРТА И ФЛОТ"),
                ("WASD", Loc.T("Сдвиг карты (Shift — быстрее), колесо — приближение")),
                ("Q · E", Loc.T("Поворот камеры")),
                ("Home", Loc.T("К столице")),
                (Loc.T("ЛКМ · ПКМ"), Loc.T("Выбрать флот (Shift, рамка — несколько) · лететь (Shift — в очередь)")),
                (Loc.T("Средняя кн."), Loc.T("Зажать и тянуть — перетащить карту")),
                ("F", Loc.T("Камера к выбранному флоту")),
                ("Backspace", Loc.T("Отменить приказы флота")),
                ("B · Tab", Loc.T("На экране планеты: стройка · следующая колония")));

            // ---- Справа: где что находится ----
            var map = Panel(rt, "Map", new Vector2(W * 0.5f + 10f, 20), new Vector2(W * 0.5f - 30f, H - 96f));
            float my = 14f;
            var head = LGBuild.Label(map, Loc.T("ГДЕ ЧТО НАХОДИТСЯ"), 13, Cyan, TextAnchor.UpperLeft, bold: true);
            head.rectTransform.TopBand(my, 20, 18, 18);
            head.Track(1.6f);
            my += 30f;
            my = Place(map, my, LGIcon.Energy, Loc.T("Сверху — ресурсы"), Loc.T("Запасы и прирост за месяц. Наведите на значок — откуда берётся и куда уходит. Стабильность — клик открывает колонии."));
            my = Place(map, my, LGIcon.Warning, Loc.T("Под ресурсами — что требует внимания"), Loc.T("Значки-оповещения: свободный слот науки, безработица, угроза. Клик — сразу к делу, правый клик — скрыть."));
            my = Place(map, my, LGIcon.Menu, Loc.T("Слева — главные экраны"), Loc.T("Империя, колонии, законы, наука, дипломатия… У каждого пункта справа — его клавиша."));
            my = Place(map, my, LGIcon.Fleet, Loc.T("Справа — текущие дела"), Loc.T("Исследования, стройка, флоты, научные корабли и строители. Клик по строке — к этому объекту."));
            my = Place(map, my, LGIcon.Star, Loc.T("Внизу слева — мини-карта"), Loc.T("Клик — перейти. Кнопки вокруг — режимы карты: границы, ресурсы, коридоры, разведка."));
            my = Place(map, my, LGIcon.Info, Loc.T("Внизу справа — новости галактики"), Loc.T("Войны, захваты, открытия соседей. Клик по новости — камера к месту событий."));
            my = Place(map, my, LGIcon.Planet, Loc.T("Двойной клик по звезде — система"), Loc.T("Планеты, станции, стройка. Надпись сверху показывает, где вы; Esc — назад к галактике."));

            var tut = LGBuild.Button(map, "Tutorial", new Color(0.05f, 0.20f, 0.15f), new Color(0.36f, 0.96f, 0.68f, 0.8f), RestartTutorial,
                LGIcon.Play, Loc.T("ПРОЙТИ ОБУЧЕНИЕ ЗАНОВО"), 11);
            var tr = (RectTransform)tut.transform;
            tr.anchorMin = new Vector2(0, 0); tr.anchorMax = new Vector2(1, 0); tr.pivot = new Vector2(0.5f, 0);
            tr.offsetMin = new Vector2(18, 18); tr.offsetMax = new Vector2(-18, 60);
            TooltipHelper.Attach(tut.gameObject, Loc.T("Советник заново покажет главное: разведку, колонии, науку, флот"));

            _root.SetActive(false);
        }

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

        private static float Group(RectTransform host, float y, string title, params (string key, string what)[] rows)
        {
            var t = LGBuild.Label(host, title, 13, Cyan, TextAnchor.UpperLeft, bold: true);
            t.rectTransform.TopBand(y, 20, 18, 18);
            t.Track(1.6f);
            y += 26f;
            foreach (var (key, what) in rows)
            {
                var cap = LGBuild.Panel(host, "Key", new Color(1f, 1f, 1f, 0.06f));
                cap.rectTransform.anchorMin = cap.rectTransform.anchorMax = new Vector2(0, 1);
                cap.rectTransform.pivot = new Vector2(0, 1);
                cap.rectTransform.anchoredPosition = new Vector2(18, -y);
                cap.rectTransform.sizeDelta = new Vector2(104, 20);
                LG.Chip(cap.gameObject, new Color(1f, 1f, 1f, 0.22f), 4f);
                var kt = LGBuild.Label(cap.transform, key, 10, Color.white, TextAnchor.MiddleCenter, bold: true);
                kt.Track(1.0f);
                var w = LGBuild.Label(host, what, 12, Primary, TextAnchor.MiddleLeft);
                w.rectTransform.TopBand(y, 20, 134, 14);
                y += 24f;
            }
            return y;
        }

        private static float Place(RectTransform host, float y, LGIcon icon, string title, string text)
        {
            var ic = LGIcons.Create(host, icon, 18, Cyan);
            ic.rectTransform.anchorMin = ic.rectTransform.anchorMax = new Vector2(0, 1);
            ic.rectTransform.pivot = new Vector2(0, 1);
            ic.rectTransform.anchoredPosition = new Vector2(18, -y - 1f);
            var t = LGBuild.Label(host, title, 13, Color.white, TextAnchor.UpperLeft, bold: true);
            t.rectTransform.TopBand(y, 20, 46, 18);
            var d = LGBuild.Label(host, text, 12, Muted, TextAnchor.UpperLeft, wrap: true);
            d.rectTransform.TopBand(y + 20f, 34, 46, 18);
            return y + 20f + Mathf.Clamp(d.preferredHeight, 16f, 34f) + 12f;
        }

        private void RestartTutorial()
        {
            Close();
            TutorialManager.Instance?.BeginTutorial();
        }
    }
}
