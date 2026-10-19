using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using WargameRTS.Combat;
using WargameRTS.Core;
using WargameRTS.Map;
using WargameRTS.Selection;
using WargameRTS.UI;
using WargameRTS.Units;

namespace WargameRTS.Sandbox
{
    /// <summary>
    /// Broken Arrow-style test sandbox: spawn any unit for either side in batches,
    /// control time, toggle AI / fog / invulnerability, command both sides,
    /// heal or delete units. GUI is drawn by BattleHud via <see cref="DrawGui"/>.
    /// </summary>
    public sealed class SandboxController : MonoBehaviour
    {
        const float CardWidth = 92f;
        const float CardHeight = 104f;
        const float CardGap = 6f;
        const float SpawnSpacing = 18f;

        static readonly string[] TimeLabels = { "Пауза", "0.5×", "1×", "2×", "4×" };
        static readonly float[] TimeScales = { 0f, 0.5f, 1f, 2f, 4f };
        static readonly int[] Counts = { 1, 5, 10, 25 };
        static readonly string[] CountLabels = { "×1", "×5", "×10", "×25" };

        static readonly UnitCategory?[] Tabs =
        {
            null, UnitCategory.Tank, UnitCategory.InfantryFightingVehicle, UnitCategory.Infantry,
            UnitCategory.Recon, UnitCategory.AntiTank, UnitCategory.AntiAir, UnitCategory.Artillery
        };

        Camera mainCamera;
        Transform unitsRoot;
        LineRenderer placementRing;
        UnitDefinition placing;
        Team spawnTeam = Team.Blue;
        int countIndex;
        int tabIndex;
        int scrollIndex;
        int timeIndex = 2;
        bool toolsCollapsed;

        public static SandboxController Instance { get; private set; }

        public bool IsPlacing => placing != null;

        void Awake()
        {
            Instance = this;
            var go = new GameObject("PlacementPreview");
            go.transform.SetParent(transform, false);
            placementRing = go.AddComponent<LineRenderer>();
            placementRing.loop = true;
            placementRing.positionCount = 40;
            placementRing.useWorldSpace = true;
            placementRing.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            placementRing.enabled = false;
        }

        void OnDestroy()
        {
            InputGate.SetCapture(this, false);
            if (Instance == this)
                Instance = null;
        }

        public void SetUnitsRoot(Transform root) => unitsRoot = root;

        // ------------------------------------------------------------------
        // Placement & hotkeys
        // ------------------------------------------------------------------

        void BeginPlacing(UnitDefinition definition)
        {
            placing = placing == definition ? null : definition;
            if (placing != null && CommandInput.Instance != null)
                CommandInput.Instance.CancelTargeting();
            InputGate.SetCapture(this, placing != null);
        }

        void CancelPlacing()
        {
            placing = null;
            placementRing.enabled = false;
            InputGate.SetCapture(this, false);
        }

        void Update()
        {
            var mouse = Mouse.current;
            var keyboard = Keyboard.current;
            if (mouse == null || keyboard == null || InputGate.MenuOpen)
            {
                placementRing.enabled = false;
                return;
            }
            if (mainCamera == null)
                mainCamera = Camera.main;

            if (keyboard.deleteKey.wasPressedThisFrame)
                DeleteSelected();

            if (placing == null)
            {
                placementRing.enabled = false;
                return;
            }

            if (keyboard.escapeKey.wasPressedThisFrame || mouse.rightButton.wasPressedThisFrame)
            {
                CancelPlacing();
                InputGate.Consume();
                return;
            }

            Vector2 pointer = mouse.position.ReadValue();
            bool hasPoint = RaycastGround(pointer, out var point);
            UpdatePreview(hasPoint, point);

            if (!hasPoint || !mouse.leftButton.wasPressedThisFrame || HudStyle.IsPointerOverHud(pointer))
                return;

            InputGate.Consume();
            Spawn(placing, spawnTeam, point, Counts[countIndex]);
            if (!keyboard.shiftKey.isPressed)
                CancelPlacing();
        }

        void UpdatePreview(bool visible, Vector3 center)
        {
            placementRing.enabled = visible;
            if (!visible)
                return;

            int count = Counts[countIndex];
            int rows = Mathf.CeilToInt(count / 8f);
            float radius = Mathf.Max(10f, Mathf.Max(Mathf.Min(count, 8) * 0.5f, rows) * SpawnSpacing);
            Color color = spawnTeam.ToColor();
            placementRing.sharedMaterial = GameMaterials.Unlit(color);
            placementRing.widthMultiplier = Mathf.Max(0.6f, Vector3.Distance(mainCamera.transform.position, center) * 0.003f);

            var map = GameMap.Instance;
            for (int i = 0; i < placementRing.positionCount; i++)
            {
                float angle = i * Mathf.PI * 2f / placementRing.positionCount;
                var p = center + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius;
                p.y = (map != null ? map.HeightAt(p.x, p.z) : center.y) + 1.5f;
                placementRing.SetPosition(i, p);
            }
        }

