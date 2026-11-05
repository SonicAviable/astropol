using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using StellarisClone.Core;

namespace StellarisClone.Rendering
{
    /// <summary>
    /// Круглый портрет правителя в левом верхнем углу (голо-HUD): кольцо стабильности и шкала вокруг
    /// рисуются в UIManager.HoloHud, название империи — справа от портрета.
    /// Живой портрет (моргание, дыхание, помехи — LeaderPortraitView), рамка цвета фракции,
    /// которая медленно «дышит» и разгорается при наведении — вместе с огнями и узорами портрета. Клик открывает обзор империи.
    /// Пока фракция не выбрана (или у неё нет портрета) — значок глобуса.
    /// </summary>
    public sealed class RulerBadge : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        /// <summary>Диаметр круглого портрета.</summary>
        public const float Width = 92f, Height = 92f;

        private LiquidGlassEffect _fx;
        private RectTransform _portraitHost;
        private LeaderPortraitView _portrait;
        private Image _fallbackIcon, _emblem;
        private string _key;
        private Color _accent = UIManager.DS.NeonCyan;

        /// <summary>Цвет фракции правителя (кольца и название империи в HUD).</summary>
        public Color Accent => _accent;
        public static RulerBadge Instance { get; private set; }
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
            Instance = badge;
            badge._fx = LG.Button(rt.gameObject, new Color(0.3f, 0.9f, 0.9f, 0.6f), Width * 0.5f);
            if (badge._fx != null) badge._fx.FillMultiplier = 2.6f;
            var motion = LG.Motion(rt.gameObject, LGAppear.Kind.SlideUp);
            motion.distance = 22f;
            motion.inDuration = 0.55f;
            badge.Build(rt);
            return badge;
        }

        private void Build(RectTransform rt)
        {
            var mask = LG.RoundedMask(rt, Width * 0.5f - 2f, 2f);
            LG.Ignore(mask.gameObject);

            _portraitHost = LGBuild.Rect(mask, "Portrait");
            _portraitHost.Stretch();

            _fallbackIcon = LGIcons.Create(mask, LGIcon.Globe, 34, UIManager.DS.NeonCyan);
            _fallbackIcon.rectTransform.At(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(34, 34));

            // Герб фракции — маленький, у нижнего края круга (поверх маски, чтобы не обрезался)
            var em = LGBuild.Rect(rt, "Emblem");
            em.At(new Vector2(0.5f, 0), new Vector2(0.5f, 0.5f), new Vector2(0, 2), new Vector2(22, 22));
            _emblem = em.gameObject.AddComponent<Image>();
            _emblem.preserveAspect = true;
            _emblem.raycastTarget = false;
            _emblem.enabled = false;
            var es = em.gameObject.AddComponent<Shadow>();
            es.effectColor = new Color(0f, 0f, 0f, 0.7f);
            es.effectDistance = new Vector2(1f, -1f);
            LG.Ignore(em.gameObject);

            Refresh();
        }

        private void OnDestroy() { if (Instance == this) Instance = null; }

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
            var emblem = FactionEmblems.ForFaction(f);
            _emblem.sprite = emblem;
            _emblem.enabled = emblem != null;
            _emblem.color = Color.Lerp(_accent, Color.white, 0.25f);

            string tip = leader != null
                ? $"<b>{leader.Name}</b>\n{leader.Title}\n<i>«{leader.Quote}»</i>\n\n"
                : "";
            TooltipHelper.Attach(gameObject, tip + Loc.T("<b>Обзор империи</b>\nРесурсы, колонии, флоты, лидеры и путь к победе."));
        }

        private void Update()
        {
            Refresh();                                           // фракция выбирается уже после постройки панели

            float dt = Time.unscaledDeltaTime;
            _hover = Mathf.MoveTowards(_hover, _hoverTarget, dt * 5f);
            float pulse = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 1.4f);
            float a = Mathf.Lerp(0.35f + 0.2f * pulse, 0.95f, _hover);
            _fx?.SetRim(new Color(_accent.r, _accent.g, _accent.b, a));
            if (_portrait != null) _portrait.Glow = Mathf.SmoothStep(0f, 1f, _hover);   // огни и узоры портрета разгораются
        }
    }
}
