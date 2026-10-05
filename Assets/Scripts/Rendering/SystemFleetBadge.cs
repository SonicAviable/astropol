using System.Collections.Generic;
using UnityEngine;
using StellarisClone.Core;

namespace StellarisClone.Rendering
{
    /// <summary>
    /// Значок флотов возле системы.
    /// Использует те же спрайты, что FleetIndicator.
    /// Крупные иконки и цифры, сильно растут при отдалении камеры.
    /// </summary>
    public class SystemFleetBadge : MonoBehaviour
    {
        private StarSystem _system;
        private Transform _cam;
        private GameObject _badgeRoot;

        private const float RefreshInterval = 0.25f;
        private const float IconSpacing = 1.15f;      // расстояние между блоками
        private const float BlockGap = 0.35f;         // доп. зазор между блоками
        private const float IconYOffset = 3.4f;

        private float _refreshTimer;
        private string _lastSignature = "";

        private readonly List<GameObject> _rows = new List<GameObject>();

        private static Font _font;

        public void Initialize(StarSystem system)
        {
            _system = system;
            if (Camera.main != null) _cam = Camera.main.transform;
            if (_font == null)
                _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf")
                     ?? Font.CreateDynamicFontFromOSFont("Arial", 96);
            CreateRoot();
            Refresh();
        }

        private void CreateRoot()
        {
            _badgeRoot = new GameObject("FleetBadge");
            _badgeRoot.transform.SetParent(transform, false);
            _badgeRoot.transform.localPosition = new Vector3(0f, IconYOffset, -1.0f);
            _badgeRoot.SetActive(false);
        }

        private void Update()
        {
            _refreshTimer -= Time.deltaTime;
            if (_refreshTimer > 0f) return;
            _refreshTimer = RefreshInterval;
            Refresh();
        }

        private void Refresh()
        {
            if (FleetManager.Instance == null) return;

            int milCount = 0, sciCount = 0, conCount = 0;

            foreach (var f in FleetManager.Instance.AllFleets)
            {
                if (f?.Data == null || f.Data.Destroyed) continue;
                if (f.Data.CurrentSystemId != _system.Id) continue;
                if (f.Data.State == FleetState.InHyperlane) continue;

                switch (f.Data.Type)
                {
                    case FleetType.Military:    milCount++; break;
                    case FleetType.Science:     sciCount++; break;
                    case FleetType.Constructor: conCount++; break;
                }
            }

            int total = milCount + sciCount + conCount;
            string sig = $"{milCount}_{sciCount}_{conCount}";

            if (sig == _lastSignature && _badgeRoot.activeSelf == (total > 0)) return;
            _lastSignature = sig;

            ClearRows();

            if (total == 0)
            {
                _badgeRoot.SetActive(false);
                return;
            }

            if (!_badgeRoot.activeSelf) _badgeRoot.SetActive(true);

            int blocks = 0;
            if (milCount > 0) blocks++;
            if (sciCount > 0) blocks++;
            if (conCount > 0) blocks++;

            float step = IconSpacing + BlockGap;
            float totalWidth = blocks * step - BlockGap;
            float startX = -totalWidth * 0.5f + IconSpacing * 0.5f;

            int idx = 0;
            if (milCount > 0)
                AddBlock("Mil", FleetIndicator.GetMilitarySprite(), milCount,
                    new Color(1f, 0.42f, 0.32f),
                    startX + idx++ * step);
            if (sciCount > 0)
                AddBlock("Sci", FleetIndicator.GetScienceSprite(), sciCount,
                    new Color(0.35f, 1f, 0.55f),
                    startX + idx++ * step);
            if (conCount > 0)
                AddBlock("Con", FleetIndicator.GetConstructorSprite(), conCount,
                    new Color(1f, 0.82f, 0.30f),
                    startX + idx++ * step);
        }

