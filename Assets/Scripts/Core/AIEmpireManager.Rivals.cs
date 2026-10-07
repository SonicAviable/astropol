using System.Collections.Generic;
using UnityEngine;

namespace StellarisClone.Core
{
    /// <summary>
    /// Кто для ИИ враг и сосед. Империй три (игрок и два ИИ), поэтому военные и экспансионные
    /// решения смотрят не на «игрока», а на всех, с кем идёт война, и на опасных соседей.
    /// </summary>
    public partial class AIEmpireManager
    {
        /// <summary>Воюет ли эта империя с владельцем (игроком или другим ИИ).</summary>
        public bool IsEnemy(int owner) => owner >= 0 && owner != OwnerId && Diplomacy.AtWar(OwnerId, owner);

        /// <summary>Идёт ли хоть одна война — с игроком или с другим ИИ.</summary>
        public bool AtWarWithAnyone => AtWar || WarsWithAIs > 0;

        public int WarsWithAIs
        {
            get
            {
                int n = 0;
                foreach (var o in Alive) if (o != this && AIRelations.AtWar(OwnerId, o.OwnerId)) n++;
                return n;
            }
        }

        /// <summary>Другие империи в партии: игрок и живые соперники.</summary>
        private IEnumerable<int> OtherEmpires()
        {
            yield return 0;
            foreach (var o in Alive) if (o != this) yield return o.OwnerId;
        }

        private float EnemyFleetPowerIn(int systemId)
        {
            var fm = FleetManager.Instance;
            if (fm == null) return 0f;
            float p = 0f;
            foreach (int o in OtherEmpires())
                if (IsEnemy(o)) p += fm.GetMilitaryPowerInSystem(o, systemId);
            return p;
        }

        /// <summary>Сила врагов в системе: их корабли и звёздная база, если система вражеская.</summary>
        private float EnemyPowerIn(int systemId)
        {
            float p = EnemyFleetPowerIn(systemId);
            var s = EmpireStats.GetSystem(systemId);
            if (s != null && IsEnemy(s.OwnerId) && CombatManager.Instance != null) p += CombatManager.Instance.StarbasePower(systemId);
            return p;
        }

        /// <summary>На чью мощь равняться при постройке флота: враги и соседи — полностью, дальние — частично.</summary>
        private float RivalPowerForBuild()
        {
            float best = 0f;
            foreach (int o in OtherEmpires())
            {
                float pw = EmpireStats.MilitaryPower(o);
                if (!IsEnemy(o) && EmpireStats.SharedBorderCount(OwnerId, o) == 0) pw *= 0.6f;
                best = Mathf.Max(best, pw);
            }
            return best;
        }

        /// <summary>Главный противник: враг с самой длинной общей границей, иначе самый опасный сосед.</summary>
        private int FocusRival()
        {
            int best = -1;
            float bestScore = float.MinValue;
            foreach (int o in OtherEmpires())
            {
                if (EmpireStats.SystemCount(o) == 0) continue;
                float score = EmpireStats.SharedBorderCount(OwnerId, o) * 10f + EmpireStats.MilitaryPower(o) * 0.01f;
                if (IsEnemy(o)) score += 1000f;
                if (score > bestScore) { bestScore = score; best = o; }
            }
            return best;
        }
    }
}
