# Начало работы

## Подключение

### NuGet (рекомендуется для внешних проектов)

Установите пакет **TrainOP** (runtime + generator в одном `.nupkg`):

```bash
dotnet add package TrainOP
```

или в `.csproj`:

```xml
<ItemGroup>
  <PackageReference Include="TrainOP" Version="0.18.0" />
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
- При ProjectReference — явная ссылка на `TrainOP.Generators` + `<Import>` `.targets` (в NuGet-пакете analyzer уже внутри `TrainOP`)

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

var report = route.Travel();
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
    → Станция 1 → запись возврата в манифест → продолжение
    → Станция 2 → запись возврата в манифест → продолжение
    → ...
    → RouteReport (визиты + финальный сигнал)
```

Handler станции — чистая функция над данными. Имена параметров = ключи вагонов в манифесте; адаптер генерируется автоматически.

## Модификаторы и возврат

Как параметр (`ref`, `ref readonly`, `in`, `out`, `params`, по значению) и форма возврата меняют манифест — в [матрице основного API](core-api.md#модификаторы-и-манифест). `Red` и `White` манифест не трогают. На `ServiceStation` состав не меняется (**TOP015**–**TOP017**). `async` не совмещается с `ref` / `out` / `in` / `ref readonly` (CS1988).

## Следующие шаги

- [Основной API](core-api.md) — async, `ref`, отмена, красные сигналы, отчёт маршрута
- [Вложенные маршруты и ветвление](core-api.md#вложенные-маршруты-и-ветвление) — подмаршруты и станция-развилка через `.Station` над данными
