using System.Collections.Generic;
using UnityEngine;
using StellarisClone.Core;

namespace StellarisClone.Ground
{
    /// <summary>Чем бьёт оружие: от этого зависят укрытия, кровь и смерть (пуля — падение, взрыв — ошмётки, огонь — обугливание).</summary>
    public enum WeaponKind { Bullet, Heavy, Explosive, Flame, Energy, Sniper, Melee, Crush }

    /// <summary>Оружие бойца или машины. Числа — стартовые, крутятся балансом.</summary>
    public sealed class WeaponDef
    {
        public string Name;
        public WeaponKind Kind;
        public float Range = 32f;
        public float Damage = 10f;
        /// <summary>Секунд между выстрелами.</summary>
        public float Cooldown = 1.2f;
        /// <summary>Попадание по открытой цели на средней дистанции (0…1).</summary>
        public float Accuracy = 0.55f;
        /// <summary>Подавление за выстрел (шкала отряда 0…100).</summary>
        public float Suppression;
        /// <summary>Радиус взрыва (0 — точечное).</summary>
        public float Splash;
        /// <summary>Пробитие: доля урона, проходящая сквозь броню техники (0…1).</summary>
        public float Pierce = 0.1f;
        /// <summary>Навесная стрельба (артиллерия): не нужна прямая видимость стрелка, только союзника.</summary>
        public bool Indirect;
        /// <summary>Сколько стреляет до перезарядки (очередь) и пауза после неё.</summary>
        public int Burst = 1;
        public float Reload;
        /// <summary>Нужно развернуться перед стрельбой (пулемёт), секунд.</summary>
        public float SetupTime;
        /// <summary>Множитель урона по зданиям и укрытиям.</summary>
        public float StructureMult = 0.3f;

        public bool IgnoresCoverHalf => Kind == WeaponKind.Flame || Kind == WeaponKind.Explosive && Indirect;
        public bool Gibs => Kind == WeaponKind.Explosive || Kind == WeaponKind.Flame || Kind == WeaponKind.Crush || Kind == WeaponKind.Heavy && Splash > 0f;
    }

    public enum UnitRole { Assault, Heavy, MachineGun, Rocket, Sniper, Flamer, Militia, Apc, Tank, Artillery, Hero, Walker, Swarm, Seer, Mercenary }

    /// <summary>
    /// Тип отряда или машины. Каталог общий для всех фракций; уникальные юниты фракций — отдельными записями
    /// с ключом фракции. Имена — через Loc.T при построении каталога (язык не меняется до перезапуска).
    /// </summary>
    public sealed class UnitDef
    {
        public string Id, Name, Desc;
        public UnitRole Role;
        /// <summary>Бойцов в отряде вместе с сержантом (техника — 1).</summary>
        public int Size = 6;
        public float Hp = 60f;
        public float Speed = 4.2f;
        public WeaponDef Weapon, SergeantWeapon, Melee;
        public int Requisition = 100, Energy;
        public bool Vehicle;
        /// <summary>Броня техники (доля урона, которую она гасит без пробития).</summary>
        public float Armor;
        /// <summary>Мест в лимите: пехота — по бойцу, техника — 6.</summary>
        public int Slots => Vehicle ? 6 : Size;
        public float SightRange = 38f;
        /// <summary>Нет морали (машины Консенсуса, дроны, техника).</summary>
        public bool Fearless;
        /// <summary>Страшный юнит: рядом с ним мораль врага падает.</summary>
        public bool Terror;
        /// <summary>Невидим в укрытии, пока не стреляет вблизи (снайперы).</summary>
        public bool Stealth;
        /// <summary>Только для фракции (null — для всех).</summary>
        public string FactionKey;
        /// <summary>Нельзя заказать (герой, ополчение, рой).</summary>
        public bool Special;
        /// <summary>Модель: множитель ширины плеч и роста.</summary>
        public float Bulk = 1f, Scale = 1f;
        public string[] Abilities = System.Array.Empty<string>();
        public LGIconHint Icon;
        /// <summary>Отряд может проводить добивания (элита, герои, шагоход).</summary>
        public bool SyncKills;
    }

    /// <summary>Значок отряда в HUD (имена значков интерфейса проекта).</summary>
    public enum LGIconHint { Infantry, Heavy, Gun, Rocket, Sniper, Flame, Vehicle, Tank, Artillery, Hero, Walker, Swarm, Seer }

