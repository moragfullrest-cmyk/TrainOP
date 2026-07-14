using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Xunit;

namespace TrainOP.Generators.Tests
{
    /// <summary>
    /// Tests source generation for data-oriented Station and ServiceStation extension methods.
    /// </summary>
    public sealed class TrainRouteStationGeneratorTests
    {
        /// <summary>
        /// Verifies that the generator emits a Station extension for a handler with ref parameters.
        /// </summary>
        [Fact]
        public void Generator_EmitsStationExtension_ForRefHandler()
        {
            const string source = @"
using TrainOP;

public static class RefRoute
{
    public static TrainRoute Build() => new TrainRoute()
        .Station(""Seed"", manifest => manifest.LoadWagon(""paymentId"", ""pay"").LoadWagon(""amount"", 4m))
        .Station(""UpdateRef"", (string paymentId, ref decimal amount) =>
            new { paymentId = paymentId + ""-ref"", amount = amount + 6m });
}";

            var generated = RunGenerators(source);

            Assert.Contains("private static readonly bool[] RefFlags_", generated);
            Assert.Contains("ref global::System.Decimal p1", generated);
            Assert.Contains("refLocalValues", generated);
            Assert.Contains("StationMerge.ToSignal(manifest, stationReturn, stationName", generated);
            Assert.Contains("ReturnMembers_", generated);
        }

        /// <summary>
        /// Verifies that the generator emits a void delegate for handlers without a return value.
        /// </summary>
        [Fact]
        public void Generator_EmitsVoidDelegate_ForVoidHandler()
        {
            const string source = @"
using TrainOP;

public static class VoidRoute
{
    public static TrainRoute Build() => new TrainRoute()
        .Station(""Seed"", () => new { paymentId = ""pay"", amount = 4m })
        .Station(""MutateRef"", (ref string paymentId, ref decimal amount) =>
        {
            paymentId = paymentId + ""-void"";
            amount = amount + 1m;
        });
}";

            var generated = RunGenerators(source);

            Assert.Contains("public delegate void TrainStationHandler_", generated);
            Assert.Contains("global::System.Object stationReturn = default;", generated);
            Assert.Contains("StationMerge.ToSignal(manifest, stationReturn, stationName", generated);
            Assert.Contains("RefFlags_", generated);
            Assert.Contains("refLocalValues", generated);
        }

        /// <summary>
        /// Verifies that the generator emits HasWagon checks for optional wagon parameters.
        /// </summary>
        [Fact]
        public void Generator_EmitsHasWagon_ForOptionalWagon()
        {
            const string source = @"
using TrainOP;

public static class OptionalRoute
{
    public static TrainRoute Build() => new TrainRoute()
        .Station(""Seed"", () => new { paymentId = ""pay"" })
        .Station(""WithOptional"", (string paymentId, decimal? amount) =>
            new { paymentId, amount = amount ?? 0m });
}";

            var generated = RunGenerators(source);

            Assert.Contains("manifest.HasWagon(\"amount\")", generated);
            Assert.Contains("default(", generated);
            Assert.Contains("global::System.Decimal?", generated);
        }

        /// <summary>
        /// Verifies that the generator emits sync cancellation-aware Station adapters.
        /// </summary>
        [Fact]
        public void Generator_EmitsSyncCancellationStationAdapter_ForCancellationTokenHandler()
        {
            const string source = @"
using System;
using System.Threading;
using System.Threading.Tasks;
using TrainOP;

public static class SyncCancellationRoute
{
    public static TrainRoute Build() => new TrainRoute()
        .Station(""Seed"", () => new { })
        .Station(""CancelableSync"", (CancellationToken token) =>
        {
            token.ThrowIfCancellationRequested();
            return RailwaySignals.Pass;
        })
        .Station(""Boom"", (CancellationToken _) =>
        {
            throw new InvalidOperationException(""sync exploded"");
        })
        .Station(""BoomAsync"", async (CancellationToken token) =>
        {
            await Task.Delay(1, token);
            throw new InvalidOperationException(""async exploded"");
        });
}";

            var generated = RunGenerators(source);

            Assert.Contains("public static TrainRoute Station(this TrainRoute route, string stationName, Action<CancellationToken> handler)", generated);
            Assert.Contains("public static TrainRoute Station(this TrainRoute route, string stationName, TrainStationHandler_", generated);
            Assert.Contains("public delegate System.Threading.Tasks.Task TrainStationHandler_", generated);
            Assert.DoesNotContain("StationAsync", generated);
            Assert.Contains("OverloadResolutionPriority", generated);
            Assert.Contains("Func<CancellationToken, global::TrainOP.Signal>", generated);
            Assert.Contains("(manifest, token) =>", generated);
            Assert.Contains("handler(token)", generated);
        }

