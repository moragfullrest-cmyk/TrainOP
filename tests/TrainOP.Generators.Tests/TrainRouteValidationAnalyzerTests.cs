using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Xunit;

namespace TrainOP.Generators.Tests
{
    /// <summary>
    /// Tests diagnostic output from <see cref="TrainRouteValidationAnalyzer"/> on route chains.
    /// </summary>
    public sealed class TrainRouteValidationAnalyzerTests
    {
        /// <summary>
        /// Verifies that a valid route chain produces no analyzer errors.
        /// </summary>
        [Fact]
        public async Task Analyzer_ValidChain_ProducesNoErrors()
        {
            const string source = @"
using TrainOP;

public static class PaymentRoute
{
    public static TrainRoute Build() => new TrainRoute()
        .Station(""Seed"", () => new { paymentId = ""pay-1"", amount = 100m })
        .Station(""Discount"", (string paymentId, decimal amount) =>
            new { paymentId, amount = amount * 0.9m });
}";

            var diagnostics = await RunAnalyzerAsync(source);

            Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        }

        /// <summary>
        /// Verifies that TOP014 is reported for multiple TrainRoute creations on the same line in one method.
        /// </summary>
        [Fact]
        public async Task Analyzer_ReportsTop014_ForSameLineMultipleTrainRouteNew()
        {
            const string source = @"
using TrainOP;

public static class MultiNewRoute
{
    public static TrainRoute Build(bool flag)
    {
        var a = new TrainRoute(), b = new TrainRoute();
        return flag
            ? a.Station(""Seed"", () => new { value = 1 })
            : b.Station(""Seed"", () => new { value = 2 });
    }
}";

            var diagnostics = await RunAnalyzerAsync(source);

            Assert.Contains(diagnostics, d => d.Id == "TOP014");
            Assert.DoesNotContain(
                diagnostics,
                d => d.Severity == DiagnosticSeverity.Error && d.Id != "TOP014");
        }

        /// <summary>
        /// Verifies that TOP001 is reported when the first station requires wagons without a seed.
        /// </summary>
        [Fact]
        public async Task Analyzer_ReportsTop001_WhenFirstStationRequiresWagonsWithoutSeed()
        {
            const string source = @"
using TrainOP;

public static class MissingSeedRoute
{
    public static TrainRoute Build() => new TrainRoute()
        .Station(""Double"", (string paymentId, decimal amount) =>
            new { paymentId, amount = amount * 2m });
}";

            var diagnostics = await RunAnalyzerAsync(source);

            Assert.Contains(diagnostics, d => d.Id == "TOP001");
        }

        /// <summary>
        /// Verifies that TOP001 is reported when a required wagon is missing from the chain.
        /// </summary>
        [Fact]
        public async Task Analyzer_ReportsTop001_WhenWagonMissing()
        {
            const string source = @"
using TrainOP;

public static class BrokenRoute
{
    public static TrainRoute Build() => new TrainRoute()
        .Station(""Seed"", () => new { paymentId = ""pay-1"" })
        .Station(""Discount"", (string paymentId, decimal amount) =>
            new { paymentId, amount = amount * 0.9m });
}";

            var diagnostics = await RunAnalyzerAsync(source);

            Assert.Contains(diagnostics, d => d.Id == "TOP001");
        }

        /// <summary>
        /// Verifies that TOP002 is reported when a wagon type conflicts with a prior station.
        /// </summary>
        [Fact]
        public async Task Analyzer_ReportsTop002_WhenWagonTypeConflicts()
        {
            const string source = @"
using TrainOP;

public static class BrokenRoute
{
    public static TrainRoute Build() => new TrainRoute()
        .Station(""Seed"", () => new { paymentId = ""pay-1"" })
        .Station(""Bad"", (int paymentId) => new { paymentId });
}";

            var diagnostics = await RunAnalyzerAsync(source);

            Assert.Contains(diagnostics, d => d.Id == "TOP002");
        }

        /// <summary>
        /// Verifies that TOP003 is reported when a removed wagon is required by a later station.
        /// </summary>
        [Fact]
        public async Task Analyzer_ReportsTop003_WhenRemovedWagonRequiredLater()
        {
            const string source = @"
using TrainOP;

public static class BrokenRoute
{
    public static TrainRoute Build() => new TrainRoute()
        .Station(""Seed"", () => new { paymentId = ""pay-1"", amount = 100m })
        .Station(""Partial"", (string paymentId, decimal amount) => new { paymentId })
        .Station(""NeedAmount"", (decimal amount) => new { amount });
}";

            var diagnostics = await RunAnalyzerAsync(source);

            Assert.Contains(diagnostics, d => d.Id == "TOP003");
        }

        /// <summary>
        /// Verifies that default ItemN tuple returns allocate ItemN keys so later stations can read them.
        /// </summary>
        [Fact]
        public async Task Analyzer_DoesNotReportTop001_WhenDefaultItemNTupleAllocatesItemNKeys()
        {
            const string source = @"
using TrainOP;

public static class ItemNParityRoute
{
    public static TrainRoute Build() => new TrainRoute()
        .Station(""Seed"", () => new { paymentId = ""pay-1"", amount = 100m })
        .Station(""Discount"", (string paymentId, decimal amount) =>
            (paymentId + ""-disc"", amount * 0.9m))
        .Station(""Finalize"", (string Item1, decimal Item2) =>
            new { paymentId = Item1, amount = Item2 });
}";

            var diagnostics = await RunAnalyzerAsync(source);

            Assert.Contains(diagnostics, d => d.Id == "TOP006");
            Assert.DoesNotContain(diagnostics, d => d.Id == "TOP001");
            Assert.DoesNotContain(diagnostics, d => d.Id == "TOP003");
        }

        /// <summary>
        /// Verifies that a partial default ItemN tuple still unloads omitted input wagons (TOP003).
        /// </summary>
        [Fact]
        public async Task Analyzer_ReportsTop003_WhenPartialDefaultItemNTupleOmitsWagon()
        {
            const string source = @"
using TrainOP;

public static class PartialItemNRoute
{
    public static TrainRoute Build() => new TrainRoute()
        .Station(""Seed"", () => new { paymentId = ""pay-1"", amount = 100m, note = ""keep"" })
        .Station(""Partial"", (string paymentId, decimal amount, string note) =>
            (paymentId + ""-only"", amount * 0.9m))
        .Station(""NeedNote"", (string note) => new { note });
}";

            var diagnostics = await RunAnalyzerAsync(source);

            Assert.Contains(diagnostics, d => d.Id == "TOP003");
        }

        /// <summary>
        /// Verifies that a route built via a local variable is recognized as a valid chain.
        /// </summary>
        [Fact]
        public async Task Analyzer_LocalVariableChain_ProducesNoTop006()
        {
            const string source = @"
using TrainOP;

public static class LocalRoute
{
    public static TrainRoute Build()
    {
        var route = new TrainRoute();
        return route.Station(""Seed"", () => new { paymentId = ""pay-1"", amount = 100m })
            .Station(""Discount"", (string paymentId, decimal amount) =>
                new { paymentId, amount = amount * 0.9m });
    }
}";

            var diagnostics = await RunAnalyzerAsync(source);

            Assert.DoesNotContain(diagnostics, d => d.Id == "TOP005");
            Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        }

        /// <summary>
        /// Verifies that a local variable with explicit type is recognized as a chain anchor.
        /// </summary>
        [Fact]
        public async Task Analyzer_LocalVariableChain_WithExplicitType_ProducesNoTop006()
        {
            const string source = @"
using TrainOP;

public static class LocalRoute
{
    public static TrainRoute Build()
    {
        TrainRoute route = new TrainRoute();
        route.Station(""Seed"", () => new { paymentId = ""pay-1"" });
        return route;
    }
}";

            var diagnostics = await RunAnalyzerAsync(source);

            Assert.DoesNotContain(diagnostics, d => d.Id == "TOP005");
        }

