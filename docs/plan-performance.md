# План: производительность Travel / hot path

> **Статус:** **P0–P3 + P4a + P4 выполнены**; **P5 снято** (typed bags — регрессия CPU); **P6 снято** (Freeze — низкий ROI по бенчу); **P7 не начато**.  
> **Цель:** снизить стоимость инфраструктуры hop в `Travel()` / `TravelAsync` без изменения data-oriented UX handler'ов.  
> **Метрика успеха:** снижение Ratio и Alloc в `LibraryVsManualBenchmarks` (TravelOnly; отдельно — TravelLight). **Не** цель догнать manual ns.  
> **Аудитория:** разработчики и AI-агенты, продолжающие работу над TrainOP.  
> **Связанный план:** data-oriented handlers — [`plan-data-oriented-handlers.md`](plan-data-oriented-handlers.md) (фазы 0–8 выполнены).

---

## 1. Цель и не-цели

### 1.1. Цель

Ускорить **транспортный протокол** между станциями: манифест, адаптер, merge, executor, отчёт. Бизнес-логика handler'ов и публичный стиль `.Station((params) => data)` не меняются.

### 1.2. Не-цели

- Догнать hand-written pipeline по наносекундам
- Менять семантику сигналов / red recovery / cancellation
- Динамическая runtime-сборка маршрута

### 1.3. Зафиксированные решения

| Тема | Выбор |
|------|--------|
| Публичный API | `CargoManifest` **мутабелен** (`LoadWagon`/`UnloadWagon` in-place, возвращают `this`) |
| Запись на hop | Без clone словаря на каждую запись; один экземпляр манифеста на прогон |
| Sync path | Отдельный sync-цикл для `Travel()` / `Travel(CancellationToken)` без `async`/`await` на hop; `TravelAsync` без изменений по контракту |
| Chain merge | Typed merge для chain-aware при известном return shape; иначе `StationMerge.ToSignal` |
| Журнал (P4a) | В visit — имя + флаг; полный сигнал только в `TerminalSignal` (перезапись на hop) |
| Метрики | Ratio/Alloc в `LibraryVsManual*` TravelOnly |

### 1.4. Уже доступно без нового кода

- **Caller dispatch** (ctor+ordinal; единственный режим) — chain-dispatch без Roslyn interceptors; см. [`plan-data-oriented-handlers.md`](plan-data-oriented-handlers.md) §4.3 и [`architecture-internals.md`](architecture-internals.md).
- Кэшировать `TrainRoute` и гонять только `Travel()` (бенчмарки: BuildAndTravel vs TravelOnly).
- В handler'ах предпочитать named / `ValueTuple` вместо `new { ... }` — меньше аллокаций на hop (сторона вызывающего кода).

---

## 2. Baseline

Источник: `BenchmarkDotNet.Artifacts/results/TrainOP.Benchmarks.LibraryVsManualBenchmarks-report-github.md` (.NET 10, Release, caller adapter).

| Сценарий | Manual | TrainOP TravelOnly | Ratio | Alloc (TrainOP) |
|----------|--------|--------------------|-------|-----------------|
| Payment (2 ст.) | ~4.7 ns | ~414 ns | **~89×** | ~1840 B |
| LongPayment (5 ст.) | ~16 ns | ~1094 ns | **~68×** | ~4208 B |
| Checkout (7 ст.) | ~17 ns | ~2531 ns | **~150×** | ~12448 B |

BuildAndTravel дороже TravelOnly на стоимость регистрации станций; оптимизационные фазы ориентируются на **TravelOnly**.

Запуск:

```bash
dotnet run -c Release --project benchmarks/TrainOP.Benchmarks -- --filter *LibraryVsManual*
```

Подробнее: [`benchmarks/README.md`](../benchmarks/README.md).

---

## 3. Горячий путь

```mermaid
flowchart TD
  Travel["Travel / TravelAsync"]
  Loop["TravelCore / TravelCoreAsync loop"]
  Exec["ExecuteStation / ExecuteStationAsync"]
  Adapter["Generated adapter"]
  Pull["PullWagon string dict"]
  Handler["User handler"]
  Merge["StationMerge.ToSignal / typed merge"]
  Load["LoadWagon in-place"]
  Visit["StationVisit + RouteReport"]
  Travel --> Loop --> Exec --> Adapter
  Adapter --> Pull --> Handler --> Merge --> Load --> Visit
```

Ключевые места:

