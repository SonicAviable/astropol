using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using StellarisClone.Core;
using StellarisClone.Rendering;

namespace StellarisClone.Cards
{
    /// <summary>
    /// Карта на экране. Два вида: полная (рука, предпросмотр — арт сверху, имя, тип, текст, цена) и компактная (на столе —
    /// арт во всю карту, сила и броня по углам). Рамка по редкости, «фольга» с бегущим бликом у элиты, фракционных и героев,
    /// рубашка с эмблемой фракции. Живёт сама: наклон к курсору и подъём в руке, переворот, «удар» при выходе на стол,
    /// вспышка урона, трещины при тяжёлых повреждениях, свечение доступности.
    /// </summary>
    public sealed class CardView : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler,
                                   IBeginDragHandler, IDragHandler, IEndDragHandler, IPointerClickHandler
    {
        public BCard Card;
        public int Side;
        public int Uid = -1;
        public bool Compact;
        public string SideFaction;

        public RectTransform Rt;
        private RectTransform _content;
        private CanvasGroup _group;
        private CardAura _aura;
        private Image _body, _frame, _sheen, _hitFlash, _cracks, _back, _backPattern, _hpFill, _statusIcon;
        private RawImage _art, _backEmblem;
        private Text _name, _cost, _power, _armor, _kind, _text, _stars, _statusText;
        private RectTransform _powerBadge, _armorBadge, _sheenRt;
        private Color _frameColor;

        // Состояние анимаций
        private float _flip = 1f, _flipTarget = 1f;   // 1 — лицом, −1 — рубашкой (через 0 — ребро)
        private bool _faceUp = true;
        private float _hover, _punch, _shakeT, _hitA, _sheenT, _glowPulse;
        private Vector2 _tilt;
        private bool _hoverable, _hovered;
        public bool Interactive;
        public bool Playable;
        public bool Selected;
        public float GlowBoost;
        /// <summary>Наведение: на сколько поднять карту и увеличить (в руке — сильно, как в Hearthstone).</summary>
        public float HoverLift = 18f, HoverScale = 0.12f;
        /// <summary>Покачивание в руке (0..1): карта чуть плавает и поворачивается, будто её держат.</summary>
        public float IdleBob;
        /// <summary>«Дыхание» юнита на столе (0..1): едва заметный пульс масштаба.</summary>
        public float IdleBreathe;
        private Rect _artUv;
        private float _idlePhase;

        public Action<CardView> OnHoverIn, OnHoverOut, OnClick;
        public Action<CardView, PointerEventData> OnDragBegin, OnDragMove, OnDragEnd;

        public int ShownPower { get; private set; }
        public int ShownArmor { get; private set; }
        public int MaxArmor { get; private set; }

        // ==================== ПОСТРОЕНИЕ ====================

        public static CardView Create(Transform parent, BCard card, int side, string sideFaction, Vector2 size, bool compact)
        {
            var rt = LGBuild.Rect(parent, "Card_" + card.Def.Id);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = size;
            var v = rt.gameObject.AddComponent<CardView>();
            v.Rt = rt;
            v.Card = card;
            v.Side = side;
            v.SideFaction = sideFaction;
            v.Compact = compact;
            v.Build(size);
            return v;
        }

        private void Build(Vector2 size)
        {
            var d = Card.Def;
            _group = gameObject.AddComponent<CanvasGroup>();
            _frameColor = CardArt.Frame(d, Card.Stars, SideFaction);
            if (Compact) BuildCompact(size, d); else BuildFull(size, d);

            // Рубашка: тёмная броня с заклёпками, эмблема фракции, рама цвета фракции
            float k = size.y / 280f;
            _back = CardSkin.Sliced(_content, "Back", "panel_plates", Color.white, 0.32f * k * (Compact ? 1.4f : 1f));
            _back.rectTransform.Stretch();
            var bc = CardArt.Faction(SideFaction);
            _backPattern = Img(_back.rectTransform, "Pattern", CardSprites.BackPattern, new Color(bc.r, bc.g, bc.b, 0.22f));
            _backPattern.rectTransform.Stretch(8, 8, 8, 8);
            var emb = CardArt.Emblem(SideFaction);
            if (emb != null)
            {
                var halo = CardSkin.Simple(_back.rectTransform, "Halo", "glow", new Color(bc.r, bc.g, bc.b, 0.45f));
                float hs = Mathf.Min(size.x, size.y) * 1.0f;
                halo.rectTransform.At(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(hs, hs));
                _backEmblem = LGBuild.Rect(_back.rectTransform, "Emblem").gameObject.AddComponent<RawImage>();
                _backEmblem.texture = emb;
                _backEmblem.raycastTarget = false;
                float es = Mathf.Min(size.x, size.y) * 0.66f;
                _backEmblem.rectTransform.At(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(es, es));
            }
            var backRim = CardSkin.Sliced(_back.rectTransform, "Rim", "frame_card", Color.Lerp(bc, CardSkin.Bronze, 0.4f), (Compact ? 0.26f : 0.38f) * k * (Compact ? 280f / 178f : 1f));
            backRim.rectTransform.Stretch();
            _back.gameObject.SetActive(false);

            // Луч на всю карту ловит мышь
            var hit = LGBuild.Panel(Rt, "Hit", new Color(0, 0, 0, 0), raycast: true);
            hit.rectTransform.Stretch();

            ShownPower = d.Power;
            MaxArmor = ShownArmor = d.Armor;
            RefreshNumbers(d.Power + (Card.Stars >= 2 ? 1 : 0) + (Card.Stars >= 3 ? 1 : 0), d.Armor + (Card.Stars >= 1 ? 1 : 0) + (Card.Stars >= 3 ? 1 : 0),
                           d.Armor + (Card.Stars >= 1 ? 1 : 0) + (Card.Stars >= 3 ? 1 : 0), false);
        }

        /// <summary>Общая часть: свечение, тень, корпус, арт в маске с бликом, трещинами и вспышкой урона.</summary>
        private RectTransform BuildBase(Vector2 size, float k, float bodyScale, out RectTransform mask)
        {
            _content = LGBuild.Rect(Rt, "Content");
            _content.Stretch();

            // Живое свечение по контуру (CardAura): доступна — зелёное «дыхание», наведение — золото с бегущими огоньками
            // и искрами. Живёт внутри Content — поднимается и наклоняется вместе с картой.
            float gs = size.y / 280f;
            var auraRt = LGBuild.Rect(_content, "Aura");
            auraRt.Stretch();
            _aura = auraRt.gameObject.AddComponent<CardAura>();
            _aura.Width = 8f * gs;
            _aura.Corner = 13f * gs;
            _aura.Inset = 3f * gs;
            _aura.Tint = _frameColor;

            var shadow = LGBuild.Panel(_content, "Shadow", new Color(0, 0, 0, 0.7f));
            shadow.sprite = CardSprites.RoundGlow;
            shadow.type = Image.Type.Sliced;
            shadow.raycastTarget = false;
            shadow.rectTransform.Stretch(-12 * k, -26 * k, -16 * k, -4 * k);

            _body = CardSkin.Sliced(_content, "Body", "panel_dark", Color.white, bodyScale);
            _body.rectTransform.Stretch();

            var maskImg = Img(_content, "ArtMask", CardSprites.Round, Color.white);
            maskImg.type = Image.Type.Sliced;
            var m = maskImg.gameObject.AddComponent<Mask>();
            m.showMaskGraphic = false;
            mask = maskImg.rectTransform;
            return _content;
        }

        private void FillArt(RectTransform mask, Vector2 size, float aspect, float k, bool bottomShade)
        {
            _art = LGBuild.Rect(mask, "Art").gameObject.AddComponent<RawImage>();
            _art.rectTransform.Stretch(-6 * k, -6 * k, -6 * k, -6 * k);
            _art.texture = CardArt.Get(Card.Def, SideFaction);
            _art.raycastTarget = false;
            FitArt(_art, mask, size, aspect);
            _artUv = _art.uvRect;
            _idlePhase = UnityEngine.Random.value * 100f;
            if (bottomShade)
            {
                var shade = Img(mask, "Shade", CardSprites.Fade, new Color(0.01f, 0.015f, 0.03f, 0.95f));
                shade.rectTransform.anchorMin = new Vector2(0, 0);
                shade.rectTransform.anchorMax = new Vector2(1, 0.48f);
                shade.rectTransform.offsetMin = shade.rectTransform.offsetMax = Vector2.zero;
                shade.rectTransform.localRotation = Quaternion.Euler(0, 0, 180f);
            }
            // Верхний свет — арт «под стеклом»
            var gloss = Img(mask, "Gloss", CardSprites.Fade, new Color(1f, 1f, 1f, 0.07f));
            gloss.rectTransform.anchorMin = new Vector2(0, 0.6f);
            gloss.rectTransform.anchorMax = new Vector2(1, 1f);
            gloss.rectTransform.offsetMin = gloss.rectTransform.offsetMax = Vector2.zero;

            _sheen = Img(mask, "Sheen", CardSprites.Sheen, new Color(1f, 1f, 1f, 0f));
            _sheenRt = _sheen.rectTransform;
            _sheenRt.anchorMin = _sheenRt.anchorMax = new Vector2(0.5f, 0.5f);
            _sheenRt.sizeDelta = new Vector2(size.x * 0.55f, size.y * 1.8f);
            _sheenRt.localRotation = Quaternion.Euler(0, 0, -24f);
            _sheenT = UnityEngine.Random.value * 4f;

            _cracks = Img(mask, "Cracks", CardSprites.Cracks, new Color(1f, 0.9f, 0.8f, 0f));
            _cracks.rectTransform.Stretch();
        }

        /// <summary>Полная карта (рука, предпросмотр): арт, лента с именем, тип, текст на тёмной коже, рама, самоцветы.</summary>
        private void BuildFull(Vector2 size, CardDef d)
        {
            float k = size.y / 280f;
            BuildBase(size, k, 0.5f * k, out var mask);
            float pad = 10f * k;
            float artH = size.y * 0.47f;
            mask.TopBand(pad, artH, pad, pad);
            FillArt(mask, size, (size.x - 2 * pad) / artH, k, false);

            var artFrame = CardSkin.Sliced(_content, "ArtFrame", "frame_thin", _frameColor, 0.35f * k);
            artFrame.rectTransform.TopBand(pad - 3 * k, artH + 6 * k, pad - 3 * k, pad - 3 * k);

            // Текстовая панель: сверху тип карты, ниже текст эффекта (шрифт сам ужимается, чтобы всё влезло)
            bool unit = ShowsPower(d) || d.IsBoardUnit;
            var tp = CardSkin.Sliced(_content, "TextPanel", "panel_text", Color.white, 0.4f * k);
            tp.rectTransform.Stretch(11 * k, 11 * k, 11 * k, pad + artH + 19 * k);
            _kind = CardSkin.TitleText(tp.rectTransform, d.KindName, Mathf.RoundToInt(9 * k), Color.Lerp(_frameColor, Color.white, 0.25f), TextAnchor.MiddleCenter, 0f);
            _kind.rectTransform.TopBand(5 * k, 13 * k, 8 * k, 8 * k);
            _kind.Track(2f);
            _text = Lbl(tp.rectTransform, d.Text, Mathf.RoundToInt(12 * k), new Color(0.92f, 0.89f, 0.84f), TextAnchor.MiddleCenter, false);
            _text.horizontalOverflow = HorizontalWrapMode.Wrap;
            _text.verticalOverflow = VerticalWrapMode.Truncate;     // без этого Unity не ужимает шрифт
            _text.rectTransform.Stretch(8 * k, unit ? 27 * k : 8 * k, 8 * k, 18 * k);
            _text.resizeTextForBestFit = true; _text.resizeTextMinSize = Mathf.Max(6, Mathf.RoundToInt(7 * k)); _text.resizeTextMaxSize = Mathf.RoundToInt(12.5f * k);
            _text.lineSpacing = 0.92f;
            _text.supportRichText = true;

            // Лента с именем поперёк низа арта
            var rb = CardSkin.Sliced(_content, "Ribbon", "ribbon", Color.white, 0.5f * k);
            rb.rectTransform.TopBand(pad + artH - 15 * k, 32 * k, -7 * k, -7 * k);
            _name = CardSkin.TitleText(rb.rectTransform, d.Name.ToUpper(), Mathf.RoundToInt(14 * k), CardSkin.Parchment, TextAnchor.MiddleCenter, 1.5f);
            _name.rectTransform.Stretch(22 * k, 0, 22 * k, 1);
            _name.horizontalOverflow = HorizontalWrapMode.Wrap;
            _name.verticalOverflow = VerticalWrapMode.Truncate;
            _name.resizeTextForBestFit = true; _name.resizeTextMinSize = 6; _name.resizeTextMaxSize = Mathf.RoundToInt(14 * k);

            _hitFlash = Img(_content, "Hit", CardSprites.Round, new Color(1f, 0.3f, 0.2f, 0f));
            _hitFlash.type = Image.Type.Sliced;
            _hitFlash.rectTransform.Stretch();

            _frame = CardSkin.Sliced(_content, "Frame", "frame_card", _frameColor, 0.38f * k);
            _frame.rectTransform.Stretch();

            // Цена — синий кристалл в углу; урон и броня — в нижних углах, внутри рамы (не налезают на текст)
            _cost = CardSkin.Gem(_content, "gem_cost", new Vector2(0, 1), new Vector2(16 * k, -16 * k), 48 * k, d.Cost.ToString(), out _);
            if (ShowsPower(d))
                _power = CardSkin.Gem(_content, "gem_attack", new Vector2(0, 0), new Vector2(19 * k, 19 * k), 46 * k, "0", out _powerBadge);
            if (d.IsBoardUnit)
                _armor = CardSkin.Gem(_content, "gem_armor", new Vector2(1, 0), new Vector2(-19 * k, 19 * k), 45 * k, "0", out _armorBadge);
            Stars(k);
            Status(size, k);
        }

        /// <summary>Показывать ли урон: у сооружений без атаки (бункер, щит) — нет, чтобы не путать «0».</summary>
        private static bool ShowsPower(CardDef d) => d.IsBoardUnit && !(d.Kind == CardKind.Structure && d.Power <= 0);

        /// <summary>Карта на столе: арт во всю карту под рамой, имя внизу, крупные сила и броня.</summary>
        private void BuildCompact(Vector2 size, CardDef d)
        {
            float k = size.y / 178f;
            BuildBase(size, k, 0.4f * k, out var mask);
            float pad = 6f * k;
            mask.Stretch(pad, pad, pad, pad);
            FillArt(mask, size, (size.x - 2 * pad) / (size.y - 2 * pad), k, true);

            _hitFlash = Img(_content, "Hit", CardSprites.Round, new Color(1f, 0.3f, 0.2f, 0f));
            _hitFlash.type = Image.Type.Sliced;
            _hitFlash.rectTransform.Stretch();

            _frame = CardSkin.Sliced(_content, "Frame", "frame_card", _frameColor, 0.26f * k);
            _frame.rectTransform.Stretch();

            _name = CardSkin.TitleText(_content, d.Name, Mathf.RoundToInt(12 * k), CardSkin.Parchment, TextAnchor.MiddleCenter, 1.5f);
            _name.rectTransform.anchorMin = new Vector2(0, 0); _name.rectTransform.anchorMax = new Vector2(1, 0);
            _name.rectTransform.pivot = new Vector2(0.5f, 0);
            _name.rectTransform.sizeDelta = new Vector2(-16 * k, 30 * k);
            _name.rectTransform.anchoredPosition = new Vector2(0, d.IsBoardUnit ? 24 * k : 10 * k);
            _name.horizontalOverflow = HorizontalWrapMode.Wrap;
            _name.verticalOverflow = VerticalWrapMode.Truncate;
            _name.resizeTextForBestFit = true; _name.resizeTextMinSize = 7; _name.resizeTextMaxSize = Mathf.RoundToInt(12 * k);

            if (ShowsPower(d))
                _power = CardSkin.Gem(_content, "gem_attack", new Vector2(0, 0), new Vector2(12 * k, 10 * k), 44 * k, "0", out _powerBadge);
            if (d.IsBoardUnit)
                _armor = CardSkin.Gem(_content, "gem_armor", new Vector2(1, 0), new Vector2(-12 * k, 10 * k), 43 * k, "0", out _armorBadge);
            Stars(k * 0.8f);
            Status(size, k * 280f / 178f * 0.75f);
        }

        private void Stars(float k)
        {
            if (Card.Stars <= 0) return;
            _stars = Lbl(_content, new string('★', Mathf.Min(3, Card.Stars)), Mathf.RoundToInt(17 * k), new Color(1f, 0.82f, 0.35f), TextAnchor.UpperRight, true);
            _stars.rectTransform.Stretch(0, 0, 12 * k, 10 * k);
            var ol = _stars.gameObject.AddComponent<Outline>();
            ol.effectColor = new Color(0, 0, 0, 0.9f);
        }

        /// <summary>Лента состояния (подавлен, отключён, приказ отдан) поперёк верха карты.</summary>
        private void Status(Vector2 size, float k)
        {
            _statusIcon = CardSkin.Sliced(_content, "Status", "ribbon", new Color(1f, 1f, 1f, 0f), 0.4f * k);
            _statusIcon.rectTransform.At(new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -8 * k), new Vector2(size.x + 12 * k, 24 * k));
            _statusText = CardSkin.TitleText(_statusIcon.rectTransform, "", Mathf.RoundToInt(10 * k), Color.white, TextAnchor.MiddleCenter, 1f);
            _statusText.horizontalOverflow = HorizontalWrapMode.Wrap;
            _statusText.verticalOverflow = VerticalWrapMode.Truncate;
            _statusText.resizeTextForBestFit = true; _statusText.resizeTextMinSize = 6; _statusText.resizeTextMaxSize = Mathf.RoundToInt(10 * k);
            _statusText.rectTransform.Stretch(14 * k, 0, 14 * k, 0);
        }

