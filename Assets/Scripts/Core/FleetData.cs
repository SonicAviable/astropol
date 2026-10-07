using System.Collections.Generic;
using UnityEngine;

namespace StellarisClone.Core
{
    public enum FleetType { Military, Constructor, Science }
    public enum FleetState { Orbiting, InHyperlane, Constructing, Surveying }

    public class FleetData
    {
        public int Id;
        public string Name;
        public FleetType Type;
        public int OwnerId;
        public int CurrentSystemId;
        public int TargetSystemId = -1;
        public FleetState State = FleetState.Orbiting;

        public Queue<int> Path = new Queue<int>();
        public float DaysRemainingInTransit;
        public float TotalDaysForTransit = GamePace.JumpDays;
        public int MilitaryPower = 120;

        public int BuildTargetSystemId = -1;
        public float DaysRemainingConstruction = 0f;
        public float TotalConstructionDays = GamePace.OutpostDays;

        public int SurveyTargetSystemId = -1;
        public float DaysRemainingSurvey = 0f;
        public float TotalSurveyDays = GamePace.SurveyDays;

        public ShipClass HullClass = ShipClass.Corvette;
        public string DesignId;
        public float DesignAlloyCost;

        public float HullPoints = 80f;
        public float ArmorPoints = 20f;
        public float ShieldPoints = 30f;
        public float MaxHullPoints = 80f;
        public float MaxArmorPoints = 20f;
        public float MaxShieldPoints = 30f;
        /// <summary>Суммарный урон одного залпа всех орудий (0 — корабль безоружен).</summary>
        public float Damage = 8f;
        public float Evasion = 12f;
        public float Accuracy;
        public WeaponDamageType PrimaryWeapon = WeaponDamageType.Kinetic;
        public float HyperSpeed = 1f;
        public readonly List<WeaponMount> Weapons = new List<WeaponMount>();

        // Ежемесячное содержание (энергия)
        public float UpkeepEnergy = 1f;

        public bool InCombat;
        public bool Destroyed;

        // ==================== ОЧЕРЕДЬ ПРИКАЗОВ ====================

        /// <summary>Следующие пункты назначения (Shift+ПКМ), после текущего маршрута.</summary>
        public readonly List<int> OrderQueue = new List<int>();

        /// <summary>Маршрут задан игроком — по прибытии показать уведомление.</summary>
        public bool HasPlayerOrder;

        public enum FleetEvent { None, Arrived, Surveyed, Built }

        /// <summary>Последнее завершённое действие (для вспышки значка). Время — Time.unscaledTime.</summary>
        [System.NonSerialized] public FleetEvent LastEvent;
        [System.NonSerialized] public float LastEventTime = -100f;

        public void MarkEvent(FleetEvent e)
        {
            LastEvent = e;
            LastEventTime = Time.unscaledTime;
        }

        /// <summary>Куда флот летит сейчас (конец текущего маршрута); -1 — стоит на месте.</summary>
        public int CurrentDestination
        {
            get
            {
                int last = -1;
                foreach (int step in Path) last = step;
                if (last >= 0) return last;
                if (State == FleetState.InHyperlane && TargetSystemId >= 0) return TargetSystemId;
                return -1;
            }
        }

        /// <summary>Есть ли у флота незавершённое дело (полёт, разведка, стройка).</summary>
        public bool IsBusy => Path.Count > 0 || State != FleetState.Orbiting;

        public FleetData(int id, string name, int startSystemId, FleetType type = FleetType.Military)
        {
            Id = id;
            Name = name;
            CurrentSystemId = startSystemId;
            Type = type;
            OwnerId = 0;
            MilitaryPower = type == FleetType.Military ? 120 : 0;
            ApplyDefaultCombatStats(type);
        }

        public void ApplyDefaultCombatStats(FleetType type)
        {
            UpkeepEnergy = UpkeepFor(type, HullClass);

            if (type != FleetType.Military)
            {
                HullPoints = MaxHullPoints = 40f;
                ArmorPoints = MaxArmorPoints = 8f;
                ShieldPoints = MaxShieldPoints = 0f;
                SetWeapons(null);
                Evasion = 8f;
                MilitaryPower = 0;
                return;
            }

            var dm = ShipDesignManager.Instance;
            var design = dm != null ? dm.GetLatestDesign(HullClass) : null;
            if (design != null)
            {
                ApplyDesign(design);
                return;
            }

            HullPoints = MaxHullPoints = 80f;
            ArmorPoints = MaxArmorPoints = 20f;
            ShieldPoints = MaxShieldPoints = 30f;
            SetWeapons(new[] { new WeaponMount(WeaponDamageType.Kinetic, 8f, 1f) });
            Evasion = 12f;
            Accuracy = 0f;
            MilitaryPower = 120;
            CombatMath.ApplyDurability(this);
        }

        /// <summary>Заменить орудия (копии — у каждого корабля своя перезарядка).</summary>
        public void SetWeapons(IEnumerable<WeaponMount> mounts)
        {
            Weapons.Clear();
            Damage = 0f;
            if (mounts != null)
                foreach (var m in mounts)
                {
                    if (m == null || m.Damage <= 0f) continue;
                    Weapons.Add(new WeaponMount(m.Type, m.Damage, m.FireRate));
                    Damage += m.Damage;
                }
            PrimaryWeapon = ShipDesign.DominantType(Weapons, WeaponDamageType.Kinetic);
        }

        public float Dps
        {
            get { float s = 0f; foreach (var w in Weapons) s += w.Dps; return s; }
        }

        /// <summary>
        /// Ежемесячное содержание в Гелии-3. Чем крупнее корпус — тем дороже флот обходится казне.
        /// </summary>
        public static float UpkeepFor(FleetType type, ShipClass hull)
        {
            switch (type)
            {
                case FleetType.Constructor: return 2f;
                case FleetType.Science:     return 1.5f;
            }
            return hull switch
            {
                ShipClass.Destroyer => 8f,
                ShipClass.Frigate   => 5f,
                _                   => 3f
            };
        }

        public void ApplyDesign(ShipDesign design)
        {
            if (design == null) return;
            HullClass = design.HullClass;
            DesignId = design.Id;
            DesignAlloyCost = design.AlloyCost;
            MaxHullPoints = HullPoints = design.Hull;
            MaxArmorPoints = ArmorPoints = design.Armor;
            MaxShieldPoints = ShieldPoints = design.Shields;
            SetWeapons(design.Mounts);
            Evasion = design.Evasion;
            Accuracy = design.Accuracy;
            HyperSpeed = design.Speed;
            MilitaryPower = Mathf.RoundToInt(design.Dps * 12f + design.Hull * 0.35f + design.Armor * 0.2f + design.Shields * 0.15f);

            UpkeepEnergy = UpkeepFor(FleetType.Military, design.HullClass);
            CombatMath.ApplyDurability(this);
        }

        public float IntegrityNormalized => Mathf.Clamp01(HullPoints / Mathf.Max(1f, MaxHullPoints));
    }
}