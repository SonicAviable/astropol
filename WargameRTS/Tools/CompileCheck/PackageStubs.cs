// API stubs for Unity packages that have no NuGet reference assemblies.
// Only members used by the project are declared; extend when needed.
namespace UnityEngine.InputSystem.Controls {
  public class InputControl {}
  public class ButtonControl : InputControl { public bool isPressed => false; public bool wasPressedThisFrame => false; public bool wasReleasedThisFrame => false; }
  public class KeyControl : ButtonControl {}
  public class Vector2Control : InputControl { public UnityEngine.Vector2 ReadValue() => default; }
  public class DeltaControl : Vector2Control {}
}
namespace UnityEngine.InputSystem {
  using UnityEngine.InputSystem.Controls;
  public enum Key { None, Space, Enter, Tab, Backquote, Quote, Semicolon, Comma, Period, Slash, Backslash, LeftBracket, RightBracket, Minus, Equals, A, B, C, D, E, F, G, H, I, J, K, L, M, N, O, P, Q, R, S, T, U, V, W, X, Y, Z, Digit1, Digit2, Digit3, Digit4, Digit5, Digit6, Digit7, Digit8, Digit9, Digit0, LeftShift, RightShift, LeftAlt, RightAlt, LeftCtrl, RightCtrl, Escape, LeftArrow, RightArrow, UpArrow, DownArrow, F1, F2 }
  public class InputDevice {}
  public class Pointer : InputDevice { public Vector2Control position => null; public DeltaControl delta => null; }
  public class Mouse : Pointer { public static Mouse current => null; public ButtonControl leftButton => null; public ButtonControl rightButton => null; public ButtonControl middleButton => null; public DeltaControl scroll => null; }
  public class Keyboard : InputDevice {
    public static Keyboard current => null;
    public KeyControl this[Key key] => null;
    public ButtonControl shiftKey => null; public ButtonControl ctrlKey => null; public ButtonControl altKey => null;
    public KeyControl aKey, dKey, eKey, fKey, qKey, rKey, sKey, wKey, xKey, escapeKey, upArrowKey, downArrowKey, leftArrowKey, rightArrowKey, f1Key;
  }
}
namespace Unity.AI.Navigation {
  public enum CollectObjects { All = 0, Volume = 1, Children = 2, MarkedWithModifier = 3 }
  public class NavMeshSurface : UnityEngine.MonoBehaviour {
    public int agentTypeID { get; set; }
    public CollectObjects collectObjects { get; set; }
    public UnityEngine.LayerMask layerMask { get; set; }
    public UnityEngine.AI.NavMeshCollectGeometry useGeometry { get; set; }
    public bool overrideVoxelSize { get; set; } public float voxelSize { get; set; }
    public void BuildNavMesh() {}
  }
}
