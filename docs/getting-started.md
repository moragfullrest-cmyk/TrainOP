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
  <PackageReference Include="TrainOP" Version="0.10.0" />
  <PackageReference Include="TrainOP.Generators" Version="0.10.0" />
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

<!-- Для ProjectReference: импортируйте targets файл, чтобы подключить analyzer/generator конфигурацию -->
<Import Project="path/to/TrainOP/TrainOP.Generators/build/TrainOP.Generators.targets" />
```

Требования (для обоих способов подключения):

- Совместимость с пакетом TrainOP: `netstandard2.0`
- SDK-style проект с поддержкой analyzers/source generators (для chain-dispatch по умолчанию `caller` дополнительных SDK-порогов не требуется)
- При ProjectReference — `<Import>` файла `TrainOP.Generators.targets` (см. [nuget.md](nuget.md))

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

var report = route.DispatchTrain().Travel();
var paymentId = report.Get<string>("paymentId");
var amount = report.Get<decimal>("amount");

if (!report.ReachedDestination)
{
    Console.WriteLine($"{report.FailureCode}: {report.FailureMessage}");
}
```

## Типичный поток

```
CargoManifest (старт)
    → Станция 1 → merge данных → продолжение
    → Станция 2 → merge данных → продолжение
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
| `RailwaySignals.Pass` | манифест без изменений → зелёный сигнал (в т.ч. `ref`-вагоны: мутации в handler не попадают в манифест) |

Восстановление после красного сигнала — через `ServiceStation` с тем же API (`Green` / `Red`). Подробнее — в [Основном API](core-api.md#станция-техобслуживания-servicestation).

## Следующие шаги

- [Основной API](core-api.md) — async, отмена, красные сигналы, отчёт маршрута
- [Вложенные маршруты и ветвление](core-api.md#вложенные-маршруты-и-ветвление) — подмаршруты и станция-развилка через data-oriented `.Station`
