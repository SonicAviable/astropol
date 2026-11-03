# Зона (рабочее название)

Шутер от первого лица с открытым миром в духе S.T.A.L.K.E.R. 2: Зона отчуждения, аномалии и артефакты,
мутанты, группировки, выживание, выбросы. Unity 6 (6000.6), шаблон **Universal 3D (URP)**.

## Как подключить проект к этой ветке

1. Unity Hub → Новый проект → **Universal 3D**. В правой панели **снимите «Unity Version Control»**
   (храним в Git). Название, например, `Zona`, папка `D:\`.
2. Установите Git LFS (один раз): `git lfs install`.
3. В папке проекта (`D:\Zona`):
   ```
   git init
   git remote add origin https://github.com/sonicaviable/astropol.git
   git fetch origin stalker
   git checkout -t origin/stalker
   git add -A
   git commit -m "Пустой проект Unity 6 URP"
   git push -u origin stalker
   ```
4. Сторонние ассеты импортируйте в `Assets/ThirdParty/` — эта папка не уходит в Git.

> Лимиты GitHub: файл ≤ 100 МБ, бесплатное хранилище LFS — 1 ГБ. Поэтому ассеты Asset Store в репозиторий не кладём.

## Ассеты (бесплатные)

### Окружение и мир
| Что | Где | Зачем |
|---|---|---|
| Terrain Tools, Terrain Sample Asset Pack | Package Manager / Asset Store (Unity) | Рельеф, кисти, скалы, трава |
| **Nature Starter Kit 2**, **Conifers [BOTD]** | Asset Store | Лес, деревья, кусты |
| **Quaternius** (наборы постапокалипсиса и зданий), **Kenney** (City Kit) | quaternius.com, kenney.nl (CC0) | Заброшенные дома, машины, хлам |
| **Poly Haven** — HDRI, текстуры, модели | polyhaven.com (CC0) | Небо, бетон, ржавчина, грунт |
| **ambientCG** | ambientcg.com (CC0) | PBR-материалы: асфальт, кирпич, металл |
| **Sketchfab** (фильтр «Downloadable», CC0/CC-BY) | sketchfab.com | Советские постройки, техника, мебель |

### Оружие и персонажи
| Что | Где | Зачем |
|---|---|---|
| **Starter Assets – First Person** | Asset Store (Unity) | Контроллер от первого лица |
| **Low Poly FPS Pack / FPS Weapons** (бесплатные версии) | Asset Store | Оружие с анимациями рук |
| **Mixamo** | mixamo.com | Персонажи, мутанты, анимации ИИ |
| **Quaternius «Ultimate Monsters», «Zombie»** | quaternius.com (CC0) | Мутанты |

### Эффекты и звук
| Что | Где | Зачем |
|---|---|---|
| **Unity Particle Pack**, **VFX Graph Samples** | Asset Store / Package Manager | Аномалии, выбросы, огонь, дым |
| **War FX (free)** | Asset Store | Вспышки выстрелов, попадания |
| **Sonniss GDC Game Audio Bundle** | sonniss.com | Выстрелы, окружение, ветер |
| **Freesound** (CC0) | freesound.org | Счётчик Гейгера, радио, скрипы |

### Пакеты Unity (Package Manager)
- **AI Navigation** — NavMesh для мутантов и сталкеров
- **Cinemachine** — камеры кат-сцен
- **Input System** — управление
- **ProBuilder** — быстрые блок-ауты локаций
- **Addressables** — подгрузка частей мира

## Первые этапы
1. Контроллер игрока: ходьба, бег, присед, прыжок, выносливость, наклоны.
2. Оружие: стрельба, отдача, разброс, перезарядка, износ.
3. Здоровье, радиация, кровотечение, голод; аптечки и еда.
4. Инвентарь с весом и слотами, торговец.
5. Аномалии (электра, жарка, воронка) и артефакты с детектором.
6. ИИ: мутанты (стая, засада), люди (укрытия, группировки).
7. Тестовая локация 1×1 км: болото, деревня, НИИ.
8. Выброс: предупреждение, укрытия, урон на открытой местности.
