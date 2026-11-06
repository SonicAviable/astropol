// Рендер поля карточного боя из board.html в PNG (прозрачный фон): node Tools/card_board/render.js <папка> [масштаб]
const { chromium } = require('playwright');
const path = require('path');
(async () => {
  const out = process.argv[2] || '.';
  const scale = parseFloat(process.argv[3] || '1');
  const b = await chromium.launch();
  const p = await b.newPage({ viewport: { width: 2000, height: 2400 }, deviceScaleFactor: scale });
  await p.goto('file://' + path.join(__dirname, 'board.html'));
  await p.waitForTimeout(300);
  for (const [id, name] of [['screen', 'board_screen'], ['lines', 'board_lines'], ['ring_a', 'holo_ring_a'], ['ring_b', 'holo_ring_b'], ['rays', 'holo_rays']])
    await p.locator('#' + id + ' svg').screenshot({ path: path.join(out, name + '.png'), omitBackground: true });
  await b.close();
})();
