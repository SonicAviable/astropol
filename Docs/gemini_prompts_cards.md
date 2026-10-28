# Промты Gemini: недостающие арты карт и секторов

Сейчас без своего арта 13 карт: вся фракция Ксарн, Тэрра'ан и «Ноль» плюс «Пси-шторм» Иридии
(командиры H1 и H2 тоже без своего арта, но у них стоит портрет лидера фракции — см. раздел «Командиры»).
Пока арта нет, игра показывает запасную иллюстрацию события, и на карте в новой рамке это заметно.

**Формат карт — 3:4, вертикально** (как у готовых артов, 768×1024).
Главный объект — в верхних двух третях кадра: нижнюю четверть закрывает панель с текстом.
Присылайте в любом формате — я обрежу, переименую и положу в `Assets/Resources/UI/Cards/<код>.jpg`.

К каждому промту карты в конце добавляйте общий хвост стиля (тот же, что у готовых артов):

```
Dark gritty military sci-fi card illustration for a collectible card game, vertical 3:4 portrait composition, main subject in the upper two thirds, painterly digital art, dramatic cinematic lighting with strong rim light, smoke, embers and battlefield debris, rich detail, high contrast, no text, no letters, no logos, no watermark, no UI, no card frame, no border
```

---

## Доминион Ксарн — красный и чёрный, жёсткий милитаризм (4 карты)

### X1 — «Штрафной батальон» (юнит, пехота; погибает в конце хода, гибель злит союзников: +1 к силе)
```
A desperate charge of a penal battalion of the Xarn Dominion: gaunt soldiers in battered black armor with red penal markings and chains on their wrists, cheap rifles and demolition charges, sprinting through a hail of tracer fire across a burning wasteland; red-armored officers with laser pistols drive them forward from behind, and the troopers behind roar in fury as the first ranks fall; doomed, savage, heroic; black steel, blood red and orange explosion light
```

### X2 — «Знамя Доминиона» (юнит, пехота; при раскрытии все союзники сектора +1 к силе)
```
A towering Xarn standard bearer in heavy black power armor with crimson trim plants a huge war banner into the rubble of a captured fortress; the banner is a tattered crimson cloth with a black spearhead sigil, lit from behind by a giant explosion; ranks of black-armored troopers raise their rifles and roar around him; triumphant, fanatical, inspiring; black, crimson and gold palette
```

### X3 — «Децимация» (приказ; самый слабый свой юнит казнён, остальные +2 к силе)
```
A grim execution in a ruined trench line of the Xarn Dominion: a stern commissar in a long black greatcoat with crimson epaulettes raises a heavy pistol, a line of kneeling disgraced soldiers in front of him, behind them rows of hardened troopers watching in silence, their eyes now burning with fury; cold rain, red signal flares in the sky; brutal, oppressive discipline; desaturated black and grey with harsh red light
```

### X4 — «Титан „Молот Ксарна“» (юнит, техника, цена 6; давит до двух пехотинцев, бронебой)
```
A colossal Xarn war titan, a walking fortress over fifty meters tall in black armor plates with glowing red seams, a giant hammer-shaped siege cannon on one arm and a plasma lance on the other, crushing a ruined city block underfoot; tiny enemy soldiers scatter around its feet; the cannon fires with a blinding muzzle flash; seen from a low heroic angle; overwhelming scale and power; black, crimson and molten orange palette
```

---

## Конклав Тэрра'ан — живые организмы, зелень и биосвечение (4 карты)

### T1 — «Споровое облако» (приказ; все враги сектора −1 к силе)
```
A vast glowing cloud of green spores rolls over an enemy trench line like a living fog; armored soldiers cough and stumble, their visors fogged, weapons lowered; inside the cloud tiny bioluminescent spores sparkle, and huge alien fungal stalks loom in the haze; eerie, suffocating, otherworldly; toxic green, sickly yellow and dark teal palette
```

### T2 — «Корни-ловушки» (ловушка; первый раскрытый враг обездвижен на 2 хода)
```
Giant alien roots burst out of the ground of a battlefield and coil around a heavy armored mech, cracking its armor plates and pinning its legs; thorny tendrils glow with green bioluminescent veins; the mech's pilot lights flicker red in panic; dust and broken concrete fly into the air; sudden, organic, terrifying ambush; dark earth tones, emerald glow and red warning lights
```

### T3 — «Рой опылителей» (юнит, рой)
```
A swarm of hundreds of alien insectoid pollinator creatures with translucent glowing wings and long stingers diving out of a stormy sky onto a battlefield; each creature the size of a dog, armored chitin in green and amber, wings shimmering like stained glass; soldiers below fire upward in panic; dynamic motion, sense of overwhelming numbers; green, amber and gold palette
```

### T4 — «Древо-бастион» (юнит, броня 10; в конце хода +1 броня союзникам сектора)
```
A gigantic living fortress-tree of the Terra'an Conclave rising over a battlefield: an ancient trunk as wide as a building, covered in bark armor plates, with glowing green sap veins and protective branches arching over alien warriors sheltering beneath; enemy shells explode harmlessly against a shimmering green bio-shield around its crown; ancient, unbreakable, protective; deep greens, emerald glow and warm gold light
```

