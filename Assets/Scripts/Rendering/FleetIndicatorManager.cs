using System.Collections.Generic;
using UnityEngine;
using StellarisClone.Core;

namespace StellarisClone.Rendering
{
    /// <summary>
    /// Автоматически создаёт FleetIndicator над каждым FleetView.
    /// Работает только после старта игры (IsGameStarted).
    /// </summary>
    public class FleetIndicatorManager : MonoBehaviour
    {
        private readonly HashSet<FleetView> _known = new HashSet<FleetView>();

        private void Update()
        {
            if (!UIManager.IsGameStarted) return;
            if (FleetManager.Instance == null) return;

            var fleets = FleetManager.Instance.AllFleets;
            for (int i = 0; i < fleets.Count; i++)
            {
                var f = fleets[i];
                if (f == null) continue;
                if (_known.Contains(f)) continue;

                _known.Add(f);

                var indicatorObj = new GameObject($"Indicator_{f.Data?.Name ?? f.name}");
                indicatorObj.transform.SetParent(f.transform.parent, false);
                var ind = indicatorObj.AddComponent<FleetIndicator>();
                ind.Init(f);
            }
        }
    }
}