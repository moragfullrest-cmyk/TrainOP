# План: data-oriented handlers (только данные на вход и выход)

> **Статус:** фазы 0–6 завершены; **фазы 7–8** — в плане (якоря цепочки; сторонние сборки)  
> **Цель:** handler станции = чистая функция над данными; `CargoManifest`, `LoadWagon`, `PullWagon`, `RailwaySignals` скрыты в сгенерированном адаптере.  
> **Аудитория:** разработчики и AI-агенты, продолжающие работу над TrainOP.

---

## 1. Целевое состояние

### 1.1. Как выглядит код пользователя

```csharp
public static class PaymentRoute
{
  public static TrainRoute Build() => new TrainRoute()
      .Station("Seed", () => new { paymentId = "pay-1", amount = 100m })
      .Station("Discount", (string paymentId, decimal amount) =>
          new { paymentId, amount = amount * 0.9m })
      .Station("Validate", (string paymentId, decimal amount) =>
          amount > 0
              ? RailwaySignals.Green(new { paymentId, amount })
              : RailwaySignals.Red("INVALID_TOTAL", "amount must be positive"));
}

var report = PaymentRoute.Build().DispatchTrain().Travel();
```

**Без атрибутов.** Analyzer находит цепочку по `new TrainRoute()`…`.Station(…)`; `chainId` = FQN метода/типа-контейнера (например `PaymentRoute.Build`).

**В теле handler'а нет:**
- `CargoManifest`, `LoadWagon`, `PullWagon`, `UnloadWagon`
- `RailwaySignals.Red` / `Green`
- `new CargoManifest()`
- `[TrainTuple]` / `[Wagon]` — **удалены** (фаза 6)

### 1.2. Принципы

| Принцип | Описание |
|---------|----------|
| **Data in** | Параметры handler'а = вагоны; имя параметра = ключ манифеста |
| **Data out** | Анонимный тип / record / tuple / `RailwaySignals.Green` / `RailwaySignals.Red` / `RailwaySignals.Pass` |
| **Adapter generated** | Маппинг manifest ↔ handler — только в `*.g.cs` |
| **Chain validated** | Компилятор проверяет поток вагонов по цепочке станций |
| **Library at boundary** | TrainOP API только в точке сборки маршрута и в runtime-движке |
| **No new attributes** | Data-oriented маршруты **без** `[TrainRouteChain]`, `[TrainTuple]`, `[Wagon]`; схема только из анализа кода |
| **Infer, don't declare** | Цепочка, вагоны, типы — из `new TrainRoute()` + `.Station`, seed, return type |

### 1.2.1. Минимальный пользовательский API

Пользователь пишет **только**:

1. `new TrainRoute()` + `.Station(...)` — единственный публичный API построения маршрута.
2. `.Station(name, (params…) => data)` — handler; **первая станция без входных параметров = seed**.
3. `DispatchTrain().Travel(manifest?)` — запуск; внешний seed через `Travel(manifest)`.

Всё остальное (адаптеры, `PullWagon`/`LoadWagon`, id цепочки, валидация, typed `Travel`) — **генератор и analyzer**, не ручные атрибуты.

`[TrainTuple]` / `[Wagon]` — **удалены** (см. §3.5).

### 1.3. Не-цели (v1)

- Динамическая сборка маршрута в runtime (`foreach` + `RegisterStation`)
- Автоматический анализ произвольных тел lambda (только сигнатура + известные типы возврата)
- Полный отказ от manifest-only станций (останутся для низкоуровневых случаев)
- Plugin-загрузка станций из произвольных DLL без перекомпиляции (см. фазу 8 §3.9 — исследуется **композиция** с заранее собранными библиотеками, не hot-plug)

---

## 2. Текущее состояние (baseline)

### 2.1. Уже есть

- `CargoManifest`, `TrainRoute`, `Train`, сигналы, async, `ServiceStation`
- `[TrainTuple]` + `[Wagon]` → `ToTuple`, `ApplyStationReturn`, `AttachStation<TResult>`, `Deconstruct`
- `WagonStationReturn` — чтение анонимных типов и кортежей (по позиции)
- Сканирование `LoadWagon`/`PullWagon` → internal `TrainWagonCatalog`
- Перегрузки `(CargoManifest, wagons...)` для доступа к «чужим» вагонам

### 2.2. Боли текущего `[TrainTuple]`

См. обсуждение в чате; кратко:
- дублирование схемы (атрибуты vs `LoadWagon`)
- жёсткий набор вагонов на класс
- кортежи в возврате — порядок = порядок `[Wagon]`, не имена
- нет compile-time проверки «вагон есть до станции»
- конфликты `Deconstruct` / overload при нескольких `[TrainTuple]`
- много сгенерированных перегрузок на класс

---

## 3. Архитектура решения

```
┌──────────────────────────────────────────────────────────┐
│ User handler: (T1, T2, ...) => TOut | DataResult<TOut>    │
└────────────────────────────┬─────────────────────────────┘
                             │ per-station adapter (*.g.cs)
┌────────────────────────────▼─────────────────────────────┐
│ StationAdapter: PullWagon → invoke → MergeReturn / Fail    │
│ Uses: WagonStationReturn, ApplyStationReturn (refactored)│
└────────────────────────────┬─────────────────────────────┘
                             │
┌────────────────────────────▼─────────────────────────────┐
│ TrainRoute.RegisterStation (runtime, codegen-only)         │
└──────────────────────────────────────────────────────────┘

Parallel compile-time:
  ChainDetector → ChainGraph → ChainValidator (diagnostics TOP002+)
```

### 3.1. API ошибок — **РЕШЕНИЕ (фаза 0, п.2)**

**Выбор: `RailwaySignals.Green` / `RailwaySignals.Red`**, не `Result<T>`, не исключения для бизнес-ошибок.

| Вариант | Решение |
|---------|---------|
| `RailwaySignals.Green(payload)` / `RailwaySignals.Red(code, msg)` | ✅ целевой API data-handler'ов |
| `Result<T>` (внешняя библиотека) | ❌ лишняя зависимость |
| `throw` в handler'е | ❌ для бизнес-валидации; исключения по-прежнему ловятся движком как `STATION_EXCEPTION` (manifest API) |

**Runtime-типы** (`src/TrainOP/StationDataResult.cs`):

```csharp
public sealed class GreenPayload<T> { public T Value { get; } }
public sealed class RedFailure { public string Code { get; } public string Message { get; } }
public sealed class GreenPass { /* RailwaySignals.Pass */ }

// RailwaySignals.Green<T>(payload), RailwaySignals.Red(code, msg), RailwaySignals.Pass
```

**Допустимые возвраты handler'а (data-oriented):**

| Возврат | Поведение адаптера |
|---------|-------------------|
| Анонимный тип / record / struct | merge по именам полей в манифест → `Green` |
| `(T1, T2, …)` ValueTuple | merge по ordinal (см. §3.4) → `Green` |
| `RailwaySignals.Green(payload)` | merge `payload` → `Green` |
| `RailwaySignals.Red(code, msg)` | `RedSignal` с `SignalIssue(code, msg, stationName)` |
| `RailwaySignals.Pass` | манифест без изменений → `Green` (merge не выполняется; `ref`-мутации в handler не записываются) |
| `CargoManifest` | escape hatch (фаза 5): заменяет манифест целиком → `Green`; analyzer `TOP005` |


**Связь с `ServiceStation`:** тот же контракт `Green` / `Red` / `Pass`; codegen читает вагоны из `red.Manifest`, опционально `SignalIssue` / `RedSignal`. Escape hatch `Func<RedSignal, Signal>` сохранён.
### 3.2. Точка сборки маршрута (fluent, v2 API)

Новый builder **рядом** с `TrainRoute` (не ломать существующий API):

```csharp
public sealed class TrainRoute
{
    public TrainRoute Station<THandler>(string name, THandler handler);  // data-oriented (codegen)
    public TrainRoute RegisterStation(...);  // внутренний API для codegen
    public Train DispatchTrain();
}
```

