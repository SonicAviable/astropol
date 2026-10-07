using System;
using System.Collections.Generic;
using UnityEngine;
using StellarisClone.Rendering;
using Sfx = StellarisClone.Core.Audio.Sfx;

namespace StellarisClone.Core
{
    public enum LeaderClass { Scientist, Admiral, Governor }
    public enum LeaderPost { None, Ship, Council, Planet }

    /// <summary>Лидер империи (учёный, адмирал, губернатор). Сериализуется в сохранение как есть.</summary>
    [Serializable]
    public class Leader
    {
        public int Id;
        public int Owner;
        public LeaderClass Class;
        public string Name;
        public int Level = 1;
        public float Xp;
        public int Age;
        public int Lifespan;
        public List<string> Traits = new List<string>();
        public LeaderPost Post;
        public int ShipId = -1;
        public int SystemId = -1;
        public int PlanetIndex = -1;
        public int Seed;

        public bool Has(string trait) => Traits.Contains(trait);
        public float XpToNext => Level >= LeaderManager.MaxLevel ? 0f : LeaderManager.LevelXp[Level] - Xp;
        public float LevelProgress => Level >= LeaderManager.MaxLevel ? 1f
            : Mathf.InverseLerp(LeaderManager.LevelXp[Level - 1], LeaderManager.LevelXp[Level], Xp);
    }

    [Serializable]
    public class LeaderState
    {
        public int NextId = 1;
        public List<Leader> Leaders = new List<Leader>();
        public List<Leader> Candidates = new List<Leader>();
    }

    /// <summary>
    /// Лидеры в духе Stellaris.
    ///   • Найм за влияние из пула кандидатов (по одному на класс, пул обновляется раз в год и после найма),
    ///     содержание — энергия каждый месяц; не больше <see cref="MaxLeaders"/> на империю.
    ///   • Учёный: на научном корабле ускоряет разведку и увеличивает трофеи; в научном совете — +к науке.
    ///   • Адмирал: на военном корабле — флагман; все свои боевые корабли в той же системе получают
    ///     бонусы к урону, скорострельности (и чертам). Гибнет вместе с флагманом.
    ///   • Губернатор: на колонии — производство планеты и скорость строительства на ней.
    ///   • Опыт: учёный — за разведку, адмирал — за урон и победы, губернатор — за время и стройки.
    ///     Уровни 1–5, на 3-м уровне — вторая черта. Лидеры стареют и однажды уходят из жизни.
    ///   • ИИ нанимает и расставляет своих лидеров сам.
    /// </summary>
    public class LeaderManager : MonoBehaviour
    {
        public static LeaderManager Instance { get; private set; }

        public const int MaxLeaders = 6;
        public const float HireInfluence = 60f;
        public const float UpkeepEnergy = 1.5f;
        public const int MaxLevel = 5;
        public static readonly float[] LevelXp = { 0f, 100f, 250f, 450f, 700f };

        public static event Action OnLeadersChanged;

        private LeaderState _s = new LeaderState();
        public IReadOnlyList<Leader> All => _s.Leaders;
        public IReadOnlyList<Leader> Candidates => _s.Candidates;
        private System.Random _rng = new System.Random(Environment.TickCount);

        // ==================== ЧЕРТЫ ====================

        public class TraitDef
        {
            public string Id, Name, Desc;
            public LeaderClass Class;
            /// <summary>Черта появляется только из событий, не выпадает случайно при найме и повышении.</summary>
            public bool EventOnly;
            public bool Negative;
        }

