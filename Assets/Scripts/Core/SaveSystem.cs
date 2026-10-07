using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using StellarisClone.Cam;
using StellarisClone.Generation;
using StellarisClone.Rendering;

namespace StellarisClone.Core
{
    // ====================================================================== Формат файла

    [Serializable]
    public class SaveFile
    {
        public int Version = SaveSystem.FormatVersion;
        public SaveMeta Meta = new SaveMeta();
        public GameState State = new GameState();
    }

    /// <summary>Только шапка — для быстрого списка сохранений.</summary>
    [Serializable]
    public class SaveHeader
    {
        public int Version;
        public SaveMeta Meta;
    }

    [Serializable]
    public class SaveMeta
    {
        public string Name;
        public string Empire;
        public string EmpireTitle;
        public Color EmpireColor = Color.white;
        public string GameDate;
        public int Year;
        public int Systems;
        public int Colonies;
        public int Population;
        public int Techs;
        public int GalaxySize;
        public int Difficulty;
        public long SavedAtUtcTicks;
        public float PlayTime;
        public bool Auto;
    }

    [Serializable]
    public class GameState
    {
        public NewGameSettings Settings;
        public string Faction;
        public int Day, Month, Year;
        public EconomySave Economy;
        public TechSave Tech;
        public VictorySave Victory;
        /// <summary>Только старые сохранения (одна империя ИИ).</summary>
        public AISave AI;
        public List<AISave> AIs = new List<AISave>();
        public List<AIRelations.PairState> AIPairs = new List<AIRelations.PairState>();
        public List<SiegeSave> Sieges = new List<SiegeSave>();
        public List<ConstructionJob> Jobs = new List<ConstructionJob>();
        public List<StarbaseSave> Starbases = new List<StarbaseSave>();
        public LeaderState Leaders;
        public EventState Events;
        public List<SystemSave> Systems = new List<SystemSave>();
        public List<FleetSave> Fleets = new List<FleetSave>();
        public List<DesignSave> Designs = new List<DesignSave>();
        public int FleetIdCounter;
        public bool HasCamera;
        public Vector3 CameraPos;
        public Quaternion CameraRot;
    }

    [Serializable]
    public class SystemSave
    {
        public int Id;
        public string Name;
        public Vector3 Position;
        public int Spectral;
        public List<int> Connected = new List<int>();
        public bool HasGeneratedPlanets;
        public int OwnerId;
        public bool HasStarbase;
        public bool IsSurveyed;
        public bool SurveyedByAI;  // до версии 2: один флаг на всех ИИ
        public int AISurvey;       // с версии 2: бит на каждую империю ИИ
        public int SurveyVer;      // 0 — старое сохранение (разведка была общей)
        public List<PlanetSave> Planets = new List<PlanetSave>();
    }

    [Serializable]
    public class PlanetSave
    {
        public string Name;
        public int Type;
        public float OrbitRadius, OrbitSpeed, Size;
        public Color Color;
        public int Energy, Minerals;
        public bool HasMiningStation;
        public bool Terraforming;
        public int Population;
        public float PopGrowth;
        public int MaxDistricts;
        public List<int> Districts = new List<int>();
    }

    [Serializable]
    public class FleetSave
    {
        public int Id;
        public string Name;
        public int Type, Owner, Current, Target, State;
        public List<int> Path = new List<int>();
        public List<int> Queue = new List<int>();
        public float DaysTransit, TotalTransit;
        public int MilitaryPower;
        public int BuildTarget;
        public float DaysConstruction, TotalConstruction;
        public int SurveyTarget;
        public float DaysSurvey, TotalSurvey;
        public int Hull;
        public string DesignId;
        public float DesignAlloyCost;
        public float HP, Armor, Shield, MaxHP, MaxArmor, MaxShield;
        public float Damage, FireRate, Evasion, HyperSpeed, Upkeep;
        public int Weapon;
        // Орудия по отдельности (с версии с независимыми орудиями; в старых сохранениях пусто)
        public List<int> WType = new List<int>();
        public List<float> WDamage = new List<float>();
        public List<float> WRate = new List<float>();
        public float Accuracy;
    }

