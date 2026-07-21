namespace TrainOP.Samples;

/// <summary>
/// Compile-only fixtures for <c>TrainRouteStationGenerator</c> debugging.
/// Each nested type is an isolated compilation unit of patterns — do not merge into one fluent chain.
/// In-chain mixed wagon names (paymentId vs orderId) — см. <c>TrainRouteStationGeneratorTests.MixedNameRoute</c>.
/// </summary>
/// <remarks>
/// <para>Not registered in <see cref="ExampleRunner"/>.</para>
/// <para><b>Breakpoints in the generator do not hit on Rebuild Samples</b> — Roslyn runs the analyzer out-of-process.</para>
/// <para><b>Option A (recommended):</b> Test Explorer → Debug test
/// <c>GeneratorDebugHarnessTests.DebugHarness_RunsGenerator_OnCoverageExampleSource</c>
/// with breakpoints in <c>src/TrainOP.Generators</c>.</para>
/// <para><b>Option B (Visual Studio):</b> startup project = <c>TrainOP.Generators</c>,
/// profile = <c>Debug Samples (generator F5)</c>, then F5 (not Build).</para>
/// <para>Disable <c>Just My Code</c> (Debug → Options) if breakpoints stay hollow.</para>
/// </remarks>
internal static class GeneratorDebugCoverageExample
{
    /// <summary>Touches every fixture so the compiler analyzes all call sites.</summary>
    public static void CompileTimeAnchor()
    {
        _ = RefAndVoidRoute.Build();
        _ = TupleReturnRoute.BuildNamed();
        _ = TupleReturnRoute.BuildItemN();
        _ = FactoryExtensionRoute.BuildParenthesized();
        _ = BranchRoute.BuildBareCreation(left: true);
        _ = BuiltinServiceStationRoute.Build();
        _ = PartialReturnRoute.Build();
        _ = SignalAndRecoveryRoute.Build();
        _ = PaymentRoute.Build();
        _ = OrderRoute.Build();
        _ = VoidAndObjectRoute.Build();
        _ = FactoryExtensionRoute.Build();
        _ = HandlerFormRoute.Build();
        _ = CancellationRoute.Build();
        _ = MultiSeedRoutes.BuildCounter();
        _ = MultiSeedRoutes.BuildPayment();
        _ = MultiPathFactory.Build(fast: true);
        _ = new GeneratorDebugInstanceHandlers().Build();
    }
}

/// <summary>ref / void / optional / manifest / green-red / service-station patterns.</summary>
internal static class RefAndVoidRoute
{
    public static TrainRoute Build() =>
        new TrainRoute()
            .Station("Seed", () => new { paymentId = "pay-ref", amount = 10m, traceId = "keep" })
            .Station("RefUpdate", (string paymentId, ref decimal amount) =>
                new { paymentId = paymentId + "-ref", amount = amount + 6m })
            .Station("VoidMutate", (ref string paymentId, ref decimal amount) =>
            {
                paymentId += "-void";
                amount += 1m;
            })
            .Station("OptionalWagon", (string paymentId, decimal? amount) =>
                new { paymentId, amount = amount ?? 0m })
            .Station("ManifestRead", (CargoManifest manifest, string paymentId, decimal amount) =>
                new { paymentId, amount, traceId = manifest.PullWagon<string>("traceId") })
            .Station("Validate", (string paymentId, decimal amount) =>
                amount > 0
                    ? RailwaySignals.Green(new { paymentId, amount })
                    : RailwaySignals.Red("INVALID_TOTAL", "amount must be positive"))
            .ServiceStation("Recovery", (ref string paymentId, ref decimal amount, RedSignal red) =>
            {
                if (red.Issue.Code == "INVALID_TOTAL")
                {
                    paymentId = "pay-recovered";
                    amount = 50m;
                    return RailwaySignals.Pass;
                }

                return RailwaySignals.Red("NOPE", "skip recovery");
            })
            .ServiceStation("RecoveryMethodGroup", RecoveryMethodGroup)
            .ServiceStation("TerminalLogger", red =>
            {
                var issue = red.Issue;
                _ = red.Manifest.PullWagon<string>("paymentId");
                return issue.Code == "STOCK_LIMIT"
                    ? RailwaySignals.Green(red.Manifest.LoadWagon("amount", 10m))
                    : RailwaySignals.Red(
                        red.Manifest,
                        new SignalIssue("UNRECOVERABLE", issue.Message, "TerminalLogger"));
            })
            .Station("Finalize", (string paymentId, decimal amount) =>
                new { paymentId, amount, status = "completed" });

