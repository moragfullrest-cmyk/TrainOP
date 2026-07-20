# Основной API

## CargoManifest

Мутабельный контейнер вагонов. `LoadWagon` / `UnloadWagon` изменяют экземпляр **на месте** и возвращают `this` (удобно для fluent-цепочек).

| Метод | Описание |
|-------|----------|
| `HasWagon(string wagonName)` | Проверка наличия вагона |
| `TryGetWagon(string wagonName, out object cargo)` | Чтение без исключения, если вагона нет |
| `PullWagon<T>(string wagonName)` | Чтение типизированного значения (бросает, если вагон отсутствует или тип не совпадает) |
| `LoadWagon(string wagonName, object cargo)` | Добавить или заменить вагон (in-place) |
| `UnloadWagon(string wagonName)` | Удалить вагон (in-place) |
| `InspectWagons()` | Live view вагонов (`IReadOnlyDictionary<string, object>`) |

```csharp
var manifest = new CargoManifest()
    .LoadWagon("id", "pay-1")
    .LoadWagon("amount", 100m);

manifest
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

Имена параметров handler'а = ключи вагонов. Первая станция без параметров — seed. Генератор создаёт адаптеры вызовов станций.

**Допустимые формы handler'а** (в текущей compilation, доступной генератору):

- лямбда: `(string paymentId, decimal amount) => …`
- anonymous method: `delegate(string paymentId, decimal amount) { … }`
- method group / local function: `.Station("Discount", Discount)` где `Discount` объявлен в этом проекте

Не поддерживаются: переменные/`Func<>` без dataflow, неоднозначные перегрузки, методы только из referenced DLL без исходников — analyzer сообщает **TOP009**.

**Валидные формы сборки цепочки** (analyzer / chain-dispatch):

```csharp
// 1) Прямая fluent-цепочка
var route = new TrainRoute()
    .Station("Seed", () => new { id = 1 })
    .Station("Next", (int id) => new { id = id + 1 });

// 2) Локальная после new TrainRoute()
var route = new TrainRoute();
route = route
    .Station("Seed", () => new { id = 1 })
    .Station("Next", (int id) => new { id = id + 1 });
```

// 3) Private/internal factory extension
var route = CreateSeed()
    .Station("Next", (int id) => new { id = id + 1 });

// 4) Public factory from referenced assembly (exported schema)
var route = PaymentModule.Build()
    .Station("Finalize", (string paymentId, decimal amount) => new { paymentId, status = "done" });
```

`PaymentRoute.Build()` с цепочкой **внутри** и вызовом только `.DispatchTrain().Travel()` снаружи по-прежнему поддерживается.

`CreateSeed().Station(...)` поддерживается для **private/internal** factory (inter-procedural analysis). **Public** factory использует generated schema (`[RouteSchemaFor]`). См. [cross-assembly-routes.md](cross-assembly-routes.md).

Параметр / поле / свойство / делегат как receiver (`baseRoute.Station(...)`, `buildRoute().Station(...)`) пока **не** поддерживаются (TOP005).

### Запуск

Входные данные задаются **seed-станцией** в начале маршрута (замыкание внешних переменных или параметры `Build(...)`). `Travel()` запускает поезд с пустым стартовым манифестом; seed загружает вагоны первой станцией.

```csharp
RouteReport Handle(string paymentId, decimal amount) =>
    new TrainRoute()
        .Station("Seed", () => new { paymentId, amount })
        .Station("Discount", (string paymentId, decimal amount) =>
            new { paymentId, amount = amount * 0.9m })
        .DispatchTrain()
        .Travel();

var train = route.DispatchTrain();
var report = train.Travel();
var paymentId = report.Get<string>("paymentId");
var amount = report.Get<decimal>("amount");
// или report["paymentId"]

// С отменой
var reportWithCt = train.Travel(cancellationToken);
```

Публичный канон: входные данные только через seed-станцию сверху; `Travel()` / `TravelAsync()` стартуют с пустого манифеста.

Доступ к терминальным вагонам — через `RouteReport` (`Get<T>` / индексатор). Typed deconstruct (`var (a, b) = …Travel()`) **не** используется: при C# 15 и ниже конфликты декомпозиции кортежей на общем terminal-типе не решаются языком.

### Асинхронное выполнение

Для станций с `Task` / `Task<T>` используйте `async`-лямбду в `Station` и `TravelAsync`:

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

## Сигналы

### Зелёный сигнал