        private static void FitArt(RawImage art, RectTransform frame, Vector2 size, float frameAspect)
        {
            if (art.texture == null) return;
            float texAspect = art.texture.width / (float)art.texture.height;
            // Обрезка по центру (cover): uvRect вместо растяжения
            if (texAspect > frameAspect)
            {
                float w = frameAspect / texAspect;
                art.uvRect = new Rect((1f - w) * 0.5f, 0f, w, 1f);
            }
            else
            {
                float h = texAspect / frameAspect;
                art.uvRect = new Rect(0f, (1f - h) * 0.6f, 1f, h);
            }
        }

        private static Image Img(Transform parent, string name, Sprite sp, Color c)
        {
            var i = LGBuild.Panel(parent, name, c);
            i.sprite = sp;
            i.raycastTarget = false;
            return i;
        }

        private static Text Lbl(Transform parent, string text, int size, Color c, TextAnchor a, bool bold)
        {
            var t = LGBuild.Label(parent, text, Mathf.Max(6, size), c, a, bold);
            return t;
        }

        // ==================== ЗНАЧЕНИЯ ====================

        public void RefreshNumbers(int power, int armor, int maxArmor, bool animate = true)
        {
            bool pChanged = power != ShownPower, aChanged = armor != ShownArmor;
            ShownPower = power;
            ShownArmor = Mathf.Max(0, armor);
            MaxArmor = Mathf.Max(1, maxArmor);
            if (_power != null)
            {
                _power.text = ShownPower.ToString();
                _power.color = ShownPower > Card.Def.Power ? new Color(0.55f, 1f, 0.6f) : ShownPower < Card.Def.Power ? new Color(1f, 0.5f, 0.45f) : Color.white;
            }
            if (_armor != null)
            {
                _armor.text = ShownArmor.ToString();
                _armor.color = ShownArmor < MaxArmor ? new Color(1f, 0.55f, 0.5f) : Color.white;
            }
            if (_hpFill != null)
            {
                float f = Mathf.Clamp01(ShownArmor / (float)MaxArmor);
                _hpFill.rectTransform.anchorMax = new Vector2(f, 1f);
                _hpFill.color = f > 0.6f ? new Color(0.4f, 1f, 0.55f) : f > 0.3f ? new Color(1f, 0.8f, 0.3f) : new Color(1f, 0.35f, 0.3f);
            }
            if (_cracks != null)
            {
                float f = ShownArmor / (float)MaxArmor;
                _cracks.color = new Color(1f, 0.92f, 0.85f, f < 0.5f ? Mathf.Lerp(0.85f, 0.25f, f * 2f) : 0f);
            }
            if (animate && pChanged && _powerBadge != null) _powerBadge.localScale = Vector3.one * 1.6f;
            if (animate && aChanged && _armorBadge != null) _armorBadge.localScale = Vector3.one * 1.6f;
        }

