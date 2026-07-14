# План: data-oriented handlers (только данные на вход и выход)

> **Статус фаз (§4):** **выполнено** 0–6 + якоря A; **неполно** расширение якорей (фаза 7 D); **удалено** 9–12 typed Travel; **запланировано** фаза 8.  
> **Терминалы:** `RouteReport` indexer/`Get` (C# ≤15 — конфликты декомпозиции кортежей нерешаемы).  
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
3. `DispatchTrain().Travel()` — запуск; входные данные — только через seed-станцию сверху (замыкание / параметры `Build(...)`).

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
| **Схема seed** | Первая `.Station` с **нулём wagon-параметров** (внешний вход — замыкание / аргументы `Build(...)`) |
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
4. Станция без wagon-параметров: вход пуст, выход = seed. Первая станция с входными вагонами без upstream seed → `TOP001`.
5. `chainId` = символ метода, содержащего выражение.
6. Data-lambda в `.Station` вне цепочки от `new TrainRoute()` → `TOP007`.

#### Runtime (репозиторий)

- Data-oriented `Station((T…) => TOut)` — codegen adapter → `RegisterStation`

| Подход | Статус |
|--------|--------|
| **`new TrainRoute()` + `.Station`** | ✅ якорь для analyzer |
| **`[TrainRouteChain]`** | ❌ удалён |
| **`DataRouteBuilder`** | ❌ удалён — всё на `TrainRoute` |

### 3.8. Якоря цепочки — **текущая политика + отложенное расширение**

#### 3.8.1. Валидные якоря сейчас (зафиксировано)

Пока **только** два паттерна — они согласованы с analyzer и chain-dispatch interceptors:

| Якорь | Пример | Статус |
|-------|--------|--------|
| **Прямая fluent-цепочка** | `new TrainRoute().Station(...).Station(...)` | ✅ |
| **Локальная после `new`** | `var r = new TrainRoute(); r.Station(...)` | ✅ |

`PaymentRoute.Build()` допустим, если **внутри** `Build` — один из двух паттернов выше, а снаружи только `.DispatchTrain().Travel()` (без `.Station` на результате вызова).

#### 3.8.2. Временно **не** поддерживается

Паттерны ниже плохо стыкуются с per-site codegen / interceptors (разрыв цепочки, чужой `chainId`, ложные или слепые diagnostics). **Убраны из текущей очереди фазы 7** — отдельное рассмотрение позже (рядом с фазой 8 или после неё):

| Сценарий | Почему отложено |
|----------|-----------------|
| `Build().Station(...)` / `CreateSeed().Station(...)` | Receiver — вызов; generator видит хвост отдельно от тела `Build` |
| Параметр `baseRoute.Station(...)` | Upstream-схема вне метода |
| Поле / свойство `_route.Station(...)` | Нет стабильной привязки к `new` без flow analysis |
| `var r = Factory(); r.Station(...)` | Не `new TrainRoute()` |

Ожидаемое поведение сейчас: **TOP006** на `.Station` вне валидного якоря. Не расширять `ChainDetector` под эти формы, пока не будет отдельного дизайна.

#### 3.8.3. `chainId` (текущие якоря)

| Якорь | `chainId` |
|-------|-----------|
| `new TrainRoute()` в `PaymentRoute.Build` | `PaymentRoute.Build` (и суффикс при необходимости) |
| Локальная `route` после `new` | `ContainingMethod@route` (см. `RouteChainIdBuilder`) |

Формулы для параметра / поля / `Build()` call site — **не реализовывать**, пока паттерн не вернулся в очередь.

#### 3.8.4. Отложенный дизайн (черновик; не фаза 7)

Когда вернёмся к расширению якорей — отдельно решить:

- алгоритм корней для `InvocationExpression` / параметра / поля (бывший псевдокод `DetectChainRoots`);
- семантику seed при extension (`TOP001` не на первой станции с входами);
- стык с `Build().Station` vs межсборочный экспорт схемы (§3.9).

Ветвления receiver'а (`?:` / `??` / `switch`) — **протокол merge графов** §3.8.5 (**шаги 1–5 реализованы** в analyzer; codegen/interceptors для join — отдельно).

Не смешивать с локальной-после-`new` и transparent peel (уже сделано).

#### 3.8.5. Ветвление receiver'а — **протокол merge графов** (утверждённый алгоритм)

Для `cond ? a : b`, `x ?? y`, `switch { … }` и аналогов **не** peel к одному ядру. Обработка:

1. **Определить все имеющиеся графы** — для каждого рукава выражения-receiver разрешить якорь и симулировать цепочку до точки стыка (как `ChainGraphSimulator` / слоты вагонов). **Реализовано:** `BranchRouteGraphDiscoverer` (+ `ChainDetector.TryBuildChainEndingAt`); terminal-слоты — `ChainSimulationResult.TerminalWagons` (LiveOrder после симуляции; пусто при `HasUnknownReturn`).
2. **Определить какие должны быть соединены** — рукава, чьи результаты сходятся в один call site `.Station` / дальше по fluent (общий downstream). **Реализовано:** `BranchRouteJoinSetFinder` (+ модель `BranchRouteJoinSet`); `Find(tree, model)` — Station/StationAsync/ServiceStation с forking receiver после peel; `FromForkReceiver` — join set из уже известного fork.
3. **Провалидировать возможность соединения** — совместимость схем на стыке (имена/типы terminal-вагонов; правила как `TOP001`/`TOP002`/`TOP003` на объединённом входе). **Реализовано:** `BranchRouteJoinValidator` (+ `BranchRouteJoinValidation`); `CanMerge`, пересечение terminal-имён, `TypesCompatible` из `ChainGraphSimulator`.
4. **Ошибка в случае неудачи** — diagnostic (новый ID или расширение `TOP002`/`TOP006`); цепочку / interceptors для этого стыка не эмитить как «успешный merge». **Реализовано:** `TOP015` (`TrainRouteDiagnostics.RouteBranchJoinFailed`); analyzer подавляет `TOP006` на DownstreamStation join set (успех и неудача).
5. **Merge в случае удачи** — один объединённый upstream-граф для продолжения `.Station…` и дальнейшей валидации. **Реализовано:** `BranchRouteJoinMerger.TryMerge`; downstream-симуляция через `ChainGraphSimulator.Simulate(chain, initialWagons)` + `ChainDetector.TryBuildChainFromStationInvocation` (`RouteChainAnchorKind.BranchJoin`).

**Шаги 3–5 подключены в analyzer:** успешный join снимает TOP006 с downstream и валидирует продолжение цепочки от merged terminals; при неудаче — только TOP015 (без TOP006 на Join). Transparent peel (paren / `!` / cast / `await` / `Task.FromResult`) — ортогонален и не заменяет этот протокол. `Build().Station` по-прежнему TOP006.

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
| **G** | Станции как типизированные делегаты / handlers без lambda в consumer | Частично | ✅ | Высокая | **Частично реализовано (same-compilation):** method group / local function / anonymous method → `TryResolveHandler` + `StationHandlerBinding`; cross-assembly по-прежнему через B-export |

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

**Альтернатива без атрибута:** соглашение об имени `PaymentModule_BuildSchema` + поиск `INamedTypeSymbol` в сборке метода по `[ModuleInitializer]` / namespace — хуже для рефакторинга; атрибут только для **export**, не для описания handler'ов.

#### 3.9.5. Что уже работает без фазы 8

| Паттерн | Runtime | Analyzer в consumer |
|---------|---------|---------------------|
| `PaymentModule.Build().DispatchTrain().Travel()` | ✅ | N/A (нет локальных станций) |
| Вложенный sub-route через data-oriented `.Station` + `Build(args)` / seed + `Travel()` | ✅ | ✅ (см. `NestedBranchingRouteExample`) |
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

### 3.10. Доступ к терминальным вагонам — **РЕШЕНИЕ: `RouteReport` indexer / `Get<T>`**

#### 3.10.1. Проблема

Желаемый ergonomics — `var (paymentId, amount) = …Travel()` с типами terminal-вагонов по цепочке. **Текущие возможности C# 15 и ниже не позволяют решить конфликты декомпозиции кортежей** для общего `RouteReport` / `Travel()`:

1. Несколько `Deconstruct` на одном типе (разная arity / набор вагонов по call site) дают неоднозначность overload resolution — конфликт декомпозиции.
2. Roslyn interceptors **не меняют** bound return type: перехватчик обязан совпадать с сигнатурой `Travel()` → `RouteReport`, поэтому `var (a, b) = …Travel()` не привязывается к per-site struct.
3. Обход через `TravelTyped(marker)` / отдельный return type отвергнут как хуже по эргономике, чем `Get` / indexer.

Повторно рассматривать typed deconstruct — только если язык/Roslyn даст решение конфликтов **после C# 15**.

#### 3.10.2. Решение

Терминальные вагоны читаются из `RouteReport`:

```csharp
var report = PaymentRoute.Build().DispatchTrain().Travel();
var paymentId = report.Get<string>("paymentId");
var amount = report.Get<decimal>("amount");
// object value = report["paymentId"];
```

Фазы 9–12 (typed Travel / deconstruct / `[TrainRouteTerminal]`) — **сняты** с очереди.

#### 3.10.3–3.10.5. ~~Архитектура interceptors / фазы 9–12~~ — не применяем

Station-interceptors для `TOP008` / chain-dispatch остаются (§ station interceptors на ветке `interceptors`).

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
| Внешний вход | Только через seed сверху: замыкание внешних переменных или `Build(paymentId, amount)` с seed внутри |
| Две seed-станции подряд | Допустимо: вторая merge'ит поверх результата первой |
| `Travel(CargoManifest)` | Obsolete: не публичный канон; runtime по-прежнему принимает манифест внутри движка |

### 3.7. `StationMerge` — **РЕШЕНИЕ (фаза 0)**

Утверждён shared helper §6.1: `StationMerge.Apply` / `StationMerge.ToSignal` для `TrainRouteStationGenerator`.

**Опциональные вагоны (фаза 5):** `T?` / `Nullable<T>` → `HasWagon` + `default` без throw; не часть фазы 1.

## 4. Фазы реализации

| Категория | Содержание |
|-----------|------------|
| **[Выполненное](#41-выполненное)** | Фазы **0–6**; фаза **7** в объёме прямая fluent + локальная после `new`; terminal-доступ через `RouteReport.Get` / indexer |
| **[Неполностью выполненное](#42-неполностью-выполненное)** | Фаза **7**: якоря `Build().Station` / параметр / поле / factory (§3.8.2) — вынесены из scope, отдельный дизайн |
| **[Удалённое](#43-удалённое)** | Фазы **9–12** (typed Travel / deconstruct / `TravelTyped` / `TOP011`–`TOP014`) — сняты; C# ≤15 не решает конфликты декомпозиции |
| **[Запланированное](#44-запланированное)** | Фаза **8** — межсборочная композиция маршрутов (§3.9) |

---

### 4.1. Выполненное

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

### Фаза 3 — Terminal-доступ после `Travel` ✅

**Задачи (итог):**
- [x] Union «живых» вагонов на конце цепочки = fold графа из фазы 2 (для analyzer / codegen station)
- [x] Async: `TravelAsync` + `CancellationToken` в handler
- [x] Доступ к terminal-вагонам: `RouteReport.Get<T>("name")` / `report["name"]`

**Критерий:** пример из §1.1 полностью работает. **Выполнено.**

**Итог:** typed-обёртки / deconstruct **не** вошли в поставку; см. §4.3 и §3.10.1 (C# ≤15).

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

#### Фаза 7 (выполненная часть) — якоря A: прямая + локальная ✅

**В scope выполнено:**

- [x] Якорь `ObjectCreation` (`new TrainRoute().Station…`)
- [x] Якорь `LocalVariable` (`var r = new TrainRoute(); r.Station…`)
- [x] `TOP006` на `.Station` вне этих якорей
- [x] Docs + регрессии (`CreateSeed().Station` → `TOP006`)

| Уровень | Якорь | Пример | Статус |
|---------|-------|--------|--------|
| **A** | `new TrainRoute()` | `new TrainRoute().Station(...)` | ✅ |
| **A** | Локальная после прямого `new` | `var r = new TrainRoute(); r.Station(...)` | ✅ |

См. §3.8. Остаток фазы 7 — в [§4.2](#42-неполностью-выполненное).

---

### 4.2. Неполностью выполненное

#### Фаза 7 (остаток) — расширение якорей — **не закрыто**

Изначально фаза 7 включала `Build().Station`, параметр, поле. **Временно снято** с реализации; суженный scope (якоря A) закрыт в §4.1. Полное расширение — только после отдельного дизайна (§3.8.2, §3.8.4).

| Уровень | Якорь | Пример | Статус |
|---------|-------|--------|--------|
| **D** | Вызов метода / свойства | `BuildRoute().Station(...)` | ❌ не сделано |
| **D** | Параметр метода | `baseRoute.Station(...)` | ❌ |
| **D** | Поле / свойство | `_route.Station(...)` | ❌ |
| **D** | Conditional / switch / coalesce | `cond ? a : b`, `x ?? y` | ✅ шаги 1–5 §3.8.5 (TOP015 / merge; TOP006 на Join снят) |
| **D** | Parenthesized / `!` / cast / `await` на внешнем якоре | `(GetRoute()).Station(...)` | ❌ якорь D |
| **❌** | Динамическая сборка | `foreach` + `RegisterStation` | не-цель (§1.3) |

Transparent peel (paren / `!` / cast / `await`) для приёмников на якорях A реализован отдельно от расширения якорей D и не разблокирует `Build().Station` / параметр / поле. Ветвления — по §3.8.5 (графы → стык → validate → error/merge), не через peel.

**Не делать без нового решения:**

- `Build().Station(...)` / `CreateSeed().Station(...)` как легитимный якорь
- Параметр / поле / свойство; seed-семантика extension
- Межпроцедурный merge графов; flow analysis от `Factory()`
- Runtime-изменения (`Railway.cs`); новые атрибуты для якоря

Ожидаемое поведение сейчас: **TOP006** на `.Station` вне якорей A (кроме join-downstream после forking receiver — §3.8.5 / `TOP015`).

---

### 4.3. Удалённое

#### Фазы 9–12 — Typed Travel / deconstruct — **СНЯТЫ**

| Что снято | Почему |
|-----------|--------|
| Typed `var (a, b) = …Travel()` через interceptors | Interceptor не меняет bound return type `Travel()` → `RouteReport` |
| `TravelTyped(marker)` + `TravelResult_*` | Хуже эргономики, чем indexer / `Get` |
| `Deconstruct` на `RouteReport` / per-arity overloads | Конфликты декомпозиции кортежей |
| Диагностики `TOP011`–`TOP014` | Не вводятся |
| Opt-in `[TrainRouteTerminal]` (фаза 12) | Вместе с typed Travel |

**Ограничение языка:** C# 15 и ниже **не позволяют** решить конфликты декомпозиции кортежей для общего terminal-типа (§3.10.1). Повторно — только после смены языка/Roslyn.

Терминальный доступ остаётся: **`RouteReport` indexer / `Get<T>`**. Interceptors для `.Station` (TOP008) **сохранены**.

---

### 4.4. Запланированное

### Фаза 8 — Сторонние сборки и композиция маршрутов — **ПЛАН (spike → решение)**

**Цель:** исследовать и внедрить способ строить маршруты с участием **скомпилированных** сторонних библиотек (NuGet / ProjectReference): готовые станции из DLL + локальные `.Station` в потребителе, с предсказуемым runtime и максимально полной compile-time проверкой.

**Зависимости:** якоря A (фаза 7 выполнено). Для consumer `.Station` после `External.Build()` нужны якоря D (§4.2) **или** экспорт схемы без локального `.Station` на результате вызова — уточняется на spike. Typed Travel (9–12) снят (§4.3).

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
- [ ] Документировать влияние экспорта схемы на analyzer (без typed Travel).
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

## 5. Диагностики — **УТВЕРЖДЕНО (фаза 0, п.4)**

| ID | Severity | Условие | Message format |
|----|----------|---------|----------------|
| `TOP001` | Error | Станция требует вагон, не произведённый ранее в цепочке | `Station '{0}' requires wagon '{1}', which is not available from earlier stations in this route` |
| `TOP002` | Error | Конфликт типов одного вагона между станциями | `Wagon '{0}' has conflicting types: '{1}' (produced at '{2}') vs '{3}' (required at '{4}')` |
| `TOP003` | Error | Вагон удалён частичным возвратом, но нужен дальше | `Wagon '{0}' was removed at station '{1}' but is required at station '{2}'` |
| `TOP004` | Warning | Handler вернул `CargoManifest` — полная замена | `Station '{0}' returns CargoManifest, which replaces the entire manifest; wagons not in the return value may be lost` |
| `TOP005` | Warning | ValueTuple в возврате data-handler'а | `Station '{0}' returns an unnamed tuple; element order must match handler parameter order ({1}). Prefer anonymous types, records, or named tuples.` |
| `TOP006` | Error | Data-lambda вне цепочки от легитимного якоря `TrainRoute` | Прямая fluent или локальная после `new`; иначе error (§3.8) |
| `TOP007` | Info | Вагон из seed не используется downstream | `Wagon '{0}' produced at seed station '{1}' is never consumed by later stations` |
| `TOP008` | Error | Конфликт имён вагонов для одной сигнатуры handler'а | `Handler wagon names ({0}) do not match the canonical names ({1})...` |
| `TOP015` | Error | Нельзя соединить ветки маршрута перед downstream Station | `Cannot join route branches before station '{0}': {1}` |
| `TOP016` | Error | Handler не лямбда/anonymous/однозначный method group текущей compilation | `Station handler must be a lambda, anonymous method, or method group / local function declared in the current compilation and uniquely resolvable` |

### 5.1. Диагностики фазы 7 (черновик)

| ID | Severity | Условие | Примечание |
|----|----------|---------|------------|
| `TOP006` | Error | Осиротевший data-handler | Текст сообщения обновить после расширения якорей (§7.2) |
| `TOP009` | Info | Станция достижима из двух корней | Опционально; можно отложить |
| `TOP015` | Error | Join forking receiver невалиден | Нерезолвимый рукав, unknown terminals, конфликт типов; подавляет TOP006 на Join |

### 5.2. Диагностики фазы 8 (черновик)

| ID | Severity | Условие | Примечание |
|----|----------|---------|------------|
| `TOP010` | Info | Внешний `TrainRoute` без экспортированной схемы | «Стык с `{0}` не проверяется; обновите RouteLib или объявите контракт» — TBD на spike |

### 5.3. Диагностики merge ветвлений (§3.8.5)

| ID | Severity | Условие | Message format |
|----|----------|---------|----------------|
| `TOP015` | Error | Ветки `?:` / `??` / `switch` нельзя соединить | `Cannot join route branches before station '{0}': {1}` |

Примеры `{1}`: `one or more branches are not resolvable TrainRoute chains`; `branch has unknown terminal wagon state`; `wagon '{name}' has conflicting types across branches ('{t1}' vs '{t2}')`; `no branches to join`.

Typed Travel / deconstruct снят; `TOP011`–`TOP014` не вводятся.

**Правила:**
- `TOP001`–`TOP004`, `TOP007` — валидация графа цепочки (`ChainValidationAnalyzer`).
- `TOP005` — предупреждение при emit / анализе tuple-return.
- `TOP006` — осиротевшие handler'ы; фаза 7 сужает ложные срабатывания; на DownstreamStation join set не выдаётся (вместо него TOP015 при ошибке стыка).
- `TOP008` — канонические имена вагонов для одинаковых сигнатур handler'а.
- `TOP015` — валидация join set (§3.8.5 шаги 3–4).
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
  TrainRouteStationInterceptorsEmitter.cs
  ChainValidationAnalyzer.cs
  StationMerge.cs

tests/
  TrainOP.Tests/
    DataOrientedStationTests.cs
    DataOrientedPaymentRouteEndToEndTests.cs
    RouteReportAccessTests.cs
  TrainOP.Generators.Tests/
    TrainRouteStationGeneratorTests.cs
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

1. Читать §3.8: валидны **только** прямая цепочка и локальная после `new`.
2. Не добавлять якоря `Build().Station` / параметр / поле без отдельного решения оператора.
3. Не ломать fluent от `new TrainRoute()` и уже существующий `LocalVariable`.
4. Сверять ID диагностик с `TrainRouteDiagnostics.cs` (§5).

### 8.5. Фаза 8 (сторонние сборки)

1. Фаза 7 закрыта по политике якорей; typed Travel (9–12) снят — не блокирует старт фазы 8.
2. Сначала spike (§4 фаза 8.1–8.2) — не писать production-код до выбора вектора B/C/D.
3. Не анализировать IL referenced assembly; только symbols + export schema.
4. Атрибут схемы — только на generated export type, не на handler lambda.
5. Фаза 7 обязательна (якорь `External.Build()`); terminal-доступ в consumer — через `RouteReport.Get` / indexer.

---

## 9. Риски и митигация

| Риск | Митигация |
|------|-----------|
| Цепочку нельзя вывести из разнесённого кода | `TOP006`; валидны прямая цепочка и локальная после `new`; `Build().Station` / параметр / поле — отложены (§3.8) |
| Взрыв комбинаторики overload'ов | Один generic `Station` + source-generated wrapper per call site, не per signature union |
| Кортежи в возврате | Предпочитать анонимные типы; analyzer `TOP005` |
| Два генератора конфликтуют | Разные extension-классы; station interceptors только для `.Station` |
| Сторонняя сборка без исходников | Фаза 8: экспорт схемы в DLL; fallback §3.8.4 (§3.9) |
| Несовместимость версий RouteLib | Semver + `TOP010` Info; тесты на старую DLL без схемы |
| Конфликт `Deconstruct` на `RouteReport` | C# ≤15 не решает; не эмитим; доступ через `Get<T>` / indexer (§3.10.1) |
| Хрупкость station interceptors (старый SDK) | Opt-in `InterceptorsNamespaces`; иначе canonical names / TOP008 |
| Производительность рефлексии | Merge только на возврате; PullWagon типизирован в compile-time |

---

## 10. Критерии завершения проекта

### Выполненное
- [x] Пример payment flow без `LoadWagon`/`PullWagon` в handler'ах (кроме manifest escape / recovery)
- [x] Analyzer ловит missing wagon в цепочке (`TOP001`)
- [x] Фаза 7 (якоря A): прямая + локальная; `Build().Station` → `TOP006` (§4.1, §3.8)
- [x] §3.8.5 шаги 1–5: discover → join set → validate (`TOP015`) → merge + analyzer (TOP006 на Join снят; `Build().Station` по-прежнему TOP006)
- [x] Терминальный доступ: `RouteReport.Get` / indexer (§3.10); фазы 9–12 сняты (§4.3)
- [x] `RailwaySignals.Red` в handler без ручного `SignalIssue`
- [x] Документация обновлена (`getting-started`, README)
- [x] Legacy API удалён

### Неполностью выполненное
- [ ] Фаза 7 (якоря D): `Build().Station` / параметр / поле (§4.2)

### Удалённое (не возвращать без смены языка)
- [x] Typed Travel / deconstruct / `TravelTyped` — сняты (§4.3; C# ≤15)

### Запланированное
- [ ] Фаза 8: композиция с маршрутами из сторонних сборок (§4.4, §3.9)
- [ ] Этот план: фаза 8 отмечена выполненной

---

## 11. Ссылки в репозитории

| Файл | Назначение |
|------|------------|
| `src/TrainOP/Railway.cs` | Runtime маршрута |
| `src/TrainOP/WagonStationReturn.cs` | Чтение возврата handler |
| `src/TrainOP.Generators/TrainRouteStationGenerator.cs` | Data-oriented адаптеры `.Station` |
| `src/TrainOP.Generators/TrainRouteStationInterceptorsEmitter.cs` | Interceptors для chain-dispatch `.Station` |
| `tests/TrainOP.Tests/DataOrientedPaymentRouteEndToEndTests.cs` | Сквозной data-oriented payment flow |
| `tests/TrainOP.Tests/RouteReportAccessTests.cs` | Доступ к терминальным вагонам через `Get` / indexer |
| `tests/TrainOP.Tests/TrainRuntimeTests.cs` | Runtime: async, cancellation, exceptions |
| `src/TrainOP.Generators/ChainDetector.cs` | Обнаружение цепочек |
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
| 2026-07-14 | **Seed-only вход:** публичный канон — seed сверху + `Travel()`; `Travel(CargoManifest)` obsolete; analyzer без Travel-escape на станции 0 |
| 2026-07-06 | **Фаза 6 (доп.):** удалён публичный `AttachStation`; codegen → `RegisterStation`; ветвление на data-oriented `.Station` |
| 2026-07-02 | **Фаза 6:** удаление legacy `[TrainTuple]` API, README/docs, `DataOrientedPaymentRouteEndToEndTests` |
| 2026-07-06 | **Фаза 8 (план):** композиция маршрутов со сторонними сборками, экспорт схемы (§3.9, §4 фаза 8) |
| 2026-07-06 | **Фаза 7 (план):** расширение якорей `ChainDetector` — параметр, поле, вызов, локальная после `new` (§3.8, §4 фаза 7) |
| 2026-07-10 | **Фазы 9–12 (план):** typed `Travel()` / `TravelAsync()` через Roslyn interceptors; дизайн §3.10; диагностики `TOP011`–`TOP014` |
| 2026-07-14 | **Очередь:** фаза 8 (сторонние сборки) сдвинута ближе к концу — `7 → 9 → 10 → 11 → 12 → 8` |
| 2026-07-14 | **Якоря:** временно только прямая цепочка и локальная после `new`; `Build().Station` / параметр / поле сняты с фазы 7 (§3.8) |
| 2026-07-14 | **Фазы 9–12 сняты:** typed Travel / `TravelTyped` отвергнуты; **C# ≤15 не решает конфликты декомпозиции кортежей**; доступ — `RouteReport` indexer/`Get` (§3.10) |
| 2026-07-14 | **§4 переразмечен:** выполнено / неполно / удалено / запланировано |
| 2026-07-14 | **§3.8.5:** протокол merge графов для `?:` / `??` / `switch` (discover → join set → validate → error/merge) |
| 2026-07-14 | **§3.8.5 шаг 1:** `BranchRouteGraphDiscoverer` + `TryBuildChainEndingAt`; `TerminalWagons` на `ChainSimulationResult` (шаги 2–5 и снятие TOP006 — pending) |
| 2026-07-14 | **§3.8.5 шаг 2:** `BranchRouteJoinSetFinder` + `BranchRouteJoinSet`; join sets по общему downstream `.Station` (шаги 3–5 и снятие TOP006 — pending) |
| 2026-07-14 | **§3.8.5 шаги 3–5:** `BranchRouteJoinValidator` / `BranchRouteJoinMerger` / `TOP015`; `Simulate(chain, initialWagons)`; analyzer merge + подавление TOP006 на Join |
| 2026-07-14 | **Non-lambda handlers (same-compilation):** `TryResolveHandler` — lambda / anonymous / method group / local function; `TOP016` для unsupported форм; пункт G частично |
