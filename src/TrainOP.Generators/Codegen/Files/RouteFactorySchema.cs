using System.Collections.Immutable;
using TrainOP.Generators.Wagons;

namespace TrainOP.Generators
{
    /// <summary>
    /// Emits one exported route factory schema type from a <see cref="SchemaDescriptor"/>.
    /// </summary>
    internal sealed class RouteFactorySchema
    {
        /// <summary>
        /// Creates a route factory schema emitter from an export descriptor.
        /// </summary>
        public RouteFactorySchema(SchemaDescriptor descriptor)
        {
            Descriptor = descriptor
                ?? throw new System.ArgumentNullException(nameof(descriptor));
        }

        /// <summary>Export descriptor backing this emit unit.</summary>
        public SchemaDescriptor Descriptor { get; }

        /// <summary>Terminal wagon slots from the descriptor.</summary>
        public ImmutableArray<WagonBinding> TerminalWagons => Descriptor.TerminalWagons;

        /// <summary>Caller chain key for the factory's <c>new TrainRoute()</c> site.</summary>
        public string CallerChainKey => Descriptor.CallerChainKey;

        /// <summary>Number of Station/ServiceStation registrations inside the factory.</summary>
        public int StationCount => Descriptor.StationCount;

        /// <summary>
        /// Emits schema attributes on a generated holder type.
        /// </summary>
        internal void Emit(CodegenWriter writer)
        {
            var descriptor = Descriptor;
            writer.AppendIndented("[RouteSchemaFor(typeof(")
                .Append(descriptor.OwnerTypeDisplay)
                .Append("), \"")
                .Append(descriptor.MethodName)
                .Append("\"");

            if (descriptor.HasDispatchIdentity)
            {
                writer.Append(", CallerChainKey = \"")
                    .Append(StringHelpers.Escape(descriptor.CallerChainKey))
                    .Append("\", StationCount = ")
                    .Append(descriptor.StationCount);
            }

            writer.Append(")]");
            writer.EndLine();

            var wagons = descriptor.TerminalWagons;
            for (var i = 0; i < wagons.Length; i++)
            {
                var wagon = SchemaWagonDescriptor.FromBinding(wagons[i]);
                writer.AppendIndented("[RouteSchemaWagon(\"")
                    .Append(StringHelpers.Escape(wagon.Name) ?? string.Empty)
                    .Append("\", typeof(")
                    .Append(wagon.TypeDisplay)
                    .Append("))]");
                writer.EndLine();
            }

            writer.AppendIndented("internal static class ")
                .Append(descriptor.SchemaTypeName)
                .Append(" { }");
            writer.EndLine();
            writer.AppendLine();
        }

        /// <summary>
        /// Builds the generated schema type name for a factory method.
        /// </summary>
        internal static string BuildSchemaTypeName(Microsoft.CodeAnalysis.IMethodSymbol methodSymbol)
        {
            return SchemaDescriptor.BuildSchemaTypeName(methodSymbol);
        }
    }
}
