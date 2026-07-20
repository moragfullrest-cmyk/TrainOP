# Архитектура TrainOP: как устроены генератор, interceptors и runtime

Документ для разработчика, который знает C#, но только поверхностно — source generators, Roslyn analyzers и interceptors. Здесь полный путь от `.Station(...)` в исходнике до `RouteReport` в runtime.

Связанные документы: [getting-started](getting-started.md), [core-api](core-api.md), [cross-assembly-routes](cross-assembly-routes.md).

---

## Главная идея в одном абзаце

Вы пишете fluent-маршрут из лямбд. **Генератор** читает имена параметров как ключи вагонов, выводит форму возврата и эмитит типизированные расширения. **Анализатор** симулирует поток вагонов по цепочке и репортит TOP* до runtime. **Interceptors** подставляют на конкретный call site таблицу имён вагонов. **Runtime** тянет поезд по списку адаптеров и мержит возвраты в `CargoManifest`.

```mermaid
flowchart LR
  A["Исходник\n.Station(...)"] --> B["Generator\nсхема handler"]
  B --> C["Extensions.g.cs\n+ Interceptors.g.cs"]
  C --> D["RegisterStation\nадаптер"]
  D --> E["Train.Travel"]
  E --> F["RouteReport"]
```

---

## 1. Метафора и публичные типы

Railway Oriented Programming: станции — шаги пайплайна, зелёный сигнал — продолжить, красный — стоп (опционально через `ServiceStation`). Данные живут в мутабельном манифесте вагонов.

| Термин | Тип | Роль |
|--------|-----|------|
| Манифест | `CargoManifest` | словарь `string → object` между станциями |
| Маршрут | `TrainRoute` | builder: `.Station` / `.ServiceStation` |
| Поезд | `Train` | исполнитель `Travel` / `TravelAsync` |
| Сигнал | `GreenSignal` / `RedSignal` | продолжение или остановка |
| DSL | `RailwaySignals.*` | что возвращает data-handler |
| Отчёт | `RouteReport` | визиты, failure, `Get<T>(wagon)` |

### Минимальный маршрут

```csharp
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
```

- Имена параметров handler'а = ключи вагонов.
- Первая станция без параметров — seed: загружает стартовый груз.
- `Travel()` всегда стартует с **пустого** манифеста; входные данные задаются только seed-станцией (или замыканием внешних переменных в seed).

---

## 2. Compile-time: что делает генератор

`TrainRouteStationGenerator` — `IIncrementalGenerator` в проекте `src/TrainOP.Generators`. Он не «магически» меняет ваши лямбды: он находит вызовы `.Station(...)`, строит схему handler'а и эмитит C#-файлы в compilation.

### Пайплайн

```mermaid
flowchart LR
  S["SyntaxProvider\n.Station / .ServiceStation"] --> R["Resolve handler\nlambda / method group"]
  R --> I["Input / return\nwagons + signals"]
  I --> C["ChainDetector\nfluent route graph"]
  C --> G["TypeSignatureGroup\nгруппировка сигнатур"]
  G --> E["Emit sources\nExtensions + Interceptors"]
```

| Шаг | Компонент | Что делает |
|-----|-----------|------------|
| 1 | `StationSyntaxHelper` | Кандидаты `.Station` / `.ServiceStation` на `TrainRoute` |
| 2 | `TryResolveHandler` | Лямбда, anonymous method, method group / local function из **текущей** compilation (иначе TOP009) |
| 3 | `HandlerInputSchemaBuilder` | Wagon inputs vs framework: `CargoManifest`, `RedSignal`, `SignalIssue`, `CancellationToken`, `ref` |
| 4 | `HandlerReturnInference` | Anonymous/record, value tuple, `GreenPayload`, `RedFailure`, `GreenPass`, `Task<T>`, void |
| 5 | `ChainDetector` + `ChainStationCallIndex` | Привязка call site к цепочке и индексу станции |
| 6 | `TypeSignatureGroup` / `MergedStationSchema` | Группировка по сигнатуре делегата |
| 7 | Emit | `TrainRouteStation.Extensions.g.cs`, при режиме interceptors — `TrainRouteStation.Interceptors.g.cs` |
| 8 | `RouteSchemaExporter` | Schema attributes для public factory (cross-assembly) |

Параллельно `ChainValidationAnalyzer` симулирует граф вагонов по цепочке и сообщает TOP001–TOP013 **без** выполнения кода.

### Что эмитится

