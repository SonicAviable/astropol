using System;
using System.Collections.Generic;
using StellarisClone.Core;

namespace StellarisClone.Cards
{
    /// <summary>Ход боя: начало, розыгрыш рубашкой вверх, раскрытие, перестрелка, конец хода и итоги.</summary>
    public sealed partial class BattleState
    {
        // ==================== НАЧАЛО ====================

        /// <summary>Разложить стол: колоды, сооружения и ловушки защитника, стартовые руки. Затем — первый ход.</summary>
        public void Begin()
        {
            Result = new BattleResult();
            DealDeck(Attacker, Setup.AttackerCards, Setup.AttackerHero);
            DealDeck(Defender, Setup.DefenderCards, Setup.DefenderHero);
            OrbitalBarrage();
            // Большая армия даёт больше вариантов с первого хода: +1 карта в руку при колоде от 14, ещё +1 от 18
            for (int s = 0; s < 2; s++)
            {
                int deck = Sides[s].Deck.Count;
                DrawCards(s, StartHand + (deck >= 14 ? 1 : 0) + (deck >= 18 ? 1 : 0));
            }
            // ИИ сразу пересдаёт неудачные карты; игрок — на экране «Начальный расклад»
            for (int s = 0; s < 2; s++) if (!Sides[s].IsPlayer) BattleAI.Mulligan(this, s);
            StartTurn();
        }

        private void DealDeck(int side, List<BCard> cards, BCard hero)
        {
            var sd = Sides[side];
            var deck = new List<BCard>();
            var structures = new List<BCard>();
            var traps = new List<BCard>();
            foreach (var c in cards)
            {
                if (side == Defender && c.Def.Kind == CardKind.Structure) structures.Add(c);
                else if (side == Defender && c.Def.Kind == CardKind.Trap) traps.Add(c);
                else deck.Add(c);
            }
            // Перемешать (Фишер–Йетс), герой — сразу в руку
            for (int i = deck.Count - 1; i > 0; i--) { int j = Rng.Next(i + 1); var t = deck[i]; deck[i] = deck[j]; deck[j] = t; }
            sd.Deck.AddRange(deck);
            if (hero != null) sd.Hand.Add(hero);

            // Оборона колонии стоит с начала: щит — в Штабе, турели — у Штаба и электростанции, бункеры — по кругу
            int hq = SectorIndex(SectorType.HQ);
            int rr = 0;
            foreach (var c in structures)
            {
                int sec;
                if (c.Def.Has(Kw.Shield)) sec = hq >= 0 ? hq : 2;
                else if (c.Def.Has(Kw.NeedsPower)) sec = (rr++ % 2 == 0 && hq >= 0) ? hq : Math.Max(0, SectorIndex(SectorType.Power));
                else sec = rr++ % 3;
                Place(c, side, sec, true);
            }
            foreach (var c in traps)
            {
                int sec = Rng.Next(3);
                Place(c, side, sec, false, hidden: true);
                Log(EvType.TrapSet, side, sec, def: c.Def);
            }
        }

        // ---- Настройки баланса укреплений и десанта (подбираются пакетным автобоем BattleSandbox.RunBalanceSweep)
        /// <summary>Урон артподготовки по каждому сооружению (флот от 3000 мощи — на 1 больше).</summary>
        public static int BarrageBase = 2;
        /// <summary>Укрытие, которое бункер даёт защитникам своего сектора в каждой перестрелке.</summary>
        public static int BunkerCover = 1;
        /// <summary>Бункер укрывает только пехоту и гарнизон (не технику и не другие сооружения).</summary>
        public static bool BunkerCoverTroopsOnly;
        /// <summary>Прибавка к силе турелей.</summary>
        public static int TurretPowerDelta;
        /// <summary>Дополнительные очки приказов десанту каждый ход.</summary>
        public static int AttackerBonusPoints;

        /// <summary>Урон артподготовки по каждому сооружению защитника (кроме щита); флот от 3000 мощи — сильнее.</summary>
        public int BarrageDamage => Sides[Attacker].FleetPower <= 0f ? 0 : BarrageBase + (Sides[Attacker].FleetPower >= 3000f ? 1 : 0);

