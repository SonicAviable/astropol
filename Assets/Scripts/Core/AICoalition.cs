using System.Collections.Generic;
using UnityEngine;
using StellarisClone.Rendering;

namespace StellarisClone.Core
{
    [System.Serializable]
    public class CoalitionSave
    {
        public int Target = -1;
        public bool PlayerMember, PlayerOrganized, Announced;
        public int Months;
        public List<int> Members = new List<int>();
    }

    /// <summary>
    /// Коалиция против одной империи («цели»). Бывает двух видов:
    ///   • Автоматическая — если империя (игрок или ИИ) уходит далеко вперёд по очкам, соперники, которым она
    ///     не нравится, объединяются против неё. Если цель — ИИ, игрок может вступить в коалицию.
    ///   • Созданная игроком — игрок собирает коалицию против любого соперника из тех, кто готов к ней присоединиться.
    /// Члены коалиции перестают воевать между собой, получают общую неприязнь к цели и, накопив силы,
    /// вместе объявляют ей войну. Игрок сам решает, воевать ли ему: но выйти из коалиции или заключить
    /// сепаратный мир, оставив союзников воевать, — значит испортить с ними отношения.
    /// Автоматическая коалиция не сохраняется: пересчитывается каждый месяц по очкам и отношениям.
    /// На лёгкой сложности коалиций нет.
    /// </summary>
    public static class AICoalition
    {
        /// <summary>Лидер должен опережать второго по очкам хотя бы во столько раз…</summary>
        public const float LeadToForm = 1.3f, LeadToHold = 1.1f;
        /// <summary>…и держать такую долю суммарных очков всех империй.</summary>
        public const float ShareToForm = 0.40f, ShareToHold = 0.34f;
        public const int MinLeaderSystems = 7;
        public const int FirstYear = 2206;
        /// <summary>Порог вступления отсчитывается от порога войны характера, выхода — с запасом, чтобы не метались.</summary>
        public const float JoinMargin = 35f, LeaveMargin = 25f;
        /// <summary>Коалиция, которую собрал игрок, привлекает чуть охотнее: ей не нужна «великая угроза».</summary>
        public const float OrganizerBonus = 10f;
        /// <summary>Коалиция нападает, когда её совокупный флот не слабее такой доли флота цели.</summary>
        public const float WarStrengthRatio = 0.75f;
        /// <summary>Месяцев между созданием коалиции и первой войной — у цели есть время среагировать.</summary>
        public const int PrepMonths = 3;
        /// <summary>Созданная игроком коалиция расходится сама, если все давно помирились.</summary>
        public const int IdleMonthsToExpire = 36;

        public const float JoinCost = 20f, OrganizeCost = 40f;

        /// <summary>Против кого направлена коалиция (0 — игрок), −1 — коалиции нет.</summary>
        public static int TargetOwner { get; private set; } = -1;
        public static bool PlayerMember { get; private set; }
        public static bool PlayerOrganized { get; private set; }

        private static readonly HashSet<int> _members = new HashSet<int>();
        private static int _months;
        private static bool _announced;

        /// <summary>Участников вместе с игроком, если он в коалиции.</summary>
        public static int MemberCount => _members.Count + (PlayerMember ? 1 : 0);
        public static bool IsActive => TargetOwner >= 0 && MemberCount >= 2;
        public static bool IsMember(int owner) => IsActive && (owner == 0 ? PlayerMember : _members.Contains(owner));
        public static bool IsTarget(int owner) => IsActive && owner == TargetOwner;
        public static bool AreAllies(int a, int b) => a != b && IsMember(a) && IsMember(b);

        /// <summary>«вас» или «империи Имя» — для фразы «коалиция против …».</summary>
        public static string TargetPhrase => TargetOwner == 0 ? "вас" : "империи " + AIEmpireManager.NameOf(TargetOwner);

        public static void Reset()
        {
            TargetOwner = -1;
            PlayerMember = PlayerOrganized = false;
            _members.Clear();
            _months = 0;
            _announced = false;
        }

        // ==================== СОХРАНЕНИЕ ====================

        public static CoalitionSave Capture() => new CoalitionSave
        {
            Target = TargetOwner, PlayerMember = PlayerMember, PlayerOrganized = PlayerOrganized,
            Announced = _announced, Months = _months, Members = new List<int>(_members)
        };

