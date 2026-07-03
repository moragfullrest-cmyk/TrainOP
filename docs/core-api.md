# Основной API

## CargoManifest

Неизменяемый контейнер вагонов. Любая мутация возвращает **новый** экземпляр.

| Метод | Описание |
|-------|----------|
| `HasWagon(string wagonName)` | Проверка наличия вагона |
| `PullWagon<T>(string wagonName)` | Чтение типизированного значения (бросает, если вагон отсутствует или тип не совпадает) |
| `LoadWagon(string wagonName, object cargo)` | Добавить или заменить вагон |
| `UnloadWagon(string wagonName)` | Удалить вагон |
| `InspectWagons()` | Снимок всех вагонов (`IReadOnlyDictionary<string, object>`) |

```csharp
var manifest = new CargoManifest()
    .LoadWagon("id", "pay-1")
    .LoadWagon("amount", 100m);

var next = manifest
    .LoadWagon("amount", 90m)   // замена
    .UnloadWagon("temporary");  // удаление
```

Имена вагонов чувствительны к регистру (сравнение ordinal).

## TrainRoute и Train

### Построение маршрута (data-oriented)

```csharp
var route = new TrainRoute()
    .Station("Seed", () => new { paymentId = "pay-1", amount = 100m })
    .Station("Discount", (string paymentId, decimal amount) =>
        new { paymentId, amount = amount * 0.9m });
```

Имена параметров handler'а = ключи вагонов. Первая станция без параметров — seed. Генератор создаёт адаптеры и typed `Travel()`.

### Запуск

```csharp
var train = route.DispatchTrain();

// Пустой стартовый манифест
var report = train.Travel();

// С начальным манифестом
var report2 = train.Travel(new CargoManifest().LoadWagon("id", 1));

// С отменой
var report3 = train.Travel(cancellationToken);
```

### Асинхронное выполнение

Для станций с `Task` / `Task<T>` используйте `TravelAsync`:

```csharp
var route = new TrainRoute()
    .Station("Seed", () => new { counter = 10 })
    .Station("Fetch", async (int counter, CancellationToken token) =>
    {
        await Task.Delay(50, token);
        return new { counter = counter * 2 };
    });

var report = await route.DispatchTrain().TravelAsync();
```

> **Важно:** вызов `Travel()` на маршруте с async-станциями бросает `InvalidOperationException` с текстом «Use TravelAsync».

### Низкоуровневый API (AttachStation)

Прямой доступ к `CargoManifest` и `Signal` без codegen-адаптера:

```csharp
var route = new TrainRoute()
    .AttachStation("A", manifest => manifest.LoadWagon("a", 1))
    .AttachStation("B", manifest => manifest.LoadWagon("b", 2));
```

Перегрузки обработчика:

| Возврат | Синхронно | С `CancellationToken` |
|---------|-----------|------------------------|
| `CargoManifest` | `Func<CargoManifest, CargoManifest>` | `Func<CargoManifest, CancellationToken, CargoManifest>` |
| `Signal` | `Func<CargoManifest, Signal>` | `Func<CargoManifest, CancellationToken, Signal>` |
| `Task<CargoManifest>` | `Func<CargoManifest, Task<CargoManifest>>` | `Func<CargoManifest, CancellationToken, Task<CargoManifest>>` |
| `Task<Signal>` | `Func<CargoManifest, Task<Signal>>` | `Func<CargoManifest, CancellationToken, Task<Signal>>` |

Если обработчик возвращает `CargoManifest`, библиотека автоматически оборачивает результат в `GreenSignal`. `null` манифест трактуется как «оставить прежний».

## Сигналы

### Зелёный сигнал

Маршрут продолжается. Манифест из сигнала передаётся на следующую станцию.

```csharp
return RailwaySignals.Green(manifest);
// или просто return manifest;  — в through-перегрузках
```

### Красный сигнал

Маршрут останавливается на этой станции (последующие станции **не** выполняются).

```csharp
.Station("Validate", (string paymentId, decimal amount) =>
    amount > 0
        ? RailwaySignals.Green(new { paymentId, amount })
        : RailwaySignals.Red("INVALID_TOTAL", "amount must be positive"))
```

В низкоуровневом `AttachStation` передайте манифест явно:

```csharp
return RailwaySignals.Red(
    manifest,
    new SignalIssue("REQ_MISSING", "request-id is required", "Validation"));
```

Адаптер преобразует `RailwaySignals.Red(code, message)` в `RedSignal` с `SignalIssue(code, message, stationName)`.

Допустимые возвраты data-handler'а:

