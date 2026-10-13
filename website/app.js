/* Astropolity: интерактив сайта (эффекты, разделы, вики). Данные — window.GAME_DATA, страницы вики — window.WIKI. */
(function () {
  "use strict";
  const D = window.GAME_DATA, W = window.WIKI;
  const $ = (s, r = document) => r.querySelector(s);
  const $$ = (s, r = document) => [...r.querySelectorAll(s)];
  const reduced = matchMedia("(prefers-reduced-motion: reduce)").matches;
  const fine = matchMedia("(pointer: fine)").matches;
  const clamp = (v, a, b) => Math.max(a, Math.min(b, v));
  const FMT = new Intl.NumberFormat("ru-RU");
  const ic = (name) => `<i class="ic" style="--i:url(img/icons/${name}.png)"></i>`;
  const set = (id, t) => { const e = document.getElementById(id); if (e) e.textContent = t; };

  ["ver", "ver2", "ver3"].forEach((i) => set(i, D.version));

  /* ---------- Заголовок по буквам ---------- */
  (function () {
    const h = $("#h1"); if (!h || reduced) return;
    let i = 0;
    const split = (node) => {
      [...node.childNodes].forEach((c) => {
        if (c.nodeType === 3) {
          const f = document.createDocumentFragment();
          [...c.textContent].forEach((ch) => { const s = document.createElement("span"); s.className = "ch"; s.style.setProperty("--n", i++); s.textContent = ch; f.appendChild(s); });
          c.replaceWith(f);
        } else if (c.nodeType === 1) split(c);
      });
    };
    split(h);
    h.setAttribute("aria-label", "Astropolity");
  })();

  /* ---------- Печатающаяся строка ---------- */
  (function () {
    const el = $("#typed"); if (!el) return;
    const lines = ["Колонизируйте миры.", "Проектируйте корабли.", "Воруйте чужие технологии.", "Собирайте коалиции.", "Вознесите свою империю."];
    if (reduced) { el.textContent = lines.join(" "); el.style.setProperty("--x", 0); return; }
    let li = 0, ci = 0, dir = 1, wait = 0;
    setInterval(() => {
      if (wait > 0) { wait--; return; }
      ci += dir;
      el.textContent = lines[li].slice(0, ci);
      if (dir > 0 && ci >= lines[li].length) { dir = -1; wait = 24; }
      else if (dir < 0 && ci <= 0) { dir = 1; li = (li + 1) % lines.length; wait = 4; }
    }, 55);
  })();

  /* ---------- Часы партии ---------- */
  (function () {
    const dateEl = $("#clockDate"), bar = $("#clockBar");
    if (!dateEl) return;
    let day = 0, speed = 1, acc = 0, last = performance.now();
    const res = { E: 400, M: 500, A: 250, I: 100 };
    const pad = (v) => String(v).padStart(2, "0");
    const draw = () => {
      const y = 2200 + Math.floor(day / 360), d = day % 360;
      dateEl.textContent = `${pad((d % 30) + 1)} · ${pad(Math.floor(d / 30) + 1)} · ${y}`;
      bar.style.width = (day / (60 * 360)) * 100 + "%";
      set("rE", FMT.format(Math.round(res.E))); set("rM", FMT.format(Math.round(res.M)));
      set("rA", FMT.format(Math.round(res.A))); set("rI", FMT.format(Math.round(res.I)));
    };
    $$(".speeds button").forEach((b) => (b.onclick = () => { speed = +b.dataset.s; $$(".speeds button").forEach((x) => x.classList.toggle("on", x === b)); }));
    draw();
    if (reduced) return;
    const tick = (now) => {
      acc += (now - last) / 1600 * speed; last = now;
      const whole = Math.floor(acc);
      if (whole > 0) {
        acc -= whole;
        for (let i = 0; i < whole; i++) {
          day++;
          if (day % 30 === 0) { res.E += 15; res.M += 10; res.A += 5; res.I += 3; }
          if (day >= 60 * 360) { day = 0; res.E = 400; res.M = 500; res.A = 250; res.I = 100; }
        }
        draw();
      }
      requestAnimationFrame(tick);
    };
    requestAnimationFrame(tick);
  })();

  /* ---------- Счётчики в герое ---------- */
  (function () {
    const box = $("#stats"); if (!box) return;
    const items = [[5, "империй"], [60, "лет партии"], [D.techs.length, "технологий"], [230, "звёзд на карте"], [D.traits.length, "черт лидеров"], [D.modules.length, "модулей кораблей"]];
    box.innerHTML = items.map(([v, l]) => `<div class="stat"><b data-v="${v}">0</b><span>${l}</span></div>`).join("");
    const run = (el) => {
      const to = +el.dataset.v, t0 = performance.now(), dur = reduced ? 1 : 1400;
      const f = (t) => { const p = clamp((t - t0) / dur, 0, 1); el.textContent = Math.round(to * (1 - Math.pow(1 - p, 3))); if (p < 1) requestAnimationFrame(f); };
      requestAnimationFrame(f);
    };
    const io = new IntersectionObserver((es) => es.forEach((e) => { if (e.isIntersecting) { run(e.target); io.unobserve(e.target); } }), { threshold: 0.6 });
    $$("b", box).forEach((b) => io.observe(b));
  })();

  /* ---------- Галактика на canvas ---------- */
  (function () {
    const cv = $("#galaxy"); if (!cv) return;
    const ctx = cv.getContext("2d"); if (!ctx) return;
    let w = 0, h = 0, dpr = 1, mx = 0, my = 0, tx = 0, ty = 0, visible = true, t0 = performance.now();
    const stars = [], pulses = [];
    const cols = ["#4df2db", "#bfefff", "#ffffff", "#ffcc52", "#bd8fff"];
    const N = innerWidth < 700 ? 700 : 1500;
    for (let i = 0; i < N; i++) {
      const arm = i % 2, r = Math.pow(Math.random(), 0.7);
      const ang = r * 5.2 + arm * Math.PI + (Math.random() - 0.5) * (0.75 - r * 0.4);
      stars.push({ a: ang, r, z: Math.random(), s: 0.4 + Math.random() * 1.4, c: cols[(Math.random() * (Math.random() < 0.8 ? 3 : 5)) | 0], tw: Math.random() * 6.28 });
    }
    const resize = () => {
      dpr = Math.min(devicePixelRatio || 1, 2);
      const r = cv.getBoundingClientRect(); w = r.width; h = r.height;
      cv.width = w * dpr; cv.height = h * dpr; ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
      if (reduced) frame(performance.now());
    };
    const pos = (s, rot) => {
      const R = s.r * Math.min(w, h * 1.7) * 0.62, a = s.a + rot * (1.4 - s.r);
      return [Math.cos(a) * R, Math.sin(a) * R * 0.46];
    };
    const frame = (now) => {
      const t = (now - t0) / 1000, rot = reduced ? 0.4 : t * 0.018;
      tx += (mx - tx) * 0.05; ty += (my - ty) * 0.05;
      ctx.clearRect(0, 0, w, h);
      const cx = w * 0.62, cy = h * 0.5;
      const g = ctx.createRadialGradient(cx, cy, 0, cx, cy, Math.min(w, h * 1.7) * 0.34);
      g.addColorStop(0, "rgba(255,204,82,0.22)"); g.addColorStop(0.35, "rgba(77,242,219,0.07)"); g.addColorStop(1, "rgba(0,0,0,0)");
      ctx.fillStyle = g; ctx.fillRect(0, 0, w, h);
      ctx.save(); ctx.translate(cx + tx * 14, cy + ty * 10); ctx.rotate(-0.22);
      for (const s of stars) {
        const [x, y] = pos(s, rot);
        const px = x + tx * 26 * (s.z - 0.4), py = y + ty * 18 * (s.z - 0.4);
        const tw = 0.55 + 0.45 * Math.sin(t * 1.6 + s.tw);
        ctx.globalAlpha = (0.25 + s.z * 0.7) * tw * (1 - s.r * 0.3);
        ctx.fillStyle = s.c;
        ctx.fillRect(px, py, s.s, s.s);
      }
      // сигналы: расходящиеся кольца
      for (let i = pulses.length - 1; i >= 0; i--) {
        const p = pulses[i], k = (now - p.t) / 2600;
        if (k >= 1) { pulses.splice(i, 1); continue; }
        ctx.globalAlpha = (1 - k) * 0.7; ctx.strokeStyle = p.c; ctx.lineWidth = 1.2;
        ctx.beginPath(); ctx.ellipse(p.x, p.y, 4 + k * 70, (4 + k * 70) * 0.46, 0, 0, 6.283); ctx.stroke();
      }
      ctx.restore(); ctx.globalAlpha = 1;
      if (!reduced && visible && Math.random() < 0.02 && pulses.length < 4) {
        const s = stars[(Math.random() * stars.length) | 0], [x, y] = pos(s, rot);
        pulses.push({ x, y, t: now, c: Math.random() < 0.5 ? "#4df2db" : "#ffcc52" });
      }
      if (!reduced && visible) requestAnimationFrame(frame);
    };
    resize(); addEventListener("resize", resize);
    if (fine) addEventListener("pointermove", (e) => { mx = e.clientX / innerWidth - 0.5; my = e.clientY / innerHeight - 0.5; }, { passive: true });
    new IntersectionObserver((es) => { visible = es[0].isIntersecting; if (visible && !reduced) requestAnimationFrame(frame); }).observe(cv);
    if (reduced) frame(performance.now()); else requestAnimationFrame(frame);

    // планета слегка смещается при скролле
    const planet = $("#heroPlanet");
    if (planet && !reduced) addEventListener("scroll", () => { planet.style.translate = `0 ${Math.min(scrollY, 900) * -0.12}px`; }, { passive: true });
  })();

  /* ---------- Таймлайн партии ---------- */
  (function () {
    const root = $("#tlIn"), wrap = $("#tl"); if (!root) return;
    const X = (y) => 4 + ((y - 2200) / 60) * 92;
    const C = { game: "#4df2db", threat: "#ff7a94", tech: "#ffcc52", diplo: "#bd8fff", win: "#66f59e" };
    const EV = [
      [2200, "Старт партии", "Столица, 400 гелия-3, 500 титана и шесть поселенцев.", "game", "up1", "first"],
      [2206, "Первые пираты и коалиции", "На лёгкой сложности пираты приходят в 2206-м; ИИ уже могут сплотиться против лидера.", "threat", "dn1"],
      [2208, "Эпоха II технологий", "22 технологии: лазеры II, щиты II, гипердвигатели.", "tech", "up2"],
      [2212, "Пираты на тяжёлой", "Первые рейды на тяжёлой сложности. Обычная начинается с 2208.", "threat", "dn2"],
      [2216, "Эпоха III", "Плазма, антиматерия, архитектура колоний.", "tech", "up1"],
      [2229, "Эпоха IV", "Мегаструктуры, червоточины, ИИ-ядро.", "tech", "dn1"],
      [2230, "Галактический совет", "Первое заседание. Следующие: 2236, 2242, 2248, 2254. Нужны две трети голосов.", "diplo", "up2"],
      [2240, "Проверка очков", "Если у какого-то ИИ очков больше, чем у вас, партия проиграна.", "threat", "dn2"],
      [2242, "Эпоха V · Вознесение", "Венец дерева технологий. Без него технологическая победа невозможна.", "tech", "up1"],
      [2250, "Окно побед", "Доминирование, наука, экономика: ранние победы возможны с 2245 по 2255.", "win", "up2"],
      [2260, "Подсчёт очков", "1 января 2260 года побеждает набравший больше всех.", "win", "dn1", "last"],
    ];
    const lanes = { up1: [274, true], up2: [390, true], dn1: [270, false], dn2: [386, false] };
    let html = `<div class="tl-axis"></div><div class="tl-fill" id="tlFill"></div><div class="tl-head" id="tlHead"></div>`;
    for (let y = 2200; y <= 2260; y += 10) html += `<span class="tl-yr big" style="left:${X(y)}%">${y}</span>`;
    for (let y = 2200; y <= 2260; y += 2) html += `<span class="tl-tick" style="left:${X(y)}%"></span>`;
    for (let y = 2230; y < 2260; y += 6) html += `<span class="tl-tick" style="left:${X(y)}%;height:18px;top:calc(var(--ax) - 14px);background:#bd8fff"></span>`;
    EV.forEach(([y, t, d, c, lane, edge]) => {
      const [off, up] = lanes[lane], pos = up ? `bottom:${off}px` : `top:${off}px`;
      html += `<span class="tl-dot" data-y="${y}" style="left:${X(y)}%;--c:${C[c]}"></span>`;
      html += `<div class="tl-ev ${up ? "up" : "dn"} ${edge || ""}" data-y="${y}" style="left:${X(y)}%;${pos};--c:${C[c]};--stem:${up ? off - 240 : off - 240}px"><span class="y">${y}</span><h4>${t}</h4><p>${d}</p></div>`;
    });
    root.innerHTML = html;
    $("#tlLegend").innerHTML = [["game", "ход игры"], ["threat", "угрозы"], ["tech", "эпохи технологий"], ["diplo", "дипломатия"], ["win", "победа"]].map(([k, v]) => `<span><i style="background:${C[k]}"></i>${v}</span>`).join("");
    const fill = $("#tlFill"), head = $("#tlHead"), evs = $$(".tl-ev", root), dots = $$(".tl-dot", root);
    const upd = () => {
      const r = wrap.getBoundingClientRect(), vh = innerHeight;
      const p = reduced ? 1 : clamp((vh * 0.78 - r.top) / (r.height * 0.9 + vh * 0.25), 0, 1);
      const x = 4 + p * 92;
      fill.style.width = x + "%"; head.style.left = x + "%";
      const yr = 2200 + p * 60;
      evs.forEach((e) => e.classList.toggle("lit", +e.dataset.y <= yr + 0.5));
      dots.forEach((e) => e.classList.toggle("lit", +e.dataset.y <= yr + 0.5));
    };
    addEventListener("scroll", upd, { passive: true }); addEventListener("resize", upd); upd();
  })();

  /* ---------- Империи ---------- */
  (function () {
    const grid = $("#facGrid"), dlg = $("#facDlg"); if (!grid) return;
    grid.innerHTML = D.factions.map((f) => `
      <button type="button" class="fac reveal" style="--acc:${f.leader.accent};--e:url(img/emblems/${f.key}.png)" data-k="${f.key}" aria-label="${f.name}">
        <img class="port" src="img/leaders/${f.key}.webp" alt="" loading="lazy" width="480" height="720">
        <span class="emb" aria-hidden="true"></span>
        <span class="meta"><span class="ttl">${f.title}</span><h3>${f.name}</h3><span class="perks">${f.perks.slice(0, 2).map((p) => `<span class="chip">${p}</span>`).join("")}${f.perks.length > 2 ? `<span class="chip">+${f.perks.length - 2}</span>` : ""}</span></span>
      </button>`).join("");
    $$(".fac", grid).forEach((c) => {
      if (fine && !reduced) {
        c.addEventListener("pointermove", (e) => {
          const r = c.getBoundingClientRect(), x = (e.clientX - r.left) / r.width, y = (e.clientY - r.top) / r.height;
          c.style.transform = `perspective(900px) rotateY(${(x - 0.5) * 9}deg) rotateX(${(0.5 - y) * 9}deg) translateY(-4px)`;
          c.style.setProperty("--mx", x * 100 + "%"); c.style.setProperty("--my", y * 100 + "%");
        });
        c.addEventListener("pointerleave", () => (c.style.transform = ""));
      }
      c.onclick = () => {
        const f = D.factions.find((x) => x.key === c.dataset.k);
        dlg.style.setProperty("--acc", f.leader.accent);
        dlg.innerHTML = `<button type="button" class="dlg-x" aria-label="Закрыть">×</button>
          <div class="fd"><div class="fd-pic"><img src="img/leaders/${f.key}.webp" alt="${f.leader.name}"></div>
          <div class="fd-body">
            <span class="eyebrow" style="color:${f.leader.accent}">${f.title}</span>
            <h3>${f.name}</h3>
            <div class="perks" style="display:flex;gap:6px;flex-wrap:wrap;--acc:${f.leader.accent}">${f.perks.map((p) => `<span class="chip">${p}</span>`).join("")}</div>
            <p>${f.lore}</p>
            <blockquote class="fd-quote" style="margin:0">«${f.leader.quote}»<cite>${f.leader.name} — ${f.leader.title}</cite></blockquote>
            <a class="btn sm" href="#wiki-empires" data-close>Об ИИ и характерах</a>
          </div></div>`;
        dlg.showModal();
        dlg.querySelector(".dlg-x").onclick = () => dlg.close();
        dlg.querySelector("[data-close]").onclick = () => dlg.close();
      };
    });
    dlg.addEventListener("click", (e) => { if (e.target === dlg) dlg.close(); });
  })();

  /* ---------- Возможности ---------- */
  (function () {
    const g = $("#featGrid"); if (!g) return;
    const F = [
      ["planet", "var(--cyan)", "w8", "Процедурная галактика", "От 85 до 230 звёзд, четыре формы: эллипс, спираль на два и четыре рукава, кольцо. Туман войны скрывает всё, что вы не разведали.", "economy"],
      ["population", "var(--green)", "", "Колонии и районы", "Четыре типа районов, жильё и рабочие места. Планета растёт так, как вы её спланируете.", "colonies"],
      ["frigate", "var(--rose)", "", "Свои корабли", "Три корпуса, 17 модулей, три типа урона. Собирайте проект под врага, а не под учебник.", "fleet"],
      ["scienceship", "var(--gold)", "w6", "89 технологий", "Три ветви, пять эпох и венец — проект «Вознесение». Дерево с подсветкой цепочек можно изучить прямо здесь.", "tech"],
      ["leader", "var(--violet)", "", "Лидеры", "Учёные, адмиралы, губернаторы и агенты. 24 черты, у каждой свой смысл.", "leaders"],
      ["sensors", "var(--violet)", "", "Шпионаж", "Внедряйтесь в чужие империи, воруйте технологии, стравливайте соседей.", "espionage"],
      ["starbase", "var(--amber)", "w6", "Узлы силы", "Врата, пульсары, монументы. Шесть особых систем дают бонус всей империи, пока там стоит ваш форпост.", "landmarks"],
      ["trade", "var(--cyan)", "", "ИИ с памятью", "Пять характеров, мнение, обиды, коалиции против лидера. Обманывать ИИ долго не получится.", "empires"],
      ["defense", "var(--rose)", "", "Угрозы и события", "Пираты, аномалии, чума и древние реликвии вмешиваются в партию.", "threats"],
    ];
    g.innerHTML = F.map((f, i) => `<article class="feat ${f[2]} reveal" style="--acc:${f[1]};--d:${(i % 3) * 90}ms">${ic(f[0])}<h3>${f[3]}</h3><p>${f[4]}</p><a class="more" href="#wiki-${f[5]}">В вики →</a></article>`).join("") +
      `<article class="feat pic w6 reveal"><div class="in"><h3>Шесть путей к победе</h3><p style="color:var(--text)">Доминирование, наука, экономика, дипломатия, война и очки. И ни одна не даётся даром.</p><a class="more" href="#wiki-victory">Условия победы →</a></div></article>`;
    if (fine && !reduced) g.addEventListener("pointermove", (e) => {
      const c = e.target.closest(".feat"); if (!c) return;
      const r = c.getBoundingClientRect(); c.style.setProperty("--mx", e.clientX - r.left + "px"); c.style.setProperty("--my", e.clientY - r.top + "px");
    });
  })();

  /* ---------- Цикл ---------- */
  (function () {
    const el = $("#loopEl"); if (!el) return;
    const S = [["sensors", "Разведка", "Научный корабль открывает системы, залежи и узлы силы."], ["planet", "Расширение", "Форпост, затем колония. Влияние ограничивает темп."], ["industry", "Развитие", "Районы, наука, лидеры. Каждая технология — рывок."], ["fleet", "Сила", "Свой проект корабля, верный состав и звёздные базы."], ["trade", "Политика", "Пакты, шпионы, коалиции и голоса на Совете."]];
    el.innerHTML = S.map((s, i) => `<div class="step reveal" style="--d:${i * 90}ms"><div class="orb">${ic(s[0])}</div><div><h3>${i + 1}. ${s[1]}</h3><p>${s[2]}</p></div></div>`).join("");
  })();

  /* ---------- Сцены ---------- */
  (function () {
    const reel = $("#reel"), dlg = $("#shotDlg"); if (!reel) return;
    const S = [["derelict", "Дрейфующий корабль"], ["distress", "Сигнал бедствия"], ["relic", "Реликвия Предтеч"], ["rift", "Пространственный разлом"], ["crystal", "Кристаллический разум"], ["amoeba", "Космическая амёба"], ["probe", "Чужой зонд"], ["plague", "Чума в колонии"], ["quake", "Землетрясение"], ["breakthrough", "Научный прорыв"], ["festival", "Фестиваль"], ["trade", "Торговое предложение"], ["defector", "Перебежчик"], ["corruption", "Коррупция"], ["drills", "Буровые установки"], ["aging", "Старение колонии"]];
    reel.innerHTML = S.map((s) => `<button type="button" class="shot" data-i="${s[0]}"><img src="img/events/${s[0]}.webp" alt="" loading="lazy" width="640" height="360"><span>${s[1]}</span></button>`).join("");
    reel.addEventListener("click", (e) => {
      const b = e.target.closest(".shot"); if (!b) return;
      dlg.innerHTML = `<button type="button" class="dlg-x" aria-label="Закрыть">×</button><img src="img/events/${b.dataset.i}.webp" alt="${b.textContent}"><p>${b.textContent}</p>`;
      dlg.showModal(); dlg.querySelector(".dlg-x").onclick = () => dlg.close();
    });
    dlg.addEventListener("click", (e) => { if (e.target === dlg) dlg.close(); });
    // колесо мыши по ленте — горизонтальная прокрутка, пока лента не упёрлась
    reel.addEventListener("wheel", (e) => {
      if (Math.abs(e.deltaY) <= Math.abs(e.deltaX)) return;
      const max = reel.scrollWidth - reel.clientWidth;
      if ((e.deltaY > 0 && reel.scrollLeft < max - 2) || (e.deltaY < 0 && reel.scrollLeft > 2)) { reel.scrollLeft += e.deltaY; e.preventDefault(); }
    }, { passive: false });
  })();

  /* ---------- Вики ---------- */
  const wiki = (function () {
    const side = $("#side"), main = $("#wikiMain"); if (!side) return null;
    const order = W.groups.flatMap((g) => g[1]);
    const inited = new Set();
    let cur = null;
    side.innerHTML = `<button type="button" class="search-btn side-search" id="sideSearch"><span>Поиск по вики</span><kbd>/</kbd></button>` +
      W.groups.map(([name, ids]) => `<div class="side-group"><h4>${name}</h4>${ids.map((id) => `<button type="button" data-p="${id}">${ic(W.pages[id].icon)}<span>${W.pages[id].title}</span></button>`).join("")}</div>`).join("");
    main.innerHTML = order.map((id) => {
      const p = W.pages[id], i = order.indexOf(id), prev = W.pages[order[i - 1]], next = W.pages[order[i + 1]];
      return `<article class="wp" id="wp-${id}" data-p="${id}"><header class="wp-head"><span class="eyebrow">${W.groups.find((g) => g[1].includes(id))[0]}</span><h2>${p.title}</h2><p>${p.lead}</p></header>${p.body()}
        <nav class="wp-nav">${prev ? `<button type="button" data-p="${order[i - 1]}"><small>← Назад</small>${prev.title}</button>` : "<span></span>"}${next ? `<button type="button" data-p="${order[i + 1]}"><small>Далее →</small>${next.title}</button>` : ""}</nav></article>`;
    }).join("");
    const show = (id, scroll) => {
      if (!W.pages[id]) id = "start";
      $$(".wp", main).forEach((p) => p.classList.toggle("on", p.dataset.p === id));
      $$("button[data-p]", side).forEach((b) => b.classList.toggle("on", b.dataset.p === id));
      const panel = $("#wp-" + id);
      if (!inited.has(id)) { inited.add(id); W.pages[id].init && W.pages[id].init(panel); }
      cur = id;
      if (scroll) {
        const top = main.getBoundingClientRect().top + scrollY - 84;
        if (scrollY > top || main.getBoundingClientRect().top > innerHeight * 0.5) scrollTo({ top, behavior: reduced ? "auto" : "smooth" });
      }
    };
    const go = (id, scroll = true) => { try { history.replaceState(null, "", "#wiki-" + id); } catch (_) { /* во встроенном окне адрес не меняется */ } show(id, scroll); };
    document.addEventListener("click", (e) => {
      const b = e.target.closest("[data-p]"); if (!b || !(side.contains(b) || main.contains(b))) return;
      go(b.dataset.p);
    });
    $("#sideSearch").onclick = () => palette.open();
    show("start", false);
    return { go, show, get cur() { return cur; } };
  })();

  /* ---------- Маршрутизация по #wiki-… ---------- */
  function route(initial) {
    const m = /^#wiki-([a-z]+)$/.exec(location.hash);
    if (!m || !wiki) return;
    wiki.show(m[1], false);
    const el = $("#wikiMain"); if (!el) return;
    const go = () => scrollTo({ top: el.getBoundingClientRect().top + scrollY - 84, behavior: reduced || initial ? "auto" : "smooth" });
    initial ? setTimeout(go, 150) : go();
  }
  addEventListener("hashchange", () => route(false));

  /* ---------- Поиск ---------- */
  const palette = (function () {
    const pal = $("#pal"), inp = $("#palIn"), list = $("#palList"); if (!pal) return {};
    const idx = [];
    Object.entries(W.pages).forEach(([id, p]) => {
      idx.push({ k: "Раздел", t: p.title, d: p.lead, go: () => wiki.go(id) });
      const tmp = document.createElement("div"); tmp.innerHTML = p.body();
      $$("h3", tmp).forEach((h) => idx.push({ k: p.title, t: h.textContent, d: "", go: () => wiki.go(id) }));
    });
    D.techs.forEach((t) => idx.push({ k: "Технология", t: t.name, d: t.desc, go: () => { wiki.go("tech"); setTimeout(() => W.pages.tech._open && W.pages.tech._open(t.id), 80); } }));
    D.modules.forEach((m) => idx.push({ k: "Модуль", t: m.name, d: m.desc, go: () => wiki.go("fleet") }));
    D.hulls.forEach((m) => idx.push({ k: "Корпус", t: m.name, d: `корпус ${m.hull}, броня ${m.armor}, щиты ${m.shields}`, go: () => wiki.go("fleet") }));
    D.traits.forEach((m) => idx.push({ k: "Черта", t: m.name, d: m.desc, go: () => wiki.go("leaders") }));
    D.factions.forEach((f) => idx.push({ k: "Империя", t: f.name, d: f.title, go: () => wiki.go("empires") }));
    let items = [], sel = 0;
    const esc = (s) => s.replace(/[&<>]/g, (c) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;" })[c]);
    const hl = (s, q) => { const i = q ? s.toLowerCase().indexOf(q) : -1; return i < 0 ? esc(s) : esc(s.slice(0, i)) + "<mark>" + esc(s.slice(i, i + q.length)) + "</mark>" + esc(s.slice(i + q.length)); };
    const render = () => {
      const q = inp.value.trim().toLowerCase();
      items = (q ? idx.map((x) => {
        const ti = x.t.toLowerCase().indexOf(q), di = x.d.toLowerCase().indexOf(q);
        return { x, s: ti === 0 ? 3 : ti > 0 ? 2 : di >= 0 ? 1 : 0 };
      }).filter((r) => r.s).sort((a, b) => b.s - a.s).map((r) => r.x) : idx.filter((x) => x.k === "Раздел")).slice(0, 40);
      sel = 0;
      list.innerHTML = items.length ? items.map((x, i) => `<li role="option" data-i="${i}" class="${i === 0 ? "on" : ""}"><span class="k">${esc(x.k)}</span><div><b>${hl(x.t, q)}</b>${x.d ? `<small>${hl(x.d.slice(0, 110), q)}</small>` : ""}</div></li>`).join("") : `<li class="pal-empty" style="display:block">Ничего не найдено</li>`;
    };
    const mark = () => $$("li", list).forEach((li, i) => { li.classList.toggle("on", i === sel); if (i === sel) li.scrollIntoView({ block: "nearest" }); });
    const open = () => { pal.classList.add("open"); inp.value = ""; render(); inp.focus(); document.body.style.overflow = "hidden"; };
    const close = () => { pal.classList.remove("open"); document.body.style.overflow = ""; };
    const choose = (i) => { const x = items[i]; if (!x) return; close(); x.go(); };
    inp.oninput = render;
    inp.onkeydown = (e) => {
      if (e.key === "ArrowDown") { sel = Math.min(items.length - 1, sel + 1); mark(); e.preventDefault(); }
      else if (e.key === "ArrowUp") { sel = Math.max(0, sel - 1); mark(); e.preventDefault(); }
      else if (e.key === "Enter") choose(sel);
    };
    list.onclick = (e) => { const li = e.target.closest("li[data-i]"); if (li) choose(+li.dataset.i); };
    pal.onclick = (e) => { if (e.target === pal) close(); };
    addEventListener("keydown", (e) => {
      const typing = /input|textarea|select/i.test(document.activeElement && document.activeElement.tagName);
      if (e.key === "/" && !typing && !pal.classList.contains("open")) { e.preventDefault(); open(); }
      else if ((e.key === "k" || e.key === "K") && (e.ctrlKey || e.metaKey)) { e.preventDefault(); pal.classList.contains("open") ? close() : open(); }
      else if (e.key === "Escape" && pal.classList.contains("open")) close();
    });
    $("#searchBtn").onclick = (e) => { e.preventDefault(); $("#navLinks").classList.remove("open"); open(); };
    return { open, close };
  })();

  /* ---------- Навигация, прогресс, появление ---------- */
  (function () {
    const nav = $("#nav"), prog = $("#progress"), top = $("#toTop"), links = $("#navLinks"), burger = $("#burger"), glow = $("#glow");
    let lastY = 0;
    const onScroll = () => {
      const y = scrollY, max = document.documentElement.scrollHeight - innerHeight;
      prog.style.width = (max > 0 ? (y / max) * 100 : 0) + "%";
      nav.classList.toggle("hide", y > lastY + 6 && y > 400 && !links.classList.contains("open"));
      if (y < lastY - 6 || y < 400) nav.classList.remove("hide");
      lastY = y;
      top.classList.toggle("show", y > 900);
    };
    addEventListener("scroll", onScroll, { passive: true }); onScroll();
    top.onclick = () => scrollTo({ top: 0, behavior: reduced ? "auto" : "smooth" });
    burger.onclick = () => { const o = links.classList.toggle("open"); burger.setAttribute("aria-expanded", o); };
    links.addEventListener("click", (e) => { if (e.target.closest("a")) links.classList.remove("open"); });
    if (fine && !reduced) addEventListener("pointermove", (e) => { glow.style.transform = `translate(${e.clientX}px,${e.clientY}px)`; }, { passive: true });
    else glow.style.display = "none";

    // активный раздел
    const secs = ["timeline", "factions", "features", "scenes", "wiki"].map((id) => document.getElementById(id));
    const so = new IntersectionObserver((es) => es.forEach((e) => {
      if (e.isIntersecting) $$("a", links).forEach((a) => a.classList.toggle("on", a.getAttribute("href") === "#" + e.target.id));
    }), { rootMargin: "-40% 0px -55% 0px" });
    secs.forEach((s) => s && so.observe(s));

    // появление при прокрутке: скрываем только то, что ниже первого экрана
    if (!reduced && "IntersectionObserver" in window) {
      const io = new IntersectionObserver((es) => es.forEach((e) => { if (e.isIntersecting) { e.target.classList.remove("pre"); io.unobserve(e.target); } }), { threshold: 0.12 });
      $$(".reveal").forEach((el) => { if (el.getBoundingClientRect().top > innerHeight * 0.95) { el.classList.add("pre"); io.observe(el); } });
    }
  })();

  route(true);
})();
