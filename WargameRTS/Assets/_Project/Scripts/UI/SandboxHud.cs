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
    /// Temporary IMGUI interface: unit labels with health bars, selection panel,
    /// help, force counter and victory/defeat screen. Will be replaced by UI Toolkit.
    /// </summary>
    public sealed class SandboxHud : MonoBehaviour
    {
        const float LabelWidth = 34f;
        const float LabelHeight = 15f;

        static readonly List<Rect> blockingRects = new List<Rect>();

        Camera mainCamera;
        Texture2D white;
        GUIStyle labelStyle;
        GUIStyle panelStyle;
        GUIStyle titleStyle;
        GUIStyle bigStyle;
        bool showHelp = true;
        float battleTime;
        string result;

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

        void OnGUI()
        {
            EnsureStyles();
            if (mainCamera == null)
                mainCamera = Camera.main;

            if (Event.current.type == EventType.Layout)
                blockingRects.Clear();

            if (Event.current.type == EventType.Repaint && mainCamera != null)
                DrawUnitLabels();

            DrawTopBar();
            if (showHelp)
                DrawHelp();
            DrawSelectionPanel();
            if (result != null)
                DrawResult();
        }

        void EnsureStyles()
        {
            if (labelStyle != null)
                return;

            white = Texture2D.whiteTexture;
            labelStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 10,
                fontStyle = FontStyle.Bold,
                padding = new RectOffset(0, 0, 0, 0),
                normal = { textColor = Color.white }
            };
            panelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                richText = true,
                wordWrap = true,
                normal = { textColor = new Color(0.92f, 0.92f, 0.9f) }
            };
            titleStyle = new GUIStyle(panelStyle) { fontSize = 15, fontStyle = FontStyle.Bold };
            bigStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 46,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = Color.white }
            };
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

                if (unit.IsSelected)
                    Fill(new Rect(rect.x - 2f, rect.y - 2f, rect.width + 4f, rect.height + 7f), new Color(0.4f, 1f, 0.4f, 0.95f));

                Fill(rect, new Color(teamColor.r * 0.55f, teamColor.g * 0.55f, teamColor.b * 0.55f, 0.9f));
                GUI.Label(rect, unit.Definition.ShortName, labelStyle);

                float health01 = unit.Health / unit.Definition.MaxHealth;
                var bar = new Rect(rect.x, rect.yMax + 1f, rect.width, 3f);
                Fill(bar, new Color(0f, 0f, 0f, 0.8f));
                Fill(new Rect(bar.x, bar.y, bar.width * health01, bar.height),
                    Color.Lerp(new Color(1f, 0.2f, 0.1f), new Color(0.3f, 1f, 0.3f), health01));
            }
        }

        void DrawTopBar()
        {
            var rect = new Rect(Screen.width * 0.5f - 170f, 8f, 340f, 30f);
            Panel(rect);
            int minutes = Mathf.FloorToInt(battleTime / 60f);
            int seconds = Mathf.FloorToInt(battleTime % 60f);
            string fog = VisibilitySystem.Instance != null && !VisibilitySystem.Instance.FogOfWar ? "  <color=#ffcc66>туман выкл.</color>" : "";
            GUI.Label(new Rect(rect.x + 10f, rect.y + 5f, rect.width - 20f, 22f),
                $"<color=#6fa0ff>Синие: {CountAlive(Team.Blue)}</color>   {minutes:00}:{seconds:00}   " +
                $"<color=#ff6a5a>Красные: {CountAlive(Team.Red)}</color>{fog}", panelStyle);
        }

        void DrawHelp()
        {
            var rect = new Rect(10f, 10f, 330f, 300f);
            Panel(rect);
            GUI.Label(new Rect(rect.x + 10f, rect.y + 8f, rect.width - 20f, 22f), "Управление  (F1 — скрыть)", titleStyle);
            GUI.Label(new Rect(rect.x + 10f, rect.y + 34f, rect.width - 20f, rect.height - 40f),
                "<b>Камера</b>: WASD / стрелки / край экрана, колесо — зум, Q/E или средняя кнопка — поворот, Shift — быстрее\n" +
                "<b>Выделение</b>: ЛКМ, рамка ЛКМ, Shift — добавить, двойной клик — все такого типа, Esc — снять\n" +
                "<b>Группы</b>: Ctrl+1..9 — назначить, 1..9 — выбрать (дважды — к камере)\n" +
                "<b>Приказы</b>: ПКМ — двигаться строем, ПКМ с протяжкой — развернуть линию, " +
                "ПКМ по врагу — атаковать, Ctrl+ПКМ — движение с атакой, X — стоп\n" +
                "<b>F</b> — туман войны вкл/выкл\n" +
                "Лес скрывает и защищает пехоту, борт и корма танка слабее лба.",
                panelStyle);
        }

        void DrawSelectionPanel()
        {
            var selection = SelectionManager.Instance != null ? SelectionManager.Instance.Selected : null;
            if (selection == null || selection.Count == 0)
                return;

            int shown = Mathf.Min(selection.Count, 10);
            float height = 34f + shown * 20f + (selection.Count > shown ? 20f : 0f);
            var rect = new Rect(10f, Screen.height - height - 10f, 360f, height);
            Panel(rect);
            GUI.Label(new Rect(rect.x + 10f, rect.y + 6f, rect.width - 20f, 22f), $"Выбрано: {selection.Count}", titleStyle);

            for (int i = 0; i < shown; i++)
            {
                var unit = selection[i];
                var row = new Rect(rect.x + 10f, rect.y + 30f + i * 20f, rect.width - 20f, 20f);
                string state = unit.CurrentTarget != null ? "<color=#ff8866>бой</color>" :
                    unit.IsMoving ? "марш" : "ожидание";
                GUI.Label(row, $"{unit.Definition.DisplayName}  —  {unit.Health:0.#}/{unit.Definition.MaxHealth:0}  [{state}]", panelStyle);
            }

            if (selection.Count > shown)
                GUI.Label(new Rect(rect.x + 10f, rect.y + 30f + shown * 20f, rect.width - 20f, 20f),
                    $"… и ещё {selection.Count - shown}", panelStyle);
        }

        void DrawResult()
        {
            var rect = new Rect(Screen.width * 0.5f - 220f, Screen.height * 0.5f - 70f, 440f, 140f);
            Panel(rect);
            GUI.Label(new Rect(rect.x, rect.y + 10f, rect.width, 70f), result, bigStyle);
            GUI.Label(new Rect(rect.x + 20f, rect.y + 90f, rect.width - 40f, 30f), "R — начать заново", titleStyle);
        }

        void Panel(Rect rect)
        {
            if (Event.current.type == EventType.Layout)
                blockingRects.Add(rect);
            else if (Event.current.type == EventType.Repaint)
                Fill(rect, new Color(0.05f, 0.06f, 0.07f, 0.78f));
        }

        void Fill(Rect rect, Color color)
        {
            var previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, white);
            GUI.color = previous;
        }
    }
}