| Возврат | Поведение |
|---------|-----------|
| анонимный тип / record | merge в манифест → зелёный сигнал |
| `RailwaySignals.Green(payload)` | merge payload → зелёный сигнал |
| `RailwaySignals.Red(code, msg)` | красный сигнал, маршрут останавливается |
| `RailwaySignals.Pass` | манифест без изменений → зелёный сигнал |
| `GreenSignal` / `RedSignal` | возврат как есть (если есть `CargoManifest` в handler) |

`SignalIssue` содержит:

- `Code` — машиночитаемый код ошибки
- `Message` — описание для человека
- `StationName` — имя станции, вернувшей красный сигнал

### Проверка результата

```csharp
var report = route.DispatchTrain().Travel();

if (report.ReachedDestination)
{
    // TerminalSignal — GreenSignal
    var manifest = report.TerminalSignal.Manifest;
}
else
{
    var red = (RedSignal)report.TerminalSignal;
    Console.WriteLine(red.Issue.Code);
}

// История прохождения
foreach (var visit in report.Visits)
{
    Console.WriteLine($"{visit.StationName}: {(visit.Signal.IsGreen ? "green" : "red")}");
}
```

## Станция техобслуживания (ServiceStation)

Один глобальный обработчик на маршрут — «станция техобслуживания». Вызывается при любом красном сигнале от обычной станции. Может вернуть зелёный сигнал и **продолжить** маршрут с оставшихся станций.

**Data-oriented** (рекомендуется):

```csharp
var route = new TrainRoute()
    .Station("Seed", () => new { amount = -1m })
    .Station("Validate", (decimal amount) =>
        amount > 0 ? RailwaySignals.Green(new { amount }) : RailwaySignals.Red("INVALID", "amount must be positive"))
    .ServiceStation("Recovery", (decimal amount, SignalIssue issue) =>
        issue.Code == "INVALID"
            ? RailwaySignals.Green(new { amount = 1m })
            : RailwaySignals.Red("CANNOT_RECOVER", "unsupported failure"))
    .Station("Double", (decimal amount) => new { amount = amount * 2m });
```

Параметры handler'а станции техобслуживания:

| Параметр | Источник |
|----------|----------|
| вагоны (`amount`, …) | `red.Manifest` |
| `CargoManifest manifest` | `red.Manifest` |
| `SignalIssue issue` | `red.Issue` (код, сообщение, станция-источник) |
| `RedSignal red` | полный красный сигнал (escape hatch) |

Возврат — тот же контракт, что у обычных data-станций: `RailwaySignals.Green` / `Red` / `Pass`, анонимный тип, record, tuple.

Handler с сигнатурой `Func<RedSignal, Signal>` (и async-вариант) — escape hatch без codegen-адаптера; читайте `red.Manifest` и `red.Issue` напрямую.

Async-вариант data-handler'а:

```csharp
.ServiceStation("Recovery", async (decimal amount, SignalIssue issue, CancellationToken token) =>
{
    await Task.Delay(10, token);
    return RailwaySignals.Green(new { amount = 1m });
});
```

Если станция техобслуживания не зарегистрирована, маршрут завершается с красным `TerminalSignal`.

## Отмена (CancellationToken)

```csharp
using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

var route = new TrainRoute()
    .AttachStation("Work", (manifest, token) =>
    {
        token.ThrowIfCancellationRequested();
        return manifest;
    });

route.DispatchTrain().Travel(cts.Token);
// или
await route.DispatchTrain().TravelAsync(cts.Token);
```

`OperationCanceledException` пробрасывается наружу и **не** преобразуется в красный сигнал.

## Необработанные исключения

Исключение внутри станции (кроме отмены) преобразуется в красный сигнал:

| Поле | Значение |
|------|----------|
| `Issue.Code` | `STATION_EXCEPTION` |
| `Issue.Message` | `Unhandled station exception: {сообщение}` |
| `Issue.StationName` | имя станции |

Аналогично для `ServiceStation` — код `SERVICE_STATION_EXCEPTION`.

## Схема выполнения

```mermaid
flowchart TD
    Start([Стартовый манифест]) --> Station[Станция N]
    Station -->|CargoManifest| Green[GreenSignal]
    Station -->|GreenSignal| Green
    Station -->|RedSignal| Red{ServiceStation?}
    Green --> Next{Есть ещё станции?}
    Next -->|да| Station
    Next -->|нет| Done([RouteReport — успех])
    Red -->|нет| Fail([RouteReport — красный])
    Red -->|да, зелёный| Next
    Red -->|да, красный| Fail
    Station -->|исключение| RedSignal[RedSignal STATION_EXCEPTION]
    RedSignal --> Red
```
