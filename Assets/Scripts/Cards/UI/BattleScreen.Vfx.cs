using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using StellarisClone.Core;
using StellarisClone.Rendering;

namespace StellarisClone.Cards
{
    /// <summary>
    /// Эффекты уровня больших карточных игр поверх стола (свои, в духе научной фантастики, без копирования чужих):
    ///   • огненная «полоса огня» по ряду противника, когда перестрелка переходит в сектор;
    ///   • голографический клинок командира — взмах с режущей дугой (герои, таран);
    ///   • светящийся шлейф и наклон летящей карты, вспышка-линза при раскрытии;
    ///   • попадания из флипбуков (молния, кольцо, крест) в цвете оружия, лучи и искры-штрихи;
    ///   • HD-взрыв техники и разлёт карты 3D-осколками, вихрь над Штабом при прорыве и смене контроля;
    ///   • свои флипбуки fx2_* (Tools/card_fx/gen_fx.py, шейдер FxRamp): звезда удара, рассекающая дуга, ударная волна,
    ///     лучи легендарных, рунный круг приказов, столб света усилений, блёстки, молнии, сотовый щит.
    /// Ленты и искры — ArcFx (одна сетка), флипбуки — UIFx; всё светом, без «пузырей».
    /// </summary>
    public sealed partial class BattleScreen
    {
        private ArcFx _arc;
        private static Sprite s_blade;

        private void VfxInit()
        {
            var go = new GameObject("ArcFx", typeof(RectTransform));
            go.transform.SetParent(_fxLayer, false);
            go.transform.SetAsFirstSibling();   // ленты под спрайтовыми вспышками
            var rt = (RectTransform)go.transform;
            rt.Stretch();
            _arc = go.AddComponent<ArcFx>();
            _arc.Speed = _animSpeed;
            var r = _fxLayer.rect;
            if (r.width > 10f) _arc.AmbientArea = new Rect(r.xMin + 40f, r.yMin + 40f, r.width - 80f, r.height - 140f);
            _arc.Ambient = false;   // тлеющие искры над столом читались как висящие «точки»
        }

        private void VfxTick()
        {
            if (_arc == null || _fx == null) return;
            _arc.Speed = _fx.Speed;
            var r = _fxLayer.rect;
            if (r.width > 10f) _arc.AmbientArea = new Rect(r.xMin + 40f, r.yMin + 40f, r.width - 80f, r.height - 140f);
        }

        private static Color Hot(Color c) => new Color(Mathf.Lerp(c.r, 1f, 0.75f), Mathf.Lerp(c.g, 1f, 0.75f), Mathf.Lerp(c.b, 1f, 0.75f), 1f);
        private static Color Deep(Color c) => new Color(c.r * 0.9f, c.g * 0.45f, c.b * 0.35f, 1f);

        // ---------------------------------------------------------------- огонь по ряду

        /// <summary>
        /// «Полоса огня»: широкая пологая дуга проходит через ряд под обстрелом слева направо, с неё сыплются искры.
        /// Для ряда сверху дуга выгнута вверх, снизу — вниз (огонь идёт со стороны стреляющих).
        /// </summary>
        private void SweepRow(int sector, int row, Color c)
        {
            if (_arc == null || sector < 0 || sector > 2) return;
            var rt = _rows[sector, row];
            if (rt == null) return;
            var rc = _fx.Local(rt);
            float w = Mathf.Max(260f, rt.rect.width);
            float R = w * 0.62f;
            const float squash = 0.3f;
            bool top = row == 0;
            var center = rc + new Vector2(0f, top ? -R * squash * 0.82f : R * squash * 0.82f);
            float a0 = top ? 158f : -158f, a1 = top ? 22f : -22f;
            _arc.Slash(center, R, a0, a1, 60f, Hot(c), c, Deep(c), 0.6f, squash, 0f, true);
            _arc.Glint(rc, c, w * 1.3f, 9f, 0.4f);
            SFXManager.Play(StellarisClone.Core.Audio.Sfx.Whoosh, 0.55f, 1.15f, Pan(rc));
        }

        // ---------------------------------------------------------------- голографический клинок

        private static Sprite Blade
        {
            get
            {
                if (s_blade != null) return s_blade;
                var t = CardSkin.Tex("fx_blade");
                if (t == null) return null;
                t.wrapMode = TextureWrapMode.Clamp;
                s_blade = Sprite.Create(t, new Rect(0, 0, t.width, t.height), new Vector2(0.5f, 0.5f), 100f);
                s_blade.name = "fx_blade";
                return s_blade;
            }
        }