        bool RaycastGround(Vector2 pointer, out Vector3 point)
        {
            point = default;
            if (mainCamera == null)
                return false;
            Ray ray = mainCamera.ScreenPointToRay(pointer);
            if (!Physics.Raycast(ray, out var hit, 10000f, GameLayers.GroundMask | GameLayers.ObstaclesMask,
                    QueryTriggerInteraction.Ignore))
                return false;
            point = hit.point;
            return true;
        }

        // ------------------------------------------------------------------
        // Actions
        // ------------------------------------------------------------------

        public void Spawn(UnitDefinition definition, Team team, Vector3 center, int count)
        {
            if (unitsRoot == null)
                unitsRoot = new GameObject("Units").transform;

            // Face the enemy's side of the map.
            Vector3 facing = team == Team.Blue ? Vector3.forward : Vector3.back;
            var map = GameMap.Instance;
            foreach (var position in FormationPlanner.Grid(count, center, facing, SpawnSpacing))
            {
                var p = map != null ? map.OnGround(map.Clamp(position, 15f)) : position;
                UnitFactory.Spawn(definition, team, p, facing, unitsRoot);
            }
            Physics.SyncTransforms();
        }

        static void DeleteSelected()
        {
            if (SelectionManager.Instance == null)
                return;
            var selected = new List<Unit>(SelectionManager.Instance.Selected);
            foreach (var unit in selected)
                unit.Despawn();
        }

        static void HealSelected()
        {
            if (SelectionManager.Instance == null)
                return;
            foreach (var unit in SelectionManager.Instance.Selected)
                unit.Heal();
        }

        static void ClearUnits(Team? team)
        {
            var victims = new List<Unit>();
            foreach (var unit in Unit.All)
            {
                if (!team.HasValue || unit.Team == team.Value)
                    victims.Add(unit);
            }
            foreach (var unit in victims)
                unit.Despawn();
        }

        void SetTimeScale(int index)
        {
            timeIndex = index;
            Time.timeScale = TimeScales[index];
        }

        // ------------------------------------------------------------------
        // GUI
        // ------------------------------------------------------------------

        /// <summary>Draws the spawn bar and the tools panel (called inside BattleHud.OnGUI).</summary>
        public void DrawGui(Rect spawnArea)
        {
            DrawTools();
            DrawSpawnBar(spawnArea);
            DrawPlacementHint();
        }

