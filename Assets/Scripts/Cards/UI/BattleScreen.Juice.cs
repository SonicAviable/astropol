using UnityEngine;
using UnityEngine.UI;
using StellarisClone.Core;
using StellarisClone.Rendering;
using Sfx = StellarisClone.Core.Audio.Sfx;

namespace StellarisClone.Cards
{
    /// <summary>
    /// «Сочность» боя — то, что делает удары весомыми, как в больших играх:
    /// • стоп-кадр (hit-stop) в момент попадания и гибели — весь стол замирает на доли секунды;
    /// • замедление на крупных убийствах, прорыве и захвате сектора;
    /// • цветная виньетка по краям экрана (ваши потери — красная, вражеские — бирюзовая);
    /// • световой «бутон» в точке попадания и тряска, растущие с уроном;
    /// • серии убийств: «ДВОЙНОЙ УДАР», «ТРОЙНОЙ УДАР», «РАЗГРОМ», «БОЙНЯ».
    /// При ускорении (×2, пробел) стоп-кадры и замедления отключаются — темп важнее.
    /// </summary>
    public partial class BattleScreen
    {
        private float _stopLeft, _slowLeft, _slowFactor = 1f;
        private Image _vignette;
        private float _vigA;
        private Color _vigC = Color.red;
        private int _chain, _chainVictim = -1;
        private float _chainUntil;

        /// <summary>Множитель хода времени анимаций: 0.04 в стоп-кадре, меньше 1 в замедлении.</summary>
        private float TimeFactor => _stopLeft > 0f ? 0.04f : _slowLeft > 0f ? _slowFactor : 1f;

        private bool JuiceOn => _animSpeed < 1.9f && !Fast && PlayerPrefs.GetInt("set.cardjuice", 1) == 1;

        private void BuildJuice()
        {
            if (_overlay == null) return;
            _vignette = LGBuild.Panel(_overlay, "JuiceVignette", new Color(1f, 0f, 0f, 0f));
            _vignette.sprite = CardSprites.Vignette;
            _vignette.raycastTarget = false;
            _vignette.rectTransform.Stretch(-60, -60, -60, -60);
            _vignette.transform.SetAsFirstSibling();
            _stopLeft = _slowLeft = 0f;
            _chain = 0;
        }

        private void HitStop(float s)
        {
            if (!JuiceOn) return;
            _stopLeft = Mathf.Max(_stopLeft, s);
        }

        private void SlowMo(float s, float factor)
        {
            if (!JuiceOn) return;
            _slowLeft = Mathf.Max(_slowLeft, s);
            _slowFactor = _slowLeft > 0f && _slowFactor < 1f ? Mathf.Min(_slowFactor, factor) : factor;
        }

        private void Vignette(Color c, float strength)
        {
            _vigC = c;
            _vigA = Mathf.Max(_vigA, strength);
        }

        /// <summary>Каждый кадр (реальное время): таймеры стоп-кадра и замедления, виньетка, скорость частиц.</summary>
        private void JuiceTick()
        {
            float dt = Time.unscaledDeltaTime;
            if (_stopLeft > 0f) _stopLeft -= dt;
            else if (_slowLeft > 0f) { _slowLeft -= dt; if (_slowLeft <= 0f) _slowFactor = 1f; }
            if (_fx != null) _fx.Speed = _animSpeed * TimeFactor;
            if (_vignette != null)
            {
                _vigA = Mathf.MoveTowards(_vigA, 0f, dt * 1.6f);
                _vignette.color = new Color(_vigC.r, _vigC.g, _vigC.b, _vigA);
            }
            if (_chain > 0 && Time.unscaledTime > _chainUntil) _chain = 0;
        }

