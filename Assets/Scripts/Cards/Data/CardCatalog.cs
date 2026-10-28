using System;
using System.Collections.Generic;
using StellarisClone.Core;

namespace StellarisClone.Cards
{
    /// <summary>
    /// Все карты штурма: 12 общих юнитов, 9 приказов, 6 карт обороны колонии, по 4 фракционные на каждую из 6 фракций,
    /// служебные (экипаж, ополчение, герои, жетоны). Производство: пехота — город, техника — промышленный район,
    /// приказы — штаб за гелий-3 и немного влияния, оборона — гарнизон колонии. Цены сбалансированы под доход средней
    /// колонии: техника — около корвета (60 сплавов) за танк, пехота — титан из городов.
    /// </summary>
    public static class CardCatalog
    {
        private static readonly List<CardDef> s_all = new List<CardDef>();
        private static readonly Dictionary<string, CardDef> s_byId = new Dictionary<string, CardDef>();

        public static IReadOnlyList<CardDef> All { get { Ensure(); return s_all; } }

        public static CardDef Get(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            Ensure();
            CardDef d;
            return s_byId.TryGetValue(id, out d) ? d : null;
        }

        private static CardDef Add(CardDef d) { s_all.Add(d); s_byId[d.Id] = d; return d; }

        private static CardDef U(string id, string name, int cost, int power, int armor, string text, Kw kw = Kw.Infantry)
            => Add(new CardDef { Id = id, Name = name, Kind = CardKind.Unit, Cost = cost, Power = power, Armor = armor, Text = text, Kw = kw, Size = 1 });

        /// <summary>
        /// Приказ. <paramref name="price"/> — цена в условных единицах: штаб платит в основном гелием-3 (снабжение, ×1.5),
        /// влияние — малая доля (÷4, не меньше 3): иначе приказы съедали всё влияние, нужное на экспансию и дипломатию.
        /// </summary>
        private static CardDef O(string id, string name, int cost, string text, Action<BattleState, int, int> fx, float price)
            => Add(new CardDef { Id = id, Name = name, Kind = CardKind.Order, Cost = cost, Text = text, OnOrder = fx, Size = 0, Site = ProdSite.Command,
                                 Energy = price * 1.5f, Influence = Math.Max(3f, (float)Math.Round(price * 0.25f)) });

        private static CardDef City(CardDef d, float min, float en, float al, int days, float upkeep = 0.2f)
        { d.Site = ProdSite.City; d.Minerals = min; d.Energy = en; d.Alloys = al; d.Days = days; d.Upkeep = upkeep; return d; }

        private static CardDef Ind(CardDef d, float al, float min, int days, int size, float upkeep = 0.5f)
        { d.Site = ProdSite.Industry; d.Alloys = al; d.Minerals = min; d.Days = days; d.Size = size; d.Upkeep = upkeep; d.Kw |= Kw.Vehicle; d.Kw &= ~Kw.Infantry; return d; }

        // Служебные карты (жетоны и временные)
        public static CardDef Crew => Get("Z_CREW");
        public static CardDef Militia => Get("D1");
        public static CardDef ShieldHQ => Get("D6");
        /// <summary>Турель укреплений базы (по одной на уровень укреплений в обороне колонии).</summary>
        public static CardDef FortTurret => Get("D4");
        public static CardDef HeroAttack => Get("H1");
        public static CardDef HeroDefense => Get("H2");

