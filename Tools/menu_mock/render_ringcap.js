// Макеты значка работы на кольце: node Tools/menu_mock/render_ringcap.js → Tools/menu_mock/ringcap.png
const { chromium } = require('playwright');
const path = require('path');
(async () => {
  const b = await chromium.launch();
  const p = await b.newPage({ viewport: { width: 1920, height: 1080 } });
  await p.goto('file://' + path.join(__dirname, 'ringcap.html'));
  await p.evaluate(() => document.fonts.ready); await p.waitForTimeout(400);
  await p.screenshot({ path: path.join(__dirname, 'ringcap.png') });
  await b.close();
})();
