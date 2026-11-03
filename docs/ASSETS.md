# Бесплатные ассеты под сценарий «Предел» (Unity 6, URP)

Перед импортом проверяйте на странице: цена **Free**, поддержка **URP**, лицензия (Standard Unity Asset Store EULA).
Если ассет только Built-in — материалы конвертируются через *Window → Rendering → Render Pipeline Converter*.
Сторонние ассеты класть в `Assets/ThirdParty/<Имя>/`.

## 0. Пакеты Unity (Package Manager, бесплатно)
| Пакет | Зачем |
|---|---|
| Input System | управление |
| Cinemachine | камеры катсцен |
| AI Navigation | NavMesh для ИИ |
| ProBuilder + Polybrush | блокаут уровней, шахта, бункеры |
| Terrain Tools | рельеф Урала, карьер |
| Timeline | катсцены |
| Addressables | стриминг регионов |
| TextMeshPro / UI Toolkit | UI, КПК |
| Splines | ж/д пути, дороги |

## 1. Asset Store — ядро (обязательно)
| Ассет | Издатель | Для чего (по сценарию) |
|---|---|---|
| **Starter Assets – FirstPerson** | Unity | база FPS-контроллера (§8.1) |
| **Low Poly Shooter Pack – Free Sample** | Infima Games | руки + оружие + анимации стрельбы, временно до своих стволов (§8.2) |
| **War FX** | Jean Moreno (JMO) | вспышки выстрелов, попадания, взрывы (М5, М12) |
| **Unity Particle Pack** | Unity | огонь/искры/дым — аномалии «Огневушка», Огневик |
| **DOTween (HOTween v2)** | Demigiant | анимация UI, КПК |
| **Ink Integration for Unity** | Inkle | диалоги с выбором, флаги концовок (§8.8) |
| **NaughtyAttributes** | Denis Rizov | удобные инспекторы |

## 2. Природа — Кордон, Лес Полоза, Ключи
| Ассет | Издатель | Для чего |
|---|---|---|
| **Conifers [BOTD]** | Unity | ели/сосны — уральская тайга |
| **Grass Flowers Pack Free** | ALP | трава, бурьян |
| **Nature Starter Kit 2** | Shapes | кусты, деревья, камни |
| **Rock and Boulders 2** | Manufactura K4 | скалы, Гора, карьер |
| **Outdoor Ground Textures** | A dog's life software | слои террейна: грязь, мох, щебень |
| **Fantasy Skybox FREE** / **AllSky Free** | Render Knight / rpgwhitelock | небо, пасмурное, ночь, небо Гула |

## 3. Постройки — Каменск-23, Ключи, Сортировочная, шахта
| Ассет | Издатель | Для чего |
|---|---|---|
| **Apartment Kit** | Brick Project Studio | интерьеры панелек |
| **Yughues Free Ground Materials** | Nobiax / Yughues | бетон, асфальт, плитка |
| **Yughues Free Metal Materials** | Nobiax / Yughues | ржавый металл, вагоны, шахтные конструкции |
| **Yughues Free Concrete Materials** | Nobiax / Yughues | панельки, бункер |
| **Old Sea Port** / аналог | Unity | контейнеры, краны, промзона (автобаза Шатунов) |

## 4. Персонажи и мутанты
| Ассет | Издатель | Для чего |
|---|---|---|
| **Zombie** | Pxltiger | Пустые (с анимациями) |
| **Basic Motions FREE** | Kevin Iglesias | анимации ходьбы/бега NPC |
| **Animal / Wolf** (поиск «wolf free») | разные | волки-«серые» |
| **Spider / Insect free** (поиск) | разные | шиши (временно) |

Вне Asset Store, бесплатно: **Mixamo** (люди + анимации — старатели, военные, бандиты),
**Quaternius** (CC0, животные, монстры — заготовки мутантов).

## 5. Звук
| Ассет | Издатель | Для чего |
|---|---|---|
| **Post Apocalypse Guns Demo** | Sound Earth Game Audio | выстрелы, перезарядка |
| **Footsteps – Essentials** | Nox Sound | шаги по поверхностям |
| **Free Casual/UI sounds** (поиск) | разные | щелчки КПК, детектор |

Вне Asset Store: **freesound.org** (CC0) — гул, ветер, радиопомехи, вой.

## 6. Чего бесплатно нет — делать самим / искать / покупать позже
- Модели советского оружия (АК, ПМ, СВД) — Sketchfab с CC-BY лицензией или заказать.
- Советский транспорт (УАЗ, ПАЗ, ЗИЛ, тепловоз) — Sketchfab CC-BY.
- Памятник, ДК, панельки снаружи — собрать в ProBuilder по модульной сетке.
- Самоцветы — простые шейдеры Shader Graph (кристалл + свечение).
- Аномалии — VFX Graph / Shader Graph (дисторсия, искажение, свечение).
- Гул — пост-обработка URP (Volume): хроматическая аберрация, виньетка, медный цвет-грейд.

## Порядок импорта под Срез 1 (Кордон + Ключи)
1. Starter Assets – FirstPerson
2. Low Poly Shooter Pack – Free Sample
3. Conifers [BOTD], Grass Flowers Pack Free, Rock and Boulders 2, Outdoor Ground Textures
4. Fantasy Skybox FREE
5. War FX, Unity Particle Pack
6. Zombie (Pxltiger) — временно вместо шишей
7. Footsteps – Essentials, Post Apocalypse Guns Demo
8. Ink Integration, DOTween
