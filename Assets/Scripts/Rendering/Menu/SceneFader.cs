using System;
using UnityEngine;
using UnityEngine.UI;

namespace StellarisClone.Rendering
{
    /// <summary>
    /// Чёрная шторка поверх всего (живёт между перезагрузками сцены):
    /// затемнение перед переходом и мягкое проявление новой сцены.
    /// </summary>
    public class SceneFader : MonoBehaviour
    {
        private static SceneFader s_instance;

        private CanvasGroup _group;
        private float _target;
        private float _speed = 2f;
        private int _holdFrames;
        private Action _onBlack;

        private static SceneFader Instance
        {
            get
            {
                if (s_instance != null) return s_instance;
                var go = new GameObject("[SceneFader]");
                DontDestroyOnLoad(go);
                s_instance = go.AddComponent<SceneFader>();
                s_instance.Build();
                return s_instance;
            }
        }

        public static bool IsBusy => s_instance != null && (s_instance._onBlack != null || s_instance._group.alpha > 0.01f);

        private void Build()
        {
            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 6000;
            gameObject.AddComponent<GraphicRaycaster>();
            LG.Ignore(gameObject);   // не превращать шторку в стекло авто-скиннингом

            var img = new GameObject("Black").AddComponent<Image>();
            img.transform.SetParent(transform, false);
            img.rectTransform.anchorMin = Vector2.zero;
            img.rectTransform.anchorMax = Vector2.one;
            img.rectTransform.offsetMin = img.rectTransform.offsetMax = Vector2.zero;
            img.color = new Color(0.004f, 0.010f, 0.016f, 1f);
            img.raycastTarget = true;

            _group = gameObject.AddComponent<CanvasGroup>();
            _group.alpha = 0f;
            _group.blocksRaycasts = false;
        }

        /// <summary>Затемнить экран и выполнить действие (обычно — перезагрузку сцены).</summary>
        public static void FadeOutThen(Action onBlack, float duration = 0.45f)
        {
            var f = Instance;
            f._onBlack = onBlack;
            f._target = 1f;
            f._speed = 1f / Mathf.Max(0.05f, duration);
            f._group.blocksRaycasts = true;
            if (f._group.alpha >= 0.999f) f.FireBlack();
        }

        /// <summary>Сразу чёрный экран, затем проявление (запуск игры, новая сцена).</summary>
        public static void RevealFromBlack(float duration = 0.9f, int holdFrames = 3)
        {
            var f = Instance;
            f._onBlack = null;
            f._group.alpha = 1f;
            f._group.blocksRaycasts = true;
            f._target = 0f;
            f._speed = 1f / Mathf.Max(0.05f, duration);
            f._holdFrames = holdFrames;
        }

        private void FireBlack()
        {
            var cb = _onBlack;
            _onBlack = null;
            cb?.Invoke();
        }

        private void Update()
        {
            if (_holdFrames > 0) { _holdFrames--; return; }
            // Ограничиваем шаг: тяжёлый первый кадр новой сцены не должен «съесть» анимацию
            float dt = Mathf.Min(Time.unscaledDeltaTime, 1f / 30f);
            float a = Mathf.MoveTowards(_group.alpha, _target, dt * _speed);
            _group.alpha = a;
            if (_target >= 1f && a >= 0.999f && _onBlack != null) FireBlack();
            if (_target <= 0f && a <= 0.001f) _group.blocksRaycasts = false;
        }
    }
}
