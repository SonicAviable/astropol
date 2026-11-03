# Промты Gemini: значки технологий

Сейчас у всех технологий одного направления один значок. Игра уже умеет брать свой значок для каждой технологии:
файл `Assets/Resources/UI/Icons/Tech/<id>.png` (например `wpn_kin_1.png`). Нет файла — остаётся общий значок направления.

## Общий стиль (добавлять к каждому промту)
> Game UI icon, single centered emblem, **pure white glyph with soft grey shading on a fully transparent background** (the game tints it), bold readable silhouette at 48 px, clean sci-fi engraving style, slight 3/4 perspective, no text, no frame, no circle background, no colors, 1024x1024 PNG.

Если Gemini не даёт прозрачный фон — просить `on pure black background`, потом убрать фон (белое на чёрном легко вырезать).
Внутри одного направления держите одну манеру; уровни I → II → III делайте «тяжелее» (больше деталей, свечения).

## Энергетика
- **Термоядерные реакторы I** — `rct_fus_1.png`
  > compact tokamak fusion reactor ring with a glowing plasma core
- **Термоядерные реакторы II** — `rct_fus_2.png`
  > twin stacked fusion tokamak rings linked by conduits, brighter core
- **Антиматерия** — `rct_ant_1.png`
  > magnetic containment bottle holding a sphere of antimatter, two opposing field rings
- **Энергия нулевой точки** — `rct_zpe_1.png`
  > perfect sphere of shimmering vacuum with a tiny point of light at its center, quantum foam ripples
- **Сверхпроводники** — `rct_sc_1.png`
  > coiled superconducting cable with frost crystals and a lightning arc along it
- **Магнитные ловушки** — `rct_mag_1.png`
  > magnetic bottle trap: two horseshoe magnets holding a floating helium-3 droplet
- **Сфера Дайсона** — `rct_dys_1.png`
  > partial Dyson sphere lattice wrapped around a small star
- **Энергия вакуума** — `rct_vac_1.png`
  > cracked space with energy pouring out of the void between two plates

## Сенсоры и ИИ
- **Сенсорные массивы** — `sen_bas_1.png`
  > large dish radar array with concentric scan waves
- **Гравиметрия** — `sen_grv_1.png`
  > gravimetric probe with warped grid lines bending around a mass
- **Тактические вычислители** — `sen_tac_1.png`
  > targeting reticle overlaid on a hexagonal tactical computer chip
- **Простейший ИИ** — `sen_ai_1.png`
  > simple robotic eye inside a circuit square
- **Продвинутый ИИ** — `sen_ai_2.png`
  > neural network brain made of circuit traces with glowing nodes
- **Квантовые вычисления** — `sen_qc_1.png`
  > quantum processor cube with entangled qubit orbits around it
- **Криптография** — `sen_crp_1.png`
  > padlock made of interlocking code glyphs
- **Агентурные сети** — `sen_net_1.png`
  > spy network: hooded silhouette head with web lines spreading to dots
- **Квантовое шифрование** — `sen_enc_1.png`
  > quantum key: a key whose teeth are entangled particle pairs
- **Глубокое внедрение** — `sen_dep_1.png`
  > masked agent face half hidden behind a dagger
- **Сингулярный разум** — `sen_sng_1.png`
  > brain dissolving into a singularity swirl

## Двигатели
- **Гипердвигатели I** — `prp_hyp_1.png`
  > starship nose piercing a hyperspace ring with speed lines
- **Гипердвигатели II** — `prp_hyp_2.png`
  > double hyperspace ring, ship streaking through both
- **Складки пространства** — `prp_fld_1.png`
  > folded sheet of space with a ship stepping from one fold to another
- **Импульсные двигатели** — `prp_eng_1.png`
  > impulse engine nozzle with bright exhaust cone
- **Ионные маневровые** — `prp_ion_1.png`
  > small ion thrusters firing blue streams in different directions
- **Инерционные компенсаторы** — `prp_inr_1.png`
  > gyroscope rings inside a ship hull silhouette, motion damping arcs
- **Подпространственные туннели** — `prp_slp_1.png`
  > subspace tunnel: spiral corridor going into depth
- **Червоточинные коридоры** — `prp_wrm_1.png`
  > wormhole: two funnel mouths connected by a twisted throat

## Наука и управление
- **Административные системы** — `soc_admin_1.png`
  > stack of data tablets with a seal stamp
- **Бюрократическая реформа** — `soc_admin_2.png`
  > gears interlocking with a scroll — bureaucratic machine
- **Научные институты** — `soc_sci_1.png`
  > domed science institute building with an atom above it
- **Нейросетевое планирование** — `soc_sci_2.png`
  > planning board with neural lines connecting schedule blocks
- **Академия Звёздного Совета** — `soc_sci_3.png`
  > grand academy with columns and a star crest
- **Коллективный разум** — `soc_sci_4.png`
  > many small heads connected into one glowing mind
- **Дипломатическая служба** — `soc_inf_1.png`
  > two hands exchanging a sealed diplomatic letter
- **Культурная гегемония** — `soc_inf_2.png`
  > radiant cultural emblem: theatre mask and broadcast waves
- **Галактический авторитет** — `soc_inf_3.png`
  > crown made of stars over a galaxy spiral
- **Проект «Вознесение»** — `soc_asc_1.png`
  > figure rising upward dissolving into light, ascension rays

## Экспансия
- **Звёздная геодезия** — `soc_geo_1.png`
  > surveyor tool: star chart with a compass and triangulation lines
