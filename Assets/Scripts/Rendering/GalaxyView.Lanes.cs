using System.Collections.Generic;
using UnityEngine;
using StellarisClone.Core;

namespace StellarisClone.Rendering
{
    /// <summary>
    /// Гиперкоридоры «Гиперпоток»: пологая дуга (LaneCurve) от края одной системы до края другой — вблизи от внешней
    /// орбиты мини-системы, издалека почти от звезды. Поток толще в середине, по нему бегут волны энергии
    /// (шейдер LaneFlow). На концах — светящиеся порталы-кольца с вращающейся дугой; видны, когда системы раскрыты.
    /// </summary>
    public partial class GalaxyView
    {
        private const int LaneSegs = 24;
        /// <summary>Лента шире сердцевины — место под ореол (сердцевина ≈ заданной толщины в пикселях).</summary>
        private const float LaneGlowK = 4.5f;
        private const float PortalPx = 22f;

        private Material _laneMat, _portalMat;
        private static Mesh s_portalQuad;
        private static Texture2D s_portalTex;
        private readonly List<Renderer[]> _lanePortals = new List<Renderer[]>();
        private readonly List<Vector3[]> _laneEnds = new List<Vector3[]>();
        private readonly List<Color> _laneCol = new List<Color>();
        private readonly List<Vector2> _laneAlpha = new List<Vector2>();
        private float _laneZoomK = -1f, _laneGeomTimer;
        private MaterialPropertyBlock _laneMpb;
        private static readonly Vector3[] s_laneBuf = new Vector3[LaneSegs];
        private static readonly int IdLaneLen = Shader.PropertyToID("_Len");
        private static readonly int IdLanePhase = Shader.PropertyToID("_Phase");
        private static readonly int IdFxColor = Shader.PropertyToID("_Color");

        private Material LaneMaterial()
        {
            if (_laneMat != null) return _laneMat;
            var sh = Resources.Load<Shader>("Shaders/LaneFlow");
            if (sh == null || !sh.isSupported) sh = GetLineShader();
            _laneMat = new Material(sh) { name = "LaneFlow" };
            return _laneMat;
        }

        private Material PortalMaterial()
        {
            if (_portalMat != null) return _portalMat;
            var sh = Resources.Load<Shader>("Shaders/StarFx");
            if (sh == null || !sh.isSupported) return null;
            _portalMat = new Material(sh) { name = "LanePortal" };
            _portalMat.SetTexture("_MainTex", PortalTexture());
            _portalMat.SetFloat("_Intensity", 1.5f);
            return _portalMat;
        }

        /// <summary>Настроить линию коридора и создать два портала на её концах.</summary>
        private void BuildLaneVisuals(LineRenderer lr)
        {
            lr.useWorldSpace = true;
            lr.positionCount = LaneSegs;
            lr.textureMode = LineTextureMode.Stretch;
            lr.numCapVertices = 0;
            lr.alignment = LineAlignment.View;
            lr.widthCurve = new AnimationCurve(new Keyframe(0f, 0.35f), new Keyframe(0.5f, 1f), new Keyframe(1f, 0.35f));
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            lr.sortingOrder = 3;
            lr.sharedMaterial = LaneMaterial();

            var portals = new Renderer[2];
            var pm = PortalMaterial();
            if (pm != null)
            {
                for (int e = 0; e < 2; e++)
                {
                    var go = new GameObject(e == 0 ? "PortalA" : "PortalB");
                    go.transform.SetParent(lr.transform, false);
                    go.AddComponent<MeshFilter>().sharedMesh = PortalQuad();
                    var r = go.AddComponent<MeshRenderer>();
                    r.sharedMaterial = pm;
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    r.receiveShadows = false;
                    r.enabled = false;
                    portals[e] = r;
                }
            }
            _lanePortals.Add(pm != null ? portals : null);
            _laneEnds.Add(new Vector3[2]);
            _laneCol.Add(Color.white);
            _laneAlpha.Add(new Vector2(0.6f, 0.6f));
        }

