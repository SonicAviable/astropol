using UnityEngine;
using StellarisClone.Core;

namespace StellarisClone.Rendering
{
    public class SystemNameplate : MonoBehaviour
    {
        public StarSystem Data => _data;

        private StarSystem _data;
        private Transform _cam;
        private GameObject _plateRoot;
        private TextMesh _titleMesh;
        private TextMesh _titleShadow;
        // Добыча системы — плашки со значками: титан (бирюзовый) и гелий-3 (золотой)
        private WorldChip _chipMin, _chipEn;
        private string _resKey;
        private bool _hasMin, _hasEn;
        private bool _isSelected;
        private bool _isHovered;
        private bool _tooltipShown;
        private bool _hiddenByIsolation;

        // Уровни детализации по расстоянию камеры (как в Stellaris: издалека — только империи,
        // ближе — свои и чужие владения, вплотную — все системы и добыча)
        private const float NearFrom = 85f, NearTo = 115f;    // все системы
        private const float MidFrom = 165f, MidTo = 205f;     // владения и системы с вашими флотами
        private const float ResFrom = 60f, ResTo = 85f;       // строка добычи
        private float _alpha = 1f, _resAlpha = 1f;
        private Color _titleBase = Color.white;
        private MeshRenderer _titleMr, _shadowMr;

        public void Initialize(StarSystem data)
        {
            _data = data;
            if (Camera.main != null) _cam = Camera.main.transform;

            CreateStellarisNameplate();
            UpdateVisuals();

            if (SystemNameplateHover.Instance != null)
                SystemNameplateHover.Instance.Register(this);
        }

        private void CreateStellarisNameplate()
        {
            _plateRoot = new GameObject("Nameplate_Visual");
            _plateRoot.transform.SetParent(transform, false);
            _plateRoot.transform.localPosition = new Vector3(0, -3.2f, 0);

            // Шрифт игры (Play), как во всём интерфейсе; запасной — встроенный
            Font font = GameFont.Bold ?? Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            var shadowObj = new GameObject("TitleShadow");
            shadowObj.transform.SetParent(_plateRoot.transform, false);
            shadowObj.transform.localPosition = new Vector3(0.08f, -0.08f, 0.02f);

            _titleShadow = shadowObj.AddComponent<TextMesh>();
            _titleShadow.font = font;
            _titleShadow.fontSize = 52;
            _titleShadow.characterSize = 0.16f;
            _titleShadow.fontStyle = FontStyle.Bold;
            _titleShadow.anchor = TextAnchor.MiddleCenter;
            _titleShadow.alignment = TextAlignment.Center;
            _titleShadow.text = _data.Name;
            _titleShadow.color = new Color(0f, 0f, 0f, 0.95f);

            var mrShadow = shadowObj.GetComponent<MeshRenderer>();
            if (font != null) mrShadow.sharedMaterial = font.material;
            mrShadow.sortingOrder = 49;
            _shadowMr = mrShadow;

            var titleObj = new GameObject("TitleText");
            titleObj.transform.SetParent(_plateRoot.transform, false);
            titleObj.transform.localPosition = Vector3.zero;

            _titleMesh = titleObj.AddComponent<TextMesh>();
            _titleMesh.font = font;
            _titleMesh.fontSize = 52;
            _titleMesh.characterSize = 0.16f;
            _titleMesh.fontStyle = FontStyle.Bold;
            _titleMesh.anchor = TextAnchor.MiddleCenter;
            _titleMesh.alignment = TextAlignment.Center;
            _titleMesh.text = _data.Name;

            var mrTitle = titleObj.GetComponent<MeshRenderer>();
            if (font != null) mrTitle.sharedMaterial = font.material;
            mrTitle.sortingOrder = 50;
            _titleMr = mrTitle;

            var resRow = new GameObject("Resources");
            resRow.transform.SetParent(_plateRoot.transform, false);
            resRow.transform.localPosition = new Vector3(0, -1.15f, 0);
            _chipMin = WorldChip.Create(resRow.transform, "Titan", 46, false, true);
            _chipMin.SetIcon(LGIcon.Minerals);
            _chipEn = WorldChip.Create(resRow.transform, "Helium", 46, false, true);
            _chipEn.SetIcon(LGIcon.Energy);
            _chipMin.SetAlpha(0f);
            _chipEn.SetAlpha(0f);

            UpdateResourceBadge();
        }