        /// <summary>
        /// Verifies statement-local Seed+Next on one bare-new local without TOP001/TOP005.
        /// </summary>
        [Fact]
        public async Task Analyzer_StatementLocalChain_ProducesNoTop001OrTop005()
        {
            const string source = @"
using TrainOP;

public static class StatementLocalRoute
{
    public static TrainRoute Build()
    {
        var route = new TrainRoute();
        route.Station(""Seed"", () => new { id = 1 });
        route.Station(""Next"", (int id) => new { id = id + 1 });
        return route;
    }
}";

            var diagnostics = await RunAnalyzerAsync(source);

            Assert.DoesNotContain(diagnostics, d => d.Id == "TOP001");
            Assert.DoesNotContain(diagnostics, d => d.Id == "TOP005");
            Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        }

        /// <summary>
        /// Verifies fluent-RHS <c>new</c> plus statement-local tail without TOP001/TOP005/TOP013.
        /// </summary>
        [Fact]
        public async Task Analyzer_FluentRhsNewThenStatementLocal_ProducesNoTop001OrTop005()
        {
            const string source = @"
using TrainOP;

public static class FluentRhsNewRoute
{
    public static TrainRoute Build()
    {
        var route = new TrainRoute()
            .Station(""Seed"", () => new { id = 1 });
        route.Station(""Next"", (int id) => new { id = id + 1 });
        return route;
    }
}";

            var diagnostics = await RunAnalyzerAsync(source);

            Assert.DoesNotContain(diagnostics, d => d.Id == "TOP001");
            Assert.DoesNotContain(diagnostics, d => d.Id == "TOP005");
            Assert.DoesNotContain(diagnostics, d => d.Id == "TOP013");
            Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        }

        /// <summary>
        /// Verifies fluent-RHS private factory plus statement-local Mid+Tail without TOP001/TOP005/TOP013.
        /// </summary>
        [Fact]
        public async Task Analyzer_FluentRhsFactoryThenStatementLocal_ProducesNoTop001OrTop005()
        {
            const string source = @"
using TrainOP;

public static class FluentRhsFactoryRoute
{
    public static TrainRoute Build()
    {
        var route = CreateSeed()
            .Station(""Mid"", (int id) => new { id });
        route.Station(""Tail"", (int id) => new { id = id + 1 });
        return route;
    }

    private static TrainRoute CreateSeed() =>
        new TrainRoute().Station(""Seed"", () => new { id = 1 });
}";

            var diagnostics = await RunAnalyzerAsync(source);

            Assert.DoesNotContain(diagnostics, d => d.Id == "TOP001");
            Assert.DoesNotContain(diagnostics, d => d.Id == "TOP005");
            Assert.DoesNotContain(diagnostics, d => d.Id == "TOP013");
            Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        }

        /// <summary>
        /// Verifies a wagon hole across statement-local stations still reports TOP001.
        /// </summary>
        [Fact]
        public async Task Analyzer_StatementLocalChain_ReportsTop001_WhenWagonMissing()
        {
            const string source = @"
using TrainOP;

public static class StatementLocalBrokenRoute
{
    public static TrainRoute Build()
    {
        var route = new TrainRoute();
        route.Station(""Seed"", () => new { paymentId = ""pay-1"" });
        route.Station(""Discount"", (string paymentId, decimal amount) =>
            new { paymentId, amount = amount * 0.9m });
        return route;
    }
}";

            var diagnostics = await RunAnalyzerAsync(source);

            Assert.Contains(diagnostics, d => d.Id == "TOP001");
        }

        /// <summary>
        /// Verifies that a reused local variable is anchored to its latest preceding assignment.
        /// </summary>
        [Fact]
        public async Task Analyzer_LocalVariableChain_ReuseAfterReassignment_ProducesNoTop006()
        {
            const string source = @"
using TrainOP;

public static class LocalRoute
{
    public static TrainRoute Build()
    {
        var route = new TrainRoute();
        route = new TrainRoute();
        return route.Station(""Seed"", () => new { paymentId = ""pay-1"", amount = 100m })
            .Station(""Discount"", (string paymentId, decimal amount) =>
                new { paymentId, amount = amount * 0.9m });
    }
}";

            var diagnostics = await RunAnalyzerAsync(source);

            Assert.DoesNotContain(diagnostics, d => d.Id == "TOP005");
            Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        }

        /// <summary>
        /// Verifies that two route chains can be built by reusing the same local variable.
        /// </summary>
        [Fact]
        public async Task Analyzer_LocalVariableChain_TwoRoutesInOneMethod_ProducesNoTop006()
        {
            const string source = @"
using TrainOP;

public static class LocalRoute
{
    public static void BuildBoth()
    {
        var route = new TrainRoute();
        route.Station(""First"", () => new { paymentId = ""pay-1"" });

        route = new TrainRoute();
        route.Station(""Second"", () => new { paymentId = ""pay-2"" });
    }
}";

            var diagnostics = await RunAnalyzerAsync(source);

            Assert.DoesNotContain(diagnostics, d => d.Id == "TOP005");
        }

        /// <summary>
        /// Verifies that parentheses around a creation receiver do not break chain detection.
        /// </summary>
        [Fact]
        public async Task Analyzer_ParenthesizedCreationReceiver_ProducesNoTop006()
        {
            const string source = @"
using TrainOP;

public static class ParenRoute
{
    public static TrainRoute Build() => (new TrainRoute())
        .Station(""Seed"", () => new { paymentId = ""pay-1"", amount = 100m })
        .Station(""Discount"", (string paymentId, decimal amount) =>
            new { paymentId, amount = amount * 0.9m });
}";

            var diagnostics = await RunAnalyzerAsync(source);

            Assert.DoesNotContain(diagnostics, d => d.Id == "TOP005");
            Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        }

        /// <summary>
        /// Verifies that nested parentheses around a local creation receiver do not break chain detection.
        /// </summary>
        [Fact]
        public async Task Analyzer_ParenthesizedLocalReceiver_ProducesNoTop006()
        {
            const string source = @"
using TrainOP;

public static class ParenLocalRoute
{
    public static TrainRoute Build()
    {
        var route = new TrainRoute();
        return ((route)).Station(""Seed"", () => new { paymentId = ""pay-1"", amount = 100m })
            .Station(""Discount"", (string paymentId, decimal amount) =>
                new { paymentId, amount = amount * 0.9m });
    }
}";

            var diagnostics = await RunAnalyzerAsync(source);

            Assert.DoesNotContain(diagnostics, d => d.Id == "TOP005");
            Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        }

        /// <summary>
        /// Verifies that a null-forgiving operator on a local creation receiver does not break chain detection.
        /// </summary>
        [Fact]
        public async Task Analyzer_NullForgivingLocalReceiver_ProducesNoTop006()
        {
            const string source = @"
using TrainOP;

public static class NullForgivingRoute
{
    public static TrainRoute Build()
    {
        var route = new TrainRoute();
        return route!.Station(""Seed"", () => new { paymentId = ""pay-1"", amount = 100m })
            .Station(""Discount"", (string paymentId, decimal amount) =>
                new { paymentId, amount = amount * 0.9m });
    }
}";

            var diagnostics = await RunAnalyzerAsync(source);

            Assert.DoesNotContain(diagnostics, d => d.Id == "TOP005");
            Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        }

        /// <summary>
        /// Verifies that a cast around a creation receiver does not break chain detection.
        /// </summary>
        [Fact]
        public async Task Analyzer_CastCreationReceiver_ProducesNoTop006()
        {
            const string source = @"
using TrainOP;

public static class CastRoute
{
    public static TrainRoute Build() => ((TrainRoute)(object)new TrainRoute())
        .Station(""Seed"", () => new { paymentId = ""pay-1"", amount = 100m })
        .Station(""Discount"", (string paymentId, decimal amount) =>
            new { paymentId, amount = amount * 0.9m });
}";

            var diagnostics = await RunAnalyzerAsync(source);

            Assert.DoesNotContain(diagnostics, d => d.Id == "TOP005");
            Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        }