        /// <summary>
        /// Взмах клинка командира: голограмма клинка проворачивается вокруг рукояти от угла a0 к a1,
        /// за остриём идёт режущая дуга, в конце — блик и искры.
        /// </summary>
        private IEnumerator GhostBlade(Vector2 pivot, float a0, float a1, Color c, float length = 520f, float dur = 0.34f)
        {
            var sp = Blade;
            if (sp == null || _fxLayer == null) yield break;
            var go = new GameObject("GhostBlade", typeof(RectTransform));
            go.transform.SetParent(_fxLayer, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(40f / 1024f, 0.5f);   // вращение вокруг рукояти
            rt.sizeDelta = new Vector2(length, length * 0.25f);
            rt.anchoredPosition = pivot;
            var img = go.AddComponent<Image>();
            img.sprite = sp;
            img.raycastTarget = false;
            img.material = UIFx.GlowMat;
            img.color = new Color(c.r, c.g, c.b, 0f);

            float tipR = length * 0.93f;
            _arc?.Slash(pivot, tipR, a0, a1, 46f, Hot(c), c, Deep(c), dur * 1.7f, 1f, 0f, true);
            SFXManager.Play(StellarisClone.Core.Audio.Sfx.Whoosh, 0.8f, 0.85f, Pan(pivot));

            float t = 0f;
            while (t < 1f && go != null)
            {
                t += Time.unscaledDeltaTime * _animSpeed / dur;
                float k = Mathf.Clamp01(t);
                float e = 1f - Mathf.Pow(1f - k, 3f);
                rt.localRotation = Quaternion.Euler(0, 0, Mathf.Lerp(a0, a1, e));
                float alpha = k < 0.15f ? k / 0.15f : 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.6f, 1f, k));
                img.color = new Color(Mathf.Lerp(c.r, 1f, 0.35f), Mathf.Lerp(c.g, 1f, 0.35f), Mathf.Lerp(c.b, 1f, 0.35f), alpha * 0.95f);
                rt.localScale = Vector3.one * (0.92f + 0.12f * e);
                yield return null;
            }
            var tip = pivot + new Vector2(Mathf.Cos(a1 * Mathf.Deg2Rad), Mathf.Sin(a1 * Mathf.Deg2Rad)) * tipR;
            _arc?.Glint(tip, c, 420f, 9f, 0.3f, a1 + 90f);
            _arc?.Embers(tip, Hot(c), new Color(c.r, c.g * 0.5f, c.b * 0.3f, 0f), 24, 200f, 620f, a1 + 90f, 140f, 0.35f, 0.8f, 600f);
            if (go != null) Destroy(go);
        }

        // ---------------------------------------------------------------- попадания и гибель

        /// <summary>Попадание: флипбук в цвете оружия (молния — пули, кольцо — энергия, крест — снайпер), лучи и искры.</summary>
        private void HitFx(Vector2 p, Color c, float ang, float scale, string kind = "bolt")
        {
            // Свои вспышки: звезда удара; энергия — ещё и ударная волна; клинок/снайпер — рассекающая дуга поперёк выстрела
            _fx.Play(UIFx.Fx2.Impact, p, c, 210f * scale, 0.4f, 250f * scale, ang + Random.Range(-30f, 30f));
            if (kind == "ring") _fx.Play(UIFx.Fx2.Shock, p, c, 80f * scale, 0.45f, 300f * scale);
            if (kind == "cross") _fx.Play(UIFx.Fx2.Slash, p, Hot(c), 300f * scale, 0.32f, 340f * scale, ang - 90f + Random.Range(-15f, 15f));
            _arc?.Embers(p, Hot(c), new Color(c.r, c.g * 0.4f, c.b * 0.3f, 0f), Mathf.RoundToInt(6 * scale), 200f, 560f * scale, ang, 110f, 0.25f, 0.55f, 520f);
        }

        /// <summary>Усиление: столб золотого света снизу вверх и блёстки; цвет c — лечение зелёным, укрытие голубым.</summary>
        private void BuffFx(CardView v, Color c, float scale = 1f)
        {
            if (v == null) return;
            var p = _fx.Local(v.Rt);
            _fx.Play(UIFx.Fx2.Pillar, p + new Vector2(0, 40f * scale), c, 150f * scale, 0.75f, 160f * scale);
            _fx.Play(UIFx.Fx2.Sparkle, p, c, 230f * scale, 0.8f, 250f * scale, 0f, 0.05f);
        }

