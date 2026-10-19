using System.Collections.Generic;
using UnityEngine;
using WargameRTS.Core;

namespace WargameRTS.UI
{
    /// <summary>
    /// Shared IMGUI kit: palette, resolution-independent scaling (layout is done
    /// in a virtual 1080p-high space), panels, buttons, toggles, icons, lines and
    /// the registry of screen areas that must not pass clicks to the game world.
    /// Call <see cref="Begin"/> at the start of every OnGUI.
    /// </summary>
    public static class HudStyle
    {
        // Palette (matches the Gemini style block and docs/ui-reference/hud-mockup.jpg).
        public static readonly Color Panel = new Color32(0x14, 0x18, 0x1D, 0xE0);
        public static readonly Color PanelSolid = new Color32(0x14, 0x18, 0x1D, 0xFA);
        public static readonly Color BorderColor = new Color32(0x3A, 0x44, 0x50, 0xFF);
        public static readonly Color ButtonColor = new Color32(0x1E, 0x24, 0x2C, 0xF2);
        public static readonly Color ButtonHover = new Color32(0x28, 0x30, 0x3A, 0xF2);
        public static readonly Color Amber = new Color32(0xF2, 0xB3, 0x3D, 0xFF);
        public static readonly Color Text = new Color32(0xE6, 0xE6, 0xE0, 0xFF);
        public static readonly Color Muted = new Color32(0x9A, 0xA3, 0xAD, 0xFF);
        public static readonly Color Dark = new Color32(0x14, 0x18, 0x1D, 0xFF);
        public static readonly Color Blue = new Color32(0x4D, 0x8C, 0xFF, 0xFF);
        public static readonly Color Red = new Color32(0xFF, 0x52, 0x40, 0xFF);

        public const string AmberHex = "#F2B33D";
        public const string MutedHex = "#9AA3AD";

        static readonly List<Rect> blockingCurrent = new List<Rect>();
        static readonly List<Rect> blockingPrevious = new List<Rect>();
        static int blockingFrame = -1;

        static GUIStyle text, small, title, center, big, header, buttonLabel;

        /// <summary>Tooltip collected during this OnGUI pass (drawn by <see cref="DrawTooltip"/>).</summary>
        public static string Tooltip;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            blockingCurrent.Clear();
            blockingPrevious.Clear();
            blockingFrame = -1;
            text = null;
        }

        // ------------------------------------------------------------------
        // Scaling
        // ------------------------------------------------------------------

        public static float Scale => Mathf.Max(0.5f, Screen.height / 1080f * GameSettings.UiScale);

        /// <summary>Virtual screen size in GUI units (after scaling).</summary>
        public static float Width => Screen.width / Scale;
        public static float Height => Screen.height / Scale;

        public static void Begin()
        {
            EnsureStyles();
            float scale = Scale;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
        }

        /// <summary>Converts an Input System screen point (pixels, Y up) to virtual GUI space.</summary>
        public static Vector2 ScreenToGui(Vector2 screenPoint)
        {
            float scale = Scale;
            return new Vector2(screenPoint.x / scale, (Screen.height - screenPoint.y) / scale);
        }

        /// <summary>Converts a camera WorldToScreenPoint result to virtual GUI space.</summary>
        public static Vector2 WorldScreenToGui(Vector3 screenPoint) => ScreenToGui(new Vector2(screenPoint.x, screenPoint.y));

        // ------------------------------------------------------------------
        // Click blocking
        // ------------------------------------------------------------------

        /// <summary>Registers a GUI-space rect that swallows clicks this frame.</summary>
        public static void Block(Rect rect)
        {
            if (Time.frameCount != blockingFrame)
            {
                blockingPrevious.Clear();
                blockingPrevious.AddRange(blockingCurrent);
                blockingCurrent.Clear();
                blockingFrame = Time.frameCount;
            }
            blockingCurrent.Add(rect);
        }