        public void SetStatus(string text, Color c)
        {
            if (_statusIcon == null) return;
            bool on = !string.IsNullOrEmpty(text);
            _statusIcon.color = on ? new Color(Mathf.Lerp(c.r, 1f, 0.35f), Mathf.Lerp(c.g, 1f, 0.35f), Mathf.Lerp(c.b, 1f, 0.35f), 1f) : new Color(0, 0, 0, 0);
            _statusText.text = on ? text : "";
        }

        // ==================== АНИМАЦИИ ====================

        public void SetFace(bool faceUp, bool instant)
        {
            _faceUp = faceUp;
            _flipTarget = faceUp ? 1f : -1f;
            if (instant) { _flip = _flipTarget; ApplyFlip(); }
        }

        public bool FaceUp => _faceUp;

        /// <summary>«Удар» при выходе на стол: карта чуть больше и оседает.</summary>
        public void Punch(float amount = 0.25f) => _punch = Mathf.Max(_punch, amount);

        public void Hit(float strength = 1f)
        {
            _hitA = Mathf.Max(_hitA, 0.75f * strength);
            _shakeT = Mathf.Max(_shakeT, 0.3f * strength);
        }

        public void SetAlpha(float a) { if (_group != null) _group.alpha = a; }

        private Vector2 _lungeDir;
        private float _lungeT = 1f, _lungeDist;

