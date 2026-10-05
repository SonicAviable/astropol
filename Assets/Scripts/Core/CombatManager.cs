using System.Collections.Generic;
using UnityEngine;
using StellarisClone.Rendering;
using StellarisClone.Generation;

namespace StellarisClone.Core
{
    public class CombatManager : MonoBehaviour
    {
        public static CombatManager Instance { get; private set; }

        public const float KineticVsShield = 1.45f;
        public const float KineticVsArmor = 0.65f;
        public const float EnergyVsShield = 0.70f;
        public const float EnergyVsArmor = 1.45f;
        public const float ExplosiveVsAll = 1.05f;

        private GalaxyGenerator _generator;
        private readonly List<LineRenderer> _beams = new List<LineRenderer>();
        private readonly List<FloatingDmg> _floaters = new List<FloatingDmg>();
        private GameObject _fxRoot;
        private GameObject _hud;
        private CanvasGroup _hudGroup;
        private UnityEngine.UI.Text _hudTitle;
        private UnityEngine.UI.Text _hudBody;
        private UnityEngine.UI.Button _ftlBtn;
        private Font _font;
        private float _ftlFailLock;

        private class FloatingDmg
        {
            public TextMesh Mesh;
            public float Life;
            public Vector3 Velocity;
        }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            _font = GameFont.Regular;
        }

        private void Start()
        {
            _generator = FindFirstObjectByType<GalaxyGenerator>();
            _fxRoot = new GameObject("CombatFxRoot");
            _fxRoot.transform.SetParent(transform, false);
            BuildHud();
        }

        private void Update()
        {
            if (!UIManager.IsGameStarted) { HideHud(); return; }
            if (TimeManager.Instance != null && TimeManager.Instance.CurrentSpeed == 0) return;

            TickCombat(Time.deltaTime);
            TickBeams(Time.deltaTime);
            TickFloaters(Time.deltaTime);
            RefreshHud();
        }

        public static bool AreHostile(FleetData a, FleetData b)
        {
            if (a == null || b == null) return false;
            if (a.OwnerId == b.OwnerId) return false;
            if (a.Destroyed || b.Destroyed) return false;

            var ai = AIEmpireManager.Instance;
            bool involvesAi = (a.OwnerId == AIEmpireManager.AIOwnerId) || (b.OwnerId == AIEmpireManager.AIOwnerId);
            bool involvesPlayer = a.OwnerId == 0 || b.OwnerId == 0;
            if (involvesAi && involvesPlayer)
                return ai == null || ai.IsHostileToPlayer;
            return true;
        }

        private void TickCombat(float dt)
        {
            var fleets = FindObjectsByType<FleetView>(FindObjectsSortMode.None);
            var bySystem = new Dictionary<int, List<FleetView>>();
            foreach (var fv in fleets)
            {
                if (fv?.Data == null || fv.Data.Destroyed) continue;
                if (fv.Data.State == FleetState.InHyperlane) continue;
                int sid = fv.Data.CurrentSystemId;
                if (!bySystem.TryGetValue(sid, out var list))
                {
                    list = new List<FleetView>();
                    bySystem[sid] = list;
                }
                list.Add(fv);
            }

            var engaged = new HashSet<FleetView>();

            foreach (var kv in bySystem)
            {
                var list = kv.Value;
                if (list.Count < 2) continue;

                var sys = (_generator != null && kv.Key >= 0 && kv.Key < _generator.Systems.Count)
                    ? _generator.Systems[kv.Key] : null;
                bool disputed = sys == null || sys.OwnerId < 0
                    || HasMixedOwners(list);

                for (int i = 0; i < list.Count; i++)
                {
                    for (int j = i + 1; j < list.Count; j++)
                    {
                        var a = list[i];
                        var b = list[j];
                        if (!AreHostile(a.Data, b.Data)) continue;
                        if (!disputed && sys != null && sys.OwnerId >= 0
                            && a.Data.OwnerId != sys.OwnerId && b.Data.OwnerId != sys.OwnerId)
                            continue;

                        if (a.Data.Type != FleetType.Military && b.Data.Type != FleetType.Military)
                            continue;

                        engaged.Add(a);
                        engaged.Add(b);
                        a.Data.InCombat = true;
                        b.Data.InCombat = true;

                        if (a.Data.Type == FleetType.Military) FireAt(a, b, dt);
                        if (b.Data.Type == FleetType.Military) FireAt(b, a, dt);
                    }
                }
            }

            foreach (var fv in fleets)
            {
                if (fv?.Data == null) continue;
                if (fv.Data.InCombat && !engaged.Contains(fv))
                    fv.Data.InCombat = false;
            }
        }

