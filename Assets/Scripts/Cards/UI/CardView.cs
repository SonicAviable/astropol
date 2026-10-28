using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using StellarisClone.Core;
using StellarisClone.Rendering;

namespace StellarisClone.Cards
{
    /// <summary>
    /// Карта на экране в оформлении по редкостям: литая рама из металла редкости (сталь, серебро, фиолет, золото,
    /// красное золото у командиров), арт во всю карту под стеклянной панелью, лента имени, кристалл цены, гербы силы и брони,
    /// значок редкости, голо-фольга у эпических и выше, гребень у легендарных. Два вида: полная (рука, предпросмотр — с типом
    /// и текстом) и компактная (на столе — крупнее имя и числа, без текста). Рубашка — гравировка в цвете фракции с эмблемой. Живёт сама: наклон к курсору и подъём в руке, переворот, «удар» при выходе на стол,
    /// вспышка урона, трещины при тяжёлых повреждениях, свечение доступности. Наклоны — в настоящей перспективе
    /// (Perspective3D): атака с замахом и отрывом от стола, качание от попадания, переворот вокруг вертикали.
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
        private Image _body, _frame, _sheen, _hitFlash, _cracks, _back, _hpFill, _statusIcon, _foil;
        private RawImage _art, _backEmblem;
        private Text _name, _cost, _power, _armor, _kind, _text, _stars, _statusText;
        private RectTransform _powerBadge, _armorBadge, _sheenRt;
        private Color _frameColor;
        private CardRarity _rarity;
        private string _rk = "common";
        private float _foilA;
        private Image _shadow;
        private Color _shadowColor;
        private Vector2 _shadowBase;
        /// <summary>Все картинки карты — для перспективы (Perspective3D) при наклоне.</summary>
        private Graphic[] _graphics;
        private bool _wasTilted;

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
        /// <summary>Спокойная карта витрины (Арсенал): без наклона к курсору и без «живого» дрейфа арта —
        /// в сетке из десятков карт движение читается как дрожь, а наклон у края уводит карту из-под курсора.</summary>
        public bool Calm;
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
            _rarity = CardArt.Rarity(d, Card.Stars);
            _rk = CardArt.RarityKey(_rarity);
            _frameColor = CardArt.RarityColor(_rarity);
            if (Compact) BuildCompact(size, d); else BuildFull(size, d);

