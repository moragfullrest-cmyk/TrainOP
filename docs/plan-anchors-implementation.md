# План реализации: statement-local + прочие якоря

> **Статус:** **закрыт** — SL + D + C-10/C-11 + DOC-4 сделаны (gate — у оператора).  
> **Детализация:** [`plan-statement-local-chains.md`](plan-statement-local-chains.md), [`plan-misc-csharp-anchors.md`](plan-misc-csharp-anchors.md).  
> **Аудитория:** разработчики и AI-агенты.

---

Сводка из планов statement-local и «Прочий C#». Opaque без явной схемы манифеста и отвергнутый «прочий C#» **не** входят.

**Сделано:** A0-матрица misc; принципы; заметка opaque+схема манифеста; **SL-0…SL-4**; **D-***; **C-10**; **C-11**; **DOC-4**.

**Build gate** (агент не гоняет без явной просьбы):

```bash
dotnet test tests/TrainOP.Generators.Tests/TrainOP.Generators.Tests.csproj -c Release
dotnet test tests/TrainOP.Tests/TrainOP.Tests.csproj -c Release
```

SL-2b + RouteConsumer; финал — `dotnet test TrainOP.sln -c Release`.

Ключевые файлы: [`RouteChainWalker.cs`](../src/TrainOP.Generators/RouteGraph/RouteChainWalker.cs), [`RouteGraphAssembler.cs`](../src/TrainOP.Generators/RouteGraph/RouteGraphAssembler.cs), [`RouteFactoryPathSimulator.cs`](../src/TrainOP.Generators/Discovery/RouteFactoryPathSimulator.cs).

```mermaid
flowchart TB
  sl0["SL0 collector"] --> sl1["SL1 bare new"]
  sl1 --> sl2a["SL2a factory inline"]
  sl2a --> sl2b["SL2b factory schema"]
  sl2b --> sl3a["SL3a fluent-new"]
  sl3a --> sl3b["SL3b fluent-factory"]
  sl3b --> sl4["SL4 docs aliases"]
  sl1 -.-> d14["D14 local fn"]
  d14 --> d15["D15 await"]
  d15 --> d16["D16 cast"]
  d16 --> d3["D3 out"]
  d3 --> d2["D2 tuple"]
  d2 --> d17["D17 pattern"]
  sl4 --> c10["C10 ternary local"]
  c10 --> c11["C11 switch local"]
  d17 --> docs4["Docs init-before-Station"]
  c11 --> docs4
```

## ToDo

| Id | Работа | Статус |
|----|--------|--------|
| SL-0 | `CollectLocalStatementStationLinks`; `BuildAnchorKey` по origin; preceding-origin API | 🟢 сделано |
| SL-1 | `LocalVariable` после bare `new` + EndingAt + тесты + gate | 🟢 сделано (gate — у оператора) |
| SL-2a | Локаль после private/internal factory + gate | 🟢 сделано (gate — у оператора) |
| SL-2b | Локаль после public `FactorySchema` + RouteConsumer gate | 🟢 сделано (gate — у оператора) |
| SL-3a | Fluent-RHS root `new` + gate | 🟢 сделано (gate — у оператора) |
| SL-3b | Fluent-RHS root factory + gate | 🟢 сделано (gate — у оператора) |
| SL-4 | Docs statement-local; алиасы TOP005; финальный gate | 🟢 сделано (gate — у оператора) |
| D-14 | Local function = factory | 🟢 сделано (gate — у оператора) |
| D-15 | `await` — все origin/factory | 🟢 сделано (gate — у оператора) |
| D-16 | Cast docs/тесты | 🟢 сделано (gate — у оператора) |
| D-3 | `out TrainRoute` ≈ return/factory | 🟢 сделано (gate — у оператора) |
| D-2 | Узкий tuple/deconstruct | 🟢 сделано (gate — у оператора) |
| D-17 | Pattern `is`/`switch` с known origin | 🟢 сделано (gate — у оператора) |
| C-10 | `?:` assign локали + join (после SL) | 🟢 сделано (gate — у оператора) |
| C-11 | `switch` assign локали + join (после SL) | 🟢 сделано (gate — у оператора) |
| DOC-4 | Init-before-Station + README | 🟢 сделано (gate — у оператора) |

## Вне скоупа

- Параметр / поле / свойство / делегат; opaque без утверждённой схемы манифеста.
- Массив, indexer, reflection, `dynamic`, LINQ, dual-writer алиасы, static field, collection expr, `ref`/`in`.
- CFG statement-`if`/`else` без join; using/IDisposable (N/A).

## Handoff

```text
Планы якорей закрыты: @docs/plan-anchors-implementation.md
Детали: @docs/plan-statement-local-chains.md @docs/plan-misc-csharp-anchors.md
Не запускай dotnet test/build, пока я явно не попрошу.
TrainOP; когитатор; not-work-project.
```
