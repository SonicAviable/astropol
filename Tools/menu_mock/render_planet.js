// Варианты экрана планеты: node Tools/menu_mock/render_planet.js → planet_a/b/c.png
const { chromium } = require('playwright');
const path = require('path');
(async () => {
  const b = await chromium.launch();
  for (const v of ['a', 'b', 'c']) {
    const p = await b.newPage({ viewport: { width: 1920, height: 1080 } });
    await p.goto('file://' + path.join(__dirname, 'planet.html') + '#' + v);
    await p.evaluate(() => document.fonts.ready);
    await p.screenshot({ path: path.join(__dirname, `planet_${v}.png`) });
    await p.close();
  }
  await b.close();
})();
