using System.Collections.Generic;
using UnityEngine;
using StellarisClone.Core;

namespace StellarisClone.Rendering
{
    /// <summary>
    /// Чудеса фракций на карте: модель вокруг звезды системы-стройки (Врата — рядом со звездой, напротив звёздной базы).
    /// Показываются вблизи камеры и только в известных игроку системах; модель пересобирается при смене этапа
    /// и каждые 5% прогресса. В виде системы — та же модель в полном масштабе (SpawnInSystemView).
    /// </summary>
    public class WonderVisuals : MonoBehaviour
    {
        public static WonderVisuals Instance { get; private set; }

        /// <summary>Масштаб модели на карте галактики относительно вида системы.</summary>
        private const float MapScale = 0.42f;
        private const float ShowHeight = 220f;

        private class Entry
        {
            public GameObject Go;
            public int SystemId = -1, Sig;
            public string Key;
        }

        private readonly Dictionary<int, Entry> _entries = new Dictionary<int, Entry>();
        private Transform _root;
        private float _timer;
        private object _systemsRef;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (_root != null) Destroy(_root.gameObject);
        }

        /// <summary>Площадка Врат: по другую сторону звезды от звёздной базы.</summary>
        public static Vector3 GateOffset(Vector3 star, float dist)
        {
            Vector3 sb = StarbaseVisuals.SiteFor(star) - star;
            sb.y = 0f;
            if (sb.sqrMagnitude < 0.01f) sb = Vector3.right;
            return -sb.normalized * dist;
        }

        public static WonderModels.Spec SpecOf(WonderSave st)
        {
            var sys = EmpireStats.GetSystem(st.SystemId);
            return new WonderModels.Spec
            {
                Key = st.Key,
                Built = st.Built,
                InProgress = st.InProgress,
                Progress = WonderManager.Instance != null ? WonderManager.Instance.Progress(st) : 0f,
                StarMul = sys != null ? StarFx.SizeMul(sys.SpectralClass) : 1f
            };
        }

        private void Update()
        {
            if (!UIManager.IsGameStarted || MenuAtmosphere.IsActive || WonderManager.Instance == null)
            {
                if (_entries.Count > 0) ClearAll();
                return;
            }
            if (_root == null) _root = new GameObject("Wonders").transform;

            var systems = EmpireStats.Systems;
            if (!ReferenceEquals(systems, _systemsRef)) { ClearAll(); _systemsRef = systems; }

            if ((_timer -= Time.unscaledDeltaTime) <= 0f)
            {
                _timer = 0.5f;
                Sync();
            }
            Visibility();
        }

        private void ClearAll()
        {
            foreach (var e in _entries.Values) if (e.Go != null) Destroy(e.Go);
            _entries.Clear();
        }

        private void Sync()
        {
            var seen = new HashSet<int>();
            foreach (var st in WonderManager.Instance.Sited)
            {
                var sys = EmpireStats.GetSystem(st.SystemId);
                if (sys == null) continue;
                seen.Add(st.Owner);
                var spec = SpecOf(st);
                int sig = WonderModels.Signature(spec);
                if (!_entries.TryGetValue(st.Owner, out var e)) { e = new Entry(); _entries[st.Owner] = e; }
                if (e.Go != null && e.Sig == sig && e.SystemId == st.SystemId && e.Key == st.Key) continue;

                if (e.Go != null) Destroy(e.Go);
                e.Go = WonderModels.Build(spec, _root);
                e.Sig = sig;
                e.SystemId = st.SystemId;
                e.Key = st.Key;
                var t = e.Go.transform;
                t.localScale = Vector3.one * MapScale;
                t.position = sys.Position + (st.Key == "iridia" ? GateOffset(sys.Position, 3.6f) + Vector3.up * 0.4f : Vector3.zero);
            }

            if (seen.Count != _entries.Count)
            {
                var gone = new List<int>();
                foreach (var id in _entries.Keys) if (!seen.Contains(id)) gone.Add(id);
                foreach (var id in gone)
                {
                    if (_entries[id].Go != null) Destroy(_entries[id].Go);
                    _entries.Remove(id);
                }
            }
        }

        private void Visibility()
        {
            var cam = Camera.main;
            bool inSystem = SystemViewManager.Instance != null && SystemViewManager.Instance.IsInSystemView;
            bool high = cam == null || cam.transform.position.y > ShowHeight;
            foreach (var kv in _entries)
            {
                var e = kv.Value;
                if (e.Go == null) continue;
                bool known = kv.Key == 0 || Vision.PlayerKnows(e.SystemId);
                bool show = known && !inSystem && !high;
                if (e.Go.activeSelf != show) e.Go.SetActive(show);
            }
        }

        /// <summary>Чудо в виде системы — полный масштаб; живой компонент пересобирает его по ходу стройки.</summary>
        public static void SpawnInSystemView(Transform container, StarSystem system)
        {
            var wm = WonderManager.Instance;
            if (container == null || system == null || wm == null) return;
            foreach (var st in wm.Sited)
            {
                if (st.SystemId != system.Id) continue;
                var holder = new GameObject("WonderHolder");
                holder.transform.SetParent(container, false);
                if (st.Key == "iridia")
                {
                    float minOrbit = 14f;
                    foreach (var p in system.Planets) minOrbit = Mathf.Min(minOrbit, p.OrbitRadius);
                    holder.transform.localPosition = GateOffset(system.Position, Mathf.Max(6f, minOrbit * 0.62f)) + Vector3.up * 0.6f;
                }
                holder.AddComponent<WonderLive>().Init(st.Owner);
            }
        }
    }

    /// <summary>Модель чуда в виде системы: пересобирается, когда меняется этап или прогресс.</summary>
    public class WonderLive : MonoBehaviour
    {
        private int _owner;
        private int _sig = int.MinValue;
        private GameObject _model;
        private float _timer;

        public void Init(int owner) { _owner = owner; Rebuild(); }

        private void Update()
        {
            if ((_timer -= Time.unscaledDeltaTime) > 0f) return;
            _timer = 1f;
            Rebuild();
        }

        private void Rebuild()
        {
            var wm = WonderManager.Instance;
            var st = wm != null ? wm.StateOf(_owner) : null;
            if (st == null) return;
            var spec = WonderVisuals.SpecOf(st);
            int sig = WonderModels.Signature(spec);
            if (sig == _sig) return;
            _sig = sig;
            if (_model != null) Destroy(_model);
            _model = WonderModels.Build(spec, transform);
        }
    }
}
