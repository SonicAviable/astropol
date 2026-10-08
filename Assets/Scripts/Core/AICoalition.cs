using System.Collections.Generic;
using UnityEngine;
using StellarisClone.Rendering;

namespace StellarisClone.Core
{
    /// <summary>
    /// Коалиция против зарвавшегося лидера. Если одна империя (игрок или ИИ) уходит далеко вперёд по очкам,
    /// те соперники, кому она не нравится, объединяются: перестают воевать между собой, получают общую
    /// неприязнь к лидеру и, накопив силы, вместе объявляют ему войну. Подарки и пакты удерживают соседей
    /// от вступления; коалиция распадается, когда лидер перестаёт быть настолько сильным.
    /// Состояние не сохраняется: каждый месяц оно пересчитывается по очкам и отношениям.
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
        /// <summary>Коалиция нападает, когда её совокупный флот не слабее такой доли флота лидера.</summary>
        public const float WarStrengthRatio = 0.75f;
        /// <summary>Месяцев между созданием коалиции и первой войной — игрок успевает среагировать.</summary>
        public const int PrepMonths = 3;

        public static int LeaderOwner { get; private set; } = -1;
        private static readonly HashSet<int> _members = new HashSet<int>();
        private static int _months;
        private static bool _announced;

        public static bool IsActive => LeaderOwner >= 0 && _members.Count >= 2;
        public static bool IsMember(int owner) => IsActive && _members.Contains(owner);
        public static bool IsTarget(int owner) => IsActive && owner == LeaderOwner;
        public static bool AreAllies(int a, int b) => a != b && IsMember(a) && IsMember(b);
        public static int MemberCount => _members.Count;

        /// <summary>«вас» или «империи Имя» — для фразы «коалиция против …».</summary>
        public static string LeaderPhrase => LeaderOwner == 0 ? "вас" : "империи " + AIEmpireManager.NameOf(LeaderOwner);

        public static void Reset()
        {
            LeaderOwner = -1;
            _members.Clear();
            _months = 0;
            _announced = false;
        }

        public static void MonthlyUpdate()
        {
            bool wasActive = IsActive;
            string oldPhrase = LeaderPhrase;
            bool oldLeaderWasPlayer = LeaderOwner == 0;

            if (GameSession.Settings.Difficulty < 1) { Reset(); AnnounceEnd(wasActive, oldPhrase, oldLeaderWasPlayer); return; }

            var owners = new List<int> { 0 };
            foreach (var ai in AIEmpireManager.Alive) owners.Add(ai.OwnerId);
            if (owners.Count < 3) { Reset(); AnnounceEnd(wasActive, oldPhrase, oldLeaderWasPlayer); return; }

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

            if (LeaderOwner >= 0)
            {
                bool holds = leader == LeaderOwner && best >= second * LeadToHold && share >= ShareToHold;
                if (!holds) Reset();
            }
            if (LeaderOwner < 0 && year >= FirstYear && EmpireStats.SystemCount(leader) >= MinLeaderSystems
                && best >= second * LeadToForm && share >= ShareToForm)
            {
                LeaderOwner = leader;
                _members.Clear();
                _months = 0;
                _announced = false;
            }

            if (LeaderOwner >= 0)
            {
                _months++;
                UpdateMembers();
                if (IsActive && !_announced) Announce();
                if (IsActive && _months >= PrepMonths) Act();
            }

            AnnounceEnd(wasActive, oldPhrase, oldLeaderWasPlayer);
        }

        private static float OpinionToLeader(AIEmpireManager ai)
        {
            if (LeaderOwner == 0) return ai.Opinion;
            var l = AIEmpireManager.For(LeaderOwner);
            return l != null ? AIRelations.Opinion(ai, l) : 0f;
        }

        private static void UpdateMembers()
        {
            foreach (var ai in AIEmpireManager.Alive)
            {
                int id = ai.OwnerId;
                if (id == LeaderOwner) continue;
                float op = OpinionToLeader(ai);
                float join = ai.Profile.WarOpinionThreshold + JoinMargin;
                bool pact = LeaderOwner == 0 && ai.HasPact;
                if (_members.Contains(id))
                {
                    if (pact || op > join + LeaveMargin) _members.Remove(id);
                }
                else if (!pact && op <= join) _members.Add(id);
            }
        }

        private static void Announce()
        {
            _announced = true;
            var names = new List<string>();
            foreach (int m in _members) names.Add(AIEmpireManager.NameOf(m));
            // Игра загружена посреди войны коалиции (все уже воюют с лидером) — не объявляем её заново
            bool allAtWar = true;
            foreach (int m in _members) if (!Diplomacy.AtWar(m, LeaderOwner)) { allAtWar = false; break; }
            if (allAtWar) return;

            if (LeaderOwner == 0)
                NotificationCenter.Show("КОАЛИЦИЯ ПРОТИВ ВАС",
                    $"{string.Join(", ", names)} объединились: вы стали слишком сильны. Подарки и пакты удержат часть соседей от вступления",
                    NotificationCenter.Kind.Danger, 12f);
            else
                NotificationCenter.Show("Коалиция в галактике",
                    $"{string.Join(", ", names)} объединяются против империи {AIEmpireManager.NameOf(LeaderOwner)}",
                    NotificationCenter.Kind.Info, 8f);
            AIEmpireManager.NotifyDiplomacyChanged();
        }

        private static void AnnounceEnd(bool wasActive, string phrase, bool leaderWasPlayer)
        {
            if (!wasActive || IsActive) return;
            _announced = false;
            NotificationCenter.Show("Коалиция распалась", $"Союз против {phrase} больше не действует",
                leaderWasPlayer ? NotificationCenter.Kind.Success : NotificationCenter.Kind.Info, 8f);
            AIEmpireManager.NotifyDiplomacyChanged();
        }

        private static void Act()
        {
            float leaderPower = Mathf.Max(1f, EmpireStats.MilitaryPower(LeaderOwner));
            float coalitionPower = 0f;
            foreach (int m in _members) coalitionPower += EmpireStats.MilitaryPower(m);
            if (coalitionPower < leaderPower * WarStrengthRatio) return;

            var leaderAi = LeaderOwner == 0 ? null : AIEmpireManager.For(LeaderOwner);
            foreach (int m in new List<int>(_members))
            {
                var ai = AIEmpireManager.For(m);
                if (ai == null || ai.IsEliminated) continue;
                if (LeaderOwner == 0) ai.CoalitionDeclareWar();
                else if (leaderAi != null) AIRelations.CoalitionWar(ai, leaderAi);
            }
        }
    }
}
