using System.Collections.Generic;
using UnityEngine;
using StellarisClone.Core;

namespace StellarisClone.Rendering
{
    /// <summary>
    /// Поля астероидов в пустотах между системами — космос не выглядит пустым при приближении.
    /// Каждое поле привязано к ближайшей системе: под туманом войны оно появляется, когда игрок её исследует.
    /// Видны только при приближении камеры (как мини-системы), в виде системы и в меню скрыты.
    /// </summary>
    public partial class GalaxyView
    {
        private const float AsteroidShowHeight = 130f;
        private GameObject _asteroidRoot;
        private bool _asteroidsShown;

        private void BuildAsteroidFields(float R, System.Random rng)
        {
            if (!AsteroidAssets.Ready || _generator == null || _generator.Systems.Count == 0) return;
            _asteroidRoot = new GameObject("AsteroidFields");
            _asteroidRoot.transform.SetParent(transform, false);

            int want = Mathf.Clamp(_generator.Systems.Count / 5, 8, 50);
            var placed = new List<Vector3>();
            for (int attempt = 0; attempt < want * 60 && placed.Count < want; attempt++)
            {
                float r = Mathf.Lerp(R * 0.2f, R * 0.96f, Mathf.Sqrt((float)rng.NextDouble()));
                float a = (float)rng.NextDouble() * Mathf.PI * 2f;
                var p = new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);

                // Пустота: до ближайшей звезды не меньше 13, но и не на отшибе
                int nearest = -1;
                float best = float.MaxValue;
                foreach (var s in _generator.Systems)
                {
                    float d = (s.Position.x - p.x) * (s.Position.x - p.x) + (s.Position.z - p.z) * (s.Position.z - p.z);
                    if (d < best) { best = d; nearest = s.Id; }
                }
                best = Mathf.Sqrt(best);
                if (best < 13f || best > 40f) continue;
                bool crowded = false;
                foreach (var q in placed) if ((q - p).sqrMagnitude < 28f * 28f) { crowded = true; break; }
                if (crowded) continue;
                placed.Add(p);

                p.y = _generator.Systems[nearest].Position.y + ((float)rng.NextDouble() - 0.5f) * 1.5f;
                var field = AsteroidField.CreateCluster(_asteroidRoot.transform, p, Mathf.Lerp(3.5f, 7.5f, (float)rng.NextDouble()),
                                                        26 + rng.Next(26), rng.Next(), 0.10f, 0.42f, 0.3f);
                int anchor = nearest;
                field.VisibleWhen = () => _asteroidsShown && Vision.PlayerKnows(anchor);
            }
        }

        private void UpdateAsteroidFields(Camera cam)
        {
            if (_asteroidRoot == null || cam == null) return;
            _asteroidsShown = cam.transform.position.y < AsteroidShowHeight && !MenuAtmosphere.IsActive
                              && !(SystemViewManager.Instance != null && SystemViewManager.Instance.IsInSystemView);
        }
    }
}
