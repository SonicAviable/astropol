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
        private TextMesh _resMesh;
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
        private Color _titleBase = Color.white, _resBase = Color.white;
        private MeshRenderer _titleMr, _shadowMr, _resMr;

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

          Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf")
            ?? Font.CreateDynamicFontFromOSFont("Arial", 64);

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
            mrTitle.sortingOrder = 50;
            _titleMr = mrTitle;

            var resObj = new GameObject("ResText");
            resObj.transform.SetParent(_plateRoot.transform, false);
            resObj.transform.localPosition = new Vector3(0, -1.1f, 0);

            _resMesh = resObj.AddComponent<TextMesh>();
            _resMesh.font = font;
            _resMesh.fontSize = 38;
            _resMesh.characterSize = 0.12f;
            _resMesh.fontStyle = FontStyle.Bold;
            _resMesh.anchor = TextAnchor.MiddleCenter;
            _resMesh.alignment = TextAlignment.Center;

            var mrRes = resObj.GetComponent<MeshRenderer>();
            mrRes.sortingOrder = 50;
            _resMr = mrRes;

            UpdateResourceBadge();
        }

        public void UpdateResourceBadge()
        {
            if (_resMesh == null || _data == null) return;

            if (!_data.IsSurveyed)
            {
                _resMesh.text = "";
                return;
            }

            string res = "";
            if (_data.HarvestedMinerals > 0) res += $"◆{_data.HarvestedMinerals} ";
            if (_data.HarvestedEnergy > 0)   res += $"⚡{_data.HarvestedEnergy} ";

            if (string.IsNullOrEmpty(res) && (_data.PotentialMinerals > 0 || _data.PotentialEnergy > 0))
            {
                if (_data.PotentialMinerals > 0) res += $"◆{_data.PotentialMinerals} ";
                if (_data.PotentialEnergy > 0)   res += $"⚡{_data.PotentialEnergy} ";
            }

            _resMesh.text = res;
            var resCol = UIManager.DS.NeonCyan;
            _resBase = new Color(resCol.r, resCol.g, resCol.b, 0.95f);
            ApplyAlpha();
        }

        public void UpdateVisuals()
        {
            if (_titleMesh == null) return;

            if (_isSelected)
                _titleBase = UIManager.DS.Gold;
            else if (_data.OwnerId == 0)
                _titleBase = UIManager.DS.NeonCyan;
            else if (_data.OwnerId > 0)
                _titleBase = UIManager.DS.Red;
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
            if (_resMesh != null) { var r = _resBase; r.a *= _resAlpha; _resMesh.color = r; }
            bool on = _alpha > 0.01f;
            if (_titleMr != null && _titleMr.enabled != on) _titleMr.enabled = on;
            if (_shadowMr != null && _shadowMr.enabled != on) _shadowMr.enabled = on;
            bool resOn = _resAlpha > 0.01f;
            if (_resMr != null && _resMr.enabled != resOn) _resMr.enabled = resOn;
        }

        private static float Smooth(float a, float b, float x) => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(a, b, x));

        public string GetTooltipContent()
        {
            string owner = _data.OwnerId == 0 ? "Ваша империя"
                         : _data.OwnerId > 0 ? (AIEmpireManager.Instance != null ? AIEmpireManager.Instance.AIName : "Другая империя")
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