# Cross-assembly route composition

TrainOP supports building routes in a class library and extending them in a consumer project.

## Route library

Publish a public factory that returns `TrainRoute` with a data-oriented chain inside:

```csharp
public static class PaymentModule
{
    public static TrainRoute Build() => new TrainRoute()
        .Station("Seed", () => new { paymentId = "pay-1", amount = 100m })
        .Station("Discount", (string paymentId, decimal amount) =>
            new { paymentId, amount = amount * 0.9m });
}
```

Reference `TrainOP` and `TrainOP.Generators` in the library project. The generator emits:

- `[RouteSchemaFor(typeof(PaymentModule), "Build")]`
- repeated `[RouteSchemaWagon(name, typeof(T))]` attributes
- `TerminalWagons` array for runtime inspection

## Consumer application

```csharp
public static class AppRoute
{
    public static TrainRoute Build() =>
        PaymentModule.Build()
            .Station("Finalize", (string paymentId, decimal amount) =>
                new { paymentId, status = "completed" });
}
```

The analyzer resolves exported terminal wagons from the referenced assembly and validates the local extension chain.

## Resolution rules

| Factory visibility | Mechanism |
|--------------------|-----------|
| `private` / non-exported `internal` | Inter-procedural analysis of the factory body |
| `public` (and other exported APIs) | Generated schema lookup via `[RouteSchemaFor]` / `[RouteSchemaWagon]` |

## Factory return-path validation

If a factory has multiple return paths (`if` / ternary / multiple `return` statements), all paths must produce the same terminal wagon set (names and types). Order is ignored. Divergence reports **TOP012**.

Unknown terminal state on any path reports **TOP013**.

## Diagnostics

| ID | Meaning |
|----|---------|
| TOP011 | Referenced public factory has no exported schema; join is not validated |
| TOP012 | Factory return paths diverge |
| TOP013 | Factory return path has unknown terminal state |

## Tuple return warnings

| ID | When | Why it matters |
|----|------|----------------|
| TOP006 | Tuple element has default `ItemN` (no `NameColon` and no name inference), e.g. `(id + "-x", amount * 0.9m)` | Manifest keys become positional `Item1`, `Item2`, … |
| — | Inferred `(paymentId, amount)` or explicit `(Item1: x, …)` | Treated as intentional names; no warning |

Diagnostics are reported on the **tuple literal**, not on the handler method.

## Samples in this repository

- `tests/TrainOP.RouteLib.Tests` — route module library
- `tests/TrainOP.RouteConsumer.Tests` — consumer extension + runtime test
