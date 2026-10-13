using System.Collections.Generic;
using UnityEngine;
using StellarisClone.Core;

namespace StellarisClone.Rendering
{
    /// <summary>
    /// Режимы карты (кнопки у мини-карты, MapModeController):
    ///   • Простой — чистая карта: от территорий — мягкий контур, без названий империй, коридоры одного нейтрального цвета;
    ///   • Политика — яркие границы и заливка территорий, названия империй, коридоры цветом владельца;
    ///   • Ресурсы — под каждой изученной системой сияние: размер — сколько осталось добыть, цвет — чего больше
    ///     (гелий-3 — золото, титан — бирюза); у неизученных — тусклый знак вопроса-кольцо;
    ///   • Коридоры — толстые яркие гиперкоридоры, узлы (4+ коридора) отмечены оранжевыми кольцами;
    ///   • Разведка — изученные системы в зелёных кольцах, встреченные, но не изученные — в пульсирующих янтарных;
    ///   • Шпионаж — разведсети (SpyNetworkView), границы как в политическом режиме.
    /// Метки систем лежат на плоскости карты под звёздами, учитывают туман войны и обновляются раз в 1,5 с.
    /// </summary>
    public partial class GalaxyView
    {
        private GameObject _modeRoot;
        private readonly List<SpriteRenderer> _modeMarks = new List<SpriteRenderer>();
        private Sprite _modeGlow, _modeRing;
        private float _modeScan;
        private float _borderK = 1f, _borderFillK = 1f;

        private static readonly Color ModeEnergy = new Color(1.00f, 0.78f, 0.35f);
        private static readonly Color ModeMinerals = new Color(0.42f, 0.95f, 0.80f);
        private static readonly Color ModeHub = new Color(1.00f, 0.62f, 0.25f);
        private static readonly Color ModeSurveyed = new Color(0.44f, 0.88f, 0.60f);
        private static readonly Color ModeUnsurveyed = new Color(1.00f, 0.72f, 0.30f);

        private bool IsMode(MapModeController.MapMode m) => _currentMapMode == m;

        /// <summary>Названия империй — только в политическом режиме и в режиме шпионажа.</summary>
        private bool ShowEmpireLabels => IsMode(MapModeController.MapMode.Political) || IsMode(MapModeController.MapMode.Intel);

        /// <summary>Применить режим: метки систем, видимость названий; коридоры перекрашивает UpdateHyperlaneColors.</summary>
        private void ApplyMapModeOverlay()
        {
            foreach (var kv in _empireLabels)
            {
                if (kv.Value == null) continue;
                if (!ShowEmpireLabels) kv.Value.SetActive(false);
                else if (kv.Key == 0 && !_isolated) kv.Value.SetActive(true);   // название игрока само не возвращается
            }
            _modeScan = 0f;
            RefreshModeMarks();
        }

        /// <summary>Сила границ в режиме (множители к альфе заливки и линий, плавно — в анимации фона).</summary>
        private void ModeBorderTargets(out float lineK, out float fillK)
        {
            switch (_currentMapMode)
            {
                case MapModeController.MapMode.Simple:     lineK = 0.6f;  fillK = 0.35f; break;   // мягкий контур, без заливки-«краски»
                case MapModeController.MapMode.Political:  lineK = 1.15f; fillK = 2.6f; break;
                case MapModeController.MapMode.Intel:      lineK = 1.0f;  fillK = 1.0f; break;
                default:                                   lineK = 0.5f;  fillK = 0.35f; break;
            }
        }

        private void UpdateMapModes()
        {
            if (_generator == null) return;
            ModeBorderTargets(out float lineK, out float fillK);
            float dt = Time.unscaledDeltaTime * 4f;
            _borderK = Mathf.MoveTowards(_borderK, lineK, dt);
            _borderFillK = Mathf.MoveTowards(_borderFillK, fillK, dt * 2f);

            bool inSystem = SystemViewManager.Instance != null && SystemViewManager.Instance.IsInSystemView;
            if (_modeRoot != null && _modeRoot.activeSelf == inSystem) _modeRoot.SetActive(!inSystem);
            if (inSystem) return;

            _modeScan -= Time.unscaledDeltaTime;
            if (_modeScan <= 0f) RefreshModeMarks();

            // Пульс янтарных колец «не изучено»
            if (IsMode(MapModeController.MapMode.Explored))
            {
                float p = 0.75f + 0.25f * Mathf.Sin(Time.unscaledTime * 3f);
                for (int i = 0; i < _modeMarks.Count && i < _generator.Systems.Count; i++)
                {
                    var sr = _modeMarks[i];
                    if (sr == null || !sr.enabled || sr.sprite != _modeRing || _generator.Systems[i].IsSurveyed) continue;
                    var c = ModeUnsurveyed; c.a = 0.85f * p;
                    sr.color = c;
                }
            }
        }

