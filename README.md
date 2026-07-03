# TrainOP

[![CI](https://github.com/moragfullrest-cmyk/TrainOP/actions/workflows/ci.yml/badge.svg?branch=master)](https://github.com/moragfullrest-cmyk/TrainOP/actions/workflows/ci.yml)

Библиотека **Railway Oriented Programming** для `.NET Standard 2.0`: маршруты из станций, неизменяемый манифест данных и сигналы зелёный/красный. Source generator добавляет **data-oriented** `.Station` handlers и typed `Travel()`.

## Документация

Полное руководство — в папке [`docs/`](docs/README.md):

| Раздел | Ссылка |
|--------|--------|
| Оглавление | [docs/README.md](docs/README.md) |
| Начало работы | [docs/getting-started.md](docs/getting-started.md) |
| Основной API | [docs/core-api.md](docs/core-api.md) |

## Быстрый пример (data-oriented)

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

Manifest-style станции (`AttachStation` с прямым доступом к `CargoManifest`) — для инфраструктурных шагов, когда data-oriented `.Station` не подходит. Подробнее — [docs/getting-started.md](docs/getting-started.md#низкоуровневый-api-attachstation).

## Установка

### NuGet

```xml
<PackageReference Include="TrainOP" Version="0.1.0" />
<PackageReference Include="TrainOP.Generators" Version="0.1.0" />
```

### Из исходников (разработка)

```xml
<ProjectReference Include="path/to/src/TrainOP/TrainOP.csproj" />
<ProjectReference Include="path/to/src/TrainOP.Generators/TrainOP.Generators.csproj"
                  OutputItemType="Analyzer"
                  ReferenceOutputAssembly="false" />
```

Генератор нужен для `.Station` data-oriented handlers и typed `Travel()`; базовый `AttachStation` API работает и без него.

Лицензия: [MIT](LICENSE).

### Локальная сборка пакетов

```bash
dotnet pack -c Release
```

`.nupkg` появятся в `src/TrainOP/bin/Release/` и `src/TrainOP.Generators/bin/Release/`.

## Структура решения

- `src/TrainOP` — библиотека (`netstandard2.0`)
- `src/TrainOP.Generators` — инкрементальные generators и анализатор цепочки
- `samples/TrainOP.Samples` — консольные примеры
- `tests/` — xUnit-тесты (сквозной data-oriented sample: `DataOrientedPaymentRouteEndToEndTests`)

## Тесты

```bash
dotnet test TrainOP.sln
```

## Концепции

| Термин | Тип | Роль |
|--------|-----|------|
| Манифест | `CargoManifest` | Неизменяемое хранилище вагонов между станциями |
| Маршрут | `TrainRoute` | Цепочка станций |
| Поезд | `Train` | Исполнитель (`DispatchTrain().Travel()`) |
| Зелёный сигнал | `GreenSignal` | Продолжить маршрут |
| Красный сигнал | `RedSignal` | Остановка с `SignalIssue` |
| Отчёт | `RouteReport` | История визитов и финальный сигнал |
