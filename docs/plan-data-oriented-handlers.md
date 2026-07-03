# План: data-oriented handlers (только данные на вход и выход)

> **Статус:** фазы 0–6 завершены — data-oriented API является единственным целевым путём  
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

1. `new TrainRoute()` + `.Station(...)` — data-oriented цепочка; `.AttachStation(...)` — низкоуровневый manifest API.
2. `.Station(name, (params…) => data)` — handler; **первая станция без входных параметров = seed**.
3. `DispatchTrain().Travel(manifest?)` — запуск; внешний seed через `Travel(manifest)`.

Всё остальное (адаптеры, `PullWagon`/`LoadWagon`, id цепочки, валидация, typed `Travel`) — **генератор и analyzer**, не ручные атрибуты.

`[TrainTuple]` / `[Wagon]` — **удалены** (см. §3.5).

### 1.3. Не-цели (v1)

- Динамическая сборка маршрута в runtime (`foreach` + `AttachStation`)
- Автоматический анализ произвольных тел lambda (только сигнатура + известные типы возврата)
- Полный отказ от manifest-only станций (останутся для низкоуровневых случаев)

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
│ TrainRoute.AttachStation (runtime, unchanged core)       │
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
| `RailwaySignals.Pass` | манифест без изменений → `Green` |
| `CargoManifest` | escape hatch (фаза 5): заменяет манифест целиком → `Green`; analyzer `TOP005` |


**Связь с `ServiceStation`:** тот же контракт `Green` / `Red` / `Pass`; codegen читает вагоны из `red.Manifest`, опционально `SignalIssue` / `RedSignal`. Escape hatch `Func<RedSignal, Signal>` сохранён.
### 3.2. Точка сборки маршрута (fluent, v2 API)

Новый builder **рядом** с `TrainRoute` (не ломать существующий API):

