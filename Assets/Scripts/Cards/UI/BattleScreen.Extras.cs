using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using StellarisClone.Core;
using StellarisClone.Rendering;
using Sfx = StellarisClone.Core.Audio.Sfx;

namespace StellarisClone.Cards
{
    /// <summary>
    /// Новые механики на экране боя: прогноз хода по секторам, индикатор доктрины фракции,
    /// кнопка приказа героя (H) и показ событий «доктрина», «приказ героя», «событие боя».
    /// </summary>
    public partial class BattleScreen
    {
        private const int ForecastSamples = 12;

        private Text[] _fcText, _docText;
        private Image[] _fcChip, _docChip;
        private Button _heroButton;
        private Text _heroLabel;
        private TooltipTrigger _heroTip;
        private string _fcSig;
        private float _fcDelay;
        private BattleState.TurnForecast _fc;

        // ==================== ПОСТРОЕНИЕ ====================

        private void BuildExtras()
        {
            _fcText = new Text[3]; _docText = new Text[3];
            _fcChip = new Image[3]; _docChip = new Image[3];
            _fcSig = null;
            _fc = null;
            BuildJuice();
            for (int i = 0; i < 3; i++)
            {
                var col = _sec[i].Col;
                const float y = -88f;
                _fcChip[i] = ChipAt(col, "Forecast", new Vector2(-122, y), new Vector2(196, 26), SubTint, new Color(RimCyan.r, RimCyan.g, RimCyan.b, 0.5f), raycast: true);
                _fcText[i] = Caption(_fcChip[i].rectTransform, "", 12, UIManager.DS.TextPrimary, TextAnchor.MiddleCenter, 0.6f);
                _fcText[i].rectTransform.Stretch(6, 0, 6, 0);
                _fcText[i].supportRichText = true;
                TooltipHelper.Attach(_fcChip[i].gameObject, Loc.F(
                    "<b>Прогноз хода</b>\nНа основе {0} прогонов этого хода с вашими выложенными картами: враг отвечает как ИИ, его карты вам неизвестны.\n" +
                    "Шанс — сектор останется за вами после перестрелки; потери — сколько ваших : вражеских юнитов в среднем погибнет", ForecastSamples));

                _docChip[i] = ChipAt(col, "Doctrine", new Vector2(50, y), new Vector2(140, 26), SubTint, new Color(Gold.r, Gold.g, Gold.b, 0.45f), raycast: true);
                _docText[i] = Caption(_docChip[i].rectTransform, "", 11, Gold, TextAnchor.MiddleCenter, 0.8f);
                _docText[i].rectTransform.Stretch(6, 0, 6, 0);
                _docText[i].supportRichText = true;
                string f = _state.Sides[_P].Faction;
                TooltipHelper.Attach(_docChip[i].gameObject, "<b>" + Loc.T("Доктрина") + ": " + (BattleState.DoctrineName(f) ?? "—") + "</b>\n" + (BattleState.DoctrineText(f) ?? ""));
                _docChip[i].gameObject.SetActive(BattleState.DoctrineName(f) != null);
            }

            // Кнопка приказа героя: под гнездом хода (под наёмниками, если они есть)
            bool merc = _state.Sides[_P].Faction == "aquila";
            Vector2 pos = BoardSkin ? new Vector2(RX, merc ? -318f : -268f) : new Vector2(RX, -100f);
            _heroButton = LGBuild.Button(_board, "HeroPower", UIManager.DS.BtnNeutral, new Color(1f, 0.78f, 0.35f, 0.7f), OnHeroPower, LGIcon.Leader, Loc.T("ПРИКАЗ ГЕРОЯ"), 12, 14f);
            _heroButton.GetComponent<Image>().rectTransform.At(Mid, Mid, pos, new Vector2(196, 44));
            _heroLabel = _heroButton.GetComponentInChildren<Text>();
            TooltipHelper.Attach(_heroButton.gameObject, "");
            _heroTip = _heroButton.GetComponent<TooltipTrigger>();
            _heroButton.gameObject.SetActive(false);
        }

        // ==================== ОБНОВЛЕНИЕ ====================