        /// <summary>Энергощит: волна по сотам вокруг карты (укрытие, щит Штаба).</summary>
        private void ShieldFx(Vector2 p, Color c, float size)
        {
            _fx.Play(UIFx.Fx2.HexShield, p, c, size * 0.85f, 0.6f, size);
        }

        /// <summary>Гибель: техника — HD-взрыв, огненный шар и волна; пехота — вспышка-крест и искры.</summary>
        private void KillFx(Vector2 p, bool hard, bool big, Color c)
        {
            if (hard)
            {
                float s = big ? 1.5f : 1.1f;
                _fx.Play(UIFx.Fx2.Burst, p, new Color(1f, 0.6f, 0.25f), 300f * s, 0.9f, 460f * s, Random.Range(0f, 360f));
                _fx.Flipbook("fx_boom_hd", 5, 5, p + new Vector2(0, 20f * s), 300f * s, 1.25f, Color.white, 380f * s, Random.Range(-15f, 15f));
                _fx.Flipbook("fx_fireball", 8, 8, p, 200f * s, 0.75f, new Color(1f, 0.85f, 0.7f, 0.9f), 260f * s, Random.Range(0f, 360f), default, 0f, true);
                _fx.Flipbook("fx_flame", 16, 4, p + new Vector2(0, 40f), 70f * s, 1.6f, new Color(1f, 0.8f, 0.6f, 0.8f), 70f * s, 0f, new Vector2(0, 6f), 0.35f, true, 2f);
                _fx.Flipbook("fx_wisp", 8, 8, p + new Vector2(0, 80f), 220f * s, 2.4f, new Color(0.55f, 0.52f, 0.5f, 0.55f), 320f * s, Random.Range(0f, 360f), new Vector2(8f, 40f), 0.4f);
                _fx.Play(UIFx.Fx2.Shock, p, new Color(1f, 0.6f, 0.3f), 120f * s, 0.6f, 560f * s);
                _arc?.Glint(p, new Color(1f, 0.7f, 0.35f), 700f * s, 12f, 0.4f);
                _arc?.Embers(p, new Color(1f, 0.9f, 0.6f), new Color(0.8f, 0.2f, 0.05f, 0f), Mathf.RoundToInt(40 * s), 250f, 900f * s, 90f, 300f, 0.6f, 1.4f, 900f, 3f);
            }
            else
            {
                _fx.Play(UIFx.Fx2.Impact, p, c, 300f, 0.5f, 360f, Random.Range(0f, 360f));
                _fx.Play(UIFx.Fx2.Shock, p, c, 90f, 0.55f, 340f);
                _arc?.Embers(p, Hot(c), new Color(c.r, c.g * 0.4f, c.b * 0.3f, 0f), 16, 120f, 420f, 90f, 360f, 0.4f, 0.9f, 400f);
            }
        }

        // ---------------------------------------------------------------- осколки

        private sealed class Shard
        {
            public RectTransform Rt;
            public RawImage Img;
            public Vector2 Vel;
            public Vector3 Rot, Spin;
        }

        /// <summary>Карта раскалывается: арт режется на куски, каждый летит от центра, кувыркаясь в 3D (Perspective3D).</summary>
        private void ShardsFx(CardView v, Color edge)
        {
            var art = v != null ? v.ArtImage : null;
            if (art == null || art.texture == null || _fxLayer == null) return;
            var center = _fx.Local(v.Rt);
            float k = v.Rt.lossyScale.x / Mathf.Max(1e-5f, _fxLayer.lossyScale.x);
            var size = v.Rt.rect.size * k;
            const int cols = 3, rows = 4;
            var cell = new Vector2(size.x / cols, size.y / rows);
            var uv = art.uvRect;
            var list = new System.Collections.Generic.List<Shard>(cols * rows);
            for (int y = 0; y < rows; y++)
                for (int x = 0; x < cols; x++)
                {
                    var go = new GameObject("Shard", typeof(RectTransform));
                    go.transform.SetParent(_fxLayer, false);
                    var rt = (RectTransform)go.transform;
                    rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                    rt.sizeDelta = cell * Random.Range(0.9f, 1f);
                    var off = new Vector2((x + 0.5f) * cell.x - size.x * 0.5f, (y + 0.5f) * cell.y - size.y * 0.5f);
                    rt.anchoredPosition = center + off;
                    var img = go.AddComponent<RawImage>();
                    img.texture = art.texture;
                    img.uvRect = new Rect(uv.x + uv.width * x / cols, uv.y + uv.height * y / rows, uv.width / cols, uv.height / rows);
                    img.color = new Color(0.9f, 0.86f, 0.82f, 1f);
                    img.raycastTarget = false;
                    var ol = go.AddComponent<Outline>();
                    ol.effectColor = edge;
                    ol.effectDistance = new Vector2(2f, -2f);
                    go.AddComponent<Perspective3D>().Root = rt;
                    var dir = off.sqrMagnitude > 1f ? off.normalized : Random.insideUnitCircle.normalized;
                    list.Add(new Shard
                    {
                        Rt = rt, Img = img,
                        Vel = dir * Random.Range(260f, 620f) + new Vector2(Random.Range(-60f, 60f), Random.Range(280f, 520f)),
                        Spin = new Vector3(Random.Range(-1f, 1f) * Random.Range(220f, 560f), Random.Range(-1f, 1f) * Random.Range(220f, 560f), Random.Range(-200f, 200f)),
                    });
                }
            StartCoroutine(FlyShards(list, 1.15f));
        }