Маршрут продолжается. Манифест из сигнала передаётся на следующую станцию.

### Красный сигнал

Маршрут останавливается на этой станции (последующие станции **не** выполняются).

```csharp
.Station("Validate", (string paymentId, decimal amount) =>
    amount > 0
        ? RailwaySignals.Green(new { paymentId, amount })
        : RailwaySignals.Red("INVALID_TOTAL", "amount must be positive"))
```

Адаптер преобразует `RailwaySignals.Red(code, message)` в `RedSignal` с `SignalIssue(code, message, stationName)`.

Допустимые возвраты data-handler'а:

| Возврат | Поведение |
|---------|-----------|
| анонимный тип / record | merge в манифест → зелёный сигнал |
| `RailwaySignals.Green(payload)` | merge payload → зелёный сигнал |
| `RailwaySignals.Red(code, msg)` | красный сигнал, маршрут останавливается |
| `RailwaySignals.Pass` | манифест без изменений → зелёный сигнал (в т.ч. `ref`-вагоны: мутации в handler не попадают в манифест) |
| `void` (без return) | эквивалент `new { }` → partial merge: `ref`-вагоны обновляются, обычные входы выгружаются |

> **Не используйте** `GreenSignal` / `RedSignal` в возврате handler'а — это runtime-типы движка. Для остановки маршрута — `RailwaySignals.Red(code, msg)`; для успеха — данные или `RailwaySignals.Green(payload)`. Возврат runtime-сигнала диагностируется как **TOP010**.

### Value tuple returns

**Рекомендуется:** именованные кортежи — `(paymentId: id, amount: amt)` — или идентификаторы с inference — `(paymentId, amount)`.

**Избегать default ItemN** (нет имени в исходнике и inference не сработал):

| Форма | Диагностика | Риск |
|-------|-------------|------|
| `(paymentId + "-x", amount * 0.9m)` | **TOP006** (Warning, на tuple literal) | Элементы = `Item1`/`Item2`; mapping позиционный и хрупок при перестановке |
| `(Item1: x, Item2: y)` | нет | Имена заданы явно (даже если это ItemN) |
| `(paymentId, amount)` | нет | Имена выведены из идентификаторов |
| `(paymentId, amount: amt)` | нет | Inference + явное имя |

```csharp
// ✅ явное имя
.Station("Discount", (string paymentId, decimal amount) =>
    (paymentId: paymentId + "-disc", amount: amount * 0.9m));

// ✅ inference
.Station("Discount", (string paymentId, decimal amount) =>
    (paymentId, amount));

// ⚠️ TOP006 — default ItemN
.Station("Discount", (string paymentId, decimal amount) =>
    (paymentId + "-disc", amount * 0.9m));
```

`RailwaySignals.Pass` пропускает merge целиком: следующая станция получит тот же манифест, что и до вызова handler'а. Изменения `ref`-параметров в теле handler'а при `Pass` **не сохраняются**. Чтобы записать новые значения `ref`-вагонов в манифест, используйте void (без `return`) или явный partial return (`new { }`, подмножество полей).

`SignalIssue` содержит:

- `Code` — машиночитаемый код ошибки
- `Message` — описание для человека
- `StationName` — имя станции, вернувшей красный сигнал

### Проверка результата

```csharp
var report = route.DispatchTrain().Travel();

if (report.ReachedDestination)
{
    var paymentId = report.Get<string>("paymentId");
}
else
{
    Console.WriteLine($"{report.FailureCode}: {report.FailureMessage}");
}

// История прохождения
foreach (var visit in report.Visits)
{
    Console.WriteLine($"{visit.StationName}: {(visit.IsGreen ? "green" : "red")}");
}
```

`RouteReport` поддерживает readonly индексатор `report["wagonName"]`, typed-метод `report.Get<T>("wagonName")` и свойства `FailureCode` / `FailureMessage` для красного терминального сигнала.  
Если вагона нет, бросается `KeyNotFoundException`.

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

## Вложенные маршруты и ветвление

TrainOP не имеет отдельного API «switch/fork». Вложенные маршруты и ветвление собираются **композицией**:

1. **Подмаршруты** — отдельные `TrainRoute`, обычно в статических фабриках `Build(...)` с собственной seed-станцией.
2. **Станция ветвления** — data-oriented `.Station`, которая по данным вагонов выбирает подмаршрут (`Build(paymentId, amount, …)`) и вызывает `subRoute.DispatchTrain().Travel()`.
3. **Результат подмаршрута** — родительская станция читает `RouteReport` подмаршрута и **сама** формирует возврат: данные (`new { … }`) или `RailwaySignals.Red(...)`. Проброс `TerminalSignal` / `GreenSignal` / `RedSignal` не используется.

