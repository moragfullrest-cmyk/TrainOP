using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace TrainOP.Generators
{
    /// <summary>
    /// Discovers exported route factories and builds <see cref="SchemaDescriptor"/> IR.
    /// TOP012/013 are produced here once; <see cref="TrainRouteValidationAnalyzer"/> reports them.
    /// The generator keeps descriptors and does not report the same diagnostics.
    /// </summary>
    internal static class SchemaDescriptorsStage
    {
        /// <summary>
        /// Result of collecting schema export descriptors and related diagnostics.
        /// </summary>
        internal sealed class CollectResult
        {
            public CollectResult(
                ImmutableArray<SchemaDescriptor> descriptors,
                ImmutableArray<Diagnostic> diagnostics)
            {
                Descriptors = descriptors.IsDefault
                    ? ImmutableArray<SchemaDescriptor>.Empty
                    : descriptors;
                Diagnostics = diagnostics.IsDefault
                    ? ImmutableArray<Diagnostic>.Empty
                    : diagnostics;
            }

            /// <summary>Valid export descriptors ready for emit / IR.</summary>
            public ImmutableArray<SchemaDescriptor> Descriptors { get; }

            /// <summary>Factory path validation diagnostics (reported even when no descriptor).</summary>
            public ImmutableArray<Diagnostic> Diagnostics { get; }
        }

        /// <summary>
        /// Discovers public route factories in <paramref name="compilation"/> and builds export descriptors.
        /// </summary>
        public static CollectResult Collect(Compilation compilation)
        {
            if (compilation == null)
            {
                return new CollectResult(
                    ImmutableArray<SchemaDescriptor>.Empty,
                    ImmutableArray<Diagnostic>.Empty);
            }

            var processed = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);
            var descriptors = ImmutableArray.CreateBuilder<SchemaDescriptor>();
            var diagnostics = ImmutableArray.CreateBuilder<Diagnostic>();

            foreach (var syntaxTree in compilation.SyntaxTrees)
            {
                if (syntaxTree.FilePath.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var semanticModel = compilation.GetSemanticModel(syntaxTree);
                foreach (var node in syntaxTree.GetRoot().DescendantNodes())
                {
                    if (node is not MethodDeclarationSyntax methodDeclaration)
                    {
                        continue;
                    }

                    var methodSymbol = semanticModel.GetDeclaredSymbol(methodDeclaration) as IMethodSymbol;
                    if (methodSymbol == null
                        || !FactoryAccessibilityHelper.IsExportedFactoryContract(methodSymbol)
                        || !StationSyntaxHelper.IsTrainRoute(methodSymbol.ReturnType)
                        || !processed.Add(methodSymbol))
                    {
                        continue;
                    }

                    var validation = RouteFactoryPathValidator.Validate(methodSymbol, compilation);
                    diagnostics.AddRange(validation.Diagnostics);

                    if (!validation.IsValid)
                    {
                        continue;
                    }

                    FactoryDispatchMetadata.TryResolveFromBody(
                        methodSymbol,
                        compilation,
                        out var callerChainKey,
                        out var stationCount);

                    // Cluster D soft coupling: factory terminals enter IR as TerminalSet(FactoryPath).
                    var terminals = new TerminalSet(
                        validation.TerminalWagons,
                        TerminalSet.Origin.FactoryPath,
                        hasUnknownReturn: false);

                    descriptors.Add(new SchemaDescriptor(
                        methodSymbol,
                        terminals,
                        callerChainKey,
                        stationCount));
                }
            }

            return new CollectResult(descriptors.ToImmutable(), diagnostics.ToImmutable());
        }
    }
}
