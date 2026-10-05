using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using StellarisClone.Core;
using StellarisClone.Generation;

namespace StellarisClone.Rendering
{
    /// <summary>
    /// Рисует прогресс-кольцо вокруг системы, если в ней идёт строительство или разведка.
    /// Автоматически создаётся и работает без дополнительных настроек.
    /// </summary>
    public class SystemProgressRing : MonoBehaviour
    {
        public static SystemProgressRing Instance { get; private set; }

        private GalaxyGenerator _gen;
        private FleetManager _fleetMgr;
        private Transform _root;
        private Camera _cam;

        private readonly Dictionary<int, GameObject> _rings = new Dictionary<int, GameObject>();
        private readonly Dictionary<int, Image> _fills = new Dictionary<int, Image>();
        private readonly Dictionary<int, Text> _labels = new Dictionary<int, Text>();

        private const float RingSize = 96f;

        private void Awake()
        {
            if (Instance == null) Instance = this;
            else { Destroy(gameObject); return; }
        }

        private void Start()
        {
            _gen = FindFirstObjectByType<GalaxyGenerator>();
            _fleetMgr = FindFirstObjectByType<FleetManager>();
            if (Camera.main != null) _cam = Camera.main;

            var rootObj = new GameObject("ProgressRingsRoot");
            rootObj.transform.SetParent(transform, false);
            _root = rootObj.transform;

            if (TimeManager.Instance != null)
                TimeManager.Instance.OnDayPassed += (_, _, _) => Refresh();
        }

        private void Refresh()
        {
            if (_gen == null || _fleetMgr == null) return;

            var active = new Dictionary<int, FleetView>();

            foreach (var f in _fleetMgr.AllFleets)
            {
                if (f?.Data == null) continue;

                int sysId = -1;
                if (f.Data.State == FleetState.Constructing && f.Data.BuildTargetSystemId >= 0)
                    sysId = f.Data.BuildTargetSystemId;
                else if (f.Data.State == FleetState.Surveying && f.Data.SurveyTargetSystemId >= 0)
                    sysId = f.Data.SurveyTargetSystemId;

                if (sysId >= 0 && !active.ContainsKey(sysId))
                    active[sysId] = f;
            }

            // Осады — отдельное кольцо (ключ со сдвигом, чтобы не пересекаться со стройкой)
            var sieges = new HashSet<int>();
            if (SiegeManager.Instance != null)
                foreach (var sg in SiegeManager.Instance.Sieges)
                    if (sg.Progress > 0f) sieges.Add(SiegeKeyOffset + sg.SystemId);

            var toRemove = new List<int>();
            foreach (var kv in _rings)
                if (!active.ContainsKey(kv.Key) && !sieges.Contains(kv.Key)) toRemove.Add(kv.Key);

            foreach (int id in toRemove)
            {
                if (_rings[id] != null) Destroy(_rings[id]);
                _rings.Remove(id); _fills.Remove(id); _labels.Remove(id);
            }

            foreach (var kv in active)
            {
                if (!_rings.ContainsKey(kv.Key))
                    CreateRing(kv.Key, kv.Value);
                else
                    UpdateRingVisual(kv.Key, kv.Value);
            }

            foreach (int key in sieges)
            {
                if (!_rings.ContainsKey(key)) CreateRing(key, null);
                else UpdateSiegeVisual(key);
            }
        }

        private const int SiegeKeyOffset = 100000;
        private static int SystemOf(int key) => key >= SiegeKeyOffset ? key - SiegeKeyOffset : key;

        private void UpdateSiegeVisual(int key)
        {
            if (!_fills.ContainsKey(key)) return;
            int sysId = SystemOf(key);
            var sg = SiegeManager.Instance?.GetSiege(sysId);
            if (sg == null || sysId < 0 || sysId >= _gen.Systems.Count) return;
            var sys = _gen.Systems[sysId];
            float need = SiegeManager.RequiredDays(sys);
            bool mine = sg.Attacker == 0;
            Color col = mine ? new Color(0.35f, 1f, 0.55f) : new Color(1f, 0.35f, 0.35f);
            if (sg.Contested || !sg.Active) col = Color.Lerp(col, Color.gray, 0.5f);
            _fills[key].fillAmount = Mathf.Clamp01(sg.Progress / Mathf.Max(1f, need));
            _fills[key].color = col;
            string state = sg.BaseHolding ? "база" : sg.Contested ? "бой" : !sg.Active ? "снята" : $"{Mathf.Max(0, need - sg.Progress):0}д";
            _labels[key].text = $"<color=#{ColorUtility.ToHtmlStringRGB(col)}>Осада · {state}</color>";
        }