        /// <summary>Бункер в начале перестрелки: укрытие защитникам сектора (см. <see cref="BunkerCover"/>).</summary>
        public void BunkerAura(BUnit bunker)
        {
            foreach (var a in UnitsOf(bunker.Side, bunker.Sector))
            {
                if (a == bunker) continue;
                if (BunkerCoverTroopsOnly && (a.IsStructure || a.Has(Kw.Vehicle) || a.Has(Kw.Drone))) continue;
                a.CoverLeft += BunkerCover;
            }
        }

        /// <summary>
        /// Артподготовка с орбиты перед первым ходом: флот десанта бьёт по укреплениям колонии (укрытие не спасает).
        /// Без неё бункеры и турели держали секторы с первого хода, а у десанта было 1–2 очка — штурм казался проигранным.
        /// </summary>
        private void OrbitalBarrage()
        {
            int dmg = BarrageDamage;
            if (dmg <= 0) return;
            var targets = new List<BUnit>();
            foreach (var sec in Sectors)
                foreach (var u in sec.Units)
                    if (u.Side == Defender && u.Alive && u.IsStructure && !u.Has(Kw.Shield)) targets.Add(u);
            if (targets.Count == 0) return;
            Log(EvType.Status, Attacker, -1, amount: dmg, text: "barrage");
            foreach (var u in targets) Damage(null, u, dmg, true);
        }

        // ==================== ХОД ====================

        public void StartTurn()
        {
            if (Over) return;
            Turn++;
            Log(EvType.TurnStart, amount: Turn);
            for (int s = 0; s < 2; s++)
            {
                var sd = Sides[s];
                sd.Points = Math.Min(Turn, PointsCap) + sd.PointsPerTurn + sd.BonusNext + (s == Attacker ? AttackerBonusPoints : 0);
                sd.BonusNext = 0;
                if (HoldsSector(s, SectorType.Mine)) sd.BonusNext += 1;
                if (Turn > 1)
                {
                    int draw = 1 + (HoldsSector(s, SectorType.Factory) ? 1 : 0);
                    DrawCards(s, draw);
                }
                sd.Pending.Clear();
            }
            if (Turn == 3) RevealWeather();
        }

        /// <summary>
        /// Муллиган: заменить выбранные карты стартовой руки — один раз за бой, до первого розыгрыша.
        /// Новые карты берутся с верха колоды, сданные возвращаются в колоду в случайные места (герой не меняется).
        /// </summary>
        public int Mulligan(int side, List<BCard> cards)
        {
            var sd = Sides[side];
            if (Over || sd.Mulliganed || Turn > 1 || sd.Pending.Count > 0 || cards == null) return 0;
            sd.Mulliganed = true;
            var back = new List<BCard>();
            foreach (var c in cards)
                if (c != null && c.Def.Kind != CardKind.Hero && sd.Hand.Remove(c)) back.Add(c);
            if (back.Count == 0) return 0;
            int take = Math.Min(back.Count, sd.Deck.Count);
            for (int i = 0; i < take; i++)
            {
                var c = sd.Deck[0];
                sd.Deck.RemoveAt(0);
                sd.Hand.Add(c);
                Log(EvType.Draw, side, def: c.Def, text: "mulligan");
            }
            foreach (var c in back) sd.Deck.Insert(Rng.Next(sd.Deck.Count + 1), c);
            // Колода была меньше, чем сдано, — добрать (может вернуться и сданная карта)
            for (int i = take; i < back.Count && sd.Deck.Count > 0; i++)
            {
                var c = sd.Deck[0];
                sd.Deck.RemoveAt(0);
                sd.Hand.Add(c);
                Log(EvType.Draw, side, def: c.Def, text: "mulligan");
            }
            return back.Count;
        }

        /// <summary>Можно ли выложить карту из руки в сектор (очки, тип карты).</summary>
        public string CanPlay(int side, BCard c, int sector)
        {
            if (Over) return Loc.T("Бой окончен");
            var sd = Sides[side];
            if (!sd.Hand.Contains(c)) return Loc.T("Карты нет в руке");
            if (sector < 0 || sector > 2) return Loc.T("Выберите сектор");
            if (c.Def.Kind == CardKind.Structure) return Loc.T("Сооружения стоят с начала боя");
            if (CostOf(side, c) > sd.Points) return Loc.T("Не хватает очков приказов");
            if (c.Def.Id == "O2" && sd.FleetPower < 1500f) return Loc.T("Нужен флот мощью от 1500 на орбите");
            return null;
        }

