using System.Collections.Generic;
using UnityEngine;
using StellarisClone.Core;

namespace StellarisClone.Rendering
{
    /// <summary>
    /// Туман войны на карте галактики (как в Civilization VI):
    ///   • неисследованные системы, их подписи и ведущие к ним коридоры скрыты;
    ///   • исследованные, но сейчас не видимые — притушены тёмной вуалью;
    ///   • чужие границы и названия империй — только в исследованной части карты.
    /// Перестраивается, только когда меняется видимость игрока (Vision.PlayerVersion).
    /// </summary>
    public partial class GalaxyView
    {
        private bool _isolated;
        private int _isolatedId = -1;
        private int _fogVersion = int.MinValue;
        private int _fogExplored = -1;
        private readonly Dictionary<int, SpriteRenderer> _fogVeils = new Dictionary<int, SpriteRenderer>();

        private void UpdateFog()
        {
            if (_generator == null || _starNodes.Count == 0) return;
            if (!Vision.Active)
            {
                if (_fogVersion != int.MinValue) ClearFog();
                return;
            }
            if (_fogVersion == Vision.PlayerVersion) return;
            _fogVersion = Vision.PlayerVersion;
            ApplyFog();
        }

        /// <summary>Применить туман сейчас (после смены видимости или изоляции системы).</summary>
        private void ApplyFog()
        {
            int explored = 0;
            for (int i = 0; i < _generator.Systems.Count && i < _starNodes.Count; i++)
            {
                var s = _generator.Systems[i];
                bool known = Vision.PlayerKnows(s.Id);
                bool seen = known && Vision.PlayerSees(s.Id);
                if (known) explored++;
                bool show = known && (!_isolated || s.Id == _isolatedId);
                if (_starNodes[i] != null && _starNodes[i].activeSelf != show) _starNodes[i].SetActive(show);
                if (_nameplates.TryGetValue(s.Id, out var plate) && plate != null) plate.SetVisible(show);
                SetVeil(i, show && !seen && !_isolated);
            }

            for (int i = 0; i < _hyperlaneRenderers.Count && i < _hyperlaneData.Count; i++)
            {
                var lane = _hyperlaneData[i];
                bool on = Vision.PlayerKnows(lane.SystemA) && Vision.PlayerKnows(lane.SystemB);
                if (_hyperlaneRenderers[i] != null && _hyperlaneRenderers[i].enabled != on) _hyperlaneRenderers[i].enabled = on;
            }

            // Открылись новые системы — границы соседей и названия империй пересчитываются
            if (explored != _fogExplored)
            {
                _fogExplored = explored;
                RefreshTerritoryVisuals();
            }
        }

        private void ClearFog()
        {
            _fogVersion = int.MinValue;
            _fogExplored = -1;
            for (int i = 0; i < _starNodes.Count && i < _generator.Systems.Count; i++)
            {
                bool show = !_isolated || _generator.Systems[i].Id == _isolatedId;
                if (_starNodes[i] != null && _starNodes[i].activeSelf != show) _starNodes[i].SetActive(show);
                SetVeil(i, false);
            }
            foreach (var kv in _nameplates) if (kv.Value != null) kv.Value.SetVisible(!_isolated || kv.Key == _isolatedId);
            foreach (var lr in _hyperlaneRenderers) if (lr != null) lr.enabled = true;
        }

        /// <summary>Тёмная вуаль поверх звезды: система известна, но сейчас её никто не видит.</summary>
        private void SetVeil(int index, bool on)
        {
            _fogVeils.TryGetValue(index, out var veil);
            if (veil == null)
            {
                if (!on || index >= _starNodes.Count || _starNodes[index] == null) return;
                var go = new GameObject("FogVeil");
                go.transform.SetParent(_starNodes[index].transform, false);
                go.transform.localScale = Vector3.one * 5.5f;
                veil = go.AddComponent<SpriteRenderer>();
                veil.sprite = _starCoreSprite;
                var shader = GetSpriteShader();
                if (shader != null) veil.material = new Material(shader);
                veil.color = new Color(0.01f, 0.015f, 0.03f, 0.62f);
                veil.sortingOrder = 19;
                go.AddComponent<BillboardLookAt>();
                _fogVeils[index] = veil;
            }
            if (veil.enabled != on) veil.enabled = on;
        }

        /// <summary>Для границ: чужая система попадает на карту, только если игрок её исследовал.</summary>
        private static bool FogAllows(StarSystem s) => s.OwnerId == 0 || Vision.PlayerKnows(s.Id);
    }
}