        public void UpdateResourceBadge()
        {
            if (_chipMin == null || _data == null) return;

            // Добыча, а если ещё ничего не добывается — залежи (бледнее)
            int min = 0, en = 0;
            bool potential = false;
            if (_data.IsSurveyed)
            {
                min = _data.HarvestedMinerals;
                en = _data.HarvestedEnergy;
                if (min <= 0 && en <= 0)
                {
                    min = _data.PotentialMinerals;
                    en = _data.PotentialEnergy;
                    potential = min > 0 || en > 0;
                }
            }

            _hasMin = min > 0;
            _hasEn = en > 0;
            string key = $"{min}|{en}|{potential}";
            if (key == _resKey) { ApplyAlpha(); return; }
            _resKey = key;

            Color cyan = UIManager.DS.NeonCyan, gold = UIManager.DS.Gold;
            float dim = potential ? 0.55f : 1f;
            StyleChip(_chipMin, cyan, dim);
            StyleChip(_chipEn, gold, dim);

            float gap = 0.14f, total = 0f;
            if (min > 0) { _chipMin.Set(min.ToString(), 0.38f); total += _chipMin.Width; }
            if (en > 0) { _chipEn.Set(en.ToString(), 0.38f); total += (total > 0f ? gap : 0f) + _chipEn.Width; }
            float x = -total * 0.5f;
            if (min > 0) { _chipMin.CenterOn(x + _chipMin.Width * 0.5f, 0f); x += _chipMin.Width + gap; }
            if (en > 0) _chipEn.CenterOn(x + _chipEn.Width * 0.5f, 0f);
            ApplyAlpha();
        }

        private static void StyleChip(WorldChip c, Color col, float dim)
        {
            c.SetColors(new Color(0.015f, 0.04f, 0.055f, 0.80f * dim),
                        new Color(col.r, col.g, col.b, 0.45f * dim),
                        col,
                        new Color(col.r, col.g, col.b, dim),
                        new Color(Mathf.Lerp(col.r, 1f, 0.55f), Mathf.Lerp(col.g, 1f, 0.55f), Mathf.Lerp(col.b, 1f, 0.55f), dim));
        }

        public void UpdateVisuals()
        {
            if (_titleMesh == null) return;

            if (_isSelected)
                _titleBase = UIManager.DS.Gold;
            else if (_data.OwnerId == 0)
                _titleBase = UIManager.DS.NeonCyan;
            else if (_data.OwnerId > 0)
                _titleBase = Color.Lerp(FleetIndicator.OwnerColor(_data.OwnerId), Color.white, 0.12f);
            else if (!GalaxyView.IsKnownToPlayer(_data))
                _titleBase = new Color(0.58f, 0.64f, 0.72f, 0.8f);   // неизведанная — блёклая
            else
            {
                var nc = UIManager.DS.TextPrimary;
                _titleBase = new Color(nc.r, nc.g, nc.b, 0.95f);
            }

            UpdateResourceBadge();
            ApplyAlpha();
        }

        public void SetSelected(bool selected)
        {
            _isSelected = selected;
            UpdateVisuals();
        }

        public void SetHovered(bool hovered)
        {
            _isHovered = hovered;
            UpdateHoverTooltip();
        }

        public void SetVisible(bool visible)
        {
            _hiddenByIsolation = !visible;
            if (_plateRoot != null) _plateRoot.SetActive(visible);
        }

        private void ApplyAlpha()
        {
            if (_titleMesh == null) return;
            var t = _titleBase; t.a *= _alpha;
            _titleMesh.color = t;
            if (_titleShadow != null) _titleShadow.color = new Color(0f, 0f, 0f, 0.95f * _alpha);
            bool on = _alpha > 0.01f;
            if (_titleMr != null && _titleMr.enabled != on) _titleMr.enabled = on;
            if (_shadowMr != null && _shadowMr.enabled != on) _shadowMr.enabled = on;
            if (_chipMin != null)
            {
                _chipMin.SetAlpha(_hasMin ? _resAlpha : 0f);
                _chipEn.SetAlpha(_hasEn ? _resAlpha : 0f);
            }
        }

        private static float Smooth(float a, float b, float x) => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(a, b, x));

        public string GetTooltipContent()
        {
            string owner = _data.OwnerId == 0 ? "Ваша империя"
                         : _data.OwnerId > 0 ? AIEmpireManager.NameOf(_data.OwnerId, "Другая империя")
                         : "Нейтральная";

            string tip = $"<b>{_data.Name}</b>\n" +
                         $"<color=#AAB4C0>Класс:</color> {_data.SpectralClass}\n" +
                         $"<color=#AAB4C0>Владелец:</color> {owner}\n";

            if (_data.IsSurveyed)
            {
                tip += $"\n<color=#66FF88>ДОБЫЧА</color>\n" +
                       $"◆ Титан: +{_data.HarvestedMinerals}\n" +
                       $"⚡ Гелий-3: +{_data.HarvestedEnergy}\n";
            }
            else
            {
                tip += "\n<color=#FFAA88>Требуется разведка</color>\n";
            }

            tip += FleetsLine() + EtaLine();
            return tip;
        }