        public static void Restore(CoalitionSave s)
        {
            Reset();
            if (s == null || s.Target < 0) return;
            TargetOwner = s.Target;
            PlayerMember = s.PlayerMember;
            PlayerOrganized = s.PlayerOrganized;
            _announced = s.Announced;
            _months = s.Months;
            if (s.Members != null) foreach (int m in s.Members) _members.Add(m);
        }

        // ==================== ЕЖЕМЕСЯЧНО ====================

        public static void MonthlyUpdate()
        {
            bool wasActive = IsActive;
            string oldPhrase = TargetPhrase;
            bool oldTargetWasPlayer = TargetOwner == 0;

            if (GameSession.Settings.Difficulty < 1) { Reset(); AnnounceEnd(wasActive, oldPhrase, oldTargetWasPlayer); return; }

            var owners = new List<int> { 0 };
            foreach (var ai in AIEmpireManager.Alive) owners.Add(ai.OwnerId);
            if (owners.Count < 3) { Reset(); AnnounceEnd(wasActive, oldPhrase, oldTargetWasPlayer); return; }

            if (!PlayerOrganized) UpdateAutomatic(owners);
            else if (AIEmpireManager.For(TargetOwner) == null || AIEmpireManager.For(TargetOwner).IsEliminated) Reset();

            if (TargetOwner >= 0)
            {
                _months++;
                UpdateMembers();
                if (PlayerOrganized && ShouldExpire()) Reset();
            }
            if (TargetOwner >= 0)
            {
                if (IsActive && !_announced) Announce();
                if (IsActive && _months >= PrepMonths) Act();
            }

            AnnounceEnd(wasActive, oldPhrase, oldTargetWasPlayer);
        }

        /// <summary>Лидер по очкам, ушедший далеко вперёд, становится целью автоматической коалиции.</summary>
        private static void UpdateAutomatic(List<int> owners)
        {
            int leader = -1;
            float best = -1f, second = -1f, total = 0f;
            foreach (int o in owners)
            {
                float sc = EmpireStats.Score(o).Total;
                total += sc;
                if (sc > best) { second = best; best = sc; leader = o; }
                else if (sc > second) second = sc;
            }
            float share = total > 0f ? best / total : 0f;
            int year = TimeManager.Instance != null ? TimeManager.Instance.Year : 0;

            if (TargetOwner >= 0)
            {
                bool holds = leader == TargetOwner && best >= second * LeadToHold && share >= ShareToHold;
                if (!holds) Reset();
            }
            if (TargetOwner < 0 && year >= FirstYear && EmpireStats.SystemCount(leader) >= MinLeaderSystems
                && best >= second * LeadToForm && share >= ShareToForm)
            {
                TargetOwner = leader;
                _members.Clear();
                _months = 0;
                _announced = false;
            }
        }

        private static bool ShouldExpire()
        {
            if (_months > 1 && _members.Count == 0) return true;
            if (_months < IdleMonthsToExpire) return false;
            foreach (int m in _members) if (Diplomacy.AtWar(m, TargetOwner)) return false;
            return !Diplomacy.AtWar(0, TargetOwner);
        }

        // ==================== УЧАСТНИКИ ====================

        private static float OpinionToTarget(AIEmpireManager ai)
        {
            if (TargetOwner == 0) return ai.Opinion;
            var t = AIEmpireManager.For(TargetOwner);
            return t != null ? AIRelations.Opinion(ai, t) : 0f;
        }

        private static float JoinThreshold(AIEmpireManager ai)
            => ai.Profile.WarOpinionThreshold + JoinMargin + (PlayerOrganized ? OrganizerBonus : 0f);

        /// <summary>Хочет ли империя ИИ вступить: цель ей неприятна, договор с целью не связывает, с игроком-участником не воюет.</summary>
        private static bool WantsIn(AIEmpireManager ai)
        {
            if (ai.IsEliminated || ai.OwnerId == TargetOwner) return false;
            if (TargetOwner == 0 && ai.HasPact) return false;
            if (PlayerMember && ai.AtWar) return false;
            return OpinionToTarget(ai) <= JoinThreshold(ai) && (!PlayerOrganized || ai.Opinion > -25f);
        }

