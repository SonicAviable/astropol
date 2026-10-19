using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using WargameRTS.CameraRig;
using WargameRTS.Core;
using WargameRTS.UI;
using WargameRTS.Units;

namespace WargameRTS.Selection
{
    /// <summary>
    /// LMB click / drag-box selection, Shift to add, double-click to select
    /// all units of the same type on screen, Ctrl+1..9 to assign groups.
    /// </summary>
    public sealed class SelectionManager : MonoBehaviour
    {
        const float DragThresholdPixels = 6f;
        const float DoubleClickSeconds = 0.3f;

        static readonly Key[] GroupKeys =
        {
            Key.Digit0, Key.Digit1, Key.Digit2, Key.Digit3, Key.Digit4,
            Key.Digit5, Key.Digit6, Key.Digit7, Key.Digit8, Key.Digit9
        };

        readonly List<Unit> selected = new List<Unit>();
        readonly List<Unit>[] groups = new List<Unit>[GroupKeys.Length];
        readonly float[] groupLastRecall = new float[GroupKeys.Length];
        Camera mainCamera;
        Texture2D whiteTexture;
        bool pressing;
        bool dragging;
        Vector2 pressPosition;
        Unit lastClicked;
        float lastClickTime;

        public static SelectionManager Instance { get; private set; }

        public IReadOnlyList<Unit> Selected => selected;

        public bool IsDragging => dragging;

        void Awake()
        {
            Instance = this;
            for (int i = 0; i < groups.Length; i++)
                groups[i] = new List<Unit>();
            Unit.Died += OnUnitDied;
        }

        void OnDestroy()
        {
            Unit.Died -= OnUnitDied;
            if (Instance == this)
                Instance = null;
        }

        void Update()
        {
            var mouse = Mouse.current;
            var keyboard = Keyboard.current;
            if (mouse == null || keyboard == null)
                return;
            if (mainCamera == null)
                mainCamera = Camera.main;
            if (mainCamera == null)
                return;

            Vector2 pointer = mouse.position.ReadValue();
            bool additive = keyboard.shiftKey.isPressed;

            if (mouse.leftButton.wasPressedThisFrame && !SandboxHud.IsPointerOverHud(pointer) &&
                !CommandInput.BlocksSelection)
            {
                pressing = true;
                dragging = false;
                pressPosition = pointer;
            }

            if (pressing && !dragging && (pointer - pressPosition).magnitude > DragThresholdPixels)
                dragging = true;

            if (pressing && mouse.leftButton.wasReleasedThisFrame)
            {
                if (dragging)
                    BoxSelect(pressPosition, pointer, additive);
                else
                    ClickSelect(pointer, additive);
                pressing = false;
                dragging = false;
            }

            if (keyboard.escapeKey.wasPressedThisFrame && !CommandInput.BlocksSelection)
                Clear();

            HandleGroupKeys(keyboard);
        }

        void HandleGroupKeys(Keyboard keyboard)
        {
            bool assign = keyboard.ctrlKey.isPressed;
            for (int i = 0; i < GroupKeys.Length; i++)
            {
                if (!keyboard[GroupKeys[i]].wasPressedThisFrame)
                    continue;

                if (assign)
                {
                    groups[i].Clear();
                    groups[i].AddRange(selected);
                    continue;
                }

                if (groups[i].Count == 0)
                    continue;

                SetSelection(groups[i], false);

                // Double tap centers the camera on the group.
                if (Time.unscaledTime - groupLastRecall[i] < DoubleClickSeconds * 1.5f && RtsCameraController.Instance != null)
                    RtsCameraController.Instance.FocusOn(FormationPlanner.Centroid(selected));
                groupLastRecall[i] = Time.unscaledTime;
            }
        }

