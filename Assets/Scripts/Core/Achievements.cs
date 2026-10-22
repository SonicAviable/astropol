using System;
using System.Collections.Generic;
using UnityEngine;
using StellarisClone.Rendering;
using StellarisClone.Generation;

namespace StellarisClone.Core
{
    public enum Ach
    {
        FirstSurvey, FirstOutpost, FirstColony, FirstTech, FirstContact, FirstLaw, FirstDesign, FirstBattle,
        Colonies5, Systems20, Tech30, TechAll, Starbase, Capture, Peace, Pact, Deal, Spy, Leviathan,
        BankruptRecover, Chronicler, Century,
        WinAny, WinConquest, WinDomination, WinScience, WinEconomic, WinDiplomatic, WinScore, WinHard, WinAllFactions
    }

    /// <summary>
    /// Достижения: описание, хранение и выдача.
    ///   • Хранятся у игрока (PlayerPrefs) — не в сохранении партии: достижение получено навсегда.
    ///   • Копию получает VK Play (через VkPlayStats); если связи нет — достижение ждёт в очереди и уйдёт позже,
    ///     как советует документация VK Play для оффлайн-режима.
    ///   • API-имя (ACH_…) должно совпадать с полем «API Name для эмуляции Steam» в кабинете VK Play.
    /// </summary>
    public static class Achievements
    {
        public sealed class Def
        {
            public Ach Id;
            public string Api, Name, Desc;
            public LGIcon Icon;
            public bool Hidden;      // условие скрыто до получения
        }

        private const string Prefix = "ach.", PendPrefix = "ach.pend.";
        public const string FactionsKey = "ach.winfactions", EventsKey = "ach.events";
        public const int EventsTarget = 25;

        public static event Action<Def> OnUnlocked;

        public static string ApiName(Ach a) => a switch
        {
            Ach.FirstSurvey => "ACH_CARTOGRAPHER",
            Ach.FirstOutpost => "ACH_FIRST_OUTPOST",
            Ach.FirstColony => "ACH_FIRST_COLONY",
            Ach.FirstTech => "ACH_FIRST_TECH",
            Ach.FirstContact => "ACH_FIRST_CONTACT",
            Ach.FirstLaw => "ACH_FIRST_LAW",
            Ach.FirstDesign => "ACH_FIRST_DESIGN",
            Ach.FirstBattle => "ACH_FIRST_BATTLE",
            Ach.Colonies5 => "ACH_COLONIES_5",
            Ach.Systems20 => "ACH_SYSTEMS_20",
            Ach.Tech30 => "ACH_TECH_30",
            Ach.TechAll => "ACH_TECH_ALL",
            Ach.Starbase => "ACH_STARBASE_DOWN",
            Ach.Capture => "ACH_CAPTURE_SYSTEM",
            Ach.Peace => "ACH_PEACE",
            Ach.Pact => "ACH_PACT",
            Ach.Deal => "ACH_DEAL",
            Ach.Spy => "ACH_SPY_OPERATION",
            Ach.Leviathan => "ACH_LEVIATHAN",
            Ach.BankruptRecover => "ACH_BANKRUPT_RECOVER",
            Ach.Chronicler => "ACH_EVENTS_25",
            Ach.Century => "ACH_CENTURY",
            Ach.WinAny => "ACH_WIN_ANY",
            Ach.WinConquest => "ACH_WIN_CONQUEST",
            Ach.WinDomination => "ACH_WIN_DOMINATION",
            Ach.WinScience => "ACH_WIN_SCIENCE",
            Ach.WinEconomic => "ACH_WIN_ECONOMIC",
            Ach.WinDiplomatic => "ACH_WIN_DIPLOMATIC",
            Ach.WinScore => "ACH_WIN_SCORE",
            Ach.WinHard => "ACH_WIN_HARD",
            _ => "ACH_WIN_ALL_FACTIONS"
        };

