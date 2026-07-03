namespace TrainOP.Samples;

internal static class ExampleRunner
{
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

internal interface IExample
{
    string Title { get; }

    void Run();
}
