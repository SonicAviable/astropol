/* Вики Astropolity: содержимое страниц. Числа берутся из window.GAME_DATA (собирается из кода игры) либо
   перечислены здесь по исходникам версии 1.3. */
(function () {
  "use strict";
  const D = window.GAME_DATA;
  const byId = (arr) => Object.fromEntries(arr.map((x) => [x.id, x]));
  const TECH = byId(D.techs), MOD = byId(D.modules), HULL = byId(D.hulls);
  const BR = {
    physics: { name: "Физика", c: "#4df2db", icon: "reactor" },
    engineering: { name: "Инженерия", c: "#ffa35c", icon: "industry" },
    society: { name: "Общество", c: "#bd8fff", icon: "leader" },
  };
  const CLS = { governor: ["Губернатор", "#ffcc52"], scientist: ["Учёный", "#66f59e"], admiral: ["Адмирал", "#ff6a6a"], agent: ["Агент", "#bd8fff"] };
  const FMT = new Intl.NumberFormat("ru-RU");
  const n = (v) => FMT.format(Math.round(v));
  const ic = (name, color) => `<i class="ic" style="--i:url(img/icons/${name}.png)${color ? ";color:" + color : ""}"></i>`;
  const R = {
    he: ic("res_energy", "var(--gold)") + " Гелий-3",
    ti: ic("res_minerals", "var(--cyan)") + " Титан",
    al: ic("res_alloys", "var(--amber)") + " Сплавы",
    inf: ic("res_influence", "var(--violet)") + " Влияние",
    sci: ic("res_research", "var(--green)") + " Наука",
  };
  const tbl = (heads, rows, numFrom = 99) =>
    `<div class="tbl"><table><thead><tr>${heads.map((h, i) => `<th class="${i >= numFrom ? "n" : ""}">${h}</th>`).join("")}</tr></thead><tbody>` +
    rows.map((r) => `<tr>${r.map((c, i) => `<td class="${i >= numFrom ? "n" : ""}">${c}</td>`).join("")}</tr>`).join("") +
    `</tbody></table></div>`;
  const call = (t, body, kind = "") => `<div class="call ${kind}"><strong>${t}</strong>${body}</div>`;
  const kbd = (...k) => k.map((x) => `<kbd>${x}</kbd>`).join(" ");
  const tname = (id) => (TECH[id] ? TECH[id].name : id);
  const dmgName = { energy: "Энергия", kinetic: "Кинетика", explosive: "Взрыв" };

  const groups = [
    ["Основы", ["start", "controls", "economy", "colonies", "build"]],
    ["Война", ["fleet", "combat"]],
    ["Развитие", ["tech", "leaders", "landmarks"]],
    ["Мир", ["empires", "diplomacy", "espionage", "threats"]],
    ["Партия", ["victory", "settings", "faq"]],
  ];

  const pages = {};

  /* ============================== ОСНОВЫ ============================== */
  pages.start = {
    title: "Начало игры", icon: "planet",
    lead: "Что происходит в первые минуты и на чём держится вся партия.",
    body: () => `
      <h3>Что это за игра</h3>
      <p>Astropolity — стратегия в реальном времени с паузой. Вы правите одной из пяти империй в процедурной галактике, где у вас четыре ресурса, один стартовый мир и шестьдесят лет на то, чтобы стать сильнейшей державой. Время идёт само: игровые сутки — это около 1,6 секунды реального времени, а скорость переключается на ×1, ×2 и ×4. Партия рассчитана на 2–3 часа.</p>
      <h3>Первые минуты</h3>
      <ol>
        <li><b>Выберите империю</b> и сложность. Лидер и доктрина задают игровой стиль, но не запирают вас в нём.</li>
        <li><b>Выслушайте советника</b> — он коротко расскажет о том, с чего начать, и выдаст стартовые бонусы.</li>
        <li><b>Займитесь столицей.</b> Она стартует с шестью поселенцами и четырьмя типами районов: три городских, один добывающий, один энергетический и один промышленный.</li>
        <li><b>Разведайте соседей.</b> Научный корабль открывает системы и ищет аномалии, узлы силы и залежи.</li>
        <li><b>Расширяйтесь.</b> Форпост в системе нужен для колонии, колония даёт доход, но съедает влияние.</li>
      </ol>
      ${tbl(["Ресурс", "Старт", "Базовый доход в месяц"], [
        [R.he, "400", "15"], [R.ti, "500", "10"], [R.al, "250", "5"], [R.inf, "100", "3"],
      ], 1)}
      ${call("Совет", "<p>Влияние — главный ограничитель расширения. Если оно уходит в минус, рост стопорится. Следите за ним раньше, чем за титаном.</p>")}
      <h3>Интерфейс</h3>
      <ul>
        <li><b>Левое меню</b> открывает окна империи, технологий, шпионажа, дипломатии и построек.</li>
        <li><b>Верхняя панель</b> показывает запасы и дату, а скорость переключается кнопками времени.</li>
        <li><b>Кликните по системе</b>, чтобы увидеть её планеты. Метки показывают, какие уже колонизованы, а какие осваиваются.</li>
        <li><b>Автосохранение</b> срабатывает периодически (период задаётся в настройках), а быстрое сохранение — на ${kbd("F5")} (загрузка ${kbd("F9")}).</li>
      </ul>`,
  };

  pages.controls = {
    title: "Управление", icon: "fleet",
    lead: "Мышь и клавиатура. Все основные действия — одним нажатием.",
    body: () => `
      ${tbl(["Действие", "Клавиша"], [
        ["Выбрать флот или систему", "ЛКМ"],
        ["Отдать приказ (лететь, атаковать)", "ПКМ"],
        ["Поставить приказ в очередь", kbd("Shift") + " + ПКМ"],
        ["Двигать камеру", kbd("W", "A", "S", "D")],
        ["Приблизить / отдалить", "Колесо мыши"],
        ["Пауза", kbd("Space")],
        ["Скорость ×1 / ×2 / ×4", kbd("1", "2", "3")],
        ["Быстрое сохранение / загрузка", kbd("F5") + " / " + kbd("F9")],
        ["Окно «Пути к победе»", kbd("V")],
        ["Перейти к флоту", kbd("F")],
        ["Закрыть окно или открыть меню паузы", kbd("Esc")],
      ])}
      ${call("Камера", "<p>Камера может приближаться вплотную к кораблям: при сильном приближении видны сопла и шлейфы двигателей, а при полёте — разгон, прыжок и торможение.</p>")}`,
  };

  pages.economy = {
    title: "Ресурсы", icon: "res_energy",
    lead: "Четыре ресурса плюс наука. Что откуда берётся и куда уходит.",
    body: () => `
      <div class="cards">
        ${[
          ["res_energy", "var(--gold)", "Гелий-3", "Базовый доход 15 в месяц", "Топливо империи. Нужен для терраформирования, содержания лидеров (1,5 в месяц каждого) и части зданий. Добывается энергетическими районами: +4 в месяц на район."],
          ["res_minerals", "var(--cyan)", "Титан", "Базовый доход 10 в месяц", "Основа строительства: колонии, районы, станции. Добывается горнодобывающими районами: +4 в месяц."],
          ["res_alloys", "var(--amber)", "Сплавы", "Базовый доход 5 в месяц", "Корабли и звёздные базы. Дают промышленные районы: +3 в месяц на район."],
          ["res_influence", "var(--violet)", "Влияние", "Базовый доход 3 в месяц", "Дипломатия, шпионаж, наём лидеров и расширение. Потолок влияния ограничивает число колоний и форпостов."],
          ["res_research", "var(--green)", "Наука", "Зависит от учёных и построек", "Копится в изучаемой технологии. Растёт от лидеров-учёных, технологий и пульсара Ирис."],
        ].map(([i, c, t, s, p]) => `<div class="card" style="--acc:${c}">${ic(i)}<h4>${t}</h4><div class="eff">${s}</div><p>${p}</p></div>`).join("")}
      </div>
      <h3>Банкротство</h3>
      <p>Если какой-либо ресурс уходит в минус, наказание применяется ко всем доходам сразу: <b>доходы падают вдвое</b>. Долгое банкротство (6 месяцев подряд) заканчивает партию поражением.</p>
      <h3>Содержание флота</h3>
      <p>Каждый военный корабль платит за содержание в сплавах: корвет 3, фрегат 5, эсминец 8 в месяц. Лимит флота — 8 единиц плюс 2 за каждую колонию; технологии доктрины его расширяют и удешевляют содержание.</p>
      <h3>Откуда берутся бонусы</h3>
      <ul>
        <li><b>Технологии</b> — основной источник: каждая вторая из 89 даёт постоянный бонус к доходу, науке или флоту.</li>
        <li><b>Узлы силы</b> — особые системы на карте: нужно держать их форпостами.</li>
        <li><b>Лидеры</b> — учёные и губернаторы, а ещё их черты.</li>
        <li><b>Империя</b> — у каждой своя доктрина: например, +20% энергии или +25% влияния.</li>
      </ul>`,
  };

  const DIST = [
    ["urban", "Городской", 60, 20, 90, "+2 жилья, +1 рабочее место", "housing"],
    ["mining", "Горнодобывающий", 80, 10, 75, "+4 титана", "res_minerals"],
    ["generator", "Энергогенерация", 70, 25, 75, "+4 гелия-3", "res_energy"],
    ["industrial", "Промышленный", 120, 60, 120, "+3 сплава", "industry"],
  ];

  pages.colonies = {
    title: "Колонии и районы", icon: "population",
    lead: "Как растёт планета и что в неё строить.",
    body: () => `
      <h3>Путь планеты</h3>
      <ol>
        <li><b>Форпост</b> в системе: 50 сплавов и 25 влияния, 120 дней строительства.</li>
        <li><b>Колония</b>: 80 титана, 20 сплавов и 25 влияния, 180 дней. Появляется с городским и горнодобывающим районом.</li>
        <li><b>Рост.</b> Население растёт, пока хватает жилья. Жильё даёт городской район, рабочие места — все остальные.</li>
        <li><b>Добывающая станция</b> (50 титана, 60 дней) качает залежи планеты без колонии.</li>
        <li><b>Терраформирование</b>: 120 гелия-3, 90 титана, 10 влияния, 360 дней — планета становится пригоднее для жизни.</li>
      </ol>
      <h3>Районы</h3>
      ${tbl(["Район", "Титан", "Сплавы", "Дни", "Даёт"], DIST.map((d) => [ic(d[6]) + " " + d[1], d[2], d[3], d[4], d[5]]), 1)}
      <p>Хорошая колония — это баланс: без городских районов не будет жителей, без добывающих — не на что строить.</p>
      <div class="tool" id="planner">
        <div class="tool-head"><i></i>Планировщик колонии</div>
        <div class="tool-body">
          <div class="tool-in">
            ${DIST.map((d) => `<div class="row"><label>${ic(d[6])} ${d[1]}</label><div class="step-in" data-k="${d[0]}"><button type="button" aria-label="Меньше">−</button><output>${d[0] === "urban" ? 3 : 1}</output><button type="button" aria-label="Больше">+</button></div></div>`).join("")}
            <div class="row"><label>${ic("population")} Население</label><div class="step-in" data-k="pop"><button type="button" aria-label="Меньше">−</button><output>6</output><button type="button" aria-label="Больше">+</button></div></div>
          </div>
          <div class="tool-out" id="plannerOut"></div>
        </div>
      </div>
      ${call("Практика", "<p>Столица на старте: 3 городских, 1 добывающий, 1 энергетический и 1 промышленный район, 6 поселенцев. Так что стартовый план — 6 жилья и 3 рабочих места.</p>")}
      <h3>Особые планеты</h3>
      <p>На картах встречаются залежи гелия-3 и титана. Добывающая станция забирает их без колонизации, а узел «Кладезь Недр» добавляет ещё +10 гелия-3 и +10 титана в месяц.</p>`,
    init(root) {
      const out = root.querySelector("#plannerOut");
      const st = { urban: 3, mining: 1, generator: 1, industrial: 1, pop: 6 };
      const draw = () => {
        const cnt = DIST.map((d) => st[d[0]]);
        const totTi = DIST.reduce((s, d, i) => s + d[2] * cnt[i], 0);
        const totAl = DIST.reduce((s, d, i) => s + d[3] * cnt[i], 0);
        const days = DIST.reduce((s, d, i) => s + d[4] * cnt[i], 0);
        const housing = st.urban * 2, jobs = st.urban + st.mining + st.generator + st.industrial;
        const homeless = Math.max(0, st.pop - housing);
        const idle = Math.max(0, st.pop - jobs);
        out.innerHTML = `
          <div class="out-line"><span>${R.ti}</span><b>${n(totTi)}</b></div>
          <div class="out-line"><span>${R.al}</span><b>${n(totAl)}</b></div>
          <div class="out-line"><span>Суммарное время работ</span><b>${n(days)} дн.</b></div>
          <div class="bar"><i style="width:${Math.min(100, days / 6)}%;--c:var(--cyan)"></i></div>
          <div class="out-line"><span>Доход ${ic("res_minerals")}</span><b>+${st.mining * 4}</b></div>
          <div class="out-line"><span>Доход ${ic("res_energy")}</span><b>+${st.generator * 4}</b></div>
          <div class="out-line"><span>Доход ${ic("res_alloys")}</span><b>+${st.industrial * 3}</b></div>
          <div class="out-line"><span>Жильё / работа</span><b>${housing} / ${jobs}</b></div>
          ${homeless ? `<p class="warn-text">Не хватает жилья: ${homeless} поселенцев без дома.</p>` : ""}
          ${idle ? `<p class="warn-text">${idle} жителей без работы — постройте районы.</p>` : ""}
          ${!homeless && !idle && st.pop ? `<p class="good-text">Планета сбалансирована.</p>` : ""}`;
        requestAnimationFrame(() => out.querySelectorAll(".bar i").forEach((b) => (b.style.width = b.style.width)));
      };
      root.querySelectorAll("#planner .step-in").forEach((el) => {
        const k = el.dataset.k, o = el.querySelector("output"), [m, p] = el.querySelectorAll("button");
        const set = (v) => { st[k] = Math.max(0, Math.min(k === "pop" ? 40 : 12, v)); o.textContent = st[k]; draw(); };
        m.onclick = () => set(st[k] - 1); p.onclick = () => set(st[k] + 1);
      });
      draw();
    },
  };

  pages.build = {
    title: "Строительство", icon: "construction",
    lead: "Очередь работ: что можно строить и сколько это занимает.",
    body: () => `
      <p>Каждая стройка идёт в очереди колонии или верфи и занимает указанное число игровых дней. Стоимость показана без учёта бонусов технологий и лидеров.</p>
      ${tbl(["Объект", "Цена", "Дни"], [
        ["Форпост", "50 сплавов, 25 влияния", "120"],
        ["Колония", "80 титана, 20 сплавов, 25 влияния", "180"],
        ["Добывающая станция", "50 титана", "60"],
        ["Терраформирование", "120 гелия-3, 90 титана, 10 влияния", "360"],
        ["Городской район", "60 титана, 20 сплавов", "90"],
        ["Горнодобывающий район", "80 титана, 10 сплавов", "75"],
        ["Энергогенерация", "70 титана, 25 сплавов", "75"],
        ["Промышленный район", "120 титана, 60 сплавов", "120"],
        ["Корвет", `${HULL.corvette.cost} сплавов`, "60"],
        ["Фрегат", `${HULL.frigate.cost} сплавов + модули`, "100"],
        ["Эсминец", `${HULL.destroyer.cost} сплавов + модули`, "150"],
        ["Гражданский корабль", "научный или строительный", "90"],
      ])}
      ${call("Стоимость корабля", "<p>Цена корабля складывается из корпуса и всех установленных модулей — смотрите конструктор в разделе «Флот».</p>")}
      <h3>Верфь и форпосты</h3>
      <p>Корабли строятся на верфи. Форпост (звёздная база) закрепляет за вами систему, защищает её и держит узлы силы; прочность баз повышают технологии обороны.</p>`,
  };

  /* ============================== ВОЙНА ============================== */
  const SLOT = { weapon: "Оружие", defense: "Защита", utility: "Системы" };
  pages.fleet = {
    title: "Флот и корабли", icon: "frigate",
    lead: "Три корпуса, семнадцать модулей и конструктор, в котором вы собираете собственные проекты.",
    body: () => `
      <h3>Корпуса</h3>
      ${tbl(["Корпус", "Корпус", "Броня", "Щиты", "Оружие", "Защита", "Системы", "Скорость", "Сплавы"],
        D.hulls.map((h) => [ic(h.id) + " " + h.name, h.hull, h.armor, h.shields, h.weapon, h.defense, h.utility, h.speed, h.cost]), 1)}
      <p>Эсминец требует технологию «${tname("cns_dst_1")}». Все военные корабли используют один и тот же линейный крейсер «Харбингер»; гражданские — свои модели.</p>
      <div class="tool" id="designer">
        <div class="tool-head"><i></i>Конструктор кораблей</div>
        <div class="tool-body">
          <div class="tool-in">
            <div class="row"><label>Корпус</label><select id="dsHull">${D.hulls.map((h) => `<option value="${h.id}">${h.name}</option>`).join("")}</select></div>
            <div id="dsSlots"></div>
          </div>
          <div class="tool-out" id="dsOut"></div>
        </div>
      </div>
      <h3>Модули</h3>
      ${["weapon", "defense", "utility"].map((s) => `<h4>${SLOT[s]}</h4>` + tbl(["Модуль", "Эффект", "Тип", "Цена", "Технология"],
        D.modules.filter((m) => m.slot === s).map((m) => [
          m.name,
          m.slot === "weapon" ? `${m.damage} × ${m.rate}/с (${(m.damage * m.rate).toFixed(1)} в секунду)` :
          m.slot === "defense" ? [m.shields && "щиты +" + m.shields, m.armor && "броня +" + m.armor, m.hull && "корпус +" + m.hull].filter(Boolean).join(", ") :
          [m.power && "энергия +" + m.power, m.speed && "скорость +" + m.speed, m.acc && "точность +" + m.acc].filter(Boolean).join(", ") || "—",
          m.dtype ? dmgName[m.dtype] : "—", m.cost, m.tech ? tname(m.tech) : "с начала",
        ]))).join("")}
      <div class="call warn"><strong>Энергия</strong><p>Реакторы вырабатывают энергию, щиты и оружие её тратят. Если расход выше выработки, корабль работает на пониженной мощности — конструктор подсвечивает это жёлтым.</p></div>`,
    init(root) {
      const hullSel = root.querySelector("#dsHull"), slots = root.querySelector("#dsSlots"), out = root.querySelector("#dsOut");
      let pick = {};
      const defaults = { weapon: "wpn_laser_red", defense: "def_shield", utility: "utl_reactor" };
      const build = () => {
        const h = HULL[hullSel.value];
        slots.innerHTML = "";
        ["weapon", "defense", "utility"].forEach((s) => {
          const opts = D.modules.filter((m) => m.slot === s);
          for (let i = 0; i < h[s]; i++) {
            const key = s + i;
            if (!pick[key] || !opts.some((o) => o.id === pick[key])) pick[key] = s === "utility" && i > 0 ? "utl_engine_basic" : defaults[s];
            const d = document.createElement("div"); d.className = "slot";
            d.innerHTML = `${ic(s === "weapon" ? "weapons" : s === "defense" ? "defense" : "reactor")}<select data-k="${key}" aria-label="${SLOT[s]} ${i + 1}">${opts.map((o) => `<option value="${o.id}"${o.id === pick[key] ? " selected" : ""}>${o.name}${o.tech ? " ★" : ""}</option>`).join("")}</select>`;
            slots.appendChild(d);
          }
        });
        slots.querySelectorAll("select").forEach((el) => (el.onchange = () => { pick[el.dataset.k] = el.value; calc(); }));
        calc();
      };
      const calc = () => {
        const h = HULL[hullSel.value];
        const mods = [...slots.querySelectorAll("select")].map((el) => MOD[el.value]);
        const sum = (f) => mods.reduce((s, m) => s + (m[f] || 0), 0);
        const dps = mods.filter((m) => m.slot === "weapon").reduce((s, m) => s + m.damage * m.rate, 0);
        const hp = h.hull + sum("hull"), armor = h.armor + sum("armor"), shield = h.shields + sum("shields");
        const power = sum("power"), draw = sum("draw");
        const cost = h.cost + sum("cost");
        const ev = h.evasion + sum("evasion"), spd = h.speed + sum("speed");
        const ok = power >= draw;
        const locked = mods.filter((m) => m.tech);
        const bar = (v, max, c) => `<div class="bar"><i style="--c:${c};width:${Math.min(100, (v / max) * 100)}%"></i></div>`;
        out.innerHTML = `
          <div class="out-line"><span>${ic("res_alloys")} Цена</span><b>${n(cost)}</b></div>
          <div class="out-line"><span>Корпус</span><b>${n(hp)}</b></div>${bar(hp, 400, "var(--rose)")}
          <div class="out-line"><span>Броня</span><b>${n(armor)}</b></div>${bar(armor, 260, "var(--amber)")}
          <div class="out-line"><span>Щиты</span><b>${n(shield)}</b></div>${bar(shield, 320, "var(--cyan)")}
          <div class="out-line"><span>Урон в секунду</span><b>${dps.toFixed(1)}</b></div>${bar(dps, 60, "var(--gold)")}
          <div class="out-line"><span>Уклонение / скорость</span><b>${ev} / ${spd.toFixed(2)}</b></div>
          <div class="out-line"><span>Энергия: выработка / расход</span><b class="${ok ? "good-text" : "bad-text"}">${power} / ${draw}</b></div>
          ${ok ? "" : `<p class="warn-text">Не хватает энергии: добавьте реактор или уберите щит.</p>`}
          ${locked.length ? `<p class="warn-text">★ требует технологий: ${[...new Set(locked.map((m) => tname(m.tech)))].join(", ")}.</p>` : ""}`;
      };
      hullSel.onchange = build; build();
    },
  };

  pages.combat = {
    title: "Бой и осады", icon: "weapons",
    lead: "Три типа урона, три слоя защиты и несколько стоек флота.",
    body: () => `
      <h3>Слои защиты</h3>
      <p>Снаряд проходит щиты, потом броню, потом бьёт по корпусу. У каждого типа урона своя эффективность против слоя защиты, и это определяет, чем стрелять по конкретному врагу.</p>
      <div class="matrix" id="matrix">
        <div class="h"></div><div class="h v">Щиты</div><div class="h v">Броня</div><div class="h v">Корпус</div>
        ${[
          ["Кинетика", "cannon", [1.45, 0.65, 1.0]],
          ["Энергия", "laser", [0.7, 1.45, 1.0]],
          ["Взрыв", "missile", [1.05, 1.05, 1.0]],
        ].map(([name, ico, v], r) => `<div class="h">${ic(ico)} ${name}</div>` + v.map((x, c) => {
          const col = x > 1.2 ? "var(--green)" : x < 0.8 ? "var(--danger)" : "var(--gold)";
          return `<div class="v" style="--c:${col};--o:${Math.min(0.45, Math.abs(x - 1) * 0.9 + 0.12)};--dl:${(r * 3 + c) * 90}ms"><b>×${x.toFixed(2)}</b></div>`;
        }).join("")).join("")}
      </div>
      <p>Кинетика пробивает щиты (×1,45), но вязнет в броне (×0,65). Энергетическое оружие — наоборот: щиты глушат его до ×0,70, а броню оно прожигает на ×1,45. Взрывчатка — универсальная, но без перекоса. Потому лучшие флоты — смешанные.</p>
      <h3>Стойки флота</h3>
      ${tbl(["Стойка", "Эффект"], [
        ["Сбалансированная", "Без бонусов и штрафов."],
        ["Натиск", "Урон +20%, сближение вдвое короче. Уклонение −25%, входящий урон +10%."],
        ["Оборона", "Входящий урон −15%, уклонение +25%, отступление на 30% быстрее. Свой урон −15%."],
        ["Фокус огня", "Все корабли бьют одну цель — самую повреждённую из приоритетных. Враги выбывают быстрее, но часть огня уходит впустую."],
      ])}
      <h3>Приоритеты целей</h3>
      <p>Флоту можно задать, по кому стрелять в первую очередь: по любым, по малым кораблям, по крупным или по звёздной базе. Хорошо работает в паре с фокусом.</p>
      <h3>Осада</h3>
      <p>Чтобы взять систему, нужно уничтожить защитников и выдержать осаду. Время осады: <b>24 дня</b> плюс <b>8 за каждую колонию</b> в системе, а за столицу — ещё <b>40</b>. </p>
      ${call("Звёздные базы", "<p>Базы получают шаблон от текущих технологий: чем выше уровень брони и щитов, тем крепче форпосты. Узел «Цитадель Древних» усиливает все ваши базы ещё на 25%.</p>")}`,
    init(root) {
      const m = root.querySelector("#matrix");
      if (m) io.observe(m);
    },
  };

  /* ============================== РАЗВИТИЕ ============================== */
  pages.tech = {
    title: "Технологии", icon: "scienceship",
    lead: "Дерево из 89 технологий в пяти эпохах. Кликните по узлу, чтобы увидеть путь к нему.",
    body: () => `
      <p>Исследования идут по одной технологии за раз. Стоимость растёт по эпохам: первая эпоха стоит базовую цену, вторая в 4,4 раза дороже, третья в 9, четвёртая в 13, пятая в 17. Узлы делятся на три ветви: физика, инженерия и общество.</p>
      ${tbl(["Эпоха", "Год", "Технологий", "Диапазон цены"],
        [0, 1, 2, 3, 4].map((t) => {
          const L = D.techs.filter((x) => x.tier === t); const c = L.map((x) => x.cost);
          return [["I", "II", "III", "IV", "V"][t], L[0].year, L.length, n(Math.min(...c)) + " – " + n(Math.max(...c))];
        }), 2)}
      <div class="tt-top">
        <div class="pills" id="ttBr"><button type="button" class="on" data-b="all">Все</button>${Object.entries(BR).map(([k, v]) => `<button type="button" data-b="${k}">${v.name}</button>`).join("")}</div>
        <label class="tt-search">${ic("sensors")}<input id="ttQ" type="text" placeholder="Найти технологию" aria-label="Поиск по технологиям"></label>
      </div>
      <div class="tt-wrap">
        <div class="tt-scroll" id="ttScroll"><div class="tt-body" id="ttBody"></div></div>
        <aside class="tt-detail hud" id="ttDetail"></aside>
      </div>
      ${call("Вознесение", "<p>Технологическая победа требует изучить <b>66 технологий из 89</b> и завершить проект «Вознесение» (★ в дереве). Он стоит " + n(TECH.soc_asc_1.cost) + " науки и закрывает Общество в 2242 году.</p>", "warn")}`,
    init(root) { initTechTree(root); },
  };

  function initTechTree(root) {
    const body = root.querySelector("#ttBody"), detail = root.querySelector("#ttDetail");
    const lanes = [];
    Object.keys(BR).forEach((b) => [0, 1, 2].forEach((r) => {
      const list = D.techs.filter((t) => t.branch === b && t.row === r);
      if (list.length) lanes.push({ b, r, list });
    }));
    body.innerHTML = `<div class="tt-heads"><div></div>${["I", "II", "III", "IV", "V"].map((x, i) => `<div>Эпоха ${x}<small>${[2200, 2208, 2216, 2229, 2242][i]}</small></div>`).join("")}</div>` +
      lanes.map((l) => {
        const cats = [...new Set(l.list.map((t) => t.cat))];
        return `<div class="tt-lane" style="--c:${BR[l.b].c}"><div class="tt-lab">${ic(BR[l.b].icon)}<b>${BR[l.b].name}</b><small>${cats.length} направл.</small></div>` +
          [0, 1, 2, 3, 4].map((tier) => `<div class="tt-cell" data-t="${tier}">` + l.list.filter((t) => t.tier === tier).map((t) =>
            `<button type="button" class="tt-node${t.id === "soc_asc_1" ? " cap" : ""}" data-id="${t.id}" style="--c:${BR[l.b].c}">${ic(iconFor(t))}<span>${t.name}</span></button>`).join("") + `</div>`).join("") + `</div>`;
      }).join("") + `<svg class="tt-svg" id="ttSvg"></svg>`;
    const svg = body.querySelector("#ttSvg");
    const nodes = Object.fromEntries([...body.querySelectorAll(".tt-node")].map((el) => [el.dataset.id, el]));
    let sel = null, branch = "all", q = "";
    const chainOf = (id) => {
      const up = new Set(), down = new Set();
      const walkUp = (i) => TECH[i].req.forEach((r) => { if (!up.has(r)) { up.add(r); walkUp(r); } });
      const walkDn = (i) => TECH[i].unlocks.forEach((r) => { if (!down.has(r)) { down.add(r); walkDn(r); } });
      walkUp(id); walkDn(id); return { up, down };
    };
    const edges = [];
    D.techs.forEach((t) => t.req.forEach((r) => { if (TECH[r]) edges.push([r, t.id]); }));
    const drawWires = () => {
      const base = body.getBoundingClientRect();
      svg.setAttribute("width", body.scrollWidth); svg.setAttribute("height", body.scrollHeight);
      const ch = sel ? chainOf(sel) : null;
      svg.innerHTML = edges.map(([a, b]) => {
        const A = nodes[a], B = nodes[b]; if (!A || !B || A.offsetParent === null || B.offsetParent === null) return "";
        const ra = A.getBoundingClientRect(), rb = B.getBoundingClientRect();
        const x1 = ra.right - base.left, y1 = ra.top + ra.height / 2 - base.top, x2 = rb.left - base.left, y2 = rb.top + rb.height / 2 - base.top;
        const dx = Math.max(24, (x2 - x1) / 2);
        const hi = ch && ((a === sel || ch.up.has(a)) && (b === sel || ch.up.has(b)) || (b === sel || ch.down.has(b)) && (a === sel || ch.down.has(a)));
        const cls = ch ? (hi ? "hi" : "dim") : "";
        return `<path class="${cls}" style="--c:${BR[TECH[a].branch].c}" d="M${x1},${y1} C${x1 + dx},${y1} ${x2 - dx},${y2} ${x2},${y2}"/>`;
      }).join("");
    };
    const apply = () => {
      const ch = sel ? chainOf(sel) : null;
      const ql = q.trim().toLowerCase();
      Object.entries(nodes).forEach(([id, el]) => {
        const t = TECH[id];
        const hidden = branch !== "all" && t.branch !== branch;
        el.closest(".tt-lane").style.display = "";
        el.style.display = hidden ? "none" : "";
        const match = ql && (t.name.toLowerCase().includes(ql) || t.desc.toLowerCase().includes(ql));
        el.classList.toggle("sel", id === sel);
        el.classList.toggle("chain", !!ch && (ch.up.has(id) || ch.down.has(id)) || !!match);
        el.classList.toggle("dim", (!!ch && id !== sel && !ch.up.has(id) && !ch.down.has(id)) || (!!ql && !match && !ch));
      });
      body.querySelectorAll(".tt-lane").forEach((l) => { l.style.display = [...l.querySelectorAll(".tt-node")].some((e) => e.style.display !== "none") ? "" : "none"; });
      requestAnimationFrame(drawWires);
    };
    const show = (id) => {
      const t = TECH[id]; const c = BR[t.branch].c;
      const link = (i) => `<button type="button" data-go="${i}">${tname(i)}</button>`;
      detail.style.setProperty("--c", c);
      detail.innerHTML = `
        <span class="badge">${BR[t.branch].name} · эпоха ${["I", "II", "III", "IV", "V"][t.tier]}</span>
        <h4>${t.name}</h4>
        <p>${t.desc}</p>
        <div class="stat-row"><div><b>${n(t.cost)}</b><span>наука</span></div><div><b>${t.year}</b><span>с года</span></div><div><b>${t.req.length}</b><span>требует</span></div></div>
        ${t.req.length ? `<small class="mono" style="color:var(--faint)">ТРЕБУЕТ</small><div class="tt-links">${t.req.map(link).join("")}</div>` : ""}
        ${t.unlocks.length ? `<small class="mono" style="color:var(--faint)">ОТКРЫВАЕТ</small><div class="tt-links">${t.unlocks.map(link).join("")}</div>` : ""}
        ${t.flavor ? `<p class="fl">${t.flavor}</p>` : ""}`;
      detail.querySelectorAll("[data-go]").forEach((b) => (b.onclick = () => pick(b.dataset.go, true)));
    };
    const pick = (id, scroll) => {
      sel = sel === id && !scroll ? null : id;
      if (sel) show(sel); else detail.innerHTML = `<p style="color:var(--muted)">Выберите технологию в дереве: подсветятся все её предшественники и всё, что она открывает.</p>`;
      if (sel && scroll) nodes[sel].scrollIntoView({ block: "nearest", inline: "center", behavior: "smooth" });
      apply();
    };
    body.addEventListener("click", (e) => { const b = e.target.closest(".tt-node"); if (b) pick(b.dataset.id); });
    root.querySelector("#ttBr").onclick = (e) => {
      const b = e.target.closest("button"); if (!b) return;
      branch = b.dataset.b; root.querySelectorAll("#ttBr button").forEach((x) => x.classList.toggle("on", x === b)); apply();
    };
    root.querySelector("#ttQ").oninput = (e) => { q = e.target.value; apply(); };
    root.querySelector("#ttScroll").addEventListener("scroll", () => {}, { passive: true });
    new ResizeObserver(() => requestAnimationFrame(drawWires)).observe(body);
    window.addEventListener("wiki:tech", (e) => { if (TECH[e.detail]) pick(e.detail, true); });
    detail.innerHTML = `<p style="color:var(--muted)">Выберите технологию в дереве: подсветятся все её предшественники и всё, что она открывает.</p>`;
    apply();
    pages.tech._open = (id) => pick(id, true);
  }

  function iconFor(t) {
    return ({ Reactor: "reactor", Sensors: "sensors", Propulsion: "propulsion", Weapons: "cannon", Defense: "shielddome", Industry: "industry", Construction: "construction",
      Colonization: "planet", Society: "population", Doctrine: "fleet" })[t.cat] || "weapons";
  }

  pages.leaders = {
    title: "Лидеры и черты", icon: "leader",
    lead: "Учёные, адмиралы, губернаторы и агенты: до восьми одновременно.",
    body: () => `
      <p>Лидеров нанимают за <b>60 влияния</b>, а содержат за <b>1,5 гелия-3</b> в месяц каждого. Максимальный уровень — 5, опыт копится за дело: бои, исследования, управление и тайные операции. На каждого лидера приходится одна из должностей: корабль, совет, планета или шпионаж.</p>
      ${tbl(["Класс", "Должность", "Чем занимается"], [
        [`<span style="color:${CLS.scientist[1]}">${CLS.scientist[0]}</span>`, "Научный корабль · Совет", "Разведка, научный совет: каждый уровень даёт +5% к разведке и +2% к науке в совете."],
        [`<span style="color:${CLS.admiral[1]}">${CLS.admiral[0]}</span>`, "Флот", "Командует соединением: каждый уровень даёт +3% к урону и +2% к скорострельности."],
        [`<span style="color:${CLS.governor[1]}">${CLS.governor[0]}</span>`, "Планета · Совет", "Управляет планетой: черты влияют на стройку и выпуск ресурсов."],
        [`<span style="color:${CLS.agent[1]}">${CLS.agent[0]}</span>`, "Шпионаж", "Внедряется в чужую империю или ведёт контрразведку: каждый уровень даёт +4% к успеху операций."],
      ])}
      <h3>Черты</h3>
      <p>Каждый лидер появляется с чертой. Некоторые черты приходят как последствия событий; часть из них — отрицательные.</p>
      <div class="pills" id="trF"><button type="button" class="on" data-c="all">Все</button>${Object.entries(CLS).map(([k, v]) => `<button type="button" data-c="${k}">${v[0]}</button>`).join("")}</div>
      <div class="cards" id="trCards"></div>`,
    init(root) {
      const box = root.querySelector("#trCards");
      const draw = (c) => {
        box.innerHTML = D.traits.filter((t) => c === "all" || t.cls === c).map((t) =>
          `<div class="card" style="--acc:${t.negative ? "var(--danger)" : CLS[t.cls][1]}"><h4>${t.name}</h4><div class="eff">${CLS[t.cls][0]}${t.event ? " · из события" : ""}${t.negative ? " · недостаток" : ""}</div><p>${t.desc}</p></div>`).join("");
      };
      root.querySelector("#trF").onclick = (e) => {
        const b = e.target.closest("button"); if (!b) return;
        root.querySelectorAll("#trF button").forEach((x) => x.classList.toggle("on", x === b)); draw(b.dataset.c);
      };
      draw("all");
    },
  };

  const LM = [
    ["Врата Предтеч", "#8dcdff", "+25% к скорости гиперпрыжков всех ваших флотов", "Кольцо из чёрного металла вокруг двойной звезды."],
    ["Пульсар Ирис", "#6bfa9e", "+12 науки в месяц", "Нейтронная звезда, мигающая строго по расписанию."],
    ["Монумент Первых", "#ff8ca8", "+1,5 влияния в месяц", "Гигантский чёрный обелиск, обращённый к ядру галактики."],
    ["Цитадель Древних", "#ffa85c", "+25% корпуса, брони и щитов всех звёздных баз", "Брошенные орудийные платформы, послушные тем, кто знает коды."],
    ["Маяк Дальнего Света", "#ffeb73", "Обзор вокруг системы на 2 прыжка", "Свет этого маяка виден за сотни парсеков."],
    ["Кладезь Недр", "#61ebdc", "+10 гелия-3 и +10 титана в месяц", "Планетоид, выпотрошенный и набитый концентратом."],
  ];
  pages.landmarks = {
    title: "Узлы силы", icon: "starbase",
    lead: "Особые системы, которые дают бонус всей империи, пока там стоит ваш форпост.",
    body: () => `
      <p>Узлы силы расставляются при создании галактики: <b>4 на маленькой, 6 на средней, 9 на большой</b>. Они лежат далеко от столицы игрока (не ближе трёх прыжков) и друг от друга. Бонус действует, пока форпост в системе принадлежит вам, и пропадает вместе с ним, так что за узлы идёт война.</p>
      <div class="cards">${LM.map((l) => `<div class="card" style="--acc:${l[1]}">${ic("starbase")}<h4>${l[0]}</h4><div class="eff">${l[2]}</div><p>${l[3]}</p></div>`).join("")}</div>
      ${call("Археотехнология", "<p>Эта технология усиливает все бонусы узлов силы на 50%. Игроки, которые держат несколько узлов, часто берут её в первую очередь.</p>")}
      <h3>Как их находить</h3>
      <p>Узел виден на карте как значок на табличке системы, но только если система разведана. Окно «Империя» содержит вкладку «Узлы» со списком всех найденных, их владельцами и бонусами. ИИ тоже ценит узлы и строит форпосты рядом с ними.</p>`,
  };

  /* ============================== МИР ============================== */
  pages.empires = {
    title: "Империи и ИИ", icon: "leader",
    lead: "Пять государств и пять характеров, которые руководят искусственным интеллектом.",
    body: () => `
      <div class="cards">${D.factions.map((f) => `<div class="card" style="--acc:${f.leader.accent}"><div style="width:44px;height:44px;margin-bottom:10px;background:${f.leader.accent};-webkit-mask:url(img/emblems/${f.key}.png) center/contain no-repeat;mask:url(img/emblems/${f.key}.png) center/contain no-repeat"></div><h4>${f.name}</h4><div class="eff">${f.title}</div><p>${f.perks.join(" · ")}</p></div>`).join("")}</div>
      <h3>Характеры ИИ</h3>
      <p>Каждый ИИ получает личность, которая влияет на войну, дипломатию, расширение и отношение к вам. Личность видна по поведению: кого он любит, кого боится и чью границу тронет первой.</p>
      ${tbl(["Характер", "Поведение"], Object.values(D.personalities).map((p) => [p.name, p.summary]))}
      <h3>Память и мнение</h3>
      <p>ИИ помнит, что вы делали: дарили, нападали, воровали технологии, срывали сделки. Мнение растёт и падает по месяцам, а старые обиды угасают медленно. Шпионаж оставляет след: раскрытая операция портит мнение.</p>
      <h3>Сложность</h3>
      ${tbl(["Сложность", "Доход ИИ", "Агрессия", "Ваш старт"], [["Лёгкая", "×0,7", "×0,6", "×1,3"], ["Обычная", "×1", "×1", "×1"], ["Тяжёлая", "×1,5", "×1,5", "×0,85"]])}`,
  };

  pages.diplomacy = {
    title: "Дипломатия", icon: "trade",
    lead: "Пакты, мир, коалиции и Галактический совет.",
    body: () => `
      ${tbl(["Действие", "Цена влияния", "Что даёт"], [
        ["Пакт о ненападении", "25", "Империя не нападёт, а голос за вас на Совете становится вероятнее."],
        ["Мирный договор", "30", "Прекращает войну; после неё действует перемирие на 3 года."],
        ["Вступить в коалицию", "20", "Вы воюете вместе с участниками против цели коалиции."],
        ["Организовать коалицию", "40", "Вы собираете союз сами против любого соперника, готового присоединиться."],
      ], 1)}
      <h3>Коалиции</h3>
      <p>С 2206 года, если одна империя (в том числе вы) уходит далеко вперёд по очкам, соперники, которым она не нравится, объединяются против неё. Лидер должен опережать второго минимум в 1,3 раза, держать 40% суммарных очков всех империй и владеть не менее чем семью системами. Коалиция готовится три месяца, а потом, когда её флот не слабее 75% флота цели, объявляет войну. Её члены не воюют между собой. На лёгкой сложности коалиций нет.</p>
      <p>Если цель — ИИ, вы можете вступить в коалицию (20 влияния) или собрать свою против любого соперника (40 влияния). Выйти из неё или заключить сепаратный мир значит испортить отношения с союзниками.</p>
      ${call("Не лидируйте слишком явно", "<p>Чем дальше вы уходите вперёд по очкам, тем вероятнее коалиция. Держите хорошие отношения с ИИ: вступить против вас им труднее.</p>")}
      <h3>Галактический совет</h3>
      <p>Первое заседание Совета проходит в 2230 году и повторяется каждые шесть лет: 2230, 2236, 2242, 2248 и 2254. Если за вас проголосуют <b>две трети</b> голосов, вы побеждаете дипломатическим путём. За вас голосуют империи с пактом либо с хорошим мнением о вас (от 40) без войны, остальные голосуют против. Голоса зависят от числа систем и населения.</p>`,
  };

  pages.espionage = {
    title: "Шпионаж", icon: "sensors",
    lead: "Агенты проникают в чужие империи, воруют технологии и разжигают войны.",
    body: () => `
      <p>Агент — четвёртый класс лидеров. Его отправляют либо внедряться в чужую империю, либо на контрразведку дома. После внедрения у агента растёт прикрытие.</p>
      ${tbl(["Прикрытие", "Что открывается"], [["20%", "Обзор всей территории цели и её флотов"], ["50%", "Полное досье: запасы, доходы, наука и мощь"]], 0)}
      <h3>Операции</h3>
      ${tbl(["Операция", "Цена влияния", "Нужное прикрытие", "Месяцев", "Базовый шанс"], [
        ["Кража технологии", 20, "40%", 3, "40%"],
        ["Диверсия", 15, "25%", 2, "55%"],
        ["Раздор", 25, "50%", 3, "50%"],
      ], 1)}
      <ul>
        <li><b>Кража технологии:</b> агент изучает технологию, которая есть у цели и нет у вас.</li>
        <li><b>Диверсия:</b> цель теряет часть сплавов и гелия-3.</li>
        <li><b>Раздор:</b> две чужие империи ссорятся друг с другом, и война между ними становится вероятнее.</li>
      </ul>
      <p>Шанс успеха зависит от класса и уровня агента, его черт, прикрытия, ваших технологий и контрразведки цели. При провале агента либо ловят (скандал, падение отношений), либо он «сгорает» и теряет прикрытие.</p>
      <h3>Контрразведка</h3>
      <p>Агент дома и технологии шифрования снижают шансы чужих операций и раскрывают их. ИИ тоже шпионит: иногда пытается украсть вашу технологию или устроить диверсию, и это обходится ему в 15 влияния. Раскрытая операция показывает, чьих она рук дело.</p>`,
  };

  pages.threats = {
    title: "Угрозы и события", icon: "defense",
    lead: "Пираты, аномалии и случайные события, которые вмешиваются в партию.",
    body: () => `
      <h3>Пираты</h3>
      <p>Первые пиратские рейды появляются в 2206 (лёгкая), 2208 (обычная) и 2212 (тяжёлая сложность). Позже возможно событие «Пиратская гавань»: можно платить дань, объявить награду за пиратов или иначе на это ответить. Держите хотя бы небольшой флот для защиты систем.</p>
      <h3>События</h3>
      <p>Случайные события ставят перед выбором: открытие кристаллической жизни, чума, землетрясение, мятежный перебежчик или древние реликвии. У каждого события несколько вариантов ответа и свои последствия: ресурсы, черты лидеров, мнение ИИ.</p>
      <div class="cards">${[
        ["Дрейфующий корабль", "derelict"], ["Сигнал бедствия", "distress"], ["Реликвия", "relic"], ["Разлом", "rift"], ["Чума", "plague"],
        ["Прорыв", "breakthrough"], ["Коррупция", "corruption"], ["Перебежчик", "defector"],
      ].map(([t, i]) => `<div class="card" style="padding:0;overflow:hidden"><img src="img/events/${i}.webp" alt="" loading="lazy" style="aspect-ratio:16/9;object-fit:cover;width:100%"><div style="padding:12px 16px"><h4 style="margin:0">${t}</h4></div></div>`).join("")}</div>`,
  };

  /* ============================== ПАРТИЯ ============================== */
  pages.victory = {
    title: "Пути к победе", icon: "starbase",
    lead: "Шесть способов победить и пять способов проиграть. Победу нужно не только добиться, но и удержать.",
    body: () => {
      const V = [
        ["var(--cyan)", "starbase", "Доминирование", 0.55, "Форпосты в 47 / 83 / 127 системах (по размеру галактики) и удержание порога 12 месяцев. Соперники могут сбросить счётчик."],
        ["var(--green)", "scienceship", "Технологии", 0.74, "66 технологий из 89 и проект «Вознесение», который закрывает дерево. Без него победа невозможна."],
        ["var(--rose)", "weapons", "Военная", 1, "Все соперники повержены."],
        ["var(--gold)", "trade", "Экономика", 0.5, "50% валового продукта галактики 36 месяцев подряд."],
        ["var(--violet)", "leader", "Дипломатия", 0.67, "Две трети голосов Галактического совета (2230, 2236, 2242, 2248 или 2254)."],
        ["var(--amber)", "population", "По очкам", 0.9, "К 1 января 2260 больше очков, чем у каждого соперника."],
      ];
      return `
      <div class="vic">${V.map((v) => `<div class="vcard" style="--acc:${v[0]}"><div class="ring" style="--off:${214 * (1 - v[3])}"><svg viewBox="0 0 74 74"><circle class="bg" cx="37" cy="37" r="34"/><circle class="fg" cx="37" cy="37" r="34"/></svg>${ic(v[1])}</div><div><h4>${v[2]}</h4><p>${v[4]}</p></div></div>`).join("")}</div>
      <h3>Очки</h3>
      ${tbl(["Источник", "Очки"], [["Каждая система с форпостом", "10"], ["Каждый житель", "4"], ["Каждая технология", "15"], ["Военная мощь", "÷ 50"]], 1)}
      <h3>Поражение</h3>
      <ul>
        <li>Потеря всех систем.</li>
        <li>Банкротство дольше 6 месяцев подряд.</li>
        <li>Кто-то из ИИ добился доминирования, технологической или экономической победы раньше вас.</li>
        <li>У какого-то ИИ больше очков вас в 2240 году.</li>
      </ul>
      ${call("Темп партии", "<p>Победы по времени разнесены так, чтобы партия длилась 2–3 часа: быстрые победы возможны не раньше 2245–2255, а подсчёт очков идёт в 2260. Следите за экраном «Пути к победе» в игре: он показывает прогресс всех сторон.</p>")}`;
    },
    init(root) { root.querySelectorAll(".ring").forEach((r) => io.observe(r)); },
  };

  pages.settings = {
    title: "Настройки игры", icon: "construction",
    lead: "Что выбирается при создании новой партии.",
    body: () => `
      ${tbl(["Параметр", "Значения"], [
        ["Размер галактики", "85 / 150 / 230 звёзд"],
        ["Форма", "эллипс · спираль на 2 рукава · спираль на 4 рукава · кольцо"],
        ["Сложность", "лёгкая · обычная · тяжёлая"],
        ["Империя", "Астрея · Ксарн · Аквила · Иридия · Терраан"],
        ["Порог доминирования", "47 · 83 · 127 систем"],
        ["Узлы силы", "4 · 6 · 9"],
      ])}
      <h3>Сохранения</h3>
      <p>Игра сохраняется автоматически; ручное сохранение — на ${kbd("F5")}, загрузка — ${kbd("F9")}. Старые сохранения совместимы: узлы силы в них расставляются при загрузке, если их не было.</p>`,
  };

  pages.faq = {
    title: "Глоссарий и FAQ", icon: "defense",
    lead: "Короткие ответы на частые вопросы.",
    body: () => `
      <h3>Часто задаваемые вопросы</h3>
      ${[
        ["Почему не растут колонии?", "Скорее всего, не хватает жилья (городских районов) или влияния. Откройте окно планеты: нехватка подсвечена."],
        ["Что делать при банкротстве?", "Отмените часть заказов и продайте лишний флот. Пока ресурс в минусе, доходы режутся вдвое."],
        ["Какое оружие брать?", "Против щитов — кинетика, против брони — лазеры. Смешанный флот надёжнее всего."],
        ["Как найти узлы силы?", "Разведайте карту: найденные узлы показываются в окне «Империя» → «Узлы»."],
        ["Могу ли я выиграть без войны?", "Да. Технологическая, экономическая и дипломатическая победы не требуют завоеваний, но ИИ не будет стоять в стороне."],
      ].map(([q, a]) => `<h4>${q}</h4><p>${a}</p>`).join("")}
      <h3>Глоссарий</h3>
      ${tbl(["Термин", "Значение"], [
        ["Форпост", "Звёздная база: закрепляет систему за империей."],
        ["Район", "Здание на планете: городской, добывающий, энергетический, промышленный."],
        ["Прикрытие", "Внедрённость агента в чужой империи."],
        ["Узел силы", "Особая система с бонусом для владельца."],
        ["Коалиция", "Союз ИИ против сильнейшей державы."],
        ["Вознесение", "Завершающий проект технологического дерева."],
      ])}`,
  };

  // Наблюдатель за появлением (кольца и матрица урона)
  const io = new IntersectionObserver((es) => es.forEach((e) => { if (e.isIntersecting) { e.target.classList.add("go"); io.unobserve(e.target); } }), { threshold: 0.4 });

  window.WIKI = { groups, pages, TECH, BR, tname };
})();
