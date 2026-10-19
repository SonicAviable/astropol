using UnityEngine;
using UnityEngine.SceneManagement;
using WargameRTS.CameraRig;
using WargameRTS.Core;
using WargameRTS.Map;

namespace WargameRTS.UI
{
    /// <summary>
    /// Main menu scene: a generated battlefield slowly orbiting in the background,
    /// mode selection (skirmish vs AI / sandbox) with match options, settings, quit.
    /// </summary>
    public sealed class MainMenu : MonoBehaviour
    {
        enum Page
        {
            Main,
            Skirmish,
            Sandbox,
            Settings
        }

        static readonly string[] MapSizeLabels = { "1.5 км", "2 км", "3 км" };
        static readonly float[] MapSizes = { 1500f, 2000f, 3000f };

        RtsCameraController cameraController;
        Page page = Page.Main;
        string seedText;
        int mapSizeIndex = 1;
        bool fogOfWar = true;
        bool sandboxArmies;
        bool sandboxAI;
        bool loading;
        int loadFrame;

        void Awake()
        {
            Time.timeScale = 1f;
            InputGate.MenuOpen = false;
            seedText = MatchSettings.Seed.ToString();
            fogOfWar = MatchSettings.FogOfWar;

            SceneSetup.SetupLighting();
            var map = new GameObject("Map").AddComponent<GameMap>();
            map.Generate(Random.Range(1, 99999), 2000f);

            var camera = SceneSetup.SetupCamera();
            cameraController = camera.gameObject.AddComponent<RtsCameraController>();
            cameraController.LockInput = true;
            cameraController.SetView(map.OnGround(Vector3.zero), 30f, 1100f);
        }

        void Update()
        {
            if (cameraController != null)
                cameraController.Orbit(Time.unscaledDeltaTime * 2.5f);

            // Load one frame after the click so "Загрузка…" gets drawn first.
            if (loading && Time.frameCount > loadFrame)
                SceneManager.LoadScene(MatchSettings.BattleScene);
        }

        void OnGUI()
        {
            HudStyle.Begin();
            float width = HudStyle.Width;
            float height = HudStyle.Height;

            // Left shade with title and main buttons.
            HudStyle.Fill(new Rect(0f, 0f, 520f, height), new Color(0.05f, 0.06f, 0.07f, 0.82f));
            HudStyle.Fill(new Rect(520f, 0f, 2f, height), HudStyle.Amber);

            float x = 70f;
            float y = height * 0.18f;
            HudStyle.Label(new Rect(x, y, 440f, 60f), "WARGAME RTS", new GUIStyle(HudStyle.HeaderStyle) { fontSize = 52 });
            HudStyle.Fill(new Rect(x, y + 66f, 120f, 3f), HudStyle.Amber);
            HudStyle.Label(new Rect(x, y + 80f, 440f, 24f), "Тактическая стратегия в реальном времени",
                new GUIStyle(HudStyle.TextStyle) { fontSize = 17, normal = { textColor = HudStyle.Muted } });

            y += 150f;
            if (MenuButton(new Rect(x, y, 340f, 52f), "БОЙ С ИИ", page == Page.Skirmish))
                page = Page.Skirmish;
            y += 64f;
            if (MenuButton(new Rect(x, y, 340f, 52f), "ПЕСОЧНИЦА", page == Page.Sandbox))
                page = Page.Sandbox;
            y += 64f;
            if (MenuButton(new Rect(x, y, 340f, 52f), "НАСТРОЙКИ", page == Page.Settings))
                page = Page.Settings;
            y += 64f;
            if (MenuButton(new Rect(x, y, 340f, 52f), "ВЫХОД", false))
                PauseMenu.Quit();

            HudStyle.Label(new Rect(x, height - 50f, 440f, 20f), "прототип · Unity 6 · URP", HudStyle.SmallStyle);

            switch (page)
            {
                case Page.Skirmish:
                case Page.Sandbox:
                    DrawMatchPanel(new Rect(width * 0.5f + 40f, height * 0.18f, Mathf.Min(560f, width * 0.5f - 80f), 470f));
                    break;
                case Page.Settings:
                    if (SettingsPanel.Draw())
                        page = Page.Main;
                    break;
            }

            if (loading)
            {
                HudStyle.Fill(new Rect(0f, 0f, width, height), new Color(0f, 0f, 0f, 0.7f));
                HudStyle.Label(new Rect(0f, height * 0.5f - 30f, width, 60f), "Загрузка…", HudStyle.BigStyle);
            }

            HudStyle.DrawTooltip();
        }

