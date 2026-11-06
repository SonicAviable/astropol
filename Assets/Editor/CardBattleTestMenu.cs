using UnityEditor;
using UnityEngine;
using StellarisClone.Cards;

/// <summary>
/// Меню «Astropolity → Тестовый бой»: карточный бой со случайной расстановкой прямо из редактора.
/// Вне Play Mode — запускает игру и открывает бой, как только она загрузится.
/// В самой игре (Play Mode) то же — клавишами F10 / Shift+F10 / Ctrl+F10 (см. BattleSandbox).
/// </summary>
public static class CardBattleTestMenu
{
    [MenuItem("Astropolity/Тестовый бой/Штурм (вы атакуете) %&b", priority = 20)]
    private static void Assault() => Run(false);

    [MenuItem("Astropolity/Тестовый бой/Оборона (вы защищаете) %&d", priority = 21)]
    private static void Defense() => Run(true);

    [MenuItem("Astropolity/Тестовый бой/200 автобоёв — сводка баланса", priority = 40)]
    private static void Batch() => BattleSandbox.RunAutoBatch(200);

    private static void Run(bool defend)
    {
        if (EditorApplication.isPlaying)
        {
            BattleSandbox.StartTest(defend);
            return;
        }
        SessionState.SetInt(BattleSandbox.PendingKey, defend ? 2 : 1);
        EditorApplication.isPlaying = true;
    }
}