- **Ксеногеология** — `col_xgeo_1.png`
  > alien rock sample with glowing crystal veins under a scanner
- **Орбитальные поселения** — `col_hab_1.png`
  > ring-shaped orbital habitat around a small planet
- **Колониальный устав** — `cns_col_1.png`
  > colony charter document with a planet seal
- **Пограничные протоколы** — `col_frt_1.png`
  > border beacon pylon emitting a boundary line
- **Гидропоника** — `col_gro_1.png`
  > hydroponic dome with sprouting plants
- **Биоинженерия** — `col_gro_2.png`
  > DNA helix entwined with a leaf
- **Археотехнология** — `col_arc_1.png`
  > ancient alien artifact obelisk with glowing runes

## Военная доктрина
- **Флотская доктрина** — `doc_flt_1.png`
  > three ship chevrons in formation
- **Учения флота** — `doc_drl_1.png`
  > crosshair with training target rings and a stopwatch
- **Командная сеть** — `doc_net_1.png`
  > command star in the center linked to several ship icons
- **Военная логистика** — `doc_log_1.png`
  > supply crates on a cargo ship with route arrows
- **Эскадренная организация** — `doc_cap_1.png`
  > squadron banner with five small ship marks
- **Корабельные уставы** — `doc_cmd_1.png`
  > regulations book with an anchor-like fleet emblem
- **Тотальная мобилизация** — `doc_war_1.png`
  > clenched fist over crossed guns, war banner
- **Тыловое обеспечение** — `doc_cap_2.png`
  > supply chain: fuel canister, gear and ammo linked in a loop
- **Доктрина Господства** — `doc_ult_1.png`
  > imperial eagle-like star emblem crushing a planet

## Оружие
- **Кинетические орудия I** — `wpn_kin_1.png`
  > simple railgun barrel firing a slug
- **Кинетические орудия II** — `wpn_kin_2.png`
  > longer twin-rail railgun with magnetic coils
- **Масс-драйверы** — `wpn_kin_3.png`
  > heavy mass driver cannon with a huge projectile
- **Лазерные батареи I** — `wpn_las_1.png`
  > single laser emitter firing a thin beam
- **Лазерные батареи II** — `wpn_las_2.png`
  > twin laser turret with focused beams
- **Рентгеновские лазеры** — `wpn_las_3.png`
  > x-ray laser crystal emitting a jagged beam
- **Плазменные орудия** — `wpn_pls_1.png`
  > plasma cannon launching a burning plasma orb
- **Лэнс-орудия** — `wpn_lnc_1.png`
  > giant spinal lance weapon with charging rings
- **Гауссовы батареи** — `wpn_kin_4.png`
  > gauss battery: row of coil cannons
- **Фотонные лэнсы** — `wpn_las_4.png`
  > photon lance: long spear of light
- **Аннигиляторы** — `wpn_ult_1.png`
  > annihilator: black sphere with consuming energy rings

## Защита
- **Титановая броня I** — `def_arm_1.png`
  > riveted titanium armor plate
- **Титановая броня II** — `def_arm_2.png`
  > layered composite armor plates
- **Нейтрониевая броня** — `def_arm_3.png`
  > dense neutronium armor block with heavy glow seams
- **Протокол «Эгида»** — `def_aeg_1.png`
  > shield emblem “Aegis” with a winged crest
- **Энергощиты I** — `def_shl_1.png`
  > hexagonal energy shield bubble deflecting a bolt
- **Энергощиты II** — `def_shl_2.png`
  > double-layer hex shield with deflected impacts
- **Гиперщиты** — `def_shl_3.png`
  > hyper shield dome with crackling energy
- **Планетарные щиты** — `def_shl_4.png`
  > planet covered by a hexagonal shield dome
- **Барьер Предтеч** — `def_bar_1.png`
  > ancient precursor barrier: carved arch with an energy veil

## Промышленность
- **Плазменные буры** — `ind_min_1.png`
  > plasma drill bit cutting into rock
- **Глубокое обогащение** — `ind_min_2.png`
  > ore refinery: crusher with glowing ingots coming out
- **Астероидные комбинаты** — `ind_min_3.png`
  > asteroid with mining platform and cables
- **Сплавы нового поколения** — `ind_all_1.png`
  > new alloy ingots stacked with a shine
- **Нанофабрикация** — `ind_all_2.png`
  > nanobots assembling a gear
- **Автоматические кузни** — `ind_all_3.png`
  > robotic arm hammering on an anvil
- **Мегаинженерия** — `ind_meg_1.png`
  > megastructure frame: giant truss ring under construction
- **Рециклинг** — `ind_rec_1.png`
  > recycling loop arrows around a scrap gear
- **Сингулярные кузни** — `ind_sin_1.png`
  > forge powered by a small black hole

## Верфи
- **Орбитальные верфи** — `cns_sta_1.png`
  > orbital shipyard frame with a ship hull inside
- **Верфи класса «Эсминец»** — `cns_dst_1.png`
  > destroyer-class warship silhouette in a dock
- **Орбитальные доки** — `cns_dck_1.png`
  > orbital docking ring with clamps
- **Модульное строительство** — `cns_mod_1.png`
  > modular blocks snapping together into a ship
- **Звёздные крепости** — `cns_fort_1.png`
  > star fortress: armored space station with turrets
- **Автоматические верфи** — `cns_arc_1.png`
  > automated shipyard: robot arms building a hull
