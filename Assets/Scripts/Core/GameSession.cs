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
        /// <summary>Грубая шкала прежних версий: 0 лёгкая · 1 нормальная · 2 сложная (пираты, шпионаж, коалиции). Выводится из Level.</summary>
        public int Difficulty = 1;
        /// <summary>Уровень сложности 0…4 (AIDifficulty); −1 — сохранение прежней версии, уровень берётся из Difficulty.</summary>
        public int Level = -1;
        public int Seed;
        public bool Tutorial = true;
        public int Shape = 1;          // GalaxyShape: 0 эллипс · 1 спираль-2 · 2 спираль-4 · 3 кольцо
        public int Rivals = AIEmpireManager.RivalCount;   // империй ИИ (1…5); в старых сохранениях поля нет — лишних уберёт загрузка
        /// <summary>Вольных миров (нейтральных систем) в партии: 2…6.</summary>
        public int Neutrals = 4;

        public const int MinNeutrals = 2, MaxNeutrals = 6;

        public static readonly string[] SizeNames = { Loc.T("Малая"), Loc.T("Средняя"), Loc.T("Большая") };
        public static readonly string[] DifficultyNames = { Loc.T("Новичок"), Loc.T("Лёгкая"), Loc.T("Нормальная"), Loc.T("Сложная"), Loc.T("Безжалостная") };
        public static readonly string[] RivalNames = { Loc.T("1 империя"), Loc.T("2 империи"), Loc.T("3 империи"), Loc.T("4 империи"), Loc.T("5 империй") };
        public static readonly string[] NeutralNames = { Loc.T("2 мира"), Loc.T("3 мира"), Loc.T("4 мира"), Loc.T("5 миров"), Loc.T("6 миров") };
        public static readonly string[] ShapeNames = { Loc.T("Эллипс"), Loc.T("Спираль · 2 рукава"), Loc.T("Спираль · 4 рукава"), Loc.T("Кольцо") };

        // Площадь растёт вместе с числом систем: радиус ∝ √(систем), плотность звёзд прежняя.
        // Карты увеличены под шесть империй (игрок и до пяти соперников): ~20–50 систем на каждую.
        public int StarCount => GalaxySize == 0 ? 110 : GalaxySize == 2 ? 320 : 210;
        public float Radius => GalaxySize == 0 ? 180f : GalaxySize == 2 ? 300f : 258f;
        public float MinStarDistance => GalaxySize == 2 ? 11f : 12f;
        /// <summary>Доминирование — около 55% систем галактики (110 / 210 / 320 звёзд); порог ещё нужно удерживать год.</summary>
        public int DominationTarget => GalaxySize == 0 ? 60 : GalaxySize == 2 ? 176 : 115;

        /// <summary>Уровень сложности 0…4: новичок, лёгкая, нормальная, сложная, безжалостная.</summary>
        public int Tier => Level >= 0 ? Mathf.Clamp(Level, 0, AIDifficulty.Count - 1) : Mathf.Clamp(Difficulty + 1, 0, AIDifficulty.Count - 1);

        /// <summary>Выбрать уровень и согласовать с ним грубую шкалу.</summary>
        public void SetLevel(int level)
        {
            Level = Mathf.Clamp(level, 0, AIDifficulty.Count - 1);
            Difficulty = Level <= 1 ? 0 : Level == 2 ? 1 : 2;
        }

        public static string LevelName(int tier) => DifficultyNames[Mathf.Clamp(tier, 0, DifficultyNames.Length - 1)];

        /// <summary>Множитель доходов ИИ.</summary>
        public float AIIncome => AIDifficulty.IncomeFor(Tier);
        /// <summary>Как быстро ИИ злится на сильного игрока.</summary>
        public float AIAggression => AIDifficulty.AggressionFor(Tier);
        /// <summary>Множитель стартовых ресурсов игрока.</summary>
        public float PlayerStart => AIDifficulty.PlayerStartFor(Tier);

        /// <summary>Сколько соперников реально в партии (1…максимум).</summary>
        public int RivalCount => Mathf.Clamp(Rivals, 1, AIEmpireManager.RivalCount);

        /// <summary>Сколько вольных миров появится (2…6).</summary>
        public int NeutralCount => Mathf.Clamp(Neutrals, MinNeutrals, MaxNeutrals);

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
            var kind = Mode == StartMode.LoadGame ? StellarisClone.Rendering.LoadingScreen.Kind.LoadGame
                     : Mode == StartMode.NewGame ? StellarisClone.Rendering.LoadingScreen.Kind.NewGame
                     : StellarisClone.Rendering.LoadingScreen.Kind.Menu;
            StellarisClone.Rendering.LoadingScreen.Begin(kind, DoReload);
        }

        private static void DoReload()
        {
            Time.timeScale = 1f;
            SceneManager.sceneLoaded += OnSceneReloaded;

            // Асинхронно — экран загрузки показывает реальный прогресс
            var scene = SceneManager.GetActiveScene();
            AsyncOperation op;
            if (scene.buildIndex >= 0) op = SceneManager.LoadSceneAsync(scene.buildIndex);
            else
            {
#if UNITY_EDITOR
                op = UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(scene.path, new LoadSceneParameters(LoadSceneMode.Single));
#else
                op = SceneManager.LoadSceneAsync(0);
#endif
            }
            StellarisClone.Rendering.LoadingScreen.TrackSceneLoad(op);
        }

        private static void OnSceneReloaded(Scene scene, LoadSceneMode mode)
        {
            SceneManager.sceneLoaded -= OnSceneReloaded;
            s_reloading = false;
            GameBootstrap.Reboot();
        }
    }
}