        /// <summary>Выложить карту рубашкой вверх (раскроется в конце хода).</summary>
        public bool Play(int side, BCard c, int sector)
        {
            if (CanPlay(side, c, sector) != null) return false;
            var sd = Sides[side];
            sd.Points -= CostOf(side, c);
            sd.Hand.Remove(c);
            sd.Pending.Add((c, sector));
            return true;
        }

        /// <summary>Вернуть выложенную в этом ходу карту в руку.</summary>
        public bool Unplay(int side, BCard c)
        {
            var sd = Sides[side];
            for (int i = 0; i < sd.Pending.Count; i++)
            {
                if (sd.Pending[i].card != c) continue;
                sd.Pending.RemoveAt(i);
                sd.Points += CostOf(side, c);
                sd.Hand.Add(c);
                return true;
            }
            return false;
        }

        /// <summary>Наёмники Аквилы: купить карту прямо в бою за гелий-3 казны и сразу выложить.</summary>
        public bool BuyMerc(int side, CardDef def, int sector, float price)
        {
            var sd = Sides[side];
            if (Over || sd.Treasury == null || sd.Treasury() < price || sector < 0 || sector > 2) return false;
            sd.SpendTreasury?.Invoke(price);
            sd.Pending.Add((new BCard(def), sector));
            return true;
        }

        public void Retreat(int side)
        {
            if (Over) return;
            Log(EvType.Retreat, side);
            Finish(Enemy(side), retreat: true);
        }

        /// <summary>
        /// Подвести ход: раскрыть все выложенные карты (первой раскрывается сторона, у которой больше секторов —
        /// отстающий видит её ход и отвечает), провести перестрелку и конец хода. События — в <see cref="Events"/>.
        /// </summary>
        public void Resolve()
        {
            if (Over) return;
            int held0 = 0, held1 = 0;
            foreach (var s in Sectors) { if (s.Control == Attacker) held0++; else if (s.Control == Defender) held1++; }
            int first = held1 > held0 ? Defender : Attacker;
            RevealSide(first);
            if (!Over) RevealSide(Enemy(first));
            if (Over) return;

            // Ауры на этот ход (комендант, узел дронов)
            foreach (var sec in Sectors)
                foreach (var u in new List<BUnit>(sec.Units))
                    if (u.Alive && u.Revealed && !u.IsTrap) u.Def.OnPreCombat?.Invoke(this, u);
            for (int i = 0; i < 3; i++) Combat(i);
            EndTurn();
        }

        private void RevealSide(int side)
        {
            var sd = Sides[side];
            var list = new List<(BCard card, int sector)>(sd.Pending);
            sd.Pending.Clear();
            foreach (var pend in list)
            {
                var card = pend.card; int sector = pend.sector;
                var d = card.Def;
                if (d.Kind == CardKind.Order)
                {
                    Log(EvType.OrderFx, side, sector, def: d);
                    d.OnOrder?.Invoke(this, side, sector);
                    sd.Spent.Add(card);
                    continue;
                }
                if (d.Kind == CardKind.Trap)
                {
                    var t = Place(card, side, sector, false, hidden: true);
                    Log(EvType.TrapSet, side, sector, t.Uid, def: d);
                    continue;
                }
                var u = Place(card, side, sector, true);
                Log(EvType.Reveal, side, sector, u.Uid, def: d);
                if (d.Salvo > 0) { var t = RandomEnemy(side, sector); if (t != null) { Log(EvType.Shot, side, sector, u.Uid, t.Uid, d.Salvo, "salvo"); Damage(u, t, d.Salvo); } }
                if (d.Has(Kw.Crush))
                    foreach (var e in Enemies(side, sector))
                        if (e.Has(Kw.Infantry) && e.Armor <= 2) { Log(EvType.Shot, side, sector, u.Uid, e.Uid, e.Armor, "crush"); Kill(e, u); }
                d.OnReveal?.Invoke(this, u);
                TriggerTraps(u);
                if (Over) return;
            }
        }

