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
            _resMesh.color = new Color(resCol.r, resCol.g, resCol.b, 0.95f);
        }

        public void UpdateVisuals()
        {
            if (_titleMesh == null) return;

            if (_isSelected)
            {
                _titleMesh.color = UIManager.DS.Gold;
            }
            else if (_data.OwnerId == 0)
            {
                _titleMesh.color = UIManager.DS.NeonCyan;
            }
            else if (_data.OwnerId > 0)
            {
                _titleMesh.color = UIManager.DS.Red;
            }
            else
            {
                var nc = UIManager.DS.TextPrimary;
                _titleMesh.color = new Color(nc.r, nc.g, nc.b, 0.95f);
            }

            UpdateResourceBadge();
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
            if (_plateRoot != null) _plateRoot.SetActive(visible);
        }

        public string GetTooltipContent()
        {
            string owner = _data.OwnerId == 0 ? "Ваша империя"
                         : _data.OwnerId > 0 ? "Другая империя"
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

            return tip;
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

            if (!_plateRoot.activeSelf) _plateRoot.SetActive(true);

            _plateRoot.transform.rotation = _cam.rotation;

            float dist = Vector3.Distance(_cam.position, transform.position);
            float scale = Mathf.Clamp(dist * 0.012f, 1.1f, 3.2f);
            _plateRoot.transform.localScale = Vector3.one * scale;
        }

        private void OnDestroy()
        {
            if (SystemNameplateHover.Instance != null)
                SystemNameplateHover.Instance.Unregister(this);

            if (_plateRoot != null) Destroy(_plateRoot);
        }
    }
}