        public static readonly TraitDef[] TraitDefs =
        {
            new TraitDef { Id = "sci_cartographer", Class = LeaderClass.Scientist, Name = "Картограф", Desc = "+25% к скорости разведки" },
            new TraitDef { Id = "sci_xeno", Class = LeaderClass.Scientist, Name = "Ксенолог", Desc = "+50% к трофеям разведки" },
            new TraitDef { Id = "sci_analyst", Class = LeaderClass.Scientist, Name = "Аналитик", Desc = "+6% к науке в научном совете" },
            new TraitDef { Id = "sci_curious", Class = LeaderClass.Scientist, Name = "Любознательный", Desc = "+30% к получаемому опыту" },

            new TraitDef { Id = "adm_aggressive", Class = LeaderClass.Admiral, Name = "Агрессор", Desc = "+10% к урону соединения" },
            new TraitDef { Id = "adm_tactician", Class = LeaderClass.Admiral, Name = "Тактик", Desc = "+12% к скорострельности соединения" },
            new TraitDef { Id = "adm_cautious", Class = LeaderClass.Admiral, Name = "Осторожный", Desc = "+15% к шансу выйти из боя, +10% к уклонению" },
            new TraitDef { Id = "adm_navigator", Class = LeaderClass.Admiral, Name = "Навигатор", Desc = "+20% к скорости в гиперкоридорах" },

            new TraitDef { Id = "gov_engineer", Class = LeaderClass.Governor, Name = "Инженер", Desc = "+25% к скорости строительства на планете" },
            new TraitDef { Id = "gov_industrialist", Class = LeaderClass.Governor, Name = "Промышленник", Desc = "+15% к сплавам планеты" },
            new TraitDef { Id = "gov_geologist", Class = LeaderClass.Governor, Name = "Геолог", Desc = "+15% к минералам планеты" },
            new TraitDef { Id = "gov_energy", Class = LeaderClass.Governor, Name = "Энергетик", Desc = "+15% к энергии планеты" },
            new TraitDef { Id = "gov_admin", Class = LeaderClass.Governor, Name = "Администратор", Desc = "+6% ко всему производству планеты" },

            // Черты из событий
            new TraitDef { Id = "sci_brilliant", Class = LeaderClass.Scientist, Name = "Гений", Desc = "+10% к скорости разведки, +4% к науке в совете", EventOnly = true },
            new TraitDef { Id = "sci_scarred", Class = LeaderClass.Scientist, Name = "Травмирован", Desc = "−15% к скорости разведки", EventOnly = true, Negative = true },
            new TraitDef { Id = "adm_veteran", Class = LeaderClass.Admiral, Name = "Ветеран", Desc = "+6% к урону и +6% к уклонению соединения", EventOnly = true },
            new TraitDef { Id = "adm_reckless", Class = LeaderClass.Admiral, Name = "Безрассудный", Desc = "+8% к урону, −15% к шансу выйти из боя", EventOnly = true },
            new TraitDef { Id = "gov_honest", Class = LeaderClass.Governor, Name = "Неподкупный", Desc = "+5% ко всему производству планеты", EventOnly = true },
            new TraitDef { Id = "gov_corrupt", Class = LeaderClass.Governor, Name = "Коррупционер", Desc = "−10% ко всему производству планеты", EventOnly = true, Negative = true },
        };

        public static TraitDef Trait(string id)
        {
            foreach (var t in TraitDefs) if (t.Id == id) return t;
            return null;
        }

        public static string ClassName(LeaderClass c) => c switch
        {
            LeaderClass.Scientist => "Учёный",
            LeaderClass.Admiral => "Адмирал",
            _ => "Губернатор"
        };

        /// <summary>Что даёт уровень (без черт) — для подсказок.</summary>
        public static string LevelBonusText(Leader l) => l.Class switch
        {
            LeaderClass.Scientist => $"разведка +{l.Level * 5}%, в совете наука +{l.Level * 2}%",
            LeaderClass.Admiral => $"урон +{l.Level * 3}%, скорострельность +{l.Level * 2}%",
            _ => $"производство +{l.Level * 3}%, стройка +{l.Level * 5}%"
        };

        // ==================== ЖИЗНЕННЫЙ ЦИКЛ ====================

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        private void Start()
        {
            if (TimeManager.Instance != null) TimeManager.Instance.OnDayPassed += HandleDay;
            CombatManager.OnShipDestroyed += HandleShipDestroyed;
            SiegeManager.OnSystemCaptured += HandleCaptured;
            ConstructionManager.OnJobCompleted += HandleJob;
            if (_s.Candidates.Count == 0) RefillCandidates(true);
        }

