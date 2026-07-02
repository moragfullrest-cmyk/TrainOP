# Начало работы

## Подключение

Добавьте ссылки на оба проекта: библиотеку и генератор (как Roslyn analyzer).

**`.csproj` потребителя:**

```xml
<ItemGroup>
  <ProjectReference Include="path/to/TrainOP/TrainOP.csproj" />
  <ProjectReference Include="path/to/TrainOP/TrainOP.Generators.csproj"
                    OutputItemType="Analyzer"
                    ReferenceOutputAssembly="false" />
</ItemGroup>
```

Требования:

- .NET Standard 2.0 и выше (или любой TFM, совместимый с netstandard2.0)
- C# с поддержкой source generators (обычно SDK-style проект на .NET 6+)

## Минимальный пример

```csharp
using TrainOP;

var route = new TrainRoute()
    .AttachStation("Validation", manifest =>
    {
        if (!manifest.HasCar("request-id"))
        {
            return RailwaySignals.Red(
                manifest,
                new SignalIssue("REQ_MISSING", "request-id is required", "Validation"));
        }

        return RailwaySignals.Green(manifest);
    })
    .AttachStation("Enrichment", manifest =>
        manifest.LoadCar("processed-at", DateTime.UtcNow));

var start = new CargoManifest().LoadCar("request-id", "abc-123");
var report = route.DispatchTrain().Travel(start);

if (!report.ReachedDestination)
{
    var red = (RedSignal)report.TerminalSignal;
    Console.WriteLine($"{red.Issue.Code}: {red.Issue.Message}");
}
else
{
    var processedAt = report.TerminalSignal.Manifest.PullCar<DateTime>("processed-at");
}
```

## Типичный поток

```
CargoManifest (старт)
    → Станция 1 → GreenSignal + новый манифест
    → Станция 2 → GreenSignal + новый манифест
    → ...
    → RouteReport (визиты + финальный сигнал)
```

Станция может:

1. **Вернуть манифест** — неявно оборачивается в зелёный сигнал.
2. **Вернуть `Signal`** — явный зелёный или красный сигнал.

## Два стиля работы со станциями

### Стиль «манифест»

Обработчик получает и возвращает `CargoManifest`. Подходит для инфраструктурных шагов, recovery и свободной схемы данных.

```csharp
.AttachStation("Step", manifest =>
    manifest.LoadCar("counter", manifest.PullCar<int>("counter") + 1))
```

### Стиль data-oriented (рекомендуемый)

Handler — чистая функция над данными; `CargoManifest` и `RailwaySignals` скрыты в сгенерированном адаптере:

```csharp
var route = new TrainRoute()
    .Station("Seed", () => new { paymentId = "pay-1", amount = 100m })
    .Station("Discount", (string paymentId, decimal amount) =>
        new { paymentId, amount = amount * 0.9m })
    .Station("Validate", (string paymentId, decimal amount) =>
        amount > 0
            ? Data.Ok(new { paymentId, amount })
            : Data.Fail("INVALID_TOTAL", "amount must be positive"));

var (paymentId, amount, report) = route.DispatchTrain().Travel();
```

Ошибки бизнес-логики — через `Data.Fail(code, message)`, не через `RailwaySignals.Red`. Успех с payload — `Data.Ok(...)`. Пропуск станции без изменений — `Data.Skip()`.

Восстановление после красного сигнала по-прежнему через `AttachRedSignalStation` (manifest-level API). Подробнее — в [Основном API](core-api.md#обработка-красного-сигнала-attachredsignalstation).

## Следующие шаги

- [Основной API](core-api.md) — async, отмена, красные сигналы, отчёт маршрута
