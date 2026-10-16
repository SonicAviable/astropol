# Гайд: создаём Unity-проект WargameRTS, подключаем Git и кодим вместе с Claude

Этот гайд — пошаговая инструкция «с нуля до первого коммита». Цель: Unity-проект
лежит в репозитории `SonicAviable/astropol` в папке `WargameRTS/`, ты работаешь
в Unity у себя на компьютере, а Claude пишет код в облачной сессии
(claude.ai/code) и пушит его в GitHub. Ты делаешь `pull` — Unity подхватывает код.

```
astropol/                    ← корень репозитория (Git)
├── Assets/ Packages/ ...    ← старый проект Astropolity (не трогаем)
└── WargameRTS/              ← НОВЫЙ Unity-проект (RTS в духе Wargame / Broken Arrow)
    ├── .gitignore           ← уже готов
    ├── .gitattributes       ← уже готов (Git LFS для текстур, моделей, звука)
    ├── CLAUDE.md            ← правила проекта для Claude
    ├── docs/                ← этот гайд и дизайн-документ
    ├── Assets/              ← появится на шаге 5
    ├── Packages/            ← появится на шаге 5
    └── ProjectSettings/     ← появится на шаге 5
```

> **Почему подпапка, а не отдельная ветка с другим проектом?** Ветка — это версия
> *того же* проекта. Если положить в разные ветки разные Unity-проекты, то при
> переключении веток Unity будет пересобирать всё с нуля, а слияние станет
> невозможным. Подпапка решает это: обе игры живут рядом, каждая открывается в
> Unity отдельно. Вариант с отдельным репозиторием описан в конце гайда.

---

## Шаг 0. Влить эту ветку в `main`

Гайд и заготовки лежат в ветке `claude/eloquent-curie-wgtboe`. Чтобы следующие
сессии Claude и твой компьютер видели папку `WargameRTS/`, влей её в `main`:

1. Открой https://github.com/SonicAviable/astropol
2. GitHub покажет жёлтую плашку «claude/eloquent-curie-wgtboe had recent pushes» →
   **Compare & pull request** (или вкладка *Pull requests* → *New pull request*,
   base: `main`, compare: `claude/eloquent-curie-wgtboe`).
3. **Create pull request** → **Merge pull request** → **Confirm merge**.

Дальше в гайде считаем, что основная ветка — `main`.

---

## Шаг 1. Установить программы

| Что | Зачем | Где взять |
|---|---|---|
| **Unity Hub** | установка и запуск версий Unity | unity.com/download |
| **Unity Editor 6000.6.3f1** | та же версия, что у Astropolity (не придётся качать вторую) | Unity Hub → *Installs* → *Install Editor* (или *Archive*, если версии нет в списке) |
| **Git** | система контроля версий | git-scm.com (на Windows — «Git for Windows», все галочки по умолчанию) |
| **Git LFS** | хранение больших бинарных файлов (текстуры, модели, звук) | идёт в составе Git for Windows; на macOS — `brew install git-lfs` |
| **GitHub Desktop** *(необязательно, но удобно)* | кнопки Pull / Commit / Push без командной строки | desktop.github.com |
| **IDE для C#** | редактирование и подсказки кода | **JetBrains Rider** (бесплатен для некоммерческих проектов), **Visual Studio 2022 Community** (галочка «Game development with Unity») или **VS Code** + расширение «Unity» |

При установке Unity Editor в Hub отметь модули:
- **Microsoft Visual Studio Community** — если у тебя ещё нет IDE;
- **Windows Build Support (IL2CPP)** — пригодится позже для релизных сборок;
- **Documentation** — по желанию.

> Важно: все, кто работает с проектом, используют **одну и ту же** версию Unity.
> Версия записана в `WargameRTS/ProjectSettings/ProjectVersion.txt`. Если решишь
> перейти на другую — скажи Claude, чтобы обновил `CLAUDE.md`.

---

## Шаг 2. Настроить Git (один раз на компьютер)

Открой терминал (Windows: **Git Bash** из меню Пуск; macOS: Terminal) и выполни:

```bash
git config --global user.name  "Твоё имя"
git config --global user.email "почта, привязанная к GitHub"
git config --global core.autocrlf true      # только на Windows; на macOS/Linux: input
git config --global init.defaultBranch main
git lfs install                             # включает Git LFS для твоего пользователя
```

Проверка: `git --version` и `git lfs version` должны напечатать версии.

---

## Шаг 3. Склонировать репозиторий

Клонируй репозиторий **в отдельную папку** с коротким путём без кириллицы и
пробелов (Unity и некоторые инструменты не любят длинные/русские пути).
Например `C:\Dev\astropol` или `~/Dev/astropol`.

