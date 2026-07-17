# План: data-oriented handlers (только данные на вход и выход)

> **Статус:** **выполнено** фазы 0–8 (включая factory anchors + schema export/import, merge ветвлений §3.8.5); **отложено** якоря параметр / поле / свойство; **снято** typed Travel (фазы 9–12).  
> **Терминалы:** `RouteReport` indexer / `Get<T>` (C# ≤15 — конфликты декомпозиции кортежей нерешаемы).  
> **Цель:** handler станции = чистая функция над данными; `CargoManifest`, `LoadWagon`, `PullWagon`, `RailwaySignals` скрыты в сгенерированном адаптере.  
> **Аудитория:** разработчики и AI-агенты, продолжающие работу над TrainOP.

---

## 1. Целевое состояние

### 1.1. Как выглядит код пользователя

```csharp
public static class PaymentRoute
{
  public static TrainRoute Build() => new TrainRoute()
      .Station("Init", () => new { paymentId = "pay-1", amount = 100m })
      .Station("Discount", (string paymentId, decimal amount) =>
          new { paymentId, amount = amount * 0.9m })
      .Station("Validate", (string paymentId, decimal amount) =>
          amount > 0
              ? RailwaySignals.Green(new { paymentId, amount })
              : RailwaySignals.Red("INVALID_TOTAL", "amount must be positive"));
}

var report = PaymentRoute.Build().DispatchTrain().Travel();
var paymentId = report.Get<string>("paymentId");
var amount = report.Get<decimal>("amount");
```

Расширение factory без локальной «seed»-станции — тоже нормальный паттерн:

```csharp
public static TrainRoute Build() =>
    PaymentModule.Build()
        .Station("Finalize", (string paymentId, decimal amount) =>
            new { paymentId, status = "completed" });
```

**Без атрибутов на handler'ах.** Analyzer находит цепочку по `new TrainRoute()`…`.Station(…)`; `chainId` = FQN метода/типа-контейнера (например `PaymentRoute.Build`).

**В теле handler'а нет:**
- `CargoManifest`, `LoadWagon`, `PullWagon`, `UnloadWagon`
- `new CargoManifest()`

### 1.2. Принципы

| Принцип | Описание |
|---------|----------|
| **Data in** | Параметры handler'а = вагоны; имя параметра = ключ манифеста |
| **Data out** | Анонимный тип / record / tuple / `RailwaySignals.Green` / `RailwaySignals.Red` / `RailwaySignals.Pass` |
| **Adapter generated** | Маппинг manifest ↔ handler — только в `*.g.cs` |
| **Chain validated** | Компилятор проверяет поток вагонов по цепочке станций |
| **Library at boundary** | TrainOP API только в точке сборки маршрута и в runtime-движке |
| **No handler attributes** | Data-oriented маршруты без атрибутов на lambda; схема из анализа кода |
| **Infer, don't declare** | Цепочка, вагоны, типы — из `new TrainRoute()` + `.Station`, return type; upstream — из factory при extension |

### 1.2.1. Минимальный пользовательский API

1. `new TrainRoute()` + `.Station(...)` — единственный публичный API построения маршрута.
2. `.Station(name, (params…) => data)` — handler; имена параметров = ключи вагонов.
3. `DispatchTrain().Travel()` — запуск с пустым стартовым манифестом.

**Начальные данные** (не обязательная отдельная станция): если цепочка начинается с `new TrainRoute()` и upstream пуст, первая станция должна **произвести** вагоны — обычно handler без wagon-параметров (замыкание / аргументы `Build(...)`). Analyzer называет такую станцию *seed*; это роль в графе, а не требование имени или отдельного шага. При extension после factory (`PaymentModule.Build().Station(...)`) локальная seed-станция **не нужна** — вагоны приходят из upstream.

Всё остальное (адаптеры, `PullWagon`/`LoadWagon`, id цепочки, валидация) — **генератор и analyzer**.

### 1.3. Не-цели

- Динамическая сборка маршрута в runtime (`foreach` + `RegisterStation`)
- Автоматический анализ произвольных тел lambda (только сигнатура + известные типы возврата)
- Plugin-загрузка станций из произвольных DLL без перекомпиляции
- Typed `var (a, b) = …Travel()` / deconstruct на `RouteReport` (см. §3.10)

---

## 2. Текущее состояние

| Компонент | Статус |
|-----------|--------|
| Data-oriented `.Station` + codegen-адаптеры | ✅ |
| Chain analyzer (`TOP001`–`TOP013`) | ✅ |
| Якоря: `new TrainRoute()`, локальная после `new`, factory invocation, branch merge | ✅ |
| Cross-assembly: `[RouteSchemaFor]` + `[RouteSchemaWagon]` export/import | ✅ |
| Terminal-доступ: `RouteReport.Get<T>` / indexer | ✅ |
| Legacy `[TrainTuple]` / `[Wagon]` / `WagonTupleGenerator` | ❌ удалены |
| `[TrainRouteChain]`, `DataRouteBuilder` | ❌ удалены |

---

## 3. Архитектура

```
User handler: (T1, T2, ...) => TOut | RailwaySignals.*
        │ per-station adapter (*.g.cs)
StationAdapter: PullWagon → invoke → StationMerge → Signal
        │
TrainRoute.RegisterStation (runtime, codegen-only)

Parallel compile-time:
  ChainDetector → ChainGraph → ChainValidator (TOP001+)
```

### 3.1. API ошибок

**Выбор:** `RailwaySignals.Green` / `Red` / `Pass`.

| Возврат | Поведение адаптера |
|---------|-------------------|
| Анонимный тип / record / struct | merge по именам полей → `Green` |
| `(T1, T2, …)` ValueTuple | merge по ordinal = порядок wagon-параметров (§3.4) |
| `RailwaySignals.Green(payload)` | merge `payload` → `Green` |
| `RailwaySignals.Red(code, msg)` | `RedSignal` |
| `RailwaySignals.Pass` | манифест без изменений |
| `CargoManifest` | escape hatch: заменяет манифест целиком; `TOP004` |

Runtime-типы: `GreenPayload<T>`, `RedFailure`, `GreenPass` (`StationDataResult.cs`).

### 3.2. Якорь цепочки

| Роль | Механизм |
|------|----------|
| Граница цепочки | `new TrainRoute()` + `.Station(...)` или extension после factory |
| Идентификатор | FQN containing method или call-site key (`RouteChainIdBuilder`) |
| Схема handler'а | SemanticModel: параметры и return type lambda |
| Seed (роль в графе) | Станция без wagon-параметров, производящая вагоны при пустом upstream; **не обязательна** при extension после factory |

**Валидные якоря:**

| Якорь | Пример |
|-------|--------|
| Прямая fluent-цепочка | `new TrainRoute().Station(...)` |
| Локальная после `new` | `var r = new TrainRoute(); r.Station(...)` |
| Factory invocation | `PaymentModule.Build().Station(...)` — wagons из factory; локальная seed не требуется |
| Branch join | `cond ? a : b` → merge terminal-состояний перед downstream `.Station` |

**Factory resolution:**

| Видимость factory | Механизм |
|-------------------|----------|
| `private`, non-exported `internal` | Inter-procedural анализ тела в текущей compilation |
| `public` / exported | Generated schema: `[RouteSchemaFor]` + `[RouteSchemaWagon]` |

Return-paths factory: set equality (имя + тип terminal-вагонов); расхождение → **TOP012**; unknown return → **TOP013**.

**Отложено:** параметр / поле / свойство как якорь (`baseRoute.Station`, `_route.Station`).

### 3.3. Cross-assembly composition

Библиотека с generators emit'ит schema для public factory. Consumer резолвит terminal wagons и валидирует локальный хвост.

Подробности: [`docs/cross-assembly-routes.md`](cross-assembly-routes.md).

PoC: `tests/TrainOP.RouteLib.Tests/`, `tests/TrainOP.RouteConsumer.Tests/`.

### 3.4. Маппинг возврата

**Рекомендуется:** анонимные типы, records, **именованные кортежи** `(name: value, …)`, `RailwaySignals.Green` / `Red` / `Pass` — merge по **именам** полей.

**Избегать:** позиционные (неименованные) кортежи `(T1, T2, …)` и смешанные `(a, b: x)`. Они поддерживаются, но merge идёт по ordinal = порядок wagon-параметров handler'а (без `CargoManifest` и `CancellationToken`); при перестановке параметров или рефакторинге сигнатуры ключи в манифесте меняются без явной подсказки в коде. Analyzer предупреждает на tuple literal: **TOP006** (все без имён), **TOP014** (смешанные).

Остальные допустимые возвраты из §3.1 — OK.

### 3.5. Начальные данные в цепочке

*Seed* — термин analyzer'а для станции **без wagon-параметров**, которая вводит вагоны при пустом upstream. Это не отдельный тип станции и не обязательный шаг маршрута.

| Сценарий | Правило |
|----------|---------|
| `new TrainRoute()` без upstream | Нужна станция, производящая первые вагоны; часто — handler без параметров (замыкание / `Build(...)`) |
| Extension после factory | Локальная seed **не нужна**; первая `.Station` consumer'а может сразу требовать вагоны из factory |
| Станция без wagon-параметров | Seed-роль в графе; имя станции произвольное |
| Только `CancellationToken` | Не wagon; станция всё ещё считается seed |
| `CargoManifest` первым параметром | Не seed |
| Две такие станции подряд | Допустимо: вторая merge'ит поверх результата первой |

### 3.6. Terminal-доступ — `RouteReport.Get<T>`

Typed `var (a, b) = …Travel()` **не реализуется**: C# ≤15 не решает конфликты декомпозиции кортежей для общего `RouteReport`; interceptors не меняют bound return type `Travel()`.

Station-interceptors для chain-dispatch (TOP007) сохранены.

### 3.7. Branch merge (§3.8.5)

Для `?:`, `??`, `switch` на receiver:

1. `BranchRouteGraphDiscoverer` — графы по рукавам
2. `BranchRouteJoinSetFinder` — общий downstream
3. `BranchRouteJoinValidator` — совместимость terminal-вагонов
4. Ошибка → **TOP008**; успех → `BranchRouteJoinMerger` + продолжение цепочки

---

## 4. Фазы реализации

| Категория | Содержание |
|-----------|------------|
| **Выполнено** | Фазы **0–8** (см. §4.1) |
| **Отложено** | Якоря параметр / поле / свойство |
| **Снято** | Фазы **9–12** (typed Travel / deconstruct) |

### 4.1. Выполненное (сводка)

| Фаза | Итог |
|------|------|
| **0** | Дизайн: якорь, `RailwaySignals`, кортежи, диагностики, удаление `[TrainTuple]` |
| **1** | `TrainRouteStationGenerator`, адаптеры `.Station`, `StationMerge` |
| **2** | `ChainDetector`, `ChainGraphValidator`, `TOP001`–`TOP007` |
| **3** | `TravelAsync`, `RouteReport.Get` / indexer |
| **4** | `RailwaySignals.Red` / `Pass` в возврате handler'а |
| **5** | `ref`-вагоны, `CargoManifest` escape, nullable-вагоны |
| **6** | Удаление legacy API, docs, end-to-end sample |
| **7** | Якоря `new TrainRoute()`, локальная после `new`, `TOP005` |
| **7D + 8** | Factory invocation, schema export/import, `TOP011`–`TOP013`, cross-assembly PoC |
| **3.8.5** | Branch merge: `TOP008`, подавление `TOP005` на join |

Ключевые файлы реализации §4.5: `RouteFactoryPathAnalyzer`, `RouteFactoryResolver`, `ExternalRouteSchemaResolver`, `RouteSchemaExporter`, `TerminalWagonsComparer`.

### 4.2. Отложено

| Якорь | Пример |
|-------|--------|
| Параметр метода | `baseRoute.Station(...)` |
| Поле / свойство | `_route.Station(...)` |
| Parenthesized / cast на внешнем factory | `(GetRoute()).Station(...)` |

Conditional / switch / coalesce на call site — **реализовано** (§3.7).

### 4.3. Снято с очереди (фазы 9–12)

| Что снято | Почему |
|-----------|--------|
| Typed `var (a, b) = …Travel()` | Interceptor не меняет bound return type |
| `TravelTyped(marker)` | Хуже эргономики, чем `Get` / indexer |
| `Deconstruct` на `RouteReport` | Конфликты декомпозиции кортежей в C# ≤15 |
| `[TrainRouteTerminal]` | Вместе с typed Travel |

---

## 5. Диагностики

| ID | Severity | Условие |
|----|----------|---------|
| `TOP001` | Error | Станция требует вагон, не произведённый ранее |
| `TOP002` | Error | Конфликт типов вагона между станциями |
| `TOP003` | Error | Вагон удалён частичным возвратом, но нужен дальше |
| `TOP004` | Warning | Handler вернул `CargoManifest` — полная замена |
| `TOP005` | Error | Data-lambda вне легитимного якоря `TrainRoute` |
| `TOP006` | Warning | Неименованный value tuple (на literal): order-dependent mapping |
| `TOP007` | Error | Конфликт имён вагонов для одной сигнатуры handler'а |
| `TOP008` | Error | Нельзя соединить ветки маршрута перед downstream Station |
| `TOP009` | Error | Handler не лямбда / anonymous / однозначный method group |
| `TOP010` | Error | Handler возвращает `GreenSignal` / `RedSignal` вместо data / `RailwaySignals` |
| `TOP011` | Info | Public factory в referenced assembly без exported schema |
| `TOP012` | Error | Return-paths factory имеют разное terminal-множество |
| `TOP013` | Error | Return-path factory с `HasUnknownReturn` |
| `TOP014` | Warning | Смешанный value tuple (на literal): ambiguous/fragile mapping |

Release tracking: `AnalyzerReleases.Shipped.md`.

---

## 6. Shared runtime helpers

- **`StationMerge`** — merge возврата handler'а в манифест (`Apply`, `ToSignal`)
- **`WagonStationReturn`** — чтение анонимных типов и кортежей; ordinal = `inputWagonNames`

---

## 7. Правила для AI-агентов

1. Читать этот файл перед изменениями в generators/analyzer.
2. Не добавлять атрибуты на data-handler lambda.
3. Атрибуты схемы — только на **generated** export type (`[RouteSchemaFor]`, `[RouteSchemaWagon]`).
4. Минимальный diff; не возвращать удалённый legacy API (`[TrainTuple]`, typed Travel).
5. Сверять ID диагностик с `TrainRouteDiagnostics.cs`.

**Целевой стиль handler'а:**

```csharp
// ✅
.Station("X", (string id, decimal amount) => new { id, amount = amount + 1 })

// ✅ ошибка как данные
.Station("X", (string id, decimal amount) =>
    amount > 0 ? RailwaySignals.Green(new { id, amount }) : RailwaySignals.Red("ERR", "..."))

// ⚠️ escape hatch
.Station("X", (CargoManifest m, string id) => new { id = id + m.PullWagon<string>("traceId") })

// ❌ в бизнес-handler'ах
.Station("X", (string id) => new CargoManifest().LoadWagon("id", id))
```

---

## 8. Критерии завершения проекта

- [x] Payment flow без `LoadWagon`/`PullWagon` в handler'ах
- [x] Analyzer: missing wagon, type conflict, orphan, factory schema, branch merge
- [x] Якоря: direct, local, factory, cross-assembly
- [x] Terminal-доступ: `RouteReport.Get` / indexer
- [x] Legacy API удалён
- [x] Документация: `getting-started`, `core-api`, `cross-assembly-routes`, `nuget`
- [ ] Якоря параметр / поле / свойство (отложено)

---

## 9. Ссылки в репозитории

| Файл | Назначение |
|------|------------|
| `src/TrainOP/Railway.cs` | Runtime маршрута |
| `src/TrainOP/RouteSchemaForAttribute.cs` | Маркер exported schema |
| `src/TrainOP.Generators/TrainRouteStationGenerator.cs` | Data-oriented адаптеры |
| `src/TrainOP.Generators/RouteSchemaExporter.cs` | Emit schema для public factory |
| `src/TrainOP.Generators/ChainDetector.cs` | Обнаружение цепочек |
| `src/TrainOP.Generators/ChainValidationAnalyzer.cs` | Валидация графа |
| `tests/TrainOP.Tests/DataOrientedPaymentRouteEndToEndTests.cs` | Сквозной payment flow |
| `tests/TrainOP.RouteConsumer.Tests/` | Cross-assembly PoC |
| `docs/cross-assembly-routes.md` | Межсборочная композиция |

---

## 10. История изменений плана

| Дата | Изменение |
|------|-----------|
| 2026-07-02 | Первая версия; удалены `DataRouteBuilder`, `[TrainRouteChain]` |
| 2026-07-06–14 | Фазы 0–7, branch merge §3.8.5, снятие typed Travel (9–12) |
| 2026-07-16 | Factory anchors + schema export (§4.5) |
| 2026-07-17 | Очистка плана: свёрнуты выполненные фазы, удалены spike-чеклисты и legacy baseline |
