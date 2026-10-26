using System.Collections.Generic;
using UnityEngine;
using StellarisClone.Core;

namespace StellarisClone.Ground
{
    /// <summary>
    /// Управление боем: ЛКМ/рамка — выделение (двойной клик — все такие же на экране), ПКМ — идти/атаковать,
    /// A+ЛКМ — идти в атаке, T — отступление, Z/X/C — способности отряда, B — подкрепления (ЛКМ по земле — место капсулы),
    /// O — орбитальный удар, Ctrl+1…9 / 1…9 — группы, Пробел — тактическая пауза, +/− — скорость, Esc — отмена.
    /// </summary>
    public sealed class GroundInput : MonoBehaviour
    {
        public enum Mode { None, Reinforce, Orbital, Ability }

        public static Mode Targeting { get; private set; }
        public static UnitDef ReinforceDef { get; private set; }
        public static string AbilityId { get; private set; }
        /// <summary>A зажата при выделенных отрядах — ЛКМ отдаёт приказ «идти в атаке» (камера на A не едет).</summary>
        public static bool AttackModifier => Input.GetKey(KeyCode.A) && s_selected.Count > 0;

        private static readonly List<Squad> s_selected = new List<Squad>();
        public static IReadOnlyList<Squad> Selected => s_selected;

        private readonly Dictionary<int, List<Squad>> _groups = new Dictionary<int, List<Squad>>();
        private Vector2 _dragStart;
        private bool _dragging;
        private float _lastClick;
        private Squad _lastClicked;
        private int _lastGroupKey = -1;
        private float _lastGroupTime;

        public static Rect DragRect { get; private set; }
        public static bool IsDragging { get; private set; }

        private GroundBattleManager G => GroundBattleManager.Instance;

        private void OnEnable() { s_selected.Clear(); Targeting = Mode.None; }
        private void OnDisable() { s_selected.Clear(); Targeting = Mode.None; IsDragging = false; }

        public static void BeginReinforce(UnitDef def) { Targeting = Mode.Reinforce; ReinforceDef = def; }
        public static void BeginOrbital() { Targeting = Mode.Orbital; }
        public static void BeginAbilityTargeting(string id) { Targeting = Mode.Ability; AbilityId = id; }
        public static void Cancel() { Targeting = Mode.None; ReinforceDef = null; AbilityId = null; }

        public static void Select(Squad s, bool add)
        {
            if (!add) { foreach (var x in s_selected) x.Selected = false; s_selected.Clear(); }
            if (s == null || s.Destroyed || s_selected.Contains(s)) return;
            s.Selected = true;
            s_selected.Add(s);
        }

        private void Update()
        {
            var gm = G;
            if (gm == null || gm.Cam == null || !GroundBattleManager.InBattle || ResultScreen.IsOpen) return;
            s_selected.RemoveAll(s => s.Destroyed);
            var player = gm.Player;
            if (player == null) return;
            bool ui = GroundHud.PointerOverUi;

            // ---- Клавиши ----
            if (Input.GetKeyDown(KeyCode.Space)) gm.TogglePause();
            if (Input.GetKeyDown(KeyCode.Equals) || Input.GetKeyDown(KeyCode.KeypadPlus)) gm.ChangeSpeed(1);
            if (Input.GetKeyDown(KeyCode.Minus) || Input.GetKeyDown(KeyCode.KeypadMinus)) gm.ChangeSpeed(-1);
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                if (Targeting != Mode.None) Cancel();
                else if (s_selected.Count > 0) Select(null, false);
                // Ничего не выделено — выход из боя (с подтверждением; бой засчитывается проигранным атакующим)
                else if (player.Index == 0) ResultScreen.Confirm(Loc.T("Покинуть бой?"), Loc.T("Десант будет отозван: штурм проигран, осада начнётся заново."), () => gm.Surrender());
                else ResultScreen.Confirm(Loc.T("Покинуть бой?"), Loc.T("Гарнизон сложит оружие: колония достанется врагу."), () => gm.Capitulate());
            }
            if (Input.GetKeyDown(KeyCode.T)) foreach (var s in s_selected) s.OrderRetreat();
            if (Input.GetKeyDown(KeyCode.B)) gm.Hud?.ToggleReinforcements();
            if (Input.GetKeyDown(KeyCode.O)) { if (player.OrbitalCooldown <= 0f && player.Energy >= GroundBattleManager.OrbitalEnergy) BeginOrbital(); else gm.Hud?.Flash(Loc.T("Орбитальный удар не готов"), Color.white); }
            if (Input.GetKeyDown(KeyCode.Z)) UseAbility(0);
            if (Input.GetKeyDown(KeyCode.X)) UseAbility(1);
            if (Input.GetKeyDown(KeyCode.C)) UseAbility(2);
            HandleGroups();

