using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using StellarisClone.Core;
using DS = StellarisClone.Rendering.UIManager.DS;
using Sfx = StellarisClone.Core.Audio.Sfx;

namespace StellarisClone.Rendering
{
    /// <summary>
    /// Плеер фоновой музыки: кнопка-динамик рядом со скоростью игры открывает стеклянную панель под датой —
    /// название и автор трека, время, «назад / пауза / вперёд», перемешивание, громкость и список треков.
    /// Громкость — та же, что в настройках (GameSettings.MusicVolume), и сохраняется так же.
    /// </summary>
    public class MusicPlayerPanel : MonoBehaviour
    {
        public static MusicPlayerPanel Instance { get; private set; }

        private const float W = 340f;
        private const float RowH = 24f;
        private const int MaxRows = 12;

        private RectTransform _root;
        private Text _title, _artist, _time;
        private RectTransform _progress, _volFill;
        private Image _playIcon, _shuffleIcon, _shuffleBg, _toggleIcon;
        private readonly System.Collections.Generic.List<(Image bg, Text label)> _rows = new System.Collections.Generic.List<(Image, Text)>();
        private bool _subscribed;
        private float _tick;

        public bool IsOpen => _root != null && _root.gameObject.activeSelf;

        /// <summary>Кнопка в панели времени (UIManager): динамик, по клику — панель.</summary>
        public static void CreateToggle(Transform timeBar, float x, Canvas canvas, float panelTop)
        {
            var host = new GameObject("[UI] MusicPlayer").AddComponent<MusicPlayerPanel>();
            host.Build(canvas, panelTop);

            var btn = LGBuild.Button(timeBar, "MusicBtn", DS.BgVisor, new Color(DS.NeonCyan.r, DS.NeonCyan.g, DS.NeonCyan.b, 0.55f), host.Toggle, null, null, 11, 15f);
            ((RectTransform)btn.transform).At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(x, 0), new Vector2(30, 30));
            host._toggleIcon = LGIcons.Create(btn.transform, LGIcon.Speaker, 15f, DS.TextMuted);
            host._toggleIcon.rectTransform.At(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(15, 15));
            host._toggleIcon.raycastTarget = false;
            TooltipHelper.Attach(btn.gameObject, Loc.T("<b>Музыка</b>\nТекущий трек, пауза, следующий, громкость  <color=#8AA2A8>(M)</color>"));
        }

