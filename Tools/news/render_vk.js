// Шапка группы VK: node Tools/news/render_vk.js → VKPlay/news/vk_cover_pc.png (1920×768) и vk_cover_mobile.png (1080×1920)
const { chromium } = require('playwright');
const path = require('path');
(async () => {
  const b = await chromium.launch();
  for (const [v, w, h, name] of [['pc', 1920, 768, 'vk_cover_pc'], ['mob', 1080, 1920, 'vk_cover_mobile']]) {
    const p = await b.newPage({ viewport: { width: w, height: h } });
    await p.goto('file://' + path.join(__dirname, 'vk_cover.html') + '?v=' + v);
    await p.evaluate(() => document.fonts.ready);
    await p.waitForTimeout(400);
    await p.locator('#cover').screenshot({ path: path.join(__dirname, '..', '..', 'VKPlay', 'news', name + '.png') });
  }
  await b.close();
})();
