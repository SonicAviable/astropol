using System;
using System.Collections.Generic;
using UnityEngine;

namespace StellarisClone.Core
{
    public enum CasusBelliKind { Reclaim, Ultimatum, Retribution, Kin, AllyDefense }

    [Serializable]
    public class CasusBelliClaim
    {
        public int Owner;
        public CasusBelliKind Kind;
        public int MonthsLeft;
        /// <summary>Для «Вернуть утраченное» — система, отнятая у игрока.</summary>
        public int SystemId = -1;
    }

    [Serializable]
    public class CasusBelliSave
    {
        public List<CasusBelliClaim> Claims = new List<CasusBelliClaim>();
    }

    /// <summary>Действующий повод для войны против одной империи — для окна дипломатии.</summary>
    public struct CasusBelliInfo
    {
        public CasusBelliKind Kind;
        public string Name, Detail;
        public int MonthsLeft;   // −1 — пока сохраняется причина
    }

    /// <summary>
    /// Поводы для войны игрока против империй ИИ. Они появляются из самой игры:
    ///   • «Вернуть утраченное» — соперник захватил вашу систему и удерживает её (10 лет);
    ///   • «Ответ на ультиматум» — вам выставили требование дани (5 лет);
    ///   • «Возмездие» — соперник объявил вам войну, тем более нарушив пакт (10–15 лет);
    ///   • «Защита сородичей» — на их планетах живут представители вашего народа (пока это так);
    ///   • «Защита союзника» — они воюют с вашим союзником (пока идёт война).
    /// Война с поводом не портит репутацию; без повода — удар по ней у всех знакомых империй.
    /// Системы, на которые у вас есть повод «Вернуть утраченное», соперник отдаёт в мирном договоре дешевле.
    /// </summary>
    public static class CasusBelli
    {
        public const int ReclaimMonths = 120, UltimatumMonths = 60, RetributionMonths = 120, BetrayalMonths = 180;
        /// <summary>Удар по репутации за войну без повода.</summary>
        public const float NoCauseReputation = -10f;

        private static readonly List<CasusBelliClaim> _claims = new List<CasusBelliClaim>();

        public static void Reset() => _claims.Clear();

        public static string Name(CasusBelliKind k) => k switch
        {
            CasusBelliKind.Reclaim => Loc.T("Вернуть утраченное"),
            CasusBelliKind.Ultimatum => Loc.T("Ответ на ультиматум"),
            CasusBelliKind.Retribution => Loc.T("Возмездие"),
            CasusBelliKind.Kin => Loc.T("Защита сородичей"),
            _ => Loc.T("Защита союзника")
        };

        // ==================== ИСТОЧНИКИ ====================

        /// <summary>Добавить или продлить повод (одна запись на империю, вид и систему).</summary>
        public static void Grant(int owner, CasusBelliKind kind, int months, int systemId = -1)
        {
            if (owner <= 0 || !AIEmpireManager.IsAI(owner)) return;
            var c = _claims.Find(x => x.Owner == owner && x.Kind == kind && x.SystemId == systemId);
            if (c != null) { c.MonthsLeft = Mathf.Max(c.MonthsLeft, months); return; }
            _claims.Add(new CasusBelliClaim { Owner = owner, Kind = kind, MonthsLeft = months, SystemId = systemId });
            AIEmpireManager.NotifyDiplomacyChanged();
        }

        /// <summary>Соперник отнял у игрока систему — появляется повод вернуть её.</summary>
        public static void OnSystemCaptured(StarSystem sys, int oldOwner, int newOwner)
        {
            if (sys == null || oldOwner != 0 || newOwner <= 0 || SiegeManager.IsPeacefulTransfer) return;
            Grant(newOwner, CasusBelliKind.Reclaim, ReclaimMonths, sys.Id);
        }

        public static void Monthly()
        {
            for (int i = _claims.Count - 1; i >= 0; i--)
            {
                var c = _claims[i];
                var ai = AIEmpireManager.For(c.Owner);
                bool gone = ai == null || ai.IsEliminated || --c.MonthsLeft <= 0;
                if (!gone && c.Kind == CasusBelliKind.Reclaim)
                {
                    var s = EmpireStats.GetSystem(c.SystemId);
                    gone = s == null || s.OwnerId != c.Owner;   // система вернулась или ушла к другим
                }
                if (gone) _claims.RemoveAt(i);
            }
        }

        // ==================== ЗАПРОСЫ ====================

