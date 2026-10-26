using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using StellarisClone.Core;
using StellarisClone.Rendering;

namespace StellarisClone.Ground
{
    /// <summary>
    /// Интерфейс наземного боя (кодом, в стиле игры — LiquidGlass):
    ///   • сверху — тикеты сторон цветами фракций, таймер, точки захвата;
    ///   • слева сверху — реквизиция, энергия, лимит мест, скорость/пауза;
    ///   • снизу по центру — карточки выделенных отрядов (бойцы точками, HP, мораль, шевроны, состояние) и способности;
    ///   • слева снизу — подкрепления капсулами (B), орбитальный удар (O), особая способность фракции;
    ///   • справа снизу — миникарта с туманом войны (клик — камера);
    ///   • над отрядами — полоски HP и морали, значки «прижат / сломлен / отступает / в укрытии», выкрики;
    ///   • рамка выделения, метки приказов, предпросмотр укрытий под курсором, всплывающие сообщения.
    /// </summary>
    public sealed class GroundHud : MonoBehaviour
    {
        public static bool PointerOverUi => EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();

        private GroundBattleManager _gm;
        private RectTransform _canvas, _world, _cards, _abilities, _reinforce, _minimapDots;
        private Text _ticketA, _ticketD, _timer, _req, _energy, _slots, _speed, _orbitalLabel, _special, _flash;
        private RectTransform _barA, _barD, _pointsRow;
        private Image _dragRect;
        private float _flashUntil;
        private readonly List<WorldTag> _tags = new List<WorldTag>();
        private readonly List<Image> _pointPips = new List<Image>();
        private readonly List<Image> _dots = new List<Image>();
        private readonly List<Image> _coverMarks = new List<Image>();
        private readonly List<(RectTransform rt, Vector3 world, float until, Color c)> _pings = new List<(RectTransform, Vector3, float, Color)>();
        private string _cardsSig;
        private float _visionUntil;
        private int _visionSide = -1;
        private readonly List<LineRenderer> _visionLines = new List<LineRenderer>();
        private Button _orbitalBtn, _specialBtn;
        private bool _reinforceOpen = true;

        private static readonly Color CPanel = new Color(0.02f, 0.05f, 0.07f, 0.9f);
        private static Color Muted => UIManager.DS.TextMuted;

        private sealed class WorldTag
        {
            public RectTransform Root;
            public RectTransform Hp, Morale;
            public Text State, Callout, Chevrons;
            public Image Bracket;
        }

        public static GroundHud Create(GroundBattleManager gm)
        {
            var go = new GameObject("[Ground] HUD");
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 300;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            go.AddComponent<GraphicRaycaster>();
            var hud = go.AddComponent<GroundHud>();
            hud._gm = gm;
            hud._canvas = (RectTransform)go.transform;
            hud.Build();
            return hud;
        }

        public void Dispose()
        {
            foreach (var l in _visionLines) if (l != null) Destroy(l.gameObject);
            Destroy(gameObject);
        }

        // ================================================================ Построение

        private RectTransform Panel(string name, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size, Color rim)
        {
            var img = LGBuild.Panel(_canvas, name, CPanel, raycast: true);
            img.rectTransform.At(anchor, pivot, pos, size);
            LG.Platter(img.gameObject, 14f).SetRim(rim);
            return img.rectTransform;
        }

        private void Build()
        {
            _world = LGBuild.Rect(_canvas, "WorldTags");
            _world.Stretch();

            var a = _gm.Sides[0];
            var d = _gm.Sides[1];

            // ---- Тикеты ----
            var top = Panel("Tickets", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -14f), new Vector2(640f, 86f), new Color(1f, 1f, 1f, 0.25f));
            _ticketA = LGBuild.Label(top, "", 13, Color.Lerp(a.Color, Color.white, 0.4f), TextAnchor.UpperLeft, bold: true);
            _ticketA.rectTransform.Stretch(18, 0, 0, 10);
            _ticketD = LGBuild.Label(top, "", 13, Color.Lerp(d.Color, Color.white, 0.4f), TextAnchor.UpperRight, bold: true);
            _ticketD.rectTransform.Stretch(0, 0, 18, 10);
            _timer = LGBuild.Label(top, "", 22, Color.white, TextAnchor.UpperCenter, bold: true);
            _timer.rectTransform.Stretch(0, 0, 0, 6);
            var barHost = LGBuild.Rect(top, "Bars");
            barHost.anchorMin = new Vector2(0f, 1f); barHost.anchorMax = new Vector2(1f, 1f);
            barHost.offsetMin = new Vector2(18f, -50f); barHost.offsetMax = new Vector2(-18f, -38f);
            var left = LGBuild.Rect(barHost, "A"); left.anchorMin = new Vector2(0f, 0f); left.anchorMax = new Vector2(0.48f, 1f); left.offsetMin = left.offsetMax = Vector2.zero;
            var right = LGBuild.Rect(barHost, "D"); right.anchorMin = new Vector2(0.52f, 0f); right.anchorMax = new Vector2(1f, 1f); right.offsetMin = right.offsetMax = Vector2.zero;
            _barA = LGBuild.Bar(left, a.Color, 1f, 10f);
            _barD = LGBuild.Bar(right, d.Color, 1f, 10f);
            // Полоса защитника заполняется справа налево
            _barD.anchorMin = new Vector2(0f, 0f);
            _pointsRow = LGBuild.Rect(top, "Points");
            _pointsRow.anchorMin = new Vector2(0.5f, 0f); _pointsRow.anchorMax = new Vector2(0.5f, 0f);
            _pointsRow.pivot = new Vector2(0.5f, 0f);
            _pointsRow.sizeDelta = new Vector2(400f, 22f);
            _pointsRow.anchoredPosition = new Vector2(0f, 8f);
            var hl = _pointsRow.gameObject.AddComponent<HorizontalLayoutGroup>();
            hl.spacing = 10f; hl.childAlignment = TextAnchor.MiddleCenter; hl.childControlWidth = false; hl.childControlHeight = false;
            foreach (var p in _gm.Map.Points)
            {
                var pip = LGBuild.Panel(_pointsRow, "Pip", Color.white, raycast: true);
                pip.rectTransform.sizeDelta = new Vector2(18f, 18f);
                LG.Chip(pip.gameObject, new Color(1f, 1f, 1f, 0.4f));
                TooltipHelper.Attach(pip.gameObject, $"<b>{p.Name}</b>");
                _pointPips.Add(pip);
            }

