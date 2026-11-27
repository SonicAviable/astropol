// Макеты экрана «Новая игра» в PNG: node Tools/menu_mock/render_load.js  →  Tools/menu_mock/load_a.png, _b, _c
const { chromium } = require('playwright');
const path = require('path');
(async () => {
  const b = await chromium.launch();
  const p = await b.newPage({ viewport: { width: 1920, height: 1080 } });
  for (const v of (process.argv[2] ? process.argv.slice(2) : ['a', 'b', 'c'])) {
    await p.goto('file://' + path.join(__dirname, 'load.html') + '?v=' + v);
    await p.evaluate(() => document.fonts.ready); await p.waitForTimeout(300);
    await p.waitForTimeout(300);
    await p.screenshot({ path: path.join(__dirname, 'load_' + v + '.png') });
  }
  await b.close();
})();
