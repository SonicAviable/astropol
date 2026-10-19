using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using WargameRTS.Combat;
using WargameRTS.Core;
using WargameRTS.Sandbox;
using WargameRTS.Selection;
using WargameRTS.Units;

namespace WargameRTS.UI
{
    /// <summary>
    /// Battle interface (IMGUI, layout after docs/ui-reference/hud-mockup.jpg).
    /// The single OnGUI of the battle: unit labels, force bar, minimap, order panel,
    /// selected-unit panel, sandbox panels, pause menu and tooltips, in that z-order.
    /// </summary>
    public sealed class BattleHud : MonoBehaviour
    {
        const float LabelWidth = 30f;
        const float LabelHeight = 20f;
        const float ButtonSize = 52f;
        const float ButtonGap = 6f;
        const float Padding = 8f;
        const float RightPanelWidth = ButtonSize * 4f + ButtonGap * 3f + Padding * 2f;
        const float MinimapSize = 240f;
        const float Margin = 10f;

        struct OrderButton
        {
            public string Icon;
            public string Tooltip;
            public Func<bool> IsActive;
            public Action OnClick;
            public bool Enabled;
        }

        Camera mainCamera;
        OrderButton[] orderButtons;
        bool showHelp;
        float battleTime;
        int initialBlue;
        int initialRed;
        string result;

        void Awake() => orderButtons = CreateOrderButtons();

        static OrderButton[] CreateOrderButtons()
        {
            Func<PendingOrder, bool> pending = order => CommandInput.Instance != null && CommandInput.Instance.Pending == order;
            Func<FireMode, bool> fireMode = mode => CommandInput.SelectedFireMode() == mode;
            Action<PendingOrder> arm = order =>
            {
                var input = CommandInput.Instance;
                if (input == null)
                    return;
                if (input.Pending == order)
                    input.CancelTargeting();
                else
                    input.BeginTargeting(order);
            };
            Action<FireMode> toggleFire = mode =>
            {
                var input = CommandInput.Instance;
                if (input != null)
                    input.SetFireModeSelected(CommandInput.SelectedFireMode() == mode ? FireMode.FireAtWill : mode);
            };

            return new[]
            {
                new OrderButton { Icon = "Move", Tooltip = "Движение (ПКМ)", Enabled = true,
                    IsActive = () => pending(PendingOrder.Move), OnClick = () => arm(PendingOrder.Move) },
                new OrderButton { Icon = "FastMove", Tooltip = "Быстрый марш — быстрее, без стрельбы", Enabled = true,
                    IsActive = () => pending(PendingOrder.FastMove), OnClick = () => arm(PendingOrder.FastMove) },
                new OrderButton { Icon = "Attack", Tooltip = "Атаковать цель (ПКМ по врагу)", Enabled = true,
                    IsActive = () => pending(PendingOrder.Attack), OnClick = () => arm(PendingOrder.Attack) },
                new OrderButton { Icon = "AttackMove", Tooltip = "Движение с атакой (Ctrl+ПКМ)", Enabled = true,
                    IsActive = () => pending(PendingOrder.AttackMove), OnClick = () => arm(PendingOrder.AttackMove) },

                new OrderButton { Icon = "Stop", Tooltip = "Стоп (X)", Enabled = true,
                    IsActive = () => false, OnClick = () => CommandInput.Instance?.StopSelected() },
                new OrderButton { Icon = "HoldFire", Tooltip = "Не стрелять (H — смена режима огня)", Enabled = true,
                    IsActive = () => fireMode(FireMode.HoldFire), OnClick = () => toggleFire(FireMode.HoldFire) },
                new OrderButton { Icon = "ReturnFire", Tooltip = "Только ответный огонь (H)", Enabled = true,
                    IsActive = () => fireMode(FireMode.ReturnFire), OnClick = () => toggleFire(FireMode.ReturnFire) },
                new OrderButton { Icon = "Smoke", Tooltip = "Дымовая завеса — скоро", Enabled = false },

                new OrderButton { Icon = "Unload", Tooltip = "Высадить пехоту — скоро", Enabled = false },
                new OrderButton { Icon = "Load", Tooltip = "Посадить пехоту — скоро", Enabled = false },
                new OrderButton { Icon = "Retreat", Tooltip = "Отступить к своему краю карты (B)", Enabled = true,
                    IsActive = () => false, OnClick = () => CommandInput.Instance?.RetreatSelected() },
                new OrderButton { Icon = "Rally", Tooltip = "Точка сбора — скоро", Enabled = false }
            };
        }

