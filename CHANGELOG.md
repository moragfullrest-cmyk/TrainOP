# Changelog

All notable changes to TrainOP are documented in this file.

Format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [0.17.0] - 2026-09-25

### Added

- **Handler `out` parameters:** create or overwrite a wagon the same way `ref` writes a local back, without pulling it first. A station that only has `out` parameters can seed the manifest. A new name on `ServiceStation` is **TOP015**. The same name as a return member is **TOP018**.
- **Handler `ref readonly` parameters:** read an existing wagon, keep it when the return omits the name, and do not write it back. The name must not appear in the return (**TOP019**).
- **Sample** `OutAndRefReadonlyExample`: seed via `out`, then `ref readonly` plus a new `out` wagon.

### Documentation

- **textbook / core-api / getting-started:** parameter and return effects are a modifier matrix (read, write, omit, composition).
- **nuget / getting-started / README / textbook / release-readiness:** version snippets bumped to 0.17.0.

### Changed

- **Terminal sets:** unknown-return sets store an empty wagon list, so callers read `TerminalSet.Wagons` directly.

## [0.16.0] - 2026-09-22

### Changed

- **BuildChains Parts IR:** route fragments are first-class parts (`CreationSeed`, `FactoryCall`, `LocalBinding`, `StationLink`, `JoinArm`, `JoinSeed`, `ExtensionTail`) glued by `ChainConstructor` with per-edge validation; Assembler / keys / factory dispatch no longer switch on `RouteChainAnchorKind` on the hot path.
- **Walker split:** `RouteChainWalker` is a thin BuildChains facade; origin/SL window lives in `RouteOriginWindow`, backward root walk in `RouteChainRootResolver`, peel/detect remain `RouteChainPeel` / `RouteAnchorDetector`.

### Added

- **Parts connectors / materializers:** linear, join, and extension connectors plus materializers and adapters under `TrainOP.Generators/Parts/`.
- **Statement-local and misc C# anchors:** broader local origin forms (fluent RHS, factories, join-assign C-10/C-11) with matching generator tests.

### Documentation

- **plan-route-parts-constructor** (D0–Z0 + post-Z0 nesting extract O1–O4), statement-local / anchors plans, architecture-internals BuildChains Materialize→Construct→Validate.
- **nuget / getting-started / README / textbook / release-readiness:** version snippets bumped to 0.16.0.

## [0.15.0] - 2026-09-16

### Added

- **TravelLight / TravelLightAsync:** opt-in travel without recording `StationVisit` journal (`Visits` is empty); terminal signal and manifest unchanged vs `Travel` / `TravelAsync`.

### Documentation

- **plan-performance:** P4 marked done; remaining course P6 → P7.
- **core-api:** TravelLight API documented.
- **nuget / getting-started / README / textbook / release-readiness:** version snippets bumped to 0.15.0.

## [0.14.0] - 2026-09-16

### Breaking

- **Single NuGet package:** `TrainOP` now ships runtime + source generator / analyzer in one `.nupkg`. Remove any `PackageReference` to `TrainOP.Generators`; that package is no longer published (`IsPackable=false`). ProjectReference consumers still reference both projects explicitly.
- **Default ItemN tuple returns:** unnamed value-tuple elements no longer map onto handler input wagon keys by position. After omitted non-`ref` inputs unload, elements allocate as new `ItemN` wagons (`max` existing `Item*` + 1). Access via `Item1`/`Item2`/…; sequential hops that spend prior `Item*` reuse the same numbers. Explicit `(Item1: …)` and inferred names are unchanged. ServiceStation still cannot add wagons (**TOP015**).

### Changed

- **CI / pack:** SDK 8/9 smoke builds only `TrainOP.csproj` (pulls Generators); .NET 10 packs a single `TrainOP` package.
- **Generator pipeline (IR-first):** stages 1a–7 populate a data-oriented `GenerationModel`; `AddSource` runs only at the end via `GenerationEmit`. Orchestrator slimmed to discovery + stage wiring; chain build, join, signature grouping, branch plans, terminals, and schema descriptors live under `Pipeline/`.

### Added

- **Pipeline stage modules:** `BuildChainsStage`, `JoinChainsStage`, `SignatureGroupingStage`, `AttachChainContextStage`, `BranchPlanStage`, `SchemaDescriptorsStage`, `ChainDispatchPolicy`, `TerminalSet`, and related IR holders.
- **`SchemaDescriptor` / `HandlerSignatureGrouping`:** explicit DTOs for schema export and signature clustering.

### Documentation

- **nuget / getting-started / README / textbook / cross-assembly / release-readiness:** installation and pack instructions updated for the unified package; version snippets bumped to 0.14.0.
- **architecture-internals:** documented IR-first stages 1a–7 → Emit-last and the `GenerationModel` contract.
- **plan-performance:** единый roadmap hot path — P0–P4a done; P5 снято; курс вперёд P4 TravelLight → P6 freeze → P7 slim dispatch (бывший `plan-acceleration` влит).

### Fixed

- **Named vs default-ItemN tuple call sites:** handlers that share one CLR `Func<…, (T1, T2, …)>` but differ in tuple element naming (named/inferred vs default ItemN) now use caller chain-dispatch with per-site `ReturnMembers` / `AllocateDefaultItemN`, and skip typed merge baked from the canonical binding so named returns keep wagon keys instead of unloading them.
- **Runtime named-tuple merge:** `StationMerge` / `WagonStationReturn` resolve boxed value-tuple elements by ordinal against generator `returnMemberNames` (CLR erases element names), so chain-dispatch named returns like `(paymentId:, amount:)` merge by name instead of unloading inputs.
- **Parameterless seed overloads:** multiple anonymous / `object` seed handlers that share `Func<object>` keep one canonical Station overload with consolidated `ReturnMembers`; distinct object return shapes no longer force chain-dispatch core overloads.

