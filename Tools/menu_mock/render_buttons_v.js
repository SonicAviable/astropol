// Варианты стиля кнопок в PNG: node Tools/menu_mock/render_buttons_v.js → Tools/menu_mock/buttons_variants.png
const { chromium } = require('playwright');
const path = require('path');
(async () => {
  const b = await chromium.launch();
  const p = await b.newPage({ viewport: { width: 1920, height: 700 } });
  await p.goto('file://' + path.join(__dirname, 'buttons_v.html'));
  await p.evaluate(() => document.fonts.ready);
  await p.screenshot({ path: path.join(__dirname, 'buttons_variants.png') });
  await b.close();
})();