        /// <summary>
        /// Verifies that the generator emits adapters for static method-group handlers.
        /// </summary>
        [Fact]
        public void Generator_EmitsStationExtension_ForStaticMethodGroup()
        {
            const string source = @"
using TrainOP;

public static class MethodGroupRoute
{
    public static TrainRoute Build() => new TrainRoute()
        .Station(""Seed"", Seed)
        .Station(""Discount"", Discount);

    private static object Seed() => new { paymentId = ""pay-1"", amount = 100m };

    private static object Discount(string paymentId, decimal amount) =>
        new { paymentId, amount = amount * 0.9m };
}";

            var generated = RunGenerators(source);

            Assert.Contains("public static TrainRoute Station(this TrainRoute route", generated);
            Assert.Contains("Func<global::System.Object>", generated);
            Assert.Contains("Func<global::System.String, global::System.Decimal, global::System.Object>", generated);
            Assert.Contains("manifest.PullWagon<global::System.String>(\"paymentId\")", generated);
            Assert.Contains("manifest.PullWagon<global::System.Decimal>(\"amount\")", generated);
        }

        /// <summary>
        /// Verifies that the generator emits adapters for instance method-group handlers.
        /// </summary>
        [Fact]
        public void Generator_EmitsStationExtension_ForInstanceMethodGroup()
        {
            const string source = @"
using TrainOP;

public sealed class InstanceMethodRoute
{
    public TrainRoute Build() => new TrainRoute()
        .Station(""Seed"", Seed)
        .Station(""Discount"", Discount);

    private object Seed() => new { paymentId = ""pay-1"", amount = 100m };

    private object Discount(string paymentId, decimal amount) =>
        new { paymentId, amount = amount * 0.9m };
}";

            var generated = RunGenerators(source);

            Assert.Contains("public static TrainRoute Station(this TrainRoute route", generated);
            Assert.Contains("Func<global::System.String, global::System.Decimal, global::System.Object>", generated);
            Assert.Contains("manifest.PullWagon<global::System.String>(\"paymentId\")", generated);
        }

        /// <summary>
        /// Verifies that the generator emits adapters for local-function handlers.
        /// </summary>
        [Fact]
        public void Generator_EmitsStationExtension_ForLocalFunction()
        {
            const string source = @"
using TrainOP;

public static class LocalFunctionRoute
{
    public static TrainRoute Build()
    {
        object Seed() => new { paymentId = ""pay-1"", amount = 100m };
        object Discount(string paymentId, decimal amount) =>
            new { paymentId, amount = amount * 0.9m };

        return new TrainRoute()
            .Station(""Seed"", Seed)
            .Station(""Discount"", Discount);
    }
}";

            var generated = RunGenerators(source);

            Assert.Contains("public static TrainRoute Station(this TrainRoute route", generated);
            Assert.Contains("Func<global::System.String, global::System.Decimal, global::System.Object>", generated);
            Assert.Contains("manifest.PullWagon<global::System.String>(\"paymentId\")", generated);
        }

        /// <summary>
        /// Verifies that the generator emits adapters for anonymous-method handlers.
        /// </summary>
        [Fact]
        public void Generator_EmitsStationExtension_ForAnonymousMethod()
        {
            const string source = @"
using TrainOP;

public static class AnonymousMethodRoute
{
    public static TrainRoute Build() => new TrainRoute()
        .Station(""Seed"", () => new { paymentId = ""pay-1"", amount = 100m })
        .Station(""Discount"", delegate(string paymentId, decimal amount)
        {
            return new { paymentId, amount = amount * 0.9m };
        });
}";

            var generated = RunGenerators(source);

            Assert.Contains("public static TrainRoute Station(this TrainRoute route", generated);
            Assert.Contains("Func<global::System.String, global::System.Decimal, global::System.Object>", generated);
            Assert.Contains("manifest.PullWagon<global::System.String>(\"paymentId\")", generated);
        }

