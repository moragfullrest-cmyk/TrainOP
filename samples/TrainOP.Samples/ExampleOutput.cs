using TrainOP;

namespace TrainOP.Samples;

internal static class ExampleOutput
{
    public static void WriteHeader(string title)
    {
        Console.WriteLine($"--- {title} ---");
    }

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

    public static void WriteManifest(string label, CargoManifest manifest)
    {
        Console.WriteLine($"{label}:");
        foreach (var pair in manifest.InspectWagons().OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            Console.WriteLine($"  {pair.Key} = {pair.Value}");
        }
    }
}