        /// <summary>Выпад к цели при атаке: карта резко подаётся вперёд по направлению dir и пружиной возвращается.</summary>
        public void Lunge(Vector2 dir, float dist = 22f)
        {
            _lungeDir = dir.sqrMagnitude > 0.001f ? dir.normalized : Vector2.up;
            _lungeDist = dist;
            _lungeT = 0f;
        }
        public void SetHoverable(bool on) { _hoverable = on; }

        private void ApplyFlip()
        {
            float sx = Mathf.Abs(_flip);
            _content.localScale = new Vector3(Mathf.Max(0.001f, sx), 1f, 1f);
            bool showBack = _flip < 0f;
            if (_back.gameObject.activeSelf != showBack) _back.gameObject.SetActive(showBack);
        }

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;

            // Переворот: через ребро
            if (!Mathf.Approximately(_flip, _flipTarget))
            {
                _flip = Mathf.MoveTowards(_flip, _flipTarget, dt * 5.5f);
                ApplyFlip();
            }

            // Наведение: подъём и наклон к курсору (только в руке)
            float hoverTarget = _hovered && _hoverable ? 1f : 0f;
            _hover = Mathf.MoveTowards(_hover, hoverTarget, dt * 7f);
            Vector2 tiltTarget = Vector2.zero;
            if (_hovered && _hoverable)
            {
                RectTransformUtility.ScreenPointToLocalPointInRectangle(Rt, Input.mousePosition, null, out var lp);
                var r = Rt.rect;
                tiltTarget = new Vector2(Mathf.Clamp(lp.y / (r.height * 0.5f), -1f, 1f) * -10f, Mathf.Clamp(lp.x / (r.width * 0.5f), -1f, 1f) * 12f);
            }
            _tilt = Vector2.Lerp(_tilt, tiltTarget, 1f - Mathf.Exp(-dt * 12f));

