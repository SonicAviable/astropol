// Макеты экрана «Управление колониями» в PNG: node Tools/menu_mock/render_colonies.js  →  Tools/menu_mock/colonies_a.png, _b, _c
const { chromium } = require('playwright');
const path = require('path');
(async () => {
  const b = await chromium.launch();
  const p = await b.newPage({ viewport: { width: 1920, height: 1080 } });
  for (const v of (process.argv[2] ? process.argv.slice(2) : ['a', 'b', 'c'])) {
    await p.goto('file://' + path.join(__dirname, 'colonies.html') + '?v=' + v);
    await p.evaluate(() => document.fonts.ready); await p.waitForTimeout(300);
    await p.waitForTimeout(300);
    await p.screenshot({ path: path.join(__dirname, 'colonies_' + v + '.png') });
  }
  await b.close();
})();
