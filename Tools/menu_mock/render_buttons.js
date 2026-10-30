// Шпаргалка кнопок в PNG: node Tools/menu_mock/render_buttons.js → Tools/menu_mock/buttons.png
const { chromium } = require('playwright');
const path = require('path');
(async () => {
  const b = await chromium.launch();
  const p = await b.newPage({ viewport: { width: 1400, height: 640 } });
  await p.goto('file://' + path.join(__dirname, 'buttons.html'));
  await p.evaluate(() => document.fonts.ready);
  await p.screenshot({ path: path.join(__dirname, 'buttons.png') });
  await b.close();
})();
