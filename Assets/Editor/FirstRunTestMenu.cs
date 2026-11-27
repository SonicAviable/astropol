using StellarisClone.Rendering;
using UnityEditor;
using UnityEngine;

/// <summary>Отладка окна первого запуска.</summary>
public static class FirstRunTestMenu
{
    [MenuItem("Astropolity/Debug/Показать окно первого запуска")]
    private static void Show()
    {
        if (Application.isPlaying && MainMenuUI.Instance != null)
        {
            MainMenuUI.Instance.ShowFirstRun();
            return;
        }
        ResetFlag();
        Debug.Log("[FirstRun] Флаг сброшен — окно появится при следующем входе в главное меню (Play).");
    }

    [MenuItem("Astropolity/Debug/Сбросить флаг первого запуска")]
    private static void ResetFlag()
    {
        PlayerPrefs.SetInt("set.firstrun", 0);
        PlayerPrefs.Save();
    }
}
