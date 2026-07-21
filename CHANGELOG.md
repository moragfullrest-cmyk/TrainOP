# Changelog

All notable changes to TrainOP are documented in this file.

Format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Changed

- **CodegenWriter:** migrated all source emit paths to indent-stack API; removed `Builder` escape hatch and string-based `StatementIndent` from emit contexts.

## [0.12.0] - 2026-07-21

### Changed

- **Codegen emit refactor:** consolidated source generation into `Emit` extension methods on handler/route models with shared infrastructure (`CodegenWriter`, `CodegenContext`, `EmissionState`, `NamingScope`, `PullStrategy`).
- **File-level emitters:** `TrainRouteExtensionsFile` and `RouteSchemasFile` now own extension/schema file emission; `TrainRouteStationGenerator` is slim orchestration only.
- **Chain and canonical paths:** `ChainAwareEmission`, `ChainBindingTable`, and `CanonicalEmission` replace monolithic `*Codegen` static classes.
- **Naming cleanup:** renamed discovery/diagnostics types (`RouteChainWalker`, `RouteFactoryPathSimulator`, `TrainRouteValidationAnalyzer`, `DelegateSignatureGroup`, …) and runtime entry points (`TrainRouteRuntime`, `StationDataHandlerResults`).

### Removed

- **Legacy codegen wrappers:** `HandlerFuncTypeCodegen`, `StationAdapterBodyCodegen`, `WagonBindingCodegen`, `TypedStationReturnCodegen`, `ChainAwareStationCodegen`, and `RouteSchemaExporter` (logic moved to extensions/resolvers/file emitters).

## [0.11.0] - 2026-07-21

### Fixed

- **Typed merge for default ItemN tuples:** compile-time merge now maps input wagons by positional return members (`Item1`, `Item2`, …) instead of name-based switches that never matched manifest wagon keys.

### Changed

- **Typed station return codegen:** replaced runtime loops and `switch` over wagon/return member names with unrolled `MergePlan`-driven `LoadWagon`/`UnloadWagon` emission when the handler return shape is known at compile time.
- **Generator modularization:** reorganized source generator into focused folders (`Chain/`, `Codegen/`, `Handlers/`, `Route/`, `RouteGraph/`, `Schema/`, `Merge/`, …) with `RouteGraph`/`RouteSite` discovery and `HandlerSchemaResolver` pipeline.

### Removed

- **Reflection chain-dispatch:** removed `TrainOP_ChainDispatchMode=reflection`, `StationHandlerParameterNames`, and reflection-specific generator emission; caller dispatch is the only chain-dispatch path.
- **Tests and benchmarks:** removed `TrainOP.ReflectionDispatch.Tests`, `TrainOP.Benchmarks.Reflection`, and `ChainDispatchBenchmarks` (reflection vs caller comparison).

## [0.10.0] - 2026-07-20

### Changed

- **Chain-dispatch default:** switched generated chain dispatch to `caller` mode (ctor+ordinal) and removed the Roslyn-interceptor emission path.
- **Validation and diagnostics:** added caller-chain validation and clearer diagnostics for unsupported chain shapes in generators.
- **Release surface:** aligned docs, benchmarks, CI conditions, and package metadata around the single-TFM `netstandard2.0` + caller-default release.

## [0.9.0] - 2026-07-17

### Added

- Architecture guide for generators, interceptors, and runtime (`docs/architecture-internals.md`).
- Plan note: Caller*-based alternative to Station-interceptors (`docs/plan-data-oriented-handlers.md` §4.3).

### Changed

- **Runtime modularization:** extracted `StationPlan`, `ServiceStationPlan`, `StationAdapter`, `StationStepResult`, and related helpers from `Railway.cs`.
- **Generator modularization:** split station codegen into focused types (`StationAdapterBodyEmitter`, handler I/O models, `MergedStationSchema`, `TypeSignatureGroup`, delegate signature helpers); removed `StationReturnMetadataBuilder`.
- Documentation index links updated for the architecture guide.
- **Single-TFM ship:** runtime package is now `netstandard2.0` only (removed `net8.0` multi-target).
- **Chain-dispatch mode default:** switched default to `caller` (ctor+ordinal dispatch) without Roslyn interceptors; removed the SDK gate.

## [0.8.0] - 2026-07-17

### Added

