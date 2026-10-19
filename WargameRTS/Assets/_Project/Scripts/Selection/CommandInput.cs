using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using WargameRTS.Combat;
using WargameRTS.Core;
using WargameRTS.UI;
using WargameRTS.Units;

namespace WargameRTS.Selection
{
    /// <summary>Order chosen on the order panel, waiting for a left click on the map.</summary>
    public enum PendingOrder
    {
        None,
        Move,
        FastMove,
        Attack,
        AttackMove
    }

    /// <summary>
    /// Orders for the selected units:
    /// RMB on ground — move in formation; RMB drag — deploy along a line;
    /// RMB on a visible enemy — attack; Ctrl+RMB — attack-move.
    /// Hotkeys: X — stop, H — cycle fire mode, B — retreat, F — toggle fog of war.
    /// Order panel buttons arm a <see cref="PendingOrder"/> that the next LMB executes.
    /// </summary>
    public sealed class CommandInput : MonoBehaviour
    {
        const float LineDragMinMeters = 15f;

        Camera mainCamera;
        LineRenderer preview;
        bool rightPressed;
        Vector3 rightPressWorld;
        bool rightPressHitGround;

        public static CommandInput Instance { get; private set; }

        PendingOrder pending;

        public PendingOrder Pending
        {
            get => pending;
            private set
            {
                pending = value;
                InputGate.SetCapture(this, value != PendingOrder.None);
            }
        }

        public bool IsTargeting => Pending != PendingOrder.None;

        void Awake()
        {
            Instance = this;

            var go = new GameObject("FormationPreview");
            go.transform.SetParent(transform, false);
            preview = go.AddComponent<LineRenderer>();
            preview.positionCount = 2;
            preview.useWorldSpace = true;
            preview.sharedMaterial = GameMaterials.Unlit(new Color(0.4f, 1f, 0.4f));
            preview.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            preview.enabled = false;
        }

        void OnDestroy()
        {
            InputGate.SetCapture(this, false);
            if (Instance == this)
                Instance = null;
        }

        // ------------------------------------------------------------------
        // Commands (used by hotkeys and by the order panel)
        // ------------------------------------------------------------------

        static IReadOnlyList<Unit> Selected =>
            SelectionManager.Instance != null ? SelectionManager.Instance.Selected : System.Array.Empty<Unit>();

        public void BeginTargeting(PendingOrder order)
        {
            Pending = Selected.Count > 0 ? order : PendingOrder.None;
        }

        public void CancelTargeting() => Pending = PendingOrder.None;

        public void StopSelected()
        {
            foreach (var unit in Selected)
                unit.Stop();
        }

        /// <summary>Moves the selection to a world point in formation (used by the minimap).</summary>
        public void MoveSelectedTo(Vector3 point, bool attackMove)
        {
            var selected = Selected;
            if (selected.Count > 0)
                IssuePointMove(selected, point, attackMove ? OrderType.AttackMove : OrderType.Move);
        }

        public void RetreatSelected()
        {
            foreach (var unit in Selected)
                unit.Retreat();
        }

        public void SetFireModeSelected(FireMode mode)
        {
            foreach (var unit in Selected)
                unit.SetFireMode(mode);
        }

        /// <summary>Fire mode shared by all selected units, or null if mixed/empty.</summary>
        public static FireMode? SelectedFireMode()
        {
            FireMode? result = null;
            foreach (var unit in Selected)
            {
                if (result.HasValue && result.Value != unit.FireMode)
                    return null;
                result = unit.FireMode;
            }
            return result;
        }

        void CycleFireMode()
        {
            var current = SelectedFireMode() ?? FireMode.HoldFire;
            var next = current == FireMode.FireAtWill ? FireMode.ReturnFire
                : current == FireMode.ReturnFire ? FireMode.HoldFire
                : FireMode.FireAtWill;
            SetFireModeSelected(next);
        }

        // ------------------------------------------------------------------
        // Input
        // ------------------------------------------------------------------

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

            if (InputGate.MenuOpen)
            {
                rightPressed = false;
                preview.enabled = false;
                return;
            }

            var selected = Selected;
            Vector2 pointer = mouse.position.ReadValue();
            bool overHud = HudStyle.IsPointerOverHud(pointer);

            if (selected.Count == 0)
                Pending = PendingOrder.None;

            HandleHotkeys(keyboard);