        private void RefreshModeMarks()
        {
            _modeScan = 1.5f;
            if (_generator == null) return;
            EnsureModeSprites();
            if (_modeRoot == null)
            {
                _modeRoot = new GameObject("MapModeMarks");
                _modeRoot.transform.SetParent(transform, false);
            }
            var sh = GetSpriteShader();
            while (_modeMarks.Count < _generator.Systems.Count)
            {
                var go = new GameObject("ModeMark");
                go.transform.SetParent(_modeRoot.transform, false);
                go.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
                var sr = go.AddComponent<SpriteRenderer>();
                if (sh != null) sr.sharedMaterial = new Material(sh);
                sr.sortingOrder = 5;                                   // под звёздами (14), над территориями
                sr.enabled = false;
                _modeMarks.Add(sr);
            }

            for (int i = 0; i < _generator.Systems.Count; i++)
            {
                var s = _generator.Systems[i];
                var sr = _modeMarks[i];
                bool onMap = i < _starNodes.Count && _starNodes[i] != null && _starNodes[i].activeSelf;   // туман войны
                if (!onMap || !MarkFor(s, out Sprite sprite, out Color color, out float size))
                {
                    sr.enabled = false;
                    continue;
                }
                sr.enabled = true;
                sr.sprite = sprite;
                sr.color = color;
                sr.transform.position = new Vector3(s.Position.x, s.Position.y - 0.02f, s.Position.z);
                sr.transform.localScale = Vector3.one * size;
            }
        }

        /// <summary>Метка системы в текущем режиме; false — метки нет.</summary>
        private bool MarkFor(StarSystem s, out Sprite sprite, out Color color, out float size)
        {
            sprite = _modeGlow; color = Color.clear; size = 0f;
            switch (_currentMapMode)
            {
                case MapModeController.MapMode.Resources:
                {
                    if (!IsKnownToPlayer(s))
                    {
                        sprite = _modeRing; size = 4.2f; color = new Color(0.7f, 0.76f, 0.84f, 0.25f);
                        return true;
                    }
                    float e = s.PotentialEnergy, m = s.PotentialMinerals, total = e + m;
                    if (total <= 0f) return false;
                    color = Color.Lerp(ModeMinerals, ModeEnergy, e / total);
                    color.a = 0.55f;
                    size = Mathf.Clamp(5f + Mathf.Sqrt(total) * 1.1f, 5f, 18f);
                    return true;
                }
                case MapModeController.MapMode.Hyperlanes:
                {
                    int links = s.ConnectedSystemIds.Count;
                    if (links < 4) return false;
                    sprite = _modeRing;
                    color = ModeHub; color.a = 0.75f;
                    size = 5.5f + (links - 4) * 1.2f;
                    return true;
                }
                case MapModeController.MapMode.Explored:
                {
                    sprite = _modeRing;
                    if (s.IsSurveyed || s.OwnerId == 0)
                    {
                        color = ModeSurveyed; color.a = 0.55f; size = 4.6f;
                    }
                    else
                    {
                        color = ModeUnsurveyed; color.a = 0.85f; size = 5.6f;
                    }
                    return true;
                }
                default:
                    return false;
            }
        }

        private void EnsureModeSprites()
        {
            if (_modeGlow != null) return;
            const int n = 128;
            var glow = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "ModeGlow" };
            var ring = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "ModeRing" };
            var g = new Color32[n * n];
            var r = new Color32[n * n];
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float dx = (x + 0.5f) / n - 0.5f, dy = (y + 0.5f) / n - 0.5f;
                float d = Mathf.Sqrt(dx * dx + dy * dy) * 2f;                       // 0 — центр, 1 — край
                float ga = Mathf.Clamp01(1f - d);
                ga = ga * ga * (0.6f + 0.4f * ga);                                   // мягкое сияние
                float ra = Mathf.Exp(-((d - 0.82f) / 0.05f) * ((d - 0.82f) / 0.05f)) // тонкое кольцо
                         + 0.25f * Mathf.Exp(-((d - 0.82f) / 0.13f) * ((d - 0.82f) / 0.13f));
                g[y * n + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(ga) * 255));
                r[y * n + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(ra) * 255));
            }
            glow.SetPixels32(g); glow.Apply(false);
            ring.SetPixels32(r); ring.Apply(false);
            _modeGlow = Sprite.Create(glow, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), n);
            _modeRing = Sprite.Create(ring, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), n);
        }

        /// <summary>Коридоры в режиме: (цвет, альфа концов, толщина) с учётом владельцев — см. UpdateHyperlaneColors.</summary>
        private void ModeLaneStyle(StarSystem a, StarSystem b, bool ka, bool kb, ref Color c, ref float alphaA, ref float alphaB, ref float px)
        {
            switch (_currentMapMode)
            {
                case MapModeController.MapMode.Simple:
                    c = new Color(0.78f, 0.86f, 1.00f);
                    alphaA = ka ? 0.45f : 0.22f;
                    alphaB = kb ? 0.45f : 0.22f;
                    px = 1.4f;
                    break;
                case MapModeController.MapMode.Resources:
                    c = new Color(0.72f, 0.78f, 0.88f);
                    alphaA = alphaB = 0.25f;
                    px = 1.2f;
                    break;
                case MapModeController.MapMode.Hyperlanes:
                    if (a.OwnerId < 0 || a.OwnerId != b.OwnerId) c = new Color(0.86f, 0.92f, 1.00f);
                    alphaA = Mathf.Max(alphaA, ka ? 0.9f : 0.5f);
                    alphaB = Mathf.Max(alphaB, kb ? 0.9f : 0.5f);
                    px *= 1.9f;
                    break;
                case MapModeController.MapMode.Explored:
                    alphaA = ka ? Mathf.Max(alphaA, 0.6f) : 0.15f;
                    alphaB = kb ? Mathf.Max(alphaB, 0.6f) : 0.15f;
                    break;
            }
        }
    }
}
