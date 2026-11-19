using System;
using System.Collections.Generic;
using UnityEngine;
using StellarisClone.Rendering;
using StellarisClone.Cards;

namespace StellarisClone.Core
{
    [Serializable]
    public class ObjectivesSave
    {
        public string ActiveId;
        public int Start, Target, Deadline;
        public string RewardRes;
        public int RewardAmount;
        public int NextOfferDay;
        public int Completed, Failed;
        public List<string> Offer = new List<string>();
    }

    /// <summary>
    /// Цели империи на середину и конец партии — продолжение стартовых «Директив». Когда стартовые цели выполнены
    /// (или прошло 10 лет), раз в эпоху империя предлагает три случайные цели под её положение: расширение, колонии,
    /// наука, флот, экономика, захваты, штурмы, договоры. Игрок выбирает одну — на неё даётся 10 лет, за успех награда,
    /// растущая с эпохой. Провал ничего не отнимает, просто следующая цель придёт позже.
    /// Числа целей считаются от текущего состояния империи, поэтому подходят и маленькой, и огромной державе.
    /// </summary>
    public class EmpireObjectives : MonoBehaviour
    {
        public static EmpireObjectives Instance { get; private set; }
        /// <summary>Предложены новые цели — окно выбора показывает их.</summary>
        public static event Action OnOffer;
        public static event Action OnChanged;

        public const int YearDays = 360, DeadlineDays = 10 * YearDays;
        private const int AfterSuccessDays = YearDays, AfterFailDays = YearDays / 2, AfterSkipDays = YearDays;

        public sealed class Template
        {
            public string Id;
            public LGIcon Icon;
            public string Title, Hint;
            /// <summary>Текущее значение показателя.</summary>
            public Func<int> Metric;
            /// <summary>Цель от текущего значения и эпохи (0…5).</summary>
            public Func<int, int, int> MakeTarget;
            /// <summary>Подходит ли цель сейчас.</summary>
            public Func<bool> Fits;
            public string RewardRes;
            /// <summary>Куда вести игрока по щелчку.</summary>
            public Action Show;
        }

        private readonly List<Template> _templates = new List<Template>();
        private ObjectivesSave _s = new ObjectivesSave();
        private int _lastCheckDay = -1;

        public ObjectivesSave Data => _s;
        public bool HasActive => !string.IsNullOrEmpty(_s.ActiveId) && Get(_s.ActiveId) != null;
        public Template Active => HasActive ? Get(_s.ActiveId) : null;
        public bool HasOffer => _s.Offer != null && _s.Offer.Count > 0;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
            BuildTemplates();
        }

        private void OnDestroy() { if (Instance == this) Instance = null; }

        private static int Today => AssaultLink.Today;
        private static int Era()
        {
            var vm = VictoryManager.Instance;
            int years = vm != null ? vm.YearsElapsed : 0;
            return Mathf.Clamp(years / 15, 0, 5);
        }

        public Template Get(string id)
        {
            foreach (var t in _templates) if (t.Id == id) return t;
            return null;
        }

        public int Current => HasActive ? SafeMetric(Active) : 0;
        public int DaysLeft => HasActive ? Mathf.Max(0, _s.Deadline - Today) : 0;

        /// <summary>Заголовок активной цели (или цели из предложения) с её числом.</summary>
        public string TitleOf(Template t, int target) => Loc.F(t.Title, target);

        private static int SafeMetric(Template t)
        {
            try { return Mathf.Max(0, t.Metric()); }
            catch (Exception) { return 0; }
        }

        // ==================== ЦИКЛ ====================

        private void Update()
        {
            if (!UIManager.IsGameStarted) return;
            int today = Today;
            if (today == _lastCheckDay) return;
            _lastCheckDay = today;

            if (HasActive)
            {
                if (SafeMetric(Active) >= _s.Target) Complete();
                else if (today > _s.Deadline) Fail();
                return;
            }
            if (HasOffer || today < _s.NextOfferDay || !Unlocked()) return;
            if (TutorialManager.Instance != null && TutorialManager.Instance.IsActive) return;
            MakeOffer();
        }