            // ---- Режим прицеливания ----
            if (Targeting != Mode.None)
            {
                if (Input.GetMouseButtonDown(1)) { Cancel(); return; }
                if (!ui && Input.GetMouseButtonDown(0) && gm.Cam.GroundUnderMouse(out var p))
                {
                    switch (Targeting)
                    {
                        case Mode.Reinforce:
                            if (ReinforceDef != null && gm.OrderReinforcement(player, ReinforceDef, p))
                                SFXManager.Play(Core.Audio.Sfx.UiConfirm);
                            else SFXManager.Play(Core.Audio.Sfx.UiDenied);
                            if (!Input.GetKey(KeyCode.LeftShift)) Cancel();
                            break;
                        case Mode.Orbital:
                            if (gm.OrbitalStrike(player, p)) Cancel();
                            break;
                        case Mode.Ability:
                            Abilities.UseAt(s_selected, AbilityId, p, EnemyUnderMouse(gm, player));
                            Cancel();
                            break;
                    }
                }
                return;
            }

            // ---- Выделение ----
            if (!ui && Input.GetMouseButtonDown(0))
            {
                if (AttackModifier && gm.Cam.GroundUnderMouse(out var ap))
                {
                    IssueMove(ap, true);
                    return;
                }
                _dragStart = Input.mousePosition;
                _dragging = true;
            }
            if (_dragging)
            {
                Vector2 cur = Input.mousePosition;
                DragRect = new Rect(Mathf.Min(_dragStart.x, cur.x), Mathf.Min(_dragStart.y, cur.y), Mathf.Abs(cur.x - _dragStart.x), Mathf.Abs(cur.y - _dragStart.y));
                IsDragging = DragRect.width > 8f || DragRect.height > 8f;
                if (Input.GetMouseButtonUp(0))
                {
                    _dragging = false;
                    bool add = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
                    if (IsDragging) BoxSelect(gm, player, DragRect, add);
                    else ClickSelect(gm, player, add);
                    IsDragging = false;
                }
            }

            // ---- Приказы ----
            if (!ui && Input.GetMouseButtonDown(1) && s_selected.Count > 0)
            {
                var enemy = EnemyUnderMouse(gm, player);
                if (enemy != null)
                {
                    foreach (var s in s_selected) s.OrderAttack(enemy);
                    gm.Hud?.Ping(enemy.Center, new Color(1f, 0.3f, 0.25f));
                    GroundSfx.Radio(0.75f);
                }
                else if (StructureUnderMouse(gm, player, out var d))
                {
                    foreach (var s in s_selected) s.OrderAttack(d);
                    gm.Hud?.Ping(d.Pos, new Color(1f, 0.3f, 0.25f));
                    GroundSfx.Radio(0.75f);
                }
                else if (gm.Cam.GroundUnderMouse(out var p)) IssueMove(p, false);
            }
        }

        private void IssueMove(Vector3 p, bool attack)
        {
            var gm = G;
            // Отряды встают в линию поперёк направления движения
            Vector3 c = Vector3.zero;
            foreach (var s in s_selected) c += s.Center;
            c /= Mathf.Max(1, s_selected.Count);
            Vector3 fwd = (p - c).WithY(0f);
            if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.forward;
            Vector3 right = Vector3.Cross(Vector3.up, fwd.normalized);
            for (int i = 0; i < s_selected.Count; i++)
            {
                float off = (i - (s_selected.Count - 1) * 0.5f) * 11f;
                s_selected[i].OrderMove(gm.Map.Clamp(p + right * off), attack);
            }
            gm.Hud?.Ping(p, attack ? new Color(1f, 0.55f, 0.2f) : new Color(0.4f, 1f, 0.6f));
            GroundSfx.Radio(0.7f);
        }

