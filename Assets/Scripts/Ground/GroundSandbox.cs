using UnityEngine;
using StellarisClone.Core;
using StellarisClone.Rendering;

namespace StellarisClone.Ground
{
    /// <summary>
    /// Отладочный бой (Shift+F9 в редакторе и dev-сборке): игрок-штурмовик против ИИ-гарнизона
    /// на случайной планете. Стратегия не меняется — итоги никуда не применяются.
    /// </summary>
    public static class GroundSandbox
    {
        public static bool Allowed => Application.isEditor || Debug.isDebugBuild;

        public static void Launch()
        {
            if (!Allowed || GroundBattleManager.InBattle || !UIManager.IsGameStarted) return;
            var types = new[] { PlanetType.Continental, PlanetType.Desert, PlanetType.Molten, PlanetType.Barren };
            var rng = new System.Random();
            var type = types[rng.Next(types.Length)];
            string playerKey = WonderManager.KeyOf(0) ?? "astrea";
            string[] keys = { "xarn", "astrea", "aquila", "iridia", "terraan", "nol" };
            string enemyKey = keys[rng.Next(keys.Length)];

            var setup = new GroundBattleSetup
            {
                PlanetName = Loc.T("Полигон"),
                Planet = type,
                Fortification = rng.Next(0, 4),
                Population = 10,
                Stability = 50f,
                Urban = 2, Mining = 1, Generator = 1, Industrial = 1,
                GarrisonSquads = 4,
                Seed = rng.Next(1, int.MaxValue),
                Sandbox = true,
                Attacker = new GroundSideSetup
                {
                    Owner = 0, FactionKey = playerKey, Color = FleetIndicator.OwnColor, Slots = 60, IsPlayer = true,
                    FleetPower = 15000f, GeneralName = Loc.T("Генерал"), GeneralLevel = 2, Blood = FactionBlood.Faction(playerKey)
                },
                Defender = new GroundSideSetup
                {
                    Owner = 1, FactionKey = enemyKey, Color = new Color(1f, 0.35f, 0.3f), Slots = 50,
                    Blood = FactionBlood.Faction(enemyKey)
                }
            };
            GroundBattleManager.Ensure().Begin(setup, null);
        }
    }
}