        private static void Ensure()
        {
            if (s_all.Count > 0) return;

            // ============================== ОБЩИЕ ЮНИТЫ ==============================
            City(U("U1", Loc.T("Штурмовая пехота"), 1, 2, 3, Loc.T("Основа любого десанта.")), 30, 10, 0, 20);
            City(Add(new CardDef { Id = "U2", Name = Loc.T("Гренадёры"), Kind = CardKind.Unit, Cost = 2, Power = 2, Armor = 3, Salvo = 2, Kw = Kw.Infantry,
                Text = Loc.T("При раскрытии: 2 урона случайному врагу в секторе.") }), 40, 15, 0, 25);
            City(Add(new CardDef { Id = "U3", Name = Loc.T("Тяжёлая пехота"), Kind = CardKind.Unit, Cost = 3, Power = 3, Armor = 5, Cover = 2, Kw = Kw.Infantry,
                Text = Loc.T("Укрытие 2: первые 2 урона за ход не проходят.") }), 60, 20, 20, 35, 0.3f);
            City(U("U4", Loc.T("Снайперы"), 2, 2, 2, Loc.T("Бьют самого хрупкого врага. По пехоте — двойной урон."), Kw.Infantry | Kw.Sniper), 45, 10, 0, 30);
            City(U("U5", Loc.T("Огнемётчики"), 3, 4, 3, Loc.T("Игнорируют укрытие. По сооружениям — двойной урон."), Kw.Infantry | Kw.Flame), 50, 10, 20, 30, 0.3f);
            var medic = City(U("U6", Loc.T("Медики"), 2, 1, 3, Loc.T("В конце хода: +2 брони самому раненому союзнику в секторе.")), 40, 10, 0, 25);
            medic.OnEndTurn = (b, u) => b.Heal(MostWounded(b, u.Side, u.Sector), 2);
            var eng = City(U("U7", Loc.T("Инженеры"), 2, 1, 3, Loc.T("При раскрытии: обезвредить ловушку врага, иначе — баррикада (+2 укрытия союзникам сектора на этот ход).")), 40, 10, 10, 25);
            eng.OnReveal = (b, u) =>
            {
                if (b.ClearTrap(u.Side, u.Sector)) return;
                foreach (var a in b.UnitsOf(u.Side, u.Sector)) { a.CoverLeft += 2; b.Log(EvType.Buff, a.Side, a.Sector, a.Uid, amount: 0, text: "cover"); }
            };
            var scout = Ind(U("U8", Loc.T("Дроны-разведчики"), 1, 1, 1, Loc.T("При раскрытии: снимают одну скрытую ловушку врага в секторе."), Kw.Drone), 15, 0, 15, 1, 0.2f);
            scout.Kw &= ~Kw.Vehicle;
            scout.OnReveal = (b, u) => b.ClearTrap(u.Side, u.Sector);
            var apc = Ind(U("U9", Loc.T("БТР «Мул»"), 2, 1, 5, Loc.T("При раскрытии: высаживает пехоту ценой до 2 из колоды в этот сектор.")), 35, 10, 40, 2);
            apc.OnReveal = (b, u) =>
            {
                var deck = b.Sides[u.Side].Deck;
                for (int i = 0; i < deck.Count; i++)
                {
                    var c = deck[i];
                    if (!c.Def.Has(Kw.Infantry) || c.Def.Cost > 2 || c.Def.Kind != CardKind.Unit) continue;
                    deck.RemoveAt(i);
                    var n = b.Place(c, u.Side, u.Sector, true);
                    b.Log(EvType.Spawn, u.Side, u.Sector, n.Uid, def: c.Def, text: "apc");
                    return;
                }
            };
            Ind(U("U10", Loc.T("Танк «Бастион»"), 4, 5, 8, Loc.T("Бронебой: по технике и сооружениям — двойной урон."), Kw.Pierce), 60, 30, 60, 2);
            Ind(U("U11", Loc.T("Артиллерия"), 4, 3, 3, Loc.T("Навесной огонь: бьёт и соседний сектор половиной силы."), Kw.Indirect), 55, 15, 55, 2);
            Ind(U("U12", Loc.T("Шагоход"), 5, 6, 10, Loc.T("При раскрытии давит до двух пехотинцев врага с бронёй до 2.")).AddKw(Kw.Crush), 95, 40, 80, 3, 1f);

            // ============================== ПРИКАЗЫ ==============================
            O("O1", Loc.T("Огневой вал флота"), 3, Loc.T("2 урона каждому врагу в секторе."), (b, side, sec) =>
            { foreach (var e in b.Enemies(side, sec)) b.Damage(null, e, 2); }, 20);
            O("O2", Loc.T("Орбитальный удар"), 5, Loc.T("7 урона по сильнейшим врагам сектора, укрытие не спасает. Район сектора разрушен. Нужен флот мощью от 1500."), (b, side, sec) =>
            { b.SpreadDamage(side, sec, 7, true); b.Sectors[sec].Ruined = true; }, 35);
            O("O3", Loc.T("Десантная капсула"), 1, Loc.T("Высаживает юнит из колоды ценой до 3; капсула наносит 2 урона при посадке."), (b, side, sec) =>
            {
                var deck = b.Sides[side].Deck;
                for (int i = 0; i < deck.Count; i++)
                {
                    var c = deck[i];
                    if (c.Def.Kind != CardKind.Unit || c.Def.Cost > 3) continue;
                    deck.RemoveAt(i);
                    var n = b.Place(c, side, sec, true);
                    b.Log(EvType.Spawn, side, sec, n.Uid, def: c.Def, text: "pod");
                    var t = b.RandomEnemy(side, sec);
                    if (t != null) b.Damage(null, t, 2);
                    return;
                }
            }, 15);
            O("O4", Loc.T("Обход"), 2, Loc.T("Ваш сильнейший юнит сектора уходит в соседний, где враг слабее, и получает +2 силы на этот ход."), (b, side, sec) =>
            {
                BUnit best = null;
                foreach (var u in b.UnitsOf(side, sec)) if (!u.IsStructure && u.Immobile <= 0 && (best == null || b.Power(u) > b.Power(best))) best = u;
                if (best == null) return;
                int to = -1, weakest = int.MaxValue;
                foreach (int n in new[] { sec - 1, sec + 1 })
                {
                    if (n < 0 || n > 2) continue;
                    int p = b.SectorPower(BattleState.Enemy(side), n);
                    if (p < weakest) { weakest = p; to = n; }
                }
                if (to < 0) return;
                b.Move(best, to);
                b.Buff(best, 2, false, "flank");
            }, 15);
            O("O5", Loc.T("Дымовая завеса"), 1, Loc.T("В этом секторе в этот ход никто не стреляет."), (b, side, sec) => b.Sectors[sec].Smoke = true, 10);
            O("O6", Loc.T("Мобилизация"), 2, Loc.T("Возьмите 2 карты."), (b, side, sec) => b.DrawCards(side, 2), 20);
            O("O7", Loc.T("Диверсия"), 2, Loc.T("Сооружения врага в секторе отключены на 2 хода. В Штабе — снимает щит на ход."), (b, side, sec) =>
            {
                foreach (var e in b.Enemies(side, sec)) if (e.IsStructure) b.SetStatus(e, "disable", 2);
                var sh = b.ShieldUnit;
                if (sh != null && sh.Sector == sec && sh.Side != side) b.SetStatus(sh, "disable", 1);
            }, 25);
            O("O8", Loc.T("Пропаганда"), 2, Loc.T("Ополчение врага в секторе переходит к вам (если стабильность колонии ниже 50), иначе −1 к его силе."), (b, side, sec) =>
            {
                float stab = b.Sides[BattleState.Enemy(side)].Stability;
                foreach (var e in b.Enemies(side, sec))
                {
                    if (!e.Has(Kw.Militia)) continue;
                    if (stab < 50f) b.Convert(e, side); else b.Buff(e, -1, true, "propaganda");
                }
            }, 20);
            O("O9", Loc.T("ЭМИ-заряд"), 3, Loc.T("Техника и сооружения врага в секторе не стреляют в этот ход."), (b, side, sec) =>
            { foreach (var e in b.Enemies(side, sec)) if (e.Has(Kw.Vehicle) || e.IsStructure || e.Has(Kw.Drone)) b.SetStatus(e, "disable", 1); }, 25);

            // ============================== ОБОРОНА КОЛОНИИ ==============================
            var militia = U("D1", Loc.T("Ополчение"), 1, 1, 2, Loc.T("Жители колонии с оружием. Погибшее ополчение — минус житель."), Kw.Infantry | Kw.Militia);
            militia.Virtual = true; militia.Site = ProdSite.Garrison;
            var garrison = U("D2", Loc.T("Гарнизон"), 2, 2, 4, Loc.T("Обученные защитники колонии."));
            garrison.Site = ProdSite.Garrison; garrison.Minerals = 40; garrison.Days = 20; garrison.Upkeep = 0.2f;
            Add(new CardDef { Id = "D3", Name = Loc.T("Бункер"), Kind = CardKind.Structure, Cost = 2, Power = 0, Armor = 6, Cover = 1, Kw = Kw.None,
                Text = Loc.T("Сооружение. Все защитники сектора получают укрытие 1 в каждой перестрелке."), Site = ProdSite.Garrison, Minerals = 50, Alloys = 30, Days = 30, Size = 0,
                OnPreCombat = (b, u) => b.BunkerAura(u) });
            Add(new CardDef { Id = "D4", Name = Loc.T("Турель"), Kind = CardKind.Structure, Cost = 3, Power = 2, Armor = 4, Kw = Kw.NeedsPower,
                Text = Loc.T("Сооружение. Стреляет, пока электростанция у защитника."), Site = ProdSite.Garrison, Alloys = 45, Days = 35, Size = 0 });
            Add(new CardDef { Id = "D5", Name = Loc.T("Минное поле"), Kind = CardKind.Trap, Cost = 1, Text = Loc.T("Ловушка: 4 урона первому раскрытому врагу в секторе."),
                Site = ProdSite.Garrison, Minerals = 30, Days = 15, Size = 0, OnTrap = (b, t, victim) => b.Damage(null, victim, 4, true) });
            Add(new CardDef { Id = "D6", Name = Loc.T("Щит Штаба"), Kind = CardKind.Structure, Cost = 0, Power = 0, Armor = 12, Kw = Kw.Shield, Virtual = true,
                Text = Loc.T("Энергетический купол: по нему не стреляют. Пока электростанция у защитника, прорыв в Штаб невозможен. Снимают захват электростанции или диверсия."), Site = ProdSite.Garrison, Size = 0 });

            // ============================== ФРАКЦИИ ==============================
            // Республика Астрея — граждане, переговоры, без жестокости
            F("astrea", City(Add(new CardDef { Id = "A1", Name = Loc.T("Добровольцы Свободы"), Kind = CardKind.Unit, Cost = 1, Power = 1, Armor = 2, Kw = Kw.Infantry,
                Text = Loc.T("При раскрытии: +1 к силе за каждого союзника в секторе."),
                OnReveal = (b, u) => { int n = 0; foreach (var a in b.UnitsOf(u.Side, u.Sector)) if (a != u) n++; if (n > 0) b.Buff(u, n, true, "volunteers"); } }), 25, 10, 0, 18));
            F("astrea", Add(new CardDef { Id = "A2", Name = Loc.T("Переговорщик"), Kind = CardKind.Unit, Cost = 2, Power = 0, Armor = 3, Kw = Kw.Infantry,
                Text = Loc.T("При раскрытии: одно ополчение врага в секторе складывает оружие и уходит."), Site = ProdSite.Command, Energy = 45, Influence = 8, Days = 20, Upkeep = 0.2f,
                OnReveal = (b, u) => { foreach (var e in b.Enemies(u.Side, u.Sector)) if (e.Has(Kw.Militia)) { b.RemoveFromBoard(e, "surrender"); return; } } }));
            F("astrea", City(Add(new CardDef { Id = "A3", Name = Loc.T("Миротворцы"), Kind = CardKind.Unit, Cost = 4, Power = 4, Armor = 6, Kw = Kw.Infantry,
                Text = Loc.T("+3 к силе, если вы не применяли «Орбитальный удар» в этом бою."),
                OnReveal = (b, u) => { foreach (var c in b.Sides[u.Side].Spent) if (c.Def.Id == "O2") return; b.Buff(u, 3, true, "peace"); } }), 70, 20, 30, 40, 0.4f));
            F("astrea", O("A4", Loc.T("Ополчение Республики"), 3, Loc.T("Поднять 2 отряда ополчения в секторе (1/2)."), (b, side, sec) =>
            { b.Spawn(Militia, side, sec); b.Spawn(Militia, side, sec); }, 25));

            // Доминион Ксарн — массы, знамёна, жестокая дисциплина
            F("xarn", City(Add(new CardDef { Id = "X1", Name = Loc.T("Штрафной батальон"), Kind = CardKind.Unit, Cost = 1, Power = 4, Armor = 1, Kw = Kw.Infantry | Kw.Fragile,
                Text = Loc.T("Погибает в конце хода. Гибель штрафников злит остальных: союзники в секторе +1 к силе до конца боя."),
                OnDeath = (b, u) => { foreach (var a in b.UnitsOf(u.Side, u.Sector)) if (a != u) b.Buff(a, 1, true, "banner"); } }), 15, 5, 0, 10, 0.1f));
            F("xarn", City(Add(new CardDef { Id = "X2", Name = Loc.T("Знамя Доминиона"), Kind = CardKind.Unit, Cost = 2, Power = 1, Armor = 3, Kw = Kw.Infantry,
                Text = Loc.T("При раскрытии: все союзники в секторе +1 к силе до конца боя."),
                OnReveal = (b, u) => { foreach (var a in b.UnitsOf(u.Side, u.Sector)) if (a != u) b.Buff(a, 1, true, "banner"); } }), 40, 10, 10, 25));
            F("xarn", O("X3", Loc.T("Децимация"), 0, Loc.T("Ваш самый слабый юнит в секторе казнён; остальные +2 к силе до конца боя."), (b, side, sec) =>
            {
                BUnit weak = null;
                foreach (var u in b.UnitsOf(side, sec)) if (!u.IsStructure && (weak == null || b.Power(u) < b.Power(weak))) weak = u;
                if (weak == null) return;
                b.Kill(weak);
                foreach (var u in b.UnitsOf(side, sec)) b.Buff(u, 2, true, "decimation");
            }, 10));
            F("xarn", Ind(Add(new CardDef { Id = "X4", Name = Loc.T("Титан «Молот Ксарна»"), Kind = CardKind.Unit, Cost = 6, Power = 6, Armor = 9, Kw = Kw.Crush | Kw.Pierce,
                Text = Loc.T("Давит до двух пехотинцев с бронёй до 2. Бронебой.") }), 150, 60, 110, 4, 1.5f));

            // Синдикат Аквила — наёмники и деньги
            F("aquila", Add(new CardDef { Id = "Q1", Name = Loc.T("Наёмники «Сокол»"), Kind = CardKind.Unit, Cost = 3, Power = 4, Armor = 4, Kw = Kw.Infantry | Kw.Merc,
                Text = Loc.T("Можно купить прямо в бою за 60 гелия-3."), Site = ProdSite.City, Energy = 60, Days = 5, Upkeep = 0.3f }));
            F("aquila", O("Q2", Loc.T("Подкуп"), 2, Loc.T("Сильнейший враг сектора с силой до 3 не стреляет 2 хода."), (b, side, sec) =>
            {
                BUnit best = null;
                foreach (var e in b.Enemies(side, sec)) if (b.Power(e) <= 3 && (best == null || b.Power(e) > b.Power(best))) best = e;
                if (best != null) b.SetStatus(best, "suppress", 2);
            }, 25));
            F("aquila", O("Q3", Loc.T("Контракт на ликвидацию"), 3, Loc.T("Уничтожить вражеского героя в секторе, иначе — 4 урона сильнейшему."), (b, side, sec) =>
            {
                foreach (var e in b.Enemies(side, sec)) if (e.Def.Kind == CardKind.Hero) { b.Kill(e); return; }
                var t = b.StrongestEnemy(side, sec);
                if (t != null) b.Damage(null, t, 4, true);
            }, 35));
            F("aquila", City(Add(new CardDef { Id = "Q4", Name = Loc.T("Частная охрана"), Kind = CardKind.Unit, Cost = 2, Power = 2, Armor = 4, Cover = 1, Kw = Kw.Infantry,
                Text = Loc.T("Укрытие 1.") }), 45, 20, 10, 25, 0.3f));

            // Иридийский Оракул — предвидение и пси-сила
            F("iridia", City(Add(new CardDef { Id = "I1", Name = Loc.T("Провидица"), Kind = CardKind.Unit, Cost = 2, Power = 2, Armor = 3, Kw = Kw.Infantry,
                Text = Loc.T("При раскрытии: вы видите руку врага до конца боя."),
                OnReveal = (b, u) => { b.Sides[u.Side].SeesEnemyHand = true; b.Log(EvType.Status, u.Side, u.Sector, u.Uid, text: "foresight"); } }), 40, 20, 0, 25));
            F("iridia", O("I2", Loc.T("Видение исхода"), 1, Loc.T("Сильнейший враг сектора уходит под низ колоды владельца."), (b, side, sec) =>
            {
                BUnit best = null;
                foreach (var e in b.Enemies(side, sec))
                    if (!e.IsStructure && e.Def.Kind != CardKind.Hero && (best == null || b.Power(e) > b.Power(best))) best = e;
                if (best == null) return;
                b.RemoveFromBoard(best, "rewind");
                b.Sides[best.Side].Deck.Add(best.Card);   // под низ: вернётся нескоро, а не на следующий ход
            }, 25));
            F("iridia", City(Add(new CardDef { Id = "I3", Name = Loc.T("Хор Оракула"), Kind = CardKind.Unit, Cost = 3, Power = 3, Armor = 5, Kw = Kw.Infantry,
                Text = Loc.T("При раскрытии: +1 очко приказов в следующем ходу."),
                OnReveal = (b, u) => b.Sides[u.Side].BonusNext += 1 }), 50, 20, 0, 30));
            F("iridia", O("I4", Loc.T("Пси-шторм"), 4, Loc.T("3 урона всем врагам в секторе и 1 — своим: пси-волна не разбирает своих."), (b, side, sec) =>
            {
                foreach (var u in new List<BUnit>(b.Sectors[sec].Units)) if (u.Alive && u.Revealed && !u.IsTrap) b.Damage(null, u, u.Side == side ? 1 : 3, true);
            }, 30));

            // Хлорофиловый Конклав Тэрра'ан — споры, корни, живая крепость
            F("terraan", O("T1", Loc.T("Споровое облако"), 2, Loc.T("Все враги сектора −1 к силе до конца боя."), (b, side, sec) =>
            { foreach (var e in b.Enemies(side, sec)) b.Buff(e, -1, true, "spores"); }, 20));
            F("terraan", City(Add(new CardDef { Id = "T2", Name = Loc.T("Корни-ловушки"), Kind = CardKind.Trap, Cost = 1, Size = 0,
                Text = Loc.T("Ловушка: первый раскрытый враг в секторе обездвижен и не стреляет 2 хода."),
                OnTrap = (b, t, victim) => b.SetStatus(victim, "immobile", 2) }), 20, 10, 0, 15, 0.1f));
            F("terraan", City(Add(new CardDef { Id = "T3", Name = Loc.T("Рой опылителей"), Kind = CardKind.Unit, Cost = 2, Power = 3, Armor = 3, Kw = Kw.Swarm,
                Text = Loc.T("Рой: сила не больше оставшейся брони.") }), 30, 15, 0, 20));
            F("terraan", City(Add(new CardDef { Id = "T4", Name = Loc.T("Древо-бастион"), Kind = CardKind.Unit, Cost = 4, Power = 2, Armor = 10, Kw = Kw.None,
                Text = Loc.T("В конце хода: +1 броня всем союзникам сектора."),
                OnEndTurn = (b, u) => { foreach (var a in b.UnitsOf(u.Side, u.Sector)) b.Heal(a, 1); } }), 80, 30, 0, 45, 0.4f));

            // Машинный Консенсус «Ноль» — рои дронов, пересборка, ассимиляция
            F("nol", Ind(Add(new CardDef { Id = "N1", Name = Loc.T("Рой дронов"), Kind = CardKind.Unit, Cost = 1, Power = 3, Armor = 4, Kw = Kw.Swarm | Kw.Drone,
                Text = Loc.T("Рой: сила не больше оставшейся брони.") }), 20, 10, 15, 1, 0.2f));
            F("nol", Ind(Add(new CardDef { Id = "N2", Name = Loc.T("Узел Консенсуса"), Kind = CardKind.Unit, Cost = 3, Power = 1, Armor = 6, Kw = Kw.Drone,
                Text = Loc.T("Все ваши дроны в секторе +1 к силе в каждой перестрелке."),
                OnPreCombat = (b, u) => { foreach (var a in b.UnitsOf(u.Side, u.Sector)) if (a != u && a.Has(Kw.Drone)) b.Buff(a, 1, false, "node"); } }), 40, 20, 35, 2));
            F("nol", O("N3", Loc.T("Пересборка"), 1, Loc.T("Два погибших ваших дрона снова в строю в этом секторе."), (b, side, sec) =>
            {
                int n = 0;
                foreach (var s in b.Sectors)
                    foreach (var u in new List<BUnit>(s.Units))
                        if (n < 2 && u.Dead && !u.Hidden && u.Side == side && u.Has(Kw.Drone) && !u.IsTrap)
                        { u.Hidden = true; b.Spawn(u.Def, side, sec); n++; }   // Hidden — этот дрон уже пересобран
            }, 20));
            F("nol", O("N4", Loc.T("Ассимиляция"), 3, Loc.T("Уничтожить слабейшего врага сектора: на его месте — ваш дрон."), (b, side, sec) =>
            {
                BUnit weak = null;
                foreach (var e in b.Enemies(side, sec)) if (!e.IsStructure && (weak == null || e.Armor < weak.Armor)) weak = e;
                if (weak == null) return;
                b.Kill(weak);
                b.Spawn(Get("Z_DRONE"), side, sec);
            }, 30));

            // ============================== СЛУЖЕБНЫЕ ==============================
            var crew = U("Z_CREW", Loc.T("Морская пехота"), 1, 1, 2, Loc.T("Экипаж кораблей флота. Есть у любого флота, но потери ослабляют корабли."));
            crew.Virtual = true;
            var drone = U("Z_DRONE", Loc.T("Дрон"), 0, 1, 1, Loc.T("Ассимилированный враг."), Kw.Drone);
            drone.Virtual = true;
            Add(new CardDef { Id = "H1", Name = Loc.T("Генерал"), Kind = CardKind.Hero, Cost = 3, Power = 3, Armor = 6, Kw = Kw.Infantry, Virtual = true,
                Text = Loc.T("При раскрытии «Прорыв»: союзники в секторе +1 к силе до конца боя."),
                OnReveal = (b, u) => { foreach (var a in b.UnitsOf(u.Side, u.Sector)) if (a != u) b.Buff(a, 1, true, "general"); } });
            Add(new CardDef { Id = "H2", Name = Loc.T("Комендант"), Kind = CardKind.Hero, Cost = 3, Power = 2, Armor = 8, Kw = Kw.Infantry, Virtual = true,
                Text = Loc.T("«Стойкость»: союзники в секторе получают укрытие 1 каждый ход."),
                OnPreCombat = (b, u) => { foreach (var a in b.UnitsOf(u.Side, u.Sector)) if (a != u) a.CoverLeft += 1; } });
            FixDrones();
            ApplyTechUnlocks();
        }

