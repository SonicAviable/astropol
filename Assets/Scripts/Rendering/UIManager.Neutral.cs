using System;
using UnityEngine;
using StellarisClone.Core;
using Sfx = StellarisClone.Core.Audio.Sfx;

namespace StellarisClone.Rendering
{
    /// <summary>
    /// Карточки вольного мира в панели системы: характер, гарнизон, расположение империй, покровитель,
    /// просьба и действия — посольство, дар, обмен, наём, война и мир. Во время войны — ход блокады.
    /// </summary>
    public partial class UIManager
    {
        private float _neutralWarConfirm = -10f;

        private void BuildNeutralCards(StarSystem system, NeutralWorld w)
        {
            var kind = w.Kind;
            Color tone = NeutralWorlds.ColorOf(kind);
            InspSection(Loc.T("ВОЛЬНЫЙ МИР"), NeutralWorlds.IconOf(kind));
            InspNote(NeutralWorlds.IconOf(kind), $"<b>{NeutralWorlds.Title(w)}</b>  ·  {NeutralWorlds.CharacterName(kind)}", tone);
            InspText($"<color=#AAB4C0>{NeutralWorlds.Description(kind)}</color>");
            var home = EmpireStats.GetSystem(w.SystemId);
            int size = 1 + (w.Territory != null ? w.Territory.Count : 0);
            InspText(system.Id != w.SystemId
                ? Loc.F("<color=#8AA2A8>Присоединённая система. Родная система мира — {0}. Территория: {1} из {2}</color>", home != null ? home.Name : "?", size, 1 + NeutralWorlds.MaxTerritory)
                : Loc.F("<color=#8AA2A8>Территория: {0} из {1} систем. Вольный мир очень медленно растёт — раз в 10–15 лет</color>", size, 1 + NeutralWorlds.MaxTerritory));

            float favor = w.FavorOf(0);
            Color fc = favor >= NeutralWorlds.FriendFavor ? DS.Green : favor < 0f ? DS.Red : DS.TextMuted;
            var guards = NeutralWorlds.Garrison(w);
            InspChips(
                (LGIcon.Population, NeutralWorlds.TotalPop(w).ToString(), Loc.T("жителей"), DS.NeonCyan),
                (LGIcon.Fleet, $"{guards.Count}/{NeutralWorlds.GarrisonTarget(w)}", Loc.T("стража"), DS.Red),
                (LGIcon.Handshake, w.AtWarWith(0) ? Loc.T("война") : $"{favor:+0;-0;0}", Loc.T("расположение"), fc));
            InspText(Loc.F("<color=#8AA2A8>Звёздная база здесь:</color> мощь {0:N0}   <color=#8AA2A8>Флот стражи:</color> мощь {1:N0}\n<color=#8AA2A8>Колоний:</color> {2} из {3}",
                NeutralWorlds.StationDefense(w, system.Id), NeutralWorlds.GarrisonPower(w), NeutralWorlds.ColonyCount(w), NeutralWorlds.MaxColonies));

            // Покровитель и что он получает
            string patron = w.Patron < 0 ? Loc.T("нет")
                          : w.Patron == 0 ? Loc.T("<color=#6FE09A>вы</color>")
                          : Contacts.NameForPlayer(w.Patron, Loc.T("другая империя"));
            InspText(Loc.F("<color=#8AA2A8>Покровитель:</color> {0}\n<color=#8AA2A8>Покровителю:</color> {1}\n<color=#8AA2A8>Другу (от {2:0}):</color> 40% этого и проход через систему",
                patron, NeutralWorlds.PatronBonus(w), NeutralWorlds.FriendFavor));

            // Расположение знакомых империй — с кем идёт борьба за влияние
            var rivals = new System.Text.StringBuilder();
            foreach (var ai in AIEmpireManager.Alive)
            {
                if (!Contacts.PlayerMet(ai.OwnerId) || !w.Met(ai.OwnerId)) continue;
                float f = w.FavorOf(ai.OwnerId);
                string c = w.AtWarWith(ai.OwnerId) ? "#FF7A7A" : f >= NeutralWorlds.FriendFavor ? "#6FE09A" : "#C8D4DA";
                rivals.Append(rivals.Length > 0 ? "   " : "")
                      .Append($"<color={LGBuild.Hex(FleetIndicator.OwnerColor(ai.OwnerId))}>■</color> {ai.AIName} <color={c}>{(w.AtWarWith(ai.OwnerId) ? Loc.T("война") : f.ToString("+0;-0;0"))}</color>");
            }
            if (rivals.Length > 0) InspText(Loc.T("<color=#8AA2A8>Соперники:</color> ") + rivals);

            if (!w.Met(0))
            {
                InspNote(LGIcon.Sensors, Loc.T("Нет контакта: подведите корабль или границу ближе (2 прыжка)"), new Color(1f, 0.67f, 0.53f));
                return;
            }

            if (w.AtWarWith(0))
            {
                BuildNeutralSiege(system, w);
                return;
            }

            InspSection(Loc.T("ДЕЙСТВИЯ"), LGIcon.Diplomacy);
            if (w.QuestRes >= 0)
            {
                InspNote(LGIcon.Gift, Loc.F("{0} · ещё {1} мес.", NeutralWorlds.QuestText(w), w.QuestMonths), DS.Gold);
                InspButton(Loc.F("Выполнить просьбу  · +{0:0}", NeutralWorlds.QuestFavor), LGIcon.Gift, DS.BtnSuccess,
                    NeutralWorlds.QuestBlocker(w, 0), Loc.T("Кто поможет первым, тот получит расположение"),
                    () => NeutralWorlds.FulfilQuest(w, 0), system);
            }
            InspButton(Loc.F("Посольство  · {0:0} влияния", NeutralWorlds.EnvoyInfluence), LGIcon.Handshake, DS.BtnPrimary,
                NeutralWorlds.EnvoyBlocker(w, 0), Loc.F("+{0:0} расположения. Раз в {1} мес.", NeutralWorlds.EnvoyFavor, NeutralWorlds.EnvoyMonths),
                () => NeutralWorlds.SendEnvoy(w, 0), system);
            string wants = NeutralWorlds.Wants(kind);
            InspButton(Loc.F("Дар  · {0:0} {1}", NeutralWorlds.GiftAmount(w), NeutralWorlds.ResName(wants)), LGIcon.Gift, DS.BtnPrimary,
                NeutralWorlds.GiftBlocker(w, 0), Loc.F("+{0:0} расположения. Раз в {1} мес.", NeutralWorlds.GiftFavor, NeutralWorlds.GiftMonths),
                () => NeutralWorlds.SendGift(w, 0), system);
            string sells = NeutralWorlds.Sells(kind);
            if (sells != null)
                InspButton(Loc.F("Обмен  · {0:0} {1} → {2:0} {3}", NeutralWorlds.TradeGive, NeutralWorlds.ResName(wants), NeutralWorlds.TradeReturn(w, 0), NeutralWorlds.ResName(sells)),
                    LGIcon.Trade, DS.BtnPrimary, NeutralWorlds.TradeBlocker(w, 0),
                    Loc.F("Караван раз в месяц, +{0:0} расположения. Чем выше расположение, тем выгоднее цена; покровителю — скидка", NeutralWorlds.TradeFavor),
                    () => NeutralWorlds.Trade(w, 0), system);
            if (kind == NeutralCharacter.Mercenary)
            {
                float k = NeutralWorlds.HireMult(w, 0);
                InspButton(Loc.F("Нанять фрегат  · {0:0} сплавов, {1:0} гелия-3", NeutralWorlds.HireAlloys * k, NeutralWorlds.HireEnergy * k), LGIcon.Ship, DS.BtnPrimary,
                    NeutralWorlds.HireBlocker(w, 0), Loc.F("Отряд прибудет в столицу. Раз в {0} мес.; покровителю — на 40% дешевле", NeutralWorlds.HireMonths),
                    () => NeutralWorlds.Hire(w, 0), system);
            }

            bool confirming = Time.unscaledTime - _neutralWarConfirm < 3f;
            InspButton(confirming ? Loc.T("Точно? Нажмите ещё раз") : Loc.T("Объявить войну"), LGIcon.Swords, DS.BtnDanger,
                NeutralWorlds.WarBlocker(w, 0),
                Loc.F("Захват — блокадой каждой системы: разбейте стражу, затем держите флот сильнее базы ({0:0} дн. в родной, {1:0} — в присоединённых). Остальные вольные миры отвернутся от вас, репутация пострадает",
                    NeutralWorlds.SiegeNeed(w), NeutralWorlds.TerritorySiegeDays),
                () =>
                {
                    if (Time.unscaledTime - _neutralWarConfirm < 3f) { _neutralWarConfirm = -10f; NeutralWorlds.DeclareWar(w, 0); }
                    else _neutralWarConfirm = Time.unscaledTime;
                }, system);
        }