        /// <summary>
        /// Verifies TOP005 when cast peels to an opaque <c>object</c> factory (not a known origin).
        /// </summary>
        [Fact]
        public async Task Analyzer_ReportsTop005_WhenCastReceiverIsOpaqueObjectFactory()
        {
            const string source = @"
using TrainOP;

public static class CastOpaqueRoute
{
    public static TrainRoute Build() =>
        ((TrainRoute)GetObject())
            .Station(""Seed"", () => new { id = 1 });

    private static object GetObject() => new TrainRoute();
}";

            var diagnostics = await RunAnalyzerAsync(source);

            Assert.Contains(diagnostics, d => d.Id == "TOP005");
        }

        /// <summary>
        /// Verifies that a cast around a local creation receiver does not break chain detection.
        /// </summary>
        [Fact]
        public async Task Analyzer_CastLocalReceiver_ProducesNoTop006()
        {
            const string source = @"
using TrainOP;

public static class CastLocalRoute
{
    public static TrainRoute Build()
    {
        var route = new TrainRoute();
        return ((TrainRoute)(object)route).Station(""Seed"", () => new { paymentId = ""pay-1"", amount = 100m })
            .Station(""Discount"", (string paymentId, decimal amount) =>
                new { paymentId, amount = amount * 0.9m });
    }
}";

            var diagnostics = await RunAnalyzerAsync(source);

            Assert.DoesNotContain(diagnostics, d => d.Id == "TOP005");
            Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        }

        /// <summary>
        /// Verifies that await Task.FromResult around a creation receiver does not break chain detection.
        /// </summary>
        [Fact]
        public async Task Analyzer_AwaitFromResultCreationReceiver_ProducesNoTop006()
        {
            const string source = @"
using System.Threading.Tasks;
using TrainOP;

public static class AwaitCreationRoute
{
    public static async Task<TrainRoute> BuildAsync() => (await Task.FromResult(new TrainRoute()))
        .Station(""Seed"", () => new { paymentId = ""pay-1"", amount = 100m })
        .Station(""Discount"", (string paymentId, decimal amount) =>
            new { paymentId, amount = amount * 0.9m });
}";

            var diagnostics = await RunAnalyzerAsync(source);

            Assert.DoesNotContain(diagnostics, d => d.Id == "TOP005");
            Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        }

        /// <summary>
        /// Verifies that await Task.FromResult around a local creation receiver does not break chain detection.
        /// </summary>
        [Fact]
        public async Task Analyzer_AwaitFromResultLocalReceiver_ProducesNoTop006()
        {
            const string source = @"
using System.Threading.Tasks;
using TrainOP;

public static class AwaitLocalRoute
{
    public static async Task<TrainRoute> BuildAsync()
    {
        var route = new TrainRoute();
        return (await Task.FromResult(route)).Station(""Seed"", () => new { paymentId = ""pay-1"", amount = 100m })
            .Station(""Discount"", (string paymentId, decimal amount) =>
                new { paymentId, amount = amount * 0.9m });
    }
}";

            var diagnostics = await RunAnalyzerAsync(source);

            Assert.DoesNotContain(diagnostics, d => d.Id == "TOP005");
            Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        }

        /// <summary>
        /// Verifies fluent extension after <c>await CreateAsync()</c> (Task&lt;TrainRoute&gt; factory).
        /// </summary>
        [Fact]
        public async Task Analyzer_AwaitAsyncFactoryExtension_ProducesNoTop001OrTop005()
        {
            const string source = @"
using System.Threading.Tasks;
using TrainOP;

public static class AwaitAsyncFactoryRoute
{
    public static async Task<TrainRoute> BuildAsync() =>
        (await CreateAsync())
            .Station(""Next"", (int id) => new { id = id + 1 });

    private static async Task<TrainRoute> CreateAsync() =>
        new TrainRoute().Station(""Seed"", () => new { id = 1 });
}";

            var diagnostics = await RunAnalyzerAsync(source);

            Assert.DoesNotContain(diagnostics, d => d.Id == "TOP001");
            Assert.DoesNotContain(diagnostics, d => d.Id == "TOP005");
            Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        }

        /// <summary>
        /// Verifies statement-local after <c>var r = await CreateAsync()</c>.
        /// </summary>
        [Fact]
        public async Task Analyzer_StatementLocalAfterAwaitAsyncFactory_ProducesNoTop001OrTop005()
        {
            const string source = @"
using System.Threading.Tasks;
using TrainOP;

public static class StatementAwaitAsyncFactoryRoute
{
    public static async Task<TrainRoute> BuildAsync()
    {
        var route = await CreateAsync();
        route.Station(""Next"", (int id) => new { id = id + 1 });
        return route;
    }

    private static async Task<TrainRoute> CreateAsync() =>
        new TrainRoute().Station(""Seed"", () => new { id = 1 });
}";

            var diagnostics = await RunAnalyzerAsync(source);

            Assert.DoesNotContain(diagnostics, d => d.Id == "TOP001");
            Assert.DoesNotContain(diagnostics, d => d.Id == "TOP005");
            Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        }

        /// <summary>
        /// Verifies await of a local async factory function plus statement-local tail.
        /// </summary>
        [Fact]
        public async Task Analyzer_StatementLocalAfterAwaitLocalAsyncFactory_ProducesNoTop001OrTop005()
        {
            const string source = @"
using System.Threading.Tasks;
using TrainOP;

public static class StatementAwaitLocalAsyncFactoryRoute
{
    public static async Task<TrainRoute> BuildAsync()
    {
        async Task<TrainRoute> LocalAsync() =>
            new TrainRoute().Station(""Seed"", () => new { id = 1 });

        var route = await LocalAsync();
        route.Station(""Next"", (int id) => new { id = id + 1 });
        return route;
    }
}";

            var diagnostics = await RunAnalyzerAsync(source);

            Assert.DoesNotContain(diagnostics, d => d.Id == "TOP001");
            Assert.DoesNotContain(diagnostics, d => d.Id == "TOP005");
            Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        }

        /// <summary>
        /// Verifies await of ValueTask&lt;TrainRoute&gt; factory extension.
        /// </summary>
        [Fact]
        public async Task Analyzer_AwaitValueTaskFactoryExtension_ProducesNoTop001OrTop005()
        {
            const string source = @"
using System.Threading.Tasks;
using TrainOP;

public static class AwaitValueTaskFactoryRoute
{
    public static async Task<TrainRoute> BuildAsync() =>
        (await CreateAsync())
            .Station(""Next"", (int id) => new { id = id + 1 });

    private static async ValueTask<TrainRoute> CreateAsync() =>
        new TrainRoute().Station(""Seed"", () => new { id = 1 });
}";

            var diagnostics = await RunAnalyzerAsync(source);

            Assert.DoesNotContain(diagnostics, d => d.Id == "TOP001");
            Assert.DoesNotContain(diagnostics, d => d.Id == "TOP005");
            Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        }

        /// <summary>
        /// Verifies statement-local after <c>Get(out TrainRoute r)</c> without TOP001/TOP005.
        /// </summary>
        [Fact]
        public async Task Analyzer_StatementLocalAfterOutFactory_ProducesNoTop001OrTop005()
        {
            const string source = @"
using TrainOP;

public static class StatementOutFactoryRoute
{
    public static TrainRoute Build()
    {
        Get(out TrainRoute route);
        route.Station(""Next"", (int id) => new { id = id + 1 });
        return route;
    }

    private static void Get(out TrainRoute route) =>
        route = new TrainRoute().Station(""Seed"", () => new { id = 1 });
}";

            var diagnostics = await RunAnalyzerAsync(source);

            Assert.DoesNotContain(diagnostics, d => d.Id == "TOP001");
            Assert.DoesNotContain(diagnostics, d => d.Id == "TOP005");
            Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        }