        private void OnDestroy()
        {
            if (TimeManager.Instance != null) TimeManager.Instance.OnDayPassed -= HandleDay;
            CombatManager.OnShipDestroyed -= HandleShipDestroyed;
            SiegeManager.OnSystemCaptured -= HandleCaptured;
            ConstructionManager.OnJobCompleted -= HandleJob;
        }

        private void Changed()
        {
            _admCacheFrame = -1;
            OnLeadersChanged?.Invoke();
        }

        // ==================== ГЕНЕРАЦИЯ ====================

        private static readonly string[] FirstNames =
        {
            "Аира", "Вейл", "Кассиан", "Лира", "Орин", "Тесса", "Зейн", "Мира", "Дарен", "Ния", "Сол", "Келан",
            "Рейна", "Ивар", "Эла", "Торин", "Нова", "Алрик", "Селена", "Марек", "Иса", "Рон", "Вега", "Лукас"
        };
        private static readonly string[] LastNames =
        {
            "Вейс", "Арден", "Корр", "Сайлен", "Морроу", "Квинт", "Рейес", "Халден", "Новак", "Стерн",
            "Валар", "Окона", "Лин", "Таро", "Ремир", "Ашфорд", "Кейн", "Дракс", "Ильмар", "Сато"
        };

        private Leader Generate(int owner, LeaderClass cls)
        {
            var l = new Leader
            {
                Id = _s.NextId++,
                Owner = owner,
                Class = cls,
                Name = FirstNames[_rng.Next(FirstNames.Length)] + " " + LastNames[_rng.Next(LastNames.Length)],
                Age = 28 + _rng.Next(20),
                Lifespan = 72 + _rng.Next(24),
                Seed = _rng.Next(1, 100000),
            };
            l.Traits.Add(RandomTrait(cls, l));
            return l;
        }

        private string RandomTrait(LeaderClass cls, Leader except)
        {
            var pool = new List<string>();
            foreach (var t in TraitDefs) if (t.Class == cls && !t.EventOnly && (except == null || !except.Has(t.Id))) pool.Add(t.Id);
            return pool.Count == 0 ? null : pool[_rng.Next(pool.Count)];
        }

        /// <summary>Кандидаты игрока: по одному на класс.</summary>
        private void RefillCandidates(bool all)
        {
            if (all) _s.Candidates.Clear();
            foreach (LeaderClass c in Enum.GetValues(typeof(LeaderClass)))
            {
                bool has = false;
                foreach (var k in _s.Candidates) if (k.Class == c) { has = true; break; }
                if (!has) _s.Candidates.Add(Generate(0, c));
            }
            _s.Candidates.Sort((a, b) => a.Class.CompareTo(b.Class));
        }

        // ==================== НАЙМ / УВОЛЬНЕНИЕ / НАЗНАЧЕНИЕ ====================

        public int CountFor(int owner)
        {
            int n = 0;
            foreach (var l in _s.Leaders) if (l.Owner == owner) n++;
            return n;
        }

        public bool CanHire(out string reason)
        {
            reason = null;
            if (CountFor(0) >= MaxLeaders) { reason = $"Не больше {MaxLeaders} лидеров"; return false; }
            var eco = EconomyManager.Instance;
            if (eco == null || eco.Influence < HireInfluence) { reason = $"Нужно {HireInfluence:0} влияния"; return false; }
            return true;
        }