        /// <summary>Цели империи начинаются после стартовых директив или через 10 лет партии.</summary>
        private static bool Unlocked()
        {
            var vm = VictoryManager.Instance;
            return StarterGoals.AllDone || (vm != null && vm.YearsElapsed >= 10);
        }

        private void MakeOffer()
        {
            var fit = new List<Template>();
            foreach (var t in _templates)
            {
                bool ok;
                try { ok = t.Fits == null || t.Fits(); } catch (Exception) { ok = false; }
                if (ok) fit.Add(t);
            }
            if (fit.Count == 0) { _s.NextOfferDay = Today + AfterFailDays; return; }
            _s.Offer = new List<string>();
            while (_s.Offer.Count < 3 && fit.Count > 0)
            {
                int i = UnityEngine.Random.Range(0, fit.Count);
                _s.Offer.Add(fit[i].Id);
                fit.RemoveAt(i);
            }
            NotificationCenter.Show(Loc.T("Новая цель империи"), Loc.T("Выберите, к чему империя будет стремиться следующие 10 лет."), NotificationCenter.Kind.Info, 6f);
            OnOffer?.Invoke();
            OnChanged?.Invoke();
        }

        /// <summary>Цель для предложения: число, к которому идти (считается при выборе от текущего состояния).</summary>
        public int PreviewTarget(Template t) => Mathf.Max(SafeMetric(t) + 1, t.MakeTarget(SafeMetric(t), Era()));

        public int RewardAmount(Template t) => Mathf.RoundToInt(BaseReward(t.RewardRes) * (1f + 0.5f * Era()));

        private static float BaseReward(string res) => res == "influence" ? 60f : res == "alloys" ? 90f : 120f;

        /// <summary>Игрок выбрал цель из предложения.</summary>
        public void Choose(string id)
        {
            var t = Get(id);
            if (t == null || !HasOffer || !_s.Offer.Contains(id)) return;
            _s.ActiveId = id;
            _s.Start = SafeMetric(t);
            _s.Target = PreviewTarget(t);
            _s.Deadline = Today + DeadlineDays;
            _s.RewardRes = t.RewardRes;
            _s.RewardAmount = RewardAmount(t);
            _s.Offer.Clear();
            GameChronicle.Instance?.AddEvent(Loc.F("Цель империи: {0}", TitleOf(t, _s.Target)), 0, -1);
            OnChanged?.Invoke();
        }

        /// <summary>Отказаться от предложенных целей: следующие придут через год.</summary>
        public void SkipOffer()
        {
            _s.Offer.Clear();
            _s.NextOfferDay = Today + AfterSkipDays;
            OnChanged?.Invoke();
        }

        /// <summary>Отказаться от текущей цели (без награды, следующая — через год).</summary>
        public void Abandon()
        {
            if (!HasActive) return;
            _s.ActiveId = null;
            _s.NextOfferDay = Today + AfterSkipDays;
            OnChanged?.Invoke();
        }

        private void Complete()
        {
            var t = Active;
            string title = TitleOf(t, _s.Target);
            Give(_s.RewardRes, _s.RewardAmount);
            _s.Completed++;
            _s.ActiveId = null;
            _s.NextOfferDay = Today + AfterSuccessDays;
            SFXManager.Play(Audio.Sfx.DealAccepted, 0.9f);
            NotificationCenter.Show(Loc.F("Цель империи достигнута: {0}", title), Loc.F("Награда: {0}", RewardText(_s.RewardRes, _s.RewardAmount)), NotificationCenter.Kind.Success, 9f);
            GameChronicle.Instance?.AddEvent(Loc.F("Цель достигнута: {0}", title), 1, -1);
            OnChanged?.Invoke();
        }

        private void Fail()
        {
            var t = Active;
            string title = TitleOf(t, _s.Target);
            _s.Failed++;
            _s.ActiveId = null;
            _s.NextOfferDay = Today + AfterFailDays;
            NotificationCenter.Show(Loc.F("Срок цели истёк: {0}", title), Loc.T("Ничего страшного — новая цель появится через полгода."), NotificationCenter.Kind.Warning, 8f);
            GameChronicle.Instance?.AddEvent(Loc.F("Цель не достигнута: {0}", title), 2, -1);
            OnChanged?.Invoke();
        }