        static bool MenuButton(Rect rect, string label, bool active) => HudStyle.Button(rect, label, active, true, null, 20);

        void DrawMatchPanel(Rect rect)
        {
            bool sandbox = page == Page.Sandbox;
            HudStyle.DrawPanel(rect, true);
            float x = rect.x + 26f;
            float y = rect.y + 22f;
            float inner = rect.width - 52f;

            HudStyle.Label(new Rect(x, y, inner, 30f), sandbox ? "ПЕСОЧНИЦА" : "БОЙ С ИИ",
                new GUIStyle(HudStyle.TitleStyle) { fontSize = 24 });
            y += 40f;
            HudStyle.Label(new Rect(x, y, inner, 60f), sandbox
                ? "Режим для тестов: ставь любую технику за любую сторону, управляй временем, включай бессмертие и командуй обеими армиями."
                : "Две армии по 19 юнитов. Противник наступает волнами. Уничтожь все силы красных.",
                new GUIStyle(HudStyle.TextStyle) { normal = { textColor = HudStyle.Muted } });
            y += 72f;

            HudStyle.Label(new Rect(x, y + 6f, 140f, 20f), "Зерно карты");
            seedText = GUI.TextField(new Rect(x + 150f, y, 140f, 30f), seedText ?? "", 6, SeedFieldStyle());
            if (HudStyle.Button(new Rect(x + 300f, y, 120f, 30f), "Случайно", false, true, null, 13))
                seedText = Random.Range(1, 99999).ToString();
            y += 44f;

            HudStyle.Label(new Rect(x, y + 6f, 140f, 20f), "Размер карты");
            mapSizeIndex = HudStyle.Segmented(new Rect(x + 150f, y, Mathf.Min(270f, inner - 150f), 30f), MapSizeLabels, mapSizeIndex, 13);
            y += 48f;

            fogOfWar = HudStyle.Toggle(new Rect(x, y, inner, 26f), "Туман войны", fogOfWar);
            y += 32f;
            if (sandbox)
            {
                sandboxArmies = HudStyle.Toggle(new Rect(x, y, inner, 26f), "Стартовые армии (иначе пустая карта)", sandboxArmies);
                y += 32f;
                sandboxAI = HudStyle.Toggle(new Rect(x, y, inner, 26f), "ИИ красных наступает", sandboxAI);
                y += 32f;
            }

            if (HudStyle.Button(new Rect(rect.xMax - 226f, rect.yMax - 70f, 200f, 46f), "НАЧАТЬ", true, !loading, null, 18))
                StartMatch(sandbox ? GameMode.Sandbox : GameMode.Skirmish);
        }

        GUIStyle seedStyle;

        GUIStyle SeedFieldStyle()
        {
            if (seedStyle != null)
                return seedStyle;
            seedStyle = new GUIStyle(GUI.skin.textField)
            {
                fontSize = 15,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = HudStyle.Text },
                focused = { textColor = HudStyle.Amber }
            };
            return seedStyle;
        }

        void StartMatch(GameMode mode)
        {
            MatchSettings.Mode = mode;
            MatchSettings.Seed = int.TryParse(seedText, out int seed) ? seed : Random.Range(1, 99999);
            MatchSettings.MapSize = MapSizes[mapSizeIndex];
            MatchSettings.FogOfWar = fogOfWar;
            MatchSettings.StartingArmies = mode == GameMode.Skirmish || sandboxArmies;
            MatchSettings.EnemyAI = mode == GameMode.Skirmish || sandboxAI;
            MatchSettings.ConfiguredFromMenu = true;

            loading = true;
            loadFrame = Time.frameCount + 1;
        }
    }
}