Каждый подмаршрут с цепочкой `.Station(...)` анализируется генератором **независимо**. Станция ветвления входит в data-oriented граф родительского маршрута. Манифест в юзер-коде не собирают: данные уходят в seed дочернего маршрута.

```csharp
internal static class PremiumBranchRoute
{
    public static TrainRoute Build(string paymentId, decimal amount) => new TrainRoute()
        .Station("Seed", () => new { paymentId, amount })
        .Station("ApplyPremiumDiscount", (string paymentId, decimal amount) =>
            new { paymentId = paymentId + "-premium", amount = amount * 0.8m, channel = "premium" });
}

internal static class StandardBranchRoute
{
    public static TrainRoute Build(string paymentId, decimal amount) => new TrainRoute()
        .Station("Seed", () => new { paymentId, amount })
        .Station("ApplyStandardFee", (string paymentId, decimal amount) =>
            new { paymentId = paymentId + "-standard", amount = amount + 2m, channel = "standard" });
}

var route = new TrainRoute()
    .Station("Seed", () => new { paymentId = "pay-branch", amount = 100m, tier = "premium" })
    .Station("Branch", (string paymentId, decimal amount, string tier) =>
    {
        var subRoute = tier == "premium"
            ? PremiumBranchRoute.Build(paymentId, amount)
            : StandardBranchRoute.Build(paymentId, amount);

        var subReport = subRoute.DispatchTrain().Travel();
        if (!subReport.ReachedDestination)
        {
            return RailwaySignals.Red("BRANCH_FAILED", $"tier '{tier}' did not complete");
        }

        return new
        {
            paymentId = subReport.Get<string>("paymentId"),
            amount = subReport.Get<decimal>("amount"),
            channel = subReport.Get<string>("channel"),
        };
    })
    .Station("Finalize", (string paymentId, decimal amount, string channel) =>
        new { paymentId, amount, channel, status = "completed" });
```

Полный runnable-пример: `samples/TrainOP.Samples/Examples/NestedBranchingRouteExample.cs`.

## Отмена (CancellationToken)

```csharp
using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

var route = new TrainRoute()
    .Station("Seed", () => new { })
    .Station("Work", (CancellationToken token) =>
    {
        token.ThrowIfCancellationRequested();
        return RailwaySignals.Pass;
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

## Диагностики (analyzer)

| ID | Severity | Условие |
|----|----------|---------|
| `TOP001` | Error | Станция требует вагон, не произведённый ранее |
| `TOP002` | Error | Конфликт типов вагона между станциями |
| `TOP003` | Error | Вагон удалён частичным возвратом, но нужен дальше |
| `TOP004` | Warning | Handler вернул `CargoManifest` — полная замена манифеста |
| `TOP005` | Error | Data-handler вне легитимного якоря `TrainRoute` |
| `TOP006` | Warning | Value tuple с default ItemN (нет явного имени и нет inference) |
| `TOP007` | Error | Конфликт имён вагонов для одной сигнатуры handler'а (вне цепочки; внутри цепочки — caller dispatch или reflection fallback) |
| `TOP008` | Error | Нельзя соединить ветки маршрута перед downstream Station |
| `TOP009` | Error | Handler не лямбда / anonymous / однозначный method group |
| `TOP010` | Error | Handler возвращает `GreenSignal` / `RedSignal` вместо data / `RailwaySignals` |
| `TOP011` | Info | Public factory в referenced assembly без exported schema |
| `TOP012` | Error | Return-paths factory имеют разное terminal-множество |
| `TOP013` | Error | Return-path factory с unknown terminal state |

### Chain-dispatch

При нескольких цепочках с одной сигнатурой типов, но разными именами вагонов:

- `caller` (default) — ctor+ordinal dispatch, без Roslyn interceptors; `new TrainRoute()` идентифицирует цепочку
- `reflection` — явный opt-out через `TrainOP_ChainDispatchMode=reflection` (имена вагонов через `ParameterInfo` при регистрации)

NativeAOT: предпочтителен `caller`, так как `reflection` зависит от сохранённых имён параметров в metadata.

Cross-assembly: [cross-assembly-routes.md](cross-assembly-routes.md). Release tracking: `AnalyzerReleases.Shipped.md`.

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