        /// <summary>Все достижения на текущем языке игры (в порядке показа).</summary>
        public static List<Def> All()
        {
            var l = new List<Def>(32);
            void A(Ach id, LGIcon icon, string name, string desc, bool hidden = false)
                => l.Add(new Def { Id = id, Api = ApiName(id), Icon = icon, Name = name, Desc = desc, Hidden = hidden });

            A(Ach.FirstSurvey, LGIcon.Sensors, Loc.T("Картограф"), Loc.T("Изучите 10 звёздных систем"));
            A(Ach.FirstOutpost, LGIcon.Starbase, Loc.T("Флаг на карте"), Loc.T("Постройте первый форпост"));
            A(Ach.FirstColony, LGIcon.Planet, Loc.T("Новый дом"), Loc.T("Основайте первую колонию"));
            A(Ach.FirstTech, LGIcon.Research, Loc.T("Эврика"), Loc.T("Завершите первое исследование"));
            A(Ach.FirstContact, LGIcon.Diplomacy, Loc.T("Мы не одни"), Loc.T("Встретьте другую цивилизацию"));
            A(Ach.FirstLaw, LGIcon.Influence, Loc.T("Слово правителя"), Loc.T("Примите первый закон империи"));
            A(Ach.FirstDesign, LGIcon.Wrench, Loc.T("Главный конструктор"), Loc.T("Сохраните свой проект корабля"));
            A(Ach.FirstBattle, LGIcon.Swords, Loc.T("Боевое крещение"), Loc.T("Выиграйте первое сражение"));
            A(Ach.Colonies5, LGIcon.Population, Loc.T("Звёздная держава"), Loc.T("Владейте пятью колониями одновременно"));
            A(Ach.Systems20, LGIcon.Border, Loc.T("Границы империи"), Loc.T("Владейте двадцатью системами одновременно"));
            A(Ach.Tech30, LGIcon.Research, Loc.T("Учёный совет"), Loc.T("Изучите 30 технологий за одну партию"));
            A(Ach.TechAll, LGIcon.Reactor, Loc.T("Конец науки"), Loc.T("Изучите все технологии за одну партию"));
            A(Ach.Starbase, LGIcon.Siege, Loc.T("Крушитель бастионов"), Loc.T("Выведите из строя вражескую звёздную базу"));
            A(Ach.Capture, LGIcon.Target, Loc.T("Новые владения"), Loc.T("Захватите систему другой империи"));
            A(Ach.Peace, LGIcon.Peace, Loc.T("Голубь мира"), Loc.T("Заключите мир после войны"));
            A(Ach.Pact, LGIcon.Handshake, Loc.T("Плечом к плечу"), Loc.T("Заключите пакт с другой империей"));
            A(Ach.Deal, LGIcon.Trade, Loc.T("Взаимная выгода"), Loc.T("Заключите торговую сделку"));
            A(Ach.Spy, LGIcon.Target, Loc.T("Тень в коридорах"), Loc.T("Проведите успешную тайную операцию"));
            A(Ach.Leviathan, LGIcon.Weapons, Loc.T("Охотник на левиафанов"), Loc.T("Уничтожьте левиафана"), hidden: true);
            A(Ach.BankruptRecover, LGIcon.Energy, Loc.T("Из долговой ямы"), Loc.T("Выведите империю из банкротства"), hidden: true);
            A(Ach.Chronicler, LGIcon.Info, Loc.T("Летописец"), Loc.F("Переживите {0} событий во всех партиях", EventsTarget));
            A(Ach.Century, LGIcon.Calendar, Loc.T("Век империи"), Loc.T("Доживите до 2300 года в одной партии"));
            A(Ach.WinAny, LGIcon.Trophy, Loc.T("Владыка галактики"), Loc.T("Одержите первую победу"));
            A(Ach.WinConquest, LGIcon.Swords, Loc.T("Железной рукой"), Loc.T("Победите завоеванием"));
            A(Ach.WinDomination, LGIcon.Starbase, Loc.T("Гегемон"), Loc.T("Победите доминированием"));
            A(Ach.WinScience, LGIcon.Research, Loc.T("Вознесение разума"), Loc.T("Одержите научную победу"));
            A(Ach.WinEconomic, LGIcon.Energy, Loc.T("Золотой век"), Loc.T("Одержите экономическую победу"));
            A(Ach.WinDiplomatic, LGIcon.Diplomacy, Loc.T("Голос галактики"), Loc.T("Одержите дипломатическую победу"));
            A(Ach.WinScore, LGIcon.Trophy, Loc.T("Летопись величия"), Loc.T("Победите по очкам к концу эпохи"));
            A(Ach.WinHard, LGIcon.Warning, Loc.T("Против всех шансов"), Loc.T("Победите на сложном уровне"));
            A(Ach.WinAllFactions, LGIcon.Leader, Loc.T("Пять корон"), Loc.T("Победите за каждую из пяти империй"));
            return l;
        }