        void DrawTools()
        {
            const float width = 270f;
            float x = HudStyle.Width - width - 10f;
            float y = 10f;
            float height = toolsCollapsed ? 40f : 372f;
            var rect = new Rect(x, y, width, height);
            HudStyle.DrawPanel(rect);

            HudStyle.Label(new Rect(x + 12f, y + 10f, 200f, 22f), "ПЕСОЧНИЦА", HudStyle.TitleStyle);
            if (HudStyle.Button(new Rect(rect.xMax - 36f, y + 7f, 26f, 26f), toolsCollapsed ? "+" : "–", false, true,
                    toolsCollapsed ? "Развернуть" : "Свернуть"))
                toolsCollapsed = !toolsCollapsed;
            if (toolsCollapsed)
                return;

            float inner = width - 24f;
            x += 12f;
            y += 42f;

            HudStyle.Label(new Rect(x, y, inner, 18f), "Скорость времени", HudStyle.SmallStyle);
            y += 18f;
            if (Mathf.Abs(Time.timeScale - TimeScales[timeIndex]) > 0.001f && (PauseMenu.Instance == null || !PauseMenu.Instance.IsOpen))
                timeIndex = ClosestTimeIndex(Time.timeScale);
            int newTime = HudStyle.Segmented(new Rect(x, y, inner, 28f), TimeLabels, timeIndex, 12);
            if (newTime != timeIndex)
                SetTimeScale(newTime);
            y += 38f;

            BattleRules.EnemyAIEnabled = HudStyle.Toggle(new Rect(x, y, inner, 24f), "ИИ красных наступает", BattleRules.EnemyAIEnabled,
                "Красные отправляют простаивающие юниты в атаку");
            y += 28f;
            if (VisibilitySystem.Instance != null)
            {
                bool fog = HudStyle.Toggle(new Rect(x, y, inner, 24f), "Туман войны (F)", VisibilitySystem.Instance.FogOfWar);
                if (fog != VisibilitySystem.Instance.FogOfWar)
                    VisibilitySystem.Instance.FogOfWar = fog;
            }
            y += 28f;
            bool both = HudStyle.Toggle(new Rect(x, y, inner, 24f), "Управлять обеими сторонами", BattleRules.ControlBothSides,
                "Можно выделять и отдавать приказы красным");
            if (both != BattleRules.ControlBothSides)
            {
                BattleRules.ControlBothSides = both;
                if (!both && SelectionManager.Instance != null)
                    SelectionManager.Instance.DropUncommandable();
            }
            y += 28f;
            BattleRules.Invulnerable = HudStyle.Toggle(new Rect(x, y, inner, 24f), "Бессмертие всех юнитов", BattleRules.Invulnerable);
            y += 36f;

            float half = (inner - 6f) * 0.5f;
            if (HudStyle.Button(new Rect(x, y, half, 30f), "Вылечить", false, true, "Полное здоровье выбранным"))
                HealSelected();
            if (HudStyle.Button(new Rect(x + half + 6f, y, half, 30f), "Удалить (Del)", false, true, "Удалить выбранных без обломков"))
                DeleteSelected();
            y += 38f;

            HudStyle.Label(new Rect(x, y, inner, 18f), "Очистить карту", HudStyle.SmallStyle);
            y += 18f;
            float third = (inner - 12f) / 3f;
            if (HudStyle.Button(new Rect(x, y, third, 28f), "Синих", false, true, null, 12))
                ClearUnits(Team.Blue);
            if (HudStyle.Button(new Rect(x + third + 6f, y, third, 28f), "Красных", false, true, null, 12))
                ClearUnits(Team.Red);
            if (HudStyle.Button(new Rect(x + (third + 6f) * 2f, y, third, 28f), "Всех", false, true, null, 12))
                ClearUnits(null);
            y += 36f;

            if (HudStyle.Button(new Rect(x, y, inner, 30f), "Расставить стартовые армии", false, true,
                    "По 19 юнитов с каждой стороны в зонах развёртывания"))
                BattleBootstrap.SpawnStartingArmies(unitsRoot);
        }

        static int ClosestTimeIndex(float scale)
        {
            int best = 0;
            for (int i = 1; i < TimeScales.Length; i++)
            {
                if (Mathf.Abs(TimeScales[i] - scale) < Mathf.Abs(TimeScales[best] - scale))
                    best = i;
            }
            return best;
        }

        void DrawSpawnBar(Rect area)
        {
            HudStyle.DrawPanel(area);
            float x = area.x + 10f;
            float y = area.y + 8f;
            float innerWidth = area.width - 20f;

            // Row 1: side, batch size, tabs.
            HudStyle.Label(new Rect(x, y + 5f, 60f, 18f), "Сторона", HudStyle.SmallStyle);
            if (HudStyle.Button(new Rect(x + 58f, y, 78f, 26f), "СИНИЕ", spawnTeam == Team.Blue, true, null, 12))
                spawnTeam = Team.Blue;
            if (HudStyle.Button(new Rect(x + 140f, y, 78f, 26f), "КРАСНЫЕ", spawnTeam == Team.Red, true, null, 12))
                spawnTeam = Team.Red;
            HudStyle.Fill(new Rect(x + 58f, y + 26f, 160f, 2f), spawnTeam.ToColor());

            HudStyle.Label(new Rect(x + 232f, y + 5f, 60f, 18f), "Кол-во", HudStyle.SmallStyle);
            countIndex = HudStyle.Segmented(new Rect(x + 282f, y, 168f, 26f), CountLabels, countIndex, 12);

            float tabsX = x + 466f;
            float tabsWidth = Mathf.Max(200f, area.xMax - 10f - tabsX);
            var tabLabels = new string[Tabs.Length];
            for (int i = 0; i < Tabs.Length; i++)
                tabLabels[i] = Tabs[i].HasValue ? UnitCatalog.CategoryName(Tabs[i].Value) : "Все";
            int newTab = HudStyle.Segmented(new Rect(tabsX, y, tabsWidth, 26f), tabLabels, tabIndex, 11);
            if (newTab != tabIndex)
            {
                tabIndex = newTab;
                scrollIndex = 0;
            }
            y += 36f;

            // Row 2: unit cards with paging.
            var units = new List<UnitDefinition>();
            foreach (var definition in UnitCatalog.All)
            {
                if (!Tabs[tabIndex].HasValue || definition.Category == Tabs[tabIndex].Value)
                    units.Add(definition);
            }

            int visible = Mathf.Max(1, Mathf.FloorToInt((innerWidth - 56f + CardGap) / (CardWidth + CardGap)));
            scrollIndex = Mathf.Clamp(scrollIndex, 0, Mathf.Max(0, units.Count - visible));
            bool paging = units.Count > visible;
            float cardsX = x + (paging ? 28f : 0f);

            if (paging)
            {
                if (HudStyle.Button(new Rect(x, y, 22f, CardHeight), "‹", false, scrollIndex > 0, null, 18))
                    scrollIndex--;
                if (HudStyle.Button(new Rect(area.xMax - 32f, y, 22f, CardHeight), "›", false, scrollIndex + visible < units.Count, null, 18))
                    scrollIndex++;
            }

            for (int i = 0; i < visible && scrollIndex + i < units.Count; i++)
            {
                var definition = units[scrollIndex + i];
                var card = new Rect(cardsX + i * (CardWidth + CardGap), y, CardWidth, CardHeight);
                if (DrawCard(card, definition))
                    BeginPlacing(definition);
            }
        }

