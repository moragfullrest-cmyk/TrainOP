# Benchmarks

Сравнение скорости TrainOP с ручным кодом.

## Запуск

```bash
dotnet run -c Release --project benchmarks/TrainOP.Benchmarks
```

Быстрый прогон:

```bash
dotnet run -c Release --project benchmarks/TrainOP.Benchmarks -- -f * --job short
```

Фильтр:

```bash
dotnet run -c Release --project benchmarks/TrainOP.Benchmarks -- --filter *LibraryVsManual*
```

## Библиотека vs ручной код

Класс `LibraryVsManualBenchmarks` сравнивает TrainOP (caller dispatch) с эквивалентными transforms без библиотеки (`ManualPipelineScenarios`).

Сценарии chain-dispatch (`Shared/ChainDispatchScenarios.cs`) компилируются в адаптер `TrainOP.Benchmarks.Interceptors` (namespace `TrainOP.Benchmarks.Caller`).

| Категория | Смысл |
|-----------|--------|
| `Payment` / `LongPayment` | те же арифметические шаги |
| `Checkout` | валидация + pricing + stock/fraud gates (happy path) |

Ожидание: manual быстрее (нет манифеста/адаптеров); разница — цена абстракции. Объём кода и единообразие ошибок/отмены — в [`docs/code-volume-comparison.md`](../docs/code-volume-comparison.md).