        private void RefreshExtras()
        {
            if (_fcText == null || _state == null) return;
            for (int i = 0; i < 3; i++)
            {
                if (_docText[i] == null || !_docChip[i].gameObject.activeSelf) continue;
                int n = _state.DoctrineCount(_P, i), need = _state.DoctrineNeed(_P);
                bool on = n >= need;
                _docText[i].text = on
                    ? "<color=#FFD36B>" + BattleState.DoctrineName(_state.Sides[_P].Faction).ToUpper() + "</color>"
                    : Loc.F("<color=#9AA6B0>доктрина {0}/{1}</color>", n, need);
            }
            RefreshHeroButton();
        }

        private void RefreshHeroButton()
        {
            if (_heroButton == null) return;
            var me = _state.Sides[_P];
            var hero = _state.HeroOnBoard(_P);
            bool show = !_state.Over && (hero != null || me.HeroPowerUsed) && BattleState.EnableHeroPowers;
            if (_heroButton.gameObject.activeSelf != show) _heroButton.gameObject.SetActive(show);
            if (!show) return;
            var def = hero != null ? hero.Def : null;
            string reason = _state.CanHeroPower(_P);
            if (me.HeroPowerQueued)
            {
                _heroLabel.text = Loc.T("ОТМЕНИТЬ ПРИКАЗ");
                _heroButton.interactable = !_busy;
            }
            else
            {
                _heroLabel.text = me.HeroPowerUsed ? Loc.T("ПРИКАЗ ОТДАН") : (BattleState.HeroPowerName(def) ?? Loc.T("ПРИКАЗ ГЕРОЯ")).ToUpper() + "  (H)";
                _heroButton.interactable = !_busy && reason == null;
            }
            if (_heroTip != null)
                _heroTip.text = "<b>" + (BattleState.HeroPowerName(def) ?? Loc.T("Приказ героя")) + "</b>\n" + (BattleState.HeroPowerText(def) ?? "") +
                                (reason != null && !me.HeroPowerQueued ? "\n<color=#FF9A7A>" + reason + "</color>" : "") +
                                "\n<color=#8AA2A8>" + Loc.T("Сработает при подведении хода, после раскрытия карт. Клавиша H") + "</color>";
        }

        /// <summary>Каждый кадр: пересчитать прогноз, когда изменился расклад игрока (с небольшой задержкой).</summary>
        private void TickExtras(float dt)
        {
            if (_fcText == null || _state == null) return;
            if (Input.GetKeyDown(KeyCode.H) && !_busy) OnHeroPower();

            bool planning = !_busy && !_state.Over && !_state.IsForecast;
            for (int i = 0; i < 3; i++) if (_fcChip[i].gameObject.activeSelf != planning) _fcChip[i].gameObject.SetActive(planning);
            if (!planning) { _fcSig = null; return; }

            var me = _state.Sides[_P];
            string sig = _state.Turn + ":" + me.Points + ":" + me.Pending.Count + ":" + me.HeroPowerQueued + ":" + _state.Events.Count;
            if (sig != _fcSig) { _fcSig = sig; _fcDelay = 0.12f; _fc = null; for (int i = 0; i < 3; i++) _fcText[i].text = "<color=#8AA2A8>…</color>"; }
            if (_fc != null) return;
            _fcDelay -= dt;
            if (_fcDelay > 0f) return;
            _fc = _state.Forecast(_P, ForecastSamples, Mathf.Max(2, _setup.AiSkill));
            for (int i = 0; i < 3; i++)
            {
                int pct = Mathf.RoundToInt(_fc.Hold[i] * 100f);
                string col = pct >= 65 ? "7CF29A" : pct >= 35 ? "F2C766" : "FF8A80";
                _fcText[i].text = Loc.F("<color=#{0}><b>{1}%</b></color> <color=#9AA6B0>удержать</color>  <color=#8AA2A8>потери {2:0.#}:{3:0.#}</color>",
                                         col, pct, _fc.MyLoss[i], _fc.EnemyLoss[i]);
            }
            if (_fc.WinNow >= 0.5f) Hint(Loc.F("Прогноз: этот ход может решить бой в вашу пользу ({0:0}%)", _fc.WinNow * 100f));
            else if (_fc.LoseNow >= 0.5f) Hint(Loc.F("Прогноз: при таком ходе бой, скорее всего, будет проигран ({0:0}%)", _fc.LoseNow * 100f), true);
        }

