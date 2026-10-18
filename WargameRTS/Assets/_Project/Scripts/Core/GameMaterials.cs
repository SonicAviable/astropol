using System.Collections.Generic;
using UnityEngine;

namespace WargameRTS.Core
{
    /// <summary>
    /// Creates and caches simple colored materials for the procedural sandbox.
    /// Works with URP and falls back to built-in shaders.
    /// </summary>
    public static class GameMaterials
    {
        static readonly Dictionary<Color, Material> litCache = new Dictionary<Color, Material>();
        static readonly Dictionary<Color, Material> unlitCache = new Dictionary<Color, Material>();
        static Shader litShader;
        static Shader unlitShader;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            litCache.Clear();
            unlitCache.Clear();
            litShader = null;
            unlitShader = null;
        }

        public static Material Lit(Color color)
        {
            if (litCache.TryGetValue(color, out var cached) && cached != null)
                return cached;

            var material = CreateLit(color);
            litCache[color] = material;
            return material;
        }

        public static Material Unlit(Color color)
        {
            if (unlitCache.TryGetValue(color, out var cached) && cached != null)
                return cached;

            if (unlitShader == null)
                unlitShader = FindShader("Universal Render Pipeline/Unlit", "Unlit/Color");

            var material = new Material(unlitShader) { color = color };
            unlitCache[color] = material;
            return material;
        }

        /// <summary>Creates a new (uncached) lit material, e.g. for textured ground.</summary>
        public static Material CreateLit(Color color, float smoothness = 0.12f)
        {
            if (litShader == null)
                litShader = FindShader("Universal Render Pipeline/Lit", "Standard");

            var material = new Material(litShader) { color = color };
            if (material.HasProperty("_Smoothness"))
                material.SetFloat("_Smoothness", smoothness);
            if (material.HasProperty("_Glossiness"))
                material.SetFloat("_Glossiness", smoothness);
            return material;
        }

        static Shader FindShader(string preferred, string fallback)
        {
            var shader = Shader.Find(preferred);
            if (shader == null)
                shader = Shader.Find(fallback);
            if (shader == null)
                Debug.LogError($"[WargameRTS] Shader '{preferred}' / '{fallback}' not found.");
            return shader;
        }
    }
}
