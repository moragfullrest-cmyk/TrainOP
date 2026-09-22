# План: statement-цепочки на одном локальном якоре

> **Статус:** **этапы 0–4 сделаны** (statement-local закрыт).  
> **Имплементация (единый backlog):** [`plan-anchors-implementation.md`](plan-anchors-implementation.md) — дальше D → C → DOC-4.  
> **Цель:** несколько statement-вызовов `.Station` / `.ServiceStation` на одной локали после одного origin → одна `RouteChain`.  
> **Аудитория:** разработчики и AI-агенты, продолжающие работу над TrainOP.

---

## Цель

Один синтаксический origin на локали + несколько statement-`.Station` / `.ServiceStation` → одна `RouteChain`. Opaque (параметр / поле / свойство / делегат) — по-прежнему TOP005. CFG statement-ветвлений и alias tracking — вне скоупа.

## Общие ограничения (все этапы)

- Порядок станций: `SpanStart` в методе.
- Сброс окна: следующее присваивание локали с **новым** origin RHS; не `route = route.Station(...)`.
- Алиас (`route1 = route.Station(A); route1.Station(B)`) → TOP005 на B.
- Ключевые файлы: [`RouteChainWalker.cs`](../src/TrainOP.Generators/RouteGraph/RouteChainWalker.cs), [`RouteGraphAssembler.cs`](../src/TrainOP.Generators/RouteGraph/RouteGraphAssembler.cs), [`RouteFactoryPathSimulator.cs`](../src/TrainOP.Generators/Discovery/RouteFactoryPathSimulator.cs).

## Этапы (todos)

| Id | Этап |
|----|------|
| `shared-collector` | 0: `CollectLocalStatementStationLinks` + `BuildAnchorKey` по origin location — **сделано** |
| `anchor-bare-new` | 1: якорь `LocalVariable` после bare `new TrainRoute()` + тесты + build gate — **сделано** (gate — у оператора) |
| `anchor-factory-inline` | 2a: якорь `MethodInvocation` — локаль после private/internal factory + EndingAt return + build gate — **сделано** (gate — у оператора) |
| `anchor-factory-schema` | 2b: якорь `FactorySchema` — локаль после public/exported factory + schema StationCount + build gate — **сделано** (gate — у оператора) |
| `anchor-fluent-new` | 3a: origin = fluent-RHS с root `new` + build gate — **сделано** (gate — у оператора) |
| `anchor-fluent-factory` | 3b: origin = fluent-RHS с root factory + build gate — **сделано** (gate — у оператора) |
| `docs-negatives` | 4: docs, алиасы TOP005, негативные тесты; финальный build gate — **сделано** (gate — у оператора) |

## Build gate (каждый этап)

После кода и тестов этапа оператор (или CI) подтверждает зелёный прогон перед следующим этапом:

```bash
dotnet test tests/TrainOP.Generators.Tests/TrainOP.Generators.Tests.csproj -c Release
dotnet test tests/TrainOP.Tests/TrainOP.Tests.csproj -c Release
```

Для этапа 2b дополнительно:

```bash
dotnet test tests/TrainOP.RouteConsumer.Tests/TrainOP.RouteConsumer.Tests.csproj -c Release
```

Критерий gate: 0 failed; новые тесты этапа зелёные; регрессий по существующим LocalVariable/factory fluent нет. Агент **не** запускает билд/тесты сам, пока оператор явно не попросит.

---

## Этап 0 — общий каркас (без новых пользовательских форм)

Инфраструктура statement-сбора; поведение для пользователя ещё не расширяется (или только внутренние хуки за флагом пути `LocalVariable` с bare `new`, если уже есть одна точка входа).

**Сделать:**

- `CollectLocalStatementStationLinks` — окно origin→next origin, statement-корни (receiver = локаль), fluent-хвост через `TryAdvanceChain`.
- `BuildAnchorKey` для локальных якорей от **origin location**, не use-site `SpanStart`.
- `TryGetPrecedingTrainRouteOriginAssignment` — заготовка API (сначала только bare `new`, остальное подключают этапы 1–3).

**Gate:** существующие [`RouteGraphAssemblerTests`](../tests/TrainOP.Generators.Tests/RouteGraphAssemblerTests.cs) / analyzer LocalVariable fluent — зелёные.

---

## Этап 1 — якорь `LocalVariable` (bare `new TrainRoute()`)

### Пример

```csharp
public static TrainRoute Build()
{
    var route = new TrainRoute();
    route.Station("Seed", () => new { id = 1 });
    route.Station("Next", (int id) => new { id = id + 1 });
    return route;
}
```

Смешение fluent на statement:

```csharp
var route = new TrainRoute();
route.Station("A", () => new { id = 1 }).Station("B", (int id) => new { id });
route.Station("C", (int id) => new { id = id + 1 });
```

### Реализация

- Preceding origin: bare `ObjectCreation` `TrainRoute`.
- `BuildChain` / `EndingAt` для bare `return route` собирают statement-станции.
- Kind: `LocalVariable`; seed пустой; `CallerChainKey` с ctor site.

### Тесты + gate

- Одна цепочка, индексы 0..N-1; ctor location для key.
- Analyzer: Seed+Next без TOP001/TOP005; дыра → TOP001.
- Reassignment `route = new TrainRoute()` → две цепочки.
- **Build gate** (Generators + TrainOP.Tests).

---

## Этап 2a — якорь `MethodInvocation` (private/internal factory)

### Пример

