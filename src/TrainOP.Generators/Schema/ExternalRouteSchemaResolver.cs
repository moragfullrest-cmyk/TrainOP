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

        /// <summary>
        /// Attempts to resolve terminal wagons for a factory method from an exported schema.
        /// </summary>
        public static bool TryResolve(
            IMethodSymbol factoryMethod,
            Compilation compilation,
            out ImmutableArray<WagonBinding> terminalWagons)
        {
            terminalWagons = ImmutableArray<WagonBinding>.Empty;
            if (factoryMethod == null || compilation == null)
            {
                return false;
            }

            foreach (var schemaType in EnumerateSchemaTypes(compilation))
            {
                if (!TryGetRouteSchemaForTarget(schemaType, out var ownerType, out var methodName))
                {
                    continue;
                }

                if (!SymbolEqualityComparer.Default.Equals(ownerType, factoryMethod.ContainingType)
                    || !string.Equals(methodName, factoryMethod.Name, StringComparison.Ordinal))
                {
                    continue;
                }

                terminalWagons = ReadTerminalWagons(schemaType);
                return !terminalWagons.IsDefaultOrEmpty;
            }

            return false;
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
            out string methodName)
        {
            ownerType = null;
            methodName = null;

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
                return ownerType != null && !string.IsNullOrEmpty(methodName);
            }

            return false;
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