    public static class UnitCatalog
    {
        private static Dictionary<string, UnitDef> s_all;
        private static List<UnitDef> s_list;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetCache() { s_all = null; s_list = null; }

        public static UnitDef Get(string id) { Init(); return id != null && s_all.TryGetValue(id, out var d) ? d : null; }

        public static IReadOnlyList<UnitDef> All { get { Init(); return s_list; } }

        /// <summary>Что сторона может заказать капсулой: общие юниты + уникальные своей фракции.</summary>
        public static List<UnitDef> Buyable(string factionKey)
        {
            Init();
            var list = new List<UnitDef>();
            foreach (var d in s_list)
                if (!d.Special && (d.FactionKey == null || d.FactionKey == factionKey)) list.Add(d);
            return list;
        }

        // ---- Оружие ----
        private static WeaponDef Rifle() => new WeaponDef { Name = Loc.T("Штурмовая винтовка"), Kind = WeaponKind.Bullet, Range = 30f, Damage = 7f, Cooldown = 0.9f, Accuracy = 0.5f, Suppression = 1.2f, Burst = 3, Reload = 1.6f };
        private static WeaponDef Pistol() => new WeaponDef { Name = Loc.T("Пистолет-пулемёт"), Kind = WeaponKind.Bullet, Range = 22f, Damage = 6f, Cooldown = 0.5f, Accuracy = 0.5f, Suppression = 0.8f };
        private static WeaponDef HeavyGun() => new WeaponDef { Name = Loc.T("Тяжёлый пулемёт"), Kind = WeaponKind.Heavy, Range = 42f, Damage = 6f, Cooldown = 0.12f, Accuracy = 0.4f, Suppression = 3.2f, Burst = 18, Reload = 2.4f, SetupTime = 2f };
        private static WeaponDef Launcher() => new WeaponDef { Name = Loc.T("Ракетомёт"), Kind = WeaponKind.Explosive, Range = 40f, Damage = 70f, Cooldown = 4.5f, Accuracy = 0.55f, Splash = 2.2f, Pierce = 0.85f, Suppression = 8f, StructureMult = 1.6f };
        private static WeaponDef LongRifle() => new WeaponDef { Name = Loc.T("Снайперская винтовка"), Kind = WeaponKind.Sniper, Range = 60f, Damage = 90f, Cooldown = 4f, Accuracy = 0.85f, Suppression = 10f };
        private static WeaponDef Flamer() => new WeaponDef { Name = Loc.T("Огнемёт"), Kind = WeaponKind.Flame, Range = 14f, Damage = 9f, Cooldown = 0.25f, Accuracy = 0.9f, Splash = 2.5f, Suppression = 4f, StructureMult = 0.8f };
        private static WeaponDef Blade(float dmg) => new WeaponDef { Name = Loc.T("Силовой клинок"), Kind = WeaponKind.Melee, Range = 2.2f, Damage = dmg, Cooldown = 1.1f, Accuracy = 0.8f };
        private static WeaponDef Autocannon() => new WeaponDef { Name = Loc.T("Автопушка"), Kind = WeaponKind.Heavy, Range = 40f, Damage = 14f, Cooldown = 0.35f, Accuracy = 0.5f, Suppression = 2.5f, Burst = 6, Reload = 2f, Pierce = 0.35f, Splash = 1f };
        private static WeaponDef Cannon() => new WeaponDef { Name = Loc.T("Танковая пушка"), Kind = WeaponKind.Explosive, Range = 48f, Damage = 120f, Cooldown = 5f, Accuracy = 0.6f, Splash = 3.5f, Pierce = 0.9f, Suppression = 15f, StructureMult = 2f };
        private static WeaponDef Howitzer() => new WeaponDef { Name = Loc.T("Гаубица"), Kind = WeaponKind.Explosive, Range = 120f, Damage = 110f, Cooldown = 7f, Accuracy = 0.35f, Splash = 6f, Pierce = 0.6f, Suppression = 25f, Indirect = true, StructureMult = 2f };
        private static WeaponDef Lasgun() => new WeaponDef { Name = Loc.T("Лазерная винтовка"), Kind = WeaponKind.Energy, Range = 34f, Damage = 9f, Cooldown = 1.1f, Accuracy = 0.6f, Suppression = 1f, Burst = 2, Reload = 1.4f };

