# Документация TrainOP

TrainOP — библиотека Railway Oriented Programming (ROP) для .NET (`netstandard2.0` / `net8.0`). Маршрут состоит из **станций**; данные передаются через неизменяемый **манифест груза** (`CargoManifest`). Станция возвращает обновлённый манифест или **сигнал** (зелёный — продолжить, красный — остановка).

**Рекомендуемый стиль:** data-oriented `.Station` handlers — чистые функции над данными; `CargoManifest` скрыт в сгенерированном адаптере.

## Содержание

| Раздел | Описание |
|--------|----------|
| [Установка через NuGet](nuget.md) | Пакеты, CLI, локальный feed, отличия от ProjectReference |
| [Начало работы](getting-started.md) | Подключение проекта, data-oriented пример |
| [Основной API](core-api.md) | `CargoManifest`, `TrainRoute`, сигналы, `RailwaySignals.Green`/`Red`, async |
| [Cross-assembly routes](cross-assembly-routes.md) | Route library + consumer extension, exported schema |
| [План: data-oriented handlers](plan-data-oriented-handlers.md) | Roadmap: фазы 0–8 выполнены; отложены якоря параметр/поле/свойство/делегат; снят typed Travel |

## Структура решения

```
TrainOP.sln
├── src/TrainOP              — основная библиотека (netstandard2.0; net8.0)
├── src/TrainOP.Generators   — source generators + chain analyzer
├── samples/TrainOP.Samples  — консольные примеры
└── tests/                   — модульные тесты (xUnit; в т.ч. ReflectionDispatch)
```

Сквозной reference-маршрут: `tests/TrainOP.Tests/DataOrientedPaymentRouteEndToEndTests.cs`.

## Ключевые типы

| Тип | Назначение |
|-----|------------|
| `CargoManifest` | Неизменяемое хранилище вагонов (ключ → значение) |
| `TrainRoute` | Построитель маршрута (`.Station` — data-oriented handlers) |
| `Train` | Исполнитель маршрута (`DispatchTrain()`) |
| `RouteReport` | Отчёт о прохождении маршрута (`FailureCode`, `FailureMessage`, `Get<T>`) |
| `RailwaySignals` | `Green` / `Red` / `Pass` для data-oriented handler'ов |
| `SignalIssue` | Код, сообщение и имя станции для красного сигнала |

## Запуск тестов

```bash
dotnet test TrainOP.sln
```
