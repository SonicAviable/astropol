using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using StellarisClone.Core;

namespace StellarisClone.Rendering
{
    /// <summary>
    /// Карточка правителя в левом верхнем углу экрана (слева от верхней панели и ряда режимов карты,
    /// на их общую высоту) вместо кнопки «Обзор империи»:
    /// живой портрет (моргание, дыхание, помехи — LeaderPortraitView), рамка цвета фракции,
    /// которая медленно «дышит» и разгорается при наведении — вместе с огнями и узорами портрета. Клик открывает обзор империи.
    /// Пока фракция не выбрана (или у неё нет портрета) — значок глобуса.
    /// </summary>
    public sealed class RulerBadge : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        /// <summary>Высота = верхняя панель (44) + зазор (12) + ряд режимов карты (42).</summary>
        public const float Width = 150f, Height = 98f;

        private LiquidGlassEffect _fx;
        private RectTransform _portraitHost;
        private LeaderPortraitView _portrait;
        private Image _fallbackIcon, _edge;
        private Text _name;
        private string _key;
        private Color _accent = UIManager.DS.NeonCyan;
        private float _hover, _hoverTarget;

        public static RulerBadge Create(Transform canvas, Vector2 topLeft, System.Action onClick)
        {
            var rt = LGBuild.Rect(canvas, "RulerBadge");
            rt.At(new Vector2(0, 1), new Vector2(0, 1), topLeft, new Vector2(Width, Height));
            UIAnchors.Register(UIAnchors.Ruler, rt);

            var bg = rt.gameObject.AddComponent<Image>();
            bg.color = new Color(0.01f, 0.025f, 0.035f, 1f);
            bg.raycastTarget = true;
            var btn = rt.gameObject.AddComponent<Button>();
            btn.onClick.AddListener(() => onClick?.Invoke());

            var badge = rt.gameObject.AddComponent<RulerBadge>();
            badge._fx = LG.Button(rt.gameObject, new Color(0.3f, 0.9f, 0.9f, 0.6f), 16f);
            if (badge._fx != null) badge._fx.FillMultiplier = 2.6f;
            var motion = LG.Motion(rt.gameObject, LGAppear.Kind.SlideUp);
            motion.distance = 22f;
            motion.inDuration = 0.55f;
            badge.Build(rt);
            return badge;
        }

        private void Build(RectTransform rt)
        {
            var mask = LG.RoundedMask(rt, 15f, 2f);
            LG.Ignore(mask.gameObject);

            _portraitHost = LGBuild.Rect(mask, "Portrait");
            _portraitHost.Stretch();

            _fallbackIcon = LGIcons.Create(mask, LGIcon.Globe, 34, UIManager.DS.NeonCyan);
            _fallbackIcon.rectTransform.At(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 10), new Vector2(34, 34));

            // Снизу затемнение под подписи
            var shade = LGBuild.Panel(mask, "Shade", new Color(0.01f, 0.025f, 0.035f, 1f));
            shade.sprite = MenuArt.VerticalFade;
            shade.rectTransform.anchorMin = Vector2.zero;
            shade.rectTransform.anchorMax = new Vector2(1, 0.55f);
            shade.rectTransform.offsetMin = shade.rectTransform.offsetMax = Vector2.zero;

            _name = LGBuild.Label(mask, "", 10, Color.white, TextAnchor.LowerLeft, bold: true);
            _name.rectTransform.Stretch(9, 19, 6, 0);
            var caption = LGBuild.Label(mask, "ОБЗОР ИМПЕРИИ", 8, UIManager.DS.TextMuted, TextAnchor.LowerLeft, bold: true);
            caption.rectTransform.Stretch(9, 7, 6, 0);

            // Светящаяся кромка снизу цвета фракции
            _edge = LGBuild.Panel(mask, "Edge", Color.white);
            _edge.rectTransform.anchorMin = Vector2.zero;
            _edge.rectTransform.anchorMax = new Vector2(1, 0);
            _edge.rectTransform.pivot = new Vector2(0.5f, 0);
            _edge.rectTransform.sizeDelta = new Vector2(0, 2f);

            Refresh();
        }

        public void OnPointerEnter(PointerEventData e) => _hoverTarget = 1f;
        public void OnPointerExit(PointerEventData e) => _hoverTarget = 0f;

        private void Refresh()
        {
            var f = UIManager.Instance != null ? UIManager.Instance.SelectedFaction : null;
            var leader = LeaderPortraits.ForFaction(f);
            string key = leader?.Key ?? "";
            if (key == _key) return;
            _key = key;

            LGBuild.Clear(_portraitHost);
            // Свой правитель — без «сбоев канала связи»: на маленькой карточке они выглядят как полосы у края лица
            _portrait = LeaderPortraitView.Create(_portraitHost, leader, 1.75f, Vector4.zero, false);
            bool ok = _portrait != null;
            _fallbackIcon.gameObject.SetActive(!ok);

            _accent = leader != null ? leader.Accent : f != null ? f.EmpireColor : UIManager.DS.NeonCyan;
            _fallbackIcon.color = Color.Lerp(_accent, Color.white, 0.3f);
            _name.text = leader != null ? leader.Name : f != null ? f.Name : "";
            _name.color = Color.Lerp(_accent, Color.white, 0.55f);

            string tip = leader != null
                ? $"<b>{leader.Name}</b>\n{leader.Title}\n<i>«{leader.Quote}»</i>\n\n"
                : "";
            TooltipHelper.Attach(gameObject, tip + "<b>Обзор империи</b>\nРесурсы, колонии, флоты, лидеры и путь к победе.");
        }

        private void Update()
        {
            Refresh();                                           // фракция выбирается уже после постройки панели

            float dt = Time.unscaledDeltaTime;
            _hover = Mathf.MoveTowards(_hover, _hoverTarget, dt * 5f);
            float pulse = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 1.4f);
            float a = Mathf.Lerp(0.35f + 0.2f * pulse, 0.95f, _hover);
            _fx?.SetRim(new Color(_accent.r, _accent.g, _accent.b, a));
            _edge.color = new Color(_accent.r, _accent.g, _accent.b, Mathf.Lerp(0.45f + 0.3f * pulse, 1f, _hover));
            if (_portrait != null) _portrait.Glow = Mathf.SmoothStep(0f, 1f, _hover);   // огни и узоры портрета разгораются
        }
    }
}
