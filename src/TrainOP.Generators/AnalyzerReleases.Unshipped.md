### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
TOP018 | TrainOP.Generators | Error | out parameter name duplicates a return member
TOP019 | TrainOP.Generators | Error | in or ref readonly parameter name is also a return member
TOP020 | TrainOP.Generators | Error | params wagon is not the last parameter
TOP021 | TrainOP.Generators | Error | synchronous Travel or TravelLight on a known chain that contains an async station
TOP022 | TrainOP.Generators | Error | wagon parameter default is not a constant
TOP023 | TrainOP.Generators | Error | ServiceStation wagon is ref or out

### Removed Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------

### Changed Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
TOP006 | TrainOP.Generators | Warning | Default ItemN elements allocate as new ItemN wagons after omitted inputs unload (no positional map onto inputs)
