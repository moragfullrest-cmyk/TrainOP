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
  <PackageReference Include="TrainOP" Version="0.15.0" />
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

## Возвраты handler'а

| Возврат | Поведение |
|---------|-----------|
| анонимный тип / record | поля записываются в манифест → зелёный сигнал |
| `RailwaySignals.Green(...)` с данными | данные из аргумента записываются в манифест → зелёный сигнал |
| `RailwaySignals.Red(code, msg)` | красный сигнал, маршрут останавливается |
| `RailwaySignals.White` | манифест без изменений → продолжение маршрута (лунно-белый) (в т.ч. `ref`-вагоны: мутации в handler не попадают в манифест) |

Восстановление после красного сигнала — через `ServiceStation` с тем же API (`Green` / `Red` / данные). Там в манифест можно записать только обновления уже существующих вагонов: состав не меняется (иначе **TOP015**–**TOP017**), потому что хвост маршрута уже ждёт эти вагоны, а техобслуживание может не вызваться. `ref` необязателен; `async` + `ref` по-прежнему запрещены языком (CS1988). Подробнее — в [Основном API](core-api.md#станция-техобслуживания-servicestation) и [параметрах `ref`](core-api.md#параметры-ref).

## Следующие шаги

- [Основной API](core-api.md) — async, `ref`, отмена, красные сигналы, отчёт маршрута
- [Вложенные маршруты и ветвление](core-api.md#вложенные-маршруты-и-ветвление) — подмаршруты и станция-развилка через `.Station` над данными
