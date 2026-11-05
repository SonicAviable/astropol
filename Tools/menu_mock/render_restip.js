// Макеты подсказки ресурса: node Tools/menu_mock/render_restip.js → Tools/menu_mock/restip.png
const { chromium } = require('playwright');
const path = require('path');
(async () => {
  const b = await chromium.launch();
  const p = await b.newPage({ viewport: { width: 1920, height: 1080 } });
  await p.goto('file://' + path.join(__dirname, 'restip.html'));
  await p.evaluate(() => document.fonts.ready); await p.waitForTimeout(400);
  await p.screenshot({ path: path.join(__dirname, 'restip.png') });
  await b.close();
})();