        /// <summary>Отступ коридора от центра системы: вблизи — край мини-системы (внешняя орбита), издалека — звезда.</summary>
        private float LaneGap(StarSystem s, float zoomK)
        {
            float star = 2.2f * StarFx.SizeMul(s.SpectralClass);
            if (!IsKnownToPlayer(s) || s.Planets == null || s.Planets.Count == 0) return star;
            int n = s.Planets.Count;
            float step = n > 1 ? Mathf.Min(MiniOrbitStep, (MiniOuterMax - MiniInnerOrbit) / (n - 1)) : MiniOrbitStep;
            float edge = MiniInnerOrbit + (n - 1) * step + 1.2f;
            return Mathf.Lerp(star, edge, zoomK);
        }

        /// <summary>Пересобрать точки дуг (отступы зависят от зума и от того, раскрыта ли система).</summary>
        private void RebuildLaneGeometry(float zoomK)
        {
            if (_laneMpb == null) _laneMpb = new MaterialPropertyBlock();
            for (int i = 0; i < _hyperlaneRenderers.Count && i < _hyperlaneData.Count; i++)
            {
                var lr = _hyperlaneRenderers[i];
                if (lr == null) continue;
                var lane = _hyperlaneData[i];
                var a = _generator.Systems[lane.SystemA];
                var b = _generator.Systems[lane.SystemB];
                Vector3 d = b.Position - a.Position;
                d.y = 0f;
                float len = Mathf.Max(0.01f, d.magnitude);
                float tA = Mathf.Clamp(LaneGap(a, zoomK) / len, 0f, 0.42f);
                float tB = 1f - Mathf.Clamp(LaneGap(b, zoomK) / len, 0f, 0.42f);
                float arc = 0f;
                for (int k = 0; k < LaneSegs; k++)
                {
                    Vector3 p = LaneCurve.Point(a, b, Mathf.Lerp(tA, tB, k / (LaneSegs - 1f)));
                    p.y = -0.05f;
                    if (k > 0) arc += Vector3.Distance(p, s_laneBuf[k - 1]);
                    s_laneBuf[k] = p;
                }
                lr.SetPositions(s_laneBuf);
                if (i < _laneEnds.Count)
                {
                    _laneEnds[i][0] = s_laneBuf[0];
                    _laneEnds[i][1] = s_laneBuf[LaneSegs - 1];
                }
                _laneMpb.Clear();
                _laneMpb.SetFloat(IdLaneLen, arc);
                _laneMpb.SetFloat(IdLanePhase, i * 1.37f);
                lr.SetPropertyBlock(_laneMpb);
            }
        }

