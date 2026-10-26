using System.Collections.Generic;
using UnityEngine;
using StellarisClone.Core;
using StellarisClone.Rendering;

namespace StellarisClone.Ground
{
    /// <summary>
    /// «Мясо»: кровь цвета вида (люди — тёмно-красная, Ксарн — фиолетовая, Тэрра'ан — зелёная, машины — белая
    /// охлаждающая жидкость и искры), пул декалей крови и гари (до 300, старые гаснут), ошмётки с физикой
    /// (3–6 кусков брони и мяса, отскоки, исчезают через 25–40 с), рэгдолл-лайт трупов (60 с),
    /// обугливание огнём, пыль, дым, языки пламени, воронки и обломки.
    /// Настройка «Кровь и расчленёнка» выключена — вместо крови пыль и искры, ошмётков нет.
    /// </summary>
    public static class GoreFx
    {
        private const int MaxDecals = 300;
        private const int MaxGibs = 160;
        private const float CorpseLife = 60f;

        private static Transform s_root;
        private static GroundMap s_map;
        private static ParticleSystem s_blood, s_dust, s_smoke, s_fire, s_sparksPs;
        private static Material s_decalMat, s_scorchMat, s_charred;
        private static readonly Queue<Transform> s_decals = new Queue<Transform>();
        private static readonly List<(Rigidbody rb, float until, bool bled, Color blood)> s_gibs = new List<(Rigidbody, float, bool, Color)>();
        private static readonly List<(GameObject go, float until, Rigidbody rb, float settle)> s_corpses = new List<(GameObject, float, Rigidbody, float)>();
        private static Mesh s_quad;
        private static GoreTicker s_ticker;

        public static bool Enabled => GameSettings.Gore;

        public static Color BloodColor(BloodKind k) => k switch
        {
            BloodKind.Violet => new Color(0.32f, 0.06f, 0.36f),
            BloodKind.Green => new Color(0.22f, 0.45f, 0.08f),
            BloodKind.Coolant => new Color(0.85f, 0.92f, 0.95f),
            _ => new Color(0.42f, 0.02f, 0.02f)
        };

        // ================================================================ Жизненный цикл

        public static void Begin(Transform root, GroundMap map)
        {
            GameSettings.EnsureLoaded();
            s_root = new GameObject("Gore").transform;
            s_root.SetParent(root, false);
            s_map = map;
            s_decals.Clear();
            s_gibs.Clear();
            s_corpses.Clear();
            var soft = SoftTexture();
            s_blood = MakePs("Blood", AlphaMat(soft), 1.6f, 1200);
            s_dust = MakePs("Dust", AlphaMat(soft), 0.15f, 800);
            s_smoke = MakePs("Smoke", AlphaMat(soft), -0.06f, 900);
            s_fire = MakePs("Fire", AddMat(soft), -0.25f, 600);
            s_sparksPs = MakePs("Sparks", AddMat(soft), 1.2f, 600);
            s_decalMat = DecalMat(SplatTexture(false));
            s_scorchMat = DecalMat(SplatTexture(true));
            s_charred = GroundMats.Lit(new Color(0.05f, 0.045f, 0.04f), 0.1f, 0.1f);
            s_ticker = s_root.gameObject.AddComponent<GoreTicker>();
        }

        public static void End()
        {
            s_decals.Clear();
            s_gibs.Clear();
            s_corpses.Clear();
            s_root = null;
            s_map = null;
            s_ticker = null;
        }

