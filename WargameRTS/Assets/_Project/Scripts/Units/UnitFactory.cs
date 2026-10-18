using UnityEngine;
using UnityEngine.AI;
using WargameRTS.Core;

namespace WargameRTS.Units
{
    public static class UnitFactory
    {
        public static Unit Spawn(UnitDefinition definition, Team team, Vector3 position, Vector3 facing, Transform parent)
        {
            if (NavMesh.SamplePosition(position, out var hit, 60f, NavMesh.AllAreas))
                position = hit.position;

            facing.y = 0f;
            if (facing.sqrMagnitude < 0.01f)
                facing = Vector3.forward;

            // Build inactive so OnEnable runs only once everything is initialized.
            var go = new GameObject($"{team} {definition.DisplayName}");
            go.SetActive(false);
            go.layer = GameLayers.Units;
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(position, Quaternion.LookRotation(facing.normalized));

            var view = UnitView.Build(go, definition, team);

            // Trigger hitbox used for mouse picking only.
            var hitbox = go.AddComponent<BoxCollider>();
            hitbox.isTrigger = true;
            float pickSize = Mathf.Max(8f, view.FootprintRadius * 2f);
            hitbox.size = new Vector3(pickSize, 4f, pickSize);
            hitbox.center = new Vector3(0f, 2f, 0f);

            var agent = go.AddComponent<NavMeshAgent>();
            agent.speed = definition.SpeedMetersPerSecond;
            agent.angularSpeed = definition.TurnRate;
            agent.acceleration = Mathf.Max(4f, definition.SpeedMetersPerSecond * 1.2f);
            agent.radius = view.FootprintRadius * 0.6f;
            agent.height = 3f;
            agent.stoppingDistance = 2f;
            agent.autoBraking = true;
            agent.obstacleAvoidanceType = ObstacleAvoidanceType.MedQualityObstacleAvoidance;
            agent.avoidancePriority = Random.Range(30, 70);

            var unit = go.AddComponent<Unit>();
            unit.Initialize(definition, team, agent, view, facing);

            go.SetActive(true);
            return unit;
        }
    }
}