        private IEnumerator FlyShards(System.Collections.Generic.List<Shard> list, float dur)
        {
            float t = 0f;
            while (t < dur)
            {
                float dt = Time.unscaledDeltaTime * _animSpeed;
                t += dt;
                float a = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(dur * 0.5f, dur, t));
                foreach (var s in list)
                {
                    if (s.Rt == null) continue;
                    s.Vel.y -= 1500f * dt;
                    s.Vel *= 1f / (1f + 0.6f * dt);
                    s.Rt.anchoredPosition += s.Vel * dt;
                    s.Rot += s.Spin * dt;
                    s.Rt.localRotation = Quaternion.Euler(s.Rot);
                    var c = s.Img.color; c.a = a; s.Img.color = c;
                    s.Img.SetVerticesDirty();
                }
                yield return null;
            }
            foreach (var s in list) if (s.Rt != null) Destroy(s.Rt.gameObject);
        }

        // ---------------------------------------------------------------- карты

        /// <summary>Карта ложится на стол: наклон «в воздухе» (стол выпрямит её сам), светящийся шлейф до места, волна на месте.</summary>
        private void FlyFx(CardView v, Color c)
        {
            if (v == null || _arc == null) return;
            v.Rt.localRotation = Quaternion.Euler(Random.Range(28f, 40f), Random.Range(-22f, 22f), Random.Range(-14f, 14f));
            _arc.Trail(v.Rt, c, 34f, 0.3f);
            StartCoroutine(Later(0.28f, () =>
            {
                if (v == null || _arc == null) return;
                var p = _fx.Local(v.Rt);
                _fx.Play(UIFx.Fx2.Shock, p, c, 90f, 0.45f, 300f);
                _arc.Embers(p + new Vector2(0, -70f), Hot(c), new Color(c.r, c.g, c.b, 0f), 12, 80f, 320f, 90f, 160f, 0.3f, 0.6f, 500f);
            }));
        }

        /// <summary>Раскрытие карты: блик-линза по горизонтали, тонкая волна и лучи (герой — ещё и взмах клинка).</summary>
        private void RevealFx(CardView v, bool heavy, bool hero)
        {
            if (v == null || _arc == null) return;
            var p = _fx.Local(v.Rt);
            var c = v.Side == _P ? new Color(0.55f, 0.9f, 1f) : new Color(1f, 0.55f, 0.4f);
            if (hero) c = new Color(1f, 0.82f, 0.4f);
            _arc.Glint(p, c, heavy || hero ? 640f : 420f, heavy || hero ? 10f : 7f, 0.34f);
            _fx.Play(UIFx.Fx2.Shock, p, c, 80f, 0.5f, heavy ? 380f : 280f);
            if (heavy || hero) _fx.Play(UIFx.Fx2.Burst, p, hero ? new Color(1f, 0.8f, 0.35f) : c, 240f, 0.8f, 360f, Random.Range(0f, 360f));
            if (hero)
            {
                bool mine = v.Side == _P;
                StartCoroutine(GhostBlade(p + new Vector2(mine ? -230f : 230f, mine ? -60f : 60f), mine ? 150f : -30f, mine ? 20f : -160f, c, 470f, 0.36f));
            }
        }

        /// <summary>Вихрь над Штабом / дорожкой сектора: прорыв, смена контроля.</summary>
        private void VortexFx(Vector2 p, Color c, float scale = 1f)
        {
            _arc?.Vortex(p, 150f * scale, c, 1.1f, 3, 15f * scale, -460f);
            _fx.Play(UIFx.Fx2.Burst, p, c, 280f * scale, 0.9f, 420f * scale, Random.Range(0f, 360f));
            _fx.Play(UIFx.Fx2.Shock, p, c, 100f * scale, 0.6f, 480f * scale);
        }
    }
}
