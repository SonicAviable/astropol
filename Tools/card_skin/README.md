# Скин карточного боя

`gen.py` генерирует текстуры интерфейса (`Assets/Resources/UI/CardBattle`): металл с фасками и освещением по карте нормалей,
самоцветы, гнёзда силы, кнопку хода, ленты, соты щита; перекрашивает флипбуки Unity Particle Pack и берёт частицы Kenney.

Нужные исходники кладутся в `dl/` рядом со скриптом (все под CC0):
- `dl/tex/Metal046B`, `dl/tex/Leather026`, `dl/tex/MetalPlates006` — ambientCG, `https://ambientcg.com/get?file=<id>_1K-JPG.zip`
- `dl/particle` — Kenney Particle Pack, https://kenney.nl/assets/particle-pack
- `dl/RussoOne-Regular.ttf` — Google Fonts (OFL)

```
python3 gen.py out        # всё
python3 mock.py mock.jpg  # макет экрана для проверки дизайна
```
