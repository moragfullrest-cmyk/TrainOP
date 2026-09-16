# Документация TrainOP

TrainOP — библиотека Railway Oriented Programming (ROP) для .NET (`netstandard2.0`). Маршрут состоит из **станций**; данные передаются через мутабельный **манифест груза** (`CargoManifest`). Станция возвращает обновлённый манифест или **сигнал** (зелёный — продолжить, красный — остановка).

**Рекомендуемый стиль:** обработчики `.Station` над данными — обычные функции; `CargoManifest` скрыт в сгенерированном адаптере.

**С чего начать:** исчерпывающий учебник — [textbook.md](textbook.md). Его можно читать последовательно от метафоры до ограничений и карты репозитория. Остальные файлы — краткие выдержки, планы разработки и чеклист релиза.

## Содержание

| Раздел | Описание |
|--------|----------|
| [Учебник](textbook.md) | Полное руководство: использование, API, generator/analyzer/runtime, cross-assembly, TOP*, perf, ограничения |
| [Установка через NuGet](nuget.md) | Единый пакет `TrainOP` (runtime + analyzer) / feed |

| [Начало работы](getting-started.md) | Минимальный quick start |
| [Основной API](core-api.md) | Компактные таблицы API |
| [Архитектура](architecture-internals.md) | Roslyn-разбор; IR-first этапы генератора (1a–7) → Emit-last |
| [Cross-assembly routes](cross-assembly-routes.md) | Краткая карточка library + consumer |
| [Сравнение объёма кода](code-volume-comparison.md) | Manual vs TrainOP |
| [Benchmarks](../benchmarks/README.md) | Library vs manual |
| [План: data-oriented handlers](plan-data-oriented-handlers.md) | Roadmap: фазы 0–8 выполнены; отложены якоря параметр/поле/свойство/делегат |
| [План: производительность Travel](plan-performance.md) | Roadmap: P0–P3 + P4a done; P5 reverted; P4 pending |
| [План: ускорение (продолжение)](plan-acceleration.md) | P4 TravelLight → freeze маршрута → slim dispatch |
| [Готовность к релизу](release-readiness.md) | Чеклист Preview / 1.0 |

## Структура решения

```
TrainOP.sln
├── src/TrainOP              — runtime + единственный NuGet-пакет (netstandard2.0)
├── src/TrainOP.Generators   — source generators + chain analyzer (упаковываются в TrainOP)
├── samples/TrainOP.Samples  — консольные примеры
├── benchmarks/              — BenchmarkDotNet: library vs manual
└── tests/                   — модульные тесты (xUnit)
```

Сквозной reference-маршрут: `tests/TrainOP.Tests/DataOrientedPaymentRouteEndToEndTests.cs`.

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
