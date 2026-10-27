using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using StellarisClone.Core;
using StellarisClone.Rendering;
using Sfx = StellarisClone.Core.Audio.Sfx;

namespace StellarisClone.Cards
{
    /// <summary>Проигрывание хода: события правил → анимации, эффекты, звук. Вступление, автодоигрывание, итоги.</summary>
    public sealed partial class BattleScreen
    {
        private bool Fast => Input.GetKey(KeyCode.Space) || Input.GetMouseButton(1);

        private IEnumerator W(float s)
        {
            float k = _animSpeed * (Fast ? 4f : 1f);
            float t = s / Mathf.Max(0.1f, k);
            float end = Time.unscaledTime + t;
            while (Time.unscaledTime < end) yield return null;
        }

        // ==================== ВСТУПЛЕНИЕ ====================

        private IEnumerator Intro()
        {
            _busy = true;
            // Чёрный экран и заголовок
            var black = LGBuild.Panel(_overlay, "Black", new Color(0, 0, 0, 1f), raycast: true);
            black.rectTransform.Stretch();
            var title = LGBuild.Label(_overlay, _P == BattleState.Attacker ? Loc.T("ДЕСАНТ") : Loc.T("ОБОРОНА"), 86, new Color(1f, 0.85f, 0.5f, 0f), TextAnchor.MiddleCenter, bold: true);
            title.rectTransform.At(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 50), new Vector2(1600, 120));
            title.Track(6f);
            var sub = LGBuild.Label(_overlay, Loc.F("{0} · {1}", _setup.PlanetName, Loc.F("{0} карт против {1}", _setup.AttackerCards.Count, _setup.DefenderCards.Count)), 22, new Color(1, 1, 1, 0f), TextAnchor.MiddleCenter);
            sub.rectTransform.At(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -30), new Vector2(1600, 40));
            float t = 0f;
            while (t < 1f) { t += Time.unscaledDeltaTime * 2f; title.color = new Color(1f, 0.85f, 0.5f, t); sub.color = new Color(1, 1, 1, t * 0.8f); yield return null; }
            yield return W(0.7f);
            // Падение сквозь атмосферу: экран светлеет, капсулы бьют в стол
            t = 0f;
            while (t < 1f)
            {
                t += Time.unscaledDeltaTime * 1.6f;
                black.color = new Color(0, 0, 0, 1f - t);
                title.color = new Color(1f, 0.85f, 0.5f, 1f - t);
                sub.color = new Color(1, 1, 1, (1f - t) * 0.8f);
                title.rectTransform.localScale = Vector3.one * (1f + t * 0.3f);
                yield return null;
            }
            Destroy(black.gameObject); Destroy(title.gameObject); Destroy(sub.gameObject);
            for (int i = 0; i < 3; i++)
            {
                var land = _fx.Local(_rows[i, _P == BattleState.Attacker ? 1 : 0]);
                StartCoroutine(Pod(land + new Vector2(Random.Range(-120f, 120f), 0f), 0.9f));
                yield return W(0.16f);
            }
            yield return W(0.5f);
            yield return PlayEvents();
            _busy = false;
            Hint(Loc.T("Перетащите карты в секторы. Enter — закончить ход. Пробел или ПКМ — ускорить анимацию"));
        }

        /// <summary>Десантная капсула: огненный след сверху, удар, пыль и ударная волна.</summary>
        private IEnumerator Pod(Vector2 land, float scale)
        {
            var from = land + new Vector2(-260f, 900f);
            float dur = 0.42f;
            _fx.Projectile(from, land, new Color(1f, 0.7f, 0.35f), dur, 30f * scale);
            _fx.Line(from, land, new Color(1f, 0.55f, 0.2f, 0.5f), 16f * scale, dur + 0.25f);
            yield return W(dur);
            _fx.Explosion(land, 0.9f * scale);
            _fx.Dust(land, 1.4f * scale);
            _fx.Shake(10f * scale, 0.35f);
            SFXManager.Play(Sfx.ExplosionSmall, 0.8f);
        }

        // ==================== ХОД ====================

        private IEnumerator ResolveTurn()
        {
            _busy = true;
            HidePreview(null);
            Hint("");
            BattleAI.PlanTurn(_state, _E, _setup.AiSkill);
            // Карты врага ложатся рубашкой вверх
            foreach (var p in _state.Sides[_E].Pending)
            {
                var v = CardView.Create(_rows[p.sector, 0], p.card, _E, FactionOf(_E), BoardCard, true);
                v.SetFace(false, true);
                v.Rt.anchoredPosition = new Vector2(0, 220);
                v.OnHoverIn = ShowPreview; v.OnHoverOut = HidePreview;
                _pending[p.card] = v;
                LayoutAll(false);
                v.Punch(0.2f);
                SFXManager.Play(Sfx.DealPlace, 0.7f, 0.9f);
                yield return W(0.14f);
            }
            yield return W(0.35f);
            // Свои выложенные карты: метка «приказ отдан» гаснет
            foreach (var kv in _pending) if (kv.Value != null && kv.Value.Side == _P) { kv.Value.SetStatus(null, Color.clear); kv.Value.OnClick = null; }
            if (!_state.Over) _state.Resolve();
            yield return PlayEvents();
            _busy = false;
            if (!_state.Over) Hint(Loc.F("Ход {0}: у вас {1} очк. приказов", _state.Turn, _state.Sides[_P].Points));
        }

        private IEnumerator AutoRest()
        {
            _busy = true;
            float saved = _animSpeed;
            _animSpeed = 3f;
            _fx.Speed = 3f;
            // Свои выложенные карты остаются, остальное решает ИИ
            int guard = 0;
            while (!_state.Over && guard++ < 12)
            {
                BattleAI.PlanTurn(_state, _P, Mathf.Max(2, _setup.AiSkill));
                BattleAI.PlanTurn(_state, _E, _setup.AiSkill);
                foreach (var v in new List<CardView>(_hand)) if (v != null) Destroy(v.gameObject);
                _hand.Clear();
                foreach (var p in _state.Sides[_P].Pending)
                    if (!_pending.ContainsKey(p.card)) { var v = CardView.Create(_rows[p.sector, 1], p.card, _P, FactionOf(_P), BoardCard, true); _pending[p.card] = v; Hook(v); }
                foreach (var p in _state.Sides[_E].Pending)
                    if (!_pending.ContainsKey(p.card)) { var v = CardView.Create(_rows[p.sector, 0], p.card, _E, FactionOf(_E), BoardCard, true); v.SetFace(false, true); _pending[p.card] = v; Hook(v); }
                LayoutAll(false);
                if (!_state.Over) _state.Resolve();
                yield return PlayEvents();
            }
            _animSpeed = saved;
            _fx.Speed = saved;
            _busy = false;
        }

        // ==================== СОБЫТИЯ ====================

        private IEnumerator PlayEvents()
        {
            while (_evCursor < _state.Events.Count)
            {
                var e = _state.Events[_evCursor++];
                yield return Handle(e);
            }
            SyncViews();
            RefreshPanels();
            if (_state.Over) yield return Results();
        }

        private CardView ViewOf(int uid) => uid >= 0 && _units.TryGetValue(uid, out var v) && v != null ? v : null;

        private string SideName(int side) => side == _P ? Loc.T("Вы") : Loc.T("Враг");

        private IEnumerator Handle(BEvent e)
        {
            switch (e.T)
            {
                case EvType.TurnStart:
                    RefreshPanels();
                    yield return Banner(e.Amount >= BattleState.Turns ? Loc.T("ПОСЛЕДНИЙ ХОД") : Loc.F("ХОД {0}", e.Amount),
                        Loc.F("Очки приказов: {0}", _state.Sides[_P].Points), new Color(1f, 0.85f, 0.5f), 0.9f);
                    AddLog(Loc.F("<color=#F2C766>— Ход {0} —</color>", e.Amount));
                    break;

                case EvType.Draw:
                    if (e.Side == _P)
                    {
                        var c = FindUnshownHandCard(e.Def);
                        if (c != null) { AddHandCard(c, true); SFXManager.Play(Sfx.PanelSlide, 0.5f, 1.2f); yield return W(0.14f); }
                    }
                    break;

                case EvType.TrapSet:
                    if (e.Uid >= 0)
                    {
                        var u = _state.FindUnit(e.Uid);
                        if (u != null && _pending.TryGetValue(u.Card, out var pv) && pv != null)
                        {
                            _pending.Remove(u.Card);
                            pv.Uid = u.Uid;
                            _units[u.Uid] = pv;
                            if (u.Side == _P) { pv.SetAlpha(0.8f); pv.SetStatus(Loc.T("ЛОВУШКА"), new Color(1f, 0.4f, 0.3f)); }
                            pv.Punch(0.1f);
                            yield return W(0.15f);
                        }
                    }
                    break;

                case EvType.TrapFire: yield return OnTrapFire(e); break;
                case EvType.Log:
                    if (!string.IsNullOrEmpty(e.Text)) AddLog(e.Text);
                    break;
                case EvType.Reveal: yield return OnReveal(e); break;
                case EvType.OrderFx: yield return OnOrder(e); break;
                case EvType.Shot: yield return OnShot(e); break;
                case EvType.Damage: yield return OnDamage(e); break;
                case EvType.Cover:
                {
                    var v = ViewOf(e.Uid);
                    if (v != null)
                    {
                        var p = _fx.Local(v.Rt);
                        _fx.Number(p + new Vector2(0, 30), Loc.F("−{0} укр.", e.Amount), new Color(0.55f, 0.85f, 1f), 22, 0.8f);
                        _fx.Ring(p, new Color(0.5f, 0.8f, 1f, 0.8f), 40f, 150f, 0.3f);
                        SFXManager.Play(Sfx.HitShield, 0.5f);
                    }
                    yield return W(0.05f);
                    break;
                }
                case EvType.Death: yield return OnDeath(e); break;
                case EvType.Heal:
                {
                    var v = ViewOf(e.Uid);
                    if (v != null)
                    {
                        var p = _fx.Local(v.Rt);
                        _fx.Number(p, "+" + e.Amount, new Color(0.45f, 1f, 0.55f), 30, 0.9f);
                        _fx.Burst(p, 10, CardSprites.Dot, new Color(0.5f, 1f, 0.6f, 0.9f), 40f, 120f, 0.6f, 1f, 10f, 4f, -120f, 1f, 90f, 120f);
                        v.RefreshNumbers(v.ShownPower, v.ShownArmor + e.Amount, v.MaxArmor);
                    }
                    yield return W(0.1f);
                    break;
                }
                case EvType.Buff:
                {
                    var v = ViewOf(e.Uid);
                    if (v != null && e.Text == "cover")
                    {
                        _fx.Number(_fx.Local(v.Rt, new Vector2(0, 40)), Loc.T("+2 укрытие"), new Color(0.55f, 0.85f, 1f), 18, 0.9f);
                        _fx.Ring(_fx.Local(v.Rt), new Color(0.5f, 0.8f, 1f, 0.6f), 30f, 130f, 0.35f);
                    }
                    else if (v != null && e.Amount != 0)
                    {
                        var p = _fx.Local(v.Rt, new Vector2(-30, -40));
                        bool up = e.Amount > 0;
                        _fx.Number(p, (up ? "+" : "") + e.Amount + Loc.T(" сила"), up ? new Color(1f, 0.85f, 0.4f) : new Color(1f, 0.45f, 0.4f), 22, 0.9f);
                        _fx.Burst(p, 8, CardSprites.Spark, up ? new Color(1f, 0.85f, 0.4f) : new Color(1f, 0.4f, 0.35f), 60f, 180f, 0.3f, 0.6f, 14f, 4f, up ? -300f : 300f, 2f, up ? 90f : 270f, 60f, align: true, stretch: 2f);
                        v.RefreshNumbers(v.ShownPower + e.Amount, v.ShownArmor, v.MaxArmor);
                        v.GlowBoost = 1f;
                    }
                    yield return W(0.08f);
                    break;
                }
                case EvType.Status: yield return OnStatus(e); break;
                case EvType.Spawn: yield return OnSpawn(e); break;
                case EvType.Move: yield return OnMove(e); break;
                case EvType.Steal:
                {
                    var v = ViewOf(e.Uid);
                    var u = _state.FindUnit(e.Uid);
                    if (v != null && u != null)
                    {
                        _fx.Ring(_fx.Local(v.Rt), new Color(0.8f, 0.5f, 1f, 0.9f), 30f, 220f, 0.5f);
                        Reparent(v, _rows[u.Sector, u.Side == _P ? 1 : 0]);
                        v.Side = u.Side;
                        AddLog(Loc.F("{0} переходит на сторону: {1}", u.Def.Name, SideName(u.Side)));
                        SFXManager.Play(Sfx.DealAccepted);
                    }
                    yield return W(0.4f);
                    break;
                }
                case EvType.Control:
                {
                    RefreshSector(e.Sector);
                    var name = CardArt.SectorName(_state.Sectors[e.Sector].Type);
                    AddLog(e.Side == _P ? Loc.F("<color=#7CF29A>Сектор «{0}» ваш</color>", name) : Loc.F("<color=#FF8A80>Сектор «{0}» у врага</color>", name));
                    SFXManager.Play(e.Side == _P ? Sfx.SystemCaptured : Sfx.UiDanger, 0.7f);
                    _fx.Ring(_fx.Local(_sec[e.Sector].PowerBar), e.Side == _P ? new Color(0.45f, 1f, 0.6f) : new Color(1f, 0.4f, 0.35f), 60f, 520f, 0.6f);
                    yield return W(0.45f);
                    break;
                }
                case EvType.ShieldDown:
                {
                    int hq = e.Sector >= 0 ? e.Sector : _state.SectorIndex(SectorType.HQ);
                    if (hq >= 0)
                    {
                        var p = _fx.Local(_rows[hq, _P == BattleState.Defender ? 1 : 0]);
                        _fx.Energy(p, new Color(0.55f, 0.85f, 1f), 2f);
                        _fx.Ring(p, new Color(0.5f, 0.8f, 1f, 1f), 60f, 700f, 0.7f);
                        _fx.Sparks(p, new Color(0.6f, 0.85f, 1f), 50, 1.6f);
                        _fx.Flash(new Color(0.6f, 0.85f, 1f), 0.4f);
                        _fx.Shake(12f, 0.5f);
                        SFXManager.Play(Sfx.StarbaseDown);
                    }
                    AddLog(Loc.T("<color=#8FD3FF>Щит Штаба пал</color>"));
                    yield return Banner(Loc.T("ЩИТ ШТАБА ПАЛ"), _P == BattleState.Attacker ? Loc.T("Теперь возможен прорыв: 9+ силы в Штабе") : Loc.T("Враг может прорваться в Штаб"),
                        new Color(0.55f, 0.85f, 1f), 1.1f);
                    break;
                }
                case EvType.Weather: yield return OnWeather(e); break;
                case EvType.Breach:
                {
                    int hq = e.Sector;
                    var p = _fx.Local(_sec[hq].Col);
                    for (int i = 0; i < 6; i++)
                    {
                        _fx.Explosion(p + Random.insideUnitCircle * 160f, 1.3f);
                        _fx.Shake(22f, 0.4f);
                        SFXManager.Play(Sfx.ExplosionLarge, 0.8f);
                        yield return W(0.12f);
                    }
                    _fx.Flash(new Color(1f, 0.85f, 0.6f), 0.9f, 1.5f);
                    yield return Banner(Loc.T("ПРОРЫВ В ШТАБ!"), Loc.T("Командный центр колонии пал"), new Color(1f, 0.55f, 0.35f), 1.6f);
                    break;
                }
                case EvType.Retreat:
                    yield return Banner(e.Side == BattleState.Attacker ? Loc.T("ДЕСАНТ ОТСТУПАЕТ") : Loc.T("ГАРНИЗОН СДАЁТСЯ"), "", new Color(0.8f, 0.8f, 0.85f), 1f);
                    break;
            }
        }

        private BCard FindUnshownHandCard(CardDef def)
        {
            foreach (var c in _state.Sides[_P].Hand)
            {
                if (def != null && c.Def != def) continue;
                bool shown = false;
                foreach (var v in _hand) if (v != null && v.Card == c) { shown = true; break; }
                if (!shown) return c;
            }
            return null;
        }

        // ---------------------------------------------------------------- раскрытие

        private IEnumerator OnReveal(BEvent e)
        {
            var u = _state.FindUnit(e.Uid);
            if (u == null) yield break;
            CardView v;
            if (_pending.TryGetValue(u.Card, out v) && v != null) _pending.Remove(u.Card);
            else v = MakeUnitView(u, false);
            v.Uid = u.Uid;
            _units[u.Uid] = v;
            Hook(v);
            v.SetAlpha(1f);
            v.SetStatus(null, Color.clear);
            bool wasDown = !v.FaceUp;
            if (wasDown) { v.SetFace(true, false); yield return W(0.22f); }
            v.RefreshNumbers(u.BasePower, u.Armor, u.MaxArmor, false);
            v.Punch(u.Def.Has(Kw.Vehicle) ? 0.35f : 0.22f);
            var p = _fx.Local(v.Rt);
            bool heavy = u.Def.Has(Kw.Vehicle) || u.Def.Cost >= 5;
            _fx.Dust(p + new Vector2(0, -60), heavy ? 1.3f : 0.7f);
            _fx.Ring(p, new Color(1f, 0.9f, 0.7f, 0.6f), 40f, heavy ? 360f : 220f, 0.4f);
            if (u.Def.Kind == CardKind.Hero)
            {
                _fx.Burst(p, 30, CardSprites.Spark, new Color(1f, 0.85f, 0.45f), 200f, 520f, 0.4f, 0.9f, 20f, 6f, 0f, 2f, align: true, stretch: 3f);
                _fx.Flash(new Color(1f, 0.85f, 0.5f), 0.25f);
            }
            if (heavy) { _fx.Shake(heavy ? 9f : 4f, 0.3f); SFXManager.Play(Sfx.HitArmor, 0.9f, 0.6f); }
            SFXManager.Play(Sfx.DealPlace, 0.9f, heavy ? 0.8f : 1f);
            AddLog(Loc.F("{0}: {1} → {2}", SideName(u.Side), u.Def.Name, CardArt.SectorName(_state.Sectors[u.Sector].Type).ToLower()));
            LayoutAll(false);
            yield return W(heavy ? 0.4f : 0.28f);
        }

        // ---------------------------------------------------------------- ловушки

        private IEnumerator OnTrapFire(BEvent e)
        {
            var trap = ViewOf(e.Uid);
            var victim = ViewOf(e.Uid2);
            _units.Remove(e.Uid);
            if (trap != null)
            {
                trap.SetAlpha(1f);
                trap.SetStatus(null, Color.clear);
                if (!trap.FaceUp) { trap.SetFace(true, false); yield return W(0.2f); }
                trap.Punch(0.4f);
                trap.GlowBoost = 1.5f;
            }
            bool cleared = e.Text == "cleared";
            if (cleared)
            {
                if (trap != null) _fx.Sparks(_fx.Local(trap.Rt), new Color(1f, 0.85f, 0.45f), 20, 1f);
                SFXManager.Play(Sfx.HoloScan, 0.7f);
                AddLog(Loc.F("{0}: ловушка «{1}» обезврежена", SideName(e.Side), e.Def != null ? e.Def.Name : "?"));
                yield return W(0.3f);
            }
            else
            {
                var p = victim != null ? _fx.Local(victim.Rt) : trap != null ? _fx.Local(trap.Rt) : RowCenter(e.Sector, BattleState.Enemy(e.Side));
                _fx.Flash(new Color(1f, 0.25f, 0.15f), 0.35f);
                _fx.Explosion(p, 1.2f);
                _fx.Ring(p, new Color(1f, 0.35f, 0.25f, 1f), 30f, 420f, 0.5f);
                _fx.Shake(14f, 0.4f);
                SFXManager.Play(Sfx.ExplosionLarge, 0.8f);
                AddLog(Loc.F("<color=#FF8A80>Ловушка «{0}» сработала</color>", e.Def != null ? e.Def.Name : "?"));
                yield return Banner(Loc.T("ЛОВУШКА!"), e.Def != null ? e.Def.Name : "", new Color(1f, 0.4f, 0.3f), 0.8f);
            }
            if (trap != null) StartCoroutine(Dissolve(trap, new Color(1f, 0.45f, 0.35f)));
        }

        // ---------------------------------------------------------------- приказы

        private IEnumerator OnOrder(BEvent e)
        {
            CardView v = null;
            BCard key = null;
            foreach (var kv in _pending)
                if (kv.Value != null && kv.Value.Side == e.Side && kv.Key.Def == e.Def) { v = kv.Value; key = kv.Key; break; }
            if (key != null) _pending.Remove(key);
            if (v == null) v = CardView.Create(_rows[e.Sector, e.Side == _P ? 1 : 0], new BCard(e.Def), e.Side, FactionOf(e.Side), BoardCard, true);
            // Карта приказа поднимается над сектором и раскрывается
            Reparent(v, _fxLayer);
            _targets[v] = _fx.Local(_sec[e.Sector].Header, new Vector2(0, -150));
            _targetRot[v] = 0f;
            v.SetFace(true, false);
            v.SetAlpha(1f);
            v.Punch(0.4f);
            v.GlowBoost = 1.2f;
            SFXManager.Play(Sfx.AlertPop, 0.8f);
            AddLog(Loc.F("{0}: приказ «{1}»", SideName(e.Side), e.Def.Name));
            yield return W(0.45f);
            yield return OrderCinematic(e.Def, e.Side, e.Sector);
            // Карта сгорает
            StartCoroutine(Dissolve(v, CardArt.Frame(e.Def, 0, FactionOf(e.Side))));
        }

        private Vector2 RowCenter(int sector, int side) => _fx.Local(_rows[sector, side == _P ? 1 : 0]);

        private IEnumerator OrderCinematic(CardDef d, int side, int sector)
        {
            int enemy = BattleState.Enemy(side);
            var target = RowCenter(sector, enemy);
            var own = RowCenter(sector, side);
            var top = new Vector2(target.x, _fxLayer.rect.height * 0.5f + 60f);
            switch (d.Id)
            {
                case "O2":   // Орбитальный удар: прицел, столб света, ударная волна
                {
                    SFXManager.Play(Sfx.UiDanger);
                    for (int i = 0; i < 10; i++)
                    {
                        var p = target + new Vector2(Random.Range(-170f, 170f), Random.Range(-60f, 60f));
                        _fx.Line(top + new Vector2(Random.Range(-300f, 300f), 0), p, new Color(1f, 0.15f, 0.1f, 0.9f), 3f, 0.25f);
                        _fx.Burst(p, 1, CardSprites.Glow, new Color(1f, 0.2f, 0.1f, 0.9f), 0, 0, 0.25f, 0.25f, 24f, 10f);
                        yield return W(0.07f);
                    }
                    _fx.Line(top, target, new Color(1f, 1f, 1f, 1f), 160f, 0.55f);
                    _fx.Line(top, target, new Color(0.6f, 0.9f, 1f, 0.9f), 280f, 0.8f);
                    _fx.Flash(Color.white, 0.95f, 2.2f);
                    _fx.Shake(26f, 0.8f);
                    SFXManager.Play(Sfx.ExplosionLarge);
                    for (int i = 0; i < 4; i++) _fx.Explosion(target + Random.insideUnitCircle * 140f, 1.5f);
                    _fx.Ring(target, new Color(1f, 0.95f, 0.8f, 1f), 40f, 900f, 0.8f);
                    _fx.Burst(target, 40, CardSprites.Dot, new Color(1f, 0.6f, 0.2f, 1f), 100f, 600f, 1.2f, 2.6f, 8f, 3f, 260f, 0.8f, 90f, 160f);
                    yield return W(0.8f);
                    break;
                }
                case "O1":   // Огневой вал с кораблей
                {
                    SFXManager.Play(Sfx.WeaponMissile);
                    for (int i = 0; i < 10; i++)
                    {
                        var p = target + new Vector2(Random.Range(-190f, 190f), Random.Range(-50f, 50f));
                        _fx.Projectile(p + new Vector2(-500f, 700f), p, new Color(1f, 0.75f, 0.35f), 0.3f, 16f);
                        StartCoroutine(Later(0.3f, () => { _fx.Explosion(p, 0.7f); _fx.Shake(6f, 0.15f); SFXManager.Play(Sfx.ExplosionSmall, 0.5f); }));
                        yield return W(0.06f);
                    }
                    yield return W(0.4f);
                    break;
                }
                case "O3":
                    yield return Pod(own, 1f);
                    break;
                case "O5":
                    for (int i = 0; i < 4; i++) { _fx.Smoke(target + Random.insideUnitCircle * 140f, 2.4f, new Color(0.65f, 0.67f, 0.7f, 0.7f)); _fx.Smoke(own + Random.insideUnitCircle * 140f, 2.4f, new Color(0.65f, 0.67f, 0.7f, 0.7f)); }
                    SFXManager.Play(Sfx.Disengage);
                    yield return W(0.6f);
                    break;
                case "O6":
                    _fx.Burst(own, 26, CardSprites.Spark, new Color(1f, 0.85f, 0.4f), 120f, 380f, 0.5f, 0.9f, 16f, 4f, 0f, 2f, align: true, stretch: 2.5f);
                    SFXManager.Play(Sfx.ShipLaunched, 0.6f);
                    yield return W(0.4f);
                    break;
                case "O7":   // Диверсия: глитч и искры на сооружениях
                case "Q2":
                    for (int i = 0; i < 6; i++)
                    {
                        var p = target + new Vector2(Random.Range(-180f, 180f), Random.Range(-60f, 60f));
                        _fx.Sparks(p, new Color(0.5f, 0.95f, 1f), 14, 0.8f);
                        _fx.Line(p - new Vector2(80, 0), p + new Vector2(80, 0), new Color(0.4f, 1f, 1f, 0.7f), 6f, 0.12f);
                        yield return W(0.07f);
                    }
                    _fx.Flash(new Color(0.4f, 0.9f, 1f), 0.25f);
                    SFXManager.Play(Sfx.HoloScan);
                    yield return W(0.3f);
                    break;
                case "O9":   // ЭМИ
                    _fx.Energy(target, new Color(0.45f, 0.8f, 1f), 1.6f);
                    _fx.Ring(target, new Color(0.4f, 0.75f, 1f, 1f), 40f, 900f, 0.7f);
                    _fx.Ring(target, new Color(0.7f, 0.9f, 1f, 0.8f), 20f, 600f, 0.5f);
                    _fx.Sparks(target, new Color(0.55f, 0.85f, 1f), 40, 1.4f);
                    _fx.Flash(new Color(0.45f, 0.75f, 1f), 0.45f);
                    SFXManager.Play(Sfx.HitShield);
                    yield return W(0.6f);
                    break;
                case "I4":   // Пси-шторм: молнии по всему сектору
                    SFXManager.Play(Sfx.WeaponEnergy);
                    for (int i = 0; i < 14; i++)
                    {
                        var a = (Random.value < 0.5f ? target : own) + new Vector2(Random.Range(-200f, 200f), Random.Range(-90f, 90f));
                        var b = a + Random.insideUnitCircle * 160f;
                        _fx.Line(a, b, new Color(0.8f, 0.5f, 1f, 1f), 5f, 0.18f);
                        _fx.Burst(b, 4, CardSprites.Spark, new Color(0.85f, 0.6f, 1f), 100f, 300f, 0.2f, 0.4f, 12f, 4f, 0f, 2f, align: true, stretch: 2f);
                        yield return W(0.05f);
                    }
                    _fx.Energy(target, new Color(0.8f, 0.5f, 1f), 1.8f);
                    _fx.Flash(new Color(0.7f, 0.4f, 1f), 0.5f);
                    _fx.Shake(10f, 0.4f);
                    yield return W(0.3f);
                    break;
                case "T1":   // Споры
                    for (int i = 0; i < 5; i++) _fx.Smoke(target + Random.insideUnitCircle * 150f, 2f, new Color(0.45f, 0.95f, 0.35f, 0.5f));
                    _fx.Burst(target, 30, CardSprites.Dot, new Color(0.6f, 1f, 0.4f, 0.9f), 20f, 90f, 1.2f, 2f, 8f, 3f, -20f, 0.6f);
                    yield return W(0.6f);
                    break;
                case "X3":   // Децимация
                    _fx.Flash(new Color(0.8f, 0.05f, 0.05f), 0.35f);
                    SFXManager.Play(Sfx.WeaponKinetic);
                    yield return W(0.4f);
                    break;
                case "Q3":   // Ликвидация: снайперская линия
                    _fx.Line(own + new Vector2(0, -400), target, new Color(1f, 0.9f, 0.5f, 1f), 3f, 0.3f);
                    SFXManager.Play(Sfx.WeaponKinetic, 1f, 0.7f);
                    yield return W(0.3f);
                    break;
                case "N3":
                case "N4":
                    _fx.Ring(target, new Color(0.9f, 0.95f, 1f, 0.9f), 30f, 360f, 0.5f);
                    _fx.Burst(target, 30, CardSprites.Spark, new Color(0.9f, 0.95f, 1f), 100f, 300f, 0.4f, 0.8f, 12f, 4f, 0f, 2f, align: true, stretch: 2f);
                    SFXManager.Play(Sfx.HoloScan);
                    yield return W(0.5f);
                    break;
                default:
                {
                    var c = CardArt.Frame(d, 0, FactionOf(side));
                    _fx.Ring(own, c, 30f, 420f, 0.5f);
                    _fx.Burst(own, 20, CardSprites.Spark, c, 100f, 300f, 0.4f, 0.8f, 14f, 4f, 0f, 2f, align: true, stretch: 2f);
                    yield return W(0.4f);
                    break;
                }
            }
        }

        private IEnumerator Later(float s, System.Action a) { yield return W(s); a(); }

        // ---------------------------------------------------------------- перестрелка

        private Color ShotColor(int side, CardView src)
        {
            string f = FactionOf(side);
            if (f == "iridia") return new Color(0.8f, 0.55f, 1f);
            if (f == "nol" || f == "astrea") return new Color(0.5f, 0.9f, 1f);
            if (src != null && src.Card.Def.Has(Kw.Flame)) return new Color(1f, 0.5f, 0.1f);
            return new Color(1f, 0.78f, 0.4f);
        }

        private IEnumerator OnShot(BEvent e)
        {
            var src = ViewOf(e.Uid);
            var tgt = ViewOf(e.Uid2);
            if (src == null || tgt == null) yield break;
            var a = _fx.Local(src.Rt, new Vector2(0, src.Side == _P ? 60 : -60));
            var b = _fx.Local(tgt.Rt);
            var c = ShotColor(src.Side, src);
            var d = src.Card.Def;
            if (e.Text == "crush")
            {
                src.Punch(0.3f);
                _fx.Shake(14f, 0.35f);
                _fx.Dust(b, 1.4f);
                SFXManager.Play(Sfx.HitArmor, 1f, 0.5f);
                yield return W(0.2f);
                yield break;
            }
            bool lob = e.Text == "lob" || e.Text == "salvo";
            bool flame = d.Has(Kw.Flame);
            // Вспышка у ствола
            float shotAng = Mathf.Atan2(b.y - a.y, b.x - a.x) * Mathf.Rad2Deg;
            _fx.Muzzle(a, shotAng, c, d.Has(Kw.Vehicle) ? 1.3f : 0.8f);
            _fx.Burst(a, 4, CardSprites.Spark, c, 160f, 320f, 0.08f, 0.16f, 16f, 6f, 0f, 3f, shotAng, 30f, align: true, stretch: 2.5f);
            src.Punch(0.06f);
            if (flame)
            {
                // Струя пламени
                for (int i = 0; i < 3; i++) _fx.Burst(a, 8, CardSprites.Glow, new Color(1f, 0.55f, 0.15f, 0.9f), 500f, 800f, 0.25f, 0.35f, 26f, 60f, 0f, 1f,
                    Mathf.Atan2(b.y - a.y, b.x - a.x) * Mathf.Rad2Deg, 18f, end: new Color(0.4f, 0.1f, 0.05f, 0f));
                SFXManager.Play(Sfx.WeaponEnergy, 0.7f, 0.6f);
                yield return W(0.18f);
                yield break;
            }
            float dur = lob ? 0.42f : d.Has(Kw.Sniper) ? 0.06f : 0.14f;
            if (d.Has(Kw.Sniper)) _fx.Line(a, b, new Color(1f, 0.95f, 0.75f, 1f), 3f, 0.2f);
            _fx.Projectile(a, b, c, dur, d.Has(Kw.Vehicle) ? 22f : 14f, lob);
            SFXManager.Play(lob ? Sfx.WeaponMissile : (FactionOf(src.Side) == "iridia" || FactionOf(src.Side) == "nol" ? Sfx.WeaponEnergy : Sfx.WeaponKinetic), 0.45f);
            yield return W(Mathf.Min(dur, 0.12f));
        }

        private IEnumerator OnDamage(BEvent e)
        {
            var v = ViewOf(e.Uid);
            if (v == null) yield break;
            var p = _fx.Local(v.Rt);
            v.Hit(Mathf.Clamp(e.Amount / 3f, 0.5f, 1.5f));
            _fx.Number(p + new Vector2(Random.Range(-20f, 20f), 10), "−" + e.Amount, new Color(1f, 0.4f, 0.32f), 32 + Mathf.Min(16, e.Amount * 2), 0.9f);
            var def = v.Card.Def;
            if (def.Has(Kw.Vehicle) || def.Kind == CardKind.Structure || def.Has(Kw.Drone))
            {
                _fx.Sparks(p, new Color(1f, 0.8f, 0.45f), 12, 0.9f);
                if (e.Amount >= 3) _fx.Flipbook("fx_explosion", 6, 6, p + Random.insideUnitCircle * 20f, 110f, 0.7f, Color.white, 150f, Random.Range(0f, 360f));
            }
            else
                _fx.Blood(p, CardArt.Blood(FactionOf(v.Side)), 0.6f);
            SFXManager.Play(Sfx.HitArmor, 0.5f, Random.Range(0.9f, 1.15f));
            v.RefreshNumbers(v.ShownPower, v.ShownArmor - e.Amount, v.MaxArmor);
            yield return W(0.07f);
        }

        private IEnumerator OnDeath(BEvent e)
        {
            var v = ViewOf(e.Uid);
            if (v == null) yield break;
            _units.Remove(e.Uid);
            var p = _fx.Local(v.Rt);
            var def = v.Card.Def;
            bool hard = def.Has(Kw.Vehicle) || def.Kind == CardKind.Structure;
            if (hard)
            {
                _fx.Explosion(p, def.Cost >= 5 ? 1.6f : 1.1f);
                _fx.Shake(def.Cost >= 5 ? 18f : 10f, 0.4f);
                SFXManager.Play(def.Cost >= 5 ? Sfx.ExplosionLarge : Sfx.ExplosionSmall, 0.9f);
            }
            else if (def.Has(Kw.Drone))
            {
                _fx.Sparks(p, new Color(0.85f, 0.95f, 1f), 24, 1.1f);
                _fx.Smoke(p, 0.6f);
                SFXManager.Play(Sfx.ExplosionSmall, 0.5f, 1.4f);
            }
            else
            {
                _fx.Blood(p, CardArt.Blood(FactionOf(v.Side)), 1.5f);
                _fx.Dust(p + new Vector2(0, -50), 0.6f);
                SFXManager.Play(Sfx.HitArmor, 0.8f, 0.7f);
            }
            AddLog(Loc.F("{0} — <color=#FF8A80>{1} погиб</color>", SideName(v.Side), def.Name));
            StartCoroutine(Shatter(v, hard));
            yield return W(0.22f);
        }

        /// <summary>Гибель карты: техника разлетается обломками, пехота падает и темнеет.</summary>
        private IEnumerator Shatter(CardView v, bool hard)
        {
            if (v == null) yield break;
            _dying.Add(v);
            _targets.Remove(v);
            var p = _fx.Local(v.Rt);
            if (hard)
            {
                var col = CardArt.Frame(v.Card.Def, v.Card.Stars, v.SideFaction);
                _fx.Burst(p, 14, CardSprites.Round, new Color(col.r * 0.5f, col.g * 0.5f, col.b * 0.5f, 1f), 200f, 600f, 0.6f, 1.1f, 26f, 14f, 1400f, 0.6f, 90f, 160f);
            }
            float t = 0f;
            var start = v.Rt.localRotation;
            float spin = Random.Range(-25f, 25f);
            while (t < 1f && v != null)
            {
                t += Time.unscaledDeltaTime * 2.2f * _animSpeed;
                v.SetAlpha(1f - t);
                v.Rt.localRotation = start * Quaternion.Euler(0, 0, spin * t);
                v.Rt.localScale = Vector3.one * (hard ? 1f + t * 0.3f : 1f - t * 0.35f);
                v.Rt.anchoredPosition += new Vector2(0, hard ? 0f : -60f * Time.unscaledDeltaTime);
                yield return null;
            }
            _dying.Remove(v);
            if (v != null) Destroy(v.gameObject);
            LayoutAll(false);
        }

        private IEnumerator Dissolve(CardView v, Color c)
        {
            if (v == null) yield break;
            _dying.Add(v);
            _targets.Remove(v);
            var p = _fx.Local(v.Rt);
            _fx.Burst(p, 30, CardSprites.Dot, new Color(c.r, c.g, c.b, 1f), 40f, 220f, 0.6f, 1.2f, 8f, 2f, -80f, 1f);
            float t = 0f;
            while (t < 1f && v != null)
            {
                t += Time.unscaledDeltaTime * 2.4f * _animSpeed;
                v.SetAlpha(1f - t);
                v.Rt.localScale = Vector3.one * (1f + t * 0.25f);
                yield return null;
            }
            _dying.Remove(v);
            if (v != null) Destroy(v.gameObject);
        }

        private IEnumerator OnStatus(BEvent e)
        {
            if (e.Text == "smoke")
            {
                yield break;   // дым уже показан приказом
            }
            var v = ViewOf(e.Uid);
            if (v == null) yield break;
            string txt; Color c;
            switch (e.Text)
            {
                case "suppress": txt = Loc.T("ПОДАВЛЕН"); c = new Color(1f, 0.6f, 0.3f); break;
                case "disable": txt = Loc.T("ОТКЛЮЧЁН"); c = new Color(0.5f, 0.85f, 1f); break;
                case "immobile": txt = Loc.T("СКОВАН"); c = new Color(0.5f, 1f, 0.4f); break;
                case "immune": txt = Loc.T("ЭЛИТА: НЕ ДРОГНУЛА"); c = new Color(1f, 0.85f, 0.4f); break;
                case "foresight": txt = Loc.T("ВИДИТ РУКУ ВРАГА"); c = new Color(0.8f, 0.55f, 1f); break;
                default: txt = e.Text; c = Color.white; break;
            }
            v.SetStatus(txt, c);
            _fx.Number(_fx.Local(v.Rt, new Vector2(0, 50)), txt, c, 18, 1f);
            yield return W(0.18f);
        }

        private IEnumerator OnSpawn(BEvent e)
        {
            var u = _state.FindUnit(e.Uid);
            if (u == null) yield break;
            var row = _rows[u.Sector, u.Side == _P ? 1 : 0];
            var land = _fx.Local(row);
            if (e.Text == "pod") yield return Pod(land, 1f);
            var v = MakeUnitView(u, true);
            v.Rt.anchoredPosition = new Vector2(0, e.Text == "apc" ? 0 : 240);
            v.Punch(0.35f);
            _fx.Dust(land, 0.9f);
            SFXManager.Play(Sfx.DealPlace);
            AddLog(Loc.F("{0}: {1} в строю", SideName(u.Side), u.Def.Name));
            LayoutAll(false);
            yield return W(0.3f);
        }

        private IEnumerator OnMove(BEvent e)
        {
            var v = ViewOf(e.Uid);
            if (v == null) yield break;
            if (e.Amount < 0)
            {
                // Ушёл со стола живым: сдался / вернулся в руку
                _units.Remove(e.Uid);
                AddLog(e.Text == "rewind" ? Loc.F("{0} вернулся в руку", v.Card.Def.Name) : Loc.F("{0} сложил оружие", v.Card.Def.Name));
                StartCoroutine(Dissolve(v, new Color(0.9f, 0.9f, 1f)));
                yield return W(0.3f);
                yield break;
            }
            var u = _state.FindUnit(e.Uid);
            if (u == null) yield break;
            Reparent(v, _rows[u.Sector, u.Side == _P ? 1 : 0]);
            _fx.Burst(_fx.Local(v.Rt), 12, CardSprites.Spark, new Color(0.5f, 1f, 0.6f), 100f, 260f, 0.3f, 0.5f, 12f, 4f, 0f, 2f, align: true, stretch: 2f);
            AddLog(Loc.F("{0} обходит с фланга", u.Def.Name));
            LayoutAll(false);
            yield return W(0.4f);
        }

        private void Reparent(CardView v, RectTransform parent)
        {
            if (v == null) return;
            v.Rt.SetParent(parent, true);
            v.Rt.SetAsLastSibling();
        }

        // ---------------------------------------------------------------- погода

        private IEnumerator OnWeather(BEvent e)
        {
            RefreshPanels();
            Color c = _setup.Terrain == Terrain.Molten ? new Color(1f, 0.5f, 0.2f) : _setup.Terrain == Terrain.Desert ? new Color(1f, 0.8f, 0.5f)
                    : _setup.Terrain == Terrain.Barren ? new Color(0.8f, 0.85f, 1f) : new Color(0.6f, 0.8f, 1f);
            SFXManager.Play(Sfx.HoloScan);
            if (_setup.Terrain == Terrain.Molten && e.Sector >= 0)
            {
                var p = _fx.Local(_sec[e.Sector].Col);
                for (int i = 0; i < 5; i++) _fx.Explosion(p + Random.insideUnitCircle * 180f, 1.1f);
                _fx.Burst(p, 40, CardSprites.Dot, new Color(1f, 0.5f, 0.15f, 1f), 200f, 700f, 0.8f, 1.6f, 10f, 4f, 900f, 0.4f, 90f, 120f);
                _fx.Shake(18f, 0.6f);
                SFXManager.Play(Sfx.ExplosionLarge);
            }
            if (_setup.Terrain == Terrain.Desert)
                for (int i = 0; i < 12; i++) _fx.Smoke(new Vector2(-_fxLayer.rect.width * 0.5f, Random.Range(-300f, 300f)), 3f, new Color(0.85f, 0.65f, 0.4f, 0.35f));
            AddLog(Loc.F("<color=#9FD0FF>Погода: {0}</color>", _state.WeatherName ?? e.Text));
            yield return Banner(_state.WeatherName ?? e.Text, _state.WeatherText ?? "", c, 1.4f);
        }

        // ---------------------------------------------------------------- баннер

        private IEnumerator Banner(string title, string sub, Color c, float dur)
        {
            var band = LGBuild.Panel(_overlay, "Banner", new Color(0.01f, 0.02f, 0.04f, 0f));
            band.rectTransform.At(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 40), new Vector2(2400, 0));
            band.sprite = CardSprites.RoundGlow;
            band.type = Image.Type.Sliced;
            var line1 = LGBuild.Panel(band.rectTransform, "L1", new Color(c.r, c.g, c.b, 0.9f));
            line1.sprite = CardSprites.Beam;
            line1.rectTransform.anchorMin = new Vector2(0.15f, 1); line1.rectTransform.anchorMax = new Vector2(0.85f, 1); line1.rectTransform.sizeDelta = new Vector2(0, 3);
            var line2 = LGBuild.Panel(band.rectTransform, "L2", new Color(c.r, c.g, c.b, 0.9f));
            line2.sprite = CardSprites.Beam;
            line2.rectTransform.anchorMin = new Vector2(0.15f, 0); line2.rectTransform.anchorMax = new Vector2(0.85f, 0); line2.rectTransform.sizeDelta = new Vector2(0, 3);
            var flare = CardSkin.Simple(band.rectTransform, "Flare", "k_flare", new Color(c.r, c.g, c.b, 0.9f));
            flare.rectTransform.At(new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(700, 160));
            var t = CardSkin.TitleText(band.rectTransform, title, 58, new Color(c.r, c.g, c.b, 0f), TextAnchor.MiddleCenter, 3f);
            t.rectTransform.Stretch(0, 22, 0, 0);
            t.Track(4f);
            var s = LGBuild.Label(band.rectTransform, sub, 18, new Color(1, 1, 1, 0f), TextAnchor.LowerCenter);
            s.rectTransform.Stretch(0, 12, 0, 0);
            SFXManager.Play(Sfx.AlertPop, 0.6f);
            float total = dur / Mathf.Max(0.1f, _animSpeed * (Fast ? 3f : 1f));
            float time = 0f;
            while (time < total)
            {
                time += Time.unscaledDeltaTime;
                float k = time / total;
                float a = k < 0.2f ? k / 0.2f : k > 0.75f ? 1f - (k - 0.75f) / 0.25f : 1f;
                band.rectTransform.sizeDelta = new Vector2(2400, Mathf.Lerp(0, 120, Mathf.Min(1f, k / 0.15f)) * (k > 0.85f ? 1f - (k - 0.85f) / 0.15f : 1f));
                band.color = new Color(0.01f, 0.02f, 0.04f, 0.85f * a);
                flare.color = new Color(c.r, c.g, c.b, 0.9f * a);
                t.color = new Color(c.r, c.g, c.b, a);
                s.color = new Color(1, 1, 1, a * 0.85f);
                t.rectTransform.localScale = Vector3.one * (1.15f - 0.15f * Mathf.Min(1f, k / 0.25f));
                yield return null;
            }
            Destroy(band.gameObject);
        }

        // ==================== СИНХРОНИЗАЦИЯ ====================

        private void SyncViews()
        {
            foreach (var kv in new List<KeyValuePair<int, CardView>>(_units))
            {
                var u = _state.FindUnit(kv.Key);
                var v = kv.Value;
                if (v == null) { _units.Remove(kv.Key); continue; }
                if (u == null || u.Dead)
                {
                    _units.Remove(kv.Key);
                    if (!_dying.Contains(v)) StartCoroutine(Shatter(v, false));
                    continue;
                }
                v.RefreshNumbers(_state.Power(u), u.Armor, u.MaxArmor, false);
                string st = u.Suppressed > 0 ? Loc.T("ПОДАВЛЕН") : u.Disabled > 0 ? Loc.T("ОТКЛЮЧЁН") : u.Immobile > 0 ? Loc.T("СКОВАН")
                          : u.Has(Kw.NeedsPower) && !_state.DefenderPowered ? Loc.T("НЕТ ЭНЕРГИИ") : null;
                if (!u.IsTrap) v.SetStatus(st, new Color(1f, 0.6f, 0.3f));
            }
            // Карты, оставшиеся «отданными приказами», — уже разыграны правилами
            foreach (var kv in new List<KeyValuePair<BCard, CardView>>(_pending))
                if (kv.Value != null && !InPending(kv.Key)) { StartCoroutine(Dissolve(kv.Value, Color.white)); _pending.Remove(kv.Key); }
            // Рука: всё, что есть в руке по правилам, — на экране
            foreach (var c in _state.Sides[_P].Hand)
            {
                bool shown = false;
                foreach (var v in _hand) if (v != null && v.Card == c) { shown = true; break; }
                if (!shown) AddHandCard(c, true);
            }
            for (int i = _hand.Count - 1; i >= 0; i--)
                if (_hand[i] == null || !_state.Sides[_P].Hand.Contains(_hand[i].Card)) { if (_hand[i] != null) Destroy(_hand[i].gameObject); _hand.RemoveAt(i); }
            LayoutHand(false);
            LayoutAll(false);
        }

        private bool InPending(BCard c)
        {
            foreach (var s in _state.Sides) foreach (var p in s.Pending) if (p.card == c) return true;
            return false;
        }

        // ==================== ИТОГИ ====================

        private IEnumerator Results()
        {
            _busy = true;
            yield return W(0.6f);
            var r = _state.Result;
            bool win = (_P == BattleState.Attacker) == r.AttackerWon;
            SFXManager.Play(win ? Sfx.BattleVictory : Sfx.BattleDefeat);
            if (win) { _fx.Flash(new Color(1f, 0.9f, 0.6f), 0.5f, 1f); for (int i = 0; i < 4; i++) _fx.Sparks(new Vector2(Random.Range(-500f, 500f), Random.Range(-200f, 200f)), new Color(1f, 0.85f, 0.45f), 40, 1.5f); }

            var dim = LGBuild.Panel(_overlay, "ResultDim", new Color(0, 0, 0, 0.72f), raycast: true);
            dim.rectTransform.Stretch();
            var accent = win ? new Color(1f, 0.82f, 0.4f) : new Color(1f, 0.45f, 0.4f);
            var rays = CardSkin.Simple(_overlay, "Rays", "k_star", new Color(accent.r, accent.g, accent.b, win ? 0.35f : 0.12f));
            rays.rectTransform.At(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 230), new Vector2(1400, 1400));
            var box = CardSkin.Sliced(_overlay, "Result", "panel_plates", Color.white, 0.7f);
            box.raycastTarget = true;
            box.rectTransform.At(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(880, 560));
            var bframe = CardSkin.Sliced(box.rectTransform, "Frame", "frame_card", accent, 0.6f);
            bframe.rectTransform.Stretch();
            LG.Motion(box.gameObject, LGAppear.Kind.Pop);

            string head;
            if (_P == BattleState.Attacker) head = r.AttackerWon ? Loc.T("ПЛАНЕТА ВЗЯТА") : r.Retreated ? Loc.T("ДЕСАНТ ОТСТУПИЛ") : Loc.T("ШТУРМ ОТБИТ");
            else head = r.AttackerWon ? Loc.T("КОЛОНИЯ ПАЛА") : Loc.T("ОБОРОНА УСТОЯЛА");
            var t = CardSkin.TitleText(box.rectTransform, head, 50, accent, TextAnchor.UpperCenter, 3f);
            t.rectTransform.Stretch(20, 0, 20, 32);
            t.Track(4f);
            var sub = LGBuild.Label(box.rectTransform, Loc.F("{0} · {1} {2}{3}", _setup.PlanetName, r.Turns, Loc.T("ход."), r.Breach ? Loc.T(" · прорыв в Штаб") : ""), 15, UIManager.DS.TextMuted, TextAnchor.UpperCenter);
            sub.rectTransform.Stretch(20, 0, 20, 92);

            int myLost = r.LostCount[_P], enLost = r.LostCount[_E];
            int veterans = 0, wounded = 0;
            foreach (var s in r.Survivors) if (s.side == _P && s.fought) { veterans++; if (s.hp < 0.5f) wounded++; }
            string Stat(string label, string val) => $"<color=#8AA2A8>{label}</color>  <b>{val}</b>";
            var lines = new List<string>
            {
                Stat(Loc.T("Ваши потери:"), Loc.F("{0} карт (навсегда)", myLost)),
                Stat(Loc.T("Потери врага:"), Loc.F("{0} карт", enLost)),
                Stat(Loc.T("Выжившие ветераны:"), Loc.F("{0} получат звезду ★", veterans)),
            };
            if (wounded > 0) lines.Add(Stat(Loc.T("Ранены:"), Loc.F("{0} — лечение {1} дн.", wounded, ArmyManager.WoundDaysAfterBattle)));
            if (r.MilitiaKilled > 0) lines.Add(Stat(Loc.T("Погибло жителей (ополчение):"), r.MilitiaKilled.ToString()));
            if (r.Ruined.Count > 0) lines.Add(Stat(Loc.T("Разрушено районов:"), r.Ruined.Count.ToString()));
            if (r.Trophy != null && _P == BattleState.Attacker && r.AttackerWon) lines.Add(Stat(Loc.T("Трофей:"), r.Trophy.Name));
            var body = LGBuild.Label(box.rectTransform, string.Join("\n", lines), 18, Color.white, TextAnchor.UpperLeft);
            body.rectTransform.Stretch(110, 120, 60, 150);
            body.lineSpacing = 1.35f;

            var brt = LGBuild.Rect(box.rectTransform, "Continue");
            brt.At(new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0, 34), new Vector2(300, 76));
            var bimg = brt.gameObject.AddComponent<Image>();
            bimg.sprite = CardSkin.Spr(win ? "btn_end_green" : "btn_end_amber");
            bimg.type = Image.Type.Sliced;
            bimg.pixelsPerUnitMultiplier = 2.4f;
            var btn = brt.gameObject.AddComponent<Button>();
            btn.targetGraphic = bimg;
            btn.onClick.AddListener(Close);
            var bl = CardSkin.TitleText(brt, Loc.T("ПРОДОЛЖИТЬ"), 20, Color.white, TextAnchor.MiddleCenter, 2f);
            bl.rectTransform.Stretch();
            _busy = false;
        }
    }
}
