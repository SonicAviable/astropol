# Astropolity — Russian → English localization rules

Game: space 4X strategy (like Stellaris / Master of Orion). Tone: concise, polished English UI; event texts are
literary sci-fi prose. American spelling.

## Hard rules (a broken string can crash or corrupt the UI)
1. Keep every placeholder EXACTLY: `{0}`, `{1:0.0}`, `{2,5}`, `{0:N0}`, `{0:+0;-0;0}` … You may reorder them, never
   change their content, never drop or add one. `{{` and `}}` are literal braces — keep them doubled.
   Named tokens like `{phrase}` or `{name}` must also be kept verbatim.
2. Keep rich-text tags exactly and balanced: `<b>`, `</b>`, `<i>`, `<color=#RRGGBB>`, `</color>`, `<size=9>`, `</size>`.
3. Keep `\n` line breaks (they appear as real newlines in the JSON values) in the same places where reasonable.
4. Keep leading/trailing spaces and separators exactly (e.g. "  ·  ", trailing ": ", leading " ").
5. Keep UPPERCASE strings uppercase, Title-like short labels in Title Case only if the Russian is a label.
6. Symbols like ▶ ✓ · — « » +/− stay; replace «…» quotes with “…”. Keep the Unicode minus "−" where used.
7. Single Russian words that are grammar helpers: "год"→"year", "года"/"лет"→"years", "день"→"day",
   "дня"/"дней"→"days", "месяц"→"month", "месяца"/"месяцев"→"months".
8. Fragments (pieces of sentences glued in code) — translate as fragments, keep the spaces at their edges.
9. Abbreviations: "дн." → "d", "мес." → "mo", "кор." (ships) → "ships", "сист." → "systems", "ур." → "lvl".
10. Don't translate things that are already Latin/numbers inside the string.

## Glossary (use consistently)
гелий-3 Helium-3 · титан Titanium · сплавы Alloys · влияние Influence · наука Research · энергия Energy ·
минералы Minerals · население / жители Population / pops · жильё Housing · район District ·
жилой район Housing district · добывающий район Mining district · генератор Generator district ·
промышленный район Industrial district · колония Colony · основать колонию Found a colony ·
терраформинг Terraforming · пригодность Habitability · добывающий комплекс / станция Mining station ·
форпост Outpost · звёздная база Starbase · верфь Shipyard · флот Fleet · корвет Corvette · фрегат Frigate ·
эсминец Destroyer · научный корабль Science ship · строительный корабль / строитель Construction ship ·
корпус Hull · модуль Module · щит Shields · броня Armor · мощь Power · флотский лимит Naval capacity ·
содержание Upkeep · гиперкоридор Hyperlane · система System · разведка Survey / exploration ·
аномалия Anomaly · Предтечи Precursors · левиафан Leviathan · пираты Pirates · каперские грамоты Letters of marque ·
дань Tribute · война War · мир Peace · перемирие Truce · пакт о ненападении Non-aggression pact ·
коалиция Coalition · торговый договор Trade agreement · научный договор Research agreement ·
обмен картами Map exchange · отношение Opinion · дипломатия Diplomacy · шпионаж Espionage ·
разведсеть Spy network · агент Agent · прикрытие Cover · досье Dossier · операция Operation ·
кража технологий Tech theft · диверсия Sabotage · раздор Discord · контрразведка Counterintelligence ·
лидер Leader · учёный Scientist · адмирал Admiral · губернатор Governor · черта Trait · опыт XP ·
уровень Level · совет Council · технология Technology · исследование Research · прорыв Breakthrough ·
планировщик Planner · обзор империи Empire Overview · конструктор судов Ship Designer ·
путь к победе Path to Victory · ОРАКУЛ-7 ORACLE-7 · советник Advisor · событие Event ·
сохранение Save · загрузка Load · настройки Settings · новая игра New Game · продолжить Continue ·
пауза Pause · скорость Speed.

## Factions and proper names
Республика Астрея → Republic of Astrea · Доминион Ксарн → Xarn Dominion · Синдикат Аквила → Aquila Syndicate ·
Иридийский Оракул → Iridian Oracle · Хлорофиловый Конклав Тэрра'ан → Chlorophyll Conclave of Terra'an ·
Ксарн → Xarn · Астрея → Astrea · Аквила → Aquila · Иридия → Iridia · Тэрра'ан → Terra'an ·
Совет Республики → Council of the Republic · Совет Корней → Council of Roots · легионы → legions.
Character/leader first names and surnames: transliterate (e.g. Ардан Ворх → Ardan Vorkh, Аурелия → Aurelia).

## Star names (Generation/StarNameGenerator.cs)
Use the standard English astronomical names: Альтаир→Altair, Бетельгейзе→Betelgeuse, Фомальгаут→Fomalhaut …
Greek letters: Альфа→Alpha, Бета→Beta … Омега→Omega, Ню→Nu, Мю→Mu, Кси→Xi.
Constellations are given in the Russian GENITIVE — translate to the Latin genitive used in Bayer names:
Кита→Ceti, Эридана→Eridani, Лебедя→Cygni, Ориона→Orionis, Персея→Persei, Лиры→Lyrae, Орла→Aquilae,
Кассиопеи→Cassiopeiae, Андромеды→Andromedae, Пегаса→Pegasi, Центавра→Centauri, Кормы→Puppis, Печи→Fornacis …
Catalogs: Глизе→Gliese, Кеплер-→Kepler- (keep the trailing hyphen), Вольф→Wolf, Росс→Ross, Лейтен→Luyten,
Лаланд→Lalande, Струве→Struve, Грумбридж→Groombridge, Козак→Kozak, Ван Маанен→Van Maanen, Тиго→Tycho.
Invented syllables (short pieces like "Ксе", "Ва", "рис") — transliterate to pleasant Latin syllables
(Ксе→Xe, Ва→Va, Ко→Co, рис→ris, гар→gar); keep them lowercase if the Russian piece is lowercase.

## Output format
A single JSON object: { "<russian key exactly as given>": "<english>", ... } containing EVERY key of the batch,
UTF-8, written with Python's json.dump(..., ensure_ascii=False, indent=0).