        /// <summary>
        /// Verifies <c>ref TrainRoute</c> remains opaque (TOP005), unlike <c>out</c>.
        /// </summary>
        [Fact]
        public async Task Analyzer_ReportsTop005_WhenLocalIsAssignedViaRefParameter()
        {
            const string source = @"
using TrainOP;

public static class RefFactoryRoute
{
    public static TrainRoute Build()
    {
        TrainRoute route = null;
        Mutate(ref route);
        route.Station(""Next"", (int id) => new { id = id + 1 });
        return route;
    }

    private static void Mutate(ref TrainRoute route) =>
        route = new TrainRoute().Station(""Seed"", () => new { id = 1 });
}";

            var diagnostics = await RunAnalyzerAsync(source);

            Assert.Contains(diagnostics, d => d.Id == "TOP005");
        }

        /// <summary>
        /// Verifies statement-local after tuple deconstruct from a known origin without TOP001/TOP005.
        /// </summary>
        [Fact]
        public async Task Analyzer_StatementLocalAfterTupleDeconstruct_ProducesNoTop001OrTop005()
        {
            const string source = @"
using TrainOP;

public static class TupleDeconstructRoute
{
    public static TrainRoute Build()
    {
        (var route, _) = (new TrainRoute().Station(""Seed"", () => new { id = 1 }), 0);
        route.Station(""Next"", (int id) => new { id = id + 1 });
        return route;
    }
}";

            var diagnostics = await RunAnalyzerAsync(source);

            Assert.DoesNotContain(diagnostics, d => d.Id == "TOP001");
            Assert.DoesNotContain(diagnostics, d => d.Id == "TOP005");
            Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        }

        /// <summary>
        /// Verifies TOP005 when deconstruct RHS is an opaque factory pair.
        /// </summary>
        [Fact]
        public async Task Analyzer_ReportsTop005_WhenTupleDeconstructUsesOpaqueGetPair()
        {
            const string source = @"
using TrainOP;

public static class OpaqueTupleRoute
{
    public static TrainRoute Build()
    {
        (var route, _) = GetPair();
        route.Station(""Next"", (int id) => new { id = id + 1 });
        return route;
    }

    private static (TrainRoute, int) GetPair() =>
        (new TrainRoute().Station(""Seed"", () => new { id = 1 }), 0);
}";

            var diagnostics = await RunAnalyzerAsync(source);

            Assert.Contains(diagnostics, d => d.Id == "TOP005");
        }

        /// <summary>
        /// Verifies statement-local after <c>is TrainRoute r</c> with known fluent origin.
        /// </summary>
        [Fact]
        public async Task Analyzer_StatementLocalAfterIsPattern_ProducesNoTop001OrTop005()
        {
            const string source = @"
using TrainOP;

public static class IsPatternRoute
{
    public static TrainRoute Build()
    {
        if (new TrainRoute().Station(""Seed"", () => new { id = 1 }) is TrainRoute route)
        {
            route.Station(""Next"", (int id) => new { id = id + 1 });
            return route;
        }

        return new TrainRoute()
            .Station(""Seed"", () => new { id = 1 })
            .Station(""Next"", (int id) => new { id = id + 1 });
    }
}";

            var diagnostics = await RunAnalyzerAsync(source);

            Assert.DoesNotContain(diagnostics, d => d.Id == "TOP001");
            Assert.DoesNotContain(diagnostics, d => d.Id == "TOP005");
            Assert.DoesNotContain(diagnostics, d => d.Id == "TOP012");
            Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        }

        /// <summary>
        /// Verifies TOP005 when <c>is TrainRoute r</c> matches an opaque expression.
        /// </summary>
        [Fact]
        public async Task Analyzer_ReportsTop005_WhenIsPatternUsesOpaqueExpression()
        {
            const string source = @"
using TrainOP;

public static class OpaqueIsPatternRoute
{
    public static TrainRoute Build()
    {
        if (GetObject() is TrainRoute route)
        {
            route.Station(""Next"", (int id) => new { id = id + 1 });
            return route;
        }

        return new TrainRoute();
    }

    private static object GetObject() =>
        new TrainRoute().Station(""Seed"", () => new { id = 1 });
}";

            var diagnostics = await RunAnalyzerAsync(source);

            Assert.Contains(diagnostics, d => d.Id == "TOP005");
        }

        /// <summary>
        /// Verifies statement-local after switch <c>case TrainRoute r</c> with factory origin.
        /// </summary>
        [Fact]
        public async Task Analyzer_StatementLocalAfterSwitchCasePattern_ProducesNoTop001OrTop005()
        {
            const string source = @"
using TrainOP;

public static class SwitchCasePatternRoute
{
    public static TrainRoute Build()
    {
        switch (CreateSeed())
        {
            case TrainRoute route:
                route.Station(""Next"", (int id) => new { id = id + 1 });
                return route;
            default:
                return CreateSeed()
                    .Station(""Next"", (int id) => new { id = id + 1 });
        }
    }

    private static TrainRoute CreateSeed() =>
        new TrainRoute().Station(""Seed"", () => new { id = 1 });
}";

            var diagnostics = await RunAnalyzerAsync(source);

            Assert.DoesNotContain(diagnostics, d => d.Id == "TOP001");
            Assert.DoesNotContain(diagnostics, d => d.Id == "TOP005");
            Assert.DoesNotContain(diagnostics, d => d.Id == "TOP012");
            Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        }

        /// <summary>
        /// Verifies that TOP005 is reported when a local is assigned from an opaque source
        /// (not bare <c>new</c> and not a resolvable private/internal factory).
        /// </summary>
        [Fact]
        public async Task Analyzer_ReportsTop005_WhenLocalIsAssignedFromOpaqueParameter()
        {
            const string source = @"
using TrainOP;

public static class BrokenRoute
{
    public static TrainRoute Build(TrainRoute incoming)
    {
        var route = incoming;
        return route.Station(""Seed"", () => new { paymentId = ""pay-1"" });
    }
}";

            var diagnostics = await RunAnalyzerAsync(source);

            Assert.Contains(diagnostics, d => d.Id == "TOP005");
        }

        /// <summary>
        /// Verifies TOP005 when <c>.Station</c> runs on a null local without known-origin init.
        /// </summary>
        [Fact]
        public async Task Analyzer_ReportsTop005_WhenStationOnNullLocalWithoutInit()
        {
            const string source = @"
using TrainOP;

public static class NullWithoutInitRoute
{
    public static TrainRoute Build()
    {
        TrainRoute route = null;
        route.Station(""Seed"", () => new { id = 1 });
        return route;
    }
}";

            var diagnostics = await RunAnalyzerAsync(source);

            Assert.Contains(diagnostics, d => d.Id == "TOP005");
        }

        /// <summary>
        /// Verifies that null/default placeholder then known-origin assign allows statement-local.
        /// </summary>
        [Fact]
        public async Task Analyzer_StatementLocalAfterNullThenKnownInit_ProducesNoTop005()
        {
            const string source = @"
using TrainOP;

public static class NullThenInitRoute
{
    public static TrainRoute Build()
    {
        TrainRoute route = null;
        route = new TrainRoute();
        route.Station(""Seed"", () => new { id = 1 });
        route.Station(""Next"", (int id) => new { id = id + 1 });
        return route;
    }
}";

            var diagnostics = await RunAnalyzerAsync(source);

            Assert.DoesNotContain(diagnostics, d => d.Id == "TOP005");
            Assert.DoesNotContain(diagnostics, d => d.Id == "TOP001");
            Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        }