        /// <summary>
        /// Verifies that async method-group handlers emit Task-aware Station adapters.
        /// </summary>
        [Fact]
        public void Generator_EmitsAsyncStationExtension_ForAsyncMethodGroup()
        {
            const string source = @"
using System.Threading;
using System.Threading.Tasks;
using TrainOP;

public static class AsyncMethodGroupRoute
{
    public static TrainRoute Build() => new TrainRoute()
        .Station(""Seed"", () => new { value = 1 })
        .Station(""Fetch"", Fetch);

    private static async Task<object> Fetch(int value, CancellationToken token)
    {
        await Task.Delay(1, token);
        return new { value = value + 1 };
    }
}";

            var generated = RunGenerators(source);

            Assert.Contains("public static TrainRoute Station(this TrainRoute route", generated);
            Assert.Contains("System.Threading.Tasks.Task", generated);
        }

        /// <summary>
        /// Verifies that ServiceStation method-group handlers with ref wagons emit adapters.
        /// </summary>
        [Fact]
        public void Generator_EmitsServiceStationExtension_ForMethodGroup()
        {
            const string source = @"
using TrainOP;

public static class ServiceMethodGroupRoute
{
    public static TrainRoute Build() => new TrainRoute()
        .Station(""Seed"", () => new { value = 0 })
        .Station(""Validate"", (int value) =>
            value > 0 ? RailwaySignals.Green(new { value }) : RailwaySignals.Red(""ERR"", ""bad""))
        .ServiceStation(""Recovery"", Recover)
        .Station(""After"", (int value) => new { value = value + 1 });

    private static object Recover(ref int value, RedSignal red)
    {
        if (red.Issue.Code == ""ERR"")
        {
            value = 1;
            return RailwaySignals.Pass;
        }

        return RailwaySignals.Red(""NOPE"", ""skip"");
    }
}";

            var generated = RunGenerators(source);

            Assert.Contains("public static TrainRoute ServiceStation(this TrainRoute route", generated);
            Assert.Contains("TrainServiceStationHandler_", generated);
        }

        /// <summary>
        /// Verifies that a Func variable is not treated as a discoverable station handler.
        /// </summary>
        [Fact]
        public void Generator_DoesNotEmit_ForFuncVariableHandler()
        {
            const string source = @"
using System;
using TrainOP;

public static class FuncVariableRoute
{
    public static TrainRoute Build()
    {
        Func<string, decimal, object> discount = (paymentId, amount) =>
            new { paymentId, amount = amount * 0.9m };

        return new TrainRoute()
            .Station(""Seed"", () => new { paymentId = ""pay-1"", amount = 100m })
            .Station(""Discount"", discount);
    }
}";

            var generated = RunGenerators(source);

            Assert.DoesNotContain("PullWagon(\"paymentId\"", generated);
            Assert.DoesNotContain("PullWagon(\"amount\"", generated);
        }

        /// <summary>
        /// Verifies that the generator emits a ServiceStation extension for a data-oriented recovery handler.
        /// </summary>
        [Fact]
        public void Generator_EmitsServiceStationExtension_ForDataHandler()
        {
            const string source = @"
using TrainOP;

public static class RecoveryRoute
{
    public static TrainRoute Build() => new TrainRoute()
        .Station(""Seed"", () => new { value = 0 })
        .Station(""Validate"", (int value) =>
            value > 0 ? RailwaySignals.Green(new { value }) : RailwaySignals.Red(""ERR"", ""bad""))
        .ServiceStation(""Recovery"", (ref int value, RedSignal red) =>
        {
            if (red.Issue.Code == ""ERR"")
            {
                value = 1;
                return RailwaySignals.Pass;
            }

            return RailwaySignals.Red(""NOPE"", ""skip"");
        })
        .Station(""After"", (int value) => new { value = value + 1 });
}";

            var generated = RunGenerators(source);

            Assert.Contains("public static TrainRoute ServiceStation(this TrainRoute route", generated);
            Assert.Contains("TrainServiceStationHandler_", generated);
            Assert.Contains("ref global::System.Int32 p0", generated);
            Assert.Contains("RedSignal pRed", generated);
            Assert.Contains("red.Manifest", generated);
            Assert.DoesNotContain("red.Issue", generated);
            Assert.Contains("StationMerge.ToServiceSignal", generated);
        }

