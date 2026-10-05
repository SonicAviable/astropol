using UnityEngine;
using StellarisClone.Cam;
using StellarisClone.Core;
using StellarisClone.Generation;
using StellarisClone.Rendering;

namespace StellarisClone.Core
{
    public static class GameBootstrap
    {
        /// <summary>Повторная инициализация после перезагрузки сцены («Новая игра»).</summary>
        public static void Reboot()
        {
            Time.timeScale = 1f;
            Boot();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            Debug.Log($"<color=#5F5>[Bootstrap]</color> === Старт инициализации ({GameSession.Mode}) ===");
            ShaderCache.Diagnose();

            // Чёрный экран → мягкое проявление (и при запуске, и после перехода между партиями)
            SceneFader.RevealFromBlack(GameSession.Mode == GameSession.StartMode.MainMenu ? 1.4f : 0.8f);

            EnsureCamera();
            EnsureCoreManagers();
            GameSettings.ApplyAll(includeDisplay: true);
            EnsureGalaxy();
            EnsureGameManagers();
            EnsureUI();
            EnsureExtras();

            new GameObject("[Save] Runtime").AddComponent<SaveRuntime>();
            if (GameSession.IsLoading)
                new GameObject("[Save] Loader").AddComponent<SaveLoader>();

            Debug.Log("<color=#5F5>[Bootstrap]</color> === Все менеджеры готовы ===");
        }

        /// <summary>Те же параметры, что у генерации партии (для предпросмотра в меню «Новая игра»).</summary>
        public static void ConfigureGenerator(GalaxyGenerator gen, NewGameSettings s)
        {
            gen.Configure(s.StarCount, s.Radius, s.MinStarDistance);
            TrySetField(gen, "maxConnectionDistance", 30f);
            TrySetField(gen, "maxConnectionsPerStar", 4);
        }

        private static void EnsureCamera()
        {
            var cam = Object.FindFirstObjectByType<Camera>();
            if (cam == null)
            {
                var camGo = new GameObject("Main Camera");
                camGo.tag = "MainCamera";
                cam = camGo.AddComponent<Camera>();
                camGo.AddComponent<AudioListener>();
                camGo.transform.position = new Vector3(0f, 80f, -80f);
                camGo.transform.rotation = Quaternion.Euler(45f, 0f, 0f);
            }
            if (cam.GetComponent<StrategyCameraController>() == null)
                cam.gameObject.AddComponent<StrategyCameraController>();
        }

        private static void EnsureCoreManagers()
        {
            EnsureComp<TimeManager>("[Managers] TimeManager");
            EnsureComp<EconomyManager>("[Managers] EconomyManager");
            EnsureComp<TechnologyManager>("[Managers] TechnologyManager");
            EnsureComp<MusicManager>("[Managers] MusicManager");
            EnsureComp<SFXManager>("[Managers] SFXManager");
        }

        private static void EnsureGalaxy()
        {
            var existing = Object.FindFirstObjectByType<GalaxyGenerator>();
            if (existing != null) return;

            var go = new GameObject("[Managers] Galaxy");
            var gen = go.AddComponent<GalaxyGenerator>();

            if (GameSession.IsLoading)
            {
                ConfigureGenerator(gen, GameSession.Settings);   // радиус нужен камере и миникарте
                gen.LoadSystems(SaveSystem.BuildSystems(GameSession.PendingLoad.State));
            }
            else
            {
                var s = GameSession.Settings;
                if (s.Seed == 0) s.Seed = NewGameSettings.RandomSeed();
                ConfigureGenerator(gen, s);
                // Сид определяет форму галактики (совпадает с предпросмотром в меню)
                Random.InitState(s.Seed);
                gen.GenerateGalaxy();
                Random.InitState(System.Environment.TickCount);
            }
            go.AddComponent<GalaxyView>();
        }

        private static void EnsureGameManagers()
        {
            EnsureComp<ShipDesignManager>("[Managers] ShipDesignManager");
            EnsureComp<FleetManager>("[Managers] FleetManager");
            EnsureComp<AIEmpireManager>("[Managers] AIEmpireManager");
            EnsureComp<CombatManager>("[Managers] CombatManager");
            EnsureComp<SystemViewManager>("[Managers] SystemViewManager");
            EnsureComp<SiegeManager>("[Managers] SiegeManager");
            EnsureComp<ConstructionManager>("[Managers] ConstructionManager");
            EnsureComp<VictoryManager>("[Managers] VictoryManager");
        }

        private static void EnsureUI()
        {
            EnsureComp<TutorialManager>("[Managers] TutorialManager");
            EnsureComp<UIManager>("[Managers] UIManager");
        }

        private static void EnsureExtras()
        {
            EnsureComp<GalaxyMinimap>("[UI] GalaxyMinimap");
            EnsureComp<TooltipSystem>("[UI] TooltipSystem");
            EnsureComp<SystemProgressRing>("[UI] SystemProgressRing");
            EnsureComp<SystemNameplateHover>("[UI] SystemNameplateHover");
            EnsureComp<AnomalyEventSystem>("[Managers] AnomalyEventSystem");
            EnsureComp<PlanetHoloStudio>("[UI] PlanetHoloStudio");
            EnsureComp<ShipDesignerModal>("[UI] ShipDesignerModal");
            EnsureComp<PlanetOverviewModal>("[UI] PlanetOverviewModal");
            EnsureComp<NotificationCenter>("[UI] NotificationCenter");
            EnsureComp<FleetSelectionController>("[UI] FleetSelectionController");
            EnsureComp<FleetRouteOverlay>("[UI] FleetRouteOverlay");
            EnsureComp<FleetCommandHUD>("[UI] FleetCommandHUD");
        }

        private static void EnsureComp<T>(string name) where T : Component
        {
            if (Object.FindFirstObjectByType<T>() != null) return;
            var go = new GameObject(name);
            go.AddComponent<T>();
        }

        private static void TrySetField<T>(object target, string fieldName, T value)
        {
            if (target == null) return;
            var type = target.GetType();
            var field = type.GetField(fieldName,
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Public);
            if (field == null) return;

            try
            {
                if (field.FieldType == typeof(T))
                    field.SetValue(target, value);
                else
                    field.SetValue(target, System.Convert.ChangeType(value, field.FieldType));
            }
            catch { }
        }
    }
}