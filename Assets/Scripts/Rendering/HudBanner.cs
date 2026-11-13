using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using StellarisClone.Core;

namespace StellarisClone.Rendering
{
    /// <summary>
    /// Кино-баннер крупного события под диском времени: «ВОЙНА ОБЪЯВЛЕНА», «ОСАДА», «ОТКРЫТО ЧУДО ПРИРОДЫ»…
    /// Раскрывается от центра, держится ~4 с и тает; события копятся в очередь (по одному на экране).
    /// Клик по баннеру с системой — камера к ней. Мелкие события остаются тостами и в ленте.
    /// </summary>
    public sealed class HudBanner : MonoBehaviour
    {
        private struct Item { public string Title, Body; public LGIcon Icon; public Color Color; public int SystemId; }

        private static HudBanner s_instance;
        private readonly Queue<Item> _queue = new Queue<Item>();
        private RectTransform _root;
        private CanvasGroup _group;
        private Image _bg, _lineTop, _lineBottom, _icon;
        private Text _title, _body;
        private int _systemId = -1;
        private float _t = -1f;                 // время показа текущего баннера, < 0 — пусто
        private const float InTime = 0.35f, HoldTime = 4f, OutTime = 0.6f;
        private const int MaxQueue = 4;

        public static void Create(Transform canvas, float top)
        {
            if (s_instance != null) return;
            var go = new GameObject("[UI] HudBanner", typeof(RectTransform));
            go.transform.SetParent(canvas, false);
            var b = go.AddComponent<HudBanner>();
            b.Build(top);
            s_instance = b;
        }

        /// <summary>Показать баннер (или поставить в очередь). systemId ≥ 0 — клик ведёт камеру к системе.</summary>
        public static void Show(string title, string body, LGIcon icon, Color color, int systemId = -1)
        {
            if (s_instance == null || GameAudio.Suppressed) return;
            if (s_instance._queue.Count >= MaxQueue) return;
            s_instance._queue.Enqueue(new Item { Title = title, Body = body, Icon = icon, Color = color, SystemId = systemId });
        }

        private void OnDestroy() { if (s_instance == this) s_instance = null; }

        private void Build(float top)
        {
            _root = (RectTransform)transform;
            _root.At(new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -top), new Vector2(720f, 66f));
            _group = gameObject.AddComponent<CanvasGroup>();
            _group.alpha = 0f;
            _group.blocksRaycasts = false;
            LG.Ignore(gameObject);

            _bg = Img("Bg", HudArt.EdgeFade(0.2f), Color.black);
            _bg.rectTransform.Stretch();
            _bg.raycastTarget = true;
            var btn = _bg.gameObject.AddComponent<Button>();
            btn.transition = Selectable.Transition.None;
            btn.onClick.AddListener(OnClick);

            _lineTop = Img("LineTop", HudArt.EdgeFade(0.3f), Color.white);
            _lineTop.rectTransform.At(new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(560f, 1.5f));
            _lineBottom = Img("LineBottom", HudArt.EdgeFade(0.3f), Color.white);
            _lineBottom.rectTransform.At(new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), Vector2.zero, new Vector2(560f, 1.5f));

            _icon = Img("Icon", null, Color.white);
            _title = LGBuild.Label(_root, "", 21, Color.white, TextAnchor.LowerLeft);
            _title.font = GameFont.Display;
            _title.Track(3f);
            _body = LGBuild.Label(_root, "", 13, Color.white, TextAnchor.UpperLeft);
        }

        private Image Img(string name, Sprite sp, Color c)
        {
            var im = LGBuild.Panel(_root, name, c);
            im.sprite = sp;
            return im;
        }

        private void OnClick()
        {
            if (_systemId < 0) return;
            var sys = EmpireStats.GetSystem(_systemId);
            if (sys != null) UIManager.Instance?.FocusSystem(sys);
            _t = Mathf.Max(_t, InTime + HoldTime);   // клик — сразу гасим
        }

        private void Present(Item it)
        {
            _systemId = it.SystemId;
            var c = it.Color;
            _bg.color = new Color(c.r * 0.22f, c.g * 0.22f, c.b * 0.22f, 0.9f);
            _lineTop.color = _lineBottom.color = new Color(c.r, c.g, c.b, 0.95f);
            _title.text = it.Title.ToUpper();
            _title.color = Color.Lerp(c, Color.white, 0.75f);
            _body.text = it.Body ?? "";
            _body.color = Color.Lerp(c, Color.white, 0.55f);

            // Раскладка по центру: значок | заголовок над строкой текста
            float tw = Mathf.Max(_title.preferredWidth, Mathf.Min(_body.preferredWidth, 520f));
            float iconW = 34f, gap = 16f;
            float total = iconW + gap + tw;
            float x0 = -total * 0.5f;
            _icon.sprite = LGIcons.Get(it.Icon);
            _icon.preserveAspect = true;
            _icon.color = c;
            _icon.rectTransform.At(new Vector2(0.5f, 0.5f), new Vector2(0f, 0.5f), new Vector2(x0, 0f), new Vector2(iconW, iconW));
            _title.rectTransform.At(new Vector2(0.5f, 0.5f), new Vector2(0f, 0f), new Vector2(x0 + iconW + gap, 1f), new Vector2(tw + 20f, 28f));
            _body.rectTransform.At(new Vector2(0.5f, 0.5f), new Vector2(0f, 1f), new Vector2(x0 + iconW + gap, -1f), new Vector2(520f, 20f));
            _root.sizeDelta = new Vector2(Mathf.Max(560f, total + 200f), 66f);
            _t = 0f;
            _group.blocksRaycasts = _systemId >= 0;
        }

        private void Update()
        {
            if (_t < 0f)
            {
                if (_queue.Count == 0) return;
                Present(_queue.Dequeue());
            }
            _t += Time.unscaledDeltaTime;
            float a, sx;
            if (_t < InTime) { float k = LGEase.OutCubic(_t / InTime); a = k; sx = Mathf.Lerp(0.55f, 1f, k); }
            else if (_t < InTime + HoldTime) { a = 1f; sx = 1f; }
            else
            {
                float k = Mathf.Clamp01((_t - InTime - HoldTime) / OutTime);
                a = 1f - k; sx = 1f + 0.04f * k;
                if (k >= 1f) { _t = -1f; _group.blocksRaycasts = false; }
            }
            _group.alpha = a;
            _root.localScale = new Vector3(sx, 1f, 1f);
        }
    }
}
