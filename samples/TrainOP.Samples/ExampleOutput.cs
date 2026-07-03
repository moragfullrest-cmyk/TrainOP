namespace TrainOP.Samples;

/// <summary>
/// Console helpers for formatting sample example output.
/// </summary>
internal static class ExampleOutput
{
    /// <summary>
    /// Prints a titled section header for an example run.
    /// </summary>
    public static void WriteHeader(string title)
    {
        Console.WriteLine($"--- {title} ---");
    }

    /// <summary>
    /// Prints destination status, visited stations, and final manifest or red-signal details.
    /// </summary>
    public static void WriteReport(RouteReport report)
    {
        Console.WriteLine($"Reached destination: {report.ReachedDestination}");
        Console.WriteLine($"Stations visited: {string.Join(" → ", report.Visits.Select(v => v.StationName))}");

        if (report.ReachedDestination)
        {
            WriteManifest("Final manifest", report.TerminalSignal.Manifest);
        }
        else if (report.TerminalSignal is RedSignal red)
        {
            Console.WriteLine($"Red signal: [{red.Issue.Code}] {red.Issue.Message} (at {red.Issue.StationName})");
            WriteManifest("Manifest at stop", red.Manifest);
        }
    }

    /// <summary>
    /// Prints all wagons in a manifest, sorted by wagon name.
    /// </summary>
    public static void WriteManifest(string label, CargoManifest manifest)
    {
        Console.WriteLine($"{label}:");
        foreach (var pair in manifest.InspectWagons().OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            Console.WriteLine($"  {pair.Key} = {pair.Value}");
        }
    }
}
