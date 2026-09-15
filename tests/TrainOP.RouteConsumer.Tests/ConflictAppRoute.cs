using TrainOP.RouteLib.Tests;

namespace TrainOP.RouteConsumer.Tests;

/// <summary>
/// Consumer-side result type so <c>Use</c> does not collide with RouteLib's
/// <c>Station(..., Func&lt;int, object&gt;)</c> overload.
/// </summary>
public sealed class BetaUseResult
{
    public int beta { get; set; }
}

/// <summary>
/// Consumer routes that share a CLR input shape with distinct wagon names.
/// </summary>
public static class ConflictAppRoute
{
    /// <summary>
    /// Library-owned chain using wagon name <c>alpha</c>.
    /// </summary>
    public static TrainRoute Alpha() => ConflictSeedModule.BuildAlpha();

    /// <summary>
    /// Factory-extension chain using wagon name <c>beta</c>.
    /// Return type differs from the library alpha <c>Use</c> to avoid CS0121 on generated extensions.
    /// </summary>
    public static TrainRoute Beta() => ConflictSeedModule.BuildBeta()
        .Station("Use", (int beta) => new BetaUseResult { beta = beta });
}