        public static string RewardText(string res, int amount)
        {
            switch (res)
            {
                case "influence": return Loc.F("+{0} влияния", amount);
                case "alloys": return Loc.F("+{0} сплавов", amount);
                case "minerals": return Loc.F("+{0} титана", amount);
                default: return Loc.F("+{0} гелия-3", amount);
            }
        }

        public static LGIcon RewardIcon(string res)
            => res == "influence" ? LGIcon.Influence : res == "alloys" ? LGIcon.Alloys : res == "minerals" ? LGIcon.Minerals : LGIcon.Energy;

        private static void Give(string res, int amount)
        {
            var eco = EconomyManager.Instance;
            if (eco == null) return;
            switch (res)
            {
                case "influence": eco.Influence += amount; break;
                case "alloys": eco.Alloys += amount; break;
                case "minerals": eco.Minerals += amount; break;
                default: eco.EnergyCredits += amount; break;
            }
            eco.RaiseResourcesChanged();
        }

        // ==================== ЦЕЛИ ====================

        private void BuildTemplates()
        {
            _templates.Add(new Template
            {
                Id = "expand", Icon = LGIcon.Starbase, Title = Loc.T("Расширьте империю до {0} систем"),
                Hint = Loc.T("Форпосты строительных кораблей и захваты расширяют границы."),
                Metric = () => EmpireStats.SystemCount(0), MakeTarget = (cur, era) => cur + 3 + era,
                RewardRes = "influence", Show = () => SelectFirst(FleetType.Constructor)
            });
            _templates.Add(new Template
            {
                Id = "colonies", Icon = LGIcon.Planet, Title = Loc.T("Владейте {0} колониями"),
                Hint = Loc.T("Ищите пригодные планеты в своих системах и основывайте колонии."),
                Metric = () => EmpireStats.Colonies(0), MakeTarget = (cur, era) => cur + 1 + era / 2,
                RewardRes = "minerals"
            });
            _templates.Add(new Template
            {
                Id = "pop", Icon = LGIcon.Population, Title = Loc.T("Вырастите население до {0}"),
                Hint = Loc.T("Жильё, пища и стабильность ускоряют рост; новые колонии — тоже."),
                Metric = () => EmpireStats.Population(0), MakeTarget = (cur, era) => cur + Mathf.Max(5, cur / 4),
                RewardRes = "energy"
            });
            _templates.Add(new Template
            {
                Id = "tech", Icon = LGIcon.Research, Title = Loc.T("Изучите {0} технологий"),
                Hint = Loc.T("Больше учёных и исследовательских районов — быстрее наука. Технологии — клавиша T."),
                Metric = () => EmpireStats.TechCount(0), MakeTarget = (cur, era) => cur + 4 + era,
                RewardRes = "energy", Show = () => UIManager.Instance?.OpenNavWindow(UIManager.NavWindow.Tech)
            });
            _templates.Add(new Template
            {
                Id = "fleet", Icon = LGIcon.Fleet, Title = Loc.T("Доведите мощь флота до {0}"),
                Hint = Loc.T("Стройте корабли на верфях и улучшайте проекты в конструкторе (N)."),
                Metric = () => Mathf.RoundToInt(EmpireStats.MilitaryPower(0)),
                MakeTarget = (cur, era) => Mathf.CeilToInt(Mathf.Max(cur * 1.5f, cur + 300f) / 50f) * 50,
                RewardRes = "alloys", Show = () => ShipyardWindow.Instance?.Open()
            });
            _templates.Add(new Template
            {
                Id = "gdp", Icon = LGIcon.Trade, Title = Loc.T("Дайте {0}% продукта галактики"),
                Hint = Loc.T("Промышленные и добывающие районы, торговля и сплавы повышают долю в экономике галактики."),
                Metric = () => Mathf.RoundToInt(EmpireStats.GdpShare(0) * 100f),
                MakeTarget = (cur, era) => Mathf.Min(cur + 6, 60),
                Fits = () => EmpireStats.GdpShare(0) < 0.5f, RewardRes = "influence"
            });
            _templates.Add(new Template
            {
                Id = "conquer", Icon = LGIcon.Swords, Title = Loc.T("Захватите {0} систем соперников"),
                Hint = Loc.T("Подавите звёздную базу флотом и удержите орбиту; колонии берут штурмом."),
                Metric = () => GameChronicle.Instance != null ? GameChronicle.Instance.Data.SystemsCaptured : 0,
                MakeTarget = (cur, era) => cur + 1 + era / 2,
                Fits = () => StarterGoals.MetCount() > 0 && EmpireStats.MilitaryPower(0) > 200f, RewardRes = "alloys"
            });
            _templates.Add(new Template
            {
                Id = "assault", Icon = LGIcon.Target, Title = Loc.T("Возьмите штурмом колоний: {0}"),
                Hint = Loc.T("Осадите колонию флотом, держите орбиту и нажмите «ШТУРМ». Армия — в Арсенале (J)."),
                Metric = () => GameChronicle.Instance != null ? GameChronicle.Instance.Data.AssaultsWon : 0,
                MakeTarget = (cur, era) => cur + 1,
                Fits = () => StarterGoals.MetCount() > 0 && ArmyManager.Instance != null && ArmyManager.Instance.AttackPool(0).Count >= 6,
                RewardRes = "alloys", Show = () => ArsenalWindow.Toggle()
            });
            _templates.Add(new Template
            {
                Id = "pact", Icon = LGIcon.Handshake, Title = Loc.T("Заключите договоры с империями: {0}"),
                Hint = Loc.T("Пакт или союз в дипломатии (P). Подарки и общие враги улучшают отношения."),
                Metric = PactCount, MakeTarget = (cur, era) => cur + 1,
                Fits = () => StarterGoals.MetCount() > PactCount(), RewardRes = "influence"
            });
            _templates.Add(new Template
            {
                Id = "army", Icon = LGIcon.Swords, Title = Loc.T("Соберите армию из {0} карт"),
                Hint = Loc.T("Задайте состав в Арсенале (J) и поднимите военный бюджет."),
                Metric = () => ArmyManager.Instance != null ? ArmyManager.Instance.AttackPool(0).Count : 0,
                MakeTarget = (cur, era) => cur + 6 + era * 2,
                Fits = () => ArmyManager.Instance != null, RewardRes = "minerals", Show = () => ArsenalWindow.Toggle()
            });
            _templates.Add(new Template
            {
                Id = "stability", Icon = LGIcon.Peace, Title = Loc.T("Поднимите среднюю стабильность до {0}%"),
                Hint = Loc.T("Законы порядка, жильё, развлечения и решения империи успокаивают колонии."),
                Metric = AvgStability, MakeTarget = (cur, era) => Mathf.Min(cur + 10, 85),
                Fits = () => AvgStability() < 75 && EmpireStats.Colonies(0) >= 2, RewardRes = "influence",
                Show = () => UIManager.Instance?.OpenNavWindow(UIManager.NavWindow.Laws)
            });
        }