- **`TrainRouteStation.Extensions.g.cs`** — типизированные `Station` / `ServiceStation`, `StationCore_*`, таблицы `ChainBinding_*`.
- **`TrainRouteStation.Interceptors.g.cs`** — методы с `[InterceptsLocation]`, которые перехватывают call site.
- Schema для public route factory через `RouteSchemaExporter` (см. [cross-assembly-routes](cross-assembly-routes.md)).

### Допустимые формы handler'а

- лямбда: `(string paymentId, decimal amount) => …`
- anonymous method: `delegate(string paymentId, decimal amount) { … }`
- method group / local function, объявленные в этом проекте

Не поддерживаются: переменные/`Func<>` без dataflow, неоднозначные перегрузки, методы только из referenced DLL без исходников — analyzer сообщает **TOP009**.

### Валидные формы сборки цепочки

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

// 3) Private/internal factory extension
var route = CreateSeed()
    .Station("Next", (int id) => new { id = id + 1 });

// 4) Public factory из referenced assembly (exported schema)
var route = PaymentModule.Build()
    .Station("Finalize", (string paymentId, decimal amount) =>
        new { paymentId, status = "done" });
```

Параметр / поле / свойство / делегат как receiver (`baseRoute.Station(...)`) пока **не** поддерживаются (**TOP005**).

---

## 3. Работа анализатора

Генератор **эмитит** код. Анализатор **не эмитит** ничего: он только ходит по синтаксису/семантике и репортит диагностики в IDE / `dotnet build`. Оба живут в пакете `TrainOP.Generators`, но это разные механизмы Roslyn.

Точка входа: `ChainValidationAnalyzer` (`[DiagnosticAnalyzer(LanguageNames.CSharp)]`).

```mermaid
flowchart TB
  Start["CompilationStart\nSemanticModelAction"] --> Skip["Пропуск *.g.cs"]
  Skip --> Chains["ChainDetector.DetectChains"]
  Chains --> FactoryRes["RouteFactoryResolver\nесли anchor = factory"]
  Chains --> Sim["ChainGraphValidator\n→ ChainGraphSimulator"]
  Start --> Factories["RouteFactoryPathValidator\npublic/exported factories"]
  Start --> Joins["BranchRouteJoinSetFinder\n+ BranchRouteJoinValidator"]
  Joins --> Downstream["Simulate downstream\nс merged terminal wagons"]
  Start --> Orphans["DetectOrphanStationInvocations\nTOP005"]
  Start --> Unsupported["Unsupported handler form\nTOP009"]