        public bool Hire(Leader candidate)
        {
            if (candidate == null || !_s.Candidates.Contains(candidate)) return false;
            if (!CanHire(out string reason))
            {
                NotificationCenter.Show("Найм невозможен", reason, NotificationCenter.Kind.Warning, 3f);
                return false;
            }
            var eco = EconomyManager.Instance;
            eco.Influence -= HireInfluence;
            eco.RaiseResourcesChanged();
            _s.Candidates.Remove(candidate);
            _s.Leaders.Add(candidate);
            RefillCandidates(false);
            SFXManager.Play(Sfx.UiConfirm);
            NotificationCenter.Show("Лидер нанят", $"{ClassName(candidate.Class)} {candidate.Name} поступил на службу. Назначьте его на пост",
                NotificationCenter.Kind.Success, 4f);
            Changed();
            return true;
        }

        public void Dismiss(Leader l)
        {
            if (l == null || !_s.Leaders.Remove(l)) return;
            if (l.Owner == 0) SFXManager.Play(Sfx.UiBack);
            Changed();
        }

        public void Unassign(Leader l)
        {
            if (l == null) return;
            l.Post = LeaderPost.None;
            l.ShipId = l.SystemId = l.PlanetIndex = -1;
            Changed();
        }

        public void AssignShip(Leader l, FleetData ship)
        {
            if (l == null || ship == null) return;
            var other = LeaderOfShip(ship.Id);
            if (other != null && other != l) Unassign(other);
            l.Post = LeaderPost.Ship;
            l.ShipId = ship.Id;
            l.SystemId = l.PlanetIndex = -1;
            Changed();
        }

        public void AssignCouncil(Leader l)
        {
            if (l == null || l.Class != LeaderClass.Scientist) return;
            var cur = CouncilScientist(l.Owner);
            if (cur != null && cur != l) Unassign(cur);
            l.Post = LeaderPost.Council;
            l.ShipId = l.SystemId = l.PlanetIndex = -1;
            Changed();
        }

        public void AssignPlanet(Leader l, PlanetData p)
        {
            if (l == null || p?.ParentSystem == null) return;
            int idx = p.ParentSystem.Planets.IndexOf(p);
            var other = GovernorOf(p.ParentSystem.Id, idx);
            if (other != null && other != l) Unassign(other);
            l.Post = LeaderPost.Planet;
            l.SystemId = p.ParentSystem.Id;
            l.PlanetIndex = idx;
            l.ShipId = -1;
            Changed();
        }

        // ==================== ПОИСК ====================

        public Leader LeaderOfShip(int shipId)
        {
            foreach (var l in _s.Leaders) if (l.Post == LeaderPost.Ship && l.ShipId == shipId) return l;
            return null;
        }

        public Leader CouncilScientist(int owner)
        {
            foreach (var l in _s.Leaders) if (l.Owner == owner && l.Post == LeaderPost.Council) return l;
            return null;
        }

        public Leader GovernorOf(int systemId, int planetIndex)
        {
            foreach (var l in _s.Leaders)
                if (l.Post == LeaderPost.Planet && l.SystemId == systemId && l.PlanetIndex == planetIndex) return l;
            return null;
        }

        public Leader GovernorOf(PlanetData p)
            => p?.ParentSystem == null ? null : GovernorOf(p.ParentSystem.Id, p.ParentSystem.Planets.IndexOf(p));

        private static FleetData FindShip(int id)
        {
            var fm = FleetManager.Instance;
            if (fm == null) return null;
            foreach (var f in fm.AllFleets)
                if (f?.Data != null && f.Data.Id == id && !f.Data.Destroyed) return f.Data;
            return null;
        }

        // Кэш «владелец+система → адмирал» на кадр: в бою запрос идёт на каждый выстрел
        private readonly Dictionary<long, Leader> _admCache = new Dictionary<long, Leader>();
        private int _admCacheFrame = -1;