        /// <summary>Скрытая ловушка врага в секторе срабатывает на первом раскрытом юните.</summary>
        private void TriggerTraps(BUnit u)
        {
            if (u.Dead) return;
            foreach (var t in Sectors[u.Sector].Units)
            {
                if (!t.IsTrap || t.Dead || t.Side == u.Side) continue;
                t.Dead = true;
                t.Revealed = true;
                Log(EvType.TrapFire, t.Side, t.Sector, t.Uid, u.Uid, def: t.Def);
                t.Def.OnTrap?.Invoke(this, t, u);
                if (t.Card.InstId >= 0) Sides[t.Side].Spent.Add(t.Card);
                return;
            }
        }

        /// <summary>Снять (обезвредить) одну скрытую ловушку врага в секторе — инженеры, дроны.</summary>
        public bool ClearTrap(int side, int sector)
        {
            foreach (var t in Sectors[sector].Units)
            {
                if (!t.IsTrap || t.Dead || t.Side == side) continue;
                t.Dead = true;
                t.Revealed = true;
                Log(EvType.TrapFire, t.Side, sector, t.Uid, -1, text: "cleared", def: t.Def);
                if (t.Card.InstId >= 0) Sides[t.Side].Spent.Add(t.Card);
                return true;
            }
            return false;
        }

        // ==================== ПЕРЕСТРЕЛКА ====================

        /// <summary>
        /// Одновременная перестрелка в секторе: каждый стрелок выбирает цель по снимку «до залпа» (без перебора
        /// по уже обречённым), потом все попадания применяются вперемешку — как залп с обеих сторон.
        /// </summary>
        private void Combat(int sector)
        {
            var sec = Sectors[sector];
            if (sec.Smoke) { Log(EvType.Status, -1, sector, text: "smoke"); return; }
            var shots = new List<(BUnit src, BUnit tgt, int amount, bool lobbed)>();
            var planned = new Dictionary<BUnit, int>();
            for (int side = 0; side < 2; side++)
            {
                var shooters = new List<BUnit>();
                foreach (var u in UnitsOf(side, sector)) if (CanShoot(u)) shooters.Add(u);
                shooters.Sort((a, b) => Power(b).CompareTo(Power(a)));
                foreach (var u in shooters)
                {
                    var t = PickTarget(u, sector, planned);
                    if (t == null) continue;
                    int amt = Power(u);
                    shots.Add((u, t, amt, false));
                    planned[t] = Remaining(t, planned) - Effective(u, t, amt);
                }
            }
            // Навесной огонь артиллерии — по соседнему сектору половиной силы
            foreach (var u in new List<BUnit>(sec.Units))
            {
                if (!u.Has(Kw.Indirect) || !CanShoot(u) || (WeatherActive && Setup.Terrain == Terrain.Desert)) continue;
                int best = -1, bestCount = 0;
                foreach (int n in new[] { sector - 1, sector + 1 })
                {
                    if (n < 0 || n > 2) continue;
                    int cnt = Enemies(u.Side, n).Count;
                    if (cnt > bestCount) { bestCount = cnt; best = n; }
                }
                if (best < 0) continue;
                var t = StrongestEnemy(u.Side, best);
                if (t != null) shots.Add((u, t, Math.Max(1, Power(u) / 2), true));
            }
            // Перемешать стороны: залп идёт навстречу, а не «сначала одни, потом другие»
            for (int i = shots.Count - 1; i > 0; i--) { int j = Rng.Next(i + 1); var tmp = shots[i]; shots[i] = shots[j]; shots[j] = tmp; }
            foreach (var shot in shots)
            {
                var src = shot.src; var tgt = shot.tgt; int amount = shot.amount; bool lobbed = shot.lobbed;
                // выстрел уже в полёте — долетает даже от стрелка, погибшего в этом же залпе
                if (tgt.Dead) continue;
                Log(EvType.Shot, src.Side, tgt.Sector, src.Uid, tgt.Uid, amount, lobbed ? "lob" : null);
                Damage(src, tgt, amount);
                if (Over) return;
            }
        }

        private int Remaining(BUnit t, Dictionary<BUnit, int> planned)
            => planned.TryGetValue(t, out int r) ? r : t.Armor + t.CoverLeft;

        private int Effective(BUnit src, BUnit t, int amt)
        {
            bool hard = t.Has(Kw.Vehicle) || t.IsStructure;
            if (src.Has(Kw.Pierce) && hard) amt *= 2;
            if (src.Has(Kw.Flame) && t.IsStructure) amt *= 2;
            if (src.Has(Kw.Sniper) && t.Has(Kw.Infantry)) amt *= 2;
            return amt;
        }