            // Рубашка: тёмная гравировка с кольцом в цвете фракции, эмблема, золотая рама
            float u = size.x / 330f;
            var bc = CardArt.Faction(SideFaction);
            _back = CardSkin.Simple(_content, "Back", "cardback_bg", Color.Lerp(bc, Color.white, 0.2f));
            _back.rectTransform.Stretch();
            var emb = CardArt.Emblem(SideFaction);
            if (emb != null)
            {
                var halo = CardSkin.Simple(_back.rectTransform, "Halo", "glow", new Color(bc.r, bc.g, bc.b, 0.5f));
                halo.rectTransform.At(new Vector2(0.5f, 0.54f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(300f * u, 300f * u));
                _backEmblem = LGBuild.Rect(_back.rectTransform, "Emblem").gameObject.AddComponent<RawImage>();
                _backEmblem.texture = emb;
                _backEmblem.raycastTarget = false;
                _backEmblem.rectTransform.At(new Vector2(0.5f, 0.54f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(190f * u, 190f * u));
            }
            var backRim = CardSkin.Simple(_back.rectTransform, "Rim", "cardframe_legend", Color.white);
            backRim.rectTransform.Stretch();
            _back.gameObject.SetActive(false);

            // Луч на всю карту ловит мышь
            var hit = LGBuild.Panel(Rt, "Hit", new Color(0, 0, 0, 0), raycast: true);
            hit.rectTransform.Stretch();

            ShownPower = d.Power;
            MaxArmor = ShownArmor = d.Armor;
            RefreshNumbers(d.Power + (Card.Stars >= 2 ? 1 : 0) + (Card.Stars >= 3 ? 1 : 0), d.Armor + (Card.Stars >= 1 ? 1 : 0) + (Card.Stars >= 3 ? 1 : 0),
                           d.Armor + (Card.Stars >= 1 ? 1 : 0) + (Card.Stars >= 3 ? 1 : 0), false);

            // Перспектива последней в цепочке эффектов сетки (после теней и разрядки текста)
            _graphics = Perspective3D.Attach(Rt);
            if (_shadow != null) _shadowBase = _shadow.rectTransform.anchoredPosition;
        }

        /// <summary>Картинка арта (для осколков при гибели).</summary>
        public RawImage ArtImage => _art;

        /// <summary>
        /// Общая часть (по промо-макету набора): свечение, тень, тёмная подложка, арт в маске со срезанными углами,
        /// голо-фольга (эпические и выше), затемнение с нижней стеклянной панелью и литая рама цвета редкости.
        /// Все размеры — в единицах макета: карта 330×462, u = ширина / 330.
        /// </summary>
        private void BuildBase(Vector2 size, float u)
        {
            _content = LGBuild.Rect(Rt, "Content");
            _content.Stretch();

            // Живое свечение по контуру (CardAura): доступна — зелёное «дыхание», наведение — золото с бегущими огоньками
            // и искрами. Живёт внутри Content — поднимается и наклоняется вместе с картой.
            var auraRt = LGBuild.Rect(_content, "Aura");
            auraRt.Stretch();
            _aura = auraRt.gameObject.AddComponent<CardAura>();
            _aura.Width = 13f * u;
            _aura.Corner = 18f * u;
            _aura.Inset = 4f * u;
            _aura.Tint = _frameColor;

            var shadow = _shadow = LGBuild.Panel(_content, "Shadow", new Color(0, 0, 0, 0.7f));
            _shadowColor = shadow.color;
            shadow.sprite = CardSprites.RoundGlow;
            shadow.type = Image.Type.Sliced;
            shadow.raycastTarget = false;
            shadow.rectTransform.Stretch(-20 * u, -42 * u, -26 * u, -6 * u);

            _body = CardSkin.Simple(_content, "Body", "card_mask", new Color(0.027f, 0.043f, 0.07f, 1f));
            _body.rectTransform.Stretch();

            var maskImg = CardSkin.Simple(_content, "ArtMask", "card_mask", Color.white);
            maskImg.rectTransform.Stretch();
            maskImg.gameObject.AddComponent<Mask>().showMaskGraphic = false;
            var mask = maskImg.rectTransform;

            // Арт — верхние три четверти, снизу его закрывает стеклянная панель
            _art = LGBuild.Rect(mask, "Art").gameObject.AddComponent<RawImage>();
            _art.rectTransform.anchorMin = new Vector2(0f, 0.25f);
            _art.rectTransform.anchorMax = Vector2.one;
            _art.rectTransform.offsetMin = _art.rectTransform.offsetMax = Vector2.zero;
            _art.texture = CardArt.Get(Card.Def, SideFaction);
            _art.raycastTarget = false;
            FitArt(_art, mask, size, size.x / (size.y * 0.75f));
            _artUv = _art.uvRect;
            _idlePhase = UnityEngine.Random.value * 100f;

            // Голографическая фольга: перелив светом поверх арта (дышит и ярче при наведении)
            _foilA = _rarity == CardRarity.Legendary ? 0.24f : _rarity == CardRarity.Epic ? 0.2f : _rarity == CardRarity.Commander ? 0.12f : 0f;
            if (_foilA > 0f)
            {
                _foil = CardSkin.Simple(mask, "Foil", "card_foil", new Color(1f, 1f, 1f, _foilA));
                _foil.material = UIFx.GlowMat;
                _foil.rectTransform.Stretch();
            }

            _sheen = Img(mask, "Sheen", CardSprites.Sheen, new Color(1f, 1f, 1f, 0f));
            _sheenRt = _sheen.rectTransform;
            _sheenRt.anchorMin = _sheenRt.anchorMax = new Vector2(0.5f, 0.5f);
            _sheenRt.sizeDelta = new Vector2(size.x * 0.55f, size.y * 1.8f);
            _sheenRt.localRotation = Quaternion.Euler(0, 0, -24f);
            _sheenT = UnityEngine.Random.value * 4f;

            _cracks = Img(mask, "Cracks", CardSprites.Cracks, new Color(1f, 0.9f, 0.8f, 0f));
            _cracks.rectTransform.anchorMin = new Vector2(0f, 0.25f);
            _cracks.rectTransform.anchorMax = Vector2.one;
            _cracks.rectTransform.offsetMin = _cracks.rectTransform.offsetMax = Vector2.zero;

            var shade = CardSkin.Simple(mask, "Shade", "card_shade_" + _rk, Color.white);
            shade.rectTransform.Stretch();

            _hitFlash = CardSkin.Simple(_content, "HitFlash", "card_mask", new Color(1f, 0.3f, 0.2f, 0f));
            _hitFlash.rectTransform.Stretch();

            _frame = CardSkin.Simple(_content, "Frame", "cardframe_" + _rk, Color.white);
            _frame.rectTransform.Stretch();

            // Вспышка по краю: урон — красным, лечение — зелёным, усиление — золотом (вместо заливки всей карты)
            float es = 0.5f * u;
            _edge = CardSkin.Sliced(_content, "EdgeFlash", "glow_edge", new Color(1f, 1f, 1f, 0f), es);
            _edge.material = UIFx.GlowMat;
            float eo = -96f * es * 0.55f;
            _edge.rectTransform.Stretch(eo, eo, eo, eo);
        }

        private Image _edge;
        private Color _edgeC = Color.white;
        private float _edgeA;

        /// <summary>Свечение по контуру карты цветом c (урон, лечение, усиление); гаснет само.</summary>
        public void EdgeFlash(Color c, float strength = 1f)
        {
            _edgeC = c;
            _edgeA = Mathf.Max(_edgeA, strength);
        }

        /// <summary>Лента имени поперёк карты: литая кромка цвета редкости, тёмная середина, имя шрифтом заголовков.</summary>
        private void Plate(CardDef d, float u, float fromTop, float h, float font)
        {
            var plate = CardSkin.Simple(_content, "Plate", "card_plate_" + _rk, Color.white);
            plate.rectTransform.At(new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f), new Vector2(0f, -fromTop - h * 0.5f), new Vector2(334f * u, h));
            bool gold = _rarity == CardRarity.Legendary || _rarity == CardRarity.Commander;
            _name = CardSkin.TitleText(plate.rectTransform, d.Name.ToUpper(), Mathf.RoundToInt(font), gold ? new Color(1f, 0.87f, 0.5f) : new Color(0.96f, 0.92f, 0.84f), TextAnchor.MiddleCenter, Mathf.Max(1f, 1.5f * u * 2f));
            _name.rectTransform.Stretch(26f * u, 0f, 26f * u, 1f);
            _name.horizontalOverflow = HorizontalWrapMode.Wrap;
            _name.verticalOverflow = VerticalWrapMode.Truncate;
            _name.resizeTextForBestFit = true; _name.resizeTextMinSize = 6; _name.resizeTextMaxSize = Mathf.RoundToInt(font);
        }