            // ---- Ресурсы ----
            var res = Panel("Resources", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(14f, -14f), new Vector2(330f, 112f), new Color(a.Color.r, a.Color.g, a.Color.b, 0.35f));
            var title = LGBuild.Label(res, Loc.F("ШТУРМ · {0}", _gm.Setup.PlanetName.ToUpper()), 11, Gold, TextAnchor.UpperLeft, bold: true);
            title.rectTransform.Stretch(16, 0, 10, 10);
            title.Track(1.4f);
            _req = Line(res, LGIcon.Minerals, new Color(0.55f, 0.85f, 1f), 34f);
            _energy = Line(res, LGIcon.Energy, new Color(1f, 0.85f, 0.3f), 58f);
            _slots = Line(res, LGIcon.Population, new Color(0.6f, 1f, 0.7f), 82f);
            _speed = LGBuild.Label(res, "", 11, Muted, TextAnchor.LowerRight, bold: true);
            _speed.rectTransform.Stretch(0, 10, 14, 0);

            // ---- Кнопка «Отозвать десант» ----
            var quit = LGBuild.Button(_canvas, "Withdraw", UIManager.DS.BtnNeutral, new Color(1f, 0.45f, 0.45f, 0.5f), ConfirmWithdraw, LGIcon.Back, Loc.T("ОТОЗВАТЬ ДЕСАНТ"), 11);
            ((RectTransform)quit.transform).At(new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-14f, -14f), new Vector2(190f, 38f));
            TooltipHelper.Attach(quit.gameObject, Loc.T("Прекратить штурм: бой будет проигран, осада сброшена"));

            // ---- Подкрепления и способности флота ----
            _reinforce = Panel("Reinforce", new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(14f, 14f), new Vector2(300f, 420f), new Color(1f, 1f, 1f, 0.2f));
            BuildReinforce();