```csharp
public sealed class TrainRoute
{
    public TrainRoute Station<THandler>(string name, THandler handler);  // data-oriented (codegen)
    public TrainRoute AttachStation(...);  // низкоуровневый manifest API
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
- Анализ голого `new TrainRoute().AttachStation(...)` как data-chain (legacy)

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

#### Зачем `new TrainRoute()` + `.Station`, а не только `.AttachStation`

- `.Station` — data-oriented handler'ы (codegen adapter в фазе 1).
- `.AttachStation` — явный manifest/signal API (legacy, низкий уровень).
- Analyzer строит граф только по `.Station` на цепочке от `new TrainRoute()`.

#### Алгоритм `ChainDetector` (фаза 2)

1. Найти `ObjectCreationExpression` с типом `TrainRoute`.
2. Обойти цепочку invocations: `.Station`, до `DispatchTrain()` / конца выражения.
3. Для каждого `.Station(name, lambda)` — построить `HandlerSchema` из lambda.
4. Станция без wagon-параметров: вход пуст, выход = seed; или внешний `Travel(manifest)`.
5. `chainId` = символ метода, содержащего выражение.
6. Data-lambda в `.Station` вне цепочки от `new TrainRoute()` → `TOP007`.

#### Runtime (репозиторий)

- `TrainRoute.Station(...)` — `src/TrainOP/Railway.cs` (делегирует в `AttachStation` до codegen)
- Data-oriented `Station((T…) => TOut)` — **фаза 1** (codegen adapter)

| Подход | Статус |
|--------|--------|
| **`new TrainRoute()` + `.Station`** | ✅ якорь для analyzer |
| **`[TrainRouteChain]`** | ❌ удалён |
| **`DataRouteBuilder`** | ❌ удалён — всё на `TrainRoute` |
| **`[TrainTuple]` для новых маршрутов** | ❌ legacy only |
| **`.AttachStation` на той же цепочке** | legacy manifest, вне data-graph |

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
- [x] Сканировать `.Station(...)` на `TrainRoute` (не путать с `.AttachStation` для data-graph)
- [x] Из SemanticModel извлечь: имена и типы параметров, тип возврата, `CancellationToken`, `CargoManifest` escape
- [x] Emit адаптер: `PullWagon` → handler → `StationMerge.ToSignal`
- [x] `StationMerge.Apply` + `RailwaySignals` runtime (`StationDataResult.cs`, `StationMerge.cs`)
- [x] Поддержать возврат: анонимный тип, tuple, `StationDataOk<>`, `StationDataFail`, `CargoManifest`
- [x] Тесты: `DataOrientedStationTests` без `[TrainTuple]`

**Не сделано в фазе 1 (отложено):** `ref`-вагоны, валидация цепочки, typed `Travel()`.

**Критерий:** тест-проект с `TrainRoute.Station`, handler'ы без TrainOP в теле. **Выполнено.**

**API-изменение:** удалены instance-перегрузки `TrainRoute.Station(manifest => …)`; manifest-стиль только через `.AttachStation`.

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

**Реализация:** `TrainRouteTravelGenerator` emit'ит `RouteReport.Deconstruct` в `namespace TrainOP` по terminal schema; дедупликация по C#-сигнатуре типов.

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
- [x] Удалены `TrainTupleAttribute`, `WagonAttribute`, `WagonTupleGenerator`, legacy-тесты и docs

**Критерий:** один сквозной sample в `tests/` на новом API; legacy API отсутствует. **Выполнено.**

---

## 5. Диагностики — **УТВЕРЖДЕНО (фаза 0, п.4)**

| ID | Severity | Условие | Message format |
|----|----------|---------|----------------|
| `TOP001` | — | *(удалён вместе с `WagonTupleGenerator`)* | — |
| `TOP002` | Error | Станция требует вагон, не произведённый ранее в цепочке | `Station '{0}' requires wagon '{1}', which is not available from earlier stations in this route.` |
| `TOP003` | Error | Конфликт типов одного вагона между станциями | `Wagon '{0}' has conflicting types: '{1}' (produced at '{2}') vs '{3}' (required at '{4}').` |
| `TOP004` | Error | Вагон удалён частичным возвратом, но нужен дальше | `Wagon '{0}' was removed at station '{1}' but is required at station '{2}'.` |
| `TOP005` | Warning | Handler вернул `CargoManifest` — полная замена | `Station '{0}' returns CargoManifest, which replaces the entire manifest; wagons not in the return value may be lost.` |
| `TOP006` | Warning | ValueTuple в возврате data-handler'а | `Station '{0}' returns a tuple; element order must match handler parameter order ({1}). Prefer anonymous types or records.` |
| `TOP007` | Error | Data-lambda вне цепочки от `new TrainRoute()` | `Data-oriented handler must be part of a TrainRoute chain starting with 'new TrainRoute()'.` |
| `TOP008` | Info | Вагон из seed не используется downstream | `Wagon '{0}' produced at seed station '{1}' is never consumed by later stations.` |

**Правила:**
- `TOP001` — legacy + data (имена параметров handler'а).
- `TOP002`–`TOP004`, `TOP007` — **фаза 2** (`ChainValidationAnalyzer`).
- `TOP005`, `TOP006`, `TOP008` — фаза 2 (можно частично в фазе 1 как warnings при emit).
- Release tracking: `AnalyzerReleases.Unshipped.md` при добавлении `TOP002+`; `TOP001` уже shipped с `WagonTupleGenerator`.

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
| **Data-oriented** `.Station` | Порядок **wagon-параметров handler'а** (§3.4); предупреждение `TOP006` |
| **Legacy `[TrainTuple]`** | Порядок свойств `[Wagon]` на классе (без изменений) |

Предпочитать анонимные типы и records в новом коде.

---

## 9. Риски и митигация

| Риск | Митигация |
|------|-----------|
| Цепочку нельзя вывести из разнесённого кода | `TOP007`; собирать маршрут в одном выражении или static `Build()` |
| Взрыв комбинаторики overload'ов | Один generic `Station` + source-generated wrapper per call site, не per signature union |
| Кортежи в возврате | Предпочитать анонимные типы; analyzer TOP006 |
| Два генератора конфликтуют | Разные extension-классы; data-route не генерит `Deconstruct` для legacy |
| Производительность рефлексии | Merge только на возврате; PullWagon типизирован в compile-time |

---

## 10. Критерии завершения проекта

- [x] Пример payment flow без `LoadWagon`/`PullWagon` в handler'ах (кроме manifest escape / recovery)
- [x] Analyzer ловит missing wagon в цепочке (TOP002)
- [x] `Travel()` с typed deconstruct по цепочке
- [x] `RailwaySignals.Red` в handler без ручного `SignalIssue`
- [x] Документация обновлена (`getting-started`, README)
- [x] Legacy API удалён
- [x] Этот план: все фазы §4 отмечены выполненными

---

## 11. Ссылки в репозитории

| Файл | Назначение |
|------|------------|
| `src/TrainOP/Railway.cs` | Runtime маршрута |
| `src/TrainOP/WagonStationReturn.cs` | Чтение возврата handler |
| `src/TrainOP.Generators/TrainRouteStationGenerator.cs` | Data-oriented адаптеры `.Station` |
| `src/TrainOP.Generators/TrainRouteTravelGenerator.cs` | Typed `Travel()` deconstruct |
| `tests/TrainOP.Tests/DataOrientedPaymentRouteEndToEndTests.cs` | Сквозной data-oriented payment flow |
| `tests/TrainOP.Tests/TrainRuntimeTests.cs` | Runtime: async, cancellation, exceptions, AttachStation smoke |
| `docs/core-api.md` | Базовый API |

---

## 12. История изменений плана

| Дата | Изменение |
|------|-----------|
| 2026-07-02 | Первая версия плана (data-oriented handlers, 6 фаз) |
| 2026-07-02 | `DataRouteBuilder` заменён на `TrainRoute.Station`; удалён `DataRouteDefinition` |
| 2026-07-02 | Пересмотр п.1: без `[TrainRouteChain]`; chainId и схема только из analyzer |
| 2026-07-02 | Отказ от `WithSeed`: seed = первая `Station` без входных вагонов; внешний seed = `Travel(manifest)` |
| 2026-07-02 | **Фаза 6:** удаление legacy `[TrainTuple]` API, README/docs, `DataOrientedPaymentRouteEndToEndTests` |
| 2026-07-03 | Очистка manifest-примеров и тестов; `DepotRouteTests` → `TrainRuntimeTests`; docs на `RailwaySignals` |
