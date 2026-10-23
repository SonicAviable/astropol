using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using StellarisClone.Rendering;

namespace StellarisClone.Core
{
    /// <summary>
    /// Панель сражения (сверху по центру, пока идёт бой с участием игрока):
    ///   • шапка — где идёт бой, сколько дней, фаза (сближение / огневой контакт), сколько ещё боёв;
    ///   • стороны — название и стойка, состав по классам (живые / в начале боя), щиты и прочность
    ///     со «следом урона» (белая полоса догоняет текущую — видно, сколько только что потеряно), мощь и урон в день;
    ///   • центр — баланс сил и прогноз исхода (кто кого и примерно когда);
    ///   • тактика — стойка и приоритет целей одним кликом (см. CombatManager.Tactics);
    ///   • низ — готовность гиперпривода и итоги, кнопки «Камера» и «Отступить» (пульсирует, если прогноз — поражение);
    ///   • под панелью — лента событий боя: потери, пробитые щиты, приказы; свежие строки вспыхивают и гаснут.
    /// </summary>
    public partial class CombatManager
    {
        private GameObject _hud;
        private int _hudSystem = -1;
        private Text _hudTitle, _hudPhase, _hudDays, _balanceText, _forecastText, _hudFooter;
        private RectTransform _balanceBar;
        private HudSide _hudMe, _hudEnemy;
        private Image[] _stanceBg, _prioBg;
        private LiquidGlassEffect _retreatFx;
        private Text[] _feed;
        private RectTransform _feedRoot;
        private int _feedSeen;

        private static readonly Color CHudDark = new Color(0.07f, 0.11f, 0.14f);
        private static readonly Color CHudOn = new Color(0.62f, 0.20f, 0.18f);

        private class HudSide
        {
            public Text Name, Stats;
            public Text[] ClassText;
            public Image[] ClassIcon;
            public RectTransform Hp, Lag, Shield;
            public float LagValue = 1f;
        }

        private static readonly LGIcon[] ClassIcons = { LGIcon.Corvette, LGIcon.Frigate, LGIcon.Destroyer, LGIcon.Starbase };
        private static readonly string[] ClassNames = { Loc.T("Корветы"), Loc.T("Фрегаты"), Loc.T("Эсминцы"), Loc.T("Звёздная база") };

        private void BuildHud()
        {
            var ui = UIManager.Instance;
            if (ui == null || ui.HudCanvas == null) return;

            var rt = LGBuild.Rect(ui.HudCanvas.transform, "[UI] BattlePanel");
            rt.At(new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -118), new Vector2(800, 236));
            _hud = rt.gameObject;
            var bg = _hud.AddComponent<Image>();
            bg.color = new Color(0.07f, 0.025f, 0.035f, 1f);
            bg.raycastTarget = true;
            LG.Glass(_hud, 22f).SetRim(new Color(1f, 0.38f, 0.38f, 0.6f));
            var mo = LG.Motion(_hud, LGAppear.Kind.SlideDown);
            mo.distance = 24f;

            // Шапка
            var head = LGBuild.Rect(rt, "Head");
            head.TopBand(10, 24, 18, 18);
            _hudTitle = LGBuild.Label(head, "", 14, UIManager.DS.Gold, TextAnchor.MiddleCenter, bold: true);
            var hi = LGIcons.Create(head, LGIcon.Swords, 16, UIManager.DS.Red);
            hi.rectTransform.At(new Vector2(0.5f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-170, 0), new Vector2(16, 16));
            _hudDays = LGBuild.Label(head, "", 11, UIManager.DS.TextMuted, TextAnchor.MiddleLeft, bold: true);
            var phase = LGBuild.Panel(head, "Phase", new Color(0.10f, 0.06f, 0.06f));
            phase.rectTransform.At(new Vector2(1, 0.5f), new Vector2(1, 0.5f), Vector2.zero, new Vector2(150, 22));
            LG.Chip(phase.gameObject, new Color(1f, 0.55f, 0.45f, 0.5f));
            _hudPhase = LGBuild.Label(phase.transform, "", 10, Color.white, TextAnchor.MiddleCenter, bold: true);