            _punch = Mathf.MoveTowards(_punch, 0f, dt * 1.6f);
            float shake = 0f;
            if (_shakeT > 0f) { _shakeT -= dt; shake = Mathf.Sin(Time.unscaledTime * 70f) * 5f * Mathf.Clamp01(_shakeT * 4f); }
            Vector2 lunge = Vector2.zero;
            if (_lungeT < 1f)
            {
                _lungeT = Mathf.Min(1f, _lungeT + dt * 3.2f);
                // быстрый выпад (первые 20%), затем пружина назад с лёгким перелётом
                float c = _lungeT < 0.2f ? Mathf.SmoothStep(0f, 1f, _lungeT / 0.2f) : Mathf.Exp(-(_lungeT - 0.2f) * 6f) * Mathf.Cos((_lungeT - 0.2f) * 9f);
                lunge = _lungeDir * _lungeDist * c;
            }
            // Живые карты: в руке — лёгкое покачивание (затихает при наведении), на столе — «дыхание»
            float tt = Time.unscaledTime + _idlePhase;
            float calm = 1f - _hover;
            float bob = IdleBob * calm * Mathf.Sin(tt * 1.35f) * 2.6f;
            float sway = IdleBob * calm * Mathf.Sin(tt * 0.83f) * 0.7f;
            float breathe = IdleBreathe * Mathf.Sin(tt * 1.15f) * 0.006f;
            _content.localPosition = new Vector3(shake + lunge.x, _hover * HoverLift + lunge.y + bob, 0f);
            float s = 1f + _hover * HoverScale + _punch + breathe;
            _content.localRotation = Quaternion.Euler(_tilt.x, _tilt.y, sway);