        private static bool HasMixedOwners(List<FleetView> list)
        {
            int first = list[0].Data.OwnerId;
            for (int i = 1; i < list.Count; i++)
                if (list[i].Data.OwnerId != first) return true;
            return false;
        }

        private void FireAt(FleetView attacker, FleetView defender, float dt)
        {
            var a = attacker.Data;
            var d = defender.Data;
            if (a.Damage <= 0f || a.FireRate <= 0f) return;
            if (d.Destroyed) return;

            a.FireCooldown -= dt;
            if (a.FireCooldown > 0f) return;
            a.FireCooldown = 1f / Mathf.Max(0.2f, a.FireRate);

            float evadeChance = Mathf.Clamp01(d.Evasion / 100f);
            if (Random.value < evadeChance * 0.45f)
            {
                SpawnFloater(defender.transform.position, "МИМО", new Color(0.7f, 0.85f, 0.9f));
                SpawnBeam(attacker.transform.position, defender.transform.position, WeaponColor(a.PrimaryWeapon), 0.12f);
                return;
            }

            float dealt = ApplyLayeredDamage(d, a.Damage, a.PrimaryWeapon);
            SpawnBeam(attacker.transform.position, defender.transform.position, WeaponColor(a.PrimaryWeapon), 0.18f);
            SpawnFloater(defender.transform.position, $"-{dealt:0}", WeaponColor(a.PrimaryWeapon));

            if (d.HullPoints <= 0f)
                DestroyFleet(defender);
        }

        public static float ApplyLayeredDamage(FleetData target, float raw, WeaponDamageType type)
        {
            float remaining = raw;
            float shown = 0f;

            if (target.ShieldPoints > 0f && remaining > 0f)
            {
                float mul = type == WeaponDamageType.Kinetic ? KineticVsShield
                    : type == WeaponDamageType.Energy ? EnergyVsShield
                    : ExplosiveVsAll;
                float hit = remaining * mul;
                float absorbed = Mathf.Min(target.ShieldPoints, hit);
                target.ShieldPoints -= absorbed;
                remaining -= absorbed / mul;
                shown += absorbed;
            }

            if (target.ArmorPoints > 0f && remaining > 0f)
            {
                float mul = type == WeaponDamageType.Energy ? EnergyVsArmor
                    : type == WeaponDamageType.Kinetic ? KineticVsArmor
                    : ExplosiveVsAll;
                float hit = remaining * mul;
                float absorbed = Mathf.Min(target.ArmorPoints, hit);
                target.ArmorPoints -= absorbed;
                remaining -= absorbed / mul;
                shown += absorbed;
            }

            if (remaining > 0f)
            {
                float hullHit = remaining;
                target.HullPoints = Mathf.Max(0f, target.HullPoints - hullHit);
                shown += hullHit;
            }

            return shown;
        }

        private void DestroyFleet(FleetView fv)
        {
            if (fv?.Data == null) return;
            fv.Data.Destroyed = true;
            fv.Data.InCombat = false;
            SpawnFloater(fv.transform.position, "УНИЧТОЖЕН", UIManager.DS.Red);

            string ownerName = fv.Data.OwnerId == 0 ? "Ваш корабль" : "USAF";
            NotificationCenter.Show("Корабль уничтожен",
                $"{ownerName}: {fv.Data.Name}",
                fv.Data.OwnerId == 0 ? NotificationCenter.Kind.Danger : NotificationCenter.Kind.Success,
                5f);

            if (FleetManager.Instance != null)
                FleetManager.Instance.NotifyFleetDestroyed(fv);

            Destroy(fv.gameObject, 0.15f);
        }