## [0.13.0] - 2026-09-15

### Breaking

- **Travel-only launch:** public `DispatchTrain()` and `Train` removed. Run with `route.Travel()` / `TravelAsync()`; each call snapshots the plan and starts with an empty manifest.
- **Signal without cargo:** `Signal` / `GreenSignal` / `RedSignal` no longer carry `Manifest`. Terminal wagons live on `RouteReport.Manifest`. Internal factories are `RailwaySignals.Green()` and `RailwaySignals.Red(issue[, priorIssues])`.
- **ServiceStation escape hatch:** low-level handlers take `CargoManifest` separately from `RedSignal` (`(red, manifest) => …`); `red.Manifest` is gone. Data-oriented ServiceStation may also declare a `CargoManifest` framework parameter.

### Fixed

- **Factory-extension chain-dispatch:** exported schema now carries `CallerChainKey` and `StationCount` so consumer stations after a public factory resolve the same caller-dispatch key as the factory’s internal `new TrainRoute()`; `CallerChainKeyBuilder` / `FactoryDispatchMetadata` no longer invent ordinal `0` for older schemas without a key.
- **ItemN simulator parity / runtime harden:** `ChainGraphSimulator` and `TrainRouteRuntime` updates landed in this release for default-ItemN tuple binding consistency and related runtime hardening.
- **Async ServiceStation escape hatch:** builtin detector now treats `(RedSignal, CancellationToken)` / `(RedSignal, CargoManifest, …)` as the runtime overload; chain walk skips that overlay without breaking factory-path simulation, and `RailwaySignals.Red` / `White` keep a known terminal wagon set, so async recovery handlers no longer raise TOP009 or TOP013.

### Added

- **TOP014:** error when a method has more than one `new TrainRoute()` on the same source line (caller-mode identity); tracked in `AnalyzerReleases` and shipped under Release 0.13.0.
- **TOP015 / TOP016 / TOP017:** analyzer errors when a data-oriented `ServiceStation` return would change manifest composition (add a wagon, omit a non-`ref` input, or return `CargoManifest`).
- **Red signal issue chains:** `RedSignal.Issues`, `RouteReport.FailureIssues`, and `RailwaySignals.Red(code, message, priorIssues)` for preserving nested sub-route failures when a parent station stops the route.
- **ServiceStation issue injection:** data-oriented handlers may take `SignalIssue` (last/immediate stop) and/or `IReadOnlyList<SignalIssue>` (full chain) without requiring a `RedSignal` parameter; codegen wires `red.Issue` / `red.Issues`.
- **Sample:** `FrameworkParametersExample` covers every Station/ServiceStation framework parameter (`CargoManifest`, `CancellationToken`, `SignalIssue`, `IReadOnlyList<SignalIssue>`, `RedSignal`).

### Changed

- **Lunar-white signal:** `RailwaySignals.Pass` / `GreenPass` renamed to `RailwaySignals.White` / `WhitePass` (unchanged-manifest continue), aligning the DSL with railway signal colors (green / white / red).
- **Positional ServiceStation travel:** service stations are hops in the same route plan as ordinary stations. Travel enters an ordinary hop only after green and a service hop only after red; otherwise the hop is skipped. Mid-route red no longer triggers a global recovery list.
- **ServiceStation overlay:** data-oriented recovery uses the same input/return contract as `Station` (by-value wagons, `Green` payload, async). Merge updates existing manifest keys only — it does not add or remove wagons, because later stations already require that composition and recovery may not run. Attempts to change composition are compile-time errors (TOP015–TOP017).
- **CI:** SDK 8 / 9 jobs build only `TrainOP` and `TrainOP.Generators` libraries; full solution restore/build/test and pack remain on .NET 10.

### Documentation

- **core-api / getting-started / architecture-internals / textbook:** documented `ref` wagon rules, CS1988 (`async` cannot capture by-ref parameters), ServiceStation overlay merge (composition preserved; TOP015–TOP017), positional ServiceStation travel, and TOP014.
- **nuget / getting-started / README:** package version snippets bumped to 0.13.0; Travel-only examples.
- **release-readiness:** refreshed for 0.13.0 after CI pack and analyzer shipping.

## [0.12.1] - 2026-09-14

### Changed

- **CodegenWriter:** migrated all source emit paths to indent-stack API; removed `Builder` escape hatch and string-based `StatementIndent` from emit contexts.
- **Public API discoverability:** marked generator-oriented runtime helpers and cross-assembly schema attributes with `[EditorBrowsable(Never)]` (`StationMerge`, `WagonStationReturn`, `RouteSchemaForAttribute`, `RouteSchemaWagonAttribute`) — same policy as `RegisterStation` and `CallerChainKeyFormat`.
- **CI:** Release `dotnet pack` smoke for `TrainOP` and `TrainOP.Generators` on .NET 10 workflow job.

### Documentation

- **Release readiness:** refreshed weighted Preview score and API-surface checklist after EditorBrowsable and CI pack.
- **Cross-assembly / core-api:** clarify that schema attributes are emitted by the generator, not hand-authored consumer API.

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
