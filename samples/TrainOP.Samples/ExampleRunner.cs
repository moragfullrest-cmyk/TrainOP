namespace TrainOP.Samples;

/// <summary>
/// Runs all registered sample examples in sequence.
/// </summary>
internal static class ExampleRunner
{
    /// <summary>
    /// Executes every sample example and writes a blank line between runs.
    /// </summary>
    public static void RunAll()
    {
        var examples = new IExample[]
        {
            new DataOrientedStationExample(),
            new RedSignalStopExample(),
            new ManifestMutationsExample(),
            new AsyncRouteExample(),
            new PartialWagonReturnExample(),
            new DataOrientedRedSignalExample(),
            new LongRouteServiceStationExample(),
            new NestedBranchingRouteExample(),
            new CodeVolumeComparisonExample(),
        };

        for (var i = 0; i < examples.Length; i++)
        {
            if (i > 0)
            {
                Console.WriteLine();
            }

            examples[i].Run();
        }
    }
}

/// <summary>
/// Contract for a runnable TrainOP sample with a display title.
/// </summary>
internal interface IExample
{
    /// <summary>
    /// Short label shown in the console header for this sample.
    /// </summary>
    string Title { get; }

    /// <summary>
    /// Builds the route, travels it, and prints the result.
    /// </summary>
    void Run();
}