        /// <summary>True if the screen point (Input System coordinates, Y up) is over any HUD panel.</summary>
        public static bool IsPointerOverHud(Vector2 screenPoint)
        {
            Vector2 gui = ScreenToGui(screenPoint);
            foreach (var rect in blockingCurrent)
            {
                if (rect.Contains(gui))
                    return true;
            }
            foreach (var rect in blockingPrevious)
            {
                if (rect.Contains(gui))
                    return true;
            }
            return false;
        }

        // ------------------------------------------------------------------
        // Styles
        // ------------------------------------------------------------------

        public static GUIStyle TextStyle { get { EnsureStyles(); return text; } }
        public static GUIStyle SmallStyle { get { EnsureStyles(); return small; } }
        public static GUIStyle TitleStyle { get { EnsureStyles(); return title; } }
        public static GUIStyle CenterStyle { get { EnsureStyles(); return center; } }
        public static GUIStyle BigStyle { get { EnsureStyles(); return big; } }
        public static GUIStyle HeaderStyle { get { EnsureStyles(); return header; } }

        static void EnsureStyles()
        {
            if (text != null)
                return;

            text = new GUIStyle(GUI.skin.label)
            {
                fontSize = 14,
                richText = true,
                wordWrap = true,
                padding = new RectOffset(0, 0, 0, 0),
                margin = new RectOffset(0, 0, 0, 0),
                normal = { textColor = Text }
            };
            small = new GUIStyle(text) { fontSize = 12, wordWrap = false, normal = { textColor = Muted } };
            title = new GUIStyle(text) { fontSize = 15, fontStyle = FontStyle.Bold, wordWrap = false };
            center = new GUIStyle(title) { alignment = TextAnchor.MiddleCenter };
            big = new GUIStyle(center) { fontSize = 48 };
            header = new GUIStyle(title) { fontSize = 40, normal = { textColor = Text } };
            buttonLabel = new GUIStyle(center) { fontSize = 14, wordWrap = true };
        }

        // ------------------------------------------------------------------
        // Primitives
        // ------------------------------------------------------------------

        public static bool IsRepaint => Event.current.type == EventType.Repaint;

        public static void Fill(Rect rect, Color color)
        {
            if (!IsRepaint)
                return;
            var previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = previous;
        }

        public static void Border(Rect rect, Color color, float thickness = 1f)
        {
            Fill(new Rect(rect.xMin, rect.yMin, rect.width, thickness), color);
            Fill(new Rect(rect.xMin, rect.yMax - thickness, rect.width, thickness), color);
            Fill(new Rect(rect.xMin, rect.yMin, thickness, rect.height), color);
            Fill(new Rect(rect.xMax - thickness, rect.yMin, thickness, rect.height), color);
        }

        /// <summary>Background panel that also blocks clicks to the world.</summary>
        public static void DrawPanel(Rect rect, bool solid = false)
        {
            Block(rect);
            Fill(rect, solid ? PanelSolid : Panel);
            Border(rect, BorderColor);
            // Amber corner ticks, as in the mockup.
            Fill(new Rect(rect.xMin, rect.yMin, 10f, 2f), Amber);
            Fill(new Rect(rect.xMin, rect.yMin, 2f, 10f), Amber);
        }

        public static void Icon(Rect rect, Texture2D icon, Color tint)
        {
            if (icon == null || !IsRepaint)
                return;
            var previous = GUI.color;
            GUI.color = tint;
            GUI.DrawTexture(rect, icon, ScaleMode.ScaleToFit, true);
            GUI.color = previous;
        }

        public static void Line(Vector2 a, Vector2 b, Color color, float width = 1.5f)
        {
            if (!IsRepaint)
                return;
            Vector2 delta = b - a;
            float length = delta.magnitude;
            if (length < 0.5f)
                return;

            var matrix = GUI.matrix;
            float angle = Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg;
            GUIUtility.RotateAroundPivot(angle, a);
            Fill(new Rect(a.x, a.y - width * 0.5f, length, width), color);
            GUI.matrix = matrix;
        }

        public static Rect Inset(Rect rect, float amount) =>
            new Rect(rect.x + amount, rect.y + amount, rect.width - amount * 2f, rect.height - amount * 2f);