**Через GitHub Desktop:** *File → Clone repository → URL* →
`https://github.com/SonicAviable/astropol` → Local path `C:\Dev` → **Clone**.
Потом вверху *Current branch* → выбери `main`.

**Через терминал:**

```bash
cd C:/Dev                         # или ~/Dev
git clone https://github.com/SonicAviable/astropol.git
cd astropol
git checkout main
git pull
```

Убедись, что в `C:\Dev\astropol\WargameRTS\` есть `.gitignore`,
`.gitattributes`, `CLAUDE.md` и папка `docs`. Если папки нет — ты не влил
ветку (шаг 0) или не сделал `git pull`.

> Если у тебя уже есть клон astropol для Astropolity — можно использовать его,
> просто сделай `git pull` на `main`.

---

## Шаг 4. Создать Unity-проект через Unity Hub

Unity Hub не умеет создавать проект в уже существующей непустой папке, поэтому
создаём его во **временном** месте, а потом переносим (шаг 5).

1. Unity Hub → **Projects** → **New project**.
2. Сверху выбери версию **6000.6.3f1**.
3. Шаблон: **Universal 3D** (URP). Если шаблон не скачан — нажми *Download template*.
   - URP — оптимальный выбор для RTS: хорошая производительность при сотнях
     юнитов, поддержка Shader Graph, VFX, декалей.
4. **Project name:** `WargameRTS`
5. **Location:** временная папка, например `C:\Temp`
   (получится `C:\Temp\WargameRTS`).
6. **Сними** галочки:
   - **Use Unity Version Control** — мы используем Git, а не Plastic/UVCS;
   - **Connect to Unity Cloud** — можно оставить, если нужен Unity-аккаунт для
     сервисов, но для Git не требуется.
7. **Create project**. Дождись, пока откроется редактор.
8. Сразу закрой Unity (*File → Exit*).

---

## Шаг 5. Перенести проект в репозиторий

Из `C:\Temp\WargameRTS\` скопируй **только три папки**:

```
Assets/
Packages/
ProjectSettings/
```

в `C:\Dev\astropol\WargameRTS\` (рядом с `.gitignore` и `CLAUDE.md`).

**Не копируй** `Library/`, `Temp/`, `Logs/`, `UserSettings/`, `obj/`, `*.sln`,
`*.csproj` — это кэш, который Unity пересоздаёт сам, и он огромный.

После копирования временную папку `C:\Temp\WargameRTS` можно удалить.

Теперь в Unity Hub: **Projects → Add → Add project from disk** → выбери
`C:\Dev\astropol\WargameRTS`. Открой проект — первый запуск займёт несколько
минут (Unity заново строит `Library/`).

---

## Шаг 6. Проверить настройки проекта для Git

В открытом Unity:

1. **Edit → Project Settings → Version Control**
   - *Mode*: **Visible Meta Files**
2. **Edit → Project Settings → Editor**
   - *Asset Serialization → Mode*: **Force Text**
     (сцены и префабы сохраняются как текст — Git видит изменения и может их сливать)
3. **Edit → Project Settings → Player → Other Settings**
   - *Active Input Handling*: **Input System Package (New)**
     (если Unity спросит про перезапуск — соглашайся)
   - *Company Name / Product Name* — по вкусу.
4. **Edit → Preferences → External Tools**
   - *External Script Editor*: выбери свою IDE (Rider / Visual Studio / VS Code)
   - нажми **Regenerate project files**
5. *Необязательно:* **Window → Package Manager → In Project** → удали пакет
   **Version Control** (`com.unity.collab-proxy`) — он не нужен при работе через Git
   и иногда мешает.
6. *Необязательно:* удали стартовый мусор шаблона — папку `Assets/TutorialInfo`
   и файл `Assets/Readme.asset`.
7. **File → Save Project**, потом закрой Unity (чтобы все файлы точно записались).

> О `.meta`-файлах: Unity создаёт `.meta` рядом с каждым файлом и папкой в
> `Assets/`. В нём хранится GUID, по которому сцены и префабы ссылаются на
> ассет. **Всегда коммить `.meta` вместе с файлом** и никогда не удаляй их
> вручную — иначе ссылки в сценах сломаются («Missing script»).

---

## Шаг 7. Первый коммит и пуш

### Через GitHub Desktop
1. Слева появится список изменений — должны быть файлы из
   `WargameRTS/Assets`, `WargameRTS/Packages`, `WargameRTS/ProjectSettings`.
2. **Проверь, что в списке НЕТ** `WargameRTS/Library/...`, `Temp/`, `Logs/`,
   `*.csproj`. Если есть — `.gitignore` не на месте (должен лежать в
   `WargameRTS/`, а не в `Assets/`).
3. Внизу: Summary → `WargameRTS: пустой Unity-проект (URP)` → **Commit to main**.
4. **Push origin**.

### Через терминал
```bash
cd C:/Dev/astropol
git status                       # проверь, что Library/ и Temp/ НЕ в списке
git add WargameRTS
git commit -m "WargameRTS: пустой Unity-проект (URP)"
git push origin main
```

Проверка LFS: `git lfs ls-files` должен показать картинки/модели из шаблона
(например `.png`). Если список пустой, а картинки в проекте есть — ты не
выполнил `git lfs install` до коммита. Исправление (историю не переписывает):

```bash
git lfs install
git add --renormalize WargameRTS
git commit -m "WargameRTS: перенос бинарных файлов в LFS"
git push origin main
```

> GitHub даёт ограниченную бесплатную квоту на LFS (хранилище и трафик) —
> текущие лимиты видны в *GitHub → Settings → Billing*. Для старта хватает с
> запасом; тяжёлые исходники (`.blend`, `.psd` на сотни МБ) лучше держать вне
> репозитория.

---

## Шаг 8. Подключить Claude (облачная сессия на claude.ai/code)

1. Открой **claude.ai/code** и начни новую сессию.
2. Выбери репозиторий **SonicAviable/astropol** (если его нет в списке — дай
   доступ GitHub-приложению Claude: github.com/apps/claude → *Configure* →
   отметь репозиторий).
3. В первом сообщении пиши, с чем работаем, например:
   > Работаем над проектом WargameRTS (папка `WargameRTS/`). Прочитай
   > `WargameRTS/CLAUDE.md` и `WargameRTS/docs/GAME_DESIGN.md`. Задача: сделай
   > RTS-камеру (WASD, зум колесом, поворот на средней кнопке).
4. Claude создаст/изменит файлы, закоммитит и запушит в ветку вида
   `claude/<название>`. В конце он напишет, что сделано и что тебе проверить в
   Unity.

> Каждая облачная сессия работает в своей ветке `claude/...`. Это нормально:
> ветка — это «черновик», который ты проверяешь и вливаешь в `main`.

---

## Шаг 9. Ежедневный цикл работы

```
 ┌─────────────┐  1. задача   ┌──────────┐  2. push в claude/...  ┌────────┐
 │    Ты       │ ───────────▶ │  Claude  │ ─────────────────────▶ │ GitHub │
 │  (Unity)    │              │ (облако) │                        │        │
 └─────────────┘              └──────────┘                        └────────┘
       ▲   │ 5. ошибки/скрины        ▲                                 │
       │   └─────────────────────────┘                                 │
       │            3. checkout ветки claude/... + pull                │
       └───────────────────────────────────────────────────────────────┘
                    4. проверка в Unity → merge в main