            var sep = LGBuild.Panel(rt, "Sep", new Color(1f, 0.45f, 0.40f, 0.22f));
            sep.rectTransform.anchorMin = new Vector2(0, 1);
            sep.rectTransform.anchorMax = new Vector2(1, 1);
            sep.rectTransform.offsetMin = new Vector2(18, -40);
            sep.rectTransform.offsetMax = new Vector2(-18, -39);
            LG.Ignore(sep.gameObject);

            _hudMe = BuildSide(rt, true);
            _hudEnemy = BuildSide(rt, false);

            // Центр: баланс и прогноз
            var center = LGBuild.Rect(rt, "Balance");
            center.At(new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -46), new Vector2(160, 92));
            _balanceText = LGBuild.Label(center, "", 12, Color.white, TextAnchor.UpperCenter, bold: true);
            var bh = LGBuild.Rect(center, "Bar");
            bh.TopBand(22, 10, 0, 0);
            _balanceBar = LGBuild.Bar(bh, FleetIndicator.OwnColor, 0.5f, 8f);
            _forecastText = LGBuild.Label(center, "", 10, Color.white, TextAnchor.UpperCenter, wrap: true);
            _forecastText.rectTransform.Stretch(0, 0, 0, 40);

            BuildTactics(rt);

            // Низ: гиперпривод и итоги, кнопки
            _hudFooter = LGBuild.Label(rt, "", 10, UIManager.DS.TextMuted, TextAnchor.LowerLeft);
            _hudFooter.rectTransform.Stretch(18, 12, 250, 0);
            _hudFooter.lineSpacing = 1.15f;

            var row = LGBuild.Rect(rt, "Buttons");
            row.anchorMin = row.anchorMax = new Vector2(1, 0);
            row.pivot = new Vector2(1, 0);
            row.sizeDelta = new Vector2(230, 32);
            row.anchoredPosition = new Vector2(-14, 10);
            var cam = LGBuild.Button(row, "Camera", UIManager.DS.BtnNeutral, new Color(0.6f, 0.9f, 1f, 0.5f), FocusBattle, LGIcon.Target, Loc.T("КАМЕРА"), 10);
            ((RectTransform)cam.transform).Column(0f, 0.42f, 0, 3);
            var retreat = LGBuild.Button(row, "Retreat", UIManager.DS.BtnDanger, UIManager.DS.Red, RetreatAll, LGIcon.Warning, Loc.T("ОТСТУПИТЬ"), 10);
            ((RectTransform)retreat.transform).Column(0.42f, 1f, 3, 0);
            _retreatFx = retreat.GetComponent<LiquidGlassEffect>();
            TooltipHelper.Attach(retreat.gameObject,
                Loc.T("<b>Экстренное отступление</b>\nКорабли не могут уйти сразу: гиперпривод разгоняется 3 дня боя ") +
                Loc.T("(в «Обороне» — быстрее). После приказа корабли держат строй и прыгают сами, как только ГПД готов ") +
                Loc.T("(80% успеха, сбой — ещё полдня разгона). Аварийный прыжок стоит 10% корпуса."));

            // Лента событий — под панелью, без подложки
            _feedRoot = LGBuild.Rect(rt, "Feed");
            _feedRoot.anchorMin = new Vector2(0.5f, 0f);
            _feedRoot.anchorMax = new Vector2(0.5f, 0f);
            _feedRoot.pivot = new Vector2(0.5f, 1f);
            _feedRoot.anchoredPosition = new Vector2(0, -8);
            _feedRoot.sizeDelta = new Vector2(620, 4 * 18);
            _feed = new Text[4];
            for (int i = 0; i < _feed.Length; i++)
            {
                var t = LGBuild.Label(_feedRoot, "", 12, Color.white, TextAnchor.MiddleCenter, bold: true);
                t.rectTransform.TopBand(i * 18, 18);
                var o = t.gameObject.AddComponent<Outline>();
                o.effectColor = new Color(0f, 0f, 0f, 0.65f);
                o.effectDistance = new Vector2(1f, -1f);
                _feed[i] = t;
            }
            LG.Ignore(_feedRoot.gameObject);

