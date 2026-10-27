# Звуки карточного боя

`build.py` нарезает и сводит звуки боя из CC0-библиотек в `Assets/Resources/Audio/Sfx/<Имя>_N.ogg`
(имя = значение `Sfx`; `SFXManager` подхватывает записи сам, без них звучит синтез — `SfxLibrary.CardFallback`).

Исходники (все CC0 1.0), разложить в одну папку:

| Папка | Откуда |
|---|---|
| `firearm/Prepared SFX Library/…` | https://opengameart.org/content/the-free-firearm-sound-library (`Prepared SFX Library.7z`) |
| `casino-audio/`, `impact-sounds/`, `sci-fi-sounds/`, `interface-sounds/` | https://kenney.nl/assets (zip распаковать как есть) |
| `oga_100-CC0-SFX_0/` | https://opengameart.org/content/100-cc0-sfx (`100-CC0-SFX_0.zip`) |
| `oga_sci-fi-sfx/` | https://opengameart.org/content/50-cc0-sci-fi-sfx (`sci-fi-sfx.zip`) |

```
python3 Tools/card_audio/build.py <папка_исходников> Assets/Resources/Audio/Sfx
```

Нужны `ffmpeg` (с libvorbis) и `numpy`. Что получается:

| Звук | Состав |
|---|---|
| GunRifle | одиночные АК-47 / AR-15 / СКС с хвостом отражений |
| GunAuto | очереди 3–5 патронов (АК, «Карл Густав», ППШ) |
| GunSniper | Мосин и .300 Blackout + короткий суббас |
| GunHeavy | дробовик на октаву ниже + суббас + треск + зал (танк, турель) |
| GunMortar, ShellWhistle | глухой «бумп» миномёта и свист снаряда |
| GunLaser, GunFlame | sci-fi лазер с отдачей; рёв форсунки |
| Boom, BoomSmall | треск + тело + суббас + обломки + зал |
| ImpactMetal / Flesh / Shield, UnitDown | попадания и гибель (лязг, обломки) |
| CardDraw / Hover / Pick / Place / Slam / Flip, OrderCast, Whoosh | бумага карт + удар о стол, вжух, приказ |
