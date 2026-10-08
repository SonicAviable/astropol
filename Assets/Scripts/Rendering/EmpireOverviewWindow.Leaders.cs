using System.Text;
using UnityEngine;
using UnityEngine.UI;
using StellarisClone.Core;

namespace StellarisClone.Rendering
{
    /// <summary>
    /// Вкладка «Лидеры» в обзоре империи: нанятые лидеры (уровень, опыт, черты, пост),
    /// кандидаты на найм и выбор поста для лидера (корабль / научный совет / колония).
    /// </summary>
    public partial class EmpireOverviewWindow
    {
        private int _assignLeaderId = -1;

        private static LGIcon ClassIcon(LeaderClass c) => c == LeaderClass.Scientist ? LGIcon.Sensors
                                                       : c == LeaderClass.Admiral ? LGIcon.Fleet : LGIcon.Planet;

        private static Color ClassColor(LeaderClass c) => c == LeaderClass.Scientist ? CResearch
                                                       : c == LeaderClass.Admiral ? new Color(1f, 0.42f, 0.42f) : CGold;

        private void BuildLeaders()
        {
            var lm = LeaderManager.Instance;
            if (lm == null) { Empty("Система лидеров недоступна."); return; }

            if (_assignLeaderId >= 0)
            {
                Leader target = null;
                foreach (var l in lm.All) if (l.Id == _assignLeaderId) { target = l; break; }
                if (target != null) { BuildAssignTargets(lm, target); return; }
                _assignLeaderId = -1;
            }

            HeaderCols((0.06f, 0.29f, "ЛИДЕР", TextAnchor.MiddleLeft),
                       (0.29f, 0.42f, "УРОВЕНЬ", TextAnchor.MiddleLeft),
                       (0.42f, 0.60f, "ЧЕРТЫ", TextAnchor.MiddleLeft),
                       (0.60f, 0.78f, "ПОСТ", TextAnchor.MiddleLeft),
                       (0.78f, 1f, "", TextAnchor.MiddleRight));

            int mine = 0;
            foreach (var l in lm.All)
            {
                if (l.Owner != 0) continue;
                mine++;
                LeaderRow(lm, l, true);
            }
            if (mine == 0)
                SectionRow("Лидеров пока нет. Наймите учёного для разведки, адмирала для флота или губернатора для колонии.", UIManager.DS.TextMuted);

            SectionRow($"КАНДИДАТЫ  ·  найм <color=#FF7A94>★{LeaderManager.HireInfluence:0}</color> влияния  ·  содержание " +
                       $"<color=#FFCC52>⚡{LeaderManager.UpkeepEnergy:0.#}</color>/мес  ·  лидеров {mine}/{LeaderManager.MaxLeaders}  ·  кандидаты обновляются каждый год",
                       UIManager.DS.TextMuted);
            foreach (var c in lm.Candidates) LeaderRow(lm, c, false);
        }

        private void SectionRow(string text, Color col)
        {
            var host = LGBuild.Rect(_list, "Section");
            LGBuild.Height(host.gameObject, 30);
            var t = LGBuild.Label(host, text, 11, col, TextAnchor.LowerLeft, bold: true);
            t.rectTransform.Stretch(10, 4, 10, 0);
        }