        /// <summary>Усиление события в момент, когда экран начинает его показывать.</summary>
        private void JuiceEvent(BEvent e)
        {
            switch (e.T)
            {
                case EvType.Damage:
                {
                    var v = ViewOf(e.Uid);
                    if (v == null) break;
                    var p = _fx.Local(v.Rt);
                    int dmg = Mathf.Max(1, e.Amount);
                    bool mine = v.Side == _P;
                    var hot = mine ? new Color(1f, 0.35f, 0.25f) : new Color(1f, 0.78f, 0.42f);
                    // Световой «бутон» и ударная волна, растущие с уроном
                    _fx.Ring(p, new Color(1f, 0.95f, 0.85f, 0.95f), 30f, 180f + dmg * 45f, 0.22f, CardSkin.Spr("glow"));
                    _fx.Ring(p, new Color(hot.r, hot.g, hot.b, 0.8f), 20f, 140f + dmg * 40f, 0.35f);
                    _fx.Sparks(p, hot, 8 + dmg * 4, 0.8f + dmg * 0.12f);
                    _fx.Shake(3f + dmg * 2.2f, 0.12f + dmg * 0.03f);
                    Kick(Mathf.Min(0.024f, 0.003f + dmg * 0.0022f));
                    v.Hit(Mathf.Clamp(dmg / 2.5f, 0.7f, 1.6f));
                    HitStop(Mathf.Clamp(0.03f + dmg * 0.014f, 0.04f, 0.12f));
                    if (dmg >= 5)
                    {
                        _fx.Flash(new Color(1f, 0.95f, 0.85f), 0.22f, 3.5f);
                        _fx.Number(p + new Vector2(0, 74), Loc.T("СОКРУШИТЕЛЬНО"), new Color(1f, 0.85f, 0.4f), 22, 0.9f);
                    }
                    if (mine) Vignette(new Color(1f, 0.15f, 0.1f), Mathf.Min(0.5f, 0.12f + dmg * 0.05f));
                    break;
                }
                case EvType.Death:
                {
                    var v = ViewOf(e.Uid);
                    if (v == null) break;
                    var p = _fx.Local(v.Rt);
                    var def = v.Card.Def;
                    bool big = def.Cost >= 5 || def.Kind == CardKind.Hero;
                    bool mine = v.Side == _P;
                    var c = mine ? new Color(1f, 0.3f, 0.22f) : new Color(0.4f, 1f, 0.85f);
                    _fx.Ring(p, new Color(1f, 1f, 1f, 0.95f), 40f, big ? 720f : 420f, big ? 0.45f : 0.3f, CardSkin.Spr("glow"));
                    _fx.Ring(p, new Color(c.r, c.g, c.b, 0.9f), 30f, big ? 900f : 520f, big ? 0.6f : 0.42f);
                    _arc?.RingWave(p, new Color(c.r, c.g, c.b, 1f), 40f, big ? 640f : 380f, big ? 14f : 9f, big ? 0.6f : 0.4f, 0.6f);
                    _fx.Sparks(p, new Color(1f, 0.85f, 0.55f), big ? 70 : 30, big ? 2f : 1.3f);
                    HitStop(big ? 0.16f : 0.08f);
                    if (big)
                    {
                        SlowMo(0.7f, 0.3f);
                        _fx.Flash(mine ? new Color(1f, 0.3f, 0.2f) : new Color(1f, 0.9f, 0.7f), 0.35f, 2f);
                        _fx.Shake(22f, 0.5f);
                        Kick(0.03f);
                        _fx.Number(p + new Vector2(0, 90), Loc.T("УНИЧТОЖЕН"), c, 34, 1.4f);
                        SFXManager.Play(Sfx.BoomFar, 0.7f, 0.8f);
                    }
                    Vignette(mine ? new Color(1f, 0.1f, 0.08f) : new Color(0.2f, 1f, 0.8f), big ? 0.75f : 0.4f);

                    // Серия убийств одной стороны подряд
                    if (_chain > 0 && _chainVictim == v.Side && Time.unscaledTime <= _chainUntil) _chain++;
                    else { _chain = 1; _chainVictim = v.Side; }
                    _chainUntil = Time.unscaledTime + 2.2f;
                    string streak = _chain == 2 ? Loc.T("ДВОЙНОЙ УДАР") : _chain == 3 ? Loc.T("ТРОЙНОЙ УДАР") : _chain == 4 ? Loc.T("РАЗГРОМ") : _chain >= 5 ? Loc.T("БОЙНЯ") : null;
                    if (streak != null)
                    {
                        StartCoroutine(Callout(streak, mine ? new Color(1f, 0.42f, 0.36f) : new Color(0.45f, 1f, 0.85f), 1f));
                        SFXManager.Play(Sfx.CardSlam, 0.8f, 0.75f + _chain * 0.05f);
                        if (_chain >= 4) { _fx.Flash(mine ? new Color(1f, 0.2f, 0.15f) : new Color(0.3f, 1f, 0.8f), 0.3f, 2f); Kick(0.025f); }
                    }
                    break;
                }
                case EvType.Shot:
                {
                    var src = ViewOf(e.Uid);
                    if (src == null) break;
                    var d = src.Card.Def;
                    if (d.Has(Kw.Vehicle) || d.Cost >= 5)
                    {
                        var p = _fx.Local(src.Rt);
                        _fx.Ring(p, new Color(1f, 0.9f, 0.7f, 0.7f), 20f, 260f, 0.2f, CardSkin.Spr("glow"));
                        Kick(0.006f);
                    }
                    break;
                }
                case EvType.Control:
                {
                    bool mine = e.Side == _P;
                    HitStop(0.1f);
                    SlowMo(0.5f, 0.45f);
                    Kick(0.026f);
                    Vignette(mine ? new Color(0.2f, 1f, 0.75f) : new Color(1f, 0.15f, 0.1f), 0.7f);
                    break;
                }
                case EvType.Breach:
                    SlowMo(1.4f, 0.35f);
                    Vignette(new Color(1f, 0.25f, 0.1f), 1f);
                    break;
                case EvType.ShieldDown:
                    HitStop(0.14f);
                    Vignette(new Color(0.4f, 0.8f, 1f), 0.8f);
                    break;
                case EvType.HeroPower:
                    HitStop(0.1f);
                    Vignette(new Color(1f, 0.8f, 0.35f), 0.7f);
                    break;
                case EvType.Synergy:
                    Vignette(e.Side == _P ? new Color(1f, 0.82f, 0.4f) : new Color(1f, 0.4f, 0.3f), 0.35f);
                    break;
                case EvType.OrderFx:
                    Kick(0.012f);
                    break;
                case EvType.TurnStart:
                    _chain = 0;
                    break;
            }
        }
    }
}
