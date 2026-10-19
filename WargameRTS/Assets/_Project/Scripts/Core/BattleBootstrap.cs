using System.Collections.Generic;
using UnityEngine;
using WargameRTS.AI;
using WargameRTS.CameraRig;
using WargameRTS.Combat;
using WargameRTS.Map;
using WargameRTS.Sandbox;
using WargameRTS.Selection;
using WargameRTS.UI;
using WargameRTS.Units;

namespace WargameRTS.Core
{
    /// <summary>
    /// Entry point of the Battle scene. Builds everything procedurally from
    /// <see cref="MatchSettings"/>: lighting, map, NavMesh, camera, game systems
    /// and (optionally) both starting armies. Played directly from the editor it
    /// starts the sandbox with starting armies.
    /// </summary>
    public sealed class BattleBootstrap : MonoBehaviour
    {
        void Awake()
        {
            Time.timeScale = 1f;
            InputGate.MenuOpen = false;
            var mode = MatchSettings.Mode;
            BattleRules.Reset(mode);
            // The sandbox started from the menu respects its "enemy AI" option; otherwise the AI attacks.
            BattleRules.EnemyAIEnabled = mode == GameMode.Skirmish || !MatchSettings.ConfiguredFromMenu || MatchSettings.EnemyAI;

            Random.InitState(MatchSettings.Seed);
            SceneSetup.SetupLighting();

            var map = new GameObject("Map").AddComponent<GameMap>();
            map.Generate(MatchSettings.Seed, MatchSettings.MapSize);
            Physics.SyncTransforms();
            map.BuildNavMesh();

            var systems = new GameObject("Systems");
            systems.AddComponent<EffectsManager>();
            systems.AddComponent<ShellManager>();
            var visibility = systems.AddComponent<VisibilitySystem>();
            visibility.FogOfWar = MatchSettings.FogOfWar;
            systems.AddComponent<SelectionManager>();
            systems.AddComponent<CommandInput>();
            systems.AddComponent<SimpleEnemyAI>().Configure(TeamExtensions.Player.Enemy());
            systems.AddComponent<PauseMenu>();

            var unitsRoot = new GameObject("Units").transform;
            if (mode == GameMode.Sandbox)
                systems.AddComponent<SandboxController>().SetUnitsRoot(unitsRoot);
            systems.AddComponent<BattleHud>();

            UnitCatalog.EnsureCreated();
            if (mode == GameMode.Skirmish || MatchSettings.StartingArmies)
                SpawnStartingArmies(unitsRoot);

            var camera = SceneSetup.SetupCamera();
            var controller = camera.GetComponent<RtsCameraController>();
            if (controller == null)
                controller = camera.gameObject.AddComponent<RtsCameraController>();
            controller.SetView(map.OnGround(new Vector3(0f, 0f, -map.HalfSize + 330f)), 0f, 420f);
        }

        /// <summary>Both armies in their deployment strips (Blue south, Red north).</summary>
        public static void SpawnStartingArmies(Transform parent)
        {
            var map = GameMap.Instance;
            if (map == null)
                return;
            UnitCatalog.EnsureCreated();
            SpawnArmy(Team.Blue, map, parent);
            SpawnArmy(Team.Red, map, parent);
            Physics.SyncTransforms();
        }

        static void SpawnArmy(Team team, GameMap map, Transform parent)
        {
            float side = team == Team.Blue ? -1f : 1f;
            float frontZ = side * (map.HalfSize - 230f);
            var facing = new Vector3(0f, 0f, -side);

            var rows = new List<(UnitDefinition definition, int count, float depth)>
            {
                (UnitCatalog.Recon, 2, 0f),
                (UnitCatalog.Infantry, 6, 40f),
                (UnitCatalog.Tank, 4, 80f),
                (UnitCatalog.Ifv, 3, 120f),
                (UnitCatalog.AntiTank, 2, 150f),
                (UnitCatalog.Howitzer, 2, 190f)
            };

            foreach (var (definition, count, depth) in rows)
            {
                float z = frontZ + side * depth;
                float spacing = definition.IsInfantry ? 45f : 55f;
                for (int i = 0; i < count; i++)
                {
                    float x = (i - (count - 1) * 0.5f) * spacing;
                    UnitFactory.Spawn(definition, team, map.OnGround(new Vector3(x, 0f, z)), facing, parent);
                }
            }
        }
    }
}