        bool DrawCard(Rect rect, UnitDefinition definition)
        {
            HudStyle.Block(rect);
            var current = Event.current;
            bool hover = rect.Contains(current.mousePosition);
            bool active = placing == definition;
            if (hover)
                HudStyle.Tooltip = CardTooltip(definition);

            if (hover && current.type == EventType.MouseDown && current.button == 0)
            {
                current.Use();
                return true;
            }

            HudStyle.Fill(rect, hover ? HudStyle.ButtonHover : HudStyle.ButtonColor);
            HudStyle.Border(rect, active ? HudStyle.Amber : hover ? HudStyle.Amber : HudStyle.BorderColor, active ? 2f : 1f);

            Color team = spawnTeam.ToColor();
            var badge = new Rect(rect.x + (rect.width - 46f) * 0.5f, rect.y + 8f, 46f, 32f);
            HudStyle.Fill(badge, new Color(team.r * 0.3f, team.g * 0.3f, team.b * 0.3f, 1f));
            HudStyle.Border(badge, team);
            HudStyle.Icon(HudStyle.Inset(badge, 4f), HudIcons.Unit(definition.Category), Color.white);

            var nameStyle = new GUIStyle(HudStyle.TextStyle) { fontSize = 11, alignment = TextAnchor.UpperCenter };
            GUI.Label(new Rect(rect.x + 3f, rect.y + 44f, rect.width - 6f, 32f), definition.DisplayName, nameStyle);

            var statStyle = new GUIStyle(HudStyle.SmallStyle) { fontSize = 11, alignment = TextAnchor.MiddleCenter };
            GUI.Label(new Rect(rect.x, rect.yMax - 24f, rect.width, 18f),
                $"<color={HudStyle.AmberHex}>{definition.Cost}</color> · {definition.MaxWeaponRange:0} м",
                new GUIStyle(statStyle) { richText = true });
            return false;
        }

        static string CardTooltip(UnitDefinition definition)
        {
            var weapons = new List<string>();
            foreach (var weapon in definition.Weapons)
                weapons.Add($"{weapon.DisplayName} {weapon.Range:0} м");
            return $"{definition.DisplayName}: броня {definition.ArmorFront}/{definition.ArmorSide}/{definition.ArmorRear}, " +
                   $"{definition.SpeedKmh:0} км/ч, обзор {definition.OpticsRange:0} м — {string.Join(", ", weapons)}";
        }

        void DrawPlacementHint()
        {
            if (placing == null)
                return;
            Vector2 mouse = Event.current.mousePosition;
            var rect = new Rect(mouse.x + 18f, mouse.y + 14f, 270f, 38f);
            HudStyle.Fill(rect, HudStyle.PanelSolid);
            HudStyle.Border(rect, spawnTeam.ToColor());
            HudStyle.Label(new Rect(rect.x + 8f, rect.y + 3f, rect.width - 16f, 18f),
                $"<color={HudStyle.AmberHex}>{placing.DisplayName} ×{Counts[countIndex]}</color> — {spawnTeam.DisplayName()}");
            HudStyle.Label(new Rect(rect.x + 8f, rect.y + 20f, rect.width - 16f, 16f),
                "ЛКМ — поставить, Shift — ещё, ПКМ/Esc — отмена", HudStyle.SmallStyle);
        }
    }
}
