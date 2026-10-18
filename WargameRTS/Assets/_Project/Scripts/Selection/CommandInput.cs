using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using WargameRTS.Combat;
using WargameRTS.Core;
using WargameRTS.UI;
using WargameRTS.Units;

namespace WargameRTS.Selection
{
    /// <summary>
    /// Orders for the selected units:
    /// RMB on ground — move in formation; RMB drag — deploy along a line;
    /// RMB on a visible enemy — attack; Ctrl+RMB — attack-move; X — stop; F — toggle fog of war.
    /// </summary>
    public sealed class CommandInput : MonoBehaviour
    {
        const float LineDragMinMeters = 15f;

        Camera mainCamera;
        LineRenderer preview;
        bool rightPressed;
        Vector3 rightPressWorld;
        bool rightPressHitGround;

        void Awake()
        {
            var go = new GameObject("FormationPreview");
            go.transform.SetParent(transform, false);
            preview = go.AddComponent<LineRenderer>();
            preview.positionCount = 2;
            preview.useWorldSpace = true;
            preview.sharedMaterial = GameMaterials.Unlit(new Color(0.4f, 1f, 0.4f));
            preview.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            preview.enabled = false;
        }

        void Update()
        {
            var mouse = Mouse.current;
            var keyboard = Keyboard.current;
            if (mouse == null || keyboard == null)
                return;
            if (mainCamera == null)
                mainCamera = Camera.main;
            if (mainCamera == null || SelectionManager.Instance == null)
                return;

            var selected = SelectionManager.Instance.Selected;
            Vector2 pointer = mouse.position.ReadValue();

            if (keyboard.fKey.wasPressedThisFrame && VisibilitySystem.Instance != null)
                VisibilitySystem.Instance.FogOfWar = !VisibilitySystem.Instance.FogOfWar;

            if (keyboard.xKey.wasPressedThisFrame)
            {
                foreach (var unit in selected)
                    unit.Stop();
            }

            if (mouse.rightButton.wasPressedThisFrame && !SandboxHud.IsPointerOverHud(pointer))
            {
                rightPressed = true;
                rightPressHitGround = RaycastGround(pointer, out rightPressWorld);
            }

            if (!rightPressed)
                return;

            bool hasCurrent = RaycastGround(pointer, out var currentWorld);
            bool isLineDrag = rightPressHitGround && hasCurrent &&
                              Vector3.Distance(rightPressWorld, currentWorld) > LineDragMinMeters;

            preview.enabled = isLineDrag && selected.Count > 0;
            if (preview.enabled)
            {
                float width = Mathf.Max(0.5f, Vector3.Distance(mainCamera.transform.position, currentWorld) * 0.003f);
                preview.widthMultiplier = width;
                preview.SetPosition(0, rightPressWorld + Vector3.up * 1.5f);
                preview.SetPosition(1, currentWorld + Vector3.up * 1.5f);
            }

            if (!mouse.rightButton.wasReleasedThisFrame)
                return;

            rightPressed = false;
            preview.enabled = false;
            if (selected.Count == 0)
                return;

            bool attackMove = keyboard.ctrlKey.isPressed;
            if (isLineDrag)
            {
                IssueLineMove(selected, rightPressWorld, currentWorld, attackMove);
                return;
            }

            var enemy = SelectionManager.PickUnit(mainCamera, pointer);
            if (enemy != null && enemy.Team != TeamExtensions.Player && enemy.IsVisibleTo(TeamExtensions.Player))
            {
                foreach (var unit in selected)
                    unit.Attack(enemy);
                return;
            }

            if (hasCurrent)
                IssuePointMove(selected, currentWorld, attackMove);
        }

        static void IssuePointMove(IReadOnlyList<Unit> units, Vector3 point, bool attackMove)
        {
            Vector3 facing = point - FormationPlanner.Centroid(units);
            facing.y = 0f;
            if (facing.sqrMagnitude < 1f)
                facing = units[0].transform.forward;
            facing.Normalize();

            foreach (var slot in FormationPlanner.AroundPoint(units, point, facing))
                slot.Unit.MoveTo(slot.Position, facing, attackMove);
        }

        static void IssueLineMove(IReadOnlyList<Unit> units, Vector3 a, Vector3 b, bool attackMove)
        {
            var slots = FormationPlanner.AlongLine(units, a, b, out var facing);
            foreach (var slot in slots)
                slot.Unit.MoveTo(slot.Position, facing, attackMove);
        }

        bool RaycastGround(Vector2 pointer, out Vector3 point)
        {
            Ray ray = mainCamera.ScreenPointToRay(pointer);
            if (Physics.Raycast(ray, out var hit, 10000f, GameLayers.GroundMask | GameLayers.ObstaclesMask,
                    QueryTriggerInteraction.Ignore))
            {
                point = hit.point;
                return true;
            }
            point = default;
            return false;
        }
    }
}