        /// <summary>Самоцвет из оформления редкости (цена, сила, броня) с числом.</summary>
        private Text Badge(string sprite, Vector2 anchor, Vector2 pos, Vector2 sz, float font, string value, out RectTransform rt)
        {
            var g = CardSkin.Simple(_content, "Gem_" + sprite, sprite + "_" + _rk, Color.white);
            g.rectTransform.At(anchor, new Vector2(0.5f, 0.5f), pos, sz);
            rt = g.rectTransform;
            var t = CardSkin.TitleText(g.rectTransform, value, Mathf.RoundToInt(font), Color.white, TextAnchor.MiddleCenter, Mathf.Max(1.2f, font / 15f));
            t.rectTransform.Stretch(0, 0, 0, 0);
            return t;
        }

        /// <summary>Эмблема фракции в правом верхнем углу (фракционные карты и командиры).</summary>
        private void FactionMark(CardDef d, float u, float sz)
        {
            var f = d.Faction ?? (d.Kind == CardKind.Hero ? SideFaction : null);
            var tex = CardArt.Emblem(f);
            if (tex == null) return;
            var back = Img(_content, "FactionShade", CardSprites.Glow, new Color(0f, 0f, 0f, 0.75f));
            back.rectTransform.At(Vector2.one, new Vector2(0.5f, 0.5f), new Vector2(-38f * u, -38f * u), new Vector2(sz * 1.5f, sz * 1.5f));
            var img = LGBuild.Rect(_content, "Faction").gameObject.AddComponent<RawImage>();
            img.texture = tex;
            img.raycastTarget = false;
            img.rectTransform.At(Vector2.one, new Vector2(0.5f, 0.5f), new Vector2(-38f * u, -38f * u), new Vector2(sz, sz));
        }

