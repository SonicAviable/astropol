using UnityEngine;
using WargameRTS.CameraRig;
using WargameRTS.Combat;
using WargameRTS.Core;
using WargameRTS.Map;
using WargameRTS.Selection;
using WargameRTS.Units;

namespace WargameRTS.UI
{
    /// <summary>
    /// Minimap: ground texture, towns, units (enemies only when spotted), camera view.
    /// LMB / drag moves the camera, RMB moves the selected units (Ctrl — with attack).
    /// </summary>
    public static class MinimapView
    {
        public static void Draw(Rect rect, Camera camera)
        {
            var map = GameMap.Instance;
            if (map == null)
                return;

            HudStyle.DrawPanel(rect, true);
            var area = HudStyle.Inset(rect, 6f);
            HandleInput(area, map);

            if (!HudStyle.IsRepaint)
                return;

            if (map.GroundTexture != null)
                GUI.DrawTexture(area, map.GroundTexture, ScaleMode.StretchToFill, false);

            bool fog = VisibilitySystem.Instance != null && VisibilitySystem.Instance.FogOfWar;
            foreach (var unit in Unit.All)
            {
                if (fog && !unit.IsVisibleTo(TeamExtensions.Player))
                    continue;
                Vector2 p = WorldToMinimap(area, map, unit.Position);
                float size = unit.IsSelected ? 5f : 4f;
                var dot = new Rect(p.x - size * 0.5f, p.y - size * 0.5f, size, size);
                HudStyle.Fill(dot, unit.IsSelected ? HudStyle.Amber : unit.Team.ToColor());
            }

            if (camera != null)
                DrawCameraFrustum(area, map, camera);

            HudStyle.Border(area, HudStyle.BorderColor);
        }

        static void HandleInput(Rect area, GameMap map)
        {
            var current = Event.current;
            if (!area.Contains(current.mousePosition))
                return;
            if (current.type != EventType.MouseDown && current.type != EventType.MouseDrag)
                return;

            Vector3 world = MinimapToWorld(area, map, current.mousePosition);
            if (current.button == 0 && RtsCameraController.Instance != null)
            {
                RtsCameraController.Instance.FocusOn(world);
                current.Use();
            }
            else if (current.button == 1 && current.type == EventType.MouseDown && CommandInput.Instance != null)
            {
                CommandInput.Instance.MoveSelectedTo(world, current.control);
                current.Use();
            }
        }

        static void DrawCameraFrustum(Rect area, GameMap map, Camera camera)
        {
            float groundHeight = map.HeightAt(camera.transform.position.x, camera.transform.position.z);
            var plane = new Plane(Vector3.up, new Vector3(0f, groundHeight, 0f));
            var corners = new Vector2[4];
            var viewport = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) };
            for (int i = 0; i < 4; i++)
            {
                Ray ray = camera.ViewportPointToRay(viewport[i]);
                Vector3 point;
                if (ray.direction.y < -0.01f && plane.Raycast(ray, out float distance))
                    point = ray.GetPoint(Mathf.Min(distance, 6000f));
                else
                    point = ray.origin + new Vector3(ray.direction.x, 0f, ray.direction.z).normalized * 6000f;
                corners[i] = WorldToMinimap(area, map, point);
            }

            for (int i = 0; i < 4; i++)
            {
                Vector2 a = ClampTo(area, corners[i]);
                Vector2 b = ClampTo(area, corners[(i + 1) % 4]);
                HudStyle.Line(a, b, new Color(1f, 1f, 1f, 0.85f), 1.2f);
            }
        }

        static Vector2 ClampTo(Rect area, Vector2 p) =>
            new Vector2(Mathf.Clamp(p.x, area.xMin, area.xMax), Mathf.Clamp(p.y, area.yMin, area.yMax));

        static Vector2 WorldToMinimap(Rect area, GameMap map, Vector3 world)
        {
            float u = (world.x + map.HalfSize) / map.Size;
            float v = (world.z + map.HalfSize) / map.Size;
            return new Vector2(area.x + u * area.width, area.y + (1f - v) * area.height);
        }

        static Vector3 MinimapToWorld(Rect area, GameMap map, Vector2 point)
        {
            float u = Mathf.Clamp01((point.x - area.x) / area.width);
            float v = Mathf.Clamp01(1f - (point.y - area.y) / area.height);
            return map.OnGround(new Vector3(u * map.Size - map.HalfSize, 0f, v * map.Size - map.HalfSize));
        }
    }
}
