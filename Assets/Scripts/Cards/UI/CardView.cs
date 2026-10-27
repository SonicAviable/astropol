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
        private Image _glow, _body, _frame, _sheen, _hitFlash, _cracks, _back, _backPattern, _hpFill, _statusIcon;
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
            float k = size.y / 280f;   // масштаб шрифтов и отступов
            _group = gameObject.AddComponent<CanvasGroup>();
            _frameColor = CardArt.Frame(d, Card.Stars, SideFaction);

            _glow = LGBuild.Panel(Rt, "Glow", new Color(_frameColor.r, _frameColor.g, _frameColor.b, 0f));
            _glow.sprite = CardSprites.RoundGlow;
            _glow.type = Image.Type.Sliced;
            _glow.rectTransform.Stretch(-26 * k - 10, -26 * k - 10, -26 * k - 10, -26 * k - 10);

            _content = LGBuild.Rect(Rt, "Content");
            _content.Stretch();

            // Тень под картой
            var shadow = LGBuild.Panel(_content, "Shadow", new Color(0, 0, 0, 0.55f));
            shadow.sprite = CardSprites.RoundGlow;
            shadow.type = Image.Type.Sliced;
            shadow.rectTransform.Stretch(-14 * k, -22 * k, -14 * k, -6 * k);

            _body = Img(_content, "Body", CardSprites.Round, new Color(0.035f, 0.05f, 0.075f, 1f));
            _body.rectTransform.Stretch();
            _body.type = Image.Type.Sliced;

            // Арт в маске
            var maskImg = Img(_content, "ArtMask", CardSprites.Round, Color.white);
            maskImg.type = Image.Type.Sliced;
            var mask = maskImg.gameObject.AddComponent<Mask>();
            mask.showMaskGraphic = false;
            float pad = 5f * k + 2f;
            float artH = Compact ? 0f : size.y * 0.52f;
            if (Compact) maskImg.rectTransform.Stretch(pad, pad, pad, pad);
            else maskImg.rectTransform.TopBand(pad, artH, pad, pad);
            _art = LGBuild.Rect(maskImg.rectTransform, "Art").gameObject.AddComponent<RawImage>();
            _art.rectTransform.Stretch(-8 * k, -8 * k, -8 * k, -8 * k);
            _art.texture = CardArt.Get(d, SideFaction);
            _art.raycastTarget = false;
            FitArt(_art, maskImg.rectTransform, size, Compact ? (size.x - 2 * pad) / (size.y - 2 * pad) : (size.x - 2 * pad) / artH);

            // Затемнение низа арта — под имя и цифры
            var shade = Img(maskImg.rectTransform, "Shade", CardSprites.Fade, new Color(0.01f, 0.015f, 0.03f, 0.92f));
            shade.rectTransform.anchorMin = new Vector2(0, 0);
            shade.rectTransform.anchorMax = new Vector2(1, Compact ? 0.45f : 0.35f);
            shade.rectTransform.offsetMin = shade.rectTransform.offsetMax = Vector2.zero;
            shade.rectTransform.localRotation = Quaternion.Euler(0, 0, 180f);

            // Блик «фольги» — внутри маски
            _sheen = Img(maskImg.rectTransform, "Sheen", CardSprites.Sheen, new Color(1f, 1f, 1f, 0f));
            _sheenRt = _sheen.rectTransform;
            _sheenRt.anchorMin = _sheenRt.anchorMax = new Vector2(0.5f, 0.5f);
            _sheenRt.sizeDelta = new Vector2(size.x * 0.55f, size.y * 1.8f);
            _sheenRt.localRotation = Quaternion.Euler(0, 0, -24f);
            _sheenT = UnityEngine.Random.value * 4f;

            _cracks = Img(maskImg.rectTransform, "Cracks", CardSprites.Cracks, new Color(1f, 0.9f, 0.8f, 0f));
            _cracks.rectTransform.Stretch();

            _hitFlash = Img(_content, "Hit", CardSprites.Round, new Color(1f, 0.3f, 0.2f, 0f));
            _hitFlash.type = Image.Type.Sliced;
            _hitFlash.rectTransform.Stretch();

            _frame = Img(_content, "Frame", CardSprites.RoundEdge, _frameColor);
            _frame.type = Image.Type.Sliced;
            _frame.rectTransform.Stretch(-1, -1, -1, -1);

            if (!Compact)
            {
                // Имя на ленте под артом
                _name = Lbl(_content, d.Name.ToUpper(), Mathf.RoundToInt(15 * k), Color.white, TextAnchor.MiddleCenter, true);
                _name.rectTransform.TopBand(pad + artH - 30 * k, 30 * k, 10 * k, 10 * k);
                _name.resizeTextForBestFit = true; _name.resizeTextMinSize = 8; _name.resizeTextMaxSize = Mathf.RoundToInt(15 * k);
                _name.horizontalOverflow = HorizontalWrapMode.Wrap;
                // Тег типа
                var tag = Img(_content, "Tag", CardSprites.Round, new Color(_frameColor.r * 0.35f, _frameColor.g * 0.35f, _frameColor.b * 0.35f, 0.95f));
                tag.type = Image.Type.Sliced;
                tag.rectTransform.TopBand(pad + artH + 6 * k, 20 * k, 12 * k, size.x - 12 * k - 92 * k);
                _kind = Lbl(tag.rectTransform, d.KindName, Mathf.RoundToInt(10 * k), _frameColor, TextAnchor.MiddleCenter, true);
                _kind.Track(1.2f);
                // Текст эффекта
                _text = Lbl(_content, d.Text, Mathf.RoundToInt(12 * k), new Color(0.80f, 0.86f, 0.92f), TextAnchor.UpperLeft, false);
                _text.horizontalOverflow = HorizontalWrapMode.Wrap;
                _text.rectTransform.Stretch(12 * k, 46 * k, 12 * k, pad + artH + 32 * k);
                _text.resizeTextForBestFit = true; _text.resizeTextMinSize = 7; _text.resizeTextMaxSize = Mathf.RoundToInt(12 * k);
                _text.lineSpacing = 0.95f;
                // Цена
                var hex = Img(_content, "Cost", CardSprites.Hex, new Color(0.05f, 0.08f, 0.12f, 1f));
                hex.rectTransform.At(new Vector2(0, 1), new Vector2(0.5f, 0.5f), new Vector2(22 * k, -22 * k), new Vector2(44 * k, 44 * k));
                var hexRim = Img(hex.rectTransform, "Rim", CardSprites.Hex, new Color(1f, 0.83f, 0.42f, 1f));
                hexRim.rectTransform.Stretch(-3 * k, -3 * k, -3 * k, -3 * k);
                hexRim.transform.SetAsFirstSibling();
                _cost = Lbl(hex.rectTransform, d.Cost.ToString(), Mathf.RoundToInt(24 * k), new Color(1f, 0.88f, 0.55f), TextAnchor.MiddleCenter, true);
            }
            else
            {
                _name = Lbl(_content, d.Name, Mathf.RoundToInt(15 * k), Color.white, TextAnchor.LowerCenter, true);
                _name.rectTransform.Stretch(6, 30 * k, 6, 0);
                _name.horizontalOverflow = HorizontalWrapMode.Wrap;
                _name.resizeTextForBestFit = true; _name.resizeTextMinSize = 7; _name.resizeTextMaxSize = Mathf.RoundToInt(15 * k);
            }

            // Сила и броня (у приказов и ловушек нет)
            if (d.IsBoardUnit)
            {
                float bs = (Compact ? 44f : 40f) * k;
                _powerBadge = Badge(CardSprites.Diamond, new Color(1f, 0.78f, 0.32f), new Vector2(0, 0), new Vector2(bs * 0.62f, bs * 0.62f), bs, out _power);
                _armorBadge = Badge(CardSprites.ShieldShape, new Color(0.55f, 0.82f, 1f), new Vector2(1, 0), new Vector2(-bs * 0.62f, bs * 0.62f), bs, out _armor);
                if (Compact)
                {
                    var hp = LGBuild.Panel(_content, "HpTrack", new Color(0, 0, 0, 0.7f));
                    hp.rectTransform.anchorMin = new Vector2(0.18f, 0f);
                    hp.rectTransform.anchorMax = new Vector2(0.82f, 0f);
                    hp.rectTransform.sizeDelta = new Vector2(0, 5);
                    hp.rectTransform.anchoredPosition = new Vector2(0, 6);
                    _hpFill = LGBuild.Panel(hp.rectTransform, "Fill", new Color(0.4f, 1f, 0.55f));
                    _hpFill.rectTransform.anchorMin = Vector2.zero;
                    _hpFill.rectTransform.anchorMax = Vector2.one;
                    _hpFill.rectTransform.offsetMin = _hpFill.rectTransform.offsetMax = Vector2.zero;
                }
            }

            // Звёзды ветеранства
            if (Card.Stars > 0)
            {
                _stars = Lbl(_content, new string('★', Mathf.Min(3, Card.Stars)), Mathf.RoundToInt(16 * k), new Color(1f, 0.82f, 0.35f), TextAnchor.UpperRight, true);
                _stars.rectTransform.Stretch(0, 0, 9 * k, 6 * k);
            }

            // Значок состояния (подавлен, отключён, обездвижен)
            _statusIcon = Img(_content, "Status", CardSprites.Round, new Color(0.9f, 0.3f, 0.3f, 0f));
            _statusIcon.type = Image.Type.Sliced;
            _statusIcon.rectTransform.At(new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -6 * k), new Vector2(size.x * 0.8f, 18 * k));
            _statusText = Lbl(_statusIcon.rectTransform, "", Mathf.RoundToInt(10 * k), Color.white, TextAnchor.MiddleCenter, true);

            // Рубашка
            _back = Img(_content, "Back", CardSprites.Round, new Color(0.03f, 0.05f, 0.08f, 1f));
            _back.type = Image.Type.Sliced;
            _back.rectTransform.Stretch();
            var bc = CardArt.Faction(SideFaction);
            _backPattern = Img(_back.rectTransform, "Pattern", CardSprites.BackPattern, new Color(bc.r, bc.g, bc.b, 0.55f));
            _backPattern.rectTransform.Stretch(4, 4, 4, 4);
            var emb = CardArt.Emblem(SideFaction);
            if (emb != null)
            {
                _backEmblem = LGBuild.Rect(_back.rectTransform, "Emblem").gameObject.AddComponent<RawImage>();
                _backEmblem.texture = emb;
                _backEmblem.raycastTarget = false;
                float es = Mathf.Min(size.x, size.y) * 0.62f;
                _backEmblem.rectTransform.At(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(es, es));
            }
            var backRim = Img(_back.rectTransform, "Rim", CardSprites.RoundEdge, new Color(bc.r, bc.g, bc.b, 0.9f));
            backRim.type = Image.Type.Sliced;
            backRim.rectTransform.Stretch(-1, -1, -1, -1);
            _back.gameObject.SetActive(false);

            // Луч на всю карту ловит мышь
            var hit = LGBuild.Panel(Rt, "Hit", new Color(0, 0, 0, 0), raycast: true);
            hit.rectTransform.Stretch();

            ShownPower = d.Power;
            MaxArmor = ShownArmor = d.Armor;
            RefreshNumbers(d.Power + (Card.Stars >= 2 ? 1 : 0) + (Card.Stars >= 3 ? 1 : 0), d.Armor + (Card.Stars >= 1 ? 1 : 0) + (Card.Stars >= 3 ? 1 : 0),
                           d.Armor + (Card.Stars >= 1 ? 1 : 0) + (Card.Stars >= 3 ? 1 : 0), false);
        }

        private RectTransform Badge(Sprite sp, Color c, Vector2 anchor, Vector2 pos, float s, out Text label)
        {
            var b = Img(_content, "Badge", sp, new Color(0.04f, 0.06f, 0.09f, 1f));
            b.rectTransform.At(anchor, new Vector2(0.5f, 0.5f), pos, new Vector2(s, s));
            var rim = Img(b.rectTransform, "Rim", sp, c);
            rim.rectTransform.Stretch(-3, -3, -3, -3);
            rim.transform.SetAsFirstSibling();
            label = Lbl(b.rectTransform, "0", Mathf.RoundToInt(s * 0.5f), Color.white, TextAnchor.MiddleCenter, true);
            var ol = label.gameObject.AddComponent<Outline>();
            ol.effectColor = new Color(0, 0, 0, 0.8f);
            return b.rectTransform;
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
            _statusIcon.color = on ? new Color(c.r * 0.6f, c.g * 0.6f, c.b * 0.6f, 0.92f) : new Color(0, 0, 0, 0);
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
            _content.localPosition = new Vector3(shake, _hover * 18f, 0f);
            float s = 1f + _hover * 0.12f + _punch;
            _content.localRotation = Quaternion.Euler(_tilt.x, _tilt.y, 0f);
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
            if (Playable) g = Mathf.Max(g, 0.35f + 0.2f * Mathf.Sin(_glowPulse * 3f));
            if (Selected || (_hovered && _hoverable)) g = Mathf.Max(g, 0.9f);
            GlowBoost = Mathf.MoveTowards(GlowBoost, 0f, dt * 1.5f);
            var gc = Playable ? new Color(0.45f, 1f, 0.6f) : _frameColor;
            _glow.color = new Color(gc.r, gc.g, gc.b, Mathf.Lerp(_glow.color.a, g * 0.85f, 1f - Mathf.Exp(-dt * 10f)));
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