        /// <summary>Лучший адмирал владельца, чей флагман стоит в этой системе (командует всем соединением).</summary>
        public Leader AdmiralAt(int owner, int systemId)
        {
            if (_admCacheFrame != Time.frameCount)
            {
                _admCacheFrame = Time.frameCount;
                _admCache.Clear();
                foreach (var l in _s.Leaders)
                {
                    if (l.Class != LeaderClass.Admiral || l.Post != LeaderPost.Ship) continue;
                    var ship = FindShip(l.ShipId);
                    if (ship == null) continue;
                    long key = ((long)l.Owner << 32) | (uint)ship.CurrentSystemId;
                    if (!_admCache.TryGetValue(key, out var cur) || l.Level > cur.Level) _admCache[key] = l;
                }
            }
            return _admCache.TryGetValue(((long)owner << 32) | (uint)systemId, out var res) ? res : null;
        }

        // ==================== ЭФФЕКТЫ ====================

        public static float SurveySpeedMult(FleetData d)
        {
            var l = Instance?.LeaderOfShip(d.Id);
            if (l == null || l.Class != LeaderClass.Scientist) return 1f;
            return 1f + l.Level * 0.05f + (l.Has("sci_cartographer") ? 0.25f : 0f)
                      + (l.Has("sci_brilliant") ? 0.10f : 0f) - (l.Has("sci_scarred") ? 0.15f : 0f);
        }

        public static float SurveyRewardMult(int shipId)
        {
            var l = Instance?.LeaderOfShip(shipId);
            if (l == null || l.Class != LeaderClass.Scientist) return 1f;
            return 1f + l.Level * 0.05f + (l.Has("sci_xeno") ? 0.5f : 0f);
        }

        public static float ResearchMult(int owner)
        {
            var l = Instance?.CouncilScientist(owner);
            if (l == null) return 1f;
            return 1f + l.Level * 0.02f + (l.Has("sci_analyst") ? 0.06f : 0f) + (l.Has("sci_brilliant") ? 0.04f : 0f);
        }

        public static float AdmiralDamageMult(int owner, int systemId)
        {
            var l = Instance?.AdmiralAt(owner, systemId);
            if (l == null) return 1f;
            return 1f + l.Level * 0.03f + (l.Has("adm_aggressive") ? 0.10f : 0f)
                      + (l.Has("adm_veteran") ? 0.06f : 0f) + (l.Has("adm_reckless") ? 0.08f : 0f);
        }

        public static float AdmiralFireRateMult(int owner, int systemId)
        {
            var l = Instance?.AdmiralAt(owner, systemId);
            if (l == null) return 1f;
            return 1f + l.Level * 0.02f + (l.Has("adm_tactician") ? 0.12f : 0f);
        }

        public static float AdmiralEvasionMult(int owner, int systemId)
        {
            var l = Instance?.AdmiralAt(owner, systemId);
            if (l == null) return 1f;
            return (l.Has("adm_cautious") ? 1.10f : 1f) * (l.Has("adm_veteran") ? 1.06f : 1f);
        }

        public static float AdmiralDisengageMult(int owner, int systemId)
        {
            var l = Instance?.AdmiralAt(owner, systemId);
            if (l == null) return 1f;
            return (l.Has("adm_cautious") ? 1.15f : 1f) * (l.Has("adm_reckless") ? 0.85f : 1f);
        }

        public static float HyperSpeedMult(FleetData d)
        {
            if (d.Type != FleetType.Military || Instance == null) return 1f;
            var l = Instance.AdmiralAt(d.OwnerId, d.CurrentSystemId);
            return l != null && l.Has("adm_navigator") ? 1.2f : 1f;
        }

        /// <summary>Множитель производства планеты по ресурсу ("energy" / "minerals" / "alloys").</summary>
        public static float PlanetOutputMult(PlanetData p, string resource)
        {
            var l = Instance?.GovernorOf(p);
            if (l == null) return 1f;
            float m = 1f + l.Level * 0.03f + (l.Has("gov_admin") ? 0.06f : 0f)
                         + (l.Has("gov_honest") ? 0.05f : 0f) - (l.Has("gov_corrupt") ? 0.10f : 0f);
            if (resource == "energy" && l.Has("gov_energy")) m += 0.15f;
            if (resource == "minerals" && l.Has("gov_geologist")) m += 0.15f;
            if (resource == "alloys" && l.Has("gov_industrialist")) m += 0.15f;
            return m;
        }