        /// <summary>
        /// Verifies TOP005 when a Station is invoked on an alias of a fluent Station result.
        /// </summary>
        [Fact]
        public async Task Analyzer_ReportsTop005_WhenStationUsesAliasOfFluentStation()
        {
            const string source = @"
using TrainOP;

public static class AliasRoute
{
    public static TrainRoute Build()
    {
        var route = new TrainRoute();
        var route1 = route.Station(""A"", () => new { id = 1 });
        route1.Station(""B"", (int id) => new { id });
        route.Station(""C"", (int id) => new { id });
        return route;
    }
}";

            var diagnostics = await RunAnalyzerAsync(source);

            Assert.Contains(diagnostics, d => d.Id == "TOP005");
        }

        /// <summary>
        /// Verifies that private factory extension chains are recognized without TOP005.
        /// </summary>
        [Fact]
        public async Task Analyzer_PrivateFactoryExtension_DoesNotReportTop005()
        {
            const string source = @"
using TrainOP;

public static class ExtensionRoute
{
    public static TrainRoute Build() =>
        CreateSeed()
            .Station(""Discount"", (decimal amount) => new { amount = amount * 0.9m });

    private static TrainRoute CreateSeed() =>
        new TrainRoute().Station(""Seed"", () => new { amount = 100m });
}";

            var diagnostics = await RunAnalyzerAsync(source);

            Assert.DoesNotContain(diagnostics, d => d.Id == "TOP005");
            Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        }

        /// <summary>
        /// Verifies local-function factory fluent extension without TOP005.
        /// </summary>
        [Fact]
        public async Task Analyzer_LocalFunctionFactoryExtension_DoesNotReportTop005()
        {
            const string source = @"
using TrainOP;

public static class LocalFunctionExtensionRoute
{
    public static TrainRoute Build()
    {
        TrainRoute Local() =>
            new TrainRoute().Station(""Seed"", () => new { amount = 100m });

        return Local()
            .Station(""Discount"", (decimal amount) => new { amount = amount * 0.9m });
    }
}";

            var diagnostics = await RunAnalyzerAsync(source);

            Assert.DoesNotContain(diagnostics, d => d.Id == "TOP005");
            Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        }

        /// <summary>
        /// Verifies statement-local extension after a local-function factory without TOP001/TOP005.
        /// </summary>
        [Fact]
        public async Task Analyzer_StatementLocalAfterLocalFunctionFactory_ProducesNoTop001OrTop005()
        {
            const string source = @"
using TrainOP;

public static class StatementLocalFunctionFactoryRoute
{
    public static TrainRoute Build()
    {
        TrainRoute Local() =>
            new TrainRoute().Station(""Seed"", () => new { id = 1 });

        var route = Local();
        route.Station(""Next"", (int id) => new { id = id + 1 });
        return route;
    }
}";

            var diagnostics = await RunAnalyzerAsync(source);

            Assert.DoesNotContain(diagnostics, d => d.Id == "TOP001");
            Assert.DoesNotContain(diagnostics, d => d.Id == "TOP005");
            Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        }

        /// <summary>
        /// Verifies statement-local extension after a private factory without TOP001/TOP005.
        /// </summary>
        [Fact]
        public async Task Analyzer_StatementLocalAfterPrivateFactory_ProducesNoTop001OrTop005()
        {
            const string source = @"
using TrainOP;

public static class StatementFactoryLocalRoute
{
    public static TrainRoute Build()
    {
        var route = CreateSeed();
        route.Station(""Next"", (int id) => new { id = id + 1 });
        return route;
    }

    private static TrainRoute CreateSeed() =>
        new TrainRoute().Station(""Seed"", () => new { id = 1 });
}";

            var diagnostics = await RunAnalyzerAsync(source);

            Assert.DoesNotContain(diagnostics, d => d.Id == "TOP001");
            Assert.DoesNotContain(diagnostics, d => d.Id == "TOP005");
            Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        }

        /// <summary>
        /// Verifies a statement-style private factory body yields known terminals (no TOP013)
        /// when extended fluently.
        /// </summary>
        [Fact]
        public async Task Analyzer_StatementStylePrivateFactory_ProducesNoTop013()
        {
            const string source = @"
using TrainOP;

public static class StatementStyleFactoryRoute
{
    public static TrainRoute Build() =>
        CreateSeed()
            .Station(""Next"", (int id) => new { id = id + 1 });

    private static TrainRoute CreateSeed()
    {
        var route = new TrainRoute();
        route.Station(""Seed"", () => new { id = 1 });
        return route;
    }
}";

            var diagnostics = await RunAnalyzerAsync(source);

            Assert.DoesNotContain(diagnostics, d => d.Id == "TOP013");
            Assert.DoesNotContain(diagnostics, d => d.Id == "TOP005");
            Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        }

        /// <summary>
        /// Verifies that parentheses around a factory extension receiver do not break chain detection.
        /// </summary>
        [Fact]
        public async Task Analyzer_ParenthesizedFactoryExtension_DoesNotReportTop005()
        {
            const string source = @"
using TrainOP;

public static class ParenFactoryRoute
{
    public static TrainRoute Build() =>
        (CreateSeed())
            .Station(""Discount"", (decimal amount) => new { amount = amount * 0.9m });

    private static TrainRoute CreateSeed() =>
        new TrainRoute().Station(""Seed"", () => new { amount = 100m });
}";

            var diagnostics = await RunAnalyzerAsync(source);

            Assert.DoesNotContain(diagnostics, d => d.Id == "TOP005");
            Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        }

        /// <summary>
        /// Verifies that a cast around a factory extension receiver does not break chain detection.
        /// </summary>
        [Fact]
        public async Task Analyzer_CastFactoryExtension_DoesNotReportTop005()
        {
            const string source = @"
using TrainOP;

public static class CastFactoryRoute
{
    public static TrainRoute Build() =>
        ((TrainRoute)(object)CreateSeed())
            .Station(""Discount"", (decimal amount) => new { amount = amount * 0.9m });

    private static TrainRoute CreateSeed() =>
        new TrainRoute().Station(""Seed"", () => new { amount = 100m });
}";

            var diagnostics = await RunAnalyzerAsync(source);

            Assert.DoesNotContain(diagnostics, d => d.Id == "TOP005");
            Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        }

        /// <summary>
        /// Verifies that TOP005 is reported when extension starts from an unsupported receiver.
        /// </summary>
        [Fact]
        public async Task Analyzer_ReportsTop005_WhenExtensionUsesParameterReceiver()
        {
            const string source = @"
using TrainOP;

public static class ParameterRoute
{
    public static TrainRoute Build(TrainRoute baseRoute) =>
        baseRoute.Station(""Discount"", (decimal amount) => new { amount = amount * 0.9m });
}";

            var diagnostics = await RunAnalyzerAsync(source);

            Assert.Contains(diagnostics, d => d.Id == "TOP005");
        }

        /// <summary>
        /// Verifies that TOP005 is reported when extension starts from a delegate invocation receiver.
        /// </summary>
        [Fact]
        public async Task Analyzer_ReportsTop005_WhenExtensionUsesDelegateInvocationReceiver()
        {
            const string source = @"
using System;
using TrainOP;

public static class DelegateRoute
{
    public static TrainRoute Build(Func<TrainRoute> buildRoute) =>
        buildRoute().Station(""Discount"", (decimal amount) => new { amount = amount * 0.9m });

    private static TrainRoute CreateSeed() =>
        new TrainRoute().Station(""Seed"", () => new { amount = 100m });
}";

            var diagnostics = await RunAnalyzerAsync(source);

            Assert.Contains(diagnostics, d => d.Id == "TOP005");
        }

        /// <summary>
        /// Verifies that a matching ternary join does not report TOP005 or other errors on Join.
        /// </summary>
        [Fact]
        public async Task Analyzer_TernaryJoin_MatchingArms_NoTop006NoErrors()
        {
            const string source = @"
using TrainOP;

public static class JoinedRoute
{
    public static TrainRoute Build(bool flag) =>
        (flag
            ? new TrainRoute().Station(""Seed"", () => new { value = 1 })
            : new TrainRoute().Station(""Seed"", () => new { value = 2 }))
        .Station(""Join"", (int value) => new { value });
}";

            var diagnostics = await RunAnalyzerAsync(source);

            Assert.DoesNotContain(diagnostics, d => d.Id == "TOP005");
            Assert.DoesNotContain(diagnostics, d => d.Id == "TOP008");
            Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        }

