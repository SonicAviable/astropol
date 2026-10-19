# Гайд: открываем WargameRTS в Unity, работаем с Git и кодим вместе с Claude

Unity-проект **уже лежит в репозитории** `SonicAviable/astropol` в папке
`WargameRTS/` — со всеми настройками (URP, Input System, слои, навигация).
Тебе нужно только скачать его и открыть в Unity. Claude пишет код в облачной
сессии (claude.ai/code) и пушит в GitHub, ты делаешь `pull` — Unity
подхватывает изменения.

```
astropol/                    ← корень репозитория (Git)
├── Assets/ Packages/ ...    ← старый проект Astropolity (не трогаем)
└── WargameRTS/              ← Unity-проект RTS (открывать в Unity Hub именно его)
    ├── Assets/_Project/     ← наш код и сцены
    ├── Assets/Settings/     ← настройки URP
    ├── Packages/            ← список пакетов Unity
    ├── ProjectSettings/     ← настройки проекта
    ├── Tools/CompileCheck/  ← проверка компиляции без Unity (для Claude)
    ├── docs/                ← этот гайд и дизайн-документ
    └── CLAUDE.md            ← правила проекта для Claude
```

> **Почему подпапка, а не отдельная ветка с другим проектом?** Ветка — это версия
> *того же* проекта. Если положить в разные ветки разные Unity-проекты, то при
> переключении веток Unity будет пересобирать всё с нуля, а слияние станет
> невозможным. Подпапка решает это: обе игры живут рядом, каждая открывается в
> Unity отдельно. Вариант с отдельным репозиторием описан в конце гайда.

---

## Быстрый старт (если хочется просто посмотреть)

1. GitHub → репозиторий `astropol` → переключи ветку (кнопка с названием ветки
   слева вверху) на `claude/eloquent-curie-wgtboe` (или `main`, если её уже влили)
   → **Code → Download ZIP** → распакуй, например, в `C:\Dev\astropol`.
2. Unity Hub → **Projects → Add → Add project from disk** → выбери папку
   `C:\Dev\astropol\WargameRTS` (именно `WargameRTS`, не корень!).
3. Открой проект в Unity **6000.6.3f1**. Первый запуск — несколько минут
   (Unity импортирует пакеты и строит кэш `Library/`).
4. Откроется сцена **Sandbox** (если нет — меню **WargameRTS → Open Sandbox
   Scene**). Нажми **Play** ▶ — или сразу меню **WargameRTS → Play Sandbox**.

ZIP подходит, чтобы посмотреть. Чтобы получать обновления от Claude одной
кнопкой, лучше клонировать репозиторий через Git — шаги ниже.

---

## Шаг 0. Влить ветку в `main` (когда всё проверишь)

Работа лежит в ветке `claude/eloquent-curie-wgtboe`. Когда убедишься, что
проект открывается, влей её в `main`, чтобы следующие сессии Claude видели код:

1. Открой https://github.com/SonicAviable/astropol
2. GitHub покажет плашку «claude/eloquent-curie-wgtboe had recent pushes» →
   **Compare & pull request** (или вкладка *Pull requests* → *New pull request*,
   base: `main`, compare: `claude/eloquent-curie-wgtboe`).
3. **Create pull request** → **Merge pull request** → **Confirm merge**.

---

## Шаг 1. Установить программы

| Что | Зачем | Где взять |
|---|---|---|
| **Unity Hub** | установка и запуск версий Unity | unity.com/download |
| **Unity Editor 6000.6.3f1** | версия проекта (та же, что у Astropolity) | Unity Hub → *Installs* → *Install Editor* (или *Archive*, если версии нет в списке) |
| **Git** | система контроля версий | git-scm.com (на Windows — «Git for Windows», все галочки по умолчанию) |
| **Git LFS** | хранение больших бинарных файлов (текстуры, модели, звук) | идёт в составе Git for Windows; на macOS — `brew install git-lfs` |
| **GitHub Desktop** *(необязательно, но удобно)* | кнопки Pull / Commit / Push без командной строки | desktop.github.com |
| **IDE для C#** | редактирование и подсказки кода | **JetBrains Rider** (бесплатен для некоммерческих проектов), **Visual Studio 2022 Community** (галочка «Game development with Unity») или **VS Code** + расширение «Unity» |