        /// <summary>
        /// Verifies that the generator does not emit extensions for a built-in red-signal ServiceStation handler.
        /// </summary>
        [Fact]
        public void Generator_DoesNotEmit_ForBuiltinRedSignalServiceStation()
        {
            const string source = @"
using System;
using TrainOP;

public static class LongRoute
{
    public static TrainRoute Build() => new TrainRoute()
        .Station(""Seed"", () => new { orderId = ""ORD-1"", amount = 200m, units = 12 })
        .Station(""CheckStock"", (string orderId, decimal amount, int units) =>
            units <= 10
                ? RailwaySignals.Green(new { orderId, amount, units })
                : RailwaySignals.Red(""STOCK_LIMIT"", ""too many""))
        .ServiceStation(""TerminalLogger"", red =>
        {
            var issue = red.Issue;
            var orderId = red.Manifest.PullWagon<string>(""orderId"");
            return RailwaySignals.Green(red.Manifest.LoadWagon(""units"", 10));
        });
}";

            var generated = RunGenerators(source);

            Assert.DoesNotContain("new string[] { \"red\" }", generated);
            Assert.DoesNotContain("PullWagon<>(\"red\")", generated);
            Assert.DoesNotContain("TrainServiceStationHandler_", generated);
        }

        /// <summary>
        /// Verifies that the generator does not emit extensions for a SignalIssue-only service station handler.
        /// </summary>
        [Fact]
        public void Generator_DoesNotEmit_ForSignalIssueOnlyServiceStation()
        {
            const string source = @"
using TrainOP;

public static class IssueRoute
{
    public static TrainRoute Build() => new TrainRoute()
        .Station(""Seed"", () => new { value = 0 })
        .Station(""Validate"", (int value) => RailwaySignals.Red(""ERR"", ""bad""))
        .ServiceStation(""Recovery"", (SignalIssue issue) =>
            RailwaySignals.Red(""CANNOT_RECOVER"", issue.Code));
}";

            var generated = RunGenerators(source);

            Assert.DoesNotContain("TrainServiceStationHandler_", generated);
        }

        /// <summary>
        /// Verifies that the generator emits a Station extension for a data-oriented handler with tuple return.
        /// </summary>
        [Fact]
        public void Generator_EmitsStationExtension_ForDataHandler()
        {
            const string source = @"
using TrainOP;

public static class PaymentRoute
{
    public static TrainRoute Build() => new TrainRoute()
        .Station(""Seed"", () => new { paymentId = ""pay-1"", amount = 100m })
        .Station(""Discount"", (string paymentId, decimal amount) =>
            (paymentId, amount: amount * 0.9m));
}";

            var generated = RunGenerators(source);

            Assert.Contains("public static class TrainRouteStationExtensions", generated);
            Assert.Contains("Func<global::System.String, global::System.Decimal, (", generated);
            Assert.Contains("stationReturn.", generated);
            Assert.Contains("public static TrainRoute Station(this TrainRoute route", generated);
            Assert.Contains("manifest.PullWagon<global::System.String>(\"paymentId\")", generated);
            Assert.Contains("manifest.PullWagon<global::System.Decimal>(\"amount\")", generated);
            Assert.Contains("StationMerge.ToSignal", generated);
            Assert.Contains("ReturnMembers_", generated);
        }

        /// <summary>
        /// Verifies that the generator emits ReturnMembers with ItemN names for an unnamed tuple return.
        /// </summary>
        [Fact]
        public void Generator_EmitsItemNames_ForUnnamedTupleReturn()
        {
            const string source = @"
using TrainOP;

public static class PaymentRoute
{
    public static TrainRoute Build() => new TrainRoute()
        .Station(""Seed"", () => new { paymentId = ""pay-1"", amount = 100m })
        .Station(""Discount"", (string paymentId, decimal amount) =>
            (paymentId, amount * 0.9m));
}";

            var generated = RunGenerators(source);

            Assert.Contains("\"Item1\"", generated);
            Assert.Contains("\"Item2\"", generated);
            Assert.DoesNotContain("TupleOrdinals_", generated);
        }