| Символ / файл | Роль |
|---------------|------|
| `Train.Travel` → `TravelCore` | Sync-цикл без `async`/`await` на hop (`TrainRouteRuntime.cs`) |
| `Train.TravelAsync` → `TravelCoreAsync` | Async-цикл с `await` на hop |
| `ExecuteStation` / `ExecuteStationAsync` / `ProcessStationStep*` | Диспетчер `StationPlan` + bookkeeping |
| `CargoManifest.LoadWagon` / `UnloadWagon` | In-place запись в `Dictionary<string,object>` (без clone) |
| Generated adapter (`TrainRouteStationGenerator`) | Pull → handler → merge → `Signal` |
| `StationMerge` / `WagonStationReturn` | Runtime merge; reflection для return members |
| Chain-dispatch | `UsesChainDispatch` → typed merge при известном return shape; иначе `StationMerge.ToSignal` |

Разрыв с manual — цена абстракции (манифест, адаптеры, сигналы, отчёт), а не арифметика станций. См. также [`code-volume-comparison.md`](code-volume-comparison.md).

---

## 4. Фазы

Порядок = ожидаемый impact. Каждая фаза самостоятельна по критериям готовности (§5).

```mermaid
flowchart LR
  P0[P0 Mutable CargoManifest]
  P1[P1 Sync Travel executor]
  P2[P2 Typed merge chain-dispatch]
  P3[P3 Binding cache at register]
  P4a[P4a Slim StationVisit]
  P4[P4 TravelLight]
  P7[P7 Slim ExecuteStation]
  P5[P5 Reduce boxing]
  P0 --> P1 --> P2 --> P3 --> P4a --> P4 --> P7
  P4a -.-> P5
```

P5 и P6 сняты. Курс вперёд: **P7** опционально (после замера; иначе снять).

### P0 — Mutable CargoManifest

**Статус:** сделано (2026-07-17).

**Суть:** `CargoManifest` мутабелен: `LoadWagon` / `UnloadWagon` пишут in-place и возвращают `this`. Убраны `CloneWagons`. Добавлен `TryGetWagon`. `InspectWagons` — live view внутреннего словаря.

**Файлы:** `src/TrainOP/Railway.cs` / `TrainRouteRuntime.cs`, `src/TrainOP/StationMerge.cs`, docs.

**Ожидание:** главный выигрыш по Gen0 и CPU на маршрутах с несколькими вагонами и hop'ами.

### P1 — Sync Travel executor

**Статус:** сделано (2026-07-17).

**Суть:** `Travel()` / `Travel(CancellationToken)` исполняют sync-цикл (`TravelCore`) без `async`/`await` на hop и без `GetAwaiter().GetResult()` вокруг `TravelCoreAsync`. Общие sync helpers (`ExecuteStation`, `ProcessStationStep`, `InvokeServiceStation`) и shared exception/visit helpers. `TravelAsync` сохраняет контракт через `TravelCoreAsync`.

**Файлы:** `src/TrainOP/TrainRouteRuntime.cs` (executor).

**Ожидание:** снятие async state machine tax на sync hot path.

### P2 — Typed merge для chain-dispatch

**Статус:** сделано (2026-07-17); **уточнено (2026-07-21)** — unrolled `MergePlan` вместо runtime-циклов.

**Суть:** расширить typed merge на chain-aware адаптеры. Убрать runtime reflection через `WagonStationReturn` там, где return shape известен на compile-time.

**2026-07-21:** typed merge больше не копирует `StationMerge.Apply` через `for`/`switch` по именам. `MergePlanBuilder` строит статический план (by-name, positional ItemN, partial unload, extra members); codegen эмитит прямые `LoadWagon`/`UnloadWagon` с `wagonNames[i]` и `stationReturn.{member}`.

**Файлы:** generators — `TypedStationReturn*`, `MergePlan*`, `TrainRouteStationGenerator`, chain-aware emit.

**Ожидание:** заметный выигрыш на сценариях бенчмарков Payment / LongPayment / Checkout (chain-dispatch).

### P3 — Cache chain binding

**Статус:** сделано (2026-07-17).

**Суть:** resolve `chainKey` + `chainStationIndex` (binding table) один раз при регистрации станции; travel lambda закрывается над стабильными `inputNames` / `returnMembers` / `refFlags`. Передача статического `ChainBinding_*` в overload `StationCore_*(..., binding)` без `ResolveChainBinding_*` на hot path.

**Файлы:** chain-aware codegen, `TrainRouteStationGenerator`.

### P4a — Slim StationVisit (флаг + только TerminalSignal)

**Статус:** сделано (2026-07-17).

**Суть:** облегчить **дефолтный** журнал без отказа от истории шагов.

**Было:** на каждый hop в `Visits` клался полный `Signal` — N сигналов удерживались до конца `Travel`. После P0 `visit.Signal.Manifest` редко давал исторический снимок. Главная цена — удержание промежуточных сигналов.

