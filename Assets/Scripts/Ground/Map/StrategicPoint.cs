using UnityEngine;
using StellarisClone.Core;

namespace StellarisClone.Ground
{
    /// <summary>Что даёт точка держащему: обычный доход, шахта (+реквизиция), генератор (+энергия, щиты защитника), HQ.</summary>
    public enum PointKind { Relay, Urban, Industrial, Generator, Mining, Headquarters }

    /// <summary>
    /// Захватываемая точка: отряд стоит в круге 10 м, а врагов в круге нет — за 8 с точка переходит к нему
    /// (сначала нейтрализуется, потом захватывается). Владелец получает доход и право пополнять отряды рядом.
    /// </summary>
    public sealed class StrategicPoint
    {
        public const float Radius = 10f;
        public const float CaptureSeconds = 8f;

        public string Name;
        public PointKind Kind;
        public Vector3 Pos;
        /// <summary>Владелец: −1 — никто, 0 — атакующий, 1 — защитник.</summary>
        public int Owner = -1;
        /// <summary>Прогресс захвата: −1 (полностью у защитника) … +1 (у атакующего), 0 — нейтрально.</summary>
        public float Progress;
        /// <summary>Визуал: флагшток и кольцо (цвет — по владельцу).</summary>
        public GameObject Visual;
        public Renderer Ring, Flag;
        /// <summary>Район колонии, к которому привязана точка (для итогов: удержанные и разрушенные районы).</summary>
        public DistrictType? District;

        public float ReqIncome => Kind == PointKind.Mining ? 10f : 4f;
        public float EnergyIncome => Kind == PointKind.Generator ? 3f : 0f;

        public static string KindName(PointKind k) => k switch
        {
            PointKind.Urban => Loc.T("Жилой квартал"),
            PointKind.Industrial => Loc.T("Завод"),
            PointKind.Generator => Loc.T("Электростанция"),
            PointKind.Mining => Loc.T("Шахта"),
            PointKind.Headquarters => Loc.T("Штаб колонии"),
            _ => Loc.T("Ретранслятор")
        };

        /// <summary>Шаг захвата: present0/present1 — есть ли в круге бойцы сторон.</summary>
        public void Tick(float dt, bool present0, bool present1)
        {
            if (present0 == present1) return;   // никого или бой в круге — точка замерла
            float dir = present0 ? 1f : -1f;
            float target = dir;
            Progress = Mathf.MoveTowards(Progress, target, dt / CaptureSeconds);
            if (Progress >= 0.999f) Owner = 0;
            else if (Progress <= -0.999f) Owner = 1;
            else if (Owner == 0 && Progress < 0f || Owner == 1 && Progress > 0f) Owner = -1;
        }
    }
}
