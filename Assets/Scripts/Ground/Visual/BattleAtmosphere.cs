using System.Collections.Generic;
using UnityEngine;
using StellarisClone.Rendering;

namespace StellarisClone.Ground
{
    /// <summary>
    /// Атмосфера поля боя: пыль в воздухе вокруг камеры, столбы чёрного дыма от горящих остовов по краям карты
    /// (с отсветом огня у основания), пепел и угли на вулканических мирах. Делает поле «уже воюющим» с первой секунды.
    /// </summary>
    public sealed class BattleAtmosphere : MonoBehaviour
    {
        private GroundMap _map;
        private ParticleSystem _motes, _plumes, _embers;
        private readonly List<(Vector3 pos, float rate, Light glow, float seed)> _fires = new List<(Vector3, float, Light, float)>();
        private Material _smokeMat, _addMat;
        private float _acc;

        public static BattleAtmosphere Create(Transform root, GroundMap map)
        {
            var go = new GameObject("Atmosphere");
            go.transform.SetParent(root, false);
            var a = go.AddComponent<BattleAtmosphere>();
            a._map = map;
            a.Build();
            return a;
        }

        private void Build()
        {
            var b = _map.Biome;
            _smokeMat = new Material(ShaderCache.Sprite) { name = "AtmoSmoke", hideFlags = HideFlags.DontSave, mainTexture = FxTextures.Smoke };
            _addMat = new Material(GroundMats.Additive) { name = "AtmoAdd", hideFlags = HideFlags.DontSave, mainTexture = FxTextures.Soft };

            // Пыль в воздухе: медленные светлые пылинки у камеры, видны в контровом свете
            _motes = Ps("Motes", _smokeMat, 600);
            var m = _motes.main;
            m.loop = true;
            m.startLifetime = new ParticleSystem.MinMaxCurve(6f, 10f);
            m.startSize = new ParticleSystem.MinMaxCurve(b.Ash ? 0.12f : 0.6f, b.Ash ? 0.25f : 2.2f);
            m.startSpeed = 0f;
            m.startColor = b.Ash ? new Color(0.2f, 0.2f, 0.2f, 0.8f) : new Color(b.Dust.r, b.Dust.g, b.Dust.b, b.Dust.a * 0.5f);
            m.gravityModifier = b.Ash ? 0.02f : 0f;
            var em = _motes.emission;
            em.enabled = true;
            em.rateOverTime = b.Ash ? 60f : 22f;
            var sh = _motes.shape;
            sh.enabled = true;
            sh.shapeType = ParticleSystemShapeType.Box;
            sh.scale = new Vector3(70f, 14f, 70f);
            var vel = _motes.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(0.6f, 1.6f);
            vel.y = new ParticleSystem.MinMaxCurve(b.Ash ? -0.8f : -0.1f, b.Ash ? -0.3f : 0.15f);
            vel.z = new ParticleSystem.MinMaxCurve(0.2f, 0.8f);
            Fade(_motes, 0.6f);
            _motes.Play();

            // Столбы дыма: остовы горят по краям поля и на нейтральной полосе
            _plumes = Ps("Plumes", _smokeMat, 1400);
            var pm = _plumes.main;
            pm.gravityModifier = 0f;
            var rot = _plumes.rotationOverLifetime;
            rot.enabled = true;
            rot.z = new ParticleSystem.MinMaxCurve(-0.25f, 0.25f);
            var sz = _plumes.sizeOverLifetime;
            sz.enabled = true;
            sz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.35f, 1f, 1.6f));
            Fade(_plumes, 0.15f);
            _plumes.Play();
            // Дым идёт от горящих остовов на карте; если их нет — от пожаров за краем поля
            var spots = new List<Vector3>(_map.Burning);
            var rng = new System.Random(_map.Origin.GetHashCode() ^ 7919);
            while (spots.Count < 3)
            {
                float side = spots.Count % 2 == 0 ? -1f : 1f;
                spots.Add(_map.ToWorld(side * (100f + (float)rng.NextDouble() * 25f), -70f + (float)rng.NextDouble() * 150f));
            }
            for (int i = 0; i < spots.Count; i++)
            {
                var w = spots[i];
                Light glow = null;
                if (i < 4)
                {
                    glow = new GameObject("FireGlow").AddComponent<Light>();
                    glow.transform.SetParent(transform, false);
                    glow.transform.position = w + Vector3.up * 2f;
                    glow.type = LightType.Point;
                    glow.color = new Color(1f, 0.5f, 0.2f);
                    glow.range = 16f;
                    glow.intensity = 3f;
                    glow.shadows = LightShadows.None;
                }
                _fires.Add((w, 6f + (float)rng.NextDouble() * 5f, glow, (float)rng.NextDouble() * 10f));
            }

