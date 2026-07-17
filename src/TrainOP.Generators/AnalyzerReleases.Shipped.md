## Release 0.6.0

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
TOP001 | TrainOP.Generators | Error | Required wagon is not available in the route chain
TOP002 | TrainOP.Generators | Error | Wagon type conflict in route chain
TOP003 | TrainOP.Generators | Error | Removed wagon is required later in the route chain
TOP004 | TrainOP.Generators | Warning | Station returns CargoManifest
TOP005 | TrainOP.Generators | Error | Data-oriented handler is outside a TrainRoute chain (direct fluent or local from new)
TOP006 | TrainOP.Generators | Warning | Value tuple has no named elements (Item1, Item2, ...): wagon mapping becomes order-dependent
TOP007 | TrainOP.Generators | Error | Conflicting wagon names for the same handler type signature
TOP008 | TrainOP.Generators | Error | Cannot join route branches before a downstream station (unresolved arm, unknown terminals, or type conflict)
TOP009 | TrainOP.Generators | Error | Station handler is not a supported resolvable form in the current compilation
TOP010 | TrainOP.Generators | Error | Station returns runtime route signal (GreenSignal / RedSignal)
TOP011 | TrainOP.Generators | Info | External route factory has no exported schema
TOP012 | TrainOP.Generators | Error | Factory return paths have divergent terminal manifest state
TOP013 | TrainOP.Generators | Error | Factory return path has unknown terminal state
TOP014 | TrainOP.Generators | Warning | Value tuple mixes named and unnamed elements: wagon mapping can become ambiguous/fragile
