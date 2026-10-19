using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using WargameRTS.Combat;
using WargameRTS.Core;
using WargameRTS.Selection;
using WargameRTS.Units;

namespace WargameRTS.UI
{
    /// <summary>
    /// Temporary IMGUI interface following docs/ui-reference/hud-mockup.jpg:
    /// NATO-style unit labels, force bar, order panel, selected-unit panel, help
    /// and victory/defeat screen. Will be replaced by UI Toolkit.
    /// </summary>
    public sealed class SandboxHud : MonoBehaviour
    {
        // Palette (see docs: Gemini style block).
        static readonly Color PanelColor = new Color32(0x14, 0x18, 0x1D, 0xD9);
        static readonly Color BorderColor = new Color32(0x3A, 0x44, 0x50, 0xFF);
        static readonly Color ButtonColor = new Color32(0x1B, 0x20, 0x27, 0xF2);
        static readonly Color Amber = new Color32(0xF2, 0xB3, 0x3D, 0xFF);
        static readonly Color TextColor = new Color32(0xE6, 0xE6, 0xE0, 0xFF);
        static readonly Color MutedText = new Color32(0x9A, 0xA3, 0xAD, 0xFF);

        const float LabelWidth = 28f;
        const float LabelHeight = 19f;
        const float ButtonSize = 52f;
        const float ButtonGap = 6f;
        const float Padding = 8f;
        const float RightPanelWidth = ButtonSize * 4f + ButtonGap * 3f + Padding * 2f;

        static readonly List<Rect> blockingRects = new List<Rect>();

        struct OrderButton
        {
            public string Icon;
            public string Tooltip;
            public Func<bool> IsActive;
            public Action OnClick;
            public bool Enabled;
        }

        Camera mainCamera;
        Texture2D white;
        GUIStyle textStyle;
        GUIStyle smallStyle;
        GUIStyle titleStyle;
        GUIStyle centerStyle;
        GUIStyle bigStyle;
        OrderButton[] orderButtons;
        bool showHelp = true;
        float battleTime;
        int initialBlue;
        int initialRed;
        string result;
        string hoveredTooltip;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => blockingRects.Clear();

        /// <summary>True if the screen point (Input System coordinates, Y up) is over a HUD panel.</summary>
        public static bool IsPointerOverHud(Vector2 screenPoint)
        {
            var guiPoint = new Vector2(screenPoint.x, Screen.height - screenPoint.y);
            foreach (var rect in blockingRects)
            {
                if (rect.Contains(guiPoint))
                    return true;
            }
            return false;
        }

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
                new OrderButton { Icon = "ReturnFire", Tooltip = "Только ответный огонь (H — смена режима огня)", Enabled = true,
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
            if (keyboard != null)
            {
                if (keyboard.f1Key.wasPressedThisFrame)
                    showHelp = !showHelp;
                if (result != null && keyboard.rKey.wasPressedThisFrame)
                    SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
            }

            if (result != null)
                return;

            battleTime += Time.deltaTime;
            int blue = CountAlive(Team.Blue);
            int red = CountAlive(Team.Red);
            initialBlue = Mathf.Max(initialBlue, blue);
            initialRed = Mathf.Max(initialRed, red);
            if (battleTime > 2f && (blue == 0 || red == 0))
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
            EnsureStyles();
            if (mainCamera == null)
                mainCamera = Camera.main;

            if (Event.current.type == EventType.Layout)
                blockingRects.Clear();
            hoveredTooltip = null;

            if (Event.current.type == EventType.Repaint && mainCamera != null)
                DrawUnitLabels();

            DrawTopBar();
            if (showHelp)
                DrawHelp();
            DrawRightPanels();
            DrawTargetingHint();
            DrawTooltip();
            if (result != null)
                DrawResult();
        }