        /// <summary>Дроны «Ноля» — не техника: бронебой их не удваивает.</summary>
        private static void FixDrones() { foreach (var id in new[] { "N1", "N2" }) { var d = Get(id); if (d != null) d.Kw &= ~Kw.Vehicle; } }

        /// <summary>
        /// Какая технология открывает карту (id из дерева технологий). Карт нет в списке — доступны с начала партии.
        /// Фракционные карты открыты своей фракции сразу (Unlock = "faction").
        /// </summary>
        private static readonly Dictionary<string, string> TechUnlocks = new Dictionary<string, string>
        {
            ["U3"] = "def_arm_1",   // Тяжёлая пехота — Титановая броня I
            ["U4"] = "sen_tac_1",   // Снайперы — Тактические вычислители
            ["U5"] = "ind_min_1",   // Огнемётчики — Плазменные буры
            ["U6"] = "col_gro_2",   // Медики — Биоинженерия
            ["U7"] = "cns_mod_1",   // Инженеры — Модульное строительство
            ["U8"] = "sen_ai_1",    // Дроны-разведчики — Простейший ИИ
            ["U9"] = "prp_eng_1",   // БТР «Мул» — Импульсные двигатели
            ["U10"] = "def_arm_2",  // Танк «Бастион» — Титановая броня II
            ["U11"] = "wpn_kin_2",  // Артиллерия — Кинетические орудия II
            ["U12"] = "ind_all_2",  // Шагоход — Нанофабрикация
            ["O1"] = "doc_flt_1",   // Огневой вал флота — Флотская доктрина
            ["O2"] = "wpn_las_2",   // Орбитальный удар — Лазерные батареи II
            ["O3"] = "doc_log_1",   // Десантная капсула — Военная логистика
            ["O4"] = "doc_drl_1",   // Обход — Учения флота
            ["O7"] = "sen_crp_1",   // Диверсия — Криптография
            ["O8"] = "soc_inf_1",   // Пропаганда — Дипломатическая служба
            ["O9"] = "def_shl_1",   // ЭМИ-заряд — Энергощиты I
            ["D4"] = "wpn_kin_1",   // Турель — Кинетические орудия I
            ["D5"] = "col_frt_1",   // Минное поле — Пограничные протоколы
        };