            if (IsTargeting)
            {
                HandleTargeting(mouse, keyboard, pointer, overHud, selected);
                return;
            }

            HandleRightButton(mouse, keyboard, pointer, overHud, selected);
        }

        void HandleHotkeys(Keyboard keyboard)
        {
            if (keyboard.fKey.wasPressedThisFrame && VisibilitySystem.Instance != null)
                VisibilitySystem.Instance.FogOfWar = !VisibilitySystem.Instance.FogOfWar;
            if (keyboard.xKey.wasPressedThisFrame)
                StopSelected();
            if (keyboard.hKey.wasPressedThisFrame)
                CycleFireMode();
            if (keyboard.bKey.wasPressedThisFrame)
                RetreatSelected();

            if (keyboard.escapeKey.wasPressedThisFrame && IsTargeting)
            {
                Pending = PendingOrder.None;
                InputGate.Consume();
            }
        }

        void HandleTargeting(Mouse mouse, Keyboard keyboard, Vector2 pointer, bool overHud, IReadOnlyList<Unit> selected)
        {
            if (mouse.rightButton.wasPressedThisFrame)
            {
                // RMB cancels the armed order instead of issuing a normal one.
                Pending = PendingOrder.None;
                InputGate.Consume();
                return;
            }

            if (!mouse.leftButton.wasPressedThisFrame || overHud || InputGate.ConsumedThisFrame)
                return;

            InputGate.Consume();
            var order = Pending;
            if (!keyboard.shiftKey.isPressed)
                Pending = PendingOrder.None;

            if (order == PendingOrder.Attack && TryAttackUnder(pointer, selected))
                return;

            if (!RaycastGround(pointer, out var point))
                return;

            var moveType = order == PendingOrder.FastMove ? OrderType.FastMove
                : order == PendingOrder.Move ? OrderType.Move
                : OrderType.AttackMove;
            IssuePointMove(selected, point, moveType);
        }

        void HandleRightButton(Mouse mouse, Keyboard keyboard, Vector2 pointer, bool overHud, IReadOnlyList<Unit> selected)
        {
            // Another mode (e.g. sandbox placement) owns the mouse: RMB cancels it, not an order.
            if (mouse.rightButton.wasPressedThisFrame && !overHud && !InputGate.BlocksSelection)
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

            var moveType = keyboard.ctrlKey.isPressed ? OrderType.AttackMove : OrderType.Move;
            if (isLineDrag)
            {
                IssueLineMove(selected, rightPressWorld, currentWorld, moveType);
                return;
            }

            if (TryAttackUnder(pointer, selected))
                return;

            if (hasCurrent)
                IssuePointMove(selected, currentWorld, moveType);
        }

        /// <summary>
        /// If a visible unit hostile to some of the selected units is under the cursor,
        /// those units attack it (the rest move next to it). Returns false otherwise.
        /// </summary>
        bool TryAttackUnder(Vector2 pointer, IReadOnlyList<Unit> selected)
        {
            var target = SelectionManager.PickUnit(mainCamera, pointer);
            if (target == null)
                return false;

            bool fogOff = VisibilitySystem.Instance != null && !VisibilitySystem.Instance.FogOfWar;
            if (!fogOff && !target.IsVisibleTo(TeamExtensions.Player))
                return false;

            bool anyHostile = false;
            foreach (var unit in selected)
                anyHostile |= unit.Team != target.Team;
            if (!anyHostile)
                return false;

            foreach (var unit in selected)
            {
                if (unit.Team != target.Team)
                    unit.Attack(target);
            }
            return true;
        }

        static void IssuePointMove(IReadOnlyList<Unit> units, Vector3 point, OrderType moveType)
        {
            Vector3 facing = point - FormationPlanner.Centroid(units);
            facing.y = 0f;
            if (facing.sqrMagnitude < 1f)
                facing = units[0].transform.forward;
            facing.Normalize();

            foreach (var slot in FormationPlanner.AroundPoint(units, point, facing))
                slot.Unit.MoveTo(slot.Position, facing, moveType);
        }

        static void IssueLineMove(IReadOnlyList<Unit> units, Vector3 a, Vector3 b, OrderType moveType)
        {
            var slots = FormationPlanner.AlongLine(units, a, b, out var facing);
            foreach (var slot in slots)
                slot.Unit.MoveTo(slot.Position, facing, moveType);
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
