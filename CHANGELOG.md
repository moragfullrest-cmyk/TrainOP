# Changelog

All notable changes to TrainOP are documented in this file.

Format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

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
