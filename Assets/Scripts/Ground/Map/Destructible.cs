using System.Collections.Generic;
using UnityEngine;

namespace StellarisClone.Ground
{
    /// <summary>
    /// Разрушаемый объект поля боя: здание, бункер, мешки с песком, турель, щитовой купол, штаб.
    /// При потере прочности рушится в груду обломков (новое лёгкое укрытие), горит и дымит;
    /// точки укрытия вокруг него пропадают. Бункеры и турели защитника сами стреляют.
    /// </summary>
    public sealed class Destructible
    {
        public enum Kind { Building, Bunker, Sandbags, Turret, ShieldDome, Headquarters, Rock, Tree, Factory, PowerPlant, Mine }

        public Kind Type;
        public GameObject Go;
        public Vector3 Pos;
        /// <summary>Радиус «попадания» взрывом (по площади основания).</summary>
        public float Radius = 3f;
        public float Hp = 1000f, MaxHp = 1000f;
        /// <summary>Сторона, которой принадлежит (−1 — ничья). Турели и бункеры стреляют по врагам этой стороны.</summary>
        public int Side = -1;
        public bool Dead;
        /// <summary>Оружие турели или бункера (null — не стреляет).</summary>
        public WeaponDef Weapon;
        public float Cooldown;
        /// <summary>Район колонии, который объект изображает (для подсчёта разрушенных районов).</summary>
        public Core.DistrictType? District;
        public readonly List<Renderer> Renderers = new List<Renderer>();
        public readonly List<Collider> Colliders = new List<Collider>();

        public float Health01 => MaxHp <= 0f ? 0f : Mathf.Clamp01(Hp / MaxHp);
    }
}