        private void Awake() => Instance = this;

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (_subscribed && MusicManager.Instance != null) MusicManager.Instance.OnStateChanged -= Refresh;
            if (_root != null) Destroy(_root.gameObject);
        }

        public void Toggle()
        {
            if (_root == null) return;
            bool open = !_root.gameObject.activeSelf;
            _root.gameObject.SetActive(open);
            if (open) { _root.SetAsLastSibling(); RebuildRows(); Refresh(); }
            SFXManager.Play(Sfx.UiClick);
        }

        private void Update()
        {
            // Горячая клавиша M — открыть/закрыть плеер (не в полях ввода)
            if (Input.GetKeyDown(KeyCode.M) && !InputFocused()) Toggle();

            // Открытая панель — поверх остального HUD (значки оповещений и пр. создаются позже и иначе ложатся сверху)
            if (_root != null && _root.gameObject.activeSelf && _root.parent != null
                && _root.GetSiblingIndex() != _root.parent.childCount - 1)
                _root.SetAsLastSibling();

            var mm = MusicManager.Instance;
            if (mm != null && !_subscribed) { mm.OnStateChanged += Refresh; _subscribed = true; }
            if (_toggleIcon != null && mm != null)
                _toggleIcon.color = mm.IsPaused ? DS.TextMuted : DS.NeonCyan;

            if (!IsOpen) return;
            _tick -= Time.unscaledDeltaTime;
            if (_tick > 0f) return;
            _tick = 0.25f;
            RefreshTime();
        }

        private static bool InputFocused()
        {
            var es = EventSystem.current;
            var go = es != null ? es.currentSelectedGameObject : null;
            return go != null && go.GetComponent<InputField>() != null;
        }

        // ================================================================ Построение

        private void Build(Canvas canvas, float panelTop)
        {
            _root = LGBuild.Rect(canvas.transform, "MusicPlayerPanel");
            _root.anchorMin = _root.anchorMax = new Vector2(0.5f, 1f);
            _root.pivot = new Vector2(0.5f, 1f);
            _root.anchoredPosition = new Vector2(0f, panelTop);
            _root.sizeDelta = new Vector2(W, 140f);
            var bg = _root.gameObject.AddComponent<Image>();
            bg.color = DS.BgDeep;
            bg.raycastTarget = true;   // клики по панели не уходят в карту
            LG.Glass(_root.gameObject, 14f).SetRim(new Color(0.55f, 0.95f, 0.92f, 0.5f));
            LG.Motion(_root.gameObject, LGAppear.Kind.SlideDown).distance = 12f;

            // Значок, название, автор
            var tile = LGBuild.Panel(_root, "Tile", new Color(DS.NeonCyan.r * 0.16f, DS.NeonCyan.g * 0.16f, DS.NeonCyan.b * 0.16f, 0.95f));
            tile.rectTransform.At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(14, -14), new Vector2(40, 40));
            LG.Chip(tile.gameObject, new Color(DS.NeonCyan.r, DS.NeonCyan.g, DS.NeonCyan.b, 0.5f));
            var ic = LGIcons.Create(tile.transform, LGIcon.Speaker, 20f, DS.NeonCyan);
            ic.rectTransform.At(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(20, 20));

            _title = LGBuild.Label(_root, "—", 14, DS.TextPrimary, TextAnchor.UpperLeft, bold: true);
            _title.rectTransform.At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(64, -14), new Vector2(W - 78, 20));
            _title.horizontalOverflow = HorizontalWrapMode.Wrap;
            _title.verticalOverflow = VerticalWrapMode.Truncate;   // длинное название — в одну строку
            _artist = LGBuild.Label(_root, "", 11, DS.TextMuted, TextAnchor.UpperLeft);
            _artist.rectTransform.At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(64, -36), new Vector2(W - 78, 16));

            // Время трека
            var bar = LGBuild.Rect(_root, "Progress");
            bar.At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(14, -64), new Vector2(W - 28 - 80, 10));
            _progress = LGBuild.Bar(bar, DS.NeonCyan, 0f, 4f);
            _time = LGBuild.Label(_root, "0:00 / 0:00", 11, DS.TextMuted, TextAnchor.MiddleRight);
            _time.rectTransform.At(new Vector2(1, 1), new Vector2(1, 1), new Vector2(-14, -60), new Vector2(76, 18));

            // Кнопки управления
            float y = -96f;
            Btn(LGIcon.Back, new Vector2(14, y), () => MusicManager.Instance?.Previous(), Loc.T("<b>Предыдущий трек</b>\nВ середине трека — этот трек сначала"), out _);
            var play = Btn(LGIcon.Pause, new Vector2(54, y), () => MusicManager.Instance?.TogglePause(), Loc.T("<b>Пауза / продолжить</b>"), out _playIcon);
            LG.Button(play.gameObject, new Color(DS.NeonCyan.r, DS.NeonCyan.g, DS.NeonCyan.b, 0.8f), 16f);
            Btn(LGIcon.Back, new Vector2(94, y), () => MusicManager.Instance?.Next(), Loc.T("<b>Следующий трек</b>"), out var nextIcon);
            nextIcon.rectTransform.localRotation = Quaternion.Euler(0, 0, 180f);
            var shuffle = Btn(LGIcon.Dice, new Vector2(134, y), () =>
            {
                var mm = MusicManager.Instance;
                if (mm != null) mm.SetShuffle(!mm.Shuffle);
            }, Loc.T("<b>Перемешивание</b>\nВключено — треки идут в случайном порядке, выключено — по списку"), out _shuffleIcon);
            _shuffleBg = shuffle.GetComponent<Image>();

            // Громкость: тот же ползунок, что в настройках
            var volIc = LGIcons.Create(_root, LGIcon.Speaker, 13f, DS.TextMuted);
            volIc.rectTransform.At(new Vector2(0, 1), new Vector2(0.5f, 0.5f), new Vector2(190, y - 16), new Vector2(13, 13));
            var vol = LGBuild.Panel(_root, "Volume", new Color(0, 0, 0, 0.001f), raycast: true);
            vol.rectTransform.At(new Vector2(0, 1), new Vector2(0, 0.5f), new Vector2(202, y - 16), new Vector2(W - 202 - 16, 22));
            _volFill = LGBuild.Bar(vol.transform, DS.Gold, GameSettings.MusicVolume, 6f);
            vol.gameObject.AddComponent<VolumeDrag>().Init(this, vol.rectTransform);
            TooltipHelper.Attach(vol.gameObject, Loc.T("<b>Громкость музыки</b>\nТяните или нажмите. То же значение, что в настройках"));

            _root.gameObject.SetActive(false);
        }

        private Button Btn(LGIcon icon, Vector2 pos, System.Action act, string tip, out Image iconImg)
        {
            var b = LGBuild.Button(_root, "Btn_" + icon, DS.BgVisor, new Color(DS.NeonCyan.r, DS.NeonCyan.g, DS.NeonCyan.b, 0.5f), act, null, null, 11, 16f);
            ((RectTransform)b.transform).At(new Vector2(0, 1), new Vector2(0, 1), pos, new Vector2(32, 32));
            iconImg = LGIcons.Create(b.transform, icon, 14f, DS.TextPrimary);
            iconImg.rectTransform.At(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(14, 14));
            iconImg.raycastTarget = false;
            TooltipHelper.Attach(b.gameObject, tip);
            return b;
        }

        /// <summary>Список треков под кнопками — клик включает трек.</summary>
        private void RebuildRows()
        {
            var mm = MusicManager.Instance;
            int n = mm != null ? Mathf.Min(mm.TrackCount, MaxRows) : 0;
            if (_rows.Count == n) return;
            foreach (var r in _rows) if (r.bg != null) Destroy(r.bg.gameObject);
            _rows.Clear();
            float top = -140f;
            for (int i = 0; i < n; i++)
            {
                int idx = i;
                var b = LGBuild.Button(_root, "Track_" + i, new Color(0.02f, 0.05f, 0.07f, 0.9f), new Color(DS.NeonCyan.r, DS.NeonCyan.g, DS.NeonCyan.b, 0.35f),
                                       () => MusicManager.Instance?.PlayTrack(idx), null, null, 11, 8f);
                ((RectTransform)b.transform).At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(14, top - i * (RowH + 4f)), new Vector2(W - 28, RowH));
                var t = LGBuild.Label(b.transform, "", 11, DS.TextMuted, TextAnchor.MiddleLeft);
                t.rectTransform.Stretch(10, 0, 8, 0);
                _rows.Add((b.GetComponent<Image>(), t));
            }
            _root.sizeDelta = new Vector2(W, 140f + (n > 0 ? n * (RowH + 4f) + 8f : 0f));
        }

        // ================================================================ Обновление

        private void Refresh()
        {
            if (!IsOpen) return;
            var mm = MusicManager.Instance;
            if (mm == null || mm.TrackCount == 0)
            {
                _title.text = Loc.T("Нет треков");
                _artist.text = Loc.T("Положите MP3/OGG в Resources/Music");
                return;
            }
            int cur = mm.CurrentIndex;
            _title.text = cur >= 0 ? mm.TrackTitle(cur) : "—";
            string artist = cur >= 0 ? mm.TrackArtist(cur) : "";
            _artist.text = (artist.Length > 0 ? artist + "  ·  " : "") + Loc.F("трек {0} из {1}", cur + 1, mm.TrackCount) + (mm.IsPaused ? Loc.T("  ·  пауза") : "") + (mm.WarMood ? Loc.T("  ·  <color=#E07A6E>военное время</color>") : "");
            _playIcon.sprite = LGIcons.Get(mm.IsPaused ? LGIcon.Play : LGIcon.Pause);
            _shuffleIcon.color = mm.Shuffle ? DS.Gold : DS.TextMuted;
            if (_shuffleBg != null) _shuffleBg.color = mm.Shuffle ? new Color(0.16f, 0.13f, 0.05f, 1f) : DS.BgVisor;
            for (int i = 0; i < _rows.Count; i++)
            {
                bool on = i == cur;
                _rows[i].label.text = (on ? "▶  " : "     ") + mm.TrackTitle(i) + (mm.IsWarTrack(i) ? Loc.T("  <color=#E07A6E>· война</color>") : "");
                _rows[i].label.color = on ? DS.NeonCyan : DS.TextMuted;
                _rows[i].bg.color = on ? new Color(0.05f, 0.16f, 0.18f, 0.95f) : new Color(0.02f, 0.05f, 0.07f, 0.9f);
            }
            RefreshTime();
        }

        private void RefreshTime()
        {
            var mm = MusicManager.Instance;
            if (mm == null) return;
            float len = mm.CurrentLength, t = Mathf.Min(mm.CurrentTime, len);
            LGBuild.SetBar(_progress, len > 0f ? t / len : 0f);
            _time.text = $"{Fmt(t)} / {Fmt(len)}";
            LGBuild.SetBar(_volFill, GameSettings.MusicVolume);
        }

        private static string Fmt(float s)
        {
            int n = Mathf.Max(0, Mathf.FloorToInt(s));
            return $"{n / 60}:{n % 60:00}";
        }

        internal void OnVolume(float v)
        {
            GameSettings.SetMusic(v);
            LGBuild.SetBar(_volFill, v);
        }

        /// <summary>Ползунок громкости: нажатие или перетаскивание по полосе.</summary>
        private class VolumeDrag : MonoBehaviour, IPointerDownHandler, IDragHandler
        {
            private MusicPlayerPanel _owner;
            private RectTransform _rt;

            public void Init(MusicPlayerPanel owner, RectTransform rt) { _owner = owner; _rt = rt; }

            public void OnPointerDown(PointerEventData e) => Apply(e);
            public void OnDrag(PointerEventData e) => Apply(e);

            private void Apply(PointerEventData e)
            {
                if (_owner == null || _rt == null) return;
                if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_rt, e.position, e.pressEventCamera, out var local)) return;
                var r = _rt.rect;
                _owner.OnVolume(Mathf.Clamp01((local.x - r.xMin) / Mathf.Max(1f, r.width)));
            }
        }
    }
}
