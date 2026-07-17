# Сравнение объёма кода: manual vs TrainOP

Один и тот же checkout-пайплайн:

- валидация входа
- async-шаги (loyalty, stock, charge)
- `CancellationToken`
- бизнес-отказы (`STOCK_LIMIT`, `FRAUD_REVIEW`, `PAYMENT_REJECTED`)
- восстановление по `STOCK_LIMIT`
- единый результат успеха/ошибки

| Реализация | Файл | ≈ строк (non-blank, non-comment) |
|------------|------|-----------------------------------|
| Без библиотеки | [`ManualCheckoutPipeline.cs`](../samples/TrainOP.Samples/Examples/CodeVolume/ManualCheckoutPipeline.cs) | **122** |
| С TrainOP | [`TrainOpCheckoutPipeline.cs`](../samples/TrainOP.Samples/Examples/CodeVolume/TrainOpCheckoutPipeline.cs) | **95** |

Разница ≈ **27 строк** (~22%). Выигрыш не в формулах скидки/доставки, а в отсутствии ручного `StepResult`, nested `if (!ok)`, `try/catch` и повторяемых проверок токена между шагами — это закрывают `Red`/`Green`, `ServiceStation` и `TravelAsync(token)`.

## Что делает manual сам

1. Тип `StepResult` и ранние `return Fail(...)`.
2. `ThrowIfCancellationRequested` перед каждым шагом (нет общего цикла маршрута).
3. Ветвление recovery внутри одного метода.
4. `try/catch`: отмена наружу, прочее → `UNHANDLED`.

TrainOP: `RailwaySignals.Red` / `Green`, `ServiceStation`, `TravelAsync(token)`; `OperationCanceledException` не заворачивается в красный сигнал.

## Запуск

```bash
dotnet run --project samples/TrainOP.Samples
```

Пример «8. Объём кода: manual vs TrainOP» печатает оба исхода и оценку строк.

## Скорость

Бенчмарк `LibraryVsManualBenchmarks` — [`benchmarks/README.md`](../benchmarks/README.md).