    private static object RecoveryMethodGroup(ref string paymentId, ref decimal amount, RedSignal red)
    {
        if (red.Issue.Code == "INVALID_TOTAL")
        {
            paymentId += "-mg";
            amount = 25m;
            return RailwaySignals.Pass;
        }

        return RailwaySignals.Red("NOPE", "skip");
    }
}

/// <summary>Named vs default ItemN tuple returns (separate routes).</summary>
internal static class TupleReturnRoute
{
    public static TrainRoute BuildNamed() =>
        new TrainRoute()
            .Station("Seed", () => new { paymentId = "pay-tuple", amount = 100m })
            .Station("Discount", (string paymentId, decimal amount) =>
                new { paymentId = paymentId + "-named", amount = amount + 1m });

    public static TrainRoute BuildItemN() =>
        new TrainRoute()
            .Station("Seed", () => new { paymentId = "pay-itemn", amount = 100m })
            .Station("Discount", (string paymentId, decimal amount) =>
                (paymentId + "-itemn", amount * 0.95m));

    public static TrainRoute Build() => BuildNamed();
}

/// <summary>Partial wagon return preserving upstream wagons.</summary>
internal static class PartialReturnRoute
{
    public static TrainRoute Build() =>
        new TrainRoute()
            .Station("Seed", () => new { paymentId = "pay-partial", amount = 3m, traceId = "keep" })
            .Station("Partial", (string paymentId, decimal amount, string traceId) =>
                new { paymentId = paymentId + "-" + traceId, amount });
}

/// <summary>Green/red signal returns in one chain.</summary>
internal static class SignalAndRecoveryRoute
{
    public static TrainRoute Build() =>
        new TrainRoute()
            .Station("Seed", () => new { paymentId = "pay-signal", amount = 100m })
            .Station("Validate", (string paymentId, decimal amount) =>
                amount > 0
                    ? RailwaySignals.Green(new { paymentId, amount })
                    : RailwaySignals.Red("INVALID_TOTAL", "amount must be positive"));
}

/// <summary>Independent chain — avoids TOP007 with <see cref="OrderRoute"/>.</summary>
internal static class PaymentRoute
{
    public static TrainRoute Build() =>
        new TrainRoute()
            .Station("Seed", () => new { paymentId = "pay-1", amount = 100m })
            .Station("Discount", (string paymentId, decimal amount) =>
                new { paymentId, amount = amount * 0.9m });
}

/// <summary>Second independent chain — same CLR signature, different wagon names.</summary>
internal static class OrderRoute
{
    public static TrainRoute Build() =>
        new TrainRoute()
            .Station("Seed", () => new { orderId = "ord-1", total = 50m })
            .Station("Validate", (string orderId, decimal total) =>
                new { orderId, total = total + 1m });
}

/// <summary>void vs object return for the same wagon signature.</summary>
internal static class VoidAndObjectRoute
{
    public static TrainRoute Build() =>
        new TrainRoute()
            .Station("Seed", () => new { paymentId = "pay-void", amount = 10m })
            .Station("Anonymous", (string paymentId, decimal amount) =>
                new { paymentId, amount = amount * 0.9m })
            .Station("Void", (string paymentId, decimal amount) =>
            {
            });
}

/// <summary>Factory extension with cast/parenthesized receiver.</summary>
internal static class FactoryExtensionRoute
{
    public static TrainRoute Build() =>
        ((TrainRoute)(object)CreatePaymentSeed())
            .Station("Discount", (string paymentId, decimal amount) =>
                new { paymentId, amount = amount * 0.9m });

    public static TrainRoute BuildParenthesized() =>
        (CreateCounterSeed())
            .Station("Bump", (int counter) => new { counter = counter + 1 });

    private static TrainRoute CreatePaymentSeed() =>
        new TrainRoute().Station("Seed", () => new { paymentId = "pay-factory", amount = 100m });

    private static TrainRoute CreateCounterSeed() =>
        new TrainRoute().Station("Seed", () => new { counter = 0 });
}

