using UnityEditor;
using UnityEngine;

namespace StellarisClone.EditorTools
{
    /// <summary>
    /// Настройки сборки, которые снимают упор в одно ядро:
    ///   • многопоточный рендеринг — подготовка кадра для видеокарты идёт в отдельном потоке рендера, а не в главном;
    ///   • графические задачи (graphics jobs) — отрисовку сцены Unity раскладывает по всем ядрам;
    ///   • инкрементальная сборка мусора — вместо редких подвисаний на сборке памяти мелкие порции каждый кадр.
    /// Применяется автоматически при открытии проекта (если кто-то выключил — включит обратно).
    /// </summary>
    [InitializeOnLoad]
    public static class MultiCoreSettings
    {
        static MultiCoreSettings() => EditorApplication.delayCall += Apply;

        [MenuItem("Tools/Производительность/Включить многопоточность")]
        public static void Apply()
        {
            bool changed = false;
            if (!PlayerSettings.MTRendering) { PlayerSettings.MTRendering = true; changed = true; }
            if (!PlayerSettings.graphicsJobs) { PlayerSettings.graphicsJobs = true; changed = true; }
            if (!PlayerSettings.gcIncremental) { PlayerSettings.gcIncremental = true; changed = true; }
            if (!changed) return;
            AssetDatabase.SaveAssets();
            Debug.Log("[MultiCoreSettings] Включены многопоточный рендеринг, graphics jobs и инкрементальная сборка мусора.");
        }
    }
}
