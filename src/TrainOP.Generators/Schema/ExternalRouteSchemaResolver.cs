using Microsoft.CodeAnalysis;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using TrainOP.Generators.Wagons;
namespace TrainOP.Generators
{
    /// <summary>
    /// Resolves exported route schemas from generated types in referenced assemblies.
    /// </summary>
    internal static class ExternalRouteSchemaResolver
    {
        private const string RouteSchemaForAttributeName = "RouteSchemaForAttribute";
        private const string RouteSchemaWagonAttributeName = "RouteSchemaWagonAttribute";
        private const string CallerChainKeyNamedArg = "CallerChainKey";
        private const string StationCountNamedArg = "StationCount";

        /// <summary>
        /// Attempts to resolve exported schema metadata for a factory method.
        /// </summary>
        public static bool TryResolve(
            IMethodSymbol factoryMethod,
            Compilation compilation,
            out ExternalRouteSchema schema)
        {
            schema = null;
            if (factoryMethod == null || compilation == null)
            {
                return false;
            }

            foreach (var schemaType in EnumerateSchemaTypes(compilation))
            {
                if (!TryGetRouteSchemaForTarget(
                    schemaType,
                    out var ownerType,
                    out var methodName,
                    out var callerChainKey,
                    out var stationCount))
                {
                    continue;
                }

                if (!SymbolEqualityComparer.Default.Equals(ownerType, factoryMethod.ContainingType)
                    || !string.Equals(methodName, factoryMethod.Name, StringComparison.Ordinal))
                {
                    continue;
                }

                var terminalWagons = ReadTerminalWagons(schemaType);
                if (terminalWagons.IsDefaultOrEmpty)
                {
                    return false;
                }

                schema = new ExternalRouteSchema(terminalWagons, callerChainKey, stationCount);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Attempts to resolve terminal wagons for a factory method from an exported schema.
        /// </summary>
        public static bool TryResolve(
            IMethodSymbol factoryMethod,
            Compilation compilation,
            out ImmutableArray<WagonBinding> terminalWagons)
        {
            terminalWagons = ImmutableArray<WagonBinding>.Empty;
            if (!TryResolve(factoryMethod, compilation, out ExternalRouteSchema schema))
            {
                return false;
            }

            terminalWagons = schema.TerminalWagons;
            return !terminalWagons.IsDefaultOrEmpty;
        }

        private static ImmutableArray<WagonBinding> ReadTerminalWagons(INamedTypeSymbol schemaType)
        {
            var builder = ImmutableArray.CreateBuilder<WagonBinding>();
            foreach (var attribute in schemaType.GetAttributes())
            {
                if (!string.Equals(attribute.AttributeClass?.Name, RouteSchemaWagonAttributeName, StringComparison.Ordinal))
                {
                    continue;
                }

                if (attribute.ConstructorArguments.Length < 2)
                {
                    continue;
                }

                var name = attribute.ConstructorArguments[0].Value as string;
                if (string.IsNullOrEmpty(name))
                {
                    continue;
                }

                var typeSymbol = attribute.ConstructorArguments[1].Value as ITypeSymbol;
                if (typeSymbol == null)
                {
                    continue;
                }

                builder.Add(new WagonBinding(
                    name,
                    typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    typeSymbol,
                    schemaType.Locations.FirstOrDefault() ?? Location.None));
            }

            return TerminalWagonsComparer.Normalize(builder.ToImmutable());
        }

        private static bool TryGetRouteSchemaForTarget(
            INamedTypeSymbol schemaType,
            out INamedTypeSymbol ownerType,
            out string methodName,
            out string callerChainKey,
            out int stationCount)
        {
            ownerType = null;
            methodName = null;
            callerChainKey = string.Empty;
            stationCount = 0;

            foreach (var attribute in schemaType.GetAttributes())
            {
                if (!string.Equals(attribute.AttributeClass?.Name, RouteSchemaForAttributeName, StringComparison.Ordinal))
                {
                    continue;
                }

                if (attribute.ConstructorArguments.Length < 2)
                {
                    continue;
                }

                ownerType = attribute.ConstructorArguments[0].Value as INamedTypeSymbol;
                methodName = attribute.ConstructorArguments[1].Value as string;
                callerChainKey = ReadNamedString(attribute, CallerChainKeyNamedArg);
                stationCount = ReadNamedInt(attribute, StationCountNamedArg);
                return ownerType != null && !string.IsNullOrEmpty(methodName);
            }

            return false;
        }

        private static string ReadNamedString(AttributeData attribute, string name)
        {
            foreach (var argument in attribute.NamedArguments)
            {
                if (string.Equals(argument.Key, name, StringComparison.Ordinal))
                {
                    return argument.Value.Value as string ?? string.Empty;
                }
            }

            return string.Empty;
        }

        private static int ReadNamedInt(AttributeData attribute, string name)
        {
            foreach (var argument in attribute.NamedArguments)
            {
                if (!string.Equals(argument.Key, name, StringComparison.Ordinal))
                {
                    continue;
                }

                if (argument.Value.Value is int intValue)
                {
                    return intValue;
                }

                if (argument.Value.Value is long longValue)
                {
                    return (int)longValue;
                }
            }

            return 0;
        }

        private static IEnumerable<INamedTypeSymbol> EnumerateSchemaTypes(Compilation compilation)
        {
            foreach (var assembly in EnumerateAssemblies(compilation.Assembly, compilation))
            {
                foreach (var type in GetAllTypes(assembly.GlobalNamespace))
                {
                    if (type.TypeKind == TypeKind.Class
                        && type.GetAttributes().Any(a =>
                            string.Equals(a.AttributeClass?.Name, RouteSchemaForAttributeName, StringComparison.Ordinal)))
                    {
                        yield return type;
                    }
                }
            }
        }

        private static IEnumerable<IAssemblySymbol> EnumerateAssemblies(IAssemblySymbol rootAssembly, Compilation compilation)
        {
            var seen = new HashSet<IAssemblySymbol>(SymbolEqualityComparer.Default);
            if (rootAssembly != null && seen.Add(rootAssembly))
            {
                yield return rootAssembly;
            }

            foreach (var reference in compilation.References)
            {
                if (compilation.GetAssemblyOrModuleSymbol(reference) is IAssemblySymbol referenced
                    && seen.Add(referenced))
                {
                    yield return referenced;
                }
            }
        }

        private static IEnumerable<INamedTypeSymbol> GetAllTypes(INamespaceSymbol namespaceSymbol)
        {
            if (namespaceSymbol == null)
            {
                yield break;
            }

            foreach (var member in namespaceSymbol.GetMembers())
            {
                if (member is INamespaceSymbol nestedNamespace)
                {
                    foreach (var type in GetAllTypes(nestedNamespace))
                    {
                        yield return type;
                    }
                }
                else if (member is INamedTypeSymbol type)
                {
                    yield return type;
                    foreach (var nested in GetAllTypes(type))
                    {
                        yield return nested;
                    }
                }
            }
        }

        private static IEnumerable<INamedTypeSymbol> GetAllTypes(INamedTypeSymbol typeSymbol)
        {
            foreach (var nested in typeSymbol.GetTypeMembers())
            {
                yield return nested;
                foreach (var deeper in GetAllTypes(nested))
                {
                    yield return deeper;
                }
            }
        }
    }
}