        public static Def Get(Ach a)
        {
            foreach (var d in All()) if (d.Id == a) return d;
            return null;
        }

        public static bool IsUnlocked(Ach a) => PlayerPrefs.HasKey(Prefix + ApiName(a));

        /// <summary>Когда получено (UTC), если получено.</summary>
        public static DateTime? UnlockedAt(Ach a)
        {
            string k = Prefix + ApiName(a);
            if (!PlayerPrefs.HasKey(k)) return null;
            return DateTimeOffset.FromUnixTimeSeconds(PlayerPrefs.GetInt(k)).UtcDateTime;
        }

        public static int UnlockedCount()
        {
            int n = 0;
            foreach (Ach a in Enum.GetValues(typeof(Ach))) if (IsUnlocked(a)) n++;
            return n;
        }

        public static int Total => Enum.GetValues(typeof(Ach)).Length;

        /// <summary>Выдать достижение (повторный вызов ничего не делает).</summary>
        public static void Unlock(Ach a)
        {
            string api = ApiName(a);
            if (PlayerPrefs.HasKey(Prefix + api)) return;
            PlayerPrefs.SetInt(Prefix + api, (int)DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            PlayerPrefs.SetInt(PendPrefix + api, 1);
            PlayerPrefs.Save();
            var def = Get(a);
            Debug.Log($"[Achievements] {api}");
            OnUnlocked?.Invoke(def);
            Sync();
        }

        /// <summary>Отправить в VK Play всё, что ещё не дошло.</summary>
        public static void Sync()
        {
            if (!VkPlayStats.Ready) return;
            bool any = false;
            foreach (Ach a in Enum.GetValues(typeof(Ach)))
            {
                string api = ApiName(a);
                if (PlayerPrefs.GetInt(PendPrefix + api, 0) != 1) continue;
                if (!VkPlayStats.TryUnlock(api)) continue;
                PlayerPrefs.DeleteKey(PendPrefix + api);
                any = true;
            }
            if (!any) return;
            VkPlayStats.Store();
            PlayerPrefs.Save();
        }

        public static bool HasPending()
        {
            foreach (Ach a in Enum.GetValues(typeof(Ach)))
                if (PlayerPrefs.GetInt(PendPrefix + ApiName(a), 0) == 1) return true;
            return false;
        }

        /// <summary>За какие империи уже побеждали (ключи фракций).</summary>
        public static HashSet<string> WonFactions()
        {
            var set = new HashSet<string>();
            foreach (var k in PlayerPrefs.GetString(FactionsKey, "").Split('|'))
                if (!string.IsNullOrEmpty(k)) set.Add(k);
            return set;
        }

        public static void AddWonFaction(string key)
        {
            if (string.IsNullOrEmpty(key)) return;
            var set = WonFactions();
            if (!set.Add(key)) return;
            PlayerPrefs.SetString(FactionsKey, string.Join("|", set));
            PlayerPrefs.Save();
        }
    }

