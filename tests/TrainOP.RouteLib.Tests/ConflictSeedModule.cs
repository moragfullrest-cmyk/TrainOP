namespace TrainOP.RouteLib.Tests;

/// <summary>
/// Factories used by cross-assembly chain-dispatch conflict tests.
/// </summary>
public static class ConflictSeedModule
{
    /// <summary>
    /// Full alpha chain lives in the library so the consumer does not emit a colliding
    /// <c>Station(..., Func&lt;object&gt;)</c> seed overload.
    /// </summary>
    public static TrainRoute BuildAlpha() => new TrainRoute()
        .Station("Seed", () => new { alpha = 1 })
        .Station("Use", (int alpha) => new { alpha });

    /// <summary>
    /// Builds a single-station seed that produces a <c>beta</c> wagon for consumer extension.
    /// </summary>
    public static TrainRoute BuildBeta() => new TrainRoute()
        .Station("Seed", () => new { beta = 2 });
}