        private static void Add(UnitDef d) { s_all[d.Id] = d; s_list.Add(d); }

        private static void Init()
        {
            if (s_all != null) return;
            s_all = new Dictionary<string, UnitDef>();
            s_list = new List<UnitDef>();

            Add(new UnitDef { Id = "assault", Role = UnitRole.Assault, Name = Loc.T("Штурмовая пехота"), Desc = Loc.T("Основа армии: захват точек, гранаты."),
                Size = 6, Hp = 60f, Speed = 4.4f, Weapon = Rifle(), Melee = Blade(12f), Requisition = 100, Abilities = new[] { "grenade" }, Icon = LGIconHint.Infantry });
            Add(new UnitDef { Id = "heavy", Role = UnitRole.Heavy, Name = Loc.T("Тяжёлая пехота"), Desc = Loc.T("Рукопашная, ломает укрытия, проводит добивания."),
                Size = 4, Hp = 140f, Speed = 4.0f, Weapon = Pistol(), Melee = Blade(30f), Requisition = 180, Bulk = 1.35f, Scale = 1.08f,
                Abilities = new[] { "charge" }, Icon = LGIconHint.Heavy, SyncKills = true, Terror = true });
            Add(new UnitDef { Id = "mg", Role = UnitRole.MachineGun, Name = Loc.T("Пулемётчики"), Desc = Loc.T("Прижимают пехоту к земле. Перед стрельбой ставят пулемёт."),
                Size = 3, Hp = 60f, Speed = 3.8f, Weapon = HeavyGun(), SergeantWeapon = Rifle(), Melee = Blade(8f), Requisition = 140, Icon = LGIconHint.Gun });
            Add(new UnitDef { Id = "rocket", Role = UnitRole.Rocket, Name = Loc.T("Ракетчики"), Desc = Loc.T("Против техники и зданий. Медленная перезарядка."),
                Size = 4, Hp = 55f, Speed = 4.0f, Weapon = Launcher(), SergeantWeapon = Rifle(), Melee = Blade(8f), Requisition = 160, Icon = LGIconHint.Rocket });
            Add(new UnitDef { Id = "sniper", Role = UnitRole.Sniper, Name = Loc.T("Снайперы"), Desc = Loc.T("Убивают по одному на 60 м. Невидимы в укрытии."),
                Size = 2, Hp = 45f, Speed = 4.4f, Weapon = LongRifle(), Melee = Blade(6f), Requisition = 150, Stealth = true, Bulk = 0.85f, SightRange = 55f, Icon = LGIconHint.Sniper });
            Add(new UnitDef { Id = "flamer", Role = UnitRole.Flamer, Name = Loc.T("Огнемётчики"), Desc = Loc.T("Выжигают укрытия. Горящие враги бегут."),
                Size = 4, Hp = 70f, Speed = 4.1f, Weapon = Flamer(), Melee = Blade(10f), Requisition = 150, Terror = true, Bulk = 1.1f, Icon = LGIconHint.Flame });
            Add(new UnitDef { Id = "apc", Role = UnitRole.Apc, Name = Loc.T("БТР"), Desc = Loc.T("Быстрый, лёгкая автопушка, защищает отряды рядом."),
                Size = 1, Hp = 420f, Speed = 8.5f, Weapon = Autocannon(), Requisition = 200, Energy = 30, Vehicle = true, Armor = 0.55f, Fearless = true, Icon = LGIconHint.Vehicle });
            Add(new UnitDef { Id = "tank", Role = UnitRole.Tank, Name = Loc.T("Танк"), Desc = Loc.T("Тяжёлая броня и пушка. Давит пехоту."),
                Size = 1, Hp = 900f, Speed = 5.2f, Weapon = Cannon(), SergeantWeapon = HeavyGun(), Requisition = 350, Energy = 80, Vehicle = true, Armor = 0.8f, Fearless = true, Terror = true, Icon = LGIconHint.Tank });
            Add(new UnitDef { Id = "artillery", Role = UnitRole.Artillery, Name = Loc.T("Артиллерия"), Desc = Loc.T("Бьёт на 120 м по площади. Цель должен видеть союзник."),
                Size = 1, Hp = 380f, Speed = 3.6f, Weapon = Howitzer(), Requisition = 300, Energy = 60, Vehicle = true, Armor = 0.4f, Fearless = true, SightRange = 25f, Icon = LGIconHint.Artillery });

            // Особые (не заказываются)
            Add(new UnitDef { Id = "hero", Role = UnitRole.Hero, Name = Loc.T("Генерал"), Desc = Loc.T("Аура морали, страшен в рукопашной. Возвращается капсулой."),
                Size = 1, Hp = 520f, Speed = 4.6f, Weapon = Pistol(), Melee = Blade(60f), Special = true, Bulk = 1.45f, Scale = 1.2f, Fearless = true, Terror = true, SyncKills = true,
                Abilities = new[] { "charge" }, Icon = LGIconHint.Hero });
            Add(new UnitDef { Id = "militia", Role = UnitRole.Militia, Name = Loc.T("Ополчение"), Desc = Loc.T("Колонисты с оружием. Слабы, но защищают дом."),
                Size = 6, Hp = 40f, Speed = 4.2f, Weapon = Pistol(), Melee = Blade(6f), Requisition = 0, Special = true, Bulk = 0.95f, Icon = LGIconHint.Infantry });

            // ---- Уникальные юниты фракций ----
            Add(new UnitDef { Id = "xarn_titan", Role = UnitRole.Walker, FactionKey = "xarn", Name = Loc.T("Титан-шагоход"), Desc = Loc.T("Огромный и медленный. Давит всё, топот трясёт землю."),
                Size = 1, Hp = 2200f, Speed = 3.0f, Weapon = Cannon(), SergeantWeapon = HeavyGun(), Melee = Blade(160f), Requisition = 600, Energy = 160, Vehicle = true, Armor = 0.85f,
                Fearless = true, Terror = true, SyncKills = true, Scale = 2.2f, Icon = LGIconHint.Walker });
            Add(new UnitDef { Id = "nol_swarm", Role = UnitRole.Swarm, FactionKey = "nol", Name = Loc.T("Рой"), Desc = Loc.T("20 дронов без морали. Каждый остов на поле даёт +2 дрона."),
                Size = 20, Hp = 18f, Speed = 6.2f, Weapon = new WeaponDef { Name = Loc.T("Резак"), Kind = WeaponKind.Energy, Range = 8f, Damage = 5f, Cooldown = 0.6f, Accuracy = 0.75f, Suppression = 1.5f },
                Melee = Blade(6f), Requisition = 220, Fearless = true, Terror = true, Scale = 0.45f, Bulk = 0.8f, Icon = LGIconHint.Swarm });
            Add(new UnitDef { Id = "iridia_seer", Role = UnitRole.Seer, FactionKey = "iridia", Name = Loc.T("Провидец"), Desc = Loc.T("«Видение» показывает пути врага. Дальний телекинез."),
                Size = 1, Hp = 260f, Speed = 4.2f, Weapon = new WeaponDef { Name = Loc.T("Телекинез"), Kind = WeaponKind.Explosive, Range = 45f, Damage = 45f, Cooldown = 3.5f, Accuracy = 0.8f, Splash = 2.5f, Suppression = 12f, Pierce = 0.5f },
                Melee = Blade(20f), Requisition = 280, Energy = 40, Abilities = new[] { "vision" }, Scale = 1.1f, SightRange = 60f, Icon = LGIconHint.Seer });
            Add(new UnitDef { Id = "aquila_merc", Role = UnitRole.Mercenary, FactionKey = "aquila", Name = Loc.T("Наёмники"), Desc = Loc.T("Платятся гелием-3 из казны империи. Сильны, но держатся за деньги, а не за империю."),
                Size = 5, Hp = 110f, Speed = 4.6f, Weapon = Lasgun(), SergeantWeapon = Launcher(), Melee = Blade(18f), Requisition = 0, Bulk = 1.15f, Abilities = new[] { "grenade" }, Icon = LGIconHint.Heavy });
            Add(new UnitDef { Id = "terraan_roots", Role = UnitRole.Assault, FactionKey = "terraan", Name = Loc.T("Садовники"), Desc = Loc.T("Сеют споры-ловушки и выращивают деревья-укрытия."),
                Size = 5, Hp = 75f, Speed = 4.2f, Weapon = Rifle(), Melee = Blade(10f), Requisition = 150, Abilities = new[] { "spores", "grove" }, Icon = LGIconHint.Infantry });
        }

        /// <summary>Цена наёмников Аквилы в гелии-3 из казны империи.</summary>
        public const float MercenaryHelium = 120f;
    }
}
