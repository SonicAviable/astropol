# Симулятор карточного боя (вне Unity)

Правила боя (`Assets/Scripts/Cards/Battle`, `Assets/Scripts/Cards/Data`) — чистый C#, их можно гонять
тысячами автобоёв обычным компилятором Mono, без Unity. `Stub.cs` подменяет `Loc` из игры.

```
cd Tools/card_sim
mcs -target:library -out:rules.dll Stub.cs ../../Assets/Scripts/Cards/Battle/*.cs ../../Assets/Scripts/Cards/Data/*.cs
mcs -out:sim.exe -r:rules.dll Sim.cs
LANG=C.UTF-8 mono sim.exe 3000 16      # боёв, карт в колоде десанта
```

Печатает: долю побед атаки, прорывов и отступлений, среднюю длину боя и распределение по ходам,
карт за ход, неистраченные очки, остаток колод и рук, выстрелы, гибели и смены контроля секторов.
Расстановка — как у тестового боя (F10): случайные колоды, укрепления 0–3, ИИ против ИИ.
