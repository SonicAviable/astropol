// Картинка новости из HTML в PNG 1920×1080: node Tools/news/render.js cover_1.6.4  →  VKPlay/news/cover_1.6.4.png
const { chromium } = require('playwright');
const path = require('path');
(async () => {
  const name = process.argv[2] || 'cover_1.6.4';
  const b = await chromium.launch();
  const p = await b.newPage({ viewport: { width: 1920, height: 1080 } });
  await p.goto('file://' + path.join(__dirname, name + '.html'));
  await p.evaluate(() => document.fonts.ready);
  await p.waitForTimeout(400);
  await p.locator('#cover').screenshot({ path: path.join(__dirname, '..', '..', 'VKPlay', 'news', name + '.png') });
  await b.close();
})();