            // ---- Миникарта ----
            var mm = Panel("Minimap", new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-14f, 14f), new Vector2(240f, 240f), new Color(1f, 1f, 1f, 0.25f));
            var mmBg = LGBuild.Panel(mm, "Ground", Color.Lerp(_gm.Map.Biome.GroundB, Color.black, 0.55f), raycast: true);
            mmBg.rectTransform.Stretch(10, 10, 10, 10);
            mmBg.gameObject.AddComponent<MinimapClick>().Hud = this;
            _minimapDots = mmBg.rectTransform;

            // ---- Карточки отрядов ----
            _cards = LGBuild.Rect(_canvas, "Cards");
            _cards.At(new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 14f), new Vector2(980f, 96f));
            var cl = _cards.gameObject.AddComponent<HorizontalLayoutGroup>();
            cl.spacing = 8f; cl.childAlignment = TextAnchor.LowerCenter; cl.childControlWidth = false; cl.childControlHeight = false;
            _abilities = LGBuild.Rect(_canvas, "Abilities");
            _abilities.At(new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 118f), new Vector2(560f, 46f));
            var al = _abilities.gameObject.AddComponent<HorizontalLayoutGroup>();
            al.spacing = 8f; al.childAlignment = TextAnchor.MiddleCenter; al.childControlWidth = false; al.childControlHeight = false;

            // ---- Сообщения и рамка ----
            _flash = LGBuild.Label(_canvas, "", 18, Color.white, TextAnchor.MiddleCenter, bold: true);
            _flash.rectTransform.At(new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -112f), new Vector2(900f, 30f));
            _dragRect = LGBuild.Panel(_canvas, "Drag", new Color(0.4f, 1f, 0.7f, 0.12f));
            _dragRect.rectTransform.pivot = Vector2.zero;
            _dragRect.rectTransform.anchorMin = _dragRect.rectTransform.anchorMax = Vector2.zero;
            var outline = _dragRect.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0.4f, 1f, 0.7f, 0.8f);
            _dragRect.enabled = false;

            if (PlayerPrefs.GetInt("ground.tutorial", 0) == 0) StartCoroutine(Tutorial());
        }

        private static Color Gold => UIManager.DS.Gold;

        private Text Line(RectTransform parent, LGIcon icon, Color c, float top)
        {
            var ic = LGIcons.Create(parent, icon, 16, c);
            ic.rectTransform.At(new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(16f, -top), new Vector2(16f, 16f));
            var t = LGBuild.Label(parent, "", 13, Color.white, TextAnchor.UpperLeft, bold: true);
            t.rectTransform.At(new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(40f, -top + 1f), new Vector2(280f, 20f));
            return t;
        }

        private void BuildReinforce()
        {
            LGBuild.Clear(_reinforce);
            var player = _gm.Player;
            if (player == null) { _reinforce.gameObject.SetActive(false); return; }
            var cap = LGBuild.Label(_reinforce, Loc.T("ПОДКРЕПЛЕНИЯ  ·  B"), 10, Muted, TextAnchor.UpperLeft, bold: true);
            cap.rectTransform.Stretch(14, 0, 0, 10);
            cap.Track(1.6f);
            float y = -30f;
            foreach (var def in UnitCatalog.Buyable(player.FactionKey))
            {
                var d = def;
                string cost = d.Role == UnitRole.Mercenary ? Loc.F("{0:0} гелия-3", UnitCatalog.MercenaryHelium)
                            : d.Energy > 0 ? Loc.F("{0} · {1} эн.", d.Requisition, d.Energy) : d.Requisition.ToString();
                var b = LGBuild.Button(_reinforce, "Buy", UIManager.DS.BtnNeutral, new Color(player.Color.r, player.Color.g, player.Color.b, 0.5f),
                    () => { GroundInput.BeginReinforce(d); }, null, null, 11);
                var rt = (RectTransform)b.transform;
                rt.anchorMin = new Vector2(0f, 1f); rt.anchorMax = new Vector2(1f, 1f); rt.pivot = new Vector2(0.5f, 1f);
                rt.offsetMin = new Vector2(10f, y - 30f); rt.offsetMax = new Vector2(-10f, y);
                var ic = LGIcons.Create(rt, IconOf(d), 16, Color.white);
                ic.rectTransform.At(new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(10f, 0f), new Vector2(16f, 16f));
                var n = LGBuild.Label(rt, d.Name, 11, Color.white, TextAnchor.MiddleLeft, bold: true);
                n.rectTransform.Stretch(34, 0, 90, 0);
                var c = LGBuild.Label(rt, cost, 10, Gold, TextAnchor.MiddleRight, bold: true);
                c.rectTransform.Stretch(0, 0, 10, 0);
                TooltipHelper.Attach(b.gameObject, $"<b>{d.Name}</b>\n{d.Desc}\n<color=#8AA2A8>{Loc.F("Мест: {0} · бойцов: {1}", d.Slots, d.Size)}</color>\n{Loc.T("ЛКМ по земле — место капсулы (у маяка или своей точки)")}");
                y -= 34f;
            }
            y -= 6f;
            _orbitalBtn = LGBuild.Button(_reinforce, "Orbital", UIManager.DS.BtnNeutral, new Color(1f, 0.35f, 0.3f, 0.6f), () => GroundInput.BeginOrbital(), LGIcon.Target, null, 11);
            var ort = (RectTransform)_orbitalBtn.transform;
            ort.anchorMin = new Vector2(0f, 1f); ort.anchorMax = new Vector2(1f, 1f); ort.pivot = new Vector2(0.5f, 1f);
            ort.offsetMin = new Vector2(10f, y - 34f); ort.offsetMax = new Vector2(-10f, y);
            _orbitalLabel = LGBuild.Label(ort, "", 11, Color.white, TextAnchor.MiddleCenter, bold: true);
            TooltipHelper.Attach(_orbitalBtn.gameObject, Loc.F("<b>Орбитальный удар</b>  (O)\nФлот бьёт по точке: прицел 3 с, столб света, воронка. {0:0} энергии, перезарядка {1:0} с",
                GroundBattleManager.OrbitalEnergy, GroundBattleManager.OrbitalCooldown));
            y -= 40f;
            string special = SpecialName(player.FactionKey);
            if (special != null)
            {
                _specialBtn = LGBuild.Button(_reinforce, "Special", UIManager.DS.BtnNeutral, new Color(player.Color.r, player.Color.g, player.Color.b, 0.6f),
                    () => _gm.UseFactionSpecial(player), LGIcon.Leader, null, 11);
                var srt = (RectTransform)_specialBtn.transform;
                srt.anchorMin = new Vector2(0f, 1f); srt.anchorMax = new Vector2(1f, 1f); srt.pivot = new Vector2(0.5f, 1f);
                srt.offsetMin = new Vector2(10f, y - 34f); srt.offsetMax = new Vector2(-10f, y);
                _special = LGBuild.Label(srt, "", 11, Color.white, TextAnchor.MiddleCenter, bold: true);
                TooltipHelper.Attach(_specialBtn.gameObject, SpecialTip(player.FactionKey));
                y -= 40f;
            }
            _reinforce.sizeDelta = new Vector2(300f, -y + 10f);
        }

        public static string SpecialName(string key) => key switch
        {
            "astrea" => Loc.T("ПОДНЯТЬ ОПОЛЧЕНИЕ"),
            _ => null
        };

        private static string SpecialTip(string key) => key switch
        {
            "astrea" => Loc.T("<b>Ополчение Республики</b>\nНа своей точке (ближайшей к камере) бесплатно поднимается отряд ополчения. Перезарядка 90 с"),
            _ => ""
        };

        public void ToggleReinforcements()
        {
            _reinforceOpen = !_reinforceOpen;
            _reinforce.gameObject.SetActive(_reinforceOpen);
        }

        public static LGIcon IconOf(UnitDef d) => d.Role switch
        {
            UnitRole.Assault => LGIcon.Swords,
            UnitRole.Heavy => LGIcon.ArmorPlate,
            UnitRole.MachineGun => LGIcon.Cannon,
            UnitRole.Rocket => LGIcon.Missile,
            UnitRole.Sniper => LGIcon.Target,
            UnitRole.Flamer => LGIcon.Reactor,
            UnitRole.Militia => LGIcon.Population,
            UnitRole.Apc => LGIcon.Corvette,
            UnitRole.Tank => LGIcon.Destroyer,
            UnitRole.Artillery => LGIcon.Siege,
            UnitRole.Hero => LGIcon.Leader,
            UnitRole.Walker => LGIcon.Siege,
            UnitRole.Swarm => LGIcon.Sensors,
            UnitRole.Seer => LGIcon.Influence,
            UnitRole.Mercenary => LGIcon.Trade,
            _ => LGIcon.Swords
        };

        // ================================================================ Обновление

        public void Tick()
        {
            if (_gm == null || _gm.Map == null) return;
            var a = _gm.Sides[0];
            var d = _gm.Sides[1];
            _ticketA.text = Loc.F("ДЕСАНТ  {0:0}", Mathf.Max(0f, a.Tickets));
            _ticketD.text = Loc.F("{0:0}  ОБОРОНА", Mathf.Max(0f, d.Tickets));
            LGBuild.SetBar(_barA, a.Tickets / 500f);
            LGBuild.SetBar(_barD, d.Tickets / 500f);
            float left = Mathf.Max(0f, GroundBattleManager.TimeLimit - _gm.Elapsed);
            _timer.text = $"{(int)(left / 60f):00}:{(int)(left % 60f):00}";
            for (int i = 0; i < _pointPips.Count && i < _gm.Map.Points.Count; i++)
            {
                var p = _gm.Map.Points[i];
                _pointPips[i].color = p.Owner >= 0 ? _gm.Sides[p.Owner].Color : Color.Lerp(new Color(0.5f, 0.5f, 0.5f), _gm.Sides[p.Progress > 0 ? 0 : 1].Color, Mathf.Abs(p.Progress));
            }

            var pl = _gm.Player;
            if (pl != null)
            {
                float reqRate = 1f, enRate = 0.5f;
                foreach (var p in _gm.Map.Points) if (p.Owner == pl.Index) { reqRate += p.ReqIncome; enRate += p.EnergyIncome; }
                _req.text = Loc.F("Реквизиция  {0:0}  <color=#7CF29A>+{1:0.#}/с</color>", pl.Requisition, reqRate * pl.IncomeMult);
                _energy.text = Loc.F("Энергия  {0:0}  <color=#7CF29A>+{1:0.#}/с</color>", pl.Energy, enRate * pl.IncomeMult);
                _slots.text = Loc.F("Места  {0} / {1}", pl.SlotsUsed, pl.SlotLimit);
                if (_orbitalLabel != null)
                    _orbitalLabel.text = pl.OrbitalCooldown > 0f ? Loc.F("ОРБИТАЛЬНЫЙ УДАР · {0:0} с", pl.OrbitalCooldown)
                                       : pl.Energy < GroundBattleManager.OrbitalEnergy ? Loc.F("ОРБИТАЛЬНЫЙ УДАР · нужно {0:0} эн.", GroundBattleManager.OrbitalEnergy)
                                       : Loc.T("ОРБИТАЛЬНЫЙ УДАР  ·  O");
                if (_special != null)
                    _special.text = pl.SpecialCooldown > 0f ? Loc.F("{0} · {1:0} с", SpecialName(pl.FactionKey), pl.SpecialCooldown) : SpecialName(pl.FactionKey);
            }
            _speed.text = _gm.Paused ? Loc.T("ПАУЗА  ·  пробел") : Mathf.Approximately(_gm.SpeedMult, 1f) ? "" : Loc.F("СКОРОСТЬ ×{0:0.#}", _gm.SpeedMult);

            if (Time.unscaledTime > _flashUntil && _flash.text.Length > 0) _flash.text = "";
            else if (_flash.text.Length > 0) _flash.color = new Color(_flash.color.r, _flash.color.g, _flash.color.b, Mathf.Clamp01((_flashUntil - Time.unscaledTime) * 1.5f));

            UpdateCards();
            UpdateWorldTags();
            UpdateMinimap();
            UpdateDrag();
            UpdateCoverPreview();
            UpdatePings();
            UpdateVision();
        }

        public void Flash(string text, Color c)
        {
            if (_flash == null) return;
            _flash.text = text;
            _flash.color = Color.Lerp(c, Color.white, 0.4f);
            _flashUntil = Time.unscaledTime + 3f;
        }

        // ---- Карточки ----

        private void UpdateCards()
        {
            var sel = GroundInput.Selected;
            var sb = new System.Text.StringBuilder();
            foreach (var s in sel) sb.Append(s.Id).Append(':').Append(s.Alive).Append(s.Veterancy).Append(',');
            string sig = sb.ToString();
            if (sig != _cardsSig)
            {
                _cardsSig = sig;
                LGBuild.Clear(_cards);
                LGBuild.Clear(_abilities);
                int n = 0;
                foreach (var s in sel) { if (n++ >= 9) break; BuildCard(s); }
                if (sel.Count > 0) BuildAbilities(sel[0]);
            }
            // Живые значения
            for (int i = 0; i < _cards.childCount && i < sel.Count; i++)
            {
                var card = _cards.GetChild(i);
                var s = sel[i];
                var hp = card.Find("Hp/BarTrack/BarFill") as RectTransform;
                var mo = card.Find("Mo/BarTrack/BarFill") as RectTransform;
                if (hp != null) LGBuild.SetBar(hp, s.Health01);
                if (mo != null) LGBuild.SetBar(mo, s.MaxMorale <= 0f ? 0f : s.Morale / s.MaxMorale, s.Broken ? new Color(1f, 0.4f, 0.3f) : new Color(0.95f, 0.85f, 0.4f));
                var st = card.Find("State")?.GetComponent<Text>();
                if (st != null) st.text = StateText(s);
            }
            for (int i = 0; i < _abilities.childCount; i++)
            {
                var t = _abilities.GetChild(i).Find("Cd")?.GetComponent<Text>();
                if (t != null && sel.Count > 0) t.text = sel[0].AbilityCooldown > 0f ? $"{sel[0].AbilityCooldown:0}" : "";
            }
        }

        private void BuildCard(Squad s)
        {
            var card = LGBuild.Panel(_cards, "Card", CPanel, raycast: true);
            card.rectTransform.sizeDelta = new Vector2(104f, 92f);
            LG.Platter(card.gameObject, 12f).SetRim(new Color(s.Side.Color.r, s.Side.Color.g, s.Side.Color.b, 0.55f));
            var ic = LGIcons.Create(card.transform, IconOf(s.Def), 20, Color.white);
            ic.rectTransform.At(new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(8f, -8f), new Vector2(20f, 20f));
            var name = LGBuild.Label(card.transform, s.Def.Name, 9, Color.white, TextAnchor.UpperLeft, bold: true, wrap: true);
            name.rectTransform.Stretch(32, 0, 4, 6);
            name.rectTransform.offsetMin = new Vector2(32f, 58f);
            string chev = s.Veterancy > 0 ? new string('^', s.Veterancy) : "";
            var vet = LGBuild.Label(card.transform, chev, 13, Gold, TextAnchor.UpperRight, bold: true);
            vet.rectTransform.Stretch(0, 0, 6, 2);
            // Бойцы точками
            string dots = s.Def.Vehicle ? "" : new string('•', Mathf.Clamp(s.Alive, 0, 20)) + "<color=#33444A>" + new string('•', Mathf.Clamp(s.Def.Size - s.Alive, 0, 20)) + "</color>";
            var dl = LGBuild.Label(card.transform, dots, 11, Color.Lerp(s.Side.Color, Color.white, 0.5f), TextAnchor.UpperLeft);
            dl.rectTransform.Stretch(8, 0, 4, 34);
            var hpHost = LGBuild.Rect(card.transform, "Hp");
            hpHost.anchorMin = new Vector2(0f, 0f); hpHost.anchorMax = new Vector2(1f, 0f);
            hpHost.offsetMin = new Vector2(8f, 26f); hpHost.offsetMax = new Vector2(-8f, 32f);
            LGBuild.Bar(hpHost, new Color(0.4f, 1f, 0.55f), s.Health01, 5f);
            var moHost = LGBuild.Rect(card.transform, "Mo");
            moHost.anchorMin = new Vector2(0f, 0f); moHost.anchorMax = new Vector2(1f, 0f);
            moHost.offsetMin = new Vector2(8f, 18f); moHost.offsetMax = new Vector2(-8f, 24f);
            LGBuild.Bar(moHost, new Color(0.95f, 0.85f, 0.4f), s.Morale / Mathf.Max(1f, s.MaxMorale), 5f);
            var st = LGBuild.Label(card.transform, "", 8, Muted, TextAnchor.LowerLeft, bold: true);
            st.name = "State";
            st.rectTransform.Stretch(8, 3, 4, 0);
            var btn = card.gameObject.AddComponent<Button>();
            var sq = s;
            btn.onClick.AddListener(() => { GroundInput.Select(sq, false); _gm.Cam.LookAt(sq.Center); });
            TooltipHelper.Attach(card.gameObject, $"<b>{s.Def.Name}</b>\n{s.Def.Desc}\n<color=#8AA2A8>{Loc.F("Убито врагов: {0}", s.Kills)}</color>");
        }

        private void BuildAbilities(Squad s)
        {
            string[] keys = { "Z", "X", "C" };
            for (int i = 0; i < s.Def.Abilities.Length && i < 3; i++)
            {
                string id = s.Def.Abilities[i];
                int slot = i;
                var b = LGBuild.Button(_abilities, "Ab", UIManager.DS.BtnNeutral, new Color(1f, 0.8f, 0.4f, 0.5f), () =>
                {
                    if (Abilities.Instant(id)) Abilities.UseAt(GroundInput.Selected, id, s.Center, null);
                    else GroundInput.BeginAbilityTargeting(id);
                }, null, null, 11);
                ((RectTransform)b.transform).sizeDelta = new Vector2(150f, 40f);
                var l = LGBuild.Label(b.transform, $"{Abilities.Name(id)}  <color=#8AA2A8>{keys[slot]}</color>", 11, Color.white, TextAnchor.MiddleCenter, bold: true);
                var cd = LGBuild.Label(b.transform, "", 11, new Color(1f, 0.6f, 0.5f), TextAnchor.MiddleRight, bold: true);
                cd.name = "Cd";
                cd.rectTransform.Stretch(0, 0, 8, 0);
                TooltipHelper.Attach(b.gameObject, $"<b>{Abilities.Name(id)}</b>\n{Abilities.Tip(id)}");
            }
            var r = LGBuild.Button(_abilities, "Retreat", UIManager.DS.BtnNeutral, new Color(1f, 0.45f, 0.45f, 0.5f), () =>
            {
                foreach (var x in GroundInput.Selected) x.OrderRetreat();
            }, null, null, 11);
            ((RectTransform)r.transform).sizeDelta = new Vector2(150f, 40f);
            LGBuild.Label(r.transform, Loc.T("ОТСТУПИТЬ  <color=#8AA2A8>T</color>"), 11, Color.white, TextAnchor.MiddleCenter, bold: true);
            TooltipHelper.Attach(r.gameObject, Loc.T("<b>Отступление</b>\nОтряд бегом уходит к точке высадки, получает вдвое меньше урона и не стреляет. Так сохраняют ветеранов"));
        }

        private static string StateText(Squad s)
        {
            if (s.Retreating) return Loc.T("ОТСТУПАЕТ");
            if (s.Broken) return Loc.T("СЛОМЛЕН");
            if (s.Pinned) return Loc.T("ПРИЖАТ");
            if (Time.time < s.RootedUntil) return Loc.T("В КОРНЯХ");
            if (s.InCover) return Loc.T("В УКРЫТИИ");
            if (s.Engaged != null) return Loc.T("В БОЮ");
            return "";
        }

        // ---- Метки над отрядами ----

        private void UpdateWorldTags()
        {
            var cam = _gm.Cam?.Cam;
            if (cam == null) return;
            var pl = _gm.Player;
            int i = 0;
            float scale = _canvas.localScale.x;
            foreach (var side in _gm.Sides)
                foreach (var s in side.Squads)
                {
                    if (s.Destroyed) continue;
                    if (pl != null && side != pl && !s.VisibleTo[pl.Index]) continue;
                    var leader = s.Leader;
                    if (leader == null) continue;
                    var sp = cam.WorldToScreenPoint(s.Center + Vector3.up * (leader.Rig.Height + 1.2f));
                    if (sp.z < 0f || sp.x < -50f || sp.x > Screen.width + 50f || sp.y < -50f || sp.y > Screen.height + 50f) continue;
                    var tag = GetTag(i++);
                    tag.Root.gameObject.SetActive(true);
                    tag.Root.anchoredPosition = new Vector2(sp.x, sp.y) / Mathf.Max(0.01f, scale);
                    LGBuild.SetBar(tag.Hp, s.Health01, side.Color);
                    LGBuild.SetBar(tag.Morale, s.MaxMorale <= 0f ? 0f : s.Morale / s.MaxMorale, s.Broken ? new Color(1f, 0.4f, 0.3f) : new Color(0.95f, 0.85f, 0.4f));
                    tag.Morale.parent.parent.gameObject.SetActive(!s.Def.Fearless);
                    string state = s.Retreating ? Loc.T("◀ ОТСТУПАЕТ") : s.Broken ? Loc.T("СЛОМЛЕН") : s.Pinned ? Loc.T("▼ ПРИЖАТ") : s.InCover ? "◆" : "";
                    tag.State.text = state;
                    tag.State.color = s.InCover && !s.Pinned && !s.Broken && !s.Retreating ? (CoverQuality(s) >= 2 ? new Color(0.4f, 1f, 0.5f) : new Color(1f, 0.85f, 0.3f)) : new Color(1f, 0.55f, 0.45f);
                    tag.Chevrons.text = s.Veterancy > 0 ? new string('^', s.Veterancy) : "";
                    tag.Callout.text = Time.time < s.CalloutUntil ? s.Callout : "";
                    tag.Bracket.enabled = s.Selected;
                }
            for (; i < _tags.Count; i++) _tags[i].Root.gameObject.SetActive(false);
        }

        private int CoverQuality(Squad s)
        {
            int heavy = 0;
            var scratch = new List<CoverPoint>();
            foreach (var m in s.Members)
                if (m.Cover != null && m.Cover.Kind == CoverKind.Heavy) heavy++;
            return heavy * 2 >= s.Alive ? 2 : 1;
        }

        private WorldTag GetTag(int i)
        {
            if (i < _tags.Count) return _tags[i];
            var root = LGBuild.Rect(_world, "Tag");
            root.anchorMin = root.anchorMax = Vector2.zero;
            root.sizeDelta = new Vector2(64f, 20f);
            var hpHost = LGBuild.Rect(root, "Hp");
            hpHost.anchorMin = new Vector2(0f, 0.5f); hpHost.anchorMax = new Vector2(1f, 0.5f);
            hpHost.offsetMin = new Vector2(0f, 1f); hpHost.offsetMax = new Vector2(0f, 6f);
            var hp = LGBuild.Bar(hpHost, Color.white, 1f, 4f);
            var moHost = LGBuild.Rect(root, "Mo");
            moHost.anchorMin = new Vector2(0f, 0.5f); moHost.anchorMax = new Vector2(1f, 0.5f);
            moHost.offsetMin = new Vector2(0f, -5f); moHost.offsetMax = new Vector2(0f, 0f);
            var mo = LGBuild.Bar(moHost, Color.yellow, 1f, 3f);
            var state = LGBuild.Label(root, "", 10, Color.white, TextAnchor.LowerCenter, bold: true);
            state.rectTransform.At(new Vector2(0.5f, 1f), new Vector2(0.5f, 0f), new Vector2(0f, 2f), new Vector2(160f, 16f));
            var callout = LGBuild.Label(root, "", 13, Color.white, TextAnchor.LowerCenter, bold: true);
            callout.rectTransform.At(new Vector2(0.5f, 1f), new Vector2(0.5f, 0f), new Vector2(0f, 18f), new Vector2(320f, 20f));
            var chev = LGBuild.Label(root, "", 12, Gold, TextAnchor.MiddleLeft, bold: true);
            chev.rectTransform.At(new Vector2(1f, 0.5f), new Vector2(0f, 0.5f), new Vector2(4f, 0f), new Vector2(30f, 16f));
            var bracket = LGBuild.Panel(root, "Sel", new Color(0.4f, 1f, 0.7f, 0f));
            bracket.rectTransform.At(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(74f, 28f));
            var ol = bracket.gameObject.AddComponent<Outline>();
            ol.effectColor = new Color(0.4f, 1f, 0.7f, 0.9f);
            ol.effectDistance = new Vector2(1.5f, 1.5f);
            var tag = new WorldTag { Root = root, Hp = hp, Morale = mo, State = state, Callout = callout, Chevrons = chev, Bracket = bracket };
            _tags.Add(tag);
            return tag;
        }

        // ---- Миникарта ----

        private void UpdateMinimap()
        {
            var r = _minimapDots.rect;
            int i = 0;
            Vector2 ToMap(Vector3 w)
            {
                var l = _gm.Map.ToLocal(w);
                return new Vector2((l.x / (GroundMap.Half * 2f) + 0.5f) * r.width, (l.z / (GroundMap.Half * 2f) + 0.5f) * r.height);
            }
            Image Dot(Vector2 p, Color c, float size)
            {
                Image img;
                if (i < _dots.Count) img = _dots[i];
                else
                {
                    img = LGBuild.Panel(_minimapDots, "Dot", Color.white);
                    img.rectTransform.anchorMin = img.rectTransform.anchorMax = Vector2.zero;
                    img.raycastTarget = false;
                    _dots.Add(img);
                }
                i++;
                img.enabled = true;
                img.color = c;
                img.rectTransform.sizeDelta = new Vector2(size, size);
                img.rectTransform.anchoredPosition = p;
                return img;
            }
            foreach (var p in _gm.Map.Points)
                Dot(ToMap(p.Pos), p.Owner >= 0 ? _gm.Sides[p.Owner].Color : new Color(0.7f, 0.7f, 0.7f), 12f);
            if (_gm.Map.Hq != null && !_gm.Map.Hq.Dead) Dot(ToMap(_gm.Map.Hq.Pos), _gm.Sides[1].Color * 0.8f, 16f);
            var pl = _gm.Player;
            foreach (var side in _gm.Sides)
                foreach (var s in side.Squads)
                {
                    if (s.Destroyed || pl != null && side != pl && !s.VisibleTo[pl.Index]) continue;
                    Dot(ToMap(s.Center), Color.Lerp(side.Color, Color.white, s.Selected ? 0.6f : 0.15f), s.Def.Vehicle ? 7f : 5f);
                }
            Dot(ToMap(_gm.Cam.Focus), new Color(1f, 1f, 1f, 0.5f), 8f);
            for (; i < _dots.Count; i++) _dots[i].enabled = false;
        }

        /// <summary>Клик по миникарте — камера туда (ПКМ — приказ выделенным).</summary>
        public void MinimapClick(Vector2 local01, bool right)
        {
            var w = _gm.Map.ToWorld((local01.x - 0.5f) * GroundMap.Half * 2f, (local01.y - 0.5f) * GroundMap.Half * 2f);
            if (right) foreach (var s in GroundInput.Selected) s.OrderMove(w, false);
            else _gm.Cam.LookAt(w);
        }

        // ---- Рамка, укрытия, метки приказов ----

        private void UpdateDrag()
        {
            _dragRect.enabled = GroundInput.IsDragging;
            if (!GroundInput.IsDragging) return;
            float s = Mathf.Max(0.01f, _canvas.localScale.x);
            var r = GroundInput.DragRect;
            _dragRect.rectTransform.anchoredPosition = new Vector2(r.x, r.y) / s;
            _dragRect.rectTransform.sizeDelta = new Vector2(r.width, r.height) / s;
        }

        private readonly List<CoverPoint> _coverScratch = new List<CoverPoint>();

        private void UpdateCoverPreview()
        {
            int i = 0;
            if (GroundInput.Selected.Count > 0 && GroundInput.Targeting == GroundInput.Mode.None && !PointerOverUi && _gm.Cam.GroundUnderMouse(out var p))
            {
                _gm.Map.Cover.Query(p, 8f, _coverScratch);
                float s = Mathf.Max(0.01f, _canvas.localScale.x);
                foreach (var c in _coverScratch)
                {
                    if (!c.Free || i >= 24) continue;
                    var sp = _gm.Cam.Cam.WorldToScreenPoint(c.Pos + Vector3.up * 0.2f);
                    if (sp.z < 0f) continue;
                    Image img;
                    if (i < _coverMarks.Count) img = _coverMarks[i];
                    else
                    {
                        img = LGBuild.Panel(_world, "Cover", Color.white);
                        img.rectTransform.anchorMin = img.rectTransform.anchorMax = Vector2.zero;
                        img.rectTransform.sizeDelta = new Vector2(9f, 9f);
                        img.raycastTarget = false;
                        LG.Chip(img.gameObject, new Color(1f, 1f, 1f, 0.3f), 4.5f);
                        _coverMarks.Add(img);
                    }
                    i++;
                    img.enabled = true;
                    img.color = c.Kind == CoverKind.Heavy ? new Color(0.35f, 1f, 0.45f, 0.9f) : c.Kind == CoverKind.Light ? new Color(1f, 0.85f, 0.25f, 0.9f) : new Color(1f, 0.3f, 0.25f, 0.9f);
                    img.rectTransform.anchoredPosition = new Vector2(sp.x, sp.y) / s;
                }
            }
            for (; i < _coverMarks.Count; i++) _coverMarks[i].enabled = false;
        }

        public void Ping(Vector3 world, Color c)
        {
            var img = LGBuild.Panel(_world, "Ping", new Color(c.r, c.g, c.b, 0f));
            img.raycastTarget = false;
            img.rectTransform.anchorMin = img.rectTransform.anchorMax = Vector2.zero;
            var ol = img.gameObject.AddComponent<Outline>();
            ol.effectColor = c;
            ol.effectDistance = new Vector2(2f, 2f);
            _pings.Add((img.rectTransform, world, Time.unscaledTime + 0.6f, c));
        }

        private void UpdatePings()
        {
            float s = Mathf.Max(0.01f, _canvas.localScale.x);
            for (int i = _pings.Count - 1; i >= 0; i--)
            {
                var p = _pings[i];
                if (p.rt == null || Time.unscaledTime > p.until) { if (p.rt != null) Destroy(p.rt.gameObject); _pings.RemoveAt(i); continue; }
                float k = 1f - (p.until - Time.unscaledTime) / 0.6f;
                var sp = _gm.Cam.Cam.WorldToScreenPoint(p.world);
                p.rt.anchoredPosition = new Vector2(sp.x, sp.y) / s;
                p.rt.sizeDelta = Vector2.one * Mathf.Lerp(10f, 46f, k);
                var ol = p.rt.GetComponent<Outline>();
                if (ol != null) ol.effectColor = new Color(p.c.r, p.c.g, p.c.b, 1f - k);
            }
        }

        // ---- Видение Провидца: пунктир маршрутов врага ----

        public void BeginVision(int side, float seconds)
        {
            _visionSide = side;
            _visionUntil = Time.time + seconds;
            _gm.RevealAllFor(side, seconds);
        }

        private void UpdateVision()
        {
            bool on = _visionSide >= 0 && Time.time < _visionUntil;
            int i = 0;
            if (on)
                foreach (var s in _gm.Sides[1 - _visionSide].Squads)
                {
                    if (s.Destroyed) continue;
                    Vector3 to = s.AttackTarget != null ? s.AttackTarget.Center : s.Destination;
                    if ((to - s.Center).sqrMagnitude < 9f) continue;
                    LineRenderer lr;
                    if (i < _visionLines.Count) lr = _visionLines[i];
                    else
                    {
                        lr = new GameObject("VisionPath").AddComponent<LineRenderer>();
                        lr.sharedMaterial = GroundMats.Additive;
                        lr.positionCount = 2;
                        lr.startWidth = lr.endWidth = 0.35f;
                        lr.textureMode = LineTextureMode.Tile;
                        _visionLines.Add(lr);
                    }
                    i++;
                    lr.enabled = true;
                    lr.SetPosition(0, s.Center + Vector3.up * 0.5f);
                    lr.SetPosition(1, to + Vector3.up * 0.5f);
                    Color c = new Color(0.85f, 0.55f, 1f, 0.6f + 0.3f * Mathf.Sin(Time.time * 6f)) * 1.5f;
                    lr.startColor = lr.endColor = c;
                }
            for (; i < _visionLines.Count; i++) _visionLines[i].enabled = false;
        }

        // ---- Отзыв десанта и обучение ----

        private void ConfirmWithdraw()
        {
            ResultScreen.Confirm(Loc.T("Отозвать десант?"), Loc.T("Штурм будет проигран: осада сбросится, флот потеряет часть корпуса."), () => _gm.Surrender());
        }

        private System.Collections.IEnumerator Tutorial()
        {
            string[] hints =
            {
                Loc.T("Укрытия решают бой: зелёные метки — тяжёлые (−60% урона), жёлтые — лёгкие. Остановившийся отряд сам занимает ближайшие."),
                Loc.T("T — отступление: отряд бежит к маяку, получает вдвое меньше урона и потом пополняется. Так сохраняют ветеранов."),
                Loc.T("Держите точки: у кого их меньше, тот теряет тикеты. Точки дают реквизицию на подкрепления (B)."),
            };
            yield return new WaitForSecondsRealtime(4f);
            foreach (var h in hints)
            {
                NotificationCenter.Show(Loc.T("Наземный бой"), h, NotificationCenter.Kind.Info, 9f);
                Flash(h, Color.white);
                _flashUntil = Time.unscaledTime + 8f;
                yield return new WaitForSecondsRealtime(10f);
            }
            PlayerPrefs.SetInt("ground.tutorial", 1);
        }
    }

    /// <summary>Клики по миникарте.</summary>
    public sealed class MinimapClick : MonoBehaviour, IPointerDownHandler
    {
        public GroundHud Hud;

        public void OnPointerDown(PointerEventData e)
        {
            var rt = (RectTransform)transform;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rt, e.position, e.pressEventCamera, out var lp)) return;
            var r = rt.rect;
            Hud?.MinimapClick(new Vector2((lp.x - r.xMin) / r.width, (lp.y - r.yMin) / r.height), e.button == PointerEventData.InputButton.Right);
        }
    }
}