### 3.3. Якорь цепочки — **РЕШЕНИЕ (фаза 0, п.1, пересмотр)**

**Принцип: анализ кода, не атрибуты.**

| Роль | Механизм |
|------|----------|
| **Граница цепочки** | `new TrainRoute()` + цепочка `.Station(...)` |
| **Идентификатор цепочки** | FQN containing method (например `PaymentRoute.Build`), или location of `new TrainRoute()` |
| **Схема handler'а** | SemanticModel: параметры и return type lambda в `.Station(...)` |
| **Схема seed** | Первая `.Station` с **нулём wagon-параметров**; или `Travel(CargoManifest)` снаружи |
| **Исполнение** | `DispatchTrain().Travel()` / `TravelAsync()` |

**Не используем:**
- `[TrainRouteChain]` и любые новые атрибуты для data-маршрутов (**удалён** `TrainRouteChainAttribute`)
- Обязательный `[TrainTuple]` / `[Wagon]` для новых маршрутов
- Анализ голого `new TrainRoute().RegisterStation(...)` как data-chain (не используется)

#### Целевой паттерн (без атрибутов)

```csharp
public static class PaymentRoute
{
    public static TrainRoute Build() => new TrainRoute()
        .Station("Seed", () => new { paymentId = "pay-1", amount = 100m })
        .Station("Discount", (string paymentId, decimal amount) =>
            new { paymentId, amount = amount * 0.9m });
}
```

#### Зачем `new TrainRoute()` + `.Station`

- `.Station` — data-oriented handler'ы (codegen adapter).
- Analyzer строит граф только по `.Station` на цепочке от `new TrainRoute()`.

#### Алгоритм `ChainDetector` (фаза 2)

1. Найти `ObjectCreationExpression` с типом `TrainRoute`.
2. Обойти цепочку invocations: `.Station`, до `DispatchTrain()` / конца выражения.
3. Для каждого `.Station(name, lambda)` — построить `HandlerSchema` из lambda.
4. Станция без wagon-параметров: вход пуст, выход = seed; или внешний `Travel(manifest)`.
5. `chainId` = символ метода, содержащего выражение.
6. Data-lambda в `.Station` вне цепочки от `new TrainRoute()` → `TOP007`.

#### Runtime (репозиторий)

- Data-oriented `Station((T…) => TOut)` — codegen adapter → `RegisterStation`

| Подход | Статус |
|--------|--------|
| **`new TrainRoute()` + `.Station`** | ✅ якорь для analyzer |
| **`[TrainRouteChain]`** | ❌ удалён |
| **`DataRouteBuilder`** | ❌ удалён — всё на `TrainRoute` |

### 3.8. Расширение якорей цепочки — **ПЛАН (фаза 7)**

#### 3.8.1. Проблема

Сейчас «легитимная» цепочка — только непрерывное синтаксическое выражение:

```csharp
new TrainRoute().Station("A", ...).Station("B", ...)
```

Codegen и analyzer расходятся:

| Сценарий | Codegen | Analyzer |
|----------|---------|----------|
| `var r = new TrainRoute(); r.Station(...)` | ✅ | ❌ TOP006 |
| `base.Station(...).Station(...)` | ✅ | ❌ TOP006 |
| `Build().Station(...)` | ✅ | ❌ TOP006 (если `new` в другом методе) |

#### 3.8.2. Алгоритм `DetectChainRoots`

Псевдокод (замена единственного обхода `ObjectCreationExpression`):

```
roots = empty set

for each ObjectCreationExpression o where type is TrainRoute:
    roots.add(o)

for each IdentifierNameSyntax id where type is TrainRoute:
    if TryGetSingleAssignmentFromTrainRouteCreation(id):
        roots.add(id)

for each ExpressionSyntax expr in candidate receivers of .Station / .ServiceStation:
    if IsTrainRoute(expr) && expr is not already covered as suffix of another root's forward walk:
        if expr is InvocationExpression | MemberAccessExpression | IdentifierNameSyntax:
            roots.add(expr)

unwrap (phase 7b): ParenthesizedExpression, ConditionalExpression arms, AwaitExpression

for each root in roots:
    walk forward with TryAdvanceChain (unchanged)
    emit RouteChain(root, stations)
```

**Правило «уже покрыто»:** если `new TrainRoute().Station("A",...)` уже поглотил `.Station("A")`, не создавать второй корень на том же invocation. Обратный обход: сначала корни от `new`, затем остальные — и вычитать уже собранные invocations.

#### 3.8.3. `chainId` для разных якорей

| Якорь | `chainId` (предложение) |
|-------|-------------------------|
| `new TrainRoute()` в `PaymentRoute.Build` | `PaymentRoute.Build` (как сейчас) |
| Параметр `baseRoute` в `Extend` | `ContainingType.Extend@baseRoute` |
| Поле `_route` | `ContainingType@_route` |
| Локальная `route` | `ContainingMethod@route` |
| `Build()` invocation | `ContainingMethod@Build()` call site |

Нужен для логов, будущего typed travel per-chain (если появится), не для пользовательского API.

#### 3.8.4. Семантика seed при extension

```csharp
public static TrainRoute Extend(TrainRoute baseRoute) =>
    baseRoute
        .Station("Extra", (string paymentId, decimal amount) =>
            new { paymentId, amount = amount + 1m });
```

| Правило | Значение |
|---------|----------|
| Первая `.Station` после якоря-параметра с wagon-параметрами | **не seed**; вход считается из внешнего манифеста |
| `TOP001` на `paymentId` / `amount` | **не** выдавать — upstream вне видимости analyzer'а |
| `TOP001` на второй и далее `.Station` в той же цепочке | как сейчас |
| Seed-станция `() => new { ... }` после параметра | допустима: merge поверх манифеста caller'а (как §3.6) |

#### 3.8.5. Примеры после фазы 7

```csharp
// Локальная (уровень A)
public static TrainRoute Build()
{
    var route = new TrainRoute();
    return route
        .Station("Seed", () => new { id = 1 })
        .Station("Next", (int id) => new { id = id + 1 });
}

// Параметр (composition)
public static TrainRoute WithAudit(TrainRoute inner) =>
    inner.Station("Audit", (string paymentId) => new { paymentId, audited = true });

// Вызов (sub-route в том же выражении)
public static TrainRoute Build() =>
    CreateSeed()
        .Station("Discount", (decimal amount) => new { amount = amount * 0.9m });

static TrainRoute CreateSeed() =>
    new TrainRoute().Station("Seed", () => new { amount = 100m });
// Цепочка в Build(): только "Discount" — вход amount внешний относительно видимого seed в CreateSeed
```

Последний пример — **ограничение v1:** analyzer в `Build()` не связывает terminal wagons `CreateSeed()` с `Discount`; `amount` трактуется как внешний вход (как у параметра). Межпроцедурный вывод внутри одной сборки — вне фазы 7; **межсборочный** — фаза 8 (§3.9).

### 3.9. Маршруты и станции из сторонних сборок — **ПЛАН (фаза 8)**

#### 3.9.1. Сценарий

Библиотека `MyRoutes.dll` (скомпилирована с `TrainOP` + `TrainOP.Generators`) экспортирует готовые фрагменты маршрута. Потребитель `App` достраивает цепочку локальными станциями:

```csharp
// MyRoutes.dll
public static class PaymentModule
{
    public static TrainRoute Build() => new TrainRoute()
        .Station("Seed", () => new { paymentId = "p1", amount = 100m })
        .Station("Discount", (string paymentId, decimal amount) =>
            new { paymentId, amount = amount * 0.9m });
}

// App (ссылка на MyRoutes.dll + TrainOP.Generators)
public static class AppRoute
{
    public static TrainRoute Build() =>
        PaymentModule.Build()
            .Station("Finalize", (string paymentId, decimal amount) =>
                new { paymentId, status = "completed" });
}
```

**Цель фазы 8:** найти и зафиксировать способ, при котором такая композиция **работает в runtime** и по возможности **проверяется compile-time** (поток вагонов через границу сборок).

#### 3.9.2. Ограничение Roslyn (фундамент)

