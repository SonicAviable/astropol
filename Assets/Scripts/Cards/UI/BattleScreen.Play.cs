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

        /// <summary>Темп боя: множитель длительности всех пауз (1 — быстро, больше — размеренно, «кинематографично»).</summary>
        private const float Pace = 1.45f;

        private IEnumerator W(float s)
        {
            float k = _animSpeed * (Fast ? 4f : 1f);
            float t = s * Pace / Mathf.Max(0.1f, k);
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
            yield return MulliganPhase();
            _busy = false;
            StartCoroutine(Callout(Loc.T("ВАШ ХОД"), CalloutGold));
            Hint(_P == BattleState.Attacker
                ? Loc.T("Сначала секторы держит гарнизон — так и задумано: за 8 ходов отбейте 2 из 3 · перетащите карты в секторы · Enter — конец хода")
                : Loc.T("Перетащите карты в секторы · золотой меч — урон за ход, красный щит — броня · Enter — конец хода"));
        }

        /// <summary>Десантная капсула: огненный след сверху, удар, пыль и ударная волна.</summary>
        private IEnumerator Pod(Vector2 land, float scale)
        {
            var from = land + new Vector2(-260f, 900f);
            float dur = 0.42f;
            _fx.Projectile(from, land, new Color(1f, 0.7f, 0.35f), dur * Pace, 30f * scale);
            _fx.Line(from, land, new Color(1f, 0.55f, 0.2f, 0.5f), 16f * scale, (dur + 0.25f) * Pace);
            yield return W(dur);
            _fx.Particles?.Play(CardParticles.Fx.Dust, land, scale);
            _fx.Explosion(land, 0.9f * scale);
            _fx.Dust(land, 1.4f * scale);
            _fx.Shake(10f * scale, 0.35f);
            SFXManager.Play(Sfx.BoomSmall, 0.8f, 0.85f, Pan(land));
            SFXManager.Play(Sfx.CardSlam, 0.7f, 0.8f, Pan(land));
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
                FlyFx(v, new Color(1f, 0.45f, 0.35f));
                SFXManager.Play(Sfx.CardPlace, 0.7f, 0.92f);
                yield return W(0.14f);
            }
            yield return W(0.35f);
            // Свои выложенные карты: метка «приказ отдан» гаснет
            foreach (var kv in _pending) if (kv.Value != null && kv.Value.Side == _P) { kv.Value.SetStatus(null, Color.clear); kv.Value.OnClick = null; }
            if (!_state.Over) _state.Resolve();
            StartCoroutine(Callout(Loc.T("РАСКРЫТИЕ"), new Color(1f, 0.6f, 0.45f), 0.8f));
            yield return PlayEvents();
            _busy = false;
            if (!_state.Over) StartCoroutine(Callout(Loc.T("ВАШ ХОД"), CalloutGold));
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
                    _focusSector = -1;
                    RefreshPanels();
                    yield return Banner(e.Amount >= BattleState.Turns ? Loc.T("ПОСЛЕДНИЙ ХОД") : Loc.F("ХОД {0}", e.Amount),
                        Loc.F("Очки приказов: {0}", _state.Sides[_P].Points), new Color(1f, 0.85f, 0.5f), 0.9f);
                    AddLog(Loc.F("<color=#F2C766>— Ход {0} —</color>", e.Amount));
                    break;

                case EvType.Draw:
                    if (e.Side == _P)
                    {
                        var c = FindUnshownHandCard(e.Def);
                        if (c != null)
                        {
                            AddHandCard(c, true);
                            SFXManager.Play(Sfx.CardDraw, 0.8f);
                            if (e.Text == "reinforce")
                            {
                                AddLog(Loc.F("<color=#7CF2D8>Подкрепления: {0}</color>", e.Def.Name));
                                SFXManager.Play(Sfx.Radio, 0.7f);
                            }
                            yield return W(0.14f);
                        }
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
                        SFXManager.Play(Sfx.ImpactShield, 0.5f);
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
                        _fx.Number(p, "+" + e.Amount, new Color(0.45f, 1f, 0.55f), 42, 1f);
                        v.EdgeFlash(new Color(0.4f, 1f, 0.55f), 1f);
                        BuffFx(v, new Color(0.35f, 1f, 0.5f), 0.9f);
                        SFXManager.Play(Sfx.FxHeal, 0.9f, Random.Range(0.96f, 1.06f), Pan(p));
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
                        _fx.Number(_fx.Local(v.Rt, new Vector2(0, 40)), Loc.T("+2 укрытие"), new Color(0.55f, 0.85f, 1f), 26, 1f);
                        v.EdgeFlash(new Color(0.5f, 0.82f, 1f), 0.9f);
                        ShieldFx(_fx.Local(v.Rt), new Color(0.45f, 0.8f, 1f), 230f);
                        SFXManager.Play(Sfx.FxShieldUp, 0.9f, Random.Range(0.97f, 1.04f), Pan(_fx.Local(v.Rt)));
                    }
                    else if (v != null && e.Amount != 0)
                    {
                        var p = _fx.Local(v.Rt, new Vector2(-30, -40));
                        bool up = e.Amount > 0;
                        _fx.Number(p, (up ? "+" : "") + e.Amount + Loc.T(" сила"), up ? new Color(1f, 0.85f, 0.4f) : new Color(1f, 0.45f, 0.4f), 34, 1f);
                        v.EdgeFlash(up ? new Color(1f, 0.82f, 0.35f) : new Color(1f, 0.35f, 0.3f), 1f);
                        if (up) BuffFx(v, new Color(1f, 0.78f, 0.3f));
                        else _fx.Play(UIFx.Fx2.Impact, _fx.Local(v.Rt), new Color(1f, 0.3f, 0.25f), 170f, 0.4f, 210f, Random.Range(0f, 360f));
                        // Чем больше прибавка, тем ниже и весомее звук
                        float pitch = Mathf.Clamp(1.08f - 0.04f * Mathf.Abs(e.Amount), 0.9f, 1.06f) * Random.Range(0.97f, 1.03f);
                        SFXManager.Play(up ? Sfx.FxBuff : Sfx.FxDebuff, 0.9f, pitch, Pan(p));
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
                        SFXManager.Play(Sfx.FxPsi, 0.8f, 1.05f, Pan(_fx.Local(v.Rt)));
                        SFXManager.Play(Sfx.Radio, 0.5f);
                        SFXManager.Play(Sfx.CardFlip, 0.7f, 0.9f);
                    }
                    yield return W(0.4f);
                    break;
                }
                case EvType.Control:
                {
                    RefreshSector(e.Sector);
                    var name = CardArt.SectorName(_state.Sectors[e.Sector].Type);
                    AddLog(e.Side == _P ? Loc.F("<color=#7CF29A>Сектор «{0}» ваш</color>", name) : Loc.F("<color=#FF8A80>Сектор «{0}» у врага</color>", name));
                    SFXManager.Play(e.Side == _P ? Sfx.FxCapture : Sfx.FxLost, 1f, 1f, Pan(_fx.Local(_sec[e.Sector].PowerBar)));
                    SFXManager.Play(Sfx.Radio, 0.5f, e.Side == _P ? 1f : 0.92f);
                    SFXManager.Play(Sfx.CardSlam, 0.6f, 0.7f);
                    _fx.Ring(_fx.Local(_sec[e.Sector].PowerBar), e.Side == _P ? new Color(0.45f, 1f, 0.6f) : new Color(1f, 0.4f, 0.35f), 60f, 520f, 0.6f);
                    // Волна цвета на весь стол: сектор взят — бирюзой, потерян — красным
                    _fx.Flash(e.Side == _P ? new Color(0.35f, 1f, 0.75f) : new Color(1f, 0.28f, 0.22f), 0.2f, 1.4f);
                    VortexFx(_fx.Local(_sec[e.Sector].PowerBar), e.Side == _P ? new Color(0.45f, 1f, 0.65f) : new Color(1f, 0.35f, 0.3f), 0.8f);
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
                        ShieldFx(p, new Color(0.5f, 0.82f, 1f), 640f);
                        _fx.Play(UIFx.Fx2.Lightning, p, new Color(0.55f, 0.85f, 1f), 520f, 0.7f, 600f, Random.Range(0f, 360f));
                        _arc?.RingWave(p, new Color(0.55f, 0.9f, 1f, 1f), 60f, 620f, 12f, 0.7f, 0.6f);
                        _arc?.Spikes(p, new Color(0.6f, 0.9f, 1f), 14, 320f, 9f, 0.4f);
                        _fx.Sparks(p, new Color(0.6f, 0.85f, 1f), 50, 1.6f);
                        _fx.Flash(new Color(0.6f, 0.85f, 1f), 0.4f);
                        _fx.Shake(12f, 0.5f);
                        SFXManager.Play(Sfx.FxShatter, 1f, 0.9f);
                        SFXManager.Play(Sfx.ElecZap, 0.8f, 0.7f);
                        SFXManager.PlayDelayed(Sfx.BoomSmall, 0.1f, 0.8f, 0.8f);
                        SFXManager.Play(Sfx.ImpactShield, 0.7f, 0.7f);
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
                    VortexFx(p, new Color(1f, 0.3f, 0.2f), 1.6f);
                    for (int i = 0; i < 6; i++)
                    {
                        _fx.Explosion(p + Random.insideUnitCircle * 160f, 1.3f);
                        _fx.Shake(22f, 0.4f);
                        Kick(0.02f);
                        SFXManager.Play(Sfx.Boom, 0.9f);
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
            if (Showcased(u.Def, u.Card.Stars))
            {
                // Розыгрыш крупным планом; карта на столе ждёт его невидимой и встаёт уже лицом
                v.SetAlpha(0f);
                yield return Showcase(v, u.Card, u.Side);
                if (v != null) { v.SetAlpha(1f); v.SetFace(true, true); }
            }
            bool wasDown = !v.FaceUp;
            if (wasDown) { v.SetFace(true, false); SFXManager.Play(Sfx.CardFlip, 0.8f, Random.Range(0.95f, 1.08f), Pan(_fx.Local(v.Rt))); yield return W(0.22f); }
            v.RefreshNumbers(u.BasePower, u.Armor, u.MaxArmor, false);
            v.Punch(u.Def.Has(Kw.Vehicle) ? 0.35f : 0.22f);
            var p = _fx.Local(v.Rt);
            bool heavy = u.Def.Has(Kw.Vehicle) || u.Def.Cost >= 5;
            _fx.Dust(p + new Vector2(0, -60), heavy ? 1.3f : 0.7f);
            _fx.Ring(p, new Color(1f, 0.9f, 0.7f, 0.6f), 40f, heavy ? 360f : 220f, 0.4f);
            _fx.Burst(p, 1, CardSkin.Spr("k_flare"), new Color(1f, 0.92f, 0.75f, 0.9f), 0f, 0f, 0.3f, 0.3f, 120f, heavy ? 420f : 300f, 0f, 0f, rotate: false);
            _fx.Burst(p, 10, CardSkin.Spr("k_spark1"), new Color(1f, 0.85f, 0.55f, 0.9f), 60f, 220f, 0.3f, 0.6f, 14f, 4f, -60f, 2f);
            RevealFx(v, heavy, u.Def.Kind == CardKind.Hero);
            if (u.Def.Kind == CardKind.Hero)
            {
                _fx.Burst(p, 30, CardSprites.Spark, new Color(1f, 0.85f, 0.45f), 200f, 520f, 0.4f, 0.9f, 20f, 6f, 0f, 2f, align: true, stretch: 3f);
                _fx.Flash(new Color(1f, 0.85f, 0.5f), 0.25f);
            }
            if (heavy) { _fx.Shake(9f, 0.3f); SFXManager.Play(Sfx.ImpactMetal, 0.8f, 0.7f, Pan(p)); }
            SFXManager.Play(Sfx.CardSlam, heavy ? 1f : 0.7f, heavy ? 0.85f : 1f, Pan(p));
            // Голос юнита: техника заводит дизель, сооружения и дроны — сервоприводы, пехота передёргивает затвор
            if (u.Def.Has(Kw.Vehicle)) SFXManager.PlayDelayed(Sfx.EngineRev, 0.12f, 0.9f, Random.Range(0.9f, 1.05f));
            else if (u.Def.Kind == CardKind.Structure || u.Def.Has(Kw.Drone)) SFXManager.PlayDelayed(Sfx.Servo, 0.1f, 0.9f);
            else if (!u.IsTrap) SFXManager.PlayDelayed(Sfx.GunCock, 0.15f, 0.8f, Random.Range(0.95f, 1.05f));
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
                SFXManager.Play(Sfx.GunCock, 0.8f);
                SFXManager.Play(Sfx.ImpactMetal, 0.4f, 1.3f);
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
                SFXManager.Play(Sfx.Boom, 0.85f);
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
            // Приказ — крупным планом посреди стола, затем над сектором срабатывает
            v.SetAlpha(0f);
            yield return Showcase(v, key ?? new BCard(e.Def), e.Side);
            if (v == null) yield break;
            // Карта приказа поднимается над сектором и раскрывается
            Reparent(v, _fxLayer);
            _targets[v] = _fx.Local(_sec[e.Sector].Header, new Vector2(0, -150));
            _targetRot[v] = 0f;
            v.SetFace(true, false);
            v.SetAlpha(1f);
            v.Punch(0.4f);
            v.GlowBoost = 1.2f;
            SFXManager.Play(Sfx.CardFlip, 0.7f);
            // Приказ срабатывает: «заряд» (втянутый «вжух», гул вверх) — удар с разрядом и суббасом; поверх тихая рация
            SFXManager.Play(Sfx.FxCast, 1f, e.Side == _P ? 1f : 0.94f, Pan(_fx.Local(_sec[e.Sector].Header)));
            SFXManager.Play(Sfx.OrderCast, 0.4f);
            AddLog(Loc.F("{0}: приказ «{1}»", SideName(e.Side), e.Def.Name));
            // Рунный круг над сектором, куда отдан приказ: свои — бирюзой, врага — красным
            var oc = e.Side == _P ? new Color(0.4f, 0.9f, 1f) : new Color(1f, 0.45f, 0.35f);
            _fx.Play(UIFx.Fx2.Circle, RowCenter(e.Sector, BattleState.Enemy(e.Side)), oc, 300f, 1.3f, 340f);
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
                    SFXManager.Play(Sfx.Radio, 1f, 0.95f);
                    for (int i = 0; i < 10; i++)
                    {
                        var p = target + new Vector2(Random.Range(-170f, 170f), Random.Range(-60f, 60f));
                        _fx.Line(top + new Vector2(Random.Range(-300f, 300f), 0), p, new Color(1f, 0.15f, 0.1f, 0.9f), 3f, 0.25f);
                        _fx.Burst(p, 1, CardSprites.Glow, new Color(1f, 0.2f, 0.1f, 0.9f), 0, 0, 0.25f, 0.25f, 24f, 10f);
                        yield return W(0.07f);
                    }
                    _fx.Particles?.Play(CardParticles.Fx.Plasma, target, 1.5f);
                    _fx.Line(top, target, new Color(1f, 1f, 1f, 1f), 160f, 0.55f);
                    _fx.Line(top, target, new Color(0.6f, 0.9f, 1f, 0.9f), 280f, 0.8f);
                    _fx.Flash(Color.white, 0.95f, 2.2f);
                    _fx.Shake(26f, 0.8f);
                    Kick(0.025f);
                    SFXManager.Play(Sfx.Boom, 1f, 0.8f);
                    SFXManager.PlayDelayed(Sfx.BoomSmall, 0.12f, 0.8f, 0.7f);
                    for (int i = 0; i < 4; i++) _fx.Explosion(target + Random.insideUnitCircle * 140f, 1.5f);
                    _fx.Ring(target, new Color(1f, 0.95f, 0.8f, 1f), 40f, 900f, 0.8f);
                    _fx.Burst(target, 40, CardSprites.Spark, new Color(1f, 0.6f, 0.2f, 1f), 100f, 600f, 1.2f, 2.6f, 9f, 3f, 260f, 0.8f, 90f, 160f, align: true, stretch: 2.5f);
                    yield return W(0.8f);
                    break;
                }
                case "O1":   // Огневой вал с кораблей
                {
                    SFXManager.Play(Sfx.GunMortar, 0.7f, 0.9f);
                    SFXManager.PlayDelayed(Sfx.GunMortar, 0.12f, 0.6f, 1.05f);
                    SFXManager.PlayDelayed(Sfx.ShellWhistle, 0.05f, 0.9f);
                    for (int i = 0; i < 10; i++)
                    {
                        var p = target + new Vector2(Random.Range(-190f, 190f), Random.Range(-50f, 50f));
                        _fx.Projectile(p + new Vector2(-500f, 700f), p, new Color(1f, 0.75f, 0.35f), 0.3f * Pace, 16f);
                        StartCoroutine(Later(0.3f, () => { _fx.Explosion(p, 0.7f); _fx.Shake(6f, 0.15f); SFXManager.Play(Sfx.BoomSmall, 0.55f, Random.Range(0.85f, 1.1f)); }));
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
                    SFXManager.Play(Sfx.Servo, 0.8f, 0.8f);
                    SFXManager.Play(Sfx.Whoosh, 0.8f, 0.7f);
                    yield return W(0.6f);
                    break;
                case "O6":
                    _fx.Burst(own, 26, CardSprites.Spark, new Color(1f, 0.85f, 0.4f), 120f, 380f, 0.5f, 0.9f, 16f, 4f, 0f, 2f, align: true, stretch: 2.5f);
                    SFXManager.Play(Sfx.FxBuff, 0.9f, 0.88f);
                    SFXManager.Play(Sfx.Whoosh, 0.9f, 0.6f);
                    SFXManager.PlayDelayed(Sfx.BoomFar, 0.2f, 1f);
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
                    SFXManager.Play(Sfx.ElecZap);
                    yield return W(0.3f);
                    break;
                case "O9":   // ЭМИ
                    _fx.Energy(target, new Color(0.45f, 0.8f, 1f), 1.6f);
                    _fx.Particles?.Play(CardParticles.Fx.Electric, target, 1.3f);
                    _fx.Ring(target, new Color(0.4f, 0.75f, 1f, 1f), 40f, 900f, 0.7f);
                    _fx.Ring(target, new Color(0.7f, 0.9f, 1f, 0.8f), 20f, 600f, 0.5f);
                    _fx.Sparks(target, new Color(0.55f, 0.85f, 1f), 40, 1.4f);
                    _fx.Flash(new Color(0.45f, 0.75f, 1f), 0.45f);
                    SFXManager.Play(Sfx.ElecZap, 1f, 0.8f);
                    SFXManager.Play(Sfx.ImpactShield, 1f, 0.6f);
                    SFXManager.Play(Sfx.FxShatter, 0.6f, 1.15f);
                    yield return W(0.6f);
                    break;
                case "I4":   // Пси-шторм: молнии по всему сектору
                    SFXManager.Play(Sfx.FxPsi, 1f, 0.9f);
                    SFXManager.Play(Sfx.ElecZap);
                    SFXManager.PlayDelayed(Sfx.ElecZap, 0.25f, 0.8f, 0.9f);
                    SFXManager.PlayDelayed(Sfx.ElecZap, 0.5f, 0.7f, 1.1f);
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
                    SFXManager.Play(Sfx.FxSpores, 1f);
                    for (int i = 0; i < 5; i++) _fx.Smoke(target + Random.insideUnitCircle * 150f, 2f, new Color(0.45f, 0.95f, 0.35f, 0.5f));
                    _fx.Burst(target, 14, CardSkin.Spr("k_smoke2"), new Color(0.55f, 0.9f, 0.35f, 0.35f), 15f, 60f, 1.6f, 2.6f, 50f, 140f, -15f, 0.6f);
                    yield return W(0.6f);
                    break;
                case "X3":   // Децимация
                    _fx.Flash(new Color(0.8f, 0.05f, 0.05f), 0.35f);
                    SFXManager.Play(Sfx.GunAuto, 1f, 0.9f);
                    SFXManager.PlayDelayed(Sfx.GunAuto, 0.18f, 0.8f, 1.05f);
                    yield return W(0.4f);
                    break;
                case "Q3":   // Ликвидация: снайперская линия
                    _fx.Line(own + new Vector2(0, -400), target, new Color(1f, 0.9f, 0.5f, 1f), 3f, 0.3f);
                    SFXManager.Play(Sfx.GunSniper);
                    yield return W(0.3f);
                    break;
                case "N3":
                case "N4":
                    _fx.Ring(target, new Color(0.9f, 0.95f, 1f, 0.9f), 30f, 360f, 0.5f);
                    _fx.Burst(target, 30, CardSprites.Spark, new Color(0.9f, 0.95f, 1f), 100f, 300f, 0.4f, 0.8f, 12f, 4f, 0f, 2f, align: true, stretch: 2f);
                    SFXManager.Play(Sfx.Servo);
                    SFXManager.Play(Sfx.FxBuff, 0.6f, 0.85f);
                    yield return W(0.5f);
                    break;
                default:
                {
                    var c = CardArt.Frame(d, 0, FactionOf(side));
                    _fx.Ring(own, c, 30f, 420f, 0.5f);
                    _fx.Burst(own, 20, CardSprites.Spark, c, 100f, 300f, 0.4f, 0.8f, 14f, 4f, 0f, 2f, align: true, stretch: 2f);
                    SFXManager.Play(Sfx.FxBuff, 0.8f, 0.92f);
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

        private int _focusSector = -1;

        /// <summary>Перестрелка переходит в новый сектор — дорожка вспыхивает золотом, короткая пауза «прицеливания».</summary>
        private IEnumerator FocusSector(int sector)
        {
            if (sector < 0 || sector > 2 || sector == _focusSector) yield break;
            _focusSector = sector;
            var s = _sec[sector];
            s.HighC = new Color(1f, 0.82f, 0.4f);
            s.HighA = 0.55f;
            yield return W(0.3f);
        }

        private bool EnergyWeapons(int side) { var f = FactionOf(side); return f == "iridia" || f == "nol" || f == "astrea"; }

        private IEnumerator OnShot(BEvent e)
        {
            var src = ViewOf(e.Uid);
            var tgt = ViewOf(e.Uid2);
            if (src == null || tgt == null) yield break;
            int shotSec = e.Sector >= 0 ? e.Sector : _state.FindUnit(e.Uid2)?.Sector ?? -1;
            bool newSector = shotSec >= 0 && shotSec != _focusSector;
            yield return FocusSector(shotSec);
            if (newSector) SweepRow(shotSec, tgt.Side == _P ? 1 : 0, ShotColor(src.Side, src));
            var a = _fx.Local(src.Rt, new Vector2(0, src.Side == _P ? 70 : -70));
            var b = _fx.Local(tgt.Rt);
            var dir = (b - a).normalized;
            float ang = Mathf.Atan2(b.y - a.y, b.x - a.x) * Mathf.Rad2Deg;
            var c = ShotColor(src.Side, src);
            var d = src.Card.Def;
            bool heavy = d.Has(Kw.Vehicle) || d.Cost >= 5;
            // «Голос» оружия: у каждого типа карт свой сдвиг тона — отряды на слух различаются
            float voice = 0.92f + ((d.Id.GetHashCode() & 0x7fffffff) % 17) * 0.01f;

            // Выпад атакующей карты к цели (как в Hearthstone), короткое «прицеливание»
            src.Lunge(dir, heavy ? 16f : 24f);
            src.GlowBoost = 0.8f;
            yield return W(0.1f);

            if (d.Kind == CardKind.Hero && e.Text != "crush")
            {
                // Командир бьёт голографическим клинком: взмах через цель, режущая дуга, удар
                var gold = new Color(1f, 0.8f, 0.38f);
                float reach = Mathf.Clamp((b - a).magnitude * 0.95f, 300f, 620f);
                float sweep = src.Side == _P ? 1f : -1f;
                StartCoroutine(GhostBlade(a, ang + 75f * sweep, ang - 75f * sweep, gold, reach, 0.3f));
                yield return W(0.17f);
                HitFx(b, gold, ang, 1.4f, "cross");
                _fx.Shake(9f, 0.25f);
                SFXManager.Play(Sfx.ImpactMetal, 0.9f, 1.2f, Pan(b));
                yield return W(0.2f);
                yield break;
            }

            if (e.Text == "crush")
            {
                // Таран: карта вминается в цель, земля трясётся
                src.Lunge(dir, 60f);
                yield return W(0.1f);
                _fx.Shake(16f, 0.4f);
                _fx.Dust(b, 1.6f);
                _fx.Impact(b, ang, new Color(1f, 0.8f, 0.5f), 1.4f);
                HitFx(b, new Color(1f, 0.7f, 0.35f), ang, 1.6f, "bolt");
                _arc?.Slash(b, 150f, ang + 80f, ang - 80f, 42f, new Color(1f, 0.95f, 0.8f), new Color(1f, 0.6f, 0.25f), new Color(0.9f, 0.25f, 0.08f), 0.45f, 0.7f, 0f, true);
                SFXManager.Play(Sfx.ImpactMetal, 1f, 0.6f, Pan(b));
                SFXManager.Play(Sfx.CardSlam, 1f, 0.7f, Pan(b));
                yield return W(0.25f);
                yield break;
            }

            bool lob = e.Text == "lob" || e.Text == "salvo";
            if (d.Has(Kw.Flame))
            {
                // Огнемёт: плотная струя пламени и жар на цели
                SFXManager.Play(Sfx.GunFlame, 1f, Random.Range(0.92f, 1.08f), Pan(a));
                _fx.Particles?.Play(CardParticles.Fx.Flame, a, 1f, ang);
                for (int i = 0; i < 6; i++)
                {
                    _fx.Burst(a, 4, CardSkin.Spr(i % 2 == 0 ? "k_fire1" : "k_flame"), new Color(1f, 0.6f, 0.2f, 0.95f), 650f, 900f, 0.28f, 0.38f, 30f, 90f, -60f, 1f, ang, 14f, end: new Color(0.5f, 0.12f, 0.04f, 0f));
                    yield return W(0.035f);
                }
                _fx.Burst(b, 6, CardSkin.Spr("k_fire2"), new Color(1f, 0.55f, 0.15f, 0.9f), 20f, 80f, 0.4f, 0.7f, 60f, 110f, -120f, 1f, 90f, 80f);
                _fx.Flipbook("fx_fireball", 8, 8, b, 170f, 0.7f, new Color(1f, 0.85f, 0.7f, 0.95f), 220f, Random.Range(0f, 360f), default, 0f, true);
                _arc?.Embers(b, new Color(1f, 0.85f, 0.5f), new Color(0.8f, 0.2f, 0.05f, 0f), 18, 120f, 420f, 90f, 200f, 0.4f, 0.9f, 200f);
                _fx.Smoke(b, 0.6f);
                yield return W(0.12f);
                yield break;
            }

            if (lob)
            {
                // Навесной снаряд: дым у орудия, шлейф по дуге, взрыв в точке падения
                float dur = 0.55f;
                _fx.Muzzle(a, 90f, c, 1.4f);
                _fx.Smoke(a, 0.5f, new Color(0.35f, 0.33f, 0.3f, 0.6f));
                _fx.Shake(5f, 0.15f);
                SFXManager.Play(Sfx.GunMortar, 0.9f, 1f, Pan(a));
                SFXManager.PlayDelayed(Sfx.ShellWhistle, dur * Pace * 0.35f / Mathf.Max(0.1f, _animSpeed), 0.7f);
                _fx.Projectile(a, b, c, dur * Pace, 16f, true);
                StartCoroutine(ShellTrail(a, b, dur * Pace));
                yield return W(dur);
                _fx.Explosion(b, 0.75f);
                _arc?.RingWave(b, new Color(1f, 0.75f, 0.45f, 0.9f), 20f, 220f, 7f, 0.4f, 0.6f);
                _arc?.Embers(b, new Color(1f, 0.9f, 0.6f), new Color(0.8f, 0.25f, 0.05f, 0f), 22, 200f, 640f, 90f, 220f, 0.4f, 1f, 900f);
                _fx.Shake(10f, 0.3f);
                SFXManager.Play(Sfx.BoomSmall, 0.8f, 1f, Pan(b));
                yield return W(0.12f);
                yield break;
            }

            if (d.Has(Kw.Sniper))
            {
                // Снайпер: тонкий луч прицела, пауза, мгновенный выстрел с хлопком
                _fx.Line(a, b, new Color(1f, 0.25f, 0.2f, 0.7f), 2f, 0.35f);
                yield return W(0.22f);
                _fx.Muzzle(a, ang, c, 1f);
                _fx.Beam(a, b, new Color(1f, 0.95f, 0.75f), 3f, 0.12f);
                SFXManager.Play(Sfx.GunSniper, 1f, 1f, Pan(a));
                yield return W(0.05f);
                _fx.Impact(b, ang, new Color(1f, 0.9f, 0.6f), 1.1f);
                HitFx(b, new Color(1f, 0.85f, 0.55f), ang, 1.1f, "cross");
                _arc?.Glint((a + b) * 0.5f, new Color(1f, 0.85f, 0.6f), (b - a).magnitude * 1.05f, 5f, 0.18f, ang);
                yield return W(0.08f);
                yield break;
            }

            if (EnergyWeapons(src.Side))
            {
                // Энергетическое оружие: импульсы-лучи
                int pulses = heavy ? 1 : 2;
                for (int i = 0; i < pulses; i++)
                {
                    _fx.Muzzle(a, ang, c, heavy ? 1.3f : 0.8f);
                    _fx.Beam(a + Random.insideUnitCircle * 6f, b + Random.insideUnitCircle * 10f, c, heavy ? 7f : 4f, 0.14f);
                    SFXManager.Play(Sfx.GunLaser, heavy ? 1f : 0.75f, heavy ? 0.8f : Random.Range(0.95f, 1.1f), Pan(a));
                    _fx.Impact(b, ang, c, heavy ? 1.2f : 0.8f);
                    HitFx(b, c, ang, heavy ? 1.3f : 0.75f, "ring");
                    if (heavy) _fx.Energy(b, c, 0.6f);
                    yield return W(0.09f);
                }
                yield break;
            }

            // Кинетика: очередь трассеров у пехоты, один тяжёлый снаряд у техники
            int shots = heavy ? 1 : 3;
            float fly = heavy ? 0.2f : 0.16f;
            bool auto = !heavy && (d.Power >= 3 || d.Has(Kw.Drone));
            if (heavy) { SFXManager.Play(Sfx.GunHeavy, 1f, voice * Random.Range(0.96f, 1.03f), Pan(a)); Kick(0.008f); }
            else if (auto) SFXManager.Play(Sfx.GunAuto, 0.9f, voice, Pan(a));
            for (int i = 0; i < shots; i++)
            {
                var spread = Random.insideUnitCircle * (heavy ? 6f : 16f);
                _fx.Muzzle(a, ang, c, heavy ? 1.5f : 0.75f);
                _fx.Bullet(a, b + spread, c, fly * Pace, heavy ? 22f : 11f);
                if (!heavy && !auto) SFXManager.Play(Sfx.GunRifle, i == 0 ? 0.9f : 0.6f, voice * Random.Range(0.97f, 1.04f), Pan(a));
                if (heavy) { src.Punch(0.08f); _fx.Shake(4f, 0.12f); }
                bool flesh = tgt.Card.Def.Has(Kw.Infantry) || tgt.Card.Def.Has(Kw.Militia);
                StartCoroutine(Later(fly, () =>
                {
                    _fx.Impact(b + spread, ang, c, heavy ? 1.3f : 0.7f, flesh);
                    _arc?.Spikes(b + spread, c, 5, heavy ? 110f : 60f, heavy ? 6f : 4f, 0.16f);
                    SFXManager.Play(flesh ? Sfx.ImpactFlesh : Sfx.ImpactMetal, heavy ? 0.9f : 0.45f, Random.Range(0.9f, 1.15f), Pan(b));
                }));
                yield return W(heavy ? 0.05f : 0.07f);
            }
            yield return W(fly);
            HitFx(b, c, ang, heavy ? 1.4f : 0.8f, "bolt");
            if (heavy) { _fx.Flipbook("fx_explosion", 6, 6, b, 120f, 0.7f, Color.white, 170f, Random.Range(0f, 360f)); _fx.Shake(8f, 0.2f); SFXManager.Play(Sfx.BoomSmall, 0.6f, 1.1f, Pan(b)); }
        }

        /// <summary>Шлейф дыма за навесным снарядом (повторяет его дугу).</summary>
        private IEnumerator ShellTrail(Vector2 a, Vector2 b, float dur)
        {
            const float g = 2400f;
            float t = 0f;
            while (t < dur)
            {
                t += Time.unscaledDeltaTime * _animSpeed;
                var p = a + (b - a) * (t / dur) + new Vector2(0, 0.5f * g * t * (dur - t));
                _fx.Burst(p, 1, CardSkin.Spr("k_smoke2"), new Color(0.6f, 0.58f, 0.55f, 0.35f), 0f, 15f, 0.5f, 0.8f, 16f, 46f, -20f, 1f);
                yield return null;
            }
        }

        private IEnumerator OnDamage(BEvent e)
        {
            var v = ViewOf(e.Uid);
            if (v == null) yield break;
            var p = _fx.Local(v.Rt);
            v.Hit(Mathf.Clamp(e.Amount / 3f, 0.5f, 1.5f));
            if (e.Uid2 < 0) _fx.Play(UIFx.Fx2.Impact, p, new Color(1f, 0.45f, 0.2f), 170f, 0.38f, 220f, Random.Range(0f, 360f));
            _fx.Number(p + new Vector2(Random.Range(-20f, 20f), 14), "−" + e.Amount, new Color(1f, 0.42f, 0.25f), 46 + Mathf.Min(22, e.Amount * 3), 1f);
            var def = v.Card.Def;
            if (def.Has(Kw.Vehicle) || def.Kind == CardKind.Structure || def.Has(Kw.Drone))
            {
                _fx.Sparks(p, new Color(1f, 0.8f, 0.45f), 12, 0.9f);
                if (e.Amount >= 3) _fx.Flipbook("fx_explosion", 6, 6, p + Random.insideUnitCircle * 20f, 110f, 0.7f, Color.white, 150f, Random.Range(0f, 360f));
            }
            else
                _fx.Blood(p, CardArt.Blood(FactionOf(v.Side)), 0.6f);
            bool metal = def.Has(Kw.Vehicle) || def.Kind == CardKind.Structure || def.Has(Kw.Drone);
            SFXManager.Play(metal ? Sfx.ImpactMetal : Sfx.ImpactFlesh, 0.55f, Random.Range(0.9f, 1.15f), Pan(p));
            v.RefreshNumbers(v.ShownPower, v.ShownArmor - e.Amount, v.MaxArmor);
            yield return W(0.12f);
        }

        private IEnumerator OnDeath(BEvent e)
        {
            var v = ViewOf(e.Uid);
            if (v == null) yield break;
            _units.Remove(e.Uid);
            var p = _fx.Local(v.Rt);
            var def = v.Card.Def;
            bool hard = def.Has(Kw.Vehicle) || def.Kind == CardKind.Structure;
            KillFx(p, hard, def.Cost >= 5, def.Has(Kw.Drone) ? new Color(0.6f, 0.9f, 1f) : v.Side == _P ? new Color(1f, 0.5f, 0.35f) : new Color(1f, 0.75f, 0.4f));
            if (hard)
            {
                _fx.Explosion(p, def.Cost >= 5 ? 1.6f : 1.1f);
                _fx.Shake(def.Cost >= 5 ? 18f : 10f, 0.4f);
                SFXManager.Play(def.Cost >= 5 ? Sfx.Boom : Sfx.BoomSmall, 0.9f, 1f, Pan(p));
                if (def.Cost >= 5) _fx.Flash(new Color(1f, 0.6f, 0.3f), 0.16f, 2f);
                Kick(def.Cost >= 5 ? 0.016f : 0.01f);
                SFXManager.PlayDelayed(Sfx.UnitDown, 0.08f, 0.7f);
            }
            else if (def.Has(Kw.Drone))
            {
                _fx.Sparks(p, new Color(0.85f, 0.95f, 1f), 24, 1.1f);
                _fx.Smoke(p, 0.6f);
                SFXManager.Play(Sfx.BoomSmall, 0.45f, 1.4f, Pan(p));
                SFXManager.Play(Sfx.ImpactMetal, 0.6f, 1.3f, Pan(p));
            }
            else
            {
                _fx.Blood(p, CardArt.Blood(FactionOf(v.Side)), 1.5f);
                _fx.Particles?.Play(CardParticles.Fx.FleshHit, p, 1.3f, 90f);
                _fx.Dust(p + new Vector2(0, -50), 0.6f);
                SFXManager.Play(Sfx.ImpactFlesh, 0.9f, 0.8f, Pan(p));
                SFXManager.Play(Sfx.UnitDown, 0.45f, 1.15f, Pan(p));
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
            if (hard)
            {
                // Техника разлетается осколками арта: каждый кувыркается в 3D и падает
                var col = CardArt.Frame(v.Card.Def, v.Card.Stars, v.SideFaction);
                ShardsFx(v, new Color(col.r * 0.45f, col.g * 0.45f, col.b * 0.45f, 0.9f));
                v.SetAlpha(0f);
                yield return W(0.2f);
            }
            else
            {
                // Пехота падает навзничь: карта опрокидывается от зрителя (перспектива), темнеет и гаснет
                float t = 0f;
                var start = v.Rt.localRotation.eulerAngles.z;
                float spin = Random.Range(-14f, 14f);
                float back = v.Side == _P ? -78f : 78f;
                while (t < 1f && v != null)
                {
                    t += Time.unscaledDeltaTime * 1.6f * _animSpeed;
                    float e = t * t;
                    v.SetAlpha(1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.35f, 1f, t)));
                    v.Rt.localRotation = Quaternion.Euler(back * e, 0f, start + spin * t);
                    v.Rt.localScale = Vector3.one * (1f - t * 0.12f);
                    yield return null;
                }
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
            SFXManager.Play(Sfx.FxBurn, 0.8f, Random.Range(0.95f, 1.08f), Pan(p));
            _arc?.Glint(p, c, 360f, 7f, 0.35f);
            _arc?.Embers(p, new Color(Mathf.Lerp(c.r, 1f, 0.5f), Mathf.Lerp(c.g, 1f, 0.5f), Mathf.Lerp(c.b, 1f, 0.5f)), new Color(c.r, c.g, c.b, 0f), 14, 200f, 420f, 90f, 120f, 0.35f, 0.7f, -150f);
            // Уходит со стола: поднимается и разворачивается ребром (3D), растворяясь
            float t = 0f;
            float turn = Random.value < 0.5f ? -90f : 90f;
            while (t < 1f && v != null)
            {
                t += Time.unscaledDeltaTime * 2f * _animSpeed;
                float e = Mathf.SmoothStep(0f, 1f, t);
                v.SetAlpha(1f - e);
                v.Rt.localRotation = Quaternion.Euler(0f, turn * e, 0f);
                v.Rt.localScale = Vector3.one * (1f + e * 0.2f);
                yield return null;
            }
            _dying.Remove(v);
            if (v != null) Destroy(v.gameObject);
        }

        private IEnumerator OnStatus(BEvent e)
        {
            if (e.Text == "barrage")
            {
                // Артподготовка с орбиты перед первым ходом: снаряды по укреплениям колонии, дальше — числа урона
                bool mine = e.Side == _P;
                AddLog(Loc.F("<color=#F2C766>Артподготовка с орбиты: укрепления −{0} брони</color>", e.Amount));
                SFXManager.Play(Sfx.ShellWhistle, 1f);
                _fx.Flash(new Color(1f, 0.55f, 0.25f), 0.2f, 1.2f);
                yield return Banner(Loc.T("АРТПОДГОТОВКА"),
                    mine ? Loc.F("Флот бьёт с орбиты по укреплениям: −{0} брони каждому", e.Amount)
                         : Loc.F("Флот врага бьёт по вашим укреплениям: −{0} брони каждому", e.Amount),
                    new Color(1f, 0.7f, 0.35f), 0.9f);
                foreach (var kv in new List<KeyValuePair<int, CardView>>(_units))
                {
                    var u = _state.FindUnit(kv.Key);
                    if (u == null || kv.Value == null || u.Side == e.Side || !u.IsStructure || u.Has(Kw.Shield)) continue;
                    var p = _fx.Local(kv.Value.Rt);
                    _fx.Explosion(p, 0.8f);
                    _fx.Shake(8f, 0.22f);
                    SFXManager.Play(Sfx.BoomSmall, 0.8f, Random.Range(0.9f, 1.05f), Pan(p));
                    yield return W(0.14f);
                }
                yield break;
            }
            if (e.Text == "smoke")
            {
                yield break;   // дым уже показан приказом
            }
            if (e.Text == "breach_warn")
            {
                // Перевес в Штабе при снятом щите: ещё ход — и прорыв
                var hq = _sec[e.Sector];
                hq.HighC = new Color(1f, 0.35f, 0.3f);
                hq.HighA = 1f;
                AddLog(Loc.T("<color=#FF8A80>Штаб под угрозой: ещё ход — и прорыв</color>"));
                SFXManager.Play(Sfx.Radio, 1f, 0.9f);
                yield return Banner(Loc.T("ШТАБ ПОД УГРОЗОЙ"),
                    e.Side == _P ? Loc.T("Удержите перевес ещё ход — и прорыв") : Loc.T("Ещё ход — и враг прорвётся. Усильте Штаб!"),
                    new Color(1f, 0.4f, 0.32f), 1.1f);
                yield break;
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
            var sp = _fx.Local(v.Rt);
            if (e.Text == "foresight") SFXManager.Play(Sfx.FxPsi, 0.7f, 1.1f, Pan(sp));
            else if (e.Text == "immune") SFXManager.Play(Sfx.FxShieldUp, 0.8f, 1.05f, Pan(sp));
            else if (e.Text == "suppress" || e.Text == "disable" || e.Text == "immobile") SFXManager.Play(Sfx.FxDebuff, 0.85f, e.Text == "disable" ? 1.1f : 0.95f, Pan(sp));
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
            SFXManager.Play(Sfx.CardSlam, 0.8f, 1f, Pan(land));
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
            // Перебежка: карта поднимается над столом, наклоняется по ходу (раскладка) и тянет за собой шлейф
            v.Raise(0.6f);
            _arc?.Trail(v.Rt, new Color(0.5f, 1f, 0.6f), 26f, 0.3f);
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
            SFXManager.Play(Sfx.Radio);
            SFXManager.PlayDelayed(Sfx.BoomFar, 0.4f, 1f);
            if (_setup.Terrain == Terrain.Molten && e.Sector >= 0)
            {
                var p = _fx.Local(_sec[e.Sector].Col);
                for (int i = 0; i < 5; i++) _fx.Explosion(p + Random.insideUnitCircle * 180f, 1.1f);
                _fx.Burst(p, 40, CardSprites.Spark, new Color(1f, 0.5f, 0.15f, 1f), 200f, 700f, 0.8f, 1.6f, 10f, 4f, 900f, 0.4f, 90f, 120f, align: true, stretch: 2.5f);
                _fx.Shake(18f, 0.6f);
                SFXManager.Play(Sfx.Boom);
                SFXManager.PlayDelayed(Sfx.BoomSmall, 0.2f, 0.7f, 0.8f);
            }
            if (_setup.Terrain == Terrain.Desert)
                for (int i = 0; i < 12; i++) _fx.Smoke(new Vector2(-_fxLayer.rect.width * 0.5f, Random.Range(-300f, 300f)), 3f, new Color(0.85f, 0.65f, 0.4f, 0.35f));
            AddLog(Loc.F("<color=#9FD0FF>Погода: {0}</color>", _state.WeatherName ?? e.Text));
            yield return Banner(_state.WeatherName ?? e.Text, _state.WeatherText ?? "", c, 1.4f);
        }

        // ---------------------------------------------------------------- баннер

        private static readonly Color CalloutGold = new Color(1f, 0.84f, 0.42f);

        /// <summary>
        /// Крупная надпись прямо на столе, без плашки («ВАШ ХОД», «РАСКРЫТИЕ»): влетает с увеличением, держится и гаснет.
        /// Не блокирует ход — запускается через StartCoroutine.
        /// </summary>
        private IEnumerator Callout(string text, Color c, float dur = 1.1f)
        {
            if (_overlay == null) yield break;
            var at = new Vector2(0f, TrayY);
            var glow = CardSkin.Simple(_overlay, "CalloutGlow", "glow", new Color(c.r, c.g, c.b, 0f));
            glow.rectTransform.At(Mid, Mid, at, new Vector2(980, 300));
            var t = Caption(_overlay, text, 76, new Color(c.r, c.g, c.b, 0f), TextAnchor.MiddleCenter, 5f);
            if (CardSkin.Title != null) { t.font = CardSkin.Title; t.fontStyle = FontStyle.Normal; }
            t.rectTransform.At(Mid, Mid, at, new Vector2(1500, 130));
            var ol = t.gameObject.AddComponent<Outline>();
            ol.effectColor = new Color(0.12f, 0.06f, 0f, 0.95f);
            ol.effectDistance = new Vector2(3f, -3f);
            var sh = t.gameObject.AddComponent<Shadow>();
            sh.effectColor = new Color(0f, 0f, 0f, 0.65f);
            sh.effectDistance = new Vector2(0f, -7f);
            _arc?.Glint(at, c, 1300f, 11f, 0.55f);
            _fx?.Play(UIFx.Fx2.Burst, at, new Color(c.r, c.g, c.b, 0.55f), 420f, dur * 0.9f, 720f, Random.Range(0f, 360f));
            SFXManager.Play(Sfx.Whoosh, 0.35f, 1.15f);
            SFXManager.Play(Sfx.FxStinger, 1f);
            float total = dur / Mathf.Max(0.1f, _animSpeed * (Fast ? 3f : 1f));
            float time = 0f;
            while (time < total && t != null)
            {
                time += Time.unscaledDeltaTime;
                float k = time / total;
                float a = k < 0.12f ? k / 0.12f : k > 0.7f ? 1f - (k - 0.7f) / 0.3f : 1f;
                float e = 1f - Mathf.Pow(1f - Mathf.Clamp01(k / 0.14f), 3f);
                t.color = new Color(c.r, c.g, c.b, a);
                glow.color = new Color(c.r, c.g, c.b, 0.32f * a);
                t.rectTransform.localScale = Vector3.one * (Mathf.Lerp(1.7f, 1f, e) + 0.05f * k);
                yield return null;
            }
            if (t != null) Destroy(t.gameObject);
            if (glow != null) Destroy(glow.gameObject);
        }

        /// <summary>Крупно показываем: приказы, героев, карты от 4 очков и эпические и выше.</summary>
        private static bool Showcased(CardDef d, int stars)
            => d != null && (d.Kind == CardKind.Order || d.Kind == CardKind.Hero || d.Cost >= 4 || CardArt.Rarity(d, stars) >= CardRarity.Epic);

        /// <summary>
        /// Розыгрыш крупным планом: карта взлетает с места на середину стола, разворачиваясь из наклона (3D),
        /// держится в ореоле цвета редкости и возвращается на место. Пробел или правая кнопка — пропустить.
        /// </summary>
        private IEnumerator Showcase(CardView from, BCard card, int side)
        {
            if (Fast || from == null || card == null || _fxLayer == null) yield break;
            var start = _fx.Local(from.Rt);
            var center = new Vector2(Mathf.Clamp(start.x * 0.35f, -260f, 260f), TrayY + 10f);
            var rarity = CardArt.Rarity(card.Def, card.Stars);
            var rc = CardArt.RarityColor(rarity);
            bool grand = rarity >= CardRarity.Legendary || card.Def.Kind == CardKind.Order;

            var halo = CardSkin.Simple(_fxLayer, "ShowHalo", "glow", new Color(rc.r, rc.g, rc.b, 0f));
            halo.rectTransform.At(Mid, Mid, center, new Vector2(640, 640));
            // Позади карты (создаются раньше неё — рисуются под ней): лучи у легендарных, рунный круг у приказов, волна у всех
            float lead = 0.2f / Mathf.Max(0.1f, _animSpeed);
            if (card.Def.Kind == CardKind.Order) _fx.Play(UIFx.Fx2.Circle, center, rc, 560f, 1.1f, 620f, 0f, lead);
            else if (grand) _fx.Play(UIFx.Fx2.Burst, center, rc, 520f, 1f, 760f, Random.Range(0f, 360f), lead);
            _fx.Play(UIFx.Fx2.Shock, center, rc, 200f, 0.6f, 640f, 0f, lead);
            var wrap = At(_fxLayer, "Showcase", start, PreviewCard);
            var big = CardView.Create(wrap, card, side, FactionOf(side), PreviewCard, false);
            big.Rt.anchoredPosition = Vector2.zero;
            foreach (var g in big.GetComponentsInChildren<Graphic>()) g.raycastTarget = false;
            float small = BoardCard.y / PreviewCard.y;
            SFXManager.Play(Sfx.Whoosh, 0.65f, 1.1f, Pan(start));
            SFXManager.PlayDelayed(Sfx.CardFlip, 0.08f, 0.7f, 1.05f);

            float spd = Mathf.Max(0.1f, _animSpeed);
            float t = 0f;
            // Влёт: из ряда на середину, из наклона — лицом к игроку
            while (t < 1f && wrap != null)
            {
                t += Time.unscaledDeltaTime * spd / 0.22f;
                float e = 1f - Mathf.Pow(1f - Mathf.Clamp01(t), 3f);
                wrap.anchoredPosition = Vector2.Lerp(start, center, e);
                wrap.localScale = Vector3.one * Mathf.Lerp(small, 1f, e);
                wrap.localRotation = Quaternion.Euler(38f * (1f - e), (start.x < center.x ? -1f : 1f) * 20f * (1f - e), -8f * (1f - e));
                halo.color = new Color(rc.r, rc.g, rc.b, 0.5f * e);
                yield return null;
            }
            if (wrap == null) { if (halo != null) Destroy(halo.gameObject); yield break; }
            _arc?.Glint(center, rc, grand ? 900f : 620f, grand ? 11f : 8f, 0.4f);
            _arc?.RingWave(center, new Color(rc.r, rc.g, rc.b, 0.85f), 120f, grand ? 520f : 380f, 7f, 0.5f, 0.7f);
            if (grand) _fx.Flash(rc, 0.14f, 1.8f);
            // Удар показа: у легендарных и приказов — в полную силу, у остальных — тише и чуть выше
            SFXManager.Play(Sfx.FxShowcase, grand ? 1f : 0.6f, grand ? 1f : 1.08f);
            big.Punch(0.06f);
            // Держится: лёгкое покачивание
            float hold = (grand ? 0.55f : 0.4f) / spd, h = 0f;
            while (h < hold && wrap != null)
            {
                h += Time.unscaledDeltaTime;
                float k = h / hold;
                wrap.localRotation = Quaternion.Euler(Mathf.Sin(k * Mathf.PI) * -4f, Mathf.Sin(k * Mathf.PI * 2f) * 5f, 0f);
                halo.color = new Color(rc.r, rc.g, rc.b, 0.5f + 0.15f * Mathf.Sin(k * Mathf.PI * 3f));
                yield return null;
            }
            // Возврат на место
            var end = from != null ? _fx.Local(from.Rt) : start;
            t = 0f;
            while (t < 1f && wrap != null)
            {
                t += Time.unscaledDeltaTime * spd / 0.18f;
                float e = Mathf.Clamp01(t) * Mathf.Clamp01(t);
                wrap.anchoredPosition = Vector2.Lerp(center, end, e);
                wrap.localScale = Vector3.one * Mathf.Lerp(1f, small, e);
                wrap.localRotation = Quaternion.Euler(-24f * e, 0f, 0f);
                halo.color = new Color(rc.r, rc.g, rc.b, 0.5f * (1f - e));
                yield return null;
            }
            if (wrap != null) Destroy(wrap.gameObject);
            if (halo != null) Destroy(halo.gameObject);
        }

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
            var t = Caption(band.rectTransform, title, 54, new Color(c.r, c.g, c.b, 0f), TextAnchor.MiddleCenter, 5f);
            t.rectTransform.Stretch(0, 22, 0, 0);
            var tol = t.gameObject.AddComponent<Outline>();
            tol.effectColor = new Color(0f, 0.02f, 0.04f, 0.8f);
            tol.effectDistance = new Vector2(2f, -2f);
            var s = Caption(band.rectTransform, sub, 16, new Color(1, 1, 1, 0f), TextAnchor.LowerCenter, 1.2f);
            s.rectTransform.Stretch(0, 12, 0, 0);
            SFXManager.Play(Sfx.Whoosh, 0.8f, 0.8f);
            SFXManager.PlayDelayed(Sfx.CardSlam, 0.08f, 0.55f, 0.7f);
            float total = dur * 1.3f / Mathf.Max(0.1f, _animSpeed * (Fast ? 3f : 1f));
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
            var accent = win ? new Color(1f, 0.82f, 0.4f) : new Color(1f, 0.45f, 0.4f);
            if (win)
            {
                _fx.Flash(new Color(1f, 0.9f, 0.6f), 0.5f, 1f);
                for (int i = 0; i < 4; i++) _fx.Sparks(new Vector2(Random.Range(-500f, 500f), Random.Range(-200f, 200f)), new Color(1f, 0.85f, 0.45f), 40, 1.5f);
            }

            // Затемнение и вращающиеся лучи за лентой
            var dim = LGBuild.Panel(_overlay, "ResultDim", new Color(0, 0, 0, 0f), raycast: true);
            dim.rectTransform.Stretch();
            var rays = CardSkin.Simple(_overlay, "Rays", "glow", new Color(accent.r, accent.g, accent.b, 0f));
            rays.rectTransform.At(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 250), new Vector2(1500, 1500));
            var rays2 = CardSkin.Simple(_overlay, "Rays2", "k_flare", new Color(accent.r, accent.g, accent.b, 0f));
            rays2.rectTransform.At(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 250), new Vector2(900, 900));

            // Непрозрачная подложка под весь блок итогов: стол (плашки секторов, карты) не просвечивает сквозь цифры и
            // текст; лучи остаются ореолом вокруг. Сверху — стеклянная кромка цвета исхода
            var back = LGBuild.Panel(_overlay, "ResultBack", new Color(0.012f, 0.028f, 0.042f, 0f), raycast: true);
            back.sprite = CardSprites.Round;
            back.type = Image.Type.Sliced;
            back.rectTransform.At(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -6), new Vector2(1340, 680));
            var backRim = GlassAt(_overlay, "ResultFrame", new Vector2(0, -6), new Vector2(1340, 680), new Color(0.02f, 0.05f, 0.07f, 0f), new Color(accent.r, accent.g, accent.b, 0.5f), false, 24f);

            // Лента-заголовок
            string head;
            if (_P == BattleState.Attacker) head = r.AttackerWon ? Loc.T("ПЛАНЕТА ВЗЯТА") : r.Retreated ? Loc.T("ДЕСАНТ ОТСТУПИЛ") : Loc.T("ШТУРМ ОТБИТ");
            else head = r.AttackerWon ? Loc.T("КОЛОНИЯ ПАЛА") : Loc.T("ОБОРОНА УСТОЯЛА");
            // Заголовок — стеклянная плашка игры с кромкой цвета исхода и тонкой акцентной чертой
            var ribbon = GlassAt(_overlay, "ResultRibbon", new Vector2(0, 250), new Vector2(860, 100), PanelTint, new Color(accent.r, accent.g, accent.b, 0.75f), false, 20f);
            var racc = LGBuild.Panel(ribbon.rectTransform, "Accent", accent);
            racc.raycastTarget = false;
            racc.rectTransform.At(new Vector2(0.5f, 0f), new Vector2(0.5f, 0.5f), new Vector2(0, 14), new Vector2(120, 2));
            var title = Caption(ribbon.rectTransform, head, 44, accent, TextAnchor.MiddleCenter, 5f);
            title.rectTransform.Stretch(40, 6, 40, 0);
            var sub = Caption(_overlay, Loc.F("{0} · {1} {2}{3}", _setup.PlanetName, r.Turns, Loc.T("ход."), r.Breach ? Loc.T(" · прорыв в Штаб") : ""), 14, Muted, TextAnchor.MiddleCenter, 1.2f);
            sub.rectTransform.At(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 182), new Vector2(900, 24));

            // Плитки с цифрами
            int myLost = r.LostCount[_P], enLost = r.LostCount[_E];
            int veterans = 0, wounded = 0;
            foreach (var s in r.Survivors) if (s.side == _P && s.fought) { veterans++; if (s.hp < 0.5f) wounded++; }
            var tiles = new List<(string label, int val, Color c, LGIcon icon)>
            {
                (Loc.T("ВАШИ ПОТЕРИ"), myLost, new Color(1f, 0.5f, 0.45f), LGIcon.Warning),
                (Loc.T("ПОТЕРИ ВРАГА"), enLost, new Color(1f, 0.82f, 0.4f), LGIcon.Target),
                (Loc.T("ВЕТЕРАНЫ ★"), veterans, new Color(0.45f, 0.9f, 1f), LGIcon.Star),
                (r.MilitiaKilled > 0 ? Loc.T("ПОГИБЛО ЖИТЕЛЕЙ") : Loc.T("РАНЕНЫ"), r.MilitiaKilled > 0 ? r.MilitiaKilled : wounded, new Color(0.85f, 0.85f, 0.85f), LGIcon.Population),
            };
            var counters = new List<(Text t, int v)>();
            for (int i = 0; i < tiles.Count; i++)
            {
                var tl = tiles[i];
                // Плитка — плотная (под стеклом сплошная заливка), иначе сквозь неё читается стол
                var solid = LGBuild.Panel(_overlay, "TileBack", new Color(0.025f, 0.055f, 0.078f, 1f));
                solid.sprite = CardSprites.Round;
                solid.type = Image.Type.Sliced;
                solid.rectTransform.At(Mid, Mid, new Vector2((i - 1.5f) * 210f, 70), new Vector2(192, 124));
                LG.Motion(solid.gameObject, LGAppear.Kind.Pop);
                var p = GlassAt(_overlay, "Tile", new Vector2((i - 1.5f) * 210f, 70), new Vector2(196, 128), SubTint, new Color(tl.c.r, tl.c.g, tl.c.b, 0.55f), true, 16f);
                var ic = LGIcons.Create(p.rectTransform, tl.icon, 18, tl.c);
                ic.rectTransform.At(new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f), new Vector2(0, -20), new Vector2(18, 18));
                var num = Caption(p.rectTransform, "0", 42, tl.c, TextAnchor.MiddleCenter, 0f);
                num.rectTransform.Stretch(0, 28, 0, 30);
                var lab = Caption(p.rectTransform, tl.label, 11, Muted, TextAnchor.LowerCenter, 1.4f);
                lab.rectTransform.Stretch(6, 14, 6, 0);
                LG.Motion(p.gameObject, LGAppear.Kind.Pop);
                counters.Add((num, tl.val));
            }

            // Карты: погибшие ваши (затемнены, навсегда) и выжившие ветераны (+★)
            var dead = new List<BCard>();
            var vets = new List<BCard>();
            foreach (var sec in _state.Sectors)
                foreach (var u in sec.Units)
                {
                    if (u.Side != _P || u.IsTrap || u.Has(Kw.Shield) || u.Card.InstId < 0) continue;
                    if (u.Dead) dead.Add(u.Card); else vets.Add(u.Card);
                }
            var row = LGBuild.Rect(_overlay, "Cards");
            row.At(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -110), new Vector2(1200, 150));
            var all = new List<(BCard c, bool lost)>();
            foreach (var c in dead) all.Add((c, true));
            foreach (var c in vets) all.Add((c, false));
            if (all.Count > 10) all.RemoveRange(10, all.Count - 10);
            var mini = new Vector2(92, 128);
            for (int i = 0; i < all.Count; i++)
            {
                var cv = CardView.Create(row, all[i].c, _P, FactionOf(_P), mini, true);
                cv.Rt.anchoredPosition = new Vector2((i - (all.Count - 1) * 0.5f) * (mini.x + 12f), 0);
                Hook(cv);
                if (all[i].lost) { cv.SetAlpha(0.45f); cv.SetStatus(Loc.T("ПОГИБ"), new Color(1f, 0.35f, 0.3f)); }
                else cv.SetStatus(Loc.T("+★ ОПЫТ"), new Color(0.45f, 0.9f, 1f));
                cv.Punch(0.3f);
            }
            if (r.Trophy != null && _P == BattleState.Attacker && r.AttackerWon)
            {
                var tr = Caption(_overlay, Loc.F("ТРОФЕЙ: {0}", r.Trophy.Name.ToUpper()), 14, Gold, TextAnchor.MiddleCenter, 1.4f);
                tr.rectTransform.At(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -205), new Vector2(900, 24));
            }

            // Кнопка
            var okc = win ? UIManager.DS.Green : Cyan;
            var btn = LGBuild.Button(_overlay, "Continue", win ? UIManager.DS.BtnSuccess : UIManager.DS.BtnPrimary, new Color(okc.r, okc.g, okc.b, 0.85f), Close, null, Loc.T("ПРОДОЛЖИТЬ"), 18, 16f);
            btn.GetComponent<Image>().rectTransform.At(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -280), new Vector2(300, 62));
            var bl = btn.GetComponentInChildren<Text>();
            if (bl != null) bl.Track(2f);
            LG.Motion(btn.gameObject, LGAppear.Kind.Pop);
            _busy = false;

            // Анимация: затемнение, лучи, лента въезжает, цифры набегают
            float t = 0f;
            var rpos = ribbon.rectTransform.anchoredPosition;
            while (t < 1f)
            {
                t += Time.unscaledDeltaTime * 1.6f;
                float e = 1f - Mathf.Pow(1f - Mathf.Clamp01(t), 3f);
                dim.color = new Color(0.005f, 0.01f, 0.02f, 0.9f * e);
                back.color = new Color(0.012f, 0.028f, 0.042f, 0.97f * e);
                backRim.color = new Color(0.02f, 0.05f, 0.07f, 0.35f * e);
                rays.color = new Color(accent.r, accent.g, accent.b, (win ? 0.35f : 0.14f) * e);
                rays2.color = new Color(accent.r, accent.g, accent.b, (win ? 0.5f : 0.2f) * e);
                ribbon.rectTransform.localScale = Vector3.one * Mathf.Lerp(1.6f, 1f, e);
                ribbon.color = new Color(PanelTint.r, PanelTint.g, PanelTint.b, PanelTint.a * e);
                title.color = new Color(accent.r, accent.g, accent.b, e);
                foreach (var cn in counters) cn.t.text = Mathf.RoundToInt(cn.v * e).ToString();
                yield return null;
            }
            foreach (var cn in counters) { cn.t.text = cn.v.ToString(); cn.t.rectTransform.localScale = Vector3.one * 1.2f; }
            SFXManager.Play(Sfx.CardSlam, 0.6f, 0.8f);
            StartCoroutine(SpinRays(rays, rays2, counters));
        }

        /// <summary>Лучи за лентой итогов медленно вращаются, цифры оседают после «щелчка».</summary>
        private IEnumerator SpinRays(Image rays, Image rays2, List<(Text t, int v)> counters)
        {
            while (rays != null && _root != null && _root.activeSelf)
            {
                rays.rectTransform.localRotation = Quaternion.Euler(0, 0, Time.unscaledTime * 6f);
                rays2.rectTransform.localRotation = Quaternion.Euler(0, 0, -Time.unscaledTime * 10f);
                foreach (var cn in counters) if (cn.t != null) cn.t.rectTransform.localScale = Vector3.Lerp(cn.t.rectTransform.localScale, Vector3.one, Time.unscaledDeltaTime * 8f);
                yield return null;
            }
        }
    }
}