        private void CreateRing(int key, FleetView fleet)
        {
            int sysId = SystemOf(key);
            var sys = _gen.Systems[sysId];

            var root = new GameObject($"ProgressRing_{sys.Name}");
            root.transform.SetParent(_root, false);
            root.transform.position = sys.Position + new Vector3(0, 0.5f, 0);

            var bg = new GameObject("BG");
            bg.transform.SetParent(root.transform, false);
            var bgRt = bg.AddComponent<RectTransform>();
            bgRt.sizeDelta = new Vector2(RingSize, RingSize);
            var bgImg = bg.AddComponent<Image>();
            bgImg.sprite = CreateRingSprite((int)RingSize, 0.055f);
            bgImg.color = new Color(0.10f, 0.20f, 0.30f, 0.55f);
            bgImg.raycastTarget = false;

            var fill = new GameObject("Fill");
            fill.transform.SetParent(root.transform, false);
            var fRt = fill.AddComponent<RectTransform>();
            fRt.sizeDelta = new Vector2(RingSize, RingSize);
            var fImg = fill.AddComponent<Image>();
            fImg.sprite = CreateRingSprite((int)RingSize, 0.055f);
            fImg.type = Image.Type.Filled;
            fImg.fillMethod = Image.FillMethod.Radial360;
            fImg.fillOrigin = (int)Image.Origin360.Top;
            fImg.fillClockwise = true;
            fImg.fillAmount = 0f;
            fImg.raycastTarget = false;

            var labelObj = new GameObject("Label");
            labelObj.transform.SetParent(root.transform, false);
            var lRt = labelObj.AddComponent<RectTransform>();
            lRt.sizeDelta = new Vector2(120, 22);
            lRt.anchoredPosition = new Vector2(0, -RingSize * 0.55f - 14f);

            var lTxt = labelObj.AddComponent<Text>();
           lTxt.font = GameFont.Regular;
            lTxt.fontSize = 12;
            lTxt.fontStyle = FontStyle.Bold;
            lTxt.alignment = TextAnchor.MiddleCenter;
            lTxt.raycastTarget = false;

            var o = labelObj.AddComponent<Outline>();
            o.effectColor = new Color(0, 0, 0, 0.9f);
            o.effectDistance = new Vector2(1f, -1f);

            if (key >= SiegeKeyOffset)
            {
                // Кольцо осады — снаружи кольца стройки, подпись выше
                bgRt.sizeDelta = fRt.sizeDelta = new Vector2(RingSize * 1.25f, RingSize * 1.25f);
                lRt.anchoredPosition = new Vector2(0, RingSize * 0.7f + 10f);
            }

            _rings[key] = root;
            _fills[key] = fImg;
            _labels[key] = lTxt;

            if (key >= SiegeKeyOffset) UpdateSiegeVisual(key);
            else UpdateRingVisual(key, fleet);
        }

        private static Sprite _cachedRingSprite;

        private Sprite CreateRingSprite(int size, float thicknessRatio)
        {
            if (_cachedRingSprite != null) return _cachedRingSprite;

            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;

            Vector2 c = new Vector2(size * 0.5f, size * 0.5f);
            float rOuter = size * 0.5f - 2f;
            float rInner = rOuter * (1f - thicknessRatio * 6f);
            float mid = (rOuter + rInner) * 0.5f;
            float range = Mathf.Max(0.01f, (rOuter - rInner) * 0.5f);

            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = Vector2.Distance(new Vector2(x, y), c);
                float a = 0f;
                if (d <= rOuter && d >= rInner)
                    a = Mathf.Clamp01(1f - Mathf.Abs(d - mid) / range);
                tex.SetPixel(x, y, new Color(1, 1, 1, a));
            }
            tex.Apply();

            _cachedRingSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100);
            return _cachedRingSprite;
        }

        private void UpdateRingVisual(int sysId, FleetView fleet)
        {
            if (!_fills.ContainsKey(sysId) || fleet?.Data == null) return;

            var fill = _fills[sysId];
            var label = _labels[sysId];

            float progress;
            Color col;
            string txt;

            if (fleet.Data.State == FleetState.Constructing)
            {
                float total = Mathf.Max(1f, fleet.Data.TotalConstructionDays);
                progress = 1f - (fleet.Data.DaysRemainingConstruction / total);
                col = new Color(1f, 0.62f, 0.25f);
                txt = $"⚙ Монтаж · {(int)fleet.Data.DaysRemainingConstruction}д";
            }
            else
            {
                float total = Mathf.Max(1f, fleet.Data.TotalSurveyDays);
                progress = 1f - (fleet.Data.DaysRemainingSurvey / total);
                col = new Color(0.35f, 0.90f, 1f);
                txt = $"◎ Разведка · {(int)fleet.Data.DaysRemainingSurvey}д";
            }

            fill.fillAmount = Mathf.Clamp01(progress);
            fill.color = col;
            label.text = $"<color=#{ColorUtility.ToHtmlStringRGB(col)}>{txt}</color>";
        }

        private void LateUpdate()
        {
            if (_cam == null || _gen == null) return;

            foreach (var kv in _rings)
            {
                var ring = kv.Value;
                if (ring == null) continue;

                var sys = _gen.Systems[SystemOf(kv.Key)];
                ring.transform.position = sys.Position + new Vector3(0, 0.5f, 0);
                ring.transform.rotation = _cam.transform.rotation;

                float dist = Vector3.Distance(_cam.transform.position, ring.transform.position);
                float scale = Mathf.Clamp(dist / 40f, 0.55f, 2.0f);
                ring.transform.localScale = Vector3.one * scale;

                if (kv.Key >= SiegeKeyOffset) { UpdateSiegeVisual(kv.Key); continue; }
                var fleet = FindFleetForSystem(kv.Key);
                if (fleet != null) UpdateRingVisual(kv.Key, fleet);
            }
        }

        private FleetView FindFleetForSystem(int sysId)
        {
            if (_fleetMgr == null) return null;
            foreach (var f in _fleetMgr.AllFleets)
            {
                if (f?.Data == null) continue;
                if (f.Data.State == FleetState.Constructing && f.Data.BuildTargetSystemId == sysId) return f;
                if (f.Data.State == FleetState.Surveying && f.Data.SurveyTargetSystemId == sysId) return f;
            }
            return null;
        }
    }
}