        private static ParticleSystem MakePs(string name, Material mat, float gravity, int max)
        {
            var go = new GameObject(name);
            go.transform.SetParent(s_root, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = false;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = max;
            main.gravityModifier = gravity * (s_map != null ? s_map.Biome.Gravity : 1f);
            main.startLifetime = 1f;
            main.startSpeed = 0f;
            var em = ps.emission;
            em.enabled = false;
            var shape = ps.shape;
            shape.enabled = false;
            var col = ps.sizeOverLifetime;
            col.enabled = true;
            col.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, name == "Smoke" || name == "Dust" ? 0.6f : 1f, 1f, name == "Smoke" || name == "Dust" ? 1.8f : 0.3f));
            var colLife = ps.colorOverLifetime;
            colLife.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.8f, 0.6f), new GradientAlphaKey(0f, 1f) });
            colLife.color = g;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = mat;
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            ps.Play();
            return ps;
        }

        private static Material AlphaMat(Texture2D tex)
        {
            var m = new Material(ShaderCache.Sprite) { name = "GoreAlpha", hideFlags = HideFlags.DontSave };
            m.mainTexture = tex;
            return m;
        }

        private static Material AddMat(Texture2D tex)
        {
            var m = new Material(GroundMats.Additive) { name = "GoreAdd", hideFlags = HideFlags.DontSave };
            m.mainTexture = tex;
            return m;
        }

        private static Material DecalMat(Texture2D tex)
        {
            var m = new Material(ShaderCache.Sprite) { name = "GoreDecal", hideFlags = HideFlags.DontSave };
            m.mainTexture = tex;
            m.renderQueue = 2450;
            return m;
        }

        private static Texture2D SoftTexture()
        {
            const int N = 64;
            var t = new Texture2D(N, N, TextureFormat.RGBA32, false) { name = "SoftDot", wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave };
            var px = new Color[N * N];
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(N * 0.5f, N * 0.5f)) / (N * 0.5f);
                    float a = Mathf.Clamp01(1f - d);
                    px[y * N + x] = new Color(1f, 1f, 1f, a * a);
                }
            t.SetPixels(px); t.Apply();
            return t;
        }

        /// <summary>Пятно крови (рваные края, брызги) или гари (мягкое чёрное пятно).</summary>
        private static Texture2D SplatTexture(bool scorch)
        {
            const int N = 128;
            var t = new Texture2D(N, N, TextureFormat.RGBA32, true) { name = scorch ? "Scorch" : "Splat", wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave };
            var px = new Color[N * N];
            float ox = Random.value * 50f;
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    var p = new Vector2((x + 0.5f) / N - 0.5f, (y + 0.5f) / N - 0.5f) * 2f;
                    float d = p.magnitude;
                    float ang = Mathf.Atan2(p.y, p.x);
                    float edge = 0.55f + Mathf.PerlinNoise(Mathf.Cos(ang) * 2f + ox, Mathf.Sin(ang) * 2f) * (scorch ? 0.35f : 0.5f);
                    float a = scorch ? Mathf.Clamp01((1f - d / (edge + 0.2f)) * 1.6f) * 0.85f : (d < edge ? 1f : 0f);
                    if (!scorch && d >= edge && Mathf.PerlinNoise(x * 0.35f + ox, y * 0.35f) > 0.72f && d < 0.95f) a = 0.9f;   // брызги
                    float shade = 0.75f + Mathf.PerlinNoise(x * 0.1f, y * 0.1f + ox) * 0.25f;
                    px[y * N + x] = new Color(shade, shade, shade, a);
                }
            t.SetPixels(px); t.Apply(true);
            return t;
        }

        // ================================================================ Попадания и смерть

        private static Color BloodOf(Soldier s) => BloodColor(s.Squad.Side.Setup.Blood);

        /// <summary>Попадание: брызги крови по направлению выстрела (или пыль и искры без крови).</summary>
        public static void Hit(Soldier v, Vector3 from, WeaponDef w, float dmg)
        {
            if (s_blood == null) return;
            Vector3 dir = (v.Chest - from).normalized;
            bool machine = v.Squad.Side.Setup.Blood == BloodKind.Coolant;
            if (!Enabled || machine) { Sparks(v.Chest, 6); if (!Enabled) return; }
            int n = Mathf.Clamp(Mathf.RoundToInt(dmg * 0.6f), 4, 22);
            var c = BloodOf(v);
            for (int i = 0; i < n; i++)
            {
                var vel = (dir + Random.insideUnitSphere * 0.55f + Vector3.up * 0.4f) * Random.Range(2f, 6f);
                Emit(s_blood, v.Chest + Random.insideUnitSphere * 0.15f, vel, Random.Range(0.07f, 0.16f), Random.Range(0.35f, 0.7f), c);
            }
            if (Random.value < 0.4f) Decal(v.Pos + dir * Random.Range(0.4f, 1.6f), Random.Range(0.5f, 1.1f), c, false);
        }

        /// <summary>Смерть бойца: пуля — тело отлетает и остаётся трупом; взрыв/огонь/давка — ошмётки; огонь — обугливание.</summary>
        public static void Death(Soldier v, WeaponDef w, Vector3 dir)
        {
            GroundSfx.Scream(v);
            var c = BloodOf(v);
            bool gib = w != null && w.Gibs && Enabled;
            bool flame = w != null && w.Kind == WeaponKind.Flame;
            if (v.Agent != null) v.Agent.enabled = false;
            var rig = v.Rig;
            if (rig?.Root == null || s_root == null) { Object.Destroy(v.gameObject); return; }

            if (gib)
            {
                Burst(v.Chest, c, 34);
                Decal(v.Pos, Random.Range(1.4f, 2.2f), c, false);
                int pieces = Random.Range(3, 7);
                Material armor = rig.Armor ?? GroundMats.Lit(v.Squad.Side.Color);
                Material meat = GroundMats.Lit(c * 0.9f, 0f, 0.6f);
                for (int i = 0; i < pieces; i++)
                    Gib(v.Chest + Random.insideUnitSphere * 0.3f, (dir + Vector3.up * 1.2f + Random.insideUnitSphere) * Random.Range(4f, 9f),
                        i % 2 == 0 ? armor : meat, Random.Range(0.18f, 0.34f) * Mathf.Max(0.8f, v.Def.Scale), c);
                Object.Destroy(v.gameObject);
                return;
            }

            // Тело: отделить модель, дать ей физику и импульс
            var body = rig.Root.gameObject;
            body.transform.SetParent(s_root, true);
            if (flame) foreach (var r in rig.Renderers) if (r != null) r.sharedMaterial = s_charred;
            body.layer = 2;
            var bc = body.AddComponent<BoxCollider>();
            bc.center = new Vector3(0f, 0.9f, 0f);
            bc.size = new Vector3(0.5f, 1.8f, 0.4f);
            var rb = body.AddComponent<Rigidbody>();
            rb.mass = 80f;
            rb.linearDamping = 0.4f;
            rb.angularDamping = 0.8f;
            float force = w != null && w.Kind == WeaponKind.Sniper ? 7f : w != null && w.Kind == WeaponKind.Heavy ? 4.5f : 3f;
            rb.AddForce((dir.WithY(0f).normalized * force + Vector3.up * 1.6f) * rb.mass, ForceMode.Impulse);
            rb.AddTorque(Random.insideUnitSphere * 60f, ForceMode.Impulse);
            s_corpses.Add((body, Time.time + CorpseLife, rb, Time.time + 3f));
            if (Enabled && !flame) { Burst(v.Chest, c, 10); Decal(v.Pos + dir * 0.8f, Random.Range(0.9f, 1.6f), c, false); }
            if (!Enabled) Sparks(v.Chest, 4);
            Object.Destroy(v.gameObject);
        }

        public static void Burn(Soldier s, float dt)
        {
            if (s_fire == null) return;
            if (Random.value < dt * 30f) Flame(s.Chest + Random.insideUnitSphere * 0.3f, 0.5f);
            if (Random.value < dt * 8f) SmokePuff(s.Chest + Vector3.up * 0.4f);
        }

        /// <summary>Фонтан крови (добивания, давка, взрыв).</summary>
        public static void Burst(Vector3 at, Color c, int n)
        {
            if (s_blood == null) return;
            if (!Enabled) { Sparks(at, n / 3); return; }
            for (int i = 0; i < n; i++)
            {
                var vel = (Vector3.up * 1.4f + Random.insideUnitSphere) * Random.Range(3f, 8f);
                Emit(s_blood, at + Random.insideUnitSphere * 0.25f, vel, Random.Range(0.09f, 0.22f), Random.Range(0.5f, 1f), c);
            }
        }

        public static void BurstFor(Soldier v, int n) => Burst(v.Chest, BloodOf(v), n);

        /// <summary>Обугливание (огнемёт) — заменить материалы модели на тёмный.</summary>
        public static void Char(Soldier v)
        {
            if (v?.Rig?.Renderers == null) return;
            foreach (var r in v.Rig.Renderers) if (r != null) r.sharedMaterial = s_charred;
        }

        // ================================================================ Ошмётки

        private static void Gib(Vector3 at, Vector3 vel, Material mat, float size, Color blood)
        {
            if (s_root == null) return;
            if (s_gibs.Count >= MaxGibs) { var old = s_gibs[0]; s_gibs.RemoveAt(0); if (old.rb != null) Object.Destroy(old.rb.gameObject); }
            var go = new GameObject("Gib");
            go.transform.SetParent(s_root, false);
            go.transform.position = at;
            go.transform.rotation = Random.rotation;
            go.layer = 2;
            var prim = Random.value < 0.5f ? PrimitiveType.Cube : PrimitiveType.Sphere;
            go.AddComponent<MeshFilter>().sharedMesh = GroundMats.Prim(prim);
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            go.transform.localScale = new Vector3(size * Random.Range(0.7f, 1.4f), size * Random.Range(0.6f, 1.2f), size * Random.Range(0.8f, 1.6f));
            var col = go.AddComponent<BoxCollider>();
            col.size = Vector3.one;
            var rb = go.AddComponent<Rigidbody>();
            rb.mass = 4f;
            rb.linearVelocity = vel;
            rb.angularVelocity = Random.insideUnitSphere * 14f;
            rb.linearDamping = 0.1f;
            // Низкая гравитация: летят выше и медленнее
            if (s_map != null && s_map.Biome.Gravity < 0.9f) { rb.useGravity = false; s_lowGrav.Add(rb); }
            s_gibs.Add((rb, Time.time + Random.Range(25f, 40f), false, blood));
        }

        private static readonly List<Rigidbody> s_lowGrav = new List<Rigidbody>();

        // ================================================================ Декали

        /// <summary>Декаль на земле (кровь или гарь). Старые гаснут, когда пул заполнен.</summary>
        public static void Decal(Vector3 at, float size, Color c, bool scorch)
        {
            if (s_root == null || s_map == null) return;
            if (!scorch && !Enabled) return;
            Transform t;
            if (s_decals.Count >= MaxDecals) t = s_decals.Dequeue();
            else
            {
                var go = new GameObject("Decal");
                go.transform.SetParent(s_root, false);
                go.AddComponent<MeshFilter>().sharedMesh = Quad();
                var mr = go.AddComponent<MeshRenderer>();
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
                t = go.transform;
            }
            var l = s_map.ToLocal(at);
            var n = s_map.Normal(l.x, l.z);
            t.position = s_map.Snap(at) + n * (0.03f + s_decals.Count * 0.00005f);
            t.rotation = Quaternion.FromToRotation(Vector3.up, n) * Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
            t.localScale = new Vector3(size, 1f, size);
            var r = t.GetComponent<MeshRenderer>();
            r.sharedMaterial = scorch ? s_scorchMat : s_decalMat;
            var mpb = new MaterialPropertyBlock();
            mpb.SetColor("_Color", scorch ? new Color(0.03f, 0.03f, 0.03f, 0.9f) : new Color(c.r, c.g, c.b, 0.92f));
            r.SetPropertyBlock(mpb);
            s_decals.Enqueue(t);
        }

        private static Mesh Quad()
        {
            if (s_quad != null) return s_quad;
            s_quad = new Mesh { name = "DecalQuad" };
            s_quad.vertices = new[] { new Vector3(-0.5f, 0f, -0.5f), new Vector3(-0.5f, 0f, 0.5f), new Vector3(0.5f, 0f, 0.5f), new Vector3(0.5f, 0f, -0.5f) };
            s_quad.uv = new[] { new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(1f, 0f) };
            s_quad.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            s_quad.RecalculateNormals();
            return s_quad;
        }

        // ================================================================ Пыль, дым, огонь

        private static void Emit(ParticleSystem ps, Vector3 pos, Vector3 vel, float size, float life, Color c)
        {
            if (ps == null) return;
            var e = new ParticleSystem.EmitParams { position = pos, velocity = vel, startSize = size, startLifetime = life, startColor = c, applyShapeToPosition = false };
            ps.Emit(e, 1);
        }

        public static void Dust(Vector3 at, float size)
        {
            if (s_dust == null || s_map == null) return;
            var c = Color.Lerp(s_map.Biome.GroundC, new Color(0.6f, 0.58f, 0.55f), 0.5f);
            c.a = 0.7f;
            int n = Mathf.Clamp(Mathf.RoundToInt(size * 8f), 2, 30);
            for (int i = 0; i < n; i++)
                Emit(s_dust, at + Random.insideUnitSphere * size * 0.4f, (Random.insideUnitSphere + Vector3.up) * size * Random.Range(1f, 3f),
                     size * Random.Range(0.6f, 1.4f), Random.Range(1f, 2.4f), c);
        }

        public static void SmokePuff(Vector3 at)
        {
            if (s_smoke == null) return;
            Emit(s_smoke, at + Random.insideUnitSphere * 0.4f, new Vector3(Random.Range(-0.3f, 0.3f), Random.Range(1.2f, 2.2f), Random.Range(-0.3f, 0.3f)),
                 Random.Range(1.2f, 2.4f), Random.Range(3f, 5f), new Color(0.12f, 0.12f, 0.12f, 0.6f));
        }

        public static void Flame(Vector3 at, float size)
        {
            if (s_fire == null) return;
            Emit(s_fire, at, Vector3.up * Random.Range(0.6f, 1.6f) + Random.insideUnitSphere * 0.3f, size * Random.Range(0.6f, 1.2f),
                 Random.Range(0.3f, 0.6f), new Color(1f, Random.Range(0.4f, 0.7f), 0.15f, 1f));
        }

        public static void Sparks(Vector3 at, int n)
        {
            if (s_sparksPs == null) return;
            for (int i = 0; i < n; i++)
                Emit(s_sparksPs, at, (Random.insideUnitSphere + Vector3.up * 0.5f) * Random.Range(3f, 7f), Random.Range(0.04f, 0.08f), Random.Range(0.2f, 0.45f),
                     new Color(1f, 0.8f, 0.4f, 1f));
        }

        public static void Crater(Vector3 at, float r)
        {
            Decal(at, r * 2.2f, Color.black, true);
            Dust(at, r * 0.8f);
        }

        public static void Debris(Vector3 at, float r)
        {
            var mat = GroundMats.Lit(new Color(0.3f, 0.29f, 0.28f), 0f, 0.1f);
            int n = Mathf.Clamp(Mathf.RoundToInt(r * 2f), 3, 10);
            for (int i = 0; i < n; i++)
                Gib(at + Vector3.up * Random.Range(0.5f, 2f) + Random.insideUnitSphere * r * 0.5f, (Vector3.up * 1.5f + Random.insideUnitSphere) * Random.Range(4f, 10f),
                    mat, Random.Range(0.25f, 0.6f), Color.clear);
            Decal(at, r * 2.4f, Color.black, true);
        }

        // ================================================================ Такт (трупы, ошмётки, низкая гравитация)

        internal static void Tick()
        {
            float now = Time.time;
            for (int i = s_corpses.Count - 1; i >= 0; i--)
            {
                var c = s_corpses[i];
                if (c.go == null) { s_corpses.RemoveAt(i); continue; }
                if (c.rb != null && now > c.settle && c.rb.linearVelocity.sqrMagnitude < 0.05f)
                {
                    Object.Destroy(c.rb);
                    var col = c.go.GetComponent<Collider>();
                    if (col != null) Object.Destroy(col);
                    s_corpses[i] = (c.go, c.until, null, c.settle);
                }
                if (now > c.until) { Object.Destroy(c.go); s_corpses.RemoveAt(i); }
            }
            for (int i = s_gibs.Count - 1; i >= 0; i--)
            {
                var g = s_gibs[i];
                if (g.rb == null) { s_gibs.RemoveAt(i); continue; }
                // Упавший кусок оставляет пятно крови
                if (!g.bled && g.blood.a > 0f && g.rb.linearVelocity.sqrMagnitude < 0.5f)
                {
                    Decal(g.rb.position, Random.Range(0.35f, 0.7f), g.blood, false);
                    s_gibs[i] = (g.rb, g.until, true, g.blood);
                }
                if (now > g.until) { Object.Destroy(g.rb.gameObject); s_gibs.RemoveAt(i); }
            }
            for (int i = s_lowGrav.Count - 1; i >= 0; i--)
            {
                var rb = s_lowGrav[i];
                if (rb == null) { s_lowGrav.RemoveAt(i); continue; }
                rb.AddForce(Physics.gravity * (s_map != null ? s_map.Biome.Gravity : 1f), ForceMode.Acceleration);
            }
        }
    }

    /// <summary>Тикает GoreFx (трупы и ошмётки) на корне эффектов боя.</summary>
    public sealed class GoreTicker : MonoBehaviour
    {
        private void FixedUpdate() => GoreFx.Tick();
    }
}