        private void AddBlock(string name, Sprite icon, int count, Color tint, float xPos)
        {
            if (icon == null) return;

            var block = new GameObject($"Block_{name}");
            block.transform.SetParent(_badgeRoot.transform, false);
            block.transform.localPosition = new Vector3(xPos, 0f, 0f);

            // Иконка со сдвигом влево
            float iconX = -0.28f;
            float iconScale = 1.05f;

            // Тень иконки
            var shadowObj = new GameObject("IconShadow");
            shadowObj.transform.SetParent(block.transform, false);
            shadowObj.transform.localPosition = new Vector3(iconX + 0.07f, -0.07f, -0.04f);
            shadowObj.transform.localScale = Vector3.one * iconScale;
            var shSr = shadowObj.AddComponent<SpriteRenderer>();
            shSr.sprite = icon;
            if (ShaderCache.Sprite != null) shSr.material = new Material(ShaderCache.Sprite);
            shSr.color = new Color(0f, 0f, 0f, 0.9f);
            shSr.sortingOrder = 50;

            // Основная иконка
            var iconObj = new GameObject("Icon");
            iconObj.transform.SetParent(block.transform, false);
            iconObj.transform.localPosition = new Vector3(iconX, 0f, -0.05f);
            iconObj.transform.localScale = Vector3.one * iconScale;
            var iconSr = iconObj.AddComponent<SpriteRenderer>();
            iconSr.sprite = icon;
            if (ShaderCache.Sprite != null) iconSr.material = new Material(ShaderCache.Sprite);
            iconSr.color = tint;
            iconSr.sortingOrder = 51;

            // Крупная цифра
            const int FontPx = 128;
            const float CharSize = 0.038f;

            float numX = 0.28f;

            // Тень цифры
            var numShadow = new GameObject("NumShadow");
            numShadow.transform.SetParent(block.transform, false);
            numShadow.transform.localPosition = new Vector3(numX + 0.07f, -0.07f, -0.04f);

            var sm = numShadow.AddComponent<TextMesh>();
            sm.font = _font;
            sm.text = count.ToString();
            sm.fontSize = FontPx;
            sm.characterSize = CharSize;
            sm.fontStyle = FontStyle.Bold;
            sm.anchor = TextAnchor.MiddleLeft;
            sm.alignment = TextAlignment.Left;
            sm.color = new Color(0f, 0f, 0f, 0.95f);
            numShadow.GetComponent<MeshRenderer>().sortingOrder = 51;

            // Основная цифра
            var numObj = new GameObject("Num");
            numObj.transform.SetParent(block.transform, false);
            numObj.transform.localPosition = new Vector3(numX, 0f, -0.05f);

            var tm = numObj.AddComponent<TextMesh>();
            tm.font = _font;
            tm.text = count.ToString();
            tm.fontSize = FontPx;
            tm.characterSize = CharSize;
            tm.fontStyle = FontStyle.Bold;
            tm.anchor = TextAnchor.MiddleLeft;
            tm.alignment = TextAlignment.Left;
            tm.color = Color.white;
            numObj.GetComponent<MeshRenderer>().sortingOrder = 52;

            _rows.Add(block);
        }

        private void ClearRows()
        {
            for (int i = _rows.Count - 1; i >= 0; i--)
                if (_rows[i] != null) Destroy(_rows[i]);
            _rows.Clear();

            for (int i = _badgeRoot.transform.childCount - 1; i >= 0; i--)
                Destroy(_badgeRoot.transform.GetChild(i).gameObject);
        }

        private void LateUpdate()
        {
            if (_cam == null || _badgeRoot == null) return;
            if (!_badgeRoot.activeSelf) return;

            _badgeRoot.transform.rotation = _cam.rotation;

            // Скейл: очень быстро растёт при отдалении
            float dist = Vector3.Distance(_cam.position, transform.position);
            float scale = Mathf.Clamp(dist * 0.028f, 1.8f, 6.5f);
            _badgeRoot.transform.localScale = Vector3.one * scale;
        }
    }
}