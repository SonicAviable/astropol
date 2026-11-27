// Обложка новости из HTML в JPG 1920×1080 прямо в игру (Resources/Menu/News):
//   node Tools/news/render_cover.js cover_1.0.0 ru   →  Assets/Resources/Menu/News/cover_1.0.0.jpg
//   node Tools/news/render_cover.js cover_1.0.0 en   →  Assets/Resources/Menu/News/cover_1.0.0_en.jpg
const { chromium } = require('playwright');
const path = require('path');
(async () => {
  const name = process.argv[2] || 'cover_1.0.0';
  const lang = process.argv[3] || 'ru';
  const b = await chromium.launch();
  const p = await b.newPage({ viewport: { width: 1920, height: 1080 } });
  await p.goto('file://' + path.join(__dirname, name + '.html') + '?lang=' + lang);
  await p.evaluate(() => document.fonts.ready);
  await p.waitForTimeout(400);
  const out = path.join(__dirname, '..', '..', 'Assets', 'Resources', 'Menu', 'News', name + (lang === 'ru' ? '' : '_' + lang) + '.jpg');
  await p.locator('#cover').screenshot({ path: out, type: 'jpeg', quality: 90 });
  await b.close();
  console.log(out);
})();