```

### Что делает за один проход syntax tree

| Этап | Компонент | Результат |
|------|-----------|-----------|
| Найти цепочки | `ChainDetector` | `RouteChain` (anchor + станции по порядку) |
| Factory как anchor | `RouteFactoryResolver` | Подтянуть upstream schema / TOP011 |
| Симуляция вагонов | `ChainGraphSimulator` через `ChainGraphValidator` | TOP001, TOP002, TOP003, TOP004, TOP006, TOP010 по ходу «виртуального» манифеста |
| Public factory paths | `RouteFactoryPathAnalyzer` + `RouteFactoryPathValidator` | TOP012 / TOP013 (расходящиеся или unknown return paths) |
| Ветвление | `BranchRouteJoinSetFinder` + `BranchRouteJoinValidator` | TOP008; при успешном merge — симуляция хвоста с объединённым терминалом |
| Orphans | `DetectOrphanStationInvocations` | TOP005 на `.Station` вне поддерживаемой цепочки |
| Форма handler'а | `StationSyntaxHelper.TryGetUnsupportedStationHandler` | TOP009 |

Сгенерированный код (`.g.cs`) анализатор **не** анализирует (`ConfigureGeneratedCodeAnalysis(None)` + явный skip по пути).

### Симуляция манифеста (`ChainGraphSimulator`)

Это сердце вагонных проверок. Анализатор **не** вызывает runtime: он station-by-station обновляет модель:

- **Live** — какие вагоны сейчас «есть» и какого типа, где произведены;
- **Removed** — какие были сняты partial-return'ом;
- **HasUnknownReturn** — возврат не разобран статически → дальше осторожнее (в т.ч. factory/join).

На каждой станции:

1. Проверяет, что все required wagon inputs есть в Live (иначе **TOP001**).
2. Сверяет типы Live vs параметр (**TOP002**).
3. Если вагон был Removed, а снова нужен — **TOP003**.
4. Учитывает return: добавляет/обновляет вагоны, снимает omitted regular inputs (как в runtime merge).
5. `return CargoManifest` → **TOP004** (warning).
6. Tuple без имён → **TOP006**.
7. `GreenSignal`/`RedSignal` вместо DSL → **TOP010**.

После симуляции известен **terminal** набор вагонов — его же используют factory schema export и join веток.

### Ветки и join (TOP008)

Когда маршрут развилками сходится обратно в `.Station(...)`:

1. `BranchRouteJoinSetFinder` находит набор веток + downstream-станцию.
2. `BranchRouteJoinValidator` проверяет: все ветки resolvable, нет unknown terminal, нет конфликтов типов одного имени между ветками.
3. Если ok — строится **merged** terminal; хвост симулируется уже от него.
4. Если нет — **TOP008**; на fork-downstream **TOP005** подавляется (join-ошибка важнее orphan).

### Factory return paths (TOP012 / TOP013)

Для exported public factory (`returns TrainRoute`):

1. `RouteFactoryPathAnalyzer` собирает все `return` / expression-body пути.
2. Каждый путь симулируется как цепочка.
3. `RouteFactoryPathValidator` требует согласованный terminal между путями:
   - разные terminal sets → **TOP012**;
   - unknown terminal на пути → **TOP013**.

Именно это позволяет consumer-сборке валидно продолжать `.Station(...)` после `PaymentModule.Build()`.

### Generator vs Analyzer: кто репортит что

Не все TOP* идут из analyzer. Часть возникает при **emit** генератора.

| Код | Кто репортит | Где логика |
|-----|--------------|------------|
| TOP001–TOP006, TOP008–TOP013 | **Analyzer** | `ChainValidationAnalyzer` + simulator / join / factory |
| TOP007 | **Generator** | `TypeSignatureGroup`: два call site с одной type-сигнатурой, но разными именами вагонов (конфликт канона группы) |
| TOP005 / TOP009 | Analyzer | orphans / unsupported form |
| TOP010 | Analyzer (и учитывается при schema) | runtime Signal return |

`WagonParameterAnalyzer` — не DiagnosticAnalyzer, а хелпер: `ref`, nullable value-type, effective type для совместимости вагонов (им пользуются и generator, и симуляция).

### Чем analyzer отличается от generator на практике

| | Generator | Analyzer |
|--|-----------|----------|
| Цель | эмитить `.g.cs` | красные/жёлтые волны в IDE |
| Нужен для сборки data-oriented API | да (без него нет `.Station` overload) | нет (сборка может пройти, если код уже «счастливый») |
| Видит цепочку | да (`ChainStationCallIndex`) | да (`ChainDetector`) |
| Симулирует вагоны | косвенно (для chain bindings / schema) | да, полный walk + TOP* |
| Interceptors | эмитит | не трогает |

Без анализатора библиотека «едет», но ошибки вагонов вылезут в runtime (`KeyNotFoundException` / cast) или вообще как неверный merge. Analyzer переносит эти проверки на compile-time.

---

## 4. Зачем caller dispatch (и почему без него ломается)

### Проблема CLR-сигнатур

Два handler'а `(string, decimal)` — один и тот же тип делегата. Но вагоны могут называться `paymentId`/`amount` в одной цепочке и `orderId`/`total` в другой. Одна overload-расширение не знает, какие имена взять на конкретном call site.

```mermaid
flowchart LR
  Call["Ваш .Station(...)\ncall site"] --> Ix["Interceptor\nInterceptsLocation"]
  Ix --> Core["StationCore_N\n+ ChainBinding"]
  Core --> Reg["RegisterStation\nruntime adapter"]
```

| Режим | Поведение |
|-------|-----------|
| Без interceptor | Вызов идёт в общую `Station(this TrainRoute, string, Func<string, decimal, …>)`. Имена вагонов либо канонические для группы, либо reflection fallback — нельзя надёжно различить два call site с одной сигнатурой типов. |
| С interceptor | Компилятор подменяет call site методом с `[InterceptsLocation(version, opaqueData)]`. Тот зовёт `StationCore_*(…, ChainBinding_*)` с уже известными именами вагонов для этой станции в этой цепочке. |

### Упрощённый вид сгенерированного interceptor

```csharp
// Сгенерированный interceptor (упрощённо)
[InterceptsLocation(1, "…opaque…")] // Program.cs:42
public static TrainRoute Station_0(
    this TrainRoute route, string stationName, TrainStationHandler_… handler)
{
    return TrainRouteStationExtensions.StationCore_Abc(
        route, stationName, handler,
        TrainRouteStationExtensions.ChainBinding_Abc_chain0_1);
}
```

### Режимы ChainDispatch (TrainOP_ChainDispatchMode)

Настраивается через targets генератора (`TrainOP.Generators.targets`):

| Режим | Когда |
|-------|--------|
| `caller` (default) | ctor+ordinal dispatch: идентичность цепочки штампуется на `new TrainRoute()` и дальше resolve идёт по `ChainKey` + `chainStationIndex` |
| `reflection` | явный opt-out: имена вагонов читаются через `StationHandlerParameterNames` в runtime |

Бенчмарки: [`benchmarks/README.md`](../benchmarks/README.md).

---

## 5. Runtime: как едет поезд

```mermaid
flowchart LR
  Seed["Seed\nзагрузка вагонов"] --> Adapter["Adapter\nPullWagon + handler"]
  Adapter --> Merge["StationMerge\nданные → Signal"]
  Merge --> Travel["Train.Travel\nпо плану станций"]
  Travel --> Report["RouteReport\nтерминал / failure"]
