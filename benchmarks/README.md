# Benchmarks

Сравнение скорости и режимов TrainOP.

## Запуск

```bash
dotnet run -c Release --project benchmarks/TrainOP.Benchmarks
```

Быстрый прогон:

```bash
dotnet run -c Release --project benchmarks/TrainOP.Benchmarks -- -f * --job short
```

Фильтры:

```bash
# reflection vs interceptors
dotnet run -c Release --project benchmarks/TrainOP.Benchmarks -- --filter *ChainDispatch*

# библиотека vs ручной код
dotnet run -c Release --project benchmarks/TrainOP.Benchmarks -- --filter *LibraryVsManual*
```

## 1. Reflection vs interceptors

| Адаптер | Режим | Механизм |
|---------|-------|----------|
| `TrainOP.Benchmarks.Reflection` | `reflection` | имена вагонов через `ParameterInfo` при регистрации |
| `TrainOP.Benchmarks.Interceptors` | `stable` | Roslyn interceptors + compile-time binding tables |

Общий сценарий (`Shared/ChainDispatchScenarios.cs`) компилируется в оба адаптера.

| Категория | Смысл |
|-----------|--------|
| `BuildAndTravel_*` | регистрация станций + `Travel()` |
| `TravelOnly_*` | повторный `Travel()` уже собранного `Train` |

## 2. Библиотека vs ручной код

Класс `LibraryVsManualBenchmarks` сравнивает interceptor-режим TrainOP с эквивалентными transforms без библиотеки (`ManualPipelineScenarios`).

| Категория | Смысл |
|-----------|--------|
| `Payment` / `LongPayment` | те же арифметические шаги |
| `Checkout` | валидация + pricing + stock/fraud gates (happy path) |

Ожидание: manual быстрее (нет манифеста/адаптеров); разница — цена абстракции. Объём кода и единообразие ошибок/отмены — в [`docs/code-volume-comparison.md`](../docs/code-volume-comparison.md).
