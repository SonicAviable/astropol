// Шапка группы VK: node Tools/news/render_vk.js → VKPlay/news/vk_cover_pc.png (1920×768)
const { chromium } = require('playwright');
const path = require('path');
(async () => {
  const b = await chromium.launch();
  const p = await b.newPage({ viewport: { width: 1920, height: 768 } });
  await p.goto('file://' + path.join(__dirname, 'vk_cover.html'));
  await p.evaluate(() => document.fonts.ready);
  await p.waitForTimeout(400);
  await p.locator('#cover').screenshot({ path: path.join(__dirname, '..', '..', 'VKPlay', 'news', 'vk_cover_pc.png') });
  await b.close();
})();