```

| Шаг | Что происходит |
|-----|----------------|
| `RegisterStation` | Сгенерированный адаптер кладётся в список `StationPlan` на `TrainRoute` |
| `DispatchTrain` | Снимок планов → `Train` (+ optional `ServiceStationPlan`) |
| `Travel` | Пустой `CargoManifest`; по очереди вызов каждого адаптера |
| Adapter | `PullWagon` по именам → handler → `StationMerge` / `ToSignal` → Green\|Red |
| Green | Манифест из сигнала идёт на следующую станцию; визит пишется в отчёт |
| Red | Опционально `ServiceStation`; green → продолжить, иначе стоп + `FailureCode` / `FailureMessage` |
| Exception | Кроме `OperationCanceledException` → Red с `STATION_EXCEPTION` / `SERVICE_STATION_EXCEPTION` |

### Sync vs Async

Есть async-станция (`Task` / `Task<T>`) → только `TravelAsync`. Синхронный `Travel()` бросит `InvalidOperationException` («Use TravelAsync»).

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

### ServiceStation

Одна «аварийная» станция на маршрут. На Red получает `RedSignal` (и при необходимости `ref` вагоны). Успешное восстановление продолжает оставшиеся станции.

```csharp
var route = new TrainRoute()
    .Station("Seed", () => new { paymentId = "pay-recover", amount = -10m })
    .Station("Validate", (string paymentId, decimal amount) =>
        amount > 0
            ? RailwaySignals.Green(new { paymentId, amount })
            : RailwaySignals.Red("INVALID_TOTAL", "amount must be positive"))
    .ServiceStation("Recovery", (ref string paymentId, ref decimal amount, RedSignal red) =>
    {
        paymentId = "pay-recover";
        amount = 50m;
        return RailwaySignals.Pass;
    })
    .Station("ApplyDiscount", (string paymentId, decimal amount) =>
        new { paymentId, amount = amount * 0.9m });
```

---

## 6. Семантика merge

Handler обычно не трогает манифест руками. Он возвращает данные; адаптер вызывает `StationMerge` (`src/TrainOP/StationMerge.cs`).

| Возврат handler'а | Поведение |
|-------------------|-----------|
| anonymous / record / named tuple | merge членов → Green |
| `RailwaySignals.Green(payload)` | unwrap payload → merge → Green |
| `RailwaySignals.Red(code, msg)` | `RedSignal` + `SignalIssue`, стоп |
| `RailwaySignals.Pass` | манифест без изменений (**без** ref writeback) |
| `void` / `new { }` | partial: `ref` пишутся, обычные input-вагоны выгружаются |
| `CargoManifest` | полная замена (предупреждение TOP004) |
| `GreenSignal` / `RedSignal` | запрещено — **TOP010** |

### Partial return

Если станция принимает `paymentId` и `amount`, а возвращает только `new { amount = 90m }`, вагон `paymentId` снимается с манифеста (как «обычный input, не вернутый»). Лишние вагоны, которые станция **не** принимала, остаются.

### Value tuple

Рекомендуются именованные кортежи или inference:

```csharp
// OK — явное имя
.Station("Discount", (string paymentId, decimal amount) =>
    (paymentId: paymentId + "-disc", amount: amount * 0.9m));

// OK — inference
.Station("Discount", (string paymentId, decimal amount) =>
    (paymentId, amount));

// Warning TOP006 — default ItemN
.Station("Discount", (string paymentId, decimal amount) =>
    (paymentId + "-disc", amount * 0.9m));