        public static float BuildSpeedMult(int systemId, int planetIndex)
        {
            var l = Instance?.GovernorOf(systemId, planetIndex);
            if (l == null) return 1f;
            return 1f + l.Level * 0.05f + (l.Has("gov_engineer") ? 0.25f : 0f);
        }

        // ==================== ОПЫТ ====================

        private void GainXp(Leader l, float xp)
        {
            if (l == null || l.Level >= MaxLevel) return;
            if (l.Has("sci_curious")) xp *= 1.3f;
            l.Xp += xp;
            while (l.Level < MaxLevel && l.Xp >= LevelXp[l.Level])
            {
                l.Level++;
                if (l.Level == 3)
                {
                    string t = RandomTrait(l.Class, l);
                    if (t != null) l.Traits.Add(t);
                }
                if (l.Owner == 0)
                {
                    string extra = l.Level == 3 && l.Traits.Count > 1 ? $" Новая черта: {Trait(l.Traits[l.Traits.Count - 1])?.Name}" : "";
                    NotificationCenter.Show("Лидер стал опытнее", $"{ClassName(l.Class)} {l.Name} — уровень {l.Level}.{extra}",
                        NotificationCenter.Kind.Success, 4f);
                }
                Changed();
            }
        }

        // ==================== ДЛЯ СОБЫТИЙ ====================

        public IEnumerable<Leader> Of(int owner)
        {
            foreach (var l in _s.Leaders) if (l.Owner == owner) yield return l;
        }

        public void GrantXp(Leader l, float xp) => GainXp(l, xp);

        /// <summary>Дать черту (если её ещё нет). Отрицательная черта вытесняет свою «противоположность».</summary>
        public bool GiveTrait(Leader l, string traitId)
        {
            var def = Trait(traitId);
            if (l == null || def == null || l.Has(traitId)) return false;
            if (traitId == "gov_corrupt") l.Traits.Remove("gov_honest");
            if (traitId == "gov_honest") l.Traits.Remove("gov_corrupt");
            l.Traits.Add(traitId);
            if (l.Owner == 0)
                NotificationCenter.Show(def.Negative ? "Лидер получил изъян" : "Лидер получил черту",
                    $"{ClassName(l.Class)} {l.Name}: «{def.Name}» — {def.Desc}",
                    def.Negative ? NotificationCenter.Kind.Warning : NotificationCenter.Kind.Success, 5f);
            Changed();
            return true;
        }

        public void RemoveTrait(Leader l, string traitId)
        {
            if (l != null && l.Traits.Remove(traitId)) Changed();
        }

        /// <summary>Лидер покидает службу (уход в отставку, гибель в событии).</summary>
        public void Retire(Leader l, string title, string message)
        {
            if (l == null || !_s.Leaders.Remove(l)) return;
            if (l.Owner == 0) NotificationCenter.Show(title, message, NotificationCenter.Kind.Info, 6f);
            Changed();
        }

        /// <summary>Лидер поступает на службу без оплаты (перебежчик, спасённый). null — нет мест.</summary>
        public Leader RecruitFree(int owner, LeaderClass cls, int level, string traitId)
        {
            if (owner == 0 && CountFor(0) >= MaxLeaders) return null;
            var l = Generate(owner, cls);
            level = Mathf.Clamp(level, 1, MaxLevel);
            l.Level = level;
            l.Xp = LevelXp[level - 1];
            if (!string.IsNullOrEmpty(traitId) && Trait(traitId) != null && !l.Has(traitId))
            {
                if (Trait(traitId).Class == cls && l.Traits.Count > 0) l.Traits[0] = traitId; else l.Traits.Add(traitId);
            }
            if (level >= 3 && l.Traits.Count < 2)
            {
                string t = RandomTrait(cls, l);
                if (t != null) l.Traits.Add(t);
            }
            _s.Leaders.Add(l);
            Changed();
            return l;
        }

