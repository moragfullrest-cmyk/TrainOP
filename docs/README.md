# Документация TrainOP

TrainOP — библиотека Railway Oriented Programming (ROP) для .NET (`netstandard2.0` / `net8.0`). Маршрут состоит из **станций**; данные передаются через мутабельный **манифест груза** (`CargoManifest`). Станция возвращает обновлённый манифест или **сигнал** (зелёный — продолжить, красный — остановка).

**Рекомендуемый стиль:** data-oriented `.Station` handlers — чистые функции над данными; `CargoManifest` скрыт в сгенерированном адаптере.

## Содержание

| Раздел | Описание |
|--------|----------|
| [Установка через NuGet](nuget.md) | Пакеты, CLI, локальный feed, отличия от ProjectReference |
| [Начало работы](getting-started.md) | Подключение проекта, data-oriented пример |
| [Основной API](core-api.md) | `CargoManifest`, `TrainRoute`, сигналы, `RailwaySignals.Green`/`Red`, async |
| [Архитектура: generator, analyzer, interceptors, runtime](architecture-internals.md) | Как устроено внутри: пайплайн генератора, анализатор цепочек, перехватчики, Travel, merge, TOP* |
| [Cross-assembly routes](cross-assembly-routes.md) | Route library + consumer extension, exported schema |
| [Сравнение объёма кода](code-volume-comparison.md) | Manual vs TrainOP (токены, ошибки, recovery) |
| [Benchmarks](../benchmarks/README.md) | Reflection vs interceptors; library vs manual |
| [План: data-oriented handlers](plan-data-oriented-handlers.md) | Roadmap: фазы 0–8 выполнены; отложены якоря параметр/поле/свойство/делегат; снят typed Travel |
| [План: производительность Travel](plan-performance.md) | Roadmap: P0–P3 + P4a done; P5 typed bags reverted; P4 TravelLight pending |

## Структура решения

```
TrainOP.sln
├── src/TrainOP              — основная библиотека (netstandard2.0; net8.0)
├── src/TrainOP.Generators   — source generators + chain analyzer
├── samples/TrainOP.Samples  — консольные примеры
├── benchmarks/              — BenchmarkDotNet: reflection vs interceptors
└── tests/                   — модульные тесты (xUnit; в т.ч. ReflectionDispatch)
```

Сквозной reference-маршрут: `tests/TrainOP.Tests/DataOrientedPaymentRouteEndToEndTests.cs`.

Бенчмарки chain-dispatch: [`benchmarks/README.md`](../benchmarks/README.md).

## Ключевые типы

| Тип | Назначение |
|-----|------------|
| `CargoManifest` | Мутабельное хранилище вагонов (ключ → значение) |
| `TrainRoute` | Построитель маршрута (`.Station` — data-oriented handlers) |
| `Train` | Исполнитель маршрута (`DispatchTrain()`) |
| `RouteReport` | Отчёт о прохождении маршрута (`FailureCode`, `FailureMessage`, `Get<T>`) |
| `RailwaySignals` | `Green` / `Red` / `Pass` для data-oriented handler'ов |
| `SignalIssue` | Код, сообщение и имя станции для красного сигнала |

## Запуск тестов

```bash
dotnet test TrainOP.sln
```