```

### Framework-параметры

Помимо вагонов handler может принимать:

- `CargoManifest` — читать лишнее без формального input;
- `CancellationToken`;
- для ServiceStation — `RedSignal` / `SignalIssue`;
- `ref` wagon parameters — writeback через сгенерированные `refLocalValues`.

Nullable value-type wagon: `HasWagon(...) ? PullWagon<T>() : default`.

---

## 7. Примеры сценариев

Исходники: `samples/TrainOP.Samples/Examples/`.

### A. Data-oriented happy path

`DataOrientedStationExample.cs` — seed → discount → validate → `report.Get<T>`.

### B. Red + ServiceStation recovery

`DataOrientedRedSignalExample.cs` — см. раздел ServiceStation выше.

### C. Async + CancellationToken

`AsyncRouteExample.cs` — `TravelAsync`.

### D. Partial wagon return

`PartialWagonReturnExample.cs` — демонстрация снятия omitted inputs.

### E. Cross-assembly

`tests/TrainOP.RouteLib.Tests/PaymentModule.cs` + `tests/TrainOP.RouteConsumer.Tests/AppRoute.cs` — public factory со schema export.

---

## 8. Диагностики TOP*

Сводка. Подробный пайплайн — в [разделе 3](#3-работа-анализатора).

| Код | Смысл | Severity | Источник |
|-----|-------|----------|----------|
| TOP001 | Нужный вагон не появился раньше в цепочке | Error | Analyzer (simulator) |
| TOP002 | Конфликт типов одного имени вагона | Error | Analyzer (simulator) |
| TOP003 | Вагон снят, но нужен позже | Error | Analyzer (simulator) |
| TOP004 | `return CargoManifest` — полная замена | Warning | Analyzer (simulator) |
| TOP005 | Handler вне поддерживаемой цепочки | Error | Analyzer (orphans) |
| TOP006 | Tuple `ItemN` без имени | Warning | Analyzer (simulator) |
| TOP007 | Разные имена вагонов при одной type-сигнатуре | Error | **Generator** (`TypeSignatureGroup`) |
| TOP008 | Ветки маршрута не сходятся | Error | Analyzer (branch join) |
| TOP009 | Неподдерживаемая форма handler'а | Error | Analyzer |
| TOP010 | `return GreenSignal`/`RedSignal` вместо DSL | Error | Analyzer (simulator) |
| TOP011 | External factory без schema | Info | Analyzer (factory resolve) |
| TOP012 | Factory paths с разным терминалом | Error | Analyzer (factory paths) |
| TOP013 | Factory path с unknown terminal | Error | Analyzer (factory paths) |

Описания: `src/TrainOP.Generators/TrainRouteDiagnostics.cs`.

---

## 9. Карта репозитория

| Путь | Назначение |
|------|------------|
| `src/TrainOP` | Runtime: `Railway.cs`, `StationMerge`, `Train` |
| `src/TrainOP.Generators` | Generator + analyzer + interceptors emitter |
| `samples/TrainOP.Samples` | Консольные сценарии |
| `tests/` | Runtime + generator + cross-assembly |
| `docs/` | Руководства пользователя и этот документ |
| `benchmarks/` | Reflection vs interceptors |

Ключевые файлы генератора:

| Файл | Роль |
|------|------|
| `TrainRouteStationGenerator.cs` | Точка входа generator |
| `ChainDetector.cs` | Обнаружение fluent-цепочек |
| `TrainRouteStationInterceptorsEmitter.cs` | Эмит interceptors |
| `StationAdapterBodyEmitter.cs` | Тело адаптера (Pull → invoke → merge) |
| `ChainValidationAnalyzer.cs` | DiagnosticAnalyzer: TOP* (кроме TOP007) |
| `ChainGraphSimulator.cs` | Виртуальный walk манифеста по цепочке |
| `ChainGraphValidator.cs` | Обёртка Simulate → diagnostics |
| `BranchRouteJoinValidator.cs` | Сходимость веток (TOP008) |
| `RouteFactoryPathAnalyzer.cs` / `RouteFactoryPathValidator.cs` | Return paths factory (TOP012/013) |
| `TypeSignatureGroup.cs` | TOP007 при emit генератора |
| `RouteSchemaExporter.cs` | Cross-assembly schema |

---

## Порядок чтения

1. Метафора и минимальный пример (раздел 1).
2. Таблица merge (раздел 6) — без неё поведение возвратов неочевидно.
3. Пайплайн генератора (раздел 2).
4. Работа анализатора (раздел 3) — чем TOP* ловятся до runtime.
5. Зачем interceptors (раздел 4).
6. Travel loop (раздел 5).
7. Сводка TOP* (раздел 8), когда IDE краснеет.

Дальше по необходимости: [core-api](core-api.md) для деталей API, [cross-assembly-routes](cross-assembly-routes.md) для библиотек маршрутов.
