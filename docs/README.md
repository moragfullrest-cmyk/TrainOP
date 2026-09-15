# Документация TrainOP

TrainOP — библиотека Railway Oriented Programming (ROP) для .NET (`netstandard2.0`). Маршрут состоит из **станций**; данные передаются через мутабельный **манифест груза** (`CargoManifest`). Станция возвращает обновлённый манифест или **сигнал** (зелёный — продолжить, красный — остановка).

**Рекомендуемый стиль:** обработчики `.Station` над данными — обычные функции; `CargoManifest` скрыт в сгенерированном адаптере.

**С чего начать чтение:** связанный учебник — [textbook.md](textbook.md). Его можно читать последовательно: метафора, первый маршрут, поток данных и сигналы, техобслуживание, async, композиция, compile-time/runtime, cross-assembly. Остальные файлы — справочник и углубление.

## Содержание

| Раздел | Описание |
|--------|----------|
| [Учебник](textbook.md) | Связный текст: принципы, использование, внутреннее устройство |
| [Установка через NuGet](nuget.md) | Пакеты, CLI, локальный feed, отличия от ProjectReference |
| [Начало работы](getting-started.md) | Подключение проекта, пример над данными |
| [Основной API](core-api.md) | `CargoManifest`, `TrainRoute`, сигналы, `RailwaySignals.Green`/`Red`, async, `ref` |
| [Архитектура: generator, analyzer, caller, runtime](architecture-internals.md) | Как устроено внутри: пайплайн генератора, анализатор цепочек, Travel, запись возврата в манифест, TOP* |
| [Cross-assembly routes](cross-assembly-routes.md) | Route library + consumer extension, exported schema |
| [Сравнение объёма кода](code-volume-comparison.md) | Manual vs TrainOP (токены, ошибки, recovery) |
| [Benchmarks](../benchmarks/README.md) | Library vs manual |
| [План: data-oriented handlers](plan-data-oriented-handlers.md) | Roadmap: фазы 0–8 выполнены; отложены якоря параметр/поле/свойство/делегат; снят typed Travel |
| [План: производительность Travel](plan-performance.md) | Roadmap: P0–P3 + P4a done; P5 typed bags reverted; P4 TravelLight pending |

## Структура решения

```
TrainOP.sln
├── src/TrainOP              — основная библиотека (netstandard2.0)
├── src/TrainOP.Generators   — source generators + chain analyzer
├── samples/TrainOP.Samples  — консольные примеры
├── benchmarks/              — BenchmarkDotNet: library vs manual
└── tests/                   — модульные тесты (xUnit)
```

Сквозной reference-маршрут: `tests/TrainOP.Tests/DataOrientedPaymentRouteEndToEndTests.cs`.

Бенчмарки chain-dispatch: [`benchmarks/README.md`](../benchmarks/README.md).

## Ключевые типы

| Тип | Назначение |
|-----|------------|
| `CargoManifest` | Мутабельное хранилище вагонов (ключ → значение) |
| `TrainRoute` | Построитель маршрута + `Travel` / `TravelAsync` |
| `RouteReport` | Отчёт (`FailureCode`, `FailureMessage`, `Manifest`, `Get<T>`) |
| `Signal` | Управление hop'ом (зелёный / красный; без груза) |
| `RailwaySignals` | `Green` / `Red` / `White` для обработчиков над данными |
| `SignalIssue` | Код, сообщение и имя станции для красного сигнала |

## Запуск тестов

```bash
dotnet test TrainOP.sln
```
