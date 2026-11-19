using UnityEngine;

namespace StellarisClone.Rendering
{
    /// <summary>
    /// Дешёвая проверка «точка (с запасом radius) в кадре главной камеры»: плоскости пирамиды видимости
    /// считаются один раз за кадр. Для сотен объектов карты (звёзды, подписи), которым незачем
    /// анимироваться за краем экрана.
    /// </summary>
    public static class ScreenCull
    {
        private static readonly Plane[] s_planes = new Plane[6];
        private static int s_frame = -1;
        private static Camera s_cam;

        public static bool Visible(Vector3 p, float radius)
        {
            if (s_frame != Time.frameCount)
            {
                s_frame = Time.frameCount;
                s_cam = Camera.main;
                if (s_cam != null) GeometryUtility.CalculateFrustumPlanes(s_cam, s_planes);
            }
            if (s_cam == null) return true;
            for (int i = 0; i < 6; i++)
                if (s_planes[i].GetDistanceToPoint(p) < -radius) return false;
            return true;
        }
    }
}