        private void OnHeroPower()
        {
            if (_busy || _state == null || _state.Over || _heroButton == null || !_heroButton.gameObject.activeSelf) return;
            var me = _state.Sides[_P];
            if (!me.HeroPowerQueued && _state.CanHeroPower(_P) != null) { SFXManager.Play(Sfx.UiDenied); Hint(_state.CanHeroPower(_P), true); return; }
            _state.ToggleHeroPower(_P);
            SFXManager.Play(me.HeroPowerQueued ? Sfx.UiConfirm : Sfx.UiBack);
            var hero = _state.HeroOnBoard(_P);
            if (me.HeroPowerQueued && hero != null)
            {
                Hint(Loc.F("Приказ героя «{0}» сработает при подведении хода", BattleState.HeroPowerName(hero.Def)));
                var v = ViewOf(hero.Uid);
                if (v != null) { v.EdgeFlash(new Color(1f, 0.8f, 0.35f), 1f); BuffFx(v, new Color(1f, 0.78f, 0.3f)); }
            }
            RefreshHeroButton();
        }

        // ==================== СОБЫТИЯ ====================

        private IEnumerator OnSynergy(BEvent e)
        {
            string name = BattleState.DoctrineName(e.Text);
            if (name == null || e.Sector < 0) yield break;
            bool mine = e.Side == _P;
            var c = mine ? new Color(1f, 0.82f, 0.4f) : new Color(1f, 0.5f, 0.42f);
            var p = _fx.Local(_sec[e.Sector].PowerBar);
            _fx.Ring(p, new Color(c.r, c.g, c.b, 0.85f), 40f, 360f, 0.5f);
            VortexFx(p, c, 0.6f);
            SFXManager.Play(Sfx.FxBuff, 0.9f, 0.9f, Pan(p));
            AddLog(Loc.F("<color=#{0}>Доктрина «{1}» — {2}, сектор «{3}»</color>", mine ? "FFD36B" : "FF8A80", name, SideName(e.Side),
                         CardArt.SectorName(_state.Sectors[e.Sector].Type)));
            StartCoroutine(Callout(name.ToUpper(), c, 0.9f));
            yield return W(0.35f);
        }

        private IEnumerator OnHeroPowerEvent(BEvent e)
        {
            var hero = _state.FindUnit(e.Uid);
            var def = hero != null ? hero.Def : CardCatalog.Get(e.Text);
            string name = BattleState.HeroPowerName(def) ?? Loc.T("Приказ героя");
            bool mine = e.Side == _P;
            var c = new Color(1f, 0.8f, 0.38f);
            var v = ViewOf(e.Uid);
            Vector2 p = v != null ? _fx.Local(v.Rt) : _fx.Local(_sec[Mathf.Max(0, e.Sector)].Col);
            if (v != null) { v.Punch(0.25f); v.EdgeFlash(c, 1.4f); v.GlowBoost = 1.5f; }
            _fx.Flash(c, 0.25f, 1.1f);
            _fx.Ring(p, new Color(c.r, c.g, c.b, 0.95f), 30f, 520f, 0.6f);
            _fx.Sparks(p, c, 40, 1.4f);
            _fx.Shake(10f, 0.35f);
            SFXManager.Play(Sfx.Radio, 0.8f);
            SFXManager.Play(Sfx.FxBuff, 1f, 0.8f, Pan(p));
            AddLog(Loc.F("<color=#FFD36B>{0}: приказ героя «{1}»</color>", SideName(e.Side), name));
            yield return Banner(name.ToUpper(), (mine ? Loc.T("Ваш герой") : Loc.T("Вражеский герой")) + " — " + (BattleState.HeroPowerText(def) ?? ""), c, 1.1f);
        }

        private IEnumerator OnBattleEvent(BEvent e)
        {
            RefreshPanels();
            var c = new Color(0.75f, 0.6f, 1f);
            SFXManager.Play(Sfx.Radio);
            SFXManager.PlayDelayed(Sfx.BoomFar, 0.3f, 0.9f);
            if (e.Sector >= 0 && e.Text == "debris")
            {
                var p = _fx.Local(_sec[e.Sector].Col);
                for (int i = 0; i < 4; i++) _fx.Explosion(p + Random.insideUnitCircle * 170f, 1f);
                _fx.Shake(14f, 0.5f);
                SFXManager.Play(Sfx.Boom, 0.9f);
            }
            AddLog(Loc.F("<color=#C4A8FF>Событие боя: {0}</color>", _state.EventName ?? e.Text));
            yield return Banner(_state.EventName ?? e.Text, _state.EventText ?? "", c, 1.5f);
        }
    }
}
