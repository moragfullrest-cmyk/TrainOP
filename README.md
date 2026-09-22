# TrainOP

[![CI](https://github.com/moragfullrest-cmyk/TrainOP/actions/workflows/ci.yml/badge.svg?branch=master)](https://github.com/moragfullrest-cmyk/TrainOP/actions/workflows/ci.yml)

Библиотека **Railway Oriented Programming** для `.NET` (`netstandard2.0`): маршруты из станций, мутабельный манифест данных и сигналы зелёный/красный. Source generator добавляет **data-oriented** `.Station` handlers.

## Документация

Полное руководство — в папке [`docs/`](docs/README.md).

**Учебник (чтение подряд):** [docs/textbook.md](docs/textbook.md) — принципы, использование и внутреннее устройство одним связным текстом.

| Раздел | Ссылка |
|--------|--------|
| Оглавление | [docs/README.md](docs/README.md) |
| Учебник | [docs/textbook.md](docs/textbook.md) |
| Установка через NuGet | [docs/nuget.md](docs/nuget.md) |
| Начало работы | [docs/getting-started.md](docs/getting-started.md) |
| Основной API | [docs/core-api.md](docs/core-api.md) |
| Архитектура (generator / analyzer / caller / runtime) | [docs/architecture-internals.md](docs/architecture-internals.md) |
| Cross-assembly routes | [docs/cross-assembly-routes.md](docs/cross-assembly-routes.md) |
| Сравнение объёма кода | [docs/code-volume-comparison.md](docs/code-volume-comparison.md) |
| Benchmarks | [benchmarks/README.md](benchmarks/README.md) |

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

// Вход — только seed сверху; Travel() без манифеста
var report = route.Travel();
var paymentId = report.Get<string>("paymentId");
var amount = report.Get<decimal>("amount");

if (!report.ReachedDestination)
{
    Console.WriteLine($"{report.FailureCode}: {report.FailureMessage}");
}
```

## Установка

### NuGet

```bash
dotnet add package TrainOP
```

Один пакет: runtime + source generator / analyzer. Подробнее: [docs/nuget.md](docs/nuget.md).

### Из исходников (разработка)

```xml
<ProjectReference Include="path/to/src/TrainOP/TrainOP.csproj" />
<ProjectReference Include="path/to/src/TrainOP.Generators/TrainOP.Generators.csproj"
                  OutputItemType="Analyzer"
                  ReferenceOutputAssembly="false" />
```

Генератор нужен для `.Station` data-oriented handlers (в NuGet он уже внутри `TrainOP`).

Лицензия: [MIT](LICENSE). Текущая версия пакета: **0.16.0** — см. [CHANGELOG.md](CHANGELOG.md).

### Локальная сборка пакета

```bash
dotnet pack src/TrainOP/TrainOP.csproj -c Release
```

`.nupkg` появится в `src/TrainOP/bin/Release/`.

## Структура решения

- `src/TrainOP` — библиотека (`netstandard2.0`), единственный NuGet-пакет
- `src/TrainOP.Generators` — generators + analyzer (упаковываются внутрь `TrainOP`)
- `samples/TrainOP.Samples` — консольные примеры (в т.ч. вложенные маршруты и ветвление)
- `tests/` — xUnit-тесты (сквозной data-oriented sample: `DataOrientedPaymentRouteEndToEndTests`)
- `benchmarks/` — BenchmarkDotNet: library vs manual pipelines

## Тесты

```bash
dotnet test TrainOP.sln
```

## Бенчмарки

```bash
dotnet run -c Release --project benchmarks/TrainOP.Benchmarks
```

Подробности: [`benchmarks/README.md`](benchmarks/README.md).

## Концепции

| Термин | Тип | Роль |
|--------|-----|------|
| Манифест | `CargoManifest` | Мутабельное хранилище вагонов между станциями |
| Маршрут | `TrainRoute` | Цепочка станций + `Travel` / `TravelAsync` |
| Зелёный сигнал | `RailwaySignals.Green` / данные | Продолжить маршрут (merge в манифест) |
| Красный сигнал | `RailwaySignals.Red` | Остановка с `SignalIssue` (без груза) |
| Отчёт | `RouteReport` | История визитов, `FailureCode` / `FailureMessage`, `Manifest` |