        /// <summary>
        /// Verifies that conflicting branch wagon types report TOP008 and suppress TOP005 on Join.
        /// </summary>
        [Fact]
        public async Task Analyzer_TernaryJoin_ConflictingTypes_ReportsTop008_NoTop006OnJoin()
        {
            const string source = @"
using TrainOP;

public static class BrokenJoinRoute
{
    public static TrainRoute Build(bool flag) =>
        (flag
            ? new TrainRoute().Station(""Left"", () => new { value = 1 })
            : new TrainRoute().Station(""Right"", () => new { value = ""text"" }))
        .Station(""Join"", (int value) => new { value });
}";

            var diagnostics = await RunAnalyzerAsync(source);

            Assert.Contains(diagnostics, d => d.Id == "TOP008");
            Assert.DoesNotContain(diagnostics, d => d.Id == "TOP005");
        }

        /// <summary>
        /// Verifies statement-local after equivalent ternary assign without TOP001/TOP005/TOP008.
        /// </summary>
        [Fact]
        public async Task Analyzer_StatementLocalAfterTernaryAssign_ProducesNoTop001OrTop005()
        {
            const string source = @"
using TrainOP;

public static class TernaryAssignRoute
{
    public static TrainRoute Build(bool flag)
    {
        var route = flag
            ? new TrainRoute()
            : new TrainRoute();
        route.Station(""Seed"", () => new { id = 1 });
        route.Station(""Next"", (int id) => new { id = id + 1 });
        return route;
    }
}";

            var diagnostics = await RunAnalyzerAsync(source);

            Assert.DoesNotContain(diagnostics, d => d.Id == "TOP001");
            Assert.DoesNotContain(diagnostics, d => d.Id == "TOP005");
            Assert.DoesNotContain(diagnostics, d => d.Id == "TOP008");
            Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        }

        /// <summary>
        /// Verifies statement-local after ternary of matching factories.
        /// </summary>
        [Fact]
        public async Task Analyzer_StatementLocalAfterTernaryFactoryAssign_ProducesNoTop001OrTop005()
        {
            const string source = @"
using TrainOP;

public static class TernaryFactoryAssignRoute
{
    public static TrainRoute Build(bool flag)
    {
        var route = flag ? CreateSeed() : CreateSeed();
        route.Station(""Next"", (int id) => new { id = id + 1 });
        return route;
    }

    private static TrainRoute CreateSeed() =>
        new TrainRoute().Station(""Seed"", () => new { id = 1 });
}";

            var diagnostics = await RunAnalyzerAsync(source);

            Assert.DoesNotContain(diagnostics, d => d.Id == "TOP001");
            Assert.DoesNotContain(diagnostics, d => d.Id == "TOP005");
            Assert.DoesNotContain(diagnostics, d => d.Id == "TOP008");
            Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        }

        /// <summary>
        /// Verifies TOP008 when ternary assign arms have conflicting terminal types.
        /// </summary>
        [Fact]
        public async Task Analyzer_StatementLocalAfterTernaryAssign_ConflictingTypes_ReportsTop008()
        {
            const string source = @"
using TrainOP;

public static class TernaryConflictAssignRoute
{
    public static TrainRoute Build(bool flag)
    {
        var route = flag
            ? new TrainRoute().Station(""Left"", () => new { value = 1 })
            : new TrainRoute().Station(""Right"", () => new { value = ""text"" });
        route.Station(""Join"", (int value) => new { value });
        return route;
    }
}";

            var diagnostics = await RunAnalyzerAsync(source);

            Assert.Contains(diagnostics, d => d.Id == "TOP008");
            Assert.DoesNotContain(diagnostics, d => d.Id == "TOP005");
        }

        /// <summary>
        /// Verifies statement-local after equivalent switch assign without TOP001/TOP005/TOP008.
        /// </summary>
        [Fact]
        public async Task Analyzer_StatementLocalAfterSwitchAssign_ProducesNoTop001OrTop005()
        {
            const string source = @"
using TrainOP;

public static class SwitchAssignRoute
{
    public static TrainRoute Build(int kind)
    {
        var route = kind switch
        {
            0 => new TrainRoute(),
            _ => new TrainRoute()
        };
        route.Station(""Seed"", () => new { id = 1 });
        route.Station(""Next"", (int id) => new { id = id + 1 });
        return route;
    }
}";

            var diagnostics = await RunAnalyzerAsync(source);

            Assert.DoesNotContain(diagnostics, d => d.Id == "TOP001");
            Assert.DoesNotContain(diagnostics, d => d.Id == "TOP005");
            Assert.DoesNotContain(diagnostics, d => d.Id == "TOP008");
            Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        }

        /// <summary>
        /// Verifies statement-local after switch of matching factories.
        /// </summary>
        [Fact]
        public async Task Analyzer_StatementLocalAfterSwitchFactoryAssign_ProducesNoTop001OrTop005()
        {
            const string source = @"
using TrainOP;

public static class SwitchFactoryAssignRoute
{
    public static TrainRoute Build(int kind)
    {
        var route = kind switch
        {
            0 => CreateSeed(),
            _ => CreateSeed()
        };
        route.Station(""Next"", (int id) => new { id = id + 1 });
        return route;
    }

    private static TrainRoute CreateSeed() =>
        new TrainRoute().Station(""Seed"", () => new { id = 1 });
}";

            var diagnostics = await RunAnalyzerAsync(source);

            Assert.DoesNotContain(diagnostics, d => d.Id == "TOP001");
            Assert.DoesNotContain(diagnostics, d => d.Id == "TOP005");
            Assert.DoesNotContain(diagnostics, d => d.Id == "TOP008");
            Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        }

        /// <summary>
        /// Verifies TOP008 when switch assign arms have conflicting terminal types.
        /// </summary>
        [Fact]
        public async Task Analyzer_StatementLocalAfterSwitchAssign_ConflictingTypes_ReportsTop008()
        {
            const string source = @"
using TrainOP;

public static class SwitchConflictAssignRoute
{
    public static TrainRoute Build(int kind)
    {
        var route = kind switch
        {
            0 => new TrainRoute().Station(""Left"", () => new { value = 1 }),
            _ => new TrainRoute().Station(""Right"", () => new { value = ""text"" })
        };
        route.Station(""Join"", (int value) => new { value });
        return route;
    }
}";

            var diagnostics = await RunAnalyzerAsync(source);

            Assert.Contains(diagnostics, d => d.Id == "TOP008");
            Assert.DoesNotContain(diagnostics, d => d.Id == "TOP005");
        }

        /// <summary>
        /// Verifies that a station after a service station is allowed in the chain.
        /// </summary>
        [Fact]
        public async Task Analyzer_AllowsStation_AfterServiceStation()
        {
            const string source = @"
using TrainOP;

public static class RecoveryRoute
{
    public static TrainRoute Build() => new TrainRoute()
        .Station(""Seed"", () => new { value = 0 })
        .Station(""Validate"", (int value) =>
            value > 0 ? RailwaySignals.Green(new { value }) : RailwaySignals.Red(""ERR"", ""bad""))
        .ServiceStation(""Recovery"", (ref int value, RedSignal red) => RailwaySignals.White)
        .Station(""After"", (int value) => new { value = value + 1 });
}";

            var diagnostics = await RunAnalyzerAsync(source);

            Assert.DoesNotContain(diagnostics, d => d.Id == "TOP005");
        }