        private static void UpdateMembers()
        {
            foreach (var ai in AIEmpireManager.Alive)
            {
                int id = ai.OwnerId;
                if (id == TargetOwner) continue;
                if (_members.Contains(id))
                {
                    bool pact = TargetOwner == 0 && ai.HasPact;
                    bool refuses = PlayerMember && ai.AtWar;
                    if (pact || refuses || OpinionToTarget(ai) > JoinThreshold(ai) + LeaveMargin) _members.Remove(id);
                }
                else if (WantsIn(ai)) _members.Add(id);
            }
        }

        // ==================== ОБЪЯВЛЕНИЯ И ВОЙНА ====================

        private static void Announce()
        {
            _announced = true;
            var names = new List<string>();
            foreach (int m in _members) names.Add(AIEmpireManager.NameOf(m));
            if (PlayerMember) names.Add("вы");
            // Игра загружена посреди войны коалиции (все уже воюют с целью) — не объявляем её заново
            bool allAtWar = true;
            foreach (int m in _members) if (!Diplomacy.AtWar(m, TargetOwner)) { allAtWar = false; break; }
            if (allAtWar) return;

            if (TargetOwner == 0)
                NotificationCenter.Show("КОАЛИЦИЯ ПРОТИВ ВАС",
                    $"{string.Join(", ", names)} объединились: вы стали слишком сильны. Подарки и пакты удержат часть соседей от вступления",
                    NotificationCenter.Kind.Danger, 12f);
            else if (!PlayerOrganized)
                NotificationCenter.Show("Коалиция в галактике",
                    $"{string.Join(", ", names)} объединяются против империи {AIEmpireManager.NameOf(TargetOwner)}. Вы можете вступить в неё в окне дипломатии",
                    NotificationCenter.Kind.Info, 10f);
            AIEmpireManager.NotifyDiplomacyChanged();
        }

        private static void AnnounceEnd(bool wasActive, string phrase, bool targetWasPlayer)
        {
            if (!wasActive || IsActive) return;
            _announced = false;
            NotificationCenter.Show("Коалиция распалась", $"Союз против {phrase} больше не действует",
                targetWasPlayer ? NotificationCenter.Kind.Success : NotificationCenter.Kind.Info, 8f);
            AIEmpireManager.NotifyDiplomacyChanged();
        }

        private static void Act()
        {
            float targetPower = Mathf.Max(1f, EmpireStats.MilitaryPower(TargetOwner));
            float coalitionPower = PlayerMember ? EmpireStats.MilitaryPower(0) : 0f;
            foreach (int m in _members) coalitionPower += EmpireStats.MilitaryPower(m);
            if (coalitionPower < targetPower * WarStrengthRatio) return;

            var targetAi = TargetOwner == 0 ? null : AIEmpireManager.For(TargetOwner);
            foreach (int m in new List<int>(_members))
            {
                var ai = AIEmpireManager.For(m);
                if (ai == null || ai.IsEliminated) continue;
                if (TargetOwner == 0) ai.CoalitionDeclareWar();
                else if (targetAi != null) AIRelations.CoalitionWar(ai, targetAi);
            }
        }

        // ==================== ДЕЙСТВИЯ ИГРОКА ====================

        private static string InfluenceBlocker(float cost)
        {
            var eco = EconomyManager.Instance;
            return eco != null && eco.CanAfford(0f, 0f, 0f, cost) ? null : $"Нужно {cost:0} влияния";
        }

        /// <summary>Что мешает вступить в действующую коалицию (null — можно).</summary>
        public static string JoinBlocker()
        {
            if (!IsActive || PlayerMember) return "Нет коалиции, к которой можно присоединиться";
            if (TargetOwner == 0) return "Эта коалиция направлена против вас";
            var target = AIEmpireManager.For(TargetOwner);
            if (target == null || target.IsEliminated) return "Цель коалиции уже повержена";
            if (target.HasPact) return "У вас пакт с этой империей: сначала расторгните его";
            foreach (int m in _members)
            {
                var ai = AIEmpireManager.For(m);
                if (ai != null && ai.AtWar) return $"Вы воюете с {ai.AIName}, участником коалиции";
            }
            return InfluenceBlocker(JoinCost);
        }