        /// <summary>Флоты на орбите (свои и чужие).</summary>
        private string FleetsLine()
        {
            var fm = FleetManager.Instance;
            if (fm == null) return "";
            int own = 0, enemy = 0;
            foreach (var f in fm.GetFleetsInSystem(_data.Id))
                if (f.Data.OwnerId == 0) own++; else enemy++;
            if (own == 0 && enemy == 0) return "";
            return $"\n<color=#AAB4C0>Флоты:</color> " +
                   (own > 0 ? $"<color=#39EBDB>ваши {own}</color>" : "") +
                   (own > 0 && enemy > 0 ? "  ·  " : "") +
                   (enemy > 0 ? $"<color=#FF6666>чужие {enemy}</color>" : "") + "\n";
        }

        /// <summary>Сколько лететь сюда выделенному флоту.</summary>
        private string EtaLine()
        {
            var fm = FleetManager.Instance;
            var f = fm != null ? fm.SelectedFleet : null;
            if (f?.Data == null) return "";
            var gen = FindAnyObjectByType<StellarisClone.Generation.GalaxyGenerator>();
            int from = f.Data.State == FleetState.InHyperlane ? f.Data.TargetSystemId : f.Data.CurrentSystemId;
            if (from == _data.Id) return "";
            var path = GalaxyPathfinder.FindPath(from, _data.Id, gen);
            if (path == null) return "";
            float days = path.Count * FleetRoute.DaysPerJump(f.Data);
            if (f.Data.State == FleetState.InHyperlane) days += f.Data.DaysRemainingInTransit / FleetRoute.JumpSpeed(f.Data);
            return $"\n<color=#4DF2DB>{f.Data.Name}: ~{FleetRoute.FormatDays(days)} ({path.Count} прыжк.)</color>\n" +
                   "<color=#8AA2A8>ПКМ — лететь, Shift+ПКМ — в очередь</color>";
        }

        private void UpdateHoverTooltip()
        {
            if (TooltipSystem.Instance == null) return;

            if (_isHovered)
            {
                if (!_tooltipShown)
                {
                    _tooltipShown = true;
                    TooltipSystem.Show(GetTooltipContent());
                }
            }
            else if (_tooltipShown)
            {
                _tooltipShown = false;
                TooltipSystem.Hide();
            }
        }

        private void LateUpdate()
        {
            if (_cam == null || _plateRoot == null) return;

            if (_hiddenByIsolation) return;
            if (!_plateRoot.activeSelf) _plateRoot.SetActive(true);

            _plateRoot.transform.rotation = _cam.rotation;

            float dist = Vector3.Distance(_cam.position, transform.position);
            float scale = Mathf.Clamp(dist * 0.012f, 1.1f, 3.2f);
            _plateRoot.transform.localScale = Vector3.one * scale;

            // Видимость подписи по зуму
            bool known = GalaxyView.IsKnownToPlayer(_data);
            float baseA = known || _data.OwnerId > 0 ? 1f : 0.5f;
            float near = 1f - Smooth(NearFrom, NearTo, dist);
            float mid = 1f - Smooth(MidFrom, MidTo, dist);
            float target = baseA * near;
            if (_data.OwnerId >= 0) target = Mathf.Max(target, mid);
            if (GalaxyView.PlayerFleetSystems.Contains(_data.Id)) target = Mathf.Max(target, mid);
            if (_isSelected || _isHovered) target = 1f;
            float resTarget = known ? (1f - Smooth(ResFrom, ResTo, dist)) : 0f;
            if (_isSelected && known) resTarget = Mathf.Max(resTarget, near);

            float k = Time.unscaledDeltaTime * 5f;
            float na = Mathf.MoveTowards(_alpha, target, k);
            float nr = Mathf.MoveTowards(_resAlpha, Mathf.Min(resTarget, target), k);
            if (Mathf.Abs(na - _alpha) > 0.0005f || Mathf.Abs(nr - _resAlpha) > 0.0005f)
            {
                _alpha = na; _resAlpha = nr;
                ApplyAlpha();
            }
        }

        private void OnDestroy()
        {
            if (SystemNameplateHover.Instance != null)
                SystemNameplateHover.Instance.Unregister(this);

            if (_plateRoot != null) Destroy(_plateRoot);
        }
    }
}