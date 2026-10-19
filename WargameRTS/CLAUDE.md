# WargameRTS — правила для Claude

Тактическая RTS в реальном времени в духе **Wargame** и **Broken Arrow**:
современная техника, колоды юнитов, захват секторов, линия видимости,
дальности оружия, мораль и подавление. Дизайн и план — в
`docs/GAME_DESIGN.md`, установка и рабочий процесс — в `docs/SETUP_GUIDE.md`.

Репозиторий `astropol` содержит два независимых Unity-проекта:
корень — старая игра Astropolity (не трогать без явной просьбы),
`WargameRTS/` — этот проект. Все пути ниже — относительно `WargameRTS/`.

## Окружение

- Unity **6000.6.3f1**, URP, Input System (Active Input Handling = Both),
  AI Navigation (NavMeshSurface). Пакеты — `Packages/manifest.json`.
- Пользователь общается на русском — отвечать по-русски; комментарии в коде
  и идентификаторы — на английском, тексты для игрока — на русском.
- В облачной сессии Unity запустить нельзя. **Перед каждым коммитом кода**
  запускать `Tools/CompileCheck/check.sh` — компиляция против UnityEngine 2021.3
  reference assemblies + заглушки пакетов в `Tools/CompileCheck/PackageStubs.cs`
  (при использовании новых API Input System / AI Navigation — дописать заглушку,
  сверившись с документацией пакета). Unity 6-only API заглушки не покрывают —
  такие места отмечать в ответе пользователю.
- В конце ответа писать, что именно проверить в Unity.

## Как устроена песочница

Две сцены, обе содержат по одному объекту-загрузчику, остальное создаётся кодом:
`Scenes/MainMenu.unity` (`UI/MainMenu`: меню + вращающаяся карта на фоне) и
`Scenes/Battle.unity` (`Core/BattleBootstrap`). Параметры боя передаются через
статический `MatchSettings` (режим Skirmish/Sandbox, зерно, размер карты, туман);
при запуске Battle напрямую — песочница со стартовыми армиями. Переключатели
текущего боя — `BattleRules` (ИИ, бессмертие, управление обеими сторонами).
На старте боя создаются: свет, карта
(`GameMap`: рельеф из Perlin noise, леса-круги, города), NavMesh, камера,
системы и армии. Материалы — `GameMaterials` (Shader.Find; для релизной сборки
понадобится положить материалы в ассеты или в Always Included Shaders).

Подключённые системы (`Systems`): `EffectsManager`, `VisibilitySystem`
(туман войны), `ShellManager` (артиллерийские снаряды), `SelectionManager`,
`CommandInput`, `SimpleEnemyAI`, `PauseMenu`, `SandboxController` (только в
песочнице) и `BattleHud` — единственный OnGUI боя, рисует всё остальное
(миникарта, панели песочницы, пауза) в нужном порядке. Ввод между режимами
разруливает `Core/InputGate` (захват мыши режимом приказа/расстановки, меню). Юнит = `Unit` (логика) + `UnitView` (модель из
примитивов) + `NavMeshAgent`, создаётся через `UnitFactory`. Данные юнитов —
`UnitCatalog` (создаёт `UnitDefinition`/`WeaponDefinition` в коде; позже станут
ассетами в `Assets/_Project/Data/`).

## Интерфейс

- Временный HUD — IMGUI, раскладка по макету `docs/ui-reference/hud-mockup.jpg`.
  Общий набор — `UI/HudStyle` (палитра, масштаб под разрешение: вёрстка в
  виртуальных координатах высотой 1080, кнопки, переключатели, панели,
  регистрация областей, которые не пропускают клики в мир). Каждый OnGUI
  начинается с `HudStyle.Begin()`. Настройки игрока — `Core/GameSettings` (PlayerPrefs).
- Иконки — PNG с прозрачностью в `Assets/_Project/Resources/UI/{Orders,Units,Status}`,
  загружаются через `HudIcons` (`Resources.Load`). Настройки импорта задаёт
  `Editor/UiTextureImporter.cs`. Иконки приказов — белые (тонируются в коде).
- Исходные листы от Gemini — `docs/ui-reference/`. Нарезка: фон #FF00FF
  ключуется в альфу (Python + Pillow), иконка вписывается в 256×256.
- Картинки UI хранятся в Git без LFS (см. `.gitattributes`), чтобы ZIP с GitHub
  был рабочим.

## Структура

```
Assets/_Project/
  Scripts/                 // сборка WargameRTS.Runtime
    Core/        // bootstrap, команды, слои, материалы, эффекты
    Map/         // карта, леса, города, высоты
    Units/       // юниты, их данные, модели-заглушки
    Combat/      // оружие, урон, броня, линия видимости, туман войны
    Selection/   // выделение, приказы, формации
    CameraRig/   // RTS-камера
    UI/          // HUD
    AI/          // противник
    Sandbox/     // режим песочницы (спавн, время, читы)
    Editor/      // сборка WargameRTS.Editor (только редактор)
  Scenes/
  Data/ Prefabs/ Art/ Audio/   // по мере появления
Assets/Settings/   // URP pipeline assets (из шаблона Universal 3D)
Tools/CompileCheck/ // проверка компиляции без Unity
```

## Правила кода

- Namespace `WargameRTS.<Папка>`, например `WargameRTS.Combat`.
- Числа баланса — в `UnitDefinition`/`WeaponDefinition` (ScriptableObject,
  публичные поля в PascalCase), не в логике.
- В MonoBehaviour — `[SerializeField] private` поля в camelCase.
- Логику (симуляция) отделять от представления (`UnitView`, эффекты, UI) —
  задел под мультиплеер и большие армии.
- 1 юнит Unity = 1 метр; скорость в данных — км/ч, в коде — м/с.
- Ввод — только Input System (`Mouse.current`, `Keyboard.current` или
  `InputActionAsset`), не `UnityEngine.Input`.
- Статические поля сбрасывать в `[RuntimeInitializeOnLoadMethod(SubsystemRegistration)]`.
- Без `FindObjectOfType`/`GameObject.Find` в Update.

## Файлы Unity и Git

- Каждый новый файл и папка в `Assets/` создаются **вместе с `.meta`**.
  Для скрипта (формат Unity 6):
  ```
  fileFormatVersion: 2
  guid: <32 случайных hex-символа>
  ```
  Для папки:
  ```
  fileFormatVersion: 2
  guid: <32 hex>
  folderAsset: yes
  DefaultImporter:
    externalObjects: {}
    userData: 
    assetBundleName: 
    assetBundleVariant: 
  ```
  Для `.asmdef` — `AssemblyDefinitionImporter:` вместо `DefaultImporter:`.
  GUID генерировать заново (`python3 -c "import uuid;print(uuid.uuid4().hex)"`),
  никогда не копировать. При удалении/переименовании файла —
  удалять/переименовывать и его `.meta`.
- Сцены (`.unity`) и префабы (`.prefab`) руками не редактировать без явной
  необходимости; лучше создавать объекты кодом или Editor-скриптом.
- Пакеты добавлять правкой `Packages/manifest.json` и сообщать пользователю.
- Бинарные ассеты идут через Git LFS (см. `.gitattributes`).
