using System.Collections.Generic;
using UnityEngine;
using StellarisClone.Generation;

namespace StellarisClone.Core
{
    /// <summary>
    /// Маршрут флота целиком: текущий перелёт, оставшиеся прыжки и очередь приказов (Shift+ПКМ),
    /// с накопленным временем в днях до каждой системы. Используется линией пути, HUD и подсказками.
    /// </summary>
    public static class FleetRoute
    {
        public struct Node
        {
            public int SystemId;
            /// <summary>Дней от текущего момента до прибытия в систему.</summary>
            public float Days;
            /// <summary>Номер пункта назначения (1 — текущий приказ, 2… — очередь); 0 — промежуточная система.</summary>
            public int OrderIndex;
        }

        /// <summary>Скорость прыжка с учётом технологий владельца и двигателей корабля.</summary>
        public static float JumpSpeed(FleetData d)
            => Mathf.Max(0.05f, EmpireBonuses.For(d.OwnerId).HyperlaneSpeed * Mathf.Max(0.5f, d.HyperSpeed));

        public static float DaysPerJump(FleetData d) => d.TotalDaysForTransit / JumpSpeed(d);

        public static List<Node> Build(FleetData d, GalaxyGenerator gen)
        {
            var nodes = new List<Node>();
            if (d == null || gen == null) return nodes;

            float perJump = DaysPerJump(d);
            float days = 0f;
            int last = d.CurrentSystemId;

            if (d.State == FleetState.InHyperlane && d.TargetSystemId >= 0)
            {
                days += Mathf.Max(0f, d.DaysRemainingInTransit) / JumpSpeed(d);
                nodes.Add(new Node { SystemId = d.TargetSystemId, Days = days });
                last = d.TargetSystemId;
            }
            foreach (int step in d.Path)
            {
                days += perJump;
                nodes.Add(new Node { SystemId = step, Days = days });
                last = step;
            }
            int order = 0;
            if (nodes.Count > 0)
            {
                var n = nodes[nodes.Count - 1];
                n.OrderIndex = ++order;
                nodes[nodes.Count - 1] = n;
            }

            foreach (int wp in d.OrderQueue)
            {
                var leg = GalaxyPathfinder.FindPath(last, wp, gen);
                if (leg == null) continue;
                foreach (int step in leg)
                {
                    days += perJump;
                    nodes.Add(new Node { SystemId = step, Days = days });
                }
                if (leg.Count > 0)
                {
                    var n = nodes[nodes.Count - 1];
                    n.OrderIndex = ++order;
                    nodes[nodes.Count - 1] = n;
                    last = wp;
                }
            }
            return nodes;
        }

        public static float TotalDays(FleetData d, GalaxyGenerator gen)
        {
            var nodes = Build(d, gen);
            return nodes.Count > 0 ? nodes[nodes.Count - 1].Days : 0f;
        }

        public static string FormatDays(float days)
        {
            int n = Mathf.Max(1, Mathf.CeilToInt(days));
            int m10 = n % 10, m100 = n % 100;
            string word = m10 == 1 && m100 != 11 ? "день"
                        : m10 >= 2 && m10 <= 4 && (m100 < 12 || m100 > 14) ? "дня" : "дней";
            return $"{n} {word}";
        }
    }
}