        private void BuildNeutralSiege(StarSystem system, NeutralWorld w)
        {
            InspSection(Loc.T("ШТУРМ"), LGIcon.Siege);
            float need = NeutralWorlds.SiegeNeed(w, system.Id);
            float defense = NeutralWorlds.StationDefense(w, system.Id);
            var fm = FleetManager.Instance;
            float mine = fm != null ? fm.GetMilitaryPowerInSystem(0, system.Id) : 0f;
            float guards = fm != null ? fm.GetMilitaryPowerInSystem(w.FleetOwner, system.Id) : 0f;
            var g = NeutralWorlds.SiegeOf(w, system.Id);
            InspBar(LGIcon.Siege, Loc.T("Блокада"), g != null && g.Besieger == 0 ? g.Days : 0f, need, DS.Red);
            string state = mine <= 0f ? Loc.T("Приведите сюда военный флот")
                         : guards > 0f ? Loc.F("В системе стража мира (мощь {0:N0}): сначала разбейте её", guards)
                         : mine < defense ? Loc.F("Флот слабее базы ({0:N0} против {1:N0}): штурм не идёт, корабли под огнём", mine, defense)
                         : g != null && g.Besieger == 0 ? Loc.F("Блокада идёт: {0:0} из {1:0} дн.", g.Days, need)
                         : g != null && g.Besieger > 0 ? Loc.T("Систему уже блокирует другая империя")
                         : Loc.T("Блокада начинается");
            InspText($"<color=#8AA2A8>{state}</color>");
            InspText(system.Id == w.SystemId
                ? Loc.T("<color=#8AA2A8>Родная система: с ней перейдёт всё, что осталось от мира</color>")
                : Loc.T("<color=#8AA2A8>Присоединённая система: перейдёт к вам отдельно, мир продолжит жить</color>"));
            InspButton(Loc.F("Заключить мир  · {0:0} влияния", NeutralWorlds.PeaceInfluence), LGIcon.Peace, DS.BtnPrimary,
                NeutralWorlds.PeaceBlocker(w, 0), Loc.F("Перемирие на {0} мес.; расположение останется низким", NeutralWorlds.TruceMonths),
                () => NeutralWorlds.MakePeace(w, 0), system);
        }

        /// <summary>Кнопка действия в панели: недоступна — с причиной в подсказке; после нажатия панель обновляется.</summary>
        private void InspButton(string label, LGIcon icon, Color tint, string blocker, string tip, Action act, StarSystem system)
        {
            bool can = blocker == null;
            var btn = InspGameButton(_inspStack, "Act", icon, label, AccentOf(tint), can, () =>
                {
                    if (!can) return;
                    SFXManager.Play(Sfx.UiClick);
                    act?.Invoke();
                    RefreshResourceBar();
                    if (_activeSystem == system && !_isInSystemMode) ShowSystemPanel(system);
                }, new Vector2(InspectorWidth - 46f, 40f));
            LGBuild.Height(btn.gameObject, 40);
            TooltipHelper.Attach(btn.gameObject, can ? tip : $"{tip}\n<color=#FF8080>{blocker}</color>");
        }
    }
}
