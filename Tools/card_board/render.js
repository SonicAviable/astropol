// Рендер стола карточного боя из board.html в PNG (прозрачный фон): node Tools/card_board/render.js <папка> [масштаб]
const { chromium } = require('playwright');
const path = require('path');
(async () => {
  const out = process.argv[2] || '.';
  const scale = parseFloat(process.argv[3] || '1.25');
  const b = await chromium.launch();
  const p = await b.newPage({ viewport: { width: 1700, height: 1200 }, deviceScaleFactor: scale });
  await p.goto('file://' + path.join(__dirname, 'board.html'));
  await p.waitForTimeout(300);
  for (const [id, name] of [['frame', 'board_frame'], ['pad_enemy', 'board_pad_enemy'], ['pad_mine', 'board_pad_mine'], ['midline', 'board_midline']])
    await p.locator('#' + id + ' svg').screenshot({ path: path.join(out, name + '.png'), omitBackground: true });
  await b.close();
})();