    /// <summary>
    /// Следит за партией и выдаёт достижения: часть — по событиям (исследование, контакт, захват, победа),
    /// часть — по состоянию империи (раз в полторы секунды: колонии, системы, законы, пакты…).
    /// Показывает уведомление о полученном достижении — по одному, без нагромождения.
    /// </summary>
    public class AchievementTracker : MonoBehaviour
    {
        public static AchievementTracker Instance { get; private set; }

        private float _stateTimer, _syncTimer, _pumpTimer, _toastTimer;
        private readonly Queue<Achievements.Def> _toasts = new Queue<Achievements.Def>();
        private int _wars = -1;
        private int _bankrupt = -1;
        private GalaxyGenerator _gen;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
            VkPlayStats.Init();
        }

        private void OnEnable()
        {
            Achievements.OnUnlocked += QueueToast;
            UIManager.OnGameStarted += HandleGameStarted;
            TechnologyManager.OnTechCompleted += HandleTech;
            Contacts.OnFirstContact += HandleContact;
            SiegeManager.OnSystemCaptured += HandleCaptured;
            CombatManager.OnShipDestroyed += HandleShipDestroyed;
            AnomalyEventSystem.OnEventTriggered += HandleEvent;
            VictoryManager.OnGameFinished += HandleFinished;
        }

        private void OnDisable()
        {
            Achievements.OnUnlocked -= QueueToast;
            UIManager.OnGameStarted -= HandleGameStarted;
            TechnologyManager.OnTechCompleted -= HandleTech;
            Contacts.OnFirstContact -= HandleContact;
            SiegeManager.OnSystemCaptured -= HandleCaptured;
            CombatManager.OnShipDestroyed -= HandleShipDestroyed;
            AnomalyEventSystem.OnEventTriggered -= HandleEvent;
            VictoryManager.OnGameFinished -= HandleFinished;
        }

        private static bool Live => UIManager.IsGameStarted && !GameAudio.Suppressed;

        private void HandleGameStarted()
        {
            _wars = -1;
            _bankrupt = -1;
            _stateTimer = 2f;   // пусть партия прогрузится
        }

        private void HandleTech(Technology t) { if (Live) Achievements.Unlock(Ach.FirstTech); }

        private void HandleContact(int a, int b) { if (Live && (a == 0 || b == 0)) Achievements.Unlock(Ach.FirstContact); }

        private void HandleCaptured(StarSystem sys, int oldOwner, int newOwner)
        {
            if (Live && newOwner == 0 && oldOwner > 0 && !SiegeManager.IsPeacefulTransfer) Achievements.Unlock(Ach.Capture);
        }

        private void HandleShipDestroyed(FleetData ship, int killer)
        {
            if (Live && killer == 0 && ship != null && ship.OwnerId == Threats.LeviathanOwner) Achievements.Unlock(Ach.Leviathan);
        }

        private void HandleEvent(GameEventData ev)
        {
            if (!Live) return;
            int n = PlayerPrefs.GetInt(Achievements.EventsKey, 0) + 1;
            PlayerPrefs.SetInt(Achievements.EventsKey, n);
            if (n >= Achievements.EventsTarget) Achievements.Unlock(Ach.Chronicler);
        }

        private void HandleFinished(VictoryManager.Outcome outcome, string title, string body)
        {
            if (!VictoryManager.IsVictory(outcome)) return;
            Achievements.Unlock(Ach.WinAny);
            switch (outcome)
            {
                case VictoryManager.Outcome.Conquest: Achievements.Unlock(Ach.WinConquest); break;
                case VictoryManager.Outcome.Domination: Achievements.Unlock(Ach.WinDomination); break;
                case VictoryManager.Outcome.Science: Achievements.Unlock(Ach.WinScience); break;
                case VictoryManager.Outcome.Economic: Achievements.Unlock(Ach.WinEconomic); break;
                case VictoryManager.Outcome.Diplomatic: Achievements.Unlock(Ach.WinDiplomatic); break;
                case VictoryManager.Outcome.Score: Achievements.Unlock(Ach.WinScore); break;
            }
            if (GameSession.Settings != null && GameSession.Settings.Difficulty >= 2) Achievements.Unlock(Ach.WinHard);

            Achievements.AddWonFaction(FactionRegistry.KeyOf(UIManager.Instance?.SelectedFaction));
            var won = Achievements.WonFactions();
            int all = 0;
            foreach (var f in FactionRegistry.AvailableFactions) if (won.Contains(FactionRegistry.KeyOf(f))) all++;
            if (all >= FactionRegistry.AvailableFactions.Length) Achievements.Unlock(Ach.WinAllFactions);
        }

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;