        /// <summary>
        /// Verifies that ServiceStation handlers with Signal delegate return and RailwaySignals.White are allowed.
        /// </summary>
        [Fact]
        public async Task Analyzer_AllowsServiceStation_WithRefWagonsAndPassReturn()
        {
            const string source = @"
using TrainOP;

public static class RecoveryRoute
{
    public static TrainRoute Build() => new TrainRoute()
        .Station(""Seed"", () => new { paymentId = ""pay-1"", amount = -1m })
        .Station(""Validate"", (string paymentId, decimal amount) =>
            amount > 0 ? RailwaySignals.Green(new { paymentId, amount }) : RailwaySignals.Red(""ERR"", ""bad""))
        .ServiceStation(""Recovery"", (ref string paymentId, ref decimal amount, RedSignal red) =>
        {
            paymentId = ""pay-fixed"";
            amount = 50m;
            return RailwaySignals.White;
        })
        .Station(""After"", (string paymentId, decimal amount) => new { paymentId, amount });
}";

            var diagnostics = await RunAnalyzerAsync(source);

            Assert.DoesNotContain(diagnostics, d => d.Id == "TOP010");
        }

        /// <summary>
        /// Verifies that by-value ServiceStation wagons with a data return are allowed.
        /// </summary>
        [Fact]
        public async Task Analyzer_AllowsServiceStation_WithByValueWagonsAndGreenReturn()
        {
            const string source = @"
using TrainOP;

public static class RecoveryRoute
{
    public static TrainRoute Build() => new TrainRoute()
        .Station(""Seed"", () => new { paymentId = ""pay-1"", amount = -1m })
        .Station(""Validate"", (string paymentId, decimal amount) =>
            amount > 0 ? RailwaySignals.Green(new { paymentId, amount }) : RailwaySignals.Red(""ERR"", ""bad""))
        .ServiceStation(""Recovery"", (decimal amount, SignalIssue issue) =>
            issue.Code == ""ERR""
                ? RailwaySignals.Green(new { amount = 50m })
                : RailwaySignals.Red(""NOPE"", ""skip""))
        .Station(""After"", (string paymentId, decimal amount) => new { paymentId, amount });
}";

            var diagnostics = await RunAnalyzerAsync(source);

            Assert.DoesNotContain(diagnostics, d => d.Id == "TOP005");
            Assert.DoesNotContain(diagnostics, d => d.Id == "TOP009");
            Assert.DoesNotContain(diagnostics, d => d.Id == "TOP010");
            Assert.DoesNotContain(diagnostics, d => d.Id == "TOP015");
            Assert.DoesNotContain(diagnostics, d => d.Id == "TOP016");
            Assert.DoesNotContain(diagnostics, d => d.Id == "TOP017");
        }

        /// <summary>
        /// Verifies that TOP015 is reported when ServiceStation return adds a new wagon.
        /// </summary>
        [Fact]
        public async Task Analyzer_ReportsTop015_WhenServiceStationAddsWagon()
        {
            const string source = @"
using TrainOP;

public static class RecoveryRoute
{
    public static TrainRoute Build() => new TrainRoute()
        .Station(""Seed"", () => new { amount = -1m })
        .Station(""Validate"", (decimal amount) => RailwaySignals.Red(""ERR"", ""bad""))
        .ServiceStation(""Recovery"", (decimal amount, SignalIssue issue) =>
            new { amount = 1m, status = ""recovered"" });
}";

            var diagnostics = await RunAnalyzerAsync(source);

            Assert.Contains(diagnostics, d => d.Id == "TOP015");
        }

        /// <summary>
        /// Verifies that TOP016 is reported when ServiceStation omits a non-ref input wagon.
        /// </summary>
        [Fact]
        public async Task Analyzer_ReportsTop016_WhenServiceStationOmitsInputWagon()
        {
            const string source = @"
using TrainOP;

public static class RecoveryRoute
{
    public static TrainRoute Build() => new TrainRoute()
        .Station(""Seed"", () => new { paymentId = ""pay-1"", amount = -1m })
        .Station(""Validate"", (string paymentId, decimal amount) => RailwaySignals.Red(""ERR"", ""bad""))
        .ServiceStation(""Recovery"", (string paymentId, decimal amount, SignalIssue issue) =>
            new { amount = 1m });
}";

            var diagnostics = await RunAnalyzerAsync(source);

            Assert.Contains(diagnostics, d => d.Id == "TOP016");
        }

        /// <summary>
        /// Verifies that TOP017 is reported when ServiceStation returns CargoManifest.
        /// </summary>
        [Fact]
        public async Task Analyzer_ReportsTop017_WhenServiceStationReturnsCargoManifest()
        {
            const string source = @"
using TrainOP;

public static class RecoveryRoute
{
    public static TrainRoute Build() => new TrainRoute()
        .Station(""Seed"", () => new { amount = -1m })
        .Station(""Validate"", (decimal amount) => RailwaySignals.Red(""ERR"", ""bad""))
        .ServiceStation(""Recovery"", (decimal amount, SignalIssue issue) =>
            new CargoManifest().LoadWagon(""amount"", 1m));
}";

            var diagnostics = await RunAnalyzerAsync(source);

            Assert.Contains(diagnostics, d => d.Id == "TOP017");
            Assert.DoesNotContain(diagnostics, d => d.Id == "TOP004");
        }

        /// <summary>
        /// Verifies that ServiceStation may update an existing non-input live wagon without TOP015.
        /// </summary>
        [Fact]
        public async Task Analyzer_AllowsServiceStation_UpdatingExistingNonInputWagon()
        {
            const string source = @"
using TrainOP;

public static class RecoveryRoute
{
    public static TrainRoute Build() => new TrainRoute()
        .Station(""Seed"", () => new { paymentId = ""pay-1"", amount = -1m, note = ""keep"" })
        .Station(""Validate"", (string paymentId, decimal amount, string note) =>
            RailwaySignals.Red(""ERR"", ""bad""))
        .ServiceStation(""Recovery"", (decimal amount, SignalIssue issue) =>
            new { amount = 1m, note = ""fixed"" })
        .Station(""After"", (string paymentId, decimal amount, string note) =>
            new { paymentId, amount, note });
}";

            var diagnostics = await RunAnalyzerAsync(source);

            Assert.DoesNotContain(diagnostics, d => d.Id == "TOP015");
            Assert.DoesNotContain(diagnostics, d => d.Id == "TOP016");
            Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        }

        /// <summary>
        /// Verifies that the built-in async ServiceStation escape hatch is not flagged as TOP009.
        /// </summary>
        [Fact]
        public async Task Analyzer_AllowsServiceStation_WithAsyncRedSignalAndCancellationToken()
        {
            const string source = @"
using System.Threading;
using System.Threading.Tasks;
using TrainOP;

public static class AsyncRecoveryRoute
{
    public static TrainRoute Build() => new TrainRoute()
        .Station(""Seed"", () => new { amount = -1m })
        .Station(""Validate"", (decimal amount) => RailwaySignals.Red(""ERR"", ""bad""))
        .ServiceStation(""Recovery"", async (RedSignal red, CargoManifest manifest, CancellationToken token) =>
        {
            await Task.Delay(1, token);
            manifest.LoadWagon(""amount"", 1m);
            return RailwaySignals.White;
        });
}";

            var diagnostics = await RunAnalyzerAsync(source);

            Assert.DoesNotContain(diagnostics, d => d.Id == "TOP009");
            Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        }

        /// <summary>
        /// Verifies that a red return does not flag unreachable stations for removed wagon diagnostics.
        /// </summary>
        [Fact]
        public async Task Analyzer_RedReturn_DoesNotRemoveWagonsForUnreachableStation()
        {
            const string source = @"
using TrainOP;

public static class FailRoute
{
    public static TrainRoute Build() => new TrainRoute()
        .Station(""Seed"", () => new { value = 0 })
        .Station(""Validate"", (int value) =>
            RailwaySignals.Red(""ERR"", ""bad""))
        .Station(""MustNotRun"", (int value) => new { value });
}";

            var diagnostics = await RunAnalyzerAsync(source);

            Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        }