        private void LeaderRow(LeaderManager lm, Leader l, bool hired)
        {
            Color col = ClassColor(l.Class);
            var row = Row(null, new Color(col.r, col.g, col.b, hired ? 0.3f : 0.16f));
            PortraitCell(row, 0f, 0.06f, l, col);
            TwoLine(row, 0.06f, 0.29f, l.Name, $"{LeaderManager.ClassName(l.Class)} · {l.Age} {AgeWord(l.Age)}");

            string lvl = l.Level >= LeaderManager.MaxLevel ? $"ур. {l.Level} · максимум" : $"ур. {l.Level}";
            BarCell(row, 0.29f, 0.42f, l.LevelProgress, col, lvl);

            var traits = new StringBuilder();
            foreach (var id in l.Traits)
            {
                var t = LeaderManager.Trait(id);
                if (t == null) continue;
                if (traits.Length > 0) traits.Append(", ");
                traits.Append(t.Negative ? $"<color=#FF7A7A>{t.Name}</color>" : t.Name);
            }
            TextCell(row, 0.42f, 0.60f, traits.ToString(), CGold, TextAnchor.MiddleLeft, 11, true);
            TextCell(row, 0.60f, 0.78f, hired ? PostText(l) : "—", UIManager.DS.TextPrimary, TextAnchor.MiddleLeft, 11);

            var actions = Cell(row, 0.78f, 1f);
            if (hired)
            {
                bool assigned = l.Post != LeaderPost.None;
                var main = LGBuild.Button(actions, "Assign", assigned ? UIManager.DS.BtnNeutral : UIManager.DS.BtnPrimary,
                    new Color(col.r, col.g, col.b, 0.6f), () =>
                    {
                        if (l.Post != LeaderPost.None) lm.Unassign(l);
                        else _assignLeaderId = l.Id;
                        RebuildList();
                    }, null, assigned ? "СНЯТЬ" : "НАЗНАЧИТЬ", 10);
                ((RectTransform)main.transform).Stretch(0, 8, 40, 8);
                var dismiss = LGBuild.Button(actions, "Dismiss", UIManager.DS.BtnDanger, UIManager.DS.Red, () =>
                {
                    lm.Dismiss(l);
                    RebuildList();
                }, LGIcon.Close, null, 10);
                ((RectTransform)dismiss.transform).At(new Vector2(1, 0.5f), new Vector2(1, 0.5f), Vector2.zero, new Vector2(32, 32));
                TooltipHelper.Attach(dismiss.gameObject, "<b>Уволить</b>\nЛидер покинет службу навсегда; содержание перестанет списываться.");
            }
            else
            {
                bool can = lm.CanHire(out string why);
                var hire = LGBuild.Button(actions, "Hire", can ? UIManager.DS.BtnPrimary : UIManager.DS.BtnNeutral,
                    new Color(col.r, col.g, col.b, 0.6f), () =>
                    {
                        lm.Hire(l);
                        RebuildList();
                    }, null, $"НАНЯТЬ  ★{LeaderManager.HireInfluence:0}", 10);
                ((RectTransform)hire.transform).Stretch(0, 8, 0, 8);
                hire.interactable = can;
                if (!can) TooltipHelper.Attach(hire.gameObject, why);
            }

            TooltipHelper.Attach(row.gameObject, LeaderTooltip(l));
        }

        /// <summary>Лицо лидера в скруглённой рамке цвета класса; без портрета — иконка класса.</summary>
        private static void PortraitCell(RectTransform row, float x0, float x1, Leader l, Color col)
        {
            if (LeaderFaces.For(l) == null) { IconCell(row, x0, x1, ClassIcon(l.Class), col); return; }
            var frame = LGBuild.Rect(Cell(row, x0, x1), "Portrait");
            frame.At(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(40, 40));
            LeaderThumb.Create(frame, 10f).Set(l, col);
        }

        private static string AgeWord(int n)
        {
            int m = n % 100, d = n % 10;
            if (m >= 11 && m <= 14) return "лет";
            return d == 1 ? "год" : d >= 2 && d <= 4 ? "года" : "лет";
        }

        private string PostText(Leader l)
        {
            switch (l.Post)
            {
                case LeaderPost.Council:
                    return $"<color=#66F59E>Научный совет</color> · наука +{(LeaderManager.ResearchMult(0) - 1f) * 100f:0}%";
                case LeaderPost.Planet:
                {
                    var sys = EmpireStats.GetSystem(l.SystemId);
                    var p = sys != null && l.PlanetIndex >= 0 && l.PlanetIndex < sys.Planets.Count ? sys.Planets[l.PlanetIndex] : null;
                    return p != null ? $"Губернатор · {p.Name}" : "Колония";
                }
                case LeaderPost.Ship:
                {
                    var fm = FleetManager.Instance;
                    if (fm != null)
                        foreach (var f in fm.AllFleets)
                            if (f?.Data != null && f.Data.Id == l.ShipId)
                                return $"{f.Data.Name} · {SystemName(f.Data.CurrentSystemId)}";
                    return "Корабль";
                }
            }
            return "<color=#FFAA55>Без поста</color>";
        }

