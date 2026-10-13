using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using StellarisClone.Core;

namespace StellarisClone.Rendering
{
    /// <summary>
    /// Курсор игры вместо системного (Resources/UI/Cursors, рисует Tools/cursors/make_cursors.py):
    ///   • стрелка — по умолчанию;
    ///   • подсвеченная стрелка — над тем, что можно нажать: кнопки интерфейса, звёзды, свои флоты, планеты;
    ///   • прицел — выделен флот и курсор на звезде: ПКМ отправит его туда.
    /// Размер курсора — по высоте экрана (32 / 48 / 64 px). Создаётся сам при запуске.
    /// </summary>
    public class GameCursor : MonoBehaviour
    {
        private enum Kind { Arrow, Hover, Move }
        private static readonly string[] Names = { "arrow", "hover", "move" };

        private readonly Texture2D[] _tex = new Texture2D[3];
        private int _size;
        private Kind _kind;
        private bool _applied;
        private Camera _cam;
        private PointerEventData _ped;
        private readonly List<RaycastResult> _uiHits = new List<RaycastResult>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (FindFirstObjectByType<GameCursor>() != null) return;
            var go = new GameObject("[UI] GameCursor");
            DontDestroyOnLoad(go);
            go.AddComponent<GameCursor>();
        }

        private void Update()
        {
            int size = Screen.height <= 1100 ? 32 : Screen.height <= 1500 ? 48 : 64;
            if (size != _size)
            {
                _size = size;
                for (int i = 0; i < _tex.Length; i++) _tex[i] = Load(Names[i], size);
                _applied = false;
            }

            var kind = Detect();
            if (_applied && kind == _kind) return;
            _kind = kind;
            _applied = true;

            var t = _tex[(int)kind] ?? _tex[(int)Kind.Arrow];
            if (t == null) { Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto); return; }
            // Остриё стрелки — в левом верхнем углу картинки, у прицела точка — в центре
            var hotspot = kind == Kind.Move ? new Vector2(t.width * 0.5f, t.height * 0.5f)
                                            : new Vector2(t.width * 0.086f, t.height * 0.068f);
            Cursor.SetCursor(t, hotspot, CursorMode.Auto);
        }

        private void OnEnable() => _applied = false;
        private void OnDisable() => Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);

        // ==================== ЧТО ПОД КУРСОРОМ ====================

        private Kind Detect()
        {
            var es = EventSystem.current;
            if (es != null && es.IsPointerOverGameObject())
                return OverInteractiveUi(es) ? Kind.Hover : Kind.Arrow;

            if (!UIManager.IsGameStarted || MenuAtmosphere.IsActive) return Kind.Arrow;
            if (GalaxyView.HoveredMiniPlanet != null) return Kind.Hover;

            Vector2 mouse = Input.mousePosition;
            if (OverOwnFleetBadge(mouse)) return Kind.Hover;

            if (_cam == null) _cam = Camera.main;
            if (_cam == null) return Kind.Arrow;
            if (!Physics.Raycast(_cam.ScreenPointToRay(mouse), out var hit, 2000f)) return Kind.Arrow;

            var col = hit.collider;
            if (col.GetComponentInParent<FleetView>() != null) return Kind.Hover;
            if (col.GetComponentInParent<SystemViewManager.PlanetClickNotifier>() != null) return Kind.Hover;
            if (col.GetComponentInParent<StarSystemSelector>() != null)
            {
                bool inSystem = SystemViewManager.Instance != null && SystemViewManager.Instance.IsInSystemView;
                var fm = FleetManager.Instance;
                return !inSystem && fm != null && fm.SelectedFleets.Count > 0 ? Kind.Move : Kind.Hover;
            }
            return Kind.Arrow;
        }

        /// <summary>Верхний элемент интерфейса под курсором — часть активной кнопки, переключателя, поля ввода…</summary>
        private bool OverInteractiveUi(EventSystem es)
        {
            if (_ped == null || _ped.eventSystem != es) _ped = new PointerEventData(es);
            _ped.position = Input.mousePosition;
            _uiHits.Clear();
            es.RaycastAll(_ped, _uiHits);
            if (_uiHits.Count == 0 || _uiHits[0].gameObject == null) return false;
            var sel = _uiHits[0].gameObject.GetComponentInParent<Selectable>();
            return sel != null && sel.IsInteractable();
        }

        /// <summary>Значок своего флота на карте — как его ловит FleetSelectionController.PickFleet.</summary>
        private static bool OverOwnFleetBadge(Vector2 mouse)
        {
            foreach (var ind in FleetIndicator.All)
            {
                if (ind == null || !ind.IsLeader || !ind.OnScreen || ind.Fleet?.Data == null) continue;
                if (ind.Fleet.Data.OwnerId != 0) continue;
                if (Vector2.Distance(mouse, ind.ScreenPos) <= ind.ScreenRadius + 3f) return true;
            }
            return false;
        }

        // ==================== ТЕКСТУРЫ ====================

        /// <summary>
        /// Курсору нужна читаемая RGBA32-текстура без мипмапов (так настроен импорт в .meta);
        /// если импорт другой — копируем картинку через RenderTexture, чтобы курсор не пропал.
        /// </summary>
        private static Texture2D Load(string name, int size)
        {
            var t = Resources.Load<Texture2D>($"UI/Cursors/cursor_{name}_{size}");
            if (t == null) return null;
            if (t.isReadable && t.format == TextureFormat.RGBA32 && t.mipmapCount == 1) return t;

            var rt = RenderTexture.GetTemporary(t.width, t.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var prev = RenderTexture.active;
            Graphics.Blit(t, rt);
            RenderTexture.active = rt;
            var copy = new Texture2D(t.width, t.height, TextureFormat.RGBA32, false) { name = t.name + "_cursor" };
            copy.ReadPixels(new Rect(0, 0, t.width, t.height), 0, 0);
            copy.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            return copy;
        }
    }
}
