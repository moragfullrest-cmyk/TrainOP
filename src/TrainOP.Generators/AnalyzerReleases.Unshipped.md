### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
TOP002 | TrainOP.Generators | Error | Required wagon is not available in the route chain
TOP003 | TrainOP.Generators | Error | Wagon type conflict in route chain
TOP004 | TrainOP.Generators | Error | Removed wagon is required later in the route chain
TOP005 | TrainOP.Generators | Warning | Station returns CargoManifest
TOP006 | TrainOP.Generators | Warning | Station returns an unnamed tuple
TOP007 | TrainOP.Generators | Error | Data-oriented handler is outside a TrainRoute chain
TOP008 | TrainOP.Generators | Info | Seed wagon is never consumed
TOP009 | TrainOP.Generators | Error | Conflicting wagon names for the same handler type signature
