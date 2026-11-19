using UnityEngine;
using UnityEngine.UI;

/// <summary>Анимация шкалы [[meter]] в тултипе. Variant: 1 — плавный рост, 2 — блик-сканер, 3 — пульс кромки.</summary>
public class MeterAnim : MonoBehaviour
{
    public static int Variant = 1;

    public Image Fill;
    private Image _fx;
    private float _target, _shown, _t;

    public void SetTarget(float k)
    {
        _target = Mathf.Max(0.004f, k);
        if (Variant != 1) _shown = _target;
        Apply();
    }

    private void OnEnable() { _shown = 0f; _t = 0f; }

    private void EnsureFx()
    {
        if (_fx != null) return;
        var go = new GameObject("Fx", typeof(RectTransform), typeof(Image));
        go.transform.SetParent(Fill.transform, false);
        _fx = go.GetComponent<Image>();
        _fx.raycastTarget = false;
    }

    private void Update()
    {
        if (Fill == null) return;
        float dt = Time.unscaledDeltaTime;
        _t += dt;
        // рост к цели (во всех вариантах — догоняет при изменении прогресса)
        _shown = Mathf.Lerp(_shown, _target, 1f - Mathf.Exp(-(Variant == 1 ? 6f : 12f) * dt));
        Apply();

        if (Variant == 1) { if (_fx) _fx.enabled = false; return; }
        EnsureFx();
        _fx.enabled = true;
        var rt = _fx.rectTransform;
        if (Variant == 2)
        {
            // светлая полоса пробегает по заполненной части
            float p = Mathf.Repeat(_t * 0.6f, 1.4f) - 0.2f;
            rt.anchorMin = new Vector2(Mathf.Clamp01(p), 0f);
            rt.anchorMax = new Vector2(Mathf.Clamp01(p + 0.12f), 1f);
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            _fx.color = new Color(1f, 1f, 1f, 0.45f * Mathf.Sin(Mathf.Clamp01(p + 0.06f) * Mathf.PI));
        }
        else
        {
            // пульсирующая яркая «голова» на кромке заполнения
            rt.anchorMin = new Vector2(1f, 0f); rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            float s = 0.5f + 0.5f * Mathf.Sin(_t * 4f);
            rt.sizeDelta = new Vector2(4f + 4f * s, 4f + 4f * s);
            rt.anchoredPosition = Vector2.zero;
            Color c = Fill.color; c = Color.Lerp(c, Color.white, 0.6f); c.a = 0.5f + 0.5f * s;
            _fx.color = c;
        }
    }

    private void Apply() => Fill.rectTransform.anchorMax = new Vector2(_shown, 1f);
}
