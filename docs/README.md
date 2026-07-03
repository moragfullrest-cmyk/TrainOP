# Документация TrainOP

TrainOP — библиотека Railway Oriented Programming (ROP) для .NET Standard 2.0. Маршрут состоит из **станций**; данные передаются через неизменяемый **манифест груза** (`CargoManifest`). Станция возвращает обновлённый манифест или **сигнал** (зелёный — продолжить, красный — остановка).

**Рекомендуемый стиль:** data-oriented `.Station` handlers — чистые функции над данными; `CargoManifest` скрыт в сгенерированном адаптере.

## Содержание

| Раздел | Описание |
|--------|----------|
| [Начало работы](getting-started.md) | Подключение проекта, data-oriented пример |
| [Основной API](core-api.md) | `CargoManifest`, `TrainRoute`, сигналы, `RailwaySignals.Green`/`Red`, async |
| [План: data-oriented handlers](plan-data-oriented-handlers.md) | Roadmap — **фазы 0–6 ✅** |

## Структура решения

```
TrainOP.sln
├── src/TrainOP              — основная библиотека (netstandard2.0)
├── src/TrainOP.Generators   — source generators + chain analyzer
├── samples/TrainOP.Samples  — консольные примеры
└── tests/                   — модульные тесты (xUnit)
```

Сквозной reference-маршрут: `tests/TrainOP.Tests/DataOrientedPaymentRouteEndToEndTests.cs`.

## Ключевые типы

| Тип | Назначение |
|-----|------------|
| `CargoManifest` | Неизменяемое хранилище вагонов (ключ → значение) |
| `TrainRoute` | Построитель маршрута (`.Station` — основной путь; `.AttachStation` — низкоуровневый) |
| `Train` | Исполнитель маршрута (`DispatchTrain()`) |
| `RouteReport` | Отчёт о прохождении маршрута |
| `GreenSignal` / `RedSignal` | Сигналы продолжения и остановки |
| `SignalIssue` | Код, сообщение и имя станции для красного сигнала |
| `RailwaySignals` | `Green` / `Red` / `Pass` для data-oriented handler'ов |

## Запуск тестов

```bash
dotnet test TrainOP.sln
```