**Сделано:** `StationVisit` = `readonly struct` (`StationName` + `IsGreen`); `visit.Signal` удалён; полный сигнал только в `RouteReport.TerminalSignal`.

**Канон реализации:**

| Элемент | Поведение |
|---------|-----------|
| На hop | `terminalSignal = signal` (перезапись); в журнал — visit **без** полного сигнала |
| `StationVisit` | `StationName` + флаг исхода (`IsGreen` / эквивалент); предпочтительно `readonly struct` |
| `RouteReport.TerminalSignal` | единственный полный сигнал (финал green или стоп-red, в т.ч. после service station) |
| `visit.Signal` | убран |

**Семантика журнала после P4a:**

- История = имя станции + green/red на шаге (включая visit service station при recovery).
- Детали ошибки (`FailureCode` / `FailureMessage` / `Issue`) — только из `TerminalSignal`, не из промежуточных visit.
- Промежуточный red с успешным recovery: в журнале будет `IsGreen == false` у станции и visit service; код/текст исходного red в visit **не** сохраняются.

**Файлы:** `TrainRouteRuntime.cs` (`StationVisit`, `ProcessStationStep*`, `CompleteRedSignalStep`, `RouteReport`), docs `core-api.md`, samples/tests.

**Ожидание:** средний выигрыш Alloc / давления на GC на любом `Travel()` с визитами; меньше, чем полный отказ от журнала (P4), но без нового API.

**Связь с P4:** P4a меняет **форму** дефолтного журнала; P4 — opt-in **без** журнала вовсе. Порядок: сначала P4a (готово), затем P4.

### P4 — Lightweight travel API (TravelLight)

**Статус:** сделано (2026-09-16).

**Суть:** opt-in без накопления `StationVisit`: `TravelLight()` / `TravelLightAsync()` (+ CT). Не ломает `Travel()` → `RouteReport` с визитами (slim после P4a).

- В [`src/TrainOP/TrainRouteRuntime.cs`](../src/TrainOP/TrainRouteRuntime.cs): `recordVisits` в `TravelCore` / `TravelCoreAsync`; light не аллоцирует journal и не вызывает `visits.Add`; `RouteReport.Visits` = `Array.Empty<StationVisit>()`.
- `TerminalSignal` / `Manifest` / `Get` / `Failure*` без изменений семантики.
- Бенч: `TrainOP_TravelLightOnly_*` в `LibraryVsManualBenchmarks`.

**Ожидание:** средний выигрыш Alloc на длинных маршрутах, когда журнал шагов не нужен.

### P5 — Reduce boxing / typed slots

**Статус:** **снято** (2026-07-17) — откат к одному `Dictionary<string, object>`.

**Суть (попытка):** typed bags для value types + `LoadWagon<T>` + home-index. На коротких маршрутах (Payment/Checkout) CPU от multi-bag / typeof / home lookup оказался **хуже**, чем boxing в один словарь; home-index не вернул уровень post-P4a.

**Итог:** storage снова единый object-dict (как после P0/P4a). `LoadWagon<T>` удалён; codegen снова эмитит `LoadWagon(name, value)`. **Не возобновлять.**

**Файлы:** `CargoManifest` / runtime.

### P6 — Freeze маршрута (только явный API)

**Статус:** **снято** (2026-09-22) — после бенча `LibraryVsManual*` эффект −4…18% Mean / −112…160 B Alloc; Ratio к Manual почти не двигается. API и бенч-пары Frozen удалены. **Не возобновлять.**

**Суть (попытка):** публичный `Freeze()` → `StationPlan[]` без snapshot на каждый Travel; без Freeze — `List` snapshot и достраивание.

### P7 — Slim диспетчер hop

**Статус:** не начато.

В `ExecuteStation` / `ExecuteStationAsync` — цепочка `if (plan.X != null)` по нескольким делегатам ([`StationPlan.cs`](../src/TrainOP/StationPlan.cs)).

- Ввести `enum StationInvokeKind` (или аналог) + один релевантный делегат на plan при регистрации.
- Убрать лишние null-проверки на hot path; поведение async/sync и service station без изменений.

**Файлы:** `StationPlan.cs`, `TrainRouteRuntime.cs`.

**Ожидание:** небольшой, но стабильный выигрыш CPU на hop (особенно короткие маршруты вроде Payment).

### Не делать (отвергнуто / высокий риск)

- **P5 typed bags** — не возобновлять (регрессия на Payment/Checkout).
- **P6 Freeze** — не возобновлять (низкий ROI vs Manual; API удалён).
- Полная генерация unrolled `Travel` под цепочку — отдельный spike только после профилирования post-P4; в этот курс не входит.
- Смена `Dictionary<string,object>` на слоты по индексу — только как отдельный эксперимент после замеров; не смешивать с P4/P7.