---

## Машинный Консенсус «Ноль» — серебро, холодный белый, бирюзовый свет (4 карты)

### N1 — «Рой дронов» (юнит, дроны; рой)
```
A dense swarm of small white-and-silver combat drones of the machine collective, each a sleek disk with a single glowing cyan eye and twin micro-guns, pouring out of a dark sky in a spiral formation over a ruined city; hundreds of cyan laser tracers rain down; cold, precise, inhuman; ice white, silver and cyan against dark smoky sky
```

### N2 — «Узел Консенсуса» (юнит, дрон, 1/6; все ваши дроны в секторе +1 к силе)
```
A tall floating machine hub of the Consensus: a monolithic white and silver spire with rotating data rings and a blazing cyan core, projecting lines of light to dozens of drones hovering around it in perfect geometric formation; holographic data streams in the air; cold, intelligent, commanding presence over a dark battlefield; white, silver and cyan palette
```

### N3 — «Пересборка» (приказ; два погибших дрона снова в строю)
```
Destroyed white combat drones reassemble themselves on a battlefield: shattered silver parts, wires and armor plates float in the air and snap back together along beams of cyan light from a hovering repair unit; one drone is already whole and its cyan eye lights up; sparks and nano-particles glitter; uncanny, unstoppable; silver, white and cyan on dark grey smoke
```

### N4 — «Ассимиляция» (приказ; слабейший враг уничтожен, на его месте — ваш дрон)
```
A human soldier in heavy armor being assimilated by the machine collective: silver nanite tendrils with cyan light crawl over his armor, turning it into white machine plating, one half of his helmet already a smooth faceless drone mask with a single cyan eye; white drones hover around him; horror and cold beauty; silver, white, cyan and dark shadows
```

---

## Иридийский Оракул — фиолетовый, псионика (1 карта)

### I4 — «Пси-шторм» (приказ; 3 урона всем врагам в секторе и 1 — своим)
```
A devastating psionic storm tears through a battlefield: a towering vortex of violet and magenta lightning descends from a purple sky, enemy soldiers and vehicles lifted into the air and thrown around, armor cracking with violet light, even the seer's own guards stagger and shield their eyes; at the eye of the storm a robed Iridian seer floats with glowing white eyes and arms raised; apocalyptic, uncontrollable power; violet, magenta and white palette
```

---

## Командиры (по желанию)

Сейчас на картах «Генерал» (H1) и «Комендант» (H2) стоит портрет лидера фракции — он уже хорошо смотрится.
Если хочется отдельные арты именно командиров, имя файла — `H1_<фракция>.jpg` / `H2_<фракция>.jpg`
(фракции: `xarn`, `aquila`, `astrea`, `iridia`, `terraan`, `nol`). Пример для Ксарна:

### H1_xarn — «Генерал» (Ксарн)
```
A battle-hardened Xarn general in ornate black power armor with crimson cape and gold rank insignia stands on a ruined hill overlooking a burning battlefield, pointing his sword-like command baton forward; behind him squads of black-armored troopers and tanks advance; heroic, commanding; black, crimson and gold palette
```

### H2_xarn — «Комендант» (Ксарн)
```
A grim Xarn fortress commandant in heavy black armor with a crimson tabard stands behind a fortified bunker wall bristling with guns, holding a heavy shield, calmly directing defenders under artillery fire; searchlights and red tracers in the night sky; unbreakable, stoic; black, crimson and steel grey palette
```

---

## Плашки секторов (по желанию, 6 штук)

Формат — **16:9, горизонтально**. Имя файла — `sector_<тип>.jpg` в `Assets/Resources/UI/Cards/`.
Хвост стиля для секторов:

```
Dark military sci-fi environment concept art, 16:9 widescreen, painterly, dramatic lighting, smoke and battlefield atmosphere, no characters in the foreground, no text, no letters, no logos, no UI
```

### sector_power — «Электростанция»
```
A fortified fusion power plant on a colony world at night: massive cooling towers and a glowing blue reactor dome surrounded by power pylons and arcing cables, under siege, searchlights sweeping the smoky sky
```

### sector_factory — «Завод»
```
A huge armored war factory complex with smokestacks, conveyor gantries and glowing furnace openings, half-built tanks rolling out of the assembly halls, sparks and orange industrial light
```

### sector_hq — «Штаб»
```
A heavily fortified planetary command headquarters: a monolithic bunker with antenna arrays, a holographic war-table glow through armored windows, a shimmering blue energy shield dome over it, artillery explosions outside
```

### sector_mine — «Шахта»
```
A giant open-pit mine on a rocky alien world: colossal drilling rigs, ore conveyors and cranes, glowing veins of crystal ore in the cliffs, dust storms and burning wreckage on the terraces
```

### sector_city — «Город»
```
A war-torn futuristic colony city: tall residential towers with lit windows, collapsed skyscrapers, barricaded streets and burning vehicles, evacuation ships lifting off into a smoky sky
```

### sector_port — «Космопорт»
```
A colony spaceport under attack: landing pads with docked cargo ships, launch towers and fuel tanks, a dropship taking off through anti-air fire, burning hangars and red tracer streams in the night sky
```