        void EnsureStyles()
        {
            if (textStyle != null)
                return;

            white = Texture2D.whiteTexture;
            textStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                richText = true,
                wordWrap = true,
                padding = new RectOffset(0, 0, 0, 0),
                normal = { textColor = TextColor }
            };
            smallStyle = new GUIStyle(textStyle) { fontSize = 11, wordWrap = false, normal = { textColor = MutedText } };
            titleStyle = new GUIStyle(textStyle) { fontSize = 14, fontStyle = FontStyle.Bold, wordWrap = false };
            centerStyle = new GUIStyle(titleStyle) { alignment = TextAnchor.MiddleCenter };
            bigStyle = new GUIStyle(centerStyle) { fontSize = 46 };
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

                var rect = new Rect(screen.x - LabelWidth * 0.5f, Screen.height - screen.y - LabelHeight, LabelWidth, LabelHeight);
                Color teamColor = unit.Team.ToColor();

                Fill(rect, new Color(teamColor.r * 0.3f, teamColor.g * 0.3f, teamColor.b * 0.3f, 0.92f));
                Border(rect, unit.IsSelected ? Amber : teamColor, unit.IsSelected ? 2f : 1f);
                DrawIcon(Inset(rect, 3f), HudIcons.Unit(unit.Definition.Category), Color.white);