        /// <summary>Каждый кадр: толщина в пикселях экрана, порталы на концах; геометрия — при смене зума.</summary>
        private void UpdateLanes(Camera cam)
        {
            if (_hyperlanesParent == null || !_hyperlanesParent.activeInHierarchy) return;
            Vector3 camPos = cam.transform.position;
            float zoomK = 1f - Smooth(MiniFullHeight, MiniShowHeight, camPos.y);
            _laneGeomTimer -= Time.unscaledDeltaTime;
            if (_laneZoomK < 0f || Mathf.Abs(zoomK - _laneZoomK) > 0.01f || _laneGeomTimer <= 0f)
            {
                _laneZoomK = zoomK;
                _laneGeomTimer = 1f;
                RebuildLaneGeometry(zoomK);
            }

            if (_laneMpb == null) _laneMpb = new MaterialPropertyBlock();
            Quaternion rot = cam.transform.rotation;
            float t = Time.unscaledTime;
            for (int i = 0; i < _hyperlaneRenderers.Count; i++)
            {
                var lr = _hyperlaneRenderers[i];
                if (lr == null) continue;
                float px = i < _laneWidthPx.Count ? _laneWidthPx[i] : 1.8f;
                Vector3 mid = lr.GetPosition(LaneSegs / 2);
                lr.widthMultiplier = WorldPerPixel(cam, Vector3.Distance(camPos, mid)) * px * LaneGlowK;

                var portals = i < _lanePortals.Count ? _lanePortals[i] : null;
                if (portals == null) continue;
                for (int e = 0; e < 2; e++)
                {
                    var r = portals[e];
                    if (r == null) continue;
                    float alpha = (e == 0 ? _laneAlpha[i].x : _laneAlpha[i].y) * zoomK;
                    bool on = lr.enabled && alpha > 0.02f;
                    if (r.enabled != on) r.enabled = on;
                    if (!on) continue;
                    Vector3 p = _laneEnds[i][e];
                    float wpp = WorldPerPixel(cam, Vector3.Distance(camPos, p));
                    float pulse = 0.88f + 0.12f * Mathf.Sin(t * 2f + i + e * 1.3f);
                    r.transform.SetPositionAndRotation(p + Vector3.up * 0.03f, rot * Quaternion.Euler(0f, 0f, t * 70f + i * 37f + e * 90f));
                    r.transform.localScale = Vector3.one * (wpp * PortalPx * pulse);
                    var c = _laneCol[i];
                    _laneMpb.Clear();
                    _laneMpb.SetVector(IdFxColor, new Vector4(c.r, c.g, c.b, alpha));
                    r.SetPropertyBlock(_laneMpb);
                }
            }
        }

        /// <summary>Цвет и прозрачность концов коридора — для порталов (задаёт UpdateHyperlaneColors).</summary>
        private void SetLanePortalColor(int i, Color c, float alphaA, float alphaB)
        {
            if (i >= _laneCol.Count) return;
            _laneCol[i] = c;
            _laneAlpha[i] = new Vector2(alphaA, alphaB);
        }

        private static Mesh PortalQuad()
        {
            if (s_portalQuad != null) return s_portalQuad;
            s_portalQuad = new Mesh
            {
                name = "LanePortalQuad", hideFlags = HideFlags.DontSave,
                vertices = new[] { new Vector3(-.5f, -.5f, 0), new Vector3(.5f, -.5f, 0), new Vector3(.5f, .5f, 0), new Vector3(-.5f, .5f, 0) },
                uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) },
                triangles = new[] { 0, 2, 1, 0, 3, 2 }
            };
            s_portalQuad.RecalculateBounds();
            return s_portalQuad;
        }

        /// <summary>Портал: кольцо, мягкое свечение внутри и внешняя дуга на 240° (вращается вместе с квадратом).</summary>
        private static Texture2D PortalTexture()
        {
            if (s_portalTex != null) return s_portalTex;
            const int N = 64;
            s_portalTex = new Texture2D(N, N, TextureFormat.RGBA32, true, true)
            { name = "LanePortal", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear, hideFlags = HideFlags.DontSave };
            var px = new Color[N * N];
            for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float u = x / (N - 1f) * 2f - 1f, v = y / (N - 1f) * 2f - 1f;
                float d = Mathf.Sqrt(u * u + v * v), an = Mathf.Atan2(v, u) + Mathf.PI;   // 0..2π
                float ring = Mathf.Exp(-((d - 0.42f) / 0.07f) * ((d - 0.42f) / 0.07f));
                float glow = Mathf.Exp(-d * d * 6f) * 0.35f;
                float arcMask = Mathf.Clamp01(an / 0.4f) * Mathf.Clamp01((4.2f - an) / 0.4f);
                float arc = Mathf.Exp(-((d - 0.78f) / 0.045f) * ((d - 0.78f) / 0.045f)) * 0.6f * arcMask;
                float a = Mathf.Clamp01(ring + glow + arc) * Mathf.Clamp01((1f - d) / 0.08f);
                px[y * N + x] = new Color(1f, 1f, 1f, a);
            }
            s_portalTex.SetPixels(px);
            s_portalTex.Apply(true, true);
            return s_portalTex;
        }
    }
}
