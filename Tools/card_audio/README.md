# Звуки карточного боя

`build.py` нарезает и сводит звуки боя в `Assets/Resources/Audio/Sfx/<Имя>_N.ogg`
(имя = значение `Sfx`; `SFXManager` подхватывает записи сам, без них звучит синтез — `SfxLibrary.CardFallback`)
и фон боя `Assets/Resources/Audio/CardBattle/battle_wind.ogg` (петля ветра; поверх неё игра изредка пускает `BoomFar`).

Только настоящие записи под CC0 1.0, без синтеза. Исходники разложить в одну папку:

| Папка | Откуда |
|---|---|
| `firearm/Prepared SFX Library/…` | https://opengameart.org/content/the-free-firearm-sound-library (`Prepared SFX Library.7z`) |
| `bsb/<номер>.flac` (или `.wav`) | https://bigsoundbank.com — `UPLOAD/flac/<номер>.flac` или `UPLOAD/bwf-en/<номер>.wav`; номера — в `Assets/Resources/Audio/Sfx/CREDITS.txt` |
| `oga_bang/` | https://opengameart.org/node/92774 (`25-CC0-bang-sfx.zip`) |
| `casino-audio/`, `impact-sounds/` | https://kenney.nl/assets (zip распаковать как есть) |

```
python3 Tools/card_audio/build.py <папка_исходников> Assets/Resources/Audio/Sfx
```

Нужны `ffmpeg` (с libvorbis) и `numpy`. Как сведено:

| Звук | Состав |
|---|---|
| GunRifle, GunAuto | записи АК-47 / AR-15 / СКС / ППШ / «Карл Густав» с хвостом отражений и звоном гильз о бетон |
| GunSniper | Мосин, .300 Blackout, Winchester + суббас, затем щелчок затвора |
| GunHeavy, GunMortar | пушечный салют и дробовик ниже по высоте + суббас + раскат грома + лязг |
| ShellWhistle | «вжух» воздуха ниже по высоте с нарастанием — снаряд на подлёте |
| GunLaser, ElecZap | сухой треск выстрела + записи электроразрядов |
| GunFlame | «вуп» воспламенения бензина + рёв горелки |
| Boom, BoomSmall, BoomFar | хлопок + дальний взрыв + гром + суббас + осыпь камней и металла |
| ImpactMetal / Flesh / Shield, UnitDown | стальные полосы и лом, удар и земля, разряд и гул трансформатора |
| GunCock, EngineRev, Servo | появление юнита: затвор (пехота), дизель (техника), пневматика (сооружения, дроны) |
| Radio, OrderCast | треск рации (захват сектора, приказы) |
| RadioOrder, RadioCancel | карта легла на стол / снята: щелчок тангенты, «пшш» шумоподавителя, треск эфира, сигнал цифровой линии, «роджер-бип»; всё в полосе рации 300–3400 Гц с перегрузом |
| CardDraw / Hover / Pick / Place / Slam / Flip, Whoosh | настоящие карты (Kenney Casino), удар о стол, «вжух» |