/// <summary>Bare-creation branch arm. Ternary fork+join — см. generator tests / NestedBranchingRouteExample.</summary>
internal static class BranchRoute
{
    public static TrainRoute BuildBareCreation(bool left)
    {
        TrainRoute route;
        if (left)
        {
            route = new TrainRoute();
        }
        else
        {
            route = new TrainRoute();
        }

        return route.Station("Seed", () => new { value = 1 });
    }
}

/// <summary>Static method group, local function, anonymous method.</summary>
internal static class HandlerFormRoute
{
    public static TrainRoute Build()
    {
        object LocalSeed() => new { paymentId = "pay-local", amount = 42m };
        object LocalDiscount(string paymentId, decimal amount) =>
            new { paymentId, amount = amount * 0.85m };

        return new TrainRoute()
            .Station("SeedStatic", StaticSeed)
            .Station("DiscountStatic", (Func<string, decimal, object>)StaticDiscount)
            .Station("SeedLocal", LocalSeed)
            .Station("DiscountLocal", (Func<string, decimal, object>)LocalDiscount)
            .Station("DiscountAnonymous", delegate (string paymentId, decimal amount)
            {
                return new { paymentId = paymentId + "-anon", amount = amount - 1m };
            });
    }

    private static object StaticSeed() => new { paymentId = "pay-static", amount = 77m };

    private static object StaticDiscount(string paymentId, decimal amount) =>
        new { paymentId, amount = amount * 0.8m };
}

/// <summary>Sync/async cancellation-aware handlers.</summary>
internal static class CancellationRoute
{
    public static TrainRoute Build() =>
        new TrainRoute()
            .Station("Seed", () => new { counter = 1 })
            .Station("CancelableSync", (CancellationToken token) =>
            {
                token.ThrowIfCancellationRequested();
                return RailwaySignals.Pass;
            })
            .Station("FetchAsyncLambda", async (int counter, CancellationToken token) =>
            {
                await Task.Delay(1, token);
                return new { counter = counter + 1 };
            })
            .Station("FetchAsyncMethodGroup", FetchAsyncMethodGroup);

    private static async Task<object> FetchAsyncMethodGroup(int counter, CancellationToken token)
    {
        await Task.Delay(1, token);
        return new { counter = counter + 100 };
    }
}

/// <summary>Shared parameterless seed overload across routes.</summary>
internal static class MultiSeedRoutes
{
    public static TrainRoute BuildCounter() =>
        new TrainRoute()
            .Station("Seed", () => new { counter = 10 });

    public static TrainRoute BuildPayment() =>
        new TrainRoute()
            .Station("Seed", () => new { paymentId = "pay-seed", amount = 3m });
}

/// <summary>Factory with two return paths.</summary>
internal static class MultiPathFactory
{
    public static TrainRoute Build(bool fast) =>
        fast ? BuildFast() : BuildSlow();

    private static TrainRoute BuildFast() =>
        new TrainRoute()
            .Station("Seed", () => new { mode = "fast", value = 1 })
            .Station("Process", (string mode, int value) => new { mode, value = value * 2 });

    private static TrainRoute BuildSlow() =>
        new TrainRoute()
            .Station("Seed", () => new { mode = "slow", value = 10 })
            .Station("Process", (string mode, int value) => new { mode, value = value + 5 });
}

/// <summary>Instance method-group handlers.</summary>
internal sealed class GeneratorDebugInstanceHandlers
{
    public TrainRoute Build() =>
        new TrainRoute()
            .Station("Seed", Seed)
            .Station("Step", Step);

    private object Seed() => new { id = 1, label = "instance" };

    private object Step(int id, string label) => new { id = id + 1, label = label + "-done" };
}

/// <summary>Built-in <c>ServiceStation(RedSignal =&gt; …)</c> without data-oriented codegen.</summary>
internal static class BuiltinServiceStationRoute
{
    public static TrainRoute Build() =>
        new TrainRoute()
            .Station("Seed", () => new { orderId = "ORD-1", amount = 200m })
            .Station("Check", (string orderId, decimal amount) =>
                RailwaySignals.Red("FAIL", "stop"))
            .ServiceStation("Logger", red =>
            {
                var issue = red.Issue;
                return RailwaySignals.Red(
                    red.Manifest,
                    new SignalIssue("UNRECOVERABLE", issue.Message, "Logger"));
            });
}
