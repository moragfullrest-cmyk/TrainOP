# Начало работы

## Подключение

### NuGet (рекомендуется для внешних проектов)

Установите оба пакета — библиотеку и генератор:

```bash
dotnet add package TrainOP
dotnet add package TrainOP.Generators
```

или в `.csproj`:

```xml
<ItemGroup>
  <PackageReference Include="TrainOP" Version="0.1.1" />
  <PackageReference Include="TrainOP.Generators" Version="0.1.1" />
</ItemGroup>
```

Полное руководство: локальный feed, проверка подключения, устранение неполадок — **[Установка через NuGet](nuget.md)**.

### Разработка в решении (ProjectReference)

Если TrainOP лежит в том же solution или клонирован рядом с вашим проектом:

```xml
<ItemGroup>
  <ProjectReference Include="path/to/TrainOP/TrainOP.csproj" />
  <ProjectReference Include="path/to/TrainOP/TrainOP.Generators/TrainOP.Generators.csproj"
                    OutputItemType="Analyzer"
                    ReferenceOutputAssembly="false" />
</ItemGroup>
```

Требования (для обоих способов подключения):

- .NET Standard 2.0 и выше (или любой TFM, совместимый с netstandard2.0)
- C# с поддержкой source generators (обычно SDK-style проект на .NET 6+)

## Минимальный пример

```csharp
using TrainOP;

var route = new TrainRoute()
    .Station("Seed", () => new { paymentId = "pay-1", amount = 100m })
    .Station("Discount", (string paymentId, decimal amount) =>
        new { paymentId, amount = amount * 0.9m })
    .Station("Validate", (string paymentId, decimal amount) =>
        amount > 0
            ? RailwaySignals.Green(new { paymentId, amount })
            : RailwaySignals.Red("INVALID_TOTAL", "amount must be positive"));

var (paymentId, amount, report) = route.DispatchTrain().Travel();

if (!report.ReachedDestination)
{
    var red = (RedSignal)report.TerminalSignal;
    Console.WriteLine($"{red.Issue.Code}: {red.Issue.Message}");
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

Handler станции — чистая функция над данными. Имена параметров = ключи вагонов в манифесте; адаптер генерируется автоматически.

## Возвраты handler'а

| Возврат | Поведение |
|---------|-----------|
| анонимный тип / record | merge в манифест → зелёный сигнал |
| `RailwaySignals.Green(payload)` | merge payload → зелёный сигнал |
| `RailwaySignals.Red(code, msg)` | красный сигнал, маршрут останавливается |
| `RailwaySignals.Pass` | манифест без изменений → зелёный сигнал |

Восстановление после красного сигнала — через `ServiceStation` с тем же API (`Green` / `Red`). Подробнее — в [Основном API](core-api.md#станция-техобслуживания-servicestation).

## Низкоуровневый API (AttachStation)

Для инфраструктурных шагов, когда нужен прямой доступ к `CargoManifest` и `Signal`, используйте `AttachStation`:

```csharp
var route = new TrainRoute()
    .AttachStation("Step", manifest =>
        manifest.LoadWagon("counter", manifest.PullWagon<int>("counter") + 1));
```

Генератор не анализирует такие станции; typed `Travel()` доступен только для data-oriented цепочек `.Station(...)`.

## Следующие шаги

- [Основной API](core-api.md) — async, отмена, красные сигналы, отчёт маршрута