            LG.Skin(_hud.transform);
            _hud.SetActive(false);
        }

        /// <summary>Блок стороны: имя и стойка, состав по классам, щиты и прочность со следом урона, мощь.</summary>
        private static HudSide BuildSide(RectTransform parent, bool left)
        {
            var s = new HudSide { ClassText = new Text[4], ClassIcon = new Image[4] };
            var block = LGBuild.Rect(parent, left ? "Mine" : "Enemy");
            block.anchorMin = new Vector2(left ? 0f : 0.5f, 1f);
            block.anchorMax = new Vector2(left ? 0.5f : 1f, 1f);
            block.pivot = new Vector2(0.5f, 1f);
            block.offsetMin = new Vector2(left ? 18 : 92, -138);
            block.offsetMax = new Vector2(left ? -92 : -18, -46);
            var anchor = left ? TextAnchor.UpperLeft : TextAnchor.UpperRight;

            s.Name = LGBuild.Label(block, "", 13, Color.white, anchor, bold: true);
            s.Name.rectTransform.TopBand(0, 18);

            // Состав: четыре значка классов с числами
            var comp = LGBuild.Rect(block, "Composition");
            comp.TopBand(22, 22);
            for (int i = 0; i < 4; i++)
            {
                int idx = left ? i : 3 - i;
                var cell = LGBuild.Rect(comp, "Class");
                cell.anchorMin = cell.anchorMax = new Vector2(left ? 0 : 1, 0.5f);
                cell.pivot = new Vector2(left ? 0 : 1, 0.5f);
                cell.sizeDelta = new Vector2(68, 22);
                cell.anchoredPosition = new Vector2((left ? 1 : -1) * i * 72, 0);
                var ic = LGIcons.Create(cell, ClassIcons[idx], 16, Color.white);
                ic.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), Vector2.zero, new Vector2(16, 16));
                var t = LGBuild.Label(cell, "", 11, Color.white, TextAnchor.MiddleLeft, bold: true);
                t.rectTransform.Stretch(20, 0, 0, 0);
                s.ClassIcon[idx] = ic;
                s.ClassText[idx] = t;
                TooltipHelper.Attach(cell.gameObject, Loc.F("<b>{0}</b>\nв строю / было в начале боя", ClassNames[idx]));
                var hit = cell.gameObject.AddComponent<Image>();
                hit.color = new Color(0, 0, 0, 0);
                LG.Ignore(cell.gameObject);
            }

            // Щиты (тонкая) и прочность со следом урона
            var shHost = LGBuild.Rect(block, "Shield");
            shHost.TopBand(52, 5);
            s.Shield = LGBuild.Bar(shHost, new Color(0.45f, 0.75f, 1f), 1f, 4f);
            var hpHost = LGBuild.Rect(block, "Hp");
            hpHost.TopBand(60, 10);
            s.Hp = LGBuild.Bar(hpHost, left ? FleetIndicator.OwnColor : FleetIndicator.EnemyColor, 1f, 9f);
            // След урона — белая полоса за основной, медленно догоняет текущее значение
            var track = s.Hp.parent;
            var lag = LGBuild.Panel(track, "Lag", new Color(1f, 0.95f, 0.85f, 0.55f));
            lag.rectTransform.anchorMin = new Vector2(0, 0);
            lag.rectTransform.anchorMax = new Vector2(1, 1);
            lag.rectTransform.offsetMin = new Vector2(1, 1);
            lag.rectTransform.offsetMax = new Vector2(-1, -1);
            lag.transform.SetSiblingIndex(s.Hp.GetSiblingIndex());
            LG.Ignore(lag.gameObject);
            s.Lag = lag.rectTransform;

            s.Stats = LGBuild.Label(block, "", 10, UIManager.DS.TextMuted, anchor);
            s.Stats.rectTransform.TopBand(76, 16);
            return s;
        }

        /// <summary>Ряд тактики: четыре стойки слева, четыре приоритета целей справа.</summary>
        private void BuildTactics(RectTransform rt)
        {
            var row = LGBuild.Rect(rt, "Tactics");
            row.TopBand(146, 30, 18, 18);

            var lbl = LGBuild.Label(row, Loc.T("ТАКТИКА"), 10, UIManager.DS.TextMuted, TextAnchor.MiddleLeft, bold: true);
            lbl.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), Vector2.zero, new Vector2(64, 30));
            LGIcon[] sIcons = { LGIcon.Swords, LGIcon.Weapons, LGIcon.ShieldDome, LGIcon.Target };
            _stanceBg = new Image[4];
            for (int i = 0; i < 4; i++)
            {
                var st = (Stance)i;
                var b = LGBuild.Button(row, "Stance", CHudDark, new Color(1f, 0.55f, 0.45f, 0.5f),
                                       () => SetPlayerStance(_hudSystem, st), sIcons[i], StanceName(st).ToUpper(), 9, 8f);
                ((RectTransform)b.transform).At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(64 + i * 100, 0), new Vector2(96, 30));
                _stanceBg[i] = b.GetComponent<Image>();
                TooltipHelper.Attach(b.gameObject, Loc.F("<b>{0}</b>\n{1}\n<color=#8AA2A8>Выбор запоминается для следующих боёв.</color>", StanceName(st), StanceDesc(st)));
            }

            var plbl = LGBuild.Label(row, Loc.T("ЦЕЛИ"), 10, UIManager.DS.TextMuted, TextAnchor.MiddleRight, bold: true);
            plbl.rectTransform.At(new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-4 * 34 - 6, 0), new Vector2(50, 30));
            LGIcon[] pIcons = { LGIcon.Fleet, LGIcon.Corvette, LGIcon.Destroyer, LGIcon.Starbase };
            _prioBg = new Image[4];
            for (int i = 0; i < 4; i++)
            {
                var p = (TargetPriority)i;
                var b = LGBuild.Button(row, "Priority", CHudDark, new Color(1f, 0.82f, 0.4f, 0.5f),
                                       () => SetPlayerPriority(_hudSystem, p), pIcons[i], null, 10, 8f);
                ((RectTransform)b.transform).At(new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-(3 - i) * 34, 0), new Vector2(30, 30));
                _prioBg[i] = b.GetComponent<Image>();
                TooltipHelper.Attach(b.gameObject, $"<b>{PriorityName(p)}</b>\n{PriorityDesc(p)}");
            }
        }

        /// <summary>Какой бой показывать: где выделенный флот, иначе самый крупный с участием игрока.</summary>
        private Battle HudBattle()
        {
            var sel = FleetManager.Instance?.SelectedFleet;
            if (sel?.Data != null)
            {
                var b = GetBattle(sel.Data.CurrentSystemId);
                if (b != null && b.PlayerInvolved) return b;
            }
            Battle best = null;
            float bestPower = -1f;
            foreach (var b in _battles.Values)
            {
                if (!b.PlayerInvolved) continue;
                float p = 0f;
                foreach (var s in b.Sides.Values) p += s.Power;
                if (p > bestPower) { bestPower = p; best = b; }
            }
            return best;
        }

        private void RefreshHud()
        {
            if (_hud == null) return;
            bool inSystem = SystemViewManager.Instance != null && SystemViewManager.Instance.IsInSystemView;
            var b = inSystem ? null : HudBattle();
            bool show = b != null;
            if (show != LG.IsVisible(_hud)) { if (show) LG.Show(_hud); else LG.Hide(_hud); }
            if (!show) { _hudSystem = -1; return; }
            if (_hudSystem != b.SystemId)
            {
                _hudMe.LagValue = _hudEnemy.LagValue = 1f;
                _feedSeen = b.Log.Count;   // в ленте — только новое
            }
            _hudSystem = b.SystemId;
            float dt = Time.unscaledDeltaTime;

            var sys = _generator.Systems[b.SystemId];
            int others = 0;
            foreach (var x in _battles.Values) if (x.PlayerInvolved && x != b) others++;
            _hudTitle.text = Loc.F("СРАЖЕНИЕ ПРИ {0}", sys.Name.ToUpper());
            _hudDays.text = Loc.F("день {0}", Mathf.Max(1, Mathf.CeilToInt(b.Days))) + (others > 0 ? Loc.F("  ·  ещё боёв: {0}", others) : "");
            _hudPhase.text = b.Approach ? Loc.T("<color=#7FD8FF>СБЛИЖЕНИЕ</color>") : Loc.T("<color=#FF8A6A>ОГНЕВОЙ КОНТАКТ</color>");

            // Стороны: мы и сильнейший противник (остальные враги — в его мощь и прочность)
            var me = Side(b, 0);
            SideStats enemy = null;
            float enemyHp = 0f, enemyStartHp = 0f, enemyDps = 0f, enemyPower = 0f, enemySh = 0f, enemyShMax = 0f;
            foreach (var kv in b.Sides)
            {
                if (kv.Key == 0 || !Diplomacy.AtWar(0, kv.Key)) continue;
                var s = kv.Value;
                if (enemy == null || s.Power > enemy.Power) enemy = s;
                enemyHp += s.Hp; enemyStartHp += s.StartHp; enemyDps += s.Dps; enemyPower += s.Power;
                enemySh += s.Shields; enemyShMax += s.MaxShields;
            }
            enemy ??= new SideStats { Owner = -1 };

            string enemyName = enemy.Owner > 0 ? AIEmpireManager.NameOf(enemy.Owner, Loc.T("Противник")) : Loc.T("Противник");
            Color ec = enemy.Owner >= 0 ? FleetIndicator.OwnerColor(enemy.Owner) : UIManager.DS.Red;
            string myName = UIManager.Instance?.SelectedFaction?.Name ?? Loc.T("Ваш флот");
            _hudMe.Name.text = $"<color={LGBuild.Hex(FleetIndicator.OwnColor)}>{myName}</color>  <size=10><color=#F2C747>{StanceName(StanceOf(b, 0)).ToUpper()}</color></size>";
            _hudEnemy.Name.text = $"<size=10><color=#F2C747>{StanceName(StanceOf(b, enemy.Owner)).ToUpper()}</color></size>  <color={LGBuild.Hex(Color.Lerp(ec, Color.white, 0.2f))}>{enemyName}</color>";

            FillSide(_hudMe, me, me.Hp, me.StartHp, me.Shields, me.MaxShields, dt, FleetIndicator.OwnColor);
            FillSide(_hudEnemy, enemy, enemyHp, enemyStartHp, enemySh, enemyShMax, dt, ec);
            _hudMe.Stats.text = Loc.F("мощь {0:N0}  ·  урон {1:N0}/день", Mathf.RoundToInt(me.Power), Mathf.RoundToInt(me.Dps));
            _hudEnemy.Stats.text = Loc.F("урон {0:N0}/день  ·  мощь {1:N0}", Mathf.RoundToInt(enemyDps), Mathf.RoundToInt(enemyPower));

            // Баланс
            float total = me.Power + enemyPower;
            float share = total > 0f ? me.Power / total : 0.5f;
            LGBuild.SetBar(_balanceBar, share, Color.Lerp(UIManager.DS.Red, UIManager.DS.Green, share));
            float ratio = me.Power / Mathf.Max(1f, enemyPower);
            _balanceText.text = ratio >= 1.1f ? Loc.F("<color=#5CF59A>ПЕРЕВЕС ×{0:0.0}</color>", ratio)
                              : ratio <= 0.9f ? Loc.F("<color=#FF6A6A>ВРАГ СИЛЬНЕЕ ×{0:0.0}</color>", 1f / Mathf.Max(0.01f, ratio))
                              : Loc.T("<color=#F2C747>РАВНЫЕ СИЛЫ</color>");

            // Прогноз: за сколько дней каждая сторона снесёт другую при нынешнем темпе
            bool losing = false;
            if (me.Ships == 0 && !me.HasStarbase) _forecastText.text = "";
            else
            {
                float tKill = enemyHp / Mathf.Max(1f, me.Dps);
                float tDie = me.Hp / Mathf.Max(1f, enemyDps);
                if (tKill > 60f && tDie > 60f) _forecastText.text = Loc.T("<color=#F2C747>Прогноз: затяжной бой</color>");
                else if (tKill < tDie * 0.9f) _forecastText.text = Loc.F("<color=#5CF59A>Прогноз: победа\nпримерно через {0}</color>", FmtDays(tKill));
                else if (tDie < tKill * 0.9f)
                {
                    losing = true;
                    _forecastText.text = Loc.F("<color=#FF6A6A>Прогноз: поражение\nпримерно через {0}</color>", FmtDays(tDie));
                }
                else _forecastText.text = Loc.T("<color=#F2C747>Прогноз: исход неясен</color>");
            }
            if (_retreatFx != null) _retreatFx.SetPulse(losing ? 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 5f) : 0f);

            // Тактика
            var myStance = StanceOf(b, 0);
            var myPrio = PriorityOf(b, 0);
            for (int i = 0; i < 4; i++)
            {
                _stanceBg[i].color = (int)myStance == i ? CHudOn : CHudDark;
                _prioBg[i].color = (int)myPrio == i ? new Color(0.55f, 0.40f, 0.12f) : CHudDark;
            }

            _hudFooter.text = FtlLine(b.SystemId) + "\n" +
                              Loc.F("нанесено {0:N0} · получено {1:N0}  ·  ", me.DamageDealt, me.DamageTaken) +
                              Loc.F("уничтожено {0} · потеряно {1}", me.Kills, me.Lost) + (me.Disengaged > 0 ? Loc.F(" · вышло {0}", me.Disengaged) : "");

            RefreshFeed(b);
        }

        private static string FmtDays(float t) => t < 1f ? Loc.T("день") : Loc.F("{0} дн.", Mathf.CeilToInt(t));

        /// <summary>Состав по классам, щиты и прочность со следом урона.</summary>
        private static void FillSide(HudSide ui, SideStats s, float hp, float startHp, float shields, float maxShields, float dt, Color col)
        {
            for (int i = 0; i < 4; i++)
            {
                int alive, start;
                if (i == 3) { alive = s.HasStarbase ? 1 : 0; start = s.HasStarbase ? 1 : 0; }
                else { alive = s.ClassAlive[i]; start = Mathf.Max(alive, s.ClassStart[i]); }
                bool any = start > 0;
                ui.ClassText[i].text = !any ? "—" : i == 3 ? Loc.T("в строю") : alive == start ? $"{alive}" : $"{alive}<color=#FF6A6A>/{start}</color>";
                ui.ClassText[i].color = any ? Color.white : UIManager.DS.TextMuted;
                ui.ClassIcon[i].color = any ? (alive > 0 ? Color.Lerp(col, Color.white, 0.35f) : new Color(1f, 0.4f, 0.4f, 0.8f))
                                            : new Color(1f, 1f, 1f, 0.18f);
            }

            float frac = startHp > 0f ? Mathf.Clamp01(hp / startHp) : 0f;
            LGBuild.SetBar(ui.Hp, frac, col);
            ui.LagValue = frac >= ui.LagValue ? frac : Mathf.MoveTowards(ui.LagValue, frac, dt * 0.18f);
            ui.Lag.anchorMax = new Vector2(Mathf.Clamp01(ui.LagValue), 1f);
            LGBuild.SetBar(ui.Shield, maxShields > 0f ? Mathf.Clamp01(shields / maxShields) : 0f);
        }

        /// <summary>Лента событий: последние четыре строки моложе 9 с; свежая вспыхивает, старые гаснут.</summary>
        private void RefreshFeed(Battle b)
        {
            float now = Time.unscaledTime;
            if (_feedSeen > b.Log.Count) _feedSeen = b.Log.Count;
            int shown = 0;
            for (int i = b.Log.Count - 1; i >= 0 && shown < _feed.Length; i--)
            {
                var e = b.Log[i];
                float age = now - e.Time;
                if (age > 9f) break;
                var t = _feed[shown++];
                t.text = e.Text;
                Color c = e.Kind switch
                {
                    LogKind.Kill => e.Owner == 0 ? new Color(1f, 0.45f, 0.40f) : new Color(0.45f, 1f, 0.62f),
                    LogKind.Shield => e.Owner == 0 ? new Color(1f, 0.70f, 0.45f) : new Color(1f, 0.85f, 0.45f),
                    LogKind.Disengage => new Color(0.95f, 0.80f, 0.45f),
                    LogKind.Phase => new Color(0.50f, 0.85f, 1f),
                    _ => new Color(0.85f, 0.92f, 1f)
                };
                float fade = Mathf.Clamp01((9f - age) / 2f);
                float flash = Mathf.Clamp01(1f - age / 0.35f);
                c = Color.Lerp(c, Color.white, flash * 0.6f);
                c.a = fade * (shown == 1 ? 1f : 0.85f);
                t.color = c;
                float k = 1f + flash * 0.18f;
                t.rectTransform.localScale = new Vector3(k, k, 1f);
            }
            for (int i = shown; i < _feed.Length; i++) _feed[i].text = "";
            _feedSeen = b.Log.Count;
        }

        /// <summary>Готовность гиперпривода наших кораблей в этом бою.</summary>
        private string FtlLine(int systemId)
        {
            var fm = FleetManager.Instance;
            if (fm == null) return "";
            float slowest = 0f;
            int ships = 0, retreating = 0;
            foreach (var f in fm.AllFleets)
            {
                var d = f?.Data;
                if (d == null || d.OwnerId != 0 || !d.InCombat || d.CurrentSystemId != systemId) continue;
                ships++;
                if (IsRetreating(d)) retreating++;
                slowest = Mathf.Max(slowest, FtlReadyIn(d));
            }
            if (ships == 0) return "";
            if (retreating > 0)
                return slowest > 0f ? Loc.F("<color=#F2C747>Отступление: прыжок через {0:0.0} дн. ({1} кор.)</color>", slowest, retreating)
                                    : Loc.F("<color=#F2C747>Отступление: корабли уходят прыжком ({0} кор.)</color>", retreating);
            return slowest > 0f ? Loc.F("<color=#7FD8FF>ГПД: разгон {0:0.0} дн.</color>", slowest) : Loc.T("<color=#5CF59A>ГПД готов к экстренному прыжку</color>");
        }

        private void FocusBattle()
        {
            if (_hudSystem < 0 || _generator == null) return;
            var cam = FindAnyObjectByType<StellarisClone.Cam.StrategyCameraController>();
            if (cam != null) cam.FocusOn(_generator.Systems[_hudSystem].Position);
        }

        private void RetreatAll()
        {
            var fm = FleetManager.Instance;
            if (fm == null || _hudSystem < 0) return;
            int tried = 0, ok = 0;
            float wait = 0f;
            foreach (var f in new List<FleetView>(fm.AllFleets))
            {
                var d = f?.Data;
                if (d == null || d.OwnerId != 0 || !d.InCombat || d.CurrentSystemId != _hudSystem) continue;
                tried++;
                if (TryEmergencyFtl(f)) ok++;
                else wait = Mathf.Max(wait, FtlReadyIn(d));
            }
            if (tried == 0) return;
            AddLog(GetBattle(_hudSystem), Loc.T("Приказ: отступление"), LogKind.Tactic, 0);
            string body = ok == tried ? Loc.F("Все корабли ушли прыжком ({0})", ok)
                        : wait > 0f ? Loc.F("Ушли сразу: {0} из {1}. Остальные держат строй и прыгнут, как только разгонится ГПД (≈{2:0.0} дн.)", ok, tried, wait)
                        : Loc.F("Ушли сразу: {0} из {1}. У остальных сбой ГПД — повтор через полдня", ok, tried);
            NotificationCenter.Show(Loc.T("Отступление"), body, ok == tried ? NotificationCenter.Kind.Info : NotificationCenter.Kind.Warning, 5f);
        }

        private void HideHud()
        {
            if (_hud != null) _hud.SetActive(false);
        }
    }
}
