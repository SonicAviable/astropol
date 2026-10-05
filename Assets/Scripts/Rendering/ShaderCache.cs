using UnityEngine;
using UnityEngine.Rendering;

namespace StellarisClone.Rendering
{
    /// <summary>
    /// Кэширует и находит рабочие шейдеры независимо от версии Unity/URP.
    /// Логирует в Console, какой шейдер выбран.
    /// </summary>
    public static class ShaderCache
    {
        private static Shader _unlit;
        private static Shader _lit;
        private static Shader _sprite;

        public static Shader Unlit
        {
            get
            {
                if (_unlit != null) return _unlit;
                _unlit = FindFirst("Unlit", 
                    "Universal Render Pipeline/Unlit",
                    "Universal Render Pipeline/Simple Lit",
                    "Sprites/Default",
                    "Unlit/Color",
                    "Legacy Shaders/VertexLit");
                return _unlit;
            }
        }

        public static Shader Lit
        {
            get
            {
                if (_lit != null) return _lit;
                _lit = FindFirst("Lit",
                    "Universal Render Pipeline/Lit",
                    "Universal Render Pipeline/Simple Lit",
                    "Standard");
                return _lit;
            }
        }

        public static Shader Sprite
        {
            get
            {
                if (_sprite != null) return _sprite;
                _sprite = FindFirst("Sprite",
                    "Sprites/Default",
                    "UI/Default",
                    "Universal Render Pipeline/2D/Sprite-Unlit-Default",
                    "Universal Render Pipeline/2D/Sprite-Lit-Default",
                    "Unlit/Transparent",
                    "Legacy Shaders/Transparent/Diffuse");
                return _sprite;
            }
        }

        private static Shader FindFirst(string label, params string[] names)
        {
            foreach (var n in names)
            {
                var s = Shader.Find(n);
                if (s != null)
                {
                    Debug.Log($"<color=#5F5>[ShaderCache]</color> {label}: <b>{n}</b>");
                    return s;
                }
            }
            Debug.LogError($"<color=#F55>[ShaderCache]</color> НИ ОДИН шейдер не найден для {label}! Проверь pipeline.");
            return null;
        }

        /// <summary>
        /// Диагностика — печатает в Console активный pipeline.
        /// </summary>
        public static void Diagnose()
        {
            var rp = GraphicsSettings.defaultRenderPipeline;
            string rpName = rp == null ? "Built-in Render Pipeline" : rp.GetType().Name;
            Debug.Log($"<color=#5F5>[ShaderCache]</color> Активный pipeline: <b>{rpName}</b>");
            Debug.Log($"<color=#5F5>[ShaderCache]</color> Sprites/Default: {(Shader.Find("Sprites/Default") != null ? "✔ есть" : "✘ нет")}");
            Debug.Log($"<color=#5F5>[ShaderCache]</color> URP/Unlit: {(Shader.Find("Universal Render Pipeline/Unlit") != null ? "✔ есть" : "✘ нет")}");
            Debug.Log($"<color=#5F5>[ShaderCache]</color> URP/2D/Sprite-Unlit-Default: {(Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default") != null ? "✔ есть" : "✘ нет")}");
        }
    }
}