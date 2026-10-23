using UnityEngine;

namespace StellarisClone.Core
{
    /// <summary>
    /// Вольные миры (NeutralWorlds) для ИИ — те же действия и цены, что у игрока.
    /// Раз в месяц ИИ делает не больше одного шага: помогает с просьбой, шлёт посольство или дар туда,
    /// где борется за покровительство (характер подсказывает, какие миры ему ближе), торгует излишками,
    /// нанимает наёмников на войне. Воинственный ИИ с сильным флотом может напасть на соседний вольный мир,
    /// который ему не друг, — флот идёт на блокаду; затянувшуюся войну он заканчивает миром.
    /// </summary>
    public partial class AIEmpireManager
    {
        private int _neutralWarMonths;

        /// <summary>Нанятый у вольных клинков отряд: фрегат (покровителю — эсминец, если он уже доступен).</summary>
        public void SpawnMercenary(bool patron)
        {
            var hull = patron && Bonuses.DestroyerUnlocked ? ShipClass.Destroyer : ShipClass.Frigate;
            var fv = CreateWarship(hull);
            if (fv != null) fv.Data.Name = Loc.F("{0} · Наёмники", AIName);
        }

        /// <summary>Насколько характеру ИИ близок вольный мир (кого стоит взять под покровительство).</summary>
        private float NeutralAffinity(NeutralCharacter c)
        {
            switch (Personality)
            {
                case AIPersonality.Trader: return c == NeutralCharacter.Trade || c == NeutralCharacter.Mining ? 1.5f : 1f;
                case AIPersonality.Scientific: return c == NeutralCharacter.Science ? 1.6f : 1f;
                case AIPersonality.Militarist: return c == NeutralCharacter.Mercenary || c == NeutralCharacter.Industry ? 1.5f : 0.8f;
                case AIPersonality.Mystic: return c == NeutralCharacter.Spiritual || c == NeutralCharacter.Science ? 1.4f : 0.9f;
                case AIPersonality.Gardener: return c == NeutralCharacter.Spiritual || c == NeutralCharacter.Trade ? 1.4f : 1f;
                case AIPersonality.Machine: return c == NeutralCharacter.Industry || c == NeutralCharacter.Mining ? 1.4f : 1f;
            }
            return 1f;
        }

        private void ThinkNeutrals()
        {
            if (NeutralWorlds.All.Count == 0 || IsEliminated || !NeutralWorld.Valid(OwnerId)) return;
            if (ThinkNeutralWar()) return;
            if (_bankrupt) return;

            // 1. Просьба по силам — лучший способ завоевать расположение
            foreach (var w in NeutralWorlds.All)
            {
                if (NeutralWorlds.QuestBlocker(w, OwnerId) != null) continue;
                string res = NeutralWorlds.QuestResource(w);
                if (Need(res) > 1f || Stock(res) - w.QuestAmount < (res == "alloys" ? 150f : 200f)) continue;
                if (NeutralWorlds.FulfilQuest(w, OwnerId)) return;
            }

            // 2. Наёмники на войне
            if (AtWarWithAnyone && Alloys > 300f)
                foreach (var w in NeutralWorlds.All)
                    if (NeutralWorlds.HireBlocker(w, OwnerId) == null && NeutralWorlds.Hire(w, OwnerId)) return;

            // 3. Борьба за покровительство: самый близкий по духу мир, где мы ещё не покровители
            NeutralWorld target = null;
            float bestScore = 0f;
            foreach (var w in NeutralWorlds.All)
            {
                if (!w.Met(OwnerId) || w.AtWarWith(OwnerId) || w.Patron == OwnerId) continue;
                float mine = w.FavorOf(OwnerId);
                float leader = 0f;
                for (int o = 0; o < NeutralWorld.Owners; o++) if (o != OwnerId) leader = Mathf.Max(leader, w.Favor[o]);
                // Безнадёжно отстали — не тратимся; почти догнали — стараемся сильнее
                if (leader - mine > 70f) continue;
                float score = NeutralAffinity(w.Kind) * (1f + Mathf.Clamp01((mine + 20f) / 80f));
                if (score > bestScore) { bestScore = score; target = w; }
            }
            if (target == null) { TradeWithNeutrals(); return; }

            float influenceReserve = YearsPlayed < 25 ? 75f : 50f;
            if (Influence - NeutralWorlds.EnvoyInfluence >= influenceReserve && NeutralWorlds.EnvoyBlocker(target, OwnerId) == null)
            {
                NeutralWorlds.SendEnvoy(target, OwnerId);
                return;
            }
            string gift = NeutralWorlds.Wants(target.Kind);
            if (Need(gift) <= 0.7f && NeutralWorlds.GiftBlocker(target, OwnerId) == null)
            {
                NeutralWorlds.SendGift(target, OwnerId);
                return;
            }
            TradeWithNeutrals();
        }