        /// <summary>
        /// Цель стрелка: снайпер — самый хрупкий (пехота в приоритете); бронебой — техника и сооружения;
        /// остальные — сначала укрытия (бункер держит удар на себе), затем самый опасный враг.
        /// </summary>
        /// <summary>Насколько бункер «тянет» огонь на себя (отладка баланса).</summary>
        public static float BunkerPull = 0f;

        private BUnit PickTarget(BUnit u, int sector, Dictionary<BUnit, int> planned)
        {
            var enemies = Enemies(u.Side, sector);
            BUnit best = null;
            float bestScore = float.MinValue;
            foreach (var e in enemies)
            {
                int left = Remaining(e, planned);
                if (left <= 0) continue;
                float score;
                if (u.Has(Kw.Sniper)) score = 100 - left + (e.Has(Kw.Infantry) ? 30 : 0);
                else
                {
                    score = Power(e) * 3 + (u.Has(Kw.Pierce) && (e.Has(Kw.Vehicle) || e.IsStructure) ? 25 : 0)
                          + (e.IsStructure && e.Def.Cover > 0 ? BunkerPull : 0) - left * 0.5f
                          + (Effective(u, e, Power(u)) >= left ? 8 : 0);   // добить
                }
                if (score > bestScore) { bestScore = score; best = e; }
            }
            return best;
        }

        // ==================== КОНЕЦ ХОДА ====================

        private void EndTurn()
        {
            foreach (var sec in Sectors)
                foreach (var u in new List<BUnit>(sec.Units))
                    if (u.Alive && u.Revealed) u.Def.OnEndTurn?.Invoke(this, u);
            foreach (var sec in Sectors)
                foreach (var u in new List<BUnit>(sec.Units))
                    if (u.Alive && u.Has(Kw.Fragile)) Kill(u);

            // Статусы, укрытие и временные бонусы
            foreach (var sec in Sectors)
            {
                sec.Smoke = false;
                foreach (var u in sec.Units)
                {
                    if (u.Dead) continue;
                    if (u.Suppressed > 0) u.Suppressed--;
                    if (u.Disabled > 0) u.Disabled--;
                    if (u.Immobile > 0) u.Immobile--;
                    u.TempPower = 0;
                    u.CoverLeft = u.Def.Cover;
                }
            }

            bool shieldBefore = ShieldUp;
            UpdateControl();
            if (shieldBefore && !ShieldUp) Log(EvType.ShieldDown, Defender, SectorIndex(SectorType.HQ));

            // Прорыв: щит снят, в Штабе у атакующего ≥ 9 силы и больше, чем у защитника — два хода подряд.
            // После первого хода — тревога «Штаб под угрозой»: у защитника есть ход, чтобы ответить.
            int hq = SectorIndex(SectorType.HQ);
            bool threat = false;
            if (hq >= 0 && !ShieldUp)
            {
                int a = SectorPower(Attacker, hq), d = SectorPower(Defender, hq);
                threat = a >= BreachPower && a > d;
                if (threat && BreachWarned)
                {
                    Log(EvType.Breach, Attacker, hq, amount: a);
                    Result.Breach = true;
                    Finish(Attacker);
                    return;
                }
                if (threat && Turn < Turns) Log(EvType.Status, Attacker, hq, amount: a, text: "breach_warn");
            }
            BreachWarned = threat;
            // Одна из сторон осталась без войск и карт
            for (int s = 0; s < 2; s++)
            {
                bool any = Sides[s].Hand.Count > 0 || Sides[s].Deck.Count > 0;
                for (int i = 0; i < 3 && !any; i++) foreach (var u in UnitsOf(s, i)) { if (!u.IsStructure) { any = true; break; } }
                if (!any && s == Attacker) { Finish(Defender); return; }
            }
            if (Turn >= Turns) { FinishByControl(); return; }
            StartTurn();
        }

        public void UpdateControl()
        {
            for (int i = 0; i < 3; i++)
            {
                int a = SectorPower(Attacker, i), d = SectorPower(Defender, i);
                // Равенство — в пользу защитника (он дома); пустой сектор остаётся за тем, кто его держал
                int c = a > d ? Attacker : d > a ? Defender : (a == 0 ? Sectors[i].Control : Defender);
                if (c != Sectors[i].Control)
                {
                    Sectors[i].Control = c;
                    Log(EvType.Control, c, i);
                }
            }
        }