        public static bool PlayerJoin()
        {
            if (JoinBlocker() != null) return false;
            var eco = EconomyManager.Instance;
            if (eco == null || !eco.TrySpend(0f, 0f, 0f, JoinCost)) return false;
            PlayerMember = true;
            NotificationCenter.Show("Вы вступили в коалицию",
                $"Вы вместе с союзниками выступаете против {TargetPhrase}. Союзники начнут войну, когда накопят силы",
                NotificationCenter.Kind.Success, 9f);
            Refresh();
            return true;
        }

        /// <summary>Что мешает собрать коалицию против этой империи (null — можно).</summary>
        public static string OrganizeBlocker(AIEmpireManager target)
        {
            if (GameSession.Settings.Difficulty < 1) return "На лёгкой сложности коалиции недоступны";
            if (target == null || target.IsEliminated) return "Империя уже повержена";
            if (TargetOwner >= 0) return "В галактике уже действует коалиция";
            int others = 0;
            foreach (var ai in AIEmpireManager.Alive) if (ai != target) others++;
            if (others < 1) return "Нужен ещё хотя бы один соперник";
            if (target.HasPact) return "У вас пакт с этой империей: сначала расторгните его";
            return InfluenceBlocker(OrganizeCost);
        }

        /// <summary>Собрать коалицию: вступят те соперники, кому цель неприятна. Если откликнулись не все, влияние всё равно тратится.</summary>
        public static bool PlayerOrganize(AIEmpireManager target)
        {
            if (OrganizeBlocker(target) != null) return false;

            // Пробный набор: кто согласится, если собрать коалицию против этой цели
            TargetOwner = target.OwnerId;
            PlayerMember = PlayerOrganized = true;
            var willing = new List<AIEmpireManager>();
            foreach (var ai in AIEmpireManager.Alive) if (WantsIn(ai)) willing.Add(ai);
            if (willing.Count == 0)
            {
                Reset();
                NotificationCenter.Show("Никто не откликнулся",
                    $"Остальные соперники не считают {target.AIName} угрозой или не доверяют вам. Улучшите отношения подарками",
                    NotificationCenter.Kind.Warning, 8f);
                return false;
            }
            var eco = EconomyManager.Instance;
            if (eco == null || !eco.TrySpend(0f, 0f, 0f, OrganizeCost)) { Reset(); return false; }

            _members.Clear();
            foreach (var ai in willing) _members.Add(ai.OwnerId);
            _months = 0;
            _announced = true;
            var names = new List<string>();
            foreach (var ai in willing) names.Add(ai.AIName);
            NotificationCenter.Show("Коалиция собрана",
                $"Против империи {target.AIName} выступят: {string.Join(", ", names)}. Через {PrepMonths} мес. союзники начнут войну, когда накопят силы",
                NotificationCenter.Kind.Success, 10f);
            Refresh();
            return true;
        }

        /// <summary>Выйти из коалиции (создатель распускает её): союзники запоминают, что их бросили.</summary>
        public static bool PlayerLeave()
        {
            if (!PlayerMember) return false;
            bool organizer = PlayerOrganized;
            foreach (int m in _members) AIEmpireManager.For(m)?.AddMemory("coalitionleft", organizer ? -8f : -12f);
            string phrase = TargetPhrase;
            if (organizer) Reset();
            else PlayerMember = false;
            NotificationCenter.Show(organizer ? "Коалиция распущена" : "Вы вышли из коалиции",
                $"Союз против {phrase} больше не связывает вас", NotificationCenter.Kind.Warning, 7f);
            Refresh();
            return true;
        }

        /// <summary>Игрок заключил мир с целью, пока союзники ещё воюют с ней: сепаратный мир портит отношения и выводит из коалиции.</summary>
        public static void OnPlayerPeace(int peacedOwner)
        {
            if (!PlayerMember || peacedOwner != TargetOwner) return;
            int abandoned = 0;
            foreach (int m in _members)
            {
                if (!Diplomacy.AtWar(m, TargetOwner)) continue;
                AIEmpireManager.For(m)?.AddMemory("separate", -20f);
                abandoned++;
            }
            bool organizer = PlayerOrganized;
            if (organizer) Reset();
            else PlayerMember = false;
            if (abandoned > 0)
                NotificationCenter.Show("Сепаратный мир",
                    "Вы вышли из войны, оставив союзников одних: они этого не забудут", NotificationCenter.Kind.Warning, 8f);
            Refresh();
        }

        private static void Refresh() => AIEmpireManager.NotifyDiplomacyChanged();
    }
}
