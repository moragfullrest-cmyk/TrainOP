using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Generic;
using System.Collections.Immutable;
using TrainOP.Generators.Route;

namespace TrainOP.Generators.Parts
{
    /// <summary>
    /// Connects a <see cref="FactoryCall"/> to its <see cref="ExtensionTail"/> via Extend,
    /// producing a <see cref="RouteChain"/>.
    /// </summary>
    internal static class ExtensionChainConnector
    {
        /// <summary>
        /// Materializes the extension tail after <paramref name="factoryCall"/> and records an Extend edge.
        /// </summary>
        public static bool TryConnect(
            FactoryCall factoryCall,
            SemanticModel semanticModel,
            IReadOnlyDictionary<string, StationLink> stationByKey,
            out ChainConstructor constructor,
            out RouteChain chain)
        {
            constructor = null;
            chain = null;

            if (!ExtensionTailMaterializer.TryMaterialize(
                    factoryCall,
                    semanticModel,
                    stationByKey,
                    out var tail))
            {
                return false;
            }

            return TryBuild(factoryCall, tail, out constructor, out chain);
        }

        /// <summary>
        /// Overload without prebuilt station sites.
        /// </summary>
        public static bool TryConnect(
            FactoryCall factoryCall,
            SemanticModel semanticModel,
            out ChainConstructor constructor,
            out RouteChain chain)
        {
            return TryConnect(factoryCall, semanticModel, stationByKey: null, out constructor, out chain);
        }

        /// <summary>
        /// Builds a factory-extension chain ending at <paramref name="endpoint"/>.
        /// </summary>
        public static bool TryConnectEndingAt(
            ExpressionSyntax endpoint,
            SemanticModel semanticModel,
            Compilation compilation,
            out RouteChain chain,
            out ImmutableArray<Diagnostic> diagnostics)
        {
            chain = null;
            diagnostics = ImmutableArray<Diagnostic>.Empty;

            if (!RouteChainRootResolver.TryFindChainRootEndingAt(
                    endpoint,
                    semanticModel,
                    out var origin))
            {
                return false;
            }

            var factoryCall = origin switch
            {
                FactoryCall direct => direct,
                LocalBinding { Origin: FactoryCall nested } => nested,
                _ => null
            };
            if (factoryCall == null
                && RouteOriginPorts.TryGetRoot(origin, out var root)
                && !FactoryCallMaterializer.TryMaterialize(root, semanticModel, out factoryCall))
            {
                return false;
            }

            if (factoryCall == null)
            {
                return false;
            }

            if (!ExtensionTailMaterializer.TryMaterialize(
                    factoryCall,
                    semanticModel,
                    stationByKey: null,
                    endpoint,
                    allowEmpty: true,
                    out var tail))
            {
                return false;
            }

            if (!TryBuild(factoryCall, tail, out _, out chain))
            {
                return false;
            }

            if (factoryCall.FactoryMethod != null && compilation != null)
            {
                RouteFactoryResolver.TryResolve(
                    factoryCall.FactoryMethod,
                    compilation,
                    factoryCall.Location,
                    out _,
                    out diagnostics);
            }

            return true;
        }

        private static bool TryBuild(
            FactoryCall factoryCall,
            ExtensionTail tail,
            out ChainConstructor constructor,
            out RouteChain chain)
        {
            constructor = null;
            chain = null;

            constructor = new ChainConstructor();
            if (!constructor.TryExtend(factoryCall, tail, out _))
            {
                return false;
            }

            return RouteOriginPorts.TryToRouteChain(constructor, out chain);
        }
    }
}