        private void FinishByControl()
        {
            int a = 0;
            foreach (var s in Sectors) if (s.Control == Attacker) a++;
            Finish(a >= 2 ? Attacker : Defender);
        }

        // ==================== ПОГОДА ====================

        private void RevealWeather()
        {
            WeatherActive = true;
            switch (Setup.Terrain)
            {
                case Terrain.Desert:
                    WeatherName = Loc.T("Песчаная буря");
                    WeatherText = Loc.T("Артиллерия не бьёт по соседям, снайперы теряют двойной урон");
                    break;
                case Terrain.Molten:
                    WeatherName = Loc.T("Лавовый прорыв");
                    WeatherText = Loc.T("Все в одном секторе получают 2 урона");
                    int sec = Rng.Next(3);
                    Log(EvType.Weather, -1, sec, text: WeatherName);
                    foreach (var u in new List<BUnit>(Sectors[sec].Units)) if (u.Alive && !u.IsTrap && u.Revealed) Damage(null, u, 2, true);
                    return;
                case Terrain.Barren:
                    WeatherName = Loc.T("Низкая гравитация");
                    WeatherText = Loc.T("Приказы стоят на 1 очко меньше");
                    Sides[0].OrderCostDiscount = Sides[1].OrderCostDiscount = 1;
                    break;
                default:
                    WeatherName = Loc.T("Ливень");
                    WeatherText = Loc.T("Техника −1 к силе");
                    break;
            }
            Log(EvType.Weather, text: WeatherName);
        }

        // ==================== ИТОГИ ====================

        private void Finish(int winner, bool retreat = false)
        {
            if (Over) return;
            Over = true;
            Winner = winner;
            var r = Result;
            r.AttackerWon = winner == Attacker;
            r.Retreated = retreat;
            r.Turns = Turn;
            for (int i = 0; i < 3; i++) if (Sectors[i].Ruined) r.Ruined.Add(Sectors[i].Type);

            var seen = new HashSet<long>();
            // Погибшие на столе
            foreach (var u in _everOnBoard)
            {
                long id = u.Card.InstId;
                if (u.Dead)
                {
                    r.LostCount[u.Side]++;
                    if (u.Has(Kw.Militia)) r.MilitiaKilled++;
                    if (id >= 0 && seen.Add(id)) r.Lost.Add(id);
                }
            }
            // Потраченные приказы и сработавшие ловушки
            for (int s = 0; s < 2; s++)
                foreach (var c in Sides[s].Spent) if (c.InstId >= 0 && seen.Add(c.InstId)) r.Lost.Add(c.InstId);
            // Выжившие на столе — ветеранство и раны
            foreach (var u in _everOnBoard)
            {
                long id = u.Card.InstId;
                if (u.Dead || id < 0 || !seen.Add(id)) continue;
                r.Survivors.Add((id, u.Side, u.HpFrac, true));
            }
            // Не сыгранные карты возвращаются как были
            for (int s = 0; s < 2; s++)
            {
                foreach (var c in Sides[s].Hand) if (c.InstId >= 0 && seen.Add(c.InstId)) r.Survivors.Add((c.InstId, s, 1f, false));
                foreach (var c in Sides[s].Deck) if (c.InstId >= 0 && seen.Add(c.InstId)) r.Survivors.Add((c.InstId, s, 1f, false));
                foreach (var p in Sides[s].Pending) if (p.card.InstId >= 0 && seen.Add(p.card.InstId)) r.Survivors.Add((p.card.InstId, s, 1f, false));
            }
            // Трофей: уцелевшая техника защитника переходит к победившему атакующему
            if (r.AttackerWon)
            {
                var trophies = new List<BUnit>();
                foreach (var u in _everOnBoard) if (!u.Dead && u.Side == Defender && u.Has(Kw.Vehicle) && u.Card.InstId >= 0) trophies.Add(u);
                if (trophies.Count > 0)
                {
                    var t = trophies[Rng.Next(trophies.Count)];
                    r.Trophy = t.Def;
                    r.TrophyInst = t.Card.InstId;
                }
            }
            Log(EvType.End, winner, text: retreat ? "retreat" : null);
        }
    }
}