        /// <summary>
        /// Verifies that returning a value tuple with default ItemN names is reported as TOP006.
        /// </summary>
        [Fact]
        public async Task Analyzer_ReportsTop006_WhenHandlerReturnsDefaultItemNTuple()
        {
            const string source = @"
using TrainOP;

public static class DefaultItemNTupleRoute
{
    public static TrainRoute Build() => new TrainRoute()
        .Station(""Seed"", () => new { paymentId = ""pay-1"", amount = 100m })
        .Station(""Discount"", (string paymentId, decimal amount) =>
            (paymentId + ""-disc"", amount * 0.9m));
}";

            var diagnostics = await RunAnalyzerAsync(source);

            var diagnostic = Assert.Single(diagnostics, d => d.Id == "TOP006");
            Assert.Contains("(paymentId + \"-disc\", amount * 0.9m)", GetSourceTextAtLocation(source, diagnostic.Location));
        }

        /// <summary>
        /// Verifies that identifier-inferred tuple names do not report TOP006.
        /// </summary>
        [Fact]
        public async Task Analyzer_DoesNotReportTop006_WhenHandlerReturnsInferredTupleNames()
        {
            const string source = @"
using TrainOP;

public static class InferredTupleRoute
{
    public static TrainRoute Build() => new TrainRoute()
        .Station(""Seed"", () => new { paymentId = ""pay-1"", amount = 100m })
        .Station(""Discount"", (string paymentId, decimal amount) =>
            (paymentId, amount));
}";

            var diagnostics = await RunAnalyzerAsync(source);

            Assert.DoesNotContain(diagnostics, d => d.Id == "TOP006");
        }

        /// <summary>
        /// Verifies that returning a named value tuple from a handler does not report TOP006.
        /// </summary>
        [Fact]
        public async Task Analyzer_DoesNotReportTop006_WhenHandlerReturnsNamedTuple()
        {
            const string source = @"
using TrainOP;

public static class NamedTupleRoute
{
    public static TrainRoute Build() => new TrainRoute()
        .Station(""Seed"", () => new { paymentId = ""pay-1"", amount = 100m })
        .Station(""Discount"", (string paymentId, decimal amount) =>
            (paymentId: paymentId + ""-disc"", amount: amount * 0.9m));
}";

            var diagnostics = await RunAnalyzerAsync(source);

            Assert.DoesNotContain(diagnostics, d => d.Id == "TOP006");
        }

        /// <summary>
        /// Verifies that explicit Item1:/Item2: names do not report TOP006.
        /// </summary>
        [Fact]
        public async Task Analyzer_DoesNotReportTop006_WhenHandlerReturnsExplicitItemNNames()
        {
            const string source = @"
using TrainOP;

public static class ExplicitItemNRoute
{
    public static TrainRoute Build() => new TrainRoute()
        .Station(""Seed"", () => new { paymentId = ""pay-1"", amount = 100m })
        .Station(""Discount"", (string paymentId, decimal amount) =>
            (Item1: paymentId + ""-disc"", Item2: amount * 0.9m));
}";

            var diagnostics = await RunAnalyzerAsync(source);

            Assert.DoesNotContain(diagnostics, d => d.Id == "TOP006");
        }

        /// <summary>
        /// Verifies that a mixed literal with one default ItemN element reports TOP006.
        /// </summary>
        [Fact]
        public async Task Analyzer_ReportsTop006_WhenHandlerReturnsTupleWithDefaultItemNElement()
        {
            const string source = @"
using TrainOP;

public static class PartialDefaultItemNRoute
{
    public static TrainRoute Build() => new TrainRoute()
        .Station(""Seed"", () => new { paymentId = ""pay-1"", amount = 100m })
        .Station(""Discount"", (string paymentId, decimal amount) =>
            (paymentId + ""-disc"", amount: amount * 0.9m));
}";

            var diagnostics = await RunAnalyzerAsync(source);

            var diagnostic = Assert.Single(diagnostics, d => d.Id == "TOP006");
            Assert.Contains("(paymentId + \"-disc\", amount: amount * 0.9m)", GetSourceTextAtLocation(source, diagnostic.Location));
        }

        /// <summary>
        /// Verifies that returning a runtime RedSignal from a handler is reported as TOP010.
        /// </summary>
        [Fact]
        public async Task Analyzer_ReportsTop010_WhenHandlerReturnsRuntimeSignal()
        {
            const string source = @"
using TrainOP;

public static class RuntimeSignalRoute
{
    public static TrainRoute Build() => new TrainRoute()
        .Station(""Seed"", () => new { value = 1 })
        .Station(""Bad"", (int value) =>
            new RedSignal(new SignalIssue(""ERR"", ""fail"", ""Bad"")));
}";

            var diagnostics = await RunAnalyzerAsync(source);

            Assert.Contains(diagnostics, d => d.Id == "TOP010");
        }

        /// <summary>
        /// Verifies that method-group handlers in the current compilation validate like lambdas.
        /// </summary>
        [Fact]
        public async Task Analyzer_AcceptsStaticMethodGroupHandler()
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

            var diagnostics = await RunAnalyzerAsync(source);

            Assert.DoesNotContain(diagnostics, d => d.Id == "TOP009");
            Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        }

        /// <summary>
        /// Verifies that anonymous-method handlers are accepted.
        /// </summary>
        [Fact]
        public async Task Analyzer_AcceptsAnonymousMethodHandler()
        {
            const string source = @"
using TrainOP;

public static class AnonymousRoute
{
    public static TrainRoute Build() => new TrainRoute()
        .Station(""Seed"", () => new { paymentId = ""pay-1"", amount = 100m })
        .Station(""Discount"", delegate(string paymentId, decimal amount)
        {
            return new { paymentId, amount = amount * 0.9m };
        });
}";

            var diagnostics = await RunAnalyzerAsync(source);

            Assert.DoesNotContain(diagnostics, d => d.Id == "TOP009");
            Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        }

        /// <summary>
        /// Verifies that a Func variable handler is rejected with TOP009.
        /// </summary>
        [Fact]
        public async Task Analyzer_ReportsTop009_ForFuncVariableHandler()
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

            var diagnostics = await RunAnalyzerAsync(source);

            Assert.Contains(diagnostics, d => d.Id == "TOP009");
        }

        /// <summary>
        /// Verifies that an ambiguous method-group handler is rejected with TOP009.
        /// </summary>
        [Fact]
        public async Task Analyzer_ReportsTop009_ForAmbiguousMethodGroup()
        {
            const string source = @"
using TrainOP;

public static class AmbiguousRoute
{
    public static TrainRoute Build() => new TrainRoute()
        .Station(""Seed"", () => new { paymentId = ""pay-1"", amount = 100m })
        .Station(""Discount"", Discount);

    private static object Discount(string paymentId, decimal amount) =>
        new { paymentId, amount = amount * 0.9m };

    private static object Discount(string paymentId) =>
        new { paymentId, amount = 0m };
}";

            var diagnostics = await RunAnalyzerAsync(source);

            Assert.Contains(diagnostics, d => d.Id == "TOP009");
        }

        internal static async Task<ImmutableArray<Diagnostic>> RunAnalyzerAsync(string source)
        {
            var syntaxTree = CSharpSyntaxTree.ParseText(source);
            var compilation = CSharpCompilation.Create(
                "AnalyzerTests",
                new[] { syntaxTree },
                GetMetadataReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            var analyzers = ImmutableArray.Create<DiagnosticAnalyzer>(new TrainRouteValidationAnalyzer());
            var compilationWithAnalyzers = compilation.WithAnalyzers(analyzers);
            return await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync();
        }

        private static string GetSourceTextAtLocation(string source, Location location)
        {
            var span = location.SourceSpan;
            return source.Substring(span.Start, span.Length);
        }

        internal static MetadataReference[] GetMetadataReferences()
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
