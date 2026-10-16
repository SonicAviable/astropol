# WargameRTS — правила для Claude

Тактическая RTS в реальном времени в духе **Wargame** и **Broken Arrow**:
современная техника, колоды юнитов, захват секторов, линия видимости,
дальности оружия, мораль и подавление. Дизайн и план — в
`docs/GAME_DESIGN.md`, установка и рабочий процесс — в `docs/SETUP_GUIDE.md`.

Репозиторий `astropol` содержит два независимых Unity-проекта:
корень — старая игра Astropolity (не трогать без явной просьбы),
`WargameRTS/` — этот проект. Все пути ниже — относительно `WargameRTS/`.

## Окружение

- Unity **6000.6.3f1**, URP, новый Input System (`com.unity.inputsystem`).
- Пользователь общается на русском — отвечать по-русски; комментарии в коде
  и имена — на английском.
- В облачной сессии Unity запустить нельзя: компилирует и тестирует
  пользователь. Поэтому изменения делаем небольшими, а в конце ответа пишем,
  что именно проверить в Unity и какие объекты/компоненты настроить руками.

## Структура

```
Assets/_Project/
  Scripts/
    Core/        // игровой цикл, тик симуляции, сервисы, события
    Units/       // юниты, их данные и компоненты
    Combat/      // оружие, баллистика, броня, урон, подавление
    Orders/      // приказы (move, attack, unload...) и их очередь
    Selection/   // выделение, группы, формации
    Map/         // секторы, местность, укрытия, линия видимости
    AI/          // противник
    CameraRig/   // RTS-камера
    UI/          // HUD, карточки юнитов, колода
    Editor/      // Editor-утилиты (отдельная сборка)
  Data/          // ScriptableObject-ассеты (юниты, оружие, колоды)
  Prefabs/
  Scenes/
  Art/ Audio/
Assets/ThirdParty/   // ассеты из Asset Store, не редактируем
```

Сборки (asmdef): `WargameRTS.Runtime` (Scripts), `WargameRTS.Editor`
(Scripts/Editor, только Editor), `WargameRTS.Tests` (Tests/, EditMode/PlayMode).

## Правила кода

- Namespace `WargameRTS.<Папка>`, например `WargameRTS.Combat`.
- Данные юнитов/оружия — ScriptableObject (`UnitDefinition`, `WeaponDefinition`),
  без хардкода чисел в MonoBehaviour.
- Отделять **симуляцию** (логика, фиксированный тик) от **представления**
  (меши, анимации, эффекты, UI) — это нужно для будущего мультиплеера и для
  тысяч юнитов без лагов.
- Единицы измерения: 1 юнит Unity = 1 метр; дальности оружия в метрах,
  скорости в км/ч в данных и в м/с в коде (конвертация в одном месте).
- Ввод — только через Input System (`InputActionAsset`), не `UnityEngine.Input`.
- Без `FindObjectOfType`/`GameObject.Find` в Update; ссылки — через
  инспектор или сервис-регистр.
- `[SerializeField] private` вместо публичных полей.

## Файлы Unity и Git

- Каждый новый файл и папка в `Assets/` создаются **вместе с `.meta`**.
  Формат для скрипта (Unity 6):
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
  GUID генерировать заново (`python3 -c "import uuid;print(uuid.uuid4().hex)"`),
  никогда не копировать чужой. При удалении/переименовании файла —
  удалять/переименовывать и его `.meta`.
- Сцены (`.unity`) и префабы (`.prefab`) руками не редактировать без явной
  просьбы. Вместо этого — описать шаги для пользователя или сделать
  Editor-скрипт (пункт меню `WargameRTS/...`), который соберёт объект.
- Пакеты добавлять правкой `Packages/manifest.json` и сообщать об этом
  пользователю (Unity сам обновит `packages-lock.json` — его надо закоммитить).
- Бинарные ассеты идут через Git LFS (см. `.gitattributes`).