        /// <summary>Значок редкости внизу по центру (и подпись на полной карте).</summary>
        private void RarityMark(float u, float gem, bool label)
        {
            var g = CardSkin.Simple(_content, "RarityGem", "card_gem_" + _rk, Color.white);
            g.rectTransform.At(new Vector2(0.5f, 0f), new Vector2(0.5f, 0.5f), new Vector2(0f, 20f * u), new Vector2(gem, gem));
            if (!label) return;
            var t = Lbl(_content, CardArt.RarityName(_rarity), Mathf.RoundToInt(9f * u), new Color(0.56f, 0.63f, 0.68f), TextAnchor.MiddleCenter, true);
            t.rectTransform.At(new Vector2(0.5f, 0f), new Vector2(0.5f, 0.5f), new Vector2(0f, 38f * u), new Vector2(200f * u, 14f * u));
            t.Track(2.5f * u);
        }

        /// <summary>Подпись типа: «ЮНИТ · ТЕХНИКА», «ПРИКАЗ», «ГЕРОЙ · ПЕХОТА».</summary>
        private static string TypeLine(CardDef d)
        {
            string sub = d.Has(Kw.Vehicle) ? Loc.T("ТЕХНИКА") : d.Has(Kw.Drone) ? Loc.T("ДРОН") : d.Has(Kw.Infantry) ? Loc.T("ПЕХОТА") : d.Has(Kw.Militia) ? Loc.T("ОПОЛЧЕНИЕ") : null;
            return sub == null ? d.KindName : d.KindName + " · " + sub;
        }

        /// <summary>Ключевое слово в начале текста («Бронебой:», «При раскрытии:») — жирным в цвете редкости.</summary>
        private static string RichText(string text, Color acc)
        {
            if (string.IsNullOrEmpty(text)) return text;
            int i = text.IndexOf(':');
            int dot = text.IndexOf('.');
            if (i <= 0 || i > 32 || (dot >= 0 && dot < i)) return text;
            string hex = ColorUtility.ToHtmlStringRGB(Color.Lerp(acc, Color.white, 0.35f));
            return "<b><color=#" + hex + ">" + text.Substring(0, i + 1) + "</color></b>" + text.Substring(i + 1);
        }

        /// <summary>Полная карта (рука, предпросмотр) — как в макете: арт во всю карту, лента имени, тип, текст, самоцветы.</summary>
        private void BuildFull(Vector2 size, CardDef d)
        {
            float u = size.x / 330f;
            BuildBase(size, u);
            float plateTop = size.y * 0.55f;
            Plate(d, u, plateTop, 46f * u, 19f * u);

            _kind = CardSkin.TitleText(_content, TypeLine(d), Mathf.RoundToInt(10.5f * u), Color.Lerp(_frameColor, Color.white, 0.2f), TextAnchor.MiddleCenter, 0f);
            _kind.rectTransform.At(new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f), new Vector2(0f, -plateTop - 59f * u), new Vector2(300f * u, 15f * u));
            _kind.Track(3f * u);