| Слой | Видит исходники `MyRoutes`? | Видит lambda в `App`? |
|------|----------------------------|------------------------|
| `TrainRouteStationGenerator` (App) | ❌ только metadata reference | ✅ |
| `ChainValidationAnalyzer` (App) | ❌ | ✅ (локальная часть цепочки) |
| Runtime | ✅ адаптеры уже в DLL | ✅ codegen в App |

Generator и analyzer работают по `compilation.SyntaxTrees` **текущего** проекта. Тела lambda и синтаксис `.Station` в **referenced assembly недоступны** — только символы (`IMethodSymbol` у `Build()`, тип `TrainRoute`).

Следствия **сейчас**:

- **Runtime:** композиция обычно **работает** — адаптеры библиотеки вшиты в `MyRoutes.dll`, локальная `.Station` генерируется в App.
- **Analyzer в App:** видит только хвост цепочки после `PaymentModule.Build()`; terminal wagons библиотеки **неизвестны** → ложный `TOP001` или «слепая» валидация (как extension-якорь в §3.8.4).
- **Typed `Travel()` в App:** terminal schema выводится только из **видимой** части цепочки, без учёта библиотечного хвоста.

Фаза 7 (якорь = вызов `PaymentModule.Build()`) — **необходимый**, но **недостаточный** предшественник.

#### 3.9.3. Кандидаты решений (spike)

| # | Подход | Compile-time граф | Runtime | Сложность | Примечание |
|---|--------|-------------------|---------|-----------|------------|
| **A** | Чёрный ящик + документация | ❌ внешний upstream неизвестен | ✅ | Низкая | Зафиксировать паттерн после фазы 7; `TOP001` не на первой локальной станции |
| **B** | **Экспорт схемы маршрута** в DLL (metadata) | ✅ при соглашении | ✅ | Средняя | **приоритет spike** — см. §3.9.4 |
| **C** | Явный контракт в public API библиотеки | ✅ если пользователь объявил | ✅ | Средняя | `RouteTerminal<...>`, record с именами вагонов рядом с `Build()` |
| **D** | Runtime-merge маршрутов (`TrainRoute.Concat`) | ❌ | ✅ | Средняя | Две независимые цепочки склеиваются в runtime без единого графа |
| **E** | Source-pack / shared project | ✅ как один проект | ✅ | Низкая | Не «скомпилированная сборка» в чистом виде |
| **F** | Embedded sources в reference | ✅ теоретически | ✅ | Высокая | Нестандартно для NuGet; хрупко |
| **G** | Станции как типизированные делегаты / handlers без lambda в consumer | Частично | ✅ | Высокая | Смена модели API; отдельное исследование |

**Не цель фазы 8:** динамическая подгрузка станций из произвольных DLL без перекомпиляции (`Reflection.Emit`, `foreach` + plugin model).

#### 3.9.4. Предпочтительный вектор (B + элементы C)

Библиотека при компиляции **дополнительно** публикует машиночитаемую схему terminal (и опционально seed) wagons для каждого фабричного метода `TrainRoute`:

```csharp
// emit в MyRoutes.dll (internal или public — TBD)
namespace MyRoutes.RouteSchemas;

[RouteSchemaFor(typeof(PaymentModule), nameof(PaymentModule.Build))]
public static class PaymentModule_Build
{
    public static readonly WagonSlot[] TerminalWagons =
    {
        new("paymentId", typeof(string)),
        new("amount", typeof(decimal)),
    };
}
```

Analyzer в App при якоре `PaymentModule.Build()`:

1. Разрешает `IMethodSymbol` вызова.
2. Ищет связанный тип схемы (атрибут-ссылка **только на границе сборок**, не для внутренних маршрутов — §3.9.6).
3. Подмешивает terminal wagons библиотеки как **виртуальный upstream** перед первой локальной `.Station`.
4. Продолжает обычную симуляцию (`ChainGraphSimulator`) по объединённому графу.

`TrainRouteTravelGenerator` может использовать ту же схему для typed `Travel()` на полной цепочке.

**Альтернатива без атрибута:** соглашение об имени `PaymentModule_BuildSchema` + поиск `INamedTypeSymbol` в сборке метода по `[ModuleInitializer]` / namespace — хуже для рефакторинга; атрибут только для **export**, не для описания handler'ов.

#### 3.9.5. Что уже работает без фазы 8

| Паттерн | Runtime | Analyzer в consumer |
|---------|---------|---------------------|
| `PaymentModule.Build().DispatchTrain().Travel()` | ✅ | N/A (нет локальных станций) |
| Вложенный sub-route через data-oriented `.Station` + `Travel(manifest)` | ✅ | ✅ (см. `NestedBranchingRouteExample`) |
| Локальный хвост после `External.Build()` | ✅ | ❌ / ложные `TOP001` без схемы |
| Две библиотеки, обе с generators | ✅ каждая в своей DLL | ❌ сквозная валидация |

#### 3.9.6. Принцип «атрибут только на границе»

Фазы 0–6: data-маршруты **без** атрибутов на handler'ах. Фаза 8 **не отменяет** это правило:

- Lambda в `.Station` по-прежнему без атрибутов.
- Допустим **опциональный** `[RouteSchemaFor]` (имя TBD) на **сгенерированном** типе схемы в **экспортирующей** сборке — машинный контракт, не ручная разметка бизнес-кода.
- Ручной дубль схемы в библиотеке (вариант C без codegen) — escape hatch для авторов пакетов, не основной путь.

#### 3.9.7. Spike-артефакты

Два проекта в `tests/` или `samples/`:

```
TrainOP.RouteLib.Tests  — class library, маршрут Seed → Discount
TrainOP.RouteConsumer.Tests — ссылается на RouteLib, добавляет Finalize
```

Проверить: ProjectReference и (отдельно) имитация NuGet через `Reference` на собранную DLL.

### 3.10. Typed `Travel()` через interceptors — **РЕШЕНИЕ (фазы 9–12)**

#### 3.10.1. Проблема

Extension `Deconstruct(this RouteReport, out string paymentId, out decimal amount)` не масштабируется: несколько маршрутов с одинаковыми типами вагонов дают конфликт overload'ов. Фаза 3 отказалась от typed-обёрток в пользу `RouteReport.Get<T>()`.

#### 3.10.2. Решение

Roslyn interceptor на **конкретный call site** `Travel()` подменяет вызов методом, возвращающим **уникальный** readonly struct `TravelResult_{chainId}_{siteIndex}` с `Deconstruct` — по аналогии с перехватчиками `.Station()` для `TOP008`.

```csharp
// Пользовательский код (без изменений)
var (paymentId, amount) = PaymentRoute.Build().DispatchTrain().Travel();
var (orderId, total)     = OrderRoute.Build().DispatchTrain().Travel();

// var report = …Travel() — без перехватчика, стандартный RouteReport
```

#### 3.10.3. Архитектура

| Компонент | Роль |
|-----------|------|
| `TerminalWagon` + расширение `ChainSimulationResult` | Финальный набор вагонов цепочки (имя + тип) |
| `TravelCallSiteIndex` | `Travel()` / `TravelAsync()` с tuple-deconstruct слева |
| `TrainRouteTravelInterceptorsEmitter` | Emit struct + `InterceptsLocation` |
| `TravelResult_*` | Поля вагонов + `RouteReport Report`; `Deconstruct` с arity 2..N и опционально +report |

Переиспользуются: `ChainDetector`, `RouteChainIdBuilder`, `InterceptorLocationFormatter`, `ManifestWagonTypes`.

#### 3.10.4. Ограничения

| Сценарий | Поведение |
|----------|-----------|
| `var report = route.Travel()` | `RouteReport`, без перехватчика |
| Красный сигнал | Deconstruct читает `TerminalSignal.Manifest`; полнота не гарантирована |
| Внешний seed через `Travel(manifest)` | Runtime OK; в deconstruct попадают только compile-time terminal-вагоны цепочки |
| IDE / Roslyn без interceptors | Graceful degradation → `report.Get<T>()` |