- BenchmarkDotNet suite (`benchmarks/`): reflection vs interceptor chain-dispatch; library vs hand-written pipelines.
- Sample + docs for code-volume comparison (`docs/code-volume-comparison.md`, checkout pipeline with tokens/failures/recovery).
- Performance roadmap (`docs/plan-performance.md`).

### Changed

- **`CargoManifest` is mutable:** `LoadWagon` / `UnloadWagon` update in place and return `this` (no per-wagon dictionary clone). Added `TryGetWagon`. `InspectWagons` returns a live view of the internal dictionary.
- **`Travel()` sync path:** dedicated `TravelCore` loop without `async`/`await` per hop or blocking on `TravelCoreAsync`.
- **Chain-dispatch typed merge:** chain-aware generated adapters emit compile-time return merge when return shape is known (interceptor: `binding.ReturnMembers`; reflection: inline member array), same gates as non-chain adapters.
- **Chain binding cache (P3):** chain-aware adapters hoist `inputNames` / `returnMembers` / `refFlags` at station registration; interceptors pass static `ChainBinding_*` fields directly to `StationCore_*` (no `ResolveChainBinding_*` on interceptor path).
- **`StationVisit` slim journal (P4a):** `StationVisit` is now a `readonly struct` with `StationName` and `IsGreen` only; removed `Signal` property. Full signal details remain on `RouteReport.TerminalSignal` / `FailureCode` / `FailureMessage`. Visit list is pre-sized (`route.Count`, or `×2` when a service station is configured) to avoid resize allocations.

### Notes

- Typed multi-bag storage (attempted as P5) was **not** shipped: CPU regression vs single `Dictionary<string, object>` on short routes.

## [0.7.0] - 2026-07-17

### Added

- Multi-target runtime package: `netstandard2.0` and `net8.0`.
- SDK-conditional chain-dispatch via `TrainOP_ChainDispatchMode` (`stable` / `experimental` / `reflection`) in `TrainOP.Generators.targets`.
- Reflection fallback for conflicting wagon names: `StationHandlerParameterNames` resolves input names from `ParameterInfo` when Roslyn interceptors are unavailable.
- CI matrix for .NET SDK 8 / 9 / 10; `TrainOP.ReflectionDispatch.Tests` forces reflection mode.

### Changed

- **TOP006** warns only for default `ItemN` tuple elements (no `NameColon` and no name inference). Inferred names and explicit `Item1:` do not warn.
- Documentation updated for TFM, chain-dispatch modes, and tuple naming policy.

### Removed

- **TOP014** (mixed named/unnamed tuple warning); superseded by the unified TOP006 default-ItemN rule.

## [0.6.0] - 2026-07-17

### Added

- Cross-assembly route composition: exported terminal schema via `[RouteSchemaFor]` / `[RouteSchemaWagon]` for public factories.
- Factory return-path validation (**TOP012**, **TOP013**) and missing-schema info (**TOP011**).
- Tuple return warnings on tuple literals: **TOP006** (unnamed) and **TOP014** (mixed named/unnamed).
- Runtime signal return diagnostic **TOP010** for `GreenSignal` / `RedSignal` in data handlers.
- Tests: `TrainOP.RouteLib.Tests`, `TrainOP.RouteConsumer.Tests`, factory schema and tuple analyzer coverage.
- Documentation: `docs/cross-assembly-routes.md`, analyzer diagnostics table in `docs/core-api.md`.

### Changed

- Analyzer diagnostics **TOP006** / **TOP014** are reported on the tuple literal, not on the handler method.
- Chain detection extended for factory anchors, local reassignment, and cross-assembly joins.
- `RouteReport` access helpers and generator return-shape inference improvements.

### Fixed

- `docs/nuget.md`: corrected analyzer ID prefix (`TOPxxxx` instead of `TRNxxxx`).

## [0.5.0] - 2026-07-14

### Changed

- Removed obsolete `Travel(CargoManifest)`; seed-only travel is canonical.
- Densified analyzer diagnostics **TOP001**–**TOP009** messaging.

## [0.4.0]

- Same-compilation non-lambda station handlers (method group, anonymous method).
- Diagnostic **TOP009** for unsupported handler forms.

## [0.3.0]

- Seed-only travel and branch-route join analysis (**TOP008**).

## [0.2.0]

- Simplified travel output to `RouteReport` access.

## [0.1.x]

- Initial data-oriented handlers, chain analyzer, and source generators.
