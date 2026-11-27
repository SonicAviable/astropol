// Варианты экрана планеты: node Tools/menu_mock/render_planet2.js → planet2_d/e/f.png
const { chromium } = require('playwright');
const path = require('path');
(async () => {
  const b = await chromium.launch();
  for (const v of ['d', 'e', 'f']) {
    const p = await b.newPage({ viewport: { width: 1920, height: 1080 } });
    await p.goto('file://' + path.join(__dirname, 'planet2.html') + '#' + v);
    await p.evaluate(() => document.fonts.ready);
    await p.screenshot({ path: path.join(__dirname, `planet2_${v}.png`) });
    await p.close();
  }
  await b.close();
})();
