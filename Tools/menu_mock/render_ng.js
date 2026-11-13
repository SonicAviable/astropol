// Макеты экрана «Новая игра» в PNG: node Tools/menu_mock/render_ng.js  →  Tools/menu_mock/newgame_a.png, _b, _c
const { chromium } = require('playwright');
const path = require('path');
(async () => {
  const b = await chromium.launch();
  const p = await b.newPage({ viewport: { width: 1920, height: 1080 } });
  for (const v of (process.argv[2] ? process.argv.slice(2) : ['a', 'b', 'c'])) {
    await p.goto('file://' + path.join(__dirname, 'newgame.html') + '?v=' + v);
    await p.waitForFunction(() => document.body.dataset.ready);
    await p.waitForTimeout(300);
    await p.screenshot({ path: path.join(__dirname, 'newgame_' + v + '.png') });
  }
  await b.close();
})();