        private static void ApplyTechUnlocks()
        {
            foreach (var kv in TechUnlocks) { var d = Get(kv.Key); if (d != null && d.Faction == null) d.Unlock = kv.Value; }
        }

        /// <summary>Технология, открывающая карту (null — открыта с начала или фракционная).</summary>
        public static string TechOf(CardDef d) => d == null || string.IsNullOrEmpty(d.Unlock) || d.Unlock == "faction" ? null : d.Unlock;

        private static CardDef F(string faction, CardDef d) { d.Faction = faction; if (d.Unlock == null) d.Unlock = "faction"; return d; }

        private static CardDef AddKw(this CardDef d, Kw k) { d.Kw |= k; return d; }

        private static BUnit MostWounded(BattleState b, int side, int sector)
        {
            BUnit best = null;
            foreach (var u in b.UnitsOf(side, sector))
                if (u.Armor < u.MaxArmor && (best == null || u.MaxArmor - u.Armor > best.MaxArmor - best.Armor)) best = u;
            return best;
        }

        /// <summary>Карты, доступные империи для производства (общие + своей фракции).</summary>
        public static IEnumerable<CardDef> ForFaction(string faction)
        {
            foreach (var d in All)
                if (!d.Virtual && (d.Faction == null || d.Faction == faction)) yield return d;
        }

        /// <summary>Какая карта ополчения/наёмников и т. п. — по id (для сохранений: защищает от удалённых карт).</summary>
        public static bool Exists(string id) => Get(id) != null;
    }
}
