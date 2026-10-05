using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using StellarisClone.Core;

namespace StellarisClone.Rendering
{
    /// <summary>
    /// Отслеживает систему под курсором и подсвечивает соответствующий неймплейт.
    /// Работает через Physics.Raycast — требует коллайдер на звёздах (уже есть).
    /// </summary>
    public class SystemNameplateHover : MonoBehaviour
    {
        public static SystemNameplateHover Instance { get; private set; }

        private Camera _cam;
        private readonly Dictionary<int, SystemNameplate> _plates = new Dictionary<int, SystemNameplate>();
        private int _hoveredId = -1;

        private void Awake()
        {
            if (Instance == null) Instance = this;
            else { Destroy(gameObject); return; }
        }

        private void Start()
        {
            _cam = Camera.main;
        }

        public void Register(SystemNameplate plate)
        {
            if (plate?.Data == null) return;
            _plates[plate.Data.Id] = plate;
        }

        public void Unregister(SystemNameplate plate)
        {
            if (plate?.Data == null) return;
            if (_plates.TryGetValue(plate.Data.Id, out var existing) && existing == plate)
                _plates.Remove(plate.Data.Id);
        }

        private void Update()
        {
            if (_cam == null) return;

            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
            {
                SetHovered(-1);
                return;
            }

            Ray ray = _cam.ScreenPointToRay(Input.mousePosition);
            if (Physics.Raycast(ray, out RaycastHit hit, 800f))
            {
                var selector = hit.collider.GetComponentInParent<StarSystemSelector>();
                if (selector?.Data != null)
                {
                    SetHovered(selector.Data.Id);
                    return;
                }
            }
            SetHovered(-1);
        }

        private void SetHovered(int sysId)
        {
            if (_hoveredId == sysId) return;

            if (_hoveredId != -1 && _plates.TryGetValue(_hoveredId, out var old))
                old.SetHovered(false);

            _hoveredId = sysId;

            if (sysId != -1 && _plates.TryGetValue(sysId, out var cur))
                cur.SetHovered(true);
        }
    }
}