            _text = Lbl(_content, RichText(d.Text, _frameColor), Mathf.RoundToInt(13.5f * u), new Color(0.89f, 0.87f, 0.82f), TextAnchor.MiddleCenter, false);
            _text.horizontalOverflow = HorizontalWrapMode.Wrap;
            _text.verticalOverflow = VerticalWrapMode.Truncate;     // без этого Unity не ужимает шрифт
            _text.rectTransform.Stretch(36f * u, 50f * u, 36f * u, plateTop + 70f * u);
            _text.resizeTextForBestFit = true; _text.resizeTextMinSize = Mathf.Max(6, Mathf.RoundToInt(8 * u)); _text.resizeTextMaxSize = Mathf.RoundToInt(13.5f * u);
            _text.lineSpacing = 0.95f;
            _text.supportRichText = true;

            FactionMark(d, u, 44f * u);
            if (_rarity == CardRarity.Legendary || _rarity == CardRarity.Commander) Crest(u);
            RarityMark(u, 26f * u, true);

            _cost = Badge("card_cost", new Vector2(0, 1), new Vector2(29f * u, -32f * u), new Vector2(70f * u, 76f * u), 30f * u, d.Cost.ToString(), out _);
            if (ShowsPower(d))
                _power = Badge("card_atk", Vector2.zero, new Vector2(28f * u, 31f * u), new Vector2(64f * u, 70f * u), 28f * u, "0", out _powerBadge);
            if (d.IsBoardUnit)
                _armor = Badge("card_def", new Vector2(1, 0), new Vector2(-28f * u, 31f * u), new Vector2(64f * u, 70f * u), 28f * u, "0", out _armorBadge);
            Stars(u);
            Status(size, size.y / 280f);
        }

        /// <summary>Гребень над рамкой легендарных карт и командиров.</summary>
        private void Crest(float u)
        {
            var c = CardSkin.Simple(_content, "Crest", "card_crest", Color.white);
            c.rectTransform.At(new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f), new Vector2(0f, -5f * u), new Vector2(160f * u, 42f * u));
        }

        /// <summary>Показывать ли урон: у сооружений без атаки (бункер, щит) — нет, чтобы не путать «0».</summary>
        private static bool ShowsPower(CardDef d) => d.IsBoardUnit && !(d.Kind == CardKind.Structure && d.Power <= 0);

        /// <summary>Карта на столе: то же оформление, но без текста — крупнее имя, сила и броня, значок редкости.</summary>
        private void BuildCompact(Vector2 size, CardDef d)
        {
            float u = size.x / 330f;
            BuildBase(size, u);
            Plate(d, u, size.y * 0.6f, 70f * u, 30f * u);
            FactionMark(d, u, 62f * u);
            if (_rarity == CardRarity.Legendary || _rarity == CardRarity.Commander) Crest(u);
            RarityMark(u, 40f * u, false);
            // На столе главное — сила и броня: крупные гербы с крупными цифрами, читаются с первого взгляда
            if (ShowsPower(d))
                _power = Badge("card_atk", Vector2.zero, new Vector2(48f * u, 52f * u), new Vector2(112f * u, 122f * u), 56f * u, "0", out _powerBadge);
            if (d.IsBoardUnit)
                _armor = Badge("card_def", new Vector2(1, 0), new Vector2(-48f * u, 52f * u), new Vector2(112f * u, 122f * u), 56f * u, "0", out _armorBadge);
            Stars(u * 1.6f);
            Status(size, size.y / 178f * 280f / 178f * 0.75f);
        }

        private void Stars(float u)
        {
            if (Card.Stars <= 0) return;
            _stars = Lbl(_content, new string('★', Mathf.Min(3, Card.Stars)), Mathf.RoundToInt(16 * u), new Color(1f, 0.82f, 0.35f), TextAnchor.MiddleCenter, true);
            _stars.rectTransform.At(new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f), new Vector2(0f, -36f * u), new Vector2(120f * u, 22f * u));
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
            EdgeFlash(new Color(1f, 0.3f, 0.18f), Mathf.Clamp(0.7f * strength, 0.5f, 1.2f));
            // Удар отбрасывает край карты — она качается в 3D и успокаивается
            _joltDir = UnityEngine.Random.insideUnitCircle.normalized;
            _joltPower = Mathf.Clamp(strength, 0.3f, 1.5f);
            _joltT = 0f;
        }

        public void SetAlpha(float a) { if (_group != null) _group.alpha = a; }

        private Vector2 _lungeDir, _joltDir;
        private float _lungeT = 1f, _lungeDist, _joltT = 1f, _joltPower, _raiseT = 1f, _raiseDur = 0.5f;

        /// <summary>
        /// Атака в 3D: карта отрывается от стола (больше, тень уходит в сторону), замахивается — передний край
        /// приподнимается, — затем бьёт к цели, наклоняясь передним краем к ней, и пружиной ложится обратно.
        /// </summary>
        public void Lunge(Vector2 dir, float dist = 22f)
        {
            _lungeDir = dir.sqrMagnitude > 0.001f ? dir.normalized : Vector2.up;
            _lungeDist = dist;
            _lungeT = 0f;
        }

        /// <summary>Подъём над столом на dur секунд (переход в другой сектор, перенос): карта «летит» и ложится.</summary>
        public void Raise(float dur = 0.5f)
        {
            _raiseDur = Mathf.Max(0.1f, dur);
            _raiseT = 0f;
        }
        public void SetHoverable(bool on) { _hoverable = on; }

        private float _flipYaw;

        /// <summary>Переворот — настоящий поворот вокруг вертикали (с перспективой); рубашка не зеркалится.</summary>
        private void ApplyFlip()
        {
            _flipYaw = Mathf.Acos(Mathf.Clamp01(Mathf.Abs(_flip))) * Mathf.Rad2Deg * (_flip < 0f ? -1f : 1f);
            _content.localRotation = Quaternion.Euler(0f, _flipYaw, 0f);
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
            if (_hovered && _hoverable && !Calm)
            {
                RectTransformUtility.ScreenPointToLocalPointInRectangle(Rt, Input.mousePosition, null, out var lp);
                var r = Rt.rect;
                tiltTarget = new Vector2(Mathf.Clamp(lp.y / (r.height * 0.5f), -1f, 1f) * -10f, Mathf.Clamp(lp.x / (r.width * 0.5f), -1f, 1f) * 12f);
            }
            _tilt = Vector2.Lerp(_tilt, tiltTarget, 1f - Mathf.Exp(-dt * 12f));

            _punch = Mathf.MoveTowards(_punch, 0f, dt * 1.6f);
            float shake = 0f;
            if (_shakeT > 0f) { _shakeT -= dt; shake = Mathf.Sin(Time.unscaledTime * 70f) * 5f * Mathf.Clamp01(_shakeT * 4f); }
            Vector2 lunge = Vector2.zero, tilt3 = Vector2.zero;
            float lift = 0f;
            if (_lungeT < 1f)
            {
                _lungeT = Mathf.Min(1f, _lungeT + dt / 0.5f);
                float t = _lungeT, c, tl;
                if (t < 0.12f)
                {
                    // замах: чуть назад, передний край вверх, карта отрывается от стола
                    float u = Mathf.SmoothStep(0f, 1f, t / 0.12f);
                    c = -0.25f * u; tl = -0.55f * u; lift = u;
                }
                else if (t < 0.22f)
                {
                    // удар: резко вперёд, передний край к цели
                    float u = (t - 0.12f) / 0.1f; u *= u;
                    c = Mathf.Lerp(-0.25f, 1f, u); tl = Mathf.Lerp(-0.55f, 1f, u); lift = 1f;
                }
                else
                {
                    // пружина назад с лёгким перелётом, карта ложится на стол
                    float u = (t - 0.22f) / 0.78f;
                    c = tl = Mathf.Exp(-u * 5f) * Mathf.Cos(u * 9f);
                    lift = 1f - Mathf.SmoothStep(0f, 1f, u);
                }
                lunge = _lungeDir * _lungeDist * c;
                // ближний к цели край уходит «в стол» (дальше от зрителя), задний поднимается
                tilt3 += new Vector2(_lungeDir.y, -_lungeDir.x) * 24f * tl;
            }
            if (_raiseT < 1f)
            {
                _raiseT = Mathf.Min(1f, _raiseT + dt / _raiseDur);
                lift = Mathf.Max(lift, Mathf.Sin(_raiseT * Mathf.PI));
            }
            if (_joltT < 1f)
            {
                _joltT = Mathf.Min(1f, _joltT + dt / 0.55f);
                float j = Mathf.Exp(-_joltT * 6f) * Mathf.Cos(_joltT * 22f) * (1f - _joltT);
                tilt3 += new Vector2(_joltDir.y, -_joltDir.x) * 16f * _joltPower * j;
            }
            // Живые карты: в руке — лёгкое покачивание (затихает при наведении), на столе — «дыхание»
            float tt = Time.unscaledTime + _idlePhase;
            float calm = 1f - _hover;
            float bob = IdleBob * calm * Mathf.Sin(tt * 1.35f) * 2.6f;
            float sway = IdleBob * calm * Mathf.Sin(tt * 0.83f) * 0.7f;
            float breathe = IdleBreathe * Mathf.Sin(tt * 1.15f) * 0.006f;
            _content.localPosition = new Vector3(shake + lunge.x, _hover * HoverLift + lunge.y + bob, 0f);
            float s = 1f + _hover * HoverScale + _punch + breathe + lift * 0.1f;
            _content.localRotation = Quaternion.Euler(_tilt.x + tilt3.x, _tilt.y + tilt3.y + _flipYaw, sway);
            // Поднятая карта: тень отходит и светлеет — карта висит над столом
            if (_shadow != null)
            {
                _shadow.rectTransform.anchoredPosition = _shadowBase + new Vector2(lift * 14f, -lift * 22f);
                _shadow.color = new Color(_shadowColor.r, _shadowColor.g, _shadowColor.b, _shadowColor.a * (1f - 0.35f * lift));
            }

            // «Живой» арт: медленный наезд и дрейф камеры по картинке, при наклоне — параллакс
            if (_art != null && _art.texture != null && _artUv.width > 0f)
            {
                float z = Calm ? 0.95f : 0.93f + 0.025f * Mathf.Sin(tt * 0.11f);
                float w = _artUv.width * z, h = _artUv.height * z;
                float fx = Calm ? 0.5f : 0.5f + 0.35f * Mathf.Sin(tt * 0.07f) + _tilt.y / 12f * 0.45f;
                float fy = Calm ? 0.5f : 0.5f + 0.35f * Mathf.Cos(tt * 0.09f) - _tilt.x / 10f * 0.45f;
                float x = _artUv.x + (_artUv.width - w) * Mathf.Clamp01(fx);
                float y = _artUv.y + (_artUv.height - h) * Mathf.Clamp01(fy);
                _art.uvRect = new Rect(x, y, w, h);
            }
            Rt.localScale = new Vector3(s, s, 1f);

            // Вспышка урона
            if (_hitA > 0f) { _hitA = Mathf.Max(0f, _hitA - dt * 3f); _hitFlash.color = new Color(1f, 0.25f, 0.15f, _hitA * 0.22f); }
            if (_edge != null && (_edgeA > 0f || _edge.color.a > 0f))
            {
                _edgeA = Mathf.Max(0f, _edgeA - dt * 2.2f);
                _edge.color = new Color(_edgeC.r, _edgeC.g, _edgeC.b, Mathf.Clamp01(_edgeA));
            }

            // Значки силы/брони возвращаются к обычному размеру
            if (_powerBadge != null) _powerBadge.localScale = Vector3.Lerp(_powerBadge.localScale, Vector3.one, 1f - Mathf.Exp(-dt * 10f));
            if (_armorBadge != null) _armorBadge.localScale = Vector3.Lerp(_armorBadge.localScale, Vector3.one, 1f - Mathf.Exp(-dt * 10f));

            // Голо-фольга дышит и разгорается при наведении
            if (_foil != null) _foil.color = new Color(1f, 1f, 1f, _foilA * (0.75f + 0.25f * Mathf.Sin(tt * 0.9f) + 0.8f * _hover));

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

        private void LateUpdate()
        {
            // Перспектива: пока карта наклонена по X/Y (или только что выпрямилась) — перестроить сетки
            bool tilted = _content != null && Perspective3D.Tilted(_content);
            if (tilted || _wasTilted) Perspective3D.Refresh(_graphics);
            _wasTilted = tilted;
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