> Все, кто работает с проектом, используют **одну и ту же** версию Unity.
> Она записана в `WargameRTS/ProjectSettings/ProjectVersion.txt`. Если Unity
> предложит обновить проект на другую версию — лучше откажись и поставь нужную.

---

## Шаг 2. Настроить Git (один раз на компьютер)

Открой терминал (Windows: **Git Bash** из меню Пуск; macOS: Terminal) и выполни:

```bash
git config --global user.name  "Твоё имя"
git config --global user.email "почта, привязанная к GitHub"
git config --global core.autocrlf true      # только на Windows; на macOS/Linux: input
git lfs install                             # включает Git LFS для твоего пользователя
```

---

## Шаг 3. Склонировать репозиторий

Клонируй в папку с коротким путём **без кириллицы и пробелов**, например
`C:\Dev\astropol`.

**Через GitHub Desktop:** *File → Clone repository → URL* →
`https://github.com/SonicAviable/astropol` → Local path `C:\Dev` → **Clone**.
Потом вверху *Current branch* → выбери `claude/eloquent-curie-wgtboe`
(после шага 0 — `main`).

**Через терминал:**

```bash
cd C:/Dev
git clone https://github.com/SonicAviable/astropol.git
cd astropol
git checkout claude/eloquent-curie-wgtboe     # после шага 0: git checkout main
```

---

## Шаг 4. Открыть проект в Unity

1. Unity Hub → **Projects → Add → Add project from disk** →
   `C:\Dev\astropol\WargameRTS`.
2. Если Hub пишет, что версия не установлена, — нажми на предупреждение и
   установи **6000.6.3f1**.
3. Открой проект. Первый импорт идёт несколько минут.
4. Откроется сцена `Assets/_Project/Scenes/Sandbox.unity`. Нажми **Play**.
   При старте карта, леса, города и навигация генерируются кодом — первые
   1–3 секунды игра может «подвиснуть», это нормально.
5. Edit → Preferences → **External Tools** → выбери свою IDE и нажми
   **Regenerate project files**.

Настройки для Git уже выставлены в проекте: *Version Control = Visible Meta
Files*, *Asset Serialization = Force Text*, *Active Input Handling = Both*.

### Что есть в песочнице

- Карта 2×2 км: холмы, леса (скрывают и защищают пехоту, глушат видимость),
  города (здания — препятствия).
- Две армии по 17 юнитов: танки, БМП, пехота, разведка, ПТРК.
- Туман войны с линией видимости, броня по сторонам (лоб/борт/корма),
  точность падает в движении и на дальней дистанции.
- Противник (красные) через ~25 секунд начинает наступать волнами.

### Управление

| Действие | Клавиши |
|---|---|
| Камера | WASD / стрелки / край экрана; колесо — зум; Q/E или средняя кнопка — поворот; Shift — быстрее |
| Выделение | ЛКМ, рамка ЛКМ, Shift — добавить, двойной клик — все такого типа на экране, Esc — снять |
| Группы | Ctrl+1..9 — назначить, 1..9 — выбрать, дважды — камера к группе |
| Движение | ПКМ по земле — строем; ПКМ с протяжкой — развернуть линию (фронтом от протяжки) |
| Атака | ПКМ по видимому врагу; Ctrl+ПКМ — движение с атакой; X — стоп |
| Панель приказов (справа внизу) | Кнопка → ЛКМ по карте (Shift — повторять, ПКМ/Esc — отмена); X — стоп, B — отход, H — режим огня |
| Прочее | F — туман войны вкл/выкл, F1 — подсказка, R — рестарт после конца боя |

---

## Шаг 5. Твои изменения → коммит и пуш

Когда ты что-то поменял в Unity (сцену, настройки, добавил ассеты):

1. **File → Save Project**.
2. GitHub Desktop: слева список изменений → проверь, что **нет** файлов из
   `WargameRTS/Library/`, `Temp/`, `Logs/`, `*.csproj` → Summary → **Commit** → **Push origin**.