        public static void Label(Rect rect, string content, GUIStyle style = null) => GUI.Label(rect, content, style ?? TextStyle);

        // ------------------------------------------------------------------
        // Widgets
        // ------------------------------------------------------------------

        /// <summary>Flat button. Returns true on click.</summary>
        public static bool Button(Rect rect, string label, bool active = false, bool enabled = true, string tooltip = null,
            int fontSize = 14)
        {
            Block(rect);
            var current = Event.current;
            bool hover = rect.Contains(current.mousePosition);
            if (hover && tooltip != null)
                Tooltip = tooltip;

            if (enabled && hover && current.type == EventType.MouseDown && current.button == 0)
            {
                current.Use();
                return true;
            }

            if (!IsRepaint)
                return false;

            Fill(rect, active ? Amber : hover && enabled ? ButtonHover : ButtonColor);
            Border(rect, active || (hover && enabled) ? Amber : BorderColor);
            buttonLabel.fontSize = fontSize;
            buttonLabel.normal.textColor = !enabled ? new Color(Muted.r, Muted.g, Muted.b, 0.5f) : active ? Dark : Text;
            GUI.Label(rect, label, buttonLabel);
            return false;
        }

        /// <summary>Checkbox with a label. Returns the new value.</summary>
        public static bool Toggle(Rect rect, string label, bool value, string tooltip = null)
        {
            Block(rect);
            var current = Event.current;
            bool hover = rect.Contains(current.mousePosition);
            if (hover && tooltip != null)
                Tooltip = tooltip;

            if (hover && current.type == EventType.MouseDown && current.button == 0)
            {
                current.Use();
                return !value;
            }

            var box = new Rect(rect.x, rect.y + (rect.height - 16f) * 0.5f, 16f, 16f);
            Fill(box, ButtonColor);
            Border(box, hover ? Amber : BorderColor);
            if (value)
                Fill(Inset(box, 4f), Amber);
            GUI.Label(new Rect(rect.x + 24f, rect.y + (rect.height - 18f) * 0.5f, rect.width - 24f, 18f), label, TextStyle);
            return value;
        }

        /// <summary>Row of mutually exclusive buttons. Returns the selected index.</summary>
        public static int Segmented(Rect rect, string[] options, int selected, int fontSize = 13)
        {
            float gap = 4f;
            float width = (rect.width - gap * (options.Length - 1)) / options.Length;
            for (int i = 0; i < options.Length; i++)
            {
                var cell = new Rect(rect.x + i * (width + gap), rect.y, width, rect.height);
                if (Button(cell, options[i], i == selected, true, null, fontSize))
                    selected = i;
            }
            return selected;
        }

        public static void HealthBar(Rect rect, float fraction)
        {
            Fill(rect, new Color(0f, 0f, 0f, 0.75f));
            fraction = Mathf.Clamp01(fraction);
            Fill(new Rect(rect.x, rect.y, rect.width * fraction, rect.height),
                Color.Lerp(new Color(1f, 0.25f, 0.15f), new Color(0.35f, 0.95f, 0.35f), fraction));
        }

        /// <summary>Draws the tooltip collected this pass next to the mouse, then clears it.</summary>
        public static void DrawTooltip()
        {
            if (string.IsNullOrEmpty(Tooltip))
                return;

            Vector2 mouse = Event.current.mousePosition;
            var style = new GUIStyle(TextStyle) { wordWrap = false };
            var size = style.CalcSize(new GUIContent(Tooltip));
            float x = Mathf.Min(mouse.x + 16f, Width - size.x - 20f);
            float y = mouse.y - 32f < 0f ? mouse.y + 24f : mouse.y - 32f;
            var rect = new Rect(x, y, size.x + 16f, 24f);
            Fill(rect, PanelSolid);
            Border(rect, Amber);
            GUI.Label(new Rect(rect.x + 8f, rect.y + 3f, size.x + 4f, 18f), Tooltip, style);
            if (IsRepaint)
                Tooltip = null;
        }
    }
}