```

1. **Перед новой задачей** закоммить и запушь свои изменения в Unity (сцены,
   префабы, настройки), чтобы Claude их видел.
2. **Дай задачу** Claude в сессии. Чем конкретнее — тем лучше («юниты должны
   выделяться рамкой ЛКМ, Shift добавляет к выделению»).
3. **Забери код:**
   - GitHub Desktop: *Fetch origin* → *Current branch* → выбери ветку `claude/...`
     → *Pull*;
   - терминал: `git fetch origin && git checkout claude/<имя> && git pull`.
4. **Переключись в Unity** — он сам перекомпилирует скрипты (внизу справа
   крутится индикатор). Открой **Console** (*Window → General → Console*).
5. **Если есть ошибки** — скопируй текст из Console (клик по ошибке → текст
   снизу, Ctrl+C) и вставь Claude в ту же сессию. Скриншоты тоже можно
   прикладывать прямо в чат. Claude исправит и снова запушит — повтори шаг 3
   (просто *Pull*).
6. **Всё работает** → влей ветку в `main` через Pull Request на GitHub (как в
   шаге 0) или попроси Claude создать PR. Потом у себя: переключись на `main`
   и сделай *Pull*.

### Правила, чтобы не ловить конфликты

- **Одну сцену/префаб в один момент меняет кто-то один.** Claude по умолчанию
  пишет код (`.cs`) и не правит сцены руками; если нужна настройка объектов —
  он либо опишет, что накликать в Unity, либо сделает Editor-скрипт
  (пункт меню), который соберёт префаб/сцену автоматически.
- **Новые скрипты Claude создаёт вместе с `.meta`** (со своим GUID) — так
  ссылки не разъедутся между твоей машиной и облаком.
- **Unity открыт во время `pull` — нормально**, но если меняется сцена, которая
  сейчас открыта, Unity предложит её перезагрузить — соглашайся (*Reload*).
- **Не держи несохранённых изменений в сцене**, когда делаешь pull/checkout.
- Коммить маленькими порциями с понятными сообщениями.

---

## Шаг 10. Как давать задачи Claude, чтобы было эффективно

- Одна сессия — одна фича (камера, выделение юнитов, туман войны…).
- Описывай **ожидаемое поведение**, а не реализацию: «танк стреляет только если
  цель в прямой видимости и в радиусе 2 км».
- Прикладывай **ошибки целиком** (со стеком) и скриншоты.
- Если Claude создал компонент, который надо повесить на объект, он напишет
  шаги вида: *«Создай пустой GameObject `RTSCamera`, добавь компонент
  `RtsCameraController`, перетащи Main Camera в поле `Camera`»*.
- Claude в облаке **не может запустить Unity** — компилятором и тестировщиком
  выступаешь ты. Поэтому идём короткими итерациями: сделал → проверил → дальше.

### Альтернатива: Claude Code локально (продвинутый вариант)

Можно установить Claude Code прямо на компьютер (code.claude.com → установка
CLI) и запускать `claude` в папке `C:\Dev\astropol`. Плюсы: Claude сам читает
лог Unity (`%LOCALAPPDATA%\Unity\Editor\Editor.log` на Windows,
`~/Library/Logs/Unity/Editor.log` на macOS) и видит ошибки компиляции без
копипасты; нет шагов с pull — правки появляются сразу в твоей папке. Дополнительно
существуют MCP-серверы для Unity (ищи «unity mcp» на GitHub), через которые
Claude может управлять редактором. Начать проще с облачного варианта.

---

## Шаг 11 (необязательный). Smart Merge для сцен и префабов

Unity поставляется с утилитой **UnityYAMLMerge**, которая умно сливает
конфликтующие сцены/префабы. `.gitattributes` уже размечает эти файлы как
`merge=unityyamlmerge`; осталось сказать Git, где утилита. Выполни (путь
подставь свой):

**Windows:**
```bash
git config --global merge.unityyamlmerge.name "Unity SmartMerge"
git config --global merge.unityyamlmerge.driver "'C:/Program Files/Unity/Hub/Editor/6000.6.3f1/Editor/Data/Tools/UnityYAMLMerge.exe' merge -p %O %B %A %A"
git config --global merge.unityyamlmerge.recursive binary
```

**macOS:**
```bash
git config --global merge.unityyamlmerge.name "Unity SmartMerge"
git config --global merge.unityyamlmerge.driver "'/Applications/Unity/Hub/Editor/6000.6.3f1/Unity.app/Contents/Tools/UnityYAMLMerge' merge -p %O %B %A %A"
git config --global merge.unityyamlmerge.recursive binary
```

Если не настроишь — ничего не сломается, Git будет сливать эти файлы обычным
способом.

---

## Частые проблемы

| Симптом | Причина и решение |
|---|---|
| В изменениях тысячи файлов из `Library/` | `.gitignore` не в той папке. Он должен лежать в `WargameRTS/.gitignore`, рядом с `Assets/`. |
| «Missing script» на объектах после pull | Кто-то закоммитил `.cs` без `.meta` (или наоборот). Найди файл без пары и закоммить `.meta`. |
| Картинки после клона «битые», весят 130 байт | Не установлен/не включён Git LFS. Выполни `git lfs install`, затем `git lfs pull`. |
| Unity пишет «Project was created with a different version» | Открываешь не той версией Unity. Поставь версию из `ProjectSettings/ProjectVersion.txt`. |
| IDE не подсказывает Unity API, всё красное | *Preferences → External Tools → Regenerate project files*, перезапусти IDE. |
| Конфликт в `.unity`/`.prefab` при merge | Выбери одну версию целиком (*Use mine/theirs*) и повтори свои изменения руками, либо настрой Smart Merge (шаг 11). На будущее — не правьте одну сцену одновременно. |
| `error: failed to push some refs` | На GitHub есть новые коммиты. Сначала *Pull*, потом *Push*. |

---

## Приложение: вариант с отдельным репозиторием

Если позже захочется вынести RTS в свой репозиторий (например
`SonicAviable/wargame-rts`):

1. На GitHub: **New repository** → имя → *Private* → без README.
2. Скопируй содержимое `WargameRTS/` (вместе с `.gitignore`, `.gitattributes`,
   `CLAUDE.md`, `docs/`, но без `Library/`) в новую пустую папку.
3. В этой папке: `git init && git lfs install && git add . && git commit -m "init"`,
   затем `git remote add origin https://github.com/SonicAviable/wargame-rts.git`
   и `git push -u origin main`.
4. Дай GitHub-приложению Claude доступ к новому репозиторию
   (github.com/apps/claude → *Configure*) и выбирай его при старте сессий.

Историю коммитов папки можно сохранить через `git subtree split` — попроси
Claude, если понадобится.