    [Serializable]
    public class DesignSave
    {
        public string Id;
        public string Name;
        public int Hull;
        public List<string> Weapons = new List<string>();
        public List<string> Defense = new List<string>();
        public List<string> Utility = new List<string>();
    }

    [Serializable]
    public class EconomySave
    {
        public float Energy, Minerals, Alloys, Influence;
        public float BaseEnergy, BaseMinerals, BaseAlloys, BaseInfluence;
        public float MultEnergy = 1f, MultMinerals = 1f, MultAlloys = 1f, MultInfluence = 1f;
        public bool Bankrupt;
        public float LowEnergyAccum;
    }

    [Serializable]
    public class TechSave
    {
        public List<string> Researched = new List<string>();
        public List<string> ProgressIds = new List<string>();
        public List<float> ProgressDays = new List<float>();
        public List<SlotSave> Slots = new List<SlotSave>();
        /// <summary>Только версия 1: старая база науки (35 + бонусы событий).</summary>
        public float BaseMonthlyScience;
        public float FlatScience;
        public float TempBoost = 1f;
        public int TempBoostDays;
    }

    [Serializable]
    public class StarbaseSave
    {
        public int System;
        public float Hull, Armor, Shields;
        public bool Disabled;
    }

    [Serializable]
    public class SiegeSave
    {
        public int System;
        public int Attacker;
        public float Progress;
    }

    [Serializable]
    public class SlotSave
    {
        public string Current;
        public float Days;
        public bool Paused;
        public List<string> Queue = new List<string>();
    }

    [Serializable]
    public class VictorySave
    {
        public int StartYear;
        public int BankruptMonths;
        public bool Started;
        public int ScoreWarnLevel;
    }

    [Serializable]
    public class AISave
    {
        public int Owner = 1;
        public int Capital = -1;
        public string Name, Title;
        public Color Color;
        public int Personality;
        public bool Eliminated;
        public float Energy, Minerals, Alloys, Influence;
        public float BaseEnergy = 15f, BaseMinerals = 10f, BaseAlloys = 5f, BaseInfluence = 3f;
        public bool Bankrupt;
        public float Relations, LastRelationBucket;
        public int WarFleetSerial;

        // Наука
        public List<string> Researched = new List<string>();
        public string CurrentTech;
        public float TechProgress;
        public int ResearchSlots = 3;
        public int ScienceWarnLevel;

        // Дипломатия
        public bool AtWar, Pact, CapitalLost;
        public int TruceDays;
        public float Weariness;
        public int WarMonths, SystemsLost, SystemsTaken, ShipsLost, PlayerShipsLost;
        public int Offer, OfferDays, OfferCooldown, WarCooldown;
        public string OfferReason;
        public List<string> MemoryKeys = new List<string>();
        public List<float> MemoryValues = new List<float>();
        public List<Agreement> Agreements = new List<Agreement>();
        public Deal PendingDeal;
        public int TradeOfferCooldown = 240;

        // Флот
        public int ArmyMode, ArmyTarget = -1, Rally = -1;
    }

    /// <summary>Строка списка сохранений.</summary>
    public class SaveEntry
    {
        public string Slot;
        public string Path;
        public SaveMeta Meta;
        public DateTime SavedAtLocal;
    }

    // ====================================================================== Сохранение / загрузка

    public static class SaveSystem
    {
        /// <summary>2 — экономика от территории, наука в очках, дипломатия и осады.</summary>
        public const int FormatVersion = 2;
        public const string AutosaveSlot = "autosave";
        private const string Ext = ".sav";

        public static event Action OnSavesChanged;

        public static string Folder
        {
            get
            {
                string dir = System.IO.Path.Combine(Application.persistentDataPath, "Saves");
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                return dir;
            }
        }

        public static bool CanSaveNow =>
            UIManager.IsGameStarted && !GameSession.IsLoading
            && (VictoryManager.Instance == null || !VictoryManager.Instance.IsFinished);