        /// <summary>
        /// Verifies that shared delegate metadata is emitted once when seed handlers share a signature.
        /// </summary>
        [Fact]
        public void Generator_DoesNotDuplicateMetadata_WhenSeedHandlersShareDelegateSignature()
        {
            const string source = @"
using TrainOP;

public static class MixedSeedRoute
{
    public static TrainRoute Payment() => new TrainRoute()
        .Station(""Seed"", () => new { paymentId = ""pay-1"", amount = 100m });

    public static TrainRoute Counter() => new TrainRoute()
        .Station(""Seed"", () => new { counter = 10 });
}";

            var generated = RunGenerators(source);
            Assert.Contains("public static TrainRoute Station(this TrainRoute route, string stationName, Func<global::System.Object>", generated);
            Assert.Equal(1, CountOccurrences("private static readonly string[] WagonNames_", generated));
            Assert.Contains("ReturnMembers_", generated);
            Assert.Contains("\"paymentId\"", generated);
            Assert.Contains("\"counter\"", generated);
        }

        /// <summary>
        /// Verifies that legacy handlers in separate chains get interceptors and avoid TOP007.
        /// </summary>
        [Fact]
        public void Generator_EmitsInterceptors_ForLegacyStationsInSeparateChains()
        {
            const string source = @"
using TrainOP;

public static class ConflictingNameRoute
{
    public static TrainRoute Payment() => new TrainRoute()
        .Station(""Seed"", () => new { paymentId = ""pay-1"", amount = 100m })
        .Station(""Discount"", (string paymentId, decimal amount) =>
            new { paymentId, amount = amount * 0.9m });

    public static TrainRoute Order() => new TrainRoute()
        .Station(""Seed"", () => new { orderId = ""ord-1"", total = 50m })
        .Station(""Validate"", (string orderId, decimal total) =>
            new { orderId, total = total + 1m });
}";

            var runResult = RunGeneratorDriver(source);
            var diagnostics = runResult.Results
                .SelectMany(result => result.Diagnostics)
                .ToList();
            var generated = RunGenerators(source);

            Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "TOP007");
            Assert.Contains("InterceptsLocation", generated);
            Assert.Contains("StationCore_", generated);
            Assert.Contains("ResolveChainBinding_", generated);
            Assert.Contains("ConflictingNameRoute.Payment", generated);
            Assert.Contains("ConflictingNameRoute.Order", generated);
            Assert.Contains("manifest.PullWagon<global::System.String>(binding.InputNames[0])", generated);
        }