                float health01 = unit.Health / unit.Definition.MaxHealth;
                var bar = new Rect(rect.x, rect.yMax + 2f, rect.width, 3f);
                Fill(bar, new Color(0f, 0f, 0f, 0.8f));
                Fill(new Rect(bar.x, bar.y, bar.width * health01, bar.height),
                    Color.Lerp(new Color(1f, 0.25f, 0.15f), new Color(0.35f, 0.95f, 0.35f), health01));
            }
        }

        void DrawTopBar()
        {
            var rect = new Rect(Screen.width * 0.5f - 230f, 8f, 460f, 40f);
            Panel(rect);

            int blue = CountAlive(Team.Blue);
            int red = CountAlive(Team.Red);
            var blueRect = new Rect(rect.x + Padding, rect.y + 6f, 170f, 20f);
            var redRect = new Rect(rect.xMax - Padding - 170f, rect.y + 6f, 170f, 20f);
            GUI.Label(blueRect, $"<color=#6fa0ff>СИНИЕ</color>  {blue}", titleStyle);
            GUI.Label(redRect, $"{red}  <color=#ff6a5a>КРАСНЫЕ</color>", new GUIStyle(titleStyle) { alignment = TextAnchor.UpperRight });
            ForceBar(new Rect(blueRect.x, rect.y + 28f, blueRect.width, 4f), blue, initialBlue, Team.Blue.ToColor(), false);
            ForceBar(new Rect(redRect.x, rect.y + 28f, redRect.width, 4f), red, initialRed, Team.Red.ToColor(), true);

            int minutes = Mathf.FloorToInt(battleTime / 60f);
            int seconds = Mathf.FloorToInt(battleTime % 60f);
            GUI.Label(new Rect(rect.center.x - 40f, rect.y + 4f, 80f, 22f), $"{minutes:00}:{seconds:00}", centerStyle);
            if (VisibilitySystem.Instance != null && !VisibilitySystem.Instance.FogOfWar)
                GUI.Label(new Rect(rect.center.x - 50f, rect.y + 22f, 100f, 16f), "<color=#F2B33D>туман выкл.</color>",
                    new GUIStyle(smallStyle) { alignment = TextAnchor.UpperCenter, richText = true });
        }

        void ForceBar(Rect rect, int current, int initial, Color color, bool alignRight)
        {
            if (Event.current.type != EventType.Repaint)
                return;
            Fill(rect, new Color(0f, 0f, 0f, 0.6f));
            float fraction = initial > 0 ? current / (float)initial : 0f;
            float width = rect.width * fraction;
            Fill(new Rect(alignRight ? rect.xMax - width : rect.x, rect.y, width, rect.height), color);
        }

        void DrawHelp()
        {
            var rect = new Rect(10f, 10f, 340f, 318f);
            Panel(rect);
            GUI.Label(new Rect(rect.x + 12f, rect.y + 10f, rect.width - 24f, 22f), "Управление  <color=#9AA3AD>(F1 — скрыть)</color>", titleStyle);
            GUI.Label(new Rect(rect.x + 12f, rect.y + 36f, rect.width - 24f, rect.height - 44f),
                "<color=#F2B33D>Камера</color>: WASD / стрелки / край экрана, колесо — зум, Q/E или средняя кнопка — поворот, Shift — быстрее\n" +
                "<color=#F2B33D>Выделение</color>: ЛКМ, рамка ЛКМ, Shift — добавить, двойной клик — все такого типа, Esc — снять\n" +
                "<color=#F2B33D>Группы</color>: Ctrl+1..9 — назначить, 1..9 — выбрать (дважды — камера)\n" +
                "<color=#F2B33D>Приказы</color>: ПКМ — строем, ПКМ с протяжкой — линией, ПКМ по врагу — атака, " +
                "Ctrl+ПКМ — движение с атакой, X — стоп, B — отход, H — режим огня. " +
                "Кнопки справа внизу: выбери приказ, затем ЛКМ по карте (Shift — несколько раз)\n" +
                "<color=#F2B33D>F</color> — туман войны вкл/выкл",
                textStyle);
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
            var infoRect = new Rect(Screen.width - RightPanelWidth - 10f, Screen.height - infoHeight - 10f, RightPanelWidth, infoHeight);
            float ordersHeight = ButtonSize * 3f + ButtonGap * 2f + Padding * 2f;
            var ordersRect = new Rect(infoRect.x, infoRect.y - ordersHeight - 6f, RightPanelWidth, ordersHeight);

            DrawOrders(ordersRect);
            DrawInfo(infoRect, selection);
        }

        void DrawOrders(Rect panel)
        {
            Panel(panel);
            for (int i = 0; i < orderButtons.Length; i++)
            {
                int row = i / 4;
                int column = i % 4;
                var rect = new Rect(panel.x + Padding + column * (ButtonSize + ButtonGap),
                    panel.y + Padding + row * (ButtonSize + ButtonGap), ButtonSize, ButtonSize);
                DrawOrderButton(rect, orderButtons[i]);
            }
        }

        void DrawOrderButton(Rect rect, OrderButton button)
        {
            var current = Event.current;
            bool hover = rect.Contains(current.mousePosition);
            bool active = button.Enabled && button.IsActive != null && button.IsActive();

            if (hover)
                hoveredTooltip = button.Tooltip;

            if (button.Enabled && hover && current.type == EventType.MouseDown && current.button == 0)
            {
                button.OnClick?.Invoke();
                current.Use();
                return;
            }

            if (current.type != EventType.Repaint)
                return;

            Fill(rect, active ? Amber : ButtonColor);
            Border(rect, active || (hover && button.Enabled) ? Amber : BorderColor, 1f);
            Color iconColor = !button.Enabled ? new Color(1f, 1f, 1f, 0.22f)
                : active ? new Color32(0x14, 0x18, 0x1D, 0xFF)
                : hover ? Amber
                : TextColor;
            DrawIcon(Inset(rect, 10f), HudIcons.Order(button.Icon), iconColor);
        }

        static float InfoPanelHeight(IReadOnlyList<Unit> selection)
        {
            var first = selection[0].Definition;
            bool sameType = true;
            foreach (var unit in selection)
                sameType &= unit.Definition == first;
            return sameType ? 104f + first.Weapons.Count * 18f : 132f;
        }

        void DrawInfo(Rect panel, IReadOnlyList<Unit> selection)
        {
            Panel(panel);
            var first = selection[0].Definition;

            // Group by unit type.
            var counts = new Dictionary<UnitDefinition, int>();
            float health = 0f;
            float maxHealth = 0f;
            foreach (var unit in selection)
            {
                counts.TryGetValue(unit.Definition, out int count);
                counts[unit.Definition] = count + 1;
                health += unit.Health;
                maxHealth += unit.Definition.MaxHealth;
            }

            float x = panel.x + Padding;
            float y = panel.y + Padding;
            float width = panel.width - Padding * 2f;

            if (counts.Count == 1)
            {
                var badge = new Rect(x, y, 34f, 24f);
                UnitBadge(badge, first.Category);
                string title = selection.Count > 1 ? $"{first.DisplayName}  ×{selection.Count}" : first.DisplayName;
                GUI.Label(new Rect(x + 42f, y + 3f, width - 42f, 20f), title, titleStyle);
                y += 30f;

                HealthBar(new Rect(x, y, width, 6f), health / maxHealth);
                GUI.Label(new Rect(x, y + 8f, width, 16f), $"{health:0.#} / {maxHealth:0}", smallStyle);
                y += 26f;

                GUI.Label(new Rect(x, y, width, 18f),
                    $"Броня  Л <b>{first.ArmorFront}</b>   Б <b>{first.ArmorSide}</b>   К <b>{first.ArmorRear}</b>     " +
                    $"{first.SpeedKmh:0} км/ч", textStyle);
                y += 20f;

                foreach (var weapon in first.Weapons)
                {
                    GUI.Label(new Rect(x, y, width * 0.7f, 18f), weapon.DisplayName, textStyle);
                    GUI.Label(new Rect(x + width * 0.7f, y, width * 0.3f, 18f), $"{weapon.Range:0} м",
                        new GUIStyle(smallStyle) { alignment = TextAnchor.UpperRight });
                    y += 18f;
                }
            }
            else
            {
                GUI.Label(new Rect(x, y + 2f, width, 20f), $"Группа  ×{selection.Count}", titleStyle);
                y += 26f;

                float chipX = x;
                foreach (var pair in counts)
                {
                    if (chipX + 52f > panel.xMax - Padding)
                        break;
                    UnitBadge(new Rect(chipX, y, 30f, 22f), pair.Key.Category);
                    GUI.Label(new Rect(chipX + 32f, y + 3f, 22f, 18f), pair.Value.ToString(), titleStyle);
                    chipX += 56f;
                }
                y += 30f;

                HealthBar(new Rect(x, y, width, 6f), health / maxHealth);
                GUI.Label(new Rect(x, y + 8f, width, 16f), $"{health:0.#} / {maxHealth:0}", smallStyle);
                y += 26f;
            }

            var fireMode = CommandInput.SelectedFireMode();
            string fire = fireMode == FireMode.FireAtWill ? "по готовности"
                : fireMode == FireMode.ReturnFire ? "<color=#F2B33D>только ответный</color>"
                : fireMode == FireMode.HoldFire ? "<color=#FF6A5A>запрещён</color>"
                : "разный";
            GUI.Label(new Rect(x, panel.yMax - Padding - 18f, width, 18f), $"Огонь: {fire}", textStyle);
        }

        void UnitBadge(Rect rect, UnitCategory category)
        {
            if (Event.current.type != EventType.Repaint)
                return;
            Color team = TeamExtensions.Player.ToColor();
            Fill(rect, new Color(team.r * 0.3f, team.g * 0.3f, team.b * 0.3f, 1f));
            Border(rect, team, 1f);
            DrawIcon(Inset(rect, 3f), HudIcons.Unit(category), Color.white);
        }

        void HealthBar(Rect rect, float fraction)
        {
            if (Event.current.type != EventType.Repaint)
                return;
            Fill(rect, new Color(0f, 0f, 0f, 0.7f));
            Fill(new Rect(rect.x, rect.y, rect.width * Mathf.Clamp01(fraction), rect.height),
                Color.Lerp(new Color(1f, 0.25f, 0.15f), new Color(0.35f, 0.95f, 0.35f), fraction));
        }

        void DrawTargetingHint()
        {
            var input = CommandInput.Instance;
            if (input == null || !input.IsTargeting)
                return;

            string text = input.Pending == PendingOrder.Move ? "Движение"
                : input.Pending == PendingOrder.FastMove ? "Быстрый марш"
                : input.Pending == PendingOrder.Attack ? "Атака: укажи врага или точку"
                : "Движение с атакой";
            Vector2 mouse = Event.current.mousePosition;
            var rect = new Rect(mouse.x + 18f, mouse.y + 14f, 230f, 36f);
            if (Event.current.type == EventType.Repaint)
            {
                Fill(rect, PanelColor);
                Border(rect, Amber, 1f);
            }
            GUI.Label(new Rect(rect.x + 8f, rect.y + 2f, rect.width - 16f, 18f), $"<color=#F2B33D>{text}</color>", textStyle);
            GUI.Label(new Rect(rect.x + 8f, rect.y + 18f, rect.width - 16f, 16f), "ЛКМ — выполнить, ПКМ/Esc — отмена", smallStyle);
        }

        void DrawTooltip()
        {
            if (string.IsNullOrEmpty(hoveredTooltip))
                return;
            Vector2 mouse = Event.current.mousePosition;
            var size = textStyle.CalcSize(new GUIContent(hoveredTooltip));
            var rect = new Rect(mouse.x - size.x - 24f, mouse.y - 30f, size.x + 16f, 24f);
            if (Event.current.type == EventType.Repaint)
            {
                Fill(rect, PanelColor);
                Border(rect, BorderColor, 1f);
            }
            GUI.Label(new Rect(rect.x + 8f, rect.y + 4f, size.x + 4f, 18f), hoveredTooltip,
                new GUIStyle(textStyle) { wordWrap = false });
        }

        void DrawResult()
        {
            var rect = new Rect(Screen.width * 0.5f - 220f, Screen.height * 0.5f - 70f, 440f, 140f);
            Panel(rect);
            if (Event.current.type == EventType.Repaint)
                Border(rect, Amber, 2f);
            GUI.Label(new Rect(rect.x, rect.y + 10f, rect.width, 70f), result, bigStyle);
            GUI.Label(new Rect(rect.x + 20f, rect.y + 90f, rect.width - 40f, 30f), "R — начать заново", centerStyle);
        }

        // ------------------------------------------------------------------
        // Primitives
        // ------------------------------------------------------------------

        void Panel(Rect rect)
        {
            if (Event.current.type == EventType.Layout)
            {
                blockingRects.Add(rect);
            }
            else if (Event.current.type == EventType.Repaint)
            {
                Fill(rect, PanelColor);
                Border(rect, BorderColor, 1f);
            }
        }

        void Fill(Rect rect, Color color)
        {
            var previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, white);
            GUI.color = previous;
        }

        void Border(Rect rect, Color color, float thickness)
        {
            Fill(new Rect(rect.xMin, rect.yMin, rect.width, thickness), color);
            Fill(new Rect(rect.xMin, rect.yMax - thickness, rect.width, thickness), color);
            Fill(new Rect(rect.xMin, rect.yMin, thickness, rect.height), color);
            Fill(new Rect(rect.xMax - thickness, rect.yMin, thickness, rect.height), color);
        }

        static void DrawIcon(Rect rect, Texture2D icon, Color tint)
        {
            if (icon == null)
                return;
            var previous = GUI.color;
            GUI.color = tint;
            GUI.DrawTexture(rect, icon, ScaleMode.ScaleToFit, true);
            GUI.color = previous;
        }

        static Rect Inset(Rect rect, float amount) =>
            new Rect(rect.x + amount, rect.y + amount, rect.width - amount * 2f, rect.height - amount * 2f);
    }
}
