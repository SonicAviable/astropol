using System;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace StellarisClone.Core
{
    /// <summary>Параметры новой партии (выбираются в главном меню, хранятся в сохранении).</summary>
    [Serializable]
    public class NewGameSettings
    {
        public int GalaxySize = 1;     // 0 малая · 1 средняя · 2 большая
        public int Difficulty = 1;     // 0 лёгкая · 1 нормальная · 2 сложная
        public int Seed;
        public bool Tutorial = true;
        public int Shape = 1;          // GalaxyShape: 0 эллипс · 1 спираль-2 · 2 спираль-4 · 3 кольцо

        public static readonly string[] SizeNames = { "Малая", "Средняя", "Большая" };
        public static readonly string[] DifficultyNames = { "Лёгкая", "Нормальная", "Сложная" };
        public static readonly string[] ShapeNames = { "Эллипс", "Спираль · 2 рукава", "Спираль · 4 рукава", "Кольцо" };

        public int StarCount => GalaxySize == 0 ? 50 : GalaxySize == 2 ? 120 : 80;
        public float Radius => GalaxySize == 0 ? 120f : GalaxySize == 2 ? 178f : 160f;
        public float MinStarDistance => GalaxySize == 2 ? 11f : 12f;
        public int DominationTarget => GalaxySize == 0 ? 25 : GalaxySize == 2 ? 60 : 40;

        /// <summary>Множитель доходов ИИ.</summary>
        public float AIIncome => Difficulty == 0 ? 0.7f : Difficulty == 2 ? 1.5f : 1f;
        /// <summary>Как быстро ИИ злится на сильного игрока.</summary>
        public float AIAggression => Difficulty == 0 ? 0.6f : Difficulty == 2 ? 1.5f : 1f;
        /// <summary>Множитель стартовых ресурсов игрока.</summary>
        public float PlayerStart => Difficulty == 0 ? 1.3f : Difficulty == 2 ? 0.85f : 1f;

        public NewGameSettings Clone() => (NewGameSettings)MemberwiseClone();

        public static int RandomSeed() => UnityEngine.Random.Range(10000, 999999);
    }

    /// <summary>
    /// Состояние запуска между перезагрузками сцены:
    ///   MainMenu — главное меню поверх живой галактики;
    ///   NewGame  — сразу выбор цивилизации (галактика по Settings);
    ///   LoadGame — восстановление из PendingLoad.
    /// Любой переход (новая игра, загрузка, выход в меню) — это затемнение + перезагрузка сцены.
    /// </summary>
    public static class GameSession
    {
        public enum StartMode { MainMenu, NewGame, LoadGame }

        public static StartMode Mode { get; private set; } = StartMode.MainMenu;
        public static NewGameSettings Settings { get; private set; } = new NewGameSettings();
        public static SaveFile PendingLoad { get; private set; }

        /// <summary>Сколько реальных секунд длится текущая партия (для сохранений).</summary>
        public static float PlayTime;

        public static bool IsLoading => Mode == StartMode.LoadGame && PendingLoad != null;

        private static bool s_reloading;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            // На случай выключенной перезагрузки домена в редакторе
            Mode = StartMode.MainMenu;
            Settings = new NewGameSettings();
            PendingLoad = null;
            PlayTime = 0f;
            s_reloading = false;
        }

        // ------------------------------------------------------------------ Переходы

        public static void StartNewGame(NewGameSettings settings)
        {
            Settings = (settings ?? new NewGameSettings()).Clone();
            if (Settings.Seed == 0) Settings.Seed = NewGameSettings.RandomSeed();
            Mode = StartMode.NewGame;
            PendingLoad = null;
            PlayTime = 0f;
            Reload();
        }

        /// <summary>Та же галактика заново (кнопка «Начать заново»).</summary>
        public static void RestartSameSettings()
        {
            Mode = StartMode.NewGame;
            PendingLoad = null;
            PlayTime = 0f;
            Reload();
        }

        public static void Load(SaveFile file)
        {
            if (file == null || file.State == null) return;
            PendingLoad = file;
            Settings = (file.State.Settings ?? new NewGameSettings()).Clone();
            Mode = StartMode.LoadGame;
            PlayTime = file.Meta != null ? file.Meta.PlayTime : 0f;
            Reload();
        }

        public static void ReturnToMainMenu()
        {
            Mode = StartMode.MainMenu;
            PendingLoad = null;
            Settings = new NewGameSettings();
            Reload();
        }

        /// <summary>Вызывается загрузчиком после восстановления партии.</summary>
        public static void MarkLoaded()
        {
            PendingLoad = null;
            Mode = StartMode.NewGame;   // «Начать заново» после загрузки = новая галактика с теми же параметрами
        }

        public static void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        // ------------------------------------------------------------------ Перезагрузка сцены

        private static void Reload()
        {
            if (s_reloading) return;
            s_reloading = true;
            StellarisClone.Rendering.SceneFader.FadeOutThen(DoReload);
        }

        private static void DoReload()
        {
            Time.timeScale = 1f;
            SceneManager.sceneLoaded += OnSceneReloaded;

            var scene = SceneManager.GetActiveScene();
            if (scene.buildIndex >= 0)
            {
                SceneManager.LoadScene(scene.buildIndex);
                return;
            }
#if UNITY_EDITOR
            UnityEditor.SceneManagement.EditorSceneManager.LoadSceneInPlayMode(scene.path, new LoadSceneParameters(LoadSceneMode.Single));
#else
            SceneManager.LoadScene(0);
#endif
        }

        private static void OnSceneReloaded(Scene scene, LoadSceneMode mode)
        {
            SceneManager.sceneLoaded -= OnSceneReloaded;
            s_reloading = false;
            GameBootstrap.Reboot();
        }
    }
}