        private static string LeaderTooltip(Leader l)
        {
            var sb = new StringBuilder();
            sb.Append($"<b>{l.Name}</b>\n{LeaderManager.ClassName(l.Class)} · уровень {l.Level}");
            if (l.Level < LeaderManager.MaxLevel) sb.Append($" · до следующего {Mathf.CeilToInt(l.XpToNext)} опыта");
            sb.Append($"\n<color=#8AA2A8>Уровень:</color> {LeaderManager.LevelBonusText(l)}");
            foreach (var id in l.Traits)
            {
                var t = LeaderManager.Trait(id);
                if (t != null) sb.Append($"\n<color={(t.Negative ? "#FF7A7A" : "#FFCC52")}>{t.Name}</color> — {t.Desc}");
            }
            sb.Append("\n\n<color=#8AA2A8>");
            sb.Append(l.Class switch
            {
                LeaderClass.Scientist => "Пост: научный корабль (разведка быстрее, трофеи больше) или научный совет (+к науке). Опыт — за разведку.",
                LeaderClass.Admiral => "Пост: военный корабль-флагман. Бонусы получают все ваши боевые корабли в той же системе. Опыт — за урон и победы. Гибнет вместе с флагманом.",
                _ => "Пост: колония. Производство планеты и скорость строительства на ней выше. Опыт — за время на посту и стройки."
            });
            sb.Append($"\n3-й уровень — вторая черта. Возраст {l.Age}.</color>");
            return sb.ToString();
        }

        // ---------- выбор поста ----------

        private void BuildAssignTargets(LeaderManager lm, Leader l)
        {
            Color col = ClassColor(l.Class);
            HeaderCols((0.06f, 0.60f, $"НАЗНАЧЕНИЕ: {LeaderManager.ClassName(l.Class).ToUpper()} {l.Name.ToUpper()}", TextAnchor.MiddleLeft),
                       (0.60f, 1f, "ТЕКУЩИЙ ЛИДЕР НА ПОСТУ", TextAnchor.MiddleRight));

            var back = Row(() => { _assignLeaderId = -1; RebuildList(); }, new Color(0.6f, 0.8f, 0.9f, 0.25f));
            IconCell(back, 0f, 0.06f, LGIcon.Back, UIManager.DS.TextMuted);
            TextCell(back, 0.06f, 1f, "Назад к списку лидеров", UIManager.DS.TextMuted, TextAnchor.MiddleLeft, 12, true);

            int n = 0;
            void Target(LGIcon icon, string title, string sub, Leader current, System.Action assign)
            {
                n++;
                var row = Row(() =>
                {
                    assign();
                    _assignLeaderId = -1;
                    SFXManager.Play(StellarisClone.Core.Audio.Sfx.UiConfirm);
                    RebuildList();
                }, new Color(col.r, col.g, col.b, 0.3f));
                IconCell(row, 0f, 0.06f, icon, col);
                TwoLine(row, 0.06f, 0.60f, title, sub);
                TextCell(row, 0.60f, 1f, current != null && current != l ? $"{current.Name} (будет снят)" : "свободно",
                    current != null && current != l ? CGold : UIManager.DS.TextMuted, TextAnchor.MiddleRight, 11);
            }

            var fm = FleetManager.Instance;
            switch (l.Class)
            {
                case LeaderClass.Scientist:
                    Target(LGIcon.Research, "Научный совет", "Ускоряет все исследования империи", lm.CouncilScientist(0), () => lm.AssignCouncil(l));
                    if (fm != null)
                        foreach (var f in fm.AllFleets)
                            if (f?.Data != null && !f.Data.Destroyed && f.Data.OwnerId == 0 && f.Data.Type == FleetType.Science)
                            {
                                var d = f.Data;
                                Target(LGIcon.Sensors, d.Name, $"Научный корабль · {SystemName(d.CurrentSystemId)} · {FleetTask(d)}",
                                    lm.LeaderOfShip(d.Id), () => lm.AssignShip(l, d));
                            }
                    break;

                case LeaderClass.Admiral:
                    if (fm != null)
                        foreach (var f in fm.AllFleets)
                            if (f?.Data != null && !f.Data.Destroyed && f.Data.OwnerId == 0 && f.Data.Type == FleetType.Military)
                            {
                                var d = f.Data;
                                Target(LGIcon.Fleet, d.Name, $"Флагман · {SystemName(d.CurrentSystemId)} · мощь {Mathf.RoundToInt(CombatMath.Power(d)):N0}",
                                    lm.LeaderOfShip(d.Id), () => lm.AssignShip(l, d));
                            }
                    break;

                default:
                    foreach (var s in EmpireStats.Systems)
                    {
                        if (s.OwnerId != 0) continue;
                        foreach (var p in s.Planets)
                        {
                            if (p.Population <= 0) continue;
                            var planet = p;
                            Target(LGIcon.Planet, p.Name, $"{s.Name} · население {p.Population} · районов {p.BuiltDistricts}",
                                lm.GovernorOf(p), () => lm.AssignPlanet(l, planet));
                        }
                    }
                    break;
            }

            if (n == 0)
                SectionRow(l.Class == LeaderClass.Admiral ? "Нет военных кораблей — постройте их на верфи."
                         : "Нет колоний для губернатора.", UIManager.DS.TextMuted);
        }
    }
}