            // «Живой» арт: медленный наезд и дрейф камеры по картинке, при наклоне — параллакс
            if (_art != null && _art.texture != null && _artUv.width > 0f)
            {
                float z = 0.93f + 0.025f * Mathf.Sin(tt * 0.11f);
                float w = _artUv.width * z, h = _artUv.height * z;
                float fx = 0.5f + 0.35f * Mathf.Sin(tt * 0.07f) + _tilt.y / 12f * 0.45f;
                float fy = 0.5f + 0.35f * Mathf.Cos(tt * 0.09f) - _tilt.x / 10f * 0.45f;
                float x = _artUv.x + (_artUv.width - w) * Mathf.Clamp01(fx);
                float y = _artUv.y + (_artUv.height - h) * Mathf.Clamp01(fy);
                _art.uvRect = new Rect(x, y, w, h);
            }
            Rt.localScale = new Vector3(s, s, 1f);

            // Вспышка урона
            if (_hitA > 0f) { _hitA = Mathf.Max(0f, _hitA - dt * 3f); _hitFlash.color = new Color(1f, 0.25f, 0.15f, _hitA * 0.6f); }

            // Значки силы/брони возвращаются к обычному размеру
            if (_powerBadge != null) _powerBadge.localScale = Vector3.Lerp(_powerBadge.localScale, Vector3.one, 1f - Mathf.Exp(-dt * 10f));
            if (_armorBadge != null) _armorBadge.localScale = Vector3.Lerp(_armorBadge.localScale, Vector3.one, 1f - Mathf.Exp(-dt * 10f));

