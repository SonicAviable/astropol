using System.Collections.Generic;
using UnityEngine;

namespace StellarisClone.Core
{
    public static class EventDatabase
    {
        private static readonly List<GameEventData> _allEvents = new List<GameEventData>();
        private static bool _initialized;

        public static GameEventData GetRandomEvent()
        {
            if (!_initialized) Initialize();
            if (_allEvents.Count == 0) return null;
            return _allEvents[Random.Range(0, _allEvents.Count)];
        }

        private static void Initialize()
        {
            if (_initialized) return;
            _initialized = true;

            _allEvents.Add(new GameEventData(
                "ancient_derelict_cruiser",
                "ЗАБРОШЕННЫЙ ДРЕЙФУЮЩИЙ КРЕЙСЕР",
                "Датчики научного судна засекли на гелевой орбите объект класса «Крейсер». Корпус покрыт микро-метеоритной эрозией возрастом ~300 лет. Бортовой транслятор молчит, но сигнатура реактора стабильна. Археологи уверяют, что архивы в центральном блоке, вероятно, сохранились.",
                AnomalyType.AncientDerelict,
                new List<EventOption>
                {
                    new EventOption(
                        "▶  РАЗОБРАТЬ НА МЕТАЛЛОЛОМ",
                        "+120 Сплавов разово (силовой каркас + фюзеляж).",
                        () =>
                        {
                            var eco = EconomyManager.Instance;
                            if (eco != null) { eco.Alloys += 120f; eco.RaiseResourcesChanged(); }
                        }
                    ),
                    new EventOption(
                        "⚛  ИЗУЧИТЬ БОРТОВОЙ АРХИВ",
                        "+80 Науки разово. 30% шанс — мгновенный прорыв случайной технологии.",
                        () =>
                        {
                            var tech = TechnologyManager.Instance;
                            if (tech != null)
                            {
                                tech.AddInstantScience(80f);
                                if (Random.value < 0.30f) tech.CompleteRandomTech();
                            }
                        }
                    ),
                    new EventOption(
                        "☢  ПОПЫТАТЬСЯ ВОССТАНОВИТЬ РЕАКТОР",
                        "50%: +240 Спл. и новый боевой корвет. 50%: взрыв, потеря времени.",
                        () =>
                        {
                            if (Random.value < 0.5f)
                            {
                                var eco = EconomyManager.Instance;
                                if (eco != null) { eco.Alloys += 240f; eco.RaiseResourcesChanged(); }
                                FleetManager.Instance?.CreateFleetObject($"{FleetManager.Instance.AllFleets.Count + 1}-й Корвет-Призрак", 0, FleetType.Military);
                            }
                        }
                    )
                }
            ));

            _allEvents.Add(new GameEventData(
                "alien_amoeba_belt",
                "КОСМИЧЕСКАЯ АМЁБА В АСТЕРОИДНОМ ПОЯСЕ",
                "В поясе астероидов обнаружена колония кремниевых амёбоподобных форм. Они поглощают силикаты и выделяют в метаболические пузырьки чистый Гелий-3. Нейросканеры показывают реакцию на фононные колебания — возможно, колония способна к обучению.",
                AnomalyType.AlienFauna,
                new List<EventOption>
                {
                    new EventOption(
                        "👁  ОСТАВИТЬ В ПОКОЕ И НАБЛЮДАТЬ",
                        "Перманентно +2 Науки/мес. к базовому доходу Империи.",
                        () =>
                        {
                            var tech = TechnologyManager.Instance;
                            tech?.AddMonthlyScience(2f);
                        }
                    ),
                    new EventOption(
                        "☠  УНИЧТОЖИТЬ И СОБРАТЬ БИОМАТЕРИАЛ",
                        "+75 Гелия-3 разово. Риск долгосрочного ухудшения биосферы.",
                        () =>
                        {
                            var eco = EconomyManager.Instance;
                            if (eco != null) { eco.EnergyCredits += 75f; eco.RaiseResourcesChanged(); }
                        }
                    )
                }
            ));

            _allEvents.Add(new GameEventData(
                "precursor_relic",
                "РЕЛИКТ ПРЕДШЕСТВЕННИКОВ",
                "На экваториальной плите третьей планеты обнаружен монолит неизвестного сплава с гравитационной аннигиляционной матрицей. Возраст — 127 млн лет. На поверхности 14 тысяч глифов, похожих на протоматематические уравнения.",
                AnomalyType.PrecursorRelic,
                new List<EventOption>
                {
                    new EventOption(
                        "⚛  ДЕШИФРОВАТЬ ГЛИФЫ",
                        "+150 Науки разово. Перманентно +1 Влияние/мес.",
                        () =>
                        {
                            var tech = TechnologyManager.Instance;
                            tech?.AddInstantScience(150f);
                            var eco = EconomyManager.Instance;
                            if (eco != null) { eco.AddIncome(0f, 0f, 0f, 1f); eco.RaiseResourcesChanged(); }
                        }
                    ),
                    new EventOption(
                        "◆  ИЗВЛЕЧЬ МОНОЛИТ НА БАЗУ",
                        "+200 Сплавов за сверхпрочный сплав оболочки.",
                        () =>
                        {
                            var eco = EconomyManager.Instance;
                            if (eco != null) { eco.Alloys += 200f; eco.RaiseResourcesChanged(); }
                        }
                    )
                }
            ));

            _allEvents.Add(new GameEventData(
                "spatial_rift",
                "ПРОСТРАНСТВЕННЫЙ РАЗЛОМ «ТИФОН»",
                "На окраине системы возникла субпространственная аномалия. Сенсоры фиксируют флуктуации на границе М-пространства; через разлом идут слабые гравитационные волны, синхронизированные с неизвестным пульсаром в 1.4 кпарсек.",
                AnomalyType.SpatialRift,
                new List<EventOption>
                {
                    new EventOption(
                        "◎  ЗОНДИРОВАТЬ РАЗЛОМ",
                        "60%: +5 Влияния разово. 40%: потеря 50 Энергии (нестабильность).",
                        () =>
                        {
                            var eco = EconomyManager.Instance;
                            if (eco == null) return;
                            if (Random.value < 0.60f) eco.Influence += 5f;
                            else eco.EnergyCredits = Mathf.Max(0, eco.EnergyCredits - 50f);
                            eco.RaiseResourcesChanged();
                        }
                    ),
                    new EventOption(
                        "⛨  ВОЗВРАТ С ДАННЫМИ СКАНИРОВАНИЯ",
                        "+60 Науки разово. Стабильный, безопасный исход.",
                        () =>
                        {
                            var tech = TechnologyManager.Instance;
                            tech?.AddInstantScience(60f);
                        }
                    )
                }
            ));

            _allEvents.Add(new GameEventData(
                "distress_signal",
                "ЗАШИФРОВАННЫЙ СИГНАЛ БЕДСТВИЯ",
                "В дециметровом диапазоне принят сигнал стандарта «Федерация Стеллар». Источник — обломок транспортера с радиационным шлейфом. Бортовой чёрный ящик, вероятно, содержит навигационные карты ближних систем.",
                AnomalyType.DistressSignal,
                new List<EventOption>
                {
                    new EventOption(
                        "🚀  СПАСАТЕЛЬНАЯ ОПЕРАЦИЯ",
                        "+30 Вл., +60 Тит., +60 Энергии (гуманитарная награда).",
                        () =>
                        {
                            var eco = EconomyManager.Instance;
                            if (eco != null)
                            {
                                eco.Influence += 30f;
                                eco.Minerals += 60f;
                                eco.EnergyCredits += 60f;
                                eco.RaiseResourcesChanged();
                            }
                        }
                    ),
                    new EventOption(
                        "🔍  БЕСКОНТАКТНОЕ СКАНИРОВАНИЕ",
                        "+60 Науки +50% скорость исследований на 30 дней.",
                        () =>
                        {
                            var tech = TechnologyManager.Instance;
                            if (tech != null)
                            {
                                tech.AddInstantScience(60f);
                                tech.ApplyTempResearchBoost(1.5f, 30);
                            }
                        }
                    )
                }
            ));
        }
    }
}