            // Угли над лавой
            if (b.LavaRiver || b.Type == Core.PlanetType.Molten)
            {
                _embers = Ps("Embers", _addMat, 500);
                var e = _embers.main;
                e.loop = true;
                e.startLifetime = new ParticleSystem.MinMaxCurve(3f, 6f);
                e.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.2f);
                e.startColor = new Color(1f, 0.45f, 0.1f, 1f);
                var ee = _embers.emission;
                ee.enabled = true;
                ee.rateOverTime = 40f;
                var es = _embers.shape;
                es.enabled = true;
                es.shapeType = ParticleSystemShapeType.Box;
                es.scale = new Vector3(80f, 1f, 80f);
                var ev = _embers.velocityOverLifetime;
                ev.enabled = true;
                ev.space = ParticleSystemSimulationSpace.World;
                ev.x = new ParticleSystem.MinMaxCurve(-0.4f, 0.8f);
                ev.y = new ParticleSystem.MinMaxCurve(0.8f, 2.4f);
                ev.z = new ParticleSystem.MinMaxCurve(-0.4f, 0.4f);
                Fade(_embers, 0.3f);
                _embers.Play();
            }
        }

        private ParticleSystem Ps(string name, Material mat, int max)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = false;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = max;
            var em = ps.emission;
            em.enabled = false;
            var sh = ps.shape;
            sh.enabled = false;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.sortingFudge = 10f;
            return ps;
        }

        private static void Fade(ParticleSystem ps, float inT)
        {
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, inT), new GradientAlphaKey(0.7f, 0.7f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
        }

        private void Update()
        {
            var cam = BattleCamera.Instance;
            if (cam != null)
            {
                var f = cam.Focus;
                if (_motes != null) _motes.transform.position = f + Vector3.up * 6f;
                if (_embers != null) _embers.transform.position = f;
            }
            if (_plumes == null) return;
            float dt = Time.deltaTime;
            _acc += dt;
            float t = Time.time;
            foreach (var fire in _fires)
            {
                if (fire.glow != null)
                    fire.glow.intensity = 2.2f + Mathf.PerlinNoise(t * 6f, fire.seed) * 2.4f;
                if (Random.value > fire.rate * dt) continue;
                var e = new ParticleSystem.EmitParams
                {
                    position = fire.pos + new Vector3(Random.Range(-1.2f, 1.2f), Random.Range(0.5f, 2f), Random.Range(-1.2f, 1.2f)),
                    velocity = new Vector3(Random.Range(0.4f, 1.6f), Random.Range(2.4f, 4f), Random.Range(-0.3f, 0.6f)),
                    startSize = Random.Range(4f, 7f),
                    startLifetime = Random.Range(9f, 14f),
                    rotation = Random.Range(0f, 360f),
                    startColor = new Color(0.08f, 0.075f, 0.07f, Random.Range(0.55f, 0.8f)),
                    applyShapeToPosition = false
                };
                _plumes.Emit(e, 1);
                if (Random.value < 0.35f) GoreFx.Flame(fire.pos + Vector3.up * Random.Range(0.4f, 1.5f), Random.Range(1.4f, 2.4f));
            }
        }

        private void OnDestroy()
        {
            if (_smokeMat != null) Destroy(_smokeMat);
            if (_addMat != null) Destroy(_addMat);
        }
    }
}