            // Фольга: блик бегает раз в несколько секунд; при наклоне следует за курсором
            bool foil = CardArt.Foil(Card.Def, Card.Stars);
            _sheenT += dt;
            if (foil || _hover > 0.01f)
            {
                float cyc = Mathf.Repeat(_sheenT, 3.6f) / 1.2f;
                float x = _hover > 0.01f ? _tilt.y / 12f : Mathf.Lerp(-1.4f, 1.4f, cyc);
                _sheenRt.anchoredPosition = new Vector2(x * Rt.rect.width * 0.8f, 0f);
                float a = _hover > 0.01f ? 0.28f * _hover : (cyc <= 1f ? Mathf.Sin(cyc * Mathf.PI) * 0.35f : 0f);
                var fc = foil ? Color.Lerp(Color.white, _frameColor, 0.4f) : Color.white;
                _sheen.color = new Color(fc.r, fc.g, fc.b, a);
            }
            else if (_sheen.color.a > 0f) _sheen.color = new Color(1, 1, 1, 0);

            // Свечение: доступна для розыгрыша / выбрана / подсветка эффекта
            _glowPulse += dt;
            float g = GlowBoost;
            bool focus = Selected || (_hovered && _hoverable);
            if (Playable) g = Mathf.Max(g, 0.45f + 0.1f * Mathf.Sin(_glowPulse * 2.6f));
            if (focus) g = Mathf.Max(g, 0.8f);
            float comets = focus ? 0.9f : Mathf.Max(Playable ? 0.3f : 0f, Mathf.Clamp01(GlowBoost) * 0.8f);
            GlowBoost = Mathf.MoveTowards(GlowBoost, 0f, dt * 1.5f);
            var gc = focus ? new Color(1f, 0.78f, 0.32f) : Playable ? new Color(0.3f, 1f, 0.5f) : _frameColor;
            float k = 1f - Mathf.Exp(-dt * 10f);
            _aura.Tint = Color.Lerp(_aura.Tint, gc, k);
            _aura.Level = Mathf.Lerp(_aura.Level, Mathf.Clamp01(g), k);
            _aura.Comets = Mathf.Lerp(_aura.Comets, comets, 1f - Mathf.Exp(-dt * 6f));
        }

        // ==================== ВВОД ====================

        public void OnPointerEnter(PointerEventData e) { _hovered = true; OnHoverIn?.Invoke(this); }
        public void OnPointerExit(PointerEventData e) { _hovered = false; OnHoverOut?.Invoke(this); }
        public void OnPointerDown(PointerEventData e) { }
        public void OnPointerClick(PointerEventData e) { if (!e.dragging) OnClick?.Invoke(this); }
        public void OnBeginDrag(PointerEventData e) { if (Interactive) OnDragBegin?.Invoke(this, e); }
        public void OnDrag(PointerEventData e) { if (Interactive) OnDragMove?.Invoke(this, e); }
        public void OnEndDrag(PointerEventData e) { if (Interactive) OnDragEnd?.Invoke(this, e); }
    }
}