        void ClickSelect(Vector2 pointer, bool additive)
        {
            Unit clicked = PickUnit(mainCamera, pointer);
            if (clicked == null || clicked.Team != TeamExtensions.Player)
            {
                if (!additive)
                    Clear();
                return;
            }

            bool isDoubleClick = clicked == lastClicked && Time.unscaledTime - lastClickTime < DoubleClickSeconds;
            lastClicked = clicked;
            lastClickTime = Time.unscaledTime;

            if (isDoubleClick)
            {
                SelectAllOnScreenOfType(clicked.Definition, additive);
                return;
            }

            if (additive)
            {
                if (clicked.IsSelected)
                    Remove(clicked);
                else
                    Add(clicked);
                return;
            }

            Clear();
            Add(clicked);
        }

        void BoxSelect(Vector2 a, Vector2 b, bool additive)
        {
            var rect = Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
            if (!additive)
                Clear();

            foreach (var unit in Unit.All)
            {
                if (unit.Team != TeamExtensions.Player)
                    continue;
                Vector3 screen = mainCamera.WorldToScreenPoint(unit.Position + Vector3.up * 1.5f);
                if (screen.z > 0f && rect.Contains(new Vector2(screen.x, screen.y)))
                    Add(unit);
            }
        }

        void SelectAllOnScreenOfType(UnitDefinition definition, bool additive)
        {
            if (!additive)
                Clear();
            var screenRect = new Rect(0f, 0f, Screen.width, Screen.height);
            foreach (var unit in Unit.All)
            {
                if (unit.Team != TeamExtensions.Player || unit.Definition != definition)
                    continue;
                Vector3 screen = mainCamera.WorldToScreenPoint(unit.Position);
                if (screen.z > 0f && screenRect.Contains(new Vector2(screen.x, screen.y)))
                    Add(unit);
            }
        }

        /// <summary>Unit under the cursor (any team), or null.</summary>
        public static Unit PickUnit(Camera camera, Vector2 pointer)
        {
            Ray ray = camera.ScreenPointToRay(pointer);
            if (Physics.Raycast(ray, out var hit, 10000f, GameLayers.UnitsMask, QueryTriggerInteraction.Collide))
                return hit.collider.GetComponentInParent<Unit>();
            return null;
        }

        public void SetSelection(IEnumerable<Unit> units, bool additive)
        {
            if (!additive)
                Clear();
            foreach (var unit in units)
                Add(unit);
        }

        void Add(Unit unit)
        {
            if (unit == null || !unit.IsAlive || unit.IsSelected)
                return;
            selected.Add(unit);
            unit.SetSelected(true);
        }

        void Remove(Unit unit)
        {
            if (selected.Remove(unit))
                unit.SetSelected(false);
        }

        public void Clear()
        {
            foreach (var unit in selected)
            {
                if (unit != null)
                    unit.SetSelected(false);
            }
            selected.Clear();
        }

        void OnUnitDied(Unit unit)
        {
            selected.Remove(unit);
            foreach (var group in groups)
                group.Remove(unit);
        }

        void OnGUI()
        {
            if (!dragging || Event.current.type != EventType.Repaint || Mouse.current == null)
                return;

            if (whiteTexture == null)
                whiteTexture = Texture2D.whiteTexture;

            // GUI space has Y pointing down.
            Vector2 a = pressPosition;
            Vector2 b = Mouse.current.position.ReadValue();
            var rect = Rect.MinMaxRect(Mathf.Min(a.x, b.x), Screen.height - Mathf.Max(a.y, b.y),
                Mathf.Max(a.x, b.x), Screen.height - Mathf.Min(a.y, b.y));

            var previous = GUI.color;
            GUI.color = new Color(0.4f, 1f, 0.4f, 0.15f);
            GUI.DrawTexture(rect, whiteTexture);
            GUI.color = new Color(0.4f, 1f, 0.4f, 0.9f);
            GUI.DrawTexture(new Rect(rect.xMin, rect.yMin, rect.width, 1f), whiteTexture);
            GUI.DrawTexture(new Rect(rect.xMin, rect.yMax - 1f, rect.width, 1f), whiteTexture);
            GUI.DrawTexture(new Rect(rect.xMin, rect.yMin, 1f, rect.height), whiteTexture);
            GUI.DrawTexture(new Rect(rect.xMax - 1f, rect.yMin, 1f, rect.height), whiteTexture);
            GUI.color = previous;
        }
    }
}