        private void UseAbility(int slot)
        {
            if (s_selected.Count == 0) return;
            var first = s_selected[0];
            if (slot >= first.Def.Abilities.Length) return;
            string id = first.Def.Abilities[slot];
            if (Abilities.Instant(id)) { Abilities.UseAt(s_selected, id, first.Center, null); return; }
            Targeting = Mode.Ability;
            AbilityId = id;
        }

        private void HandleGroups()
        {
            for (int k = 1; k <= 9; k++)
            {
                if (!Input.GetKeyDown(KeyCode.Alpha0 + k)) continue;
                bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
                if (ctrl) { _groups[k] = new List<Squad>(s_selected); G.Hud?.Flash(Loc.F("Группа {0}", k), Color.white); continue; }
                if (!_groups.TryGetValue(k, out var g)) continue;
                g.RemoveAll(s => s.Destroyed);
                Select(null, false);
                foreach (var s in g) Select(s, true);
                // Двойное нажатие — камера к группе
                if (_lastGroupKey == k && Time.unscaledTime - _lastGroupTime < 0.4f && g.Count > 0) G.Cam.LookAt(g[0].Center);
                _lastGroupKey = k;
                _lastGroupTime = Time.unscaledTime;
            }
        }

        // ---- Поиск под курсором ----

        private static Squad SquadUnderMouse(GroundBattleManager gm, BattleSide side, bool enemy)
        {
            Vector2 mp = Input.mousePosition;
            Squad best = null;
            float bd = 30f * 30f;
            var sides = enemy ? gm.Sides[1 - side.Index] : side;
            foreach (var sq in sides.Squads)
            {
                if (sq.Destroyed || enemy && !sq.VisibleTo[side.Index]) continue;
                foreach (var m in sq.Members)
                {
                    if (m == null || m.Dead) continue;
                    var sp = gm.Cam.Cam.WorldToScreenPoint(m.Pos + Vector3.up * m.Rig.Height * 0.5f);
                    if (sp.z < 0f) continue;
                    float r = m.IsVehicle ? 46f : 30f;
                    float d = ((Vector2)sp - mp).sqrMagnitude;
                    if (d < bd && d < r * r) { bd = d; best = sq; }
                }
            }
            return best;
        }

        private static Squad EnemyUnderMouse(GroundBattleManager gm, BattleSide player) => SquadUnderMouse(gm, player, true);

        private static bool StructureUnderMouse(GroundBattleManager gm, BattleSide player, out Destructible d)
        {
            d = null;
            var ray = gm.Cam.Cam.ScreenPointToRay(Input.mousePosition);
            if (!Physics.Raycast(ray, out var hit, 1500f, ~(1 << 2), QueryTriggerInteraction.Ignore)) return false;
            foreach (var x in gm.Map.Destructibles)
            {
                if (x.Dead || x.Side != 1 - player.Index) continue;
                if (x.Colliders.Contains(hit.collider)) { d = x; return true; }
            }
            return false;
        }

        private void ClickSelect(GroundBattleManager gm, BattleSide player, bool add)
        {
            var sq = SquadUnderMouse(gm, player, false);
            if (sq != null && _lastClicked == sq && Time.unscaledTime - _lastClick < 0.35f)
            {
                // Двойной клик: все такие же отряды на экране
                Select(null, false);
                foreach (var s in player.Squads)
                    if (!s.Destroyed && s.Def == sq.Def && OnScreen(gm, s.Center)) Select(s, true);
                return;
            }
            _lastClick = Time.unscaledTime;
            _lastClicked = sq;
            Select(sq, add);
            if (sq != null) GroundSfx.Radio(sq.Def.Vehicle ? 0.55f : 0.45f);
        }

        private static bool OnScreen(GroundBattleManager gm, Vector3 p)
        {
            var sp = gm.Cam.Cam.WorldToScreenPoint(p);
            return sp.z > 0f && sp.x >= 0f && sp.y >= 0f && sp.x <= Screen.width && sp.y <= Screen.height;
        }

        private void BoxSelect(GroundBattleManager gm, BattleSide player, Rect r, bool add)
        {
            if (!add) Select(null, false);
            foreach (var sq in player.Squads)
            {
                if (sq.Destroyed) continue;
                foreach (var m in sq.Members)
                {
                    if (m == null || m.Dead) continue;
                    var sp = gm.Cam.Cam.WorldToScreenPoint(m.Pos);
                    if (sp.z > 0f && r.Contains(new Vector2(sp.x, sp.y))) { Select(sq, true); break; }
                }
            }
        }
    }
}