        private static int PactCount()
        {
            int n = 0;
            foreach (var ai in AIEmpireManager.All) if (ai != null && !ai.IsEliminated && (ai.HasPact || ai.HasAlliance)) n++;
            return n;
        }

        private static int AvgStability()
        {
            float sum = 0f; int n = 0;
            foreach (var s in EmpireStats.Systems)
            {
                if (s == null || s.OwnerId != 0) continue;
                foreach (var p in s.Planets) if (p.Population > 0) { sum += p.Stability; n++; }
            }
            return n == 0 ? 0 : Mathf.RoundToInt(sum / n);
        }

        private static void SelectFirst(FleetType type)
        {
            var fm = FleetManager.Instance;
            if (fm == null) return;
            foreach (var f in fm.AllFleets)
            {
                var d = f != null ? f.Data : null;
                if (d == null || d.Destroyed || d.OwnerId != 0 || d.Type != type) continue;
                fm.SelectFleet(f);
                return;
            }
        }

        // ==================== СОХРАНЕНИЕ ====================

        public ObjectivesSave CaptureState() => _s;

        public void RestoreState(ObjectivesSave s)
        {
            _s = s ?? new ObjectivesSave();
            if (_s.Offer == null) _s.Offer = new List<string>();
            _lastCheckDay = -1;
            OnChanged?.Invoke();
            if (HasOffer) OnOffer?.Invoke();
        }
    }
}