        // ------------------------------------------------------------------ Слоты

        public static string SlotFromName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) name = "save";
            var sb = new StringBuilder(name.Length);
            var bad = System.IO.Path.GetInvalidFileNameChars();
            foreach (char c in name.Trim())
                sb.Append(Array.IndexOf(bad, c) >= 0 ? '_' : c);
            string s = sb.ToString();
            return s.Length > 48 ? s.Substring(0, 48) : s;
        }

        public static string PathOf(string slot) => System.IO.Path.Combine(Folder, slot + Ext);

        public static bool Exists(string slot) => File.Exists(PathOf(slot));

        public static List<SaveEntry> List()
        {
            var list = new List<SaveEntry>();
            string[] files;
            try { files = Directory.GetFiles(Folder, "*" + Ext); }
            catch (Exception e) { Debug.LogWarning("[Save] Нет доступа к папке сохранений: " + e.Message); return list; }

            foreach (var path in files)
            {
                try
                {
                    var header = JsonUtility.FromJson<SaveHeader>(File.ReadAllText(path, Encoding.UTF8));
                    if (header?.Meta == null) continue;
                    list.Add(new SaveEntry
                    {
                        Slot = System.IO.Path.GetFileNameWithoutExtension(path),
                        Path = path,
                        Meta = header.Meta,
                        SavedAtLocal = new DateTime(header.Meta.SavedAtUtcTicks, DateTimeKind.Utc).ToLocalTime()
                    });
                }
                catch (Exception e) { Debug.LogWarning($"[Save] Повреждённый файл {path}: {e.Message}"); }
            }
            list.Sort((a, b) => b.Meta.SavedAtUtcTicks.CompareTo(a.Meta.SavedAtUtcTicks));
            return list;
        }

        public static SaveEntry Latest()
        {
            var l = List();
            return l.Count > 0 ? l[0] : null;
        }

        public static SaveFile Read(string slot)
        {
            try
            {
                var file = JsonUtility.FromJson<SaveFile>(File.ReadAllText(PathOf(slot), Encoding.UTF8));
                if (file?.State == null || file.State.Systems == null || file.State.Systems.Count == 0) return null;
                return file;
            }
            catch (Exception e)
            {
                Debug.LogError($"[Save] Не удалось прочитать «{slot}»: {e.Message}");
                return null;
            }
        }

        public static void Delete(string slot)
        {
            try { if (Exists(slot)) File.Delete(PathOf(slot)); }
            catch (Exception e) { Debug.LogWarning("[Save] Удаление: " + e.Message); }
            OnSavesChanged?.Invoke();
        }

        /// <summary>Загрузить слот (перезагрузка сцены).</summary>
        public static bool LoadSlot(string slot)
        {
            var file = Read(slot);
            if (file == null)
            {
                NotificationCenter.Show("Загрузка невозможна", "Файл сохранения повреждён", NotificationCenter.Kind.Danger, 5f);
                return false;
            }
            SFXManager.Play("ui_click", 0.8f, 0.9f);
            GameSession.Load(file);
            return true;
        }

        // ------------------------------------------------------------------ Запись

        public static bool Save(string displayName, bool auto = false, bool notify = true)
        {
            if (!CanSaveNow) return false;
            string slot = auto ? AutosaveSlot : SlotFromName(displayName);
            try
            {
                var file = Capture(displayName, auto);
                string json = JsonUtility.ToJson(file);
                string path = PathOf(slot);
                string tmp = path + ".tmp";
                File.WriteAllText(tmp, json, Encoding.UTF8);
                if (File.Exists(path)) File.Delete(path);
                File.Move(tmp, path);
                OnSavesChanged?.Invoke();
                if (notify)
                    NotificationCenter.Show(auto ? "Автосохранение" : "Игра сохранена", file.Meta.Name,
                                            NotificationCenter.Kind.Success, auto ? 2.5f : 3.5f);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError("[Save] Ошибка записи: " + e);
                NotificationCenter.Show("Ошибка сохранения", e.Message, NotificationCenter.Kind.Danger, 6f);
                return false;
            }
        }

        public static SaveFile Capture(string displayName, bool auto)
        {
            var gen = UnityEngine.Object.FindAnyObjectByType<GalaxyGenerator>();
            var file = new SaveFile();
            var s = file.State;
            var time = TimeManager.Instance;
            var faction = UIManager.Instance != null ? UIManager.Instance.SelectedFaction : null;

            s.Settings = GameSession.Settings.Clone();
            s.Faction = faction?.Name;
            if (time != null) { s.Day = time.Day; s.Month = time.Month; s.Year = time.Year; }

            s.Economy = EconomyManager.Instance != null ? EconomyManager.Instance.CaptureState() : null;
            s.Tech = TechnologyManager.Instance != null ? TechnologyManager.Instance.CaptureState() : null;
            s.Victory = VictoryManager.Instance != null ? VictoryManager.Instance.CaptureState() : null;
            foreach (var ai in AIEmpireManager.All) s.AIs.Add(ai.CaptureState());
            s.AIPairs = AIRelations.Capture();
            if (SiegeManager.Instance != null) s.Sieges = SiegeManager.Instance.CaptureState();
            if (ConstructionManager.Instance != null) s.Jobs = ConstructionManager.Instance.CaptureState();
            if (CombatManager.Instance != null) s.Starbases = CombatManager.Instance.CaptureStarbases();
            if (LeaderManager.Instance != null) s.Leaders = LeaderManager.Instance.CaptureState();
            if (AnomalyEventSystem.Instance != null) s.Events = AnomalyEventSystem.Instance.CaptureState();

            int colonies = 0, pop = 0, owned = 0;
            if (gen != null)
            {
                foreach (var sys in gen.Systems)
                {
                    s.Systems.Add(CaptureSystem(sys));
                    if (sys.OwnerId != 0) continue;
                    owned++;
                    foreach (var p in sys.Planets)
                        if (p.Population > 0) { colonies++; pop += p.Population; }
                }
            }

            var fm = FleetManager.Instance;
            if (fm != null)
            {
                foreach (var f in fm.AllFleets)
                    if (f != null && f.Data != null && !f.Data.Destroyed) s.Fleets.Add(CaptureFleet(f.Data));
                s.FleetIdCounter = fm.NextFleetId;
            }

            if (ShipDesignManager.Instance != null)
                foreach (var d in ShipDesignManager.Instance.SavedDesigns) s.Designs.Add(CaptureDesign(d));

            var cam = UnityEngine.Object.FindAnyObjectByType<StrategyCameraController>();
            if (cam != null && cam.TryGetGalaxyPose(out var pos, out var rot))
            {
                s.HasCamera = true;
                s.CameraPos = pos;
                s.CameraRot = rot;
            }

            int techs = 0;
            if (TechnologyManager.Instance != null)
                foreach (var t in TechnologyManager.Instance.AllTechs) if (t.IsResearched) techs++;

            var m = file.Meta;
            m.Empire = faction?.Name ?? "Империя";
            m.EmpireTitle = faction?.Title ?? "";
            m.EmpireColor = faction != null ? faction.EmpireColor : UIManager.DS.NeonCyan;
            m.GameDate = time != null ? time.GetFormattedDate() : "";
            m.Year = s.Year;
            m.Systems = owned;
            m.Colonies = colonies;
            m.Population = pop;
            m.Techs = techs;
            m.GalaxySize = s.Settings.GalaxySize;
            m.Difficulty = s.Settings.Difficulty;
            m.SavedAtUtcTicks = DateTime.UtcNow.Ticks;
            m.PlayTime = GameSession.PlayTime;
            m.Auto = auto;
            m.Name = auto ? $"Автосохранение · {m.GameDate}"
                   : string.IsNullOrWhiteSpace(displayName) ? $"{m.Empire} · {m.GameDate}" : displayName.Trim();
            return file;
        }

        private static SystemSave CaptureSystem(StarSystem sys)
        {
            var ss = new SystemSave
            {
                Id = sys.Id, Name = sys.Name, Position = sys.Position, Spectral = (int)sys.SpectralClass,
                HasGeneratedPlanets = sys.HasGeneratedPlanets, OwnerId = sys.OwnerId,
                HasStarbase = sys.HasStarbase, IsSurveyed = sys.IsSurveyed,
                AISurvey = sys.AISurveyMask, SurveyVer = 2
            };
            ss.Connected.AddRange(sys.ConnectedSystemIds);
            foreach (var p in sys.Planets)
            {
                var ps = new PlanetSave
                {
                    Name = p.Name, Type = (int)p.Type, OrbitRadius = p.OrbitRadius, OrbitSpeed = p.OrbitSpeed,
                    Size = p.Size, Color = p.PlanetColor, Energy = p.EnergyDeposit, Minerals = p.MineralDeposit,
                    HasMiningStation = p.HasMiningStation, Terraforming = p.TerraformingInProgress,
                    Population = p.Population, PopGrowth = p.PopGrowthProgress, MaxDistricts = p.MaxDistricts
                };
                foreach (var d in p.Districts) ps.Districts.Add((int)d.Type);
                ss.Planets.Add(ps);
            }
            return ss;
        }

        private static FleetSave CaptureFleet(FleetData d)
        {
            var f = new FleetSave
            {
                Id = d.Id, Name = d.Name, Type = (int)d.Type, Owner = d.OwnerId, Current = d.CurrentSystemId,
                Target = d.TargetSystemId, State = (int)d.State,
                DaysTransit = d.DaysRemainingInTransit, TotalTransit = d.TotalDaysForTransit,
                MilitaryPower = d.MilitaryPower,
                BuildTarget = d.BuildTargetSystemId, DaysConstruction = d.DaysRemainingConstruction,
                TotalConstruction = d.TotalConstructionDays,
                SurveyTarget = d.SurveyTargetSystemId, DaysSurvey = d.DaysRemainingSurvey, TotalSurvey = d.TotalSurveyDays,
                Hull = (int)d.HullClass, DesignId = d.DesignId, DesignAlloyCost = d.DesignAlloyCost,
                HP = d.HullPoints, Armor = d.ArmorPoints, Shield = d.ShieldPoints,
                MaxHP = d.MaxHullPoints, MaxArmor = d.MaxArmorPoints, MaxShield = d.MaxShieldPoints,
                Damage = d.Damage, Evasion = d.Evasion, HyperSpeed = d.HyperSpeed, Accuracy = d.Accuracy,
                Upkeep = d.UpkeepEnergy, Weapon = (int)d.PrimaryWeapon
            };
            foreach (var w in d.Weapons)
            {
                f.WType.Add((int)w.Type);
                f.WDamage.Add(w.Damage);
                f.WRate.Add(w.FireRate);
            }
            f.Path.AddRange(d.Path);
            f.Queue.AddRange(d.OrderQueue);
            return f;
        }

        private static DesignSave CaptureDesign(ShipDesign d)
        {
            var ds = new DesignSave { Id = d.Id, Name = d.Name, Hull = (int)d.HullClass };
            ds.Weapons.AddRange(d.WeaponModuleIds);
            ds.Defense.AddRange(d.DefenseModuleIds);
            ds.Utility.AddRange(d.UtilityModuleIds);
            return ds;
        }

        // ------------------------------------------------------------------ Восстановление

        /// <summary>Галактика из сохранения (вызывается бутстрапом вместо генерации).</summary>
        public static List<StarSystem> BuildSystems(GameState s)
        {
            var list = new List<StarSystem>(s.Systems.Count);
            foreach (var ss in s.Systems)
            {
                var sys = new StarSystem(ss.Id, ss.Name, ss.Position, (StarSpectralClass)ss.Spectral)
                {
                    OwnerId = ss.OwnerId, HasStarbase = ss.HasStarbase, IsSurveyed = ss.IsSurveyed,
                    AISurveyMask = ss.SurveyVer >= 2 ? ss.AISurvey : (ss.SurveyVer > 0 ? ss.SurveyedByAI : ss.IsSurveyed) ? ~1 : 0,
                    HasGeneratedPlanets = ss.HasGeneratedPlanets
                };
                sys.ConnectedSystemIds.AddRange(ss.Connected);
                foreach (var ps in ss.Planets)
                {
                    var p = new PlanetData(ps.Name, (PlanetType)ps.Type, ps.OrbitRadius, ps.OrbitSpeed,
                                           ps.Size, ps.Color, ps.Energy, ps.Minerals)
                    {
                        HasMiningStation = ps.HasMiningStation,
                        TerraformingInProgress = ps.Terraforming,
                        Population = ps.Population,
                        PopGrowthProgress = ps.PopGrowth,
                        MaxDistricts = ps.MaxDistricts
                    };
                    foreach (int d in ps.Districts) p.Districts.Add(new DistrictData((DistrictType)d));
                    sys.Planets.Add(p);
                }
                sys.RestoreDerivedTotals();
                list.Add(sys);
            }
            list.Sort((a, b) => a.Id.CompareTo(b.Id));
            return list;
        }

        public static void ApplyFleet(FleetData d, FleetSave f)
        {
            d.Id = f.Id;
            d.Name = f.Name;
            d.OwnerId = f.Owner;
            d.CurrentSystemId = f.Current;
            d.TargetSystemId = f.Target;
            d.State = (FleetState)f.State;
            d.Path.Clear();
            foreach (int p in f.Path) d.Path.Enqueue(p);
            d.OrderQueue.Clear();
            if (f.Queue != null) d.OrderQueue.AddRange(f.Queue);
            d.DaysRemainingInTransit = f.DaysTransit;
            d.TotalDaysForTransit = GamePace.JumpDays;          // темп берётся текущий, а не из старого сохранения
            d.MilitaryPower = f.MilitaryPower;
            d.BuildTargetSystemId = f.BuildTarget;
            d.DaysRemainingConstruction = f.DaysConstruction;
            d.TotalConstructionDays = GamePace.OutpostDays;
            d.SurveyTargetSystemId = f.SurveyTarget;
            d.DaysRemainingSurvey = f.DaysSurvey;
            d.TotalSurveyDays = GamePace.SurveyDays;
            d.HullClass = (ShipClass)f.Hull;
            d.DesignId = f.DesignId;
            d.DesignAlloyCost = f.DesignAlloyCost;
            d.HullPoints = f.HP; d.ArmorPoints = f.Armor; d.ShieldPoints = f.Shield;
            d.MaxHullPoints = f.MaxHP; d.MaxArmorPoints = f.MaxArmor; d.MaxShieldPoints = f.MaxShield;
            d.Evasion = f.Evasion;
            d.Accuracy = f.Accuracy;
            d.HyperSpeed = f.HyperSpeed;
            d.SetWeapons(RestoreWeapons(f, d));
            // Содержание — производное от типа и корпуса (в старых сохранениях ставки были другими)
            d.UpkeepEnergy = FleetData.UpkeepFor(d.Type, d.HullClass);
            d.InCombat = false;
            d.Destroyed = false;
            // Корабль «в пути» без цели — просто стоит на орбите
            if (d.State == FleetState.InHyperlane && d.TargetSystemId < 0) d.State = FleetState.Orbiting;
        }

        /// <summary>
        /// Орудия корабля из сохранения. В старых сохранениях урон и скорострельность всех орудий
        /// хранились суммами — берём орудия из проекта, а если проекта нет (автопроекты ИИ),
        /// делим суммы поровну на слоты оружия корпуса.
        /// </summary>
        private static List<WeaponMount> RestoreWeapons(FleetSave f, FleetData d)
        {
            var list = new List<WeaponMount>();
            if (f.WType != null && f.WType.Count > 0)
            {
                for (int i = 0; i < f.WType.Count && i < f.WDamage.Count && i < f.WRate.Count; i++)
                    list.Add(new WeaponMount((WeaponDamageType)f.WType[i], f.WDamage[i], f.WRate[i]));
                return list;
            }
            if (d.Type != FleetType.Military || f.Damage <= 0f) return list;

            var dm = ShipDesignManager.Instance;
            var design = dm != null ? dm.GetDesign(f.DesignId) : null;
            if (design != null)
            {
                dm.Recalc(design);
                if (design.Mounts.Count > 0)
                {
                    if (f.Accuracy <= 0f) d.Accuracy = design.Accuracy;
                    return design.Mounts;
                }
            }

            var hull = dm != null ? dm.GetHull(d.HullClass) : null;
            int n = Mathf.Max(1, hull != null ? hull.WeaponSlots : 1);
            for (int i = 0; i < n; i++)
                list.Add(new WeaponMount((WeaponDamageType)f.Weapon, f.Damage / n, Mathf.Max(0.1f, f.FireRate / n)));
            return list;
        }

        public static ShipDesign BuildDesign(DesignSave ds)
        {
            var d = new ShipDesign { Id = ds.Id, Name = ds.Name, HullClass = (ShipClass)ds.Hull };
            d.WeaponModuleIds.AddRange(ds.Weapons);
            d.DefenseModuleIds.AddRange(ds.Defense);
            d.UtilityModuleIds.AddRange(ds.Utility);
            return d;
        }

        public static FactionInfo FindFaction(string name)
        {
            foreach (var f in FactionRegistry.AvailableFactions)
                if (f.Name == name) return f;
            return FactionRegistry.AvailableFactions.Length > 0 ? FactionRegistry.AvailableFactions[0] : null;
        }

        // ------------------------------------------------------------------ Форматирование

        public static string FormatPlayTime(float seconds)
        {
            int total = Mathf.Max(0, Mathf.RoundToInt(seconds));
            int h = total / 3600, m = (total / 60) % 60;
            return h > 0 ? $"{h} ч {m:D2} мин" : $"{Mathf.Max(1, m)} мин";
        }

        public static string FormatSavedAt(DateTime local)
        {
            var now = DateTime.Now;
            if (local.Date == now.Date) return "сегодня, " + local.ToString("HH:mm");
            if (local.Date == now.Date.AddDays(-1)) return "вчера, " + local.ToString("HH:mm");
            return local.ToString("dd.MM.yyyy, HH:mm");
        }
    }

    // ====================================================================== Загрузчик партии

    /// <summary>
    /// Создаётся бутстрапом в режиме загрузки. Start с поздним порядком — все менеджеры
    /// уже прошли свой Start, можно переписывать их состояние.
    /// </summary>
    [DefaultExecutionOrder(1000)]
    public class SaveLoader : MonoBehaviour
    {
        private void Start()
        {
            var file = GameSession.PendingLoad;
            if (file == null) { Destroy(gameObject); return; }

            try { Apply(file.State, file.Version); }
            catch (Exception e)
            {
                Debug.LogError("[Save] Ошибка восстановления: " + e);
            }
            GameSession.MarkLoaded();
            Destroy(gameObject);
        }

        private static void Apply(GameState s, int version)
        {
            GameAudio.Suppress(2f);   // восстановление состояния не должно «звучать» (война, захваты, уведомления)
            var time = TimeManager.Instance;
            if (time != null) { time.SetDate(s.Day, s.Month, s.Year); time.SetSpeed(0); }

            // Технологии раньше экономики: от них зависят бонусы и перевод старых сохранений
            if (s.Tech != null) TechnologyManager.Instance?.RestoreState(s.Tech, version);
            if (s.Economy != null) EconomyManager.Instance?.RestoreState(s.Economy, version);
            if (s.Designs != null && s.Designs.Count > 0 && ShipDesignManager.Instance != null)
            {
                var designs = new List<ShipDesign>();
                foreach (var ds in s.Designs) designs.Add(SaveSystem.BuildDesign(ds));
                ShipDesignManager.Instance.RestoreDesigns(designs);
            }

            var fm = FleetManager.Instance;
            if (fm != null)
            {
                foreach (var f in s.Fleets) fm.RestoreFleet(f);
                fm.SetNextFleetId(s.FleetIdCounter);
            }

            if (s.AIs != null && s.AIs.Count > 0)
            {
                foreach (var a in s.AIs) AIEmpireManager.For(a.Owner)?.RestoreState(a, version);
                // Империи, которых не было в той партии, не появляются посреди игры
                foreach (var ai in new List<AIEmpireManager>(AIEmpireManager.All))
                    if (s.AIs.Find(x => x.Owner == ai.OwnerId) == null) ai.RemoveFromGame();
            }
            else if (s.AI != null)
            {
                // Старое сохранение «один на один»: вторую империю ИИ убираем
                AIEmpireManager.For(1)?.RestoreState(s.AI, version);
                foreach (var ai in new List<AIEmpireManager>(AIEmpireManager.All))
                    if (ai.OwnerId != 1) ai.RemoveFromGame();
            }
            AIRelations.Restore(s.AIPairs);
            SiegeManager.Instance?.RestoreState(s.Sieges);
            ConstructionManager.Instance?.RestoreState(s.Jobs);
            CombatManager.Instance?.RestoreStarbases(s.Starbases);
            LeaderManager.Instance?.RestoreState(s.Leaders);
            AnomalyEventSystem.Instance?.RestoreState(s.Events);
            if (s.Victory != null) VictoryManager.Instance?.RestoreState(s.Victory);

            if (s.HasCamera)
            {
                var cam = FindAnyObjectByType<StrategyCameraController>();
                if (cam != null) cam.Teleport(s.CameraPos, s.CameraRot);
            }

            UIManager.Instance?.BeginLoadedGame(SaveSystem.FindFaction(s.Faction));
            EconomyManager.Instance?.RecalculateAll();
            GalaxyView.Instance?.RefreshTerritoryVisuals();
            NotificationCenter.Show("Игра загружена", time != null ? time.GetFormattedDate() : "", NotificationCenter.Kind.Info, 3.5f);
        }
    }

    // ====================================================================== Автосохранение, F5/F9, время партии

    public class SaveRuntime : MonoBehaviour
    {
        private int _monthsSinceAutosave;
        private bool _subscribed;

        private void Start() => TrySubscribe();

        private void TrySubscribe()
        {
            if (_subscribed || TimeManager.Instance == null) return;
            TimeManager.Instance.OnDayPassed += HandleDay;
            _subscribed = true;
        }

        private void OnDestroy()
        {
            if (_subscribed && TimeManager.Instance != null) TimeManager.Instance.OnDayPassed -= HandleDay;
        }

        private void HandleDay(int day, int month, int year)
        {
            if (day != 1 || !SaveSystem.CanSaveNow) return;
            int every = GameSettings.AutosaveMonths;
            if (every <= 0) return;
            _monthsSinceAutosave++;
            if (_monthsSinceAutosave < every) return;
            _monthsSinceAutosave = 0;
            SaveSystem.Save(null, auto: true);
        }

        private void Update()
        {
            TrySubscribe();
            if (!UIManager.IsGameStarted) return;
            GameSession.PlayTime += Time.unscaledDeltaTime;

            if (IsTyping()) return;
            if (Input.GetKeyDown(KeyCode.F5))
            {
                if (SaveSystem.CanSaveNow) SaveSystem.Save("Быстрое сохранение");
            }
            else if (Input.GetKeyDown(KeyCode.F9))
            {
                if (SaveSystem.Exists(SaveSystem.SlotFromName("Быстрое сохранение")))
                    SaveSystem.LoadSlot(SaveSystem.SlotFromName("Быстрое сохранение"));
                else
                    NotificationCenter.Show("Нет быстрого сохранения", "Нажмите F5, чтобы создать", NotificationCenter.Kind.Warning, 3f);
            }
        }

        private static bool IsTyping()
        {
            var es = UnityEngine.EventSystems.EventSystem.current;
            var sel = es != null ? es.currentSelectedGameObject : null;
            return sel != null && sel.GetComponent<UnityEngine.UI.InputField>() != null;
        }
    }
}