        public void OnSurveyDay(FleetData ship) => GainXp(LeaderOfShip(ship.Id), 1.5f);
        public void OnSurveyComplete(FleetData ship) => GainXp(LeaderOfShip(ship.Id), 25f);

        public void OnDamageDealt(int owner, int systemId, float dealt)
        {
            var l = AdmiralAt(owner, systemId);
            if (l != null) GainXp(l, dealt / 15f);
        }

        public void OnKill(int owner, int systemId) => GainXp(AdmiralAt(owner, systemId), 20f);

        private void HandleJob(ConstructionJob job)
        {
            if (job == null || !job.IsPlanetJob) return;
            GainXp(GovernorOf(job.SystemId, job.PlanetIndex), 12f);
        }

        // ==================== СОБЫТИЯ ====================

        private void HandleShipDestroyed(FleetData ship, int killer)
        {
            var l = LeaderOfShip(ship.Id);
            if (l == null) return;
            _s.Leaders.Remove(l);
            if (l.Owner == 0)
                NotificationCenter.Show("Лидер погиб", $"{ClassName(l.Class)} {l.Name} погиб вместе с кораблём {ship.Name}",
                    NotificationCenter.Kind.Danger, 6f);
            Changed();
        }

        private void HandleCaptured(StarSystem sys, int oldOwner, int newOwner)
        {
            bool any = false;
            foreach (var l in _s.Leaders)
                if (l.Post == LeaderPost.Planet && l.SystemId == sys.Id && l.Owner != newOwner)
                {
                    l.Post = LeaderPost.None; l.SystemId = l.PlanetIndex = -1; any = true;
                    if (l.Owner == 0)
                        NotificationCenter.Show("Губернатор эвакуирован", $"{l.Name} покинул {sys.Name} и ждёт нового назначения",
                            NotificationCenter.Kind.Warning, 5f);
                }
            if (any) Changed();
        }

        private void HandleDay(int day, int month, int year)
        {
            if (!UIManager.IsGameStarted) return;
            bool changed = false;

            // Ежедневный опыт за службу и проверка постов
            for (int i = _s.Leaders.Count - 1; i >= 0; i--)
            {
                var l = _s.Leaders[i];
                switch (l.Post)
                {
                    case LeaderPost.Planet:
                        var sys = EmpireStats.GetSystem(l.SystemId);
                        if (sys == null || sys.OwnerId != l.Owner || l.PlanetIndex < 0 || l.PlanetIndex >= sys.Planets.Count
                            || sys.Planets[l.PlanetIndex].Population <= 0)
                        { l.Post = LeaderPost.None; l.SystemId = l.PlanetIndex = -1; changed = true; }
                        else GainXp(l, 0.15f);
                        break;
                    case LeaderPost.Council:
                        GainXp(l, 0.1f);
                        break;
                    case LeaderPost.Ship:
                        if (FindShip(l.ShipId) == null) { l.Post = LeaderPost.None; l.ShipId = -1; changed = true; }
                        else if (l.Class == LeaderClass.Admiral) GainXp(l, 0.05f);
                        break;
                }
            }

            if (day == 1)
            {
                // Содержание
                int player = CountFor(0);
                var eco = EconomyManager.Instance;
                if (eco != null && player > 0) { eco.EnergyCredits -= player * UpkeepEnergy; eco.RaiseResourcesChanged(); }
                foreach (var rival in AIEmpireManager.Alive)
                {
                    int n = CountFor(rival.OwnerId);
                    if (n > 0) rival.SpendStock("energy", n * UpkeepEnergy);
                    AIThink(rival);
                }

                if (month == 1)
                {
                    // Старение; пул кандидатов обновляется
                    for (int i = _s.Leaders.Count - 1; i >= 0; i--)
                    {
                        var l = _s.Leaders[i];
                        l.Age++;
                        if (l.Age < l.Lifespan) continue;
                        _s.Leaders.RemoveAt(i);
                        changed = true;
                        if (l.Owner == 0)
                            NotificationCenter.Show("Лидер ушёл из жизни", $"{ClassName(l.Class)} {l.Name} скончался в возрасте {l.Age} лет",
                                NotificationCenter.Kind.Info, 6f);
                    }
                    RefillCandidates(true);
                    changed = true;
                }
            }
            if (changed) Changed();
        }