```csharp
private static TrainRoute CreateSeed() =>
    new TrainRoute().Station("Seed", () => new { id = 1 });

public static TrainRoute Build()
{
    var route = CreateSeed();
    route.Station("Next", (int id) => new { id = id + 1 });
    return route;
}
```

### Реализация

- Preceding origin: bare factory invocation + `RouteFactoryResolver.TryResolveInline`.
- Anchor: `MethodInvocation`, `InitialWagons`, dispatch через `FactoryDispatchMetadata`.
- Statement-индексы с `upstreamStationCount`.
- `EndingAt` / path simulator: `return route` после statement.

### Тесты + gate

- Цепочка Next с upstream seed; нет TOP001.
- Factory `Build` statement-style → terminals / нет TOP013.
- **Build gate**.

---

## Этап 2b — якорь `FactorySchema` (public / cross-assembly)

### Пример

```csharp
// library
public static TrainRoute Build() =>
    new TrainRoute().Station("Seed", () => new { paymentId = "pay-1", amount = 100m });

// consumer
public static TrainRoute Extend()
{
    var route = PaymentModule.Build();
    route.Station("Finalize", (string paymentId, decimal amount) =>
        new { paymentId, status = "done" });
    return route;
}
```

### Реализация

- Preceding origin: exported factory + schema (`FactorySchema`).
- Schema / `StationCount` корректны, если public factory сама собрана statement-стилем (опирается на EndingAt из 2a).

### Тесты + gate

- [`TrainOP.RouteConsumer.Tests`](../tests/TrainOP.RouteConsumer.Tests) / assembler: statement extension после public factory.
- **Build gate** (+ RouteConsumer.Tests).

---

## Этап 3a — origin = fluent-RHS с root `new`

### Пример

```csharp
public static TrainRoute Build()
{
    var route = new TrainRoute()
        .Station("Seed", () => new { id = 1 });
    route.Station("Next", (int id) => new { id = id + 1 });
    return route;
}
```

### Реализация

- Preceding RHS: Station/ServiceStation chain; `TryFindChainRootEndingAt` → `ObjectCreation`.
- Станции RHS входят в цепочку первыми; statement-корни **после** assignment — хвост (без дубля RHS).

### Тесты + gate

- Одна цепочка Seed→Next; **Build gate**.

---

## Этап 3b — origin = fluent-RHS с root factory

### Пример

```csharp
public static TrainRoute Build()
{
    var route = CreateSeed()
        .Station("Mid", (int id) => new { id });
    route.Station("Tail", (int id) => new { id = id + 1 });
    return route;
}
```

### Реализация

- Как 3a, root = factory (`MethodInvocation` / `FactorySchema`).

### Тесты + gate

- Upstream + Mid + Tail; **Build gate**.

---

## Этап 4 — docs и негативы

### Примеры (негатив)

```csharp
// TOP005 — алиас
route1 = route.Station("A", () => new { id = 1 });
route1.Station("B", (int id) => new { id }); // TOP005
route.Station("C", (int id) => new { id });

// TOP005 — opaque
void Extend(TrainRoute baseRoute) =>
    baseRoute.Station("X", (int id) => new { id });
```

### Документация

- [`core-api.md`](core-api.md), [`architecture-internals.md`](architecture-internals.md), [`textbook.md`](textbook.md): все позитивные формы этапов 1–3b; ветвление statement не поддерживается; алиасы.

### Финальный build gate

```bash
dotnet test TrainOP.sln -c Release
```

---

## Вне скоупа

- Параметр / `WithX(TrainRoute)`.
- CFG `if`/`else` statement-регистраций без join.
- Alias / points-to.

### Способы получить `TrainRoute` в C#, которые не являются допустимым origin (TOP005 / вне модели)

Уже зафиксированы как opaque:

| Способ | Пример |
|--------|--------|
| Параметр метода | `void F(TrainRoute r) => r.Station(...)` |
| Поле / свойство | `_route.Station(...)` / `this.Route.Station(...)` |
| Делегат / `Func<TrainRoute>` | `build().Station(...)` где `build` — delegate |

Прочие C#-пути к экземпляру (тоже не якоря; план их не добавляет):

| Способ | Пример / заметка |
|--------|------------------|
| Элемент массива / списка / span | `routes[i].Station(...)` |
| Opaque tuple / deconstruct | `(var r, _) = GetPair(); r.Station(...)` (узкий literal — OK) |
| `ref` / `in` параметр | `Mutate(ref r); r.Station(...)` (`out` — OK, как factory) |
| `null` / `default` без init | `TrainRoute r = null; r.Station(...)` — TOP005; `r = new …;` перед Station — OK |
| Reflection / `Activator` | `Activator.CreateInstance<TrainRoute>()` |
| Динамик | `((dynamic)x).Station(...)` — вне analyzer |
| LINQ / проекции | `sources.Select(Create).First().Station(...)` |
| Локаль = другая локаль (копия ссылки) | `var r2 = r1; r2.Station(...)` — алиас |
| Локаль = `.Station(...)` другой локали | `r2 = r1.Station(A); r2.Station(B)` — алиас |
| Статический / instance member factory через property get | `Module.Route.Station(...)` — свойство |
| Не user-defined «factory» | методы TrainOP API, не возвращающие анализируемую цепочку |

**Не путать с прозрачными обёртками** (уже снимаются peel, origin под ними должен быть допустимым): `(expr)`, `expr!`, cast, `await`, `await Task.FromResult(...)`.

**Fluent-receiver `?:` / `??` / `switch`** на call site и statement-локаль после forking assign — поддерживаются (join / TOP008); см. C-10/C-11.
