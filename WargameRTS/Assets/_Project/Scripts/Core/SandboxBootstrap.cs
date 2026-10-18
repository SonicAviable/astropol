using System.Collections.Generic;
using UnityEngine;
using WargameRTS.AI;
using WargameRTS.CameraRig;
using WargameRTS.Combat;
using WargameRTS.Map;
using WargameRTS.Selection;
using WargameRTS.UI;
using WargameRTS.Units;

namespace WargameRTS.Core
{
    /// <summary>
    /// Entry point of the Sandbox scene. Builds everything procedurally on Play:
    /// lighting, map, NavMesh, camera, game systems and both armies.
    /// </summary>
    public sealed class SandboxBootstrap : MonoBehaviour
    {
        [SerializeField] int seed = 1944;
        [SerializeField] bool randomSeed;
        [SerializeField] float mapSize = 2000f;
        [SerializeField] bool enemyAI = true;

        void Awake()
        {
            int actualSeed = randomSeed ? Random.Range(0, int.MaxValue) : seed;
            Random.InitState(actualSeed);

            SetupLighting();

            var map = new GameObject("Map").AddComponent<GameMap>();
            map.Generate(actualSeed, mapSize);
            Physics.SyncTransforms();
            map.BuildNavMesh();

            var systems = new GameObject("Systems");
            systems.AddComponent<EffectsManager>();
            systems.AddComponent<VisibilitySystem>();
            systems.AddComponent<SelectionManager>();
            systems.AddComponent<CommandInput>();
            systems.AddComponent<SandboxHud>();
            if (enemyAI)
                systems.AddComponent<SimpleEnemyAI>().Configure(TeamExtensions.Player.Enemy());

            UnitCatalog.EnsureCreated();
            var unitsRoot = new GameObject("Units").transform;
            SpawnArmy(Team.Blue, map, unitsRoot);
            SpawnArmy(Team.Red, map, unitsRoot);
            Physics.SyncTransforms();

            SetupCamera(map);
        }

        static void SetupLighting()
        {
            var sun = Object.FindAnyObjectByType<Light>();
            if (sun == null || sun.type != LightType.Directional)
            {
                sun = new GameObject("Sun").AddComponent<Light>();
                sun.type = LightType.Directional;
            }
            sun.transform.rotation = Quaternion.Euler(48f, -35f, 0f);
            sun.color = new Color(1f, 0.95f, 0.86f);
            sun.intensity = 1.3f;
            sun.shadows = LightShadows.Soft;
            RenderSettings.sun = sun;

            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.55f, 0.62f, 0.72f);
            RenderSettings.ambientEquatorColor = new Color(0.42f, 0.44f, 0.42f);
            RenderSettings.ambientGroundColor = new Color(0.2f, 0.2f, 0.17f);

            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = new Color(0.62f, 0.68f, 0.74f);
            RenderSettings.fogStartDistance = 1800f;
            RenderSettings.fogEndDistance = 5000f;
        }

        static void SpawnArmy(Team team, GameMap map, Transform parent)
        {
            // Blue deploys in the south facing north, Red in the north facing south.
            float side = team == Team.Blue ? -1f : 1f;
            float frontZ = side * (map.HalfSize - 230f);
            Vector3 facing = new Vector3(0f, 0f, -side);

            var rows = new List<(UnitDefinition definition, int count, float depth)>
            {
                (UnitCatalog.Recon, 2, 0f),
                (UnitCatalog.Infantry, 6, 40f),
                (UnitCatalog.Tank, 4, 80f),
                (UnitCatalog.Ifv, 3, 120f),
                (UnitCatalog.AntiTank, 2, 150f)
            };

            foreach (var (definition, count, depth) in rows)
            {
                float z = frontZ + side * depth;
                float spacing = definition.IsInfantry ? 45f : 55f;
                for (int i = 0; i < count; i++)
                {
                    float x = (i - (count - 1) * 0.5f) * spacing;
                    Vector3 position = map.OnGround(new Vector3(x, 0f, z));
                    UnitFactory.Spawn(definition, team, position, facing, parent);
                }
            }
        }

        static void SetupCamera(GameMap map)
        {
            var camera = Camera.main;
            if (camera == null)
            {
                var go = new GameObject("Main Camera") { tag = "MainCamera" };
                camera = go.AddComponent<Camera>();
                go.AddComponent<AudioListener>();
            }

            camera.fieldOfView = 40f;
            camera.nearClipPlane = 0.5f;
            camera.farClipPlane = 8000f;
            camera.clearFlags = CameraClearFlags.Skybox;

            var controller = camera.GetComponent<RtsCameraController>();
            if (controller == null)
                controller = camera.gameObject.AddComponent<RtsCameraController>();

            Vector3 blueFront = map.OnGround(new Vector3(0f, 0f, -map.HalfSize + 330f));
            controller.SetView(blueFront, 0f, 420f);
        }
    }
}