        public bool TryEmergencyFtl(FleetView fleet)
        {
            if (fleet?.Data == null || !fleet.Data.InCombat) return false;
            if (_ftlFailLock > Time.unscaledTime) return false;
            if (_generator == null) return false;

            bool success = Random.value < 0.72f;
            if (!success)
            {
                _ftlFailLock = Time.unscaledTime + 2.4f;
                SpawnFloater(fleet.transform.position, "СБОЙ ГПД", UIManager.DS.Gold);
                return false;
            }

            var sys = _generator.Systems[fleet.Data.CurrentSystemId];
            int escape = -1;
            foreach (int n in sys.ConnectedSystemIds)
            {
                if (n < 0 || n >= _generator.Systems.Count) continue;
                escape = n;
                if (_generator.Systems[n].OwnerId == fleet.Data.OwnerId) break;
            }
            if (escape < 0) return false;

            fleet.Data.InCombat = false;
            fleet.Data.Path.Clear();
            FleetManager.Instance?.IssueMoveOrder(fleet, escape);
            SpawnFloater(fleet.transform.position, "ГПД АКТИВИРОВАН", UIManager.DS.NeonCyan);
            return true;
        }

        private static Color WeaponColor(WeaponDamageType t) => t switch
        {
            WeaponDamageType.Energy => new Color(1f, 0.25f, 0.22f, 1f),
            WeaponDamageType.Kinetic => new Color(1f, 0.85f, 0.35f, 1f),
            _ => new Color(0.45f, 1f, 0.55f, 1f)
        };