#### 3.10.5. Фазы

| Фаза | Содержание |
|------|------------|
| **9** | Sync `Travel()`, якорь `new TrainRoute()` |
| **10** | `TravelAsync` + `await var (…) = …` |
| **11** | Расширенные якоря (после фазы 7) |
| **12** | Opt-in `[TrainRouteTerminal]` (опционально) |

### 3.4. Маппинг кортежей — **РЕШЕНИЕ (фаза 0, п.3)**


| Контекст | Правило сопоставления возврата |
|----------|-------------------------------|
| **Data-oriented** (`.Station(lambda)`) | Имена полей анонимного типа / record → ключи манифеста. Для `ValueTuple`: **ordinal = порядок wagon-параметров handler'а** (слева направо), без `CargoManifest` и `CancellationToken` |
| **Legacy `[TrainTuple]`** | Без изменений: ordinal tuple = порядок свойств класса с `[Wagon]` |

**Рекомендация для нового кода:** анонимные типы и records; кортежи в возврате — только если осознанно (analyzer `TOP006`).

**Пример (data-oriented):**

```csharp
.Station("X", (string paymentId, decimal amount) =>
    (paymentId + "-ok", amount + 1m))
// Item1 → "paymentId", Item2 → "amount" (порядок параметров, не алфавит имён)
```

**Merge omitted regular inputs:** если handler вернул подмножество wagon-параметров, невозвращённые **обычные** (не `ref`) входы удаляются из манифеста — как сейчас в `ApplyStationReturn`.

### 3.5. Судьба `[TrainTuple]` — **УДАЛЕНО**