            _pumpTimer -= dt;
            if (_pumpTimer <= 0f) { _pumpTimer = 0.25f; VkPlayStats.Pump(); }

            _syncTimer -= dt;
            if (_syncTimer <= 0f)
            {
                _syncTimer = 5f;
                if (Achievements.HasPending()) Achievements.Sync();
            }

            _toastTimer -= dt;
            if (_toastTimer <= 0f && _toasts.Count > 0 && UIManager.IsGameStarted)
            {
                var d = _toasts.Dequeue();
                if (d != null)
                {
                    NotificationCenter.Show(Loc.T("Достижение получено"), $"<b>{d.Name}</b> — {d.Desc}", NotificationCenter.Kind.Success, 7f);
                    _toastTimer = 1.6f;
                }
            }

            if (!Live) return;
            _stateTimer -= dt;
            if (_stateTimer > 0f) return;
            _stateTimer = 1.5f;
            CheckState();
        }

        private void QueueToast(Achievements.Def d) => _toasts.Enqueue(d);

        private void CheckState()
        {
            if (_gen == null) _gen = FindAnyObjectByType<GalaxyGenerator>();
            if (_gen != null)
            {
                int surveyed = 0;
                foreach (var s in _gen.Systems) if (s.IsSurveyedBy(0)) surveyed++;
                if (surveyed >= 10) Achievements.Unlock(Ach.FirstSurvey);
            }

            int systems = EmpireStats.SystemCount(0);
            if (systems >= 2) Achievements.Unlock(Ach.FirstOutpost);
            if (systems >= 20) Achievements.Unlock(Ach.Systems20);

            int colonies = EmpireStats.Colonies(0);
            if (colonies >= 2) Achievements.Unlock(Ach.FirstColony);
            if (colonies >= 5) Achievements.Unlock(Ach.Colonies5);

            var tm = TechnologyManager.Instance;
            if (tm != null)
            {
                int techs = tm.ResearchedCount;
                if (techs >= 30) Achievements.Unlock(Ach.Tech30);
                if (tm.AllTechs != null && tm.AllTechs.Count > 0 && techs >= tm.AllTechs.Count) Achievements.Unlock(Ach.TechAll);
            }

            foreach (var c in EmpireLaws.Categories)
            {
                var law = EmpireLaws.Active(c);
                if (law != null && !law.Default) { Achievements.Unlock(Ach.FirstLaw); break; }
            }

            int wars = 0;
            foreach (var ai in AIEmpireManager.All)
            {
                if (ai == null) continue;
                if (Contacts.PlayerMet(ai.OwnerId)) Achievements.Unlock(Ach.FirstContact);
                if (ai.HasPact) Achievements.Unlock(Ach.Pact);
                if (ai.AtWar) wars++;
            }
            if (_wars >= 0 && wars < _wars) Achievements.Unlock(Ach.Peace);
            _wars = wars;

            var spy = EspionageManager.Instance;
            if (spy != null && spy.Succeeded > 0) Achievements.Unlock(Ach.Spy);

            var eco = EconomyManager.Instance;
            if (eco != null)
            {
                int b = eco.IsBankrupt ? 1 : 0;
                if (_bankrupt == 1 && b == 0) Achievements.Unlock(Ach.BankruptRecover);
                _bankrupt = b;
            }

            var time = TimeManager.Instance;
            if (time != null && time.Year >= 2300) Achievements.Unlock(Ach.Century);
        }
    }
}