        /// <summary>Все действующие поводы против империи: записанные и те, что следуют из текущего положения.</summary>
        public static List<CasusBelliInfo> For(int owner)
        {
            var list = new List<CasusBelliInfo>();
            var ai = AIEmpireManager.For(owner);
            if (ai == null || ai.IsEliminated) return list;

            int reclaim = 0, reclaimMonths = 0;
            foreach (var c in _claims)
            {
                if (c.Owner != owner) continue;
                if (c.Kind == CasusBelliKind.Reclaim) { reclaim++; reclaimMonths = Mathf.Max(reclaimMonths, c.MonthsLeft); continue; }
                list.Add(new CasusBelliInfo
                {
                    Kind = c.Kind, Name = Name(c.Kind), MonthsLeft = c.MonthsLeft,
                    Detail = c.Kind == CasusBelliKind.Ultimatum ? Loc.T("Они требовали от вас дань")
                           : Loc.T("Они напали на вас — ответный удар оправдан")
                });
            }
            if (reclaim > 0)
                list.Insert(0, new CasusBelliInfo
                {
                    Kind = CasusBelliKind.Reclaim, Name = Name(CasusBelliKind.Reclaim), MonthsLeft = reclaimMonths,
                    Detail = Loc.F("Они удерживают ваши бывшие системы: {0}", reclaim)
                });

            int kin = KinPops(owner);
            if (kin > 0)
                list.Add(new CasusBelliInfo { Kind = CasusBelliKind.Kin, Name = Name(CasusBelliKind.Kin), MonthsLeft = -1,
                    Detail = Loc.F("Под их властью живут ваши сородичи: {0}", kin) });

            foreach (var ally in AIEmpireManager.Alive)
                if (ally.HasAlliance && ally.OwnerId != owner && AIRelations.AtWar(ally.OwnerId, owner))
                {
                    list.Add(new CasusBelliInfo { Kind = CasusBelliKind.AllyDefense, Name = Name(CasusBelliKind.AllyDefense), MonthsLeft = -1,
                        Detail = Loc.F("Они воюют с вашим союзником: {0}", ally.AIName) });
                    break;
                }
            return list;
        }

        public static bool Has(int owner) => For(owner).Count > 0;

        /// <summary>Есть ли у игрока повод «Вернуть утраченное» именно на эту систему.</summary>
        public static bool IsClaimed(int systemId, int owner)
            => _claims.Exists(c => c.Kind == CasusBelliKind.Reclaim && c.SystemId == systemId && c.Owner == owner);

        /// <summary>Сородичи игрока (народ-основатель и родственные линии) на планетах этой империи.</summary>
        private static int KinPops(int owner)
        {
            int founder = SpeciesRegistry.FounderOf(0);
            int n = 0;
            foreach (var s in EmpireStats.Systems)
            {
                if (s.OwnerId != owner) continue;
                foreach (var p in s.Planets)
                    if (p.Population > 0) n += p.CountOf(sp => !sp.Robot && SpeciesRegistry.Related(sp.Id, founder));
            }
            return n;
        }

        /// <summary>Подсказка для окна дипломатии: какие поводы есть и что будет без них.</summary>
        public static string Tooltip(int owner)
        {
            var list = For(owner);
            var sb = new System.Text.StringBuilder();
            if (list.Count == 0)
            {
                sb.Append(Loc.T("<b>Повода для войны нет</b>\n"));
                sb.Append(Loc.F("Объявить войну можно, но все знакомые империи сочтут вас агрессором: репутация {0:+0;-0}.", NoCauseReputation));
            }
            else
            {
                sb.Append(Loc.T("<b>Поводы для войны</b>"));
                foreach (var c in list)
                {
                    sb.Append($"\n<color=#F2C747>{c.Name}</color> — {c.Detail}");
                    if (c.MonthsLeft > 0) sb.Append(Loc.F(" <color=#8AA2A8>· ещё {0}</color>", DealCatalog.Months(c.MonthsLeft)));
                }
                sb.Append(Loc.T("\n<color=#8AA2A8>С поводом война не портит репутацию, а утраченные системы проще вернуть в мирном договоре</color>"));
            }
            return sb.ToString();
        }

        // ==================== СОХРАНЕНИЕ ====================

        public static CasusBelliSave Capture()
        {
            var s = new CasusBelliSave();
            foreach (var c in _claims)
                s.Claims.Add(new CasusBelliClaim { Owner = c.Owner, Kind = c.Kind, MonthsLeft = c.MonthsLeft, SystemId = c.SystemId });
            return s;
        }

        public static void Restore(CasusBelliSave s)
        {
            Reset();
            if (s?.Claims != null) _claims.AddRange(s.Claims);
        }
    }
}