`TrainTupleAttribute`, `WagonAttribute` и `WagonTupleGenerator` удалены. Единственный путь описания вагонов — data-oriented `.Station` (имена параметров handler'а).

### 3.6. Seed — уточнения (фаза 0)

| Сценарий | Правило |
|----------|---------|
| Первая `.Station` без wagon-параметров | Seed: `() => new { … }` или `() => value` (фаза 1) |
| Только `CancellationToken` | Не считается wagon-параметром; станция всё ещё seed |
| `CargoManifest` первым параметром | Не seed; wagon-параметры после него участвуют в графе (фаза 5) |
| Внешний seed | `DispatchTrain().Travel(manifest)` — манифест до первой станции |
| Две seed-станции подряд | Допустимо: вторая merge'ит поверх результата первой |
| Seed + `Travel(manifest)` | Манифест из `Travel` **мержится** перед первой станцией (как сейчас в runtime) |

### 3.7. `StationMerge` — **РЕШЕНИЕ (фаза 0)**

Утверждён shared helper §6.1: `StationMerge.Apply` / `StationMerge.ToSignal` для `TrainRouteStationGenerator`.

**Опциональные вагоны (фаза 5):** `T?` / `Nullable<T>` → `HasWagon` + `default` без throw; не часть фазы 1.

## 4. Фазы реализации

**Очередь (не выполнено):** фаза 7 → фаза 8 → фаза 9 → фаза 10 → фаза 11 → фаза 12 (опционально). Фазы 9–10 могут стартовать параллельно с 7–8 при якоре `new TrainRoute()`; фаза 11 зависит от 7; фаза 12 — после 9 (и опционально 8).

### Фаза 0 — Дизайн и критерии готовности (1 PR, docs only) ✅

**Задачи:**
- [x] Утвердить якорь цепочки — **`new TrainRoute()` + `.Station` + analyzer, без атрибутов** (см. §3.3)
- [x] Утвердить API ошибок — **`RailwaySignals.Green` / `Red` / `Pass`** (см. §3.1)
- [x] Утвердить правила маппинга кортежей — **ordinal = порядок wagon-параметров handler'а** (см. §3.4)
- [x] Утвердить список диагностик — **`TOP001`–`TOP008`** (см. §5)
- [x] Судьба `[TrainTuple]` — **удалён** (см. §3.5)

**Файлы:** этот документ, `docs/README.md` (ссылка на план).

**Критерий:** агент может начать фазу 1 без дополнительных решений. **Выполнено.**

---

### Фаза 1 — Per-handler адаптер без `[TrainTuple]` (MVP glue) ✅

**Цель:** handler `(string paymentId, decimal amount) => new { ... }` работает **без** `[TrainTuple]`.

**Задачи:**
- [x] Новый generator: `TrainRouteStationGenerator` (**отдельно** от `WagonTupleGenerator`, см. §3.5)
- [x] Сканировать `.Station(...)` на `TrainRoute` (не путать с `RegisterStation` для data-graph)
- [x] Из SemanticModel извлечь: имена и типы параметров, тип возврата, `CancellationToken`, `CargoManifest` escape
- [x] Emit адаптер: `PullWagon` → handler → `StationMerge.ToSignal`
- [x] `StationMerge.Apply` + `RailwaySignals` runtime (`StationDataResult.cs`, `StationMerge.cs`)
- [x] Поддержать возврат: анонимный тип, tuple, `StationDataOk<>`, `StationDataFail`, `CargoManifest`
- [x] Тесты: `DataOrientedStationTests` без `[TrainTuple]`

**Не сделано в фазе 1 (отложено):** `ref`-вагоны, валидация цепочки, typed `Travel()`.

**Критерий:** тест-проект с `TrainRoute.Station`, handler'ы без TrainOP в теле. **Выполнено.**

**API-изменение:** удалены публичные manifest-перегрузки; codegen регистрирует станции через `RegisterStation` (`[EditorBrowsable(Never)]`).

---

### Фаза 2 — Валидация цепочки (analyzer only) ✅

**Цель:** compile-time ошибки при нарушении потока вагонов.

**Задачи:**
- [x] `ChainDetector`: от якоря собрать упорядоченный список станций и их схем in/out
- [x] `ChainGraphValidator`: для каждой станции — required inputs, produced outputs, removed keys (partial return)
- [x] Диагностики (§5): missing wagon, type conflict, use-after-remove (`TOP002`–`TOP004`, `TOP007`; warnings `TOP005`–`TOP006`, info `TOP008`)
- [x] Unit-тесты analyzer'а (`ChainValidationAnalyzerTests`)

**Не делать:** исправление кода, автогенерация seed.

**Критерий:** заведомо сломанный маршрут не компилируется с понятной диагностикой. **Выполнено.**

---

### Фаза 3 — typed результат `Travel` ✅

**Задачи:**
- [x] Генерировать `Travel()` extension для цепочки: `(T1 w1, T2 w2, RouteReport report)`
- [x] Union «живых» вагонов на конце цепочки = fold графа из фазы 2
- [x] Async: `TravelAsync` + `CancellationToken` в handler

**Критерий:** пример из §1.1 полностью работает. **Выполнено.**

**Реализация:** typed-обёртки terminal-отчёта удалены; доступ к итоговым вагонам — через `RouteReport` (`report["name"]`, `report.Get<T>("name")`). Typed deconstruct планируется заново в **фазах 9–12** (interceptors, §3.10).

---

### Фаза 4 — Ошибки и красные сигналы без `RailwaySignals` в handler ✅

**Задачи:**
- [x] `RailwaySignals.Red(code, message)` в возврате → adapter → `RedSignal`
- [x] `RailwaySignals.Pass` / pass-through (без изменений)
- [x] Документировать взаимодействие с `ServiceStation` (остаётся manifest-level)

**Критерий:** станция валидации без импорта `RailwaySignals` в файле handler'а. **Выполнено.**

---

### Фаза 5 — Ref-вагоны и manifest escape hatch ✅

**Задачи:**
- [x] `ref` параметры: те же правила, что сейчас (`StationLocals`, omit → write ref value)
- [x] Опциональный `CargoManifest manifest` первым параметром — без отдельного `[TrainTuple]`
- [x] Опциональные вагоны: `decimal? amount` → `HasWagon` + default (см. §3.7; реализация фаза 5)

**Критерий:** портировать `RefAmountWagonTupleTests` на новый API. **Выполнено** (`DataOrientedRefAmountTests`).

---

### Фаза 6 — Миграция и удаление legacy ✅

**Задачи:**
- [x] README и docs: primary path = data-oriented
- [x] Сквозной sample `DataOrientedPaymentRouteEndToEndTests`
- [x] Удалены `TrainTupleAttribute`, `WagonAttribute`, `WagonTupleGenerator`, публичный `AttachStation`, legacy-тесты и docs

**Критерий:** один сквозной sample в `tests/` на новом API; legacy API отсутствует. **Выполнено.**

---

### Фаза 7 — Расширение якорей цепочки (analyzer + travel generator) — **ПЛАН**

**Цель:** `ChainDetector` и `TrainRouteTravelGenerator` распознают data-oriented цепочки не только от `new TrainRoute()`, но и от других легитимных источников `TrainRoute`, без ложных `TOP006` (осиротевшие handler'ы).

**Контекст (текущее ограничение):**

- Codegen (`TrainRouteStationGenerator`) уже принимает любой receiver типа `TrainRoute` (`IsTrainRouteReceiver`).
- Analyzer (`ChainDetector`) стартует **только** с `ObjectCreationExpressionSyntax` (`new TrainRoute()`).
- Разрыв fluent-выражения (локальная переменная, параметр, поле) → codegen есть, `TOP006` — ложное срабатывание.

См. детальный дизайн §3.8.

#### 7.1. Таксономия якорей

| Уровень | Якорь | Пример | Примечание |
|---------|-------|--------|------------|
| **A (v1 фазы 7)** | `new TrainRoute()` | `new TrainRoute().Station(...)` | ✅ уже есть |
| **A** | Вызов метода / свойства | `BuildRoute().Station(...)`, `Routes.Shared.Station(...)` | receiver = `InvocationExpression` или `MemberAccess` на property |
| **A** | Параметр метода | `baseRoute.Station(...)` | extension / composition |
| **A** | Поле / свойство | `_route.Station(...)`, `this.Route.Station(...)` | static или instance |
| **A** | Локальная после прямого `new` | `var r = new TrainRoute(); r.Station(...)` | см. §3.8.2 — одно присваивание в том же методе |
| **B (v2 фазы 7)** | Parenthesized / conditional | `(GetRoute()).Station(...)`, `(a ? r1 : r2).Station(...)` | unwrap до внутреннего якоря |
| **B** | `await` | `(await GetRouteAsync()).Station(...)` | unwrap `AwaitExpression` |
| **C (отложено)** | Индексатор | `routes[i].Station(...)` | нет стабильного `chainId` |
| **C** | Произвольный data flow | `var r = Factory(); r.Station(...)` | требует flow analysis |
| **❌** | Динамическая сборка | `foreach` + `RegisterStation` | не-цель (§1.3) |

**Не дублировать** перечисление в документации: семантически все якоря уровня A — «выражение статического типа `TrainRoute`», отличие только в **синтаксической форме корня** для обхода.

#### 7.2. Задачи реализации

**Модель данных**

- [ ] Ввести `RouteChainAnchor` (kind + syntax location + optional `IMethodSymbol` containing method).
- [ ] Расширить `RouteChain`: якорь вместо только `Location` от `new`.
- [ ] `chainId`: FQN containing method + стабильный суффикс якоря (см. §3.8.3).

**`ChainDetector`**

- [ ] `DetectChainRoots(syntaxTree, semanticModel)` → множество корневых `ExpressionSyntax`.
- [ ] Для каждого корня — существующий `TryAdvanceChain` (без изменений логики fluent-обхода).
- [ ] `CollectChainedStationInvocations` — union по всем корням.
- [ ] Дедупликация: одна `.Station` не должна входить в две цепочки; при конфликте — более «внутренний» якорь (ближе к `new`) или diagnostic `TOP009` (см. §5.1).

**Локальная переменная (уровень A)**

- [ ] `TryGetSingleAssignmentFromTrainRouteCreation(IdentifierNameSyntax, semanticModel)` — в том же `IMethodSymbol` одно присваивание вида `var x = new TrainRoute()` / `TrainRoute x = new TrainRoute()`.
- [ ] Не считать якорем после второго присваивания тому же символу в методе.
- [ ] Не следовать через `out` / `ref` / поля структуры.

**Seed и валидация при extension-якоре**

- [ ] `ChainGraphValidator`: для extension-якоря первая станция с wagon-параметрами — внешний вход (`TOP001` не применять к недостающим upstream-вагонам).
- [ ] `TOP007` (unused seed) — только если первая станция цепочки — seed (`0` wagon-параметров).
- [ ] Документировать: типизированная композиция `Extend(TrainRoute base)` не выводит схему `base` без межпроцедурного анализа.

**Диагностики**

- [ ] Уточнить текст `TOP006`: не только `new TrainRoute()`, а «часть цепочки от легитимного якоря `TrainRoute`» (§5.1).
- [ ] Опционально `TOP009` (Info/Warning): двусмысленная принадлежность станции к двум цепочкам.

**Потребители**

- [ ] `ChainValidationAnalyzer` — без изменений контракта, только новые корни.
- [ ] `TrainRouteTravelGenerator` — typed `Travel()` interceptors (фазы 9–12, §3.10); в фазе 7 — только подхват цепочек через `DetectChains` для validator

**Тесты** (`ChainValidationAnalyzerTests` + при необходимости travel)

- [ ] `var r = new TrainRoute(); return r.Station(...)` — нет `TOP006`.
- [ ] Extension-цепочка: `TOP001` не на первой `.Station` с wagon-параметрами после якоря-параметра/поля/вызова.
- [ ] `Build().Station(...)` когда `Build()` => `new TrainRoute()` в другом методе — цепочка в caller'е.
- [ ] `_field.Station(...)` — нет `TOP006`.
- [ ] Двойное присваивание локальной — по-прежнему `TOP006` (или явный negative test).
- [ ] Регрессия: существующие тесты фазы 2 без изменений поведения для `new TrainRoute()` fluent.

**Документация**

- [ ] `docs/core-api.md` — допустимые формы сборки маршрута.
- [ ] `docs/getting-started.md` — краткий пример extension через параметр.

#### 7.3. Критерий готовности

- Паттерны уровня **A** из §7.1 не дают `TOP006`.
- Валидация вагонов (`TOP001`–`TOP003`) работает внутри цепочки как сейчас.
- Extension-цепочка от параметра не требует фиктивной seed-станции.
- `dotnet test TrainOP.sln` зелёный.

#### 7.4. Не делать в фазе 7

- Межпроцедурный merge графов (`PaymentRoute.Build()` + `Extend(that)` с проверкой совместимости terminal wagons).
- Flow analysis от произвольного factory-метода.
- Runtime-изменения (`Railway.cs`).
- Новые атрибуты для якоря.

---

### Фаза 8 — Сторонние сборки и композиция маршрутов — **ПЛАН (spike → решение)**

**Цель:** исследовать и внедрить способ строить маршруты с участием **скомпилированных** сторонних библиотек (NuGet / ProjectReference): готовые станции из DLL + локальные `.Station` в потребителе, с предсказуемым runtime и максимально полной compile-time проверкой.

**Зависимости:** фаза 7 (якорь = вызов `ExternalModule.Build()`).

См. §3.9.

#### 8.1. Вопросы spike (ответить до реализации)

- [ ] Достаточно ли варианта **A** (чёрный ящик + правила `TOP001`) для v1 межсборочной композиции?
- [ ] Какой формат экспорта схемы (**B**): атрибут на generated type, embedded resource, или public record рядом с `Build()`?
- [ ] Нужен ли `TrainRoute.Concat` (**D**) как runtime API, если есть fluent `External.Build().Station(...)`?
- [ ] Как версионировать схему при изменении библиотеки (consumer старше / новее RouteLib)?
- [ ] Работает ли symbol lookup одинаково для ProjectReference и NuGet metadata reference?

#### 8.2. Задачи spike (исследование)

- [ ] PoC: `RouteLib` + `RouteConsumer` (два проекта в solution).
- [ ] Зафиксировать baseline: runtime OK, какие диагностики в consumer без доработок.
- [ ] Прототип emit схемы в `TrainRouteStationGenerator` / отдельный `RouteSchemaExporter` для методов, возвращающих `TrainRoute` с data-chain.
- [ ] Прототип чтения схемы в `ChainDetector` / `ChainGraphValidator` при внешнем якоре-вызове.
- [ ] Оценить влияние на typed `Travel()` (фазы 9–12): terminal-схема из экспорта vs только локальный хвост.
- [ ] Документ `docs/cross-assembly-routes.md` с выбранным решением и примерами для авторов NuGet-пакетов.

#### 8.3. Задачи реализации (после выбора вектора)

**Экспорт (библиотека-маршрут)**

- [ ] При обнаружении полной data-chain в проекте — emit тип схемы + wagon slots (terminal; опционально seed).
- [ ] Связь «метод `Build` → тип схемы» (атрибут или naming convention).
- [ ] Схема в сборке: `public` или `internal` + `InternalsVisibleTo` для тестов — TBD на spike.

**Импорт (потребитель)**

- [ ] `TryResolveExternalRouteSchema(IMethodSymbol factoryMethod)` → `WagonSlot[]`.
- [ ] Объединение внешней схемы с локальным хвостом цепочки; `TOP001`/`TOP002`/`TOP003` на стыке.
- [ ] Fallback: схема не найдена → поведение как extension-якорь §3.8.4 (внешний вход неизвестен), опционально `TOP010` Info.

**Тесты**

- [ ] Analyzer: consumer компилируется без ложного `TOP001` при известной схеме RouteLib.
- [ ] Analyzer: несовместимые типы на стыке → `TOP002`.
- [ ] Negative: RouteLib без схемы (старая версия) → graceful degradation.
- [ ] E2E: `RouteLib.Build().Station(...).DispatchTrain().Travel()` — корректный manifest.

**Документация**

- [ ] `docs/nuget.md` — раздел «маршруты в библиотеке и композиция в приложении».
- [ ] `docs/core-api.md` — паттерн extension после внешнего `Build()`.

#### 8.4. Критерий готовности

- Автор пакета `MyRoutes` публикует `Build()`; потребитель добавляет `.Station` без ложных `TOP001` при совпадении контракта.
- Стык библиотека → локальная станция ловит конфликт типов (`TOP002`) на этапе компиляции consumer.
- Решение задокументировано; отклонённые варианты (D, E, F, G) кратко обоснованы в §3.9 или `cross-assembly-routes.md`.
- `dotnet test TrainOP.sln` зелёный.

#### 8.5. Не делать в фазе 8

- Plugin-модель / загрузка handler'ов из произвольных сборок в runtime.
- Анализ IL или decompile referenced assembly.
- Обязательные атрибуты на пользовательских lambda-handler'ах.
- Межсборочный вывод без экспортированной схемы (полный symbolic execution по metadata).

---

### Фаза 9 — Typed `Travel()` через Roslyn interceptors (sync MVP) — **ПЛАН**

**Цель:** вернуть эргономику `var (paymentId, amount) = route.DispatchTrain().Travel()` без конфликтующих `Deconstruct(this RouteReport, …)` extension'ов, когда в проекте несколько маршрутов с одинаковыми типами вагонов, но разными именами.

**Контекст (текущее ограничение):**

- Фаза 3 убрала typed-обёртки; доступ к терминальным вагонам — через `RouteReport.Get<T>()` / индексатор.
- Extension `Deconstruct` на `RouteReport` не масштабируется: `(out string paymentId, out decimal amount)` и `(out string orderId, out decimal total)` — одна сигнатура для компилятора.
- Перехватчики для `.Station()` (ветка `interceptors`, `TOP008`) уже решают аналогичную проблему per call site; тот же паттерн применим к `Travel()`.

См. дизайн §3.10.

#### 9.1. Условия эмиссии перехватчика

Перехватчик **только** когда одновременно:

| # | Условие |
|---|---------|
| 1 | Родитель `Travel()` — deconstruct-assignment: `var (a, b) = …Travel()` (не `var report = …`) |
| 2 | `TrainRoute` трассируется к якорю цепочки (`new TrainRoute()`; после фазы 7 — расширенные якоря) |
| 3 | `ChainGraphSimulator` дал конкретную terminal-схему (`!HasUnknownReturn`) |
| 4 | Все terminal-типы проходят `ManifestWagonTypes.IsSupported` |
| 5 | Число терминальных вагонов ≤ лимита (TBD, по умолчанию 8) |

`var report = route.DispatchTrain().Travel()` — **без** перехватчика, стандартный `RouteReport`.

#### 9.2. Задачи реализации

**Модель данных**

- [ ] `TerminalWagon` (name + type display) в `ChainSimulationResult` — fold `LiveOrder` + `Live` после симуляции цепочки.
- [ ] `TravelCallSite` (invocation, chainId, terminal wagons, deconstruct arity).

**Индексация**

- [ ] `TravelCallSiteIndex` — обход syntax tree, поиск `Travel()` / перегрузок с `CargoManifest` / `CancellationToken` при tuple-deconstruct слева.
- [ ] Трассировка receiver: `…DispatchTrain().Travel()` → якорь `TrainRoute` (как `ChainStationCallIndex`).

**Codegen**

- [ ] `TrainRouteTravelInterceptorsEmitter` — аналог `TrainRouteStationInterceptorsEmitter`.
- [ ] Per-site readonly struct `TravelResult_{chainId}_{siteIndex}` с полями вагонов + `RouteReport Report`.
- [ ] `Deconstruct(out T1 w1, …)` и опционально `Deconstruct(…, out RouteReport report)`.
- [ ] Interceptor-методы для всех sync-перегрузок `Travel()`.

**Потребители / интеграция**

- [ ] Подключить эмиссию в `TrainRouteTravelGenerator` (или объединить с `TrainRouteStationGenerator` — TBD).
- [ ] Переиспользовать `InterceptorLocationFormatter`, `RouteChainIdBuilder`.

**Тесты**

- [ ] Generator: эмиссия struct + `InterceptsLocation` для deconstruct-assignment.
- [ ] Generator: **нет** перехватчика для `var report = …Travel()`.
- [ ] Generator: разные `TravelResult_*` для отдельных цепочек с одинаковыми типами (`SeparateChainRoutes`).
- [ ] Runtime: deconstruct payment/order маршрутов без перекрёстного загрязнения вагонов.
- [ ] Runtime: `var (a, b, report) = …Travel()` — третий элемент `RouteReport`.

**Документация**

- [ ] `docs/core-api.md` — typed deconstruct vs `report.Get<T>()`.
- [ ] `docs/nuget.md` — убрать/уточнить устаревшую формулировку «typed tuple как признак генератора».

#### 9.3. Критерий готовности

- `var (paymentId, amount) = PaymentRoute.Build().DispatchTrain().Travel()` компилируется и возвращает корректные значения.
- Два маршрута с `(string, decimal)` и разными именами вагонов deconstruct'ятся независимо.
- `var report = …Travel()` по-прежнему возвращает `RouteReport` без перехватчика.

#### 9.4. Не делать в фазе 9

- `TravelAsync` / `await var (…) = …` (фаза 10).
- Якоря кроме `new TrainRoute()` (фаза 11, зависит от фазы 7).
- Opt-in атрибут (фаза 12).
- Подмена `Travel()` когда результат не deconstruct'ится.
- Runtime-изменения контракта `Train.Travel` (только generated interceptors).

---

### Фаза 10 — Typed `TravelAsync()` через interceptors — **ПЛАН**

**Цель:** тот же typed deconstruct для async-маршрутов.

**Зависимости:** фаза 9.

#### 10.1. Задачи

- [ ] `TravelCallSiteIndex` — `await var (a, b) = …TravelAsync()` и `var x = await …TravelAsync()` (без перехватчика для второго).
- [ ] Interceptor возвращает `Task<TravelResult_*>`; внутри — `train.TravelAsync(…)`.
- [ ] Перегрузки: `TravelAsync()`, `TravelAsync(ct)`, `TravelAsync(manifest)`, `TravelAsync(manifest, ct)`.
- [ ] Generator + runtime тесты на async-цепочку (`.Station` + `TravelAsync`).

#### 10.2. Критерий готовности

- `await var (paymentId, amount) = route.DispatchTrain().TravelAsync()` работает на существующих async-тестах.
- Синхронный путь фазы 9 не регрессирует.

#### 10.3. Не делать в фазе 10

- `IAsyncEnumerable` / streaming travel.
- Комбинированный interceptor на цепочку `DispatchTrain().TravelAsync()` целиком (только `TravelAsync` invocation).

---

### Фаза 11 — Typed `Travel()` на расширенных якорях — **ПЛАН**

**Цель:** deconstruct работает там же, где фаза 7 убирает ложные `TOP006` — локальная после `new`, параметр, поле, вызов `Build()`.

**Зависимости:** фазы 7, 9.

#### 11.1. Задачи

- [ ] `TravelCallSiteIndex` — трассировка `TrainRoute` через якоря уровня A из §7.1 (не только `new TrainRoute()` fluent).
- [ ] `chainId` согласован с `RouteChainIdBuilder` фазы 7.
- [ ] Negative: deconstruct на `Travel()` вне привязанной цепочки → `TOP014`.
- [ ] Тесты: `var r = new TrainRoute(); var (…) = r.DispatchTrain().Travel()`; `Extend(baseRoute).DispatchTrain().Travel()`.

#### 11.2. Критерий готовности

- Паттерны уровня A из фазы 7 поддерживают typed deconstruct без дублирования перехватчиков на одну `.Travel()` invocation.

#### 11.3. Не делать в фазе 11

- Flow analysis от произвольного factory (уровень C фазы 7).
- Межсборочная terminal-схема (фаза 8 + 12).

---

### Фаза 12 — Opt-in `[TrainRouteTerminal]` для typed `Travel()` — **ПЛАН (опционально)**

**Цель:** escape hatch, когда статический анализ не связывает `Travel()` с цепочкой, но автор явно указывает terminal-схему.

**Зависимости:** фаза 9; опционально фазы 7–8 для межсборочных сценариев.

#### 12.1. Задачи

- [ ] Атрибут `[TrainRouteTerminal]` на метод, возвращающий `TrainRoute` (имя TBD).
- [ ] Emit или ручное объявление terminal wagon slots; связь «метод → схема» для `TravelCallSiteIndex`.
- [ ] Analyzer: атрибут без data-chain → warning; конфликт с выведенной схемой → error.
- [ ] Документация в `docs/core-api.md` — когда нужен opt-in vs автоматический вывод.

#### 12.2. Критерий готовности

- Маршрут, собранный через нестандартный factory с `[TrainRouteTerminal]`, поддерживает `var (…) = …Travel()` без `TOP014`.

#### 12.3. Не делать в фазе 12

- Атрибуты на lambda-handler'ах (принцип §3.9.6 сохраняется).
- Замена экспорта схемы фазы 8 — только дополнение.

---

## 5. Диагностики — **УТВЕРЖДЕНО (фаза 0, п.4)**

| ID | Severity | Условие | Message format |
|----|----------|---------|----------------|
| `TOP001` | Error | Станция требует вагон, не произведённый ранее в цепочке | `Station '{0}' requires wagon '{1}', which is not available from earlier stations in this route` |
| `TOP002` | Error | Конфликт типов одного вагона между станциями | `Wagon '{0}' has conflicting types: '{1}' (produced at '{2}') vs '{3}' (required at '{4}')` |
| `TOP003` | Error | Вагон удалён частичным возвратом, но нужен дальше | `Wagon '{0}' was removed at station '{1}' but is required at station '{2}'` |
| `TOP004` | Warning | Handler вернул `CargoManifest` — полная замена | `Station '{0}' returns CargoManifest, which replaces the entire manifest; wagons not in the return value may be lost` |
| `TOP005` | Warning | ValueTuple в возврате data-handler'а | `Station '{0}' returns an unnamed tuple; element order must match handler parameter order ({1}). Prefer anonymous types, records, or named tuples.` |
| `TOP006` | Error | Data-lambda вне цепочки от легитимного якоря `TrainRoute` | Сейчас: `...starting with 'new TrainRoute()'`; после фазы 7 — §3.8 |
| `TOP007` | Info | Вагон из seed не используется downstream | `Wagon '{0}' produced at seed station '{1}' is never consumed by later stations` |
| `TOP008` | Error | Конфликт имён вагонов для одной сигнатуры handler'а | `Handler wagon names ({0}) do not match the canonical names ({1})...` |

### 5.1. Диагностики фазы 7 (черновик)

| ID | Severity | Условие | Примечание |
|----|----------|---------|------------|
| `TOP006` | Error | Осиротевший data-handler | Текст сообщения обновить после расширения якорей (§7.2) |
| `TOP009` | Info | Станция достижима из двух корней | Опционально; можно отложить |

### 5.2. Диагностики фазы 8 (черновик)

| ID | Severity | Условие | Примечание |
|----|----------|---------|------------|
| `TOP010` | Info | Внешний `TrainRoute` без экспортированной схемы | «Стык с `{0}` не проверяется; обновите RouteLib или объявите контракт» — TBD на spike |

### 5.3. Диагностики фаз 9–12 (черновик, typed `Travel()`)

| ID | Severity | Условие | Примечание |
|----|----------|---------|------------|
| `TOP011` | Info | Deconstruct при неизвестной terminal-схеме | Перехватчик не эмитится; использовать `RouteReport.Get<T>()` |
| `TOP012` | Warning | Тип terminal-вагона не поддержан для typed travel | `ManifestWagonTypes.IsSupported` == false |
| `TOP013` | Error | Arity deconstruct не совпадает с числом terminal-вагонов | `var (a) = …Travel()` при двух вагонах в цепочке |
| `TOP014` | Error | `Travel()` с deconstruct вне привязанной цепочки | До фазы 11 — частый случай для разнесённого кода |

**Правила:**
- `TOP001`–`TOP004`, `TOP007` — валидация графа цепочки (`ChainValidationAnalyzer`).
- `TOP005` — предупреждение при emit / анализе tuple-return.
- `TOP006` — осиротевшие handler'ы; фаза 7 сужает ложные срабатывания.
- `TOP008` — канонические имена вагонов для одинаковых сигнатур handler'а.
- `TOP011`–`TOP014` — typed `Travel()` deconstruct (фазы 9–12, §3.10).
- Release tracking: `AnalyzerReleases.Unshipped.md`.

---

## 6. Рефакторинг runtime (shared helpers)

### 6.1. `StationMerge` (новый или rename `ApplyStationReturn`)

```csharp
internal static class StationMerge
{
    public static CargoManifest Apply<T>(
        CargoManifest manifest,
        T stationReturn,
        IReadOnlyList<string> inputWagonNames,
        in StationLocals locals,
        StationMergeOptions options);
}
```

Правила merge (сохранить текущее поведение):
- returned → `LoadWagon`
- regular input not returned → `UnloadWagon` (если `removeOmittedRegularInputs`)
- ref input not returned → `LoadWagon` from locals

### 6.2. `WagonStationReturn`

- Оставить `TryGetMemberValue`, `TryGetTupleElement`
- Для data-oriented: ordinal tuple = порядок **inputWagonNames**, не `[TrainTuple]`

### 6.3. Shared merge

`StationMerge` используется `TrainRouteStationGenerator` для merge возвратов handler'ов.

---

## 7. Структура проекта (целевая)

```
src/TrainOP/
  Railway.cs              # без изменений ядра
  StationDataResult.cs    # GreenPayload / RedFailure / GreenPass
  StationMerge.cs         # NEW: shared merge logic
  WagonStationReturn.cs   # existing

src/TrainOP.Generators/
  TrainRouteStationGenerator.cs
  TrainRouteTravelGenerator.cs
  ChainValidationAnalyzer.cs
  StationMerge.cs

tests/
  TrainOP.Tests/
    DataOrientedStationTests.cs
    DataOrientedPaymentRouteEndToEndTests.cs
  TrainOP.Generators.Tests/
    TrainRouteStationGeneratorTests.cs
    TrainRouteTravelGeneratorTests.cs
    ChainValidationAnalyzerTests.cs
```

---

## 8. Правила для AI-агентов

### 8.1. При реализации фазы

1. Читать этот файл и отмечать чекбоксы в §4.
2. Не добавлять атрибуты для data-маршрутов.
3. Минимальный diff в рамках фазы.
4. Каждая фаза — зелёные `dotnet test TrainOP.sln`.

### 8.2. При написании handler'ов (целевой стиль)

```csharp
// ✅ Хорошо
.Station("X", (string id, decimal amount) => new { id, amount = amount + 1 })

// ✅ Хорошо — ошибка как данные
.Station("X", (string id, decimal amount) =>
    amount > 0 ? RailwaySignals.Green(new { id, amount }) : RailwaySignals.Red("ERR", "..."))

// ⚠️ Допустимо — доступ к полному манифесту
.Station("X", (CargoManifest m, string id) => new { id = id + m.PullWagon<string>("traceId") })

// ❌ Избегать в бизнес-handler'ах
.Station("X", (string id) => new CargoManifest().LoadWagon("id", id))

// ❌ Избегать в бизнес-handler'ах
.Station("X", manifest => RailwaySignals.Red(manifest, ...))
```

### 8.3. Порядок кортежей в возврате

| API | Правило ordinal |
|-----|-----------------|
| **Data-oriented** `.Station` | Порядок **wagon-параметров handler'а** (§3.4); предупреждение `TOP005` |
| **Legacy `[TrainTuple]`** | Порядок свойств `[Wagon]` на классе (без изменений) |

Предпочитать анонимные типы и records в новом коде.

### 8.4. Фаза 7 (якоря цепочки)

1. Читать §3.8 и §4 фаза 7 перед изменением `ChainDetector`.
2. Не ломать обход fluent от `new TrainRoute()` — только добавить корни.
3. Extension-якорь: первая станция с параметрами ≠ seed; не применять `TOP001` к «внешним» вагонам.
4. Сверять ID диагностик с `TrainRouteDiagnostics.cs` (§5).

### 8.5. Фаза 8 (сторонние сборки)

1. Сначала spike (§4 фаза 8.1–8.2) — не писать production-код до выбора вектора B/C/D.
2. Не анализировать IL referenced assembly; только symbols + export schema.
3. Атрибут схемы — только на generated export type, не на handler lambda.
4. Фаза 7 должна быть готова или учтена в spike (якорь `External.Build()`).

---

## 9. Риски и митигация

| Риск | Митигация |
|------|-----------|
| Цепочку нельзя вывести из разнесённого кода | `TOP006`; фаза 7: локальная после `new`, параметр, поле, вызов (§3.8) |
| Взрыв комбинаторики overload'ов | Один generic `Station` + source-generated wrapper per call site, не per signature union |
| Кортежи в возврате | Предпочитать анонимные типы; analyzer `TOP005` |
| Два генератора конфликтуют | Разные extension-классы; per-site `TravelResult_*` вместо `Deconstruct` на `RouteReport` (§3.10) |
| Сторонняя сборка без исходников | Фаза 8: экспорт схемы в DLL; fallback §3.8.4 (§3.9) |
| Несовместимость версий RouteLib | Semver + `TOP010` Info; тесты на старую DLL без схемы |
| Конфликт `Deconstruct` на `RouteReport` | Interceptors + уникальный struct per call site (фаза 9, §3.10) |
| Хрупкость interceptors (старый SDK) | Deconstruct только при emit; иначе `RouteReport.Get<T>()` |
| Производительность рефлексии | Merge только на возврате; PullWagon типизирован в compile-time |

---

## 10. Критерии завершения проекта

- [x] Пример payment flow без `LoadWagon`/`PullWagon` в handler'ах (кроме manifest escape / recovery)
- [x] Analyzer ловит missing wagon в цепочке (`TOP001`)
- [ ] Фаза 7: якоря цепочки кроме `new TrainRoute()` (§4, §3.8)
- [ ] Фаза 8: композиция с маршрутами из сторонних сборок (§4, §3.9)
- [ ] Фаза 9: sync typed `Travel()` deconstruct через interceptors (§4, §3.10)
- [ ] Фаза 10: typed `TravelAsync()` deconstruct (§4, §3.10)
- [ ] Фаза 11: typed `Travel()` на расширенных якорях (§4, §3.10; после фазы 7)
- [ ] Фаза 12 (опционально): `[TrainRouteTerminal]` opt-in (§4, §3.10)
- [ ] `Travel()` с typed deconstruct по цепочке (фазы 9–11; см. §3.10; фаза 3 — только `RouteReport.Get<T>()`)
- [x] `RailwaySignals.Red` в handler без ручного `SignalIssue`
- [x] Документация обновлена (`getting-started`, README)
- [x] Legacy API удалён
- [ ] Этот план: фазы 7–12 §4 отмечены выполненными (фазы 0–6 ✅)

---

## 11. Ссылки в репозитории

| Файл | Назначение |
|------|------------|
| `src/TrainOP/Railway.cs` | Runtime маршрута |
| `src/TrainOP/WagonStationReturn.cs` | Чтение возврата handler |
| `src/TrainOP.Generators/TrainRouteStationGenerator.cs` | Data-oriented адаптеры `.Station` |
| `src/TrainOP.Generators/TrainRouteStationInterceptorsEmitter.cs` | Interceptors для chain-dispatch `.Station` |
| `src/TrainOP.Generators/TrainRouteTravelGenerator.cs` | Typed `Travel()` interceptors (фазы 9–12, §3.10) |
| `src/TrainOP.Generators/TravelCallSiteIndex.cs` | *(фазы 9–11)* Индекс call site'ов `Travel()` с deconstruct |
| `src/TrainOP.Generators/TrainRouteTravelInterceptorsEmitter.cs` | *(фазы 9–10)* Emit `TravelResult_*` + interceptors |
| `tests/TrainOP.Tests/DataOrientedPaymentRouteEndToEndTests.cs` | Сквозной data-oriented payment flow |
| `tests/TrainOP.Tests/TrainRuntimeTests.cs` | Runtime: async, cancellation, exceptions |
| `src/TrainOP.Generators/ChainDetector.cs` | Обнаружение цепочек (фаза 7: расширение якорей) |
| `docs/cross-assembly-routes.md` | *(фаза 8)* Межсборочная композиция — TBD |
| `docs/core-api.md` | Базовый API |

---

## 12. История изменений плана

| Дата | Изменение |
|------|-----------|
| 2026-07-02 | Первая версия плана (data-oriented handlers, 6 фаз) |
| 2026-07-02 | `DataRouteBuilder` заменён на `TrainRoute.Station`; удалён `DataRouteDefinition` |
| 2026-07-02 | Пересмотр п.1: без `[TrainRouteChain]`; chainId и схема только из analyzer |
| 2026-07-02 | Отказ от `WithSeed`: seed = первая `Station` без входных вагонов; внешний seed = `Travel(manifest)` |
| 2026-07-06 | **Фаза 6 (доп.):** удалён публичный `AttachStation`; codegen → `RegisterStation`; ветвление на data-oriented `.Station` |
| 2026-07-02 | **Фаза 6:** удаление legacy `[TrainTuple]` API, README/docs, `DataOrientedPaymentRouteEndToEndTests` |
| 2026-07-06 | **Фаза 8 (план):** композиция маршрутов со сторонними сборками, экспорт схемы (§3.9, §4 фаза 8) |
| 2026-07-06 | **Фаза 7 (план):** расширение якорей `ChainDetector` — параметр, поле, вызов, локальная после `new` (§3.8, §4 фаза 7) |
| 2026-07-10 | **Фазы 9–12 (план):** typed `Travel()` / `TravelAsync()` через Roslyn interceptors; дизайн §3.10; диагностики `TOP011`–`TOP014` |