---

## 5. Критерии готовности фазы

Для каждой фазы P0–P3, P4a, P4, P7 (и исторически P5, P6):

- [ ] Поведение публичного API и семантика сигналов / отмены без регрессий (оператор гоняет тесты)
- [ ] `LibraryVsManualBenchmarks` TravelOnly (и TravelLight для P4): Ratio и/или Alloc ниже baseline §2 (артефакт в `BenchmarkDotNet.Artifacts` или обновление цифр в этом плане)
- [ ] Краткая запись в §7 истории этого плана
- [ ] При необходимости — строка в `CHANGELOG.md` / `benchmarks/README.md`

---

## 6. Ссылки в репозитории

| Файл | Назначение |
|------|------------|
| `src/TrainOP/TrainRouteRuntime.cs` | `Travel` / executor |
| `src/TrainOP/StationPlan.cs` | kind + делегаты hop |
| `src/TrainOP/StationMerge.cs` | Runtime merge / `ToSignal` |
| `src/TrainOP/WagonStationReturn.cs` | Reflection return members |
| `src/TrainOP.Generators/TrainRouteStationGenerator.cs` | Адаптеры, chain-dispatch |
| `benchmarks/README.md` | Запуск и категории бенчмарков |
| `benchmarks/TrainOP.Benchmarks/LibraryVsManualBenchmarks.cs` | Library vs manual |
| `benchmarks/TrainOP.Benchmarks/ManualPipelineScenarios.cs` | Manual baseline |
| `docs/code-volume-comparison.md` | Trade-off объём кода vs абстракция |
| `docs/core-api.md` | Публичный API |
| `docs/plan-data-oriented-handlers.md` | UX / analyzer roadmap (выполнено) |

---

## 7. История изменений плана

| Дата | Изменение |
|------|-----------|
| 2026-07-17 | Создание плана по результатам `LibraryVsManualBenchmarks` и разбору hot path; фазы P0–P5 |
| 2026-07-17 | P0: `CargoManifest` сделан мутабельным in-place (вместо отдельного travel buffer + immutable snapshot) |
| 2026-07-17 | P1: sync `TravelCore` / `ExecuteStation` / `ProcessStationStep` / `InvokeServiceStation`; `Travel()` больше не блокирует `TravelCoreAsync` |
| 2026-07-17 | P2: typed merge в chain-aware адаптерах (`EmitChainAware*` / reflection); `BuildCompileTimeReturnMembersExpression` для reflection mode |
| 2026-07-17 | P3: кэш chain binding при регистрации — hoisted `inputNames`/`returnMembers`/`refFlags`; interceptor fast path через статический `ChainBinding_*` |
| 2026-07-17 | Добавлена фаза **P4a**: slim `StationVisit` (флаг + только `TerminalSignal`); P4 уточнён как opt-in без журнала |
| 2026-07-17 | P4a: `StationVisit` → `readonly struct` (`StationName` + `IsGreen`); `visit.Signal` удалён; полный сигнал только в `RouteReport.TerminalSignal` |
| 2026-07-17 | Pre-size visit journal: `List` capacity = `_route.Count` (×2 при наличии service station) |
| 2026-07-17 | P5: typed bags в `CargoManifest` (`decimal`/`int`/`long`/`bool`/`double`/`float` + object fallback); `LoadWagon<T>`; typed merge codegen → `LoadWagon<T>`; `InspectWagons` — combined snapshot |
| 2026-07-17 | P5 hot-path fix: home-index вместо `Remove`/`Contains` по всем bags на каждый `LoadWagon`/`PullWagon` |
| 2026-07-17 | **P5 снято:** откат typed bags → один `Dictionary<string,object>`; `LoadWagon<T>` удалён |
| 2026-07-17 | Ссылка на идею Caller*-альтернативы Station-interceptors ([`plan-data-oriented-handlers.md`](plan-data-oriented-handlers.md) §4.3) |
| 2026-07-20 | Ссылка на spike S0–S4 и go/no-go в §4.3.11–4.3.12 |
| 2026-09-16 | Продолжение курса (бывший `plan-acceleration.md`): P4 → freeze → slim dispatch |
| 2026-09-16 | **Объединение** с `plan-acceleration.md`: P4 уточнён (`TravelLight`); добавлены **P6** freeze и **P7** slim `ExecuteStation`; файл `plan-acceleration.md` удалён |
| 2026-09-16 | **P4 сделано:** `TravelLight` / `TravelLightAsync` (+ CT); empty `Visits`; бенч `TravelLightOnly_*` |
| 2026-09-22 | **P6 снято:** `Freeze` удалён после бенча (Alloc −112…160 B, Mean −4…18%; Ratio к Manual почти без сдвига) |
