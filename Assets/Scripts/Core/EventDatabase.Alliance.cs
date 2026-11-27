namespace StellarisClone.Core
{
    /// <summary>События союза: союзника атаковали — вступить в войну или отказать.</summary>
    public static partial class EventDatabase
    {
        private static void AddAllianceEvents()
        {
            _art["ally_call"] = "LegionReview";

            Add(new Template
            {
                Id = "ally_call", When = Trigger.Chain,
                CanFire = c =>
                {
                    var ally = c != null ? AIEmpireManager.For(c.Ally) : null;
                    var enemy = c != null ? AIEmpireManager.For(c.Rival) : null;
                    return ally != null && enemy != null && !ally.IsEliminated && !enemy.IsEliminated && ally.HasAlliance
                           && AIRelations.AtWar(ally.OwnerId, enemy.OwnerId) && !Diplomacy.AtWar(0, enemy.OwnerId);
                },
                Build = c =>
                {
                    var ally = AIEmpireManager.For(c.Ally);
                    var enemy = AIEmpireManager.For(c.Rival);
                    string pact = enemy.HasPact
                        ? Loc.F(" Пакт о ненападении с {0} будет расторгнут, но защиту союзника вероломством никто не сочтёт.", enemy.AIName)
                        : "";
                    return Ev("ally_call", Loc.T("СОЮЗНИК ПРОСИТ О ПОМОЩИ"),
                        Loc.F("Империя {0} напала на {1}. Посол союзника ждёт ответа: по договору мы обязаны вступить в войну. ", enemy.AIName, ally.AIName) +
                        Loc.T("Вся галактика смотрит, держим ли мы слово.") + pact,
                        AnomalyType.Diplomatic, Opts(
                            new EventOption(Loc.T("ВСТУПИТЬ В ВОЙНУ"),
                                Loc.F("Война с {0}. Отношение {1} +15, репутация +5.", enemy.AIName, ally.AIName),
                                () => ally.PlayerHonorAlliance(enemy)),
                            new EventOption(Loc.T("ОТКАЗАТЬ"),
                                Loc.F("Союз с {0} распадается: отношение −30, репутация −15 у всех знакомых империй.", ally.AIName),
                                () => ally.PlayerAbandonAlliance(enemy))));
                }
            });
        }
    }
}