        void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard != null && !InputGate.MenuOpen)
            {
                if (keyboard.f1Key.wasPressedThisFrame)
                    showHelp = !showHelp;
                if (result != null && keyboard.rKey.wasPressedThisFrame)
                    PauseMenu.Restart();
            }

            int blue = CountAlive(Team.Blue);
            int red = CountAlive(Team.Red);
            initialBlue = Mathf.Max(initialBlue, blue);
            initialRed = Mathf.Max(initialRed, red);

            if (result != null)
                return;
            battleTime += Time.deltaTime;

            // Victory/defeat only in skirmish; the sandbox never ends.
            if (!BattleRules.IsSandbox && battleTime > 2f && (blue == 0 || red == 0))
                result = red == 0 ? "ПОБЕДА" : "ПОРАЖЕНИЕ";
        }

        static int CountAlive(Team team)
        {
            int count = 0;
            foreach (var unit in Unit.All)
            {
                if (unit.Team == team)
                    count++;
            }
            return count;
        }

        // ------------------------------------------------------------------
        // Drawing
        // ------------------------------------------------------------------

        void OnGUI()
        {
            HudStyle.Begin();
            if (mainCamera == null)
                mainCamera = Camera.main;

            if (HudStyle.IsRepaint && mainCamera != null)
                DrawUnitLabels();

            DrawTopBar();
            DrawHelp();
            MinimapView.Draw(new Rect(Margin, HudStyle.Height - MinimapSize - Margin, MinimapSize, MinimapSize), mainCamera);

            if (BattleRules.IsSandbox && SandboxController.Instance != null)
            {
                float left = Margin + MinimapSize + Margin;
                float right = HudStyle.Width - RightPanelWidth - Margin * 2f;
                const float barHeight = 158f;
                SandboxController.Instance.DrawGui(new Rect(left, HudStyle.Height - barHeight - Margin, right - left, barHeight));
            }

            DrawRightPanels();
            DrawTargetingHint();
            if (result != null)
                DrawResult();

            if (PauseMenu.Instance != null)
                PauseMenu.Instance.DrawGui();
            HudStyle.DrawTooltip();
        }

        void DrawUnitLabels()
        {
            bool fog = VisibilitySystem.Instance != null && VisibilitySystem.Instance.FogOfWar;
            foreach (var unit in Unit.All)
            {
                if (fog && !unit.IsVisibleTo(TeamExtensions.Player))
                    continue;

                Vector3 screen = mainCamera.WorldToScreenPoint(unit.Position + Vector3.up * 6f);
                if (screen.z <= 0f)
                    continue;

                Vector2 gui = HudStyle.WorldScreenToGui(screen);
                var rect = new Rect(gui.x - LabelWidth * 0.5f, gui.y - LabelHeight, LabelWidth, LabelHeight);
                Color teamColor = unit.Team.ToColor();

                HudStyle.Fill(rect, new Color(teamColor.r * 0.3f, teamColor.g * 0.3f, teamColor.b * 0.3f, 0.92f));
                HudStyle.Border(rect, unit.IsSelected ? HudStyle.Amber : teamColor, unit.IsSelected ? 2f : 1f);
                HudStyle.Icon(HudStyle.Inset(rect, 3f), HudIcons.Unit(unit.Definition.Category), Color.white);
                HudStyle.HealthBar(new Rect(rect.x, rect.yMax + 2f, rect.width, 3f), unit.Health / unit.Definition.MaxHealth);
            }
        }

        void DrawTopBar()
        {
            var rect = new Rect(HudStyle.Width * 0.5f - 240f, Margin, 480f, 44f);
            HudStyle.DrawPanel(rect);

            int blue = CountAlive(Team.Blue);
            int red = CountAlive(Team.Red);
            var blueRect = new Rect(rect.x + 12f, rect.y + 7f, 170f, 20f);
            var redRect = new Rect(rect.xMax - 12f - 170f, rect.y + 7f, 170f, 20f);
            HudStyle.Label(blueRect, $"<color=#6fa0ff>СИНИЕ</color>  {blue}", HudStyle.TitleStyle);
            HudStyle.Label(redRect, $"{red}  <color=#ff6a5a>КРАСНЫЕ</color>", new GUIStyle(HudStyle.TitleStyle) { alignment = TextAnchor.UpperRight });
            ForceBar(new Rect(blueRect.x, rect.y + 31f, blueRect.width, 4f), blue, initialBlue, Team.Blue.ToColor(), false);
            ForceBar(new Rect(redRect.x, rect.y + 31f, redRect.width, 4f), red, initialRed, Team.Red.ToColor(), true);

            int minutes = Mathf.FloorToInt(battleTime / 60f);
            int seconds = Mathf.FloorToInt(battleTime % 60f);
            HudStyle.Icon(new Rect(rect.center.x - 44f, rect.y + 8f, 18f, 18f), HudIcons.Status("Timer"), Color.white);
            HudStyle.Label(new Rect(rect.center.x - 22f, rect.y + 6f, 70f, 22f), $"{minutes:00}:{seconds:00}", HudStyle.TitleStyle);

            string mode = BattleRules.IsSandbox ? "ПЕСОЧНИЦА" : "БОЙ С ИИ";
            if (VisibilitySystem.Instance != null && !VisibilitySystem.Instance.FogOfWar)
                mode += $"  <color={HudStyle.AmberHex}>туман выкл.</color>";
            if (Time.timeScale != 1f && (PauseMenu.Instance == null || !PauseMenu.Instance.IsOpen))
                mode += $"  <color={HudStyle.AmberHex}>{(Time.timeScale == 0f ? "пауза" : Time.timeScale.ToString("0.#") + "×")}</color>";
            HudStyle.Label(new Rect(rect.x, rect.y + 26f, rect.width, 16f), mode,
                new GUIStyle(HudStyle.SmallStyle) { alignment = TextAnchor.UpperCenter, richText = true, fontSize = 11 });
        }

        static void ForceBar(Rect rect, int current, int initial, Color color, bool alignRight)
        {
            HudStyle.Fill(rect, new Color(0f, 0f, 0f, 0.6f));
            float fraction = initial > 0 ? current / (float)initial : 0f;
            float width = rect.width * fraction;
            HudStyle.Fill(new Rect(alignRight ? rect.xMax - width : rect.x, rect.y, width, rect.height), color);
        }

        void DrawHelp()
        {
            if (!showHelp)
            {
                if (HudStyle.Button(new Rect(Margin, Margin, 150f, 28f), "Управление  (F1)", false, true, null, 12))
                    showHelp = true;
                return;
            }

            var rect = new Rect(Margin, Margin, 380f, 380f);
            HudStyle.DrawPanel(rect);
            HudStyle.Label(new Rect(rect.x + 14f, rect.y + 12f, rect.width - 28f, 22f),
                $"Управление  <color={HudStyle.MutedHex}>(F1 — скрыть)</color>", HudStyle.TitleStyle);
            string a = HudStyle.AmberHex;
            HudStyle.Label(new Rect(rect.x + 14f, rect.y + 40f, rect.width - 28f, rect.height - 48f),
                $"<color={a}>Камера</color>: WASD / стрелки / край экрана, колесо — зум, Q/E или средняя кнопка — поворот, Shift — быстрее. Клик по миникарте — переместить камеру\n" +
                $"<color={a}>Выделение</color>: ЛКМ, рамка ЛКМ, Shift — добавить, двойной клик — все такого типа, Esc — снять\n" +
                $"<color={a}>Группы</color>: Ctrl+1..9 — назначить, 1..9 — выбрать (дважды — камера)\n" +
                $"<color={a}>Приказы</color>: ПКМ — строем, ПКМ с протяжкой — линией, ПКМ по врагу — атака, " +
                "Ctrl+ПКМ — с атакой, ПКМ по миникарте — туда. X — стоп, B — отход, H — режим огня\n" +
                $"<color={a}>Песочница</color>: карточка внизу → ЛКМ по карте (Shift — ещё), Del — удалить выбранных\n" +
                $"<color={a}>F</color> — туман войны,  <color={a}>Esc / F10</color> — меню паузы");
        }

        // ------------------------------------------------------------------
        // Bottom-right: order buttons + selected units
        // ------------------------------------------------------------------

        void DrawRightPanels()
        {
            var selection = SelectionManager.Instance != null ? SelectionManager.Instance.Selected : null;
            if (selection == null || selection.Count == 0)
                return;

            float infoHeight = InfoPanelHeight(selection);
            var infoRect = new Rect(HudStyle.Width - RightPanelWidth - Margin, HudStyle.Height - infoHeight - Margin, RightPanelWidth, infoHeight);
            float ordersHeight = ButtonSize * 3f + ButtonGap * 2f + Padding * 2f;
            var ordersRect = new Rect(infoRect.x, infoRect.y - ordersHeight - 6f, RightPanelWidth, ordersHeight);

            DrawOrders(ordersRect);
            DrawInfo(infoRect, selection);
        }

        void DrawOrders(Rect panel)
        {
            HudStyle.DrawPanel(panel);
            for (int i = 0; i < orderButtons.Length; i++)
            {
                int row = i / 4;
                int column = i % 4;
                var rect = new Rect(panel.x + Padding + column * (ButtonSize + ButtonGap),
                    panel.y + Padding + row * (ButtonSize + ButtonGap), ButtonSize, ButtonSize);
                DrawOrderButton(rect, orderButtons[i]);
            }
        }

        static void DrawOrderButton(Rect rect, OrderButton button)
        {
            var current = Event.current;
            bool hover = rect.Contains(current.mousePosition);
            bool active = button.Enabled && button.IsActive != null && button.IsActive();
            if (hover)
                HudStyle.Tooltip = button.Tooltip;

            if (button.Enabled && hover && current.type == EventType.MouseDown && current.button == 0)
            {
                button.OnClick?.Invoke();
                current.Use();
                return;
            }

            HudStyle.Fill(rect, active ? HudStyle.Amber : hover && button.Enabled ? HudStyle.ButtonHover : HudStyle.ButtonColor);
            HudStyle.Border(rect, active || (hover && button.Enabled) ? HudStyle.Amber : HudStyle.BorderColor);
            Color iconColor = !button.Enabled ? new Color(1f, 1f, 1f, 0.22f)
                : active ? HudStyle.Dark
                : hover ? HudStyle.Amber
                : HudStyle.Text;
            HudStyle.Icon(HudStyle.Inset(rect, 10f), HudIcons.Order(button.Icon), iconColor);
        }

        static float InfoPanelHeight(IReadOnlyList<Unit> selection)
        {
            var first = selection[0].Definition;
            bool sameType = true;
            foreach (var unit in selection)
                sameType &= unit.Definition == first;
            return sameType ? 112f + first.Weapons.Count * 19f : 136f;
        }

        static void DrawInfo(Rect panel, IReadOnlyList<Unit> selection)
        {
            HudStyle.DrawPanel(panel);
            var first = selection[0].Definition;

            var counts = new Dictionary<UnitDefinition, int>();
            bool mixedTeams = false;
            float health = 0f;
            float maxHealth = 0f;
            foreach (var unit in selection)
            {
                counts.TryGetValue(unit.Definition, out int count);
                counts[unit.Definition] = count + 1;
                health += unit.Health;
                maxHealth += unit.Definition.MaxHealth;
                mixedTeams |= unit.Team != selection[0].Team;
            }
            Team badgeTeam = selection[0].Team;

            float x = panel.x + Padding;
            float y = panel.y + Padding;
            float width = panel.width - Padding * 2f;

            if (counts.Count == 1)
            {
                UnitBadge(new Rect(x, y, 36f, 26f), first.Category, badgeTeam);
                string title = selection.Count > 1 ? $"{first.DisplayName}  ×{selection.Count}" : first.DisplayName;
                HudStyle.Label(new Rect(x + 44f, y + 4f, width - 44f, 20f), title,
                    new GUIStyle(HudStyle.TitleStyle) { fontSize = 14, clipping = TextClipping.Clip });
                y += 32f;

                HudStyle.HealthBar(new Rect(x, y, width, 6f), health / maxHealth);
                HudStyle.Label(new Rect(x, y + 8f, width, 16f), $"{health:0.#} / {maxHealth:0}", HudStyle.SmallStyle);
                y += 26f;

                HudStyle.Icon(new Rect(x, y + 1f, 16f, 16f), HudIcons.Status("Armor"), Color.white);
                HudStyle.Label(new Rect(x + 20f, y, width - 20f, 18f),
                    $"Л <b>{first.ArmorFront}</b>  Б <b>{first.ArmorSide}</b>  К <b>{first.ArmorRear}</b>   " +
                    $"<color={HudStyle.MutedHex}>{first.SpeedKmh:0} км/ч</color>");
                y += 21f;

                foreach (var weapon in first.Weapons)
                {
                    HudStyle.Icon(new Rect(x + 1f, y + 2f, 14f, 14f), HudIcons.Status("Ammo"), Color.white);
                    HudStyle.Label(new Rect(x + 20f, y, width * 0.68f - 20f, 18f), weapon.DisplayName);
                    HudStyle.Label(new Rect(x + width * 0.68f, y, width * 0.32f, 18f), $"{weapon.Range:0} м",
                        new GUIStyle(HudStyle.SmallStyle) { alignment = TextAnchor.UpperRight });
                    y += 19f;
                }
            }
            else
            {
                HudStyle.Label(new Rect(x, y + 2f, width, 20f), mixedTeams ? $"Обе стороны  ×{selection.Count}" : $"Группа  ×{selection.Count}",
                    HudStyle.TitleStyle);
                y += 26f;

                float chipX = x;
                foreach (var pair in counts)
                {
                    if (chipX + 52f > panel.xMax - Padding)
                        break;
                    UnitBadge(new Rect(chipX, y, 30f, 22f), pair.Key.Category, badgeTeam);
                    HudStyle.Label(new Rect(chipX + 32f, y + 3f, 22f, 18f), pair.Value.ToString(), HudStyle.TitleStyle);
                    chipX += 56f;
                }
                y += 30f;

                HudStyle.HealthBar(new Rect(x, y, width, 6f), health / maxHealth);
                HudStyle.Label(new Rect(x, y + 8f, width, 16f), $"{health:0.#} / {maxHealth:0}", HudStyle.SmallStyle);
            }

            var fireMode = CommandInput.SelectedFireMode();
            string fire = fireMode == FireMode.FireAtWill ? "по готовности"
                : fireMode == FireMode.ReturnFire ? $"<color={HudStyle.AmberHex}>только ответный</color>"
                : fireMode == FireMode.HoldFire ? "<color=#FF6A5A>запрещён</color>"
                : "разный";
            HudStyle.Label(new Rect(x, panel.yMax - Padding - 18f, width, 18f), $"Огонь: {fire}");
        }

        static void UnitBadge(Rect rect, UnitCategory category, Team team)
        {
            Color color = team.ToColor();
            HudStyle.Fill(rect, new Color(color.r * 0.3f, color.g * 0.3f, color.b * 0.3f, 1f));
            HudStyle.Border(rect, color);
            HudStyle.Icon(HudStyle.Inset(rect, 3f), HudIcons.Unit(category), Color.white);
        }

        static void DrawTargetingHint()
        {
            var input = CommandInput.Instance;
            if (input == null || !input.IsTargeting)
                return;

            string text = input.Pending == PendingOrder.Move ? "Движение"
                : input.Pending == PendingOrder.FastMove ? "Быстрый марш"
                : input.Pending == PendingOrder.Attack ? "Атака: укажи врага или точку"
                : "Движение с атакой";
            Vector2 mouse = Event.current.mousePosition;
            var rect = new Rect(mouse.x + 18f, mouse.y + 14f, 240f, 38f);
            HudStyle.Fill(rect, HudStyle.PanelSolid);
            HudStyle.Border(rect, HudStyle.Amber);
            HudStyle.Label(new Rect(rect.x + 8f, rect.y + 3f, rect.width - 16f, 18f), $"<color={HudStyle.AmberHex}>{text}</color>");
            HudStyle.Label(new Rect(rect.x + 8f, rect.y + 20f, rect.width - 16f, 16f), "ЛКМ — выполнить, ПКМ/Esc — отмена", HudStyle.SmallStyle);
        }

        void DrawResult()
        {
            var rect = new Rect(HudStyle.Width * 0.5f - 240f, HudStyle.Height * 0.5f - 90f, 480f, 180f);
            HudStyle.DrawPanel(rect, true);
            HudStyle.Border(rect, HudStyle.Amber, 2f);
            HudStyle.Label(new Rect(rect.x, rect.y + 14f, rect.width, 70f), result, HudStyle.BigStyle);
            if (HudStyle.Button(new Rect(rect.x + 40f, rect.yMax - 64f, 180f, 40f), "ЗАНОВО (R)"))
                PauseMenu.Restart();
            if (HudStyle.Button(new Rect(rect.xMax - 220f, rect.yMax - 64f, 180f, 40f), "ГЛАВНОЕ МЕНЮ"))
                PauseMenu.LoadMainMenu();
        }
    }
}
