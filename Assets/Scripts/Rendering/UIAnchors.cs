using System.Collections.Generic;
using UnityEngine;

namespace StellarisClone.Rendering
{
    /// <summary>
    /// Именованные элементы интерфейса, на которые может указать обучение (подсветка, стрелка).
    /// Элементы регистрируют себя при постройке; под одним ключом может быть несколько —
    /// тогда подсвечивается их общая рамка (например, все плашки ресурсов).
    /// </summary>
    public static class UIAnchors
    {
        public const string Ruler = "ruler";
        public const string Resources = "resources";
        public const string Speed = "speed";
        public const string Tech = "tech";
        public const string Designer = "designer";
        public const string Diplomacy = "diplomacy";
        public const string MapModes = "mapmodes";
        public const string FleetHud = "fleethud";

        private static readonly Dictionary<string, List<RectTransform>> s_All = new Dictionary<string, List<RectTransform>>();
        private static readonly Vector3[] s_Corners = new Vector3[4];

        public static void Register(string key, RectTransform rt)
        {
            if (string.IsNullOrEmpty(key) || rt == null) return;
            if (!s_All.TryGetValue(key, out var list)) s_All[key] = list = new List<RectTransform>();
            list.RemoveAll(r => r == null);
            if (!list.Contains(rt)) list.Add(rt);
        }

        /// <summary>
        /// Экранная рамка элемента (в пикселях экрана), если он сейчас виден.
        /// Учитывает скрытые объекты и прозрачные CanvasGroup (например, спрятанный HUD).
        /// </summary>
        public static bool TryGetScreenRect(string key, out Rect rect)
        {
            rect = default;
            if (!s_All.TryGetValue(key, out var list)) return false;
            bool any = false;
            float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
            foreach (var rt in list)
            {
                if (rt == null || !IsVisible(rt)) continue;
                var canvas = rt.GetComponentInParent<Canvas>();
                var cam = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
                rt.GetWorldCorners(s_Corners);
                foreach (var c in s_Corners)
                {
                    Vector2 sp = RectTransformUtility.WorldToScreenPoint(cam, c);
                    x0 = Mathf.Min(x0, sp.x); y0 = Mathf.Min(y0, sp.y);
                    x1 = Mathf.Max(x1, sp.x); y1 = Mathf.Max(y1, sp.y);
                }
                any = true;
            }
            if (!any) return false;
            rect = Rect.MinMaxRect(x0, y0, x1, y1);
            return true;
        }

        private static readonly List<CanvasGroup> s_Groups = new List<CanvasGroup>();

        private static bool IsVisible(RectTransform rt)
        {
            if (!rt.gameObject.activeInHierarchy) return false;
            rt.GetComponentsInParent(false, s_Groups);
            foreach (var g in s_Groups)
            {
                if (g.alpha < 0.2f) return false;
                if (g.ignoreParentGroups) break;
            }
            return true;
        }
    }
}