3. Терминал: `git add WargameRTS && git commit -m "что сделал" && git push`.

> О `.meta`-файлах: Unity создаёт `.meta` рядом с каждым файлом и папкой в
> `Assets/`. В нём хранится GUID, по которому сцены и префабы ссылаются на
> ассет. **Всегда коммить `.meta` вместе с файлом** и никогда не удаляй их
> вручную — иначе ссылки в сценах сломаются («Missing script»).

Проверка LFS: когда добавишь картинки/модели, `git lfs ls-files` должен их
показать. Если пусто — ты не выполнил `git lfs install` до коммита. Исправление
(историю не переписывает):

```bash
git lfs install
git add --renormalize WargameRTS
git commit -m "WargameRTS: перенос бинарных файлов в LFS"
git push
```

> GitHub даёт ограниченную бесплатную квоту на LFS (хранилище и трафик) —
> текущие лимиты видны в *GitHub → Settings → Billing*. Тяжёлые исходники
> (`.blend`, `.psd` на сотни МБ) лучше держать вне репозитория.

---

## Шаг 6. Подключить Claude (облачная сессия на claude.ai/code)

1. Открой **claude.ai/code** и начни новую сессию.
2. Выбери репозиторий **SonicAviable/astropol** (если его нет в списке — дай
   доступ GitHub-приложению Claude: github.com/apps/claude → *Configure* →
   отметь репозиторий).
3. В первом сообщении пиши, с чем работаем, например:
   > Работаем над проектом WargameRTS (папка `WargameRTS/`). Прочитай
   > `WargameRTS/CLAUDE.md` и `WargameRTS/docs/GAME_DESIGN.md`. Задача: добавь
   > сектора на карте и очки командования (веха M6).
4. Claude создаст/изменит файлы, закоммитит и запушит в ветку вида
   `claude/<название>`. В конце он напишет, что сделано и что тебе проверить в
   Unity.

> Каждая облачная сессия работает в своей ветке `claude/...`. Это нормально:
> ветка — это «черновик», который ты проверяешь и вливаешь в `main`.

---

## Шаг 7. Ежедневный цикл работы

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

## Шаг 8. Как давать задачи Claude, чтобы было эффективно

- Одна сессия — одна фича (камера, выделение юнитов, туман войны…).
- Описывай **ожидаемое поведение**, а не реализацию: «танк стреляет только если
  цель в прямой видимости и в радиусе 2 км».
- Прикладывай **ошибки целиком** (со стеком) и скриншоты.
- Если Claude создал компонент, который надо повесить на объект, он напишет
  шаги вида: *«Создай пустой GameObject `RTSCamera`, добавь компонент
  `RtsCameraController`, перетащи Main Camera в поле `Camera`»*.
- Claude в облаке **не может запустить Unity**. Он проверяет, что код
  компилируется (`WargameRTS/Tools/CompileCheck/check.sh`), но как игра
  *ведёт себя* — проверяешь ты. Поэтому идём короткими итерациями:
  сделал → проверил → дальше.

### Альтернатива: Claude Code локально (продвинутый вариант)

Можно установить Claude Code прямо на компьютер (code.claude.com → установка
CLI) и запускать `claude` в папке `C:\Dev\astropol`. Плюсы: Claude сам читает
лог Unity (`%LOCALAPPDATA%\Unity\Editor\Editor.log` на Windows,
`~/Library/Logs/Unity/Editor.log` на macOS) и видит ошибки компиляции без
копипасты; нет шагов с pull — правки появляются сразу в твоей папке. Дополнительно
существуют MCP-серверы для Unity (ищи «unity mcp» на GitHub), через которые
Claude может управлять редактором. Начать проще с облачного варианта.

---

## Шаг 9 (необязательный). Smart Merge для сцен и префабов

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
| Конфликт в `.unity`/`.prefab` при merge | Выбери одну версию целиком (*Use mine/theirs*) и повтори свои изменения руками, либо настрой Smart Merge (шаг 9). На будущее — не правьте одну сцену одновременно. |
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