        private void SpawnBeam(Vector3 from, Vector3 to, Color col, float width)
        {
            var go = new GameObject("CombatBeam");
            go.transform.SetParent(_fxRoot.transform, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.positionCount = 2;
            lr.useWorldSpace = true;
            lr.startWidth = width;
            lr.endWidth = width * 0.35f;
            lr.SetPosition(0, from + Vector3.up * 0.4f);
            lr.SetPosition(1, to + Vector3.up * 0.4f);
            var sh = ShaderCache.Unlit;
            if (sh != null)
            {
                var mat = new Material(sh);
                mat.color = col;
                lr.material = mat;
            }
            lr.startColor = col;
            lr.endColor = new Color(col.r, col.g, col.b, 0.15f);
            _beams.Add(lr);
        }

        private void TickBeams(float dt)
        {
            for (int i = _beams.Count - 1; i >= 0; i--)
            {
                var lr = _beams[i];
                if (lr == null) { _beams.RemoveAt(i); continue; }
                var c0 = lr.startColor;
                c0.a -= dt * 4f;
                lr.startColor = c0;
                if (c0.a <= 0.02f)
                {
                    Destroy(lr.gameObject);
                    _beams.RemoveAt(i);
                }
            }
        }

        private void SpawnFloater(Vector3 pos, string text, Color col)
        {
            var go = new GameObject("DmgFloater");
            go.transform.position = pos + Vector3.up * 2.2f + Random.insideUnitSphere * 0.4f;
            go.transform.SetParent(_fxRoot.transform, true);
            var tm = go.AddComponent<TextMesh>();
            tm.text = text;
            tm.fontSize = 28;
            tm.characterSize = 0.11f;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            tm.color = col;
            if (_font != null) tm.font = _font;
            _floaters.Add(new FloatingDmg { Mesh = tm, Life = 0.85f, Velocity = Vector3.up * 2.4f });
        }

        private void TickFloaters(float dt)
        {
            var cam = Camera.main;
            for (int i = _floaters.Count - 1; i >= 0; i--)
            {
                var f = _floaters[i];
                f.Life -= dt;
                if (f.Mesh == null) { _floaters.RemoveAt(i); continue; }
                f.Mesh.transform.position += f.Velocity * dt;
                if (cam != null) f.Mesh.transform.rotation = cam.transform.rotation;
                var c = f.Mesh.color;
                c.a = Mathf.Clamp01(f.Life / 0.85f);
                f.Mesh.color = c;
                if (f.Life <= 0f)
                {
                    Destroy(f.Mesh.gameObject);
                    _floaters.RemoveAt(i);
                }
            }
        }

        private void BuildHud()
        {
            var canvas = FindFirstObjectByType<UIManager>()?.GetComponentInChildren<Canvas>();
            if (canvas == null) return;

            _hud = new GameObject("CombatHud");
            _hud.transform.SetParent(canvas.transform, false);
            var rt = _hud.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0f);
            rt.anchorMax = new Vector2(0.5f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.anchoredPosition = new Vector2(0, 28);
            rt.sizeDelta = new Vector2(520, 92);

            _hudGroup = _hud.AddComponent<CanvasGroup>();
            var img = _hud.AddComponent<UnityEngine.UI.Image>();
            img.color = UIManager.DS.BgDeep;
            var ol = _hud.AddComponent<UnityEngine.UI.Outline>();
            ol.effectColor = UIManager.DS.Red;
            ol.effectDistance = new Vector2(1.4f, -1.4f);

            _hudTitle = MakeText(_hud.transform, "ЛОКАЛЬНЫЙ КОНФЛИКТ", 13, FontStyle.Bold, UIManager.DS.Gold, TextAnchor.UpperCenter);
            var tRt = _hudTitle.rectTransform;
            tRt.anchorMin = new Vector2(0, 0.55f);
            tRt.anchorMax = new Vector2(1, 1);
            tRt.offsetMin = new Vector2(8, 0);
            tRt.offsetMax = new Vector2(-8, -6);

            _hudBody = MakeText(_hud.transform, "", 11, FontStyle.Normal, UIManager.DS.TextPrimary, TextAnchor.UpperCenter);
            var bRt = _hudBody.rectTransform;
            bRt.anchorMin = new Vector2(0, 0.28f);
            bRt.anchorMax = new Vector2(1, 0.62f);
            bRt.offsetMin = new Vector2(10, 0);
            bRt.offsetMax = new Vector2(-10, 0);

            var ftlGo = new GameObject("FtlBtn");
            ftlGo.transform.SetParent(_hud.transform, false);
            var fRt = ftlGo.AddComponent<RectTransform>();
            fRt.anchorMin = new Vector2(0.5f, 0f);
            fRt.anchorMax = new Vector2(0.5f, 0f);
            fRt.pivot = new Vector2(0.5f, 0f);
            fRt.anchoredPosition = new Vector2(0, 8);
            fRt.sizeDelta = new Vector2(360, 26);
            var fImg = ftlGo.AddComponent<UnityEngine.UI.Image>();
            fImg.color = UIManager.DS.BtnDanger;
            _ftlBtn = ftlGo.AddComponent<UnityEngine.UI.Button>();
            _ftlBtn.onClick.AddListener(() =>
            {
                var sel = FleetManager.Instance?.SelectedFleet;
                if (sel != null) TryEmergencyFtl(sel);
            });
            MakeText(ftlGo.transform, "⚠  ЭКСТРЕННОЕ ГИПЕРПРОСТРАНСТВЕННОЕ ОТСТУПЛЕНИЕ", 10, FontStyle.Bold, Color.white, TextAnchor.MiddleCenter)
                .rectTransform.sizeDelta = fRt.sizeDelta;

            _hud.SetActive(false);
        }

        private UnityEngine.UI.Text MakeText(Transform parent, string val, int size, FontStyle style, Color col, TextAnchor anchor)
        {
            var go = new GameObject("Txt");
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<UnityEngine.UI.Text>();
            t.font = _font;
            t.text = val;
            t.fontSize = size;
            t.fontStyle = style;
            t.color = col;
            t.alignment = anchor;
            t.raycastTarget = false;
            t.supportRichText = true;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            return t;
        }

        private void RefreshHud()
        {
            var sel = FleetManager.Instance?.SelectedFleet;
            bool show = sel != null && sel.Data != null && sel.Data.InCombat && sel.Data.OwnerId == 0;
            if (_hud == null) return;
            _hud.SetActive(show);
            if (!show) return;

            var d = sel.Data;
            _hudTitle.text = "ЛОКАЛЬНЫЙ КОНФЛИКТ · БОЕВОЙ КОНТАКТ";
            _hudBody.text = $"{d.Name}  ·  Корпус {d.HullPoints:0}/{d.MaxHullPoints:0}  ·  Броня {d.ArmorPoints:0}  ·  Щиты {d.ShieldPoints:0}";
        }

        private void HideHud()
        {
            if (_hud != null) _hud.SetActive(false);
        }
    }
}