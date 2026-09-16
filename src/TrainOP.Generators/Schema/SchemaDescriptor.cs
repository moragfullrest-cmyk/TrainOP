using Microsoft.CodeAnalysis;
using System;
using System.Collections.Immutable;
using TrainOP.Generators.Wagons;

namespace TrainOP.Generators
{
    /// <summary>
    /// Stage 6 IR: data-oriented export descriptor for one public route factory schema.
    /// Holds attribute payload for <c>RouteSchemas.g.cs</c> without <c>StringBuilder</c> / <c>AddSource</c>.
    /// </summary>
    internal sealed class SchemaDescriptor
    {
        /// <summary>
        /// Creates an export descriptor from a validated factory and its terminal set.
        /// </summary>
        public SchemaDescriptor(
            IMethodSymbol method,
            TerminalSet terminals,
            string callerChainKey,
            int stationCount)
        {
            Method = method ?? throw new ArgumentNullException(nameof(method));
            Terminals = terminals ?? TerminalSet.Empty(TerminalSet.Origin.FactoryPath);
            CallerChainKey = callerChainKey ?? string.Empty;
            StationCount = stationCount < 0 ? 0 : stationCount;
            OwnerTypeDisplay = Method.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            MethodName = Method.Name;
            SchemaTypeName = BuildSchemaTypeName(Method);
        }

        /// <summary>Exported factory method symbol.</summary>
        public IMethodSymbol Method { get; }

        /// <summary>
        /// Factory terminal wagons tagged <see cref="TerminalSet.Origin.FactoryPath"/>.
        /// </summary>
        public TerminalSet Terminals { get; }

        /// <summary>
        /// Terminal wagon bindings for emit (empty when <see cref="TerminalSet.HasUnknownReturn"/>).
        /// </summary>
        public ImmutableArray<WagonBinding> TerminalWagons => TerminalSetAdapters.ToWagons(Terminals);

        /// <summary>Fully qualified owner type for <c>typeof(...)</c> in <c>RouteSchemaFor</c>.</summary>
        public string OwnerTypeDisplay { get; }

        /// <summary>Factory method name for <c>RouteSchemaFor</c>.</summary>
        public string MethodName { get; }

        /// <summary>Generated holder type name (<c>Owner_Method_Schema</c>).</summary>
        public string SchemaTypeName { get; }

        /// <summary>Caller chain key for the factory's <c>new TrainRoute()</c> site.</summary>
        public string CallerChainKey { get; }

        /// <summary>Number of Station/ServiceStation registrations inside the factory.</summary>
        public int StationCount { get; }

        /// <summary>
        /// True when descriptor carries dispatch identity (<c>CallerChainKey</c> / <c>StationCount</c>).
        /// </summary>
        public bool HasDispatchIdentity => !string.IsNullOrEmpty(CallerChainKey);

        /// <summary>
        /// Builds the generated schema type name for a factory method.
        /// </summary>
        internal static string BuildSchemaTypeName(IMethodSymbol methodSymbol)
        {
            if (methodSymbol == null)
            {
                return "_Schema";
            }

            var typeName = methodSymbol.ContainingType?.Name ?? string.Empty;
            var methodName = methodSymbol.Name;
            if (typeName.Length == 0)
            {
                return methodName + "_Schema";
            }

            return typeName + "_" + methodName + "_Schema";
        }
    }

    /// <summary>
    /// One <c>[RouteSchemaWagon]</c> slot as pure emit data (name + type display).
    /// </summary>
    internal readonly struct SchemaWagonDescriptor
    {
        /// <summary>
        /// Creates a wagon emit slot from a binding.
        /// </summary>
        public SchemaWagonDescriptor(string name, string typeDisplay)
        {
            Name = name ?? string.Empty;
            TypeDisplay = typeDisplay ?? string.Empty;
        }

        /// <summary>Wagon key written into <c>RouteSchemaWagon</c>.</summary>
        public string Name { get; }

        /// <summary>Fully qualified type display for <c>typeof(...)</c>.</summary>
        public string TypeDisplay { get; }

        /// <summary>
        /// Adapts a <see cref="WagonBinding"/> into an emit-only wagon slot.
        /// </summary>
        public static SchemaWagonDescriptor FromBinding(WagonBinding binding)
        {
            if (binding == null)
            {
                return new SchemaWagonDescriptor(string.Empty, string.Empty);
            }

            return new SchemaWagonDescriptor(binding.Name, binding.TypeDisplay);
        }
    }
}
