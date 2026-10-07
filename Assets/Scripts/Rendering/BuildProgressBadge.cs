using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using StellarisClone.Core;

namespace StellarisClone.Rendering
{
    /// <summary>
    /// Прогресс постройки кораблей над системой-верфью: капсула с иконкой корабля, процентом,
    /// полосой прогресса и счётчиком очереди. Следует за системой на экране, не ловит клики,
    /// прячется в режиме системы и когда стапели пусты.
    /// </summary>
    public class BuildProgressBadge : MonoBehaviour
    {
        private const float W = 176f, H = 40f;

        private Canvas _canvas;
        private RectTransform _canvasRect, _root, _bar;
        private CanvasGroup _group;
        private Image _icon;
        private Text _title, _more;
        private float _alpha;
        private Camera _cam;

        public void Init(Canvas hud)
        {
            _canvas = hud;
            _canvasRect = (RectTransform)hud.transform;

            _root = LGBuild.Rect(hud.transform, "[UI] BuildProgressBadge");
            _root.anchorMin = _root.anchorMax = new Vector2(0.5f, 0.5f);
            _root.pivot = new Vector2(0.5f, 0f);
            _root.sizeDelta = new Vector2(W, H);
            _root.SetAsFirstSibling();   // под панелями интерфейса
            _group = _root.gameObject.AddComponent<CanvasGroup>();
            _group.alpha = 0f;
            _group.blocksRaycasts = false;
            _group.interactable = false;

            var bg = _root.gameObject.AddComponent<Image>();
            bg.color = new Color(0.02f, 0.07f, 0.09f, 0.92f);
            bg.raycastTarget = false;
            LG.Platter(_root.gameObject, H * 0.5f).SetRim(new Color(0.30f, 0.95f, 0.86f, 0.55f));

            _icon = LGIcons.Create(_root, LGIcon.Corvette, 20, UIManager.DS.NeonCyan);
            _icon.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(12, 0), new Vector2(20, 20));

            _title = LGBuild.Label(_root, "", 10, UIManager.DS.TextPrimary, TextAnchor.UpperLeft, bold: true);
            _title.rectTransform.Stretch(38, 0, 36, 7);

            var barHost = LGBuild.Rect(_root, "Bar");
            barHost.anchorMin = new Vector2(0, 0); barHost.anchorMax = new Vector2(1, 0);
            barHost.offsetMin = new Vector2(38, 8); barHost.offsetMax = new Vector2(-36, 14);
            _bar = LGBuild.Bar(barHost, UIManager.DS.NeonCyan, 0f, 5f);

            _more = LGBuild.Label(_root, "", 11, UIManager.DS.Gold, TextAnchor.MiddleRight, bold: true);
            _more.rectTransform.Stretch(0, 0, 12, 0);

            // Ножка-указатель вниз к системе
            var stem = LGBuild.Panel(_root, "Stem", new Color(0.30f, 0.95f, 0.86f, 0.55f));
            stem.rectTransform.At(new Vector2(0.5f, 0f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(1.5f, 14f));
        }

        private static string ShipName(ConstructionJob j) =>
            j.ShipType == FleetType.Science ? "Научный корабль" : j.ShipType == FleetType.Constructor ? "Строитель"
            : j.Hull == ShipClass.Destroyer ? "Эсминец" : j.Hull == ShipClass.Frigate ? "Фрегат" : "Корвет";

        private static LGIcon ShipIcon(ConstructionJob j) =>
            j.ShipType == FleetType.Science ? LGIcon.Sensors : j.ShipType == FleetType.Constructor ? LGIcon.Construction : ModuleVisuals.HullIcon(j.Hull);

        private void LateUpdate()
        {
            if (_root == null) return;
            bool show = TryPlace(out ConstructionJob lead, out int total);
            _alpha = Mathf.MoveTowards(_alpha, show ? 1f : 0f, Time.unscaledDeltaTime * 5f);
            _group.alpha = _alpha;
            if (!show || lead == null) return;

            _icon.sprite = LGIcons.Get(ShipIcon(lead));
            _title.text = $"{ShipName(lead)}  <color=#4DF2DB>{Mathf.RoundToInt(lead.Progress * 100f)}%</color>";
            LGBuild.SetBar(_bar, lead.Progress);
            _more.text = total > 1 ? $"+{total - 1}" : "";
        }

        /// <summary>Есть ли что показать и где (над верфью на экране).</summary>
        private bool TryPlace(out ConstructionJob lead, out int total)
        {
            lead = null;
            total = 0;
            if (!UIManager.IsGameStarted) return false;
            if (SystemViewManager.Instance != null && SystemViewManager.Instance.IsInSystemView) return false;
            var cm = ConstructionManager.Instance;
            var fm = FleetManager.Instance;
            if (cm == null || fm == null) return false;

            List<ConstructionJob> q = cm.ShipQueue(0);
            total = q.Count;
            if (total == 0) return false;
            // Ведущий — ближайший к готовности из тех, что на стапелях
            for (int i = 0; i < Mathf.Min(q.Count, ConstructionManager.ShipyardSlots); i++)
                if (lead == null || q[i].DaysLeft < lead.DaysLeft) lead = q[i];
            if (lead == null) return false;

            var sys = EmpireStats.GetSystem(fm.PlayerShipyardSystem());
            if (sys == null) return false;
            if (_cam == null) _cam = Camera.main;
            if (_cam == null) return false;
            Vector3 screen = _cam.WorldToScreenPoint(sys.Position);
            if (screen.z <= 0f) return false;

            var uiCam = _canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : _canvas.worldCamera;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_canvasRect, screen, uiCam, out Vector2 local)) return false;
            // Выше значков поселения и имени системы
            _root.anchoredPosition = local + new Vector2(0f, 62f);
            return true;
        }
    }
}
