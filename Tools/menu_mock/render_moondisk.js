// Макеты луны в диске даты: node Tools/menu_mock/render_moondisk.js → Tools/menu_mock/moondisk.png
const { chromium } = require('playwright');
const path = require('path');
(async () => {
  const b = await chromium.launch({ args: ['--allow-file-access-from-files'] });
  const p = await b.newPage({ viewport: { width: 1920, height: 1080 } });
  await p.goto('file://' + path.join(__dirname, 'moondisk.html'));
  await p.evaluate(() => document.fonts.ready);
  await p.waitForFunction(() => document.body.dataset.ready === '1', null, { timeout: 60000 });
  await p.screenshot({ path: path.join(__dirname, 'moondisk.png') });
  await b.close();
})();