        /// <summary>Обменять излишек на то, чего не хватает.</summary>
        private void TradeWithNeutrals()
        {
            foreach (var w in NeutralWorlds.All)
            {
                if (NeutralWorlds.TradeBlocker(w, OwnerId) != null) continue;
                string give = NeutralWorlds.Wants(w.Kind), get = NeutralWorlds.Sells(w.Kind);
                if (get == null || Need(give) > 0.7f) continue;
                bool wanted = get == "science" ? Personality == AIPersonality.Scientific : Need(get) >= 1.5f;
                if (!wanted) continue;
                if (NeutralWorlds.Trade(w, OwnerId)) return;
            }
        }

        /// <summary>
        /// Война с вольным миром: начать (воинственный ИИ, сильный флот, мир по соседству и не друг)
        /// или закончить (больше двух лет или флот слишком слаб для блокады). true — месяц занят войной.
        /// </summary>
        private bool ThinkNeutralWar()
        {
            int sysId = NeutralWorlds.WarTargetOf(OwnerId);
            if (sysId >= 0)
            {
                var w = NeutralWorlds.At(sysId);
                _neutralWarMonths++;
                float power = EmpireStats.MilitaryPower(OwnerId);
                bool hopeless = power < NeutralWorlds.Defense(w) * 0.8f && _neutralWarMonths >= 6;
                if ((_neutralWarMonths >= 24 || hopeless || AtWarWithAnyone) && NeutralWorlds.PeaceBlocker(w, OwnerId) == null)
                {
                    NeutralWorlds.MakePeace(w, OwnerId);
                    _neutralWarMonths = 0;
                }
                return true;
            }
            _neutralWarMonths = 0;

            if (AtWarWithAnyone || IsPreparingWar || YearsPlayed < AIDifficulty.PeaceYears + 3) return false;
            bool aggressive = Personality == AIPersonality.Militarist || (Personality == AIPersonality.Machine && AIDifficulty.Tier >= 3);
            if (!aggressive || Random.value > 0.08f * AggressionFactor) return false;

            float myPower = EmpireStats.MilitaryPower(OwnerId);
            foreach (var w in NeutralWorlds.All)
            {
                var sys = EmpireStats.GetSystem(w.SystemId);
                if (sys == null || !BordersOwn(sys) || NeutralWorlds.WarBlocker(w, OwnerId) != null) continue;
                if (w.FavorOf(OwnerId) >= 10f || w.Patron == OwnerId) continue;
                if (myPower < NeutralWorlds.Defense(w) * 1.6f) continue;
                // Мир под покровительством игрока — только если игрока не боимся
                if (w.Patron == 0 && PowerRatio < 1.3f) continue;
                if (NeutralWorlds.DeclareWar(w, OwnerId)) return true;
            }
            return false;
        }

        private void CaptureNeutrals(AISave s) => s.NeutralWarMonths = _neutralWarMonths;
        private void RestoreNeutrals(AISave s) => _neutralWarMonths = s.NeutralWarMonths;
    }
}