        // ==================== ИИ ====================

        /// <summary>ИИ раз в месяц нанимает (когда хватает влияния) и расставляет лидеров по лучшим постам.</summary>
        private void AIThink(AIEmpireManager ai)
        {
            if (ai == null) return;
            int owner = ai.OwnerId;

            if (CountFor(owner) < 4 && ai.GetStock("influence") >= HireInfluence + 40f)
            {
                int sci = 0, adm = 0, gov = 0;
                foreach (var l in _s.Leaders)
                    if (l.Owner == owner) { if (l.Class == LeaderClass.Scientist) sci++; else if (l.Class == LeaderClass.Admiral) adm++; else gov++; }
                LeaderClass want = adm == 0 && ai.AtWarWithAnyone ? LeaderClass.Admiral
                                 : sci == 0 ? LeaderClass.Scientist
                                 : adm == 0 ? LeaderClass.Admiral
                                 : gov == 0 ? LeaderClass.Governor
                                 : (LeaderClass)_rng.Next(3);
                ai.SpendStock("influence", HireInfluence);
                _s.Leaders.Add(Generate(owner, want));
            }

            var fm = FleetManager.Instance;
            foreach (var l in _s.Leaders)
            {
                if (l.Owner != owner || l.Post != LeaderPost.None) continue;
                switch (l.Class)
                {
                    case LeaderClass.Scientist:
                    {
                        FleetData free = null;
                        if (fm != null)
                            foreach (var f in fm.AllFleets)
                                if (f?.Data != null && !f.Data.Destroyed && f.Data.OwnerId == owner && f.Data.Type == FleetType.Science
                                    && LeaderOfShip(f.Data.Id) == null) { free = f.Data; break; }
                        if (free != null) { l.Post = LeaderPost.Ship; l.ShipId = free.Id; }
                        else if (CouncilScientist(owner) == null) l.Post = LeaderPost.Council;
                        break;
                    }
                    case LeaderClass.Admiral:
                    {
                        FleetData best = null;
                        if (fm != null)
                            foreach (var f in fm.AllFleets)
                                if (f?.Data != null && !f.Data.Destroyed && f.Data.OwnerId == owner && f.Data.Type == FleetType.Military
                                    && LeaderOfShip(f.Data.Id) == null && (best == null || CombatMath.Power(f.Data) > CombatMath.Power(best)))
                                    best = f.Data;
                        if (best != null) { l.Post = LeaderPost.Ship; l.ShipId = best.Id; }
                        break;
                    }
                    default:
                    {
                        PlanetData best = null;
                        foreach (var s in EmpireStats.Systems)
                        {
                            if (s.OwnerId != owner) continue;
                            foreach (var p in s.Planets)
                                if (p.Population > 0 && GovernorOf(p) == null && (best == null || p.Population > best.Population)) best = p;
                        }
                        if (best != null) { l.Post = LeaderPost.Planet; l.SystemId = best.ParentSystem.Id; l.PlanetIndex = best.ParentSystem.Planets.IndexOf(best); }
                        break;
                    }
                }
            }
        }

        // ==================== СОХРАНЕНИЕ ====================

        public LeaderState CaptureState() => _s;

        public void RestoreState(LeaderState s)
        {
            _s = s ?? new LeaderState();
            _s.Leaders ??= new List<Leader>();
            _s.Candidates ??= new List<Leader>();
            foreach (var l in _s.Leaders) l.Traits ??= new List<string>();
            foreach (var l in _s.Candidates) l.Traits ??= new List<string>();
            if (_s.Candidates.Count == 0) RefillCandidates(true);
            Changed();
        }
    }
}