        /// <summary>
        /// Verifies that legacy handlers in one chain can use different wagon names via chain dispatch.
        /// </summary>
        [Fact]
        public void Generator_EmitsChainDispatch_ForLegacyStationsWithDifferentWagonNamesInOneChain()
        {
            const string source = @"
using TrainOP;

public static class MixedNameRoute
{
    public static TrainRoute Build() => new TrainRoute()
        .Station(""Seed"", () => new { paymentId = ""pay-1"", amount = 100m })
        .Station(""Discount"", (string paymentId, decimal amount) =>
            new { paymentId, amount = amount * 0.9m })
        .Station(""Validate"", (string orderId, decimal total) =>
            new { paymentId = orderId, amount = total + 1m });
}";

            var generated = RunGenerators(source);

            Assert.Equal(2, CountStationOverloads(generated));
            Assert.Contains("StationCore_", generated);
            Assert.Contains("\"paymentId\"", generated);
            Assert.Contains("\"orderId\"", generated);
            var diagnostics = RunGeneratorDriver(source).Results
                .SelectMany(result => result.Diagnostics)
                .ToList();

            Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "TOP007");
        }

        /// <summary>
        /// Verifies that a single Station overload is emitted when legacy handlers share a type signature but differ in parameter names.
        /// </summary>
        [Fact]
        public void Generator_EmitsSingleStationOverload_WhenHandlersShareTypeSignatureButDifferInParameterNames()
        {
            const string source = @"
using TrainOP;

public static class MixedNameRoute
{
    public static TrainRoute Build() => new TrainRoute()
        .Station(""Seed"", () => new { paymentId = ""pay-1"", amount = 100m })
        .Station(""Discount"", (string paymentId, decimal amount) =>
            new { paymentId, amount = amount * 0.9m })
        .Station(""Validate"", (string orderId, decimal total) =>
            new { paymentId = orderId, amount = total + 1m });
}";

            var generated = RunGenerators(source);
            Assert.Equal(2, CountStationOverloads(generated));
            Assert.Contains("Func<global::System.String, global::System.Decimal, global::System.Object>", generated);
            Assert.Contains("ResolveChainBinding_", generated);
            Assert.Contains("\"paymentId\"", generated);
            Assert.Contains("\"orderId\"", generated);
        }

        /// <summary>
        /// Verifies that TOP007 is reported when non-chain handlers share a type signature but use different manifest keys.
        /// </summary>
        [Fact]
        public void Generator_ReportsConflictingWagonNames_WhenHandlersShareTypeSignatureButUseDifferentManifestKeysOutsideChains()
        {
            const string source = @"
using TrainOP;

public static class ConflictingNameRoute
{
    public static TrainRoute Build() => new TrainRoute()
        .Station(""Seed"", () => new { paymentId = ""pay-1"", amount = 100m });

    public static TrainRoute AttachDiscount(TrainRoute route) =>
        route.Station(""Discount"", (string paymentId, decimal amount) =>
            new { paymentId, amount = amount * 0.9m });

    public static TrainRoute AttachValidate(TrainRoute route) =>
        route.Station(""Validate"", (string orderId, decimal total) =>
            new { orderId, total = total + 1m });
}";

            var runResult = RunGeneratorDriver(source);
            var diagnostics = runResult.Results
                .SelectMany(result => result.Diagnostics)
                .ToList();

            Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "TOP007");
        }

        /// <summary>
        /// Verifies that void and object-return handlers with the same wagon signature get separate Station overloads.
        /// </summary>
        [Fact]
        public void Generator_EmitsSeparateStationOverloads_WhenHandlersShareSignatureButDifferInVoidReturn()
        {
            const string source = @"
using TrainOP;

public static class VoidAndObjectReturnRoute
{
    public static TrainRoute Build() => new TrainRoute()
        .Station(""Seed"", () => new { paymentId = ""pay-1"", amount = 100m })
        .Station(""Anonymous"", (string paymentId, decimal amount) =>
            new { paymentId, amount = amount * 0.9m })
        .Station(""Void"", (string paymentId, decimal amount) =>
        {
        });
}";

            var generated = RunGenerators(source);

            Assert.Contains("Func<global::System.String, global::System.Decimal, global::System.Object>", generated);
            Assert.Contains("Action<global::System.String, global::System.Decimal>", generated);
            Assert.Equal(3, CountStationOverloads(generated));
        }

        /// <summary>
        /// Verifies that a single Station overload is emitted when handlers share a signature but differ in return shape.
        /// </summary>
        [Fact]
        public void Generator_EmitsSingleStationOverload_WhenHandlersShareSignatureButDifferInReturnShape()
        {
            const string source = @"
using TrainOP;

public static class MixedReturnRoute
{
    public static TrainRoute Build() => new TrainRoute()
        .Station(""Seed"", () => new { paymentId = ""pay-1"", amount = 100m })
        .Station(""Anonymous"", (string paymentId, decimal amount) =>
            new { paymentId, amount = amount * 0.9m })
        .Station(""Tuple"", (string paymentId, decimal amount) =>
            (paymentId, amount: amount + 1m));
}";

            var generated = RunGenerators(source);

            Assert.Equal(3, CountStationOverloads(generated));
            Assert.Contains("manifest.PullWagon<global::System.String>(\"paymentId\")", generated);
            Assert.Contains("StationMerge.ToSignal(manifest, stationReturn, stationName, ", generated);
            Assert.DoesNotContain(", true, null);", generated);
        }

        /// <summary>
        /// Verifies that ReturnMembers are emitted only when all handlers in a group share one return shape.
        /// </summary>
        [Fact]
        public void Generator_EmitsReturnMembersOnly_WhenHandlersShareSingleReturnShape()
        {
            const string source = @"
using TrainOP;

public static class PartialReturnRoute
{
    public static TrainRoute Build() => new TrainRoute()
        .Station(""Seed"", () => new { paymentId = ""pay-partial"", amount = 3m, traceId = ""keep"" })
        .Station(""Partial"", (string paymentId, decimal amount) =>
            new { paymentId = paymentId + ""-merged"" });
}";

            var generated = RunGenerators(source);

            var returnMembersStart = generated.IndexOf("ReturnMembers_", StringComparison.Ordinal);
            Assert.True(returnMembersStart >= 0);
            var returnMembersEnd = generated.IndexOf("};", returnMembersStart, StringComparison.Ordinal);
            var returnMembersBlock = generated.Substring(returnMembersStart, returnMembersEnd - returnMembersStart);

            Assert.Contains("\"paymentId\"", returnMembersBlock);
            Assert.DoesNotContain("\"amount\"", returnMembersBlock);
            Assert.DoesNotContain("\"Item1\"", returnMembersBlock);
        }

        /// <summary>
        /// Verifies that multiple parameterless seed handlers share one Func of object overload.
        /// </summary>
        [Fact]
        public void Generator_EmitsSingleParameterlessStationOverload_ForMultipleSeedHandlers()
        {
            const string source = @"
using TrainOP;

public static class MultiSeedRoute
{
    public static TrainRoute First() => new TrainRoute()
        .Station(""Seed"", () => new { paymentId = ""pay-1"", amount = 100m });

    public static TrainRoute Second() => new TrainRoute()
        .Station(""Seed"", () => new { orderId = ""ord-1"", total = 50m });
}";

            var generated = RunGenerators(source);

            Assert.Equal(1, CountOccurrences("Func<global::System.Object>", generated));
            Assert.Contains("global::System.Object stationReturn = handler();", generated);
        }

        private static int CountOccurrences(string needle, string haystack)
        {
            var count = 0;
            var index = 0;
            while ((index = haystack.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
            {
                count++;
                index += needle.Length;
            }

            return count;
        }

        private static int CountStationOverloads(string generated)
        {
            var extensionsBlock = generated;
            const string extensionsMarker = "class TrainRouteStationExtensions";
            const string interceptorsMarker = "class TrainRouteStationInterceptors";
            var start = generated.IndexOf(extensionsMarker, StringComparison.Ordinal);
            if (start >= 0)
            {
                var interceptorsStart = generated.IndexOf(interceptorsMarker, start, StringComparison.Ordinal);
                extensionsBlock = interceptorsStart > start
                    ? generated.Substring(start, interceptorsStart - start)
                    : generated.Substring(start);
            }

            var stationOverloadCount = 0;
            var index = 0;
            while (index < extensionsBlock.Length)
            {
                var syncIndex = extensionsBlock.IndexOf("public static TrainRoute ", index, StringComparison.Ordinal);
                if (syncIndex < 0)
                {
                    break;
                }

                var lineEnd = extensionsBlock.IndexOf('\n', syncIndex);
                if (lineEnd < 0)
                {
                    lineEnd = extensionsBlock.Length;
                }

                var line = extensionsBlock.Substring(syncIndex, lineEnd - syncIndex);
                if (line.Contains("Station(this TrainRoute route", StringComparison.Ordinal))
                {
                    stationOverloadCount++;
                }

                index = syncIndex + 1;
            }

            return stationOverloadCount;
        }

        private static string RunGenerators(string source)
        {
            return string.Join(
                Environment.NewLine + "-----" + Environment.NewLine,
                RunGeneratorDriver(source).Results
                    .SelectMany(x => x.GeneratedSources)
                    .Select(x => x.SourceText.ToString()));
        }

        private static GeneratorDriverRunResult RunGeneratorDriver(string source)
        {
            var syntaxTree = CSharpSyntaxTree.ParseText(source, path: "Test0.cs");
            var compilation = CSharpCompilation.Create(
                "GeneratorTests",
                new[] { syntaxTree },
                GetMetadataReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            var generators = new ISourceGenerator[]
            {
                new TrainRouteStationGenerator().AsSourceGenerator(),
            };

            GeneratorDriver driver = CSharpGeneratorDriver.Create(generators);
            driver = driver.RunGenerators(compilation);

            return driver.GetRunResult();
        }

        private static MetadataReference[] GetMetadataReferences()
        {
            var coreDir = Path.GetDirectoryName(typeof(object).Assembly.Location);
            return new[]
            {
                MetadataReference.CreateFromFile(Path.Combine(coreDir, "System.Private.CoreLib.dll")),
                MetadataReference.CreateFromFile(Path.Combine(coreDir, "System.Runtime.dll")),
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(Attribute).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(TrainOP.CargoManifest).Assembly.Location),
                MetadataReference.CreateFromFile(Assembly.Load("netstandard").Location)
            };
        }
    }
}
